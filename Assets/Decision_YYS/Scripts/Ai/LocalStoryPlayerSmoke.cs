#if UNITY_EDITOR || DEVELOPMENT_BUILD || ONEWAY_STABILITY_VERIFICATION
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OneWay.Decision.DirectLlm;
using OneWay.Loading;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Opt-in verification player. Isolate saves before any scene Awake; omitted from shipped builds.</summary>
public sealed class LocalStoryPlayerSmoke : MonoBehaviour
{
    private static string output;
    private static bool performance;
    private readonly List<float> frameMilliseconds = new();
    private bool samplingFrames;

    private void Update()
    {
        if (samplingFrames) frameMilliseconds.Add(Time.unscaledDeltaTime * 1000f);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        if (Application.isEditor) return;
        var args = Environment.GetCommandLineArgs();
        performance = args.Contains("-localStoryPerformanceSmoke");
        if (!performance && !args.Contains("-localStorySmoke")) return;
        string root=Path.GetFullPath(Path.Combine(Application.dataPath,"..","LocalStorySmoke"));
        Directory.CreateDirectory(root);output=Path.Combine(root,"Result.json");
        var constructor=typeof(SaveIOService).GetConstructor(BindingFlags.NonPublic|BindingFlags.Instance,null,new[]{typeof(string)},null);
        typeof(SaveIOService).GetField("_instance",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,
            constructor.Invoke(new object[]{Path.Combine(root,"Saves")}));
        var go=new GameObject("LocalStoryPlayerSmoke");DontDestroyOnLoad(go);go.AddComponent<LocalStoryPlayerSmoke>();
    }
    private async void Start()
    {
        var watch=System.Diagnostics.Stopwatch.StartNew();
        var result=new JObject { ["passed"]=false,["platform"]=Application.platform.ToString(),["editor"]=Application.isEditor,
            ["isolatedSaves"]=SaveIOService.Instance.StorageRoot,["model"]=LocalStoryTestModels.Name(LocalStoryTestModel.Gemma) };
        using var budget=new CancellationTokenSource(TimeSpan.FromSeconds(180));
        try
        {
            if (performance)
            {
                await CheckNetworkFallbackDuringGameplay(result, budget.Token);
                result["passed"] = true;
                return;
            }

            string root=Path.Combine(Application.streamingAssetsPath,"LocalStory");
            string model=LocalStoryTestModels.PathFor(root,LocalStoryTestModel.Gemma);
            result["modelPath"]=model;result["qwenBundled"]=File.Exists(Path.Combine(root,LocalStoryTestModels.QwenFile));
            var prepared=await LocalStoryInference.PrepareGemma(model,budget.Token);
            result["preparation"]=JObject.Parse(JsonUtility.ToJson(prepared));
            result["preparationModelResident"]=LocalStoryInference.HasResidentModel;
            while(LocalStoryRuntimeService.IsPreparing) { budget.Token.ThrowIfCancellationRequested(); await Task.Delay(16); }
            if(LocalStoryRuntimeService.LastPreparation==null) throw new InvalidOperationException("메뉴의 Gemma 사전 검사가 실행되지 않았습니다.");
            if(LocalStoryRuntimeService.LastPreparation!=null)
                result["startupPreparation"]=JObject.Parse(JsonUtility.ToJson(LocalStoryRuntimeService.LastPreparation));
            if(LocalStoryInference.HasResidentModel) throw new InvalidOperationException("파일 사전 검사 중 모델이 메모리에 적재되었습니다.");
            string messages=LocalStoryTestModels.Messages(LocalStoryTestModel.Gemma,
                "[{\"role\":\"system\",\"content\":\"지정된 JSON만 출력한다.\"},{\"role\":\"user\",\"content\":\"연결 검사를 진행한다.\"}]");
            var first=await LocalStoryInference.Generate(root,messages,"root ::= \"{\\\"step\\\":1}\"",12345,budget.Token,null,LocalStorySampling.Comparison(),model);
            var second=await LocalStoryInference.Generate(root,messages,"root ::= \"{\\\"step\\\":2}\"",12345,budget.Token,null,LocalStorySampling.Comparison(),model);
            result["first"]=JObject.Parse(JsonUtility.ToJson(first));result["second"]=JObject.Parse(JsonUtility.ToJson(second));
            if(first.json.Trim()!="{\"step\":1}"||second.json.Trim()!="{\"step\":2}"||!first.verificationCached||first.verificationBytes!=0||!second.sessionReused||second.modelLoadSeconds!=0)
                throw new InvalidOperationException("Player 네이티브 출력/세션 재사용 검사 실패");
            const string storyPath="Initial/Initial_01/Encounters/guide/Mark";
            var report=new LocalStoryTestReport {storyPath=storyPath,model="Gemma",seed=12345,
                original=JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(storyPath))};
            await LocalStoryRuntimePipeline.Infer(report,budget.Token);
            bool valid=report.correctionAttempted?report.correctionValid:report.valid;
            result["storyValid"]=valid;result["sectionsGenerated"]=report.sectionsGenerated;result["authoredCards"]=report.authoredCards;
            result["sectionsCorrected"]=report.sectionsCorrected;
            result["storyDraft"]=report.rawOutput;result["storyCorrection"]=report.correctionRawOutput;
            if(!valid) throw new InvalidOperationException(report.correctionError??report.error??"Player 구간 생성 검사 실패");
            if((bool)result["qwenBundled"]) throw new InvalidOperationException("불필요한 Qwen 모델이 빌드에 남았습니다.");
            result["passed"]=true;
        }
        catch(Exception e) {result["error"]=e.ToString();Debug.LogException(e);}
        finally
        {
            LocalStoryLifetime.CancelAndWait();result["seconds"]=watch.Elapsed.TotalSeconds;
            File.WriteAllText(output,result.ToString(Newtonsoft.Json.Formatting.Indented));
            Debug.Log("[LocalStoryPlayerSmoke] "+result);
            Application.Quit((bool)result["passed"]?0:2);
        }
    }

    private async Task CheckNetworkFallbackDuringGameplay(JObject result, CancellationToken token)
    {
        var menu = FindFirstObjectByType<MainMenuController>();
        if (menu == null) throw new InvalidOperationException("Main menu is missing.");
        menu.OnClickStart();
        while (SceneManager.GetActiveScene().name != "DecisionScene" || LoadingScreenController.IsTransitioning)
            await Task.Delay(30, token);
        await Task.Delay(750, token);

        result["scene"] = SceneManager.GetActiveScene().name;
        result["graphicsApi"] = SystemInfo.graphicsDeviceType.ToString();
        result["targetFps"] = Application.targetFrameRate;
        result["window"] = $"{Screen.width}x{Screen.height}";
        if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 ||
            Application.targetFrameRate != 60 || Screen.fullScreen || Screen.width != 1760 || Screen.height != 990)
            throw new InvalidOperationException("Unexpected player graphics or window settings.");

        var constructor = typeof(SaveIOService).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance,
            null, new[] { typeof(string) }, null);
        var store = (SaveIOService)constructor.Invoke(new object[] { Path.Combine(Path.GetDirectoryName(output), "NetworkQueue") });
        store.SaveProfile(new ProfileData { currentRun = 1 });
        store.SaveRunData(1, "Progress", new GameProgress { runSeed = 9125 });
        var script = MineLearningStation.ReadContent("Stories/CHUNK-02/MAIN-02_V1");
        var rows = script["opening"].OfType<JObject>().Take(3).ToArray();
        var job = new LocalStoryDialogueJob
        {
            jobId = "performance-probe", storyPath = "AuthoredMine/MAIN-02", placeId = "MAIN-02",
            authoredPack = true, authoredStoryPackMode = "Short", characterId = (string)rows[0]["speaker_id"],
            sourceRun = 1, targetRun = 2, revision = 1, sequence = 1, emotion = "당황",
            emotionLines = rows.Select((row, index) => new LocalStoryEmotionLine
            {
                cardIndex = index, authoredLinePath = row.Path, originalText = (string)row["text"],
                speakerId = (string)row["speaker_id"], speechStyle = "반말"
            }).ToList()
        };
        LocalStoryDialogueQueue.Save(store, new LocalStoryDialogueQueueData
        {
            sourceRun = 1, targetRun = 2, authoredStoryPackMode = "Short", finalized = true,
            jobs = new List<LocalStoryDialogueJob> { job }
        });

        var network = new UnavailableTransport();
        var generator = new DirectStoryGenerator(new DirectLlmClient(new DirectLlmOptions(), network,
            _ => new DirectLlmCredentials("smoke-test-only", new string('a', 32)),
            requestTimeoutSeconds: 3, providerCooldown: TimeSpan.FromMinutes(2)));
        samplingFrames = true;
        try { await LocalStoryDialogueQueue.Process(store, job, generator, token); }
        finally { samplingFrames = false; }

        var saved = LocalStoryDialogueQueue.Load(store, 1).jobs.Single();
        result["providerCalls"] = network.Calls;
        result["linesFinished"] = saved.emotionLines.Count(line => line.finished && line.apiProvider == "Gemma");
        result["acceptedLines"] = saved.emotionLines.Count(line => !line.emotionFallback);
        var frames = frameMilliseconds.OrderBy(value => value).ToArray();
        result["frames"] = frames.Length;
        result["p95FrameMs"] = frames.Length == 0 ? -1f : frames[(int)((frames.Length - 1) * .95f)];
        result["maxFrameMs"] = frames.LastOrDefault();
        var residentCheck = await LocalStoryGemmaDialogue.Generate(
            LocalStoryGemmaDialogue.Messages(saved.emotionLines[0], job.emotion, saved.emotionLines[0].originalText, nextRun: true),
            9125, token, nextRun: true);
        result["backend"] = residentCheck.Backend;
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        result["peakWorkingSetMiB"] = process.PeakWorkingSet64 / 1048576;
        if (network.Calls != 3 || (int)result["linesFinished"] != rows.Length || frames.Length < 60 ||
            residentCheck.Backend != "CPU AVX2")
            throw new InvalidOperationException("The offline dialogue queue or render loop did not complete as expected.");
    }

    private sealed class UnavailableTransport : IDirectLlmTransport
    {
        public int Calls { get; private set; }

        public async Task<DirectLlmHttpResponse> SendAsync(DirectLlmHttpRequest request, CancellationToken token)
        {
            Calls++;
            if (request.Provider == DirectLlmProvider.Gemini)
                await Task.Delay(Timeout.Infinite, token);
            return new DirectLlmHttpResponse { StatusCode = request.Provider == DirectLlmProvider.Groq ? 429 : 0 };
        }
    }
}
#endif
