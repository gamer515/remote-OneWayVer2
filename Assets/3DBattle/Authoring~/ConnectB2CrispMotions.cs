using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using OneWay.Battle3D;
public static class ConnectB2CrispMotions
{
 public static object Run()
 {
  if(Application.isPlaying)throw new Exception("Edit mode required");
  string folder="Assets/3DBattle/Boss2/BossAnim/Crisp/";
  string path=folder+"B2_CrispSequence.controller";
  
  var controller=BattleCharacterAssetPaths.Load<AnimatorController>(path);if(controller==null)controller=AnimatorController.CreateAnimatorControllerAtPath(path);
  string[] clips={"B2_PowerSlash_Crisp","B2_DoubleCombo_Crisp","B2_WeaponCombo_Crisp"};string[] names={"PowerSlash","DoubleCombo","WeaponCombo"};
  var sm=controller.layers[0].stateMachine;var states=new AnimatorState[3];
  for(int i=0;i<3;i++){states[i]=sm.states.Select(x=>x.state).FirstOrDefault(x=>x.name==names[i]);if(states[i]==null)states[i]=sm.AddState(names[i],new Vector3(i*260,0,0));states[i].motion=BattleCharacterAssetPaths.Load<AnimationClip>(folder+clips[i]+".anim");}
  sm.defaultState=states[0];
  for(int i=0;i<3;i++){var t=states[i].transitions.FirstOrDefault()??states[i].AddTransition(states[(i+1)%3]);t.hasExitTime=true;t.exitTime=1;t.hasFixedDuration=true;t.duration=.12f;t.offset=0;t.canTransitionToSelf=false;}
  var dead=sm.states.Select(x=>x.state).FirstOrDefault(x=>x.name=="Dead")??sm.AddState("Dead");dead.motion=states[0].motion;dead.speed=0;
  var b2=UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(t=>t.name=="B2");
  var boss=b2.Find("Boss").GetComponent<BattleBoss>();var animator=b2.GetComponentsInChildren<Animator>(true).Single(a=>a.name.Contains("Heavy_Hammer"));
  // Advance an isolated copy to validate the controller without starting gameplay or emitting attacks.
  var modelAsset=BattleCharacterAssetPaths.Load<GameObject>(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(animator.gameObject)));
  var test=UnityEngine.Object.Instantiate(modelAsset);var visited=new HashSet<string>();
  try
  {
   var legacy=test.GetComponent<Animation>();if(legacy)UnityEngine.Object.DestroyImmediate(legacy);var a=test.GetComponent<Animator>();if(!a)a=test.AddComponent<Animator>();a.avatar=animator.avatar;a.runtimeAnimatorController=controller;a.fireEvents=false;a.cullingMode=AnimatorCullingMode.AlwaysAnimate;a.Rebind();a.Update(0);
   for(int i=0;i<600;i++){a.Update(1/60f);var state=a.GetCurrentAnimatorStateInfo(0);foreach(string n in names)if(state.IsName(n))visited.Add(n);}
   if(visited.Count!=3)throw new Exception("Sequence did not visit every state: "+string.Join(",",visited));
  }
  finally{UnityEngine.Object.DestroyImmediate(test);}
  Undo.RecordObjects(new UnityEngine.Object[]{animator,boss},"Apply crisp B2 attack sequence");
  animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.speed=1;
  boss.animator=animator;boss.idleState="Base Layer.PowerSlash";boss.walkState="Base Layer.PowerSlash";boss.deadState="Base Layer.Dead";
  EditorUtility.SetDirty(animator);EditorUtility.SetDirty(boss);AssetDatabase.SaveAssets();
  UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(boss.gameObject.scene);bool saved=UnityEditor.SceneManagement.EditorSceneManager.SaveScene(boss.gameObject.scene);
  return new{saved,controller=path,visited=visited.ToArray(),eventsPerCycle=states.Sum(s=>AnimationUtility.GetAnimationEvents((AnimationClip)s.motion).Length),receiver=animator.GetComponent<B2SwordSlash>()!=null};
 }
}


