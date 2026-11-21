using System;
using UnityEngine;

[RequireComponent(typeof(Animator))]
public sealed class TaxCollectionView : MonoBehaviour
{
    private const float BowledCoinFlight = 12f / 60f;
    private static readonly int CollectState = Animator.StringToHash("Base Layer.Collect_ThenHold");

    private Animator animator;
    private Action completed;
    private TaxScaleView scale;
    private Transform[] coins;
    private Transform coinGrip, bowlGrip, bowl, originalBowl;
    private Vector3 pickupOffset, bowlOffset, endOffset;
    private Vector3[] depositOrigins;
    private Vector3 bowlCoinBase;
    private Quaternion bowlRestRotation;
    private float duration, startedAt, pickupTime, depositTime, bowlPickupTime;
    private bool useScale, picked, deposited, bowlPicked, playbackComplete;
    private bool pickupQueued, depositQueued, bowlPickupQueued;

    internal void Play(Transform[] paidCoins, bool fromScale, Vector3 boardPickup, TaxScaleView scaleView,
        Camera camera, Vector2 npcScreen, int layer, Action onComplete)
    {
        coins = paidCoins;
        useScale = fromScale;
        scale = scaleView;
        completed = onComplete;
        animator = GetComponent<Animator>();
        animator.enabled = false;
        animator.applyRootMotion = false;
        animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var clip = animator.runtimeAnimatorController.animationClips[0];
        duration = clip.length;
        // The imported clip events are the frame markers in FrameMarkers.json.
        foreach (AnimationEvent marker in clip.events)
        {
            switch (marker.functionName)
            {
                case nameof(OnTaxCoinsPicked):
                case nameof(OnDirectCoinsPicked): pickupTime = marker.time; break;
                case nameof(OnTaxCoinsDeposited): depositTime = marker.time; break;
                case nameof(OnTaxBowlPicked): bowlPickupTime = marker.time; break;
            }
        }

        var hand = transform.Find("Hand");
        coinGrip = hand.Find("CoinStackGrip");
        bowlGrip = hand.Find("BowlRimGrip");
        var authoredBowl = transform.Find("Bowl");
        var authoredPickup = transform.Find("PlayerCoinStart").localPosition;
        var authoredEnd = transform.Find("NpcHandoff").localPosition;
        transform.Find("PaymentCoins").gameObject.SetActive(false);
        authoredBowl.gameObject.SetActive(false);
        if (transform.Find("ScaleReference") is Transform previewScale)
            previewScale.gameObject.SetActive(false);
        foreach (Transform child in hand.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = layer;

        Vector3 pickup = useScale ? scale.PlayerStackPosition : boardPickup;
        Vector3 head = camera.WorldToScreenPoint(pickup);
        Vector3 handoff = camera.ScreenToWorldPoint(new Vector3(npcScreen.x, npcScreen.y, head.z + 1.5f));
        Vector3 authoredDirection = authoredEnd - authoredPickup;
        Vector3 direction = handoff - pickup;
        if (useScale)
        {
            originalBowl = scale.CollectionBowl;
            bowl = Instantiate(originalBowl, originalBowl.position, originalBowl.rotation);
            bowl.localScale = originalBowl.lossyScale;
            originalBowl.gameObject.SetActive(false);
            var floor = bowl.Find("Walnut inner floor");
            float floorY = floor != null ? BoardItemDrop.ItemBounds(floor).max.y : bowl.position.y;
            bowlCoinBase = bowl.InverseTransformPoint(new Vector3(bowl.position.x,
                floorY + paidCoins[0].position.y - BoardItemDrop.ItemBounds(paidCoins[0]).min.y, bowl.position.z));
            bowlRestRotation = bowl.rotation;
            authoredDirection = authoredBowl.localPosition - authoredPickup;
            direction = bowl.position - pickup;
        }

        Vector3 bowlPosition = bowl != null ? bowl.position : Vector3.zero;
        transform.localScale = Vector3.one * 4.25f;
        transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(direction, Vector3.up)) *
            Quaternion.Inverse(Quaternion.LookRotation(Vector3.ProjectOnPlane(authoredDirection, Vector3.up)));
        pickupOffset = pickup - transform.TransformVector(authoredPickup);
        bowlOffset = bowlPosition - transform.TransformVector(authoredBowl.localPosition);
        endOffset = handoff - transform.TransformVector(authoredEnd);
        transform.position = pickupOffset;
        if (bowl != null) bowl.SetParent(transform, true);

        if (!useScale)
            for (int i = 0; i < coins.Length; i++)
                coins[i].SetPositionAndRotation(pickup + Vector3.up * (i * TaxScaleView.StackSpacing), Quaternion.identity);
        startedAt = Time.unscaledTime;
        animator.enabled = true;
        animator.Play(CollectState, 0, 0f);
    }

    public void OnTaxCoinsPicked(int frame) => pickupQueued = true;
    public void OnDirectCoinsPicked(int frame) => pickupQueued = true;
    public void OnTaxCoinsDeposited(int frame) => depositQueued = true;
    public void OnTaxBowlPicked(int frame) => bowlPickupQueued = true;
    public void OnTaxCollectionComplete(int frame) => playbackComplete = true;

    private void LateUpdate()
    {
        if (completed == null) return;
        float time = Mathf.Min(duration, animator.GetCurrentAnimatorStateInfo(0).normalizedTime * duration);
        if (!useScale)
            transform.position = MoveBetween(pickupOffset, endOffset, pickupTime, duration, time);
        else if (time <= depositTime)
            transform.position = MoveBetween(pickupOffset, bowlOffset, pickupTime, depositTime, time);
        else
            transform.position = MoveBetween(bowlOffset, endOffset, bowlPickupTime, duration, time);

        if (bowl != null)
        {
            if (!bowlPicked)
                bowl.SetPositionAndRotation(originalBowl.position, bowlRestRotation);
            else
                bowl.rotation = bowlRestRotation;
        }
        if (pickupQueued && !picked)
        {
            picked = true;
            foreach (var coin in coins) coin.SetParent(coinGrip, true);
            if (useScale) scale.SetPlayerCount(0);
        }
        if (depositQueued && !deposited)
        {
            deposited = true;
            depositOrigins = new Vector3[coins.Length];
            for (int i = 0; i < coins.Length; i++)
            {
                coins[i].SetParent(bowl, true);
                depositOrigins[i] = coins[i].localPosition;
            }
        }
        if (deposited && !bowlPicked)
        {
            float drop = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(depositTime, depositTime + BowledCoinFlight, time));
            for (int i = 0; i < coins.Length; i++)
            {
                var target = bowlCoinBase + Vector3.up * (i * TaxScaleView.StackSpacing / bowl.lossyScale.y);
                coins[i].localPosition = Vector3.Lerp(depositOrigins[i], target, drop);
                coins[i].rotation = Quaternion.Slerp(coins[i].rotation, Quaternion.identity, drop);
            }
        }
        if (bowlPickupQueued && !bowlPicked)
        {
            bowlPicked = true;
            bowl.SetParent(bowlGrip, true);
        }

        if (playbackComplete || Time.unscaledTime - startedAt > duration + .5f)
        {
            var finish = completed;
            completed = null;
            finish();
        }
    }

    private static Vector3 MoveBetween(Vector3 from, Vector3 to, float start, float end, float time) =>
        Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, time)));

    internal void ReleaseVisuals()
    {
        completed = null;
        if (coins != null)
            foreach (var coin in coins)
                if (coin != null)
                {
                    coin.gameObject.SetActive(false);
                    Destroy(coin.gameObject);
                }
        if (bowl != null) bowl.gameObject.SetActive(false);
        if (originalBowl != null) originalBowl.gameObject.SetActive(true);
        scale?.SetPlayerCount(0);
        coins = null;
        bowl = originalBowl = null;
        scale = null;
    }

    private void OnDestroy() => ReleaseVisuals();
}
