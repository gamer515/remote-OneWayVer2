using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;


public class SaveManager
{
    private static SaveManager _instance;

    public static SaveManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new SaveManager();
            }
            return _instance;
        }
    }

    // 저장 파일이 위치할 기본 경로
    private string BasePath => Path.Combine(Application.persistentDataPath, "Saves");
    //private string ScenarioPath => Path.Combine(BasePath, "Scenario");

    private int scenarioIndex = 0;

    public int ScenarioIndex
    {
        get => scenarioIndex;
        set 
        {
            ++scenarioIndex;
        }
    }

    private SaveManager() 
    {
        if (!Directory.Exists(BasePath))
        {
            Directory.CreateDirectory(BasePath);
        }
    }

    public void Save<T>(string fileName, T data)
    {
        string subFolder = "";

        if(data is ScenarioData scenarioData)
        {
            subFolder = "Scenario" + scenarioIndex;
        }

        string directoryPath = Path.Combine(BasePath, subFolder);

        string path = Path.Combine(directoryPath, $"{fileName}.json");
        if(!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        string json = JsonUtility.ToJson(data, true);

        File.WriteAllText(path, json);

        Debug.Log($"[SaveManager] 데이터 저장 성공: {path}");
    }

    public T Load<T>(string fileName) 
    {
        string subFolder = "";

        if(typeof(T) == typeof(ScenarioData))
        {
            subFolder = "Scenario" + scenarioIndex;
        }

        string directoryPath = Path.Combine(BasePath, subFolder);
        string path = Path.Combine(directoryPath, $"{fileName}.json");
        if (!File.Exists(path)) return default;

        string json = File.ReadAllText(path);

        return JsonUtility.FromJson<T>(json);
    }

    public bool Exists(string category) 
    {
        return File.Exists(Path.Combine(BasePath, $"{category}.json"));
    }

    public string[] GetAllSaveFiles()
    {
        if (!Directory.Exists(BasePath)) return new string[0];

        string[] files = Directory.GetFiles(BasePath, "*.json");
        for (int i = 0; i < files.Length; i++)
        {
            files[i] = Path.GetFileNameWithoutExtension(files[i]);
        }

        return files;
    }

    public void DeleteAllSaves()
    {
        if (Directory.Exists(BasePath))
        {
            Directory.Delete(BasePath, true);
            Directory.CreateDirectory(BasePath);
        }
    }

    public T LoadData<T>(string fileName, string resourcesSubFolder = "Story_Json_Data")
    {
        // 1. 빌드 환경에서도 읽고 쓰기가 가능한 유저 데이터 폴더 경로
        string saveFolder = "";

        if(typeof(T) == typeof(ScenarioData))
        {
            saveFolder = "scenario" + scenarioIndex;
        }

        string savePath = Path.Combine(BasePath, saveFolder, $"{fileName}.json");

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
            string resourcePathr = string.IsNullOrEmpty(resourcesSubFolder) ? fileName : $"{resourcesSubFolder}/{fileName}";

            TextAsset textAsset = Resources.Load<TextAsset>(resourcePathr);

            if (textAsset == null)
            {
                Debug.LogError($"[JsonManager] 원본 JSON 파일도 찾을 수 없습니다. 파일명: {resourcePathr}");

                return default;
            }

            Debug.Log($"[JsonManager] 원본 리소스 데이터를 불러옵니다: {resourcePathr}");

            return JsonUtility.FromJson<T>(textAsset.text);
        }
    }
}
