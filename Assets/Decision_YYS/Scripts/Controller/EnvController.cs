using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 챕터별 지형을 연속된 월드에 등록하고 플레이어 위치를 스트리밍에 전달합니다.
/// </summary>
public class EnvController : MonoBehaviour
{
    private sealed class RegisteredTerrain
    {
        public TerrainData Data;
        public TerrainPlaceRegistry Registry;
    }

    [FormerlySerializedAs("terrain")]
    [SerializeField] private GameObject environment;
    [SerializeField] private GameObject groundPrefab;
    [FormerlySerializedAs("prefab")]
    [SerializeField] private GameObject housePrefab;
    [SerializeField, Min(0.01f)] private float chunkSize = 100f;
    [SerializeField, Min(0)] private int loadRadius = 1;

    public TerrainData TerrainData { get; private set; }
    public TerrainPlaceRegistry PlaceRegistry { get; private set; }
    public float ChunkSize => chunkSize;
    public Vector3 TerrainOrigin => environment != null ? environment.transform.position : Vector3.zero;

    private readonly TerrainRepository terrainRepository = new TerrainRepository();
    private readonly TerrainBuilder terrainBuilder = new TerrainBuilder();
    private readonly Dictionary<string, RegisteredTerrain> registeredTerrains =
        new Dictionary<string, RegisteredTerrain>();

    private TerrainStreamingController terrainStreamingController;
    private Transform playerTransform;
    private int nextGlobalChunkIndex;

    private void Awake()
    {
        if (environment == null)
        {
            Debug.LogError("EnvController의 environment가 지정되지 않았습니다.", this);
            return;
        }

        if (groundPrefab == null)
            Debug.LogWarning("groundPrefab이 없어 청크의 바닥은 생성하지 않습니다.", this);

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
            return true;
        }

        TerrainData terrainData = terrainRepository.Load(terrainPath);
        int chunkCount = GetChunkCount(terrainData);
        if (terrainData == null || chunkCount == 0)
        {
            Debug.LogError($"지형 데이터를 불러올 수 없습니다: {terrainPath}", this);
            return false;
        }

        // 새 챕터의 로컬 청크 0은 이전 지형의 마지막 전역 청크 바로 뒤에서 시작합니다.
        Vector3 segmentOrigin = TerrainOrigin +
            Vector3.forward * (nextGlobalChunkIndex * chunkSize);
        TerrainPlaceRegistry registry = new TerrainPlaceRegistry(
            terrainData, segmentOrigin, chunkSize);

        terrainBuilder.RegisterTerrain(terrainData, registry, groundPrefab,
            housePrefab, environment.transform, nextGlobalChunkIndex, chunkCount);

        registeredTerrains.Add(terrainPath, new RegisteredTerrain
        {
            Data = terrainData,
            Registry = registry
        });

        nextGlobalChunkIndex += chunkCount;
        TerrainData = terrainData;
        PlaceRegistry = registry;
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

        int separatorIndex = episodeTitle.LastIndexOf('_');
        string episodeNumber = separatorIndex >= 0
            ? episodeTitle.Substring(separatorIndex + 1)
            : "01";

        return $"{chapterName}/Terrain/{chapterName}_Terrain_{episodeNumber}";
    }

    private static int GetChunkCount(TerrainData terrainData)
    {
        if (terrainData?.places == null || terrainData.places.Length == 0) return 0;

        int maximumChunkIndex = -1;
        foreach (PlaceData place in terrainData.places)
        {
            if (place != null && place.chunkIndex > maximumChunkIndex)
                maximumChunkIndex = place.chunkIndex;
        }

        return maximumChunkIndex + 1;
    }
}
