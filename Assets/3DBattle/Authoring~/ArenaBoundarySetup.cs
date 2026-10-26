using System;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using OneWay.Battle3D;
public static class ArenaBoundarySetup
{
    static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    public static string Test()
    {
        Check(Application.isPlaying,"Play required");
        var scene=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        scene.SelectStage(1);
        var boss=scene.CurrentStage.boss;
        var brain=boss.GetComponent<BossApproachController>();
        var actions=boss.GetComponent<BossActionController>();
        var motion=boss.GetComponent<BossAttackMotion>();
        var pattern=(BossWeaponAttackPattern)brain.basicAttack;
        var body=boss.GetComponent<BattleArenaBody>();
        float speed=boss.animator.speed;
        try
        {
            brain.automaticApproach=false; actions.RequestStop();
            foreach(var actor in new[]{body,scene.player.GetComponent<BattleArenaBody>()})
            foreach(var direction in new[]{Vector3.right,Vector3.left,Vector3.forward,Vector3.back,new Vector3(1,0,1)})
            {
                actor.MoveTo(direction*100+Vector3.up*50);
                Vector3 center=actor.torso!=null ? actor.torso.position : actor.transform.TransformPoint(actor.localCenter);
                Check(Mathf.Abs(center.x)+actor.radius<=29.001f && Mathf.Abs(center.z)+actor.radius<=29.001f,"Escaped wall");
                Check(Mathf.Abs(actor.transform.position.y-50)<.01f,"Boundary changed jump height");
            }
            scene.SelectStage(1); brain.automaticApproach=false;
            scene.player.transform.position=boss.transform.position+Vector3.forward*10;
            Check(actions.RequestAttack(pattern),"Attack starts");
            boss.animator.speed=0;
            Vector3 start=boss.transform.position;
            for(int i=1;i<=20;i++)
            {
                float time=i*.05f;
                boss.animator.Play(pattern.animationState,0,time/pattern.clip.length); boss.animator.Update(0); motion.Advance();
            }
            Vector3 expected=boss.transform.TransformVector(new Vector3(pattern.travelX.Evaluate(1)-pattern.travelX.Evaluate(0),0,pattern.travelZ.Evaluate(1)-pattern.travelZ.Evaluate(0)));
            expected.y=0;
            Check(Vector3.Distance(boss.transform.position-start,expected)<.01f,"Free attack travel mismatch");
            Check(expected.magnitude>.01f,"Attack has no extracted travel");
            actions.RequestStop();
            var savedX=pattern.travelX; var savedZ=pattern.travelZ;
            try
            {
                // Synthetic forward clip curve isolates wall blocking and discarded movement debt.
                pattern.travelX=AnimationCurve.Linear(0,1,pattern.clip.length,1);
                pattern.travelZ=AnimationCurve.Linear(0,0,pattern.clip.length,20);
                boss.transform.rotation=Quaternion.identity;
                body.MoveTo(new Vector3(0,0,100));
                scene.player.transform.position=new Vector3(0,0,100);
                Check(actions.RequestAttack(pattern),"Wall attack starts");
                for(int i=1;i<=20;i++)
                {
                    boss.animator.Play(pattern.animationState,0,i*.04f); boss.animator.Update(0); motion.Advance();
                    Check(body.torso.position.z+body.radius<29.001f,"Attack crossed wall");
                }
                Check(actions.Current==BossActionKind.Attack,"Wall cancelled animation");
                float previousTime=boss.animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                body.MoveTo(new Vector3(0,0,0));
                boss.animator.Play(pattern.animationState,0,previousTime+.01f); boss.animator.Update(0); motion.Advance();
                Check(boss.transform.position.z<2,"Blocked movement accumulated");
            }
            finally { pattern.travelX=savedX; pattern.travelZ=savedZ; }
            return "PASS: all four walls/corner for player and boss, airborne containment, authored free attack travel, wall-blocked animation continues, no stored movement debt.";
        }
        finally { actions.RequestStop(); boss.animator.speed=speed; brain.automaticApproach=true; scene.SelectStage(2); scene.SelectStage(1); }
    }
    public static string Setup()
    {
        if(Application.isPlaying) throw new Exception("Edit mode required");
        var scene=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        var stage=scene.FindStage(1);
        if(stage.gameObject.scene.path!="Assets/Scenes/3DBattleScene.unity") throw new Exception("Wrong scene");
        var walls=Enumerable.Range(1,4).Select(i=>stage.GetComponentsInChildren<Transform>(true)
            .Single(t=>t.name=="Wall"+i).GetComponent<BoxCollider>()).ToArray();
        var boundary=stage.GetComponent<BattleArenaBoundary>()??Undo.AddComponent<BattleArenaBoundary>(stage.gameObject);
        Undo.RecordObject(boundary,"Arena walls"); boundary.walls=walls;
        Undo.RecordObject(stage,"Stage boundary"); stage.boundary=boundary;
        foreach(var go in new[]{stage.boss.gameObject,scene.player.gameObject})
        {
            var body=go.GetComponent<BattleArenaBody>()??Undo.AddComponent<BattleArenaBody>(go);
            Undo.RecordObject(body,"Body boundary");
            body.battle=scene;
            var capsule=go.GetComponent<CapsuleCollider>();
            var player=go.GetComponent<CharacterController>();
            if(capsule!=null)
            {
                body.radius=capsule.radius*Mathf.Max(Mathf.Abs(go.transform.lossyScale.x),Mathf.Abs(go.transform.lossyScale.z));
                body.localCenter=capsule.center;
                body.torso=stage.boss.animator.transform.Find("target_character/Hips");
            }
            else { body.radius=player.radius*Mathf.Max(Mathf.Abs(go.transform.lossyScale.x),Mathf.Abs(go.transform.lossyScale.z)); body.localCenter=player.center; }
            EditorUtility.SetDirty(body);
        }
        var boss=stage.boss;
        if(boss.GetComponent<BossAttackMotion>()==null) Undo.AddComponent<BossAttackMotion>(boss.gameObject);
        var controller=(UnityEditor.Animations.AnimatorController)boss.animator.runtimeAnimatorController;
        var state=controller.layers[0].stateMachine.states.Single(s=>s.state.name=="Attack").state;
        var original=AssetDatabase.LoadAllAssetsAtPath("Assets/3DBattle/Boss1/Meshy_AI_boss_character_model_biped_3/Meshy_AI_boss_character_model_biped_Animation_Attack_frame_rate_30.fbx")
            .OfType<AnimationClip>().First(c=>!c.name.StartsWith("__"));
        var idle=(AnimationClip)controller.layers[0].stateMachine.states.Single(s=>"Base Layer."+s.state.name==boss.idleState).state.motion;
        var pattern=AssetDatabase.LoadAssetAtPath<BossWeaponAttackPattern>("Assets/3DBattle/Settings/B1_BasicAttack3.asset");
        var x=new AnimationCurve(); var z=new AnimationCurve();
        var copy=UnityEngine.Object.Instantiate(boss.animator.gameObject);
        copy.hideFlags=HideFlags.HideAndDontSave;
        try
        {
            var hips=copy.transform.Find("target_character/Hips");
            original.SampleAnimation(copy,0);
            Vector3 start=copy.transform.InverseTransformPoint(hips.position);
            int frames=Mathf.CeilToInt(original.length*original.frameRate);
            for(int i=0;i<=frames;i++)
            {
                float time=Mathf.Min(original.length,i/original.frameRate);
                original.SampleAnimation(copy,time);
                Vector3 delta=copy.transform.InverseTransformPoint(hips.position)-start;
                x.AddKey(time,delta.x); z.AddKey(time,delta.z);
            }
            for(int i=0;i<x.length;i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(x,i,AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(x,i,AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyLeftTangentMode(z,i,AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(z,i,AnimationUtility.TangentMode.Linear);
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(copy); }
        const string path="Assets/3DBattle/Boss1/Attack_InPlace_Boss1.anim";
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if(clip==null) { clip=UnityEngine.Object.Instantiate(original); AssetDatabase.CreateAsset(clip,path); }
        else EditorUtility.CopySerialized(original,clip);
        clip.name="Attack_InPlace_Boss1";
        foreach(var binding in AnimationUtility.GetCurveBindings(clip).Where(b=>
            (b.path=="target_character" || b.path=="target_character/Hips") &&
            (b.propertyName=="m_LocalPosition.x" || b.propertyName=="m_LocalPosition.z")))
            AnimationUtility.SetEditorCurve(clip,binding,AnimationCurve.Constant(0,clip.length,AnimationUtility.GetEditorCurve(idle,binding).Evaluate(0)));
        Undo.RecordObject(state,"Separate attack travel"); state.motion=clip;
        Undo.RecordObject(pattern,"Authored attack travel"); pattern.clip=clip; pattern.travelX=x; pattern.travelZ=z;
        foreach(var asset in new UnityEngine.Object[]{clip,state,controller,pattern})
        { EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); }
        EditorUtility.SetDirty(boundary); EditorUtility.SetDirty(stage);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(stage.gameObject.scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(stage.gameObject.scene);
        Selection.activeGameObject=boss.gameObject;
        return "B1 walls/body radii connected; Attack planar travel extracted and animation made in-place.";
    }
    public static string Inspect()
    {
        var scene=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        var stage=scene.FindStage(1); var text=new StringBuilder();
        foreach(var t in stage.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("Wall")))
        {
            text.AppendLine(t.name+" pos="+t.position+" scale="+t.lossyScale+" rotation="+t.eulerAngles);
            foreach(var c in t.GetComponents<Collider>()) text.AppendLine(c.GetType().Name+" "+c.bounds+" trigger="+c.isTrigger);
        }
        text.AppendLine("Boss collider "+stage.boss.hitbox+" "+stage.boss.hitbox.bounds);
        return text.ToString();
    }
}
