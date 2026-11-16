using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using OneWay.Battle3D;

public static class SetupBossPhaseTwo
{
    const string Folder = "Assets/3DBattle/Settings/";
    public static object Run()
    {
        if (Application.isPlaying) throw new Exception("Stop play mode first");
        var scene = SceneManager.GetActiveScene();
        if (scene.name != "3DBattleDebugScene") throw new Exception("Expected debug scene");
        var boss = UnityEngine.Object.FindFirstObjectByType<BattleBoss>();
        var source = AssetDatabase.LoadAssetAtPath<BossWeaponAttackPattern>(Folder + "B1_Attack8.asset");
        var clip = new AnimationClip { name = "Skill_02_Phase2_TenSpins", frameRate = 30 };
        var bindings = AnimationUtility.GetCurveBindings(source.clip);
        const string hips = "target_character/Hips";
        var rotation = new[] { "x", "y", "z", "w" }.Select(s => AnimationUtility.GetEditorCurve(source.clip,
            bindings.First(b => b.path == hips && b.propertyName == "m_LocalRotation." + s))).ToArray();
        Func<float, Quaternion> sample = t => new Quaternion(rotation[0].Evaluate(t),rotation[1].Evaluate(t),rotation[2].Evaluate(t),rotation[3].Evaluate(t));
        const int frames = 200;
        const float duration = frames / 30f;
        float[] headings = new float[frames+1];
        headings[0] = sample(0).eulerAngles.y;
        for (int f=1; f<=frames; f++) headings[f] = headings[f-1] + Mathf.DeltaAngle(headings[f-1],sample(source.clip.length*f/frames).eulerAngles.y);
        float originalTurn = headings[frames]-headings[0];
        var spinCurves = new[] {new AnimationCurve(),new AnimationCurve(),new AnimationCurve(),new AnimationCurve()};
        Quaternion previous = Quaternion.identity;
        for (int f=0; f<=frames; f++)
        {
            float turn = headings[f]-headings[0];
            // Preserve tilt and limb poses, with the previous average rotation speed.
            var q = Quaternion.AngleAxis(turn*(3600f/Mathf.Abs(originalTurn)-1f),Vector3.up)*sample(source.clip.length*f/frames);
            if (f>0 && Quaternion.Dot(previous,q)<0) q=new Quaternion(-q.x,-q.y,-q.z,-q.w);
            previous=q;
            for(int c=0;c<4;c++) spinCurves[c].AddKey(f/30f,q[c]);
        }
        foreach(var binding in bindings)
        {
            AnimationCurve curve;
            if(binding.path==hips && binding.propertyName.StartsWith("m_LocalRotation."))
                curve=spinCurves["xyzw".IndexOf(binding.propertyName.Last())];
            else
            {
                var old=AnimationUtility.GetEditorCurve(source.clip,binding);
                curve=new AnimationCurve(Enumerable.Range(0,frames+1).Select(f=>new Keyframe(f/30f,old.Evaluate(source.clip.length*f/frames))).ToArray());
            }
            for(int k=0;k<curve.length;k++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Linear);
            }
            AnimationUtility.SetEditorCurve(clip,binding,curve);
        }
        clip.EnsureQuaternionContinuity();
        string clipPath="Assets/3DBattle/Boss1/Skill_02_Phase2_TenSpins.anim";
        var saved=AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if(saved==null){AssetDatabase.CreateAsset(clip,clipPath);saved=clip;} else {EditorUtility.CopySerialized(clip,saved);UnityEngine.Object.DestroyImmediate(clip);}
        var settings=AnimationUtility.GetAnimationClipSettings(saved);settings.loopTime=false;AnimationUtility.SetAnimationClipSettings(saved,settings);
        var attack=AssetDatabase.LoadAssetAtPath<BossWeaponAttackPattern>(Folder+"B1_Phase2_Attack8.asset");
        if(attack==null){attack=ScriptableObject.CreateInstance<BossWeaponAttackPattern>();AssetDatabase.CreateAsset(attack,Folder+"B1_Phase2_Attack8.asset");}
        attack.displayName="페이즈 2 진입 전용 · 8번 10회전";
        attack.clip=saved;attack.animationState="Base Layer.Skill_02_Phase2_TenSpins";
        attack.damage=source.damage;attack.recoverySeconds=source.recoverySeconds;
        // Find the actual 360-degree crossings in the baked quaternion curves;
        // rotation speed varies, so equal-length windows would reset mid-turn.
        var boundaries = new float[11];
        boundaries[10] = saved.length;
        float accumulated = 0, previousYaw = 0;
        int next = 1;
        for (int s=0;s<=frames*20;s++)
        {
            float t=s/600f;
            var q=new Quaternion(spinCurves[0].Evaluate(t),spinCurves[1].Evaluate(t),spinCurves[2].Evaluate(t),spinCurves[3].Evaluate(t));
            float yaw=q.eulerAngles.y;
            if(s>0)
            {
                float delta=Mathf.DeltaAngle(previousYaw,yaw)*Mathf.Sign(originalTurn);
                float before=accumulated;accumulated+=delta;
                if(next<10 && accumulated>=next*360f)
                {
                    boundaries[next]=(s-1+Mathf.InverseLerp(before,accumulated,next*360f))/600f;
                    next++;
                }
            }
            previousYaw=yaw;
        }
        if(next!=10)throw new Exception("Could not locate all spin boundaries");
        attack.hitWindows=Enumerable.Range(0,10).Select(i=>new BossWeaponAttackPattern.HitWindow(
            boundaries[i],boundaries[i+1]-(i<9?.00001f:0f)){downwardExtension=4.5f}).ToArray();
        attack.independentStrikes=true;
        attack.hitStartSeconds=0;attack.hitEndSeconds=saved.length;
        attack.downwardExtension=4.5f;
        attack.travelX=AnimationCurve.Constant(0,duration,0);attack.travelZ=AnimationCurve.Constant(0,duration,0);
        var controller=(AnimatorController)boss.animator.runtimeAnimatorController;
        var machine=controller.layers[0].stateMachine;
        var state=machine.states.Select(x=>x.state).FirstOrDefault(s=>s.name=="Skill_02_Phase2_TenSpins");
        if(state==null)state=machine.AddState("Skill_02_Phase2_TenSpins");state.motion=saved;state.speed=1;
        var phase=boss.GetComponent<BossPhaseTwo>();if(phase==null)phase=boss.gameObject.AddComponent<BossPhaseTwo>();
        phase.entryAttack=attack;phase.originalAttack8=source;
        var approach=boss.GetComponent<BossApproachController>();
        approach.attacks=approach.attacks.Where(a=>a!=source&&a!=attack).ToArray();
        EditorUtility.SetDirty(attack);EditorUtility.SetDirty(controller);EditorUtility.SetDirty(saved);
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        return new {scene=scene.name,originalTurn,turnDegrees=accumulated,duration=saved.length,boundaries,attackValid=attack.TimingIsValid(),normalAttacks=approach.attacks.Length};
    }
}
