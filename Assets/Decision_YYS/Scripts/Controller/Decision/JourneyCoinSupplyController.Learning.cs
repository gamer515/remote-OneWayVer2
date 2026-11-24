using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class JourneyCoinSupplyController
{
    private sealed class LandedCoin
    {
        internal LearningBoardCoin Data;
        internal GameObject Model;
        internal Vector3 RestPosition;
        internal Vector3 OutlineOffset;
        internal Vector2 OutlineRadii;
        internal bool InFlight;
    }

    private readonly List<LandedCoin> learningCoins = new();
    private readonly List<LandedCoin> selectedLearningCoins = new();
    private MineLearningProgress learningProgress;
    private Action saveLearningBoard;
    private Vector2 selectionStart, learningGrabPoint;
    private bool selectingLearningCoins, draggingLearningCoins;
    private CoinSelectionGraphic learningSelectionGraphic;
    private static Mesh coinStarMesh;
    private static Material coinStarMaterial;
    private const string CoinStarName = "CoinFaceStar";

    internal GameObject CreateLearningCoinVisual(int ability, Vector3 position, Transform parent)
    {
        if (coinPrefabs[ability] == null) return null;
        var model = UnityEngine.Object.Instantiate(coinPrefabs[ability], position, Quaternion.identity, parent);
        SetLayerRecursively(model, board.gameObject.layer);
        if (model.TryGetComponent<Rigidbody>(out var body))
        {
            BoardItemDrop.Hold(body);
            body.detectCollisions = false;
        }
        foreach (var collider in model.GetComponentsInChildren<Collider>()) collider.enabled = false;
        StampCoin(model);
        return model;
    }

    internal void BindLearningBoard(MineLearningProgress progress, Action save)
    {
        if (learningProgress == progress) return;
        UnbindLearningBoard();
        learningProgress = progress;
        saveLearningBoard = save;
        progress.boardRewardCoins ??= new List<LearningBoardCoin>();
        Bounds bounds = paymentGrid.bounds;
        foreach (var coin in progress.boardRewardCoins.ToArray())
        {
            if (coin == null || (uint)coin.ability >= coinPrefabs.Length)
            {
                progress.boardRewardCoins.Remove(coin);
                continue;
            }
            Vector3 point = new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, coin.position.x),
                bounds.max.y, Mathf.Lerp(bounds.min.z, bounds.max.z, coin.position.y));
            if (!PlaceLearningCoin(coin, point)) progress.boardRewardCoins.Remove(coin);
        }
        RefreshDisplay();
    }

    private void UnbindLearningBoard()
    {
        CancelLearningSelection();
        foreach (var coin in learningCoins) DestroyLearningModel(coin);
        learningCoins.Clear();
        learningProgress = null;
        saveLearningBoard = null;
    }

    internal bool LandLearningCoin(int ability, Vector2 screen)
    {
        if (learningProgress == null || !CanStageCoins) return false;
        // The wallet was already credited. If the reward has since been spent, retire its animation.
        if (AvailableCoins(ability) <= 0) return true;
        Bounds bounds = paymentGrid.bounds;
        var point = bounds.center;
        if (boardInput.TryCreateRay(screen, out var ray) &&
            new Plane(Vector3.up, new Vector3(0f, bounds.max.y, 0f)).Raycast(ray, out float distance))
            point = ray.GetPoint(distance);
        var coin = new LearningBoardCoin { ability = ability };
        if (PlaceLearningCoin(coin, point))
        {
            learningProgress.boardRewardCoins.Add(coin);
            RefreshDisplay();
        }
        // If the board has no free spot, this coin remains available in its canister.
        return true;
    }

    private bool PlaceLearningCoin(LearningBoardCoin data, Vector3 point)
    {
        var model = CreateLearningCoinVisual(data.ability, point, coinContainer);
        if (model == null) return false;
        model.name = "LearningBoardCoin_" + CoinNames[data.ability];

        var obstacles = BoardCoins.Select(BoardItemDrop.ItemBounds);
        if (BooksOnBoard != null) obstacles = obstacles.Concat(BooksOnBoard().Select(BoardItemDrop.ItemBounds));
        if (PaymentBowl != null) obstacles = obstacles.Append(BoardItemDrop.ItemBounds(PaymentBowl));
        if (!BoardItemDrop.TryFindClearPosition(paymentGrid, model.transform, point, .005f, obstacles, out var rest))
        {
            UnityEngine.Object.Destroy(model);
            return false;
        }
        model.transform.position = rest;
        Bounds bounds = paymentGrid.bounds;
        data.position = new Vector2(Mathf.InverseLerp(bounds.min.x, bounds.max.x, rest.x),
            Mathf.InverseLerp(bounds.min.z, bounds.max.z, rest.z));
        var shape = BoardItemDrop.ItemBounds(model.transform);
        var top = new Vector3(shape.center.x, shape.max.y, shape.center.z);
        learningCoins.Add(new LandedCoin
        {
            Model = model, Data = data, RestPosition = rest,
            OutlineOffset = top - rest, OutlineRadii = new Vector2(shape.extents.x, shape.extents.z)
        });
        return true;
    }

    internal GameObject ReserveNpcRewardCoin(int ability, Vector3 position)
    {
        if (learningProgress == null || !CanStageCoins || AvailableCoins(ability) <= 0) return null;
        var data = new LearningBoardCoin { ability = ability };
        if (!PlaceLearningCoin(data, position)) return null;
        learningProgress.boardRewardCoins.Add(data);
        var coin = learningCoins[learningCoins.Count - 1];
        coin.InFlight = true;
        foreach (var renderer in coin.Model.GetComponentsInChildren<Renderer>()) renderer.forceRenderingOff = true;
        RefreshDisplay();
        return coin.Model;
    }

    internal void RevealNpcRewardCoin(GameObject model)
    {
        if (model == null) return;
        var coin = learningCoins.Find(item => item.Model == model);
        if (coin == null) return;
        coin.InFlight = false;
        foreach (var renderer in model.GetComponentsInChildren<Renderer>()) renderer.forceRenderingOff = false;
    }

    internal Vector3 RewardCanisterOpening(int ability)
    {
        Bounds bounds = supplyHoverBounds[ability];
        return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
    }

    private void ReconcileLearningCoins()
    {
        if (learningCoins.Count == 0) return;
        int[] counts = inventory.RemainingCoins;
        for (int i = 0; i < learningCoins.Count;)
        {
            var coin = learningCoins[i];
            if (counts[coin.Data.ability]-- > 0)
            {
                i++;
                continue;
            }
            CancelLearningSelection();
            RemoveLearningCoin(coin);
        }
    }

    private void RemoveLearningCoin(LandedCoin coin)
    {
        learningCoins.Remove(coin);
        learningProgress.boardRewardCoins.Remove(coin.Data);
        DestroyLearningModel(coin);
    }

    private static void DestroyLearningModel(LandedCoin coin)
    {
        coin.Model.SetActive(false);
        UnityEngine.Object.Destroy(coin.Model);
    }

    internal bool CancelLearningSelection()
    {
        bool hadSelection = selectingLearningCoins || draggingLearningCoins || selectedLearningCoins.Count > 0;
        foreach (var coin in selectedLearningCoins) coin.Model.transform.position = coin.RestPosition;
        selectedLearningCoins.Clear();
        selectingLearningCoins = draggingLearningCoins = false;
        ShowLearningSelection();
        return hadSelection;
    }

    private bool HandleLearningPointer(Vector2 point, bool down, bool held, bool up)
    {
        if (learningCoins.Count == 0 || !boardInput.InputEnabled) return CancelLearningSelection();
        bool hasRay = boardInput.TryCreateRay(point, out var ray);
        if (down && hasRay)
        {
            var hit = learningCoins.Where(coin => !coin.InFlight && BoardItemDrop.ItemBounds(coin.Model.transform).IntersectRay(ray, out _))
                .OrderBy(coin => (coin.RestPosition - ray.origin).sqrMagnitude).FirstOrDefault();
            if (hit != null)
            {
                if (!selectedLearningCoins.Contains(hit))
                {
                    CancelLearningSelection();
                    selectedLearningCoins.Add(hit);
                }
                learningGrabPoint = point;
                draggingLearningCoins = true;
                ShowLearningSelection();
                inventory.TryPlayAbilityCoinSound(hit.Model, hit.Data.ability);
            }
            else
            {
                CancelLearningSelection();
                if (!HasBoardPayment && paymentGrid.Raycast(ray, out var boardHit, 1000f) &&
                    !BoardPointerOccupied(ray, boardHit.distance))
                {
                    selectingLearningCoins = true;
                    selectionStart = point;
                }
            }
        }
        if (selectingLearningCoins)
        {
            if (held || up)
            {
                Rect area = SelectionRect(point);
                selectedLearningCoins.Clear();
                if (area.width >= 5f && area.height >= 5f)
                    selectedLearningCoins.AddRange(learningCoins.Where(coin => !coin.InFlight &&
                        area.Contains(CoinScreenPoint(coin.RestPosition))));
                ShowLearningSelection(held && !up ? area : null);
            }
            else ShowLearningSelection();
            if (up || !held) selectingLearningCoins = false;
            return true;
        }
        if (!draggingLearningCoins) return false;
        if (held)
        {
            foreach (var coin in selectedLearningCoins)
            {
                var target = CoinScreenPoint(coin.RestPosition) + point - learningGrabPoint;
                var dragPlane = new Plane(-inputCamera.transform.forward, coin.RestPosition);
                if (boardInput.TryCreateRay(target, out var dragRay) && dragPlane.Raycast(dragRay, out float distance))
                    coin.Model.transform.position = dragRay.GetPoint(distance);
            }
            ShowLearningSelection();
        }
        if (up)
        {
            int target = hasRay ? FindSupplyTarget(ray) : -1;
            if (target >= 0 && (selectedLearningCoins.Count > 1 || selectedLearningCoins[0].Data.ability == target))
            {
                foreach (var coin in selectedLearningCoins) RemoveLearningCoin(coin);
                selectedLearningCoins.Clear();
                RefreshDisplay();
                saveLearningBoard?.Invoke();
            }
            CancelLearningSelection();
        }
        else if (!held) CancelLearningSelection();
        return true;
    }

    private bool BoardPointerOccupied(Ray ray, float boardDistance)
    {
        bool Blocks(Transform item) => item != null &&
            BoardItemDrop.ItemBounds(item).IntersectRay(ray, out float hit) && hit < boardDistance;
        return Blocks(BoardShelf) || Blocks(PaymentBowl) ||
            paymentCoins.Any(coin => Blocks(coin.model.transform)) ||
            (BooksOnBoard?.Invoke().Any(Blocks) ?? false);
    }

    private Vector2 CoinScreenPoint(Vector3 position)
    {
        if (boardInput.boardViewport == null) return inputCamera.WorldToScreenPoint(position);
        var image = boardInput.boardViewport;
        var canvas = image.canvas;
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var viewport = inputCamera.WorldToViewportPoint(position);
        var rect = image.rectTransform.rect;
        var local = rect.min + Vector2.Scale(rect.size, viewport);
        return RectTransformUtility.WorldToScreenPoint(camera, image.rectTransform.TransformPoint(local));
    }

    private Rect SelectionRect(Vector2 end) => Rect.MinMaxRect(
        Mathf.Min(selectionStart.x, end.x), Mathf.Min(selectionStart.y, end.y),
        Mathf.Max(selectionStart.x, end.x), Mathf.Max(selectionStart.y, end.y));

    private void ShowLearningSelection(Rect? area = null)
    {
        if (learningSelectionGraphic == null)
        {
            if (!area.HasValue && selectedLearningCoins.Count == 0) return;
            var root = new GameObject("LearningCoinSelection", typeof(RectTransform), typeof(Canvas));
            root.transform.SetParent(coinContainer, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 143;
            var graphic = new GameObject("SelectionOutlines", typeof(RectTransform), typeof(CoinSelectionGraphic))
                .GetComponent<CoinSelectionGraphic>();
            graphic.transform.SetParent(root.transform, false);
            graphic.raycastTarget = false;
            var rect = graphic.rectTransform;
            rect.anchorMin = rect.pivot = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            learningSelectionGraphic = graphic;
        }
        learningSelectionGraphic.BeginFrame(area);
        foreach (var coin in selectedLearningCoins)
        {
            Vector3 top = coin.Model.transform.position + coin.OutlineOffset;
            learningSelectionGraphic.AddCoin(CoinScreenPoint(top),
                CoinScreenPoint(top + Vector3.right * coin.OutlineRadii.x),
                CoinScreenPoint(top + Vector3.forward * coin.OutlineRadii.y));
        }
        learningSelectionGraphic.EndFrame();
    }

    private static void StampCoin(GameObject coin)
    {
        if (coin.transform.Find(CoinStarName) != null) return;
        Bounds bounds = BoardItemDrop.ItemBounds(coin.transform);
        float radius = Mathf.Min(bounds.extents.x, bounds.extents.z) * .64f;
        if (radius <= 0f) return;
        if (coinStarMesh == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return;
            coinStarMaterial = new Material(shader) { name = "Coin Star Gold" };
            coinStarMaterial.SetColor("_BaseColor", new Color32(255, 207, 91, 255));
            var vertices = new Vector3[17];
            var triangles = new int[48];
            for (int i = 0; i < 16; i++)
            {
                float angle = i * Mathf.PI / 8f;
                float tip = i % 2 != 0 ? .25f : i % 4 == 0 ? 1f : .72f;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * tip;
                triangles[i * 3 + 1] = (i + 1) % 16 + 1;
                triangles[i * 3 + 2] = i + 1;
            }
            coinStarMesh = new Mesh { name = "Eight-point coin star", vertices = vertices, triangles = triangles };
            coinStarMesh.RecalculateBounds();
        }

        var star = new GameObject(CoinStarName, typeof(MeshFilter), typeof(MeshRenderer));
        star.layer = coin.layer;
        star.transform.position = new Vector3(bounds.center.x, bounds.max.y + .004f, bounds.center.z);
        star.transform.localScale = Vector3.one * radius;
        star.transform.SetParent(coin.transform, true);
        star.GetComponent<MeshFilter>().sharedMesh = coinStarMesh;
        var renderer = star.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = coinStarMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}
