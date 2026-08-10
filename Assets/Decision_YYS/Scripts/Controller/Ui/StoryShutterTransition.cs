using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// CameraShutter Animator의 닫힘과 열림 재생 순서를 관리합니다.
/// Unity 생명주기가 필요하지 않아 일반 C# 객체로 사용합니다.
/// </summary>
public sealed class StoryShutterTransition
{
    private const string CloseStateName = "Base Layer.CameraShutter_Close";
    private const string OpenStateName = "Base Layer.CameraShutter_Open";

    private static readonly int CloseStateHash = Animator.StringToHash(CloseStateName);
    private static readonly int OpenStateHash = Animator.StringToHash(OpenStateName);

    private readonly Animator shutterAnimator;
    private readonly GameObject shutterObject;
    private bool isPlaying;

    public StoryShutterTransition(Animator shutterAnimator)
    {
        this.shutterAnimator = shutterAnimator;
        shutterObject = shutterAnimator != null ? shutterAnimator.gameObject : null;

        if (shutterAnimator != null)
            shutterAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;

        if (shutterObject != null)
            shutterObject.SetActive(false);
    }

    public IEnumerator Play(Action onClosed, Action onCompleted)
    {
        if (isPlaying || shutterAnimator == null || shutterAnimator.runtimeAnimatorController == null)
        {
            onClosed?.Invoke();
            onCompleted?.Invoke();
            yield break;
        }

        isPlaying = true;
        shutterObject.SetActive(true);

        shutterAnimator.Play(CloseStateHash, 0, 0f);
        shutterAnimator.Update(0f);
        yield return WaitForStateCompleted(CloseStateHash);

        // 셔터가 완전히 닫혀 화면이 가려진 뒤에만 다음 이야기 내용을 적용합니다.
        onClosed?.Invoke();

        shutterAnimator.Play(OpenStateHash, 0, 0f);
        shutterAnimator.Update(0f);
        yield return WaitForStateCompleted(OpenStateHash);

        shutterObject.SetActive(false);
        isPlaying = false;
        onCompleted?.Invoke();
    }

    private IEnumerator WaitForStateCompleted(int stateHash)    
    {
        while (true)
        {
            AnimatorStateInfo stateInfo = shutterAnimator.GetCurrentAnimatorStateInfo(0);
            bool isRequestedState = stateInfo.fullPathHash == stateHash;
            bool isCompleted = stateInfo.normalizedTime >= 1f;

            if (isRequestedState && isCompleted && !shutterAnimator.IsInTransition(0))
                yield break;

            yield return null;
        }
    }
}
