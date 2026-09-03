using GloomhavenVR.Hands;

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
/// <para><b>Last writer wins, per owner — and the displaced writer keeps a copy (this build).</b>
/// Two hands can hold two props, but the game has ONE of each window
/// (<c>decompiled/GH.Runtime/UITextInfoPanel.cs</c> and <c>UIPropInfoPanel.cs</c> are both
/// <c>Singleton&lt;T&gt;</c> with a single content slot: one <c>_elements[]</c> row set, one
/// <c>propName</c> + <c>conditions</c> list). User, 2026-09-03: with a prop in EACH hand only one
/// of the two showed its floating info. So the window belongs to whichever prop raised it last —
/// the same rule the figure card applies (<c>StatPanelSurface.Reconcile</c>: PRIMARY is the
/// latest-grabbed hand) — and the prop it displaced becomes that window's UNDERSTUDY: its card is
/// the frozen snapshot copy <c>PropInfoSurface.SnapshotDisplacedCard</c> takes of the window
/// IMMEDIATELY BEFORE the later grab overwrites it, docked at the understudy's own hand. The
/// later grab keeps the live window for the same reason the figure card gives it the real panel:
/// at the instant of the second grab the window is ALREADY showing the earlier prop, fully
/// populated, so the copy can be taken from what is on screen with nothing to borrow and nothing
/// to blank; and the prop just picked up is the one the player is looking at.</para>
///
/// <para><see cref="Release"/> is keyed on the owner so the first prop released cannot take the
/// second one's card down with it; releasing the OWNER promotes the understudy back to the live
/// window (its content is re-pushed at once and the copy is destroyed), releasing the UNDERSTUDY
/// only destroys the copy.</para>
///
/// <para>Pure presentation state: nothing here is read by any game system, and nothing here writes
/// game state.</para>
/// </summary>
internal static class HeldPropCard
{
    // ModBuild 404 — ONE OWNER PER WINDOW KIND (user: "sobald ich in beide Hände ein prop nehme
    // ist der schwebende Hinweis … wieder an den Kopfbewegungen gebunden"). With a single owner,
    // the second hand's Claim overwrote the first: a Trap (rich window) in one hand and an
    // Obstacle (plain window) in the other left the first window OPEN but UN-OWNED, so its
    // surface fell back to the head-follower — exactly the report. The game shows the two kinds
    // in two different windows, so two owners is the honest count.
    private static GrabbableProp? _ownerPropInfo;
    private static GrabbableProp? _ownerTextInfo;

    // The EARLIER hold a later grab of the SAME card kind displaced out of the window. Its card
    // is PropInfoSurface's snapshot copy, not the live window, so it must neither re-assert into
    // the window (that would be a write war with the owner) nor be docked by the live watch.
    private static GrabbableProp? _understudyPropInfo;
    private static GrabbableProp? _understudyTextInfo;

    /// <summary>The window a held prop most recently claimed, or <see cref="HeldPropCardWindow.None"/>.</summary>
    internal static HeldPropCardWindow Window { get; private set; }

    /// <summary>
    /// Called by the writer IMMEDIATELY BEFORE it repopulates <paramref name="window"/> for the
    /// prop in its hand. If another still-held prop owns that window, the content about to be
    /// overwritten is that prop's card — snapshot it now, while it is still on screen; the copy
    /// is committed by the <see cref="Claim"/> that follows a successful push and discarded by
    /// <see cref="EndClaim"/> otherwise. Must precede the game's <c>Hide()</c> in the rich route:
    /// that hide destroys the effect rows, and a copy taken after it would be an empty window.
    /// </summary>
    internal static void BeforePopulate(GrabbableProp writer, HeldPropCardWindow window)
    {
        GrabbableProp? owner = OwnerOf(window);
        if (owner == null || ReferenceEquals(owner, writer))
            return;
        HandSide? side = owner.HolderSide;
        if (side == null)
            return;
        WorldUI.Surfaces.PropInfoSurface.SnapshotDisplacedCard(window, side.Value, owner.Label);
    }

    /// <summary>Record that <paramref name="owner"/> (a <see cref="GrabbableProp"/>) has just
    /// populated <paramref name="window"/> for the prop in its hand. A different prop that owned
    /// the window becomes its understudy and the snapshot <see cref="BeforePopulate"/> took of its
    /// content is committed as that prop's card.</summary>
    internal static void Claim(GrabbableProp owner, HeldPropCardWindow window)
    {
        if (window != HeldPropCardWindow.TextInfo && window != HeldPropCardWindow.PropInfo)
            return;
        GrabbableProp? prev = OwnerOf(window);
        if (prev != null && !ReferenceEquals(prev, owner))
        {
            SetUnderstudy(window, prev);
            WorldUI.Surfaces.PropInfoSurface.CommitSecondCard(window);
        }
        else if (ReferenceEquals(UnderstudyOf(window), owner))
        {
            // The understudy re-took the window itself (a promotion's re-push, or the rich route
            // conceding to the plain one) — it is the owner again and the copy is stale.
            SetUnderstudy(window, null);
            WorldUI.Surfaces.PropInfoSurface.DropSecondCard("the understudy re-took the game window");
        }
        SetOwner(window, owner);
        Window = window;
    }

    /// <summary>The election is over for this grab: a snapshot that no <see cref="Claim"/>
    /// committed (the rich route hid the window and then declined, so the later prop went to the
    /// OTHER window) is destroyed — the displaced prop still owns its window and re-asserts it
    /// through its own <c>TickInfo</c>.</summary>
    internal static void EndClaim() => WorldUI.Surfaces.PropInfoSurface.DiscardUncommittedSnapshot();

    /// <summary>Drop <paramref name="owner"/>'s claim. A no-op when a LATER hold has since taken
    /// the card over — the second prop's card must survive the first prop's release — except that
    /// releasing an OWNER with an understudy promotes the understudy back to the live window, and
    /// releasing an UNDERSTUDY destroys its copy.</summary>
    internal static void Release(GrabbableProp owner)
    {
        ReleaseWindow(HeldPropCardWindow.TextInfo, owner);
        ReleaseWindow(HeldPropCardWindow.PropInfo, owner);
        Window = _ownerPropInfo != null ? HeldPropCardWindow.PropInfo
               : _ownerTextInfo != null ? HeldPropCardWindow.TextInfo
               : HeldPropCardWindow.None;
    }

    private static void ReleaseWindow(HeldPropCardWindow window, GrabbableProp owner)
    {
        if (ReferenceEquals(UnderstudyOf(window), owner))
        {
            SetUnderstudy(window, null);
            WorldUI.Surfaces.PropInfoSurface.DropSecondCard("the earlier prop was released");
            return;
        }
        if (!ReferenceEquals(OwnerOf(window), owner))
            return;
        GrabbableProp? promoted = UnderstudyOf(window);
        SetOwner(window, promoted);
        SetUnderstudy(window, null);
        if (promoted == null)
            return;
        // The later prop left the hand: its content is being hidden by its own ClearInfo (which
        // hides BEFORE it releases, for exactly this order), so the earlier prop's content goes
        // straight back into the live window and its frozen copy goes away — never a frame with
        // two cards for one prop, never a frame with none while the game window can show it.
        WorldUI.Surfaces.PropInfoSurface.DropSecondCard("promoted back to the game window");
        promoted.RePushInfo();
    }

    /// <summary>Clear unconditionally (scenario teardown, feature dial off).</summary>
    internal static void Clear()
    {
        _ownerPropInfo = null;
        _ownerTextInfo = null;
        _understudyPropInfo = null;
        _understudyTextInfo = null;
        Window = HeldPropCardWindow.None;
        WorldUI.Surfaces.PropInfoSurface.DropSecondCard("held-prop registry cleared");
    }

    /// <summary>
    /// TRUE when the window this watch/state is following is the one a held prop owns.
    /// <paramref name="isTextInfo"/> is the caller's existing per-window discriminator, so the two
    /// presentation steps keep their shape and gain one term.
    /// </summary>
    internal static bool Owns(bool isTextInfo) =>
        isTextInfo ? _ownerTextInfo != null : _ownerPropInfo != null;

    /// <summary>The prop whose content the window is showing, or null while no held prop owns it.</summary>
    internal static GrabbableProp? OwnerOf(HeldPropCardWindow window) => window switch
    {
        HeldPropCardWindow.TextInfo => _ownerTextInfo,
        HeldPropCardWindow.PropInfo => _ownerPropInfo,
        _ => null,
    };

    /// <summary>TRUE while <paramref name="prop"/>'s card is the snapshot copy rather than the
    /// live window — it must not re-assert into a window another prop is showing in.</summary>
    internal static bool IsUnderstudy(GrabbableProp prop) =>
        ReferenceEquals(_understudyTextInfo, prop) || ReferenceEquals(_understudyPropInfo, prop);

    private static GrabbableProp? UnderstudyOf(HeldPropCardWindow window) =>
        window == HeldPropCardWindow.TextInfo ? _understudyTextInfo : _understudyPropInfo;

    private static void SetOwner(HeldPropCardWindow window, GrabbableProp? owner)
    {
        if (window == HeldPropCardWindow.TextInfo)
            _ownerTextInfo = owner;
        else
            _ownerPropInfo = owner;
    }

    private static void SetUnderstudy(HeldPropCardWindow window, GrabbableProp? understudy)
    {
        if (window == HeldPropCardWindow.TextInfo)
            _understudyTextInfo = understudy;
        else
            _understudyPropInfo = understudy;
    }
}
