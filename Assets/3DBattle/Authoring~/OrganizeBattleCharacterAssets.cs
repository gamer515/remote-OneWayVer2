using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using OneWay.Battle3D;
using Object = UnityEngine.Object;

public static class OrganizeBattleCharacterAssets
{
    const string Scope = "Assets/3DBattle/";
    const string ReportFolder = Scope + "Authoring~/";
    public sealed class Reference
    {
        public string actor, path, guid, type, name, source;
        public long localId;
    }
    public static List<Reference> Scan()
    {
        if (Application.isPlaying || AnimationMode.InAnimationMode()) throw new Exception("Edit mode with animation preview stopped is required");
        var battle = Object.FindObjectsByType<Battle3DSceneController>(FindObjectsInactive.Include,FindObjectsSortMode.None)
            .Single(x=>x.gameObject.scene.path=="Assets/Scenes/3DBattleScene.unity");
        var list=new List<Reference>();
        AddActor(list,"Player",battle.player.gameObject,battle.player.animator);
        foreach(var stage in battle.stages.Where(s=>s && (s.StageIndex==1 || s.StageIndex==2)))
            AddActor(list,"B"+stage.StageIndex,stage.boss.gameObject,stage.boss.animator);
        return list.GroupBy(r=>r.actor+":"+r.guid+":"+r.localId).Select(g=>g.First()).ToList();
    }
    static void Add(List<Reference> list,string actor,Object asset,string source)
    {
        if(!asset || !EditorUtility.IsPersistent(asset))return;
        var path=AssetDatabase.GetAssetPath(asset);if(!path.StartsWith("Assets/",StringComparison.Ordinal))return;
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset,out string guid,out long localId);
        list.Add(new Reference{actor=actor,path=path,guid=guid,localId=localId,type=asset.GetType().Name,name=asset.name,source=source});
    }
    static void AddActor(List<Reference> list,string label,GameObject root,Animator animator)
    {
        if(!animator || !animator.runtimeAnimatorController)throw new Exception(label+" animator missing");
        Add(list,label,animator.runtimeAnimatorController,"Animator");Add(list,label,animator.avatar,"Avatar");
        foreach(var clip in animator.runtimeAnimatorController.animationClips.Distinct())Add(list,label,clip,"Animator clip");
        Add(list,label,PrefabUtility.GetCorrespondingObjectFromOriginalSource(animator.gameObject),"Model source");
        var components=animator.GetComponentsInChildren<Component>(true).Concat(root.GetComponents<Component>()).Where(c=>c).Distinct();
        foreach(var component in components)
        {
            if(component is SkinnedMeshRenderer skin)Add(list,label,skin.sharedMesh,"Body mesh");
            if(component is MeshFilter filter)Add(list,label,filter.sharedMesh,"Equipment mesh");
            if(component is Renderer renderer)foreach(var material in renderer.sharedMaterials)Add(list,label,material,"Model material");
            if(component is MeshyAnimationDebugger)continue; // Separate preview playlist, not the combat animation set.
            var serialized=new SerializedObject(component);var p=serialized.GetIterator();
            while(p.Next(true))if(p.propertyType==SerializedPropertyType.ObjectReference)
            {
                var value=p.objectReferenceValue;
                if(value is AnimationClip || value is NewPlayerAttackSet || value is BossAttackPattern)
                    Add(list,label,value,component.GetType().Name+"."+p.propertyPath);
            }
        }
        // Attack sets/patterns can reference extra clips outside the Animator's state list.
        foreach(var path in list.Where(r=>r.actor==label && (r.type==nameof(NewPlayerAttackSet) || r.type.EndsWith("Pattern"))).Select(r=>r.path).Distinct().ToArray())
            foreach(var dependency in AssetDatabase.GetDependencies(path,true))
                foreach(var clip in AssetDatabase.LoadAllAssetsAtPath(dependency).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")))
                    Add(list,label,clip,"Attack data dependency");
    }
    public static object Inventory()
    {
        var refs=Scan();
        File.WriteAllText(ReportFolder+"CharacterAssetInventoryBefore.json",Newtonsoft.Json.JsonConvert.SerializeObject(refs,Newtonsoft.Json.Formatting.Indented));
        return refs.GroupBy(r=>r.actor).Select(g=>new{actor=g.Key,files=g.GroupBy(r=>r.path).Select(f=>new{path=f.Key,types=f.Select(r=>r.type).Distinct().ToArray(),sources=f.Select(r=>r.source).Distinct().ToArray()}).ToArray()}).ToArray();
    }
    public sealed class Move
    {
        public string from,to,guid,actor,category;
        public string[] referencers;
    }
    static string ActorRoot(string actor)=>Scope+(actor=="Player"?"NewPlayer":actor=="B1"?"Boss1":"Boss2")+"/Runtime";
    static string Owner(string path,IEnumerable<Reference> refs)
    {
        if(path.StartsWith(Scope+"NewPlayer/",StringComparison.Ordinal) || path==Scope+"Shield4.fbx" || path==Scope+"Shield4_Mat.mat" || path==Scope+"Materials/KnightArmor.mat")return "Player";
        if(path.StartsWith(Scope+"Boss2/",StringComparison.Ordinal) || path.StartsWith(Scope+"Meshy_AI_lowpoly_paladin_tpose_biped/",StringComparison.Ordinal))return "B2";
        return refs.Select(r=>r.actor).First();
    }
    public static object Plan()
    {
        var refs=Scan();
        // Bring material/texture dependencies with the used model; keep shared shader/code assets in place.
        foreach(var seed in refs.Where(r=>r.type==nameof(Mesh)||r.type==nameof(Material)||r.type==nameof(GameObject)).ToArray())
            foreach(var path in AssetDatabase.GetDependencies(seed.path,true))
            {
                if(!path.StartsWith(Scope,StringComparison.Ordinal))continue;
                var asset=AssetDatabase.LoadMainAssetAtPath(path);
                if(asset is Material || asset is Texture)Add(refs,seed.actor,asset,"Model dependency");
            }
        refs=refs.GroupBy(r=>r.actor+":"+r.guid+":"+r.localId).Select(g=>g.First()).ToList();
        var moves=new List<Move>();
        foreach(var group in refs.Where(r=>r.path.StartsWith(Scope,StringComparison.Ordinal)).GroupBy(r=>r.path))
        {
            string path=group.Key,actor=Owner(path,group),category;
            if(path==Scope+"Materials/Attack.mat")continue; // Shared debug/attack-window material, not a character surface.
            var main=AssetDatabase.LoadMainAssetAtPath(path);
            if(main is Material) category="Model/Materials";
            else if(main is Texture) category="Model/Textures";
            else if(main is RuntimeAnimatorController) category="Animator";
            else if(main is AnimationClip) category="Animations";
            else if(main is NewPlayerAttackSet || main is BossAttackPattern)category="Data";
            else if(group.Any(r=>r.source=="Body mesh" || r.source=="Model source"))category="Model";
            else if(group.Any(r=>r.source=="Equipment mesh"))category="Model/Equipment";
            else if(group.Any(r=>r.type==nameof(AnimationClip)))category="Animations/SourceModels";
            else continue;
            string destination=ActorRoot(actor)+"/"+category+"/"+Path.GetFileName(path);
            if(destination==path)continue;
            if(moves.Any(m=>m.to==destination))throw new Exception("Destination collision: "+destination);
            if(File.Exists(destination))throw new Exception("Destination already exists: "+destination);
            moves.Add(new Move{from=path,to=destination,guid=AssetDatabase.AssetPathToGUID(path),actor=actor,category=category});
        }
        // Query every saved scene/prefab/controller/data asset, including other team folders, read-only.
        var candidates=AssetDatabase.GetAllAssetPaths().Where(p=>p.StartsWith("Assets/",StringComparison.Ordinal) &&
            new[]{".unity",".prefab",".controller",".overrideController",".asset",".mat"}.Contains(Path.GetExtension(p))).ToArray();
        var bySource=moves.ToDictionary(m=>m.from,m=>new HashSet<string>());
        foreach(var path in candidates)foreach(var dependency in AssetDatabase.GetDependencies(path,true))
            if(dependency!=path && bySource.TryGetValue(dependency,out var users))users.Add(path);
        foreach(var move in moves)move.referencers=bySource[move.from].OrderBy(p=>p).ToArray();
        File.WriteAllText(ReportFolder+"CharacterAssetInventoryBefore.json",Newtonsoft.Json.JsonConvert.SerializeObject(refs,Newtonsoft.Json.Formatting.Indented));
        File.WriteAllText(ReportFolder+"CharacterAssetMovePlan.json",Newtonsoft.Json.JsonConvert.SerializeObject(moves,Newtonsoft.Json.Formatting.Indented));
        return new{total=moves.Count,groups=moves.GroupBy(m=>m.actor+"/"+m.category).Select(g=>new{folder=g.Key,count=g.Count()}).ToArray(),external=refs.Where(r=>!r.path.StartsWith(Scope,StringComparison.Ordinal)).Select(r=>r.path).Distinct().ToArray(),otherSceneReferences=moves.Count(m=>m.referencers.Any(p=>p.StartsWith("Assets/Decision_YYS/",StringComparison.Ordinal)||p.StartsWith("Assets/MainMenu/",StringComparison.Ordinal)))};
    }
    static T Read<T>(string name)=>Newtonsoft.Json.JsonConvert.DeserializeObject<T>(File.ReadAllText(ReportFolder+name));
    static void Write(string name,object value)=>File.WriteAllText(ReportFolder+name,Newtonsoft.Json.JsonConvert.SerializeObject(value,Newtonsoft.Json.Formatting.Indented));
    static string Hash(string file)
    {
        using(var stream=File.OpenRead(file))using(var sha=System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(stream));
    }
    static string Id(Object asset)
    {
        if(!asset)return "null";
        return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset,out string guid,out long id)?guid+":"+id:asset.GetType().Name+":"+asset.name;
    }
    static string Subassets(string path)=>string.Join("\n",AssetDatabase.LoadAllAssetsAtPath(path).Where(x=>x).Select(x=>Id(x)+":"+x.GetType().Name+":"+x.name).OrderBy(x=>x));
    static string ActorState()
    {
        var battle=Object.FindObjectsByType<Battle3DSceneController>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(c=>c.gameObject.scene.path=="Assets/Scenes/3DBattleScene.unity");
        var actors=new[]{battle.player.animator,battle.FindStage(1).boss.animator,battle.FindStage(2).boss.animator};
        return Newtonsoft.Json.JsonConvert.SerializeObject(actors.Select(a=>new{
            animator=Id(a.runtimeAnimatorController),avatar=Id(a.avatar),
            nodes=a.GetComponentsInChildren<Transform>(true).Select(t=>new{name=t.name,p=V(t.localPosition),r=Q(t.localRotation),s=V(t.localScale),active=t.gameObject.activeSelf}).ToArray(),
            skins=a.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(s=>new{s.name,mesh=Id(s.sharedMesh),materials=s.sharedMaterials.Select(Id).ToArray(),bones=s.bones.Select(t=>t?t.name:null).ToArray(),s.enabled}).ToArray(),
            clips=a.runtimeAnimatorController.animationClips.Distinct().Select(c=>new{id=Id(c),c.name,c.length,c.frameRate,bindings=AnimationUtility.GetCurveBindings(c).Length}).OrderBy(c=>c.id).ToArray()
        }).ToArray());
    }
    static float[] V(Vector3 v)=>new[]{v.x,v.y,v.z};
    static float[] Q(Quaternion q)=>new[]{q.x,q.y,q.z,q.w};
    public sealed class Snapshot
    {
        public Dictionary<string,string> hashes,subassets,scenes;
        public Dictionary<string,long> outsideFiles;
        public string actorState;
    }
    public static object SnapshotBeforeMove()
    {
        var moves=Read<List<Move>>("CharacterAssetMovePlan.json");
        var snapshot=new Snapshot{hashes=new Dictionary<string,string>(),subassets=new Dictionary<string,string>(),scenes=new Dictionary<string,string>(),outsideFiles=new Dictionary<string,long>(),actorState=ActorState()};
        foreach(var move in moves)
        {
            if(!move.from.StartsWith(Scope,StringComparison.Ordinal)||!move.to.StartsWith(Scope,StringComparison.Ordinal))throw new Exception("Out-of-scope move");
            snapshot.hashes[move.guid]=Hash(move.from);snapshot.subassets[move.guid]=Subassets(move.from);
        }
        foreach(var file in Directory.GetFiles("Assets","*",SearchOption.AllDirectories).Select(p=>p.Replace('\\','/')).Where(p=>!p.StartsWith(Scope,StringComparison.Ordinal)))
        {
            snapshot.outsideFiles[file]=File.GetLastWriteTimeUtc(file).Ticks;
            if(file.EndsWith(".unity",StringComparison.Ordinal))snapshot.scenes[file]=Hash(file);
        }
        Write("CharacterAssetSnapshotBefore.json",snapshot);
        return new{assets=moves.Count,sceneFiles=snapshot.scenes.Count,outsideFiles=snapshot.outsideFiles.Count,sceneDirty=UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty};
    }
    static void Folder(string path)
    {
        if(AssetDatabase.IsValidFolder(path))return;
        string parent=Path.GetDirectoryName(path).Replace('\\','/');Folder(parent);
        if(string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent,Path.GetFileName(path))))throw new Exception("Cannot create "+path);
    }
    public static object ApplyMoves()
    {
        if(Application.isPlaying || AnimationMode.InAnimationMode())throw new Exception("Edit mode required");
        if(!File.Exists(ReportFolder+"CharacterAssetSnapshotBefore.json"))throw new Exception("Capture baseline first");
        var moves=Read<List<Move>>("CharacterAssetMovePlan.json");
        foreach(var move in moves)
        {
            if(!move.from.StartsWith(Scope,StringComparison.Ordinal)||!move.to.StartsWith(Scope,StringComparison.Ordinal))throw new Exception("Out-of-scope move");
            string current=AssetDatabase.GUIDToAssetPath(move.guid);
            if(current!=move.from && current!=move.to)throw new Exception("Unexpected source: "+current);
            if(current==move.from && File.Exists(move.to))throw new Exception("Will not overwrite "+move.to);
            Folder(Path.GetDirectoryName(move.to).Replace('\\','/'));
        }
        var completed=new List<string>();
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach(var move in moves)
            {
                if(AssetDatabase.GUIDToAssetPath(move.guid)==move.to){completed.Add(move.to);continue;}
                string error=AssetDatabase.MoveAsset(move.from,move.to);
                if(!string.IsNullOrEmpty(error))throw new Exception(move.from+": "+error);
                completed.Add(move.to);Write("CharacterAssetMoveProgress.json",completed);
            }
        }
        finally{AssetDatabase.StopAssetEditing();}
        return new{moved=completed.Count,deleted=0};
    }
    public static object Verify()
    {
        var moves=Read<List<Move>>("CharacterAssetMovePlan.json");var before=Read<Snapshot>("CharacterAssetSnapshotBefore.json");
        var errors=new List<string>();
        foreach(var move in moves)
        {
            if(AssetDatabase.GUIDToAssetPath(move.guid)!=move.to)errors.Add("Wrong GUID path: "+move.from);
            if(!File.Exists(move.to)||File.Exists(move.from)){errors.Add("Wrong file placement: "+move.to);continue;}
            if(Hash(move.to)!=before.hashes[move.guid])errors.Add("Asset bytes changed: "+move.to);
            if(Subassets(move.to)!=before.subassets[move.guid])errors.Add("Subasset identities changed: "+move.to);
        }
        if(ActorState()!=before.actorState)errors.Add("Scene model/animation state changed");
        foreach(var scene in before.scenes)if(!File.Exists(scene.Key)||Hash(scene.Key)!=scene.Value)errors.Add("Scene file changed: "+scene.Key);
        foreach(var pair in before.outsideFiles)if(!File.Exists(pair.Key)||File.GetLastWriteTimeUtc(pair.Key).Ticks!=pair.Value)errors.Add("Outside file touched: "+pair.Key);
        var newOutside=Directory.GetFiles("Assets","*",SearchOption.AllDirectories).Select(p=>p.Replace('\\','/')).Where(p=>!p.StartsWith(Scope,StringComparison.Ordinal)&&!before.outsideFiles.ContainsKey(p)).ToArray();
        errors.AddRange(newOutside.Select(p=>"Outside file added: "+p));
        var refs=Read<List<Reference>>("CharacterAssetInventoryBefore.json");
        foreach(var reference in refs)
        {
            var path=AssetDatabase.GUIDToAssetPath(reference.guid);
            if(!AssetDatabase.LoadAllAssetsAtPath(path).Any(o=>o && Id(o)==reference.guid+":"+reference.localId))errors.Add("Missing reference: "+reference.name);
        }
        var result=new{moved=moves.Count,deleted=0,assetBytesUnchanged=errors.All(e=>!e.StartsWith("Asset bytes")),subassetIdentitiesUnchanged=errors.All(e=>!e.StartsWith("Subasset")),sceneFilesChecked=before.scenes.Count,outsideFilesChecked=before.outsideFiles.Count,errors};
        Write("CharacterAssetOrganizationVerification.json",result);
        if(errors.Count>0)throw new Exception(string.Join("\n",errors));
        return result;
    }
    public static object Catalog()
    {
        var moves=Read<List<Move>>("CharacterAssetMovePlan.json");
        string Csv(string value)=>"\""+value.Replace("\"","\"\"")+"\"";
        var csv=new System.Text.StringBuilder("Character,Category,CurrentPath,PreviousPath,GUID,ReferenceCount\n");
        foreach(var move in moves)csv.AppendLine(string.Join(",",new[]{move.actor,move.category,move.to,move.from,move.guid,move.referencers.Length.ToString()}.Select(Csv)));
        File.WriteAllText(Scope+"CurrentCharacterAssets.csv",csv.ToString(),new System.Text.UTF8Encoding(true));
        var paths=AssetDatabase.GetAllAssetPaths().Where(p=>p.StartsWith("Assets/",StringComparison.Ordinal)).ToArray();
        var scenes=paths.Where(p=>p.EndsWith(".unity",StringComparison.Ordinal)).ToArray();
        var prefabs=paths.Where(p=>p.EndsWith(".prefab",StringComparison.Ordinal)).ToArray();
        var data=paths.Where(p=>p.EndsWith(".controller",StringComparison.Ordinal)||p.EndsWith(".overrideController",StringComparison.Ordinal)||p.EndsWith(".asset",StringComparison.Ordinal)).ToArray();
        var sceneRefs=new HashSet<string>(AssetDatabase.GetDependencies(scenes,true));
        var prefabRefs=new HashSet<string>(AssetDatabase.GetDependencies(prefabs,true));
        var dataRefs=new HashSet<string>(AssetDatabase.GetDependencies(data,true));
        var retained=new HashSet<string>(moves.Select(m=>m.to));
        var assets=AssetDatabase.FindAssets("t:AnimationClip",new[]{Scope.TrimEnd('/')})
            .Concat(AssetDatabase.FindAssets("t:Model",new[]{Scope.TrimEnd('/')}))
            .Concat(AssetDatabase.FindAssets("t:Mesh",new[]{Scope.TrimEnd('/')}))
            .Select(AssetDatabase.GUIDToAssetPath).Distinct().Where(p=>!retained.Contains(p)).OrderBy(p=>p).ToArray();
        csv=new System.Text.StringBuilder("Path,ReferencedByScene,ReferencedByPrefab,ReferencedByControllerOrData,DeletionApproved\n");
        foreach(var path in assets)csv.AppendLine(string.Join(",",new[]{path,sceneRefs.Contains(path).ToString(),prefabRefs.Contains(path).ToString(),dataRefs.Contains(path).ToString(),"NO"}.Select(Csv)));
        File.WriteAllText(Scope+"OtherAnimationModelReferences.csv",csv.ToString(),new System.Text.UTF8Encoding(true));
        var external=Read<List<Reference>>("CharacterAssetInventoryBefore.json").Where(r=>!r.path.StartsWith(Scope,StringComparison.Ordinal)).Select(r=>r.path).Distinct();
        string note="현재 전투 캐릭터 자산 정리 (2026-11-23)\n\n"+
            "B1: Assets/3DBattle/Boss1/Runtime\nB2: Assets/3DBattle/Boss2/Runtime\n플레이어: Assets/3DBattle/NewPlayer/Runtime\n\n"+
            "Model: 현재 몸체 메시, 필요한 모델 원본, 장비, 재질과 텍스처\nAnimations: 현재 연결된 모션\nAnimator: 실제 사용하는 Animator Controller\nData: 공격 순서/판정/타이밍 등의 연결 데이터\n\n"+
            "모델과 모션 내용은 바꾸지 않고 기존 파일과 .meta를 함께 이동했습니다. 씬 파일은 저장하지 않았습니다. 삭제한 파일은 없습니다.\n"+
            "B1 Animations/SourceModels의 FBX는 현재 모션을 직접 참조하므로 유지해야 합니다. B1 Model의 FBX에는 대기 모션도 함께 들어 있습니다.\n"+
            "플레이어 Model의 AttackBody_BothFists.asset는 현재 몸체 메시입니다. 뼈대와 장비 배치, 헬멧/어깨 수정은 기존 씬 오브젝트에 그대로 있습니다. 이 메시 파일 자체가 완성형 캐릭터 프리팹은 아닙니다.\n\n"+
            "CurrentCharacterAssets.csv: 이동한 90개 파일의 현재/이전 위치와 GUID\n"+
            "OtherAnimationModelReferences.csv: 이동하지 않은 모션/모델의 다른 씬·프리팹·Controller/설정 참조 여부. Runtime 밖이라는 이유만으로 미사용 파일은 아닙니다. 이 목록은 삭제 허가 목록이 아닙니다.\n"+
            "3DBattleDebug의 사용자 편집본, 미리보기와 백업은 원래 위치에 남겨두었습니다. Decision의 BossRevealPreparedChunk 프리팹도 B1 자산을 공유합니다.\n"+
            "Authoring~/CharacterAssetMovePlan.json: 상세 이동표와 자산별 참조 목록. Editor/BattleCharacterAssetPaths.cs는 과거 제작 도구의 조합형 경로를 GUID로 해석합니다.\n"+
            "Authoring~/CharacterAssetOrganizationVerification.json: 자산 내용·하위 객체 ID·씬 및 범위 밖 파일 검증 결과\n\n"+
            "플레이어가 참조하지만 이번 범위 밖이라 그대로 둔 원본:\n"+string.Join("\n",external)+"\n";
        File.WriteAllText(Scope+"CharacterAssets_README.txt",note,new System.Text.UTF8Encoding(true));
        int resolved=0;
        foreach(var move in moves)
        {
            if(BattleCharacterAssetPaths.Resolve(move.from)!=move.to || !BattleCharacterAssetPaths.LoadMain(move.from))throw new Exception("Legacy path resolution failed: "+move.from);
            resolved++;
        }
        return new{current=moves.Count,remainingReferenceEntries=assets.Length,legacyPathsResolved=resolved,deleted=0};
    }
    public static object FinalChecks()
    {
        var moves=Read<List<Move>>("CharacterAssetMovePlan.json");
        foreach(var move in moves)
        {
            if(BattleCharacterAssetPaths.Resolve(move.from)!=move.to || !BattleCharacterAssetPaths.LoadMain(move.from))throw new Exception("Legacy path lookup failed: "+move.from);
        }
        var verification=Verify();
        var result=new{legacyPathsResolved=moves.Count,remainingReferenceEntries=File.ReadLines(Scope+"OtherAnimationModelReferences.csv").Count()-1,verification,playing=Application.isPlaying,compiling=EditorApplication.isCompiling,sceneDirty=UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty};
        Write("CharacterAssetFinalChecks.json",result);return result;
    }
}
