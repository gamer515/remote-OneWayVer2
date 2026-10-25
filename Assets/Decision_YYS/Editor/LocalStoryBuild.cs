using System;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Verified Gemma payload for Windows players. Comparison models in the project are preserved.</summary>
public sealed class LocalStoryBuild : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    public int callbackOrder => 1000;
    public static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath,".."));
    public static string SourceModel => Path.Combine(ProjectRoot,"Tests","LocalStoryBenchmark","Models",LocalStoryTestModels.GemmaFile);
    private static string Support => Path.Combine(ProjectRoot,"BuildSupport","LocalStory","Gemma");
    private static string SourceRuntime => Path.Combine(Application.streamingAssetsPath,"LocalStory");
    public static string FileHash(string file) => LocalStoryModelHash.FileHash(file);
    public static string PlayerRoot(string executable)
    {
        string exe=Path.GetFullPath(executable);
        if(!exe.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Windows .exe 빌드 경로가 필요합니다.");
        return Path.Combine(Path.GetDirectoryName(exe),Path.GetFileNameWithoutExtension(exe)+"_Data","StreamingAssets","LocalStory");
    }
    public static void ValidateSources()
    {
        Debug.Log("[LocalStoryBuild] Gemma 모델 파일 검증 시작");
        LocalStoryTestModels.VerifyGemma(SourceModel,CancellationToken.None);
        var manifest=JObject.Parse(File.ReadAllText(Path.Combine(Support,"ModelManifest.json")));
        if((string)manifest["file"]!=LocalStoryTestModels.GemmaFile || (long)manifest["bytes"]!=LocalStoryTestModels.GemmaBytes ||
            (string)manifest["sha256"]!=LocalStoryTestModels.GemmaSha256) throw new BuildFailedException("Gemma 배포 명세와 설치 모델의 기준이 다릅니다.");
        foreach(string file in new[]{"Notice.txt","GemmaTerms.html","GemmaProhibitedUsePolicy.html"})
            if(!File.Exists(Path.Combine(Support,file)) || new FileInfo(Path.Combine(Support,file)).Length==0)
                throw new BuildFailedException("Gemma 배포 고지 누락: "+file);
        foreach(string file in new[]{"runtimes/win-x64/native/llamalib_win-x64_tinyblas.dll","runtimes/win-x64/native/llamalib_win-x64_avx2.dll",
            "LICENSE","third_party_licenses/llama.cpp.LICENSE.txt"})
            if(!File.Exists(Path.Combine(SourceRuntime,file))) throw new BuildFailedException("내장 추론 런타임/고지 누락: "+file);
        foreach(var npc in ChapterContentCatalog.Chapters.SelectMany(c=>LocalStoryNotebook.ReadContexts(LocalStoryNotebook.TemplatePath((string)c["id"]),Array.Empty<string>())).Where(n=>n.autoGenerate))
            if(npc.model!="Gemma") throw new BuildFailedException("배포 패키지는 Gemma를 포함합니다. 자동 생성 NPC의 모델 설정을 확인하세요: "+npc.characterId+" / "+npc.model);
    }
    public void OnPreprocessBuild(BuildReport report)
    {
        if(report.summary.platform!=BuildTarget.StandaloneWindows64)
            throw new BuildFailedException("현재 내장 Gemma 배포 지원 대상은 Windows x64입니다.");
        if(PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone)!=ScriptingImplementation.Mono2x)
            throw new BuildFailedException("현재 검증 대상은 Windows x64 Mono입니다. IL2CPP 네이티브 콜백 지원은 별도 검증이 필요합니다.");
        ValidateSources();
    }
    public void OnPostprocessBuild(BuildReport report) => PackagePlayer(report.summary.outputPath);
    public static void PackagePlayer(string executable)
    {
        string root=PlayerRoot(executable);
        if(!File.Exists(executable) || !Directory.Exists(root)) throw new BuildFailedException("Unity가 만든 플레이어/StreamingAssets 폴더가 없습니다: "+root);
        // Only touch the exact Windows output directory. Reject linked parents before copying/deleting.
        for(var parent=new DirectoryInfo(root);parent!=null;parent=parent.Parent)
            if((parent.Attributes&FileAttributes.ReparsePoint)!=0) throw new BuildFailedException("연결된 경로에는 모델을 패키징하지 않습니다: "+parent.FullName);
        ValidateSources();
        string destination=LocalStoryTestModels.PackagedPath(root,LocalStoryTestModel.Gemma);
        Debug.Log("[LocalStoryBuild] Gemma 모델 패키징: "+destination);
        if(!File.Exists(destination) || new FileInfo(destination).Length!=LocalStoryTestModels.GemmaBytes || FileHash(destination)!=LocalStoryTestModels.GemmaSha256)
            File.Copy(SourceModel,destination,true);
        if(new FileInfo(destination).Length!=LocalStoryTestModels.GemmaBytes || FileHash(destination)!=LocalStoryTestModels.GemmaSha256)
            throw new BuildFailedException("빌드 Gemma 복사본 검증 실패");
        string notices=Path.Combine(root,"Gemma");Directory.CreateDirectory(notices);
        foreach(string name in new[]{"Notice.txt","GemmaTerms.html","GemmaProhibitedUsePolicy.html"}) File.Copy(Path.Combine(Support,name),Path.Combine(notices,name),true);
        File.Copy(Path.Combine(Support,"ModelManifest.json"),Path.Combine(root,"ModelManifest.json"),true);
        // StreamingAssets initially contains the historical comparison payload. Remove only its verified
        // copy from this build; never move/delete the project's installed Qwen model or old test evidence.
        string qwen=LocalStoryTestModels.PackagedPath(root,LocalStoryTestModel.Qwen);
        if(File.Exists(qwen))
        {
            var original=JObject.Parse(File.ReadAllText(Path.Combine(ProjectRoot,"BuildSupport","LocalStory","Qwen","ModelManifest.json")));
            if((string)original["file"]!=LocalStoryTestModels.QwenFile || new FileInfo(qwen).Length!=(long)original["bytes"] || FileHash(qwen)!=(string)original["sha256"])
                throw new BuildFailedException("알 수 없는 Qwen 파일은 삭제하지 않습니다. 새 빌드 폴더를 사용하세요.");
            File.Delete(qwen);
        }
        string license=Path.Combine(root,"Qwen-LICENSE.txt");if(File.Exists(license)&&FileHash(license)==FileHash(Path.Combine(SourceRuntime,"Qwen-LICENSE.txt"))) File.Delete(license);
        string retiredBook=Path.Combine(root,"NpcStoryData.xlsx");
        if(File.Exists(retiredBook) && FileHash(retiredBook)=="4c43ca9c3f7ea14a5f45489842e93c0d7728dae908b741ddb598497f17a9b68f") File.Delete(retiredBook);
        if(Directory.GetFiles(root,"*.gguf",SearchOption.AllDirectories).Any(p=>Path.GetFullPath(p)!=Path.GetFullPath(destination)))
            throw new BuildFailedException("예상하지 못한 추가 모델이 출력 폴더에 있습니다. 새 빌드 폴더를 사용하세요.");
        Debug.Log("[LocalStoryBuild] Gemma Windows 패키지 검증 완료 / "+LocalStoryTestModels.GemmaBytes+" bytes");
    }
}
