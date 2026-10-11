using UnityEditor;

/// <summary>Native callbacks must finish before Play-stop or managed domain unload.</summary>
[InitializeOnLoad]
public static class LocalStoryReloadGuard
{
    static LocalStoryReloadGuard()
    {
        AssemblyReloadEvents.beforeAssemblyReload += LocalStoryLifetime.CancelAndWait;
        EditorApplication.quitting += LocalStoryLifetime.CancelAndWait;
        EditorApplication.playModeStateChanged += state =>
        {
            if(state == PlayModeStateChange.ExitingPlayMode) LocalStoryLifetime.CancelAndWait();
        };
    }
}
