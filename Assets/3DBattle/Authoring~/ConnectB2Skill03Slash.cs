using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using OneWay.Battle3D;
public static class ConnectB2Skill03Slash
{
 public static object Run()
 {
  if(Application.isPlaying)throw new Exception("Edit mode required");
  var emitter=UnityEngine.Object.FindObjectsByType<B2SwordSlash>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(e=>e.gameObject.activeInHierarchy);
  Undo.RecordObject(emitter,"Align Skill03 slash to blade");
  emitter.swordBlade=emitter.GetComponentsInChildren<Transform>().Single(t=>t.name=="B2 Holy Sword");
  var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/3DBattle/Boss2/BossAnim/Crisp/B2_Skill03_FromPlayer.anim");
  var events=AnimationUtility.GetAnimationEvents(clip);int changed=0;
  foreach(var e in events)if(e.functionName=="EmitB2SwordSlash"||e.functionName=="EmitB2Skill03Slash"){e.functionName="EmitB2Skill03Slash";changed++;}
  if(changed!=1)throw new Exception("Unexpected Skill03 event count: "+changed);
  Undo.RecordObject(clip,"Align Skill03 slash to blade");AnimationUtility.SetAnimationEvents(clip,events);
  EditorUtility.SetDirty(clip);EditorUtility.SetDirty(emitter);PrefabUtility.RecordPrefabInstancePropertyModifications(emitter);AssetDatabase.SaveAssets();
  UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(emitter.gameObject.scene);
  bool saved=UnityEditor.SceneManagement.EditorSceneManager.SaveScene(emitter.gameObject.scene);
  return new{saved,blade=emitter.swordBlade.name,events=events.Select(e=>new{e.functionName,frame=e.time*clip.frameRate}).ToArray()};
 }
}
