using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[Serializable] public sealed class NarrativeChapterInteractions
{
    public string chapterId;
    public int generationCardsPerSection = 2;
    public bool proofreadAllSections;
    public NarrativeWritingGuide[] writingGuides;
    public NarrativeInteractionRule[] interactions;
    public NarrativeScene[] scenes;
}

[Serializable] public sealed class NarrativeWritingGuide
{
    public string storyPath, characterId;
    public string[] facts;
    public NarrativeWritingFrame[] frames;
    public string[] promptRules;
    public NarrativeWritingParagraph[] paragraphs;
    public NarrativeWritingLimits limits;
}
[Serializable] public sealed class NarrativeWritingLimits
{
    public int maxCards, textMaxChars, optionMaxChars, maxOutputTokens;
}
[Serializable] public sealed class NarrativeWritingParagraph
{
    public string id, purpose;
    public string[] eventIds, currentState;
    public NarrativeWritingLimits limits;
}
[Serializable] public sealed class NarrativeWritingFrame
{
    public string eventId, purpose, currentState;
    public string[] forbiddenMentions;
}

[Serializable] public sealed class NarrativeScene
{
    public string sceneId, storyPath, characterId, sectionId;
    public List<EncounterCard> MainStory;
}

[Serializable] public sealed class NarrativeInteractionRule
{
    public string id, characterId, notice, revealedFact, followupStoryPath;
    public string[] choiceIds;
    public string[] requiresItems, excludesItems, grantsItems, consumesItems, requiresUnlocks, excludesUnlocks, grantsUnlocks;
    public string[] requiresEffects, excludesEffects;
}

/// <summary>Chapter-owned authored effects. Actual item movements and discoveries share the play journal.</summary>
public static class NarrativeInteractions
{
    private static readonly Dictionary<string, NarrativeChapterInteractions> Chapters = new Dictionary<string, NarrativeChapterInteractions>();
    public static void ResetCache() => Chapters.Clear();
    public static NarrativeChapterInteractions Chapter(string chapterId)
    {
        if (string.IsNullOrWhiteSpace(chapterId) || chapterId.IndexOfAny(new[] { '/', '\\', '.', ':' }) >= 0)
            return null;
        if (Chapters.TryGetValue(chapterId, out var cached)) return cached;
        var chapter = ChapterContentCatalog.Chapter(chapterId);
        if (chapter != null && chapter.chapterId != chapterId) throw new InvalidOperationException("상호작용 챕터 ID 불일치: " + chapterId);
        var writingPaths = new HashSet<string>();
        foreach (var guide in chapter?.writingGuides ?? Array.Empty<NarrativeWritingGuide>())
        {
            if (guide == null || string.IsNullOrWhiteSpace(guide.storyPath) || !guide.storyPath.StartsWith(chapterId+"/",StringComparison.Ordinal) ||
                !writingPaths.Add(guide.storyPath) || string.IsNullOrWhiteSpace(guide.characterId) || guide.frames == null)
                throw new InvalidOperationException("집필 장면 경로/인물/상황을 확인하세요: " + chapterId);
            var frameIds = new HashSet<string>();
            foreach (var frame in guide.frames)
                if (frame == null || string.IsNullOrWhiteSpace(frame.eventId) || !frameIds.Add(frame.eventId) ||
                    string.IsNullOrWhiteSpace(frame.purpose) || string.IsNullOrWhiteSpace(frame.currentState) ||
                    (frame.forbiddenMentions ?? Array.Empty<string>()).Any(string.IsNullOrWhiteSpace))
                    throw new InvalidOperationException("집필 구간의 사건/목적/현재 상황을 확인하세요: " + guide.storyPath);
        }
        var ids = new HashSet<string>();
        foreach (var rule in chapter?.interactions ?? Array.Empty<NarrativeInteractionRule>())
        {
            if (rule == null || string.IsNullOrWhiteSpace(rule.id) || !ids.Add(rule.id) ||
                string.IsNullOrWhiteSpace(rule.characterId) || rule.choiceIds==null || rule.choiceIds.Length==0 || rule.choiceIds.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException("상호작용 ID/인물/선택 ID를 확인하세요: " + chapterId);
        }
        var sceneIds=new HashSet<string>(); var paths=new HashSet<string>();
        foreach(var scene in chapter?.scenes??Array.Empty<NarrativeScene>())
        {
            if(scene==null || string.IsNullOrWhiteSpace(scene.sceneId) || !sceneIds.Add(scene.sceneId) ||
                string.IsNullOrWhiteSpace(scene.sectionId) || string.IsNullOrWhiteSpace(scene.characterId) ||
                string.IsNullOrWhiteSpace(scene.storyPath) || !scene.storyPath.StartsWith(chapterId+"/",StringComparison.Ordinal) || !paths.Add(scene.storyPath) ||
                scene.MainStory==null || scene.MainStory.Count==0)
                throw new InvalidOperationException("챕터 장면의 ID/구간/인물/이야기 경로를 확인하세요: "+chapterId);
            StoryChoiceRoles.ValidateRoot(new EncounterCardRoot {MainStory=scene.MainStory});
        }
        Chapters[chapterId] = chapter;
        return chapter;
    }

    public static string StorySource(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.IndexOfAny(new[] { '.', '\\', ':' }) >= 0) return null;
        return ChapterContentCatalog.Story(path);
    }

    public static bool TryApply(GameProgress progress, NarrativeInteractionRule rule, StoryEventRecord choice)
    {
        if (progress == null || rule == null || choice == null || choice.characterId != rule.characterId || !(rule.choiceIds?.Contains(choice.choiceId)??false))
            return false;
        string key = "interaction:" + choice.chapterId + ":" + rule.id;
        if (progress.appliedEffectIds?.Contains(key) == true) return false;
        var items = new List<string>(progress.storedItemIds ?? new List<string>());
        var worldItems = new List<WorldInventoryItemState>(progress.worldInventoryItems ?? new List<WorldInventoryItemState>());
        bool Owns(string id) => items.Contains(id) || worldItems.Any(item => item != null && item.itemId == id);
        if ((rule.requiresUnlocks ?? Array.Empty<string>()).Any(id => progress.unlockedIds?.Contains(id) != true) ||
            (rule.requiresItems ?? Array.Empty<string>()).Any(id => !Owns(id)) ||
            (rule.excludesItems ?? Array.Empty<string>()).Any(Owns) ||
            (rule.excludesUnlocks ?? Array.Empty<string>()).Any(id => progress.unlockedIds?.Contains(id)==true) ||
            (rule.requiresEffects ?? Array.Empty<string>()).Any(id=>progress.appliedEffectIds?.Contains(id)!=true ||
                (progress.storyEvents?.Find(e=>e.eventKey==id)?.sequence??int.MaxValue)>=choice.sequence) ||
            (rule.excludesEffects ?? Array.Empty<string>()).Any(id=>progress.appliedEffectIds?.Contains(id)==true)) return false;
        if (!string.IsNullOrEmpty(rule.followupStoryPath) && StorySource(rule.followupStoryPath)==null)
            throw new InvalidOperationException("상호작용 후속 이야기가 없습니다: "+rule.followupStoryPath);
        // Validate the whole exchange before changing anything. Only installed catalog items can move.
        var catalog = Resources.LoadAll<InventoryItemDefinition>("Inventory").Select(i => i.ItemId).ToHashSet();
        foreach (string id in (rule.grantsItems ?? Array.Empty<string>()).Concat(rule.consumesItems ?? Array.Empty<string>()))
            if (string.IsNullOrWhiteSpace(id) || !catalog.Contains(id)) throw new InvalidOperationException("등록되지 않은 상호작용 아이템: " + id);
        foreach (string id in rule.consumesItems ?? Array.Empty<string>())
            if (!items.Remove(id))
            {
                int index = worldItems.FindIndex(item => item != null && item.itemId == id);
                if (index < 0) return false;
                worldItems.RemoveAt(index);
            }

        var movements = new List<StoryEventRecord>();
        void Movement(string id, bool received)
        {
            movements.Add(new StoryEventRecord {
                eventKey = key + ":" + movements.Count, run = choice.run, chapterId = choice.chapterId,
                episodeId = choice.episodeId, terrainId = choice.terrainId, terrainName = choice.terrainName,
                placeId = choice.placeId, characterId = rule.characterId, characterName = choice.characterName,
                storyPath = choice.storyPath, cardId = choice.cardId, choiceId = choice.choiceId,
                kind = received ? "item_received" : "item_returned", itemId = id, quantity = 1,
                direction = received ? "in" : "out", reason = rule.id
            });
        }
        foreach (string id in rule.consumesItems ?? Array.Empty<string>()) Movement(id, false);
        foreach (string id in rule.grantsItems ?? Array.Empty<string>())
        {
            // The pilot items are unique per run; repeated choices cannot duplicate a reward.
            if (Owns(id)) continue;
            items.Add(id); Movement(id, true);
        }
        progress.storedItemIds = items;
        progress.worldInventoryItems = worldItems;
        progress.unlockedIds ??= new List<string>();
        foreach (string id in rule.grantsUnlocks ?? Array.Empty<string>())
            if (!string.IsNullOrWhiteSpace(id) && !progress.unlockedIds.Contains(id)) progress.unlockedIds.Add(id);
        progress.storyEvents ??= new List<StoryEventRecord>();
        if (!string.IsNullOrEmpty(rule.followupStoryPath))
        {
            var selected=progress.storyEvents.Find(e=>e.eventKey==choice.eventKey);
            if(selected!=null) selected.resultStoryPath=rule.followupStoryPath;
        }
        movements.Add(new StoryEventRecord {
            eventKey = key, run = choice.run, chapterId = choice.chapterId, episodeId = choice.episodeId,
            placeId = choice.placeId, characterId = rule.characterId, characterName = choice.characterName,
            storyPath = choice.storyPath, cardId = choice.cardId, choiceId = choice.choiceId,
            kind = "interaction", reason = rule.id, displayedText = rule.notice, semanticText = ChapterContentCatalog.Setting(rule.revealedFact,progress.unlockedIds.ToArray()),
            grantsUnlocks = rule.grantsUnlocks
        });
        foreach (var record in movements) { record.sequence = progress.storyEvents.Count + 1; progress.storyEvents.Add(record); }
        progress.appliedEffectIds ??= new List<string>();
        progress.appliedEffectIds.Add(key);
        return true;
    }

    public static string RevealedFacts(string characterId, IEnumerable<string> earned)
    {
        var unlocked = new HashSet<string>(earned ?? Array.Empty<string>());
        // Only chapter catalogs are scanned; no historical dialogue or save files are read here.
        return string.Join("\n", ChapterContentCatalog.Chapters.Select(c => Chapter((string)c["id"]))
            .SelectMany(c => c.interactions ?? Array.Empty<NarrativeInteractionRule>())
            .Where(r => r.characterId == characterId && !string.IsNullOrWhiteSpace(r.revealedFact) &&
                r.grantsUnlocks?.Length > 0 && r.grantsUnlocks.All(unlocked.Contains))
            .Select(r => ChapterContentCatalog.Setting(r.revealedFact,unlocked.ToArray())).Distinct());
    }
}
