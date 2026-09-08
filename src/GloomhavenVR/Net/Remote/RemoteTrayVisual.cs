using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// The REAL 3D control-board asset on a peer's remote board — the same bundled prefab
/// (<c>PlayTray.prefab</c> / its Steel / Bronze variants) the owning player's own
/// <c>PlayTray.EnsureBuilt</c> instantiates, selected by the peer's SYNCED style
/// (<c>RemoteAvatar.BoardStyle</c>, extras byte A bits 5..6) and cloned under the remote board
/// root. This replaces the old flat "Frame" quad — the "komisch 2D" look — with the identical
/// mesh, materials (bundled BoardLit, scene-light independent) and slot recesses the owner sees.
///
/// FAITHFUL BY CONSTRUCTION, in the same three steps the local build takes:
///   1. the prefab is instantiated with an IDENTITY local pose under the board root — the wire
///      pose IS <c>PlayTray.Current.Root</c>'s world pose (<c>NetAvatarDriver</c> samples exactly
///      that transform), so the clone lands where the owner's board physically is;
///   2. the bundle anchors — the four frame ones (<c>Slot1/Slot2/ShortRestToken/LongRestToken</c>)
///      plus the board's BUTTON SEATS, resolved through <c>Cards.BoardAnchors</c> so that both the
///      new <c>ButtonSeat1/2/3</c> spelling and the legacy <c>ConfirmButton/UndoButton</c> one
///      resolve here EXACTLY as they do on the owner's own board — carry the FBX export's 90°
///      twist and are re-aligned to the ONE board face
///      frame derived from their own positions — verbatim the anchor math in
///      <c>PlayTray.EnsureBuilt</c> (−Z out of the decorated face, +Y up the short axis) — so
///      cards and caps parented on them face the viewer exactly like the owner's do;
///   3. the peer's own per-board MESH POSE (AssetOffset / AssetPitch / Yaw / Roll) IS applied,
///      through the same algorithm the local board uses: the offset + euler move the MESH inside
///      the board root while every anchor is pinned back to the root-local pose it held
///      before, so slots, rest tokens and Confirm/Undo — and everything docked on them — stay
///      exactly where they were and only the slab moves. Their values ride extension record 28
///      when the owner has moved a dial and are this client's shipped constant when they have
///      not; the bronze board's shipped AssetOffset (0, −0.11, 0.08) and 57° pitch mean the
///      mirror now reproduces even the UNTUNED bronze mesh pose, which the old
///      DELIBERATELY-NOT rule dropped.
///
/// INERT: every <c>Collider</c> (the bundled board ships a real MeshCollider the LOCAL board
/// registers as its laser surface) and any <c>Rigidbody</c> is destroyed at clone time,
/// SILENTLY — they are expected cargo of the prefab, not a leak — before the board's loud
/// <see cref="RemoteBoardFurniture.StripColliders"/> guard ever runs. Nothing is registered
/// anywhere; a peer's board remains a pure display.
///
/// Returns null while the original bundle is unavailable or its required anchors are missing.
/// The caller retries through the shared original-asset loader before presenting a native board.
/// </summary>
/// <remarks>CLASSIFICATION: VR-ONLY input, zero NEW wire — rendered entirely from fields that
/// already ride the wire (board pose/scale + style code). See INVARIANTS-Net-Rig.md
/// "Net — content classification".</remarks>
internal sealed class RemoteTrayVisual
{
    /// <summary>The style this visual was built from (the peer's synced choice). A mismatch with
    /// the live <c>RemoteAvatar.BoardStyle</c> makes <see cref="RemoteControlBoard"/> rebuild.</summary>
    public ControlBoard Style { get; }

    /// <summary>The instantiated prefab clone (child of the board root).</summary>
    public Transform Root { get; }

    private readonly Transform[] _slots = new Transform[2];
    private readonly Vector3[] _slotLocal = new Vector3[2];

    /// <summary>The board's BUTTON SEATS by index, resolved through <c>Cards.BoardAnchors</c> —
    /// the same table, the same alias order and therefore the same answer the owner's
    /// <c>PlayTray.EnsureBuilt</c> got. A null entry means this board has no such recess (seat 2 on
    /// every board shipped so far), and the furniture builder falls back to its authored mount
    /// exactly as it always has for an anchor-less board.</summary>
    private readonly Transform?[] _buttonSeats = new Transform?[Cards.BoardAnchors.ButtonSeatCount];

    /// <summary>The re-aligned anchor of button seat <paramref name="seat"/>, or null when this
    /// board has no such recess.</summary>
    public Transform? SeatAnchor(int seat) =>
        seat >= 0 && seat < _buttonSeats.Length ? _buttonSeats[seat] : null;

    /// <summary>Seat 0 — the commit seat (CONFIRM and the item "Use" cap share it, as on the
    /// owner's board). Named view onto <see cref="SeatAnchor"/> so the furniture builder reads the
    /// same word it always did.</summary>
    public Transform? ConfirmAnchor => SeatAnchor(0);

    /// <summary>Seat 1 — the UNDO seat.</summary>
    public Transform? UndoAnchor => SeatAnchor(1);

    /// <summary>Seat 2 — the turn-flow SKIP seat. It has an occupant since 2026-08-25: the skip cap
    /// left <c>WorldUI.ButtonCluster</c>'s own column and became a generic keycap here, exactly as
    /// it did on the owner's board.</summary>
    public Transform? SkipAnchor => SeatAnchor(2);

    /// <summary>
    /// This board's own button-recess PITCH — the step down the stack from one seat anchor to the
    /// next, in the anchors' own frame — or null when the prefab supplies fewer than two seats.
    ///
    /// <para>THE OTHER HALF OF <see cref="SeatMinHalf"/>'S ARGUMENT, and it needs no wire field for
    /// the same reason: this peer cloned the SAME prefab out of the SAME bundle, so it measures the
    /// identical pitch, and <c>Cards.BoardAnchors.StackDelta</c> is one function called on both
    /// sides. What DOES ride the wire is the owner's dimensionless spacing multiplier
    /// (<c>[Cards] ButtonStackSpacing</c>, record 28); the per-board number it multiplies is
    /// measured, not sent — which is exactly what lets one shared dial be correct on three boards
    /// whose recess pitches differ by 8 %.</para>
    /// </summary>
    public float? SeatPitch => Cards.BoardAnchors.StackPitch(_buttonSeats);

    /// <summary>
    /// The TIGHTEST button-seat recess this board carries (smallest half-extent over its seats, in
    /// board metres), or null when the prefab has no <c>SeatExtent1/2/3</c> measurement.
    ///
    /// <para>THIS IS WHY THE FITTED CAP SIZE NEEDS NO WIRE FIELD. The peer clones the SAME prefab
    /// out of the SAME bundle the owner loaded, so it measures the identical recess and
    /// <c>Cards.BoardAnchors.FitCapSize</c> — one function, called on both sides — returns the
    /// identical cap. The size is DERIVED on every client from data every client already has,
    /// exactly the way the seat POSES are derived from the synced offset/spacing dials. A peer on an
    /// older bundle measures nothing, gets null, and draws the tuned size — which is also what its
    /// own local board would draw, so the two stay consistent with each other.</para>
    /// </summary>
    public Vector2? SeatMinHalf { get; private set; }

    /// <summary>The tighter of the board's two authored REST PADS (smallest half-extent over both), or
    /// null when the prefab carries no <c>RestExtentShort/Long</c> measurement. Same mechanism and
    /// same purpose as <see cref="SeatMinHalf"/>: the mirrored rest discs are fitted and clamped to
    /// the pad exactly as the owner's are, derived locally, no wire field.</summary>
    public Vector2? RestMinHalf { get; private set; }

    public Transform? ShortRestAnchor { get; private set; }
    public Transform? LongRestAnchor { get; private set; }

    /// <summary>The re-aligned recess anchor for round-card slot <paramref name="i"/> (0/1).</summary>
    public Transform SlotAnchor(int i) => _slots[i];

    /// <summary>Board-root-local position of slot <paramref name="i"/> — the live layout the
    /// card-FX flights and furniture overlays must agree with (the recesses of the three boards
    /// are NOT at the old hardcoded offsets).</summary>
    public Vector3 SlotLocal(int i) => _slotLocal[i];

    private RemoteTrayVisual(ControlBoard style, Transform root)
    {
        Style = style;
        Root = root;
    }

    /// <summary>True when the REAL tray prefab could be built right now (bundle resident) —
    /// polled by <see cref="RemoteControlBoard"/> while original-asset construction is pending.</summary>
    public static bool PrefabAvailable(ControlBoard style) =>
        VRCardFactory.PeekTrayPrefab(style) != null;

    /// <summary>
    /// Clone the real board asset for <c>style</c> under <paramref name="boardRoot"/>
    /// and align its anchors. Null keeps native construction pending while the shared loader
    /// recovers a missing bundle or an installation supplies the required original anchor set.
    /// </summary>
    public static RemoteTrayVisual? Build(Transform boardRoot, in RemoteBoardTuning tuning)
    {
        ControlBoard style = tuning.Style;
        GameObject? prefab = VRCardFactory.PeekTrayPrefab(style);
        if (prefab == null)
            return null;

        GameObject go = Object.Instantiate(prefab, boardRoot, false);
        go.name = "TrayVisual";

        // Inertness first (silent — see the class note): the bundled MeshCollider is the LOCAL
        // board's laser surface and must not exist on a display copy. DestroyIMMEDIATE, not
        // Destroy: the board's loud StripColliders guard sweeps the whole root later THIS frame,
        // and a deferred destroy would still be visible to it — the expected prefab cargo would
        // then trip the warning that exists to catch genuine leaks.
        foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
            Object.DestroyImmediate(c);
        foreach (Rigidbody rb in go.GetComponentsInChildren<Rigidbody>(true))
            Object.DestroyImmediate(rb);

        var visual = new RemoteTrayVisual(style, go.transform);
        visual._slots[0] = FindDeep(go.transform, "Slot1")!;
        visual._slots[1] = FindDeep(go.transform, "Slot2")!;
        visual.ShortRestAnchor = FindDeep(go.transform, "ShortRestToken");
        visual.LongRestAnchor = FindDeep(go.transform, "LongRestToken");
        // THE SEATS GO THROUGH THE SHARED TABLE, and that is what keeps the mirror 1:1 across an
        // asset regeneration. These two lines used to read "ConfirmButton"/"UndoButton" literally.
        // The instant the asset lane emitted a board with the new ButtonSeat1/2/3 spelling, the
        // OWNER's board would have seated its keycaps in the new recesses (PlayTray resolves both)
        // while every PEER resolved null here and fell back to the hardcoded Oak ConfirmMount /
        // UndoMount — a divergence in the picture on the exact control the 1:1 rule is about, and
        // one no wire field could have repaired because the seat is DERIVED on each client.
        int seatsFound = Cards.BoardAnchors.ResolveSeats(go.transform, visual._buttonSeats);
        for (int i = 0; i < Cards.BoardAnchors.ButtonSeatCount; i++)
        {
            Vector2? e = Cards.BoardAnchors.SeatExtent(go.transform, i);
            if (e == null)
                continue;
            visual.SeatMinHalf = visual.SeatMinHalf == null
                ? e
                : new Vector2(Mathf.Min(visual.SeatMinHalf.Value.x, e.Value.x),
                              Mathf.Min(visual.SeatMinHalf.Value.y, e.Value.y));
        }
        foreach (bool shortRest in new[] { true, false })
        {
            Vector2? e = Cards.BoardAnchors.RestExtent(go.transform, shortRest);
            if (e == null)
                continue;
            visual.RestMinHalf = visual.RestMinHalf == null
                ? e
                : new Vector2(Mathf.Min(visual.RestMinHalf.Value.x, e.Value.x),
                              Mathf.Min(visual.RestMinHalf.Value.y, e.Value.y));
        }

        if (visual._slots[0] == null || visual._slots[1] == null
            || visual.ShortRestAnchor == null || visual.LongRestAnchor == null)
        {
            VRLog.Warn("Net", $"Remote tray prefab '{style}' lacks its anchor set — native " +
                              "board construction remains pending for this peer.");
            Object.Destroy(go);
            return null;
        }

        // Anchor re-alignment — verbatim PlayTray.EnsureBuilt: one face frame from the anchors'
        // own positions (-Z out of the decorated face toward the viewer, +Y up the short axis).
        Vector3 uF = (visual._slots[1].position - visual._slots[0].position).normalized;
        Vector3 sF = (visual.ShortRestAnchor!.position - visual.LongRestAnchor!.position).normalized;
        Vector3 nF = Vector3.Cross(uF, sF).normalized;
        Vector3 vF = Vector3.Cross(nF, uF).normalized;
        Quaternion faceWorld = Quaternion.LookRotation(nF, vF);
        Transform?[] anchors = visual.AllAnchors();
        foreach (Transform? a in anchors)
        {
            if (a != null)
                a.rotation = faceWorld;
        }

        // THE MESH POSE (extension record 28 / the shipped per-board default) — verbatim the
        // local PlayTray algorithm: capture the anchors' board-root-local poses while the mesh is
        // untouched, move the mesh, then write the anchors back. Without the pinning the anchors
        // would ride the mesh and drag every docked element with them, which is the opposite of
        // what the dial does on the owner's own board. The bronze board reaches this even untuned.
        Vector3 assetOffset = tuning.AssetOffset;
        var assetEuler = new Vector3(tuning.AssetPitchDegrees, tuning.AssetYawDegrees,
                                     tuning.AssetRollDegrees);
        // BOUNDED THE SAME WAY THE OWNER BOUNDS IT, from the same terms: the tuned pose arrives on
        // extension record 28 and the lever arm is read off this peer's own clone of the same
        // prefab, so no wire field is added and the two clients cannot disagree. The gate is this
        // board's measured recess — an old-bundle peer has none, is unbounded, and draws exactly
        // what it drew before. Cards.BoardAnchors.ClampAssetPose carries the argument and the
        // measurement.
        float? assetRadius = visual.SeatMinHalf == null
                             ? (float?)null
                             : Cards.BoardAnchors.AnchorRadius(boardRoot, anchors);
        Vector3 reqOffset = assetOffset, reqEuler = assetEuler;
        Cards.BoardAnchors.ClampAssetPose(ref assetOffset, ref assetEuler, assetRadius);
        if (Cards.BoardAnchors.AssetPoseWasClamped(reqOffset, reqEuler, assetOffset, assetEuler))
            VRLog.Info("Net", $"Remote '{style}' board mesh pose CLAMPED: owner's offset " +
                              $"{reqOffset:F3} / euler {reqEuler:F1}° would have walked the mesh out " +
                              $"from under the pinned control set; applied {assetOffset:F3} / " +
                              $"{assetEuler:F1}° — the owner clamps to the same numbers, so the two " +
                              "pictures stay 1:1.");
        if (assetOffset != Vector3.zero || assetEuler != Vector3.zero)
        {
            Transform?[] pinned = anchors;
            var pinPos = new Vector3[pinned.Length];
            var pinRot = new Quaternion[pinned.Length];
            for (int i = 0; i < pinned.Length; i++)
            {
                if (pinned[i] == null)
                    continue;
                pinPos[i] = boardRoot.InverseTransformPoint(pinned[i]!.position);
                pinRot[i] = Quaternion.Inverse(boardRoot.rotation) * pinned[i]!.rotation;
            }
            go.transform.localPosition += assetOffset;
            go.transform.localRotation = Quaternion.Euler(assetEuler) * go.transform.localRotation;
            for (int i = 0; i < pinned.Length; i++)
            {
                if (pinned[i] == null)
                    continue;
                pinned[i]!.position = boardRoot.TransformPoint(pinPos[i]);
                pinned[i]!.rotation = boardRoot.rotation * pinRot[i];
            }
        }

        for (int i = 0; i < 2; i++)
            visual._slotLocal[i] = boardRoot.InverseTransformPoint(visual._slots[i].position);

        VRLog.Info("Net", $"Remote tray visual built from the REAL '{style}' board prefab " +
                          $"('{VRCardFactory.TrayPrefabPath(style)}') — slots board-local " +
                          $"{visual._slotLocal[0]} / {visual._slotLocal[1]}, mesh pose offset " +
                          $"{assetOffset:F3} / euler {assetEuler:F1}° (the owner's own, extension " +
                          "record 28 where they tuned it), anchors pinned back so nothing docked " +
                          $"moved, {seatsFound} of {Cards.BoardAnchors.ButtonSeatCount} button seats " +
                          "supplied by the asset, tightest recess " +
                          (visual.SeatMinHalf != null
                              ? $"{visual.SeatMinHalf.Value.x * 2000f:F1} × {visual.SeatMinHalf.Value.y * 2000f:F1} mm"
                              : "not measured (older bundle)") +
                          ", all colliders stripped (pure display).");
        return visual;
    }

    /// <summary>
    /// EVERY anchor of this clone — the four frame anchors plus whichever button seats the board
    /// supplies — as one array. The face-frame re-alignment and the mesh-pose pin both walk it, so
    /// they cannot disagree about the set: an anchor that is rotated but not pinned drifts with the
    /// mesh the moment the owner touches an AssetOffset dial, and a seat that is pinned but not
    /// rotated seats its cap edge-on.
    /// </summary>
    private Transform?[] AllAnchors()
    {
        var all = new Transform?[4 + _buttonSeats.Length];
        all[0] = _slots[0];
        all[1] = _slots[1];
        all[2] = ShortRestAnchor;
        all[3] = LongRestAnchor;
        for (int i = 0; i < _buttonSeats.Length; i++)
            all[4 + i] = _buttonSeats[i];
        return all;
    }

    /// <summary>Depth-first name lookup — the shared one, so the peer's copy of a board walks it
    /// exactly as the owner's does.</summary>
    private static Transform? FindDeep(Transform root, string name) =>
        Cards.BoardAnchors.FindDeep(root, name);
}
