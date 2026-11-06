using System;
using System.Reflection;
using UnityEngine;
using OneWay.Battle3D;
public static class VerifyTenSpinHits
{
    public static object Run()
    {
        if(!Application.isPlaying)throw new Exception("Play mode required");
        var boss=UnityEngine.Object.FindFirstObjectByType<BattleBoss>();
        boss.GetComponent<BossApproachController>().automaticApproach=false;
        boss.GetComponent<BossActionController>().RequestStop();
        var attack=boss.GetComponent<BossPhaseTwo>().entryAttack;
        var hit=boss.GetComponent<BossWeaponHit>();
        var health=boss.GetComponent<BossPatternRunner>().Target;
        var player=health.GetComponent<BattlePlayerController>();
        var collider=health.GetComponent<Collider>();
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var contact=typeof(BossWeaponHit).GetMethod("TryHit",flags);
        var defense=typeof(BattlePlayerController).GetMethod("SetDefenseMode",flags);
        var block=typeof(BattlePlayerController).GetField("blockRemaining",flags);
        var effect=health.GetComponent<BattleParryEffect>();
        var oldEffect=effect.effectPrefab;effect.effectPrefab=null;
        health.ignoreDamage=false;health.maxHealth=1000;health.ResetHealth();
        defense.Invoke(player,new object[]{false});
        hit.Begin(attack,health);
        int damageCount=0,parryCount=0;
        for(int i=0;i<10;i++)
        {
            var window=attack.GetHitWindow(i);
            if(attack.GetStrikeIndex((window.startSeconds+window.endSeconds)*.5f)!=i)throw new Exception("Window mismatch");
            if(!(bool)contact.Invoke(hit,new object[]{collider,i}))throw new Exception("New turn damage rejected: "+i);
            if((bool)contact.Invoke(hit,new object[]{collider,i}))throw new Exception("Same turn damaged twice");
            damageCount++;
        }
        float expected=1000-attack.damage*10;
        if(Mathf.Abs(health.Health-expected)>.01f)throw new Exception("Incorrect damage total");
        health.ResetHealth();defense.Invoke(player,new object[]{true});block.SetValue(player,100f);
        hit.Begin(attack,health);
        for(int i=0;i<10;i++)
        {
            if(!(bool)contact.Invoke(hit,new object[]{collider,i}))throw new Exception("New turn parry rejected: "+i);
            if((bool)contact.Invoke(hit,new object[]{collider,i}))throw new Exception("Same turn parried twice");
            parryCount++;
        }
        if(health.Health!=1000||hit.HitCount!=0)throw new Exception("Parry caused damage");
        hit.End();effect.effectPrefab=oldEffect;defense.Invoke(player,new object[]{false});
        return new {damageCount,parryCount,duplicateContactsBlocked=true,independent=attack.independentStrikes,valid=attack.TimingIsValid()};
    }
}
