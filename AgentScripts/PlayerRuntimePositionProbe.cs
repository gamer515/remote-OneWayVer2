using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

public static class PlayerRuntimePositionProbe
{
    public static async Task<string> VerifyAnimatedMovement()
    {
        if(!Application.isPlaying) throw new InvalidOperationException("Live Play required");
        var prefab=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Decision_YYS/Prafabs/People/KnightCharacter.prefab");
        var instance=UnityEngine.Object.Instantiate(prefab);
        instance.name="PlayerAnimatedMovementCheck";
        foreach(var c in instance.GetComponentsInChildren<Camera>(true)) c.enabled=false;
        foreach(var r in instance.GetComponentsInChildren<Renderer>(true)) r.enabled=false;
        foreach(var c in instance.GetComponentsInChildren<Collider>(true)) c.enabled=false;
        var player=instance.GetComponent<Player>();
        var animator=instance.GetComponent<Animator>();
        animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        int firstFrame=Time.frameCount, checks=0;
        Action<bool,string> check=(ok,message)=>{if(!ok) throw new Exception(message);checks++;};
        var start=new Vector3(-220,0,10);
        try
        {
            player.Initialize(start);
            player.SetFacing(Quaternion.Euler(0,90,0));
            await Task.Delay(350);
            check(Time.frameCount-firstFrame>=3,"No actual Player frames elapsed");
            check(Vector3.Distance(instance.transform.position,start)<0.001f,"Animator reset startup position");
            check(Quaternion.Angle(instance.transform.rotation,Quaternion.Euler(0,90,0))<0.1f,"Animator reset saved facing");
            check(!animator.GetBool("IsWalking"),"idle marked as walking");
            player.FaceForward(); player.SetTargetZ(12);
            await Task.Delay(250);
            var moving=instance.transform.position;
            check(Mathf.Abs(moving.x+220)<0.001f && Mathf.Abs(moving.y)<0.001f && moving.z>10 && moving.z<=12,"animated walking left lane");
            check(animator.GetBool("IsWalking"),"walking animation did not start");
            player.StopAndLookAt(new Vector3(-216,0,moving.z));
            float stopped=instance.transform.position.z;
            await Task.Delay(200);
            check(Mathf.Abs(instance.transform.position.z-stopped)<0.001f,"look-at did not stop movement");
            check(Quaternion.Angle(instance.transform.rotation,Quaternion.Euler(0,90,0))<0.1f,"NPC-facing rotation overwritten");
            player.FaceForward(); player.SetTargetZ(12);
            await Task.Delay(800);
            check(Vector3.Distance(instance.transform.position,new Vector3(-220,0,12))<0.001f,"target not reached");
            check(!animator.GetBool("IsWalking"),"walking never returned to idle");
            check(Quaternion.Angle(instance.transform.rotation,Quaternion.identity)<0.1f,"forward facing lost");
            return checks+" live-frame checks passed over "+(Time.frameCount-firstFrame)+" real frames with original Animator enabled. Start/walk/stop/look/idle stable. No save writes.";
        }
        finally { UnityEngine.Object.Destroy(instance); }
    }
    public static async Task<string> ReproduceStartup()
    {
        if(!Application.isPlaying) throw new InvalidOperationException("Live Play required");
        var prefab=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Decision_YYS/Prafabs/People/KnightCharacter.prefab");
        var instance=UnityEngine.Object.Instantiate(prefab);
        instance.name="PlayerStartupProbe";
        foreach(var camera in instance.GetComponentsInChildren<Camera>(true)) camera.enabled=false;
        foreach(var renderer in instance.GetComponentsInChildren<Renderer>(true)) renderer.enabled=false;
        foreach(var collider in instance.GetComponentsInChildren<Collider>(true)) collider.enabled=false;
        var player=instance.GetComponent<Player>();
        var animator=instance.GetComponent<Animator>();
        animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        player.enabled=false;
        var target=new Vector3(-220,0,10);
        try
        {
            player.Initialize(target);
            var initial=instance.transform.position;
            await Task.Delay(200);
            var afterFirstAnimation=instance.transform.position;
            instance.transform.position=target;
            await Task.Delay(200);
            return "Initialize="+initial.ToString("F3")+"; first animator evaluation="+afterFirstAnimation.ToString("F3")+
                "; after manual reapply="+instance.transform.position.ToString("F3")+". Player Update disabled; Animator only. No saves.";
        }
        finally { UnityEngine.Object.Destroy(instance); }
    }
    public static async Task<string> CompareAnimator()
    {
        if(!Application.isPlaying) throw new InvalidOperationException("Live Play required");
        var player=UnityEngine.Object.FindFirstObjectByType<Player>();
        var manager=UnityEngine.Object.FindFirstObjectByType<DecisionManager>();
        var field=typeof(DecisionManager).GetField("playerController",BindingFlags.NonPublic|BindingFlags.Instance);
        var control=(DecisionPlayerController)field.GetValue(manager);
        var animator=player.GetComponent<Animator>();
        Vector3 original=player.transform.position, target=control.TargetPosition;
        Quaternion rotation=player.transform.rotation;
        bool enabled=animator.enabled;
        try
        {
            player.transform.position=target;
            await Task.Delay(150);
            Vector3 animated=player.transform.position;
            animator.enabled=false;
            player.transform.position=target;
            await Task.Delay(150);
            Vector3 noAnimator=player.transform.position;
            return "target="+target.ToString("F3")+"; animator enabled="+animated.ToString("F3")+
                "; animator disabled="+noAnimator.ToString("F3")+"; original restored. No save writes.";
        }
        finally
        {
            player.transform.SetPositionAndRotation(original,rotation);
            animator.enabled=enabled;
        }
    }
}
