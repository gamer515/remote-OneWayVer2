using System;using System.Linq;using UnityEngine;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine.Rendering;using UnityEngine.Rendering.Universal;using OneWay.Battle3D;
public static class MakeB2GoldenSlash{
public static string Run(){if(Application.isPlaying)throw new Exception("Edit mode required");
 var b2=UnityEngine.Object.FindObjectsByType<BattleStage>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(s=>s.name=="B2");if(b2.gameObject.scene.path!="Assets/Scenes/3DBattleScene.unity")throw new Exception("Wrong scene");
 AssetDatabase.ImportAsset("Assets/3DBattle/Boss2/B2GoldenSlash.shader",ImportAssetOptions.ForceSynchronousImport);var shader=Shader.Find("OneWay/B2 Golden Slash");if(shader==null||ShaderUtil.ShaderHasError(shader))throw new Exception("Shader error");
 var mat=AssetDatabase.LoadAssetAtPath<Material>("Assets/3DBattle/Boss2/B2SlashWhite.mat");Undo.RecordObject(mat,"Golden slash glow");mat.shader=shader;mat.SetColor("_CoreColor",new Color(10,8,3,1));mat.SetColor("_EdgeColor",new Color(6,2.5f,.15f,1));EditorUtility.SetDirty(mat);
 var profile=ScriptableObject.CreateInstance<VolumeProfile>();profile.name="B2 Golden Slash Bloom";var bloom=profile.Add<Bloom>(true);bloom.threshold.Override(2);bloom.intensity.Override(1.3f);bloom.scatter.Override(.75f);bloom.tint.Override(new Color(1,.8f,.4f));
 string path="Assets/3DBattle/Boss2/B2GoldenSlashBloom.asset";AssetDatabase.CreateAsset(profile,path);AssetDatabase.AddObjectToAsset(bloom,profile);
 var go=new GameObject("B2 Golden Slash Bloom");go.transform.SetParent(b2.transform,false);var volume=go.AddComponent<Volume>();volume.isGlobal=true;volume.priority=10;volume.sharedProfile=profile;Undo.RegisterCreatedObjectUndo(go,"B2 slash bloom");
 var cam=Camera.main;var data=cam.GetUniversalAdditionalCameraData();Undo.RecordObject(data,"Enable slash glow");data.renderPostProcessing=true;data.volumeLayerMask|=1<<go.layer;cam.allowHDR=true;EditorUtility.SetDirty(data);
 AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(b2.gameObject.scene);EditorSceneManager.SaveScene(b2.gameObject.scene);
 return "Golden HDR core and edges applied; B2-only Bloom volume, camera postprocessing enabled; pipeline HDR="+((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline).supportsHDR;
}}
