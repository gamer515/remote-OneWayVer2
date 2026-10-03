using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations.Rigging;

/// <summary>
/// 여러 손가락 IK Target을 Grip 값 하나로 열린 자세와 닫힌 자세 사이에서 움직입니다.
/// 게임 코드에서는 SetGrip(0f~1f)만 호출하면 됩니다.
/// </summary>
[ExecuteAlways, DisallowMultipleComponent]
public sealed class FingerGripController : MonoBehaviour
{
    [Serializable]
    private sealed class FingerPose
    {
        public string name;
        public ChainIKConstraint constraint;
        public Transform target;
        public Vector3 openLocalPosition;
        public Quaternion openLocalRotation = Quaternion.identity;
        public Vector3 closedLocalPosition;
        public Quaternion closedLocalRotation = Quaternion.identity;
    }

    [Header("Grip")]
    [SerializeField, Range(0f, 1f)] private float grip;
    [SerializeField, Min(0f)] private float transitionSpeed = 4f;
    [SerializeField] private bool smoothAtRuntime = true;
    [SerializeField] private bool previewInEditMode = true;
    [SerializeField] private bool enableFingerConstraints = true;

    [Header("Default Closed Pose Generation")]
    [SerializeField, Range(0.2f, 0.9f)] private float closedReach = 0.62f;
    [SerializeField, Range(-0.5f, 0.5f)] private float bendAmount = 0.18f;
    [SerializeField] private Vector3 localBendDirection = Vector3.up;

    [Header("Finger Targets")]
    [SerializeField] private List<FingerPose> fingers = new List<FingerPose>();
    [SerializeField, HideInInspector] private bool hasOpenPose;
    [SerializeField, HideInInspector] private bool hasClosedPose;

    private float desiredGrip;
    private float lastEditorGrip = float.NaN;
    private RigBuilder rigBuilder;

    public float Grip => grip;
    public int FingerCount => fingers.Count(item => item != null && item.target != null);

    private void Awake()
    {
        EnsureTargets();
        rigBuilder = GetComponent<RigBuilder>();
        desiredGrip = grip;
        SetConstraintsEnabled(enableFingerConstraints);
        ApplyGrip(grip);
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            UpdateEditModePreview();
            return;
        }

        float next = smoothAtRuntime
            ? Mathf.MoveTowards(grip, desiredGrip, transitionSpeed * Time.deltaTime)
            : desiredGrip;

        if (Mathf.Approximately(next, grip)) return;
        grip = next;
        ApplyGrip(grip);
    }

    /// <summary>0은 열린 손, 1은 닫힌 손입니다. 기본 설정에서는 부드럽게 이동합니다.</summary>
    public void SetGrip(float value)
    {
        desiredGrip = Mathf.Clamp01(value);
        if (!Application.isPlaying || !smoothAtRuntime)
        {
            grip = desiredGrip;
            ApplyGrip(grip);
            RequestEditModePreview();
        }
    }

    /// <summary>보간 시간을 기다리지 않고 즉시 지정한 자세를 적용합니다.</summary>
    public void SetGripImmediate(float value)
    {
        grip = desiredGrip = Mathf.Clamp01(value);
        ApplyGrip(grip);
        RequestEditModePreview();
    }

    public void OpenHand() => SetGrip(0f);
    public void CloseHand() => SetGrip(1f);

    [ContextMenu("Finger Grip/Auto Find Targets")]
    public void AutoFindTargets()
    {
        ChainIKConstraint[] constraints = GetComponentsInChildren<ChainIKConstraint>(true);
        string[] order = { "ThumbIK", "IndexIK", "MiddleIK", "PinkyIK" };
        var rebuilt = new List<FingerPose>();

        foreach (string constraintName in order)
        {
            ChainIKConstraint constraint = constraints.FirstOrDefault(item => item.name == constraintName);
            if (constraint == null || constraint.data.target == null) continue;

            FingerPose existing = fingers.FirstOrDefault(item => item != null && item.constraint == constraint);
            rebuilt.Add(existing ?? new FingerPose
            {
                name = constraintName.Replace("IK", string.Empty),
                constraint = constraint,
                target = constraint.data.target
            });
        }

        fingers = rebuilt;
        MarkDirty();
    }

    [ContextMenu("Finger Grip/Capture Current As Open Pose")]
    public void CaptureOpenPose()
    {
        EnsureTargets();
        foreach (FingerPose finger in fingers)
        {
            if (finger == null || finger.target == null) continue;
            finger.openLocalPosition = finger.target.localPosition;
            finger.openLocalRotation = finger.target.localRotation;
        }
        hasOpenPose = true;
        MarkDirty();
    }

    [ContextMenu("Finger Grip/Capture Current As Closed Pose")]
    public void CaptureClosedPose()
    {
        EnsureTargets();
        foreach (FingerPose finger in fingers)
        {
            if (finger == null || finger.target == null) continue;
            finger.closedLocalPosition = finger.target.localPosition;
            finger.closedLocalRotation = finger.target.localRotation;
        }
        hasClosedPose = true;
        MarkDirty();
    }

    [ContextMenu("Finger Grip/Generate Default Closed Pose")]
    public void GenerateDefaultClosedPose()
    {
        EnsureTargets();
        if (!hasOpenPose) CaptureOpenPose();

        Vector3 bendDirection = transform.TransformDirection(localBendDirection.normalized);
        foreach (FingerPose finger in fingers)
        {
            if (finger == null || finger.target == null || finger.constraint == null) continue;
            Transform root = finger.constraint.data.root;
            Transform tip = finger.constraint.data.tip;
            if (root == null || tip == null || finger.target.parent == null) continue;

            float chainLength = CalculateChainLength(root, tip);
            Vector3 fingerDirection = (tip.position - root.position).normalized;
            Vector3 perpendicularBend = Vector3.ProjectOnPlane(bendDirection, fingerDirection).normalized;
            if (perpendicularBend.sqrMagnitude < 0.001f)
                perpendicularBend = Vector3.ProjectOnPlane(transform.right, fingerDirection).normalized;

            Vector3 closedWorldPosition = Vector3.Lerp(root.position, tip.position, closedReach) +
                                          perpendicularBend * (chainLength * bendAmount);
            finger.closedLocalPosition = finger.target.parent.InverseTransformPoint(closedWorldPosition);
            finger.closedLocalRotation = finger.openLocalRotation;
        }

        hasClosedPose = true;
        ApplyGrip(grip);
        MarkDirty();
    }

    private void EnsureTargets()
    {
        if (fingers.Count == 0 || fingers.Any(item => item == null || item.target == null))
            AutoFindTargets();
    }

    private void ApplyGrip(float value)
    {
        if (!hasOpenPose || !hasClosedPose) return;
        float normalized = Mathf.Clamp01(value);

        foreach (FingerPose finger in fingers)
        {
            if (finger == null || finger.target == null) continue;
            finger.target.localPosition = Vector3.LerpUnclamped(
                finger.openLocalPosition, finger.closedLocalPosition, normalized);
            finger.target.localRotation = Quaternion.SlerpUnclamped(
                finger.openLocalRotation, finger.closedLocalRotation, normalized);
        }
    }

    private void SetConstraintsEnabled(bool enabled)
    {
        foreach (FingerPose finger in fingers)
        {
            if (finger?.constraint != null) finger.constraint.enabled = enabled;
        }
    }

    private static float CalculateChainLength(Transform root, Transform tip)
    {
        float length = 0f;
        Transform current = tip;
        while (current != null && current != root)
        {
            if (current.parent == null) return 0f;
            length += Vector3.Distance(current.position, current.parent.position);
            current = current.parent;
        }
        return current == root ? length : 0f;
    }

    private void OnValidate()
    {
        grip = Mathf.Clamp01(grip);
        desiredGrip = grip;
        if (!Application.isPlaying && previewInEditMode)
        {
            ApplyGrip(grip);
            RequestEditModePreview();
        }
    }

    private void UpdateEditModePreview()
    {
        if (!previewInEditMode || Mathf.Approximately(lastEditorGrip, grip)) return;
        lastEditorGrip = grip;
        ApplyGrip(grip);
        RequestEditModePreview();
    }

    [ContextMenu("Finger Grip/Refresh Edit Mode Preview")]
    public void RefreshEditModePreview()
    {
#if UNITY_EDITOR
        if (Application.isPlaying || !previewInEditMode || !isActiveAndEnabled) return;

        ApplyGrip(grip);
        lastEditorGrip = grip;

        // Animation/Timeline Preview에서는 RigBuilder가 자신의 Preview Graph를 사용합니다.
        if (UnityEditor.AnimationMode.InAnimationMode())
        {
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            return;
        }

        if (rigBuilder == null) rigBuilder = GetComponent<RigBuilder>();
        if (rigBuilder != null && rigBuilder.enabled)
        {
            bool ready = rigBuilder.graph.IsValid() || rigBuilder.Build();
            if (ready)
            {
                rigBuilder.Evaluate(0f);
                rigBuilder.Clear();
            }
        }

        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
#endif
    }

    private void RequestEditModePreview()
    {
#if UNITY_EDITOR
        if (Application.isPlaying) return;
        UnityEditor.EditorApplication.delayCall -= RefreshEditModePreview;
        UnityEditor.EditorApplication.delayCall += RefreshEditModePreview;
#endif
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall -= RefreshEditModePreview;
#endif
    }

    private void MarkDirty()
    {
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        if (gameObject.scene.IsValid())
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
    }
}
