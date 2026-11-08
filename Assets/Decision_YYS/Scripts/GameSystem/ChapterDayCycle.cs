using System;
using UnityEngine;

[Serializable]
public sealed class ChapterDayCycle
{
    public float daySeconds = 135, duskSeconds = 15, nightSeconds = 135, dawnSeconds = 15;
    public float daylightSky = .34f, nightSky = .12f, nightLightMultiplier = .28f;

    public float DayLength => daySeconds + duskSeconds + nightSeconds + dawnSeconds;
    public bool IsValid => FinitePositive(daySeconds) && FinitePositive(duskSeconds) &&
        FinitePositive(nightSeconds) && FinitePositive(dawnSeconds) && float.IsFinite(DayLength) &&
        InRange(daylightSky) && InRange(nightSky) && InRange(nightLightMultiplier);

    public void RestoreClock(MineChapterProgress progress)
    {
        const float legacyDayLength = 600f;
        float savedLength = FinitePositive(progress.savedDayLength) ? progress.savedDayLength : legacyDayLength;
        progress.elapsedSeconds *= DayLength / savedLength;
        progress.savedDayLength = DayLength;
    }

    public float Daylight(float elapsedSeconds)
    {
        float position = Mathf.Repeat(elapsedSeconds, DayLength);
        if (position < daySeconds) return 1f;
        position -= daySeconds;
        if (position < duskSeconds) return 1f - position / duskSeconds;
        position -= duskSeconds;
        if (position < nightSeconds) return 0f;
        return Mathf.Clamp01((position - nightSeconds) / dawnSeconds);
    }

    private static bool FinitePositive(float value) => float.IsFinite(value) && value > 0;
    private static bool InRange(float value) => float.IsFinite(value) && value >= 0 && value <= 1;
}
