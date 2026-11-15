using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace OneWay.Decision.DirectLlm
{
    public sealed class DirectLlmHttpRequest
    {
        public DirectLlmProvider Provider { get; }
        public string Url { get; }
        public string HeaderName { get; }
        public string HeaderValue { get; }
        public string Body { get; }
        public int TimeoutSeconds { get; }
        internal DirectLlmHttpRequest(DirectLlmProvider provider, string url, string headerName, string headerValue, string body, int timeoutSeconds)
        { Provider = provider; Url = url; HeaderName = headerName; HeaderValue = headerValue; Body = body; TimeoutSeconds = timeoutSeconds; }
    }

    public sealed class DirectLlmHttpResponse
    {
        public long StatusCode;
        public bool TransportSucceeded;
        public string Body;
    }

    /// <summary>Inject a fake transport for tests. Implementations must honor cancellation and timeout.</summary>
    public interface IDirectLlmTransport
    {
        Task<DirectLlmHttpResponse> SendAsync(DirectLlmHttpRequest request, CancellationToken token);
    }

    public sealed class UnityDirectLlmTransport : IDirectLlmTransport
    {
        // Call from Unity's main thread. Do not use Task.Run around this method.
        public async Task<DirectLlmHttpResponse> SendAsync(DirectLlmHttpRequest input, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (SynchronizationContext.Current == null) throw new InvalidOperationException("Direct LLM requires Unity's main thread.");
            using var request = new UnityWebRequest(input.Url, "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(input.Body)),
                downloadHandler = new DownloadHandlerBuffer(), timeout = input.TimeoutSeconds, redirectLimit = 0
            };
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader(input.HeaderName, input.HeaderValue);
            try
            {
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    token.ThrowIfCancellationRequested();
                    await Task.Yield();
                }
                token.ThrowIfCancellationRequested();
                return new DirectLlmHttpResponse { StatusCode = request.responseCode,
                    TransportSucceeded = request.result == UnityWebRequest.Result.Success, Body = request.downloadHandler.text };
            }
            finally
            {
                // Abort on Unity's thread, not in a CancellationToken callback on an arbitrary worker.
                if (!request.isDone) request.Abort();
            }
        }
    }
}
