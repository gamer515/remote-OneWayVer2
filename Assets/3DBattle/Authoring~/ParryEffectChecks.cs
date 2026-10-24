using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using OneWay.Battle3D;
public static class ParryEffectChecks
{
    static void Check(bool ok,string label){if(!ok)throw new Exception(label);}
    static int Count()=>UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Count(t=>t.name=="Player Parry Hit Blue");
    public static string Run()
    {
        Check(Application.isPlaying,"Play mode required");
        var b=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        b.SelectStage(b.SelectedStageIndex);
        var p=b.player; var h=p.GetComponent<BattlePlayerHealth>(); h.ignoreDamage=false;
        var w=b.CurrentStage.boss.GetComponent<BossWeaponHit>();
        var pattern=AssetDatabase.LoadAssetAtPath<BossWeaponAttackPattern>("Assets/3DBattle/Settings/B1_Attack7_Smooth10_20.asset");
        var method=typeof(BossWeaponHit).GetMethod("TryHit",BindingFlags.Instance|BindingFlags.NonPublic);
        var collider=p.GetComponent<CharacterController>();
        Func<int,bool> contact=i=>(bool)method.Invoke(w,new object[]{collider,i});
        int count=Count();
        p.ProcessControlFrame(-1,true,false,0);
        Check(p.IsBlocking,"Shield not active"); Check(Count()==count,"Shield without contact spawned FX");
        w.Begin(pattern,h); float health=h.Health;
        Check(contact(0),"Parry not accepted"); Check(h.Health==health,"Parry damaged player");
        Check(Count()==count+1,"Missing parry effect");
        Check(!contact(0) && Count()==count+1,"Duplicate parry");
        Check(contact(1) && Count()==count+2,"Next combo strike missing effect");
        p.ProcessControlFrame(1,false,false,0);
        Check(!contact(0) && h.Health==health,"Deflected swing damaged after block ended");
        w.Begin(pattern,h); Check(contact(0),"Normal hit rejected");
        Check(h.Health<health && Count()==count+2,"Normal hit spawned parry effect");
        var fx=UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).First(t=>t.name=="Player Parry Hit Blue");
        var systems=fx.GetComponentsInChildren<ParticleSystem>();
        foreach(var ps in systems) ps.Simulate(.05f,false,true,true);
        Check(systems.Sum(ps=>ps.particleCount)>0,"No visible particles emitted");
        w.End();
        return "PASS: shield alone no FX; parry no damage; one FX per strike; next combo strike independent; no late damage from parried swing; normal hits no FX; particles emit.";
    }
}
