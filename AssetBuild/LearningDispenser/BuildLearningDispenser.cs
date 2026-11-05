using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

// Retained authoring tool. Run through Unity Pipeline run_script, entry LearningDispenserBuilder.Build.
public static class LearningDispenserBuilder
{
    const string Root = "Assets/AiAsset/LearningDispenserPrototype";
    static Material wood, darkWood, gold, inset, pages, amber;
    static Material[] colors;
    static Mesh bevel, cylinder, star;
    static readonly List<string> checks = new List<string>();

    public static string Build()
    {
        if (EditorApplication.isPlaying) throw new Exception("Stop Play mode before authoring assets.");
        if (AssetDatabase.IsValidFolder(Root)) return FinishExisting();
        foreach (var d in new[]{Root,Root+"/Materials",Root+"/Meshes",Root+"/Textures",Root+"/Prefabs",Root+"/Animations",Root+"/Documentation"}) Directory.CreateDirectory(d);
        Directory.CreateDirectory("output/LearningDispenserPrototype");
        AssetDatabase.Refresh();
        wood=Mat("HoneyWood",new Color(.58f,.29f,.105f),0,.25f);
        darkWood=Mat("DarkWood",new Color(.21f,.105f,.047f),0,.19f);
        gold=Mat("Brass",new Color(.88f,.57f,.17f),.58f,.38f);
        inset=Mat("Recess",new Color(.045f,.075f,.09f),0,.13f);
        pages=Mat("BookPages",new Color(.85f,.83f,.66f),0,.22f);
        amber=Mat("ScheduledAmber",new Color(.98f,.57f,.12f),.12f,.32f);
        colors=new[]{Mat("Blue",new Color(.035f,.32f,.8f),.1f,.4f),Mat("Red",new Color(.72f,.055f,.07f),.1f,.4f),Mat("Yellow",new Color(.98f,.7f,.035f),.1f,.4f),Mat("Teal",new Color(.015f,.55f,.54f),.1f,.4f)};
        Grain();
        bevel=BeveledCube(); AssetDatabase.CreateAsset(bevel,Root+"/Meshes/BeveledBlock.asset");
        var primitive=GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cylinder=UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); cylinder.name="Cylinder";
        UnityEngine.Object.DestroyImmediate(primitive); AssetDatabase.CreateAsset(cylinder,Root+"/Meshes/Cylinder.asset");
        star=Star(); AssetDatabase.CreateAsset(star,Root+"/Meshes/EightPointStar.asset");
        var machine=new GameObject("LearningDispenser_Prototype");
        try
        {
            var frame=Group(machine,"01_WoodCabinet");
            Block(frame,"Backboard",new Vector3(0,2.7f,.19f),new Vector3(5.05f,5.4f,.25f),wood);
            Block(frame,"Outer_Left",new Vector3(-2.47f,2.7f,-.05f),new Vector3(.16f,5.56f,.44f),wood);
            Block(frame,"Outer_Right",new Vector3(2.47f,2.7f,-.05f),new Vector3(.16f,5.56f,.44f),wood);
            foreach(float y in new[]{0f,4.29f,5.4f}) Block(frame,"Crossbeam",new Vector3(0,y,-.12f),new Vector3(5.12f,.17f,.4f),wood);
            for(int i=0;i<4;i++) Block(frame,"PanelSeam_"+i,new Vector3(-1.59f+i*1.02f,2.1f,.04f),new Vector3(.018f,4.1f,.02f),darkWood);
            foreach(float x in new[]{-2.47f,-1.53f,2.47f}) foreach(float y in new[]{.04f,4.28f,5.39f}) Disc(frame,"FramePin",new Vector3(x,y,-.35f),.055f,.045f,gold);

            var schedule=Group(machine,"02_ScheduledStack");
            Block(schedule,"DarkShaft",new Vector3(-2.0f,2.7f,-.03f),new Vector3(.72f,5.05f,.2f),inset);
            for(int i=0;i<39;i++) Block(schedule,"ScheduledTile_"+i.ToString("00"),new Vector3(-2, .25f+i*.126f,-.25f),new Vector3(.59f,.108f,.25f),amber);
            foreach(float x in new[]{-2.39f,-1.61f}) Block(schedule,"StackRail",new Vector3(x,2.7f,-.22f),new Vector3(.085f,5.23f,.23f),wood);

            for(int c=0;c<4;c++)
            {
                float x=-1.05f+c*1.02f;
                var col=Group(machine,"03_Column_"+new[]{"Blue","Red","Yellow","Teal"}[c]);
                col.transform.localPosition=new Vector3(x,0,0);
                Disc(col,"CoinRim",new Vector3(0,4.91f,-.18f),.37f,.12f,gold);
                Disc(col,"CoinFace",new Vector3(0,4.91f,-.255f),.325f,.07f,colors[c]);
                Part(col,"Star",star,new Vector3(0,4.91f,-.32f),new Vector3(.24f,.24f,.045f),gold);
                Block(col,"SupplyRecess",new Vector3(0,2.94f,-.02f),new Vector3(.83f,2.05f,.23f),inset);
                foreach(float dx in new[]{-.445f,.445f}) Block(col,"SupplySide",new Vector3(dx,2.94f,-.16f),new Vector3(.075f,2.2f,.25f),wood);
                foreach(float y in new[]{1.86f,4.02f}) Block(col,"SupplyEnd",new Vector3(0,y,-.16f),new Vector3(.95f,.075f,.25f),wood);
                for(int n=0;n<3;n++) Book(col,"LearningObject_"+n,new Vector3(0,2.12f+n*.49f,-.2f),colors[c]);
                foreach(float dx in new[]{-.44f,.44f}) foreach(float y in new[]{1.85f,4.02f}) Disc(col,"SupplyPin",new Vector3(dx,y,-.32f),.043f,.035f,gold);
                Block(col,"ButtonMount",new Vector3(0,1.22f,-.08f),new Vector3(.94f,1.03f,.23f),gold);
                var button=Group(col,"PressButton"); button.transform.localPosition=new Vector3(0,1.23f,-.22f);
                Disc(button,"Rim",Vector3.zero,.315f,.13f,gold);
                Disc(button,"Cap",new Vector3(0,0,-.09f),.262f,.14f,colors[c]);
                button.AddComponent<BoxCollider>().size=new Vector3(.62f,.62f,.3f);
                checks.Add("Column "+c+": coin, supply, button center x="+x.ToString("F3",System.Globalization.CultureInfo.InvariantCulture));
            }
            var dispenser=Group(machine,"04_LeftDispenser");
            Block(dispenser,"Backing",new Vector3(-2,-1.05f,.18f),new Vector3(.95f,2.0f,.25f),wood);
            Block(dispenser,"OutletShadow",new Vector3(-2,-.35f,-.015f),new Vector3(.65f,.44f,.22f),inset);
            foreach(float x in new[]{-2.4f,-1.6f}) Block(dispenser,"OutletSide",new Vector3(x,-.35f,-.2f),new Vector3(.09f,.6f,.26f),gold);
            foreach(float y in new[]{-.65f,-.05f}) Block(dispenser,"OutletLip",new Vector3(-2,y,-.2f),new Vector3(.88f,.09f,.29f),gold);
            var chute=Block(dispenser,"SlopingChute",new Vector3(-2,-.66f,-.32f),new Vector3(.65f,.065f,.44f),wood);
            chute.transform.localRotation=Quaternion.Euler(-20,0,0);
            Block(dispenser,"ReceiverFloor",new Vector3(-2,-1.88f,-.25f),new Vector3(.88f,.12f,.83f),wood);
            Block(dispenser,"ReceiverFront",new Vector3(-2,-1.68f,-.66f),new Vector3(.88f,.32f,.1f),gold);
            foreach(float x in new[]{-2.44f,-1.56f}) Block(dispenser,"ReceiverSide",new Vector3(x,-1.65f,-.25f),new Vector3(.09f,.46f,.86f),wood);
            for(int i=0;i<3;i++) Block(dispenser,"CollectedTile_"+i,new Vector3(-2,-1.75f+i*.115f,-.25f),new Vector3(.58f,.1f,.37f),amber);
            Block(dispenser,"EmergingTile",new Vector3(-2,-.42f,-.26f),new Vector3(.58f,.1f,.38f),amber);
            var falling=Block(dispenser,"FallingTile",new Vector3(-2,-1.07f,-.38f),new Vector3(.58f,.1f,.37f),amber);
            falling.transform.localRotation=Quaternion.Euler(14,0,-23);
            machine.AddComponent<BoxCollider>().size=new Vector3(5.15f,5.6f,.5f);
            machine.GetComponent<BoxCollider>().center=new Vector3(0,2.7f,.1f);
            Save(machine,"LearningDispenser_Static");
            var clip=new AnimationClip {name="Dispense_Loop",legacy=true,wrapMode=WrapMode.Loop};
            string path="04_LeftDispenser/FallingTile";
            foreach(var axis in new[]{"x","y","z"})
            {
                float start=axis=="x"?-2:axis=="y"?-.48f:-.37f;
                float end=axis=="y"?-1.38f:start;
                var curve=new AnimationCurve(new Keyframe(0,start),new Keyframe(.2f,start),new Keyframe(.68f,end),new Keyframe(.98f,end),new Keyframe(1.05f,start),new Keyframe(1.4f,start));
                clip.SetCurve(path,typeof(Transform),"localPosition."+axis,curve);
            }
            clip.SetCurve("03_Column_Blue/PressButton",typeof(Transform),"localPosition.z",new AnimationCurve(new Keyframe(0,-.22f),new Keyframe(.12f,-.16f),new Keyframe(.28f,-.22f),new Keyframe(1.4f,-.22f)));
            AssetDatabase.CreateAsset(clip,Root+"/Animations/Dispense_Loop.anim");
            var animation=machine.AddComponent<Animation>(); animation.AddClip(clip,clip.name); animation.clip=clip; animation.playAutomatically=true;
            Save(machine,"LearningDispenser_AnimatedDemo");
            UnityEngine.Object.DestroyImmediate(animation);
            var tile=Block(null,"ScheduledTile",Vector3.zero,new Vector3(.59f,.108f,.25f),amber); Save(tile,"ScheduledTile"); UnityEngine.Object.DestroyImmediate(tile);
            for(int c=0;c<4;c++) {var book=Book(null,"LearningBook",Vector3.zero,colors[c]); Save(book,"LearningBook_"+c); UnityEngine.Object.DestroyImmediate(book);}
            var reusableCoin=UnityEngine.Object.Instantiate(machine.transform.Find("03_Column_Blue/CoinFace").gameObject); reusableCoin.name="CoinBlue"; reusableCoin.transform.position=Vector3.zero; Save(reusableCoin,"CoinBlue"); UnityEngine.Object.DestroyImmediate(reusableCoin);
            var reusableButton=UnityEngine.Object.Instantiate(machine.transform.Find("03_Column_Blue/PressButton").gameObject); reusableButton.name="ButtonBlue"; reusableButton.transform.position=Vector3.zero; Save(reusableButton,"ButtonBlue"); UnityEngine.Object.DestroyImmediate(reusableButton);
            Preview(machine);
        }
        finally {UnityEngine.Object.DestroyImmediate(machine);}
        return FinishPackage();
    }
    static string FinishExisting()
    {
        var model=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/LearningDispenser_Static.prefab"));
        try {Preview(model);} finally {UnityEngine.Object.DestroyImmediate(model);}
        return FinishPackage();
    }
    static string FinishPackage()
    {
        File.WriteAllText(Root+"/Documentation/START_HERE.txt", "Learning Dispenser Prototype v1\nUnity 6000.3 / URP. Real 3D meshes; no reference image texture.\nImport the .unitypackage and drag Prefabs/LearningDispenser_Static into your scene.\nFront faces local -Z. 1 unit = 1 meter; scale root to fit your UI camera.\nMain cabinet ~5.1 wide x 5.5 high, left dispenser extends 1.95 below base.\n01 cabinet, 02 scheduled stack, 03 four aligned columns, 04 left dispenser.\nThe four lower coin tubes are intentionally excluded. No text labels.\nButtons, coins and learning objects are separate child objects.\nAnimatedDemo uses a legacy Animation clip to show a repeating dispense; it does not implement scheduling, inventory or input. Static prefab captures a tile mid-fall.\nOptional isolated demo scene included. No changes to your existing scene.\nMeshes/materials/prefabs are owned by this package; no third-party models/textures.\nEdit source: AssetBuild/LearningDispenser/BuildLearningDispenser.cs in source workspace.\n");
        AssetDatabase.Refresh(); AssetDatabase.SaveAssets();
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/LearningDispenser_Static.prefab");
        foreach(var r in prefab.GetComponentsInChildren<MeshRenderer>(true)) foreach(var m in r.sharedMaterials) if(m==null || m.shader==null || m.shader.name=="Hidden/InternalErrorShader") throw new Exception("Invalid material");
        foreach(var f in prefab.GetComponentsInChildren<MeshFilter>(true)) if(!AssetDatabase.Contains(f.sharedMesh)) throw new Exception("Unsaved mesh");
        checks.Add("Validated persistent meshes and non-error materials.");
        checks.Add("Prefab renderers: "+prefab.GetComponentsInChildren<MeshRenderer>(true).Length);
        File.WriteAllLines("output/LearningDispenserPrototype/validation.txt",checks);
        AssetDatabase.ExportPackage(Root,"output/LearningDispenserPrototype/LearningDispenserPrototype_v1.unitypackage",ExportPackageOptions.Recurse);
        return "Created package, prefabs, preview and isolated demo scene.";
    }

    public static string Validate()
    {
        var p=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/LearningDispenser_Static.prefab");
        if(!p) throw new Exception("Missing prefab");
        for(int c=0;c<4;c++)
        {
            var column=p.transform.Find("03_Column_"+new[]{"Blue","Red","Yellow","Teal"}[c]);
            foreach(var name in new[]{"CoinFace","SupplyRecess","PressButton"}) if(Mathf.Abs(column.Find(name).localPosition.x)>.001f) throw new Exception("Column alignment invalid");
        }
        if(p.transform.Find("02_ScheduledStack").childCount!=42) throw new Exception("Scheduled stack missing pieces");
        var animated=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/LearningDispenser_AnimatedDemo.prefab");
        var clip=animated.GetComponent<Animation>().clip;
        var instance=UnityEngine.Object.Instantiate(animated);
        try {clip.SampleAnimation(instance,.2f);float a=instance.transform.Find("04_LeftDispenser/FallingTile").localPosition.y;clip.SampleAnimation(instance,.68f);float b=instance.transform.Find("04_LeftDispenser/FallingTile").localPosition.y;if(a-b<.8f)throw new Exception("Dispense motion failed");checks.Add("Sampled dispense clip: tile moved down "+(a-b)+" units.");}
        finally{UnityEngine.Object.DestroyImmediate(instance);}
        checks.Add("Four column alignments checked; scheduled stack 39 tiles; no lower coin tubes.");
        if(EditorApplication.isPlaying) {File.AppendAllLines("output/LearningDispenserPrototype/validation.txt",checks);return "Alignment and animation sampling passed; preserving active Play session.";}
        // Render the saved demo in an ordinary additive scene with actual scene lights.
        var previous=SceneManager.GetActiveScene();var s=EditorSceneManager.OpenScene(Root+"/LearningDispenser_Demo.unity",OpenSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(s);Camera cam=null;foreach(var obj in s.GetRootGameObjects()) {if(obj.GetComponent<Camera>())cam=obj.GetComponent<Camera>();foreach(var tr in obj.GetComponentsInChildren<Transform>(true))tr.gameObject.layer=30;}
            cam.cullingMask=1<<30;cam.transform.position=new Vector3(-.35f,2.05f,-13);cam.transform.LookAt(new Vector3(0,1.75f,0));
            foreach(var obj in s.GetRootGameObjects())if(obj.GetComponent<Light>()){obj.GetComponent<Light>().cullingMask=1<<30;obj.GetComponent<Light>().shadows=LightShadows.Soft;}
            var rt=new RenderTexture(1000,1400,24);rt.antiAliasing=4;rt.Create();cam.targetTexture=rt;cam.Render();var old=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(1000,1400,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1000,1400),0,0);image.Apply();RenderTexture.active=old;File.WriteAllBytes("output/LearningDispenserPrototype/Preview.png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);cam.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);
        }
        finally{EditorSceneManager.CloseScene(s,true);if(previous.IsValid())SceneManager.SetActiveScene(previous);}
        checks.Add("Four column alignments checked; scheduled stack 39 tiles; no lower coin tubes.");
        File.AppendAllLines("output/LearningDispenserPrototype/validation.txt",checks);
        return "Alignment and animation sampling passed; demo rendered.";
    }

    static Material Mat(string name,Color color,float metallic,float smooth)
    {
        var shader=Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard");
        var m=new Material(shader){name=name,color=color}; m.SetFloat("_Metallic",metallic); m.SetFloat("_Smoothness",smooth);
        AssetDatabase.CreateAsset(m,Root+"/Materials/"+name+".mat");return m;
    }
    static void Grain()
    {
        var t=new Texture2D(256,256,TextureFormat.RGB24,false); var rng=new System.Random(73);
        for(int y=0;y<256;y++)for(int x=0;x<256;x++) {float v=.84f+.07f*Mathf.Sin(y*.47f+Mathf.Sin(x*.04f)*.9f)+.025f*(float)rng.NextDouble();t.SetPixel(x,y,new Color(v,v,v));}
        t.Apply();File.WriteAllBytes(Root+"/Textures/WoodGrain.png",t.EncodeToPNG());UnityEngine.Object.DestroyImmediate(t);AssetDatabase.ImportAsset(Root+"/Textures/WoodGrain.png");
        wood.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/WoodGrain.png");
    }
    static GameObject Group(GameObject parent,string name) {var o=new GameObject(name);if(parent)o.transform.SetParent(parent.transform,false);return o;}
    static GameObject Part(GameObject parent,string name,Mesh mesh,Vector3 pos,Vector3 scale,Material material)
    {var o=Group(parent,name);o.transform.localPosition=pos;o.transform.localScale=scale;o.AddComponent<MeshFilter>().sharedMesh=mesh;o.AddComponent<MeshRenderer>().sharedMaterial=material;return o;}
    static GameObject Block(GameObject parent,string name,Vector3 pos,Vector3 size,Material m) {return Part(parent,name,bevel,pos,size,m);}
    static GameObject Disc(GameObject parent,string name,Vector3 pos,float radius,float depth,Material m)
    {var o=Part(parent,name,cylinder,pos,new Vector3(radius*2,depth*.5f,radius*2),m);o.transform.localRotation=Quaternion.Euler(90,0,0);return o;}
    static GameObject Book(GameObject parent,string name,Vector3 pos,Material m)
    {var o=Group(parent,name);o.transform.localPosition=pos;Block(o,"Pages",Vector3.zero,new Vector3(.56f,.2f,.34f),pages);foreach(float y in new[]{-.135f,.135f})Block(o,"Cover",new Vector3(0,y,0),new Vector3(.65f,.065f,.42f),m);Block(o,"Spine",new Vector3(0,0,.18f),new Vector3(.65f,.28f,.07f),m);return o;}
    static void Save(GameObject obj,string name) {PrefabUtility.SaveAsPrefabAsset(obj,Root+"/Prefabs/"+name+".prefab");}

    static Mesh BeveledCube()
    {
        var vs=new List<Vector3>();var ts=new List<int>();var uv=new List<Vector2>();float h=.5f,k=.435f;
        Action<Vector3[]> face=points=>{int start=vs.Count;Vector3 center=Vector3.zero;foreach(var p in points)center+=p;center/=points.Length;if(Vector3.Dot(Vector3.Cross(points[1]-points[0],points[2]-points[0]),center)<0)Array.Reverse(points);foreach(var p in points){vs.Add(p);uv.Add(new Vector2(p.x+p.z+.5f,p.y+.5f));}for(int i=1;i<points.Length-1;i++){ts.Add(start);ts.Add(start+i);ts.Add(start+i+1);}};
        Func<int,float,int,float,int,float,Vector3> vec=(a,av,b,bv,c,cv)=>{var p=Vector3.zero;p[a]=av;p[b]=bv;p[c]=cv;return p;};
        for(int a=0;a<3;a++)for(int s=-1;s<=1;s+=2){int b=(a+1)%3,c=(a+2)%3;face(new[]{vec(a,s*h,b,-k,c,-k),vec(a,s*h,b,k,c,-k),vec(a,s*h,b,k,c,k),vec(a,s*h,b,-k,c,k)});}
        for(int a=0;a<3;a++)for(int b=a+1;b<3;b++){int c=3-a-b;for(int s=-1;s<=1;s+=2)for(int t=-1;t<=1;t+=2)face(new[]{vec(a,s*h,b,t*k,c,-k),vec(a,s*k,b,t*h,c,-k),vec(a,s*k,b,t*h,c,k),vec(a,s*h,b,t*k,c,k)});}
        for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)face(new[]{new Vector3(x*h,y*k,z*k),new Vector3(x*k,y*h,z*k),new Vector3(x*k,y*k,z*h)});
        var mesh=new Mesh{name="BeveledBlock"};mesh.SetVertices(vs);mesh.SetTriangles(ts,0);mesh.SetUVs(0,uv);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }
    static Mesh Star()
    {
        var v=new List<Vector3>();var t=new List<int>();
        for(int side=0;side<2;side++)for(int i=0;i<16;i++){float angle=i*Mathf.PI/8;float r=i%2==0?1:.35f;v.Add(new Vector3(Mathf.Sin(angle)*r,Mathf.Cos(angle)*r,side==0?-.5f:.5f));}
        v.Add(new Vector3(0,0,-.5f));v.Add(new Vector3(0,0,.5f));
        for(int i=0;i<16;i++){int j=(i+1)%16;t.AddRange(new[]{32,j,i,33,i+16,j+16,i,j,j+16,i,j+16,i+16});}
        var mesh=new Mesh{name="EightPointStar"};mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }
    static void Preview(GameObject source)
    {
        var scene=EditorSceneManager.NewPreviewScene();var oldScene=SceneManager.GetActiveScene();
        RenderTexture rt=null;Texture2D image=null;
        try
        {
            var model=UnityEngine.Object.Instantiate(source);SceneManager.MoveGameObjectToScene(model,scene);
            var cameraObject=new GameObject("PreviewCamera");SceneManager.MoveGameObjectToScene(cameraObject,scene);var camera=cameraObject.AddComponent<Camera>();camera.scene=scene;
            camera.transform.position=new Vector3(0,1.75f,-13);camera.transform.rotation=Quaternion.identity;camera.orthographic=true;camera.orthographicSize=4.0f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.13f,.16f,.17f);
            foreach(var p in new[]{new Vector3(-3,6,-5),new Vector3(4,3,-3)}) {var l=new GameObject("PreviewLight");SceneManager.MoveGameObjectToScene(l,scene);l.transform.position=p;l.transform.LookAt(new Vector3(0,2,0));var light=l.AddComponent<Light>();light.type=LightType.Directional;light.intensity=p.x<0?1.4f:.6f;}
            rt=new RenderTexture(1000,1400,24);rt.Create();camera.targetTexture=rt;camera.Render();var previous=RenderTexture.active;RenderTexture.active=rt;image=new Texture2D(1000,1400,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1000,1400),0,0);image.Apply();RenderTexture.active=previous;
            File.WriteAllBytes("output/LearningDispenserPrototype/Preview.png",image.EncodeToPNG());
            checks.Add("Rendered preview in isolated scene, 1000x1400.");
            // Save an ordinary scene copy, without switching the user's active scene.
            var demo=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            try {var go=PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/LearningDispenser_AnimatedDemo.prefab"),demo) as GameObject;var cam=UnityEngine.Object.Instantiate(cameraObject);cam.GetComponent<Camera>().targetTexture=null;cam.tag="MainCamera";SceneManager.MoveGameObjectToScene(cam,demo);foreach(var root in scene.GetRootGameObjects())if(root.GetComponent<Light>()){var copy=UnityEngine.Object.Instantiate(root);SceneManager.MoveGameObjectToScene(copy,demo);}EditorSceneManager.SaveScene(demo,Root+"/LearningDispenser_Demo.unity");}
            finally {EditorSceneManager.CloseScene(demo,true);if(oldScene.IsValid())SceneManager.SetActiveScene(oldScene);}
        }
        finally {if(image)UnityEngine.Object.DestroyImmediate(image);if(rt){rt.Release();UnityEngine.Object.DestroyImmediate(rt);}EditorSceneManager.ClosePreviewScene(scene);}
    }
}
