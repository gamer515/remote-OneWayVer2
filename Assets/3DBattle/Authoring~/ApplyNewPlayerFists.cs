using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class ApplyNewPlayerFists
{
 public static object Run()
 {
  if(Application.isPlaying)throw new Exception("Stop Play first");
  var modified=new HashSet<Mesh>();var report=new List<object>();
  foreach(string name in new[]{"Running Knight Preview","New Knight Defeat Preview","New Knight Roll Preview"})
  {
   var root=GameObject.Find(name);var skin=root.GetComponentInChildren<SkinnedMeshRenderer>();var mesh=skin.sharedMesh;
   int hand=Array.FindIndex(skin.bones,b=>b.name=="RightHand");int arm=Array.FindIndex(skin.bones,b=>b.name=="RightLowerArm"||b.name=="RightForeArm");
   Vector3 wrist=mesh.bindposes[hand].inverse.GetColumn(3);Vector3 elbow=mesh.bindposes[arm].inverse.GetColumn(3);Vector3 axis=(wrist-elbow).normalized;
   var old=mesh.vertices;int changed=0;
   if(modified.Add(mesh))
   {
    string path=AssetDatabase.GetAssetPath(mesh),backup=path.Replace(".asset","_BeforeRightFist.asset");
    if(AssetDatabase.LoadAssetAtPath<Mesh>(backup)==null)AssetDatabase.CopyAsset(path,backup);
    changed=NewPlayerRightFist.Apply(skin);EditorUtility.SetDirty(mesh);
    var ws=mesh.boneWeights;var now=mesh.vertices;
    int left=Array.FindIndex(skin.bones,b=>b.name=="LeftHand");
    if(Enumerable.Range(0,old.Length).Any(i=>ws[i].boneIndex0==left&&Vector3.Distance(old[i],now[i])>.000001f))throw new Exception("Left hand changed");
   }
   var animator=root.GetComponentInChildren<Animator>();var clip=animator.runtimeAnimatorController.animationClips[0];
   var sword=skin.bones[hand].GetComponentsInChildren<Transform>().First(t=>t.name=="Sword");
   string swordPath=AnimationUtility.CalculateTransformPath(sword,animator.transform);
   // Move the existing grip from the wrist into the center of the curled palm.
   Vector3 localOffset=mesh.bindposes[hand].MultiplyVector(axis*.095f-Vector3.ProjectOnPlane(Vector3.up,axis).normalized*.025f);
   for(int j=0;j<3;j++)
   {
    var binding=EditorCurveBinding.FloatCurve(swordPath,typeof(Transform),"m_LocalPosition."+"xyz"[j]);var curve=AnimationUtility.GetEditorCurve(clip,binding);
    var keys=curve.keys;for(int k=0;k<keys.Length;k++)keys[k].value+=localOffset[j];curve.keys=keys;AnimationUtility.SetEditorCurve(clip,binding,curve);
   }
   EditorUtility.SetDirty(clip);clip.SampleAnimation(animator.gameObject,0);
   report.Add(new{name,changedVertices=changed,leftHandPreserved=true});
  }
  AssetDatabase.SaveAssets();var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
  foreach(string path in System.IO.Directory.GetFiles("Assets/3DBattle/NewPlayer","*.fbx"))AssetDatabase.ImportAsset(path.Replace('\\','/'),ImportAssetOptions.ForceUpdate);
  return report;
 }
}
