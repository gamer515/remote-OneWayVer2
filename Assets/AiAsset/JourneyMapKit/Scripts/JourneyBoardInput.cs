using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace JourneyMapKit
{
    /// <summary>
    /// JourneyController의 기어와 노란 버튼 입력을 처리합니다.
    /// </summary>
    public sealed class JourneyBoardInput : MonoBehaviour
    {
        public JourneyBoardReferences board;
        public Camera inputCamera;

        [Tooltip("보드 카메라를 RawImage로 표시할 때만 연결합니다.")]
        public RawImage boardViewport;

        public UnityEvent onYellowPressed = new UnityEvent();
        public UnityEvent<int> onGearSelected = new UnityEvent<int>();

        [SerializeField, Min(1)] private float choiceStepPixels = 14f;
        public int SelectedIndex { get; private set; } = -1;
        public bool InputEnabled { get; private set; } = true;

        private bool yellowInteractable = true;
        private bool dragging;
        private float choiceStepOriginY;
        private int choiceCount;
        private Vector2 dragOrigin;
        private Quaternion restRotation;
        private Vector3 restButtonPosition;
        private Coroutine pressRoutine;

        private void Awake()
        {
            if (board == null)
                board = GetComponent<JourneyBoardReferences>();
            if (inputCamera == null)
                inputCamera = Camera.main;

            if (board != null)
            {
                restRotation = board.gear.localRotation;
                restButtonPosition = board.yellowButton.transform.localPosition;
            }
        }

        private void Update()
        {
            if (!InputEnabled || board == null || inputCamera == null)
                return;

            ReadPointer(out Vector2 pointerPosition, out bool down, out bool held, out bool up);

            if (down && TryCreateRay(pointerPosition, out Ray ray))
            {
                foreach (RaycastHit hit in Physics.RaycastAll(
                             ray, 1000f, inputCamera.cullingMask))
                {
                    if (hit.collider == board.yellowButton)
                    {
                        PressYellow();
                        break;
                    }

                    if (hit.collider == board.gearHandleCollider)
                    {
                        dragging = true;
                        choiceStepOriginY = 0f;
                        dragOrigin = pointerPosition;
                        break;
                    }
                }
            }

            if (dragging && held)
                UpdateGear(pointerPosition - dragOrigin);

            if (up && dragging)
            {
                dragging = false;
                board.gear.localRotation = restRotation;
            }
        }

        private static void ReadPointer(
            out Vector2 position,
            out bool down,
            out bool held,
            out bool up)
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null)
            {
                position = Vector2.zero;
                down = held = up = false;
                return;
            }

            position = Mouse.current.position.ReadValue();
            down = Mouse.current.leftButton.wasPressedThisFrame;
            held = Mouse.current.leftButton.isPressed;
            up = Mouse.current.leftButton.wasReleasedThisFrame;
#else
            position = Input.mousePosition;
            down = Input.GetMouseButtonDown(0);
            held = Input.GetMouseButton(0);
            up = Input.GetMouseButtonUp(0);
#endif
        }

        private void UpdateGear(Vector2 dragDelta)
        {
            float pitch = Mathf.Clamp(dragDelta.y * 0.12f, -22f, 22f);
            if (choiceCount > 0)
            {
                board.gear.localRotation = restRotation * Quaternion.Euler(pitch, 0f, 0f);
                int steps = (int)((dragDelta.y - choiceStepOriginY) / choiceStepPixels);
                if (steps == 0) return;
                choiceStepOriginY += steps * choiceStepPixels;
                SelectGear(Mathf.Clamp(SelectedIndex - steps, 0, choiceCount - 1));
                return;
            }
            float roll = Mathf.Clamp(-dragDelta.x * 0.12f, -22f, 22f);
            board.gear.localRotation =
                restRotation * Quaternion.Euler(pitch, 0f, roll);

            if (dragDelta.sqrMagnitude <= 100f)
            {
                SelectGear(-1);
                return;
            }

            int index = dragDelta.x < 0f
                ? (dragDelta.y >= 0f ? 0 : 1)
                : (dragDelta.y >= 0f ? 2 : 3);
            SelectGear(index);
        }

        public bool TryCreateRay(Vector2 screenPosition, out Ray ray)
        {
            if (boardViewport == null)
            {
                ray = inputCamera.ScreenPointToRay(screenPosition);
                return true;
            }

            RectTransform rectTransform = boardViewport.rectTransform;
            Canvas canvas = boardViewport.canvas;
            Camera uiCamera =
                canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? canvas.worldCamera
                    : null;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rectTransform, screenPosition, uiCamera, out Vector2 localPoint) ||
                !rectTransform.rect.Contains(localPoint))
            {
                ray = default;
                return false;
            }

            Vector2 viewportPosition =
                (localPoint - rectTransform.rect.min) / rectTransform.rect.size;
            ray = inputCamera.ViewportPointToRay(viewportPosition);
            return true;
        }

        public void SelectGear(int index)
        {
            index = Mathf.Clamp(index, -1, choiceCount > 0 ? choiceCount - 1 : 3);
            if (index == SelectedIndex)
                return;

            SelectedIndex = index;
            onGearSelected.Invoke(index);
        }

        public void SetChoiceCount(int count, int selected = 0)
        {
            dragging = false;
            if (board != null && board.gear != null)
                board.gear.localRotation = restRotation;
            choiceCount = count;
            SelectGear(count > 0 ? Mathf.Clamp(selected, 0, count - 1) : -1);
        }

        public void PressYellow()
        {
            if (!InputEnabled || !isActiveAndEnabled || board == null)
                return;

            if (pressRoutine != null)
                StopCoroutine(pressRoutine);
            pressRoutine = StartCoroutine(AnimatePress());

            if (yellowInteractable)
                onYellowPressed.Invoke();
        }

        public void SetYellowInteractable(bool interactable)
        {
            yellowInteractable = interactable;
        }

        public void SetInputEnabled(bool enabled)
        {
            if (InputEnabled == enabled) return;
            InputEnabled = enabled;
            if (enabled) return;

            dragging = false;
            if (pressRoutine != null)
                StopCoroutine(pressRoutine);
            pressRoutine = null;

            if (board != null)
            {
                if (board.yellowButton != null)
                    board.yellowButton.transform.localPosition = restButtonPosition;
                if (board.gear != null)
                    board.gear.localRotation = restRotation;
            }

            SelectedIndex = -1;
        }

        public void ResetSelection()
        {
            dragging = false;
            if (board != null && board.gear != null)
                board.gear.localRotation = restRotation;

            SelectGear(-1);
        }

        private IEnumerator AnimatePress()
        {
            float elapsed = 0f;
            const float duration = 0.18f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float height = Mathf.Sin(
                    Mathf.Clamp01(elapsed / duration) * Mathf.PI) * 0.045f;
                board.yellowButton.transform.localPosition =
                    restButtonPosition + Vector3.down * height;
                yield return null;
            }

            board.yellowButton.transform.localPosition = restButtonPosition;
            pressRoutine = null;
        }

        private void OnDisable()
        {
            dragging = false;
            if (pressRoutine != null)
                StopCoroutine(pressRoutine);
            pressRoutine = null;

            if (board != null)
            {
                board.yellowButton.transform.localPosition = restButtonPosition;
                board.gear.localRotation = restRotation;
            }
        }
    }
}
