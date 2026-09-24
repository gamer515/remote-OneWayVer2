using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public class AIAPIClient : MonoBehaviour
{
    private static AIAPIClient _instance;
    public static AIAPIClient Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<AIAPIClient>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("AIAPIClient");
                    _instance = go.AddComponent<AIAPIClient>();
                    DontDestroyOnLoad(go);
                }
            }
            return _instance;
        }
    }

    [Header("Gemini API Settings")]
    [SerializeField] private string apiKey = "YOUR_API_KEY";
    private string apiUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key=";

    // 문제 1(Race Condition) 방어용 플래그
    public bool isAiProcessing { get; private set; } = false;
    private readonly Queue<StoryPacket> pendingPackets = new Queue<StoryPacket>();
    private readonly HashSet<string> queuedPacketKeys = new HashSet<string>();

    #region JSON Serialization Classes for Gemini API
    [Serializable] private class GeminiRequest { public List<Content> contents; public GenConfig generationConfig; }
    [Serializable] private class Content { public List<Part> parts; }
    [Serializable] private class Part { public string text; }
    [Serializable] private class GenConfig { public string response_mime_type; }

    [Serializable] private class GeminiResponse { public List<Candidate> candidates; }
    [Serializable] private class Candidate { public Content content; }

    // AI가 반환할 수정된 텍스트 구조체
    [Serializable]
    public class AIModifiedData
    {
        public string encounterPath;
        public int cardIndex;
        public string text;
    }
    #endregion

    private void Awake()
    {
        if (_instance == null) { _instance = this; DontDestroyOnLoad(gameObject); }
        else if (_instance != this) { Destroy(gameObject); }
    }

    public void ProcessPacket(StoryPacket packet)
    {
        if (packet == null || string.IsNullOrWhiteSpace(packet.fileName))
            return;

        string packetKey = CreatePacketKey(packet);
        if (!queuedPacketKeys.Add(packetKey))
        {
            Debug.Log($"[AI SYSTEM] 이미 대기 또는 처리 중인 요청입니다: {packetKey}");
            return;
        }

        pendingPackets.Enqueue(packet);
        UpdateGenerationStatus(packet, ContentGenerationStatus.Pending);
        if (!isAiProcessing)
            StartCoroutine(ProcessPacketQueue());
    }

    private IEnumerator ProcessPacketQueue()
    {
        isAiProcessing = true;
        while (pendingPackets.Count > 0)
        {
            StoryPacket packet = pendingPackets.Dequeue();
            Debug.Log($"<color=#42f590><b>[AI SYSTEM - STARTING API CALL]</b> {packet.fileName}</color>");
            UpdateGenerationStatus(packet, ContentGenerationStatus.Generating);
            yield return CommunicateWithGeminiRoutine(packet);
            queuedPacketKeys.Remove(CreatePacketKey(packet));
        }

        isAiProcessing = false;
    }

    private IEnumerator CommunicateWithGeminiRoutine(StoryPacket packet)
    {
        // 1. 요청 페이로드 세팅 (JSON 형태로 응답을 강제함)
        GeminiRequest requestData = new GeminiRequest
        {
            contents = new List<Content> { new Content { parts = new List<Part> { new Part { text = packet.finalPrompt } } } },
            generationConfig = new GenConfig { response_mime_type = "application/json" }
        };

        string jsonPayload = JsonUtility.ToJson(requestData);

        using (UnityWebRequest request = new UnityWebRequest(apiUrl + apiKey, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            // API 호출 후 응답 대기 (TempAttackScene에서 플레이하는 동안 알아서 진행됨)
            yield return request.SendWebRequest();

            // 2. 에러 처리 (문제 2 해결: 네트워크 에러 시 Fallback)
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[AI SYSTEM] API 통신 실패: {request.error}\n원본 스토리를 유지합니다.");
                UpdateGenerationStatus(packet, ContentGenerationStatus.Failed, request.error);
                yield break;
            }

            // 3. 성공 시 데이터 파싱 및 저장
            try
            {
                GeminiResponse responseData = JsonUtility.FromJson<GeminiResponse>(request.downloadHandler.text);
                string generatedJson = responseData.candidates[0].content.parts[0].text;

                // 프롬프트에서 [ { } ] 형태의 배열로 응답하도록 지시했으므로 래퍼(wrapper)를 씌워 파싱
                string wrappedJson = $"{{\"items\": {generatedJson}}}";
                AIModifiedData[] modifiedItems = JsonHelper.FromJson<AIModifiedData>(wrappedJson);

                if (!TryValidateModifiedItems(packet, modifiedItems, out string validationError))
                {
                    Debug.LogError($"[AI SYSTEM] 응답 검증 실패: {validationError}\n원본 스토리를 유지합니다.");
                    UpdateGenerationStatus(packet, ContentGenerationStatus.Failed, validationError);
                    yield break;
                }

                if (ApplyAndSave(packet, modifiedItems))
                    UpdateGenerationStatus(packet, ContentGenerationStatus.Ready);
                else
                    UpdateGenerationStatus(packet, ContentGenerationStatus.Failed, "생성 이야기 저장 실패");
            }
            catch (Exception e)
            {
                Debug.LogError($"[AI SYSTEM] JSON 파싱 에러: {e.Message}\n원본 스토리를 유지합니다.");
                UpdateGenerationStatus(packet, ContentGenerationStatus.Failed, e.Message);
            }
        }
    }

    private bool ApplyAndSave(StoryPacket packet, AIModifiedData[] modifiedItems)
    {
        if (packet?.encounterHistory == null || modifiedItems == null)
            return false;

        Dictionary<string, List<AIModifiedData>> itemsByEncounter =
            new Dictionary<string, List<AIModifiedData>>(StringComparer.Ordinal);
        foreach (AIModifiedData item in modifiedItems)
        {
            if (!itemsByEncounter.TryGetValue(item.encounterPath, out List<AIModifiedData> items))
            {
                items = new List<AIModifiedData>();
                itemsByEncounter.Add(item.encounterPath, items);
            }
            items.Add(item);
        }

        foreach (KeyValuePair<string, List<AIModifiedData>> group in itemsByEncounter)
        {
            EncounterCardRoot original = SaveIOService.Instance.LoadResourceData<EncounterCardRoot>(
                group.Key + "/Story");
            if (original?.MainStory == null)
                return false;

            foreach (AIModifiedData item in group.Value)
            {
                if (item.cardIndex < 0 || item.cardIndex >= original.MainStory.Count)
                    return false;
                original.MainStory[item.cardIndex].text = item.text;
            }

            if (!SaveIOService.Instance.SaveGeneratedContent(
                    packet.targetRun,
                    "Encounters",
                    group.Key + "/Story",
                    original))
                return false;

            Debug.Log(
                $"<color=#f5e642><b>[AI SYSTEM] {packet.targetRun}회차 Encounter 저장 완료: " +
                $"{group.Key}/Story</b></color>");
        }

        return itemsByEncounter.Count > 0;
    }

    private static string CreatePacketKey(StoryPacket packet)
    {
        return $"{packet.targetRun}:{packet.fileName}";
    }

    private static void UpdateGenerationStatus(
        StoryPacket packet,
        ContentGenerationStatus status,
        string errorMessage = null)
    {
        SaveIOService.Instance.UpdateGeneratedEpisodeStatus(
            packet.sourceRun,
            packet.targetRun,
            packet.fileName,
            status,
            errorMessage);
    }

    private static bool TryValidateModifiedItems(
        StoryPacket packet,
        AIModifiedData[] modifiedItems,
        out string errorMessage)
    {
        if (packet?.encounterHistory == null || packet.encounterHistory.Count == 0)
        {
            errorMessage = "요청에 변경 대상 지문이 없습니다.";
            return false;
        }

        if (modifiedItems == null || modifiedItems.Length != packet.encounterHistory.Count)
        {
            errorMessage = "응답 항목 수가 요청한 지문 수와 다릅니다.";
            return false;
        }

        Dictionary<string, PlayedEncounterCardRecord> originalsByKey =
            new Dictionary<string, PlayedEncounterCardRecord>(StringComparer.Ordinal);
        foreach (PlayedEncounterCardRecord record in packet.encounterHistory)
        {
            if (record?.card == null || string.IsNullOrWhiteSpace(record.encounterPath))
            {
                errorMessage = "요청 Encounter 카드의 경로 또는 원문이 없습니다.";
                return false;
            }

            string key = CreateEncounterCardKey(record.encounterPath, record.cardIndex);
            if (record.cardIndex < 0 || originalsByKey.ContainsKey(key))
            {
                errorMessage = "요청 Encounter 카드의 순번이 잘못되었거나 중복되었습니다.";
                return false;
            }
            originalsByKey.Add(key, record);
        }

        HashSet<string> responseKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (AIModifiedData item in modifiedItems)
        {
            string key = item == null
                ? null
                : CreateEncounterCardKey(item.encounterPath, item.cardIndex);
            if (item == null || string.IsNullOrWhiteSpace(item.encounterPath) ||
                !responseKeys.Add(key) || !originalsByKey.ContainsKey(key))
            {
                errorMessage = $"응답에 null, 중복 또는 요청하지 않은 Encounter 카드가 있습니다: " +
                    $"{item?.encounterPath}#{item?.cardIndex}";
                return false;
            }

            if (string.IsNullOrWhiteSpace(item.text))
            {
                errorMessage = $"{item.encounterPath}#{item.cardIndex}의 변경 문장이 비어 있습니다.";
                return false;
            }


            if (!HasSameImmutableCores(originalsByKey[key].card.text, item.text))
            {
                errorMessage = $"{item.encounterPath}#{item.cardIndex}의 {{ }} 핵심 문자열이 변경되었습니다.";
                return false;
            }
        }

        errorMessage = null;
        return true;
    }

    private static string CreateEncounterCardKey(string encounterPath, int cardIndex)
    {
        return $"{encounterPath}\n{cardIndex}";
    }

    private static bool HasSameImmutableCores(string originalText, string generatedText)
    {
        MatchCollection originalCores = Regex.Matches(originalText ?? string.Empty, @"\{[^{}]*\}");
        MatchCollection generatedCores = Regex.Matches(generatedText ?? string.Empty, @"\{[^{}]*\}");
        if (originalCores.Count != generatedCores.Count)
            return false;

        for (int i = 0; i < originalCores.Count; i++)
        {
            if (!string.Equals(
                originalCores[i].Value,
                generatedCores[i].Value,
                StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}

// 최상위 JSON 배열 파싱을 위한 헬퍼 클래스 (JsonUtility의 한계 극복용)
public static class JsonHelper
{
    public static T[] FromJson<T>(string json)
    {
        Wrapper<T> wrapper = JsonUtility.FromJson<Wrapper<T>>(json);
        return wrapper.items;
    }

    [Serializable]
    private class Wrapper<T>
    {
        public T[] items;
    }
}
