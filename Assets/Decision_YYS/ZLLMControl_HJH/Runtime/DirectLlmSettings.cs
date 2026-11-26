using System;
using System.Collections.Generic;
using UnityEngine;

namespace OneWay.Decision.DirectLlm
{
    public enum DirectLlmProvider { Gemini, Groq, Cloudflare }

    [Serializable]
    public sealed class DirectLlmEndpoint
    {
        public DirectLlmProvider provider;
        public bool enabled = true;
        public string model;
        public DirectLlmEndpoint(DirectLlmProvider provider, string model)
        { this.provider = provider; this.model = model; }
    }

    [Serializable]
    public sealed class DirectLlmOptions
    {
        [Range(3, 120)] public int timeoutSeconds = 30;
        [Tooltip("요청(이야기 구간)마다 첫 공급자를 순환합니다. 실패하면 다음 공급자를 한 번씩 시도합니다.")]
        public bool roundRobin = true;
        public List<DirectLlmEndpoint> endpoints = new List<DirectLlmEndpoint>
        {
            new DirectLlmEndpoint(DirectLlmProvider.Gemini, "gemini-2.5-flash"),
            new DirectLlmEndpoint(DirectLlmProvider.Groq, "openai/gpt-oss-20b"),
            new DirectLlmEndpoint(DirectLlmProvider.Cloudflare, "@cf/meta/llama-3.1-8b-instruct-fast")
        };
    }

    /// <summary>Credentials are deliberately not serialized into Unity assets.</summary>
    [CreateAssetMenu(menuName = "Decision/Direct LLM Settings", fileName = "DirectLlmSettings")]
    public sealed class DirectLlmSettings : ScriptableObject
    {
        public DirectLlmOptions options = new DirectLlmOptions();
        public DirectLlmClient CreateClient() => new DirectLlmClient(options);

        public static DirectLlmClient CreateProjectClient(bool gameplay = false)
        {
            var options = Resources.Load<DirectLlmSettings>(nameof(DirectLlmSettings))?.options ?? new DirectLlmOptions();
            return gameplay
                ? new DirectLlmClient(options, requestTimeoutSeconds: Math.Min(options.timeoutSeconds, 12),
                    providerCooldown: TimeSpan.FromMinutes(2))
                : new DirectLlmClient(options);
        }
    }

    public sealed class DirectLlmCredentials
    {
        public string ApiKey { get; }
        public string AccountId { get; }
        public DirectLlmCredentials(string apiKey, string accountId = null)
        { ApiKey = apiKey?.Trim(); AccountId = accountId?.Trim(); }

        public static DirectLlmCredentials FromEnvironment(DirectLlmProvider provider)
        {
            string name = provider == DirectLlmProvider.Gemini ? "GEMINI_API_KEY" :
                provider == DirectLlmProvider.Groq ? "GROQ_API_KEY" : "CLOUDFLARE_API_TOKEN";
            return new DirectLlmCredentials(Read(name), provider == DirectLlmProvider.Cloudflare ? Read("CLOUDFLARE_ACCOUNT_ID") : null);
        }

        private static string Read(string name)
        {
#if UNITY_EDITOR
            string inspectorValue = ReadInspectorValue(name);
            if (!string.IsNullOrWhiteSpace(inspectorValue)) return inspectorValue;
#endif
            string value = Environment.GetEnvironmentVariable(name);
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (string.IsNullOrWhiteSpace(value)) value = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
#endif
            return value;
        }

#if UNITY_EDITOR
        private static string InspectorKey(string name) => "OneWay.DirectLlm." + Application.dataPath + "." + name;

        public static string ReadInspectorValue(string name) => UnityEditor.EditorPrefs.GetString(InspectorKey(name), "");

        public static void SaveInspectorValue(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) UnityEditor.EditorPrefs.DeleteKey(InspectorKey(name));
            else UnityEditor.EditorPrefs.SetString(InspectorKey(name), value.Trim());
        }
#endif
    }
}
