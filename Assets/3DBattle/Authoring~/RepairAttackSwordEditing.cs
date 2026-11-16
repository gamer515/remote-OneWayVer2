using System;using System.Linq;using System.Threading.Tasks;using UnityEngine;using UnityEditor;
public static class RepairAttackSwordEditing
{
 public static async Task<object> Run()
 {
  if(Application.isPlaying)throw new Exception("Stop play first");var root=GameObject.Find("Attack 01 - Attack");var a=root.GetComponentInChildren<Animator>();var sword=a.GetComponentsInChildren<Transform>().First(t=>t.name=="Sword");var clip=a.runtimeAnimatorController.animationClips[0];var path=AnimationUtility.CalculateTransformPath(sword,a.transform);string asset=AssetDatabase.GetAssetPath(clip);string backup=asset.Replace(".anim","_BeforeEditableSword.anim");if(AssetDatabase.LoadAssetAtPath<AnimationClip>(backup)==null)AssetDatabase.CopyAsset(asset,backup);
  clip.SampleAnimation(a.gameObject,0);var q=sword.localRotation;var p=sword.localPosition;var e=q.eulerAngles;
  var bindings=AnimationUtility.GetCurveBindings(clip).Where(b=>b.path==path).ToArray();
  // Only collapse channels that are constant; retain user edits if any exist.
  bool constant=bindings.All(b=>{var c=AnimationUtility.GetEditorCurve(clip,b);return c.keys.All(k=>Mathf.Abs(k.value-c.keys[0].value)<.00001f);});
  var times=bindings.SelectMany(b=>{var c=AnimationUtility.GetEditorCurve(clip,b);return c.keys.Where(k=>Mathf.Abs(k.value-c.keys[0].value)>.0001f).Select(k=>k.time);}).Distinct().OrderBy(t=>t).ToArray();
  var samples=new System.Collections.Generic.SortedDictionary<float,Vector3[]>();samples[0]=new[]{p,e};samples[clip.length]=new[]{p,e};
  if(times.Length>0){samples[Mathf.Max(0,(Mathf.Floor(times.First()*30)-1)/30)]=new[]{p,e};samples[Mathf.Min(clip.length,(Mathf.Ceil(times.Last()*30)+1)/30)]=new[]{p,e};}
  foreach(float t in times){clip.SampleAnimation(a.gameObject,t);var eu=sword.localRotation.eulerAngles;for(int c=0;c<3;c++)eu[c]=e[c]+Mathf.DeltaAngle(e[c],eu[c]);samples[Mathf.Floor(t*30+.5001f)/30]=new[]{sword.localPosition,eu};}
  foreach(var b in bindings)AnimationUtility.SetEditorCurve(clip,b,null);
  for(int kind=0;kind<2;kind++)for(int c=0;c<3;c++){var curve=new AnimationCurve(samples.Select(s=>new Keyframe(s.Key,s.Value[kind][c])).ToArray());for(int k=0;k<curve.length;k++){AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Linear);}AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(path,typeof(Transform),(kind==0?"m_LocalPosition.":"localEulerAnglesRaw.")+"xyz"[c]),curve);}
  EditorUtility.SetDirty(clip);AssetDatabase.SaveAssets();
  // Exercise the Animation window's real Undo-based recording route, then remove the test edit.
  var saved=UnityEngine.Object.Instantiate(clip);var w=EditorWindow.GetWindow<AnimationWindow>();Selection.activeGameObject=a.gameObject;w.animationClip=clip;w.previewing=true;w.frame=10;w.recording=true;await Task.Delay(250);
  Quaternion before=sword.localRotation;Undo.RecordObject(sword,"Verify sword animation recording");sword.localRotation=before*Quaternion.Euler(0,7,0);Quaternion expected=sword.localRotation;Undo.FlushUndoRecordObjects();await Task.Delay(250);
  int keys=AnimationUtility.GetCurveBindings(clip).Where(b=>b.path==path&&b.propertyName.Contains("Euler")).Select(b=>AnimationUtility.GetEditorCurve(clip,b).length).Sum();w.frame=11;await Task.Delay(100);w.frame=10;await Task.Delay(100);float error=Quaternion.Angle(expected,sword.localRotation);
  w.recording=false;w.previewing=false;EditorUtility.CopySerialized(saved,clip);UnityEngine.Object.DestroyImmediate(saved);EditorUtility.SetDirty(clip);AssetDatabase.SaveAssets();Selection.activeGameObject=sword.gameObject;w.animationClip=clip;w.frame=10;
  return new{constantOriginal=constant,oldKeysPerChannel=1345,newKeysPerChannel=samples.Count,preservedUserEdits=times.Length,recordedEulerKeys=keys,returnFrameAngleError=error,recordingPassed=error<.1f&&keys>samples.Count*3};
 }
}
