using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
public static class InspectNewPlayerRigs
{
 public static object Run()
 {
  return System.IO.Directory.GetFiles("Assets/3DBattle/NewPlayer","*.fbx").OrderBy(p=>p).Select(raw=>
  {
   string path=raw.Replace('\\','/');var g=AssetDatabase.LoadAssetAtPath<GameObject>(path);var importer=(ModelImporter)AssetImporter.GetAtPath(path);
   var animator=g.GetComponent<Animator>();
   var renderers=g.GetComponentsInChildren<SkinnedMeshRenderer>(true);
   var clips=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).ToArray();
   return new{file=System.IO.Path.GetFileName(path),rigType=importer.animationType.ToString(),avatarSetup=importer.avatarSetup.ToString(),avatarHuman=animator!=null&&animator.avatar!=null&&animator.avatar.isHuman,
    meshes=renderers.Select(s=>new{s.name,vertices=s.sharedMesh.vertexCount,boneCount=s.bones.Length,rootBone=s.rootBone!=null?s.rootBone.name:null,bones=s.bones.Select(b=>new{name=b.name,parent=b.parent!=null?b.parent.name:null,path=AnimationUtility.CalculateTransformPath(b,g.transform)}).ToArray()}).ToArray(),
    clips=clips.Select(c=>new{c.name,c.length,c.frameRate,c.humanMotion,bindings=AnimationUtility.GetCurveBindings(c).Length,paths=AnimationUtility.GetCurveBindings(c).Select(b=>b.path).Distinct().OrderBy(p=>p).ToArray()}).ToArray()};
  }).ToArray();
 }
}
