using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using OneWay.Battle3D;
public static class InspectB2MeleeReach
{
 public static object Run()
 {
  var ai=UnityEngine.Object.FindFirstObjectByType<B2CombatController>();ai.enabled=false;ai.movement.enabled=false;
  var boss=ai.GetComponent<BattleBoss>();var a=boss.animator;var hit=boss.GetComponent<BossWeaponHit>();var player=ai.battle.player;
  var motor=player.GetComponent<CharacterController>();var bp=boss.transform.position;var br=boss.transform.rotation;var pp=player.transform.position;
  a.fireEvents=false;a.speed=1;var result=new List<object>();
  try
  {
   boss.transform.SetPositionAndRotation(new Vector3(1000,0,1000),Quaternion.identity);motor.enabled=false;player.transform.position=boss.transform.position+Vector3.forward*5;motor.enabled=true;
   foreach(var p in ai.attacks)
   {
    var hits=new List<object>();
    for(int i=0;i<=Mathf.RoundToInt(p.clip.length*60);i++)
    {
     a.Play(p.animationState,0,i/60f/p.clip.length);a.Update(0);Physics.SyncTransforms();hit.GetWorldBox(out var center,out var rot,out var half);
     var touch=Physics.OverlapBox(center,half,rot).Any(c=>c==motor);
     float downward=touch?0:float.PositiveInfinity;
     foreach(var contact in Physics.BoxCastAll(center,half,Vector3.down,rot,8))if(contact.collider==motor)downward=Mathf.Min(downward,contact.distance);
     if(!float.IsInfinity(downward))hits.Add(new{frame=i/2f,down=downward});
    }
    result.Add(new{p.name,frames=p.clip.length*30,events=AnimationUtility.GetAnimationEvents(p.clip).Select(e=>e.time*30).ToArray(),contacts=hits});
   }
   return result;
  }
  finally{a.fireEvents=true;boss.transform.SetPositionAndRotation(bp,br);motor.enabled=false;player.transform.position=pp;motor.enabled=true;ai.movement.enabled=true;ai.enabled=true;}
 }
}
