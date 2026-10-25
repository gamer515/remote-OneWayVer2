using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

[Serializable] public sealed class LocalStoryPatch { public LocalStoryPatchCard[] cards; }
[Serializable] public sealed class LocalStoryPatchCard { public int index; public string text; public string[] options; }
[Serializable] public sealed class LocalStoryWritingBrief
{
    public string storyPath, sourceSha256, style;
    public string[] eventOrder;
    public LocalStoryChoiceReference[] choiceReferences;
}
[Serializable] public sealed class LocalStoryChoiceReference { public string choiceId, text, heardText; }
[Serializable] public sealed class LocalStoryTestReport
{
    public string storyPath, model = "Qwen3-4B-Instruct-2507 Q4_K_M", backend, fallback, prompt, rawOutput, testScenario;
    public string archiveId, archiveOrigin;
    public string writingPolicy, generationGrammar;
    public string examplePrompt, exampleSummary;
    public string comparisonMode;
    public string[] exampleSources;
    public string generationMode;
    public List<LocalStorySectionInput> sectionInputs;
    public List<LocalStorySectionOutput> sectionOutputs;
    public int sectionsGenerated, sectionsCorrected, sectionsResumed, authoredCards, paragraphsPlanned;
    public bool allowFailedOutput, reviewRequired;
    public int reviewGeneratedCards;
    [NonSerialized] public string reviewRawOutput;
    public string reviewError;
    [NonSerialized] public LocalStorySectionCheckpoint sectionCheckpoint;
    [NonSerialized] public Action<LocalStorySectionCheckpoint> saveSectionCheckpoint;
    [NonSerialized] public Action<int,int,int,int,string> sectionProgress;
    [NonSerialized] public Func<bool> sectionStillCurrent;
    public LocalStoryRevisionRequest interactionMemory;
    public string interactionSource;
    public int archiveNumber;
    public int sourceRun, targetRun, seed, changedCards, unchangedCards;
    public double seconds, literalSimilarity;
    public bool valid, reviewed, saved;
    public string error, naturalness = "미평가 — 직접 읽고 평가", createdUtc;
    public EncounterCardRoot original, generated;
    // rawOutput/generated always remain the draft, including rejected output. Never overwrite with correction or source.
    public string samplingProfile, draftParameters, correctionParameters, correctionPrompt, correctionRawOutput, correctionError, correctionBackend, correctionFallback;
    public double draftSeconds, correctionSeconds;
    public bool correctionAttempted, correctionValid;
    public EncounterCardRoot corrected;
    public string runtimeStage, runtimeError;
    public string modelPath, runtimePath, runtimeFailureDetails;
    public int verbatimReferenceLines;
    public bool draftSessionReused, correctionSessionReused;
    public double draftModelLoadSeconds, correctionModelLoadSeconds, draftGenerationSeconds, correctionGenerationSeconds;
    public double draftVerificationSeconds, correctionVerificationSeconds;
    public string verificationProvider;
    public long verificationBytes;
    public double draftFirstTextSeconds, correctionFirstTextSeconds;
    public void RecordPerformance(LocalStoryModelOutput output, bool correction)
    {
        verificationProvider=output.verificationProvider; verificationBytes+=output.verificationBytes;
        if (correction)
        {
            correctionSessionReused=output.sessionReused; correctionModelLoadSeconds=output.modelLoadSeconds;
            correctionVerificationSeconds=output.verificationSeconds;
            correctionGenerationSeconds=output.generationSeconds; correctionFirstTextSeconds=output.firstTextSeconds;
        }
        else
        {
            draftSessionReused=output.sessionReused; draftModelLoadSeconds=output.modelLoadSeconds;
            draftVerificationSeconds=output.verificationSeconds;
            draftGenerationSeconds=output.generationSeconds; draftFirstTextSeconds=output.firstTextSeconds;
        }
    }
}

/// <summary>Explicitly synthetic comparison inputs. Never written to player progress or next-run requests.</summary>
public static class LocalStoryTestInputs
{
    public const string GuidePath="Initial/Initial_01/Encounters/guide";
    public static string Name(int scenario) => scenario==1?"첫 만남":scenario==2?"우호 재회":scenario==3?"모욕 재회":"실제 기록";
    public static LocalStoryRevisionRequest Create(int scenario)
    {
        if(scenario==1) return null;
        if(scenario!=2 && scenario!=3) throw new ArgumentOutOfRangeException(nameof(scenario));
        return new LocalStoryRevisionRequest {sourceRun=1,targetRun=2,chapterId="Initial",finalStats=new[]{5,5,5,5},
            events=new List<StoryEventRecord>{new StoryEventRecord {chapterId="Initial",characterId="Guide",characterName="로키",
                terrainName="Dungeon",kind="choice",choiceId=scenario==2?"loki_question_place":"loki_insult",
                semanticText=scenario==2?"왜 여기에 있는 거지?":"꺼져!",relationshipDelta=scenario==2?1:-2}}};
    }
}

/// <summary>The model returns wording only, never an executable Story or new facts/actions.</summary>
public static partial class LocalStoryRewrite
{
    private static string[] knownIdentifiers;
    public static void ResetIdentifiers() => knownIdentifiers=null;
    public const string WritingPolicy = "간결한 핵심 장면 v10.3";
    public static string Quote(string value)
    {
        var b = new StringBuilder("\"");
        foreach (char c in value ?? "")
        {
            switch (c)
            {
                case '"': b.Append("\\\""); break;
                case '\\': b.Append("\\\\"); break;
                case '\n': b.Append("\\n"); break;
                case '\r': b.Append("\\r"); break;
                case '\t': b.Append("\\t"); break;
                default: if (c < 32) b.Append("\\u" + ((int)c).ToString("x4")); else b.Append(c); break;
            }
        }
        return b.Append('"').ToString();
    }
    public static string Grammar(EncounterCardRoot original, bool requireNonEmpty = false)
    {
        var b = new StringBuilder("root ::= \"{\" ws \"\\\"cards\\\"\" ws \":\" ws \"[\" ws ");
        for (int i = 0; i < original.MainStory.Count; i++)
            b.Append((i == 0 ? "" : "\",\" ws ") + "card" + i + " ws ");
        b.Append("\"]\" ws \"}\" ws\n");
        for (int i = 0; i < original.MainStory.Count; i++)
        {
            var card = original.MainStory[i];
            b.Append("card" + i + " ::= " + Quote("{\"index\":" + i + ",\"text\":") + " ws " +
                "string" + " ws " + Quote(",\"options\":[") + " ws ");
            for (int j = 0; j < (card.options?.Length ?? 0); j++)
                b.Append((j == 0 ? "" : "\",\" ws ") + "string" + " ws ");
            b.Append(Quote("]}") + "\n");
        }
        // Unbounded ws let the model spend its entire output budget on newlines between cards.
        // One optional whitespace token at each boundary is sufficient; display formatting is separate.
        b.Append("string ::= \"\\\"\" chars \"\\\"\"\nchars ::= ([^\"\\\\\\x00-\\x1F] | \"\\\\\" ([\"\\\\/bfnrt] | \"u\" [0-9a-fA-F] [0-9a-fA-F] [0-9a-fA-F] [0-9a-fA-F]))" +
            (requireNonEmpty ? "+" : "*") + "\nws ::= [ \\t\\n\\r]?\n");
        return b.ToString();
    }

    public static string Messages(string storyPath, EncounterCardRoot original, LocalStoryRevisionRequest memory) =>
        LocalStoryPrompt.Messages(storyPath,original,0,original.MainStory.Count,new List<EncounterCard>(),memory);
    // Scene direction and source-bound event/choice data are separate. Ordinary slots have no sentence-level intent.
    public static LocalStoryWritingBrief WritingBrief(string path, EncounterCardRoot original)
    {
        var brief=ChapterContentCatalog.Brief(path);
        if(brief==null) return null;
        var source=ChapterContentCatalog.Asset("Story_Json_Data/"+path+"/Story");
        var complete=source==null?null:JsonUtility.FromJson<EncounterCardRoot>(source.text);
        if(brief?.storyPath!=path || complete?.storyFrame==null || string.IsNullOrWhiteSpace(complete.storyFrame.purpose) ||
            complete.storyFrame.worldFacts==null || complete.storyFrame.firstMeetingFlow==null ||
            complete.storyFrame.reunionFlow==null || brief.eventOrder==null ||
            complete?.MainStory==null || brief.eventOrder.Length!=complete.MainStory.Count ||
            original?.MainStory==null || original.MainStory.Count==0 || original.MainStory.Count>brief.eventOrder.Length)
            throw new InvalidOperationException("집필 장면 설정과 Story 연결이 맞지 않습니다.");
        var ids=new HashSet<string>(); var refs=new HashSet<string>();
        foreach(var card in complete.MainStory)
            foreach(var choice in card.choiceActions??Array.Empty<StoryChoiceAction>()) ids.Add(choice.choiceId);
        foreach(var reference in brief.choiceReferences??Array.Empty<LocalStoryChoiceReference>())
            if(reference==null || !ids.Contains(reference.choiceId) || !refs.Add(reference.choiceId) || string.IsNullOrWhiteSpace(reference.text))
                throw new InvalidOperationException("집필 선택지 발화 참고의 선택 ID가 맞지 않습니다.");
        for(int i=0;i<complete.MainStory.Count;i++)
            if(brief.eventOrder[i]!=complete.MainStory[i].eventIdStable)
                throw new InvalidOperationException("집필 장면 설정의 사건 순서가 맞지 않습니다: "+i);
        if(JsonUtility.ToJson(original.storyFrame)!=JsonUtility.ToJson(complete.storyFrame))
            throw new InvalidOperationException("원문의 장면 핵심 설정이 변경됐습니다.");
        for(int i=0;i<original.MainStory.Count;i++)
        {
            var card=original.MainStory[i]; var sourceCard=complete.MainStory[i];
            if(brief.eventOrder[i]!=card.eventIdStable || card.speakerId!=sourceCard.speakerId || (card.choiceKind??"")!=(sourceCard.choiceKind??"") || (card.coreMeaning??"")!=(sourceCard.coreMeaning??"") ||
                (card.options?.Length??0)!=(sourceCard.options?.Length??0) ||
                JsonUtility.ToJson(card.guideAction)!=JsonUtility.ToJson(sourceCard.guideAction) ||
                !SameChoiceBindings(card.choiceActions,sourceCard.choiceActions))
                throw new InvalidOperationException("집필 원본 사건/화자/선택 연결이 변경됐습니다: "+i);
        }
        return brief;
    }
    private static bool SameChoiceBindings(StoryChoiceAction[] left,StoryChoiceAction[] right)
    {
        if((left?.Length??0)!=(right?.Length??0)) return false;
        for(int i=0;i<(left?.Length??0);i++)
            if(JsonUtility.ToJson(left[i])!=JsonUtility.ToJson(right[i])) return false;
        return true;
    }
    public static string CorrectionMessages(string path, EncounterCardRoot original, LocalStoryRevisionRequest memory, string draft)
    {
        // Check shape only here: wording/garbling errors must reach the correction pass rather than stop it.
        var patch=new PatchReader(draft).Read();
        if(patch.cards.Length!=original.MainStory.Count) throw new FormatException("교정 불가: 초안 카드 수 불일치");
        for(int i=0;i<patch.cards.Length;i++)
        {
            var card=original.MainStory[i]; var p=patch.cards[i];
            if(p.index!=i || p.options.Length!=(card.options?.Length??0)) throw new FormatException("교정 불가: 초안 순서/선택지 수 불일치");
        }
        var issues=new List<string>();
        for(int i=0;i<patch.cards.Length;i++)
        {
            var candidate=JsonUtility.FromJson<EncounterCard>(JsonUtility.ToJson(original.MainStory[i]));
            candidate.text=patch.cards[i].text;candidate.options=patch.cards[i].options;
            if(!ValidateCardWording(original.MainStory[i],candidate,out var error))issues.Add("index="+i+": "+error);
        }
        return LocalStoryPrompt.Messages(path,original,0,original.MainStory.Count,new List<EncounterCard>(),memory,draft,string.Join("\n",issues));
    }

    /// <summary>Shared by the test UI and diagnostics. Validation never replaces raw output with the source.</summary>
    public static void EvaluateDraft(LocalStoryTestReport report, EncounterCardRoot original, LocalStoryRevisionRequest memory)
    {
        report.changedCards = report.unchangedCards = 0;
        report.literalSimilarity = 0;
        report.valid = TryBuild(report.rawOutput, original, memory, out report.generated, out report.error);
        if (!report.valid) return;

        report.literalSimilarity = Similarity(original, report.generated);
        for (int i = 0; i < original.MainStory.Count; i++)
        {
            var source = original.MainStory[i];
            var candidate = report.generated.MainStory[i];
            if (source.text == candidate.text && Same(source.options, candidate.options ?? Array.Empty<string>()))
                report.unchangedCards++;
            else report.changedCards++;
        }
    }

    public static bool TryBuild(string raw, EncounterCardRoot original, LocalStoryRevisionRequest memory,
        out EncounterCardRoot generated, out string error)
    {
        generated = null;
        try
        {
            StoryChoiceRoles.ValidateRoot(original);
            // A deliberately narrow parser rejects additional keys, duplicate keys, non-JSON and truncation.
            LocalStoryPatch patch = new PatchReader(raw).Read();
            if (patch.cards.Length != original.MainStory.Count) throw new FormatException("카드 수 불일치");
            var candidate = JsonUtility.FromJson<EncounterCardRoot>(JsonUtility.ToJson(original));
            for (int i = 0; i < patch.cards.Length; i++)
            {
                var p = patch.cards[i]; var o = original.MainStory[i];
                if (p.index != i) throw new FormatException("카드 순서/번호 불일치");
                ValidateWording(o.text, p.text, 160, memory);
                if (p.options.Length != (o.options?.Length ?? 0)) throw new FormatException("선택지 수 변경");
                for (int j = 0; j < p.options.Length; j++) ValidateWording(o.options[j], p.options[j], 45, memory);
                ValidateGarbled(o,p.text);
                ValidateGarbledChoices(o,p.options);
                ValidateIdentifiers(o,p.text,p.options);
                candidate.MainStory[i].text = p.text;
                candidate.MainStory[i].options = o.options == null ? null : p.options;
            }
            // Rules/IDs/actions were never supplied by the model: they are cloned from the original.
            generated = candidate; error = null; return true;
        }
        catch (Exception e) { error = e.Message; return false; }
    }
    // Diagnostic playback accepts wording failures, while the model still cannot supply game actions or bindings.
    public static bool TryBuildForReview(string raw,EncounterCardRoot original,out EncounterCardRoot generated,out string error,bool allowPartial=false)
    {
        generated=null;
        try
        {
            StoryChoiceRoles.ValidateRoot(original);
            var patch=new PatchReader(raw).Read();
            if(patch.cards.Length==0 || patch.cards.Length>original.MainStory.Count || (!allowPartial&&patch.cards.Length!=original.MainStory.Count))throw new FormatException("카드 수 불일치");
            var candidate=JsonUtility.FromJson<EncounterCardRoot>(JsonUtility.ToJson(original));
            for(int i=0;i<patch.cards.Length;i++)
            {
                var card=patch.cards[i];var source=original.MainStory[i];
                if(card.index!=i || card.options.Length!=(source.options?.Length??0))throw new FormatException("카드 순서/선택지 수 불일치");
                if(string.IsNullOrWhiteSpace(card.text)||card.options.Any(string.IsNullOrWhiteSpace))throw new FormatException("표시할 대사/선택지가 비어 있음");
                candidate.MainStory[i].text=card.text;candidate.MainStory[i].options=source.options==null?null:card.options;
            }
            generated=candidate;error=patch.cards.Length<original.MainStory.Count?patch.cards.Length+"/"+original.MainStory.Count+"카드 생성 문구 반영, 나머지는 원문 유지":null;return true;
        }
        catch(Exception e){error=e.Message;return false;}
    }
    // Dialogue wording is generated; executable bindings and scene definitions come from authored game data.
    public static bool SameBindings(EncounterCardRoot original, EncounterCardRoot changed)
    {
        if(original?.MainStory==null || changed?.MainStory==null || original.MainStory.Count!=changed.MainStory.Count ||
            original.MainStory.Any(c=>c==null) || changed.MainStory.Any(c=>c==null)) return false;
        var left=JsonUtility.FromJson<EncounterCardRoot>(JsonUtility.ToJson(original));
        var right=JsonUtility.FromJson<EncounterCardRoot>(JsonUtility.ToJson(changed));
        for(int i=0;i<left.MainStory.Count;i++)
        {
            if((left.MainStory[i].options?.Length??0)!=(right.MainStory[i].options?.Length??0)) return false;
            left.MainStory[i].text=right.MainStory[i].text=null;
            left.MainStory[i].options=right.MainStory[i].options=null;
        }
        return JsonUtility.ToJson(left)==JsonUtility.ToJson(right);
    }
    /// <summary>Shared runtime/test wording guards. They do not certify semantic or literary quality.</summary>
    public static bool ValidateCardWording(EncounterCard source, EncounterCard candidate, out string error)
    {
        try
        {
            ValidateWording(source.text,candidate.text,160,null);
            if((source.options?.Length??0)!=(candidate.options?.Length??0)) throw new FormatException("선택지 수 변경");
            for(int i=0;i<(source.options?.Length??0);i++) ValidateWording(source.options[i],candidate.options[i],45,null);
            ValidateGarbled(source,candidate.text); ValidateGarbledChoices(source,candidate.options);
            ValidateIdentifiers(source,candidate.text,candidate.options);
            error=null; return true;
        }
        catch(Exception e) { error=e.Message; return false; }
    }
    private static void ValidateIdentifiers(EncounterCard source,string text,string[] options)
    {
        if(knownIdentifiers==null)knownIdentifiers=ChapterContentCatalog.Stories.SelectMany(info=>
            JsonUtility.FromJson<EncounterCardRoot>(ChapterContentCatalog.Story((string)info["path"])).MainStory)
            .SelectMany(c=>(c.choiceActions??Array.Empty<StoryChoiceAction>()).Select(a=>a?.choiceId).Append(c.eventIdStable))
            .Where(id=>!string.IsNullOrWhiteSpace(id)).Distinct().ToArray();
        var identifiers=(source.choiceActions??Array.Empty<StoryChoiceAction>()).Select(a=>a?.choiceId)
            .Append(source.eventIdStable).Concat(knownIdentifiers)
            .Where(id=>!string.IsNullOrWhiteSpace(id)).Distinct();
        var authored=new[]{source.text}.Concat(source.options??Array.Empty<string>());
        var generated=new[]{text}.Concat(options??Array.Empty<string>());
        foreach(string id in identifiers)
            if(!authored.Any(s=>s?.Contains(id)==true) && generated.Any(s=>s?.Contains(id)==true))
                throw new FormatException("내부 이벤트/선택 ID를 대사로 출력함: "+id);
    }
    private static void ValidateGarbledChoices(EncounterCard original, string[] changed)
    {
        if(string.IsNullOrWhiteSpace(original.semanticText)) return;
        var refs=ChapterContentCatalog.References().ToArray();
        for(int i=0;i<(changed?.Length??0);i++)
            foreach(var reference in refs??Array.Empty<LocalStoryChoiceReference>())
                if(i<(original.choiceActions?.Length??0) && reference.choiceId==original.choiceActions[i].choiceId && !reference.choiceId.EndsWith("_mumble",StringComparison.Ordinal) &&
                    Regex.Replace(changed[i],@"[\s\p{P}\p{S}]","")==Regex.Replace(reference.text,@"[\s\p{P}\p{S}]",""))
                    throw new FormatException("난독 선택지가 정상 참고 문장으로 복원됨");
    }
    private static bool Same(string[] a, string[] b)
    {
        if ((a?.Length ?? 0) != b.Length) return false;
        for (int i = 0; i < b.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
    private static void ValidateWording(string original, string changed, int limit, LocalStoryRevisionRequest memory)
    {
        if (string.IsNullOrWhiteSpace(changed) || changed.Length > limit) throw new FormatException("대사/선택지 비어 있음 또는 길이 초과");
        if (Regex.IsMatch(changed, @"^\s*[^:：\r\n]{1,20}[:：]") || Regex.IsMatch(changed, @"\b(?:Guide|Player|Duelist|Gambler)\s*(?:→|->)") || changed.Contains("<think>") || changed.Contains("```"))
            throw new FormatException("역할 접두사/추론문/코드 블록 금지");
        if (Cores(original) != Cores(changed)) throw new FormatException("{고정 문자열} 변경");
        if (Numbers(original) != Numbers(changed)) throw new FormatException("숫자 사실 변경/추가");
        if (memory?.events != null)
            foreach (var e in memory.events)
                if (!string.IsNullOrWhiteSpace(e?.terrainName) && original.Contains(e.terrainName) && !changed.Contains(e.terrainName))
                    throw new FormatException("지형 이름 변경");
        // Semantic world facts belong to the writing brief and human review, not exact-word matching.
        // Structural acceptance alone is not a claim that all generated facts are correct.
    }
    private static void ValidateGarbled(EncounterCard original, string changed)
    {
        if(string.IsNullOrWhiteSpace(original.semanticText)) return;
        // A cheap guard for literal restoration only. Semantic/phonetic quality still needs human review.
        string Meaning(string value) => Regex.Replace(value??"",@"[\s\p{P}\p{S}]","");
        if(Meaning(changed).Contains(Meaning(original.semanticText)))
            throw new FormatException("난독 연출이 정상 의미 문장으로 복원됨");
    }
    private static string Cores(string s) => string.Join("|", MatchValues(s, @"\{[^{}]*\}"));
    private static string Numbers(string s) => string.Join("|", MatchValues(s, @"\d+"));
    private static List<string> MatchValues(string s, string pattern)
    { var values = new List<string>(); foreach (Match m in Regex.Matches(s ?? "", pattern)) values.Add(m.Value); return values; }

    public static double Similarity(EncounterCardRoot a, EncounterCardRoot b)
    {
        int total = 0, distance = 0;
        for (int i = 0; i < a.MainStory.Count; i++)
        {
            var pairs = new List<(string, string)> { (a.MainStory[i].text, b.MainStory[i].text) };
            for (int j = 0; j < (a.MainStory[i].options?.Length ?? 0); j++) pairs.Add((a.MainStory[i].options[j], b.MainStory[i].options[j]));
            foreach (var p in pairs) { total += Math.Max(p.Item1.Length, p.Item2.Length); distance += EditDistance(p.Item1, p.Item2); }
        }
        return total == 0 ? 1 : 1.0 - (double)distance / total;
    }
    private static int EditDistance(string a, string b)
    {
        int[] d = new int[b.Length + 1]; for (int j = 0; j <= b.Length; j++) d[j] = j;
        for (int i = 1; i <= a.Length; i++)
        { int last = d[0]; d[0] = i; for (int j = 1; j <= b.Length; j++) { int old = d[j]; d[j] = Math.Min(Math.Min(d[j] + 1, d[j - 1] + 1), last + (a[i - 1] == b[j - 1] ? 0 : 1)); last = old; } }
        return d[b.Length];
    }

    private sealed class PatchReader
    {
        private readonly string s; private int at;
        public PatchReader(string json) { if (json == null || json.Length > 50000) throw new FormatException("출력 길이 초과"); s = json; }
        private void White() { while (at < s.Length && " \t\r\n".IndexOf(s[at]) >= 0) at++; }
        private void Take(char c) { White(); if (at >= s.Length || s[at++] != c) throw new FormatException("JSON 형식 불일치"); }
        private bool Next(char c) { White(); return at < s.Length && s[at] == c; }
        private void Key(string name) { if (String() != name) throw new FormatException("예상하지 않은 JSON 키"); Take(':'); }
        private string String()
        {
            Take('"'); var b = new StringBuilder();
            while (at < s.Length)
            {
                char c = s[at++]; if (c == '"') return b.ToString();
                if (c < 32) throw new FormatException("JSON 제어문자");
                if (c != '\\') { b.Append(c); continue; }
                if (at >= s.Length) break;
                c = s[at++];
                switch (c)
                {
                    case '"': case '\\': case '/': b.Append(c); break;
                    case 'b': b.Append('\b'); break; case 'f': b.Append('\f'); break;
                    case 'n': b.Append('\n'); break; case 'r': b.Append('\r'); break; case 't': b.Append('\t'); break;
                    case 'u': if (at + 4 > s.Length) throw new FormatException("JSON 잘림"); b.Append((char)Convert.ToInt32(s.Substring(at, 4), 16)); at += 4; break;
                    default: throw new FormatException("JSON escape 오류");
                }
            }
            throw new FormatException("JSON 문자열 잘림");
        }
        public LocalStoryPatch Read()
        {
            Take('{'); Key("cards"); Take('['); var cards = new List<LocalStoryPatchCard>();
            if (!Next(']')) do
            {
                if (cards.Count > 64) throw new FormatException("카드 수 초과");
                Take('{'); Key("index"); White(); int start = at;
                while (at < s.Length && s[at] >= '0' && s[at] <= '9') at++;
                string number = s.Substring(start, at - start);
                if (number.Length == 0 || (number.Length > 1 && number[0] == '0')) throw new FormatException("index 형식 오류");
                var card = new LocalStoryPatchCard { index = int.Parse(number) };
                Take(','); Key("text"); card.text = String(); Take(','); Key("options"); Take('[');
                var options = new List<string>();
                if (!Next(']')) do { if (options.Count >= 4) throw new FormatException("선택지 수 초과"); options.Add(String()); if (!Next(',')) break; Take(','); } while (true);
                Take(']'); Take('}'); card.options = options.ToArray(); cards.Add(card);
                if (!Next(',')) break; Take(',');
            } while (true);
            Take(']'); Take('}'); White(); if (at != s.Length) throw new FormatException("JSON 뒤 추가 내용");
            return new LocalStoryPatch { cards = cards.ToArray() };
        }
    }
}
