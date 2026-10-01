using UnityEngine;
using UnityEngine.UI;

public class DrawGizmoAreas : MonoBehaviour
{
    [Header("Gizmo Colors")]
    [SerializeField] private Color gizmoGColor = Color.green;
    [SerializeField] private Color gizmoRColor = Color.red;
    [SerializeField] private Color gizmoBColor = Color.blue;
    [SerializeField] private Color gizmoYColor = Color.yellow;

    [Header("Gizmo Areas")]
    [SerializeField] private Vector2 npcGizmoPoint = new Vector2(-1, -1);
    [SerializeField] private float pointOffset = 100f;
    [SerializeField] private float pointSize = 200f;

    [Header("OnSet")]
    [SerializeField] private bool drawGizmoOnSet = true;

    private int drawGizmoCount = 4;

    #region
    /// <summary>
    /// 해당 변수의 경우는 실제 플레이할 경우, NPC의 위치를 가져와서 그려야 하는데, 
    /// 현재는 임시로 고정된 위치를 사용하고 있습니다.
    /// </summary>
    //private Camera npcGizmoObj;
    //private Transform npc;
    //private RawImage rawImage;
    //private Canvas canvas;
    #endregion

    private void OnDrawGizmos()
    {
        if (!drawGizmoOnSet) return;

        npcGizmoPoint = new Vector2(628f, 675f);

        int j = 0;
        for (int i = 0; i < drawGizmoCount; i++)
        {
            switch (i)
            {
                case 0:
                    Gizmos.color = gizmoGColor;
                    break;
                case 1:
                    Gizmos.color = gizmoRColor;
                    break;
                case 2:
                    Gizmos.color = gizmoBColor;
                    break;
                case 3:
                    Gizmos.color = gizmoYColor;
                    break;
            }

            int signX = (i % 2 == 0) ? 1 : -1;
            int signY = (i < 2) ? 1 : -1;

            Vector3 position = new Vector3(
                            npcGizmoPoint.x + signX * pointOffset,
                            npcGizmoPoint.y + signY * pointOffset,
                            10f);

            Vector3 worldPoint = Camera.main.ScreenToWorldPoint(position);

            Vector3 rightPoint = Camera.main.ScreenToWorldPoint(
                position + new Vector3(pointSize, 0f, 0f));

            Vector3 upPoint = Camera.main.ScreenToWorldPoint(
                position + new Vector3(0f, pointSize, 0f));

            float width = Vector3.Distance(worldPoint, rightPoint);
            float height = Vector3.Distance(worldPoint, upPoint);

            Matrix4x4 previousMatrix = Gizmos.matrix;

            Gizmos.matrix = Matrix4x4.TRS(
                worldPoint,
                Camera.main.transform.rotation,
                Vector3.one);

            Gizmos.DrawCube(Vector3.zero, new Vector3(width, height, 0.01f));

            Gizmos.matrix = previousMatrix;
        }
    }
}
