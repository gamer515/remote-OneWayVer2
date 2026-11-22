using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class DecisionManager
{
    private sealed class NpcRewardDrop
    {
        internal int Ability;
        internal GameObject BoardCoin;
        internal GameObject FlyingCoin;
        internal Vector3 Destination;
        internal Vector3 GripOffset;
        internal bool Settled;
    }

    private readonly Queue<List<NpcRewardDrop>> npcRewardQueue = new();
    private List<NpcRewardDrop> activeNpcReward;
    private Coroutine npcRewardRoutine;
    private GameObject npcRewardRoot;

    private void EnqueueNpcRewardPresentation(int[] before, int[] after)
    {
        if (journeyCoinSupply?.CanStageCoins != true || npcPaymentPickupRigPrefab == null) return;
        var drops = new List<NpcRewardDrop>();
        Bounds grid = StudyGrid.bounds;
        Vector3 center = new Vector3(grid.center.x, grid.max.y, grid.center.z);
        for (int ability = 0; ability < after.Length; ability++)
            for (int i = before[ability]; i < after[ability]; i++)
            {
                // Reserve the board location in the same checkpoint as the existing reward.
                // A scene change can reveal it or restore it without replaying the reward.
                float angle = drops.Count * 2.399963f;
                float radius = drops.Count == 0 ? 0f : .48f * Mathf.Sqrt(drops.Count);
                var point = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                var model = journeyCoinSupply.ReserveNpcRewardCoin(ability, point);
                drops.Add(new NpcRewardDrop
                {
                    Ability = ability,
                    BoardCoin = model,
                    Destination = model != null ? model.transform.position : journeyCoinSupply.RewardCanisterOpening(ability)
                });
            }
        if (drops.Count == 0) return;
        npcRewardQueue.Enqueue(drops);
        npcRewardRoutine ??= StartCoroutine(PlayNpcRewards());
    }

    private IEnumerator PlayNpcRewards()
    {
        // Let the existing outcome present its dialogue and finish its checkpoint first.
        yield return null;
        while (npcRewardQueue.Count > 0)
        {
            while (npcPaymentRoot != null || endTaxCollection != null || collectingTax)
                yield return null;
            activeNpcReward = npcRewardQueue.Dequeue();
            yield return PlayNpcRewardHand(activeNpcReward);
            RevealNpcReward(activeNpcReward);
            activeNpcReward = null;
            ClearNpcRewardRig();
        }
        npcRewardRoutine = null;
    }

    private IEnumerator PlayNpcRewardHand(List<NpcRewardDrop> drops)
    {
        Camera camera = journeyBoardInput.inputCamera;
        Bounds board = StudyGrid.bounds;
        Vector3 center = new Vector3(board.center.x, board.max.y, board.center.z);
        Vector3 hover = center + Vector3.up * .85f;
        Vector2 screen = uiController.StoryScreenEntrance;
        float depth = camera.WorldToScreenPoint(hover).z;
        Vector3 entry = camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, depth + 1f));
        Vector3 approach = Vector3.ProjectOnPlane(entry - center, Vector3.up);
        Quaternion facing = approach.sqrMagnitude > .001f ? Quaternion.LookRotation(approach) : Quaternion.identity;

        npcRewardRoot = new GameObject("NPC Reward Hand");
        npcRewardRoot.transform.SetParent(transform, false);
        var rig = Instantiate(npcPaymentPickupRigPrefab, npcRewardRoot.transform, false);
        rig.transform.rotation = facing;
        var hand = rig.transform.Find("Hand");
        var animator = rig.GetComponent<Animator>();
        if (hand == null || animator == null) yield break;
        if (rig.transform.Find("Bowl") is Transform bowl) bowl.gameObject.SetActive(false);
        foreach (var node in rig.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = StudyGrid.gameObject.layer;
        foreach (var collider in rig.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        hand.gameObject.SetActive(true);
        animator.enabled = true;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Rebind();
        animator.speed = 0f;
        Transform palm = hand.Find("CoinBowlGrip") ?? hand;
        var skin = hand.GetComponentInChildren<SkinnedMeshRenderer>();
        void Pose(float time, Vector3 palmPosition)
        {
            animator.Play(PickupState, 0, time);
            animator.Update(0f);
            rig.transform.position += palmPosition - palm.position;
        }

        Pose(PickupGripTime, entry);
        if (skin != null)
        {
            float width = Mathf.Max(skin.bounds.size.x, skin.bounds.size.y, skin.bounds.size.z);
            if (width > .001f) rig.transform.localScale *= Mathf.Clamp(1.40f / width, .35f, 20f);
        }
        Pose(PickupGripTime, entry);
        for (int i = 0; i < drops.Count; i++)
        {
            var drop = drops[i];
            drop.GripOffset = new Vector3((i % 3 - 1) * .16f, i / 3 * .055f, (i % 2 - .5f) * .12f);
            drop.FlyingCoin = journeyCoinSupply.CreateLearningCoinVisual(drop.Ability, palm.position, npcRewardRoot.transform);
        }

        const float carryTime = .36f, openTime = .19f, fallTime = .36f, holdTime = .12f, returnTime = .33f;
        float stagger = Mathf.Min(.045f, .21f / Mathf.Max(1, drops.Count - 1));
        float lastLanding = carryTime + openTime + (drops.Count - 1) * stagger + fallTime;
        float retreat = lastLanding + holdTime;
        for (float clock = 0f; clock < retreat + returnTime; clock += Time.unscaledDeltaTime)
        {
            if (collectingTax || endTaxCollection != null || journeyCoinSupply.HasBoardPayment) yield break;
            float move = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(clock / carryTime));
            float open = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(carryTime, carryTime + openTime, clock));
            float back = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(retreat, retreat + returnTime, clock));
            Vector3 at = Vector3.Lerp(Vector3.Lerp(entry, hover, move), entry, back);
            at.y += .28f * Mathf.Sin(Mathf.PI * move) + .20f * Mathf.Sin(Mathf.PI * back);
            Pose(Mathf.Lerp(PickupGripTime, .16f, open), at);
            for (int i = 0; i < drops.Count; i++)
            {
                var drop = drops[i];
                if (drop.Settled || drop.FlyingCoin == null) continue;
                float t = (clock - carryTime - openTime - i * stagger) / fallTime;
                Vector3 gripOffset = facing * drop.GripOffset;
                if (t < 0f)
                {
                    drop.FlyingCoin.transform.position = palm.position + gripOffset;
                    continue;
                }
                if (t >= 1f)
                {
                    SettleNpcRewardDrop(drop);
                    continue;
                }
                Vector3 start = hover + gripOffset;
                float travel = Mathf.Clamp01(t / .72f);
                Vector3 position = Vector3.Lerp(start, drop.Destination, travel * travel);
                float hop = Mathf.InverseLerp(.72f, 1f, t);
                position.y += Mathf.Sin(hop * Mathf.PI) * .12f;
                Quaternion rotation = Quaternion.Euler(Mathf.Sin(t * Mathf.PI) * (25f + i % 3 * 11f), 0f, 0f);
                drop.FlyingCoin.transform.SetPositionAndRotation(position, rotation);
            }
            yield return null;
        }
    }

    private void SettleNpcRewardDrop(NpcRewardDrop drop)
    {
        if (drop.Settled) return;
        drop.Settled = true;
        if (drop.FlyingCoin != null) drop.FlyingCoin.SetActive(false);
        journeyCoinSupply?.RevealNpcRewardCoin(drop.BoardCoin);
    }

    private void RevealNpcReward(List<NpcRewardDrop> drops)
    {
        if (drops == null) return;
        foreach (var drop in drops) SettleNpcRewardDrop(drop);
    }

    private void ClearNpcRewardRig()
    {
        if (npcRewardRoot != null)
        {
            npcRewardRoot.SetActive(false);
            Destroy(npcRewardRoot);
        }
        npcRewardRoot = null;
    }

    private void StopNpcRewardPresentation()
    {
        if (npcRewardRoutine != null) StopCoroutine(npcRewardRoutine);
        npcRewardRoutine = null;
        RevealNpcReward(activeNpcReward);
        activeNpcReward = null;
        while (npcRewardQueue.Count > 0) RevealNpcReward(npcRewardQueue.Dequeue());
        ClearNpcRewardRig();
    }
}
