using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Board;

/// <summary>
/// The LOCAL-side renderer of the character-focus cues: the initiative-track rings and the frame
/// around the local player's own control board. (A peer's board and Steam avatar are drawn by
/// <see cref="Net.RemoteFocusOutline"/>, from the same <see cref="FocusCue"/> palette.)
///
/// <para>WHAT IT DRAWS, and why each one exists:</para>
/// <list type="bullet">
/// <item>a STEADY blue-white ring on the initiative entry of the character the player is currently
///   looking at — the user's "the initiative track must make the current selection visible";</item>
/// <item>a ring on the entry of the character AT TURN — the user's "the initiative track must also
///   make it visually clear which character is currently at turn". It BLINKS green when the local
///   player owns that character and is looking at it, BLINKS red when they own it but are looking
///   elsewhere, and is a STEADY gold otherwise (a teammate's or a monster's turn: a fact, not a
///   demand);</item>
/// <item>the same blinking green/red as a frame around the local player's own control board — the
///   user's "the board gets a red blinking outline" and "the control board of the player who owns
///   the character at turn gets a blinking outline".</item>
/// </list>
///
/// <para>WHY THE TWO RINGS NEVER STACK: when the focused character IS the character at turn, only
/// the turn ring is drawn. Green already says "you are on the right one", and two concentric rings
/// on one small portrait read as noise rather than as two facts.</para>
///
/// <para>Entry resolution and ring seating are deliberately the SAME recipe the selection-phase
/// cue uses (<c>InitiativeSelectionGlow</c>): the game's public
/// <c>InitiativeTrack.FindInitiativeTrackActor(actor)</c> for the entry, its avatar's
/// <c>RawImage</c> for the visible portrait rect, a hollow non-raycasting frame as a nested child
/// at local z 0. The rings must never eat the portrait's click — that click is how the player
/// CHANGES the focus.</para>
///
/// <para>Runs in <c>LateUpdate</c>, after the game's own initiative writes, through
/// <see cref="TickGuard"/> so a throwing frame can never starve input (the standing rule from the
/// WorldUI input incident). Every ring is re-resolved each tick, so a pooled/reassigned entry can
/// never keep a stale colour, and everything clears the moment the phase gate shuts.</para>
/// </summary>
internal sealed class FocusDriver : MonoBehaviour
{
    /// <summary>Half-width of the local control board's slab in tray-root local metres — the ONE
    /// dimension <c>PlayTray</c> publishes. The slab is 0.64 × 0.32 m, i.e. exactly 2:1, so the
    /// half-height is half of this; see <see cref="BoardAspect"/>.</summary>
    private static float BoardHalfW => PlayTray.BoardHalfWidthLocal;

    /// <summary>The control board slab's height/width ratio (0.32 / 0.64). Stated here rather than
    /// mirrored from PlayTray's private constants, and only ever used to place a decoration —
    /// a board style with a different aspect would draw a slightly loose frame, never a wrong
    /// board.</summary>
    private const float BoardAspect = 0.5f;

    /// <summary>Outward margin of the board frame past the slab edge (metres), so the outline sits
    /// just OFF the board rather than on top of its art.</summary>
    private const float BoardFrameMargin = 0.022f;

    /// <summary>Bar thickness of the board frame (metres) — thick enough to read across the table,
    /// thin enough not to look like furniture.</summary>
    private const float BoardFrameThickness = 0.012f;

    /// <summary>Local z of the board frame: slightly toward the viewer (+Z points AWAY under the
    /// module convention), so the frame is never z-fought by the slab face.</summary>
    private const float BoardFrameZ = -0.004f;

    // Rings per initiative entry, kept (deactivated) for cheap reuse — same contract as the
    // selection-phase glow. Two families so an entry can hold both roles across frames without
    // rebuilding: the focus ring and the turn ring are separate GameObjects.
    private readonly Dictionary<InitiativeTrackActorBehaviour, UiRing> _focusRings = new(8);
    private readonly Dictionary<InitiativeTrackActorBehaviour, UiRing> _turnRings = new(8);
    private readonly List<InitiativeTrackActorBehaviour> _stale = new(8);

    private WorldFrame? _boardFrame;
    private Transform? _boardFrameHost;

    private System.Action? _tickCached;

    private void LateUpdate() =>
        TickGuard.Run("Board.CharacterFocus", _tickCached ??= Tick);

    private void OnDestroy()
    {
        ClearAllRings();
        _boardFrame?.Destroy();
        _boardFrame = null;
        _boardFrameHost = null;
    }

    private void Tick()
    {
        // Gate: no live scenario (or a teardown that left PhaseManager's static phase stale — the
        // exact trap SelectionReadyHighlighter documents) ⇒ everything off, nothing dereferenced.
        bool live = Net.RevealGate.InScenario && CardsGameApi.InScenario
                    && ScenarioManager.Scenario != null;
        if (!live)
        {
            HideAll();
            return;
        }

        CPlayerActor? focused = CharacterFocus.LookingAt;
        CPlayerActor? turn = CharacterFocus.TurnActor;
        FocusTurnMark localMark = CharacterFocus.LocalMark;

        TickRings(focused, turn, localMark);
        TickBoardFrame(localMark);
        TickReadOnlyRefresh();
    }

    /// <summary>
    /// Keep a live READ-ONLY focus view current. The watched character's hand, piles and played
    /// cards change as THEY act, and none of those changes raise an event on this client (the
    /// driver's rebuild edges are all about the LOCAL player's own hand). A low-rate poll is the
    /// honest answer: the alternative is a per-frame rebuild of a whole card board.
    /// </summary>
    private void TickReadOnlyRefresh()
    {
        if (!CharacterFocus.ReadOnlyView)
            return;
        if (Time.unscaledTime < _nextReadOnlyRefreshAt)
            return;
        _nextReadOnlyRefreshAt = Time.unscaledTime + ReadOnlyRefreshInterval;
        Cards.CardsDriver.RequestRebuild();
    }

    /// <summary>Poll period of the read-only focus refresh. 4 Hz: fast enough that a teammate
    /// playing a card is seen essentially immediately, slow enough that the rebuild cost is a
    /// rounding error next to the per-frame work the board already does.</summary>
    private const float ReadOnlyRefreshInterval = 0.25f;

    private float _nextReadOnlyRefreshAt;

    // ------------------------------------------------------------------------- initiative rings --

    private void TickRings(CPlayerActor? focused, CPlayerActor? turn, FocusTurnMark localMark)
    {
        InitiativeTrack track = InitiativeTrack.Instance;
        if (track == null)
        {
            HideRings();
            return;
        }

        InitiativeTrackActorBehaviour? turnEntry = EntryFor(track, turn);
        // Two rings never stack on one portrait: when the focus IS the actor at turn the turn ring
        // (which carries the urgent colour) wins and the focus ring is skipped.
        InitiativeTrackActorBehaviour? focusEntry =
            ReferenceEquals(focused, turn) ? null : EntryFor(track, focused);

        // The turn ring: blinking in the local mark's colour while the local player owns the turn,
        // steady gold otherwise.
        Color? turnTint = localMark != FocusTurnMark.None
            ? FocusCue.Tint(localMark)
            : (turn != null ? FocusCue.AtTurnRingTint() : (Color?)null);

        ApplyRing(_turnRings, turnEntry, turnTint, breathe: localMark != FocusTurnMark.None,
                  "GloomhavenVR.FocusTurnRing");
        ApplyRing(_focusRings, focusEntry,
                  focused != null ? FocusCue.SelectionRingTint() : (Color?)null,
                  breathe: false, "GloomhavenVR.FocusRing");
    }

    /// <summary>Show the ring for <paramref name="entry"/> in <paramref name="tint"/> and hide
    /// every OTHER ring of the same family (pooled entries are re-resolved every tick, so a
    /// reassigned portrait can never keep a stale colour).</summary>
    private void ApplyRing(Dictionary<InitiativeTrackActorBehaviour, UiRing> rings,
                           InitiativeTrackActorBehaviour? entry, Color? tint, bool breathe,
                           string name)
    {
        if (entry != null && tint != null)
        {
            UiRing? ring = EnsureRing(rings, entry, name);
            ring?.Apply(tint, breathe);
        }

        _stale.Clear();
        foreach (KeyValuePair<InitiativeTrackActorBehaviour, UiRing> kv in rings)
        {
            // Unity fake-null (a destroyed entry) is still a real reference — safe as a key.
            if (kv.Key == null || !kv.Value.Alive)
            {
                _stale.Add(kv.Key!);
                continue;
            }
            if (!ReferenceEquals(kv.Key, entry) || tint == null)
                kv.Value.Apply(null, false);
        }
        for (int i = 0; i < _stale.Count; i++)
        {
            if (rings.TryGetValue(_stale[i], out UiRing dead))
                dead.Destroy();
            rings.Remove(_stale[i]);
        }
    }

    private static UiRing? EnsureRing(Dictionary<InitiativeTrackActorBehaviour, UiRing> rings,
                                      InitiativeTrackActorBehaviour entry, string name)
    {
        if (rings.TryGetValue(entry, out UiRing existing))
        {
            if (existing.Alive)
                return existing;
            rings.Remove(entry); // destroyed / re-pooled with the old track — rebuild below
        }
        UiRing? built = UiRing.Build(PortraitRect(entry), name);
        if (built != null)
            rings[entry] = built;
        return built;
    }

    /// <summary>The entry of <paramref name="actor"/>, or null when it is not spawned / on screen.
    /// <c>FindInitiativeTrackActor</c> is the game's own public lookup and matches by reference,
    /// so the entry is exactly this actor's.</summary>
    private static InitiativeTrackActorBehaviour? EntryFor(InitiativeTrack track, CActor? actor)
    {
        if (actor == null)
            return null;
        InitiativeTrackActorBehaviour entry = track.FindInitiativeTrackActor(actor);
        return entry != null && entry.gameObject.activeInHierarchy ? entry : null;
    }

    /// <summary>The VISIBLE portrait rect of an entry — the avatar face, which is the avatar's only
    /// <c>RawImage</c> (every other graphic under it is a TMP or an Image). Null while the avatar
    /// has not been pooled in yet; the caller retries next tick.</summary>
    private static RectTransform? PortraitRect(InitiativeTrackActorBehaviour entry)
    {
        InitiativeTrackActorAvatar avatar = entry.Avatar;
        if (avatar == null)
            return null;
        RawImage[] raws = avatar.GetComponentsInChildren<RawImage>(includeInactive: false);
        for (int i = 0; i < raws.Length; i++)
        {
            if (raws[i] != null && raws[i].transform is RectTransform rt)
                return rt;
        }
        return null;
    }

    // ---------------------------------------------------------------------------- board frame --

    private void TickBoardFrame(FocusTurnMark localMark)
    {
        PlayTray? tray = PlayTray.Current;
        Transform? root = tray?.Root;
        if (root == null)
        {
            _boardFrame?.Apply(null);
            return;
        }
        if (_boardFrame == null || !ReferenceEquals(_boardFrameHost, root))
        {
            // The tray root is re-created on a board SWITCH; the frame is a child and dies with
            // it, so rebuild against the live root rather than resurrect a dangling handle.
            _boardFrame?.Destroy();
            float w = BoardHalfW * 2f + BoardFrameMargin * 2f;
            float h = BoardHalfW * 2f * BoardAspect + BoardFrameMargin * 2f;
            _boardFrame = WorldFrame.Build(root, "GloomhavenVR.FocusBoardFrame",
                                           new Vector2(w, h), BoardFrameThickness, BoardFrameZ);
            _boardFrameHost = root;
        }
        _boardFrame.Apply(FocusCue.Tint(localMark));
    }

    // ---------------------------------------------------------------------------------- teardown --

    private void HideAll()
    {
        HideRings();
        _boardFrame?.Apply(null);
    }

    private void HideRings()
    {
        foreach (UiRing ring in _focusRings.Values)
            ring.Apply(null, false);
        foreach (UiRing ring in _turnRings.Values)
            ring.Apply(null, false);
    }

    private void ClearAllRings()
    {
        foreach (UiRing ring in _focusRings.Values)
            ring.Destroy();
        foreach (UiRing ring in _turnRings.Values)
            ring.Destroy();
        _focusRings.Clear();
        _turnRings.Clear();
        _stale.Clear();
    }
}
