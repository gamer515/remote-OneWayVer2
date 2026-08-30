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

    /// <summary>
    /// 빌드에 포함된 변경 불가능한 원본 JSON을 Resources에서 읽습니다.
    /// </summary>
    public T LoadResourceData<T>(string fileName, string resourcesSubFolder = "Story_Json_Data")
    {
        string resourcePath = string.IsNullOrEmpty(resourcesSubFolder)
            ? fileName
            : $"{resourcesSubFolder}/{fileName}";
        TextAsset textAsset = Resources.Load<TextAsset>(resourcePath);
        if (textAsset == null)
        {
            Debug.LogError($"[SaveIO] 원본 JSON을 찾을 수 없습니다: {resourcePath}");
            return default;
        }

        return JsonUtility.FromJson<T>(textAsset.text);
    }

    /// <summary>
    /// 특정 회차용 생성 콘텐츠를 읽습니다. 파일 부재와 파싱 실패는 호출자가 원본으로 대체할 수 있게 false를 반환합니다.
    /// </summary>
    public bool SaveGeneratedContent<T>(
        int runNumber,
        string contentType,
        string relativePath,
        T data)
    {
        if (ReferenceEquals(data, null) ||
            !TryGetGeneratedContentPath(runNumber, contentType, relativePath, out string path))
            return false;

        try
        {
            WriteJsonAtomically(path, data);
            Debug.Log($"[SaveIO] {runNumber}회차 생성 콘텐츠 저장 성공: {path}");
            return true;
        }
        catch (System.Exception exception)
        {
            Debug.LogError($"[SaveIO] 생성 콘텐츠 저장 실패: {path}\n{exception.Message}");
            return false;
        }
    }

    public void UpdateGeneratedEpisodeStatus(
        int sourceRun,
        int targetRun,
        string scenarioPath,
        ContentGenerationStatus status,
        string errorMessage = null)
    {
        if (sourceRun < 1 || targetRun <= sourceRun || string.IsNullOrWhiteSpace(scenarioPath))
            return;

        string manifestPath = Path.Combine(
            BasePath,
            "GeneratedContent",
            $"For_Run_{targetRun:D4}",
            "manifest.json");
        try
        {
            GeneratedContentManifest manifest = File.Exists(manifestPath)
                ? ReadJson<GeneratedContentManifest>(manifestPath)
                : new GeneratedContentManifest();

            if (manifest == null)
                manifest = new GeneratedContentManifest();
            manifest.sourceRun = sourceRun;
            manifest.targetRun = targetRun;
            if (manifest.episodes == null)
                manifest.episodes = new System.Collections.Generic.List<GeneratedEpisodeStatus>();

            GeneratedEpisodeStatus episode = manifest.episodes.Find(
                item => item != null && item.scenarioPath == scenarioPath);
            if (episode == null)
            {
                episode = new GeneratedEpisodeStatus { scenarioPath = scenarioPath };
                manifest.episodes.Add(episode);
            }

            episode.status = status.ToString();
            episode.errorMessage = errorMessage;
            WriteJsonAtomically(manifestPath, manifest);
        }
        catch (System.Exception exception)
        {
            Debug.LogError($"[SaveIO] 생성 상태 저장 실패: {manifestPath}\n{exception.Message}");
        }
    }

    public bool TryLoadGeneratedContent<T>(
        int runNumber,
        string contentType,
        string relativePath,
        out T data)
    {
        data = default;
        if (!TryGetGeneratedContentPath(
            runNumber,
            contentType,
            relativePath,
            out string path))
            return false;
        if (!File.Exists(path))
            return false;

        try
        {
            data = ReadJson<T>(path);
            return !ReferenceEquals(data, null);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[SaveIO] 생성 콘텐츠를 읽지 못했습니다: {path}\n{exception.Message}");
            data = default;
            return false;
        }
    }

    private bool TryGetGeneratedContentPath(
        int runNumber,
        string contentType,
        string relativePath,
        out string path)
    {
        path = null;
        if (runNumber <= 1 || string.IsNullOrWhiteSpace(contentType) ||
            string.IsNullOrWhiteSpace(relativePath))
            return false;

        string contentRoot = Path.GetFullPath(Path.Combine(
            BasePath,
            "GeneratedContent",
            $"For_Run_{runNumber:D4}",
            contentType));
        string normalizedRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        string candidatePath = Path.GetFullPath(
            Path.Combine(contentRoot, $"{normalizedRelativePath}.json"));

        // 전달된 상대 경로가 생성 콘텐츠 폴더 밖으로 나가지 못하게 제한합니다.
        string rootPrefix = contentRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidatePath.StartsWith(rootPrefix, System.StringComparison.OrdinalIgnoreCase))
            return false;

        path = candidatePath;
        return true;
    }

    private static void WriteJsonAtomically<T>(string path, T data)
    {
        string directoryPath = Path.GetDirectoryName(path);
        Directory.CreateDirectory(directoryPath);
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonUtility.ToJson(data, true));

        if (File.Exists(path))
            File.Replace(temporaryPath, path, null);
        else
            File.Move(temporaryPath, path);
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

    public void DeleteAllSaves()
    {
        if (Directory.Exists(BasePath))
        {
            Directory.Delete(BasePath, true);
            Directory.CreateDirectory(BasePath);
        }
    }

}
