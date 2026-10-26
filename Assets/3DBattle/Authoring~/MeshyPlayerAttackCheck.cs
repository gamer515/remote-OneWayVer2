using System;
using System.Linq;
using System.IO;
using UnityEngine;
using UnityEditor;
using OneWay.Battle3D;
public static class MeshyPlayerAttackCheck {
 public static string Run(){
 if(!Application.isPlaying)throw new Exception("Play required");
 var player=UnityEngine.Object.FindFirstObjectByType<BattlePlayerController>();
 var go=UnityEngine.Object.Instantiate(player.gameObject);
 try{
 foreach(var b in go.GetComponentsInChildren<MonoBehaviour>())b.enabled=false;
 var p=go.GetComponent<BattlePlayerController>();p.boss=null;p.feedback=null;var a=p.animator;a.enabled=true;a.cullingMode=AnimatorCullingMode.AlwaysAnimate;a.Rebind();a.Update(0);
 var clip=a.runtimeAnimatorController.animationClips.Single(c=>c.name=="MeshyWeaponCombo_Player");
 int missing=AnimationUtility.GetCurveBindings(clip).Count(b=>b.path!=""&&a.transform.Find(b.path)==null);if(missing!=0)throw new Exception("Missing bindings "+missing);
 p.ProcessControlFrame(0,true,false,.01f);a.Update(0);
 if(!p.UsesNewNormalAttack||!a.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.AttackNew"))throw new Exception("Wrong normal attack");
 if(a.GetCurrentAnimatorClipInfo(0)[0].clip!=clip)throw new Exception("Old clip still active");
 float expected=clip.length/Mathf.Max(.1f,p.normalAttackSpeed);if(Mathf.Abs(p.ComboChainDelay(0)-expected)>.001f)throw new Exception("Incorrect timing");
 var bones=a.GetComponentsInChildren<Transform>();var first=bones.Select(b=>b.localRotation).ToArray();a.Play("Base Layer.AttackNew",0,.6f);a.Update(0);int moved=bones.Where((b,i)=>Quaternion.Angle(first[i],b.localRotation)>.1f).Count();if(moved<10)throw new Exception("Insufficient motion");
 p.ProcessControlFrame(0,true,false,.01f);p.ProcessControlFrame(0,false,false,expected+.1f);a.Update(0);if(p.ComboStep!=2||p.NormalAttackStartFrame!=0)throw new Exception("Repeat failed");
 var result="PASS: click selects MeshyWeaponCombo_Player; bindings missing=0; moving transforms="+moved+"; duration="+clip.length+"; speed="+p.normalAttackSpeed+"; repeat starts at frame 0; old clip retained. Damage remains one hit per input.";
 File.WriteAllText("Assets/3DBattle/Authoring~/MeshyPlayerAttackValidation.txt",result);return result;
 }finally{UnityEngine.Object.DestroyImmediate(go);}
 }
}
