using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 베팅 보드의 3D 버튼 클릭을 Blue, Red, Yellow 명령으로 변환합니다.
/// 코인 생성이나 이야기 처리는 담당 컨트롤러가 각 이벤트를 받아 수행합니다.
/// </summary>
public sealed class BettingButtonController : MonoBehaviour
{
    [Header("3D Button Colliders")]
    [SerializeField] private Collider blueButton;
    [SerializeField] private Collider redButton;
    [SerializeField] private Collider yellowButton;
    [SerializeField] private Camera inputCamera;

    [Header("Button Press Motion")]
    [Tooltip("버튼이 로컬 Y축 아래로 눌리는 거리입니다.")]
    [SerializeField] private float pressDistance = 0.12f;
    [SerializeField] private float pressDuration = 0.06f;
    [SerializeField] private float releaseDuration = 0.1f;
    [SerializeField] private Ease pressEase = Ease.OutQuad;
    [SerializeField] private Ease releaseEase = Ease.OutBack;

    public event Action BluePressed;
    public event Action RedPressed;
    public event Action YellowPressed;

    private bool yellowInteractable = true;
    private bool bettingInteractable;
    private readonly Dictionary<Transform, Vector3> originalButtonPositions =
        new Dictionary<Transform, Vector3>();

    private void Awake()
    {
        if (inputCamera == null)
            inputCamera = Camera.main;

        ResolveButtonColliders();
        CacheOriginalButtonPositions();
    }

    private void Update()
    {
        if (!Input.GetMouseButtonDown(0) || inputCamera == null)
            return;

        Collider clickedButton = FindClickedButton();
        if (clickedButton == null)
            return;

        // 기능이 잠긴 버튼도 실제로 눌렀다는 시각적 반응은 항상 보여줍니다.
        PlayPressMotion(clickedButton);

        if (clickedButton == blueButton && bettingInteractable)
        {
            BluePressed?.Invoke();
        }
        else if (clickedButton == redButton && bettingInteractable)
        {
            RedPressed?.Invoke();
        }
        else if (clickedButton == yellowButton && yellowInteractable)
        {
            YellowPressed?.Invoke();
        }
    }

    private void OnDisable()
    {
        // 재활성화될 때 버튼이 눌린 위치에 남지 않도록 Tween과 위치를 함께 복원합니다.
        foreach (KeyValuePair<Transform, Vector3> button in originalButtonPositions)
        {
            if (button.Key == null)
                continue;

            button.Key.DOKill();
            button.Key.localPosition = button.Value;
        }
    }

    public void SetYellowInteractable(bool interactable)
    {
        yellowInteractable = interactable;
    }

    public void SetBettingInteractable(bool interactable)
    {
        bettingInteractable = interactable;
    }

    private void PlayPressMotion(Collider buttonCollider)
    {
        if (buttonCollider == null)
            return;

        Transform button = buttonCollider.transform;
        if (!originalButtonPositions.TryGetValue(button, out Vector3 originalPosition))
        {
            originalPosition = button.localPosition;
            originalButtonPositions[button] = originalPosition;
        }

        // 연속 클릭에도 기준 위치가 누적되지 않도록 기존 Tween을 끊고 원위치에서 재생합니다.
        button.DOKill();
        button.localPosition = originalPosition;

        DOTween.Sequence()
            .Append(button.DOLocalMoveY(
                originalPosition.y - pressDistance,
                pressDuration).SetEase(pressEase))
            .Append(button.DOLocalMoveY(
                originalPosition.y,
                releaseDuration).SetEase(releaseEase))
            .SetLink(button.gameObject, LinkBehaviour.KillOnDestroy);
    }

    private void CacheOriginalButtonPositions()
    {
        CacheButtonPosition(blueButton);
        CacheButtonPosition(redButton);
        CacheButtonPosition(yellowButton);
    }

    private void CacheButtonPosition(Collider buttonCollider)
    {
        if (buttonCollider != null)
            originalButtonPositions[buttonCollider.transform] = buttonCollider.transform.localPosition;
    }

    private Collider FindClickedButton()
    {
        Ray ray = inputCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity);

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i].collider;
            if (hit == blueButton || hit == redButton || hit == yellowButton)
                return hit;
        }

        return null;
    }

    private void ResolveButtonColliders()
    {
        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider candidate = colliders[i];
            switch (candidate.gameObject.name)
            {
                case "Button_Blue":
                    if (blueButton == null) blueButton = candidate;
                    break;

                case "Button_Red":
                    if (redButton == null) redButton = candidate;
                    break;

                case "Button_Yellow":
                    if (yellowButton == null) yellowButton = candidate;
                    break;
            }
        }

        if (blueButton == null || redButton == null || yellowButton == null)
            Debug.LogError("베팅 보드의 Blue, Red, Yellow 버튼 Collider를 모두 찾지 못했습니다.", this);
    }
}
