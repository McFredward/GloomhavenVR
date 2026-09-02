using GloomhavenVR.Core;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// The MOD-DRAWN reproductions of everything a peer's control board shows beyond their two round
/// cards — the "alles was am Controllboard angezeigt ist soll auch beim fremden Controllboard
/// sichtbar sein" requirement. All of it is rendered at the peer's board pose by
/// <see cref="RemoteControlBoard"/>; NOTHING here rides the wire.
///
/// WHY A REPRODUCTION AND NOT THE GAME'S OWN CANVAS — AND WHERE THAT ARGUMENT WAS WRONG.
/// The game instantiates exactly ONE objectives container (<c>UIManager.MissionObjectiveContainer</c>),
/// ONE infusion board (<c>InfusionBoardUI.Instance</c>) and ONE initiative track
/// (<c>InitiativeTrack.Instance</c>) per client, and the local board already docks those single
/// instances onto ITS mounts (WorldUI TrayMountedPanelSurface). This file used to conclude from that:
/// "a canvas cannot be in two places at once, and re-parenting or duplicating a live game canvas
/// would violate the module's reversibility rule" — so every remote panel had to be mod-drawn.
///
/// The first half stands; the conclusion did not, and the user's round-3 rejection of the green/red
/// initiative chips and the stand-in objectives box is what forced it out. RE-PARENTING a live game
/// canvas is indeed forbidden (a tray teardown must never cascade into destroying game-owned UI);
/// DUPLICATING it is not — <c>Object.Instantiate</c> reads the source and writes a new object tree,
/// leaving the original untouched, which is the guarantee <see cref="RemoteCardArt"/> has shipped
/// for round-card faces all along. <see cref="RemoteWidgetMirror"/> now does exactly that for the
/// two panels whose hand-drawn versions the user rejected: the INITIATIVE TRACK
/// (<see cref="RemoteInitiativeTrack"/>) and the OBJECTIVES panel
/// (<see cref="RemoteObjectivesPanel"/>) are live CLONES of the game's own widgets, driven per frame
/// from the original, with the mod-drawn versions demoted to fallbacks for the frames where the
/// widget does not exist. Everything else below is still a mod-drawn reproduction fed from the SAME
/// model data the local panels read.
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
///   • DELIBERATELY-NOT (costs 0 B by decision) — NOT SAFE TO LEAK, and nothing else.
///
///     THE "NOT WORTH A FIELD" HALF OF THIS CLASS IS VOID (user ruling 2026-08-08, verbatim: "Ich
///     möchte das die Schadensabfrage 1:1 beim remote-board so angezeigt wird wie der Spieler es
///     auch sieht. generell gilt die Regel, das man alle Interaktionen, Animationen und Anzeigen
///     des Controllboards in MP auch synchronisieren soll. Die einzige Ausnahme ist hier die
///     geheime Quest des characters und während der Auswahlphase die tatsächlichen Oberseiten der
///     Karten."). "Knowable but not worth a byte" was a wire-economy judgement this project is no
///     longer allowed to make on the user's behalf: if the owner's board SHOWS it, their remote
///     board shows it too, and the extension tail
///     (<see cref="NetProtocol.PileBrowseExtensionBit"/>) makes the cost of saying so a couple of
///     bytes. It was that half of the definition that produced the neutral-look placeholders the
///     user rejected one at a time — the always-drawn furniture, the fixed FOLLOW cap, the
///     re-localized CONFIRM wording, the empty decision drawer, and finally the decision row that
///     kept riding while its owner could not see it.
///
///     WHAT SURVIVES IS EXACTLY TWO THINGS, both named by the ruling itself: the character's
///     SECRET BATTLE GOAL, and — during the selection phase — the actual FRONTS of the cards.
///     Card IDENTITY in general remains what it always was: never on this wire in any form,
///     reveals only through <see cref="RevealGate"/>.
///
///     Local PERSONAL TUNING (a peer's private offsets, their own board-size preference) is not in
///     this class at all and never was — it is not a thing their board displays to them and then
///     hides from others; every client renders a given board style at its shipped layout, which is
///     a rendering convention, noted where it applies.
///
/// WHY THE HEADER NAMES ALL FOUR AND NOT JUST THIS FILE'S TWO: the decision rule for new remote
/// content is "GLOBAL or PER-ACTOR MODEL by default; VR-ONLY must be justified", and wire room is
/// no longer the scarce thing it was — the extension tail
/// (<see cref="NetProtocol.PileBrowseExtensionBit"/>) takes new fields without a bit each. A
/// reader who learns only that remote content
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
        // THE SAME RIM THE LOCAL BOARD'S FLOATING LABELS NOW CARRY (user request 5, 2026-09-03).
        // A peer's board hangs in the same scenario the local one does, so its readouts cross the
        // same lit rock and the same black litter; relieving one board and not the other would be
        // the 1:1 ruling broken in the direction that is hardest to notice, because the owner of
        // that board sees his own copy relieved and only the VIEWER sees the bare one. There is no
        // dial and no wire field here — it is one constant recipe applied identically on both
        // sides, which is what makes the two pictures the same.
        WorldUI.NativeButtonSkin.StyleWorldReadableLabel(tmp);
        TmpFit.Fit(tmp, box.x, box.y, maxFont, wrap);
        return tmp;
    }

    /// <summary>Change-gated TMP write (see the class note on auto-size churn).</summary>
    internal static void SetText(TextMeshPro? tmp, string text)
    {
        if (tmp != null && tmp.text != text)
            tmp.text = text;
    }

    /// <summary>
    /// A LIT material on the same shader ladder the LOCAL board's own furniture uses
    /// (<c>PlayTray.BoardLitShader()</c> → Standard → Diffuse → Sprites/Default, i.e. verbatim
    /// <c>PlayTray.Tint</c>'s non-overlay branch).
    ///
    /// WHY IT EXISTS NEXT TO <see cref="BoardVisual.Unlit"/>: unlit is the right default for the
    /// mod's OWN cosmetics (they must read the same regardless of the scenario's lighting), but it
    /// is the wrong default for a surface whose whole job is to look like a piece of the owner's
    /// board. An unlit copy of a lit, dark plate renders it at full brightness — flat and matte
    /// beside a shaded board — which is exactly how the round readout's backing became the "grey
    /// box" the owner does not perceive (defect (c) of the 1:1-parity round). A part that mirrors a
    /// LIT part of the local board must be lit too, or the copy is brighter than the original by
    /// construction.
    /// </summary>
    internal static Material BoardLit(Color color)
    {
        Shader? shader = Cards.PlayTray.BoardLitShader()
                         ?? Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse")
                         ?? Shader.Find("Sprites/Default");
        return shader != null ? new Material(shader) { color = color } : BoardVisual.Unlit(color);
    }
}
