using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using TMPro;

public sealed class BattleContext
{
    public readonly BattleSceneController Owner;
    public BattleStageData Stage => Owner.ActiveStage;
    public PlayerController Player => Owner.player;
    public BattleSceneBattleBoxController Box => Owner.box;
    public Camera Camera => Owner.battleCamera;
    public readonly BattleClock Clock = new BattleClock();
    public readonly BattleCleanupService Cleanup = new BattleCleanupService();
    public readonly BattleArena Arena;
    public readonly Transform RuntimeRoot;
    public readonly Transform StageRoot;
    public readonly Transform Enemies;
    public readonly B1MistController Mist;
    public readonly Image Overlay;
    public readonly TextMeshProUGUI Message;
    public readonly RectTransform MessageBox;
    public readonly Image SlashLine;

    public BattleContext(BattleSceneController owner)
    {
        Owner = owner;
        Arena = new BattleArena(Camera, Box);
        RuntimeRoot = new GameObject("Battle Runtime").transform;
        RuntimeRoot.SetParent(owner.transform, false);
        StageRoot = new GameObject("Stage Presentation").transform;
        StageRoot.SetParent(RuntimeRoot, false);
        Enemies = new GameObject("Enemy Formation").transform;
        Enemies.SetParent(StageRoot, false);
        var mistObject = new GameObject("B1 Moving Mist");
        mistObject.transform.SetParent(StageRoot, false);

        Mist = mistObject.AddComponent<B1MistController>();
        Mist.Initialize(this);

        var canvasObject = new GameObject("Battle Presentation UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(RuntimeRoot, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        Overlay = MakeImage("Screen Cover", canvasObject.transform, new Color(0, 0, 0, 0));
        Stretch(Overlay.rectTransform, Vector2.zero, Vector2.one);
        var border = MakeImage("Dialogue Border", canvasObject.transform, Color.white);
        MessageBox = border.rectTransform;
        Stretch(MessageBox, new Vector2(.13f, .06f), new Vector2(.87f, .30f));
        var black = MakeImage("Dialogue Background", border.transform, Color.black);
        Stretch(black.rectTransform, Vector2.zero, Vector2.one);
        black.rectTransform.offsetMin = Vector2.one * 5;
        black.rectTransform.offsetMax = Vector2.one * -5;
        Message = new GameObject("Dialogue", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        Message.transform.SetParent(black.transform, false);
        Stretch(Message.rectTransform, Vector2.zero, Vector2.one);
        Message.rectTransform.offsetMin = new Vector2(18, 12);
        Message.rectTransform.offsetMax = new Vector2(-18, -12);
        if (owner.legacy.typewriter != null)
            Message.font = owner.legacy.typewriter.GetComponent<TextMeshProUGUI>().font;
        Message.fontSize = 30;
        Message.color = Color.white;
        Message.raycastTarget = false;
        MessageBox.gameObject.SetActive(false);
        SlashLine = MakeImage("Slash Preview", canvasObject.transform, Color.white);
        SlashLine.rectTransform.anchorMin = SlashLine.rectTransform.anchorMax = Vector2.zero;
        SlashLine.rectTransform.pivot = new Vector2(0, .5f);
        SlashLine.gameObject.SetActive(false);
    }

    public static Image MakeImage(string name, Transform parent, Color color)
    {
        var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.transform.SetParent(parent, false);
        image.color = color;
        image.raycastTarget = false;
        return image;
    }
    static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min; rect.anchorMax = max;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
    public void ShowMessage(string message, bool cover)
    {
        Overlay.color = cover ? Color.black : Color.clear;
        Message.text = message;
        MessageBox.gameObject.SetActive(true);
    }
    public void HideMessage()
    {
        MessageBox.gameObject.SetActive(false);
        Overlay.color = Color.clear;
    }
    public void DrawSlash(Vector2 start, Vector2 end, bool visible)
    {
        SlashLine.gameObject.SetActive(visible);
        if (!visible) return;
        var parent = (RectTransform)SlashLine.transform.parent;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, start, null, out var a);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, end, null, out var b);
        var rect = SlashLine.rectTransform;
        rect.localPosition = a;
        rect.sizeDelta = new Vector2(Vector2.Distance(a, b), 3f);
        rect.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
    }
    public void LayoutMist(float alpha)
    {
        Mist.SetVisibility(alpha);
    }
    public GameObject Primitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.transform.localScale = scale;
        var col = go.GetComponent<Collider>();
        if (col != null) { col.enabled = false; Object.Destroy(col); }
        var renderer = go.GetComponent<Renderer>();
        renderer.sharedMaterial = Owner.flatMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return go;
    }
}
