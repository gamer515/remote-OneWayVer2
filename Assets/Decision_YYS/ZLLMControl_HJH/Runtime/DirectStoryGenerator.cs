using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OneWay.Decision.DirectLlm
{
    /// <summary>Explicit adapter for LocalStoryDialogueQueue.Process's generate delegate. Never auto-registers.</summary>
    public sealed class DirectStoryGenerator
    {
        private readonly DirectLlmClient client;
        private int busy;
        public DirectStoryGenerator(DirectLlmClient client) => this.client = client ?? throw new ArgumentNullException(nameof(client));

        public async Task GenerateRuntimeAsync(LocalStoryTestReport report, CancellationToken token)
        {
            if (report?.original?.MainStory == null) throw new ArgumentException("A populated LocalStoryTestReport is required.");
            token.ThrowIfCancellationRequested();
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) throw new InvalidOperationException("This generator is already processing a story.");
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            LocalStoryLifetime.Register(lifetime);
            try
            {
                // The current ModelFor fallback uses the pass-through Qwen *message formatter* for this label.
                // This adapter never calls LocalStoryRuntimePipeline.Infer or loads a local model.
                report.model = "Direct HTTP";
                report.generationIdentity = client.Identity;
                report.runtimePath = "HTTPS / provider APIs";
                await LocalStorySections.Generate(report, lifetime.Token, async (input, correction, ct, progress) =>
                {
                    var source = LocalStorySections.Slice(report.original, input.start, input.count);
                    var result = await client.GenerateAsync(correction ? input.correctionPrompt : input.prompt,
                        input.maxTokens, input.seed, ct,
                        json => LocalStoryRewrite.TryBuild(json, source, report.interactionMemory, out _, out _));
                    // The section pipeline still performs paragraph/situation/example-copy validation.
                    return ToModelOutput(result);
                });
                report.generationWinner = "Direct HTTP";
            }
            finally
            {
                LocalStoryLifetime.Unregister(lifetime);
                Volatile.Write(ref busy, 0);
            }
        }

        public static LocalStoryModelOutput ToModelOutput(DirectLlmResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            return new LocalStoryModelOutput
            {
                json = result.CardsJson, backend = "Direct / " + result.Provider + " / " + result.Model,
                generationSeconds = result.TotalSeconds, firstTextSeconds = -1,
                inputTokens = result.InputTokens, outputTokens = result.OutputTokens, tokenLimitReached = false,
                fallback = string.Join(" / ", result.Attempts.Where(a => a.Code != "Success").Select(a => a.Provider + ":" + a.Code))
            };
        }
    }
}
