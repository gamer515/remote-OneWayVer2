using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using OneWay.Battle3D;

public static class TestB2Skill03SlashDirection
{
 static readonly MethodInfo Late=typeof(B2SwordSlash).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
 static List<GameObject> Effects(B2SwordSlash e)=>(List<GameObject>)typeof(B2SwordSlash).GetField("active",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(e);
 static void RenderComparison(GameObject model,B2SwordSlash emitter,GameObject corrected)
 {
  var oldRT=RenderTexture.active;var meshes=new List<Mesh>();var draws=new List<GameObject>();
  var cameraGo=new GameObject("Skill03 slash review camera");var camera=cameraGo.AddComponent<Camera>();camera.enabled=false;
  var rt=new RenderTexture(640,500,24);var tile=new Texture2D(640,500,TextureFormat.RGB24,false);var atlas=new Texture2D(1280,500,TextureFormat.RGB24,false);
  var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.color=new Color(.7f,.72f,.78f);
  GameObject legacy=null;
  try
  {
   emitter.EmitB2SwordSlash();legacy=Effects(emitter).Last();
   foreach(var g in new[]{model,corrected,legacy})foreach(var t in g.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
   foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
   {
    var mesh=new Mesh();meshes.Add(mesh);skin.BakeMesh(mesh);var draw=new GameObject("Review skin");draw.layer=30;draw.transform.SetParent(skin.transform,false);draw.transform.localScale=Vector3.one/skin.transform.lossyScale.x;
    draw.AddComponent<MeshFilter>().sharedMesh=mesh;draw.AddComponent<MeshRenderer>().sharedMaterial=material;draws.Add(draw);skin.enabled=false;
   }
   camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.10f,.12f,.15f);camera.cullingMask=1<<30;camera.targetTexture=rt;camera.orthographic=true;camera.orthographicSize=11;camera.nearClipPlane=.1f;camera.farClipPlane=150;
   var target=model.transform.position+Vector3.up*4;camera.transform.position=target+new Vector3(12,14,27);camera.transform.LookAt(target);
   for(int i=0;i<2;i++)
   {
    legacy.SetActive(i==0);corrected.SetActive(i==1);var effect=i==0?legacy:corrected;
    foreach(var ps in effect.GetComponentsInChildren<ParticleSystem>()){ps.Simulate(.09f,false,true,true);ps.Pause(false);}
    camera.Render();RenderTexture.active=rt;tile.ReadPixels(new Rect(0,0,640,500),0,0);tile.Apply();atlas.SetPixels(i*640,0,640,500,tile.GetPixels());
   }
   atlas.Apply();System.IO.File.WriteAllBytes("Assets/3DBattle/Authoring~/B2Skill03SlashDirectionReview.png",atlas.EncodeToPNG());
  }
  finally
  {
   if(legacy)UnityEngine.Object.DestroyImmediate(legacy);foreach(var g in draws)UnityEngine.Object.DestroyImmediate(g);foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);
   RenderTexture.active=oldRT;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(cameraGo);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tile);UnityEngine.Object.DestroyImmediate(atlas);UnityEngine.Object.DestroyImmediate(material);
  }
 }
 public static object Run()
 {
  if(!Application.isPlaying)throw new Exception("Play mode required");
  var scene=UnityEngine.Object.FindObjectsByType<B2SwordSlash>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(e=>e.gameObject.activeInHierarchy&&!e.name.Contains("Review"));
  var results=new List<object>();
  foreach(int fps in new[]{30,60,120})foreach(int yaw in new[]{0,90})
  {
   var clone=UnityEngine.Object.Instantiate(scene.boss.gameObject,new Vector3(1000,0,1000),Quaternion.Euler(0,yaw,0));clone.name="Temporary Skill03 direction test";
   var emitter=clone.GetComponentInChildren<B2SwordSlash>();
   try
   {
    foreach(var c in clone.GetComponentsInChildren<Collider>(true))c.enabled=false;
    foreach(var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))if(behaviour!=emitter)behaviour.enabled=false;
    var animator=emitter.GetComponent<Animator>();animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.applyRootMotion=false;animator.fireEvents=true;animator.Rebind();animator.Play("Base Layer.WaitAfterWeaponCombo",0,.9f);animator.Update(0);
    if(!emitter.boss.IsAlive)throw new Exception("Test boss is not alive");
    int seen=0;float pitchError=-1,outwardError=-1,originError=-1,eventFrame=-1;bool rendered=false;
    for(int step=0;step<Mathf.RoundToInt(1.8f*fps);step++)
    {
     animator.Update(1f/fps);Late.Invoke(emitter,null);
     if(emitter.EmittedCount==seen)continue;
     seen=emitter.EmittedCount;
     if(seen!=1+(rendered?1:0))throw new Exception("Unexpected extra effect before next attack");
     if(pitchError>=0)continue;
     var effect=Effects(emitter).Last();var ps=effect.GetComponentInChildren<ParticleSystem>();ps.Simulate(.03f,false,true,true);ps.Pause(false);
     var particles=new ParticleSystem.Particle[8];int count=ps.GetParticles(particles);if(count!=1)throw new Exception("Expected one crescent particle");
     var meshRotation=ps.transform.rotation*Quaternion.Euler(particles[0].rotation3D);
     Vector3 normal=meshRotation*Vector3.forward;Vector3 outward=meshRotation*new Vector3(-1,1,0).normalized;
     pitchError=Mathf.Acos(Mathf.Clamp(Mathf.Abs(Vector3.Dot(normal,Vector3.up)),-1,1))*Mathf.Rad2Deg;
     outwardError=Vector3.Angle(outward,emitter.swordBlade.forward);
     originError=Vector3.Distance(ps.transform.position,emitter.swordGrip.position);
     eventFrame=animator.GetCurrentAnimatorStateInfo(0).normalizedTime*animator.GetCurrentAnimatorClipInfo(0)[0].clip.length*30;
     if(pitchError>2||outwardError>1||originError>.001f)throw new Exception("Slash orientation mismatch: "+pitchError+", "+outwardError+", "+originError);
     if(effect.GetComponent<B2SlashFlight>())throw new Exception("Stationary slash unexpectedly travels");
     if(fps==60&&yaw==0){RenderComparison(clone,emitter,effect);rendered=true;seen=emitter.EmittedCount;}
    }
    if(pitchError<0)throw new Exception("Skill03 did not emit a slash");
    results.Add(new{fps,yaw,eventFrame,planeErrorDegrees=pitchError,bladeDirectionErrorDegrees=outwardError,gripDistance=originError,skill03EffectCount=emitter.EmittedCount-(rendered?1:0)});
   }
   finally{foreach(var effect in Effects(emitter))if(effect)UnityEngine.Object.DestroyImmediate(effect);Effects(emitter).Clear();UnityEngine.Object.DestroyImmediate(clone);}
  }
  return results;
 }
}
