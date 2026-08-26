using GloomhavenVR.Board;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// THE blinking frame around a peer's STEAM AVATAR — one implementation, two places it appears:
/// the picture in the corner tag of their control board (<see cref="RemoteFocusOutline"/>) and the
/// picture floating over their head mask (<see cref="RemoteNameTag"/>).
///
/// <para>WHY IT EXISTS (user request 2026-08-09 #4): "Ich will auch, dass das Steambild über der
/// Maske umrahmt blinkt bei dem Spieler der gerade am Zug ist." The board-corner picture has worn
/// this ring since the character-focus round; the one over the head — the only identity marker you
/// can see when a peer's board is hidden, folded away or simply not where you are looking — did
/// not. Same question, same answer, so it must be the same cue and not a second one: this class
/// exists so the two can never drift apart in size, colour, phase or MR behaviour.</para>
///
/// <para>WHAT DRIVES IT — AND WHY IT CANNOT DESYNCHRONISE. Nothing here reads a clock, a turn or a
/// game object. The colour is <see cref="FocusCue.Tint"/> of
/// <see cref="CharacterFocus.MarkForPeer"/>, i.e. a PURE FUNCTION of that peer's synced extras
/// record 22 (the character they are looking at, the bit "the character the game is waiting on is
/// mine", and that character's id): the very same expression the peer's board frame and their
/// initiative-track rings already evaluate on this client. So the ring, the board stroke and the
/// track ring are three renderings of ONE value — a peer can never be blinking here and silent
/// there — and because the value comes off the wire rather than from a local turn read, this
/// client's own idea of whose turn it is cannot make it disagree with the peer's own board. No new
/// wire field: the multiplayer 1:1 rule is met by consuming the record that already exists.</para>
///
/// <para>THE ANIMATION IS THE SHARED ONE. <see cref="FocusCue"/> blinks at 0.667 Hz off
/// <see cref="Time.unscaledTime"/> — a continuous sine, so the ring breathes rather than steps,
/// and every cue in the scene rides the same clock and is therefore IN PHASE. There is
/// deliberately no extra fade of this ring's own: an onset ramp only here would make the head ring
/// arrive after the board stroke and the track ring, which is the opposite of reading as one
/// state.</para>
///
/// <para>Geometry only, no text — nothing to localise. Purely cosmetic; never touches game
/// state.</para>
/// </summary>
/// <remarks>CLASSIFICATION: VR-ONLY presentation of extras extension record 22 — zero new wire.
/// See INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal sealed class AvatarTurnRing
{
    /// <summary>Bar thickness of the ring (parent-local metres). It frames a ~5.5-7.5 cm picture,
    /// so it stays thin — the board's own 12 mm stroke would swallow it. Parent-local on purpose:
    /// over a head the row is scaled by the sender's world zoom
    /// (<c>RemoteNameTag</c>), and the ring must scale with the picture it frames.</summary>
    private const float Thickness = 0.005f;

    /// <summary>Outward margin past the avatar quad (parent-local metres).</summary>
    private const float Margin = 0.006f;

    /// <summary>Local z of the ring (+Z points AWAY under the module convention, so a small
    /// negative pulls it toward the viewer and off the picture's own plane).</summary>
    private const float ProudZ = -0.004f;

    private readonly int _playerId;
    private readonly string _name;

    private WorldFrame? _ring;
    private Transform? _host;

    /// <summary>A ring for <paramref name="playerId"/>; <paramref name="name"/> distinguishes the
    /// two carriers in the scene hierarchy ("board" / "head").</summary>
    internal AvatarTurnRing(int playerId, string name)
    {
        _playerId = playerId;
        _name = name;
    }

    /// <summary>
    /// Per-frame refresh. <paramref name="avatarQuad"/> is the live Steam-avatar quad or null while
    /// there is none (no picture yet, or the carrier rebuilt) — pass it every tick; the ring
    /// re-seats itself when the quad is replaced. <paramref name="visible"/> is the carrier's own
    /// visibility decision, so a hidden tag never leaves a ring hanging in the void.
    ///
    /// <para>Returns TRUE on the tick the ring was (re)built — the head carrier uses that to
    /// re-cache its renderers immediately, so a fresh ring never spends a frame off the panel
    /// distance ladder its row rides.</para>
    /// </summary>
    internal bool Tick(Transform? avatarQuad, Vector2 avatarSize, bool visible)
    {
        Color? tint = visible ? FocusCue.Tint(CharacterFocus.MarkForPeer(_playerId)) : null;
        if (avatarQuad == null)
        {
            _ring?.Apply(null);
            return false;
        }

        bool built = false;
        if (_ring == null || !ReferenceEquals(_host, avatarQuad))
        {
            // The carrier rebuilds its children whenever the sprite or the username changes, so
            // the ring is rebuilt against the LIVE quad rather than resurrected.
            _ring?.Destroy();
            // SIBLING of the quad, not a child: the quad carries a non-uniform localScale (it is a
            // unit Unity quad stretched to the avatar size) which a child would inherit and be
            // squashed by.
            Transform parent = avatarQuad.parent != null ? avatarQuad.parent : avatarQuad;
            _ring = WorldFrame.Build(
                parent, $"GloomhavenVR.AvatarTurnRing[{_name}:{_playerId}]",
                new Vector2(avatarSize.x + Margin * 2f, avatarSize.y + Margin * 2f),
                Thickness, ProudZ);
            _ring.Apply(null);
            _host = avatarQuad;
            built = true;
        }

        // Follow the quad's own seat inside its row (the avatar sits left of the name).
        Vector3 seat = avatarQuad.localPosition;
        _ring!.SetLocalSeat(new Vector3(seat.x, seat.y, seat.z + ProudZ));
        _ring.Apply(tint);
        return built;
    }

    internal void Destroy()
    {
        _ring?.Destroy();
        _ring = null;
        _host = null;
    }
}
