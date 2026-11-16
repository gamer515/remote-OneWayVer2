using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;
public static class PromoteDebugBattle
{
 const string Source="Assets/Scenes/3DBattleDebugScene.unity",Target="Assets/Scenes/3DBattleScene.unity";
 static Scene Load(string path){var s=SceneManager.GetSceneByPath(path);return s.isLoaded?s:EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);}
 static Battle3DSceneController Battle(Scene s)=>s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Battle3DSceneController>(true)).Single();
 public static object Inspect()
 {
  if(Application.isPlaying)throw new Exception("Edit mode required");
  return new[]{Load(Source),Load(Target)}.Select(s=>{var b=Battle(s);return new{scene=s.name,dirty=s.isDirty,player=b.player.name,playerActive=b.player.gameObject.activeSelf,stages=b.stages.Select(x=>new{name=x.name,id=x.StageIndex,boss=x.boss.name}).ToArray(),intro=b.GetComponent<BattleIntroWalkDebug>()?.movementState};}).ToArray();
 }
 static void MapTree(Transform source,Transform target,Dictionary<UnityEngine.Object,UnityEngine.Object> map)
 {
  map[source]=target;map[source.gameObject]=target.gameObject;
  foreach(var c in source.GetComponents<Component>())if(c!=null){var other=target.GetComponent(c.GetType());if(other!=null)map[c]=other;}
  foreach(Transform child in source){var other=target.Find(child.name);if(other!=null)MapTree(child,other,map);}
 }
 static Component[] Components(Scene s)=>s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Component>(true)).ToArray();
 static bool Inside(Component c,GameObject root)=>c!=null && (c.gameObject==root || c.transform.IsChildOf(root.transform));
 static GameObject Owner(UnityEngine.Object o)=>o is GameObject g?g:o is Component c?c.gameObject:null;
 public static object Run()
 {
  if(Application.isPlaying)throw new Exception("Edit mode required");
  var source=Load(Source);var dest=Load(Target);var src=Battle(source);var dst=Battle(dest);
  var sb=src.stages.Single(x=>x.StageIndex==1);var db=dst.stages.Single(x=>x.StageIndex==1);
  if(dst.player.GetComponent<NewPlayerAttackCombo>()!=null)throw new Exception("New player already installed; inspect before repeating");
  // Persist the user's latest debug-scene equipment/pose edits before testing the destination alone.
  if(source.isDirty)EditorSceneManager.SaveScene(source);
  Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Promote debug player and B1 boss");
  GameObject oldPlayer=dst.player.gameObject,oldBoss=db.boss.gameObject;
  var oldPlayerParent=oldPlayer.transform.parent;var oldBossParent=oldBoss.transform.parent;
  try
  {
   var newPlayer=UnityEngine.Object.Instantiate(src.player.gameObject);newPlayer.name=src.player.name;
   SceneManager.MoveGameObjectToScene(newPlayer,dest);Undo.RegisterCreatedObjectUndo(newPlayer,"Copy new player");newPlayer.transform.SetParent(oldPlayerParent,true);
   var newBoss=UnityEngine.Object.Instantiate(sb.boss.gameObject);newBoss.name=oldBoss.name;
   SceneManager.MoveGameObjectToScene(newBoss,dest);Undo.RegisterCreatedObjectUndo(newBoss,"Copy B1 boss");newBoss.transform.SetParent(oldBossParent,true);
   newBoss.transform.SetPositionAndRotation(db.bossSpawn.position,db.bossSpawn.rotation);
   var current=dst.CurrentStage;newPlayer.transform.SetPositionAndRotation(current.playerSpawn.position,current.playerSpawn.rotation);
   var map=new Dictionary<UnityEngine.Object,UnityEngine.Object>();
   MapTree(src.transform,dst.transform,map);MapTree(src.followCamera.transform,dst.followCamera.transform,map);
   MapTree(src.player.transform,newPlayer.transform,map);MapTree(sb.boss.transform,newBoss.transform,map);
   MapTree(oldPlayer.transform,newPlayer.transform,map);MapTree(oldBoss.transform,newBoss.transform,map);
   var errors=new List<string>();int rewired=0;
   foreach(var c in Components(dest))
   {
    if(c==null)throw new Exception("Missing script in destination");
    if(Inside(c,oldPlayer)||Inside(c,oldBoss))continue;
    var so=new SerializedObject(c);var prop=so.GetIterator();bool changed=false;
    while(prop.Next(true))
    {
     if(prop.propertyType!=SerializedPropertyType.ObjectReference || prop.propertyPath=="m_Script")continue;
     var value=prop.objectReferenceValue;if(value==null)continue;
     if(map.TryGetValue(value,out var replacement) && value!=replacement)
     {if(!changed)Undo.RecordObject(c,"Rewire battle references");prop.objectReferenceValue=replacement;changed=true;rewired++;}
     else
     {
      var owner=Owner(value);
      if(owner!=null && owner.scene.IsValid() && (owner.scene!=dest || owner==oldPlayer || owner.transform.IsChildOf(oldPlayer.transform) || owner==oldBoss || owner.transform.IsChildOf(oldBoss.transform)))errors.Add(c.name+"."+prop.propertyPath+" -> "+owner.name);
     }
    }
    if(changed)so.ApplyModifiedProperties();
   }
   if(errors.Count>0)throw new Exception("Unmapped refs: "+string.Join("; ",errors));
   var intro=dst.GetComponent<BattleIntroWalkDebug>();
   if(intro!=null){Undo.RecordObject(intro,"New player intro locomotion");intro.movementState="Base Layer."+newPlayer.GetComponent<NewPlayerLocomotion>().runClip.name;}
   Undo.DestroyObjectImmediate(oldPlayer);Undo.DestroyObjectImmediate(oldBoss);
   newPlayer.SetActive(true);newBoss.SetActive(true);
   EditorSceneManager.MarkSceneDirty(dest);EditorSceneManager.SaveScene(dest);
   Undo.CollapseUndoOperations(group);SceneManager.SetActiveScene(dest);EditorSceneManager.CloseScene(source,true);
   Selection.activeGameObject=dst.gameObject;
   return new {scene=dest.name,player=dst.player.name,boss=db.boss.name,phase2=db.boss.GetComponent<BossPhaseTwoGimmick>()!=null,rewired,stages=dst.stages.Length,intro= intro!=null?intro.movementState:"none",controllerEnabled=dst.enabled};
  }
  catch{Undo.RevertAllDownToGroup(group);throw;}
 }
}
