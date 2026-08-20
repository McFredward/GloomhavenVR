using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;   // UIWindow — the game ships it in this namespace

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// WHY THIS ICON DID (OR DID NOT) GET A HOVER CARD — one attributable line per hovered location
/// (ModBuild 188).
///
/// <para>WHAT IT REPLACES. The 187 round could only report "bei manchen kam ein mouseover und bei
/// manchen nicht", and nothing in the log could separate the four completely different things that
/// produce that same silence: the beam never reached the icon, the game refused to highlight it,
/// the game highlighted it but has no preview for that kind of location, or the preview was
/// suppressed by state. A scan that only speaks on success is indistinguishable from one that never
/// ran — so this speaks on BOTH, and it names the gate.</para>
///
/// <para>EVERY GATE ON THE GAME'S OWN PATH, in the order the game evaluates them, is measured
/// rather than assumed (decompiled MapLocation.cs / MapChoreographer.cs / UIQuestPopupManager.cs):
/// <list type="number">
/// <item><c>OnPointerEnter</c> → <c>CanHighlight()</c> → <c>IsSelectable()</c> (:253-262, :310-332).
///   False and NOTHING happens — no highlight, no sound, no preview. A location with no
///   <c>LocationQuest</c> is selectable only as an available Headquarters or Store.</item>
/// <item><c>m_OnHighlightAction</c> → <c>MapChoreographer.OnMapLocationHighlight</c> (:1304-1427),
///   which returns FALSE while the map is initialising or moving, and for a Headquarters outside
///   the world-map guildmaster mode. Read back off <c>IsHighlighted</c>, which is only set when
///   both gates passed.</item>
/// <item><c>Highlight</c> → <c>UpdateMarkers</c> → <c>HasQuestPreview()</c> (:561-585, :340-354).
///   False and the location is highlighted but has no preview to show at all.</item>
/// <item>The SELECTED location takes the other branch on purpose: <c>UpdateMarkers</c> hides the
///   preview and focuses its quest marker instead (:568-574), and the manager shows the full quest
///   window rather than a card (<c>UIQuestPopupManager.PreviewQuest</c> :73-76).</item>
/// <item><c>PreviewQuest()</c> early-outs while any marker-hide request stands (:612-616).</item>
/// <item><c>UIQuestPopupManager.PreviewQuest</c> previews only while <c>selectedQuest == null</c>
///   (:77). Public as <c>IsQuestShown</c>.</item>
/// </list>
/// The private members are reached by reflection and each miss stands the TERM down with one Warn
/// naming what the verdict loses — never a throw, and never a silently wrong verdict.</para>
///
/// <para>VOLUME. Info once per (location kind × verdict) per map-room session — the shape the
/// hardware report is written in ("some symbols") is exactly a per-kind claim, so that is the
/// granularity that makes it decidable — plus Debug on every single hover for a full trace.</para>
/// </summary>
internal static class MapHoverVerdict
{
    private const string Scope = "MapRoom";

    /// <summary>Frames after the hover starts before the verdict is read. The game shows the
    /// preview synchronously inside <c>OnPointerEnter</c>, but its window fades in over a few
    /// frames — reading the popup on the same frame would call every success a failure.</summary>
    internal const int VerdictDelayFrames = 8;

    private static MethodInfo? _isSelectable;
    private static MethodInfo? _hasQuestPreview;
    private static FieldInfo? _hideMarkerRequests;
    private static bool _isSelectableMissing;
    private static bool _hasQuestPreviewMissing;
    private static bool _hideMarkerRequestsMissing;

    private static UIQuestPreviewPopup? _previewPopup;
    private static UIWindow? _previewWindow;

    /// <summary>(kind|verdict) keys already reported at Info this session.</summary>
    private static readonly HashSet<string> Reported = new();

    /// <summary>Forget the per-session Info throttle — the room came down, and the next one is
    /// entitled to its own first line per kind.</summary>
    internal static void Reset()
    {
        Reported.Clear();
        _previewPopup = null;
        _previewWindow = null;
    }

    /// <summary>
    /// Judge one hover and log it. <paramref name="pickedBy"/> is how the beam reached this
    /// location (the drawn-icon pad or the game's own hit box) — half of the coverage question is
    /// "did the pick even land", so the answer travels with the verdict rather than in a second
    /// line somebody has to correlate.
    /// </summary>
    internal static void Evaluate(MapLocation loc, string pickedBy)
    {
        if (loc == null)
            return;

        string kind = loc.MapLocationType.ToString();
        bool hasQuest = loc.LocationQuest != null;
        bool highlighted = loc.IsHighlighted;
        bool selected = loc.IsSelected;
        bool? selectable = Selectable(loc);
        bool? hasPreview = HasQuestPreview(loc);
        int hideRequests = HideMarkerRequests(loc);
        bool questShown = IsQuestShown();
        bool popupOpen = PreviewPopupOpen();

        string verdict;
        if (popupOpen)
        {
            verdict = "PREVIEW SHOWN — the card is up and TickHoverCards is flying it over this icon";
        }
        else if (selectable == false)
        {
            verdict = "no preview: the game refuses to HIGHLIGHT this location at all "
                      + "(MapLocation.CanHighlight → IsSelectable false). With no LocationQuest a "
                      + "location is selectable only as an AVAILABLE Headquarters or Store, so this is "
                      + "the game's own rule and the flat game shows nothing here either";
        }
        else if (!highlighted)
        {
            verdict = "no preview: the highlight was REFUSED downstream — MapLocation.OnPointerEnter ran "
                      + "but IsHighlighted is still false, i.e. MapChoreographer.OnMapLocationHighlight "
                      + "returned false (map not initialised, party moving, or a Headquarters outside the "
                      + "world-map guildmaster mode)";
        }
        else if (hasPreview == false)
        {
            verdict = "no preview: highlighted, but the game HAS no preview for this location "
                      + "(UpdateMarkers → HasQuestPreview false). Nothing to show — not a mod gap";
        }
        else if (selected)
        {
            verdict = "no card BY DESIGN: this is the SELECTED location, so the game hides the preview and "
                      + "focuses its quest marker instead (UpdateMarkers), and the manager shows the full "
                      + "quest WINDOW rather than a card. The window is the card's content";
        }
        else if (hideRequests > 0)
        {
            verdict = $"no preview: MapLocation.PreviewQuest early-out — {hideRequests} marker-hide "
                      + "request(s) standing on this location";
        }
        else if (questShown)
        {
            verdict = "no preview: a quest is already SELECTED, and UIQuestPopupManager.PreviewQuest only "
                      + "previews while selectedQuest is null. Hidden or closed that quest and the cards "
                      + "come back. This is the game's own rule, unchanged in VR";
        }
        else
        {
            verdict = "no preview AND NO GATE EXPLAINS IT — every condition this mod can read was open and "
                      + "the preview window is still closed. That is a genuine unknown: the next step is the "
                      + "MODAL FALLBACK lines for a UIQuestPreviewPopup window transition, because a preview "
                      + "that opened and was not floated looks exactly like this one";
        }

        string line = $"MAP ROOM hover '{loc.name}' [{kind}] via {pickedBy} — {verdict}. "
                      + $"(quest={(hasQuest ? "yes" : "none")}, selectable={Show(selectable)}, "
                      + $"highlighted={highlighted}, hasPreview={Show(hasPreview)}, selected={selected}, "
                      + $"markerHideRequests={hideRequests}, aQuestIsSelected={questShown}, "
                      + $"previewWindowOpen={popupOpen})";

        VRLog.Debug(Scope, line);
        string key = kind + "|" + (popupOpen ? "shown" : verdict.Substring(0, Mathf.Min(48, verdict.Length)));
        if (Reported.Add(key))
            VRLog.Info(Scope, line);
    }

    private static string Show(bool? v) => v.HasValue ? (v.Value ? "True" : "False") : "unknown";

    private static bool? Selectable(MapLocation loc)
    {
        if (_isSelectableMissing)
            return null;
        if (_isSelectable == null)
        {
            _isSelectable = typeof(MapLocation).GetMethod(
                "IsSelectable", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_isSelectable == null)
            {
                _isSelectableMissing = true;
                VRLog.Warn(Scope, "MapLocation.IsSelectable not found by name. CONSEQUENCE: the hover verdict "
                                  + "can no longer distinguish 'the game refuses to highlight this location' "
                                  + "from 'the highlight action refused it' — it will report the second for "
                                  + "both. Everything else is unaffected.");
                return null;
            }
        }
        try
        {
            return _isSelectable.Invoke(loc, null) as bool?;
        }
        catch (System.Exception ex)
        {
            VRLog.Warn(Scope, $"MapLocation.IsSelectable threw while judging a hover: {ex.Message}");
            _isSelectableMissing = true;
            return null;
        }
    }

    private static bool? HasQuestPreview(MapLocation loc)
    {
        if (_hasQuestPreviewMissing)
            return null;
        if (_hasQuestPreview == null)
        {
            _hasQuestPreview = typeof(MapLocation).GetMethod(
                "HasQuestPreview", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_hasQuestPreview == null)
            {
                _hasQuestPreviewMissing = true;
                VRLog.Warn(Scope, "MapLocation.HasQuestPreview not found by name. CONSEQUENCE: the hover "
                                  + "verdict cannot say whether the game has a preview for a location at all, "
                                  + "so 'nothing to show' will be reported as an unexplained miss. Nothing "
                                  + "else changes.");
                return null;
            }
        }
        try
        {
            return _hasQuestPreview.Invoke(loc, null) as bool?;
        }
        catch (System.Exception ex)
        {
            VRLog.Warn(Scope, $"MapLocation.HasQuestPreview threw while judging a hover: {ex.Message}");
            _hasQuestPreviewMissing = true;
            return null;
        }
    }

    private static int HideMarkerRequests(MapLocation loc)
    {
        if (_hideMarkerRequestsMissing)
            return 0;
        if (_hideMarkerRequests == null)
        {
            _hideMarkerRequests = typeof(MapLocation).GetField(
                "hideMarkerRequests", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_hideMarkerRequests == null)
            {
                _hideMarkerRequestsMissing = true;
                VRLog.Warn(Scope, "MapLocation.hideMarkerRequests not found by name. CONSEQUENCE: the hover "
                                  + "verdict reports 0 for it, so a PreviewQuest early-out caused by a standing "
                                  + "marker-hide request would fall through to the 'no gate explains it' "
                                  + "verdict. Nothing else changes.");
                return 0;
            }
        }
        return _hideMarkerRequests.GetValue(loc) is ICollection c ? c.Count : 0;
    }

    /// <summary>Is a quest already SELECTED? <c>IsQuestShown</c> is the public form of
    /// <c>selectedQuest != null</c> (UIQuestPopupManager.cs:28), the exact term
    /// <c>PreviewQuest</c> gates on — so no reflection is needed for this one.</summary>
    private static bool IsQuestShown() =>
        Singleton<UIQuestPopupManager>.IsInitialized
        && Singleton<UIQuestPopupManager>.Instance != null
        && Singleton<UIQuestPopupManager>.Instance.IsQuestShown;

    /// <summary>Is the preview popup actually up? Its <c>UIWindow</c> lives on its own GameObject
    /// (<c>UIQuestPreviewPopup.Awake</c> takes it with <c>GetComponent</c>), so this is an identity
    /// test on that one object and not a search for something related to it.</summary>
    private static bool PreviewPopupOpen()
    {
        if (_previewPopup == null)
        {
            _previewPopup = Object.FindObjectOfType<UIQuestPreviewPopup>();
            _previewWindow = _previewPopup != null ? _previewPopup.GetComponent<UIWindow>() : null;
        }
        if (_previewWindow == null)
            return false;
        // IsVisible (CanvasGroup alpha > 0) as well as IsOpen (the settled visual state): a window
        // still fading in is up as far as the player is concerned, and the verdict is read a few
        // frames into the hover precisely so this reads the settled answer.
        return _previewWindow.IsOpen || _previewWindow.IsVisible;
    }
}
