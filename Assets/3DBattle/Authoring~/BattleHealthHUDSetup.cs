using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;

public static class BattleHealthHUDSetup
{
    static RectTransform Child(string name, Transform parent)
    {
        var found = parent.Find(name) as RectTransform;
        if (found != null) return found;
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Update battle health HUD");
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }
    static T Component<T>(GameObject go) where T : Component
    {
        var component = go.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(go);
    }
    static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
    }
    public static string Run()
    {
        if (Application.isPlaying) throw new Exception("Edit mode required");
        var battle = UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        if (battle.gameObject.scene.path != "Assets/Scenes/3DBattleScene.unity") throw new Exception("Wrong scene");
        var player = battle.GetComponentInChildren<BattlePlayerHealthUI>(true);
        var panel = (RectTransform)player.transform.Find("Player Health");
        Undo.RecordObjects(new UnityEngine.Object[] {panel, panel.GetComponent<Image>(), player.valueText}, "Simplify player HUD");
        panel.GetComponent<Image>().enabled = false;
        panel.Find("Label").gameObject.SetActive(false);
        panel.sizeDelta = new Vector2(360,32);
        var playerBar = (RectTransform)panel.Find("Health Bar");
        Stretch(playerBar); playerBar.GetComponent<Image>().enabled = false;
        player.Refresh();

        var bar = Child("Boss Health", player.transform);
        bar.gameObject.SetActive(false);
        bar.anchorMin = bar.anchorMax = new Vector2(.5f,0);
        bar.pivot = new Vector2(.5f,.5f);
        bar.sizeDelta = new Vector2(0,32); bar.anchoredPosition = new Vector2(0,50);
        var bg = Component<Image>(bar.gameObject);
        bg.color = new Color(.025f,.16f,.07f,.6f); bg.raycastTarget = false;
        Component<RectMask2D>(bar.gameObject);
        var fill = Child("Remaining Health",bar); Stretch(fill);
        var image = Component<Image>(fill.gameObject);
        image.color = new Color(.1f,.75f,.32f); image.raycastTarget = false;
        var value = Child("Health Value",bar); Stretch(value);
        var text = Component<Text>(value.gameObject);
        text.font = player.valueText.font; text.fontSize = 22; text.fontStyle = FontStyle.Bold;
        text.color = Color.white; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
        var shadow = Component<Shadow>(value.gameObject);
        shadow.effectColor = new Color(0,0,0,.8f); shadow.effectDistance = new Vector2(1,-1);
        var hud = Component<BattleBossHealthUI>(bar.gameObject);
        hud.battle = battle; hud.intro = battle.GetComponent<BattleIntroWalkDebug>();
        if(hud.intro == null) hud.intro = UnityEngine.Object.FindFirstObjectByType<BattleIntroWalkDebug>();
        if(hud.intro == null) throw new Exception("Missing five-step intro");
        hud.bar = bar; hud.fill = fill; hud.valueText = text;
        hud.visibility = Component<CanvasGroup>(bar.gameObject);
        hud.revealSeconds = .7f; hud.screenWidth = .94f;
        bar.gameObject.SetActive(true); hud.Hide();
        EditorSceneManager.MarkSceneDirty(battle.gameObject.scene);
        EditorSceneManager.SaveScene(battle.gameObject.scene);
        return "Saved player green/numbers only; boss bottom center, 94% width, 0.7s reveal after five-step completion.";
    }
}
