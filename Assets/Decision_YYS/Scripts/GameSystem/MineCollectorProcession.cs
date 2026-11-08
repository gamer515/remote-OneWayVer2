using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Outside: moving rows. Doorways: columns. The saved route first travels 4→1, then 1→4.</summary>
public sealed class MineCollectorProcession : MonoBehaviour
{
    [Serializable] public struct Stop { public Transform entrance, exit; }
    private enum Phase { Arrival, FormRow, Wait, FormColumn, March, Leave, Finished, RowMarch }
    [Header("Formation")]
    [SerializeField, Range(3, 5)] private int minimumCount = 3;
    [SerializeField, Range(3, 5)] private int maximumCount = 5;
    [SerializeField, Min(0.5f)] private float spacing = 1.2f;
    [SerializeField, Min(0)] private float gatheringDistance = 7f;
    [Tooltip("입구 바로 앞에서 세로 대형으로 전환하는 거리")]
    [SerializeField, Min(0.1f)] private float entranceBuffer = 1.5f;
    [Header("Timing / seconds")]
    [Tooltip("출구 밖에서 가로 정렬을 마친 뒤 대기하는 시간(초)")]
    [SerializeField, Min(0)] private float rowWaitSeconds = 3f;
    [SerializeField, Min(0.01f)] private float arrivalSpeed = 3f;
    [SerializeField, Min(0.01f)] private float marchSpeed = 0.25f;
    [Header("Authored entry/exit anchors, first to fourth place")]
    [SerializeField] private Stop[] stops;
    private (float entrance, float exit)[] routeDoors;
    [SerializeField] private float modelYawOffset = 180f;
    [SerializeField, Min(.5f)] private float dialogueClearance = 1.5f;
    private Transform[] members;
    private MineChapterProgress progress;
    private Vector3 origin;
    private float chunkEnd, preparationZ;
    private float firstPassSpeed, npcWaitMin, npcWaitMax;
    private int routeSeed;
    private Vector3[][] npcTargets;
    private Vector3[] dialoguePositions;
    public float RowWaitSeconds => rowWaitSeconds;
    public int Count => members?.Length ?? 0;
    public bool Finished => progress != null && (Phase)progress.collectorPhase == Phase.Finished;
    public string CurrentPhase => progress == null ? "NotStarted" : ((Phase)progress.collectorPhase).ToString();
    public bool CanMeet => progress != null && progress.collectorArrived && !progress.collectorPassedPlayer &&
        (Phase)progress.collectorPhase != Phase.Finished;
    public Transform NearestMember(Vector3 point) => members?.OrderBy(t => (t.position - point).sqrMagnitude).FirstOrDefault();
    public Vector3? CurrentNpcPosition
    {
        get
        {
            int stop = progress.collectorNpcPauseIndex - 1;
            int actor = progress.collectorNpcActorIndex - 1;
            return stop >= 0 && stop < npcTargets.Length && actor >= 0 && actor < npcTargets[stop].Length
                ? npcTargets[stop][actor] : null;
        }
    }

    public void Initialize(GameObject prefab, MineChapterProgress state, Vector3 start, float length, int seed,
        float firstPassSeconds = 0, float minimumNpcWait = 0, float maximumNpcWait = 0,
        IEnumerable<Vector3> npcPositions = null, IEnumerable<(Vector3 entrance, Vector3 exit)> placedDoors = null)
    {
        if (prefab == null || stops?.Length != 4) throw new InvalidOperationException("징수원 모델과 네 장소의 입출구 앵커가 필요합니다.");
        origin = start; chunkEnd = start.z + length; progress = state;
        routeDoors = placedDoors?.OrderBy(door => door.entrance.z).Select(door => (door.entrance.z, door.exit.z)).ToArray();
        if (routeDoors?.Length != stops.Length)
            routeDoors = stops.Select(stop => (stop.entrance.position.z, stop.exit.position.z)).ToArray();
        preparationZ = routeDoors[0].entrance - gatheringDistance;
        firstPassSpeed = firstPassSeconds > 0 ? Mathf.Abs(chunkEnd - preparationZ) / firstPassSeconds : arrivalSpeed;
        npcWaitMin = minimumNpcWait;
        npcWaitMax = maximumNpcWait;
        routeSeed = seed;
        var midpoints = routeDoors.Select(door => (door.entrance + door.exit) * .5f).ToArray();
        var targets = (npcPositions ?? midpoints.Select(z => new Vector3(start.x, start.y, z)))
            .Distinct().OrderBy(point => point.z).ThenBy(point => point.x)
            .ToLookup(point => Enumerable.Range(0, midpoints.Length)
                .OrderBy(index => Mathf.Abs(midpoints[index] - point.z)).First());
        npcTargets = Enumerable.Range(0, stops.Length).Select(index => targets[index].ToArray()).ToArray();
        if (state.collectorRouteVersion != 2)
        {
            state.collectorRouteVersion = 2;
            state.collectorCount = new System.Random(seed).Next(minimumCount, Mathf.Max(minimumCount, maximumCount) + 1);
            BeginRoute();
        }
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
    public void BeginRoute()
    {
        progress.collectorZ = chunkEnd; progress.collectorDirection = -1;
        progress.collectorPhase = (int)Phase.Arrival; progress.collectorStop = stops.Length - 1;
        progress.collectorPassedPlayer = progress.collectorArrived = false;
        progress.collectorWait = progress.collectorNpcPause = 0;
        progress.collectorNpcPauseIndex = progress.collectorNpcWitnessIndex = 0;
        progress.collectorNpcActorIndex = progress.collectorNpcWitnessActor = 0;
        for (int i = 0; i < Count; i++) members[i].SetPositionAndRotation(Desired(i), Heading());
        SetVisible(true);
    }
    private Quaternion Heading() => Quaternion.Euler(0, modelYawOffset + (progress.collectorDirection < 0 ? 180 : 0), 0);
    private Vector3 Desired(int index)
    {
        var phase = (Phase)progress.collectorPhase;
        bool row = phase != Phase.Arrival && phase != Phase.FormColumn && phase != Phase.March;
        float x = row ? (index - (Count - 1) * 0.5f) * spacing : 0;
        float z = progress.collectorZ - (row ? 0 : progress.collectorDirection * index * spacing);
        return new Vector3(origin.x + x, origin.y, z);
    }
    public void Tick(float seconds, float waitSeconds = -1f)
    {
        if (progress == null || members == null) return;
        if (waitSeconds < 0) waitSeconds = seconds;
        if (Finished) return;
        if (progress.collectorNpcPause > 0)
        {
            progress.collectorNpcPause = Mathf.Max(0, progress.collectorNpcPause - waitSeconds);
            return;
        }
        var phase = (Phase)progress.collectorPhase;
        float speed = !progress.collectorArrived ? firstPassSpeed :
            phase == Phase.FormRow || phase == Phase.FormColumn ? arrivalSpeed : marchSpeed;
        int direction = progress.collectorDirection;
        switch (phase)
        {
            case Phase.Arrival:
                MoveLeader(preparationZ, firstPassSpeed, seconds, Phase.FormRow);
                if ((Phase)progress.collectorPhase == Phase.FormRow) progress.collectorStop = -1;
                break;
            case Phase.RowMarch:
                if (progress.collectorStop >= stops.Length) { progress.collectorPhase = (int)Phase.Leave; break; }
                var nextDoor = routeDoors[progress.collectorStop];
                float gate = direction > 0 ? nextDoor.entrance - entranceBuffer : nextDoor.exit + entranceBuffer;
                MoveLeader(gate, speed, seconds, Phase.FormColumn);
                break;
            case Phase.Wait:
                progress.collectorWait -= waitSeconds;
                if (progress.collectorWait <= 0) progress.collectorPhase = (int)Phase.RowMarch;
                break;
            case Phase.March:
                var currentDoor = routeDoors[progress.collectorStop];
                float exit = direction > 0 ? currentDoor.exit + gatheringDistance : currentDoor.entrance - gatheringDistance;
                if (direction < 0 || !TryPauseAtNpc(exit, speed * seconds))
                    MoveLeader(exit, speed, seconds, Phase.FormRow);
                break;
            case Phase.Leave:
                MoveLeader(chunkEnd + spacing, speed, seconds, Phase.Finished);
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
            if (progress.collectorStop < 0) { progress.collectorDirection = 1; progress.collectorStop = 0; progress.collectorArrived = true; }
            foreach (var member in members) member.rotation = Heading();
            progress.collectorPhase = (int)Phase.Wait;
            progress.collectorWait = progress.collectorPassedPlayer ? 0 : rowWaitSeconds;
        }
        else if (settled && (Phase)progress.collectorPhase == Phase.FormColumn)
        {
            foreach (var member in members) member.rotation = Heading();
            progress.collectorPhase = (int)Phase.March;
        }
        if (Finished) SetVisible(false);
    }
    private bool TryPauseAtNpc(float exit, float travel)
    {
        if (npcWaitMax <= 0) return false;
        int stop = progress.collectorStop;
        float entry = routeDoors[stop].entrance - entranceBuffer;
        var points = npcTargets[stop];
        while (progress.collectorNpcActorIndex < points.Length)
        {
            int actor = progress.collectorNpcActorIndex;
            float z = Mathf.Clamp(points[actor].z, entry, exit);
            if (z > progress.collectorZ + travel) return false;
            progress.collectorNpcActorIndex++;
            if (z < progress.collectorZ - .001f) continue;
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
            progress.collectorStop += progress.collectorDirection;
            progress.collectorNpcActorIndex = 0;
        }
        progress.collectorPhase = (int)next;
    }
    public void ContinueForward()
    {
        progress.collectorPassedPlayer = true; progress.collectorWait = 0;
        if ((Phase)progress.collectorPhase == Phase.Wait) progress.collectorPhase = (int)Phase.RowMarch;
    }
    public void ArrangeForDialogue(Vector3 viewer, Vector3[] residents, Vector3? witness)
    {
        if (members == null || residents.Length == 0) return;
        RestoreDialoguePositions();
        dialoguePositions = members.Select(member => member.position).ToArray();
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
    }

    public void RestoreDialoguePositions()
    {
        if (dialoguePositions == null) return;
        for (int i = 0; i < dialoguePositions.Length; i++) members[i].position = dialoguePositions[i];
        dialoguePositions = null;
    }

    private static float FlatDistanceSquared(Vector3 a, Vector3 b) =>
        new Vector2(a.x - b.x, a.z - b.z).sqrMagnitude;
    public void SetVisible(bool visible) { if (members != null) foreach (var member in members) member.gameObject.SetActive(visible && (Phase)progress.collectorPhase != Phase.Finished); }
    private void OnDrawGizmosSelected()
    {
        if (stops == null) return;
        Gizmos.color = Color.cyan;
        foreach (var stop in stops) if (stop.entrance != null && stop.exit != null) Gizmos.DrawLine(stop.entrance.position, stop.exit.position);
    }
}
