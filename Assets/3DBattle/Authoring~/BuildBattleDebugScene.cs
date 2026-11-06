using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;
public static class BuildBattleDebugScene {
 public static string Run(){
 if(Application.isPlaying)throw new Exception("Edit mode required");
 var source=SceneManager.GetSceneByPath("Assets/Scenes/3DBattleScene.unity");var target=SceneManager.GetSceneByPath("Assets/Scenes/3DBattleDebugScene.unity");
 if(!source.isLoaded||!target.isLoaded)throw new Exception("Both scenes must be loaded");
 if(target.GetRootGameObjects().Any(g=>g.GetComponent<BattleDebugBootstrap>()!=null))throw new Exception("Debug setup already exists");
 var original=source.GetRootGameObjects().Select(g=>g.GetComponent<Battle3DSceneController>()).First(b=>b!=null);
 var root=UnityEngine.Object.Instantiate(original.gameObject);root.name="Battle3D Debug";SceneManager.MoveGameObjectToScene(root,target);
 var battle=root.GetComponent<Battle3DSceneController>();battle.enabled=false;
 UnityEngine.Object.DestroyImmediate(root.GetComponent<BattleIntroWalkDebug>());
 var b1=battle.stages.First(s=>s.StageIndex==1);
 foreach(var stage in battle.stages.Where(s=>s!=b1).ToArray())UnityEngine.Object.DestroyImmediate(stage.gameObject);
 battle.stages=new[]{b1};b1.gameObject.SetActive(true);
 foreach(string name in new[]{"BackgroundCylinder","Arena Outer Rocks"}){var t=b1.transform.Find(name);if(t!=null)UnityEngine.Object.DestroyImmediate(t.gameObject);}
 var hud=root.transform.Find("Player Health HUD");if(hud!=null)UnityEngine.Object.DestroyImmediate(hud.gameObject);
 var cameraRoot=UnityEngine.Object.Instantiate(original.followCamera.gameObject);cameraRoot.name="Main Camera";SceneManager.MoveGameObjectToScene(cameraRoot,target);
 foreach(var old in target.GetRootGameObjects().Where(g=>g!=cameraRoot&&g.GetComponent<Camera>()!=null).ToArray())Undo.DestroyObjectImmediate(old);
 var follow=cameraRoot.GetComponent<BattleFollowCamera>();follow.target=battle.player.transform;follow.offset=new Vector3(0,3,-9.54f);follow.lookOffset=new Vector3(0,1,0);
 follow.ignoredColliders=b1.boundary.walls.Cast<Collider>().ToArray();battle.followCamera=follow;battle.player.viewCamera=cameraRoot.GetComponent<Camera>();
 cameraRoot.GetComponent<Camera>().clearFlags=CameraClearFlags.SolidColor;cameraRoot.GetComponent<Camera>().backgroundColor=new Color(.14f,.16f,.19f);
 foreach(var wall in b1.boundary.walls){var r=wall.GetComponent<Renderer>();if(r!=null)r.enabled=false;wall.enabled=true;}
 var floor=b1.transform.Find("Arena").GetComponent<Renderer>();string matPath="Assets/3DBattle/3DBattleDebug/DebugFloor.mat";
 var mat=AssetDatabase.LoadAssetAtPath<Material>(matPath);if(mat==null){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));mat.SetColor("_BaseColor",new Color(.32f,.34f,.37f));mat.SetFloat("_Smoothness",0);AssetDatabase.CreateAsset(mat,matPath);}floor.sharedMaterial=mat;
 var bootstrap=root.AddComponent<BattleDebugBootstrap>();bootstrap.battle=battle;
 battle.SelectStage(1);
 // Keep the initial player capsule inside the copied left boundary.
 var body=battle.player.GetComponent<BattleArenaBody>();if(body!=null){var position=body.Constrain(battle.player.transform.position);b1.playerSpawn.position=position;battle.player.transform.position=position;}
 follow.Snap();
 var external=new System.Collections.Generic.List<string>();
 foreach(var c in root.GetComponentsInChildren<Component>(true).Concat(cameraRoot.GetComponents<Component>())){if(c==null)throw new Exception("Missing component");var so=new SerializedObject(c);var p=so.GetIterator();while(p.Next(true)){if(p.propertyType!=SerializedPropertyType.ObjectReference)continue;var value=p.objectReferenceValue;if(value is Component other && other.gameObject.scene.IsValid() && other.gameObject.scene!=target)external.Add(c.name+"."+p.propertyPath);if(value is GameObject go && go.scene.IsValid()&&go.scene!=target)external.Add(c.name+"."+p.propertyPath);}}
 if(external.Count>0)throw new Exception("External scene refs: "+string.Join(",",external));
 EditorSceneManager.MarkSceneDirty(target);EditorSceneManager.SaveScene(target);EditorSceneManager.CloseScene(source,true);SceneManager.SetActiveScene(target);Selection.activeGameObject=root;
 return "Debug scene saved: B1/player/camera/light/plain floor/4 invisible walls; no intro/background/HUD/other bosses; no external scene refs; source scene not saved or modified.";
 }
}
