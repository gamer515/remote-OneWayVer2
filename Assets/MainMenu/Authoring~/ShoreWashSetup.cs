using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class ShoreWashSetup
{
    public static string Apply()
    {
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(Application.isPlaying || scene.path!="Assets/Scenes/MainMenuScene.unity")throw new Exception("MainMenuScene edit mode required");
        AssetDatabase.Refresh();
        var root=GameObject.Find("Menu Coast").transform;
        var stones=root.Cast<Transform>().Where(t=>t.name.StartsWith("Coastal Stone ")).ToArray();
        foreach(var stone in stones)Undo.DestroyObjectImmediate(stone.gameObject);
        var edge=root.Find("Shore Edge");if(edge!=null){Undo.RecordObject(edge.gameObject,"Hide straight shore edge");edge.gameObject.SetActive(false);}
        var sea=root.Find("Sea");var mesh=sea.GetComponent<MeshFilter>().sharedMesh;
        Undo.RecordObject(mesh,"Extend water over shore");
        var vertices=mesh.vertices;
        // Rebuild horizontal extent from grid columns; safe when rerun.
        const int nx=128;
        for(int i=0;i<vertices.Length;i++)vertices[i].x=-10+(i%(nx+1))*130f/nx;
        mesh.vertices=vertices;mesh.bounds=new Bounds(new Vector3(55,0,0),new Vector3(130,4,240));
        EditorUtility.SetDirty(mesh);AssetDatabase.SaveAssetIfDirty(mesh);
        var mat=sea.GetComponent<Renderer>().sharedMaterial;Undo.RecordObject(mat,"Shore wash settings");
        mat.SetFloat("_ShoreReach",2.4f);mat.SetFloat("_ShoreCurve",1.1f);
        EditorUtility.SetDirty(mat);AssetDatabase.SaveAssetIfDirty(mat);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        return "Removed stones="+stones.Length+"; wash reaches ground; shader errors="+ShaderUtil.ShaderHasError(mat.shader);
    }
}
