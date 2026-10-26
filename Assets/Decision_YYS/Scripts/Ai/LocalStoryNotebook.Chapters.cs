using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class LocalStoryNotebook
{
    private static readonly Dictionary<string,(long stamp,long length,Dictionary<string,List<string[]>> sheets)> sheetCache=new Dictionary<string,(long,long,Dictionary<string,List<string[]>>) >();
    private static readonly Dictionary<string,(long stamp,long length,GameProgress progress)> progressCache=new Dictionary<string,(long,long,GameProgress)>();
    public static string ChapterPath(SaveIOService storage,string chapter)=>Path.Combine(storage.StorageRoot,"Records",ChapterContentCatalog.ChapterFolder(chapter)+".xlsx");
    public static string TemplatePath(string chapter)
    {
        string folder=Path.Combine(Application.streamingAssetsPath,"LocalStory","Chapters"),file=Path.Combine(folder,ChapterContentCatalog.ChapterFolder(chapter)+".xlsx");
        // New chapters reuse the common sheet layout; their rows still come entirely from their JSON.
        return File.Exists(file)?file:Directory.GetFiles(folder,"*.xlsx").OrderBy(p=>p,StringComparer.Ordinal).First();
    }
    private static List<string[]> CachedRows(string file,string sheet)
    {
        var info=new FileInfo(file);
        if(!sheetCache.TryGetValue(file,out var cache)||cache.stamp!=info.LastWriteTimeUtc.Ticks||cache.length!=info.Length)
        {cache=(info.LastWriteTimeUtc.Ticks,info.Length,new Dictionary<string,List<string[]>>());sheetCache[file]=cache;}
        if(!cache.sheets.TryGetValue(sheet,out var rows))
        {using var stream=File.Open(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);using var zip=new ZipArchive(stream,ZipArchiveMode.Read);rows=Rows(zip,sheet);cache.sheets[sheet]=rows;}
        return rows;
    }
    private static GameProgress CachedProgress(string file)
    {
        var info=new FileInfo(file);
        if(progressCache.TryGetValue(file,out var c)&&c.stamp==info.LastWriteTimeUtc.Ticks&&c.length==info.Length)return c.progress;
        var p=ProgressDocument.Read(file);progressCache[file]=(info.LastWriteTimeUtc.Ticks,info.Length,p);return p;
    }
    private static JObject Sheet(string name,string[] headers,double[] widths,IEnumerable<string[]> rows)=>new JObject {
        ["name"]=name,["headers"]=JArray.FromObject(headers),["widths"]=JArray.FromObject(widths),["rows"]=JArray.FromObject(rows)};
    private static IEnumerable<string> Values(JToken value)=>value is JArray a?a.Values<string>():Array.Empty<string>();
    private static string Situation(JObject rule,bool hidden=false)
    {
        var fields=hidden?new[]{("requiresItems_ALL","보유 아이템"),("requiresUnlock_ALL","해금 상태: 해금됨")}:
            new[]{("requiresItems","보유 아이템"),("excludesItems","미보유 아이템"),("requiresUnlocks","해금 상태: 해금됨"),
                ("excludesUnlocks","해금 상태: 미해금"),("requiresEffects","이번 회차 기록: 발생함"),("excludesEffects","이번 회차 기록: 발생하지 않음")};
        return Numbered(fields.SelectMany(f=>Values(rule[f.Item1]).Select(id=>f.Item2+": "+id)));
    }
    // Templates and runtime additions share this JSON projection; no NPC-specific descriptions.
    public static JObject BookData(string chapter,string[] earned=null)
    {
        var people=ChapterContentCatalog.Characters(chapter).ToArray();
        var npc=people.Select(p=>new[]{(string)p["characterId"],(string)p["name"],Numbered(new[]{"@npc:"+p["characterId"]+"/public"}),
            Numbered((p["hidden"] as JObject)?.Properties().Select(h=>"@npc:"+p["characterId"]+"/hidden/"+h.Name)??Array.Empty<string>()),
            Numbered((p["hidden"] as JObject)?.Properties().SelectMany(h=>Values(h.Value["requiresUnlock_ALL"])).Distinct()??Array.Empty<string>()),
            (bool?)p["autoGenerate"]==true?"1":"0","Gemma"}.Concat(p["stats"] is JArray stats?stats.Values<string>():new[]{"","","",""}).ToArray()).ToArray();
        var unlocked=new HashSet<string>(earned??Array.Empty<string>());
        var unlocks=new Dictionary<string,string[]>();
        void Unlock(string id,string person,string description,string reference="")
        {
            if(string.IsNullOrWhiteSpace(id))return;
            if(!unlocks.TryGetValue(id,out var row))unlocks[id]=new[]{id,person??"",description??"",unlocked.Contains(id)?"1":"0",reference};
            else
            {
                foreach(var part in new[]{(1,person),(2,description),(4,reference)})
                    if(!string.IsNullOrWhiteSpace(part.Item2)&&!row[part.Item1].Split('\n').Contains(part.Item2))
                        row[part.Item1]=string.IsNullOrWhiteSpace(row[part.Item1])?part.Item2:row[part.Item1]+"\n"+part.Item2;
            }
        }
        foreach(var p in people)
        foreach(var h in (p["hidden"] as JObject)?.Properties()??Enumerable.Empty<JProperty>())
        {
            var rule=(JObject)h.Value;
            foreach(string id in Values(rule["requiresUnlock_ALL"]))
                Unlock(id,(string)p["characterId"],(string)rule["text"],"@npc:"+p["characterId"]+"/hidden/"+h.Name);
        }
        foreach(var r in NarrativeInteractions.Chapter(chapter)?.interactions??Array.Empty<NarrativeInteractionRule>())
            foreach(string id in (r.grantsUnlocks??Array.Empty<string>()).Concat(r.requiresUnlocks??Array.Empty<string>()).Concat(r.excludesUnlocks??Array.Empty<string>()))
                Unlock(id,r.characterId,"",r.revealedFact??"");
        foreach(var scene in ChapterContentCatalog.Stories.Where(s=>(string)s["chapterId"]==chapter && !((string)s["key"]).Contains('/')))
            Unlock("event:"+(string)scene["placeId"],(string)scene["characterId"],"만남 완료: "+(string)scene["placeId"]);
        var rewards=(NarrativeInteractions.Chapter(chapter)?.interactions??Array.Empty<NarrativeInteractionRule>()).Select(r=>new[]{r.characterId,r.id,
            Numbered(r.choiceIds??Array.Empty<string>()),Situation(JObject.FromObject(r)),Numbered(r.grantsItems??Array.Empty<string>()),
            Numbered(r.consumesItems??Array.Empty<string>()),Numbered(r.grantsUnlocks??Array.Empty<string>())});
        return new JObject {["folder"]=ChapterContentCatalog.ChapterFolder(chapter),["sheets"]=new JArray {
            Sheet("NPC설정",new[]{"NPC_ID","이름","공개설정 참조","숨은설정 참조","참조 해금_ID","자동생성","모델","체력","속도","지능","매력"},new double[]{19,19,35,65,50,12,13,10,10,10,10},npc),
            Sheet("해금규칙",new[]{"해금_ID","NPC_ID","설명","해금상태","설정 참조"},new double[]{43,18,78,12,70},unlocks.Values),
            Sheet("아이템보상",new[]{"NPC_ID","규칙_ID","선택_ID","현재상황 (모두 충족)","지급아이템","회수아이템","획득해금"},new double[]{18,35,43,85,35,35,48},rewards)}};
    }
    private static bool SimpleBook(string file)
    {
        using var stream=File.Open(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);using var zip=new ZipArchive(stream,ZipArchiveMode.Read);
        return Read(zip,"xl/workbook.xml").Descendants(Ns+"sheet").Select(s=>(string)s.Attribute("name")).SequenceEqual(new[]{"NPC설정","해금규칙","아이템보상"})&&
            Read(zip,SheetPath(zip,"NPC설정")).Descendants(Ns+"row").First().Elements(Ns+"c").Count()==11;
    }
    private static List<string[]> EditableRows(string file)
    {
        bool simple=SimpleBook(file);
        return CachedRows(file,"NPC설정").Where(r=>!string.IsNullOrWhiteSpace(r[0])).Select(r=>simple?r.Take(11).ToArray():new[]{r[0],r[1],r[4],r[5],r[6],r[7],r[8],r[9],r[10],r[11],r[12]}).ToList();
    }
    private static void EnsureChapterBooks(SaveIOService storage,string[] earned=null)
    {
        earned??=EarnedUnlocks(storage);
        foreach(var chapter in ChapterContentCatalog.Chapters)
        {
            string id=(string)chapter["id"],file=ChapterPath(storage,id);bool exists=File.Exists(file),simple=exists&&SimpleBook(file);
            var sheets=(JArray)BookData(id,earned)["sheets"];
            string legacy=Path.Combine(storage.StorageRoot,"Narrative","NpcStoryData.xlsx");
            var edits=exists?EditableRows(file):File.Exists(legacy)?EditableRows(legacy):new List<string[]>();
            foreach(JArray row in sheets[0]["rows"])
            {
                var edit=edits.FirstOrDefault(r=>r[0]==(string)row[0]);if(edit==null)continue;
                // Only controls are editable. Source references always follow the current JSON.
                foreach(int i in new[]{1,5,6,7,8,9,10})row[i]=edit[i];
            }
            bool current=simple&&CurrentUnlockSheet(file);
            if(current&&sheets.OfType<JObject>().All(s=>CachedRows(file,(string)s["name"]).Where(r=>!string.IsNullOrWhiteSpace(r[0])).Select(r=>r.Take(((JArray)s["headers"]).Count).ToArray())
                .Select(r=>string.Join("\u001f",r)).SequenceEqual(s["rows"].Select(r=>string.Join("\u001f",r.Values<string>())))))continue;
            Directory.CreateDirectory(Path.GetDirectoryName(file));string temp=file+".tmp";File.Copy(TemplatePath(id),temp,true);
            try
            {
                using(var zip=ZipFile.Open(temp,ZipArchiveMode.Update))foreach(var s in sheets)ReplaceRows(zip,(string)s["name"],s["rows"].Select(r=>r.Values<string>().ToArray()).ToList());
                ReadChapterContexts(temp,Array.Empty<string>());
                if(exists)
                {
                    if(!simple)
                    {
                        string backup=Path.Combine(storage.StorageRoot,"Migrations","BeforeSimpleRecords",Path.GetFileName(file));
                        Directory.CreateDirectory(Path.GetDirectoryName(backup));if(!File.Exists(backup))File.Copy(file,backup);
                    }
                    File.Replace(temp,file,null);
                }
                else File.Move(temp,file);
                sheetCache.Remove(file);
            }
            catch(IOException e) {Debug.LogWarning("[Story] Excel 갱신 보류: "+e.Message);}
            finally {if(File.Exists(temp))File.Delete(temp);}
        }
    }
    public static LocalStoryNpcContext[] ConfiguredContexts(SaveIOService storage,string[] earned,bool create=true)
    {
        if(create)EnsureChapterBooks(storage,earned);
        return ChapterContentCatalog.Chapters.SelectMany(c=>{string chapter=(string)c["id"],file=ChapterPath(storage,chapter);return ReadChapterContexts(File.Exists(file)?file:TemplatePath(chapter),earned);})
            .GroupBy(c=>c.characterId).Select(g=>g.First()).ToArray();
    }
    private static LocalStoryNpcContext[] ReadChapterContexts(string file,string[] earned)
    {
        var result=new List<LocalStoryNpcContext>();earned??=Array.Empty<string>();
        // Read each workbook once per file timestamp. Requests freeze these IDs; no Excel I/O per section.
        var flags=ReadUnlockStates(file);
        earned=earned.Where(id=>flags.TryGetValue(id,out byte value)&&value==1).ToArray();
        foreach(var r in EditableRows(file))
        {
            var source=ChapterContentCatalog.Character(r[0]);if(source==null||result.Any(c=>c.characterId==r[0]))throw new InvalidDataException("알 수 없거나 중복된 NPC: "+r[0]);
            if(r[5]!="0"&&r[5]!="1")throw new InvalidDataException("자동생성은 0 또는 1: "+r[0]);
            if(r[6]!="Gemma"&&r[6]!="Qwen"&&r[6]!="OmniRoute")throw new InvalidDataException("알 수 없는 모델: "+r[6]);
            string[] needs=Ids(r[4]);string hidden=r[3].Contains("@npc:")?ChapterContentCatalog.Setting(r[3],earned):needs.Length>0&&needs.All(earned.Contains)?r[3]:"";
            if(!r[3].Contains("@npc:")&&needs.Length>0&&!needs.All(earned.Contains))hidden="";
            result.Add(new LocalStoryNpcContext {characterId=r[0],name=string.IsNullOrEmpty(r[1])?source.name:r[1],role=source.role,personality=source.personality,
                publicFacts=ChapterContentCatalog.Setting(r[2],earned),revealedPast=hidden,autoGenerate=r[5]=="1",model=r[6],
                unlockedIds=earned,stats=r.Skip(7).Take(4).All(string.IsNullOrWhiteSpace)?Array.Empty<int>():r.Skip(7).Take(4).Select(int.Parse).ToArray()});
        }
        foreach(var npc in result)npc.publicFacts+="\n"+NarrativeInteractions.RevealedFacts(npc.characterId,earned);
        return result.ToArray();
    }
    private static bool CurrentUnlockSheet(string file)
    {
        using var stream=File.Open(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);using var zip=new ZipArchive(stream,ZipArchiveMode.Read);
        var rows=Read(zip,SheetPath(zip,"해금규칙")).Descendants(Ns+"row").ToArray();
        return rows.First().Elements(Ns+"c").Count()==5&&rows.Skip(1).SelectMany(r=>r.Elements(Ns+"c"))
            .Where(c=>((string)c.Attribute("r")).StartsWith("D",StringComparison.Ordinal)).All(c=>c.Attribute("t")==null || (string)c.Attribute("t")=="n");
    }
    public static Dictionary<string,byte> ReadUnlockStates(string file)
    {
        var result=new Dictionary<string,byte>();if(!CurrentUnlockSheet(file))return result;
        foreach(var row in CachedRows(file,"해금규칙").Where(r=>!string.IsNullOrWhiteSpace(r[0])))
        {
            if((row[3]!="0"&&row[3]!="1") || result.ContainsKey(row[0]))throw new InvalidDataException("해금 ID 중복 또는 0/1 상태 오류: "+row[0]);
            result.Add(row[0],byte.Parse(row[3]));
        }
        return result;
    }
}
