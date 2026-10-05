using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class JourneyV3Layout
{
    const string ScenePath="Assets/Scenes/DecisionScene.unity";
    static Scene Scene()
    {
        var s=SceneManager.GetSceneByPath(ScenePath);
        if(!s.IsValid()||!s.isLoaded) s=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
        return s;
    }
    static Transform Find(string path)
    {
        var parts=path.Split('/');
        var root=Scene().GetRootGameObjects().Single(o=>o.name==parts[0]).transform;
        return parts.Length==1?root:root.Find(string.Join("/",parts.Skip(1)))??throw new Exception(path);
    }
    static string Path(Transform t) => t.parent==null?t.name:Path(t.parent)+"/"+t.name;
    static float[] ProjectBounds(Transform root,Camera camera)
    {
        var pixels=root.GetComponentsInChildren<MeshFilter>(true)
            .Where(m=>m.sharedMesh!=null&&m.GetComponent<Renderer>()!=null&&m.GetComponent<Renderer>().enabled&&m.gameObject.activeInHierarchy)
            .SelectMany(m=>m.sharedMesh.vertices.Select(v=>camera.WorldToViewportPoint(m.transform.TransformPoint(v)))).ToArray();
        return new[]{pixels.Min(v=>v.x)*1920,pixels.Min(v=>v.y)*1080,pixels.Max(v=>v.x)*1920,pixels.Max(v=>v.y)*1080};
    }
    static float[] RectBounds(RectTransform r,Camera camera)
    {
        var corners=new Vector3[4]; r.GetWorldCorners(corners);
        var p=corners.Select(c=>camera.WorldToViewportPoint(c)).ToArray();
        return new[]{p.Min(v=>v.x)*1920,p.Min(v=>v.y)*1080,p.Max(v=>v.x)*1920,p.Max(v=>v.y)*1080};
    }
    public static object Inspect()
    {
        var s=Scene();
        var camera=Find("_World/Main Camera").GetComponent<Camera>();
        var board=Find("_Gameplay3D/3D_UI/JourneyInterfaceV3_Runtime");
        return new {
            scenes=Enumerable.Range(0,SceneManager.sceneCount).Select(i=>new{SceneManager.GetSceneAt(i).path,SceneManager.GetSceneAt(i).isDirty}).ToArray(),
            board=ProjectBounds(board,camera),
            parts=new[]{"GridBoard","Chassis","CoinSupplyRack","BackpackMount","CollectionTray"}.Select(n=>new{name=n,bounds=ProjectBounds(board.Find(n),camera)}).ToArray(),
            ui=Find("_UI/Decision_Canvas/Decision_UI_Root").GetComponentsInChildren<RectTransform>(true)
                .Where(r=>r.GetComponent<UnityEngine.UI.RawImage>()!=null||r.name.Contains("Frame")||r.name.Contains("Face")||
                    r.name=="Walking_View_Back"||r.name=="Encounter_Display"||r.name.Contains("Text"))
                .Select(r=>new{path=Path(r),rect=RectBounds(r,camera),size=r.rect.size.ToString(),
                    anchors=r.anchorMin.ToString()+r.anchorMax.ToString(),pivot=r.pivot.ToString(),pos=r.anchoredPosition.ToString(),scale=r.localScale.ToString()}).ToArray()
        };
    }
    static void Record(Transform t)
    {
        Undo.RecordObject(t,"Fit Journey V3 interface");
    }
    static float[] FaceBounds(Transform root,Camera camera)
    {
        var pixels=new List<Vector3>();
        foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if(!renderer.enabled||!renderer.gameObject.activeInHierarchy)continue;
            Mesh mesh;
            if(renderer is SkinnedMeshRenderer skinned)
            {
                var bounds=skinned.bounds;
                for(int x=0;x<2;x++)for(int y=0;y<2;y++)for(int z=0;z<2;z++)
                    pixels.Add(camera.WorldToViewportPoint(new Vector3(x==0?bounds.min.x:bounds.max.x,y==0?bounds.min.y:bounds.max.y,z==0?bounds.min.z:bounds.max.z)));
                continue;
            }
            else mesh=renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if(mesh!=null) pixels.AddRange(mesh.vertices.Select(v=>camera.WorldToViewportPoint(renderer.transform.TransformPoint(v))));
        }
        if(pixels.Count==0)throw new Exception("Face has no visible geometry");
        return new[]{pixels.Min(v=>v.x)*1920,pixels.Min(v=>v.y)*1080,pixels.Max(v=>v.x)*1920,pixels.Max(v=>v.y)*1080};
    }
    public static object FitFace()
    {
        if(EditorApplication.isPlaying)throw new Exception("Stop Play first");
        var s=Scene(); var camera=Find("_World/Main Camera").GetComponent<Camera>();
        var face=(RectTransform)Find("_UI/Decision_Canvas/Decision_UI_Root/Face_Display");
        var roots=face.Cast<Transform>().Where(c=>!(c is RectTransform)&&c.GetComponentsInChildren<Renderer>(true).Length>0).ToArray();
        if(roots.Length!=1)throw new Exception("Expected one face rig root, got "+roots.Length);
        var root=roots[0]; var rect=RectBounds(face,camera); var before=FaceBounds(root,camera);
        string backup="Temp/JourneyV3Migration/FaceFitBefore-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".unity";
        if(!EditorSceneManager.SaveScene(s,backup,true))throw new Exception("Backup failed");
        Undo.IncrementCurrentGroup(); int group=Undo.GetCurrentGroup();
        try
        {
            Record(root);
            root.localScale*=Mathf.Min((rect[2]-rect[0]-40)/(before[2]-before[0]),(rect[3]-rect[1]-40)/(before[3]-before[1]));
            var b=FaceBounds(root,camera); float ppu=1080/(camera.orthographicSize*2);
            root.position+=camera.transform.right*((rect[0]+rect[2]-b[0]-b[2])*.5f/ppu)+camera.transform.up*((rect[1]+rect[3]-b[1]-b[3])*.5f/ppu);
            Persist(root); b=FaceBounds(root,camera);
            if(b[0]<rect[0]+19||b[1]<rect[1]+19||b[2]>rect[2]-19||b[3]>rect[3]-19)throw new Exception("Face fit failed");
            EditorSceneManager.MarkSceneDirty(s); if(!EditorSceneManager.SaveScene(s))throw new Exception("Save failed");
            Undo.CollapseUndoOperations(group);
            return new{backup,before,after=b,rect,root=Path(root),managerActive=Find("_Systems/Decision_Manager").gameObject.activeSelf};
        }
        catch{Undo.RevertAllDownToGroup(group);throw;}
    }
    public static object RestoreFaceThenFit()
    {
        var preview=EditorSceneManager.OpenPreviewScene("Temp/JourneyV3Migration/FaceFitBefore-20261005-104106.unity");
        try
        {
            var old=preview.GetRootGameObjects().Single(g=>g.name=="_UI").transform.Find("Decision_Canvas/Decision_UI_Root/Face_Display/AangFace");
            var target=Find("_UI/Decision_Canvas/Decision_UI_Root/Face_Display/AangFace");
            Record(target); target.localPosition=old.localPosition; target.localRotation=old.localRotation; target.localScale=old.localScale; Persist(target);
        }
        finally{EditorSceneManager.ClosePreviewScene(preview);}
        return FitFace();
    }
    static void Persist(Transform t)
    {
        PrefabUtility.RecordPrefabInstancePropertyModifications(t);
        EditorUtility.SetDirty(t);
    }
    static string Pose(Transform t)=>t.position.ToString("F6")+t.rotation.ToString("F6")+t.lossyScale.ToString("F6");
    public static object Apply()
    {
        if(EditorApplication.isPlaying)throw new Exception("Stop Play first");
        var scene=Scene();
        var camera=Find("_World/Main Camera").GetComponent<Camera>();
        var protectedPoses=scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<Transform>(true))
            .Where(t=>t.name.Contains("Pose")||t.name.Contains("SpawnPoint")||t.name=="KnightCharacter_Copy"||t.name=="HandRoot"||t.name=="NpcHandRoot")
            .ToDictionary(t=>t,Pose);
        string backup="Temp/JourneyV3Migration/LayoutBefore-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".unity";
        System.IO.Directory.CreateDirectory("Temp/JourneyV3Migration");
        if(!EditorSceneManager.SaveScene(scene,backup,true))throw new Exception("Backup failed");
        Undo.IncrementCurrentGroup(); int group=Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Fit Journey V3 board and frames");
        try
        {
            var board=Find("_Gameplay3D/3D_UI/JourneyInterfaceV3_Runtime");
            var map=(RectTransform)Find("_UI/Decision_Canvas/Decision_UI_Root/JourneyInterfaceV3_Frames/Frame_Map_370x556");
            float topLimit=RectBounds(map,camera)[1]-24;
            var bounds=ProjectBounds(board,camera);
            float factor=Mathf.Min((1920-48)/(bounds[2]-bounds[0]),(topLimit-24)/(bounds[3]-bounds[1]));
            Record(board); board.localScale*=factor;
            bounds=ProjectBounds(board,camera);
            // Keep the board's world surface height; translate in X/Z, not in Y (knight/hand poses stay fixed).
            float pixelsPerUnit=1080/(camera.orthographicSize*2);
            board.position+=Vector3.right*((960-(bounds[0]+bounds[2])*.5f)/pixelsPerUnit);
            board.position+=Vector3.forward*((24-bounds[1])/(pixelsPerUnit*camera.transform.up.z));
            Persist(board);
            var frames=Find("_UI/Decision_Canvas/Decision_UI_Root/JourneyInterfaceV3_Frames");
            var story=(RectTransform)frames.Find("Frame_Story_1065x778");
            Record(story); story.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,1016);
            story.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,700); Persist(story);
            var walking=(RectTransform)Find("_UI/Decision_Canvas/Decision_UI_Root/Encounter_Display/Event_View/Walking_View_Back/Walking_View");
            Record(walking);
            walking.anchorMin=walking.anchorMax=new Vector2(0,1);
            walking.pivot=new Vector2(0,1);
            walking.anchoredPosition=new Vector2(18.22f,-16);
            walking.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,1016);
            walking.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,700); Persist(walking);
            var faceFrame=(RectTransform)frames.Find("Frame_Face_384x520");
            Record(faceFrame); faceFrame.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,map.rect.width);
            faceFrame.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,map.rect.height); Persist(faceFrame);
            var face=(RectTransform)Find("_UI/Decision_Canvas/Decision_UI_Root/Face_Display");
            var oldFaceSize=face.rect.size;
            Record(face); face.anchoredPosition=new Vector2(-(1920-(1536+map.rect.width)),-16);
            face.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,map.rect.width);
            face.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,map.rect.height); Persist(face);
            // Recenter the existing 3D face, keeping its scale, rig and expression controller.
            foreach(Transform child in face)
            {
                if(child is RectTransform)continue;
                Record(child);
                child.localPosition+=new Vector3((oldFaceSize.x-map.rect.width)*.5f,-(map.rect.height-oldFaceSize.y)*.5f,0);
                Persist(child);
            }
            foreach(var p in protectedPoses)if(Pose(p.Key)!=p.Value)throw new Exception("Protected duel pose changed: "+p.Key.name);
            bounds=ProjectBounds(board,camera);
            if(bounds[0]<23||bounds[1]<23||bounds[2]>1897||bounds[3]>topLimit+1)throw new Exception("Board does not fit");
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Undo.CollapseUndoOperations(group);
            return new{backup,scale=board.localScale.ToString("F4"),board=bounds,topLimit,protectedTransforms=protectedPoses.Count};
        }
        catch {Undo.RevertAllDownToGroup(group);throw;}
    }
    public static object ClearStoryCorner()
    {
        var story=(RectTransform)Find("_UI/Decision_Canvas/Decision_UI_Root/JourneyInterfaceV3_Frames/Frame_Story_1065x778");
        var walking=(RectTransform)Find("_UI/Decision_Canvas/Decision_UI_Root/Encounter_Display/Event_View/Walking_View_Back/Walking_View");
        Record(story); story.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,1016); Persist(story);
        Record(walking); walking.anchorMin=walking.anchorMax=new Vector2(0,1); walking.pivot=new Vector2(0,1);
        walking.anchoredPosition=new Vector2(18.22f,-16); walking.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,1016); Persist(walking);
        EditorSceneManager.MarkSceneDirty(Scene()); EditorSceneManager.SaveScene(Scene());
        return Inspect();
    }
    public static object FillWithoutMargins()
    {
        if(EditorApplication.isPlaying)throw new Exception("Stop Play first");
        var s=Scene(); var camera=Find("_World/Main Camera").GetComponent<Camera>();
        var board=Find("_Gameplay3D/3D_UI/JourneyInterfaceV3_Runtime");
        string backup="Temp/JourneyV3Migration/LayoutNoMarginBefore-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".unity";
        if(!EditorSceneManager.SaveScene(s,backup,true))throw new Exception("Backup failed");
        Undo.IncrementCurrentGroup(); int group=Undo.GetCurrentGroup();
        try
        {
            var b=ProjectBounds(board,camera); Record(board);
            board.localScale*=1920/(b[2]-b[0]);
            b=ProjectBounds(board,camera);
            float ppu=1080/(camera.orthographicSize*2);
            board.position+=Vector3.right*((960-(b[0]+b[2])*.5f)/ppu);
            board.position+=Vector3.forward*(-b[1]/(ppu*camera.transform.up.z)); Persist(board);
            var frames=Find("_UI/Decision_Canvas/Decision_UI_Root/JourneyInterfaceV3_Frames");
            var story=(RectTransform)frames.Find("Frame_Story_1065x778");
            Record(story); story.sizeDelta=new Vector2(1038,650); Persist(story);
            var walking=(RectTransform)Find("_UI/Decision_Canvas/Decision_UI_Root/Encounter_Display/Event_View/Walking_View_Back/Walking_View");
            Record(walking); walking.sizeDelta=new Vector2(1038,650); Persist(walking);
            var testBar=(RectTransform)Find("_UI/Decision_Canvas/Decision_UI_Root/Encounter_Display/Event_View/Walking_View_Back/MiniGame_TestBar");
            Record(testBar); testBar.anchoredPosition=new Vector2(-106,-18); Persist(testBar);
            var map=(RectTransform)frames.Find("Frame_Map_370x556");
            Record(map); map.sizeDelta=new Vector2(369.52f,450); Persist(map);
            var mapContent=(RectTransform)Find("_UI/Decision_Canvas/Decision_UI_Root/RawImage/RawImage_Road_View_And_Map");
            Record(mapContent); mapContent.sizeDelta=new Vector2(369.52f,450);
            var mapRect=RectBounds(map,camera); var contentRect=RectBounds(mapContent,camera);
            mapContent.position+=camera.transform.up*((mapRect[1]+mapRect[3]-contentRect[1]-contentRect[3])*.5f/ppu); Persist(mapContent);
            var faceFrame=(RectTransform)frames.Find("Frame_Face_384x520");
            Record(faceFrame); faceFrame.sizeDelta=map.sizeDelta; Persist(faceFrame);
            var face=(RectTransform)Find("_UI/Decision_Canvas/Decision_UI_Root/Face_Display");
            Record(face); face.sizeDelta=map.sizeDelta; Persist(face);
            var faceRect=RectBounds(face,camera);
            foreach(Transform child in face)
            {
                if(child is RectTransform||child.GetComponentsInChildren<MeshFilter>(true).Length==0)continue;
                Record(child);
                var projected=FaceBounds(child,camera);
                float fit=Mathf.Min((map.rect.width-40)/(projected[2]-projected[0]),(map.rect.height-40)/(projected[3]-projected[1]));
                child.localScale*=fit;
                projected=FaceBounds(child,camera);
                child.position+=camera.transform.right*((faceRect[0]+faceRect[2]-projected[0]-projected[2])*.5f/ppu)+
                    camera.transform.up*((faceRect[1]+faceRect[3]-projected[1]-projected[3])*.5f/ppu);
                Persist(child);
            }
            b=ProjectBounds(board,camera);
            if(Mathf.Abs(b[0])>1||Mathf.Abs(b[1])>1||Mathf.Abs(b[2]-1920)>1||b[3]>RectBounds(map,camera)[1]-20)
                throw new Exception("No-margin board/frame fit failed");
            EditorSceneManager.MarkSceneDirty(s); EditorSceneManager.SaveScene(s); Undo.CollapseUndoOperations(group);
            return new {backup,board=b,map=RectBounds(map,camera),story=RectBounds(story,camera),scale=board.localScale.ToString("F4")};
        }
        catch{Undo.RevertAllDownToGroup(group);throw;}
    }
}
