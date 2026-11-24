using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class JourneyCoinSupplyController
{
    private readonly List<PaymentCoin> selectedPaymentCoins = new();
    private readonly Dictionary<PaymentCoin, Vector3> paymentGrabPositions = new();
    private Vector2 paymentSelectionStart, paymentGrabPoint;
    private bool selectingPaymentCoins;
    private bool DraggingPaymentBundle => paymentGrabPositions.Count > 0;

    internal bool CancelCoinSelection() => CancelPaymentSelection() | CancelLearningSelection();

    private bool CancelPaymentSelection()
    {
        bool hadSelection = selectingPaymentCoins || selectedPaymentCoins.Count > 0;
        foreach (var (coin, position) in paymentGrabPositions)
        {
            coin.model.transform.position = position;
            BoardItemDrop.Release(coin.body);
        }
        paymentGrabPositions.Clear();
        selectedPaymentCoins.Clear();
        selectingPaymentCoins = false;
        if (hadSelection) ShowLearningSelection();
        return hadSelection;
    }

    private bool IsWhiteBoardCoin(PaymentCoin coin) => !coin.landing && !coin.onTaxPan &&
        !coin.inPaymentBowl && coin.refillSlot < 0 && BoardItemDrop.IsOnSurface(WhiteBoard, coin.model.transform);

    private bool HandlePaymentSelectionPointer(Vector2 point, bool down, bool held, bool up)
    {
        if (WhiteBoard == null || !boardInput.InputEnabled) return CancelPaymentSelection();
        bool hasRay = boardInput.TryCreateRay(point, out var ray);
        if (down)
        {
            var hit = hasRay ? selectedPaymentCoins.FirstOrDefault(coin => IsWhiteBoardCoin(coin) &&
                BoardItemDrop.ItemBounds(coin.model.transform).IntersectRay(ray)) : null;
            if (hit != null)
            {
                selectedPaymentCoins.RemoveAll(coin => !IsWhiteBoardCoin(coin));
                foreach (var coin in selectedPaymentCoins)
                {
                    paymentGrabPositions.Add(coin, coin.model.transform.position);
                    BoardItemDrop.Hold(coin.body);
                }
                paymentGrabPoint = point;
                inventory?.TryPlayAbilityCoinSound(hit.model, hit.type);
            }
            else
            {
                CancelPaymentSelection();
                if (hasRay && paymentCoins.Any(IsWhiteBoardCoin) && WhiteBoard.Raycast(ray, out var hitBoard, 1000f) &&
                    !BoardPointerOccupied(ray, hitBoard.distance) && FindSupplyButton(ray) < 0 &&
                    (taxScale == null || !taxScale.HitsPlayerPan(ray)))
                {
                    CancelLearningSelection();
                    paymentSelectionStart = point;
                    selectingPaymentCoins = true;
                }
            }
        }
        if (selectingPaymentCoins)
        {
            var area = Rect.MinMaxRect(Mathf.Min(paymentSelectionStart.x, point.x), Mathf.Min(paymentSelectionStart.y, point.y),
                Mathf.Max(paymentSelectionStart.x, point.x), Mathf.Max(paymentSelectionStart.y, point.y));
            selectedPaymentCoins.Clear();
            if ((held || up) && area.width >= 5f && area.height >= 5f)
                selectedPaymentCoins.AddRange(paymentCoins.Where(coin => IsWhiteBoardCoin(coin) &&
                    area.Contains(CoinScreenPoint(BoardItemDrop.ItemBounds(coin.model.transform).center))));
            selectingPaymentCoins = held && !up;
            ShowLearningSelection(selectingPaymentCoins ? area : null);
            return true;
        }
        if (!DraggingPaymentBundle) return false;
        if (held)
        {
            foreach (var (coin, position) in paymentGrabPositions)
            {
                var target = CoinScreenPoint(position) + (point - paymentGrabPoint);
                var dragPlane = new Plane(-inputCamera.transform.forward, position);
                if (boardInput.TryCreateRay(target, out var dragRay) && dragPlane.Raycast(dragRay, out float distance))
                    coin.model.transform.position = dragRay.GetPoint(distance);
            }
            ShowLearningSelection();
        }
        if (up)
        {
            if (hasRay && DropPaymentBundle(ray)) paymentGrabPositions.Clear();
            CancelPaymentSelection();
        }
        else if (!held) CancelPaymentSelection();
        return true;
    }

    private bool DropPaymentBundle(Ray ray)
    {
        if (FindSupplyTarget(ray) >= 0)
        {
            foreach (var coin in selectedPaymentCoins)
            {
                RemovePaymentCoin(coin);
                paymentCoins.Remove(coin);
            }
            RefreshDisplay();
            return true;
        }
        if (taxScale != null && taxScale.HitsPlayerPan(ray))
        {
            if (paymentCoins.Count(coin => coin.onTaxPan) + selectedPaymentCoins.Count > taxScale.PlayerCapacity)
                return false;
            foreach (var coin in selectedPaymentCoins) coin.onTaxPan = true;
            RestackTaxPan();
            return true;
        }
        if (paymentBowlOpening?.Invoke(ray) != null) return false;
        Collider surface = WhiteBoard;
        if (!surface.Raycast(ray, out var hit, 1000f))
        {
            if (!paymentGrid.Raycast(ray, out hit, 1000f)) return false;
            surface = paymentGrid;
        }
        return PlacePaymentBundle(surface, hit.point);
    }

    private bool PlacePaymentBundle(Collider surface, Vector3 center)
    {
        var obstacles = paymentCoins.Where(coin => !paymentGrabPositions.ContainsKey(coin) &&
                !coin.onTaxPan && !coin.inPaymentBowl && coin.refillSlot < 0)
            .Select(coin => coin.model.transform)
            .Concat(learningCoins.Select(coin => coin.Model.transform))
            .Concat(BooksOnBoard?.Invoke() ?? Enumerable.Empty<Transform>())
            .Append(BoardShelf).Append(PaymentBowl)
            .Where(item => item != null && item.gameObject.activeInHierarchy)
            .Select(BoardItemDrop.ItemBounds).ToList();
        var anchor = paymentGrabPositions.Values.Aggregate(Vector3.zero, (sum, point) => sum + point) /
            selectedPaymentCoins.Count;
        var positions = new List<Vector3>(selectedPaymentCoins.Count);
        foreach (var coin in selectedPaymentCoins)
        {
            var model = coin.model.transform;
            var target = center + paymentGrabPositions[coin] - anchor;
            if (!BoardItemDrop.TryFindClearPosition(surface, model, target, .12f, obstacles, out var position))
                return false;
            var shape = BoardItemDrop.ItemBounds(model);
            shape.center += position - model.position;
            obstacles.Add(shape);
            positions.Add(position);
        }
        for (int i = 0; i < positions.Count; i++)
        {
            var coin = selectedPaymentCoins[i];
            coin.model.transform.position = positions[i];
            if (DropRunner != null) DropRunner.StartCoroutine(DropCoin(coin));
            else BoardItemDrop.Release(coin.body);
        }
        return true;
    }
}
