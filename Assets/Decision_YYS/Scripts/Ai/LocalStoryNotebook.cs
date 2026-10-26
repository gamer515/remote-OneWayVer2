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
    public int[] stats;
    public string[] unlockedIds;
}

/// <summary>NPC controls and JSON-derived unlock/reward views. Play and generation state stay in JSON.</summary>
public static partial class LocalStoryNotebook
{
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    public static string PathFor(SaveIOService storage) => ChapterPath(storage,"Initial");
    private static void Ensure(SaveIOService storage)
    {
        EnsureChapterBooks(storage);
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
            CachedProgress(p).unlockedIds ?? new List<string>()).Distinct().OrderBy(s => s).ToArray();
    private static string[] ProgressFiles(SaveIOService storage)
    {
        string runs = Path.Combine(storage.StorageRoot,"Runs");
        return Directory.Exists(runs) ? Directory.GetFiles(runs,"Progress.json",SearchOption.AllDirectories).OrderBy(RunNumber).ToArray() : Array.Empty<string>();
    }
    private static int RunNumber(string file) => int.TryParse(Path.GetFileName(Path.GetDirectoryName(file)).Replace("Run_",""),out int run) ? run : 0;
    private static string[] Ids(string text) => (text ?? "").Split(new[]{';',',','\n','\r'},StringSplitOptions.RemoveEmptyEntries)
        .Select(v => System.Text.RegularExpressions.Regex.Replace(v.Trim(),@"^\d+[.)]\s*","")).Where(v => v.Length > 0).ToArray();
    public static string Numbered(string text) => Numbered((text??"").Split(new[]{'\n','\r'},StringSplitOptions.RemoveEmptyEntries));
    public static string Numbered(IEnumerable<string> values) => string.Join("\n",values.Where(v=>!string.IsNullOrWhiteSpace(v))
        .Select(v=>System.Text.RegularExpressions.Regex.Replace(v.Trim(),@"^\d+[.)]\s*","")).Select((v,i)=>(i+1)+". "+v));
    public static LocalStoryNpcContext[] Contexts(SaveIOService storage, string[] earned)
    {
        return ConfiguredContexts(storage,earned);
    }
    public static LocalStoryNpcContext[] ReadContexts(string file, string[] earned)
    {
        return ReadChapterContexts(file,earned);
    }
    public static string ResolveOpeningPath(SaveIOService storage, string npc, string original)
    {
        return original; // Authored JSON owns story routing.
    }
    public static bool IsStoryPath(string path) => !string.IsNullOrWhiteSpace(path) && path.IndexOfAny(new[]{'.','\\',':'}) < 0 &&
        NarrativeInteractions.StorySource(path) != null;
    public static string PromptContext(LocalStoryRevisionRequest request, string npc)
    {
        var c = request.npcContexts?.FirstOrDefault(p => p.characterId == npc);
        if (c == null) return "";
        var facts=new Newtonsoft.Json.Linq.JObject { ["name"]=c.name,["role"]=c.role,["personality"]=c.personality,["publicFacts"]=c.publicFacts,["revealedPast"]=c.revealedPast };
        return "\n[인물 설정 — 대사에 그대로 출력하지 않을 참고 자료]\n"+facts.ToString(Newtonsoft.Json.Formatting.Indented)+
            "\n해금 조건을 통과한 설정만 포함했다. 내부 해금 ID를 추측하거나 대사로 읽지 않는다."+
            "\n핵심 대사도 같은 사건 의미로 새로 표현한다. 게임 기능/보상/분기는 만들거나 변경하지 않는다.";
    }
    public static void Refresh(SaveIOService storage)
    {
        EnsureChapterBooks(storage);
    }
    private static string Column(int index) { string value=""; for(index++;index>0;index=(index-1)/26) value=(char)('A'+(index-1)%26)+value; return value; }
    private static void ReplaceRows(ZipArchive zip, string name, List<string[]> rows)
    {
        string path=SheetPath(zip,name);
        var document=Read(zip,path); var data=document.Root.Element(Ns+"sheetData");
        var shared=zip.GetEntry("xl/sharedStrings.xml")==null?Array.Empty<string>():Read(zip,"xl/sharedStrings.xml").Descendants(Ns+"si").Select(s=>string.Concat(s.Descendants(Ns+"t").Select(t=>t.Value))).ToArray();
        var headers=data.Elements(Ns+"row").First().Elements(Ns+"c").Select(c=>(string)c.Attribute("t")=="s"?shared[(int)c.Element(Ns+"v")]:
            (string)c.Attribute("t")=="inlineStr"?string.Concat(c.Descendants(Ns+"t").Select(t=>t.Value)):(string)c.Element(Ns+"v")??"").ToArray();
        var numeric=new HashSet<string>(new[]{"자동생성","해금상태","번호","회차","순서","이전회차","다음회차","청크","체력","속도","지능","매력","관계변화","수량"});
        var style=data.Elements(Ns+"row").FirstOrDefault(r => (int?)r.Attribute("r")==2)?.Elements(Ns+"c").Select(c => (string)c.Attribute("s")??"0").ToArray()??Array.Empty<string>();
        data.Elements(Ns+"row").Where(r => (int?)r.Attribute("r")!=1).Remove();
        for(int i=0;i<rows.Count;i++)
        {
            double Width(int column)=>(double?)document.Root.Element(Ns+"cols")?.Elements(Ns+"col").FirstOrDefault(c=>(int)c.Attribute("min")<=column+1&&(int)c.Attribute("max")>=column+1)?.Attribute("width")??25;
            int lines=rows[i].Select((v,j)=>(v??"").Split('\n').Sum(line=>Math.Max(1,(int)Math.Ceiling(line.Sum(c=>c>255?2.0:1.0)/Math.Max(8,Width(j)-4))))).DefaultIfEmpty(1).Max();
            var row=new XElement(Ns+"row",new XAttribute("r",i+2),new XAttribute("ht",Math.Min(409,Math.Max(44,lines*18+20))),new XAttribute("customHeight",1));
            for(int j=0;j<rows[i].Length;j++)
            {
                var cell=new XElement(Ns+"c",new XAttribute("r",Column(j)+(i+2)),new XAttribute("s",j<style.Length?style[j]:"0"));
                if(j<headers.Length && numeric.Contains(headers[j]) && int.TryParse(rows[i][j],out int value)) cell.Add(new XElement(Ns+"v",value));
                else {cell.SetAttributeValue("t","inlineStr");cell.Add(new XElement(Ns+"is",new XElement(Ns+"t",new XAttribute(XNamespace.Xml+"space","preserve"),rows[i][j]??"")));}
                row.Add(cell);
            }
            data.Add(row);
        }
        string range="A1:"+Column(rows.Count>0?rows[0].Length-1:data.Elements(Ns+"row").First().Elements(Ns+"c").Count()-1)+Math.Max(2,rows.Count+1);
        document.Root.Element(Ns+"dimension")?.SetAttributeValue("ref",range);
        var filter=document.Root.Element(Ns+"autoFilter");
        if(filter==null) data.AddAfterSelf(new XElement(Ns+"autoFilter",new XAttribute("ref",range)));
        else filter.SetAttributeValue("ref",range);
        zip.GetEntry(path).Delete(); using var output=zip.CreateEntry(path).Open(); document.Save(output);
    }
}
