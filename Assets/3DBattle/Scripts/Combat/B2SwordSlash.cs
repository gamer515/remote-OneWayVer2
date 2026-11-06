using System.Collections.Generic;
using UnityEngine;

namespace OneWay.Battle3D
{
    public sealed class B2SwordSlash : MonoBehaviour
    {
        public BattleBoss boss;
        public SkinnedMeshRenderer body;
        public Transform swordGrip;
        public GameObject slashPrefab;
        [Min(.01f)] public float heightMultiplier = 2f;
        [Tooltip("참격 이동 속도 (유닛/초)")]
        [Min(0)] public float travelSpeed = 20f;
        [Tooltip("생성된 위치에서 기다린 뒤 이동을 시작하는 시간 (초)")]
        [Min(0)] public float launchDelay = .2f;
        [Tooltip("대기가 끝난 뒤 날아가는 시간 (초)")]
        [Min(.01f)] public float lifetime = 2f;
        [Header("Trail Length Multipliers (1 = original length)")]
        [Tooltip("맨 위 연기 길이 배율")][Min(0)] public float topTrailLength = 1f;
        [Tooltip("위와 중간 사이 연기 길이 배율")][Min(0)] public float upperMiddleTrailLength = .7f;
        [Tooltip("중앙 연기 길이 배율")][Min(0)] public float centerTrailLength = .4f;
        [Tooltip("아래와 중간 사이 연기 길이 배율")][Min(0)] public float lowerMiddleTrailLength = .7f;
        [Tooltip("맨 아래 연기 길이 배율")][Min(0)] public float bottomTrailLength = 1f;
        public int EmittedCount { get; private set; }
        public float BossHeight => body != null && body.sharedMesh != null
            ? body.sharedMesh.bounds.size.y * Mathf.Abs(body.transform.lossyScale.y) : 1f;
        readonly List<GameObject> active = new List<GameObject>();

        // Called by the downswing event, once per animation loop.
        public void EmitB2SwordSlash()
        {
            if (!isActiveAndEnabled || slashPrefab == null || boss == null || !boss.IsAlive) return;
            Vector3 forward = Vector3.ProjectOnPlane(boss.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .001f) forward = Vector3.forward;
            Vector3 origin = swordGrip != null ? swordGrip.position : transform.position + Vector3.up * BossHeight * .5f;
            var slash = Instantiate(slashPrefab, origin,
                Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(20f, 0f, 0f));
            slash.name = "B2 White Slash";
            slash.transform.localScale = Vector3.one * (BossHeight * heightMultiplier);
            var trails = slash.GetComponent<B2SlashTrailLayout>();
            if (trails != null)
                trails.ApplyLengths(topTrailLength, upperMiddleTrailLength, centerTrailLength,
                    lowerMiddleTrailLength, bottomTrailLength);
            slash.AddComponent<B2SlashFlight>().Initialize(forward, travelSpeed, lifetime, launchDelay);
            active.RemoveAll(g => g == null);
            active.Add(slash);
            EmittedCount++;
        }
        void OnDisable()
        {
            foreach (var g in active) if (g != null) Destroy(g);
            active.Clear();
        }
    }
}
