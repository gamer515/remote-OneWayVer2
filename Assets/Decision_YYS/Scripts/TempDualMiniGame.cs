using System;
using UnityEngine;

public enum miniGameState { Duel }
public class TempDualMiniGame : MonoBehaviour
{
    public Action<miniGameState> OnMiniGameStateChanged;
}
