using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// All mutations are explicit entry points; inspection never changes scenes or saves.
public static class DecisionCleanup
{
    private static readonly string[] Candidates = {
        "Assets/UnityAssets/Animated Characters Bundle",
        "Assets/Scenes/TempDecisionScene.unity",
        "Assets/Scenes/MainMenuTestGameScene.unity",
        "Assets/_Recovery/0.unity",
        "Assets/AiAsset/JourneyInterfaceV3/Scenes/BoardPreview_Direct3D.unity",
        "Assets/AiAsset/JourneyInterfaceV3/Scenes/InterfacePreview_Direct3D.unity",
        "Assets/AiAsset/JourneyInterfaceV4Baked/Scenes/BoardPreview_Baked.unity",
        "Assets/AiAsset/JourneyInterfaceV4Baked/Scenes/InterfacePreview_Baked.unity",
        "Assets/AiAsset/JourneyMapKit/Scenes/JourneyKit_ApprovedLayout.unity",
        "Assets/AiAsset/JourneyMapKit/Scenes/JourneyKit_ProjectLayout.unity"
    };
    private static HashSet<string> Used()
    {
        var all = AssetDatabase.GetAllAssetPaths();
        var roots = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path)
            .Concat(all.Where(p => p.Contains("/Resources/") || p.StartsWith("Assets/StreamingAssets/")))
            .Concat(PlayerSettings.GetPreloadedAssets().Where(a => a != null).Select(AssetDatabase.GetAssetPath))
            .Concat(Directory.GetFiles("ProjectSettings").Select(p => p.Replace('\\','/'))).Distinct().ToArray();
        return new HashSet<string>(AssetDatabase.GetDependencies(roots, true));
    }
    public static object Inspect()
    {
        var used = Used();
        var all = AssetDatabase.GetAllAssetPaths();
        var owners=all.Where(a=>a.EndsWith(".prefab")||a.EndsWith(".unity")||a.EndsWith(".asset"))
            .ToDictionary(a=>a,a=>AssetDatabase.GetDependencies(a,false));
        return new { playing = EditorApplication.isPlaying, scenes = Enumerable.Range(0,SceneManager.sceneCount)
            .Select(i => SceneManager.GetSceneAt(i)).Select(s => new { s.path, s.isDirty }).ToArray(),
            candidates = Candidates.Select(p => new { path=p, exists=File.Exists(p)||Directory.Exists(p),
                used=used.Where(a => a==p || a.StartsWith(p+"/",StringComparison.Ordinal)).ToArray(),
                externalOwners=owners.Where(a => a.Key!=p && !a.Key.StartsWith(p+"/",StringComparison.Ordinal) &&
                    a.Value.Any(d=>d==p||d.StartsWith(p+"/",StringComparison.Ordinal))).Select(a=>a.Key).ToArray() }).ToArray() };
    }
    public static object Ping() => new { ready=true, playing=EditorApplication.isPlaying, compiling=EditorApplication.isCompiling };
    public static object InspectSvgImporter()
    {
        string path="Assets/UnityAssets/Animated Characters Bundle/Skins/Source/Skins/racerRedFemale.svg";
        if(!File.Exists(path))return new {retired=true,path};
        var importer=AssetImporter.GetAtPath(path);
        var so=new SerializedObject(importer);var it=so.GetIterator();var properties=new List<object>();
        while(it.Next(true)) if(it.propertyType==SerializedPropertyType.Enum)
            properties.Add(new {it.propertyPath,it.enumValueIndex,it.enumNames});
        return new {type=importer.GetType().FullName,properties,
            animalAssets=AssetDatabase.LoadAllAssetsAtPath("Assets/UnityAssets/Animated Characters Bundle/Skins/Animals/Source/animalA.svg").Select(a=>a.GetType().FullName).ToArray()};
    }
    public static object FixSvgImports()
    {
        if(!Directory.Exists("Assets/UnityAssets/Animated Characters Bundle/Skins")) return new {retired=true,fixedImports=0};
        var paths=new List<string>();var errors=new List<string>();
        Application.LogCallback capture=(message,stack,type)=> {
            if(type==LogType.Error||type==LogType.Exception) errors.Add(message);
        };
        Application.logMessageReceived+=capture;
        try
        {
            foreach(string file in Directory.GetFiles("Assets/UnityAssets/Animated Characters Bundle/Skins","*.svg",SearchOption.AllDirectories))
            {
                var xml=new System.Xml.XmlDocument();xml.Load(file);
                if(!xml.SelectNodes("//*[local-name()='linearGradient' or local-name()='radialGradient']")
                    .Cast<System.Xml.XmlNode>().Any(g=>g.SelectNodes("./*[local-name()='stop']").Count>8)) continue;
                string path=file.Replace('\\','/');var importer=AssetImporter.GetAtPath(path);
                var so=new SerializedObject(importer);
                var svg=so.FindProperty("m_SvgType");var tessellation=so.FindProperty("m_TessellationMode");
                int spriteMode=Array.IndexOf(svg.enumNames,"VectorSprite"),triangleMode=Array.IndexOf(tessellation.enumNames,"Triangulation");
                if(spriteMode<0||triangleMode<0) throw new InvalidOperationException("Unknown SVG importer settings");
                if(svg.enumValueIndex!=spriteMode || tessellation.enumValueIndex!=triangleMode)
                {
                    svg.enumValueIndex=spriteMode;tessellation.enumValueIndex=triangleMode;
                    so.ApplyModifiedPropertiesWithoutUndo();importer.SaveAndReimport();
                }
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
                if(!AssetDatabase.LoadAllAssetsAtPath(path).Any(a=>a is Sprite)) throw new InvalidOperationException("SVG sprite import failed: "+path);
                paths.Add(path);
            }
        }
        finally {Application.logMessageReceived-=capture;}
        if(errors.Count>0) throw new InvalidOperationException(string.Join(" / ",errors));
        return new {fixedImports=paths,gradientErrors=errors.Count,originalSvgUnchanged=true};
    }
    public static object ValidateBattle()
    {
        BattleConfigurationValidator.Validate();
        return new { passed=true };
    }
    public static object FinalizeApprovedFields()
    {
        var paths=AssetDatabase.FindAssets("t:PromptData").Select(AssetDatabase.GUIDToAssetPath).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
        string[] retained={"neutralStatValue","moderateChangeMinStat","strongChangeMinStat"};
        var values=new List<object>();
        foreach(string path in paths)
        {
            var asset=AssetDatabase.LoadAssetAtPath<PromptData>(path);var so=new SerializedObject(asset);
            string backup="Documentation/CleanupRecovery/"+path.Replace('/','_').Replace('\\','_')+".txt";
            if(!File.Exists(backup)) throw new IOException("Missing approved field recovery copy: "+path);
            string text=File.ReadAllText(backup);
            foreach(string field in retained)
            {
                var match=System.Text.RegularExpressions.Regex.Match(text,@"(?m)^\s*"+field+@":\s*(-?\d+)");
                if(!match.Success || so.FindProperty(field).intValue!=int.Parse(match.Groups[1].Value))
                    throw new InvalidOperationException("Retained influence value changed: "+path+" / "+field);
            }
            values.Add(new {path,asset.neutralStatValue,asset.moderateChangeMinStat,asset.strongChangeMinStat});
        }
        AssetDatabase.ForceReserializeAssets(paths,ForceReserializeAssetsOptions.ReserializeAssets);
        const string planFile="Documentation/DecisionCleanupDeletionPlan.json";
        var plan=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(planFile));
        var retired=((Newtonsoft.Json.Linq.JArray)plan["obsoletePromptDataFields"]).Values<string>().ToArray();
        foreach(string path in paths)
            if(retired.Any(field=>System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(path),@"(?m)^\s*"+field+":")))
                throw new InvalidOperationException("Retired prompt data remains in "+path);
        plan["promptDataFieldsRemoved"]=true;plan["reserializedPromptAssets"]=Newtonsoft.Json.Linq.JArray.FromObject(paths);
        File.WriteAllText(planFile,plan.ToString(Newtonsoft.Json.Formatting.Indented));
        return new {removedFields=retired.Length,retainedValues=values};
    }
    public static object ValidateCleanScenes()
    {
        var rows=new List<object>();
        foreach(string path in EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path))
        {
            var scene=EditorSceneManager.OpenPreviewScene(path);
            try
            {
                var transforms=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).ToArray();
                int missingScripts=transforms.Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
                int missingPrefabs=transforms.Count(t=>PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)&&PrefabUtility.GetPrefabInstanceStatus(t.gameObject)==PrefabInstanceStatus.MissingAsset);
                int retiredClients=transforms.SelectMany(t=>t.GetComponents<MonoBehaviour>()).Count(b=>b!=null&&b.GetType().Name=="AIAPIClient");
                if(missingScripts+missingPrefabs+retiredClients>0) throw new InvalidOperationException("Broken scene references after cleanup: "+path+" / "+missingScripts+" scripts / "+missingPrefabs+" prefabs / "+retiredClients+" retired clients");
                rows.Add(new {path,missingScripts,missingPrefabs,retiredClients});
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
        }
        return new {passed=true,scenes=rows};
    }
    public static object InspectBrokenDecision()
    {
        var scene=EditorSceneManager.OpenPreviewScene("Assets/Scenes/DecisionScene.unity");
        try
        {
            return scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Where(t=>
                GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0 ||
                (PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)&&PrefabUtility.GetPrefabInstanceStatus(t.gameObject)==PrefabInstanceStatus.MissingAsset))
                .Select(t=>new {t.name,parent=t.parent==null?null:t.parent.name,missingScripts=GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject),
                    prefabStatus=PrefabUtility.GetPrefabInstanceStatus(t.gameObject).ToString(),source=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject),id=GlobalObjectId.GetGlobalObjectIdSlow(t.gameObject).ToString()}).ToArray();
        }
        finally{EditorSceneManager.ClosePreviewScene(scene);}
    }
    public static object RemoveOrphanFace()
    {
        const string path="Assets/Scenes/DecisionScene.unity";
        if(EditorApplication.isPlaying || Enumerable.Range(0,SceneManager.sceneCount).Any(i=>SceneManager.GetSceneAt(i).isDirty))
            throw new InvalidOperationException("Editor must be stopped with no unsaved scenes.");
        var scene=SceneManager.GetSceneByPath(path);bool opened=!scene.IsValid()||!scene.isLoaded;
        if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
        int removed=0;
        try
        {
            var candidates=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true))
                .Where(t=>t.name=="AangFace" && t.parent!=null && t.parent.name=="Face_Display" &&
                    GlobalObjectId.GetGlobalObjectIdSlow(t.gameObject).ToString()=="GlobalObjectId_V1-2-1b43567c20479cb44b21edc2432a6d18-2131054597-0").ToArray();
            if(candidates.Length>1) throw new InvalidOperationException("Ambiguous orphan face.");
            foreach(var t in candidates)
            {
                if(PrefabUtility.GetPrefabInstanceStatus(t.gameObject)!=PrefabInstanceStatus.MissingAsset ||
                    GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)!=1)
                    throw new InvalidOperationException("Face is no longer the inspected missing prefab.");
                UnityEngine.Object.DestroyImmediate(t.gameObject);removed++;
            }
            if(removed>0&&!EditorSceneManager.SaveScene(scene))throw new IOException("Could not save orphan cleanup.");
        }
        finally{if(opened)EditorSceneManager.CloseScene(scene,true);}
        return new {removedOrphanFaces=removed,holderPreserved=true};
    }
    public static object ApplyApprovedPlan()
    {
        const string planFile="Documentation/DecisionCleanupDeletionPlan.json";
        var plan=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(planFile));
        var targets=((Newtonsoft.Json.Linq.JArray)plan["assets"]).Select(r=>(string)r["path"]).ToArray();
        if(targets.Length!=101 || targets.Distinct(StringComparer.Ordinal).Count()!=101)
            throw new InvalidOperationException("The approved deletion list changed.");
        string project=Path.GetFullPath(Path.Combine(Application.dataPath,".."));
        string assetsPrefix=Path.GetFullPath(Application.dataPath)+Path.DirectorySeparatorChar;
        foreach(string path in targets)
        {
            string full=Path.GetFullPath(path);
            if(!full.StartsWith(assetsPrefix,StringComparison.OrdinalIgnoreCase) || path.Contains(".."))
                throw new IOException("Deletion target escaped Assets: "+path);
            if(File.Exists(full)||Directory.Exists(full))
            {
                for(string current=full;current!=null && current.StartsWith(project,StringComparison.OrdinalIgnoreCase);current=Path.GetDirectoryName(current))
                    if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0) throw new IOException("Linked deletion target: "+path);
                if(Directory.Exists(full) && Directory.GetFileSystemEntries(full,"*",SearchOption.AllDirectories)
                    .Any(p=>(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)) throw new IOException("Linked child: "+path);
                int count=File.Exists(full)?1:Directory.GetFiles(full,"*",SearchOption.AllDirectories).Count(f=>!f.EndsWith(".meta",StringComparison.Ordinal));
                int approved=(int)((Newtonsoft.Json.Linq.JArray)plan["assets"]).Single(r=>(string)r["path"]==path)["files"];
                if(count!=approved) throw new InvalidOperationException("Target contents changed: "+path);
            }
        }
        if(EditorApplication.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("Editor must be idle.");
        if(Enumerable.Range(0,SceneManager.sceneCount).Any(i=>SceneManager.GetSceneAt(i).isDirty))
            throw new InvalidOperationException("An open scene has unsaved changes.");
        var used=Used();
        var owners=AssetDatabase.GetAllAssetPaths().Where(a=>a.EndsWith(".prefab")||a.EndsWith(".unity")||a.EndsWith(".asset"))
            .ToDictionary(a=>a,a=>AssetDatabase.GetDependencies(a,false));
        Func<string,bool> retiring=p=>targets.Any(t=>p==t||p.StartsWith(t+"/",StringComparison.Ordinal));
        foreach(string target in targets)
        {
            if(used.Any(p=>p==target||p.StartsWith(target+"/",StringComparison.Ordinal))) throw new InvalidOperationException("Target is now used: "+target);
            if(owners.Any(a=>!retiring(a.Key)&&a.Value.Any(d=>d==target||d.StartsWith(target+"/",StringComparison.Ordinal))))
                throw new InvalidOperationException("Retained asset now references: "+target);
        }
        const string scenePath="Assets/Scenes/DecisionScene.unity";
        Directory.CreateDirectory("Documentation/CleanupRecovery");
        string backup="Documentation/CleanupRecovery/DecisionScene.before-approved-cleanup.unity.txt";
        if(!File.Exists(backup)) File.Copy(scenePath,backup);
        var scene=SceneManager.GetSceneByPath(scenePath);bool opened=!scene.IsValid()||!scene.isLoaded;
        if(opened)scene=EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Additive);
        int components=0,containers=0;
        try
        {
            var clients=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<MonoBehaviour>(true))
                .Where(b=>b!=null&&b.GetType().Name=="AIAPIClient").ToArray();
            foreach(var client in clients)
            {
                var go=client.gameObject;
                bool emptyContainer=go.name=="TempAiM"&&go.transform.parent==null&&go.transform.childCount==0&&go.GetComponents<Component>().Length==2;
                UnityEngine.Object.DestroyImmediate(client);components++;
                if(emptyContainer){UnityEngine.Object.DestroyImmediate(go);containers++;}
            }
            if(components>0&&!EditorSceneManager.SaveScene(scene))throw new IOException("Failed to save cleaned Decision scene.");
        }
        finally{if(opened)EditorSceneManager.CloseScene(scene,true);}
        var scripts=new[]{"Assets/Decision_YYS/Scripts/Ai/AIAPIClient.cs","Assets/Decision_YYS/Scripts/TempDualMiniGame.cs"};
        used=Used();
        foreach(string script in scripts)
            if(used.Contains(script)||AssetDatabase.GetAllAssetPaths().Where(p=>p.EndsWith(".unity")||p.EndsWith(".prefab")||p.EndsWith(".asset"))
                .Any(p=>AssetDatabase.GetDependencies(p,false).Contains(script))) throw new InvalidOperationException("Retired script still referenced: "+script);
        var removed=new List<string>();long bytes=0;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach(string path in targets.Concat(scripts))
            {
                if(!File.Exists(path)&&!Directory.Exists(path))continue;
                bytes+=File.Exists(path)?new FileInfo(path).Length:Directory.GetFiles(path,"*",SearchOption.AllDirectories).Sum(f=>new FileInfo(f).Length);
                if(!AssetDatabase.DeleteAsset(path))throw new IOException("Failed to delete approved target: "+path);
                removed.Add(path);
            }
        }
        finally{AssetDatabase.StopAssetEditing();AssetDatabase.Refresh();}
        plan["status"]="Approved by user and applied";
        plan["removedTargets"]=Newtonsoft.Json.Linq.JArray.FromObject(removed);
        plan["removedBytes"]=bytes;plan["retiredSceneComponents"]=components;plan["retiredEmptyContainers"]=containers;
        plan["sceneBackup"]=backup;
        File.WriteAllText(planFile,plan.ToString(Newtonsoft.Json.Formatting.Indented));
        return new {removedTargets=removed.Count,bytes,components,containers,backup};
    }
    public static object WriteDeletionPlan()
    {
        var used=Used();var all=AssetDatabase.GetAllAssetPaths();
        var owners=all.Where(a=>a.EndsWith(".prefab")||a.EndsWith(".unity")||a.EndsWith(".asset"))
            .ToDictionary(a=>a,a=>AssetDatabase.GetDependencies(a,false));
        var targets=new HashSet<string>(Candidates.Where(p=>File.Exists(p)||Directory.Exists(p)));
        foreach(string p in targets)
            if(used.Any(d=>d==p||d.StartsWith(p+"/",StringComparison.Ordinal)) || owners.Any(a=>a.Key!=p &&
                !targets.Any(t=>a.Key==t||a.Key.StartsWith(t+"/",StringComparison.Ordinal)) &&
                a.Value.Any(d=>d==p||d.StartsWith(p+"/",StringComparison.Ordinal))))
                throw new InvalidOperationException("Candidate acquired a reference: "+p);
        var prefixes=new[]{"Assets/AiAsset/JourneyInterfaceV3/","Assets/AiAsset/JourneyInterfaceV4Baked/","Assets/AiAsset/JourneyMapKit/"};
        var extensions=new HashSet<string>(new[]{".asset",".prefab",".mat",".png",".exr",".renderTexture"});
        var generated=new HashSet<string>(all.Where(p=>prefixes.Any(prefix=>p.StartsWith(prefix,StringComparison.Ordinal)) && File.Exists(p) && !used.Contains(p) && extensions.Contains(Path.GetExtension(p))));
        var sourceFiles=Directory.GetFiles("AgentScripts","*",SearchOption.AllDirectories)
            .Concat(Directory.GetFiles("Assets","*.cs",SearchOption.AllDirectories))
            .Where(p=>new[]{".cs",".ps1",".mjs",".py"}.Contains(Path.GetExtension(p))).Select(File.ReadAllText).ToArray();
        generated.RemoveWhere(p=>sourceFiles.Any(s=>s.Contains(p)||s.Contains("\""+Path.GetFileName(p)+"\"")));
        bool changed;
        do
        {
            var retained=generated.Where(p=>owners.Any(a=>!generated.Contains(a.Key) &&
                !targets.Any(t=>a.Key==t||a.Key.StartsWith(t+"/",StringComparison.Ordinal)) && a.Value.Contains(p))).ToArray();
            changed=retained.Length>0;foreach(string p in retained) generated.Remove(p);
        } while(changed);
        targets.UnionWith(generated);
        var rows=new Newtonsoft.Json.Linq.JArray(targets.OrderBy(p=>p,StringComparer.Ordinal).Select(p=>new Newtonsoft.Json.Linq.JObject {
            ["path"]=p,["bytes"]=File.Exists(p)?new FileInfo(p).Length:Directory.GetFiles(p,"*",SearchOption.AllDirectories).Sum(f=>new FileInfo(f).Length),
            ["files"]=File.Exists(p)?1:Directory.GetFiles(p,"*",SearchOption.AllDirectories).Count(f=>!f.EndsWith(".meta")),
            ["knownRootReference"]=false,["externalRetainedAssetReference"]=false }));
        var doc=new Newtonsoft.Json.Linq.JObject { ["status"]="Awaiting explicit deletion approval; no assets deleted",
            ["branch"]="feature/ai-training",["recovery"]="Tracked Assets can be restored from Git. Editor authoring previews will be removed.",
            ["assets"]=rows,["additionalSceneChange"]="Remove the retired AIAPIClient component and its empty container from DecisionScene, then delete AIAPIClient.cs and unreferenced TempDualMiniGame.cs.",
            ["obsoletePromptDataFields"]=new Newtonsoft.Json.Linq.JArray("subtleChangeInstruction","moderateChangeInstruction","strongChangeInstruction","episodeEndTemplate","midTransitionTemplate","chapterEndTemplate","lowStatAtmosphere","midStatAtmosphere","highStatAtmosphere","responseFormatTemplateV2"),
            ["limits"]="Known roots: enabled build scenes, Resources, StreamingAssets, preloaded assets and ProjectSettings. Authoring tools using constructed paths may need these assets again." };
        const string output="Documentation/DecisionCleanupDeletionPlan.json";
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output,doc.ToString(Newtonsoft.Json.Formatting.Indented));
        return new {output,assetTargets=rows.Count,files=rows.Sum(r=>(int)r["files"]),bytes=rows.Sum(r=>(long)r["bytes"])};
    }
    public static object BattleSnapshot()
    {
        var scene=EditorSceneManager.OpenPreviewScene("Assets/Scenes/BattleScene.unity");
        try
        {
            var c=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<BattleSceneController>(true)).Single();
            return c.stages.Select(s=>new{s.stageIndex,s.recoverySeconds,s.forwardViewportY,s.slashTargetCount,s.slashStartZ,s.slashSpeed,
                path=AssetDatabase.GetAssetPath(s),steps=s.flow.steps.Select(t=>new {kind=t.kind.ToString(),pattern=t.pattern==null?null:new {
                    kind=t.pattern.kind.ToString(),t.pattern.duration,t.pattern.legacyCase}}).ToArray()}).ToArray();
        }
        finally{EditorSceneManager.ClosePreviewScene(scene);}
    }
}
