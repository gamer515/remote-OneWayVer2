using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Random = UnityEngine.Random;

public sealed class BettingResult
{
    public int[] CoinCounts { get; }
    public int TotalCoins { get; }

    public BettingResult(int[] coinCounts, int totalCoins)
    {
        CoinCounts = coinCounts;
        TotalCoins = totalCoins;
    }
}

[Serializable]
public sealed class ChapterCoinMaterialSet
{
    public string chapterId;
    public Material[] materials;
}

/// <summary>
/// Blue 버튼으로 코인을 생성하고 Red 버튼으로 현재 베팅 코인을 회수합니다.
/// 지문 가중치와 능력치 계산은 BettingCompleted를 구독하는 상위 흐름에서 처리합니다.
/// </summary>
public sealed class CoinDropController : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private BettingButtonController buttonController;
    [SerializeField] private JoystickLikeGear gearController;

    [Header("Coin Prefabs (Gear Slot 0~3)")]
    [SerializeField] private GameObject[] coinPrefabs;
    [Tooltip("챕터별 네 능력치 코인 Material입니다.")]
    [SerializeField] private ChapterCoinMaterialSet[] chapterMaterialSets;
    [SerializeField] private Transform[] exits;
    [SerializeField] private Transform coinExit;

    [Header("Betting Rules")]
    [Tooltip("한 번의 선택에서 Betting Board에 놓을 수 있는 전체 코인 수입니다.")]
    [SerializeField, Min(1)] private int maxBettingCoins = 5;
    [Tooltip("새 에피소드에서 능력치 종류별로 지급할 코인 수입니다.")]
    [SerializeField, Min(1)] private int coinsPerType = 5;

    [Header("Inventory Display (Coin_01~04)")]
    [SerializeField] private TextMeshProUGUI[] inventoryCountTexts;

    [Header("Drop Motion")]
    [SerializeField] private float downwardImpulse = 0.5f;
    [SerializeField] private float lateralImpulse = 0.25f;
    [SerializeField] private float angularImpulse = 3f;
    [Tooltip("베팅 중인 코인이 이 월드 Y 좌표 아래로 떨어지면 입구에서 다시 떨어뜨립니다.")]
    [SerializeField] private float recoveryHeight = -20f;

    [Header("Coin Collection")]
    [SerializeField] private float collectDuration = 0.35f;
    [SerializeField] private float intervalBetweenCoins = 0.08f;

    public event Action<BettingResult> BettingCompleted;
    public event Action BettingCollectionStarted;

    private readonly List<BettingCoin> spawnedCoins = new List<BettingCoin>();
    private int[] remainingCoins = new int[4];
    private Material[] activeCoinMaterials;
    private bool isCollecting;

    public int[] RemainingCoins => (int[])remainingCoins.Clone();

    public void InitializeInventory(int[] savedInventory)
    {
        if (savedInventory != null && savedInventory.Length == remainingCoins.Length)
            remainingCoins = (int[])savedInventory.Clone();
        else
            ResetInventory();

        RefreshInventoryDisplay();
    }

    public void ResetInventory()
    {
        for (int i = 0; i < remainingCoins.Length; i++)
            remainingCoins[i] = coinsPerType;

        RefreshInventoryDisplay();
    }

    public void SetChapterMaterials(string chapterId)
    {
        activeCoinMaterials = null;
        if (chapterMaterialSets == null || string.IsNullOrWhiteSpace(chapterId))
            return;

        for (int i = 0; i < chapterMaterialSets.Length; i++)
        {
            ChapterCoinMaterialSet set = chapterMaterialSets[i];
            if (set != null && string.Equals(
                    set.chapterId,
                    chapterId,
                    StringComparison.OrdinalIgnoreCase))
            {
                activeCoinMaterials = set.materials;
                return;
            }
        }
    }

    private void Awake()
    {
        if (buttonController == null)
            buttonController = FindFirstObjectByType<BettingButtonController>();
        if (gearController == null)
            gearController = FindFirstObjectByType<JoystickLikeGear>();

        ResolveExits();
        ResolveCoinExit();
        ResolveInventoryTexts();
        ResetInventory();
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

    private void Update()
    {
        if (isCollecting || exits == null || exits.Length == 0)
            return;

        for (int i = 0; i < spawnedCoins.Count; i++)
        {
            BettingCoin coin = spawnedCoins[i];
            if (coin != null && coin.transform.position.y <= recoveryHeight)
                DropFromRandomEntrance(coin);
        }
    }

    private void SpawnSelectedCoin()
    {
        if (isCollecting)
            return;

        // 파괴된 참조를 제외한 실제 베팅 코인 수를 기준으로 생성 한도를 검사합니다.
        spawnedCoins.RemoveAll(coin => coin == null);
        if (spawnedCoins.Count >= maxBettingCoins)
        {
            Debug.LogWarning($"코인은 한 번에 최대 {maxBettingCoins}개까지 베팅할 수 있습니다.", this);
            return;
        }

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
        if (coinTypeIndex >= remainingCoins.Length || remainingCoins[coinTypeIndex] <= 0)
        {
            Debug.LogWarning($"코인 종류 {coinTypeIndex}의 남은 재고가 없습니다.", this);
            return;
        }

        Transform exit = exits[Random.Range(0, exits.Length)];
        GameObject coin = Instantiate(coinPrefab, exit.position, Random.rotation);
        ApplyCoinMaterial(coin, coinTypeIndex);

        BettingCoin bettingCoin = coin.GetComponent<BettingCoin>();
        if (bettingCoin == null)
            bettingCoin = coin.AddComponent<BettingCoin>();
        bettingCoin.Initialize(coinTypeIndex);
        spawnedCoins.Add(bettingCoin);
        remainingCoins[coinTypeIndex]--;
        RefreshInventoryDisplay();

        ApplyDropMotion(bettingCoin);
    }

    /// <summary>
    /// 테이블 밖으로 떨어진 코인을 재고 차감 없이 임의의 입구로 되돌립니다.
    /// 최초 생성과 같은 힘과 회전을 적용하므로 다시 떨어지는 모션도 동일합니다.
    /// </summary>
    private void DropFromRandomEntrance(BettingCoin coin)
    {
        Transform entrance = exits[Random.Range(0, exits.Length)];
        coin.transform.SetPositionAndRotation(entrance.position, Random.rotation);
        ApplyDropMotion(coin);
    }

    private void ApplyDropMotion(BettingCoin bettingCoin)
    {
        if (bettingCoin == null)
            return;

        Rigidbody body = bettingCoin.GetComponent<Rigidbody>();
        if (body == null)
            body = bettingCoin.gameObject.AddComponent<Rigidbody>();

        body.isKinematic = false;
        body.useGravity = true;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
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

        BettingResult result = CreateBettingResult();

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

    private BettingResult CreateBettingResult()
    {
        int[] coinCounts = new int[4];
        for (int i = 0; i < spawnedCoins.Count; i++)
        {
            BettingCoin coin = spawnedCoins[i];
            if (coin != null && coin.CoinTypeIndex >= 0 && coin.CoinTypeIndex < coinCounts.Length)
                coinCounts[coin.CoinTypeIndex]++;
        }

        return new BettingResult(coinCounts, spawnedCoins.Count);
    }

    private GameObject GetCoinPrefab(int coinTypeIndex)
    {
        if (coinPrefabs == null || coinPrefabs.Length == 0)
            return null;

        if (coinTypeIndex < coinPrefabs.Length && coinPrefabs[coinTypeIndex] != null)
            return coinPrefabs[coinTypeIndex];

        return coinPrefabs[0];
    }

    private void ApplyCoinMaterial(GameObject coin, int coinTypeIndex)
    {
        if (coin == null || activeCoinMaterials == null ||
            coinTypeIndex < 0 || coinTypeIndex >= activeCoinMaterials.Length ||
            activeCoinMaterials[coinTypeIndex] == null)
        {
            return;
        }

        Renderer[] renderers = coin.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].material = activeCoinMaterials[coinTypeIndex];
    }

    private void ResolveInventoryTexts()
    {
        if (inventoryCountTexts != null && inventoryCountTexts.Length == 4)
            return;

        inventoryCountTexts = new TextMeshProUGUI[4];
        TextMeshProUGUI[] texts = FindObjectsByType<TextMeshProUGUI>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < texts.Length; i++)
        {
            for (int coinIndex = 0; coinIndex < inventoryCountTexts.Length; coinIndex++)
            {
                if (texts[i].gameObject.name == $"Coin_{coinIndex + 1:D2}")
                    inventoryCountTexts[coinIndex] = texts[i];
            }
        }
    }

    private void RefreshInventoryDisplay()
    {
        if (inventoryCountTexts == null)
            return;

        int count = Mathf.Min(inventoryCountTexts.Length, remainingCoins.Length);
        for (int i = 0; i < count; i++)
        {
            if (inventoryCountTexts[i] != null)
                inventoryCountTexts[i].text = remainingCoins[i].ToString();
        }
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

    /// <summary>
    /// Betting Board 자체가 연결되어 있어도 그 안의 실제 회수 지점인 Exit_Point를 사용합니다.
    /// 기존 Scene의 Inspector 연결을 다시 하지 않아도 되도록 자식까지 검색합니다.
    /// </summary>
    private void ResolveCoinExit()
    {
        if (coinExit == null || coinExit.name == "Exit_Point")
            return;

        Transform exitPoint = FindDescendant(coinExit, "Exit_Point");
        if (exitPoint != null)
            coinExit = exitPoint;
    }

    private static Transform FindDescendant(Transform parent, string objectName)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == objectName)
                return child;

            Transform found = FindDescendant(child, objectName);
            if (found != null)
                return found;
        }

        return null;
    }
}
