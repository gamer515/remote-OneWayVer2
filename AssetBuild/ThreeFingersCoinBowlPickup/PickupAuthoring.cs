using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.SceneManagement;

public static class ThreeFingersPickupAuthoring
{
    const string Root = "Assets/ThreeFingersCoinBowlPickup";
    const string Output = "output/ThreeFingersCoinBowlPickup";
    const string HandSource = "Assets/Decision_YYS/Prafabs/Hand.prefab";
    const string BowlSource = "Assets/AntiqueCoinScale/Prefabs/WoodenCoinBowl.prefab";
    const float GrabTime = 32f / 60f;
    const float HandScale = .065f;
    static readonly Vector3 ContactPosition = new Vector3(0,.090f,.138f);
    static Dictionary<string,Quaternion> openPose;
    static Dictionary<string,Quaternion> thumbPose;
    static Dictionary<string,float[]> fingerAngles;

    public static string Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Authoring requires Edit mode.");
        Directory.CreateDirectory(Output);
        foreach (string folder in new[] { "Animations", "Prefabs", "Scenes" }) Directory.CreateDirectory(Root + "/" + folder);
        AssetDatabase.Refresh();
        fingerAngles=new Dictionary<string,float[]>
        {
            {"index",new[]{0f,55f,15f}},
            {"middle",new[]{10f,65f,10f}},
            {"pinky",new[]{15f,25f,10f}}
        };
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject rig = null;
        try
        {
            rig = new GameObject("ThreeFingers_CoinBowlPickupRig");
            SceneManager.MoveGameObjectToScene(rig,scene);
            var hand = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HandSource),rig.transform);
            hand.name = "Hand";
            foreach (var component in hand.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.DestroyImmediate(component);
            foreach (var component in hand.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(component);
            var sword = hand.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Sword");
            if (sword) UnityEngine.Object.DestroyImmediate(sword.gameObject);
            hand.transform.localScale = Vector3.one*HandScale;
            hand.transform.localRotation = Quaternion.Euler(-90,0,0);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UnityAssets/three-finger-hand_Paid_Unity.fbx");
            openPose = source.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("DEF-")).ToDictionary(t=>t.name,t=>t.localRotation);
            ResetPose(hand);
            hand.transform.localPosition = ContactPosition;
            MakeThumbPose(hand);
            var bowl = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(BowlSource),rig.transform);
            bowl.name = "Bowl";
            bowl.transform.localPosition = Vector3.zero;
            bowl.transform.localRotation = Quaternion.identity;
            AddCoins(bowl);
            PrefabUtility.SaveAsPrefabAsset(bowl,Root+"/Prefabs/CoinBowl_Independent.prefab");
            UnityEngine.Object.DestroyImmediate(bowl);
            bowl=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/CoinBowl_Independent.prefab"),rig.transform);
            bowl.name="Bowl";
            Pose(hand,GrabTime);
            var grip = new GameObject("CoinBowlGrip").transform;
            grip.SetParent(hand.transform,false);
            grip.position = new Vector3(0,.073f,.080f);
            grip.rotation = Quaternion.identity;
            var attachment = bowl.AddComponent<ParentConstraint>();
            attachment.AddSource(new ConstraintSource {sourceTransform=grip,weight=1});
            attachment.translationAtRest = Vector3.zero;
            attachment.rotationAtRest = Vector3.zero;
            attachment.SetTranslationOffset(0,Quaternion.Inverse(grip.rotation)*(bowl.transform.position-grip.position));
            attachment.SetRotationOffset(0,(Quaternion.Inverse(grip.rotation)*bowl.transform.rotation).eulerAngles);
            attachment.locked = true;
            attachment.constraintActive = true;
            attachment.weight = 0;
            var clip = CreateClip(rig,hand);
            var controllerPath = Root+"/Animations/ThreeFingers_CoinBowlPickup.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (!controller) controller=AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var machine = controller.layers[0].stateMachine;
            var state = machine.states.Length>0?machine.states[0].state:machine.AddState("Pickup_ThenHold");
            state.motion = clip;
            machine.defaultState = state;
            var animator = rig.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Sample(rig,clip,0);
            PrefabUtility.SaveAsPrefabAsset(rig,Root+"/Prefabs/ThreeFingers_CoinBowlPickupRig.prefab");
            RenderReview(scene,rig,clip);
            Validate(rig,hand,bowl,clip);
            SaveScene();
            foreach(string guid in AssetDatabase.FindAssets("",new[]{Root}))
                AssetDatabase.SaveAssetIfDirty(new GUID(guid));
        }
        finally
        {
            if (rig) UnityEngine.Object.DestroyImmediate(rig);
            EditorSceneManager.ClosePreviewScene(scene);
        }
        return "Built 60 fps, 1-second pickup; grip frame 32; front and side review rendered.";
    }

    static void ResetPose(GameObject hand)
    {
        foreach (var bone in hand.GetComponentsInChildren<Transform>())
            if (openPose.TryGetValue(bone.name,out var rotation)) bone.localRotation=rotation;
    }

    static Transform Bone(GameObject hand,string name) => hand.GetComponentsInChildren<Transform>().First(t=>t.name==name);

    static void MakeThumbPose(GameObject hand)
    {
        var joints=new[]{Bone(hand,"DEF-thumb.01.L"),Bone(hand,"DEF-thumb.02.L"),Bone(hand,"DEF-thumb.03.L")};
        Vector3 target=new Vector3(-.032f,.087f,.089f);
        Vector3 origin=joints[0].position;
        float first=Vector3.Distance(joints[0].position,joints[1].position);
        float second=Vector3.Distance(joints[1].position,joints[2].position);
        Vector3 direction=(target-origin).normalized;
        float distance=Mathf.Min(Vector3.Distance(target,origin),first+second-.0001f);
        float along=(first*first-second*second+distance*distance)/(2*distance);
        float height=Mathf.Sqrt(Mathf.Max(0,first*first-along*along));
        Vector3 pole=Vector3.ProjectOnPlane(Vector3.up,direction).normalized;
        Vector3 elbow=origin+direction*along+pole*height;
        joints[0].rotation=Quaternion.FromToRotation(joints[1].position-origin,elbow-origin)*joints[0].rotation;
        joints[1].rotation=Quaternion.FromToRotation(joints[2].position-joints[1].position,target-joints[1].position)*joints[1].rotation;
        Vector3 tipDirection=new Vector3(-.038f,.081f,.074f)-joints[2].position;
        joints[2].rotation=Quaternion.FromToRotation(joints[2].TransformVector(Vector3.up),tipDirection)*joints[2].rotation;
        thumbPose=joints.ToDictionary(t=>t.name,t=>t.localRotation);
        ResetPose(hand);
    }

    static void Pose(GameObject hand,float time)
    {
        ResetPose(hand);
        float curl=Smooth(.23f,.43f,time);
        foreach (string finger in new[]{"index","middle","pinky"})
            for (int joint=1;joint<=3;joint++)
            {
                var bone=Bone(hand,"DEF-f_"+finger+".0"+joint+".L");
                float[] angles;
                if (!fingerAngles.TryGetValue(finger,out angles)) angles=new[]{25f,45f,15f};
                float closed=angles[joint-1];
                bone.localRotation=openPose[bone.name]*Quaternion.Euler(Mathf.Lerp(5,closed,curl),0,0);
            }
        foreach (var pair in thumbPose) Bone(hand,pair.Key).localRotation=Quaternion.Slerp(openPose[pair.Key],pair.Value,curl);
        // A single eased arc avoids the old pause between forward travel and descent.
        float progress = Mathf.Clamp01(time / GrabTime);
        float u = progress * progress * progress * (progress * (progress * 6 - 15) + 10);
        float remaining = 1 - u;
        Vector3 start = new Vector3(0, .173f, .215f);
        Vector3 forward = new Vector3(0, .178f, .175f);
        Vector3 aboveRim = new Vector3(0, .155f, ContactPosition.z);
        Vector3 contact = remaining * remaining * remaining * start
            + 3 * remaining * remaining * u * forward
            + 3 * remaining * u * u * aboveRim
            + u * u * u * ContactPosition;
        hand.transform.localPosition=contact+Vector3.up*(.085f*Smooth(GrabTime,1,time));
    }

    static float Smooth(float start,float end,float value) => Mathf.SmoothStep(0,1,Mathf.InverseLerp(start,end,value));

    static AnimationClip CreateClip(GameObject rig,GameObject hand)
    {
        var clip=new AnimationClip {name="ThreeFingers_CoinBowlPickup",frameRate=60,wrapMode=WrapMode.ClampForever};
        var transforms=hand.GetComponentsInChildren<Transform>().Where(t=>t==hand.transform||t.name.StartsWith("DEF-")).ToArray();
        var curves=new Dictionary<string,AnimationCurve[]>();
        foreach (var transform in transforms)
            curves[AnimationUtility.CalculateTransformPath(transform,rig.transform)]=Enumerable.Range(0,7).Select(_=>new AnimationCurve()).ToArray();
        for (int frame=0;frame<=60;frame++)
        {
            float t=frame/60f;
            Pose(hand,t);
            foreach (var transform in transforms)
            {
                var channels=curves[AnimationUtility.CalculateTransformPath(transform,rig.transform)];
                var p=transform.localPosition;
                var q=transform.localRotation;
                float[] values={p.x,p.y,p.z,q.x,q.y,q.z,q.w};
                for (int i=0;i<7;i++) channels[i].AddKey(t,values[i]);
            }
        }
        string[] properties={"m_LocalPosition.x","m_LocalPosition.y","m_LocalPosition.z","m_LocalRotation.x","m_LocalRotation.y","m_LocalRotation.z","m_LocalRotation.w"};
        foreach(var pair in curves)
            for (int i=0;i<7;i++)
            {
                for (int k=0;k<pair.Value[i].length;k++)
                {
                    var tangent = pair.Key == "Hand" && i < 3
                        ? AnimationUtility.TangentMode.ClampedAuto
                        : AnimationUtility.TangentMode.Linear;
                    AnimationUtility.SetKeyLeftTangentMode(pair.Value[i],k,tangent);
                    AnimationUtility.SetKeyRightTangentMode(pair.Value[i],k,tangent);
                }
                AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(pair.Key,typeof(Transform),properties[i]),pair.Value[i]);
            }
        var weight=new AnimationCurve(new Keyframe(0,0),new Keyframe(GrabTime-1/60f,0),new Keyframe(GrabTime,1),new Keyframe(1,1));
        for (int k=0;k<weight.length;k++)
        {
            AnimationUtility.SetKeyLeftTangentMode(weight,k,AnimationUtility.TangentMode.Constant);
            AnimationUtility.SetKeyRightTangentMode(weight,k,AnimationUtility.TangentMode.Constant);
        }
        AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("Bowl",typeof(ParentConstraint),"m_Weight"),weight);
        clip.EnsureQuaternionContinuity();
        var settings=AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime=false;
        AnimationUtility.SetAnimationClipSettings(clip,settings);
        string path=Root+"/Animations/ThreeFingers_CoinBowlPickup.anim";
        var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if(existing){EditorUtility.CopySerialized(clip,existing);UnityEngine.Object.DestroyImmediate(clip);return existing;}
        AssetDatabase.CreateAsset(clip,path);
        return clip;
    }

    static void Sample(GameObject rig,AnimationClip clip,float time)
    {
        clip.SampleAnimation(rig,time);
        var bowl=rig.transform.Find("Bowl");
        var constraint=bowl.GetComponent<ParentConstraint>();
        if (time<GrabTime){bowl.localPosition=Vector3.zero;bowl.localRotation=Quaternion.identity;}
        else
        {
            var source=constraint.GetSource(0).sourceTransform;
            bowl.SetPositionAndRotation(source.position+source.rotation*constraint.GetTranslationOffset(0),source.rotation*Quaternion.Euler(constraint.GetRotationOffset(0)));
        }
    }

    static void AddCoins(GameObject bowl)
    {
        var coin=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AntiqueCoinScale/Prefabs/SampleCoin.prefab");
        var content=new GameObject("PickupCoinContents").transform;
        content.SetParent(bowl.transform,false);
        Vector3[] positions={new Vector3(-.025f,.0138f,-.016f),new Vector3(.026f,.0138f,-.015f),new Vector3(0,.0138f,.030f)};
        for(int i=0;i<positions.Length;i++)
        {
            var copy=UnityEngine.Object.Instantiate(coin,content);
            copy.name="ContainedCoin_"+(i+1);
            copy.transform.localPosition=positions[i];
            copy.transform.localRotation=Quaternion.Euler(0,i*47,0);
            foreach(var body in copy.GetComponentsInChildren<Rigidbody>())body.isKinematic=true;
        }
        foreach(var body in bowl.GetComponentsInChildren<Rigidbody>())body.isKinematic=true;
    }

    static void RenderReview(Scene scene,GameObject rig,AnimationClip clip)
    {
        var cameraObject=new GameObject("ReviewCamera");
        SceneManager.MoveGameObjectToScene(cameraObject,scene);
        var camera=cameraObject.AddComponent<Camera>();
        camera.scene=scene;
        camera.orthographic=true;
        camera.orthographicSize=.165f;
        camera.clearFlags=CameraClearFlags.SolidColor;
        camera.backgroundColor=new Color(.10f,.12f,.14f);
        camera.cullingMask=1<<30;
        foreach(var t in rig.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
        foreach(var position in new[]{new Vector3(-.3f,.6f,-.4f),new Vector3(.4f,.25f,.3f)})
        {
            var go=new GameObject("ReviewLight");
            SceneManager.MoveGameObjectToScene(go,scene);
            go.transform.position=position;
            go.transform.LookAt(new Vector3(0,.08f,.04f));
            var light=go.AddComponent<Light>();
            light.type=LightType.Directional;
            light.intensity=position.y>.3f?1.5f:.6f;
            light.cullingMask=1<<30;
        }
        float[] times={0,.23f,.43f,GrabTime,.75f,1};
        var sheet=new Texture2D(2400,800,TextureFormat.RGB24,false);
        try
        {
            for(int view=0;view<2;view++)
                for(int i=0;i<times.Length;i++)
                {
                    Sample(rig,clip,times[i]);
                    camera.transform.position=view==0?new Vector3(0,.36f,-.32f):new Vector3(.4f,.16f,.055f);
                    camera.transform.LookAt(new Vector3(0,.085f,.06f));
                    var rt=new RenderTexture(400,400,24);
                    rt.antiAliasing=4;rt.Create();camera.targetTexture=rt;camera.Render();
                    var previous=RenderTexture.active;
                    RenderTexture.active=rt;
                    var image=new Texture2D(400,400,TextureFormat.RGB24,false);
                    image.ReadPixels(new Rect(0,0,400,400),0,0);image.Apply();RenderTexture.active=previous;
                    sheet.SetPixels(i*400,(1-view)*400,400,400,image.GetPixels());
                    File.WriteAllBytes(Output+"/"+(view==0?"front":"side")+"_"+Mathf.RoundToInt(times[i]*60).ToString("00")+".png",image.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(image);camera.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);
                }
            sheet.Apply();File.WriteAllBytes(Output+"/contact-sheet.png",sheet.EncodeToPNG());
        }
        finally{UnityEngine.Object.DestroyImmediate(sheet);UnityEngine.Object.DestroyImmediate(cameraObject);}
    }

    static void Validate(GameObject rig,GameObject hand,GameObject bowl,AnimationClip clip)
    {
        var skin=hand.GetComponentInChildren<SkinnedMeshRenderer>();
        var baked=new Mesh();
        var collision=bowl.GetComponentsInChildren<BoxCollider>().Where(c=>c.name.StartsWith("Wall ")).ToArray();
        var records=new List<object>();
        try
        {
            for(int frame=0;frame<=60;frame++)
            {
                Sample(rig,clip,frame/60f);skin.BakeMesh(baked,true);int inside=0;
                foreach(var vertex in baked.vertices)
                {
                    Vector3 world=skin.transform.TransformPoint(vertex);
                    foreach(var wall in collision)
                    {
                        Vector3 local=wall.transform.InverseTransformPoint(world)-wall.center;
                        Vector3 half=wall.size*.5f-Vector3.one*.00025f;
                        if(Mathf.Abs(local.x)<half.x&&Mathf.Abs(local.y)<half.y&&Mathf.Abs(local.z)<half.z){inside++;break;}
                    }
                }
                records.Add(new{frame,wallInteriorVertices=inside,bowlY=bowl.transform.position.y});
            }
            File.WriteAllText(Output+"/geometry-check.json",Newtonsoft.Json.JsonConvert.SerializeObject(records,Newtonsoft.Json.Formatting.Indented));
        }
        finally{UnityEngine.Object.DestroyImmediate(baked);}
    }

    static void SaveScene()
    {
        var previous=SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
            PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/ThreeFingers_CoinBowlPickupRig.prefab"),scene);
            var cameraObject=new GameObject("FrontCamera");
            SceneManager.MoveGameObjectToScene(cameraObject,scene);cameraObject.tag="MainCamera";
            var camera=cameraObject.AddComponent<Camera>();
            camera.orthographic=true;camera.orthographicSize=.165f;
            camera.transform.position=new Vector3(0,.36f,-.32f);camera.transform.LookAt(new Vector3(0,.085f,.06f));
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.10f,.12f,.14f);
            var side=UnityEngine.Object.Instantiate(cameraObject);
            side.name="SideCamera";side.tag="Untagged";SceneManager.MoveGameObjectToScene(side,scene);
            side.transform.position=new Vector3(.4f,.16f,.055f);side.transform.LookAt(new Vector3(0,.085f,.06f));side.GetComponent<Camera>().enabled=false;
            foreach(var position in new[]{new Vector3(-.3f,.6f,-.4f),new Vector3(.4f,.25f,.3f)})
            {
                var lightObject=new GameObject("PickupPreviewLight");SceneManager.MoveGameObjectToScene(lightObject,scene);
                lightObject.transform.position=position;lightObject.transform.LookAt(new Vector3(0,.08f,.04f));
                var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=position.y>.3f?1.5f:.6f;
            }
            EditorSceneManager.SaveScene(scene,Root+"/Scenes/ThreeFingers_CoinBowlPickup_Preview.unity");
        }
        finally {EditorSceneManager.CloseScene(scene,true);if(previous.IsValid())SceneManager.SetActiveScene(previous);}
    }

    public static string Optimize()
    {
        var scene=EditorSceneManager.NewPreviewScene();
        var rig=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/ThreeFingers_CoinBowlPickupRig.prefab"));
        SceneManager.MoveGameObjectToScene(rig,scene);
        var baked=new Mesh();
        try
        {
            rig.GetComponent<Animator>().enabled=false;
            var hand=rig.transform.Find("Hand").gameObject;
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UnityAssets/three-finger-hand_Paid_Unity.fbx");
            openPose=source.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("DEF-")).ToDictionary(t=>t.name,t=>t.localRotation);
            ResetPose(hand);hand.transform.localPosition=ContactPosition;MakeThumbPose(hand);
            fingerAngles=new Dictionary<string,float[]>();
            var skin=hand.GetComponentInChildren<SkinnedMeshRenderer>();
            var weights=skin.sharedMesh.boneWeights;
            var walls=rig.transform.Find("Bowl").GetComponentsInChildren<BoxCollider>().Where(c=>c.name.StartsWith("Wall ")).ToArray();
            foreach(string finger in new[]{"index","middle","pinky"})
            {
                var vertices=Enumerable.Range(0,weights.Length).Where(i=>skin.bones[weights[i].boneIndex0].name.Contains("f_"+finger)).ToArray();
                float best=float.PositiveInfinity;
                float[] winner=null;
                foreach(float first in new[]{0f,5f,10f,15f,20f,25f,30f})
                    foreach(float second in new[]{25f,35f,45f,55f,65f})
                        foreach(float third in new[]{0f,5f,10f,15f})
                        {
                            fingerAngles[finger]=new[]{first,second,third};
                            Pose(hand,GrabTime);skin.BakeMesh(baked,true);
                            var points=baked.vertices;
                            int inside=0,near=0;
                            float closest=.1f;
                            foreach(int index in vertices)
                            {
                                Vector3 p=skin.transform.TransformPoint(points[index]);
                                if(p.y>.074f||p.y<.035f)continue;
                                float distance=.1f;
                                foreach(var wall in walls)
                                {
                                    Vector3 q=wall.transform.InverseTransformPoint(p)-wall.center;
                                    Vector3 d=new Vector3(Mathf.Abs(q.x),Mathf.Abs(q.y),Mathf.Abs(q.z))-wall.size*.5f;
                                    if(d.x<-.00025f&&d.y<-.00025f&&d.z<-.00025f)inside++;
                                    float gap=new Vector3(Mathf.Max(0,d.x),Mathf.Max(0,d.y),Mathf.Max(0,d.z)).magnitude;
                                    distance=Mathf.Min(distance,gap);
                                }
                                closest=Mathf.Min(closest,distance);
                                if(distance>.0002f&&distance<.0025f)near++;
                            }
                            var tip=Bone(hand,"DEF-f_"+finger+".03.L");
                            float y=tip.TransformPoint(new Vector3(0,.00030f,0)).y;
                            float score=inside*100+Mathf.Max(0,.042f-y)*1000+Mathf.Max(0,y-.065f)*1000+Mathf.Abs(closest-.0007f)*1000-Mathf.Min(near,30)*.07f;
                            if(score<best){best=score;winner=new[]{first,second,third};}
                        }
                fingerAngles[finger]=winner;
            }
            File.WriteAllText(Output+"/finger-tuning.json",Newtonsoft.Json.JsonConvert.SerializeObject(fingerAngles,Newtonsoft.Json.Formatting.Indented));
            return File.ReadAllText(Output+"/finger-tuning.json");
        }
        finally{UnityEngine.Object.DestroyImmediate(baked);UnityEngine.Object.DestroyImmediate(rig);EditorSceneManager.ClosePreviewScene(scene);}
    }

    public static string PrepareIsolatedProject()
    {
        const string project="AssetBuild/ThreeFingersCoinBowlPickup/ValidationProject";
        Directory.CreateDirectory(project+"/Assets/Editor");
        Directory.CreateDirectory(project+"/Packages");
        Directory.CreateDirectory(project+"/ProjectSettings");
        var authoringInputs = new[]
        {
            Root+"/Scenes/ThreeFingers_CoinBowlPickup_Preview.unity",
            HandSource, BowlSource,
            "Assets/UnityAssets/three-finger-hand_Paid_Unity.fbx",
            "Assets/AntiqueCoinScale/Prefabs/SampleCoin.prefab"
        };
        foreach(string path in AssetDatabase.GetDependencies(authoringInputs,true))
        {
            if(!path.StartsWith("Assets/") || path.EndsWith(".cs"))continue;
            string target=project+"/"+path;
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(path,target,true);
            if(File.Exists(path+".meta"))File.Copy(path+".meta",target+".meta",true);
        }
        var materialPaths=AssetDatabase.GetDependencies(Root+"/Scenes/ThreeFingers_CoinBowlPickup_Preview.unity",true).Where(p=>p.EndsWith(".mat"));
        var colors=materialPaths.Select(p=>
        {
            var material=AssetDatabase.LoadAssetAtPath<Material>(p);
            var color=material.color;
            return new{path=p,r=color.r,g=color.g,b=color.b,a=color.a,texture=AssetDatabase.GetAssetPath(material.mainTexture)};
        }).ToArray();
        File.WriteAllText(project+"/Assets/Editor/MaterialCatalog.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{materials=colors}));
        File.Copy("AssetBuild/ThreeFingersCoinBowlPickup/PickupPlayCheck.cs",project+"/Assets/Editor/PickupPlayCheck.cs",true);
        File.Copy("AssetBuild/ThreeFingersCoinBowlPickup/PickupAuthoring.cs",project+"/Assets/Editor/PickupAuthoring.cs",true);
        File.Copy("ProjectSettings/ProjectVersion.txt",project+"/ProjectSettings/ProjectVersion.txt",true);
        var jsonPackage = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(Newtonsoft.Json.JsonConvert).Assembly);
        var packages = new Dictionary<string,string>
        {
            {"com.unity.modules.animation","1.0.0"},
            {"com.unity.modules.physics","1.0.0"},
            {"com.unity.modules.imageconversion","1.0.0"},
            {"com.unity.nuget.newtonsoft-json","file:"+jsonPackage.resolvedPath.Replace('\\','/')}
        };
        File.WriteAllText(project+"/Packages/manifest.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{dependencies=packages}));
        return Path.GetFullPath(project);
    }
}
