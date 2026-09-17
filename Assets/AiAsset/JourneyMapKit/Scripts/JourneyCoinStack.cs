using UnityEngine;

namespace JourneyMapKit
{
    public sealed class JourneyCoinStack : MonoBehaviour
    {
        public GameObject[] coins;
        [SerializeField,Range(0,12)] int count=10;
        public int Count=>count;
        public void SetCount(int newCount)
        {
            count=Mathf.Clamp(newCount,0,coins==null?0:coins.Length);
            if(coins==null)return;
            for(int i=0;i<coins.Length;i++)if(coins[i])coins[i].SetActive(i<count);
        }
        void OnEnable(){SetCount(count);}
    }
}
