using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using OneWay.Battle3D;
public static class Jump17MotionSetup
{
    public static string BuildClip()
    {
        if(Application.isPlaying) throw new Exception("Edit mode required");
        var b=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>().FindStage(1).boss;
        var original=b.GetComponent<BossJump17TimingDebug>().clip;
        const string path="Assets/3DBattle/Boss1/Jump_with_Arms_Open_InPlace_Boss1.anim";
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if(clip==null) { clip=UnityEngine.Object.Instantiate(original); AssetDatabase.CreateAsset(clip,path); }
        else EditorUtility.CopySerialized(original,clip);
        clip.name="Jump_with_Arms_Open_InPlace_Boss1";
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
        var state=controller.layers[0].stateMachine.states.Single(s=>s.state.name=="Jump_with_Arms_Open_Boss1").state;
        Undo.RecordObject(state,"Use in-place jump"); state.motion=clip;
        EditorUtility.SetDirty(clip); EditorUtility.SetDirty(state); EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssetIfDirty(clip); AssetDatabase.SaveAssetIfDirty(controller);
        var timing=b.GetComponent<BossJump17TimingDebug>(); timing.takeoffTime=1.4f; timing.landingTime=2.16f; var motion=b.GetComponent<BossJumpMotionDebug>(); motion.jump17Timing=timing; motion.useJump17=true; EditorUtility.SetDirty(timing); EditorUtility.SetDirty(motion); UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(b.gameObject.scene); UnityEditor.SceneManagement.EditorSceneManager.SaveScene(b.gameObject.scene); UnityEditor.Selection.activeGameObject=b.gameObject;
        return "Created in-place jump; 4 planar curves frozen at start; vertical motion and bone rotations retained. Original FBX unchanged.";
    }
    public static string Test()
    {
        if(!Application.isPlaying) throw new Exception("Play required");
        var c=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        c.SelectStage(1);
        var d=c.CurrentStage.boss.GetComponent<BossJumpMotionDebug>(); var b=c.CurrentStage.boss;
        d.useJump17=true; var player=c.player;
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
                d.Begin(); d.Tick(1.41f);
                if(!d.IsRunning) throw new Exception("Jump not running");
                if(Vector3.Dot(b.transform.forward,approach)<.999f) throw new Exception("Boss turned away");
                if(expectedLanding.HasValue && Vector3.Distance(expectedLanding.Value,d.LandingPosition)>.001f) throw new Exception("Landing depends on player facing");
                expectedLanding=d.LandingPosition;
                Vector3 travel=d.LandingPosition-start; travel.y=0f;
                if(Vector3.Cross(travel,approach).magnitude>.001f || Vector3.Dot(travel,approach)<0f) throw new Exception("Not shortest-line travel");
                d.Tick(.76f);
                Vector3 landed=b.transform.position;
                var hips=b.animator.transform.Find("target_character/Hips");
                Vector3 local=b.transform.InverseTransformPoint(hips.position);
                for(int i=0;i<14;i++)
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
            d.ResetToSpawn(); d.Begin(); d.Tick(1.41f);
            var priority=b.GetComponent<BossActionPriority>();
            b.TakeDamage(b.maxHealth*.34f);
            if(!priority.Airborne || priority.HitReacting || !priority.PendingHit) throw new Exception("17 hit interrupted flight");
            b.TakeDamage(b.maxHealth); d.Tick(.76f);
            if(!priority.Down || priority.HitReacting || d.IsRunning) throw new Exception("17 landing death priority failed");
            d.ResetToSpawn(); d.Begin(); d.Tick(1.41f); b.TakeDamage(b.maxHealth*.34f); d.Tick(.76f);
            if(!priority.HitReacting || d.IsRunning) throw new Exception("17 landing hit priority failed");
            d.ResetToSpawn(); d.playbackSpeed=.5f; d.Begin(); d.Tick(2.82f);
            if(!priority.Airborne) throw new Exception("17 slow takeoff failed");
            d.Tick(1.52f); if(priority.Airborne) throw new Exception("17 slow landing failed");
            d.RequestStop(); d.ResetToSpawn(); d.playbackSpeed=1f;
            return "PASS: 17 priority + speed; four player headings produce same shortest-line landing; boss faces approach; root AND hips stay horizontally fixed throughout landing recovery and root stays at completion.";
        }
        finally { player.transform.rotation=originalRotation; c.SelectStage(1); }
    }
}

