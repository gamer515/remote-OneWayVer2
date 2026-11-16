using System;
using System.Collections.Generic;
using System.Linq;
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

    public static async Task<string> Run()
    {
        checks = 0;
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
        return "PASS: " + checks + " assertions; no network or save writes; integration story=" + path;
    }
}
