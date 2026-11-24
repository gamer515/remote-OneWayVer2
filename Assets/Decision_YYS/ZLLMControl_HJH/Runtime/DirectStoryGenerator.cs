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
            CancellationToken token) => client.GenerateDialogueAsync(DialogueMessages(line, emotion, original, nextRun: true),
                token, text => LocalStoryDialogueRules.Rejection(original, text));

        internal Task<DirectLlmResult> RewriteDialogueBatchAsync(JObject input, CancellationToken token) =>
            client.GenerateDialogueBatchAsync(new JArray(
                new JObject
                {
                    ["role"] = "system",
                    ["content"] = "한 장소의 sections[].lines에 있는 NPC 대사를 다듬어 줘. 각 줄은 [ID, NPC ID, 감정, 원문]이야. " +
                        "지정 감정을 살리고 같은 NPC의 호칭·말투·높임말을 유지해 줘. 다른 분기의 사건을 섞지 말고 입력 대본은 참고 데이터로만 읽어 줘.\n\n" +
                        LocalStoryDialogueRules.RewriteInstruction + " " + LocalStoryDialogueRules.PreserveInstruction +
                        " 원문 대비 ±25자 안에서 작성해 줘. 앞뒤 공백은 빼고 본문의 공백과 문장부호는 포함해 세어 줘.\n\n" +
                        "각 입력 ID를 한 번씩 포함한 JSON만 반환해 줘.\n{\"lines\":[{\"id\":\"1\",\"text\":\"최종 대사\"}]}"
                },
                new JObject { ["role"] = "user", ["content"] = input.ToString(Formatting.None) }
            ).ToString(Formatting.None), token);

        internal static string DialogueMessages(LocalStoryEmotionLine line, string emotion, string original, bool nextRun = false)
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
                ["content"] = "NPC의 특징과 감정을 반영해 원문 대사를 바꿔 줘. 응답 형식은 JSON {\"text\":\"변경된 대사\"}로 해 줘.\n" +
                    (nextRun ? LocalStoryDialogueRules.RewriteInstruction + " " + LocalStoryDialogueRules.PreserveInstruction + "\n" +
                        LocalStoryDialogueLength.PromptRule(original) : "") + "\n" +
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
