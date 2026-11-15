using System;
using UnityEngine;

namespace OneWay.Battle3D
{
    // Opt-in: only the debug player owns this component. Shared clips remain untouched.
    [DisallowMultipleComponent, DefaultExecutionOrder(-200)]
    public sealed class DebugPlayerCombatFeel : MonoBehaviour
    {
        [Serializable] public class Segment
        {
            [Tooltip("Original 30 fps preview frame. Zero means the end of the clip.")]
            public float endFrame;
            [Min(.01f)] public float seconds;
            public Segment(float end, float duration) { endFrame = end; seconds = duration; }
        }
        [Serializable] public class AttackTiming
        {
            public string label;
            public Segment[] segments;
            [Min(0)] public float stepDistance;
        }

        [Header("Attack timing (seconds at Playback Speed 1.6)")]
        public AttackTiming[] attacks = Defaults();
        [Min(.01f)] public float linkSeconds = .065f;
        [Range(.05f, .3f)] public float inputBufferSeconds = .15f;
        [Header("Jump (original preview frames)")]
        public float jumpTakeoffFrame = 10f;
        public float jumpPeakFrame = 17f;
        public float jumpLandingFrame = 19.692308f;
        [Min(.1f)] public float jumpHeightScale = 1f;
        [Min(1)] public float cancelledJumpGravity = 55f;
        [Header("Camera")]
        [Range(0, 1)] public float jumpCameraFollow = .3f;
        [Min(.01f)] public float cameraRiseSmooth = .12f;
        [Min(.01f)] public float cameraFallSmooth = .18f;
        [Range(0, .2f)] public float lightShake = .025f;
        [Range(0, .3f)] public float heavyShake = .075f;
        [Min(.01f)] public float shakeSeconds = .12f;
        [Header("Impact pause (unscaled seconds, both combatants)")]
        public bool hitStop = true;
        [Range(0, .12f)] public float lightHitStop = .035f;
        [Range(0, .15f)] public float heavyHitStop = .065f;
        [Header("Sword effects")]
        [Range(0, .2f)] public float slashTailSeconds = .045f;
        [Range(.2f, 2)] public float lightSlashScale = .9f;
        [Range(.2f, 2)] public float heavySlashScale = 1.15f;
        [Header("Sound (optional clip overrides)")]
        public AudioClip swingSound, hitSound, heavyHitSound;
        public bool generatedSoundFallback = true;
        [Range(0, 1)] public float soundVolume = .22f;
        public bool IsHitStopped => ownsPause;
        public int ConfirmedHits { get; private set; }
        public int Landings { get; private set; }

        NewPlayerAttackCombo combo;
        BattlePlayerController player;
        AudioSource audioSource;
        AudioClip generatedSwing, generatedHit, generatedHeavy;
        bool ownsPause, cameraInitialized;
        float pauseUntil, resumeScale, shakeUntil, shakeStrength, pivotY, pivotVelocity;

        public static AttackTiming[] Defaults() => new[] {
            Timing("01 - Retreat / rush finisher", 0, new Segment(13,.18f), new Segment(27,.11f), new Segment(30,.08f), new Segment(33,.07f), new Segment(86,.16f), new Segment(0,.22f)),
            Timing("02 - Upward slash", .65f, new Segment(13,.14f), new Segment(21,.105f), new Segment(0,.24f)),
            Timing("03 - Double slash", .8f, new Segment(17,.16f), new Segment(25,.1f), new Segment(33,.105f), new Segment(41,.1f), new Segment(0,.24f)),
            Timing("04 - Side slash", .55f, new Segment(6,.095f), new Segment(14,.105f), new Segment(0,.2f)),
            Timing("05 - Skill 03", 0, new Segment(6,.085f), new Segment(10,.06f), new Segment(16,.075f), new Segment(0,.22f)),
            Timing("06 - Jump slam", .8f, new Segment(10,.12f), new Segment(16.5f,.19f), new Segment(17,.05f), new Segment(19.692308f,.1f), new Segment(27,.11f), new Segment(0,.26f))
        };
        static AttackTiming Timing(string label, float step, params Segment[] segments)
            => new AttackTiming { label = label, stepDistance = step, segments = segments };

        void Awake()
        {
            combo = GetComponent<NewPlayerAttackCombo>();
            player = GetComponent<BattlePlayerController>();
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0;
            if (generatedSoundFallback)
            {
                generatedSwing = MakeSound("Debug sword swish", .12f, 0);
                generatedHit = MakeSound("Debug sword impact", .09f, 1);
                generatedHeavy = MakeSound("Debug sword heavy impact", .16f, 2);
            }
        }
        void Update()
        {
            if (ownsPause && (Time.unscaledTime >= pauseUntil || player == null || player.IsDefeated)) ReleasePause();
            // A scene transition or another system that resumes time takes ownership away.
            if (ownsPause && Time.timeScale != 0) ownsPause = false;
            // Defeat/scene-exit disables the controller's Tick, but an interrupted jump must still settle.
            if (combo != null && combo.IsRecoveringFromJump && player != null && (!player.enabled || player.IsDefeated))
                combo.Tick(Time.deltaTime);
        }

        public float Rate(int attack, float frame, float frames, bool preparation, out float boundary)
        {
            boundary = frames;
            if (preparation) return frames / (48f * Mathf.Max(.01f, linkSeconds));
            if (attacks == null || attack < 0 || attack >= attacks.Length || attacks[attack] == null || attacks[attack].segments == null) return 1;
            float start = 0;
            foreach (var s in attacks[attack].segments)
            {
                if (s == null) continue;
                float end = s.endFrame <= 0 ? frames : Mathf.Min(frames, s.endFrame);
                if (end <= start) continue;
                if (frame < end - .00001f)
                {
                    boundary = end;
                    return Mathf.Max(.01f, (end - start) / (48f * Mathf.Max(.01f, s.seconds)));
                }
                start = end;
            }
            return 1;
        }
        public float Duration(int attack, float from, float to, float frames, float speed, bool preparation = false)
        {
            float seconds = 0;
            for (int i = 0; i < 128 && from < to - .00001f; i++)
            {
                float rate = Rate(attack, from, frames, preparation, out float end);
                end = Mathf.Min(to, end);
                if (end <= from) break;
                seconds += (end - from) / (30f * Mathf.Max(.1f, speed) * rate);
                from = end;
            }
            return seconds;
        }
        public static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3 - 2 * t); }
        public float Step(int attack, float frame, NewPlayerAttackSet.Motion motion)
        {
            if (attacks == null || attack < 0 || attack >= attacks.Length || attacks[attack] == null) return 0;
            float distance = attacks[attack].stepDistance;
            if (attack == 5) return distance * Ease(Mathf.InverseLerp(jumpTakeoffFrame, jumpLandingFrame, frame));
            if (motion.damageWindows == null || motion.damageWindows.Length == 0) return 0;
            float progress = 0;
            foreach (var w in motion.damageWindows)
                progress += Ease(Mathf.InverseLerp(Mathf.Max(0, w.x - 4), Mathf.Max(w.x, w.y - 1), frame));
            return distance * progress / motion.damageWindows.Length;
        }
        public float Jump(float frame, NewPlayerAttackSet.Motion motion, float multiplier)
        {
            float peak = Mathf.Max(0, motion.jump.Evaluate(jumpPeakFrame)) * multiplier * jumpHeightScale;
            if (frame <= jumpTakeoffFrame || frame >= jumpLandingFrame) return 0;
            if (frame <= jumpPeakFrame)
                return peak * Mathf.Sin(Mathf.InverseLerp(jumpTakeoffFrame, jumpPeakFrame, frame) * Mathf.PI * .5f);
            float t = Mathf.InverseLerp(jumpPeakFrame, jumpLandingFrame, frame);
            return peak * (1 - t * t);
        }
        public Vector3 CameraPivot(Vector3 authored, float delta)
        {
            float height = combo != null ? combo.AirborneHeight : 0;
            float wanted = authored.y - height * (1 - jumpCameraFollow);
            if (!cameraInitialized) { pivotY = wanted; pivotVelocity = 0; cameraInitialized = true; }
            if (delta > 0)
                pivotY = Mathf.SmoothDamp(pivotY, wanted, ref pivotVelocity,
                    wanted > pivotY ? cameraRiseSmooth : cameraFallSmooth, Mathf.Infinity, delta);
            authored.y = pivotY;
            return authored;
        }
        public Vector3 CameraShake()
        {
            float t = Mathf.Clamp01((shakeUntil - Time.unscaledTime) / Mathf.Max(.01f, shakeSeconds));
            return new Vector3(Mathf.Sin(Time.unscaledTime * 141), Mathf.Sin(Time.unscaledTime * 173), 0) * (shakeStrength * t * t);
        }
        void Shake(bool heavy)
        {
            float strength = heavy ? heavyShake : lightShake;
            shakeStrength = Time.unscaledTime < shakeUntil ? Mathf.Max(shakeStrength, strength) : strength;
            shakeUntil = Time.unscaledTime + shakeSeconds;
        }
        public void OnSwing(int attack)
        {
            Play(swingSound != null ? swingSound : generatedSwing, .65f);
        }
        public void OnHit(int attack)
        {
            ConfirmedHits++;
            bool heavy = attack == 0 || attack == 5;
            Shake(heavy);
            Play(heavy ? (heavyHitSound != null ? heavyHitSound : generatedHeavy) : (hitSound != null ? hitSound : generatedHit), 1);
            float duration = heavy ? heavyHitStop : lightHitStop;
            if (!hitStop || duration <= 0 || player == null || player.IsDefeated) return;
            if (ownsPause) { pauseUntil = Mathf.Max(pauseUntil, Time.unscaledTime + duration); return; }
            if (Time.timeScale <= 0) return;
            resumeScale = Time.timeScale;
            ownsPause = true;
            pauseUntil = Time.unscaledTime + duration;
            Time.timeScale = 0;
        }
        public void OnLanding()
        {
            Landings++;
            Shake(true);
        }
        void ReleasePause()
        {
            if (ownsPause && Time.timeScale == 0) Time.timeScale = resumeScale;
            ownsPause = false;
        }
        void Play(AudioClip clip, float volume)
        {
            if (audioSource != null && clip != null && soundVolume > 0) audioSource.PlayOneShot(clip, soundVolume * volume);
        }
        static AudioClip MakeSound(string name, float duration, int kind)
        {
            const int rate = 22050;
            var data = new float[Mathf.CeilToInt(rate * duration)];
            uint seed = 7193;
            float filtered = 0;
            for (int i = 0; i < data.Length; i++)
            {
                seed = seed * 1664525u + 1013904223u;
                float noise = ((seed >> 8) / 8388607.5f) - 1;
                filtered = Mathf.Lerp(filtered, noise, kind == 0 ? .18f : .42f);
                float t = (float)i / rate, u = (float)i / data.Length;
                float envelope = kind == 0 ? Mathf.Sin(u * Mathf.PI) * (1 - u) : Mathf.Min(1, t / .002f) * Mathf.Exp(-u * 7);
                float thump = kind == 0 ? 0 : Mathf.Sin(2 * Mathf.PI * (kind == 2 ? 85 : 150) * t) * .4f;
                data[i] = Mathf.Clamp((filtered * .8f + thump) * envelope, -1, 1);
            }
            var clip = AudioClip.Create(name, data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
        public void ResetFeedback() { ReleasePause(); cameraInitialized = false; shakeUntil = 0; }
        void OnDisable() => ResetFeedback();
        void OnDestroy()
        {
            ReleasePause();
            if (generatedSwing != null) Destroy(generatedSwing);
            if (generatedHit != null) Destroy(generatedHit);
            if (generatedHeavy != null) Destroy(generatedHeavy);
        }
    }
}
