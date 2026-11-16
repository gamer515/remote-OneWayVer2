using System;using UnityEngine;using UnityEditor;
public static class MakeGoldenSlashStreaks{
public static string Run(){if(Application.isPlaying)throw new Exception("Edit mode required");string path="Assets/3DBattle/Boss2/B2WhiteSlash.prefab";var root=PrefabUtility.LoadPrefabContents(path);
try{var ps=root.GetComponentInChildren<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var main=ps.main;main.startSize=new ParticleSystem.MinMaxCurve(.006f,.013f);main.startLifetime=new ParticleSystem.MinMaxCurve(.35f,.65f);main.startSpeed=0;main.startRotation=0;
var shape=ps.shape;shape.randomDirectionAmount=0;
var velocity=ps.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.Local;velocity.x=0;velocity.y=0;velocity.z=-.22f;
var noise=ps.noise;noise.enabled=false;var rotation=ps.rotationOverLifetime;rotation.enabled=false;
var emission=ps.emission;emission.rateOverTime=100;emission.SetBursts(new[]{new ParticleSystem.Burst(0,22)});
var size=ps.sizeOverLifetime;size.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,.4f),new Keyframe(.15f,1),new Keyframe(1,.1f)));
var color=ps.colorOverLifetime;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(new Color(1,.95f,.65f),0),new GradientColorKey(new Color(1,.85f,.35f),1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.75f,.1f),new GradientAlphaKey(.5f,.55f),new GradientAlphaKey(0,1)});color.color=gradient;
var r=ps.GetComponent<ParticleSystemRenderer>();r.renderMode=ParticleSystemRenderMode.Stretch;r.lengthScale=18;r.velocityScale=.2f;r.cameraVelocityScale=0;r.freeformStretching=true;r.rotateWithStretchDirection=true;
PrefabUtility.SaveAsPrefabAsset(root,path);AssetDatabase.SaveAssets();return "Thin gold streaks: width .006-.013 relative to slash, length18x, local backward flow, noise disabled, fading .35-.65s.";
}finally{PrefabUtility.UnloadPrefabContents(root);}}
}
