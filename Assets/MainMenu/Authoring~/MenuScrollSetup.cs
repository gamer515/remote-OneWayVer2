using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class MenuScrollSetup
{
    public static string Apply()
    {
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(Application.isPlaying || scene.path!="Assets/Scenes/MainMenuScene.unity")throw new Exception("MainMenuScene edit mode required");
        var root=GameObject.Find("Menu Coast");var scroll=root.GetComponent<MainMenuCoastScroll>();if(!scroll)scroll=Undo.AddComponent<MainMenuCoastScroll>(root);
        Undo.RecordObject(scroll,"Connect menu background scrolling");
        scroll.groundSurfaces=new[]{root.transform.Find("Land").GetComponent<MeshFilter>(),root.transform.Find("Walking Path").GetComponent<MeshFilter>()};
        scroll.waterRenderer=root.transform.Find("Sea").GetComponent<Renderer>();scroll.scrollSpeed=1.5f;scroll.waterDirection=.25f;
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        return "Connected land, path, ocean; speed=1.5; player remains fixed";
    }
    public static string Check()
    {
        if(!Application.isPlaying)throw new Exception("Play mode required");
        var scroll=GameObject.Find("Menu Coast").GetComponent<MainMenuCoastScroll>();
        var player=GameObject.Find("Menu Coast/Menu Player").transform;
        var position=player.position;var before=scroll.groundSurfaces[0].sharedMesh.uv;
        typeof(MainMenuCoastScroll).GetField("distance",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(scroll,2.0);
        scroll.SendMessage("Update");
        var after=scroll.groundSurfaces[0].sharedMesh.uv;bool moved=false;
        for(int i=0;i<after.Length;i++)if((after[i]-before[i]).sqrMagnitude>.01f)moved=true;
        return "Ground scroll="+moved+"; player fixed="+(position==player.position)+"; water speed="+scroll.waterRenderer.sharedMaterial.GetFloat("_SurfaceFoamSpeed")+"; direction="+scroll.waterDirection;
    }
}
