using HarmonyLib;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// Prefix-skips the game's per-frame FIGURE TRANSFORM writers for HELD actors only
/// (<see cref="HeldFigures.Owns"/>). Vanilla behavior everywhere else (returns true).
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
    private static bool Update_Prefix(ActorBehaviour __instance) => !HeldFigures.Owns(__instance);

    [HarmonyPrefix]
    [HarmonyPatch("LateUpdate")]
    private static bool LateUpdate_Prefix(ActorBehaviour __instance) => !HeldFigures.Owns(__instance);
}
