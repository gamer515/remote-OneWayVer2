#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>Opt-in development-player probe. Isolate saves before any scene Awake; never active in release players.</summary>
public sealed class LocalStoryPlayerSmoke : MonoBehaviour
{
    private static string output;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        if(Application.isEditor || !Environment.GetCommandLineArgs().Contains("-localStorySmoke")) return;
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
}
#endif
