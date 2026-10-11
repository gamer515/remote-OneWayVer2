using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

// Editor test harness outside Assets. Never writes original stories or actual run content.
public static class LocalStoryBenchmark
{
    private const string SuitePath = "Tests/LocalStoryBenchmark/Suite.json";
    private const string StateKey = "LocalStoryBenchmark.State";
    private const string PathKey = "LocalStoryBenchmark.Path";
    private static readonly Encoding Utf8 = new UTF8Encoding(false);
    private static string Hash(string value)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Utf8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
    }
    private static string FileHash(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static JObject Read(string path) => JObject.Parse(File.ReadAllText(path, Utf8));
    private static void Write(string path, JToken value) => File.WriteAllText(path, value.ToString(Formatting.Indented) + "\n", Utf8);
    private static JObject Freeze()
    {
        var suite = Read(SuitePath);
        Require((int)suite["passes"] == 1 && !(bool)suite["automaticRepair"] && !(bool)suite["gameContentWrites"], "single-pass test-only required");
        var frozen = new JArray();
        foreach (JObject scenario in (JArray)suite["scenarios"])
        foreach (int seed in suite["seeds"].Values<int>())
        {
            string path = (string)scenario["storyPath"];
            Require(System.Text.RegularExpressions.Regex.IsMatch((string)scenario["id"]??"", @"^[a-z][a-z0-9-]{0,31}$"), "unsafe case id");
            Require(path==LocalStoryTestInputs.GuidePath || path==LocalStoryTestInputs.GuidePath+"/Redistribute", "v1 scope is Guide only");
            var asset = Resources.Load<TextAsset>("Story_Json_Data/" + path + "/Story");
            Require(asset != null, "missing story: " + path);
            var original = JsonUtility.FromJson<EncounterCardRoot>(asset.text);
            var memory = LocalStoryTestInputs.Create((int)scenario["scenario"]);
            string prompt = LocalStoryRewrite.Messages(path, original, memory);
            string grammar = LocalStoryRewrite.Grammar(original);
            string parameters = LocalStorySampling.Recommended().Parameters(seed);
            var checks = (JArray)suite["commonChecks"].DeepClone();
            foreach (var check in (JArray)scenario["checks"]) checks.Add(check.DeepClone());
            frozen.Add(new JObject {
                ["id"]=(string)scenario["id"]+"-"+seed, ["storyPath"]=path,
                ["scenario"]=scenario["scenario"].DeepClone(), ["seed"]=seed,
                ["original"]=JObject.Parse(asset.text), ["sourceSha256"]=Hash(asset.text),
                ["messages"]=JArray.Parse(prompt), ["promptSha256"]=Hash(prompt),
                ["grammar"]=grammar, ["grammarSha256"]=Hash(grammar),
                ["parameters"]=JObject.Parse(parameters), ["parametersSha256"]=Hash(parameters),
                ["memory"]=memory==null ? JValue.CreateNull() : JObject.Parse(JsonUtility.ToJson(memory)),
                ["checks"]=checks
            });
        }
        Require(frozen.Count==8 && frozen.Select(c=>(string)c["id"]).Distinct().Count()==8,"expected 8 unique cases");
        return new JObject {
            ["suiteId"]=suite["suiteId"], ["suiteSha256"]=Hash(File.ReadAllText(SuitePath,Utf8)),
            ["humanReview"]=suite["humanReview"].DeepClone(),
            ["timeoutSecondsPerCase"]=suite["timeoutSecondsPerCase"], ["cases"]=frozen,
            ["scope"]="현재 Guide 사건 요약과 Redistribute 기존 프롬프트를 각각 고정. 다른 챕터 품질을 보증하지 않음."
        };
    }
    private static void Verify(JObject snapshot)
    {
        Require(Hash(File.ReadAllText(SuitePath,Utf8))==(string)snapshot["suiteSha256"],"suite changed");
        foreach (JObject item in (JArray)snapshot["cases"])
        {
            string path=(string)item["storyPath"];
            var current=Resources.Load<TextAsset>("Story_Json_Data/"+path+"/Story");
            Require(current!=null && Hash(current.text)==(string)item["sourceSha256"],"source changed: "+path);
            var original=JsonUtility.FromJson<EncounterCardRoot>(item["original"].ToString(Formatting.None));
            var memory=item["memory"].Type==JTokenType.Null?null:JsonUtility.FromJson<LocalStoryRevisionRequest>(item["memory"].ToString(Formatting.None));
            string prompt=LocalStoryRewrite.Messages(path,original,memory);
            Require(Hash(prompt)==(string)item["promptSha256"],"prompt changed: "+path);
            Require(Hash(item["messages"].ToString(Formatting.None))==(string)item["promptSha256"],"frozen messages changed");
            Require(Hash(LocalStoryRewrite.Grammar(original))==(string)item["grammarSha256"],"grammar changed");
            Require(Hash((string)item["grammar"])==(string)item["grammarSha256"],"frozen grammar changed");
            Require(Hash(LocalStorySampling.Recommended().Parameters((int)item["seed"]))==(string)item["parametersSha256"],"sampling changed");
        }
    }
    public static string Regression()
    {
        var snapshot=Freeze(); Verify(snapshot);
        var cases=(JArray)snapshot["cases"];
        Require(cases[0]["sourceSha256"].ToString()==cases[1]["sourceSha256"].ToString(),"seed source mismatch");
        Require(cases[0]["promptSha256"].ToString()==cases[1]["promptSha256"].ToString(),"seed prompt mismatch");
        Require(cases[0]["promptSha256"].ToString()!=cases[2]["promptSha256"].ToString(),"memory scenarios not separated");
        Require(cases[2]["promptSha256"].ToString()!=cases[4]["promptSha256"].ToString(),"friendly/insult not separated");
        Require(cases[4]["checks"].Count()>3,"checks lost");
        Require(cases[6]["original"]["MainStory"].Any(c=>(string)c["eventIdStable"]=="loki_partial_immortality"),"actual allocation absent");
        var changed=(JObject)snapshot.DeepClone(); changed["cases"][0]["promptSha256"]="changed";
        bool rejected=false; try { Verify(changed); } catch(InvalidOperationException) { rejected=true; }
        Require(rejected,"drift not rejected");
        return "8 frozen cases verified; seed pairing, memory separation, real allocation branch and drift rejection passed. No save writes.";
    }
    public static string Prepare()
    {
        Require(!SessionState.GetString(StateKey,"").StartsWith("running"),"benchmark already running");
        var snapshot=Freeze(); Verify(snapshot);
        string folder=Path.Combine(BenchmarkRoot,"Runs","Qwen-"+
            DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));
        Directory.CreateDirectory(folder);
        Write(Path.Combine(folder,"Inputs.json"),snapshot);
        SessionState.SetString(PathKey,folder);
        SessionState.SetString("LocalStoryBenchmark.InputHash",FileHash(Path.Combine(folder,"Inputs.json")));
        SessionState.SetString(StateKey,"prepared");
        return folder;
    }
    public static string Status() => SessionState.GetString(StateKey,"idle")+"; output="+SessionState.GetString(PathKey,"");
    public static string Assess()
    {
        Require(SessionState.GetString(StateKey,"").StartsWith("completed"),"wait for all cases to complete");
        string folder=Path.GetFullPath(SessionState.GetString(PathKey,""));
        var snapshot=Read(InputFile(folder));
        var results=new JArray();
        var display=new StringBuilder("# 고정 시험 결과\n\n자동 검사는 의미·자연스러움 합격이 아니다. 생성 원시 출력은 원문으로 대체하지 않았다.\n\n");
        foreach(JObject item in (JArray)snapshot["cases"])
        {
            string id=(string)item["id"];
            var report=LocalStoryTestArchive.ReadBenchmarkReport(Path.Combine(folder,id+".report.json"));
            JObject parsed=null;
            string parseError=null;
            try { parsed=JObject.Parse((string)report["rawOutput"],new JsonLoadSettings { DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error }); }
            catch(Exception e) { parseError=e.Message; }
            results.Add(new JObject { ["caseId"]=id,["jsonParseable"]=parsed!=null,
                ["jsonError"]=parseError,["automatedRulesPassed"]=report["valid"],
                ["ruleError"]=report["error"],["seconds"]=report["seconds"],
                ["qualityStatus"]="not_reviewed",["modelMemoryBytes"]=JValue.CreateNull() });
            display.Append("## "+id+"\n\nJSON 파싱: "+(parsed!=null?"가능":"실패")+" / 자동 규칙: "+((bool)report["valid"]?"통과":(string)report["error"])+"\n\n");
            display.Append("검토 기준:\n\n");
            foreach(var check in (JArray)item["checks"]) display.Append("- "+(string)check+"\n");
            display.Append("\n");
            var source=(JArray)item["original"]["MainStory"];
            var cards=parsed?["cards"] as JArray;
            if(cards!=null)
            {
                for(int i=0;i<source.Count;i++)
                {
                    var candidate=cards.OfType<JObject>().FirstOrDefault(c=>(int?)c["index"]==i);
                    display.Append("### 카드 "+i+"\n\n원문: "+(string)source[i]["text"]+"\n\n생성: "+(candidate==null?"출력 없음":(string)candidate["text"])+"\n\n");
                    if(source[i]["options"] is JArray options)
                    {
                        for(int j=0;j<options.Count;j++)
                        {
                            var outputOptions=candidate?["options"] as JArray;
                            display.Append("선택지 "+(j+1)+" 원문: "+(string)options[j]+"\n\n생성: "+(outputOptions!=null && j<outputOptions.Count?(string)outputOptions[j]:"출력 없음")+"\n\n");
                        }
                    }
                }
            }
            else display.Append("원시 출력은 Results의 Result.json 또는 통합 Index.html에서 확인. 파싱 실패해도 내용은 보관됨.\n\n");
        }
        var seconds=results.Select(r=>(double)r["seconds"]).OrderBy(v=>v).ToArray();
        var assessment=new JObject {
            ["caseCount"]=results.Count,["jsonParseableCount"]=results.Count(r=>(bool)r["jsonParseable"]),
            ["automatedRulesPassedCount"]=results.Count(r=>(bool)r["automatedRulesPassed"]),
            ["medianSeconds"]=(seconds[3]+seconds[4])/2,["cases"]=results,
            ["qualityStatus"]="not_reviewed",["memoryNote"]="현재 환경의 프로세스 조회가 0을 반환하므로 모델 메모리를 0으로 해석하지 않는다. VRAM 실사용량도 미측정."
        };
        Write(Path.Combine(folder,"Assessment.json"),assessment);
        File.WriteAllText(Path.Combine(folder,"Comparison.md"),display.ToString(),Utf8);
        return Path.Combine(folder,"Comparison.md")+"\n"+assessment.ToString(Formatting.Indented);
    }
    public static async Task<string> RunBaseline()
    {
        Require(!SessionState.GetString(StateKey,"").StartsWith("running"),"benchmark already running");
        var panel=UnityEngine.Object.FindFirstObjectByType<LocalStoryTestPanel>();
        Require(panel==null || !panel.IsGenerating,"wait for UI generation");
        Require(!SessionState.GetString("TwoPassStatus","").Equals("running"),"wait for two-pass test");
        if(SessionState.GetString(StateKey,"")!="prepared") Prepare();
        string folder=Path.GetFullPath(SessionState.GetString(PathKey,"")), inputPath=InputFile(folder);
        Require(FileHash(inputPath)==SessionState.GetString("LocalStoryBenchmark.InputHash",""),"snapshot changed");
        var snapshot=Read(inputPath); Verify(snapshot);
        string root=Path.Combine(Application.streamingAssetsPath,"LocalStory");
        var manifest=Read(Path.Combine(root,"ModelManifest.json"));
        Require((string)manifest["file"]=="Qwen3-4B-Instruct-2507-Q4_K_M.gguf" && (string)manifest["model"]=="Qwen3-4B-Instruct-2507", "baseline runner is fixed to current Qwen model; do not relabel another model");
        string modelPath=Path.Combine(root,(string)manifest["file"]);
        Require(File.Exists(modelPath) && new FileInfo(modelPath).Length==(long)manifest["bytes"],"model missing/wrong size");
        Require(FileHash(modelPath)==(string)manifest["sha256"],"model hash mismatch");
        var runtimes=new JArray();
        foreach(string file in Directory.GetFiles(Path.Combine(root,"runtimes"),"llamalib*.dll",SearchOption.AllDirectories))
            runtimes.Add(new JObject { ["file"]=Path.GetFileName(file),["sha256"]=FileHash(file) });
        Write(Path.Combine(folder,"ModelManifest.json"),manifest);
        Write(Path.Combine(folder,"Environment.json"),new JObject {
            ["unity"]=Application.unityVersion,["gpu"]=SystemInfo.graphicsDeviceName,
            ["gpuMemoryMiB"]=SystemInfo.graphicsMemorySize,["systemMemoryMiB"]=SystemInfo.systemMemorySize,
            ["runtimeDlls"]=runtimes,["memoryNote"]="프로세스 메모리는 Unity 전체이며 모델만의 메모리가 아님. VRAM 실사용량 미측정."
        });
        var completed=new JArray();
        using var batch=new CancellationTokenSource();
        LocalStoryLifetime.Register(batch);
        Action<PlayModeStateChange> onPlay=s=> { if(s==PlayModeStateChange.ExitingPlayMode) batch.Cancel(); };
        EditorApplication.playModeStateChanged+=onPlay;
        try
        {
            int index=0;
            foreach(JObject item in (JArray)snapshot["cases"])
            {
                batch.Token.ThrowIfCancellationRequested(); Verify(snapshot);
                string id=(string)item["id"], path=(string)item["storyPath"];
                SessionState.SetString(StateKey,"running "+(++index)+"/8 "+id);
                var source=JsonUtility.FromJson<EncounterCardRoot>(item["original"].ToString(Formatting.None));
                var memory=item["memory"].Type==JTokenType.Null?null:JsonUtility.FromJson<LocalStoryRevisionRequest>(item["memory"].ToString(Formatting.None));
                var report=new LocalStoryTestReport {
                    storyPath=path,original=source,seed=(int)item["seed"],
                    model=(string)manifest["model"]+" "+(string)manifest["quantization"],
                    testScenario="Frozen benchmark / "+id,createdUtc=DateTime.UtcNow.ToString("o"),interactionMemory=memory,interactionSource="고정 비교 시험 / 가상 기억",
                    prompt=item["messages"].ToString(Formatting.None),samplingProfile=LocalStorySampling.Recommended().Name,
                    draftParameters=LocalStorySampling.Recommended().Parameters((int)item["seed"]),
                    sourceRun=memory?.sourceRun??1,targetRun=memory?.targetRun??2
                };
                var watch=System.Diagnostics.Stopwatch.StartNew();
                bool cancelled=false;
                using var request=CancellationTokenSource.CreateLinkedTokenSource(batch.Token);
                request.CancelAfter(TimeSpan.FromSeconds((int)snapshot["timeoutSecondsPerCase"]));
                try
                {
                    var output=await LocalStoryInference.Generate(root,report.prompt,(string)item["grammar"],report.seed,request.Token,
                        t=>report.rawOutput=t,LocalStorySampling.Recommended());
                    report.rawOutput=output.json; report.backend=output.backend; report.fallback=output.fallback;
                    LocalStoryRewrite.EvaluateDraft(report,source,memory);
                }
                catch(OperationCanceledException) { report.error="취소 또는 240초 제한 — 부분 출력 보관"; cancelled=true; }
                catch(Exception e) { report.error=e.Message; }
                finally
                {
                    report.seconds=report.draftSeconds=watch.Elapsed.TotalSeconds;
                    LocalStoryTestArchive.Save(LocalStoryTestArchive.Root, JObject.Parse(JsonUtility.ToJson(report)),
                        Path.GetRelativePath(LocalStoryTestArchive.Root, Path.Combine(folder,id+".report.json")).Replace('\\','/'));
                    using var process=System.Diagnostics.Process.GetCurrentProcess();
                    completed.Add(new JObject {
                        ["id"]=id,["seconds"]=report.seconds,["validStructure"]=report.valid,["error"]=report.error,
                        ["literalSimilarity"]=report.literalSimilarity,["changedCards"]=report.changedCards,
                        ["unityWorkingSetBytesAfter"]=process.WorkingSet64>0 ? new JValue(process.WorkingSet64) : JValue.CreateNull(),["qualityStatus"]="not_reviewed"
                    });
                    Write(Path.Combine(folder,"Summary.json"),new JObject {
                        ["suiteId"]=snapshot["suiteId"],["inputFileSha256"]=SessionState.GetString("LocalStoryBenchmark.InputHash",""),
                        ["completedCases"]=completed.DeepClone(),["qualityStatus"]="not_reviewed"
                    });
                }
                if(cancelled) { SessionState.SetString(StateKey,"cancelled "+index+"/8"); return Status(); }
            }
            SessionState.SetString(StateKey,"completed 8/8 — manual quality review required");
            return Status();
        }
        catch(OperationCanceledException) { SessionState.SetString(StateKey,"cancelled — saved results retained"); return Status(); }
        catch(Exception e) { SessionState.SetString(StateKey,"failed: "+e.Message); throw; }
        finally { EditorApplication.playModeStateChanged-=onPlay; LocalStoryLifetime.Unregister(batch); }
    }

    private const string GemmaStateKey="LocalStoryBenchmark.GemmaState";
    private static string BenchmarkRoot => Path.GetFullPath(LocalStoryTestArchive.ComparisonsRoot);
    private static string InputFile(string folder)
    {
        folder=Path.GetFullPath(folder);
        string own=Path.Combine(folder,"Inputs.json");
        if(File.Exists(own)) return own;
        string shared=Path.Combine(Path.GetDirectoryName(folder),"Inputs.json");
        Require(shared.StartsWith(BenchmarkRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) && File.Exists(shared),"frozen comparison input missing");
        return shared;
    }
    private static JObject Baseline(string folder)
    {
        folder=Path.GetFullPath(folder);
        Require(folder.StartsWith(BenchmarkRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"baseline must be inside test Comparisons folder");
        var snapshot=Read(InputFile(folder)); Verify(snapshot);
        Require(FileHash(InputFile(folder))==(string)Read(Path.Combine(folder,"Summary.json"))["inputFileSha256"],"baseline input hash mismatch");
        var manifest=Read(Path.Combine(folder,"ModelManifest.json"));
        Require((string)manifest["sha256"]=="2fde00ce69dd4899c70d020845e2638353015bba0fdf161b3eb965f2bca4464e","not the recorded Qwen baseline");
        foreach(JObject item in (JArray)snapshot["cases"])
        {
            var report=LocalStoryTestArchive.ReadBenchmarkReport(Path.Combine(folder,(string)item["id"]+".report.json"));
            Require((int)report["seed"]==(int)item["seed"] && Hash((string)report["prompt"])==(string)item["promptSha256"],"baseline report does not match frozen case");
        }
        return snapshot;
    }
    private static string GemmaMessages(JArray messages)
    {
        Require(messages.Count==2 && (string)messages[0]["role"]=="system" && (string)messages[1]["role"]=="user","unsupported message layout; adapter must be reviewed");
        return LocalStoryTestModels.Messages(LocalStoryTestModel.Gemma,messages.ToString(Formatting.None));
    }
    private static JObject ValidateGemma(string modelPath,bool userConfirmedTerms)
    {
        Require(userConfirmedTerms,"User must personally accept the official Gemma conditions first. Do not send tokens to chat.");
        var manifest=Read("Tests/LocalStoryBenchmark/GemmaManifest.json");
        Require((string)manifest["sha256"]=="76aed0a8285b83102f18b5d60e53c70d09eb4e9917a20ce8956bd546452b56e2","comparison manifest identity changed");
        Require(File.Exists(modelPath) && new FileInfo(modelPath).Length==(long)manifest["bytes"],"Gemma official GGUF missing or wrong size");
        Require(FileHash(modelPath)==(string)manifest["sha256"],"Gemma SHA256 mismatch");
        return manifest;
    }
    private static void EnsureIdle()
    {
        Require(!SessionState.GetString(StateKey,"").StartsWith("running"),"Qwen benchmark already running");
        Require(!SessionState.GetString(GemmaStateKey,"").StartsWith("running"),"Gemma test already running");
        var panel=UnityEngine.Object.FindFirstObjectByType<LocalStoryTestPanel>();
        Require(panel==null || !panel.IsGenerating,"wait for UI generation");
        Require(SessionState.GetString("TwoPassStatus","")!="running","wait for two-pass generation");
    }
    private static Task<LocalStoryModelOutput> GenerateGemma(string modelPath,string messages,string grammar,int seed,CancellationToken token,Action<string> progress)
    {
        // UI and benchmark share the same gate, native lifetime, GPU/CPU fallback, and installed model.
        string root=Path.Combine(Application.streamingAssetsPath,"LocalStory");
        return LocalStoryInference.Generate(root,messages,grammar,seed,token,progress,LocalStorySampling.Recommended(),modelPath);
    }
    public static async Task<string> SmokeGemma(string modelPath,bool userConfirmedTerms=false)
    {
        EnsureIdle(); var manifest=ValidateGemma(modelPath,userConfirmedTerms);
        string folder=Path.Combine(BenchmarkRoot,"Diagnostics","Gemma-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));
        Directory.CreateDirectory(folder); SessionState.SetString(GemmaStateKey,"running smoke");
        using var request=new CancellationTokenSource(TimeSpan.FromSeconds(240));
        var watch=System.Diagnostics.Stopwatch.StartNew(); var report=new JObject { ["model"]=manifest,["rawOutput"]="",["compatible"]=false,["qualityStatus"]="not_reviewed" };
        try
        {
            string messages=new JArray(new JObject { ["role"]="user",["content"]="준비되었으면 JSON으로 {\"ready\":true}만 출력해." }).ToString(Formatting.None);
            var output=await GenerateGemma(modelPath,messages,"root ::= \"{\\\"ready\\\":true}\"",12345,request.Token,t=>report["rawOutput"]=t);
            report["rawOutput"]=output.json; report["backend"]=output.backend; report["fallback"]=output.fallback;
            Require(JObject.Parse(output.json).Value<bool>("ready"),"smoke JSON not ready");
            report["compatible"]=true; SessionState.SetString("LocalStoryBenchmark.GemmaSmokeHash",(string)manifest["sha256"]);
            SessionState.SetString("LocalStoryBenchmark.GemmaSmokeBackend",output.backend);
            SessionState.SetString(GemmaStateKey,"smoke passed");
        }
        catch(Exception e) { report["error"]=e.Message; report["exception"]=e.ToString(); SessionState.SetString("LocalStoryBenchmark.GemmaSmokeHash",""); SessionState.SetString(GemmaStateKey,"smoke failed"); }
        finally { report["seconds"]=watch.Elapsed.TotalSeconds; Write(Path.Combine(folder,"Compatibility.json"),report); }
        return folder+"\n"+report.ToString(Formatting.Indented);
    }
    public static async Task<string> RunGemma(string baselinePath,string modelPath,bool userConfirmedTerms=false)
    {
        EnsureIdle(); baselinePath=Path.GetFullPath(baselinePath);
        var snapshot=Baseline(baselinePath); var manifest=ValidateGemma(modelPath,userConfirmedTerms);
        Require(SessionState.GetString("LocalStoryBenchmark.GemmaSmokeHash","")==(string)manifest["sha256"],"Run SmokeGemma first in this Editor session");
        string runtimeRoot=Path.Combine(Application.streamingAssetsPath,"LocalStory","runtimes");
        foreach(JObject file in (JArray)Read(Path.Combine(baselinePath,"Environment.json"))["runtimeDlls"])
        {
            string match=Directory.GetFiles(runtimeRoot,(string)file["file"],SearchOption.AllDirectories).Single();
            Require(FileHash(match)==(string)file["sha256"],"runtime changed since Qwen baseline");
        }
        string folder=Path.Combine(BenchmarkRoot,"Runs","Gemma-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));
        Directory.CreateDirectory(folder);
        File.Copy(InputFile(baselinePath),Path.Combine(folder,"Inputs.json"));
        Write(Path.Combine(folder,"ModelManifest.json"),manifest);
        var environment=Read(Path.Combine(baselinePath,"Environment.json"));
        environment["unity"]=Application.unityVersion; environment["gpu"]=SystemInfo.graphicsDeviceName;
        environment["gpuMemoryMiB"]=SystemInfo.graphicsMemorySize; environment["systemMemoryMiB"]=SystemInfo.systemMemorySize;
        environment["capturedUtc"]=DateTime.UtcNow.ToString("o"); environment["runtimeVerifiedAgainstBaseline"]=true;
        Write(Path.Combine(folder,"Environment.json"),environment);
        var completed=new JArray(); using var batch=new CancellationTokenSource(); LocalStoryLifetime.Register(batch);
        Action<PlayModeStateChange> onPlay=s=> { if(s==PlayModeStateChange.ExitingPlayMode) batch.Cancel(); };
        EditorApplication.playModeStateChanged+=onPlay; bool finished=false;
        try
        {
            foreach(JObject item in (JArray)snapshot["cases"])
            {
                batch.Token.ThrowIfCancellationRequested(); Verify(snapshot);
                string id=(string)item["id"]; SessionState.SetString(GemmaStateKey,"running "+(completed.Count+1)+"/8 "+id);
                var source=JsonUtility.FromJson<EncounterCardRoot>(item["original"].ToString(Formatting.None));
                var memory=item["memory"].Type==JTokenType.Null?null:JsonUtility.FromJson<LocalStoryRevisionRequest>(item["memory"].ToString(Formatting.None));
                var report=new LocalStoryTestReport { storyPath=(string)item["storyPath"],original=source,seed=(int)item["seed"],model=(string)manifest["model"]+" "+(string)manifest["quantization"],
                    testScenario="Gemma frozen comparison / "+id,prompt=GemmaMessages((JArray)item["messages"]),draftParameters=LocalStorySampling.Recommended().Parameters((int)item["seed"]),samplingProfile="Fixed baseline sampler, not Gemma-specific tuning",createdUtc=DateTime.UtcNow.ToString("o"),sourceRun=memory?.sourceRun??1,targetRun=memory?.targetRun??2,interactionMemory=memory,interactionSource="고정 비교 시험 / 가상 기억" };
                var watch=System.Diagnostics.Stopwatch.StartNew(); bool cancelled=false,executionError=false;
                using var request=CancellationTokenSource.CreateLinkedTokenSource(batch.Token); request.CancelAfter(TimeSpan.FromSeconds((int)snapshot["timeoutSecondsPerCase"]));
                try
                {
                    var output=await GenerateGemma(modelPath,report.prompt,(string)item["grammar"],report.seed,request.Token,t=>report.rawOutput=t);
                    report.rawOutput=output.json; report.backend=output.backend; report.fallback=output.fallback; LocalStoryRewrite.EvaluateDraft(report,source,memory);
                }
                catch(OperationCanceledException) { cancelled=true; report.error="취소 또는 240초 제한 — 부분 출력 보관"; }
                catch(Exception e) { executionError=true; report.error=e.Message; }
                finally
                {
                    report.seconds=report.draftSeconds=watch.Elapsed.TotalSeconds;
                    var document=JObject.Parse(JsonUtility.ToJson(report)); document["inputPromptSha256"]=item["promptSha256"]; document["adapter"]=manifest["messageAdapter"]; document["executionError"]=executionError;
                    LocalStoryTestArchive.Save(LocalStoryTestArchive.Root, document,
                        Path.GetRelativePath(LocalStoryTestArchive.Root, Path.Combine(folder,id+".report.json")).Replace('\\','/'));
                    completed.Add(new JObject { ["id"]=id,["seconds"]=report.seconds,["valid"]=report.valid,["error"]=report.error,["cancelled"]=cancelled,["executionError"]=executionError });
                    Write(Path.Combine(folder,"Summary.json"),new JObject { ["suiteId"]=snapshot["suiteId"],["inputFileSha256"]=FileHash(Path.Combine(folder,"Inputs.json")),["completedCases"]=completed.DeepClone(),["qualityStatus"]="not_reviewed" });
                }
                if(cancelled || executionError) break;
            }
            finished=completed.Count==8 && !completed.Any(c=>(bool)c["cancelled"] || (bool)c["executionError"]);
        }
        finally { EditorApplication.playModeStateChanged-=onPlay; LocalStoryLifetime.Unregister(batch); SessionState.SetString(GemmaStateKey,finished?"completed 8/8":"stopped — saved outputs retained"); }
        return BuildComparison(baselinePath,folder);
    }
    public static string BuildComparison(string baselinePath,string gemmaPath="")
    {
        baselinePath=Path.GetFullPath(baselinePath);
        var snapshot=Baseline(baselinePath); string inputHash=FileHash(InputFile(baselinePath));
        bool hasCandidate=!string.IsNullOrEmpty(gemmaPath);
        if(hasCandidate)
        {
            gemmaPath=Path.GetFullPath(gemmaPath);
            Require(gemmaPath.StartsWith(BenchmarkRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"candidate must be inside test Comparisons folder");
            Require(FileHash(InputFile(gemmaPath))==inputHash,"cannot compare different frozen inputs");
            Require((string)Read(Path.Combine(gemmaPath,"ModelManifest.json"))["sha256"]=="76aed0a8285b83102f18b5d60e53c70d09eb4e9917a20ce8956bd546452b56e2","not the configured Gemma candidate");
        }
        var cases=new JArray();
        foreach(JObject item in (JArray)snapshot["cases"])
        {
            string id=(string)item["id"],candidateFile=hasCandidate?Path.Combine(gemmaPath,id+".report.json"):"";
            JObject candidate=hasCandidate?LocalStoryTestArchive.ReadBenchmarkReport(candidateFile):null;
            if(candidate!=null) Require((int)candidate["seed"]==(int)item["seed"] && (string)candidate["inputPromptSha256"]==(string)item["promptSha256"] && (string)candidate["prompt"]==GemmaMessages((JArray)item["messages"]),"candidate prompt mismatch");
            string context=(int)item["scenario"]==2?"시험용 기억: 이전 만남에서 예의 있게 장소를 질문함.":(int)item["scenario"]==3?"시험용 기억: 이전 만남에서 로키에게 ‘꺼져!’라고 모욕함.":"시험용 입력: 첫 만남. 과거 만남 없음.";
            Func<JObject,JToken> select=r=>r==null?JValue.CreateNull():new JObject { ["rawOutput"]=r["rawOutput"],["model"]=r["model"],["valid"]=r["valid"],["error"]=r["error"],["seconds"]=r["seconds"],["backend"]=r["backend"] };
            cases.Add(new JObject { ["id"]=id,["seed"]=item["seed"],["storyPath"]=item["storyPath"],["original"]=item["original"].DeepClone(),["checks"]=item["checks"].DeepClone(),["context"]=context,
                ["qwen"]=select(LocalStoryTestArchive.ReadBenchmarkReport(Path.Combine(baselinePath,id+".report.json"))),["gemma"]=select(candidate) });
        }
        var data=new JObject { ["suiteId"]=snapshot["suiteId"],["inputSha256"]=inputHash,["qwenModel"]=Read(Path.Combine(baselinePath,"ModelManifest.json")),["gemmaModel"]=Read("Tests/LocalStoryBenchmark/GemmaManifest.json"),["cases"]=cases };
        string html=File.ReadAllText("Tests/LocalStoryBenchmark/Compare.html",Utf8);
        Require(html.Split(new[]{"__COMPARISON_DATA__"},StringSplitOptions.None).Length==2,"comparison template marker missing or duplicated");
        string outputFolder=Path.Combine(BenchmarkRoot,"Reports","Qwen-vs-Gemma-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));
        Directory.CreateDirectory(outputFolder); // Data is embedded in HTML; do not duplicate it as Comparison.json.
        File.WriteAllText(Path.Combine(outputFolder,"Comparison.html"),html.Replace("__COMPARISON_DATA__",Convert.ToBase64String(Utf8.GetBytes(data.ToString(Formatting.None)))),Utf8);
        return Path.Combine(outputFolder,"Comparison.html");
    }
    public static string ComparisonRegression(string baselinePath)
    {
        var snapshot=Baseline(baselinePath);
        foreach(JObject item in (JArray)snapshot["cases"])
        {
            var messages=(JArray)item["messages"]; var adapted=JArray.Parse(GemmaMessages(messages));
            Require((string)adapted[0]["content"]==(string)messages[0]["content"]+"\n\n"+(string)messages[1]["content"],"adapter changed instructions");
        }
        bool denied=false; try { ValidateGemma("does-not-exist",false); } catch(InvalidOperationException) { denied=true; } Require(denied,"terms guard bypassed");
        var gate=typeof(LocalStoryInference).GetField("Gate",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)?.GetValue(null) as SemaphoreSlim;
        Require(gate!=null,"shared inference gate unavailable");
        Require(File.ReadAllText("Tests/LocalStoryBenchmark/Compare.html",Utf8).Contains("__COMPARISON_DATA__"),"viewer template missing");
        return "8 baseline reports verified; Gemma adapter preserves all instruction content; terms guard and shared native gate checked. No inference or game writes.";
    }
}
