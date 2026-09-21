#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class BattleConfigurationValidator
{
    const string ScenePath = "Assets/Scenes/BattleScene.unity";
    [MenuItem("One-Way/Validate Battle Configuration")]
    public static void Validate()
    {
        var scene = EditorSceneManager.OpenPreviewScene(ScenePath);
        int assertions = 0;
        Action<bool, string> check = (condition, message) =>
        { if (!condition) throw new InvalidOperationException(message); assertions++; };
        try
        {
            var objects = scene.GetRootGameObjects();
            var controller = objects.SelectMany(o => o.GetComponentsInChildren<BattleSceneController>(true)).Single();
            check(controller.player != null && controller.box != null && controller.attacks != null && controller.battleCamera != null && controller.legacy != null, "Missing scene references");
            check(controller.legacy.sequenceController == controller, "Legacy bridge is disconnected");
            check(controller.heart != null && AssetDatabase.GetAssetPath(controller.heart) == "Assets/Battle/BattleScenePlayerHeart.png", "Incorrect player heart");
            check(controller.mist != null && AssetDatabase.GetAssetPath(controller.mist) == "Assets/Battle/mist.png", "Incorrect mist");
            check(controller.flatMaterial != null && controller.flatMaterial.shader.name == "OneWay/BattleUnlit", "Missing unlit material");
            check(!ShaderUtil.GetShaderMessages(controller.flatMaterial.shader).Any(m => m.severity.ToString() == "Error"), "Battle shader compile error");
            check(controller.stages != null && controller.stages.Length == 3, "Three stages required");
            for (int i = 0; i < 3; i++)
            {
                var stage = controller.stages[i];
                check(stage != null && stage.stageIndex == i + 1, "Invalid stage index");
                check(stage.flow != null && stage.flow.steps.Count > 0, "Stage has no steps");
                foreach (var step in stage.flow.steps)
                {
                    check(Enum.IsDefined(typeof(BattleStepKind), step.kind), "Unknown step kind");
                    if (step.kind == BattleStepKind.Pattern)
                        check(step.pattern != null && step.pattern.duration > 0 && step.pattern.interval > 0 && step.pattern.speed > 0, "Invalid pattern timing");
                }
            }
            var b1 = controller.stages[0];
            check(Mathf.Abs(b1.forwardViewportY - 1f / 3f) < .0001f, "B1 forward line must be one third from bottom");
            check(b1.flow.steps.Single(s => s.pattern.kind == BattlePatternKind.Debris).pattern.duration == 60, "B1 must last 60 active seconds");
            check(b1.recoverySeconds == 5, "B1 warning must last five seconds");
            var b2 = controller.stages[1];
            int slash = b2.flow.steps.FindIndex(s => s.kind == BattleStepKind.Slash);
            check(slash > 0 && slash + 1 < b2.flow.steps.Count, "Missing slash insertion");
            check(b2.flow.steps[slash - 1].pattern.legacyCase == 4 && b2.flow.steps[slash + 1].pattern.legacyCase == 5, "Slash must connect case4 to case5");
            check(b2.slashTargetCount == 5 && b2.slashStartZ > 0 && b2.slashSpeed > 0, "Invalid slash targets");
            var buttons = objects.SelectMany(o => o.GetComponentsInChildren<Button>(true)).ToArray();
            for (int i = 1; i <= 3; i++)
            {
                string method = "SetBattleStage" + i;
                check(buttons.Any(b => Enumerable.Range(0, b.onClick.GetPersistentEventCount()).Any(n =>
                    b.onClick.GetPersistentMethodName(n) == method && b.onClick.GetPersistentTarget(n) is BattleDebugController)), "Missing " + method + " button callback");
            }
            var debugCanvas = objects.SelectMany(o => o.GetComponentsInChildren<Canvas>(true)).Single(c => c.name == "SceneChangeDebug");
            check(debugCanvas.renderMode == RenderMode.ScreenSpaceOverlay && debugCanvas.sortingOrder > 100, "Stage buttons must remain above transitions");

            check(SlashTarget3D.CrossedArrival(.2f, -.3f), "Overshooting Z=0 must hit");
            check(!SlashTarget3D.CrossedArrival(-.2f, -.3f), "An arrival must not repeat");
            check(!SlashTarget3D.CrossedArrival(.2f, .1f), "Positive Z must not hit");
            check(Mathf.Abs(SlashResolver.DistanceToSegment(new Vector2(3, 1), Vector2.zero, new Vector2(2, 0)) - Mathf.Sqrt(2)) < .0001f, "Slash must use a finite segment");
            check(Mathf.Abs(SlashResolver.DistanceToSegment(Vector2.one, Vector2.zero, Vector2.zero) - Mathf.Sqrt(2)) < .0001f, "Zero-length slash distance");
            var arena = new BattleArena(controller.battleCamera, controller.box) { Mode = BattleArena.Boundary.Screen };
            Rect rect = arena.ScreenRect;
            Vector2 clamped = arena.Clamp(new Vector2(1000, -1000), new Vector2(.3f, .25f));
            check(Mathf.Abs(clamped.x - (rect.xMax - .3f)) < .001f && Mathf.Abs(clamped.y - (rect.yMin + .25f)) < .001f, "B3 must include player extents in screen clamp");
            CheckMesh(check);
            string result = "Battle validation passed: " + assertions + " assertions. Scene links, buttons, stage order, arrival, slash geometry and bounds verified.";
            Debug.Log(result);
            WriteReport(result);
        }
        catch (Exception exception) { WriteReport(exception.ToString()); throw; }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
    static void CheckMesh(Action<bool, string> check)
    {
        var source = new Mesh
        {
            vertices = new[] { new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f),
                new Vector3(.5f,.5f,-.5f), new Vector3(-.5f,.5f,-.5f), new Vector3(-.5f,-.5f,.5f),
                new Vector3(.5f,-.5f,.5f), new Vector3(.5f,.5f,.5f), new Vector3(-.5f,.5f,.5f) },
            triangles = new[] { 0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,3,7,6,3,6,2,0,4,7,0,7,3,1,2,6,1,6,5 }
        };
        var left = SlashResolver.Clip(source, new Plane(Vector3.right, Vector3.zero), true);
        var right = SlashResolver.Clip(source, new Plane(Vector3.right, Vector3.zero), false);
        try
        {
            check(left.vertexCount > 0 && right.vertexCount > 0, "Both cut fragments must contain geometry");
            check(left.vertices.All(v => v.x >= -.0001f) && right.vertices.All(v => v.x <= .0001f), "Fragments must lie on opposite sides of the cut");
            check(Mathf.Abs(left.bounds.size.x - .5f) < .001f && Mathf.Abs(right.bounds.size.x - .5f) < .001f, "Cut halves must retain original dimensions");
        }
        finally { UnityEngine.Object.DestroyImmediate(left); UnityEngine.Object.DestroyImmediate(right); UnityEngine.Object.DestroyImmediate(source); }
    }
    static void WriteReport(string text)
    {
        string directory = Path.Combine(Path.GetTempPath(), "OneWayBattleValidation");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "UnityValidation.txt"), text);
    }

    // Batch-only integration harness. It never edits/saves the user's project or stage assets.
    public static void RunSmokeBatch()
    {
        if (!Application.dataPath.Replace('\\', '/').Contains("/OneWayBattleValidation/UnityProject/"))
            throw new InvalidOperationException("RunSmokeBatch is restricted to the temporary validation project.");
        Validate();
        SessionState.SetBool("BattleSmokeRunning", true);
        SessionState.SetInt("BattleSmokeStage", 1);
        PrepareSmokeScene();
    }
    static void PrepareSmokeScene()
    {
        
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var controller = UnityEngine.Object.FindFirstObjectByType<BattleSceneController>();
        for (int i = 0; i < controller.stages.Length; i++)
        {
            var copy = UnityEngine.Object.Instantiate(controller.stages[i]);
            copy.returnToDecision = false;
            copy.flow.steps.RemoveAll(s => s.kind == BattleStepKind.Dialogue);
            if (i != 1) foreach (var step in copy.flow.steps)
                if (step.kind == BattleStepKind.Pattern) step.pattern.duration = 2;
            controller.stages[i] = copy;
        }
        controller.player.maxHp = 1000;
        
        EditorApplication.isPlaying = true;
    }
    static double smokeStart, warningStart;
    static bool pushedRear, sawWarning, warningFinished, testedCut, checkedArrival, pushedBounds, checkedBounds, pressedButton;
    static Vector3 frozenPosition;
    static Vector3[] frozenDebris;
    static float slashHp;
    static int boundsFrame;
    static readonly System.Collections.Generic.HashSet<string> captured = new System.Collections.Generic.HashSet<string>();
    static void Capture(BattleSceneController c, string name)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || captured.Contains(name)) return;
        captured.Add(name);
        var overlays = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(canvas => canvas.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
        var cameras = overlays.Select(canvas => canvas.worldCamera).ToArray();
        var planes = overlays.Select(canvas => canvas.planeDistance).ToArray();
        float aspect = c.battleCamera.aspect;
        var texture = new RenderTexture(1280, 720, 24);
        var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            c.battleCamera.aspect = 1280f / 720;
            foreach (var canvas in overlays)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = c.battleCamera; canvas.planeDistance = .5f;
            }
            Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(c.battleCamera, new UniversalRenderPipeline.SingleCameraRequest { destination = texture });
            RenderTexture.active = texture;
            pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
            string path = Path.Combine(Path.GetTempPath(), "OneWayBattleValidation", name + ".png");
            File.WriteAllBytes(path, pixels.EncodeToPNG());
            Debug.Log("BATTLE_CAPTURE: " + path);
        }
        finally
        {
            RenderTexture.active = previous;
            for (int i = 0; i < overlays.Length; i++)
            {
                overlays[i].renderMode = RenderMode.ScreenSpaceOverlay;
                overlays[i].worldCamera = cameras[i]; overlays[i].planeDistance = planes[i];
            }
            c.battleCamera.aspect = aspect;
            UnityEngine.Object.DestroyImmediate(pixels);
            texture.Release(); UnityEngine.Object.DestroyImmediate(texture);
        }
    }
    static void SmokeUpdate()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            int stage = SessionState.GetInt("BattleSmokeStage", 1);
            if (smokeStart == 0) { smokeStart = EditorApplication.timeSinceStartup; Time.timeScale = 3; }
            if (EditorApplication.timeSinceStartup - smokeStart > 120) throw new Exception("Smoke test timed out");
            var c = UnityEngine.Object.FindFirstObjectByType<BattleSceneController>();
            if (c == null || c.Context == null) return;
            if (c.Result == BattleResult.Failed) throw new Exception("Unexpected smoke player death");
            
            if (stage == 1 && c.CurrentStep.Contains("Wind"))
            {
                if (c.Context.Clock.Paused) Capture(c, "B1-warning");
                else if (!pushedRear) Capture(c, "B1-corridor");
            }
            if (stage == 2 && c.CurrentStep == "Case 1") Capture(c, "B2-box");
            if (stage == 2 && c.Context.RuntimeRoot.GetComponentsInChildren<Transform>().Any(t => t.name == "Slash Target 1")) Capture(c, "B2-slash");
            if (stage == 3 && c.CurrentStep.Contains("fracture") && c.Context.RuntimeRoot.GetComponentsInChildren<Transform>().Any(t => t.name == "Crack")) Capture(c, "B3-fracture");
            if (stage == 3 && c.CurrentStep.Contains("radial") && !pushedBounds) Capture(c, "B3-arena");
            if (stage == 1)
            {
                if (!pushedRear && c.CurrentStep.Contains("Wind") && c.Context.RuntimeRoot.GetComponentsInChildren<Transform>().Any(t => t.name == "Debris"))
                {
                    c.player.transform.position = new Vector3(0, c.Context.Arena.Bounds.yMin, 0);
                    pushedRear = true;
                }
                if (c.Context.Clock.Paused)
                {
                    var debris = c.Context.RuntimeRoot.GetComponentsInChildren<Transform>().Where(t => t.name == "Debris").Select(t => t.position).ToArray();
                    if (!sawWarning)
                    {
                        sawWarning = true; warningStart = EditorApplication.timeSinceStartup;
                        frozenPosition = c.player.transform.position; frozenDebris = debris;
                    }
                    if (c.player.Body.simulated || c.Context.Clock.Delta != 0 || c.player.transform.position != frozenPosition || !debris.SequenceEqual(frozenDebris))
                        throw new Exception("B1 warning did not freeze gameplay");
                }
                else if (sawWarning && !warningFinished)
                {
                    if (EditorApplication.timeSinceStartup - warningStart < 4.8) throw new Exception("B1 warning ended early");
                    if (!c.player.Body.simulated) throw new Exception("B1 physics did not resume");
                    warningFinished = true;
                }
            }
            if (stage == 2 && !c.battleCamera.orthographic && c.CurrentStep.Contains("2-a"))
            {
                if (!testedCut)
                {
                    var scope = c.Context.Cleanup.Create("Smoke Cut", c.Context.RuntimeRoot);
                    var target = new SlashTarget3D(c.Context, scope, new Vector3(0, 0, 12), 99);
                    Vector2 center = c.battleCamera.WorldToScreenPoint(target.Object.transform.position);
                    float before = c.player.currentHp;
                    if (!target.TrySlash(center + Vector2.left * 180, center + Vector2.right * 180) || !target.Resolved)
                        throw new Exception("3D target slash failed");
                    target.Tick(4);
                    if (c.player.currentHp != before) throw new Exception("Cut target caused damage");
                    c.Context.Cleanup.Release(scope);
                    testedCut = true; slashHp = c.player.currentHp;
                }
            }
            if (stage == 2 && testedCut && !checkedArrival && c.CurrentStep == "Case 5")
            {
                if (Mathf.Abs(slashHp - c.player.currentHp - 5) > .001f || !c.battleCamera.orthographic)
                    throw new Exception("Five missed targets or camera restoration failed");
                checkedArrival = true;
            }
            if (stage == 3 && c.CurrentStep.Contains("radial"))
            {
                if (!pushedBounds) { c.player.transform.position = new Vector3(1000, 1000, 0); boundsFrame = Time.frameCount; pushedBounds = true; }
                else if (!checkedBounds && Time.frameCount > boundsFrame + 1)
                {
                    Vector2 actual = c.player.transform.position;
                    Vector2 expected = c.Context.Arena.Clamp(actual, c.player.HalfSize);
                    if ((actual - expected).sqrMagnitude > .0001f) throw new Exception("B3 escaped screen bounds");
                    checkedBounds = true;
                }
            }
            if (pressedButton && c.ActiveStage.stageIndex == 1)
            {
                Debug.Log("BATTLE_SMOKE_PASSED: B1 pause/resume, all B2 cases, 3D cut/misses, B3 bounds, stage button reload.");
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "OneWayBattleValidation", "SmokeValidation.txt"), "PASS: B1 pause/resume; B2 cases1-9, 3D mesh cut, five arrival damages and camera restore; B3 bounds; stage button reload.");
                SessionState.SetBool("BattleSmokeRunning", false);
                Time.timeScale = 1; EditorApplication.Exit(0); return;
            }
            if (c.Result == BattleResult.Completed && !pressedButton)
            {
                if (stage == 1 && !warningFinished) throw new Exception("B1 recovery was not exercised");
                if (stage == 2 && (!testedCut || !checkedArrival)) throw new Exception("B2 slash was not exercised");
                if (stage == 3 && !checkedBounds) throw new Exception("B3 bounds were not exercised");
                Debug.Log("BATTLE_SMOKE_STAGE_PASSED: B" + stage);
                if (stage == 3)
                {
                    pressedButton = true;
                    var button = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b => b.name == "Button1");
                    button.onClick.Invoke();
                }
                else { SessionState.SetInt("BattleSmokeStage", stage + 1); EditorApplication.isPlaying = false; }
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "OneWayBattleValidation", "SmokeValidation.txt"), exception.ToString());
            SessionState.SetBool("BattleSmokeRunning", false); EditorApplication.Exit(1);
        }
    }
    [InitializeOnLoadMethod]
    static void ResumeSmoke()
    {
        if (!SessionState.GetBool("BattleSmokeRunning", false)) return;
        EditorApplication.update += SmokeUpdate;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool("BattleSmokeRunning", false))
                EditorApplication.delayCall += PrepareSmokeScene;
        };
    }
    // Read-only preview-scene validation after this implementation is imported.
    [InitializeOnLoadMethod]
    static void ValidateAfterImport()
    {
        if (SessionState.GetBool("OneWayBattleValidatedV1", false)) return;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            SessionState.SetBool("OneWayBattleValidatedV1", true);
            try { Validate(); } catch (Exception exception) { Debug.LogException(exception); }
        };
    }

    [RuntimeInitializeOnLoadMethod(
    RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void SupplySmokeBattleData()
    {
        if (!SessionState.GetBool("BattleSmokeRunning", false))
            return;

        // 기존 검증 코드와 동일하게 임시 검증 프로젝트로 제한합니다.
        string projectPath = Application.dataPath.Replace('\\', '/');

        if (!projectPath.Contains(
                "/OneWayBattleValidation/UnityProject/"))
        {
            return;
        }

        int stage = SessionState.GetInt("BattleSmokeStage", 1);

        BattleEntry.SetNext(
            new BattleStartData(stage, 1, 1, 1, 1));
    }
}
#endif
