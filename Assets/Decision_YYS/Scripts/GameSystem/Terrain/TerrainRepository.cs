using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 지형 정의와 오브젝트 Transform JSON을 placeId로 병합합니다.
/// </summary>
public sealed class TerrainRepository
{
    public TerrainData Load(string terrainFilePath)
    {
        TerrainDefinitionRoot definitionRoot =
            SaveIOService.Instance.LoadData<TerrainDefinitionRoot>(terrainFilePath);
        string transformPath = CreateTransformPath(terrainFilePath);
        TerrainTransformRoot transformRoot =
            SaveIOService.Instance.LoadData<TerrainTransformRoot>(transformPath);

        TerrainDefinition definition = definitionRoot?.TerrainInfo;
        TerrainPlaceTransform[] transforms = transformRoot?.TerrainTransforms?.places;
        if (definition?.places == null || transforms == null)
        {
            Debug.LogError($"지형 분리 파일을 불러오지 못했습니다: {terrainFilePath}, {transformPath}");
            return null;
        }

        Dictionary<string, TerrainPlaceTransform> transformsById =
            new Dictionary<string, TerrainPlaceTransform>(StringComparer.Ordinal);
        foreach (TerrainPlaceTransform transformData in transforms)
        {
            if (transformData == null || string.IsNullOrWhiteSpace(transformData.placeId) ||
                !transformsById.TryAdd(transformData.placeId, transformData))
            {
                Debug.LogError($"Transform JSON에 비어 있거나 중복된 placeId가 있습니다: {transformData?.placeId}");
                return null;
            }
        }

        PlaceData[] places = new PlaceData[definition.places.Length];
        HashSet<string> definitionIds = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < definition.places.Length; i++)
        {
            TerrainPlaceDefinition place = definition.places[i];
            if (place == null || string.IsNullOrWhiteSpace(place.placeId) ||
                !definitionIds.Add(place.placeId))
            {
                Debug.LogError($"지형 정의 JSON에 비어 있거나 중복된 placeId가 있습니다: {place?.placeId}");
                return null;
            }

            if (!transformsById.TryGetValue(place.placeId, out TerrainPlaceTransform transformData))
            {
                Debug.LogError($"placeId '{place.placeId}'의 Transform 정보가 없습니다.");
                return null;
            }

            if (definition.chunkCount > 0 &&
                (place.chunkIndex < 0 || place.chunkIndex >= definition.chunkCount))
            {
                Debug.LogError($"placeId '{place.placeId}'의 chunkIndex가 지형 범위를 벗어났습니다.");
                return null;
            }

            // 폭 24, 중앙 보행로 폭 5인 현재 지형에서는 건물 중심의 X를 ±8 안으로 제한합니다.
            if (Mathf.Abs(transformData.position.x) > 8f ||
                transformData.position.z < 0f || transformData.position.z > 100f)
            {
                Debug.LogError($"placeId '{place.placeId}'의 청크 내부 위치가 허용 범위를 벗어났습니다.");
                return null;
            }

            places[i] = new PlaceData
            {
                placeId = place.placeId,
                chunkIndex = place.chunkIndex,
                prefabId = place.prefabId,
                destination = place.destination,
                position = transformData.position,
                rotation = transformData.rotation
            };
        }

        if (definitionIds.Count != transformsById.Count)
        {
            Debug.LogError("지형 정의 없이 Transform JSON에만 존재하는 placeId가 있습니다.");
            return null;
        }

        return new TerrainData
        {
            terrainName = definition.terrainName,
            chunkCount = definition.chunkCount,
            places = places
        };
    }

    private static string CreateTransformPath(string terrainFilePath)
    {
        const string terrainToken = "_Terrain_";
        int tokenIndex = terrainFilePath.LastIndexOf(terrainToken, StringComparison.Ordinal);
        if (tokenIndex < 0)
            return terrainFilePath + "_Transform";

        return terrainFilePath.Substring(0, tokenIndex) +
               "_Transform_" +
               terrainFilePath.Substring(tokenIndex + terrainToken.Length);
    }
}
