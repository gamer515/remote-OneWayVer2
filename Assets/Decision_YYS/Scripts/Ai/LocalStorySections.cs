using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

[Serializable] public sealed class LocalStorySectionInput
{
    // authoredTailCards is retained only to read old diagnostic/checkpoint files. New requests generate every card.
    public int start, count, authoredTailCards, seed, maxTokens;
    public string sectionId;
    public string prompt, correctionPrompt, grammar, draftParameters, correctionParameters;
    public string[] promptModules, correctionModules;
    public int promptChars, correctionPromptChars;
}
[Serializable] public sealed class LocalStorySectionOutput
{
    public int start, count, authoredTailCards;
    public string sectionId;
    public string rawOutput, correctionRawOutput, error, correctionError;
    public bool valid, correctionAttempted, correctionValid, draftSessionReused, correctionSessionReused;
    public bool reviewApplied, sourceFallback;
    public double draftSeconds, correctionSeconds, verificationSeconds, modelLoadSeconds, generationSeconds;
    public double draftFirstTextSeconds, correctionFirstTextSeconds;
    public int draftInputTokens=-1,draftOutputTokens=-1,correctionInputTokens=-1,correctionOutputTokens=-1;
    public double draftPrefillSeconds=-1,draftDecodeSeconds=-1,correctionPrefillSeconds=-1,correctionDecodeSeconds=-1;
    public bool draftTokenLimitReached,correctionTokenLimitReached;
    public string verificationProvider;
    public long verificationBytes;
}
[Serializable] public sealed class LocalStorySectionCheckpoint
{
    public string key;
    public List<LocalStorySectionInput> inputs;
    public List<LocalStorySectionOutput> outputs;
    public List<EncounterCard> accepted;
}

/// <summary>Shared gameplay/test paragraph requests; repair only the failed section by default.</summary>
public static class LocalStorySections
{
    public const string PolicyVersion = "JSON prompt modules v8";
    public static NarrativeWritingGuide WritingGuide(string path) => NarrativeInteractions.Chapter(path.Split('/')[0])?.writingGuides?.SingleOrDefault(g=>g.storyPath==path);
    public static string GenerationPolicyHash(string path)
    {
        var guide = WritingGuide(path);
        return LocalStoryDialogueQueue.Hash(PolicyVersion + "\n" + LocalStoryPrompt.Hash + "\n" + ChapterContentCatalog.Explanation(path)?.ToString(Newtonsoft.Json.Formatting.None) + "\n" + (guide == null ? "" : JsonUtility.ToJson(guide)) + "\n" + string.Join("\n", ChapterContentCatalog.Characters(path.Split('/')[0]).Select(c=>c.ToString(Newtonsoft.Json.Formatting.None))));
    }
    private static string Normalized(string text) => Regex.Replace(text ?? "", @"[\s\p{P}\p{S}]", "").ToLowerInvariant();
    private static bool ClaimsActualMeeting(string text) => Regex.IsMatch(text??"",@"오랜만|잘\s*지냈|아는\s*얼굴|다시\s*만났|또\s*만났|전에\s*만났");
    public static bool ValidateSituation(string path, EncounterCard source, string text, IEnumerable<EncounterCard> previous, out string error)
    {
        string normal = Normalized(text);
        var frame = WritingGuide(path)?.frames?.SingleOrDefault(f=>f.eventId==source.eventIdStable);
        foreach (string blocked in frame?.forbiddenMentions ?? Array.Empty<string>())
            if (normal.Contains(Normalized(blocked))) {error="현재 구간에서 언급하지 않을 상태/사건: "+blocked;return false;}
        if (normal.Length >= 10 && previous.Any(c=>Normalized(c.text)==normal)) {error="앞에서 한 대사를 그대로 반복함";return false;}
        error=null;return true;
    }
    private static bool TrySection(LocalStoryTestReport report, EncounterCardRoot source, string raw, List<EncounterCard> previous, int start,
        out EncounterCardRoot built, out string error)
    {
        if (!LocalStoryRewrite.TryBuild(raw,source,report.interactionMemory,out built,out error)) return false;
        var seen = previous.ToList();
        foreach (var card in built.MainStory)
        {
            var limits=LocalStoryPrompt.Limits(report.storyPath,LocalStoryPrompt.Paragraph(report.storyPath,report.original.MainStory[start],start));
            if(card.text.Length>limits.textMaxChars || (card.options??Array.Empty<string>()).Any(s=>s.Length>limits.optionMaxChars))
            {built=null;error="Explanation에 지정한 대사/선택지 길이 초과";return false;}
            if(report.interactionMemory?.memoryMode=="Dream" && ClaimsActualMeeting(card.text))
            { built=null;error="꿈의 인상만 있는 NPC가 실제 과거 만남을 단정함. 낯익은 느낌만 표현하고 친분/안부 인사를 만들지 않는다.";return false; }
            if (!ValidateSituation(report.storyPath,card,card.text,seen,out error)) {built=null;return false;}
            seen.Add(card);
        }
        return true;
    }
    private static bool IsBoundary(EncounterCard card) => card.type == "Choice" || card.type == "End" ||
        !string.IsNullOrWhiteSpace(card.guideAction?.kind) || !string.IsNullOrWhiteSpace(card.startAction);
    public static List<(int start, int count)> Plan(EncounterCardRoot source, int maxCards)
    {
        if (source?.MainStory == null || source.MainStory.Count == 0) throw new ArgumentException("이야기 카드가 없습니다.");
        maxCards = Math.Max(1, Math.Min(16, maxCards));
        var result = new List<(int start, int count)>();
        for (int start = 0; start < source.MainStory.Count;)
        {
            int count = 0;
            do
            {
                count++;
                if (IsBoundary(source.MainStory[start + count - 1])) break;
                if (start + count < source.MainStory.Count && IsBoundary(source.MainStory[start + count])) break;
                if (start + count < source.MainStory.Count &&
                    string.IsNullOrWhiteSpace(source.MainStory[start + count - 1].semanticText) != string.IsNullOrWhiteSpace(source.MainStory[start + count].semanticText)) break;
            } while (count < maxCards && start + count < source.MainStory.Count);
            result.Add((start, count)); start += count;
        }
        return result;
    }
    public static EncounterCardRoot Slice(EncounterCardRoot source, int start, int count)
    {
        var copy = JsonUtility.FromJson<EncounterCardRoot>(JsonUtility.ToJson(source));
        copy.MainStory = copy.MainStory.Skip(start).Take(count).ToList();
        return copy;
    }
    private static string Patch(IEnumerable<EncounterCard> cards) => new JObject { ["cards"] = new JArray(cards.Select((c,i) =>
        new JObject { ["index"] = i, ["text"] = c.text, ["options"] = new JArray(c.options ?? Array.Empty<string>()) }))
    }.ToString(Newtonsoft.Json.Formatting.None);
    private static string RawDraft(LocalStoryTestReport report)
    {
        var cards = new JArray();
        foreach (var section in report.sectionOutputs)
        {
            if (section.count > 0)
            {
                var raw = LocalStoryTestArchive.RawCards(section.rawOutput);
                if (raw == null || raw.Count != section.count)
                    return new JObject { ["incompleteSections"] = JArray.FromObject(report.sectionOutputs.Select(s => new {s.start,s.count,s.rawOutput})) }.ToString();
                for (int i = 0; i < raw.Count; i++)
                {
                    var card = (JObject)raw[i].DeepClone(); card["index"] = section.start + (int)card["index"]; cards.Add(card);
                }
            }
        }
        return new JObject { ["cards"] = cards }.ToString(Newtonsoft.Json.Formatting.None);
    }
    public static async Task Generate(LocalStoryTestReport report, CancellationToken token,
        Func<LocalStorySectionInput, bool, CancellationToken, Action<string>, Task<LocalStoryModelOutput>> infer)
    {
        var chapter = NarrativeInteractions.Chapter(report.storyPath.Split('/')[0]);
        int maxCards = LocalStoryPrompt.Limits(report.storyPath).maxCards;
        bool proofreadAll = chapter?.proofreadAllSections == true;
        int corrections=(int?)LocalStoryPrompt.Policy["runtime"]?["maxCorrectionsPerSection"]??1;
        if(corrections<0 || corrections>1)throw new InvalidOperationException("runtime.maxCorrectionsPerSection must be 0 or 1.");
        // Strict application and explicit proofreading keep their repair pass.
        bool repair=corrections>0 || !report.allowFailedOutput || proofreadAll;
        report.samplingProfile = proofreadAll ? "짧은 구간 작성 / 생성 구간 전체 교정" : repair?"짧은 구간 작성 / 검사 실패 구간만 교정":"짧은 구간 작성 / 자동 교정 생략·실패본 확인";
        report.writingPolicy = LocalStoryRewrite.WritingPolicy + " / " + PolicyVersion + " / 빈 대사 제한 v1";
        report.generationMode = proofreadAll ? "SectionsWithProofreading" : repair?"SectionsWithTargetedRepair":"SectionsWithoutRepair";
        report.prompt = "구간별 독립 생성. 실제 모델 요청과 토큰 예산은 sectionInputs에서 확인합니다.";
        report.sectionInputs = new List<LocalStorySectionInput>(); report.sectionOutputs = new List<LocalStorySectionOutput>();
        report.sectionsGenerated = report.sectionsCorrected = report.sectionsResumed = report.authoredCards = 0;
        var accepted = new List<EncounterCard>();
        var plan = LocalStoryPrompt.Plan(report.storyPath, report.original);
        report.paragraphsPlanned=WritingGuide(report.storyPath).paragraphs.Length;
        string key = LocalStoryDialogueQueue.Hash(report.writingPolicy + "\n" + JsonUtility.ToJson(report.original) + "\n" + report.model + "\n" + report.generationIdentity + "\n" + report.seed +
            "\n" + JsonUtility.ToJson(report.interactionMemory) + "\n" + report.examplePrompt + "\n" + maxCards + ":" + proofreadAll + ":" + report.allowFailedOutput + ":" + GenerationPolicyHash(report.storyPath));
        var checkpoint = report.sectionCheckpoint;
        EncounterCardRoot restored=null;
        if (checkpoint?.key == key && checkpoint.inputs?.Count > 0 && checkpoint.inputs.Count == checkpoint.outputs?.Count && checkpoint.inputs.Count <= plan.Count &&
            checkpoint.inputs.Select((s,i) => s.start == plan[i].start && s.count == plan[i].count && s.authoredTailCards == 0).All(s => s) &&
            checkpoint.accepted?.Count == checkpoint.inputs.Sum(s => s.count) &&
            checkpoint.outputs.All(s => (s.correctionAttempted ? s.correctionValid : s.valid) || (report.allowFailedOutput && (s.reviewApplied||s.sourceFallback))) &&
            (report.allowFailedOutput ? LocalStoryRewrite.TryBuildForReview(Patch(checkpoint.accepted),Slice(report.original,0,checkpoint.accepted.Count),out restored,out _) :
                LocalStoryRewrite.TryBuild(Patch(checkpoint.accepted),Slice(report.original,0,checkpoint.accepted.Count),report.interactionMemory,out restored,out _)))
        {
            accepted.AddRange(restored.MainStory);
            report.sectionInputs.AddRange(checkpoint.inputs); report.sectionOutputs.AddRange(checkpoint.outputs);
            report.sectionsResumed = checkpoint.inputs.Count;
            report.correctionAttempted = checkpoint.outputs.Any(s => s.correctionAttempted);
        }
        try
        {
            foreach (var range in plan.Skip(report.sectionsResumed))
            {
                token.ThrowIfCancellationRequested();
                if (report.sectionStillCurrent?.Invoke() == false) throw new InvalidOperationException("현재 요청 또는 수동 적용 상태가 바뀌어 생성을 보류합니다.");
                var source = Slice(report.original, range.start, range.count);
                string sectionId = report.original.MainStory[range.start].eventIdStable;
                sectionId = LocalStoryPrompt.Paragraph(report.storyPath,report.original.MainStory[range.start],range.start).id + "/" + (sectionId ?? range.start.ToString());
                var input = new LocalStorySectionInput {start = range.start, count = source.MainStory.Count, seed = report.seed + range.start,
                    sectionId = sectionId,
                    maxTokens = LocalStoryPrompt.MaxTokens(report.storyPath, report.original, range.start, source.MainStory.Count)};
                report.sectionInputs.Add(input);
                var output = new LocalStorySectionOutput {start = range.start, count = input.count, sectionId = sectionId};
                report.sectionOutputs.Add(output);
                void SaveProgress()
                {
                    report.saveSectionCheckpoint?.Invoke(new LocalStorySectionCheckpoint {key = key,
                        inputs = report.sectionInputs.ToList(), outputs = report.sectionOutputs.ToList(), accepted = accepted.ToList()});
                }
                input.grammar = LocalStoryRewrite.Grammar(source, requireNonEmpty: true,
                    limits:LocalStoryPrompt.Limits(report.storyPath,LocalStoryPrompt.Paragraph(report.storyPath,report.original.MainStory[range.start],range.start)));
                report.generationGrammar=input.grammar;
                input.prompt = Wrap(report, input, LocalStoryRewrite.SectionMessages(report.storyPath, report.original, range.start, input.count, accepted, report.interactionMemory));
                input.promptModules=LocalStoryPromptModules.Sources(input.prompt);input.promptChars=input.prompt.Length;
                input.draftParameters = LocalStorySampling.Comparison().WithTokenLimit(input.maxTokens).Parameters(input.seed);
                report.draftParameters=input.draftParameters;
                var watch = Stopwatch.StartNew();
                LocalStoryLog.Generation(LocalStoryLogStage.Working, report,"카드 " + (range.start + 1) + "~" + (range.start + range.count) + " 작성");
                report.sectionProgress?.Invoke(range.start,range.count,report.sectionInputs.Count,plan.Count,"작성 중");
                try
                {
                    var draft = await infer(input, false, token, s => output.rawOutput = s);
                    output.rawOutput = draft.json; output.draftSessionReused = draft.sessionReused;
                    Record(report, output, draft, false); report.sectionsGenerated++;
                    LocalStoryLog.Generation(LocalStoryLogStage.Working,report,"카드 "+(range.start+1)+"~"+(range.start+range.count)+" 응답 수신 / "+draft.backend);
                }
                finally { output.draftSeconds = watch.Elapsed.TotalSeconds; report.draftSeconds += output.draftSeconds; }
                output.valid = TrySection(report,source,output.rawOutput,accepted,range.start,out var built,out output.error);
                if(output.valid && !LocalStoryPromptModules.ValidateExampleCopies(source,built,input.prompt,out output.error))output.valid=false;
                if ((!output.valid || proofreadAll) && repair)
                {
                    output.correctionAttempted = report.correctionAttempted = true;
                    input.correctionPrompt = Wrap(report, input, LocalStoryRewrite.SectionMessages(report.storyPath, report.original,
                        range.start, input.count, accepted, report.interactionMemory, output.rawOutput, output.error));
                    input.correctionModules=LocalStoryPromptModules.Sources(input.correctionPrompt);input.correctionPromptChars=input.correctionPrompt.Length;
                    input.correctionParameters = LocalStorySampling.Proofreading().WithTokenLimit(input.maxTokens).Parameters(input.seed);
                    report.correctionParameters=input.correctionParameters;
                    report.correctionPrompt = "필요한 구간만 교정합니다. 실제 요청은 sectionInputs의 correctionPrompt에서 확인합니다.";
                    report.sectionsCorrected++; watch.Restart();
                    LocalStoryLog.Generation(LocalStoryLogStage.Working, report,"카드 " + (range.start + 1) + "~" + (range.start + range.count) + " 구간 교정");
                    report.sectionProgress?.Invoke(range.start,range.count,report.sectionInputs.Count,plan.Count,"교정 중");
                    try
                    {
                        var correction = await infer(input, true, token, s => output.correctionRawOutput = s);
                        output.correctionRawOutput = correction.json; output.correctionSessionReused = correction.sessionReused;
                        Record(report, output, correction, true);
                        LocalStoryLog.Generation(LocalStoryLogStage.Working,report,"카드 "+(range.start+1)+"~"+(range.start+range.count)+" 교정 응답 수신 / "+correction.backend);
                        output.correctionValid = TrySection(report,source,output.correctionRawOutput,accepted,range.start,out built,out output.correctionError);
                        if(output.correctionValid && !LocalStoryPromptModules.ValidateExampleCopies(source,built,input.correctionPrompt,out output.correctionError))output.correctionValid=false;
                    }
                    finally { output.correctionSeconds = watch.Elapsed.TotalSeconds; report.correctionSeconds += output.correctionSeconds; }
                    if (!output.correctionValid)
                    {
                        if(!report.allowFailedOutput)throw new InvalidOperationException("구간 교정 실패: " + output.correctionError);
                        output.reviewApplied=LocalStoryRewrite.TryBuildForReview(output.correctionRawOutput,source,out built,out _) ||
                            LocalStoryRewrite.TryBuildForReview(output.rawOutput,source,out built,out _);
                        output.sourceFallback=!output.reviewApplied;
                        if(output.sourceFallback)built=source;
                        LocalStoryLog.Generation(LocalStoryLogStage.Failed,report,"카드 "+(range.start+1)+"~"+(range.start+range.count)+" / "+
                            (output.reviewApplied?"검사 실패 문구 반영, 다음 구간 계속":"출력 형식 불일치로 이 구간 원문 유지, 다음 구간 계속")+" / "+output.correctionError);
                    }
                }
                else if(!output.valid)
                {
                    output.reviewApplied=LocalStoryRewrite.TryBuildForReview(output.rawOutput,source,out built,out _);
                    output.sourceFallback=!output.reviewApplied;
                    if(output.sourceFallback)built=source;
                }
                accepted.AddRange(built.MainStory);
                SaveProgress();
            }
        }
        finally
        {
            report.rawOutput = RawDraft(report);
            LocalStoryRewrite.EvaluateDraft(report, report.original, report.interactionMemory);
            if (report.sectionOutputs.Any(s=>!s.valid))
            {
                report.valid=false; report.generated=null;
                report.error=report.sectionOutputs.First(s=>!s.valid).error;
            }
            if (report.correctionAttempted)
            {
                // This is an explicitly assembled patch: corrected sections + untouched successful drafts.
                report.correctionRawOutput = Patch(accepted);
                report.correctionValid = LocalStoryRewrite.TryBuild(report.correctionRawOutput, report.original, report.interactionMemory,
                    out report.corrected, out report.correctionError);
            }
            report.reviewRequired=report.sectionOutputs.Any(s=>s.reviewApplied||s.sourceFallback);
            if(report.reviewRequired)
            {
                report.reviewGeneratedCards=accepted.Count-report.sectionOutputs.Where(s=>s.sourceFallback).Sum(s=>s.count);
                report.reviewError=string.Join(" / ",report.sectionOutputs.Where(s=>s.reviewApplied||s.sourceFallback)
                    .Select(s=>"카드 "+(s.start+1)+"~"+(s.start+s.count)+": "+(s.correctionError??s.error)+(s.sourceFallback?" (표시 불가, 원문 유지)":" (실패 문구 반영)")));
                if(accepted.Count==report.original.MainStory.Count && report.sectionOutputs.Any(s=>s.valid||s.correctionValid||s.reviewApplied))
                    report.reviewRawOutput=Patch(accepted);
                report.correctionValid=false;report.corrected=null;report.correctionError=report.reviewError;
            }
        }
    }
    private static string Wrap(LocalStoryTestReport report, LocalStorySectionInput input, string messages)
    {
        var parsed = JArray.Parse(messages);
        return LocalStoryTestModels.Messages(LocalStoryTestModels.ModelFor(report),
            LocalStoryTestArchive.AttachWritingExamples(parsed.ToString(Newtonsoft.Json.Formatting.None), ScopedExample(report, input)));
    }
    private static string ScopedExample(LocalStoryTestReport report, LocalStorySectionInput input)
    {
        if((bool?)LocalStoryPrompt.Policy["composition"]["useEditedExamples"]!=true)return "";
        if (report.original.MainStory.Skip(input.start).Take(input.count).Any(c=>!string.IsNullOrWhiteSpace(c.semanticText))) return "";
        string text = report.examplePrompt ?? ""; int start = text.IndexOf('[');
        if (start < 0) return text;
        try
        {
            var samples = JArray.Parse(text.Substring(start));
            if(report.interactionMemory?.memoryMode=="Dream")
                samples=new JArray(samples.Where(s=>!ClaimsActualMeeting((string)s["text"])));
            var source = report.original.MainStory[input.start];
            samples = new JArray(samples.Where(s=>ValidateSituation(report.storyPath,source,(string)s["text"],Array.Empty<EncounterCard>(),out _)).Select(s=>s.DeepClone()));
            var matching = samples.Where(s => (string)s["eventId"] == source.eventIdStable && !string.IsNullOrEmpty(source.eventIdStable)).ToList();
            if (matching.Count > 0) samples = new JArray(matching);
            else samples = new JArray(samples.Where(s=>string.IsNullOrEmpty((string)s["eventId"])));
            if (samples.Count == 0) return "";
            int pick = (int)(unchecked((uint)report.seed * 2654435761u + (uint)input.start) % (uint)samples.Count);
            return (string)LocalStoryPrompt.Policy["composition"]["editedExamplesInstruction"]+"\n" +
                new JArray(new JObject { ["text"] = samples[pick]["text"].DeepClone() }).ToString(Newtonsoft.Json.Formatting.None);
        }
        catch (Newtonsoft.Json.JsonException) { return text; }
    }
    private static void Record(LocalStoryTestReport report, LocalStorySectionOutput section, LocalStoryModelOutput output, bool correction)
    {
        section.modelLoadSeconds += output.modelLoadSeconds; section.generationSeconds += output.generationSeconds;
        section.verificationSeconds += output.verificationSeconds;
        section.verificationProvider = output.verificationProvider; section.verificationBytes += output.verificationBytes;
        report.verificationProvider = output.verificationProvider; report.verificationBytes += output.verificationBytes;
        report.backend = output.backend; report.fallback = output.fallback;
        if (correction)
        {
            section.correctionFirstTextSeconds=output.firstTextSeconds;
            section.correctionInputTokens=output.inputTokens;section.correctionOutputTokens=output.outputTokens;
            section.correctionPrefillSeconds=output.prefillSeconds;section.correctionDecodeSeconds=output.decodeSeconds;section.correctionTokenLimitReached=output.tokenLimitReached;
            report.correctionBackend = output.backend; report.correctionFallback = output.fallback;
            report.correctionSessionReused = output.sessionReused;
            report.correctionModelLoadSeconds += output.modelLoadSeconds; report.correctionGenerationSeconds += output.generationSeconds;
            report.correctionVerificationSeconds += output.verificationSeconds;
            if (report.sectionsCorrected == 1) report.correctionFirstTextSeconds = output.firstTextSeconds;
        }
        else
        {
            section.draftFirstTextSeconds=output.firstTextSeconds;
            section.draftInputTokens=output.inputTokens;section.draftOutputTokens=output.outputTokens;
            section.draftPrefillSeconds=output.prefillSeconds;section.draftDecodeSeconds=output.decodeSeconds;section.draftTokenLimitReached=output.tokenLimitReached;
            if (report.sectionsGenerated == 0) { report.draftSessionReused = output.sessionReused; report.draftFirstTextSeconds = output.firstTextSeconds; }
            report.draftModelLoadSeconds += output.modelLoadSeconds; report.draftGenerationSeconds += output.generationSeconds;
            report.draftVerificationSeconds += output.verificationSeconds;
        }
    }
}

public static partial class LocalStoryRewrite
{
    public static string SectionMessages(string path, EncounterCardRoot original, int start, int count,
        List<EncounterCard> accepted, LocalStoryRevisionRequest memory, string draft = null, string error = null) =>
        LocalStoryPrompt.Messages(path,original,start,count,accepted,memory,draft,error);
}
