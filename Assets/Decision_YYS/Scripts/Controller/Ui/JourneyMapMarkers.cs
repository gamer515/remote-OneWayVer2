using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>Projects actual actors and room entrances onto the existing road camera image.</summary>
[DefaultExecutionOrder(100)]
public sealed class JourneyMapMarkers : MonoBehaviour
{
    [SerializeField] private Camera mapCamera;
    [SerializeField] private MineNpcPlacement placement;
    [SerializeField] private MineCollectorProcession collectors;
    [SerializeField] private RectTransform mapArea;
    [SerializeField] private MapSymbolGraphic warning;
    [SerializeField] private TextMeshProUGUI warningText;
    [SerializeField, Min(4)] private float markerSize=16;
    private readonly Dictionary<string,MapSymbolGraphic> markers=new Dictionary<string,MapSymbolGraphic>();
    private Player player;
    private EnvController environment;
    private TextMeshProUGUI nextShopText;
    private float nextScan;
    private readonly List<Transform> actors=new List<Transform>();
    private void LateUpdate()
    {
        if(mapCamera==null||placement==null)return;
        if(player==null)player=FindFirstObjectByType<Player>();
        if(environment==null)environment=FindFirstObjectByType<EnvController>();
        if(Time.unscaledTime>=nextScan)
        {
            nextScan=Time.unscaledTime+1;actors.Clear();
            foreach(var place in placement.Places)
            {
                var root=GameObject.Find("Place_"+place.placeId);if(root==null)continue;
                foreach(Transform actor in root.transform)if(actor.name.StartsWith("ActorSocket_"))actors.Add(actor);
            }
        }
        foreach(var place in placement.Places)
        {
            var doors = placement.Doors(place);
            Position("room:"+place.placeId,(doors.entrance+doors.exit)*.5f,new Color(.12f,.4f,.9f),true);
        }
        foreach(var actor in actors)if(actor!=null)Position("npc:"+actor.GetInstanceID(),actor.position,new Color(.25f,.95f,.68f),false);
        if(player!=null)Position("player",player.transform.position,new Color(1,.72f,.2f),false);
        UpdateFacilityMarkers();
        UpdateCollectors();
    }

    private void UpdateCollectors()
    {
        bool visible = collectors != null && collectors.Count > 0 && collectors.Members[0].gameObject.activeInHierarchy;
        Vector3 front = visible ? mapCamera.WorldToViewportPoint(collectors.Members[0].position) : Vector3.zero;
        bool showCloud = visible && !collectors.IsReturning && front.z > 0 && front.y > 0 && front.y < 1;
        warning.gameObject.SetActive(showCloud);
        warningText.gameObject.SetActive(visible);
        if (!visible)
        {
            foreach (var marker in markers)
                if (marker.Key.StartsWith("collector:") && marker.Value.gameObject.activeSelf)
                    marker.Value.gameObject.SetActive(false);
            return;
        }

        float nearest = float.PositiveInfinity;
        foreach (var member in collectors.Members)
        {
            Position("collector:" + member.GetInstanceID(), member.position, new Color(1,.25f,.35f), false);
            if (player != null) nearest = Mathf.Min(nearest, Vector3.Distance(member.position, player.transform.position));
        }

        if (showCloud) warning.SetWarningFront(front.y, true);
        string phase = collectors.Finished ? "징수원 대기" : collectors.IsReturning ? "징수원 복귀 ↑" : "징수원 접근 ↓";
        warningText.text = float.IsFinite(nearest) ? $"{phase} / {nearest:F0}u" : phase;
    }
    private void UpdateFacilityMarkers()
    {
        if (environment == null || player == null || warningText == null) return;
        float distance = float.PositiveInfinity;
        string nearestShop = null;
        foreach (var facility in environment.FacilityMarkers)
        {
            float ahead = facility.Position.z - player.transform.position.z;
            if (!facility.IsShop || ahead < -1f || ahead >= distance) continue;
            distance = Mathf.Max(0f, ahead);
            nearestShop = facility.Id;
        }
        foreach (var facility in environment.FacilityMarkers)
            Position("facility:" + facility.Id, facility.Position,
                facility.IsShop ? new Color(1f, .68f, .19f) : new Color(.28f, .76f, .92f), true, facility.Id == nearestShop);
        if (nextShopText == null && float.IsFinite(distance))
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
            nextShopText.text = distance < 1f ? "상점 예정지" : $"다음 상점 예정지 {Mathf.CeilToInt(distance)}u";
    }
    private void Position(string id,Vector3 world,Color tint,bool building,bool pinToEdge = false)
    {
        if(!markers.TryGetValue(id,out var graphic))
        {
            var go=new GameObject(id,typeof(RectTransform),typeof(MapSymbolGraphic));go.transform.SetParent(mapArea,false);
            graphic=go.GetComponent<MapSymbolGraphic>();graphic.raycastTarget=false;graphic.building=building;graphic.color=tint;
            graphic.rectTransform.sizeDelta=Vector2.one*(building?markerSize*1.5f:markerSize);markers.Add(id,graphic);
        }
        Vector3 point=mapCamera.WorldToViewportPoint(world);
        if (pinToEdge)
        {
            point.x = Mathf.Clamp(point.x, .06f, .94f);
            point.y = EdgeHeight(point.y);
        }
        bool visible=(point.z>0 || (pinToEdge && mapCamera.orthographic))&&point.x>=0&&point.x<=1&&point.y>=0&&point.y<=1;
        graphic.gameObject.SetActive(visible);
        if(visible) { graphic.rectTransform.anchorMin=graphic.rectTransform.anchorMax=new Vector2(point.x,point.y);graphic.rectTransform.anchoredPosition=Vector2.zero; }
    }

    private static float EdgeHeight(float value) => Mathf.Clamp(value, .07f, .93f);
}
