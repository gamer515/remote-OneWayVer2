using System;
using System.Linq;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
public static class SetupMenuCoast
{
    const string Dir="Assets/MainMenu/Coast/";
    static Material Mat(string name, Color color)
    {
        var path=Dir+name+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m==null) { m=new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(m,path); }
        m.SetColor("_BaseColor",color); EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); return m;
    }
    static GameObject Box(Transform parent,string name,Vector3 pos,Vector3 scale,Material material)
    {
        var o=GameObject.CreatePrimitive(PrimitiveType.Cube);o.name=name;o.transform.SetParent(parent,false);o.transform.localPosition=pos;o.transform.localScale=scale;
        o.GetComponent<Renderer>().sharedMaterial=material;UnityEngine.Object.DestroyImmediate(o.GetComponent<Collider>());return o;
    }
    static void Place(Transform panel,string name,Vector2 anchor,Vector2 size)
    {
        var t=panel.Find(name) as RectTransform;if(t==null)return;
        Undo.RecordObject(t,"Position menu UI");t.anchorMin=t.anchorMax=anchor;t.pivot=new Vector2(.5f,.5f);t.anchoredPosition=Vector2.zero;t.sizeDelta=size;
    }
    public static string Build()
    {
        if(Application.isPlaying) throw new Exception("Exit Play mode first");
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/MainMenuScene.unity")throw new Exception("Open MainMenuScene first");
        if(scene.GetRootGameObjects().Any(x=>x.name=="Menu Coast"))throw new Exception("Menu Coast already exists; preserve existing work");
        AssetDatabase.Refresh();
        var m=UnityEngine.Object.FindFirstObjectByType<MainMenuController>();
        var root=new GameObject("Menu Coast");Undo.RegisterCreatedObjectUndo(root,"Create coastal menu background");
        var rock=Mat("Coast Ground",new Color(.49f,.51f,.5f));
        var edge=Mat("Coast Edge",new Color(.36f,.4f,.4f));
        var pathMat=Mat("Coast Path",new Color(.64f,.64f,.59f));
        var sea=new Material(Shader.Find("MainMenu/Coastal Sea"));AssetDatabase.CreateAsset(sea,Dir+"Menu Sea.mat");
        Box(root.transform,"Sea",new Vector3(30,-.2f,0),new Vector3(60,.2f,200),sea);
        Box(root.transform,"Land",new Vector3(-30,-.05f,0),new Vector3(60,.3f,200),rock);
        Box(root.transform,"Shore Edge",new Vector3(-.22f,.12f,0),new Vector3(.44f,.12f,200),edge);
        Box(root.transform,"Walking Path",new Vector3(-2.4f,.115f,0),new Vector3(2.4f,.035f,200),pathMat);
        var random=new System.Random(42);
        for(int i=0;i<70;i++)
        {
            float z=-90+i*2.6f;float x=-4.8f-(float)random.NextDouble()*13;
            float s=.15f+(float)random.NextDouble()*.65f;
            var stone=Box(root.transform,"Coastal Stone "+i,new Vector3(x,.2f,z),new Vector3(s,.15f+s*.25f,s*.65f),edge);
            stone.transform.localRotation=Quaternion.Euler(0,(float)random.NextDouble()*180,0);
        }
        var anchor=new GameObject("Player Placement");anchor.transform.SetParent(root.transform,false);anchor.transform.position=new Vector3(-2.4f,.15f,-7);anchor.transform.rotation=Quaternion.identity;
        var cam=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<Camera>(true)).First(x=>x.CompareTag("MainCamera"));
        Undo.RecordObject(cam,"Set coastal menu camera");Undo.RecordObject(cam.transform,"Set coastal camera pose");
        cam.orthographic=true;cam.orthographicSize=12.5f;cam.nearClipPlane=.1f;cam.farClipPlane=300;
        var focus=new Vector3(2,0,0);cam.transform.position=focus+new Vector3(20,30,-30);cam.transform.LookAt(focus);
        cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.03f,.33f,.48f);
        var panel=m.mainMenuPanel.transform;
        var bg=panel.Find("MainMenuBackgroundImage");if(bg!=null){Undo.RecordObject(bg.gameObject,"Reveal 3D coast");bg.gameObject.SetActive(false);}
        var panelImage=panel.GetComponent<Image>();if(panelImage!=null){Undo.RecordObject(panelImage,"Clear menu backdrop");panelImage.color=Color.clear;}
        Place(panel,"TitleText (TMP)",new Vector2(.76f,.82f),new Vector2(330,120));
        Place(panel,"StartButton",new Vector2(.82f,.57f),new Vector2(140,55));
        Place(panel,"QuitButton",new Vector2(.82f,.43f),new Vector2(140,55));
        var title=panel.Find("TitleText (TMP)").GetComponent<TMP_Text>();Undo.RecordObject(title,"Fit temporary title");title.enableAutoSizing=true;title.fontSizeMin=26;title.fontSizeMax=46;
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject=root;
        return "Coast and fixed camera saved; player placement viewport="+cam.WorldToViewportPoint(anchor.transform.position)+"; existing button callbacks retained";
    }
    public static string Preview()
    {
        var cam=Camera.main;var previous=cam.targetTexture;var rt=new RenderTexture(1280,720,24);
        var previousActive=RenderTexture.active;
        try {cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;var tex=new Texture2D(1280,720,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1280,720),0,0);tex.Apply();File.WriteAllBytes("Assets/MainMenu/Authoring~/CoastPreview.png",tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);}
        finally {cam.targetTexture=previous;RenderTexture.active=previousActive;rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
        return "Assets/MainMenu/Authoring~/CoastPreview.png";
    }
}
