using System;
using System.Linq;
using UnityEditor;

public sealed class ChapterContentReload : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(string[] imported,string[] deleted,string[] moved,string[] movedFrom)
    {
        if(imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(p=>p.StartsWith("Assets/Decision_YYS/Resources/Chapters/",StringComparison.Ordinal) || p.StartsWith("Assets/Decision_YYS/Resources/LocalStory/",StringComparison.Ordinal)))
            ChapterContentCatalog.Reset();
    }
}
