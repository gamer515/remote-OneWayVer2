using UnityEngine;

/// <summary>
/// 지문의 목적지와 지형 장소를 연결하고 지문별 목표 Z를 계산합니다.
/// </summary>
public sealed class DestinationController
{
    private readonly TerrainPlaceRegistry placeRegistry;

    private ScenarioData scenario;
    private PlaceData currentDestination;
    private int destinationStoryIndex = -1;
    private int segmentStartStoryIndex = -1;
    private float segmentStartZ;
    private float destinationZ;
    private float plannedTargetZ;

    public string CurrentDestinationName => currentDestination?.destination;

    public DestinationController(
        TerrainPlaceRegistry placeRegistry,
        float initialTargetZ)
    {
        this.placeRegistry = placeRegistry;
        plannedTargetZ = initialTargetZ;
    }

    public void BeginScenario(ScenarioData newScenario, int storyIndex)
    {
        scenario = newScenario;
        ResetSegment();
        CacheNextDestination(storyIndex);
    }

    public void OnStoryAdvanced(int storyIndex)
    {
        if (currentDestination != null && storyIndex > destinationStoryIndex)
        {
            ResetSegment();
            CacheNextDestination(storyIndex);
        }
    }

    public float GetTargetZ(int storyIndex)
    {
        if (currentDestination == null || destinationStoryIndex < segmentStartStoryIndex)
            return plannedTargetZ;

        // 시작 지문에서는 정지하고, 지문 사이의 전환 횟수만 이동 횟수로 계산합니다.
        // 예: 0번에서 시작해 2번에 도착하면 0→1, 1→2의 두 번으로 나눕니다.
        int totalSteps = destinationStoryIndex - segmentStartStoryIndex;
        if (totalSteps <= 0)
        {
            plannedTargetZ = destinationZ;
            return plannedTargetZ;
        }

        int completedSteps = storyIndex - segmentStartStoryIndex;
        float progress = Mathf.Clamp01((float)completedSteps / totalSteps);
        plannedTargetZ = Mathf.Lerp(segmentStartZ, destinationZ, progress);
        return plannedTargetZ;
    }

    private void ResetSegment()
    {
        currentDestination = null;
        destinationStoryIndex = -1;
        segmentStartStoryIndex = -1;
    }

    private void CacheNextDestination(int storyIndex)
    {
        if (scenario?.MainStory == null)
            return;

        for (int i = storyIndex; i < scenario.MainStory.Count; i++)
        {
            string destinationName = scenario.MainStory[i].destination;
            if (string.IsNullOrEmpty(destinationName))
                continue;

            if (placeRegistry == null || !placeRegistry.TryGetPlace(destinationName, out PlaceData place))
                continue;

            currentDestination = place;
            destinationStoryIndex = i;
            segmentStartStoryIndex = storyIndex;
            segmentStartZ = plannedTargetZ;
            destinationZ = placeRegistry.GetWorldPosition(place).z;
            return;
        }
    }
}
