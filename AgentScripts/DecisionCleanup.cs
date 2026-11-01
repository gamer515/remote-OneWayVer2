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
        var importer=AssetImporter.GetAtPath(path);
        var so=new SerializedObject(importer);var it=so.GetIterator();var properties=new List<object>();
        while(it.Next(true)) if(it.propertyType==SerializedPropertyType.Enum)
            properties.Add(new {it.propertyPath,it.enumValueIndex,it.enumNames});
        return new {type=importer.GetType().FullName,properties,
            animalAssets=AssetDatabase.LoadAllAssetsAtPath("Assets/UnityAssets/Animated Characters Bundle/Skins/Animals/Source/animalA.svg").Select(a=>a.GetType().FullName).ToArray()};
    }
    public static object FixSvgImports()
    {
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
