using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>NPC main, choice branches and hidden dialogue have separate sources; logical save paths remain stable.</summary>
public static class ChapterContentCatalog
{
    private static JObject catalog;
    private static readonly Dictionary<string,JObject> documents = new Dictionary<string,JObject>();
    private static readonly Dictionary<string,TextAsset> assets = new Dictionary<string,TextAsset>();
    private static readonly Dictionary<string,string> storyCache = new Dictionary<string,string>();
    private static readonly HashSet<string> CardFields = new HashSet<string>(new[] {
        "ID","type","eventIDstable","speakID","Text","guideAction","choiceKind","options","choiceActions"});
    private static readonly HashSet<string> ChoiceFields = new HashSet<string>(new[] {
        "choiceID","action","storyPath","relationship","relationshipDelta"});
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reset() { catalog=null; documents.Clear();storyCache.Clear();LocalStoryRewrite.ResetIdentifiers();NarrativeInteractions.ResetCache();LocalStoryPrompt.Reset(); foreach(var a in assets.Values) if(a!=null) {if(Application.isPlaying) UnityEngine.Object.Destroy(a);else UnityEngine.Object.DestroyImmediate(a);} assets.Clear(); }
    public static TextAsset Asset(string path)
    {
        if(assets.TryGetValue(path,out var cached) && cached!=null) return cached;
        // Unity may unload synthetic TextAssets after a build or scene unload.
        assets.Remove(path);
        const string prefix="Story_Json_Data/";
        if(!path.StartsWith(prefix,StringComparison.Ordinal)) return Resources.Load<TextAsset>(path);
        string key=path.Substring(prefix.Length), json=null;
        if(key.StartsWith("Characters/",StringComparison.Ordinal)) json=CharacterData(key.Substring(11))?.ToString();
        else if(key.EndsWith("/Story",StringComparison.Ordinal)) json=Story(key.Substring(0,key.Length-6));
        else json=Resource(key);
        if(json==null) return Resources.Load<TextAsset>(path);
        var asset=new TextAsset(json); assets.Add(path,asset); return asset;
    }
    private static JObject Document(string resource)
    {
        if(documents.TryGetValue(resource,out var value)) return value;
        var asset=Resources.Load<TextAsset>("Chapters/"+resource);
        if(asset==null) throw new InvalidDataException("챕터 자료가 없습니다: "+resource);
        value=JObject.Parse(asset.text); documents.Add(resource,value); return value;
    }
    private static JObject Catalog => catalog ??= Document("Catalog");
    public static IEnumerable<JObject> Chapters => ((JArray)Catalog["chapters"]).OfType<JObject>();
    public static IEnumerable<JObject> Stories => ((JArray)Catalog["stories"]).OfType<JObject>();
    public static JObject Info(string path) => Stories.FirstOrDefault(e=>(string)e["path"]==path);
    public static int CardId(string path,int index)
    {
        var info=Info(path);return (int)Pack(info,false)[(string)info["key"]]["MainStory"][index]["ID"];
    }
    public static string ChapterFolder(string chapter) => (string)Chapters.FirstOrDefault(c=>(string)c["id"]==chapter)?["folder"] ?? chapter;
    public static string EpisodeFolder(string episode) => episode.StartsWith("Initial",StringComparison.Ordinal) ? episode.Replace("Initial","Chapter_00") :
        System.Text.RegularExpressions.Regex.Replace(episode,@"^Chapter_(\d+)_",m=>"Chapter_"+int.Parse(m.Groups[1].Value).ToString("D2")+"_");
    public static string SourceFile(JObject info,bool explanation=false)
    {
        string file=(string)info?[explanation?"explanationFile":"storyFile"];
        if(string.IsNullOrWhiteSpace(file) || file.StartsWith("/",StringComparison.Ordinal) || file.Contains("..") ||
            file.IndexOfAny(new[]{'\\',':'})>=0 || !file.EndsWith(".json",StringComparison.Ordinal))
            throw new InvalidDataException("이야기 원본 경로를 확인하세요: "+info?["path"]);
        return file;
    }
    private static JObject Pack(JObject info,bool explanation) => Document(SourceFile(info,explanation).Substring(0,SourceFile(info,explanation).Length-5));
    public static JObject Explanation(string path)
    {
        var info=Info(path); return info==null?null:Pack(info,true)[(string)info["key"]] as JObject;
    }
    public static string ResolveStoryPath(string path,int run)
    {
        var seen=new HashSet<string>();
        while(!string.IsNullOrEmpty(path) && seen.Add(path))
        {
            var scene=Explanation(path)?["coreMeaning"]?["scene"] as JObject;
            string repeat=(string)scene?["repeatStoryPath"];
            if(string.IsNullOrEmpty(repeat) || run<((int?)scene["repeatFromRun"]??2)) return path;
            var target=Info(repeat);var source=Info(path);
            if(target==null || (string)target["characterId"]!=(string)source["characterId"] ||
                (string)target["episodeId"]!=(string)source["episodeId"])
                throw new InvalidDataException("반복 회차 이야기 연결 오류: "+path);
            path=repeat;
        }
        if(string.IsNullOrEmpty(path)) return path;
        throw new InvalidDataException("반복 회차 이야기의 순환 연결: "+path);
    }
    public static string Story(string path)
    {
        if(storyCache.TryGetValue(path,out var cached)) return cached;
        var info=Info(path); if(info==null) return null;
        var source=Pack(info,false)[(string)info["key"]] as JObject;
        if(source==null || source.Properties().Any(p=>p.Name!="MainStory") || !(source["MainStory"] is JArray cards) || cards.Count==0) throw new InvalidDataException("이야기 묶음 속성/카드 오류: "+path);
        var explanation=Explanation(path);
        if(explanation==null || explanation.Properties().Any(p=>p.Name!="storyFlame" && p.Name!="coreMeaning" && p.Name!="paragraphs" && p.Name!="promptRules" && p.Name!="limits"))
            throw new InvalidDataException("이야기 설명 속성 오류: "+path);
        var actions=Document((string)info["chapterFolder"]+"/Terrain")["actions"]?[path];
        var result=new JArray(); var ids=new HashSet<int>();
        foreach(JObject card in cards)
        {
            if(card.Properties().Any(p=>!CardFields.Contains(p.Name)) || card["ID"]?.Type!=JTokenType.Integer || !ids.Add((int)card["ID"]))
                throw new InvalidDataException("허용되지 않은 이야기 속성 또는 문장 ID: "+path);
            string id=((int)card["ID"]).ToString();
            var line=new JObject();
            var meaning=explanation["coreMeaning"]?[id];
            foreach(var property in card.Properties())
            {
                if(property.Name=="ID") continue;
                string name=property.Name=="Text"?"text":property.Name=="eventIDstable"?"eventIdStable":property.Name=="speakID"?"speakerId":property.Name;
                line[name]=property.Value.DeepClone();
            }
            if(line["choiceActions"] is JArray choices)
                foreach(JObject choice in choices)
                {
                    if(choice.Properties().Any(p=>!ChoiceFields.Contains(p.Name))) throw new InvalidDataException("허용되지 않은 선택 속성: "+path);
                    Rename(choice,"choiceID","choiceId"); Rename(choice,"relationship","relationshipId");
                    var role=meaning?["choices"]?[(string)choice["choiceId"]??""]?["choiceRole"];
                    if(role?.Type==JTokenType.String) choice["choiceRole"]=role.DeepClone();
                    var extra=actions?[id]?["choiceRules"]?[(string)choice["choiceId"]??""] as JObject;
                    if(extra!=null) foreach(var p in extra.Properties()) choice[p.Name]=p.Value.DeepClone();
                }
            foreach(string field in new[]{"coreMeaning","semanticText"}) if(meaning?[field]!=null) line[field]=meaning[field].DeepClone();
            if(actions?[id] is JObject runtime)
                foreach(var p in runtime.Properties().Where(p=>p.Name!="choiceRules")) line[p.Name]=p.Value.DeepClone();
            result.Add(line);
        }
        var root=new JObject { ["MainStory"]=result };
        if(explanation["storyFlame"]?.Type==JTokenType.Object) root["storyFrame"]=explanation["storyFlame"].DeepClone();
        string json=root.ToString(Newtonsoft.Json.Formatting.None);storyCache[path]=json;return json;
    }
    private static void Rename(JObject obj,string from,string to) { if(obj[from]!=null) {obj[to]=obj[from];obj.Remove(from);} }
    public static string Resource(string path)
    {
        if(path=="Omnibus_01") return Document("Omnibus").ToString();
        var parts=path.Split('/');
        if(parts.Length==3 && (parts[2]=="Terrain" || parts[2]=="Transform"))
            return Document(ChapterFolder(parts[0])+"/"+parts[2])["episodes"]?[parts[1]]?.ToString();
        return null;
    }
    public static JObject CharacterData(string npc) => Chapters.SelectMany(c=>(JArray)Document((string)c["folder"]+"/Characters")["characters"])
        .OfType<JObject>().FirstOrDefault(c=>(string)c["characterId"]==npc);
    public static StoryCharacter Character(string npc) => CharacterData(npc)?.ToObject<StoryCharacter>();
    public static IEnumerable<JObject> Characters(string chapter) => ((JArray)Document(ChapterFolder(chapter)+"/Characters")["characters"]).OfType<JObject>();
    public static NarrativeChapterInteractions Chapter(string id)
    {
        var chapter=Chapters.FirstOrDefault(c=>(string)c["id"]==id); if(chapter==null) return null;
        var terrain=Document((string)chapter["folder"]+"/Terrain");
        var guides=new List<NarrativeWritingGuide>(); var scenes=new List<NarrativeScene>();
        foreach(var info in Stories.Where(s=>(string)s["chapterId"]==id))
        {
            string path=(string)info["path"]; var explanation=Explanation(path);
            var frames=new List<NarrativeWritingFrame>();
            var source=JObject.Parse(Story(path));
            var authored=(JArray)Pack(info,false)[(string)info["key"]]["MainStory"];
            foreach(JObject card in authored)
            {
                var meaning=explanation["coreMeaning"]?[(string)card["ID"]];
                if(meaning?["purpose"]==null) continue;
                frames.Add(new NarrativeWritingFrame {eventId=(string)card["eventIDstable"],purpose=(string)meaning["purpose"],
                    currentState=(string)meaning["currentState"],forbiddenMentions=meaning["forbiddenMentions"]?.ToObject<string[]>()});
            }
            guides.Add(new NarrativeWritingGuide {storyPath=path,characterId=(string)info["characterId"],
                facts=explanation["coreMeaning"]?["scene"]?["facts"]?.ToObject<string[]>(),frames=frames.ToArray(),
                paragraphs=explanation["paragraphs"]?.ToObject<NarrativeWritingParagraph[]>(),
                promptRules=explanation["promptRules"]?.ToObject<string[]>(),limits=explanation["limits"]?.ToObject<NarrativeWritingLimits>()});
            if(info["sceneId"]!=null) scenes.Add(new NarrativeScene {storyPath=path,sceneId=(string)info["sceneId"],
                characterId=(string)info["characterId"],sectionId=(string)info["sectionId"],MainStory=JsonUtility.FromJson<EncounterCardRoot>(source.ToString()).MainStory});
        }
        return new NarrativeChapterInteractions {chapterId=id,generationCardsPerSection=2,writingGuides=guides.ToArray(),scenes=scenes.ToArray(),
            interactions=terrain["interactions"]?.ToObject<NarrativeInteractionRule[]>()??Array.Empty<NarrativeInteractionRule>()};
    }
    public static LocalStoryWritingBrief Brief(string path)
    {
        var e=Explanation(path); string style=(string)e?["coreMeaning"]?["scene"]?["style"];
        if(string.IsNullOrEmpty(style)) return null;
        var root=JsonUtility.FromJson<EncounterCardRoot>(Story(path));
        return new LocalStoryWritingBrief {storyPath=path,style=style,eventOrder=root.MainStory.Select(c=>c.eventIdStable).ToArray(),
            choiceReferences=References(path).ToArray()};
    }
    public static IEnumerable<LocalStoryChoiceReference> References(string path=null)
    {
        foreach(var info in Stories.Where(i=>path==null || (string)i["path"]==path))
            foreach(var m in ((JObject)Explanation((string)info["path"])["coreMeaning"]).Properties())
                if(m.Value["choices"] is JObject choices)
                    foreach(var c in choices.Properties())
                        if(!string.IsNullOrWhiteSpace((string)c.Value["text"]))
                        {
                            var reference=c.Value.ToObject<LocalStoryChoiceReference>();
                            reference.choiceId ??= c.Name;yield return reference;
                        }
    }
    public static string Setting(string reference,string[] earned)
    {
        string clean=System.Text.RegularExpressions.Regex.Replace(reference??"",@"(?m)^\s*\d+[.)]\s*","").Trim();
        if(clean.Contains("\n")) return string.Join("\n",clean.Split(new[]{'\n','\r'},StringSplitOptions.RemoveEmptyEntries).Select(line=>Setting(line.Trim(),earned)).Where(s=>!string.IsNullOrEmpty(s)));
        reference=clean;
        if(string.IsNullOrEmpty(reference) || !reference.StartsWith("@npc:",StringComparison.Ordinal)) return reference??"";
        string[] parts=reference.Substring(5).Split('/'); var npc=CharacterData(parts[0]);
        if(npc==null) throw new InvalidDataException("NPC 설정 참조 오류: "+reference);
        if(parts.Length==2 && parts[1]=="public")
        {
            var facts=npc["publicFacts"]?.Values<string>().ToArray()??Array.Empty<string>();
            return facts.Length>0?string.Join("\n",facts):(string)npc["role"]??"";
        }
        if(parts.Length==3 && parts[1]=="hidden")
        {
            var h=npc["hidden"]?[parts[2]]; if(h==null) throw new InvalidDataException("숨은 설정 참조 오류: "+reference);
            var needs=h["requiresUnlock_ALL"]?.Values<string>().ToArray()??Array.Empty<string>();
            return needs.Length>0 && needs.All(id=>(earned??Array.Empty<string>()).Contains(id))?(string)h["text"]:"";
        }
        throw new InvalidDataException("설정 참조 형식 오류: "+reference);
    }
}
