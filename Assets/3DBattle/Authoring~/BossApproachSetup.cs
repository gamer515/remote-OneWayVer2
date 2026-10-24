using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;

public static class BossApproachSetup
{
    static void Check(bool ok, string message) { if(!ok) throw new Exception(message); }
    public static string Setup()
    {
        Check(!Application.isPlaying,"Edit mode required");
        var scene=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        var b=scene.FindStage(1).boss;
        Check(b.gameObject.scene.path=="Assets/Scenes/3DBattleScene.unity","Wrong scene");
        var brain=b.GetComponent<BossApproachController>();
        if(brain==null) brain=Undo.AddComponent<BossApproachController>(b.gameObject);
        Undo.RecordObject(brain,"Configure B1 approach");
        brain.battle=scene;
        var controller=(AnimatorController)b.animator.runtimeAnimatorController;
        var states=controller.layers[0].stateMachine.states.Select(s=>s.state).ToArray();
        var idle=(AnimationClip)states.Single(s=>"Base Layer."+s.name==b.idleState).motion;
        foreach(var stateName in new[]{brain.runState,brain.walkState})
        {
            var state=states.Single(s=>"Base Layer."+s.name==stateName);
            var source=(AnimationClip)state.motion;
            string path="Assets/3DBattle/Boss1/"+state.name+"_Approach.anim";
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(clip==null) { clip=UnityEngine.Object.Instantiate(source); AssetDatabase.CreateAsset(clip,path); }
            foreach(var binding in AnimationUtility.GetCurveBindings(clip).Where(x=>
                (x.path=="target_character" || x.path=="target_character/Hips") &&
                (x.propertyName=="m_LocalPosition.x" || x.propertyName=="m_LocalPosition.z")))
            {
                var reference=AnimationUtility.GetEditorCurve(idle,binding);
                Check(reference!=null,"Missing idle translation");
                AnimationUtility.SetEditorCurve(clip,binding,AnimationCurve.Constant(0,clip.length,reference.Evaluate(0)));
            }
            var settings=AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime=true;
            AnimationUtility.SetAnimationClipSettings(clip,settings);
            Undo.RecordObject(state,"Connect approach animation");
            state.motion=clip;
            EditorUtility.SetDirty(clip); EditorUtility.SetDirty(state);
            AssetDatabase.SaveAssetIfDirty(clip);
        }
        EditorUtility.SetDirty(controller); AssetDatabase.SaveAssetIfDirty(controller);
        EditorUtility.SetDirty(brain);
        EditorSceneManager.MarkSceneDirty(b.gameObject.scene);
        EditorSceneManager.SaveScene(b.gameObject.scene);
        Selection.activeGameObject=b.gameObject;
        return "B1 approach connected; run/walk loop clips use script-controlled horizontal travel.";
    }
    public static string Test()
    {
        Check(Application.isPlaying,"Play required");
        var scene=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        scene.SelectStage(1);
        var b=scene.CurrentStage.boss;
        var brain=b.GetComponent<BossApproachController>();
        var actions=b.GetComponent<BossActionController>();
        var jump=b.GetComponent<BossJumpMotionDebug>();
        var player=scene.player.transform;
        var initial=player.position;
        var origin=b.transform.position;
        try
        {
            player.position=origin+Vector3.forward*10;
            brain.Tick(0);
            Check(actions.Current==BossActionKind.RunApproach,"Default run");
            brain.Tick(.1f);
            Check(Vector3.Distance(b.transform.position,origin)>.1f,"Run moves");
            player.position=b.transform.position+Vector3.forward*20;
            brain.Tick(0);
            Check(actions.Current==BossActionKind.Jump12,"20 inclusive jumps");
            jump.Tick(1.71f);
            player.position+=Vector3.forward*10;
            jump.Tick(.67f);
            brain.Tick(0);
            Check(actions.Current==BossActionKind.Jump12,"Wait for full jump recovery");
            jump.Tick(10);
            brain.Tick(0);
            Check(actions.Current==BossActionKind.WalkApproach,"Walk after completed jump");
            brain.walkSpeed=.01f;
            brain.Tick(4.99f);
            Check(actions.Current==BossActionKind.WalkApproach,"Still walks before 5 sec");
            brain.Tick(.02f);
            Check(actions.Current==BossActionKind.RunApproach,"Run after 5 sec");
            player.position=b.transform.position+Vector3.forward*4;
            brain.Tick(0);
            Check(actions.Current==BossActionKind.Idle,"Reached distance completes");
            player.position=b.transform.position+Vector3.forward*5;
            brain.Tick(0);
            Check(actions.Current==BossActionKind.RunApproach,"5 inclusive approaches");
            b.TakeDamage(b.maxHealth*.34f);
            var stopped=b.transform.position; brain.Tick(1);
            Check(actions.Current==BossActionKind.HitReaction && b.transform.position==stopped,"Hit stops approach");
            actions.Tick(10); brain.Tick(0);
            Check(actions.Current==BossActionKind.RunApproach,"Rejudge after hit");
            brain.automaticApproach=false; brain.Tick(0);
            Check(actions.Current==BossActionKind.Idle,"Inspector pause");
            brain.automaticApproach=true;
            scene.SelectStage(2); scene.SelectStage(1);
            Check(brain.WalkElapsed==0 && actions.Current==BossActionKind.Idle,"Stage reset");
            return "PASS: run, 20/5 boundaries, full jump completion then walk, 5-second timeout, arrival, hit interruption/resume, pause/reset.";
        }
        finally
        {
            brain.walkSpeed=2; brain.automaticApproach=true;
            scene.SelectStage(2); scene.SelectStage(1); player.position=initial;
        }
    }
}
