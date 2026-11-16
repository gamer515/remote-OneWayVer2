using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using OneWay.Battle3D;

public static class TestB2AttackSteps
{
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);}
 static GameObject Clone(B2AttackStepMovement source,Vector3 position,out B2AttackStepMovement movement)
 {
  var clone=UnityEngine.Object.Instantiate(source.gameObject,position,Quaternion.identity);clone.name="Temporary B2 step test";
  movement=clone.GetComponent<B2AttackStepMovement>();
  foreach(var b in clone.GetComponentsInChildren<MonoBehaviour>(true))if(b!=movement)b.enabled=false;
  foreach(var c in clone.GetComponentsInChildren<Collider>(true))c.enabled=false;
  movement.animator.fireEvents=false;movement.animator.applyRootMotion=false;movement.animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;movement.arenaBody=null;
  movement.animator.Rebind();movement.animator.Update(0);movement.ResetTracking();return clone;
 }
 public static object Run()
 {
  Check(Application.isPlaying,"Play mode required");
  var source=UnityEngine.Object.FindObjectsByType<B2AttackStepMovement>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(m=>m.gameObject.activeInHierarchy);
  var results=new List<object>();
  foreach(int fps in new[]{30,60,120})
  {
   var clone=Clone(source,Vector3.zero,out var movement);var animator=movement.animator;
   try
   {
    animator.Play("Base Layer.PowerSlash",0,0);animator.Update(0);movement.Advance();
    float expected=0,cycleStart=0,maxWaitDelta=0,maxNegativeDelta=0;string previous="PowerSlash";var completed=new List<object>();
    for(int i=0;i<fps*24;i++)
    {
     Vector3 before=clone.transform.position;animator.Update(1f/fps);movement.Advance();var state=animator.GetCurrentAnimatorStateInfo(0);
     float delta=clone.transform.position.z-before.z;maxNegativeDelta=Mathf.Max(maxNegativeDelta,-delta);
     string name=movement.attacks.SelectMany(a=>new[]{a.stateName,"WaitAfter"+a.stateName}).FirstOrDefault(n=>state.IsName(n));
     if(name!=previous)
     {
      if(name!=null&&name.StartsWith("WaitAfter"))
      {
       var attack=movement.attacks.Single(a=>name=="WaitAfter"+a.stateName);
       expected+=attack.forwardDistance.Evaluate(attack.clip.length)*Mathf.Abs(animator.transform.lossyScale.z);
       float error=Mathf.Abs(clone.transform.position.z-expected);
       Check(error<.006f,"Travel mismatch at "+name+" "+error);
       completed.Add(new{attack=attack.stateName,distance=clone.transform.position.z-cycleStart});cycleStart=clone.transform.position.z;
      }
      previous=name;
     }
     else if(name!=null&&name.StartsWith("WaitAfter")&&!animator.IsInTransition(0))maxWaitDelta=Mathf.Max(maxWaitDelta,Mathf.Abs(delta));
    }
    Check(completed.Count>=8,"Did not complete two attack cycles");Check(maxNegativeDelta<.0001f,"Boss moved backwards");Check(maxWaitDelta<.0001f,"Boss moved during one-second pause");
    var beforeDeath=clone.transform.position;clone.GetComponent<BattleBoss>().TakeDamage(1000000);animator.Update(.2f);movement.Advance();Check(Vector3.Distance(beforeDeath,clone.transform.position)<.0001f,"Dead boss still moves");
    results.Add(new{fps,maxWaitDelta,maxNegativeDelta,completed=completed.Take(4).ToArray(),deathStops=true});
   }
   finally{UnityEngine.Object.DestroyImmediate(clone);}
  }
  var contactClone=Clone(source,new Vector3(1000,0,1000),out var contactMove);GameObject obstacle=null;
  try
  {
   contactMove.bodyCollider.enabled=true;obstacle=new GameObject("Temporary player collision capsule");obstacle.transform.position=contactClone.transform.position+Vector3.forward*5;
   var capsule=obstacle.AddComponent<CapsuleCollider>();capsule.center=Vector3.up;capsule.height=2;capsule.radius=.5f;Physics.SyncTransforms();
   var animator=contactMove.animator;animator.Play("Base Layer.WeaponCombo",0,0);animator.Update(0);contactMove.Advance();
   for(int i=0;i<100;i++){animator.Update(1/60f);contactMove.Advance();}
   float travelled=contactClone.transform.position.z-1000;
   Check(travelled>.05f&&travelled<4,"Boss failed to stop at obstacle: "+travelled);
   Physics.SyncTransforms();Check(!Physics.ComputePenetration(contactMove.bodyCollider,contactClone.transform.position,contactClone.transform.rotation,capsule,capsule.transform.position,capsule.transform.rotation,out var direction,out var depth)||depth<.001f,"Boss overlapped player capsule");
   UnityEngine.Object.DestroyImmediate(obstacle);obstacle=null;var held=contactClone.transform.position;contactMove.Advance();Check(Vector3.Distance(held,contactClone.transform.position)<.0001f,"Blocked travel was paid back");
   results.Add(new{collisionStops=true,travelled,noStoredMovement=true});
  }
  finally{if(obstacle)UnityEngine.Object.DestroyImmediate(obstacle);UnityEngine.Object.DestroyImmediate(contactClone);}
  var wallClone=Clone(source,new Vector3(0,0,25),out var wallMove);
  try
  {
   wallMove.arenaBody=wallClone.GetComponent<BattleArenaBody>();wallMove.arenaBody.battle=source.arenaBody.battle;
   var animator=wallMove.animator;animator.Play("Base Layer.WeaponCombo",0,0);animator.Update(0);wallMove.Advance();
   for(int i=0;i<180;i++){animator.Update(1/60f);wallMove.Advance();}
   Vector3 final=wallClone.transform.position;Check(final.z<26&&final.z>=25,"Arena bound failed: "+final.z);
   Check(Vector3.Distance(final,wallMove.arenaBody.Constrain(final))<.001f,"Boss outside boundary");results.Add(new{arenaStops=true,wallZ=final.z});
  }
  finally{UnityEngine.Object.DestroyImmediate(wallClone);}
  return results;
 }
}
