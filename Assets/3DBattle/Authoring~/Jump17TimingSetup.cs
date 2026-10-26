using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;
public static class Jump17TimingSetup
{
    public static string Run()
    {
        if(Application.isPlaying || AnimationMode.InAnimationMode()) throw new Exception("Stop Play and animation preview first");
        var c=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        if(c.gameObject.scene.path!="Assets/Scenes/3DBattleScene.unity") throw new Exception("Wrong scene");
        var b=c.FindStage(1).boss;
        var old=b.GetComponents<BossJumpTimingDebug>().First(x=>x.GetType()==typeof(BossJumpTimingDebug));
        float takeoff12=old.takeoffTime, landing12=old.landingTime;
        var d=b.GetComponent<BossJump17TimingDebug>();
        if(d==null) d=Undo.AddComponent<BossJump17TimingDebug>(b.gameObject);
        d.animator=b.animator;
        d.clip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/3DBattle/Boss1/Jump_with_Arms_Open_Boss1.anim");
        d.leftFoot=old.leftFoot; d.rightFoot=old.rightFoot;
        var motion=b.GetComponent<BossJumpMotionDebug>();
        Undo.RecordObject(motion,"Connect jump 17 timing"); motion.jump17Timing=d;
        var bones=d.animator.GetComponentsInChildren<Transform>(true);
        var pos=bones.Select(x=>x.localPosition).ToArray();
        var rot=bones.Select(x=>x.localRotation).ToArray();
        var scale=bones.Select(x=>x.localScale).ToArray();
        var e=UnityEditor.Editor.CreateEditor(d);
        var flags=BindingFlags.NonPublic|BindingFlags.Instance;
        bool moved=false;
        try
        {
            var seek=e.GetType().GetMethod("Seek",flags);
            if(seek==null) throw new Exception("Shared inspector not selected");
            seek.Invoke(e,new object[]{0f});
            var start=bones.Select(x=>x.localRotation).ToArray();
            for(int i=1;i<8;i++)
            {
                seek.Invoke(e,new object[]{d.clip.length*i/8f});
                moved |= bones.Where((x,j)=>Quaternion.Angle(start[j],x.localRotation)>.1f).Any();
                if(Vector3.Distance(bones[0].localPosition,pos[0])>.001f) throw new Exception("Root moved");
            }
            e.GetType().GetMethod("Stop",flags).Invoke(e,null);
            if(!moved) throw new Exception("No bone movement");
            for(int i=0;i<bones.Length;i++)
                if(Vector3.Distance(pos[i],bones[i].localPosition)>.001f || Quaternion.Angle(rot[i],bones[i].localRotation)>.01f || Vector3.Distance(scale[i],bones[i].localScale)>.001f) throw new Exception("Pose restoration failed");
        }
        finally { UnityEngine.Object.DestroyImmediate(e); }
        if(old.takeoffTime!=takeoff12 || old.landingTime!=landing12) throw new Exception("12 timing changed");
        EditorUtility.SetDirty(d); EditorUtility.SetDirty(motion);
        EditorSceneManager.MarkSceneDirty(c.gameObject.scene); EditorSceneManager.SaveScene(c.gameObject.scene);
        Selection.activeGameObject=b.gameObject;
        return "PASS: 17 frame preview, fixed root, restored pose; 12 timings preserved="+takeoff12+"/"+landing12+"; 17 length="+d.clip.length+", FPS="+d.clip.frameRate;
    }
}
