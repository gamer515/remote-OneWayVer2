using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed partial class MineLearningStation
{
    internal static readonly string[] AbilityKeys = { "strength", "speed", "intelligence", "charm" };
    internal const string AuthoredContent = "Chapters/Chapter_01/AuthoredNarrative/";
    private Dictionary<string, JObject> readingBooks = new Dictionary<string, JObject>();
    private readonly Dictionary<string, string> previousBookIds = new Dictionary<string, string>(StringComparer.Ordinal);
    private const string FirstComboBook = "BK001", SecondComboBook = "BK002";
    private JObject[] readingCombos = Array.Empty<JObject>();

    internal static JObject ReadContent(string path) =>
        JObject.Parse(Resources.Load<TextAsset>(AuthoredContent + path).text);

    private void ConfigureReading()
    {
        readingBooks = Resources.LoadAll<TextAsset>(AuthoredContent + "Reading/books")
            .Select(asset => JObject.Parse(asset.text)).ToDictionary(book => (string)book["bookId"]);
        readingCombos = Resources.LoadAll<TextAsset>(AuthoredContent + "Reading/combos")
            .Select(asset => JObject.Parse(asset.text)).OrderBy(combo => (string)combo["id"]).ToArray();
        var previous = authoredBooks;
        previousBookIds.Clear();
        authoredBooks = readingBooks.Values.OrderBy(book => (string)book["bookId"]).GroupBy(book =>
            Array.IndexOf(AbilityKeys, (string)book["mainStat"])).SelectMany(group =>
        {
            var templates = previous.Where(book => book.ability == group.Key).ToArray();
            return group.Select((entry, index) =>
            {
                string id = (string)entry["bookId"];
                var exact = templates.FirstOrDefault(book => book.id == id ||
                    book.name == (string)entry["originalTitle"] || book.name == (string)entry["parodyTitle"]);
                if (!string.IsNullOrEmpty(exact?.id) && exact.id != id)
                    previousBookIds[exact.id] = id;
                var template = exact
                    ?? templates.ElementAtOrDefault(Math.Min(index, templates.Length - 1));
                return new BookDefinition
                {
                    id = id, name = (string)entry["parodyTitle"], ability = group.Key,
                    author = (string)entry["author"], seconds = template?.seconds ?? minimumSeconds,
                    generationSeconds = template?.generationSeconds ?? deliverySeconds,
                    requiredKnowledge = template?.requiredKnowledge ?? 0,
                    requiredAbility = template?.requiredAbility ?? 0
                };
            });
        }).ToArray();
    }

    private string CurrentReadingBookId(MineLearningJob job)
    {
        if (!string.IsNullOrEmpty(job.bookId))
        {
            if (readingBooks.ContainsKey(job.bookId)) return job.bookId;
            if (previousBookIds.TryGetValue(job.bookId, out string id)) return id;
        }
        if (string.IsNullOrEmpty(job.bookName)) return job.bookId;
        foreach (var book in readingBooks)
            if ((string)book.Value["originalTitle"] == job.bookName || (string)book.Value["parodyTitle"] == job.bookName)
                return book.Key;
        return job.bookId;
    }

    internal JObject PendingReading => progress?.pendingReading?.Count > 0 &&
        readingBooks.TryGetValue(progress.pendingReading[0], out var book) ? book : null;
    internal string ReadingResult => progress?.readingResult;

    internal void SelectReadingChoice(int index)
    {
        var book = PendingReading;
        if (book == null || ReadingResult != null || index < 0 || index >= book["choices"].Count()) return;
        var choice = book["choices"][index];
        var amounts = new int[4];
        void AddRewards(JToken reward)
        {
            for (int i = 0; i < amounts.Length; i++) amounts[i] += (int?)reward?[AbilityKeys[i]] ?? 0;
        }

        string id = (string)book["bookId"];
        progress.pendingReading.RemoveAt(0);
        progress.completedReading.Add(id);
        if (!progress.mastered.Contains(id)) progress.mastered.Add(id);
        progress.successfulStudies++;
        AddRewards(choice["reward"]);
        var combos = new List<string>();
        foreach (var combo in readingCombos)
        {
            string comboId = (string)combo["id"];
            var condition = combo["condition"];
            if (progress.claimedReadingCombos.Contains(comboId) ||
                condition["bookIds"].Values<string>().Count(progress.completedReading.Contains) <
                (int)condition["minCompleted"]) continue;
            progress.claimedReadingCombos.Add(comboId);
            AddRewards(combo["reward"]);
            combos.Add((string)combo["name"]);
        }
        for (int i = 0; i < amounts.Length; i++) AwardCoins(i, amounts[i]);
        progress.readingResult = (string)choice["result"] + "\n\n" +
            string.Join(" / ", amounts.Select((quantity, i) => quantity > 0 ? AbilityName(i) + " +" + quantity : null)
                .Where(label => label != null));
        if (combos.Count > 0) progress.readingResult += "\n콤보 달성\n" + string.Join("\n", combos);
        save?.Invoke();
    }

    internal void CloseReadingResult()
    {
        progress.readingResult = null;
        save?.Invoke();
    }
}
