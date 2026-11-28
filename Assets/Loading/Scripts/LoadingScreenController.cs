using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OneWay.Loading
{
    public sealed class LoadingScreenController : MonoBehaviour
    {
        public const string LoadingScenePath = "Assets/Scenes/LoadingScene.unity";
        private const float ResumeMinimumDisplayTime = .5f;

        [Header("Destination / 목적지")]
        [Tooltip("Battle: 마우스 조작 안내 / Decision: LoadDecisionScene 문구 / MainMenu: 문구 없음")]
        public LoadingDestination destination = LoadingDestination.Battle;

        [Header("Loading bar / 로딩바")]
        [Tooltip("도움말과 빈 로딩바를 먼저 표시하고, 이 시간이 지나면 로딩바를 채우기 시작합니다.")]
        [Min(0f)] public float fillStartDelay = 5f;

        [Header("Standalone preview / 촬영용 미리보기")]
        [Tooltip("LoadingScene을 직접 실행하면 다음 씬으로 이동하지 않고 로딩바만 재생합니다. Open()으로 연결된 실제 전환에는 적용되지 않습니다.")]
        public bool previewOnly = false;
        [Min(.1f)] public float previewDuration = 6f;

        [Header("Actual loading / 실제 로딩")]
        [Tooltip("실제 로딩이 빨리 끝나도 로딩 화면을 최소한 이 시간만큼 표시합니다.")]
        [Min(0f)] public float minimumDisplayTime = 2f;
        [Min(0f)] public float fadeOutDuration = .25f;

        [Header("UI references")]
        public LoadingTipCatalog tipCatalog;
        public TMP_Text tipLabel;
        public RectTransform fill;
        public RectTransform marker;
        public CanvasGroup canvasGroup;

        [Header("Battle-only presentation / 전투 진입 전용 화면")]
        public GameObject battleInstructions;
        public GameObject battleLoadingBar;
        public RectTransform battleFill;
        public GameObject standardLoadingBar;
        [Min(0f), Tooltip("전투 조작 안내를 먼저 보여 준 뒤 로딩바가 채워지기 시작하는 시간입니다.")]
        public float battleFillStartDelay = 5f;
        public float EffectiveFillStartDelay => standaloneBattle != null ? 8f :
            destination == LoadingDestination.Battle && battleInstructions != null ? battleFillStartDelay : fillStartDelay;
        public bool IsStandaloneBattleEntry => standaloneBattle != null;

        public float DisplayedProgress { get; private set; }
        public float DisplaySecondsElapsed => Mathf.Max(0f, Time.realtimeSinceStartup - displayStartedAt);
        public bool IsPreview { get; private set; }
        public bool IsComplete { get; private set; }
        public string LastError { get; private set; }
        public string SelectedTip => tipLabel != null ? tipLabel.text : string.Empty;
        public static bool IsTransitioning => requestPending;

        static (LoadingDestination target, bool startProgressImmediately)? pendingRequest;
        static bool requestPending;
        static (LoadingDestination target, bool startProgressImmediately)? queuedRequest;
        static LoadingScreenController activeScreen;
        static RetainedBattle pendingStandaloneBattle;
        RetainedBattle standaloneBattle;
        Scene standaloneLoadingScene;
        bool quitting;
        float displayStartedAt;
        bool activatingDestination;
        bool startProgressImmediately;
        AsyncOperation operation;
        Coroutine routine;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRequest()
        {
            pendingRequest = null;
            requestPending = false;
            queuedRequest = null;
            activeScreen = null;
            pendingStandaloneBattle = null;
        }

        sealed class RetainedBattle
        {
            public readonly Scene scene;
            readonly GameObject[] roots;
            readonly bool[] active;
            bool restored;

            public RetainedBattle(Scene source)
            {
                scene = source; roots = source.GetRootGameObjects(); active = new bool[roots.Length];
                for (int i = 0; i < roots.Length; i++) active[i] = roots[i].activeSelf;
            }
            public void Suspend()
            {
                for (int i = 0; i < roots.Length; i++) if (active[i] && roots[i] != null) roots[i].SetActive(false);
            }
            public void Resume()
            {
                if (restored || !scene.IsValid() || !scene.isLoaded) return;
                restored = true; SceneManager.SetActiveScene(scene);
                for (int i = 0; i < roots.Length; i++) if (active[i] && roots[i] != null) roots[i].SetActive(true);
            }
        }

        // Keep the initial scene instances so unsaved Inspector values and the selected
        // boss survive. No default Decision data is manufactured for this entry path.
        public static bool OpenBeforeStandaloneBattle(Scene battleScene)
        {
            if (!Application.isPlaying || requestPending || !battleScene.IsValid() || !battleScene.isLoaded ||
                battleScene.path != ScenePath(LoadingDestination.Battle) || !CanLoadScene(LoadingScenePath)) return false;
            var retained = new RetainedBattle(battleScene);
            requestPending = true;
            pendingRequest = (LoadingDestination.Battle, false);
            pendingStandaloneBattle = retained;
            try
            {
                retained.Suspend();
                if (LoadScene(LoadingScenePath, LoadSceneMode.Additive) == null)
                    throw new InvalidOperationException("독립 실행용 로딩 화면을 열지 못했습니다.");
                return true;
            }
            catch { retained.Resume(); ResetRequest(); throw; }
        }

        public static string ScenePath(LoadingDestination target)
        {
            switch (target)
            {
                case LoadingDestination.Battle: return "Assets/Scenes/3DBattleScene.unity";
                case LoadingDestination.Decision: return "Assets/Scenes/DecisionScene.unity";
                case LoadingDestination.MainMenu: return "Assets/Scenes/MainMenuScene.unity";
                default: throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown loading destination.");
            }
        }

        static bool CanLoadScene(string path)
        {
            if (Application.CanStreamedLevelBeLoaded(path)) return true;
#if UNITY_EDITOR
            // Test scenes in the Editor without silently editing the team's Build Settings.
            return UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>(path) != null;
#else
            return false;
#endif
        }

        static AsyncOperation LoadScene(string path, LoadSceneMode mode = LoadSceneMode.Single)
        {
#if UNITY_EDITOR
            if (!Application.CanStreamedLevelBeLoaded(path))
                return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                    path, new LoadSceneParameters(mode));
#endif
            return SceneManager.LoadSceneAsync(path, mode);
        }

        /// <summary>
        /// Request a destination through LoadingScene. beforeLoad runs only for accepted requests,
        /// before any scene is replaced, so callers can hand off battle data safely.
        /// </summary>
        public static bool Open(
            LoadingDestination target, Action beforeLoad = null, bool startProgressImmediately = false)
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("로딩 전환은 Play 중에만 요청할 수 있습니다.");
                return false;
            }
            if (!Enum.IsDefined(typeof(LoadingDestination), target) ||
                !CanLoadScene(LoadingScenePath) || !CanLoadScene(ScenePath(target)))
            {
                Debug.LogError("목적지를 확인하고 LoadingScene과 목적지 씬을 Build Profiles의 Scene List에 추가해야 합니다.");
                return false;
            }
            if (requestPending)
            {
                // Decision.Start can resume an unfinished battle during scene activation.
                // Queue that redirect until this overlay is gone; reject duplicate button clicks.
                if (activeScreen == null || !activeScreen.activatingDestination ||
                    target == activeScreen.destination || queuedRequest.HasValue) return false;
                queuedRequest = (target, startProgressImmediately);
                try { beforeLoad?.Invoke(); }
                catch { queuedRequest = null; throw; }
                return true;
            }
            requestPending = true;
            pendingRequest = (target, startProgressImmediately);
            try
            {
                beforeLoad?.Invoke();
                if (LoadScene(LoadingScenePath) == null)
                    throw new InvalidOperationException("LoadingScene 로딩을 시작하지 못했습니다.");
            }
            catch { ResetRequest(); throw; }
            return true;
        }

        void Start()
        {
            bool requested = pendingRequest.HasValue;
            standaloneBattle = pendingStandaloneBattle;
            pendingStandaloneBattle = null;
            if (standaloneBattle != null) standaloneLoadingScene = gameObject.scene;
            if (pendingRequest is { } request)
            {
                destination = request.target;
                startProgressImmediately = request.startProgressImmediately;
            }
            pendingRequest = null;
            IsPreview = !requested && previewOnly;
            if (!IsPreview)
            {
                activeScreen = this;
                requestPending = true;
                Time.timeScale = 1f;
                ReleaseCursor();
            }
            Begin();
        }

        void Begin()
        {
            displayStartedAt = Time.realtimeSinceStartup;
            IsComplete = false;
            LastError = null;
            if (canvasGroup != null) canvasGroup.alpha = 1f;
            RefreshPresentation();
            SetProgress(0f);
            routine = StartCoroutine(IsPreview ? Preview() : standaloneBattle != null ? StartRetainedBattle() : LoadDestination());
        }

        public void ReplayPreview()
        {
            if (!Application.isPlaying || !IsPreview) return;
            if (routine != null) StopCoroutine(routine);
            Begin();
        }

        // Select presentation by destination, never by the scene the player came from.
        // Decision keeps the same catalog and random selection for every entry route.
        public void RefreshPresentation()
        {
            bool battle = destination == LoadingDestination.Battle && battleInstructions != null;
            if (battleInstructions != null) battleInstructions.SetActive(battle);
            if (battleLoadingBar != null) battleLoadingBar.SetActive(battle);
            if (standardLoadingBar != null) standardLoadingBar.SetActive(!battle);
            if (tipLabel != null)
            {
                tipLabel.gameObject.SetActive(!battle);
                tipLabel.text = battle ? string.Empty : tipCatalog != null ? tipCatalog.Pick(destination) : string.Empty;
            }
            SetProgress(DisplayedProgress);
        }

        void ShowLoadError()
        {
            if (battleInstructions != null) battleInstructions.SetActive(false);
            if (tipLabel == null) return;
            tipLabel.gameObject.SetActive(true);
            tipLabel.text = "화면을 불러오지 못했습니다.";
        }

        IEnumerator Preview()
        {
            if (EffectiveFillStartDelay > 0f) yield return new WaitForSecondsRealtime(EffectiveFillStartDelay);
            float elapsed = 0f;
            float duration = Mathf.Max(.1f, previewDuration);
            while (elapsed < duration)
            {
                yield return null;
                elapsed += Time.unscaledDeltaTime;
                SetProgress(Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration)));
            }
            SetProgress(1f);
            IsComplete = true;
            routine = null;
        }

        IEnumerator LoadDestination()
        {
            string path = ScenePath(destination);
            if (!CanLoadScene(path))
            {
                LastError = "목적지 씬이 Build Profiles의 Scene List에 없습니다: " + path;
                Debug.LogError(LastError, this);
                ShowLoadError();
                ResetRequest();
                yield break;
            }

            // Keep the overlay through activation and initialization of the destination scene.
            DontDestroyOnLoad(gameObject);
            yield return null;
            float started = Time.realtimeSinceStartup;
            try { operation = LoadScene(path); }
            catch (Exception exception) { LastError = exception.Message; }
            if (operation == null)
            {
                LastError = "씬 로딩을 시작하지 못했습니다: " + path + " " + LastError;
                Debug.LogError(LastError, this);
                ShowLoadError();
                ResetRequest();
                yield break;
            }
            operation.allowSceneActivation = false;
            // Load the destination during the delay; only the bar presentation waits.
            float delay = startProgressImmediately ? 0f : EffectiveFillStartDelay;
            float displayTime = startProgressImmediately ? ResumeMinimumDisplayTime : minimumDisplayTime;
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
            while (true)
            {
                float minimumProgress = displayTime <= 0f ? 1f :
                    Mathf.Clamp01((Time.realtimeSinceStartup - started) / displayTime);
                float readyProgress = Mathf.Clamp01(operation.progress / .9f);
                float target = Mathf.Min(readyProgress, minimumProgress);
                SetProgress(Mathf.MoveTowards(DisplayedProgress, target, Time.unscaledDeltaTime * 1.5f));
                if (readyProgress >= 1f && minimumProgress >= 1f && DisplayedProgress >= 1f) break;
                yield return null;
            }
            // Ensure the completed bar is drawn before allowing scene activation.
            yield return null;
            activatingDestination = true;
            operation.allowSceneActivation = true;
            yield return operation;
            yield return null;
            yield return FinishTransition();
        }

        IEnumerator StartRetainedBattle()
        {
            DontDestroyOnLoad(gameObject);
            yield return null; // Draw the instructions before measuring the eight-second wait.
            yield return new WaitForSecondsRealtime(EffectiveFillStartDelay);
            while (DisplayedProgress < 1f)
            {
                SetProgress(Mathf.MoveTowards(DisplayedProgress, 1f, Time.unscaledDeltaTime * 1.5f));
                yield return null;
            }
            activatingDestination = true;
            standaloneBattle.Resume();
            if (standaloneLoadingScene.IsValid() && standaloneLoadingScene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(standaloneLoadingScene);
            yield return null;
            yield return FinishTransition();
        }

        IEnumerator FinishTransition()
        {
            IsComplete = true;
            float fade = 0f;
            while (fade < fadeOutDuration)
            {
                fade += Time.unscaledDeltaTime;
                if (canvasGroup != null) canvasGroup.alpha = 1f - Mathf.Clamp01(fade / fadeOutDuration);
                yield return null;
            }
            if (destination != LoadingDestination.Battle) ReleaseCursor();
            var redirect = queuedRequest;
            ResetRequest();
            Destroy(gameObject);
            if (redirect is { } next)
                Open(next.target, startProgressImmediately: next.startProgressImmediately);
        }

        static void ReleaseCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void SetProgress(float value)
        {
            DisplayedProgress = Mathf.Clamp01(value);
            UpdateFill(fill, DisplayedProgress);
            UpdateFill(battleFill, DisplayedProgress);
            if (marker != null)
            {
                // Keep the marker wholly over the capsule, including its rounded end caps.
                var bar = marker.parent as RectTransform;
                float inset = bar != null && bar.rect.width > 0f
                    ? Mathf.Min(.5f, marker.rect.width * .5f / bar.rect.width) : 0f;
                float x = Mathf.Lerp(inset, 1f - inset, DisplayedProgress);
                marker.anchorMin = marker.anchorMax = new Vector2(x, 1f);
            }
        }

        static void UpdateFill(RectTransform rect, float progress)
        {
            if (rect == null) return;
            rect.anchorMax = new Vector2(progress, 1f);
            rect.gameObject.SetActive(progress > 0f);
        }

        void OnDestroy()
        {
            // Do not leave Unity's async scene queue blocked if this overlay is removed early.
            if (operation != null && !operation.isDone) operation.allowSceneActivation = true;
            if (activeScreen == this)
            {
                if (!quitting && Application.isPlaying && standaloneBattle != null)
                {
                    standaloneBattle.Resume();
                    if (standaloneLoadingScene.IsValid() && standaloneLoadingScene.isLoaded)
                        SceneManager.UnloadSceneAsync(standaloneLoadingScene);
                }
                ResetRequest();
            }
        }

        void OnApplicationQuit() => quitting = true;
    }
}
