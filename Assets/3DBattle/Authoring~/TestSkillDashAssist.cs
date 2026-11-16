using System;using System.Collections.Generic;using UnityEngine;using OneWay.Battle3D;
public static class TestSkillDashAssist
{
 static void Check(bool ok,string m){if(!ok)throw new Exception(m);}
 public static object Run(){
  Check(Application.isPlaying,"Requires play mode");var c=UnityEngine.Object.FindFirstObjectByType<NewPlayerAttackCombo>();var p=c.GetComponent<BattlePlayerController>();var motor=p.GetComponent<CharacterController>();var body=p.GetComponent<BattleArenaBody>();var camera=p.viewCamera;var boss=p.boss;var feedback=p.feedback;var pos=p.transform.position;var rot=p.transform.rotation;float range=p.attackRange,dash=c.skillDashDistance;bool pe=p.enabled,be=body.enabled;var camObject=new GameObject("Temporary assist camera");var cam=camObject.AddComponent<Camera>();cam.enabled=false;cam.fieldOfView=60;cam.aspect=1.6f;var target=new GameObject("Temporary assist boss");var hit=target.AddComponent<SphereCollider>();hit.radius=1;hit.center=Vector3.up;hit.isTrigger=true;var b=target.AddComponent<BattleBoss>();b.maxHealth=100000;b.ResetBoss();b.hitbox=hit;p.enabled=false;body.enabled=false;motor.enabled=false;p.viewCamera=cam;p.feedback=null;p.attackRange=3.5f;c.skillDashDistance=6;c.readMouseInput=false;var origin=new Vector3(1000,0,1000);var results=new List<object>();
  Action tick=()=>c.Tick(.5f/(30*c.playbackSpeed));
  Action reset=()=>{c.Cancel();p.transform.SetPositionAndRotation(origin,Quaternion.identity);cam.transform.SetPositionAndRotation(origin+new Vector3(0,1,-2),Quaternion.identity);};
  Action<Vector3,bool,bool,bool> scenario=(offset,enemy,expectedAssist,skip)=>{
   reset();p.boss=enemy?b:null;target.transform.position=origin+offset;Physics.SyncTransforms();c.RequestAttack();for(int i=0;i<10;i++)tick();Check(!c.InPreparation&&c.Frame<.01f,"Skill start mismatch");var start=p.transform.position;var heading=p.transform.forward;float planned=c.ActiveSkillDashDistance;Check(c.SkillDashAssisted==expectedAssist,"Wrong assist condition "+offset);if(skip)Check(planned==0,"In-range dash not suppressed");else if(!expectedAssist)Check(Mathf.Abs(planned-6)<.001f,"Unassisted dash reduced");if(expectedAssist){var sample=new Vector3(start.x,hit.bounds.center.y,start.z);var to=Vector3.ProjectOnPlane(hit.ClosestPoint(sample)-sample,Vector3.up);Check(Vector3.Angle(heading,to)<.03f,"Not aimed at boss");Check(Mathf.Abs(planned-(to.magnitude-motor.radius-.05f))<.001f,"Wrong surface stopping distance");}
   for(int i=0;i<20;i++)tick();var delta=Vector3.ProjectOnPlane(p.transform.position-start,Vector3.up);Check(Mathf.Abs(delta.magnitude-planned)<.01f,"Actual travel differs "+delta);if(planned>0)Check(Vector3.Angle(delta,heading)<.05f,"Dash drifted sideways");if(expectedAssist)Check(b.PlanarDistance(p.transform.position)>=motor.radius,"Overlapped boss");results.Add(new{offset=offset.ToString(),expectedAssist,skip,planned,actual=delta.magnitude});
  };
  try{
   scenario(new Vector3(0,0,3),true,false,true);
   scenario(new Vector3(-3,0,0),true,false,true);
   scenario(new Vector3(.5f,0,5),true,true,false);
   scenario(new Vector3(3,0,4),true,false,false);
   scenario(new Vector3(.5f,0,10),true,false,false);
   scenario(new Vector3(0,0,-5),true,false,false);
   scenario(new Vector3(0,50,5),true,false,false);
   scenario(Vector3.zero,false,false,false);
   // The middle Skill must plan from its current position, rather than reuse the first dash.
   reset();p.boss=null;c.RequestAttack();bool reached=false;for(int i=0;i<2000;i++){if(c.SequenceIndex==4&&c.InPreparation){reached=true;break;}if(!c.InPreparation)c.RequestAttack();tick();}Check(reached,"Middle Skill preparation missing");for(int i=0;i<10;i++){cam.transform.SetPositionAndRotation(p.transform.position+new Vector3(0,1,-2),Quaternion.identity);target.transform.position=p.transform.position+new Vector3(.5f,0,5);p.boss=b;Physics.SyncTransforms();tick();}Check(c.SequenceIndex==4&&!c.InPreparation&&c.SkillDashAssisted,"Middle Skill did not replan");
   c.Cancel();reset();p.boss=b;target.transform.position=origin+new Vector3(0,0,3);Physics.SyncTransforms();c.RequestAttack();int old=c.DamageApplications;for(int i=0;i<42;i++)tick();Check(c.DamageApplications==old+1,"Suppressed dash lost attack damage");
   return new{passed=true,cases=results,middleSkillReplans=true,stationarySkillStillDamages=true};
  }finally{c.Cancel();p.viewCamera=camera;p.boss=boss;p.feedback=feedback;p.attackRange=range;c.skillDashDistance=dash;p.transform.SetPositionAndRotation(pos,rot);motor.enabled=true;body.enabled=be;p.enabled=pe;c.readMouseInput=true;UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(camObject);}
 }
}
