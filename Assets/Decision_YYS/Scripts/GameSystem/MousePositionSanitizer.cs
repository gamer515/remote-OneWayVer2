#if ENABLE_INPUT_SYSTEM
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

internal static class MousePositionSanitizer
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        InputSystem.onEvent -= SanitizeEvent;
        InputSystem.onEvent += SanitizeEvent;
    }

    private static void SanitizeEvent(InputEventPtr inputEvent, InputDevice device)
    {
        if (device is not Mouse mouse || (!inputEvent.IsA<StateEvent>() && !inputEvent.IsA<DeltaStateEvent>()))
            return;

#if UNITY_EDITOR
        if (!Application.isPlaying)
            return;
#endif

        // Preserve button and scroll changes in the same event so releases still end drags.
        RestoreInvalidCoordinate(mouse.position.x, inputEvent);
        RestoreInvalidCoordinate(mouse.position.y, inputEvent);
    }

    private static void RestoreInvalidCoordinate(AxisControl axis, InputEventPtr inputEvent)
    {
        if (!axis.ReadValueFromEvent(inputEvent, out float coordinate) || float.IsFinite(coordinate))
            return;

        float previous = axis.ReadValue();
        axis.WriteValueIntoEvent(float.IsFinite(previous) ? previous : 0f, inputEvent);
    }
}
#endif
