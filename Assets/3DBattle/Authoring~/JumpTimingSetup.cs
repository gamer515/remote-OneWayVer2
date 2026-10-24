using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;
public static class JumpTimingSetup
{
    public static string Run()
    {
        if(Application.isPlaying) throw new Exception("Edit mode required");
        var c=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        if(c.gameObject.scene.path!="Assets/Scenes/3DBattleScene.unity") throw new Exception("Wrong scene");
        var boss=c.FindStage(1).boss;
        var clip=boss.animator.runtimeAnimatorController.animationClips.First(a=>a.name.Split('|').Last()=="Basic_Jump");
        var d=boss.GetComponent<BossJumpTimingDebug>();
        if(d==null) d=Undo.AddComponent<BossJumpTimingDebug>(boss.gameObject);
        Undo.RecordObject(d,"Configure jump timing debug");
        d.animator=boss.animator; d.clip=clip;
        var bones=d.animator.GetComponentsInChildren<Transform>(true);
        d.leftFoot=bones.FirstOrDefault(b=>b.name.EndsWith("LeftFoot",StringComparison.OrdinalIgnoreCase));
        d.rightFoot=bones.FirstOrDefault(b=>b.name.EndsWith("RightFoot",StringComparison.OrdinalIgnoreCase));
        var positions=bones.Select(b=>b.localPosition).ToArray();
        var rotations=bones.Select(b=>b.localRotation).ToArray();
        var scales=bones.Select(b=>b.localScale).ToArray();
        var editor=UnityEditor.Editor.CreateEditor(d);
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        try
        {
            var seek=editor.GetType().GetMethod("Seek",flags);
            seek.Invoke(editor,new object[]{0f});
            var pose=bones.Select(b=>b.localRotation).ToArray();
            bool moved=false;
            for(int frame=1;frame<=6;frame++)
            {
                seek.Invoke(editor,new object[]{clip.length*frame/7f});
                moved |= bones.Where((b,i)=>Quaternion.Angle(pose[i],b.localRotation)>.1f).Any();
                if(Vector3.Distance(d.animator.transform.localPosition,positions[0])>.001f) throw new Exception("Root moved");
            }
            if(!moved) throw new Exception("Clip did not animate bones");
            editor.GetType().GetMethod("Stop",flags).Invoke(editor,null);
            for(int i=0;i<bones.Length;i++)
                if(Vector3.Distance(positions[i],bones[i].localPosition)>.001f || Quaternion.Angle(rotations[i],bones[i].localRotation)>.01f || Vector3.Distance(scales[i],bones[i].localScale)>.001f)
                    throw new Exception("Pose not restored: "+bones[i].name);
        }
        finally { UnityEngine.Object.DestroyImmediate(editor); }
        EditorUtility.SetDirty(d);
        EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
        EditorSceneManager.SaveScene(c.gameObject.scene);
        Selection.activeGameObject=boss.gameObject;
        return "PASS: Basic_Jump bound, frame sampling animates bones, root fixed, full pose restored. Length="+clip.length+", FPS="+clip.frameRate+", feet="+(d.leftFoot!=null)+"/"+(d.rightFoot!=null);
    }
}
