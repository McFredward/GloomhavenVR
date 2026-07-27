using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// The MOD-DRAWN reproductions of everything a peer's control board shows beyond their two round
/// cards — the "alles was am Controllboard angezeigt ist soll auch beim fremden Controllboard
/// sichtbar sein" requirement. All of it is rendered at the peer's board pose by
/// <see cref="RemoteControlBoard"/>; NOTHING here rides the wire.
///
/// WHY A REPRODUCTION AND NOT THE GAME'S OWN CANVAS: the game instantiates exactly ONE objectives
/// container (<c>UIManager.MissionObjectiveContainer</c>), ONE infusion board
/// (<c>InfusionBoardUI.Instance</c>) and ONE initiative track (<c>InitiativeTrack.Instance</c>) per
/// client, and the local board already docks those single instances onto ITS mounts
/// (WorldUI TrayMountedPanelSurface). A canvas cannot be in two places at once, and re-parenting or
/// duplicating a live game canvas would violate the module's reversibility rule. So a peer's board
/// draws its own picture from the SAME model data the local panels are fed from — a read-only
/// mirror, exactly like <see cref="RemoteControlBoard"/>'s round-card panels.
///
/// FOUR DATA CLASSES. Two of them are zero-wire and are what THIS file draws; the other two exist
/// on a remote board too, and are named here because the first question about any new remote-board
/// content is which of the four it is. Every remote-content type carries the answer as a greppable
/// <c>CLASSIFICATION:</c> tag on its own doc comment — <c>grep -rn "CLASSIFICATION:" Net/</c>.
///   • GLOBAL (zero wire) — objectives, element infusions, round number. Scenario-wide state that is
///     bit-identical on every client (<c>ScenarioManager.CurrentScenarioState</c>,
///     <c>ElementInfusionBoardManager</c>, <c>Choreographer</c>), so a peer's board just has to
///     RENDER it at their pose.
///   • PER-ACTOR MODEL (zero wire) — initiative, pile counts, rest state, active cards. Read locally
///     off the already-host-replicated <c>CPlayerActor.CharacterClass</c>. Everything that the
///     vanilla client itself hides during the secret selection phase goes through
///     <see cref="RevealGate"/>; the rest is information vanilla already gives away for free (see
///     the per-section notes).
///   • VR-ONLY (costs wire bytes) — facts that exist NOWHERE in the game model: the board's world
///     pose and scale, the chosen board style, hand/head poses, the reading fans, card-FX events.
///     Nothing in THIS file is VR-only; the pose everything here is drawn at comes from
///     <see cref="RemoteControlBoard"/>, which is.
///   • DELIBERATELY-NOT (costs 0 B by decision) — knowable, but not worth a field or not safe to
///     leak. <see cref="RemoteBoardFurniture"/>'s "NEUTRAL LOOKS" and "LOCAL-ONLY STATE" blocks are
///     this class, not a fifth thing: button enabled-states, the local player's own tuning offsets,
///     and card IDENTITY, which never crosses the wire in any form.
///
/// WHY THE HEADER NAMES ALL FOUR AND NOT JUST THIS FILE'S TWO: the decision rule for new remote
/// content is "GLOBAL or PER-ACTOR MODEL by default; VR-ONLY must be justified", and there is
/// exactly ONE free bit left in the whole protocol
/// (<see cref="NetProtocol.PileBrowseReservedBit"/>). A reader who learns only that remote content
/// is "zero-wire" has no framework for the one question they must answer first. See
/// <c>.planning/refactor/INVARIANTS-Net-Rig.md</c> "Net — content classification".
///
/// Style: unlit (<see cref="BoardVisual"/>) like every other remote-board visual, change-gated TMP
/// writes (a per-frame <c>TMP.text</c> assignment re-triggers auto-size layout — the badge-flicker
/// lesson), and content re-read on a 4 Hz cadence rather than per frame so a table of four peers
/// costs nothing measurable.
/// </summary>
/// <remarks>CLASSIFICATION: n/a — this type is the shared TMP/label plumbing for the widgets below,
/// not content of its own. Each widget carries its own CLASSIFICATION tag.</remarks>
internal static class RemoteBoardContent
{
    /// <summary>Content re-read cadence (seconds). The board POSE follows every frame; only the
    /// model reads + TMP rebuilds are throttled.</summary>
    internal const float DefaultRefreshSeconds = 0.25f;

    /// <summary>
    /// EFFECTIVE content re-read cadence. [Optimize] RemoteContentInterval can widen it: this walk
    /// scales with the number of PEERS (every remote board's model reads, furniture, card faces,
    /// fans and FX ride this one cadence), so on a four-player table it is the mod cost that grows
    /// while a single-player capture shows nothing at all. 0 in config = keep the 0.25 s default,
    /// which is what ships — the trade it buys is purely how fast a PEER's board contents catch up,
    /// never anything about the local player's own board, and it is worth nothing in single player.
    /// </summary>
    internal static float RefreshSeconds
    {
        get
        {
            float over = Core.PerfConfig.RemoteContentSeconds;
            return over > 0f ? over : DefaultRefreshSeconds;
        }
    }

    /// <summary>A fitted, unlit world-space label under <paramref name="parent"/>. Shared by every
    /// section below so the remote board's typography is consistent with the local one.</summary>
    internal static TextMeshPro Label(Transform parent, string name, Vector3 localPos, Vector2 box,
        float maxFont, Color color, TextAlignmentOptions align, FontStyles style = FontStyles.Normal,
        bool wrap = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, worldPositionStays: false);
        go.transform.localPosition = localPos;
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.alignment = align;
        tmp.color = color;
        tmp.fontStyle = style;
        TmpFit.Fit(tmp, box.x, box.y, maxFont, wrap);
        return tmp;
    }

    /// <summary>Change-gated TMP write (see the class note on auto-size churn).</summary>
    internal static void SetText(TextMeshPro? tmp, string text)
    {
        if (tmp != null && tmp.text != text)
            tmp.text = text;
    }
}
