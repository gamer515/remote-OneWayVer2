using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using OneWay.Battle3D;
public static class InspectDebugCombatFeel
{
    public static string Run()
    {
        var set = BattleCharacterAssetPaths.Load<NewPlayerAttackSet>("Assets/3DBattle/NewPlayer/PlayableAttacks/NewPlayerAttackSet.asset");
        return JsonConvert.SerializeObject(set.attacks.Select(m => new { m.label, frames = m.Frames, m.chainFrame,
            jump = Enumerable.Range(0, (int)m.Frames + 1).Where(i => m.jump.Evaluate(i) > .001f)
                .Select(i => new { frame = i, height = m.jump.Evaluate(i) * 20 }).ToArray(),
            forwardStart = m.forward.Evaluate(0), forwardChain = m.forward.Evaluate(m.chainFrame),
            forwardEnd = m.forward.Evaluate(m.Frames), clip = AssetDatabase.GetAssetPath(m.clip) }), Formatting.Indented);
    }
}
