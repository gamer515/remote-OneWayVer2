using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Isolated logical checks only. No actual player save, Edit, scene or model generation changes.
public static class LocalStoryChoiceOrderCheck
{
    static int checks;
    static void Check(bool condition,string error) { if(!condition) throw new Exception(error); checks++; }
    static EncounterCardRoot Read(string path) => JsonUtility.FromJson<EncounterCardRoot>(
        Resources.Load<TextAsset>("Story_Json_Data/"+path+"/Story").text);
    static EncounterCardRoot Clone(EncounterCardRoot root) => JsonUtility.FromJson<EncounterCardRoot>(JsonUtility.ToJson(root));
    static string Patch(EncounterCardRoot root) => new JObject {["cards"]=new JArray(root.MainStory.Select((c,i)=>new JObject {
        ["index"]=i,["text"]=c.text,["options"]=new JArray(c.options??Array.Empty<string>())}))}.ToString();
    static StoryEventRecord Event(string id,int slot) => new StoryEventRecord { kind="choice",eventKey=id,choiceId=id,
        characterId="Guide",storyPath=LocalStoryTestInputs.GuidePath,choiceSlot=slot,relationshipId="Guide" };
    public static string Regression()
    {
        checks=0;
        var source=Read(LocalStoryTestInputs.GuidePath);
        string before=JsonUtility.ToJson(source);
        var attitude=source.MainStory[8];
        Check(attitude.choiceKind=="Attitude" && source.MainStory[4].choiceKind=="Mechanic","guide types missing");
        Check(attitude.options.SequenceEqual(new[]{"진짜 신이구나. 신기하네!","왜 이 광산에 있는 거야?","관심없어.","꺼져!"}),"actual original wording/order wrong");
        Check(source.MainStory[4].options.SequenceEqual(new[]{"무라아구?","쥐엥에잔?","시이이부라랄","자즈나! 다씨 되돌려"}),"garbled choices rewritten");
        string[] ids={"loki_question_god","loki_question_place","loki_dismiss","loki_insult"};
        string[] branches={"QuestionGod","QuestionPlace","Dismiss","Insult"};
        int[] rewards={1,1,-1,-2};
        for(int i=0;i<4;i++)
        {
            var a=attitude.choiceActions[i];
            Check(a.choiceId==ids[i] && a.choiceRole==StoryChoiceRoles.Expected(i),"ID/role mismatch "+i);
            Check(a.action=="story" && a.storyPath==LocalStoryTestInputs.GuidePath+"/"+branches[i] &&
                a.relationshipId=="Guide" && a.relationshipDelta==rewards[i],"branch/reward changed "+i);
            Check(StoryChoiceRoles.Recorded(attitude.choiceKind,a)==a.choiceRole,"recorded role wrong");
            Check(StoryChoiceRoles.ForMemory(Event(ids[i],3-i))==a.choiceRole,"legacy role guessed by slot");
            Check(source.MainStory[4].choiceActions[i].action=="continue" &&
                StoryChoiceRoles.Recorded("Mechanic",source.MainStory[4].choiceActions[i])=="Mechanic","practice became emotion");
        }
        Check(StoryChoiceRoles.ForMemory(Event("not_authored",0))=="Unknown","unknown old choice guessed positive");
        Check(StoryChoiceRoles.ForMemory(Event("loki_garbled_confused",0))=="Mechanic","legacy practice not resolved");
        Check(StoryChoiceRoles.Recorded(null,new StoryChoiceAction())=="Unknown","untyped menu guessed");
        Check(StoryChoiceRoles.Validate(attitude,out _),"valid attitude rejected");
        var bad=Clone(source).MainStory[8]; bad.choiceActions[0].choiceRole="Negative";
        Check(!StoryChoiceRoles.Validate(bad,out _),"swapped tone accepted");
        bad=Clone(source).MainStory[8]; bad.choiceActions[0].choiceRole=null;
        Check(!StoryChoiceRoles.Validate(bad,out _),"missing role accepted");
        bad=Clone(source).MainStory[8]; bad.choiceActions[1].choiceId=bad.choiceActions[0].choiceId;
        Check(!StoryChoiceRoles.Validate(bad,out _),"duplicate ID accepted");
        bad=Clone(source).MainStory[8]; bad.choiceKind=null;
        Check(!StoryChoiceRoles.Validate(bad,out _),"untyped relationship choice accepted");
        bad=Clone(source).MainStory[8]; bad.choiceKind="Mechanic";
        Check(!StoryChoiceRoles.Validate(bad,out _),"mechanic with attitude roles accepted");
        bad=Clone(source).MainStory[4]; bad.choiceKind="Unknown";
        Check(!StoryChoiceRoles.Validate(bad,out _),"unknown kind accepted");
        Check(LocalStoryRewrite.TryBuild(Patch(source),source,null,out var built,out string error),error);
        Check(built.MainStory[8].choiceKind=="Attitude" && built.MainStory[8].choiceActions[0].choiceRole=="Positive","generated metadata lost");
        string prompt=LocalStoryRewrite.Messages(LocalStoryTestInputs.GuidePath,source,null);
        Check(prompt.Contains("태도 선택 index=8") && prompt.Contains("1번 긍정 / 2번 중립 / 3번 부정 / 4번 혼돈") &&
            prompt.Contains("난독 연습에는 이 성격 순서를 적용하지 않는다"),"concise order prompt absent");
        Check(LocalStoryRewrite.CorrectionMessages(LocalStoryTestInputs.GuidePath,source,null,Patch(source)).Contains("태도 선택 index=8"),"corrector order absent");
        var legacy=Clone(source);
        foreach(var c in legacy.MainStory) { c.choiceKind=null; foreach(var a in c.choiceActions??Array.Empty<StoryChoiceAction>()) a.choiceRole=null; }
        var report=new LocalStoryTestReport {storyPath=LocalStoryTestInputs.GuidePath,original=legacy,rawOutput=Patch(source)};
        Check(LocalStoryGameApply.Build(report,"draft",false).MainStory[8].choiceActions[0].choiceRole=="Positive","legacy report/reference incompatible");
        report.original.MainStory[8].choiceActions[0].choiceRole="Negative";
        bool rejected=false; try { LocalStoryGameApply.Build(report,"draft",false); } catch(InvalidOperationException) { rejected=true; }
        Check(rejected,"explicit conflicting old role silently repaired");
        report.original=Clone(legacy); report.original.MainStory[8].choiceActions[0].choiceRole=null;
        report.original.MainStory[8].choiceActions[0].action="skip";
        rejected=false; try { LocalStoryGameApply.Build(report,"draft",false); } catch(InvalidOperationException) { rejected=true; }
        Check(rejected,"old branch silently changed");
        int typed=0;
        foreach(var asset in Resources.LoadAll<TextAsset>("Story_Json_Data/Initial"))
        {
            var json=JObject.Parse(asset.text);
            if(!(json["MainStory"] is JArray)) continue;
            var root=JsonUtility.FromJson<EncounterCardRoot>(asset.text);
            foreach(var c in root.MainStory.Where(c=>c.type=="Choice"))
            { Check(!string.IsNullOrEmpty(c.choiceKind) && StoryChoiceRoles.Validate(c,out _),"untyped/invalid Initial choice: "+asset.name); typed++; }
        }
        Check(typed==14,"Initial choice coverage changed: "+typed);
        string temp=Path.Combine(Path.GetTempPath(),"OneWayChoiceOrder_"+Guid.NewGuid().ToString("N"));
        var store=(SaveIOService)typeof(SaveIOService).GetConstructor(BindingFlags.NonPublic|BindingFlags.Instance,
            null,new[]{typeof(string)},null).Invoke(new object[]{temp});
        var instance=typeof(SaveIOService).GetField("_instance",BindingFlags.Static|BindingFlags.NonPublic);
        var previous=instance.GetValue(null);
        try
        {
            instance.SetValue(null,store);
            Check(new EncounterContentRepository().TryLoadCards(LocalStoryTestInputs.GuidePath,out var loaded,out error),error);
            Check(loaded.MainStory[8].choiceKind=="Attitude" && loaded.MainStory[4].choiceKind=="Mechanic","runtime types missing");
            foreach(string npc in new[]{"duelist","gambler"})
            foreach(string variant in new[]{"KnowledgeZero","CharmZero","KnowledgeCharmZero"})
            {
                string path="Initial/Initial_01/Encounters/"+npc;
                var expected=Read(path+"/Variants/"+variant);
                Check(new EncounterContentRepository().TryLoadCards(path,out loaded,out error,
                    variant.Contains("Knowledge"),variant.Contains("Charm")),error);
                Check(loaded.MainStory.Last().choiceKind=="Mechanic" && loaded.MainStory.Last().text==expected.MainStory.Last().text,
                    "ability variant no longer selected: "+npc+"/"+variant);
            }
            var progress=new GameProgress();
            for(int i=0;i<4;i++) progress.storyEvents.Add(Event(ids[i],3-i));
            progress.storyEvents.Add(Event("loki_garbled_confused",0));
            progress.storyEvents.Add(Event("not_authored",0));
            store.SaveRunCheckpoint(1,"Progress",progress);
            var rules=JsonUtility.FromJson<LocalStoryDreamRules>(Resources.Load<TextAsset>("LocalStory/DreamMemoryRules").text).rules;
            var memories=LocalStoryDialogueQueue.SelectDreamMemories(store,1,rules);
            Check(memories.Count==3 && memories.Any(m=>m.eventKey==ids[0]) && memories.Any(m=>m.eventKey==ids[2]) &&
                memories.Any(m=>m.eventKey==ids[3]),"stable ID dreams wrong");
            Check(!memories.Any(m=>m.eventKey==ids[1] || m.eventKey.Contains("garbled") || m.eventKey=="not_authored"),"neutral/practice/unknown became positive dream");
            Check(JsonUtility.ToJson(source)==before,"source fixture mutated");
            return checks+" checks passed: actual original choice wording/roles, stable branches/rewards, typed mechanic variants, loader/parser/prompts, legacy report compatibility and ID-based dreams. No real saves/Edits/model writes. Fixture="+temp;
        }
        finally { instance.SetValue(null,previous); }
    }
}
