using System;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using OneWay.Battle3D;

public static class FixDownStart
{
    const string Output="Assets/3DBattle/Boss1/Knock_Down_Aligned_Boss1.anim";
    static BattleBoss Boss => UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>().FindStage(1).boss;
    static AnimatorState State(string name) => ((AnimatorController)Boss.animator.runtimeAnimatorController)
        .layers[0].stateMachine.states.Single(s=>"Base Layer."+s.state.name==name).state;
    static AnimationClip Original => AssetDatabase.LoadAllAssetsAtPath(
        "Assets/3DBattle/Boss1/Meshy_AI_boss_character_model_biped_4/Meshy_AI_boss_character_model_biped_Animation_Knock_Down_frame_rate_30.fbx")
        .OfType<AnimationClip>().First(c=>!c.name.StartsWith("__"));
    public static string Apply()
    {
        if(Application.isPlaying) throw new Exception("Edit mode required");
        var original=Original;
        var idle=(AnimationClip)State(Boss.idleState).motion;
        var corrected=AssetDatabase.LoadAssetAtPath<AnimationClip>(Output);
        if(corrected==null) { corrected=UnityEngine.Object.Instantiate(original); AssetDatabase.CreateAsset(corrected,Output); }
        else EditorUtility.CopySerialized(original,corrected);
        corrected.name="Knock_Down_Aligned_Boss1";
        int count=0;
        foreach(var b in AnimationUtility.GetCurveBindings(original).Where(b=>
            (b.path=="target_character" || b.path=="target_character/Hips") &&
            (b.propertyName=="m_LocalPosition.x" || b.propertyName=="m_LocalPosition.z")))
        {
            var curve=AnimationUtility.GetEditorCurve(original,b);
            var baseline=AnimationUtility.GetEditorCurve(idle,b);
            if(baseline==null) throw new Exception("Missing reference curve");
            float offset=baseline.Evaluate(0f)-curve.Evaluate(0f);
            var keys=curve.keys;
            for(int i=0;i<keys.Length;i++) keys[i].value+=offset;
            curve.keys=keys;
            AnimationUtility.SetEditorCurve(corrected,b,curve);
            count++;
        }
        if(count!=4) throw new Exception("Expected four planar curves");
        var state=State(Boss.deadState);
        Undo.RecordObject(state,"Align knock-down start");
        state.motion=corrected;
        EditorUtility.SetDirty(corrected); EditorUtility.SetDirty(state);
        EditorUtility.SetDirty(Boss.animator.runtimeAnimatorController);
        AssetDatabase.SaveAssetIfDirty(corrected);
        AssetDatabase.SaveAssetIfDirty(Boss.animator.runtimeAnimatorController);
        return Verify();
    }
    public static string Verify()
    {
        var original=Original;
        var corrected=(AnimationClip)State(Boss.deadState).motion;
        var idle=(AnimationClip)State(Boss.idleState).motion;
        var copy=UnityEngine.Object.Instantiate(Boss.animator.gameObject);
        copy.hideFlags=HideFlags.HideAndDontSave;
        try
        {
            var hips=copy.transform.Find("target_character/Hips");
            idle.SampleAnimation(copy,0); var idleStart=hips.position;
            original.SampleAnimation(copy,0); var oldStart=hips.position;
            corrected.SampleAnimation(copy,0); var newStart=hips.position;
            var planar=newStart-idleStart; planar.y=0;
            if(planar.magnitude>.001f) throw new Exception("Start still offset "+planar);
            for(int i=0;i<=75;i++)
            {
                float t=original.length*i/75f;
                original.SampleAnimation(copy,t); var oldDelta=hips.position-oldStart;
                corrected.SampleAnimation(copy,t); var newDelta=hips.position-newStart;
                if(Vector3.Distance(oldDelta,newDelta)>.002f) throw new Exception("Trajectory changed at "+t);
            }
            return "PASS: start horizontal offset="+planar.magnitude+"; original flight displacement preserved at 76 samples; clip duration="+corrected.length;
        }
        finally { UnityEngine.Object.DestroyImmediate(copy); }
    }
    public static string RuntimeCheck()
    {
        if(!Application.isPlaying) throw new Exception("Play mode required");
        var scene=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        scene.SelectStage(1);
        var b=Boss; var a=b.animator;
        try
        {
            a.Play(b.idleState,0,0); a.Update(0);
            var hips=a.transform.Find("target_character/Hips");
            var before=hips.position; var root=b.transform.position;
            b.TakeDamage(b.maxHealth); a.Update(0);
            var delta=hips.position-before; delta.y=0;
            if(delta.magnitude>.001f || Vector3.Distance(root,b.transform.position)>.001f)
                throw new Exception("Death transition teleported "+delta);
            if(b.GetComponent<BossActionController>().Current!=BossActionKind.Down)
                throw new Exception("Not in down state");
            a.Update(.5f);
            var travel=hips.position-before; travel.y=0;
            if(travel.magnitude<.01f) throw new Exception("Flight motion lost");
            return "PASS: runtime death starts without horizontal teleport; flight motion remains, displacement at 0.5 sec="+travel.magnitude;
        }
        finally { scene.SelectStage(2); scene.SelectStage(1); }
    }
    public static string Inspect()
    {
        var result=new StringBuilder("Playing="+Application.isPlaying+"\n");
        foreach(var name in new[]{Boss.idleState,Boss.deadState,"Base Layer.Basic_Jump"})
        {
            var clip=(AnimationClip)State(name).motion;
            result.AppendLine(name+" "+AssetDatabase.GetAssetPath(clip));
            foreach(var b in AnimationUtility.GetCurveBindings(clip).Where(b=>
                (b.path=="target_character" || b.path=="target_character/Hips") && b.propertyName.StartsWith("m_LocalPosition")))
            {
                var c=AnimationUtility.GetEditorCurve(clip,b);
                result.AppendLine(b.path+" "+b.propertyName+" start="+c.Evaluate(0)+" end="+c.Evaluate(clip.length));
            }
        }
        var copy=UnityEngine.Object.Instantiate(Boss.animator.gameObject);
        copy.hideFlags=HideFlags.HideAndDontSave;
        try
        {
            var hips=copy.transform.Find("target_character/Hips");
            foreach(var name in new[]{Boss.idleState,Boss.deadState})
            {
                ((AnimationClip)State(name).motion).SampleAnimation(copy,0f);
                result.AppendLine(name+" hip relative="+(hips.position-copy.transform.position).ToString("F4"));
                result.AppendLine("parent axes: "+hips.parent.TransformVector(Vector3.right).ToString("F3")+" / "+hips.parent.TransformVector(Vector3.up).ToString("F3")+" / "+hips.parent.TransformVector(Vector3.forward).ToString("F3"));
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(copy); }
        return result.ToString();
    }
}
