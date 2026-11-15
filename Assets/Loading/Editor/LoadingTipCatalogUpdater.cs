using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace OneWay.Loading.Editor
{
    [InitializeOnLoad]
    public sealed class LoadingTipCatalogUpdater : AssetPostprocessor, IPreprocessBuildWithReport
    {
        public const string CatalogPath = "Assets/Loading/LoadingTips.asset";
        const string Root = "Assets/Loading/LoadingText";
        static bool queued;
        public int callbackOrder => 0;

        static LoadingTipCatalogUpdater()
        {
            QueueSync();
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode) Sync();
            };
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Concat(deleted).Concat(moved).Concat(movedFrom)
                .Any(p => p == Root || p.StartsWith(Root + "/", StringComparison.Ordinal))) QueueSync();
        }

        static void QueueSync()
        {
            if (queued) return;
            queued = true;
            EditorApplication.delayCall += () => { queued = false; Sync(); };
        }

        public static LoadingTipCatalog Sync()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Loading")) return null;
            var catalog = AssetDatabase.LoadAssetAtPath<LoadingTipCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<LoadingTipCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            var battle = ReadFolder(Root + "/LoadBattleScene");
            var decision = ReadFolder(Root + "/LoadDecisionScene");
            if (!(catalog.battleTips ?? Array.Empty<TextAsset>()).SequenceEqual(battle) ||
                !(catalog.decisionTips ?? Array.Empty<TextAsset>()).SequenceEqual(decision))
            {
                catalog.battleTips = battle;
                catalog.decisionTips = decision;
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssetIfDirty(catalog);
            }
            return catalog;
        }

        static TextAsset[] ReadFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path)) return Array.Empty<TextAsset>();
            return AssetDatabase.FindAssets("t:TextAsset", new[] { path })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<TextAsset>)
                .Where(a => a != null).ToArray();
        }

        public void OnPreprocessBuild(BuildReport report) => Sync();
    }
}
