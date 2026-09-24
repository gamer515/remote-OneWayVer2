using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 3D 가방, 가로 스크롤 인벤토리, 보드 위 월드 아이템의 입출고를 관리합니다.
/// </summary>
public sealed class BackpackInventoryController : MonoBehaviour
{
    private const string BackpackStateName = "BackpackOpen";
    private const string BackpackCloseStateName = "BackpackClose";

    [Header("Backpack")]
    [SerializeField] private Animator backpackAnimator;
    [SerializeField] private Collider backpackCollider;
    [SerializeField] private float animationDuration = 0.8333333f;

    [Header("Inventory UI")]
    [SerializeField] private GameObject inventoryPanel;
    [SerializeField] private CanvasGroup inventoryCanvasGroup;
    [SerializeField] private ScrollRect horizontalScroll;
    [SerializeField] private RectTransform content;
    [SerializeField] private InventorySlotView slotPrefab;
    [SerializeField] private RectTransform dragIconRoot;
    [SerializeField] private UnityEngine.UI.Image dragIcon;

    [Header("World")]
    [SerializeField] private Camera inputCamera;
    [Tooltip("3D 보드를 표시하는 RawImage입니다. 화면 좌표를 보드 카메라 좌표로 변환합니다.")]
    [SerializeField] private RawImage boardViewport;
    [SerializeField] private Collider boardDropArea;
    [SerializeField] private Transform boardSpawnPoint;
    [SerializeField] private InventoryItemDefinition[] itemCatalog;

    [Header("DOTween")]
    [SerializeField, Min(0.01f)] private float uiTweenDuration = 0.22f;
    [SerializeField] private Vector3 hiddenScale = new Vector3(0.9f, 0.9f, 1f);

    private readonly List<string> storedItemIds = new List<string>();
    private readonly Dictionary<string, InventoryItemDefinition> definitions =
        new Dictionary<string, InventoryItemDefinition>(StringComparer.Ordinal);

    private Action<IReadOnlyList<string>> saveInventory;
    private Sequence transition;
    private bool isOpen;
    private bool inputLocked;
    private int draggedSlotIndex = -1;
    private WorldInventoryItem draggedWorldItem;
    private Rigidbody draggedBody;
    private Vector3 draggedOriginalPosition;
    private Quaternion draggedOriginalRotation;
    private Plane dragPlane;

    public bool IsOpen => isOpen;

    private void Awake()
    {
        if (backpackAnimator == null)
            backpackAnimator = GetComponent<Animator>();
        if (backpackCollider == null)
            backpackCollider = GetComponent<Collider>();
        if (inputCamera == null)
            inputCamera = Camera.main;

        BuildCatalog();
        SetClosedImmediately();
    }

    private void OnDisable()
    {
        transition?.Kill();
        transition = null;
        CancelWorldDrag();
    }

    public void Initialize(
        IEnumerable<string> savedItemIds,
        Action<IReadOnlyList<string>> saveCallback)
    {
        saveInventory = saveCallback;
        storedItemIds.Clear();
        if (savedItemIds != null)
        {
            foreach (string id in savedItemIds)
            {
                if (!string.IsNullOrWhiteSpace(id) && definitions.ContainsKey(id))
                    storedItemIds.Add(id);
                else if (!string.IsNullOrWhiteSpace(id))
                    Debug.LogWarning($"인벤토리 카탈로그에 없는 itemId를 건너뜁니다: {id}", this);
            }
        }

        RebuildSlots();
    }

    private void Update()
    {
        ReadPointer(out Vector2 pointerPosition, out bool down, out bool held, out bool up);

        if (down && !inputLocked && !IsInsideInventory(pointerPosition) &&
            TryRaycast(pointerPosition, out RaycastHit hit))
        {
            if (hit.collider == backpackCollider)
            {
                Toggle();
                return;
            }

            WorldInventoryItem worldItem = hit.collider.GetComponentInParent<WorldInventoryItem>();
            if (isOpen && worldItem != null && worldItem.Definition != null)
                BeginWorldDrag(worldItem, pointerPosition);
        }

        if (draggedWorldItem != null && held)
            UpdateWorldDrag(pointerPosition);

        if (draggedWorldItem != null && up)
            EndWorldDrag(pointerPosition);
    }

    public void Toggle()
    {
        if (inputLocked) return;
        if (isOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (isOpen || inputLocked) return;

        isOpen = true;
        inputLocked = true;
        transition?.Kill();
        inventoryPanel.SetActive(false);

        PlayBackpackForward();
        transition = DOTween.Sequence()
            .AppendInterval(animationDuration)
            .AppendCallback(ShowPanel)
            .Append(inventoryCanvasGroup.DOFade(1f, uiTweenDuration))
            .Join(inventoryPanel.transform.DOScale(Vector3.one, uiTweenDuration)
                .SetEase(Ease.OutBack))
            .OnComplete(() => inputLocked = false)
            .SetLink(gameObject);
    }

    public void Close()
    {
        if (!isOpen || inputLocked) return;

        isOpen = false;
        inputLocked = true;
        transition?.Kill();
        CancelWorldDrag();

        transition = DOTween.Sequence()
            .Append(inventoryCanvasGroup.DOFade(0f, uiTweenDuration))
            .Join(inventoryPanel.transform.DOScale(hiddenScale, uiTweenDuration)
                .SetEase(Ease.InBack))
            .AppendCallback(() => inventoryPanel.SetActive(false))
            .AppendCallback(PlayBackpackClose)
            .AppendInterval(animationDuration)
            .AppendCallback(StopAnimator)
            .OnComplete(() => inputLocked = false)
            .SetLink(gameObject);
    }

    public void BeginSlotDrag(int index, InventoryItemDefinition definition, Vector2 screenPosition)
    {
        if (!isOpen || inputLocked || definition == null) return;
        draggedSlotIndex = index;
        dragIcon.sprite = definition.Icon;
        dragIcon.enabled = definition.Icon != null;
        dragIconRoot.gameObject.SetActive(true);
        UpdateSlotDrag(screenPosition);
    }

    public void UpdateSlotDrag(Vector2 screenPosition)
    {
        if (draggedSlotIndex < 0) return;
        dragIconRoot.position = screenPosition;
    }

    public void EndSlotDrag(Vector2 screenPosition)
    {
        int index = draggedSlotIndex;
        draggedSlotIndex = -1;
        dragIconRoot.gameObject.SetActive(false);

        if (index < 0 || index >= storedItemIds.Count ||
            !TryRaycastBoard(screenPosition, out Vector3 spawnPosition))
            return;

        string itemId = storedItemIds[index];
        if (!definitions.TryGetValue(itemId, out InventoryItemDefinition definition) ||
            definition.WorldPrefab == null)
            return;

        GameObject instance = Instantiate(
            definition.WorldPrefab,
            spawnPosition,
            Quaternion.identity);
        WorldInventoryItem worldItem = instance.GetComponent<WorldInventoryItem>();
        if (worldItem == null)
            Debug.LogWarning($"{definition.WorldPrefab.name}에 WorldInventoryItem이 없습니다.", instance);

        storedItemIds.RemoveAt(index);
        SaveAndRefresh();
    }

    private void BeginWorldDrag(WorldInventoryItem item, Vector2 screenPosition)
    {
        draggedWorldItem = item;
        draggedOriginalPosition = item.transform.position;
        draggedOriginalRotation = item.transform.rotation;
        draggedBody = item.GetComponent<Rigidbody>();
        if (draggedBody != null)
        {
            draggedBody.linearVelocity = Vector3.zero;
            draggedBody.angularVelocity = Vector3.zero;
            draggedBody.isKinematic = true;
        }

        float planeHeight = boardDropArea != null
            ? boardDropArea.bounds.max.y + 0.1f
            : item.transform.position.y;
        dragPlane = new Plane(Vector3.up, new Vector3(0f, planeHeight, 0f));
        UpdateWorldDrag(screenPosition);
    }

    private void UpdateWorldDrag(Vector2 screenPosition)
    {
        if (draggedWorldItem == null || inputCamera == null) return;
        Ray ray = inputCamera.ScreenPointToRay(screenPosition);
        if (dragPlane.Raycast(ray, out float distance))
            draggedWorldItem.transform.position = ray.GetPoint(distance);
    }

    private void EndWorldDrag(Vector2 screenPosition)
    {
        if (IsInsideInventory(screenPosition))
        {
            storedItemIds.Add(draggedWorldItem.Definition.ItemId);
            Destroy(draggedWorldItem.gameObject);
            draggedWorldItem = null;
            draggedBody = null;
            SaveAndRefresh();
            return;
        }

        CancelWorldDrag();
    }

    private void CancelWorldDrag()
    {
        if (draggedWorldItem == null) return;
        draggedWorldItem.transform.SetPositionAndRotation(
            draggedOriginalPosition, draggedOriginalRotation);
        if (draggedBody != null)
            draggedBody.isKinematic = false;
        draggedWorldItem = null;
        draggedBody = null;
    }

    private void SaveAndRefresh()
    {
        RebuildSlots();
        saveInventory?.Invoke(storedItemIds);
    }

    private void RebuildSlots()
    {
        if (content == null || slotPrefab == null) return;
        for (int i = content.childCount - 1; i >= 0; i--)
            Destroy(content.GetChild(i).gameObject);

        for (int i = 0; i < storedItemIds.Count; i++)
        {
            if (!definitions.TryGetValue(storedItemIds[i], out InventoryItemDefinition definition))
                continue;
            Instantiate(slotPrefab, content).Bind(this, definition, i);
        }

        // 아이템이 적어도 인벤토리의 가로 폭은 빈 슬롯으로 채웁니다.
        // 아이템이 폭을 넘기면 마지막 빈 슬롯까지 Content가 늘어나 가로 스크롤됩니다.
        int minimumSlotCount = CalculateMinimumVisibleSlotCount();
        int emptySlotCount = Mathf.Max(1, minimumSlotCount - storedItemIds.Count);
        for (int i = 0; i < emptySlotCount; i++)
            Instantiate(slotPrefab, content).Bind(this, null, -1);

        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
    }

    private int CalculateMinimumVisibleSlotCount()
    {
        RectTransform viewport = horizontalScroll != null
            ? horizontalScroll.viewport
            : null;
        RectTransform slotRect = slotPrefab.transform as RectTransform;
        HorizontalLayoutGroup layout = content.GetComponent<HorizontalLayoutGroup>();
        if (viewport == null || slotRect == null)
            return 1;

        float slotWidth = slotRect.rect.width;
        float spacing = layout != null ? layout.spacing : 0f;
        float horizontalPadding = layout != null
            ? layout.padding.left + layout.padding.right
            : 0f;
        float availableWidth = Mathf.Max(0f, viewport.rect.width - horizontalPadding);
        return Mathf.Max(1, Mathf.FloorToInt((availableWidth + spacing) / (slotWidth + spacing)));
    }

    private void BuildCatalog()
    {
        definitions.Clear();
        if (itemCatalog == null) return;
        foreach (InventoryItemDefinition definition in itemCatalog)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.ItemId)) continue;
            definitions[definition.ItemId] = definition;
        }
    }

    private void ShowPanel()
    {
        RebuildSlots();
        inventoryCanvasGroup.alpha = 0f;
        inventoryPanel.transform.localScale = hiddenScale;
        inventoryPanel.SetActive(true);
    }

    private void SetClosedImmediately()
    {
        transition?.Kill();
        isOpen = false;
        inputLocked = false;
        if (inventoryPanel != null)
        {
            inventoryPanel.SetActive(false);
            inventoryPanel.transform.localScale = hiddenScale;
        }
        if (inventoryCanvasGroup != null)
            inventoryCanvasGroup.alpha = 0f;
        if (dragIconRoot != null)
            dragIconRoot.gameObject.SetActive(false);

        if (backpackAnimator != null)
        {
            backpackAnimator.speed = 0f;
            backpackAnimator.Play(BackpackStateName, 0, 0f);
            backpackAnimator.Update(0f);
        }
    }

    private void PlayBackpackForward()
    {
        backpackAnimator.speed = 1f;
        backpackAnimator.Play(BackpackStateName, 0, 0f);
    }

    private void PlayBackpackClose()
    {
        backpackAnimator.speed = 1f;
        backpackAnimator.Play(BackpackCloseStateName, 0, 0f);
    }

    private void StopAnimator()
    {
        if (backpackAnimator != null)
            backpackAnimator.speed = 0f;
    }

    private bool IsInsideInventory(Vector2 screenPosition)
    {
        if (!isOpen || inventoryPanel == null) return false;
        RectTransform rect = inventoryPanel.transform as RectTransform;
        return rect != null && RectTransformUtility.RectangleContainsScreenPoint(
            rect, screenPosition, GetUiCamera(rect));
    }

    private bool TryRaycastBoard(Vector2 screenPosition, out Vector3 position)
    {
        position = boardSpawnPoint != null ? boardSpawnPoint.position : Vector3.zero;
        if (boardDropArea == null || !TryRaycast(screenPosition, out RaycastHit hit))
            return false;
        if (hit.collider != boardDropArea && !hit.collider.transform.IsChildOf(boardDropArea.transform))
            return false;
        position = hit.point + Vector3.up * 0.15f;
        return true;
    }

    private bool TryRaycast(Vector2 screenPosition, out RaycastHit hit)
    {
        hit = default;
        return TryCreateWorldRay(screenPosition, out Ray ray) && Physics.Raycast(
            ray, out hit, 1000f,
            inputCamera.cullingMask, QueryTriggerInteraction.Ignore);
    }

    private bool TryCreateWorldRay(Vector2 screenPosition, out Ray ray)
    {
        if (inputCamera == null)
        {
            ray = default;
            return false;
        }

        if (boardViewport == null)
        {
            ray = inputCamera.ScreenPointToRay(screenPosition);
            return true;
        }

        RectTransform viewportRect = boardViewport.rectTransform;
        Canvas canvas = boardViewport.canvas;
        Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera : null;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                viewportRect, screenPosition, uiCamera, out Vector2 localPoint) ||
            !viewportRect.rect.Contains(localPoint))
        {
            ray = default;
            return false;
        }

        Vector2 viewportPosition =
            (localPoint - viewportRect.rect.min) / viewportRect.rect.size;
        ray = inputCamera.ViewportPointToRay(viewportPosition);
        return true;
    }

    private static Camera GetUiCamera(RectTransform rect)
    {
        Canvas canvas = rect.GetComponentInParent<Canvas>();
        return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera : null;
    }

    private static void ReadPointer(
        out Vector2 position, out bool down, out bool held, out bool up)
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current == null)
        {
            position = Vector2.zero;
            down = held = up = false;
            return;
        }
        position = Mouse.current.position.ReadValue();
        down = Mouse.current.leftButton.wasPressedThisFrame;
        held = Mouse.current.leftButton.isPressed;
        up = Mouse.current.leftButton.wasReleasedThisFrame;
#else
        position = Input.mousePosition;
        down = Input.GetMouseButtonDown(0);
        held = Input.GetMouseButton(0);
        up = Input.GetMouseButtonUp(0);
#endif
    }
}
