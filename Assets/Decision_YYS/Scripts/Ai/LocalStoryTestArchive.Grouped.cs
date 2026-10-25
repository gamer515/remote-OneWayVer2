using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class LocalStoryTestArchive
{
    private static readonly System.Collections.Generic.HashSet<string> migratedRoots=new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static readonly System.Collections.Generic.HashSet<string> migratingRoots=new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static void ArchiveFile(string root,string file)
    {
        root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        file=Path.GetFullPath(file);
        if(!file.StartsWith(root,StringComparison.OrdinalIgnoreCase)) throw new IOException("검토 기록 이관 경로 오류");
        byte[] bytes=File.ReadAllBytes(file);string name=Path.GetRelativePath(root,file).Replace('\\','/')+"."+Sha(Convert.ToBase64String(bytes));
        string archive=Path.Combine(root,"Legacy.zip");
        using(var zip=ZipFile.Open(archive,ZipArchiveMode.Update))
            if(zip.GetEntry(name)==null) {using var stream=zip.CreateEntry(name,System.IO.Compression.CompressionLevel.Optimal).Open();stream.Write(bytes,0,bytes.Length);}
        using(var zip=ZipFile.OpenRead(archive))
        using(var stream=zip.GetEntry(name).Open())
        using(var copy=new MemoryStream())
        {stream.CopyTo(copy);if(!bytes.SequenceEqual(copy.ToArray()) || !bytes.SequenceEqual(File.ReadAllBytes(file))) throw new IOException("기존 검토 기록 보관 확인 실패");}
        File.Delete(file);
    }
    public static int MigrateGrouped(string root)
    {
        lock(Sync)
        {
            root=Path.GetFullPath(root);if(migratedRoots.Contains(root) || migratingRoots.Contains(root)) return 0;
            migratingRoots.Add(root);int count=0;
            try
            {
                foreach(string file in List(root).Where(f=>f.EndsWith(".Result.json",StringComparison.Ordinal)).ToArray())
                    if(Read(file)["evidenceEntry"]!=null || (int?)Read(file)["schemaVersion"]<4)
                    {
                        BackupReport(root,file);
                        var compact=CompactReport(ReadGrouped(file));Write(file,compact);
                        if(!JToken.DeepEquals(Read(file),compact))throw new IOException("결과 JSON 이관 확인 실패");
                        count++;
                    }
                if(Directory.Exists(Path.Combine(root,"Results")))
                    foreach(string zip in Directory.GetFiles(Path.Combine(root,"Results"),"Evidence.zip",SearchOption.AllDirectories))
                        if(Directory.GetFiles(Path.GetDirectoryName(zip),"*.Result.json").All(f=>Read(f)["evidenceEntry"]==null))File.Delete(zip);
                foreach(string old in List(root).Where(f=>!f.EndsWith(".Result.json",StringComparison.Ordinal)).ToArray())
                {
                    var report=ReadReport(old);string oldEdit=EditPath(old);
                    byte[] human=File.Exists(oldEdit)?File.ReadAllBytes(oldEdit):null;
                    string file=SaveGrouped(root,report,null),edit=NumberedEditPath(file,report);
                    if(human!=null)
                    {
                        if(File.Exists(edit) && !JToken.DeepEquals(Read(edit),SparseEdit(report)) && !File.ReadAllBytes(edit).SequenceEqual(human))
                            throw new IOException("서로 다른 수정본을 모두 보존합니다: "+oldEdit);
                        File.WriteAllBytes(edit,human);
                        if(!File.ReadAllBytes(edit).SequenceEqual(human)) throw new IOException("수정본 이관 확인 실패");
                    }
                    if(!JToken.DeepEquals(Read(file),CompactReport(report))) throw new IOException("결과 이관 확인 실패");
                    if(Path.GetFileName(old)=="Result.json")
                    {
                        string folder=Path.GetDirectoryName(old);
                        foreach(string f in Directory.GetFiles(folder)) ArchiveFile(root,f);
                        if(!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
                    }
                    else ArchiveFile(root,old);
                    if(human!=null && File.Exists(oldEdit) && oldEdit!=edit) ArchiveFile(root,oldEdit);
                    count++;
                }
                string html=Path.Combine(root,"Index.html");
                if(File.Exists(html) && File.ReadAllText(html).Contains("<!-- LocalStoryCatalog:v")) ArchiveFile(root,html);
                migratedRoots.Add(root);return count;
            }
            finally {migratingRoots.Remove(root);}
        }
    }
    private static void BackupReport(string root,string file)
    {
        byte[] bytes=File.ReadAllBytes(file);
        string entry="Reports/"+Path.GetRelativePath(root,file).Replace('\\','/')+"."+Sha(Convert.ToBase64String(bytes));
        string archive=Path.Combine(root,"Legacy.zip");
        using(var zip=ZipFile.Open(archive,ZipArchiveMode.Update))
            if(zip.GetEntry(entry)==null){using var stream=zip.CreateEntry(entry,System.IO.Compression.CompressionLevel.Optimal).Open();stream.Write(bytes,0,bytes.Length);}
        using(var zip=ZipFile.OpenRead(archive))
        using(var stream=zip.GetEntry(entry).Open())
        using(var copy=new MemoryStream())
        {stream.CopyTo(copy);if(!copy.ToArray().SequenceEqual(bytes))throw new IOException("Report backup verification failed.");}
    }
    private static string TestRoot(string file)
    {
        var dir=new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(file)));
        while(dir!=null && dir.Name!="Results") dir=dir.Parent;
        if(dir==null) throw new InvalidDataException("Results 아래의 결과가 아닙니다: "+file);
        return dir.Parent.FullName;
    }
    private static string Group(JObject report)
    {
        string path=(string)report["storyPath"]??"Unknown";
        var info=ChapterContentCatalog.Info(path);
        string[] parts=path.Split('/');
        return Path.Combine($"For_Run_{Math.Max(1,(int?)report["targetRun"]??2):D2}",
            Slug((string)info?["chapterFolder"]??ChapterContentCatalog.ChapterFolder(parts[0])),
            Slug(ChapterContentCatalog.EpisodeFolder((string)info?["episodeId"]??parts.ElementAtOrDefault(1)??"Unknown")),
            Slug((string)info?["characterId"]??(string)report["interactionMemory"]?["characterId"]??"Unknown"));
    }
    private static int LineId(JObject report,int index) => ChapterContentCatalog.Info((string)report["storyPath"])==null?index:
        ChapterContentCatalog.CardId((string)report["storyPath"],index);
    private static JArray ChangedLines(JObject report,string stage)
    {
        var raw=RawCards((string)report[stage=="draft"?"rawOutput":"correctionRawOutput"]);
        var originals=report["original"]?["MainStory"] as JArray;
        var result=new JArray();
        if(raw==null || originals==null) return result;
        foreach(JObject line in raw)
        {
            int i=(int)line["index"];if(i<0 || i>=originals.Count) continue;
            bool text=(string)line["text"]!=(string)originals[i]["text"];
            var options=line["options"] as JArray;
            bool choice=!JToken.DeepEquals(options??new JArray(), originals[i]["options"] as JArray??new JArray());
            if(!text && !choice) continue;
            var changed=new JObject { ["ID"]=LineId(report,i),["speaker"]=EditSpeaker(report,i),["text"]=line["text"].DeepClone() };
            if(options?.Count>0) changed["options"]=options.DeepClone();
            result.Add(changed);
        }
        return result;
    }
    private static JObject SparseEdit(JObject report)
    {
        var result=new JObject { ["schemaVersion"]=2,["NPC_ID"]=ChapterContentCatalog.Info((string)report["storyPath"])?["characterId"]?.DeepClone(),
            ["storyPath"]=report["storyPath"]?.DeepClone(),["draft"]=ChangedLines(report,"draft") };
        if(!string.IsNullOrEmpty((string)report["correctionRawOutput"])) result["correction"]=ChangedLines(report,"correction");
        return result;
    }
    public static JObject ReadEditData(string file,JObject report)
    {
        var edit=Read(file);if((int?)edit["schemaVersion"]!=2) return edit;
        if((string)edit["storyPath"]!=(string)report["storyPath"]) throw new InvalidDataException("다른 이야기의 수정본입니다.");
        var result=new JObject();
        foreach(string stage in new[]{"draft","correction"})
        {
            if(!(edit[stage] is JArray sparse)) continue;
            var full=BuildEdit(report,stage);var seen=new System.Collections.Generic.HashSet<int>();
            foreach(JObject line in sparse)
            {
                if(line["ID"]?.Type!=JTokenType.Integer || !seen.Add((int)line["ID"])) throw new InvalidDataException("수정 문장 ID가 없거나 중복입니다.");
                int index=Enumerable.Range(0,full.Count).Where(i=>LineId(report,i)==(int)line["ID"]).DefaultIfEmpty(-1).First();
                if(index<0) throw new InvalidDataException("수정할 문장 ID를 찾을 수 없습니다: "+line["ID"]);
                var copy=(JObject)line.DeepClone();copy.Remove("ID");full[index]=copy;
            }
            result[stage]=full;
        }
        return result;
    }
    private static JObject ReadGrouped(string file)
    {
        var view=Read(file);string entry=(string)view["evidenceEntry"];
        if(string.IsNullOrEmpty(entry))
        {
            if(view["correctionPatch"] is JArray patch)
            {
                var cards=(JArray)(RawCards((string)view["rawOutput"])??throw new InvalidDataException("Correction patch has no draft.")).DeepClone();
                var seen=new System.Collections.Generic.HashSet<int>();
                foreach(JObject card in patch)
                {
                    int index=(int)card["index"];
                    if(index<0 || index>=cards.Count || !seen.Add(index))throw new InvalidDataException("Invalid correction patch index.");
                    cards[index]=card.DeepClone();
                }
                view["correctionRawOutput"]=new JObject {["cards"]=cards}.ToString(Newtonsoft.Json.Formatting.None);
            }
            view["prompt"]=view["diagnostics"]?["lastPrompt"]?.DeepClone()??new JValue("");
            if(view["application"] is JObject app)foreach(var p in app.Properties())view[p.Name]=p.Value.DeepClone();
            view["draft"]=ChangedLines(view,"draft");view["correction"]=ChangedLines(view,"correction");
            return view;
        }
        using var zip=ZipFile.OpenRead(Path.Combine(Path.GetDirectoryName(file),"Evidence.zip"));
        var item=zip.GetEntry(entry)??throw new InvalidDataException("생성 근거 기록이 없습니다.");
        using var reader=new StreamReader(item.Open());var report=JObject.Parse(reader.ReadToEnd());
        if(view["application"] is JObject application) foreach(var p in application.Properties()) report[p.Name]=p.Value.DeepClone();
        return report;
    }
    private static JObject CompactReport(JObject report)
    {
        var compact=(JObject)report.DeepClone();string lastPrompt=(string)report["prompt"];
        if(report["sectionInputs"] is JArray inputs && inputs.Count>0)
        {
            bool repaired=report["sectionOutputs"]?.LastOrDefault()?["correctionAttempted"]?.Value<bool>()==true;
            lastPrompt=(string)inputs.Last()[repaired?"correctionPrompt":"prompt"];
        }
        else if((bool?)report["correctionAttempted"]==true && !string.IsNullOrEmpty((string)report["correctionPrompt"]))lastPrompt=(string)report["correctionPrompt"];
        if(compact["application"] is JObject application)
            foreach(var field in application.Properties().ToArray())compact[field.Name]=field.Value.DeepClone();
        foreach(string key in new[]{"evidenceEntry","prompt","correctionPrompt","sectionInputs","sectionOutputs","originalJson","generated","corrected","draft","correction","application","correctionPatch"})compact.Remove(key);
        if(compact["interactionMemory"] is JObject memory) {memory.Remove("playEvents");memory.Remove("playedCards");}
        compact["schemaVersion"]=4;compact["NPC_ID"]=ChapterContentCatalog.Info((string)report["storyPath"])?["characterId"]?.DeepClone();
        var draft=RawCards((string)report["rawOutput"]);var correction=RawCards((string)report["correctionRawOutput"]);
        if(draft!=null && correction!=null && draft.Count==correction.Count &&
            draft.Select((c,i)=>(int)c["index"]==i).All(v=>v) && correction.Select((c,i)=>(int)c["index"]==i).All(v=>v) &&
            (string)report["correctionRawOutput"]==new JObject {["cards"]=correction.DeepClone()}.ToString(Newtonsoft.Json.Formatting.None))
        {
            compact["correctionPatch"]=new JArray(correction.Where((c,i)=>!JToken.DeepEquals(c,draft[i])).Select(c=>c.DeepClone()));
            compact.Remove("correctionRawOutput");
        }
        compact["diagnostics"]=report["diagnostics"]?.DeepClone()??new JObject {
            ["seconds"]=report["seconds"]?.DeepClone(),["model"]=report["model"]?.DeepClone(),["error"]=report["error"]?.DeepClone(),
            ["operatingSystem"]=SystemInfo.operatingSystem,["processor"]=SystemInfo.processorType,["memoryMB"]=SystemInfo.systemMemorySize,
            ["graphics"]=SystemInfo.graphicsDeviceName,["graphicsMemoryMB"]=SystemInfo.graphicsMemorySize};
        compact["diagnostics"]["lastPrompt"]=lastPrompt??(string)report["diagnostics"]?["lastPrompt"]??"";
        foreach(string field in new[]{"seconds","model","error"})((JObject)compact["diagnostics"]).Remove(field);
        var compositionInputs=report["sectionInputs"] as JArray;
        if(report["sectionOutputs"] is JArray sections && sections.Count>0)
            compact["diagnostics"]["sectionTimings"]=new JArray(sections.Select(s=>new JObject {
                ["sectionId"]=s["sectionId"]?.DeepClone(),["startCard"]=((int?)s["start"]??0)+1,["cards"]=s["count"]?.DeepClone(),
                ["draftSeconds"]=s["draftSeconds"]?.DeepClone(),["correctionSeconds"]=s["correctionSeconds"]?.DeepClone(),
                ["verificationSeconds"]=s["verificationSeconds"]?.DeepClone(),["modelLoadSeconds"]=s["modelLoadSeconds"]?.DeepClone(),
                ["generationSeconds"]=s["generationSeconds"]?.DeepClone(),["correctionAttempted"]=s["correctionAttempted"]?.DeepClone(),
                ["draftFirstTextSeconds"]=s["draftFirstTextSeconds"]?.DeepClone(),["correctionFirstTextSeconds"]=s["correctionFirstTextSeconds"]?.DeepClone(),
                ["promptChars"]=compositionInputs?.FirstOrDefault(i=>(int?)i["start"]==(int?)s["start"])?["promptChars"]?.DeepClone(),
                ["promptModules"]=compositionInputs?.FirstOrDefault(i=>(int?)i["start"]==(int?)s["start"])?["promptModules"]?.DeepClone(),
                ["reviewApplied"]=s["reviewApplied"]?.DeepClone(),["sourceFallback"]=s["sourceFallback"]?.DeepClone(),
                ["error"]=s["error"]?.DeepClone(),["correctionError"]=s["correctionError"]?.DeepClone() }));
        return compact;
    }
    private static string SaveGrouped(string root,JObject report,string origin)
    {
        lock(Sync)
        {
            root=Path.GetFullPath(root);
            if(!migratedRoots.Contains(root) && !migratingRoots.Contains(root)) MigrateGrouped(root);
            if(!string.IsNullOrEmpty(origin))
            {
                var prior=List(root).Where(f=>f.EndsWith(".Result.json",StringComparison.Ordinal)).FirstOrDefault(f=>(string)ReadReport(f)["archiveOrigin"]==origin);
                if(prior!=null) return prior;
            }
            int number=(int?)report["archiveNumber"]??0;
            string sequence=Path.Combine(root,"Sequence.json");
            if(number<=0)
            {
                int next=File.Exists(sequence)?(int)Read(sequence)["next"]:List(root).Select(f=>(int?)ReadReport(f)["archiveNumber"]??0).DefaultIfEmpty().Max()+1;
                number=next;report["archiveNumber"]=number;report["archiveId"]=number.ToString("D4");
            }
            Write(sequence,new JObject { ["next"]=Math.Max(number+1,File.Exists(sequence)?(int)Read(sequence)["next"]:1) });
            report["archiveOrigin"]=origin!=null?new JValue(origin):report["archiveOrigin"]?.DeepClone()??JValue.CreateNull();
            string folder=Path.Combine(root,"Results",Group(report));Directory.CreateDirectory(folder);
            string file=Path.Combine(folder,number.ToString("D4")+".Result.json");var view=CompactReport(report);
            if(!File.Exists(file)) Write(file,view);
            if(!JToken.DeepEquals(Read(file),view)) throw new IOException("기존 AI 결과는 덮어쓰지 않습니다.");
            string edit=NumberedEditPath(file,report);if(!File.Exists(edit)) Write(edit,SparseEdit(report));
            PruneAutomaticResults(root,file,report);
            return file;
        }
    }
    private static void PruneAutomaticResults(string root,string latest,JObject report)
    {
        if(!((string)report["testScenario"]??"").StartsWith("ActualGameplay",StringComparison.Ordinal)) return;
        var automatic=List(root).Where(f=>f.EndsWith(".Result.json",StringComparison.Ordinal)).Select(f=>(file:f,report:ReadReport(f)))
            .Where(p=>(string)p.report["storyPath"]==(string)report["storyPath"] && ((string)p.report["testScenario"]??"").StartsWith("ActualGameplay",StringComparison.Ordinal))
            .OrderByDescending(p=>(int?)p.report["archiveNumber"]??0).Skip(3).ToArray();
        foreach(var item in automatic)
        {
            string edit=EditPath(item.file);
            if((bool?)item.report["reviewed"]==true || !File.Exists(edit) || !JToken.DeepEquals(Read(edit),SparseEdit(item.report))) continue;
            File.Delete(item.file);File.Delete(edit);
        }
    }
    private static void UpdateGrouped(string file,LocalStoryTestReport report)
    {
        var view=CompactReport(ReadReport(file));
        var application=new JObject { ["saved"]=report.saved,["runtimeStage"]=report.runtimeStage,["runtimeError"]=report.runtimeError,
            ["reviewRequired"]=report.reviewRequired,["reviewError"]=report.reviewError,["reviewGeneratedCards"]=report.reviewGeneratedCards,
            ["modelPath"]=report.modelPath,["runtimePath"]=report.runtimePath,["runtimeFailureDetails"]=report.runtimeFailureDetails,["seconds"]=report.seconds };
        foreach(var field in application.Properties())view[field.Name]=field.Value.DeepClone();
        Write(file,view);
    }
}
