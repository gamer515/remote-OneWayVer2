using System;
using System.Linq;
using TMPro;
using UnityEngine;

/// <summary>A small Canvas view for the requested waiting/race progress and return-to-place controls.</summary>
public sealed class MineChapterActivityView : MonoBehaviour
{
    private TextMeshProUGUI text;
    private UnityEngine.UI.Button[] buttons;
    private Action<int> selected;
    public void Initialize()
    {
        var font = FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.font != null)?.font;
        var canvas = gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 90;
        var scaler = gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 900);
        gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        var panel = new GameObject("ActivityPanel", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        panel.transform.SetParent(transform, false);
        var rect = (RectTransform)panel.transform;
        rect.anchorMin = new Vector2(0.02f, 0.72f); rect.anchorMax = new Vector2(0.40f, 0.97f); rect.offsetMin = rect.offsetMax = Vector2.zero;
        panel.GetComponent<UnityEngine.UI.Image>().color = new Color(0.035f, 0.06f, 0.09f, 0.94f);
        var label = new GameObject("Progress", typeof(RectTransform), typeof(TextMeshProUGUI)); label.transform.SetParent(panel.transform, false);
        var lr = (RectTransform)label.transform; lr.anchorMin = new Vector2(0.03f, 0.48f); lr.anchorMax = new Vector2(0.97f, 0.97f); lr.offsetMin = lr.offsetMax = Vector2.zero;
        text = label.GetComponent<TextMeshProUGUI>(); text.font = font; text.fontSize = 21; text.raycastTarget = false;
        buttons = new UnityEngine.UI.Button[4];
        for (int i = 0; i < buttons.Length; i++)
        {
            int slot = i;
            var go = new GameObject("Action_" + i, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
            go.transform.SetParent(panel.transform, false);
            var br = (RectTransform)go.transform; br.anchorMin = new Vector2(0.03f + i % 2 * 0.48f, 0.04f + (1 - i / 2) * 0.20f);
            br.anchorMax = br.anchorMin + new Vector2(0.45f, 0.17f); br.offsetMin = br.offsetMax = Vector2.zero;
            go.GetComponent<UnityEngine.UI.Image>().color = new Color(0.13f, 0.25f, 0.30f);
            buttons[i] = go.GetComponent<UnityEngine.UI.Button>(); buttons[i].onClick.AddListener(() => selected?.Invoke(slot));
            var caption = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)); caption.transform.SetParent(go.transform, false);
            var cr = (RectTransform)caption.transform; cr.anchorMin = Vector2.zero; cr.anchorMax = Vector2.one; cr.offsetMin = cr.offsetMax = Vector2.zero;
            var tmp = caption.GetComponent<TextMeshProUGUI>(); tmp.font = font; tmp.fontSize = 18; tmp.alignment = TextAlignmentOptions.Center; tmp.raycastTarget = false;
        }
    }
    public void Show(string message, string[] actions = null, Action<int> onSelect = null)
    {
        gameObject.SetActive(true); text.text = message; selected = onSelect;
        for (int i = 0; i < buttons.Length; i++)
        {
            bool visible = i < (actions?.Length ?? 0); buttons[i].gameObject.SetActive(visible);
            if (visible) buttons[i].GetComponentInChildren<TextMeshProUGUI>().text = actions[i];
        }
    }
    public void Hide() => gameObject.SetActive(false);
}
