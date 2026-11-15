using System;
using UnityEngine;

namespace OneWay.Loading
{
    // Keep existing serialized Battle/Decision values unchanged.
    public enum LoadingDestination { Battle = 0, Decision = 1, MainMenu = 2 }

    // Asset references keep the text available in a build without runtime Assets-folder IO.
    public sealed class LoadingTipCatalog : ScriptableObject
    {
        public TextAsset[] battleTips = Array.Empty<TextAsset>();
        public TextAsset[] decisionTips = Array.Empty<TextAsset>();

        public string Pick(LoadingDestination destination)
        {
            // Menu transitions have no gameplay tip category.
            var tips = destination == LoadingDestination.Battle ? battleTips :
                destination == LoadingDestination.Decision ? decisionTips : null;
            if (tips == null) return string.Empty;
            string chosen = string.Empty;
            int count = 0;
            foreach (var tip in tips)
            {
                if (tip == null) continue;
                string value = tip.text.Trim('\uFEFF', ' ', '\r', '\n', '\t');
                if (value.Length == 0) continue;
                if (UnityEngine.Random.Range(0, ++count) == 0) chosen = value;
            }
            return chosen;
        }
    }
}
