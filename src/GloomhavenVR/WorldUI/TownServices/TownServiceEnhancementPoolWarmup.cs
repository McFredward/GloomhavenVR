using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Builds the original enhancement list's empty slot wrappers during the map load.
/// Build 594's Frame log has MapRoom engaged at line 1098, the spinner retiring at
/// 1785, and the first enchantress visit at 2258. That leaves a hidden map-loading
/// interval in which the native HUD already exists. The first native open then costs
/// 211 ms, but even a second visit still hitches, so this is only a bounded reduction
/// of the slot-clone part, not a claim that the entire native open was precomputed.
///
/// The game's UIPartyCharacterEnhancementAbilityCardsDisplay.Display calls
/// HelperTools.NormalizePool before UIEnhanceCardSlot.Init. NormalizePool reuses
/// existing entries in slotsPool and activates them; Init creates/binds ability cards
/// and enhancement points. This class adds only inactive prefab clones to that same
/// list and content parent. Calling Display or EnterShop early would also change
/// selection, navigation, sound, animation and even saved stock state. Those native
/// calls remain exclusively on the visitor's original entry path. The pool is local
/// presentation; no game model or network state changes here.
/// </summary>
internal static class TownServiceEnhancementPoolWarmup
{
    private const int MaxSlots = 32;
    private static UIPartyCharacterEnhancementAbilityCardsDisplay? _display;
    private static int _target, _created;
    private static bool _targetReady, _finished, _failed;

    internal static void Tick()
    {
        try { TickCore(); }
        catch (Exception e) { Fail(e.GetType().Name + ": " + e.Message); }
    }

    private static void TickCore()
    {
        if (!MapRoomDriver.Active || !WorldUIConfig.ImmersiveTownServices.Value
            || !TownServiceEnhancementHandoff.Enabled) return;
        SceneController? scene = SceneController.Instance;
        if (!LoadingIndicator.FlatScreenSuppressed
            && (scene == null || !scene.IsLoading && !scene.ScenarioIsLoading)) return;
        UIWindow? window = GuildmasterDestinations.ModeWindow(EGuildmasterMode.Enchantress);
        if (window == null || window.IsOpen) return;
        UINewEnhancementWindow? shop = window.GetComponent<UINewEnhancementWindow>();
        UIPartyCharacterEnhancementAbilityCardsDisplay? display = shop?.CardsDisplay;
        if (display == null) return;
        if (!ReferenceEquals(_display, display))
        {
            _display = display;
            _target = _created = 0;
            _targetReady = _finished = _failed = false;
        }
        if (_finished || _failed) return;
        try
        {
            if (!_targetReady)
            {
                // The native Display calls GetOwnedAbilityCards on its selected service.
                // Count each assigned slot here so a later local character switch can
                // reuse the same wrappers. No card/model is instantiated or bound.
                NewPartyDisplayUI? party = NewPartyDisplayUI.PartyDisplay;
                List<NewPartyCharacterUI>? characters = party?.CharacterSlots;
                if (characters == null || characters.Count == 0) return;
                int maximum = 0;
                foreach (NewPartyCharacterUI character in characters)
                {
                    if (character == null || character.State != PartySlotState.Assigned
                        || character.Service is not ICharacterEnhancementService service) continue;
                    maximum = Math.Max(maximum, service.GetOwnedAbilityCards().Count);
                }
                if (maximum == 0) return;
                _target = Math.Min(maximum, MaxSlots);
                _targetReady = true;
            }
            List<UIEnhanceCardSlot>? pool = display.slotsPool;
            UIEnhanceCardSlot? prefab = display.slotPrefab;
            ScrollRect? panel = display.abilityCardsPanel;
            if (pool == null || prefab == null || panel == null || panel.content == null)
            {
                Fail("native slot pool, prefab or content is unavailable");
                return;
            }
            if (pool.Count >= _target)
            {
                Finish(pool.Count);
                return;
            }
            // HelperTools.NormalizePool activates and initializes the existing entries
            // on the real visit. Add at most one wrapper per load frame, inactive and in
            // the exact native content parent, so its next Display reuses it untouched.
            UIEnhanceCardSlot? slot = null;
            try
            {
                slot = UnityEngine.Object.Instantiate(prefab);
                slot.name = prefab.name;
                slot.transform.SetParent(panel.content, worldPositionStays: false);
                slot.gameObject.SetActive(false);
                pool.Add(slot);
            }
            catch
            {
                if (slot != null)
                {
                    slot.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(slot.gameObject);
                }
                throw;
            }
            _created++;
            if (pool.Count >= _target) Finish(pool.Count);
        }
        catch (Exception e)
        {
            // Leave any completed native wrappers in their own pool. A later Display
            // can use them and grow the rest by its original path.
            Fail(e.GetType().Name + ": " + e.Message);
        }
    }

    internal static void ReportAtOpen()
    {
        if (!VRLog.WantsDebug) return;
        try
        {
            int pool = _display != null ? _display.slotsPool?.Count ?? -1 : -1;
            VRLog.Debug("WorldUI", "TOWN ENHANCEMENT native pool at open: slots=" + pool
                + " target=" + _target + " prewarmed=" + _created + " complete=" + _finished + ".");
        }
        catch { /* Diagnostics must never interrupt the native destination press. */ }
    }

    private static void Finish(int count)
    {
        _finished = true;
        if (VRLog.WantsDebug)
            VRLog.Debug("WorldUI", "TOWN ENHANCEMENT native slot warmup: target=" + _target
                + " pool=" + count + " created=" + _created + " during map load.");
    }

    private static void Fail(string reason)
    {
        if (_failed) return;
        _failed = true;
        if (VRLog.WantsDebug)
            VRLog.Debug("WorldUI", "TOWN ENHANCEMENT native slot warmup skipped: " + reason + ".");
    }
}
