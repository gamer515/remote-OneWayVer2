using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OneWay.Decision.DirectLlm;
using UnityEngine;

[Serializable]
public sealed class LocalStoryEmotionLine
{
    public int cardIndex;
    public string speakerId, name, role, speechStyle, personality;
    public string[] publicFacts;
    public string generatedText, rejection, meaning, revisionNote;
    public string authoredLinePath, originalText;
    public string apiProvider, apiModel;
    public bool paraphrased;
    public bool emotionFallback;
    public bool finished;
    public bool locationBatchAttempted;
}

internal static class LocalStoryEmotion
{
    internal const string Origin = "GemmaEmotion";
    internal static readonly string[] DefaultEmotions = { "분노", "경멸", "두려움", "슬픔", "당황", "기쁨", "안도", "감사", "존중", "희망" };

    internal static string[] EmotionsFor(string storyPath)
    {
        var configured = ChapterContentCatalog.Info(storyPath)?["rewriteEmotions"] as JArray;
        var values = configured?.Values<string>().Select(value => value?.Trim())
            .Where(value => !string.IsNullOrEmpty(value)).Distinct().ToArray();
        return values?.Length > 0 ? values : DefaultEmotions;
    }

    internal static LocalStoryEmotionLine CreateLine(string chapterId, EncounterCard card, int index)
    {
        if (string.IsNullOrWhiteSpace(card?.speakerId) || string.IsNullOrWhiteSpace(card.text) ||
            string.Equals(card.speakerId, "Player", StringComparison.OrdinalIgnoreCase)) return null;

        var person = ChapterContentCatalog.Characters(chapterId).FirstOrDefault(npc => (string)npc["characterId"] == card.speakerId);
        if (person == null || (bool?)person["autoGenerate"] == false) return null;
        var authored = chapterId == "Chapter_1"
            ? MineLearningStation.ReadContent("Relationships/NPC_전체목록")["npcs"].OfType<JObject>()
                .FirstOrDefault(npc => (string)npc["name"] == (string)person["name"])
            : null;
        return CreateLine(card, index, person, authored);
    }

    internal static LocalStoryEmotionLine CreateLine(EncounterCard card, int index, JObject person, JObject authored)
    {
        string Prefer(string catalogKey, string authoredKey)
        {
            string value = (string)person[catalogKey];
            return string.IsNullOrWhiteSpace(value) ? (string)authored?[authoredKey] : value;
        }

        return new LocalStoryEmotionLine
        {
            cardIndex = index, speakerId = card.speakerId, originalText = card.text,
            name = (string)person["name"], role = (string)person["role"],
            speechStyle = Prefer("speechStyle", "speech_style"), personality = Prefer("personality", "personality"),
            publicFacts = ((person["publicFacts"] as JArray)?.Values<string>() ?? Enumerable.Empty<string>())
                .Concat((authored?["character_traits"] as JArray)?.Values<string>() ?? Enumerable.Empty<string>())
                .Distinct().ToArray()
        };
    }
}

internal static class LocalStoryDialogueLength
{
    private const int Tolerance = 25;

    private static int Count(string text) => new StringInfo(text?.Trim() ?? string.Empty).LengthInTextElements;

    internal static (int Min, int Max) Range(string original)
    {
        int count = Count(original);
        return (Math.Max(1, count - Tolerance), count + Tolerance);
    }

    internal static bool Allows(string original, string text)
    {
        var (min, max) = Range(original);
        int count = Count(text);
        return count >= min && count <= max;
    }

    internal static string PromptRule(string original)
    {
        var (min, max) = Range(original);
        return $"응답의 text 값은 {min}~{max}자로 써 줘. 앞뒤 공백을 제외하고 본문 안의 공백과 문장부호를 포함해 세어 줘.\n";
    }
}

internal static class LocalStoryDialogueRules
{
    private const int MaxPhraseWords = 4;
    private static readonly Regex PoliteEnding = new Regex(
        @"(?:습니다|습니까|십시오|나요|세요|네요|데요|거예요|예요|이에요|에요|어요|아요|해요|까요|죠|지만요|기요|만요)(?=[,.!?…]|\s*$)");
    private static readonly Regex Negation = new Regex(@"않|없|아니|(?<!\p{L})(?:안|못)\s|지\s*말");
    private static readonly Regex ObscuredText = new Regex(@"[\u2580-\u259F\u25A0-\u25FF※#]+");

    internal static string RewriteInstruction =>
        $"원문의 짧은 구절 한 곳만 바꿔 줘. 바꾸기 전과 후 모두 최대 {MaxPhraseWords}어절이고, 나머지 문구는 그대로 유지해 줘.";

    internal const string PreserveInstruction =
        "원문의 사실, 상황, 인물 이름, 수량, 요구와 판단, 반말·존댓말, 표현 수위를 유지해 줘. " +
        "화자의 말투에 맞게 지정된 감정을 표현하되, 새로운 사건이나 의도를 추가하지 마. 같은 뜻으로 바꾸기 어렵다면 원문 그대로 답해 줘.";

    internal static string Rejection(string original, string candidate)
    {
        if (!LocalStoryDialogueLength.Allows(original, candidate)) return "DialogueLengthOutOfRange";
        string source = original?.Trim() ?? "";
        string text = candidate.Trim();
        if (source == text) return null;
        if (!LocalStoryRewrite.ValidateDialogueWording(source, text) ||
            PoliteEnding.IsMatch(source) != PoliteEnding.IsMatch(text) ||
            Negation.IsMatch(source) != Negation.IsMatch(text) ||
            !ObscuredText.Matches(source).Cast<Match>().Select(match => match.Value)
                .SequenceEqual(ObscuredText.Matches(text).Cast<Match>().Select(match => match.Value)) ||
            !ChangesOnePhrase(source, text))
            return "DialogueContentChanged";
        return null;
    }

    private static bool ChangesOnePhrase(string source, string text)
    {
        var before = Regex.Matches(source, @"\S+");
        var after = Regex.Matches(text, @"\S+");
        int start = 0;
        while (start < before.Count && start < after.Count && before[start].Value == after[start].Value) start++;
        int endBefore = before.Count;
        int endAfter = after.Count;
        while (endBefore > start && endAfter > start && before[endBefore - 1].Value == after[endAfter - 1].Value)
        {
            endBefore--;
            endAfter--;
        }
        int removed = endBefore - start, added = endAfter - start;
        bool keepsSourceWording = before.Count == 1 || start > 0 || endBefore < before.Count;
        if (!keepsSourceWording || removed + added == 0 || removed > MaxPhraseWords || added > MaxPhraseWords) return false;
        if (removed == 0) return KeepsUnchangedText(source, before, text);
        if (added == 0) return KeepsUnchangedText(text, after, source);
        int prefixBefore = before[start].Index, prefixAfter = after[start].Index;
        int suffixBefore = before[endBefore - 1].Index + before[endBefore - 1].Length;
        int suffixAfter = after[endAfter - 1].Index + after[endAfter - 1].Length;
        return source.Substring(0, prefixBefore) == text.Substring(0, prefixAfter) &&
            source.Substring(suffixBefore) == text.Substring(suffixAfter);

        bool KeepsUnchangedText(string shorter, MatchCollection words, string longer)
        {
            int split = start < words.Count ? words[start].Index : shorter.Length;
            return longer.StartsWith(shorter.Substring(0, split), StringComparison.Ordinal) &&
                longer.EndsWith(shorter.Substring(split), StringComparison.Ordinal);
        }
    }
}

internal static class LocalStoryGemmaDialogue
{
    internal const string Mode = "GemmaDialogue";
    internal const string ProviderName = "Gemma";
    internal const string Policy = "짧은 구절 한 곳 변형";
    private const int MaxPhraseWords = 4;
    private const string Grammar = "root ::= \"{\" ws \"\\\"text\\\"\" ws \":\" ws str ws \"}\"\nstr ::= \"\\\"\" ([^\"\\\\\\x00-\\x1f] | \"\\\\\" [\"\\\\/bfnrt] | \"\\\\u\" hex hex hex hex)+ \"\\\"\"\nhex ::= [0-9a-fA-F]\nws ::= [ \\t\\n\\r]*";

    internal static string Messages(LocalStoryEmotionLine line, string emotion, string original, bool nextRun = false)
    {
        string instructions = nextRun
            ? LocalStoryDialogueRules.RewriteInstruction + " " + LocalStoryDialogueRules.PreserveInstruction + "\n\n"
            : "[원문]에서 짧은 구절 한 곳을 골라 [화자 말투]와 [감정]에 어울리는 표현으로 바꿔 줘. " +
            $"바꾸기 전과 후 모두 최대 {MaxPhraseWords}어절이고, 나머지 문구는 그대로 유지해 줘.\n\n" +
            "인물 이름, 수량, 반말·존댓말, 표현 수위, 누가 누구에게 무엇을 요구하거나 판단하는지는 유지해 줘.\n\n";
        string prompt = instructions +
            $"[화자 말투] {line.speechStyle}\n[감정] {emotion}\n[원문] {original}\n\n" +
            (nextRun ? LocalStoryDialogueLength.PromptRule(original) : "") +
            "{\"text\":\"최종 대사\"} 형식으로 응답해 줘.";
        return new JArray(new JObject { ["role"] = "user", ["content"] = prompt }).ToString(Formatting.None);
    }

    internal static async Task<(string Text, string Json, string Backend)> Generate(
        string messages, int seed, CancellationToken token, bool nextRun = false)
    {
        string root = Path.Combine(Application.streamingAssetsPath, "LocalStory");
        var sampling = LocalStorySampling.Dialogue();
        if (nextRun) sampling = sampling.WithTokenLimit(256);
        var output = await LocalStoryInference.Generate(root, messages, Grammar, seed, token, null,
            sampling, LocalStoryTestModels.PathFor(root, LocalStoryTestModel.Gemma));
        return (DirectLlmProtocol.Dialogue(output.json), output.json, output.backend);
    }
}
