using UnityEngine;

namespace JourneyMapKit
{
    /// <summary>Preview-scene behavior only; deliberately separate from the user's game rules.</summary>
    public sealed class JourneyDemoController : MonoBehaviour
    {
        public JourneyBoardInput input;
        public GameObject[] coinPrefabs;
        public JourneyCoinStack[] supplies;
        public Transform coinContainer;
        int selected;
        void OnEnable(){input.onYellowPressed.AddListener(DropOnRamp);input.onGearSelected.AddListener(Select);}
        void OnDisable(){if(input){input.onYellowPressed.RemoveListener(DropOnRamp);input.onGearSelected.RemoveListener(Select);}}
        public void Select(int index){if(index>=0&&index<coinPrefabs.Length)selected=index;}
        public void RequestCoin(int index){Select(index);DropOnRamp();}
        public void DropOnRamp()
        {
            if(supplies[selected].Count==0)return;
            supplies[selected].SetCount(supplies[selected].Count-1);
            Vector3 p=Vector3.Lerp(input.board.chuteEntry.position,input.board.chuteExit.position,.22f)+Vector3.up*.18f;
            var coin=Instantiate(coinPrefabs[selected],p,Quaternion.identity,coinContainer);
            SetLayer(coin,input.board.gameObject.layer);
        }
        static void SetLayer(GameObject go,int layer){go.layer=layer;foreach(Transform child in go.transform)SetLayer(child.gameObject,layer);}
        public void ResetPreview()
        {
            foreach(Transform coin in coinContainer)Destroy(coin.gameObject);
            foreach(var supply in supplies)supply.SetCount(10);
        }
    }
}
