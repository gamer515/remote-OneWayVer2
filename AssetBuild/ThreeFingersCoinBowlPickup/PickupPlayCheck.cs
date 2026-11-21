using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;

// Copied into the isolated validation project's Editor folder, never into the game's Assets.
[InitializeOnLoad]
public static class PickupPlayCheck
{
    const string ScenePath="Assets/ThreeFingersCoinBowlPickup/Scenes/ThreeFingers_CoinBowlPickup_Preview.unity";
    static GameObject rig;
    static Animator animator;
    static Transform bowl;
    static Transform grip;
    static Camera front,side;
    static int frame;
    static Vector3 offset;
    static Vector3[] coinPositions;
    static bool autoplayVerified;
    static double autoplayStart;
    static Texture2D contactSheet;
    static readonly List<FrameRecord> records=new List<FrameRecord>();
    static string Output => Path.GetFullPath("../../../output/ThreeFingersCoinBowlPickup/runtime");

    [Serializable] sealed class FrameRecord
    {
        public int frame;
        public float time, bowlY, attachmentWeight, horizontalAngle, contactError, coinError;
    }
    [Serializable] sealed class Result
    {
        public bool passed;
        public string message;
        public bool autoplayVerified;
        public FrameRecord[] frames;
    }
    [Serializable] sealed class MaterialCatalog
    {
        public MaterialRecord[] materials;
    }
    [Serializable] sealed class MaterialRecord
    {
        public string path,texture;
        public float r,g,b,a;
    }

    static PickupPlayCheck()
    {
        EditorApplication.playModeStateChanged+=OnPlayState;
    }

    public static void Start()
    {
        Directory.CreateDirectory(Output);
        // The isolated project uses its built-in renderer for deterministic batch captures.
        var catalog=JsonUtility.FromJson<MaterialCatalog>(File.ReadAllText("Assets/Editor/MaterialCatalog.json"));
        foreach(var record in catalog.materials)
        {
            var material=AssetDatabase.LoadAssetAtPath<Material>(record.path);
            material.shader=Shader.Find("Standard");
            material.color=new Color(record.r,record.g,record.b,record.a);
            material.mainTexture=string.IsNullOrEmpty(record.texture)?null:AssetDatabase.LoadAssetAtPath<Texture>(record.texture);
            material.SetFloat("_Metallic",0);
            material.SetFloat("_Glossiness",.35f);
        }
        EditorSceneManager.OpenScene(ScenePath);
        EditorSettings.enterPlayModeOptionsEnabled=true;
        EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
        Application.runInBackground=true;
        EditorApplication.isPlaying=true;
    }

    public static void BuildAndStart()
    {
        try
        {
            EditorSceneManager.OpenScene(ScenePath);
            ThreeFingersPickupAuthoring.Build();
            Start();
        }
        catch (Exception error) { Fail(error); }
    }

    static void OnPlayState(PlayModeStateChange state)
    {
        if(state!=PlayModeStateChange.EnteredPlayMode)return;
        try
        {
            rig=GameObject.Find("ThreeFingers_CoinBowlPickupRig");
            animator=rig.GetComponent<Animator>();
            animator.speed=1;
            bowl=rig.transform.Find("Bowl");
            grip=bowl.GetComponent<ParentConstraint>().GetSource(0).sourceTransform;
            var coins=bowl.Find("PickupCoinContents");
            coinPositions=new Vector3[coins.childCount];
            for(int i=0;i<coins.childCount;i++)coinPositions[i]=coins.GetChild(i).localPosition;
            offset=new Vector3(0,-.073f,-.080f);
            front=GameObject.Find("FrontCamera").GetComponent<Camera>();
            side=GameObject.Find("SideCamera").GetComponent<Camera>();
            front.enabled=false;
            frame=-1;
            contactSheet=new Texture2D(3840,1280,TextureFormat.RGB24,false);
            autoplayStart=EditorApplication.timeSinceStartup;
            EditorApplication.update+=Tick;
        }
        catch(Exception error){Fail(error);}
    }

    static void Tick()
    {
        if(!EditorApplication.isPlaying)return;
        try
        {
            if (!autoplayVerified)
            {
                float elapsed=animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                if (elapsed>=1.05f)
                {
                    if(Mathf.Abs(bowl.position.y-.085f)>.0001f)throw new Exception("Autoplay did not finish at lift position");
                    autoplayVerified=true;
                    animator.speed=0;
                }
                else
                {
                    if(EditorApplication.timeSinceStartup-autoplayStart>15)throw new Exception("Autoplay failed to advance");
                    return;
                }
            }
            if(frame>=0)
            {
                float time=frame/60f;
                var constraint=bowl.GetComponent<ParentConstraint>();
                float error=frame>=32?Vector3.Distance(bowl.position,grip.position+grip.rotation*offset):bowl.position.magnitude;
                float coinError=0;
                var coins=bowl.Find("PickupCoinContents");
                for(int i=0;i<coins.childCount;i++)coinError=Mathf.Max(coinError,Vector3.Distance(coinPositions[i],coins.GetChild(i).localPosition));
                records.Add(new FrameRecord{frame=frame,time=time,bowlY=bowl.position.y,attachmentWeight=constraint.weight,horizontalAngle=Quaternion.Angle(bowl.rotation,Quaternion.identity),contactError=error,coinError=coinError});
                if(error>.0001f)throw new Exception("Native grip drift at frame "+frame+": "+error);
                if(frame<32 && bowl.position.sqrMagnitude>1e-9f)throw new Exception("Bowl moved before grip");
                if(coinError>1e-6f)throw new Exception("Coin slipped from bowl");
                if(frame%2==0 && frame<=60)
                {
                    Capture(front,"front_"+frame.ToString("00"));
                    Capture(side,"side_"+frame.ToString("00"));
                }
            }
            frame++;
            if(frame>72)
            {
                if(Mathf.Abs(bowl.position.y-.085f)>.0001f)throw new Exception("Final lift height differs");
                var result=new Result{passed=true,autoplayVerified=autoplayVerified,message="Normal Animator autoplay and 73 native constraint samples passed, including end hold",frames=records.ToArray()};
                File.WriteAllText(Path.Combine(Output,"play-check.json"),JsonUtility.ToJson(result,true));
                contactSheet.Apply();
                File.WriteAllBytes(Path.Combine(Output,"contact-sheet.png"),contactSheet.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(contactSheet);
                EditorApplication.update-=Tick;
                EditorApplication.Exit(0);
                return;
            }
            animator.Play("Pickup_ThenHold",0,Mathf.Min(frame/60f,1));
            animator.Update(0);
        }
        catch(Exception error){Fail(error);}
    }

    static void Capture(Camera camera,string name)
    {
        var rt=new RenderTexture(640,640,24);
        rt.antiAliasing=4;rt.Create();camera.targetTexture=rt;camera.Render();
        var previous=RenderTexture.active;RenderTexture.active=rt;
        var image=new Texture2D(640,640,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,640,640),0,0);image.Apply();RenderTexture.active=previous;
        int index=Array.IndexOf(new[]{0,12,24,32,44,60},frame);
        if (index>=0 && contactSheet!=null)
            contactSheet.SetPixels(index*640,camera==front?640:0,640,640,image.GetPixels());
        File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(image);camera.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);
    }

    static void Fail(Exception error)
    {
        Directory.CreateDirectory(Output);
        File.WriteAllText(Path.Combine(Output,"play-check.json"),JsonUtility.ToJson(new Result{passed=false,message=error.ToString(),frames=records.ToArray()},true));
        EditorApplication.update-=Tick;
        EditorApplication.Exit(2);
    }
}
