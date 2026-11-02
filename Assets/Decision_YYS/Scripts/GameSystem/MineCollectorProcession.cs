using System;
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
    [SerializeField] private float modelYawOffset = 180f;
    private Transform[] members;
    private MineChapterProgress progress;
    private Vector3 origin;
    private float chunkEnd;
    public float RowWaitSeconds => rowWaitSeconds;
    public int Count => members?.Length ?? 0;
    public string CurrentPhase => progress == null ? "NotStarted" : ((Phase)progress.collectorPhase).ToString();
    public bool CanMeet => progress != null && progress.collectorArrived && !progress.collectorPassedPlayer &&
        (Phase)progress.collectorPhase != Phase.Finished;
    public Transform NearestMember(Vector3 point) => members?.OrderBy(t => (t.position - point).sqrMagnitude).FirstOrDefault();

    public void Initialize(GameObject prefab, MineChapterProgress state, Vector3 start, float length, int seed)
    {
        if (prefab == null || stops?.Length != 4) throw new InvalidOperationException("징수원 모델과 네 장소의 입출구 앵커가 필요합니다.");
        origin = start; chunkEnd = start.z + length; progress = state;
        if (state.collectorRouteVersion != 2)
        {
            state.collectorRouteVersion = 2;
            state.collectorCount = new System.Random(seed).Next(minimumCount, Mathf.Max(minimumCount, maximumCount) + 1);
            state.collectorZ = chunkEnd; state.collectorDirection = -1;
            state.collectorPhase = (int)Phase.Arrival; state.collectorStop = stops.Length - 1;
            state.collectorPassedPlayer = state.collectorArrived = false; state.collectorWait = 0;
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
    private Quaternion Heading() => Quaternion.Euler(0, modelYawOffset + (progress.collectorDirection < 0 ? 180 : 0), 0);
    private Vector3 Desired(int index)
    {
        var phase = (Phase)progress.collectorPhase;
        bool row = phase != Phase.FormColumn && phase != Phase.March;
        float x = row ? (index - (Count - 1) * 0.5f) * spacing : 0;
        float z = progress.collectorZ - (row ? 0 : progress.collectorDirection * index * spacing);
        return new Vector3(origin.x + x, origin.y, z);
    }
    public void Tick(float seconds)
    {
        if (progress == null || members == null || (Phase)progress.collectorPhase == Phase.Finished) return;
        var phase = (Phase)progress.collectorPhase;
        float speed = phase == Phase.FormRow || phase == Phase.FormColumn || !progress.collectorArrived ? arrivalSpeed : marchSpeed;
        int direction = progress.collectorDirection;
        switch (phase)
        {
            case Phase.Arrival:
            case Phase.RowMarch:
                if (progress.collectorStop >= stops.Length) { progress.collectorPhase = (int)Phase.Leave; break; }
                float gate = direction > 0 ? stops[progress.collectorStop].entrance.position.z - entranceBuffer : stops[progress.collectorStop].exit.position.z + entranceBuffer;
                MoveLeader(gate, speed, seconds, Phase.FormColumn);
                break;
            case Phase.Wait:
                progress.collectorWait -= seconds;
                if (progress.collectorWait <= 0) progress.collectorPhase = (int)Phase.RowMarch;
                break;
            case Phase.March:
                float exit = direction > 0 ? stops[progress.collectorStop].exit.position.z + gatheringDistance : stops[progress.collectorStop].entrance.position.z - gatheringDistance;
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
        if ((Phase)progress.collectorPhase == Phase.Finished) SetVisible(false);
    }
    private void MoveLeader(float targetZ, float speed, float seconds, Phase next)
    {
        progress.collectorZ = Mathf.MoveTowards(progress.collectorZ, targetZ, speed * seconds);
        if (!Mathf.Approximately(progress.collectorZ, targetZ)) return;
        if ((Phase)progress.collectorPhase == Phase.March) progress.collectorStop += progress.collectorDirection;
        progress.collectorPhase = (int)next;
    }
    public void ContinueForward()
    {
        progress.collectorPassedPlayer = true; progress.collectorWait = 0;
        if ((Phase)progress.collectorPhase == Phase.Wait) progress.collectorPhase = (int)Phase.RowMarch;
    }
    public void SetVisible(bool visible) { if (members != null) foreach (var member in members) member.gameObject.SetActive(visible && (Phase)progress.collectorPhase != Phase.Finished); }
    private void OnDrawGizmosSelected()
    {
        if (stops == null) return;
        Gizmos.color = Color.cyan;
        foreach (var stop in stops) if (stop.entrance != null && stop.exit != null) Gizmos.DrawLine(stop.entrance.position, stop.exit.position);
    }
}
