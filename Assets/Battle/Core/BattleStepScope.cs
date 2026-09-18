using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class BattleStepScope : IDisposable
{
    public Transform Root { get; }
    readonly List<Action> cleanup = new List<Action>();
    bool disposed;
    public BattleStepScope(string name, Transform parent)
    {
        Root = new GameObject(name).transform;
        Root.SetParent(parent, false);
    }
    public void OnDispose(Action action) => cleanup.Add(action);
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        for (int i = cleanup.Count - 1; i >= 0; i--)
        {
            try { cleanup[i](); }
            catch (Exception exception) { Debug.LogException(exception); }
        }
        cleanup.Clear();
        if (Root != null)
        {
            Root.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(Root.gameObject);
        }
    }
}
