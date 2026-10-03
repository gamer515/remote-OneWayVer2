using UnityEngine;

/// <summary>
/// [역할] Sword에 붙어 칼날 양 끝의 화면 좌표와 잔상을 관리합니다. 손 클릭 판정과는 무관합니다.
/// [참조] BladeBase=손잡이 밖 칼날 시작, BladeTip=칼끝.
/// [핵심] CapturePose로 자세를 기록하고 TrySweepScreenContact로 두 검의 화면상 이동을 검사합니다.
/// [수정] 잔상은 Trail 설정에서 조절합니다. 화면 접촉 함수는 재사용용이며 현재 NPC는 공격 판정에 사용하지 않습니다.
/// NpcDuelStateMachine이 Animator 평가 후 궤적만 기록합니다. 지난 잔상에는 공격 판정이 없습니다.
/// </summary>
public sealed class DuelBladeHitbox : MonoBehaviour
{
    [Tooltip("실제 칼날 시작점입니다. 손잡이와 가드는 포함하지 않도록 배치합니다.")]
    [SerializeField] private Transform bladeBase;
    [Tooltip("실제 칼날 끝점입니다. 칼끝 잔상도 이 위치에서 생성합니다.")]
    [SerializeField] private Transform bladeTip;
    /// <summary>실제 칼날 시작 마커. 손 위치나 손잡이 기준점이 아닙니다.</summary>
    public Transform BladeBase => bladeBase;
    /// <summary>실제 칼날 끝 마커. 궤적과 판정 선분의 끝점입니다.</summary>
    public Transform BladeTip => bladeTip;
    [Header("Actual Motion Trail")]
    [Tooltip("공격 또는 이동 중 칼끝 잔상을 표시합니다. 잔상 자체에는 공격 판정이 없습니다.")]
    [SerializeField] private bool showTrail = true;
    [Tooltip("칼끝 잔상의 시작 색상입니다. 끝으로 갈수록 투명해집니다.")]
    [SerializeField] private Color trailColor = new Color(0.3f, 0.85f, 1f, 0.8f);
    [Tooltip("잔상과 최근 궤적을 유지하는 시간(초)입니다.")]
    [SerializeField, Min(0.01f)] private float trailSeconds = 0.3f;
    [Tooltip("잔상 시작 폭(월드 유닛)입니다. 칼날 판정 크기와는 별개입니다.")]
    [SerializeField, Min(0.001f)] private float trailWidth = 0.035f;
    [Tooltip("칼끝 잔상에 사용할 머티리얼입니다. 비우면 런타임에 기본 머티리얼을 생성합니다.")]
    [SerializeField] private Material trailMaterial;
    [Tooltip("Sword 선택 시 칼날 중심선과 최근 이동 궤적을 Scene Gizmos에 표시합니다.")]
    [SerializeField] private bool showContactGizmos = true;
    /// <summary>칼날 시작과 끝 사이의 월드 좌표 중심. 두 마커가 할당되어 있어야 합니다.</summary>
    public Vector3 Midpoint => (bladeBase.position + bladeTip.position) * 0.5f;
    /// <summary>칼날 시작·끝 마커가 모두 할당되어 자세를 계산할 수 있는지 여부입니다.</summary>
    public bool IsValid => bladeBase != null && bladeTip != null;
    /// <summary>현재 궤적을 기록할지 여부입니다. 공격 판정 활성 여부는 NPC 공격 이벤트에서 별도로 관리합니다.</summary>
    public bool MotionActive { get; private set; }
    /// <summary>한 시점의 칼날 월드 중심·회전·상대 위치를 저장하는 자세 데이터입니다.</summary>
    public struct Pose
    {
        /// <summary>칼날 중심의 월드 좌표입니다.</summary>
        public Vector3 center;
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
            center = Vector3.Lerp(a.center,b.center,t), rotation = Quaternion.Slerp(a.rotation,b.rotation,t),
            baseOffset = Vector3.Lerp(a.baseOffset,b.baseOffset,t), tipOffset = Vector3.Lerp(a.tipOffset,b.tipOffset,t)
        };
    }
    private TrailRenderer tipTrail;
    private Material generatedMaterial;
    private readonly Vector3[] baseHistory = new Vector3[128], tipHistory = new Vector3[128];
    private readonly float[] historyTimes = new float[128];
    private int historyIndex, historyCount;
    /// <summary>현재 마커 위치와 검 회전을 캡처합니다. Animator와 위치 보정이 끝난 뒤 호출합니다.</summary>
    public Pose CapturePose()
    {
        var rotation = transform.rotation;
        var center = Midpoint;
        return new Pose { center = center, rotation = rotation,
            baseOffset = Quaternion.Inverse(rotation) * (bladeBase.position-center),
            tipOffset = Quaternion.Inverse(rotation) * (bladeTip.position-center) };
    }
    /// <summary>궤적 기록·잔상 출력을 켜거나 끕니다. 새로 켤 때 이전 잔상을 비웁니다.</summary>
    public void SetMotionActive(bool active)
    {
        if (active && !MotionActive && tipTrail != null) tipTrail.Clear();
        MotionActive = active;
        if (tipTrail != null) tipTrail.emitting = active && showTrail;
    }
    /// <summary>최근 이동 기록과 잔상을 초기화합니다. 대련 시작·종료 때 사용합니다.</summary>
    public void ResetHistory()
    {
        historyIndex = historyCount = 0;
        if (tipTrail != null) tipTrail.Clear();
    }
    private void Awake()
    {
        if (!IsValid) return;
        tipTrail = bladeTip.GetComponent<TrailRenderer>();
        if (tipTrail == null) tipTrail = bladeTip.gameObject.AddComponent<TrailRenderer>();
        if (trailMaterial != null) tipTrail.sharedMaterial = trailMaterial;
        else {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            if (shader != null) { generatedMaterial = new Material(shader); tipTrail.sharedMaterial = generatedMaterial; }
        }
        tipTrail.emitting = false;
        tipTrail.minVertexDistance = 0.01f;
        tipTrail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tipTrail.receiveShadows = false;
    }
    /// <summary>NPC 컨트롤러가 Animator·깊이 보정 후 호출하여 잔상 설정과 칼날 이동 기록을 갱신합니다.</summary>
    public void RecordMotion()
    {
        if (!IsValid) return;
        if (tipTrail != null) {
            tipTrail.time=trailSeconds; tipTrail.startWidth=trailWidth; tipTrail.endWidth=0f;
            tipTrail.startColor=trailColor; tipTrail.endColor=new Color(trailColor.r,trailColor.g,trailColor.b,0f);
            tipTrail.emitting=MotionActive && showTrail;
        }
        if (!MotionActive) return;
        baseHistory[historyIndex]=bladeBase.position; tipHistory[historyIndex]=bladeTip.position; historyTimes[historyIndex]=Time.time;
        historyIndex=(historyIndex+1)%baseHistory.Length; historyCount=Mathf.Min(historyCount+1,baseHistory.Length);
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
    private void OnDisable() { SetMotionActive(false); ResetHistory(); }
    private void OnDestroy() { if(generatedMaterial!=null) Destroy(generatedMaterial); }
}
