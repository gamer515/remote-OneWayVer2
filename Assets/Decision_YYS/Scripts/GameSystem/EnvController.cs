using UnityEngine;

public class EnvController : MonoBehaviour
{
    // terrain 저장소

    [SerializeField] private GameObject terrain;

    private TerrainDataRoot currentTerrainData;

    private void Start()
    {
        // 초기화
        if (terrain != null)
        {
            currentTerrainData = SaveIOService.Instance.LoadData<TerrainDataRoot>("Initial_Terrain/TerrainInfo_Initial");
        }

        //Debug.Log("<color=green>Current Terrain Data1: </color>" + currentTerrainData.TerrainInfo.terrainName);
        //Debug.Log("<color=green>Current Terrain Data2: </color>" + currentTerrainData.TerrainInfo.places.Length);
    }

    public void LoadTerrain(string terrainFilePath)
    {
        currentTerrainData = SaveIOService.Instance.LoadData<TerrainDataRoot>(terrainFilePath);
        if (currentTerrainData != null && terrain != null)
        {
            // 기존 terrain 제거
            foreach (Transform child in terrain.transform)
            {
                Destroy(child.gameObject);
            }
            // 새로운 terrain 생성
            foreach (var place in currentTerrainData.TerrainInfo.places)
            {
                GameObject prefab = Resources.Load<GameObject>(place.prefadId);
                if (prefab != null)
                {
                    GameObject newPlace = Instantiate(prefab, place.position, Quaternion.Euler(place.rotation), terrain.transform);
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
