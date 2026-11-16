using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using OneWay.Battle3D;

public static class BuildB2AttackSteps
{
 public static object Run()
 {
  if(Application.isPlaying)throw new Exception("Edit mode required");
  var animator=UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(a=>a.name.Contains("Heavy_Hammer")&&a.gameObject.activeInHierarchy);
  var boss=animator.GetComponentInParent<BattleBoss>();var arena=boss.GetComponent<BattleArenaBody>();
  var battle=UnityEngine.Object.FindObjectsByType<Battle3DSceneController>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(b=>b.gameObject.scene==boss.gameObject.scene);
  var controller=(UnityEditor.Animations.AnimatorController)animator.runtimeAnimatorController;
  var clone=UnityEngine.Object.Instantiate(animator.gameObject);var profiles=new List<B2AttackStepMovement.AttackStep>();var report=new List<object>();
  string[] names={"PowerSlash","DoubleCombo","WeaponCombo","Skill03"};
  try
  {
   clone.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);clone.transform.localScale=Vector3.one;clone.GetComponent<Animator>().enabled=false;
   var left=clone.GetComponentsInChildren<Transform>().Single(t=>t.name=="LeftFoot");var right=clone.GetComponentsInChildren<Transform>().Single(t=>t.name=="RightFoot");
   foreach(var name in names)
   {
    var state=controller.layers[0].stateMachine.states.Single(s=>s.state.name==name).state;var clip=(AnimationClip)state.motion;
    int count=Mathf.RoundToInt(clip.length*120)+1;var l=new Vector3[count];var r=new Vector3[count];float ground=float.PositiveInfinity;
    for(int i=0;i<count;i++){clip.SampleAnimation(clone,i/120f);l[i]=left.position;r[i]=right.position;ground=Mathf.Min(ground,l[i].y,r[i].y);}
    float distance=0;var curve=new AnimationCurve();curve.AddKey(new Keyframe(0,0));int start=-1,last=-1;
    for(int i=1;i<count;i++)
    {
     float ly=(l[i-1].y+l[i].y)*.5f,ry=(r[i-1].y+r[i].y)*.5f,low=Mathf.Min(ly,ry);
     // The lower foot carries most of the weight. A lifted foot must not cancel the planted foot's travel.
     float lw=Mathf.Exp(-(ly-low)/.025f),rw=Mathf.Exp(-(ry-low)/.025f);
     float contact=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(ground+.12f,ground+.20f,low));
     float step=Mathf.Max(0,-((l[i].z-l[i-1].z)*lw+(r[i].z-r[i-1].z)*rw)/(lw+rw))*contact;
     if(name=="Skill03"&&i/120f<=8/30f)step=0; // Existing entry blend holds a preparation pose.
     if(step>.00001f){if(start<0)start=i;last=i;}distance+=step;curve.AddKey(new Keyframe(i/120f,distance));
    }
    for(int i=0;i<curve.length;i++){AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear);}
    profiles.Add(new B2AttackStepMovement.AttackStep{stateName=name,clip=clip,distanceMultiplier=1,forwardDistance=curve});
    report.Add(new{state=name,localDistance=distance,worldDistance=distance*Mathf.Abs(animator.transform.lossyScale.z),firstMovingFrame=start/4f,lastMovingFrame=last/4f});
   }
  }
  finally{UnityEngine.Object.DestroyImmediate(clone);}
  var movement=boss.GetComponent<B2AttackStepMovement>();if(!movement)movement=Undo.AddComponent<B2AttackStepMovement>(boss.gameObject);
  Undo.RecordObject(movement,"Match B2 attack travel to feet");movement.animator=animator;movement.arenaBody=arena;movement.bodyCollider=boss.GetComponent<CapsuleCollider>();movement.attacks=profiles.ToArray();movement.distanceMultiplier=1;movement.enabled=true;
  if(movement.bodyCollider.direction!=1)throw new Exception("Movement expects an upright body capsule");
  Undo.RecordObject(arena,"Connect B2 arena constraint");arena.battle=battle;
  EditorUtility.SetDirty(arena);EditorUtility.SetDirty(movement);PrefabUtility.RecordPrefabInstancePropertyModifications(arena);PrefabUtility.RecordPrefabInstancePropertyModifications(movement);
  AssetDatabase.SaveAssets();UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(boss.gameObject.scene);bool saved=UnityEditor.SceneManagement.EditorSceneManager.SaveScene(boss.gameObject.scene);
  return new{saved,profile=report,arenaConnected=arena.battle!=null,component=movement.GetType().Name};
 }
}
