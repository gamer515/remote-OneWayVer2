using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JourneyMapKit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Direct DecisionScene comparison only. Generation alone never activates game content. Promotion requires explicit selection and confirmation.</summary>
public sealed class LocalStoryTestPanel : MonoBehaviour
{
    private GameObject canvasRoot, panel, applyPanel;
    private TMP_Text applyPreview;
    private UnityEngine.UI.Button applyButton, applyConfirm;
    private LocalStoryTestReport pendingApply;
    private string pendingStage, pendingEditPath, pendingJson;
    private bool pendingUseEdit;
    private int pendingRun;
    private TMP_FontAsset font;
    private TMP_Text pathLabel, statusLabel, originalLabel, generatedLabel, contextLabel;
    private Button generateButton, previousButton, nextButton;
    private UnityEngine.UI.Button inputButton;
    private UnityEngine.UI.Button modelButton;
    private LocalStoryTestModel selectedModel = LocalStoryTestModel.Qwen;
    public LocalStoryTestModel SelectedModel => selectedModel;
    private volatile bool correcting;
    private UnityEngine.UI.Button savedResultsButton;
    private List<string> savedReports = new List<string>();
    private bool readingSaved, rawView;
    private int savedSelected;
    private List<string> paths;
    private int selected, inputScenario;
    private LocalStoryRevisionRequest memory;
    private EncounterCardRoot original;
    public LocalStoryTestReport Report { get; private set; }
    private CancellationTokenSource cancellation;
    private Task job;
    private volatile string streamed;
    private double started;
    private bool inputWasEnabled, ownsInputLock;
    private JourneyBoardInput board;
    public bool IsGenerating => job != null && !job.IsCompleted;
    public static LocalStoryTestPanel Create(DecisionMiniGameTestLauncher launcher)
    {
        var instance = launcher.gameObject.AddComponent<LocalStoryTestPanel>();
        instance.Build(launcher); return instance;
    }
    private void Build(DecisionMiniGameTestLauncher launcher)
    {
        font = FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.font != null)?.font;
        board = FindFirstObjectByType<JourneyBoardInput>();
        canvasRoot = new GameObject("LocalStoryTestCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = Camera.main; canvas.planeDistance = 1f; canvas.sortingOrder = 2000;
        if(canvas.worldCamera == null) canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasRoot.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920,1080); scaler.matchWidthOrHeight = 0.5f;
        var open = MakeButton(canvasRoot.transform, "이야기 LLM 테스트", 1590, 82, 280, 56, () => { launcher.StopTest(); Open(); });
        panel = Rect("StoryComparison",canvasRoot.transform).gameObject;
        Stretch((RectTransform)panel.transform); panel.AddComponent<Image>().color = new Color(0.065f,0.075f,0.09f,0.99f);
        Text(panel.transform,"내장형 이야기 생성 테스트",50,28,1000,55,38);
        applyButton=MakeButton(panel.transform,"게임 확인용 적용",1100,28,260,55,OpenGameApply);
        modelButton = MakeButton(panel.transform,"모델: Qwen",1390,28,280,55,CycleModel);
        MakeButton(panel.transform,"닫기",1700,28,170,55,Close);
        previousButton = MakeButton(panel.transform,"이전 이야기",50,102,190,60,()=>Select(-1));
        pathLabel = Text(panel.transform,"",260,102,1320,60,28);
        nextButton = MakeButton(panel.transform,"다음 이야기",1600,102,270,60,()=>Select(1));
        contextLabel = Text(panel.transform,"",50,175,1310,65,25);
        inputButton = MakeButton(panel.transform,"입력: 실제 기록",1410,175,460,58,CycleInput);
        Text(panel.transform,"원문",50,248,860,45,32);
        Text(panel.transform,"모델 출력 그대로",1010,248,860,45,32);
        originalLabel = ScrollText(panel.transform,"Original",50,296,860,530);
        generatedLabel = ScrollText(panel.transform,"Generated",1010,296,860,530);
        statusLabel = Text(panel.transform,"",50,844,1820,92,25);
        generateButton = MakeButton(panel.transform,"로컬 생성",50,952,230,58,Generate);
        generateButton.GetComponentInChildren<TMP_Text>().text="생성 (Qwen)";
        MakeButton(panel.transform,"취소",300,952,180,58,()=>cancellation?.Cancel());
        savedResultsButton = MakeButton(panel.transform,"저장 결과 보기",1100,952,420,58,()=>
        {
            if(IsGenerating) return;
            if(readingSaved) { readingSaved=false; rawView=false; savedResultsButton.GetComponentInChildren<TMP_Text>().text="저장 결과 보기"; ShowSelected(); SetBusy(false); }
            else OpenSavedResults();
        });
        MakeButton(panel.transform,"결과·편집 폴더",1550,952,320,58,()=>
        {
            string file=readingSaved && savedReports.Count>0 ? savedReports[savedSelected] :
                LocalStoryTestArchive.List().FirstOrDefault(f=>LocalStoryTestArchive.Load(f).archiveId==Report?.archiveId);
            string folder=file==null ? null : System.IO.Path.GetDirectoryName(file);
            if(System.IO.Directory.Exists(folder)) Application.OpenURL(new Uri(folder+System.IO.Path.DirectorySeparatorChar).AbsoluteUri);
            else statusLabel.text="저장 결과를 먼저 생성/불러오세요.";
        });
        Text(panel.transform,"생성은 원문과 게임 저장을 바꾸지 않습니다. 게임 확인용 적용에서 결과/회차를 확인한 뒤 별도로 저장할 수 있습니다.",50,1026,1820,38,22);
        BuildGameApply();
        paths = DiscoverPaths(); panel.SetActive(false);
#if UNITY_EDITOR
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
#endif
    }
    public static List<string> DiscoverPaths()
    {
        var terrain = Resources.Load<TextAsset>("Story_Json_Data/Initial/Initial_01/Terrain");
        var definition = terrain == null ? null : JsonUtility.FromJson<TerrainDefinitionRoot>(terrain.text);
        var queue = new Queue<string>((definition?.TerrainInfo?.places ?? Array.Empty<TerrainPlaceDefinition>()).Select(p=>p.connectStoryCards).Where(p=>!string.IsNullOrWhiteSpace(p)));
        var known = new HashSet<string>(); var result = new List<string>();
        while (queue.Count > 0)
        {
            string path = queue.Dequeue();
            if (!known.Add(path) || !path.StartsWith("Initial/",StringComparison.Ordinal)) continue;
            var asset = Resources.Load<TextAsset>("Story_Json_Data/"+path+"/Story");
            if (asset == null) continue;
            result.Add(path);
            var root = JsonUtility.FromJson<EncounterCardRoot>(asset.text);
            foreach (var c in root.MainStory)
            {
                foreach (string branch in new[] {c.nextStoryPath,c.winStoryPath,c.loseStoryPath,c.errorStoryPath,c.guideAction?.readyStoryPath,c.guideAction?.retryStoryPath,c.guideAction?.charmZeroStoryPath})
                    if (!string.IsNullOrWhiteSpace(branch)) queue.Enqueue(branch);
                foreach (var a in c.choiceActions ?? Array.Empty<StoryChoiceAction>()) if (!string.IsNullOrWhiteSpace(a.storyPath)) queue.Enqueue(a.storyPath);
            }
            if (!path.Contains("/Variants/")) foreach (string v in new[] {"KnowledgeZero","CharmZero","KnowledgeCharmZero"}) queue.Enqueue(path+"/Variants/"+v);
        }
        result.Sort(StringComparer.Ordinal); return result;
    }
    public void Open()
    {
        panel.SetActive(true);
        if (board != null && !ownsInputLock) { inputWasEnabled = board.InputEnabled; board.SetInputEnabled(false); ownsInputLock = true; }
        if (IsGenerating) return;
        if(readingSaved) ShowSavedSelected(); else ShowSelected();
    }
    public void Close()
    {
        cancellation?.Cancel(); applyPanel?.SetActive(false); panel.SetActive(false);
        if (ownsInputLock && board != null) board.SetInputEnabled(inputWasEnabled);
        ownsInputLock = false;
    }
    private void Select(int direction)
    {
        if(readingSaved)
        {
            if(IsGenerating || savedReports.Count==0) return;
            savedSelected=(savedSelected+direction+savedReports.Count)%savedReports.Count; ShowSavedSelected(); return;
        }
        if (IsGenerating || paths.Count == 0) return;
        selected = (selected + direction + paths.Count) % paths.Count; ShowSelected();
    }
    private void ShowSelected()
    {
        UpdateModelLabels();
        if (paths.Count == 0) { statusLabel.text = "테스트할 Initial Story가 없습니다."; generateButton.interactable = false; return; }
        string path = paths[selected];
        if(path!=LocalStoryTestInputs.GuidePath) inputScenario=0;
        memory=inputScenario==0?SaveIOService.Instance.LoadLatestLocalStoryRevision():LocalStoryTestInputs.Create(inputScenario);
        inputButton.GetComponentInChildren<TMP_Text>().text="입력: "+LocalStoryTestInputs.Name(inputScenario);
        inputButton.interactable=path==LocalStoryTestInputs.GuidePath;
        pathLabel.text = (selected+1)+" / "+paths.Count+"  "+path.Replace("Initial/Initial_01/Encounters/","");
        original = JsonUtility.FromJson<EncounterCardRoot>(Resources.Load<TextAsset>("Story_Json_Data/"+path+"/Story").text);
        originalLabel.text = Format(original); generatedLabel.text = "생성 버튼을 눌러 비교하세요.";
        Report = null;
        contextLabel.text = inputScenario>0?"테스트 전용: "+LocalStoryTestInputs.Name(inputScenario)+" / 가상 기억 사용 / 실제 선택/세이브 미변경":
            memory == null ? "실제 완료 회차 기록 없음 — 첫 만남 표현 테스트" :
            $"실제 기록: {memory.sourceRun} -> {memory.targetRun}회차 / {memory.chapterId} / 사건 {memory.events?.Count ?? 0}개 / 능력치 {string.Join("/", memory.finalStats ?? Array.Empty<int>())}";
        statusLabel.text = LocalStoryTestModels.Name(selectedModel)+" / 외부 통신 없음 / 모델 로딩 시간 포함\n"+
            (selectedModel==LocalStoryTestModel.Gemma?"Gemma는 Editor 테스트 전용. 초안·교정 모두 Gemma 사용. 설치 파일 검증 시간 포함.":"초안·교정 모두 Qwen 사용.");
    }
    private void UpdateModelLabels()
    {
        string name=selectedModel==LocalStoryTestModel.Gemma?"Gemma":"Qwen";
        if(readingSaved) name=Report?.model?.IndexOf("Gemma",StringComparison.OrdinalIgnoreCase)>=0?"Gemma":
            Report?.model?.IndexOf("Qwen",StringComparison.OrdinalIgnoreCase)>=0?"Qwen":"저장 결과";
        modelButton.GetComponentInChildren<TMP_Text>().text="모델: "+name;
        generateButton.GetComponentInChildren<TMP_Text>().text=readingSaved?"생성 (읽기 전용)":"생성 ("+name+")";
    }
    private void CycleModel()
    {
        if(IsGenerating || readingSaved) return;
        selectedModel=selectedModel==LocalStoryTestModel.Qwen?LocalStoryTestModel.Gemma:LocalStoryTestModel.Qwen;
        ShowSelected(); SetBusy(false);
    }
    private void CycleInput()
    {
        if(readingSaved && !IsGenerating) { rawView=!rawView; ShowSavedSelected(); return; }
        if(IsGenerating || paths[selected]!=LocalStoryTestInputs.GuidePath) return;
        inputScenario=(inputScenario+1)%4; ShowSelected();
    }
    public void Generate() => GenerateInternal(true);
    // Test-only entry; no persistent preference, Inspector field, or game-model change.
    public void GenerateWithoutWritingExamplesForComparison() => GenerateInternal(false);
    private async void GenerateInternal(bool useWritingExamples)
    {
          if (IsGenerating || LocalStoryRuntimeService.IsBusy || original == null || readingSaved) return;
        cancellation?.Dispose(); cancellation = new CancellationTokenSource(); cancellation.CancelAfter(TimeSpan.FromMinutes(5));
        Report = null; streamed = ""; correcting=false; generatedLabel.text = "초안 모델 로딩 중...";
        var runOriginal = JsonUtility.FromJson<EncounterCardRoot>(JsonUtility.ToJson(original));
        string path = paths[selected], root = System.IO.Path.Combine(Application.streamingAssetsPath,"LocalStory");
        var model=selectedModel; // Capture once: both passes must use the same selection.
        var report = new LocalStoryTestReport { storyPath = path, model=LocalStoryTestModels.Name(model), original = runOriginal, sourceRun = memory?.sourceRun ?? 1,
            targetRun = memory?.targetRun ?? 2, seed = 12345, createdUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") };
        var sampling=LocalStorySampling.Comparison(); // Fixed conditions; only the model selection changes.
        report.samplingProfile=sampling.Name; report.draftParameters=sampling.Parameters(report.seed);
        string modelPath;
        try { if(useWritingExamples) report.examplePrompt = LocalStoryTestArchive.CaptureWritingExamples(path,out report.exampleSummary,out report.exampleSources);
            else { report.examplePrompt=""; report.exampleSources=Array.Empty<string>(); report.exampleSummary="수정 예시 미사용 대조 시험"; report.comparisonMode="NoWritingExamples"; }
            report.prompt = LocalStoryTestModels.Messages(model,LocalStoryTestArchive.AttachWritingExamples(LocalStoryRewrite.Messages(path,runOriginal,memory),report.examplePrompt));
            modelPath=LocalStoryTestModels.PathFor(root,model); }
        catch(Exception e) { statusLabel.text="생성 입력 확인 실패: "+e.Message;
            LocalStoryLog.Write(LocalStoryLogStage.Failed,"테스트 / "+report.model+" / "+path+" / 입력 확인: "+e.Message); return; }
        string writingPolicy=path==LocalStoryTestInputs.GuidePath?LocalStoryRewrite.WritingPolicy:"기존 일반 대화";
        report.testScenario=(inputScenario==0?"ActualRecord":"Synthetic / "+LocalStoryTestInputs.Name(inputScenario))+" / "+writingPolicy;
        if(!useWritingExamples) report.testScenario+=" / 수정 예시 제외 대조";
        report.writingPolicy=writingPolicy;
        report.generationGrammar=LocalStoryRewrite.Grammar(runOriginal);
        report.interactionMemory=memory;
        report.interactionSource=inputScenario==0?"실제 플레이 기록을 입력으로 선택한 시험":"시험용 가상 기억 / "+LocalStoryTestInputs.Name(inputScenario);
        Report = report;
        started = Time.realtimeSinceStartupAsDouble;
        // Store the task so Play-stop/assembly reload can cancel and join native work safely.
        job = RunGeneration(root,modelPath,model,runOriginal,report,cancellation.Token,sampling);
        SetBusy(true);
        try { await job; }
        finally { if (this != null) SetBusy(false); }
    }
    private async Task RunGeneration(string root, string modelPath, LocalStoryTestModel model, EncounterCardRoot source, LocalStoryTestReport report, CancellationToken token, LocalStorySampling sampling)
    {
        var watch = Stopwatch.StartNew();
        LocalStoryLog.Write(LocalStoryLogStage.Working,"테스트 / "+report.model+" / "+report.storyPath+" / 초안 작성 시작 (게임 적용 아님)");
        bool interrupted=false, failed=false;
        try
        {
            var output = await LocalStoryInference.Generate(root,report.prompt,report.generationGrammar,report.seed,token,text=>streamed=text,sampling,modelPath);
            report.draftSeconds = watch.Elapsed.TotalSeconds; report.backend=output.backend; report.fallback=output.fallback; report.rawOutput=output.json;
            LocalStoryRewrite.EvaluateDraft(report, source, memory);
            if(this == null) return;
            ShowOutput(report);
            if(report.storyPath==LocalStoryTestInputs.GuidePath)
            {
                try { report.correctionPrompt=LocalStoryTestModels.Messages(model,LocalStoryTestArchive.AttachWritingExamples(LocalStoryRewrite.CorrectionMessages(report.storyPath,source,memory,report.rawOutput),report.examplePrompt)); }
                catch(Exception e) { report.correctionError=e.Message; }
                if(report.correctionPrompt!=null)
                {
                    token.ThrowIfCancellationRequested();
                    correcting=true; streamed=""; report.correctionAttempted=true;
                    LocalStoryLog.Write(LocalStoryLogStage.Working,"테스트 / "+report.model+" / "+report.storyPath+" / 한국어 교정 시작");
                    var correctionWatch=Stopwatch.StartNew();
                    var preset=LocalStorySampling.Proofreading(); report.correctionParameters=preset.Parameters(report.seed);
                    try
                    {
                        var correction=await LocalStoryInference.Generate(root,report.correctionPrompt,report.generationGrammar,report.seed,token,text=>streamed=text,preset,modelPath);
                        report.correctionRawOutput=correction.json; report.correctionBackend=correction.backend; report.correctionFallback=correction.fallback;
                        report.correctionValid=LocalStoryRewrite.TryBuild(correction.json,source,memory,out report.corrected,out report.correctionError);
                    }
                    catch(OperationCanceledException) { report.correctionError="교정 취소/5분 시간 제한"; throw; }
                    catch(Exception e) { report.correctionError=e.Message; }
                    finally { report.correctionSeconds=correctionWatch.Elapsed.TotalSeconds; }
                }
            }
            statusLabel.text=$"{report.model} / 초안 {report.draftSeconds:F1}초 ({report.samplingProfile}) / 교정 {report.correctionSeconds:F1}초 / {report.backend}\n{report.exampleSummary} / 초안 검사: {(report.valid?"통과":report.error)} / 교정 검사: {(report.correctionValid?"통과":report.correctionError??"미실행")} / 의미·자연스러움은 직접 평가";
        }
        catch(OperationCanceledException)
        {
            interrupted=true;
            if(correcting) report.correctionError="교정 취소/5분 시간 제한"; else report.error="초안 취소/5분 시간 제한";
            if(this!=null) statusLabel.text=report.model+" 생성 취소. 초안/부분 교정 출력은 보관하고 원본은 유지합니다.";
        }
        catch(Exception e)
        {
            failed=true;
            if(correcting) report.correctionError=e.Message; else report.error=e.Message;
            if(this!=null) statusLabel.text=report.model+" 생성 실패: "+e.Message;
        }
        finally
        {
            report.seconds=watch.Elapsed.TotalSeconds;
            if(report.draftSeconds==0) report.draftSeconds=report.seconds;
            // Keep even incomplete/failed output for inspection, never substitute the source in the comparison view.
            if(correcting) { if(string.IsNullOrEmpty(report.correctionRawOutput)) report.correctionRawOutput=streamed; }
            else if(string.IsNullOrEmpty(report.rawOutput) && !string.IsNullOrEmpty(streamed)) report.rawOutput=streamed;
            if(this!=null) { ShowOutput(report); SaveIOService.Instance.SaveLocalStoryTest(report); }
            bool valid=!failed && (report.correctionAttempted?report.correctionValid:report.valid);
            LocalStoryLog.Write(interrupted?LocalStoryLogStage.Deferred:valid?LocalStoryLogStage.Complete:LocalStoryLogStage.Failed,
                "테스트 / "+report.model+" / "+report.storyPath+" / "+report.seconds.ToString("F1")+"초 / "+
                (interrupted?"취소 또는 시간 제한":valid?"출력 검사 통과 (게임 적용 아님)":
                    report.correctionError??report.error??"출력 검사 실패"));
        }
    }
    private void Update()
    {
        if(IsGenerating && panel != null && panel.activeSelf)
        {
            statusLabel.text=$"{Report?.model} / {(correcting?"2/2 한국어 교정":"초안 작성")} 중... {Time.realtimeSinceStartupAsDouble-started:F1}초 / {Report?.exampleSummary} / 외부 통신 없음 / 취소 가능";
            if(!string.IsNullOrEmpty(streamed)) generatedLabel.text=correcting?"[초안]\n"+FormatModelOutput(Report.rawOutput)+"\n\n[한국어 교정본 — 생성 중]\n"+FormatModelOutput(streamed):FormatModelOutput(streamed);
        }
    }
    private void SetBusy(bool busy)
    {
        generateButton.interactable=!busy && !readingSaved && original!=null;
        previousButton.interactable=nextButton.interactable=savedResultsButton.interactable=!busy;
        inputButton.interactable=!busy && (readingSaved || (paths.Count>0 && paths[selected]==LocalStoryTestInputs.GuidePath));
        modelButton.interactable=!busy && !readingSaved;
        applyButton.interactable=!busy && Report!=null && !string.IsNullOrEmpty(Report.archiveId);
    }
    private void BuildGameApply()
    {
        applyPanel=Rect("GameApplyConfirmation",panel.transform).gameObject;
        Stretch((RectTransform)applyPanel.transform);
        applyPanel.AddComponent<UnityEngine.UI.Image>().color=new Color(.03f,.04f,.05f,.99f);
        Text(applyPanel.transform,"게임 확인용 결과 선택",80,60,1760,60,38);
        Text(applyPanel.transform,"자연스러움과 핵심 의미를 읽고 확인하세요. 원문/현재 회차/테스트 결과는 보존하며 다음 회차용 문구만 저장합니다.",80,135,1760,70,25);
        MakeButton(applyPanel.transform,"AI 초안",80,230,310,60,()=>PrepareGameApply("draft",false));
        MakeButton(applyPanel.transform,"AI 교정본",420,230,310,60,()=>PrepareGameApply("correction",false));
        MakeButton(applyPanel.transform,"Edit 초안",760,230,310,60,()=>PrepareGameApply("draft",true));
        MakeButton(applyPanel.transform,"Edit 교정본",1100,230,310,60,()=>PrepareGameApply("correction",true));
        applyPreview=ScrollText(applyPanel.transform,"ApplicationPreview",80,325,1760,570);
        applyConfirm=MakeButton(applyPanel.transform,"내용 확인 후 저장",80,945,700,65,ConfirmGameApply);
        MakeButton(applyPanel.transform,"취소 / 돌아가기",1460,945,380,65,()=>applyPanel.SetActive(false));
        applyPanel.SetActive(false);
    }
    private void OpenGameApply()
    {
        if(IsGenerating || Report==null || string.IsNullOrEmpty(Report.archiveId)) return;
        pendingApply=Report; pendingJson=null; applyConfirm.interactable=false;
        applyPreview.text="AI 초안·교정본 또는 번호별 Edit에서 사용할 단계를 골라 주세요. 검사 실패는 그대로 표시하며 다른 결과나 원문으로 대체하지 않습니다.";
        applyPanel.SetActive(true);
    }
    private void PrepareGameApply(string stage,bool useEdit)
    {
        pendingJson=null; applyConfirm.interactable=false;
        try
        {
            string result=LocalStoryTestArchive.List().FirstOrDefault(f=>LocalStoryTestArchive.Load(f).archiveId==pendingApply.archiveId);
            if(result==null) throw new InvalidOperationException("저장 결과가 없습니다.");
            pendingStage=stage; pendingUseEdit=useEdit; pendingEditPath=LocalStoryTestArchive.EditPath(result);
            var built=LocalStoryGameApply.Build(pendingApply,stage,useEdit,pendingEditPath);
            pendingJson=JsonUtility.ToJson(built); pendingRun=LocalStoryGameApply.NextRun(pendingApply);
            applyPreview.text=$"#{pendingApply.archiveNumber:D4} / {pendingApply.model} / {(useEdit?"Edit":"AI")} {stage}\n"+
                $"저장 대상: {pendingRun}회차 / {pendingApply.storyPath}\n"+
                "자동 검사는 형식, 내부 표기, 숫자, 난독 정상복원 일부만 확인합니다. 사건 의미와 자연스러움을 직접 확인하세요.\n"+
                "메인에서 이 회차가 시작되어 해당 NPC를 만나면 사용합니다. 회차를 강제로 넘기거나 현재 진행을 초기화하지 않습니다.\n\n"+Format(built);
            applyConfirm.GetComponentInChildren<TMP_Text>().text=pendingRun+"회차에 이 내용 저장";
            applyConfirm.interactable=true;
        }
        catch(Exception e) { applyPreview.text="적용할 수 없음: "+e.Message; }
    }
    private void ConfirmGameApply()
    {
        if(pendingJson==null) return;
        try
        {
            if(LocalStoryGameApply.NextRun(pendingApply)!=pendingRun ||
                JsonUtility.ToJson(LocalStoryGameApply.Build(pendingApply,pendingStage,pendingUseEdit,pendingEditPath))!=pendingJson)
                throw new InvalidOperationException("선택 후 원문/Edit/회차가 변경됐습니다. 다시 선택하고 확인하세요.");
            string saved=LocalStoryGameApply.Save(pendingApply,pendingStage,pendingUseEdit,pendingRun,SaveIOService.Instance,pendingEditPath);
            applyPreview.text="게임 확인용 저장 완료: "+saved+"\n원문과 현재 진행은 그대로입니다. 기존 생성 파일이 있었다면 GeneratedContent/Recovery에 보관했습니다.";
            pendingJson=null; applyConfirm.interactable=false;
        }
        catch(Exception e) { applyPreview.text="저장 실패: "+e.Message; applyConfirm.interactable=false; pendingJson=null; }
    }

    private void ShowOutput(LocalStoryTestReport report)
    {
        // Read dialogue straight from the raw patch even if the quality check failed.
        // Never use generated/corrected as a fallback, substitute the source, or mutate rawOutput.
        generatedLabel.text=rawView ? DisplayRawOutput(report) : DisplayOutput(report);
    }
    public static string DisplayOutput(LocalStoryTestReport report)
    {
        string draft=string.IsNullOrEmpty(report.rawOutput)?"모델이 출력한 내용이 없습니다.":LocalStoryTestArchive.DialogueText(report.rawOutput);
        if(!report.correctionAttempted && string.IsNullOrEmpty(report.correctionError)) return draft;
        return "[초안]\n"+draft+"\n\n[한국어 교정본]\n"+
            (string.IsNullOrEmpty(report.correctionRawOutput)?"출력 없음":LocalStoryTestArchive.DialogueText(report.correctionRawOutput))+
            (report.correctionError==null?"":"\n\n교정 검사/실행 결과: "+report.correctionError);
    }
    public static string DisplayRawOutput(LocalStoryTestReport report)
    {
        string draft=string.IsNullOrEmpty(report.rawOutput)?"모델이 출력한 내용이 없습니다.":FormatModelOutput(report.rawOutput);
        if(!report.correctionAttempted && string.IsNullOrEmpty(report.correctionError)) return draft;
        return "[초안]\n"+draft+"\n\n[한국어 교정본]\n"+
            (string.IsNullOrEmpty(report.correctionRawOutput)?"출력 없음":FormatModelOutput(report.correctionRawOutput))+
            (report.correctionError==null?"":"\n\n교정 검사/실행 결과: "+report.correctionError);
    }
    public static string FormatModelOutput(string raw)
    {
        if(string.IsNullOrEmpty(raw) || !raw.TrimStart().StartsWith("{",StringComparison.Ordinal)) return raw;
        var b=new StringBuilder(raw.Length+256);
        bool quoted=false, escaped=false;
        int objects=0, arrays=0;
        for(int i=0;i<raw.Length;i++)
        {
            char c=raw[i]; b.Append(c);
            if(quoted)
            {
                if(escaped) escaped=false;
                else if(c=='\\') escaped=true;
                else if(c=='"') quoted=false;
                continue;
            }
            if(c=='"') { quoted=true; continue; }
            if(c=='{') objects++;
            else if(c=='}') objects--;
            else if(c=='[')
            {
                arrays++;
                int next=i+1; while(next<raw.Length && char.IsWhiteSpace(raw[next])) next++;
                if(next<raw.Length && raw[next]!=']') b.Append(arrays==1?"\n\n":"\n    ");
            }
            else if(c==']') arrays--;
            else if(c==',')
            {
                if(objects==1 && arrays==1) b.Append("\n\n"); // Separate dialogue cards.
                else if(arrays>1) b.Append("\n    "); // One option per line.
                else if(objects==2 && arrays==1) b.Append("\n  "); // Dialogue on its own line.
            }
        }
        return b.ToString();
    }
    public void OpenSavedResults()
    {
        if(IsGenerating) return;
        try
        {
            string importWarning=null;
            try { LocalStoryTestArchive.ImportExisting(); }
            catch(Exception e) { importWarning="추가 정리 실패(기존 결과는 표시): "+e.Message; }
            savedReports=LocalStoryTestArchive.List();
            readingSaved=true; rawView=false; savedSelected=Math.Max(0,savedReports.Count-1);
            savedResultsButton.GetComponentInChildren<TMP_Text>().text="새 생성으로 돌아가기";
            ShowSavedSelected(); SetBusy(false);
            if(importWarning!=null) statusLabel.text += "\n"+importWarning;
        }
        catch(Exception e) { statusLabel.text="저장 결과 읽기 실패: "+e.Message; }
    }
    private void ShowSavedSelected()
    {
        if(savedReports.Count==0) { Report=null; generatedLabel.text="저장된 결과가 없습니다."; return; }
        try
        {
            Report=LocalStoryTestArchive.Load(savedReports[savedSelected]);
            UpdateModelLabels();
            originalLabel.text=Report.original?.MainStory==null?"원문 없음":Format(Report.original);
            pathLabel.text=$"{savedSelected+1}/{savedReports.Count}  #{Report.archiveNumber:D4}  {Report.model}\n{Report.storyPath}";
            contextLabel.text=$"저장 결과 / {Report.testScenario} / 시드 {Report.seed} / 읽기 전용";
            inputButton.GetComponentInChildren<TMP_Text>().text=rawView?"대사로 보기":"원출력 JSON 보기";
            statusLabel.text=$"{Report.model} / 합계 {Report.seconds:F1}초 / 초안 {Report.draftSeconds:F1}초 / 교정 {Report.correctionSeconds:F1}초 / {Report.backend}\n자동 검사: {(Report.valid?"통과":Report.error)} / 품질 평가는 HTML에서 — 적용 버튼으로 선택한 결과만 별도 저장";
            ShowOutput(Report);
            foreach(var scroll in panel.GetComponentsInChildren<UnityEngine.UI.ScrollRect>()) scroll.verticalNormalizedPosition=1f;
        }
        catch(Exception e) { statusLabel.text="결과 파일 판독 실패: "+e.Message; generatedLabel.text="결과를 불러오지 못했습니다. 원문으로 대체하지 않습니다."; }
    }
    private void Shutdown()
    {
        cancellation?.Cancel();
        // The model is native: do not unload a managed callback while inference is still using it.
        // Wait only for native worker completion, not Unity-context continuations.
        // An idle test panel must not block scene changes on the background game's native worker.
        if(IsGenerating) LocalStoryLifetime.WaitForWorkers();
    }
    private void OnDestroy()
    {
        Shutdown();
#if UNITY_EDITOR
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
#endif
        cancellation?.Dispose(); if(canvasRoot!=null) Destroy(canvasRoot);
    }
    public static string Format(EncounterCardRoot root)
    {
        var b=new StringBuilder();
        for(int i=0;i<root.MainStory.Count;i++)
        { var c=root.MainStory[i]; b.Append((i+1)+". "+c.text+"\n"); for(int j=0;j<(c.options?.Length??0);j++) b.Append("   "+(j+1)+") "+c.options[j]+"\n"); b.Append('\n'); }
        return b.ToString();
    }
    private static RectTransform Rect(string name,Transform parent)
    { var go=new GameObject(name,typeof(RectTransform)); go.transform.SetParent(parent,false); return (RectTransform)go.transform; }
    private static void Place(RectTransform r,float x,float y,float w,float h)
    { r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1); r.anchoredPosition=new Vector2(x,-y); r.sizeDelta=new Vector2(w,h); }
    private static void Stretch(RectTransform r)
    { r.anchorMin=Vector2.zero; r.anchorMax=Vector2.one; r.offsetMin=r.offsetMax=Vector2.zero; }
    private TMP_Text Text(Transform parent,string value,float x,float y,float w,float h,float size)
    {
        var r=Rect("Label",parent); Place(r,x,y,w,h); var t=r.gameObject.AddComponent<TextMeshProUGUI>(); t.font=font; t.text=value;
        t.fontSize=size; t.color=new Color(0.96f,0.94f,0.86f); t.raycastTarget=false; t.richText=false; return t;
    }
    private Button MakeButton(Transform parent,string label,float x,float y,float w,float h,UnityEngine.Events.UnityAction action)
    {
        var r=Rect(label,parent); Place(r,x,y,w,h); var image=r.gameObject.AddComponent<Image>(); image.color=new Color(0.25f,0.23f,0.17f);
        var button=r.gameObject.AddComponent<Button>(); button.targetGraphic=image; button.onClick.AddListener(action);
        var t=Text(r,label,8,0,w-16,h,27); t.alignment=TextAlignmentOptions.Center;
        return button;
    }
    private TMP_Text ScrollText(Transform parent,string name,float x,float y,float w,float h)
    {
        var r=Rect(name,parent); Place(r,x,y,w,h); var scroll=r.gameObject.AddComponent<ScrollRect>(); scroll.horizontal=false;
        var view=Rect("Viewport",r); Stretch(view); view.gameObject.AddComponent<Image>().color=new Color(.11f,.13f,.15f);
        view.gameObject.AddComponent<Mask>().showMaskGraphic=true;
        var content=Rect("Content",view); content.anchorMin=new Vector2(0,1); content.anchorMax=Vector2.one; content.pivot=new Vector2(.5f,1);
        content.offsetMin=new Vector2(16,0); content.offsetMax=new Vector2(-16,0);
        var layout=content.gameObject.AddComponent<VerticalLayoutGroup>(); layout.childControlWidth=true; layout.childControlHeight=true; layout.childForceExpandHeight=false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
        var t=Text(content,"",0,0,w-32,h,30); t.gameObject.AddComponent<LayoutElement>();
        scroll.viewport=view; scroll.content=content; scroll.movementType=ScrollRect.MovementType.Clamped; scroll.scrollSensitivity=45;
        return t;
    }
}
