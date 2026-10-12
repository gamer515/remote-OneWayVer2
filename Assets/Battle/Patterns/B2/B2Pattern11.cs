using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public sealed class B2Pattern11 : IBattlePattern
{

    private const int ProjectileCount = 10;
    private const float SpawnInterval = 1.2f;
    private sealed class Fragment
    {
        public Transform visual;
        public Vector2 origin, velocity;
        public float birth, angle, spin;
    }

    public IEnumerator Run(BattleContext c, BattlePatternData data, BattleStepScope scope)
    {
        BattlePlayerPreparationRunner.Apply(c, data.playerPreparation);
        scope.OnDispose(() => BattlePlayerPreparationRunner.EndSpecial(c));

        // 반전 화면에 맞춘 이동과 피격은 이 패턴에서 직접 처리한다.
        c.Player.SetMovementMode(PlayerController.MovementMode.External);
        c.Player.Body.simulated = false;

        // 카메라 출력을 반전하기 전의 화면 범위를 저장한다.
        Rect normalScreen = c.Arena.ScreenRect;

        // 카메라가 그린 결과만 상하 반전.
        EnableVerticalFlip(c, scope);
        Texture2D texture = Texture2D.whiteTexture;
        Sprite square = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
            Vector2.one * 0.5f, texture.width, 0, SpriteMeshType.FullRect);
        scope.OnDispose(() => Object.Destroy(square));

        Rect box = c.Arena.Bounds;
        float speed = Mathf.Max(0.1f, data.speed);
        Vector2 size = new Vector2(
    Mathf.Max(0.02f, data.swordSize.x),
    Mathf.Max(0.1f, data.swordSize.y));

        Vector2 half = size * 0.5f;

        float rotation = data.swordRotationSpeed;
        float fragmentSize = Mathf.Max(0.02f, data.burstFragmentSize);
        float fragmentSpeed = Mathf.Max(0.1f, data.burstFragmentSpeed);
        float lifetime = Mathf.Max(0.1f, data.burstFragmentLifetime);
        int fragmentCount = Mathf.Clamp(data.burstFragmentCount, 3, 40);

        // 탄환 전체가 화면 오른쪽 바깥에서 시작.
        float startX = Mathf.Max(normalScreen.xMax, box.xMax)
            + half.magnitude + 0.5f;

        

        var origins = new Vector2[ProjectileCount];
        var births = new float[ProjectileCount];
        var times = new float[ProjectileCount];
        var swords = new Transform[ProjectileCount];

        for (int i = 0; i < ProjectileCount; i++)
        {
            // 회전 중심의 Y가 BattleBox 내부에 위치.
            float margin = Mathf.Min(0.1f, box.height * 0.1f);

            origins[i] = new Vector2(
                startX,
                Random.Range(box.yMin + margin, box.yMax - margin));

            births[i] = i * SpawnInterval;

            // 각 검마다 별도로 폭발 위치 선택.
            // 왼쪽 벽 안쪽 ~ BattleBox 중심 사이.
            float explosionX = Random.Range(
                box.xMin,
                box.center.x);

            times[i] = births[i] + (startX - explosionX) / speed;

            swords[i] = CreateSquare(
                "Inverted Burst Projectile " + (i + 1),
                square,
                scope.Root,
                size);

            swords[i].position = origins[i];

            // 생성 시점 전에는 보이지 않도록 숨김.
            swords[i].gameObject.SetActive(false);
        }

        var fragments = new List<Fragment>(
            fragmentCount * ProjectileCount);
        int exploded = 0;
        float elapsed = 0f;
        Vector2 previousPlayer = c.Player.transform.position;
        float fragmentSpin = Mathf.Max(90f, Mathf.Abs(rotation));
        float travelBound = Mathf.Max(speed + Mathf.Abs(rotation) * Mathf.Deg2Rad * half.magnitude,
            fragmentSpeed * 1.15f + fragmentSpin * Mathf.Deg2Rad * fragmentSize);
        while ((exploded < ProjectileCount || fragments.Count > 0) && c.Owner.Result == BattleResult.Running)
        {
            float dt = c.Clock.Delta;
            if (dt <= 0f) { yield return null; continue; }

            MovePlayerForFlippedView(c, box, normalScreen, dt);

            Vector2 player = c.Player.transform.position;
            int samples = Mathf.Max(1, Mathf.CeilToInt((travelBound * dt + Vector2.Distance(previousPlayer, player)) / 0.04f));
            bool contacted = false;
            for (int sample = 1; sample <= samples; sample++)
            {
                float blend = (float)sample / samples;
                float time = elapsed + dt * blend;
                Vector2 playerAtTime = Vector2.Lerp(previousPlayer, player, blend);
                for (int i = 0; i < ProjectileCount; i++)
                {
                    if (swords[i] == null)
                        continue;

                    if (time < births[i])
                        continue;

                    swords[i].gameObject.SetActive(true);

                    // 생성 이후 실제로 이동한 시간.
                    float swordTime = Mathf.Min(time, times[i]) - births[i];
                    Vector2 position = origins[i] + Vector2.left * (speed * swordTime);
                    float angle = rotation * swordTime;
                    swords[i].SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, angle));
                    if (time >= times[i])
                    {
                        Burst(square, scope.Root, position, angle, times[i], half, fragmentCount,
                            fragmentSize, fragmentSpeed, fragmentSpin, fragments);
                        swords[i].gameObject.SetActive(false);
                        Object.Destroy(swords[i].gameObject);
                        swords[i] = null;
                        exploded++;
                    }
                    else if (!contacted && Touches(playerAtTime, c.Player.HitRadius + 0.02f, position, angle, half))
                    {
                        contacted = true;
                        c.Player.TakeDamage(1f);
                        if (c.Owner.Result != BattleResult.Running) yield break;
                    }
                }
                for (int i = fragments.Count - 1; i >= 0; i--)
                {
                    Fragment fragment = fragments[i];
                    float age = time - fragment.birth;
                    if (age >= lifetime)
                    {
                        fragment.visual.gameObject.SetActive(false);
                        Object.Destroy(fragment.visual.gameObject);
                        fragments.RemoveAt(i);
                        continue;
                    }
                    Vector2 position = fragment.origin + fragment.velocity * age;
                    float angle = fragment.angle + fragment.spin * age;
                    fragment.visual.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, angle));
                    if (!contacted && Touches(playerAtTime, c.Player.HitRadius + 0.02f, position,
                        angle, Vector2.one * (fragmentSize * 0.5f)))
                    {
                        contacted = true;
                        c.Player.TakeDamage(1f);
                        if (c.Owner.Result != BattleResult.Running) yield break;
                    }
                }
            }
            elapsed += dt;
            previousPlayer = player;
            yield return null;
        }
    }

    private static void Burst(Sprite square, Transform parent, Vector2 position, float angle, float time,
        Vector2 half, int count, float size, float speed, float spin, List<Fragment> fragments)
    {
        Quaternion rotation = Quaternion.Euler(0f, 0f, angle);
        float phase = Random.Range(0f, 360f);
        for (int i = 0; i < count; i++)
        {
            // Pieces start along the actual rotated blade, rather than a separate explosion sprite.
            Vector2 local = new Vector2(Random.Range(-half.x, half.x), Mathf.Lerp(-half.y, half.y, (i + 0.5f) / count));
            Vector2 origin = position + (Vector2)(rotation * (Vector3)local);
            float direction = (phase + 360f * (i + Random.Range(-0.2f, 0.2f)) / count) * Mathf.Deg2Rad;
            Transform visual = CreateSquare("Sword Fragment", square, parent, Vector2.one * size);
            visual.SetPositionAndRotation(origin, rotation);
            fragments.Add(new Fragment
            {
                visual = visual, origin = origin, birth = time, angle = angle,
                velocity = new Vector2(Mathf.Cos(direction), Mathf.Sin(direction)) * speed * Random.Range(0.85f, 1.15f),
                spin = spin * (Random.value < 0.5f ? -1f : 1f)
            });
        }
    }

    private static Transform CreateSquare(string name, Sprite square, Transform parent, Vector2 size)
    {
        var go = new GameObject(name, typeof(SpriteRenderer));
        go.transform.SetParent(parent, false);
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        var renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = square;
        renderer.color = Color.white;
        renderer.sortingOrder = 15;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return go.transform;
    }

    private static bool Touches(Vector2 player, float radius, Vector2 center, float angle, Vector2 half)
    {
        Vector2 delta = player - center;
        float radians = angle * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians), sin = Mathf.Sin(radians);
        Vector2 local = new Vector2(cos * delta.x + sin * delta.y, -sin * delta.x + cos * delta.y);
        Vector2 nearest = new Vector2(Mathf.Clamp(local.x, -half.x, half.x), Mathf.Clamp(local.y, -half.y, half.y));
        return (local - nearest).sqrMagnitude <= radius * radius;
    }

    private static void EnableVerticalFlip(
    BattleContext c,
    BattleStepScope scope)
    {
        Camera camera = c.Camera;

        RenderTexture originalTarget = camera.targetTexture;
        Rect originalRect = camera.rect;
        float originalAspect = camera.aspect;

        // 현재 프로젝트의 전체 화면 Battle Camera 기준.
        var texture = new RenderTexture(
            Mathf.Max(1, Screen.width),
            Mathf.Max(1, Screen.height),
            24);

        texture.name = "B2 Pattern 11 Flipped View";
        texture.Create();

        var canvasObject = new GameObject(
            "B2 Flipped Battle View",
            typeof(RectTransform),
            typeof(Canvas));

        canvasObject.transform.SetParent(scope.Root, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        // 기존 대화창과 패턴 선택 UI보다 뒤에 표시.
        canvas.sortingOrder = -100;
        canvas.targetDisplay = camera.targetDisplay;

        var imageObject = new GameObject(
            "Flipped Camera Image",
            typeof(RectTransform),
            typeof(RawImage));

        imageObject.transform.SetParent(canvasObject.transform, false);

        RawImage image = imageObject.GetComponent<RawImage>();
        image.texture = texture;
        image.color = Color.white;
        image.raycastTarget = false;

        // Y 방향으로만 텍스처를 뒤집는다.
        image.uvRect = new Rect(0f, 1f, 1f, -1f);

        RectTransform rect = image.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        scope.OnDispose(() =>
        {
            // 반전 화면을 먼저 숨긴 뒤 카메라 출력 복원.
            if (canvasObject != null)
                canvasObject.SetActive(false);

            if (camera != null)
            {
                camera.targetTexture = originalTarget;
                camera.rect = originalRect;
                camera.aspect = originalAspect;
            }

            texture.Release();
            Object.Destroy(texture);
        });

        camera.targetTexture = texture;
        camera.rect = new Rect(0f, 0f, 1f, 1f);
        camera.aspect = originalAspect;

        // Display에 직접 출력하는 배경용 카메라.
        // 전투 오브젝트는 그리지 않고 검은 배경만 출력한다.
        // 그 위에 기존 RawImage가 반전된 전투 영상을 표시한다.
        var displayCameraObject = new GameObject(
            "B2 Pattern 11 Display Camera",
            typeof(Camera));

        displayCameraObject.transform.SetParent(scope.Root, false);

        Camera displayCamera =
            displayCameraObject.GetComponent<Camera>();

        displayCamera.targetTexture = null;
        displayCamera.targetDisplay = camera.targetDisplay;
        displayCamera.rect = new Rect(0f, 0f, 1f, 1f);

        displayCamera.clearFlags = CameraClearFlags.SolidColor;
        displayCamera.backgroundColor = Color.black;
        displayCamera.cullingMask = 0;

        displayCamera.depth = camera.depth - 1f;
        displayCamera.allowHDR = false;
        displayCamera.allowMSAA = false;
        displayCamera.enabled = true;

        scope.OnDispose(() =>
        {
            if (displayCameraObject != null)
            {
                displayCameraObject.SetActive(false);
                Object.Destroy(displayCameraObject);
            }
        });
    }


    private static void MovePlayerForFlippedView(
        BattleContext c,
        Rect box,
        Rect normalScreen,
        float dt)
    {
        if (c.Player.isControlLocked)
            return;

        // Viewport座標を使い、RenderTextureの解像度に依存させない。
        Vector3 viewport = new Vector3(
            Input.mousePosition.x / Mathf.Max(1f, Screen.width),
            Input.mousePosition.y / Mathf.Max(1f, Screen.height),
            0f);

        Ray ray = c.Camera.ViewportPointToRay(viewport);
        Plane plane = new Plane(Vector3.forward, Vector3.zero);

        if (!plane.Raycast(ray, out float distance))
            return;

        Vector2 target = ray.GetPoint(distance);

        // 뒤집혀 표시된 BattleBox를 기준으로 조작 범위를 맞춘다.
        // 표시된 상자 중심에 마우스를 두면 플레이어도 중심으로 간다.
        target.y += 2f * (box.center.y - normalScreen.center.y);

        target = c.Arena.Clamp(target, c.Player.HalfSize);

        Vector2 position = Vector2.MoveTowards(
            c.Player.transform.position,
            target,
            Mathf.Max(0f, c.Player.b2MouseMoveSpeed) * dt);

        c.Player.transform.position =
            new Vector3(position.x, position.y, 0f);

        c.Player.Body.position = position;
        c.Player.Body.linearVelocity = Vector2.zero;
    }
}