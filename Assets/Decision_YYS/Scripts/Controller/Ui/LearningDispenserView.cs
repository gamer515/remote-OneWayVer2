using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>Displays the existing learning jobs on the supplied 3D dispenser; owns no economy or timers.</summary>
public sealed partial class LearningDispenserView : MonoBehaviour
{
    [SerializeField] private MineLearningStation station;
    [SerializeField] private Transform queueBottom, dynamicRoot;
    [SerializeField] private Vector3 queueStep = new Vector3(0,.126f,0);
    [SerializeField] private float studyQueueHeight = 3.7f;
    [SerializeField] private Vector3 generationSlotOffset = new Vector3(0, 4.47f, -.04f);
    [SerializeField] private GameObject queuedTilePrefab;
    [SerializeField] private GameObject[] learningBookPrefabs;
    [SerializeField] private Transform[] outletPath;
    [SerializeField] private Vector3 activeStep = new Vector3(0,.32f,0);
    [SerializeField] private BoxCollider bookBoard;
    [SerializeField] private TMPro.TMP_Text bookHoverLabel;
    [SerializeField] private TMPro.TextMeshProUGUI studyNotice;
    [SerializeField] private Transform studentHead;
    [SerializeField] private float studyNoticeVerticalOffset = -16f;
    [SerializeField] private float studyMeterVerticalOffset = -12f;
    [SerializeField] private RectTransform learningMeter, learningFill;
    [Header("Screen overlays follow the authored model anchors")]
    [SerializeField] private Camera viewCamera;
    [SerializeField] private RectTransform overlayRoot;
    [SerializeField] private RectTransform[] overlays;
    [SerializeField] private Transform[] overlayAnchors;
    private readonly List<Transform> tiles = new List<Transform>();
    private ChatBubbleGraphic studyNoticeBackdrop;
    private TMPro.TextMeshProUGUI foregroundStudyNotice;
    private Color originalStudyNoticeColor;
    private string fittedStudyNotice;
    private float fittedStudyNoticeWidth;
    private readonly Dictionary<MineLearningJob,Transform> books = new Dictionary<MineLearningJob,Transform>();
    private readonly List<MineLearningJob> finished = new List<MineLearningJob>();
    private MineLearningJob draggedJob;
    private Transform draggedBook;
    private MineLearningJob pressedBook;
    private Vector2 bookPressPoint;
    private float bookPressTime;
    private const float BookDragDistanceSquared = 36f;
    private const float BookPreviewMaxPress = .35f;
    private Vector3 dragOrigin;
    private float bookGrabY;
    private Collider bookDragBoard;
    private readonly List<Image> queueTicks = new List<Image>();
    private Quaternion dragRotation;
    private BoxCollider studyGrid;
    private KnightPresentation student;
    private Animator studentAnimator;
    private Transform readingBook;
    private int readingAbility = -1;
    private Transform[] refillAnchors;
    private Rect[] refillAreas;
    private Image studentStatsPanel;
    private TMPro.TMP_Text studentStatsText;
    private Renderer[] studentRenderers;
    private TMPro.TMP_Text studyResult;
    private Image bookHoverBackground;
    private ChatBubbleGraphic studyResultBackground;
    private Transform[][] stockModels;
    private Bounds[] dispenserHoverBounds;
    private JourneyCoinSupplyController coinSupply;
    private readonly Dictionary<int, Button> stackSubmitButtons = new();
    private Coroutine pickup;
    private readonly HashSet<MineLearningJob> settlingBooks = new HashSet<MineLearningJob>();
    private MineLearningJob carriedBook;
    public int QueuedVisualCount => tiles.Count;
    public int ActiveVisualCount => books.Count;
    public bool IsDraggingBook => draggedBook != null;
    public BoxCollider WhiteBoard => bookBoard;
    internal void BindStudyGrid(BoxCollider grid) => studyGrid = grid;
    private TaxScaleView taxScale;
    private Vector2? defaultHoverSize;
    internal void BindTaxScale(TaxScaleView view) => taxScale = view;
    public void BindCoinSupply(JourneyCoinSupplyController supply)
    {
        coinSupply = supply;
        if (supply != null)
        {
            supply.BoardShelf = bookshelf;
            supply.BooksOnBoard = BookBoardOccupants;
        }
    }

    private IEnumerable<Transform> BookBoardOccupants()
    {
        foreach (var pair in books)
            if (pair.Value != null && pair.Value.gameObject.activeInHierarchy && pair.Value != draggedBook &&
                pair.Key.onBoard && pair.Key.boardIndex < 2)
                yield return pair.Value;
        foreach (var codex in CodicesOnBoard())
            yield return codex;
    }

    private IEnumerable<Transform> CodicesOnBoard()
    {
        for (int i = 0; i < codexBooks.Length; i++)
            if (codexOnBoard[i] && (!draggingCodex || pressedCodex != i) && codexBooks[i] != null)
                yield return codexBooks[i];
    }

    private IEnumerable<Bounds> ShelfBounds()
    {
        if (bookshelf != null && bookshelf.gameObject.activeInHierarchy)
            yield return bookshelfBounds;
    }

    private void Awake()
    {
        if (bookBoard != null) bookBoard.isTrigger = false;
    }

    private void Update()
    {
        bool reading = station != null &&
            (station.ReadingPreview != null || station.PendingReading != null || station.ReadingResult != null);
        bool canInteract = station?.CanMoveBooks == true && !reading && studyCodexUi?.IsComboGuideInputBlocked != true;
        if (!canInteract)
        {
            pressedBook = null;
            CancelBookDrag();
            CancelCodexPointer();
            if (reading && station.CanMoveBooks)
            {
                if (bookHoverLabel != null) bookHoverLabel.gameObject.SetActive(false);
                if (bookHoverBackground != null) bookHoverBackground.gameObject.SetActive(false);
                HideStudentStats();
                return;
            }
        }
#if ENABLE_INPUT_SYSTEM
        var pointer=Pointer.current;
        if (pointer == null)
        {
            pressedBook = null;
            CancelBookDrag();
            CancelCodexPointer();
            HideStudentStats();
            return;
        }
        Vector2 position=pointer.position.ReadValue();
        bool down=pointer.press.wasPressedThisFrame, held=pointer.press.isPressed, up=pointer.press.wasReleasedThisFrame;
#else
        Vector2 position=Input.mousePosition;
        bool down=Input.GetMouseButtonDown(0), held=Input.GetMouseButton(0), up=Input.GetMouseButtonUp(0);
#endif
        if (canInteract) HandleBookPointer(position, down, held, up);
        ShowHoveredBook(position);
        UpdateStudentStats(position);
    }

    private void HandleBookPointer(Vector2 position, bool down, bool held, bool up)
    {
        if (HandleCodexPointer(position, down, held, up)) return;
        if (down)
        {
            if (stackSubmitButtons.Values.Any(button => HitsComboButton(button, position))) return;
            if (viewCamera != null && TryFindBook(viewCamera.ScreenPointToRay(position), out var job, out _))
            {
                pressedBook = job;
                bookPressPoint = position;
                bookPressTime = Time.unscaledTime;
            }
        }
        if (pressedBook != null)
        {
            bool moved = (position - bookPressPoint).sqrMagnitude >= BookDragDistanceSquared;
            if (moved || up || !held)
            {
                var job = pressedBook;
                pressedBook = null;
                if (moved && books.TryGetValue(job, out var book)) BeginBookDrag(job, book, bookPressPoint);
                else if (up && !moved && Time.unscaledTime - bookPressTime <= BookPreviewMaxPress)
                    station.TryOpenReadingPreview(job);
            }
        }
        if(held && IsDraggingBook) MoveBookDrag(position);
        if(up && IsDraggingBook) EndBookDrag(position);
    }

    private void UpdateStudentStats(Vector2 position)
    {
        if (station?.UsesStudyRules != true || station.Progress == null ||
            studentHead == null || !studentHead.gameObject.activeInHierarchy ||
            viewCamera == null || overlayRoot == null || bookHoverLabel == null || IsDraggingBook)
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
            $"대기 {progress.queued.Count}/{station.ProductionQueueLimit}</color>";
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

    private void BeginBookDrag(MineLearningJob job, Transform book, Vector2 screenPosition)
    {
        if (IsDraggingBook || book == null || !book.gameObject.activeInHierarchy || bookBoard == null ||
            !station.CanMoveBooks || job.held || job.comboStudy || job.transit > 0 ||
            !station.Progress.active.Contains(job)) return;
        draggedJob = job;
        draggedBook = book;
        dragOrigin = draggedBook.position;
        dragRotation = draggedBook.rotation;
        bookGrabY = screenPosition.y;
        bookDragBoard = draggedJob.onBoard && draggedJob.boardIndex == 1 && studyGrid != null
            ? studyGrid : bookBoard;
        BoardItemDrop.Hold(draggedBook.GetComponent<Rigidbody>());
        draggedBook.rotation = Quaternion.identity;
        draggedJob.held = true;
    }

    private bool TryFindBook(Ray ray, out MineLearningJob job, out Transform book, Transform ignore = null)
    {
        job = null;
        book = null;
        float nearest = float.PositiveInfinity;
        foreach(var pair in books)
        {
            if (pair.Value == null || pair.Value == ignore || !pair.Value.gameObject.activeInHierarchy) continue;
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
        if (!IsDraggingBook && taxScale?.GetHover(viewCamera.ScreenPointToRay(screenPoint)) is string taxNotice)
        {
            SetHover(taxNotice, screenPoint, keepOnScreen: true);
            return;
        }
        if (!IsDraggingBook && station?.Progress?.producing is MineLearningJob producing)
        {
            Vector2 center = viewCamera.WorldToScreenPoint(queueBottom.TransformPoint(generationSlotOffset));
            Vector2 corner = viewCamera.WorldToScreenPoint(queueBottom.TransformPoint(generationSlotOffset + new Vector3(.34f, .44f, 0)));
            if (Mathf.Abs(screenPoint.x - center.x) <= Mathf.Abs(corner.x - center.x) &&
                Mathf.Abs(screenPoint.y - center.y) <= Mathf.Abs(corner.y - center.y))
            {
                SetHover($"{station.GetBookDisplayName(producing)} / 생성 중 {Mathf.Max(0, producing.generationRemaining):0.0}초", screenPoint);
                return;
            }
        }
        if (!IsDraggingBook && !draggingCodex && CodexUnderPointer(screenPoint) is int codex && codex >= 0)
        {
            SetHover(CodexTitles[codex], screenPoint, keepOnScreen: true);
            return;
        }
        if (!IsDraggingBook && !draggingCodex && TryFindBook(viewCamera.ScreenPointToRay(screenPoint), out var job, out _))
        {
            SetHover(station.GetBookHoverText(job), screenPoint, keepOnScreen: MineLearningStation.IsStoredBook(job));
            return;
        }
        if (!IsDraggingBook && station?.UsesStudyRules == true && refillAreas != null)
            for (int i = 0; i < refillAreas.Length; i++)
                if (refillAreas[i].Contains(screenPoint))
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

    private void SetHover(string label, Vector2 screenPoint, bool keepOnScreen = false)
    {
        if (bookHoverLabel == null || overlayRoot == null) return;
        bookHoverLabel.text = label;
        bookHoverLabel.color = Color.black;
        bookHoverLabel.gameObject.SetActive(true);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(overlayRoot, screenPoint + Vector2.up * 28, null, out var point);
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
        var labelRect = bookHoverLabel.rectTransform;
        defaultHoverSize ??= labelRect.sizeDelta;
        var preferredSize = bookHoverLabel.GetPreferredValues(label);
        labelRect.sizeDelta = keepOnScreen ? preferredSize + new Vector2(2, 2) : defaultHoverSize.Value;
        rect.sizeDelta = preferredSize + new Vector2(20, 10);
        if (keepOnScreen)
        {
            var bounds = overlayRoot.rect;
            var beforePivot = Vector2.Scale(rect.sizeDelta, rect.pivot);
            var afterPivot = rect.sizeDelta - beforePivot;
            point.x = Mathf.Clamp(point.x, bounds.xMin + beforePivot.x, bounds.xMax - afterPivot.x);
            point.y = Mathf.Clamp(point.y + beforePivot.y, bounds.yMin + beforePivot.y, bounds.yMax - afterPivot.y);
        }
        bookHoverLabel.rectTransform.anchoredPosition = point;
        rect.anchoredPosition = point;
        bookHoverBackground.gameObject.SetActive(true);
    }

    public void MoveBookDrag(Vector2 screenPosition)
    {
        if (!IsDraggingBook) return;
        var surface = FindBookDragBoard(screenPosition, out var hit);
        if (surface != null)
            draggedBook.position = BoardItemDrop.PositionAbove(surface, draggedBook, hit,
                BoardItemDrop.PointerLift(viewCamera, screenPosition, bookGrabY));
    }

    public bool EndBookDrag(Vector2 screenPosition)
    {
        if(!IsDraggingBook) return false;
        Ray ray = viewCamera.ScreenPointToRay(screenPosition);
        bool overShelf = bookshelf != null && bookshelfBounds.IntersectRay(ray);
        if (overShelf && !TryStoreDraggedBook(screenPosition))
        {
            CancelBookDrag();
            return false;
        }
        bool stacked = !overShelf && TryFindBook(ray, out var target, out _, draggedBook) &&
            station.TryStackBook(draggedJob, target);
        if (stacked || overShelf)
        {
            draggedJob.held = false;
            draggedJob = null;
            draggedBook = null;
            return true;
        }
        var surface = FindBookDragBoard(screenPosition, out var hit);
        float lift = BoardItemDrop.PointerLift(viewCamera, screenPosition, bookGrabY);
        if (surface == null || !BoardItemDrop.TryFindClearPosition(surface, draggedBook, hit, lift,
                ShelfBounds().Concat(CodicesOnBoard().Select(BoardItemDrop.ItemBounds)), out var position))
        {
            CancelBookDrag();
            return false;
        }
        DropBook(draggedBook, draggedJob, surface, position);
        draggedBook = null;
        draggedJob = null;
        return true;
    }

    public void CancelBookDrag()
    {
        if (draggedBook != null)
        {
            draggedBook.SetPositionAndRotation(dragOrigin, dragRotation);
            if (draggedJob.onBoard && draggedJob.boardIndex < 2 && draggedJob.stackOrder == 0)
                BoardItemDrop.Release(draggedBook.GetComponent<Rigidbody>());
        }
        if(draggedJob != null) draggedJob.held=false;
        draggedBook=null;draggedJob=null;
        bookDragBoard = null;
    }
    private void OnDisable()
    {
        pressedBook = null;
        CancelCodexPointer();
        CancelBookDrag();
        HideStudentStats();
        if (studyNoticeBackdrop != null) studyNoticeBackdrop.gameObject.SetActive(false);
        if (foregroundStudyNotice != null && studyNotice != null) studyNotice.color = originalStudyNoticeColor;
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

    private static bool HitsComboButton(Button button, Vector2 screen) => button != null &&
        button.isActiveAndEnabled &&
        RectTransformUtility.RectangleContainsScreenPoint((RectTransform)button.transform, screen);

    private void OnDestroy()
    {
        if (studyNoticeBackdrop != null) studyNoticeBackdrop.DisposeNoticeOverlay();
        if (bookshelf != null) Destroy(bookshelf.gameObject);
        foreach (var codex in codexBooks)
            if (codex != null) Destroy(codex.gameObject);
    }

    private Collider FindBookDragBoard(Vector2 screen, out Vector3 hit)
    {
        var ray = viewCamera.ScreenPointToRay(screen);
        var other = bookDragBoard == bookBoard ? studyGrid : bookBoard;
        // Keep the same surface in the gap and where both board projections meet.
        if (other != null && other.Raycast(ray, out _, 1000f) &&
            (bookDragBoard == null || !bookDragBoard.Raycast(ray, out _, 1000f)))
            bookDragBoard = other;
        return BoardItemDrop.FindSurface(ray, viewCamera, screen, draggedBook, bookDragBoard, null, out hit);
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
        var surface = job.boardIndex == 1 && studyGrid != null ? studyGrid : bookBoard;
        BoardRange(surface, book, out var min, out var max);
        var desired = new Vector3(Mathf.Lerp(min.x, max.x, job.boardU), min.y, Mathf.Lerp(min.z, max.z, job.boardV));
        if (!BoardItemDrop.TryFindClearPosition(surface, book, desired, .01f, ShelfBounds(), out var position))
        {
            book.gameObject.SetActive(false);
            return;
        }
        book.position = position;
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
        if (station.UsesStudyRules)
        {
            UpdateStudyControls();
            UpdateStudyNotice();
        }
        UpdateBookshelf();
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
            var book = ShowBook(job, station.UsesStudyRules && !job.onBoard && !job.held ? .78f : 1f);
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
            if (job.stackOrder > 0) continue;
            if (MineLearningStation.IsStoredBook(job) && bookshelfSlots != null)
            {
                book.localScale = Vector3.Scale(book.localScale, ShelvedBookScale);
                PlaceBookshelfBook(book, bookshelfSlots[MineLearningStation.BookshelfSlot(job)]);
                continue;
            }
            if (job.onBoard && job.boardIndex == 2 && PositionBookInHands(book))
            {
                book.gameObject.SetActive(!job.comboStudy ||
                    job == progress.active.Find(item => item.comboStudy && item.boardIndex == 2));
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
            Vector3 start = station.UsesStudyRules && segment == 0
                ? StudyQueuePosition(0, station.ProductionQueueLimit) : outletPath[segment].position;
            Vector3 position=Vector3.Lerp(start,outletPath[segment+1].position,path-segment);
            if(job.transit<=0 && !station.UsesStudyRules)position+=dynamicRoot.TransformVector(activeStep*i);
            book.SetPositionAndRotation(position,transform.rotation);
        }
        UpdateBoardStacks();
        if (pickup == null && station.UsesStudyRules && station.CanBeginStudySession)
        {
            if (station.HasComboStudy) PlaceComboWave(station.NextComboWave());
            bool comboReady = progress.active.All(job => !job.comboStudy || MineLearningStation.IsQueuedComboBook(job) ||
                !job.held && job.boardIndex == 1);
            var waiting = progress.active.Find(job => job.onBoard && job.stackOrder == 0 && job.boardIndex == 1 && !job.held &&
                (job.comboStudy ? comboReady : !station.HasComboStudy) &&
                (job.started || progress.attemptsToday < station.TodayLimit) &&
                books.TryGetValue(job, out var placedBook) && BoardItemDrop.IsOnSurface(studyGrid, placedBook));
            if (waiting != null && books.TryGetValue(waiting, out var model)) StartPickup(waiting, model);
        }
        if (!station.UsesStudyRules)
        {
            foreach (var tick in queueTicks) tick.gameObject.SetActive(false);
            return;
        }
        int queueCapacity = station.ProductionQueueLimit;
        int rows = Mathf.Max(queueCapacity, progress.queued.Count);
        for (int i = 0; i < progress.queued.Count; i++)
            ShowBook(progress.queued[i], .60f * Mathf.Min(1f, 8f / rows))
                .SetPositionAndRotation(StudyQueuePosition(i, rows), queueBottom.rotation);
        if (progress.producing != null)
            ShowBook(progress.producing, .78f).SetPositionAndRotation(queueBottom.TransformPoint(generationSlotOffset), queueBottom.rotation);
        UpdateStudyQueueTicks(queueCapacity, rows, progress.queued.Count);
    }

    private void UpdateBoardStacks()
    {
        var stacks = station.BoardStacks().ToArray();
        foreach (int id in stackSubmitButtons.Keys.Where(id => !stacks.Any(stack => stack.Key == id)).ToArray())
        {
            Destroy(stackSubmitButtons[id].gameObject);
            stackSubmitButtons.Remove(id);
        }
        foreach (var group in stacks)
        {
            var stack = group.ToArray();
            PositionBoardStack(stack);
            UpdateStackSubmitButton(group.Key, stack);
        }
    }

    private void PositionBoardStack(MineLearningJob[] stack)
    {
        var first = stack[0];
        var surface = first.boardIndex == 1 && studyGrid != null ? studyGrid : bookBoard;
        if (surface == null || !books.TryGetValue(first, out var baseModel)) return;
        BoardRange(surface, baseModel, out var min, out var max);
        var center = new Vector3(Mathf.Lerp(min.x, max.x, first.boardU), surface.bounds.max.y,
            Mathf.Lerp(min.z, max.z, first.boardV));
        if (surface == bookBoard && bookshelf != null)
        {
            var footprint = BoardItemDrop.ItemBounds(baseModel);
            float clearance = footprint.extents.x + .04f;
            if (center.x + clearance >= bookshelfBounds.min.x &&
                center.z + footprint.extents.z >= bookshelfBounds.min.z &&
                center.z - footprint.extents.z <= bookshelfBounds.max.z)
                center.x = Mathf.Min(center.x, bookshelfBounds.min.x - clearance);
        }
        float height = center.y;
        foreach (var job in stack)
        {
            if (!books.TryGetValue(job, out var book) || book == draggedBook) continue;
            BoardItemDrop.Hold(book.GetComponent<Rigidbody>());
            book.SetPositionAndRotation(center, Quaternion.Euler(0f, job.stackYaw, 0f));
            var bounds = BoardItemDrop.ItemBounds(book);
            book.position += Vector3.up * (height - bounds.min.y);
            height += bounds.size.y + .002f;
        }
    }

    private void UpdateStackSubmitButton(int stackId, MineLearningJob[] stack)
    {
        if (overlayRoot == null || viewCamera == null || bookHoverLabel == null ||
            !books.TryGetValue(stack[0], out var bottom)) return;
        if (!stackSubmitButtons.TryGetValue(stackId, out var button))
        {
            button = CreateComboButton("StudyBoardStack_" + stackId, stackId, 88f);
            stackSubmitButtons.Add(stackId, button);
        }
        bool visible = !stack[0].comboStudy && bottom != draggedBook;
        button.gameObject.SetActive(visible);
        if (!visible) return;
        Bounds bounds = BoardItemDrop.ItemBounds(bottom);
        Vector3 up = viewCamera.transform.up;
        Vector3 frontEdge = bounds.center - new Vector3(Mathf.Sign(up.x) * bounds.extents.x,
            Mathf.Sign(up.y) * bounds.extents.y, Mathf.Sign(up.z) * bounds.extents.z);
        Vector2 screen = viewCamera.WorldToScreenPoint(bounds.center);
        screen.y = viewCamera.WorldToScreenPoint(frontEdge).y;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(overlayRoot, screen, null, out var point);
        var control = (RectTransform)button.transform;
        string numbers = string.Join(" · ", stack.Select(job => job.bookNumber.ToString()));
        var label = button.GetComponentInChildren<TMPro.TMP_Text>();
        string caption = numbers + "\n학습하기";
        if (label.text != caption)
        {
            label.text = caption;
            control.sizeDelta = new Vector2(Mathf.Clamp(label.GetPreferredValues(numbers).x + 16f, 88f, 280f), 44f);
        }
        control.anchoredPosition = point + Vector2.down * 25f;
        button.interactable = CanUseComboButton(stack);
    }

    private Vector3 StudyQueuePosition(int slot, int rows) =>
        queueBottom.TransformPoint(Vector3.up * (studyQueueHeight * (slot + .5f) / Mathf.Max(1, rows)));

    private void UpdateStudyQueueTicks(int capacity, int rows, int occupied)
    {
        if (overlayRoot == null || viewCamera == null || queueBottom == null) return;
        while (queueTicks.Count < capacity)
        {
            var marker = new GameObject("BookQueueTick", typeof(RectTransform), typeof(Image));
            var rect = marker.GetComponent<RectTransform>();
            rect.SetParent(overlayRoot, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            var image = marker.GetComponent<Image>();
            image.raycastTarget = false;
            queueTicks.Add(image);
        }
        for (int slot = 0; slot < queueTicks.Count; slot++)
        {
            var tick = queueTicks[slot];
            tick.gameObject.SetActive(slot < capacity);
            if (slot >= capacity) continue;
            Vector3 edge = StudyQueuePosition(slot, rows) + queueBottom.TransformVector(Vector3.right * .35f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(overlayRoot, viewCamera.WorldToScreenPoint(edge), null, out var point);
            tick.rectTransform.anchoredPosition = point;
            tick.rectTransform.sizeDelta = new Vector2(slot < occupied ? 12f : 8f, 2f);
            tick.color = slot < occupied ? new Color(.97f, .78f, .45f) : new Color(.71f, .62f, .51f, .65f);
        }
    }

    private Button CreateComboButton(string name, int stackId, float width)
    {
        var control = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        control.transform.SetParent(overlayRoot, false);
        var button = control.GetComponent<Button>();
        button.image.color = new Color(.12f, .13f, .14f, .92f);
        ((RectTransform)control.transform).sizeDelta = new Vector2(width, 28f);
        var label = Instantiate(bookHoverLabel, control.transform);
        label.gameObject.SetActive(true);
        label.text = "학습하기";
        label.color = Color.white;
        label.fontSize = 17;
        label.enableAutoSizing = false;
        label.alignment = TMPro.TextAlignmentOptions.Center;
        label.raycastTarget = false;
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        button.onClick.AddListener(() => SubmitCombo(stackId));
        UiInteractionAudio.Bind(button);
        return button;
    }

    private bool CanUseComboButton(MineLearningJob[] batch) =>
        studyGrid != null && pickup == null && !IsDraggingBook && station.CanSubmitCombo(batch);

    private void SubmitCombo(int stackId)
    {
        if (pickup != null || IsDraggingBook || studyGrid == null) return;
        if (station.SubmitCombo(studyGrid.bounds.center, stackId)) PlaceComboWave(station.NextComboWave());
    }

    private void PlaceComboWave(MineLearningJob[] batch)
    {
        var grid = studyGrid;
        if (grid == null || batch.Length == 0) return;
        var placements = new Vector3[batch.Length];
        var blocked = ShelfBounds().Concat(BookBoardOccupants().Concat(coinSupply?.BoardCoins ?? Enumerable.Empty<Transform>())
            .Where(model => !batch.Any(job => books.TryGetValue(job, out var moving) && moving == model))
            .Select(BoardItemDrop.ItemBounds)).ToList();
        for (int i = 0; i < batch.Length; i++)
        {
            var point = grid.bounds.center;
            point.x = Mathf.Lerp(grid.bounds.min.x, grid.bounds.max.x, (i + 1f) / (batch.Length + 1f));
            if (!books.TryGetValue(batch[i], out var model)) return;
            var originalRotation = model.rotation;
            var originalScale = model.localScale;
            ShowBook(batch[i], 1);
            model.rotation = Quaternion.identity;
            var shape = BoardItemDrop.ItemBounds(model);
            bool available = BoardItemDrop.TryFindClearPosition(grid, model, point, BoardItemDrop.DefaultLift, blocked, out placements[i]);
            model.rotation = originalRotation;
            model.localScale = originalScale;
            if (!available) return;
            shape.center += placements[i] - model.position;
            blocked.Add(shape);
        }
        for (int i = 0; i < batch.Length; i++)
            DropBook(ShowBook(batch[i], 1), batch[i], grid, placements[i]);
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
        var batch = station.Progress.active.Where(item => item == job || job.comboStudy && item.comboStudy &&
            !MineLearningStation.IsQueuedComboBook(item) && item.boardIndex == 1).ToArray();
        foreach (var item in batch) item.held = true;
        foreach (var item in batch)
            if (books.TryGetValue(item, out var model)) BoardItemDrop.Hold(model.GetComponent<Rigidbody>());
        Vector3 target = book.position;
        Bounds floor = (studyGrid ?? bookBoard).bounds;
        target.x = Mathf.Clamp(target.x, floor.min.x, floor.max.x);
        target.z = Mathf.Clamp(target.z, floor.min.z, floor.max.z);
        target.y = floor.max.y;
        yield return student.FetchBook(target, () => carriedBook = job);
        foreach (var item in batch) station.RememberPlacement(item, .5f, .5f, 2);
        foreach (var item in batch) item.held = false;
        carriedBook = null;
        pickup = null;
    }

    private void DropBook(Transform book, MineLearningJob job, Collider surface, Vector3 position)
    {
        book.rotation = Quaternion.identity;
        book.position = position;
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

    private void UpdateStudyNotice()
    {
        if (studyNotice == null || !studyNotice.isActiveAndEnabled || string.IsNullOrEmpty(studyNotice.text))
        {
            if (studyNoticeBackdrop != null) studyNoticeBackdrop.gameObject.SetActive(false);
            return;
        }

        var rect = studyNotice.rectTransform;
        var parent = (RectTransform)rect.parent;
        if (studyNoticeBackdrop == null)
        {
            studyNoticeBackdrop = ChatBubbleGraphic.CreateNoticeBackdrop(parent, "StudyNoticeBubble", true);
            originalStudyNoticeColor = studyNotice.color;
            foregroundStudyNotice = Instantiate(studyNotice, studyNoticeBackdrop.transform);
            foregroundStudyNotice.color = Color.black;
            foregroundStudyNotice.alignment = TMPro.TextAlignmentOptions.Center;
            foregroundStudyNotice.enableAutoSizing = false;
            foregroundStudyNotice.raycastTarget = false;
            var labelRect = foregroundStudyNotice.rectTransform;
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(.5f, .5f);
            labelRect.pivot = new Vector2(.5f, .5f);
            labelRect.anchoredPosition = Vector2.zero;
        }
        studyNotice.color = Color.clear;
        float width = Mathf.Min(580f, parent.rect.width * .54f);
        if (studyNotice.text != fittedStudyNotice || width != fittedStudyNoticeWidth)
        {
            foregroundStudyNotice.text = studyNotice.text;
            studyNoticeBackdrop.FitNotice(foregroundStudyNotice, width);
            fittedStudyNotice = studyNotice.text;
            fittedStudyNoticeWidth = width;
        }
        bool anchored = studyNoticeBackdrop.FollowSpeaker(studentHead, viewCamera, 10f);
        studyNoticeBackdrop.gameObject.SetActive(anchored);
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
            AddBookCoverDetails(book, job, localBounds);
            books.Add(job, book);
        }
        book.localScale = prefab.transform.localScale * scale;
        return book;
    }

    private void UpdateStudyControls()
    {
        if (overlayRoot == null || viewCamera == null || overlayAnchors.Length < 8 || overlays.Length < 8) return;
        if (refillAnchors == null)
        {
            refillAnchors = new Transform[4];
            refillAreas = new Rect[4];
            var slots = GetComponentsInChildren<Transform>(true).Where(part => part.name == "CoinSlot").ToArray();
            for (int i = 0; i < 4; i++)
            {
                Transform balance = overlayAnchors[i * 2 + 1];
                if (balance == null) continue;
                var slot = slots.OrderBy(part => Mathf.Abs(viewCamera.WorldToViewportPoint(part.position).x -
                    viewCamera.WorldToViewportPoint(balance.position).x)).FirstOrDefault();
                refillAnchors[i] = slot;
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
                if (stockModels[i].Length >= 2)
                {
                    var top = stockModels[i][stockModels[i].Length - 1];
                    var below = stockModels[i][stockModels[i].Length - 2];
                    var extra = Instantiate(top, top.parent);
                    extra.localPosition = top.localPosition + (top.localPosition - below.localPosition);
                    stockModels[i] = stockModels[i].Append(extra).ToArray();
                }
                if (anchor != null) dispenserHoverBounds[i] = BoardItemDrop.ItemBounds(anchor);
            }
        }
        for (int i = 0; i < 4; i++)
        {
            for (int item = 0; item < stockModels[i].Length; item++)
                stockModels[i][item].gameObject.SetActive(item < station.Stock(i));
            if (refillAnchors[i] != null)
            {
                var bounds = BoardItemDrop.ItemBounds(refillAnchors[i]);
                Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
                Vector2 max = new Vector2(float.MinValue, float.MinValue);
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 vertex = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                        (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    Vector2 screen = viewCamera.WorldToScreenPoint(vertex);
                    min = Vector2.Min(min, screen);
                    max = Vector2.Max(max, screen);
                }
                float padding = Screen.height * .008f;
                refillAreas[i] = Rect.MinMaxRect(min.x - padding, min.y - padding, max.x + padding, max.y + padding);
            }
        }
    }

    public Transform FindRefillSlot(Vector2 point, int ability) =>
        refillAreas != null && ability >= 0 && ability < refillAreas.Length && station.CanMoveBooks &&
        refillAreas[ability].Contains(point) ? refillAnchors[ability] : null;

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
