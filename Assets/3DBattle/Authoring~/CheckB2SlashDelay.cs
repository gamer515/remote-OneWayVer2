using System;using System.Linq;using System.Reflection;using UnityEngine;using UnityEditor;using UnityEditor.SceneManagement;using OneWay.Battle3D;
public static class CheckB2SlashDelay{
 public static string Run(){
 if(Application.isPlaying)throw new Exception("Edit mode required");
 var go=new GameObject("Temporary slash delay check");go.hideFlags=HideFlags.HideAndDontSave;
 try{var flight=go.AddComponent<B2SlashFlight>();var advance=typeof(B2SlashFlight).GetMethod("Advance",BindingFlags.Instance|BindingFlags.NonPublic);flight.Initialize(Vector3.forward,20,2,.2f);
 advance.Invoke(flight,new object[]{.1f});if(go.transform.position.magnitude>.00001f)throw new Exception("Moved before delay");
 advance.Invoke(flight,new object[]{.15f});if(Mathf.Abs(go.transform.position.z-1)>.0001f)throw new Exception("Delay boundary overshoot");
 advance.Invoke(flight,new object[]{.5f});if(Mathf.Abs(go.transform.position.z-11)>.0001f)throw new Exception("Speed mismatch");
 }finally{UnityEngine.Object.DestroyImmediate(go);}
 var emitter=UnityEngine.Object.FindObjectsByType<B2SwordSlash>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(e=>e.gameObject.scene.path=="Assets/Scenes/3DBattleScene.unity");
 Undo.RecordObject(emitter,"Set slash launch delay");emitter.launchDelay=.2f;EditorUtility.SetDirty(emitter);PrefabUtility.RecordPrefabInstancePropertyModifications(emitter);EditorSceneManager.MarkSceneDirty(emitter.gameObject.scene);EditorSceneManager.SaveScene(emitter.gameObject.scene);
 Selection.activeGameObject=emitter.gameObject;
 return "PASS: stationary during delay, correct partial-frame movement, correct speed; saved delay="+emitter.launchDelay+", speed="+emitter.travelSpeed+"; Inspector: "+AnimationUtility.CalculateTransformPath(emitter.transform,null);
 }
}
