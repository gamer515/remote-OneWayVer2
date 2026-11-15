using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

public static class InspectB2Skill03Entry
{
 public static object Run()
 {
  var sceneAnimator=UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(a=>a.name.Contains("Heavy_Hammer")&&a.gameObject.activeInHierarchy);
  var controller=(AnimatorController)sceneAnimator.runtimeAnimatorController;
  var asset=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(sceneAnimator.gameObject)));
  var test=UnityEngine.Object.Instantiate(asset);
  try
  {
   test.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);test.transform.localScale=Vector3.one;
   var legacy=test.GetComponent<Animation>();if(legacy)UnityEngine.Object.DestroyImmediate(legacy);
   var a=test.GetComponent<Animator>();if(!a)a=test.AddComponent<Animator>();
   a.avatar=sceneAnimator.avatar;a.runtimeAnimatorController=controller;a.fireEvents=false;a.applyRootMotion=false;a.cullingMode=AnimatorCullingMode.AlwaysAnimate;a.Rebind();a.Update(0);
   var clip=(AnimationClip)controller.layers[0].stateMachine.states.Single(s=>s.state.name=="Skill03").state.motion;
   var hand=test.GetComponentsInChildren<Transform>().Single(t=>t.name=="RightHand");
   var sampled=new List<object>();Vector3 prev=Vector3.zero;
   for(int f=0;f<=16;f++){clip.SampleAnimation(test,f/30f);var p=hand.position;sampled.Add(new{frame=f,handDistance=f==0?0:Vector3.Distance(prev,p)});prev=p;}
   a.Rebind();a.Play("Base Layer.WeaponCombo",0,.98f);a.Update(0);
   var entry=new List<object>();bool started=false;
   for(int i=0;i<100;i++)
   {
    a.Update(1/120f);bool blend=a.IsInTransition(0);var current=a.GetCurrentAnimatorStateInfo(0);var next=a.GetNextAnimatorStateInfo(0);
    if(current.IsName("Skill03")||(blend&&next.IsName("Skill03")))
    {
     started=true;var state=current.IsName("Skill03")?current:next;
     entry.Add(new{frame=state.normalizedTime*clip.length*30,blend,blendProgress=blend?a.GetAnimatorTransitionInfo(0).normalizedTime:1});
     if(entry.Count>=23)break;
    }
    else if(started)break;
   }
   return new{sampled,entry};
  }
  finally{UnityEngine.Object.DestroyImmediate(test);}
 }
}
