using System;
using System.IO;
using System.Linq;
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
            cardIndex = index, speakerId = card.speakerId,
            name = (string)person["name"], role = (string)person["role"],
            speechStyle = Prefer("speechStyle", "speech_style"), personality = Prefer("personality", "personality"),
            publicFacts = ((person["publicFacts"] as JArray)?.Values<string>() ?? Enumerable.Empty<string>())
                .Concat((authored?["character_traits"] as JArray)?.Values<string>() ?? Enumerable.Empty<string>())
                .Distinct().ToArray()
        };
    }
}

internal static class LocalStoryGemmaDialogue
{
    internal const string Mode = "GemmaDialogue";
    internal const string ProviderName = "Gemma";
    internal const string Policy = "짧은 구절 한 곳 변형";
    private const int MaxPhraseWords = 4;
    private const string Grammar = "root ::= \"{\" ws \"\\\"text\\\"\" ws \":\" ws str ws \"}\"\nstr ::= \"\\\"\" ([^\"\\\\\\x00-\\x1f] | \"\\\\\" [\"\\\\/bfnrt] | \"\\\\u\" hex hex hex hex)+ \"\\\"\"\nhex ::= [0-9a-fA-F]\nws ::= [ \\t\\n\\r]*";

    internal static string Messages(LocalStoryEmotionLine line, string emotion, string original)
    {
        string prompt = "[원문]에서 짧은 구절 한 곳을 골라 [화자 말투]와 [감정]에 어울리는 표현으로 바꿔 줘. " +
            $"바꾸기 전과 후 모두 최대 {MaxPhraseWords}어절이고, 나머지 문구는 그대로 유지해 줘.\n\n" +
            "인물 이름, 수량, 반말·존댓말, 표현 수위, 누가 누구에게 무엇을 요구하거나 판단하는지는 유지해 줘.\n\n" +
            $"[화자 말투] {line.speechStyle}\n[감정] {emotion}\n[원문] {original}\n\n" +
            "{\"text\":\"최종 대사\"} 형식으로 응답해 줘.";
        return new JArray(new JObject { ["role"] = "user", ["content"] = prompt }).ToString(Formatting.None);
    }

    internal static async Task<(string Text, string Json, string Backend)> Generate(
        string messages, int seed, CancellationToken token)
    {
        string root = Path.Combine(Application.streamingAssetsPath, "LocalStory");
        var output = await LocalStoryInference.Generate(root, messages, Grammar, seed, token, null,
            LocalStorySampling.Dialogue(), LocalStoryTestModels.PathFor(root, LocalStoryTestModel.Gemma));
        return (DirectLlmProtocol.Dialogue(output.json), output.json, output.backend);
    }
}
