using UnityEngine;
using System.IO;

public class BiographyController : CancelPanelController
{
    [Header("패널 설정")]
    public GameObject roundListPanel;
    public GameObject detailLogPanel;

    [Header("프리팹")]
    public GameObject roundButtonPrefab;
    public GameObject logEntryPrefab;

    [Header("부모 컨텐츠")]
    public Transform roundContent;
    public Transform logContent;

    // [추가된 부분] MainMenuController에서 이 함수를 호출합니다.
    public void OpenBiography()
    {
        // 1. 패널 초기화: 목록은 켜고, 상세창은 끕니다.
        roundListPanel.SetActive(true);
        detailLogPanel.SetActive(false);

        // 2. 데이터 로드 시작
        LoadRoundList();
    }

    // 회차 목록 표시
    public void LoadRoundList()
    {
        // 기존에 생성된 버튼이 있다면 삭제 (중복 방지)
        foreach (Transform child in roundContent) Destroy(child.gameObject);

        string path = Path.Combine(Application.dataPath, "MainMenu","MainMenuStreamingAssets", "PlayHistory.json");
        //string path = System.IO.Path.Combine(Application.persistentDataPath, "PlayHistory.json");

        // 파일 존재 여부 확인 (방어적 코드)
        if (!File.Exists(path))
        {
            Debug.LogError($"JSON 파일을 찾을 수 없습니다: {path}");
            return;
        }

        string json = File.ReadAllText(path);
        var history = JsonUtility.FromJson<AllPlayHistory>(json);

        if (history == null || history.allRounds == null) return;

        foreach (var round in history.allRounds)
        {
            // 프리팹 생성 후 컨트롤러 가져오기
            var btnObj = Instantiate(roundButtonPrefab, roundContent);
            var btn = btnObj.GetComponent<RoundButtonController>();

            if (btn != null)
            {
                btn.Setup(round, this);
            }
        }
    }

    public void ShowDetail(RoundData data)
    {
        roundListPanel.SetActive(false);
        detailLogPanel.SetActive(true);

        foreach (Transform child in logContent) Destroy(child.gameObject);

        foreach (var log in data.logs)
        {
            var entryObj = Instantiate(logEntryPrefab, logContent);
            var entry = entryObj.GetComponent<LogEntryController>();

            if (entry != null)
            {
                entry.SetLogData(log);
            }
        }
    }
}