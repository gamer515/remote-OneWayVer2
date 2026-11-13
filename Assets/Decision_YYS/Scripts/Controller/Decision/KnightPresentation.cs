using System.Collections;
using UnityEngine;

/// <summary>Shared seated preview and duel entrance animation for the rigged Knight_For_Ani.</summary>
[RequireComponent(typeof(Animator))]
public sealed class KnightPresentation : MonoBehaviour
{
    public enum Phase { Seated, Dismount, Walking, Warrior }
    [SerializeField] private Vector2 seatedInterval = new Vector2(6f, 8f);
    [SerializeField, Min(0.05f)] private float blendSeconds = 0.3f;
    [SerializeField, Min(0.1f)] private float walkSpeed = 1.35f;
    [SerializeField, Min(0.1f)] private float turnSpeed = 240f;
    [SerializeField] private AnimationClip deskDismount;
    [Tooltip("Avatar 크기와 앉은 기준점 차이. KnightAnimationSetup.Build에서 실제 자세를 샘플링해 계산합니다.")]
    [SerializeField] private Vector3 dismountStartPoseOffset;
    [SerializeField] private Vector3 dismountEndPoseOffset;
    private Animator animator;
    private float nextGestureAt;
    private bool gesturePlaying;
    private float dismountProgress;
    private float poseBlendIn;
    private float poseBlendOut;
    private bool reading;
    public void SetReading(bool value)
    {
        if (reading == value) return;
        reading = value;
        if (CurrentPhase == Phase.Seated && animator != null && animator.isActiveAndEnabled)
        {
            gesturePlaying = false;
            animator.CrossFadeInFixedTime("Sitting Idle", blendSeconds, 0, 0f);
            ScheduleGesture();
        }
    }
    public Phase CurrentPhase { get; private set; }
    public int GestureCount { get; private set; }
    public string LastGesture { get; private set; }

    private void Awake() => animator = GetComponent<Animator>();
    private void OnEnable() => ResetSeated();

    public void ResetSeated()
    {
        if (animator == null) animator = GetComponent<Animator>();
        CurrentPhase = Phase.Seated;
        gesturePlaying = false;
        poseBlendOut = poseBlendIn = 0f;
        animator.enabled = true;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Play("Sitting Idle", 0, 0f);
        animator.Update(0f);
        ScheduleGesture();
    }

    public void MatchSeatedPose(Animator source)
    {
        ResetSeated();
        if (source == null || !source.enabled) return;
        var state = source.GetCurrentAnimatorStateInfo(0);
        if (!animator.HasState(0, state.fullPathHash)) return;
        animator.Play(state.fullPathHash, 0, state.normalizedTime);
        animator.Update(0f);
    }

    private void ScheduleGesture() => nextGestureAt = Time.time + Random.Range(
        Mathf.Max(0.1f, seatedInterval.x), Mathf.Max(seatedInterval.x, seatedInterval.y));

    private void Update()
    {
        if (reading || CurrentPhase != Phase.Seated || animator == null || !animator.enabled) return;
        var state = animator.GetCurrentAnimatorStateInfo(0);
        if (gesturePlaying)
        {
            if (!animator.IsInTransition(0) && state.IsName("Sitting Idle"))
            {
                gesturePlaying = false;
                ScheduleGesture();
            }
            return;
        }
        if (Time.time < nextGestureAt || animator.IsInTransition(0)) return;
        LastGesture = Random.value < 0.5f ? "Sitting And Pointing" : "Sitting Rubbing Arm";
        GestureCount++;
        animator.CrossFadeInFixedTime(LastGesture, blendSeconds, 0, 0f);
        gesturePlaying = true;
    }

    private void LateUpdate()
    {
        if (reading && CurrentPhase == Phase.Seated && animator != null && animator.isHuman)
        {
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head != null) head.rotation = Quaternion.AngleAxis(19f, transform.right) * head.rotation;
        }
        // Keep authored joint motion while aligning the different clips' pelvis reference points.
        // Fade the correction with Animator's blend so it cannot introduce a transition pop.
        Vector3 correction = Vector3.zero;
        if (CurrentPhase == Phase.Dismount)
            correction = Vector3.Lerp(dismountStartPoseOffset, dismountEndPoseOffset, dismountProgress) * poseBlendIn;
        else if (poseBlendOut > 0f)
        {
            correction = dismountEndPoseOffset * Mathf.Clamp01(poseBlendOut / blendSeconds);
            poseBlendOut = Mathf.Max(0f, poseBlendOut - Time.deltaTime);
        }
        if (correction.sqrMagnitude > 0f && animator != null && animator.isHuman)
        {
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips != null) hips.position += transform.TransformVector(correction);
        }
    }

    public IEnumerator EnterDuel(Vector3 destination, Quaternion finalRotation)
    {
        yield return Dismount(destination.y);
        yield return WalkTo(destination);
        yield return TurnTo(finalRotation);
        CurrentPhase = Phase.Warrior;
        animator.CrossFadeInFixedTime("Warrior Idle", blendSeconds, 0, 0f);
        yield return new WaitForSeconds(blendSeconds);
    }

    public IEnumerator FetchBook(Vector3 destination, System.Action collected)
    {
        Vector3 seat = transform.position;
        Quaternion seatedRotation = transform.rotation;
        yield return Dismount(destination.y);
        yield return WalkTo(destination);
        collected();
        yield return WalkTo(new Vector3(seat.x, destination.y, seat.z));
        yield return TurnTo(seatedRotation);
        transform.position = seat;
        ResetSeated();
    }

    private IEnumerator Dismount(float floor)
    {
        CurrentPhase = Phase.Dismount;
        gesturePlaying = false;
        animator.enabled = true;
        animator.applyRootMotion = false;
        animator.CrossFadeInFixedTime("Desk Dismount", blendSeconds, 0, 0f);
        float duration = deskDismount != null ? deskDismount.length : 1.64f;
        // Imported forward root motion is extracted and disabled to avoid moving twice.
        Vector3 start = transform.position;
        Vector3 landing = start + transform.forward * (0.45f * transform.lossyScale.y);
        landing.y = floor;
        for (float t = 0; t < duration; t += Time.deltaTime)
        {
            dismountProgress = Mathf.Clamp01(t / duration);
            poseBlendIn = Mathf.Clamp01(t / blendSeconds);
            float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
            transform.position = Vector3.Lerp(start, landing, progress);
            yield return null;
        }
        transform.position = landing;
        poseBlendOut = blendSeconds;
    }

    private IEnumerator WalkTo(Vector3 destination)
    {
        CurrentPhase = Phase.Walking;
        animator.CrossFadeInFixedTime("Walking", blendSeconds, 0, 0f);
        while ((destination - transform.position).sqrMagnitude > 0.0001f)
        {
            Vector3 heading = destination - transform.position;
            heading.y = 0f;
            if (heading.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(heading), turnSpeed * Time.deltaTime);
            // Turn before translating to keep the feet from sliding sideways.
            if (heading.sqrMagnitude < 0.0001f || Quaternion.Angle(transform.rotation, Quaternion.LookRotation(heading)) < 15f)
                transform.position = Vector3.MoveTowards(transform.position, destination, walkSpeed * Time.deltaTime);
            yield return null;
        }
        transform.position = destination;
    }

    private IEnumerator TurnTo(Quaternion finalRotation)
    {
        while (Quaternion.Angle(transform.rotation, finalRotation) > 0.1f)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, finalRotation, turnSpeed * Time.deltaTime);
            yield return null;
        }
        transform.rotation = finalRotation;
    }
}
