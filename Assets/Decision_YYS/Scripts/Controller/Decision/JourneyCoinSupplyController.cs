using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly JourneyBoardInput boardInput;
    private readonly GameObject[] coinPrefabs;
    private readonly JourneyCoinStack[] stacks = new JourneyCoinStack[4];
    private readonly Bounds[] supplyHoverBounds = new Bounds[4];
    private readonly List<BettingCoin> activeCoins = new List<BettingCoin>();
    private readonly CoinDropController inventory;
    private readonly Action<int[]> saveInventory;
    private readonly Transform coinContainer;
    private static readonly string[] CoinNames = { "Health", "Speed", "Intelligence", "Charm" };
    private readonly List<PaymentCoin> paymentCoins = new List<PaymentCoin>();
    private int[] paymentCost;
    private Collider paymentGrid;
    private PaymentCoin draggedPaymentCoin;
    private float coinGrabY;
    private bool absorbingPayment;
    public Collider WhiteBoard { get; set; }
    public Func<Vector2, int, Transform> RefillSlot { get; set; }
    public Func<int, int, bool> RefillBooks { get; set; }
    public MonoBehaviour DropRunner { get; set; }
    public bool HasBoardPayment => paymentCost != null;
    public bool CanStageCoins => IsReady && board.trayFloor != null && paymentGrid != null &&
        board.supplyButtons != null && board.supplyButtons.Length >= 4;
    public void SetBoardGrid(Collider grid) => paymentGrid = grid;

    private sealed class PaymentCoin
    {
        public GameObject model;
        public Rigidbody body;
        public int type;
        public bool onGrid;
        public bool landing;
        public int refillSlot = -1;
    }

    public JourneyCoinSupplyController(
        JourneyBoardInput boardInput,
        GameObject[] coinPrefabs,
        CoinDropController inventory,
        Action<int[]> saveInventory)
    {
        this.boardInput = boardInput;
        board = boardInput != null ? boardInput.board : null;
        inputCamera = boardInput != null && boardInput.inputCamera != null
            ? boardInput.inputCamera : Camera.main;
        this.coinPrefabs = coinPrefabs;
        this.inventory = inventory;
        this.saveInventory = saveInventory;
        if (board != null)
        {
            Transform existing = board.transform.Find("JourneyActiveCoins");
            coinContainer = existing != null ? existing :
                new GameObject("JourneyActiveCoins").transform;
            coinContainer.SetParent(board.transform, true);
        }

        if (board != null && board.supplyStacks != null && board.supplyStacks.Length == stacks.Length)
        {
            Array.Copy(board.supplyStacks, stacks, stacks.Length);
        }
        else foreach (JourneyCoinStack stack in UnityEngine.Object.FindObjectsByType<JourneyCoinStack>(
            FindObjectsSortMode.None))
        {
            if (stack.name.StartsWith("Supply_", StringComparison.Ordinal) &&
                int.TryParse(stack.name.Substring("Supply_".Length), out int number) &&
                number >= 1 && number <= stacks.Length)
                stacks[number - 1] = stack;
        }
        for (int i = 0; i < stacks.Length; i++)
            if (stacks[i] != null) supplyHoverBounds[i] = BoardItemDrop.ItemBounds(stacks[i].transform);
    }

    public bool IsReady => board != null && board.chuteEntry != null &&
        board.chuteExit != null && inputCamera != null && inventory != null &&
        coinPrefabs != null && coinPrefabs.Length == 4 &&
        Array.TrueForAll(stacks, stack => stack != null);

    public void RefreshDisplay()
    {
        if (inventory == null) return;
        int[] counts = AvailableCoinCounts();
        for (int index = 0; index < stacks.Length; index++)
            if (stacks[index] != null) stacks[index].SetCount(counts[index]);
    }

    public int AvailableCoins(int index) =>
        Mathf.Max(0, inventory.RemainingCoins[index] - paymentCoins.Count(coin => coin.type == index));

    public int[] AvailableCoinCounts()
    {
        int[] counts = inventory.RemainingCoins;
        foreach (var coin in paymentCoins)
            counts[coin.type] = Mathf.Max(0, counts[coin.type] - 1);
        return counts;
    }

    public int GetHoveredSupplyIndex(Vector2 point)
    {
        if (!IsReady || !boardInput.TryCreateRay(point, out var ray)) return -1;
        return FindSupplyTarget(ray);
    }

    private int FindSupplyTarget(Ray ray)
    {
        int button = FindSupplyButton(ray);
        if (button >= 0) return button;
        int nearest = -1;
        float distance = float.PositiveInfinity;
        for (int i = 0; i < stacks.Length; i++)
        {
            if (!supplyHoverBounds[i].IntersectRay(ray, out float hit) || hit >= distance) continue;
            nearest = i;
            distance = hit;
        }
        return nearest;
    }

    public void ResetBoard()
    {
        ClearBoardPayment();
        foreach (BettingCoin coin in activeCoins)
            if (coin != null) UnityEngine.Object.Destroy(coin.gameObject);
        activeCoins.Clear();
        RefreshDisplay();
    }

    public void HandleInput()
    {
        int index = GetClickedSupplyIndex();
        if (index >= 0) Dispense(index);
    }

    public int GetClickedSupplyIndex()
    {
        if (!IsReady || (boardInput != null && !boardInput.InputEnabled)) return -1;
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return -1;
        Vector2 pointerPosition = Mouse.current.position.ReadValue();
#else
        if (!Input.GetMouseButtonDown(0)) return -1;
        Vector2 pointerPosition = Input.mousePosition;
#endif

        Ray ray;
        if (boardInput != null)
        {
            if (!boardInput.TryCreateRay(pointerPosition, out ray)) return -1;
        }
        else ray = inputCamera.ScreenPointToRay(pointerPosition);
        return FindSupplyButton(ray);
    }

    private int FindSupplyButton(Ray ray)
    {
        RaycastHit[] hits = Physics.RaycastAll(ray, 1000f, inputCamera.cullingMask);
        JourneyCoinStack selected = null;
        float nearestDistance = float.MaxValue;
        foreach (RaycastHit hit in hits)
        {
            if (hit.distance >= nearestDistance) continue;
            int boundIndex = board.supplyButtons != null
                ? Array.IndexOf(board.supplyButtons, hit.collider) : -1;
            if (boundIndex < 0 && hit.collider.name != "Supply_Button") continue;
            JourneyCoinStack stack = boundIndex >= 0 && boundIndex < stacks.Length
                ? stacks[boundIndex] : hit.collider.GetComponentInParent<JourneyCoinStack>();
            if (stack == null) continue;
            selected = stack;
            nearestDistance = hit.distance;
        }

        if (selected == null) return -1;
        for (int index = 0; index < stacks.Length; index++)
            if (stacks[index] == selected) return index;
        return -1;
    }

    public bool BeginBoardPayment(int[] cost, Collider grid)
    {
        if (!IsReady || board.trayFloor == null || grid == null ||
            board.supplyButtons == null || board.supplyButtons.Length < 4 ||
            !AbilityPayment.IsValid(cost) || cost == null || !Array.Exists(cost, amount => amount > 0))
            return false;
        paymentCost = (int[])cost.Clone();
        paymentGrid = grid;
        if (draggedPaymentCoin != null)
        {
            draggedPaymentCoin.onGrid = false;
            draggedPaymentCoin.refillSlot = -1;
            DropOnBoard(draggedPaymentCoin, WhiteBoard ?? board.trayFloor,
                TrayPosition(draggedPaymentCoin.type, paymentCoins.IndexOf(draggedPaymentCoin)), .20f);
        }
        draggedPaymentCoin = null;
        var retained = new int[4];
        for (int i = 0; i < paymentCoins.Count;)
        {
            var coin = paymentCoins[i];
            if (retained[coin.type] < cost[coin.type])
            {
                retained[coin.type]++;
                i++;
                continue;
            }
            RemovePaymentCoin(coin);
            paymentCoins.RemoveAt(i);
        }
        RefreshDisplay();
        return true;
    }

    public void HandleBoardPaymentInput(Action ready, Action cancelled)
    {
        if (!CanStageCoins || absorbingPayment) return;
        if (HasBoardPayment && draggedPaymentCoin == null && PaymentCoinCountMatches())
        {
            ready();
            return;
        }
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
        {
            if (HasBoardPayment) cancelled(); else ClearBoardPayment();
            return;
        }
        var mouse = Mouse.current;
        if (mouse == null) return;
        Vector2 point = mouse.position.ReadValue();
        bool down = mouse.leftButton.wasPressedThisFrame, held = mouse.leftButton.isPressed, up = mouse.leftButton.wasReleasedThisFrame;
#else
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (HasBoardPayment) cancelled(); else ClearBoardPayment();
            return;
        }
        Vector2 point = Input.mousePosition;
        bool down = Input.GetMouseButtonDown(0), held = Input.GetMouseButton(0), up = Input.GetMouseButtonUp(0);
#endif
        HandleBoardPaymentPointer(point, down, held, up, ready);
    }

    private void HandleBoardPaymentPointer(Vector2 point, bool down, bool held, bool up, Action ready)
    {
        if (!HasBoardPayment && draggedPaymentCoin != null &&
            RefillSlot?.Invoke(point, draggedPaymentCoin.type) is Transform refill)
        {
            InsertRefillCoin(refill, up);
            return;
        }
        if (!boardInput.TryCreateRay(point, out var ray))
        {
            if (up && draggedPaymentCoin != null)
            {
                draggedPaymentCoin.onGrid = false;
                draggedPaymentCoin.refillSlot = -1;
                DropOnBoard(draggedPaymentCoin, WhiteBoard ?? board.trayFloor,
                    TrayPosition(draggedPaymentCoin.type, paymentCoins.IndexOf(draggedPaymentCoin)), .20f);
                draggedPaymentCoin = null;
            }
            return;
        }
        if (down)
        {
            float closest = float.PositiveInfinity;
            foreach (var coin in paymentCoins)
            {
                if (coin.landing) continue;
                var bounds = BoardItemDrop.ItemBounds(coin.model.transform);
                if (!bounds.IntersectRay(ray, out float distance) || distance >= closest) continue;
                closest = distance;
                draggedPaymentCoin = coin;
            }
            if (draggedPaymentCoin != null)
            {
                coinGrabY = point.y;
                BoardItemDrop.Hold(draggedPaymentCoin.body);
            }
            if (draggedPaymentCoin == null)
            {
                int index = FindSupplyButton(ray);
                if (index >= 0) DispensePaymentCoin(index);
            }
        }
        if (draggedPaymentCoin == null) return;
        if (FindSupplyTarget(ray) == draggedPaymentCoin.type)
        {
            var coin = draggedPaymentCoin;
            if (up)
            {
                draggedPaymentCoin = null;
                RemovePaymentCoin(coin);
                paymentCoins.Remove(coin);
                RefreshDisplay();
            }
            else if (held)
            {
                Bounds container = supplyHoverBounds[coin.type];
                coin.model.transform.position = new Vector3(container.center.x, container.max.y + .15f, container.center.z);
            }
            return;
        }
        var surface = BoardItemDrop.FindSurface(ray, inputCamera, point, draggedPaymentCoin.model.transform,
            WhiteBoard ?? board.trayFloor, paymentGrid, out var hit);
        float lift = BoardItemDrop.PointerLift(inputCamera, point, coinGrabY);
        if (held && surface != null)
            draggedPaymentCoin.model.transform.position =
                BoardItemDrop.PositionAbove(surface, draggedPaymentCoin.model.transform, hit, lift);
        if (!up) return;
        var item = draggedPaymentCoin;
        draggedPaymentCoin = null;
        item.refillSlot = -1;
        item.onGrid = surface == paymentGrid;
        DropOnBoard(item, surface, hit, lift);
    }

    private void InsertRefillCoin(Transform slot, bool released)
    {
        var coin = draggedPaymentCoin;
        Vector3 towardCamera = -inputCamera.transform.forward;
        Vector3 axis = new Vector3(Mathf.Abs(towardCamera.x), Mathf.Abs(towardCamera.y), Mathf.Abs(towardCamera.z));
        float front = slot.GetComponentsInChildren<Renderer>().Select(renderer =>
            Vector3.Dot(renderer.bounds.center - slot.position, towardCamera) +
            Vector3.Dot(renderer.bounds.extents, axis)).DefaultIfEmpty(0).Max();
        float clearance = Vector3.Dot(coin.model.GetComponentInChildren<Renderer>().bounds.extents, axis);
        coin.model.transform.position = slot.position + towardCamera * (front + clearance + .06f);
        if (!released) return;
        draggedPaymentCoin = null;
        coin.onGrid = false;
        coin.refillSlot = coin.type;
        var inserted = paymentCoins.FindAll(item => item.refillSlot == coin.type);
        coin.model.transform.position += towardCamera * (.07f * (inserted.Count - 1));
        if (RefillBooks?.Invoke(coin.type, inserted.Count) != true) return;
        foreach (var item in inserted)
        {
            RemovePaymentCoin(item);
            paymentCoins.Remove(item);
        }
        RefreshDisplay();
    }

    private void DropOnBoard(PaymentCoin item, Collider surface, Vector3 point, float lift)
    {
        var model = item.model.transform;
        model.position = BoardItemDrop.PositionAbove(surface, model, point, lift);
        if (DropRunner != null) DropRunner.StartCoroutine(DropCoin(item, paymentGrid));
        else BoardItemDrop.Release(item.body);
    }

    private bool PaymentCoinCountMatches()
    {
        var placed = new int[4];
        if (paymentCoins.Exists(coin => coin.onGrid && coin.landing)) return false;
        foreach (var coin in paymentCoins) if (coin.onGrid) placed[coin.type]++;
        for (int i = 0; i < placed.Length; i++) if (placed[i] != paymentCost[i]) return false;
        return true;
    }

    private Vector3 TrayPosition(int index, int number)
    {
        Bounds tray = board.trayFloor.bounds;
        float x = board.supplySpawns != null && index < board.supplySpawns.Length && board.supplySpawns[index] != null
            ? board.supplySpawns[index].position.x : tray.center.x;
        float z = Mathf.Clamp(tray.center.z + number % 5 * .23f, tray.min.z + .15f, tray.max.z - .15f);
        return new Vector3(Mathf.Clamp(x, tray.min.x + .15f, tray.max.x - .15f), tray.max.y + .09f, z);
    }

    private bool DispensePaymentCoin(int index)
    {
        if (coinPrefabs[index] == null || AvailableCoins(index) == 0 ||
            (HasBoardPayment && paymentCoins.Count(coin => coin.type == index) >= paymentCost[index]))
            return false;
        Vector3 trayPosition = TrayPosition(index, paymentCoins.Count);
        Vector3 outlet = board.supplySpawns != null && index < board.supplySpawns.Length && board.supplySpawns[index] != null
            ? board.supplySpawns[index].position : trayPosition + Vector3.up * .35f;
        var coin = UnityEngine.Object.Instantiate(coinPrefabs[index], outlet, Quaternion.identity, coinContainer);
        coin.name = "PaymentCoin_" + CoinNames[index];
        SetLayerRecursively(coin, board.gameObject.layer);
        var body = coin.GetComponent<Rigidbody>();
        body.constraints = RigidbodyConstraints.None;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        BoardItemDrop.Hold(body);
        var item = new PaymentCoin { model = coin, body = body, type = index };
        paymentCoins.Add(item);
        RefreshDisplay();
        DropOnBoard(item, WhiteBoard ?? board.trayFloor, trayPosition, Mathf.Max(.18f, outlet.y - trayPosition.y));
        return true;
    }

    private static System.Collections.IEnumerator DropCoin(PaymentCoin item, Collider grid)
    {
        item.landing = true;
        yield return BoardItemDrop.Play(item.body);
        if (item.model != null && item.onGrid)
            item.onGrid = BoardItemDrop.IsOnSurface(grid, item.model.transform);
        item.landing = false;
    }

    public System.Collections.IEnumerator AbsorbBoardPayment(Vector2 storyScreenPosition)
    {
        absorbingPayment = true;
        foreach (var coin in paymentCoins) BoardItemDrop.Hold(coin.body);
        var positions = paymentCoins.ConvertAll(coin => coin.model.transform.position);
        var scales = paymentCoins.ConvertAll(coin => coin.model.transform.localScale);
        float elapsed = 0;
        while (elapsed < .55f)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / .55f));
            for (int i = 0; i < paymentCoins.Count; i++)
            {
                Vector3 start = positions[i];
                Vector3 end = inputCamera.ScreenToWorldPoint(new Vector3(storyScreenPosition.x, storyScreenPosition.y,
                    inputCamera.WorldToScreenPoint(start).z));
                var coin = paymentCoins[i].model.transform;
                coin.position = Vector3.Lerp(start, end, t);
                coin.localScale = Vector3.Lerp(scales[i], Vector3.zero, t);
            }
            yield return null;
        }
        ClearBoardPayment();
    }

    public void ClearBoardPayment()
    {
        foreach (var coin in paymentCoins) RemovePaymentCoin(coin);
        paymentCoins.Clear();
        paymentCost = null;
        draggedPaymentCoin = null;
        absorbingPayment = false;
        RefreshDisplay();
    }

    private static void RemovePaymentCoin(PaymentCoin coin)
    {
        if (coin.model == null) return;
        coin.model.SetActive(false);
        UnityEngine.Object.Destroy(coin.model);
    }

    public System.Collections.IEnumerator AnimateStoryCoin(int index, bool adding)
    {
        if (!IsReady || index < 0 || index >= 4 || coinPrefabs[index] == null) yield break;
        Transform spawnPoint = board.supplySpawns != null && index < board.supplySpawns.Length
            ? board.supplySpawns[index] : null;
        Vector3 top = spawnPoint != null ? spawnPoint.position : stacks[index].transform.position + Vector3.up * .3f;
        // 회수는 코인통 출구에서 사진의 경사로 방향으로 빠지고, 반환은 통 위에서 내려옵니다.
        Vector3 outside = board.chuteExit.position;
        Vector3 start = adding ? top + Vector3.up * .7f : top;
        Vector3 end = adding ? top : outside;
        GameObject visual = UnityEngine.Object.Instantiate(coinPrefabs[index], start, Quaternion.identity, coinContainer);
        visual.name = "StoryCoin_" + CoinNames[index];
        SetLayerRecursively(visual, board.gameObject.layer);
        foreach (Collider collider in visual.GetComponentsInChildren<Collider>()) collider.enabled = false;
        foreach (Rigidbody body in visual.GetComponentsInChildren<Rigidbody>()) { body.isKinematic = true; body.useGravity = false; }
        float elapsed = 0f;
        while (elapsed < .14f && visual != null)
        {
            elapsed += Time.unscaledDeltaTime;
            visual.transform.position = Vector3.Lerp(start, end, Mathf.SmoothStep(0f, 1f, elapsed / .14f));
            yield return null;
        }
        if (visual != null) UnityEngine.Object.Destroy(visual);
    }

    private void Dispense(int index)
    {
        activeCoins.RemoveAll(coin => coin == null);
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

        // V3는 각 코인통 출구, 기존 보드는 왼쪽 경사로에서 배출합니다. 이후 이동은 물리로 처리합니다.
        Vector3 spawn = board.supplySpawns != null && index < board.supplySpawns.Length &&
            board.supplySpawns[index] != null ? board.supplySpawns[index].position :
            Vector3.Lerp(board.chuteEntry.position, board.chuteExit.position, 0.22f)
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
