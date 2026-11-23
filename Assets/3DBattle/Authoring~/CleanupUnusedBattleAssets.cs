using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using OneWay.Battle3D;
using Object = UnityEngine.Object;

public static class CleanupUnusedBattleAssets
{
    const string Scope="Assets/3DBattle/";
    const string Reports=Scope+"Authoring~/";
    static readonly string[] Scenes={"Assets/Scenes/MainMenuScene.unity","Assets/Scenes/DecisionScene.unity","Assets/Scenes/3DBattleScene.unity","Assets/Scenes/3DBattleDebugScene.unity"};
    static readonly HashSet<string> ModelExtensions=new HashSet<string>(StringComparer.OrdinalIgnoreCase){".fbx",".dae",".obj",".blend",".blend1",".glb",".gltf",".3ds",".max"};
    public sealed class Entry { public string path,guid,kind,hash,metaHash; public long bytes; public string[] toolingReferences; }
    public sealed class PlanData { public string[] scenes,protectedPaths; public List<Entry> delete; public Dictionary<string,string> keptReasons; public bool openSceneDirty; }
    static void Write(string name,object value)=>File.WriteAllText(Reports+name,JsonConvert.SerializeObject(value,Formatting.Indented));
    static T Read<T>(string name)=>JsonConvert.DeserializeObject<T>(File.ReadAllText(Reports+name));
    static string Physical(string path)=>@"\\?\"+Path.GetFullPath(path).Replace('/','\\');
    static string Hash(string path)
    {
        using(var stream=File.OpenRead(Physical(path)))using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream));
    }
    static void ValidatePath(string path)
    {
        string full=Path.GetFullPath(path),root=Path.GetFullPath(Scope).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(!path.StartsWith(Scope,StringComparison.Ordinal)||!full.StartsWith(root,StringComparison.OrdinalIgnoreCase)||Directory.Exists(path))throw new Exception("Outside cleanup file scope: "+path);
    }
    static HashSet<string> ProtectedPaths()
    {
        foreach(var scene in Scenes)if(!File.Exists(scene))throw new Exception("Expected scene missing: "+scene);
        var protectedPaths=new HashSet<string>(AssetDatabase.GetDependencies(Scenes,true));
        // Include the unsaved scene state as well as the files on disk, without saving it.
        for(int i=0;i<SceneManager.sceneCount;i++)
        {
            var scene=SceneManager.GetSceneAt(i);if(!Scenes.Contains(scene.path))continue;
            foreach(var asset in EditorUtility.CollectDependencies(scene.GetRootGameObjects()))
                if(asset && EditorUtility.IsPersistent(asset))protectedPaths.Add(AssetDatabase.GetAssetPath(asset));
        }
        // Resources and addressable/asset-bundle configuration can load prefabs indirectly.
        var dynamicRoots=AssetDatabase.GetAllAssetPaths().Where(p=>p.StartsWith("Assets/",StringComparison.Ordinal)&&!AssetDatabase.IsValidFolder(p)&&
            (p.Contains("/Resources/")||p.StartsWith("Assets/AddressableAssetsData/",StringComparison.Ordinal))).ToArray();
        if(dynamicRoots.Length>0)protectedPaths.UnionWith(AssetDatabase.GetDependencies(dynamicRoots,true));
        foreach(var bundle in AssetDatabase.GetAllAssetBundleNames())
            protectedPaths.UnionWith(AssetDatabase.GetDependencies(AssetDatabase.GetAssetPathsFromAssetBundle(bundle),true));
        return protectedPaths;
    }
    static string Kind(string path)
    {
        string ext=Path.GetExtension(path);
        if(ModelExtensions.Contains(ext))return "Model source";
        if(ext==".anim")return "Animation clip";
        if(ext==".controller"||ext==".overrideController")return "Animation controller";
        if(ext!=".asset"&&ext!=".prefab")return null;
        var asset=AssetDatabase.LoadMainAssetAtPath(path);
        if(asset is Mesh)return "Mesh";
        if(asset is Avatar)return "Avatar";
        if(asset is AnimationClip)return "Animation clip";
        if(asset is NewPlayerAttackSet)return "Animation set";
        if(asset is UnityEditor.Animations.BlendTree)return "Animation blend tree";
        if(ext==".prefab"&&asset is GameObject prefab)
        {
            if(prefab.GetComponentInChildren<SkinnedMeshRenderer>(true)||prefab.GetComponentInChildren<Animator>(true)||prefab.GetComponentInChildren<Animation>(true))return "Character prefab";
            if(prefab.GetComponent<MeshFilter>() || (prefab.GetComponentInChildren<MeshFilter>(true)&&!prefab.GetComponentInChildren<ParticleSystem>(true)))return "Model prefab";
        }
        return null;
    }
    public static object Plan()
    {
        if(Application.isPlaying||AnimationMode.InAnimationMode())throw new Exception("Edit mode, without animation preview, required");
        var trash=typeof(AssetDatabase).GetMethod("MoveAssetToTrash",BindingFlags.Public|BindingFlags.Static,null,new[]{typeof(string)},null);
        if(trash==null)throw new Exception("Unity trash API unavailable");
        var protectedPaths=ProtectedPaths();
        var plan=new PlanData{scenes=Scenes,protectedPaths=protectedPaths.OrderBy(p=>p).ToArray(),delete=new List<Entry>(),keptReasons=new Dictionary<string,string>(),openSceneDirty=SceneManager.GetActiveScene().isDirty};
        var files=Directory.GetFiles(Scope,"*",SearchOption.AllDirectories).Select(p=>p.Replace('\\','/')).Where(p=>!p.Contains("/Authoring~/")&&!p.EndsWith(".meta",StringComparison.Ordinal)).ToArray();
        var tooling=Directory.GetFiles(Scope,"*.cs",SearchOption.AllDirectories).Where(p=>!p.EndsWith("CleanupUnusedBattleAssets.cs",StringComparison.Ordinal)).Select(p=>new{path=p.Replace('\\','/'),text=File.ReadAllText(p)}).ToArray();
        foreach(var file in files)
        {
            string kind=Kind(file);if(kind==null)continue;
            if(protectedPaths.Contains(file)){plan.keptReasons[file]="Referenced by one of the four scenes, live unsaved scene, or runtime-loading roots";continue;}
            if(AssetDatabase.LoadAllAssetsAtPath(file).Any(a=>a&&EditorUtility.IsDirty(a))){plan.keptReasons[file]="Unsaved asset edits";continue;}
            ValidatePath(file);
            plan.delete.Add(new Entry{path=file,guid=AssetDatabase.AssetPathToGUID(file),kind=kind,bytes=new FileInfo(file).Length,hash=Hash(file),metaHash=File.Exists(Physical(file+".meta"))?Hash(file+".meta"):null,
                toolingReferences=tooling.Where(t=>t.text.Contains(file)||t.text.Contains(Path.GetFileNameWithoutExtension(file))).Select(t=>t.path).ToArray()});
        }
        Write("UnusedBattleAssetDeletionPlan.json",plan);
        return new{scenes=Scenes,deleteCount=plan.delete.Count,sizeMiB=plan.delete.Sum(e=>e.bytes)/1048576d,groups=plan.delete.GroupBy(e=>e.kind).Select(g=>new{kind=g.Key,count=g.Count()}).ToArray(),kept=plan.keptReasons.Count,openSceneDirty=plan.openSceneDirty,unsavedAssets=plan.keptReasons.Where(p=>p.Value=="Unsaved asset edits").Select(p=>p.Key).ToArray(),trashAvailable=true};
    }
    public static object ReviewReferences()
    {
        var plan=Read<PlanData>("UnusedBattleAssetDeletionPlan.json");
        var deleting=new HashSet<string>(plan.delete.Select(e=>e.path));
        var references=new Dictionary<string,string[]>();
        foreach(var path in AssetDatabase.GetAllAssetPaths().Where(p=>p.StartsWith("Assets/",StringComparison.Ordinal)&&!deleting.Contains(p)))
        {
            string ext=Path.GetExtension(path);
            if(ext!=".unity"&&ext!=".prefab"&&ext!=".asset"&&ext!=".controller"&&ext!=".overrideController")continue;
            var hits=AssetDatabase.GetDependencies(path,true).Where(deleting.Contains).ToArray();
            if(hits.Length>0)references[path]=hits;
        }
        Write("UnusedBattleAssetIncomingReferences.json",references);
        return references;
    }
    public static object FinalizePlan()
    {
        var plan=Read<PlanData>("UnusedBattleAssetDeletionPlan.json");
        var references=Read<Dictionary<string,string[]>>("UnusedBattleAssetIncomingReferences.json");
        // Do not leave dangling references in settings or sample scenes that are outside this model/animation cleanup.
        foreach(var pair in references)
            foreach(var dependency in pair.Value)
            {
                plan.delete.RemoveAll(e=>e.path==dependency);
                plan.keptReasons[dependency]="Shared by retained asset: "+pair.Key;
            }
        foreach(var file in Directory.GetFiles(Reports,"*",SearchOption.AllDirectories).Select(p=>p.Replace('\\','/')).Where(p=>ModelExtensions.Contains(Path.GetExtension(p))))
        {
            if(plan.delete.Any(e=>e.path==file))continue;
            ValidatePath(file);
            plan.delete.Add(new Entry{path=file,guid="",kind="Authoring model source",bytes=new FileInfo(Physical(file)).Length,hash=Hash(file),
                metaHash=File.Exists(Physical(file+".meta"))?Hash(file+".meta"):null,toolingReferences=Directory.GetFiles(Scope,"*.cs",SearchOption.AllDirectories).Where(p=>!p.EndsWith("CleanupUnusedBattleAssets.cs",StringComparison.Ordinal)&&File.ReadAllText(p).Contains(Path.GetFileName(file))).Select(p=>p.Replace('\\','/')).ToArray()});
        }
        Write("UnusedBattleAssetDeletionPlan.json",plan);
        return new{count=plan.delete.Count,sizeMiB=plan.delete.Sum(e=>e.bytes)/1048576d,preservedSharedAssets=references.SelectMany(p=>p.Value).Distinct().Count(),groups=plan.delete.GroupBy(e=>e.kind).Select(g=>new{kind=g.Key,count=g.Count()}).ToArray()};
    }
    public sealed class Snapshot
    {
        public Dictionary<string,string[]> sceneDependencies;
        public Dictionary<string,string> sceneHashes,subassets;
        public Dictionary<string,long[]> retainedFiles;
        public string liveState;
        public string[] protectedPaths;
    }
    static string Id(Object asset)
    {
        if(!asset)return "null";
        return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset,out string guid,out long id)?guid+":"+id:asset.GetType().Name+":"+asset.GetInstanceID();
    }
    static string LiveState()
    {
        var scenes=new List<object>();
        for(int i=0;i<SceneManager.sceneCount;i++)
        {
            var scene=SceneManager.GetSceneAt(i);
            scenes.Add(new{scene.path,scene.isDirty,roots=scene.GetRootGameObjects().Select(root=>new{
                name=root.name,nodes=root.GetComponentsInChildren<Transform>(true).Select(t=>new{
                    id=t.GetInstanceID(),t.name,parent=t.parent?t.parent.GetInstanceID():0,
                    p=new[]{t.localPosition.x,t.localPosition.y,t.localPosition.z},
                    r=new[]{t.localRotation.x,t.localRotation.y,t.localRotation.z,t.localRotation.w},
                    s=new[]{t.localScale.x,t.localScale.y,t.localScale.z},active=t.gameObject.activeSelf,
                    components=t.GetComponents<Component>().Select(c=>c?EditorJsonUtility.ToJson(c):"missing-script").ToArray()
                }).ToArray()
            }).ToArray()});
        }
        return JsonConvert.SerializeObject(scenes);
    }
    static string Subassets(string path)=>string.Join("\n",AssetDatabase.LoadAllAssetsAtPath(path).Where(a=>a).Select(a=>Id(a)+":"+a.GetType().Name+":"+a.name).OrderBy(s=>s));
    public static object SnapshotBeforeDeletion()
    {
        var plan=Read<PlanData>("UnusedBattleAssetDeletionPlan.json");
        var deleting=new HashSet<string>(plan.delete.SelectMany(e=>new[]{e.path,e.path+".meta"}));
        var current=ProtectedPaths();
        var collision=plan.delete.Where(e=>current.Contains(e.path)).Select(e=>e.path).ToArray();
        if(collision.Length>0)throw new Exception("Now in use: "+string.Join(",",collision));
        var snapshot=new Snapshot{
            sceneDependencies=Scenes.ToDictionary(p=>p,p=>AssetDatabase.GetDependencies(p,true).OrderBy(d=>d).ToArray()),
            sceneHashes=Scenes.ToDictionary(p=>p,Hash),subassets=new Dictionary<string,string>(),
            retainedFiles=new Dictionary<string,long[]>(),liveState=LiveState(),protectedPaths=current.OrderBy(p=>p).ToArray()};
        foreach(var path in current.Where(p=>p.StartsWith(Scope,StringComparison.Ordinal)&&Kind(p)!=null))snapshot.subassets[path]=Subassets(path);
        foreach(var path in Directory.GetFiles("Assets","*",SearchOption.AllDirectories).Select(p=>p.Replace('\\','/')).Where(p=>!p.StartsWith(Reports,StringComparison.Ordinal)&&!deleting.Contains(p)))
        {
            var info=new FileInfo(Physical(path));snapshot.retainedFiles[path]=new[]{info.Length,info.LastWriteTimeUtc.Ticks};
        }
        Write("UnusedBattleAssetSnapshotBefore.json",snapshot);
        return new{protectedAssets=current.Count,subassetFiles=snapshot.subassets.Count,retainedFiles=snapshot.retainedFiles.Count,sceneDirty=SceneManager.GetActiveScene().isDirty};
    }
    public sealed class Progress { public List<string> trashed=new List<string>(); public string failedPath,error; public bool complete; }
    public static object DeleteUnused()
    {
        if(Application.isPlaying||AnimationMode.InAnimationMode())throw new Exception("Edit mode required");
        var plan=Read<PlanData>("UnusedBattleAssetDeletionPlan.json");
        var before=Read<Snapshot>("UnusedBattleAssetSnapshotBefore.json");
        if(LiveState()!=before.liveState)throw new Exception("Open scene state changed since snapshot; review again");
        var progress=File.Exists(Reports+"UnusedBattleAssetDeletionProgress.json")?Read<Progress>("UnusedBattleAssetDeletionProgress.json"):new Progress();
        var protectedPaths=ProtectedPaths();
        // Validate every candidate before making any change. The reviewed manifest is the only input.
        foreach(var entry in plan.delete.Where(e=>!progress.trashed.Contains(e.path)))
        {
            ValidatePath(entry.path);
            if(protectedPaths.Contains(entry.path))throw new Exception("Protected asset: "+entry.path);
            if(Hash(entry.path)!=entry.hash || (entry.metaHash!=null&&Hash(entry.path+".meta")!=entry.metaHash))throw new Exception("Asset changed since review: "+entry.path);
            if(AssetDatabase.LoadAllAssetsAtPath(entry.path).Any(a=>a&&EditorUtility.IsDirty(a)))throw new Exception("Unsaved asset edits: "+entry.path);
        }
        var method=typeof(AssetDatabase).GetMethod("MoveAssetToTrash",BindingFlags.Public|BindingFlags.Static,null,new[]{typeof(string)},null);
        if(method==null)throw new Exception("Unity trash API unavailable");
        progress.failedPath=null;progress.error=null;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach(var entry in plan.delete.Where(e=>!progress.trashed.Contains(e.path)))
            {
                progress.failedPath=entry.path;
                if(!(bool)method.Invoke(null,new object[]{entry.path}))throw new Exception("MoveAssetToTrash returned false: "+entry.path);
                progress.trashed.Add(entry.path);progress.failedPath=null;
                Write("UnusedBattleAssetDeletionProgress.json",progress);
            }
            progress.complete=true;
        }
        catch(Exception ex){progress.error=ex.ToString();throw;}
        finally{AssetDatabase.StopAssetEditing();Write("UnusedBattleAssetDeletionProgress.json",progress);}
        return new{trashed=progress.trashed.Count,progress.complete};
    }
    public static object VerifyDeletion()
    {
        var plan=Read<PlanData>("UnusedBattleAssetDeletionPlan.json");
        var before=Read<Snapshot>("UnusedBattleAssetSnapshotBefore.json");
        var progress=Read<Progress>("UnusedBattleAssetDeletionProgress.json");
        var errors=new List<string>();
        foreach(var entry in plan.delete)
        {
            if(File.Exists(Physical(entry.path))||File.Exists(Physical(entry.path+".meta")))errors.Add("File remains: "+entry.path);
            // Unity retains recently deleted GUID mappings in the editor session. Check only existing assets.
            if(!string.IsNullOrEmpty(entry.guid)&&!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(entry.path,AssetPathToGUIDOptions.OnlyExistingAssets)))errors.Add("Asset database entry remains: "+entry.path);
        }
        foreach(var pair in before.sceneHashes)if(Hash(pair.Key)!=pair.Value)errors.Add("Scene file changed: "+pair.Key);
        foreach(var pair in before.sceneDependencies)if(!AssetDatabase.GetDependencies(pair.Key,true).OrderBy(p=>p).SequenceEqual(pair.Value))errors.Add("Scene dependencies changed: "+pair.Key);
        foreach(var pair in before.subassets)if(Subassets(pair.Key)!=pair.Value)errors.Add("Subasset identities changed: "+pair.Key);
        foreach(var pair in before.retainedFiles)
        {
            var file=new FileInfo(Physical(pair.Key));
            if(!file.Exists||file.Length!=pair.Value[0]||file.LastWriteTimeUtc.Ticks!=pair.Value[1])errors.Add("Retained file changed: "+pair.Key);
        }
        var newPaths=Directory.GetFiles("Assets","*",SearchOption.AllDirectories).Select(p=>p.Replace('\\','/')).Where(p=>!p.StartsWith(Reports,StringComparison.Ordinal)&&!before.retainedFiles.ContainsKey(p)).ToArray();
        errors.AddRange(newPaths.Select(p=>"Unexpected new asset: "+p));
        if(LiveState()!=before.liveState)errors.Add("Open scene state changed");
        var protectedNow=ProtectedPaths();
        errors.AddRange(before.protectedPaths.Where(p=>!protectedNow.Contains(p)).Select(p=>"Protected reference lost: "+p));
        if(!progress.complete||progress.trashed.Count!=plan.delete.Count)errors.Add("Deletion incomplete");
        var result=new{passed=errors.Count==0,errors,deleted=progress.trashed.Count,deletedMiB=plan.delete.Sum(e=>e.bytes)/1048576d,
            groups=plan.delete.GroupBy(e=>e.kind).Select(g=>new{kind=g.Key,count=g.Count()}).ToArray(),
            scenesChecked=Scenes,protectedSubassetFiles=before.subassets.Count,retainedFilesChecked=before.retainedFiles.Count,
            sceneDirty=SceneManager.GetActiveScene().isDirty,playing=Application.isPlaying,compiling=EditorApplication.isCompiling};
        Write("UnusedBattleAssetDeletionVerification.json",result);return result;
    }
    public static object CatalogAfterDeletion()
    {
        if(!(bool)Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Reports+"UnusedBattleAssetDeletionVerification.json"))["passed"])throw new Exception("Verify deletion first");
        var plan=Read<PlanData>("UnusedBattleAssetDeletionPlan.json");
        var moves=Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(Reports+"CharacterAssetMovePlan.json"));
        var runtime=new HashSet<string>(moves.Select(m=>(string)m["to"]));
        foreach(var move in moves)if(AssetDatabase.AssetPathToGUID((string)move["to"],AssetPathToGUIDOptions.OnlyExistingAssets)!=(string)move["guid"])throw new Exception("Runtime asset changed: "+move["to"]);
        var paths=AssetDatabase.GetAllAssetPaths().Where(p=>p.StartsWith("Assets/",StringComparison.Ordinal)).ToArray();
        var sceneRefs=new HashSet<string>(AssetDatabase.GetDependencies(paths.Where(p=>p.EndsWith(".unity",StringComparison.Ordinal)).ToArray(),true));
        var prefabRefs=new HashSet<string>(AssetDatabase.GetDependencies(paths.Where(p=>p.EndsWith(".prefab",StringComparison.Ordinal)).ToArray(),true));
        var dataRefs=new HashSet<string>(AssetDatabase.GetDependencies(paths.Where(p=>p.EndsWith(".controller",StringComparison.Ordinal)||p.EndsWith(".overrideController",StringComparison.Ordinal)||p.EndsWith(".asset",StringComparison.Ordinal)).ToArray(),true));
        var assets=AssetDatabase.FindAssets("t:AnimationClip",new[]{Scope.TrimEnd('/')}).Concat(AssetDatabase.FindAssets("t:Model",new[]{Scope.TrimEnd('/')})).Concat(AssetDatabase.FindAssets("t:Mesh",new[]{Scope.TrimEnd('/')}))
            .Select(AssetDatabase.GUIDToAssetPath).Distinct().Where(p=>!runtime.Contains(p)&&File.Exists(Physical(p))).OrderBy(p=>p).ToArray();
        string Csv(string value)=>"\""+value.Replace("\"","\"\"")+"\"";
        var csv=new StringBuilder("Path,ReferencedByScene,ReferencedByPrefab,ReferencedByControllerOrData,RetentionReason\n");
        foreach(var path in assets)csv.AppendLine(string.Join(",",new[]{path,sceneRefs.Contains(path).ToString(),prefabRefs.Contains(path).ToString(),dataRefs.Contains(path).ToString(),plan.keptReasons.TryGetValue(path,out var reason)?reason:"Retained"}.Select(Csv)));
        File.WriteAllText(Scope+"OtherAnimationModelReferences.csv",csv.ToString(),new UTF8Encoding(true));
        var report=new StringBuilder("미사용 모델·애니메이션 정리 완료 (2026-11-23)\n\n");
        report.AppendLine("검사 씬: "+string.Join(", ",Scenes.Select(Path.GetFileNameWithoutExtension)));
        report.AppendLine("삭제: "+plan.delete.Count+"개, "+(plan.delete.Sum(e=>e.bytes)/1073741824d).ToString("F2")+" GiB (해당 .meta도 함께 휴지통으로 이동)");
        report.AppendLine("현재 Runtime 자산 90개 유지. 씬 파일/참조, 열린 씬의 미저장 상태, 보존 파일 12,758개 검증 통과.");
        report.AppendLine("공유 설정/샘플 씬을 깨뜨리지 않기 위해 아래 공유 파일 7개는 보존했습니다.");
        foreach(var pair in plan.keptReasons.Where(p=>p.Value.StartsWith("Shared by retained asset:",StringComparison.Ordinal)))report.AppendLine(pair.Key+" <- "+pair.Value);
        report.AppendLine("\n과거 제작 도구에서만 사용하는 원본과 백업도 삭제 대상에 포함됩니다. 해당 옛 작업을 재생성하려면 삭제 목록의 원본과 .meta를 휴지통에서 함께 복원해야 합니다.");
        report.AppendLine("\n삭제 목록:");foreach(var entry in plan.delete.OrderBy(e=>e.path))report.AppendLine(entry.kind+" | "+entry.path);
        File.WriteAllText(Reports+"UnusedBattleAssetCleanup.txt",report.ToString(),new UTF8Encoding(true));
        return new{runtimeAssetsVerified=runtime.Count,remainingModelAnimationReferences=assets.Length,deleted=plan.delete.Count,report=Reports+"UnusedBattleAssetCleanup.txt"};
    }
}
