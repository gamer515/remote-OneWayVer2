using UnityEngine;

public sealed class SlashTarget3D
{
    public readonly GameObject Object;
    public bool Resolved { get; private set; }
    readonly BattleContext context;
    readonly BattleStepScope scope;
    readonly float speed;
    GameObject first, second;
    Vector3 splitDirection;
    float fragmentAge;
    public bool Finished => Resolved && (first == null && second == null);

    public SlashTarget3D(BattleContext c, BattleStepScope owner, Vector3 position, int index)
    {
        context = c; scope = owner; speed = c.Stage.slashSpeed;
        Object = c.Primitive("Slash Target " + (index + 1), PrimitiveType.Sphere, scope.Root, position, Vector3.one * 2.2f);
    }
    public static bool CrossedArrival(float previousZ, float currentZ) => previousZ > 0 && currentZ <= 0;
    public void Tick(float dt)
    {
        if (!Resolved)
        {
            float previous = Object.transform.position.z;
            Object.transform.position += Vector3.back * (speed * dt);
            if (CrossedArrival(previous, Object.transform.position.z))
            {
                Resolved = true;
                UnityEngine.Object.Destroy(Object);
                context.Player.TakeArrivalDamage(context.Stage.slashDamage);
            }
        }
        else if (first != null || second != null)
        {
            fragmentAge += dt;
            Vector3 fall = Vector3.down * (fragmentAge * 2f) + Vector3.back * 1.5f;
            if (first != null) first.transform.position += (splitDirection * 2.5f + fall) * dt;
            if (second != null) second.transform.position += (-splitDirection * 2.5f + fall) * dt;
            if (fragmentAge > 1f) { UnityEngine.Object.Destroy(first); UnityEngine.Object.Destroy(second); }
        }
    }
    public bool TrySlash(Vector2 a, Vector2 b)
    {
        if (Resolved || Object == null) return false;
        var filter = Object.GetComponent<MeshFilter>();
        if (!SlashResolver.Hits(context.Camera, filter, a, b, context.Stage.slashWidthPixels)) return false;
        Ray ra = context.Camera.ScreenPointToRay(a), rb = context.Camera.ScreenPointToRay(b);
        Vector3 normal = Vector3.Cross(ra.direction, rb.direction).normalized;
        if (normal.sqrMagnitude < .5f) return false;
        var worldPlane = new Plane(normal, context.Camera.transform.position);
        Vector3 point = worldPlane.ClosestPointOnPlane(Object.transform.position);
        // A thick stroke can graze the silhouette: keep the cut just inside its surface.
        float distance = worldPlane.GetDistanceToPoint(Object.transform.position);
        float radius = Object.transform.localScale.x * .5f;
        if (Mathf.Abs(distance) >= radius) point = Object.transform.position - normal * Mathf.Sign(distance) * radius * .95f;
        var localPlane = new Plane(Object.transform.InverseTransformDirection(normal), Object.transform.InverseTransformPoint(point));
        Mesh left = SlashResolver.Clip(filter.sharedMesh, localPlane, true);
        Mesh right = SlashResolver.Clip(filter.sharedMesh, localPlane, false);
        scope.OnDispose(() => { UnityEngine.Object.Destroy(left); UnityEngine.Object.Destroy(right); });
        first = Fragment(left, "Cut A"); second = Fragment(right, "Cut B");
        splitDirection = normal;
        Resolved = true;
        UnityEngine.Object.Destroy(Object);
        return true;
    }
    GameObject Fragment(Mesh mesh, string name)
    {
        var go = context.Primitive(name, PrimitiveType.Cube, scope.Root, Object.transform.position, Object.transform.localScale);
        go.transform.rotation = Object.transform.rotation;
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        return go;
    }
}
