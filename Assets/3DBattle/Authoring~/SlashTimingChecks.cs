using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using OneWay.Battle3D;
public static class SlashTimingChecks
{
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);}
 public static string Run()
 {
  var p=UnityEngine.Object.FindFirstObjectByType<BattlePlayerController>();
  var fx=p.GetComponent<BattleSwordSlashEffect>();
  var field=typeof(BattleSwordSlashEffect).GetField("active",BindingFlags.Instance|BindingFlags.NonPublic);
  Func<GameObject> current=()=>field.GetValue(fx) as GameObject;
  p.CancelInput(); p.enabled=false;
  int count=0;
  foreach(float speed in new[]{1f,3f})for(int i=0;i<5;i++){
   p.normalAttackSpeed=speed;
   var clip=p.animator.runtimeAnimatorController.animationClips.First(c=>c.name=="MeshyCombo0"+(i+1));
   Action<float> frame=f=>{p.animator.Play("Base Layer.MeshyCombo0"+(i+1),0,f/(clip.length*clip.frameRate));p.animator.Update(0);fx.Sample();};
   fx.PlaySwing(i+1,speed);frame(0);Check(current()==null,"Preparation effect "+i);
   frame(fx.startFrames[i]-.5f);Check(current()==null,"Early effect "+i);
   frame(fx.startFrames[i]+.1f);var effect=current();Check(effect!=null,"Missing swing "+i);
   Check(Quaternion.Angle(effect.transform.rotation,p.transform.rotation*fx.localSwingRotations[i])<.01f,"Wrong swing rotation");
   frame(fx.startFrames[i]+1);Check(current()==effect,"Duplicate effect "+i);
   frame(fx.endFrames[i]+.1f);Check(current()==null,"Recovery still emitting "+i);count++;
  }
  fx.PlaySwing(1,1);p.animator.Play("Base Layer.MeshyCombo01",0,0);p.animator.Update(0);fx.Sample();fx.Cancel();
  Check(current()==null,"Cancelled attack effect");
  return "PASS: "+count+" swings (5 at 1x and 3x), no preparation/recovery emission, one instance each, distinct authored plane and direction, cancellation.";
 }
}
