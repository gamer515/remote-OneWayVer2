using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;
public static class BossHitboxDebugSetup
{
    public static string Run()
    {
        if(Application.isPlaying) throw new Exception("Edit mode required");
        var brain=UnityEngine.Object.FindObjectsByType<BossApproachController>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single();
        const string path="Assets/3DBattle/Materials/BossHitboxDebug.mat";
        var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!mat) {
            mat=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.name="BossHitboxDebug"; mat.SetFloat("_Cull",0); mat.SetColor("_BaseColor",Color.white);
            AssetDatabase.CreateAsset(mat,path);
        }
        var copy=UnityEngine.Object.Instantiate(brain.gameObject);
        copy.hideFlags=HideFlags.HideAndDontSave;
        var dummy=new GameObject("HitboxTestTarget") {hideFlags=HideFlags.HideAndDontSave};
        try {
            foreach(var script in copy.GetComponentsInChildren<MonoBehaviour>(true))script.enabled=false;
            copy.SetActive(true);
            var animator=copy.GetComponent<BattleBoss>().animator;
            animator.enabled=true; animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind(); animator.Update(0);
            var health=dummy.AddComponent<BattlePlayerHealth>();health.ResetHealth();
            var hit=copy.GetComponent<BossWeaponHit>();hit.enabled=true;
            var debug=copy.GetComponent<BossWeaponHitDebug>() ?? copy.AddComponent<BossWeaponHitDebug>();
            debug.lineMaterial=mat;debug.showHitbox=true;debug.enabled=true;
            int checks=0;
            foreach(var p in brain.attacks.OfType<BossWeaponAttackPattern>()) {
                hit.Begin(p,health);
                foreach(float time in new[]{p.hitStartSeconds-1/p.clip.frameRate,p.hitStartSeconds+.01f/p.clip.frameRate,p.hitEndSeconds+1/p.clip.frameRate}) {
                    animator.Play(p.animationState,0,time/p.clip.length);animator.Update(0);
                    hit.SampleContact();debug.Refresh();
                    bool expected=time>=p.hitStartSeconds&&time<=p.hitEndSeconds;
                    if(hit.WindowOpen!=expected)throw new Exception("Window: "+p.name);
                    var line=copy.transform.Find("BossHitbox_DebugOnly").GetComponent<LineRenderer>();
                    if(!line||!line.enabled)throw new Exception("Line hidden");
                    if(line.sharedMaterial.GetColor("_BaseColor")!=(expected?debug.activeColor:debug.inactiveColor))throw new Exception("Wrong color");
                    hit.GetWorldBox(out var center,out var rotation,out var half);
                    for(int i=0;i<line.positionCount;i++) {
                        var point=Quaternion.Inverse(rotation)*(line.GetPosition(i)-center);
                        var absolute=new Vector3(Mathf.Abs(point.x),Mathf.Abs(point.y),Mathf.Abs(point.z));
                        if(Vector3.Distance(absolute,half)>.001f)throw new Exception("Wrong box geometry");
                    }
                    checks++;
                }
                hit.End();debug.Refresh();
            }
            debug.showHitbox=false;debug.Refresh();
            if(copy.transform.Find("BossHitbox_DebugOnly").GetComponent<LineRenderer>().enabled)throw new Exception("Toggle failed");
            if(health.Health!=health.maxHealth)throw new Exception("Unexpected test damage");
            var live=brain.GetComponent<BossWeaponHitDebug>() ?? Undo.AddComponent<BossWeaponHitDebug>(brain.gameObject);
            Undo.RecordObject(live,"Enable boss hitbox debug");
            live.lineMaterial=mat;live.showHitbox=true;live.showControls=true;live.enabled=true;
            EditorUtility.SetDirty(live);AssetDatabase.SaveAssetIfDirty(mat);
            EditorSceneManager.MarkSceneDirty(brain.gameObject.scene);EditorSceneManager.SaveScene(brain.gameObject.scene);
            return "PASS: "+checks+" samples across six attacks: timing/color/exact physics box, toggle off, no damage. Debug attached and scene saved.";
        } finally {
            UnityEngine.Object.DestroyImmediate(copy);UnityEngine.Object.DestroyImmediate(dummy);
        }
    }
}

