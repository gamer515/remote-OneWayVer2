using UnityEngine;

namespace OneWay.Battle3D
{
    // Distance preview only: step markers are not animation foot-contact events.
    public sealed class BattleIntroWalkDebug : MonoBehaviour
    {
        public Battle3DSceneController battle;
        [Min(1)] public int stepCount = 5;
        [Min(0.01f)] public float distancePerStep = 0.5f;
        [Min(0.01f)] public float moveSpeed = 1f;
        public bool freezeCamera = true;
        public bool autoPlayOnStageStart = true;
        public bool cinematicPresentation = true;
        public string bossIntroState = "Base Layer.Skill_01_Boss1";
        [Tooltip("Existing locomotion state; replace with a walk state when available.")]
        public string movementState = "Base Layer.Run";
        public string idleState = "Base Layer.Idle";
        public float TotalDistance => Mathf.Max(1, stepCount) * Mathf.Max(.01f, distancePerStep);
        public bool IsRunning { get; private set; }
        public float Travelled { get; private set; }
        public string Status { get; private set; } = "Ready";
        public event System.Action Started;
        public event System.Action<bool> Finished;
        public event System.Action ReturnedToStart;
        BattleStage stage;
        BattlePlayerController player;
        BattleFollowCamera cameraFollow;
        CharacterController motor;
        bool playerEnabled, cameraEnabled, rootMotion, cursorVisible;
        CursorLockMode cursorMode;
        Vector3 start, direction;
        float targetDistance, speed;
        BossApproachController heldApproach;
        BattleBoss heldBoss;
        bool approachEnabled, patrolEnabled;
        bool bossAnimationHeld, previousAnimationControl, walkFinished;
        int bossIntroHash;
        float blackRemaining;
        bool presentationStarted, bossIntroStarted;
        Vector3 cameraEnd, cameraBegin;
        Quaternion cameraEndRotation, cameraBeginRotation;

        public bool GetRoute(out Vector3 origin, out Vector3 forward)
        {
            origin = Vector3.zero; forward = Vector3.forward;
            var current = battle != null ? battle.CurrentStage : null;
            if (current == null || current.playerSpawn == null || current.bossSpawn == null) return false;
            origin = current.playerSpawn.position;
            forward = current.bossSpawn.position - origin;
            forward.y = 0f;
            if (forward.sqrMagnitude < .0001f) return false;
            forward.Normalize();
            return true;
        }

        public void Begin()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || battle == null || battle.player == null) return;
            Stop();
            if (!GetRoute(out start, out direction)) { Status = "Missing or overlapping spawn markers"; return; }
            stage = battle.CurrentStage;
            player = battle.player;
            motor = player.GetComponent<CharacterController>();
            if (motor == null || !motor.enabled) { Status = "CharacterController must be enabled"; return; }
            heldBoss = stage.boss;
            heldApproach = heldBoss != null ? heldBoss.GetComponent<BossApproachController>() : null;
            approachEnabled = heldApproach != null && heldApproach.enabled;
            if (heldApproach != null) heldApproach.enabled = false;
            patrolEnabled = heldBoss != null && heldBoss.patrolEnabled;
            if (heldBoss != null)
            {
                heldBoss.patrolEnabled = false;
                Vector3 facing = start - heldBoss.transform.position;
                facing.y = 0f;
                if (facing.sqrMagnitude > .0001f) heldBoss.transform.rotation = Quaternion.LookRotation(facing);
            }
            bossAnimationHeld = false;
            walkFinished = false;
            blackRemaining = 3f;
            presentationStarted = bossIntroStarted = false;
            if (heldBoss != null && heldBoss.animator != null && !string.IsNullOrEmpty(bossIntroState))
            {
                bossIntroHash = Animator.StringToHash(bossIntroState);
                if (heldBoss.animator.HasState(0, bossIntroHash))
                {
                    previousAnimationControl = heldBoss.PatternControlsAnimation;
                    heldBoss.PatternControlsAnimation = true;
                    bossAnimationHeld = true;
                }
            }
            playerEnabled = player.enabled;
            cameraFollow = battle.followCamera;
            cameraEnabled = cameraFollow != null && cameraFollow.enabled;
            cursorMode = Cursor.lockState; cursorVisible = Cursor.visible;
            player.CancelInput();
            player.enabled = false;
            if (freezeCamera && cameraFollow != null) cameraFollow.enabled = false;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            Teleport(start, Quaternion.LookRotation(direction));
            if (player.animator != null)
            {
                rootMotion = player.animator.applyRootMotion;
                player.animator.applyRootMotion = false;
            }
            targetDistance = TotalDistance; speed = Mathf.Max(.01f, moveSpeed);
            if (cameraFollow != null && freezeCamera && cinematicPresentation)
            {
                var cameraComponent = cameraFollow.GetComponent<Camera>();
                float clearance = Mathf.Max(.45f, motor.radius * Mathf.Max(player.transform.lossyScale.x, player.transform.lossyScale.z)
                    + (cameraComponent != null ? cameraComponent.nearClipPlane : .1f) + .1f);
                cameraBegin = start - direction * clearance + Vector3.up * 1.7f;
                cameraEnd = start - direction * 12f + Vector3.up * 1.7f;
                cameraBeginRotation = cameraEndRotation = Quaternion.LookRotation(direction);
                cameraFollow.transform.SetPositionAndRotation(cameraBegin, cameraBeginRotation);
            }
            Physics.SyncTransforms();
            Travelled = 0f; IsRunning = true; Status = "Black screen";
            PlayState(idleState);
            Started?.Invoke();
        }

        void Update() => Tick(Time.deltaTime);
        public void Tick(float deltaTime)
        {
            if (!IsRunning) return;
            var health = player != null ? player.GetComponent<BattlePlayerHealth>() : null;
            if (battle.CurrentStage != stage || player == null || !motor.enabled ||
                (health != null && !health.IsAlive)) { Finish("Interrupted"); return; }
            deltaTime = Mathf.Max(0f, deltaTime);
            if (!presentationStarted)
            {
                float consumed = Mathf.Min(blackRemaining, deltaTime);
                blackRemaining -= consumed;
                deltaTime -= consumed;
                if (blackRemaining > 0f) return;
                presentationStarted = true;
                Status = "Walking";
                PlayState(movementState);
            }
            if (deltaTime < .00001f) return;
            float remaining = targetDistance - Travelled;
            Vector3 before = player.transform.position;
            var flags = motor.Move(direction * Mathf.Min(remaining, speed * Mathf.Max(0f, deltaTime)));
            player.transform.rotation = Quaternion.LookRotation(direction);
            Travelled = Mathf.Max(0f, Vector3.Dot(player.transform.position - start, direction));
            if (cameraFollow != null && freezeCamera && cinematicPresentation)
            {
                float blend = Mathf.SmoothStep(0, 1, Travelled / targetDistance);
                cameraFollow.transform.SetPositionAndRotation(
                    Vector3.Lerp(cameraBegin, cameraEnd, blend),
                    Quaternion.Slerp(cameraBeginRotation, cameraEndRotation, blend));
            }
            if (bossAnimationHeld && !bossIntroStarted &&
                (cameraFollow == null || Vector3.ProjectOnPlane(cameraFollow.transform.position - player.transform.position, Vector3.up).magnitude > 1f))
            {
                bossIntroStarted = true;
                heldBoss.animator.Play(bossIntroHash, 0, 0f);
                heldBoss.animator.Update(0f);
            }
            if (Travelled >= targetDistance - .001f)
            {
                if (!walkFinished) { walkFinished = true; PlayState(idleState); }
                var bossAnimator = heldBoss != null ? heldBoss.animator : null;
                var bossState = bossAnimator != null ? bossAnimator.GetCurrentAnimatorStateInfo(0) : default;
                if (!bossAnimationHeld || (bossIntroStarted && bossState.fullPathHash == bossIntroHash && bossState.normalizedTime >= 1f))
                    Finish("Completed");
                else Status = "Waiting for boss intro";
            }
            else if ((flags & CollisionFlags.Sides) != 0 ||
                (deltaTime > 0f && (player.transform.position - before).sqrMagnitude < .00000001f)) Finish("Blocked by collider");
        }

        public void Stop() { if (IsRunning) Finish("Stopped"); }
        void Finish(string status)
        {
            IsRunning = false; Status = status;
            if (heldApproach != null) heldApproach.enabled = approachEnabled;
            if (heldBoss != null) heldBoss.patrolEnabled = patrolEnabled;
            if (bossAnimationHeld && heldBoss != null)
            {
                heldBoss.PatternControlsAnimation = previousAnimationControl;
                heldBoss.RefreshAnimation();
            }
            bossAnimationHeld = false;
            heldApproach = null; heldBoss = null;
            if (player != null)
            {
                PlayState(idleState);
                if (player.animator != null) player.animator.applyRootMotion = rootMotion;
                player.enabled = playerEnabled;
            }
            if (cameraFollow != null)
            {
                if (freezeCamera && cinematicPresentation)
                {
                    cameraFollow.AdoptCurrentPose();
                    cameraFollow.ReturnToGameplayDistance(10f);
                }
                cameraFollow.enabled = cameraEnabled;
            }
            Cursor.lockState = cursorMode; Cursor.visible = cursorVisible;
            Finished?.Invoke(status == "Completed");
        }

        public void ReturnToStart()
        {
            if (!Application.isPlaying || battle == null || battle.player == null) return;
            Stop();
            if (!GetRoute(out start, out direction)) return;
            player = battle.player; motor = player.GetComponent<CharacterController>();
            player.CancelInput();
            Teleport(start, battle.CurrentStage.playerSpawn.rotation);
            PlayState(idleState);
            Travelled = 0f; Status = "Returned to start";
            ReturnedToStart?.Invoke();
        }

        void OnGUI()
        {
            if (!Application.isPlaying || !IsRunning || !cinematicPresentation) return;
            float progress = Mathf.Clamp01(Travelled / Mathf.Max(.01f, targetDistance));
            var oldColor = GUI.color;
            int oldDepth = GUI.depth;
            GUI.depth = -1000;
            GUI.color = Color.black;
            float height = Screen.height * .075f * (1f - Mathf.SmoothStep(0,1,Mathf.InverseLerp(.65f,1,progress)));
            GUI.DrawTexture(new Rect(0,0,Screen.width,height),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0,Screen.height-height,Screen.width,height),Texture2D.whiteTexture);
            GUI.color = new Color(0,0,0,presentationStarted ? 1f-Mathf.Clamp01(progress/.2f) : 1f);
            GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),Texture2D.whiteTexture);
            GUI.color = oldColor; GUI.depth = oldDepth;
        }

        void Teleport(Vector3 position, Quaternion rotation)
        {
            bool enabledBefore = motor.enabled;
            motor.enabled = false;
            player.transform.SetPositionAndRotation(position, rotation);
            motor.enabled = enabledBefore;
        }
        void PlayState(string state)
        {
            var a = player != null ? player.animator : null;
            if (a != null && a.runtimeAnimatorController != null && !string.IsNullOrEmpty(state) && a.HasState(0, Animator.StringToHash(state)))
                a.Play(state, 0, 0f);
        }
        void OnDisable() => Stop();
        void OnDrawGizmosSelected()
        {
            if (!GetRoute(out var origin, out var forward)) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(origin, origin + forward * TotalDistance);
            int count = Mathf.Clamp(stepCount, 1, 1000);
            for (int i = 0; i <= count; i++)
                Gizmos.DrawWireSphere(origin + forward * (TotalDistance * i / count), .1f);
        }
    }
}
