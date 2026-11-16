using UnityEngine;

namespace OneWay.Battle3D
{
    /// <summary>A stationary edit-mode view of the actual flying slash, for positioning its Transform.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class B2SlashPlacementPreview : MonoBehaviour
    {
        [Tooltip("날아가는 참격을 이 시간의 모양으로 표시합니다. 높이/각도는 위 Transform에서 조절하세요.")]
        [Range(.05f, 2f)] public float sampleTime = .65f;
        ParticleSystem[] particles;
        float sampledTime = -1f;

        void OnEnable()
        {
            // This object is only an authoring reference, not another gameplay attack.
            if (Application.IsPlaying(gameObject)) { gameObject.SetActive(false); return; }
            particles = GetComponentsInChildren<ParticleSystem>(true);
            Sample();
        }

        void Update()
        {
            if (Application.IsPlaying(gameObject)) return;
            if (particles == null || transform.hasChanged || sampledTime != sampleTime) Sample();
        }

        public void Sample()
        {
            if (Application.IsPlaying(gameObject)) return;
            if (particles == null) particles = GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in particles)
            {
                if (!ps || !ps.gameObject.activeInHierarchy) continue;
                ps.Simulate(sampleTime, false, true, false);
                ps.Pause(false);
            }
            sampledTime = sampleTime;
            transform.hasChanged = false;
        }
    }
}
