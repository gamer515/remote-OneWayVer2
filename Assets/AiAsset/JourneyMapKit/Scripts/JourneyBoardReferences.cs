using UnityEngine;

namespace JourneyMapKit
{
    public sealed class JourneyBoardReferences : MonoBehaviour
    {
        public Transform gear;
        public SphereCollider gearHandleCollider;
        public Collider yellowButton;
        public Transform traySpawn;
        public Transform chuteEntry;
        public Transform chuteExit;
        public Transform[] coinEntrances;
        public Collider trayFloor;
        [HideInInspector] public Collider[] supplyButtons;
        [HideInInspector] public Transform[] supplySpawns;
        [HideInInspector] public JourneyCoinStack[] supplyStacks;
    }
}
