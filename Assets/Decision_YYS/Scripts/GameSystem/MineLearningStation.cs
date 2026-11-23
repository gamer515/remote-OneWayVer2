using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

public readonly struct LearningComboReward
{
    public readonly string Name;
    public readonly int[] Coins;

    public LearningComboReward(string name, int[] coins)
    {
        Name = name;
        Coins = coins;
    }
}

[Serializable] public sealed class MineLearningJob
{
    public int ability, reward;
    public float duration, remaining, transit;
    public bool onBoard;
    public bool requiresBoard;
    public string bookId, bookName;
    public float boardU, boardV;
    public int boardIndex;
    public float generationRemaining;
    public bool started;
    public int studyAttempt;
    public bool comboStudy;
    public int stackId;
    public int stackOrder;
    public float stackYaw;
    [NonSerialized] public bool held;
}
[Serializable] public sealed class ReadingComboProgress
{
    public string id;
    public int payouts;
    public List<string> unusedBooks = new List<string>();
}
[Serializable] public sealed class LearningBoardCoin
{
    public int ability;
    public Vector2 position;
}

[Serializable] public sealed class MineLearningProgress
{
    public List<MineLearningJob> active = new List<MineLearningJob>(), queued = new List<MineLearningJob>();
    public int sequence;
    public MineLearningJob producing;
    public List<MineLearningJob> stored = new List<MineLearningJob>();
    public List<string> mastered = new List<string>(), rewardedCombos = new List<string>();
    public List<StudySkillValue> skills = new List<StudySkillValue>();
    public int[] stock, refills, nextBook, lastStudyDay;
    public int day = -1, attemptsToday;
    public int successfulStudies;
    public bool studySuccessCountInitialized;
    public float clockDays;
    public List<string> comboSuccesses = new List<string>();
    public List<string> pendingReading = new List<string>();
    public List<string> completedReading = new List<string>();
    public List<string> claimedReadingCombos = new List<string>();
    public List<ReadingComboProgress> readingComboProgress = new List<ReadingComboProgress>();
    public string readingResult;
    public Vector3 comboBoardPoint;
    public List<LearningBoardCoin> boardRewardCoins = new List<LearningBoardCoin>();
}

/// <summary>FIFO learning, paid once on entry; capacities and durations are authored in the Inspector.</summary>
public sealed partial class MineLearningStation : MonoBehaviour
{
    [Serializable] public sealed class BookDefinition
    {
        public string id, name;
        public int ability, requiredKnowledge, cost, reward;
        public float seconds;
        public string author, skill;
        public int requiredAbility, skillReward;
        public float generationSeconds;
    }

    [Serializable] public sealed class BookCatalog { public BookDefinition[] books; }

    [Serializable] public sealed class LearningSettings
    {
        public CapacityStep[] capacities;
        public bool randomDuration;
        public float minimumSeconds, maximumSeconds, deliverySeconds;
    }
    [Serializable] public sealed class CapacityStep
    {
        [Min(0)] public int knowledgeCoins;
        [Min(1)] public int simultaneous = 1;
        [Min(0)] public int waiting = 5;
    }
    [Header("Knowledge thresholds: only the initial row is defined until balance is approved")]
    [SerializeField] private CapacityStep[] capacities = { new CapacityStep() };
    [Header("Trial cost / reward per ability")]
    [SerializeField] private int[] costs = {1,1,1,1}, rewards = {1,1,1,1};
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
    private Action<int[], IReadOnlyList<LearningComboReward>> grant;
    private Action save;
    private int seed;
    private bool paused;
    private bool studyPaused;
    private string noticeText;
    private string notice
    {
        get => noticeText;
        set
        {
            noticeText = value;
            noticeUntil = Time.unscaledTime + 4;
        }
    }
    private float noticeUntil;
    private BookDefinition[] authoredBooks;
    private LearningSettings authoredLearning;
    public MineLearningProgress Progress => progress;
    public float DeliverySeconds => study?.deliverySeconds ?? authoredLearning?.deliverySeconds ?? deliverySeconds;
    public bool CanMoveBooks => progress != null && wallet != null && !paused;
    public bool CanStudyBooks => CanMoveBooks && !studyPaused;
    public Func<int[]> DisplayCoinCounts { private get; set; }
    public int[] CurrentCoins => DisplayCoinCounts?.Invoke() ?? wallet?.Invoke();
    public string GetBookDisplayName(MineLearningJob job)
    {
        if (IsStrengthTutorial(job)) return "힘 책";
        string id = CurrentReadingBookId(job);
        if (!string.IsNullOrWhiteSpace(id))
        {
            var definition = authoredBooks?.FirstOrDefault(book => book.id == id);
            if (!string.IsNullOrWhiteSpace(definition?.name)) return definition.name;
        }
        if (!string.IsNullOrWhiteSpace(job.bookName)) return job.bookName;
        return $"{AbilityName(job.ability)} 책";
    }
    public int TodayLimit => UsesStudyRules && wallet != null ? DailyLimit(CurrentCoins[2]) : 0;
    private static bool IsReadingPlacement(MineLearningJob job) => job.onBoard && job.boardIndex == 2;
    public bool IsStudying(MineLearningJob job) => CanStudyBooks && job != null && job.onBoard && !job.held &&
        job.transit <= 0 && (study == null ? !job.requiresBoard || job.boardIndex == 1 :
            IsReadingPlacement(job) && job.started && SharesStudySession(job, CurrentStudy) && CanRead(job));
    public void RememberPlacement(MineLearningJob job, float u, float v, int boardIndex = 0)
    {
        if(progress == null || !progress.active.Contains(job)) return;
        job.stackId = job.stackOrder = 0;
        job.onBoard=true; job.boardU=Mathf.Clamp01(u); job.boardV=Mathf.Clamp01(v); job.boardIndex=boardIndex; save?.Invoke();
    }
    public CapacityStep Capacity
    {
        get
        {
            var steps = study?.capacities ?? authoredLearning?.capacities ?? capacities;
            int knowledge = CurrentCoins?[2] ?? 0;
            var step = steps.Where(c => c.knowledgeCoins <= knowledge)
                .OrderBy(c => c.knowledgeCoins).LastOrDefault() ?? new CapacityStep();
            if (FirstStudyDay && step.simultaneous < 2)
                return new CapacityStep { knowledgeCoins = step.knowledgeCoins, simultaneous = 2, waiting = step.waiting };
            return step;
        }
    }

    public void ConfigureContent(BookCatalog books, LearningSettings settings)
    {
        study = null;
        authoredBooks = books?.books;
        authoredLearning = settings;
    }
    public void Initialize(MineLearningProgress state, int runSeed, Func<int[]> getWallet, Func<int,int,bool> spend,
        Action<int[], IReadOnlyList<LearningComboReward>> reward, Action persist)
    {
        progress=state; seed=runSeed; wallet=getWallet; charge=spend; grant=reward; save=persist;paused=studyPaused=false;notice=null;
        CloseReadingPreview();
        readingBooks[StrengthTutorialBook] = ReadContent("Reading/StrengthTutorial");
        progress.active ??= new List<MineLearningJob>();
        progress.queued ??= new List<MineLearningJob>();
        InitializeStudyState();
        if (study != null && status != null)
        {
            status.gameObject.SetActive(false);
            status.raycastTarget = false;
            status.rectTransform.anchorMin = status.rectTransform.anchorMax = new Vector2(.5f, .5f);
            status.rectTransform.anchoredPosition = new Vector2(-305, -151);
            status.rectTransform.sizeDelta = new Vector2(580, 44);
            status.fontSize = 19;
            status.color = new Color(.24f, .15f, .08f);
        }
        for(int i=0;i<abilityButtons.Length;i++) { int ability=i; abilityButtons[i].onClick.RemoveAllListeners(); abilityButtons[i].onClick.AddListener(()=>Request(ability)); }
        bookTemplate.gameObject.SetActive(false); Rebuild();
    }
    public bool Request(int ability)
    {
        if(!CanMoveBooks || ability<0 || ability>=4) return false;
        if(study != null) return ProduceBook(ability);
        var capacity=Capacity;
        if(progress.active.Count>=capacity.simultaneous && progress.queued.Count>=capacity.waiting) { notice="학습 대기열이 가득 찼습니다."; return false; }
        var book = authoredBooks?.Where(item => item.ability == ability && item.requiredKnowledge <= wallet()[2])
            .OrderByDescending(item => item.requiredKnowledge).FirstOrDefault();
        if (authoredBooks != null && book == null) { notice = "읽을 수 있는 책이 없습니다."; return false; }
        int cost = book?.cost ?? Mathf.Max(0, costs[ability]);
        if(wallet()[ability]<cost) { notice="선택한 능력 코인이 부족합니다."; return false; }
        if(!charge(ability,cost)) return false;
        float low = Mathf.Max(.1f, authoredLearning?.minimumSeconds ?? minimumSeconds);
        float high = Mathf.Max(low, authoredLearning?.maximumSeconds ?? maximumSeconds);
        bool random = authoredLearning?.randomDuration ?? randomDuration;
        float seconds = random ? Mathf.Lerp(low, high, (float)MineChapterSettings.Roll(seed, progress.sequence++, "learning")) : book?.seconds ?? low;
        progress.queued.Add(new MineLearningJob
        {
            ability = ability,
            reward = book?.reward ?? Mathf.Max(0, rewards[ability]),
            duration = seconds,
            remaining = seconds,
            bookId = book?.id,
            bookName = book?.name,
            requiresBoard = book != null || ability == 0
        });
        notice=null; FillSlots(); Rebuild(); save(); return true;
    }
    private bool FillSlots()
    {
        bool changed=false;
        while (progress.queued.Count > 0 &&
            (study != null ? CanDeliverStudyBook : progress.active.Count < Capacity.simultaneous))
        {
            var job=progress.queued[0]; progress.queued.RemoveAt(0); job.transit=DeliverySeconds; progress.active.Add(job); changed=true;
        }
        return changed;
    }
    public void Tick(float seconds, bool stop, bool stopStudying = false, float? calendarDays = null)
    {
        if(progress==null || wallet==null) return;
        paused=stop; studyPaused=stopStudying; bool changed=false;
        if(study != null)
            changed = TickStudy(seconds, stop, calendarDays);
        else if(!stop)
        {
            for(int i=progress.active.Count-1;i>=0;i--)
            {
                var job=progress.active[i];
                if(job.held) continue;
                if(job.transit>0) { job.transit=Mathf.Max(0,job.transit-seconds); continue; }
                bool tutorial = IsStrengthTutorial(job);
                if ((job.requiresBoard || tutorial) && (!job.onBoard || job.boardIndex != 1)) continue;
                if (tutorial) PrepareStrengthTutorial(job);
                job.remaining=Mathf.Max(0,job.remaining-seconds);
                if(job.remaining>0) continue;
                if (tutorial && OpenCompletedReading(StrengthTutorialBook))
                {
                    progress.active.RemoveAt(i);
                    changed = true;
                    continue;
                }
                var book = authoredBooks?.FirstOrDefault(item => item.id == job.bookId);
                int reward = book?.reward ?? (string.IsNullOrEmpty(job.bookId) ? Mathf.Max(0, rewards[job.ability]) : job.reward);
                progress.active.RemoveAt(i); GrantSingleAbility(job.ability, reward); changed=true;
            }
            changed |= FillSlots();
        }
        if(changed) { Rebuild(); save(); }
        var amounts = CurrentCoins;
        for(int i=0;i<balances.Length;i++) balances[i].text=amounts[i].ToString();
        for(int i=0;i<abilityButtons.Length;i++) abilityButtons[i].interactable=!stop;
        if (study == null)
            status.text = (stop ? "요구 수행 중 / 학습 일시 정지" :
                $"학습 {progress.active.Count}/{Capacity.simultaneous} / 대기 {progress.queued.Count}/{Capacity.waiting}") +
                (notice == null ? "" : "\n" + notice);
        else
        {
            bool showNotice = notice != null && Time.unscaledTime < noticeUntil;
            status.gameObject.SetActive(showNotice);
            if (showNotice) status.text = notice;
        }
        for(int i=0;showFlatBooks && i<progress.active.Count;i++)
        {
            var job=progress.active[i];var rect=books[i];
            rect.GetComponentInChildren<TextMeshProUGUI>().text=job.transit>0?"이동 중":job.remaining.ToString("F1")+"초";
            if(job.transit>0 && deliveryPath.Length>1)
            {
                float t=(1-job.transit/DeliverySeconds)*(deliveryPath.Length-1);int segment=Mathf.Min((int)t,deliveryPath.Length-2);
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
