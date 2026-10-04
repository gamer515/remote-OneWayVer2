using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// [역할] Sword에 붙어 칼날 양 끝의 화면 좌표와 잔상을 관리합니다. 손 클릭 판정과는 무관합니다.
/// [참조] BladeBase=손잡이 밖 칼날 시작, BladeTip=칼끝.
/// [핵심] CapturePose로 자세를 기록하고 TrySweepBladeContact로 칼날 전용 BoxCollider의 3D 접촉을 검사합니다.
/// [수정] 잔상은 Trail 설정에서 조절합니다. 화면 접촉 함수는 기존 재사용용입니다.
/// NpcDuelStateMachine이 Animator 평가 후 현재 프레임 접촉과 궤적을 기록합니다. 지난 잔상에는 판정이 없습니다.
/// </summary>
[DefaultExecutionOrder(100)]
public sealed class DuelBladeHitbox : MonoBehaviour
{
    [Tooltip("실제 칼날 시작점입니다. 손잡이와 가드는 포함하지 않도록 배치합니다.")]
    [SerializeField] private Transform bladeBase;
    [Tooltip("실제 칼날 끝점입니다. 시작점부터 이 끝점까지 지나간 면을 잔상으로 만듭니다.")]
    [SerializeField] private Transform bladeTip;
    /// <summary>실제 칼날 시작 마커. 손 위치나 손잡이 기준점이 아닙니다.</summary>
    public Transform BladeBase => bladeBase;
    /// <summary>실제 칼날 끝 마커. 궤적과 판정 선분의 끝점입니다.</summary>
    public Transform BladeTip => bladeTip;
    [Header("Actual Motion Trail")]
    [Tooltip("공격 또는 이동 중 칼날 전체가 지나간 면과 칼끝 선을 표시합니다. 잔상 자체에는 공격 판정이 없습니다.")]
    [SerializeField] private bool showTrail = true;
    [Tooltip("칼날 전체 잔상의 색상입니다. 오래된 궤적일수록 투명해집니다.")]
    [SerializeField] private Color trailColor = new Color(0.3f, 0.85f, 1f, 0.8f);
    [Tooltip("잔상과 최근 궤적을 유지하는 시간(초)입니다.")]
    [SerializeField, Min(0.01f)] private float trailSeconds = 0.3f;
    [Tooltip("칼끝 궤적 선의 폭(월드 유닛)입니다. 잔상 면은 이 값이 아니라 실제 칼날 길이 전체를 사용합니다.")]
    [SerializeField, Min(0.001f)] private float trailWidth = 0.035f;
    [Tooltip("칼날 잔상에 사용할 머티리얼입니다. 투명도와 정점 색상을 지원해야 합니다. 비우면 런타임 기본 머티리얼을 생성합니다.")]
    [SerializeField] private Material trailMaterial;
    [Tooltip("Sword 선택 시 칼날 중심선과 최근 이동 궤적을 Scene Gizmos에 표시합니다.")]
    [SerializeField] private bool showContactGizmos = true;
    /// <summary>칼날 시작과 끝 사이의 월드 좌표 중심. 두 마커가 할당되어 있어야 합니다.</summary>
    public Vector3 Midpoint => (bladeBase.position + bladeTip.position) * 0.5f;
    /// <summary>칼날 시작·끝 마커가 모두 할당되어 자세를 계산할 수 있는지 여부입니다.</summary>
    public bool IsValid => bladeBase != null && bladeTip != null;
    /// <summary>공격/방어 이벤트가 요청한 궤적 상태. 대련 중 상시 시각화와 공격 판정은 별개입니다.</summary>
    public bool MotionActive { get; private set; }
    /// <summary>손 클릭 박스가 아닌 Sword의 활성 칼날 전용 BoxCollider가 있는지 여부입니다.</summary>
    public bool HasBladeContactShape => bladeContactBox != null && bladeContactBox.enabled && bladeContactBox.gameObject.activeInHierarchy;
    /// <summary>한 시점의 칼날 월드 중심·회전·상대 위치를 저장하는 자세 데이터입니다.</summary>
    public struct Pose
    {
        /// <summary>칼날 중심의 월드 좌표입니다.</summary>
        public Vector3 center;
        /// <summary>칼날 BoxCollider가 달린 Sword의 월드 원점. 중간 프레임 접촉 검사에 사용합니다.</summary>
        public Vector3 position;
        /// <summary>회전을 제거한 중심→칼날 시작점 벡터(월드 길이)입니다.</summary>
        public Vector3 baseOffset;
        /// <summary>회전을 제거한 중심→칼끝 벡터(월드 길이)입니다.</summary>
        public Vector3 tipOffset;
        /// <summary>Sword Transform의 월드 회전입니다.</summary>
        public Quaternion rotation;
        /// <summary>저장된 자세에서 실제 칼날 시작·끝의 월드 좌표를 복원합니다.</summary>
        public void Segment(out Vector3 a, out Vector3 b) { a = center + rotation * baseOffset; b = center + rotation * tipOffset; }
        /// <summary>두 자세 사이를 t(0~1)로 보간해 빠른 이동 중간의 접촉도 검사합니다.</summary>
        public static Pose Interpolate(Pose a, Pose b, float t) => new Pose {
            center = Vector3.Lerp(a.center,b.center,t), position = Vector3.Lerp(a.position,b.position,t), rotation = Quaternion.Slerp(a.rotation,b.rotation,t),
            baseOffset = Vector3.Lerp(a.baseOffset,b.baseOffset,t), tipOffset = Vector3.Lerp(a.tipOffset,b.tipOffset,t)
        };
    }
    private TrailRenderer tipTrail;
    private BoxCollider bladeContactBox;
    private Material generatedMaterial;
    private GameObject sweptTrailObject;
    private Mesh sweptTrailMesh;
    private MeshRenderer sweptTrailRenderer;
    private readonly List<Vector3> trailVertices = new List<Vector3>(768);
    private readonly List<Vector3> trailNormals = new List<Vector3>(768);
    private readonly List<Color> trailColors = new List<Color>(768);
    private readonly List<Vector2> trailUvs = new List<Vector2>(768);
    private readonly List<int> trailTriangles = new List<int>(1530);
    private bool liveTrail;
    private bool ShouldRecordMotion => MotionActive || liveTrail;
    private readonly Vector3[] baseHistory = new Vector3[128], tipHistory = new Vector3[128];
    private readonly float[] historyTimes = new float[128];
    private int historyIndex, historyCount;
    /// <summary>현재 마커 위치와 검 회전을 캡처합니다. Animator와 위치 보정이 끝난 뒤 호출합니다.</summary>
    public Pose CapturePose()
    {
        var rotation = transform.rotation;
        var center = Midpoint;
        return new Pose { center = center, position = transform.position, rotation = rotation,
            baseOffset = Quaternion.Inverse(rotation) * (bladeBase.position-center),
            tipOffset = Quaternion.Inverse(rotation) * (bladeTip.position-center) };
    }
    /// <summary>이벤트가 요청한 잔상을 켜거나 끕니다. 대련의 실시간 궤적은 이벤트 종료로 끊기지 않습니다.</summary>
    public void SetMotionActive(bool active)
    {
        bool wasRecording = ShouldRecordMotion;
        MotionActive = active;
        RefreshTrailEmission(wasRecording);
    }
    /// <summary>실제 칼날 전체가 지나간 궤적을 Game 화면에도 표시합니다. 공격/피격 판정 상태는 바꾸지 않습니다.</summary>
    public void SetLiveTrail(bool active)
    {
        bool wasRecording = ShouldRecordMotion;
        liveTrail = active;
        RefreshTrailEmission(wasRecording);
    }
    private void RefreshTrailEmission(bool wasRecording)
    {
        if (ShouldRecordMotion && !wasRecording) ResetHistory();
        if (tipTrail != null) tipTrail.emitting = ShouldRecordMotion && showTrail;
    }
    /// <summary>최근 이동 기록과 잔상을 초기화합니다. 대련 시작·종료 때 사용합니다.</summary>
    public void ResetHistory()
    {
        historyIndex = historyCount = 0;
        if (tipTrail != null) tipTrail.Clear();
        if (sweptTrailMesh != null) sweptTrailMesh.Clear();
        if (sweptTrailRenderer != null) sweptTrailRenderer.enabled = false;
    }
    private void Awake()
    {
        bladeContactBox = GetComponent<BoxCollider>();
        if (!IsValid) return;
        tipTrail = bladeTip.GetComponent<TrailRenderer>();
        if (tipTrail == null) tipTrail = bladeTip.gameObject.AddComponent<TrailRenderer>();
        if (trailMaterial != null) tipTrail.sharedMaterial = trailMaterial;
        else {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            if (shader != null)
            {
                generatedMaterial = new Material(shader);
                // URP Particle/Unlit는 기본값이 불투명입니다. 얇은 칼끝 선뿐 아니라 면의 알파도 적용합니다.
                if (generatedMaterial.HasProperty("_Surface"))
                {
                    generatedMaterial.SetFloat("_Surface", 1f);
                    generatedMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    generatedMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    generatedMaterial.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
                    generatedMaterial.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    generatedMaterial.SetFloat("_ZWrite", 0f);
                    generatedMaterial.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
                    generatedMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    generatedMaterial.SetOverrideTag("RenderType", "Transparent");
                    generatedMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }
                tipTrail.sharedMaterial = generatedMaterial;
            }
        }
        tipTrail.emitting = false;
        tipTrail.minVertexDistance = 0.01f;
        tipTrail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tipTrail.receiveShadows = false;
        sweptTrailObject = new GameObject("BladeSweptTrail") { layer = gameObject.layer };
        sweptTrailObject.transform.SetParent(transform, false);
        sweptTrailMesh = new Mesh { name = "Runtime Blade Swept Surface" };
        sweptTrailMesh.MarkDynamic();
        sweptTrailObject.AddComponent<MeshFilter>().sharedMesh = sweptTrailMesh;
        sweptTrailRenderer = sweptTrailObject.AddComponent<MeshRenderer>();
        sweptTrailRenderer.sharedMaterial = tipTrail.sharedMaterial;
        sweptTrailRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sweptTrailRenderer.receiveShadows = false;
        sweptTrailRenderer.enabled = false;
    }
    /// <summary>NPC 컨트롤러가 Animator·깊이 보정 후 호출하여 잔상 설정과 칼날 이동 기록을 갱신합니다.</summary>
    public void RecordMotion()
    {
        if (!IsValid) return;
        if (tipTrail != null) {
            tipTrail.time=trailSeconds; tipTrail.startWidth=trailWidth; tipTrail.endWidth=0f;
            tipTrail.startColor=trailColor; tipTrail.endColor=new Color(trailColor.r,trailColor.g,trailColor.b,0f);
            tipTrail.emitting=ShouldRecordMotion && showTrail;
        }
        if (!ShouldRecordMotion) return;
        if (historyCount > 0)
        {
            int previous = (historyIndex - 1 + baseHistory.Length) % baseHistory.Length;
            // 정지/일시정지 프레임이 반복돼도 이전 베기 궤적을 같은 자세로 덮어쓰지 않습니다.
            if ((baseHistory[previous] - bladeBase.position).sqrMagnitude < 0.00000001f &&
                (tipHistory[previous] - bladeTip.position).sqrMagnitude < 0.00000001f) return;
        }
        baseHistory[historyIndex]=bladeBase.position; tipHistory[historyIndex]=bladeTip.position; historyTimes[historyIndex]=Time.time;
        historyIndex=(historyIndex+1)%baseHistory.Length; historyCount=Mathf.Min(historyCount+1,baseHistory.Length);
    }
    // NPC LateUpdate에서 원본 Animator 자세와 부모 위치 보정을 기록한 뒤 면을 갱신합니다.
    // 기록이 멈춰도 기존 면은 유지 시간에 맞춰 사라지고, 선택/Gizmos 여부에 의존하지 않습니다.
    private void LateUpdate() => RefreshSweptTrail();

    private void RefreshSweptTrail()
    {
        if (sweptTrailMesh == null) return;
        while (historyCount > 0)
        {
            int oldest = (historyIndex - historyCount + baseHistory.Length) % baseHistory.Length;
            if (Time.time - historyTimes[oldest] < trailSeconds) break;
            historyCount--;
        }
        if (!showTrail || historyCount < 2)
        {
            sweptTrailRenderer.enabled = false;
            sweptTrailMesh.Clear();
            return;
        }
        trailVertices.Clear(); trailNormals.Clear(); trailColors.Clear(); trailUvs.Clear(); trailTriangles.Clear();
        Matrix4x4 worldToLocal = sweptTrailObject.transform.worldToLocalMatrix;
        for (int i = 0; i < historyCount; i++)
        {
            int index = (historyIndex - historyCount + i + baseHistory.Length) % baseHistory.Length;
            float fade = 1f - Mathf.Clamp01((Time.time - historyTimes[index]) / Mathf.Max(0.01f, trailSeconds));
            Color color = trailColor;
            color.a *= fade * 0.3f; // 배경/검이 가려지지 않게 면은 칼끝 선보다 옅게 표시합니다.
            trailVertices.Add(worldToLocal.MultiplyPoint3x4(baseHistory[index]));
            trailVertices.Add(worldToLocal.MultiplyPoint3x4(tipHistory[index]));
            trailNormals.Add(Vector3.forward); trailNormals.Add(Vector3.forward);
            trailColors.Add(color); trailColors.Add(color);
            trailUvs.Add(new Vector2(0f, i / (float)(historyCount - 1)));
            trailUvs.Add(new Vector2(1f, i / (float)(historyCount - 1)));
            if (i == 0) continue;
            // 직전 프레임과 현재 프레임의 칼날 전체 선분을 두 삼각형으로 잇습니다.
            int previous = (i - 1) * 2, current = i * 2;
            trailTriangles.Add(previous); trailTriangles.Add(current); trailTriangles.Add(previous + 1);
            trailTriangles.Add(previous + 1); trailTriangles.Add(current); trailTriangles.Add(current + 1);
        }
        // 사진의 부채꼴 선처럼, 각 시점의 칼날 전체 선분도 면 위에 가늘게 남깁니다.
        Camera camera = Camera.main;
        Vector3 viewDirection = camera != null ? camera.transform.forward : Vector3.forward;
        for (int i = 0; i < historyCount; i++)
        {
            int index = (historyIndex - historyCount + i + baseHistory.Length) % baseHistory.Length;
            Vector3 side = Vector3.Cross(tipHistory[index] - baseHistory[index], viewDirection).normalized * trailWidth * 0.35f;
            if (side.sqrMagnitude < 0.00000001f) continue;
            Color color = trailColor;
            color.a *= (1f - Mathf.Clamp01((Time.time - historyTimes[index]) / Mathf.Max(0.01f, trailSeconds))) * 0.7f;
            int first = trailVertices.Count;
            AddTrailVertex(worldToLocal.MultiplyPoint3x4(baseHistory[index] - side), color, new Vector2(0f, 0f));
            AddTrailVertex(worldToLocal.MultiplyPoint3x4(baseHistory[index] + side), color, new Vector2(1f, 0f));
            AddTrailVertex(worldToLocal.MultiplyPoint3x4(tipHistory[index] - side), color, new Vector2(0f, 1f));
            AddTrailVertex(worldToLocal.MultiplyPoint3x4(tipHistory[index] + side), color, new Vector2(1f, 1f));
            trailTriangles.Add(first); trailTriangles.Add(first + 1); trailTriangles.Add(first + 2);
            trailTriangles.Add(first + 2); trailTriangles.Add(first + 1); trailTriangles.Add(first + 3);
        }
        sweptTrailMesh.Clear();
        sweptTrailMesh.SetVertices(trailVertices);
        sweptTrailMesh.SetNormals(trailNormals);
        sweptTrailMesh.SetColors(trailColors);
        sweptTrailMesh.SetUVs(0, trailUvs);
        sweptTrailMesh.SetTriangles(trailTriangles, 0);
        sweptTrailMesh.RecalculateBounds();
        sweptTrailRenderer.enabled = true;
    }
    private void AddTrailVertex(Vector3 point, Color color, Vector2 uv)
    {
        trailVertices.Add(point); trailNormals.Add(Vector3.forward); trailColors.Add(color); trailUvs.Add(uv);
    }

    /// <summary>같은 시각의 두 칼날 BoxCollider를 보간해 실제 3D 겹침을 검사합니다. 화면 겹침·손잡이·과거 잔상은 대상이 아닙니다.</summary>
    public bool TrySweepBladeContact(Pose previous, Pose current, DuelBladeHitbox other, Pose otherPrevious, Pose otherCurrent,
        float from, float to, out Vector3 contact)
    {
        contact = default;
        if (!HasBladeContactShape || other == null || !other.HasBladeContactShape || from > to) return false;
        float travel = SweepTravel(previous, current) + SweepTravel(otherPrevious, otherCurrent);
        float spacing = Mathf.Max(0.001f, Mathf.Min(ContactThickness(), other.ContactThickness()) * 0.25f);
        int steps = Mathf.Clamp(Mathf.CeilToInt(travel * (to - from) / spacing), 1, 512);
        for (int i = 0; i <= steps; i++)
        {
            float t = Mathf.Lerp(from, to, i / (float)steps);
            Pose a = Pose.Interpolate(previous, current, t), b = Pose.Interpolate(otherPrevious, otherCurrent, t);
            // 위치/회전을 직접 전달하므로 Animator로 움직이는 검도 Physics.SyncTransforms 없이 검사합니다.
            if (!Physics.ComputePenetration(bladeContactBox, a.position, a.rotation,
                other.bladeContactBox, b.position, b.rotation, out _, out _)) continue;
            Vector3 otherCenter = b.position + b.rotation * Vector3.Scale(other.bladeContactBox.center, other.transform.lossyScale);
            Vector3 first = Physics.ClosestPoint(otherCenter, bladeContactBox, a.position, a.rotation);
            Vector3 second = Physics.ClosestPoint(first, other.bladeContactBox, b.position, b.rotation);
            contact = (first + second) * 0.5f;
            return true;
        }
        return false;
    }
    private float ContactThickness()
    {
        Vector3 size = Vector3.Scale(bladeContactBox.size, transform.lossyScale);
        return Mathf.Min(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
    }
    private static float SweepTravel(Pose previous, Pose current)
    {
        previous.Segment(out var a, out var b);
        float radius = Mathf.Max(Vector3.Distance(previous.position, a), Vector3.Distance(previous.position, b));
        return Vector3.Distance(previous.position, current.position) + Quaternion.Angle(previous.rotation, current.rotation) * Mathf.Deg2Rad * radius;
    }
    /// <summary>칼날 양 끝을 화면 픽셀 좌표로 변환합니다. 카메라 앞에 두 마커가 있을 때만 성공하며 3D 접촉을 뜻하지 않습니다.</summary>
    public bool TryGetScreenSegment(Camera camera, out Vector2 start, out Vector2 end)
    {
        start = end = default;
        if (camera == null || bladeBase == null || bladeTip == null) return false;
        Vector3 a = camera.WorldToScreenPoint(bladeBase.position);
        Vector3 b = camera.WorldToScreenPoint(bladeTip.position);
        if (a.z <= camera.nearClipPlane || b.z <= camera.nearClipPlane) return false;
        start = a; end = b;
        return true;
    }
    /// <summary>저장된 자세의 칼날 양 끝을 화면 픽셀 좌표로 투영합니다.</summary>
    public static bool TryGetScreenSegment(Camera camera, Pose pose, out Vector2 start, out Vector2 end)
    {
        start = end = default;
        if (camera == null) return false;
        pose.Segment(out var a, out var b);
        Vector3 screenA = camera.WorldToScreenPoint(a), screenB = camera.WorldToScreenPoint(b);
        if (screenA.z <= camera.nearClipPlane || screenB.z <= camera.nearClipPlane) return false;
        start = screenA; end = screenB;
        return true;
    }

    /// <summary>두 칼날의 이전→현재 자세를 같은 시각으로 보간해 화면 선분의 접촉을 검사합니다. 영역/손잡이/3D 깊이는 조건이 아닙니다.</summary>
    public bool TrySweepScreenContact(Camera camera, Pose previous, Pose current, DuelBladeHitbox other,
        Pose otherPrevious, Pose otherCurrent, float tolerancePixels, int maxSteps,
        out Vector3 contact, out float fraction)
    {
        contact = default; fraction = 1f;
        if (other == null || camera == null) return false;
        int steps = ScreenSweepSteps(camera, previous, current, tolerancePixels);
        steps = Mathf.Min(Mathf.Max(1, maxSteps), Mathf.Max(steps,
            ScreenSweepSteps(camera, otherPrevious, otherCurrent, tolerancePixels)));
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            Pose npc = Pose.Interpolate(previous, current, t);
            Pose player = Pose.Interpolate(otherPrevious, otherCurrent, t);
            if (!TryGetScreenSegment(camera, npc, out var a, out var b) ||
                !TryGetScreenSegment(camera, player, out var c, out var d)) continue;
            if (SegmentDistance(a, b, c, d, out var screenContact) > tolerancePixels) continue;
            float depth = camera.WorldToScreenPoint(npc.center).z;
            contact = camera.ScreenToWorldPoint(new Vector3(screenContact.x, screenContact.y, depth));
            fraction = t;
            return true;
        }
        return false;
    }

    /// <summary>NPC의 실제 칼날 이동이 공격 목표 사각형을 가로질렀는지 확인합니다. 방어 영역 제한과는 무관합니다.</summary>
    public bool TrySweepScreenArea(Camera camera, Pose previous, Pose current, Rect area, int maxSteps, out Vector3 contact)
    {
        contact = default;
        if (camera == null || area.width <= 0f || area.height <= 0f) return false;
        int steps = Mathf.Min(Mathf.Max(1, maxSteps), ScreenSweepSteps(camera, previous, current, 4f));
        for (int i = 0; i <= steps; i++)
        {
            var pose = Pose.Interpolate(previous, current, i / (float)steps);
            if (!TryGetScreenSegment(camera, pose, out var a, out var b) || !SegmentIntersectsRect(a, b, area, out var point)) continue;
            float depth = camera.WorldToScreenPoint(pose.center).z;
            contact = camera.ScreenToWorldPoint(new Vector3(point.x, point.y, depth));
            return true;
        }
        return false;
    }
    /// <summary>칼날 선분과 사각형의 실제 겹침을 검사합니다. 영역에 가장 먼저 들어온 점을 반환합니다.</summary>
    public static bool SegmentIntersectsRect(Vector2 a, Vector2 b, Rect rect, out Vector2 point)
    {
        Vector2 delta = b - a;
        float enter = 0f, exit = 1f;
        point = default;
        if (!ClipAxis(a.x, delta.x, rect.xMin, rect.xMax, ref enter, ref exit) ||
            !ClipAxis(a.y, delta.y, rect.yMin, rect.yMax, ref enter, ref exit)) return false;
        point = a + delta * enter;
        return true;
    }
    private static bool ClipAxis(float start, float delta, float min, float max, ref float enter, ref float exit)
    {
        if (Mathf.Abs(delta) < 0.00001f) return start >= min && start <= max;
        float a = (min - start) / delta, b = (max - start) / delta;
        if (a > b) { float swap = a; a = b; b = swap; }
        enter = Mathf.Max(enter, a); exit = Mathf.Min(exit, b);
        return enter <= exit;
    }

    private static int ScreenSweepSteps(Camera camera, Pose previous, Pose current, float tolerancePixels)
    {
        if (!TryGetScreenSegment(camera, previous, out var a, out var b) ||
            !TryGetScreenSegment(camera, current, out var c, out var d)) return 1;
        float travel = Mathf.Max(Vector2.Distance(a, c), Vector2.Distance(b, d));
        return Mathf.Max(1, Mathf.CeilToInt(travel / Mathf.Max(2f, tolerancePixels * 0.5f)));
    }

    /// <summary>화면상의 두 2D 선분 사이 거리와 접점 근사를 계산합니다. 실제 검의 앞뒤 깊이는 반영하지 않습니다.</summary>
    public static float SegmentDistance(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 contact)
    {
        Vector2 r = b - a, s = d - c;
        float cross = Cross(r, s);
        if (Mathf.Abs(cross) > 0.00001f)
        {
            float t = Cross(c - a, s) / cross, u = Cross(c - a, r) / cross;
            if (t >= 0f && t <= 1f && u >= 0f && u <= 1f) { contact = a + t * r; return 0f; }
        }
        float best = float.PositiveInfinity;
        contact = a;
        Closest(a, c, d, ref best, ref contact);
        Closest(b, c, d, ref best, ref contact);
        Closest(c, a, b, ref best, ref contact);
        Closest(d, a, b, ref best, ref contact);
        return best;
    }
    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    private static void Closest(Vector2 point, Vector2 a, Vector2 b, ref float best, ref Vector2 contact)
    {
        Vector2 edge = b - a;
        float t = edge.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(point - a, edge) / edge.sqrMagnitude) : 0f;
        Vector2 closest = a + edge * t;
        float distance = Vector2.Distance(point, closest);
        if (distance >= best) return;
        best = distance;
        contact = (point + closest) * 0.5f;
    }
    private void OnDrawGizmosSelected()
    {
        if (!IsValid || !showContactGizmos) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(bladeBase.position, bladeTip.position);
        Gizmos.color=trailColor;
        for(int i=1;i<historyCount;i++) {
            int previous=(historyIndex-i-1+baseHistory.Length)%baseHistory.Length,next=(previous+1)%baseHistory.Length;
            if(Time.time-historyTimes[previous]>trailSeconds) break;
            Gizmos.DrawLine(baseHistory[previous],baseHistory[next]);Gizmos.DrawLine(tipHistory[previous],tipHistory[next]);
            Gizmos.DrawLine(baseHistory[next],tipHistory[next]);
        }
    }
    private void OnDisable() { SetLiveTrail(false); SetMotionActive(false); ResetHistory(); }
    private void OnDestroy()
    {
        if (sweptTrailObject != null) Destroy(sweptTrailObject);
        if (sweptTrailMesh != null) Destroy(sweptTrailMesh);
        if (generatedMaterial != null) Destroy(generatedMaterial);
    }
}
