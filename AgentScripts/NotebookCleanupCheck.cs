using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.IO.Compression;
using System.Xml.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static class NotebookCleanupCheck
{
    const string Guide="Initial/Initial_01/Encounters/guide";
    static int checks;
    static readonly XNamespace Ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    static void Check(bool ok,string error){if(!ok)throw new Exception(error);checks++;}
    static SaveIOService Store(string root)=>(SaveIOService)Activator.CreateInstance(typeof(SaveIOService),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{root},null);
    static List<string[]> Rows(string file,string sheet)
    {
        using var zip=ZipFile.OpenRead(file);
        return (List<string[]>)typeof(LocalStoryNotebook).GetMethod("Rows",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{zip,sheet});
    }
    static JObject Document(string resource)=>(JObject)typeof(ChapterContentCatalog).GetMethod("Document",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{resource});
    static void WriteXml(ZipArchive zip,string name,XDocument doc){zip.GetEntry(name).Delete();using var stream=zip.CreateEntry(name).Open();doc.Save(stream);}
    static XDocument ReadXml(ZipArchive zip,string name){using var stream=zip.GetEntry(name).Open();return XDocument.Load(stream);}
    static Dictionary<string,string> Fingerprints(IEnumerable<string> files)=>files.Where(File.Exists).ToDictionary(f=>f,f=>{
        using var stream=File.OpenRead(f);using var sha=SHA256.Create();return Convert.ToBase64String(sha.ComputeHash(stream));});
    public static object UpdateExistingRecords()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Play를 중지하고 실행하세요.");
        string root=Path.Combine(Application.persistentDataPath,"Saves"),tests=Path.Combine(root,"LocalStoryTests");
        var saves=Fingerprints((Directory.Exists(Path.Combine(root,"Runs"))?Directory.GetFiles(Path.Combine(root,"Runs"),"*.json",SearchOption.AllDirectories):Array.Empty<string>()).Concat(new[]{Path.Combine(root,"Profile.json")}));
        var edits=Fingerprints(Directory.Exists(Path.Combine(tests,"Edits"))?Directory.GetFiles(Path.Combine(tests,"Edits"),"*.json",SearchOption.AllDirectories):Array.Empty<string>());
        int archivesBefore=Directory.Exists(tests)?Directory.GetFiles(tests,"Evidence.zip",SearchOption.AllDirectories).Length:0;
        int migrated=LocalStoryTestArchive.MigrateGrouped(tests);
        var storage=SaveIOService.Instance;LocalStoryNotebook.Refresh(storage);
        if(saves.Any(p=>!File.Exists(p.Key)||Fingerprints(new[]{p.Key})[p.Key]!=p.Value)||edits.Any(p=>!File.Exists(p.Key)||Fingerprints(new[]{p.Key})[p.Key]!=p.Value))
            throw new IOException("플레이 저장/직접 수정본이 변경되었습니다.");
        return new {root,migratedResults=migrated,archivesBefore,archivesAfter=Directory.Exists(tests)?Directory.GetFiles(tests,"Evidence.zip",SearchOption.AllDirectories).Length:0,
            records=ChapterContentCatalog.Chapters.Select(c=>LocalStoryNotebook.ChapterPath(storage,(string)c["id"])).ToArray(),playSaveFilesUnchanged=saves.Count,humanEditsUnchanged=edits.Count};
    }
    public static object CleanupExportSidecars()
    {
        string folder="Assets/StreamingAssets/LocalStory/Chapters";int removed=0;
        foreach(string file in Directory.GetFiles(folder,"*.inspect.ndjson"))if(UnityEditor.AssetDatabase.DeleteAsset(file.Replace('\\','/')))removed++;
        if(File.Exists("tmp/NotebookTemplates.json"))File.Delete("tmp/NotebookTemplates.json");
        return new {removed};
    }
    public static object ExportTemplates()
    {
        string file=Path.GetFullPath("tmp/NotebookTemplates.json");Directory.CreateDirectory(Path.GetDirectoryName(file));
        var data=new JArray(ChapterContentCatalog.Chapters.Select(c=>LocalStoryNotebook.BookData((string)c["id"])));
        File.WriteAllText(file,data.ToString());return new{file,chapters=data.Count};
    }
    public static object ArchiveRegression()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Play를 중지하고 실행하세요.");
        checks=0;string root=Path.GetFullPath("tmp/ArchiveCleanup/"+Guid.NewGuid().ToString("N")),tests=Path.Combine(root,"LocalStoryTests");
        var source=JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(Guide));
        var changed=JsonUtility.FromJson<EncounterCardRoot>(JsonUtility.ToJson(source));changed.MainStory[0].text="자네, 잠깐 이쪽을 보게.";
        string raw=new JObject {["cards"]=new JArray(changed.MainStory.Select((c,i)=>new JObject {["index"]=i,["text"]=c.text,["options"]=JArray.FromObject(c.options??Array.Empty<string>())}))}.ToString();
        var report=new LocalStoryTestReport {storyPath=Guide,original=source,rawOutput=raw,prompt="LAST_ACTUAL_PROMPT",sourceRun=1,targetRun=2,model="Gemma",seconds=1,
            interactionMemory=new LocalStoryRevisionRequest {sourceRun=1,targetRun=2,memoryMode="Dream",playEvents=new List<StoryEventRecord>{new StoryEventRecord {displayedText="AUDIT_MUST_STAY_IN_RUN_HISTORY"}},
                playedCards=new List<PlayedEncounterCardRecord>{new PlayedEncounterCardRecord {card=new Dialogue {text="AUDIT_MUST_STAY_IN_RUN_HISTORY"}}}}};
        LocalStoryRewrite.EvaluateDraft(report,source,report.interactionMemory);Check(report.valid,report.error);
        var full=JObject.Parse(JsonUtility.ToJson(report));string file=LocalStoryTestArchive.Save(tests,full);
        Check(!Directory.GetFiles(tests,"Evidence.zip",SearchOption.AllDirectories).Any(),"new Evidence.zip created");
        var stored=JObject.Parse(File.ReadAllText(file));
        Check((int)stored["schemaVersion"]==4&&stored["evidenceEntry"]==null,"result depends on another file");
        Check(stored["draft"]==null&&stored["correction"]==null&&stored["application"]==null,"derived dialogue/application copies persisted");
        Check(!File.ReadAllText(file).Contains("AUDIT_MUST_STAY_IN_RUN_HISTORY"),"full play history duplicated in result");
        Check((string)stored["rawOutput"]==raw&&JToken.DeepEquals(stored["original"],full["original"]),"raw output or original action snapshot lost");
        Check(stored["sectionInputs"]==null&&stored["sectionOutputs"]==null&&stored["generated"]==null,"redundant request/generated copies remain");
        Check((string)stored["diagnostics"]["lastPrompt"]=="LAST_ACTUAL_PROMPT","last prompt missing");
        var loaded=LocalStoryTestArchive.Load(file);
        Check(loaded.valid&&loaded.generated.MainStory.Count==9&&loaded.generated.MainStory[0].text==changed.MainStory[0].text,"stored result cannot be displayed without Evidence");
        Check(LocalStoryGameApply.Build(loaded,"draft",false).MainStory[0].text==changed.MainStory[0].text,"stored result cannot be applied");
        string edit=LocalStoryTestArchive.EditPath(file);var human=JObject.Parse(File.ReadAllText(edit));human["draft"][0]["text"]="자네, 잠시 기다리게.";File.WriteAllText(edit,human.ToString());
        Check(LocalStoryGameApply.Build(loaded,"draft",true,edit).MainStory[0].text=="자네, 잠시 기다리게.","human Edit cannot be applied");
        var bad=(JObject)full.DeepClone();bad["rawOutput"]="{BROKEN_RAW_OUTPUT";bad["valid"]=false;bad["archiveNumber"]=0;
        string failed=LocalStoryTestArchive.Save(tests,bad);Check((string)LocalStoryTestArchive.ReadReport(failed)["rawOutput"]=="{BROKEN_RAW_OUTPUT","invalid output fabricated or discarded");

        string legacyRoot=Path.Combine(root,"LegacyTests"),legacy=Path.Combine(legacyRoot,"Results/For_Run_02/Chapter_00/Chapter_00_01/Guide/0001.Result.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy));
        var old=(JObject)full.DeepClone();old["archiveNumber"]=1;old["archiveId"]="0001";
        string archive=Path.Combine(Path.GetDirectoryName(legacy),"Evidence.zip");
        using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create))using(var writer=new StreamWriter(zip.CreateEntry("0001.json").Open()))writer.Write(old.ToString());
        File.WriteAllText(legacy,new JObject {["storyPath"]=Guide,["archiveNumber"]=1,["evidenceEntry"]="0001.json"}.ToString());
        string legacyEdit=LocalStoryTestArchive.NumberedEditPath(legacy,old);Directory.CreateDirectory(Path.GetDirectoryName(legacyEdit));File.Copy(edit,legacyEdit);byte[] humanBefore=File.ReadAllBytes(legacyEdit);
        Check(LocalStoryTestArchive.MigrateGrouped(legacyRoot)==1,"old result not migrated");
        Check(!File.Exists(archive),"old Evidence archive remains");
        Check(File.ReadAllBytes(legacyEdit).SequenceEqual(humanBefore),"human Edit changed during migration");
        var recovered=LocalStoryTestArchive.Load(legacy);Check(recovered.valid&&recovered.rawOutput==raw&&recovered.generated.MainStory.Count==9,"old result cannot be opened after Evidence deletion");
        Check(LocalStoryGameApply.Build(recovered,"draft",true,legacyEdit).MainStory[0].text=="자네, 잠시 기다리게.","old human Edit cannot be applied");
        return new{passed=true,checks,root,noEvidence=true,legacyResultsAndEditsPreserved=true};
    }
    public static object Regression()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Play를 중지하고 실행하세요.");
        checks=0;string root=Path.GetFullPath("tmp/NotebookCleanup/"+Guid.NewGuid().ToString("N"));var store=Store(root);
        LocalStoryNotebook.Refresh(store);string book=LocalStoryNotebook.PathFor(store);
        using(var zip=ZipFile.OpenRead(book))
        {
            var names=XDocument.Load(zip.GetEntry("xl/workbook.xml").Open()).Descendants(Ns+"sheet").Select(s=>(string)s.Attribute("name"));
            Check(names.SequenceEqual(new[]{"NPC설정","해금규칙","아이템보상"}),"obsolete sheets remain");
            var header=XDocument.Load(zip.GetEntry("xl/worksheets/sheet1.xml").Open()).Descendants(Ns+"row").First();
            Check(header.Elements(Ns+"c").Count()==11,"role/personality columns remain");
        }
        var known=Rows(book,"아이템보상").Single(r=>r[1]=="persistent_loki_known");
        Check(known[2]=="1. loki_question_god","choiceID replaced with manually written dialogue");
        Check(known[3].Split('\n').Length==3&&known[3].Contains("보유 아이템")&&known[3].Contains("해금 상태")&&known[3].Contains("이번 회차 기록"),"situation categories or line breaks missing");
        var npc=LocalStoryNotebook.Contexts(store,Array.Empty<string>()).Single(c=>c.characterId=="Guide");
        Check(npc.role==ChapterContentCatalog.Character("Guide").role&&npc.personality==ChapterContentCatalog.Character("Guide").personality,"settings are not read from JSON");
        DateTime unchanged=File.GetLastWriteTimeUtc(book);LocalStoryNotebook.Refresh(store);Check(File.GetLastWriteTimeUtc(book)==unchanged,"unchanged data rewrites workbook");

        // Simulate the former 13-column workbook without depending on a user's existing file.
        using(var zip=ZipFile.Open(book,ZipArchiveMode.Update))
        {
            var doc=ReadXml(zip,"xl/worksheets/sheet1.xml");
            foreach(var row in doc.Descendants(Ns+"row"))
            {
                int n=(int)row.Attribute("r");
                foreach(var cell in row.Elements(Ns+"c").Reverse().ToArray())
                {
                    string address=(string)cell.Attribute("r");int col=0;foreach(char c in address.TakeWhile(char.IsLetter))col=col*26+c-'A'+1;
                    if(col<3)continue;col+=2;string letters="";for(int x=col;x>0;x=(x-1)/26)letters=(char)('A'+(x-1)%26)+letters;
                    cell.SetAttributeValue("r",letters+n);
                }
                row.Add(new XElement(Ns+"c",new XAttribute("r","C"+n),new XAttribute("t","inlineStr"),new XElement(Ns+"is",new XElement(Ns+"t",n==1?"역할":"OLD_ROLE"))),
                    new XElement(Ns+"c",new XAttribute("r","D"+n),new XAttribute("t","inlineStr"),new XElement(Ns+"is",new XElement(Ns+"t",n==1?"성격":"OLD_PERSONALITY"))));
            }
            var control=doc.Descendants(Ns+"c").Single(c=>(string)c.Attribute("r")=="H4");control.RemoveNodes();control.SetAttributeValue("t","inlineStr");control.Add(new XElement(Ns+"is",new XElement(Ns+"t","0")));
            WriteXml(zip,"xl/worksheets/sheet1.xml",doc);
        }
        byte[] legacy=File.ReadAllBytes(book);LocalStoryNotebook.Refresh(store);
        Check(LocalStoryNotebook.Contexts(store,Array.Empty<string>()).Single(c=>c.characterId=="Guide").autoGenerate==false,"existing auto-generation control reset");
        Check(File.ReadAllBytes(Path.Combine(root,"Migrations/BeforeSimpleRecords/Chapter_00.xlsx")).SequenceEqual(legacy),"old controls backup differs");
        Check(Rows(book,"NPC설정").Single(r=>r[0]=="Guide")[5]=="0","legacy column mapping wrong");

        string fixture=Path.Combine(root,"TemporaryContent.json");
        try
        {
            var people=(JArray)Document("Chapter_00/Characters")["characters"];
            var person=new JObject {["characterId"]="CleanupFixture",["name"]="임시 검사 NPC",["role"]="임시 역할",["personality"]="임시 성격",["publicFacts"]=new JArray(),
                ["hidden"]=new JObject {["cleanup_fixture_secret"]=new JObject {["requiresUnlock_ALL"]=new JArray("cleanup:secret"),["text"]="임시 비밀",["storyPath"]=Guide+"/CleanupFixture/Second"}}};
            people.Add(person);
            var docs=(Dictionary<string,JObject>)typeof(ChapterContentCatalog).GetField("documents",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
            const string source="Chapter_00/Chapter_00_01/CleanupFixture/Story",explanation="Chapter_00/Chapter_00_01/CleanupFixture/Explanation";
            docs[source]=new JObject();docs[explanation]=new JObject();var stories=(JArray)Document("Catalog")["stories"];
            for(int i=0;i<2;i++)
            {
                string key=i==0?"cleanup_fixture":"cleanup_fixture/second",path=Guide+"/CleanupFixture"+(i==0?"":"/Second");
                docs[source][key]=new JObject {["MainStory"]=new JArray(new JObject {["ID"]=910010+i,["type"]="Choice",["eventIDstable"]="cleanup_fixture_"+i,["speakID"]="CleanupFixture",
                    ["Text"]="임시 검사 선택",["choiceKind"]="Mechanic",["options"]=new JArray("하나","둘","셋","넷"),["choiceActions"]=new JArray(Enumerable.Range(0,4).Select(n=>new JObject {["choiceID"]="cleanup_fixture_"+i+"_"+n,["action"]="continue"}))})};
                docs[explanation][key]=new JObject {["storyFlame"]=null,["coreMeaning"]=new JObject()};
                stories.Add(new JObject {["path"]=path,["chapterId"]="Initial",["chapterFolder"]="Chapter_00",["episodeId"]="Initial_01",["characterId"]="CleanupFixture",["placeId"]="cleanup_fixture_"+i,["chunkIndex"]=i+5,["key"]=key,["storyFile"]=source+".json",["explanationFile"]=explanation+".json"});
            }
            var reward=new JObject {["id"]="cleanup_fixture_reward",["characterId"]="CleanupFixture",["choiceIds"]=new JArray("cleanup_fixture_0_0"),["requiresItems"]=new JArray("prototype_loki_mark"),["grantsUnlocks"]=new JArray("cleanup:secret")};
            ((JArray)Document("Chapter_00/Terrain")["interactions"]).Add(reward);NarrativeInteractions.ResetCache();
            File.WriteAllText(fixture,new JObject {["character"]=person.DeepClone(),["story"]=docs[source].DeepClone(),["explanation"]=docs[explanation].DeepClone(),["reward"]=reward.DeepClone()}.ToString());
            using(var locked=File.Open(book,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                LocalStoryNotebook.Refresh(store);
                Check(!Rows(book,"NPC설정").Any(r=>r[0]=="CleanupFixture"),"locked workbook was replaced");
            }
            LocalStoryNotebook.Refresh(store);
            Check(Rows(book,"NPC설정").Count(r=>r[0]=="CleanupFixture")==1,"new NPC not automatically reflected");
            Check(Rows(book,"해금규칙").Any(r=>r[0]=="cleanup:secret"&&r[3]=="0"),"new unlock not reflected");
            Check(Rows(book,"아이템보상").Any(r=>r[1]=="cleanup_fixture_reward"&&r[2]=="1. cleanup_fixture_0_0"),"new choiceID/reward not reflected");
            Check(ChapterContentCatalog.Stories.Count(s=>(string)s["characterId"]=="CleanupFixture")==2,"same NPC across two chunks missing");
        }
        finally {ChapterContentCatalog.Reset();if(File.Exists(fixture))File.Delete(fixture);LocalStoryNotebook.Refresh(store);}
        Check(!Rows(book,"NPC설정").Any(r=>r[0]=="CleanupFixture")&&!Rows(book,"아이템보상").Any(r=>r[1]=="cleanup_fixture_reward"),"temporary content remains");
        Check(!File.Exists(fixture)&&ChapterContentCatalog.Character("CleanupFixture")==null,"temporary author content was not removed");
        return new{passed=true,checks,root,temporaryContentRemoved=true,choiceIdsPreserved=true};
    }
}
