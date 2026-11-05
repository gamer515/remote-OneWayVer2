using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

[Serializable] public sealed class MineLearningJob
{
    public int ability, reward;
    public float duration, remaining, transit;
}
[Serializable] public sealed class MineLearningProgress
{
    public List<MineLearningJob> active = new List<MineLearningJob>(), queued = new List<MineLearningJob>();
    public int sequence;
}

/// <summary>FIFO learning, paid once on entry; capacities and durations are authored in the Inspector.</summary>
public sealed class MineLearningStation : MonoBehaviour
{
    [Serializable] public sealed class CapacityStep
    {
        [Min(0)] public int knowledgeCoins;
        [Min(1)] public int simultaneous = 1;
        [Min(0)] public int waiting = 5;
    }
    [Header("Knowledge thresholds: only the initial row is defined until balance is approved")]
    [SerializeField] private CapacityStep[] capacities = { new CapacityStep() };
    [Header("Trial cost / reward per ability")]
    [SerializeField] private int[] costs = {1,1,1,1}, rewards = {2,2,2,2};
    [Header("Duration in seconds")]
    [SerializeField, Min(.1f)] private float minimumSeconds = 5, maximumSeconds = 5;
    [SerializeField] private bool randomDuration;
    [SerializeField, Min(.05f)] private float deliverySeconds = .6f;
    [Header("Station view")]
    [SerializeField] private bool showFlatBooks = true;
    [SerializeField] private UnityEngine.UI.Button[] abilityButtons;
    [SerializeField] private TextMeshProUGUI[] balances;
    [SerializeField] private TextMeshProUGUI status;
    [SerializeField] private RectTransform queueArea, activeArea, bookTemplate;
    [SerializeField] private RectTransform[] deliveryPath;
    private readonly Color[] colors = {new Color(.1f,.36f,.8f),new Color(.75f,.13f,.2f),new Color(.9f,.65f,.1f),new Color(.04f,.58f,.62f)};
    private readonly List<RectTransform> books = new List<RectTransform>();
    private MineLearningProgress progress;
    private Func<int[]> wallet;
    private Func<int,int,bool> charge;
    private Action<int,int> grant;
    private Action save;
    private int seed;
    private bool paused;
    private string notice;
    public MineLearningProgress Progress => progress;
    public float DeliverySeconds => deliverySeconds;
    public CapacityStep Capacity => capacities.Where(c=>c.knowledgeCoins <= wallet()[2]).OrderBy(c=>c.knowledgeCoins).LastOrDefault() ?? new CapacityStep();
    public void Initialize(MineLearningProgress state, int runSeed, Func<int[]> getWallet, Func<int,int,bool> spend, Action<int,int> reward, Action persist)
    {
        progress=state; seed=runSeed; wallet=getWallet; charge=spend; grant=reward; save=persist;paused=false;notice=null;
        for(int i=0;i<abilityButtons.Length;i++) { int ability=i; abilityButtons[i].onClick.RemoveAllListeners(); abilityButtons[i].onClick.AddListener(()=>Request(ability)); }
        bookTemplate.gameObject.SetActive(false); Rebuild();
    }
    public bool Request(int ability)
    {
        if(progress==null || paused || ability<0 || ability>=4) return false;
        var capacity=Capacity;
        if(progress.active.Count>=capacity.simultaneous && progress.queued.Count>=capacity.waiting) { notice="학습 대기열이 가득 찼습니다."; return false; }
        int cost=Mathf.Max(0,costs[ability]);
        if(wallet()[ability]<cost) { notice="선택한 능력 코인이 부족합니다."; return false; }
        if(!charge(ability,cost)) return false;
        float low=Mathf.Max(.1f,minimumSeconds), high=Mathf.Max(low,maximumSeconds);
        float seconds=randomDuration ? Mathf.Lerp(low,high,(float)MineChapterSettings.Roll(seed,progress.sequence++,"learning")) : low;
        progress.queued.Add(new MineLearningJob {ability=ability,reward=Mathf.Max(0,rewards[ability]),duration=seconds,remaining=seconds});
        notice=null; FillSlots(); Rebuild(); save(); return true;
    }
    private bool FillSlots()
    {
        bool changed=false;
        while(progress.active.Count<Capacity.simultaneous && progress.queued.Count>0)
        {
            var job=progress.queued[0]; progress.queued.RemoveAt(0); job.transit=deliverySeconds; progress.active.Add(job); changed=true;
        }
        return changed;
    }
    public void Tick(float seconds, bool stop)
    {
        if(progress==null) return;
        paused=stop; bool changed=false;
        if(!stop)
        {
            for(int i=progress.active.Count-1;i>=0;i--)
            {
                var job=progress.active[i];
                if(job.transit>0) { job.transit=Mathf.Max(0,job.transit-seconds); continue; }
                job.remaining=Mathf.Max(0,job.remaining-seconds);
                if(job.remaining>0) continue;
                progress.active.RemoveAt(i); grant(job.ability,job.reward); changed=true;
            }
            changed |= FillSlots();
        }
        if(changed) { Rebuild(); save(); }
        for(int i=0;i<balances.Length;i++) balances[i].text=wallet()[i].ToString();
        for(int i=0;i<abilityButtons.Length;i++) abilityButtons[i].interactable=!stop;
        status.text=(stop?"요구 수행 중 / 학습 일시 정지":"학습 "+progress.active.Count+"/"+Capacity.simultaneous+" / 대기 "+progress.queued.Count+"/"+Capacity.waiting)+
            (notice==null?"":"\n"+notice);
        for(int i=0;showFlatBooks && i<progress.active.Count;i++)
        {
            var job=progress.active[i];var rect=books[i];
            rect.GetComponentInChildren<TextMeshProUGUI>().text=job.transit>0?"이동 중":job.remaining.ToString("F1")+"초";
            if(job.transit>0 && deliveryPath.Length>1)
            {
                float t=(1-job.transit/deliverySeconds)*(deliveryPath.Length-1);int segment=Mathf.Min((int)t,deliveryPath.Length-2);
                rect.position=Vector3.Lerp(deliveryPath[segment].position,deliveryPath[segment+1].position,t-segment);
            }
            else PlaceActive(rect,i);
        }
    }
    private void Rebuild()
    {
        foreach(var book in books) if(book!=null) Destroy(book.gameObject); books.Clear();
        if(!showFlatBooks) return;
        foreach(var job in progress.active.Concat(progress.queued))
        {
            var book=Instantiate(bookTemplate,transform); book.gameObject.SetActive(true);book.name="LearningBook_"+job.ability;
            book.GetComponent<UnityEngine.UI.Image>().color=colors[job.ability]; books.Add(book);
        }
        for(int i=0;i<books.Count;i++)
        {
            if(i<progress.active.Count) PlaceActive(books[i],i);
            else { int index=i-progress.active.Count;books[i].SetParent(queueArea,false);books[i].anchorMin=books[i].anchorMax=new Vector2(.5f,0);books[i].anchoredPosition=new Vector2(0,18+index*26);books[i].sizeDelta=new Vector2(32,21);books[i].GetComponentInChildren<TextMeshProUGUI>().text=(index+1).ToString(); }
        }
    }
    private void PlaceActive(RectTransform rect,int index)
    {
        int count=Mathf.Max(Capacity.simultaneous,progress.active.Count),columns=count>2?2:1,rows=Mathf.CeilToInt((float)count/columns);
        float width=activeArea.rect.width/columns,height=activeArea.rect.height/rows;
        rect.SetParent(activeArea,false);rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);
        rect.anchoredPosition=new Vector2((index%columns+.5f)*width-activeArea.rect.width*.5f,activeArea.rect.height*.5f-(index/columns+.5f)*height);
        rect.sizeDelta=new Vector2(Mathf.Min(60,width-8),Mathf.Min(24,height-4));
    }
}
