using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 걷는 구간에 놓인 오브젝트를 월드 좌표로 검색합니다.
/// 청크 스트리밍이나 이야기 카드 순서와 독립적으로 동작합니다.
/// </summary>
public sealed class EncounterFlowController
{
    public readonly struct Encounter
    {
        public readonly string PlaceId;
        public readonly string PrefabId;
        public readonly string DisplayName;
        public readonly string ContentPath;
        public readonly Vector3 WorldPosition;

        public Encounter(PlaceData place, Vector3 worldPosition)
        {
            PlaceId = place.placeId;
            PrefabId = place.prefabId;
            ContentPath = place.connectStoryCards;
            DisplayName = !string.IsNullOrEmpty(place.characterId)
                ? place.characterId
                : place.prefabId;
            WorldPosition = worldPosition;
        }
    }

    private readonly List<Encounter> encounters = new List<Encounter>();

    public float EpisodeEndZ { get; }

    public EncounterFlowController(
        TerrainData terrain,
        TerrainPlaceRegistry registry,
        float episodeEndZ)
    {
        EpisodeEndZ = episodeEndZ;
        if (terrain?.places == null || registry == null)
            throw new ArgumentException("조우 탐색에 필요한 지형 데이터가 없습니다.");

        HashSet<string> placedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (PlaceData place in terrain.places)
        {
            if (place == null || string.IsNullOrWhiteSpace(place.connectStoryCards))
                continue;
            if (!placedIds.Add(place.placeId))
                throw new ArgumentException($"placeId '{place.placeId}'가 중복되었습니다.");

            encounters.Add(new Encounter(place, registry.GetWorldPosition(place)));
        }
        encounters.Sort((a, b) =>
        {
            int byZ = a.WorldPosition.z.CompareTo(b.WorldPosition.z);
            return byZ != 0 ? byZ : string.CompareOrdinal(a.PlaceId, b.PlaceId);
        });
    }

    public bool TryGetEncounter(string placeId, out Encounter encounter)
    {
        foreach (Encounter candidate in encounters)
        {
            if (candidate.PlaceId == placeId)
            {
                encounter = candidate;
                return true;
            }
        }

        encounter = default;
        return false;
    }

    public bool TryFindFirst(
        float startZ,
        float endZ,
        ISet<string> resolvedPlaces,
        out Encounter encounter)
    {
        foreach (Encounter candidate in encounters)
        {
            float eventZ = candidate.WorldPosition.z;
            // 같은 좌표에 NPC와 건물이 있으면 하나를 처리한 뒤 다음 진행에서 다른 하나도 만납니다.
            if (eventZ < startZ - 0.01f || eventZ > endZ + 0.01f ||
                resolvedPlaces.Contains(candidate.PlaceId))
            {
                continue;
            }

            encounter = candidate;
            return true;
        }

        encounter = default;
        return false;
    }
}
