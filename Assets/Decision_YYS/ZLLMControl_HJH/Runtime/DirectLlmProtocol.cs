using System;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OneWay.Decision.DirectLlm
{
    // Internal: URLs, headers and bodies must never become diagnostic messages.
    internal static class DirectLlmProtocol
    {
        internal static JToken Parse(string json)
        {
            try
            {
                return JToken.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            }
            catch (JsonException) { throw new DirectLlmFailure("InvalidJson"); }
        }

        internal static JArray Messages(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("messages JSON is required.");
            var messages = Parse(json) as JArray;
            if (messages == null || messages.Count == 0 || messages.Any(m => !(m is JObject) ||
                m["role"]?.Type != JTokenType.String || m["content"]?.Type != JTokenType.String ||
                !new[] { "system", "user", "assistant" }.Contains((string)m["role"]) ||
                ((JObject)m).Properties().Any(p => p.Name != "role" && p.Name != "content")) ||
                !messages.Any(m => (string)m["role"] == "user"))
                throw new ArgumentException("messages must contain role/content text messages and at least one user message.");
            return messages;
        }

        internal static DirectLlmHttpRequest Request(DirectLlmEndpoint endpoint, DirectLlmCredentials credentials,
            JArray messages, int maxTokens, int seed, int timeoutSeconds)
        {
            if (credentials == null || string.IsNullOrWhiteSpace(credentials.ApiKey) ||
                credentials.ApiKey.Any(c => c <= 32 || c >= 127)) throw new DirectLlmFailure("MissingOrInvalidKey");
            string model = endpoint.model?.Trim();
            if (string.IsNullOrWhiteSpace(model)) throw new DirectLlmFailure("MissingModel");
            string url, header, value;
            JObject body;
            if (endpoint.provider == DirectLlmProvider.Gemini)
            {
                if (model.StartsWith("models/", StringComparison.Ordinal)) model = model.Substring(7);
                if (!Regex.IsMatch(model, @"^[A-Za-z0-9._-]+$")) throw new DirectLlmFailure("InvalidModel");
                url = "https://generativelanguage.googleapis.com/v1beta/models/" + Uri.EscapeDataString(model) + ":generateContent";
                header = "x-goog-api-key"; value = credentials.ApiKey;
                var contents = new JArray(messages.Where(m => (string)m["role"] != "system").Select(m => new JObject
                {
                    ["role"] = (string)m["role"] == "assistant" ? "model" : "user",
                    ["parts"] = new JArray(new JObject { ["text"] = (string)m["content"] })
                }));
                body = new JObject { ["contents"] = contents, ["generationConfig"] = new JObject
                { ["maxOutputTokens"] = maxTokens, ["responseMimeType"] = "application/json", ["seed"] = seed } };
                var systems = messages.Where(m => (string)m["role"] == "system").ToArray();
                if (systems.Length > 0) body["systemInstruction"] = new JObject
                { ["parts"] = new JArray(systems.Select(m => new JObject { ["text"] = (string)m["content"] })) };
            }
            else
            {
                header = "Authorization"; value = "Bearer " + credentials.ApiKey;
                body = new JObject { ["model"] = model, ["stream"] = false, ["messages"] = messages.DeepClone() };
                if (endpoint.provider == DirectLlmProvider.Groq)
                {
                    url = "https://api.groq.com/openai/v1/chat/completions";
                    body["max_completion_tokens"] = maxTokens;
                    body["seed"] = seed;
                    body["response_format"] = new JObject { ["type"] = "json_object" };
                    if (model.StartsWith("openai/gpt-oss-", StringComparison.Ordinal))
                    { body["include_reasoning"] = false; body["reasoning_effort"] = "low"; }
                }
                else if (endpoint.provider == DirectLlmProvider.Cloudflare)
                {
                    if (!Regex.IsMatch(credentials.AccountId ?? "", @"^[A-Fa-f0-9]{32}$")) throw new DirectLlmFailure("InvalidCloudflareAccount");
                    url = "https://api.cloudflare.com/client/v4/accounts/" + credentials.AccountId + "/ai/v1/chat/completions";
                    body["max_tokens"] = maxTokens;
                }
                else throw new DirectLlmFailure("UnknownProvider");
            }
            return new DirectLlmHttpRequest(endpoint.provider, url, header, value, body.ToString(Formatting.None), timeoutSeconds);
        }

        internal static DirectLlmResult Response(DirectLlmProvider provider, string raw)
        {
            try
            {
                var root = Parse(raw) as JObject;
                if (root == null || root["error"] != null || (bool?)root["success"] == false) throw new DirectLlmFailure("ProviderError");
                string content;
                var result = new DirectLlmResult { Provider = provider };
                if (provider == DirectLlmProvider.Gemini)
                {
                    var candidate = root["candidates"]?.First;
                    if ((string)candidate?["finishReason"] != "STOP") throw new DirectLlmFailure("IncompleteOrBlocked");
                    var parts = candidate["content"]?["parts"] as JArray;
                    if (parts == null) throw new DirectLlmFailure("EmptyResponse");
                    content = string.Concat(parts.Where(p => (bool?)p["thought"] != true && p["text"]?.Type == JTokenType.String).Select(p => (string)p["text"]));
                    result.Model = (string)root["modelVersion"];
                    result.InputTokens = (int?)root["usageMetadata"]?["promptTokenCount"] ?? -1;
                    result.OutputTokens = (int?)root["usageMetadata"]?["candidatesTokenCount"] ?? -1;
                }
                else
                {
                    var choice = root["choices"]?.First;
                    if ((string)choice?["finish_reason"] != "stop") throw new DirectLlmFailure("IncompleteOrBlocked");
                    var message = choice["message"];
                    if (!string.IsNullOrWhiteSpace((string)message?["refusal"])) throw new DirectLlmFailure("Refused");
                    if (message?["content"]?.Type != JTokenType.String) throw new DirectLlmFailure("EmptyResponse");
                    content = (string)message["content"];
                    result.Model = (string)root["model"];
                    result.InputTokens = (int?)root["usage"]?["prompt_tokens"] ?? -1;
                    result.OutputTokens = (int?)root["usage"]?["completion_tokens"] ?? -1;
                }
                result.CardsJson = Cards(content);
                return result;
            }
            catch (DirectLlmFailure) { throw; }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is FormatException || e is OverflowException)
            { throw new DirectLlmFailure("InvalidResponse"); }
        }

        internal static string Cards(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new DirectLlmFailure("EmptyResponse");
            text = text.Trim();
            if (text.StartsWith("```", StringComparison.Ordinal))
            {
                int newline = text.IndexOf('\n');
                if (newline < 0 || !text.EndsWith("```", StringComparison.Ordinal) || text.Length <= newline + 4)
                    throw new DirectLlmFailure("InvalidCards");
                string label = text.Substring(0, newline).Trim();
                if (label != "```" && label != "```json") throw new DirectLlmFailure("InvalidCards");
                text = text.Substring(newline + 1, text.Length - newline - 4).Trim();
            }
            var root = Parse(text) as JObject;
            var cards = root?["cards"] as JArray;
            if (root == null || root.Properties().Count() != 1 || cards == null || cards.Count < 1 || cards.Count > 64)
                throw new DirectLlmFailure("InvalidCards");
            var normalized = new JArray();
            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i] as JObject;
                if (card == null || card.Properties().Count() != 3 || card["index"]?.Type != JTokenType.Integer ||
                    (long)card["index"] != i || card["text"]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)card["text"]) ||
                    ((string)card["text"]).Length > 160 || !(card["options"] is JArray options) || options.Count > 4 ||
                    options.Any(o => o.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)o) || ((string)o).Length > 45))
                    throw new DirectLlmFailure("InvalidCards");
                // The game's narrow parser expects this property order. Never change the wording.
                normalized.Add(new JObject { ["index"] = i, ["text"] = card["text"].DeepClone(), ["options"] = options.DeepClone() });
            }
            return new JObject { ["cards"] = normalized }.ToString(Formatting.None);
        }
    }
}
