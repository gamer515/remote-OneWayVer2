using UnityEngine;

// The dedicated controller plays Idle -> preparation -> Roar -> recovery.
// This component only preserves the preview placement and keeps its feet grounded.
[DisallowMultipleComponent]
public sealed class DecisionBoss1PreviewSequence : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private Transform leftFoot;
    [SerializeField] private Transform rightFoot;
    [SerializeField, Min(0.05f)] private float playbackSpeed = 1f;

    private Vector3 position;
    private Vector3 scale;
    private Quaternion rotation;
    private float feetHeight;

    private void OnEnable()
    {
        if (animator == null) return;
        position = animator.transform.localPosition;
        rotation = animator.transform.localRotation;
        scale = animator.transform.localScale;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.speed = Mathf.Max(0.05f, playbackSpeed);
        animator.Play("Axe_Breathe_and_Look_Around", 0, 0f);
        animator.Update(0f);
        RestorePlacement();
        feetHeight = LowestFoot();
    }

    private void Update()
    {
        if (animator != null) animator.speed = Mathf.Max(0.05f, playbackSpeed);
    }

    private void LateUpdate()
    {
        if (animator == null) return;
        RestorePlacement();
        if (leftFoot != null && rightFoot != null)
            animator.transform.position += Vector3.up * (feetHeight - LowestFoot());
    }

    private void RestorePlacement()
    {
        animator.transform.SetLocalPositionAndRotation(position, rotation);
        animator.transform.localScale = scale;
    }

    private float LowestFoot() => leftFoot != null && rightFoot != null
        ? Mathf.Min(leftFoot.position.y, rightFoot.position.y)
        : transform.position.y;
}
