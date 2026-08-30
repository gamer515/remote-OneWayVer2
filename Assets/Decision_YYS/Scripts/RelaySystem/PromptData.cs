using UnityEngine;

[CreateAssetMenu(fileName = "PromptData", menuName = "ScriptableObjects/PromptData", order = 1)]
public class PromptData : ScriptableObject
{
    [Header("Story Change Intensity")]
    [Tooltip("네 특성의 합계가 이 값 이상이면 Moderate입니다.")]
    [Min(0)] public int moderateChangeMinStat = 10;
    [Tooltip("네 특성의 합계가 이 값 이상이면 Strong입니다.")]
    [Min(0)] public int strongChangeMinStat = 20;

    [TextArea(2, 5)]
    public string subtleChangeInstruction =
        "핵심 사건은 유지하고 문장의 어조와 주변 묘사를 미세하게 조정합니다.";

    [TextArea(2, 5)]
    public string moderateChangeInstruction =
        "핵심 사건은 유지하되 문장과 주변 묘사에 주요·보조 약점이 드러나도록 조정합니다.";

    [TextArea(2, 5)]
    public string strongChangeInstruction =
        "이야기 순서와 목적지는 유지하면서, 선택 결과가 분명하게 느껴지도록 장면의 묘사와 긴장감을 강하게 조정합니다.";

    [Header("Prompt Templates")]
    [TextArea(5, 10)]
    [Tooltip("에피소드 종료 시 사용할 템플릿. {0}: 지문 요약, {1}: 분위기, {2}: 반환 형식 지침")]
    public string episodeEndTemplate = "에피소드가 종료되었습니다.\n\n[요청 사항]\n플레이 결과와 변경 강도에 맞춰 다음 회차에서 사용할 같은 에피소드의 표현을 수정해 주세요.\n전체적인 분위기는 '{1}'입니다.\n\n[전달된 지문 기록]\n{0}\n\n[제약 조건]\n{2}";

    [TextArea(5, 10)]
    [Tooltip("중간 전환 시 사용할 템플릿. {0}: 지문 요약, {1}: 분위기, {2}: 반환 형식 지침")]
    public string midTransitionTemplate = "중간에 이야기가 끊겼습니다.\n\n[요청 사항]\n전체적인 분위기를 '{1}'(으)로 수정해 주세요.\n\n[전달된 지문 기록]\n{0}\n\n[제약 조건]\n{2}";

    [TextArea(5, 10)]
    [Tooltip("챕터 종료 시 사용할 템플릿. {0}: 지문 요약, {1}: 분위기, {2}: 반환 형식 지침")]
    public string chapterEndTemplate = "챕터가 종료되었습니다.\n\n[요청 사항]\n전체적인 분위기를 '{1}'(으)로 수정해 주세요.\n\n[전달된 지문 기록]\n{0}\n\n[제약 조건]\n{2}";

    [Header("Atmosphere Settings")]
    [TextArea(2, 5)]
    [Tooltip("Subtle 변경 강도에서 사용할 분위기")]
    public string lowStatAtmosphere = "부드럽고 온화하며 감성적인 분위기";
    
    [TextArea(2, 5)]
    [Tooltip("Moderate 변경 강도에서 사용할 분위기")]
    public string midStatAtmosphere = "평온하면서도 무게감이 느껴지는 균형 잡힌 분위기";
    
    [TextArea(2, 5)]
    [Tooltip("Strong 변경 강도에서 사용할 분위기")]
    public string highStatAtmosphere = "강렬하고 긴박하며 압도적인 기운이 느껴지는 분위기";

    [Header("Response Guidelines")]
    [TextArea(5, 10)]
    [Tooltip("AI에게 전달할 반환 형식 및 괄호 처리 지침")]
    public string responseFormatTemplateV2 = "1. 모든 대사의 text는 새롭게 표현할 수 있습니다.\n2. 단, 원본 text의 { }와 그 내부 문자열은 핵심 설정이므로 순서와 내용을 한 글자도 변경하지 마세요.\n3. id, 이야기 순서, 선택지, destination, character, type은 변경하지 마세요.\n4. 응답은 반드시 아래 JSON 배열만 출력하세요.\n[ { \"id\": ID값, \"text\": \"{핵심 문자열}을 그대로 보존한 수정 문장\" } ]";
}
