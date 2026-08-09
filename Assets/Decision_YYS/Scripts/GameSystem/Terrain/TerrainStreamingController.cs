using UnityEngine;

/// <summary>
/// 플레이어가 속한 청크를 기준으로 주변 청크의 로드 범위를 관리합니다.
/// </summary>
public sealed class TerrainStreamingController
{
    private readonly TerrainBuilder terrainBuilder;
    private readonly float chunkSize;
    private readonly float terrainOriginZ;
    private readonly int loadRadius;
    private int currentChunkIndex = int.MinValue;

    public TerrainStreamingController(TerrainBuilder terrainBuilder, float chunkSize,
        float terrainOriginZ, int loadRadius)
    {
        this.terrainBuilder = terrainBuilder;
        this.chunkSize = Mathf.Max(0.01f, chunkSize);
        this.terrainOriginZ = terrainOriginZ;
        this.loadRadius = Mathf.Max(0, loadRadius);
    }

    public void UpdatePlayerPosition(float playerWorldZ)
    {
        // 월드 좌표가 아니라 지형 시작점을 기준으로 청크 번호를 계산합니다.
        float localZ = playerWorldZ - terrainOriginZ;
        int newChunkIndex = Mathf.Max(0, Mathf.FloorToInt(localZ / chunkSize));
        if (currentChunkIndex == newChunkIndex) return;

        currentChunkIndex = newChunkIndex;
        // 만약 처음 뒤에 있는 풍경은 어떻게 하지?
        int minimumChunkIndex = Mathf.Max(0, currentChunkIndex - loadRadius);
        int maximumChunkIndex = currentChunkIndex + loadRadius;

        for (int chunkIndex = minimumChunkIndex; chunkIndex <= maximumChunkIndex; chunkIndex++)
            terrainBuilder.CreateOrLoadChunk(chunkIndex);

        // 범위 기준 회수라서 플레이어가 앞이나 뒤로 이동해도 같은 방식으로 동작합니다.
        terrainBuilder.UnloadOutsideRange(minimumChunkIndex, maximumChunkIndex);
    }
}
