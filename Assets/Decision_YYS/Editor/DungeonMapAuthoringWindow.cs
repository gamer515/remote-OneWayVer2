using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

/// <summary>Chunk authoring UI using the existing Dungeon prefab brush.</summary>
public sealed class DungeonMapAuthoringWindow : EditorWindow
{
    private static readonly string[] Layers = { "FloorTiles", "Structures", "Nature", "Decorations" };
    private const string DefaultFolder = "Assets/UnityAssets/Buildings_And_Terrains/Dungeon_FBX format";
    private const string PackRoot = "Assets/UnityAssets/Buildings_And_Terrains";
    [SerializeField] private GameObject chunk;
    [SerializeField] private Grid targetGrid;
    [SerializeField] private GameObject selectedPrefab;
    [SerializeField] private Vector2 chunkSize = new Vector2(50f, 120f);
    [SerializeField] private Vector3 origin;
    [SerializeField] private float cellSize = 2.8f;
    [SerializeField] private Material groundMaterial;
    [SerializeField] private int floor = 1;
    [SerializeField] private float floorHeight = 4f;
    [SerializeField] private int layer;
    [SerializeField] private Vector3 placementScale = Vector3.one;
    [SerializeField] private float heightOffset;
    [SerializeField] private int quarterTurns;
    [SerializeField] private string assetFolder = DefaultFolder;
    private DungeonRandomGameObjectBrush brush;
    private bool painting;
    private bool stroking;
    private bool erasing;
    private int strokeUndo;
    private Vector3Int? previousCell;
    private readonly HashSet<Vector3Int> visited = new HashSet<Vector3Int>();
    private ObjectField gridField;
    private ObjectField prefabField;
    private Label status;
    private VisualElement catalog;
    private PopupField<string> packField;
    private ObjectField folderField;
    private TextField packNameField;
    private ToolbarSearchField searchField;
    private List<DungeonMapAssetPackSettings.Entry> availablePacks;
    private IVisualElementScheduledItem keyboardSchedule;

    [MenuItem("Tools/Dungeon/3D Map Authoring")]
    public static void Open() => GetWindow<DungeonMapAuthoringWindow>("3D Map Authoring");

    private void OnEnable()
    {
        brush = CreateInstance<DungeonRandomGameObjectBrush>();
        brush.hideFlags = HideFlags.HideAndDontSave;
        SceneView.duringSceneGui += DuringSceneGUI;
        string savedFolder = AssetDatabase.GUIDToAssetPath(DungeonMapAssetPackSettings.instance.selectedFolderGuid);
        if (AssetDatabase.IsValidFolder(savedFolder)) assetFolder = savedFolder;
    }

    private void OnDisable()
    {
        FinishStroke();
        SceneView.duringSceneGui -= DuringSceneGUI;
        if (brush != null) DestroyImmediate(brush);
        SceneView.RepaintAll();
    }

    public void CreateGUI()
    {
        keyboardSchedule?.Pause();
        rootVisualElement.Clear();
        var stylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Decision_YYS/Editor/DungeonMapAuthoringWindow.uss");
        if (stylesheet != null && !rootVisualElement.styleSheets.Contains(stylesheet)) rootVisualElement.styleSheets.Add(stylesheet);
        var scroll = new ScrollView();
        scroll.AddToClassList("map-scroll");
        rootVisualElement.Add(scroll);
        scroll.Add(new HelpBox("청크 생성 → 층/레이어 준비 → 타일 선택 → 배치 모드 켜기 → Scene에서 클릭·드래그. Shift: 지우기 / R: 90도 회전 / Esc: 배치 종료 / Ctrl+Z: 실행 취소", HelpBoxMessageType.Info));

        var chunkSection = new Foldout { text = "1. 청크 / 그라운드", value = true };
        scroll.Add(chunkSection);
        var chunkField = new ObjectField("작업 청크") { objectType = typeof(GameObject), allowSceneObjects = true, value = chunk };
        chunkField.RegisterValueChangedCallback(e => { FinishStroke(); chunk = (GameObject)e.newValue; targetGrid = null; gridField?.SetValueWithoutNotify(null); });
        chunkSection.Add(chunkField);
        var sizeField = new Vector2Field("크기 X / Z (units)") { value = chunkSize };
        sizeField.RegisterValueChangedCallback(e => chunkSize = e.newValue);
        chunkSection.Add(sizeField);
        var originField = new Vector3Field("새 청크 위치") { value = origin };
        originField.RegisterValueChangedCallback(e => origin = e.newValue);
        chunkSection.Add(originField);
        var cellField = new FloatField("새 청크 타일 간격") { value = cellSize };
        cellField.RegisterValueChangedCallback(e => cellSize = e.newValue);
        chunkSection.Add(cellField);
        var materialField = new ObjectField("그라운드 재질 (선택)") { objectType = typeof(Material), allowSceneObjects = false, value = groundMaterial };
        materialField.RegisterValueChangedCallback(e => groundMaterial = (Material)e.newValue);
        chunkSection.Add(materialField);
        chunkSection.Add(new Button(() =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            try
            {
                chunk = CreateChunk(chunkSize, cellSize, origin, groundMaterial);
                chunkField.SetValueWithoutNotify(chunk);
                floor = 1;
                PrepareLayer();
                Selection.activeGameObject = chunk;
                SceneView.lastActiveSceneView?.FrameSelected();
                SetStatus("그라운드가 생성되었습니다. 타일을 선택해 배치하세요.");
            }
            catch (ArgumentException e) { SetStatus(e.Message); }
        }) { text = "새 청크 + 평평한 그라운드 생성" });

        var placement = new Foldout { text = "2. 층 / 편집 레이어 / 배치", value = true };
        scroll.Add(placement);
        var floorField = new IntegerField("층 (1층부터)") { value = floor };
        floorField.RegisterValueChangedCallback(e => { FinishStroke(); floor = Mathf.Max(1, e.newValue); floorField.SetValueWithoutNotify(floor); ClearTarget(); });
        placement.Add(floorField);
        var floorHeightField = new FloatField("층간 높이 (units)") { value = floorHeight };
        floorHeightField.RegisterValueChangedCallback(e => { floorHeight = e.newValue; ClearTarget(); });
        placement.Add(floorHeightField);
        var layerField = new PopupField<string>("레이어", new List<string> { "바닥 타일", "건물", "자연", "장식" }, layer);
        layerField.RegisterValueChangedCallback(e => { layer = layerField.index; ClearTarget(); });
        placement.Add(layerField);
        placement.Add(new HelpBox("층/레이어마다 독립된 Grid를 사용합니다. 전역 Unity Layer 설정은 변경하지 않습니다. 기존 층은 처음 만든 높이를 유지합니다.", HelpBoxMessageType.None));
        placement.Add(new Button(PrepareLayer) { text = "선택한 층 / 레이어 준비" });
        gridField = new ObjectField("배치 대상 Grid") { objectType = typeof(Grid), allowSceneObjects = true, value = targetGrid };
        gridField.RegisterValueChangedCallback(e => { FinishStroke(); targetGrid = (Grid)e.newValue; });
        placement.Add(gridField);
        placement.Add(new HelpBox("기존 툴의 FloorTiles / Structures / Decorations Grid도 위 칸에 지정해 사용할 수 있습니다.", HelpBoxMessageType.None));
        var scaleField = new Vector3Field("크기 배율 X / Y / Z") { value = placementScale };
        scaleField.RegisterValueChangedCallback(e => placementScale = e.newValue);
        placement.Add(scaleField);
        var heightField = new FloatField("층 바닥에서 높이 오프셋") { value = heightOffset };
        heightField.RegisterValueChangedCallback(e => heightOffset = e.newValue);
        placement.Add(heightField);
        var rotationField = new PopupField<string>("회전", new List<string> { "0°", "90°", "180°", "270°" }, quarterTurns);
        rotationField.RegisterValueChangedCallback(e => quarterTurns = rotationField.index);
        placement.Add(rotationField);
        var paintToggle = new Toggle("Scene 배치 모드") { value = painting };
        paintToggle.RegisterValueChangedCallback(e => { FinishStroke(); painting = e.newValue; SceneView.RepaintAll(); });
        placement.Add(paintToggle);
        // Keep keyboard changes visible without rebuilding the window during a stroke.
        keyboardSchedule = rootVisualElement.schedule.Execute(() =>
        {
            paintToggle.SetValueWithoutNotify(painting);
            rotationField.SetValueWithoutNotify(new[] { "0°", "90°", "180°", "270°" }[quarterTurns]);
        }).Every(150);

        var palette = new Foldout { text = "3. 타일 / 오브젝트 선택", value = true };
        scroll.Add(palette);
        prefabField = new ObjectField("선택한 타일 / 프리팹") { objectType = typeof(GameObject), allowSceneObjects = false, value = selectedPrefab };
        prefabField.RegisterValueChangedCallback(e => selectedPrefab = (GameObject)e.newValue);
        palette.Add(prefabField);
        packField = new PopupField<string>("에셋 팩", new List<string> { "팩 선택" }, 0);
        packField.RegisterValueChangedCallback(e =>
        {
            if (availablePacks == null || packField.index < 0 || packField.index >= availablePacks.Count) return;
            SelectFolder(availablePacks[packField.index].FolderPath);
        });
        palette.Add(packField);
        palette.Add(new Button(RefreshPackChoices) { text = "에셋 팩 목록 새로고침" });
        folderField = new ObjectField("에셋 폴더") { objectType = typeof(DefaultAsset), allowSceneObjects = false, value = AssetDatabase.LoadAssetAtPath<DefaultAsset>(assetFolder) };
        folderField.RegisterValueChangedCallback(e =>
        {
            string path = AssetDatabase.GetAssetPath(e.newValue);
            if (!AssetDatabase.IsValidFolder(path))
            {
                folderField.SetValueWithoutNotify(AssetDatabase.LoadAssetAtPath<DefaultAsset>(assetFolder));
                SetStatus("프로젝트의 폴더를 선택하세요.");
                return;
            }
            SelectFolder(path);
            RefreshPackChoices();
        });
        palette.Add(folderField);
        var registration = new Foldout { text = "사용자 에셋 팩 등록 / 변경", value = false };
        packNameField = new TextField("팩 이름") { value = FolderLabel(assetFolder) };
        registration.Add(packNameField);
        registration.Add(new HelpBox("에셋 폴더를 지정하고 이름을 입력해 등록하세요. 등록한 팩은 목록에서 전환할 수 있습니다. 같은 폴더를 다시 등록하면 표시 이름이 변경됩니다.", HelpBoxMessageType.None));
        registration.Add(new Button(() =>
        {
            if (!DungeonMapAssetPackSettings.instance.RegisterFolder(assetFolder, packNameField.value))
            { SetStatus("유효한 폴더와 팩 이름을 지정하세요."); return; }
            RefreshPackChoices();
            SetStatus("사용자 팩 등록 / 이름 변경 완료. 원본 에셋은 그대로 사용합니다.");
        }) { text = "현재 폴더를 팩으로 등록 / 이름 변경" });
        registration.Add(new Button(() =>
        {
            DungeonMapAssetPackSettings.instance.UnregisterFolder(assetFolder);
            var remaining = DiscoverPacks();
            if (!remaining.Exists(p => p.FolderPath == assetFolder) && remaining.Count > 0)
                SelectFolder(remaining[0].FolderPath);
            RefreshPackChoices();
            SetStatus("사용자 등록만 해제했습니다. 자동 검색 폴더는 목록에 계속 표시됩니다.");
        }) { text = "현재 폴더의 사용자 팩 등록 해제" });
        palette.Add(registration);
        searchField = new ToolbarSearchField();
        searchField.RegisterValueChangedCallback(e => RefreshCatalog(e.newValue));
        palette.Add(searchField);
        catalog = new VisualElement();
        catalog.AddToClassList("tile-catalog");
        palette.Add(catalog);
        status = new Label("배치 모드를 켜면 Scene에서 배치 위치와 크기 미리보기가 표시됩니다.");
        scroll.Add(status);
        RefreshPackChoices();
        RefreshCatalog("");
    }

    private void OnProjectChange()
    {
        if (packField == null) return;
        string resolved = AssetDatabase.GUIDToAssetPath(DungeonMapAssetPackSettings.instance.selectedFolderGuid);
        if (AssetDatabase.IsValidFolder(resolved)) assetFolder = resolved;
        RefreshPackChoices();
        RefreshCatalog(searchField?.value ?? "");
    }

    public static List<DungeonMapAssetPackSettings.Entry> DiscoverPacks()
    {
        var result = new List<DungeonMapAssetPackSettings.Entry>();
        if (AssetDatabase.IsValidFolder(PackRoot))
            foreach (string path in AssetDatabase.GetSubFolders(PackRoot))
                result.Add(new DungeonMapAssetPackSettings.Entry { folderGuid = AssetDatabase.AssetPathToGUID(path), displayName = FolderLabel(path) });
        foreach (var saved in DungeonMapAssetPackSettings.instance.entries)
        {
            if (!AssetDatabase.IsValidFolder(saved.FolderPath)) continue;
            result.RemoveAll(entry => entry.folderGuid == saved.folderGuid);
            result.Add(saved);
        }
        result.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.displayName, b.displayName));
        return result;
    }

    private static string FolderLabel(string path)
    {
        string name = System.IO.Path.GetFileName(path.TrimEnd('/'));
        string normalized = name.Replace("_FBX format", "").Replace("_FBX", "");
        if (string.Equals(normalized, "Dungeon", StringComparison.OrdinalIgnoreCase)) return "던전";
        if (string.Equals(normalized, "Castle", StringComparison.OrdinalIgnoreCase)) return "캐슬";
        return normalized;
    }

    private void RefreshPackChoices()
    {
        if (packField == null) return;
        availablePacks = DiscoverPacks();
        if (AssetDatabase.IsValidFolder(assetFolder) && !availablePacks.Exists(p => p.FolderPath == assetFolder))
            availablePacks.Add(new DungeonMapAssetPackSettings.Entry { folderGuid = AssetDatabase.AssetPathToGUID(assetFolder), displayName = FolderLabel(assetFolder) });
        var choices = new List<string>();
        int selected = -1;
        for (int i = 0; i < availablePacks.Count; i++)
        {
            var pack = availablePacks[i];
            choices.Add($"{pack.displayName} — {pack.FolderPath}");
            if (pack.FolderPath == assetFolder) selected = i;
        }
        if (choices.Count == 0) choices.Add("에셋 폴더를 지정하세요");
        packField.choices = choices;
        packField.SetValueWithoutNotify(selected >= 0 ? choices[selected] : "에셋 폴더를 지정하세요");
        folderField?.SetValueWithoutNotify(AssetDatabase.LoadAssetAtPath<DefaultAsset>(assetFolder));
        if (selected >= 0) packNameField?.SetValueWithoutNotify(availablePacks[selected].displayName);
    }

    private void SelectFolder(string path)
    {
        if (!AssetDatabase.IsValidFolder(path)) return;
        FinishStroke();
        bool changed = assetFolder != path;
        assetFolder = path;
        DungeonMapAssetPackSettings.instance.SelectFolder(path);
        if (changed)
        {
            selectedPrefab = null;
            prefabField?.SetValueWithoutNotify(null);
        }
        folderField?.SetValueWithoutNotify(AssetDatabase.LoadAssetAtPath<DefaultAsset>(path));
        RefreshPackChoices();
        searchField?.SetValueWithoutNotify("");
        RefreshCatalog("");
        SetStatus($"에셋 팩 전환: {FolderLabel(path)}. 배치할 타일을 선택하세요.");
        SceneView.RepaintAll();
    }

    private void SetStatus(string message) { if (status != null) status.text = message; }

    private void ClearTarget()
    {
        FinishStroke();
        targetGrid = null;
        gridField?.SetValueWithoutNotify(null);
    }

    private void PrepareLayer()
    {
        if (chunk == null || EditorUtility.IsPersistent(chunk) || EditorApplication.isPlayingOrWillChangePlaymode)
        { SetStatus("Scene의 작업 청크를 먼저 선택하세요."); return; }
        try
        {
            targetGrid = EnsureLayer(chunk, floor, floorHeight, Layers[layer]);
            gridField?.SetValueWithoutNotify(targetGrid);
            SetStatus($"{floor}층 / {Layers[layer]} 준비 완료 (청크 바닥 기준 높이 {chunk.transform.InverseTransformPoint(targetGrid.transform.position).y:0.##}).");
        }
        catch (ArgumentException e) { SetStatus(e.Message); }
    }

    private void RefreshCatalog(string query)
    {
        if (catalog == null) return;
        catalog.Clear();
        if (!AssetDatabase.IsValidFolder(assetFolder)) return;
        var paths = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:GameObject", new[] { assetFolder }))
            paths.Add(AssetDatabase.GUIDToAssetPath(guid));
        paths.Sort(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || prefab.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
            var button = new Button(() => { selectedPrefab = prefab; prefabField.SetValueWithoutNotify(prefab); SetStatus($"선택: {prefab.name} — Scene에서 클릭·드래그하세요."); })
            { tooltip = path };
            button.AddToClassList("tile-button");
            var thumbnail = new Image { image = AssetPreview.GetAssetPreview(prefab) ?? AssetPreview.GetMiniThumbnail(prefab), scaleMode = ScaleMode.ScaleToFit };
            thumbnail.AddToClassList("tile-preview");
            button.Add(thumbnail);
            var label = new Label(prefab.name);
            label.AddToClassList("tile-name");
            button.Add(label);
            catalog.Add(button);
        }
        if (catalog.childCount == 0)
            catalog.Add(new HelpBox("선택한 폴더에 검색 조건과 일치하는 FBX·프리팹이 없습니다.", HelpBoxMessageType.Info));
    }

    public static GameObject CreateChunk(Vector2 size, float spacing, Vector3 position, Material material = null)
    {
        if (!Positive(size.x) || !Positive(size.y) || !Positive(spacing))
            throw new ArgumentException("청크 크기와 타일 간격은 0보다 큰 유한한 값이어야 합니다.");
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new ArgumentException("Play 모드를 종료한 후 청크를 생성하세요.");
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Create Map Chunk");
        var root = new GameObject($"MapChunk_{size.x:0.##}x{size.y:0.##}");
        Undo.RegisterCreatedObjectUndo(root, "Create Map Chunk");
        root.transform.position = position;
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Ground";
        Undo.RegisterCreatedObjectUndo(ground, "Create Chunk Ground");
        ground.transform.SetParent(root.transform, false);
        ground.transform.localScale = new Vector3(size.x, 1f, size.y);
        ground.transform.localPosition = new Vector3(0f, -0.5f, 0f);
        if (material != null) ground.GetComponent<Renderer>().sharedMaterial = material;
        var grid = AddGrid(root.transform, "DungeonAuthoringGrid", spacing);
        grid.transform.localPosition = new Vector3(-size.x * 0.5f, 0f, -size.y * 0.5f);
        foreach (string category in Layers) EnsureLayer(root, 1, 4f, category);
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(root.scene);
        return root;
    }

    public static Grid EnsureLayer(GameObject root, int floorNumber, float storeyHeight, string category)
    {
        if (root == null || floorNumber < 1 || !Positive(storeyHeight) || Array.IndexOf(Layers, category) < 0)
            throw new ArgumentException("청크, 층, 층간 높이와 레이어를 확인하세요.");
        var parent = root.transform.Find("DungeonAuthoringGrid");
        if (parent == null || parent.GetComponent<Grid>() == null)
            throw new ArgumentException("이 청크에는 DungeonAuthoringGrid가 없습니다. 기존 Grid는 배치 대상 칸에 직접 지정하세요.");
        // Reuse legacy layer names on the ground floor without restructuring existing prefabs.
        Transform legacy = floorNumber == 1 ? parent.Find(category) : null;
        if (legacy != null && legacy.TryGetComponent(out Grid legacyGrid)) return legacyGrid;
        string floorName = $"Floor_{floorNumber:00}";
        Transform storey = parent.Find(floorName);
        if (storey == null)
        {
            var go = new GameObject(floorName);
            Undo.RegisterCreatedObjectUndo(go, "Create Authoring Floor");
            go.transform.SetParent(parent, false);
            storey = go.transform;
            storey.localPosition = new Vector3(0f, (floorNumber - 1) * storeyHeight, 0f);
        }
        Transform existing = storey.Find(category);
        if (existing != null && existing.TryGetComponent(out Grid existingGrid)) return existingGrid;
        Grid result = AddGrid(storey, category, parent.GetComponent<Grid>().cellSize.x);
        EditorSceneManager.MarkSceneDirty(root.scene);
        return result;
    }

    private static Grid AddGrid(Transform parent, string name, float spacing)
    {
        var go = new GameObject(name, typeof(Grid));
        Undo.RegisterCreatedObjectUndo(go, "Create Authoring Layer");
        go.transform.SetParent(parent, false);
        Grid grid = go.GetComponent<Grid>();
        grid.cellSize = Vector3.one * spacing;
        grid.cellSwizzle = GridLayout.CellSwizzle.XZY;
        return grid;
    }

    private static bool Positive(float value) => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);

    public static bool CellInsideGround(Grid grid, Vector3Int cell)
    {
        Transform authoring = grid.transform;
        while (authoring != null && authoring.name != "DungeonAuthoringGrid") authoring = authoring.parent;
        Transform ground = authoring != null && authoring.parent != null ? authoring.parent.Find("Ground") : null;
        if (ground == null) return true; // Legacy authoring grids have no generated base.
        Vector3 world = grid.LocalToWorld(grid.CellToLocalInterpolated(cell + new Vector3(0.5f, 0.5f, 0f)));
        Vector3 local = ground.InverseTransformPoint(world);
        return local.x >= -0.5f && local.x < 0.5f && local.z >= -0.5f && local.z < 0.5f;
    }

    private void ConfigureBrush() => brush.Configure(selectedPrefab, placementScale, heightOffset, quarterTurns);

    private void DuringSceneGUI(SceneView view)
    {
        Event e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        { painting = false; FinishStroke(); e.Use(); Repaint(); return; }
        if (!painting || targetGrid == null || EditorUtility.IsPersistent(targetGrid) ||
            !targetGrid.gameObject.activeInHierarchy || targetGrid.cellSwizzle != GridLayout.CellSwizzle.XZY ||
            EditorApplication.isPlayingOrWillChangePlaymode) { FinishStroke(); return; }
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.R)
        { quarterTurns = (quarterTurns + 1) % 4; e.Use(); view.Repaint(); }
        if (e.type == EventType.Layout && !e.alt) HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        if (e.type == EventType.MouseUp && e.button == 0) { FinishStroke(); e.Use(); return; }
        if (e.alt || e.control || e.command) { FinishStroke(); return; }
        var plane = new Plane(targetGrid.transform.up, targetGrid.transform.position);
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        if (!plane.Raycast(ray, out float distance)) return;
        Vector3Int cell = targetGrid.WorldToCell(ray.GetPoint(distance));
        cell.z = 0;
        bool valid = CellInsideGround(targetGrid, cell);
        if (e.type == EventType.Repaint)
        {
            using (new Handles.DrawingScope(valid ? Color.cyan : Color.red, targetGrid.transform.localToWorldMatrix))
            {
                Vector3 min = targetGrid.CellToLocal(cell);
                Vector3 max = targetGrid.CellToLocal(cell + new Vector3Int(1, 1, 0));
                Handles.DrawWireCube((min + max) * 0.5f, max - min);
            }
            if (valid && selectedPrefab != null)
            {
                ConfigureBrush();
                using (new Handles.DrawingScope(new Color(0.3f, 1f, 0.4f, 0.7f), brush.PlacementMatrix(targetGrid, cell, selectedPrefab)))
                    foreach (MeshFilter mesh in selectedPrefab.GetComponentsInChildren<MeshFilter>(true))
                        if (mesh.sharedMesh != null)
                        {
                            Matrix4x4 local = selectedPrefab.transform.worldToLocalMatrix * mesh.transform.localToWorldMatrix;
                            using (new Handles.DrawingScope(Handles.color, Handles.matrix * local))
                                Handles.DrawWireCube(mesh.sharedMesh.bounds.center, mesh.sharedMesh.bounds.size);
                        }
            }
        }
        if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0)
        {
            if (e.type == EventType.MouseDrag && !stroking) return;
            if (!valid) { previousCell = null; return; }
            if (!e.shift && (selectedPrefab == null || !Positive(placementScale.x) || !Positive(placementScale.y) || !Positive(placementScale.z)))
            { SetStatus("타일을 선택하고 크기 배율을 0보다 크게 설정하세요."); return; }
            if (!stroking)
            {
                Undo.IncrementCurrentGroup();
                strokeUndo = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(e.shift ? "Erase Map Stroke" : "Paint Map Stroke");
                stroking = true;
                erasing = e.shift;
            }
            ConfigureBrush();
            Vector3Int start = previousCell ?? cell;
            int steps = Mathf.Max(Mathf.Abs(cell.x - start.x), Mathf.Abs(cell.y - start.y));
            // Fill cells skipped by a fast drag so a continuous stroke has no holes.
            for (int i = 0; i <= steps; i++)
            {
                Vector3Int point = Vector3Int.RoundToInt(Vector3.Lerp(start, cell, steps == 0 ? 0f : (float)i / steps));
                if (!CellInsideGround(targetGrid, point) || !visited.Add(point)) continue;
                if (erasing) brush.Erase(targetGrid, targetGrid.gameObject, point);
                else brush.Paint(targetGrid, targetGrid.gameObject, point);
            }
            previousCell = cell;
            e.Use();
        }
        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag) view.Repaint();
        if (e.type == EventType.MouseLeaveWindow || e.type == EventType.Ignore) FinishStroke();
    }

    private void FinishStroke()
    {
        if (stroking) Undo.CollapseUndoOperations(strokeUndo);
        stroking = false;
        previousCell = null;
        visited.Clear();
    }
}

// Local authoring preferences: no shared scene, prefab or per-pack assets are created.
[FilePath("UserSettings/DungeonMapAssetPacks.asset", FilePathAttribute.Location.ProjectFolder)]
public sealed class DungeonMapAssetPackSettings : ScriptableSingleton<DungeonMapAssetPackSettings>
{
    [Serializable]
    public sealed class Entry
    {
        public string folderGuid;
        public string displayName;
        public string FolderPath => AssetDatabase.GUIDToAssetPath(folderGuid);
    }

    [SerializeField] public List<Entry> entries = new List<Entry>();
    [SerializeField] public string selectedFolderGuid;

    public bool RegisterFolder(string path, string label)
    {
        if (!AssetDatabase.IsValidFolder(path) || string.IsNullOrWhiteSpace(label)) return false;
        string guid = AssetDatabase.AssetPathToGUID(path);
        Entry entry = entries.Find(p => p.folderGuid == guid);
        if (entry == null) { entry = new Entry { folderGuid = guid }; entries.Add(entry); }
        entry.displayName = label.Trim();
        Save(true);
        return true;
    }

    public void UnregisterFolder(string path)
    {
        string guid = AssetDatabase.AssetPathToGUID(path);
        if (entries.RemoveAll(p => p.folderGuid == guid) > 0) Save(true);
    }

    public void SelectFolder(string path)
    {
        string guid = AssetDatabase.AssetPathToGUID(path);
        if (selectedFolderGuid == guid) return;
        selectedFolderGuid = guid;
        Save(true);
    }
}
