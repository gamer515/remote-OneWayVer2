using System.Net.NetworkInformation;
using UnityEngine;

/// <summary>
/// 환경 데이터 로딩과 지형 생성을 연결하는 Unity 진입점입니다.
/// </summary>
public class EnvController : MonoBehaviour
{
    // 각 챕터의 지형 정의 데이터를 불러올 경로를 지정해야 함.
    private const string InitialTerrainPath = "Initial/Terrain/Initial_Terrain_01";

    [SerializeField] private GameObject environment;
    [SerializeField] private GameObject groundPrefab;
    [SerializeField] private GameObject housePrefab;
    [SerializeField] private float chunkSize = 100f;

    public TerrainData TerrainData { get; private set; }
    public TerrainPlaceRegistry PlaceRegistry { get; private set; }
    public float ChunkSize => chunkSize;
    public Vector3 TerrainOrigin => environment != null ? environment.transform.position : Vector3.zero;

    private readonly TerrainRepository terrainRepository = new TerrainRepository();
    private readonly TerrainBuilder terrainBuilder = new TerrainBuilder();
    private TerrainStreamingController terrainStreamingController;

    private Transform playaerTransform;

    private void Awake()
    {
        TerrainData = terrainRepository.Load(InitialTerrainPath);
        PlaceRegistry = new TerrainPlaceRegistry(TerrainData, TerrainOrigin, chunkSize);
        terrainStreamingController = new TerrainStreamingController(terrainBuilder, chunkSize);

        if (TerrainData == null)
        {
            Debug.LogError($"지형 데이터를 불러올 수 없습니다: {InitialTerrainPath}");
            return;
        }

        terrainBuilder.Build(
            TerrainData,
            PlaceRegistry,
            housePrefab,
            environment != null ? environment.transform : null);
    }

    private void Update()
    {
        if(playaerTransform == null || terrainStreamingController == null)
        {
            return;
        }

        terrainStreamingController.UpdatePlayerPosition(playaerTransform.position.z);
    }

    public void BindPlayer(Player player)
    {
        if(player == null)
        {
            Debug.LogError("Player 객체가 null입니다.");
            return;
        }

        playaerTransform = player.transform;
    }
}
