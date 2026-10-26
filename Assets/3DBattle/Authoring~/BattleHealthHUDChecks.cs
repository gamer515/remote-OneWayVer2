using System;
using UnityEngine;
using UnityEngine.UI;
using OneWay.Battle3D;
public static class BattleHealthHUDChecks
{
    static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
    public static string Run()
    {
        Check(Application.isPlaying,"Play required");
        var hud=UnityEngine.Object.FindFirstObjectByType<BattleBossHealthUI>();
        var intro=hud.intro; var battle=hud.battle;
        var player=UnityEngine.Object.FindFirstObjectByType<BattlePlayerHealthUI>();
        player.Refresh();
        Check(!player.transform.Find("Player Health").GetComponent<Image>().enabled,"Player panel visible");
        Check(!player.transform.Find("Player Health/Label").gameObject.activeSelf,"Player title visible");
        Check(!player.valueText.text.Contains("HP"),"HP prefix remains");
        intro.Begin(); Check(intro.IsRunning,"Intro not running");
        Check(hud.visibility.alpha==0,"Visible during intro");
        intro.Stop(); hud.Tick(1); Check(hud.visibility.alpha==0,"Interrupted intro reveals HUD");
        intro.Begin();
        for(int i=0;i<1000 && intro.IsRunning;i++) intro.Tick(.02f);
        Check(intro.Status=="Completed","Intro: "+intro.Status);
        Check(hud.IsRevealing,"Completion did not reveal HUD");
        hud.Tick(.35f);
        Check(Mathf.Abs(hud.bar.anchorMin.x-.265f)<.001f && Mathf.Abs(hud.bar.anchorMax.x-.735f)<.001f,"Halfway width not symmetric");
        hud.Tick(.35f);
        Check(Mathf.Abs(hud.bar.anchorMin.x-.03f)<.001f && Mathf.Abs(hud.bar.anchorMax.x-.97f)<.001f,"Full width wrong");
        Check(hud.visibility.alpha==1 && hud.valueText.enabled,"Completed bar hidden");
        var boss=battle.CurrentStage.boss;
        float before=boss.Health; boss.TakeDamage(10); hud.Refresh();
        Check(boss.Health<before && Mathf.Abs(hud.fill.anchorMax.x-boss.Health/boss.maxHealth)<.001f,"Damage not reflected");
        intro.Begin(); Check(hud.visibility.alpha==0,"Replay did not hide");
        intro.ReturnToStart(); Check(hud.visibility.alpha==0,"Return did not hide");
        battle.SelectStage(battle.SelectedStageIndex); Check(hud.visibility.alpha==0,"Reset did not hide");
        intro.Begin();
        for(int i=0;i<1000 && intro.IsRunning;i++) intro.Tick(.02f);
        Check(intro.Status=="Completed","Final preview intro failed");
        hud.Tick(1);
        ScreenCapture.CaptureScreenshot("Assets/3DBattle/Authoring~/BattleHealthHUDPreview.png");
        return "PASS: player styling, intro hidden, interrupted hidden, five-step completion, symmetric half/full reveal, damage fill, replay/return/stage reset. Preview requested.";
    }
}
