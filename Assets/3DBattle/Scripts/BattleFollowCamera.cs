using UnityEngine;
using UnityEngine.InputSystem;

namespace OneWay.Battle3D
{
    [DefaultExecutionOrder(-100)]
    public sealed class BattleFollowCamera : MonoBehaviour
    {
        public Transform target;
        public Vector3 offset = new Vector3(8f, 15f, -11f);
        public Vector3 lookOffset = new Vector3(0f, 0.5f, 0f);
        [Min(0.01f)] public float smoothTime = 0.16f;
        [Header("Mouse orbit")]
        [Min(0.01f)] public float mouseSensitivity = 0.15f;
        public bool invertY;
        [Range(-80f, 80f)] public float minPitch = -20f;
        [Range(0f, 85f)] public float maxPitch = 75f;
        [Min(0.1f)] public float groundClearance = 0.5f;
        public LayerMask groundMask = ~0;
        [Min(.01f)] public float collisionRadius = .2f;
        [Min(.01f)] public float collisionPadding = .05f;
        public float CurrentDistance => currentDistance;
        float currentDistance, distanceVelocity;
        [Tooltip("Colliders ignored by camera ground clearance, without changing character collisions.")]
        public Collider[] ignoredColliders = new Collider[0];
        public bool InputCaptured => Cursor.lockState == CursorLockMode.Locked && !capturedThisFrame;
        public float FollowDistance => distance;
        float yaw, pitch, distance;
        bool initialized, capturedThisFrame;
        bool returningFromIntro;

        public void ReturnToGameplayDistance(float value)
        {
            distance = Mathf.Max(1f, value);
            returningFromIntro = true;
            distanceVelocity = 0f;
        }

        void Start() { Snap(); Capture(); }
        void Capture() { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        void OnDisable() { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        void OnApplicationFocus(bool focus) { if (!focus) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; } }

        void Update()
        {
            capturedThisFrame = false;
            if (!Application.isFocused) return;
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                if (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
                {
                    Capture();
                    capturedThisFrame = true;
                }
                return;
            }
            Orbit(mouse.delta.ReadValue());
        }

        public void Orbit(Vector2 delta)
        {
            Initialize();
            yaw += delta.x * mouseSensitivity;
            pitch = Mathf.Clamp(pitch + delta.y * mouseSensitivity * (invertY ? 1f : -1f), minPitch, maxPitch);
            ApplyPose(0f);
        }

        void Initialize()
        {
            if (initialized) return;
            distance = Mathf.Max(1f, offset.magnitude);
            pitch = Mathf.Asin(Mathf.Clamp(offset.y / distance, -1f, 1f)) * Mathf.Rad2Deg;
            yaw = Mathf.Atan2(-offset.x, -offset.z) * Mathf.Rad2Deg;
            currentDistance = distance;
            initialized = true;
        }

        public void Snap()
        {
            if (target == null) return;
            if (!Application.isPlaying) initialized = false;
            Initialize();
            ApplyPose(0f);
        }

        void LateUpdate() => ApplyPose(Time.deltaTime);

        // Continue from the cinematic camera without snapping to the old orbit.
        public void AdoptCurrentPose()
        {
            if (target == null) return;
            var forward = transform.forward;
            distance = Mathf.Max(1f, Vector3.Dot(target.position - transform.position, forward));
            lookOffset = transform.position + forward * distance - target.position;
            yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            pitch = -Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            currentDistance = distance;
            distanceVelocity = 0f;
            initialized = true;
        }

        bool IgnoreCollision(Collider collider)
        {
            if (System.Array.IndexOf(ignoredColliders, collider) >= 0 ||
                collider.transform.IsChildOf(target) || collider.GetComponentInParent<BattleBoss>() != null)
                return true;
            var boundary = collider.GetComponentInParent<BattleArenaBoundary>();
            if (boundary != null)
                foreach (var wall in boundary.walls)
                    if (wall == collider) return true;
            return false;
        }

        void ApplyPose(float deltaTime)
        {
            if (target == null) return;
            Initialize();
            // Keep the mouse's requested orientation independent of obstacle avoidance.
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 direction = rotation * Vector3.back;
            Vector3 pivot = target.position + lookOffset;
            float allowedDistance = returningFromIntro ? Mathf.Max(distance, currentDistance) : distance;
            foreach (var hit in Physics.SphereCastAll(pivot, collisionRadius, direction,
                allowedDistance, groundMask, QueryTriggerInteraction.Ignore))
            {
                if (IgnoreCollision(hit.collider)) continue;
                allowedDistance = Mathf.Min(allowedDistance, Mathf.Max(0f, hit.distance - collisionPadding));
            }
            // Pull in immediately to prevent clipping; ease out only when space is clear.
            if (returningFromIntro)
            {
                if (deltaTime > 0f)
                    currentDistance = Mathf.SmoothDamp(currentDistance, distance, ref distanceVelocity, .35f, Mathf.Infinity, deltaTime);
                currentDistance = Mathf.Min(currentDistance, allowedDistance);
                if (Mathf.Abs(currentDistance - distance) < .01f) { currentDistance = distance; returningFromIntro = false; }
            }
            else if (allowedDistance < currentDistance)
            {
                currentDistance = allowedDistance;
                distanceVelocity = 0f;
            }
            else if (deltaTime > 0f)
                currentDistance = Mathf.SmoothDamp(currentDistance, allowedDistance, ref distanceVelocity,
                    smoothTime, Mathf.Infinity, deltaTime);
            currentDistance = Mathf.Clamp(currentDistance, 0f, allowedDistance);
            transform.SetPositionAndRotation(pivot + direction * currentDistance, rotation);
        }
    }
}
