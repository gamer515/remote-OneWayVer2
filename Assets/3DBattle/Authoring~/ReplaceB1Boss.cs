using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using OneWay.Battle3D;
public static class ReplaceB1Boss
{
    public static string Apply()
    {
        var scene=SceneManager.GetActiveScene();
        if(Application.isPlaying || scene.path!="Assets/Scenes/3DBattleScene.unity") throw new Exception("Open 3DBattleScene in Edit mode");
        var c=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        var stage=c.FindStage(1);
        var old=stage.boss;
        if(old==null || old.name=="Boss1_AllAnimations") throw new Exception("Missing old boss or already replaced");
        var others=c.stages.Where(s=>s!=stage).Select(s=>s.boss).ToArray();
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/3DBattle/Boss1/Boss1_AllAnimations.prefab");
        if(prefab==null) throw new Exception("Missing prefab");
        Undo.RegisterFullObjectHierarchyUndo(c.gameObject,"Replace B1 boss");
        var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,stage.transform);
        Undo.RegisterCreatedObjectUndo(go,"Create B1 boss");
        go.name="Boss1_AllAnimations";
        go.transform.SetPositionAndRotation(old.transform.position,old.transform.rotation);
        foreach(var debug in go.GetComponentsInChildren<MeshyAnimationDebugger>(true)) debug.enabled=false;
        var animator=go.GetComponentInChildren<Animator>(true);
        animator.enabled=true;
        animator.applyRootMotion=false;
        var boss=Undo.AddComponent<BattleBoss>(go);
        boss.maxHealth=stage.settings.bossHealth;
        boss.patrolEnabled=old.patrolEnabled;
        boss.patrolDistance=old.patrolDistance;
        boss.patrolSpeed=old.patrolSpeed;
        boss.animator=animator;
        boss.idleState="Base Layer.Axe_Breathe_and_Look_Around";
        boss.walkState="Base Layer.Spear_Walk";
        boss.deadState="Base Layer.Knock_Down";
        boss.bodyParts=go.GetComponentsInChildren<Renderer>(true);
        boss.body=boss.bodyParts.First();
        var bounds=boss.body.bounds;
        foreach(var r in boss.bodyParts) bounds.Encapsulate(r.bounds);
        var collider=Undo.AddComponent<CapsuleCollider>(go);
        collider.center=go.transform.InverseTransformPoint(bounds.center);
        collider.height=Mathf.Max(.1f,bounds.size.y/Mathf.Abs(go.transform.lossyScale.y));
        collider.radius=Mathf.Max(.1f,Mathf.Min(bounds.size.x,bounds.size.z)*.35f/Mathf.Max(Mathf.Abs(go.transform.lossyScale.x),Mathf.Abs(go.transform.lossyScale.z)));
        boss.hitbox=collider;
        var runner=Undo.AddComponent<BossPatternRunner>(go);
        if(stage.patternRunner.telegraph!=null)
        {
            runner.telegraph=stage.patternRunner.telegraph;
            Undo.SetTransformParent(runner.telegraph.transform,go.transform,"Keep attack telegraph");
            runner.telegraph.enabled=false;
        }
        stage.boss=boss;
        stage.patternRunner=runner;
        if(c.player.boss==old) c.player.boss=boss;
        if(c.feedback.boss==old) c.feedback.boss=boss;
        Undo.DestroyObjectImmediate(old.gameObject);
        if(c.stages.Where(s=>s!=stage).Select(s=>s.boss).Where((b,i)=>b!=others[i]).Any()) throw new Exception("Other stage boss changed");
        PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
        foreach(var debug in go.GetComponentsInChildren<MeshyAnimationDebugger>(true)) PrefabUtility.RecordPrefabInstancePropertyModifications(debug);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject=go;
        return "Replaced B1 only; prefab scale="+go.transform.localScale+", rendered size="+bounds.size+", collider and battle references connected.";
    }
    public static string Validate()
    {
        if(!Application.isPlaying) throw new Exception("Play mode required");
        var c=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        c.SelectStage(1);
        var b=c.CurrentStage.boss;
        if(b.name!="Boss1_AllAnimations" || c.player.boss!=b || c.feedback.boss!=b || b.hitbox==null || !b.hitbox.enabled) throw new Exception("Invalid boss wiring");
        foreach(var state in new[]{b.idleState,b.walkState,b.deadState,"Base Layer.Attack"})
            if(!b.animator.HasState(0,Animator.StringToHash(state))) throw new Exception("Missing animation: "+state);
        if(b.GetComponentsInChildren<MeshyAnimationDebugger>().Any(d=>d.enabled)) throw new Exception("Debugger active");
        float hp=b.Health; b.TakeDamage(10f);
        if(b.Health!=hp-10f) throw new Exception("Damage failed");
        if(!c.RunPattern(0)) throw new Exception("Attack pattern failed");
        c.StopPattern();
        c.SelectStage(2); c.SelectStage(3); c.SelectStage(1);
        if(b.Health!=b.maxHealth) throw new Exception("Reset failed");
        return "PASS: B1 prefab, references, collider, idle/walk/death/attack states, damage, pattern execution, B2/B3 switching and reset.";
    }
}
