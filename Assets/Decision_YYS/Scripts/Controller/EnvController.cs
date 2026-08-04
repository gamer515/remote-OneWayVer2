using UnityEngine;

public class EnvController : MonoBehaviour
{
    // terrain 저장소

    [SerializeField] private GameObject terrain;
    [SerializeField] private GameObject prefab;
    private float chunkSize = 100f;

    private TerrainDataRoot currentTerrainData;

    private void Start()
    {
        // 초기화
        if (terrain != null)
        {
            LoadTerrain("Initial_Terrain/TerrainInfo_Initial");
        }

        //Debug.Log("<color=green>Current Terrain Data1: </color>" + currentTerrainData.TerrainInfo.terrainName);
        //Debug.Log("<color=green>Current Terrain Data2: </color>" + currentTerrainData.TerrainInfo.places.Length);
    }

    public void LoadTerrain(string terrainFilePath)
    {
        currentTerrainData = SaveIOService.Instance.LoadData<TerrainDataRoot>(terrainFilePath);

        if (currentTerrainData != null && terrain != null)
        {
            // 새로운 terrain 생성
            foreach (var place in currentTerrainData.TerrainInfo.places)
            {
                //ScriptableObject로 건물을 구분을 할려고 함.
                //GameObject prefab = Resources.Load<GameObject>(place.prefadId);
                if (prefab != null)
                {
                    Vector3 origin = terrain.transform.position;
                    Vector3 eachPosition = new Vector3(place.position.x, place.position.y, place.position.z + chunkSize * place.chunkIndex);
                    Vector3 position = origin + eachPosition;

                    GameObject newPlace = Instantiate(prefab, position, Quaternion.Euler(place.rotation), terrain.transform);
                    newPlace.name = $"Place_{place.chunkIndex}";
                }
                else
                {
                    Debug.LogWarning($"Prefab not found for ID: {place.prefadId}");
                }
            }
        }
        else
        {
            Debug.LogWarning("Failed to load terrain data or terrain GameObject is not assigned.");
        }
    }
}
