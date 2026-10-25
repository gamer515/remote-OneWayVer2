using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>Data-only writing instructions. No NPC, scene, example or writing prose belongs here.</summary>
public static class LocalStoryPromptModules
{
    private static readonly Dictionary<string,JObject> documents=new Dictionary<string,JObject>();
    public static void Reset() => documents.Clear();
    public static JObject Read(string resource)
    {
        if(string.IsNullOrWhiteSpace(resource) || !resource.StartsWith("LocalStory/",StringComparison.Ordinal) || resource.Contains(".."))
            throw new InvalidDataException("Invalid prompt module resource: "+resource);
        if(documents.TryGetValue(resource,out var cached)) return cached;
        var asset=Resources.Load<TextAsset>(resource);
        if(asset==null) throw new InvalidDataException("Missing prompt JSON: "+resource);
        var value=JObject.Parse(asset.text);documents.Add(resource,value);return value;
    }
    public static IEnumerable<(string resource,JObject data)> All()
    {
        var paths=LocalStoryPrompt.Policy["modules"] as JArray;
        if(paths==null || paths.Count==0 || paths.Values<string>().Distinct().Count()!=paths.Count)
            throw new InvalidDataException("PromptPolicy.modules must contain unique resource paths.");
        var ids=new HashSet<string>();
        foreach(string path in paths.Values<string>())
        {
            var data=Read(path);
            if(string.IsNullOrWhiteSpace((string)data["id"]) || !ids.Add((string)data["id"]) ||
                !(data["rules"] is JArray rules) || rules.Any(r=>r.Type!=JTokenType.String || string.IsNullOrWhiteSpace((string)r)) ||
                !(data["when"] is JObject)) throw new InvalidDataException("Invalid prompt module: "+path);
            yield return (path,data);
        }
    }
    public static List<(string resource,JObject data)> Select(JObject context) => All().Where(m=>
        ((JObject)m.data["when"]).Properties().All(p=>context[p.Name]!=null && JToken.DeepEquals(context[p.Name],p.Value))).ToList();
    public static string Hash => LocalStoryDialogueQueue.Hash(LocalStoryPrompt.Policy.ToString(Formatting.None)+"\n"+
        string.Join("\n",All().Select(m=>m.resource+"\n"+m.data.ToString(Formatting.None))));
    public static string Rules(IEnumerable<(string resource,JObject data)> modules) =>
        string.Join("\n",modules.SelectMany(m=>m.data["rules"].Values<string>()));
    public static JArray Examples(List<(string resource,JObject data)> modules,JObject context)
    {
        int max=(int?)LocalStoryPrompt.Policy["composition"]?["maxExamples"]??0;
        if(max<0 || max>4) throw new InvalidDataException("Prompt example limit must be 0..4.");
        var result=new JArray();
        foreach(var sample in modules.SelectMany(m=>(m.data["examples"] as JArray)??new JArray())
            .Where(e=>!(e["when"] is JObject when) || when.Properties().All(p=>JToken.DeepEquals(context[p.Name],p.Value))).Take(max))
        {
            if(!(sample["input"] is JObject) || !(sample["output"]?["cards"] is JArray))
                throw new InvalidDataException("Prompt example requires input/output.cards.");
            var reference=(JObject)sample.DeepClone();reference.Remove("when");result.Add(reference);
        }
        return result;
    }
    public static JObject Context(JObject variable) => new JObject {
        ["task"]=variable["repair"]==null?"draft":"correction",
        ["hasChoices"]=variable["cards"].Any(c=>c["options"].Any()),
        ["speechMode"]=(string)variable["cards"][0]["speechMode"],
        ["memoryMode"]=(string)variable["memory"]["mode"],
        ["hasFixedStrings"]=variable["cards"].Any(c=>c["fixedStrings"]?.Any()==true),
        ["hasUnlockedFacts"]=(bool?)variable["hasUnlockedFacts"]??false };
    public static string[] Sources(string messages)
    {
        var input=JArray.Parse(messages);
        string content=(string)input.Last["content"];
        // The final adapter can merge system/user; find the request JSON by its cards property.
        foreach(string line in content.Split('\n').Reverse())
            if(line.StartsWith("{",StringComparison.Ordinal))
            {
                JObject request;try{request=JObject.Parse(line);}catch(JsonException){continue;}
                if(request["cards"] is JArray && request["memory"] is JObject)
                    return Select(Context(request)).Select(m=>m.resource).ToArray();
            }
        throw new InvalidDataException("Prompt composition request not found.");
    }
    public static bool ValidateExampleCopies(EncounterCardRoot source,EncounterCardRoot generated,string messages,out string error)
    {
        error=null;
        if((bool?)LocalStoryPrompt.Policy["composition"]["rejectExampleCopies"]!=true)return true;
        var input=JArray.Parse(messages);JArray examples=null;
        foreach(string line in ((string)input.Last["content"]).Split('\n').Reverse())
            if(line.StartsWith("{",StringComparison.Ordinal))
            {
                JObject request;try{request=JObject.Parse(line);}catch(JsonException){continue;}
                if(request["cards"] is JArray) {examples=request["examples"] as JArray;break;}
            }
        if(examples==null)return true;
        IEnumerable<string> Phrases(EncounterCardRoot root)=>root.MainStory.SelectMany(c=>new[]{c.text}.Concat(c.options??Array.Empty<string>())).Where(s=>!string.IsNullOrWhiteSpace(s)).Select(s=>s.Trim());
        var authored=new HashSet<string>(Phrases(source));
        var samples=new HashSet<string>(examples.SelectMany(e=>e["output"]["cards"])
            .SelectMany(c=>new[]{(string)c["text"]}.Concat((c["options"] as JArray??new JArray()).Values<string>()))
            .Where(s=>!string.IsNullOrWhiteSpace(s)).Select(s=>s.Trim()).Where(s=>!authored.Contains(s)));
        if(Phrases(generated).Any(samples.Contains)) {error="example-copy";return false;}
        return true;
    }
}
