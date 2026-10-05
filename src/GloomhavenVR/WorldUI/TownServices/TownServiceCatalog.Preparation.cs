using System.Collections.Generic;
using System.Diagnostics;
using GloomhavenVR.Net;
using GloomhavenVR.Cards;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

internal sealed partial class TownServiceCatalog
{
    private readonly HashSet<Entry> _preparedBankEntries = new();
    private readonly List<Entry> _preparedBankOrder = new();
    private readonly List<Entry> _publicationEntries = new();
    private readonly HashSet<Entry> _publicationSet = new();
    internal IReadOnlyList<Entry> PreparedOriginalEntries => _preparedBankOrder;
    internal uint OriginalBankRevision => _bankNativeRevision;
    internal IReadOnlyList<Entry> PublicationEntries
    {
        get { _publicationEntries.Clear(); _publicationSet.Clear(); CollectPages(_publicationEntries, _publicationSet, true); return _publicationEntries; }
    }
    private uint _bankNativeRevision = uint.MaxValue;
    private int _bankEntryCount = -1, _bankPreparationCursor;
    internal bool OriginalBankPrepared => _persistent && _bankNativeRevision == _backend.PresentationRevision
        && _bankEntryCount == _entries.Count && _preparedBankEntries.Count == _entries.Count;

    private void ObserveOriginalBankRevision()
    {
        if (_bankNativeRevision == _backend.PresentationRevision && _bankEntryCount == _entries.Count) return;
        _bankNativeRevision = _backend.PresentationRevision; _bankEntryCount = _entries.Count;
        _preparedBankEntries.Clear(); _preparedBankOrder.Clear(); _bankPreparationCursor = 0;
    }

    /// <summary>Prepare actual hidden original rows/cards, not replacement presentation models.</summary>
    internal bool PrepareOriginalBankForLoading()
    {
        if (!_persistent) return true;
        if (_disposed || !_alive()) return false;
        ObserveOriginalBankRevision();
        if (OriginalBankPrepared) return true;
        long began = Stopwatch.GetTimestamp();
        for (int visited = 0; visited < 8 && visited < _entries.Count; visited++)
        {
            if (_bankPreparationCursor >= _entries.Count) _bankPreparationCursor = 0;
            Entry entry = _entries[_bankPreparationCursor++];
            if (!_preparedBankEntries.Contains(entry) && entry.PrepareOriginalBankEntry())
                { _preparedBankEntries.Add(entry); _preparedBankOrder.Add(entry); }
            if ((Stopwatch.GetTimestamp() - began) / (double)Stopwatch.Frequency >= .002) break;
        }
        return OriginalBankPrepared;
    }

    internal sealed partial class Entry
    {
        internal bool PrepareOriginalBankEntry()
        {
            if (!Current || CardUI == null || !RemoteItemCardSource.PrepareMapItemForLoading(ItemId)) return false;
            if (_owner._opening.alpha < .999f || Time.unscaledTime - _presentedAt < .24f) return false;
            // Native Show/Start and ImageAddressableLoader own their output. Resident pins
            // alone do not mean the authored original widget has consumed both sprites yet.
            if (CardUI.cardBackground == null || CardUI.cardBackground.sprite == null || !CardUI.cardBackground.enabled)
                return false;
            if (Item.YMLData.ValidEquipCharacterClassIDs.Count > 0 && (CardUI.validOwnerIcon == null
                || CardUI.validOwnerIcon.sprite == null || !CardUI.validOwnerIcon.enabled)) return false;
            _artWatch.Poll("merchant cabinet bank item");
            CardFaceMipBake.Rescan(CardUI); _artWatch.Capture(CardUI);
            _row.Refresh(RowSource.transform); SuppressNativeBacking();
            RefreshSoldOutMarker(!Sample.IsMoving);
            // A hidden slot follows the same time-based original opening pose. Preparing it
            // must never move the actual visitor-held/offered/returning card.
            if (!Sample.IsMoving)
            {
                float t = Mathf.Clamp01((Time.unscaledTime - _presentedAt) / .24f);
                float ease = t * t * (3f - 2f * t);
                _display.localPosition = _displayHome + new Vector3(0f, 0f, .045f * (1f - ease));
                if (_body != null) TownServiceCardBody.SetVisibility(_body.gameObject, _owner._opening.alpha);
            }
            TownServiceNativeAssets.PrepareItem(CardUI);
            // Freeze every original observer partition before loading reports readiness,
            // including categories which have never been selected on this client.
            foreach (string key in new[] { "merchant.cardmount", "merchant.cardface", "item." + ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture), "merchant.cardbody", "merchant.row" })
                foreach (NativeTemplates.Part part in NativeTemplates.Parts(key))
                    NativeTemplates.Resolve(1, 1, key + "|" + part.Path);
            return RowContent != null;
        }
    }
}
