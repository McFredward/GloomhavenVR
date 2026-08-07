using GloomhavenVR.Board;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// The PEER side of the character-focus turn cue: the blinking green/red frame around a remote
/// player's <see cref="RemoteControlBoard"/>, and the matching ring around the Steam avatar in
/// that board's <see cref="OwnerTag"/>.
///
/// <para>WHAT IT IS FED: nothing but a <see cref="FocusTurnMark"/> from
/// <see cref="CharacterFocus.MarkForPeer"/>, which combines the peer's synced record 22 (their
/// focused character + "the character at turn is mine") with THIS client's own read of who is at
/// turn. So the turn itself is never taken on trust from a packet — only the two facts a receiver
/// genuinely cannot derive travel, and everything else is re-derived locally. A peer with no
/// record (an older build, a spectator, a scenario-less client) produces
/// <see cref="FocusTurnMark.None"/> and therefore no outline at all: the pre-record behaviour.</para>
///
/// <para>WHY IT SHARES <see cref="FocusCue"/> AND NOT A COPY OF IT: the local board, the local
/// initiative rings and these two peer outlines must blink at the same rate, in the same phase and
/// in the same two colours, or the table reads as several unrelated warnings instead of one state.
/// Every colour and every alpha here comes from <see cref="FocusCue.Tint"/>; this class only owns
/// GEOMETRY (where the frame sits and how big it is).</para>
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

    /// <summary>Bar thickness of the avatar ring (metres). Thinner than the board's: it frames a
    /// ~5.5 cm picture, and a 12 mm bar would swallow it.</summary>
    private const float AvatarThickness = 0.005f;

    /// <summary>Outward margin of the avatar ring past the avatar quad (metres).</summary>
    private const float AvatarMargin = 0.006f;

    private readonly int _playerId;
    private readonly WorldFrame _boardFrame;
    private WorldFrame? _avatarRing;
    private Transform? _avatarHost;

    /// <summary>
    /// Build the board frame under <paramref name="boardRoot"/> for a slab of
    /// <paramref name="boardSize"/> board-local metres. The avatar ring is built lazily, the first
    /// time the owner tag actually has an avatar quad (the Steam picture arrives asynchronously).
    /// </summary>
    internal RemoteFocusOutline(int playerId, Transform boardRoot, Vector2 boardSize)
    {
        _playerId = playerId;
        _boardFrame = WorldFrame.Build(
            boardRoot, $"GloomhavenVR.RemoteFocusFrame[{playerId}]",
            new Vector2(boardSize.x + BoardMargin * 2f, boardSize.y + BoardMargin * 2f),
            BoardThickness, BoardZ);
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
        _boardFrame.Apply(tint);

        if (avatarQuad == null)
        {
            _avatarRing?.Apply(null);
            return;
        }
        if (_avatarRing == null || !ReferenceEquals(_avatarHost, avatarQuad))
        {
            // The tag rebuilds its children whenever the avatar sprite or the username changes,
            // so the ring is rebuilt against the live quad rather than resurrected.
            _avatarRing?.Destroy();
            // The ring is a SIBLING of the quad, not a child: the quad carries a non-uniform
            // localScale (it is a unit Unity quad stretched to the avatar size), which a child
            // would inherit and be squashed by.
            Transform? parent = avatarQuad.parent;
            _avatarRing = WorldFrame.Build(
                parent != null ? parent : avatarQuad,
                $"GloomhavenVR.RemoteFocusAvatarRing[{_playerId}]",
                new Vector2(avatarSize.x + AvatarMargin * 2f, avatarSize.y + AvatarMargin * 2f),
                AvatarThickness, BoardZ);
            _avatarRing.Apply(null);
            _avatarHost = avatarQuad;
        }
        // Follow the quad's own seat inside the tag row (the avatar sits left of the name).
        Vector3 seat = avatarQuad.localPosition;
        _avatarRing.SetLocalSeat(new Vector3(seat.x, seat.y, seat.z + BoardZ));
        _avatarRing.Apply(tint);
    }

    internal void Destroy()
    {
        _boardFrame.Destroy();
        _avatarRing?.Destroy();
        _avatarRing = null;
        _avatarHost = null;
    }
}
