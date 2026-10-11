using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;

// Read-only validation of the exact allocation instruction literals and current font fallback chain.
public static class GuideAllocationFontCheck
{
    public static string Verify()
    {
        string source=File.ReadAllText(Path.Combine(UnityEngine.Application.dataPath,
            "Decision_YYS/Scripts/GameSystem/DecisionManager.Guide.cs"));
        var block=Regex.Match(source,@"string text = ""(?<first>[^""]*)"" \+\s*\$""(?<last>[^""]*)"";");
        if(!block.Success) throw new Exception("Allocation instruction literals not found.");
        string first=block.Groups["first"].Value.Replace("\\n","\n");
        string last=block.Groups["last"].Value;
        if(first.Contains("·") || last.Contains("·")) throw new Exception("Unsupported middle dot remains.");
        var fonts=AssetDatabase.FindAssets("ChosunCentennial_ttf t:TMP_FontAsset")
            .Select(g=>AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(f=>f!=null && f.name=="ChosunCentennial_ttf SDF").ToArray();
        if(fonts.Length!=1) throw new Exception("Expected exactly one ChosunCentennial_ttf SDF asset: "+fonts.Length);
        var font=fonts[0];
        foreach(string mode in new[]{"기어 방향을 선택해","추가 모드","빼기 모드"})
        {
            string text=first+last.Replace("{GuideAllocationRules.Remaining(allocation)}","20").Replace("{mode}",mode);
            var missing=text.Distinct().Where(c=>!char.IsWhiteSpace(c) && !font.HasCharacter(c,true)).ToArray();
            if(missing.Length>0) throw new Exception("Missing allocation glyphs: "+string.Join(",",missing.Select(c=>"U+"+((int)c).ToString("X4"))));
        }
        return "All 3 allocation modes have supported glyphs. U+00B7 supported="+font.HasCharacter((char)183,true)+
            "; slash supported="+font.HasCharacter('/',true)+"; font="+AssetDatabase.GetAssetPath(font)+
            ". Font/scene/player saves unchanged.";
    }
}
