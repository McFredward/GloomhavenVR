namespace GloomhavenVR.Board.FigureGrab;

/// <summary>Which of the game's two hover-info windows a prop IN THE HAND is currently
/// occupying.</summary>
internal enum HeldPropCardWindow
{
    /// <summary>No prop in a hand owns either window.</summary>
    None,

    /// <summary>The plain <c>UITextInfoPanel</c> ('Text Info Panel', ID TextInfoPanel) — a title
    /// and up to two lines. The fallback, and everything a chest/gold/obstacle/door resolves to.</summary>
    TextInfo,

    /// <summary>The rich <c>UIPropInfoPanel</c> ('Prop Info Panel', ID TrapInfoPanel) — the card
    /// that carries a trap's EFFECT rows.</summary>
    PropInfo,
}

/// <summary>
/// WHICH WINDOW THE HELD-PROP CARD IS IN, for the two presentation steps that have to follow it.
///
/// <para><b>WHY THIS EXISTS (ModBuild 366).</b> User, 2026-09-03, verbatim: "Wenn ich mit dem
/// laser über eine Falle hovere sehe ich noch weitere 'Effekte' der Falle. Wenn ich die Falle
/// allerdings in die Hand nehme mit dem neuen Feature sehen ich nur den Hinweis 'Falle' ohne die
/// Effekte darunter. Ich will, dass wenn man etwas in die Hand nimmt immer die detaillierteste
/// Info angezeigt wird inkl. aller effekte bei allen props die man in die Hand nehmen kann."</para>
///
/// <para>The fix for that sentence moves the held card from the PLAIN window to the RICH one
/// whenever the prop has a rich one. ModBuild 360 had given the held card a dock that rides the
/// prop, and both halves of that dock — <c>WorldUI/Surfaces/PropInfoSurface.PlaceWatch</c> and
/// <c>WorldUI/Tooltips/HexHintFacing.LateTick</c> — selected the held branch by asking "is this the
/// <c>UITextInfoPanel</c> watch?". That question was a correct STAND-IN for "is this the held
/// card?" only while the held card could not be anything else. Moving the content without moving
/// that test is the exact shape of fixing the content and losing the position, so the two questions
/// are separated here: this type answers "is this window the held card?" and the surfaces ask it.</para>
///
/// <para><b>Last writer wins, per owner.</b> Two hands can hold two props, but the game has ONE of
/// each window, so the card belongs to whichever prop raised it last — the same rule
/// <c>PropInfoSurface.TryResolveHeldAnchor</c> already applies when it docks at the MOST RECENT
/// held prop. <see cref="Release"/> is keyed on the owner so the first prop released cannot take
/// the second one's card down with it.</para>
///
/// <para>Pure presentation state: nothing here is read by any game system, and nothing here writes
/// game state.</para>
/// </summary>
internal static class HeldPropCard
{
    private static object? _owner;

    /// <summary>The window a held prop currently owns, or <see cref="HeldPropCardWindow.None"/>.</summary>
    internal static HeldPropCardWindow Window { get; private set; }

    /// <summary>Record that <paramref name="owner"/> (a <see cref="GrabbableProp"/>) has just
    /// populated <paramref name="window"/> for the prop in its hand.</summary>
    internal static void Claim(object owner, HeldPropCardWindow window)
    {
        _owner = owner;
        Window = window;
    }

    /// <summary>Drop <paramref name="owner"/>'s claim. A no-op when a LATER hold has since taken
    /// the card over — the second prop's card must survive the first prop's release.</summary>
    internal static void Release(object owner)
    {
        if (!ReferenceEquals(_owner, owner))
            return;
        _owner = null;
        Window = HeldPropCardWindow.None;
    }

    /// <summary>Clear unconditionally (scenario teardown, feature dial off).</summary>
    internal static void Clear()
    {
        _owner = null;
        Window = HeldPropCardWindow.None;
    }

    /// <summary>
    /// TRUE when the window this watch/state is following is the one a held prop owns.
    /// <paramref name="isTextInfo"/> is the caller's existing per-window discriminator, so the two
    /// presentation steps keep their shape and gain one term.
    /// </summary>
    internal static bool Owns(bool isTextInfo) =>
        isTextInfo ? Window == HeldPropCardWindow.TextInfo : Window == HeldPropCardWindow.PropInfo;
}
