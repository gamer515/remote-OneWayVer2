using System;using System.Linq;using System.IO;using UnityEngine;using UnityEditor;using UnityEditor.SceneManagement;using OneWay.Battle3D;
public static class UpdateB2Cathedral{
 public static string Run(){
 if(Application.isPlaying)throw new Exception("Edit mode required");
 var b2=UnityEngine.Object.FindObjectsByType<BattleStage>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(s=>s.name=="B2");
 if(b2.gameObject.scene.path!="Assets/Scenes/3DBattleScene.unity")throw new Exception("Wrong scene");
 var root=b2.transform.Find("Cathedral Stage");var walls=b2.boundary.walls;var before=walls.Select(w=>w.bounds).ToArray();
 var boundary=root.Find("Transparent Arena Boundary");if(boundary!=null)Undo.SetTransformParent(boundary,b2.transform,"Keep arena boundary independent");
 Undo.RecordObject(root,"Double cathedral dimensions");root.localScale*=2;
 string folder="Assets/3DBattle/Boss2/Cathedral/";string[] mats={"Cathedral Front","Cathedral Left","Cathedral Entrance","Cathedral Vault","Cathedral Right"};
 for(int i=0;i<5;i++){
  var path=Directory.GetFiles(folder,"*-"+(i+1)+"-removebg-preview.png").Single().Replace('\\','/');
  AssetDatabase.ImportAsset(path);var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.alphaIsTransparency=true;importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
  var mat=AssetDatabase.LoadAssetAtPath<Material>(folder+"StageAssets/"+mats[i]+".mat");Undo.RecordObject(mat,"Use background removed cathedral texture");mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(path));mat.SetFloat("_AlphaClip",1);mat.SetFloat("_Cutoff",.3f);mat.EnableKeyword("_ALPHATEST_ON");mat.SetOverrideTag("RenderType","TransparentCutout");mat.renderQueue=2450;EditorUtility.SetDirty(mat);
 }
 for(int i=0;i<walls.Length;i++)if(Vector3.Distance(before[i].center,walls[i].bounds.center)>.001f||Vector3.Distance(before[i].size,walls[i].bounds.size)>.001f)throw new Exception("Boundary changed");
 AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(b2.gameObject.scene);EditorSceneManager.SaveScene(b2.gameObject.scene);Selection.activeGameObject=root.gameObject;
 return "PASS: five removebg textures, alpha clipping, cathedral scale="+root.localScale+", all four boundary world bounds unchanged; scene saved.";
 }
}
