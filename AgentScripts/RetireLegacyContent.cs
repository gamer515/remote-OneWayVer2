using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Reflection;

public static class RetireLegacyContent
{
    private const string Root = "Assets/Decision_YYS/Resources/Chapters/";
    private const string Archive = "Archive/LegacyContent/20261102/";
    private static JObject Read(string name) => JObject.Parse(File.ReadAllText(Root + name + ".json"));
    private static void Write(string name, JObject data) => File.WriteAllText(Root + name + ".json", data.ToString());

    public static object Apply()
    {
        if (Application.isPlaying || SceneManager.GetActiveScene().isDirty) throw new Exception("Expected saved Edit mode scene.");
        string previous = SceneManager.GetActiveScene().path;
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/DecisionScene.unity");
        string archive = Path.GetFullPath(Archive);
        Directory.CreateDirectory(archive);
        foreach (string name in new[] { "Catalog", "Omnibus" })
            if (!File.Exists(archive + "/" + name + ".before.json")) File.Copy(Root + name + ".json", archive + "/" + name + ".before.json");
        var catalog = Read("Catalog");
        catalog["chapters"] = new JArray(((JArray)catalog["chapters"]).Where(c => (string)c["id"] == "Chapter_1"));
        catalog["stories"] = new JArray(((JArray)catalog["stories"]).Where(s => (string)s["characterId"] == "Brook"));
        Write("Catalog", catalog);
        var omnibus = Read("Omnibus");
        omnibus["legacyChapterIds"] = new JArray("Initial", "Chapter_1", "Chapter_2");
        omnibus["chapters"] = new JArray(((JArray)omnibus["chapters"]).Where(c => (string)c["chapterId"] == "Chapter_1"));
        Write("Omnibus", omnibus);
        var people = Read("Chapter_01/Characters");
        people["characters"] = new JArray(((JArray)people["characters"]).Where(c => (string)c["characterId"] != "Instructor"));
        Write("Chapter_01/Characters", people);
        var terrain = Read("Chapter_01/Terrain");
        var paths = ((JArray)catalog["stories"]).Select(s => (string)s["path"]).ToArray();
        terrain["actions"] = new JObject(((JObject)terrain["actions"]).Properties().Where(p => paths.Contains(p.Name)).Select(p => new JProperty(p.Name, p.Value.DeepClone())));
        Write("Chapter_01/Terrain", terrain);
        var env = UnityEngine.Object.FindFirstObjectByType<EnvController>(FindObjectsInactive.Include);
        var serialized = new SerializedObject(env);
        Keep(serialized.FindProperty("groundEntries"), "groundId", "mine_chunk");
        Keep(serialized.FindProperty("terrainPrefabCatalog.characterEntries"), "prefabId", "mine_miner");
        serialized.FindProperty("groundPrefab").objectReferenceValue = serialized.FindProperty("groundEntries").GetArrayElementAtIndex(0).FindPropertyRelative("prefab").objectReferenceValue;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        string[] retired = {
            Root + "Chapter_00", Root + "Chapter_02", Root + "Chapter_01/Chapter_01_01/Instructor",
            "Assets/Decision_YYS/Prafabs/Terrain_Initial_Village_01.prefab",
            "Assets/Decision_YYS/Prafabs/Terrain_Initial_Village_02.prefab" };
        foreach (string path in retired) Move(path);
        AssetDatabase.Refresh();
        AssetDatabase.SaveAssets();
        ChapterContentCatalog.Reset();
        if (previous != scene.path && !string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous);
        var result = new { activeChapters = 1, activeStories = 1, characters = new[] { "Brook", "Dor" }, archivedPaths = retired, archive };
        File.WriteAllText(archive + "/manifest.json", Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
        return result;
    }

    private static void Keep(SerializedProperty entries, string field, string id)
    {
        for (int i = entries.arraySize - 1; i >= 0; i--)
            if (entries.GetArrayElementAtIndex(i).FindPropertyRelative(field).stringValue != id) entries.DeleteArrayElementAtIndex(i);
        if (entries.arraySize != 1) throw new Exception("Expected one retained entry: " + id);
    }

    public static object RefreshNpcTemplates()
    {
        var field = typeof(SaveIOService).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
        object previous = field.GetValue(null);
        string folder = Path.GetFullPath("output/MineChapterReview/CurrentNpcConfiguration");
        var storage = (SaveIOService)typeof(SaveIOService).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(string) }, null).Invoke(new object[] { folder });
        field.SetValue(null, storage);
        try
        {
            ChapterContentCatalog.Reset();
            var contexts = LocalStoryNotebook.ConfiguredContexts(storage, Array.Empty<string>());
            string template = "Assets/StreamingAssets/LocalStory/Chapters/Chapter_01.xlsx";
            string backup = Path.GetFullPath(Archive + template);
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            if (!File.Exists(backup)) File.Copy(template, backup);
            File.Copy(LocalStoryNotebook.ChapterPath(storage, "Chapter_1"), template, true);
            Move("Assets/StreamingAssets/LocalStory/Chapters/Chapter_00.xlsx");
            Move("Assets/StreamingAssets/LocalStory/Chapters/Chapter_02.xlsx");
            AssetDatabase.Refresh();
            return new { characters = contexts.Select(c => c.characterId).ToArray(), templates = new[] { template } };
        }
        finally { field.SetValue(null, previous); ChapterContentCatalog.Reset(); }
    }

    private static void Move(string path)
    {
        string root = Path.GetFullPath(".").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string source = Path.GetFullPath(path), target = Path.GetFullPath(Archive + path);
        string archiveRoot = Path.GetFullPath(Archive).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!source.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !target.StartsWith(archiveRoot, StringComparison.OrdinalIgnoreCase))
            throw new Exception("Archive path escaped workspace.");
        if (!File.Exists(source) && !Directory.Exists(source)) return;
        if (File.Exists(target) || Directory.Exists(target)) throw new Exception("Archive already exists: " + target);
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        FileUtil.MoveFileOrDirectory(source, target);
        if (File.Exists(source + ".meta")) FileUtil.MoveFileOrDirectory(source + ".meta", target + ".meta");
    }
}
