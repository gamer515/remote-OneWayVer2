using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using OneWay.Battle3D;
public static class Boss9FacingSetup
{
    public static string Build()
    {
        if(Application.isPlaying) throw new Exception("Edit mode required");
        var boss=UnityEngine.Object.FindObjectsByType<BossApproachController>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single().GetComponent<BattleBoss>();
        var pattern=AssetDatabase.LoadAssetAtPath<BossWeaponAttackPattern>("Assets/3DBattle/Settings/B1_Attack9_Smooth10_20.asset");
        var copy=UnityEngine.Object.Instantiate(boss.gameObject);
        copy.hideFlags=HideFlags.HideAndDontSave;
        try {
            foreach(var b in copy.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled=false;
            var animator=copy.GetComponent<Animator>(); animator.enabled=false;
            var controller=(AnimatorController)boss.animator.runtimeAnimatorController;
            var idle=(AnimationClip)controller.layers[0].stateMachine.states.Single(s=>"Base Layer."+s.state.name==boss.idleState).state.motion;
            var chest=copy.transform.Find("target_character/Hips/Spine02/Spine01/Spine");
            idle.SampleAnimation(copy,0);
            var reference=chest.InverseTransformDirection(copy.transform.forward);
            int finalFrame=Mathf.RoundToInt(pattern.clip.length*pattern.clip.frameRate);
            int attackEndFrame=finalFrame-20;
            var source=AssetDatabase.LoadAssetAtPath<BossWeaponAttackPattern>("Assets/3DBattle/Settings/B1_Attack9.asset");
            if(attackEndFrame!=Mathf.RoundToInt(source.clip.length*source.clip.frameRate)+10)
                throw new Exception("Unexpected attack/recovery frame layout");
            var anchors=new[]{15,50,attackEndFrame};
            // No correction through frame 5; the original frame 5 is runtime frame 15.
            var curve=new AnimationCurve(new Keyframe(0,0),new Keyframe(5/pattern.clip.frameRate,0));
            float previousYaw=0;
            foreach(int frame in anchors.Distinct()) {
                pattern.clip.SampleAnimation(copy,frame/pattern.clip.frameRate);
                float yaw=Vector3.SignedAngle(Vector3.ProjectOnPlane(chest.TransformDirection(reference),Vector3.up),copy.transform.forward,Vector3.up);
                yaw=previousYaw+Mathf.DeltaAngle(previousYaw,yaw);
                curve.AddKey(new Keyframe(frame/pattern.clip.frameRate,yaw,0,0));
                previousYaw=yaw;
            }
            // Fade out over the existing 20-frame return to idle.
            curve.AddKey(new Keyframe(pattern.clip.length,0,0,0));
            var test=copy.GetComponent<BossAttackFacingCorrection>() ?? copy.AddComponent<BossAttackFacingCorrection>();
            test.enabled=false; test.animator=animator; test.skeletonRoot=copy.transform.Find("target_character");
            test.attack=pattern; test.yawDegrees=curve;
            var report=new System.Text.StringBuilder();
            foreach(int frame in new[]{0,5,15,50,attackEndFrame,finalFrame}.Distinct()) {
                test.ClearCorrection(); pattern.clip.SampleAnimation(copy,frame/pattern.clip.frameRate);
                var original=test.skeletonRoot.localRotation;
                test.ApplyAtTime(frame/pattern.clip.frameRate);
                var once=test.skeletonRoot.localRotation;
                test.ApplyAtTime(frame/pattern.clip.frameRate);
                if(Quaternion.Angle(once,test.skeletonRoot.localRotation)>.05f)throw new Exception("Accumulation");
                if(anchors.Contains(frame)) {
                    float error=Mathf.Abs(Vector3.SignedAngle(Vector3.ProjectOnPlane(chest.TransformDirection(reference),Vector3.up),copy.transform.forward,Vector3.up));
                    if(error>.05f)throw new Exception("Facing error "+error);
                    report.AppendLine("frame "+frame+" facing error="+error);
                } else if(Quaternion.Angle(original,once)>.05f)throw new Exception("Attack pose altered");
                test.ClearCorrection();
                if(Quaternion.Angle(original,test.skeletonRoot.localRotation)>.05f)throw new Exception("Cleanup failed");
            }
            var component=boss.GetComponent<BossAttackFacingCorrection>() ?? Undo.AddComponent<BossAttackFacingCorrection>(boss.gameObject);
            Undo.RecordObject(component,"Configure attack 9 facing");
            component.animator=boss.animator;
            component.skeletonRoot=boss.animator.transform.Find("target_character");
            component.attack=pattern; component.yawDegrees=curve;
            EditorUtility.SetDirty(component);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(boss.gameObject.scene);
            if(!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(boss.gameObject.scene)) throw new Exception("Save failed");
            return report+"PASS: no accumulation, cleanup restores pose, frames 0/5/"+finalFrame+" unmodified; attack end="+attackEndFrame+"; scene saved; animation assets untouched.";
        } finally {UnityEngine.Object.DestroyImmediate(copy);}
    }
    public static string Measure()
    {
        var boss=UnityEngine.Object.FindObjectsByType<BossApproachController>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single().GetComponent<BattleBoss>();
        var copy=UnityEngine.Object.Instantiate(boss.gameObject);
        copy.hideFlags=HideFlags.HideAndDontSave;
        try {
            foreach(var b in copy.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled=false;
            var animator=copy.GetComponent<Animator>(); animator.enabled=false;
            var controller=(AnimatorController)boss.animator.runtimeAnimatorController;
            var idle=(AnimationClip)controller.layers[0].stateMachine.states.Single(s=>"Base Layer."+s.state.name==boss.idleState).state.motion;
            var hips=copy.transform.Find("target_character/Hips/Spine02/Spine01/Spine");
            idle.SampleAnimation(copy,0);
            var reference=hips.InverseTransformDirection(copy.transform.forward);
            var report=new System.Text.StringBuilder();
            foreach(var path in new[]{"Assets/3DBattle/Settings/B1_Attack9.asset","Assets/3DBattle/Settings/B1_Attack9_Smooth10_20.asset"}){
                var p=AssetDatabase.LoadAssetAtPath<BossWeaponAttackPattern>(path);
                foreach(int frame in new[]{0,5,15,50}) {
                    p.clip.SampleAnimation(copy,frame/p.clip.frameRate);
                    var facing=Vector3.ProjectOnPlane(hips.TransformDirection(reference),Vector3.up);
                    report.AppendLine(p.name+" frame="+frame+" correction="+Vector3.SignedAngle(facing,copy.transform.forward,Vector3.up));
                }
            }
            return report.ToString();
        } finally {UnityEngine.Object.DestroyImmediate(copy);}
    }
}


