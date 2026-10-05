using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using JourneyMapKit;
using Newtonsoft.Json.Linq;

// Editor-only Pipeline helper. Keep the existing runtime hierarchy and all gameplay references.
public static class JourneyV4BakedMigration
{
    const string ScenePath="Assets/Scenes/DecisionScene.unity";
    const string Source="Assets/AiAsset/JourneyInterfaceV4Baked/";
    const string BoardPath="_Gameplay3D/3D_UI/JourneyInterfaceV3_Runtime";
    const string Generated="Assets/Decision_YYS/JourneyV4Integration";
    static readonly string[] Colors={"Blue","Red","Yellow","Teal"};
    static T Asset<T>(string path) where T:UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new Exception("Missing asset: "+path);
    static Transform Find(Scene scene,string path)
    {
        var p=path.Split('/');
        var root=scene.GetRootGameObjects().Single(o=>o.name==p[0]).transform;
        return p.Length==1?root:root.Find(string.Join("/",p.Skip(1))) ?? throw new Exception(path);
    }
    static string Relative(Transform child,Transform root) => child==root?"":(child.parent==root?child.name:Relative(child.parent,root)+"/"+child.name);
    static object MeshInfo(MeshFilter m,Transform root) => new {path=Relative(m.transform,root),mesh=AssetDatabase.GetAssetPath(m.sharedMesh),vertices=m.sharedMesh.vertexCount,submeshes=m.sharedMesh.subMeshCount,
        uv2=m.sharedMesh.uv2.Length,position=m.transform.localPosition.ToString("F5"),scale=m.transform.localScale.ToString("F5"),materials=m.GetComponent<Renderer>().sharedMaterials.Select(a=>new{a.name,shader=a.shader.name,supported=a.shader.isSupported}).ToArray()};
    public static object Inspect()
    {
        var loaded=SceneManager.GetSceneByPath(ScenePath); bool preview=!loaded.IsValid()||!loaded.isLoaded;
        var scene=preview?EditorSceneManager.OpenPreviewScene(ScenePath):loaded;
        try
        {
            var board=Find(scene,BoardPath);
            var source=Asset<GameObject>(Source+"Prefabs/JourneyBoardAssembly_Baked.prefab").transform;
            var manager=Find(scene,"_Systems/Decision_Manager");
            return new {
                scenes=Enumerable.Range(0,SceneManager.sceneCount).Select(i=>new{SceneManager.GetSceneAt(i).path,SceneManager.GetSceneAt(i).isDirty}).ToArray(),
                pipeline=AssetDatabase.GetAssetPath(GraphicsSettings.currentRenderPipeline),
                old=board.GetComponentsInChildren<MeshFilter>(true).Where(m=>!m.name.StartsWith("StoredCoin")).Select(m=>MeshInfo(m,board)).ToArray(),
                baked=source.GetComponentsInChildren<MeshFilter>(true).Select(m=>MeshInfo(m,source)).ToArray(),
                stock=board.GetComponentsInChildren<JourneyCoinStack>(true).Select(s=>new{s.name,count=s.Count,coins=s.coins.Select(c=>new{c.name,pos=c.transform.localPosition.ToString("F5"),c.activeSelf}).ToArray()}).ToArray(),
                manager=manager.name,boardPose=board.position.ToString("F5")+board.localScale.ToString("F5")
            };
        }
        finally {if(preview)EditorSceneManager.ClosePreviewScene(scene);}
    }
    sealed class Swap
    {
        public MeshFilter target;
        public Mesh mesh;
        public Material[] materials;
        public bool generated;
        public int triangles;
    }
    static string Point(Vector3 p) => Mathf.RoundToInt(p.x*10000)+","+Mathf.RoundToInt(p.y*10000)+","+Mathf.RoundToInt(p.z*10000);
    static string Triangle(Vector3 a,Vector3 b,Vector3 c)
    {
        var values=new[]{Point(a),Point(b),Point(c)}; Array.Sort(values,StringComparer.Ordinal); return string.Join(";",values);
    }
    static Matrix4x4 Between(Transform from,Transform to)
    {
        if(from.parent!=to.parent)throw new Exception("Expected stock/visual siblings");
        return Matrix4x4.TRS(to.localPosition,to.localRotation,to.localScale).inverse*Matrix4x4.TRS(from.localPosition,from.localRotation,from.localScale);
    }
    static void Classify(Mesh mesh,Matrix4x4 matrix,int owner,Dictionary<string,int> owners)
    {
        var vertices=mesh.vertices.Select(v=>matrix.MultiplyPoint3x4(v)).ToArray();
        var indices=mesh.triangles;
        for(int i=0;i<indices.Length;i+=3)
        {
            string key=Triangle(vertices[indices[i]],vertices[indices[i+1]],vertices[indices[i+2]]);
            if(owners.TryGetValue(key,out var previous)&&previous!=owner)throw new Exception("Ambiguous stock geometry");
            owners[key]=owner;
        }
    }
    static Mesh Subset(Mesh source,List<int>[] triangles,Matrix4x4 matrix,string name)
    {
        var originals=triangles.SelectMany(t=>t).Distinct().ToArray();
        if(originals.Length==0)throw new Exception("Empty baked subset: "+name);
        var remap=originals.Select((old,index)=>new{old,index}).ToDictionary(p=>p.old,p=>p.index);
        var mesh=new Mesh {name=name,indexFormat=originals.Length>65535?IndexFormat.UInt32:IndexFormat.UInt16};
        var vertices=source.vertices; mesh.vertices=originals.Select(i=>matrix.MultiplyPoint3x4(vertices[i])).ToArray();
        var normals=source.normals;
        if(normals.Length==vertices.Length) mesh.normals=originals.Select(i=>matrix.inverse.transpose.MultiplyVector(normals[i]).normalized).ToArray();
        var tangents=source.tangents;
        if(tangents.Length==vertices.Length) mesh.tangents=originals.Select(i=>{var t=tangents[i];var v=matrix.MultiplyVector(new Vector3(t.x,t.y,t.z)).normalized;return new Vector4(v.x,v.y,v.z,t.w);}).ToArray();
        var colors=source.colors32;
        if(colors.Length==vertices.Length)mesh.colors32=originals.Select(i=>colors[i]).ToArray();
        for(int channel=0;channel<8;channel++)
        {
            var uvs=new List<Vector4>();source.GetUVs(channel,uvs);
            if(uvs.Count==vertices.Length)mesh.SetUVs(channel,originals.Select(i=>uvs[i]).ToList());
        }
        mesh.subMeshCount=source.subMeshCount;
        for(int i=0;i<triangles.Length;i++)mesh.SetTriangles(triangles[i].Select(t=>remap[t]).ToArray(),i);
        mesh.RecalculateBounds(); return mesh;
    }
    static List<Swap> Prepare(Transform board)
    {
        var source=Asset<GameObject>(Source+"Prefabs/JourneyBoardAssembly_Baked.prefab").transform;
        var swaps=new List<Swap>();
        try
        {
            foreach(var baked in source.GetComponentsInChildren<MeshFilter>(true))
            {
                var path=Relative(baked.transform,source); var target=board.Find(path)?.GetComponent<MeshFilter>();
                if(target==null)throw new Exception("Missing existing visual: "+path);
                var materials=baked.GetComponent<MeshRenderer>().sharedMaterials;
                if(materials.Any(m=>m==null||!m.shader.isSupported)||baked.sharedMesh.uv2.Length!=baked.sharedMesh.vertexCount)
                    throw new Exception("Invalid baked materials/UV2: "+path);
                bool canister=path.StartsWith("CoinSupplyRack/Canister_",StringComparison.Ordinal)&&path.Split('/').Length==3;
                if(!canister)
                {
                    if(Vector3.Distance(target.sharedMesh.bounds.center,baked.sharedMesh.bounds.center)>.0001f||Vector3.Distance(target.sharedMesh.bounds.size,baked.sharedMesh.bounds.size)>.0001f)
                        throw new Exception("Visual geometry differs: "+path);
                    swaps.Add(new Swap{target=target,mesh=baked.sharedMesh,materials=materials}); continue;
                }
                var stack=target.transform.parent.GetComponent<JourneyCoinStack>();
                if(stack==null||stack.coins.Length!=14)throw new Exception("Stock references missing: "+path);
                var owners=new Dictionary<string,int>();
                Classify(target.sharedMesh,Matrix4x4.identity,0,owners);
                for(int i=0;i<stack.coins.Length;i++)
                {
                    var coin=stack.coins[i].transform;
                    Classify(coin.GetComponent<MeshFilter>().sharedMesh,Between(coin,target.transform),i+1,owners);
                }
                var mesh=baked.sharedMesh; var vertices=mesh.vertices;
                var pieces=Enumerable.Range(0,15).Select(_=>Enumerable.Range(0,mesh.subMeshCount).Select(__=>new List<int>()).ToArray()).ToArray();
                for(int sub=0;sub<mesh.subMeshCount;sub++)
                {
                    var indices=mesh.GetTriangles(sub);
                    for(int i=0;i<indices.Length;i+=3)
                    {
                        string key=Triangle(vertices[indices[i]],vertices[indices[i+1]],vertices[indices[i+2]]);
                        if(!owners.TryGetValue(key,out int owner))throw new Exception("Unmatched baked triangle in "+path+": "+key);
                        pieces[owner][sub].AddRange(new[]{indices[i],indices[i+1],indices[i+2]});
                    }
                }
                string color=stack.name.Substring("Canister_".Length);
                for(int i=0;i<15;i++)
                {
                    var dest=i==0?target:stack.coins[i-1].GetComponent<MeshFilter>();
                    int count=pieces[i].Sum(t=>t.Count);
                    if(count!=dest.sharedMesh.triangles.Length)throw new Exception("Stock triangle count mismatch: "+color+" part "+i);
                    var part=Subset(mesh,pieces[i],i==0?Matrix4x4.identity:Between(target.transform,dest.transform),color+"_"+(i==0?"Housing":dest.name));
                    if(Vector3.Distance(part.bounds.center,dest.sharedMesh.bounds.center)>.00015f||Vector3.Distance(part.bounds.size,dest.sharedMesh.bounds.size)>.00015f)
                        throw new Exception("Stock placement changed: "+dest.name);
                    swaps.Add(new Swap{target=dest,mesh=part,materials=materials,generated=true,triangles=count/3});
                }
            }
            if(swaps.Count!=board.GetComponentsInChildren<MeshFilter>(true).Length)throw new Exception("Some board visuals were not covered");
            return swaps;
        }
        catch {foreach(var swap in swaps.Where(s=>s.generated))UnityEngine.Object.DestroyImmediate(swap.mesh);throw;}
    }
    static Scene Decision()
    {
        var s=SceneManager.GetSceneByPath(ScenePath);
        return s.IsValid()&&s.isLoaded?s:EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
    }
    public static object PrepareCheck()
    {
        var s=Decision();var swaps=Prepare(Find(s,BoardPath));
        try {return new{total=swaps.Count,direct=swaps.Count(p=>!p.generated),stockMeshes=swaps.Count(p=>p.generated),pieces=swaps.Where(p=>p.generated).Select(p=>new{p.mesh.name,p.triangles,vertices=p.mesh.vertexCount}).ToArray()};}
        finally{foreach(var swap in swaps.Where(p=>p.generated))UnityEngine.Object.DestroyImmediate(swap.mesh);}
    }
    static bool CoinPhysicsEqual(GameObject a,GameObject b)
    {
        if(a.GetComponentsInChildren<Transform>(true).Length!=b.GetComponentsInChildren<Transform>(true).Length)return false;
        foreach(var original in a.GetComponentsInChildren<Transform>(true))
        {
            var path=Relative(original,a.transform);var next=path==""?b.transform:b.transform.Find(path);
            if(next==null||original.localPosition!=next.localPosition||original.localRotation!=next.localRotation||original.localScale!=next.localScale)return false;
            foreach(var c in original.GetComponents<Component>().Where(c=>c is Collider||c is Rigidbody||c is MonoBehaviour))
            {
                var n=next.GetComponent(c.GetType());
                if(n==null||EditorJsonUtility.ToJson(c)!=EditorJsonUtility.ToJson(n))return false;
            }
        }
        return true;
    }
    public static object CoinCheck()
    {
        return Colors.Select(c=>new{color=c,equal=CoinPhysicsEqual(Asset<GameObject>("Assets/AiAsset/JourneyInterfaceV3/Prefabs/JourneyCoin_"+c+".prefab"),Asset<GameObject>(Source+"Prefabs/JourneyCoin_"+c+"_Baked.prefab"))}).ToArray();
    }
    static string PreserveJson(Component component)
    {
        var json=EditorJsonUtility.ToJson(component);
        if(!(component is DecisionManager))return json;
        var data=JObject.Parse(json);
        foreach(var property in data.Descendants().OfType<JProperty>().Where(p=>p.Name=="journeyCoinPrefabs").ToArray())property.Remove();
        return data.ToString(Newtonsoft.Json.Formatting.None);
    }
    static Dictionary<Component,string> Preserved(Scene scene)
    {
        return scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Component>(true))
            .Where(c=>c!=null&&!(c is MeshFilter)&&!(c is MeshRenderer)).ToDictionary(c=>c,PreserveJson);
    }
    public static object Migrate()
    {
        if(EditorApplication.isPlaying)throw new Exception("Stop Play first");
        if(AssetDatabase.IsValidFolder(Generated))throw new Exception("V4 generated folder exists; refusing overwrite");
        var scene=Decision();var board=Find(scene,BoardPath);
        var preserved=Preserved(scene);
        var swaps=Prepare(board);
        var manager=Find(scene,"_Systems/Decision_Manager").GetComponent<DecisionManager>();
        var managerObject=new SerializedObject(manager);var coins=managerObject.FindProperty("journeyCoinPrefabs");
        if(coins.arraySize!=4)throw new Exception("Unexpected supply coin mapping");
        var newCoins=Colors.Select(c=>Asset<GameObject>(Source+"Prefabs/JourneyCoin_"+c+"_Baked.prefab")).ToArray();
        for(int i=0;i<4;i++)if(!CoinPhysicsEqual((GameObject)coins.GetArrayElementAtIndex(i).objectReferenceValue,newCoins[i]))
            throw new Exception("Coin gameplay/physics differs: "+Colors[i]);
        var pipeline=GraphicsSettings.defaultRenderPipeline;var qualityPipeline=QualitySettings.renderPipeline;
        Directory.CreateDirectory("Temp/JourneyV4Migration");
        string backup="Temp/JourneyV4Migration/DecisionBefore-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".unity";
        if(!EditorSceneManager.SaveScene(scene,backup,true))throw new Exception("Backup failed");
        Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Use V4 baked visuals; preserve gameplay setup");
        bool created=false;
        try
        {
            if(string.IsNullOrEmpty(AssetDatabase.CreateFolder("Assets/Decision_YYS","JourneyV4Integration")))throw new Exception("Folder creation failed");created=true;
            foreach(var swap in swaps)
            {
                if(swap.generated)AssetDatabase.CreateAsset(swap.mesh,Generated+"/"+swap.mesh.name+".asset");
                var renderer=swap.target.GetComponent<MeshRenderer>();
                Undo.RecordObject(swap.target,"V4 baked mesh");Undo.RecordObject(renderer,"V4 baked materials");
                swap.target.sharedMesh=swap.mesh;renderer.sharedMaterials=swap.materials;
                PrefabUtility.RecordPrefabInstancePropertyModifications(swap.target);PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            Undo.RecordObject(manager,"V4 coin visual assets");
            for(int i=0;i<4;i++)coins.GetArrayElementAtIndex(i).objectReferenceValue=newCoins[i];
            managerObject.ApplyModifiedProperties();PrefabUtility.RecordPrefabInstancePropertyModifications(manager);
            foreach(var p in preserved)if(p.Key==null||PreserveJson(p.Key)!=p.Value)throw new Exception("Existing setup changed: "+p.Key?.name+" / "+p.Key?.GetType().Name);
            if(GraphicsSettings.defaultRenderPipeline!=pipeline||QualitySettings.renderPipeline!=qualityPipeline)throw new Exception("Render pipeline changed");
            foreach(var swap in swaps)if(swap.target.sharedMesh.uv2.Length!=swap.target.sharedMesh.vertexCount)throw new Exception("Baked UV2 lost");
            foreach(var swap in swaps.Where(s=>s.generated))AssetDatabase.SaveAssetIfDirty(swap.mesh);
            EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene))throw new Exception("Scene save failed");
            Undo.CollapseUndoOperations(group);
            return new{backup,visuals=swaps.Count,generatedMeshes=swaps.Count(s=>s.generated),preservedComponents=preserved.Count,board=board.name,
                pipeline=AssetDatabase.GetAssetPath(pipeline),coinPrefabs=newCoins.Select(AssetDatabase.GetAssetPath).ToArray(),settingsUnchanged=true};
        }
        catch
        {
            Undo.RevertAllDownToGroup(group);
            if(created)AssetDatabase.DeleteAsset(Generated);
            foreach(var swap in swaps.Where(s=>s.generated&&s.mesh!=null&&!EditorUtility.IsPersistent(s.mesh)))UnityEngine.Object.DestroyImmediate(swap.mesh);
            throw;
        }
        finally{managerObject.Dispose();}
    }
    public static object Validate()
    {
        var scene=Decision();var board=Find(scene,BoardPath);
        var input=Find(scene,"_Gameplay3D/3D_UI/JourneyController").GetComponent<JourneyBoardInput>();
        var refs=input.board;var camera=Find(scene,"_World/Main Camera").GetComponent<Camera>();
        Physics.SyncTransforms();
        var filters=board.GetComponentsInChildren<MeshFilter>(true);
        var inventory=new SerializedObject(Find(scene,"_Gameplay3D/3D_UI/FantasyBackpack").GetComponent<BackpackInventoryController>());
        var manager=new SerializedObject(Find(scene,"_Systems/Decision_Manager").GetComponent<DecisionManager>());
        return new {
            scene=scene.path,scene.isDirty,
            storyManagerActive=Find(scene,"_Systems/Decision_Manager").gameObject.activeSelf,
            visuals=filters.Length,allBaked=filters.All(f=>f.sharedMesh.uv2.Length==f.sharedMesh.vertexCount&&f.GetComponent<Renderer>().sharedMaterials.All(m=>AssetDatabase.GetAssetPath(m).StartsWith(Source,StringComparison.Ordinal)&&m.shader.isSupported)),
            controlHits=new[]{refs.yellowButton,refs.gearHandleCollider}.Concat(refs.supplyButtons).Select(c=>new{c.name,hit=Physics.RaycastAll(camera.ViewportPointToRay(camera.WorldToViewportPoint(c.bounds.center)),1000,camera.cullingMask).Any(h=>h.collider==c)}).ToArray(),
            stockCounts=refs.supplyStacks.Select(s=>s.Count).ToArray(),stockVisible=refs.supplyStacks.Select(s=>s.coins.Count(c=>c.activeSelf)).ToArray(),
            callbackYellow=input.onYellowPressed.GetPersistentEventCount(),callbackGear=input.onGearSelected.GetPersistentEventCount(),
            bagClips=AnimationUtility.GetAnimationClips(((Animation)inventory.FindProperty("backpackAnimation").objectReferenceValue).gameObject).Select(AssetDatabase.GetAssetPath).ToArray(),
            encounterStopDistance=manager.FindProperty("encounterStopDistance").floatValue,
            uiDepth=Find(scene,"_UI/Decision_Canvas").GetComponent<Canvas>().planeDistance,
            eventSystems=scene.GetRootGameObjects().Sum(g=>g.GetComponentsInChildren<UnityEngine.EventSystems.EventSystem>(true).Length),
            missingScripts=scene.GetRootGameObjects().Sum(g=>g.GetComponentsInChildren<Transform>(true).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)))
        };
    }
    public static object SaveAndVerify()
    {
        if(EditorApplication.isPlaying)throw new Exception("Stop Play first");
        var scene=Decision();if(!EditorSceneManager.SaveScene(scene))throw new Exception("Save failed");
        var preview=EditorSceneManager.OpenPreviewScene(ScenePath);
        try
        {
            var live=Find(scene,BoardPath);var saved=Find(preview,BoardPath);
            foreach(var filter in live.GetComponentsInChildren<MeshFilter>(true))
            {
                var restored=saved.Find(Relative(filter.transform,live)).GetComponent<MeshFilter>();
                if(restored.sharedMesh!=filter.sharedMesh||!restored.GetComponent<Renderer>().sharedMaterials.SequenceEqual(filter.GetComponent<Renderer>().sharedMaterials))
                    throw new Exception("Saved visual differs: "+filter.name);
            }
            var liveManager=new SerializedObject(Find(scene,"_Systems/Decision_Manager").GetComponent<DecisionManager>());
            var savedManager=new SerializedObject(Find(preview,"_Systems/Decision_Manager").GetComponent<DecisionManager>());
            var a=liveManager.FindProperty("journeyCoinPrefabs");var b=savedManager.FindProperty("journeyCoinPrefabs");
            for(int i=0;i<4;i++)if(a.GetArrayElementAtIndex(i).objectReferenceValue!=b.GetArrayElementAtIndex(i).objectReferenceValue)throw new Exception("Saved coin asset differs");
            return new{saved=true,visuals=72,coins=4,scene.path,scene.isDirty};
        }
        finally{EditorSceneManager.ClosePreviewScene(preview);}
    }
}
