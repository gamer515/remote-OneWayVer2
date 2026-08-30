using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 지형 정의와 오브젝트 Transform JSON을 placeId로 병합합니다.
/// </summary>
public sealed class TerrainRepository
{
    private const float LeftSlotX = -8f;
    private const float RightSlotX = 8f;
    private const int FirstSlotZ = 10;
    private const int SlotSpacingZ = 20;
    private const int SlotCountPerSide = 5;
    private readonly int runNumber;

    public TerrainRepository(int runNumber = 1)
    {
        this.runNumber = Math.Max(1, runNumber);
    }

    public TerrainData Load(string terrainFilePath)
    {
        string transformPath = CreateTransformPath(terrainFilePath);

        bool hasGeneratedDefinition = SaveIOService.Instance.TryLoadGeneratedContent(
            runNumber,
            "Episodes",
            terrainFilePath,
            out TerrainDefinitionRoot generatedDefinition);
        bool hasGeneratedTransform = SaveIOService.Instance.TryLoadGeneratedContent(
            runNumber,
            "Episodes",
            transformPath,
            out TerrainTransformRoot generatedTransform);

        if (hasGeneratedDefinition && hasGeneratedTransform)
        {
            if (TryBuildTerrain(generatedDefinition, generatedTransform, out TerrainData generated, out string error))
            {
                Debug.Log($"[TerrainRepository] {runNumber}회차 생성 지형을 사용합니다: {terrainFilePath}");
                return generated;
            }

            Debug.LogWarning(
                $"[TerrainRepository] 생성 지형 검증 실패로 원본을 사용합니다: {terrainFilePath} ({error})");
        }
        else if (hasGeneratedDefinition || hasGeneratedTransform)
        {
            Debug.LogWarning(
                $"[TerrainRepository] 생성 지형의 Terrain/Transform 한쪽이 없어 원본을 사용합니다: {terrainFilePath}");
        }

        TerrainDefinitionRoot definitionRoot =
            SaveIOService.Instance.LoadResourceData<TerrainDefinitionRoot>(terrainFilePath);
        TerrainTransformRoot transformRoot =
            SaveIOService.Instance.LoadResourceData<TerrainTransformRoot>(transformPath);

        if (TryBuildTerrain(definitionRoot, transformRoot, out TerrainData original, out string originalError))
            return original;

        Debug.LogError($"지형 분리 파일이 올바르지 않습니다: {terrainFilePath}, {transformPath} ({originalError})");
        return null;
    }

    private static bool TryBuildTerrain(
        TerrainDefinitionRoot definitionRoot,
        TerrainTransformRoot transformRoot,
        out TerrainData terrainData,
        out string errorMessage)
    {
        terrainData = null;

        TerrainDefinition definition = definitionRoot?.TerrainInfo;
        TerrainPlaceTransform[] transforms = transformRoot?.TerrainTransforms?.places;
        if (definition?.places == null || transforms == null)
        {
            errorMessage = "Terrain 또는 Transform의 places가 없습니다.";
            return false;
        }

        Dictionary<string, TerrainPlaceTransform> transformsById =
            new Dictionary<string, TerrainPlaceTransform>(StringComparer.Ordinal);
        foreach (TerrainPlaceTransform transformData in transforms)
        {
            if (transformData == null || string.IsNullOrWhiteSpace(transformData.placeId) ||
                !transformsById.TryAdd(transformData.placeId, transformData))
            {
                errorMessage = $"Transform JSON에 비어 있거나 중복된 placeId가 있습니다: {transformData?.placeId}";
                return false;
            }
        }

        PlaceData[] places = new PlaceData[definition.places.Length];
        HashSet<string> definitionIds = new HashSet<string>(StringComparer.Ordinal);
        HashSet<string> occupiedSlots = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < definition.places.Length; i++)
        {
            TerrainPlaceDefinition place = definition.places[i];
            if (place == null || string.IsNullOrWhiteSpace(place.placeId) ||
                !definitionIds.Add(place.placeId))
            {
                errorMessage = $"지형 정의 JSON에 비어 있거나 중복된 placeId가 있습니다: {place?.placeId}";
                return false;
            }

            if (!transformsById.TryGetValue(place.placeId, out TerrainPlaceTransform transformData))
            {
                errorMessage = $"placeId '{place.placeId}'의 Transform 정보가 없습니다.";
                return false;
            }

            if (definition.chunkCount > 0 &&
                (place.chunkIndex < 0 || place.chunkIndex >= definition.chunkCount))
            {
                errorMessage = $"placeId '{place.placeId}'의 chunkIndex가 지형 범위를 벗어났습니다.";
                return false;
            }

            if (!TryGetSlotPosition(transformData, out Vector3 slotPosition))
            {
                errorMessage = $"placeId '{place.placeId}'의 슬롯이 올바르지 않습니다. " +
                    "side는 left/right, z는 10/30/50/70/90 중 하나여야 합니다.";
                return false;
            }

            string slotKey = $"{place.chunkIndex}:{transformData.side}:{transformData.z}";
            if (!occupiedSlots.Add(slotKey))
            {
                errorMessage = $"같은 청크의 배치 슬롯이 중복되었습니다: {slotKey}";
                return false;
            }

            places[i] = new PlaceData
            {
                placeId = place.placeId,
                chunkIndex = place.chunkIndex,
                prefabId = place.prefabId,
                destination = place.destination,
                position = slotPosition,
                rotation = transformData.rotation
            };
        }

        if (definitionIds.Count != transformsById.Count)
        {
            errorMessage = "지형 정의 없이 Transform JSON에만 존재하는 placeId가 있습니다.";
            return false;
        }

        terrainData = new TerrainData
        {
            terrainName = definition.terrainName,
            chunkCount = definition.chunkCount,
            places = places
        };
        errorMessage = null;
        return true;
    }

    private static bool TryGetSlotPosition(
        TerrainPlaceTransform transformData,
        out Vector3 position)
    {
        position = default;
        if (transformData == null) return false;

        float x;
        if (string.Equals(transformData.side, "left", StringComparison.OrdinalIgnoreCase))
            x = LeftSlotX;
        else if (string.Equals(transformData.side, "right", StringComparison.OrdinalIgnoreCase))
            x = RightSlotX;
        else
            return false;

        int zOffset = transformData.z - FirstSlotZ;
        bool isValidZ = zOffset >= 0 &&
                        zOffset < SlotSpacingZ * SlotCountPerSide &&
                        zOffset % SlotSpacingZ == 0;
        if (!isValidZ) return false;

        // JSON은 배치 슬롯만 선택하고, 실제 청크 로컬 좌표는 이곳에서 일관되게 계산합니다.
        position = new Vector3(x, transformData.y, transformData.z);
        return true;
    }

    private static string CreateTransformPath(string terrainFilePath)
    {
        const string terrainFileName = "/Terrain";
        if (terrainFilePath.EndsWith(terrainFileName, StringComparison.Ordinal))
        {
            return terrainFilePath.Substring(
                0,
                terrainFilePath.Length - terrainFileName.Length) + "/Transform";
        }

        return terrainFilePath + "/Transform";
    }
}
