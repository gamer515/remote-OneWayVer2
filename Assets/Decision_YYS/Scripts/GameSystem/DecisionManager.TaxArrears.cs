using System;
using System.Collections.Generic;
using UnityEngine;

public partial class DecisionManager
{
    private void RecordMineCollectorEscape(MineCollectorProcession procession, Vector3 position)
    {
        bool advancingInShortcut = packShortcutWalking && position.z > shortcutLastZ;
        if (packShortcutWalking) shortcutLastZ = position.z;
        bool shortcutEscape = false;
        if (advancingInShortcut && procession.IsReturning && !mine.collectorChaseClosed && mine.HasTaxInvoice)
        {
            bool inLane = Mathf.Abs(position.x - shortcutLaneX) < .6f &&
                position.z >= shortcutLaneStartZ - .05f && position.z <= shortcutLaneEndZ + .05f;
            if (inLane)
            {
                string id = mine.taxInvoice.EnsureId();
                float reach = mineRules.encounterDistance;
                if (procession.IsBesidePursuers(position, reach)) mine.shortcutPursuitInvoiceId = id;
                shortcutEscape = procession.IsAtEscapeBoundary(position.z) ||
                    (mine.shortcutPursuitInvoiceId == id && procession.IsAheadOfPursuers(position, reach));
            }
        }
        if (!procession.RecordPlayerEscape(position.z, shortcutEscape)) return;
        if (shortcutEscape) CarryMissedMineTax();
        mine.shortcutPursuitInvoiceId = null;
        SaveGuideCheckpoint();
    }

    private void CarryMissedMineTax()
    {
        var invoice = mine.taxInvoice;
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
