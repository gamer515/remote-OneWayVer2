using System;
using System.Linq;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;
public static class EquipPlayerShield {
 public static string Run(){
  if(EditorApplication.isPlaying)throw new Exception("Exit Play mode first");
  var player=UnityEngine.Object.FindObjectsByType<BattlePlayerController>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(p=>p.gameObject.scene.path=="Assets/Scenes/3DBattleScene.unity");
  var wrist=player.animator.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Palm.L");
  var shield=UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(t=>t.name=="Shield4"&&t.gameObject.scene==player.gameObject.scene&&t.GetComponent<MeshRenderer>()!=null);
  var renderer=shield.GetComponent<MeshRenderer>();var materials=renderer.sharedMaterials;
  var before=shield.position;var rotation=shield.rotation;var scale=shield.lossyScale;
  Undo.SetTransformParent(shield,wrist,"Equip shield on left wrist");
  PrefabUtility.RecordPrefabInstancePropertyModifications(shield);
  if(Vector3.Distance(before,shield.position)>.0001f||Quaternion.Angle(rotation,shield.rotation)>.05f||Vector3.Distance(scale,shield.lossyScale)>.0001f)throw new Exception("Attachment changed visible pose");
  if(!materials.SequenceEqual(renderer.sharedMaterials))throw new Exception("Materials changed");
  var clone=UnityEngine.Object.Instantiate(player.gameObject);clone.hideFlags=HideFlags.HideAndDontSave;
  string result;
  try{
   foreach(var component in clone.GetComponentsInChildren<MonoBehaviour>(true))component.enabled=false;
   var p=clone.GetComponent<BattlePlayerController>();p.animator.enabled=false;
   var s=clone.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Shield4");var localPosition=s.localPosition;var localRotation=s.localRotation;var localScale=s.localScale;
   string[] names={"MeshyRun_Player","MeshyCombo01","MeshyCombo04","MeshyCombo05","MeshyShieldPushLeft_Player","MeshyGroundRoll_Player"};float moved=0;
   foreach(string name in names){var clip=p.animator.runtimeAnimatorController.animationClips.Single(c=>c.name==name);clip.SampleAnimation(p.animator.gameObject,0);var first=s.position;for(int i=1;i<=6;i++){
    clip.SampleAnimation(p.animator.gameObject,clip.length*i/6f);moved=Mathf.Max(moved,Vector3.Distance(first,s.position));
    if(Vector3.Distance(localPosition,s.localPosition)>.0001f||Quaternion.Angle(localRotation,s.localRotation)>.05f||Vector3.Distance(localScale,s.localScale)>.0001f)throw new Exception("Shield detached during "+name);
   }}
   if(moved<.01f)throw new Exception("Shield did not follow animation");
   result="Shield4 attached to player's Palm.L; original world placement, scale and materials retained.\nValidated attachment through run, attacks 1/4/5, shield push, roll; max motion="+moved+"m; local attachment remained constant.\nScene="+player.gameObject.scene.path;
  }finally{UnityEngine.Object.DestroyImmediate(clone);}
  EditorSceneManager.MarkSceneDirty(player.gameObject.scene);if(!EditorSceneManager.SaveScene(player.gameObject.scene))throw new Exception("Scene save failed");
  File.WriteAllText("Assets/3DBattle/Authoring~/PlayerShieldValidation.txt",result);return result;
 }
}
