using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>Displays the existing learning jobs on the supplied 3D dispenser; owns no economy or timers.</summary>
public sealed class LearningDispenserView : MonoBehaviour
{
    [SerializeField] private MineLearningStation station;
    [SerializeField] private Transform queueBottom, dynamicRoot;
    [SerializeField] private Vector3 queueStep = new Vector3(0,.126f,0);
    [SerializeField] private GameObject queuedTilePrefab;
    [SerializeField] private GameObject[] learningBookPrefabs;
    [SerializeField] private Transform[] outletPath;
    [SerializeField] private Vector3 activeStep = new Vector3(0,.32f,0);
    [SerializeField] private BoxCollider bookBoard;
    [SerializeField] private TMPro.TMP_Text bookHoverLabel;
    [SerializeField] private Transform studentHead;
    [SerializeField] private Transform comboShelfPrefab;
    [SerializeField] private Vector3 comboShelfLocalEuler = new Vector3(-30f, 0f, 0f);
    [SerializeField] private float studyNoticeVerticalOffset = -16f;
    [SerializeField] private float studyMeterVerticalOffset = -12f;
    [SerializeField] private RectTransform learningMeter, learningFill;
    [Header("Screen overlays follow the authored model anchors")]
    [SerializeField] private Camera viewCamera;
    [SerializeField] private RectTransform overlayRoot;
    [SerializeField] private RectTransform[] overlays;
    [SerializeField] private Transform[] overlayAnchors;
    private readonly List<Transform> tiles = new List<Transform>();
    private readonly Dictionary<MineLearningJob,Transform> books = new Dictionary<MineLearningJob,Transform>();
    private readonly List<MineLearningJob> finished = new List<MineLearningJob>();
    private MineLearningJob draggedJob;
    private Transform draggedBook;
    private Vector3 dragOrigin;
    private float bookGrabY;
    private Quaternion dragRotation;
    private BackpackInventoryController backpack;
    private KnightPresentation student;
    private Animator studentAnimator;
    private Transform readingBook;
    private int readingAbility = -1;
    private RectTransform[] refillTargets;
    private Transform[] refillAnchors;
    private bool draggingStoredBook;
    private Image studentStatsPanel;
    private TMPro.TMP_Text studentStatsText;
    private Renderer[] studentRenderers;
    private TMPro.TMP_Text studyResult;
    private Image bookHoverBackground;
    private ChatBubbleGraphic studyResultBackground;
    private Transform[][] stockModels;
    private Bounds[] dispenserHoverBounds;
    private JourneyCoinSupplyController coinSupply;
    private Transform[] shelfSlots;
    private Button shelfSubmit;
    private Transform comboShelf;
    private bool draggingShelf;
    private Plane shelfDragPlane;
    private Vector3 shelfGrabOffset;
    private Coroutine pickup;
    private readonly HashSet<MineLearningJob> settlingBooks = new HashSet<MineLearningJob>();
    private MineLearningJob carriedBook;
    public int QueuedVisualCount => tiles.Count;
    public int ActiveVisualCount => books.Count;
    public bool IsDraggingBook => draggedBook != null;
    public BoxCollider WhiteBoard => bookBoard;
    public void BindBackpack(BackpackInventoryController inventory) => backpack = inventory;
    public void BindCoinSupply(JourneyCoinSupplyController supply) => coinSupply = supply;

    private void Awake()
    {
        if (bookBoard != null) bookBoard.isTrigger = false;
    }

    private void Update()
    {
        if ((IsDraggingBook || draggingShelf) && station?.CanMoveBooks != true)
        {
            CancelBookDrag();
            draggingShelf = false;
            return;
        }
#if ENABLE_INPUT_SYSTEM
        var pointer=Pointer.current;
        if (pointer == null)
        {
            CancelBookDrag();
            draggingShelf = false;
            HideStudentStats();
            return;
        }
        Vector2 position=pointer.position.ReadValue();
        bool down=pointer.press.wasPressedThisFrame, held=pointer.press.isPressed, up=pointer.press.wasReleasedThisFrame;
#else
        Vector2 position=Input.mousePosition;
        bool down=Input.GetMouseButtonDown(0), held=Input.GetMouseButton(0), up=Input.GetMouseButtonUp(0);
#endif
        if (down && !BeginBookDrag(position)) BeginShelfDrag(position);
        if (draggingShelf)
        {
            if (held || up) MoveShelfDrag(position);
            if (!held || up) draggingShelf = false;
            return;
        }
        if(held && IsDraggingBook) MoveBookDrag(position);
        if(up && IsDraggingBook) EndBookDrag(position);
        ShowHoveredBook(position);
        UpdateStudentStats(position);
    }

    private void UpdateStudentStats(Vector2 position)
    {
        if (station?.UsesStudyRules != true || station.Progress == null ||
            studentHead == null || !studentHead.gameObject.activeInHierarchy ||
            viewCamera == null || overlayRoot == null || bookHoverLabel == null || IsDraggingBook || draggingStoredBook)
        {
            HideStudentStats();
            return;
        }

        if (!HitsStudent(position))
        {
            HideStudentStats();
            return;
        }

        if (studentStatsPanel == null) CreateStudentStatsPanel();
        var progress = station.Progress;
        var coins = station.CurrentCoins;
        int limit = station.TodayLimit;
        int remaining = Mathf.Max(0, limit - progress.attemptsToday);
        studentStatsText.text = "<size=23><b>학습 상태</b></size>\n" +
            $"<color=#91BFFF>힘 {coins[0]}</color><pos=122><color=#FFAEAE>속도 {coins[1]}</color>\n" +
            $"<color=#FFDA83>지능 {coins[2]}</color><pos=122><color=#88DDD4>매력 {coins[3]}</color>\n" +
            $"<color=#E9C885>성공 {progress.successfulStudies}<pos=122>오늘 {remaining}/{limit}\n" +
            $"대기 {progress.queued.Count}/{station.Capacity.waiting}</color>";
        RectTransformUtility.ScreenPointToLocalPointInRectangle(overlayRoot,
            viewCamera.WorldToScreenPoint(studentHead.position), null, out var point);
        studentStatsPanel.rectTransform.anchoredPosition = point + new Vector2(48, -62);
        studentStatsPanel.gameObject.SetActive(true);
    }

    private void CreateStudentStatsPanel()
    {
        var panel = new GameObject("StudentStatsPopover", typeof(RectTransform), typeof(Image), typeof(Outline));
        panel.transform.SetParent(overlayRoot, false);
        studentStatsPanel = panel.GetComponent<Image>();
        studentStatsPanel.color = new Color(.08f, .065f, .05f, .95f);
        studentStatsPanel.raycastTarget = false;
        var rect = studentStatsPanel.rectTransform;
        rect.sizeDelta = new Vector2(288, 140);
        rect.pivot = new Vector2(0, .24f);
        var outline = panel.GetComponent<Outline>();
        outline.effectColor = new Color(.63f, .48f, .28f);
        outline.effectDistance = new Vector2(1, -1);
        studentStatsText = Instantiate(bookHoverLabel, rect);
        studentStatsText.gameObject.SetActive(true);
        studentStatsText.raycastTarget = false;
        studentStatsText.richText = true;
        studentStatsText.enableAutoSizing = false;
        studentStatsText.fontSize = 20;
        studentStatsText.color = new Color(.97f, .92f, .8f);
        studentStatsText.alignment = TMPro.TextAlignmentOptions.TopLeft;
        var textRect = studentStatsText.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(14, 8);
        textRect.offsetMax = new Vector2(-12, -9);
    }

    private void HideStudentStats()
    {
        if (studentStatsPanel != null) studentStatsPanel.gameObject.SetActive(false);
    }

    private bool HitsStudent(Vector2 position)
    {
        if (studentHead == null || !studentHead.gameObject.activeInHierarchy || viewCamera == null) return false;
        if (student == null) student = studentHead.GetComponentInParent<KnightPresentation>(true);
        if (student == null) return false;
        studentRenderers ??= student.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var ray = viewCamera.ScreenPointToRay(position);
        return studentRenderers.Any(renderer => renderer != null && renderer.isVisible && renderer.bounds.IntersectRay(ray));
    }

    public bool BeginBookDrag(Vector2 screenPosition)
    {
        if(IsDraggingBook || station == null || !station.CanMoveBooks || viewCamera == null || bookBoard == null) return false;
        Ray ray=viewCamera.ScreenPointToRay(screenPosition);
        if (!TryFindBook(ray, out draggedJob, out draggedBook, draggableOnly: true)) return false;
        dragOrigin = draggedBook.position;
        dragRotation = draggedBook.rotation;
        bookGrabY = screenPosition.y;
        BoardItemDrop.Hold(draggedBook.GetComponent<Rigidbody>());
        draggedBook.rotation = Quaternion.identity;
        draggedJob.held = true;
        return true;
    }

    private bool TryFindBook(Ray ray, out MineLearningJob job, out Transform book, bool draggableOnly = false)
    {
        job = null;
        book = null;
        float nearest = float.PositiveInfinity;
        foreach(var pair in books)
        {
            if (pair.Value == null || !pair.Value.gameObject.activeInHierarchy || (draggableOnly &&
                (pair.Key.held || pair.Key.comboStudy || pair.Key.transit > 0 || !station.Progress.active.Contains(pair.Key)))) continue;
            var renderers=pair.Value.GetComponentsInChildren<Renderer>();
            if(renderers.Length==0) continue;
            Bounds bounds=renderers[0].bounds;
            foreach(var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            if(bounds.IntersectRay(ray,out float distance) && distance<nearest)
            {nearest=distance;job=pair.Key;book=pair.Value;}
        }
        return book != null;
    }

    private void ShowHoveredBook(Vector2 screenPoint)
    {
        if (bookHoverLabel == null || viewCamera == null || overlayRoot == null) return;
        if (draggingStoredBook) return;
        if (!IsDraggingBook && TryFindBook(viewCamera.ScreenPointToRay(screenPoint), out var job, out _) &&
            !string.IsNullOrEmpty(job.bookName))
        {
            SetHover(job.bookName, screenPoint);
            return;
        }
        if (!IsDraggingBook && station?.UsesStudyRules == true && refillTargets != null)
            for (int i = 0; i < refillTargets.Length; i++)
                if (RectTransformUtility.RectangleContainsScreenPoint(refillTargets[i], screenPoint))
                {
                    SetHover($"{MineLearningStation.AbilityName(i)} 책 {station.Stock(i)}권 / 리필 코인 {station.RefillCost(i)}개", screenPoint);
                    return;
                }
        if (!IsDraggingBook && coinSupply?.GetHoveredSupplyIndex(screenPoint) is int coinIndex && coinIndex >= 0)
        {
            SetHover($"{MineLearningStation.AbilityName(coinIndex)} 코인 {coinSupply.AvailableCoins(coinIndex)}개", screenPoint);
            return;
        }
        if (!IsDraggingBook && station?.UsesStudyRules == true && dispenserHoverBounds != null)
        {
            Ray ray = viewCamera.ScreenPointToRay(screenPoint);
            for (int i = 0; i < dispenserHoverBounds.Length; i++)
            {
                if (!dispenserHoverBounds[i].IntersectRay(ray)) continue;
                SetHover($"{MineLearningStation.AbilityName(i)} 책 {station.Stock(i)}권", screenPoint);
                return;
            }
        }
        bookHoverLabel.gameObject.SetActive(false);
        if (bookHoverBackground != null) bookHoverBackground.gameObject.SetActive(false);
    }

    private void SetHover(string label, Vector2 screenPoint)
    {
        if (bookHoverLabel == null || overlayRoot == null) return;
        bookHoverLabel.text = label;
        bookHoverLabel.color = Color.black;
        bookHoverLabel.gameObject.SetActive(true);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(overlayRoot, screenPoint + Vector2.up * 28, null, out var point);
        bookHoverLabel.rectTransform.anchoredPosition = point;
        if (bookHoverBackground == null)
        {
            var background = new GameObject("BookTitleBackground", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(bookHoverLabel.transform.parent, false);
            background.transform.SetSiblingIndex(bookHoverLabel.transform.GetSiblingIndex());
            bookHoverBackground = background.GetComponent<Image>();
            bookHoverBackground.color = new Color(1, .97f, .88f, .96f);
            bookHoverBackground.raycastTarget = false;
        }
        var rect = bookHoverBackground.rectTransform;
        rect.anchorMin = bookHoverLabel.rectTransform.anchorMin;
        rect.anchorMax = bookHoverLabel.rectTransform.anchorMax;
        rect.pivot = bookHoverLabel.rectTransform.pivot;
        rect.anchoredPosition = point;
        rect.sizeDelta = bookHoverLabel.GetPreferredValues(label) + new Vector2(20, 10);
        bookHoverBackground.gameObject.SetActive(true);
    }

    public void ShowStoredBookDrag(MineLearningJob book, Vector2 point)
    {
        draggingStoredBook = true;
        SetHover(book.bookName, point);
    }

    public bool RestoreStoredBook(MineLearningJob book, Vector2 point)
    {
        draggingStoredBook = false;
        var surface = FindBoard(point, null, out var hit);
        if (surface == null || !station.RestoreBook(book, .5f, .5f, BoardIndex(surface))) return false;
        DropBook(ShowBook(book, 1), book, surface, hit);
        return true;
    }

    public void MoveBookDrag(Vector2 screenPosition)
    {
        if(!IsDraggingBook) return;
        var surface = FindBoard(screenPosition, draggedBook, out var hit);
        if (surface != null)
            draggedBook.position = BoardItemDrop.PositionAbove(surface, draggedBook, hit,
                BoardItemDrop.PointerLift(viewCamera, screenPosition, bookGrabY));
    }

    public bool EndBookDrag(Vector2 screenPosition)
    {
        if(!IsDraggingBook) return false;
        if (backpack != null && backpack.IsBookDropTarget(screenPosition) && station.StoreBook(draggedJob))
        {
            draggedJob = null;
            draggedBook = null;
            return true;
        }
        if (!draggedJob.started && TryPlaceOnShelf(screenPosition))
        {
            draggedJob.held = false;
            draggedJob = null;
            draggedBook = null;
            return true;
        }
        var surface = FindBoard(screenPosition, draggedBook, out var hit);
        if (surface == null) { CancelBookDrag(); return false; }
        DropBook(draggedBook, draggedJob, surface, hit, BoardItemDrop.PointerLift(viewCamera, screenPosition, bookGrabY));
        draggedBook = null;
        draggedJob = null;
        return true;
    }

    public void CancelBookDrag()
    {
        if (draggedBook != null)
        {
            draggedBook.SetPositionAndRotation(dragOrigin, dragRotation);
            if (draggedJob.onBoard && draggedJob.boardIndex < 2)
                BoardItemDrop.Release(draggedBook.GetComponent<Rigidbody>());
        }
        if(draggedJob != null) draggedJob.held=false;
        draggedBook=null;draggedJob=null;
    }
    private void OnDisable()
    {
        CancelBookDrag();
        draggingShelf = false;
        HideStudentStats();
        StopAllCoroutines();
        foreach (var job in settlingBooks) job.held = false;
        settlingBooks.Clear();
        if (pickup != null)
        {
            foreach (var job in station.Progress.active) job.held = false;
            pickup = null;
            carriedBook = null;
        }
    }

    private Collider FindBoard(Vector2 screen, Transform item, out Vector3 hit)
    {
        hit = default;
        if (station?.CanMoveBooks != true || viewCamera == null) return null;
        Ray ray = viewCamera.ScreenPointToRay(screen);
        return BoardItemDrop.FindSurface(ray, viewCamera, screen, item, bookBoard, backpack?.StudyGrid, out hit);
    }

    private int BoardIndex(Collider surface) => surface == bookBoard ? 0 : 1;

    private static void BoardRange(Collider surface, Transform book, out Vector3 min, out Vector3 max)
    {
        min = BoardItemDrop.PositionAbove(surface, book, surface.bounds.min, .01f);
        max = BoardItemDrop.PositionAbove(surface, book, surface.bounds.max, .01f);
    }
    private void PlaceOnBoard(Transform book,MineLearningJob job)
    {
        book.rotation=Quaternion.identity;
        var surface = job.boardIndex == 1 && backpack?.StudyGrid != null ? backpack.StudyGrid : bookBoard;
        BoardRange(surface, book, out var min, out var max);
        book.position = new Vector3(Mathf.Lerp(min.x, max.x, job.boardU), min.y, Mathf.Lerp(min.z, max.z, job.boardV));
        BoardItemDrop.Release(book.GetComponent<Rigidbody>());
    }

    private void LateUpdate()
    {
        if(overlayRoot != null && viewCamera != null)
            for(int i=0;i<Mathf.Min(overlays.Length,overlayAnchors.Length);i++)
            {
                if(overlays[i] == null || overlayAnchors[i] == null) continue;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(overlayRoot,viewCamera.WorldToScreenPoint(overlayAnchors[i].position),null,out var point);
                overlays[i].anchoredPosition=point;
            }
        if(station == null) return;
        var progress = station.Progress;
        if(progress == null) return;
        if (station.UsesStudyRules) UpdateStudyControls();
        UpdateShelf();
        UpdateStudyResult();
        UpdateLearningMeter(progress);
        int tileCount = station.UsesStudyRules ? 0 : progress.queued.Count;
        while(tiles.Count > tileCount)
        {
            var last=tiles[tiles.Count-1];tiles.RemoveAt(tiles.Count-1);Remove(last);
        }
        while(tiles.Count < tileCount)
            tiles.Add(Instantiate(queuedTilePrefab,dynamicRoot).transform);
        for(int i=0;i<tiles.Count;i++)
            tiles[i].SetPositionAndRotation(queueBottom.TransformPoint(queueStep*i),queueBottom.rotation);
        finished.Clear();
        foreach(var job in books.Keys)
            if(!progress.active.Contains(job) && (!station.UsesStudyRules ||
                (job != progress.producing && !progress.queued.Contains(job)))) finished.Add(job);
        foreach(var job in finished) {if(job==draggedJob)CancelBookDrag();Remove(books[job]);books.Remove(job);}
        for(int i=0;i<progress.active.Count;i++)
        {
            var job=progress.active[i];
            var book = ShowBook(job, 1);
            if (book == draggedBook) continue;
            if (job.held)
            {
                if (job == carriedBook)
                {
                    PositionBookInHands(book);
                    book.localScale *= .48f;
                }
                continue;
            }
            book.gameObject.SetActive(true);
            if (job.boardIndex == 3 && shelfSlots != null && shelfSlots.Length > 0)
            {
                var slot = shelfSlots[Mathf.RoundToInt(job.boardU * (shelfSlots.Length - 1))];
                book.SetPositionAndRotation(slot.position, slot.rotation);
                book.localScale *= .65f;
                continue;
            }
            if (job.onBoard && job.boardIndex == 2 && PositionBookInHands(book))
            {
                book.gameObject.SetActive(!job.comboStudy || job == progress.active.Find(item => item.comboStudy));
                book.localScale *= .48f;
                continue;
            }
            if (job.onBoard && bookBoard != null)
            {
                if (book.GetComponent<Rigidbody>().isKinematic) PlaceOnBoard(book, job);
                continue;
            }
            if(outletPath == null || outletPath.Length<2) continue;
            float t=job.transit>0?Mathf.Clamp01(1-job.transit/station.DeliverySeconds):1;
            float path=t*(outletPath.Length-1);int segment=Mathf.Min((int)path,outletPath.Length-2);
            Vector3 position=Vector3.Lerp(outletPath[segment].position,outletPath[segment+1].position,path-segment);
            if(job.transit<=0)position+=dynamicRoot.TransformVector(activeStep*i);
            book.SetPositionAndRotation(position,transform.rotation);
        }
        if (pickup == null && station.CanMoveBooks && station.UsesStudyRules)
        {
            bool comboReady = progress.active.All(job => !job.comboStudy || !job.held && job.boardIndex == 1);
            var waiting = progress.active.Find(job => job.onBoard && job.boardIndex == 1 && !job.held &&
                (!job.comboStudy || comboReady) && (job.started || progress.attemptsToday < station.TodayLimit) &&
                books.TryGetValue(job, out var placedBook) && BoardItemDrop.IsOnSurface(backpack?.StudyGrid, placedBook));
            if (waiting != null && books.TryGetValue(waiting, out var model)) StartPickup(waiting, model);
        }
        if (!station.UsesStudyRules) return;
        for (int i = 0; i < progress.queued.Count; i++)
            ShowBook(progress.queued[i], .44f).SetPositionAndRotation(queueBottom.TransformPoint(queueStep * (1.9f * i)), queueBottom.rotation);
        if (progress.producing != null)
            ShowBook(progress.producing, .58f).SetPositionAndRotation(queueBottom.TransformPoint(queueStep * 34), queueBottom.rotation);
    }

    private void BeginShelfDrag(Vector2 point)
    {
        if (station?.CanMoveBooks != true || comboShelf == null || viewCamera == null ||
            (shelfSubmit.isActiveAndEnabled &&
             RectTransformUtility.RectangleContainsScreenPoint(shelfSubmit.GetComponent<RectTransform>(), point)))
            return;

        Ray ray = viewCamera.ScreenPointToRay(point);
        if (!BoardItemDrop.ItemBounds(comboShelf).IntersectRay(ray)) return;

        shelfDragPlane = new Plane(Vector3.up, comboShelf.position);
        if (!shelfDragPlane.Raycast(ray, out float distance)) return;
        shelfGrabOffset = comboShelf.position - ray.GetPoint(distance);
        draggingShelf = true;
        if (bookHoverLabel != null) bookHoverLabel.gameObject.SetActive(false);
        if (bookHoverBackground != null) bookHoverBackground.gameObject.SetActive(false);
        HideStudentStats();
    }

    private void MoveShelfDrag(Vector2 point)
    {
        Ray ray = viewCamera.ScreenPointToRay(point);
        if (!shelfDragPlane.Raycast(ray, out float distance)) return;

        Vector3 target = ray.GetPoint(distance) + shelfGrabOffset;
        Vector3 position = BoardItemDrop.PositionAbove(bookBoard, comboShelf, target, 0f);
        position.y = comboShelf.position.y;
        comboShelf.position = position;
    }

    private bool TryPlaceOnShelf(Vector2 point)
    {
        if (shelfSlots == null) return false;
        for (int i = 0; i < shelfSlots.Length; i++)
        {
            if (Vector2.Distance(viewCamera.WorldToScreenPoint(shelfSlots[i].position), point) > Screen.height * .022f ||
                station.Progress.active.Any(job => job != draggedJob && job.boardIndex == 3 &&
                    Mathf.RoundToInt(job.boardU * (shelfSlots.Length - 1)) == i)) continue;
            station.RememberPlacement(draggedJob, (float)i / (shelfSlots.Length - 1), .5f, 3);
            return true;
        }
        return false;
    }

    private void UpdateShelf()
    {
        if (!station.UsesStudyRules || comboShelfPrefab == null || bookBoard == null || overlayRoot == null) return;
        if (comboShelf == null)
        {
            Bounds board = bookBoard.bounds;
            comboShelf = Instantiate(comboShelfPrefab, dynamicRoot);
            comboShelf.name = "StudyComboShelf";
            comboShelf.localRotation = Quaternion.Euler(comboShelfLocalEuler);
            comboShelf.localScale *= .65f;
            comboShelf.position = new Vector3(board.center.x, board.max.y, Mathf.Lerp(board.center.z, board.max.z, .65f));
            shelfSlots = comboShelf.Cast<Transform>().Where(part => part.name.StartsWith("BookPosition_"))
                .OrderBy(part => part.name).ToArray();
            var control = new GameObject("SubmitStudyCombo", typeof(RectTransform), typeof(Image), typeof(Button));
            control.transform.SetParent(overlayRoot, false);
            shelfSubmit = control.GetComponent<Button>();
            shelfSubmit.image.color = new Color(.12f, .13f, .14f, .92f);
            shelfSubmit.GetComponent<RectTransform>().sizeDelta = new Vector2(120, 30);
            var label = Instantiate(bookHoverLabel, control.transform);
            label.gameObject.SetActive(true);
            label.text = "함께 읽기";
            label.color = Color.white;
            label.fontSize = 17;
            label.enableAutoSizing = false;
            label.alignment = TMPro.TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            shelfSubmit.onClick.AddListener(SubmitShelf);
        }
        RectTransformUtility.ScreenPointToLocalPointInRectangle(overlayRoot, viewCamera.WorldToScreenPoint(comboShelf.position),
            null, out var point);
        shelfSubmit.GetComponent<RectTransform>().anchoredPosition = point + Vector2.down * 36;
        shelfSubmit.gameObject.SetActive(station.Progress.active.Any(job => job.boardIndex == 3));
    }

    private void SubmitShelf()
    {
        var grid = backpack?.StudyGrid;
        if (pickup != null || grid == null || !station.SubmitShelf(grid.bounds.center)) return;
        var batch = station.Progress.active.Where(job => job.comboStudy).ToArray();
        for (int i = 0; i < batch.Length; i++)
        {
            var point = grid.bounds.center;
            point.x = Mathf.Lerp(grid.bounds.min.x, grid.bounds.max.x, (i + 1f) / (batch.Length + 1f));
            DropBook(ShowBook(batch[i], 1), batch[i], grid, point);
        }
    }

    private void StartPickup(MineLearningJob job, Transform book)
    {
        if (studentHead == null) return;
        student ??= studentHead.GetComponentInParent<KnightPresentation>(true);
        if (student == null || !student.isActiveAndEnabled || student.CurrentPhase != KnightPresentation.Phase.Seated) return;
        pickup = StartCoroutine(PickupBook(job, book));
    }

    private System.Collections.IEnumerator PickupBook(MineLearningJob job, Transform book)
    {
        var batch = station.Progress.active.Where(item => item == job || job.comboStudy && item.comboStudy).ToArray();
        foreach (var item in batch) item.held = true;
        foreach (var item in batch)
            if (books.TryGetValue(item, out var model)) BoardItemDrop.Hold(model.GetComponent<Rigidbody>());
        Vector3 target = book.position;
        Bounds floor = (backpack?.StudyGrid ?? bookBoard).bounds;
        target.x = Mathf.Clamp(target.x, floor.min.x, floor.max.x);
        target.z = Mathf.Clamp(target.z, floor.min.z, floor.max.z);
        target.y = floor.max.y;
        yield return student.FetchBook(target, () => carriedBook = job);
        foreach (var item in batch) station.RememberPlacement(item, .5f, .5f, 2);
        foreach (var item in batch) item.held = false;
        carriedBook = null;
        pickup = null;
    }

    private void DropBook(Transform book, MineLearningJob job, Collider surface, Vector3 point,
        float lift = BoardItemDrop.DefaultLift)
    {
        book.rotation = Quaternion.identity;
        book.position = BoardItemDrop.PositionAbove(surface, book, point, lift);
        RememberBoardPosition(book, job, surface);
        StartCoroutine(SettleBook(book, job, surface));
    }

    private void RememberBoardPosition(Transform book, MineLearningJob job, Collider surface)
    {
        BoardRange(surface, book, out var min, out var max);
        station.RememberPlacement(job, Mathf.InverseLerp(min.x, max.x, book.position.x),
            Mathf.InverseLerp(min.z, max.z, book.position.z), BoardIndex(surface));
    }

    private System.Collections.IEnumerator SettleBook(Transform book, MineLearningJob job, Collider surface)
    {
        settlingBooks.Add(job);
        job.held = true;
        yield return BoardItemDrop.Play(book.GetComponent<Rigidbody>());
        settlingBooks.Remove(job);
        job.held = false;
        if (book != null && surface != null) RememberBoardPosition(book, job, surface);
    }

    private void UpdateStudyResult()
    {
        bool hasResult = station.StudyResult != null && Time.unscaledTime < station.StudyResultUntil;
        bool visible = (hasResult || station.IsDailyLimitBlocking) &&
            studentHead != null && studentHead.gameObject.activeInHierarchy && viewCamera != null &&
            overlayRoot != null && bookHoverLabel != null;
        if (!visible)
        {
            if (studyResultBackground != null) studyResultBackground.gameObject.SetActive(false);
            return;
        }
        if (studyResult == null)
        {
            var panel = new GameObject("StudentStudyNotice", typeof(RectTransform), typeof(ChatBubbleGraphic));
            panel.transform.SetParent(overlayRoot, false);
            studyResultBackground = panel.GetComponent<ChatBubbleGraphic>();
            studyResultBackground.color = new Color(.12f, .13f, .14f, .82f);
            studyResultBackground.borderColor = new Color(.25f, .27f, .29f, .85f);
            studyResultBackground.showTail = false;
            studyResultBackground.raycastTarget = false;
            studyResultBackground.rectTransform.pivot = new Vector2(.5f, 0);
            studyResult = Instantiate(bookHoverLabel, studyResultBackground.rectTransform);
            studyResult.name = "StudentStudyResult";
            studyResult.gameObject.SetActive(true);
            studyResult.fontSize = 22;
            studyResult.enableAutoSizing = false;
            studyResult.raycastTarget = false;
            studyResult.alignment = TMPro.TextAlignmentOptions.Center;
            var label = studyResult.rectTransform;
            label.anchorMin = Vector2.zero;
            label.anchorMax = Vector2.one;
            label.offsetMin = new Vector2(9, 5);
            label.offsetMax = new Vector2(-9, -5);
        }
        studyResult.text = hasResult ? station.StudyResult : "오늘의 학습량 소진!";
        studyResult.color = !hasResult ? new Color(1, .86f, .55f) :
            station.StudyResultSucceeded ? new Color(.83f, 1, .58f) : new Color(1, .67f, .64f);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(overlayRoot,
            viewCamera.WorldToScreenPoint(studentHead.position), null, out var point);
        var panelRect = studyResultBackground.rectTransform;
        float widthLimit = Mathf.Max(130, 2 * (point.x - overlayRoot.rect.xMin - 8));
        var preferred = studyResult.GetPreferredValues(studyResult.text);
        float width = Mathf.Min(preferred.x + 18, widthLimit, 340);
        float height = studyResult.GetPreferredValues(studyResult.text, width - 18, 0).y + 10;
        panelRect.sizeDelta = new Vector2(width, height);
        panelRect.anchoredPosition = point + Vector2.up * studyNoticeVerticalOffset;
        studyResultBackground.gameObject.SetActive(true);
    }

    private Transform ShowBook(MineLearningJob job, float scale)
    {
        var prefab = learningBookPrefabs[job.ability];
        if (!books.TryGetValue(job, out var book))
        {
            book = Instantiate(prefab, dynamicRoot).transform;
            var body = book.gameObject.AddComponent<Rigidbody>();
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            BoardItemDrop.Hold(body);
            var shape = book.gameObject.AddComponent<BoxCollider>();
            var localBounds = new Bounds(Vector3.zero, Vector3.zero);
            foreach (var renderer in book.GetComponentsInChildren<Renderer>())
            {
                var bounds = renderer.localBounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 vertex = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                        (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    localBounds.Encapsulate(book.InverseTransformPoint(renderer.transform.TransformPoint(vertex)));
                }
            }
            shape.center = localBounds.center;
            shape.size = localBounds.size;
            books.Add(job, book);
        }
        book.localScale = prefab.transform.localScale * scale;
        return book;
    }

    private void UpdateStudyControls()
    {
        if (overlayRoot == null || viewCamera == null || overlayAnchors.Length < 8 || overlays.Length < 8) return;
        if (refillTargets == null)
        {
            refillTargets = new RectTransform[4];
            refillAnchors = new Transform[4];
            var slots = GetComponentsInChildren<Transform>(true).Where(part => part.name == "CoinSlot").ToArray();
            for (int i = 0; i < 4; i++)
            {
                Transform balance = overlayAnchors[i * 2 + 1];
                var slot = slots.OrderBy(part => Mathf.Abs(viewCamera.WorldToViewportPoint(part.position).x -
                    viewCamera.WorldToViewportPoint(balance.position).x)).FirstOrDefault();
                refillAnchors[i] = slot;
                var button = new GameObject("BookRefill_" + i, typeof(RectTransform), typeof(Image), typeof(Button));
                button.transform.SetParent(overlayRoot, false);
                var target = (RectTransform)button.transform;
                target.sizeDelta = new Vector2(54, 25);
                button.GetComponent<Image>().color = Color.clear;
                refillTargets[i] = target;
            }
        }
        if (stockModels == null)
        {
            dispenserHoverBounds = new Bounds[4];
            stockModels = new Transform[4][];
            for (int i = 0; i < stockModels.Length; i++)
            {
                var anchor = overlayAnchors[i * 2];
                while (anchor != null && !anchor.name.Contains("_Column_")) anchor = anchor.parent;
                stockModels[i] = anchor == null ? System.Array.Empty<Transform>() : anchor.GetComponentsInChildren<Transform>(true)
                    .Where(part => part.name.StartsWith("LearningObject_")).OrderBy(part => part.localPosition.y).ToArray();
                if (anchor != null) dispenserHoverBounds[i] = BoardItemDrop.ItemBounds(anchor);
            }
        }
        for (int i = 0; i < 4; i++)
        {
            for (int item = 0; item < stockModels[i].Length; item++)
                stockModels[i][item].gameObject.SetActive(item < station.Stock(i));
            if (refillAnchors[i] != null)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(overlayRoot,
                    viewCamera.WorldToScreenPoint(refillAnchors[i].position), null, out var point);
                refillTargets[i].anchoredPosition = point;
            }
        }
    }

    public Transform FindRefillSlot(Vector2 point, int ability) =>
        refillTargets != null && ability >= 0 && ability < refillTargets.Length && station.CanMoveBooks &&
        RectTransformUtility.RectangleContainsScreenPoint(refillTargets[ability], point) ? refillAnchors[ability] : null;

    public bool TryRefillBooks(int ability, int insertedCoins) => station.Refill(ability, insertedCoins);

    private void UpdateLearningMeter(MineLearningProgress progress)
    {
        if (learningMeter == null || learningFill == null || studentHead == null || viewCamera == null || overlayRoot == null) return;
        var job = progress.active.Find(station.IsStudying);
        bool visible = job != null && studentHead.gameObject.activeInHierarchy;
        UpdateReadingPose(visible ? job : null);
        learningMeter.gameObject.SetActive(visible);
        if (!visible) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(overlayRoot,
            viewCamera.WorldToScreenPoint(studentHead.position), null, out var point);
        learningMeter.anchoredPosition = point + Vector2.up * studyMeterVerticalOffset;
        learningFill.anchorMax = new Vector2(Mathf.Clamp01(job.remaining / Mathf.Max(.1f, job.duration)), 1);
    }

    private void UpdateReadingPose(MineLearningJob job)
    {
        if (student == null) student = studentHead.GetComponentInParent<KnightPresentation>(true);
        student?.SetReading(job != null);
        if (job == null || job.boardIndex == 2)
        {
            if (readingBook != null) readingBook.gameObject.SetActive(false);
            return;
        }
        if (readingAbility != job.ability || readingBook == null)
        {
            if (readingBook != null) Remove(readingBook);
            readingAbility = job.ability;
            readingBook = Instantiate(learningBookPrefabs[readingAbility], dynamicRoot).transform;
            readingBook.localScale *= .48f;
        }
        readingBook.gameObject.SetActive(PositionBookInHands(readingBook));
    }

    private bool PositionBookInHands(Transform book)
    {
        if (studentHead == null || !studentHead.gameObject.activeInHierarchy) return false;
        if (studentAnimator == null) studentAnimator = studentHead.GetComponentInParent<Animator>(true);
        if (studentAnimator == null || !studentAnimator.isHuman) return false;
        var left = studentAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
        var right = studentAnimator.GetBoneTransform(HumanBodyBones.RightHand);
        if (left == null || right == null) return false;
        book.SetPositionAndRotation((left.position + right.position) * .5f + studentAnimator.transform.up * .10f,
            studentAnimator.transform.rotation * Quaternion.Euler(-23, 0, 0));
        return true;
    }
    private void Remove(Transform item)
    {
        item.gameObject.SetActive(false);Destroy(item.gameObject);
    }
}
