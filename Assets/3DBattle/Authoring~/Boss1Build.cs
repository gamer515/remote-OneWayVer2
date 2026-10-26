using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;
public static class Boss1Build {
 const string Folder="Assets/3DBattle/Boss1/";
 public static string Run(){
 if(EditorApplication.isPlaying)throw new Exception("Stop Play Mode");
 var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();if(scene.path!="Assets/Scenes/3DBattleScene.unity")throw new Exception("Wrong scene");
 var paths=Directory.GetFiles(Folder,"*.fbx",SearchOption.AllDirectories).Select(p=>p.Replace('\\','/')).OrderBy(p=>int.Parse(Path.GetFileName(Path.GetDirectoryName(p)).Split('_').Last())).ToArray();
 if(paths.Length!=15)throw new Exception("Expected 15 FBXs");
 var clips=paths.Select(p=>AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview__"))).ToArray();
 var basePath=paths.Single(p=>p.Contains("Axe_Breathe"));
 var probe=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(basePath));probe.hideFlags=HideFlags.HideAndDontSave;
 var report=new List<string>();
 try{
 foreach(var clip in clips){
 var missing=AnimationUtility.GetCurveBindings(clip).Where(b=>b.path!=""&&probe.transform.Find(b.path)==null).ToArray();if(missing.Length>0)throw new Exception("Unbound clip "+clip.name);
 var parts=probe.GetComponentsInChildren<Transform>();clip.SampleAnimation(probe,0);var first=parts.Select(t=>t.localRotation).ToArray();int changes=0;
 for(int f=1;f<=8;f++){clip.SampleAnimation(probe,clip.length*f/8f);changes+=parts.Where((t,i)=>Quaternion.Angle(first[i],t.localRotation)>.1f).Count();}
 if(changes==0)throw new Exception("Static clip: "+clip.name);
 report.Add(clip.name+": movement verified, all paths bound");
 }
 }finally{UnityEngine.Object.DestroyImmediate(probe);}
 var matPath=Folder+"Boss1Surface.mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(matPath);
 if(mat==null){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,matPath);}
 string textures=Path.GetDirectoryName(basePath).Replace('\\','/')+"/Meshy_AI_boss_character_model_biped_texture_0";
 mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(textures+".png"));mat.SetColor("_BaseColor",Color.white);mat.SetFloat("_Smoothness",.25f);
 var normalImporter=(TextureImporter)AssetImporter.GetAtPath(textures+"_normal.png");if(normalImporter.textureType!=TextureImporterType.NormalMap){normalImporter.textureType=TextureImporterType.NormalMap;normalImporter.SaveAndReimport();}
 mat.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(textures+"_normal.png"));mat.EnableKeyword("_NORMALMAP");EditorUtility.SetDirty(mat);AssetDatabase.SaveAssetIfDirty(mat);
 foreach(var path in paths){
 var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);var names=model.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Select(m=>m.name).Distinct().ToArray();
 var importer=(ModelImporter)AssetImporter.GetAtPath(path);if(importer.GetExternalObjectMap().Values.Contains(mat))continue;foreach(var name in names)importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),name),mat);importer.SaveAndReimport();
 }
 clips=paths.Select(p=>AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview__"))).ToArray();
 string controllerPath=Folder+"Boss1Animations.controller";
 var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath)??AnimatorController.CreateAnimatorControllerAtPath(controllerPath);var machine=controller.layers[0].stateMachine;
 foreach(var clip in clips){string name=clip.name.Split('|').Last();var state=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name==name)??machine.AddState(name);state.motion=clip;state.speed=1;if(name=="Axe_Breathe_and_Look_Around")machine.defaultState=state;EditorUtility.SetDirty(state);}
 EditorUtility.SetDirty(controller);AssetDatabase.SaveAssetIfDirty(controller);
 var boss=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(basePath));
 try{
 boss.name="Boss1_AllAnimations";boss.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
 var animator=boss.GetComponent<Animator>();if(animator==null)animator=boss.AddComponent<Animator>();animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
 foreach(var r in boss.GetComponentsInChildren<SkinnedMeshRenderer>()){r.sharedMaterials=r.sharedMaterials.Select(_=>mat).ToArray();r.updateWhenOffscreen=true;}
 var debug=boss.AddComponent<MeshyAnimationDebugger>();debug.clips=clips;debug.playOnEnable=true;debug.autoAdvance=true;debug.loopPlaylist=true;
 PrefabUtility.SaveAsPrefabAsset(boss,Folder+"Boss1_AllAnimations.prefab");
 }finally{UnityEngine.Object.DestroyImmediate(boss);}
 var instance=GameObject.Find("Boss1_AllAnimations");if(instance==null){instance=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"Boss1_AllAnimations.prefab"));Undo.RegisterCreatedObjectUndo(instance,"Add Boss1");instance.transform.position=new Vector3(8,0,4);}
 EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);Selection.activeGameObject=instance;if(SceneView.lastActiveSceneView!=null)SceneView.lastActiveSceneView.FrameSelected();
 File.WriteAllLines("Assets/3DBattle/Authoring~/Boss1Validation.txt",report);
 return "Created Boss1 prefab + controller; 15 models material remapped; 15/15 clips move on shared rig.";
 }
}

