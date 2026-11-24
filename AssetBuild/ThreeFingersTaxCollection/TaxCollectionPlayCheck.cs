using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;

// Isolated-project-only validator: normal Animator playback, then native-constraint frame sampling.
[InitializeOnLoad]
public static class TaxCollectionPlayCheck
{
    static int mode,frame;
    static Animator animator;
    static GameObject rig;
    static Transform bowl,coins;
    static Camera front,side;
    static bool autoplay;
    static double started;
    static Vector3 initialCoins,previousCoins,previousBowl;
    static readonly List<object> records=new List<object>();
    static readonly List<object> results=new List<object>();
    static string Output=>TaxCollectionAuthoring.Output;
    static int End=>mode==0?180:72;
    static TaxCollectionPlayCheck(){EditorApplication.playModeStateChanged+=State;}
    public static void BuildAndStart()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/ThreeFingersCoinBowlPickup/Scenes/ThreeFingers_CoinBowlPickup_Preview.unity");
            TaxCollectionAuthoring.Build();
            var catalog=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText("Assets/Editor/MaterialCatalog.json"));
            foreach(var r in catalog["materials"])
            {
                var m=AssetDatabase.LoadAssetAtPath<Material>((string)r["path"]);m.shader=Shader.Find("Standard");m.color=new Color((float)r["r"],(float)r["g"],(float)r["b"],(float)r["a"]);
                string tex=(string)r["texture"];m.mainTexture=string.IsNullOrEmpty(tex)?null:AssetDatabase.LoadAssetAtPath<Texture>(tex);m.SetFloat("_Glossiness",.35f);
            }
            EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;Application.runInBackground=true;
            Begin();
        }
        catch(Exception e){Fail(e);}
    }
    static void Begin(){EditorSceneManager.OpenScene(TaxCollectionAuthoring.Root+"/Scenes/"+TaxCollectionAuthoring.Names[mode]+"_Preview.unity");EditorApplication.isPlaying=true;}
    static void State(PlayModeStateChange s)
    {
        if(s==PlayModeStateChange.EnteredEditMode&&mode==1){EditorApplication.delayCall+=Begin;return;}
        if(s!=PlayModeStateChange.EnteredPlayMode)return;
        try
        {
            rig=GameObject.Find(TaxCollectionAuthoring.Names[mode]+"Rig");animator=rig.GetComponent<Animator>();animator.speed=1;
            bowl=rig.transform.Find("Bowl");coins=rig.transform.Find("PaymentCoins");initialCoins=coins.position;previousCoins=initialCoins;previousBowl=bowl.position;
            front=GameObject.Find("FrontCamera").GetComponent<Camera>();side=GameObject.Find("SideCamera").GetComponent<Camera>();front.enabled=false;side.enabled=false;
            frame=-1;autoplay=false;started=EditorApplication.timeSinceStartup;records.Clear();
            Directory.CreateDirectory(Output+"/"+(mode==0?"scale":"direct"));EditorApplication.update+=Tick;
        }
        catch(Exception e){Fail(e);}
    }
    static Vector3 Expected(ParentConstraint c)
    {
        Vector3 p=Vector3.zero;float total=0;
        for(int i=0;i<c.sourceCount;i++){var s=c.GetSource(i);p+=(s.sourceTransform.position+s.sourceTransform.rotation*c.GetTranslationOffset(i))*s.weight;total+=s.weight;}
        return total>0?p/total:c.translationAtRest;
    }
    static void Tick()
    {
        if(!EditorApplication.isPlaying)return;
        try
        {
            if(!autoplay)
            {
                if(animator.GetCurrentAnimatorStateInfo(0).normalizedTime<1.05f){if(EditorApplication.timeSinceStartup-started>20)throw new Exception("Animator failed to advance");return;}
                Vector3 target=mode==0?TaxCollectionAuthoring.Destination:TaxCollectionAuthoring.Destination;
                float e=Vector3.Distance(mode==0?bowl.position:coins.position,target);
                if(e>.0002f)throw new Exception("Autoplay final destination mismatch "+e);
                autoplay=true;animator.speed=0;
            }
            if(frame>=0)
            {
                var cc=coins.GetComponent<ParentConstraint>();var bc=bowl.GetComponent<ParentConstraint>();
                float ce=Vector3.Distance(coins.position,frame<30?initialCoins:Expected(cc));
                float be=mode==0?Vector3.Distance(bowl.position,frame<120?TaxCollectionAuthoring.BowlStart:Expected(bc)):0;
                float jump=frame==30||frame==75||frame==120?Vector3.Distance(coins.position,previousCoins):0;
                float bowlJump=frame==120?Vector3.Distance(bowl.position,previousBowl):0;
                float tilt=mode==0?Quaternion.Angle(bowl.rotation,Quaternion.identity):0;
                int wallInside=0;
                if(mode==0&&frame>=96&&frame<=180)
                {
                    var skin=rig.transform.Find("Hand").GetComponentInChildren<SkinnedMeshRenderer>();var mesh=new Mesh();skin.BakeMesh(mesh,true);
                    var walls=bowl.GetComponentsInChildren<BoxCollider>().Where(c=>c.name.StartsWith("Wall ")).ToArray();
                    foreach(var v in mesh.vertices){var p=skin.transform.TransformPoint(v);foreach(var wall in walls){var q=wall.transform.InverseTransformPoint(p)-wall.center;var h=wall.size*.5f-Vector3.one*.00025f;if(Mathf.Abs(q.x)<h.x&&Mathf.Abs(q.y)<h.y&&Mathf.Abs(q.z)<h.z){wallInside++;break;}}}
                    UnityEngine.Object.DestroyImmediate(mesh);
                }
                records.Add(new{frame,time=frame/60f,coins=new[]{coins.position.x,coins.position.y,coins.position.z},bowl=new[]{bowl.position.x,bowl.position.y,bowl.position.z},coinContactError=ce,bowlContactError=be,switchJump=jump,bowlSwitchJump=bowlJump,bowlTilt=tilt,wallInteriorVertices=wallInside});
                if(ce>.0002f||be>.0002f||jump>.002f||bowlJump>.0002f||tilt>.01f||wallInside>0)throw new Exception("Contact validation failed at "+frame+" coin="+ce+" bowl="+be+" jump="+jump+" wall="+wallInside);
                previousCoins=coins.position;previousBowl=bowl.position;
                if(frame<=End&&(frame%3==0||new[]{30,75,120,End}.Contains(frame))){Capture(front,"front");Capture(side,"side");}
            }
            frame++;
            if(frame>End+12)
            {
                var result=new{clip=TaxCollectionAuthoring.Names[mode],passed=true,autoplayVerified=autoplay,samples=records.ToArray()};results.Add(result);
                File.WriteAllText(Output+"/"+(mode==0?"scale":"direct")+"/play-check.json",Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));
                EditorApplication.update-=Tick;
                if(mode==0){mode=1;EditorApplication.isPlaying=false;return;}
                File.WriteAllText(Output+"/validation.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{passed=true,results},Newtonsoft.Json.Formatting.Indented));EditorApplication.Exit(0);return;
            }
            animator.Play("Collect_ThenHold",0,Mathf.Min(frame/(float)End,1));animator.Update(0);
        }
        catch(Exception e){Fail(e);}
    }
    static void Capture(Camera camera,string view)
    {
        var rt=new RenderTexture(720,600,24);rt.antiAliasing=4;rt.Create();camera.targetTexture=rt;camera.Render();
        var old=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(720,600,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,720,600),0,0);image.Apply();RenderTexture.active=old;
        File.WriteAllBytes(Output+"/"+(mode==0?"scale":"direct")+"/"+view+"_"+frame.ToString("000")+".png",image.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(image);camera.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);
    }
    static void Fail(Exception e){Directory.CreateDirectory(Output);File.WriteAllText(Output+"/validation.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{passed=false,error=e.ToString(),mode,frame,records},Newtonsoft.Json.Formatting.Indented));EditorApplication.update-=Tick;EditorApplication.Exit(2);}
}
