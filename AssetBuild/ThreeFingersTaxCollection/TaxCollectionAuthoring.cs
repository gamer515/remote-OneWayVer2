using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;

// Reproducible authoring tool. Runs outside the game's Assets; no runtime scripts required.
public static class TaxCollectionAuthoring
{
    public const string Root="Assets/ThreeFingersTaxCollection";
    const string Previous="Assets/ThreeFingersCoinBowlPickup";
    const string Work="AssetBuild/ThreeFingersTaxCollection";
    public static string Output=>Path.GetFullPath("../../../output/ThreeFingersTaxCollection");
    public static readonly string[] Names={"ThreeFingers_TaxScaleCollection","ThreeFingers_DirectCoinCollection"};
    public static readonly Vector3 BowlStart=new Vector3(.14f,0,0);
    public static readonly Vector3 Destination=new Vector3(-.20f,.19f,.28f);
    static Dictionary<string,Quaternion> opened,closed;
    static AnimationClip original;
    static GameObject hand;
    static Vector3 coinStart,coinHand,depositHand,bowlHand;
    static int mode;
    public static string Prepare()
    {
        string project=Work+"/ValidationProject";
        foreach(string folder in new[]{"Assets/Editor","Packages","ProjectSettings"})Directory.CreateDirectory(project+"/"+folder);
        var inputs=new[]{Previous+"/Scenes/ThreeFingers_CoinBowlPickup_Preview.unity","Assets/AntiqueCoinScale/Prefabs/WoodenCoinBowl.prefab","Assets/AntiqueCoinScale/Prefabs/AntiqueBalanceScale.prefab","Assets/AntiqueCoinScale/Prefabs/SampleCoin.prefab"};
        var deps=AssetDatabase.GetDependencies(inputs,true).Where(p=>p.StartsWith("Assets/")&&!p.EndsWith(".cs")).ToArray();
        foreach(string path in deps){string target=project+"/"+path;Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(path,target,true);if(File.Exists(path+".meta"))File.Copy(path+".meta",target+".meta",true);}
        var materials=deps.Where(p=>p.EndsWith(".mat")).Select(p=>{var m=AssetDatabase.LoadAssetAtPath<Material>(p);var c=m.color;return new{path=p,r=c.r,g=c.g,b=c.b,a=c.a,texture=AssetDatabase.GetAssetPath(m.mainTexture)};}).ToArray();
        File.WriteAllText(project+"/Assets/Editor/MaterialCatalog.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{materials}));
        foreach(string name in new[]{"TaxCollectionAuthoring.cs","TaxCollectionPlayCheck.cs"})File.Copy(Work+"/"+name,project+"/Assets/Editor/"+name,true);
        File.Copy("ProjectSettings/ProjectVersion.txt",project+"/ProjectSettings/ProjectVersion.txt",true);
        var json=UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(Newtonsoft.Json.JsonConvert).Assembly);
        var packages=new Dictionary<string,string>{{"com.unity.modules.animation","1.0.0"},{"com.unity.modules.physics","1.0.0"},{"com.unity.modules.imageconversion","1.0.0"},{"com.unity.nuget.newtonsoft-json","file:"+json.resolvedPath.Replace('\\','/')}};
        File.WriteAllText(project+"/Packages/manifest.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{dependencies=packages}));
        return Path.GetFullPath(project);
    }
    public static void Build()
    {
        foreach(string folder in new[]{"Animations","Prefabs","Scenes","Documentation","Materials"})Directory.CreateDirectory(Root+"/"+folder);
        Directory.CreateDirectory(Output);
        AssetDatabase.Refresh();
        original=AssetDatabase.LoadAssetAtPath<AnimationClip>(Previous+"/Animations/ThreeFingers_CoinBowlPickup.anim");
        for(mode=0;mode<2;mode++)BuildOne();
        File.WriteAllText(Root+"/Documentation/FrameMarkers.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{
            fps=60,frameNumbering="zero-based",clips=new[]{
                new{name=Names[0],duration=3f,frames=180,markers=new[]{new{frame=30,time=.5f,eventName="OnTaxCoinsPicked"},new{frame=75,time=1.25f,eventName="OnTaxCoinsDeposited"},new{frame=120,time=2f,eventName="OnTaxBowlPicked"},new{frame=180,time=3f,eventName="OnTaxCollectionComplete"}}},
                new{name=Names[1],duration=1.2f,frames=72,markers=new[]{new{frame=30,time=.5f,eventName="OnDirectCoinsPicked"},new{frame=72,time=1.2f,eventName="OnTaxCollectionComplete"}}}
            }},Newtonsoft.Json.Formatting.Indented));
        AssetDatabase.Refresh();
        foreach(string guid in AssetDatabase.FindAssets("",new[]{Root})) {var a=AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));if(a)AssetDatabase.SaveAssetIfDirty(a);}
    }
    static void BuildOne()
    {
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var rig=new GameObject(Names[mode]+"Rig");
        var template=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Previous+"/Prefabs/ThreeFingers_CoinBowlPickupRig.prefab"));
        template.GetComponent<Animator>().enabled=false;
        original.SampleAnimation(template,0);
        opened=PoseDictionary(template.transform.Find("Hand"));
        original.SampleAnimation(template,32/60f);
        closed=PoseDictionary(template.transform.Find("Hand"));
        hand=template.transform.Find("Hand").gameObject;
        hand.transform.SetParent(rig.transform,false);
        foreach(var t in hand.GetComponentsInChildren<Transform>().Where(t=>t.name=="CoinBowlGrip").ToArray())UnityEngine.Object.DestroyImmediate(t.gameObject);
        UnityEngine.Object.DestroyImmediate(template);
        // Preserve the original bowl as an independent nested prefab.
        var bowl=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AntiqueCoinScale/Prefabs/WoodenCoinBowl.prefab"));
        bowl.name="Bowl";bowl.transform.SetParent(rig.transform,false);bowl.transform.localPosition=BowlStart;
        foreach(var rb in bowl.GetComponentsInChildren<Rigidbody>())rb.isKinematic=true;
        if(mode==0)
        {
            var scale=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AntiqueCoinScale/Prefabs/AntiqueBalanceScale.prefab"));
            scale.name="ScaleReference";scale.transform.SetParent(rig.transform,false);scale.transform.localPosition=new Vector3(-.17f,0,0);scale.transform.localScale=Vector3.one*.55f;
            foreach(var t in scale.GetComponentsInChildren<Transform>().Where(t=>t.name=="Coin stack bases").ToArray())UnityEngine.Object.DestroyImmediate(t.gameObject);
            var pan=scale.GetComponentsInChildren<Transform>().First(t=>t.name=="Left Pan");
            var floor=pan.GetComponentsInChildren<Transform>().First(t=>t.name=="Floor");
            var mesh=floor.GetComponent<MeshCollider>().sharedMesh;
            float floorTop=floor.TransformPoint(new Vector3(0,mesh.bounds.max.y,0)).y;
            coinStart=new Vector3(pan.position.x,floorTop+.0002f,pan.position.z);
        }
        else {coinStart=new Vector3(-.18f,.003f,0);bowl.SetActive(false);}
        coinHand=coinStart+new Vector3(0,.040f,.045f);
        depositHand=BowlStart+new Vector3(0,.140f,.045f); // Stack bottom .10 above rim before release.
        bowlHand=BowlStart+new Vector3(0,.090f,.138f);
        Pose(30);
        var coinGrip=NewAnchor(hand.transform,"CoinStackGrip",coinStart);
        Pose(120);
        var bowlGrip=NewAnchor(hand.transform,"BowlRimGrip",BowlStart+new Vector3(0,.073f,.080f));
        var bowlConstraint=bowl.AddComponent<ParentConstraint>();
        Configure(bowlConstraint,new[]{bowlGrip},new[]{BowlStart});
        var coins=new GameObject("PaymentCoins");coins.transform.SetParent(rig.transform,false);coins.transform.localPosition=coinStart;
        for(int i=0;i<6;i++)
        {
            var coin=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AntiqueCoinScale/Prefabs/SampleCoin.prefab"));
            coin.name="PaymentCoin_"+i;coin.transform.SetParent(coins.transform,false);coin.transform.localPosition=Vector3.up*(i*.0054f);coin.transform.localScale=new Vector3(1.4f,1.2f,1.4f);
            foreach(var rb in coin.GetComponentsInChildren<Rigidbody>())rb.isKinematic=true;
        }
        Pose(75);
        var coinConstraint=coins.AddComponent<ParentConstraint>();
        if(mode==0)Configure(coinConstraint,new[]{coinGrip,bowl.transform},new[]{coinGrip.position,BowlStart+Vector3.up*.10f});
        else Configure(coinConstraint,new[]{coinGrip},new[]{coinStart});
        // Source 0 is calibrated at pickup, never at the later deposit pose.
        Pose(30);coinConstraint.SetTranslationOffset(0,Vector3.zero);coinConstraint.SetRotationOffset(0,Vector3.zero);
        var clip=MakeClip(rig,bowlConstraint,coinConstraint);
        var controller=AnimatorController.CreateAnimatorControllerAtPath(Root+"/Animations/"+Names[mode]+".controller");
        var state=controller.layers[0].stateMachine.AddState("Collect_ThenHold");state.motion=clip;controller.layers[0].stateMachine.defaultState=state;
        var animator=rig.AddComponent<Animator>();animator.runtimeAnimatorController=controller;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.applyRootMotion=false;
        clip.SampleAnimation(rig,0);coins.transform.localPosition=coinStart;bowl.transform.localPosition=BowlStart;
        NewAnchor(rig.transform,"PlayerCoinStart",coinStart);
        NewAnchor(rig.transform,"NpcHandoff",Destination);
        PrefabUtility.SaveAsPrefabAsset(rig,Root+"/Prefabs/"+Names[mode]+"Rig.prefab");
        var stage=GameObject.CreatePrimitive(PrimitiveType.Cube);stage.name="PreviewTable";stage.transform.position=new Vector3(-.06f,-.018f,.07f);stage.transform.localScale=new Vector3(.85f,.025f,.65f);
        var wood=new Material(Shader.Find("Standard")){color=new Color(.28f,.13f,.045f)};AssetDatabase.CreateAsset(wood,Root+"/Materials/PreviewTable_"+mode+".mat");stage.GetComponent<Renderer>().sharedMaterial=wood;
        Camera("FrontCamera",new Vector3(-.15f,.62f,-.85f),new Vector3(-.08f,.17f,.10f));
        Camera("SideCamera",new Vector3(.85f,.46f,.16f),new Vector3(-.08f,.17f,.10f)).enabled=false;
        foreach(var p in new[]{new Vector3(-.4f,1,-.6f),new Vector3(.5f,.5f,.4f)}){var go=new GameObject("PreviewLight");go.transform.position=p;go.transform.LookAt(Vector3.zero);var l=go.AddComponent<Light>();l.type=LightType.Directional;l.intensity=p.y>.6f?.9f:.35f;}
        RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.3f,.3f,.3f);
        EditorSceneManager.SaveScene(scene,Root+"/Scenes/"+Names[mode]+"_Preview.unity");
    }
    static Dictionary<string,Quaternion> PoseDictionary(Transform h)=>h.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("DEF-")).ToDictionary(t=>t.name,t=>t.localRotation);
    static Transform NewAnchor(Transform parent,string name,Vector3 world){var t=new GameObject(name).transform;t.SetParent(parent,false);t.position=world;t.rotation=Quaternion.identity;return t;}
    static void Configure(ParentConstraint c,Transform[] sources,Vector3[] targetPositions)
    {
        c.translationAtRest=c.transform.localPosition;c.rotationAtRest=Vector3.zero;
        for(int i=0;i<sources.Length;i++){c.AddSource(new ConstraintSource{sourceTransform=sources[i],weight=i==0?1:0});c.SetTranslationOffset(i,Quaternion.Inverse(sources[i].rotation)*(targetPositions[i]-sources[i].position));c.SetRotationOffset(i,Quaternion.Inverse(sources[i].rotation).eulerAngles);}
        c.locked=true;c.constraintActive=true;c.weight=0;
    }
    static float Ease(float a,float b,float f){float u=Mathf.Clamp01((f-a)/(b-a));return u*u*u*(u*(u*6-15)+10);}
    static Vector3 Arc(Vector3 a,Vector3 b,float u,float height)=>Vector3.Lerp(a,b,u)+Vector3.up*(Mathf.Sin(Mathf.PI*u)*height);
    static void Pose(float f)
    {
        float curl=Ease(14,30,f);
        Vector3 p=Arc(coinHand+new Vector3(.015f,.15f,.08f),coinHand,Ease(0,30,f),.015f);
        if(mode==0)
        {
            if(f>=30&&f<=75)p=Arc(coinHand,depositHand,Ease(30,75,f),.20f);
            if(f>=75&&f<96){p=depositHand+Vector3.up*(.07f*Ease(80,96,f));curl=1-Ease(75,85,f);}
            if(f>=96&&f<=120){p=Arc(depositHand+Vector3.up*.07f,bowlHand,Ease(96,120,f),.045f);curl=Ease(106,120,f);}
            if(f>=120){p=bowlHand+Arc(Vector3.zero,Destination-BowlStart,Ease(120,180,f),.035f);curl=1;}
        }
        else if(f>=30){p=Arc(coinHand,Destination+new Vector3(0,.04f,.045f),Ease(30,72,f),.065f);curl=1;}
        hand.transform.localPosition=p;
        foreach(var t in hand.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("DEF-")))t.localRotation=Quaternion.Slerp(opened[t.name],closed[t.name],curl);
    }
    static AnimationClip MakeClip(GameObject rig,ParentConstraint bowl,ParentConstraint coins)
    {
        int end=mode==0?180:72;
        var clip=new AnimationClip{name=Names[mode],frameRate=60,wrapMode=WrapMode.ClampForever};
        var transforms=hand.GetComponentsInChildren<Transform>().Where(t=>t==hand.transform||t.name.StartsWith("DEF-")).ToArray();
        var curves=transforms.ToDictionary(t=>t,t=>Enumerable.Range(0,7).Select(_=>new AnimationCurve()).ToArray());
        for(int f=0;f<=end;f++){Pose(f);foreach(var t in transforms){var p=t.localPosition;var q=t.localRotation;float[] v={p.x,p.y,p.z,q.x,q.y,q.z,q.w};for(int j=0;j<7;j++)curves[t][j].AddKey(f/60f,v[j]);}}
        string[] properties={"m_LocalPosition.x","m_LocalPosition.y","m_LocalPosition.z","m_LocalRotation.x","m_LocalRotation.y","m_LocalRotation.z","m_LocalRotation.w"};
        foreach(var pair in curves)for(int j=0;j<7;j++){var c=pair.Value[j];for(int k=0;k<c.length;k++){AnimationUtility.SetKeyLeftTangentMode(c,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(c,k,AnimationUtility.TangentMode.Linear);}AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(pair.Key,rig.transform),typeof(Transform),properties[j]),c);}
        Step(clip,"PaymentCoins","m_Weight",new[]{0,29,30,end},new[]{0f,0,1,1});
        Step(clip,"Bowl","m_Weight",new[]{0,119,120,end},new[]{0f,0,1,1},mode==0);
        if(mode==0)
        {
            Step(clip,"PaymentCoins","m_Sources.Array.data[0].weight",new[]{0,74,75,end},new[]{1f,1,0,0});
            Step(clip,"PaymentCoins","m_Sources.Array.data[1].weight",new[]{0,74,75,end},new[]{0f,0,1,1});
            var fall=new AnimationCurve();for(int f=0;f<=end;f++){float u=Mathf.Clamp01((f-75)/12f);float y=Mathf.Lerp(.10f,.014f,u*u);fall.AddKey(f/60f,y);}AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("PaymentCoins",typeof(ParentConstraint),"m_TranslationOffsets.Array.data[1].y"),fall);
        }
        var markers=mode==0?new[]{(30,"OnTaxCoinsPicked"),(75,"OnTaxCoinsDeposited"),(120,"OnTaxBowlPicked"),(180,"OnTaxCollectionComplete")}:new[]{(30,"OnDirectCoinsPicked"),(72,"OnTaxCollectionComplete")};
        AnimationUtility.SetAnimationEvents(clip,markers.Select(m=>new AnimationEvent{time=m.Item1/60f,functionName=m.Item2,intParameter=m.Item1,messageOptions=SendMessageOptions.DontRequireReceiver}).ToArray());
        clip.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=false;AnimationUtility.SetAnimationClipSettings(clip,settings);
        AssetDatabase.CreateAsset(clip,Root+"/Animations/"+Names[mode]+".anim");return clip;
    }
    static void Step(AnimationClip clip,string path,string property,int[] frames,float[] values,bool enabled=true)
    {
        if(!enabled)return;var c=new AnimationCurve();for(int i=0;i<frames.Length;i++)c.AddKey(frames[i]/60f,values[i]);for(int i=0;i<c.length;i++){AnimationUtility.SetKeyLeftTangentMode(c,i,AnimationUtility.TangentMode.Constant);AnimationUtility.SetKeyRightTangentMode(c,i,AnimationUtility.TangentMode.Constant);}AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(path,typeof(ParentConstraint),property),c);
    }
    static Camera Camera(string name,Vector3 p,Vector3 target){var g=new GameObject(name);g.transform.position=p;g.transform.LookAt(target);var c=g.AddComponent<Camera>();c.orthographic=true;c.orthographicSize=.39f;c.backgroundColor=new Color(.075f,.09f,.11f);c.clearFlags=CameraClearFlags.SolidColor;return c;}
}
