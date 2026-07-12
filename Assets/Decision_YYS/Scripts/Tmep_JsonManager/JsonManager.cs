using UnityEngine;
using System.IO;

public class JsonManager : IJsonSerializer
{
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
