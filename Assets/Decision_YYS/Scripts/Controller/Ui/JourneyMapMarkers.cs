using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>Projects actual actors and room entrances onto the existing road camera image.</summary>
public sealed class JourneyMapMarkers : MonoBehaviour
{
    [SerializeField] private Camera mapCamera;
    [SerializeField] private MineNpcPlacement placement;
    [SerializeField] private MineCollectorProcession collectors;
    [SerializeField] private RectTransform mapArea;
    [SerializeField] private MapSymbolGraphic warning;
    [SerializeField] private TextMeshProUGUI warningText;
    [SerializeField, Min(1)] private float warningDistance=40;
    [SerializeField, Min(4)] private float markerSize=16;
    private readonly Dictionary<string,MapSymbolGraphic> markers=new Dictionary<string,MapSymbolGraphic>();
    private Player player;
    private float nextScan;
    private readonly List<Transform> actors=new List<Transform>();
    private void LateUpdate()
    {
        if(mapCamera==null||placement==null)return;
        if(player==null)player=FindFirstObjectByType<Player>();
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
            Position("room:"+place.placeId,placement.transform.TransformPoint((place.entrance+place.exit)*.5f),new Color(.12f,.4f,.9f),true);
        foreach(var actor in actors)if(actor!=null)Position("npc:"+actor.GetInstanceID(),actor.position,new Color(.25f,.95f,.68f),false);
        if(player!=null)Position("player",player.transform.position,new Color(1,.72f,.2f),false);
        var members=collectors!=null?collectors.GetComponentsInChildren<Transform>():System.Array.Empty<Transform>();
        float nearest=float.MaxValue;float collectorZ=0;
        foreach(var member in members)
        {
            if(!member.name.StartsWith("Collector_"))continue;
            Position("collector:"+member.GetInstanceID(),member.position,new Color(1,.25f,.35f),false);
            if(player!=null)
            {
                float distance=Vector3.Distance(member.position,player.transform.position);
                if(distance<nearest) { nearest=distance;collectorZ=member.position.z; }
            }
        }
        bool approaching=nearest<=warningDistance;
        warning.gameObject.SetActive(approaching);warningText.gameObject.SetActive(approaching);
        if(approaching)
        {
            warning.warningHeight=Mathf.Lerp(.08f,.34f,1-nearest/warningDistance);warning.SetVerticesDirty();
            warningText.text="징수원 접근 "+(collectorZ>player.transform.position.z?"↓":"↑")+" / "+nearest.ToString("F0");
        }
    }
    private void Position(string id,Vector3 world,Color tint,bool building)
    {
        if(!markers.TryGetValue(id,out var graphic))
        {
            var go=new GameObject(id,typeof(RectTransform),typeof(MapSymbolGraphic));go.transform.SetParent(mapArea,false);
            graphic=go.GetComponent<MapSymbolGraphic>();graphic.raycastTarget=false;graphic.building=building;graphic.color=tint;
            graphic.rectTransform.sizeDelta=Vector2.one*(building?markerSize*1.5f:markerSize);markers.Add(id,graphic);
        }
        Vector3 point=mapCamera.WorldToViewportPoint(world);
        bool visible=point.z>0&&point.x>=0&&point.x<=1&&point.y>=0&&point.y<=1;
        graphic.gameObject.SetActive(visible);
        if(visible) { graphic.rectTransform.anchorMin=graphic.rectTransform.anchorMax=new Vector2(point.x,point.y);graphic.rectTransform.anchoredPosition=Vector2.zero; }
    }
}
