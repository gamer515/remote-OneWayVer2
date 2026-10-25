using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public static class LocalStoryArchiveCheck
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static IEnumerable<string> EditWords(JToken stage)
    {
        var cards = stage is JArray lines ? lines : stage?["cards"] as JArray;
        return (cards ?? new JArray()).SelectMany(c => new[] { (string)c["text"] }.Concat((c["options"] as JArray ?? new JArray()).Values<string>()));
    }
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    private static JObject Sample(string model = "Qwen3-4B-Instruct-2507 Q4_K_M") => new JObject {
        ["model"] = model, ["storyPath"] = LocalStoryTestInputs.GuidePath, ["testScenario"] = "Synthetic / 모욕 재회",
        ["seed"] = 12345, ["prompt"] = "검사용 고정 입력", ["valid"] = false, ["extraBenchmarkMetadata"] = "must-preserve",
        ["original"] = JObject.Parse("{\"MainStory\":[{\"text\":\"원래 대사\",\"options\":[],\"speakerId\":\"Guide\",\"eventIdStable\":\"greeting\"}]}"),
        ["rawOutput"] = "{\"cards\":[{\"index\":0,\"text\":\"원문과 다른, 생성 대사\",\"options\":[\"수정 전 선택지\"]}]}"
    };
    public static string Regression()
    {
        string root = Path.Combine(Path.GetTempPath(), "OneWayArchiveChecks_" + Guid.NewGuid().ToString("N"));
        var report = Sample();
        string file = LocalStoryTestArchive.Save(root, report);
        Assert(Path.GetFileName(file) == "Result.json" && Path.GetFileName(Path.GetDirectoryName(file)) == "0001", "four-file numbering");
        Assert(JToken.DeepEquals(report, LocalStoryTestArchive.ReadReport(file)), "evidence partition lost data");
        Assert(Directory.GetFiles(Path.GetDirectoryName(file)).Select(Path.GetFileName).OrderBy(n=>n).SequenceEqual(
            new[]{"Analysis.json","Input.json","Result.json"}), "unexpected result sidecars");
        Assert(File.Exists(Path.Combine(root,"Index.html")) && !Directory.GetFiles(root,"*.html",SearchOption.AllDirectories).Any(p=>Path.GetFileName(p)!="Index.html"), "per-result HTML remains");
        string editPath = LocalStoryTestArchive.EditPath(file);
        Assert(Path.GetFileName(Path.GetDirectoryName(editPath))=="Edits" && Path.GetFileName(editPath)=="0001__Qwen__Initial-guide__모욕-재회.json","numbered edit not grouped");
        var edit = JObject.Parse(File.ReadAllText(editPath));
        Assert(edit["draft"] is JArray simple && simple.Count==1, "choice is mixed into ordinary dialogue");
        Assert(edit["draft"][0].Children<JProperty>().Select(p=>p.Name).SequenceEqual(new[]{"speaker","text","options"}), "edit contains context/technical fields");
        Assert((string)edit["draft"][0]["speaker"]=="로키", "speaker names not resolved");
        Assert((string)edit["draft"][0]["options"][0]=="수정 전 선택지", "choice text changed");
        var readable = new JObject { ["draft"] = new JObject {
            ["situation"] = "사용자 상황\n두 번째 줄", ["cards"] = new JArray(new JObject {
                ["situation"] = "첫 줄\n둘째 줄", ["text"] = "직접 쓴 대사", ["options"] = new JArray("직접 쓴 선택지") }) } };
        var beforeReadable = (JObject)readable.DeepClone();
        Assert(LocalStoryTestArchive.FormatEditSituations(readable),"old context not formatted");
        Assert(string.Join("\n",readable["draft"]["situation"].Values<string>()) == (string)beforeReadable["draft"]["situation"] &&
            string.Join("\n",readable["draft"]["cards"][0]["situation"].Values<string>()) == (string)beforeReadable["draft"]["cards"][0]["situation"],"situation meaning changed");
        Assert((string)readable["draft"]["cards"][0]["text"] == "직접 쓴 대사" && (string)readable["draft"]["cards"][0]["options"][0] == "직접 쓴 선택지","human text/options changed");
        Assert(!LocalStoryTestArchive.FormatEditSituations(readable),"format migration not idempotent");
        edit["draft"][0]["text"]="사용자 직접 수정";
        File.WriteAllText(editPath,edit.ToString(Formatting.Indented));
        string same=LocalStoryTestArchive.Save(root,report);
        Assert(same==file && (string)JObject.Parse(File.ReadAllText(editPath))["draft"][0]["text"]=="사용자 직접 수정","human edit overwritten");
        string analysisFile=Path.Combine(Path.GetDirectoryName(file),"Analysis.json");
        var analysis=JObject.Parse(File.ReadAllText(analysisFile));analysis["humanReviews"]=new JObject {["draft"]=new JObject {["notes"]="보존 메모"}};
        File.WriteAllText(analysisFile,analysis.ToString());
        LocalStoryTestArchive.Save(root,report);
        Assert((string)JObject.Parse(File.ReadAllText(analysisFile))["humanReviews"]["draft"]["notes"]=="보존 메모","review overwritten");
        var gemma=Sample("gemma-3-4b-it Q4_0");string second=LocalStoryTestArchive.Save(root,gemma);
        Assert(Path.GetFileName(Path.GetDirectoryName(second))=="0002" && LocalStoryTestArchive.List(root).Count==2,"numbering duplicated");
        var unsafeReport=Sample();unsafeReport["archiveId"]="../escape";unsafeReport["archiveNumber"]=3;bool denied=false;
        try {LocalStoryTestArchive.Save(root,unsafeReport);}catch(InvalidOperationException){denied=true;}Assert(denied,"unsafe path allowed");
        var changed=(JObject)report.DeepClone();changed["rawOutput"]="changed";denied=false;
        try {LocalStoryTestArchive.Save(root,changed);}catch(InvalidOperationException){denied=true;}Assert(denied,"raw overwritten");
        var partial=Sample();partial["rawOutput"]="incomplete {";string partialFile=LocalStoryTestArchive.Save(root,partial);
        Assert((string)LocalStoryTestArchive.ReadReport(partialFile)["rawOutput"]=="incomplete {" &&
            ((JArray)JObject.Parse(File.ReadAllText(LocalStoryTestArchive.EditPath(partialFile)))["draft"]).Count==0,"bad raw replaced with source");
        Assert(LocalStoryTestArchive.DialogueText("broken").EndsWith("broken"),"bad output hidden");
        var old=Sample();old["archiveNumber"]=1;old["archiveId"]="0001__legacy";old["archiveOrigin"]=null;
        string migrationRoot=Path.Combine(Path.GetTempPath(),"OneWayArchiveMigration_"+Guid.NewGuid().ToString("N"));
        string folder=Path.Combine(migrationRoot,"Results","0001");Directory.CreateDirectory(folder);
        string legacy=Path.Combine(folder,"0001__legacy.report.json");File.WriteAllText(legacy,old.ToString());
        var human=new JObject {["situation"]="사용자 상황",["cards"]=new JArray(new JObject {["situation"]="직접 수정 장면",["text"]="이전 파일의 사용자 수정",["options"]=new JArray("수정 선택지")})};
        File.WriteAllText(legacy.Replace(".report.json",".draft.edit.json"),human.ToString());
        File.WriteAllText(legacy.Replace(".report.json",".evaluation.html"),"legacy offline HTML");
        File.WriteAllText(legacy.Replace(".report.json",".draft.review.json"),new JObject {["notes"]="이전 평가 메모"}.ToString());
        LocalStoryTestArchive.Migrate(migrationRoot,Path.Combine(root,"recovery"));
        string result=Path.Combine(folder,"Result.json");
        Assert(JToken.DeepEquals(old,LocalStoryTestArchive.ReadReport(result)),"legacy evidence changed");
        Assert(JToken.DeepEquals(human,JObject.Parse(File.ReadAllText(LocalStoryTestArchive.EditPath(result)))["draft"]),"legacy edit changed");
        Assert((string)JObject.Parse(File.ReadAllText(Path.Combine(folder,"Analysis.json")))["humanReviews"]["0001__legacy.draft.review.json"]["notes"]=="이전 평가 메모","legacy review lost");
        Assert(Directory.GetFiles(folder).Length==3 && !File.Exists(legacy),"old sidecars not removed");
        LocalStoryTestArchive.Migrate(migrationRoot,Path.Combine(root,"recovery"));
        Assert(Directory.GetFiles(folder).Length==3,"repeat migration duplicated");
        return "PASS: three evidence files + numbered Edits, unified Index only, full report reconstruction, edits/reviews preserved, safe paths, immutable raw, failed JSON, backup migration + repeat migration. Isolated: "+root;
    }
    public static string SimpleEditRegression()
    {
        var report=Sample(); report["archiveNumber"]=1;
        var original=new JObject { ["draft"]=new JObject { ["situation"]=new JArray("저장한 상황 메모"),
            ["cards"]=new JArray(new JObject { ["situation"]=new JArray("화자: 직접 쓴 화자","장면 메모"),
                ["text"]="직접 수정한 지문\n둘째 줄",["options"]=new JArray("직접 수정한 선택지","쉼표, 따옴표 \"보존\"") }) } };
        var copy=(JObject)original.DeepClone();
        Assert(LocalStoryTestArchive.SimplifyEdit(copy,report),"old edit not converted");
        Assert((string)copy["draft"][0]["speaker"]=="직접 쓴 화자","human speaker lost");
        Assert(EditWords(copy["draft"]).SequenceEqual(new[]{"직접 수정한 지문\n둘째 줄","직접 수정한 선택지","쉼표, 따옴표 \"보존\""}),"human wording changed");
        Assert(((JArray)copy["draft"]).Count==1 && ((JArray)copy["draft"][0]["options"]).Count==2,"options not grouped separately");
        Assert(copy["correction"]==null && !LocalStoryTestArchive.SimplifyEdit(copy,report),"stage added/idempotence failure");
        var mixed=(JObject)original.DeepClone();mixed["correction"]=copy["draft"].DeepClone();
        Assert(LocalStoryTestArchive.SimplifyEdit(mixed,report) && JToken.DeepEquals(mixed["draft"],mixed["correction"]),"mixed stages altered");
        var broken=(JObject)original.DeepClone();broken["draft"]["cards"][0]["options"]=new JArray(123);
        string before=broken.ToString(); bool denied=false;
        try { LocalStoryTestArchive.SimplifyEdit(broken,report); } catch(JsonException) { denied=true; }
        Assert(denied && broken.ToString()==before,"failed conversion mutated edit");
        broken=(JObject)original.DeepClone();broken["notes"]="別 메모";denied=false;
        try { LocalStoryTestArchive.SimplifyEdit(broken,report); } catch(JsonException) { denied=true; }
        Assert(denied,"unknown human field silently removed");
        var flat=new JObject { ["draft"]=new JArray(new JObject { ["speaker"]="로키",["text"]="직접 고친 대사" },
            new JObject { ["speaker"]="플레이어",["text"]="직접 고친 선택지" }) };
        Assert(LocalStoryTestArchive.SimplifyEdit(flat,report),"flat dialogue not converted");
        Assert((string)flat["draft"][0]["options"][0]=="직접 고친 선택지" && ((JArray)flat["draft"]).Count==1,"flat choice lost");
        var ambiguous=new JObject { ["draft"]=new JArray(new JObject { ["speaker"]="로키",["text"]="직접 고친 대사" }) };
        before=ambiguous.ToString();denied=false;
        try { LocalStoryTestArchive.SimplifyEdit(ambiguous,report); } catch(JsonException) { denied=true; }
        Assert(denied && ambiguous.ToString()==before,"ambiguous flat mapping guessed");
        string root=Path.GetFullPath(Path.Combine("tmp/LocalStoryOrganizationTests","SimpleEdits_"+Guid.NewGuid().ToString("N")));
        string result=LocalStoryTestArchive.Save(root,report);
        string target=LocalStoryTestArchive.NumberedEditPath(result),old=Path.Combine(Path.GetDirectoryName(result),"Edit.json");
        File.Move(target,old);File.WriteAllText(old,original.ToString());
        string status=SimplifyEdits(root);
        Assert(File.Exists(target)&&!File.Exists(old),"edit not moved to numbered Edits");
        Assert(JToken.DeepEquals(JObject.Parse(File.ReadAllText(target)),copy),"migration changed human lines");
        Assert(SimplifyEdits(root).StartsWith("이미"),"repeated migration changed files");
        Assert(!status.Contains("실패"),"migration failed");
        return "PASS: speaker/text + separate options, authored names/lines/choices preserved, flat conversion/ambiguous rejection, multiline escaping, partial/mixed stages, atomic rejection, numbered Edits, verified backup + repeat migration. Fixture: "+root;
    }
    public static string WritingExamplesRegression()
    {
        string root=Path.Combine(Path.GetTempPath(),"OneWayWritingExamples_"+Guid.NewGuid().ToString("N"));
        const string snippetPath="Initial/Initial_01/Encounters/duelist";
        var report=Sample(); report["storyPath"]=snippetPath; string result=LocalStoryTestArchive.Save(root,report),editFile=LocalStoryTestArchive.EditPath(result);
        Assert(LocalStoryTestArchive.CaptureWritingExamples(snippetPath,out _,out var none,root)=="" && none.Length==0,"unmodified AI output used as human example");
        var edit=JObject.Parse(File.ReadAllText(editFile));edit["draft"][0]["text"]="잠깐, 여긴 왕국의 광산이야."; edit["draft"][0]["options"][0]="그럼 넌 왜 여기 있어?";
        File.WriteAllText(editFile,edit.ToString());string frozen=File.ReadAllText(editFile),evidence=File.ReadAllText(result);
        string examples=LocalStoryTestArchive.CaptureWritingExamples(snippetPath,out var summary,out var sources,root);
        Assert(sources.Length==2 && examples.Contains("잠깐, 여긴 왕국의 광산이야.") && examples.Contains("그럼 넌 왜 여기 있어?"),"human corrections absent");
        Assert(!examples.Contains("원문과 다른, 생성 대사") && !examples.Contains("수정 전 선택지"),"uncorrected generation leaked");
        Assert(sources.All(s=>s.StartsWith("Edits/")&&s.Contains("SHA256=")),"example provenance missing");
        string messages="[{\"role\":\"system\",\"content\":\"현재 사건 규칙\"},{\"role\":\"user\",\"content\":\"현재 기억과 대상\"}]";
        var attached=JArray.Parse(LocalStoryTestArchive.AttachWritingExamples(messages,examples));
        Assert(attached.Count==2 && (string)attached.First["content"]=="현재 사건 규칙" && ((string)attached.Last["content"]).EndsWith("현재 기억과 대상"),"examples replaced current facts/rules");
        var gemma=JArray.Parse(LocalStoryTestModels.Messages(LocalStoryTestModel.Gemma,attached.ToString()));
        Assert(gemma.Count==1 && ((string)gemma[0]["content"]).Contains(examples) && ((string)gemma[0]["content"]).EndsWith("현재 기억과 대상"),"Gemma examples adapter rejected/altered context");
        Assert(LocalStoryTestArchive.AttachWritingExamples(messages,"")==messages,"no-example prompt changed");
        var other=Sample();other["storyPath"]="Initial/Initial_01/Encounters/gambler"; string otherResult=LocalStoryTestArchive.Save(root,other);
        var otherEdit=JObject.Parse(File.ReadAllText(LocalStoryTestArchive.EditPath(otherResult)));otherEdit["draft"][0]["text"]="다른 이야기의 수정";
        File.WriteAllText(LocalStoryTestArchive.EditPath(otherResult),otherEdit.ToString());
        Assert(LocalStoryTestArchive.CaptureWritingExamples(snippetPath,out _,out _,root)==examples,"different story mixed");
        File.WriteAllText(editFile,"incomplete {");
        Assert(LocalStoryTestArchive.CaptureWritingExamples(snippetPath,out summary,out sources,root)=="" && summary.Contains("읽기 실패"),"partial editor save stopped generation");
        Assert(attached.ToString().Contains("잠깐, 여긴 왕국의 광산이야."),"captured examples changed mid-run");
        Assert(File.ReadAllText(result)==evidence,"raw evidence overwritten");
        File.WriteAllText(editFile,frozen);
        return "PASS: only actual human differences, separate choice examples, no unedited/different-story material, frozen context/provenance, malformed edit fallback, facts/rules/source preserved. Isolated: "+root;
    }
    public static string Organize(string root = null)
    {
        root=Path.GetFullPath(root??LocalStoryTestArchive.Root);
        string allowed=Path.GetFullPath("tmp/LocalStoryOrganizationTests")+Path.DirectorySeparatorChar;
        Assert(root.Equals(Path.GetFullPath(LocalStoryTestArchive.Root),StringComparison.OrdinalIgnoreCase)||root.StartsWith(allowed,StringComparison.OrdinalIgnoreCase),"Only actual test root or isolated fixture allowed");
        Assert(!Application.isPlaying,"Stop Play before explicit cleanup");
        UnityEditor.AssetDatabase.ImportAsset("Assets/Decision_YYS/Resources/LocalStory/TestIndex.txt",UnityEditor.ImportAssetOptions.ForceUpdate);
        string result=LocalStoryTestArchive.Migrate(root,Path.GetFullPath("tmp/LocalStoryRecovery"));
        string readme=@"# 이야기 생성 테스트 사용법

1. Index.html을 열고 번호·모델·이야기·상황으로 결과를 선택합니다.
2. 원문과 AI 생성문을 카드별로 비교합니다. 초안/교정본을 구분하며, 실패한 출력도 원문으로 대체하지 않습니다.
3. NPC 관계·감정·상호작용 입력에서 플레이어가 했던 선택, 당시 결과와 NPC 태도 작성 지시를 구분합니다. 관계 숫자를 감정으로 단정하지 않으며, 실제 기록인지 시험용 입력인지 표시합니다.
4. 프롬프트 항목에는 해당 실행에 실제 사용한 입력을 그대로 보관합니다. 새 프롬프트를 만들거나 추론을 실행하는 버튼은 아닙니다.

## Unity에서 새 결과 생성

DecisionScene을 직접 Play하고 ‘이야기 LLM 테스트’를 엽니다. 상단 ‘모델: Qwen / Gemma’ 버튼으로 모델을 선택하고 이야기·입력 상황을 고른 뒤 생성합니다.
초안과 Guide 교정 모두 같은 선택 모델을 사용하며 모델명·초안/교정 시간·실행 장치를 표시합니다. 생성 중/저장 결과 보기 중에는 모델을 바꿀 수 없습니다.
현재 UI는 모델만 전환하고 생성 설정/시드 12345는 고정합니다. 두 모델을 동시에 생성하지 않습니다. 이전 보고서의 ‘공통 비교’는 Qwen 권장 설정값을 두 모델에 동일하게 적용했다는 기록입니다. ‘장면 흐름 v8’은 Guide의 장면 단위 집필 기준입니다. 모델이나 샘플링 수치를 변경한 표시는 아닙니다. ‘큰 흐름 v7’ 결과도 당시 입력 그대로 보존합니다.
Editor의 Gemma는 사용자가 조건에 동의하고 설치한 Tests/LocalStoryBenchmark/Models/gemma-3-4b-it-q4_0.gguf를 사용합니다. Windows x64 Mono 빌드에는 모델·DLL·고지를 자동 포함하고 Player는 StreamingAssets/LocalStory에서 읽습니다. Qwen은 Editor 비교에 남기고 기본 빌드에서는 제외합니다. 빌드 절차는 프로젝트 Docs/LocalStoryGemmaBuild.md에 있습니다.
파일이 없거나 검증/실행에 실패하면 오류를 남기고 Qwen으로 자동 대체하지 않습니다. GPU 실패 시 CPU로 실행해도 선택 모델은 바뀌지 않습니다.
모델 로딩 시간이 포함되며 Gemma 최초 실행은 SHA256 검증 시간도 포함합니다. 저장한 편집 파일의 실제 수정 문장은 다음 테스트 생성의 말투·문장 참고로 사용합니다. 모델 학습은 하지 않습니다. 생성만으로 본 게임에 자동 적용하지 않으며, 상단 ‘게임 확인용 적용’에서 선택/확인한 문구만 별도로 저장합니다.

## 원문 핵심 구조 — v9에서 도입

Guide 원문 Story.json의 storyFrame은 장면 목적(purpose), 세계관(worldFacts), 첫 만남/재회 흐름(firstMeetingFlow/reunionFlow)을 보관합니다. 중요한 카드 6곳의 coreMeaning에는 표현이 바뀌어도 남아야 할 사건 의미만 적습니다. 일반 대화에는 속성을 억지로 붙이지 않습니다.
GuideIntroBrief.json에는 말투(style), 원문 해시(sourceSha256), 사건 순서(eventOrder), 난독 선택지의 정상 발화 참고(choiceReferences)만 남습니다. 장면 목적/세계관/흐름은 원문에서 읽으며 두 파일에 중복 작성하지 않습니다.
초안과 교정은 같은 핵심 자료를 사용합니다. 교정은 맞춤법뿐 아니라 핵심과 어긋난 초안을 고칠 수 있습니다. 난독 오류가 있는 초안도 교정까지 전달하지만 원출력은 바꾸지 않습니다.
내부 화자 표기, 원문에 없던 숫자, 정상화된 난독의 일부 표현은 검사에서 거절합니다. 모든 의미 오류/어색한 문장을 자동 판정하는 것은 아닙니다. 테스트 UI와 Index는 실패한 AI 결과도 그대로 보여줍니다.

## 간결한 핵심 장면 v10.2 — 현재 생성 방식

원문 Story.json의 핵심 속성은 유지합니다. 모델에게 같은 사건을 핵심/상태 변화/선택 설명으로 반복 전달하던 부분은 하나로 정리했습니다. 핵심이 없는 연결 대사는 자유롭게 씁니다.
원문 핵심 6곳의 의미는 각 한 번만 전달합니다. 인덱스는 게임 연결 번호이고 대사로 읽지 않습니다. 출력 형식은 실제 JSON 모양으로 별도 안내합니다.
GuideIntroBrief.choiceReferences의 text는 선택의 정상 의미/검사 참고이며, heardText는 플레이어에게 들리는 난독 발화 예시입니다. 난독 슬롯에는 heardText를 문장 참고로 쓰고 정상 선택지를 말 예시로 보내지 않습니다. 예시 자체는 새 표현으로 바꿀 수 있습니다.
화자는 이해력이 정상인 로키, 듣는 쪽은 지식이 낮아진 플레이어로 구분합니다. 교정 입력에는 원문 핵심/이번 만남과 함께 검사에서 발견한 수정 필요 인덱스를 전달합니다. 정상 뜻을 괄호로 해설한 난독도 게임 적용 검사에서 거절합니다.
Guide 수정 예시는 산발적인 6개 문장 대신 카드 순서가 맞는 완성 대화 한 묶음을 사용합니다. 각 카드에서 text 또는 options가 실제로 수정된 파일만 후보이며, 현재 원문의 화자/카드/선택지/실행 연결 검사도 통과해야 합니다. 수정하지 않은 AI 출력, 화자가 틀린 파일, 카드가 빠진 파일은 예시로 쓰지 않습니다. 일부 카드만 수정한 Edit도 보존되고 수동 게임 적용 기능은 그대로지만, Guide의 완성 대화 예시로는 아직 선택되지 않습니다. 적합한 완성 예시가 없으면 예시 없이 생성합니다. 다른 NPC의 기존 부분 문장 예시 방식은 이번에 바꾸지 않았습니다.
이 작업은 프롬프트와 검사 개선이지 모델 학습이 아닙니다. 형식 검사 통과가 자연스러움/세계관 의미 통과를 뜻하지 않습니다. 실패한 초안·교정은 숨기거나 원문으로 바꾸지 않습니다.

2026-10-11 실제 시험: 0018~0022를 보관했습니다. 예시 없는 입력은 3044자에서 2270자로 줄였지만 의미/자연스러움 오류가 남았습니다. 0021(첫 만남)과 0022(모욕 재회)는 완성 Edit 예시를 초안 전체에 그대로 복사했습니다. 0022 교정도 NPC의 과거 모욕 반응을 만들지 못했습니다. 이 결과들은 품질 승인이나 다음 회차 자동 적용 대상이 아닙니다. 원문과 사용자가 저장한 기존 Edit은 바꾸지 않았습니다.

## 게임에서 선택한 결과 확인

1. DecisionScene 직접 Play → 이야기 LLM 테스트 → 새 생성 또는 저장 결과 보기.
2. ‘게임 확인용 적용’을 누르고 AI 초안/AI 교정본/Edit 초안/Edit 교정본을 선택합니다.
3. 카드별 대사와 대상 회차를 읽고 ‘N회차에 이 내용 저장’을 눌러야 저장됩니다. 교정이 없거나 검사에 실패하면 다른 단계/원문으로 몰래 대체하지 않습니다.
4. 저장 위치는 Saves/Narrative/Chapters/<chapterId>.json의 회차·인물·장면·구간별 항목입니다. 다음 메인 플레이에서 해당 회차에 도달해 그 NPC를 만나면 실제 게임 로더가 이 문구를 사용합니다.
5. 현재 회차는 바꾸거나 강제로 진행하지 않습니다. 기본 대상은 현재 활성 회차 다음 번호와 보고서 targetRun 중 큰 값(최소 2)입니다. 진행 중에 놓친 NPC를 바로 다시 보여주는 기능은 아닙니다.

원문 Story.json, 실제 선택 기록, AI Result/Input/Analysis, 직접 수정한 Edit은 별도로 보존합니다. 수동 적용본은 자동 생성이 덮어쓰지 않으며 직전 교체본 한 버전을 같은 항목에 보관합니다.
예전 결과도 현재 원문과 기능/분기/화자 연결이 같으면 새 핵심 속성을 유지한 채 적용할 수 있습니다. Edit의 순서/카드 수/선택지 수/화자는 원문과 맞춰야 합니다.
여기서 선택해 저장한 이야기 JSON은 모델이 없어도 다음 회차 게임에서 읽을 수 있습니다. 실제 만남 종료 자동 생성은 대화 큐/런타임 서비스가 맡으며, Windows x64 Mono 빌드에는 Gemma를 자동 포함합니다. 수동 적용 버튼은 별도의 검토 경로입니다.

## 원문으로 되돌리기

원문은 덮어쓰지 않습니다. Play를 중지하고 적용 회차·논리 이야기 경로를 확인합니다.
`NarrativeStoreCheck.SetEnabled`를 run_script의 entry로 지정하고 해당 회차/경로/false를 args에 전달하면 다음 새 만남부터 원문을 사용합니다. true는 재활성화입니다.
직전 수동 교체본은 `NarrativeStoreCheck.RestorePrevious`에 회차/경로를 전달해 복원합니다. 중단한 대화의 저장 버전은 유지합니다.
기존 개별 Story/disabled는 이관 시 Narrative/LegacyContent.zip으로 보관하며 파일 이름 변경 방식은 새 저장소에 적용하지 않습니다. Results/Edits와 실제 진행은 지우지 않습니다.
구체적인 PowerShell 명령과 검사 결과 기준은 Docs/NarrativeImplementation.md, Docs/LocalStoryLiveGameplay.md를 참고하세요.

## 이전 장면 흐름 v8 — 당시 기록

GuideIntroBrief.json은 장면 목적(sceneGoal), 인물 말투(style), 세계관(worldFacts), 첫 만남/재회 전체 흐름(firstMeetingFlow/reunionFlow), 원본 사건 순서(eventOrder), 선택 ID에 연결한 직접 발화 참고(choiceReferences)를 분리합니다. 일반 대사마다 사건 의미와 선택 의도를 설명하는 방식은 제거했습니다.
현재 v8.1은 원본 Story와 Input.json의 original에 실제 코인 수치·선택 ID·분기·관계 변화를 보존합니다. 모델에는 화자/상대와 대화에 필요한 상태 변화, 플레이어 직접 발화 참고만 전달합니다. 실행용 ID·분기 경로·코인 수치는 모델에게 문장 재료로 반복 전달하지 않습니다. 게임이 기존 연결에 생성된 문구만 적용합니다.
이번 단계는 기존 9개 카드와 선택 순서를 유지합니다. 선택지를 우호/중립/적대/혼돈 순서로 재배치하거나 보상을 가변화하지 않았습니다. 일반 대사는 장면 흐름 안에서 새로 쓰고, 난독의 정상 의미는 별도로 전달합니다. 초안·교정은 같은 장면 자료를 사용합니다.
‘수정 예시 제외 대조’는 기존 Edit 예시를 넣지 않은 비교 실행입니다. 평소 생성 버튼의 예시 사용 방식은 그대로이며, 이번 작업에서 예시 선별이나 파인튜닝을 하지는 않았습니다.
자동 구조 검사는 자연스러움·세계관 의미를 보증하지 않습니다. 원출력과 교정본을 모두 읽어 평가하세요. 대사 160자/선택지 45자 제한은 이번 비교에서 유지합니다.

최초 v8의 0015는 상세 실행 연결표가 대사에 유입돼 실패했습니다. 그 실패도 원출력 그대로 보존합니다. v8.1 결과와 구분해 확인하세요. Input.json의 writingPolicy가 실제 사용한 버전입니다.

## 이전 큰 흐름 v7 기록

Guide는 세계관 사실과 핵심 사건/연결 대화를 구분해 씁니다. 재회에는 첫 소개를 반복하는 대신 실제 이전 선택과 결과를 도입·중간 대화에 반영합니다. 삶/여정을 묻는 질문이나 세계관 설명은 허용하지만 새로운 능력/보상/기록에 없는 과거 사실은 약속하지 않습니다.
난독은 숨은 의미와 선택 의도를 유지하고 흐트러진 발음을 새로 생성합니다. GBNF의 원문 고정을 풀었으며 교정도 변경된 초안을 원문으로 되돌리지 않습니다.
JSON 카드 순서/개수/선택지 개수, 게임 행동/ID/분기/보상, 고정 토큰과 숫자, 대사 160자/선택지 45자 최대값은 유지합니다.
자동 검사는 문법·형식·일부 불변값 검사일 뿐입니다. 난독의 뜻/느낌, 세계관 의미, 새로운 약속 여부와 대화 자연스러움은 직접 평가해야 합니다.
새 Input.json에 writingPolicy와 generationGrammar를 보관합니다. 기존 결과·수정본은 다시 쓰지 않습니다. 테스트 결과는 게임이나 다음 회차에 적용하지 않습니다.

## 폴더 구조

~~~text
LocalStoryTests/
  Index.html
  README.md
  Results/
    0001/
      Result.json
      Analysis.json
      Input.json
    0002/
      (동일한 세 파일)
  Edits/
    0001__Qwen__Initial-guide__첫-만남.json
    0002__Gemma__Initial-guide__우호-재회.json
~~~

- Result.json: 실제 AI 원출력 문자열과 판독 가능한 카드. 직접 수정하지 않습니다.
- Edits/번호__모델__이야기__상황.json: 직접 수정할 파일입니다. draft(초안)/correction(교정본) 각각은 speaker(화자)/text(지문)의 배열입니다. 선택지가 있는 지문은 options 배열에 플레이어의 선택 문구를 따로 적습니다. 없는 교정본은 만들지 않습니다.
- Analysis.json: 모델·시드·시간·검사·유사도·생성 ID 등 분석 정보. humanReviews만 사람의 평가 영역이며 그 밖의 실행 정보는 고치지 않습니다.
- Input.json: 원문, 실제 사용한 프롬프트, 이야기 경로, 회차, 플레이 기록과 입력 출처, 새 결과의 집필 기준/출력 문법입니다. 다시 생성할 때 입력을 검증할 수 있도록 고치지 않습니다.

JSON은 들여쓰기하고 지문마다 별도 줄로 구분합니다. speaker에는 로키/플레이어 같은 화자만 쓰고 text에는 실제 지문만 씁니다. 상황/분기/규칙은 Input.json에 남아 있으므로 편집 파일에 다시 적을 필요가 없습니다. 수정해도 실제 게임·원본·다음 회차에 자동 적용하거나 학습하지 않습니다.

## 수정본과 평가

Index의 번호가 붙은 편집 파일 링크를 열거나 Edits 폴더에서 직접 엽니다. 예를 들어 다음처럼 작성합니다.

~~~json
{
  ""draft"": [
    { ""speaker"": ""로키"", ""text"": ""어이, 자네. 여기 있으면 안 돼."" },
    {
      ""speaker"": ""로키"",
      ""text"": ""나는 장난의 신 로키이지."",
      ""options"": [
        ""네가 신이라고?"",
        ""왜 여기에 있는 거지?""
      ]
    }
  ]
}
~~~

초안과 교정본은 별도 배열입니다. 기존 결과를 교정할 때는 지문의 순서와 개수, options 순서와 개수를 유지하면 나중에 원본과 대응하기 쉽습니다. 선택지를 일반 플레이어 대화 줄로 섞지 않습니다. 자유롭게 쓴 대화를 보내는 것은 가능하지만 그 경우는 말투/자연스러움 예시이며 자동으로 게임 분기가 되지 않습니다. 번호는 결과 번호입니다.
불필요한 수정본 미리보기·원출력 JSON 영역은 Index에서 제거했습니다. 원출력은 Result.json에서 확인할 수 있습니다.
Unity 테스트 화면의 ‘저장 결과 보기’를 누르면 Index가 갱신됩니다. 이후 브라우저를 새로고침하세요. 새 생성 결과도 저장 시 Index를 갱신합니다.
의미 일치·자연스러움·변화 정도·메모는 브라우저에 임시 저장됩니다. ‘분석 JSON 내려받기’로 받은 Analysis.json을 해당 번호 폴더에 직접 교체해야 파일로 보관됩니다. 다른 번호의 파일과 섞지 마세요.
처리 중 미완료/JSON 오류 출력은 원출력 그대로 보여 주고，수정할 카드가 없으면 빈 배열입니다.

## 저장한 수정 문장으로 다시 테스트하기

편집 파일을 저장한 뒤 새 생성을 누르면 같은 이야기 경로의 수정본을 참고합니다. Guide는 위 기준에 맞는 완성 대화 1묶음을 사용하며, 다른 NPC의 기존 방식은 선택지 최대 3묶음과 대사 최대 3개입니다. 원출력 그대로인 파일이나 다른 이야기의 수정본은 제외합니다.
초안과 교정은 생성 시작 시 읽은 같은 예시를 사용합니다. 진행 중 수정은 다음 생성부터 반영됩니다. Input.json의 examplePrompt/exampleSources/exampleSummary에서 실제 예시와 파일 경로·해시·개수를 확인합니다.
이것은 좋은 문장을 옆에 놓고 쓰게 하는 참고 방식이며 학습은 아닙니다. 사건·보상·과거 기억은 현재 대상의 입력이 우선이고 예시 내용이 게임 규칙이 되지 않습니다. 읽기 실패는 건너뛰고 표시하며, 예시가 없으면 기존 입력을 그대로 씁니다. 자연스러움은 생성 결과를 직접 읽고 평가해야 합니다.

## 정리와 복구

개별 평가 HTML과 옛 report/edit 파일은 네 JSON에 내용이 보존됐는지 확인한 뒤 제거했습니다. 이전 파일은 프로젝트 tmp/LocalStoryRecovery의 LocalStoryTests-four-files-*.zip에 보관됩니다.
옛 개별 HTML의 브라우저 임시 평가는 자동 이관을 보장하지 않습니다. 이미 내려받은 review JSON은 Analysis.json의 humanReviews로 보존합니다. 필요하면 백업의 옛 HTML을 원래 위치에 복구해 평가를 내보내세요.
옛 비교 기록과 ArchiveMap.json은 정리했습니다. 현재 결과 확인·수정은 Index, Results, Edits를 사용합니다.
기존 Results/번호/Edit.json을 Edits로 옮기고 상황 설명을 제거할 때는 프로젝트 tmp/LocalStoryRecovery/EditSpeakerText-*.zip에 원래 파일 전체를 먼저 백업합니다. 기존에 고친 지문/선택지는 바꾸지 않습니다. 열어둔 옛 Edit.json은 닫고 새 번호 파일을 다시 여세요. 옛 경로로 저장하면 오래된 사본이 생길 수 있습니다.
평면 플레이어 줄을 options로 분리하는 변경 전 파일 전체도 tmp/LocalStoryRecovery/EditOptions-*.zip에 보관합니다. 열어둔 편집 파일에 변경 알림이 나오면 디스크의 새 내용을 다시 읽으세요.
";
        File.WriteAllText(Path.Combine(root,"README.md"),readme,new System.Text.UTF8Encoding(false));
        return result+" / 통합 Index·사용법 갱신";
    }
    // Explicit edit-only conversion. Back up authored context and all edits before touching any.
    public static string SimplifyEdits(string root = null)
    {
        root = Path.GetFullPath(root ?? LocalStoryTestArchive.Root);
        string fixtures = Path.GetFullPath("tmp/LocalStoryOrganizationTests") + Path.DirectorySeparatorChar;
        Assert(root.Equals(Path.GetFullPath(LocalStoryTestArchive.Root),StringComparison.OrdinalIgnoreCase) ||
            root.StartsWith(fixtures,StringComparison.OrdinalIgnoreCase),"Only actual test root or isolated fixture allowed");
        Assert(!Application.isPlaying,"Stop Play before reformatting edits");
        var plans = new List<(string file,string destination,string original,string updated)>();
        var protectedFiles = new Dictionary<string,string>();
        foreach(string result in LocalStoryTestArchive.List(root))
        {
            string folder = Path.GetDirectoryName(result);
            Assert(Path.GetFileName(result) == "Result.json","Migrate old reports first");
            foreach(string name in new[]{"Result.json","Input.json","Analysis.json"})
            {
                string file = Path.Combine(folder,name);
                protectedFiles.Add(file,File.ReadAllText(file));
            }
            string edit = LocalStoryTestArchive.EditPath(result), destination = LocalStoryTestArchive.NumberedEditPath(result), original = File.ReadAllText(edit);
            string local = Path.Combine(folder,"Edit.json");
            Assert(edit==local || !File.Exists(local),"Both old and numbered Edit exist; preserve both and resolve manually");
            Assert((File.GetAttributes(edit)&FileAttributes.ReparsePoint)==0,"Do not move reparse-point files");
            Assert(edit==destination || !File.Exists(destination),"Numbered edit already exists; preserve both copies");
            var value = JObject.Parse(original);
            var before = (JObject)value.DeepClone();
            bool converted = LocalStoryTestArchive.SimplifyEdit(value,LocalStoryTestArchive.ReadReport(result));
            if(!converted && edit==destination) continue;
            foreach(string stage in new[]{"draft","correction"})
            {
                Assert(EditWords(before[stage]).SequenceEqual(EditWords(value[stage])),"Human dialogue/choices changed: "+edit);
            }
            plans.Add((edit,destination,original,value.ToString(Formatting.Indented)+"\n"));
        }
        if(plans.Count == 0) return "이미 화자·지문·분리된 options 형식 / 수정 내용 보존";
        string recovery=Path.GetFullPath("tmp/LocalStoryRecovery");Directory.CreateDirectory(recovery);
        string backup=Path.Combine(recovery,"EditOptions-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ")+".zip");
        using(var zip=System.IO.Compression.ZipFile.Open(backup,System.IO.Compression.ZipArchiveMode.Create))
            foreach(var plan in plans)
                System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(zip,plan.file,
                    Path.GetRelativePath(root,plan.file).Replace('\\','/'),System.IO.Compression.CompressionLevel.Optimal);
        using(var zip=System.IO.Compression.ZipFile.OpenRead(backup))
        {
            Assert(zip.Entries.Count == plans.Count,"Backup count mismatch");
            foreach(var plan in plans)
            {
                var entry=zip.GetEntry(Path.GetRelativePath(root,plan.file).Replace('\\','/'));
                using var reader=new StreamReader(entry.Open(),System.Text.Encoding.UTF8);
                Assert(reader.ReadToEnd() == plan.original,"Backup content mismatch");
            }
        }
        foreach(var plan in plans) Assert(File.ReadAllText(plan.file)==plan.original,"Edit changed since backup");
        foreach(var plan in plans)
        {
            string editsRoot=Path.GetFullPath(Path.Combine(root,"Edits"));
            Assert(Path.GetFullPath(plan.destination).StartsWith(editsRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Destination outside Edits");
            Directory.CreateDirectory(editsRoot);
            Assert((File.GetAttributes(editsRoot)&FileAttributes.ReparsePoint)==0,"Do not follow Edits reparse point");
            string temporary=plan.destination+"."+Guid.NewGuid().ToString("N")+".tmp";
            File.WriteAllText(temporary,plan.updated,new System.Text.UTF8Encoding(false));
            if(plan.file==plan.destination) File.Replace(temporary,plan.destination,null);
            else File.Move(temporary,plan.destination);
            Assert(JToken.DeepEquals(JObject.Parse(File.ReadAllText(plan.destination)),JObject.Parse(plan.updated)),"Reformat readback mismatch");
            if(plan.file!=plan.destination)
            {
                Assert(Path.GetFullPath(plan.file).StartsWith(Path.GetFullPath(Path.Combine(root,"Results"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Old edit outside Results");
                Assert(File.ReadAllText(plan.file)==plan.original,"Edit changed while moving; keep both");
                File.Delete(plan.file);
            }
        }
        foreach(var pair in protectedFiles) Assert(File.ReadAllText(pair.Key)==pair.Value,"AI evidence changed");
        LocalStoryTestArchive.UpdateIndex(root);
        return "PASS: "+plans.Count+" numbered Edit files with separate options; authored dialogue/choices and "+protectedFiles.Count+" input/result/analysis files preserved. Previous format preserved in verified backup: "+backup;
    }
    public static string Import() => LocalStoryTestArchive.ImportExisting();
    public static string StoredResults()
    {
        var files=LocalStoryTestArchive.List();
        Assert(files.Count==21,"expected all 21 existing results");
        int qwen=0,gemma=0;
        foreach(string file in files)
        {
            Assert(Path.GetFileName(file)=="Result.json" && Directory.GetFiles(Path.GetDirectoryName(file)).Length==3 && File.Exists(LocalStoryTestArchive.EditPath(file)),"old layout remains");
            var evidence=LocalStoryTestArchive.ReadReport(file);
            var report=LocalStoryTestArchive.Load(file);
            Assert(report.rawOutput==(string)evidence["rawOutput"] && report.prompt==(string)evidence["prompt"],"Unity deserialization changed raw/input");
            string raw=report.rawOutput,correction=report.correctionRawOutput;
            LocalStoryTestPanel.DisplayOutput(report);
            LocalStoryTestPanel.DisplayRawOutput(report);
            Assert(report.rawOutput==raw && report.correctionRawOutput==correction,"Unity formatter altered evidence");
            if(report.model.Contains("Qwen"))qwen++;
            if(report.model.IndexOf("gemma",StringComparison.OrdinalIgnoreCase)>=0)gemma++;
        }
        Assert(qwen==13 && gemma==8,"models missing");
        return "PASS: Unity loaded 21 canonical Result files (Qwen 13/Gemma 8); raw output/prompt preserved; UI formatters do not mutate or substitute evidence. No Play/inference/save.";
    }
    public static string Fingerprints(bool verify = false)
    {
        string source = "Assets/Decision_YYS/Resources/Chapters";
        string saves = Path.Combine(Application.persistentDataPath, "Saves");
        var files = Directory.GetFiles(source, "*.json", SearchOption.AllDirectories).Concat(
            Directory.GetFiles(saves, "*.json", SearchOption.AllDirectories).Where(f => !f.StartsWith(LocalStoryTestArchive.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)));
        string snapshot = string.Join("\n", files.OrderBy(f => f, StringComparer.Ordinal).Select(f => f + ":" + Convert.ToBase64String(System.Security.Cryptography.SHA256.Create().ComputeHash(File.ReadAllBytes(f)))));
        if(verify) Assert(UnityEditor.SessionState.GetString("LocalStoryArchiveCheck.Fingerprints", "") == snapshot, "story or game save changed");
        else UnityEditor.SessionState.SetString("LocalStoryArchiveCheck.Fingerprints", snapshot);
        return (verify ? "Verified unchanged: " : "Captured: ") + files.Count() + " source/game save files; test artifacts excluded.";
    }
    public static async Task<string> DuelLifecycle()
    {
        Assert(Application.isPlaying, "Play required");
        var errors = new List<string>();
        Application.LogCallback callback = (message, stack, type) => { if (type == LogType.Error || type == LogType.Exception) errors.Add(message); };
        Application.logMessageReceived += callback;
        var parent = new GameObject("DuelLifecycleRegression");
        try
        {
            var references = parent.AddComponent<DuelAuthoringReferences>();
            var setup = parent.AddComponent<TutorialMiniGameController>();
            var bridge = parent.AddComponent<DuelMiniGameBridge>();
            Set(bridge, "authoringReferences", references); Set(bridge, "setupController", setup);
            for (int mode = 0; mode < 3; mode++)
            {
                var player = new GameObject("DuelPlayerHand"); player.transform.SetParent(parent.transform);
                var npc = new GameObject("DuelNpcHand"); npc.transform.SetParent(parent.transform);
                var knight = new GameObject("TutorialMiniGameObjects"); knight.transform.SetParent(parent.transform);
                Set(references, "spawnedPlayerHand", player); Set(references, "spawnedNpcHand", npc); Set(setup, "spawnedRoot", knight.transform);
                if (mode == 0)
                {
                    bridge.CleanupDuel();
                    Assert(player.transform.parent == null && npc.transform.parent == null && knight.transform.parent == null, "normal restart no longer detaches old bounds");
                }
                else if (mode == 1) parent.SetActive(false);
                else bridge.enabled = false;
                await Task.Delay(100);
                Assert(player == null && npc == null && knight == null && references.SpawnedPlayerHand == null, "orphan or stale reference after cleanup");
                parent.SetActive(true); bridge.enabled = true;
            }
            Assert(errors.Count == 0, string.Join("\n", errors));
            return "Duel lifecycle passed: normal restart detaches; parent deactivation and component disable destroy both hands + knight root without SetParent errors/orphans.";
        }
        finally { Application.logMessageReceived -= callback; UnityEngine.Object.Destroy(parent); }
    }
    public static string Viewer()
    {
        Assert(Application.isPlaying, "Play required");
        var panel = UnityEngine.Object.FindFirstObjectByType<LocalStoryTestPanel>(); Assert(panel != null, "Decision direct test panel missing");
        panel.Open(); panel.OpenSavedResults();
        var reports = LocalStoryTestArchive.List(); Assert(reports.Count >= 16, "benchmark imports missing");
        Assert(reports.Any(f => LocalStoryTestArchive.Load(f).model.IndexOf("gemma",StringComparison.OrdinalIgnoreCase)>=0) && reports.Any(f => LocalStoryTestArchive.Load(f).model.Contains("Qwen")), "both models not imported");
        var root = (GameObject)typeof(LocalStoryTestPanel).GetField("panel", Private).GetValue(panel);
        Assert(root.GetComponentsInChildren<Toggle>(true).Length == 0, "review checkbox still exists");
        var output = (TMP_Text)typeof(LocalStoryTestPanel).GetField("generatedLabel", Private).GetValue(panel);
        for (int i = 0; i < reports.Count; i++)
        {
            Set(panel, "savedSelected", i);
            typeof(LocalStoryTestPanel).GetMethod("ShowSavedSelected", Private).Invoke(panel, null);
            Assert(output.text == LocalStoryTestPanel.DisplayOutput(panel.Report), "viewer substituted source");
            string raw = panel.Report.rawOutput;
            typeof(LocalStoryTestPanel).GetMethod("CycleInput", Private).Invoke(panel, null);
            Assert(output.text == LocalStoryTestPanel.DisplayRawOutput(panel.Report) && raw == panel.Report.rawOutput, "JSON view changed raw");
            typeof(LocalStoryTestPanel).GetMethod("CycleInput", Private).Invoke(panel, null);
        }
        Canvas.ForceUpdateCanvases();
        Assert(root.GetComponentsInChildren<ScrollRect>().All(s => s.content.rect.height > s.viewport.rect.height), "dialogues not scrollable");
        var canvas = root.GetComponentInParent<Canvas>();
        var close = root.GetComponentsInChildren<Button>().Single(b => b.name == "닫기");
        var rect = (RectTransform)close.transform;
        var point = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, rect.TransformPoint(rect.rect.center));
        var hits = new List<UnityEngine.EventSystems.RaycastResult>();
        UnityEngine.EventSystems.EventSystem.current.RaycastAll(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { position = point }, hits);
        Assert(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == close, "close button blocked: point="+point+" screen="+Screen.width+"x"+Screen.height+" camera="+canvas.worldCamera?.name+" enabled="+canvas.enabled+" active="+root.activeInHierarchy+" hits="+string.Join(",",hits.Select(h=>h.gameObject.name)));
        // Leave the final saved result displayed; no generation, review or gameplay save.
        return "Viewer passed for " + reports.Count + " stored results: both models, exact generated dialogue/options and raw JSON, no review control, scroll layout and close raycast.";
    }
}
