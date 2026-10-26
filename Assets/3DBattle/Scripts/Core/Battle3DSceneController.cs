using UnityEngine;
using UnityEngine.SceneManagement;

namespace OneWay.Battle3D
{
    public sealed class Battle3DSceneController : MonoBehaviour
    {
        public BattlePlayerController player;
        public BattleCombatFeedback feedback;
        public BattleFollowCamera followCamera;
        public BattleStage[] stages = new BattleStage[0];
        [SerializeField, HideInInspector] int selectedStageIndex = 1;
        public int SelectedStageIndex => selectedStageIndex;
        public Battle3DStartData StartData { get; private set; }
        public BattleStage CurrentStage => FindStage(selectedStageIndex);
        public event System.Action<BattleStage> StageSelected;
        const string ReturnScene = "DecisionScene";
        const double ReturnDelay = 5d;
        double returnAt = -1d;
        bool battleReady, returnRequested;
        System.Func<bool, bool> finishFinalBattle = GameManager.TryFinishFinalBattle;
        public bool IsReturnPending => returnAt >= 0d && !returnRequested;
        public float ReturnSecondsRemaining => IsReturnPending
            ? Mathf.Max(0f, (float)(returnAt - Time.realtimeSinceStartupAsDouble)) : 0f;

        void Update()
        {
            if (!battleReady || returnRequested) return;
            if (!IsReturnPending)
            {
                var boss = CurrentStage?.boss;
                var health = player != null ? player.GetComponent<BattlePlayerHealth>() : null;
                if ((boss != null && !boss.IsAlive) || (health != null && !health.IsAlive))
                    returnAt = Time.realtimeSinceStartupAsDouble + ReturnDelay;
                return;
            }
            if (Time.realtimeSinceStartupAsDouble < returnAt) return;
            // Latch before loading so simultaneous deaths cannot request two scene loads.
            returnRequested = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            var finalBoss = CurrentStage?.boss;
            var finalHealth = player != null ? player.GetComponent<BattlePlayerHealth>() : null;
            bool won = finalBoss != null && !finalBoss.IsAlive && (finalHealth == null || finalHealth.IsAlive);
            if (finishFinalBattle(won)) return;
            if (!Application.CanStreamedLevelBeLoaded(ReturnScene))
            {
                Debug.LogError("DecisionScene is not available in the active build scene list.", this);
                return;
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            var loading = SceneManager.LoadSceneAsync(ReturnScene, LoadSceneMode.Single);
            if (loading != null) loading.completed += _ =>
            {
                // The battle intro/camera can restore a locked cursor during scene teardown.
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            };
        }

        public bool DefeatCurrentBoss()
        {
            var boss = CurrentStage?.boss;
            if (!Application.isPlaying || !isActiveAndEnabled || !battleReady || returnRequested ||
                boss == null || !boss.IsAlive) return false;
            boss.TakeDamage(boss.Health);
            if (!IsReturnPending) returnAt = Time.realtimeSinceStartupAsDouble + ReturnDelay;
            return true;
        }

        void Start()
        {
            StartData = Battle3DEntry.TryTake(out var data)
                ? data : Battle3DStartData.CreateDefault().WithStage(selectedStageIndex);
            if (!SelectStage(StartData.StageIndex))
            {
                Debug.LogError($"No valid 3D battle configuration for B{StartData.StageIndex}.", this);
                if (player != null) player.enabled = false;
            }
        }

        public BattleStage FindStage(int index)
        {
            BattleStage result = null;
            foreach (var stage in stages)
            {
                if (stage == null || stage.StageIndex != index) continue;
                if (result != null) return null; // Ambiguous IDs must not silently select the wrong fight.
                result = stage;
            }
            return result;
        }

        public bool SelectStage(int index)
        {
            if (returnRequested) return false;
            var next = FindStage(index);
            if (next == null || next.boss == null || next.patternRunner == null ||
                next.playerSpawn == null || next.bossSpawn == null || player == null) return false;

            returnAt = -1d;
            battleReady = true;

            foreach (var stage in stages)
            {
                if (stage == null) continue;
                if (Application.isPlaying) stage.patternRunner?.StopPattern();
                stage.gameObject.SetActive(stage == next);
            }
            selectedStageIndex = index;
            player.boss = next.boss;
            if (feedback != null) feedback.boss = next.boss;
            if (Application.isPlaying)
            {
                StartData = (StartData ?? Battle3DStartData.CreateDefault()).WithStage(index);
                next.boss.maxHealth = next.settings.bossHealth;
                next.boss.ResetAt(next.bossSpawn);
                player.enabled = true;
                player.ResetForBattle(next.playerSpawn);
                next.patternRunner.SetTarget(player.GetComponent<BattlePlayerHealth>());
                feedback?.ClearEffects();
            }
            else player.transform.SetPositionAndRotation(next.playerSpawn.position, next.playerSpawn.rotation);
            followCamera?.Snap();
            StageSelected?.Invoke(next);
            if (Application.isPlaying)
            {
                var intro = GetComponent<BattleIntroWalkDebug>();
                if (intro != null && intro.autoPlayOnStageStart) intro.Begin();
            }
            return true;
        }

        public bool RunPattern(int patternIndex)
        {
            var stage = CurrentStage;
            if (!Application.isPlaying || stage == null || stage.settings == null ||
                patternIndex < 0 || patternIndex >= stage.settings.patterns.Length) return false;
            return stage.patternRunner.Run(stage.settings.patterns[patternIndex]);
        }

        public void StopPattern() => CurrentStage?.patternRunner?.StopPattern();
        void OnDisable()
        {
            returnAt = -1d;
            if (Application.isPlaying) StopPattern();
        }
    }
}
