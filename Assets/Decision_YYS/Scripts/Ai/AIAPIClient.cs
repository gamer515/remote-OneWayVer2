using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;

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

    #region JSON Serialization Classes for Gemini API
    [Serializable] private class GeminiRequest { public List<Content> contents; public GenConfig generationConfig; }
    [Serializable] private class Content { public List<Part> parts; }
    [Serializable] private class Part { public string text; }
    [Serializable] private class GenConfig { public string response_mime_type; }

    [Serializable] private class GeminiResponse { public List<Candidate> candidates; }
    [Serializable] private class Candidate { public Content content; }

    // AI가 반환할 수정된 텍스트 구조체
    [Serializable] public class AIModifiedData { public int id; public string text; }
    #endregion

    private void Awake()
    {
        if (_instance == null) { _instance = this; DontDestroyOnLoad(gameObject); }
        else if (_instance != this) { Destroy(gameObject); }
    }

    public void ProcessPacket(StoryPacket packet)
    {
        Debug.Log("<color=#42f590><b>[AI SYSTEM - STARTING API CALL]</b></color>");
        // 백그라운드 코루틴 시작 (메인 스레드를 멈추지 않고 씬 전환 가능)
        StartCoroutine(CommunicateWithGeminiRoutine(packet));
    }

    private IEnumerator CommunicateWithGeminiRoutine(StoryPacket packet)
    {
        isAiProcessing = true; // 처리 시작 상태 플래그 ON

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
                ApplyAndSave(packet, null);
                isAiProcessing = false;
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

                ApplyAndSave(packet, modifiedItems);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AI SYSTEM] JSON 파싱 에러: {e.Message}\n원본 스토리를 유지합니다.");
                ApplyAndSave(packet, null);
            }
        }

        isAiProcessing = false; // 처리 완료 상태 플래그 OFF
    }

    private void ApplyAndSave(StoryPacket packet, AIModifiedData[] modifiedItems)
    {
        if (string.IsNullOrEmpty(packet.fileName)) return;

        ScenarioData originalData = SaveIOService.Instance.LoadData<ScenarioData>(packet.fileName);
        if (originalData == null || originalData.MainStory == null) return;

        if(modifiedItems != null)
        {
            // 원본 데이터에 AI 수정 텍스트 덮어쓰기
            foreach (var item in modifiedItems)
            {
                var targetDialogue = originalData.MainStory.Find(d => d.id == item.id);
                if (targetDialogue != null)
                {
                    targetDialogue.text = item.text;
                    Debug.Log($"<color=cyan>[AI System] ID {targetDialogue.id} 스토리 교체 완료</color>");
                }
            }
        }
        else
        {
            Debug.LogWarning("<color=yellow>[AI System] 수정된 데이터가 없습니다. 원본 스토리를 유지합니다.</color>");
        }

        string saveFileName = "NewStory_" + packet.fileName.Replace("/", "_");
        SaveIOService.Instance.Save(saveFileName, originalData);
        Debug.Log($"<color=#f5e642><b>[AI SYSTEM] 최종 스토리 저장 완료: {saveFileName}</b></color>");
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