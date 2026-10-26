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
    private const string LegacyBackpackOpen = "Backpack_OpenLid";
    private const string LegacyBackpackClose = "Backpack_CloseLid";

    [Header("Backpack")]
    [SerializeField] private Animator backpackAnimator;
    [SerializeField, HideInInspector] private Animation backpackAnimation;
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

    private readonly Dictionary<string, WorldInventoryItem> worldItems = new Dictionary<string, WorldInventoryItem>();
    private readonly Dictionary<string, WorldInventoryItemState> worldStates = new Dictionary<string, WorldInventoryItemState>();
    private Action<InventoryState> saveInventory;
    private bool initialized;
    private float nextPoseSave;
    private string lastSavedInventory;
    private Sequence transition;
    private bool isOpen;
    private bool inputLocked;
    private int draggedSlotIndex = -1;
    private WorldInventoryItem draggedWorldItem;
    private Rigidbody draggedBody;
    private bool draggedWasKinematic;
    private Vector3 draggedOriginalPosition;
    private Quaternion draggedOriginalRotation;
    private Plane dragPlane;

    public bool IsOpen => isOpen;

    private void Awake()
    {
        if (backpackAnimator == null && backpackAnimation == null)
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
        SaveNow();
    }

    private void OnApplicationPause(bool paused) { if (paused) SaveNow(); }
    private void OnApplicationQuit() { CancelWorldDrag(); SaveNow(); }

    public void Initialize(
        IEnumerable<string> savedItemIds,
        Action<IReadOnlyList<string>> saveCallback)
    {
        Initialize(new InventoryState { storedItemIds = new List<string>(savedItemIds ?? Array.Empty<string>()) },
            saveCallback == null ? null : state => saveCallback(state.storedItemIds));
    }

    public void Initialize(InventoryState state, Action<InventoryState> saveCallback)
    {
        CancelWorldDrag();
        state ??= new InventoryState();
        saveInventory = saveCallback;
        storedItemIds.Clear();
        if (state.storedItemIds != null)
        {
            foreach (string id in state.storedItemIds)
            {
                if (!string.IsNullOrWhiteSpace(id))
                    storedItemIds.Add(id);
            }
        }
        var wanted = new HashSet<string>();
        foreach (var saved in state.worldItems ?? new List<WorldInventoryItemState>())
        {
            if (saved == null || string.IsNullOrWhiteSpace(saved.itemId)) continue;
            string id = string.IsNullOrWhiteSpace(saved.instanceId) ? Guid.NewGuid().ToString("N") : saved.instanceId;
            if (!wanted.Add(id)) continue;
            worldStates[id] = new WorldInventoryItemState {instanceId=id,itemId=saved.itemId,
                localPosition=saved.localPosition,localRotation=saved.localRotation};
            if (worldItems.TryGetValue(id,out var existing) && existing != null && existing.Definition?.ItemId == saved.itemId) continue;
            if (existing != null) DestroyItem(existing.gameObject);
            worldItems.Remove(id);
            if (!definitions.TryGetValue(saved.itemId,out var definition)) continue; // Retain unknown catalog IDs for later recovery.
            Quaternion rotation = saved.localRotation;
            if (rotation.x*rotation.x+rotation.y*rotation.y+rotation.z*rotation.z+rotation.w*rotation.w < .001f) rotation=Quaternion.identity;
            var instance = Finite(saved.localPosition) && Finite(new Vector3(rotation.x,rotation.y,rotation.z)) && !float.IsNaN(rotation.w) && !float.IsInfinity(rotation.w) ?
                Spawn(definition,InventoryAnchor.TransformPoint(saved.localPosition),InventoryAnchor.rotation*rotation.normalized) : null;
            if (instance != null) worldItems[id]=instance;
            else {storedItemIds.Add(saved.itemId);worldStates.Remove(id);wanted.Remove(id);}
        }
        foreach (string id in new List<string>(worldStates.Keys))
            if (!wanted.Contains(id)) worldStates.Remove(id);
        foreach (string id in new List<string>(worldItems.Keys))
            if (!wanted.Contains(id)) {if(worldItems[id]!=null)DestroyItem(worldItems[id].gameObject);worldItems.Remove(id);}
        initialized = true;
        lastSavedInventory = JsonUtility.ToJson(state);
        RebuildSlots();
    }

    private Transform InventoryAnchor => boardDropArea != null ? boardDropArea.transform : transform;
    private static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsInfinity(v.x) &&
        !float.IsNaN(v.y) && !float.IsInfinity(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.z);
    private static void DestroyItem(GameObject item)
    {
        if (Application.isPlaying) Destroy(item); else DestroyImmediate(item);
    }
    private WorldInventoryItem Spawn(InventoryItemDefinition definition, Vector3 position, Quaternion rotation)
    {
        if (definition.WorldPrefab == null || !Finite(position)) return null;
        var instance = Instantiate(definition.WorldPrefab,position,rotation);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(instance,gameObject.scene);
        var item = instance.GetComponent<WorldInventoryItem>();
        if (item != null && item.Definition?.ItemId == definition.ItemId) return item;
        DestroyItem(instance);
        Debug.LogWarning("월드 아이템의 정의가 올바르지 않아 가방에 보존합니다: " + definition.ItemId,this);
        return null;
    }
    public InventoryState CaptureState()
    {
        var state = new InventoryState {storedItemIds = new List<string>(storedItemIds)};
        foreach (var pair in worldStates)
        {
            var saved = pair.Value;
            if (worldItems.TryGetValue(pair.Key,out var item) && item != null)
            {
                Vector3 position = item == draggedWorldItem ? draggedOriginalPosition : item.transform.position;
                Quaternion rotation = item == draggedWorldItem ? draggedOriginalRotation : item.transform.rotation;
                if (Finite(position))
                {
                    saved.localPosition = InventoryAnchor.InverseTransformPoint(position);
                    saved.localRotation = Quaternion.Inverse(InventoryAnchor.rotation)*rotation;
                }
            }
            state.worldItems.Add(new WorldInventoryItemState {instanceId=saved.instanceId,itemId=saved.itemId,
                localPosition=saved.localPosition,localRotation=saved.localRotation});
        }
        return state;
    }
    public bool SaveNow()
    {
        if (!initialized || saveInventory == null) return true;
        var state=CaptureState(); string json=JsonUtility.ToJson(state);
        if (json==lastSavedInventory) return true;
        try {saveInventory(state);lastSavedInventory=json;return true;}
        catch (Exception e) {Debug.LogWarning("아이템 저장 실패. 다음 저장에서 다시 시도합니다: "+e.Message,this);return false;}
    }

    private void Update()
    {
        if (initialized && Time.unscaledTime >= nextPoseSave)
        {
            nextPoseSave=Time.unscaledTime+.5f;
            SaveNow();
        }
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

        TryDeploySlot(index,spawnPosition);
    }

    public bool TryDeploySlot(int index, Vector3 spawnPosition)
    {
        if (index < 0 || index >= storedItemIds.Count) return false;
        string itemId = storedItemIds[index];
        if (!definitions.TryGetValue(itemId, out InventoryItemDefinition definition) ||
            definition.WorldPrefab == null)
            return false;
        var item = Spawn(definition,spawnPosition,Quaternion.identity);
        if (item == null) return false;
        string id=Guid.NewGuid().ToString("N");
        worldItems[id]=item;
        worldStates[id]=new WorldInventoryItemState {instanceId=id,itemId=itemId,
            localPosition=InventoryAnchor.InverseTransformPoint(spawnPosition),localRotation=Quaternion.Inverse(InventoryAnchor.rotation)};
        storedItemIds.RemoveAt(index);
        if (!SaveNow())
        {
            storedItemIds.Insert(index,itemId);worldItems.Remove(id);worldStates.Remove(id);DestroyItem(item.gameObject);return false;
        }
        RebuildSlots(); return true;
    }

    private void BeginWorldDrag(WorldInventoryItem item, Vector2 screenPosition)
    {
        draggedWorldItem = item;
        draggedOriginalPosition = item.transform.position;
        draggedOriginalRotation = item.transform.rotation;
        draggedBody = item.GetComponent<Rigidbody>();
        if (draggedBody != null)
        {
            draggedWasKinematic = draggedBody.isKinematic;
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
        if (IsInsideInventory(screenPosition) && TryStoreWorldItem(draggedWorldItem)) return;

        CancelWorldDrag();
    }

    public bool TryStoreWorldItem(WorldInventoryItem item)
    {
        if (item == null || !item.gameObject.activeSelf || item.Definition == null || !definitions.ContainsKey(item.Definition.ItemId)) return false;
        string id=null;
        foreach (var pair in worldItems) if(pair.Value==item) {id=pair.Key;break;}
        WorldInventoryItemState saved=id==null?null:worldStates[id];
        if(id!=null) {worldItems.Remove(id);worldStates.Remove(id);}
        storedItemIds.Add(item.Definition.ItemId);
        if(!SaveNow())
        {
            storedItemIds.RemoveAt(storedItemIds.Count-1);
            if(id!=null) {worldItems[id]=item;worldStates[id]=saved;}
            return false;
        }
        if(draggedWorldItem==item) {draggedWorldItem=null;draggedBody=null;}
        item.gameObject.SetActive(false);DestroyItem(item.gameObject);RebuildSlots();return true;
    }

    private void CancelWorldDrag()
    {
        if (draggedWorldItem == null) return;
        draggedWorldItem.transform.SetPositionAndRotation(
            draggedOriginalPosition, draggedOriginalRotation);
        if (draggedBody != null)
            draggedBody.isKinematic = draggedWasKinematic;
        draggedWorldItem = null;
        draggedBody = null;
    }

    private void RebuildSlots()
    {
        if (content == null || slotPrefab == null) return;
        for (int i = content.childCount - 1; i >= 0; i--)
            DestroyItem(content.GetChild(i).gameObject);

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
        // Authored resource items also work in existing scenes without another serialized catalog copy.
        foreach (InventoryItemDefinition definition in Resources.LoadAll<InventoryItemDefinition>("Inventory"))
            if (definition != null && !string.IsNullOrWhiteSpace(definition.ItemId)) definitions[definition.ItemId] = definition;
        foreach (InventoryItemDefinition definition in itemCatalog ?? Array.Empty<InventoryItemDefinition>())
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

        if (backpackAnimation != null)
        {
            backpackAnimation.Stop();
            AnimationState closed = backpackAnimation[LegacyBackpackClose];
            if (closed != null)
            {
                closed.enabled = true;
                closed.weight = 1f;
                closed.normalizedTime = 1f;
                backpackAnimation.Sample();
                closed.enabled = false;
            }
        }
        else if (backpackAnimator != null)
        {
            backpackAnimator.speed = 0f;
            backpackAnimator.Play(BackpackStateName, 0, 0f);
            backpackAnimator.Update(0f);
        }
    }

    private void PlayBackpackForward()
    {
        if (backpackAnimation != null) { backpackAnimation.Play(LegacyBackpackOpen); return; }
        if (backpackAnimator == null) return;
        backpackAnimator.speed = 1f;
        backpackAnimator.Play(BackpackStateName, 0, 0f);
    }

    private void PlayBackpackClose()
    {
        if (backpackAnimation != null) { backpackAnimation.Play(LegacyBackpackClose); return; }
        if (backpackAnimator == null) return;
        backpackAnimator.speed = 1f;
        backpackAnimator.Play(BackpackCloseStateName, 0, 0f);
    }

    private void StopAnimator()
    {
        if (backpackAnimation != null) backpackAnimation.Stop();
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
