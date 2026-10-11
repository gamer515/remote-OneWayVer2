using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static class LocalStoryCoreCheck
{
    static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
    static string Patch(EncounterCardRoot r)=>new JObject{["cards"]=new JArray(r.MainStory.Select((c,i)=>new JObject{
        ["index"]=i,["text"]=c.text,["options"]=new JArray(c.options??Array.Empty<string>())}))}.ToString(Newtonsoft.Json.Formatting.None);
    public static string Regression()
    {
        string path=LocalStoryTestInputs.GuidePath;
        var original=JsonUtility.FromJson<EncounterCardRoot>(Resources.Load<TextAsset>("Story_Json_Data/"+path+"/Story").text);
        string source=JsonUtility.ToJson(original);
        Check(original.storyFrame?.worldFacts?.Length>0 && original.MainStory.Count(c=>!string.IsNullOrEmpty(c.coreMeaning))==6,"core frame missing");
        Check(string.IsNullOrEmpty(original.MainStory[0].coreMeaning)&&string.IsNullOrEmpty(original.MainStory[2].coreMeaning),"chatter constrained");
        var changed=JsonUtility.FromJson<EncounterCardRoot>(source);
        changed.MainStory[0].text="어이, 자네. 광산 구경은 여기서 그만하게.";
        changed.MainStory[4].text="아라듣찌 모타게찌?";
        changed.MainStory[4].options=new[]{"머라구우?","아아, 아라쏘!","으라부라라","다씨 도려줘!"};
        var report=new LocalStoryTestReport{original=original,storyPath=path,rawOutput=Patch(changed),targetRun=2};
        var built=LocalStoryGameApply.Build(report,"draft",false);
        Check(built.MainStory[4].text==changed.MainStory[4].text && built.MainStory[3].guideAction.amount==5,"variation/action not preserved");
        Check(built.MainStory[7].coreMeaning==original.MainStory[7].coreMeaning && JsonUtility.ToJson(built.storyFrame)==JsonUtility.ToJson(original.storyFrame),"model overwrote cores");
        string prompt=LocalStoryRewrite.Messages(path,original,null);
        Check(prompt.Contains(original.MainStory[7].coreMeaning)&&!prompt.Contains("지식 코인 5개")&&!prompt.Contains("Guide → Player"),"writer context leak/missing core");
        var bad=JsonUtility.FromJson<EncounterCardRoot>(JsonUtility.ToJson(changed));
        bad.MainStory[0].text="Guide → Player; 자네, 멈추게.";
        Check(!LocalStoryRewrite.TryBuild(Patch(bad),original,null,out _,out _),"role route leak accepted");
        bad.MainStory[0].text="지식 코인 5개를 가져가게.";
        Check(!LocalStoryRewrite.TryBuild(Patch(bad),original,null,out _,out _),"new coin quantity accepted");
        bad.MainStory[0].text=changed.MainStory[0].text;
        bad.MainStory[4].options[0]="뭐라고?";
        Check(!LocalStoryRewrite.TryBuild(Patch(bad),original,null,out _,out _),"normal garbled choice accepted");
        Check(LocalStoryRewrite.CorrectionMessages(path,original,null,Patch(bad)).Contains("핵심과 어긋나면"),"bad wording cannot be corrected");
        var ctor=typeof(SaveIOService).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(string)},null);
        string temp=Path.Combine(Path.GetTempPath(),"OneWayCore_"+Guid.NewGuid().ToString("N"));
        var isolated=(SaveIOService)ctor.Invoke(new object[]{temp});
        var field=typeof(SaveIOService).GetField("_instance",BindingFlags.Static|BindingFlags.NonPublic);
        var old=field.GetValue(null);
        try
        {
            field.SetValue(null,isolated);
            LocalStoryGameApply.Save(report,"draft",false,2,isolated);
            Check(new EncounterContentRepository(2).TryLoadCards(path,out var cards,out string error),error);
            Check(cards.MainStory[0].text==changed.MainStory[0].text && cards.MainStory[4].text==changed.MainStory[4].text,"actual game loader reverted valid changes");
            Check(cards.MainStory[7].coreMeaning==original.MainStory[7].coreMeaning,"runtime core lost");
            Check(new EncounterContentRepository(1).TryLoadCards(path,out var first,out error)&&first.MainStory[0].text==original.MainStory[0].text,"current run changed");
            LocalStoryGameApply.Save(report,"draft",false,2,isolated);
            Check(Directory.GetFiles(Path.Combine(temp,"GeneratedContent","Recovery")).Length==1,"prior story recovery missing");
            string edit=Path.Combine(temp,"Edit.json");
            var lines=new JArray(changed.MainStory.Select(c=>new JObject{["speaker"]="로키",["text"]=c.text,["options"]=new JArray(c.options??Array.Empty<string>())}));
            File.WriteAllText(edit,new JObject{["draft"]=lines}.ToString());
            lines[0]["text"]="자네, 오늘은 광산 구경하러 온 건가?";
            File.WriteAllText(edit,new JObject{["draft"]=lines}.ToString());
            Check(LocalStoryGameApply.Build(report,"draft",true,edit).MainStory[0].text==(string)lines[0]["text"],"Edit text not used");
            string savedEdit=File.ReadAllText(edit);
            lines[0]["speaker"]="플레이어"; File.WriteAllText(edit,new JObject{["draft"]=lines}.ToString());
            bool rejected=false; try{LocalStoryGameApply.Build(report,"draft",true,edit);}catch(FormatException){rejected=true;}
            Check(rejected,"wrong Edit speaker accepted");
            // No source or real progress write. The diagnostic folder is retained for inspection.
            Check(JsonUtility.ToJson(original)==source,"source mutated");
            return "18 checks passed: source cores, free chatter, writer/corrector context, role/number/garbling guards, explicit AI/Edit promotion, actual run-2 loader, run-1 unchanged, recoverable replacement. Isolated storage="+temp;
        }
        finally { field.SetValue(null,old); }
    }
    public static string CompactRegression()
    {
        int checks=0;
        string path=LocalStoryTestInputs.GuidePath;
        var source=JsonUtility.FromJson<EncounterCardRoot>(Resources.Load<TextAsset>("Story_Json_Data/"+path+"/Story").text);
        string baseline=JsonUtility.ToJson(source);
        string messages=LocalStoryRewrite.Messages(path,source,null);
        string input=(string)JArray.Parse(messages)[1]["content"];
        var brief=LocalStoryRewrite.WritingBrief(path,source);
        foreach(var card in source.MainStory.Where(c=>!string.IsNullOrWhiteSpace(c.coreMeaning)))
        { Check(input.Split(new[]{card.coreMeaning},StringSplitOptions.None).Length==2,"core repeated or missing"); checks++; }
        Check(!input.Contains("[실행 사건")&&!input.Contains("[선택 연결"),"duplicate instruction sections remain"); checks++;
        foreach(var reference in brief.choiceReferences)
        {
            Check(!string.IsNullOrWhiteSpace(reference.heardText)&&input.Contains(reference.heardText),"heard example missing"); checks++;
            if(!reference.choiceId.EndsWith("_mumble"))
            { Check(!input.Contains(reference.text),"normal-language example supplied as garbled speech"); checks++; }
        }
        Check(input.Contains(source.MainStory[4].semanticText),"hidden meaning missing"); checks++;
        var bad=JsonUtility.FromJson<EncounterCardRoot>(baseline);
        bad.MainStory[4].options[0]="뭐라고?";
        string correction=LocalStoryRewrite.CorrectionMessages(path,source,null,Patch(bad));
        var correctInput=(string)JArray.Parse(correction)[1]["content"];
        Check(correctInput.Contains("[검사에서 확인된 수정 필요 부분]")&&correctInput.Contains("index=4: 난독"),"failed slot not identified to corrector"); checks++;
        Check(correctInput.StartsWith(input.Substring(0,input.IndexOf("\n전체 대화를 작성한다.",StringComparison.Ordinal)),StringComparison.Ordinal),"correction does not share exact scene context"); checks++;
        bad.MainStory[4].text="아라듣찌 모타게찌? (알아듣지 못하겠지?)";
        Check(!LocalStoryRewrite.TryBuild(Patch(bad),source,null,out _,out string error)&&error.Contains("정상 의미"),"normal gloss embedded inside garbling accepted"); checks++;
        var oldFile=LocalStoryTestArchive.List().Single(f=>LocalStoryTestArchive.Load(f).archiveNumber==17);
        var old=LocalStoryTestArchive.Load(oldFile);
        int length=LocalStoryTestModels.Messages(LocalStoryTestModel.Gemma,messages).Length;
        Check(length<old.prompt.Length,"prompt was not shortened"); checks++;
        Check(old.draftParameters==LocalStorySampling.Comparison().Parameters(old.seed),"comparison settings changed"); checks++;
        Check(JsonUtility.ToJson(source)==baseline,"source mutated"); checks++;
        return checks+" checks passed; v9 prompt="+old.prompt.Length+" chars; v10="+length+
            " chars; one core occurrence, heard examples, targeted correction findings, identical settings. No semantic quality approval.";
    }
    public static string SceneExamplesRegression()
    {
        string path=LocalStoryTestInputs.GuidePath;
        var source=JsonUtility.FromJson<EncounterCardRoot>(Resources.Load<TextAsset>("Story_Json_Data/"+path+"/Story").text);
        var referenceResult=LocalStoryTestArchive.List().Single(f=>LocalStoryTestArchive.Load(f).archiveNumber==14);
        string authored=File.ReadAllText(LocalStoryTestArchive.EditPath(referenceResult));
        var lines=(JArray)JObject.Parse(authored)["correction"];
        string root=Path.Combine(Path.GetTempPath(),"OneWaySceneExamples_"+Guid.NewGuid().ToString("N"));
        var record=new LocalStoryTestReport{storyPath=path,model="Gemma",original=source,rawOutput=Patch(source),seed=12345,
            createdUtc=DateTime.UtcNow.ToString("o"),testScenario="Synthetic / 첫 만남"};
        string result=LocalStoryTestArchive.Save(root,JObject.Parse(JsonUtility.ToJson(record)));
        string editFile=LocalStoryTestArchive.EditPath(result);
        Check(LocalStoryTestArchive.CaptureWritingExamples(path,out _,out var refs,root)==""&&refs.Length==0,"unchanged AI became scene example");
        var edit=JObject.Parse(File.ReadAllText(editFile));
        edit["draft"]=lines.DeepClone(); File.WriteAllText(editFile,edit.ToString());
        string frozen=File.ReadAllText(editFile),evidence=File.ReadAllText(result);
        string example=LocalStoryTestArchive.CaptureWritingExamples(path,out var summary,out refs,root);
        Check(refs.Length==1&&refs[0].Contains("SHA256=")&&summary.Contains("9카드"),"scene/provenance missing");
        var cards=(JArray)JObject.Parse(example.Substring(example.IndexOf("{\"cards\"",StringComparison.Ordinal)))["cards"];
        Check(cards.Count==source.MainStory.Count,"ordered scene incomplete");
        for(int i=0;i<cards.Count;i++)
            Check((int)cards[i]["index"]==i && (string)cards[i]["text"]==(string)lines[i]["text"]&&
                JToken.DeepEquals(cards[i]["options"],lines[i]["options"]??new JArray()),"authored scene changed or reordered");
        edit["draft"]=(JArray)lines.DeepClone(); edit["draft"][4]["speaker"]="플레이어";
        File.WriteAllText(editFile,edit.ToString());
        Check(LocalStoryTestArchive.CaptureWritingExamples(path,out _,out refs,root)==""&&refs.Length==0,"wrong speaker scene used");
        edit["draft"]=lines.DeepClone(); ((JArray)edit["draft"]).RemoveAt(8); File.WriteAllText(editFile,edit.ToString());
        Check(LocalStoryTestArchive.CaptureWritingExamples(path,out _,out refs,root)==""&&refs.Length==0,"partial scene used");
        File.WriteAllText(editFile,frozen);
        Check(File.ReadAllText(result)==evidence && File.ReadAllText(LocalStoryTestArchive.EditPath(referenceResult))==authored,"evidence/user Edit changed");
        string actual=LocalStoryTestArchive.CaptureWritingExamples(path,out summary,out refs);
        Check(refs.Length==1 && actual.Contains("{\"cards\"")&&summary.Contains("9카드"),"actual complete Edit not selected");
        return "15 scene example checks passed: edited rows only, one ordered complete scene, correct speaker/choice/action binding, wrong-speaker/partial rejection, original Edit/raw preserved. "+summary+" / "+string.Join(",",refs)+". Isolated="+root;
    }
    public static string ExistingEdit(int number=14)
    {
        string file=LocalStoryTestArchive.List().Single(f=>LocalStoryTestArchive.Load(f).archiveNumber==number);
        var report=LocalStoryTestArchive.Load(file);
        string editPath=LocalStoryTestArchive.EditPath(file);
        var edit=JObject.Parse(File.ReadAllText(editPath));
        string stage=edit["correction"] is JArray?"correction":"draft";
        var built=LocalStoryGameApply.Build(report,stage,true,editPath);
        Check(built.MainStory[0].text==(string)edit[stage][0]["text"],"old edited text not used");
        return "Existing Edit "+number+" / "+stage+" passes current game application checks, with current source core properties. First="+built.MainStory[0].text+". No game save.";
    }
    public static string ApplyPreview(int number=14,bool useEdit=true,string stage="correction")
    {
        var p=UnityEngine.Object.FindFirstObjectByType<LocalStoryTestPanel>();
        Check(p!=null && !p.IsGenerating,"idle Play UI required");
        p.OpenSavedResults();
        var flags=BindingFlags.NonPublic|BindingFlags.Instance;
        var files=(System.Collections.Generic.List<string>)typeof(LocalStoryTestPanel).GetField("savedReports",flags).GetValue(p);
        int selected=files.FindIndex(f=>LocalStoryTestArchive.Load(f).archiveNumber==number);
        Check(selected>=0,"result not found");
        typeof(LocalStoryTestPanel).GetField("savedSelected",flags).SetValue(p,selected);
        p.Open();
        typeof(LocalStoryTestPanel).GetMethod("OpenGameApply",flags).Invoke(p,null);
        typeof(LocalStoryTestPanel).GetMethod("PrepareGameApply",flags).Invoke(p,new object[]{stage,useEdit});
        var preview=(TMPro.TMP_Text)typeof(LocalStoryTestPanel).GetField("applyPreview",flags).GetValue(p);
        var confirm=(UnityEngine.UI.Button)typeof(LocalStoryTestPanel).GetField("applyConfirm",flags).GetValue(p);
        Check(confirm.interactable==useEdit,"expected valid Edit / rejected raw AI");
        return preview.text;
    }

    public static string UI()
    {
        var p=UnityEngine.Object.FindFirstObjectByType<LocalStoryTestPanel>();
        Check(p!=null,"Direct DecisionScene Play required");
        p.OpenSavedResults(); p.Open();
        var flags=BindingFlags.NonPublic|BindingFlags.Instance;
        typeof(LocalStoryTestPanel).GetMethod("OpenGameApply",flags).Invoke(p,null);
        var panel=(GameObject)typeof(LocalStoryTestPanel).GetField("applyPanel",flags).GetValue(p);
        Check(panel.activeSelf,"apply dialog not visible");
        Check(UnityEngine.Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.None).Length==1,"EventSystem count");
        Check(panel.GetComponentInParent<UnityEngine.UI.GraphicRaycaster>()!=null,"raycaster missing");
        Check(panel.GetComponentsInChildren<UnityEngine.UI.Button>().All(b=>b.targetGraphic!=null && b.targetGraphic.raycastTarget && b.onClick.GetPersistentEventCount()==0),"button wiring/graphics");
        Check(((UnityEngine.UI.Button)typeof(LocalStoryTestPanel).GetField("applyConfirm",flags).GetValue(p)).interactable==false,"save enabled before selection");
        foreach(var b in panel.GetComponentsInChildren<UnityEngine.UI.Button>())
        {
            var r=(RectTransform)b.transform;
            Check(r.rect.width>0 && r.rect.height>0,"zero button bounds");
        }
        panel.SetActive(false);
        return "Game-apply UI opens; six buttons have visible bounds, one EventSystem/raycaster; confirmation disabled before selection. No actual game content saved.";
    }
}
