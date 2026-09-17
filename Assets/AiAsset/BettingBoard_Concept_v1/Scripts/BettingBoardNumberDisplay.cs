using UnityEngine;

namespace BettingBoardAssets
{
    /// <summary>A three-digit, zero-padded display using the board's original 3D digits.</summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Betting Board/Number Display")]
    public sealed class BettingBoardNumberDisplay : MonoBehaviour
    {
        public const int MinValue = 0;
        public const int MaxValue = 999;

        [SerializeField, Range(MinValue, MaxValue)]
        [Tooltip("Displayed number (000–999). Updates in Edit Mode and Play Mode.")]
        private int value = 24;

        [SerializeField, HideInInspector] private MeshFilter[] digitSlots;
        [SerializeField, HideInInspector] private Mesh[] digitMeshes;
        private bool refreshPending = true;

        public int Value
        {
            get => value;
            set => SetValue(value);
        }

        /// <summary>Call on Unity's main thread when the game value changes. Also usable from UnityEvent&lt;int&gt;.</summary>
        public void SetValue(int newValue)
        {
            value = Mathf.Clamp(newValue, MinValue, MaxValue);
            refreshPending = true;
            RefreshDisplay();
        }

        private void OnEnable()
        {
            refreshPending = true;
            RefreshDisplay();
        }

        private void OnValidate()
        {
            // OnValidate may run off the main thread. Only validate data here.
            value = Mathf.Clamp(value, MinValue, MaxValue);
            refreshPending = true;
        }

        private void Update()
        {
            if (refreshPending) RefreshDisplay();
        }

        private void RefreshDisplay()
        {
            if (digitSlots == null || digitSlots.Length != 3 ||
                digitMeshes == null || digitMeshes.Length != 10) return;

            int number = Mathf.Clamp(value, MinValue, MaxValue);
            int divisor = 100;
            for (int i = 0; i < 3; i++)
            {
                MeshFilter slot = digitSlots[i];
                Mesh mesh = digitMeshes[number / divisor % 10];
                if (slot == null || mesh == null) return;
                if (slot.sharedMesh != mesh) slot.sharedMesh = mesh;
                divisor /= 10;
            }
            refreshPending = false;
        }
    }
}
