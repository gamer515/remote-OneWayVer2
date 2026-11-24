using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[Serializable] public sealed class StudySkillValue
{
    public string id;
    public int value;
}

public sealed partial class MineLearningStation
{
    [Serializable] public sealed class StudyRules
    {
        public int startingStock, refillStock, refillBase, refillIncrease, decayCoins;
        public float daySeconds, deliverySeconds;
        public CapacityStep[] capacities;
        public StudyTier[] intelligence;
        [Newtonsoft.Json.JsonProperty("enduranceThresholds")]
        public int[] dailyIntelligenceThresholds;
        public int[] dailyLimits;
        public int dailyLimitBonus = 2;
        public StudySkill[] skills;
        public StudyCombo[] combos;
    }

    [Serializable] public sealed class StudyTier
    {
        public int threshold, decayDays;
        public float success;
    }

    [Serializable] public sealed class StudySkill
    {
        public string id, name, description;
        public int ability;
    }

    [Serializable] public sealed class StudyCombo
    {
        public string id, name, skill;
        public string[] books;
        public int[] coins;
        public int skillReward;
    }

    private StudyRules study;
    [Header("Book production and storage")]
    [SerializeField, Min(0)] private int extraProductionQueueSlots = 5;
    [SerializeField, Min(1)] private int looseBookStorageLimit = 8;
    private float studyDaySeconds;
    private float nextStudySave;
    public bool UsesStudyRules => study != null;
    public int ProductionQueueLimit => Capacity.waiting + (UsesStudyRules ? extraProductionQueueSlots : 0);
    public int LooseBookStorageLimit => Mathf.Max(Capacity.simultaneous, looseBookStorageLimit);
    private bool CanDeliverStudyBook => progress.active.All(job => job.onBoard) &&
        progress.active.Count(job => !IsReadingPlacement(job) && !IsStoredBook(job) && !job.comboStudy) < LooseBookStorageLimit;
    public string StudyResult { get; private set; }
    public bool StudyResultSucceeded { get; private set; }
    public float StudyResultUntil { get; private set; }
    public bool IsDailyLimitBlocking => study != null && CanStudyBooks &&
        progress.attemptsToday >= TodayLimit &&
        progress.active.Any(job => job.onBoard && job.stackOrder == 0 && (job.boardIndex == 1 || IsReadingPlacement(job)) &&
            !job.started && !job.held && CanRead(job));
    public int Stock(int ability) => progress?.stock?[ability] ?? 0;
    public int RefillCost(int ability) => study == null ? 0 :
        study.refillBase + (progress?.refills?[ability] ?? 0) * study.refillIncrease;
    public bool HasComboStudy => progress.active.Any(job => job.comboStudy);
    public bool CanBeginStudySession => CanStudyBooks && CurrentStudy == null &&
        progress.pendingReading.Count == 0 && ReadingPreview == null;

    private MineLearningJob CurrentStudy => progress.active.Find(job => job.started && IsReadingPlacement(job)) ??
        progress.active.Find(IsReadingPlacement);

    private static bool SharesStudySession(MineLearningJob job, MineLearningJob current) =>
        current != null && (job == current || current.comboStudy && job.comboStudy);

    public void ConfigureStudy(BookCatalog catalog, StudyRules rules, float calendarDaySeconds, float calendarDays)
    {
        study = catalog?.books?.Length > 0 ? rules : null;
        authoredBooks = catalog?.books;
        if (study != null) ConfigureReading();
        studyDaySeconds = calendarDaySeconds > 0 ? calendarDaySeconds : study?.daySeconds ?? 0;
        usesChapterCalendar = calendarDaySeconds > 0;
        initialCalendarDays = Mathf.Max(0, calendarDays);
    }

    private float initialCalendarDays;
    private bool usesChapterCalendar;

    private bool InitializeStudyState()
    {
        if (study == null) return false;
        nextStudySave = 0;
        StudyResult = null;
        progress.stored ??= new List<MineLearningJob>();
        // Older saves can deserialize an empty producing slot as a default job.
        if (progress.producing is MineLearningJob slot && string.IsNullOrEmpty(slot.bookId) &&
            string.IsNullOrEmpty(slot.bookName) && slot.duration <= 0 && slot.generationRemaining <= 0)
            progress.producing = null;
        var excessOutletBooks = progress.active.Where(job => !job.onBoard).Skip(1).ToArray();
        foreach (var job in excessOutletBooks)
        {
            job.transit = 0;
            progress.active.Remove(job);
        }
        progress.queued.InsertRange(0, excessOutletBooks);
        foreach (var job in progress.active.Concat(progress.queued).Concat(progress.stored).Append(progress.producing))
        {
            if (job == null) continue;
            PrepareStrengthTutorial(job);
            job.bookId = CurrentReadingBookId(job);
            job.bookName = GetBookDisplayName(job);
        }
        bool migratedProduction = progress.producing != null;
        if (migratedProduction)
        {
            var book = progress.producing;
            progress.producing = null;
            book.generationRemaining = 0f;
            progress.queued.Add(book);
        }
        progress.pendingReading ??= new List<string>();
        progress.pendingReading.RemoveAll(id => string.IsNullOrEmpty(id) || !readingBooks.ContainsKey(id));
        readingDeferred = progress.pendingReading.Count > 0;
        progress.readingResult = null;
        progress.completedReading ??= new List<string>();
        progress.claimedReadingCombos ??= new List<string>();
        progress.readingComboProgress ??= new List<ReadingComboProgress>();
        progress.mastered ??= new List<string>();
        if (!progress.studySuccessCountInitialized)
        {
            progress.successfulStudies = progress.mastered.Count;
            progress.studySuccessCountInitialized = true;
        }
        progress.rewardedCombos ??= new List<string>();
        progress.comboSuccesses ??= new List<string>();
        progress.skills ??= new List<StudySkillValue>();
        if (progress.stock?.Length != 4) progress.stock = Enumerable.Repeat(study.startingStock, 4).ToArray();
        if (progress.refills?.Length != 4) progress.refills = new int[4];
        if (progress.nextBook?.Length != 4) progress.nextBook = new int[4];
        progress.clockDays = usesChapterCalendar ? initialCalendarDays : Mathf.Max(progress.clockDays, initialCalendarDays);
        if (progress.lastStudyDay?.Length != 4)
            progress.lastStudyDay = Enumerable.Repeat(Mathf.FloorToInt(progress.clockDays), 4).ToArray();
        RefreshDay();
        if (NeedsStrengthTutorial)
        {
            var tutorial = new MineLearningJob
            {
                bookId = StrengthTutorialBook, requiresBoard = true,
                onBoard = true, boardIndex = 0, boardU = .26f, boardV = .46f
            };
            PrepareStrengthTutorial(tutorial);
            progress.active.Add(tutorial);
        }
        RestoreBookshelf();
        return migratedProduction;
    }

    private bool RefreshDay(bool renewAllowance = true)
    {
        int day = Mathf.FloorToInt(progress.clockDays);
        if (day == progress.day) return false;
        progress.day = day;
        if (renewAllowance)
        {
            progress.attemptsToday = 0;
            notice = null;
        }
        Array.Clear(progress.refills, 0, progress.refills.Length);
        return true;
    }

    public void RecoverStudyAfterRest(float? skippedToCalendarDay)
    {
        if (study == null || progress == null) return;
        if (skippedToCalendarDay.HasValue)
        {
            // The rest skipped the night. Advance the calendar without granting a second refill.
            progress.clockDays = skippedToCalendarDay.Value;
            RefreshDay(renewAllowance: false);
        }
        int limit = TodayLimit;
        int recovered = (limit + 1) / 2;
        progress.attemptsToday = Mathf.Max(0, Mathf.Min(progress.attemptsToday, limit) - recovered);
        notice = null;
    }

    private bool FirstStudyDay => study != null && progress?.day == 0 && readingBooks.Count > 0;

    private int DailyLimit(int intelligence)
    {
        int index = Array.FindLastIndex(study.dailyIntelligenceThresholds, threshold => intelligence >= threshold);
        int limit = study.dailyLimits[Mathf.Max(0, index)];
        return (FirstStudyDay ? Mathf.Max(2, limit) : limit) + Mathf.Max(0, study.dailyLimitBonus);
    }

    private StudyTier Intelligence(int knowledge) =>
        study.intelligence.LastOrDefault(tier => knowledge >= tier.threshold) ?? study.intelligence[0];

    private bool CanRead(BookDefinition book, int[] amounts) =>
        (FirstStudyDay && (book.id == FirstComboBook || book.id == SecondComboBook)) ||
        (amounts[2] >= book.requiredKnowledge && amounts[book.ability] >= book.requiredAbility);

    private bool CanRead(MineLearningJob job)
    {
        var definition = authoredBooks.FirstOrDefault(book => book.id == job.bookId);
        return definition == null || CanRead(definition, wallet());
    }

    private bool Explain(string message)
    {
        notice = message;
        return false;
    }

    private bool ProduceBook(int ability)
    {
        if (Stock(ability) <= 0) return Explain("책이 없습니다. 해당 능력 코인을 투입구에 넣으세요.");
        int queueLimit = ProductionQueueLimit;
        if (progress.queued.Count >= queueLimit &&
            !(queueLimit == 0 && progress.queued.Count == 0 && CanDeliverStudyBook))
            return Explain("생성 대기열이 가득 찼습니다.");

        var amounts = wallet();
        var available = authoredBooks.Where(entry => ProductionAbility(entry) == ability && CanRead(entry, amounts)).ToArray();
        if (available.Length == 0) return Explain("이 분야의 책을 읽으려면 지능이나 해당 능력이 더 필요합니다.");
        var book = available[progress.nextBook[ability] % available.Length];
        progress.nextBook[ability]++;
        progress.stock[ability]--;
        var job = new MineLearningJob
        {
            ability = ability, reward = book.reward, duration = book.seconds, remaining = book.seconds,
            bookId = book.id, bookName = book.name,
            requiresBoard = true
        };
        AssignBookNumber(job);
        DiscoverBook(job.bookId);
        progress.queued.Add(job);
        notice = null;
        save?.Invoke();
        return true;
    }

    public bool Refill(int ability, int insertedCoins = 1)
    {
        if (!UsesStudyRules || !CanMoveBooks || ability < 0 || ability >= 4) return false;
        int cost = RefillCost(ability);
        if (insertedCoins < cost) return Explain($"코인 {cost}개를 넣어야 리필됩니다.");
        int available = wallet()[ability];
        if (available < cost || (ability == 0 && available == cost))
            return Explain("리필할 능력 코인이 부족합니다.");
        if (!charge(ability, cost)) return false;
        progress.stock[ability] += study.refillStock;
        progress.refills[ability]++;
        notice = $"책 {study.refillStock}권 리필 / 코인 {cost}개 사용";
        save?.Invoke();
        return true;
    }

    internal const int BookshelfCompartments = 6;
    internal const int BookshelfSlots = BookshelfCompartments * 3;
    private const int LegacyShelfBoard = 3, SingleBookShelfBoard = 4, BookshelfBoard = 5;

    internal static bool IsStoredBook(MineLearningJob job) =>
        job.boardIndex is LegacyShelfBoard or SingleBookShelfBoard or BookshelfBoard;

    internal static int BookshelfSlot(MineLearningJob job)
    {
        int maxSlot = job.boardIndex switch
        {
            LegacyShelfBoard => 4,
            SingleBookShelfBoard => BookshelfCompartments - 1,
            _ => BookshelfSlots - 1
        };
        return Mathf.Clamp(Mathf.RoundToInt(job.boardU * maxSlot), 0, maxSlot);
    }

    private void RestoreBookshelf()
    {
        progress.discoveredBookIds ??= new List<string>();
        bool changed = false;
        foreach (var job in progress.active.Where(job => !job.comboStudy &&
                     job.boardIndex is LegacyShelfBoard or SingleBookShelfBoard))
        {
            job.boardU = BookshelfSlot(job) / (BookshelfSlots - 1f);
            job.boardIndex = BookshelfBoard;
            changed = true;
        }
        foreach (string id in progress.active.Concat(progress.queued).Concat(progress.stored)
                     .Select(job => job.bookId).Concat(progress.completedReading).Concat(progress.mastered)
                     .Concat(progress.pendingReading))
            changed |= DiscoverBook(id);
        if (changed) save?.Invoke();
    }

    internal bool CanStoreBook(MineLearningJob book, int slot) => CanMoveBooks && !book.comboStudy &&
        (uint)slot < BookshelfSlots && progress.active.Contains(book) &&
        !progress.active.Any(job => job != book && IsStoredBook(job) && BookshelfSlot(job) == slot);

    internal bool TryStoreBook(MineLearningJob book, int slot)
    {
        if (!CanStoreBook(book, slot)) return false;
        RememberPlacement(book, slot / (BookshelfSlots - 1f), .5f, BookshelfBoard);
        return true;
    }

    internal IEnumerable<IGrouping<int, MineLearningJob>> BoardStacks() => progress.active
        .Where(job => job.stackOrder > 0).OrderBy(job => job.stackOrder).GroupBy(job => job.stackId);

    internal bool TryStackBook(MineLearningJob book, MineLearningJob target)
    {
        if (!UsesStudyRules || book == target || book.started || book.comboStudy || target.started ||
            target.held || target.comboStudy || !target.onBoard || target.boardIndex > 1 ||
            !progress.active.Contains(book) || !progress.active.Contains(target)) return false;
        if (target.stackOrder == 0)
        {
            target.stackId = progress.active.Max(job => job.stackId) + 1;
            AssignStackOrder(target, 1);
        }
        int top = progress.active.Where(job => job.stackId == target.stackId && job.stackOrder > 0)
            .Max(job => job.stackOrder);
        book.stackId = target.stackId;
        AssignStackOrder(book, top + 1);
        book.onBoard = true;
        book.boardIndex = target.boardIndex;
        book.boardU = target.boardU;
        book.boardV = target.boardV;
        save?.Invoke();
        return true;
    }

    private static void AssignStackOrder(MineLearningJob book, int order)
    {
        book.stackOrder = order;
        if (book.stackYaw == 0f) book.stackYaw = Mathf.Lerp(-10f, 10f, Mathf.Repeat(order * .618034f, 1f));
    }

    internal MineLearningJob[] ComboSubmission(int stackId) => progress.active
        .Where(job => job.stackOrder > 0 && job.stackId == stackId)
            .OrderBy(job => job.stackOrder).ToArray();

    internal bool CanSubmitCombo(MineLearningJob[] batch)
    {
        if (!UsesStudyRules || !CanBeginStudySession || HasComboStudy || ReadingResult != null ||
            batch.Length < 2 || batch.Length > TodayRemaining ||
            batch.Any(job => job.started || job.held || !CanRead(job))) return false;
        return NumberComboReward(batch) != null || MatchStackCombo(batch, hasAbilitySequence: false) != null ||
            study.combos.Any(combo => !progress.rewardedCombos.Contains(combo.id) &&
            combo.books.All(id => batch.Any(job => job.bookId == id))) ||
            readingCombos.Any(combo => !IsAbilityCombo(combo) && CanCompleteReadingCombo(combo, batch));
    }

    internal bool SubmitCombo(Vector3 pickupPoint, int stackId)
    {
        var batch = ComboSubmission(stackId);
        if (!CanSubmitCombo(batch)) return false;
        progress.numberComboRewardId = (string)NumberComboReward(batch)?["id"];
        progress.numberComboChoicesRemaining = progress.numberComboRewardId != null ? batch.Length : 0;
        progress.numberComboBookCount = progress.numberComboChoicesRemaining;
        progress.colorComboPattern = MatchStackCombo(batch, progress.numberComboRewardId != null);
        progress.colorComboChoicesRemaining = progress.colorComboPattern != null ? batch.Length : 0;
        progress.comboSuccesses.Clear();
        progress.comboBoardPoint = pickupPoint;
        foreach (var job in batch) job.comboStudy = true;
        save?.Invoke();
        return true;
    }

    internal static bool IsQueuedComboBook(MineLearningJob job) =>
        job.comboStudy && (job.stackOrder > 0 || job.boardIndex == LegacyShelfBoard);

    internal MineLearningJob[] NextComboWave()
    {
        if (!HasComboStudy || !CanBeginStudySession || ReadingResult != null ||
            progress.active.Any(job => job.comboStudy && !IsQueuedComboBook(job)))
            return Array.Empty<MineLearningJob>();
        int slots = Mathf.Min(Capacity.simultaneous, TodayRemaining);
        return progress.active.Where(IsQueuedComboBook)
            .OrderBy(job => job.stackOrder).ThenBy(job => job.boardU).Take(slots).ToArray();
    }

    private bool TickStudy(float seconds, bool stop, float? calendarDays)
    {
        progress.clockDays = calendarDays ?? (progress.clockDays + seconds / studyDaySeconds);
        bool changed = RefreshDay();
        var amounts = wallet();
        int decayDays = Intelligence(amounts[2]).decayDays;
        for (int ability = 0; ability < 4; ability++)
        {
            int periods = (progress.day - progress.lastStudyDay[ability]) / decayDays;
            if (periods <= 0) continue;
            // Leave a living player's last health coin available for movement and recovery.
            int floor = ability == 0 && amounts[ability] > 0 ? 1 : 0;
            int loss = (int)Math.Min(Math.Max(0, amounts[ability] - floor), (long)periods * study.decayCoins);
            if (loss > 0) GrantSingleAbility(ability, -loss);
            progress.lastStudyDay[ability] += periods * decayDays;
            changed = true;
        }
        if (!stop)
        {
            var currentStudy = CurrentStudy;
            for (int i = 0; i < progress.active.Count;)
            {
                var job = progress.active[i];
                if (job.transit > 0) job.transit = Mathf.Max(0, job.transit - seconds);
                else if (!studyPaused && !job.held && IsReadingPlacement(job) &&
                    SharesStudySession(job, currentStudy) &&
                    (job.comboStudy || job.started || progress.pendingReading.Count == 0))
                {
                    PrepareStrengthTutorial(job);
                    if (!CanRead(job)) notice = $"{GetBookDisplayName(job)} / 읽기 능력치 부족";
                    else if (job.started || TodayRemaining > 0)
                    {
                        if (!job.started)
                        {
                            ApplyReadingDuration(job);
                            job.started = true;
                            job.studyAttempt = ++progress.sequence;
                            progress.attemptsToday++;
                            notice = null;
                            changed = true;
                        }
                        job.remaining = Mathf.Max(0, job.remaining - seconds);
                        if (job.remaining <= 0)
                        {
                            progress.active.RemoveAt(i);
                            CompleteStudy(job);
                            changed = true;
                            continue;
                        }
                    }
                }
                i++;
            }
            if (ReadingPreview == null) changed |= FillSlots();
        }
        if (Time.unscaledTime >= nextStudySave)
        {
            nextStudySave = Time.unscaledTime + 2;
            changed = true;
        }
        return changed;
    }

    private void ApplyReadingDuration(MineLearningJob job)
    {
        if (IsStrengthTutorial(job) || !readingBooks.ContainsKey(job.bookId ?? "")) return;

        int comprehension = progress.skills.Find(skill => skill.id == "comprehension")?.value ?? 0;
        float reduction = Mathf.Clamp(comprehension * .04f, 0f, .30f);
        job.duration *= 1f - reduction;
        job.remaining = job.duration;
    }

    private void CompleteStudy(MineLearningJob job)
    {
        if (OpenCompletedReading(job.bookId)) return;
        var book = authoredBooks.FirstOrDefault(item => item.id == job.bookId);
        var tier = Intelligence(wallet()[2]);
        if (book != null && MineChapterSettings.Roll(seed, job.studyAttempt, "study:" + book.id) >= tier.success)
        {
            if (job.comboStudy && !HasComboStudy) progress.comboSuccesses.Clear();
            ShowStudyResult(false, $"학습 실패\n{AbilityNames[job.ability]} +0");
            return;
        }
        var coins = new int[4];
        int reward = book?.reward ?? job.reward;
        coins[job.ability] = reward;
        progress.successfulStudies++;
        if (book != null)
        {
            AwardSkill(book.skill, book.skillReward);
            if (!progress.mastered.Contains(book.id)) progress.mastered.Add(book.id);
        }
        var combos = new List<LearningComboReward>();
        if (job.comboStudy) progress.comboSuccesses.Add(job.bookId);
        foreach (var combo in study.combos)
        {
            if (!job.comboStudy || HasComboStudy || progress.rewardedCombos.Contains(combo.id) ||
                !combo.books.All(progress.comboSuccesses.Contains)) continue;
            progress.rewardedCombos.Add(combo.id);
            var bonus = BoostComboCoins(combo.coins, combo.books.Length);
            for (int ability = 0; ability < bonus.Length; ability++)
                coins[ability] += bonus[ability];
            AwardSkill(combo.skill, combo.skillReward);
            combos.Add(new LearningComboReward(combo.name.Replace('·', '/').Replace('×', '/'), bonus));
        }
        AwardCoins(coins, combos);
        if (job.comboStudy && !HasComboStudy) progress.comboSuccesses.Clear();
        var rewards = coins.Select((amount, ability) => amount > 0 ? $"{AbilityNames[ability]} +{amount}" : null)
            .Where(text => text != null).ToList();
        var skill = study.skills.FirstOrDefault(item => item.id == book?.skill);
        if (skill != null && book.skillReward > 0) rewards.Add($"{skill.name} +{book.skillReward}");
        string detail = rewards.Count == 0 ? "획득 없음" : string.Join(" / ", rewards);
        if (combos.Count > 0) detail += "\n콤보\n" + string.Join("\n", combos.Select(ComboResult));
        ShowStudyResult(true, "학습 성공\n" + detail);
    }

    private static readonly string[] AbilityNames = { "힘", "속도", "지능", "매력" };
    public static string AbilityName(int index) => AbilityNames[index];

    private void ShowStudyResult(bool succeeded, string text)
    {
        notice = null;
        StudyResult = text;
        StudyResultSucceeded = succeeded;
        StudyResultUntil = Time.unscaledTime + 3.5f;
    }

    private void AwardCoins(int[] amounts, IReadOnlyList<LearningComboReward> combos)
    {
        if (!amounts.Any(amount => amount != 0)) return;
        grant(amounts, combos);
        for (int ability = 0; ability < amounts.Length; ability++)
            if (amounts[ability] != 0) progress.lastStudyDay[ability] = progress.day;
    }

    private void GrantSingleAbility(int ability, int amount)
    {
        var delta = new int[4];
        delta[ability] = amount;
        grant(delta, null);
    }

    private void AwardSkill(string id, int amount)
    {
        if (string.IsNullOrEmpty(id) || amount <= 0) return;
        var value = progress.skills.Find(item => item.id == id);
        if (value == null) progress.skills.Add(value = new StudySkillValue { id = id });
        value.value += amount;
    }
}
