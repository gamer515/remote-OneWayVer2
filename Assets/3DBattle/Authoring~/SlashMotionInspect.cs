using System;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;
public static class SlashMotionInspect
{
 public static string Apply()
 {
  var p=UnityEngine.Object.FindFirstObjectByType<BattlePlayerController>();
  var fx=p.GetComponent<BattleSwordSlashEffect>();
  if(Application.isPlaying)throw new Exception("Edit mode required");
  var scene=EditorSceneManager.NewPreviewScene();
  var clone=UnityEngine.Object.Instantiate(p.animator.gameObject); clone.SetActive(false);
  UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(clone,scene);
  clone.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
  var sword=clone.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Sword");
  var bounds=sword.GetComponent<MeshFilter>().sharedMesh.bounds;
  var tip=new Vector3(bounds.center.x,bounds.center.y,bounds.max.z);
  Undo.RecordObject(fx,"Align five slash motions");
  fx.sword=p.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Sword");
  fx.startFrames=new[]{52,20,32,16,28}; fx.endFrames=new[]{60,28,42,26,38};
  fx.localSwingRotations=new Quaternion[5];
  var report=new StringBuilder();
  try{for(int n=0;n<5;n++){
   var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/3DBattle/MeshyPlayer/Converted/MeshyCombo0"+(n+1)+".anim");
   clip.SampleAnimation(clone,fx.startFrames[n]/clip.frameRate);
   var first=sword.TransformPoint(tip); var hand=sword.position; var radial=(first-hand).normalized;
   clip.SampleAnimation(clone,(fx.startFrames[n]+3)/clip.frameRate);
   var velocity=sword.TransformPoint(tip)-first;
   var normal=Vector3.Cross(radial,velocity).normalized;
   if(normal.sqrMagnitude<.5f)throw new Exception("Degenerate swing "+n);
   fx.localSwingRotations[n]=Quaternion.LookRotation(radial,normal);
   report.AppendLine((n+1)+": frames "+fx.startFrames[n]+"-"+fx.endFrames[n]+" plane="+normal.ToString("F2")+" motion="+velocity.normalized.ToString("F2"));
  }}finally{UnityEngine.Object.DestroyImmediate(clone);EditorSceneManager.ClosePreviewScene(scene);}
  EditorUtility.SetDirty(fx); EditorSceneManager.MarkSceneDirty(p.gameObject.scene);EditorSceneManager.SaveScene(p.gameObject.scene);
  return report.ToString();
 }
 public static string Run()
 {
  var p=UnityEngine.Object.FindFirstObjectByType<BattlePlayerController>();
  var scene=EditorSceneManager.NewPreviewScene();
  var clone=UnityEngine.Object.Instantiate(p.animator.gameObject); clone.SetActive(false);
  UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(clone,scene);
  clone.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
  var sword=clone.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Sword");
  var bounds=sword.GetComponent<MeshFilter>().sharedMesh.bounds;
  var tip=new Vector3(bounds.center.x,bounds.center.y,bounds.max.z);
  var output=new StringBuilder();
  try{for(int n=1;n<=5;n++){
   var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/3DBattle/MeshyPlayer/Converted/MeshyCombo0"+n+".anim");
   output.AppendLine("Combo "+n+" fps="+clip.frameRate+" frames="+clip.length*clip.frameRate);
   Vector3 prev=Vector3.zero;
   for(int f=0;f<=Mathf.RoundToInt(clip.length*clip.frameRate);f++){
    clip.SampleAnimation(clone,f/clip.frameRate);var point=sword.TransformPoint(tip);
    float speed=f==0?0:Vector3.Distance(prev,point)*clip.frameRate;
    if(f%4==0)output.AppendLine(f+" tip="+point.ToString("F2")+" speed="+speed.ToString("F1")); prev=point;
   }
  }}finally{UnityEngine.Object.DestroyImmediate(clone);EditorSceneManager.ClosePreviewScene(scene);}
  System.IO.File.WriteAllText("Assets/3DBattle/Authoring~/SlashMotionSamples.txt",output.ToString());
  return output.ToString();
 }
}

