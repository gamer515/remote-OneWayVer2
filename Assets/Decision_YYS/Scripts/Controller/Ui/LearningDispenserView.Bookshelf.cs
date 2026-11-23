using System.Linq;
using UnityEngine;

public sealed partial class LearningDispenserView
{
    [SerializeField] private Transform bookshelfPrefab;
    [SerializeField] private Vector3 bookshelfPosition;
    [SerializeField] private Vector3 bookshelfEulerAngles;
    [SerializeField] private Vector3 bookshelfScale = Vector3.one;
    private Transform bookshelf;
    private Bounds bookshelfBounds;
    private Vector3[] bookshelfSlots;
    private readonly Transform[] codexBooks = new Transform[2];
    private readonly bool[] codexOnBoard = new bool[2];
    private static readonly string[] CodexTitles = { "발견한 책", "발견한 콤보" };
    private MainStoryUi studyCodexUi;
    private int pressedCodex = -1;
    private bool draggingCodex;
    private Pose codexDragOrigin;
    private Vector2 codexPressPoint;
    private float codexPressTime;
    private static readonly Vector3 ShelvedBookScale = new(.54f, .50f, .60f);
    private static readonly Quaternion ShelvedBookRotation = Quaternion.Euler(-28f, 0f, 0f) *
        Quaternion.Euler(0f, 90f, 0f) * Quaternion.Euler(-90f, 0f, 0f);
    // Centers of the openings in the CounterBookshelf mesh, from the plinth up.
    private static readonly float[] BookshelfRowCenters = { .175f, .420f, .665f, .915f };

    private void UpdateBookshelf()
    {
        if (!station.UsesStudyRules || bookBoard == null || viewCamera == null || bookshelfPrefab == null) return;
        if (bookshelf == null) BuildBookshelf();
    }

    private void BuildBookshelf()
    {
        bookshelf = Instantiate(bookshelfPrefab, bookshelfPosition, Quaternion.Euler(bookshelfEulerAngles));
        bookshelf.localScale = bookshelfScale;
        bookshelfBounds = BoardItemDrop.ItemBounds(bookshelf);
        bookshelfBounds.Encapsulate(bookshelfBounds.max + Vector3.up * .1f);
        if (coinSupply != null) coinSupply.BoardShelf = bookshelf;
        bookshelfSlots = new Vector3[MineLearningStation.BookshelfSlots];
        for (int i = 0; i < bookshelfSlots.Length; i++)
        {
            int compartment = i % MineLearningStation.BookshelfCompartments;
            int lane = i / MineLearningStation.BookshelfCompartments;
            float offset = lane == 0 ? 0f : lane == 1 ? -.092f : .092f;
            bookshelfSlots[i] = BookshelfBookPosition(compartment % 2, compartment / 2, offset);
        }

        studyCodexUi = FindFirstObjectByType<MainStoryUi>(FindObjectsInactive.Include);
        for (int i = 0; i < codexBooks.Length; i++)
        {
            var model = codexBooks[i] = Instantiate(learningBookPrefabs[i + 2]).transform;
            model.name = i == 0 ? "DiscoveredBooksCodex" : "DiscoveredCombosCodex";
            model.localScale = Vector3.Scale(model.localScale, ShelvedBookScale);
            foreach (var shape in model.GetComponentsInChildren<Collider>()) shape.enabled = false;
            var cover = new MaterialPropertyBlock();
            cover.SetColor("_BaseColor", i == 0 ? new Color32(145, 92, 172, 255) : new Color32(227, 209, 177, 255));
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>())
                if (renderer.name is "Cover" or "Spine") renderer.SetPropertyBlock(cover);
            AddSpineBands(model);
            PlaceBookshelfBook(model, BookshelfBookPosition(i, 3));
        }
    }

    private Vector3 BookshelfBookPosition(int column, int row, float offset = 0f) =>
        bookshelf.Find("Cabinet").TransformPoint(
            new Vector3((column == 0 ? -.156f : .156f) + offset, BookshelfRowCenters[row], .046f));

    private static void PlaceBookshelfBook(Transform book, Vector3 slot)
    {
        if (book.TryGetComponent<Rigidbody>(out var body)) BoardItemDrop.Hold(body);
        book.SetPositionAndRotation(slot, ShelvedBookRotation);
        book.position += Vector3.up * (slot.y - BoardItemDrop.ItemBounds(book).min.y);
    }

    private bool TryStoreDraggedBook(Vector2 point)
    {
        if (CodexUnderPointer(point) >= 0) return false;
        var candidates = Enumerable.Range(0, bookshelfSlots.Length)
            .OrderBy(i => ((Vector2)viewCamera.WorldToScreenPoint(bookshelfSlots[i]) - point).sqrMagnitude);
        return candidates.Any(i => station.TryStoreBook(draggedJob, i));
    }

    private int CodexUnderPointer(Vector2 point)
    {
        if (viewCamera == null) return -1;
        var ray = viewCamera.ScreenPointToRay(point);
        for (int i = 0; i < codexBooks.Length; i++)
            if (codexBooks[i] != null && BoardItemDrop.ItemBounds(codexBooks[i]).IntersectRay(ray))
                return i;
        return -1;
    }

    private bool HandleCodexPointer(Vector2 point, bool down, bool held, bool up)
    {
        if (down && !IsDraggingBook)
        {
            pressedCodex = CodexUnderPointer(point);
            if (pressedCodex < 0) return false;
            codexPressPoint = point;
            codexPressTime = Time.unscaledTime;
        }
        if (pressedCodex < 0) return false;
        var model = codexBooks[pressedCodex];
        if (!draggingCodex && (point - codexPressPoint).sqrMagnitude >= BookDragDistanceSquared)
        {
            draggingCodex = true;
            codexDragOrigin = new Pose(model.position, model.rotation);
            model.rotation = Quaternion.identity;
        }
        if (held && draggingCodex)
        {
            var ray = viewCamera.ScreenPointToRay(point);
            var plane = new Plane(Vector3.up, new Vector3(0f, bookBoard.bounds.max.y + .23f, 0f));
            if (plane.Raycast(ray, out float distance)) model.position = ray.GetPoint(distance);
        }
        if (up)
        {
            int index = pressedCodex;
            if (draggingCodex) DropCodex(point, index);
            else if (Time.unscaledTime - codexPressTime <= BookPreviewMaxPress && CodexUnderPointer(point) == index)
                studyCodexUi?.OpenStudyCodex(station, index == 0);
            CancelCodexPointer();
        }
        return down || held || up;
    }

    private void DropCodex(Vector2 point, int index)
    {
        var model = codexBooks[index];
        var ray = viewCamera.ScreenPointToRay(point);
        if (bookshelfBounds.IntersectRay(ray))
        {
            codexOnBoard[index] = false;
            PlaceBookshelfBook(model, BookshelfBookPosition(index, 3));
        }
        else
        {
            if (!bookBoard.Raycast(ray, out var hit, viewCamera.farClipPlane)) return;
            var obstacles = BookBoardOccupants().Concat(coinSupply?.BoardCoins ?? Enumerable.Empty<Transform>())
                .Select(BoardItemDrop.ItemBounds).Append(bookshelfBounds);
            if (!BoardItemDrop.TryFindClearPosition(bookBoard, model, hit.point, .01f, obstacles, out var position)) return;
            model.position = position;
            codexOnBoard[index] = true;
        }
        draggingCodex = false;
    }

    private void CancelCodexPointer()
    {
        if (draggingCodex) codexBooks[pressedCodex].SetPositionAndRotation(codexDragOrigin.position, codexDragOrigin.rotation);
        draggingCodex = false;
        pressedCodex = -1;
    }
}
