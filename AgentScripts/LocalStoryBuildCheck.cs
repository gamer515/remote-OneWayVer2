using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public static class LocalStoryBuildCheck
{
    public static object Inspect()
    {
        string root=Path.Combine(Application.streamingAssetsPath,"LocalStory");
        return new {target=EditorUserBuildSettings.activeBuildTarget.ToString(),
            backend=PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone).ToString(),
            windowsInstalled=BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone,BuildTarget.StandaloneWindows64),
            sourceGemma=LocalStoryBuild.SourceModel,gemmaInstalled=File.Exists(LocalStoryBuild.SourceModel),
            editorPath=LocalStoryTestModels.PathFor(root,LocalStoryTestModel.Gemma),
            playerPath=LocalStoryTestModels.PackagedPath(root,LocalStoryTestModel.Gemma),
            enabledNpcs=LocalStoryNotebook.ReadContexts(Path.Combine(root,"Chapters","Chapter_00.xlsx"),Array.Empty<string>()).Where(n=>n.autoGenerate).Select(n=>new{n.characterId,n.model}).ToArray()};
    }
    public static object Regression()
    {
        int checks=0;void Check(bool value,string message) {if(!value)throw new Exception(message);checks++;}
        string root=Path.GetFullPath("tmp/LocalStoryBuildRegression");
        Check(LocalStoryTestModels.PackagedPath(root,LocalStoryTestModel.Gemma)==Path.Combine(root,LocalStoryTestModels.GemmaFile),"Player path depends on Editor tests directory");
        Check(LocalStoryBuild.PlayerRoot(Path.Combine(root,"한글 게임.exe"))==Path.Combine(root,"한글 게임_Data","StreamingAssets","LocalStory"),"Windows data-folder mapping failed");
        bool denied=false;try{LocalStoryBuild.PlayerRoot("foo.app");}catch(ArgumentException){denied=true;}
        Check(denied,"unsupported output layout accepted");
        var manifest=JObject.Parse(File.ReadAllText("BuildSupport/LocalStory/Gemma/ModelManifest.json"));
        Check((string)manifest["modelId"]=="Gemma"&&(string)manifest["file"]==LocalStoryTestModels.GemmaFile,"build model identity wrong");
        Check((long)manifest["bytes"]==LocalStoryTestModels.GemmaBytes&&(string)manifest["sha256"]==LocalStoryTestModels.GemmaSha256,"build model checksum differs");
        Check(!(bool)manifest["externalInferenceNetwork"],"external inference enabled");
        Check(File.ReadAllText("BuildSupport/LocalStory/Gemma/Notice.txt").Contains("Gemma is provided under and subject to the Gemma Terms of Use found at ai.google.dev/gemma/terms"),"model notice missing");
        var npcs=LocalStoryNotebook.ReadContexts(LocalStoryNotebook.TemplatePath("Initial"),Array.Empty<string>()).Where(n=>n.autoGenerate).ToArray();
        Check(npcs.Length>0&&npcs.All(n=>n.model=="Gemma"),"enabled NPC template differs from packaged model");
        denied=false;try{LocalStoryBuild.PackagePlayer(Path.Combine(root,"not-built.exe"));}catch(BuildFailedException){denied=true;}
        Check(denied,"packaging accepted a non-build folder");
        return new {passed=true,checks,defaultBuildModel="Gemma",windowsMono=true};
    }
    public static object InspectPlayer(string executable)
    {
        string root=LocalStoryBuild.PlayerRoot(executable);
        var manifest=JObject.Parse(File.ReadAllText(Path.Combine(root,"ModelManifest.json")));
        var models=Directory.GetFiles(root,"*.gguf",SearchOption.AllDirectories);
        return new {executable,root,model=(string)manifest["model"],models=models.Select(p=>new{path=p,bytes=new FileInfo(p).Length}),
            gemmaOnly=models.Length==1&&Path.GetFileName(models[0])==LocalStoryTestModels.GemmaFile,
            notices=((JArray)manifest["licenses"]).Values<string>().Select(p=>new{path=p,present=File.Exists(Path.Combine(root,p))})};
    }
}
