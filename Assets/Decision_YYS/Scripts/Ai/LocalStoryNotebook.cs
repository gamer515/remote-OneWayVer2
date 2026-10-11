using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

[Serializable] public sealed class LocalStoryNpcContext
{
    public string characterId, name, role, personality, publicFacts, revealedPast;
    public bool autoGenerate;
    public string model;
}

/// <summary>Editable NPC/rule sheets are author inputs; recorded sheets only mirror real saves.</summary>
public static class LocalStoryNotebook
{
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    public static string PathFor(SaveIOService storage) => Path.Combine(storage.StorageRoot, "Narrative", "NpcStoryData.xlsx");
    private static void Ensure(SaveIOService storage)
    {
        string file = PathFor(storage);
        if (File.Exists(file)) return;
        string template = Path.Combine(Application.streamingAssetsPath, "LocalStory", "NpcStoryData.xlsx");
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        File.Copy(template, file, false);
    }
    private static XDocument Read(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path) ?? throw new InvalidDataException("Excel 구성 누락: " + path);
        using var stream = entry.Open();
        // XDocument's default reader prohibits DTD/entity expansion.
        return XDocument.Load(stream);
    }
    private static string SheetPath(ZipArchive zip, string name)
    {
        var sheet = Read(zip, "xl/workbook.xml").Descendants(Ns + "sheet").Single(s => (string)s.Attribute("name") == name);
        string id = (string)sheet.Attribute(XName.Get("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships"));
        var rel = Read(zip, "xl/_rels/workbook.xml.rels").Root.Elements().Single(r => (string)r.Attribute("Id") == id);
        string target = (string)rel.Attribute("Target");
        if ((string)rel.Attribute("TargetMode") == "External" || string.IsNullOrEmpty(target) || target.Contains("..") || target.Contains('\\'))
            throw new InvalidDataException("외부/상위 경로 Excel 참조 금지");
        return target.StartsWith("/") ? target.TrimStart('/') : "xl/" + target;
    }
    private static List<string[]> Rows(ZipArchive zip, string name)
    {
        string[] shared = zip.GetEntry("xl/sharedStrings.xml") == null ? Array.Empty<string>() :
            Read(zip,"xl/sharedStrings.xml").Descendants(Ns+"si").Select(s => string.Concat(s.Descendants(Ns+"t").Select(t => t.Value))).ToArray();
        var rows = new List<string[]>();
        foreach (var row in Read(zip, SheetPath(zip,name)).Descendants(Ns+"row").Take(10000))
        {
            if ((int?)row.Attribute("r") == 1) continue;
            var values = new string[32];
            foreach (var cell in row.Elements(Ns+"c"))
            {
                if (cell.Element(Ns+"f") != null) throw new InvalidDataException("설정/해금 입력에는 수식을 사용하지 마세요.");
                int column = 0;
                foreach (char c in ((string)cell.Attribute("r") ?? "")) { if (c < 'A' || c > 'Z') break; column = column * 26 + c - 'A' + 1; }
                if (column < 1 || column > values.Length) continue;
                string value = (string)cell.Element(Ns+"v") ?? "";
                if ((string)cell.Attribute("t") == "s") value = shared[int.Parse(value)];
                if ((string)cell.Attribute("t") == "inlineStr") value = string.Concat(cell.Descendants(Ns+"t").Select(t => t.Value));
                values[column-1] = value;
            }
            rows.Add(values.Select(v => v ?? "").ToArray());
        }
        return rows;
    }
    public static string[] EarnedUnlocks(SaveIOService storage, int throughRun = int.MaxValue) =>
        ProgressFiles(storage).Where(p => RunNumber(p) <= throughRun).SelectMany(p =>
            JsonUtility.FromJson<GameProgress>(File.ReadAllText(p)).unlockedIds ?? new List<string>()).Distinct().OrderBy(s => s).ToArray();
    private static string[] ProgressFiles(SaveIOService storage)
    {
        string runs = Path.Combine(storage.StorageRoot,"Runs");
        return Directory.Exists(runs) ? Directory.GetFiles(runs,"Progress.json",SearchOption.AllDirectories).OrderBy(RunNumber).ToArray() : Array.Empty<string>();
    }
    private static int RunNumber(string file) => int.TryParse(Path.GetFileName(Path.GetDirectoryName(file)).Replace("Run_",""),out int run) ? run : 0;
    private static string[] Ids(string text) => (text ?? "").Split(new[]{';',','},StringSplitOptions.RemoveEmptyEntries).Select(v => v.Trim()).Where(v => v.Length > 0).ToArray();
    public static LocalStoryNpcContext[] Contexts(SaveIOService storage, string[] earned)
    {
        Ensure(storage);
        using var stream = File.Open(PathFor(storage),FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
        using var zip = new ZipArchive(stream,ZipArchiveMode.Read);
        var result = new List<LocalStoryNpcContext>();
        foreach (var r in Rows(zip,"NPC설정").Where(r => !string.IsNullOrWhiteSpace(r[0])))
        {
            if (result.Any(c => c.characterId == r[0]) || Resources.Load<TextAsset>("Story_Json_Data/Characters/"+r[0]) == null)
                throw new InvalidDataException("알 수 없거나 중복된 NPC_ID: "+r[0]);
            if (r[7] != "0" && r[7] != "1") throw new InvalidDataException("자동생성은 0 또는 1: "+r[0]);
            if (r[8] != "Gemma" && r[8] != "Qwen") throw new InvalidDataException("모델은 Gemma 또는 Qwen: "+r[0]);
            string[] needs = Ids(r[6]);
            result.Add(new LocalStoryNpcContext {characterId=r[0],name=r[1],role=r[2],personality=r[3],publicFacts=r[4],
                revealedPast=needs.Length > 0 && needs.All(id => earned.Contains(id)) ? r[5] : "",autoGenerate=r[7]=="1",model=r[8]});
        }
        foreach (var r in Rows(zip,"해금규칙").Where(r => r[6]=="1" && !string.IsNullOrWhiteSpace(r[0])))
        {
            var npc=result.FirstOrDefault(c => c.characterId==r[1]);
            string[] needs=Ids(r[3]);
            if(npc==null || needs.Length==0 || !needs.All(id => earned.Contains(id))) continue;
            if(IsStoryPath(r[2]) && IsStoryPath(r[4]) && r[4].StartsWith(r[2]+"/",StringComparison.Ordinal) && !r[4].Contains("/Variants/"))
                npc.publicFacts+=(string.IsNullOrWhiteSpace(r[5])?"":"\n"+r[5]);
        }
        return result.ToArray();
    }
    public static string ResolveOpeningPath(SaveIOService storage, string npc, string original)
    {
        try
        {
            Ensure(storage);
            using var stream = File.Open(PathFor(storage),FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
            using var zip = new ZipArchive(stream,ZipArchiveMode.Read);
            string[] earned = EarnedUnlocks(storage);
            foreach (var r in Rows(zip,"해금규칙").Where(r => r[6]=="1" && r[1]==npc && r[2]==original))
            {
                string[] needs = Ids(r[3]);
                if (string.IsNullOrWhiteSpace(r[0]) || needs.Length==0 || !needs.All(id => earned.Contains(id))) continue;
                string target = r[4];
                if (!IsStoryPath(target) || !target.StartsWith(original+"/",StringComparison.Ordinal) || target.Contains("/Variants/"))
                    throw new InvalidDataException("해금 이야기는 기존 NPC 폴더 아래의 원문 경로여야 합니다: "+target);
                Debug.Log("[Story] Excel 해금 "+r[0]+" → "+target);
                return target; // First matching authored row wins, no model-defined branches.
            }
        }
        catch(Exception e) { Debug.LogWarning("[Story] Excel 해금 확인 실패, 기본 이야기 유지: "+e.Message); }
        return original;
    }
    public static bool IsStoryPath(string path) => !string.IsNullOrWhiteSpace(path) && path.IndexOfAny(new[]{'.','\\',':'}) < 0 &&
        Resources.Load<TextAsset>("Story_Json_Data/"+path+"/Story") != null;
    public static string PromptContext(LocalStoryRevisionRequest request, string npc)
    {
        var c = request.npcContexts?.FirstOrDefault(p => p.characterId == npc);
        if (c == null) return "";
        var facts=new Newtonsoft.Json.Linq.JObject { ["name"]=c.name,["role"]=c.role,["personality"]=c.personality,["publicFacts"]=c.publicFacts,["revealedPast"]=c.revealedPast };
        return "\n[Excel 인물 설정 — 대사에 그대로 출력하지 않을 참고 자료]\n"+facts.ToString(Newtonsoft.Json.Formatting.Indented)+
            "\n실제 해금된 ID: "+string.Join(", ",request.earnedUnlocks??Array.Empty<string>())+
            "\n핵심 대사도 같은 사건 의미로 새로 표현한다. 게임 기능/보상/분기는 만들거나 변경하지 않는다.";
    }
    public static void Refresh(SaveIOService storage)
    {
        try
        {
            Ensure(storage);
            string file = PathFor(storage), temp = file+".tmp";
            var history = new List<string[]>();
            var generation = new List<string[]>();
            foreach (string progressFile in ProgressFiles(storage))
            {
                int run = RunNumber(progressFile);
                var progress = JsonUtility.FromJson<GameProgress>(File.ReadAllText(progressFile));
                foreach (var e in progress.storyEvents ?? new List<StoryEventRecord>())
                    history.Add(new[]{run.ToString(),e.sequence.ToString(),e.characterId,e.characterName,e.terrainName,e.storyPath,e.kind,e.choiceId,
                        e.displayedText,e.reason,e.relationshipDelta.ToString(),Stats(e.statsBefore),Stats(e.statsAfter),Stats(e.coinsBefore),Stats(e.coinsAfter),
                        e.itemId,e.quantity.ToString(),e.direction,string.Join(";",e.grantsUnlocks??Array.Empty<string>()),e.eventKey});
                // Stored actual displayed dialogue, separate from the player's choice/event rows.
                int observation=0;
                foreach (var c in progress.guideProgress?.runHistory ?? new List<PlayedEncounterCardRecord>())
                {
                    observation++;
                    if(c?.card == null) continue;
                    var known=progress.storyEvents?.FirstOrDefault(e => e.characterId==c.card.speakerId && e.placeId==c.placeId);
                    history.Add(new[]{run.ToString(),"",c.card.speakerId,known?.characterName,known?.terrainName,c.encounterPath,"dialogue","",c.card.text,"","","","","","","","","","",
                        "observed:"+observation+":"+c.placeId+":"+c.encounterPath+":"+c.cardIndex});
                }
                var request = storage.LoadRunData<LocalStoryRevisionRequest>(run,"LocalStoryRevisionRequest");
                if(request != null)
                {
                    var results=request.generationResults??new List<LocalStoryRuntimeResult>();
                    if(results.Count==0) generation.Add(new[]{request.sourceRun.ToString(),request.targetRun.ToString(),request.chapterId,"","",request.status,request.generationError,request.reason,"LocalStoryTests/Index.html"});
                    foreach(var r in results) generation.Add(new[]{request.sourceRun.ToString(),request.targetRun.ToString(),request.chapterId,r.storyPath,r.model,r.status,r.error,request.reason,r.reportPath});
                }
            }
            // Copy-on-write keeps all editable sheets, styles and other author content intact.
            File.Copy(file,temp,true);
            using (var zip = ZipFile.Open(temp,ZipArchiveMode.Update))
            { ReplaceRows(zip,"플레이기록",history); ReplaceRows(zip,"생성기록",generation); }
            File.Replace(temp,file,null);
        }
        catch(Exception e) { Debug.LogWarning("[Story] Excel 갱신 보류 (게임 JSON은 유지): "+e.Message); }
    }
    private static string Stats(int[] values) => values==null ? "" : string.Join(" / ",values);
    private static string Column(int index) { string value=""; for(index++;index>0;index=(index-1)/26) value=(char)('A'+(index-1)%26)+value; return value; }
    private static void ReplaceRows(ZipArchive zip, string name, List<string[]> rows)
    {
        string path=SheetPath(zip,name);
        var document=Read(zip,path); var data=document.Root.Element(Ns+"sheetData");
        var style=data.Elements(Ns+"row").FirstOrDefault(r => (int?)r.Attribute("r")==2)?.Elements(Ns+"c").Select(c => (string)c.Attribute("s")??"0").ToArray()??Array.Empty<string>();
        data.Elements(Ns+"row").Where(r => (int?)r.Attribute("r")!=1).Remove();
        for(int i=0;i<rows.Count;i++)
        {
            var row=new XElement(Ns+"row",new XAttribute("r",i+2),new XAttribute("ht",54),new XAttribute("customHeight",1));
            for(int j=0;j<rows[i].Length;j++) row.Add(new XElement(Ns+"c",new XAttribute("r",Column(j)+(i+2)),
                new XAttribute("s",j<style.Length?style[j]:"0"),new XAttribute("t","inlineStr"),
                new XElement(Ns+"is",new XElement(Ns+"t",new XAttribute(XNamespace.Xml+"space","preserve"),rows[i][j]??""))));
            data.Add(row);
        }
        string range="A1:"+Column(rows.Count>0?rows[0].Length-1:name=="플레이기록"?19:8)+Math.Max(2,rows.Count+1);
        document.Root.Element(Ns+"dimension")?.SetAttributeValue("ref",range);
        var filter=document.Root.Element(Ns+"autoFilter");
        if(filter==null) data.AddAfterSelf(new XElement(Ns+"autoFilter",new XAttribute("ref",range)));
        else filter.SetAttributeValue("ref",range);
        zip.GetEntry(path).Delete(); using var output=zip.CreateEntry(path).Open(); document.Save(output);
    }
}
