using System.Collections.Generic;
using UnityEngine;

public sealed class B1MistController : MonoBehaviour
{
    sealed class MistPiece
    {
        public SpriteRenderer renderer;
        public float speed;
    }

    readonly List<MistPiece> pieces = new List<MistPiece>();
    readonly List<Sprite> sprites = new List<Sprite>();

    BattleContext context;
    Rect area;

    bool started;
    float spawnTimer;
    float visibility;

    BattleSceneController Settings => context.Owner;

    public void Initialize(BattleContext battleContext)
    {
        context = battleContext;

        if (Settings.b1MistSprites != null)
        {
            foreach (Sprite sprite in Settings.b1MistSprites)
            {
                if (sprite != null)
                    sprites.Add(sprite);
            }
        }

        // 새 이미지를 연결하지 않았다면 기존 Mist 사용.
        if (sprites.Count == 0 && Settings.mist != null)
            sprites.Add(Settings.mist);
    }

    public void SetVisibility(float alpha)
    {
        visibility = Mathf.Clamp01(alpha);

        if (!started)
        {
            started = true;

            // B1 통로의 범위를 저장.
            // 퇴장 때 Arena.Mode가 바뀌어도 생성 범위는 유지됨.
            area = context.Arena.ScreenRect;

            int count = Mathf.Clamp(
                Settings.b1MistInitialCount,
                0,
                Mathf.Max(1, Settings.b1MistMaxCount));

            for (int i = 0; i < count; i++)
                Spawn(true);
        }

        ApplyOpacity();
    }

    void Update()
    {
        if (!started || context == null ||
            context.Owner.Result != BattleResult.Running)
            return;

        float dt = context.Clock.Delta;

        // B1 경고 대사 중에는 이동과 생성 모두 정지.
        if (dt <= 0f)
            return;

        for (int i = pieces.Count - 1; i >= 0; i--)
        {
            MistPiece piece = pieces[i];

            piece.renderer.transform.position +=
                Vector3.down * (piece.speed * dt);

            // 안개의 위쪽 끝까지 화면 아래로 내려가면 제거.
            if (piece.renderer.bounds.max.y <
                context.Arena.ScreenRect.yMin)
            {
                Destroy(piece.renderer.gameObject);
                pieces.RemoveAt(i);
            }
        }

        spawnTimer += dt;

        float interval = Mathf.Max(
            0.05f, Settings.b1MistSpawnInterval);

        if (spawnTimer >= interval)
        {
            spawnTimer = 0f;

            if (pieces.Count < Mathf.Max(1, Settings.b1MistMaxCount))
                Spawn(false);
        }

        // 실행 중 Inspector에서 투명도를 바꿔도 반영.
        ApplyOpacity();
    }

    void Spawn(bool initial)
    {
        if (sprites.Count == 0)
            return;

        Sprite sprite = sprites[Random.Range(0, sprites.Count)];

        var go = new GameObject("B1 Mist Piece");
        go.transform.SetParent(transform, false);

        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 20;

        float width = RandomPositive(Settings.b1MistWidthRange);
        float scale = width / Mathf.Max(0.001f, sprite.bounds.size.x);

        // 원본 이미지의 가로세로 비율 유지.
        go.transform.localScale = Vector3.one * scale;

        // 안개의 중심이 화면 왼쪽 끝부터 오른쪽 끝까지 배치됨.
        float x = Random.Range(area.xMin, area.xMax);

        float centerY;

        if (initial)
        {
            // 처음에는 화면 전체 높이에 안개를 배치.
            centerY = Random.Range(area.yMin, area.yMax);
        }
        else
        {
            // 이후에는 안개의 아래쪽 끝이 화면 위에 있도록 생성.
            float halfHeight = sprite.bounds.extents.y * scale;
            centerY = area.yMax + halfHeight + 0.1f;
        }

        // Sprite의 Pivot 위치와 관계없이 이미지 중심을 맞춤.
        go.transform.position = new Vector3(
            x - sprite.bounds.center.x * scale,
            centerY - sprite.bounds.center.y * scale,
            -1f);

        pieces.Add(new MistPiece
        {
            renderer = renderer,
            speed = RandomPositive(Settings.b1MistSpeedRange)
        });
    }

    void ApplyOpacity()
    {
        float alpha = visibility * Mathf.Clamp01(Settings.b1MistOpacity);

        foreach (MistPiece piece in pieces)
            piece.renderer.color = new Color(1f, 1f, 1f, alpha);
    }

    static float RandomPositive(Vector2 range)
    {
        float min = Mathf.Max(0.01f, Mathf.Min(range.x, range.y));
        float max = Mathf.Max(min, Mathf.Max(range.x, range.y));

        return Random.Range(min, max);
    }
}