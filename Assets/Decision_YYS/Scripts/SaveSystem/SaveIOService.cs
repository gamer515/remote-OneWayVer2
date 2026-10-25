using System.IO;
using UnityEngine;

/// <summary>
/// 파일 데이터 직렬화 처리.
/// </summary>
public partial class SaveIOService
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
    private readonly string basePathOverride;
    private string BasePath => basePathOverride ?? Path.Combine(Application.persistentDataPath, "Saves");
    public string StorageRoot => BasePath;
    private string ProfilePath => Path.Combine(BasePath, "Profile.json");

    private SaveIOService() : this(null) { }

    // Editor regression checks can use an isolated folder without touching player saves.
    private SaveIOService(string isolatedBasePath)
    {
        basePathOverride = isolatedBasePath;
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
        EnsureChapterLayout();
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
        WriteJsonAtomically(path,data);
        Debug.Log($"[SaveIO] {Mathf.Max(1, runNumber)}회차 데이터 저장 성공: {path}");
    }

    // The comparison test reads existing saves without creating a profile or advancing a run.
    public LocalStoryRevisionRequest LoadLatestLocalStoryRevision()
    {
        var dialogue = LoadLatestNarrativeRequest();
        if (dialogue != null) return dialogue;
        string runs = Path.Combine(BasePath, "Runs");
        if (!Directory.Exists(runs)) return null;
        string[] directories = Directory.GetDirectories(runs, "Run_*");
        System.Array.Sort(directories, (left, right) => RunDirectoryNumber(left).CompareTo(RunDirectoryNumber(right)));
        for (int i = directories.Length - 1; i >= 0; i--)
        {
            string path = Path.Combine(directories[i], "LocalStoryRevisionRequest.json");
            if (!File.Exists(path)) continue;
            try
            {
                var request = ReadJson<LocalStoryRevisionRequest>(path);
                if (request != null && request.sourceRun >= 1 && request.targetRun == request.sourceRun + 1)
                    return request;
            }
            catch (System.Exception e) { Debug.LogWarning("[LocalStory] 기록 읽기 실패: " + e.Message); }
        }
        return null;
    }
    private static int RunDirectoryNumber(string path)
    {
        string name = Path.GetFileName(path);
        return name.Length > 4 && int.TryParse(name.Substring(4), out int number) ? number : 0;
    }

    public string SaveLocalStoryTest(LocalStoryTestReport report)
    {
        try
        {
            var document = Newtonsoft.Json.Linq.JObject.Parse(JsonUtility.ToJson(report));
            string path = LocalStoryTestArchive.Save(Path.Combine(BasePath, "LocalStoryTests"), document);
            report.archiveId = (string)document["archiveId"];
            report.archiveNumber = (int)document["archiveNumber"];
            return path;
        }
        catch (System.Exception e) { Debug.LogWarning("[LocalStory] 테스트 기록 저장 실패: " + e.Message); return null; }
    }

    public void SaveRunCheckpoint<T>(int runNumber, string fileName, T data)
    {
        WriteJsonAtomically(GetRunDataPath(runNumber, fileName), data);
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
        TextAsset textAsset = ChapterContentCatalog.Asset(resourcePath);
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
        if (IsNarrativeKey(runNumber, contentType, relativePath))
            return SaveNarrativeContent(runNumber, relativePath, data, false);
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

    // User-triggered test promotion: retain a recoverable prior generated story before replacement.
    public bool SaveGeneratedContentForTest<T>(int runNumber,string contentType,string relativePath,T data)
    {
        if (IsNarrativeKey(runNumber, contentType, relativePath))
            return SaveNarrativeContent(runNumber, relativePath, data, true);
        if(!TryGetGeneratedContentPath(runNumber,contentType,relativePath,out string path)) return false;
        try
        {
            if(File.Exists(path))
            {
                string recovery=Path.Combine(BasePath,"GeneratedContent","Recovery");
                Directory.CreateDirectory(recovery);
                File.Copy(path,Path.Combine(recovery,"Run_"+runNumber.ToString("D4")+"_"+System.Guid.NewGuid().ToString("N")+".json"));
            }
            return SaveGeneratedContent(runNumber,contentType,relativePath,data);
        }
        catch(System.Exception e) { Debug.LogWarning("[LocalStory] 적용 저장 실패: "+e.Message); return false; }
    }

    public bool SaveGeneratedContentForReview<T>(int runNumber,string contentType,string relativePath,T data,string reason,bool manual=false)
    {
        return IsNarrativeKey(runNumber,contentType,relativePath) &&
            SaveNarrativeContent(runNumber,relativePath,data,manual,true,reason??"생성 검사 실패 문구 확인 필요");
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
        // Encounter work and its status are owned by the chapter document now.
        if (NarrativeInteractions.StorySource(scenarioPath) != null) return;

        string manifestPath = Path.Combine(
            BasePath,
            "GeneratedContent",
            $"For_Run_{targetRun:D2}",
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
        if (IsNarrativeKey(runNumber, contentType, relativePath))
            return TryLoadNarrativeContent(runNumber, relativePath, out data);
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
            $"For_Run_{runNumber:D2}",
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
        File.WriteAllText(temporaryPath, data is GameProgress progress ? ProgressDocument.Encode(path,progress) : JsonUtility.ToJson(data, true));

        if (File.Exists(path))
        {
            // Windows 백신/인덱서가 짧게 파일을 잠근 경우에도 체크포인트를 잃지 않게 재시도합니다.
            for (int attempt = 0; ; attempt++)
            {
                try { File.Replace(temporaryPath, path, null, true); break; }
                catch (IOException) when (attempt < 4)
                { System.Threading.Thread.Sleep(10 << attempt); }
            }
        }
        else
            File.Move(temporaryPath, path);
    }

    private string GetRunDirectory(int runNumber)
    {
        int safeRunNumber = Mathf.Max(1, runNumber);
        return EnsureRunDirectory(safeRunNumber);
    }

    private string GetRunDataPath(int runNumber, string fileName)
    {
        return Path.Combine(GetRunDirectory(runNumber), $"{fileName}.json");
    }

    private static T ReadJson<T>(string path)
    {
        if(typeof(T)==typeof(GameProgress)) return (T)(object)ProgressDocument.Read(path);
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
