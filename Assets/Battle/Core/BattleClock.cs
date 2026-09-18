using System.Collections;
using UnityEngine;

public sealed class BattleClock
{
    public bool Paused { get; set; }
    public float Delta => Paused ? 0f : Time.deltaTime;
    public IEnumerator Wait(float seconds)
    {
        for (float elapsed = 0; elapsed < seconds; elapsed += Delta)
            yield return null;
    }
}
