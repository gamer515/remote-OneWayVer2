using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>Loads one validated authoring snapshot for the whole session.</summary>
internal static class ChapterWorkbookContent
{
    private static readonly string[] WorkbookNames =
    {
        "Chapters", "Maps", "Places", "NPCs", "Story", "Choices", "Rules", "Taxes", "TaxCatalog", "Books", "Learning", "Study"
    };
    private static readonly string[] Header = { "Document", "Field", "Type", "Value" };
    private static readonly Regex DocumentName = new Regex(@"^[A-Za-z0-9_-]+(/[A-Za-z0-9_-]+)*$");
    private static JObject snapshot;
    private static string storageRoot;

    internal static void Reset()
    {
        snapshot = null;
        storageRoot = null;
    }

    internal static JObject Get(string document)
    {
        if (!Application.isPlaying) return null;
        string root = SaveIOService.Instance.StorageRoot;
        if (storageRoot != root)
        {
            snapshot = Load(root);
            storageRoot = root;
        }
        return snapshot?[document] as JObject;
    }

    internal static JObject Load(string saveRoot)
    {
        string folder = Path.Combine(saveRoot, "ContentAuthoring");
        string previous = Path.Combine(folder, "AppliedContent.json");
        try
        {
            Directory.CreateDirectory(folder);
            foreach (string name in WorkbookNames)
            {
                string file = name + ".xlsx";
                string target = Path.Combine(folder, file);
                string seed = Path.Combine(Application.streamingAssetsPath, "ContentAuthoring", file);
                if (!File.Exists(target)) File.Copy(seed, target);
            }

            var candidate = Read(folder);
            ValidateStudy(candidate);
            Validate(candidate);
            string json = candidate.ToString(Newtonsoft.Json.Formatting.None);
            if (!File.Exists(previous) || File.ReadAllText(previous) != json)
            {
                string temp = previous + ".tmp";
                File.WriteAllText(temp, json);
                if (File.Exists(previous)) File.Replace(temp, previous, null);
                else File.Move(temp, previous);
            }
            return candidate;
        }
        catch (Exception error)
        {
            Debug.LogWarning($"[ContentAuthoring] 엑셀 적용 보류: {error.Message}");
            try
            {
                if (!File.Exists(previous)) return null;
                var lastGood = JObject.Parse(File.ReadAllText(previous));
                Validate(lastGood);
                return lastGood;
            }
            catch (Exception fallbackError)
            {
                Debug.LogWarning($"[ContentAuthoring] 이전 적용본을 읽지 못했습니다: {fallbackError.Message}");
                return null;
            }
        }
    }

    private static JObject Read(string folder)
    {
        var documents = new JObject();
        foreach (string name in WorkbookNames)
        {
            string file = name + ".xlsx";
            int rowNumber = 0;
            try
            {
                using var stream = File.Open(Path.Combine(folder, file), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
                bool hasHeader = false;
                foreach (var row in LocalStoryNotebook.ReadWorkbookRows(zip, "Data", includeHeader: true))
                {
                    rowNumber = row.number;
                    string[] cell = row.cells;
                    if (rowNumber == 1)
                    {
                        hasHeader = cell.Take(4).SequenceEqual(Header);
                        if (!hasHeader) throw new InvalidDataException("열 이름은 Document, Field, Type, Value여야 합니다.");
                        continue;
                    }
                    if (cell.Take(4).All(string.IsNullOrEmpty)) continue;
                    if (!DocumentName.IsMatch(cell[0]) || !cell[1].StartsWith("/", StringComparison.Ordinal))
                        throw new InvalidDataException("문서 또는 필드 경로 형식 오류");
                    var document = documents[cell[0]] as JObject;
                    if (document == null) documents[cell[0]] = document = new JObject();
                    Set(document, cell[1], Parse(cell[2], cell[3]));
                }
                if (!hasHeader) throw new InvalidDataException("제목 행이 없습니다.");
            }
            catch (Exception error)
            {
                throw new InvalidDataException($"{file} / Data / {rowNumber}행: {error.Message}", error);
            }
        }
        return documents;
    }

    private static JToken Parse(string type, string value)
    {
        if (type == "text") return new JValue(value);
        if (type == "flag" && (value == "1" || value == "0")) return new JValue(value == "1");
        if (type == "null" && value.Length == 0) return JValue.CreateNull();
        if (type == "number")
        {
            var number = JToken.Parse(value);
            if (number.Type == JTokenType.Integer || number.Type == JTokenType.Float && double.IsFinite((double)number))
                return number;
        }
        if (type == "json") return JToken.Parse(value);
        throw new InvalidDataException("Type과 Value가 일치하지 않습니다: " + type);
    }

    // #N marks an array position. Object keys, including numeric card IDs, stay strings.
    private static void Set(JObject document, string path, JToken value)
    {
        string[] parts = path.Substring(1).Split('/');
        JToken parent = document;
        for (int i = 0; i < parts.Length; i++)
        {
            string key = parts[i].Replace("~1", "/").Replace("~0", "~");
            bool final = i == parts.Length - 1;
            if (key.Length == 0) throw new InvalidDataException("빈 필드 경로: " + path);
            bool arrayChild = !final && parts[i + 1].StartsWith("#", StringComparison.Ordinal);
            JToken child;
            if (parent is JObject obj)
            {
                child = obj[key];
                if (child == null) obj[key] = child = final ? value : arrayChild ? new JArray() : new JObject();
                else if (final) throw new InvalidDataException("중복 필드: " + path);
            }
            else if (parent is JArray array && key.StartsWith("#", StringComparison.Ordinal) &&
                     int.TryParse(key.Substring(1), out int index) && index >= 0 && index <= array.Count)
            {
                if (index == array.Count)
                {
                    child = final ? value : arrayChild ? new JArray() : new JObject();
                    array.Add(child);
                }
                else
                {
                    if (final) throw new InvalidDataException("중복 배열 항목: " + path);
                    child = array[index];
                }
            }
            else throw new InvalidDataException("필드 타입 또는 배열 순서 오류: " + path);
            parent = child;
        }
    }

    private static void Validate(JObject documents)
    {
        var order = documents["Omnibus"]?.ToObject<OmnibusData>();
        var catalog = documents["Catalog"];
        if (order?.chapters == null || order.chapters.Count == 0 || catalog?["chapters"] is not JArray chapters ||
            catalog["stories"] is not JArray stories || order.PlayableChapterCount == 0)
            throw new InvalidDataException("Chapters.xlsx: 챕터 목록 또는 시작/종료 범위 누락");
        _ = order.FirstPlayableChapterIndex;
        var chapterById = chapters.ToDictionary(c => (string)c["id"], StringComparer.Ordinal);
        var storyByPath = stories.ToDictionary(s => (string)s["path"], StringComparer.Ordinal);
        var seen = new HashSet<string>();
        foreach (var chapter in order.chapters)
        {
            if (chapter == null || string.IsNullOrWhiteSpace(chapter.chapterId) || !seen.Add(chapter.chapterId) ||
                !chapterById.TryGetValue(chapter.chapterId, out var entry) || chapter.episodeIds == null ||
                chapter.episodeIds.Count == 0 || chapter.episodeIds.Distinct().Count() != chapter.episodeIds.Count)
                throw new InvalidDataException("Chapters.xlsx: 잘못된 챕터/에피소드 ID " + chapter?.chapterId);
            string folder = (string)entry["folder"];
            if (string.IsNullOrWhiteSpace(folder) || documents[folder + "/Characters"]?["characters"] is not JArray characters)
                throw new InvalidDataException("NPCs.xlsx: 챕터 인물 목록 누락 " + chapter.chapterId);
            foreach (var character in characters.OfType<JObject>())
                if (character["autoGenerate"] is JToken enabled && enabled.Type != JTokenType.Boolean ||
                    character["speechStyle"] is JToken style && style.Type != JTokenType.String)
                    throw new InvalidDataException("NPCs.xlsx: 자동 생성 설정 또는 말투 형식 오류 " + character["characterId"]);
            if (chapter.bossStage < 0 || !string.IsNullOrEmpty(chapter.rulesDocument) && documents[chapter.rulesDocument] is not JObject ||
                chapter.rulesEpisodes != null && chapter.rulesEpisodes.Any(id => !chapter.episodeIds.Contains(id)))
                throw new InvalidDataException("Chapters.xlsx: 보스 또는 규칙 참조 오류 " + chapter.chapterId);
            if (!string.IsNullOrEmpty(chapter.rulesDocument) && documents[chapter.rulesDocument]?["dayCycle"] is JToken clock &&
                !clock.ToObject<ChapterDayCycle>().IsValid)
                throw new InvalidDataException("Rules.xlsx: 하루 시간 또는 밝기 범위 오류 " + chapter.chapterId);
            if (!string.IsNullOrEmpty(chapter.rulesDocument))
            {
                var rules = documents[chapter.rulesDocument].ToObject<MineChapterSettings>();
                if (!float.IsFinite(rules.collectorArrivalSeconds) || rules.collectorArrivalSeconds < 0 ||
                    !float.IsFinite(rules.collectorNpcWaitMin) || rules.collectorNpcWaitMin < 0 ||
                    !float.IsFinite(rules.collectorNpcWaitMax) || rules.collectorNpcWaitMax < rules.collectorNpcWaitMin)
                    throw new InvalidDataException("Taxes.xlsx: 징수원 준비 이동 또는 NPC 대기 시간 오류 " + chapter.chapterId);
            }
            if (documents[folder + "/Taxes"] is JToken taxes && !taxes.ToObject<ChapterTaxSettings>().IsValid)
                throw new InvalidDataException("TaxCatalog.xlsx: 세목, 부과 금액 또는 징수 주기 오류 " + chapter.chapterId);
            ValidateLearning(documents, folder);
            foreach (string episode in chapter.episodeIds)
            {
                string path = chapter.chapterId + "/" + episode;
                var terrain = documents[folder + "/Terrain"]?["episodes"]?[episode]?["TerrainInfo"]?.ToObject<TerrainDefinition>();
                var places = documents[folder + "/Transform"]?["episodes"]?[episode]?["TerrainTransforms"]?.ToObject<TerrainTransformData>();
                if (terrain == null || terrain.chunkCount <= 0 || !float.IsFinite(terrain.chunkLength) || terrain.chunkLength <= 0 ||
                    terrain.groundIds == null || terrain.groundIds.Length != terrain.chunkCount ||
                    terrain.groundIds.Any(string.IsNullOrWhiteSpace) || places?.places == null || terrain.places == null)
                    throw new InvalidDataException("Maps.xlsx / Places.xlsx: 맵 또는 좌표 누락 " + path);
                if (terrain.facilityRules != null && (!float.IsFinite(terrain.facilitySpacing) || terrain.facilitySpacing < 0 ||
                    terrain.facilityMajorLimit < 0 || terrain.facilityOptionalLimit < 0 ||
                    terrain.facilityRules.Any(rule => rule == null || string.IsNullOrWhiteSpace(rule.id) ||
                        rule.minimumChunkGap < 0 || rule.maximumChunkGap < rule.minimumChunkGap ||
                        rule.firstMinimumChunk < 0 || rule.firstMaximumChunk < rule.firstMinimumChunk) ||
                    terrain.facilityRules.Select(rule => rule.id).Distinct().Count() != terrain.facilityRules.Length))
                    throw new InvalidDataException("Places.xlsx: 시설 ID, 간격 또는 등장 주기 오류 " + path);
                var positions = places.places.ToDictionary(p => p.placeId, StringComparer.Ordinal);
                var placeIds = new HashSet<string>();
                foreach (var place in terrain.places)
                {
                    if (string.IsNullOrWhiteSpace(place.placeId) || !placeIds.Add(place.placeId) ||
                        !positions.ContainsKey(place.placeId) || place.chunkIndex < 0 || place.chunkIndex >= terrain.chunkCount ||
                        !string.IsNullOrEmpty(place.connectStoryCards) && !storyByPath.ContainsKey(place.connectStoryCards))
                        throw new InvalidDataException("Places.xlsx: 장소/이야기 연결 오류 " + path + "/" + place.placeId);
                }
            }
        }

        foreach (var entry in storyByPath.Values)
        {
            string path = (string)entry["path"];
            if (entry["rewriteEmotions"] is JToken emotions && (emotions is not JArray choices || choices.Count == 0 ||
                choices.Any(value => value.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)value))))
                throw new InvalidDataException("Story.xlsx: rewriteEmotions에는 비어 있지 않은 감정 문자열 배열을 입력하세요. " + path);
            string key = (string)entry["key"];
            string storyFile = ChapterContentCatalog.SourceFile((JObject)entry);
            string explanationFile = ChapterContentCatalog.SourceFile((JObject)entry, true);
            if (documents[storyFile.Substring(0, storyFile.Length - 5)]?[key]?["MainStory"] is not JArray cards ||
                cards.Count == 0 || documents[explanationFile.Substring(0, explanationFile.Length - 5)]?[key] is not JObject)
                throw new InvalidDataException("Story.xlsx: 이야기/의미 누락 " + path);
            var ids = new HashSet<int>();
            foreach (var card in cards)
            {
                if (card["ID"]?.Type != JTokenType.Integer || !ids.Add((int)card["ID"]))
                    throw new InvalidDataException("Story.xlsx: 카드 ID 중복 또는 형식 오류 " + path);
                if (card["choiceActions"] is JArray actions)
                    foreach (var action in actions)
                        RequireStory(storyByPath, (string)action["storyPath"], path);
            }
        }
        foreach (var document in documents.Properties().Where(p => p.Name.EndsWith("/Terrain", StringComparison.Ordinal)))
        {
            if (document.Value["actions"] is not JObject actions) continue;
            foreach (var field in actions.Descendants().OfType<JProperty>())
            {
                if (field.Name == "abilityCost" && (field.Value is not JArray || !AbilityPayment.IsValid(field.Value.ToObject<int[]>())))
                    throw new InvalidDataException("Choices.xlsx: 능력 비용은 0 이상 정수 4개입니다. " + field.Path);
                if (field.Name == "winStoryPath" || field.Name == "loseStoryPath" || field.Name == "nextStoryPath" || field.Name == "insufficientStoryPath")
                    RequireStory(storyByPath, (string)field.Value, field.Path);
            }
        }
    }

    private static void RequireStory(Dictionary<string, JToken> stories, string target, string source)
    {
        if (!string.IsNullOrEmpty(target) && !stories.ContainsKey(target))
            throw new InvalidDataException($"Story.xlsx / Choices.xlsx: 분기 대상 '{target}' 누락 ({source})");
    }

    private static void ValidateLearning(JObject documents, string folder)
    {
        var books = documents[folder + "/Books"]?.ToObject<MineLearningStation.BookCatalog>();
        if (books != null && (books.books == null || books.books.Any(book => book == null ||
            string.IsNullOrWhiteSpace(book.id) || string.IsNullOrWhiteSpace(book.name) || book.ability < 0 || book.ability > 3 ||
            book.requiredKnowledge < 0 || book.cost < 0 || book.reward < 0 || !float.IsFinite(book.seconds) || book.seconds <= 0) ||
            books.books.GroupBy(book => book.id).Any(group => group.Count() > 1) ||
            books.books.GroupBy(book => (book.ability, book.requiredKnowledge)).Any(group => group.Count() > 1)))
            throw new InvalidDataException("Books.xlsx: 잘못된 책 ID, 능력, 비용 또는 시간 " + folder);

        var learning = documents[folder + "/Learning"]?.ToObject<MineLearningStation.LearningSettings>();
        if (learning != null && (learning.capacities == null || !learning.capacities.Any(step => step?.knowledgeCoins == 0) ||
            learning.capacities.Any(step => step == null || step.knowledgeCoins < 0 || step.simultaneous < 1 || step.waiting < 0) ||
            learning.capacities.GroupBy(step => step.knowledgeCoins).Any(group => group.Count() > 1) ||
            !float.IsFinite(learning.minimumSeconds) || learning.minimumSeconds <= 0 ||
            !float.IsFinite(learning.maximumSeconds) || learning.maximumSeconds < learning.minimumSeconds ||
            !float.IsFinite(learning.deliverySeconds) || learning.deliverySeconds <= 0))
            throw new InvalidDataException("Learning.xlsx: 잘못된 용량 또는 시간 " + folder);
    }

    private static void ValidateStudy(JObject documents)
    {
        var books = documents["LearningSystem/Books"]?.ToObject<MineLearningStation.BookCatalog>()?.books;
        var rules = documents["LearningSystem/Rules"]?.ToObject<MineLearningStation.StudyRules>();
        bool Positive(float number) => float.IsFinite(number) && number > 0;
        bool Ordered(IEnumerable<int> values)
        {
            var numbers = values.ToArray();
            return numbers.Length > 0 && numbers[0] >= 0 &&
                numbers.Zip(numbers.Skip(1), (a, b) => a < b).All(result => result);
        }
        if (rules == null || books == null || rules.skills == null || rules.combos == null ||
            rules.intelligence == null || rules.capacities == null ||
            rules.enduranceThresholds == null || rules.dailyLimits == null ||
            rules.startingStock < 0 || rules.refillStock <= 0 || rules.refillBase <= 0 || rules.refillIncrease < 0 ||
            rules.decayCoins < 0 || rules.healthPerEndurance <= 0 || !Positive(rules.daySeconds) ||
            !Positive(rules.deliverySeconds) || !Ordered(rules.enduranceThresholds) ||
            rules.dailyLimits.Length != rules.enduranceThresholds.Length || rules.dailyLimits.Any(value => value <= 0) ||
            !Ordered(rules.capacities.Select(step => step.knowledgeCoins)) ||
            rules.capacities.Any(step => step.simultaneous < 1 || step.waiting < 0) ||
            !Ordered(rules.intelligence.Select(tier => tier.threshold)) ||
            rules.intelligence.Any(tier => tier.decayDays <= 0 || !float.IsFinite(tier.success) || tier.success < 0 || tier.success > 1))
            throw new InvalidDataException("Study.xlsx: 학습 단계, 일일 한도 또는 재고 설정 오류");
        var skills = rules.skills.Select(skill => skill.id).ToHashSet();
        var ids = books.Select(book => book.id).ToHashSet();
        if (skills.Count != rules.skills.Length || ids.Count != books.Length ||
            rules.skills.Any(skill => string.IsNullOrWhiteSpace(skill.id) || string.IsNullOrWhiteSpace(skill.name) ||
                skill.ability < 0 || skill.ability > 3) ||
            books.Any(book => string.IsNullOrWhiteSpace(book.id) || string.IsNullOrWhiteSpace(book.name) ||
                string.IsNullOrWhiteSpace(book.author) || book.ability < 0 || book.ability > 3 ||
                book.requiredAbility < 0 || book.requiredKnowledge < 0 || book.reward < 0 || book.skillReward < 0 ||
                !skills.Contains(book.skill) || !Positive(book.seconds) || !Positive(book.generationSeconds)) ||
            rules.combos.Select(combo => combo.id).Distinct().Count() != rules.combos.Length ||
            rules.combos.Any(combo => string.IsNullOrWhiteSpace(combo.id) || string.IsNullOrWhiteSpace(combo.name) ||
                combo.books == null || combo.books.Length < 2 || combo.books.Distinct().Count() != combo.books.Length ||
                combo.books.Any(id => !ids.Contains(id)) || combo.coins?.Length != 4 ||
                combo.coins.Any(amount => amount < 0) || combo.skillReward < 0 ||
                (combo.skillReward > 0 && !skills.Contains(combo.skill))))
            throw new InvalidDataException("Study.xlsx: 책, 세부 능력 또는 콤보 설정 오류");
    }
}
