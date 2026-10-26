using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// Pure position cases plus preview-scene Player/Camera integration. Never starts real gameplay.
public static class PlayerSpawnRecoveryCheck
{
    static int checks;
    static void Check(bool value,string reason) { if(!value) throw new Exception(reason); checks++; }
    static Vector3 Resolve(Vector3? saved,Vector3 origin,float end,Vector3 fallback,bool repaired)
    {
        var result=DecisionPlayerController.ResolveStartPosition(saved,origin,end,fallback,out bool actual);
        Check(actual==repaired,"incorrect recovery flag for "+saved);
        return result;
    }
    public static string Verify()
    {
        checks=0;
        var origin=new Vector3(-200,0,0); var entry=new Vector3(-200,0,10);
        Check(Resolve(null,origin,50,entry,false)==entry,"new game entry");
        Check(Resolve(Vector3.zero,origin,50,entry,true)==entry,"reported zero-position bug");
        foreach(float z in new[]{0f,10f,22.5f,50f})
        {
            var saved=new Vector3(-200,0,z);
            Check(Resolve(saved,origin,50,entry,false)==saved,"normal continuation moved");
        }
        foreach(var saved in new[]{new Vector3(0,0,28),new Vector3(-200,80,28),
            new Vector3(float.NaN,0,28),new Vector3(-200,float.PositiveInfinity,28)})
            Check(Resolve(saved,origin,50,entry,true)==new Vector3(-200,0,28),"valid travelled Z lost");
        foreach(float z in new[]{-1f,51f,float.NaN,float.PositiveInfinity,float.NegativeInfinity})
            Check(Resolve(new Vector3(-200,0,z),origin,50,entry,true)==entry,"invalid Z not recovered");
        var drift=new Vector3(-199.9f,0.1f,30);
        Check(Resolve(drift,origin,50,entry,false)==drift,"minor valid lane offset lost");
        var shifted=new Vector3(24,5,100); var shiftedEntry=new Vector3(24,5,110);
        Check(Resolve(null,shifted,150,shiftedEntry,false)==shiftedEntry,"terrain offset hard-coded");
        Check(Resolve(Vector3.zero,shifted,150,shiftedEntry,true)==shiftedEntry,"later terrain recovery");
        Check(Resolve(Vector3.zero,Vector3.zero,50,new Vector3(0,0,10),false)==Vector3.zero,"valid world zero rejected");
        Check(Resolve(new Vector3(-200,0,50),origin,100,new Vector3(-200,0,60),false)==new Vector3(-200,0,50),
            "chapter-boundary position lost");

        string manager=File.ReadAllText(Path.Combine(Application.dataPath,"Decision_YYS/Scripts/GameSystem/DecisionManager.cs"));
        Check(manager.Contains("ResolveStartPosition(") && manager.Contains("saveService.SaveProgress(session, startPosition, playerController.CurrentRotation)"),
            "startup or repaired checkpoint not wired");
        Check(manager.IndexOf("playerController.Initialize(startPosition",StringComparison.Ordinal)<
            manager.IndexOf("envController.BindPlayer(startData.Player)",StringComparison.Ordinal),"camera/streaming bound before recovery");

        string save=Path.Combine(Application.persistentDataPath,"Saves","Runs","Run_01","Progress.json");
        string before=File.Exists(save)?File.ReadAllText(save):null;
        if(before!=null)
        {
            var progress=ProgressDocument.Read(save);
            var p=progress.currentPosition;
            if(p?.Length==3)
            {
                Vector3 recovered=DecisionPlayerController.ResolveStartPosition(new Vector3(p[0],p[1],p[2]),origin,50,entry,out bool repaired);
                Check(recovered.x==origin.x && Mathf.Abs(recovered.y-origin.y)<=0.5f,"actual save preview remains outside lane");
            }
        }
        var preview=EditorSceneManager.NewPreviewScene();
        try
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Decision_YYS/Prafabs/People/KnightCharacter.prefab");
            var knight=(GameObject)PrefabUtility.InstantiatePrefab(prefab,preview);
            var player=knight.GetComponent<Player>();
            var control=new DecisionPlayerController(player);
            Quaternion rotation=Quaternion.Euler(0,90,0);
            control.Initialize(Resolve(Vector3.zero,origin,50,entry,true),rotation);
            Check(knight.transform.position==entry && control.TargetPosition==entry,"Player did not use repaired position");
            Check(Quaternion.Angle(knight.transform.rotation,rotation)<0.01f,"saved facing lost");
            control.MoveToZ(28);
            Check(control.TargetPosition==new Vector3(-200,0,28),"walking target returns outside lane");

            var cameraObject=new GameObject("SpawnRecoveryCamera");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject,preview);
            var camera=cameraObject.AddComponent<RoadViewCameraController>();
            camera.Initialize(knight.transform,false);
            Check(camera.transform.position==new Vector3(-200,18,10.5f),"resume camera not snapped to repaired player");
            Check(knight.GetComponentsInChildren<Camera>(true).All(c=>Mathf.Abs(c.transform.position.x+200)<1f),
                "first-person camera left behind at world zero");
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
        Check(before==null || File.ReadAllText(save)==before,"real save modified by test");
        return checks+" checks passed: zero/invalid coordinates, valid resumes, preserved Z/facing, shifted terrain and chapter boundary, startup checkpoint wiring, real prefab and camera in isolated preview. Actual saves/scenes unchanged.";
    }
}
