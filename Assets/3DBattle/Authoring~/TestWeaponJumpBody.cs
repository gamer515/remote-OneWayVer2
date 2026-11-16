using System;
using System.Collections.Generic;
using UnityEngine;
using OneWay.Battle3D;
public static class TestWeaponJumpBody
{
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 public static object Run(){
  Check(Application.isPlaying,"Play mode required");
  var c=UnityEngine.Object.FindFirstObjectByType<NewPlayerAttackCombo>();var p=c.GetComponent<BattlePlayerController>();var motor=p.GetComponent<CharacterController>();var arena=p.GetComponent<BattleArenaBody>();
  var oldBoss=p.boss;var feedback=p.feedback;var pos=p.transform.position;var rot=p.transform.rotation;bool enabled=p.enabled, arenaEnabled=arena.enabled,input=c.readMouseInput;float speed=c.playbackSpeed;
  var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(1000,-.5f,1000);floor.transform.localScale=new Vector3(200,1,200);
  var target=new GameObject("Temporary jump collision boss");var first=target.AddComponent<CapsuleCollider>();first.radius=1.715f;first.height=7.6f;first.center=new Vector3(0,2.97f,0);
  var child=new GameObject("Body Barrier");child.transform.SetParent(target.transform,false);var second=child.AddComponent<CapsuleCollider>();second.radius=1.65f;second.height=4;second.center=new Vector3(.69f,2.67f,.11f);
  var boss=target.AddComponent<BattleBoss>();boss.maxHealth=100000;boss.ResetBoss();boss.hitbox=first;
  p.enabled=false;arena.enabled=false;c.readMouseInput=false;p.feedback=null;p.boss=boss;var results=new List<object>();
  Action tick=()=>c.Tick(.5f/(30*c.playbackSpeed));
  Action setup=()=>{c.Cancel();motor.enabled=false;p.transform.SetPositionAndRotation(new Vector3(1000,.01f,1000),Quaternion.identity);motor.enabled=true;target.transform.position=new Vector3(1050,0,1050);Physics.SyncTransforms();c.RequestAttack();for(int i=0;i<4000;i++){if(c.SequenceIndex==3&&!c.InPreparation&&c.Frame<.01f)return;if(!c.InPreparation)c.RequestAttack();tick();}throw new Exception("Weapon not reached");};
  try{
   foreach(bool moving in new[]{false,true}){
    c.playbackSpeed=moving?4:1;setup();var start=p.transform.position;target.transform.position=new Vector3(start.x,0,start.z)+p.transform.forward*2.3f;Physics.SyncTransforms();float peak=0,minGap=100;bool moved=false;int damage=c.DamageApplications;
    while(c.IsActive){if(moving&&!moved&&c.Frame>=18){target.transform.position=new Vector3(p.transform.position.x,0,p.transform.position.z);Physics.SyncTransforms();moved=true;}tick();peak=Mathf.Max(peak,p.transform.position.y);foreach(var hit in new[]{first,second}){var delta=Vector3.ProjectOnPlane(p.transform.position-hit.bounds.center,Vector3.up);float gap=delta.magnitude-hit.radius;minGap=Mathf.Min(minGap,gap);Check(gap>=motor.radius-.01f,"Boss overlap frame "+c.Frame+" gap "+gap);}}
    Check(peak>2,"Actual jump lost");Check(Mathf.Abs(p.transform.position.y)<.12f,"Landed on boss: "+p.transform.position.y);Check(c.DamageApplications>damage,"Weapon damage lost");results.Add(new{moving,speed=c.playbackSpeed,peak,minGap,landing=p.transform.position.y});
   }
   setup();while(c.Frame<18)tick();target.transform.position=new Vector3(p.transform.position.x,0,p.transform.position.z);Physics.SyncTransforms();c.Cancel();Check(Mathf.Abs(p.transform.position.y)<.12f,"Cancel landed on boss");
   return new{passed=true,cases=results,cancelReturnsToGround=true};
  }finally{c.Cancel();p.boss=oldBoss;p.feedback=feedback;c.playbackSpeed=speed;motor.enabled=false;p.transform.SetPositionAndRotation(pos,rot);motor.enabled=true;p.enabled=enabled;arena.enabled=arenaEnabled;c.readMouseInput=input;UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(floor);}
 }
}
