using UnityEngine;
using UnityEngine.Rendering;

namespace OneWay.Battle3D
{
    // Read-only display, updated after BossWeaponHit's contact query.
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(BossWeaponHit))]
    public sealed class BossWeaponHitDebug : MonoBehaviour
    {
        public bool showHitbox = true;
        public bool showControls = true;
        [Min(.001f)] public float lineWidth = .04f;
        public Material lineMaterial;
        public Color inactiveColor = Color.yellow;
        public Color activeColor = Color.red;
        BossWeaponHit hit;
        LineRenderer line;
        Material instanceMaterial;
        readonly Vector3[] points = new Vector3[16];
        static readonly int[] Edges = {0,1,3,2,0,4,5,1,5,7,3,7,6,2,6,4};
        static readonly int[] ExtrudedEdges = BuildExtrudedEdges();
        readonly Vector3[] extendedPoints = new Vector3[ExtrudedEdges.Length];
        // Euler circuit: both box cages plus all eight vertical connecting edges.
        static int[] BuildExtrudedEdges()
        {
            var links = new bool[16,16];
            for (int i = 0; i < 8; i++)
            {
                for (int bit = 1; bit <= 4; bit *= 2)
                {
                    int j = i ^ bit;
                    links[i,j] = links[i+8,j+8] = true;
                }
                links[i,i+8] = links[i+8,i] = true;
            }
            var stack = new System.Collections.Generic.Stack<int>();
            var path = new System.Collections.Generic.List<int>();
            stack.Push(0);
            while (stack.Count > 0)
            {
                int a = stack.Peek(), next = -1;
                for (int b = 0; b < 16; b++) if (links[a,b]) { next = b; break; }
                if (next < 0) path.Add(stack.Pop());
                else { links[a,next] = links[next,a] = false; stack.Push(next); }
            }
            return path.ToArray();
        }

        void LateUpdate() => Refresh();
        public void Refresh()
        {
            if (hit == null) hit = GetComponent<BossWeaponHit>();
            bool visible = showHitbox && hit != null && hit.isActiveAndEnabled && hit.weapon != null;
            if (!visible) { if (line != null) line.enabled = false; return; }
            if (line == null)
            {
                if (lineMaterial == null) return;
                var go = new GameObject("BossHitbox_DebugOnly") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, false);
                line = go.AddComponent<LineRenderer>();
                instanceMaterial = new Material(lineMaterial) { hideFlags = HideFlags.DontSave };
                line.sharedMaterial = instanceMaterial;
                line.useWorldSpace = true;
                line.positionCount = points.Length;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.lightProbeUsage = LightProbeUsage.Off;
                line.reflectionProbeUsage = ReflectionProbeUsage.Off;
                line.startColor = line.endColor = Color.white;
            }
            hit.GetWorldBox(out var center, out var rotation, out var half);
            float down = hit.CurrentDownwardExtension;
            var vertices = down > 0 ? extendedPoints : points;
            var indices = down > 0 ? ExtrudedEdges : Edges;
            for (int i = 0; i < vertices.Length; i++)
            {
                int corner = indices[i];
                vertices[i] = center + rotation * new Vector3(
                    (corner & 1) == 0 ? -half.x : half.x,
                    (corner & 2) == 0 ? -half.y : half.y,
                    (corner & 4) == 0 ? -half.z : half.z) + ((corner & 8) != 0 ? Vector3.down * down : Vector3.zero);
            }
            line.positionCount = vertices.Length;
            line.SetPositions(vertices);
            line.startWidth = line.endWidth = lineWidth;
            instanceMaterial.SetColor("_BaseColor", hit.WindowOpen ? activeColor : inactiveColor);
            line.enabled = true;
        }
        void OnGUI()
        {
            if (!showControls || !Application.isPlaying) return;
            if (hit == null) hit = GetComponent<BossWeaponHit>();
            GUILayout.BeginArea(new Rect(12, Screen.height - 230, 470, 130), GUI.skin.box);
            showHitbox = GUILayout.Toggle(showHitbox, "Boss weapon hitbox (yellow: OFF / red: ON)");
            if (showHitbox && hit != null)
            {
                var pattern = hit.CurrentPattern;
                GUILayout.Label(pattern != null ? pattern.displayName : "No attack");
                if (pattern != null)
                {
                    string window = "OFF";
                    if (hit.CurrentStrikeIndex >= 0)
                    {
                        var w = pattern.GetHitWindow(hit.CurrentStrikeIndex);
                        window = $"{w.startSeconds * pattern.clip.frameRate:F0}-{w.endSeconds * pattern.clip.frameRate:F0}";
                    }
                    GUILayout.Label($"Frame {hit.CurrentFrame:F1} | Strike {hit.CurrentStrikeIndex + 1}/{pattern.StrikeCount} | Active {window} | Hits {hit.HitCount} | Down {hit.CurrentDownwardExtension:F1}");
                    var motion = GetComponent<BossAttackMotion>();
                    if (motion != null && pattern.stepDecisionSeconds != null && pattern.stepDecisionSeconds.Length > 0)
                        GUILayout.Label($"Step {motion.CurrentStepIndex + 1}: {(motion.StepAllowed ? "MOVE" : "STAY")} | Decision distance {motion.DecisionDistance:F2}");
                }
            }
            GUILayout.EndArea();
        }
        void OnDisable() { if (line != null) line.enabled = false; }
        void OnDestroy()
        {
            if (Application.isPlaying)
            {
                if (line != null) Destroy(line.gameObject);
                if (instanceMaterial != null) Destroy(instanceMaterial);
            }
            else
            {
                if (line != null) DestroyImmediate(line.gameObject);
                if (instanceMaterial != null) DestroyImmediate(instanceMaterial);
            }
        }
    }
}
