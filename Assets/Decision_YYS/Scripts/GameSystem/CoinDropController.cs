using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public sealed class BettingResult
{
    public int[] CoinCounts { get; }
    public int TotalCoins { get; }
    public int WinningCoinIndex { get; }

    public BettingResult(int[] coinCounts, int totalCoins, int winningCoinIndex)
    {
        CoinCounts = coinCounts;
        TotalCoins = totalCoins;
        WinningCoinIndex = winningCoinIndex;
    }
}

/// <summary>
/// Blue 버튼으로 코인을 생성하고 Red 버튼으로 현재 베팅 코인을 회수합니다.
/// 최종 선택지 및 스탯 계산은 BettingCompleted를 구독하는 상위 흐름에서 처리합니다.
/// </summary>
public sealed class CoinDropController : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private BettingButtonController buttonController;
    [SerializeField] private JoystickLikeGear gearController;

    [Header("Coin Prefabs (Gear Slot 0~3)")]
    [SerializeField] private GameObject[] coinPrefabs;
    [SerializeField] private Transform[] exits;
    [SerializeField] private Transform coinExit;

    [Header("Drop Motion")]
    [SerializeField] private float downwardImpulse = 0.5f;
    [SerializeField] private float lateralImpulse = 0.25f;
    [SerializeField] private float angularImpulse = 3f;

    [Header("Coin Collection")]
    [SerializeField] private float collectDuration = 0.35f;
    [SerializeField] private float intervalBetweenCoins = 0.08f;

    public event Action<BettingResult> BettingCompleted;
    public event Action BettingCollectionStarted;

    private readonly List<BettingCoin> spawnedCoins = new List<BettingCoin>();
    private bool isCollecting;

    private void Awake()
    {
        if (buttonController == null)
            buttonController = FindFirstObjectByType<BettingButtonController>();
        if (gearController == null)
            gearController = FindFirstObjectByType<JoystickLikeGear>();

        ResolveExits();
    }

    private void OnEnable()
    {
        if (buttonController == null)
            return;

        buttonController.BluePressed += SpawnSelectedCoin;
        buttonController.RedPressed += ConfirmBetting;
    }

    private void OnDisable()
    {
        if (buttonController == null)
            return;

        buttonController.BluePressed -= SpawnSelectedCoin;
        buttonController.RedPressed -= ConfirmBetting;
    }

    private void SpawnSelectedCoin()
    {
        if (isCollecting)
            return;
        if (gearController == null || exits == null || exits.Length == 0)
        {
            Debug.LogError("코인을 생성할 기어 또는 출구 참조가 없습니다.", this);
            return;
        }

        int coinTypeIndex = gearController.SelectedCoinIndex;
        if (coinTypeIndex < 0)
        {
            Debug.LogWarning("기어로 코인 종류를 먼저 선택해야 합니다.", this);
            return;
        }

        GameObject coinPrefab = GetCoinPrefab(coinTypeIndex);
        if (coinPrefab == null)
        {
            Debug.LogError($"기어 {coinTypeIndex}에 사용할 코인 프리팹이 없습니다.", this);
            return;
        }

        Transform exit = exits[Random.Range(0, exits.Length)];
        GameObject coin = Instantiate(coinPrefab, exit.position, Random.rotation);

        BettingCoin bettingCoin = coin.GetComponent<BettingCoin>();
        if (bettingCoin == null)
            bettingCoin = coin.AddComponent<BettingCoin>();
        bettingCoin.Initialize(coinTypeIndex);
        spawnedCoins.Add(bettingCoin);

        Rigidbody body = coin.GetComponent<Rigidbody>();
        if (body == null)
            body = coin.AddComponent<Rigidbody>();

        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        Vector3 randomImpulse = new Vector3(
            Random.Range(-lateralImpulse, lateralImpulse),
            -downwardImpulse,
            Random.Range(-lateralImpulse, lateralImpulse));
        body.AddForce(randomImpulse, ForceMode.Impulse);
        body.AddTorque(Random.insideUnitSphere * angularImpulse, ForceMode.Impulse);
    }

    private void ConfirmBetting()
    {
        spawnedCoins.RemoveAll(coin => coin == null);

        if (isCollecting)
            return;
        if (spawnedCoins.Count == 0)
        {
            Debug.LogWarning("확정할 코인이 없습니다. Blue 버튼으로 코인을 먼저 추가하세요.", this);
            return;
        }
        if (coinExit == null)
        {
            Debug.LogError("Coin_Exit 참조가 없어 베팅을 확정할 수 없습니다.", this);
            return;
        }

        if (!TryCreateBettingResult(out BettingResult result))
        {
            Debug.LogWarning("가장 많은 코인이 동률입니다. Blue 버튼으로 한 종류를 더 베팅하세요.", this);
            return;
        }

        isCollecting = true;
        buttonController?.SetBettingInteractable(false);
        // 잡고 있는 코인이 있다면 물리 상태로 놓은 후 회수를 시작합니다.
        BettingCollectionStarted?.Invoke();
        StartCoroutine(CollectCoins(result));
    }

    private IEnumerator CollectCoins(BettingResult result)
    {
        for (int i = 0; i < spawnedCoins.Count; i++)
        {
            BettingCoin coin = spawnedCoins[i];
            if (coin == null)
                continue;

            yield return coin.MoveToExit(coinExit, collectDuration);
            Destroy(coin.gameObject);

            if (intervalBetweenCoins > 0f)
                yield return new WaitForSeconds(intervalBetweenCoins);
        }

        spawnedCoins.Clear();
        isCollecting = false;

        BettingCompleted?.Invoke(result);
    }

    private bool TryCreateBettingResult(out BettingResult result)
    {
        int[] coinCounts = new int[4];
        for (int i = 0; i < spawnedCoins.Count; i++)
        {
            BettingCoin coin = spawnedCoins[i];
            if (coin != null && coin.CoinTypeIndex >= 0 && coin.CoinTypeIndex < coinCounts.Length)
                coinCounts[coin.CoinTypeIndex]++;
        }

        int winnerIndex = -1;
        int highestCount = 0;
        bool hasTie = false;

        for (int i = 0; i < coinCounts.Length; i++)
        {
            if (coinCounts[i] > highestCount)
            {
                highestCount = coinCounts[i];
                winnerIndex = i;
                hasTie = false;
            }
            else if (coinCounts[i] == highestCount && highestCount > 0)
            {
                hasTie = true;
            }
        }

        result = hasTie || winnerIndex < 0
            ? null
            : new BettingResult(coinCounts, spawnedCoins.Count, winnerIndex);
        return result != null;
    }

    private GameObject GetCoinPrefab(int coinTypeIndex)
    {
        if (coinPrefabs == null || coinPrefabs.Length == 0)
            return null;

        if (coinTypeIndex < coinPrefabs.Length && coinPrefabs[coinTypeIndex] != null)
            return coinPrefabs[coinTypeIndex];

        return coinPrefabs[0];
    }

    private void ResolveExits()
    {
        if (exits != null && exits.Length > 0)
            return;

        List<Transform> foundExits = new List<Transform>();
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child.name.StartsWith("Coin_Entrance_"))
                foundExits.Add(child);
        }

        foundExits.Sort((left, right) => string.CompareOrdinal(left.name, right.name));
        exits = foundExits.ToArray();
    }
}
