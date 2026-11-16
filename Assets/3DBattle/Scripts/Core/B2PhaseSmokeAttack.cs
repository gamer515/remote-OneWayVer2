using System.Collections.Generic;
using UnityEngine;

namespace OneWay.Battle3D
{
    [DisallowMultipleComponent, DefaultExecutionOrder(30)]
    public sealed class B2PhaseSmokeAttack : MonoBehaviour
    {
        public enum Phase { Inactive, Retreat, Volley, ClearingSmoke, CenterSmoke, RunOut, Finished }
        public enum SlashStyle { Vertical, Horizontal, Diagonal }
        public B2CombatController combat;
        public B2SwordSlash swordEffect;
        public BattleArenaBoundary boundary;
        public Transform arenaCenter;
        public Transform[] emissionPoints = new Transform[0];
        public B2PhaseSmokeCloud smokePrefab;
        public GameObject slashPrefab;
        public BossWeaponAttackPattern damageReference;
        [Header("Retreat into smoke")]
        [Min(0)] public float retreatWait = 1.1f;
        [Min(.1f)] public float retreatSeconds = 2.666667f;
        [Min(0)] public float retreatDistance = 6;
        [Min(0)] public float vanishSeconds = .35f;
        [Header("Eight random shots (repeats allowed)")]
        [Min(1)] public int shotCount = 8;
        [Min(0)] public float smokeSpreadSeconds = 3.6f;
        [Min(.05f)] public float shotInterval = 1;
        [Min(0)] public float launchDelay = .2f;
        [Min(.1f)] public float slashSpeed = 40;
        [Min(.1f)] public float flightSeconds = 2.7f;
        [Min(.1f)] public float slashScale = 13.6f;
        public float slashHeight = 3.4f;
        public Vector3 hitLocalCenter, hitLocalSize = new Vector3(.15f, 1, 1);
        [Header("Slash angles / 참격 방향과 높이")]
        [Tooltip("세로/가로/대각선을 세 발마다 무작위 순서로 섞습니다. 발사 위치는 기존처럼 독립적으로 선택합니다.")]
        public bool useAngledSlashes = true;
        [Tooltip("가로 참격의 발사 지점 기준 높이. 씬 미리보기에서 가져온 값입니다.")]
        public float horizontalSlashHeight = 3.4f;
        public Vector3 horizontalSlashEuler = new Vector3(0, 20, 90);
        [Tooltip("대각선 참격의 발사 지점 기준 높이. 씬 미리보기에서 가져온 값입니다.")]
        public float diagonalSlashHeight = 6f;
        public Vector3 diagonalSlashEuler = new Vector3(0, 20, 45);
        [Header("Return from the arena centre")]
        [Min(.01f)] public float smokeFadeSeconds = .6f;
        [Min(.01f)] public float appearSeconds = .35f;
        [Min(.1f)] public float runOutSeconds = 3;
        public Phase CurrentPhase { get; private set; }
        public bool IsRunning => CurrentPhase != Phase.Inactive && CurrentPhase != Phase.Finished;
        public bool IsInvulnerable => IsRunning && (CurrentPhase != Phase.RunOut || phaseTime < appearSeconds);
        public int ShotsEmitted { get; private set; }
        public int Hits { get; private set; }
        public int Parries { get; private set; }
        public bool Completed { get; private set; }
        public int ActiveProjectileCount => projectiles.Count;
        public float Damage => damageReference ? damageReference.damage : 20;
        public readonly List<int> ShotLocations = new List<int>();
        public readonly List<SlashStyle> ShotStyles = new List<SlashStyle>();
        readonly List<SlashStyle> remainingStyles = new List<SlashStyle>(3);
        readonly List<B2PhaseSmokeCloud> clouds = new List<B2PhaseSmokeCloud>();
        readonly List<B2PhaseSlashProjectile> projectiles = new List<B2PhaseSlashProjectile>();
        BattleBoss boss;
        BattlePlayerHealth target;
        Collider[] colliders;
        bool[] collisionEnabled;
        Renderer[] renderers;
        bool[] rendererEnabled;
        Material[][] originals, transparent;
        bool savedSwordEnabled, captured, collisionsRestored, runSettled;
        Vector3 retreatFrom, retreatTo;
        float phaseTime, nextShotTime;
        uint sequence;
        int contactSequence;

        public bool Begin(BattlePlayerHealth player)
        {
            if (IsRunning || !player || !player.IsAlive || !smokePrefab || !slashPrefab || emissionPoints.Length != 8 || !arenaCenter) return false;
            foreach (var point in emissionPoints) if (!point) return false;
            Cancel(); boss = GetComponent<BattleBoss>(); target = player;
            if (!combat) combat = GetComponent<B2CombatController>();
            colliders = GetComponentsInChildren<Collider>(); collisionEnabled = new bool[colliders.Length];
            for (int i = 0; i < colliders.Length; i++) { collisionEnabled[i] = colliders[i].enabled; colliders[i].enabled = false; }
            renderers = combat.animator.GetComponentsInChildren<Renderer>(); rendererEnabled = new bool[renderers.Length];
            originals = new Material[renderers.Length][]; transparent = new Material[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++) { rendererEnabled[i] = renderers[i].enabled; originals[i] = renderers[i].sharedMaterials; }
            savedSwordEnabled = swordEffect && swordEffect.enabled; if (swordEffect) swordEffect.enabled = false;
            captured = true; collisionsRestored = runSettled = false; sequence++; contactSequence = 0;
            ShotsEmitted = Hits = Parries = 0; ShotLocations.Clear(); ShotStyles.Clear(); remainingStyles.Clear(); Completed = false;
            retreatFrom = transform.position;
            float radius = GetComponent<BattleArenaBody>()?.radius ?? 3.4f;
            retreatTo = boundary ? boundary.Constrain(retreatFrom - transform.forward * retreatDistance, radius) : retreatFrom - transform.forward * retreatDistance;
            retreatTo.y = retreatFrom.y;
            SpawnCloud(retreatTo, transform.rotation);
            combat.animator.Play("Base Layer.Idle", 0, 0);
            Change(Phase.Retreat); return true;
        }
        void Update() => Tick(Time.deltaTime);
        public void Tick(float delta)
        {
            if (!IsRunning) return;
            if (!boss || !boss.IsAlive || !target || !target.IsAlive || !combat || !combat.isActiveAndEnabled ||
                combat.battle == null || combat.battle.CurrentStage == null || combat.battle.CurrentStage.boss != boss)
            { Cancel(); return; }
            delta = Mathf.Max(0, delta); float previous = phaseTime; phaseTime += delta;
            for (int i = projectiles.Count - 1; i >= 0; i--)
            {
                var shot = projectiles[i]; if (shot) shot.Advance(delta);
                if (!shot || shot.Complete) { if (shot) Destroy(shot.gameObject); projectiles.RemoveAt(i); }
            }
            float smokeAlpha = CurrentPhase == Phase.ClearingSmoke ? 1 - Mathf.Clamp01(phaseTime / smokeFadeSeconds) : 1;
            if (CurrentPhase == Phase.RunOut) smokeAlpha = 1 - Mathf.Clamp01((phaseTime - .6f) / 1.2f);
            foreach (var cloud in clouds) if (cloud) cloud.Advance(delta, smokeAlpha);
            switch (CurrentPhase)
            {
                case Phase.Retreat:
                    if (previous < retreatWait && phaseTime >= retreatWait) combat.animator.Play("Base Layer.PhaseRetreat", 0, 0);
                    transform.position = Vector3.Lerp(retreatFrom, retreatTo, Mathf.Clamp01((phaseTime - retreatWait) / retreatSeconds));
                    float end = retreatWait + retreatSeconds;
                    SetOpacity(1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(end - vanishSeconds, end, phaseTime)));
                    if (phaseTime >= end)
                    {
                        SetOpacity(0); ClearClouds();
                        foreach (var point in emissionPoints) SpawnCloud(point.position, point.rotation);
                        nextShotTime = smokeSpreadSeconds; Change(Phase.Volley);
                    }
                    break;
                case Phase.Volley:
                    // The first movement starts at 3.6 seconds; materialise 0.2 seconds earlier.
                    while (ShotsEmitted < shotCount && phaseTime >= nextShotTime - launchDelay)
                    { EmitShot(); nextShotTime += shotInterval; }
                    if (ShotsEmitted >= shotCount && projectiles.Count == 0) Change(Phase.ClearingSmoke);
                    break;
                case Phase.ClearingSmoke:
                    if (phaseTime >= smokeFadeSeconds)
                    {
                        ClearClouds(); transform.position = arenaCenter.position;
                        FacePlayer(); SpawnCloud(arenaCenter.position, transform.rotation);
                        combat.animator.Play("Base Layer.Run", 0, 0); Change(Phase.CenterSmoke);
                    }
                    break;
                case Phase.CenterSmoke:
                    if (phaseTime >= smokeSpreadSeconds)
                    {
                        FacePlayer(); SeparateFromPlayer(); RestoreCollisions();
                        combat.animator.Play("Base Layer.Run", 0, 0); Change(Phase.RunOut);
                    }
                    break;
                case Phase.RunOut:
                    SetOpacity(Mathf.SmoothStep(0, 1, phaseTime / appearSeconds));
                    FacePlayer();
                    if (!collisionsRestored && phaseTime >= appearSeconds) { SeparateFromPlayer(); RestoreCollisions(); }
                    float gap = collisionsRestored ? boss.PlanarDistance(target.transform.position) : PlanarGap();
                    if (!runSettled && gap > combat.attackRange && phaseTime < runOutSeconds)
                        combat.movement.MoveWorldDelta(transform.forward * Mathf.Min(combat.runSpeed * delta, gap - combat.attackRange));
                    else if (!runSettled && phaseTime >= appearSeconds)
                    {
                        runSettled = true;
                        combat.animator.CrossFadeInFixedTime("Base Layer.Idle", .15f);
                    }
                    // Even a player standing near the centre gets a complete emergence/fade.
                    if (runSettled && phaseTime >= Mathf.Max(appearSeconds, 1.8f)) Finish();
                    break;
            }
        }
        void EmitShot()
        {
            int location = Random.Range(0, emissionPoints.Length);
            SlashStyle style = NextSlashStyle();
            Vector3 origin = emissionPoints[location].position + Vector3.up * GetSlashHeight(style);
            var go = Instantiate(slashPrefab, origin, Quaternion.identity); go.name = "B2 Phase Slash " + (ShotsEmitted + 1);
            go.transform.localScale = Vector3.one * slashScale;
            var layout = go.GetComponent<B2SlashTrailLayout>();
            if (layout && swordEffect) layout.ApplyLengths(swordEffect.topTrailLength, swordEffect.upperMiddleTrailLength, swordEffect.centerTrailLength, swordEffect.lowerMiddleTrailLength, swordEffect.bottomTrailLength);
            var shot = go.AddComponent<B2PhaseSlashProjectile>(); shot.Initialize(this, target, ShotsEmitted, style);
            projectiles.Add(shot); ShotLocations.Add(location); ShotStyles.Add(style); ShotsEmitted++;
        }

        SlashStyle NextSlashStyle()
        {
            if (!useAngledSlashes) return SlashStyle.Vertical;
            // A shuffled group guarantees variety in an eight-shot phase, without changing
            // the independent random choice of smoke point (including repeated points).
            if (remainingStyles.Count == 0)
            {
                remainingStyles.Add(SlashStyle.Vertical);
                remainingStyles.Add(SlashStyle.Horizontal);
                remainingStyles.Add(SlashStyle.Diagonal);
            }
            int choice = Random.Range(0, remainingStyles.Count);
            SlashStyle style = remainingStyles[choice];
            remainingStyles.RemoveAt(choice);
            return style;
        }

        public float GetSlashHeight(SlashStyle style) => style == SlashStyle.Horizontal ? horizontalSlashHeight
            : style == SlashStyle.Diagonal ? diagonalSlashHeight : slashHeight;

        // Stored values, not EditorOnly preview references: the same settings survive a build.
        public Quaternion GetSlashRotation(SlashStyle style) => Quaternion.Euler(style == SlashStyle.Horizontal
            ? horizontalSlashEuler : style == SlashStyle.Diagonal ? diagonalSlashEuler : new Vector3(20, 0, 0));
        public bool ResolveContact(BattlePlayerHealth player)
        {
            if (!IsRunning || !player || !player.IsAlive) return false;
            var parry = player.GetComponent<BattleParryEffect>();
            if (parry && parry.TryParry(Damage)) { Parries++; return true; }
            // Contacts can arrive out of launch order. Each projectile supplies one contact;
            // use contact order to retain independent strikes without bypassing dodge/guard.
            if (player.TakeAttackDamage(Damage, this, sequence, ++contactSequence, true)) Hits++;
            return false;
        }
        float PlanarGap()
        {
            Vector3 delta = target.transform.position - transform.position; delta.y = 0;
            return Mathf.Max(0, delta.magnitude - (GetComponent<BattleArenaBody>()?.radius ?? 3.4f));
        }
        void SeparateFromPlayer()
        {
            var motor = target.GetComponent<CharacterController>();
            float needed = (GetComponent<BattleArenaBody>()?.radius ?? 3.4f) + (motor ? motor.radius : .5f) + .1f;
            Vector3 away = Vector3.ProjectOnPlane(transform.position - target.transform.position, Vector3.up);
            if (away.magnitude >= needed) return;
            float gap = away.magnitude; if (gap < .001f) away = -transform.forward;
            Vector3 destination = transform.position + away.normalized * (needed - gap);
            transform.position = boundary ? boundary.Constrain(destination, GetComponent<BattleArenaBody>()?.radius ?? 3.4f) : destination;
        }
        void FacePlayer()
        {
            Vector3 facing = Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up);
            if (facing.sqrMagnitude > .001f) transform.rotation = Quaternion.LookRotation(facing);
        }
        void SpawnCloud(Vector3 position, Quaternion rotation)
        {
            var cloud = Instantiate(smokePrefab, position, rotation); cloud.name = "B2 Phase Gold Smoke"; clouds.Add(cloud);
        }
        void Change(Phase next) { CurrentPhase = next; phaseTime = 0; }
        void ClearClouds() { foreach (var cloud in clouds) if (cloud) Destroy(cloud.gameObject); clouds.Clear(); }
        void RestoreCollisions()
        {
            if (!captured) return;
            for (int i = 0; i < colliders.Length; i++) if (colliders[i]) colliders[i].enabled = collisionEnabled[i];
            collisionsRestored = true;
        }
        void SetOpacity(float alpha)
        {
            if (!captured) return;
            alpha = Mathf.Clamp01(alpha);
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i]; if (!renderer) continue;
                renderer.enabled = rendererEnabled[i] && alpha > 0;
                if (alpha <= 0 || alpha >= 1) { renderer.sharedMaterials = originals[i]; continue; }
                if (transparent[i] == null)
                {
                    transparent[i] = new Material[originals[i].Length];
                    for (int j = 0; j < originals[i].Length; j++)
                    {
                        var source = originals[i][j]; if (!source) continue;
                        var mat = new Material(source) { hideFlags = HideFlags.HideAndDontSave };
                        mat.SetFloat("_Surface", 1); mat.SetFloat("_ZWrite", 0);
                        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                        mat.SetOverrideTag("RenderType", "Transparent"); mat.renderQueue = 2990; mat.SetShaderPassEnabled("ShadowCaster", false);
                        transparent[i][j] = mat;
                    }
                }
                for (int j = 0; j < transparent[i].Length; j++) if (transparent[i][j])
                    foreach (var property in new[] { "_BaseColor", "_Color" }) if (transparent[i][j].HasProperty(property))
                    { var color = originals[i][j].GetColor(property); color.a *= alpha; transparent[i][j].SetColor(property, color); }
                renderer.sharedMaterials = transparent[i];
            }
        }
        void Release()
        {
            ClearClouds(); foreach (var shot in projectiles) if (shot) Destroy(shot.gameObject); projectiles.Clear();
            if (!captured) return;
            SetOpacity(1); RestoreCollisions();
            if (swordEffect) swordEffect.enabled = savedSwordEnabled;
            foreach (var materials in transparent) if (materials != null) foreach (var material in materials) if (material) Destroy(material);
            captured = false; originals = transparent = null;
        }
        void Finish() { Release(); Completed = true; Change(Phase.Finished); }
        public void Cancel() { Release(); Completed = false; Change(Phase.Inactive); }
        void OnDisable() => Cancel();
        void OnDestroy() => Cancel();
    }
}
