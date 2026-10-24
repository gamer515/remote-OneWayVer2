using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class CreateBackgroundCylinder
{
    public static string Run()
    {
        if (EditorApplication.isPlaying) throw new System.Exception("Exit Play mode first.");
        var stage = GameObject.Find("Battle3D/Stages/B1");
        if (!stage) throw new System.Exception("B1 stage missing.");
        if (stage.transform.Find("BackgroundCylinder")) throw new System.Exception("Cylinder already exists.");
        const string folder = "Assets/3DBattle/BackGround";
        const string meshPath = folder + "/BackgroundCylinder_Inward.asset";
        const string matPath = folder + "/BackgroundCylinder_Unlit.mat";
        if (AssetDatabase.LoadMainAssetAtPath(meshPath) || AssetDatabase.LoadMainAssetAtPath(matPath)) throw new System.Exception("Output assets already exist.");
        const int segments = 128;
        var vertices = new Vector3[(segments+1)*2];
        var normals = new Vector3[vertices.Length];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[segments*6];
        for (int i=0; i<=segments; i++)
        {
            float u=(float)i/segments;
            float angle=u*Mathf.PI*2;
            var radial=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
            vertices[i*2]=radial;
            vertices[i*2+1]=radial+Vector3.up;
            normals[i*2]=normals[i*2+1]=-radial;
            uv[i*2]=new Vector2(u,0);
            uv[i*2+1]=new Vector2(u,1);
            if(i==segments) continue;
            int a=i*2,b=a+2,t=i*6;
            triangles[t]=a; triangles[t+1]=b; triangles[t+2]=a+1;
            triangles[t+3]=b; triangles[t+4]=b+1; triangles[t+5]=a+1;
        }
        for(int i=0;i<triangles.Length;i+=3)
        {
            var a=vertices[triangles[i]]; var b=vertices[triangles[i+1]]; var c=vertices[triangles[i+2]];
            var radial=(a+b+c)/3; radial.y=0;
            if(Vector3.Dot(Vector3.Cross(b-a,c-a),radial)>=0) throw new System.Exception("Outward winding found.");
        }
        var mesh=new Mesh {name="BackgroundCylinder_Inward_128"};
        mesh.vertices=vertices; mesh.normals=normals; mesh.uv=uv; mesh.triangles=triangles;
        mesh.RecalculateBounds(); mesh.RecalculateTangents();
        AssetDatabase.CreateAsset(mesh,meshPath);
        var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        material.name="BackgroundCylinder_Unlit";
        material.SetColor("_BaseColor",Color.white);
        material.SetFloat("_Cull",2);
        material.SetFloat("_AlphaClip",1);
        material.SetFloat("_Cutoff",0.5f);
        material.EnableKeyword("_ALPHATEST_ON");
        AssetDatabase.CreateAsset(material,matPath);
        var go=new GameObject("BackgroundCylinder",typeof(MeshFilter),typeof(MeshRenderer));
        Undo.RegisterCreatedObjectUndo(go,"Create inward background cylinder");
        go.transform.position=new Vector3(0,-0.3f,0);
        go.transform.localScale=new Vector3(100,48,100);
        go.transform.SetParent(stage.transform,true);
        go.GetComponent<MeshFilter>().sharedMesh=mesh;
        var renderer=go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial=material;
        renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows=false;
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(go.scene);
        if(!EditorSceneManager.SaveScene(go.scene)) throw new System.Exception("Scene save failed.");
        Selection.activeGameObject=go;
        return "Created Battle3D/Stages/B1/BackgroundCylinder; radius=100 height=48; 256 triangles all inward; no caps; UV 0..1; no collider; shadows off; original walls unchanged; texture unassigned; scene saved.";
    }
}

