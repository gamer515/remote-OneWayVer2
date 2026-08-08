using UnityEngine;

public class TerrainStreamingController
{
    private readonly TerrainBuilder terrainBuilder;

    private float chunkSize;
    private int currentChunkIndex = -1;

    public TerrainStreamingController(TerrainBuilder terrainBuilder, float chunkSize)
    {
        this.terrainBuilder = terrainBuilder;
        this.chunkSize = chunkSize;

        //여기서 청크 0을 만든다.
    }

    public void UpdatePlayerPosition(float playerZ)
    {
        int newChunkIndex = Mathf.FloorToInt(playerZ / chunkSize);

        if (currentChunkIndex == newChunkIndex) 
        {
            return;
        }

        currentChunkIndex = newChunkIndex;

        terrainBuilder.CreateOrLoadChunk(currentChunkIndex);
        terrainBuilder.CreateOrLoadChunk(currentChunkIndex+1);
        terrainBuilder.UnloadChunk(currentChunkIndex-1);
    }
}
