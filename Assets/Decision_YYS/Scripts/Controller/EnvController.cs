using System.Collections.Generic;
using UnityEngine;

//로딩에 필요한 데이터 구분 고려
public class EnvController : MonoBehaviour
{
    // terrain 저장소

    [SerializeField] private GameObject terrain;
    [SerializeField] private GameObject prefab;
    private float chunkSize = 100f;

    private TerrainDataRoot currentTerrainData;

    private void Awake()
    {
        // 초기화
        if (terrain != null)
        {
            LoadTerrain("Initial_Terrain/TerrainInfo_Initial");
        }

        //Debug.Log("<color=green>Current Terrain Data1: </color>" + currentTerrainData.TerrainInfo.terrainName);
        //Debug.Log("<color=green>Current Terrain Data2: </color>" + currentTerrainData.TerrainInfo.places.Length);
    }

    private void LoadTerrain(string terrainFilePath)
    {
        currentTerrainData = SaveIOService.Instance.LoadData<TerrainDataRoot>(terrainFilePath);

        if (currentTerrainData != null && terrain != null)
        {
            // 새로운 terrain 생성
            foreach (var place in currentTerrainData.TerrainInfo.places)
            {
                //ScriptableObject로 건물을 구분을 할려고 함.
                //GameObject prefab = Resources.Load<GameObject>(place.prefadId);
                if (prefab != null && !string.IsNullOrEmpty(place.destination))
                {
                    Vector3 origin = terrain.transform.position;
                    Vector3 eachPosition = new Vector3(place.position.x, place.position.y, place.position.z + chunkSize * place.chunkIndex);
                    Vector3 position = origin + eachPosition;

                    GameObject newPlace = Instantiate(prefab, position, Quaternion.Euler(place.rotation), terrain.transform);
                    newPlace.name = $"Place_{place.chunkIndex}";
                }
                else
                {
                    Debug.LogWarning($"Prefab not found or destination is empty for ID");
                }
            }
        }
        else
        {
            Debug.LogWarning("Failed to load terrain data or terrain GameObject is not assigned.");
        }
    }

    public TerrainData getTerrainData()
    {
        return currentTerrainData.TerrainInfo;
    }

    public float getChunkSize()
    {
        return chunkSize;
    }

    public List<string> FindPlace()
    {
        TerrainData terrainData = currentTerrainData.TerrainInfo;
        if(terrainData == null || terrainData.places == null)
        {
            Debug.LogWarning("Terrain data or places are null.");
            return new List<string>();
        }

        List<string> placeData = new List<string>();

        foreach (var place in terrainData.places)
        {
            if (!string.IsNullOrEmpty(place.destination))
            {
                placeData.Add(place.destination);
            }
            else
            {
                placeData.Add("noPlace");
            }
        }

        Debug.Log("<color=blue>Place Data: </color>" + string.Join(", ", placeData));

        return placeData;
    }
}
