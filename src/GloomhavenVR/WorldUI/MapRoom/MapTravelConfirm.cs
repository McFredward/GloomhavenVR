using System.Reflection;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.MapRoom;

// ---------------------------------------------------------------------------
// THE TRAVEL CONFIRMATION — THE GAME ALREADY HAS ONE, AND VR WAS ROUTING AROUND IT.
//
// User: "Ich vermisse einen Bestätigungsknopf, aktuell löst man die tatsächliche Auswahl viel zu
// schnell versehentlich aus. Wie ist das nochmal im flat Spiel?"
//
// READ FROM SOURCE, not from memory (decompiled AdventureMapUIManager.cs):
//
//     public void OnSelectedMapLocation(MapLocation mapLocation, Action<MapLocation> cb)   // :340
//     {
//         this.onConfirmTravelCallback = cb;
//         if (mapLocation == locationToTravel)
//         {
//             if (!FFSNetwork.IsOnline)
//                 ConfirmTravel();          // <-- SECOND CLICK ON THE SAME LOCATION JUST GOES
//             return;
//         }
//         ...
//         travelButton.TextLanguageKey = locationToTravel.IsCompleted() ? "GUI_REPLAY_LOCATION"
//                                                                       : "GUI_TRAVEL";        // :356
//     }
//
//     public void EnableTravelOptions(bool show)                                              // :408
//     {
//         if (show) travelButton.gameObject.SetActive(!FFSNetwork.IsOnline);
//         travelOptions.SetActive(flag);
//     }
//
// So the flat game DOES have a confirmation step — a `travelButton` labelled Reisen/Wiederholen
// with a Cancel beside it, inside a `travelOptions` container — and it ALSO has a single-player
// shortcut: clicking the same location twice commits immediately, with no prompt. Online that
// shortcut is deliberately switched off (`!FFSNetwork.IsOnline`), which is the game's own statement
// that it is a convenience and not part of the contract.
//
// IN VR THAT SHORTCUT IS A TRAP. A second trigger pull on the same icon is far easier to produce
// than a mouse double-click — the beam stays where your hand is — and the map room never drew the
// flat HUD that carries the travelButton, so the shortcut was the ONLY reachable commit path AND
// it committed without asking. Both halves of that are wrong, and this class fixes both.
//
// USER RULINGS THIS IMPLEMENTS (asked and answered, 2026-08-21):
//   * the confirm button belongs IN THE QUEST WINDOW — "dort, wo du sowieso hinschaust", no new
//     surface on the table to trigger by accident;
//   * the same-location shortcut is switched OFF ENTIRELY — exactly as the game already does
//     online — so travel commits through the button and through nothing else.
//
// TWO MECHANISMS, BOTH THROUGH SEAMS THE GAME ALREADY USES:
//
//   1. A PREFIX that reproduces the online branch. It does not "block a click": on the
//      same-location path it performs the original's own first statement (store the callback) and
//      returns, which is byte-for-byte what the original does when FFSNetwork.IsOnline. Every other
//      path runs vanilla. Nothing is invented — the behaviour already ships, for other players.
//
//   2. THE TRAVEL OPTIONS TRAVEL WITH THE WINDOW. `travelOptions` lives in the flat map HUD, which
//      the room does not render, so the button existed and was unreachable. It is parked inside the
//      floated quest window while that window is up and handed back afterwards — the same move
//      GuildmasterDestinations makes for the shared banner, and the same move the GAME makes for
//      its own banner when it enters the temple (UIGuildmasterHUD.cs:198-200). Anchored to the
//      window's bottom edge so it reads as the window's own footer rather than as loose furniture.
//
// RESTORE DISCIPLINE: the container's original parent, sibling index, anchors, pivot and
// anchoredPosition are recorded before the first move and written back on release, on stand-down
// and on teardown — and only while it is still OURS, because the game moves its own UI too and
// winning a write war with it is a bug this project has already paid for once.
//
// DEGRADES SAFELY: every private member is resolved through AccessTools once. If any is missing,
// ONE Warn names the consequence (the shortcut stays live / the button stays unreachable) and the
// class stands completely down. Nothing thrown, nothing on the wire, no rule library touched.
// ---------------------------------------------------------------------------

/// <summary>
/// Makes the game's own travel confirmation reachable in the 3D map room, and switches off the
/// single-player double-click shortcut that committed without asking. Installed by
/// <see cref="MapRoomDriver"/>.
/// </summary>
internal static class MapTravelConfirm
{
    private const string Scope = "MapRoom";

    private static bool _installed;
    private static bool _resolved;
    private static bool _standDown;

    private static FieldInfo? _locationToTravel;
    private static FieldInfo? _onConfirmCallback;
    private static FieldInfo? _travelOptions;
    private static FieldInfo? _travelButton;

    // ---- the parked container's home, recorded once ----------------------------------------
    private static Transform? _optionsHome;
    private static int _optionsHomeIndex;
    private static Vector2 _homeAnchorMin, _homeAnchorMax, _homePivot, _homeAnchoredPos;
    private static bool _homeRecorded;
    private static UIWindow? _host;
    private static bool _reported;

    /// <summary>Register the prefix exactly once. Called from the map room's engage path rather
    /// than from WorldUIModule so the two lanes that own that file cannot collide over it.</summary>
    internal static void Install()
    {
        if (_installed)
            return;
        _installed = true;
        VRSession.Harmony?.PatchAll(typeof(TravelShortcutGate));
        VRLog.Info(Scope, "MAP TRAVEL CONFIRM installed — the single-player 'click the same location "
                          + "twice and go' shortcut is switched off while the 3D map room stands (the "
                          + "game itself switches it off online, so this is its own behaviour and not an "
                          + "invention), and the game's real Reisen/Abbrechen buttons are parked inside "
                          + "the floated quest window so they can be reached at all. Travel now commits "
                          + "through that button and through nothing else.");
    }

    /// <summary>
    /// Level-triggered, one call per tick from <see cref="MapRoomDriver"/>. Parks the travel
    /// options inside <paramref name="questWindow"/> while that window is floated, and hands them
    /// back otherwise. Idempotent: a steady state costs two reference compares and no writes.
    /// </summary>
    internal static void Reconcile(UIWindow? questWindow)
    {
        if (_standDown)
            return;
        if (!EnsureReflection())
            return;

        AdventureMapUIManager? mgr = Manager();
        GameObject? options = mgr != null ? _travelOptions?.GetValue(mgr) as GameObject : null;

        if (!MapRoomDriver.Active || questWindow == null || options == null)
        {
            Unpark(!MapRoomDriver.Active ? "map room stood down"
                : questWindow == null ? "no quest window is floated"
                : "the game's travel options are gone");
            return;
        }

        if (ReferenceEquals(_host, questWindow))
            return; // already parked where it belongs

        Unpark("a different quest window took over");

        Transform t = options.transform;
        if (!_homeRecorded)
        {
            _homeRecorded = true;
            _optionsHome = t.parent;
            _optionsHomeIndex = t.GetSiblingIndex();
            if (t is RectTransform home)
            {
                _homeAnchorMin = home.anchorMin;
                _homeAnchorMax = home.anchorMax;
                _homePivot = home.pivot;
                _homeAnchoredPos = home.anchoredPosition;
            }
        }

        _host = questWindow;
        t.SetParent(questWindow.transform, worldPositionStays: false);
        t.SetAsLastSibling();
        // The window's own FOOTER: bottom-centre, pinned, so it reads as part of the card rather
        // than as loose furniture floating near it. Anchors are restored verbatim on unpark.
        if (t is RectTransform rect)
        {
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
        }

        if (!_reported)
        {
            _reported = true;
            var btn = mgr != null ? _travelButton?.GetValue(mgr) as Component : null;
            VRLog.Info(Scope, $"MAP TRAVEL CONFIRM: the game's travel options were moved from "
                              + $"'{(_optionsHome != null ? _optionsHome.name : "<none>")}' into the floated "
                              + $"quest window '{questWindow.name}' and anchored to its bottom edge. The "
                              + $"button itself is '{(btn != null ? btn.name : "<not found>")}' — the SAME "
                              + "ExtendedButton the flat game uses, so its label (Reisen / Wiederholen), "
                              + "its interactable state and every guard behind OnTravelButtonClick are the "
                              + "game's own. They go home the moment the window releases.");
        }
    }

    /// <summary>Put the container back — only while it is still parented under our host.</summary>
    private static void Unpark(string why)
    {
        if (_host == null)
            return;
        UIWindow? host = _host;
        _host = null;
        if (!EnsureReflection())
            return;
        AdventureMapUIManager? mgr = Manager();
        if (mgr == null || _travelOptions?.GetValue(mgr) is not GameObject options)
            return;
        Transform t = options.transform;
        // STILL OURS? The game re-parents its own UI freely, and taking it back from wherever it
        // has since put it would be a write war this project has already lost once.
        if (host == null || host.transform == null || t.parent == null
            || !t.parent.IsChildOf(host.transform))
            return;
        if (_optionsHome == null)
            return;
        t.SetParent(_optionsHome, worldPositionStays: false);
        t.SetSiblingIndex(Mathf.Clamp(_optionsHomeIndex, 0, Mathf.Max(0, _optionsHome.childCount - 1)));
        if (t is RectTransform rect)
        {
            rect.anchorMin = _homeAnchorMin;
            rect.anchorMax = _homeAnchorMax;
            rect.pivot = _homePivot;
            rect.anchoredPosition = _homeAnchoredPos;
        }
        VRLog.Info(Scope, $"MAP TRAVEL CONFIRM: travel options handed back to their own home ({why}) — "
                          + "parent, sibling index, anchors, pivot and position all restored verbatim.");
    }

    /// <summary>Teardown — hand the container back before the room disappears under it.</summary>
    internal static void Reset() => Unpark("map room teardown");

    /// <summary>Is this the location the game already has staged for travel? Used by the gate.</summary>
    private static bool IsStagedLocation(AdventureMapUIManager mgr, MapLocation? candidate)
    {
        if (candidate == null || _locationToTravel == null)
            return false;
        return ReferenceEquals(_locationToTravel.GetValue(mgr) as MapLocation, candidate);
    }

    private static AdventureMapUIManager? Object_FindManager() =>
        Object.FindObjectOfType<AdventureMapUIManager>(true);

    private static AdventureMapUIManager? Manager() =>
        Singleton<AdventureMapUIManager>.IsInitialized
            ? Singleton<AdventureMapUIManager>.Instance
            : Object_FindManager();

    private static bool EnsureReflection()
    {
        if (_standDown)
            return false;
        if (_resolved)
            return true;
        _resolved = true;
        _locationToTravel = AccessTools.Field(typeof(AdventureMapUIManager), "locationToTravel");
        _onConfirmCallback = AccessTools.Field(typeof(AdventureMapUIManager), "onConfirmTravelCallback");
        _travelOptions = AccessTools.Field(typeof(AdventureMapUIManager), "travelOptions");
        _travelButton = AccessTools.Field(typeof(AdventureMapUIManager), "travelButton");
        if (_locationToTravel == null || _onConfirmCallback == null || _travelOptions == null)
        {
            _standDown = true;
            VRLog.Warn(Scope, "MAP TRAVEL CONFIRM: AdventureMapUIManager private members not found by name "
                              + $"(locationToTravel={_locationToTravel != null}, "
                              + $"onConfirmTravelCallback={_onConfirmCallback != null}, "
                              + $"travelOptions={_travelOptions != null}) — the whole feature stands down. "
                              + "CONSEQUENCE: the single-player double-press shortcut stays live (a second "
                              + "press on the same location travels immediately) and the Reisen button "
                              + "stays unreachable in the room. Nothing else changes.");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Reproduces the game's ONLINE branch of <c>OnSelectedMapLocation</c> while the map room
    /// stands: store the callback and return, without the single-player <c>ConfirmTravel()</c>.
    /// Every other path runs vanilla.
    /// </summary>
    [HarmonyPatch(typeof(AdventureMapUIManager), "OnSelectedMapLocation")]
    internal static class TravelShortcutGate
    {
        private static bool _logged;

        private static bool Prefix(AdventureMapUIManager __instance, MapLocation mapLocation,
                                   System.Action<MapLocation> onConfirmTravelCallback)
        {
            if (_standDown || !MapRoomDriver.Active || __instance == null)
                return true;
            if (!EnsureReflection())
                return true;
            if (!IsStagedLocation(__instance, mapLocation))
                return true; // a DIFFERENT location — the original stages it, exactly as always

            // Same location, second press. The original's own first statement, then out — which is
            // precisely what it does when FFSNetwork.IsOnline. The ConfirmTravel() call is the only
            // thing skipped.
            _onConfirmCallback?.SetValue(__instance, onConfirmTravelCallback);
            if (!_logged)
            {
                _logged = true;
                VRLog.Info(Scope, "MAP TRAVEL CONFIRM: a second press on the ALREADY-SELECTED location "
                                  + "did NOT start the journey. The flat single-player build treats that "
                                  + "as 'go now'; in VR a second trigger pull on the same icon is far too "
                                  + "easy to produce, and the user reported committing by accident. This "
                                  + "is byte-for-byte the branch the game itself takes online. Travel "
                                  + "commits through the Reisen button in the quest window.");
            }
            return false;
        }
    }
}
