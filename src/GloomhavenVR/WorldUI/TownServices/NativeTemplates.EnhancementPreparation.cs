using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Net.TownServices;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

internal static partial class NativeTemplates
{
    private static readonly Queue<int> EnhancementPreparation = new();
    private static readonly HashSet<int> EnhancementPrepared = new();
    private static float _enhancementPartyCheckAt;
    private static float _enhancementRetryAt;
    private static uint _enhancementBasisGeneration = uint.MaxValue;
    private static uint _enhancementBasisRevision = uint.MaxValue;
    private static readonly Queue<string> EnhancementBasisPreparation = new();
    private static readonly string[] EnhancementWidgetKeys =
    {
        "enchant.inventory", "enchant.row", "enchant.tooltip", "enchant.holder",
        "enchant.capacity", "enchant.information", "enchant.point", "enchant.highlight",
        "enhance.confirm.part.0", "enhance.confirm.part.1", "enhance.confirm.part.2",
        "enhance.confirm.part.3", "enhance.confirm.part.4", "enhance.confirm.part.5",
        "item.confirm.part.0", "item.confirm.part.1", "item.confirm.part.2", "item.confirm.part.3"
    };

    private static void WarmEnhancementBasis(string key)
    {
        if (!Entries.TryGetValue(key, out Entry? entry)) return;
        byte service = key.StartsWith("item.confirm.", StringComparison.Ordinal) ? (byte)1 : (byte)3;
        foreach (Part part in entry.Parts)
        {
            string address = key + "|" + part.Path;
            TownServiceMirror.RegisterTemplate(service, 1, part.Original, part.Excluded.Contains, address,
                part.NativeRingRoot, part.NativeRingRate);
            TownServiceMirror.PrepareNativeTemplateBasis(service, address);
        }
    }

    /// <summary>Prepare the same original public map faces for every selected party
    /// member before their first handoff. Unassigned observers have no personal fan,
    /// but still need the original assets immediately when another visitor offers.</summary>
    private static void PrepareEnhancementOriginals()
    {
        if (!WorldUI.MapRoom.MapRoomDriver.Active || _bank == null || ObjectPool.instance == null) return;
        float now = Time.unscaledTime;
        if (_enhancementBasisGeneration != TownServiceMirror.Assets.Generation
            || _enhancementBasisRevision != TownServiceMirror.NativeTemplatePreparationRevision)
        {
            _enhancementBasisGeneration = TownServiceMirror.Assets.Generation;
            _enhancementBasisRevision = TownServiceMirror.NativeTemplatePreparationRevision;
            EnhancementBasisPreparation.Clear();
            foreach (string key in EnhancementWidgetKeys) EnhancementBasisPreparation.Enqueue(key);
            // Network teardown invalidates mirror bases without destroying this
            // bank. Queue existing faces again so observers also warm after reconnect.
            foreach (int id in EnhancementPrepared)
            { EnhancementBasisPreparation.Enqueue("face." + id); EnhancementBasisPreparation.Enqueue("card." + id); }
        }
        if (now >= _enhancementPartyCheckAt)
        {
            _enhancementPartyCheckAt = now + 1f;
            var party = MapRuleLibrary.Adventure.AdventureState.MapState?.MapParty;
            var characters = party?.SelectedCharactersArray;
            if (characters != null)
                foreach (var character in characters)
                {
                    if (character == null || character.HandAbilityCardIDs == null) continue;
                    foreach (int id in character.HandAbilityCardIDs)
                        if (id > 0 && EnhancementPrepared.Add(id)) EnhancementPreparation.Enqueue(id);
                }
        }
        if (now < _enhancementRetryAt) return;
        long started = System.Diagnostics.Stopwatch.GetTimestamp(); int count = 0;
        while (EnhancementBasisPreparation.Count != 0 && count++ < 2)
        {
            string key = EnhancementBasisPreparation.Dequeue();
            try { WarmEnhancementBasis(key); }
            catch (Exception) { EnhancementBasisPreparation.Enqueue(key); _enhancementRetryAt = now + .5f; break; }
            if ((System.Diagnostics.Stopwatch.GetTimestamp() - started)
                / (double)System.Diagnostics.Stopwatch.Frequency >= .002) return;
        }
        while (EnhancementPreparation.Count != 0 && count++ < 2)
        {
            int id = EnhancementPreparation.Dequeue();
            try
            {
                CAbilityCard? model = CharacterClassManager.AllAbilityCards.Find(card => card.ID == id);
                if (model == null) { EnhancementPrepared.Remove(id); continue; }
                CardArtPin.PinForCard(model); TownServiceNativeAssets.PrepareCard(model);
                EnsureCard("face." + id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                EnsureCard("card." + id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                WarmEnhancementBasis("face." + id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                WarmEnhancementBasis("card." + id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception)
            {
                // New party data may precede its native pool. Retry at a bounded
                // preparation cadence; a normal scene boundary clears this census.
                EnhancementPrepared.Remove(id); _enhancementRetryAt = now + .5f; break;
            }
            if ((System.Diagnostics.Stopwatch.GetTimestamp() - started)
                / (double)System.Diagnostics.Stopwatch.Frequency >= .002) break;
        }
    }

    private static void ResetEnhancementPreparation()
    {
        EnhancementPreparation.Clear(); EnhancementPrepared.Clear(); EnhancementBasisPreparation.Clear();
        _enhancementPartyCheckAt = _enhancementRetryAt = 0f; _enhancementBasisGeneration = uint.MaxValue;
        _enhancementBasisRevision = uint.MaxValue;
    }
}
