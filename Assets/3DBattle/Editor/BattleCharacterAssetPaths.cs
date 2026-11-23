using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Old one-off authoring scripts assemble paths from folder constants and clip names.
// Resolve only relocated assets by GUID, so those tools keep loading the same assets.
public static class BattleCharacterAssetPaths
{
    const string Manifest = "Assets/3DBattle/Authoring~/CharacterAssetMovePlan.json";
    sealed class Entry { public string from { get; set; } public string guid { get; set; } }
    static Dictionary<string, string> oldPaths;

    public static string Resolve(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        if (oldPaths == null)
        {
            oldPaths = new Dictionary<string, string>(StringComparer.Ordinal);
            if (File.Exists(Manifest))
                foreach (var entry in JsonConvert.DeserializeObject<Entry[]>(File.ReadAllText(Manifest)))
                    oldPaths[entry.from] = entry.guid;
        }
        if (oldPaths.TryGetValue(path.Replace('\\', '/'), out string guid))
        {
            string current = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(current)) return current;
        }
        return path;
    }
    public static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(Resolve(path));
    public static Object[] LoadAll(string path) => AssetDatabase.LoadAllAssetsAtPath(Resolve(path));
    public static Object LoadMain(string path) => AssetDatabase.LoadMainAssetAtPath(Resolve(path));
}
