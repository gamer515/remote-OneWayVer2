using UnityEngine;
using UnityEngine.UI;

namespace OneWay.Battle3D
{
    [DisallowMultipleComponent]
    public sealed class BattleCombatModeUI : MonoBehaviour
    {
        public BattlePlayerController player;
        public Image attackIcon;
        public Image parryIcon;
        public Color selected = Color.white;
        public Color unselected = new Color(.42f, .47f, .55f, .3f);

        void OnEnable() => Refresh();
        void LateUpdate() => Refresh();

        public void Refresh()
        {
            bool parry = player != null && player.IsDefenseMode;
            SetColour(attackIcon, parry ? unselected : selected);
            SetColour(parryIcon, parry ? selected : unselected);
        }

        static void SetColour(Image icon, Color colour)
        {
            if (icon != null && icon.color != colour) icon.color = colour;
        }
    }
}
