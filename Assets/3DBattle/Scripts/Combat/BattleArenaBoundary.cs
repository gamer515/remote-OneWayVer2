using UnityEngine;

namespace OneWay.Battle3D
{
    public sealed class BattleArenaBoundary : MonoBehaviour
    {
        public BoxCollider[] walls = new BoxCollider[0];

        // Infinite-height inward planes keep bodies inside even during jumps.
        public Vector3 Constrain(Vector3 center, float radius)
        {
            Vector3 interior = Vector3.zero;
            int count = 0;
            foreach (var wall in walls)
                if (wall != null) { interior += wall.transform.TransformPoint(wall.center); count++; }
            if (count == 0) return center;
            interior /= count;
            for (int pass = 0; pass < 4; pass++)
            foreach (var wall in walls)
            {
                if (wall == null) continue;
                var t = wall.transform;
                Vector3 size = Vector3.Scale(wall.size, t.lossyScale);
                bool thinX = Mathf.Abs(size.x) < Mathf.Abs(size.z);
                Vector3 normal = thinX ? t.right : t.forward;
                normal.y = 0; normal.Normalize();
                Vector3 wallCenter = t.TransformPoint(wall.center);
                if (Vector3.Dot(interior - wallCenter, normal) < 0) normal = -normal;
                float thickness = Mathf.Abs(thinX ? size.x : size.z) * .5f;
                float distance = Vector3.Dot(center - wallCenter, normal);
                float needed = thickness + Mathf.Max(0f, radius) + .01f - distance;
                if (needed > 0) center += normal * needed;
            }
            return center;
        }
    }
}
