/// <summary>
/// 저장소 경로로부터 지형 정의 데이터만 불러옵니다.
/// </summary>
public sealed class TerrainRepository
{
    public TerrainData Load(string terrainFilePath)
    {
        TerrainDataRoot root = SaveIOService.Instance.LoadData<TerrainDataRoot>(terrainFilePath);
        return root?.TerrainInfo;
    }
}
