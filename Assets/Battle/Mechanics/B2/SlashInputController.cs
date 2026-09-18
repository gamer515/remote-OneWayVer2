using UnityEngine;
using UnityEngine.EventSystems;

public sealed class SlashInputController
{
    bool dragging;
    Vector2 start;
    public bool Tick(BattleContext c, out Vector2 a, out Vector2 b)
    {
        a = b = Vector2.zero;
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (Input.GetMouseButtonDown(0) && !overUI) { start = Input.mousePosition; dragging = true; }
        if (dragging && (!Application.isFocused || Input.GetKeyDown(KeyCode.Escape))) Cancel(c);
        if (!dragging) return false;
        c.DrawSlash(start, Input.mousePosition, true);
        if (!Input.GetMouseButtonUp(0)) return false;
        a = start; b = Input.mousePosition;
        Cancel(c);
        return !overUI && Vector2.Distance(a, b) >= 4f;
    }
    public void Cancel(BattleContext c) { dragging = false; c.DrawSlash(Vector2.zero, Vector2.zero, false); }
}
