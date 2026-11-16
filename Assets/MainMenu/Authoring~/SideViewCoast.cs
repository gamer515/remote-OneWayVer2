using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class SideViewCoast
{
    public static string Apply()
    {
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(Application.isPlaying || scene.path!="Assets/Scenes/MainMenuScene.unity") throw new Exception("MainMenuScene edit mode required");
        AssetDatabase.Refresh();
        var root=GameObject.Find("Menu Coast");var sea=root.transform.Find("Sea");
        const int nx=128,nz=192;
        var vertices=new Vector3[(nx+1)*(nz+1)];var triangles=new int[nx*nz*6];
        for(int z=0;z<=nz;z++) for(int x=0;x<=nx;x++) vertices[z*(nx+1)+x]=new Vector3(x*120f/nx,0,-120+z*240f/nz);
        int k=0;
        for(int z=0;z<nz;z++) for(int x=0;x<nx;x++) {int a=z*(nx+1)+x;triangles[k++]=a;triangles[k++]=a+nx+1;triangles[k++]=a+1;triangles[k++]=a+1;triangles[k++]=a+nx+1;triangles[k++]=a+nx+2;}
        var mesh=new Mesh();mesh.name="Menu Sea Waves";mesh.vertices=vertices;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.bounds=new Bounds(new Vector3(60,0,0),new Vector3(120,4,240));
        string path="Assets/MainMenu/Coast/MenuSeaSurface.asset";
        var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing==null) AssetDatabase.CreateAsset(mesh,path); else {EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);mesh=existing;EditorUtility.SetDirty(mesh);AssetDatabase.SaveAssetIfDirty(mesh);}
        Undo.RecordObject(sea,"Set animated sea surface");sea.localPosition=new Vector3(0,-.1f,0);sea.localScale=Vector3.one;
        Undo.RecordObject(sea.GetComponent<MeshFilter>(),"Set sea mesh");sea.GetComponent<MeshFilter>().sharedMesh=mesh;
        var mat=sea.GetComponent<Renderer>().sharedMaterial;
        mat.SetFloat("_WaveHeight",.22f);mat.SetFloat("_WaveSpeed",.8f);mat.SetFloat("_WaveLength",6);mat.SetFloat("_FoamStrength",.75f);
        EditorUtility.SetDirty(mat);AssetDatabase.SaveAssetIfDirty(mat);
        var cam=Camera.main;Undo.RecordObject(cam,"Side view camera");Undo.RecordObject(cam.transform,"Side view camera pose");
        cam.orthographic=true;cam.orthographicSize=10;
        var focus=new Vector3(2,0,0);cam.transform.position=focus+new Vector3(32,12,-12);cam.transform.LookAt(focus);
        // Keep the shore and path broad enough to fill the low-angle background.
        var land=root.transform.Find("Land");Undo.RecordObject(land,"Extend coastal ground");land.localPosition=new Vector3(-70,-.05f,0);land.localScale=new Vector3(140,.3f,240);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        return "Saved low side view (19 degrees down), animated sea mesh, inward wave crests and foam; shader errors="+ShaderUtil.ShaderHasError(mat.shader);
    }
}
