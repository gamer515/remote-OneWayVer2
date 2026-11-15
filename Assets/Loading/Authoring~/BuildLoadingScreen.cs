using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using OneWay.Loading;
using OneWay.Loading.Editor;

public static class BuildLoadingScreen
{
    public static object Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode required.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != LoadingScreenController.LoadingScenePath)
            throw new InvalidOperationException("Open the existing LoadingScene before building.");
        if (scene.GetRootGameObjects().Any(x => x.name == "LoadingScreen"))
            throw new InvalidOperationException("LoadingScreen already exists; inspect before modifying it.");
        var catalog = LoadingTipCatalogUpdater.Sync();
        var font = CreateFont(catalog);
        var camera = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Camera>(true)).FirstOrDefault();
        if (camera == null) throw new InvalidOperationException("LoadingScene camera missing.");
        Undo.RecordObject(camera, "Set loading background");
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.cullingMask = 0;
        camera.allowHDR = false;

        var root = new GameObject("LoadingScreen");
        Undo.RegisterCreatedObjectUndo(root, "Create loading screen");
        var controller = root.AddComponent<LoadingScreenController>();
        controller.canvasGroup = root.AddComponent<CanvasGroup>();
        controller.tipCatalog = catalog;
        var canvasRect = Rect("LoadingCanvas", root.transform);
        var canvas = canvasRect.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;
        var scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;
        canvas.gameObject.AddComponent<GraphicRaycaster>();

        var background = Rect("BlackBackground", canvasRect);
        Stretch(background);
        background.gameObject.AddComponent<Image>().color = Color.black;

        var tipRect = Rect("RandomTip", canvasRect);
        tipRect.anchorMin = new Vector2(.13f, .625f);
        tipRect.anchorMax = new Vector2(.89f, .625f);
        tipRect.sizeDelta = new Vector2(0f, 180f);
        var label = tipRect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.fontSize = 32f;
        label.enableAutoSizing = true;
        label.fontSizeMin = 24f;
        label.fontSizeMax = 32f;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Overflow;
        label.color = Color.white;
        label.richText = false;
        label.raycastTarget = false;
        label.text = catalog.Pick(LoadingDestination.Battle);
        controller.tipLabel = label;

        var bar = Rect("LoadingBar", canvasRect);
        bar.anchorMin = new Vector2(.13f, .13f);
        bar.anchorMax = new Vector2(.89f, .13f);
        bar.sizeDelta = new Vector2(0f, 64f);
        var outline = bar.gameObject.AddComponent<LoadingShapeGraphic>();
        outline.color = Color.white;
        outline.raycastTarget = false;

        var inside = Rect("RoundedClip", bar);
        Stretch(inside);
        inside.offsetMin = Vector2.one * 6f;
        inside.offsetMax = Vector2.one * -6f;
        var maskGraphic = inside.gameObject.AddComponent<LoadingShapeGraphic>();
        maskGraphic.color = Color.black;
        maskGraphic.raycastTarget = false;
        inside.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var fill = Rect("CurrentProgress", inside);
        Stretch(fill);
        var white = fill.gameObject.AddComponent<Image>();
        white.color = Color.white;
        white.raycastTarget = false;
        controller.fill = fill;

        Canvas.ForceUpdateCanvases();
        SetDisplay(controller, .65f);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);
        return Inspect();
    }

    static TMP_FontAsset CreateFont(LoadingTipCatalog catalog)
    {
        const string path = "Assets/Loading/LoadingKoreanFont.asset";
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (font != null) return font;
        var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/Decision_YYS/Fonts/ChosunCentennial_ttf.ttf");
        if (source == null) throw new InvalidOperationException("Korean font source not found.");
        font = TMP_FontAsset.CreateFontAsset(source, 40, 5, GlyphRenderMode.SDFAA,
            1024, 1024, AtlasPopulationMode.Dynamic, true);
        font.name = "Loading Korean Font";
        AssetDatabase.CreateAsset(font, path);
        foreach (var atlas in font.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, font);
        AssetDatabase.AddObjectToAsset(font.material, font);
        string glyphs = "화면을 불러오지 못했습니다." + string.Concat(catalog.battleTips.Concat(catalog.decisionTips).Select(x => x.text));
        font.TryAddCharacters(glyphs);
        EditorUtility.SetDirty(font);
        EditorUtility.SetDirty(font.material);
        foreach (var atlas in font.atlasTextures) EditorUtility.SetDirty(atlas);
        AssetDatabase.SaveAssetIfDirty(font);
        return font;
    }

    static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchoredPosition = Vector2.zero;
        return rect;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    static void SetDisplay(LoadingScreenController screen, float value)
    {
        typeof(LoadingScreenController).GetMethod("SetProgress", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Invoke(screen, new object[] { value });
    }

    public static object Inspect()
    {
        var screen = UnityEngine.Object.FindFirstObjectByType<LoadingScreenController>();
        if (screen == null) throw new InvalidOperationException("LoadingScreen not found.");
        return new {
            scene = screen.gameObject.scene.path, screen.previewOnly, screen.previewDuration, screen.fillStartDelay,
            destination = screen.destination.ToString(), screen.IsPreview, screen.IsComplete,
            screen.DisplayedProgress, screen.SelectedTip, screen.LastError,
            battleTips = screen.tipCatalog.battleTips.Length,
            decisionTips = screen.tipCatalog.decisionTips.Length,
            font = AssetDatabase.GetAssetPath(screen.tipLabel.font),
            missingGlyphs = screen.tipLabel.text.Where(c => !char.IsWhiteSpace(c) && !screen.tipLabel.font.HasCharacter(c)).Distinct().ToArray(),
            roots = SceneManager.GetActiveScene().GetRootGameObjects().Select(x => x.name).ToArray()
        };
    }

    public static object PreviewAtHalf()
    {
        var screen = UnityEngine.Object.FindFirstObjectByType<LoadingScreenController>();
        if (!Application.isPlaying || screen == null || !screen.IsPreview) throw new InvalidOperationException("Preview play mode required.");
        screen.StopAllCoroutines();
        SetDisplay(screen, .65f);
        Canvas.ForceUpdateCanvases();
        return Inspect();
    }

    public static object RepairRenderers()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Edit mode required.");
        foreach (var shape in UnityEngine.Object.FindObjectsByType<LoadingShapeGraphic>(FindObjectsSortMode.None))
        {
            if (shape.gameObject.scene.path != LoadingScreenController.LoadingScenePath) continue;
            if (shape.GetComponent<CanvasRenderer>() == null) Undo.AddComponent<CanvasRenderer>(shape.gameObject);
            shape.SetAllDirty();
            var mask = shape.GetComponent<Mask>();
            if (mask != null) { mask.enabled = false; mask.enabled = true; }
        }
        Canvas.ForceUpdateCanvases();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        return Inspect();
    }

    public static object RemoveMarker()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Edit mode required.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != LoadingScreenController.LoadingScenePath)
            throw new InvalidOperationException("LoadingScene must be active.");
        var root = scene.GetRootGameObjects().Single(x => x.name == "LoadingScreen");
        var screen = root.GetComponent<LoadingScreenController>();
        Undo.RecordObject(screen, "Remove loading marker");
        screen.marker = null;
        var marker = root.transform.Find("LoadingCanvas/LoadingBar/RedMarker");
        if (marker != null) Undo.DestroyObjectImmediate(marker.gameObject);
        EditorUtility.SetDirty(screen);
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        return new { saved, markerRemoved = root.transform.Find("LoadingCanvas/LoadingBar/RedMarker") == null,
            referenceCleared = screen.marker == null, barPresent = screen.fill != null, textPresent = screen.tipLabel != null };
    }

    public static object SetFillDelay()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Edit mode required.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != LoadingScreenController.LoadingScenePath)
            throw new InvalidOperationException("LoadingScene must be active.");
        var screen = scene.GetRootGameObjects().Single(x => x.name == "LoadingScreen").GetComponent<LoadingScreenController>();
        Undo.RecordObject(screen, "Delay loading bar");
        screen.fillStartDelay = 5f;
        SetDisplay(screen, 0f);
        EditorUtility.SetDirty(screen);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return Inspect();
    }

    public static async System.Threading.Tasks.Task<object> ObservePreviewTiming()
    {
        var screen = UnityEngine.Object.FindFirstObjectByType<LoadingScreenController>();
        if (!Application.isPlaying || screen == null || !screen.IsPreview)
            throw new InvalidOperationException("Preview play mode required.");
        bool previousBackground = Application.runInBackground;
        Application.runInBackground = true;
        try
        {
            screen.ReplayPreview();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var readings = new System.Collections.Generic.List<object>();
            foreach (double at in new[] { 0d, 4.5d, 5.5d, 11.5d })
            {
                int remaining = (int)Math.Max(0, at * 1000d - clock.Elapsed.TotalMilliseconds);
                if (remaining > 0) await System.Threading.Tasks.Task.Delay(remaining);
                readings.Add(new { seconds = clock.Elapsed.TotalSeconds, progress = screen.DisplayedProgress,
                    complete = screen.IsComplete, tip = screen.SelectedTip, fillVisible = screen.fill.gameObject.activeSelf });
            }
            return readings;
        }
        finally { Application.runInBackground = previousBackground; }
    }
}
