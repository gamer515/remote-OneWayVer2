using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class FantasyBackpackPrefabInstaller
{
    const string Root = "Assets/FantasyBackpack";
    const string PrefabPath = Root + "/FantasyBackpack.prefab";

    static FantasyBackpackPrefabInstaller()
    {
        EditorApplication.delayCall += () =>
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) Build();
        };
    }

    [MenuItem("Tools/Fantasy Backpack/Rebuild Prefab")]
    public static void Build()
    {
        EnsureFolder("Assets", "FantasyBackpack");
        EnsureFolder(Root, "Materials");
        EnsureFolder(Root, "Animations");

        var canvas = MaterialAsset("Canvas_Dusty", new Color(.47f,.42f,.30f), 0f, .24f);
        var leather = MaterialAsset("Leather_Olive", new Color(.31f,.27f,.19f), .02f, .22f);
        var dark = MaterialAsset("Leather_Dark", new Color(.16f,.12f,.11f), .02f, .18f);
        var trim = MaterialAsset("Trim_Brown", new Color(.25f,.12f,.10f), .02f, .20f);
        var brass = MaterialAsset("Brass_Aged", new Color(.38f,.25f,.10f), .65f, .35f);

        var root = new GameObject("FantasyBackpack");
        root.transform.localScale = Vector3.one;

        Box(root.transform,"Body",new Vector3(2.15f,2.55f,.95f),new Vector3(0,1.25f,0),Vector3.zero,canvas);
        Box(root.transform,"BackPanel",new Vector3(2.28f,2.65f,.18f),new Vector3(0,1.28f,.50f),Vector3.zero,dark);
        Box(root.transform,"Bottom",new Vector3(2.22f,.38f,1.02f),new Vector3(0,.18f,-.01f),Vector3.zero,dark);
        Box(root.transform,"FrontPocket",new Vector3(1.28f,.92f,.32f),new Vector3(.25f,.82f,-.61f),new Vector3(-5,0,0),leather);
        Box(root.transform,"PocketFlap",new Vector3(1.34f,.33f,.37f),new Vector3(.25f,1.28f,-.64f),new Vector3(-10,0,0),trim);
        Box(root.transform,"PocketBuckle",new Vector3(.18f,.28f,.09f),new Vector3(.25f,1.08f,-.86f),Vector3.zero,brass);
        Box(root.transform,"LeftSidePocket",new Vector3(.40f,.78f,.65f),new Vector3(-1.18f,.76f,-.05f),new Vector3(0,0,8),dark);
        Box(root.transform,"RightSidePocket",new Vector3(.40f,.78f,.65f),new Vector3(1.18f,.76f,-.05f),new Vector3(0,0,-8),dark);
        Box(root.transform,"UpperPatch",new Vector3(.52f,.60f,.07f),new Vector3(-.68f,1.83f,-.52f),new Vector3(0,0,-14),trim);
        Box(root.transform,"LowerPatch",new Vector3(.45f,.48f,.07f),new Vector3(.65f,.30f,-.55f),new Vector3(0,0,8),trim);
        Box(root.transform,"FrontStrapL",new Vector3(.16f,2.15f,.08f),new Vector3(-.64f,1.12f,-.55f),new Vector3(0,0,-3),trim);
        Box(root.transform,"FrontStrapR",new Vector3(.16f,2.15f,.08f),new Vector3(.76f,1.12f,-.55f),new Vector3(0,0,3),trim);

        // Wearable padded shoulder straps on the rear (+Z).
        Capsule(root.transform,"ShoulderStrap_L",new Vector3(-.58f,1.48f,.69f),new Vector3(.30f,1.28f,.16f),new Vector3(0,0,-10),dark);
        Capsule(root.transform,"ShoulderStrap_R",new Vector3(.58f,1.48f,.69f),new Vector3(.30f,1.28f,.16f),new Vector3(0,0,10),dark);
        Box(root.transform,"ShoulderAnchor_L",new Vector3(.28f,.55f,.18f),new Vector3(-.58f,.40f,.66f),new Vector3(0,0,-8),trim);
        Box(root.transform,"ShoulderAnchor_R",new Vector3(.28f,.55f,.18f),new Vector3(.58f,.40f,.66f),new Vector3(0,0,8),trim);

        // Carry handle.
        Capsule(root.transform,"HandleLeft",new Vector3(-.24f,2.67f,.30f),new Vector3(.12f,.38f,.12f),new Vector3(0,0,-35),dark);
        Capsule(root.transform,"HandleRight",new Vector3(.24f,2.67f,.30f),new Vector3(.12f,.38f,.12f),new Vector3(0,0,35),dark);
        Capsule(root.transform,"HandleTop",new Vector3(0,2.86f,.30f),new Vector3(.12f,.34f,.12f),new Vector3(0,0,90),dark);

        var lidPivot = new GameObject("LidPivot");
        lidPivot.transform.SetParent(root.transform,false);
        lidPivot.transform.localPosition = new Vector3(0,2.48f,.44f);
        Box(lidPivot.transform,"Lid",new Vector3(2.27f,.16f,1.35f),new Vector3(0,-.12f,-.52f),new Vector3(12,0,0),leather);
        Box(lidPivot.transform,"LidFront",new Vector3(2.28f,.60f,.16f),new Vector3(0,-.48f,-1.08f),new Vector3(-8,0,0),leather);
        Box(lidPivot.transform,"LidTrim",new Vector3(2.34f,.12f,.15f),new Vector3(0,-.73f,-1.10f),Vector3.zero,trim);

        foreach(var c in root.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);

        var clipPath = Root + "/Animations/OpenLid.anim";
        AssetDatabase.DeleteAsset(clipPath);
        var clip = new AnimationClip { name="OpenLid", legacy=true, frameRate=30 };
        var q0=Quaternion.Euler(0,0,0); var q1=Quaternion.Euler(-8,0,0); var q2=Quaternion.Euler(112,0,0);
        Curve(clip,"localRotation.x",q0.x,q1.x,q2.x); Curve(clip,"localRotation.y",q0.y,q1.y,q2.y);
        Curve(clip,"localRotation.z",q0.z,q1.z,q2.z); Curve(clip,"localRotation.w",q0.w,q1.w,q2.w);
        clip.EnsureQuaternionContinuity(); AssetDatabase.CreateAsset(clip,clipPath);
        var animation=root.AddComponent<Animation>(); animation.playAutomatically=false; animation.AddClip(clip,"OpenLid"); animation.clip=clip;

        PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        Selection.activeObject=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Debug.Log("FantasyBackpack prefab created: " + PrefabPath);
    }

    public static void BuildAndExport()
    {
        Build();
        AssetDatabase.ExportPackage(Root, Path.GetFullPath("../FantasyBackpack_Prefab.unitypackage"), ExportPackageOptions.Recurse);
        Debug.Log("Exported native prefab package.");
        EditorApplication.Exit(0);
    }

    static void Curve(AnimationClip clip,string property,float a,float b,float c)
    {
        AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("LidPivot",typeof(Transform),property),
            new AnimationCurve(new Keyframe(0,a),new Keyframe(.16f,b),new Keyframe(1,c)));
    }
    static Material MaterialAsset(string name,Color color,float metallic,float smoothness)
    {
        var path=Root+"/Materials/"+name+".mat"; var old=AssetDatabase.LoadAssetAtPath<Material>(path); if(old!=null)return old;
        var shader=Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"); var m=new Material(shader){name=name,color=color};
        if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",color); if(m.HasProperty("_Metallic"))m.SetFloat("_Metallic",metallic); if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",smoothness);
        AssetDatabase.CreateAsset(m,path); return m;
    }
    static GameObject Box(Transform p,string n,Vector3 s,Vector3 pos,Vector3 rot,Material m) => Primitive(PrimitiveType.Cube,p,n,s,pos,rot,m);
    static GameObject Capsule(Transform p,string n,Vector3 pos,Vector3 scale,Vector3 rot,Material m) => Primitive(PrimitiveType.Capsule,p,n,scale,pos,rot,m);
    static GameObject Primitive(PrimitiveType type,Transform p,string n,Vector3 s,Vector3 pos,Vector3 rot,Material m)
    { var g=GameObject.CreatePrimitive(type); g.name=n; g.transform.SetParent(p,false); g.transform.localScale=s; g.transform.localPosition=pos; g.transform.localEulerAngles=rot; g.GetComponent<Renderer>().sharedMaterial=m; return g; }
    static void EnsureFolder(string parent,string child){var p=parent+"/"+child;if(!AssetDatabase.IsValidFolder(p))AssetDatabase.CreateFolder(parent,child);}
}
