using UnityEngine;

[System.Serializable]
public class TerrainData
{
    public string terrainName;
    public int chunkCount;
    public PlaceData[] places;
}

[System.Serializable]
public class PlaceData
{
    public string placeId;
    public int chunkIndex;
    public string prefabId;
    public string destination;
    public string characterId;
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
    public int chunkCount;
    public TerrainPlaceDefinition[] places;
}

[System.Serializable]
public class TerrainPlaceDefinition
{
    public string placeId;
    public int chunkIndex;
    public string prefabId;
    public string destination;
    public string characterId;
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
    public string side;
    public float y;
    public int z;
    public Vector3 offset;
    public Vector3 rotation;
}
