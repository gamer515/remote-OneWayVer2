using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>Test-only evidence with per-result edits and evaluation. Never used as game content.</summary>
public static partial class LocalStoryTestArchive
{
    private static readonly object Sync = new object();
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
    public static string Root => Path.Combine(Application.persistentDataPath, "Saves", "LocalStoryTests");
    public static string ComparisonsRoot => Path.Combine(Root, "Comparisons");
    public static string NumberedEditPath(string reportFile, JObject report = null)
    {
        reportFile = Path.GetFullPath(reportFile);
        if(reportFile.EndsWith(".Result.json",StringComparison.Ordinal))
        {
            report ??= ReadReport(reportFile);
            return Path.Combine(TestRoot(reportFile),"Edits",Group(report),((int)report["archiveNumber"]).ToString("D4")+".json");
        }
        report ??= ReadReport(reportFile);
        string root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(reportFile)));
        string model = ((string)report["model"] ?? "").IndexOf("Gemma", StringComparison.OrdinalIgnoreCase) >= 0 ? "Gemma" :
            ((string)report["model"] ?? "").IndexOf("Qwen", StringComparison.OrdinalIgnoreCase) >= 0 ? "Qwen" : Slug((string)report["model"]);
        string[] parts = ((string)report["storyPath"] ?? "UnknownStory").Split('/');
        int encounter = Array.IndexOf(parts, "Encounters");
        string story = parts[0] + (encounter >= 0 ? "-" + string.Join("-", parts.Skip(encounter + 1)) : "-" + string.Join("-", parts.Skip(1)));
        string scenario = ((string)report["testScenario"] ?? "실제 기록").Split('/').LastOrDefault()?.Trim();
        // Writing-policy versions belong in Input; the human filename identifies the test situation.
        var scenarios = ((string)report["testScenario"] ?? "").Split('/');
        if (scenarios.Length > 1 && scenarios[0].Trim() == "Synthetic") scenario = scenarios[1].Trim();
        int number = (int?)report["archiveNumber"] ?? 0;
        if (number <= 0) throw new InvalidOperationException("Edit 결과 번호가 없습니다.");
        return Path.Combine(root, "Edits", number.ToString("D4") + "__" + model + "__" + Slug(story) + "__" + Slug(scenario) + ".json");
    }
    public static string EditPath(string reportFile, string stage = "draft")
    {
        string numbered = NumberedEditPath(reportFile);
        string old = Path.Combine(Path.GetDirectoryName(reportFile), "Edit.json");
        // Existing edits are only moved by the explicit, backed-up migration.
        return !File.Exists(numbered) && File.Exists(old) ? old : numbered;
    }


    public static string Slug(string value)
    {
        string slug = Regex.Replace(value ?? "unknown", @"[^a-zA-Z0-9가-힣_.-]+", "-").Trim('-','.');
        return string.IsNullOrEmpty(slug) ? "unknown" : slug.Substring(0, Math.Min(100, slug.Length));
    }
    private static string Sha(string text)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Utf8.GetBytes(text ?? ""))).Replace("-", "").ToLowerInvariant();
    }
    private static string Short(string value, int length)
    {
        string slug = Slug(value);
        return slug.Substring(0, Math.Min(length, slug.Length));
    }
    private static JObject Read(string file) => JObject.Parse(File.ReadAllText(file, Utf8));
    private static void Write(string file, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        string temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, contents, Utf8);
        if (File.Exists(file))
        {
            for(int attempt=0;;attempt++)
            {
                try { File.Replace(temporary, file, null); break; }
                catch (IOException) when (Path.GetFileName(file) == "Index.html")
                {
                    // Derived view may be open without delete sharing. Result/edit writes stay atomic.
                    File.Copy(temporary, file, true);
                    File.Delete(temporary); break;
                }
                catch(IOException) when(attempt<3 && File.Exists(temporary) && File.Exists(file))
                {
                    // Windows can briefly hold a just-written result/sequence file. Retry the atomic replace only.
                    System.Threading.Thread.Sleep(20<<attempt);
                }
            }
        }
        else File.Move(temporary, file);
    }
    private static void Write(string file, JToken value) => Write(file, value.ToString(Formatting.Indented) + "\n");

    public static List<string> List(string root = null)
    {
        string results = Path.Combine(root ?? Root, "Results");
        if (!Directory.Exists(results)) return new List<string>();
        return Directory.GetFiles(results,"*.Result.json",SearchOption.AllDirectories).Concat(Directory.GetFiles(results, "Result.json", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(results, "*.report.json", SearchOption.AllDirectories)
                .Where(p => !File.Exists(Path.Combine(Path.GetDirectoryName(p), "Result.json"))))
            .OrderBy(p => p, StringComparer.Ordinal).ToList();
    }
    public static JObject ReadReport(string file)
    {
        if(file.EndsWith(".Result.json",StringComparison.Ordinal)) return ReadGrouped(file);
        if (Path.GetFileName(file) != "Result.json")
        {
            if (File.Exists(file)) return Read(file);
            file = Path.Combine(Path.GetDirectoryName(file), "Result.json");
        }
        var report = new JObject();
        foreach (string name in new[] { "Input.json", "Result.json", "Analysis.json" })
        foreach (var property in Read(Path.Combine(Path.GetDirectoryName(file), name)).Properties())
        {
            if (new[] { "interactionSummary", "draftCards", "correctionCards", "humanReviews" }.Contains(property.Name)) continue;
            if (report.ContainsKey(property.Name)) throw new InvalidOperationException("Duplicate evidence field: " + property.Name);
            report.Add(property.Name, property.Value.DeepClone());
        }
        return report;
    }
    public static LocalStoryTestReport Load(string file)
    {
        var report=JsonUtility.FromJson<LocalStoryTestReport>(ReadReport(file).ToString(Formatting.None));
        if(report.original!=null)
        {
            if(report.valid && (report.generated?.MainStory==null||report.generated.MainStory.Count==0) && LocalStoryRewrite.TryBuild(report.rawOutput,report.original,report.interactionMemory,out var draft,out _))report.generated=draft;
            if(report.correctionValid && (report.corrected?.MainStory==null||report.corrected.MainStory.Count==0) && LocalStoryRewrite.TryBuild(report.correctionRawOutput,report.original,report.interactionMemory,out var corrected,out _))report.corrected=corrected;
        }
        return report;
    }

    // raw JSON parsing is independent of automatic quality validation. A failed quality check must still be visible.
    public static JArray RawCards(string raw)
    {
        try
        {
            var json = JObject.Parse(raw ?? "", new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            var cards = json["cards"] as JArray;
            if (cards == null || cards.Any(c => !(c is JObject) || c["text"]?.Type != JTokenType.String ||
                c["index"]?.Type != JTokenType.Integer || !(c["options"] is JArray options) || options.Any(o => o.Type != JTokenType.String))) return null;
            return cards;
        }
        catch (JsonException) { return null; }
    }
    public static string DialogueText(string raw)
    {
        var cards = RawCards(raw);
        if (cards == null) return "[JSON 판독 불가 — 원출력 그대로]\n" + (raw ?? "");
        var text = new StringBuilder();
        foreach (JObject card in cards)
        {
            text.AppendLine(((long)card["index"] + 1) + ". " + (string)card["text"]);
            int option = 0;
            foreach (string value in (JArray)card["options"]) text.AppendLine("   " + (++option) + ") " + value);
            text.AppendLine();
        }
        return text.ToString();
    }

    private static readonly HashSet<string> InputFields = new HashSet<string>(new[] {
        "storyPath", "original", "prompt", "correctionPrompt", "testScenario", "sourceRun", "targetRun", "interactionMemory", "interactionSource", "writingPolicy", "generationGrammar", "examplePrompt", "exampleSummary", "exampleSources", "comparisonMode", "generationMode", "sectionInputs"
    });
    private static readonly HashSet<string> OutputFields = new HashSet<string>(new[] {
        "rawOutput", "correctionRawOutput", "generated", "corrected", "sectionOutputs"
    });
    private static string PromptText(string prompt)
    {
        try { return string.Join("\n\n", JArray.Parse(prompt ?? "[]").Select(m => (string)m["content"])); }
        catch (JsonException) { return prompt ?? ""; }
    }
    // Test-only few-shot context: human differences, not self-generated examples or learned weights.
    public static string CaptureWritingExamples(string storyPath, out string summary, out string[] sources, string root = null, int? seed = null)
    {
        root = Path.GetFullPath(root ?? Root);
        if(storyPath==LocalStoryTestInputs.GuidePath) return CaptureGuideSceneExample(root,out summary,out sources,seed);
        var candidates = new List<(JObject example,string source,bool choice)>();
        var seen = new HashSet<string>(); int skipped = 0;
        foreach (string result in ReferenceOrder(root,seed))
        {
            try
            {
                var report = ReadReport(result);
                if ((string)report["storyPath"] != storyPath) continue;
                string file = EditPath(result);
                if (!File.Exists(file)) continue;
                string contents = File.ReadAllText(file, Utf8), fingerprint = Sha(contents);
                var edit = ReadEditData(file,report);
                foreach (string stage in new[] { "correction", "draft" })
                {
                    var raw = RawCards((string)report[stage == "draft" ? "rawOutput" : "correctionRawOutput"]);
                    if (raw == null || !(edit[stage] is JArray cards)) continue;
                    var rawTexts = new HashSet<string>(raw.Select(c => ((string)c["text"] ?? "").Trim()));
                    var rawChoices = new HashSet<string>(raw.SelectMany(c => ((JArray)c["options"]).Values<string>()).Select(s => s.Trim()));
                    for (int i = 0; i < cards.Count; i++)
                    {
                        if (!(cards[i] is JObject card) || card["speaker"]?.Type != JTokenType.String || card["text"]?.Type != JTokenType.String ||
                            card.Properties().Any(p => !new[] { "speaker", "text", "options" }.Contains(p.Name))) continue;
                        var choices = card["options"] as JArray;
                        if (card["options"] != null && (choices == null || choices.Any(c => c.Type != JTokenType.String))) continue;
                        string speaker = (string)card["speaker"], text = (string)card["text"];
                        string source = Path.GetRelativePath(root, file).Replace('\\','/') + "#" + stage + "[" + i + "]; SHA256=" + fingerprint;
                        if (!string.IsNullOrWhiteSpace(speaker) && speaker.Length <= 30 && !string.IsNullOrWhiteSpace(text) && text.Length <= 160 && !rawTexts.Contains(text.Trim()) && seen.Add("text:" + text.Trim()))
                        {
                            var example = new JObject { ["speaker"] = speaker, ["text"] = text };
                            var original = report["original"]?["MainStory"] as JArray;
                            if (original != null && cards.Count == original.Count && !string.IsNullOrEmpty((string)original[i]["semanticText"]))
                                example["delivery"] = "의도적으로 발음을 흐트러뜨리되 뜻을 조금 눈치챌 수 있는 난독";
                            candidates.Add((example,source,false));
                        }
                        var changed = new JArray();
                        foreach (string choice in choices ?? new JArray())
                            if (!string.IsNullOrWhiteSpace(choice) && choice.Length <= 45 && !rawChoices.Contains(choice.Trim()) && seen.Add("choice:" + choice.Trim())) changed.Add(choice);
                        if (changed.Count > 0) candidates.Add((new JObject { ["speaker"] = "플레이어", ["options"] = new JArray(changed.Take(4)) },source,true));
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is JsonException || e is InvalidOperationException || e is UnauthorizedAccessException)
            { skipped++; } // A partially saved edit must not stop inference or replace its output.
        }
        var selected = candidates.Where(c => c.choice).Take(3).Concat(candidates.Where(c => !c.choice).Take(3)).ToList();
        sources = selected.Select(c => c.source).ToArray();
        summary = "수정 문장 예시 " + selected.Count + "개" + (skipped == 0 ? "" : " / 읽기 실패 " + skipped + "개 건너뜀");
        if (selected.Count == 0) return "";
        return (string)LocalStoryPrompt.Policy["composition"]["editedExamplesInstruction"]+"\n" + new JArray(selected.Select(c => c.example)).ToString(Formatting.Indented);
    }
    // Runtime uses actual edited bridge lines; the comparison UI keeps its complete-scene reference contract.
    public static string CaptureRuntimeWritingExamples(string storyPath, out string summary, out string[] sources, string root, int? seed = null)
    {
        root = Path.GetFullPath(root);
        var selected = new List<(JObject example, string source)>();
        var seen = new HashSet<string>(); int skipped = 0;
        foreach (string result in ReferenceOrder(root, seed))
        {
            try
            {
                var record = ReadReport(result);
                if ((string)record["storyPath"] != storyPath) continue;
                string file = EditPath(result);
                if (!File.Exists(file)) continue;
                string contents = File.ReadAllText(file, Utf8);
                var edit = ReadEditData(file,record);
                var report = JsonUtility.FromJson<LocalStoryTestReport>(record.ToString(Formatting.None));
                foreach (string stage in new[] { "correction", "draft" })
                {
                    var raw = RawCards((string)record[stage == "draft" ? "rawOutput" : "correctionRawOutput"]);
                    if (raw == null || !(edit[stage] is JArray lines) || lines.Count != raw.Count ||
                        !raw.Select((c, i) => (long)c["index"] == i).All(inOrder => inOrder)) continue;
                    var built = LocalStoryGameApply.Build(report, stage, true, file);
                    for (int i = 0; i < built.MainStory.Count; i++)
                    {
                        var card = built.MainStory[i];
                        if (!string.IsNullOrWhiteSpace(card.semanticText) ||
                            string.IsNullOrWhiteSpace(card.text) || card.text.Trim() == ((string)raw[i]["text"] ?? "").Trim() ||
                            !seen.Add(card.text.Trim())) continue;
                        selected.Add((new JObject { ["eventId"] = card.eventIdStable, ["text"] = card.text },
                            Path.GetRelativePath(root, file).Replace('\\', '/') + "#" + stage + "[" + i + "]; SHA256=" + Sha(contents)));
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is JsonException || e is InvalidOperationException ||
                e is UnauthorizedAccessException || e is FormatException || e is ArgumentException) { skipped++; }
            if (selected.Count >= 3) break;
        }
        selected = selected.Take(3).ToList();
        sources = selected.Select(s => s.source).ToArray();
        summary = "수정한 연결 대사 참고 " + selected.Count + "문장" + (skipped == 0 ? "" : " / 읽기 실패 " + skipped + "개 건너뜀");
        return selected.Count == 0 ? "" : (string)LocalStoryPrompt.Policy["composition"]["editedExamplesInstruction"]+"\n" +
            new JArray(selected.Select(s => s.example)).ToString(Formatting.None);
    }
    // Guide is a small ordered scene, not a bag of interchangeable dialogue/choice snippets.
    // Only an edited row in every slot qualifies; untouched generated files never count as authored references.
    private static System.Collections.Generic.IEnumerable<string> ReferenceOrder(string root,int? seed)
    {
        var paths=List(root).AsEnumerable().Reverse();
        return seed.HasValue ? paths.OrderBy(p=>Sha(seed.Value+":"+Path.GetRelativePath(root,p)),StringComparer.Ordinal) : paths;
    }
    private static string CaptureGuideSceneExample(string root,out string summary,out string[] sources,int? seed=null)
    {
        int skipped=0;
        foreach(string result in ReferenceOrder(root,seed))
        {
            try
            {
                var report=ReadReport(result);
                if((string)report["storyPath"]!=LocalStoryTestInputs.GuidePath) continue;
                string editFile=EditPath(result);
                if(!File.Exists(editFile)) continue;
                string contents=File.ReadAllText(editFile,Utf8);
                var edit=ReadEditData(editFile,report);
                foreach(string stage in new[]{"correction","draft"})
                {
                    var raw=RawCards((string)report[stage=="draft"?"rawOutput":"correctionRawOutput"]);
                    if(raw==null || !(edit[stage] is JArray lines) || raw.Count!=lines.Count || lines.Count==0) continue;
                    bool allRowsEdited=true;
                    for(int i=0;i<lines.Count;i++)
                    {
                        if(!(lines[i] is JObject line) || line["text"]?.Type!=JTokenType.String)
                        { allRowsEdited=false; break; }
                        string text=(string)line["text"];
                        string options=(line["options"] as JArray??new JArray()).ToString(Formatting.None);
                        string rawOptions=((JArray)raw[i]["options"]).ToString(Formatting.None);
                        if(text.Trim()==((string)raw[i]["text"]??"").Trim() && options==rawOptions)
                        { allRowsEdited=false; break; }
                    }
                    if(!allRowsEdited) continue;
                    // Check the real source's speaker/order/option/action bindings without writing a game file.
                    var typed=JsonUtility.FromJson<LocalStoryTestReport>(report.ToString(Formatting.None));
                    var built=LocalStoryGameApply.Build(typed,stage,true,editFile);
                    var example=new JObject{["cards"]=new JArray(built.MainStory.Select((c,i)=>new JObject{
                        ["index"]=i,["text"]=c.text,["options"]=new JArray(c.options??Array.Empty<string>())}))};
                    sources=new[]{Path.GetRelativePath(root,editFile).Replace('\\','/')+"#"+stage+"[0.."+(lines.Count-1)+"]; SHA256="+Sha(contents)};
                    summary="수정한 완성 대화 1묶음 / "+lines.Count+"카드";
                    return (string)LocalStoryPrompt.Policy["composition"]["sceneExamplesInstruction"]+"\n" + example.ToString(Formatting.None);
                }
            }
            catch(Exception e) when(e is IOException || e is JsonException || e is InvalidOperationException ||
                e is UnauthorizedAccessException || e is FormatException || e is ArgumentException)
            { skipped++; }
        }
        sources=Array.Empty<string>();
        summary="수정된 완성 대화 없음"+(skipped==0?"":" / 읽기 실패 "+skipped+"개 건너뜀");
        return "";
    }
    public static string AttachWritingExamples(string messages, string captured)
    {
        if (string.IsNullOrEmpty(captured)) return messages;
        var input = JArray.Parse(messages);
        if (input.Count == 0 || (string)input.Last["role"] != "user" || input.Last["content"]?.Type != JTokenType.String)
            throw new InvalidOperationException("수정 예시를 연결할 사용자 입력이 없습니다.");
        // Keep the two-message adapter contract for Gemma; the current request stays last.
        input.Last["content"] = (string)input.Last["content"] + "\n\n"+(string)LocalStoryPrompt.Policy["composition"]["editedExamplesHeading"]+"\n" + captured;
        return input.ToString(Formatting.None);
    }
    private static JObject Interaction(JObject report)
    {
        string scenario = (string)report["testScenario"] ?? "";
        string source = (string)report["interactionSource"];
        if (string.IsNullOrEmpty(source)) source = scenario.IndexOf("ActualRecord", StringComparison.OrdinalIgnoreCase) >= 0
            ? "실제 플레이 기록을 입력으로 선택한 시험" : "시험용 입력 / 과거 보고서";
        var lines = PromptText((string)report["prompt"]).Split('\n').Where(l =>
            l.StartsWith("기억 사실:") || l.StartsWith("지형=") || l.StartsWith("현재 능력치") || l.StartsWith("지난 회차 종료 능력치") ||
            l.StartsWith("생성 요청 시점 능력치") || l.StartsWith("인물=") && l.Contains("꿈의 인상=") ||
            l.StartsWith("기억 조건을 통과한 인상") ||
            l.StartsWith("체력/민첩/지식/매력:") || l.StartsWith("회차 ") || l.StartsWith("과거 선택:") ||
            l.StartsWith("고정 지형") || l.StartsWith("확정된 이전 만남:") || l.StartsWith("과거 기록 없음") ||
            l.StartsWith("기록 없음") || l.StartsWith("첫 카드:") || l.StartsWith("이 카드의 첫 문장에 반드시 넣을 기억 반응:")).Distinct();
        return new JObject {
            ["source"] = source, ["recordedContext"] = new JArray(lines),
            ["emotionNote"] = (string)report["interactionMemory"]?["memoryMode"]=="Dream"?
                "현재 플레이 전체 기록과 다음 회차의 꿈 인상은 분리됩니다. 선택 조건을 통과한 인상만 전달하며 확정된 과거 만남이 아닙니다.":
                "별도 감정값이 기록되지 않았다면 추측하지 않습니다. 관계변화·선택과 당시 프롬프트의 감정/말투 지시를 확인하세요."
        };
    }
    private static string EditSpeaker(JObject report, int index, string supplied = null)
    {
        string speaker = supplied;
        var original = report["original"]?["MainStory"] as JArray;
        if (string.IsNullOrWhiteSpace(speaker) && original != null && index >= 0 && index < original.Count)
            speaker = (string)original[index]["speakerId"];
        if (string.IsNullOrWhiteSpace(speaker))
        {
            string path = (string)report["storyPath"] ?? "";
            speaker = path.Contains("/guide") ? "Guide" : path.Contains("/duelist") ? "Duelist" : path.Contains("/gambler") ? "Gambler" : "NPC";
        }
        // Only known character IDs are resolved. Human-authored names remain unchanged.
        if (speaker == "Guide" || speaker == "Duelist" || speaker == "Gambler")
        {
            var character = ChapterContentCatalog.Asset("Story_Json_Data/Characters/" + speaker);
            string name = character == null ? null : (string)JObject.Parse(character.text)["name"];
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }
        return speaker;
    }
    private static JObject EditLine(string speaker, JToken text, JArray options = null)
    {
        if (text?.Type != JTokenType.String) throw new JsonException("Edit 지문은 문자열이어야 합니다.");
        var line = new JObject { ["speaker"] = speaker, ["text"] = text.DeepClone() };
        if (options != null && options.Count > 0)
        {
            if (options.Any(o => o.Type != JTokenType.String)) throw new JsonException("Edit 선택지는 문자열이어야 합니다.");
            line["options"] = options.DeepClone();
        }
        return line;
    }
    private static JArray BuildEdit(JObject report, string stage)
    {
        string raw = (string)report[stage == "draft" ? "rawOutput" : "correctionRawOutput"];
        var cards = RawCards(raw);
        var edits = new JArray();
        if (cards != null) foreach (JObject card in cards)
        {
            edits.Add(EditLine(EditSpeaker(report, (int)card["index"]), card["text"], (JArray)card["options"]));
        }
        return edits;
    }
    /// <summary>Explicit edit-only conversion. Caller must back up old context before saving.</summary>
    public static bool SimplifyEdit(JObject edit, JObject report)
    {
        var converted = (JObject)edit.DeepClone();
        bool changed = false;
        foreach (var property in edit.Properties())
        {
            string stage = property.Name;
            if (stage != "draft" && stage != "correction") throw new JsonException("알 수 없는 Edit 영역을 보존해야 합니다: " + stage);
            if (property.Value is JArray lines)
            {
                foreach (var line in lines)
                    if (!(line is JObject obj) || obj.Properties().Any(p => !new[] { "speaker", "text", "options" }.Contains(p.Name)) ||
                        obj["speaker"]?.Type != JTokenType.String || obj["text"]?.Type != JTokenType.String ||
                        (obj["options"] != null && (!(obj["options"] is JArray choices) || choices.Any(o => o.Type != JTokenType.String))))
                        throw new JsonException("Edit에는 speaker/text와 별도의 options 문자열 배열만 사용합니다.");
                if (lines.Count == 0 || lines.Any(l => l["options"] != null)) continue;
                // Recover the previous flat format only when its complete positional mapping is unambiguous.
                var reference = RawCards((string)report[stage == "draft" ? "rawOutput" : "correctionRawOutput"]);
                if (reference == null) throw new JsonException("원출력 없이 평면 지문의 선택지를 추측할 수 없습니다.");
                int expected = reference.Sum(c => 1 + ((JArray)c["options"]).Count);
                if (lines.Count != expected) throw new JsonException("편집 줄 수가 달라 선택지 위치를 확정할 수 없습니다. 수정본은 보존합니다.");
                var grouped = new JArray(); int cursor = 0;
                foreach (var card in reference)
                {
                    var dialogue = lines[cursor++];
                    var options = new JArray();
                    for (int j = 0; j < ((JArray)card["options"]).Count; j++)
                    {
                        var option = lines[cursor++];
                        if ((string)option["speaker"] != "플레이어") throw new JsonException("선택지 화자가 바뀌어 일반 대화와 구분할 수 없습니다. 수정본은 보존합니다.");
                        options.Add(option["text"].DeepClone());
                    }
                    grouped.Add(EditLine((string)dialogue["speaker"], dialogue["text"], options));
                }
                if (!JToken.DeepEquals(grouped, lines)) { converted[stage] = grouped; changed = true; }
                continue;
            }
            if (!(property.Value is JObject section) || !(section["cards"] is JArray oldCards) ||
                section.Properties().Any(p => p.Name != "situation" && p.Name != "cards"))
                throw new JsonException("변환할 수 없는 Edit 영역입니다: " + stage);
            var output = new JArray();
            var raw = RawCards((string)report[stage == "draft" ? "rawOutput" : "correctionRawOutput"]);
            for (int i = 0; i < oldCards.Count; i++)
            {
                if (!(oldCards[i] is JObject card) || !(card["options"] is JArray options) ||
                    card.Properties().Any(p => !new[] { "situation", "text", "options", "speaker" }.Contains(p.Name)))
                    throw new JsonException("변환할 수 없는 Edit 카드입니다: " + i);
                string speaker = (string)card["speaker"];
                if (string.IsNullOrWhiteSpace(speaker))
                {
                    string[] context = card["situation"] is JArray contextLines ? contextLines.Values<string>().ToArray() : ((string)card["situation"] ?? "").Split('\n');
                    string authored = context.FirstOrDefault(l => l != null && l.StartsWith("화자:"));
                    if (authored != null) speaker = authored.Substring(3).Trim();
                }
                int index = raw != null && i < raw.Count ? (int)raw[i]["index"] : i;
                output.Add(EditLine(EditSpeaker(report, index, speaker), card["text"], options));
            }
            converted[stage] = output;
            changed = true;
        }
        if (changed)
        {
            edit.RemoveAll();
            foreach (var property in converted.Properties()) edit.Add(property.Name, property.Value.DeepClone());
        }
        return changed;
    }
    private static JArray SituationLines(string text) => new JArray((text ?? "").Split('\n'));
    // Presentation-only migration. Never changes human dialogue/options or the model input.
    public static bool FormatEditSituations(JObject edit)
    {
        bool changed = false;
        foreach (string stage in new[] { "draft", "correction" })
        {
            if (edit[stage] == null) continue;
            if (!(edit[stage] is JObject section)) throw new JsonException("Broken edit stage");
            var contexts = new List<JObject> { section };
            if (section["cards"] is JArray cards)
                foreach (JToken card in cards)
                {
                    if (!(card is JObject obj)) throw new JsonException("Broken edit card");
                    contexts.Add(obj);
                }
            foreach (JObject context in contexts)
            {
                if (context["situation"]?.Type == JTokenType.String)
                {
                    context["situation"] = SituationLines((string)context["situation"]);
                    changed = true;
                }
                else if (context["situation"] != null && (!(context["situation"] is JArray lines) ||
                    lines.Any(line => line.Type != JTokenType.String))) throw new JsonException("Broken situation lines");
            }
        }
        return changed;
    }
    private static void EnsureEdit(JObject report, string file, string legacyFile = null)
    {
        string numbered = NumberedEditPath(file, report), old = Path.Combine(Path.GetDirectoryName(file), "Edit.json");
        string editFile = !File.Exists(numbered) && File.Exists(old) ? old : numbered;
        var edit = File.Exists(editFile) ? Read(editFile) : new JObject();
        bool changed = !File.Exists(editFile);
        foreach (string stage in new[] { "draft", "correction" })
        {
            if (stage == "correction" && string.IsNullOrEmpty((string)report["correctionRawOutput"])) continue;
            if (edit[stage] != null) { if (!(edit[stage] is JObject) && !(edit[stage] is JArray)) throw new JsonException("Broken edit stage"); continue; }
            string previous = legacyFile?.Replace(".report.json", "." + stage + ".edit.json");
            edit[stage] = previous != null && File.Exists(previous) ? Read(previous) : BuildEdit(report, stage);
            changed = true;
        }
        if (changed) Write(editFile, edit); // Never overwrite existing human text or review.
    }
    public static string Save(string root, JObject report, string origin = null)
    {
        return SaveGrouped(root,report,origin);
    }
    public static JObject ReadBenchmarkReport(string file)
    {
        file = Path.GetFullPath(file);
        if (File.Exists(file)) return ReadReport(file);
        return List().Select(ReadReport).FirstOrDefault(r => !string.IsNullOrEmpty((string)r["archiveOrigin"]) &&
            Path.GetFullPath(Path.Combine(Root, (string)r["archiveOrigin"])).Equals(file, StringComparison.OrdinalIgnoreCase));
    }
    private static string Link(string root, string file) => string.Join("/", Path.GetRelativePath(root, file)
        .Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));
    // Runtime application metadata may change; immutable raw model output/Input and human Edits never do.
    public static void RecordRuntimeApplication(string root,string reportFile,LocalStoryTestReport report)
    {
        lock(Sync)
        {
            root=Path.GetFullPath(root);
            string file=Path.GetFullPath(reportFile);
            if(file.EndsWith(".Result.json",StringComparison.Ordinal)) {UpdateGrouped(file,report);return;}
            if(Path.GetFileName(file)!="Result.json" || !file.StartsWith(Path.Combine(root,"Results")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("안전하지 않은 생성 결과 경로");
            string analysisFile=Path.Combine(Path.GetDirectoryName(file),"Analysis.json");
            var analysis=Read(analysisFile);
            analysis["saved"]=report.saved;
            analysis["runtimeStage"]=report.runtimeStage;
            analysis["runtimeError"]=report.runtimeError;
            analysis["modelPath"]=report.modelPath;
            analysis["runtimePath"]=report.runtimePath;
            analysis["runtimeFailureDetails"]=report.runtimeFailureDetails;
            analysis["seconds"]=report.seconds;
            Write(analysisFile,analysis.ToString(Formatting.Indented));
            UpdateIndex(root);
        }
    }
    public static void UpdateIndex(string root = null)
    {
        // Results are directly readable JSON; HTML catalog generation was removed.
    }
    // Explicit, scoped one-time migration. No original stories/game saves are touched.
    public static string Migrate(string root, string recoveryDirectory)
    {
        lock (Sync)
        {
            root = Path.GetFullPath(root);
            string resultsRoot = Path.Combine(root, "Results");
            if ((File.GetAttributes(resultsRoot) & FileAttributes.ReparsePoint) != 0 ||
                Directory.GetDirectories(resultsRoot, "*", SearchOption.AllDirectories).Any(p=>(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0))
                throw new InvalidOperationException("Do not migrate reparse-point folders");
            var legacy = Directory.GetFiles(resultsRoot, "*.report.json", SearchOption.AllDirectories);
            if (legacy.Length == 0) { UpdateIndex(root); return "원출력 구조 확인 / 결과 " + List(root).Count + "개"; }
            var sources = legacy.SelectMany(file => Directory.GetFiles(Path.GetDirectoryName(file), "*", SearchOption.TopDirectoryOnly)).Distinct().ToList();
            sources.AddRange(new[]{"Index.html","README.md"}.Select(n=>Path.Combine(root,n)).Where(File.Exists));
            if (sources.Any(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)) throw new InvalidOperationException("Do not follow reparse points");
            var hashes = sources.ToDictionary(p => p, p => Sha(File.ReadAllText(p, Utf8)));
            Directory.CreateDirectory(recoveryDirectory);
            string backup = Path.Combine(recoveryDirectory, "LocalStoryTests-four-files-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + ".zip");
            using (var zip = ZipFile.Open(backup, ZipArchiveMode.Create))
            foreach (string file in sources) zip.CreateEntryFromFile(file, Path.GetRelativePath(root, file).Replace('\\', '/'), System.IO.Compression.CompressionLevel.Optimal);
            using (var zip = ZipFile.OpenRead(backup))
            foreach (string file in sources)
            {
                using var stream = zip.GetEntry(Path.GetRelativePath(root, file).Replace('\\', '/')).Open();
                using var reader = new StreamReader(stream, Utf8);
                if (Sha(reader.ReadToEnd()) != hashes[file]) throw new InvalidOperationException("Recovery verification failed");
            }
            var expected = legacy.ToDictionary(f => f, Read);
            var verifiedRemoval = new HashSet<string>(legacy);
            foreach (var pair in expected)
            {
                var report = pair.Value;
                string folder = Path.GetDirectoryName(pair.Key), file = Path.Combine(folder, "Result.json");
                // Import edits before Save so a previously edited stage is never reset.
                EnsureEdit(report, file, pair.Key);
                Save(root, report);
                if (!JToken.DeepEquals(report, ReadReport(file))) throw new InvalidOperationException("Migrated report differs");
                var reviews = Directory.GetFiles(folder, "*.review.json").Concat(Directory.GetFiles(folder, "*.legacy-review.json")).Distinct().ToList();
                foreach (string review in reviews) verifiedRemoval.Add(review);
                string oldHtml = pair.Key.Replace(".report.json", ".evaluation.html");
                if (File.Exists(oldHtml)) verifiedRemoval.Add(oldHtml);
                if (reviews.Count > 0)
                {
                    string path = Path.Combine(folder, "Analysis.json");
                    var analysis = Read(path);
                    analysis["humanReviews"] = new JObject(reviews.Select(p => new JProperty(Path.GetFileName(p), Read(p))));
                    Write(path, analysis);
                }
                foreach (string stage in new[] { "draft", "correction" })
                {
                    string edit = pair.Key.Replace(".report.json", "." + stage + ".edit.json");
                    if (File.Exists(edit) && !JToken.DeepEquals(Read(edit), Read(EditPath(file))[stage]))
                        throw new InvalidOperationException("Human edit changed");
                    if (File.Exists(edit)) verifiedRemoval.Add(edit);
                }
            }
            UpdateIndex(root);
            // Remove only verified old sidecars, not new files or unrecognized user material.
            foreach (string file in verifiedRemoval)
            {
                if (!Path.GetFullPath(file).StartsWith(Path.GetFullPath(resultsRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Removal outside Results");
                if (Sha(File.ReadAllText(file, Utf8)) != hashes[file]) throw new InvalidOperationException("Source changed during migration");
                File.Delete(file);
            }
            return "이관 " + legacy.Length + "개 / 복구 ZIP: " + backup;
        }
    }
    public static string ImportExisting(string root = null)
    {
        root = Path.GetFullPath(root ?? Root);
        if (!Directory.Exists(root)) return "기존 테스트 없음";
        int before = List(root).Count;
        var files = Directory.GetFiles(root, "*.json", SearchOption.TopDirectoryOnly).ToList();
        foreach (string name in new[] { "Comparisons" })
        {
            string folder = Path.Combine(root, name);
            if (Directory.Exists(folder)) files.AddRange(Directory.GetFiles(folder, "*.report.json", SearchOption.AllDirectories));
        }
        foreach (string source in files.OrderBy(f => f, StringComparer.Ordinal))
        {
            var report = Read(source);
            if (report["storyPath"]?.Type != JTokenType.String || report["original"] == null) continue;
            Save(root, report, Path.GetRelativePath(root, source).Replace('\\', '/'));
        }
        foreach (string file in List(root).Where(p => Path.GetFileName(p) == "Result.json")) EnsureEdit(ReadReport(file), file);
        UpdateIndex(root);
        return "새로 정리 " + (List(root).Count - before) + "개 / 전체 " + List(root).Count + "개. 통합 Index와 결과·수정·분석·입력 준비.";
    }
}
