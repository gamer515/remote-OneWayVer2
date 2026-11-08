using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using JourneyMapKit;

/// <summary>Retained scene authoring and regression checks for the fitted dispenser and book board.</summary>
public static class LearningDispenserRevision
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    const string Output="output/LearningDispenserRevision";
    static void Check(bool pass,string message) {if(!pass)throw new Exception(message);}
    public static string Apply()
    {
        Check(!Application.isPlaying,"Edit mode required");
        var model=GameObject.Find("LearningDispenser");Check(model!=null,"Dispenser missing");
        var view=model.GetComponent<LearningDispenserView>();var so=new SerializedObject(view);
        var camera=(Camera)so.FindProperty("viewCamera").objectReferenceValue;
        var board=UnityEngine.Object.FindFirstObjectByType<JourneyBoardReferences>();
        Undo.RecordObject(model.transform,"Fit learning dispenser");
        float sx=2*camera.orthographicSize*camera.aspect*.25f/5.12f;
        float sy=2*camera.orthographicSize*.47f/5.57f;
        model.transform.localScale=new Vector3(sx,sy,sx);
        model.transform.position=camera.ViewportToWorldPoint(new Vector3(.675f,.5f+.085f*sy/(2*camera.orthographicSize),12));
        string[] names={"Blue","Red","Yellow","Teal"};
        for(int i=0;i<4;i++)
        {
            var column=model.transform.Find("03_Column_"+names[i]);Undo.RecordObject(column,"Align dispenser to coin tube");
            var p=column.localPosition;p.x=model.transform.InverseTransformPoint(board.supplyButtons[i].bounds.center).x;column.localPosition=p;
            PrefabUtility.RecordPrefabInstancePropertyModifications(column);
        }
        var station=(MineLearningStation)so.FindProperty("station").objectReferenceValue;
        var ss=new SerializedObject(station);var status=(TMPro.TMP_Text)ss.FindProperty("status").objectReferenceValue;
        Undo.RecordObject(status.gameObject,"Hide learning status");status.gameObject.SetActive(false);
        var timer=GameObject.Find("DispenserTimer");if(timer)Undo.DestroyObjectImmediate(timer);
        var rects=so.FindProperty("overlays");var anchors=so.FindProperty("overlayAnchors");
        var pairs=new List<Tuple<UnityEngine.Object,UnityEngine.Object>>();
        for(int i=0;i<Mathf.Min(rects.arraySize,anchors.arraySize);i++)
        {
            var r=rects.GetArrayElementAtIndex(i).objectReferenceValue;
            if(r!=null && r!=status.transform) pairs.Add(Tuple.Create(r,anchors.GetArrayElementAtIndex(i).objectReferenceValue));
        }
        rects.arraySize=anchors.arraySize=pairs.Count;
        for(int i=0;i<pairs.Count;i++){rects.GetArrayElementAtIndex(i).objectReferenceValue=pairs[i].Item1;anchors.GetArrayElementAtIndex(i).objectReferenceValue=pairs[i].Item2;}
        var paper=UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Single(r=>r.name=="Paper");
        var surface=GameObject.Find("LearningBookBoardSurface");
        if(!surface){surface=new GameObject("LearningBookBoardSurface");Undo.RegisterCreatedObjectUndo(surface,"Add book drop board");}
        surface.transform.SetParent(paper.transform,true);surface.transform.rotation=Quaternion.identity;
        var parentScale=paper.transform.lossyScale;
        surface.transform.localScale=new Vector3(1/parentScale.x,1/parentScale.y,1/parentScale.z);
        surface.transform.position=paper.bounds.center+Vector3.up*(paper.bounds.extents.y+.01f);
        var collider=surface.GetComponent<BoxCollider>();if(collider==null)collider=surface.AddComponent<BoxCollider>();
        collider.size=new Vector3(paper.bounds.size.x-.12f,.025f,paper.bounds.size.z-.12f);collider.isTrigger=true;
        so.FindProperty("bookBoard").objectReferenceValue=collider;so.ApplyModifiedProperties();
        PrefabUtility.RecordPrefabInstancePropertyModifications(model.transform);
        EditorSceneManager.MarkSceneDirty(model.scene);EditorSceneManager.SaveScene(model.scene);
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output+"/layout.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{target=new{left=.55,right=.8,bottom=.5,top=.97},columns=Enumerable.Range(0,4).Select(i=>new {color=names[i],tubeX=board.supplyButtons[i].bounds.center.x,columnX=model.transform.Find("03_Column_"+names[i]).position.x}),boardWidth=collider.bounds.size.x,boardDepth=collider.bounds.size.z},Newtonsoft.Json.Formatting.Indented));
        return "Fitted cabinet, aligned real coin centers, removed status/timer and connected full white-paper drop board.";
    }

    public static string CheckInteraction()
    {
        Check(Application.isPlaying,"Play mode required");
        var view=UnityEngine.Object.FindFirstObjectByType<LearningDispenserView>();var vs=new SerializedObject(view);
        var original=(MineLearningStation)vs.FindProperty("station").objectReferenceValue;
        var camera=(Camera)vs.FindProperty("viewCamera").objectReferenceValue;
        var board=(BoxCollider)vs.FindProperty("bookBoard").objectReferenceValue;
        var test=new GameObject("LearningBoardRegression");
        var station=test.AddComponent<MineLearningStation>();var ss=new SerializedObject(station);var os=new SerializedObject(original);
        ss.FindProperty("abilityButtons").arraySize=0;ss.FindProperty("balances").arraySize=0;
        ss.FindProperty("bookTemplate").objectReferenceValue=os.FindProperty("bookTemplate").objectReferenceValue;
        ss.FindProperty("status").objectReferenceValue=os.FindProperty("status").objectReferenceValue;
        ss.FindProperty("showFlatBooks").boolValue=false;ss.ApplyModifiedPropertiesWithoutUndo();
        int[] wallet={20,20,20,20};int saves=0;var progress=new MineLearningProgress();
        station.Initialize(progress,1,()=>wallet,(a,n)=>{wallet[a]-=n;return true;},(a,n)=>wallet[a]+=n,()=>saves++);
        vs.FindProperty("station").objectReferenceValue=station;vs.ApplyModifiedPropertiesWithoutUndo();
        var sync=typeof(LearningDispenserView).GetMethod("LateUpdate",Hidden);
        var results=new List<string>();void Assert(bool pass,string message){Check(pass,message);results.Add(message);}
        try
        {
            var paper=UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Single(r=>r.name=="Paper");
            Assert(board.bounds.size.x<=paper.bounds.size.x && board.bounds.size.z<=paper.bounds.size.z,"Drop collider fits actual white paper mesh");
            Assert(station.Request(1),"Book request accepted once");station.Tick(1,false);sync.Invoke(view,null);
            var dictionary=(Dictionary<MineLearningJob,Transform>)typeof(LearningDispenserView).GetField("books",Hidden).GetValue(view);
            var job=progress.active[0];var book=dictionary[job];Vector3 origin=book.position;
            Assert(view.BeginBookDrag(camera.WorldToScreenPoint(book.position)),"Dispensed book picked using screen ray");
            float remaining=job.remaining;station.Tick(10,false);Assert(job.remaining==remaining,"Held book cannot expire during drag");
            Vector3 target=board.bounds.center+Vector3.forward*.6f;
            view.MoveBookDrag(camera.WorldToScreenPoint(target));
            Assert(view.EndBookDrag(camera.WorldToScreenPoint(target)),"Book drops onto full white board");
            sync.Invoke(view,null);Assert(job.onBoard && Mathf.Abs(book.position.z-target.z)<.08f,"Board placement survives visual refresh");
            var saved=JsonUtility.FromJson<MineLearningProgress>(JsonUtility.ToJson(progress));
            Assert(saved.active[0].onBoard && Mathf.Abs(saved.active[0].boardV-job.boardV)<.001f,"Placement serializes with learning save");
            Vector3 placed=book.position;
            Assert(view.BeginBookDrag(camera.WorldToScreenPoint(book.position)),"Placed book can be picked again");
            view.MoveBookDrag(new Vector2(15,15));Assert(!view.EndBookDrag(new Vector2(15,15)),"Outside-board drop rejected");
            Assert(Vector3.Distance(book.position,placed)<.001f,"Rejected drop returns to previous place");
            Assert(view.BeginBookDrag(camera.WorldToScreenPoint(book.position)),"Book picked before game pause");
            station.Tick(0,true);typeof(LearningDispenserView).GetMethod("Update",Hidden).Invoke(view,null);
            Assert(!view.IsDraggingBook && !job.held && Vector3.Distance(book.position,placed)<.001f,"Game pause cancels drag and releases held state");station.Tick(0,false);
            Assert(view.BeginBookDrag(camera.WorldToScreenPoint(book.position)),"Book picked for boundary check");
            var edge=board.bounds.max-new Vector3(.01f,0,.01f);
            Assert(view.EndBookDrag(camera.WorldToScreenPoint(edge)),"Board-edge drop accepted and clamped");
            var visual=book.GetComponentsInChildren<Renderer>();var bounds=visual[0].bounds;foreach(var r in visual)bounds.Encapsulate(r.bounds);
            Assert(bounds.min.x>=board.bounds.min.x && bounds.max.x<=board.bounds.max.x && bounds.min.z>=board.bounds.min.z && bounds.max.z<=board.bounds.max.z,"Whole book stays inside paper boundaries");
            Assert(wallet[1]==19 && wallet.Sum()==79,"Moving book does not spend or reward coins");
            station.Tick(5.1f,false);sync.Invoke(view,null);
            Assert(progress.active.Count==0 && view.ActiveVisualCount==0 && wallet[1]==21,"Normal completion removes book and grants reward once");
            File.WriteAllText(Output+"/interaction-checks.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{passed=true,checks=results,saves,frame=Time.frameCount},Newtonsoft.Json.Formatting.Indented));
            return "PASS "+results.Count+" book drag/drop and economy checks";
        }
        finally
        {
            view.CancelBookDrag();vs.Update();vs.FindProperty("station").objectReferenceValue=original;vs.ApplyModifiedPropertiesWithoutUndo();sync.Invoke(view,null);UnityEngine.Object.Destroy(test);
        }
    }
}
