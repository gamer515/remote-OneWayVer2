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
        public int[] enduranceThresholds, dailyLimits;
        public int healthPerEndurance;
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
    private float studyDaySeconds;
    private float nextStudySave;
    public bool UsesStudyRules => study != null;
    public string StudyResult { get; private set; }
    public bool StudyResultSucceeded { get; private set; }
    public float StudyResultUntil { get; private set; }
    public bool IsDailyLimitBlocking => study != null && !paused && progress != null &&
        progress.attemptsToday >= TodayLimit &&
        progress.active.Any(job => job.onBoard && (job.boardIndex == 1 || IsReadingPlacement(job)) &&
            !job.started && !job.held && CanRead(job));
    public int Stock(int ability) => progress?.stock?[ability] ?? 0;
    public int RefillCost(int ability) => study == null ? 0 :
        study.refillBase + (progress?.refills?[ability] ?? 0) * study.refillIncrease;
    public bool HasComboStudy => progress.active.Any(job => job.comboStudy);

    public void ConfigureStudy(BookCatalog catalog, StudyRules rules, float calendarDaySeconds, float calendarDays)
    {
        study = catalog?.books?.Length > 0 ? rules : null;
        authoredBooks = catalog?.books;
        studyDaySeconds = calendarDaySeconds > 0 ? calendarDaySeconds : study?.daySeconds ?? 0;
        initialCalendarDays = Mathf.Max(0, calendarDays);
    }

    private float initialCalendarDays;

    private void InitializeStudyState()
    {
        if (study == null) return;
        nextStudySave = 0;
        StudyResult = null;
        progress.stored ??= new List<MineLearningJob>();
        progress.mastered ??= new List<string>();
        if (!progress.studySuccessCountInitialized)
        {
            progress.successfulStudies = progress.mastered.Count;
            progress.studySuccessCountInitialized = true;
        }
        progress.rewardedCombos ??= new List<string>();
        progress.comboSuccesses ??= new List<string>();
        progress.skills ??= new List<StudySkillValue>();
        progress.stock ??= Enumerable.Repeat(study.startingStock, 4).ToArray();
        progress.refills ??= new int[4];
        progress.nextBook ??= new int[4];
        progress.clockDays = Mathf.Max(progress.clockDays, initialCalendarDays);
        progress.lastStudyDay ??= Enumerable.Repeat(Mathf.FloorToInt(progress.clockDays), 4).ToArray();
        RefreshDay();
    }

    private bool RefreshDay()
    {
        int day = Mathf.FloorToInt(progress.clockDays);
        if (day == progress.day) return false;
        progress.day = day;
        progress.attemptsToday = 0;
        Array.Clear(progress.refills, 0, progress.refills.Length);
        return true;
    }

    private int Skill(string id) => progress.skills.Find(value => value.id == id)?.value ?? 0;

    private int DailyLimit(int[] amounts)
    {
        int endurance = Skill("endurance") + amounts[0] / study.healthPerEndurance;
        int index = Array.FindLastIndex(study.enduranceThresholds, threshold => endurance >= threshold);
        return study.dailyLimits[Mathf.Max(0, index)];
    }

    private StudyTier Intelligence(int knowledge) =>
        study.intelligence.LastOrDefault(tier => knowledge >= tier.threshold) ?? study.intelligence[0];

    private bool CanRead(BookDefinition book, int[] amounts) =>
        amounts[2] >= book.requiredKnowledge && amounts[book.ability] >= book.requiredAbility;

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
        if (progress.producing != null) return Explain("다른 책을 만들고 있습니다.");
        if (Stock(ability) <= 0) return Explain("책이 없습니다. 해당 능력 코인을 투입구에 넣으세요.");
        if (progress.active.Count + progress.queued.Count >= Capacity.simultaneous + Capacity.waiting)
            return Explain("학습 대기열이 가득 찼습니다.");

        var amounts = wallet();
        var available = authoredBooks.Where(book => book.ability == ability && CanRead(book, amounts)).ToArray();
        if (available.Length == 0) return Explain("이 분야의 책을 읽으려면 지능이나 해당 능력이 더 필요합니다.");
        var book = available[progress.nextBook[ability] % available.Length];
        progress.nextBook[ability]++;
        progress.stock[ability]--;
        progress.producing = new MineLearningJob
        {
            ability = ability, reward = book.reward, duration = book.seconds, remaining = book.seconds,
            generationRemaining = book.generationSeconds, bookId = book.id, bookName = book.name,
            requiresBoard = true
        };
        notice = null;
        save?.Invoke();
        return true;
    }

    public bool Refill(int ability, int insertedCoins = 1)
    {
        if (!UsesStudyRules || !CanMoveBooks || ability < 0 || ability >= 4) return false;
        if (Stock(ability) > 0) return Explain("남은 책을 모두 뽑으면 리필할 수 있습니다.");
        int cost = RefillCost(ability);
        if (insertedCoins < cost) return Explain($"코인 {cost}개를 넣어야 리필됩니다.");
        int available = wallet()[ability];
        if (available < cost || (ability == 0 && available == cost))
            return Explain("리필할 능력 코인이 부족합니다.");
        if (!charge(ability, cost)) return false;
        progress.stock[ability] = study.refillStock;
        progress.refills[ability]++;
        notice = $"책 {study.refillStock}권 리필 / 코인 {cost}개 사용";
        save?.Invoke();
        return true;
    }

    public bool SubmitShelf(Vector3 pickupPoint)
    {
        if (!UsesStudyRules || !CanMoveBooks || HasComboStudy) return false;
        var batch = progress.active.Where(job => job.boardIndex == 3 && !job.started).ToArray();
        if (!study.combos.Any(combo => !progress.rewardedCombos.Contains(combo.id) &&
            combo.books.All(id => batch.Any(job => job.bookId == id))))
            return Explain("책장에 콤보 조합을 모아 주세요.");
        if (batch.Length < 2 || batch.Length > Capacity.simultaneous ||
            batch.Length > TodayLimit - progress.attemptsToday || !batch.All(CanRead))
            return Explain("콤보 학습에는 함께 읽을 자리와 오늘의 학습량이 필요합니다.");
        progress.comboSuccesses.Clear();
        progress.comboBoardPoint = pickupPoint;
        foreach (var job in batch)
        {
            job.comboStudy = true;
            job.boardIndex = 1;
        }
        save?.Invoke();
        return true;
    }

    public event Action StoredBooksChanged;

    public bool StoreBook(MineLearningJob job)
    {
        if (!CanMoveBooks || job == null || job.comboStudy || !job.onBoard || !progress.active.Remove(job)) return false;
        job.onBoard = job.held = false;
        progress.stored.Add(job);
        notice = $"{job.bookName} / 가방에 보관";
        Rebuild();
        save?.Invoke();
        StoredBooksChanged?.Invoke();
        return true;
    }

    public bool RestoreBook(MineLearningJob job, float u, float v, int boardIndex = 0)
    {
        if (!CanMoveBooks || job == null || progress.active.Count >= Capacity.simultaneous)
            return Explain("보드에 놓을 수 있는 책 자리가 없습니다.");
        if (!progress.stored.Remove(job)) return false;
        progress.active.Add(job);
        job.transit = 0;
        RememberPlacement(job, u, v, boardIndex);
        Rebuild();
        StoredBooksChanged?.Invoke();
        return true;
    }

    private bool TickStudy(float seconds, bool stop)
    {
        progress.clockDays += seconds / studyDaySeconds;
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
            if (loss > 0) grant(ability, -loss);
            progress.lastStudyDay[ability] += periods * decayDays;
            changed = true;
        }
        if (!stop)
        {
            bool enteredQueue = false;
            if (progress.producing != null)
            {
                progress.producing.generationRemaining -= seconds;
                if (progress.producing.generationRemaining <= 0)
                {
                    progress.queued.Add(progress.producing);
                    progress.producing = null;
                    enteredQueue = changed = true;
                }
            }

            for (int i = 0; i < progress.active.Count;)
            {
                var job = progress.active[i];
                if (job.transit > 0) job.transit = Mathf.Max(0, job.transit - seconds);
                else if (!job.held && IsReadingPlacement(job))
                {
                    if (!CanRead(job)) notice = $"{job.bookName} / 읽기 능력치 부족";
                    else if (job.started || progress.attemptsToday < DailyLimit(wallet()))
                    {
                        if (!job.started)
                        {
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
            // Give a newly completed book a rendered frame in the queue before delivery starts.
            if (!enteredQueue) changed |= FillSlots();
        }
        if (Time.unscaledTime >= nextStudySave)
        {
            nextStudySave = Time.unscaledTime + 2;
            changed = true;
        }
        return changed;
    }

    private void CompleteStudy(MineLearningJob job)
    {
        var book = authoredBooks.FirstOrDefault(item => item.id == job.bookId);
        var tier = Intelligence(wallet()[2]);
        if (book != null && MineChapterSettings.Roll(seed, job.studyAttempt, "study:" + book.id) >= tier.success)
        {
            if (job.comboStudy && !HasComboStudy) progress.comboSuccesses.Clear();
            ShowStudyResult(false, $"학습 실패\n{AbilityNames[job.ability]} +0");
            return;
        }
        var coins = new int[4];
        coins[job.ability] = job.reward;
        AwardCoins(job.ability, job.reward);
        progress.successfulStudies++;
        if (book != null)
        {
            AwardSkill(book.skill, book.skillReward);
            if (!progress.mastered.Contains(book.id)) progress.mastered.Add(book.id);
        }
        var combos = new List<string>();
        if (job.comboStudy) progress.comboSuccesses.Add(job.bookId);
        foreach (var combo in study.combos)
        {
            if (!job.comboStudy || HasComboStudy || progress.rewardedCombos.Contains(combo.id) ||
                !combo.books.All(progress.comboSuccesses.Contains)) continue;
            progress.rewardedCombos.Add(combo.id);
            for (int ability = 0; ability < combo.coins.Length; ability++)
            {
                AwardCoins(ability, combo.coins[ability]);
                coins[ability] += combo.coins[ability];
            }
            AwardSkill(combo.skill, combo.skillReward);
            combos.Add(combo.name.Replace('·', '/').Replace('×', '/'));
        }
        if (job.comboStudy && !HasComboStudy) progress.comboSuccesses.Clear();
        var rewards = coins.Select((amount, ability) => amount > 0 ? $"{AbilityNames[ability]} +{amount}" : null)
            .Where(text => text != null).ToList();
        var skill = study.skills.FirstOrDefault(item => item.id == book?.skill);
        if (skill != null && book.skillReward > 0) rewards.Add($"{skill.name} +{book.skillReward}");
        string detail = rewards.Count == 0 ? "획득 없음" : string.Join(" / ", rewards);
        if (combos.Count > 0) detail += "\n콤보 " + string.Join(" / ", combos);
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

    private void AwardCoins(int ability, int amount)
    {
        if (amount == 0) return;
        grant(ability, amount);
        progress.lastStudyDay[ability] = progress.day;
    }

    private void AwardSkill(string id, int amount)
    {
        if (string.IsNullOrEmpty(id) || amount <= 0) return;
        var value = progress.skills.Find(item => item.id == id);
        if (value == null) progress.skills.Add(value = new StudySkillValue { id = id });
        value.value += amount;
    }
}
