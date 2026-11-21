using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Read-only presentation of an already committed learning reward.</summary>
public sealed partial class LearningCoinRewardView : MaskableGraphic
{
    internal static readonly Color32[] CoinColors =
    {
        new Color32(249, 98, 88, 255), new Color32(97, 170, 255, 255),
        new Color32(92, 229, 210, 255), new Color32(255, 213, 104, 255)
    };
    private static readonly Color[] Enamels =
    {
        new Color32(185, 21, 24, 255), new Color32(19, 85, 224, 255),
        new Color32(16, 150, 145, 255), new Color32(222, 140, 7, 255)
    };
    private const int RowCapacity = 5;
    private const float GroupDelay = .16f;
    private const float ForceStagger = .03f;
    private const float ForceRecoilDuration = .125f;
    private static readonly (float Reveal, float Drop, float Flight, float Settle)[] Timings =
    {
        (.14f, .42f, .34f, .28f),
        (.23f, .83f, 0f, .20f),
        (.38f, .74f, .70f, .26f),
        (.22f, .58f, .56f, .36f)
    };
    private readonly Queue<Reward> pending = new();
    private Reward active;
    private float elapsed;
    private bool sounded;
    internal bool IsPlaying { get; private set; }

    private sealed class Reward
    {
        internal Func<Vector2> StoryCenter;
        internal Func<float> StoryWidth;
        internal Action<Vector2> Recoil;
        internal Camera Camera;
        internal Collider Board;
        internal CoinDropController Audio;
        internal JourneyCoinSupplyController Supply;
        internal RewardCoin[] Coins;
        internal int Dominant, Tier, Rows;
        internal float Reveal, CharmDelay, End;
    }

    private readonly struct RewardCoin
    {
        internal readonly int Ability, Index, InRow, Row, RowSize, Wave;

        internal RewardCoin(int ability, int index, int inRow, int row, int rowSize)
        {
            Ability = ability;
            Index = index;
            InRow = inRow;
            Row = row;
            RowSize = rowSize;
            Wave = ability == 3 ? index / RowCapacity : 0;
        }
    }

    private readonly struct CoinPose
    {
        internal readonly Vector2 Position;
        internal readonly float Progress, Alpha, Scale, Angle;

        internal CoinPose(Vector2 position, float progress, float alpha, float scale = 1f, float angle = 0f)
        {
            Position = position;
            Progress = progress;
            Alpha = alpha;
            Scale = scale;
            Angle = angle;
        }
    }

    internal static LearningCoinRewardView Create(Scene scene)
    {
        var root = new GameObject("LearningCoinRewardCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        SceneManager.MoveGameObjectToScene(root, scene);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 144;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;
        var effect = new GameObject("LearningCoinEffects", typeof(RectTransform), typeof(CanvasRenderer))
            .AddComponent<LearningCoinRewardView>();
        effect.transform.SetParent(root.transform, false);
        effect.rectTransform.anchorMin = Vector2.zero;
        effect.rectTransform.anchorMax = Vector2.one;
        effect.rectTransform.offsetMin = effect.rectTransform.offsetMax = Vector2.zero;
        effect.raycastTarget = false;
        return effect;
    }

    internal static int GetTier(int count) => count >= 15 ? 2 : count >= 5 ? 1 : 0;

    internal void Enqueue(int[] amounts, Func<Vector2> storyCenter, Func<float> storyWidth, Action<Vector2> recoil,
        Camera camera, Collider board, CoinDropController audio, JourneyCoinSupplyController supply = null)
    {
        var coins = new List<RewardCoin>();
        int rows = 0, dominant = 0;
        for (int ability = 0; ability < amounts.Length; ability++)
        {
            if (amounts[ability] > amounts[dominant]) dominant = ability;
            for (int i = 0; i < amounts[ability]; i++)
            {
                int inRow = i % RowCapacity;
                if (inRow == 0) rows++;
                int rowSize = Mathf.Min(RowCapacity, amounts[ability] - i + inRow);
                coins.Add(new RewardCoin(ability, i, inRow, rows - 1, rowSize));
            }
        }
        if (coins.Count == 0) return;
        float charmDelay = Mathf.Min(GroupDelay, .24f / Mathf.Max(1, (amounts[3] - 1) / RowCapacity));
        var reward = new Reward
        {
            StoryCenter = storyCenter, StoryWidth = storyWidth, Recoil = recoil, Camera = camera, Board = board, Audio = audio,
            Coins = coins.ToArray(), Dominant = dominant, Tier = GetTier(coins.Count), Rows = rows,
            Reveal = Timings[dominant].Reveal, CharmDelay = charmDelay, Supply = supply
        };
        reward.End = coins.Max(coin => LandingTime(reward, coin) + Timings[coin.Ability].Settle);
        pending.Enqueue(reward);
        if (!IsPlaying) StartNext();
    }

    private void StartNext()
    {
        active?.Recoil(Vector2.zero);
        active = pending.Dequeue();
        elapsed = 0f;
        sounded = false;
        IsPlaying = true;
        PrepareCoinImages();
        SetVerticesDirty();
    }

    private void Update()
    {
        if (!IsPlaying) return;
        elapsed = Mathf.Min(elapsed + Time.unscaledDeltaTime, active.End);
        UpdateRecoil();
        if (!sounded && active.Tier > 0 && elapsed >= active.Reveal)
        {
            sounded = true;
            active.Audio?.PlayLearningCoinSound(active.Dominant);
        }
        if (elapsed >= active.End)
        {
            IsPlaying = false;
            if (active.Supply != null)
            {
                Rect board = BoardArea();
                active.Coins = active.Coins.Where(coin =>
                {
                    var layout = CoinLayout(coin, board, Vector2.zero);
                    Vector2 screen = RectTransformUtility.WorldToScreenPoint(null,
                        rectTransform.TransformPoint(layout.Finish));
                    return !active.Supply.LandLearningCoin(coin.Ability, screen);
                }).ToArray();
                HideCoinImages();
            }
            if (pending.Count > 0) StartNext();
        }
        if (IsPlaying) UpdateCoinImages();
        SetVerticesDirty();
    }

    internal void Clear()
    {
        active?.Recoil(Vector2.zero);
        pending.Clear();
        active = null;
        IsPlaying = false;
        HideCoinImages();
        SetVerticesDirty();
    }

    private void UpdateRecoil()
    {
        if (active == null || active.Dominant != 0) return;
        float sinceImpact = elapsed - active.Reveal;
        if (sinceImpact < 0f || sinceImpact >= ForceRecoilDuration)
        {
            active.Recoil(Vector2.zero);
            return;
        }
        float progress = sinceImpact / ForceRecoilDuration;
        float amplitude = (.008f + .0015f * active.Tier) * (1f - progress) * (1f - progress);
        active.Recoil(amplitude * new Vector2(Mathf.Cos(progress * Mathf.PI * 4f),
            .77f * Mathf.Cos(progress * Mathf.PI * 3f)));
    }

    private Vector2 ToCanvas(Vector2 point)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, point, null, out var local);
        return local;
    }

    private Rect BoardArea()
    {
        if (active.Camera == null || active.Board == null)
        {
            Vector2 min = ToCanvas(new Vector2(Screen.width * .08f, Screen.height * .10f));
            Vector2 max = ToCanvas(new Vector2(Screen.width * .42f, Screen.height * .25f));
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
        var bounds = active.Board.bounds;
        Vector2 minPoint = new Vector2(float.MaxValue, float.MaxValue), maxPoint = -minPoint;
        for (int x = 0; x < 2; x++)
        for (int z = 0; z < 2; z++)
        {
            var world = new Vector3(x == 0 ? bounds.min.x : bounds.max.x, bounds.max.y,
                z == 0 ? bounds.min.z : bounds.max.z);
            var point = ToCanvas(active.Camera.WorldToScreenPoint(world));
            minPoint = Vector2.Min(minPoint, point);
            maxPoint = Vector2.Max(maxPoint, point);
        }
        return Rect.MinMaxRect(minPoint.x, minPoint.y, maxPoint.x, maxPoint.y);
    }

    private static float DropTime(Reward reward, RewardCoin coin)
    {
        var timing = Timings[coin.Ability];
        float stagger = coin.Ability == 0 ? coin.Index % 3 * ForceStagger : coin.Wave * reward.CharmDelay;
        return reward.Reveal + timing.Drop - timing.Reveal + stagger;
    }

    private static float LandingTime(Reward reward, RewardCoin coin) => DropTime(reward, coin) + Timings[coin.Ability].Flight;

    private CoinPose GetCoinPose(RewardCoin coin, Vector2 start, Vector2 end, float time)
    {
        float reveal = active.Reveal + coin.Wave * active.CharmDelay;
        float drop = DropTime(active, coin);
        float landing = LandingTime(active, coin);
        float alpha = coin.Ability == 0 ? (time >= reveal ? 1f : 0f) :
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(reveal, reveal + .12f, time));
        if (coin.Ability == 1)
            return time < drop ? new CoinPose(start, 0f, alpha) : new CoinPose(end, 1f, 1f);
        float progress = Mathf.InverseLerp(drop, landing, time);
        float move = coin.Ability == 0 ? progress * progress : Mathf.SmoothStep(0f, 1f, progress);
        var position = Vector2.Lerp(start, end, move);
        if (coin.Ability == 2)
        {
            position.x += Mathf.Sin(progress * Mathf.PI) * 13f * Mathf.Cos(coin.Index * 2f);
            float roll = Mathf.Sin(progress * Tau + coin.Index) * 23f * Mathf.Sin(progress * Mathf.PI);
            return new CoinPose(position, progress, alpha, angle: roll);
        }
        if (coin.Ability != 0) return new CoinPose(position, progress, alpha);

        float pop = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(reveal, reveal + .14f, time));
        float bounce = Mathf.InverseLerp(landing, landing + .12f, time);
        float hop = bounce < 1f ? Mathf.Sin(bounce * Mathf.PI) : 0f;
        position.y += hop * (7f + coin.Index % 3 * 1.6f);
        float rotation = Mathf.Sin(progress * Mathf.PI) * (13f + coin.Index % 3 * 10f) *
            (coin.Index % 2 == 0 ? -1f : 1f);
        return new CoinPose(position, progress, alpha, 1f + .20f * pop, rotation);
    }
}
