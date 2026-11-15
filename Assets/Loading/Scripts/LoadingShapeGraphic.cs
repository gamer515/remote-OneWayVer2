using UnityEngine;
using UnityEngine.UI;

namespace OneWay.Loading
{
    // Resolution-independent shapes; no texture imports or third-party UI assets are needed.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class LoadingShapeGraphic : MaskableGraphic
    {
        public bool ellipse;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect r = GetPixelAdjustedRect();
            if (r.width <= 0 || r.height <= 0) return;
            Color32 tint = color;
            mesh.AddVert(r.center, tint, new Vector2(.5f, .5f));
            const int segments = 64;
            float radius = Mathf.Min(r.width, r.height) * .5f;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 point;
                if (ellipse)
                    point = r.center + Vector2.Scale(direction, r.size * .5f);
                else
                {
                    Vector2 corner = r.center + new Vector2(
                        Mathf.Sign(direction.x) * (r.width * .5f - radius),
                        Mathf.Sign(direction.y) * (r.height * .5f - radius));
                    point = corner + direction * radius;
                }
                mesh.AddVert(point, tint, new Vector2(.5f, .5f));
            }
            for (int i = 0; i < segments; i++)
                mesh.AddTriangle(0, i + 1, (i + 1) % segments + 1);
        }
    }
}
