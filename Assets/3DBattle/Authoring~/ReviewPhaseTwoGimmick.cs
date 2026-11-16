using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using OneWay.Battle3D;
public static class ReviewPhaseTwoGimmick
{
 static void Call(object o,string n)=>o.GetType().GetMethod(n,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(o,null);
 static void Set(object o,string n,object v)=>o.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(o,v);
 static void Require(bool b,string m){if(!b)throw new Exception(m);}
 public static object Run()
 {
  EditorApplication.isPaused=true;
  var battle=UnityEngine.Object.FindFirstObjectByType<BattleDebugBootstrap>().battle;battle.SelectStage(1);
  var boss=battle.CurrentStage.boss;var phase=boss.GetComponent<BossPhaseTwo>();var g=boss.GetComponent<BossPhaseTwoGimmick>();var actions=boss.GetComponent<BossActionController>();var p=battle.player;
  boss.GetComponent<BossApproachController>().automaticApproach=false;
  var motor=p.GetComponent<CharacterController>();motor.enabled=false;p.transform.position=boss.transform.position+Vector3.forward*3.8f;motor.enabled=true;
  phase.ForcePhaseTwo();phase.Tick(0);boss.animator.Play(phase.transitionState,0,1);boss.animator.Update(0);actions.Tick(0);phase.Tick(0);phase.Tick(2.1f);
  Require(g.Locked,"No lock");Set(g,"blendElapsed",1f);Call(g,"LateUpdate");
  var cam=battle.followCamera.GetComponent<Camera>();var rt=new RenderTexture(1000,700,24);var old=cam.targetTexture;var active=RenderTexture.active;
  try{cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;var tex=new Texture2D(1000,700,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1000,700),0,0);tex.Apply();File.WriteAllBytes("Assets/3DBattle/Authoring~/PhaseTwoGimmickPreview.png",tex.EncodeToPNG());UnityEngine.Object.Destroy(tex);}
  finally{cam.targetTexture=old;RenderTexture.active=active;rt.Release();UnityEngine.Object.Destroy(rt);}
  Vector3 p0=p.transform.position,b0=boss.transform.position;
  Set(g,"pushRemaining",g.pushDistance);for(int i=0;i<40;i++)Call(g,"LateUpdate");
  float pd=Vector3.Distance(p0,p.transform.position),bd=Vector3.Distance(b0,boss.transform.position);
  Require(pd>0 && Mathf.Abs(pd-bd)<.03f,"Push mismatch "+pd+"/"+bd);
  var body=p.GetComponent<BattleArenaBody>();motor.enabled=false;
  Vector3 wall=body.Constrain(p.transform.position+Vector3.forward*10000);p.transform.position=wall;
  boss.transform.position=p.transform.position-Vector3.forward*3.8f;Set(g,"facing",Vector3.back);motor.enabled=true;
  p0=p.transform.position;b0=boss.transform.position;Set(g,"pushRemaining",g.pushDistance);for(int i=0;i<40;i++)Call(g,"LateUpdate");
  float wallP=Vector3.Distance(p0,p.transform.position),wallB=Vector3.Distance(b0,boss.transform.position);
  Require(wallP<.05f && wallB<.05f,"Wall push leaked "+wallP+"/"+wallB);
  Set(g,"hitElapsed",.1f);g.PressParry(g.rotationStarts[1]+.01f);
  Require((float)g.GetType().GetField("hitElapsed",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(g)<0,"Hit cancel failed");
  var hp=p.GetComponent<BattlePlayerHealth>();hp.TakeGimmickDamage(1000);Call(g,"Update");g.ResetGimmick();
  Require(!g.Locked && p.enabled && battle.followCamera.enabled,"Death cleanup failed");
  return new{pushPlayer=pd,pushBoss=bd,wallPlayer=wallP,wallBoss=wallB,hitCancel=true,deathCleanup=true,preview="Assets/3DBattle/Authoring~/PhaseTwoGimmickPreview.png"};
 }
}
