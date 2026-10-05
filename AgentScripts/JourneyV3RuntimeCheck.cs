using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using JourneyMapKit;

// Isolated Play-mode smoke test: disables only story startup temporarily so no save is written.
public static class JourneyV3RuntimeCheck
{
    static Transform Find(string name) => SceneManager.GetSceneByPath("Assets/Scenes/DecisionScene.unity")
        .GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Transform>(true)).Single(t => t.name == name);
    static object Field(object o, string name) => o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);
    static async Task Pump(int milliseconds)
    {
        // A hidden Game tab can stall Player frames even while the Editor's Pipeline keeps ticking.
        var until=DateTime.UtcNow.AddMilliseconds(milliseconds);
        while(DateTime.UtcNow<until)
        {
            EditorApplication.QueuePlayerLoopUpdate();
            await Task.Delay(16);
        }
    }
    public static object Prepare()
    {
        var manager = Find("Decision_Manager").gameObject;
        SessionState.SetBool("JourneyV3ManagerActive",manager.activeSelf);
        manager.SetActive(false);
        return "Story startup disabled only for temporary test; saves remain untouched";
    }
    public static async Task<object> Check()
    {
        if (!Application.isPlaying) throw new Exception("Play required");
        var input = Find("JourneyController").GetComponent<JourneyBoardInput>();
        var inventory = Find("Coin_Drop").GetComponent<CoinDropController>();
        inventory.InitializeInventory(new[] {10,10,10,10});
        var manager = Find("Decision_Manager").GetComponent<DecisionManager>();
        var coins = (GameObject[])Field(manager,"journeyCoinPrefabs");
        int saves = 0;
        var supply = new JourneyCoinSupplyController(input,coins,inventory,values => saves++);
        if (!supply.IsReady) throw new Exception("Supply not ready");
        var dispense = typeof(JourneyCoinSupplyController).GetMethod("Dispense",BindingFlags.NonPublic|BindingFlags.Instance);
        for (int i=0;i<4;i++) dispense.Invoke(supply,new object[] {i});
        var stock = input.board.supplyStacks.Select(s => s.coins.Count(c => c.activeSelf)).ToArray();
        var active = input.board.transform.Find("JourneyActiveCoins").GetComponentsInChildren<BettingCoin>();
        bool spawnMatches = active.Length == 4 && Enumerable.Range(0,4).All(i =>
            Vector3.Distance(active[i].transform.position,input.board.supplySpawns[i].position) < .001f);
        await Pump(1200);
        bool onTray = active.All(c => c.transform.position.y > -9f);
        var bag = Find("FantasyBackpack").GetComponent<BackpackInventoryController>();
        bag.Initialize(Array.Empty<string>(),null);
        var animation = (Animation)Field(bag,"backpackAnimation");
        var lid = animation.transform.Find("LidPivot");
        float closed = Quaternion.Angle(Quaternion.identity,lid.localRotation);
        bag.Open();
        bool openClip = animation.IsPlaying("Backpack_OpenLid");
        await Pump(900);
        bool panelOpen = ((GameObject)Field(bag,"inventoryPanel")).activeSelf;
        float opened = Quaternion.Angle(Quaternion.identity,lid.localRotation);
        bag.Close(); await Pump(1000);
        bool panelClosed = !((GameObject)Field(bag,"inventoryPanel")).activeSelf;
        float closedAgain = Quaternion.Angle(Quaternion.identity,lid.localRotation);
        Physics.SyncTransforms();
        var camera = input.inputCamera;
        var collider = (Collider)Field(bag,"backpackCollider");
        bool bagPick = Physics.Raycast(camera.ScreenPointToRay(camera.WorldToScreenPoint(collider.bounds.center)),out var hit,1000f,
            camera.cullingMask,QueryTriggerInteraction.Ignore) && hit.collider == collider;
        var bridge = Find("DuelMiniGame").GetComponent<DuelMiniGameBridge>();
        bridge.BeginDuel(null,3,10,null); await Pump(100);
        bool duelRunning = bridge.IsRunning && bridge.CurrentKnightTarget != null;
        var knight = bridge.CurrentKnightTarget;
        var knightPose = Find("KnightCharacter_Copy");
        bool knightPreserved = knight != null && Vector3.Distance(knight.transform.position,knightPose.position)<.0001f &&
            Vector3.Distance(knight.transform.localScale,Vector3.one*.6f)<.0001f;
        bool boardLockedDuringDuel = !input.InputEnabled;
        bridge.CleanupDuel();
        return new { ready = supply.IsReady, stock, inventory = inventory.RemainingCoins, spawnMatches, onTray,
            openClip, panelOpen, panelClosed, closed, opened, closedAgain, bagPick,
            duelRunning, knightPreserved, boardLockedDuringDuel, boardRestored = input.InputEnabled,
            isolatedSaveCallbackCalls = saves };
    }
    public static object Restore()
    {
        Find("Decision_Manager").gameObject.SetActive(SessionState.GetBool("JourneyV3ManagerActive",true));
        return "Story startup restored without scene save";
    }
    public static async Task<object> CheckBag()
    {
        var bag=Find("FantasyBackpack").GetComponent<BackpackInventoryController>();
        var animation=(Animation)Field(bag,"backpackAnimation");
        var before=new {bag.IsOpen, locked=Field(bag,"inputLocked"), bag.enabled,
            active=bag.gameObject.activeInHierarchy, animationEnabled=animation.enabled,
            clips=animation.Cast<AnimationState>().Select(c=>new{c.name,c.length}).ToArray()};
        typeof(BackpackInventoryController).GetMethod("SetClosedImmediately",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(bag,null);
        bag.Open();
        var immediately=new {bag.IsOpen, locked=Field(bag,"inputLocked"), playing=animation.IsPlaying("Backpack_OpenLid")};
        await Pump(1500);
        var after=new {bag.IsOpen,locked=Field(bag,"inputLocked"),panel=((GameObject)Field(bag,"inventoryPanel")).activeSelf,
            angle=animation.transform.Find("LidPivot").localEulerAngles.ToString("F2")};
        bag.Close(); await Pump(1300);
        return new {before,immediately,after,closedPanel=!((GameObject)Field(bag,"inventoryPanel")).activeSelf,
            closedAngle=animation.transform.Find("LidPivot").localEulerAngles.ToString("F2")};
    }
}
