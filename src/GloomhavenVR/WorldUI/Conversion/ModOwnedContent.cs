using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// <b>"IS THIS OBJECT THE MOD'S OR THE GAME'S?" — ASKED SO IT CANNOT BE FORGOTTEN.</b>
///
/// <para>Until ModBuild 420 that question had exactly one answer in the whole conversion layer:
/// does the GameObject's NAME start with <c>GloomhavenVR.</c> (see
/// <c>CanvasConversion.ModOwnedPrefix</c> and <c>CanvasConversion.FindGameContent</c>). Everything
/// the mod parks under a float host has to carry that prefix, or the host-destroy guard reads the
/// mod's own debris as a game window, refuses to destroy the host, frees the debris to the scene
/// root and leaks it — once per window close.</para>
///
/// <para><b>AN OWNERSHIP TEST THAT DEPENDS ON EVERY FUTURE AUTHOR REMEMBERING A STRING IS BROKEN BY
/// CONSTRUCTION, AND THE LOG PROVES IT TWICE.</b> ModBuild 419 renamed the close plate's hit target
/// from <c>HitPlane</c> to <c>GloomhavenVR.HitPlane</c> because every close in every hardware log
/// read <c>HOST DESTROY DEFERRED … still holds the GAME object 'HitPlane'</c>. The ModBuild 419
/// hardware log then printed the SAME warning 24 times naming the next unprefixed sibling,
/// <c>'XBar'</c> — a child of that very plate, created eight lines further down the same file. The
/// prefix rule did not fail because someone was careless; it failed because it is a convention with
/// no enforcement and no failure mode short of a hardware round.</para>
///
/// <para><b>THE MARKER ANSWERS FOR A WHOLE SUBTREE, WHICH IS WHY IT CANNOT BE FORGOTTEN THE WAY THE
/// PREFIX COULD.</b> An author marks the ROOT they park under the host — one call, at the one place
/// they already write <c>SetParent(host, …)</c> — and every child they add under it afterwards,
/// today's <c>XBar</c> and tomorrow's glyph, is mod-owned automatically. The prefix could only ever
/// answer for the single object whose name carried it.</para>
///
/// <para><b>THE PREFIX IS KEPT AS A FALLBACK AND IS NOT DEPRECATED.</b> Several mod-owned subtrees
/// are parked under float hosts from files outside this lane (<c>GloomhavenVR.StoryDock</c>,
/// <c>GloomhavenVR.PanelSS_*</c>, <c>GloomhavenVR.WindowMaterialiseDebris*</c>) and they work
/// today. The walk asks the marker FIRST and falls back to the name, so nothing already shipped
/// changes behaviour and new code has a question it cannot answer wrongly by omission.</para>
///
/// <para><b>MULTIPLAYER:</b> a local marker component on a local GameObject. No wire field, no game
/// state, no config key.</para>
/// </summary>
internal sealed class ModOwnedContent : MonoBehaviour
{
    /// <summary>
    /// TRUE when this mod-owned subtree may ALSO hold objects belonging to the game, so the
    /// ownership walk must keep descending through it instead of stopping at this node.
    ///
    /// <para>This flag is the reason the marker is a component and not a naming rule: the two
    /// shapes genuinely differ. <c>GloomhavenVR.ModalCloseX</c> is SEALED — everything below it was
    /// created by the mod, so the walk can skip the whole subtree. <c>GloomhavenVR.StoryDock</c> is
    /// TRANSPARENT — StoryComposite parks it under the host and then moves the GAME's story-window
    /// children into it, so a walk that stopped there would call the host empty and destroy the
    /// story window with it (the case <c>CanvasConversion.FindGameContent</c>'s own doc names).</para>
    ///
    /// <para>Default false, i.e. sealed. Marking a transparent dock as sealed is the dangerous
    /// direction, so the dangerous value is the one you have to type.</para>
    /// </summary>
    internal bool MayHoldGameContent;

    /// <summary>
    /// Mark <paramref name="go"/> as the root of a mod-owned subtree. Idempotent — a second call
    /// updates the flag rather than adding a second component. Returns the marker so a caller can
    /// keep it if it wants to flip the flag later.
    /// </summary>
    internal static ModOwnedContent Mark(GameObject go, bool mayHoldGameContent = false)
    {
        if (!go.TryGetComponent(out ModOwnedContent marker))
            marker = go.AddComponent<ModOwnedContent>();
        marker.MayHoldGameContent = mayHoldGameContent;
        return marker;
    }
}
