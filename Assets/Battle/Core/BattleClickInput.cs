using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class BattleClickInput
{
    private static int consumedFrame = -1;

    private static readonly List<RaycastResult> hits =
        new List<RaycastResult>();

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        consumedFrame = -1;
        hits.Clear();
    }

    public static bool TryConsume()
    {
        if (!Application.isFocused ||
            !Input.GetMouseButtonDown(0) ||
            consumedFrame == Time.frameCount)
        {
            return false;
        }

        // 패턴 선택 버튼 등 조작 가능한 UI 클릭은 제외.
        // 대화창 배경이나 텍스트를 클릭하는 것은 허용.
        EventSystem events = EventSystem.current;

        if (events != null)
        {
            var pointer = new PointerEventData(events)
            {
                position = Input.mousePosition
            };

            hits.Clear();
            events.RaycastAll(pointer, hits);

            foreach (RaycastResult hit in hits)
            {
                if (hit.gameObject.GetComponentInParent<Selectable>() != null)
                    return false;
            }
        }

        // 이 프레임의 클릭은 한 곳에서만 사용.
        consumedFrame = Time.frameCount;
        return true;
    }
}