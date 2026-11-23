using System;
using System.Collections.Generic;
using UnityEngine;

public partial class DecisionManager
{
    private void ResetShortcutPursuits()
    {
        if (mine?.collectorPatrols == null) return;
        foreach (var state in mine.collectorPatrols) state.shortcutPursuitInvoiceId = null;
    }

    private void RecordMineCollectorEscape(MineCollectorProcession procession, Vector3 position, bool advancingInShortcut)
    {
        var state = procession.State;
        bool shortcutEscape = false;
        if (advancingInShortcut && procession.IsReturning && !state.collectorChaseClosed && state.HasTaxInvoice)
        {
            bool inLane = Mathf.Abs(position.x - shortcutLaneX) < .6f &&
                position.z >= shortcutLaneStartZ - .05f && position.z <= shortcutLaneEndZ + .05f;
            if (inLane)
            {
                string id = state.taxInvoice.EnsureId();
                float reach = mineRules.encounterDistance;
                if (procession.IsBesidePursuers(position, reach)) state.shortcutPursuitInvoiceId = id;
                shortcutEscape = state.shortcutPursuitInvoiceId == id && procession.IsAheadOfPursuers(position, reach);
            }
        }
        if (!procession.RecordPlayerEscape(shortcutEscape)) return;
        CarryMissedMineTax(state.taxInvoice);
        state.shortcutPursuitInvoiceId = null;
        SaveGuideCheckpoint();
    }

    private void CarryMissedMineTax(MineTaxInvoice invoice)
    {
        if (invoice == null || taxRules == null) return;
        string id = invoice.EnsureId();
        var arrears = mine.taxArrears ??= new List<MineTaxArrear>();
        if (arrears.Exists(entry => entry.invoice.id == id)) return;
        arrears.Add(new MineTaxArrear
        {
            invoice = invoice,
            penaltyCoins = (int)Math.Ceiling(invoice.Total * (decimal)taxRules.shortcutLateFeeRate)
        });
    }
}
