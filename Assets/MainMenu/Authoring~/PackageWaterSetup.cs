using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering.Universal;
public static class PackageWaterSetup
{
    public static string Apply()
    {
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(Application.isPlaying || scene.path!="Assets/Scenes/MainMenuScene.unity") throw new Exception("MainMenuScene edit mode required");
        const string source="Assets/UnrealtechnologDev AS Publisher/Simple Water Shader Package/WaterShader/Water.mat";
        const string target="Assets/MainMenu/Coast/Menu Package Water.mat";
        var original=AssetDatabase.LoadAssetAtPath<Material>(source);
        if(original==null || ShaderUtil.ShaderHasError(original.shader)) throw new Exception("Package water material missing or shader errors");
        var mat=AssetDatabase.LoadAssetAtPath<Material>(target);
        if(mat==null) { mat=new Material(original);mat.name="Menu Package Water";AssetDatabase.CreateAsset(mat,target); }
        var sea=GameObject.Find("Menu Coast").transform.Find("Sea");
        var renderer=sea.GetComponent<Renderer>();Undo.RecordObject(renderer,"Use package water");renderer.sharedMaterial=mat;
        var mesh=sea.GetComponent<MeshFilter>().sharedMesh;Undo.RecordObject(mesh,"Water shader UV coordinates");
        var vertices=mesh.vertices;var uv=new Vector2[vertices.Length];
        for(int i=0;i<vertices.Length;i++) uv[i]=new Vector2(vertices[i].x/20f,vertices[i].z/20f);
        mesh.uv=uv;mesh.RecalculateNormals();mesh.RecalculateTangents();EditorUtility.SetDirty(mesh);AssetDatabase.SaveAssetIfDirty(mesh);
        Undo.RecordObject(sea,"Package water height");sea.localPosition=new Vector3(0,0,0);
        var root=GameObject.Find("Menu Coast").transform;
        var sun=root.Find("Menu Water Sun");
        if(sun==null){var obj=new GameObject("Menu Water Sun");Undo.RegisterCreatedObjectUndo(obj,"Light package water");obj.transform.SetParent(root,false);sun=obj.transform;var light=obj.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.2f;light.color=new Color(1,.97f,.9f);light.shadows=LightShadows.None;sun.rotation=Quaternion.Euler(50,-30,0);}
        var cam=Camera.main;var data=cam.GetComponent<UniversalAdditionalCameraData>();
        if(data==null)data=Undo.AddComponent<UniversalAdditionalCameraData>(cam.gameObject);
        Undo.RecordObject(data,"Enable water depth on menu camera");data.requiresDepthOption=CameraOverrideOption.On;data.requiresColorOption=CameraOverrideOption.On;
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        return "Assigned "+AssetDatabase.GetAssetPath(mat)+"; shader="+mat.shader.name+"; supported="+mat.shader.isSupported+"; errors="+ShaderUtil.ShaderHasError(mat.shader);
    }
}

