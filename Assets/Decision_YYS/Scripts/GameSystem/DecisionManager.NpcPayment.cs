using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;

public partial class DecisionManager
{
    [SerializeField] private GameObject npcPaymentPickupRigPrefab;
    private const float PickupGripTime = 32f / 60f;
    private static readonly int PickupState = Animator.StringToHash("Base Layer.Pickup_ThenHold");

    private Transform npcPaymentRoot;
    private Coroutine npcPaymentRoutine;
    private Transform npcPaymentBowl;
    private Bounds npcPaymentBowlBounds;
    private bool npcPaymentBowlReady, npcPaymentConfirmed;
    private readonly Queue<(Vector3 entry, Rigidbody body, Action<bool> settled)> npcPaymentDrops = new();

    private bool CanShowNpcPaymentBowl => npcPaymentPickupRigPrefab != null &&
        npcPaymentPickupRigPrefab.GetComponent<Animator>() != null &&
        npcPaymentPickupRigPrefab.transform.Find("Hand") != null &&
        npcPaymentPickupRigPrefab.transform.Find("Bowl") is Transform bowl &&
        bowl.GetComponent<ParentConstraint>() != null &&
        bowl.Find("Coin stack bases") is Transform stacks && stacks.childCount > 0;

    private void BeginNpcPaymentPresentation()
    {
        Bounds grid = StudyGrid.bounds;
        float diameter = Mathf.Min(grid.size.x * .24f, grid.size.z * .68f);
        npcPaymentRoot = new GameObject("NPC Toll Presentation").transform;
        npcPaymentRoot.SetParent(transform);
        npcPaymentRoot.position = new Vector3(grid.center.x, grid.max.y, grid.center.z);

        var rig = Instantiate(npcPaymentPickupRigPrefab, npcPaymentRoot, false);
        var animator = rig.GetComponent<Animator>();
        animator.enabled = false;
        npcPaymentBowl = rig.transform.Find("Bowl");
        var constraint = npcPaymentBowl.GetComponent<ParentConstraint>();
        constraint.constraintActive = false;
        var hand = rig.transform.Find("Hand");
        hand.gameObject.SetActive(false);
        if (npcPaymentBowl.Find("PickupCoinContents") is Transform previewCoins)
            previewCoins.gameObject.SetActive(false);
        foreach (Transform child in rig.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = StudyGrid.gameObject.layer;
        foreach (var collider in rig.GetComponentsInChildren<Collider>(true)) collider.enabled = false;

        journeyCoinSupply.PaymentBowl = npcPaymentBowl;
        Vector3 size = BoardItemDrop.ItemBounds(npcPaymentBowl).size;
        rig.transform.localScale *= diameter / Mathf.Max(size.x, size.z);
        constraint.SetTranslationOffset(0, constraint.GetTranslationOffset(0) * rig.transform.lossyScale.x);
        Bounds bounds = BoardItemDrop.ItemBounds(npcPaymentBowl);
        rig.transform.position += Vector3.up * (grid.max.y - bounds.min.y);
        npcPaymentRoutine = StartCoroutine(PlayNpcPayment(npcPaymentBowl, animator, hand, constraint, diameter));
    }

    private Vector3? NpcPaymentBowlOpening(Ray ray)
    {
        if (!npcPaymentBowlReady || npcPaymentConfirmed || npcPaymentBowl == null) return null;
        Bounds bounds = npcPaymentBowlBounds;
        // The opening is visible from the rim down to the inner floor in the tilted board view.
        foreach (float height in new[] { bounds.max.y, bounds.center.y, bounds.min.y + bounds.size.y * .2f })
        {
            var plane = new Plane(Vector3.up, new Vector3(0f, height, 0f));
            if (!plane.Raycast(ray, out float distance)) continue;
            Vector3 hit = ray.GetPoint(distance);
            float x = (hit.x - bounds.center.x) / (bounds.extents.x * .72f);
            float z = (hit.z - bounds.center.z) / (bounds.extents.z * .72f);
            if (x * x + z * z <= 1f) return new Vector3(hit.x, bounds.max.y, hit.z);
        }
        return null;
    }

    private void QueueNpcPaymentCoin(Vector3 entry, Rigidbody body, Action<bool> settled) =>
        npcPaymentDrops.Enqueue((entry, body, settled));

    private IEnumerator PlayNpcPayment(Transform bowl, Animator animator, Transform hand, ParentConstraint constraint, float diameter)
    {
        Vector3 landing = bowl.position;
        Camera camera = journeyBoardInput.inputCamera;
        Vector3 landingOnScreen = camera.WorldToScreenPoint(landing);
        Vector2 entranceOnScreen = uiController.StoryScreenEntrance;
        Vector3 screenEntrance = camera.ScreenToWorldPoint(new Vector3(
            entranceOnScreen.x, entranceOnScreen.y, landingOnScreen.z + diameter));
        Vector3 towardScreen = Vector3.ProjectOnPlane(screenEntrance - landing, Vector3.up);
        animator.transform.rotation = Quaternion.LookRotation(towardScreen, Vector3.up);
        yield return TossNpcPaymentBowl(bowl, screenEntrance, landing, diameter);
        npcPaymentBowlBounds = BoardItemDrop.ItemBounds(bowl);
        foreach (var collider in bowl.GetComponentsInChildren<Collider>()) collider.enabled = true;
        float floorLift = npcPaymentBowlBounds.size.y * .36f;
        if (bowl.Find("Walnut inner floor") is Transform floor &&
            bowl.GetComponentsInChildren<Collider>().FirstOrDefault(collider => collider.name == "Floor") is Collider floorCollider)
        {
            floor.position += Vector3.up * floorLift;
            floorCollider.transform.position += Vector3.up * floorLift;
        }
        Physics.SyncTransforms();
        npcPaymentBowlReady = true;
        Vector3 coinDisplayOffset = towardScreen.normalized * (diameter * .06f);
        var stacks = bowl.Find("Coin stack bases").Cast<Transform>()
            .OrderByDescending(stack => Vector3.Dot(stack.position - landing, towardScreen)).ToArray();
        int settledCoins = 0;
        while (!npcPaymentConfirmed || npcPaymentDrops.Count > 0)
        {
            if (npcPaymentDrops.Count == 0)
            {
                yield return null;
                continue;
            }
            var drop = npcPaymentDrops.Dequeue();
            Vector3 stack = stacks[settledCoins % stacks.Length].position + coinDisplayOffset;
            yield return DropNpcPaymentCoin(stack, drop.entry, drop.body, inside =>
            {
                if (inside) settledCoins++;
                drop.settled(inside);
            });
        }

        foreach (var collider in bowl.GetComponentsInChildren<Collider>()) collider.enabled = false;
        foreach (var coinBody in bowl.GetComponentsInChildren<Rigidbody>())
        {
            Transform coin = coinBody.transform;
            Vector3 localPosition = coin.localPosition;
            Quaternion localRotation = coin.localRotation;
            coinBody.interpolation = RigidbodyInterpolation.None;
            coinBody.detectCollisions = false;
            Destroy(coinBody);
            coin.SetLocalPositionAndRotation(localPosition, localRotation);
        }
        // Remove the physics bodies before animating the filled bowl as one prop.
        yield return null;
        yield return PlayNpcPaymentPickup(animator, hand, constraint, landing);
        // Freeze the final grip before scaling the whole prop on its way into the story screen.
        constraint.constraintActive = false;

        Vector3 startCarry = npcPaymentRoot.position;
        yield return MovePaymentProp(npcPaymentRoot, startCarry, screenEntrance, .60f,
            Quaternion.identity, Quaternion.identity, endScale: .06f);
        bool resumeShortcut = Authored?.storyPackFrame == PackShortcutTravel;
        ClearNpcPaymentVisuals();
        if (resumeShortcut) BeginPackShortcutWalk();
    }

    private static IEnumerator PlayNpcPaymentPickup(Animator animator, Transform hand, ParentConstraint constraint, Vector3 restPosition)
    {
        Transform rig = animator.transform;
        float scale = rig.lossyScale.x;
        rig.position = restPosition;
        constraint.constraintActive = false;
        constraint.transform.SetLocalPositionAndRotation(constraint.translationAtRest, Quaternion.Euler(constraint.rotationAtRest));
        hand.gameObject.SetActive(true);
        animator.enabled = true;
        animator.Rebind();
        animator.speed = 0f;
        animator.Play(PickupState, 0, PickupGripTime);
        animator.Update(0f);
        float gripHeight = hand.localPosition.y;

        void Sample(float time)
        {
            animator.Play(PickupState, 0, time);
            animator.Update(0f);
            // The large tabletop prop retains the clip's world-space 8.5 cm lift.
            float lift = time >= PickupGripTime ? hand.localPosition.y - gripHeight : 0f;
            rig.position = restPosition + Vector3.up * (lift * (1f - scale));
        }

        Sample(0f);
        constraint.constraintActive = true;
        for (float elapsed = Time.unscaledDeltaTime; elapsed < 1f; elapsed += Time.unscaledDeltaTime)
        {
            yield return null;
            Sample(elapsed);
        }
        Sample(1f);
        yield return null;
    }

    private IEnumerator DropNpcPaymentCoin(Vector3 stack, Vector3 entry, Rigidbody body, Action<bool> settled)
    {
        Transform coin = body.transform;
        coin.SetParent(npcPaymentBowl, true);
        coin.rotation = Quaternion.identity;
        var shape = BoardItemDrop.ItemBounds(coin);
        float height = Mathf.Max(coin.position.y, entry.y + coin.position.y - shape.min.y + .15f);
        Vector3 start = new Vector3(stack.x, height, stack.z);
        var previousConstraints = body.constraints;
        body.constraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ |
            RigidbodyConstraints.FreezeRotation;
        yield return MovePaymentProp(coin, new Vector3(entry.x, height, entry.z), start, .12f,
            Quaternion.identity, Quaternion.identity);
        yield return BoardItemDrop.Play(body);
        shape = BoardItemDrop.ItemBounds(coin);
        var support = Physics.RaycastAll(shape.center, Vector3.down, shape.extents.y + .055f)
            .Where(hit => hit.collider.transform.IsChildOf(npcPaymentBowl) && !hit.collider.transform.IsChildOf(coin))
            .OrderBy(hit => hit.distance).FirstOrDefault();
        bool inside = support.collider != null;
        body.constraints = previousConstraints;
        if (inside)
        {
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.isKinematic = true;
            if (support.rigidbody is Rigidbody lowerCoin)
            {
                float overlap = BoardItemDrop.ItemBounds(lowerCoin.transform).max.y - shape.min.y;
                if (overlap > 0f)
                {
                    coin.position += Vector3.up * (overlap + .001f);
                    body.position = coin.position;
                }
            }
        }
        else BoardItemDrop.Hold(body);
        settled(inside);
    }

    private static IEnumerator TossNpcPaymentBowl(Transform bowl, Vector3 entry, Vector3 landing, float diameter)
    {
        const float flightSeconds = .57f;
        Vector3 fullScale = bowl.localScale;
        Quaternion restRotation = bowl.rotation;
        for (float elapsed = 0f; elapsed < flightSeconds; elapsed += Time.unscaledDeltaTime)
        {
            float t = elapsed / flightSeconds;
            Vector3 position = Vector3.Lerp(entry, landing, t);
            position.y += 2.4f * diameter * t * (1f - t);
            Quaternion rotation = restRotation * Quaternion.Euler(Mathf.Lerp(-34f, 0f, t), Mathf.Lerp(-45f, 0f, t), 0f);
            bowl.SetPositionAndRotation(position, rotation);
            bowl.localScale = fullScale * Mathf.Lerp(.15f, 1f, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }

        bowl.SetPositionAndRotation(landing, restRotation);
        bowl.localScale = fullScale;
        const float reboundSeconds = .19f;
        for (float elapsed = 0f; elapsed < reboundSeconds; elapsed += Time.unscaledDeltaTime)
        {
            float t = elapsed / reboundSeconds;
            float lift = 4f * t * (1f - t) * diameter * .08f;
            bowl.position = landing + Vector3.up * lift;
            yield return null;
        }
        bowl.position = landing;
    }

    private static IEnumerator MovePaymentProp(Transform model, Vector3 start, Vector3 end, float duration,
        Quaternion startRotation, Quaternion endRotation, float startScale = 1f, float endScale = 1f)
    {
        Vector3 fullScale = model.localScale;
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            model.SetPositionAndRotation(Vector3.Lerp(start, end, t), Quaternion.Slerp(startRotation, endRotation, t));
            if (startScale != endScale) model.localScale = fullScale * Mathf.Lerp(startScale, endScale, t);
            yield return null;
        }
        model.SetPositionAndRotation(end, endRotation);
        if (startScale != endScale) model.localScale = fullScale * endScale;
    }

    private void OnNpcPaymentViewChanged(bool walking)
    {
        if (walking) StopNpcRewardPresentation();
        if (!walking || npcPaymentRoot == null) return;
        if (journeyCoinSupply?.HasBoardPayment == true)
        {
            journeyCoinSupply.ClearBoardPayment();
            commitBoardPayment = cancelBoardPayment = null;
            npcBoardPaymentCost = null;
            SetYellowInputInteractable(true);
        }
        StopNpcPaymentPresentation();
    }

    private void StopNpcPaymentPresentation()
    {
        if (npcPaymentRoutine != null) StopCoroutine(npcPaymentRoutine);
        ClearNpcPaymentVisuals();
    }

    private void ClearNpcPaymentVisuals()
    {
        if (npcPaymentRoot != null) Destroy(npcPaymentRoot.gameObject);
        npcPaymentRoot = null;
        npcPaymentRoutine = null;
        npcPaymentBowl = null;
        npcPaymentBowlReady = npcPaymentConfirmed = false;
        npcPaymentDrops.Clear();
    }
}
