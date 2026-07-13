using UnityEngine;
using System.IO;


//Main 씬, 결정 씬, 전투 씬에 각각 저장이 될 수 있게 Main 씬에서 부터 메모리에 올라가고 싱글톤으로 구성하면 좋을 듯?
public class JsonManager
{
    // Saves 파일에는 진행도, 전투, 결정 이렇게 세개의 파일로 나누어서 각각 저장.
    // 결정된 이야기의 경우는 게임 플레이가 진행이 끝나면 _1 이렇게 나누어서 회차별로 저장.
    // Main 씬에서 그걸 토대로 삭제 및 불어오기 기능 구현.

    private static JsonManager _instance;

    public static JsonManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new JsonManager();
            }
            return _instance;
        }
    }

    private JsonManager() { }

    public T LoadData<T>(string fileName)
    {
        // 1. 빌드 환경에서도 읽고 쓰기가 가능한 유저 데이터 폴더 경로
        string savePath = Path.Combine(Application.persistentDataPath, fileName + ".json");

        // 2. 만약 AI가 수정한 세이브 파일이 존재한다면, 그걸 우선적으로 읽습니다. (2회차 이상)
        if (File.Exists(savePath))
        {
            string json = File.ReadAllText(savePath);
            Debug.Log($"[JsonManager] 수정된 세이브 데이터를 불러옵니다: {fileName} (경로: {savePath})");
            return JsonUtility.FromJson<T>(json);
        }
        else
        {
            // 3. 세이브 파일이 없다면(1회차), Resources 폴더에 있는 원본을 읽어옵니다.
            // 파일이 Assets/Decision_YYS/Resources/Story_Json_Data/ 폴더 안에 있어야 합니다.
            TextAsset textAsset = Resources.Load<TextAsset>("Story_Json_Data/" + fileName);
            
            if (textAsset == null)
            {
                Debug.LogError($"[JsonManager] 원본 JSON 파일도 찾을 수 없습니다. 파일명: {fileName}, 예상 경로: Resources/Story_Json_Data/{fileName}");
                return default;
            }

            Debug.Log($"[JsonManager] 원본 리소스 데이터를 불러옵니다: {fileName}");
            return JsonUtility.FromJson<T>(textAsset.text);
        }
    }

    public void SaveData<T>(T data, string fileName)
    {
        // 4. AI가 수정한 데이터를 저장할 때는 항상 읽고 쓰기가 가능한 persistentDataPath에 저장합니다.
        string savePath = Path.Combine(Application.persistentDataPath, fileName + ".json");
        string json = JsonUtility.ToJson(data, true);
        
        File.WriteAllText(savePath, json);
        Debug.Log($"[JsonManager] AI 수정 데이터 저장 완료: {savePath}");
    }
}
