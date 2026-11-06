using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class MenuRaisedGroundSetup
{
    static void Surface(Transform t,Material material,string name)
    {
        var renderer=t.GetComponent<Renderer>();Undo.RecordObject(renderer,"Brick floor material");renderer.sharedMaterial=material;
        var filter=t.GetComponent<MeshFilter>();var mesh=UnityEngine.Object.Instantiate(filter.sharedMesh);mesh.name=name;
        var vertices=mesh.vertices;var normals=mesh.normals;var uv=new Vector2[vertices.Length];
        for(int i=0;i<vertices.Length;i++)
        {
            var p=Vector3.Scale(vertices[i],t.localScale);var n=normals[i];
            uv[i]=(Mathf.Abs(n.y)>.5f ? new Vector2(p.x,p.z) : Mathf.Abs(n.x)>.5f ? new Vector2(p.z,p.y) : new Vector2(p.x,p.y))/4f;
        }
        mesh.uv=uv;mesh.RecalculateTangents();
        string path="Assets/MainMenu/Coast/"+name+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing==null)AssetDatabase.CreateAsset(mesh,path);
        else {EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);mesh=existing;EditorUtility.SetDirty(mesh);AssetDatabase.SaveAssetIfDirty(mesh);}
        Undo.RecordObject(filter,"Ground texture scale");filter.sharedMesh=mesh;
    }
    public static string Apply()
    {
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(Application.isPlaying || scene.path!="Assets/Scenes/MainMenuScene.unity")throw new Exception("MainMenuScene edit mode required");
        var tex=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/KoreanTraditional_SmartMaterials Vol.1/Textures/BrickFloorA1_Roughness.png");
        if(tex==null)throw new Exception("Ground texture missing");
        const string path="Assets/MainMenu/Coast/BrickFloorA1_Roughness.mat";
        var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(mat==null){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,path);}
        Undo.RecordObject(mat,"Requested gray brick surface");mat.SetTexture("_BaseMap",tex);mat.SetColor("_BaseColor",Color.white);mat.SetFloat("_Smoothness",.1f);mat.SetFloat("_Metallic",0);
        EditorUtility.SetDirty(mat);AssetDatabase.SaveAssetIfDirty(mat);
        var root=GameObject.Find("Menu Coast").transform;var land=root.Find("Land");Undo.RecordObject(land,"Raise land above sea");
        land.localPosition=new Vector3(-70,.65f,0);land.localScale=new Vector3(140,1.7f,240);Surface(land,mat,"Menu Raised Ground Mesh");
        var walk=root.Find("Walking Path");Undo.RecordObject(walk,"Raise walking path");var p=walk.localPosition;p.y=1.515f;walk.localPosition=p;Surface(walk,mat,"Menu Raised Path Mesh");
        var anchor=root.Find("Player Placement");if(anchor!=null){Undo.RecordObject(anchor,"Raise player placement with ground");p=anchor.localPosition;p.y=1.55f;anchor.localPosition=p;}
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        return "Land top=1.5; material="+path+"; shader errors="+ShaderUtil.ShaderHasError(mat.shader);
    }
}
