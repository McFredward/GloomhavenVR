using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// The cosmetic ghost of a remote player's ITEM fan (<see cref="ItemsPile"/>) — the counterpart of
/// <see cref="RemoteHandFan"/> for equipped item cards.
///
/// ROOT CAUSE this exists (user report 5, multiplayer half): the extras packet only ever carried
/// the ABILITY hand-card count, so when a peer raised their item fan — a very visible, near-square
/// arc of cards floating over their palm or their board — every other player saw absolutely
/// nothing. The item fan is now broadcast as an additive count + a held/board-anchored flag
/// (<see cref="NetProtocol.FlagItemFan"/>), and this renders it.
///
/// ANTI-CHEAT / bandwidth: no item identity, art or state ever rides the WIRE — a peer transmits how
/// many item cards are up and where the fan is, nothing more. Item cards are near-square rather than
/// 63.5×88, so the slab uses its own dimensions; the real per-card face size is deliberately NOT
/// transmitted (it would be per-card data for something the receiver can measure itself).
///
/// CARD FRONTS (user ruling 2026-08-08, "Die Oberseiten der Karten des remote Spielers soll auch
/// überall sichtbar sein … NUR in der Auswahlphase sieht man überall nur die Rückseiten"): this arc
/// used to be BACKS UNCONDITIONALLY. That made the secrecy rule a PLACE rule, in direct conflict with
/// the PHASE rule the round-card slots and the hand fan already obeyed. Each slab now carries the REAL
/// item card face whenever <see cref="RevealGate.ShowRoundCardFronts"/> is open for the displayed
/// actor, drawn by <see cref="RemotePileFronts"/> from that peer's own host-replicated
/// <c>CInventory.AllItems</c> — the identical list the LOCAL <see cref="ItemsPile"/> reads. Zero new
/// wire bytes: the item identities were already on this client, they were simply never drawn.
///
/// PLACEMENT mirrors the local fan's two modes: HAND-HELD → floating a palm standoff above the
/// sender's DOMINANT hand (the hand that pinch-grabbed the item stack); BOARD-ANCHORED → floating
/// above the sender's synced control board at the same shared board-top spot the local fan uses.
/// Faces away from the owner's head so the owner's side reads as the "front" and everyone else sees
/// backs — the same convention as every other remote card visual.
/// </summary>
/// <remarks>CLASSIFICATION: VR-ONLY (the fan's existence, size and placement) + PER-ACTOR MODEL (its
/// card faces) — costs wire bytes: extras <c>FlagItemFan</c> + one count byte, plus the two pure flags
/// <c>FlagItemFanHeld</c> / <c>FlagItemFanLeft</c> (0 B each). Item IDENTITY and per-item face size
/// are DELIBERATELY-NOT transmitted; the FACES are resolved locally off the host-replicated
/// <c>Inventory.AllItems</c> behind <see cref="RevealGate"/> (<see cref="RemotePileFronts"/>). The
/// item's real effect syncs authoritatively through <c>UseItemService</c>, not through here. See
/// INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal sealed class RemoteItemFan
{
    // ---- geometry (mirror of ItemsPile's arc constants) --------------------------------------
    private const int MaxCards = 12;
    private const float CardW = 0.075f;                  // item cards read near-square…
    private const float CardH = CardW * 1.15f;           // …so this is NOT the 88/63.5 ability ratio
    private const float Radius = 0.1792f * 1.7f;         // CardsConfig.FanEffectiveRadius × RadiusFactor
    private const float MaxArcDegrees = 110f;            // ItemsPile.MaxArcDegrees
    private const float MaxStepDegrees = 10f;            // ItemsPile.MaxStepDegrees
    private const float ZStagger = 0.004f;               // ItemsPile.ZStagger (draw order)
    private const float HandPalmOffset = 0.16f;          // ItemsPile.HandPalmOffset
    private const float BoardFloatHeight = 0.26f;        // ItemsPile.BoardFloatHeight
    private const float BoardFloatProudZ = -0.05f;       // ItemsPile.BoardFloatProudZ
    private const float Smoothing = 14f;

    private readonly RemoteAvatar _owner;
    private GameObject? _root;
    private readonly List<GameObject> _cards = new(MaxCards);

    /// <summary>The FRONT layer over those slabs (user ruling 2026-08-08) — one overlay per slab,
    /// gated on <see cref="RevealGate.ShowRoundCardFronts"/> and fed from the peer's own replicated
    /// inventory. Owned here, destroyed with the fan.</summary>
    private readonly RemotePileFronts _fronts;

    private Mesh? _mesh;
    private int _builtCount = -1;
    private bool _poseInit;
    private int _loggedCount = -1;
    private bool _loggedHeld;
    /// <summary>One-shot latch for the "hidden by the remote-board setting" line (see Tick).</summary>
    private bool _gateHiddenLogged;

    // ---- emerge / collapse (the peer-visible "Auf- und Zuklappen" of the item Fach) ------------
    // The local fan does BOTH: ItemsPile.EmergeAll seeds every chip ON the items stack at 0.35×
    // size and lets the chip's own home-glide fly it out into the arc, and ItemsPile.CollapseChips
    // glides every chip back INTO that stack over ItemChip.CollapseSeconds before it dies. This
    // ghost used to do neither properly — it spread from its own centre and then vanished instantly
    // on close — so a peer never saw the fan close AT ALL, it just blinked out.
    //
    // Driven, like RemoteBrowserFan's, off the RECEIVER'S state transition (count 0↔N) rather than
    // off an event: the receiver already knows the sender's board pose and therefore where their
    // ITEMS stack is (RemoteControlBoard.AnchorLocal), so both arcs replay locally for zero extra
    // wire bytes. Timings match the local ones so both players see the same motion.
    private float _emergeElapsed = -1f;
    private const float EmergeSharpness = 14f;        // [Cards] CardLerpSpeed default — the chip home-glide
    private const float EmergeSettleSeconds = 0.7f;
    private const float EmergeSeedScale = 0.35f;      // ItemChip.BeginEmerge's seed size
    private const float CollapseSeconds = 0.26f;      // ItemsPile.ItemChip.CollapseSeconds

    private float _collapseElapsed = -1f;
    private Vector3 _collapseTo;
    private readonly List<Vector3> _collapseFrom = new(MaxCards);

    public RemoteItemFan(RemoteAvatar owner)
    {
        _owner = owner;
        _fronts = new RemotePileFronts(owner, "item fan");
    }

    public void Tick(float dt)
    {
        dt = Mathf.Max(dt, 0f);

        // A collapse (the fan closing) runs to completion on its own — the count already went to 0,
        // so this is the only thing keeping the chips on screen.
        if (_collapseElapsed >= 0f)
        {
            TickCollapse(dt);
            return;
        }

        int count = Mathf.Clamp(_owner.ItemCardCount, 0, MaxCards);
        if (count == 0)
        {
            // Close edge: prefer the collapse-into-the-stack glide; BeginCollapse returns false when
            // there is nothing up or no stack to aim at, and only then do we blink out as before.
            if (!BeginCollapse())
                Hide();
            return;
        }

        // VISIBILITY ([Net] RemoteBoards — audit 2026-07). A BOARD-ANCHORED item fan is part of the
        // peer's board: it floats at a fixed board-local spot above their control board and emerges
        // out of that board's items stack. Before this check it ignored the setting entirely, so a
        // player who had chosen "Aus" — or "Aktionsphase" during the secret selection phase — saw a
        // near-square arc of card backs blooming in empty air exactly where the board they had asked
        // NOT to see would have been. It obeys the shared gate now. A HAND-HELD fan is avatar
        // content (it rides the sender's palm) and is deliberately left alone: the setting governs
        // boards, not hands.
        // Hidden INSTANTLY rather than collapsed: the collapse animation flies the chips into the
        // board's items stack, and that stack is exactly what the gate just hid — an arc gliding
        // into nothing is worse than the fan simply not being there. The next time the gate opens
        // with the fan still up, _root is inactive, so the normal emerge-out-of-the-stack runs.
        if (!_owner.ItemFanHeld && !RemoteBoardGate.ShowBoardSurface(_owner))
        {
            if (!_gateHiddenLogged)
            {
                _gateHiddenLogged = true;
                VRLog.Info("Net", $"Remote ITEM fan [player {_owner.PlayerId}]: board-anchored fan " +
                                  $"HIDDEN by [Net] RemoteBoards = {RemoteBoardGate.Mode} " +
                                  "(it belongs to that peer's board, which this client is not drawing).");
            }
            Hide();
            return;
        }
        _gateHiddenLogged = false;

        if (!TryResolvePose(out Vector3 target, out Quaternion rot))
        {
            Hide(); // holding hand not tracked / no board pose — no anchor, so nothing to show
            return;
        }

        EnsureRoot();
        if (_root == null)
            return;
        if (count != _builtCount)
            Rebuild(count);
        // Scale lives on the root (the fan is NOT parented under a scaled holder) and re-applies
        // every frame (one compare). It reproduces WHICHEVER transform the LOCAL fan hangs under
        // — the same rule RemoteBrowserFan documents: a HELD fan rides the palm (rig scale), a
        // BOARD-ANCHORED one is a child of the board root (ItemsPile.Open) and wears the BOARD
        // scale. The board branch used to apply the rig scale — the receiver-side fidelity bug
        // the old TryResolvePose note recorded as "needs a hardware round"; fixed in the 1:1
        // parity round (the board scales independently of world zoom, so the two routinely
        // differed and the peer's fan rendered the wrong size).
        float rootScale = _owner.ItemFanHeld
            ? _owner.AppliedScale
            : _owner.BoardScale > 0f ? _owner.BoardScale : 1f;
        if (!Mathf.Approximately(_root.transform.localScale.x, rootScale))
            _root.transform.localScale = Vector3.one * rootScale;
        if (!_root.activeSelf)
        {
            _root.SetActive(true);
            _poseInit = true;   // snap on the frame we appear, never ease in from a stale pose
            _root.transform.SetPositionAndRotation(target, rot);
            SeedEmerge();       // …and the chips fly OUT of the sender's ITEMS stack, like the local fan
        }

        Transform t = _root.transform;
        if (_poseInit)
        {
            _poseInit = false;
            t.SetPositionAndRotation(target, rot);
        }
        else
        {
            float k = 1f - Mathf.Exp(-Smoothing * Mathf.Max(dt, 0f));
            t.SetPositionAndRotation(Vector3.Lerp(t.position, target, k), Quaternion.Slerp(t.rotation, rot, k));
        }

        Layout(count, dt);

        // THE FRONT LAYER (user ruling 2026-08-08). After the layout, so a face is only ever asked for
        // on a slab that already sits where it belongs. The gate inside is evaluated every frame; the
        // model resolve behind it rides the board-content cadence — see RemotePileFronts.
        _fronts.Tick(RemotePileFronts.Content.Items);

        if (count != _loggedCount || _owner.ItemFanHeld != _loggedHeld)
        {
            _loggedCount = count;
            _loggedHeld = _owner.ItemFanHeld;
            VRLog.Info("Net", $"Remote ITEM fan [player {_owner.PlayerId}]: {count} item card(s), " +
                              $"{(_owner.ItemFanHeld ? $"hand-held above their {(_owner.ItemFanLeftHand ? "LEFT" : "RIGHT")} palm" : "anchored above their board")} " +
                              "— no item identity on the wire; whether the slabs show FRONTS or BACKS " +
                              "is stated separately by the \"Remote item fan faces\" line (RevealGate " +
                              "decides, per phase).");
        }
    }

    /// <summary>Where the fan sits this frame: above the sender's dominant palm when they hold it,
    /// otherwise at their synced board-local fan anchor (record 5; authored default for
    /// pre-record senders). False when neither reference exists yet.
    ///
    /// NEAR-TWIN OF <c>RemoteBrowserFan.TryResolveAnchor</c> — kept separate because the browse
    /// fan carries per-card enlargement and a root-scale out-param. Since the 1:1 parity round
    /// the SCALE rule finally agrees between the two: both reproduce whichever transform the
    /// LOCAL fan hangs under (held → rig scale, board-anchored → board scale; see the scale
    /// note in <see cref="Tick"/> — the old rig-scale-on-the-board-branch divergence was a
    /// receiver-side fidelity bug, now fixed).</summary>
    private bool TryResolvePose(out Vector3 pos, out Quaternion rot)
    {
        pos = default;
        rot = Quaternion.identity;
        float scale = _owner.AppliedScale;

        if (_owner.ItemFanHeld)
        {
            // The grabbing hand rides the wire as a flag (FlagItemFanLeft) — the owner may raise the
            // item fan with either hand, and guessing the dominant one put it on the wrong arm.
            Transform? holder = _owner.ItemFanLeftHand ? _owner.LeftHandHolder : _owner.RightHandHolder;
            if (holder == null || !holder.gameObject.activeInHierarchy)
                return false;
            pos = holder.position + holder.up * (HandPalmOffset * scale);
        }
        else
        {
            if (!_owner.HasBoard)
                return false;
            float bs = _owner.BoardScale > 0f ? _owner.BoardScale : 1f;
            // Defect 6 ("die Fächer sitzen nicht da, wo der Besitzer sie hat"): prefer the
            // SYNCED board-local anchor (extension record 5) — the owner's real fan spot
            // including their per-board [Cards] offsets. The authored default survives only
            // for pre-record senders / while the record is absent.
            Vector3 local = _owner.HasFanAnchor
                ? _owner.FanAnchorLocal
                : new Vector3(0f, PlayTray.BoardTopLocalY + BoardFloatHeight, BoardFloatProudZ);
            pos = _owner.BoardPosition + _owner.BoardRotation * (local * bs);
        }

        // Fronts (−Z) toward the owner, backs toward everyone else — the shared card convention.
        Transform? head = _owner.HeadHolder;
        if (head != null)
        {
            Vector3 away = pos - head.position;
            if (away.sqrMagnitude > 1e-6f)
                rot = Quaternion.LookRotation(away.normalized, Vector3.up);
        }
        return true;
    }

    /// <summary>Arc the slabs in fan-local space — the same reading arc <see cref="ItemsPile"/>
    /// lays its chips out on (capped sweep, capped per-card step, z-staggered for draw order),
    /// easing out of the <see cref="SeedEmerge"/> seed on the same exponential the local chips
    /// home-glide on.</summary>
    private void Layout(int n, float dt)
    {
        float step = n > 1 ? Mathf.Min(MaxStepDegrees, MaxArcDegrees / (n - 1)) : 0f;
        float start = -step * (n - 1) * 0.5f;

        bool easing = _emergeElapsed >= 0f;
        float k = 0f;
        if (easing)
        {
            _emergeElapsed += dt;
            k = 1f - Mathf.Exp(-EmergeSharpness * dt);
            if (_emergeElapsed >= EmergeSettleSeconds)
                _emergeElapsed = -1f; // settled: assert the slots exactly from here on
        }

        // WHICH ITEM CHIP THE OWNER IS SINGLING OUT (extension record 6, byte 1 — hardware MP
        // test 2026-08-04: "Das Highlighting der Karten in den Fächern ist nicht synchronisiert").
        // ROOT CAUSE of that report: the SENDER has fed the item fan's HighlightedIndex into the
        // card-highlight record since build 36 (NetAvatarDriver reads ItemsPile.HighlightedIndex
        // when no pile browser is open — the two board fans share wire byte 1 because at most one
        // is ever open), and RemoteBrowserFan renders it for the browse arc — but THIS renderer
        // never consumed the index, so a peer sweeping their equipped items showed a flat arc on
        // every other screen. Same fix shape as RemoteBrowserFan.Layout: clamp the synced index
        // against OUR live slab count (a packet can arrive a frame off the count it was measured
        // against) and lift that one slab.
        int hovered = _owner.FanHighlightIndex;
        if (hovered < 0 || hovered >= _cards.Count)
            hovered = -1;

        for (int i = 0; i < _cards.Count; i++)
        {
            float angle = start + step * i;
            float rad = angle * Mathf.Deg2Rad;
            var pos = new Vector3(Mathf.Sin(rad) * Radius, (Mathf.Cos(rad) - 1f) * Radius, -ZStagger * i);
            Quaternion rot = Quaternion.Euler(0f, 0f, -angle);
            // The POP, exactly as the owner's own chip applies it (ItemsPile.ItemChip: PopLift
            // toward the viewer along the chip's local −Z, ×PopScale enlargement, eased at
            // PopLerpSpeed): reproduced from the synced INDEX alone — the ramp runs on the LOCAL
            // clock, so the wire carries a position and never an animation. −Z is toward the
            // owner's head here (the fan billboards its back at everyone else), which matches the
            // local chip popping toward ITS viewer.
            float popT = PopAmount(i, hovered, dt);
            if (popT > 0f)
                pos += rot * new Vector3(0f, 0f, -PopLift * popT);
            float scale = 1f + (PopScale - 1f) * popT;
            Transform t = _cards[i].transform;
            if (easing)
            {
                t.localPosition = Vector3.Lerp(t.localPosition, pos, k);
                t.localRotation = Quaternion.Slerp(t.localRotation, rot, k);
                t.localScale = Vector3.Lerp(t.localScale, Vector3.one * scale, k);
            }
            else
            {
                t.localPosition = pos;
                t.localRotation = rot;
                t.localScale = Vector3.one * scale;
            }
        }
        LogHighlightIfChanged(hovered);
    }

    // ---------------------------------------------------------------- highlight (record 6) --

    /// <summary>ItemsPile.ItemChip.PopScale — the enlargement a lifted item chip takes locally
    /// (×1.18). Local copy of the authored value, like every geometry constant in this file.</summary>
    private const float PopScale = 1.18f;

    /// <summary>ItemsPile.ItemChip.PopLift — how far the lifted chip comes toward its viewer
    /// (local −Z, metres at chip scale 1).</summary>
    private const float PopLift = 0.02f;

    /// <summary>ItemsPile.ItemChip.PopLerpSpeed — the exponential ease rate of the local pop.</summary>
    private const float PopLerpSpeed = 16f;

    /// <summary>Per-slab pop ramp (0..1), index-aligned with <c>_cards</c> — per slab so a lift
    /// MOVING along the arc has the old chip relaxing while the new one rises, exactly like the
    /// owner's own sweep.</summary>
    private readonly List<float> _pop = new(MaxCards);

    /// <summary>Last highlighted index stated in the log (−2 = never).</summary>
    private int _loggedHighlight = -2;

    /// <summary>Advance and return slab <paramref name="i"/>'s pop ramp toward 1 while it is the
    /// highlighted chip and toward 0 otherwise, on the local chip's own exponential.</summary>
    private float PopAmount(int i, int hovered, float dt)
    {
        while (_pop.Count <= i)
            _pop.Add(0f);
        float target = i == hovered ? 1f : 0f;
        float t = Mathf.Lerp(_pop[i], target, 1f - Mathf.Exp(-PopLerpSpeed * Mathf.Max(dt, 0f)));
        // Snap the tail so a settled ramp stops writing transforms (the exponential never quite
        // arrives on its own).
        if (Mathf.Abs(t - target) < 0.005f)
            t = target;
        _pop[i] = t;
        return t;
    }

    /// <summary>Drop every pop ramp (fan closed / rebuilt) so a re-opened fan never starts with a
    /// stale chip already lifted.</summary>
    private void ClearPops()
    {
        for (int i = 0; i < _pop.Count; i++)
            _pop[i] = 0f;
        _loggedHighlight = -2;
    }

    /// <summary>Change-gated evidence that the synced item-fan highlight reached the render path
    /// (grep: "Remote item fan highlight").</summary>
    private void LogHighlightIfChanged(int hovered)
    {
        if (hovered == _loggedHighlight)
            return;
        _loggedHighlight = hovered;
        VRLog.Info("Net", $"Remote item fan highlight [{_owner.PlayerId}]: " +
                          $"index {(hovered >= 0 ? hovered.ToString() : "none")} of " +
                          $"{_cards.Count} slab(s) (wire index {_owner.FanHighlightIndex}) — " +
                          "the chip lifts on ItemsPile's own pop, from the INDEX alone " +
                          "(extension record 6: no item identity).");
    }

    // ------------------------------------------------------------------ emerge / collapse --

    /// <summary>
    /// Seed every chip ON the sender's ITEMS stack at <see cref="EmergeSeedScale"/> size so the ease
    /// in <see cref="Layout"/> flies them OUT of the pile — the wire-free replay of
    /// <c>ItemsPile.EmergeAll</c> + <c>ItemChip.BeginEmerge</c>. Falls back to the fan centre when
    /// the sender's board pose is unknown, so the worst case is a spread-open, never a pop-in.
    /// </summary>
    private void SeedEmerge()
    {
        if (_root == null)
            return;
        Vector3 seedLocal = Vector3.zero;
        if (TryItemStackWorld(out Vector3 stackWorld))
            seedLocal = _root.transform.InverseTransformPoint(stackWorld);
        for (int i = 0; i < _cards.Count; i++)
        {
            Transform t = _cards[i].transform;
            t.localPosition = seedLocal + new Vector3(0f, 0f, -ZStagger * i); // keep the draw order stable
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one * EmergeSeedScale;
        }
        _emergeElapsed = 0f;
    }

    /// <summary>
    /// Close edge: glide every chip back INTO the sender's items stack over
    /// <see cref="CollapseSeconds"/> instead of blinking the fan out — the replay of
    /// <c>ItemsPile.CollapseChips</c>. The root pose is frozen for the duration and the chips are
    /// driven in WORLD space, mirroring how the local chips are re-parented out of the fan root
    /// before they glide. Returns false (caller hides instantly) when there is nothing to collapse
    /// or no stack to collapse into.
    /// </summary>
    private bool BeginCollapse()
    {
        if (_root == null || !_root.activeSelf || _cards.Count == 0)
            return false;
        if (!TryItemStackWorld(out Vector3 stackWorld))
            return false;

        _emergeElapsed = -1f;
        _collapseTo = stackWorld;
        _collapseFrom.Clear();
        for (int i = 0; i < _cards.Count; i++)
            _collapseFrom.Add(_cards[i].transform.position);
        _collapseElapsed = 0f;

        VRLog.Info("Net", $"Remote ITEM fan [player {_owner.PlayerId}]: closing — {_cards.Count} item card(s) " +
                          $"collapse back into their items stack ({CollapseSeconds:F2}s), matching the local fan.");
        _loggedCount = 0; // Hide's own "closed" line is redundant with this one
        return true;
    }

    private void TickCollapse(float dt)
    {
        _collapseElapsed += dt;
        float u = Mathf.Clamp01(_collapseElapsed / CollapseSeconds);
        float e = u * u * (3f - 2f * u);
        for (int i = 0; i < _cards.Count && i < _collapseFrom.Count; i++)
        {
            Transform t = _cards[i].transform;
            t.position = Vector3.Lerp(_collapseFrom[i], _collapseTo, e);
            t.localScale = Vector3.one * Mathf.Lerp(1f, EmergeSeedScale, e);
        }
        if (u < 1f)
            return;
        _collapseElapsed = -1f;
        Hide();
    }

    /// <summary>World position of the sender's ITEMS stack, resolved through the SHARED board-local
    /// stack layout against their own synced board pose — the point the chips emerge from and
    /// collapse into, wherever that player parked their board.</summary>
    private bool TryItemStackWorld(out Vector3 world)
    {
        world = default;
        if (!_owner.HasBoard)
            return false;
        float bs = _owner.BoardScale > 0f ? _owner.BoardScale : 1f;
        world = _owner.BoardPosition
                + _owner.BoardRotation * (_owner.BoardAnchorLocal(CardFxAnchor.Items) * bs);
        return true;
    }

    private void EnsureRoot()
    {
        if (_root != null)
            return;
        _root = new GameObject($"GloomhavenVR.RemoteItemFan[{_owner.PlayerId}]");
        Object.DontDestroyOnLoad(_root);
        _root.hideFlags = HideFlags.HideAndDontSave;
        // Sized by the sender's rig scale so the fan reads the same physical size as their hands.
        _root.transform.localScale = Vector3.one * _owner.AppliedScale;
        _root.SetActive(false);
        _mesh = RemoteHandFan.BuildBackSlab(CardW, CardH);
        VRLayers.Apply(_root);
    }

    private void Rebuild(int count)
    {
        for (int i = _cards.Count - 1; i >= 0; i--)
        {
            if (_cards[i] != null)
                Object.Destroy(_cards[i]);
        }
        _cards.Clear();
        _collapseFrom.Clear(); // parallel to _cards — never let it outlive the slabs it indexed
        ClearPops();           // index-aligned with _cards too — a rebuilt arc starts flat

        Material back = CardMesh.CreateBackMaterial(); // SHARED cache — never ours to destroy
        for (int i = 0; i < count; i++)
        {
            var card = new GameObject($"Item{i}");
            card.transform.SetParent(_root!.transform, worldPositionStays: false);
            var mf = card.AddComponent<MeshFilter>();
            mf.sharedMesh = _mesh;
            var mr = card.AddComponent<MeshRenderer>();
            mr.sharedMaterial = back;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _cards.Add(card);
        }
        _builtCount = count;
        VRLayers.Apply(_root!);
        // Re-bind the front overlays onto the NEW slabs (the old ones died with their hosts above).
        // The card size handed over is the UNSCALED slab size: the overlay is a child of the slab, so
        // it inherits the emerge seed scale and the pop enlargement for free, like the slab's own mesh.
        _fronts.Rebuild(_cards, CardW, CardH);
    }

    private void Hide()
    {
        _fronts.HideAll(); // a hidden fan keeps no game-widget clones alive
        if (_loggedCount > 0)
        {
            _loggedCount = 0;
            VRLog.Info("Net", $"Remote ITEM fan [player {_owner.PlayerId}]: closed.");
        }
        if (_root != null && _root.activeSelf)
            _root.SetActive(false);
        _emergeElapsed = -1f;   // next appearance emerges out of the stack again
        _collapseElapsed = -1f;
        _collapseFrom.Clear();
        ClearPops();            // a re-opened fan never starts with a stale chip lifted
    }

    public void Destroy()
    {
        _fronts.Destroy();
        _cards.Clear();
        _collapseFrom.Clear();
        _builtCount = -1;
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
