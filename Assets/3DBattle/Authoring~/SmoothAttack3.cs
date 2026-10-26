using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;

public static class SmoothAttack3
{
    public static string BuildAll()
    {
        var reports=new System.Collections.Generic.List<string>();
        try
        {
            foreach(int number in new[]{3,6,7,9,10,14})
            {
                Number=number;
                reports.Add(number==3 ? Verify() : Build());
            }
        }
        finally { Number=3; }
        return string.Join("\n",reports);
    }
    const float Blend=10f/30f;
    const float ReturnBlend=20f/30f;
    static int Number = 3;
    static string ClipPath => Number == 3 ? "Assets/3DBattle/Boss1/Attack_Smooth10_Boss1.anim" : "Assets/3DBattle/Boss1/Attack"+Number+"_Smooth10_20_Boss1.anim";
    static string PatternPath => Number == 3 ? "Assets/3DBattle/Settings/B1_BasicAttack3_Smooth10.asset" : "Assets/3DBattle/Settings/B1_Attack"+Number+"_Smooth10_20.asset";
    static string SourcePath => Number == 3 ? "Assets/3DBattle/Settings/B1_BasicAttack3.asset" : "Assets/3DBattle/Settings/B1_Attack"+Number+".asset";
    static AnimationCurve Curve(AnimationClip clip,EditorCurveBinding binding)=>AnimationUtility.GetEditorCurve(clip,binding);
    static Quaternion Rotation(AnimationClip clip,EditorCurveBinding binding,float time)
    {
        var q=Quaternion.identity;
        for(int i=0;i<4;i++)
        {
            binding.propertyName="m_LocalRotation."+"xyzw"[i];
            var c=Curve(clip,binding); q[i]=c!=null?c.Evaluate(time):(i==3?1:0);
        }
        return q.normalized;
    }
    static float Value(AnimationClip idle,AnimationClip attack,EditorCurveBinding b,float idleTime,float attackTime,float t)
    {
        if(b.propertyName.StartsWith("m_LocalRotation."))
        {
            var a=Rotation(attack,b,attackTime); var d=Rotation(idle,b,idleTime);
            if(Quaternion.Dot(a,d)<0) d=new Quaternion(-d.x,-d.y,-d.z,-d.w);
            return Quaternion.SlerpUnclamped(d,a,t)["xyzw".IndexOf(b.propertyName.Last())];
        }
        var ac=Curve(attack,b); var ic=Curve(idle,b);
        float value=ac.Evaluate(attackTime);
        return Mathf.LerpUnclamped(ic!=null?ic.Evaluate(idleTime):value,value,t);
    }
    static AnimationCurve ShiftTravel(AnimationCurve source,float length)
    {
        var keys=source.keys;
        for(int i=0;i<keys.Length;i++) keys[i].time+=Blend;
        var result=new AnimationCurve(keys);
        result.AddKey(0,source.Evaluate(0));
        result.AddKey(length+Blend+ReturnBlend,source.Evaluate(length));
        for(int i=0;i<result.length;i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(result,i,AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(result,i,AnimationUtility.TangentMode.Linear);
        }
        return result;
    }
    public static string Build()
    {
        if(Application.isPlaying) throw new Exception("Edit mode required");
        var scene=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>(); var stage=scene.FindStage(1);
        if(stage.gameObject.scene.path!="Assets/Scenes/3DBattleScene.unity") throw new Exception("Wrong scene");
        var controller=(AnimatorController)stage.boss.animator.runtimeAnimatorController;
        var sourcePattern=AssetDatabase.LoadAssetAtPath<BossWeaponAttackPattern>(SourcePath);
        var state=controller.layers[0].stateMachine.states.Single(s=>"Base Layer."+s.state.name==sourcePattern.animationState).state;
        var idle=(AnimationClip)controller.layers[0].stateMachine.states.Single(s=>s.state.name=="Axe_Breathe_and_Look_Around").state.motion;
        var original=AssetDatabase.LoadAssetAtPath<BossWeaponAttackPattern>(SourcePath);
        var source=original.clip;
        if(idle.length<5) throw new Exception("Idle shorter than 5 seconds");
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        if(clip==null) {clip=UnityEngine.Object.Instantiate(source);AssetDatabase.CreateAsset(clip,ClipPath);}
        else EditorUtility.CopySerialized(source,clip);
        clip.name=System.IO.Path.GetFileNameWithoutExtension(ClipPath);
        foreach(var b in AnimationUtility.GetCurveBindings(source))
        {
            var keys=Curve(source,b).keys;
            for(int i=0;i<keys.Length;i++) keys[i].time+=Blend;
            var c=new AnimationCurve(keys);
            for(int f=0;f<10;f++)
            {
                float t=f/10f; t=t*t*(3-2*t);
                c.AddKey(f/30f,Value(idle,source,b,5,0,t));
            }
            for(int f=1;f<=20;f++)
            {
                float t=f/20f; t=t*t*(3-2*t);
                c.AddKey(source.length+Blend+f/30f,Value(idle,source,b,0,source.length,1-t));
            }
            for(int i=0;i<c.length;i++)
            {
                AnimationUtility.SetKeyBroken(c,i,true);
                if(c.keys[i].time<=Blend+.00001f) AnimationUtility.SetKeyLeftTangentMode(c,i,AnimationUtility.TangentMode.Linear);
                if(c.keys[i].time<Blend-.00001f) AnimationUtility.SetKeyRightTangentMode(c,i,AnimationUtility.TangentMode.Linear);
                if(c.keys[i].time>=source.length+Blend-.00001f) AnimationUtility.SetKeyRightTangentMode(c,i,AnimationUtility.TangentMode.Linear);
                if(c.keys[i].time>source.length+Blend+.00001f) AnimationUtility.SetKeyLeftTangentMode(c,i,AnimationUtility.TangentMode.Linear);
            }
            // AddKey may recalculate neighbouring tangents. Restore the authored central segment.
            var finalKeys=c.keys;
            foreach(var originalKey in keys)
            {
                int index=Array.FindIndex(finalKeys,k=>Mathf.Abs(k.time-originalKey.time)<.00001f);
                var restored=finalKeys[index];
                restored.value=originalKey.value;
                if(originalKey.time>Blend+.00001f) restored.inTangent=originalKey.inTangent;
                if(originalKey.time<source.length+Blend-.00001f) restored.outTangent=originalKey.outTangent;
                restored.weightedMode=originalKey.weightedMode;restored.inWeight=originalKey.inWeight;restored.outWeight=originalKey.outWeight;
                finalKeys[index]=restored;
            }
            c.keys=finalKeys;
            AnimationUtility.SetEditorCurve(clip,b,c);
        }
        var settings=AnimationUtility.GetAnimationClipSettings(clip);
        settings.startTime=0;settings.stopTime=source.length+Blend+ReturnBlend;settings.loopTime=false;
        AnimationUtility.SetAnimationClipSettings(clip,settings);
        var pattern=AssetDatabase.LoadAssetAtPath<BossWeaponAttackPattern>(PatternPath);
        if(pattern==null) {pattern=UnityEngine.Object.Instantiate(original);AssetDatabase.CreateAsset(pattern,PatternPath);}
        else EditorUtility.CopySerialized(original,pattern);
        pattern.name=System.IO.Path.GetFileNameWithoutExtension(PatternPath); pattern.displayName=original.displayName+" · 진입 10 / 복귀 20";
        pattern.clip=clip;pattern.hitStartSeconds=original.hitStartSeconds+Blend;pattern.hitEndSeconds=original.hitEndSeconds+Blend;
        pattern.travelX=ShiftTravel(original.travelX,source.length);pattern.travelZ=ShiftTravel(original.travelZ,source.length);
        Undo.RecordObject(state,"Connect smooth attack copy");state.motion=clip;
        var brain=stage.boss.GetComponent<BossApproachController>();Undo.RecordObject(brain,"Connect smooth pattern");
        if(Number==3) brain.basicAttack=pattern;
        for(int i=0;i<brain.attacks.Length;i++) if(brain.attacks[i]==original) brain.attacks[i]=pattern;
        Undo.RecordObject(stage.settings,"Replace attack 3 debug entry");
        for(int i=0;i<stage.settings.patterns.Length;i++) if(stage.settings.patterns[i]==original) stage.settings.patterns[i]=pattern;
        foreach(var asset in new UnityEngine.Object[]{clip,pattern,state,controller,stage.settings})
        {EditorUtility.SetDirty(asset);AssetDatabase.SaveAssetIfDirty(asset);}
        EditorUtility.SetDirty(brain);EditorSceneManager.MarkSceneDirty(stage.gameObject.scene);EditorSceneManager.SaveScene(stage.gameObject.scene);
        return Verify();
    }
    public static string Verify()
    {
        var scene=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();var boss=scene.FindStage(1).boss;
        var controller=(AnimatorController)boss.animator.runtimeAnimatorController;
        var idle=(AnimationClip)controller.layers[0].stateMachine.states.Single(s=>s.state.name=="Axe_Breathe_and_Look_Around").state.motion;
        var source=AssetDatabase.LoadAssetAtPath<BossWeaponAttackPattern>(SourcePath).clip;
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        var a=UnityEngine.Object.Instantiate(boss.animator.gameObject);var b=UnityEngine.Object.Instantiate(boss.animator.gameObject);
        a.hideFlags=b.hideFlags=HideFlags.HideAndDontSave;
        try
        {
            var at=a.GetComponentsInChildren<Transform>();var bt=b.GetComponentsInChildren<Transform>();
            Action compare=()=>{for(int i=1;i<at.Length;i++) if(Vector3.Distance(at[i].localPosition,bt[i].localPosition)>.00001f || Quaternion.Angle(at[i].localRotation,bt[i].localRotation)>.1f) throw new Exception("Pose mismatch "+at[i].name+" position="+Vector3.Distance(at[i].localPosition,bt[i].localPosition)+" angle="+Quaternion.Angle(at[i].localRotation,bt[i].localRotation));};
            idle.SampleAnimation(a,5);clip.SampleAnimation(b,0);compare();
            idle.SampleAnimation(a,0);clip.SampleAnimation(b,clip.length);compare();
            for(int f=0;f<=Mathf.RoundToInt(source.length*30);f++){source.SampleAnimation(a,f/30f);clip.SampleAnimation(b,(f+10)/30f);try{compare();}catch(Exception e){throw new Exception("frame "+f+" "+e.Message);}}
            if(Mathf.Abs(clip.length-(source.length+1f))>.001f) throw new Exception("Wrong duration");
            return Number+": PASS idle endpoints + all original attack frames; total frames="+Mathf.RoundToInt(clip.length*30);
        }
        finally{UnityEngine.Object.DestroyImmediate(a);UnityEngine.Object.DestroyImmediate(b);}
    }
    public static async System.Threading.Tasks.Task<string> RuntimeTest()
    {
        if(!Application.isPlaying) throw new Exception("Play required");
        var scene=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();scene.SelectStage(1);
        var boss=scene.CurrentStage.boss;var brain=boss.GetComponent<BossApproachController>();
        var actions=boss.GetComponent<BossActionController>();var hit=boss.GetComponent<BossWeaponHit>();
        var pattern=(BossWeaponAttackPattern)brain.basicAttack;
        var health=scene.player.GetComponent<BattlePlayerHealth>();var motor=scene.player.GetComponent<CharacterController>();
        bool background=Application.runInBackground,ignore=health.ignoreDamage;
        try
        {
            Application.runInBackground=true;brain.automaticApproach=false;actions.RequestStop();scene.player.enabled=false;
            health.ignoreDamage=false;health.ResetHealth();
            if(pattern.clip!=AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath)||!actions.RequestAttack(pattern)) throw new Exception("Copy not connected");
            boss.animator.speed=0;
            Action<float> pose=t=>{boss.animator.Play(pattern.animationState,0,t/pattern.clip.length);boss.animator.Update(0);scene.player.transform.position=hit.weapon.TransformPoint(hit.localCenter)-motor.center;Physics.SyncTransforms();hit.SampleContact();};
            pose(.15f);if(health.Health!=health.maxHealth) throw new Exception("Intro deals damage");
            pose((pattern.hitStartSeconds+pattern.hitEndSeconds)*.5f);
            if(health.Health!=health.maxHealth-pattern.damage) throw new Exception("Shifted hit window failed");
            actions.RequestStop();health.ResetHealth();scene.player.transform.position=new Vector3(-25,0,-25);
            boss.animator.speed=1;actions.RequestAttack(pattern);
            float timeout=Time.realtimeSinceStartup+6;
            while(actions.IsBusy&&Time.realtimeSinceStartup<timeout) await System.Threading.Tasks.Task.Delay(50);
            if(actions.IsBusy||hit.WindowOpen) throw new Exception("Attack did not complete");
            return "PASS: B1 uses copy, intro harmless, shifted hit window damages, natural completion closes damage. Hit window="+pattern.hitStartSeconds+"–"+pattern.hitEndSeconds;
        }
        finally{actions.RequestStop();boss.animator.speed=1;health.ignoreDamage=ignore;brain.automaticApproach=true;scene.player.enabled=true;Application.runInBackground=background;scene.SelectStage(2);scene.SelectStage(1);}
    }
}



