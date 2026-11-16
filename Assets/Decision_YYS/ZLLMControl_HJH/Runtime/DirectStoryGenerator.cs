using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OneWay.Decision.DirectLlm
{
    /// <summary>Adapts the direct HTTP client to the dialogue queue and the full-story comparison tool.</summary>
    public sealed class DirectStoryGenerator
    {
        public const string ModelName = "Direct HTTP";
        internal const string DialogueOrigin = "DirectEmotion";
        private readonly DirectLlmClient client;
        private int busy;
        public DirectStoryGenerator(DirectLlmClient client) => this.client = client ?? throw new ArgumentNullException(nameof(client));

        public Task<DirectLlmResult> RewriteDialogueAsync(LocalStoryEmotionLine line, string emotion, string original,
            CancellationToken token) => client.GenerateDialogueAsync(DialogueMessages(line, emotion, original), token);

        internal static string DialogueMessages(LocalStoryEmotionLine line, string emotion, string original)
        {
            var input = new JObject
            {
                ["npc"] = new JObject
                {
                    ["name"] = line.name, ["role"] = line.role, ["personality"] = line.personality,
                    ["speechStyle"] = line.speechStyle, ["publicFacts"] = new JArray(line.publicFacts ?? Array.Empty<string>())
                },
                ["emotion"] = emotion,
                ["original"] = original
            };
            return new JArray(new JObject
            {
                ["role"] = "user",
                ["content"] = "NPC의 특징과 감정을 반영해 원문 대사를 바꿔 줘. 응답 형식은 JSON {\"text\":\"변경된 대사\"}로 해 줘.\n\n" +
                    input.ToString(Formatting.None)
            }).ToString(Formatting.None);
        }

        public async Task GenerateRuntimeAsync(LocalStoryTestReport report, CancellationToken token)
        {
            if (report?.original?.MainStory == null) throw new ArgumentException("A populated LocalStoryTestReport is required.");
            token.ThrowIfCancellationRequested();
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) throw new InvalidOperationException("This generator is already processing a story.");
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            LocalStoryLifetime.Register(lifetime);
            try
            {
                report.model = ModelName;
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
                report.generationWinner = ModelName;
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
