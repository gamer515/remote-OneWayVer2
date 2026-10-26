using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;
public static class HealthDamageLayersSetup
{
    static RectTransform Layers(RectTransform parent, RectTransform green)
    {
        var bg=parent.GetComponent<Image>();
        Undo.RecordObject(bg,"Black health background");
        bg.enabled=true; bg.color=Color.black; bg.raycastTarget=false;
        var red=parent.Find("Recent Damage") as RectTransform;
        if(red==null)
        {
            var go=new GameObject("Recent Damage",typeof(RectTransform),typeof(Image));
            Undo.RegisterCreatedObjectUndo(go,"Damage health layer");
            go.transform.SetParent(parent,false); red=(RectTransform)go.transform;
        }
        red.anchorMin=Vector2.zero; red.anchorMax=new Vector2(0,1);
        red.offsetMin=red.offsetMax=Vector2.zero;
        red.GetComponent<Image>().color=new Color(.94f,.06f,.09f,1);
        red.GetComponent<Image>().raycastTarget=false;
        red.SetAsFirstSibling(); green.SetSiblingIndex(1);
        return red;
    }
    public static string Run()
    {
        if(Application.isPlaying) throw new Exception("Edit mode required");
        var p=UnityEngine.Object.FindFirstObjectByType<BattlePlayerHealthUI>();
        var b=UnityEngine.Object.FindFirstObjectByType<BattleBossHealthUI>();
        if(p.gameObject.scene.path!="Assets/Scenes/3DBattleScene.unity") throw new Exception("Wrong scene");
        Undo.RecordObjects(new UnityEngine.Object[]{p,b,b.valueText},"Health damage UI");
        p.damageFill=Layers((RectTransform)p.fill.parent,p.fill);
        b.damageFill=Layers(b.bar,b.fill);
        b.valueText.enabled=false; b.valueText.gameObject.SetActive(false);
        p.valueText.transform.SetAsLastSibling();
        p.Refresh(); b.Hide();
        EditorUtility.SetDirty(p); EditorUtility.SetDirty(b);
        EditorSceneManager.MarkSceneDirty(p.gameObject.scene);
        EditorSceneManager.SaveScene(p.gameObject.scene);
        return "Saved black/red/green layers, boss numbers disabled; player numbers retained.";
    }
    static void Near(float a,float b,string message)
    { if(Mathf.Abs(a-b)>.001f) throw new Exception(message+": "+a+" expected "+b); }
    public static string Check()
    {
        var m=new BattleHealthBarMotion();
        m.Update(1,0,null,null); m.Update(.6f,0,null,null);
        Near(m.Displayed,1,"No instant jump");
        m.Update(.6f,1,null,null); Near(m.Displayed,.8f,"1 second midpoint");
        m.Update(.6f,1,null,null); Near(m.Displayed,.6f,"2 second finish");
        Near(m.DamageEdge,1,"Damage retained after shrink");
        m.Update(.6f,.99f,null,null); Near(m.DamageEdge,1,"Before 3 seconds");
        m.Update(.6f,.02f,null,null); Near(m.DamageEdge,.6f,"Damage expires");
        m.Reset(); m.Update(1,0,null,null); m.Update(.8f,0,null,null);
        m.Update(.8f,2,null,null); m.Update(.4f,0,null,null);
        m.Update(.4f,1.01f,null,null); Near(m.DamageEdge,.8f,"Old hit expires independently");
        m.Update(.4f,1.99f,null,null); Near(m.DamageEdge,.4f,"Second hit expires");
        m.Update(1,0,null,null); Near(m.Displayed,1,"Reset/heal immediate"); Near(m.DamageEdge,1,"Reset clears trail");
        var p=UnityEngine.Object.FindFirstObjectByType<BattlePlayerHealthUI>();
        var b=UnityEngine.Object.FindFirstObjectByType<BattleBossHealthUI>();
        foreach(var green in new[]{p.fill,b.fill})
        {
            var bg=green.parent.GetComponent<Image>();
            if(!bg.enabled || bg.color!=Color.black) throw new Exception("Black background missing");
            if(green.GetSiblingIndex()!=1 || green.parent.Find("Recent Damage").GetSiblingIndex()!=0) throw new Exception("Wrong draw order");
        }
        if(b.valueText.gameObject.activeSelf) throw new Exception("Boss text still visible");
        return "PASS: 2-second interpolation, 3-second damage expiry, independent consecutive hits, heal/reset, black/red/green order, boss text hidden.";
    }
    public static string PlayCheck()
    {
        if(!Application.isPlaying) throw new Exception("Play required");
        var p=UnityEngine.Object.FindFirstObjectByType<BattlePlayerHealthUI>();
        var b=UnityEngine.Object.FindFirstObjectByType<BattleBossHealthUI>();
        b.battle.SelectStage(b.battle.SelectedStageIndex);
        Physics.SyncTransforms();
        b.intro.Begin();
        for(int i=0;i<1000 && b.intro.IsRunning;i++) b.intro.Tick(.02f);
        if(b.intro.Status!="Completed") throw new Exception(b.intro.Status);
        b.Tick(1); p.Refresh();
        p.playerHealth.ignoreDamage=false;
        p.playerHealth.TakeDamage(40); b.battle.CurrentStage.boss.TakeDamage(400);
        p.Refresh(); b.Refresh();
        Near(p.fill.anchorMax.x,1,"Player starts full"); Near(b.fill.anchorMax.x,1,"Boss starts full");
        p.Tick(1); b.Tick(1);
        Near(p.fill.anchorMax.x,.8f,"Player midpoint"); Near(b.fill.anchorMax.x,.8f,"Boss midpoint");
        p.Tick(1); b.Tick(1);
        Near(p.fill.anchorMax.x,.6f,"Player finish"); Near(b.fill.anchorMax.x,.6f,"Boss finish");
        Near(p.damageFill.anchorMax.x,1,"Player red holds"); Near(b.damageFill.anchorMax.x,1,"Boss red holds");
        p.Tick(1.01f); b.Tick(1.01f);
        Near(p.damageFill.anchorMax.x,.6f,"Player red expires"); Near(b.damageFill.anchorMax.x,.6f,"Boss red expires");
        p.playerHealth.hitInvulnerabilitySeconds=0; p.playerHealth.ResetHealth(); p.Refresh();
        p.playerHealth.TakeDamage(20); p.Refresh(); p.Tick(3.1f);
        // A second hit leaves green, red and black simultaneously visible.
        p.playerHealth.TakeAttackDamage(20,p.playerHealth,99,0,true);
        b.battle.CurrentStage.boss.TakeDamage(200); b.Refresh(); b.Tick(1);
        p.Refresh(); p.Tick(1);
        UnityEditor.EditorApplication.isPaused=true;
        ScreenCapture.CaptureScreenshot(System.IO.Path.GetFullPath("Assets/3DBattle/Authoring~/HealthDamageLayersPreview.png"));
        UnityEditor.EditorWindow.GetWindow(System.Type.GetType("UnityEditor.GameView,UnityEditor")).Focus();
        return "PASS: both live health sources interpolate and expire correctly; intro reveal preserved. Paused for preview.";
    }
}
