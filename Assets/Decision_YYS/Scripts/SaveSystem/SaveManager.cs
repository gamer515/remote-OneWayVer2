using UnityEngine;

public class SaveManager
{
    private readonly ProfileData profile;
    private GameProgress cachedProgress;

    public int CurrentRun => profile.currentRun;

    public SaveManager()
    {
        // 프로필의 현재 회차를 기준으로 진행도와 능력치의 저장 폴더를 고정합니다.
        profile = SaveIOService.Instance.LoadOrCreateProfile();
        cachedProgress = SaveIOService.Instance.LoadRunData<GameProgress>(CurrentRun, "Progress")
            ?? new GameProgress();
        EnsureRunSeed();
    }

    public int RunSeed => EnsureRunSeed();

    private int EnsureRunSeed()
    {
        if (cachedProgress.runSeed != 0)
            return cachedProgress.runSeed;

        byte[] bytes = System.Guid.NewGuid().ToByteArray();
        cachedProgress.runSeed = System.Math.Max(1, System.BitConverter.ToInt32(bytes, 0) & int.MaxValue);
        SaveIOService.Instance.SaveRunData(CurrentRun, "Progress", cachedProgress);
        return cachedProgress.runSeed;
    }

    /// <summary>
    /// 현재 스토리 진행도를 저장합니다.
    /// </summary>
    /// <param name="chapterIndex">챕터 인덱스. 예: Initial, Chapter_1, Chapter_2</param>
    /// <param name="episodeIndex">챕터 내부 에피소드 인덱스.</param>
    /// <param name="storyIndex">에피소드 내부 지문 인덱스.</param>
    public void SaveProgress(int chapterIndex, int episodeIndex, int storyIndex)
    {
        cachedProgress.chapterIndex = chapterIndex;
        cachedProgress.episodeIndex = episodeIndex;
        cachedProgress.storyIndex = storyIndex;

        SaveIOService.Instance.SaveRunData(CurrentRun, "Progress", cachedProgress);
        Debug.Log($"[Save] Progress Saved: Ch {chapterIndex}, Ep {episodeIndex}, St {storyIndex}");
    }

    public void SaveStats(int[] statsArray)
    {
        PlayerStats stats = new PlayerStats
        {
            stats = statsArray
        };
        SaveIOService.Instance.SaveRunData(CurrentRun, "Stats", stats);
    }

    public void SaveProgressAndPlayerPosition(
        int chapterIndex,
        int episodeIndex,
        int storyIndex,
        Vector3 position,
        Quaternion rotation)
    {
        // 지문 인덱스와 목표 위치는 같은 체크포인트이므로 한 번의 파일 쓰기로 저장합니다.
        cachedProgress.chapterIndex = chapterIndex;
        cachedProgress.episodeIndex = episodeIndex;
        cachedProgress.storyIndex = storyIndex;
        cachedProgress.currentPosition = new[] { position.x, position.y, position.z };
        cachedProgress.currentRotation = new[] { rotation.x, rotation.y, rotation.z, rotation.w };

        SaveIOService.Instance.SaveRunData(CurrentRun, "Progress", cachedProgress);
        Debug.Log(
            $"[Save] Checkpoint Saved: Ch {chapterIndex}, Ep {episodeIndex}, " +
            $"St {storyIndex}, Z {position.z}");
    }

    public Vector3? LoadPlayerPosition()
    {
        float[] position = cachedProgress.currentPosition;
        if (position == null || position.Length < 3)
            return null;

        for (int i = 0; i < 3; i++)
        {
            if (float.IsNaN(position[i]) || float.IsInfinity(position[i]))
                return null;
        }

        return new Vector3(position[0], position[1], position[2]);
    }

    public Quaternion? LoadPlayerRotation()
    {
        float[] rotation = cachedProgress.currentRotation;
        if (rotation == null || rotation.Length < 4)
            return null;

        for (int i = 0; i < 4; i++)
        {
            if (float.IsNaN(rotation[i]) || float.IsInfinity(rotation[i]))
                return null;
        }

        Quaternion value = new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]);
        return Quaternion.Dot(value, value) > 0.0001f ? Quaternion.Normalize(value) : null;
    }

    public void ClearPlayerPosition()
    {
        cachedProgress.currentPosition = null;
        cachedProgress.currentRotation = null;
    }

    public void SaveEncounterProgress(
        int chapterIndex,
        int episodeIndex,
        Vector3 playerPosition,
        Quaternion playerRotation,
        string phase,
        string activePlaceId,
        int activeCardIndex,
        System.Collections.Generic.IEnumerable<string> resolvedPlaceIds)
    {
        // 실제 플레이어 좌표와 배치물 진행도를 한 번에 기록합니다.
        cachedProgress.chapterIndex = chapterIndex;
        cachedProgress.episodeIndex = episodeIndex;
        cachedProgress.currentPosition = new[] { playerPosition.x, playerPosition.y, playerPosition.z };
        cachedProgress.currentRotation = new[]
        {
            playerRotation.x, playerRotation.y, playerRotation.z, playerRotation.w
        };
        cachedProgress.interactionPhase = phase;
        cachedProgress.activePlaceId = activePlaceId;
        cachedProgress.activeCardIndex = activeCardIndex;
        cachedProgress.resolvedPlaceIds = new System.Collections.Generic.List<string>(resolvedPlaceIds);
        SaveIOService.Instance.SaveRunData(CurrentRun, "Progress", cachedProgress);
    }

    public int[] LoadRemainingCoins()
    {
        if (cachedProgress.remainingCoins == null || cachedProgress.remainingCoins.Length != 4)
            cachedProgress.remainingCoins = new[] { 5, 5, 5, 5 };

        return (int[])cachedProgress.remainingCoins.Clone();
    }

    public void SaveRemainingCoins(int[] remainingCoins)
    {
        if (remainingCoins == null || remainingCoins.Length != 4)
            return;

        cachedProgress.remainingCoins = (int[])remainingCoins.Clone();
        SaveIOService.Instance.SaveRunData(CurrentRun, "Progress", cachedProgress);
    }

    public System.Collections.Generic.List<string> LoadStoredItemIds()
    {
        if (cachedProgress.storedItemIds == null)
            cachedProgress.storedItemIds = new System.Collections.Generic.List<string>();
        return new System.Collections.Generic.List<string>(cachedProgress.storedItemIds);
    }

    public void SaveStoredItemIds(System.Collections.Generic.IReadOnlyList<string> itemIds)
    {
        cachedProgress.storedItemIds = itemIds == null
            ? new System.Collections.Generic.List<string>()
            : new System.Collections.Generic.List<string>(itemIds);
        SaveIOService.Instance.SaveRunData(CurrentRun, "Progress", cachedProgress);
    }

    public bool HasUnlock(string unlockId)
    {
        return !string.IsNullOrWhiteSpace(unlockId) &&
               cachedProgress.unlockedIds != null &&
               cachedProgress.unlockedIds.Contains(unlockId);
    }

    public bool HasAllUnlocks(System.Collections.Generic.IEnumerable<string> unlockIds)
    {
        if (unlockIds == null) return true;
        foreach (string unlockId in unlockIds)
            if (!string.IsNullOrWhiteSpace(unlockId) && !HasUnlock(unlockId)) return false;
        return true;
    }

    public void ApplyEncounterEffect(
        string effectId,
        System.Collections.Generic.IEnumerable<string> grantsUnlocks,
        string relationshipId,
        int relationshipDelta)
    {
        if (string.IsNullOrWhiteSpace(effectId)) return;
        cachedProgress.appliedEffectIds ??= new System.Collections.Generic.List<string>();
        if (cachedProgress.appliedEffectIds.Contains(effectId)) return;

        cachedProgress.unlockedIds ??= new System.Collections.Generic.List<string>();
        if (grantsUnlocks != null)
        {
            foreach (string unlockId in grantsUnlocks)
                if (!string.IsNullOrWhiteSpace(unlockId) &&
                    !cachedProgress.unlockedIds.Contains(unlockId))
                    cachedProgress.unlockedIds.Add(unlockId);
        }

        if (!string.IsNullOrWhiteSpace(relationshipId) && relationshipDelta != 0)
        {
            cachedProgress.relationships ??= new System.Collections.Generic.List<RelationshipValue>();
            RelationshipValue relationship = cachedProgress.relationships.Find(
                value => value != null && value.id == relationshipId);
            if (relationship == null)
            {
                relationship = new RelationshipValue { id = relationshipId };
                cachedProgress.relationships.Add(relationship);
            }
            relationship.value += relationshipDelta;
        }

        cachedProgress.appliedEffectIds.Add(effectId);
        SaveIOService.Instance.SaveRunData(CurrentRun, "Progress", cachedProgress);
    }

    public void MarkEncounterCompleted(string placeId)
    {
        if (string.IsNullOrWhiteSpace(placeId)) return;
        ApplyEncounterEffect(
            $"{placeId}:completed",
            new[] { $"event:{placeId}" },
            null,
            0);
    }

    public void RecordInfluenceProfile(StoryInfluenceProfile profile)
    {
        if (profile == null)
            return;

        if (cachedProgress.influenceHistory == null)
            cachedProgress.influenceHistory = new System.Collections.Generic.List<StoryInfluenceProfile>();

        cachedProgress.influenceHistory.RemoveAll(
            item => item != null && item.chapterIndex == profile.chapterIndex);
        cachedProgress.influenceHistory.Add(profile);
        SaveIOService.Instance.SaveRunData(CurrentRun, "Progress", cachedProgress);
    }

    public void SaveCompletedEpisode(CompletedEpisodeRecord episode)
    {
        if (episode == null || string.IsNullOrWhiteSpace(episode.scenarioPath))
            return;

        if (cachedProgress.pendingEpisodes == null)
            cachedProgress.pendingEpisodes = new System.Collections.Generic.List<CompletedEpisodeRecord>();

        cachedProgress.pendingEpisodes.RemoveAll(
            item => item != null && item.scenarioPath == episode.scenarioPath);
        cachedProgress.pendingEpisodes.Add(episode);
        SaveIOService.Instance.SaveRunData(CurrentRun, "Progress", cachedProgress);
    }

    public System.Collections.Generic.List<CompletedEpisodeRecord> LoadPendingEpisodes()
    {
        return cachedProgress.pendingEpisodes ??
            new System.Collections.Generic.List<CompletedEpisodeRecord>();
    }

    public void ClearPendingEpisodes()
    {
        cachedProgress.pendingEpisodes?.Clear();
        SaveIOService.Instance.SaveRunData(CurrentRun, "Progress", cachedProgress);
    }

    public GameProgress LoadProgress()
    {
        return cachedProgress;
    }

    public PlayerStats LoadStats()
    {
        if (SaveIOService.Instance.RunDataExists(CurrentRun, "Stats"))
        {
            return SaveIOService.Instance.LoadRunData<PlayerStats>(CurrentRun, "Stats");
        }
        return null;
    }

    public bool HasSaveData(string key)
    {
        return SaveIOService.Instance.RunDataExists(CurrentRun, key);
    }

    /// <summary>
    /// 현재 회차를 완료 상태로 저장합니다. 여러 번 호출되어도 회차 번호는 증가하지 않습니다.
    /// </summary>
    public void CompleteCurrentRun()
    {
        if (cachedProgress.isCompleted)
            return;

        cachedProgress.isCompleted = true;
        SaveIOService.Instance.SaveRunData(CurrentRun, "Progress", cachedProgress);
        Debug.Log($"[Save] {CurrentRun}회차 완료 상태 저장");
    }

    /// <summary>
    /// 기존 회차를 보존하고 다음 회차의 빈 진행도를 만든 뒤 활성 회차를 변경합니다.
    /// 실제 씬 재시작은 이 메서드를 호출하는 화면 흐름에서 담당합니다.
    /// </summary>
    public int StartNextRun()
    {
        CompleteCurrentRun();

        int nextRun = CurrentRun + 1;
        GameProgress nextProgress = new GameProgress
        {
            runSeed = System.Math.Max(
                1,
                System.BitConverter.ToInt32(System.Guid.NewGuid().ToByteArray(), 0) & int.MaxValue)
        };

        // 다음 회차 파일을 먼저 만든 뒤 Profile을 갱신하여 불완전한 회차를 가리키지 않게 합니다.
        SaveIOService.Instance.SaveRunData(nextRun, "Progress", nextProgress);
        SaveIOService.Instance.SaveRunData(nextRun, "Stats", new PlayerStats());
        profile.currentRun = nextRun;
        SaveIOService.Instance.SaveProfile(profile);
        cachedProgress = nextProgress;

        Debug.Log($"[Save] {nextRun}회차 저장 데이터 생성 및 활성화 완료");
        return nextRun;
    }
}
