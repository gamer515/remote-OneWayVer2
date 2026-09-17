using System;
using System.Collections.Generic;
using JourneyMapKit;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// JourneyCoinSupplyRack 클릭을 실제 재고와 연결합니다.
/// 이야기/베팅 규칙은 DecisionManager에 남기고, 이 클래스는 공급과 물리 생성만 담당합니다.
/// </summary>
public sealed class JourneyCoinSupplyController
{
    private readonly JourneyBoardReferences board;
    private readonly Camera inputCamera;
    private readonly GameObject[] coinPrefabs;
    private readonly JourneyCoinStack[] stacks = new JourneyCoinStack[4];
    private readonly List<BettingCoin> activeCoins = new List<BettingCoin>();
    private readonly CoinDropController inventory;
    private readonly Action<int[]> saveInventory;
    private readonly int maxCoinsOnBoard;
    private readonly Transform coinContainer;
    private static readonly string[] CoinNames = { "Health", "Speed", "Intelligence", "Charm" };

    public JourneyCoinSupplyController(
        JourneyBoardInput boardInput,
        GameObject[] coinPrefabs,
        CoinDropController inventory,
        Action<int[]> saveInventory,
        int maxCoinsOnBoard = 5)
    {
        board = boardInput != null ? boardInput.board : null;
        inputCamera = boardInput != null && boardInput.inputCamera != null
            ? boardInput.inputCamera : Camera.main;
        this.coinPrefabs = coinPrefabs;
        this.inventory = inventory;
        this.saveInventory = saveInventory;
        this.maxCoinsOnBoard = maxCoinsOnBoard;
        if (board != null)
        {
            Transform existing = board.transform.Find("JourneyActiveCoins");
            coinContainer = existing != null ? existing :
                new GameObject("JourneyActiveCoins").transform;
            coinContainer.SetParent(board.transform, true);
        }

        foreach (JourneyCoinStack stack in UnityEngine.Object.FindObjectsByType<JourneyCoinStack>(
            FindObjectsSortMode.None))
        {
            if (stack.name.StartsWith("Supply_", StringComparison.Ordinal) &&
                int.TryParse(stack.name.Substring("Supply_".Length), out int number) &&
                number >= 1 && number <= stacks.Length)
                stacks[number - 1] = stack;
        }
    }

    public bool IsReady => board != null && board.chuteEntry != null &&
        board.chuteExit != null && inputCamera != null && inventory != null &&
        coinPrefabs != null && coinPrefabs.Length == 4 &&
        Array.TrueForAll(stacks, stack => stack != null);

    public void RefreshDisplay()
    {
        if (inventory == null) return;
        int[] counts = inventory.RemainingCoins;
        for (int index = 0; index < stacks.Length; index++)
            if (stacks[index] != null) stacks[index].SetCount(counts[index]);
    }

    public void ResetBoard()
    {
        foreach (BettingCoin coin in activeCoins)
            if (coin != null) UnityEngine.Object.Destroy(coin.gameObject);
        activeCoins.Clear();
        RefreshDisplay();
    }

    public void HandleInput()
    {
        if (!IsReady) return;
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
        Vector2 pointerPosition = Mouse.current.position.ReadValue();
#else
        if (!Input.GetMouseButtonDown(0)) return;
        Vector2 pointerPosition = Input.mousePosition;
#endif

        Ray ray = inputCamera.ScreenPointToRay(pointerPosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, 1000f, inputCamera.cullingMask);
        JourneyCoinStack selected = null;
        float nearestDistance = float.MaxValue;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.name != "Supply_Button" || hit.distance >= nearestDistance) continue;
            JourneyCoinStack stack = hit.collider.GetComponentInParent<JourneyCoinStack>();
            if (stack == null) continue;
            selected = stack;
            nearestDistance = hit.distance;
        }

        if (selected == null) return;
        for (int index = 0; index < stacks.Length; index++)
            if (stacks[index] == selected) { Dispense(index); return; }
    }

    private void Dispense(int index)
    {
        activeCoins.RemoveAll(coin => coin == null);
        if (activeCoins.Count >= maxCoinsOnBoard)
        {
            Debug.LogWarning($"Journey 보드에는 코인을 최대 {maxCoinsOnBoard}개까지 둘 수 있습니다.");
            return;
        }
        if (coinPrefabs[index] == null)
        {
            Debug.LogError($"Journey 코인 종류 {index}의 프리팹이 비어 있습니다.");
            return;
        }
        if (!inventory.TryConsumeCoin(index))
        {
            Debug.Log($"Journey 코인 종류 {index}의 재고가 없습니다.");
            return;
        }

        // 경사로 윗부분에서 중력으로 내려가게 합니다. 보드까지의 실제 충돌 동작은 프리팹 Collider가 결정합니다.
        Vector3 spawn = Vector3.Lerp(board.chuteEntry.position, board.chuteExit.position, 0.22f)
            + Vector3.up * 0.18f;
        GameObject coinObject = UnityEngine.Object.Instantiate(
            coinPrefabs[index], spawn, Quaternion.identity, coinContainer);
        coinObject.name = CoinNames[index];
        SetLayerRecursively(coinObject, board.gameObject.layer);
        BettingCoin coin = coinObject.GetComponent<BettingCoin>() ?? coinObject.AddComponent<BettingCoin>();
        coin.Initialize(index);
        activeCoins.Add(coin);
        RefreshDisplay();
        saveInventory?.Invoke(inventory.RemainingCoins);
        Debug.Log($"[Journey 코인] {CoinNames[index]} 공급, 남은 수량 {inventory.RemainingCoins[index]}");
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        target.layer = layer;
        foreach (Transform child in target.transform)
            SetLayerRecursively(child.gameObject, layer);
    }
}
