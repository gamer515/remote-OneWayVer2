using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public sealed partial class LearningCoinRewardView
{
    private const float Tau = Mathf.PI * 2f;
    private static readonly Color WarmWhite = new Color32(255, 248, 216, 255);
    private static readonly Color Gold = new Color32(255, 194, 59, 255);
    private static readonly Color HotPink = new Color32(255, 93, 191, 255);
    private static readonly Color Violet = new Color32(183, 110, 255, 255);
    private static readonly Color Ice = new Color32(89, 221, 255, 255);
    private readonly RenderTexture[] coinFaceTextures = new RenderTexture[4];
    private readonly float[] coinWorldDiameters = new float[4];
    private readonly List<RawImage> coinImages = new();

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (active == null) return;

        Rect board = BoardArea();
        Vector2 origin = ToCanvas(active.StoryCenter());
        Vector2 landing = board.center + Vector2.left * (board.width * .025f);
        if (IsPlaying)
            DrawSummon(mesh, origin);

        for (int i = 0; i < active.Coins.Length; i++)
        {
            var coin = active.Coins[i];
            var (start, finish, formation, radius) = CoinLayout(coin, board, origin);

            var pose = GetCoinPose(coin, start, finish, elapsed);
            if (pose.Alpha <= 0f) continue;
            if (IsPlaying)
            {
                if (pose.Progress > 0f && pose.Progress < 1f)
                    DrawTrail(mesh, coin, pose.Position, start, finish, radius);
                if (coin.Ability == 0 || coin.InRow == coin.RowSize / 2)
                {
                    bool electric = coin.Ability == 1;
                    var effectCenter = electric ? landing + formation.y * Vector2.up : finish;
                    float landingTime = LandingTime(active, coin);
                    DrawArrival(mesh, effectCenter, landingTime,
                        radius * (electric && active.Dominant == 1 ? 1f : .75f),
                        electric ? Ice : coin.Ability == 3 ? Gold : CoinColors[coin.Ability], coin.Ability);
                }
            }
            if (!IsPlaying || active.Supply == null || i >= coinImages.Count || coinImages[i].texture == null)
                DrawCoin(mesh, coin, pose, radius);
        }
    }

    private void PrepareCoinImages()
    {
        HideCoinImages();
        if (active.Supply == null) return;
        for (int i = 0; i < active.Coins.Length; i++)
        {
            int ability = active.Coins[i].Ability;
            if (coinFaceTextures[ability] == null)
                coinFaceTextures[ability] = RenderCoinFace(ability);
            if (i == coinImages.Count)
            {
                var image = new GameObject("LearningRewardCoin", typeof(RectTransform), typeof(RawImage))
                    .GetComponent<RawImage>();
                image.transform.SetParent(rectTransform, false);
                image.raycastTarget = false;
                coinImages.Add(image);
            }
            coinImages[i].texture = coinFaceTextures[ability];
        }
    }

    private RenderTexture RenderCoinFace(int ability)
    {
        // Use the same isolated camera setup as the roadside coin reveal.
        var stage = new GameObject("LearningCoinRenderStage");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(stage, gameObject.scene);
        stage.transform.position = new Vector3(900f, 200f, 900f);
        var coin = active.Supply.CreateLearningCoinVisual(ability, stage.transform.position, stage.transform);
        if (coin == null)
        {
            Destroy(stage);
            return null;
        }
        Bounds bounds = BoardItemDrop.ItemBounds(coin.transform);
        coinWorldDiameters[ability] = Mathf.Max(bounds.size.x, bounds.size.z);
        var texture = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGB32)
        {
            name = $"LearningCoinFace_{ability}"
        };
        var camera = new GameObject("CoinFaceCamera").AddComponent<Camera>();
        camera.enabled = false;
        camera.transform.SetParent(stage.transform, false);
        if (active.Camera != null)
        {
            var boardView = active.Camera.transform;
            camera.transform.SetPositionAndRotation(bounds.center - boardView.forward * 3f, boardView.rotation);
        }
        else
        {
            camera.transform.position = bounds.center + new Vector3(0f, 2.8f, -1.1f);
            camera.transform.LookAt(bounds.center);
        }
        camera.orthographic = true;
        camera.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.z) * 1.1f;
        camera.farClipPlane = 6f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        camera.allowHDR = false;
        camera.cullingMask = 1 << coin.layer;
        var light = new GameObject("CoinFaceLight").AddComponent<Light>();
        light.transform.SetParent(stage.transform, false);
        light.transform.localPosition = new Vector3(-1f, 1.8f, -1f);
        light.type = LightType.Point;
        light.range = 4f;
        light.intensity = 3f;
        var request = new UniversalRenderPipeline.SingleCameraRequest { destination = texture };
        if (RenderPipeline.SupportsRenderRequest(camera, request))
            RenderPipeline.SubmitRenderRequest(camera, request);
        else
        {
            camera.targetTexture = texture;
            camera.Render();
        }
        stage.SetActive(false);
        Destroy(stage);
        return texture;
    }

    private void UpdateCoinImages()
    {
        if (active.Supply == null) return;
        Rect board = BoardArea();
        Vector2 origin = ToCanvas(active.StoryCenter());
        for (int i = 0; i < active.Coins.Length && i < coinImages.Count; i++)
        {
            var image = coinImages[i];
            if (image.texture == null) continue;
            var coin = active.Coins[i];
            var (start, finish, _, radius) = CoinLayout(coin, board, origin);
            var pose = GetCoinPose(coin, start, finish, elapsed);
            image.gameObject.SetActive(pose.Alpha > 0f);
            image.color = new Color(1f, 1f, 1f, pose.Alpha);
            image.rectTransform.anchoredPosition = pose.Position;
            image.rectTransform.localRotation = Quaternion.Euler(0f, 0f, pose.Angle);
            float size = radius * 2.2f * pose.Scale * Mathf.Lerp(.87f, 1f, pose.Alpha);
            if (active.Camera != null && active.Board != null)
            {
                Vector3 center = active.Board.bounds.center;
                var projectedLeft = ToCanvas(active.Camera.WorldToScreenPoint(center));
                var projectedRight = ToCanvas(active.Camera.WorldToScreenPoint(
                    center + active.Camera.transform.right * coinWorldDiameters[coin.Ability]));
                float landedSize = Vector2.Distance(projectedLeft, projectedRight) * 1.1f;
                size = Mathf.Lerp(size, landedSize, pose.Progress);
            }
            image.rectTransform.sizeDelta = Vector2.one * size;
        }
    }

    private void HideCoinImages()
    {
        foreach (var image in coinImages) image.gameObject.SetActive(false);
    }

    protected override void OnDestroy()
    {
        foreach (var texture in coinFaceTextures)
            if (texture != null)
            {
                texture.Release();
                Destroy(texture);
            }
        base.OnDestroy();
    }

    private (Vector2 Start, Vector2 Finish, Vector2 Formation, float Radius)
        CoinLayout(RewardCoin coin, Rect board, Vector2 origin)
    {
        float gap = Mathf.Min(68f, board.height * .78f / (active.Rows + .22f));
        float radius = Mathf.Max(2f, Mathf.Min(32f, board.width * .073f, gap * .67f));
        Vector2 formation = Formation(coin, radius, gap);
        Vector2 start = origin + formation;
        Vector2 finish = board.center + Vector2.left * (board.width * .025f) + formation;
        if (coin.Ability == 0)
        {
            start = origin + formation * .69f;
            finish += new Vector2(Mathf.Sin(coin.Index * 8.61f + 1f) * .33f,
                Mathf.Sin(coin.Index * 13.17f + 2f) * .31f) * radius;
        }
        return (start, finish, formation, radius);
    }

    private Vector2 Formation(RewardCoin coin, float radius, float gap)
    {
        float x = coin.InRow - (coin.RowSize - 1) * .5f;
        float y = (coin.Row - (active.Rows - 1) * .5f) * gap;
        if (coin.Ability == 3)
        {
            float spread = Mathf.Max(1f, (coin.RowSize - 1) * .5f);
            return new Vector2(x * radius * 2.35f, y + Mathf.Pow(x / spread, 2f) * radius * .66f);
        }
        if (coin.RowSize == 3)
        {
            Vector2 triangle = coin.InRow == 0 ? new Vector2(0f, .77f) :
                new Vector2(coin.InRow == 1 ? -1.20f : 1.20f, -.53f);
            return triangle * radius + Vector2.up * y;
        }
        return new Vector2(x * radius * 2.18f, y + Mathf.Cos(coin.InRow * Mathf.PI) * radius * .14f);
    }

    private void DrawCoin(VertexHelper mesh, RewardCoin coin, CoinPose pose, float radius)
    {
        float alpha = pose.Alpha;
        float tilt = Mathf.Lerp(.93f, .57f, pose.Progress);
        if (coin.Ability == 2) tilt *= 1f - .34f * Mathf.Sin(pose.Progress * Mathf.PI);
        if (coin.Ability == 0) tilt *= 1f - .26f * Mathf.Abs(Mathf.Sin(pose.Angle * Mathf.Deg2Rad));
        float size = radius * pose.Scale * Mathf.Lerp(.87f, 1f, alpha);
        Vector2 at = pose.Position;
        if (pose.Progress >= 1f)
            Disc(mesh, at + new Vector2(2f, -size * .29f), new Vector2(size * 1.12f, size * .54f),
                new Color(.18f, .08f, .02f, .62f), Color.clear, 22);
        Disc(mesh, at + Vector2.down * (size * .16f), new Vector2(size, size * tilt),
            WithAlpha(new Color32(125, 62, 13, 255), alpha), WithAlpha(new Color32(193, 108, 15, 255), alpha), 24);
        Disc(mesh, at, new Vector2(size, size * tilt), WithAlpha(WarmWhite, alpha), WithAlpha(Gold, alpha), 28);
        Disc(mesh, at, new Vector2(size * .87f, size * tilt * .87f),
            WithAlpha(new Color32(150, 74, 7, 255), alpha), WithAlpha(new Color32(129, 68, 11, 255), alpha), 24);
        Color enamel = Enamels[coin.Ability];
        Disc(mesh, at, new Vector2(size * .80f, size * tilt * .80f),
            WithAlpha(Color.Lerp(enamel, Color.white, .08f), alpha), WithAlpha(enamel * .82f, alpha), 26);
        Star(mesh, at, size * .67f, tilt, pose.Angle * Mathf.Deg2Rad, WithAlpha(WarmWhite, alpha), WithAlpha(Gold, alpha), 8);
        Arc(mesh, at, size * .94f, tilt, .53f, 1.94f, 1.2f, WithAlpha(WarmWhite, alpha * .86f), 11);
    }

    private void DrawSummon(VertexHelper mesh, Vector2 center)
    {
        int ability = active.Dominant;
        int tier = active.Tier;
        float elapsedSinceReveal = elapsed - active.Reveal;
        float intensity = 1f + .28f * tier;
        if (ability == 0)
        {
            DrawForceSummon(mesh, center, elapsedSinceReveal);
            return;
        }
        if (ability == 1 && elapsed >= Timings[1].Drop) return;
        float fade = ability == 2
            ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.27f, .49f, elapsedSinceReveal))
            : Mathf.Clamp01(1f - elapsedSinceReveal / .53f);
        float anticipation = Mathf.Clamp01(elapsed / active.Reveal);
        float burst = Mathf.Clamp01(elapsedSinceReveal / .30f);
        Color color = ability == 3 ? HotPink : Ice;

        if (ability == 1)
        {
            if (elapsed < active.Reveal)
            {
                float flash = Mathf.Sin(anticipation * Mathf.PI * .9f);
                Halo(mesh, center, (28f + flash * 28f) * intensity, 1f, color, flash * .76f);
                Star(mesh, center, 38f * flash * intensity, .86f, 0, WithAlpha(WarmWhite, flash), WithAlpha(color, flash), 4);
            }
            else if (fade > 0f)
            {
                float radius = (48f + Mathf.Sqrt(burst) * 112f) * intensity;
                Halo(mesh, center, radius * .90f, .84f, color, fade * .42f);
                Lightning(mesh, center, radius * 1.3f, fade, 6 + tier * 3, .84f);
            }
            if (elapsed < active.Reveal)
                Lightning(mesh, center, 50f * intensity, anticipation * .72f, 4, .80f);
            return;
        }

        if (fade <= 0f) return;
        float orbit = (elapsed < active.Reveal ? Mathf.Lerp(124f, 46f, anticipation) :
            Mathf.Lerp(75f, 150f, burst)) * intensity;
        if (ability == 2)
        {
            Halo(mesh, center, orbit * 1.2f, .87f, Violet, fade * .27f);
            if (elapsedSinceReveal >= 0f)
            {
                GlowArc(mesh, center, orbit, .90f, 0, Tau, Ice, fade * .95f, 54);
                GlowArc(mesh, center, orbit * .79f, .90f, 0, Tau, Violet, fade, 50);
                if (tier == 2) GlowArc(mesh, center, orbit * 1.16f, .90f, 0, Tau, Ice, fade * .45f, 49);
                Rays(mesh, center, orbit * 1.17f, Violet, fade * .68f, 12 + tier * 5);
            }
            int runes = 10 + tier * 4;
            for (int i = 0; i < runes; i++)
            {
                float angle = Tau * i / runes + elapsed * .62f;
                float gatherStart = i * active.Reveal * .68f / runes;
                float gather = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(gatherStart, gatherStart + .105f, elapsed));
                Vector2 at = center + Polar(angle, orbit + 65f * (1f - gather), .90f);
                Rune(mesh, at, 8f + tier * 1.8f, i, WithAlpha(i % 2 == 0 ? Ice : Violet, fade * gather));
            }
            return;
        }

        orbit = Mathf.Max(65f, orbit);
        Halo(mesh, center + Vector2.down * 20f, orbit * 1.30f, .47f, HotPink, fade * .25f * anticipation);
        GlowArc(mesh, center, orbit * 1.33f, .52f, Mathf.PI, Tau, HotPink, fade * anticipation, 32);
        GlowArc(mesh, center, orbit * 1.38f, .50f, 3.36f, 5.99f, Gold, fade * .8f * anticipation, 30);
        for (int i = 0; i < 10 + tier * 4; i++)
        {
            float angle = Mathf.PI + i * Mathf.PI / (9f + tier * 4f);
            Vector2 at = center + Polar(angle, orbit * (1.12f + .28f * Mathf.Sin(i * 5f)), .61f);
            float flicker = fade * anticipation * (.45f + .55f * Mathf.Abs(Mathf.Sin(i * 2.62f + elapsed * 12f)));
            Color spark = i % 2 == 0 ? HotPink : Gold;
            Halo(mesh, at, 18f, 1f, spark, flicker * .56f);
            Star(mesh, at, (8f + tier * 2f) * flicker, 1.25f, 0, WithAlpha(WarmWhite, flicker),
                WithAlpha(spark, flicker), 4);
        }
    }

    private void DrawForceSummon(VertexHelper mesh, Vector2 center, float impact)
    {
        var red = new Color32(255, 53, 32, 255);
        var ember = new Color32(255, 154, 84, 255);
        float width = Vector2.Distance(ToCanvas(Vector2.zero), ToCanvas(Vector2.right * active.StoryWidth()));
        float edgeRadius = width / 6f;
        if (impact < 0f)
        {
            float focus = Mathf.InverseLerp(-active.Reveal, 0f, impact);
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Tau / 8f + .32f;
                Vector2 spark = center + Polar(angle, Mathf.Lerp(89f, 8f, focus * focus), .68f);
                Stroke(mesh, spark + Polar(angle, 9f * focus, .68f), spark, 2.6f,
                    WithAlpha(red, 0f), WithAlpha(ember, focus * .81f));
            }
            Halo(mesh, center, 24f + 17f * focus, .75f, red, focus * .52f);
            Star(mesh, center, 12f * focus, .8f, 0, WithAlpha(WarmWhite, focus), WithAlpha(red, focus), 4);
            return;
        }

        const float shockDuration = .25f;
        if (impact >= shockDuration) return;
        float time = impact / shockDuration;
        float fade = (1f - time) * (1f - time);
        float expansion = 1f - Mathf.Pow(1f - Mathf.Clamp01(impact / .11f), 3f);
        float radius = Mathf.Lerp(30f, edgeRadius, expansion);
        float flash = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(impact / .086f));
        Halo(mesh, center, radius * .84f, .74f, red, fade * .72f);
        Halo(mesh, center, 79f, .76f, ember, flash * .93f);
        Star(mesh, center, 122f, .66f, .20f, WithAlpha(WarmWhite, flash), WithAlpha(red, flash * .80f), 4);
        GlowArc(mesh, center, radius, .73f, 0, Tau, red, fade, 56);
        GlowArc(mesh, center, radius * .86f, .76f, 0, Tau, ember, fade * .82f, 46);
        if (active.Tier > 0)
            GlowArc(mesh, center, radius * .93f, .77f, 0, Tau, WarmWhite, fade * .5f, 54);
        if (active.Tier == 2)
            Rays(mesh, center, radius * .76f, WarmWhite, fade, 20);

        int fragments = 14 + active.Tier * 5;
        for (int i = 0; i < fragments; i++)
        {
            float angle = i * Tau / fragments + Mathf.Sin(i * 1.7f) * .13f;
            float distance = Mathf.Lerp(21f, edgeRadius * (.58f + .25f * Mathf.Abs(Mathf.Sin(i * 7f))), expansion);
            Vector2 point = center + Polar(angle, distance, .71f) - Vector2.up * (34f * time * time);
            float chunk = (2.9f + i % 4 * 1.5f) * (1f - .43f * time);
            Disc(mesh, point + Polar(angle, -chunk * 2.3f, .71f), new Vector2(17f + time * 20f, 9f + time * 8f),
                WithAlpha(ember, fade * .31f), WithAlpha(ember, 0f), 12);
            Stroke(mesh, point - Polar(angle, chunk * 4f, .71f), point,
                chunk * .55f, Color.clear, WithAlpha(red, fade * .86f));
            int vertex = mesh.currentVertCount;
            Vertex(mesh, point + Polar(angle + time * 4f, chunk * 1.55f, .78f), WithAlpha(ember, fade));
            Vertex(mesh, point + Polar(angle + time * 4f + 2.04f, chunk, .78f), WithAlpha(new Color32(112, 55, 36, 255), fade));
            Vertex(mesh, point + Polar(angle + time * 4f + 4.58f, chunk, .78f), WithAlpha(red, fade * .91f));
            mesh.AddTriangle(vertex, vertex + 1, vertex + 2);
        }
    }

    private void DrawTrail(VertexHelper mesh, RewardCoin coin, Vector2 position, Vector2 start, Vector2 finish, float radius)
    {
        Color tint = coin.Ability == 3 ? HotPink : coin.Ability == 2 ? Violet : CoinColors[0];
        var direction = (finish - start).normalized;
        Vector2 trailing = position - direction * radius * (coin.Ability == 0 ? 1.24f : 2f);
        Stroke(mesh, trailing, position, radius * .52f, WithAlpha(tint, .24f), Color.clear);
        Stroke(mesh, trailing, position, radius * .14f, WithAlpha(tint, .76f), Color.clear);
        if (coin.Ability == 3 || coin.Ability == 2)
        {
            Vector2 side = new Vector2(-direction.y, direction.x);
            for (int i = 0; i < 3; i++)
            {
                var point = Vector2.Lerp(position, trailing, (i + 1) / 3f) + side * Mathf.Sin(i * 7f + elapsed * 10f) * 10f;
                Color spark = coin.Ability == 3 ? Gold : Ice;
                Star(mesh, point, 6f - i, 1.3f, 0f, WithAlpha(WarmWhite, .64f), WithAlpha(spark, .66f), 4);
            }
        }
    }

    private void DrawArrival(VertexHelper mesh, Vector2 point, float landingTime, float radius, Color tint, int ability)
    {
        float t = (elapsed - landingTime) / Timings[ability].Settle;
        if (t < 0f || t >= 1f) return;
        bool electric = ability == 1;
        float fade = 1f - t;
        float reach = radius * (electric ? Mathf.Lerp(1.9f, 4.7f, t) : Mathf.Lerp(1.1f, 2.1f, t));
        Halo(mesh, point, reach, .55f, tint, fade * (electric ? .65f : .22f));
        if (electric)
        {
            GlowArc(mesh, point, reach, .52f, 0f, Tau, tint, fade, 46);
            Lightning(mesh, point, reach * 1.2f, fade, 6 + active.Tier * 3, .52f);
            return;
        }
        if (ability == 0)
        {
            var dust = new Color32(220, 142, 95, 255);
            for (int i = -1; i <= 1; i++)
            {
                var puff = point + new Vector2(i * radius * (1f + t), radius * t * .68f);
                Disc(mesh, puff, new Vector2(radius * (.55f + t), radius * (.21f + t * .38f)),
                    WithAlpha(dust, fade * fade * .32f), WithAlpha(dust, 0), 15);
            }
            return;
        }
        Star(mesh, point + new Vector2(-radius, radius), 10f * fade, 1f, 0,
            WithAlpha(WarmWhite, fade), WithAlpha(tint, fade), 4);
    }

    private static void Rune(VertexHelper mesh, Vector2 at, float size, int index, Color tint)
    {
        int sides = index % 3 == 0 ? 3 : 4;
        float turn = index % 2 == 0 ? Mathf.PI * .25f : Mathf.PI * .50f;
        Arc(mesh, at, size, 1f, turn, Tau + turn, 2f, tint, sides);
        Stroke(mesh, at - Vector2.right * size * .26f, at + Vector2.right * size * .26f, 2f, tint, tint);
    }

    private static void Rays(VertexHelper mesh, Vector2 center, float radius, Color tint, float alpha, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = i * Tau / count + .13f;
            Vector2 from = center + Polar(angle, radius * (.82f + i % 3 * .12f), .66f);
            Vector2 end = center + Polar(angle, radius * (1.16f + i % 5 * .055f), .66f);
            Stroke(mesh, from, end, 2f, Color.clear, WithAlpha(tint, alpha));
        }
    }

    private static void Lightning(VertexHelper mesh, Vector2 center, float radius, float alpha, int count, float ratio)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = Tau * i / count + .14f;
            var from = center + Polar(angle, radius * .36f, ratio);
            for (int part = 1; part <= 5; part++)
            {
                float jag = Mathf.Sin(i * 17.4f + part * 15.6f) * .13f;
                Vector2 to = center + Polar(angle + jag, radius * (.36f + part * .13f), ratio);
                float glow = alpha * (1f - part * .115f);
                Stroke(mesh, from, to, 9f, WithAlpha(Ice, glow * .20f), WithAlpha(Ice, glow * .20f));
                Stroke(mesh, from, to, 2.1f, WithAlpha(WarmWhite, glow), WithAlpha(Ice, glow));
                from = to;
            }
        }
    }

    private static void Halo(VertexHelper mesh, Vector2 at, float radius, float ratio, Color tint, float alpha)
    {
        Disc(mesh, at, new Vector2(radius, radius * ratio), WithAlpha(tint, alpha), WithAlpha(tint, 0), 32);
        Disc(mesh, at, new Vector2(radius * .34f, radius * ratio * .34f),
            WithAlpha(WarmWhite, alpha * .56f), WithAlpha(tint, 0), 22);
    }

    private static void GlowArc(VertexHelper mesh, Vector2 center, float radius, float ratio,
        float from, float to, Color tint, float alpha, int segments)
    {
        Arc(mesh, center, radius, ratio, from, to, 12f, WithAlpha(tint, alpha * .19f), segments);
        Arc(mesh, center, radius, ratio, from, to, 5f, WithAlpha(tint, alpha * .67f), segments);
        Arc(mesh, center, radius, ratio, from, to, 1.6f, WithAlpha(Color.Lerp(tint, WarmWhite, .67f), alpha), segments);
    }

    private static void Arc(VertexHelper mesh, Vector2 center, float radius, float ratio,
        float from, float to, float thickness, Color tint, int segments)
    {
        Vector2 previous = center + Polar(from, radius, ratio);
        for (int i = 1; i <= segments; i++)
        {
            Vector2 next = center + Polar(Mathf.Lerp(from, to, i / (float)segments), radius, ratio);
            Stroke(mesh, previous, next, thickness, tint, tint);
            previous = next;
        }
    }

    private static void Star(VertexHelper mesh, Vector2 center, float radius, float ratio, float rotation,
        Color core, Color edge, int rays)
    {
        int start = mesh.currentVertCount;
        Vertex(mesh, center, core);
        int wedges = rays * 2;
        for (int i = 0; i <= wedges; i++)
        {
            float length = i % 2 != 0 ? radius * .25f : i % 4 == 0 || rays == 4 ? radius : radius * .72f;
            Vertex(mesh, center + Polar(rotation + Tau * i / wedges, length, ratio), edge);
            if (i > 0) mesh.AddTriangle(start, start + i, start + i + 1);
        }
    }

    private static void Disc(VertexHelper mesh, Vector2 center, Vector2 radius, Color fill, Color edge, int segments)
    {
        int start = mesh.currentVertCount;
        Vertex(mesh, center, fill);
        for (int i = 0; i <= segments; i++)
        {
            float angle = Tau * i / segments;
            Vertex(mesh, center + Vector2.Scale(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), radius), edge);
            if (i > 0) mesh.AddTriangle(start, start + i, start + i + 1);
        }
    }

    private static void Stroke(VertexHelper mesh, Vector2 from, Vector2 to, float width, Color startColor, Color endColor)
    {
        var axis = (to - from).normalized;
        Vector2 side = new Vector2(-axis.y, axis.x) * (width * .5f);
        int start = mesh.currentVertCount;
        Vertex(mesh, from + side, startColor);
        Vertex(mesh, from - side, startColor);
        Vertex(mesh, to - side, endColor);
        Vertex(mesh, to + side, endColor);
        mesh.AddTriangle(start, start + 1, start + 2);
        mesh.AddTriangle(start, start + 2, start + 3);
    }

    private static void Vertex(VertexHelper mesh, Vector2 point, Color color) =>
        mesh.AddVert(point, color, Vector2.zero);

    private static Vector2 Polar(float angle, float radius, float ratio) =>
        new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * ratio);

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }
}
