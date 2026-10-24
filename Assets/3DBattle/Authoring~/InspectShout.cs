using System.Linq;
using UnityEngine;
using UnityEditor;
public static class InspectShout {
 public static string Run(){ foreach(var p in AssetDatabase.FindAssets("t:Model",new[]{"Assets/3DBattle/Boss1"}).Select(AssetDatabase.GUIDToAssetPath).Where(p=>p.Contains("Animation_"))) Render(p); return "All previews rendered"; } public static string Render(string path){
 
 var clip=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
 var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
 var g=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path)); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(g,scene);
 var cg=new GameObject("Review Camera"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cg,scene); var cam=cg.AddComponent<Camera>(); cam.scene=scene; cam.orthographic=true;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.15f,.15f,.15f);cam.nearClipPlane=.01f;cam.farClipPlane=100;
 var lg=new GameObject("Review Light");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lg,scene); var light=lg.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.4f;lg.transform.rotation=Quaternion.Euler(30,-30,0);
 var rt=new RenderTexture(360,360,24);var sheet=new Texture2D(1440,720,TextureFormat.RGB24,false);var tile=new Texture2D(360,360,TextureFormat.RGB24,false);var old=RenderTexture.active;
 try {for(int n=0;n<8;n++){clip.SampleAnimation(g,clip.length*n/8f);var baked=new System.Collections.Generic.List<GameObject>(); foreach(var skin in g.GetComponentsInChildren<SkinnedMeshRenderer>()){var mesh=new Mesh();skin.BakeMesh(mesh);var obj=new GameObject("baked");obj.transform.SetParent(skin.transform,false);obj.AddComponent<MeshFilter>().sharedMesh=mesh;obj.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;skin.enabled=false;baked.Add(obj);} var rs=g.GetComponentsInChildren<Renderer>().Where(r=>r.enabled).ToArray();var bounds=rs[0].bounds;foreach(var r in rs)bounds.Encapsulate(r.bounds);float size=bounds.size.magnitude;cam.orthographicSize=size*.48f;cam.transform.position=bounds.center+new Vector3(-.4f,.15f,1).normalized*size*2;cam.transform.LookAt(bounds.center);cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;tile.ReadPixels(new Rect(0,0,360,360),0,0);tile.Apply();sheet.SetPixels(n%4*360,(1-n/4)*360,360,360,tile.GetPixels());foreach(var obj in baked){Object.DestroyImmediate(obj.GetComponent<MeshFilter>().sharedMesh);Object.DestroyImmediate(obj);}}sheet.Apply();System.IO.File.WriteAllBytes("Assets/3DBattle/Authoring~/Review_"+path.Split(new[]{"Animation_"},System.StringSplitOptions.None).Last().Replace(".fbx",".png"),sheet.EncodeToPNG());return "Frames at 0, .75, 1.5, 2.25, 3, 3.75, 4.5, 5.25 seconds";}
 finally{RenderTexture.active=old;Object.DestroyImmediate(rt);Object.DestroyImmediate(tile);Object.DestroyImmediate(sheet);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
 }
}


