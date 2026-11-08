using UnityEngine;

namespace LearningDispenserAssetKit
{
    [DisallowMultipleComponent]
    public sealed class DeskCalendarDisplay : MonoBehaviour
    {
        [SerializeField] private string date = "1005-01-01";
        [SerializeField] private string countdown = "D-2";
        [SerializeField] private bool isNight = true;

        [Header("Display references")]
        [SerializeField] private TextMesh dateLabel;
        [SerializeField] private TextMesh countdownLabel;
        [SerializeField] private GameObject dayIcon;
        [SerializeField] private GameObject nightIcon;

        private void Awake() => RefreshDisplay();
        private void OnValidate() => RefreshDisplay();

        public void SetDisplay(string newDate, string newCountdown, bool night)
        {
            date = newDate;
            countdown = newCountdown;
            isNight = night;
            RefreshDisplay();
        }

        private void RefreshDisplay()
        {
            if (dateLabel && dateLabel.text != date) dateLabel.text = date;
            if (countdownLabel && countdownLabel.text != countdown) countdownLabel.text = countdown;
            if (dayIcon && dayIcon.activeSelf == isNight) dayIcon.SetActive(!isNight);
            if (nightIcon && nightIcon.activeSelf != isNight) nightIcon.SetActive(isNight);
        }
    }
}
