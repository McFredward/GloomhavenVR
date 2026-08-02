using System;
using System.Collections.Generic;
using GloomhavenVR.Board.FigureGrab;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// HOLDING A MINI PREVIEWS ITS TURN — the VR-native twin of the flat "hover the portrait on
/// the initiative track" gesture.
///
/// WHY THIS EXISTS (proven from the decompiled sources, 2026-08-02): the monster's round
/// action ("what will it do this turn") has exactly ONE display path in the whole game —
/// <c>InitiativeTrackActorAvatar.OnPointerEnter → SetHilighted(true) → onAvatarHighlightAction</c>
/// → <c>InitiativeTrackEnemyBehaviour.OnAvatarHighlight</c> → <c>monsterBaseUI.TogglePreview(true)</c>
/// (InitiativeTrackEnemyBehaviour.cs:98-118; a repo-wide grep finds no second
/// <c>TogglePreview</c> caller). Picking the mini up did NOT reach that path: the mod's figure
/// grab (<c>Board/FigureGrab</c>) is purely visual — hand-riding transform, shimmer overlay and
/// home-cell ghost — so in VR the most natural "let me look at this monster" gesture showed
/// nothing. This class closes that gap.
///
/// WHAT IT DOES: while a figure is held in a hand (<see cref="HeldFigures"/>, the LOCAL grab
/// registry), call <c>SetHilighted(true)</c> on that actor's initiative-track avatar — the exact
/// call the game's own pointer-enter makes, resolved through the game's public lookup
/// <c>InitiativeTrack.FindInitiativeTrackActor(CActor)</c> (InitiativeTrack.cs:532). On release
/// the flag is set back to false. Consequences are therefore identical to a mouse hover, including
/// the game's own availability rules — <c>OnAvatarHighlight</c> itself refuses during
/// <c>MonsterClassesSelectAbilityCards</c> and before the round card exists, so the peek is exactly
/// as available as the portrait hover and never invents information.
///
/// WHAT IT DELIBERATELY DOES NOT DO:
/// - It does NOT post <c>UIEvent.InitiativeAvatarHovered</c>. That event is what the tutorial's
///   HT_10 strip waits on; producing it from a grab would silently complete a scripted step the
///   player was told to do with the laser. Read-only DISPLAY only, zero game state, zero events,
///   zero wire traffic (MP-safe by construction: nothing here is networked, and a remote peer's
///   held figures — <c>NetHeldFigures</c> — are deliberately NOT peeked, since the initiative
///   track is local UI showing the LOCAL player what they are holding).
/// - It does not touch the player-entry path in any special way: a held PLAYER mini runs the
///   game's <c>InitiativeTrackPlayerBehaviour.OnAvatarHighlight</c>, whose card preview the mod
///   already no-ops (<see cref="Patches.InitiativeHoverCardBlock"/>) — so holding a hero highlights
///   its track entry and nothing else balloons into the room.
///
/// SAFETY: per-frame service on the WorldUI driver (TickGuard-wrapped), cold path outside a
/// scenario is one static read; every body try/caught behind a permanent error latch (the
/// TutorialVR/LevelMessageHeal pattern) so a track rebuild mid-grab can never flood the log or
/// destabilise the grab it decorates. No config of its own: the feature is self-gating — it can
/// only ever act on a figure the player is already holding, which requires
/// <c>[FigureGrab] GrabFigures</c>.
/// </summary>
internal static class FigureIntentPeek
{
    /// <summary>Actors whose track avatar WE put into the highlighted state (so exactly those are
    /// un-highlighted again on release — we never clear a highlight we did not set).</summary>
    private static readonly List<CActor> Peeked = new(4);

    /// <summary>Scratch: the actors held THIS frame. Reused so the per-frame path allocates nothing.</summary>
    private static readonly List<CActor> HeldNow = new(4);

    private static bool _disabledByError;
    private static int _peeks;

    /// <summary>
    /// Per-frame service (WorldUI driver). Cold path outside a live initiative track: one static
    /// read plus an empty held-set walk.
    /// </summary>
    internal static void Tick()
    {
        if (_disabledByError)
            return;
        try
        {
            TickCore();
        }
        catch (Exception ex)
        {
            // Latch pattern (TutorialVR / LevelMessageHeal): a decoration bug must never keep
            // re-throwing inside the hand's frame. Disarm permanently, log ONCE with the stack.
            _disabledByError = true;
            Peeked.Clear();
            VRLog.Error("Board", "figure-intent peek threw and is disabled for this session: "
                + $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>True while at least one held figure is driving a track preview — the tutorial
    /// step that teaches this gesture uses it as its "player performed the action" signal
    /// (<c>Compat.TutorialGrabStep</c>), so the hint and the effect can never disagree.</summary>
    internal static bool Active => Peeked.Count > 0;

    private static void TickCore()
    {
        InitiativeTrack? track = InitiativeTrack.Instance;
        if (track == null)
        {
            // No track (menu / scenario teardown): the avatars are gone with it, so there is
            // nothing to un-highlight — just forget what we were peeking at.
            Peeked.Clear();
            return;
        }

        HeldNow.Clear();
        foreach (ActorBehaviour behaviour in HeldFigures.All)
        {
            if (behaviour == null)
                continue;
            CActor? actor = behaviour.Actor;
            if (actor != null && !HeldNow.Contains(actor))
                HeldNow.Add(actor);
        }

        // Released figures first, so a hand that swaps one mini for another never leaves the
        // old entry stuck in the highlighted state.
        for (int i = Peeked.Count - 1; i >= 0; i--)
        {
            CActor actor = Peeked[i];
            if (HeldNow.Contains(actor))
                continue;
            Peeked.RemoveAt(i);
            SetTrackHighlight(track, actor, on: false);
        }

        for (int i = 0; i < HeldNow.Count; i++)
        {
            CActor actor = HeldNow[i];
            if (Peeked.Contains(actor))
                continue;
            if (!SetTrackHighlight(track, actor, on: true))
                continue; // no track entry (e.g. a summon not in the track) — nothing to preview
            Peeked.Add(actor);
            _peeks++;
            string line = $"Held figure '{actor.GetPrefabName()}' → its initiative-track avatar is "
                + "highlighted, which runs the game's OWN hover display path "
                + "(InitiativeTrackEnemyBehaviour.OnAvatarHighlight → MonsterBaseUI.TogglePreview) — "
                + $"the monster's round action is previewed while it is held (#{_peeks} this session).";
            if (_peeks == 1) VRLog.Info("Board", line);
            else VRLog.Debug("Board", line);
        }
    }

    /// <summary>
    /// Drive one track entry's highlight flag. <c>SetHilighted</c> is the game's own public
    /// method and the single funnel its pointer-enter/exit uses — we call nothing else, so the
    /// preview's own phase/round-card conditions stay the game's business.
    /// </summary>
    private static bool SetTrackHighlight(InitiativeTrack track, CActor actor, bool on)
    {
        InitiativeTrackActorBehaviour? entry = track.FindInitiativeTrackActor(actor);
        InitiativeTrackActorAvatar? avatar = entry != null ? entry.Avatar : null;
        if (avatar == null)
            return false;
        avatar.SetHilighted(on);
        return true;
    }
}
