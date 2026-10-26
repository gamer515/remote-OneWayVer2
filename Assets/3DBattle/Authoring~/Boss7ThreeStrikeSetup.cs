using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using OneWay.Battle3D;
public static class Boss7ThreeStrikeSetup
{
    static void Check(bool ok,string message) {if(!ok)throw new Exception(message);}
    public static string Run()
    {
        Check(!Application.isPlaying,"Edit mode required");
        var brain=UnityEngine.Object.FindObjectsByType<BossApproachController>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single();
        var asset=AssetDatabase.LoadAssetAtPath<BossWeaponAttackPattern>("Assets/3DBattle/Settings/B1_Attack7_Smooth10_20.asset");
        Check(brain.attacks.Contains(asset),"7 not connected");
        var p=UnityEngine.Object.Instantiate(asset);
        p.hitWindows=new[]{new BossWeaponAttackPattern.HitWindow(35f/30,46f/30),new BossWeaponAttackPattern.HitWindow(47f/30,74f/30),new BossWeaponAttackPattern.HitWindow(75f/30,94f/30)};
        p.independentStrikes=true;p.stepDecisionSeconds=new[]{10f/30,46f/30,74f/30};
        p.hitStartSeconds=35f/30;p.hitEndSeconds=94f/30;
        Check(p.TimingIsValid(),"Invalid timing");
        var copy=UnityEngine.Object.Instantiate(brain.gameObject);copy.hideFlags=HideFlags.HideAndDontSave;
        var dummy=new GameObject("Boss7TestPlayer"){hideFlags=HideFlags.HideAndDontSave};
        try {
            foreach(var script in copy.GetComponentsInChildren<MonoBehaviour>(true))script.enabled=false;
            copy.SetActive(true);
            var health=dummy.AddComponent<BattlePlayerHealth>();health.maxHealth=1000;health.ResetHealth();
            var collider=dummy.AddComponent<BoxCollider>();collider.size=Vector3.one*.05f;
            var extra=new GameObject("second collider");extra.transform.SetParent(dummy.transform,false);extra.AddComponent<BoxCollider>().size=Vector3.one*.05f;
            var animator=copy.GetComponent<BattleBoss>().animator;animator.enabled=true;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();animator.Update(0);
            var hit=copy.GetComponent<BossWeaponHit>();hit.enabled=true;
            var motion=copy.GetComponent<BossAttackMotion>();
            var runner=copy.GetComponent<BossPatternRunner>();runner.SetTarget(health);
            var body=copy.GetComponent<BattleArenaBody>();if(body)body.battle=null;
            Action<float> pose=frame=>{animator.Play(p.animationState,0,frame/30/p.clip.length);animator.Update(0);};
            Action<float> contact=frame=>{pose(frame);dummy.transform.position=hit.weapon.TransformPoint(hit.localCenter);Physics.SyncTransforms();hit.SampleContact();};
            hit.Begin(p,health);
            foreach(float frame in new[]{34f,46.5f,74.5f,95f}){contact(frame);Check(!hit.WindowOpen&&hit.HitCount==0,"Inactive frame "+frame);}
            foreach(int frame in new[]{35,47,75})Check(p.GetStrikeIndex(frame/30f)>=0,"Start boundary "+frame);
            hit.End();health.ResetHealth();hit.Begin(p,health);
            int count=0;
            foreach(float frame in new[]{36f,48f,76f}){
                contact(frame);count++;
                Check(hit.HitCount==count&&Mathf.Abs(health.Health-(1000-count*p.damage))<.001f,"Separate strike "+count);
                contact(frame+.1f);Check(hit.HitCount==count,"Duplicate hit");
            }
            // A new execution or an unrelated damage source must not bypass the same 3s timer.
            hit.End();hit.Begin(p,health);contact(36);Check(hit.HitCount==0,"New attack bypassed invulnerability");
            health.TakeDamage(10);Check(Mathf.Abs(health.Health-(1000-3*p.damage))<.001f,"Other damage bypassed invulnerability");
            health.ResetHealth();health.hitInvulnerabilitySeconds=0;hit.End();hit.Begin(p,health);
            contact(36);contact(45);Check(hit.HitCount==1,"Same swing rehit with zero invulnerability");
            hit.End();health.hitInvulnerabilitySeconds=3;
            // Verify defensive states survive the new independent-strike path.
            var player=dummy.AddComponent<BattlePlayerController>();player.enabled=false;
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            health.ResetHealth();Check(health.TakeAttackDamage(10,hit,999,0,true),"Initial damage");
            typeof(BattlePlayerController).GetField("dashRemaining",flags).SetValue(player,1f);
            Check(!health.TakeAttackDamage(10,hit,999,1,true),"Dodge bypassed");
            typeof(BattlePlayerController).GetField("dashRemaining",flags).SetValue(player,0f);
            typeof(BattlePlayerController).GetMethod("SetDefenseMode",flags).Invoke(player,new object[]{true});
            typeof(BattlePlayerController).GetField("blockRemaining",flags).SetValue(player,1f);
            Check(!health.TakeAttackDamage(10,hit,999,1,true),"Block bypassed");
            typeof(BattlePlayerController).GetField("blockRemaining",flags).SetValue(player,0f);
            health.ignoreDamage=true;Check(!health.TakeAttackDamage(10,hit,999,1,true),"IgnoreDamage bypassed");health.ignoreDamage=false;
            Check(health.TakeAttackDamage(10,hit,999,1,true),"Next strike blocked");
            collider.enabled=false;extra.SetActive(false);dummy.GetComponent<CharacterController>().enabled=false;
            var origin=copy.transform.position;
            Func<float,Vector3> travel=f=>new Vector3(p.travelX.Evaluate(f/30),0,p.travelZ.Evaluate(f/30));
            Action<float> advance=f=>{pose(f);motion.Advance();};
            Action<bool> distance=far=>dummy.transform.position=copy.transform.position+copy.transform.forward*(far?100:1);
            // All near: no displacement. All far: preserve the authored full trajectory.
            foreach(bool far in new[]{false,true}) {
                copy.transform.position=origin;motion.Begin(p);
                foreach(float f in new[]{10.01f,45.9f,46.01f,73.9f,74.01f,94f,121f,141f}){distance(far);advance(f);Check(motion.StepAllowed==far,"Decision "+f);}
                Vector3 expected=far?copy.transform.TransformVector(travel(141)-travel(0)):Vector3.zero;
                expected.y=0;
                Check(Vector3.Distance(copy.transform.position-origin,expected)<.003f,"Near/far displacement");motion.End();
            }
            // First and third stay, second moves: skipped displacement must not accumulate.
            copy.transform.position=origin;motion.Begin(p);distance(false);advance(10.01f);advance(45.9f);
            distance(true);advance(46.01f);Check(motion.CurrentStepIndex==1&&motion.StepAllowed,"Second decision");advance(73.9f);
            distance(false);advance(74.01f);Check(motion.CurrentStepIndex==2&&!motion.StepAllowed,"Third decision");advance(141f);
            var mixed=copy.transform.TransformVector(travel(74)-travel(46));mixed.y=0;
            Check(Vector3.Distance(copy.transform.position-origin,mixed)<.003f,"Skipped step movement debt");
            motion.End();Check(!motion.StepAllowed&&motion.CurrentStepIndex==-1,"Motion cancellation");
            // Legacy attacks still use their original one window and ungated movement.
            foreach(var other in brain.attacks.OfType<BossWeaponAttackPattern>().Where(a=>a!=asset))Check(other.StrikeCount==1&&other.TimingIsValid(),"Legacy timing changed");
            Undo.RecordObject(asset,"Split attack 7 strikes and conditional steps");
            asset.hitWindows=p.hitWindows;asset.independentStrikes=true;asset.stepDecisionSeconds=p.stepDecisionSeconds;
            asset.hitStartSeconds=p.hitStartSeconds;asset.hitEndSeconds=p.hitEndSeconds;
            EditorUtility.SetDirty(asset);AssetDatabase.SaveAssetIfDirty(asset);
            return "PASS: three separate hits during 3s invulnerability; duplicate colliders/no repeated strike even with 0s invulnerability; new execution and unrelated damage blocked; dodge/block/ignoreDamage preserved; inactive gaps; near/far/near-far-near movement and no debt; cancellation; five legacy timings preserved. Saved runtime windows 35-46,47-74,75-94 and decisions 10,46,74.";
        } finally {UnityEngine.Object.DestroyImmediate(copy);UnityEngine.Object.DestroyImmediate(dummy);UnityEngine.Object.DestroyImmediate(p);}
    }
}
