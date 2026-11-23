using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static class UpdateUserFinisherRightRecovery
{
 const string Folder="Assets/3DBattle/3DBattleDebug/Finisher";
 const string TargetPath=Folder+"/DebugFinisher_RetreatRush_UserEdit.anim";
 const string Manifest="Assets/3DBattle/Authoring~/UserFinisherRightRecoveryPlan.json";
 const string Report="Assets/3DBattle/Authoring~/UserFinisherRightRecoveryVerification.json";
 const string Preview="Assets/3DBattle/Authoring~/UserFinisherRightRecoveryReview.png";
 const int Start=43, End=104;
 const float Rate=30;
 static readonly string[] Bones={"RightShoulder","RightUpperArm","RightLowerArm","RightHand"};
 static string Fingerprint(AnimationClip c) { using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(EditorJsonUtility.ToJson(c)))); }
 static Dictionary<EditorCurveBinding,AnimationCurve> Curves(AnimationClip c)=>AnimationUtility.GetCurveBindings(c).ToDictionary(b=>b,b=>AnimationUtility.GetEditorCurve(c,b));
 static bool Affected(EditorCurveBinding b)=>Bones.Contains(b.path.Split('/').Last())&&b.propertyName.StartsWith("m_LocalRotation.");
 static Quaternion Q(Dictionary<EditorCurveBinding,AnimationCurve> c,string path,float frame)
 {
  var q=new Quaternion();for(int i=0;i<4;i++)q[i]=c.Single(x=>x.Key.path==path&&x.Key.propertyName=="m_LocalRotation."+"xyzw"[i]).Value.Evaluate(frame/Rate);return q.normalized;
 }
 static double Angle(Quaternion a,Quaternion b)
 {
  double dot=0,aa=0,bb=0;for(int i=0;i<4;i++){dot+=(double)a[i]*b[i];aa+=(double)a[i]*a[i];bb+=(double)b[i]*b[i];}
  return 2*Math.Acos(Math.Min(1,Math.Abs(dot/Math.Sqrt(aa*bb))))*180/Math.PI;
 }
 static AnimationClip Build(AnimationClip source)
 {
  if(Math.Abs(source.frameRate-Rate)>.0001||Math.Abs(source.length*Rate-End)>.0001)throw new Exception("Unexpected clip timing.");
  var before=Curves(source);var bindings=before.Keys.Where(Affected).ToArray();
  if(bindings.Length!=16)throw new Exception("Expected four right-arm rotation tracks.");
  var replacement=new List<AnimationCurve>();
  foreach(var b in bindings)
  {
   int channel="xyzw".IndexOf(b.propertyName.Last());var src=before[b];
   var old=src.keys;var keys=old.Where(k=>k.time<=Start/Rate+.000001f).ToList();
   if(keys.Count==0||Math.Abs(keys.Last().time*Rate-Start)>.0001)throw new Exception("Frame 43 key missing.");
   var start=Q(before,b.path,Start);var end=Q(before,b.path,End);
   for(int frame=Start+1;frame<=End;frame++)
   {
    float t=(frame-Start)/(float)(End-Start);float eased=t*t*(3-2*t);
    var q=Quaternion.Slerp(start,end,eased);
    if(Quaternion.Dot(q,start)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);
    keys.Add(new Keyframe(frame/Rate,q[channel]));
   }
   var curve=new AnimationCurve(keys.ToArray()){preWrapMode=src.preWrapMode,postWrapMode=src.postWrapMode};
   int boundary=Array.FindIndex(curve.keys,k=>Math.Abs(k.time*Rate-Start)<.0001);
   // Only the outgoing side of frame 43 changes. Earlier poses and interpolation stay intact.
   AnimationUtility.SetKeyBroken(curve,boundary,true);
   AnimationUtility.SetKeyRightTangentMode(curve,boundary,AnimationUtility.TangentMode.Linear);
   for(int i=boundary+1;i<curve.length;i++)
   {
    AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear);
    AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear);
   }
   replacement.Add(curve);
  }
  var candidate=Object.Instantiate(source);candidate.name="DebugFinisher_RetreatRush_UserEdit";
  AnimationUtility.SetEditorCurves(candidate,bindings,replacement.ToArray());
  try { Verify(source,candidate);return candidate; } catch {Object.DestroyImmediate(candidate);throw;}
 }
 static object Verify(AnimationClip source,AnimationClip result)
 {
  var a=Curves(source);var b=Curves(result);
  if(!a.Keys.ToHashSet().SetEquals(b.Keys))throw new Exception("Bindings changed.");
  float preservedError=0;int untouched=0;
  foreach(var binding in a.Keys)
  {
   if(!Affected(binding))
   {
    if(!a[binding].keys.SequenceEqual(b[binding].keys)||a[binding].preWrapMode!=b[binding].preWrapMode||a[binding].postWrapMode!=b[binding].postWrapMode)throw new Exception("Unrelated track changed: "+binding.path+"/"+binding.propertyName);
    untouched++;continue;
   }
   for(int i=0;i<=Start*4;i++)preservedError=Mathf.Max(preservedError,Mathf.Abs(a[binding].Evaluate(i/(4*Rate))-b[binding].Evaluate(i/(4*Rate))));
   if(b[binding].keys.Any(k=>Math.Abs(k.time*Rate-Mathf.Round(k.time*Rate))>.0001))throw new Exception("Fractional key introduced.");
  }
  if(preservedError>.00001f)throw new Exception("Frames 0-43 changed: "+preservedError);
  if(Math.Abs(source.length-result.length)>.00001||source.frameRate!=result.frameRate)throw new Exception("Clip timing changed.");
  var measures=a.Keys.Where(Affected).Select(x=>x.path).Distinct().Select(path=>{
   double max=0;for(int f=Start;f<End;f++)max=Math.Max(max,Angle(Q(b,path,f),Q(b,path,f+1)));
   double endError=Angle(Q(a,path,End),Q(b,path,End));
   if(endError>.001||max>5)throw new Exception("Recovery is not smooth or end pose changed: "+path);
   return new{bone=path.Split('/').Last(),old43to44Degrees=Angle(Q(a,path,43),Q(a,path,44)),new43to44Degrees=Angle(Q(b,path,43),Q(b,path,44)),maxDegreesPerFrame=max,finalPoseErrorDegrees=endError};
  }).ToArray();
  return new{passed=true,changedBones=Bones,modifiedRange=new[]{44,104},preservedThroughFrame=43,protectedCurveCount=untouched,protectedFrameMaxValueError=preservedError,leftArmAndHandExactlyPreserved=true,allPositionScaleSwordAndBodyCurvesExactlyPreserved=true,measures};
 }
 static void Render(AnimationClip before,AnimationClip after)
 {
  var player=Object.FindObjectsByType<OneWay.Battle3D.NewPlayerAttackCombo>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(c=>c.name=="New Player - Attack Combo");
  var live=player.GetComponent<OneWay.Battle3D.BattlePlayerController>().animator;
  GameObject root=null,camGo=null,floor=null;Material mat=null;Texture2D sheet=null;Mesh[] meshes=null;
  try
  {
   root=Object.Instantiate(live.gameObject);root.name="Temporary right recovery review";root.hideFlags=HideFlags.HideAndDontSave;
   foreach(var animator in root.GetComponentsInChildren<Animator>(true))animator.enabled=false;
   foreach(var script in root.GetComponentsInChildren<MonoBehaviour>(true))script.enabled=false;
   root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);root.transform.localScale=live.transform.lossyScale;
   foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
   var skins=root.GetComponentsInChildren<SkinnedMeshRenderer>(true);meshes=skins.Select(s=>new Mesh()).ToArray();
   var proxies=skins.Select(s=>{var g=new GameObject("Review baked skin");g.layer=31;g.transform.SetParent(s.transform,false);g.AddComponent<MeshFilter>();g.AddComponent<MeshRenderer>().sharedMaterials=s.sharedMaterials;s.enabled=false;return g;}).ToArray();
   camGo=new GameObject("Temporary review camera");camGo.hideFlags=HideFlags.HideAndDontSave;var cam=camGo.AddComponent<Camera>();cam.enabled=false;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.13f,.16f,.20f);cam.orthographic=true;cam.orthographicSize=1.65f;cam.nearClipPlane=.01f;cam.farClipPlane=30;cam.cullingMask=1<<31;
   cam.transform.position=new Vector3(4,2.3f,4);cam.transform.LookAt(new Vector3(0,1.25f,0));
   floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.hideFlags=HideFlags.HideAndDontSave;floor.layer=31;floor.transform.position=new Vector3(0,-.045f,0);floor.transform.localScale=new Vector3(10,.05f,10);
   mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));mat.color=new Color(.22f,.24f,.26f);floor.GetComponent<Renderer>().sharedMaterial=mat;
   const int width=400,height=500;sheet=new Texture2D(width*5,height*2,TextureFormat.RGB24,false);var frames=new[]{43,44,60,80,104};
   for(int row=0;row<2;row++)for(int col=0;col<5;col++)
   {
    (row==0?before:after).SampleAnimation(root,frames[col]/Rate);
    for(int k=0;k<skins.Length;k++){skins[k].BakeMesh(meshes[k],true);proxies[k].GetComponent<MeshFilter>().sharedMesh=meshes[k];}
    var rt=RenderTexture.GetTemporary(width,height,24);var oldActive=RenderTexture.active;Texture2D tex=null;
    try{cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;tex=new Texture2D(width,height,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,width,height),0,0);tex.Apply();sheet.SetPixels(col*width,(1-row)*height,width,height,tex.GetPixels());}
    finally{cam.targetTexture=null;RenderTexture.active=oldActive;RenderTexture.ReleaseTemporary(rt);if(tex)Object.DestroyImmediate(tex);}
   }
   sheet.Apply();File.WriteAllBytes(Preview,sheet.EncodeToPNG());
  }
  finally{if(meshes!=null)foreach(var m in meshes)Object.DestroyImmediate(m);if(sheet)Object.DestroyImmediate(sheet);if(root)Object.DestroyImmediate(root);if(camGo)Object.DestroyImmediate(camGo);if(floor)Object.DestroyImmediate(floor);if(mat)Object.DestroyImmediate(mat);}
 }
 public static string CaptureAndReview()
 {
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required.");
  var target=BattleCharacterAssetPaths.Load<AnimationClip>(TargetPath);string hash=Fingerprint(target);bool sceneDirty=SceneManager.GetActiveScene().isDirty;
  string backupPath=AssetDatabase.GenerateUniqueAssetPath(Folder+"/Backups/DebugFinisher_UserEdit_BeforeRightRecovery_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".anim");
  var backup=Object.Instantiate(target);backup.name=Path.GetFileNameWithoutExtension(backupPath);backup.hideFlags=HideFlags.None;AssetDatabase.CreateAsset(backup,backupPath);AssetDatabase.SaveAssetIfDirty(backup);
  var a=Curves(target);var b=Curves(backup);foreach(var binding in a.Keys)if(!a[binding].keys.SequenceEqual(b[binding].keys))throw new Exception("Backup mismatch.");
  AnimationClip candidate=null;
  try{candidate=Build(backup);var verification=Verify(backup,candidate);Render(backup,candidate);if(Fingerprint(target)!=hash||SceneManager.GetActiveScene().isDirty!=sceneDirty)throw new Exception("Review altered source state.");
   var report=new{target=TargetPath,backup=backupPath,sourceFingerprint=hash,preview=Preview,previewFrames=new[]{43,44,60,80,104},topRow="Before",bottomRow="Proposed",verification};
   string json=JsonConvert.SerializeObject(report,Formatting.Indented);File.WriteAllText(Manifest,json);return json;
  }finally{if(candidate)Object.DestroyImmediate(candidate);}
 }
 public static string Apply()
 {
  var plan=JObject.Parse(File.ReadAllText(Manifest));var target=BattleCharacterAssetPaths.Load<AnimationClip>(TargetPath);var backup=BattleCharacterAssetPaths.Load<AnimationClip>((string)plan["backup"]);
  if(Fingerprint(target)!=(string)plan["sourceFingerprint"])throw new Exception("User edited clip after capture; recapture before applying.");
  var candidate=Build(backup);string saved=EditorJsonUtility.ToJson(target);bool sceneDirty=SceneManager.GetActiveScene().isDirty;
  try
  {
   var bindings=AnimationUtility.GetCurveBindings(candidate).Where(Affected).ToArray();var curves=bindings.Select(b=>AnimationUtility.GetEditorCurve(candidate,b)).ToArray();
   Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Smooth only right arm recovery after frame 43");Undo.RegisterCompleteObjectUndo(target,"Smooth only right arm recovery after frame 43");
   AnimationUtility.SetEditorCurves(target,bindings,curves);var verification=Verify(backup,target);
   if(SceneManager.GetActiveScene().isDirty!=sceneDirty)throw new Exception("Scene dirty state changed.");
   EditorUtility.SetDirty(target);AssetDatabase.SaveAssetIfDirty(target);Undo.CollapseUndoOperations(group);
   foreach(var w in Resources.FindObjectsOfTypeAll<AnimationWindow>().Where(w=>w.animationClip==target))w.Repaint();SceneView.RepaintAll();
   var result=new{applied=true,target=TargetPath,backup=(string)plan["backup"],verification,sceneChanged=false,controllerChanged=false};string json=JsonConvert.SerializeObject(result,Formatting.Indented);File.WriteAllText(Report,json);return json;
  }
  catch{EditorJsonUtility.FromJsonOverwrite(saved,target);EditorUtility.SetDirty(target);AssetDatabase.SaveAssetIfDirty(target);throw;}
  finally{Object.DestroyImmediate(candidate);}
 }
}