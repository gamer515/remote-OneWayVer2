using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Editable scene configuration is the source of NPC spawn positions and doorway travel.</summary>
public sealed class MineNpcPlacement : MonoBehaviour
{
    [Serializable] public sealed class Place
    {
        public string placeId;
        public string label;
        [Tooltip("청크 기준 로컬 좌표. 이 다섯 위치 중 회차 Seed로 NPC 위치를 고릅니다.")]
        public Vector3[] spawnPoints;
        [Tooltip("후보 번호는 0부터 시작합니다. 비우면 다섯 후보를 모두 사용합니다.")]
        public int[] firstActorPoints, secondActorPoints;
        public Vector3 entrance, exit;
    }
    [SerializeField] private Place[] places;
    public Place[] Places => places;
    public float LateralReach(Vector3 origin) => places.SelectMany(p => p.spawnPoints)
        .Select(p => Mathf.Abs(transform.TransformPoint(p).x - origin.x)).DefaultIfEmpty(0.5f).Max();
    public void Apply(TerrainData terrain, int seed, Vector3 origin)
    {
        foreach (var place in terrain.places)
        {
            var config = places.FirstOrDefault(p => p.placeId == place.placeId);
            if (config?.spawnPoints == null || config.spawnPoints.Length == 0) continue;
            place.spawnPoints = config.spawnPoints.Select(p => transform.TransformPoint(p) - origin).ToArray();
            int Pick(int[] allowed, int excluded, string key)
            {
                var choices = (allowed?.Length > 0 ? allowed : Enumerable.Range(0, place.spawnPoints.Length).ToArray())
                    .Where(i => i >= 0 && i < place.spawnPoints.Length && i != excluded).Distinct().ToArray();
                if (choices.Length == 0) choices = Enumerable.Range(0, place.spawnPoints.Length).ToArray();
                return choices[(int)(MineChapterSettings.Roll(seed, 0, key) * choices.Length)];
            }
            int first = Pick(config.firstActorPoints, -1, place.placeId);
            int second = Pick(config.secondActorPoints, first, place.placeId + ":second");
            place.position = place.spawnPoints[first];
            place.actorPositions = new[] { place.position, place.spawnPoints[second] };
        }
    }
    public Vector3 ApproachPoint(Vector3 npc, Vector3 origin, float distance)
    {
        Vector3 towardLane = new Vector3(origin.x - npc.x, 0, 0);
        if (towardLane.sqrMagnitude < 0.01f) towardLane = Vector3.back;
        return npc + towardLane.normalized * distance;
    }
    public IEnumerable<Vector3> Path(Vector3 from, Vector3 destination)
    {
        float direction = Mathf.Sign(destination.z - from.z);
        var doors = places.SelectMany(p => new[] { transform.TransformPoint(p.entrance), transform.TransformPoint(p.exit) })
            .Where(p => direction * (p.z - from.z) > 0.01f && direction * (destination.z - p.z) > 0.01f)
            .OrderBy(p => direction * p.z);
        foreach (var door in doors) yield return door;
        yield return destination;
    }
    private void OnDrawGizmosSelected()
    {
        if (places == null) return;
        Gizmos.color = Color.cyan;
        foreach (var point in places.Where(p => p.spawnPoints != null).SelectMany(p => p.spawnPoints))
            Gizmos.DrawWireSphere(transform.TransformPoint(point), 0.15f);
    }
}
