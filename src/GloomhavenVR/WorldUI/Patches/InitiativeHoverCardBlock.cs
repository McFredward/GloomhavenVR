using GloomhavenVR.Core;
using HarmonyLib;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.WorldUI.Patches;

/// <summary>
/// MP test bug #10b — the ROOM-SIZED card when the laser hovers a PLAYER entry on the
/// initiative track.
///
/// ROOT CAUSE: hovering an initiative-track player avatar fires the game's own hover
/// chain — <c>InitiativeTrackActorAvatar.OnPointerEnter → SetHilighted →
/// InitiativeTrackPlayerBehaviour.OnAvatarHighlight</c> (InitiativeTrackPlayerBehaviour.cs:24)
/// — which calls <c>CardsHandManager.Preview(playerActor, cardsHighlightHolder)</c>. That
/// preview REPARENTS the player's real, full-size <c>AbilityCardUI</c> objects
/// (<c>CardsHandUI.PreviewActionCards</c>: <c>SetParent(holder)</c>, <c>ToggleFullCard(true)</c>,
/// alpha 1) out of the 2D card-hand canvas under the track entry's highlight holder. In VR the
/// card-hand canvas is NOT one of our converted surfaces (the mod renders hands as real VR
/// cards instead), so the hoisted full-card rect ends up without the screen-overlay scaling
/// that makes it card-sized on a monitor — 100s of UI pixels map straight to world metres and
/// the card fills the room. Our laser delivers genuine uGUI pointer-enter events to the
/// converted track (<c>InitiativeTrackSurface</c>), so every hover of one of the party's
/// entries popped it.
///
/// FIX: while VR canvas conversion is active, no-op <c>CardsHandManager.Preview</c> — the
/// entry point used ONLY by that initiative-track player hover (sole caller in GH.Runtime).
/// This kills the own/teammate-character hover card completely, in SP and MP alike. The
/// ENEMY hover info the user wants kept is untouched: monsters go through
/// <c>InitiativeTrackEnemyBehaviour.OnAvatarHighlight → monsterBaseUI.TogglePreview</c> —
/// a widget INSIDE the converted initiative track that shows the monster's round action at
/// track scale. <c>StopPreview</c> is left vanilla (it early-returns while nothing is
/// previewing), and with the mod off the prefix returns <c>true</c>, so desktop play is
/// byte-identical.
/// </summary>
[HarmonyPatch]
internal static class InitiativeHoverCardBlock
{
    /// <summary>One log line per hover burst (ENTER spam repeats at laser jitter rate).</summary>
    private static float _lastLogTime = float.NegativeInfinity;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(CardsHandManager), nameof(CardsHandManager.Preview),
        typeof(CPlayerActor), typeof(Transform))]
    private static bool BlockHoverCardPreview(CPlayerActor playerActor)
    {
        if (!WorldUIConfig.ConversionActive)
            return true; // vanilla flat behaviour when VR is off

        float now = Time.unscaledTime;
        if (now - _lastLogTime > 5f)
        {
            _lastLogTime = now;
            string who = playerActor != null ? playerActor.GetPrefabName() : "<null>";
            VRLog.Debug("WorldUI",
                $"Initiative hover: suppressed the player-card preview for '{who}' " +
                "(CardsHandManager.Preview would hoist an unscaled full-size AbilityCardUI " +
                "into the room; VR shows hands as real cards instead). Enemy action info untouched.");
        }
        return false; // skip PreviewActionCards — nothing reparented, nothing to undo
    }
}
