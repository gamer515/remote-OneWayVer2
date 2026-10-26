using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using OneWay.Battle3D;
public static class B1ExactAnimationCheck {
 public static string Run(){
  if(!Application.isPlaying)throw new Exception("Play mode required");
  const string path="Battle3D/Stages/B1/Boss1_AllAnimations";
  var g=UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Single(t=>AnimationUtility.CalculateTransformPath(t,null)==path).gameObject;
  var a=g.GetComponent<Animator>();var boss=g.GetComponent<BattleBoss>();
  if(boss.animator!=a)throw new Exception("BattleBoss points to another Animator");
  var scripts=g.GetComponents<MonoBehaviour>();var flags=scripts.Select(s=>s.enabled).ToArray();
  bool active=g.activeSelf,enabled=a.enabled;var oldMode=a.cullingMode;
  var mesh=new Mesh();var report=new List<string>{"Exact target: "+path};
  try{
   foreach(var s in scripts)s.enabled=false;g.SetActive(true);a.enabled=true;a.cullingMode=AnimatorCullingMode.AlwaysAnimate;a.Rebind();a.Update(0);
   var parts=g.GetComponentsInChildren<Transform>(true);var skin=g.GetComponentInChildren<SkinnedMeshRenderer>();
   foreach(var name in new[]{"Jump_with_Arms_Open_Boss1","Run_03_Boss1"}){
    var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/3DBattle/Boss1/"+name+".anim");
    if(AnimationUtility.GetCurveBindings(clip).Any(b=>b.path!=""&&g.transform.Find(b.path)==null))throw new Exception("Unbound clip "+name);
    int hash=Animator.StringToHash("Base Layer."+name);if(!a.HasState(0,hash))throw new Exception("Missing state "+name);
    a.Play(hash,0,0);a.Update(0);var rotations=parts.Select(t=>t.localRotation).ToArray();skin.BakeMesh(mesh);var vertices=mesh.vertices;
    float maxMotion=0;int moved=0;
    for(int i=1;i<=8;i++){
     a.Play(hash,0,i/9f);a.Update(0);
     if(a.GetCurrentAnimatorStateInfo(0).fullPathHash!=hash)throw new Exception("Wrong active state");
     moved+=parts.Where((t,j)=>Quaternion.Angle(rotations[j],t.localRotation)>.1f).Count();
     skin.BakeMesh(mesh);var current=mesh.vertices;for(int j=0;j<current.Length;j++)maxMotion=Mathf.Max(maxMotion,Vector3.Distance(current[j],vertices[j]));
    }
    if(moved==0||maxMotion<.001f)throw new Exception("No visible deformation "+name);
    report.Add(name+": state found, missing bindings=0; bone movement samples="+moved+"; baked mesh vertex delta="+maxMotion+" (mesh units)");
   }
  }finally{UnityEngine.Object.DestroyImmediate(mesh);a.cullingMode=oldMode;a.enabled=enabled;g.SetActive(active);for(int i=0;i<scripts.Length;i++)scripts[i].enabled=flags[i];}
  report.Add("Ground contact not evaluated or changed. Runtime-only inspection; no scene mutations saved.");
  File.WriteAllLines("Assets/3DBattle/Authoring~/B1ExactAnimationValidation.txt",report);
  return string.Join("\n",report);
 }
}

