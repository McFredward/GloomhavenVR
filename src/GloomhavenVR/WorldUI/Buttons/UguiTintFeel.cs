namespace GloomhavenVR.WorldUI;

/// <summary>
/// <b>HOW A FLAT uGUI CONTROL IN THIS MOD ANSWERS A HOVER</b> — the one number behind every
/// <c>Selectable.ColorBlock</c> the mod authors, so that a window's close X, an options row and a
/// variant tile all brighten at the same rate.
///
/// <para><b>WHY IT EXISTS (ModBuild 439, survey row R20).</b> <c>fadeDuration = 0.08f</c> was
/// written by hand in three unrelated files — <c>ModalCloseButton</c>, <c>VROptionsTab.2.Rows</c>
/// and <c>VariantTiles</c>. Three literals for one piece of FEEL is how two of them end up at 0.08
/// and one at 0.12 after somebody tunes the menu, and the player reads that as the close button
/// being a different kind of object from the row he just clicked — the same complaint that
/// collapsed the combat log's pin into the board's cap ("der fixiert button sollte gleich sein wie
/// der button am controllboard") and its X into the shared one ("der X Button ist anders").</para>
///
/// <para><b>WHY IT IS NOT IN THE OPTIONS TAB, AND NOT IN <c>ButtonTuning</c>.</b> The options menu
/// is a CONSUMER of this feel, not its owner: a close button on every window in the game reading a
/// constant owned by the settings screen is a dependency pointing the wrong way, and it would put
/// the whole options tree in the compile path of every window's chrome. <c>ButtonTuning</c> is the
/// wrong home for the opposite reason — it is the live-tunable geometry of the 3D BOARD caps, split
/// into per-category bind sets precisely so one family of caps cannot resize another, and a flat
/// uGUI tint is not one of those families. So this is a neutral third place that both sides may
/// reference and neither owns.</para>
///
/// <para><b>NOT A CONFIG KEY, DELIBERATELY.</b> Nothing here is a dial the player asked for; it is
/// the mod's own authored feel, and a settings entry for it would be a row he has to read past to
/// find the ones he wants. If it ever becomes a dial it becomes one HERE, once, and all three
/// consumers pick it up.</para>
///
/// <para><b>MULTIPLAYER:</b> nothing here goes on the wire. A hover tint is drawn on the machine
/// whose pointer is doing the hovering; a peer's mirror of a widget is not hovered by anyone.</para>
/// </summary>
internal static class UguiTintFeel
{
    /// <summary>
    /// Seconds a mod-authored uGUI control takes to cross-fade between its normal, highlighted and
    /// pressed tints (<c>ColorBlock.fadeDuration</c>). The historic 0.08 — short enough that the
    /// answer feels immediate under a laser that is never perfectly still, long enough that a beam
    /// jittering across an edge does not strobe the control.
    /// </summary>
    internal const float HoverTintFadeSeconds = 0.08f;
}
