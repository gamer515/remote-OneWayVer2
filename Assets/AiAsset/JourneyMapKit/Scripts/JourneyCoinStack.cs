using UnityEngine;

namespace JourneyMapKit
{
    public sealed class JourneyCoinStack : MonoBehaviour
    {
        public GameObject[] coins;
        [SerializeField,Range(0,20)] int count=10;
        private GameObject[] tintedSlots;
        private Color milestoneTint;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColor = Shader.PropertyToID("_Color");
        public int Count=>count;
        public void SetCount(int newCount)
        {
            EnsureTwentySlots();
            count=Mathf.Clamp(newCount,0,coins==null?0:coins.Length);
            if(coins==null)return;
            for(int i=0;i<coins.Length;i++)if(coins[i])coins[i].SetActive(i<count);
        }

        public void SetMilestoneTint(Color tint)
        {
            if (coins == null || (tintedSlots == coins && milestoneTint == tint)) return;
            tintedSlots = coins;
            milestoneTint = tint;
            var properties = new MaterialPropertyBlock();
            for (int i = 4; i < coins.Length; i += 5)
            {
                if (coins[i] == null) continue;
                foreach (var renderer in coins[i].GetComponentsInChildren<Renderer>(true))
                {
                    renderer.GetPropertyBlock(properties);
                    properties.SetColor(BaseColor, tint);
                    properties.SetColor(LegacyColor, tint);
                    renderer.SetPropertyBlock(properties);
                }
            }
        }
        // 런타임에서만 표시 슬롯을 확장합니다. 원본 베이크 모델/프리팹은 변경하지 않습니다.
        void EnsureTwentySlots()
        {
            if (!Application.isPlaying || coins == null || coins.Length == 0 || coins.Length >= 20 || !coins[0]) return;
            int originalCount = coins.Length;
            GameObject template = coins[0];
            Vector3 first = template.transform.localPosition;
            Vector3 last = coins[originalCount - 1].transform.localPosition;
            float thicknessRatio = (originalCount - 1f) / 19f;
            var expanded = new GameObject[20];
            for (int i = 0; i < expanded.Length; i++)
            {
                expanded[i] = i < originalCount ? coins[i] : Instantiate(template, template.transform.parent);
                expanded[i].name = "SupplyCoin_" + (i + 1).ToString("D2");
            }
            for (int i = 0; i < expanded.Length; i++)
            {
                expanded[i].transform.localPosition = Vector3.Lerp(first, last, i / 19f);
                Vector3 scale = expanded[i].transform.localScale;
                scale.y *= thicknessRatio;
                expanded[i].transform.localScale = scale;
            }
            coins = expanded;
        }
        void OnEnable(){SetCount(count);}
    }
}
