using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>Shared authoring schema and prompt assembly. Story content is read from Resources, never selected by NPC name.</summary>
public static class LocalStoryPrompt
{
    private static JObject policy;
    public static void Reset() { policy=null; LocalStoryPromptModules.Reset(); }
    public static JObject Policy
    {
        get
        {
            if(policy!=null) return policy;
            var asset=Resources.Load<TextAsset>("LocalStory/PromptPolicy");
            if(asset==null) throw new InvalidDataException("공통 집필 규칙이 없습니다: LocalStory/PromptPolicy.json");
            var data=JObject.Parse(asset.text);
            if(!(data["modules"] is JArray modules) || modules.Count==0 || !(data["composition"] is JObject) || !(data["participants"]?["player"] is JObject))
                throw new InvalidDataException("PromptPolicy modules/composition/participants를 확인하세요.");
            policy=data; return policy;
        }
    }
    public static string CommonRules => LocalStoryPromptModules.Rules(LocalStoryPromptModules.All().Where(m=>!((JObject)m.data["when"]).HasValues));
    public static string Hash => LocalStoryPromptModules.Hash;
    public static bool AllowFailedOutput => (bool?)Policy["runtime"]?["allowFailedOutput"]??false;
    public static NarrativeWritingLimits Limits(string path,NarrativeWritingParagraph paragraph=null)
    {
        var result=Policy["limits"].ToObject<NarrativeWritingLimits>();
        void Apply(NarrativeWritingLimits v)
        {
            if(v==null)return;
            if(v.maxCards>0)result.maxCards=v.maxCards;
            if(v.textMaxChars>0)result.textMaxChars=v.textMaxChars;
            if(v.optionMaxChars>0)result.optionMaxChars=v.optionMaxChars;
            if(v.maxOutputTokens>0)result.maxOutputTokens=v.maxOutputTokens;
        }
        Apply(LocalStorySections.WritingGuide(path)?.limits);Apply(paragraph?.limits);
        if(result.maxCards<1 || result.maxCards>16 || result.textMaxChars<1 || result.textMaxChars>160 ||
            result.optionMaxChars<1 || result.optionMaxChars>45 || result.maxOutputTokens<128 || result.maxOutputTokens>1800)
            throw new InvalidDataException("집필 limits 범위 오류: "+path);
        return result;
    }
    public static List<(int start,int count)> Plan(string path,EncounterCardRoot source)
    {
        var paragraphs=LocalStorySections.WritingGuide(path)?.paragraphs;
        if(paragraphs==null || paragraphs.Length==0)
            throw new InvalidDataException("Explanation의 paragraphs가 없습니다: "+path);
        var result=new List<(int start,int count)>();int offset=0;var ids=new HashSet<string>();
        foreach(var p in paragraphs)
        {
            if(p==null || string.IsNullOrWhiteSpace(p.id) || !ids.Add(p.id) || string.IsNullOrWhiteSpace(p.purpose) ||
                p.eventIds==null || p.eventIds.Length==0 || p.currentState==null || p.currentState.Length==0 || p.currentState.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("단락 ID/목적/상황/이벤트를 확인하세요: "+path);
            for(int i=0;i<p.eventIds.Length;i++)
                if(offset+i>=source.MainStory.Count || p.eventIds[i]!=EventId(path,source.MainStory[offset+i],offset+i))
                    throw new InvalidDataException("단락은 Story의 모든 카드를 순서대로 한 번씩 포함해야 합니다: "+path+" / "+p.id);
            var slice=LocalStorySections.Slice(source,offset,p.eventIds.Length);
            foreach(var range in LocalStorySections.Plan(slice,Limits(path,p).maxCards))result.Add((offset+range.start,range.count));
            offset+=p.eventIds.Length;
        }
        if(offset!=source.MainStory.Count)throw new InvalidDataException("단락에서 빠진 Story 카드가 있습니다: "+path);
        return result;
    }
    public static string EventId(string path,EncounterCard card,int index) => string.IsNullOrEmpty(card.eventIdStable)?"card:"+ChapterContentCatalog.CardId(path,index):card.eventIdStable;
    public static NarrativeWritingParagraph Paragraph(string path,EncounterCard card,int index) =>
        LocalStorySections.WritingGuide(path)?.paragraphs?.SingleOrDefault(p=>p.eventIds?.Contains(EventId(path,card,index))==true);
    public static int MaxTokens(string path,EncounterCardRoot source,int start,int count)
    {
        var limit=Limits(path,Paragraph(path,source.MainStory[start],start));
        var budget=Policy["composition"]["tokenBudget"];
        return Math.Min(limit.maxOutputTokens,(int)budget["base"]+count*(int)budget["perCard"]+
            source.MainStory.Skip(start).Take(count).Sum(c=>c.options?.Length??0)*(int)budget["perOption"]);
    }
    private static string Short(string value,int max) => string.IsNullOrEmpty(value)?value:value.Substring(0,Math.Min(max,value.Length));
    private static JArray Strings(IEnumerable<string> values) => new JArray((values??Array.Empty<string>()).Where(v=>!string.IsNullOrWhiteSpace(v)).Distinct());
    private static JObject Character(string id,LocalStoryRevisionRequest memory)
    {
        var definition=ChapterContentCatalog.CharacterData(id)??throw new InvalidDataException("인물 설정 누락: "+id);
        var configured=memory?.npcContexts?.FirstOrDefault(c=>c.characterId==id);
        var earned=new HashSet<string>(configured?.unlockedIds??memory?.earnedUnlocks??Array.Empty<string>());
        var items=new HashSet<string>(memory?.ownedItemIds??Array.Empty<string>());
        var facts=new List<string>();
        foreach(var h in (definition["hidden"] as JObject)?.Properties()??Enumerable.Empty<JProperty>())
        {
            var unlocks=h.Value["requiresUnlock_ALL"]?.Values<string>().ToArray()??Array.Empty<string>();
            var requiresItems=h.Value["requiresItems_ALL"]?.Values<string>().ToArray()??Array.Empty<string>();
            if((unlocks.Length>0 || requiresItems.Length>0) && unlocks.All(earned.Contains) && requiresItems.All(items.Contains))
                facts.Add((string)h.Value["text"]);
        }
        return new JObject { ["id"]=id,["name"]=configured?.name??(string)definition["name"],
            ["role"]=(string)definition["role"],["personality"]=(string)definition["personality"],
            ["speechStyle"]=(string)definition["speechStyle"],
            ["publicFacts"]=Strings(definition["publicFacts"]?.Values<string>()),["unlockedFacts"]=Strings(facts) };
    }
    private static JObject Memory(LocalStoryRevisionRequest memory,HashSet<string> speakers)
    {
        var data=new JObject { ["mode"]=string.IsNullOrWhiteSpace(memory?.memoryMode)?"None":memory.memoryMode };
        if(memory?.memoryMode=="Dream")
            data["impressions"]=Strings((memory.dreamMemories??new List<LocalStoryDreamMemory>()).Where(m=>speakers.Contains(m.characterId))
                .Select(m=>m.characterId+": "+m.impression).Reverse().Distinct().Take(8).Reverse());
        else
        {
            var events=(memory?.events??new List<StoryEventRecord>()).Where(e=>e.chapterId==memory.chapterId && speakers.Contains(e.characterId) &&
                (e.kind=="choice" || e.relationshipDelta!=0 || !string.IsNullOrEmpty(e.itemId) || e.kind?.Contains("result")==true)).ToArray();
            if(events.Length>0 && string.IsNullOrEmpty(memory.memoryMode))data["mode"]="CurrentRun";
            data["facts"]=new JArray(events.Skip(Math.Max(0,events.Length-4)).Select(e=>new JObject {
                ["characterId"]=e.characterId,["utterance"]=Short(e.semanticText,100),["result"]=e.reason,
                ["relationshipDelta"]=e.relationshipDelta }));
        }
        return data;
    }
    public static string Messages(string path,EncounterCardRoot original,int start,int count,List<EncounterCard> accepted,
        LocalStoryRevisionRequest memory,string draft=null,string error=null)
    {
        if(original?.MainStory==null || start<0 || count<1 || start+count>original.MainStory.Count)throw new ArgumentException("집필 대상 범위 오류");
        var guide=LocalStorySections.WritingGuide(path);var p=Paragraph(path,original.MainStory[start],start);
        var speakers=original.MainStory.Skip(start).Take(count).Select(c=>c.speakerId).Where(s=>!string.IsNullOrWhiteSpace(s)).ToHashSet();
        var explanation=ChapterContentCatalog.Explanation(path);
        var scene=new JObject { ["purpose"]=(string)(explanation?["storyFlame"] as JObject)?["purpose"],
            ["facts"]=Strings(original.storyFrame?.worldFacts?.Length>0?original.storyFrame.worldFacts:guide?.facts),
            ["style"]=(string)explanation?["coreMeaning"]?["scene"]?["style"],["rules"]=Strings(guide?.promptRules) };
        bool reunion=memory?.memoryMode!="Dream" && (memory?.events??new List<StoryEventRecord>()).Any(e=>e.chapterId==memory.chapterId&&speakers.Contains(e.characterId));
        if((bool?)Policy["composition"]["includeSceneFlow"]==true)
            scene["flow"]=Strings(reunion?original.storyFrame?.reunionFlow:original.storyFrame?.firstMeetingFlow);
        var stable=new JObject { ["characters"]=new JArray(speakers.OrderBy(s=>s).Select(s=>Character(s,memory))),["scene"]=scene };
        var paragraphData=new JArray((guide?.paragraphs??Array.Empty<NarrativeWritingParagraph>()).Where(part=>Enumerable.Range(start,count).Any(i=>part.eventIds.Contains(EventId(path,original.MainStory[i],i))))
            .Select(part=>new JObject {["purpose"]=part.purpose,["currentState"]=Strings(part.currentState)}));
        var variable=new JObject { ["paragraphs"]=paragraphData,
            ["position"]=new JObject { ["start"]=start+1,["count"]=count,["total"]=original.MainStory.Count },
            ["limits"]=JObject.FromObject(Limits(path,p)),["memory"]=Memory(memory,speakers) };
        bool garbled=!string.IsNullOrWhiteSpace(original.MainStory[start].semanticText);
        variable["previousDialogue"]=new JArray(accepted.Where(c=>!string.IsNullOrWhiteSpace(c.semanticText)==garbled).Reverse().Take((int)Policy["composition"]["previousDialogueCards"]).Reverse()
            .Select(c=>new JObject { ["speakerId"]=c.speakerId,["text"]=Short(c.text,160) }));
        variable["previousEvents"]=Strings(accepted.Where(c=>!string.IsNullOrWhiteSpace(c.coreMeaning)).Reverse().Take((int)Policy["composition"]["previousEvents"]).Reverse().Select(c=>Short(c.coreMeaning,140)));
        var references=ChapterContentCatalog.References(path).ToDictionary(r=>r.choiceId);
        var cards=new JArray();
        for(int i=0;i<count;i++)
        {
            var c=original.MainStory[start+i];var frame=guide?.frames?.SingleOrDefault(f=>f.eventId==c.eventIdStable);
            var meaning=explanation?["coreMeaning"]?[ChapterContentCatalog.CardId(path,start+i).ToString()];
            var speaker=ChapterContentCatalog.CharacterData(c.speakerId);
            var player=Policy["participants"]["player"];
            var card=new JObject { ["index"]=i,["type"]=c.type,["speakerId"]=c.speakerId,["reference"]=c.text,["coreMeaning"]=c.coreMeaning,
                ["speaker"]=(string)speaker?["name"],["listener"]=player["name"].DeepClone(),
                ["purpose"]=frame?.purpose,["currentState"]=frame?.currentState,
                ["speechMode"]=string.IsNullOrWhiteSpace(c.semanticText)?"normal":"garbled",["semanticText"]=c.semanticText,
                ["fixedStrings"]=Strings(Regex.Matches(c.text??"",@"\{[^{}]*\}|\d+").Cast<Match>().Select(m=>m.Value)) };
            if(meaning?["participants"] is JObject participants)
                foreach(var part in participants.Properties()) card[part.Name]=part.Value.DeepClone();
            foreach(string field in new[]{"stateBefore","stateAfter","dialogueTiming"})
                if(meaning?[field]!=null)card[field]=meaning[field].DeepClone();
            if(card["fixedStrings"] is JArray fixedValues && fixedValues.Count==0)card.Remove("fixedStrings");
            if(card["semanticText"]?.Type==JTokenType.Null)card.Remove("semanticText");
            if(!string.IsNullOrWhiteSpace(c.coreMeaning))card.Remove("reference");
            if(card["stateBefore"]!=null && card["stateAfter"]!=null)card.Remove("currentState");
            var options=new JArray();
            for(int j=0;j<(c.options?.Length??0);j++)
            {
                var action=c.choiceActions?[j];LocalStoryChoiceReference reference=null;
                if(action?.choiceId!=null)references.TryGetValue(action.choiceId,out reference);
                string intent=(string)meaning?["choices"]?[action?.choiceId??""]?["intent"]??reference?.text??c.options[j];
                options.Add(new JObject { ["index"]=j,["speaker"]=player["name"].DeepClone(),["listener"]=(string)speaker?["name"],["intent"]=intent,
                    ["reference"]=string.IsNullOrWhiteSpace(c.semanticText)?reference?.text??c.options[j]:reference?.heardText??c.options[j] });
            }
            card["options"]=options;cards.Add(card);
        }
        variable["cards"]=cards;
        if(draft!=null)variable["repair"]=new JObject { ["draft"]=draft,["error"]=error };
        variable["hasUnlockedFacts"]=stable["characters"].Any(c=>c["unlockedFacts"].Any());
        var context=LocalStoryPromptModules.Context(variable);
        var selected=LocalStoryPromptModules.Select(context);
        var examples=LocalStoryPromptModules.Examples(selected,context);
        if(examples.Count>0)variable["examples"]=examples;
        // Examples/context precede the actual target. All prose describing these fields lives in JSON.
        variable.Property("cards").Remove();variable["cards"]=cards;
        string user=stable.ToString(Formatting.None)+"\n"+variable.ToString(Formatting.None);
        string system=LocalStoryPromptModules.Rules(selected);
        return new JArray(new JObject { ["role"]="system",["content"]=system },new JObject { ["role"]="user",["content"]=user }).ToString(Formatting.None);
    }
}
