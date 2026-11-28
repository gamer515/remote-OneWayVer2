using UnityEngine;
using UnityEngine.UI;

namespace OneWay.Battle3D
{
    [DisallowMultipleComponent]
    public sealed class BattleHudVerticalGradient : BaseMeshEffect
    {
        public Color bottom = new Color(.035f, .52f, .23f);
        public Color top = new Color(.13f, .77f, .37f);

        public override void ModifyMesh(VertexHelper vertices)
        {
            if (!IsActive() || vertices.currentVertCount == 0) return;
            Rect r = graphic.rectTransform.rect;
            UIVertex vertex = UIVertex.simpleVert;
            for (int i = 0; i < vertices.currentVertCount; i++)
            {
                vertices.PopulateUIVertex(ref vertex, i);
                vertex.color = (Color)vertex.color * Color.Lerp(bottom, top, Mathf.InverseLerp(r.yMin, r.yMax, vertex.position.y));
                vertices.SetUIVertex(vertex, i);
            }
        }
    }
}
