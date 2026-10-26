using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public sealed class StoryGenerationConfiguration
{
    public string mode = "FirstCompleted";
    public string apiUrl = "http://127.0.0.1:20128/v1/chat/completions";
    public string combo = "explain";
    public int timeoutSeconds = 120;
}

/// <summary>Transport settings are independent of the shared writing rules. Never contains credentials.</summary>
public static class StoryGenerationSettings
{
    public static StoryGenerationConfiguration Read()
    {
        var asset = Resources.Load<TextAsset>("LocalStory/GenerationSettings");
        var value = asset == null ? new StoryGenerationConfiguration() :
            JsonUtility.FromJson<StoryGenerationConfiguration>(asset.text);
        if (value == null || (value.mode != "FirstCompleted" && value.mode != "Embedded" && value.mode != "OmniRoute") ||
            string.IsNullOrWhiteSpace(value.combo) || value.timeoutSeconds < 1 || value.timeoutSeconds > 300 ||
            !Uri.TryCreate(value.apiUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("LocalStory/GenerationSettings의 mode, apiUrl, combo, timeoutSeconds를 확인하세요.");
        return value;
    }

    public static string ApiKey()
    {
        string key = Environment.GetEnvironmentVariable("OMNIROUTE_API_KEY");
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        // Also works for an Editor that was already open when the user configured their key.
        if (string.IsNullOrWhiteSpace(key)) key = Environment.GetEnvironmentVariable("OMNIROUTE_API_KEY", EnvironmentVariableTarget.User);
#endif
        return key?.Trim();
    }
}

/// <summary>Project adaptation of the supplied converter: common story messages in, cards JSON out.</summary>
public static class OmniRouteFreeStackConverter
{
    public static JObject RequestBody(string messages, int maxTokens, int seed, string combo)
    {
        var parsed = JArray.Parse(messages);
        if (parsed.Count == 0) throw new FormatException("OmniRoute messages가 비어 있습니다.");
        foreach (var message in parsed)
            if (!(message is JObject) || message["content"]?.Type != JTokenType.String ||
                Array.IndexOf(new[] { "system", "user", "assistant" }, (string)message["role"]) < 0)
                throw new FormatException("OmniRoute 메시지 역할/본문 형식 오류");
        return new JObject { ["model"] = combo, ["stream"] = false, ["messages"] = parsed,
            ["max_tokens"] = maxTokens, ["seed"] = seed, ["temperature"] = 0.7, ["top_p"] = 0.8 };
    }

    public static string Content(string response)
    {
        var root = JObject.Parse(response);
        string text = (string)root["choices"]?[0]?["message"]?["content"];
        if (string.IsNullOrWhiteSpace(text)) throw new FormatException("OmniRoute 대사 응답이 비어 있습니다.");
        text = text.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            int start = text.IndexOf('\n'), end = text.LastIndexOf("```", StringComparison.Ordinal);
            if (start < 0 || end <= start) throw new FormatException("OmniRoute JSON 코드 블록이 잘렸습니다.");
            text = text.Substring(start + 1, end - start - 1).Trim();
        }
        var cards = JObject.Parse(text);
        if (!(cards["cards"] is JArray)) throw new FormatException("OmniRoute 응답에 cards 배열이 없습니다.");
        return cards.ToString(Formatting.None);
    }

    public static async Task<LocalStoryModelOutput> Generate(string messages, int maxTokens, int seed,
        CancellationToken token, StoryGenerationConfiguration configuration = null)
    {
        var config = configuration ?? StoryGenerationSettings.Read();
        token.ThrowIfCancellationRequested();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        LocalStoryLifetime.Register(lifetime);
        try
        {
            token = lifetime.Token;
            byte[] bytes = Encoding.UTF8.GetBytes(RequestBody(messages, maxTokens, seed, config.combo).ToString(Formatting.None));
            using var request = new UnityWebRequest(config.apiUrl, "POST") {
                uploadHandler = new UploadHandlerRaw(bytes), downloadHandler = new DownloadHandlerBuffer(), timeout = config.timeoutSeconds };
            request.SetRequestHeader("Content-Type", "application/json");
            string key = StoryGenerationSettings.ApiKey();
            if (!string.IsNullOrWhiteSpace(key)) request.SetRequestHeader("Authorization", "Bearer " + key);
            var watch = Stopwatch.StartNew();
            var operation = request.SendWebRequest();
            using var abort = token.Register(() => request.Abort());
            try
            {
                while (!operation.isDone) { token.ThrowIfCancellationRequested(); await Task.Yield(); }
                token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) { request.Abort(); throw; }
            // Never include the server body or request headers in errors (may contain account details).
            if (request.result != UnityWebRequest.Result.Success)
                throw new InvalidOperationException("OmniRoute 요청 실패: HTTP " + request.responseCode + " / " + request.result);
            var response = JObject.Parse(request.downloadHandler.text);
            string model = request.GetResponseHeader("X-OmniRoute-Model") ?? (string)response["model"] ?? config.combo;
            string provider = request.GetResponseHeader("X-OmniRoute-Provider") ?? "provider-unreported";
            return new LocalStoryModelOutput { json = Content(request.downloadHandler.text),
                backend = "OmniRoute / " + provider + " / " + model, firstTextSeconds = -1,
                generationSeconds = watch.Elapsed.TotalSeconds,
                inputTokens = (int?)response["usage"]?["prompt_tokens"] ?? -1,
                outputTokens = (int?)response["usage"]?["completion_tokens"] ?? -1,
                tokenLimitReached = (string)response["choices"]?[0]?["finish_reason"] == "length" };
        }
        finally { LocalStoryLifetime.Unregister(lifetime); }
    }
}
