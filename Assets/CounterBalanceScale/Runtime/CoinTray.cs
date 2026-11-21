using System.Collections.Generic;
using UnityEngine;

namespace CounterBalanceScale
{
    /// <summary>Stores weights and stacks the existing game coin prefabs.</summary>
    [DisallowMultipleComponent]
    public sealed class CoinTray : MonoBehaviour
    {
        [SerializeField] private GameObject coinPrefab;
        [SerializeField] private Transform[] stackBases;
        [SerializeField, Min(1)] private int layersPerStack = 5;
        [SerializeField, Min(0.001f)] private float coinHeight = 0.0045f;

        private readonly List<Entry> coins = new();
        private double weightSum;

        public int Count => coins.Count;
        public int Capacity => (stackBases?.Length ?? 0) * layersPerStack;
        public float TotalWeight => (float)weightSum;

        private readonly struct Entry
        {
            public readonly GameObject Visual;
            public readonly double PreviousWeight;

            public Entry(GameObject visual, double previousWeight)
            {
                Visual = visual;
                PreviousWeight = previousWeight;
            }
        }

        /// <summary>Fills each layer from the first stack to the last.</summary>
        public bool TryAddCoin(float weight) => TryAddCoin(weight, coinPrefab);

        public bool TryAddCoin(float weight, GameObject visualPrefab)
        {
            if (Count >= Capacity || !(weight > 0f) || float.IsInfinity(weight) ||
                weightSum + weight > float.MaxValue)
                return false;

            int slot = Count % stackBases.Length;
            int layer = Count / stackBases.Length;
            if (!stackBases[slot])
                return false;

            GameObject visual = null;
            if (visualPrefab)
            {
                visual = Instantiate(visualPrefab, stackBases[slot], false);
                visual.transform.localScale = visualPrefab.transform.localScale * 0.25f;
                foreach (Rigidbody body in visual.GetComponentsInChildren<Rigidbody>())
                {
                    body.interpolation = RigidbodyInterpolation.None;
                    body.isKinematic = true;
                    body.detectCollisions = false;
                    Destroy(body);
                }
                foreach (Collider collider in visual.GetComponentsInChildren<Collider>())
                    collider.enabled = false;
                visual.transform.SetLocalPositionAndRotation(
                    Vector3.up * (layer * coinHeight),
                    Quaternion.Euler(0f, 19f * (layer + slot), 0f));
            }
            coins.Add(new Entry(visual, weightSum));
            weightSum += weight;
            return true;
        }

        /// <summary>Removes the most recently added coin.</summary>
        public bool TryRemoveCoin()
        {
            if (Count == 0)
                return false;

            Entry entry = coins[Count - 1];
            if (entry.Visual)
            {
                entry.Visual.SetActive(false);
                Destroy(entry.Visual);
            }

            coins.RemoveAt(Count - 1);
            weightSum = entry.PreviousWeight;
            return true;
        }

        public void Clear()
        {
            while (TryRemoveCoin()) { }
        }
    }
}