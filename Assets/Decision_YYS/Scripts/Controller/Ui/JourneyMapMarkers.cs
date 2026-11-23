using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>Projects loaded terrain objects and actors onto the existing road camera image.</summary>
[DefaultExecutionOrder(100)]
public sealed class JourneyMapMarkers : MonoBehaviour
{
    private const float ApproachDistance = 24f;
    private const float CloudDuration = 1.7f;
    private static readonly Color PlaceColor = new Color(.12f, .4f, .9f);
    private static readonly Color ShopColor = new Color(.82f, .44f, .08f);
    [SerializeField] private Camera mapCamera;
    [SerializeField] private MineCollectorProcession collectors;
    [SerializeField] private RectTransform mapArea;
    [SerializeField] private MapSymbolGraphic warning;
    [SerializeField] private TextMeshProUGUI warningText;
    [SerializeField, Min(4)] private float markerSize=16;
    private readonly Dictionary<string,MapSymbolGraphic> markers=new Dictionary<string,MapSymbolGraphic>();
    private readonly Dictionary<Transform, MapSymbolGraphic> worldMarkers = new();
    private readonly HashSet<Transform> loadedTargets = new();
    private readonly List<Transform> removedTargets = new();
    private Player player;
    private EnvController environment;
    private TextMeshProUGUI nextShopText;
    private bool collectorNear;
    private bool collectorReturning;
    private float cloudStart = float.NegativeInfinity;
    private void LateUpdate()
    {
        if (mapCamera == null) return;
        if (player == null) player = FindFirstObjectByType<Player>();
        if (environment == null) environment = FindFirstObjectByType<EnvController>();
        if (player != null) Position("player", player.transform.position, new Color(1, .72f, .2f));
        UpdateWorldMarkers();
        UpdateCollectors();
    }

    private void UpdateWorldMarkers()
    {
        loadedTargets.Clear();
        float nextShopDistance = float.PositiveInfinity;
        if (environment != null)
        {
            foreach (var chunk in environment.LoadedMapPoints)
            foreach (var point in chunk)
            {
                var target = point.Target;
                if (target == null || !target.gameObject.activeInHierarchy || !loadedTargets.Add(target)) continue;

                if (!worldMarkers.TryGetValue(target, out var graphic))
                {
                    graphic = point.Kind switch
                    {
                        TerrainBuilder.MapPointKind.Shop => CreateMarker("shop:" + target.name, ShopColor, MapSymbolGraphic.Symbol.Shop),
                        TerrainBuilder.MapPointKind.Place => CreateMarker("place:" + target.name, PlaceColor, MapSymbolGraphic.Symbol.Place),
                        _ => CreateMarker("roadside:" + target.name, new Color(.25f, .95f, .68f), MapSymbolGraphic.Symbol.Dot)
                    };
                    if (markers.TryGetValue("player", out var playerMarker))
                        graphic.rectTransform.SetSiblingIndex(playerMarker.rectTransform.GetSiblingIndex());
                    worldMarkers.Add(target, graphic);
                }
                Position(graphic, target.position);

                if (point.Kind == TerrainBuilder.MapPointKind.Shop && player != null)
                {
                    float ahead = target.position.z - player.transform.position.z;
                    if (ahead >= -1f) nextShopDistance = Mathf.Min(nextShopDistance, Mathf.Max(0f, ahead));
                }
            }
        }

        removedTargets.Clear();
        foreach (var entry in worldMarkers)
            if (!loadedTargets.Contains(entry.Key)) removedTargets.Add(entry.Key);
        foreach (var target in removedTargets)
        {
            Destroy(worldMarkers[target].gameObject);
            worldMarkers.Remove(target);
        }
        UpdateShopDistance(nextShopDistance);
    }

    private void UpdateCollectors()
    {
        bool visible = collectors != null && collectors.Count > 0 && collectors.Members[0].gameObject.activeInHierarchy;
        warningText.gameObject.SetActive(visible);
        if (!visible)
        {
            collectorNear = false;
            cloudStart = float.NegativeInfinity;
            warning.SetCollectorWarning(0f, false, Vector2.zero, 0f);
            warning.gameObject.SetActive(false);
            foreach (var marker in markers)
                if (marker.Key.StartsWith("collector:") && marker.Value.gameObject.activeSelf)
                    marker.Value.gameObject.SetActive(false);
            return;
        }

        float nearest = float.PositiveInfinity;
        foreach (var member in collectors.Members)
        {
            Position("collector:" + member.GetInstanceID(), member.position, new Color(1,.25f,.35f),
                MapSymbolGraphic.Symbol.Collector);
            if (player != null) nearest = Mathf.Min(nearest, Vector3.Distance(member.position, player.transform.position));
        }

        UpdateCollectorCloud(nearest);
        string phase = collectors.Finished ? "징수원 대기" : collectors.IsReturning ? "세금 추격 ↑" : "시민 분류 검사 ↓";
        warningText.text = float.IsFinite(nearest) ? $"{phase} / {nearest:F0}u" : phase;
    }

    private void UpdateCollectorCloud(float nearest)
    {
        Vector3 front = mapCamera.WorldToViewportPoint(collectors.Members[0].position);
        bool frontVisible = front.z > 0f && front.x > 0f && front.x < 1f && front.y > 0f && front.y < 1f;
        bool returning = collectors.IsReturning;
        bool near = collectors.IsOnPatrol && frontVisible && nearest <= ApproachDistance + (collectorNear ? 2f : 0f);
        float now = Time.unscaledTime;
        if (near && (!collectorNear || returning != collectorReturning))
            cloudStart = now;
        collectorNear = near;
        collectorReturning = returning;

        float age = now - cloudStart;
        float opacity = near && age < CloudDuration
            ? Mathf.Clamp01(age / .18f) * (1f - Mathf.SmoothStep(0f, 1f, (age - .5f) / (CloudDuration - .5f)))
            : 0f;
        float offset = front.x > .53f ? -.16f : .16f;
        var cloud = new Vector2(Mathf.Clamp(front.x + offset, .15f, .85f), Mathf.Clamp(front.y, .04f, .96f));
        if (opacity > 0f && player != null)
        {
            Vector2 playerPoint = mapCamera.WorldToViewportPoint(player.transform.position);
            if (Mathf.Abs(cloud.y - playerPoint.y) < .07f && Mathf.Abs(cloud.x - playerPoint.x) < .15f)
            {
                cloud.x = Mathf.Clamp(front.x - offset, .15f, .85f);
                if (Mathf.Abs(cloud.x - playerPoint.x) < .15f)
                    opacity = 0f;
            }
        }
        bool showFront = frontVisible && !returning;
        warning.SetCollectorWarning(front.y, showFront, cloud, opacity);
        warning.gameObject.SetActive(showFront || opacity > 0f);
    }
    private void UpdateShopDistance(float distance)
    {
        if (nextShopText == null && warningText != null && float.IsFinite(distance))
        {
            nextShopText = Instantiate(warningText, mapArea);
            nextShopText.name = "NextShopDistance";
            nextShopText.raycastTarget = false;
            nextShopText.alignment = TextAlignmentOptions.Center;
            nextShopText.textWrappingMode = TextWrappingModes.NoWrap;
            var rect = nextShopText.rectTransform;
            rect.anchorMin = new Vector2(.08f, .81f);
            rect.anchorMax = new Vector2(.92f, .81f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(0f, 24f);
            rect.anchoredPosition = Vector2.zero;
        }
        if (nextShopText == null) return;
        nextShopText.gameObject.SetActive(float.IsFinite(distance));
        if (nextShopText.gameObject.activeSelf)
            nextShopText.text = distance < 1f ? "상점" : $"다음 상점 {Mathf.CeilToInt(distance)}u";
    }
    private void Position(string id, Vector3 world, Color tint,
        MapSymbolGraphic.Symbol symbol = MapSymbolGraphic.Symbol.Dot)
    {
        if (!markers.TryGetValue(id, out var graphic))
        {
            graphic = CreateMarker(id, tint, symbol);
            markers.Add(id, graphic);
        }
        Position(graphic, world);
    }

    private MapSymbolGraphic CreateMarker(string name, Color tint, MapSymbolGraphic.Symbol symbol)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(MapSymbolGraphic));
        go.transform.SetParent(mapArea, false);
        var graphic = go.GetComponent<MapSymbolGraphic>();
        graphic.raycastTarget = false;
        graphic.Shape = symbol;
        graphic.color = tint;
        bool location = symbol == MapSymbolGraphic.Symbol.Place || symbol == MapSymbolGraphic.Symbol.Shop;
        graphic.rectTransform.sizeDelta = Vector2.one * (location ? markerSize * 1.5f : markerSize);
        return graphic;
    }

    private void Position(MapSymbolGraphic graphic, Vector3 world)
    {
        Vector3 point = mapCamera.WorldToViewportPoint(world);
        bool visible = point.z > 0f && point.x >= 0f && point.x <= 1f && point.y >= 0f && point.y <= 1f;
        graphic.gameObject.SetActive(visible);
        if (!visible) return;
        graphic.rectTransform.anchorMin = graphic.rectTransform.anchorMax = new Vector2(point.x, point.y);
        graphic.rectTransform.anchoredPosition = Vector2.zero;
    }
}
