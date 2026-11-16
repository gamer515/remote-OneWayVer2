using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Patrols from the far edge of a chunk and marches forward after turning.</summary>
public sealed class MineCollectorProcession : MonoBehaviour
{
    [Serializable] public struct Stop { public Transform entrance, exit; }
    private enum Phase { Arrival, FormRow, Wait, FormColumn, March, Leave, Finished, RowMarch }
    [Header("Formation")]
    [SerializeField, Range(3, 5)] private int minimumCount = 3;
    [SerializeField, Range(3, 5)] private int maximumCount = 5;
    [SerializeField, Min(0.5f)] private float spacing = 1.2f;
    [SerializeField, Min(0)] private float gatheringDistance = 7f;
    [SerializeField, Min(0)] private float entranceOvershoot = 4f;
    [Tooltip("입구 바로 앞에서 세로 대형으로 전환하는 거리")]
    [SerializeField, Min(0.1f)] private float entranceBuffer = 1.5f;
    [Header("Timing / seconds")]
    [Tooltip("출구 밖에서 가로 정렬을 마친 뒤 대기하는 시간(초)")]
    [SerializeField, Min(0)] private float rowWaitSeconds = 3f;
    [SerializeField, Min(0.01f)] private float arrivalSpeed = 3f;
    [SerializeField, Min(0.01f)] private float marchSpeed = 3f;
    [Header("Authored entry/exit anchors, first to fourth place")]
    [SerializeField] private Stop[] stops;
    private (float entrance, float exit)[] authoredDoors, routeDoors;
    [SerializeField] private float modelYawOffset = 180f;
    [SerializeField, Min(.5f)] private float dialogueClearance = 1.5f;
    private Transform[] members;
    private MineChapterProgress progress;
    private Vector3 origin;
    private float chunkStart, chunkEnd, preparationZ;
    private float firstPassSpeed, firstPassSeconds, npcWaitMin, npcWaitMax;
    private int routeSeed;
    private const int PatrolVersion = 5;
    private MineCollectorPatrolSettings patrolSettings;
    private Vector3[][] npcTargets, routeNpcTargets;
    private (Vector3 position, Quaternion rotation)[] dialoguePoses;
    public float RowWaitSeconds => rowWaitSeconds;
    public int Count => members?.Length ?? 0;
    public bool Finished => progress != null && (Phase)progress.collectorPhase == Phase.Finished;
    public bool IsOnPatrol => progress?.taxPatrolActive == true && !Finished;
    public bool IsReturning => progress?.collectorReturning == true;
    public bool IsVisible => IsOnPatrol;
    public IReadOnlyList<Transform> Members => members ?? Array.Empty<Transform>();
    public string CurrentPhase => progress == null ? "NotStarted" : ((Phase)progress.collectorPhase).ToString();
    public int ChunkNumber => progress.collectorChunkIndex + 1;
    public int InspectionMinimum => patrolSettings.MinimumCoins(progress.collectorChunkIndex);
    public bool CanMeet(Vector3 point) => IsOnPatrol && point.z >= chunkStart && point.z < chunkEnd &&
        (IsReturning ? !progress.collectorChaseClosed && progress.HasTaxInvoice : !progress.collectorPassedPlayer);

    public bool RecordPlayerEscape(float z)
    {
        if (!IsOnPatrol || progress.collectorChaseClosed || z < chunkEnd) return false;
        progress.collectorChaseClosed = true;
        return true;
    }
    public Transform NearestMember(Vector3 point) => members?.OrderBy(t => (t.position - point).sqrMagnitude).FirstOrDefault();
    public Transform MemberInRange(Vector3 point, float distance)
    {
        Transform nearest = null;
        float nearestDistance = distance * distance;
        foreach (var member in Members)
        {
            float separation = FlatDistanceSquared(point, member.position);
            if (separation > nearestDistance) continue;
            nearestDistance = separation;
            nearest = member;
        }
        return nearest;
    }
    public Vector3? CurrentNpcPosition
    {
        get
        {
            int stop = progress.collectorNpcPauseIndex - 1;
            int actor = progress.collectorNpcActorIndex - 1;
            return stop >= 0 && stop < routeNpcTargets.Length && actor >= 0 && actor < routeNpcTargets[stop].Length
                ? routeNpcTargets[stop][routeNpcTargets[stop].Length - 1 - actor] : null;
        }
    }

    public void Initialize(GameObject prefab, MineChapterProgress state, Vector3 start, float length, int seed,
        float firstPassSeconds = 0, float minimumNpcWait = 0, float maximumNpcWait = 0,
        IEnumerable<Vector3> npcPositions = null, IEnumerable<(Vector3 entrance, Vector3 exit)> placedDoors = null,
        MineCollectorPatrolSettings settings = null)
    {
        if (prefab == null) throw new InvalidOperationException("징수원 모델이 필요합니다.");
        origin = start; progress = state;
        patrolSettings = settings ?? new MineCollectorPatrolSettings();
        this.firstPassSeconds = firstPassSeconds;
        authoredDoors = placedDoors?.OrderBy(door => door.entrance.z).Select(door => (door.entrance.z, door.exit.z)).ToArray();
        if (authoredDoors == null || authoredDoors.Length == 0)
            authoredDoors = (stops ?? Array.Empty<Stop>()).Where(stop => stop.entrance != null && stop.exit != null)
                .OrderBy(stop => stop.entrance.position.z).Select(stop => (stop.entrance.position.z, stop.exit.position.z)).ToArray();
        npcWaitMin = minimumNpcWait;
        npcWaitMax = maximumNpcWait;
        routeSeed = seed;
        var midpoints = authoredDoors.Select(door => (door.entrance + door.exit) * .5f).ToArray();
        var targets = (midpoints.Length == 0 ? Enumerable.Empty<Vector3>() :
                npcPositions ?? midpoints.Select(z => new Vector3(start.x, start.y, z)))
            .Distinct().OrderBy(point => point.z).ThenBy(point => point.x)
            .ToLookup(point => Enumerable.Range(0, midpoints.Length)
                .OrderBy(index => Mathf.Abs(midpoints[index] - point.z)).First());
        npcTargets = Enumerable.Range(0, authoredDoors.Length).Select(index => targets[index].ToArray()).ToArray();
        float endZ = (state.taxPatrolActive || state.collectorOpeningCompleted) && state.collectorRouteEndZ > start.z
            ? state.collectorRouteEndZ : start.z + length;
        bool legacy = state.collectorRouteVersion < PatrolVersion;
        bool legacyInspectionDone = state.taxPatrolActive && (state.collectorPassedPlayer || !state.HasTaxInvoice);
        if (state.collectorRouteVersion < 4)
        {
            if (state.collectorCount == 0)
                state.collectorCount = new System.Random(seed).Next(minimumCount, Mathf.Max(minimumCount, maximumCount) + 1);
            BeginRoute(start.z, endZ, 0);
            state.collectorPassedPlayer = legacyInspectionDone;
        }
        else ConfigureRoute(state.collectorRouteStartZ > start.z ? state.collectorRouteStartZ : start.z, endZ);
        if (legacy)
        {
            state.collectorRouteVersion = PatrolVersion;
            state.collectorChunkIndex = Mathf.Max(0, Mathf.RoundToInt((chunkStart - start.z) / Mathf.Max(.01f, chunkEnd - chunkStart)));
            // The former pass flag also marked a resolved general invoice.
            state.collectorPassedPlayer = legacyInspectionDone;
            state.collectorChaseClosed = legacyInspectionDone;
        }
        if (Finished && !IsReturning && state.taxPatrolActive) BeginReturn();
        if (members != null) return;
        members = new Transform[state.collectorCount];
        for (int i = 0; i < members.Length; i++)
        {
            members[i] = Instantiate(prefab, transform).transform;
            members[i].name = "Collector_" + (i + 1);
            foreach (var script in members[i].GetComponentsInChildren<MonoBehaviour>()) script.enabled = false;
            foreach (var animator in members[i].GetComponentsInChildren<Animator>()) animator.enabled = false;
            members[i].SetPositionAndRotation(Desired(i), Heading());
        }
    }
    private void ConfigureRoute(float startZ, float endZ)
    {
        chunkStart = startZ;
        chunkEnd = endZ;
        var route = Enumerable.Range(0, authoredDoors.Length)
            .Where(index => authoredDoors[index].entrance >= startZ && authoredDoors[index].exit <= endZ)
            .ToArray();
        routeDoors = route.Select(index => authoredDoors[index]).ToArray();
        routeNpcTargets = route.Select(index => npcTargets[index]).ToArray();
        preparationZ = routeDoors.Length > 0 ? Mathf.Max(chunkStart, routeDoors[0].entrance - gatheringDistance) : chunkStart;
        float tail = (progress.collectorCount - 1) * spacing;
        float formationTravel = 2f * routeDoors.Length * new Vector2(tail, tail * .5f).magnitude;
        firstPassSpeed = firstPassSeconds > 0 ? (chunkEnd - preparationZ + formationTravel) / firstPassSeconds : arrivalSpeed;
    }

    public void BeginRoute(float startZ, float endZ, int chunkIndex)
    {
        ConfigureRoute(startZ, endZ);
        progress.collectorRouteStartZ = chunkStart;
        progress.collectorRouteEndZ = chunkEnd;
        progress.collectorChunkIndex = chunkIndex;
        progress.collectorChaseClosed = false;
        progress.collectorZ = chunkEnd;
        progress.collectorReturning = false;
        progress.collectorPhase = (int)(routeDoors.Length == 0 ? Phase.Arrival : Phase.RowMarch);
        progress.collectorStop = routeDoors.Length - 1;
        progress.collectorPassedPlayer = false;
        progress.collectorWait = progress.collectorNpcPause = 0;
        progress.collectorNpcPauseIndex = progress.collectorNpcWitnessIndex = 0;
        progress.collectorNpcActorIndex = progress.collectorNpcWitnessActor = 0;
        for (int i = 0; i < Count; i++) members[i].SetPositionAndRotation(Desired(i), Heading());
        SetVisible(true);
    }
    private int Direction => IsReturning ? 1 : -1;
    private Quaternion Heading() => Quaternion.Euler(0, modelYawOffset + (IsReturning ? 0 : 180), 0);
    private Vector3 Desired(int index)
    {
        var phase = (Phase)progress.collectorPhase;
        bool row = phase != Phase.Arrival && phase != Phase.FormColumn && phase != Phase.March;
        float x = row ? (index - (Count - 1) * 0.5f) * spacing : 0;
        float z = progress.collectorZ - (row ? 0 : Direction * index * spacing);
        return new Vector3(origin.x + x, origin.y, z);
    }
    public void Tick(float seconds, float waitSeconds = -1f, bool skipNpcStops = false)
    {
        if (progress == null || members == null) return;
        if (waitSeconds < 0) waitSeconds = seconds;
        if (Finished) return;
        if (progress.collectorNpcPause > 0)
        {
            progress.collectorNpcPause = skipNpcStops ? 0 : Mathf.Max(0, progress.collectorNpcPause - waitSeconds);
            if (!skipNpcStops) return;
        }
        var phase = (Phase)progress.collectorPhase;
        float speed = IsReturning ? patrolSettings.ChaseSpeed(progress.collectorChunkIndex) : phase == Phase.FormRow || phase == Phase.FormColumn ? firstPassSpeed :
            progress.collectorPassedPlayer ? marchSpeed : firstPassSpeed;
        switch (phase)
        {
            case Phase.Arrival:
                MoveLeader(preparationZ, firstPassSpeed, seconds, routeDoors.Length == 0 ? Phase.Leave : Phase.FormRow);
                if ((Phase)progress.collectorPhase == Phase.FormRow) progress.collectorStop = -1;
                break;
            case Phase.RowMarch:
                if (progress.collectorStop < 0 || progress.collectorStop >= routeDoors.Length)
                {
                    progress.collectorPhase = (int)Phase.Leave;
                    break;
                }
                var nextDoor = routeDoors[progress.collectorStop];
                float gate = IsReturning ? Mathf.Max(progress.collectorZ, nextDoor.entrance - entranceBuffer) :
                    Mathf.Min(progress.collectorZ, nextDoor.exit + entranceBuffer);
                MoveLeader(gate, speed, seconds, Phase.FormColumn);
                break;
            case Phase.Wait:
                progress.collectorWait -= waitSeconds;
                if (progress.collectorWait <= 0) progress.collectorPhase = (int)Phase.RowMarch;
                break;
            case Phase.March:
                var currentDoor = routeDoors[progress.collectorStop];
                float clearance = Mathf.Max(gatheringDistance, spacing * (Count - 1) + entranceBuffer);
                int nextStop = progress.collectorStop + Direction;
                float exit = IsReturning
                    ? Mathf.Max(progress.collectorZ, Mathf.Min(currentDoor.exit + clearance,
                        nextStop < routeDoors.Length ? routeDoors[nextStop].entrance - entranceBuffer : chunkEnd))
                    : Mathf.Min(progress.collectorZ, Mathf.Max(currentDoor.entrance - clearance,
                        nextStop >= 0 ? routeDoors[nextStop].exit + entranceBuffer : preparationZ));
                if (IsReturning || skipNpcStops || !TryPauseAtNpc(exit, speed * seconds))
                    MoveLeader(exit, speed, seconds, Phase.FormRow);
                break;
            case Phase.Leave:
                if (IsReturning)
                    MoveLeader(chunkEnd, speed, seconds, Phase.Finished);
                else
                {
                    float turnaround = chunkStart - entranceOvershoot;
                    MoveLeader(turnaround, speed, seconds, Phase.Leave);
                    if (Mathf.Approximately(progress.collectorZ, turnaround)) BeginReturn();
                }
                break;
        }
        bool settled = true;
        for (int i = 0; i < Count; i++)
        {
            Vector3 desired = Desired(i), movement = desired - members[i].position;
            members[i].position = Vector3.MoveTowards(members[i].position, desired, speed * seconds);
            if (movement.sqrMagnitude > 0.0001f)
                members[i].rotation = Quaternion.LookRotation(movement.normalized) * Quaternion.Euler(0, modelYawOffset, 0);
            settled &= (members[i].position - desired).sqrMagnitude < 0.001f;
        }
        if (settled && (Phase)progress.collectorPhase == Phase.FormRow)
        {
            foreach (var member in members) member.rotation = Heading();
            progress.collectorPhase = (int)Phase.Wait;
            progress.collectorWait = IsReturning ? 0 : rowWaitSeconds;
        }
        else if (settled && (Phase)progress.collectorPhase == Phase.FormColumn)
        {
            foreach (var member in members) member.rotation = Heading();
            progress.collectorPhase = (int)Phase.March;
        }
    }

    private void BeginReturn()
    {
        progress.collectorReturning = true;
        progress.collectorPhase = (int)Phase.Leave;
        progress.collectorNpcPause = 0;
        foreach (var member in Members) member.rotation = Heading();
    }
    private bool TryPauseAtNpc(float exit, float travel)
    {
        if (npcWaitMax <= 0) return false;
        int stop = progress.collectorStop;
        float entry = routeDoors[stop].exit + entranceBuffer;
        var points = routeNpcTargets[stop];
        while (progress.collectorNpcActorIndex < points.Length)
        {
            int actor = points.Length - 1 - progress.collectorNpcActorIndex;
            float z = Mathf.Clamp(points[actor].z, exit, entry);
            if (z < progress.collectorZ - travel) return false;
            progress.collectorNpcActorIndex++;
            if (z > progress.collectorZ + .001f) continue;
            progress.collectorZ = z;
            progress.collectorNpcPauseIndex = stop + 1;
            progress.collectorNpcPause = Mathf.Lerp(npcWaitMin, npcWaitMax,
                (float)MineChapterSettings.Roll(routeSeed, Mathf.FloorToInt(progress.elapsedSeconds), $"collector:{stop}:{actor}"));
            return true;
        }
        return false;
    }

    private void MoveLeader(float targetZ, float speed, float seconds, Phase next)
    {
        progress.collectorZ = Mathf.MoveTowards(progress.collectorZ, targetZ, speed * seconds);
        if (!Mathf.Approximately(progress.collectorZ, targetZ)) return;
        if ((Phase)progress.collectorPhase == Phase.March)
        {
            progress.collectorStop += Direction;
            progress.collectorNpcActorIndex = 0;
        }
        progress.collectorPhase = (int)next;
    }
    public void CompleteInspection()
    {
        progress.collectorPassedPlayer = true; progress.collectorWait = 0;
        if ((Phase)progress.collectorPhase == Phase.Wait) progress.collectorPhase = (int)Phase.RowMarch;
    }
    public void ArrangeForDialogue(Vector3 viewer, Vector3[] residents, Vector3? witness)
    {
        if (members == null) return;
        RestoreDialoguePositions();
        dialoguePoses = members.Select(member => (member.position, member.rotation)).ToArray();
        float clearanceSquared = dialogueClearance * dialogueClearance;
        for (int i = 0; i < members.Length; i++)
        {
            Transform member = members[i];
            Vector3 start = member.position;
            if (Clear(start, false)) continue;
            for (int sample = 0; sample < 16; sample++)
            {
                float angle = sample % 8 * Mathf.PI / 4f;
                Vector3 candidate = start + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) *
                    (sample < 8 ? dialogueClearance : 2f * dialogueClearance);
                if (!Clear(candidate, true)) continue;
                member.position = candidate;
                break;
            }

            bool Clear(Vector3 candidate, bool checkMovement)
            {
                if (residents.Any(point => FlatDistanceSquared(point, candidate) < clearanceSquared)) return false;
                if (witness.HasValue)
                {
                    Vector3 line = witness.Value - viewer;
                    line.y = 0;
                    float along = Vector3.Dot(candidate - viewer, line) / Mathf.Max(.001f, line.sqrMagnitude);
                    if (along > 0f && along < 1f &&
                        FlatDistanceSquared(candidate, viewer + line * along) < clearanceSquared * .25f) return false;
                }
                if (!checkMovement) return true;
                if (members.Any(other => other != member && FlatDistanceSquared(other.position, candidate) < clearanceSquared))
                    return false;
                return !Physics.RaycastAll(start + Vector3.up * .8f, (candidate - start).normalized,
                    Vector3.Distance(start, candidate), ~0, QueryTriggerInteraction.Ignore)
                    .Any(hit => hit.collider.gameObject.isStatic);
            }
        }
        foreach (var member in members)
        {
            Vector3 facing = viewer - member.position;
            facing.y = 0;
            if (facing.sqrMagnitude > .001f)
                member.rotation = Quaternion.LookRotation(facing) * Quaternion.Euler(0, modelYawOffset, 0);
        }
    }

    public void RestoreDialoguePositions()
    {
        if (dialoguePoses == null) return;
        for (int i = 0; i < dialoguePoses.Length; i++)
            members[i].SetPositionAndRotation(dialoguePoses[i].position, dialoguePoses[i].rotation);
        dialoguePoses = null;
    }

    private static float FlatDistanceSquared(Vector3 a, Vector3 b) =>
        new Vector2(a.x - b.x, a.z - b.z).sqrMagnitude;
    public void SetVisible(bool visible) { if (members != null) foreach (var member in members) member.gameObject.SetActive(visible); }
    private void OnDrawGizmosSelected()
    {
        if (stops == null) return;
        Gizmos.color = Color.cyan;
        foreach (var stop in stops) if (stop.entrance != null && stop.exit != null) Gizmos.DrawLine(stop.entrance.position, stop.exit.position);
    }
}
