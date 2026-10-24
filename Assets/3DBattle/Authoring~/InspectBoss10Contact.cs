using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEditor;
using OneWay.Battle3D;
public static class InspectBoss10Contact
{
    static string Span(List<float> frames)=>frames.Count==0?"none":$"{frames.Min():F1}..{frames.Max():F1} ({frames.Count} samples)";
    public static string Run()
    {
        var brain=UnityEngine.Object.FindObjectsByType<BossApproachController>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single();
        var p=AssetDatabase.LoadAssetAtPath<BossWeaponAttackPattern>("Assets/3DBattle/Settings/B1_Attack10_Smooth10_20.asset");
        var cc=UnityEngine.Object.FindObjectsByType<BattlePlayerHealth>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single().GetComponent<CharacterController>();
        var copy=UnityEngine.Object.Instantiate(brain.gameObject);copy.hideFlags=HideFlags.HideAndDontSave;
        var probes=new GameObject("Diagnostic probes"){hideFlags=HideFlags.HideAndDontSave};
        try {
            foreach(var b in copy.GetComponentsInChildren<MonoBehaviour>(true))b.enabled=false;
            foreach(var c in copy.GetComponentsInChildren<Collider>(true))c.enabled=false;
            var animator=copy.GetComponent<BattleBoss>().animator;animator.enabled=false;
            copy.transform.rotation=Quaternion.identity;
            var origin=new Vector3(0,brain.transform.position.y,0);
            var hit=copy.GetComponent<BossWeaponHit>();var grounding=copy.GetComponent<BossFootGrounding>();
            var box=probes.AddComponent<BoxCollider>();box.isTrigger=true;
            var cap=probes.AddComponent<CapsuleCollider>();cap.isTrigger=true;
            cap.direction=1;cap.center=cc.center;cap.height=cc.height;cap.radius=cc.radius;
            var tall=probes.AddComponent<CapsuleCollider>();tall.isTrigger=true;tall.direction=1;tall.height=100;tall.radius=cc.radius;tall.center=Vector3.zero;
            float[] distances={2,3,4,4.9f,6};
            var full=distances.Select(d=>new List<float>()).ToArray();var active=distances.Select(d=>new List<float>()).ToArray();
            var horizontal=distances.Select(d=>new List<float>()).ToArray();var pre=distances.Select(d=>new List<float>()).ToArray();
            var lowered=distances.Select(d=>new List<float>()).ToArray();
            var gridBefore=new List<float>();var gridActive=new List<float>();
            var rows=new StringBuilder();float activeMin=float.PositiveInfinity,activeMax=float.NegativeInfinity;
            int count=Mathf.RoundToInt(p.clip.length*p.clip.frameRate*10);
            for(int i=0;i<=count;i++){
                float frame=i/10f,time=frame/p.clip.frameRate;
                grounding?.RemoveCorrection();p.clip.SampleAnimation(copy,time);
                copy.transform.position=origin+copy.transform.TransformVector(new Vector3(p.travelX.Evaluate(time),0,p.travelZ.Evaluate(time)));
                grounding?.CorrectPose();
                hit.GetWorldBox(out var center,out var rotation,out var half);box.size=half*2;
                var x=rotation*Vector3.right*half.x;var y=rotation*Vector3.up*half.y;var z=rotation*Vector3.forward*half.z;
                float extentY=Mathf.Abs(x.y)+Mathf.Abs(y.y)+Mathf.Abs(z.y),bottom=center.y-extentY,top=center.y+extentY;
                bool enabled=p.GetStrikeIndex(time)>=0;
                if(enabled){activeMin=Mathf.Min(activeMin,bottom);activeMax=Mathf.Max(activeMax,top);}
                if(i%20==0&&frame>=20&&frame<=52)rows.AppendLine($"f{frame:F0} boxY={bottom:F2}..{top:F2} centerXZ={center.x:F2},{center.z:F2} active={enabled}");
                for(int j=0;j<distances.Length;j++){
                    var playerPos=new Vector3(0,cc.transform.position.y,distances[j]);
                    bool touch=Physics.ComputePenetration(box,center,rotation,cap,playerPos,Quaternion.identity,out _,out _);
                    bool horizontalTouch=Physics.ComputePenetration(box,center,rotation,tall,playerPos,Quaternion.identity,out _,out _);
                    if(touch){full[j].Add(frame);if(enabled)active[j].Add(frame);else if(time<p.hitStartSeconds)pre[j].Add(frame);}
                    if(enabled&&horizontalTouch)horizontal[j].Add(frame);
                    if(enabled&&Physics.ComputePenetration(box,center-Vector3.up*1.5f,rotation,cap,playerPos,Quaternion.identity,out _,out _))lowered[j].Add(frame);
                }
                if(i%10==0&&frame>=20&&frame<=46)for(float px=-3;px<=3;px+=.5f)for(float pz=1;pz<=5;pz+=.5f){
                    if(px*px+pz*pz>25)continue;
                    if(Physics.ComputePenetration(box,center,rotation,cap,new Vector3(px,cc.transform.position.y,pz),Quaternion.identity,out _,out _)){
                        if(enabled)gridActive.Add(frame);else if(time<p.hitStartSeconds)gridBefore.Add(frame);
                    }
                }
            }
            var report=new StringBuilder($"Runtime clip {p.clip.name}; FPS={p.clip.frameRate}; window={p.hitStartSeconds*p.clip.frameRate}..{p.hitEndSeconds*p.clip.frameRate}; bossY={origin.y}; playerY={cc.transform.position.y+cc.center.y-cc.height/2}..{cc.transform.position.y+cc.center.y+cc.height/2}; active box min/maxY={activeMin:F2}..{activeMax:F2}\n");
            for(int j=0;j<distances.Length;j++)report.AppendLine($"distance {distances[j]}: any geometric contact={Span(full[j])}; BEFORE start={Span(pre[j])}; active 3D={Span(active[j])}; active ignoring height={Span(horizontal[j])}; active box lowered 1.5 (DIAGNOSTIC ONLY)={Span(lowered[j])}");
            report.AppendLine("Front grid within radius 5: before start="+Span(gridBefore)+" active="+Span(gridActive));
            report.Append(rows);
            string result=report.ToString();System.IO.File.WriteAllText("Assets/3DBattle/Authoring~/Boss10ContactReport.txt",result);return result;
        }finally{UnityEngine.Object.DestroyImmediate(copy);UnityEngine.Object.DestroyImmediate(probes);}
    }
}
