using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;

public static class BossAttackSetSetup
{
    static readonly int[] Numbers={6,7,8,9,10,14};
    static readonly string[] Names={"회전 공격","무기 연속 공격","스킬 공격 02","스킬 공격 03","오른손 베기","차지 베기"};
    static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
    public static string Setup()
    {
        Check(!Application.isPlaying,"Edit mode required");
        var scene=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        var stage=scene.FindStage(1); var boss=stage.boss;
        Check(stage.gameObject.scene.path=="Assets/Scenes/3DBattleScene.unity","Wrong scene");
        var controller=(AnimatorController)boss.animator.runtimeAnimatorController;
        var states=controller.layers[0].stateMachine.states.Select(s=>s.state).ToArray();
        var idle=(AnimationClip)states.Single(s=>"Base Layer."+s.name==boss.idleState).motion;
        var patterns=new List<BossAttackPattern>{boss.GetComponent<BossApproachController>().basicAttack};
        var report=new List<string>();
        for(int n=0;n<Numbers.Length;n++)
        {
            int number=Numbers[n];
            string sourcePath=Directory.GetFiles("Assets/3DBattle/Boss1/Meshy_AI_boss_character_model_biped_"+number,"*.fbx").Single().Replace('\\','/');
            var original=AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__"));
            string stateName=original.name.Split('|').Last();
            var state=states.Single(s=>s.name==stateName);
            var travelX=new AnimationCurve(); var travelZ=new AnimationCurve();
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
                    var delta=copy.transform.InverseTransformPoint(hips.position)-start;
                    travelX.AddKey(time,delta.x); travelZ.AddKey(time,delta.z);
                }
                foreach(var curve in new[]{travelX,travelZ})
                for(int i=0;i<curve.length;i++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
            string path="Assets/3DBattle/Boss1/"+stateName+"_InPlace_Boss1.anim";
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(clip==null) { clip=UnityEngine.Object.Instantiate(original); AssetDatabase.CreateAsset(clip,path); }
            else EditorUtility.CopySerialized(original,clip);
            clip.name=stateName+"_InPlace_Boss1";
            foreach(var binding in AnimationUtility.GetCurveBindings(clip).Where(b=>
                (b.path=="target_character" || b.path=="target_character/Hips") &&
                (b.propertyName=="m_LocalPosition.x" || b.propertyName=="m_LocalPosition.z")))
                AnimationUtility.SetEditorCurve(clip,binding,AnimationCurve.Constant(0,clip.length,AnimationUtility.GetEditorCurve(idle,binding).Evaluate(0)));
            var settings=AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime=false;
            AnimationUtility.SetAnimationClipSettings(clip,settings);
            Undo.RecordObject(state,"Connect boss attack"); state.motion=clip;
            string assetPath="Assets/3DBattle/Settings/B1_Attack"+number+".asset";
            var pattern=AssetDatabase.LoadAssetAtPath<BossWeaponAttackPattern>(assetPath);
            if(pattern==null)
            {
                pattern=ScriptableObject.CreateInstance<BossWeaponAttackPattern>();
                pattern.displayName=number+"번 "+Names[n];
                pattern.damage=20; pattern.recoverySeconds=.3f;
                // Initial tuning window; inspect and refine per animation in the asset Inspector.
                pattern.hitStartSeconds=Mathf.Round(clip.length*.2f*100)/100;
                pattern.hitEndSeconds=Mathf.Round(clip.length*.8f*100)/100;
                AssetDatabase.CreateAsset(pattern,assetPath);
            }
            pattern.animationState="Base Layer."+stateName; pattern.clip=clip;
            pattern.travelX=travelX; pattern.travelZ=travelZ;
            foreach(var asset in new UnityEngine.Object[]{clip,state,pattern})
            { EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); }
            patterns.Add(pattern);
            report.Add(pattern.displayName+" | "+stateName+" | "+clip.length+"s | window "+pattern.hitStartSeconds+"–"+pattern.hitEndSeconds);
        }
        var brain=boss.GetComponent<BossApproachController>();
        Undo.RecordObject(brain,"Attack selection pool"); brain.attacks=patterns.ToArray();
        Undo.RecordObject(stage.settings,"Attack debug list");
        stage.settings.patterns=stage.settings.patterns.Concat(patterns).Distinct().ToArray();
        EditorUtility.SetDirty(controller); AssetDatabase.SaveAssetIfDirty(controller);
        EditorUtility.SetDirty(stage.settings); AssetDatabase.SaveAssetIfDirty(stage.settings);
        EditorUtility.SetDirty(brain);
        EditorSceneManager.MarkSceneDirty(stage.gameObject.scene); EditorSceneManager.SaveScene(stage.gameObject.scene);
        Selection.activeGameObject=boss.gameObject;
        return string.Join("\n",report);
    }
    public static async Task<string> Test()
    {
        Check(Application.isPlaying,"Play required");
        var scene=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        scene.SelectStage(1);
        var boss=scene.CurrentStage.boss; var brain=boss.GetComponent<BossApproachController>();
        var actions=boss.GetComponent<BossActionController>(); var runner=boss.GetComponent<BossPatternRunner>();
        var hit=boss.GetComponent<BossWeaponHit>(); var motion=boss.GetComponent<BossAttackMotion>();
        var player=scene.player; var health=player.GetComponent<BattlePlayerHealth>();
        var motor=player.GetComponent<CharacterController>();
        bool oldIgnore=health.ignoreDamage, background=Application.runInBackground, random=brain.randomAttackOrder;
        var results=new List<string>();
        try
        {
            brain.automaticApproach=false; actions.RequestStop(); player.enabled=false;
            health.ignoreDamage=false; Application.runInBackground=true;
            Check(brain.attacks.Length==7,"Expected 7 attacks");
            foreach(var pattern in brain.attacks.Cast<BossWeaponAttackPattern>())
            {
                scene.SelectStage(1); brain.automaticApproach=false; player.enabled=false;
                health.ResetHealth();
                Check(pattern.IsValid(runner),"Invalid "+pattern.name);
                Check(actions.RequestAttack(pattern),"Start "+pattern.name);
                boss.animator.speed=0;
                float time=(pattern.hitStartSeconds+pattern.hitEndSeconds)*.5f;
                boss.animator.Play(pattern.animationState,0,time/pattern.clip.length); boss.animator.Update(0);
                motion.Advance();
                player.transform.position=hit.weapon.TransformPoint(hit.localCenter)-motor.center;
                Physics.SyncTransforms(); hit.SampleContact(); hit.SampleContact();
                Check(hit.HitCount==1 && health.Health==health.maxHealth-pattern.damage,"Contact "+pattern.name);
                actions.RequestStop();
                Check(!hit.WindowOpen,"Stop cleanup "+pattern.name);
                player.transform.position=new Vector3(-25,0,-25);
                boss.animator.speed=1;
                Check(actions.RequestAttack(pattern),"Natural start "+pattern.name);
                float deadline=Time.realtimeSinceStartup+pattern.clip.length+3;
                while(actions.IsBusy && Time.realtimeSinceStartup<deadline) await Task.Delay(50);
                Check(!actions.IsBusy && !hit.WindowOpen,"Natural completion "+pattern.name);
                results.Add(pattern.displayName+": contact once, movement, cancellation, full completion PASS");
            }
            scene.SelectStage(1); brain.automaticApproach=true; brain.randomAttackOrder=false;
            for(int i=0;i<brain.attacks.Length;i++)
            {
                player.transform.position=boss.transform.position+boss.transform.forward*4;
                brain.Tick(0);
                Check(runner.CurrentPattern==brain.attacks[i],"Sequential selection "+i);
                actions.RequestStop();
            }
            brain.ResetDecision(); brain.randomAttackOrder=true;
            BossAttackPattern previous=null;
            for(int i=0;i<30;i++)
            {
                brain.Tick(0);
                Check(runner.CurrentPattern!=null && runner.CurrentPattern!=previous,"Random immediate repeat");
                previous=runner.CurrentPattern; actions.RequestStop();
            }
            return string.Join("\n",results)+"\nSelection: sequential 7/7 and random no immediate repeat PASS";
        }
        finally
        {
            actions.RequestStop(); boss.animator.speed=1; health.ignoreDamage=oldIgnore;
            Application.runInBackground=background; brain.randomAttackOrder=random; brain.automaticApproach=true;
            player.enabled=true; scene.SelectStage(2); scene.SelectStage(1);
        }
    }
}
