using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
public static class ReviewB2Skill03
{
 public static object Run()
 {
  var paths=new[]{"B2_Skill03_FromPlayer"}.Select(n=>"Assets/3DBattle/Boss2/BossAnim/Crisp/"+n+".anim").ToArray();
  var atlas=new Texture2D(1200,240,TextureFormat.RGB24,false);var rt=new RenderTexture(200,240,24);var tile=new Texture2D(200,240,TextureFormat.RGB24,false);
  var cameraGo=new GameObject("Motion inspection camera");var camera=cameraGo.AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.14f,.17f);camera.orthographic=true;camera.targetTexture=rt;camera.cullingMask=1<<30;
  var mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));mat.color=new Color(.7f,.72f,.78f);
  var result=new List<object>();var old=RenderTexture.active;
  try
  {
   for(int row=0;row<paths.Length;row++)
   {
    var model=UnityEngine.Object.Instantiate(UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(a=>a.name.Contains("Heavy_Hammer")&&a.gameObject.activeInHierarchy).gameObject);
    model.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);model.transform.localScale=Vector3.one;var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(paths[row]);
    var baked=new List<Mesh>();
    try
    {
     foreach(var t in model.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
     foreach(var a in model.GetComponentsInChildren<Animator>())a.enabled=false;
     var skins=model.GetComponentsInChildren<SkinnedMeshRenderer>();
     var outputs=new List<Mesh>();foreach(var skin in skins){var go=new GameObject("Review mesh");go.layer=30;go.transform.SetParent(skin.transform,false);go.transform.localScale=Vector3.one/skin.transform.lossyScale.x;var mesh=new Mesh();baked.Add(mesh);outputs.Add(mesh);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=mat;skin.enabled=false;}
     var hand=model.GetComponentsInChildren<Transform>().Single(t=>t.name=="RightHand"||t.name=="mixamorig:RightHand");
     var hips=model.GetComponentsInChildren<Transform>().Single(t=>t.name=="Hips"||t.name=="mixamorig:Hips");
     var frames=new List<object>();Vector3 previous=Vector3.zero;float maxSpeed=0;var speeds=new List<float>();
     int end=Mathf.RoundToInt(clip.length*30);
     for(int f=0;f<=end;f++){clip.SampleAnimation(model,f/30f);var pos=model.transform.InverseTransformPoint(hand.position);float speed=f==0?0:Vector3.Distance(previous,pos)*30;maxSpeed=Mathf.Max(maxSpeed,speed);speeds.Add(speed);previous=pos;if(f%5==0)frames.Add(new{f,speed=Math.Round(speed,2),y=Math.Round(pos.y,2)});}
     clip.SampleAnimation(model,0);for(int i=0;i<skins.Length;i++)skins[i].BakeMesh(outputs[i]);
     Bounds bounds=model.GetComponentsInChildren<MeshRenderer>().First().bounds;foreach(var r in model.GetComponentsInChildren<MeshRenderer>())bounds.Encapsulate(r.bounds);
     camera.nearClipPlane=.01f;camera.farClipPlane=100;camera.orthographicSize=1.6f;camera.transform.position=new Vector3(0,1,6);camera.transform.LookAt(new Vector3(0,1,0));
     for(int col=0;col<6;col++){float f=new[]{0f,9f,11f,14f,24f,44f}[col];clip.SampleAnimation(model,f/30f);for(int i=0;i<skins.Length;i++)skins[i].BakeMesh(outputs[i]);camera.Render();RenderTexture.active=rt;tile.ReadPixels(new Rect(0,0,200,240),0,0);tile.Apply();atlas.SetPixels(col*200,0,200,240,tile.GetPixels());}
     result.Add(new{row,clip=clip.name,end,maxSpeed,frames});
    }
    finally{UnityEngine.Object.DestroyImmediate(model);foreach(var m in baked)UnityEngine.Object.DestroyImmediate(m);}
   }
   atlas.Apply();System.IO.File.WriteAllBytes("Assets/3DBattle/Authoring~/B2Skill03Sheet.png",atlas.EncodeToPNG());return result;
  }
  finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(cameraGo);UnityEngine.Object.DestroyImmediate(mat);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tile);UnityEngine.Object.DestroyImmediate(atlas);}
 }
}






