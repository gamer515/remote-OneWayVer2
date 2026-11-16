using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class TrimAttackPreviews
{
 public static object Run()
 {
  if(Application.isPlaying)throw new Exception("Stop play first");var result=new List<object>();
  foreach(var item in new[]{new {name="Skill_03",frame=33,root="Attack 05 - Skill_03",correct=true},new{name="Weapon_Combo_1",frame=44,root="Attack 06 - Weapon_Combo_1",correct=false}})
  {
   string sourcePath=AssetDatabase.FindAssets("t:Model",new[]{"Assets/3DBattle/NewPlayer/Attack"}).Select(AssetDatabase.GUIDToAssetPath).Single(p=>p.EndsWith("_"+item.name+".fbx"));
   var source=AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
   var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/3DBattle/NewPlayer/Attack/Previews/NewKnight_"+item.name+"_Preview.anim");
   var live=GameObject.Find(item.root);var clone=UnityEngine.Object.Instantiate(live);clone.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);var a=clone.GetComponentInChildren<Animator>();a.enabled=false;
   try{
    float start=item.frame/source.frameRate,duration=source.length-start;Undo.RecordObject(clip,"Trim attack preview");clip.ClearCurves();
    foreach(var b in AnimationUtility.GetCurveBindings(source)){
     var original=AnimationUtility.GetEditorCurve(source,b);var keys=original.keys.Where(k=>k.time>=start-.00001f&&k.time<=source.length+.00001f).Select(k=>{k.time=Mathf.Clamp(k.time-start,0,duration);return k;}).ToList();
     if(keys.Count==0||keys[0].time>.00001f){float slope=(original.Evaluate(start+.0001f)-original.Evaluate(start-.0001f))/.0002f;keys.Insert(0,new Keyframe(0,original.Evaluate(start),slope,slope));}
     if(keys.Last().time<duration-.00001f)keys.Add(new Keyframe(duration,original.Evaluate(source.length)));
     AnimationUtility.SetEditorCurve(clip,b,new AnimationCurve(keys.ToArray()));
    }
    float yaw=0;var skin=clone.GetComponentInChildren<SkinnedMeshRenderer>();var hips=skin.bones.First(b=>b.name=="Hips");var chest=skin.bones.First(b=>b.name=="UpperChest");
    var bind=skin.transform.localToWorldMatrix*skin.sharedMesh.bindposes[Array.IndexOf(skin.bones,chest)].inverse;
    source.SampleAnimation(a.gameObject,start);Vector3 heading=Vector3.ProjectOnPlane(chest.rotation*Quaternion.Inverse(bind.rotation)*Vector3.forward,Vector3.up).normalized;
    if(item.correct){yaw=-Vector3.SignedAngle(Vector3.forward,heading,Vector3.up);var rotation=Quaternion.AngleAxis(yaw,Vector3.up);Vector3 pivot=hips.position;
     var curves=Enumerable.Range(0,7).Select(_=>new List<Keyframe>()).ToArray();int count=Mathf.RoundToInt(duration*120);
     for(int f=0;f<=count;f++){float time=duration*f/count;source.SampleAnimation(a.gameObject,start+time);hips.SetPositionAndRotation(pivot+rotation*(hips.position-pivot),rotation*hips.rotation);var p=hips.localPosition;var q=hips.localRotation;for(int c=0;c<7;c++)curves[c].Add(new Keyframe(time,c<3?p[c]:q[c-3]));}
     for(int c=0;c<7;c++){var curve=new AnimationCurve(curves[c].ToArray());for(int k=0;k<curve.length;k++){AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Linear);}AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(hips,a.transform),typeof(Transform),c<3?"m_LocalPosition."+"xyz"[c]:"m_LocalRotation."+"xyzw"[c-3]),curve);}
    }
    clip.frameRate=30;clip.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.startTime=0;settings.stopTime=duration;settings.loopTime=true;settings.loopBlend=false;AnimationUtility.SetAnimationClipSettings(clip,settings);EditorUtility.SetDirty(clip);
    clip.SampleAnimation(a.gameObject,0);Vector3 finalHeading=Vector3.ProjectOnPlane(chest.rotation*Quaternion.Inverse(bind.rotation)*Vector3.forward,Vector3.up).normalized;
    clip.SampleAnimation(live.GetComponentInChildren<Animator>().gameObject,0);
    result.Add(new{item.name,sourceStart=item.frame,sourceEnd=Mathf.RoundToInt(source.length*30),newLastFrame=Mathf.RoundToInt(clip.length*30),length=clip.length,yawCorrection=yaw,startHeading=Vector3.SignedAngle(Vector3.forward,finalHeading,Vector3.up)});
   }finally{UnityEngine.Object.DestroyImmediate(clone);}
  }
  AssetDatabase.SaveAssets();var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);return result;
 }
}
