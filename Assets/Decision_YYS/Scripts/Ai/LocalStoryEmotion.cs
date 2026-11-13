using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

[Serializable]
public sealed class LocalStoryEmotionLine
{
    public int cardIndex;
    public string speakerId, name, role, speechStyle, personality;
    public string[] publicFacts;
    public string generatedText, rejection, meaning, revisionNote;
    public bool paraphrased;
    public bool finished;
}

internal static class LocalStoryEmotion
{
    internal const string Origin = "GemmaEmotion";
    internal const string RewriteGrammar = "root ::= \"{\" ws \"\\\"text\\\"\" ws \":\" ws string ws \"}\" ws\n" +
        "string ::= \"\\\"\" [^\"\\\\\\x00-\\x1f]{1,160} \"\\\"\"\nws ::= [ \\t\\r\\n]?";
    private const string ReviewGrammar = "root ::= \"{\" ws \"\\\"usable\\\"\" ws \":\" ws (\"true\" | \"false\") ws \"}\" ws\nws ::= [ \\t\\r\\n]?";
    private const string PreviousRewriteInstruction =
        "지정된 감정으로 NPC 대사 한 줄의 말투를 바꿔라. 원문의 단어 순서, 높임말, 부정과 요구 표현은 유지해라. " +
        "앞뒤에 감탄사(아, 어, 음, 하, 으으, 쯧, 휴, 아휴, 어휴, 앗)를 짧게 넣거나 쉼과 느낌표를 바꿔 감정을 표현해라. " +
        "물음표는 유지해라. 나는→난, 너는→넌, 이것은→이건, 그것은→그건 같은 구어 축약은 가능하다. " +
        "발화만 160자 이내로 쓰고 JSON {\"text\":\"대사\"}만 반환해라.";
    private const string RewriteInstruction =
        "원문 NPC 대사를 지정된 감정과 말투로 다시 말해라. 같은 내용을 말하면서 어휘나 어순을 바꿔라. " +
        "원문에 없는 설명, 이유, 인물, 사건은 쓰지 마라. 이름, 사실, 질문·요구의 강도, 높임말을 유지해라. " +
        "감탄사나 문장부호만 바꾸지 말고 한국어 발화만 160자 이내로 JSON {\"text\":\"대사\"}로 반환해라.";
    private const string ReviewInstruction =
        "원문과 후보를 비교해 다음을 각각 확인해라. 누가 무엇을 했는지, 긍정과 부정, 시간과 비교 방향이 같은가? " +
        "질문·요구의 강도와 높임말이 같은가? 새 사실이 없고 지정 감정이 자연스럽게 느껴지는가? " +
        "하나라도 어긋나거나 불확실하면 false, 모두 맞으면 true다. " +
        "JSON {\"usable\":true} 또는 {\"usable\":false}만 반환해라.";
    private static readonly string[] DefaultEmotions = { "화", "겁", "슬픔", "기쁨", "당황", "안도" };
    private static readonly Regex Facts = new Regex(@"\{[^{}]*\}|[0-9]+(?:[.,][0-9]+)*");
    private const string Interjection = "아|어|음|하|으으|쯧|휴|아휴|어휴|앗|헉|허|어이|저기|어머나|이런|에이";
    private static readonly Regex InterjectionCue = new Regex(@"(?<!\S)(?:" + Interjection + @")[,.!?…~]+\s*");
    private static readonly Regex Closing = new Regex(@"[,!.…~]+\s*(?:" + Interjection + @")[!.…~]*$");
    private static readonly Regex Pacing = new Regex(@"[,.!?…~]+(?=\s|$)");
    private static readonly Regex Negation = new Regex(@"않|없|아니|(?<!\p{L})(?:안|못)\s|지\s*말");
    private static readonly Regex PoliteEnding = new Regex(
        @"(?:습니다|습니까|십시오|나요|세요|네요|데요|거예요|예요|이에요|에요|어요|아요|해요|까요|죠|지만요|기요|만요)(?=[,.!?…]|$)");
    private static readonly Regex PaymentTerm = new Regex(@"통행세|통행료|세금");
    private static readonly Regex PaymentDemand = new Regex(
        @"(?:내(?=[,.!?…]|$)|(?:내(?:고|라(?:고)?|야(?:만)?|세요|십시오|도록|지\s*않으면)|(?:납부|지불)(?:해(?:라)?|하라|하세요|해야))(?=[\s,.!?…]|$))");
    private static readonly Regex EarlierWeight = new Regex(@"어제.{0,30}(?:이렇게|이만큼|이 정도로)\s*무겁.{0,8}않");
    private static readonly Regex LighterEarlier = new Regex(@"어제.{0,35}(?:가벼웠|덜\s*무거웠)(?:다|지|는데|어(?!야))");
    private static readonly (string Long, string Short)[] Contractions =
    {
        ("나는", "난"), ("너는", "넌"), ("이것은", "이건"), ("그것은", "그건"), ("저것은", "저건")
    };

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
        return new LocalStoryEmotionLine
        {
            cardIndex = index, speakerId = card.speakerId,
            name = (string)person["name"], role = (string)person["role"],
            speechStyle = (string)person["speechStyle"], personality = (string)person["personality"],
            publicFacts = (person["publicFacts"] as JArray)?.Values<string>().ToArray()
        };
    }

    internal static async Task RewriteLine(LocalStoryDialogueJob job, LocalStoryEmotionLine line, EncounterCard card,
        Func<string, string, int, CancellationToken, Task<string>> infer, CancellationToken token)
    {
        int seed = unchecked(job.sourceRun * 100003 + ChapterContentCatalog.CardId(job.storyPath, line.cardIndex));
        line.meaning = card.coreMeaning == card.text ? null : card.coreMeaning;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var input = Input(job.emotion, line, card.text);
            string raw = await infer(Messages(RewriteInstruction, input).ToString(Formatting.None), RewriteGrammar, seed + attempt, token);
            token.ThrowIfCancellationRequested();
            string candidate = (string)Parse(raw, "text", JTokenType.String)?.Value;
            if (!ValidChange(card.text, candidate) || SpokenContent(card.text) == SpokenContent(candidate))
            {
                line.revisionNote = "원문의 사실과 요구를 유지하면서 어휘나 어순이 다른 대사를 써라.";
                continue;
            }
            input.Remove("revisionNote");
            input.Remove("example");
            input["candidate"] = candidate;
            raw = await infer(Messages(ReviewInstruction, input).ToString(Formatting.None), ReviewGrammar, seed + attempt, token);
            token.ThrowIfCancellationRequested();
            if ((bool?)Parse(raw, "usable", JTokenType.Boolean)?.Value == true)
            {
                line.generatedText = candidate;
                line.paraphrased = true;
                line.finished = true;
                return;
            }
            line.revisionNote = "원문과 대조해 사실과 발화 의도를 보존하고, 감정이 드러나는 새 문장을 써라.";
        }

        line.rejection = "뜻을 유지하며 어휘나 어순이 바뀐 문장을 확인하지 못했습니다.";
        line.finished = true;
    }

    internal static async Task<string> Infer(string messages, string grammar, int seed, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            string root = Path.Combine(Application.streamingAssetsPath, "LocalStory");
            var output = await LocalStoryInference.Generate(root, messages, grammar, seed, timeout.Token, null,
                LocalStorySampling.Proofreading().WithTokenLimit(256));
            return output.tokenLimitReached ? null : output.json;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("Gemma 응답 시간 초과. 다음 실행에서 이 문장을 다시 시도합니다.");
        }
    }

    internal static string TrainingExample(string emotion, LocalStoryEmotionLine line, string original)
    {
        var messages = Messages(line.paraphrased ? RewriteInstruction : PreviousRewriteInstruction,
            Input(emotion, line, original, line.paraphrased));
        messages.Add(new JObject
        {
            ["role"] = "assistant",
            ["content"] = new JObject { ["text"] = line.generatedText }.ToString(Formatting.None)
        });
        return new JObject { ["messages"] = messages }.ToString(Formatting.None);
    }

    internal static bool ValidChange(string original, string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Trim() == original?.Trim() ||
            candidate != candidate.Trim() || candidate.Length > 160 || candidate.Any(char.IsControl) ||
            Regex.IsMatch(candidate, @"^.{1,20}[:：]|<[^>]+>|\x60\x60\x60") ||
            !Regex.IsMatch(candidate, "[가-힣]")) return false;
        if (string.IsNullOrWhiteSpace(original) ||
            !Facts.Matches(original).Cast<Match>().Select(match => match.Value)
                .SequenceEqual(Facts.Matches(candidate).Cast<Match>().Select(match => match.Value)) ||
            PoliteEnding.IsMatch(original) != PoliteEnding.IsMatch(candidate) ||
            (Negation.IsMatch(original) && !Negation.IsMatch(candidate) &&
                !(EarlierWeight.IsMatch(original) && LighterEarlier.IsMatch(candidate)))) return false;
        var payment = PaymentTerm.Match(original);
        return !payment.Success || !PaymentDemand.IsMatch(original) ||
            (candidate.Contains(payment.Value) && PaymentDemand.IsMatch(candidate));
    }

    private static string SpokenContent(string text)
    {
        // Compare the spoken clauses after removing only supported changes in delivery.
        string spoken = Closing.Replace(InterjectionCue.Replace(text.Trim(), ""), "");
        spoken = Regex.Replace(Pacing.Replace(spoken, ""), @"\s+", " ").Trim();
        foreach (var (longForm, shortForm) in Contractions)
            spoken = Regex.Replace(spoken, @"(?<!\p{L})" + shortForm + @"(?!\p{L})", longForm);
        return spoken;
    }

    private static JObject Input(string emotion, LocalStoryEmotionLine line, string original, bool newPrompt = true)
    {
        var input = new JObject
        {
            ["emotion"] = emotion,
            ["npc"] = new JObject
            {
                ["name"] = line.name,
                ["role"] = line.role,
                ["personality"] = line.personality,
                ["speechStyle"] = string.IsNullOrWhiteSpace(line.speechStyle)
                    ? (newPrompt ? "원문의 화자 말투와 높임말을 참고" : "원문의 어휘와 말끝을 참고")
                    : line.speechStyle,
                ["publicFacts"] = JArray.FromObject(line.publicFacts ?? Array.Empty<string>())
            },
            ["original"] = original
        };
        if (!string.IsNullOrWhiteSpace(line.meaning)) input["meaning"] = line.meaning;
        if (!string.IsNullOrWhiteSpace(line.revisionNote)) input["revisionNote"] = line.revisionNote;
        if (newPrompt)
        {
            input["register"] = PoliteEnding.IsMatch(original) ? "원문의 높임말을 유지해라." :
                "원문처럼 반말로 써라. 문장을 ~요, ~습니다로 끝내지 마라.";
            if (emotion == "겁")
                input["emotionHint"] = "어이없거나 화난 반응 대신 겁먹거나 불안한 목소리로 말해라.";
            var payment = PaymentTerm.Match(original);
            if (payment.Success && PaymentDemand.IsMatch(original))
            {
                input["keep"] = $"\"{payment.Value}\"를 그대로 써라. 들어가기 전에 상대에게 그 돈을 내라고 요구해라.";
                input["example"] = "화 · 원문: 문을 열려면 열쇠부터 줘. → 문 열고 싶으면 열쇠 먼저 내놔!";
            }
            else if (EarlierWeight.IsMatch(original))
            {
                input["keep"] = "원문의 물건이 어제까지 지금보다 덜 무거웠다는 뜻이다. 물건 이름, 시간과 무게 비교 방향을 유지해라.";
                input["example"] = "겁 · 원문: 아까까진 이 가방이 이렇게 무겁지 않았는데. → 이상해… 아까만 해도 이 가방은 이 정도로 무겁진 않았어.";
            }
            else if (Negation.IsMatch(original))
                input["keep"] = "원문이 무엇을 부정하는지 확인하고 그 뜻을 유지해라.";
        }
        return input;
    }

    private static JArray Messages(string instruction, JObject input) => new JArray(new JObject
    {
        ["role"] = "user", ["content"] = instruction + "\n\n" + input.ToString(Formatting.None)
    });

    private static JProperty Parse(string raw, string name, JTokenType type)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            var value = JObject.Parse(raw, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            var property = value.Property(name);
            return value.Count == 1 && property?.Value.Type == type ? property : null;
        }
        catch (JsonException) { return null; }
    }
}
