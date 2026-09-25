using UnityEngine;

[System.Serializable]
public class TerrainData
{
    public string terrainName;
    public string groundId;
    // 첫 항목은 고정이며, 두 번째 이후 항목만 회차 Seed로 순서를 섞습니다.
    public string[] groundIds;
    public int chunkCount;
    public PlaceData[] places;
}

[System.Serializable]
public class PlaceData
{
    public string placeId;
    public int chunkIndex;
    public string prefabId;
    public string characterId;
    // 오브젝트 전용 카드 폴더의 Resources 경로입니다. 비어 있으면 장식입니다.
    public string connectStoryCards;
    public Vector3 position;
    public Vector3 rotation;
}

[System.Serializable]
public class TerrainDefinitionRoot
{
    public TerrainDefinition TerrainInfo;
}

[System.Serializable]
public class TerrainDefinition
{
    public string terrainName;
    public string groundId;
    public string[] groundIds;
    public int chunkCount;
    public TerrainPlaceDefinition[] places;
}

[System.Serializable]
public class TerrainPlaceDefinition
{
    public string placeId;
    public int chunkIndex;
    public string prefabId;
    public string characterId;
    public string connectStoryCards;
}

[System.Serializable]
public class TerrainTransformRoot
{
    public TerrainTransformData TerrainTransforms;
}

[System.Serializable]
public class TerrainTransformData
{
    public TerrainPlaceTransform[] places;
}

[System.Serializable]
public class TerrainPlaceTransform
{
    public string placeId;
    // 새 형식: Terrain 프리팹에서 확인한 정확한 로컬 좌표를 그대로 사용합니다.
    public bool useExactPosition;
    public Vector3 position;
    // 아래 필드는 기존 JSON 호환용 슬롯 형식입니다.
    public string side;
    public float y;
    public int z;
    public Vector3 offset;
    public Vector3 rotation;
}
