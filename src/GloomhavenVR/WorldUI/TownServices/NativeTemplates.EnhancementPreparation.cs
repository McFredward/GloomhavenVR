using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

internal static partial class NativeTemplates
{
    private static readonly Queue<int> EnhancementPreparation = new();
    private static readonly HashSet<int> EnhancementPrepared = new();
    private static float _enhancementPartyCheckAt;
    private static float _enhancementRetryAt;

    /// <summary>Prepare the same original public map faces for every selected party
    /// member before their first handoff. Unassigned observers have no personal fan,
    /// but still need the original assets immediately when another visitor offers.</summary>
    private static void PrepareEnhancementOriginals()
    {
        if (!WorldUI.MapRoom.MapRoomDriver.Active || _bank == null || ObjectPool.instance == null) return;
        float now = Time.unscaledTime;
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
    { EnhancementPreparation.Clear(); EnhancementPrepared.Clear(); _enhancementPartyCheckAt = _enhancementRetryAt = 0f; }
}
