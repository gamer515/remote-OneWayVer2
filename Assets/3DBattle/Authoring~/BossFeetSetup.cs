using System;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using OneWay.Battle3D;
public static class BossFeetSetup
{
    public static string Setup()
    {
        if(Application.isPlaying) throw new Exception("Edit required");
        var scene=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        var boss=scene.FindStage(1).boss;
        if(boss.gameObject.scene.path!="Assets/Scenes/3DBattleScene.unity") throw new Exception("Wrong scene");
        var feet=boss.GetComponent<BossFootGrounding>()??Undo.AddComponent<BossFootGrounding>(boss.gameObject);
        Undo.RecordObject(feet,"Configure foot grounding");
        feet.skin=boss.GetComponentInChildren<SkinnedMeshRenderer>();
        feet.skeletonRoot=boss.animator.transform.Find("target_character");
        feet.leftFoot=boss.GetComponentsInChildren<Transform>().Single(t=>t.name=="LeftFoot");
        feet.rightFoot=boss.GetComponentsInChildren<Transform>().Single(t=>t.name=="RightFoot");
        feet.battle=scene;
        EditorUtility.SetDirty(feet);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(boss.gameObject.scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(boss.gameObject.scene);
        return "B1 foot grounding configured";
    }
    public static string Test()
    {
        if(!Application.isPlaying) throw new Exception("Play required");
        var scene=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>(); scene.SelectStage(1);
        var boss=scene.CurrentStage.boss; var brain=boss.GetComponent<BossApproachController>();
        var actions=boss.GetComponent<BossActionController>(); var feet=boss.GetComponent<BossFootGrounding>();
        var skin=feet.skin; var source=skin.sharedMesh; var weights=source.boneWeights; var bones=skin.bones;
        Func<int,bool> footBone=i=>bones[i].name.Contains("Foot")||bones[i].name.Contains("Toe");
        var indices=Enumerable.Range(0,weights.Length).Where(i=>{
            var w=weights[i]; return (footBone(w.boneIndex0)?w.weight0:0)+(footBone(w.boneIndex1)?w.weight1:0)+(footBone(w.boneIndex2)?w.weight2:0)+(footBone(w.boneIndex3)?w.weight3:0)>=.5f;
        }).ToArray();
        var mesh=new Mesh(); int count=0; float worst=0,maxLift=0;
        try
        {
            brain.automaticApproach=false; actions.RequestStop();
            var root=boss.transform.position;
            foreach(var state in ((AnimatorController)boss.animator.runtimeAnimatorController).layers[0].stateMachine.states)
            {
                var clip=state.state.motion as AnimationClip; if(clip==null) continue;
                for(int f=0;f<=30;f++)
                {
                    feet.RemoveCorrection(); clip.SampleAnimation(boss.animator.gameObject,clip.length*f/30f);
                    feet.CorrectPose(); skin.BakeMesh(mesh,true);
                    var vertices=mesh.vertices;
                    float minimum=indices.Min(i=>skin.transform.TransformPoint(vertices[i]).y);
                    // Arena floor at the character location, excluding actors and boundary walls.
                    float floor=Physics.RaycastAll(root+Vector3.up*20,Vector3.down,100).Where(h=>h.normal.y>.5f && h.collider.GetComponentInParent<BattleBoss>()==null && h.collider.GetComponentInParent<BattlePlayerController>()==null && !scene.CurrentStage.boundary.walls.Contains(h.collider)).Max(h=>h.point.y);
                    worst=Mathf.Min(worst,minimum-floor); maxLift=Mathf.Max(maxLift,feet.AppliedLift);
                    if(minimum<floor-.005f) throw new Exception(state.state.name+" feet below floor "+(minimum-floor));
                    if(Vector3.Distance(root,boss.transform.position)>.001f) throw new Exception("Moved gameplay root");
                    count++;
                }
            }
            return "PASS: "+count+" animation samples; foot mesh penetration="+worst+"; max model lift="+maxLift+"; gameplay root unchanged; probes="+indices.Length;
        }
        finally {feet.RemoveCorrection(); UnityEngine.Object.Destroy(mesh); brain.automaticApproach=true; scene.SelectStage(2);scene.SelectStage(1);}
    }
    public static string Inspect()
    {
        var scene=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        var boss=scene.FindStage(1).boss;
        var result=new StringBuilder("root="+boss.transform.position+"\n");
        foreach(var t in boss.GetComponentsInChildren<Transform>().Where(t=>t.name.Contains("Foot") || t.name.Contains("Toe"))) result.AppendLine(t.name+" "+t.position);
        var copy=UnityEngine.Object.Instantiate(boss.animator.gameObject); copy.hideFlags=HideFlags.HideAndDontSave;
        var mesh=new Mesh();
        try
        {
            var states=((AnimatorController)boss.animator.runtimeAnimatorController).layers[0].stateMachine.states;
            foreach(var s in states)
            {
                var clip=s.state.motion as AnimationClip; if(clip==null) continue;
                float min=999,max=-999;
                for(int frame=0;frame<=30;frame++)
                {
                    clip.SampleAnimation(copy,clip.length*frame/30f);
                    foreach(var r in copy.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        r.BakeMesh(mesh,true);
                        float y=mesh.vertices.Min(v=>r.transform.TransformPoint(v).y);
                        min=Mathf.Min(min,y); max=Mathf.Max(max,y);
                    }
                }
                result.AppendLine(s.state.name+": lowest mesh y range "+min+" .. "+max);
            }
        }
        finally {UnityEngine.Object.DestroyImmediate(copy);UnityEngine.Object.DestroyImmediate(mesh);}
        return result.ToString();
    }
}

