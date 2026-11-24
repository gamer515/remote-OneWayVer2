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
public sealed partial class JourneyCoinSupplyController
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
    private static readonly Color32 IntelligenceMilestone = new Color32(255, 204, 0, 255);
    private readonly List<PaymentCoin> paymentCoins = new List<PaymentCoin>();
    private int[] paymentCost;
    private Collider paymentGrid;
    private Func<Ray, Vector3?> paymentBowlOpening;
    private Action<Vector3, Rigidbody, Action<bool>> dropPaymentCoin;
    internal Transform PaymentBowl { get; set; }
    private PaymentCoin draggedPaymentCoin;
    private bool grabbedFromTaxPan;
    private float coinGrabY;
    private bool absorbingPayment;
    private TaxScaleView taxScale;
    public Collider WhiteBoard { get; set; }
    public Collider CoinRamp { get; set; }
    public Func<Vector2, int, Transform> RefillSlot { get; set; }
    public Func<int, int, bool> RefillBooks { get; set; }
    internal Transform BoardShelf { get; set; }
    internal Func<IEnumerable<Transform>> BooksOnBoard { get; set; }
    internal IEnumerable<Transform> BoardCoins => paymentCoins
        .Where(coin => coin != draggedPaymentCoin && !coin.inPaymentBowl && !coin.onTaxPan && coin.refillSlot < 0 && coin.model != null)
        .Select(coin => coin.model.transform).Concat(learningCoins.Select(coin => coin.Model.transform));
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
        public bool landing;
        public bool inPaymentBowl;
        public bool onTaxPan;
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
        ReconcileLearningCoins();
        int[] counts = AvailableCoinCounts();
        for (int index = 0; index < stacks.Length; index++)
        {
            if (stacks[index] == null) continue;
            stacks[index].SetCount(counts[index]);
            stacks[index].SetMilestoneTint(index == 2 ? IntelligenceMilestone : AbilityCoinPresentation.Colors[index]);
            foreach (var coin in stacks[index].coins ?? Array.Empty<GameObject>())
                if (coin != null && coin.activeSelf) StampCoin(coin);
        }
    }

    public int AvailableCoins(int index) =>
        AvailableCoinCounts()[index];

    public int[] AvailableCoinCounts()
    {
        int[] counts = inventory.RemainingCoins;
        foreach (var coin in paymentCoins)
            counts[coin.type] = Mathf.Max(0, counts[coin.type] - 1);
        foreach (var coin in learningCoins)
            counts[coin.Data.ability] = Mathf.Max(0, counts[coin.Data.ability] - 1);
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
        UnbindLearningBoard();
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

    public bool BeginBoardPayment(int[] cost, Collider grid, Func<Ray, Vector3?> bowlOpening = null,
        Action<Vector3, Rigidbody, Action<bool>> dropCoin = null, bool useTaxPan = false)
    {
        if (!IsReady || board.trayFloor == null || grid == null ||
            board.supplyButtons == null || board.supplyButtons.Length < 4 ||
            !AbilityPayment.IsValid(cost) || cost == null || !Array.Exists(cost, amount => amount > 0))
            return false;
        if (!useTaxPan) SetTaxScale(null);
        CancelCoinSelection();
        paymentCost = (int[])cost.Clone();
        paymentGrid = grid;
        paymentBowlOpening = bowlOpening;
        dropPaymentCoin = dropCoin;
        if (draggedPaymentCoin != null)
        {
            draggedPaymentCoin.refillSlot = -1;
            DropOnBoard(draggedPaymentCoin, WhiteBoard ?? board.trayFloor,
                TrayPosition(draggedPaymentCoin.type, paymentCoins.IndexOf(draggedPaymentCoin)), .20f);
        }
        draggedPaymentCoin = null;
        grabbedFromTaxPan = false;
        // Reservations on the scale fill matching requirements before loose coins.
        var requiredOnBoard = (int[])cost.Clone();
        foreach (var coin in paymentCoins)
            if (coin.onTaxPan && requiredOnBoard[coin.type] > 0)
                requiredOnBoard[coin.type]--;
        var retainedPan = new int[4];
        var retainedBoard = new int[4];
        for (int i = 0; i < paymentCoins.Count;)
        {
            var coin = paymentCoins[i];
            int limit = coin.onTaxPan ? cost[coin.type] - requiredOnBoard[coin.type] : requiredOnBoard[coin.type];
            int[] retained = coin.onTaxPan ? retainedPan : retainedBoard;
            if (retained[coin.type] < limit)
            {
                retained[coin.type]++;
                i++;
                continue;
            }
            RemovePaymentCoin(coin);
            paymentCoins.RemoveAt(i);
        }
        RestackTaxPan();
        RefreshDisplay();
        return true;
    }

    public void HandleBoardPaymentInput(Action ready, Action cancelled)
    {
        if (!CanStageCoins || absorbingPayment) return;
        if (HasBoardPayment && draggedPaymentCoin == null && !DraggingPaymentBundle && PaymentCoinCountMatches())
        {
            ready();
            return;
        }
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
        {
            if (CancelCoinSelection()) return;
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
            if (CancelCoinSelection()) return;
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
        if (draggedPaymentCoin == null && HandlePaymentSelectionPointer(point, down, held, up)) return;
        if (draggedPaymentCoin == null && HandleLearningPointer(point, down, held, up)) return;
        if (!HasBoardPayment && taxScale == null && draggedPaymentCoin != null &&
            RefillSlot?.Invoke(point, draggedPaymentCoin.type) is Transform refill)
        {
            InsertRefillCoin(refill, up);
            return;
        }
        if (!boardInput.TryCreateRay(point, out var ray))
        {
            if (up && draggedPaymentCoin != null)
            {
                draggedPaymentCoin.refillSlot = -1;
                DropOnBoard(draggedPaymentCoin, WhiteBoard ?? board.trayFloor,
                    TrayPosition(draggedPaymentCoin.type, paymentCoins.IndexOf(draggedPaymentCoin)), .20f);
                draggedPaymentCoin = null;
                grabbedFromTaxPan = false;
            }
            return;
        }
        if (down)
        {
            float closest = float.PositiveInfinity;
            foreach (var coin in paymentCoins)
            {
                if (coin.landing || coin.inPaymentBowl) continue;
                var bounds = BoardItemDrop.ItemBounds(coin.model.transform);
                if (!bounds.IntersectRay(ray, out float distance) || distance >= closest) continue;
                closest = distance;
                draggedPaymentCoin = coin;
            }
            if (draggedPaymentCoin != null)
            {
                coinGrabY = point.y;
                BoardItemDrop.Hold(draggedPaymentCoin.body);
                grabbedFromTaxPan = draggedPaymentCoin.onTaxPan;
                if (grabbedFromTaxPan)
                {
                    draggedPaymentCoin.onTaxPan = false;
                    draggedPaymentCoin.model.transform.SetParent(coinContainer, true);
                    RestackTaxPan();
                }
                inventory?.TryPlayAbilityCoinSound(draggedPaymentCoin.model, draggedPaymentCoin.type);
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
                grabbedFromTaxPan = false;
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
        Collider surface;
        Vector3 hit;
        if (CoinRamp != null && CoinRamp.Raycast(ray, out var rampHit, 1000f) && rampHit.normal.y > .5f)
        {
            surface = CoinRamp;
            hit = rampHit.point;
        }
        else
            surface = BoardItemDrop.FindSurface(ray, inputCamera, point, draggedPaymentCoin.model.transform,
                WhiteBoard ?? board.trayFloor, paymentGrid, out hit);
        float lift = BoardItemDrop.PointerLift(inputCamera, point, coinGrabY);
        var opening = paymentBowlOpening?.Invoke(ray);
        bool taxPanTarget = taxScale != null && taxScale.HitsPlayerPan(ray) &&
            paymentCoins.Count(coin => coin.onTaxPan) < taxScale.PlayerCapacity;
        if (held)
        {
            var model = draggedPaymentCoin.model.transform;
            if (taxPanTarget)
                model.position = taxScale.PlayerStackPosition + Vector3.up *
                    (paymentCoins.Count(coin => coin.onTaxPan) * TaxScaleView.StackSpacing + lift);
            else if (opening is Vector3 aboveRim)
            {
                Bounds shape = BoardItemDrop.ItemBounds(model);
                model.position = aboveRim + Vector3.up * (lift + model.position.y - shape.min.y);
            }
            else if (surface != null)
            {
                Vector3 position = CoinPositionAbove(surface, model, hit, lift);
                if (PaymentBowl == null || !CoinOverlapsBowl(model, position))
                    model.position = position;
            }
        }
        if (!up) return;
        var item = draggedPaymentCoin;
        draggedPaymentCoin = null;
        grabbedFromTaxPan = false;
        item.refillSlot = -1;
        if (taxPanTarget)
        {
            item.onTaxPan = true;
            RestackTaxPan();
        }
        else if (opening is Vector3 entry && dropPaymentCoin != null) InsertPaymentCoin(item, entry);
        else DropOnBoard(item, surface, hit, lift);
    }

    private bool CoinOverlapsBowl(Transform coin, Vector3 position)
    {
        Bounds shape = BoardItemDrop.ItemBounds(coin);
        shape.center += position - coin.position;
        return BoardItemDrop.ItemBounds(PaymentBowl).Intersects(shape);
    }

    private void InsertPaymentCoin(PaymentCoin item, Vector3 entry)
    {
        item.landing = true;
        dropPaymentCoin(entry, item.body, inside =>
        {
            if (!paymentCoins.Contains(item)) return;
            item.landing = false;
            item.inPaymentBowl = inside;
            if (inside) return;
            item.model.transform.SetParent(coinContainer, true);
            DropOnBoard(item, WhiteBoard ?? board.trayFloor,
                TrayPosition(item.type, paymentCoins.IndexOf(item)), .2f);
        });
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

    private bool DropOnBoard(PaymentCoin item, Collider surface, Vector3 point, float lift)
    {
        var model = item.model.transform;
        var obstacles = new List<Bounds>(2);
        if (BoardShelf != null && BoardShelf.gameObject.activeInHierarchy)
            obstacles.Add(BoardItemDrop.ItemBounds(BoardShelf));
        if (PaymentBowl != null) obstacles.Add(BoardItemDrop.ItemBounds(PaymentBowl));
        if (surface != null && surface == CoinRamp)
            model.position = CoinPositionAbove(surface, model, point, lift);
        else if (surface != null && BoardItemDrop.TryFindClearPosition(surface, model, point, lift, obstacles, out var position))
            model.position = position;
        else
        {
            RemovePaymentCoin(item);
            paymentCoins.Remove(item);
            RefreshDisplay();
            return false;
        }
        if (DropRunner != null) DropRunner.StartCoroutine(DropCoin(item));
        else BoardItemDrop.Release(item.body);
        return true;
    }

    private Vector3 CoinPositionAbove(Collider surface, Transform model, Vector3 point, float lift)
    {
        if (surface != CoinRamp) return BoardItemDrop.PositionAbove(surface, model, point, lift);
        float bottomOffset = model.position.y - BoardItemDrop.ItemBounds(model).min.y;
        return point + Vector3.up * (bottomOffset + lift);
    }

    private bool PaymentCoinCountMatches()
    {
        var placed = new int[4];
        foreach (var coin in paymentCoins)
            if (!coin.landing && coin.refillSlot < 0 && (paymentBowlOpening != null
                    ? coin.inPaymentBowl : coin.onTaxPan || BoardItemDrop.IsOnSurface(paymentGrid, coin.model.transform)))
                placed[coin.type]++;
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
        StampCoin(coin);
        var body = coin.GetComponent<Rigidbody>();
        body.constraints = RigidbodyConstraints.None;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        BoardItemDrop.Hold(body);
        var item = new PaymentCoin { model = coin, body = body, type = index };
        paymentCoins.Add(item);
        RefreshDisplay();
        if (!DropOnBoard(item, WhiteBoard ?? board.trayFloor, trayPosition, Mathf.Max(.18f, outlet.y - trayPosition.y)))
            return false;
        inventory?.TryPlayAbilityCoinSound(coin, index);
        return true;
    }

    private static System.Collections.IEnumerator DropCoin(PaymentCoin item)
    {
        item.landing = true;
        yield return BoardItemDrop.Play(item.body);
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

    public void ClearBoardPayment(bool keepBowlCoins = false)
    {
        CancelPaymentSelection();
        foreach (var coin in paymentCoins)
            if (!keepBowlCoins || !coin.inPaymentBowl) RemovePaymentCoin(coin);
        paymentCoins.Clear();
        paymentCost = null;
        paymentBowlOpening = null;
        dropPaymentCoin = null;
        PaymentBowl = null;
        draggedPaymentCoin = null;
        grabbedFromTaxPan = false;
        absorbingPayment = false;
        taxScale?.SetPlayerCount(0);
        RefreshDisplay();
    }

    internal void SetTaxScale(TaxScaleView scale)
    {
        if (taxScale == scale) return;
        CancelPaymentSelection();
        if (grabbedFromTaxPan && draggedPaymentCoin != null)
        {
            RemovePaymentCoin(draggedPaymentCoin);
            paymentCoins.Remove(draggedPaymentCoin);
            draggedPaymentCoin = null;
            grabbedFromTaxPan = false;
        }
        foreach (var coin in paymentCoins)
            if (coin.onTaxPan) RemovePaymentCoin(coin);
        paymentCoins.RemoveAll(coin => coin.onTaxPan);
        taxScale?.SetPlayerCount(0);
        taxScale = scale;
        RefreshDisplay();
    }

    private void RestackTaxPan()
    {
        if (taxScale == null) return;
        int level = 0;
        foreach (var coin in paymentCoins)
            if (coin.onTaxPan) taxScale.PlacePlayerCoin(coin.model.transform, level++);
        taxScale.SetPlayerCount(level);
    }

    internal Transform[] TakeCollectorCoins(out bool allOnScale, out Vector3 boardCoinPosition)
    {
        CancelPaymentSelection();
        allOnScale = paymentCoins.Count > 0 && paymentCoins.All(coin => coin.onTaxPan);
        var boardCoin = paymentCoins.FirstOrDefault(coin => !coin.onTaxPan);
        boardCoinPosition = boardCoin != null ? boardCoin.model.transform.position : Vector3.zero;
        var visuals = new Transform[paymentCoins.Count];
        for (int i = 0; i < paymentCoins.Count; i++)
        {
            var coin = paymentCoins[i];
            BoardItemDrop.Hold(coin.body);
            coin.body.detectCollisions = false;
            UnityEngine.Object.Destroy(coin.body);
            visuals[i] = coin.model.transform;
        }
        paymentCoins.Clear();
        paymentCost = null;
        draggedPaymentCoin = null;
        grabbedFromTaxPan = false;
        if (!allOnScale) taxScale?.SetPlayerCount(0);
        return visuals;
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
        StampCoin(coinObject);
        BettingCoin coin = coinObject.GetComponent<BettingCoin>() ?? coinObject.AddComponent<BettingCoin>();
        coin.Initialize(index);
        activeCoins.Add(coin);
        inventory.TryPlayAbilityCoinSound(coinObject, index);
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
