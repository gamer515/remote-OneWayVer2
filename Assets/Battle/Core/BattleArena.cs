using UnityEngine;

public sealed class BattleArena
{
    public enum Boundary { None, Screen, Box, Corridor }
    public Boundary Mode = Boundary.None;
    readonly Camera camera;
    readonly BattleSceneBattleBoxController box;
    public float CorridorWidth = 0.48f;
    public BattleArena(Camera camera, BattleSceneBattleBoxController box)
    {
        this.camera = camera;
        this.box = box;
    }
    public Rect ScreenRect
    {
        get
        {
            float depth = Vector3.Dot(-camera.transform.position, camera.transform.forward);
            Vector3 a = camera.ViewportToWorldPoint(new Vector3(0, 0, depth));
            Vector3 b = camera.ViewportToWorldPoint(new Vector3(1, 1, depth));
            return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
        }
    }
    public Rect Bounds
    {
        get
        {
            Rect r = ScreenRect;
            if (Mode == Boundary.Box)
                return Rect.MinMaxRect(box.leftWall.position.x + box.wallThickness / 2,
                    box.bottomWall.position.y + box.wallThickness / 2,
                    box.rightWall.position.x - box.wallThickness / 2,
                    box.topWall.position.y - box.wallThickness / 2);
            if (Mode == Boundary.Corridor)
                return new Rect(r.center.x - r.width * CorridorWidth / 2, r.yMin,
                    r.width * CorridorWidth, r.height);
            return r;
        }
    }
    public Vector2 Clamp(Vector2 position, Vector2 halfSize)
    {
        if (Mode == Boundary.None) return position;
        Rect r = Bounds;
        float x = Mathf.Min(halfSize.x, r.width / 2);
        float y = Mathf.Min(halfSize.y, r.height / 2);
        return new Vector2(Mathf.Clamp(position.x, r.xMin + x, r.xMax - x),
            Mathf.Clamp(position.y, r.yMin + y, r.yMax - y));
    }
}
