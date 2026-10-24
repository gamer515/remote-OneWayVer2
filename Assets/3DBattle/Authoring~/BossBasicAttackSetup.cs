using System;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using OneWay.Battle3D;
public static class BossBasicAttackSetup
{
    public static string Setup()
    {
        if(Application.isPlaying) throw new Exception("Edit mode required");
        var scene=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        var stage=scene.FindStage(1); var boss=stage.boss;
        if(boss.gameObject.scene.path!="Assets/Scenes/3DBattleScene.unity") throw new Exception("Wrong scene");
        var controller=(AnimatorController)boss.animator.runtimeAnimatorController;
        var clip=(AnimationClip)controller.layers[0].stateMachine.states.Single(s=>s.state.name=="Attack").state.motion;
        const string path="Assets/3DBattle/Settings/B1_BasicAttack3.asset";
        var pattern=AssetDatabase.LoadAssetAtPath<BossWeaponAttackPattern>(path);
        if(pattern==null)
        {
            pattern=ScriptableObject.CreateInstance<BossWeaponAttackPattern>();
            pattern.displayName="3번 기본 공격 · Pickaxe";
            pattern.animationState="Base Layer.Attack";
            pattern.damage=20; pattern.recoverySeconds=.3f;
            AssetDatabase.CreateAsset(pattern,path);
        }
        pattern.clip=clip;
        var weapon=boss.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="pickaxe");
        var hit=boss.GetComponent<BossWeaponHit>();
        if(hit==null) hit=Undo.AddComponent<BossWeaponHit>(boss.gameObject);
        Undo.RecordObject(hit,"Connect pickaxe contact");
        hit.weapon=weapon;
        var bounds=weapon.GetComponent<MeshFilter>().sharedMesh.bounds;
        hit.localCenter=bounds.center; hit.localSize=bounds.size;
        var brain=boss.GetComponent<BossApproachController>();
        Undo.RecordObject(brain,"Connect basic attack"); brain.basicAttack=pattern;
        Undo.RecordObject(stage.settings,"Register basic attack");
        if(!stage.settings.patterns.Contains(pattern)) stage.settings.patterns=stage.settings.patterns.Concat(new BossAttackPattern[]{pattern}).ToArray();
        EditorUtility.SetDirty(stage.settings); AssetDatabase.SaveAssetIfDirty(stage.settings);
        EditorUtility.SetDirty(pattern); AssetDatabase.SaveAssetIfDirty(pattern);
        EditorUtility.SetDirty(hit); EditorUtility.SetDirty(brain);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(boss.gameObject.scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(boss.gameObject.scene);
        Selection.activeGameObject=boss.gameObject;
        return "Attack 3 wired to B1 decisions and pattern list; Pickaxe box follows mesh bounds; damage 20, window 0.85–1.45 sec.";
    }
    static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    public static async System.Threading.Tasks.Task<string> Test()
    {
        Check(Application.isPlaying,"Play required");
        var scene=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        scene.SelectStage(1);
        var boss=scene.CurrentStage.boss;
        var brain=boss.GetComponent<BossApproachController>();
        var actions=boss.GetComponent<BossActionController>();
        var hit=boss.GetComponent<BossWeaponHit>();
        var pattern=(BossWeaponAttackPattern)brain.basicAttack;
        var player=scene.player; var health=player.GetComponent<BattlePlayerHealth>();
        var motor=player.GetComponent<CharacterController>();
        bool oldIgnore=health.ignoreDamage, background=Application.runInBackground;
        var previousAttacks=brain.attacks;
        brain.attacks=new BossAttackPattern[]{brain.basicAttack};
        var originalPosition=player.transform.position;
        float speed=boss.animator.speed;
        try
        {
            brain.automaticApproach=false; actions.RequestStop(); health.ignoreDamage=false; health.ResetHealth();
            player.enabled=false; motor.enabled=false; motor.enabled=true;
            player.transform.position=boss.transform.position+boss.transform.forward*4;
            brain.automaticApproach=true; brain.Tick(0); brain.automaticApproach=false;
            Check(actions.Current==BossActionKind.Attack,"AI starts attack inside range");
            boss.animator.speed=0;
            Action<float> sample=t=>{boss.animator.Play(pattern.animationState,0,t/pattern.clip.length);boss.animator.Update(0);};
            Action place=()=>{player.transform.position=hit.weapon.TransformPoint(hit.localCenter)-motor.center;Physics.SyncTransforms();};
            sample(.3f); place(); hit.SampleContact();
            Check(health.Health==health.maxHealth,"No windup damage");
            sample(1.1f); player.transform.position=boss.transform.position+Vector3.one*100; Physics.SyncTransforms(); hit.SampleContact();
            Check(health.Health==health.maxHealth,"No distance-only damage");
            place(); hit.SampleContact(); hit.SampleContact();
            Check(health.Health==health.maxHealth-pattern.damage && hit.HitCount==1,"Contact damages once");
            actions.RequestStop(); health.ResetHealth(); hit.SampleContact();
            Check(!hit.WindowOpen && health.Health==health.maxHealth,"Stop closes hit window");
            Check(actions.RequestAttack(pattern),"Second swing starts");
            sample(1.1f); place(); hit.SampleContact();
            Check(health.Health==health.maxHealth-pattern.damage,"Next swing can hit again");
            boss.TakeDamage(boss.maxHealth*.34f);
            Check(actions.Current==BossActionKind.HitReaction && !hit.WindowOpen,"Hit interrupts contact immediately");
            actions.Tick(10);
            health.ResetHealth(); player.transform.position=boss.transform.position+Vector3.one*100;
            boss.animator.speed=1; Application.runInBackground=true;
            Check(actions.RequestAttack(pattern),"Natural attack start");
            for(int i=0;i<100 && actions.IsBusy;i++) await System.Threading.Tasks.Task.Delay(50);
            Check(!actions.IsBusy && !hit.WindowOpen,"Natural completion closes contact");
            health.ResetHealth();
            player.transform.position=boss.transform.position+boss.transform.forward*4.99f;
            Physics.SyncTransforms();
            Check(actions.RequestAttack(pattern),"Range attack starts");
            for(int i=0;i<100 && actions.IsBusy;i++) await System.Threading.Tasks.Task.Delay(50);
            Check(health.Health<health.maxHealth,"Stationary target at approach stopping distance was missed");
            return "PASS: AI attack, no windup/miss damage, Pickaxe contact once per swing, next-swing reset, cancellation/hit cleanup, natural completion, actual animation hits stationary player at distance 4.99.";
        }
        finally
        {
            actions.RequestStop(); boss.animator.speed=speed; health.ignoreDamage=oldIgnore;
            Application.runInBackground=background; brain.automaticApproach=true; player.enabled=true;
            brain.attacks=previousAttacks;
            scene.SelectStage(2); scene.SelectStage(1); player.transform.position=originalPosition;
        }
    }
    public static string Inspect()
    {
        var scene=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        var boss=scene.FindStage(1).boss;
        var result=new StringBuilder();
        var controller=(AnimatorController)boss.animator.runtimeAnimatorController;
        var state=controller.layers[0].stateMachine.states.Single(s=>s.state.name=="Attack").state;
        result.AppendLine("Attack="+AssetDatabase.GetAssetPath(state.motion)+" length="+((AnimationClip)state.motion).length);
        foreach(var t in boss.GetComponentsInChildren<Transform>(true).Where(t=>t.name.IndexOf("pickaxe",StringComparison.OrdinalIgnoreCase)>=0))
        {
            result.AppendLine("Weapon "+AnimationUtility.CalculateTransformPath(t,boss.transform)+" scale="+t.lossyScale);
            foreach(var r in t.GetComponentsInChildren<Renderer>()) result.AppendLine("Renderer "+r.name+" localBounds="+r.localBounds+" worldBounds="+r.bounds+" type="+r.GetType().Name);
        }
        var cc=scene.player.GetComponent<CharacterController>();
        result.AppendLine("Player "+cc.bounds+" radius="+cc.radius+" height="+cc.height);
        var copy=UnityEngine.Object.Instantiate(boss.animator.gameObject);
        copy.hideFlags=HideFlags.HideAndDontSave;
        try
        {
            var weapon=copy.GetComponentsInChildren<Transform>().First(t=>t.name=="pickaxe");
            var mesh=weapon.GetComponent<MeshRenderer>();
            for(float time=0;time<2.84f;time+=.2f)
            {
                ((AnimationClip)state.motion).SampleAnimation(copy,time);
                result.AppendLine(time.ToString("F1")+" weapon local center="+copy.transform.InverseTransformPoint(mesh.bounds.center).ToString("F2"));
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(copy); }
        return result.ToString();
    }
}

