using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;

public static class PlayerHealthUISetup
{
    static RectTransform Rect(string name, Transform parent)
    {
        var go=new GameObject(name,typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go,"Create player health UI");
        go.transform.SetParent(parent,false);
        return (RectTransform)go.transform;
    }
    static Image Image(RectTransform rect, Color color)
    {
        var image=rect.gameObject.AddComponent<Image>();
        image.color=color; image.raycastTarget=false; return image;
    }
    static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin=Vector2.zero; rect.anchorMax=Vector2.one;
        rect.offsetMin=min; rect.offsetMax=max;
    }
    public static string Setup()
    {
        if(Application.isPlaying) throw new Exception("Edit mode required");
        var battle=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        if(battle.gameObject.scene.path!="Assets/Scenes/3DBattleScene.unity") throw new Exception("Wrong scene");
        if(battle.GetComponentInChildren<BattlePlayerHealthUI>(true)!=null) return "Health UI already present";
        var root=Rect("Player Health HUD",battle.transform);
        var canvas=root.gameObject.AddComponent<Canvas>();
        canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.sortingOrder=20;
        var scaler=root.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution=new Vector2(1920,1080); scaler.matchWidthOrHeight=.5f;
        var panel=Rect("Player Health",root);
        panel.anchorMin=panel.anchorMax=panel.pivot=Vector2.one;
        panel.anchoredPosition=new Vector2(-28,-28); panel.sizeDelta=new Vector2(360,88);
        Image(panel,new Color(.035f,.055f,.08f,.92f));
        var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var label=Rect("Label",panel);
        Stretch(label,new Vector2(16,48),new Vector2(-16,-8));
        var title=label.gameObject.AddComponent<Text>();
        title.font=font; title.text="PLAYER"; title.fontSize=18;
        title.color=new Color(.8f,.85f,.9f); title.alignment=TextAnchor.MiddleLeft; title.raycastTarget=false;
        var bar=Rect("Health Bar",panel);
        Stretch(bar,new Vector2(16,14),new Vector2(-16,-42));
        Image(bar,new Color(.65f,.08f,.12f));
        var fill=Rect("Remaining Health",bar);
        Stretch(fill,Vector2.zero,Vector2.zero);
        Image(fill,new Color(.1f,.75f,.32f));
        var value=Rect("Health Value",bar);
        Stretch(value,Vector2.zero,Vector2.zero);
        var text=value.gameObject.AddComponent<Text>();
        text.font=font; text.fontSize=20; text.fontStyle=FontStyle.Bold;
        text.alignment=TextAnchor.MiddleCenter; text.color=Color.white; text.raycastTarget=false;
        var shadow=value.gameObject.AddComponent<Shadow>(); shadow.effectColor=new Color(0,0,0,.8f);
        shadow.effectDistance=new Vector2(1,-1);
        var hud=root.gameObject.AddComponent<BattlePlayerHealthUI>();
        hud.playerHealth=battle.player.GetComponent<BattlePlayerHealth>(); hud.fill=fill; hud.valueText=text;
        hud.Refresh();
        EditorSceneManager.MarkSceneDirty(battle.gameObject.scene);
        EditorSceneManager.SaveScene(battle.gameObject.scene);
        Selection.activeGameObject=panel.gameObject;
        return "Top-right player HP HUD created; full green/red remainder; non-interactive; reference 1920x1080.";
    }
}
