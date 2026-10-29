using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class ExtendMenuGround
{
    public static string Apply()
    {
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(Application.isPlaying || scene.path!="Assets/Scenes/MainMenuScene.unity")throw new Exception("MainMenuScene edit mode required");
        var land=GameObject.Find("Menu Coast").transform.Find("Land");
        float top=land.localPosition.y+land.localScale.y*.5f;
        float bottom=Mathf.Min(-20,land.localPosition.y-land.localScale.y*.5f);
        Undo.RecordObject(land,"Extend ground downward");
        var position=land.localPosition;var scale=land.localScale;
        position.y=(top+bottom)*.5f;scale.y=top-bottom;
        land.localPosition=position;land.localScale=scale;
        var mesh=land.GetComponent<MeshFilter>().sharedMesh;Undo.RecordObject(mesh,"Keep side texture density");
        var vertices=mesh.vertices;var normals=mesh.normals;var uv=mesh.uv;
        for(int i=0;i<vertices.Length;i++)
        {
            if(Mathf.Abs(normals[i].y)>.5f)continue;
            var p=Vector3.Scale(vertices[i],scale);
            uv[i]=new Vector2(Mathf.Abs(normals[i].x)>.5f?p.z:p.x,p.y+position.y)/4f;
        }
        mesh.uv=uv;mesh.RecalculateTangents();EditorUtility.SetDirty(mesh);AssetDatabase.SaveAssetIfDirty(mesh);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        return "Ground top preserved="+top+", bottom="+bottom+", thickness="+scale.y;
    }
}
