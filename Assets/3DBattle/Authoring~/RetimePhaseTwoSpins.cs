using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using OneWay.Battle3D;

public static class RetimePhaseTwoSpins
{
    const string ClipPath="Assets/3DBattle/Boss1/Skill_02_Phase2_TenSpins.anim";
    const string BackupPath="Assets/3DBattle/Boss1/Skill_02_Phase2_TenSpins_BeforeSpeedRamp.anim";
    public static object Run()
    {
        if(Application.isPlaying)throw new Exception("Stop play mode first");
        if(AssetDatabase.LoadAssetAtPath<AnimationClip>(BackupPath)==null)
            if(!AssetDatabase.CopyAsset(ClipPath,BackupPath))throw new Exception("Backup failed");
        var source=AssetDatabase.LoadAssetAtPath<AnimationClip>(BackupPath);
        var destination=AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        var attack=AssetDatabase.LoadAssetAtPath<BossWeaponAttackPattern>("Assets/3DBattle/Settings/B1_Phase2_Attack8.asset");
        var bindings=AnimationUtility.GetCurveBindings(source);
        var curves=bindings.Select(b=>AnimationUtility.GetEditorCurve(source,b)).ToArray();
        var rotation=new[]{"x","y","z","w"}.Select(s=>Array.FindIndex(bindings,b=>b.path=="target_character/Hips"&&b.propertyName=="m_LocalRotation."+s)).ToArray();
        Func<float,Quaternion> sample=t=>new Quaternion(curves[rotation[0]].Evaluate(t),curves[rotation[1]].Evaluate(t),curves[rotation[2]].Evaluate(t),curves[rotation[3]].Evaluate(t));
        int n=Mathf.CeilToInt(source.length*1200);
        float[] angles=new float[n+1];float previous=sample(0).eulerAngles.y;
        for(int i=1;i<=n;i++){float yaw=sample(source.length*i/n).eulerAngles.y;angles[i]=angles[i-1]+Mathf.DeltaAngle(previous,yaw);previous=yaw;}
        Func<float,float> oldTime=angle=>
        {
            if(angle<=0)return 0;if(angle>=angles[n])return source.length;
            int lo=0,hi=n;while(hi-lo>1){int mid=(lo+hi)/2;if(angles[mid]<angle)lo=mid;else hi=mid;}
            return source.length*(lo+Mathf.InverseLerp(angles[lo],angles[hi],angle))/n;
        };
        float oldEighthDuration=oldTime(2880)-oldTime(2520);
        float peak=360/oldEighthDuration;
        // Reach peak speed at the start of turn 3, hold through turn 8, stop at turn 10.
        float startSpeed=peak*.3f;
        float accelerationTime=1440/(startSpeed+peak);
        float constantTime=2160/peak;
        float brakingTime=1440/peak;
        float brakeStart=accelerationTime+constantTime;
        float duration=brakeStart+brakingTime;
        Func<float,float> angleAt=t=>
        {
            if(t<accelerationTime)return startSpeed*t+(peak-startSpeed)*t*t/(2*accelerationTime);
            if(t<brakeStart)return 720+peak*(t-accelerationTime);
            float u=Mathf.Min(t-brakeStart,brakingTime);
            return 2880+peak*u-peak*u*u/(2*brakingTime);
        };
        int count=Mathf.CeilToInt(duration*120);
        var result=bindings.Select(b=>new AnimationCurve()).ToArray();
        for(int i=0;i<=count;i++)
        {
            float t=duration*i/count;float old=oldTime(angleAt(t));
            for(int c=0;c<curves.Length;c++)result[c].AddKey(new Keyframe(t,curves[c].Evaluate(old)));
        }
        destination.ClearCurves();
        for(int c=0;c<bindings.Length;c++)
        {
            for(int k=0;k<result[c].length;k++)
            {
                AnimationUtility.SetKeyLeftTangentMode(result[c],k,AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(result[c],k,AnimationUtility.TangentMode.Linear);
            }
            AnimationUtility.SetEditorCurve(destination,bindings[c],result[c]);
        }
        destination.EnsureQuaternionContinuity();
        var boundaries=new float[11];
        for(int i=1;i<10;i++)
        {
            float lo=0,hi=duration;
            for(int j=0;j<40;j++){float mid=(lo+hi)*.5f;if(angleAt(mid)<i*360)lo=mid;else hi=mid;}
            boundaries[i]=(lo+hi)*.5f;
        }
        boundaries[10]=duration;
        attack.hitWindows=Enumerable.Range(0,8).Select(i=>new BossWeaponAttackPattern.HitWindow(boundaries[i],boundaries[i+1]-.00001f){downwardExtension=4.5f}).ToArray();
        attack.hitStartSeconds=0;attack.hitEndSeconds=boundaries[8]-.00001f;
        attack.independentStrikes=true;
        attack.travelX=AnimationCurve.Constant(0,duration,0);attack.travelZ=AnimationCurve.Constant(0,duration,0);
        EditorUtility.SetDirty(destination);EditorUtility.SetDirty(attack);AssetDatabase.SaveAssets();
        if(!attack.TimingIsValid())throw new Exception("Invalid hit timing");
        var outputRot=rotation.Select(i=>AnimationUtility.GetEditorCurve(destination,bindings[i])).ToArray();
        float lastYaw=0,total=0,minSpeed=float.PositiveInfinity,maxSpeed=0;
        const float dt=1f/1200;
        for(int i=0;i<=Mathf.CeilToInt(duration/dt);i++)
        {
            float t=Mathf.Min(i*dt,duration);
            float yaw=new Quaternion(outputRot[0].Evaluate(t),outputRot[1].Evaluate(t),outputRot[2].Evaluate(t),outputRot[3].Evaluate(t)).eulerAngles.y;
            if(i>0){float d=Mathf.DeltaAngle(lastYaw,yaw);total+=d;if(t>accelerationTime+.02f&&t<brakeStart-.02f){minSpeed=Mathf.Min(minSpeed,d/dt);maxSpeed=Mathf.Max(maxSpeed,d/dt);}}
            lastYaw=yaw;
        }
        if(Mathf.Abs(total-3600)>.1f || attack.GetStrikeIndex((boundaries[8]+boundaries[9])*.5f)!=-1 || attack.GetStrikeIndex((boundaries[9]+duration)*.5f)!=-1)
            throw new Exception("Rotation or inactive final turns validation failed");
        return new{oldEighthDuration,peakDegreesPerSecond=peak,duration,boundaries,totalDegrees=total,measuredConstantMin=minSpeed,measuredConstantMax=maxSpeed,activeTurns=attack.StrikeCount,lastTwoTurnsInactive=true};
    }
}
