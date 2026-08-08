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
///   2. the six bundle anchors (<c>Slot1/Slot2/ShortRestToken/LongRestToken/ConfirmButton/
///      UndoButton</c>) carry the FBX export's 90° twist and are re-aligned to the ONE board face
///      frame derived from their own positions — verbatim the anchor math in
///      <c>PlayTray.EnsureBuilt</c> (−Z out of the decorated face, +Y up the short axis) — so
///      cards and caps parented on them face the viewer exactly like the owner's do;
///   3. the peer's own per-board MESH POSE (AssetOffset / AssetPitch / Yaw / Roll) IS applied,
///      through the same algorithm the local board uses: the offset + euler move the MESH inside
///      the board root while the six anchors are pinned back to the root-local poses they held
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
/// Null (procedural fallback: the old flat frame quad) when the bundle is not resident yet or
/// the prefab lacks its anchors — the same degradation ladder the local board has.
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

    public Transform? ConfirmAnchor { get; private set; }
    public Transform? UndoAnchor { get; private set; }
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
    /// polled by <see cref="RemoteControlBoard"/> so a board that came up on the procedural
    /// fallback (bundle not loaded yet) upgrades itself once the asset arrives.</summary>
    public static bool PrefabAvailable(ControlBoard style) =>
        VRCardFactory.PeekTrayPrefab(style) != null;

    /// <summary>
    /// Clone the real board asset for <paramref name="style"/> under <paramref name="boardRoot"/>
    /// and align its anchors. Null → caller keeps the flat-quad fallback (bundle absent, or an
    /// old bundle whose prefab lacks the anchor set — then a mis-aligned mesh would be worse
    /// than the honest fallback).
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
        visual.ConfirmAnchor = FindDeep(go.transform, "ConfirmButton");
        visual.UndoAnchor = FindDeep(go.transform, "UndoButton");

        if (visual._slots[0] == null || visual._slots[1] == null
            || visual.ShortRestAnchor == null || visual.LongRestAnchor == null)
        {
            VRLog.Warn("Net", $"Remote tray prefab '{style}' lacks its anchor set — keeping the " +
                              "flat fallback board for this peer.");
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
        foreach (Transform? a in new[] { visual._slots[0], visual._slots[1], visual.ShortRestAnchor,
                                         visual.LongRestAnchor, visual.ConfirmAnchor, visual.UndoAnchor })
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
        if (assetOffset != Vector3.zero || assetEuler != Vector3.zero)
        {
            Transform[] pinned = { visual._slots[0], visual._slots[1], visual.ShortRestAnchor!,
                                   visual.LongRestAnchor!, visual.ConfirmAnchor!, visual.UndoAnchor! };
            var pinPos = new Vector3[pinned.Length];
            var pinRot = new Quaternion[pinned.Length];
            for (int i = 0; i < pinned.Length; i++)
            {
                if (pinned[i] == null)
                    continue;
                pinPos[i] = boardRoot.InverseTransformPoint(pinned[i].position);
                pinRot[i] = Quaternion.Inverse(boardRoot.rotation) * pinned[i].rotation;
            }
            go.transform.localPosition += assetOffset;
            go.transform.localRotation = Quaternion.Euler(assetEuler) * go.transform.localRotation;
            for (int i = 0; i < pinned.Length; i++)
            {
                if (pinned[i] == null)
                    continue;
                pinned[i].position = boardRoot.TransformPoint(pinPos[i]);
                pinned[i].rotation = boardRoot.rotation * pinRot[i];
            }
        }

        for (int i = 0; i < 2; i++)
            visual._slotLocal[i] = boardRoot.InverseTransformPoint(visual._slots[i].position);

        VRLog.Info("Net", $"Remote tray visual built from the REAL '{style}' board prefab " +
                          $"('{VRCardFactory.TrayPrefabPath(style)}') — slots board-local " +
                          $"{visual._slotLocal[0]} / {visual._slotLocal[1]}, mesh pose offset " +
                          $"{assetOffset:F3} / euler {assetEuler:F1}° (the owner's own, extension " +
                          "record 28 where they tuned it), anchors pinned back so nothing docked " +
                          "moved, all colliders stripped (pure display).");
        return visual;
    }

    private static Transform? FindDeep(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform? found = FindDeep(root.GetChild(i), name);
            if (found != null)
                return found;
        }
        return null;
    }
}
