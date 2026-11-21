using System.Collections.Generic;
using UnityEngine;

public sealed class TaxScaleView : MonoBehaviour
{
    [SerializeField] private Transform beam;
    [SerializeField] private Transform noticePan;
    [SerializeField] private Transform playerPan;
    [SerializeField] private Transform bowl;

    internal const float StackSpacing = .089f;
    private readonly List<GameObject> coins = new List<GameObject>();
    private GameObject[] coinPrefabs;
    private Quaternion beamRest;
    private Vector3 noticeRest, playerRest;
    private Transform stackBase, playerStack;
    private Renderer panCup, playerCup;
    private string noticeText;
    private float targetTilt, tilt;
    private int playerCount;

    internal Transform CollectionBowl => bowl;
    internal Vector3 PlayerStackPosition => playerStack.position;
    internal int PlayerCapacity => playerPan.GetComponent<CounterBalanceScale.CoinTray>().Capacity;

    internal void BindCoinPrefabs(GameObject[] prefabs) => coinPrefabs = prefabs;

    private void Awake()
    {
        beamRest = beam.localRotation;
        noticeRest = noticePan.localPosition;
        playerRest = playerPan.localPosition;
        panCup = noticePan.Find("Brass cup").GetComponent<Renderer>();
        stackBase = noticePan.Find("Coin stack bases/Coin Stack");
        playerStack = playerPan.Find("Coin stack bases/Coin Stack");
        playerCup = playerPan.Find("Brass cup").GetComponent<Renderer>();
        // The pans use pointer tests, so they do not intercept other board physics.
        foreach (var collider in beam.parent.GetComponentsInChildren<Collider>())
            collider.enabled = false;
        foreach (var collider in bowl.GetComponentsInChildren<Collider>())
            collider.enabled = false;
    }

    internal void ShowNotice(int[] cost, int unassigned, string text)
    {
        ClearNotice();
        noticeText = text;
        for (int level = 0; level < unassigned; level++)
            AddCoin(coinPrefabs[0], unassigned: true);
        for (int ability = 0; ability < cost.Length; ability++)
            for (int level = 0; level < cost[ability]; level++)
                AddCoin(coinPrefabs[ability]);
        UpdateTilt();
    }

    private void AddCoin(GameObject prefab, bool unassigned = false)
    {
        int level = coins.Count;
        var coin = Instantiate(prefab, stackBase.position + stackBase.up * (level * StackSpacing),
            stackBase.rotation * Quaternion.Euler(0f, level * 19f, 0f));
        coin.transform.SetParent(stackBase, true);
        // Use the same world scale as a dispensed coin, with no board physics.
        foreach (var collider in coin.GetComponentsInChildren<Collider>())
            collider.enabled = false;
        foreach (var body in coin.GetComponentsInChildren<Rigidbody>())
        {
            body.detectCollisions = false;
            Destroy(body);
        }
        if (unassigned)
            coin.transform.Find("Face").GetComponent<Renderer>().sharedMaterial = coin.GetComponent<Renderer>().sharedMaterial;
        coins.Add(coin);
    }

    internal void ClearNotice()
    {
        noticeText = null;
        foreach (var coin in coins)
        {
            coin.SetActive(false);
            Destroy(coin);
        }
        coins.Clear();
        UpdateTilt();
    }

    internal bool HitsPlayerPan(Ray ray)
    {
        var bounds = playerCup.bounds;
        if (playerCount > 0)
            bounds.Encapsulate(playerStack.position + playerStack.up * (StackSpacing * playerCount));
        return bounds.IntersectRay(ray);
    }

    internal void PlacePlayerCoin(Transform coin, int level)
    {
        coin.SetParent(playerStack, true);
        coin.SetPositionAndRotation(playerStack.position + playerStack.up * (level * StackSpacing),
            playerStack.rotation * Quaternion.Euler(0f, level * 19f, 0f));
    }

    internal void SetPlayerCount(int count)
    {
        playerCount = count;
        UpdateTilt();
    }

    private void UpdateTilt() => targetTilt = Mathf.Clamp((coins.Count - playerCount) * 4f, -8f, 8f);

    private void Update()
    {
        if (tilt == targetTilt) return;
        tilt = Mathf.MoveTowards(tilt, targetTilt, Time.unscaledDeltaTime * 40f);
        var turn = Quaternion.AngleAxis(tilt, Vector3.forward);
        beam.localRotation = turn * beamRest;
        var pivot = beam.localPosition;
        // Move each suspension with the beam while keeping the trays level.
        noticePan.localPosition = pivot + turn * (noticeRest - pivot);
        playerPan.localPosition = pivot + turn * (playerRest - pivot);
    }

    internal string GetHover(Ray ray)
    {
        if (noticeText == null) return null;
        var bounds = panCup.bounds;
        if (coins.Count > 0)
            bounds.Encapsulate(BoardItemDrop.ItemBounds(coins[coins.Count - 1].transform));
        return bounds.IntersectRay(ray) ? noticeText : null;
    }
}
