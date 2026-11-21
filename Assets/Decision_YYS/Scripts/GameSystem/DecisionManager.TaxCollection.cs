using System;
using UnityEngine;

public partial class DecisionManager
{
    [SerializeField] private GameObject taxScaleCollectionRig;
    [SerializeField] private GameObject directTaxCollectionRig;

    private MineTaxInvoice stagedTaxInvoice;
    private Transform[] settledTaxCoins;
    private Vector3 directTaxPickup;
    private bool collectFromScale;
    private TaxCollectionView collectorCollectionView;
    private Action endTaxCollection;

    private void OpenTaxScalePreparation(MineTaxInvoice invoice)
    {
        if (ReferenceEquals(stagedTaxInvoice, invoice)) return;
        CloseTaxScalePreparation();
        stagedTaxInvoice = invoice;
        journeyCoinSupply?.SetTaxScale(taxScaleView);
    }

    private void CloseTaxScalePreparation()
    {
        stagedTaxInvoice = null;
        journeyCoinSupply?.SetTaxScale(null);
    }

    private void CollectBoardTaxPayment(Action commit)
    {
        settledTaxCoins = journeyCoinSupply.TakeCollectorCoins(out collectFromScale, out directTaxPickup);
        // Only the existing tax callback can authorize a visual collection.
        commit();
        if (settledTaxCoins == null) return;
        DiscardSettledTaxCoins();
        journeyCoinSupply.RefreshDisplay();
        SetYellowInputInteractable(true);
    }

    private void PresentSettledTaxPayment(Action finished)
    {
        // The wallet and the inspection/invoice outcome are durable before playback.
        SaveGuideCheckpoint();
        GameObject prefab = collectFromScale ? taxScaleCollectionRig : directTaxCollectionRig;
        if (settledTaxCoins == null || settledTaxCoins.Length == 0 || prefab == null ||
            journeyBoardInput?.inputCamera == null)
        {
            DiscardSettledTaxCoins();
            finished();
            return;
        }

        var rig = Instantiate(prefab);
        collectorCollectionView = rig.AddComponent<TaxCollectionView>();
        endTaxCollection = finished;
        SetYellowInputInteractable(false);
        presentationController.ShowInteraction("납부를 확인했다.", "징수원");
        collectorCollectionView.Play(settledTaxCoins, collectFromScale, directTaxPickup, taxScaleView,
            journeyBoardInput.inputCamera, uiController.StoryScreenCenter, StudyGrid.gameObject.layer,
            FinishTaxCollectionPresentation);
        settledTaxCoins = null;
        collectFromScale = false;
    }

    private void FinishTaxCollectionPresentation()
    {
        var finished = endTaxCollection;
        StopTaxCollection();
        finished?.Invoke();
    }

    private void StopTaxCollection()
    {
        DiscardSettledTaxCoins();
        if (collectorCollectionView != null)
        {
            collectorCollectionView.ReleaseVisuals();
            Destroy(collectorCollectionView.gameObject);
        }
        collectorCollectionView = null;
        endTaxCollection = null;
    }

    private void DiscardSettledTaxCoins()
    {
        if (settledTaxCoins == null) return;
        foreach (var coin in settledTaxCoins)
            if (coin != null) Destroy(coin.gameObject);
        settledTaxCoins = null;
        collectFromScale = false;
        taxScaleView?.SetPlayerCount(0);
    }

    private void OnTaxCollectionViewChanged(bool walking)
    {
        if (!walking) return;
        if (endTaxCollection != null)
        {
            FinishTaxCollectionPresentation();
            return;
        }
        bool hadCollectorPayment = collectorBoardPayment || stagedTaxInvoice != null;
        if (collectorBoardPayment)
        {
            commitBoardPayment = cancelBoardPayment = null;
            collectorBoardPayment = false;
            journeyCoinSupply.ClearBoardPayment();
            SetYellowInputInteractable(true);
        }
        CloseTaxScalePreparation();
        taxScaleView?.ClearNotice();
        if (hadCollectorPayment && collectingTax) FinishCollectorDialogue();
    }
}
