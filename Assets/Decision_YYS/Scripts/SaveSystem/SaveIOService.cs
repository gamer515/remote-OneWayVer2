using System.IO;
using UnityEngine;

/// <summary>
/// 파일 데이터 직렬화 처리.
/// </summary>
public class SaveIOService
{
    private static SaveIOService _instance;

    public static SaveIOService Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new SaveIOService();
            }
            return _instance;
        }
    }

    // 저장 파일이 위치할 기본 경로
    private string BasePath => Path.Combine(Application.persistentDataPath, "Saves");
    private string ProfilePath => Path.Combine(BasePath, "Profile.json");

    private int scenarioIndex = 0;

    public int ScenarioIndex
    {
        get => scenarioIndex;
        set 
        {
            ++scenarioIndex;
        }
    }

    private SaveIOService() 
    {
        if (!Directory.Exists(BasePath))
        {
            Directory.CreateDirectory(BasePath);
        }
    }

    /// <summary>
    /// 현재 회차를 담은 프로필을 불러오며, 없으면 1회차 프로필을 생성합니다.
    /// </summary>
    public ProfileData LoadOrCreateProfile()
    {
        if (File.Exists(ProfilePath))
        {
            ProfileData loadedProfile = ReadJson<ProfileData>(ProfilePath);
            if (loadedProfile != null)
            {
                loadedProfile.currentRun = Mathf.Max(1, loadedProfile.currentRun);
                return loadedProfile;
            }
        }

        ProfileData newProfile = new ProfileData();
        SaveProfile(newProfile);
        return newProfile;
    }

    public void SaveProfile(ProfileData profile)
    {
        if (profile == null)
            return;

        profile.currentRun = Mathf.Max(1, profile.currentRun);
        Directory.CreateDirectory(BasePath);
        File.WriteAllText(ProfilePath, JsonUtility.ToJson(profile, true));
        Debug.Log($"[SaveIO] 프로필 저장 성공: {ProfilePath}");
    }

    /// <summary>
    /// 진행도와 능력치처럼 특정 회차에 속하는 데이터를 저장합니다.
    /// </summary>
    public void SaveRunData<T>(int runNumber, string fileName, T data)
    {
        string directoryPath = GetRunDirectory(runNumber);
        Directory.CreateDirectory(directoryPath);

        string path = Path.Combine(directoryPath, $"{fileName}.json");
        File.WriteAllText(path, JsonUtility.ToJson(data, true));
        Debug.Log($"[SaveIO] {Mathf.Max(1, runNumber)}회차 데이터 저장 성공: {path}");
    }

    /// <summary>
    /// 회차 데이터를 불러옵니다. 기존 루트 세이브는 삭제하지 않고 1회차 폴더로 복사합니다.
    /// </summary>
    public T LoadRunData<T>(int runNumber, string fileName)
    {
        string path = GetRunDataPath(runNumber, fileName);
        if (File.Exists(path))
            return ReadJson<T>(path);

        string legacyPath = Path.Combine(BasePath, $"{fileName}.json");
        if (runNumber == 1 && File.Exists(legacyPath))
        {
            T legacyData = ReadJson<T>(legacyPath);
            SaveRunData(runNumber, fileName, legacyData);
            Debug.Log($"[SaveIO] 기존 {fileName} 세이브를 1회차 폴더로 복사했습니다.");
            return legacyData;
        }

        return default;
    }

    public bool RunDataExists(int runNumber, string fileName)
    {
        if (File.Exists(GetRunDataPath(runNumber, fileName)))
            return true;

        return runNumber == 1 && File.Exists(Path.Combine(BasePath, $"{fileName}.json"));
    }

    private string GetRunDirectory(int runNumber)
    {
        int safeRunNumber = Mathf.Max(1, runNumber);
        return Path.Combine(BasePath, "Runs", $"Run_{safeRunNumber:D4}");
    }

    private string GetRunDataPath(int runNumber, string fileName)
    {
        return Path.Combine(GetRunDirectory(runNumber), $"{fileName}.json");
    }

    private static T ReadJson<T>(string path)
    {
        return JsonUtility.FromJson<T>(File.ReadAllText(path));
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
