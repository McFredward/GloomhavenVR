using HarmonyLib;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// Prefix-skips the game's per-frame FIGURE TRANSFORM writers for HELD actors only —
/// either the LOCAL hand (<see cref="HeldFigures.Owns"/>) or a REMOTE player's hand
/// (<see cref="NetHeldFigures.Owns"/>, cosmetic pickup sync). Vanilla behavior everywhere
/// else (returns true). Suppressing the writes for a remotely-held figure is exactly what
/// lets <c>Net/NetFigures.Tick</c> drive it toward the synced pose instead of the game
/// re-deriving it to its board cell every frame.
///
/// Verified against the decompiled <c>ActorBehaviour</c> (GH.Runtime):
/// <code>
///   private void Update()      // → DoTransform(): writes m_AnimatedGameObject /
///                              //   m_RootGameObject .transform.position + rotation
///                              //   (ActorBehaviour.cs:477-478, :536, :543); also nudges
///                              //   the RunBlend BLEND PARAM (:547) and the hilight ring.
///   private void LateUpdate()  // → ApplyMotion(): re-asserts
///                              //   m_RootGameObject.transform.position = position and
///                              //   m_AnimatedGameObject.transform.localPosition = zero
///                              //   (ActorBehaviour.cs:610-611). Pure positioning.
/// </code>
/// Why a whole-method skip is safe for animation (the load-bearing requirement): the
/// skeletal animation is driven by the SEPARATE <c>m_Animator</c> component, which Unity
/// ticks on its own every frame while <c>m_Animator.enabled</c> (we never call
/// <c>PauseLoco</c>, which is the only thing that disables it). <c>DoTransform</c> only
/// WRITES the transform position and sets the <c>RunBlend</c> locomotion-blend float — it
/// does not play the animation. So skipping Update+LateUpdate freezes the position
/// (mini rides the hand) and freezes RunBlend at its current value (idle for a stationary
/// figure) while the Animator keeps playing the current clip. <c>FixedUpdate</c>
/// (invisibility dissolve) is deliberately NOT patched.
///
/// STATE is never touched — only the transform. On release the actor leaves
/// <see cref="HeldFigures"/>, Update runs again and snaps it back to its board cell.
/// </summary>
[HarmonyPatch(typeof(ActorBehaviour))]
internal static class ActorBehaviour_HeldTransform_Patch
{
    [HarmonyPrefix]
    [HarmonyPatch("Update")]
    private static bool Update_Prefix(ActorBehaviour __instance)
        => !HeldFigures.Owns(__instance) && !NetHeldFigures.Owns(__instance);

    [HarmonyPrefix]
    [HarmonyPatch("LateUpdate")]
    private static bool LateUpdate_Prefix(ActorBehaviour __instance)
        => !HeldFigures.Owns(__instance) && !NetHeldFigures.Owns(__instance);

    /// <summary>
    /// TASK #2 — while a figure is held (by anyone), the game's selection-ring toggles
    /// (<c>public static void SetHilighted(GameObject, bool)</c> — the ONLY place the game
    /// SetActives <c>m_Hilight</c>, verified in the decompiled ActorBehaviour) are routed to
    /// <see cref="FigureRingSuppressor.RecordGameIntent"/> instead of the ring itself: the ring
    /// stays off under the in-hand mini, and the recorded intent (select OR deselect mid-hold) is
    /// re-applied exactly on release. Vanilla behavior for every non-held actor.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(nameof(ActorBehaviour.SetHilighted))]
    private static bool SetHilighted_Prefix(UnityEngine.GameObject gameObject, bool hilight)
    {
        if (gameObject == null)
            return true;
        ActorBehaviour actor = ActorBehaviour.GetActorBehaviour(gameObject);
        if (actor == null || (!HeldFigures.Owns(actor) && !NetHeldFigures.Owns(actor)))
            return true;
        FigureRingSuppressor.RecordGameIntent(actor, hilight);
        return false; // held: the live ring stays suppressed; the ghost's ring shows the state
    }
}
