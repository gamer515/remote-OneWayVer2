using System;using System.Collections.Generic;using UnityEngine;using OneWay.Battle3D;
public static class TestAttack01Dash {
 static void Check(bool ok,string m){if(!ok)throw new Exception(m);}
 public static object Run(){
  var c=UnityEngine.Object.FindFirstObjectByType<NewPlayerAttackCombo>();var p=c.GetComponent<BattlePlayerController>();var motor=p.GetComponent<CharacterController>();var body=p.GetComponent<BattleArenaBody>();var camera=p.viewCamera;var boss=p.boss;var feedback=p.feedback;var pos=p.transform.position;var rot=p.transform.rotation;float range=p.attackRange,retreat=c.attackRetreatDistance;bool pe=p.enabled,be=body.enabled,me=motor.enabled,input=c.readMouseInput;
  var camObject=new GameObject("Test dash camera");var cam=camObject.AddComponent<Camera>();cam.enabled=false;cam.fieldOfView=60;cam.aspect=1.6f;var target=new GameObject("Test dash boss");var hit=target.AddComponent<SphereCollider>();hit.radius=1;hit.center=Vector3.up;hit.isTrigger=true;var enemy=target.AddComponent<BattleBoss>();enemy.maxHealth=100000;enemy.ResetBoss();enemy.hitbox=hit;
  p.enabled=false;body.enabled=false;motor.enabled=false;p.viewCamera=cam;p.feedback=null;p.attackRange=3.5f;c.attackRetreatDistance=3;c.readMouseInput=false;var results=new List<object>();
  Action tick=()=>c.Tick(.5f/(30*c.playbackSpeed));
  try{foreach(int scenario in new[]{0,1,2,3,4}){
   c.Cancel();p.boss=null;p.transform.SetPositionAndRotation(new Vector3(1000,0,1000),Quaternion.identity);cam.transform.rotation=Quaternion.identity;c.RequestAttack();
   for(int i=0;i<4000;i++){if(c.SequenceIndex==6&&!c.InPreparation&&c.Frame<.01f)break;if(!c.InPreparation)c.RequestAttack();tick();}
   Check(c.AttackIndex==0&&!c.InPreparation,"Attack01 missing");var start=p.transform.position;for(int i=0;i<26;i++)tick();var back=p.transform.position;Check(Mathf.Abs(Vector3.ProjectOnPlane(back-start,Vector3.up).magnitude-3)<.01f,"Retreat changed");for(int i=0;i<27;i++)tick();Check(c.Frame==26.5f,"Timing mismatch");Check(Vector3.ProjectOnPlane(back-p.transform.position,Vector3.up).magnitude<.001f,"Hold drift");
   cam.transform.SetPositionAndRotation(p.transform.position+new Vector3(-2,1,0),Quaternion.Euler(0,90,0));
   var offset=scenario==1?new Vector3(3,0,0):scenario==2?new Vector3(5,0,-.5f):scenario==3?new Vector3(4,0,-3):new Vector3(10,0,-.5f);
   target.transform.position=p.transform.position+offset;p.boss=scenario==0?null:enemy;Physics.SyncTransforms();tick();
   Check(c.Frame==27,"Dash start boundary");var dashStart=p.transform.position;float planned=c.ActiveAttackDashDistance;var heading=p.transform.forward;
   if(scenario==1)Check(planned==0,"Close boss should suppress");else if(scenario==2)Check(c.AttackDashAssisted&&planned<6,"Assist missing");else Check(Mathf.Abs(planned-6)<.01f&&!c.AttackDashAssisted,"Double distance failed");
   if(scenario!=2)Check(Vector3.Angle(heading,Vector3.right)<.05f,"Camera reaim missing");
   // Changing camera mid-dash must not bend the trajectory.
   cam.transform.rotation=Quaternion.Euler(0,180,0);for(int i=0;i<6;i++)tick();var travel=Vector3.ProjectOnPlane(p.transform.position-dashStart,Vector3.up);
   Check(Mathf.Abs(travel.magnitude-planned)<.015f,"Dash distance wrong");if(planned>0)Check(Vector3.Angle(travel,heading)<.05f,"Dash heading changed");
   if(scenario==2){Check(enemy.PlanarDistance(p.transform.position)>=motor.radius-.01f,"Boss overshot");Check(Vector3.Angle(heading,offset)<.1f,"Assist direction wrong");}
   results.Add(new{scenario,planned,actual=travel.magnitude,assisted=c.AttackDashAssisted});
  }return new{passed=true,cases=results};
  }finally{c.Cancel();p.boss=boss;p.viewCamera=camera;p.feedback=feedback;p.attackRange=range;c.attackRetreatDistance=retreat;p.transform.SetPositionAndRotation(pos,rot);motor.enabled=me;p.enabled=pe;body.enabled=be;c.readMouseInput=input;UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(camObject);}
 }
}
