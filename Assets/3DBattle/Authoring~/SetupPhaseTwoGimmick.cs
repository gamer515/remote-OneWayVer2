using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;
public static class SetupPhaseTwoGimmick
{
    const string Folder="Assets/3DBattle/3DBattleDebug/";
    static Transform Skeleton(Transform source, Transform parent)
    {
        var t=new GameObject(source.name).transform;t.SetParent(parent,false);
        t.localPosition=source.localPosition;t.localRotation=source.localRotation;t.localScale=source.localScale;
        foreach(Transform child in source)Skeleton(child,t);return t;
    }
    public static object Run()
    {
        if(Application.isPlaying)throw new Exception("Edit mode required");
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/3DBattleDebugScene.unity")throw new Exception("Open 3DBattleDebugScene first");
        var battle=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<Battle3DSceneController>(true)).Single();
        var boss=battle.stages.Single(x=>x.StageIndex==1).boss;var phase=boss.GetComponent<BossPhaseTwo>();
        var gimmick=boss.GetComponent<BossPhaseTwoGimmick>();if(!gimmick)gimmick=Undo.AddComponent<BossPhaseTwoGimmick>(boss.gameObject);
        Undo.RecordObject(gimmick,"Configure phase two parry");Undo.RecordObject(phase,"Debug-only entry attack");
        string path=Folder+"B1_Phase2_ParryAttack.asset";
        var pattern=AssetDatabase.LoadAssetAtPath<BossWeaponAttackPattern>(path);
        if(!pattern){pattern=UnityEngine.Object.Instantiate(phase.entryAttack);AssetDatabase.CreateAsset(pattern,path);}
        phase.entryAttack=pattern;
        var clip=pattern.clip;var bindings=AnimationUtility.GetCurveBindings(clip);
        var qcurves=new[]{"x","y","z","w"}.Select(axis=>AnimationUtility.GetEditorCurve(clip,bindings.First(b=>b.path=="target_character/Hips" && b.propertyName=="m_LocalRotation."+axis))).ToArray();
        Func<float,float> yaw=t=>new Quaternion(qcurves[0].Evaluate(t),qcurves[1].Evaluate(t),qcurves[2].Evaluate(t),qcurves[3].Evaluate(t)).eulerAngles.y;
        float total=0,prev=yaw(0);int samples=Mathf.CeilToInt(clip.length*1200);
        for(int i=1;i<=samples;i++){float next=yaw(clip.length*i/samples);total+=Mathf.DeltaAngle(prev,next);prev=next;}
        if(Mathf.Abs(total)<3500)throw new Exception("Not a ten-spin clip: "+total);
        var starts=new float[10];int nextTurn=1;float accumulated=0;prev=yaw(0);
        for(int i=1;i<=samples && nextTurn<10;i++)
        {
            float t=clip.length*i/samples;float next=yaw(t);float before=accumulated;
            accumulated+=Mathf.DeltaAngle(prev,next)*Mathf.Sign(total);prev=next;
            if(accumulated>=nextTurn*360){starts[nextTurn]=(i-1+Mathf.InverseLerp(before,accumulated,nextTurn*360))*clip.length/samples;nextTurn++;}
        }
        if(nextTurn!=10)throw new Exception("Incomplete rotation boundaries");
        gimmick.rotationStarts=starts;
        // Inspector exposes the seven contact beats independently of the full-rotation boundaries.
        gimmick.strikeTimes=new float[7];
        var sampleRoot=Skeleton(boss.animator.transform,null);
        sampleRoot.gameObject.hideFlags=HideFlags.HideAndDontSave;
        sampleRoot.SetPositionAndRotation(Vector3.zero,Quaternion.identity);sampleRoot.localScale=boss.animator.transform.lossyScale;
        try
        {
            var hit=boss.GetComponent<BossWeaponHit>();
            var weapon=sampleRoot.Find(AnimationUtility.CalculateTransformPath(hit.weapon,boss.animator.transform));
            for(int turn=1;turn<=7;turn++)
            {
                float best=float.PositiveInfinity;
                for(int k=0;k<400;k++)
                {
                    float time=Mathf.Lerp(starts[turn],starts[turn+1],k/400f);clip.SampleAnimation(sampleRoot.gameObject,time);
                    var center=weapon.TransformPoint(hit.localCenter);center.y=0;
                    float score=(center-Vector3.forward*gimmick.attackDistance).sqrMagnitude;
                    if(score<best){best=score;gimmick.strikeTimes[turn-1]=time;}
                }
            }
        }
        finally {UnityEngine.Object.DestroyImmediate(sampleRoot.gameObject);}
        gimmick.guardClip=battle.player.GetComponent<NewPlayerLocomotion>().parryClip;
        gimmick.hitClip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/3DBattle/NewPlayer/Converted/NewKnight_Hit_Preview.anim");
        var animator=battle.player.animator;
        var missing=AnimationUtility.GetCurveBindings(gimmick.hitClip).Where(b=>b.path!="" && animator.transform.Find(b.path)==null).Select(b=>b.path).Distinct().ToArray();
        if(missing.Length>0)throw new Exception("Hit animation binding mismatch: "+string.Join(",",missing));
        pattern.hitWindows=Enumerable.Range(1,7).Select(i=>new BossWeaponAttackPattern.HitWindow(starts[i],starts[i+1]-.00001f){downwardExtension=4.5f}).ToArray();
        pattern.hitStartSeconds=starts[1];pattern.hitEndSeconds=starts[8]-.00001f;
        pattern.displayName="Debug Phase 2 · seven timed parries (rotations 2–8)";
        EditorUtility.SetDirty(pattern);AssetDatabase.SaveAssetIfDirty(pattern);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        return new{scene=scene.name,rotationStarts=starts,strikeTimes=gimmick.strikeTimes,missingBindings=missing,player=battle.player.name,clipLength=clip.length};
    }
}

