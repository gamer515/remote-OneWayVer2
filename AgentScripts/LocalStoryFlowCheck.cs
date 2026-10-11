using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Phase-two diagnostics, outside Assets. Fixtures never enter real progress or generated game content.
public static class LocalStoryFlowCheck
{
    private static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
    private static string Patch(EncounterCardRoot source) => "{\"cards\":["+string.Join(",",source.MainStory.Select((c,i)=>
        "{\"index\":"+i+",\"text\":"+LocalStoryRewrite.Quote(c.text)+",\"options\":["+
        string.Join(",",(c.options??Array.Empty<string>()).Select(LocalStoryRewrite.Quote))+"]}"))+"]}";
    private static EncounterCardRoot Copy(EncounterCardRoot source) => JsonUtility.FromJson<EncounterCardRoot>(JsonUtility.ToJson(source));
    public static string Regression()
    {
        int checks=0;
        string path=LocalStoryTestInputs.GuidePath;
        var source=JsonUtility.FromJson<EncounterCardRoot>(Resources.Load<TextAsset>("Story_Json_Data/"+path+"/Story").text);
        string original=JsonUtility.ToJson(source),grammar=LocalStoryRewrite.Grammar(source);
        Check(!grammar.Contains(LocalStoryRewrite.Quote(LocalStoryRewrite.Quote(source.MainStory[4].text))),"garbled text still locked by GBNF"); checks++;
        Check(!grammar.Contains(LocalStoryRewrite.Quote(LocalStoryRewrite.Quote(source.MainStory[4].options[0]))),"garbled option still locked"); checks++;
        Check(grammar.Contains("ws ::= [ \\t\\n\\r]?"),"unbounded whitespace"); checks++;
        var changed=Copy(source);
        changed.MainStory[4].text="아라듣찌 모타게찌?";
        changed.MainStory[4].options=new[]{"머라구우?","아아, 아라쏘!","으라부라라","다씨 도려줘!"};
        changed.MainStory[1].text="왕국 소유의 광산이니 함부로 들어와서는 곤란하지.";
        changed.MainStory[6].text="이 세계에 이름을 남기고 싶나?";
        changed.MainStory[7].text="나는 로키, 장난을 다스리는 신이지. 적적해서 이곳을 거닐었네.";
        Check(LocalStoryRewrite.TryBuild(Patch(changed),source,LocalStoryTestInputs.Create(3),out var built,out var error),error); checks++;
        Check(built.MainStory[4].semanticText==source.MainStory[4].semanticText,"hidden meaning overwritten"); checks++;
        Check(built.MainStory[4].text==changed.MainStory[4].text && built.MainStory[4].options.SequenceEqual(changed.MainStory[4].options),"variation replaced with source"); checks++;
        for(int i=0;i<source.MainStory.Count;i++)
        {
            var left=Copy(source).MainStory[i]; var right=Copy(built).MainStory[i];
            left.text=right.text=null; left.options=right.options=null;
            Check(JsonUtility.ToJson(left)==JsonUtility.ToJson(right),"rule/ID/action changed at "+i); checks++;
        }
        string correction=LocalStoryRewrite.CorrectionMessages(path,source,LocalStoryTestInputs.Create(3),Patch(changed));
        Check(correction.Contains("아라듣찌 모타게찌?") && correction.Contains("정상 한국어로 복원하지 않는다"),"variable garbling not sent to correction"); checks++;
        Check(!correction.Contains("한 글자도") && !correction.Contains("그대로 복사"),"old proofreading lock remains"); checks++;
        Check(correction.Contains("이미 아는 정체") && correction.Contains("세계관 사실"),"reunion correction still uses first-meeting outline"); checks++;
        var restored=Copy(changed); restored.MainStory[4].text="알아듣지 못하겠지 ?";
        Check(!LocalStoryRewrite.TryBuild(Patch(restored),source,null,out _,out error)&&error.Contains("정상 의미"),"literal normal-language restoration accepted"); checks++;
        bool rejected=false;
        try { LocalStoryRewrite.CorrectionMessages(path,source,null,Patch(restored)); } catch(FormatException) { rejected=true; }
        Check(!rejected,"normalised garbling cannot reach correction for repair"); checks++;
        var tooLong=Copy(changed); tooLong.MainStory[0].text=new string('가',161);
        Check(!LocalStoryRewrite.TryBuild(Patch(tooLong),source,null,out _,out _),"text cap removed"); checks++;
        tooLong=Copy(changed); tooLong.MainStory[8].options[0]=new string('가',46);
        Check(!LocalStoryRewrite.TryBuild(Patch(tooLong),source,null,out _,out _),"option cap removed"); checks++;
        Check(!LocalStoryRewrite.TryBuild(Patch(changed).Replace("\"index\":4","\"index\":5"),source,null,out _,out _),"reordered cards accepted"); checks++;
        Check(!LocalStoryRewrite.TryBuild(Patch(changed).Replace("\"options\":[]","\"options\":[\"새 선택\"]"),source,null,out _,out _),"extra options accepted"); checks++;
        var first=LocalStoryRewrite.Messages(path,source,LocalStoryTestInputs.Create(1));
        var reunion=LocalStoryRewrite.Messages(path,source,LocalStoryTestInputs.Create(3));
        foreach(string input in new[]{first,reunion,correction})
        {
            Check(((string)JArray.Parse(input)[0]["content"]).Contains(LocalStoryRewrite.SceneDialogueRule) &&
                !input.Contains(LocalStoryRewrite.DialogueOnlyRule),"scene direct-speech rule missing / old per-card examples remain"); checks++;
        }
        Check(((string)JArray.Parse(LocalStoryRewrite.Messages("Initial/Initial_01/Encounters/duelist",source,null))[0]["content"]).Contains(LocalStoryRewrite.DialogueOnlyRule),"non-Guide prompt changed"); checks++;
        Check(!first.Contains(source.MainStory[0].text)&&!reunion.Contains(source.MainStory[0].text),"original dialogue supplied as template"); checks++;
        Check(!reunion.Contains("첫 문장=")&&!reunion.Contains("첫 카드에서 한 번만")&&!reunion.Contains("고정 text="),"reunion sentence lock remains"); checks++;
        Check(reunion.Contains("연결 대화")&&reunion.Contains("이미 아는 정체")&&reunion.Contains("이 세계에 이름을 남기고"),"flow/expansion missing"); checks++;
        Check(first.Contains(source.MainStory[4].semanticText)&&first.Contains("발음을 흐트러뜨린다"),"garbled semantic brief missing"); checks++;
        var memory=LocalStoryTestInputs.Create(3);
        memory.events.Add(new StoryEventRecord{chapterId="Initial",characterId="Guide",terrainName="Dungeon",kind="item_received",itemId="KnownItem",quantity=1,reason="기록된 교환"});
        Check(LocalStoryRewrite.Messages(path,source,memory).Contains("KnownItem")&&LocalStoryRewrite.CorrectionMessages(path,source,memory,Patch(changed)).Contains("KnownItem"),"item/result context lost in one pass"); checks++;
        memory.events[1].characterId="Gambler";
        Check(!LocalStoryRewrite.Messages(path,source,memory).Contains("KnownItem")&&!LocalStoryRewrite.CorrectionMessages(path,source,memory,Patch(changed)).Contains("KnownItem"),"unrelated NPC memory leaked"); checks++;
        Check(JsonUtility.ToJson(source)==original,"source mutated"); checks++;
        return checks+" checks passed: variable garbling, literal restoration guard, flow expansion, immutable actions, bounded output, matching draft/correction context, no source mutation. Semantic quality still requires human review.";
    }
    public static string Comparison()
    {
        var files=LocalStoryTestArchive.List();
        var reports=files.Select(LocalStoryTestArchive.Load).ToList();
        var result=new JArray();
        foreach(int scenario in new[]{1,2,3})
        {
            string name=LocalStoryTestInputs.Name(scenario);
            var old=reports.FirstOrDefault(r=>r.model.Contains("Gemma") && string.IsNullOrEmpty(r.writingPolicy) && r.testScenario=="Synthetic / "+name);
            var next=reports.LastOrDefault(r=>r.model.Contains("Gemma") && r.writingPolicy==LocalStoryRewrite.WritingPolicy && string.IsNullOrEmpty(r.examplePrompt) && string.IsNullOrEmpty(r.comparisonMode) && r.testScenario.StartsWith("Synthetic / "+name+" /"));
            Check(old!=null && next!=null,"missing before/after "+name);
            Check(old.model==next.model&&old.seed==next.seed&&old.draftParameters==next.draftParameters&&old.correctionParameters==next.correctionParameters,"sampler/model changed "+name);
            Check(JsonUtility.ToJson(old.original)==JsonUtility.ToJson(next.original),"source input changed "+name);
            Check(JsonUtility.ToJson(old.interactionMemory)==JsonUtility.ToJson(next.interactionMemory),"memory changed "+name);
            Check(next.generationGrammar==LocalStoryRewrite.Grammar(next.original),"saved grammar differs "+name);
            var raw=LocalStoryTestArchive.RawCards(next.rawOutput);
            Check(raw!=null&&raw.Count==next.original.MainStory.Count,"not all draft cards generated "+name);
            var oldRaw=LocalStoryTestArchive.RawCards(old.rawOutput);
            result.Add(new JObject{
                ["scenario"]=name,["before"]=old.archiveNumber,["after"]=next.archiveNumber,
                ["sameModelSeedSamplingSourceMemory"]=true,
                ["beforeFirst"]=(string)oldRaw[0]["text"],["afterFirst"]=(string)raw[0]["text"],
                ["afterBridge"]=(string)raw[2]["text"],["beforeGarbled"]=(string)oldRaw[4]["text"],["afterGarbled"]=(string)raw[4]["text"],
                ["draftValid"]=next.valid,["draftError"]=next.error,["correctionValid"]=next.correctionValid,["correctionError"]=next.correctionError,
                ["seconds"]=next.seconds,["humanQuality"]="Not automatically approved"
            });
        }
        return result.ToString();
    }
    public static string Progress()
    {
        var p=UnityEngine.Object.FindFirstObjectByType<LocalStoryTestPanel>();
        Check(p!=null,"Play panel required");
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var streamed=(string)typeof(LocalStoryTestPanel).GetField("streamed",flags).GetValue(p);
        return "Running="+p.IsGenerating+"; streamedChars="+(streamed?.Length??0)+"; promptChars="+(p.Report?.prompt?.Length??0)+
            "; correcting="+typeof(LocalStoryTestPanel).GetField("correcting",flags).GetValue(p)+"; startedUtc="+p.Report?.createdUtc+
            "; tail="+(streamed==null?"":streamed.Substring(Math.Max(0,streamed.Length-90)));
    }
    public static string DialogueTrialComparison(int firstNumber=11)
    {
        var reports=LocalStoryTestArchive.List().Select(LocalStoryTestArchive.Load).ToList();
        var result=new JArray(); LocalStoryTestReport first=null;
        foreach(int scenario in new[]{1,2,3})
        {
            var report=reports.Single(r=>r.archiveNumber==firstNumber+scenario-1);
            var before=reports.Single(r=>r.archiveNumber==(scenario==1?10:scenario==2?8:9));
            Check(report.model.Contains("Gemma") && report.testScenario.StartsWith("Synthetic / "+LocalStoryTestInputs.Name(scenario)+" /"),"trial scenario/model mismatch");
            Check(JArray.Parse(report.prompt).Any(m=>((string)m["content"]).Contains(LocalStoryRewrite.DialogueOnlyRule)),"actual draft did not receive direct-speech rule");
            if(report.correctionAttempted)
                Check(JArray.Parse(report.correctionPrompt).Any(m=>((string)m["content"]).Contains(LocalStoryRewrite.DialogueOnlyRule)&&((string)m["content"]).Contains(report.examplePrompt)),"correction rule/examples missing");
            if(first==null) first=report;
            Check(report.examplePrompt==first.examplePrompt && (report.exampleSources??Array.Empty<string>()).SequenceEqual(first.exampleSources??Array.Empty<string>()),"examples changed between trial scenarios");
            Check(report.seed==first.seed&&report.draftParameters==first.draftParameters&&report.correctionParameters==first.correctionParameters&&JsonUtility.ToJson(report.original)==JsonUtility.ToJson(first.original),"trial sampler/source changed");
            result.Add(new JObject{
                ["scenario"]=LocalStoryTestInputs.Name(scenario),["before"]=before.archiveNumber,["after"]=report.archiveNumber,
                ["sameHistoricalModelSeedSamplingSourceMemory"]=before.model==report.model&&before.seed==report.seed&&before.draftParameters==report.draftParameters&&
                    before.correctionParameters==report.correctionParameters&&JsonUtility.ToJson(before.original)==JsonUtility.ToJson(report.original)&&JsonUtility.ToJson(before.interactionMemory)==JsonUtility.ToJson(report.interactionMemory),
                ["sameHistoricalExampleContext"]=before.examplePrompt==report.examplePrompt,
                ["examples"]=report.exampleSummary,["draftSeconds"]=report.draftSeconds,["correctionSeconds"]=report.correctionSeconds,
                ["draftValid"]=report.valid,["correctionValid"]=report.correctionValid,["error"]=report.error,["correctionError"]=report.correctionError,
                ["beforeDraft"]=LocalStoryTestArchive.RawCards(before.rawOutput),["draft"]=LocalStoryTestArchive.RawCards(report.rawOutput),
                ["correction"]=LocalStoryTestArchive.RawCards(report.correctionRawOutput),["qualityApproval"]="Not automatic; read both stages"
            });
        }
        return result.ToString();
    }
    public static string NoExamplesComparison(int beforeNumber=11,int afterNumber=14)
    {
        var reports=LocalStoryTestArchive.List().Select(LocalStoryTestArchive.Load).ToList();
        var before=reports.Single(r=>r.archiveNumber==beforeNumber); var after=reports.Single(r=>r.archiveNumber==afterNumber);
        Check(after.comparisonMode=="NoWritingExamples" && string.IsNullOrEmpty(after.examplePrompt) && (after.exampleSources?.Length??0)==0,"examples not actually disabled");
        Check(!string.IsNullOrEmpty(before.examplePrompt),"baseline has no examples");
        Check(after.model==before.model && after.seed==before.seed && after.draftParameters==before.draftParameters && after.correctionParameters==before.correctionParameters &&
            after.generationGrammar==before.generationGrammar && JsonUtility.ToJson(after.original)==JsonUtility.ToJson(before.original) &&
            JsonUtility.ToJson(after.interactionMemory)==JsonUtility.ToJson(before.interactionMemory),"ablation changed model/settings/source/memory/grammar");
        string section=before.examplePrompt+"\n\n[현재 생성 대상 — 아래 사실과 규칙 우선]\n";
        var prior=JArray.Parse(before.prompt); var current=JArray.Parse(after.prompt);
        Check(prior.Count==1 && current.Count==1 && ((string)prior[0]["content"]).Contains(section),"unexpected baseline message layout");
        Check(((string)prior[0]["content"]).Replace(section,"")==(string)current[0]["content"],"draft prompt changed beyond example removal");
        if(after.correctionAttempted && after.writingPolicy==LocalStoryRewrite.WritingPolicy)
            Check(after.correctionPrompt==LocalStoryTestModels.Messages(LocalStoryTestModel.Gemma,LocalStoryRewrite.CorrectionMessages(after.storyPath,after.original,after.interactionMemory,after.rawOutput)),"correction has extra examples/rules");
        return new JObject{
            ["before"]=beforeNumber,["after"]=afterNumber,["onlyDraftInputChange"]= "writing examples removed",
            ["sameModelSeedSettingsSourceMemoryGrammar"]=true,["draftSeconds"]=after.draftSeconds,["correctionSeconds"]=after.correctionSeconds,
            ["draftValid"]=after.valid,["correctionValid"]=after.correctionValid,["error"]=after.error,["correctionError"]=after.correctionError,
            ["beforeDraft"]=LocalStoryTestArchive.RawCards(before.rawOutput),["draft"]=LocalStoryTestArchive.RawCards(after.rawOutput),
            ["correction"]=LocalStoryTestArchive.RawCards(after.correctionRawOutput),["humanApproval"]=false
        }.ToString();
    }
    public static string SceneRegression()
    {
        int checks=0; string path=LocalStoryTestInputs.GuidePath;
        var source=JsonUtility.FromJson<EncounterCardRoot>(Resources.Load<TextAsset>("Story_Json_Data/"+path+"/Story").text);
        string original=JsonUtility.ToJson(source);
        var first=LocalStoryRewrite.Messages(path,source,null);
        var correction=LocalStoryRewrite.CorrectionMessages(path,source,null,Patch(source));
        foreach(string input in new[]{first,correction})
        {
            foreach(string section in new[]{"[인물과 장면]","[이번 만남과 실제 기록]","[장면 전체 흐름]","[출력 연결표"})
            { Check(input.Contains(section),"missing scene section "+section); checks++; }
            Check(!input.Contains("사건 의미=") && !input.Contains("options[0]의 의도=") && !input.Contains("의미 확인용 카드 요약"),"old per-card semantic commands"); checks++;
            Check(input.Contains("대사 화자: 로키 / 듣는 사람: 플레이어") && input.Contains("선택지는 플레이어가 상대 인물에게 하는 직접 말"),"speaker/recipient route missing"); checks++;
            Check(input.Contains(source.MainStory[3].coreMeaning) && input.Contains(source.MainStory[5].coreMeaning) && !input.Contains("[실행 사건"),"core missing or duplicate action-state section remains"); checks++;
            Check(input.Contains(source.MainStory[4].semanticText) && input.Contains("정상 한국어로 복원하지 않는다"),"hidden meaning lost"); checks++;
            Check(!input.Contains("장난 그만하고 다시 돌려줘!") && input.Contains("그마안! 다씨 도려주우!") && LocalStoryRewrite.WritingBrief(path,source).choiceReferences.Any(r=>r.choiceId=="loki_garbled_restore"),"normal spoken example leaked / heard reference binding missing"); checks++;
            Check(!input.Contains("플레이어가 로키의 정체를 궁금해하는지 물음"),"reversed identity directive remains"); checks++;
        }
        Check(!first.Contains("Guide → Player") && !first.Contains("지식 코인 5개") && !first.Contains(source.MainStory[0].eventIdStable) &&
            !first.Contains(source.MainStory[8].choiceActions[0].storyPath),"execution metadata leaked into writer input"); checks++;
        var firstContext=(string)JArray.Parse(first)[1]["content"];
        var correctionContext=(string)JArray.Parse(correction)[1]["content"];
        Check(correctionContext.StartsWith(firstContext.Substring(0,firstContext.IndexOf("\n전체 대화를 작성한다.")),StringComparison.Ordinal),"draft/correction scene context differs"); checks++;
        var positive=LocalStoryRewrite.Messages(path,source,LocalStoryTestInputs.Create(2));
        var negative=LocalStoryRewrite.Messages(path,source,LocalStoryTestInputs.Create(3));
        Check(positive.Contains("예의 있는 질문") && negative.Contains("빈정거림"),"reunion attitudes lost"); checks++;
        Check(positive.Contains("이미 아는 정체") && !positive.Contains("첫 만남이다"),"reunion uses first-meeting flow"); checks++;
        foreach(Action<EncounterCardRoot> mutation in new Action<EncounterCardRoot>[] {
            r=>r.MainStory[3].guideAction.amount=99,
            r=>r.MainStory[0].speakerId="Gambler",
            r=>r.MainStory[8].choiceActions[0].storyPath="Other/Reward",
            r=>r.MainStory[8].choiceActions[0].relationshipDelta=99,
            r=>r.MainStory[8].choiceActions[0].choiceId="OtherChoice",
            r=>r.MainStory[0].eventIdStable="OtherEvent",
            r=>r.MainStory[3].coreMeaning="새 보상을 준다.",
            r=>r.storyFrame.purpose="다른 세계로 이동한다."})
        {
            var changed=Copy(source); mutation(changed); bool rejected=false;
            try { LocalStoryRewrite.Messages(path,changed,null); } catch(InvalidOperationException) { rejected=true; }
            Check(rejected,"modified source binding accepted"); checks++;
        }
        var partial=Copy(source); partial.MainStory=partial.MainStory.Take(3).ToList();
        Check(LocalStoryRewrite.Messages(path,partial,null).Contains("0부터 2까지") && !LocalStoryRewrite.Messages(path,partial,null).Contains("index=8"),"partial diagnostic layout broken"); checks++;
        Check(original==JsonUtility.ToJson(source),"source mutated"); checks++;
        return checks+" checks passed: scene-level direction, direct choice references, shared draft/correction context, immutable event/choice/speaker bindings, first/reunion separation. Not a semantic quality approval.";
    }
    public static string SceneComparison(int beforeNumber=14,int afterNumber=15)
    {
        var reports=LocalStoryTestArchive.List().Select(LocalStoryTestArchive.Load).ToList();
        var before=reports.Single(r=>r.archiveNumber==beforeNumber); var after=reports.Single(r=>r.archiveNumber==afterNumber);
        Check(before.writingPolicy=="큰 흐름 v7.1" && after.writingPolicy.StartsWith("장면 흐름 v8"),"unexpected policies");
        Check(new[]{before,after}.All(r=>r.comparisonMode=="NoWritingExamples" && string.IsNullOrEmpty(r.examplePrompt) && (r.exampleSources?.Length??0)==0),"examples differ");
        Check(before.model==after.model && before.seed==after.seed && before.draftParameters==after.draftParameters &&
            before.correctionParameters==after.correctionParameters && before.generationGrammar==after.generationGrammar &&
            JsonUtility.ToJson(before.original)==JsonUtility.ToJson(after.original) &&
            JsonUtility.ToJson(before.interactionMemory)==JsonUtility.ToJson(after.interactionMemory),"model/seed/settings/source/memory/grammar changed");
        if(after.writingPolicy==LocalStoryRewrite.WritingPolicy)
            Check(after.prompt==LocalStoryTestModels.Messages(LocalStoryTestModel.Gemma,LocalStoryRewrite.Messages(after.storyPath,after.original,after.interactionMemory)),"actual generation prompt differs from scene path");
        if(after.correctionAttempted && after.writingPolicy==LocalStoryRewrite.WritingPolicy)
            Check(after.correctionPrompt==LocalStoryTestModels.Messages(LocalStoryTestModel.Gemma,LocalStoryRewrite.CorrectionMessages(after.storyPath,after.original,after.interactionMemory,after.rawOutput)),"actual correction prompt differs");
        return new JObject{
            ["before"]=beforeNumber,["after"]=afterNumber,["sameModelSeedSettingsSourceMemoryGrammarAndNoExamples"]=true,
            ["changed"]=after.writingPolicy+" scene-level writing brief and correction context; not a single-word ablation",
            ["actualPromptReplayChecked"]=after.writingPolicy==LocalStoryRewrite.WritingPolicy,
            ["beforePromptChars"]=before.prompt.Length,["afterPromptChars"]=after.prompt.Length,
            ["draftSeconds"]=after.draftSeconds,["correctionSeconds"]=after.correctionSeconds,
            ["draftValid"]=after.valid,["correctionValid"]=after.correctionValid,["error"]=after.error,["correctionError"]=after.correctionError,
            ["beforeDraft"]=LocalStoryTestArchive.RawCards(before.rawOutput),["draft"]=LocalStoryTestArchive.RawCards(after.rawOutput),
            ["correction"]=LocalStoryTestArchive.RawCards(after.correctionRawOutput),["humanApproval"]=false
        }.ToString();
    }
    public static string RecordSceneExampleObservations()
    {
        string first=RecordObservation(21,"완성 Edit 0014 교정본의 대사와 선택지를 초안 9카드 전체에 그대로 복사했다. 형식과 화자는 안정됐지만 새로운 표현 생성은 아니다. 교정은 광산을 길을 보여주는 곳이 아니라고 하거나, 지식 반환 뒤 돌려줄게라고 하는 등 어색함과 상태 의미 오류가 남았다. 품질 승인 및 실제 게임 적용 없음.");
        string reunion=RecordObservation(22,"모욕 재회 입력에 이전 꺼져! 선택과 기억 반응 지시가 있지만 초안은 0021 및 Edit 0014 교정본과 9카드 전체가 같다. 교정도 로키의 대사 9개를 그대로 두고 선택지 2개만 바꿨다. 과거 모욕 반응과 재회 태도를 출력하지 않았다. 구조 검사 통과는 품질 승인이 아니다. 실제 게임 적용 없음.");
        return first+"\n"+reunion;
    }
    public static string RecordObservation(int number,string note)
    {
        Check(number>=5 && !string.IsNullOrWhiteSpace(note),"only new phase-two results may receive observations");
        var reportPath=LocalStoryTestArchive.List().Single(p=>LocalStoryTestArchive.Load(p).archiveNumber==number);
        var report=LocalStoryTestArchive.Load(reportPath);
        Check(!string.IsNullOrEmpty(report.writingPolicy),"legacy result must remain untouched");
        var file=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(reportPath),"Analysis.json");
        var json=JObject.Parse(System.IO.File.ReadAllText(file));
        json["agentReview"]=new JObject {
            ["reviewer"]="Codex 직접 읽기 검토",
            ["status"]="needs_human_review",
            ["note"]=note,
            ["notAutomaticSemanticApproval"]=true
        };
        System.IO.File.WriteAllText(file,json.ToString(Newtonsoft.Json.Formatting.Indented)+"\n",new System.Text.UTF8Encoding(false));
        return "#"+number.ToString("D4")+" observation recorded; raw/source/Edit/humanReviews unchanged.";
    }
}
