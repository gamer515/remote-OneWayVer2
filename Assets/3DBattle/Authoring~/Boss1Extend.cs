using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;
public static class Boss1Extend {
 const string F="Assets/3DBattle/Boss1/";
 public static string Run(){
  if(EditorApplication.isPlaying) throw new Exception("Stop play first");
  var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
  if(scene.path!="Assets/Scenes/3DBattleScene.unity")throw new Exception("Wrong scene");
  var prefabPath=F+"Boss1_AllAnimations.prefab";
  var mat=AssetDatabase.LoadAssetAtPath<Material>(F+"Boss1Surface.mat");
  var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(F+"Boss1Animations.controller");
  if(mat==null||controller==null)throw new Exception("Missing existing assets");
  var report=new List<string>();var added=new List<AnimationClip>();
  foreach(int number in new[]{16}){
   var dir=F+"Meshy_AI_boss_character_model_biped_"+number;
   var files=Directory.GetFiles(dir,"*.fbx").Select(p=>p.Replace('\\','/')).ToArray();
   if(files.Length==0){report.Add(number+": missing FBX; skipped");continue;}
   foreach(var path in files){
    var clips=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).ToArray();
    if(clips.Length==0)throw new Exception("No clip: "+path);
    clips=clips.Select(c=>Adapt(path,c,prefabPath,report)).ToArray();
    var probe=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
    probe.hideFlags=HideFlags.HideAndDontSave;
    try{
     foreach(var c in clips){
      var missing=AnimationUtility.GetCurveBindings(c).Where(b=>b.path!=""&&probe.transform.Find(b.path)==null).ToArray();
      if(missing.Length>0)throw new Exception("Missing bindings "+c.name+": "+string.Join(",",missing.Select(b=>b.path)));
      var parts=probe.GetComponentsInChildren<Transform>(true);c.SampleAnimation(probe,0);
      var rotations=parts.Select(t=>t.localRotation).ToArray();int moving=0;
      for(int f=1;f<=8;f++){c.SampleAnimation(probe,c.length*f/8);moving+=parts.Where((t,i)=>Quaternion.Angle(t.localRotation,rotations[i])>.1f).Count();}
      if(moving==0)throw new Exception("Static clip: "+c.name);
      report.Add(number+": "+c.name+"; duration="+c.length+"; all bindings found; movement samples="+moving);
     }
    }finally{UnityEngine.Object.DestroyImmediate(probe);}
    var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);
    var names=model.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Select(m=>m.name).Distinct().ToArray();
    var importer=(ModelImporter)AssetImporter.GetAtPath(path);
    foreach(var name in names)importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),name),mat);
    importer.SaveAndReimport();
    foreach(var c in clips){
     var machine=controller.layers[0].stateMachine;var name=c.name.Split('|').Last();
     var state=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name==name);
     if(state==null)state=machine.AddState(name);
     state.motion=c;EditorUtility.SetDirty(state);added.Add(c);
    }
   }
  }
  EditorUtility.SetDirty(controller);AssetDatabase.SaveAssetIfDirty(controller);
  var root=PrefabUtility.LoadPrefabContents(prefabPath);
  try{Append(root,added);PrefabUtility.SaveAsPrefabAsset(root,prefabPath);}
  finally{PrefabUtility.UnloadPrefabContents(root);}
  foreach(var rootObject in scene.GetRootGameObjects())foreach(var debug in rootObject.GetComponentsInChildren<MeshyAnimationDebugger>(true)){
   var animator=debug.GetComponent<Animator>();
   if(animator!=null&&animator.runtimeAnimatorController==controller){Append(debug.gameObject,added);PrefabUtility.RecordPrefabInstancePropertyModifications(debug);report.Add(debug.name+": playlist "+debug.clips.Length);}
  }
  EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
  File.WriteAllLines("Assets/3DBattle/Authoring~/Boss1ExtensionValidation.txt",report);
  return string.Join("\n",report);
 }
 static void Append(GameObject go,List<AnimationClip> added){
  var debug=go.GetComponent<MeshyAnimationDebugger>();if(debug==null)throw new Exception("Missing debugger");
  var list=debug.clips.ToList();foreach(var clip in added)if(!list.Contains(clip))list.Add(clip);
  debug.clips=list.ToArray();EditorUtility.SetDirty(debug);
 }
 static AnimationClip Adapt(string path,AnimationClip clip,string prefab,List<string> report){
  var src=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
  var dst=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(prefab));
  src.hideFlags=dst.hideFlags=HideFlags.HideAndDontSave;
  try{
   var sr=src.GetComponentInChildren<SkinnedMeshRenderer>();var dr=dst.GetComponentInChildren<SkinnedMeshRenderer>();
   var source=sr.bones.ToDictionary(t=>t.name);
   var bones=dst.GetComponentsInChildren<Transform>(true).Where(t=>dr.bones.Contains(t)).ToArray();
   foreach(var b in bones)if(!source.ContainsKey(b.name))throw new Exception("Missing bone "+b.name);
   foreach(var b in bones){
    int si=Array.IndexOf(sr.bones,source[b.name]),di=Array.IndexOf(dr.bones,b);
    var sb=sr.transform.localToWorldMatrix*sr.sharedMesh.bindposes[si].inverse;
    var db=dr.transform.localToWorldMatrix*dr.sharedMesh.bindposes[di].inverse;
    if(Vector3.Distance(sb.GetColumn(3),db.GetColumn(3))>.001f||Quaternion.Angle(sb.rotation,db.rotation)>.1f)throw new Exception("Incompatible bind pose: "+b.name);
   }
   var arm=bones[0].parent;
   var tracks=new[]{arm}.Concat(bones).ToArray();
   var curves=tracks.Select(t=>Enumerable.Range(0,10).Select(_=>new AnimationCurve()).ToArray()).ToArray();
   var previous=new Quaternion[tracks.Length];int frames=Mathf.CeilToInt(clip.length*120);
   for(int f=0;f<=frames;f++){
    float time=clip.length*f/frames;clip.SampleAnimation(src,time);
    foreach(var b in bones)b.SetPositionAndRotation(source[b.name].position,source[b.name].rotation);
    for(int i=0;i<tracks.Length;i++){
     var t=tracks[i];var p=t.localPosition;var q=t.localRotation;var s=t.localScale;
     if(f>0&&Quaternion.Dot(previous[i],q)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);previous[i]=q;
     var values=new[]{p.x,p.y,p.z,q.x,q.y,q.z,q.w,s.x,s.y,s.z};
     for(int j=0;j<10;j++)curves[i][j].AddKey(time,values[j]);
    }
   }
   var output=new AnimationClip{name=clip.name,frameRate=120};
   var properties=new[]{"m_LocalPosition.x","m_LocalPosition.y","m_LocalPosition.z","m_LocalRotation.x","m_LocalRotation.y","m_LocalRotation.z","m_LocalRotation.w","m_LocalScale.x","m_LocalScale.y","m_LocalScale.z"};
   for(int i=0;i<tracks.Length;i++)for(int j=0;j<10;j++){
    var curve=curves[i][j];for(int k=0;k<curve.length;k++){AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Linear);}
    AnimationUtility.SetEditorCurve(output,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(tracks[i],dst.transform),typeof(Transform),properties[j]),curve);
   }
   output.EnsureQuaternionContinuity();
   float maxPosition=0,maxAngle=0;
   for(int f=0;f<=20;f++){
    float time=clip.length*f/20;clip.SampleAnimation(src,time);output.SampleAnimation(dst,time);
    foreach(var b in bones){maxPosition=Mathf.Max(maxPosition,Vector3.Distance(b.position,source[b.name].position));maxAngle=Mathf.Max(maxAngle,Quaternion.Angle(b.rotation,source[b.name].rotation));}
   }
   if(maxPosition>.003f||maxAngle>1)throw new Exception("Retarget mismatch "+maxPosition+" / "+maxAngle);
   string outputPath=F+clip.name.Split('|').Last()+"_Boss1.anim";
   var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(outputPath);
   if(existing!=null){EditorUtility.CopySerialized(output,existing);UnityEngine.Object.DestroyImmediate(output);output=existing;EditorUtility.SetDirty(output);AssetDatabase.SaveAssetIfDirty(output);}else AssetDatabase.CreateAsset(output,outputPath);
   report.Add("Adapted clip: "+outputPath+"; max world bone position error="+maxPosition+" m; angle error="+maxAngle+" degrees");
   return output;
  }finally{UnityEngine.Object.DestroyImmediate(src);UnityEngine.Object.DestroyImmediate(dst);}
 }
}

