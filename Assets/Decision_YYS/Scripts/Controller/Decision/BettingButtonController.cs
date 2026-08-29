using System;
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

    public event Action BluePressed;
    public event Action RedPressed;
    public event Action YellowPressed;

    private bool yellowInteractable = true;
    private bool bettingInteractable;

    private void Awake()
    {
        if (inputCamera == null)
            inputCamera = Camera.main;

        ResolveButtonColliders();
    }

    private void Update()
    {
        if (!Input.GetMouseButtonDown(0) || inputCamera == null)
            return;

        Collider clickedButton = FindClickedButton();
        if (clickedButton == blueButton && bettingInteractable)
        {
            // 다음 단계에서 선택된 코인 한 개를 랜덤 출구로 생성합니다.
            BluePressed?.Invoke();
        }
        else if (clickedButton == redButton && bettingInteractable)
        {
            // 베팅 결과 계산은 코인 이동 연출이 준비된 뒤 연결합니다.
            RedPressed?.Invoke();
        }
        else if (clickedButton == yellowButton && yellowInteractable)
        {
            YellowPressed?.Invoke();
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
