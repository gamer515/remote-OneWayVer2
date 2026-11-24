using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OneWay.Decision.DirectLlm;
using UnityEngine;

public static class VerifyDirectLlm
{
    private const string Messages = "[{\"role\":\"system\",\"content\":\"Return cards JSON; keep {무력}.\"},{\"role\":\"user\",\"content\":\"한국어 원문\"}]";
    private const string Cards = "{\"cards\":[{\"options\":[],\"text\":\"네 {무력}을 보여라.\",\"index\":0}]}";
    private static int checks;
    private static void Check(bool condition, string name)
    { if (!condition) throw new Exception("FAILED: " + name); checks++; }

    private sealed class Fake : IDirectLlmTransport
    {
        public readonly List<DirectLlmHttpRequest> Calls = new List<DirectLlmHttpRequest>();
        public Func<DirectLlmHttpRequest, CancellationToken, Task<DirectLlmHttpResponse>> Handler;
        public Task<DirectLlmHttpResponse> SendAsync(DirectLlmHttpRequest request, CancellationToken token)
        { Calls.Add(request); return Handler == null ? Task.FromResult(Reply(request.Provider, Cards)) : Handler(request, token); }
    }
    private static DirectLlmCredentials Credentials(DirectLlmProvider provider) => new DirectLlmCredentials("test-not-a-real-key", new string('a', 32));
    private static DirectLlmClient Client(Fake fake) => new DirectLlmClient(new DirectLlmOptions(), fake, Credentials);
    private static DirectLlmHttpResponse Reply(DirectLlmProvider provider, string cards, string finish = null)
    {
        JObject body = provider == DirectLlmProvider.Gemini ? new JObject
        {
            ["modelVersion"] = "test-model", ["usageMetadata"] = new JObject { ["promptTokenCount"] = 7, ["candidatesTokenCount"] = 9 },
            ["candidates"] = new JArray(new JObject { ["finishReason"] = finish ?? "STOP", ["content"] = new JObject
            { ["parts"] = new JArray(new JObject { ["text"] = "ignored reasoning", ["thought"] = true }, new JObject { ["text"] = cards }) } })
        } : new JObject
        {
            ["model"] = "test-model", ["usage"] = new JObject { ["prompt_tokens"] = 7, ["completion_tokens"] = 9 },
            ["choices"] = new JArray(new JObject { ["finish_reason"] = finish ?? "stop", ["message"] = new JObject { ["content"] = cards } })
        };
        return new DirectLlmHttpResponse { StatusCode = 200, TransportSucceeded = true, Body = body.ToString(Formatting.None) };
    }

    public static void CheckDialogue()
    {
        var line = new LocalStoryEmotionLine { name = "브룩", role = "광부", personality = "솔직함",
            speechStyle = "반말", publicFacts = new[] { "갱도에서 일함" } };
        string original = "  " + new string('가', 60) + "\n";
        string rewritten = "  정말 반가워!\n" + new string('아', 40) + "  ";
        string dialogueJson = new JObject { ["text"] = rewritten }.ToString(Formatting.None);
        foreach (DirectLlmProvider provider in Enum.GetValues(typeof(DirectLlmProvider)))
        {
            var fake = new Fake { Handler = (request, token) => Task.FromResult(Reply(request.Provider, dialogueJson,
                request.Provider == DirectLlmProvider.Gemini ? "MAX_TOKENS" : "length")) };
            var options = new DirectLlmOptions();
            foreach (var endpoint in options.endpoints) endpoint.enabled = endpoint.provider == provider;
            var generator = new DirectStoryGenerator(new DirectLlmClient(options, fake, Credentials));
            Check(generator.RewriteDialogueAsync(line, "기쁨", original, CancellationToken.None).GetAwaiter().GetResult().Text == rewritten,
                "plain dialogue response retained verbatim");
            var body = JObject.Parse(fake.Calls.Single().Body);
            Check(body["generationConfig"]?["maxOutputTokens"] == null &&
                body["max_tokens"] == null && body["max_completion_tokens"] == null, "no output token limits");
            Check(provider == DirectLlmProvider.Gemini
                ? (string)body["generationConfig"]["responseMimeType"] == "application/json"
                : (string)body["response_format"]["type"] == "json_object", "JSON response format requested");
            string prompt = DialoguePrompt(fake.Calls.Single());
            Check(prompt.Contains("35~85자") && prompt.Contains("사실") && prompt.Contains("존댓말"),
                "content instructions and numeric range on every provider");
            var input = JObject.Parse(prompt.Substring(prompt.IndexOf("\n\n", StringComparison.Ordinal) + 2));
            Check(input.Count == 3 && (string)input["npc"]["personality"] == line.personality &&
                (string)input["emotion"] == "기쁨" && (string)input["original"] == original, "NPC, emotion and source dialogue");
        }
    }

    private static string DialoguePrompt(DirectLlmHttpRequest request)
    {
        var body = JObject.Parse(request.Body);
        return request.Provider == DirectLlmProvider.Gemini
            ? (string)body["contents"].Single()["parts"].Single()["text"]
            : (string)body["messages"].Last["content"];
    }

    private static string DialogueJson(string text) => new JObject { ["text"] = text }.ToString(Formatting.None);

    private static async Task CheckDialogueLength()
    {
        string original = new string('가', 60);
        string minimum = " \t" + new string('가', 33) + " !\n";
        string maximum = new string('나', 85);
        var line = new LocalStoryEmotionLine();
        foreach (string invalid in new[] { new string('가', 34), new string('가', 86) })
        {
            var fake = new Fake { Handler = (r, t) => Task.FromResult(Reply(r.Provider,
                DialogueJson(r.Provider == DirectLlmProvider.Gemini ? invalid : minimum))) };
            var result = await new DirectStoryGenerator(Client(fake)).RewriteDialogueAsync(line, "기쁨", original, CancellationToken.None);
            Check(result.Text == minimum && fake.Calls.Count == 2 &&
                result.Attempts[0].Code == "DialogueLengthOutOfRange", "off by one retries and lower bound is accepted verbatim");
        }

        string shortOriginal = "  안녕! ";
        Check(LocalStoryDialogueLength.Range(shortOriginal) == (1, 28) &&
            !LocalStoryDialogueLength.Allows(shortOriginal, " \t\r\n") &&
            LocalStoryDialogueLength.Allows(shortOriginal, " 네 "), "short original, whitespace and minimum of one");
        Check(LocalStoryGemmaDialogue.Messages(line, "기쁨", shortOriginal, nextRun: true).Contains("1~28자"),
            "Gemma uses the same numeric range");

        var exhausted = new Fake { Handler = (r, t) => Task.FromResult(Reply(r.Provider, DialogueJson(new string('가', 86)))) };
        try
        {
            await new DirectStoryGenerator(Client(exhausted)).RewriteDialogueAsync(line, "기쁨", original, CancellationToken.None);
            throw new Exception("expected dialogue rejection");
        }
        catch (DirectLlmGenerationException error)
        {
            Check(error.Attempts.Count == 3 && error.Attempts.All(a => a.Code == "DialogueLengthOutOfRange"),
                "all external providers reject long dialogue");
        }

        var completeLine = typeof(LocalStoryDialogueQueue).GetMethod("CompleteLine", BindingFlags.NonPublic | BindingFlags.Static);
        completeLine.Invoke(null, new object[] { line, shortOriginal, (maximum, "Gemma", "mock") });
        Check(line.finished && line.emotionFallback && line.generatedText == null && line.originalText == shortOriginal,
            "invalid final Gemma candidate leaves the original available without truncation");
        completeLine.Invoke(null, new object[] { line, original, (maximum, "Gemma", "mock") });
        Check(line.finished && !line.emotionFallback && line.generatedText == maximum, "valid Gemma candidate at upper bound");

        string root = Path.GetFullPath(Path.Combine("Builds", "DialogueLengthReview", Guid.NewGuid().ToString("N")));
        var constructor = typeof(SaveIOService).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance,
            null, new[] { typeof(string) }, null);
        var store = (SaveIOService)constructor.Invoke(new object[] { root });
        store.SaveProfile(new ProfileData { currentRun = 1 });
        var job = new LocalStoryDialogueJob
        {
            jobId = "test-length", storyPath = "test-length", sourceRun = 1, targetRun = 2,
            authoredPack = true, emotion = "기쁨",
            emotionLines = new List<LocalStoryEmotionLine>
            {
                new LocalStoryEmotionLine { cardIndex = 0, originalText = original },
                new LocalStoryEmotionLine { cardIndex = 1, originalText = shortOriginal },
                new LocalStoryEmotionLine { cardIndex = 2, originalText = shortOriginal, generatedText = maximum, finished = true }
            }
        };
        LocalStoryDialogueQueue.Save(store, new LocalStoryDialogueQueueData
            { sourceRun = 1, targetRun = 2, finalized = true, jobs = new List<LocalStoryDialogueJob> { job } });
        var queueFake = new Fake { Handler = (r, t) => Task.FromResult(Reply(r.Provider,
            DialogueJson(DialoguePrompt(r).Contains("35~85자") ? maximum : "네"))) };
        await LocalStoryDialogueQueue.Process(store, job, new DirectStoryGenerator(Client(queueFake)), CancellationToken.None);
        var saved = LocalStoryDialogueQueue.Load(store, 1).jobs.Single();
        Check(queueFake.Calls.Count == 2 && DialoguePrompt(queueFake.Calls[0]).Contains("35~85자") &&
            DialoguePrompt(queueFake.Calls[1]).Contains("1~28자"), "unavailable script preserves individual source ranges");
        Check(saved.status == "Ready" && saved.emotionLines.Select(l => l.generatedText)
            .SequenceEqual(new[] { maximum, "네", maximum }), "queue persists each valid result and preserves the previously finished line");
    }

    private static async Task CheckDialogueContent()
    {
        const string original = "이젠 돌보다 수저가 더 무겁구먼.";
        const string recorded = "귀관의 도움으로 본인의 책무를 다했습니다. 감사합니다.";
        const string valid = "이젠 돌보다 수저가 더 묵직하구먼.";
        var line = new LocalStoryEmotionLine();
        Check(LocalStoryDialogueLength.Allows(original, recorded), "the recorded Garen reply passes length alone");
        var fake = new Fake { Handler = (r, t) => Task.FromResult(Reply(r.Provider,
            DialogueJson(r.Provider == DirectLlmProvider.Gemini ? recorded : valid))) };
        var result = await new DirectStoryGenerator(Client(fake)).RewriteDialogueAsync(line, "기쁨", original, CancellationToken.None);
        Check(result.Text == valid && fake.Calls.Count == 2 && result.Attempts[0].Code == "DialogueContentChanged",
            "content rejection still tries the next external provider");
        Check(LocalStoryDialogueRules.Rejection("여기서 코인 2개를 내.", "여기서 코인 3개를 내.") == "DialogueContentChanged" &&
            LocalStoryDialogueRules.Rejection("여기서 코인 2개를 내.", "여기서 코인 2개를 내세요.") == "DialogueContentChanged" &&
            LocalStoryDialogueRules.Rejection("여기서는 코인을 내지 말자.", "여기서는 코인을 내자.") == "DialogueContentChanged",
            "numbers, speech register and negation are protected");
        Check(LocalStoryDialogueRules.Rejection("▥※▣ 없어도 ▣▨※?", "힘이 없어도 광부지?") == "DialogueContentChanged",
            "masked words are protected");
        Check(LocalStoryDialogueRules.Rejection("여기는 낡은 갱도야.", "여기는 정말 낡은 갱도야.") == null &&
            LocalStoryDialogueRules.Rejection("여기는 정말 낡은 갱도야.", "여기는 낡은 갱도야.") == null &&
            LocalStoryDialogueRules.Rejection("여기는 오래된 좁은 갱도야.", "여기는  오래된 깊은 갱도야.") == "DialogueContentChanged",
            "single phrase insertions and deletions remain valid while unrelated whitespace changes are rejected");
        var completeLine = typeof(LocalStoryDialogueQueue).GetMethod("CompleteLine", BindingFlags.NonPublic | BindingFlags.Static);
        completeLine.Invoke(null, new object[] { line, original, (recorded, "Gemma", "mock") });
        Check(line.finished && line.emotionFallback && line.generatedText == null && line.originalText == original,
            "Gemma result within range is rejected for changed content");
    }

    private static async Task CheckLocationDialogue(bool partial)
    {
        const string path = "AuthoredMine/MAIN-02";
        string artifacts = Path.GetFullPath(Path.Combine("Builds", "CompactDialogueReview"));
        Directory.CreateDirectory(artifacts);
        string root = Path.Combine(artifacts, Guid.NewGuid().ToString("N"));
        var constructor = typeof(SaveIOService).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance,
            null, new[] { typeof(string) }, null);
        var store = (SaveIOService)constructor.Invoke(new object[] { root });
        store.SaveProfile(new ProfileData { currentRun = 2 });
        store.SaveRunData(1, "Progress", new GameProgress { runSeed = 9125, isCompleted = true });
        var script = MineLearningStation.ReadContent("Stories/CHUNK-02/MAIN-02_V1");
        var people = MineLearningStation.ReadContent("Relationships/NPC_전체목록")["npcs"].ToDictionary(p => (string)p["npc_id"]);
        var sourceRows = script["opening"].OfType<JObject>()
            .Append((JObject)script.SelectToken("choices[0].coin_options[0].dialogue[0]")).ToArray();
        var jobs = sourceRows.GroupBy(row => (string)row["speaker_id"]).Select((group, index) =>
        {
            var person = people[group.Key];
            return new LocalStoryDialogueJob
            {
                jobId = path + "|" + group.Key + "|Short", storyPath = path, placeId = "MAIN-02",
                authoredPack = true, authoredStoryPackMode = "Short", characterId = group.Key,
                model = DirectStoryGenerator.ModelName, sourceRun = 1, targetRun = 2, revision = 1, sequence = index + 1,
                emotion = index == 0 ? "당황" : "존중",
                emotionLines = group.Select((row, i) => new LocalStoryEmotionLine
                {
                    cardIndex = i, authoredLinePath = row.Path, originalText = (string)row["text"],
                    speakerId = group.Key, name = (string)person["name"], role = (string)person["role"],
                    personality = (string)person["personality"], speechStyle = (string)person["speech_style"],
                    publicFacts = person["character_traits"].Values<string>().ToArray()
                }).ToList()
            };
        }).ToList();
        LocalStoryDialogueQueue.Save(store, new LocalStoryDialogueQueueData
        {
            sourceRun = 1, targetRun = 2, authoredStoryPackMode = "Short", finalized = true, jobs = jobs
        });

        const string edit = "갱목 아래쪽에 흰 가루가 쌓였네.";
        string prefix = partial ? "partial" : "success";
        string Original(string tokenPath) => (string)script.SelectToken(tokenPath)["text"];
        var retryOriginals = new List<string>();
        using var cancel = new CancellationTokenSource();
        var fake = new Fake();
        var options = new DirectLlmOptions();
        foreach (var endpoint in options.endpoints) endpoint.enabled = endpoint.provider == DirectLlmProvider.Gemini;
        var generator = new DirectStoryGenerator(new DirectLlmClient(options, fake, Credentials));
        fake.Handler = (request, _) =>
        {
            string prompt = DialoguePrompt(request);
            if (prompt.StartsWith("{", StringComparison.Ordinal))
            {
                var input = JObject.Parse(prompt);
                File.WriteAllText(Path.Combine(artifacts, prefix + "-request.json"), JObject.Parse(request.Body).ToString());
                var sections = (JArray)input["sections"];
                var editable = sections.SelectMany(section => (JArray)section["lines"]).OfType<JArray>().ToArray();
                Check(editable.Length == 6 && editable.Select(row => (string)row[1]).Distinct().Count() == 2,
                    "MAIN-02 batch contains six editable lines from two NPC jobs");
                Check(input.Count == 3 && (string)input["location"] == "금 간 갱목" && sections.Count == 2 &&
                    (string)sections[0]["branch"] == "공통 도입" &&
                    ((string)sections[1]["context"]).Contains("힘 코인 2개"),
                    "only the requested opening and payment branch are included, with relevant choice context");
                foreach (var row in editable)
                    Check(row.Count == 4 && int.TryParse((string)row[0], out int _) &&
                        (string)row[2] == jobs.Single(job => job.characterId == (string)row[1]).emotion,
                        "short IDs, four array fields and existing job emotion");
                Check(((JObject)input["npcs"]).Count == 2 && editable.Select(row => (string)row[0]).Distinct().Count() == 6,
                    "only two actual NPC profiles and six distinct short IDs");
                var output = new JArray();
                // Return out of order to verify ID-based routing even if the provider ignores ordering.
                foreach (var row in editable.Reverse())
                {
                    string id = (string)row[0];
                    string lineOriginal = (string)row[3];
                    if (partial && lineOriginal == Original("opening[3]")) continue;
                    string text = lineOriginal == Original("opening[0]") ? edit : lineOriginal;
                    if (partial && lineOriginal == Original("opening[4]")) text += new string('가', 26);
                    var reply = new JObject { ["id"] = id, ["text"] = text };
                    output.Add(reply);
                    if (partial && lineOriginal == Original("opening[2]")) output.Add(reply.DeepClone());
                }
                if (partial) output.Add(new JObject { ["id"] = "not-an-input-id", ["text"] = "이 대사는 저장하면 안 돼." });
                var response = new JObject { ["lines"] = output };
                File.WriteAllText(Path.Combine(artifacts, prefix + "-response.json"), response.ToString());
                return Task.FromResult(Reply(request.Provider, response.ToString(Formatting.None)));
            }
            string original = (string)JObject.Parse(prompt.Substring(prompt.IndexOf("\n\n", StringComparison.Ordinal) + 2))["original"];
            if (!cancel.IsCancellationRequested)
            {
                cancel.Cancel(); // Interrupt the first failed row after the valid batch rows have been checkpointed.
                return Task.FromCanceled<DirectLlmHttpResponse>(cancel.Token);
            }
            retryOriginals.Add(original);
            return Task.FromResult(Reply(request.Provider, DialogueJson(original)));
        };

        if (partial)
        {
            try
            {
                await LocalStoryDialogueQueue.Process(store, jobs[0], generator, cancel.Token);
                throw new InvalidOperationException("Expected an interrupted retry");
            }
            catch (OperationCanceledException) { }
            var saved = LocalStoryDialogueQueue.Load(store, 1).jobs.SelectMany(job => job.emotionLines).ToArray();
            Check(fake.Calls.Count == 2 && saved.Count(line => line.finished) == 3 &&
                saved.Where(line => !line.finished).All(line => line.locationBatchAttempted),
                "partial success and failed IDs are checkpointed before the cancelled retry");
        }
        var reopened = (SaveIOService)constructor.Invoke(new object[] { root });
        while (LocalStoryDialogueQueue.Next(reopened) is LocalStoryDialogueJob next)
            await LocalStoryDialogueQueue.Process(reopened, next, generator, CancellationToken.None);
        var final = LocalStoryDialogueQueue.Load(reopened, 1);
        Check(final.jobs.All(job => job.status == "Ready") &&
            final.jobs.SelectMany(job => job.emotionLines).Single(line => line.authoredLinePath == "opening[0]").generatedText == edit,
            "both NPC jobs commit and the changed text is saved against its source path");
        Check(final.jobs.SelectMany(job => job.emotionLines).All(line => line.generatedText ==
            (line.authoredLinePath == "opening[0]" ? edit : line.originalText)), "every other response is stored on its own source line");
        if (partial)
            Check(retryOriginals.SequenceEqual(new[] { "opening[2]", "opening[3]", "opening[4]" }
                .Select(p => (string)script.SelectToken(p)["text"])), "duplicates, omissions, and content violations alone are retried");
        int calls = fake.Calls.Count;
        foreach (var job in jobs) await LocalStoryDialogueQueue.Process(reopened, job, generator, CancellationToken.None);
        Check(calls == (partial ? 5 : 1) && fake.Calls.Count == calls && LocalStoryDialogueQueue.Next(reopened) == null,
            "success, partial retries and reopening have the expected request count");
        File.WriteAllText(Path.Combine(artifacts, prefix + "-count.txt"),
            $"HTTP requests: {calls}; batches: 1; resumed line retries: {retryOriginals.Count}; duplicate requests after completion: 0");
    }

    private static async Task CheckLegacyLocationDialogue()
    {
        string root = Path.GetFullPath(Path.Combine("Builds", "LocationDialogueReview", Guid.NewGuid().ToString("N")));
        var constructor = typeof(SaveIOService).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance,
            null, new[] { typeof(string) }, null);
        var store = (SaveIOService)constructor.Invoke(new object[] { root });
        store.SaveProfile(new ProfileData { currentRun = 2 });
        const string opening = "Chapter_1/Chapter_1_01/Encounters/brook";
        var jobs = new[] { opening, opening + "_pay" }.Select((path, index) =>
        {
            string source = ChapterContentCatalog.Story(path);
            var lines = JsonUtility.FromJson<EncounterCardRoot>(source).MainStory
                .Select((card, i) => LocalStoryEmotion.CreateLine("Chapter_1", card, i)).Where(line => line != null);
            return new LocalStoryDialogueJob
            {
                jobId = path, storyPath = path, placeId = "brook", revision = 1, sequence = index + 1,
                sourceRun = 1, targetRun = 2, sourceHash = LocalStoryDialogueQueue.Hash(source), emotion = "감사",
                model = DirectStoryGenerator.ModelName, emotionLines = lines.Take(1).ToList()
            };
        }).ToList();
        LocalStoryDialogueQueue.Save(store, new LocalStoryDialogueQueueData
            { sourceRun = 1, targetRun = 2, finalized = true, jobs = jobs });
        var fake = new Fake { Handler = (request, _) =>
        {
            var sections = (JArray)JObject.Parse(DialoguePrompt(request))["sections"];
            var editable = sections.SelectMany(section => (JArray)section["lines"]).ToArray();
            Check(editable.Length == 2 && sections.Any(section => (string)section["branch"] == "brook_pay" && section["context"] != null),
                "legacy opening and linked payment branch are sent as one location");
            return Task.FromResult(Reply(request.Provider, new JObject
            {
                ["lines"] = new JArray(editable.Select(row => new JObject
                    { ["id"] = row[0].DeepClone(), ["text"] = row[3].DeepClone() }))
            }.ToString(Formatting.None)));
        } };
        foreach (var job in jobs)
            await LocalStoryDialogueQueue.Process(store, job, new DirectStoryGenerator(Client(fake)), CancellationToken.None);
        Check(fake.Calls.Count == 1 && LocalStoryDialogueQueue.Load(store, 1).jobs.All(job => job.status == "Ready"),
            "linked legacy scripts use one request and keep both existing per-story commits");
    }

    public static async Task<string> Run()
    {
        checks = 0;
        CheckDialogue();
        await CheckDialogueLength();
        await CheckDialogueContent();
        await CheckLocationDialogue(false);
        await CheckLocationDialogue(true);
        await CheckLegacyLocationDialogue();
        var fake = new Fake(); var client = Client(fake);
        var first = await client.GenerateAsync(Messages, 512, 17, CancellationToken.None);
        Check(first.Provider == DirectLlmProvider.Gemini && fake.Calls.Count == 1, "success stops further providers");
        Check(first.InputTokens == 7 && first.OutputTokens == 9, "usage extraction");
        Check(first.CardsJson.StartsWith("{\"cards\":[{\"index\":0,\"text\":"), "canonical game parser order");
        var gemini = JObject.Parse(fake.Calls[0].Body);
        Check((string)gemini["systemInstruction"]["parts"][0]["text"] == "Return cards JSON; keep {무력}." &&
            (string)gemini["contents"][0]["parts"][0]["text"] == "한국어 원문", "Gemini preserves prompt");
        Check(fake.Calls[0].HeaderName == "x-goog-api-key" && !fake.Calls[0].Url.Contains("test-not-a-real-key"), "Gemini key stays out of URL");
        await client.GenerateAsync(Messages, 512, 17, CancellationToken.None);
        var groq = JObject.Parse(fake.Calls[1].Body);
        Check(fake.Calls[1].Provider == DirectLlmProvider.Groq && JToken.DeepEquals(groq["messages"], JArray.Parse(Messages)), "Groq rotation and prompt");
        Check((int)groq["max_completion_tokens"] == 512 && (bool)groq["include_reasoning"] == false && groq["reasoning_format"] == null, "Groq GPT OSS parameters");
        await client.GenerateAsync(Messages, 512, 17, CancellationToken.None);
        var cloudflare = JObject.Parse(fake.Calls[2].Body);
        Check(fake.Calls[2].Provider == DirectLlmProvider.Cloudflare && (int)cloudflare["max_tokens"] == 512 && cloudflare["max_completion_tokens"] == null, "Cloudflare token parameter");
        await client.GenerateAsync(Messages, 512, 17, CancellationToken.None);
        Check(fake.Calls[3].Provider == DirectLlmProvider.Gemini, "round robin wraps");

        fake = new Fake { Handler = (r, t) => Task.FromResult(r.Provider == DirectLlmProvider.Gemini ?
            new DirectLlmHttpResponse { StatusCode = 429, Body = "test-not-a-real-key" } : Reply(r.Provider, "```json\n" + Cards + "\n```")) };
        var fallback = await Client(fake).GenerateAsync(Messages, 512, 17, CancellationToken.None);
        Check(fake.Calls.Count == 2 && fallback.Provider == DirectLlmProvider.Groq && fallback.Attempts[0].HttpStatus == 429, "HTTP fallback");

        foreach (string invalid in new[] { "{\"replacements\":[\"x\"]}", "{\"cards\":[{\"index\":0,\"index\":0,\"text\":\"x\",\"options\":[]}]}",
            "{\"cards\":[{\"index\":1,\"text\":\"x\",\"options\":[]}]}", "{\"cards\":[{\"index\":0,\"text\":\"x\",\"options\":[],\"startAction\":\"bad\"}]}" })
        {
            fake = new Fake { Handler = (r, t) => Task.FromResult(Reply(r.Provider, r.Provider == DirectLlmProvider.Gemini ? invalid : Cards)) };
            var recovered = await Client(fake).GenerateAsync(Messages, 512, 17, CancellationToken.None);
            Check(recovered.Provider == DirectLlmProvider.Groq && fake.Calls.Count == 2, "invalid cards fallback");
        }
        fake = new Fake { Handler = (r, t) => Task.FromResult(Reply(r.Provider, Cards, r.Provider == DirectLlmProvider.Gemini ? "MAX_TOKENS" : null)) };
        Check((await Client(fake).GenerateAsync(Messages, 512, 17, CancellationToken.None)).Provider == DirectLlmProvider.Groq, "truncated output rejected");

        var original = new EncounterCardRoot { MainStory = new List<EncounterCard> { new EncounterCard { type = "Next", text = "네 {무력}을 보여라." } } };
        fake = new Fake { Handler = (r, t) => Task.FromResult(Reply(r.Provider, r.Provider == DirectLlmProvider.Gemini ? Cards.Replace("무력", "지혜") : Cards)) };
        var valid = await Client(fake).GenerateAsync(Messages, 512, 17, CancellationToken.None,
            json => LocalStoryRewrite.TryBuild(json, original, null, out _, out _));
        Check(valid.Provider == DirectLlmProvider.Groq && valid.Attempts[0].Code == "GameValidationFailed", "fixed core mismatch fallback");

        fake = new Fake { Handler = (r, t) => Task.FromResult(new DirectLlmHttpResponse { StatusCode = 500, Body = "test-not-a-real-key" }) };
        try { await Client(fake).GenerateAsync(Messages, 512, 17, CancellationToken.None); throw new Exception("expected failure"); }
        catch (DirectLlmGenerationException e) { Check(fake.Calls.Count == 3 && e.Attempts.Count == 3 && !e.ToString().Contains("test-not-a-real-key"), "bounded failures and no secret diagnostics"); }

        using (var cancellation = new CancellationTokenSource())
        {
            fake = new Fake { Handler = (r, t) => { cancellation.Cancel(); t.ThrowIfCancellationRequested(); return Task.FromResult(Reply(r.Provider, Cards)); } };
            client = Client(fake);
            try { await client.GenerateAsync(Messages, 512, 17, cancellation.Token); throw new Exception("expected cancellation"); }
            catch (OperationCanceledException) { Check(fake.Calls.Count == 1 && !client.IsBusy, "cancellation has no fallback and releases busy"); }
        }
        fake = new Fake { Handler = (r, t) => r.Provider == DirectLlmProvider.Gemini ?
            Task.FromException<DirectLlmHttpResponse>(new OperationCanceledException()) : Task.FromResult(Reply(r.Provider, Cards)) };
        Check((await Client(fake).GenerateAsync(Messages, 512, 17, CancellationToken.None)).Attempts[0].Code == "Timeout", "provider timeout falls back");
        fake = new Fake(); client = Client(fake);
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            try { await client.GenerateAsync(Messages, 512, 17, cancellation.Token); throw new Exception("expected cancellation"); }
            catch (OperationCanceledException) { Check(fake.Calls.Count == 0, "pre-cancel no request"); }
        }
        try { await client.GenerateAsync("[]", 512, 17, CancellationToken.None); throw new Exception("expected invalid input"); }
        catch (ArgumentException) { Check(fake.Calls.Count == 0, "invalid input no request"); }

        var gate = new TaskCompletionSource<DirectLlmHttpResponse>();
        fake = new Fake { Handler = (r, t) => gate.Task }; client = Client(fake);
        var pending = client.GenerateAsync(Messages, 512, 17, CancellationToken.None);
        try { await client.GenerateAsync(Messages, 512, 17, CancellationToken.None); throw new Exception("expected busy"); }
        catch (InvalidOperationException) { Check(fake.Calls.Count == 1, "concurrent request rejected"); }
        gate.SetResult(Reply(DirectLlmProvider.Gemini, Cards)); await pending;

        fake = new Fake();
        var single = new DirectLlmOptions(); single.endpoints[0].enabled = false; single.endpoints[2].enabled = false;
        var singleClient = new DirectLlmClient(single, fake, Credentials); single.endpoints[1].enabled = false;
        await singleClient.GenerateAsync(Messages, 512, 17, CancellationToken.None);
        Check(fake.Calls.Count == 1 && fake.Calls[0].Provider == DirectLlmProvider.Groq, "disabled providers and config snapshot");

        // Run the real section/prompt pipeline against authored data, still with an in-memory fake HTTP transport.
        string path = (string)ChapterContentCatalog.Stories.First()["path"];
        var report = new LocalStoryTestReport { storyPath = path, seed = 17, allowFailedOutput = true,
            original = JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(path)) };
        string before = JsonUtility.ToJson(report.original);
        fake = new Fake { Handler = (r, t) =>
        {
            var request = JObject.Parse(r.Body);
            Check(request["systemInstruction"] != null, "adapter retains system prompt");
            string user = (string)request["contents"][0]["parts"][0]["text"];
            var variable = JObject.Parse(user.Split('\n').Last());
            int start = (int)variable["position"]["start"] - 1, count = (int)variable["position"]["count"];
            var cards = new JArray(report.original.MainStory.Skip(start).Take(count).Select((c, i) => new JObject
                { ["index"] = i, ["text"] = c.text, ["options"] = new JArray(c.options ?? Array.Empty<string>()) }));
            return Task.FromResult(Reply(r.Provider, new JObject { ["cards"] = cards }.ToString(Formatting.None)));
        } };
        var adapterOptions = new DirectLlmOptions(); adapterOptions.endpoints[1].enabled = adapterOptions.endpoints[2].enabled = false;
        await new DirectStoryGenerator(new DirectLlmClient(adapterOptions, fake, Credentials)).GenerateRuntimeAsync(report, CancellationToken.None);
        Check(fake.Calls.Count > 0 && report.sectionOutputs.Sum(s => s.count) == report.original.MainStory.Count, "adapter completes real section pipeline");
        Check(before == JsonUtility.ToJson(report.original) && report.backend.StartsWith("Direct / Gemini /"), "source immutable and direct backend recorded");
        return "PASS: " + checks + " assertions; fake HTTP and isolated queue; integration story=" + path;
    }
}
