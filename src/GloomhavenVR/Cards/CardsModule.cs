using GloomhavenVR.Cards.Patches;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// Phase 3b (feat/cards) — the Demeo card hand (ROADMAP R3):
/// 2D hand suppressed (visuals only — bookkeeping untouched, see the header of
/// Cards/Patches/HandSuppressionPatches.cs for the strategy — that is a FILE, not a
/// type; the patch classes in it are <see cref="Patches.CardsHandManager_ShowList_Patch"/>
/// and siblings), palm-up fan
/// of live-canvas 3D cards, grab/inspect, play tray with initiative slot order +
/// swap, short/long-rest tokens, in-turn top/bottom half poking. All game entry
/// points verified against the real DLLs in <see cref="CardsGameApi"/>; blocking
/// select calls serialized through <see cref="CardActionQueue"/>.
///
/// Active when VR runs, and in Dev mode (desktop sim + [Cards] DevFakeHand).
/// Shutdown restores every re-parented card canvas and the 2D hand window
/// (hot-reload clean).
/// </summary>
internal sealed class CardsModule : IVRModule
{
    public string Name => "Cards";

    private GameObject? _driverGo;
    private bool _active;

    public void Init()
    {
        CardsConfig.Bind();

        if (!VRSession.IsRunning && !Plugin.DevMode.Value)
        {
            VRLog.Debug(Name, "VR not running and dev mode off — card hand not installed.");
            return;
        }

        _active = true;

        VRSession.Harmony?.PatchAll(typeof(CardsHandManager_ShowList_Patch));
        VRSession.Harmony?.PatchAll(typeof(CardsHandManager_ShowAll_Patch));
        VRSession.Harmony?.PatchAll(typeof(CardsHandManager_ShowHands_Patch));
        VRSession.Harmony?.PatchAll(typeof(CardsHandUI_OnDestroy_Patch));
        VRSession.Harmony?.PatchAll(typeof(CardsHandUI_DestroyCardUI_Patch));
        // Damage-negation burn flow hardening (task #11) + hover-FX suppression (task #10).
        VRSession.Harmony?.PatchAll(typeof(CardsHandUI_OnLoseCardClick_Gate));
        VRSession.Harmony?.PatchAll(typeof(TakeDamagePanel_BurnHover_Skip));
        VRSession.Harmony?.PatchAll(typeof(DialogPopup_Show_HoverStrip));
        // ITEMS 9 + 10 (2026-09-05): the pick flow's own OPEN and END edges. The game latches
        // CardsHandUI.cardHandMode and maxCardsSelected and NOTHING in TakeDamagePanel's close
        // path clears either, so a burn's banner outlived its flow by ~87 s and re-raised itself
        // over the next damage prompt. See Cards/Patches/PickFlowPatches.cs for the log excerpt.
        VRSession.Harmony?.PatchAll(typeof(CardsHandUI_UpdateView_PickFlowOpen));
        VRSession.Harmony?.PatchAll(typeof(CardsHandUI_Hide_PickFlowEnd));
        // Laser half-hover: the docked action-selection cards take their half highlight
        // from the beam's geometry (HalfSelection.UpdateLaserHighlight); the per-graphic
        // pushers are silenced for laser events so a tooltip row winning the raycast can
        // no longer drop the highlight (see Patches/HalfHoverPatches.cs).
        VRSession.Harmony?.PatchAll(typeof(FullCardEventPusher_Enter_LaserGeometric));
        VRSession.Harmony?.PatchAll(typeof(FullCardEventPusher_Exit_LaserGeometric));

        // Multiplayer half-hover TAP (extras extension record 14): record every half
        // enter/exit the game processes — the single funnel both the geometric laser
        // resolve and the fingertip pusher chain reach — so the Net sender can tell peers
        // which half of which docked slot card is lit (see Patches/HalfHoverPatches.cs).
        VRSession.Harmony?.PatchAll(typeof(FullAbilityCard_Enter_HalfHoverSync));
        VRSession.Harmony?.PatchAll(typeof(FullAbilityCard_Exit_HalfHoverSync));

        // WHITE decision-phase card faces: an ADOPTED face is re-activated by CardFace every
        // time the game's pick-mode UpdateView deactivates it, and each cycle re-enters the
        // addressable card-art loader mid-load — which Unloads first (Image.sprite = null =
        // the white action halves). The guard skips only those restarts and replays/heals
        // once the loads are quiet (see Cards/CardArtGuard.cs).
        VRSession.Harmony?.PatchAll(typeof(FullAbilityCard_ShowCard_ArtGuard));

        // THE ENCHANTRESS EDGE (user 2026-08-23, item 4): a card enhanced at the Magierin must
        // update in the VR hand fan at once. The map-room fan's face is an Object.Instantiate
        // SNAPSHOT of a pool widget, so the game's own redraw cannot reach it and three latches
        // stop it ever being re-printed — see Cards/HandFanEnhancementRefresh.cs for the chain.
        // The commit raises no event, so the commit itself is the edge.
        VRSession.Harmony?.PatchAll(typeof(MapPartyEnhancementShopService_AddEnhancement_FanRefresh));

        HandSuppression.Active = true;

        _driverGo = new GameObject("GloomhavenVR.Cards");
        Object.DontDestroyOnLoad(_driverGo);
        _driverGo.hideFlags = HideFlags.HideAndDontSave;
        _driverGo.AddComponent<CardsDriver>();

        VRLog.Info(Name, "Card hand installed (fan + tray + half selection + pile viewer; " +
                         "2D hand visually suppressed).");
    }

    public void Shutdown()
    {
        if (!_active)
            return;
        _active = false;

        if (_driverGo != null)
        {
            // OnDestroy on the driver restores adopted faces + destroys VR objects.
            Object.Destroy(_driverGo);
            _driverGo = null;
        }

        HandSuppression.Active = false;
        HandSuppression.Restore();
        CardsSignals.Clear();
        CardActionQueue.Clear();
        CardArtGuard.Reset();
        CardArtPrewarm.Reset();
        CardArtPin.ReleaseAll("the cards module was torn down");
        CardHalfTone.Reset();
        PickFlowWatch.Reset();
        PokePads.Reset();
        // Harmony patches are removed collectively by Plugin.OnDestroy (UnpatchSelf).
    }
}
