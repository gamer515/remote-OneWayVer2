using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;
public static class SetupB2PaladinLoop {
 public static string Run(){
 if(Application.isPlaying)throw new Exception("Edit mode required");
 string folder="Assets/3DBattle/Meshy_AI_lowpoly_paladin_tpose_biped";
 var stage=UnityEngine.Object.FindObjectsByType<BattleStage>(FindObjectsInactive.Include,FindObjectsSortMode.None).First(s=>s.name=="B2");
 var model=stage.boss.GetComponentsInChildren<Transform>(true).First(t=>t.name.StartsWith("Meshy_AI_lowpoly_paladin"));
 var path=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(model.gameObject);
 var source=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__"));
 var missing=AnimationUtility.GetCurveBindings(source).Where(b=>b.path!=""&&model.Find(b.path)==null).ToArray();if(missing.Length>0)throw new Exception("Unbound tracks: "+missing.Length);
 string cp=folder+"/Heavy_Hammer_Swing_B2_Loop.anim";
 var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(cp);if(clip==null){clip=UnityEngine.Object.Instantiate(source);AssetDatabase.CreateAsset(clip,cp);}else EditorUtility.CopySerialized(source,clip);
 clip.name="Heavy_Hammer_Swing_B2_Loop";clip.legacy=false;var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=true;AnimationUtility.SetAnimationClipSettings(clip,settings);EditorUtility.SetDirty(clip);
 string ac=folder+"/Paladin_B2_Loop.controller";var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(ac)??AnimatorController.CreateAnimatorControllerAtPath(ac);
 var machine=controller.layers[0].stateMachine;var state=machine.states.Select(x=>x.state).FirstOrDefault(s=>s.name=="HeavyHammerLoop")??machine.AddState("HeavyHammerLoop");state.motion=clip;state.speed=1;machine.defaultState=state;EditorUtility.SetDirty(controller);
 Undo.RecordObject(model,"Align B2 paladin visual");model.localPosition=Vector3.zero;
 var animator=model.GetComponent<Animator>();if(animator==null)animator=Undo.AddComponent<Animator>(model.gameObject);animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
 var boss=stage.boss;Undo.RecordObject(boss,"Connect B2 paladin animation");boss.animator=animator;boss.bodyParts=model.GetComponentsInChildren<Renderer>(true);boss.body=boss.bodyParts.First();boss.hitbox=boss.GetComponent<Collider>();boss.idleState="Base Layer.HeavyHammerLoop";boss.walkState=boss.idleState;
 PrefabUtility.RecordPrefabInstancePropertyModifications(model);PrefabUtility.RecordPrefabInstancePropertyModifications(animator);EditorUtility.SetDirty(animator);EditorUtility.SetDirty(boss);
 AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(stage.gameObject.scene);EditorSceneManager.SaveScene(stage.gameObject.scene);
 return "Connected B2; all "+AnimationUtility.GetCurveBindings(source).Length+" tracks match directly; loop "+clip.length+" seconds; source FBX unchanged.";
 }
}

