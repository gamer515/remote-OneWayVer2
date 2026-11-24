using System;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>One optional, scene-owned speaker for the mine dialogue view.</summary>
internal sealed class NpcMumbleVoice
{
    private const float VolumeScale = .36f;
    private const string VoiceCode = "animalese speed=1.85 seed=";
    private AudioSource source;
    private Component speaker;
    private Func<string, float> say;
    private Action stop;
    private FieldInfo voiceCode;
    private bool unavailable;

    internal void Speak(Transform parent, string id, string displayedText)
    {
        if (unavailable || string.IsNullOrWhiteSpace(displayedText)) return;
        float volume = VolumeScale * Mathf.Clamp01(PlayerPrefs.GetFloat("BGM_Volume", .5f));
        if (volume <= 0f)
        {
            Stop();
            return;
        }

        try
        {
            if (speaker == null && !Bind(parent)) return;
            source.volume = volume;
            voiceCode.SetValue(speaker, VoiceCode + VoiceSeed(id).ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (say(displayedText) <= 0f) Stop();
        }
        catch (Exception error)
        {
            Disable(error);
        }
    }

    internal void Stop()
    {
        if (speaker == null || unavailable) return;
        try { stop(); }
        catch (Exception error) { Disable(error); }
    }

    private bool Bind(Transform parent)
    {
        var type = Type.GetType("MumbleSpeak.MumbleSpeaker, MumbleSpeak.Runtime", throwOnError: false);
        if (type == null)
        {
            unavailable = true;
            return false;
        }
        var sayMethod = type.GetMethod("Say", new[] { typeof(string) });
        var stopMethod = type.GetMethod("Stop", Type.EmptyTypes);
        voiceCode = type.GetField("voiceCode");
        if (sayMethod == null || stopMethod == null || voiceCode == null)
            throw new MissingMethodException("Mumble Speak 1.1.0 speaker API");

        // Check native loading before the SDK's Unity Awake creates its looped carrier.
        IntPtr probe = mumble_create(AudioSettings.outputSampleRate);
        if (probe == IntPtr.Zero) throw new InvalidOperationException("Mumble Speak could not initialize");
        mumble_destroy(probe);

        var host = new GameObject("Mine NPC Mumble");
        host.transform.SetParent(parent, false);
        source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.volume = 0f;
        speaker = host.AddComponent(type);
        say = (Func<string, float>)sayMethod.CreateDelegate(typeof(Func<string, float>), speaker);
        stop = (Action)stopMethod.CreateDelegate(typeof(Action), speaker);
        return true;
    }

    private void Disable(Exception error)
    {
        unavailable = true;
        if (source != null)
        {
            source.volume = 0f;
            source.Stop();
        }
        Debug.LogWarning($"NPC 말소리를 사용할 수 없습니다. ({error.GetType().Name})");
    }

    private static uint VoiceSeed(string id)
    {
        uint hash = 2166136261u;
        foreach (char character in id)
            hash = unchecked((hash ^ character) * 16777619u);
        return hash == 0 ? 1u : hash;
    }

#if (UNITY_IOS || UNITY_TVOS) && !UNITY_EDITOR
    private const string NativeLibrary = "__Internal";
#else
    private const string NativeLibrary = "mumble";
#endif
    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mumble_create(int sampleRate);
    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern void mumble_destroy(IntPtr synth);
}
