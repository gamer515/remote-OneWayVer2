using UnityEngine;
using System.Collections.Generic;
using System.Text;

public enum StoryRelayTrigger
{
    EpisodeEnd,
    MidTransition,
    ChapterEnd
}

public class StoryRelayManager : MonoBehaviour
{
    [SerializeField] private PromptData promptData;

    /// <summary>
    /// 이야기 데이터를 필터링하고 요약하여 외부로 전송합니다.
    /// </summary>
    public void Relay(
        StoryRelayTrigger trigger,
        string currentFileName,
        List<Dialogue> history,
        List<BettingDecisionRecord> bettingDecisions,
        int[] stats,
        int chapter,
        int sourceRun,
        StoryInfluenceProfile fixedInfluence = null)
    {
        if (promptData == null)
        {
            Debug.LogError("StoryRelayManager에 PromptData가 연결되지 않았습니다.", this);
            return;
        }

        if (chapter == (int)Constants.Chapter.Initial)
        {
            Debug.Log("[StoryRelay] Initial은 다음 회차 이야기 생성에서 제외합니다.", this);
            return;
        }

        // 에피소드 단위 요청이므로 모든 대사를 전달하고 text 전체를 변경할 수 있게 합니다.
        List<Dialogue> filtered = history != null
            ? history.FindAll(d => d != null)
            : new List<Dialogue>();

        // Initial처럼 변경 대상으로 표시된 지문이 없는 구간은 AI 요청을 만들지 않습니다.
        if (filtered.Count == 0)
        {
            Debug.Log("[StoryRelay] 변경 대상 지문이 없어 AI 요청을 생략합니다.", this);
            return;
        }
        
        List<BettingDecisionRecord> decisionSnapshot = bettingDecisions != null
            ? new List<BettingDecisionRecord>(bettingDecisions)
            : new List<BettingDecisionRecord>();
        int[] statsSnapshot = stats != null ? (int[])stats.Clone() : new int[0];
        StoryInfluenceProfile influence = fixedInfluence ??
            CreateInfluenceProfile(statsSnapshot, chapter, sourceRun);
        string summary = BuildSummary(filtered, decisionSnapshot);
        string atmosphere = DetermineAtmosphere(influence.intensity);

        string template = trigger == StoryRelayTrigger.EpisodeEnd
            ? promptData.episodeEndTemplate
            : trigger == StoryRelayTrigger.MidTransition
                ? promptData.midTransitionTemplate
                : promptData.chapterEndTemplate;
            
        string finalPrompt = string.Format(
            template,
            summary,
            atmosphere,
            promptData.responseFormatTemplateV2);
        finalPrompt += BuildInfluenceInstruction(influence, statsSnapshot);
        
        StoryPacket packet = new StoryPacket(
            currentFileName,
            finalPrompt,
            filtered,
            sourceRun);
        SendPacket(packet);
    }

    public StoryInfluenceProfile CreateInfluenceProfile(
        int[] stats,
        int chapter,
        int sourceRun)
    {
        int[] safeScores = new int[Mathf.Max(4, stats?.Length ?? 0)];
        int total = 0; //불필요 추후 변경.
        int changeMagnitude = 0;
        for (int i = 0; i < safeScores.Length; i++)
        {
            // 음수 가중치로 내려간 능력치도 약점 강도에 그대로 반영합니다.
            safeScores[i] = stats != null && i < stats.Length ? stats[i] : 0;
            total += safeScores[i];
            changeMagnitude += Mathf.Abs(safeScores[i] - promptData.neutralStatValue);
        }

        List<int> ranking = new List<int>();
        for (int i = 0; i < safeScores.Length; i++) ranking.Add(i);
        ranking.Sort((left, right) =>
        {
            int scoreCompare = safeScores[right].CompareTo(safeScores[left]);
            return scoreCompare != 0 ? scoreCompare : left.CompareTo(right);
        });

        int primaryIndex = ranking[0];
        int weakCandidateA = ranking[ranking.Count - 2];
        int weakCandidateB = ranking[ranking.Count - 1];
        int randomSeed = CreateStableSeed(sourceRun, chapter, safeScores);
        System.Random random = new System.Random(randomSeed);
        double weightA = CalculateWeaknessWeight(safeScores[primaryIndex], safeScores[weakCandidateA]);
        double weightB = CalculateWeaknessWeight(safeScores[primaryIndex], safeScores[weakCandidateB]);
        bool chooseAAsMajor = random.NextDouble() * (weightA + weightB) < weightA;
        int majorWeaknessIndex = chooseAAsMajor ? weakCandidateA : weakCandidateB;
        int minorWeaknessIndex = chooseAAsMajor ? weakCandidateB : weakCandidateA;

        int moderateMinimum = Mathf.Max(0, promptData.moderateChangeMinStat);
        int strongMinimum = Mathf.Max(moderateMinimum + 1, promptData.strongChangeMinStat);
        StoryChangeIntensity intensity = changeMagnitude >= strongMinimum
            ? StoryChangeIntensity.Strong
            : changeMagnitude >= moderateMinimum
                ? StoryChangeIntensity.Moderate
                : StoryChangeIntensity.Subtle;

        return new StoryInfluenceProfile
        {
            chapterIndex = chapter,
            sourceRun = sourceRun,
            intensity = intensity,
            scores = safeScores,
            majorWeaknessIndex = majorWeaknessIndex,
            majorWeaknessName = GetStatName(chapter, majorWeaknessIndex),
            majorWeaknessValue = safeScores[majorWeaknessIndex],
            minorWeaknessIndex = minorWeaknessIndex,
            minorWeaknessName = GetStatName(chapter, minorWeaknessIndex),
            minorWeaknessValue = safeScores[minorWeaknessIndex],
            totalStatValue = total,
            changeMagnitude = changeMagnitude,
            randomSeed = randomSeed
        };
    }

    private static int CreateStableSeed(int sourceRun, int chapter, int[] scores)
    {
        unchecked
        {
            int seed = 17;
            seed = seed * 31 + sourceRun;
            seed = seed * 31 + chapter;
            foreach (int score in scores) seed = seed * 31 + score;
            return seed & int.MaxValue;
        }
    }

    private static double CalculateWeaknessWeight(int primaryScore, int weaknessScore)
    {
        double difference = Mathf.Max(1, primaryScore + 1 - weaknessScore);
        return difference * difference;
    }

    private string DetermineAtmosphere(StoryChangeIntensity intensity)
    {
        if (promptData == null) return "알 수 없는 분위기";

        if (intensity == StoryChangeIntensity.Strong)
            return promptData.highStatAtmosphere;
        if (intensity == StoryChangeIntensity.Moderate)
            return promptData.midStatAtmosphere;
        return promptData.lowStatAtmosphere;
    }

    private string BuildSummary(
        List<Dialogue> dialogs,
        List<BettingDecisionRecord> bettingDecisions)
    {
        if (dialogs == null || dialogs.Count == 0) return "(기록 없음)";

        Dictionary<int, BettingDecisionRecord> decisionsByDialogueId =
            new Dictionary<int, BettingDecisionRecord>();
        foreach (BettingDecisionRecord decision in bettingDecisions)
        {
            if (decision != null)
                decisionsByDialogueId[decision.dialogueId] = decision;
        }
        
        StringBuilder sb = new StringBuilder();
        foreach (var d in dialogs)
        {
            sb.AppendLine($"- [ID: {d.id}] [{d.character}] {d.text}");
            if (decisionsByDialogueId.TryGetValue(d.id, out BettingDecisionRecord decision))
            {
                sb.AppendLine(
                    $"  코인 분포: [{string.Join(", ", decision.coinCounts ?? new int[0])}]\n" +
                    $"  상황 가중치: [{string.Join(", ", decision.statWeights ?? new int[0])}]\n" +
                    $"  실제 변화량: [{string.Join(", ", decision.statChanges ?? new int[0])}]");
            }
        }
        return sb.ToString();
    }

    private string BuildInfluenceInstruction(
        StoryInfluenceProfile influence,
        int[] stats)
    {
        string instruction;
        if (influence.intensity == StoryChangeIntensity.Strong)
            instruction = promptData.strongChangeInstruction;
        else if (influence.intensity == StoryChangeIntensity.Moderate)
            instruction = promptData.moderateChangeInstruction;
        else
            instruction = promptData.subtleChangeInstruction;

        return
            $"\n\n[플레이 결과]\n" +
            $"전체 수치: [{string.Join(", ", stats)}]\n" +
            $"전체 합계: {influence.totalStatValue}\n" +
            $"중립값 대비 변화량: {influence.changeMagnitude}\n" +
            $"주요 약점: {influence.majorWeaknessName} ({influence.majorWeaknessValue})\n" +
            $"보조 약점: {influence.minorWeaknessName} ({influence.minorWeaknessValue})\n" +
            $"변경 강도: {influence.intensity}\n" +
            $"변경 지침: {instruction}\n" +
            "주요 약점은 갈등과 실수에 강하게, 보조 약점은 불안 요소에 약하게 반영하세요.\n" +
            "id와 이야기 순서, destination, character, type은 변경하지 마세요.\n" +
            "원문에서 { }로 감싼 문자열은 괄호를 포함해 한 글자도 변경하지 마세요.";
    }

    private static string GetStatName(int chapter, int statIndex)
    {
        string[][] namesByChapter =
        {
            new[] { "None", "None", "None", "None" },
            new[] { "Strength", "Technique", "Tactics", "Strategy" },
            new[] { "Intuition", "Learning", "Magic", "Wisdom" }
        };

        if (chapter < 0 || chapter >= namesByChapter.Length ||
            statIndex < 0 || statIndex >= namesByChapter[chapter].Length)
            return $"Stat_{statIndex}";

        return namesByChapter[chapter][statIndex];
    }

    private void SendPacket(StoryPacket packet)
    {
        if (AIAPIClient.Instance == null)
        {
            Debug.LogError("AIAPIClient 인스턴스를 찾을 수 없습니다.");
            return;
        }

        AIAPIClient.Instance.ProcessPacket(packet);
    }
}
