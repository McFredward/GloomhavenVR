using GloomhavenVR.Board;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// The PEER side of the character-focus turn cue: the blinking green/red frame around a remote
/// player's <see cref="RemoteControlBoard"/>, and the matching ring around the Steam avatar in
/// that board's <see cref="OwnerTag"/>.
///
/// <para>WHAT IT IS FED: nothing but a <see cref="FocusTurnMark"/> from
/// <see cref="CharacterFocus.MarkForPeer"/> — which, since 2026-08-08, is a PURE FUNCTION of the
/// peer's synced record 22 (the character they are looking at, the bit "the character the game is
/// waiting on is mine", and that character's id when the two differ). No local state enters it.
/// That is the fix: the mark used to be re-derived by comparing their focus against THIS client's
/// <c>Choreographer.CurrentPlayerActor</c>, which is right only while both machines agree about
/// what the game is waiting on — and a pending DECISION breaks exactly that (raised inside an
/// ENEMY's action, where the turn actor is null everywhere, and hidden on every non-deciding
/// machine by <c>TakeDamagePanel.ShowOtherPlayer</c> → <c>myWindow.Hide(instant: true)</c>,
/// TakeDamagePanel.cs:1133). A peer with no record (an older build, a spectator, a scenario-less
/// client) produces <see cref="FocusTurnMark.None"/> and therefore no outline at all: the
/// pre-record behaviour.</para>
///
/// <para>WHY IT SHARES <see cref="FocusCue"/> AND NOT A COPY OF IT: the local board, the local
/// initiative rings and these two peer outlines must blink at the same rate, in the same phase and
/// in the same two colours, or the table reads as several unrelated warnings instead of one state.
/// Every colour and every alpha here comes from <see cref="FocusCue.Tint"/> — including the
/// MIXED-REALITY fork, which is decided there and nowhere else, so a peer's board can never be
/// wearing the MR palette while your own board wears the flat one. This class only owns GEOMETRY
/// (where the outline sits and how big it is).</para>
///
/// <para>THE BOARD CUE IS A THIN STROKE ON THE ASSET'S OUTER EDGE (2026-08-08). A peer's board is a
/// clone of the SAME bundled prefab the owner renders (<see cref="RemoteTrayVisual"/>, child
/// "TrayVisual"), so the very same <see cref="BoardFrame"/> builder the local board uses works here
/// unchanged and needs nothing passed to it: it finds the asset under the board root itself, traces
/// its plan-view boundary and lays ONE closed line on it — nothing inside, nothing floating beside
/// it. A peer still on the FLAT fallback board (bundle not resident on this client) has no asset,
/// and keeps the rectangle — the same degradation ladder the board itself has.</para>
///
/// <para>The Steam-avatar ring stays a <see cref="WorldFrame"/>: it frames a ~5.5 cm PICTURE (a
/// stretched unit quad), not a modelled object, so there is no silhouette to trace — a rectangle is
/// the true outline of a rectangular photo. Since 2026-08-09 it is not built here either: the same
/// picture also floats over that peer's head mask and had to grow the same ring (user request #4),
/// so the ring is ONE class both carriers drive — <see cref="AvatarTurnRing"/>.</para>
///
/// <para>Purely cosmetic; owned and torn down by the board that builds it. Never touches game
/// state, never reads a card.</para>
/// </summary>
/// <remarks>CLASSIFICATION: VR-ONLY presentation of extras extension record 22. No card identity,
/// no game write. See INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal sealed class RemoteFocusOutline
{
    /// <summary>Outward margin of the board frame past the board slab (board-local metres).</summary>
    private const float BoardMargin = 0.022f;

    /// <summary>Bar thickness of the board frame (board-local metres) — the local board's value,
    /// so a peer's board and your own wear the same weight of outline.</summary>
    private const float BoardThickness = 0.012f;

    /// <summary>Local z of the board frame (+Z points AWAY under the module convention, so a small
    /// negative pulls it toward the viewer and off the slab face).</summary>
    private const float BoardZ = -0.004f;

    private readonly int _playerId;

    /// <summary>The frame around the peer's board asset, or null when that peer is still on the
    /// flat fallback board — then <see cref="_rectFrame"/> carries the cue.</summary>
    private readonly BoardFrame? _boardFrame;

    /// <summary>The legacy rectangle: the FALLBACK renderer only (see the class doc).</summary>
    private readonly WorldFrame? _rectFrame;

    /// <summary>The ring around the board tag's Steam picture — the shared cue (see the class
    /// doc); built lazily by <see cref="AvatarTurnRing"/> the first tick a picture exists.</summary>
    private readonly AvatarTurnRing _avatarRing;

    /// <summary>
    /// Build the board frame under <paramref name="boardRoot"/>. Frames the real board asset's
    /// contour when this peer's board was built from the bundled prefab; falls back to a rectangle
    /// of <paramref name="boardSize"/> board-local metres when it was not. The avatar ring is built
    /// lazily, the first time the owner tag actually has an avatar quad (the Steam picture arrives
    /// asynchronously).
    /// </summary>
    internal RemoteFocusOutline(int playerId, Transform boardRoot, Vector2 boardSize)
    {
        _playerId = playerId;
        _avatarRing = new AvatarTurnRing(playerId, "board");
        _boardFrame = BoardFrame.Build(boardRoot, $"remote control board [{playerId}]");
        // DRAW ORDER is NOT set here any more. The stroke is board FURNITURE (a transparent,
        // depth-less surface in the board's own plane at BoardZ = −0.004), and since 2026-08-09 a
        // peer's board HAS a distance-ranked draw-order cluster of its own — the limitation this
        // seat used to inherit is gone. The board's sweep seats the stroke at the tier that depth
        // earns, which is the same "under this board's docked widgets" it asserted before, now
        // ranked against the converted-panel ladder as well. The local board's stroke is
        // ladder-ranked the same way — see FocusDriver.TickBoardFrame.
        if (_boardFrame == null)
        {
            _rectFrame = WorldFrame.Build(
                boardRoot, $"GloomhavenVR.RemoteFocusFrame[{playerId}]",
                new Vector2(boardSize.x + BoardMargin * 2f, boardSize.y + BoardMargin * 2f),
                BoardThickness, BoardZ);
        }
    }

    /// <summary>
    /// Per-frame refresh. <paramref name="visible"/> is the board's own visibility decision (a
    /// hidden board must not leave a frame hanging in the void). <paramref name="avatarQuad"/> is
    /// the owner tag's Steam-avatar quad, or null while it has none — pass it every tick; the ring
    /// re-seats itself when the tag rebuilds.
    /// </summary>
    internal void Tick(bool visible, Transform? avatarQuad, Vector2 avatarSize)
    {
        Color? tint = visible ? FocusCue.Tint(CharacterFocus.MarkForPeer(_playerId)) : null;
        _boardFrame?.Apply(tint);
        _rectFrame?.Apply(tint);
        // The picture's ring reads the SAME mark from the SAME palette one call deeper; passing
        // `visible` rather than the resolved tint keeps that single decision point intact.
        _avatarRing.Tick(avatarQuad, avatarSize, visible);
    }

    internal void Destroy()
    {
        _boardFrame?.Destroy();
        _rectFrame?.Destroy();
        _avatarRing.Destroy();
    }
}
