using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
public static class BuildNewKnightAttackPreviews
{
 public static object Run()
 {
  if(Application.isPlaying)throw new Exception("Stop play mode first");var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();if(scene.name!="3DBattleDebugScene")throw new Exception("Wrong scene");
  if(GameObject.Find("New Knight Attack Previews")!=null)throw new Exception("Attack previews already exist");
  var paths=AssetDatabase.FindAssets("t:Model",new[]{"Assets/3DBattle/NewPlayer/Attack"}).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p=>p).ToArray();
  var template=GameObject.Find("Running Knight Preview");
  const string folder="Assets/3DBattle/NewPlayer/Attack/Previews";if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder("Assets/3DBattle/NewPlayer/Attack","Previews");
  var group=new GameObject("New Knight Attack Previews");Undo.RegisterCreatedObjectUndo(group,"Create attack previews");group.transform.position=template.transform.position+template.transform.forward*5f;
  var report=new List<object>();
  for(int i=0;i<paths.Length;i++)
  {
   var source=AssetDatabase.LoadAllAssetsAtPath(paths[i]).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
   var root=UnityEngine.Object.Instantiate(template,group.transform);root.name=string.Format("Attack {0:00} - {1}",i+1,source.name);root.transform.SetPositionAndRotation(group.transform.position+template.transform.right*(i%3*4f)+template.transform.forward*(i/3*4f),template.transform.rotation);
   var a=root.GetComponentInChildren<Animator>();a.enabled=false;a.runtimeAnimatorController.animationClips[0].SampleAnimation(a.gameObject,0);
   int missing=AnimationUtility.GetCurveBindings(source).Count(b=>a.transform.Find(b.path)==null);if(missing!=0)throw new Exception(source.name+" missing paths: "+missing);
   var clip=UnityEngine.Object.Instantiate(source);clip.name="NewKnight_"+source.name+"_Preview";clip.frameRate=source.frameRate;
   var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=true;settings.loopBlend=false;AnimationUtility.SetAnimationClipSettings(clip,settings);
   AssetDatabase.CreateAsset(clip,folder+"/"+clip.name+".anim");var controller=AnimatorController.CreateAnimatorControllerAtPath(folder+"/"+clip.name+".controller");var state=controller.layers[0].stateMachine.AddState(source.name);state.motion=clip;controller.layers[0].stateMachine.defaultState=state;
   a.runtimeAnimatorController=controller;a.applyRootMotion=false;a.cullingMode=AnimatorCullingMode.AlwaysAnimate;a.enabled=true;clip.SampleAnimation(a.gameObject,0);
   bool exact=AnimationUtility.GetCurveBindings(source).All(b=>{var x=AnimationUtility.GetEditorCurve(source,b);var y=AnimationUtility.GetEditorCurve(clip,b);return x.keys.SequenceEqual(y.keys);});
   report.Add(new{objectName=root.name,source=paths[i],fps=clip.frameRate,lastFrame=Mathf.RoundToInt(source.length*source.frameRate),duration=clip.length,unchangedCurves=exact,missingBindings=missing,rightFist=root.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh.name.EndsWith("_RightFist")});
  }
  AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);Selection.activeGameObject=group;SceneView.lastActiveSceneView?.FrameSelected();
  System.IO.File.WriteAllText("Assets/3DBattle/Authoring~/AttackPreviewReport.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));return report;
 }
 public static object Preview()
 {
  var group=GameObject.Find("New Knight Attack Previews");var cg=new GameObject("Attack preview camera");var cam=cg.AddComponent<Camera>();cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.22f,.24f,.26f);cam.fieldOfView=40;
  var rt=new RenderTexture(400,500,24);cam.targetTexture=rt;var tex=new Texture2D(1200,1000,TextureFormat.RGB24,false);var old=RenderTexture.active;
  try{for(int i=0;i<group.transform.childCount;i++){var root=UnityEngine.Object.Instantiate(group.transform.GetChild(i).gameObject);try{root.transform.SetPositionAndRotation(new Vector3(1000,0,1000),Quaternion.identity);var a=root.GetComponentInChildren<Animator>();a.enabled=false;var c=a.runtimeAnimatorController.animationClips[0];c.SampleAnimation(a.gameObject,c.length*.35f);var skin=root.GetComponentInChildren<SkinnedMeshRenderer>();var hips=skin.bones.First(b=>b.name=="Hips");var target=hips.position+Vector3.up*.1f;cam.transform.position=target+new Vector3(-2.7f,1.2f,4.8f);cam.transform.LookAt(target);cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,400,500),i%3*400,(1-i/3)*500);}finally{UnityEngine.Object.DestroyImmediate(root);}}
   tex.Apply();System.IO.File.WriteAllBytes("Assets/3DBattle/Authoring~/AttackPreviews.png",tex.EncodeToPNG());return "saved";
  }finally{RenderTexture.active=old;cam.targetTexture=null;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(cg);}
 }
}
