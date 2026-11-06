using System;using UnityEngine;using UnityEditor;
public static class AddGoldenSlashSmoke{
public static string Run(){if(Application.isPlaying)throw new Exception("Edit mode required");const string dir="Assets/3DBattle/Boss2/";
var mat=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/3DBattle/Effects/Dust/QuarryDust.mat"));mat.name="B2 Golden Smoke";mat.SetColor("_BaseColor",new Color(2.8f,1.25f,.16f,1));AssetDatabase.CreateAsset(mat,dir+"B2GoldenSmoke.mat");
var root=PrefabUtility.LoadPrefabContents(dir+"B2WhiteSlash.prefab");
try{if(root.transform.Find("Golden Smoke")!=null)throw new Exception("Smoke already exists");var go=new GameObject("Golden Smoke");go.transform.SetParent(root.transform,false);var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
var main=ps.main;main.duration=3;main.loop=true;main.playOnAwake=true;main.simulationSpace=ParticleSystemSimulationSpace.World;main.scalingMode=ParticleSystemScalingMode.Hierarchy;main.startLifetime=new ParticleSystem.MinMaxCurve(.6f,.95f);main.startSize=new ParticleSystem.MinMaxCurve(.10f,.23f);main.startSpeed=new ParticleSystem.MinMaxCurve(.015f,.045f);main.startRotation=new ParticleSystem.MinMaxCurve(0,Mathf.PI*2);main.startColor=Color.white;main.maxParticles=350;main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
var shape=ps.shape;shape.enabled=true;shape.shapeType=ParticleSystemShapeType.Mesh;shape.mesh=root.GetComponent<MeshFilter>().sharedMesh;shape.meshShapeType=ParticleSystemMeshShapeType.Triangle;shape.normalOffset=.002f;shape.randomDirectionAmount=.7f;
var emission=ps.emission;emission.rateOverTime=160;emission.SetBursts(new[]{new ParticleSystem.Burst(0,35)});
var color=ps.colorOverLifetime;color.enabled=true;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(new Color(1,.83f,.45f),0),new GradientColorKey(new Color(1,.58f,.12f),1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.45f,.12f),new GradientAlphaKey(.28f,.55f),new GradientAlphaKey(0,1)});color.color=gradient;
var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,.4f),new Keyframe(.35f,1),new Keyframe(1,1.7f)));
var noise=ps.noise;noise.enabled=true;noise.strength=.035f;noise.frequency=3;noise.scrollSpeed=.4f;noise.quality=ParticleSystemNoiseQuality.Medium;
var rotation=ps.rotationOverLifetime;rotation.enabled=true;rotation.z=new ParticleSystem.MinMaxCurve(-.5f,.5f);
var r=ps.GetComponent<ParticleSystemRenderer>();r.sharedMaterial=mat;r.renderMode=ParticleSystemRenderMode.Billboard;r.sortMode=ParticleSystemSortMode.Distance;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;r.receiveShadows=false;r.minParticleSize=0;r.maxParticleSize=.3f;
PrefabUtility.SaveAsPrefabAsset(root,dir+"B2WhiteSlash.prefab");AssetDatabase.SaveAssets();return "Golden world-space smoke added along crescent surface; 160/s, 0.6-0.95s fade, inherits slash scale.";
}finally{PrefabUtility.UnloadPrefabContents(root);}}
}
