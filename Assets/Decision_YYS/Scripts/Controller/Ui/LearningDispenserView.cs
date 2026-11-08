using System.Collections.Generic;
using UnityEngine;
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
    private Plane dragPlane;
    private Vector3 grabOffset, dragOrigin;
    private Quaternion dragRotation;
    public int QueuedVisualCount => tiles.Count;
    public int ActiveVisualCount => books.Count;
    public bool IsDraggingBook => draggedBook != null;

    private void Update()
    {
        if(IsDraggingBook && (station == null || !station.CanMoveBooks)) { CancelBookDrag(); return; }
#if ENABLE_INPUT_SYSTEM
        var pointer=Pointer.current;
        if(pointer == null) {CancelBookDrag();return;}
        Vector2 position=pointer.position.ReadValue();
        bool down=pointer.press.wasPressedThisFrame, held=pointer.press.isPressed, up=pointer.press.wasReleasedThisFrame;
#else
        Vector2 position=Input.mousePosition;
        bool down=Input.GetMouseButtonDown(0), held=Input.GetMouseButton(0), up=Input.GetMouseButtonUp(0);
#endif
        if(down) BeginBookDrag(position);
        if(held && IsDraggingBook) MoveBookDrag(position);
        if(up && IsDraggingBook) EndBookDrag(position);
        ShowHoveredBook(position);
    }

    public bool BeginBookDrag(Vector2 screenPosition)
    {
        if(IsDraggingBook || station == null || !station.CanMoveBooks || viewCamera == null || bookBoard == null) return false;
        Ray ray=viewCamera.ScreenPointToRay(screenPosition);
        if (!TryFindBook(ray, out draggedJob, out draggedBook)) return false;
        dragOrigin=draggedBook.position;dragRotation=draggedBook.rotation;
        dragPlane=new Plane(viewCamera.transform.forward,dragOrigin);
        if(!dragPlane.Raycast(ray,out float enter)) {draggedBook=null;draggedJob=null;return false;}
        grabOffset=dragOrigin-ray.GetPoint(enter);draggedJob.held=true;return true;
    }

    private bool TryFindBook(Ray ray, out MineLearningJob job, out Transform book)
    {
        job = null;
        book = null;
        float nearest = float.PositiveInfinity;
        foreach(var pair in books)
        {
            if(pair.Key.transit>0 || pair.Value == null) continue;
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
        if (IsDraggingBook || !TryFindBook(viewCamera.ScreenPointToRay(screenPoint), out var job, out _) ||
            string.IsNullOrEmpty(job.bookName))
        {
            bookHoverLabel.gameObject.SetActive(false);
            return;
        }
        bookHoverLabel.text = job.bookName;
        bookHoverLabel.gameObject.SetActive(true);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(overlayRoot, screenPoint + Vector2.up * 28, null, out var point);
        bookHoverLabel.rectTransform.anchoredPosition = point;
    }

    public void MoveBookDrag(Vector2 screenPosition)
    {
        if(!IsDraggingBook) return;
        Ray ray=viewCamera.ScreenPointToRay(screenPosition);
        if(dragPlane.Raycast(ray,out float enter)) draggedBook.position=ray.GetPoint(enter)+grabOffset;
    }

    public bool EndBookDrag(Vector2 screenPosition)
    {
        if(!IsDraggingBook) return false;
        RaycastHit hit=default;
        bool placed=station.CanMoveBooks && bookBoard.Raycast(viewCamera.ScreenPointToRay(screenPosition),out hit,1000);
        if(placed)
        {
            draggedBook.rotation=Quaternion.identity;
            BoardRange(draggedBook,out var min,out var max,out _);
            station.RememberPlacement(draggedJob,Mathf.InverseLerp(min.x,max.x,hit.point.x),Mathf.InverseLerp(min.z,max.z,hit.point.z));
            PlaceOnBoard(draggedBook,draggedJob);
        }
        else draggedBook.SetPositionAndRotation(dragOrigin,dragRotation);
        draggedJob.held=false;draggedBook=null;draggedJob=null;return placed;
    }

    public void CancelBookDrag()
    {
        if(draggedBook != null) draggedBook.SetPositionAndRotation(dragOrigin,dragRotation);
        if(draggedJob != null) draggedJob.held=false;
        draggedBook=null;draggedJob=null;
    }
    private void OnDisable() => CancelBookDrag();

    private void BoardRange(Transform book,out Vector3 min,out Vector3 max,out float height)
    {
        var renderers=book.GetComponentsInChildren<Renderer>();
        Bounds bounds=new Bounds(book.position,Vector3.zero);
        foreach(var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        Vector3 extents=bounds.extents;
        min=bookBoard.bounds.min+new Vector3(extents.x+.025f,0,extents.z+.025f);
        max=bookBoard.bounds.max-new Vector3(extents.x+.025f,0,extents.z+.025f);
        height=bookBoard.bounds.max.y+extents.y+.015f;
    }
    private void PlaceOnBoard(Transform book,MineLearningJob job)
    {
        book.rotation=Quaternion.identity;
        BoardRange(book,out var min,out var max,out float height);
        book.position=new Vector3(Mathf.Lerp(min.x,max.x,job.boardU),height,Mathf.Lerp(min.z,max.z,job.boardV));
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
        UpdateLearningMeter(progress);
        while(tiles.Count > progress.queued.Count)
        {
            var last=tiles[tiles.Count-1];tiles.RemoveAt(tiles.Count-1);Remove(last);
        }
        while(tiles.Count < progress.queued.Count)
            tiles.Add(Instantiate(queuedTilePrefab,dynamicRoot).transform);
        for(int i=0;i<tiles.Count;i++)
            tiles[i].SetPositionAndRotation(queueBottom.TransformPoint(queueStep*i),queueBottom.rotation);
        finished.Clear();
        foreach(var job in books.Keys) if(!progress.active.Contains(job)) finished.Add(job);
        foreach(var job in finished) {if(job==draggedJob)CancelBookDrag();Remove(books[job]);books.Remove(job);}
        for(int i=0;i<progress.active.Count;i++)
        {
            var job=progress.active[i];
            if(!books.TryGetValue(job,out var book))
            {
                book=Instantiate(learningBookPrefabs[job.ability],dynamicRoot).transform;
                books.Add(job,book);
            }
            if(book==draggedBook) continue;
            if(job.onBoard && bookBoard != null) {PlaceOnBoard(book,job);continue;}
            if(outletPath == null || outletPath.Length<2) continue;
            float t=job.transit>0?Mathf.Clamp01(1-job.transit/station.DeliverySeconds):1;
            float path=t*(outletPath.Length-1);int segment=Mathf.Min((int)path,outletPath.Length-2);
            Vector3 position=Vector3.Lerp(outletPath[segment].position,outletPath[segment+1].position,path-segment);
            if(job.transit<=0)position+=dynamicRoot.TransformVector(activeStep*i);
            book.SetPositionAndRotation(position,transform.rotation);
        }
    }

    private void UpdateLearningMeter(MineLearningProgress progress)
    {
        if (learningMeter == null || learningFill == null || studentHead == null || viewCamera == null || overlayRoot == null) return;
        var job = progress.active.Find(item => item.onBoard && item.transit <= 0);
        bool visible = job != null && studentHead.gameObject.activeInHierarchy;
        learningMeter.gameObject.SetActive(visible);
        if (!visible) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(overlayRoot,
            viewCamera.WorldToScreenPoint(studentHead.position), null, out var point);
        learningMeter.anchoredPosition = point;
        learningFill.anchorMax = new Vector2(Mathf.Clamp01(job.remaining / Mathf.Max(.1f, job.duration)), 1);
    }
    private void Remove(Transform item)
    {
        item.gameObject.SetActive(false);Destroy(item.gameObject);
    }
}
