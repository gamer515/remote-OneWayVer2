using UnityEngine;

/// <summary>Boss reveal presentation only; the battle prefab and controller remain unchanged.</summary>
public sealed class BossPreviewIdle : MonoBehaviour
{
    [SerializeField] private Animator animator;
    private static readonly int Idle = Animator.StringToHash("Base Layer.Axe_Breathe_and_Look_Around");
    [SerializeField, Min(0.5f)] private float encounterDistance = 7f;
    private Vector3 position, scale;
    private Quaternion rotation;
    internal Vector3 EncounterPosition => transform.position - Vector3.forward * encounterDistance;

    internal void FacePlayer(Vector3 playerPosition)
    {
        Vector3 direction = Vector3.ProjectOnPlane(playerPosition - transform.position, Vector3.up);
        // The preview rig faces local -Z.
        if (direction.sqrMagnitude > .001f) transform.rotation = Quaternion.LookRotation(-direction);
    }

    private void Awake()
    {
        if (animator == null) return;
        position = animator.transform.localPosition;
        rotation = animator.transform.localRotation;
        scale = animator.transform.localScale;
    }

    private void OnEnable()
    {
        if (animator == null) return;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Play(Idle, 0, 0f);
        animator.Update(0f);
        RestorePlacement();
    }

    private void LateUpdate()
    {
        if (animator == null) return;
        RestorePlacement();
    }

    private void RestorePlacement()
    {
        animator.transform.SetLocalPositionAndRotation(position, rotation);
        animator.transform.localScale = scale;
    }
}
