using UnityEngine;
using UnityEngine.EventSystems;

public sealed class B1ScrollMovement
{
    float grace;
    bool armed = true;
    public void GiveGrace(float seconds) { grace = seconds; armed = false; }
    public bool Tick(BattleContext c)
    {
        float dt = c.Clock.Delta;
        if (dt <= 0) return false;
        Rect bounds = c.Arena.Bounds;
        Vector2 half = c.Player.HalfSize;
        Vector2 position = c.Player.transform.position;
        Vector3 mouse = c.Camera.ScreenToWorldPoint(new Vector3(Input.mousePosition.x,
            Input.mousePosition.y, -c.Camera.transform.position.z));
        float targetX = Mathf.Clamp(mouse.x, bounds.xMin + half.x, bounds.xMax - half.x);
        position.x = Mathf.Lerp(position.x, targetX, 1f - Mathf.Exp(-c.Stage.mouseFollow * dt));
        if (grace > 0) grace -= dt;
        else position.y -= c.Stage.backwardSpeed * dt;
        if (Input.GetMouseButtonDown(0) && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
            position.y += c.Stage.clickAdvance;
        float limit = Mathf.Lerp(c.Arena.ScreenRect.yMin, c.Arena.ScreenRect.yMax, c.Stage.forwardViewportY) - half.y;
        position.y = Mathf.Min(position.y, limit);
        float rear = bounds.yMin + half.y;
        bool hit = armed && position.y <= rear;
        position.y = Mathf.Max(position.y, rear);
        if (position.y > rear + .1f) armed = true;
        c.Player.transform.position = new Vector3(position.x, position.y, 0);
        return hit;
    }
}
