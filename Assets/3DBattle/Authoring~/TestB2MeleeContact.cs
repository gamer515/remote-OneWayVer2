using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using OneWay.Battle3D;
public static class TestB2MeleeContact
{
 public static object Run(bool expanded=false)
 {
  if(!Application.isPlaying)throw new Exception("Play mode required");
  var ai=UnityEngine.Object.FindFirstObjectByType<B2CombatController>();var boss=ai.GetComponent<BattleBoss>();
  var player=ai.battle.player;var health=player.GetComponent<BattlePlayerHealth>();var motor=player.GetComponent<CharacterController>();
  var hit=boss.GetComponent<BossWeaponHit>();var animator=boss.animator;var move=ai.movement;
  var bossPos=boss.transform.position;var bossRot=boss.transform.rotation;var playerPos=player.transform.position;
  ai.enabled=false;move.enabled=false;animator.speed=1;animator.fireEvents=false;health.ignoreDamage=false;player.enabled=false;
  var savedSize=hit.localSize;var testPatterns=new List<BossWeaponAttackPattern>();
  foreach(var p in ai.attacks){var copy=UnityEngine.Object.Instantiate(p);if(expanded)for(int i=0;i<copy.hitWindows.Length;i++){copy.hitWindows[i].downwardExtension=9;copy.hitWindows[i].startSeconds=Mathf.Max(0,copy.hitWindows[i].startSeconds-.05f);}testPatterns.Add(copy);}
  if(expanded)hit.localSize=new Vector3(.006f,.0012f,.013f);
  var result=new List<object>();
  try
  {
   boss.transform.SetPositionAndRotation(new Vector3(1000,0,1000),Quaternion.identity);
   foreach(float distance in new[]{4.4f,5f,5.6f})foreach(var attack in testPatterns)
   {
    health.ResetHealth();motor.enabled=false;player.transform.position=boss.transform.position+Vector3.forward*distance;motor.enabled=true;Physics.SyncTransforms();
    animator.Play(attack.animationState,0,0);animator.Update(0);hit.Begin(attack,health);
    bool preparationSafe=true;
    for(int frame=0;frame<=Mathf.CeilToInt(attack.clip.length*120);frame++)
    {
     float time=Mathf.Min(frame/120f,attack.clip.length);
     animator.Play(attack.animationState,0,time/attack.clip.length);animator.Update(0);Physics.SyncTransforms();hit.SampleContact();
     if(time<attack.hitWindows[0].startSeconds&&health.Health!=health.maxHealth)preparationSafe=false;
    }
    result.Add(new{attack=attack.name,distance,hitCount=hit.HitCount,strikes=attack.StrikeCount,damage=health.maxHealth-health.Health,preparationSafe});hit.End();
   }
   return result;
  }
  finally
  {
   hit.End();boss.transform.SetPositionAndRotation(bossPos,bossRot);motor.enabled=false;player.transform.position=playerPos;motor.enabled=true;
   health.ResetHealth();health.ignoreDamage=true;animator.fireEvents=true;move.enabled=true;ai.enabled=true;
   hit.localSize=savedSize;foreach(var p in testPatterns)UnityEngine.Object.DestroyImmediate(p);
  }
 }
}
