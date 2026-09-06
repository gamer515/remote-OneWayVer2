using UnityEngine;

/// <summary>
/// 지문의 건물 목적지, 인물 대화 정지 구간과 에피소드 끝까지의 이동 목표를 계산합니다.
/// </summary>
public sealed class DestinationController
{
    private readonly TerrainPlaceRegistry placeRegistry;
    private readonly float episodeEndZ;

    private ScenarioData scenario;
    private float[] targetZByStory;

    public DestinationController(
        TerrainPlaceRegistry placeRegistry,
        float initialTargetZ,
        float episodeEndZ)
    {
        this.placeRegistry = placeRegistry;
        this.episodeEndZ = Mathf.Max(initialTargetZ, episodeEndZ);
    }

    public void BeginScenario(ScenarioData newScenario, int storyIndex, float initialTargetZ)
    {
        scenario = newScenario;
        BuildStoryTargets(storyIndex, initialTargetZ);
    }

    public void OnStoryAdvanced(int storyIndex)
    {
        // 목표 위치는 시나리오 시작 시 지문별로 계산되어 있으므로 다시 검색할 필요가 없습니다.
    }

    public float GetTargetZ(int storyIndex)
    {
        if (targetZByStory == null || targetZByStory.Length == 0)
            return 0f;

        return targetZByStory[Mathf.Clamp(storyIndex, 0, targetZByStory.Length - 1)];
    }

    public bool TryGetCharacterPosition(int storyIndex, out Vector3 worldPosition)
    {
        worldPosition = default;
        if (scenario?.MainStory == null || storyIndex < 0 || storyIndex >= scenario.MainStory.Count)
            return false;

        string characterId = scenario.MainStory[storyIndex].character;
        if (string.IsNullOrWhiteSpace(characterId) ||
            string.Equals(characterId, "none", System.StringComparison.OrdinalIgnoreCase) ||
            placeRegistry == null || !placeRegistry.TryGetCharacter(characterId, out PlaceData character))
        {
            return false;
        }

        worldPosition = placeRegistry.GetWorldPosition(character);
        return true;
    }

    private void BuildStoryTargets(int startStoryIndex, float initialTargetZ)
    {
        int count = scenario?.MainStory?.Count ?? 0;
        targetZByStory = new float[count];
        for (int i = 0; i < count; i++)
            targetZByStory[i] = initialTargetZ;

        if (count == 0)
            return;

        int segmentStartIndex = Mathf.Clamp(startStoryIndex, 0, count - 1);
        float segmentStartZ = initialTargetZ;

        for (int i = segmentStartIndex; i < count; i++)
        {
            if (!TryGetDestinationZ(i, out float destinationZ))
                continue;

            FillSegment(segmentStartIndex, i, segmentStartZ, destinationZ);
            segmentStartIndex = i;
            segmentStartZ = destinationZ;
        }

        // 마지막 건물 뒤에도 이동 가능한 지문이 남으면 에피소드 지형 끝까지 거리를 나눕니다.
        int lastStoryIndex = count - 1;
        if (segmentStartIndex < lastStoryIndex)
            FillSegment(segmentStartIndex, lastStoryIndex, segmentStartZ, episodeEndZ);
    }

    private void FillSegment(int startIndex, int endIndex, float startZ, float endZ)
    {
        targetZByStory[startIndex] = startZ;
        int movableStepCount = 0;
        for (int i = startIndex + 1; i <= endIndex; i++)
        {
            if (!IsCharacterDialogue(i) || TryGetDestinationZ(i, out _))
                movableStepCount++;
        }

        if (movableStepCount == 0)
            return;

        int completedSteps = 0;
        float previousTarget = startZ;
        for (int i = startIndex + 1; i <= endIndex; i++)
        {
            if (IsCharacterDialogue(i) && !TryGetDestinationZ(i, out _))
            {
                targetZByStory[i] = previousTarget;
                continue;
            }

            completedSteps++;
            previousTarget = Mathf.Lerp(startZ, endZ, (float)completedSteps / movableStepCount);
            targetZByStory[i] = previousTarget;
        }
    }

    private bool TryGetDestinationZ(int storyIndex, out float destinationZ)
    {
        destinationZ = 0f;
        string destination = scenario.MainStory[storyIndex].destination;
        if (string.IsNullOrWhiteSpace(destination) || placeRegistry == null ||
            !placeRegistry.TryGetPlace(destination, out PlaceData place))
        {
            return false;
        }

        destinationZ = placeRegistry.GetWorldPosition(place).z;
        return true;
    }

    private bool IsCharacterDialogue(int storyIndex)
    {
        return TryGetCharacterPosition(storyIndex, out _);
    }
}
