using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 챕터별 지형을 연속된 월드에 등록하고 플레이어 위치를 스트리밍에 전달합니다.
/// </summary>
public class EnvController : MonoBehaviour
{
    [System.Serializable]
    private sealed class TerrainGroundEntry
    {
        public string groundId;
        public GameObject prefab;
    }

    private sealed class RegisteredTerrain
    {
        public TerrainData Data;
        public TerrainPlaceRegistry Registry;
        public float EndZ;
    }

    [FormerlySerializedAs("terrain")]
    [SerializeField] private GameObject environment;
    [Tooltip("이전 Scene 호환용 기본 바닥입니다. groundId가 비어 있거나 카탈로그에 없을 때 사용합니다.")]
    [SerializeField] private GameObject groundPrefab;
    [SerializeField] private TerrainGroundEntry[] groundEntries;
    [SerializeField] private TerrainPrefabCatalog terrainPrefabCatalog;
    [SerializeField, Min(0.01f)] private float chunkSize = 50f;
    [SerializeField, Min(0)] private int loadRadius = 1;

    public TerrainData TerrainData { get; private set; }
    public TerrainPlaceRegistry PlaceRegistry { get; private set; }
    public float ChunkSize => chunkSize;
    public float CurrentTerrainEndZ { get; private set; }
    public Vector3 TerrainOrigin => environment != null ? environment.transform.position : Vector3.zero;

    private TerrainRepository terrainRepository = new TerrainRepository();
    private readonly TerrainBuilder terrainBuilder = new TerrainBuilder();
    private readonly Dictionary<string, RegisteredTerrain> registeredTerrains =
        new Dictionary<string, RegisteredTerrain>();

    private TerrainStreamingController terrainStreamingController;
    private Transform playerTransform;
    private int nextGlobalChunkIndex;
    private int contentRun = 1;
    private int contentSeed;

    private void Awake()
    {
        if (environment == null)
        {
            Debug.LogError("EnvController의 environment가 지정되지 않았습니다.", this);
            return;
        }

        if (groundPrefab == null)
            Debug.LogWarning("groundPrefab이 없어 청크의 바닥은 생성하지 않습니다.", this);

        if (terrainPrefabCatalog == null)
        {
            Debug.LogError("지형 프리팹 카탈로그가 지정되지 않았습니다.", this);
        }
        else if (!terrainPrefabCatalog.TryValidate(out string catalogError))
        {
            Debug.LogError(catalogError, this);
        }

        terrainStreamingController = new TerrainStreamingController(
            terrainBuilder, chunkSize, TerrainOrigin.z, loadRadius);
    }

    public bool RegisterTerrain(string chapterName, string episodeTitle)
    {
        string terrainPath = CreateTerrainPath(chapterName, episodeTitle);
        if (string.IsNullOrEmpty(terrainPath)) return false;

        // 저장 복원이나 중복 호출 시 이미 등록된 지형을 다시 이어 붙이지 않습니다.
        if (registeredTerrains.TryGetValue(terrainPath, out RegisteredTerrain registered))
        {
            TerrainData = registered.Data;
            PlaceRegistry = registered.Registry;
            CurrentTerrainEndZ = registered.EndZ;
            return true;
        }

        TerrainData terrainData = terrainRepository.Load(terrainPath);
        int chunkCount = GetChunkCount(terrainData);
        if (terrainData == null || chunkCount == 0)
        {
            Debug.LogError($"지형 데이터를 불러올 수 없습니다: {terrainPath}", this);
            return false;
        }

        if (!TryValidateTerrainReferences(terrainData, out string referenceError))
        {
            Debug.LogError($"지형 오브젝트 참조가 올바르지 않습니다: {terrainPath}\n{referenceError}", this);
            return false;
        }

        // 새 챕터의 로컬 청크 0은 이전 지형의 마지막 전역 청크 바로 뒤에서 시작합니다.
        Vector3 segmentOrigin = TerrainOrigin +
            Vector3.forward * (nextGlobalChunkIndex * chunkSize);
        TerrainPlaceRegistry registry = new TerrainPlaceRegistry(
            terrainData, segmentOrigin, chunkSize);

        if (!TryResolveGroundPrefabs(terrainData, out GameObject[] selectedGroundPrefabs,
                out string groundError))
        {
            Debug.LogError($"{groundError}: {terrainPath}", this);
            return false;
        }

        terrainBuilder.RegisterTerrain(terrainData, registry, selectedGroundPrefabs,
            terrainPrefabCatalog, environment.transform, nextGlobalChunkIndex, chunkCount,
            contentSeed);

        float segmentEndZ = segmentOrigin.z + chunkCount * chunkSize;
        registeredTerrains.Add(terrainPath, new RegisteredTerrain
        {
            Data = terrainData,
            Registry = registry,
            EndZ = segmentEndZ
        });

        nextGlobalChunkIndex += chunkCount;
        TerrainData = terrainData;
        PlaceRegistry = registry;
        CurrentTerrainEndZ = segmentEndZ;
        if (playerTransform != null)
            terrainStreamingController?.RefreshPlayerPosition(playerTransform.position.z);
        return true;
    }

    /// <summary>
    /// 지형을 등록하기 전에 현재 회차를 지정하여 원본/생성 지형의 출처를 고정합니다.
    /// </summary>
    public void SetContentRun(int runNumber, int runSeed)
    {
        int safeRunNumber = Mathf.Max(1, runNumber);
        if (registeredTerrains.Count > 0 && safeRunNumber != contentRun)
        {
            Debug.LogError("이미 지형을 등록한 뒤에는 콘텐츠 회차를 변경할 수 없습니다.", this);
            return;
        }

        contentRun = safeRunNumber;
        contentSeed = runSeed;
        terrainRepository = new TerrainRepository(contentRun, contentSeed);
    }

    public bool TryValidateAllContent(OmnibusData omnibus, out string errorMessage)
    {
        if (omnibus?.chapters == null)
        {
            errorMessage = "Omnibus의 chapters가 없습니다.";
            return false;
        }

        foreach (ChapterInfo chapter in omnibus.chapters)
        {
            if (chapter == null || string.IsNullOrWhiteSpace(chapter.chapterId) ||
                chapter.episodeIds == null)
            {
                errorMessage = "Omnibus에 비어 있는 chapterId 또는 episodeIds가 있습니다.";
                return false;
            }

            foreach (string episodeId in chapter.episodeIds)
            {
                string terrainPath = CreateTerrainPath(chapter.chapterId, episodeId);
                TerrainData terrainData = terrainRepository.Load(terrainPath);
                if (terrainData == null)
                {
                    errorMessage = $"지형을 불러올 수 없습니다: {terrainPath}";
                    return false;
                }

                if (!TryResolveGroundPrefabs(terrainData, out _, out string groundError))
                {
                    errorMessage = $"{groundError}: {terrainPath}";
                    return false;
                }

                if (!TryValidateTerrainReferences(terrainData, out string referenceError))
                {
                    errorMessage = $"{terrainPath}: {referenceError}";
                    return false;
                }
            }
        }

        errorMessage = null;
        return true;
    }

    private void Update()
    {
        if (playerTransform == null || terrainStreamingController == null) return;
        terrainStreamingController.UpdatePlayerPosition(playerTransform.position.z);
    }

    public void BindPlayer(Player player)
    {
        if (player == null)
        {
            Debug.LogError("EnvController에 연결할 Player가 없습니다.", this);
            return;
        }

        playerTransform = player.transform;
        terrainStreamingController?.UpdatePlayerPosition(playerTransform.position.z);
    }

    private static string CreateTerrainPath(string chapterName, string episodeTitle)
    {
        if (string.IsNullOrWhiteSpace(chapterName) || string.IsNullOrWhiteSpace(episodeTitle))
            return null;

        return $"{chapterName}/{episodeTitle}/Terrain";
    }

    private static int GetChunkCount(TerrainData terrainData)
    {
        if (terrainData == null) return 0;

        // 건물이 없는 마지막 청크도 유지할 수 있도록 JSON의 명시적인 청크 수를 우선 사용합니다.
        if (terrainData.chunkCount > 0) return terrainData.chunkCount;

        // 이전 형식의 Terrain JSON을 위한 호환 처리입니다.
        if (terrainData.places == null || terrainData.places.Length == 0) return 0;

        int maximumChunkIndex = -1;
        foreach (PlaceData place in terrainData.places)
        {
            if (place != null && place.chunkIndex > maximumChunkIndex)
                maximumChunkIndex = place.chunkIndex;
        }

        return maximumChunkIndex + 1;
    }

    private bool TryValidateTerrainReferences(TerrainData terrainData, out string errorMessage)
    {
        if (terrainPrefabCatalog == null)
        {
            errorMessage = "TerrainPrefabCatalog가 없습니다.";
            return false;
        }

        EncounterContentRepository contentRepository = new EncounterContentRepository();
        foreach (PlaceData place in terrainData.places)
        {
            if (place == null)
            {
                errorMessage = "places 배열에 null 항목이 있습니다.";
                return false;
            }

            if (!terrainPrefabCatalog.TryGetPrefab(place.prefabId, out _))
            {
                errorMessage = $"placeId '{place.placeId}'의 prefabId '{place.prefabId}'가 " +
                    "Inspector 카탈로그에 없습니다.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(place.connectStoryCards))
            {
                errorMessage = $"placeId '{place.placeId}'의 connectStoryCards가 비어 있습니다.";
                return false;
            }

            string normalizedPath = place.connectStoryCards.Replace('\\', '/').TrimEnd('/');
            int separatorIndex = normalizedPath.LastIndexOf('/');
            string folderName = separatorIndex >= 0
                ? normalizedPath.Substring(separatorIndex + 1)
                : normalizedPath;
            if (!string.Equals(folderName, place.placeId, System.StringComparison.Ordinal))
            {
                errorMessage = $"placeId '{place.placeId}'와 카드 폴더 '{folderName}'가 다릅니다.";
                return false;
            }

            if (!contentRepository.TryLoadInteraction(
                    place.connectStoryCards, out _, out string interactionError))
            {
                errorMessage = $"placeId '{place.placeId}': {interactionError}";
                return false;
            }

            if (!contentRepository.TryLoadCards(
                    place.connectStoryCards, out _, out string cardError))
            {
                errorMessage = $"placeId '{place.placeId}': {cardError}";
                return false;
            }
        }

        errorMessage = null;
        return true;
    }

    private GameObject ResolveGroundPrefab(string groundId)
    {
        if (!string.IsNullOrWhiteSpace(groundId) && groundEntries != null)
        {
            foreach (TerrainGroundEntry entry in groundEntries)
            {
                if (entry != null && entry.prefab != null &&
                    string.Equals(entry.groundId, groundId, System.StringComparison.Ordinal))
                    return entry.prefab;
            }

            Debug.LogWarning(
                $"groundId '{groundId}'가 Ground Entries에 없어 기본 Ground Prefab을 사용합니다.",
                this);
        }

        return groundPrefab;
    }

    private bool TryResolveGroundPrefabExact(string groundId, out GameObject prefab)
    {
        prefab = null;
        if (string.IsNullOrWhiteSpace(groundId) || groundEntries == null) return false;
        foreach (TerrainGroundEntry entry in groundEntries)
        {
            if (entry != null && entry.prefab != null &&
                string.Equals(entry.groundId, groundId, System.StringComparison.Ordinal))
            {
                prefab = entry.prefab;
                return true;
            }
        }
        return false;
    }

    private bool TryResolveGroundPrefabs(
        TerrainData terrainData,
        out GameObject[] prefabs,
        out string errorMessage)
    {
        prefabs = null;
        string[] groundIds = terrainData?.groundIds;
        if (groundIds == null || groundIds.Length == 0)
            groundIds = new[] { terrainData?.groundId };

        if (groundIds.Length != GetChunkCount(terrainData))
        {
            errorMessage = "groundIds 개수와 chunkCount가 다릅니다";
            return false;
        }

        prefabs = new GameObject[groundIds.Length];
        for (int index = 0; index < groundIds.Length; index++)
        {
            if (!TryResolveGroundPrefabExact(groundIds[index], out prefabs[index]))
            {
                errorMessage = $"groundId '{groundIds[index]}'가 Ground Entries에 없습니다";
                prefabs = null;
                return false;
            }
        }

        errorMessage = null;
        return true;
    }
}

[System.Serializable]
public sealed class TerrainPrefabEntry
{
    public string prefabId;
    public GameObject prefab;
}

/// <summary>
/// JSON의 논리적인 prefabId와 Unity 프리팹 참조를 연결합니다.
/// 에셋 경로를 JSON에 저장하지 않으므로 프리팹을 이동해도 연결이 유지됩니다.
/// </summary>
[System.Serializable]
public sealed class TerrainPrefabCatalog
{
    [Header("Buildings")]
    [FormerlySerializedAs("entries")]
    [SerializeField] private TerrainPrefabEntry[] buildingEntries;

    [Header("Characters")]
    [SerializeField] private TerrainPrefabEntry[] characterEntries;

    private Dictionary<string, GameObject> prefabsById;

    public bool TryGetPrefab(string prefabId, out GameObject prefab)
    {
        BuildLookupIfNeeded();
        prefab = null;
        return !string.IsNullOrWhiteSpace(prefabId) &&
               prefabsById.TryGetValue(prefabId, out prefab) && prefab != null;
    }

    public bool TryValidate(out string errorMessage)
    {
        prefabsById = null;
        BuildLookupIfNeeded();

        int entryCount = GetEntryCount(buildingEntries) + GetEntryCount(characterEntries);
        if (entryCount == 0)
        {
            errorMessage = "지형 프리팹 카탈로그가 비어 있습니다.";
            return false;
        }

        HashSet<string> ids = new HashSet<string>(System.StringComparer.Ordinal);
        if (!TryValidateEntries(buildingEntries, "건물", ids, out errorMessage))
            return false;
        if (!TryValidateEntries(characterEntries, "인물", ids, out errorMessage))
            return false;

        errorMessage = null;
        return true;
    }

    private void BuildLookupIfNeeded()
    {
        if (prefabsById != null) return;

        prefabsById = new Dictionary<string, GameObject>(System.StringComparer.Ordinal);
        AddEntriesToLookup(buildingEntries);
        AddEntriesToLookup(characterEntries);
    }

    private void AddEntriesToLookup(TerrainPrefabEntry[] entries)
    {
        if (entries == null) return;

        foreach (TerrainPrefabEntry entry in entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.prefabId)) continue;
            prefabsById[entry.prefabId] = entry.prefab;
        }
    }

    private static bool TryValidateEntries(
        TerrainPrefabEntry[] entries,
        string categoryName,
        HashSet<string> ids,
        out string errorMessage)
    {
        if (entries == null)
        {
            errorMessage = null;
            return true;
        }

        foreach (TerrainPrefabEntry entry in entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.prefabId) || entry.prefab == null)
            {
                errorMessage = $"{categoryName} 프리팹 목록에 ID 또는 프리팹이 비어 있습니다.";
                return false;
            }

            if (!ids.Add(entry.prefabId))
            {
                errorMessage = $"건물/인물 프리팹 ID가 중복되었습니다: {entry.prefabId}";
                return false;
            }
        }

        errorMessage = null;
        return true;
    }

    private static int GetEntryCount(TerrainPrefabEntry[] entries)
    {
        return entries?.Length ?? 0;
    }
}
