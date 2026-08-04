using UnityEngine;

[System.Serializable]
public class TerrainDataRoot
{
    public TerrainData TerrainInfo;
}

[System.Serializable]
public class TerrainData
{
    public string terrainName;
    public PlaceData[] places;
}

[System.Serializable]
public class PlaceData
{
    public int chunkIndex;
    public string prefadId;
    public string destination;
    public Vector3 position;
    public Vector3 rotation;
}