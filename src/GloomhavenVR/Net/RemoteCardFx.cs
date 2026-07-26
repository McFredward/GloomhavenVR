using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// Plays a remote player's CARD ANIMATIONS locally (user report 6). One event byte from the extras
/// packet (<see cref="NetProtocol.FlagCardFx"/>, decoded by <see cref="NetCardFx"/>) says which two
/// pieces of that player's VR furniture the card travelled between; this class resolves both
/// anchors against the SENDER'S OWN synced poses and flies a card-BACK slab along the same arc the
/// local <see cref="VRCard.FlyToPile"/> uses — so a peer sees the card slide into their discard /
/// burnt stack, glide back into their hand fan, or dock into a play slot, at the moment it really
/// happened.
///
/// WHY A LOCAL REPLAY AND NOT A POSE STREAM: the flight lasts ~0.4 s. Streaming it would need the
/// full 15 Hz pose channel for its whole duration (~20 B per packet) and would still stutter under
/// loss; replaying it from a 2-byte event costs one packet and is immune to jitter afterwards.
///
/// ANTI-CHEAT: the slab is a BACK on both faces, exactly like <see cref="RemoteHandFan"/>'s default
/// and the held-card slab. No card identity is ever transmitted or rendered here.
///
/// VISIBILITY: gated on <see cref="NetModule.RemoteBoards"/> — every anchor except the hand fan is
/// board furniture, so a player who has chosen not to see remote boards does not get cards flying
/// to invisible places either. Also requires the sender's board pose (<c>owner.HasBoard</c>).
/// </summary>
internal sealed class RemoteCardFx
{
    /// <summary>Concurrent flights (a turn-clear can launch both round cards at once). Beyond this
    /// the oldest slab is recycled — the animation is cosmetic, never a queue that may back up.</summary>
    private const int MaxFlights = 6;

    /// <summary>Arc height as a fraction of the travelled distance — the value
    /// <see cref="VRCard.FlyArcHeightFraction"/> uses locally, so the bow matches.</summary>
    private const float ArcFraction = 0.28f;

    /// <summary>Absolute minimum arc peak in board-scaled metres, mirroring
    /// <c>CardsDriver.BoardArcMin</c> (~1.5 card heights) so a short hop still clears the board.</summary>
    private const float MinArcCardHeights = 1.5f;

    private readonly RemoteAvatar _owner;

    private sealed class Flight
    {
        public GameObject? Go;
        public Vector3 From;
        public Vector3 To;
        public Vector3 ArcUp;
        public float Arc;
        public float Elapsed;
        public bool Active;
    }

    private readonly List<Flight> _flights = new(MaxFlights);
    private Mesh? _mesh;
    private GameObject? _root;
    private int _played;   // diagnostics: how many flights this avatar has played

    public RemoteCardFx(RemoteAvatar owner)
    {
        _owner = owner;
    }

    // ------------------------------------------------------------------ play --

    /// <summary>
    /// Start one flight for a decoded event byte. Silently skipped when the endpoints cannot be
    /// resolved (no board pose yet / remote boards hidden / hand not tracked) — a missed cosmetic
    /// flight is always better than a card arcing to the world origin.
    /// </summary>
    public void Play(byte endpoints)
    {
        CardFxAnchor from = NetCardFx.From(endpoints);
        CardFxAnchor to = NetCardFx.To(endpoints);

        if (NetModule.RemoteBoards == null || NetModule.RemoteBoards.Value == RemoteBoardVisibility.Off)
            return;
        if (!TryResolve(from, out Vector3 a) || !TryResolve(to, out Vector3 b))
        {
            VRLog.Info("Net", $"Remote card FX [player {_owner.PlayerId}]: {from} -> {to} SKIPPED " +
                              "(endpoint unresolved — no synced board pose or the hand is not tracked).");
            return;
        }

        Flight f = Acquire();
        if (f.Go == null)
            return;

        float scale = _owner.BoardScale > 0f ? _owner.BoardScale : 1f;
        f.From = a;
        f.To = b;
        f.ArcUp = _owner.HasBoard ? _owner.BoardRotation * Vector3.up : Vector3.up;
        f.Arc = Mathf.Max(RemoteHandFan.DefaultCardHeight * MinArcCardHeights * scale,
                          Vector3.Distance(a, b) * ArcFraction);
        f.Elapsed = 0f;
        f.Active = true;
        f.Go.transform.localScale = Vector3.one * scale;
        f.Go.transform.SetPositionAndRotation(a, FaceHeadRotation(a));
        if (!f.Go.activeSelf)
            f.Go.SetActive(true);

        _played++;
        VRLog.Info("Net", $"Remote card FX [player {_owner.PlayerId}]: {from} -> {to} playing " +
                          $"({NetProtocol.CardFxSeconds:F2}s, arc {f.Arc:F3} m) — flight #{_played}.");
    }

    // ------------------------------------------------------------------ per frame --

    public void Tick(float dt)
    {
        for (int i = 0; i < _flights.Count; i++)
        {
            Flight f = _flights[i];
            if (!f.Active || f.Go == null)
                continue;
            f.Elapsed += Mathf.Max(dt, 0f);
            float t = NetProtocol.CardFxSeconds > 0f
                ? Mathf.Clamp01(f.Elapsed / NetProtocol.CardFxSeconds)
                : 1f;
            // Same shape as VRCard's fly: smoothstep along the chord + a sine bow along the
            // board's up axis, so the card visibly clears the board instead of skimming it.
            float e = t * t * (3f - 2f * t);
            Vector3 p = Vector3.Lerp(f.From, f.To, e) + f.ArcUp * (Mathf.Sin(t * Mathf.PI) * f.Arc);
            f.Go.transform.SetPositionAndRotation(p, FaceHeadRotation(p));
            if (t >= 1f)
            {
                f.Active = false;
                f.Go.SetActive(false);
            }
        }
    }

    // ------------------------------------------------------------------ anchors --

    /// <summary>
    /// World position of one anchor on the SENDER's furniture. Board anchors are the sender's
    /// synced board pose × the shared board-local layout (<see cref="RemoteControlBoard.AnchorLocal"/>),
    /// which is why the flight lands on that player's piles wherever they parked their board; the
    /// hand-fan anchor is their non-dominant palm plus the fan standoff (mirroring
    /// <see cref="RemoteHandFan"/>'s own pose math).
    /// </summary>
    private bool TryResolve(CardFxAnchor anchor, out Vector3 world)
    {
        world = default;
        if (anchor == CardFxAnchor.HandFan)
        {
            Transform? holder = _owner.NonDominantHandHolder;
            if (holder == null || !holder.gameObject.activeInHierarchy)
                return false;
            world = holder.position + holder.up * (RemoteHandFan.PalmStandoff * _owner.AppliedScale);
            return true;
        }

        if (!_owner.HasBoard)
            return false;
        float scale = _owner.BoardScale > 0f ? _owner.BoardScale : 1f;
        world = _owner.BoardPosition + _owner.BoardRotation * (RemoteControlBoard.AnchorLocal(anchor) * scale);
        return true;
    }

    /// <summary>Billboard the slab so its BACK faces the local viewer (the mod's card convention:
    /// +Z points away from the reader). A flying card is only ever seen edge-on otherwise.</summary>
    private static Quaternion FaceHeadRotation(Vector3 at)
    {
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head == null)
            return Quaternion.identity;
        Vector3 away = at - head.transform.position;
        return away.sqrMagnitude > 1e-6f
            ? Quaternion.LookRotation(away.normalized, Vector3.up)
            : Quaternion.identity;
    }

    // ------------------------------------------------------------------ pool --

    private Flight Acquire()
    {
        for (int i = 0; i < _flights.Count; i++)
        {
            if (!_flights[i].Active)
                return _flights[i];
        }
        if (_flights.Count >= MaxFlights)
        {
            // All busy: recycle the OLDEST (largest elapsed) rather than dropping the new event.
            Flight oldest = _flights[0];
            for (int i = 1; i < _flights.Count; i++)
            {
                if (_flights[i].Elapsed > oldest.Elapsed)
                    oldest = _flights[i];
            }
            return oldest;
        }

        EnsureRoot();
        var f = new Flight();
        if (_root != null && _mesh != null)
        {
            var go = new GameObject($"CardFx{_flights.Count}");
            go.transform.SetParent(_root.transform, worldPositionStays: false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = _mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = CardMesh.CreateBackMaterial(); // SHARED cache — never ours to destroy
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.SetActive(false);
            VRLayers.Apply(go);
            f.Go = go;
        }
        _flights.Add(f);
        return f;
    }

    private void EnsureRoot()
    {
        if (_root != null)
            return;
        _root = new GameObject($"GloomhavenVR.RemoteCardFx[{_owner.PlayerId}]");
        Object.DontDestroyOnLoad(_root);
        _root.hideFlags = HideFlags.HideAndDontSave;
        _root.transform.localScale = Vector3.one;
        _mesh = RemoteHandFan.BuildBackSlab(RemoteHandFan.DefaultCardWidth, RemoteHandFan.DefaultCardHeight);
        VRLayers.Apply(_root);
    }

    public void Destroy()
    {
        _flights.Clear();
        if (_mesh != null)
            Object.Destroy(_mesh); // asset — not freed with the GameObject tree
        _mesh = null;
        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
        }
    }
}
