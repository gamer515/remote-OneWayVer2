using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

internal sealed class UiInteractionAudio : MonoBehaviour
{
    private const float GearTickInterval = .08f;
    private static UiInteractionAudio instance;
    private AudioSource source;
    private AudioClip click;
    private AudioClip ratchet;
    private int pendingGearTicks;
    private float nextGearTick;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneButtons()
    {
        SceneManager.sceneLoaded -= BindSceneButtons;
        SceneManager.sceneLoaded += BindSceneButtons;
    }

    private static void BindSceneButtons(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single && instance != null)
            instance.pendingGearTicks = 0;

        foreach (var root in scene.GetRootGameObjects())
            foreach (var button in root.GetComponentsInChildren<Button>(true))
                Bind(button);
    }

    internal static void Bind(Button button)
    {
        button.onClick.RemoveListener(PlayClick);
        button.onClick.AddListener(PlayClick);
    }

    private static UiInteractionAudio GetPlayer()
    {
        if (instance != null) return instance;

        var player = new GameObject("UI Interaction Audio");
        DontDestroyOnLoad(player);
        instance = player.AddComponent<UiInteractionAudio>();
        instance.source = player.AddComponent<AudioSource>();
        instance.source.playOnAwake = false;
        instance.source.spatialBlend = 0f;
        return instance;
    }

    // Coin effects already use this saved slider value.
    private static float EffectVolume => Mathf.Clamp01(PlayerPrefs.GetFloat("BGM_Volume", .5f));

    internal static void PlayClick()
    {
        float volume = EffectVolume;
        if (volume <= 0f) return;
        var player = GetPlayer();
        player.click ??= Resources.Load<AudioClip>("Audio/UiControls/sfxmint-ui-click-82");
        if (player.click != null) player.source.PlayOneShot(player.click, volume * .6f);
    }

    internal static void PlayGearSteps(int steps)
    {
        if (steps <= 0 || EffectVolume <= 0f) return;
        var player = GetPlayer();
        player.pendingGearTicks += steps;
        player.PlayQueuedGearTick();
    }

    private void Update() => PlayQueuedGearTick();

    private void PlayQueuedGearTick()
    {
        if (pendingGearTicks == 0 || Time.unscaledTime < nextGearTick) return;

        pendingGearTicks--;
        nextGearTick = Time.unscaledTime + GearTickInterval;
        float volume = EffectVolume;
        if (volume <= 0f) return;

        ratchet ??= Resources.Load<AudioClip>("Audio/UiControls/freesound_community-single_ratchet-03-97604");
        if (ratchet != null) source.PlayOneShot(ratchet, volume * .6f);
    }
}
