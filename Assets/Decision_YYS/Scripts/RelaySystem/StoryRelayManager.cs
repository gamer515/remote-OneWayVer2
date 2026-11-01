using UnityEngine;
using System.Collections.Generic;

/// <summary>Builds deterministic chapter influence records from player stats.</summary>
public class StoryRelayManager : MonoBehaviour
{
    [SerializeField] private PromptData promptData;

    public StoryInfluenceProfile CreateInfluenceProfile(
        int[] stats,
        int chapter,
        int sourceRun)
    {
        int[] safeScores = new int[Mathf.Max(4, stats?.Length ?? 0)];
        int total = 0;
        int changeMagnitude = 0;
        for (int i = 0; i < safeScores.Length; i++)
        {
            // 저장된 실제 능력치를 기준으로 약점 강도를 계산합니다.
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

    private static string GetStatName(int chapter, int statIndex)
    {
        string[][] namesByChapter =
        {
            new[] { "Health", "Speed", "Intelligence", "Charm" },
            new[] { "Health", "Speed", "Intelligence", "Charm" },
            new[] { "Health", "Speed", "Intelligence", "Charm" }
        };

        if (chapter < 0 || chapter >= namesByChapter.Length ||
            statIndex < 0 || statIndex >= namesByChapter[chapter].Length)
            return $"Stat_{statIndex}";

        return namesByChapter[chapter][statIndex];
    }

}
