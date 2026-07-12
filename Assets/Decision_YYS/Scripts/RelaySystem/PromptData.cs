using UnityEngine;

[CreateAssetMenu(fileName = "PromptData", menuName = "ScriptableObjects/PromptData", order = 1)]
public class PromptData : ScriptableObject
{
    [Header("Prompt Templates")]
    [TextArea(5, 10)]
    [Tooltip("중간 전환 시 사용할 템플릿. {0}: 지문 요약, {1}: 분위기, {2}: 반환 형식 지침")]
    public string midTransitionTemplate = "중간에 이야기가 끊겼습니다.\n\n[요청 사항]\n전체적인 분위기를 '{1}'(으)로 수정해 주세요.\n\n[전달된 지문 기록]\n{0}\n\n[제약 조건]\n{2}";

    [TextArea(5, 10)]
    [Tooltip("챕터 종료 시 사용할 템플릿. {0}: 지문 요약, {1}: 분위기, {2}: 반환 형식 지침")]
    public string chapterEndTemplate = "챕터가 종료되었습니다.\n\n[요청 사항]\n전체적인 분위기를 '{1}'(으)로 수정해 주세요.\n\n[전달된 지문 기록]\n{0}\n\n[제약 조건]\n{2}";

    [Header("Atmosphere Settings")]
    [TextArea(2, 5)]
    [Tooltip("스탯 0~4 구간")]
    public string lowStatAtmosphere = "부드럽고 온화하며 감성적인 분위기";
    
    [TextArea(2, 5)]
    [Tooltip("스탯 5 구간")]
    public string midStatAtmosphere = "평온하면서도 무게감이 느껴지는 균형 잡힌 분위기";
    
    [TextArea(2, 5)]
    [Tooltip("스탯 6~10 구간")]
    public string highStatAtmosphere = "강렬하고 긴박하며 압도적인 기운이 느껴지는 분위기";

    [Header("Response Guidelines")]
    [TextArea(5, 10)]
    [Tooltip("AI에게 전달할 반환 형식 및 괄호 처리 지침")]
    public string responseFormatTemplate = "1. 전달된 텍스트 중 { }로 감싸진 부분은 분위기에 맞춰 변경해야 할 핵심 위치입니다.\n2. { } 안의 내용을 지정된 분위기에 어울리는 단어나 문구로 수정하되, 수정 후에도 반드시 { }로 감싼 형태를 유지해 주세요.\n3. { } 이외의 나머지 문장은 원본의 흐름을 최대한 유지해 주세요.\n4. 응답은 반드시 아래 JSON 배열 형식만을 출력해 주세요. 다른 설명은 필요 없습니다.\n[ { \"id\": ID값, \"text\": \"수정된 {내용}이 포함된 전체 문장\" } ]";
}
