using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// Maintained Play-mode regression: real Animator updates, entrance/cancel/restart, no persistent save writes.
public static class KnightAnimationChecks
{
    static readonly string Output = Path.GetFullPath("output/KnightAnimationReview");
    public static object Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Run in DecisionScene Play mode.");
        var bridge=UnityEngine.Object.FindFirstObjectByType<DuelMiniGameBridge>();
        if(bridge==null)throw new InvalidOperationException("DecisionScene is required.");
        Directory.CreateDirectory(Output);
        bridge.StartCoroutine(Sequence(bridge));
        return new{started=true,result=Path.Combine(Output,"checks.json")};
    }
    static IEnumerator Sequence(DuelMiniGameBridge bridge)
    {
        var checks=new List<object>(); var phases=new List<string>(); var samples=new List<object>();
        var gestures=new HashSet<string>();
        void Check(bool passed,string message) => checks.Add(new{passed,message});
        void Capture(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(Output,name+".png"));
        float previousScale=Time.timeScale; var randomState=UnityEngine.Random.state;
        try
        {
            bridge.CleanupDuel(); yield return null; yield return null;
            var preview=UnityEngine.Object.FindFirstObjectByType<KnightPresentation>();
            preview.ResetSeated();
            int frame=Time.frameCount; float start=Time.time;
            Time.timeScale=4f;
            var animator=preview.GetComponent<Animator>();
            Quaternion leftArm=animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).localRotation;
            Quaternion rightArm=animator.GetBoneTransform(HumanBodyBones.RightUpperArm).localRotation;
            float maxGestureJointChange=0f;
            Vector3 original=preview.transform.position;
            while(Time.time-start<95f)
            {
                var info=animator.GetCurrentAnimatorStateInfo(0);
                maxGestureJointChange=Mathf.Max(maxGestureJointChange,
                    Quaternion.Angle(leftArm,animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).localRotation),
                    Quaternion.Angle(rightArm,animator.GetBoneTransform(HumanBodyBones.RightUpperArm).localRotation));
                if(info.normalizedTime>0.25f && info.normalizedTime<0.8f)
                {
                    if(info.IsName("Sitting And Pointing")) {if(gestures.Add("Sitting And Pointing"))Capture("02-pointing");}
                    if(info.IsName("Sitting Rubbing Arm")) {if(gestures.Add("Sitting Rubbing Arm"))Capture("03-rubbing-arm");}
                }
                if(gestures.Count==2 && info.IsName("Sitting Idle") && !animator.IsInTransition(0)) break;
                yield return null;
            }
            Check(Time.frameCount>frame+10,"Player loop advances during real animation playback");
            Check(gestures.Count==2,"Both random gestures play and return to Sitting Idle");
            Check(maxGestureJointChange>5f,"Retargeted gestures move actual arm bones (max change "+maxGestureJointChange.ToString("F2")+" degrees)");
            Check(Vector3.Distance(original,preview.transform.position)<0.001f,"Seated gestures do not drift the root");
            Check(animator.isHuman && animator.avatar.isValid,"Knight_For_Ani uses a valid Humanoid Avatar");
            Capture("01-seated-idle");
            Time.timeScale=1f;
            bridge.BeginTestDuel(); start=Time.time;
            bool handsBeforeArrival=false; bool hiddenDuringEntrance=false;bool shotDismount=false,shotWalk=false;
            Vector3? previousHips=null;float maxHipStep=0f,maxUnexplainedStep=0f,previousDelta=Time.deltaTime;
            while(Time.time-start<18f && !bridge.IsRunning)
            {
                var knight=bridge.CurrentKnightTarget;
                if(knight!=null)
                {
                    var p=knight.GetComponent<KnightPresentation>();string phase=p.CurrentPhase.ToString();
                    var hip=knight.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Hips).position;
                    if(previousHips.HasValue)
                    {
                        float step=Vector3.Distance(hip,previousHips.Value);
                        if(step>maxHipStep) samples.Add(new{eventName="largestPelvisStep",phase,time=Time.time-start,step,dt=Time.deltaTime,previousDelta});
                        maxHipStep=Mathf.Max(maxHipStep,step);
                        // Screenshots may stall a frame. Allow elapsed-time travel at 3 m/s, then detect unexplained jumps.
                        maxUnexplainedStep=Mathf.Max(maxUnexplainedStep,step-3f*Mathf.Max(Time.deltaTime,previousDelta));
                    }
                    previousDelta=Time.deltaTime;
                    previousHips=hip;
                    hiddenDuringEntrance|=!preview.gameObject.activeSelf;
                    handsBeforeArrival|=bridge.AuthoringReferences.SpawnedNpcHand!=null;
                    if(phases.Count==0||phases[phases.Count-1]!=phase)
                    {
                        phases.Add(phase);
                        samples.Add(new{phase,time=Time.time-start,root=knight.transform.position.ToString("F4"),hips=knight.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Hips).position.ToString("F4")});
                    }
                    if(phase=="Dismount" && Time.time-start>0.65f && !shotDismount) {Capture("04-dismount");shotDismount=true;}
                    if(phase=="Walking" && Time.time-start>2.7f && !shotWalk) {Capture("05-walking");shotWalk=true;}
                }
                yield return null;
            }
            Check(bridge.IsRunning,"Duel starts after entrance finishes");
            Check(maxUnexplainedStep<0.12f,"Pelvis motion remains continuous with elapsed-time allowance (max unexplained step "+maxUnexplainedStep.ToString("F4")+" m; raw step "+maxHipStep.ToString("F4")+" m)");
            Check(phases.SequenceEqual(new[]{"Dismount","Walking","Warrior"}),"Entrance plays Dismount -> Walking -> Warrior in order");
            Check(!handsBeforeArrival,"Duel hands/input wait for the entrance to finish");
            Check(hiddenDuringEntrance,"Seated preview is hidden while its moving duplicate is visible");
            var setup=UnityEngine.Object.FindFirstObjectByType<TutorialMiniGameController>();
            Check(bridge.CurrentKnightTarget!=null && Vector3.Distance(bridge.CurrentKnightTarget.transform.position,setup.KnightSpawnPose.position)<0.001f,"Knight reaches the authored board destination");
            if(bridge.CurrentKnightTarget!=null)
            {
                var a=bridge.CurrentKnightTarget.GetComponent<Animator>();
                Check(a.GetCurrentAnimatorStateInfo(0).IsName("Warrior Idle"),"Existing duel logic preserves Warrior Idle");
                Check(a.HasState(0,Animator.StringToHash("HumanArmature_Attacked")),"Existing knight hit-reaction state is retained");
            }
            Capture("06-warrior-idle");
            yield return new WaitForSeconds(0.2f);
            var hand=bridge.AuthoringReferences.SpawnedNpcHand;
            Check(hand!=null && Vector3.Distance(hand.transform.position,new Vector3(-5.36f,-7.7199f,-2f))<0.01f,"Generated NPC hand inherits the marked permanent start position");
            Check(hand!=null && Quaternion.Angle(hand.transform.rotation,Quaternion.Euler(-73.519f,170.87f,-21.078f))<0.1f,"Generated NPC hand inherits the marked rotation");
            var mouse=UnityEngine.Object.FindFirstObjectByType<DuelMouseHandController>();
            Check(mouse.TryCaptureHand(),"Reaction verification captures the player hand to resume combat time");
            var npc=UnityEngine.Object.FindFirstObjectByType<NpcDuelStateMachine>();
            npc.ReceiveNpcHit();yield return null;
            Check(npc.IsKnightReacting,"Existing knight hit reaction actually starts");
            float reactionStart=Time.time;
            while((npc.IsKnightReacting || !bridge.CurrentKnightTarget.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0).IsName("Warrior Idle")) && Time.time-reactionStart<5f)yield return null;
            Check(!npc.IsKnightReacting && bridge.CurrentKnightTarget.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0).IsName("Warrior Idle"),"Hit reaction returns to Warrior Idle");
            bridge.CleanupDuel();yield return null;yield return null;
            Check(preview.gameObject.activeSelf && preview.CurrentPhase==KnightPresentation.Phase.Seated,"Cleanup restores the seated preview");
            bridge.BeginTestDuel();yield return null;bridge.CleanupDuel();yield return new WaitForSeconds(2.2f);
            Check(!bridge.IsRunning && bridge.CurrentKnightTarget==null,"Cancelling the entrance prevents a late duel start");
            Check(preview.gameObject.activeSelf,"Cancelling the entrance restores the seated preview");
        }
        finally
        {
            bridge.CleanupDuel();Time.timeScale=previousScale;UnityEngine.Random.state=randomState;
            var result=new{checks,phases,samples,gestures=gestures.ToArray(),time=DateTimeOffset.Now.ToString("O")};
            File.WriteAllText(Path.Combine(Output,"checks.json"),Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));
        }
    }
}
