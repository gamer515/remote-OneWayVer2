using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using OneWay.Battle3D;
public static class FixJumpTravel
{
    public static string BuildClip()
    {
        if(Application.isPlaying) throw new Exception("Edit mode required");
        var b=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>().FindStage(1).boss;
        var original=b.GetComponent<BossJumpTimingDebug>().clip;
        const string path="Assets/3DBattle/Boss1/Basic_Jump_InPlace_Boss1.anim";
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if(clip==null) { clip=UnityEngine.Object.Instantiate(original); AssetDatabase.CreateAsset(clip,path); }
        else EditorUtility.CopySerialized(original,clip);
        clip.name="Basic_Jump_InPlace_Boss1";
        int count=0;
        foreach(var binding in AnimationUtility.GetCurveBindings(clip))
        {
            if((binding.path=="target_character" || binding.path=="target_character/Hips") &&
                (binding.propertyName=="m_LocalPosition.x" || binding.propertyName=="m_LocalPosition.z"))
            {
                var curve=AnimationUtility.GetEditorCurve(clip,binding);
                float value=curve.Evaluate(0f);
                AnimationUtility.SetEditorCurve(clip,binding,AnimationCurve.Constant(0f,original.length,value));
                count++;
            }
        }
        if(count!=4) throw new Exception("Unexpected skeleton translation curves: "+count);
        var controller=(AnimatorController)b.animator.runtimeAnimatorController;
        var state=controller.layers[0].stateMachine.states.Single(s=>s.state.name=="Basic_Jump").state;
        Undo.RecordObject(state,"Use in-place jump"); state.motion=clip;
        EditorUtility.SetDirty(clip); EditorUtility.SetDirty(state); EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssetIfDirty(clip); AssetDatabase.SaveAssetIfDirty(controller);
        return "Created in-place jump; 4 planar curves frozen at start; vertical motion and bone rotations retained. Original FBX unchanged.";
    }
    public static string Test()
    {
        if(!Application.isPlaying) throw new Exception("Play required");
        var c=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        c.SelectStage(1);
        var d=c.CurrentStage.boss.GetComponent<BossJumpMotionDebug>(); var b=c.CurrentStage.boss;
        var player=c.player;
        var originalRotation=player.transform.rotation;
        Vector3? expectedLanding=null;
        try
        {
            for(int turn=0;turn<4;turn++)
            {
                d.ResetToSpawn();
                player.transform.rotation=Quaternion.Euler(0f,turn*90f,0f);
                Vector3 start=b.transform.position;
                Vector3 approach=player.transform.position-start; approach.y=0f; approach.Normalize();
                d.Begin(); d.Tick(1.71f);
                if(!d.IsRunning) throw new Exception("Jump not running");
                if(Vector3.Dot(b.transform.forward,approach)<.999f) throw new Exception("Boss turned away");
                if(expectedLanding.HasValue && Vector3.Distance(expectedLanding.Value,d.LandingPosition)>.001f) throw new Exception("Landing depends on player facing");
                expectedLanding=d.LandingPosition;
                Vector3 travel=d.LandingPosition-start; travel.y=0f;
                if(Vector3.Cross(travel,approach).magnitude>.001f || Vector3.Dot(travel,approach)<0f) throw new Exception("Not shortest-line travel");
                d.Tick(.67f);
                Vector3 landed=b.transform.position;
                var hips=b.animator.transform.Find("target_character/Hips");
                Vector3 local=b.transform.InverseTransformPoint(hips.position);
                for(int i=0;i<30;i++)
                {
                    d.Tick(.1f);
                    Vector3 now=b.transform.InverseTransformPoint(hips.position);
                    if(Vector3.Distance(b.transform.position,landed)>.001f) throw new Exception("Root slides after landing");
                    if(Vector2.Distance(new Vector2(local.x,local.z),new Vector2(now.x,now.z))>.001f) throw new Exception("Hips slide after landing");
                    if(Vector3.Dot(b.transform.forward,approach)<.999f) throw new Exception("Recovery turned away");
                }
                d.Tick(1f);
                if(d.IsRunning) throw new Exception("Jump not completed");
                if(Vector3.Distance(b.transform.position,landed)>.001f) throw new Exception("Completion shifted landing");
            }
            return "PASS: four player headings produce same shortest-line landing; boss faces approach; root AND hips stay horizontally fixed throughout landing recovery and root stays at completion.";
        }
        finally { player.transform.rotation=originalRotation; c.SelectStage(1); }
    }
}
