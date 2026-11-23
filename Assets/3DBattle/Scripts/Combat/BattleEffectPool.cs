using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OneWay.Battle3D
{
    // Pools belong to one emitter and one scene; nothing survives a scene unload.
    public sealed class BattleEffectPool : IDisposable
    {
        public enum Kind { Particles, FlyingSlash, PhaseSlash, Smoke }
        public GameObject Prefab { get; }
        public int CreatedCount { get; private set; }
        public int ReusedCount { get; private set; }
        public int ActiveCount { get; private set; }
        readonly Kind kind;
        readonly int retainedLimit;
        readonly GameObject storage;
        readonly Stack<BattlePooledEffect> available;
        readonly List<BattlePooledEffect> instances;
        bool disposed;

        public BattleEffectPool(GameObject prefab, Transform sceneOwner, Kind kind, int prewarm, int retainedLimit)
        {
            Prefab = prefab; this.kind = kind; this.retainedLimit = Mathf.Max(prewarm, retainedLimit);
            available = new Stack<BattlePooledEffect>(this.retainedLimit);
            instances = new List<BattlePooledEffect>(this.retainedLimit);
            storage = new GameObject("Battle FX Pool - " + prefab.name);
            SceneManager.MoveGameObjectToScene(storage, sceneOwner.gameObject.scene);
            storage.SetActive(false);
            for (int i = 0; i < prewarm; i++) available.Push(Create());
        }

        BattlePooledEffect Create()
        {
            // The inactive parent prevents Play On Awake before configuration is complete.
            var instance = UnityEngine.Object.Instantiate(Prefab, storage.transform, false);
            instance.SetActive(false);
            var effect = instance.GetComponent<BattlePooledEffect>();
            if (!effect) effect = instance.AddComponent<BattlePooledEffect>();
            effect.Prepare(this, kind);
            instances.Add(effect); CreatedCount++; return effect;
        }

        public BattleEffectLease Rent(Vector3 position, Quaternion rotation)
        {
            if (disposed) throw new ObjectDisposedException(nameof(BattleEffectPool));
            BattlePooledEffect effect = null;
            while (available.Count > 0 && !effect) effect = available.Pop();
            if (effect) ReusedCount++; else effect = Create();
            effect.transform.SetParent(null, false);
            SceneManager.MoveGameObjectToScene(effect.gameObject, storage.scene);
            effect.transform.SetPositionAndRotation(position, rotation);
            effect.transform.localScale = Prefab.transform.localScale;
            effect.BeginLease(); ActiveCount++;
            return new BattleEffectLease(effect);
        }

        internal void Return(BattlePooledEffect effect)
        {
            if (disposed || !effect || !effect.IsRented) return;
            effect.EndLease(); ActiveCount--;
            if (!storage || available.Count >= retainedLimit)
            {
                instances.Remove(effect); UnityEngine.Object.Destroy(effect.gameObject); return;
            }
            effect.transform.SetParent(storage.transform, false);
            available.Push(effect);
        }

        public void ReleaseAll()
        {
            // Return may remove overflow entries, so walk backwards.
            for (int i = instances.Count - 1; i >= 0; i--)
                if (instances[i] && instances[i].IsRented) Return(instances[i]);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var instance in instances) if (instance)
            { instance.EndLease(); UnityEngine.Object.Destroy(instance.gameObject); }
            instances.Clear(); available.Clear(); ActiveCount = 0;
            if (storage) UnityEngine.Object.Destroy(storage);
        }
    }

    // A previous swing must never cancel a different swing that reused its object.
    public readonly struct BattleEffectLease
    {
        public readonly BattlePooledEffect Effect;
        readonly uint generation;
        internal BattleEffectLease(BattlePooledEffect effect) { Effect = effect; generation = effect.Generation; }
        public bool IsAlive => Effect && Effect.IsRented && Effect.Generation == generation;
        public void Release() { if (IsAlive) Effect.Release(); }
    }
}
