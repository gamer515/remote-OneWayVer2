using UnityEngine;

/// <summary>Boss reveal presentation only; the battle prefab and controller remain unchanged.</summary>
public sealed class BossPreviewIdle : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private Transform leftFoot, rightFoot;
    [SerializeField] private string idleState = "Axe_Breathe_and_Look_Around";
    [SerializeField] private string roarState = "Sword_Shout";
    [SerializeField, Min(0.1f)] private float idleSeconds = 11.3f;
    [SerializeField, Min(0.1f)] private float roarSeconds = 6.2f;
    private float elapsed;
    private Vector3 position, scale;
    private Quaternion rotation;
    private float feetHeight;
    public bool IsRoaring { get; private set; }

    private void OnEnable()
    {
        if (animator == null) return;
        position = animator.transform.localPosition; rotation = animator.transform.localRotation; scale = animator.transform.localScale;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        elapsed = 0; IsRoaring = false;
        animator.Play(idleState, 0, 0);
        animator.Update(0);
        animator.transform.SetLocalPositionAndRotation(position, rotation); animator.transform.localScale = scale;
        feetHeight = LowestFoot();
    }
    private void Update()
    {
        if (animator == null) return;
        elapsed += Time.deltaTime;
        if (elapsed < (IsRoaring ? roarSeconds : idleSeconds)) return;
        elapsed = 0; IsRoaring = !IsRoaring;
        animator.CrossFadeInFixedTime(IsRoaring ? roarState : idleState, 0.2f, 0);
    }
    private void LateUpdate()
    {
        if (animator == null) return;
        animator.transform.SetLocalPositionAndRotation(position, rotation);
        animator.transform.localScale = scale;
        if (leftFoot != null && rightFoot != null)
            animator.transform.position += Vector3.up * (feetHeight - LowestFoot());
    }
    private float LowestFoot() => leftFoot != null && rightFoot != null ? Mathf.Min(leftFoot.position.y, rightFoot.position.y) : transform.position.y;
}
