using OneWay.Loading;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OneWay.Battle3D
{
    // Only the first scene of a Play session is inspected. Later Decision -> Battle
    // transitions and Loading -> Battle activation cannot trigger another loading screen.
    public static class BattleStandaloneLoading
    {
        static bool waitingForInitialScene;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ObserveInitialScene()
        {
            waitingForInitialScene = true;
            SceneManager.sceneLoaded -= OnInitialSceneLoaded;
            SceneManager.sceneLoaded += OnInitialSceneLoaded;
        }

        static void OnInitialSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!waitingForInitialScene) return;
            waitingForInitialScene = false;
            SceneManager.sceneLoaded -= OnInitialSceneLoaded;
            if (SceneManager.sceneCount != 1 || scene.path != LoadingScreenController.ScenePath(LoadingDestination.Battle) ||
                LoadingScreenController.IsTransitioning || Battle3DEntry.HasPendingData) return;
            if (!LoadingScreenController.OpenBeforeStandaloneBattle(scene))
                Debug.LogError("독립 실행 전투의 로딩 화면을 열지 못했습니다.");
        }

        // Also cover Play configurations that retain the already loaded scene.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CheckRetainedInitialScene()
        {
            if (waitingForInitialScene) OnInitialSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }
    }
}
