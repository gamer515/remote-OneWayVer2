using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OneWay.Decision.DirectLlm
{
    public sealed class DirectLlmAttempt
    {
        public DirectLlmProvider Provider { get; internal set; }
        public string Code { get; internal set; }
        public long HttpStatus { get; internal set; }
        public double Seconds { get; internal set; }
    }

    public sealed class DirectLlmResult
    {
        public DirectLlmProvider Provider { get; internal set; }
        public string Model { get; internal set; }
        public string CardsJson { get; internal set; }
        public string Text { get; internal set; }
        public string RawText { get; internal set; }
        public int InputTokens { get; internal set; } = -1;
        public int OutputTokens { get; internal set; } = -1;
        public double TotalSeconds { get; internal set; }
        public IReadOnlyList<DirectLlmAttempt> Attempts { get; internal set; }
    }

    public sealed class DirectLlmGenerationException : Exception
    {
        public IReadOnlyList<DirectLlmAttempt> Attempts { get; }
        internal DirectLlmGenerationException(List<DirectLlmAttempt> attempts)
            : base("Direct LLM failed: " + string.Join(" / ", attempts.Select(a => a.Provider + ":" + a.Code + " (HTTP " + a.HttpStatus + ")")))
        { Attempts = attempts.AsReadOnly(); }
    }

    internal sealed class DirectLlmFailure : Exception
    {
        internal DirectLlmFailure(string code) : base(code) { }
    }

    /// <summary>One request at a time per instance. No scene hooks, storage writes or automatic retries.</summary>
    public sealed class DirectLlmClient
    {
        private readonly DirectLlmEndpoint[] endpoints;
        private readonly int timeoutSeconds;
        private readonly bool roundRobin;
        private readonly IDirectLlmTransport transport;
        private readonly Func<DirectLlmProvider, DirectLlmCredentials> credentials;
        private int next, busy;
        public bool IsBusy => Volatile.Read(ref busy) != 0;
        public string Identity { get; }

        public DirectLlmClient(DirectLlmOptions options, IDirectLlmTransport transport = null,
            Func<DirectLlmProvider, DirectLlmCredentials> credentials = null)
        {
            if (options?.endpoints == null || options.timeoutSeconds < 3 || options.timeoutSeconds > 120)
                throw new ArgumentException("Direct LLM options require endpoints and a 3–120 second timeout.");
            if (options.endpoints.Any(e => e == null || !Enum.IsDefined(typeof(DirectLlmProvider), e.provider)) ||
                options.endpoints.GroupBy(e => e.provider).Any(g => g.Count() != 1))
                throw new ArgumentException("Each provider may occur only once.");
            endpoints = options.endpoints.Where(e => e.enabled).Select(e => new DirectLlmEndpoint(e.provider, e.model)).ToArray();
            if (endpoints.Length == 0) throw new ArgumentException("Enable at least one provider.");
            timeoutSeconds = options.timeoutSeconds; roundRobin = options.roundRobin;
            this.transport = transport ?? new UnityDirectLlmTransport();
            this.credentials = credentials ?? DirectLlmCredentials.FromEnvironment;
            // Configuration snapshot: changing an Inspector asset requires creating a new client.
            Identity = "DirectHTTP-v1|" + string.Join("|", endpoints.Select(e => e.provider + ":" + e.model)) +
                "|roundRobin=" + roundRobin + "|timeout=" + timeoutSeconds;
        }

        public Task<DirectLlmResult> GenerateAsync(string messages, int maxTokens, int seed,
            CancellationToken token, Func<string, bool> acceptCards = null)
        {
            if (maxTokens < 1 || maxTokens > 8192) throw new ArgumentOutOfRangeException(nameof(maxTokens));
            return Generate(messages, maxTokens, seed, token,
                acceptCards == null ? null : json => acceptCards(json) ? null : "GameValidationFailed");
        }

        public Task<DirectLlmResult> GenerateDialogueAsync(string messages, CancellationToken token,
            Func<string, string> rejectDialogue = null) => Generate(messages, null, 0, token, rejectDialogue);

        private async Task<DirectLlmResult> Generate(string messages, int? maxTokens, int seed,
            CancellationToken token, Func<string, string> rejectOutput)
        {
            token.ThrowIfCancellationRequested();
            var parsed = DirectLlmProtocol.Messages(messages);
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
                throw new InvalidOperationException("This DirectLlmClient is already generating. Reuse it sequentially or create another client.");
            int start = next;
            next = roundRobin ? (next + 1) % endpoints.Length : 0;
            var attempts = new List<DirectLlmAttempt>();
            var total = Stopwatch.StartNew();
            try
            {
                for (int i = 0; i < endpoints.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var endpoint = endpoints[(start + i) % endpoints.Length];
                    var attempt = new DirectLlmAttempt { Provider = endpoint.provider };
                    attempts.Add(attempt);
                    var watch = Stopwatch.StartNew();
                    try
                    {
                        var request = DirectLlmProtocol.Request(endpoint, credentials(endpoint.provider), parsed, maxTokens, seed, timeoutSeconds);
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                        deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                        var response = await transport.SendAsync(request, deadline.Token);
                        token.ThrowIfCancellationRequested();
                        deadline.Token.ThrowIfCancellationRequested();
                        if (response == null) throw new DirectLlmFailure("EmptyHttpResponse");
                        attempt.HttpStatus = response.StatusCode;
                        if (!response.TransportSucceeded || response.StatusCode < 200 || response.StatusCode >= 300)
                            throw new DirectLlmFailure("HttpOrNetworkError");
                        var result = DirectLlmProtocol.Response(endpoint.provider, response.Body, maxTokens.HasValue);
                        string rejection = rejectOutput?.Invoke(maxTokens.HasValue ? result.CardsJson : result.Text);
                        if (rejection != null) throw new DirectLlmFailure(rejection);
                        token.ThrowIfCancellationRequested();
                        attempt.Code = "Success";
                        attempt.Seconds = watch.Elapsed.TotalSeconds;
                        result.Model = string.IsNullOrWhiteSpace(result.Model) ? endpoint.model : result.Model;
                        result.TotalSeconds = total.Elapsed.TotalSeconds;
                        result.Attempts = attempts.AsReadOnly();
                        return result;
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (OperationCanceledException) { attempt.Code = "Timeout"; }
                    catch (DirectLlmFailure e) { attempt.Code = e.Message; }
                    // No raw server body, key, URL, prompt or exception message is retained in diagnostics.
                    catch (Exception) { attempt.Code = "ConfigurationOrTransportError"; }
                    finally { attempt.Seconds = watch.Elapsed.TotalSeconds; }
                }
                throw new DirectLlmGenerationException(attempts);
            }
            finally { Volatile.Write(ref busy, 0); }
        }
    }
}
