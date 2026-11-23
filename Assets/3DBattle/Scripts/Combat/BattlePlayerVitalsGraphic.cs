using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OneWay.Battle3D
{
    // One shared metal frame. Energy colour coordinates span both slots, not each slot.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BattlePlayerVitalsGraphic : MaskableGraphic
    {
        [Header("Preview / displayed values")]
        [Range(0, 1)] public float health = 1;
        [Range(0, 1)] public float damage = 1;
        [Range(0, 1)] public float energy = .5f;
        [Header("Colours")]
        public Color healthBottom = new Color(.035f, .52f, .23f);
        public Color healthTop = new Color(.13f, .77f, .37f);
        public Color energyBlue = new Color(.035f, .09f, .78f);
        public Color energySky = new Color(.32f, .84f, 1f);
        public Color energyWhite = new Color(.96f, .99f, 1f);
        [Header("Metal frame")]
        public Color copperMetal = new Color(.64f, .32f, .15f);
        public Color silverMetal = new Color(.61f, .65f, .71f);
        public Color goldMetal = new Color(.93f, .64f, .15f);
        public Color silverGlow = Color.white;
        public Color goldInnerGlow = Color.white;
        public Color goldOuterGlow = new Color(1f, .78f, .055f);
        public enum FrameMetal { Copper, Silver, Gold }
        public FrameMetal Metal => energy >= 1f - .00001f ? FrameMetal.Gold
            : energy >= .5f - .00001f ? FrameMetal.Silver : FrameMetal.Copper;
        public float FrameGlow01 => Metal == FrameMetal.Gold ? 1 : Metal == FrameMetal.Silver ? .5f : 0;

        sealed class FrameCache
        {
            public readonly List<UIVertex> vertices = new List<UIVertex>(256);
            public readonly List<int> indices = new List<int>(768);
            public bool valid;
        }
        readonly FrameCache[] frames = { new FrameCache(), new FrameCache(), new FrameCache() };
        readonly List<List<Vector2>> pointBuffers = new List<List<Vector2>>(64);
        readonly List<int> triangulation = new List<int>(32);
        readonly VertexHelper frameBuilder = new VertexHelper();
        Mesh frameMesh;
        int pointCursor;
        Rect cachedRect;
        Color cachedCopper, cachedSilver, cachedGold, cachedSilverGlow, cachedGoldInner, cachedGoldOuter;
        static readonly float[] ReflectionStops = { 0f, .16f, .38f, .46f, .55f, .72f, .88f, 1f };
        static readonly List<Vector2> HealthShape = new List<Vector2> {
            new Vector2(6, 9.5f), new Vector2(10, 5.5f), new Vector2(76, 5.5f),
            new Vector2(76, 23), new Vector2(81, 28), new Vector2(410, 28),
            new Vector2(414, 32), new Vector2(414, 54.5f), new Vector2(410, 58.5f),
            new Vector2(10, 58.5f), new Vector2(6, 54.5f)
        };

        List<Vector2> Points()
        {
            if (pointCursor == pointBuffers.Count) pointBuffers.Add(new List<Vector2>(24));
            var points = pointBuffers[pointCursor++]; points.Clear(); return points;
        }

        void AppendFrame(VertexHelper vh)
        {
            var rect = rectTransform.rect;
            if (cachedRect != rect || cachedCopper != copperMetal || cachedSilver != silverMetal || cachedGold != goldMetal ||
                cachedSilverGlow != silverGlow || cachedGoldInner != goldInnerGlow || cachedGoldOuter != goldOuterGlow)
            {
                foreach (var item in frames) item.valid = false;
                cachedRect = rect; cachedCopper = copperMetal; cachedSilver = silverMetal; cachedGold = goldMetal;
                cachedSilverGlow = silverGlow; cachedGoldInner = goldInnerGlow; cachedGoldOuter = goldOuterGlow;
            }
            var cache = frames[(int)Metal];
            if (!cache.valid)
            {
                frameBuilder.Clear(); pointCursor = 0; DrawMetalFrame(frameBuilder);
                cache.vertices.Clear();
                var vertex = UIVertex.simpleVert;
                for (int i = 0; i < frameBuilder.currentVertCount; i++)
                { frameBuilder.PopulateUIVertex(ref vertex, i); cache.vertices.Add(vertex); }
                if (!frameMesh) frameMesh = new Mesh { name = "HUD frame cache", hideFlags = HideFlags.HideAndDontSave };
                frameBuilder.FillMesh(frameMesh); frameMesh.GetTriangles(cache.indices, 0);
                cache.valid = true;
            }
            vh.AddUIVertexStream(cache.vertices, cache.indices);
        }

        protected override void OnDestroy()
        {
            frameBuilder.Dispose();
            if (frameMesh) { if (Application.isPlaying) Destroy(frameMesh); else DestroyImmediate(frameMesh); }
            base.OnDestroy();
        }

        public void SetValues(float hp, float recentDamage, float charge)
        {
            hp = Mathf.Clamp01(hp); recentDamage = Mathf.Clamp01(recentDamage); charge = Mathf.Clamp01(charge);
            if (Mathf.Approximately(health, hp) && Mathf.Approximately(damage, recentDamage) && Mathf.Approximately(energy, charge)) return;
            health = hp; damage = recentDamage; energy = charge; SetVerticesDirty();
        }

        public Color EnergyColour(float globalProgress)
        {
            float t = Mathf.Clamp01(globalProgress);
            return t <= .75f ? Color.Lerp(energyBlue, energySky, t / .75f)
                : Color.Lerp(energySky, energyWhite, (t - .75f) / .25f);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); if (rectTransform.rect.width < 1 || rectTransform.rect.height < 1) return;
            AppendFrame(vh);
            pointCursor = 0;

            // Keep the HUD footprint, with a 3.5–4 px outer rim and 2 px row separator.
            // The health silhouette wraps down the left of the inset energy slots.
            var hpShape = HealthShape;
            Polygon(vh, hpShape, new Color(.015f, .11f, .065f), new Color(.035f, .20f, .11f), 5.5f, 58.5f);
            Polygon(vh, ClipX(hpShape, 6, 6 + 408 * damage), new Color(.52f, .075f, .07f), new Color(.85f, .19f, .15f), 5.5f, 58.5f);
            Polygon(vh, ClipX(hpShape, 6, 6 + 408 * health), healthBottom, healthTop, 5.5f, 58.5f);
            Polygon(vh, ClipX(ClipY(hpShape, 57.7f, 58.5f), 6, 6 + 408 * health), new Color(.52f, 1, .67f, .36f));

            const float left = 80, right = 414, gap = 2, bottom = 5.5f, top = 26;
            float slotWidth = (right - left - gap) * .5f;
            for (int slot = 0; slot < 2; slot++)
            {
                float x0 = left + slot * (slotWidth + gap), x1 = x0 + slotWidth;
                var shape = Bevel(x0, bottom, x1, top, 4);
                // Both dividers use the global material tier, even when one slot is empty.
                Polygon(vh, Bevel(x0 - .65f, bottom - .65f, x1 + .65f, top + .65f, 4.3f),
                    MetalColour(.2f), MetalColour(.88f), bottom - .65f, top + .65f);
                Polygon(vh, shape, new Color(.015f, .025f, .075f), new Color(.035f, .055f, .13f), bottom, top);
                float localFill = Mathf.Clamp01(energy * 2 - slot);
                // Keep the global 75% sky-blue stop and bevel corners explicit. Twelve
                // narrow bands retain the vertical shading without 48 clipping passes.
                float a = 0;
                while (a < localFill - .000001f)
                {
                    float b = Mathf.Min((Mathf.Floor(a * 12 + .0001f) + 1) / 12f, localFill);
                    float corner = 4 / slotWidth;
                    if (a < corner - .000001f) b = Mathf.Min(b, corner);
                    if (a < 1 - corner - .000001f) b = Mathf.Min(b, 1 - corner);
                    float from = Mathf.Lerp(x0, x1, a), to = Mathf.Lerp(x0, x1, b);
                    var section = ClipX(shape, from, to);
                    PolygonEnergy(vh, section, x0, slotWidth, slot, bottom, top);
                    a = b;
                }
                if (localFill > 0)
                {
                    var highlight = ClipX(ClipY(shape, top - 1, top), x0, x0 + slotWidth * localFill);
                    Polygon(vh, highlight, new Color(.78f, .92f, 1, .25f));
                }
            }
        }

        void DrawMetalFrame(VertexHelper vh)
        {
            var outside = Bevel(2, 2, 418, 62, 8);
            Polygon(vh, Bevel(1, -1, 421, 60, 8), new Color(0, 0, 0, .7f));
            if (Metal == FrameMetal.Silver)
            {
                // A restrained, entirely white halo; the silver face stays readable.
                Ring(vh, Bevel(-5, -5, 425, 69, 12), Bevel(-1, -1, 421, 65, 10),
                    Alpha(silverGlow, 0), Alpha(silverGlow, .055f));
                Ring(vh, Bevel(-1, -1, 421, 65, 10), outside,
                    Alpha(silverGlow, .055f), Alpha(silverGlow, .23f));
            }
            else if (Metal == FrameMetal.Gold)
            {
                // The white core is nearest the frame; the wider falloff is yellow.
                Ring(vh, Bevel(-12, -12, 432, 76, 18), Bevel(-6, -6, 426, 70, 13),
                    Alpha(goldOuterGlow, 0), Alpha(goldOuterGlow, .075f));
                Ring(vh, Bevel(-6, -6, 426, 70, 13), Bevel(-1, -1, 421, 65, 10),
                    Alpha(goldOuterGlow, .075f), Alpha(goldOuterGlow, .38f));
                Ring(vh, Bevel(-1, -1, 421, 65, 10), Bevel(.8f, .8f, 419.2f, 63.2f, 8.7f),
                    Alpha(goldOuterGlow, .38f), Alpha(goldInnerGlow, .72f));
                Ring(vh, Bevel(.8f, .8f, 419.2f, 63.2f, 8.7f), outside,
                    Alpha(goldInnerGlow, .72f), Alpha(goldInnerGlow, .96f));
            }

            // Alternating reflection bands and bevels give the face a metallic finish.
            // These are surface highlights, so copper has no outward light at all.
            var stops = ReflectionStops;
            for (int i = 1; i < stops.Length; i++)
            {
                float bottom = Mathf.Lerp(2, 62, stops[i - 1]), top = Mathf.Lerp(2, 62, stops[i]);
                Polygon(vh, ClipY(outside, bottom, top), MetalColour(stops[i - 1]), MetalColour(stops[i]), bottom, top);
            }
            Ring(vh, outside, Bevel(2.8f, 2.8f, 417.2f, 61.2f, 7.5f), MetalColour(.16f), MetalColour(.88f));
            // Keep the continuous metal surface visible between the bars and the outer rim.
            // Empty health/energy backgrounds are drawn only inside their own silhouettes.
            Polygon(vh, Bevel(12, 60.7f, 408, 61.2f, .2f), Alpha(MetalColour(1), .8f));
            if (Metal != FrameMetal.Copper)
            {
                Color edge = Metal == FrameMetal.Gold ? Alpha(goldInnerGlow, .85f) : Alpha(silverGlow, .28f);
                Ring(vh, outside, Bevel(2.45f, 2.45f, 417.55f, 61.55f, 7.7f), edge, edge);
            }
        }

        Color MetalColour(float height)
        {
            Color body = Metal == FrameMetal.Gold ? goldMetal : Metal == FrameMetal.Silver ? silverMetal : copperMetal;
            Color dark = Color.Lerp(Color.black, body, Metal == FrameMetal.Copper ? .32f : .36f);
            Color highlight = Color.Lerp(body, Color.white, Metal == FrameMetal.Copper ? .24f : Metal == FrameMetal.Silver ? .72f : .40f);
            Color mid = Color.Lerp(Color.black, body, .70f), trough = Color.Lerp(Color.black, body, .58f);
            if (height < .16f) return Color.Lerp(dark, mid, height / .16f);
            if (height < .38f) return Color.Lerp(mid, body, (height - .16f) / .22f);
            if (height < .46f) return Color.Lerp(body, highlight, (height - .38f) / .08f);
            if (height < .55f) return Color.Lerp(highlight, trough, (height - .46f) / .09f);
            if (height < .88f) return Color.Lerp(trough, highlight, (height - .55f) / .33f);
            return Color.Lerp(highlight, body, (height - .88f) / .12f);
        }

        static Color Alpha(Color c, float alpha) { c.a = alpha; return c; }
        List<Vector2> Bevel(float x0, float y0, float x1, float y1, float cut)
        {
            var p = Points();
            p.Add(new Vector2(x0 + cut, y0)); p.Add(new Vector2(x1 - cut, y0));
            p.Add(new Vector2(x1, y0 + cut)); p.Add(new Vector2(x1, y1 - cut)); p.Add(new Vector2(x1 - cut, y1));
            p.Add(new Vector2(x0 + cut, y1)); p.Add(new Vector2(x0, y1 - cut)); p.Add(new Vector2(x0, y0 + cut));
            return p;
        }
        Vector3 Point(Vector2 p)
        {
            var r = rectTransform.rect; return new Vector3(r.xMin + p.x / 420f * r.width, r.yMin + p.y / 64f * r.height);
        }
        void Ring(VertexHelper vh, List<Vector2> outer, List<Vector2> inner, Color a, Color b)
        {
            int first = vh.currentVertCount;
            for (int i = 0; i < outer.Count; i++) { vh.AddVert(Point(outer[i]), a, Vector2.zero); vh.AddVert(Point(inner[i]), b, Vector2.zero); }
            for (int i = 0; i < outer.Count; i++) { int p = first + i * 2, q = first + (i + 1) % outer.Count * 2; vh.AddTriangle(p, q, q + 1); vh.AddTriangle(p, q + 1, p + 1); }
        }
        void Polygon(VertexHelper vh, List<Vector2> points, Color c) => Polygon(vh, points, c, c, 0, 64);
        void Polygon(VertexHelper vh, List<Vector2> points, Color bottom, Color top, float y0, float y1)
        {
            if (points.Count < 3) return;
            int first = vh.currentVertCount;
            foreach (var p in points) vh.AddVert(Point(p), Color.Lerp(bottom, top, Mathf.InverseLerp(y0, y1, p.y)), Vector2.zero);
            Triangulate(vh, points, first);
        }
        void PolygonEnergy(VertexHelper vh, List<Vector2> points, float x0, float width, int slot, float bottom, float top)
        {
            if (points.Count < 3) return;
            int first = vh.currentVertCount;
            foreach (var p in points)
            {
                var c = EnergyColour((slot + (p.x - x0) / width) * .5f);
                float shade = Mathf.Lerp(.86f, 1, Mathf.InverseLerp(bottom, top, p.y));
                c.r *= shade; c.g *= shade; c.b *= shade;
                vh.AddVert(Point(p), c, Vector2.zero);
            }
            Triangulate(vh, points, first);
        }
        // Ear clipping also handles the concave health silhouette and clipped L-shaped fills.
        void Triangulate(VertexHelper vh, List<Vector2> p, int first)
        {
            var ids = triangulation; ids.Clear(); for (int i = 0; i < p.Count; i++) ids.Add(i);
            for (int guard = 0; ids.Count > 2 && guard < p.Count * p.Count; guard++)
            {
                bool found = false;
                for (int i = 0; i < ids.Count; i++)
                {
                    int a = ids[(i + ids.Count - 1) % ids.Count], b = ids[i], c = ids[(i + 1) % ids.Count];
                    if (Cross(p[b] - p[a], p[c] - p[b]) <= .000001f) continue;
                    bool inside = false;
                    foreach (int j in ids) if (j != a && j != b && j != c &&
                        Cross(p[b] - p[a], p[j] - p[a]) > .000001f && Cross(p[c] - p[b], p[j] - p[b]) > .000001f &&
                        Cross(p[a] - p[c], p[j] - p[c]) > .000001f) { inside = true; break; }
                    if (inside) continue;
                    vh.AddTriangle(first + a, first + b, first + c); ids.RemoveAt(i); found = true; break;
                }
                if (!found) break;
            }
        }
        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        List<Vector2> ClipX(List<Vector2> p, float min, float max) => Clip(Clip(p, 0, min, true), 0, max, false);
        List<Vector2> ClipY(List<Vector2> p, float min, float max) => Clip(Clip(p, 1, min, true), 1, max, false);
        List<Vector2> Clip(List<Vector2> p, int axis, float value, bool above)
        {
            var output = Points(); if (p.Count == 0) return output;
            Vector2 previous = p[p.Count - 1]; bool previousInside = above ? previous[axis] >= value : previous[axis] <= value;
            foreach (var current in p)
            {
                bool inside = above ? current[axis] >= value : current[axis] <= value;
                if (inside != previousInside) output.Add(Vector2.Lerp(previous, current, (value - previous[axis]) / (current[axis] - previous[axis])));
                if (inside) output.Add(current); previous = current; previousInside = inside;
            }
            return output;
        }
    }
}
