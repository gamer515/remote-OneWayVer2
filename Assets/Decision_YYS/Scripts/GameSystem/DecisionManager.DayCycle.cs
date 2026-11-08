using System;
using System.Globalization;
using LearningDispenserAssetKit;
using UnityEngine;

public partial class DecisionManager
{
    [SerializeField] private Material chapterSkybox;
    [SerializeField] private Light chapterSun;
    [SerializeField] private DeskCalendarDisplay chapterCalendar;
    [SerializeField] private string calendarStartDate = "1005-01-01";
    private Material runtimeChapterSky, previousSky;
    private float previousSunIntensity;
    private int lastCalendarDay = -1, lastCalendarRemaining = -1;
    private bool lastCalendarNight;

    private void StartChapterSky()
    {
        if (mineRules?.dayCycle == null || chapterSkybox == null || runtimeChapterSky != null) return;
        previousSky = RenderSettings.skybox;
        runtimeChapterSky = new Material(chapterSkybox);
        // EmacEArt blends toward the disc color, even when that color is black.
        // A disc the same color as the sky is invisible across the whole day cycle.
        runtimeChapterSky.SetColor("_SunGlowColor", Color.black);
        runtimeChapterSky.SetFloat("_CloudsEnabled", 0f);
        runtimeChapterSky.SetFloat("_StarsEnabled", 0f);
        runtimeChapterSky.DisableKeyword("_CLOUDSENABLED_ON");
        runtimeChapterSky.DisableKeyword("_STARSENABLED_ON");
        runtimeChapterSky.SetFloat("_AtmosphereStrength", 0f);
        runtimeChapterSky.SetFloat("_SkyGradStr", 0f);
        runtimeChapterSky.SetFloat("_Contrast", 1f);
        if (chapterSun != null) previousSunIntensity = chapterSun.intensity;
        RenderSettings.skybox = runtimeChapterSky;
    }

    private void UpdateChapterSky(float daylight)
    {
        if (runtimeChapterSky == null || mineRules?.dayCycle == null) return;
        var cycle = mineRules.dayCycle;
        float gray = Mathf.Lerp(cycle.nightSky, cycle.daylightSky, daylight);
        var skyColor = new Color(gray, gray, gray, 1);
        runtimeChapterSky.SetColor("_ZenithColor", skyColor);
        runtimeChapterSky.SetColor("_HorizonColor", skyColor);
        runtimeChapterSky.SetColor("_GroundColor", skyColor);
        runtimeChapterSky.SetColor("_SkyGradColor", skyColor);
        runtimeChapterSky.SetColor("_SunColor", skyColor);
        if (chapterSun != null) chapterSun.intensity = previousSunIntensity * Mathf.Lerp(cycle.nightLightMultiplier, 1f, daylight);
    }

    private void StopChapterSky()
    {
        if (runtimeChapterSky == null) return;
        if (RenderSettings.skybox == runtimeChapterSky) RenderSettings.skybox = previousSky;
        if (chapterSun != null) chapterSun.intensity = previousSunIntensity;
        Destroy(runtimeChapterSky);
        runtimeChapterSky = null;
    }

    private void UpdateChapterCalendar()
    {
        if (chapterCalendar == null) return;
        var cycle = mineRules?.dayCycle;
        chapterCalendar.gameObject.SetActive(mine != null && cycle != null);
        if (mine == null || cycle == null)
        {
            lastCalendarDay = -1;
            return;
        }

        int day = Mathf.FloorToInt(mine.elapsedSeconds / cycle.DayLength);
        float position = Mathf.Repeat(mine.elapsedSeconds, cycle.DayLength);
        bool taxCountdown = taxRules != null && mine.nextTaxDay > 0;
        int remaining = taxCountdown
            ? (mine.taxPatrolActive && mine.HasTaxInvoice ? 0 : Mathf.Max(0, mine.nextTaxDay - day))
            : Mathf.CeilToInt(cycle.DayLength - position);
        bool night = position >= cycle.daySeconds + cycle.duskSeconds &&
                     position < cycle.DayLength - cycle.dawnSeconds;
        if (lastCalendarDay == day && lastCalendarRemaining == remaining && lastCalendarNight == night) return;
        if (!DateTime.TryParseExact(calendarStartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var start))
            start = new DateTime(1005, 1, 1);
        int safeDay = Mathf.Clamp(day, 0, (DateTime.MaxValue.Date - start).Days);
        string date = start.AddDays(safeDay).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string countdown = taxCountdown ? (remaining == 0 ? "D-DAY" : $"D-{remaining}") : $"{remaining / 60:00}:{remaining % 60:00}";
        chapterCalendar.SetDisplay(date, countdown, night);
        lastCalendarDay = day;
        lastCalendarRemaining = remaining;
        lastCalendarNight = night;
    }
}
