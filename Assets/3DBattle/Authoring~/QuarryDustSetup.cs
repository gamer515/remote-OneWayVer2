using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class QuarryDustSetup
{
    const string Folder="Assets/3DBattle/Effects/Dust/";
    static ParticleSystem Layer(GameObject root,string name,Material mat,int kind,bool loop)
    {
        var go=new GameObject(name); go.transform.SetParent(root.transform,false);
        go.transform.localPosition=new Vector3(0,kind==0?.3f:.15f,0);
        go.transform.localRotation=Quaternion.Euler(-90,0,0);
        var ps=go.AddComponent<ParticleSystem>(); ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.useAutoRandomSeed=false; ps.randomSeed=(uint)(517+kind*87);
        var main=ps.main; main.duration=4; main.loop=loop; main.playOnAwake=true;
        main.simulationSpace=ParticleSystemSimulationSpace.World; main.scalingMode=ParticleSystemScalingMode.Hierarchy;
        main.maxParticles=kind==2?100:70;
        main.startLifetime=new ParticleSystem.MinMaxCurve(kind==2?.7f:1.5f,kind==2?1.5f:2.7f);
        main.startSize=new ParticleSystem.MinMaxCurve(kind==2?.035f:.65f,kind==2?.12f:1.25f);
        main.startSpeed=new ParticleSystem.MinMaxCurve(kind==0?2f:kind==1?2.8f:1f,kind==0?4f:kind==1?4f:4.5f);
        main.startRotation=new ParticleSystem.MinMaxCurve(0,Mathf.PI*2);
        main.startColor=new ParticleSystem.MinMaxGradient(new Color(.57f,.44f,.30f,.65f),new Color(.80f,.66f,.48f,.9f));
        main.gravityModifier=kind==2?.45f:0;
        var em=ps.emission; em.rateOverTime=0;
        em.SetBursts(new[]{new ParticleSystem.Burst(0,(short)(kind==0?22:kind==1?18:45))});
        var shape=ps.shape; shape.enabled=true; shape.shapeType=ParticleSystemShapeType.Cone;
        shape.angle=kind==0?82:kind==1?18:48; shape.radius=kind==0?.25f:.16f;
        var limit=ps.limitVelocityOverLifetime; limit.enabled=kind!=2; limit.limit=kind==1?1.8f:1.2f; limit.dampen=.12f;
        var noise=ps.noise; noise.enabled=kind!=2; noise.strength=.25f; noise.frequency=.8f; noise.scrollSpeed=.35f;
        var size=ps.sizeOverLifetime; size.enabled=true;
        size.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,.35f),new Keyframe(.25f,.85f),new Keyframe(1,kind==2?.35f:2.1f)));
        var color=ps.colorOverLifetime; color.enabled=true;
        var gradient=new Gradient(); gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(.85f,.82f,.78f),1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(kind==2?1:.85f,.08f),new GradientAlphaKey(.55f,.45f),new GradientAlphaKey(0,1)});
        color.color=gradient;
        var rotation=ps.rotationOverLifetime; rotation.enabled=true; rotation.z=new ParticleSystem.MinMaxCurve(-.5f,.5f);
        var renderer=go.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial=mat;
        renderer.renderMode=ParticleSystemRenderMode.Billboard; renderer.sortMode=ParticleSystemSortMode.Distance;
        renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
        renderer.SetActiveVertexStreams(new System.Collections.Generic.List<ParticleSystemVertexStream>{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.Normal,ParticleSystemVertexStream.Color,ParticleSystemVertexStream.UV});
        return ps;
    }
    public static string Run()
    {
        if(Application.isPlaying) throw new Exception("Exit play mode first");
        AssetDatabase.Refresh();
        var importer=(TextureImporter)AssetImporter.GetAtPath(Folder+"smoke_test_two.png");
        importer.textureType=TextureImporterType.Default; importer.alphaSource=TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency=true; importer.wrapMode=TextureWrapMode.Clamp;
        importer.mipmapEnabled=true; importer.textureCompression=TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
        var shader=Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if(shader==null || !shader.isSupported) throw new Exception("URP particle shader unavailable");
        var mat=AssetDatabase.LoadAssetAtPath<Material>(Folder+"QuarryDust.mat");
        if(mat==null){mat=new Material(shader); AssetDatabase.CreateAsset(mat,Folder+"QuarryDust.mat");}
        mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"smoke_test_two.png"));
        mat.SetColor("_BaseColor",Color.white); mat.SetFloat("_Surface",1); mat.SetFloat("_Blend",0);
        mat.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha); mat.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_SrcBlendAlpha",1); mat.SetFloat("_DstBlendAlpha",(float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite",0); mat.SetFloat("_Cull",0); mat.SetFloat("_ColorMode",0);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); mat.SetOverrideTag("RenderType","Transparent"); mat.renderQueue=3000;
        EditorUtility.SetDirty(mat);
        var preview=EditorSceneManager.NewPreviewScene();
        try
        {
            foreach(bool loop in new[]{false,true})
            {
                var root=new GameObject(loop?"QuarryDust_LoopPreview":"QuarryDust_Burst");
                root.transform.localScale=Vector3.one*1.3f;
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,preview);
                Layer(root,"Ground Spread",mat,0,loop); Layer(root,"Rising Plume",mat,1,loop); Layer(root,"Fine Debris",mat,2,loop);
                PrefabUtility.SaveAsPrefabAsset(root,Folder+root.name+".prefab");
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
        finally{EditorSceneManager.ClosePreviewScene(preview);}
        AssetDatabase.SaveAssets();
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"QuarryDust_Burst.prefab");
        Selection.activeObject=prefab; EditorGUIUtility.PingObject(prefab);
        return "Created two URP dust prefabs, material and supplied alpha texture. No battle scene changes.";
    }
    public static string Preview()
    {
        var scene=EditorSceneManager.NewPreviewScene();
        RenderTexture rt=null; Texture2D image=null;
        try
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"QuarryDust_Burst.prefab");
            var root=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
            var systems=root.GetComponentsInChildren<ParticleSystem>();
            foreach(var ps in systems) ps.Simulate(.7f,false,true,true);
            int live=systems.Sum(p=>p.particleCount); if(live==0)throw new Exception("No dust emitted");
            var go=new GameObject("Preview Camera"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
            var camera=go.AddComponent<Camera>(); camera.scene=scene;
            camera.transform.position=new Vector3(4,3,-6); camera.transform.LookAt(new Vector3(0,1.1f,0));
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.12f,.15f,.19f);
            camera.fieldOfView=40; camera.nearClipPlane=.05f;
            rt=new RenderTexture(960,720,24); camera.targetTexture=rt; camera.Render();
            var previous=RenderTexture.active; RenderTexture.active=rt;
            image=new Texture2D(960,720,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,960,720),0,0); image.Apply(); RenderTexture.active=previous;
            File.WriteAllBytes("Assets/3DBattle/Authoring~/QuarryDustPreview.png",image.EncodeToPNG());
            foreach(var ps in systems) ps.Simulate(5,false,true,true);
            if(systems.Any(p=>p.particleCount!=0)) throw new Exception("Burst did not finish");
            return "PASS: "+live+" particles at 0.7s; all expired by 5s. Preview rendered.";
        }
        finally
        {
            if(image!=null)UnityEngine.Object.DestroyImmediate(image);
            if(rt!=null){rt.Release(); UnityEngine.Object.DestroyImmediate(rt);}
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
