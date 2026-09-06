using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// "IS THIS RENDERER A FIGURE, AN ACTOR, OR A THING IN THE PLAYER'S HAND?" — the exemption a mod
/// sweep that may hide a foreign renderer is SUPPOSED to ask, and the only place its clause list
/// is written down.
///
/// <para><b>"HAS TO ASK" WAS THE WORDING HERE UNTIL 2026-09-06, AND IT WAS A HYPOTHESIS DRESSED
/// AS A CONTRACT.</b> Nothing makes a sweep ask this; the ModBuild 461 host log names one that
/// did not. <c>WallSegmentFade.CollectAdoptedSiblings</c> hand-rolls its own exclusion list
/// (ProceduralWall / ActorBehaviour / TileBehaviour / UnityGameEditorDoorProp, a floor band and a
/// water term), never calls anything in this type, and therefore faded a TREE in a peer's hand to
/// 1.00 — eighteen FADE WRITE lines, all eighteen attributed to the <c>asset sibling</c> lane,
/// while record 37 had that prop in player 2's right hand. Ten other sites in that subsystem DID
/// ask; being the tenth caller is not a guarantee about the eleventh lane. The held half of this
/// rule is therefore ALSO enforced at the wall system's four fade-WRITE primitives, where a lane
/// cannot route around it — see <c>Core/WallFade/WallSegmentFade.Held.cs</c>. This type stays the
/// clause list and the adoption-time exemption; it is not, and never was, a choke point.</para>
///
/// <para><b>WHY THIS TYPE EXISTS (redundancy survey R9, 2026-09-05).</b> The list was written
/// twice. <c>WallSegmentFade</c> had FIVE clauses; <c>MixedReality</c> had a hand copy with FOUR,
/// and the missing one was the held-prop term added in ModBuild 340 for the user's report
/// <i>"ich sehe zwar einen Geist aber in der Hand ist es garnicht oder nur immer ganz kurz für
/// einen Frame sichtbar"</i>. The MR copy's own doc said <i>"Deliberate MIRROR of the wall
/// system's guard … Keep the two in step"</i>, and ModBuild 340 stepped one and not the other —
/// which is what a mirror maintained by hand does, every time, eventually. The consequence was
/// real: carry a chest or a gold pile across an unexplored region and the MR unseen-underlay pass
/// gave it <c>forceDark</c>, i.e. a permanent dark backing plate that follows it home, because
/// teardown fires only when the source renderer dies.</para>
///
/// <para><b>THE CLAUSES ARE SHARED; THE CALL SHAPE IS NOT.</b> <c>WallSegmentFade</c> runs this
/// guard for every renderer in the scene — around 3000 per rescan — and memoises the ANCESTOR half
/// per transform inside a pass (its <c>FigureAncestryMemo</c>). That memo is exact only because
/// ancestry is a property of the CHAIN, and it is deliberately not shared here: what is shared is
/// the clause LIST, split so the memo can still front <see cref="HasFigureAncestor"/> with its own
/// cache while <see cref="CarriesFigureComponent"/> supplies the per-transform test the cache is
/// built from. A caller with no such pass (MixedReality) uses
/// <see cref="IsFigureOrActorRenderer"/> and gets the whole rule.</para>
///
/// <para><b>OVER-BROAD ON PURPOSE, and the direction matters.</b> Every clause FAILS OPEN: a true
/// verdict means "leave this renderer alone", so a false positive costs one un-hidden piece of
/// scenery and a false negative permanently hides a Brute's horned head (the round-7 ruling) or a
/// prop in the player's hand (ModBuild 340). Anything added here should be added in that
/// direction.</para>
/// </summary>
internal static class FigureRendererGuard
{
    /// <summary>
    /// Does this ONE transform carry a figure component? The unit the ancestor walk is built from,
    /// and the test a per-transform ancestry cache memoises.
    ///
    /// <para>The three components, and why each is in the list: <c>ActorBehaviour</c> is the game's
    /// board actor; <c>CInteractableActor</c> is the game's interactable figure root, the same
    /// component FigureGrab picks by; <c>Animator</c> catches an accessory that hangs off a BONE —
    /// whatever the actor component layout, the animation rig root is always above it — and spares
    /// animated props (chests) as a side effect, which no scenery sweep should be hiding anyway.</para>
    /// </summary>
    internal static bool CarriesFigureComponent(Transform t) =>
        t.GetComponent<ActorBehaviour>() != null
        || t.GetComponent<CInteractableActor>() != null
        || t.GetComponent<Animator>() != null;

    /// <summary>
    /// Does this renderer, or any ancestor of it, carry a figure component? The unmemoised form —
    /// three <c>GetComponentInParent</c> walks to the scene root, which is what a caller without a
    /// pass-scoped cache pays.
    ///
    /// <para><c>GetComponentInParent&lt;T&gt;()</c> without <c>includeInactive</c> considers only
    /// active GameObjects, which is the qualifier that makes an ancestry cache exact for a renderer
    /// that is <c>activeInHierarchy</c> and inexact for one that is not.</para>
    /// </summary>
    internal static bool HasFigureAncestor(Renderer r) =>
        r.GetComponentInParent<ActorBehaviour>() != null
        || r.GetComponentInParent<CInteractableActor>() != null
        || r.GetComponentInParent<Animator>() != null;

    /// <summary>
    /// IS THIS RENDERER PART OF A PROP THE PLAYER IS HOLDING RIGHT NOW? (ModBuild 340.)
    ///
    /// <para><b>WHY THE FOUR CLAUSES AROUND IT CANNOT COVER IT.</b> A prop passes NONE of them by
    /// construction: it has no <c>ActorBehaviour</c> at all — that is the very reason a prop is a
    /// <c>GrabbableProp</c> and not a <c>FigureGrabbable</c> — its body is a plain
    /// <c>MeshRenderer</c>, and the ancestor walks are <c>GetComponentInParent</c>, so whatever a
    /// board ancestor contributed is discarded the instant the prop is reparented under the hand's
    /// grab anchor. Lifting a prop makes it airborne, un-exempt and re-parented in ONE step:
    /// adopted on the very next sweep, hidden. That is "sichtbar für einen Frame", exactly.</para>
    ///
    /// <para><b>IT MUST BE ASKED BEFORE ANY ANCESTRY MEMO, and it must never be cached.</b> Every
    /// other clause is a property of the ancestor CHAIN, which is what makes such a memo exact.
    /// Held-ness is not: the same renderer under the same parents answers differently one frame
    /// later, and caching it would latch a prop hidden for the rest of the pass it was grabbed
    /// in.</para>
    ///
    /// <para><b>COST.</b> One <c>List.Count</c> compare while nothing is held — the steady state,
    /// and the state a wall rescan asks this in ~3000 times.</para>
    /// </summary>
    internal static bool HeldByPlayer(Renderer r) =>
        Board.FigureGrab.HeldProps.OwnsRendererOf(r.transform);

    /// <summary>
    /// The whole rule, unmemoised — for a caller that has no pass-scoped ancestry cache. Clause
    /// order is the reference order and is chosen for cost: the type test first, then the held
    /// check (a list-count compare in the steady state), then the three ancestor walks.
    /// </summary>
    internal static bool IsFigureOrActorRenderer(Renderer r) =>
        r is SkinnedMeshRenderer
        || HeldByPlayer(r)
        || HasFigureAncestor(r);
}
