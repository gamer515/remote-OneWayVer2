using UnityEngine;

/// <summary>
/// 환경 데이터 로딩과 지형 생성을 연결하는 Unity 진입점입니다.
/// </summary>
public class EnvController : MonoBehaviour
{
    private const string InitialTerrainPath = "Initial_Terrain/TerrainInfo_Initial";

    [SerializeField] private GameObject terrain;
    [SerializeField] private GameObject prefab;
    [SerializeField] private float chunkSize = 100f;

    public TerrainData TerrainData { get; private set; }
    public TerrainPlaceRegistry PlaceRegistry { get; private set; }
    public float ChunkSize => chunkSize;
    public Vector3 TerrainOrigin => terrain != null ? terrain.transform.position : Vector3.zero;

    private readonly TerrainRepository terrainRepository = new TerrainRepository();
    private readonly TerrainBuilder terrainBuilder = new TerrainBuilder();

    private void Awake()
    {
        TerrainData = terrainRepository.Load(InitialTerrainPath);
        PlaceRegistry = new TerrainPlaceRegistry(TerrainData, TerrainOrigin, chunkSize);

        if (TerrainData == null)
        {
            Debug.LogError($"지형 데이터를 불러올 수 없습니다: {InitialTerrainPath}");
            return;
        }

        terrainBuilder.Build(
            TerrainData,
            PlaceRegistry,
            prefab,
            terrain != null ? terrain.transform : null);
    }
}
