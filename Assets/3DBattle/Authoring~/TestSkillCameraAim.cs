using System;using UnityEngine;using OneWay.Battle3D;
public static class TestSkillCameraAim
{
 static void Check(bool b,string message){if(!b)throw new Exception(message);}
 public static object Run(){
  Check(Application.isPlaying,"Requires Play mode");var c=UnityEngine.Object.FindFirstObjectByType<NewPlayerAttackCombo>();var p=c.GetComponent<BattlePlayerController>();var body=p.GetComponent<BattleArenaBody>();var motor=p.GetComponent<CharacterController>();var oldCamera=p.viewCamera;var oldBoss=p.boss;var pos=p.transform.position;var rot=p.transform.rotation;bool enabled=p.enabled,bodyEnabled=body.enabled;var cameraObject=new GameObject("Temporary aim test camera");var cam=cameraObject.AddComponent<Camera>();cam.enabled=false;p.enabled=false;body.enabled=false;motor.enabled=false;p.boss=null;p.viewCamera=cam;c.readMouseInput=false;
  Action<float> aim=yaw=>cam.transform.rotation=Quaternion.Euler(35,yaw,0);
  Action tick=()=>c.Tick(.5f/(30f*c.playbackSpeed));
  Action<float> facing=yaw=>Check(Vector3.Angle(p.transform.forward,Quaternion.Euler(0,yaw,0)*Vector3.forward)<.01f,"Wrong heading; expected "+yaw+" at step "+c.SequenceIndex+", frame "+c.Frame);
  try{
   aim(0);c.RequestAttack();aim(90);for(int i=0;i<10;i++)tick();Check(!c.InPreparation&&c.SequenceIndex==0,"First Skill did not start");facing(90);
   var dashStart=p.transform.position;aim(180);for(int i=0;i<20;i++)tick();facing(90);var dash=Vector3.ProjectOnPlane(p.transform.position-dashStart,Vector3.up);Check(Mathf.Abs(Vector3.Dot(dash,Vector3.right)-c.skillDashDistance)<.02f,"Dash did not use new aim: "+dash.ToString());
   c.RequestAttack();bool middle=false;for(int i=0;i<2000;i++){if(!c.InPreparation)c.RequestAttack();if(c.SequenceIndex==4&&c.InPreparation)aim(270);tick();if(c.SequenceIndex==4&&!c.InPreparation){middle=true;break;}facing(90);}Check(middle,"Middle Skill missing");facing(270);
   dashStart=p.transform.position;aim(45);for(int i=0;i<20;i++)tick();facing(270);dash=Vector3.ProjectOnPlane(p.transform.position-dashStart,Vector3.up);Check(Mathf.Abs(Vector3.Dot(dash,Vector3.left)-c.skillDashDistance)<.02f,"Middle dash heading wrong: "+dash.ToString());
   bool restarted=false;c.RequestAttack();for(int i=0;i<2000;i++){if(!c.InPreparation)c.RequestAttack();tick();if(c.SequenceIndex==0&&!c.InPreparation){restarted=true;break;}facing(270);}Check(restarted,"Repeat combo missing");facing(45);Check(Mathf.Abs(p.transform.forward.y)<.0001f,"Camera pitch tilted player");
   return new{passed=true,firstSkillYaw=90,middleSkillYaw=270,repeatedFirstSkillYaw=45,dashDirectionUpdated=true,nonSkillAttacksKeepHeading=true,cameraPitchIgnored=true};
  }finally{c.Cancel();p.viewCamera=oldCamera;p.boss=oldBoss;p.transform.SetPositionAndRotation(pos,rot);motor.enabled=true;body.enabled=bodyEnabled;p.enabled=enabled;c.readMouseInput=true;UnityEngine.Object.DestroyImmediate(cameraObject);}
 }
}

