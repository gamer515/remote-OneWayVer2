using UnityEngine;
using System.Collections;
using TMPro; // TextMeshPro를 사용하기 위한 네임스페이스

[RequireComponent(typeof(TextMeshProUGUI))]
public class TypewriterEffect : MonoBehaviour
{
    [Header("UI Reference")]
    private TextMeshProUGUI textComponent;

    [Header("Typing Settings")]
    public float typingSpeed = 0.05f; // 글자가 나오는 속도 (낮을수록 빠름)

    [Header("Audio Settings (Optional)")]
    public AudioSource audioSource;
    public AudioClip typingSound; // 샌즈의 "다다다" 목소리 파일
    [Range(0f, 1f)] public float volume = 0.5f;

    private Coroutine typingCoroutine;
    private string fullText;
    private bool isTyping = false;

    void Awake()
    {
        textComponent = GetComponent<TextMeshProUGUI>();
        textComponent.text = ""; // 시작할 때 텍스트 비우기
    }

    void Update()
    {
        // 텍스트가 타이핑 중일 때 스페이스바(또는 Z키)를 누르면 스킵
        if (isTyping && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Z)))
        {
            SkipTyping();
        }
    }

    // 외부에서 텍스트를 전달하며 타이핑을 시작하는 함수
    public void PlayText(string textToType)
    {
        fullText = textToType;

        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
        }

        typingCoroutine = StartCoroutine(TypeText());
    }

    private IEnumerator TypeText()
    {
        isTyping = true;
        textComponent.text = "";

        // 한 글자씩 출력
        foreach (char c in fullText.ToCharArray())
        {
            textComponent.text += c;

            // 공백이 아닐 때만 소리 재생 (언더테일 디테일)
            if (c != ' ' && audioSource != null && typingSound != null)
            {
                audioSource.PlayOneShot(typingSound, volume);
            }

            yield return new WaitForSeconds(typingSpeed);
        }

        isTyping = false;
    }

    // 타이핑 효과를 멈추고 전체 텍스트를 한 번에 띄움
    private void SkipTyping()
    {
        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
        }

        textComponent.text = fullText;
        isTyping = false;
    }

    // 타이핑이 완전히 끝났는지 확인하는 프로퍼티 (다른 매니저에서 체크용)
    public bool IsTyping => isTyping;

    public void ClearText()
    {
        if (textComponent != null)
        {
            textComponent.text = "";
        }
    }

    public void StopAndClear()
    {
        StopAllCoroutines();     // 돌아가던 타이핑(코루틴) 즉시 정지!

        // IsTyping = false; (대문자 에러)
        isTyping = false;        // 소문자로 수정

        // if (textMeshPro != null) (없는 변수 에러)
        if (textComponent != null) // textComponent로 수정
        {
            textComponent.text = ""; // 남아있는 글자도 깔끔하게 지우기
        }
    }
}