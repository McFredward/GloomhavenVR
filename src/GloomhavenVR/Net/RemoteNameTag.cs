using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// Floating identity tag above a remote VR player's head mask: their (bad-word-masked) username
/// next to their Steam avatar picture, billboarded toward the local head every frame — the same
/// row layout, unlit materials and facing convention as <see cref="OwnerTag"/>, which pins the
/// identical pair to a corner of the peer's control board. That tag answers "whose board is
/// this"; this one answers "who is this person" when you look at their avatar, board or no board.
///
/// POSITION IS THE MASK'S RENDERED BOUNDS, NOT A CONSTANT RISE: every tick the tag reads the
/// world-space AABBs of the mask renderer(s) under the head holder and floats a small margin
/// above their top (<see cref="ClearanceAboveMask"/> × <see cref="RemoteAvatar.AppliedScale"/>,
/// plus the tag's own half height), centred on the bounds. Bounds already contain everything
/// that moved the old constant-rise formula off target on hardware — the mask STYLE's authored
/// height (Mask_0..2 or the placeholder), the sender's <see cref="RemoteAvatar.MaskSize"/>
/// stepper, the sender-zoom holder scale AND the head's current rotation — so the tag hugs the
/// mask with no per-mask magic numbers. Top = max world Y: world up is the LOCAL viewer's up,
/// the same axis the billboard uses, so "above" always reads correct to the viewer. The old
/// holder-relative rise survives only as the fallback for the frame(s) before any mask renderer
/// exists.
///
/// CENTRED ON MEASURED INK, NOT ON A FIXED BOX: the row's width is avatar + pad + the name's
/// RENDERED glyph width (measured once per identity change in <see cref="Rebuild"/>), so the group
/// straddles the anchor evenly at any name length, with or without an avatar, at any world scale.
/// Centring the avatar plus the fixed <see cref="NameWidth"/> container instead — the old layout —
/// pushed every short name's visible ink left of the mask by half the container's unused tail.
///
/// SCALE FOLLOWS THE SENDER'S ZOOM: the tag's visual size uses
/// <see cref="RemoteAvatar.AppliedScale"/> — the sender's rig <c>WorldScale</c> from the rig
/// packet, the EXACT factor <c>RemoteAvatar.SetTarget</c> writes onto the head/hand holders — so
/// when a peer zooms their world and their avatar shrinks or grows on our screen, the tag stays
/// visually attached and proportionate instead of dwarfing a tiny head. Clamped at
/// <see cref="MinScale"/> so a far-zoomed-in peer keeps a readable label (see the constant's
/// remark for the trade-off).
///
/// Toggleable LIVE via <c>[Net] NameTags</c>: the gate is re-read every tick, so flipping the
/// config (or its curated options row) hides/shows all tags without a restart or a rebuild.
/// </summary>
/// <remarks>CLASSIFICATION: PER-ACTOR MODEL — ZERO wire on OUR side channel. Username and Steam
/// avatar are read out of the GAME's own netcode by reflection (<see cref="NetPlayerActors"/>:
/// <c>FFSNet.NetworkPlayer.Username</c> / <c>.Avatar</c>, which the game itself fills from
/// Steamworks — <c>PlatformUserData.GetAvatarForNetworkPlayer</c> → Facepunch
/// <c>SteamFriends.GetLargeAvatarAsync</c>, with the game's own default sprite for non-Steam
/// peers). Adding an identity field to our packet would be a second source of truth for
/// something the game is authoritative about — the same rule <see cref="OwnerTag"/> records.
///
/// PASSIVE READING IS NOT ENOUGH (MP-test defects 2 + 3): the game's automatic fetch on join
/// (<c>NetworkPlayer.Attached()</c>) feeds Steam a 32-BIT AccountId where a 64-bit SteamId is
/// required, so it resolves to nobody and the game stamps its OWN GREY PLACEHOLDER onto
/// <c>.Avatar</c> — non-null, which is why the first fix's "sprite is null" retry never fired and
/// both roles kept the silhouette (host until its roster row happened to re-fetch on character
/// assignment, VR client forever). The re-fetch therefore no longer lives here: it is the pump in
/// <see cref="NetPlayerActors.TickAvatarFetch"/>, driven per KNOWN PEER by
/// <see cref="NetAvatarDriver"/> and gated on <see cref="NetPlayerActors.ClassifyAvatar"/> (real
/// picture vs. the game's placeholder), so it runs the moment the peer exists — with this tag
/// hidden, with name tags switched off, and on both roles. Still zero wire, still the game's own
/// data path. This tag only DISPLAYS whatever sprite the game currently holds and swaps in the
/// real picture the frame it lands (the change gate below).
///
/// Degrades gracefully: no avatar sprite → name only (logged once per player, never a throw);
/// no username → "Player &lt;id&gt;". Purely cosmetic; owned/ticked/torn down by
/// <see cref="RemoteAvatar"/>.</remarks>
internal sealed class RemoteNameTag
{
    // World-unit metrics at avatar scale 1 (the placeholder head is ~0.20 units tall, see
    // HeadMaskLibrary.BuildPlaceholderHead) — deliberately a touch larger than OwnerTag's
    // board-corner row because this one floats in free space, not against a board face.
    private const float AvatarSize = 0.075f;  // square Steam-avatar quad
    private const float NameWidth = 0.32f;    // username box width (~1.6 head-widths, fits long names)
    private const float Height = 0.08f;       // tag row height (TmpFit caps line fill)
    private const float Pad = 0.012f;         // gap between avatar and name

    /// <summary>Air between the mask's rendered bounds TOP and the tag row's bottom edge, at
    /// sender scale 1 (metres; scaled by <see cref="RemoteAvatar.AppliedScale"/>). Small on
    /// purpose — the tag should hover DIRECTLY above the mask; every other size factor (mask
    /// style, mask-size stepper, head rotation, sender zoom) is already inside the bounds.</summary>
    private const float ClearanceAboveMask = 0.05f;

    /// <summary>FALLBACK rise above the head-HOLDER center, at scale 1 / mask size 1 — used only
    /// for the frame(s) in which no mask renderer exists yet (the bounds anchor above is the real
    /// placement): half the placeholder head (0.10) + the tag's own half height + air.</summary>
    private const float Rise = 0.30f;

    /// <summary>Readability floor for the tag's visual scale. The sender's rig scale (their world
    /// zoom) drops far below 1 when they lean into the diorama, and glyphs that render ~3 cm or
    /// smaller stop resolving at table distance in-headset. CHOICE: floor at 0.4× (≈3.2 cm row
    /// height) — below it the tag rides proportionally LARGE on a tiny avatar rather than
    /// shrinking into an unreadable speck; it stays visually attached regardless, because the
    /// follow position and rise keep tracking the head every frame.</summary>
    private const float MinScale = 0.4f;

    private readonly RemoteAvatar _owner;
    private readonly GameObject _root;
    private readonly Transform _billboard;
    private readonly string _fallbackName; // cached: NameFor is polled per frame, no per-frame $"" garbage

    private Material? _avatarMat;          // our clone (BoardVisual.Unlit creates one) — freed on rebuild/teardown
    private Sprite? _shownAvatar;
    private string? _shownName;
    private bool _built;
    private bool _loggedNameOnly;          // one line per player when we degrade to name-only

    // Centring diagnostics of the LAST Rebuild, logged on the first tick that places the rebuilt
    // row (the anchor is only known there) — see the CENTRING note in Rebuild.
    private bool _logPlacement;            // a rebuild is waiting for its placement log line
    private float _measuredTextWidth;      // rendered ink width of the name (local units, scale 1)
    private float _groupWidth;             // avatar + pad + measured name ink (local units, scale 1)
    private float _centringOffset;         // x shift vs. the old fixed-box row (local units, scale 1)

    // Mask-bounds anchor: the renderers under the head holder, cached until one of them dies
    // (mask style swap destroys the old HeadVisual → Unity-null entries → refetch next tick).
    private Renderer[] _maskRenderers = System.Array.Empty<Renderer>();

    public RemoteNameTag(RemoteAvatar owner)
    {
        _owner = owner;
        _fallbackName = $"Player {owner.PlayerId}"; // same fallback OwnerTag ships
        _root = new GameObject($"NameTag[{owner.PlayerId}]");
        // Parent = the avatar ROOT (scale 1, identity), NOT the head holder: the holder carries
        // the sender's full head rotation and a child would swing around as they look about.
        // Position/rotation/scale are re-solved in world space every Tick instead.
        _root.transform.SetParent(owner.Root, worldPositionStays: false);
        _billboard = _root.transform;
        _root.SetActive(false); // hidden until the head is tracked and the config gate says show
    }

    /// <summary>Follow the head, refresh identity (change-gated) and billboard toward the local
    /// head. Defensive style of the sibling add-ons: null-guards inside, hard failures land in
    /// the driver's per-avatar catch (NetAvatarDriver.TickAvatars) and cost only this avatar's
    /// frame — never the driver.</summary>
    public void Tick()
    {
        if (_root == null)
            return; // destroyed out from under us — RemoteAvatar.Tick's defensive contract

        // LIVE config gate + head presence. Re-read every tick (one bool), so toggling the
        // config entry applies immediately without a restart.
        bool want = NetModule.NameTags != null && NetModule.NameTags.Value
                    && _owner.HeadHolder != null && _owner.HeadHolder.gameObject.activeSelf;
        if (!want)
        {
            if (_root.activeSelf)
                _root.SetActive(false);
            return;
        }
        if (!_root.activeSelf)
            _root.SetActive(true);

        // Identity refresh, change-gated exactly like OwnerTag: the registry fills late on join
        // (bad-word masking + async Steam avatar), so keep polling; a rebuild only happens when
        // the sprite reference or the name actually changes.
        // NOTE: no fetch here. Asking for the picture is the pump's job
        // (NetPlayerActors.TickAvatarFetch, driven per peer by NetAvatarDriver) — a tag that only
        // ticks while it is VISIBLE is the wrong place to own a network retry, and the sprite the
        // game holds may be its placeholder rather than nothing at all.
        Sprite? avatar = NetPlayerActors.AvatarFor(_owner.PlayerId);
        string? name = NetPlayerActors.NameFor(_owner.PlayerId);
        if (string.IsNullOrEmpty(name))
            name = _fallbackName;
        if (!_built || !ReferenceEquals(avatar, _shownAvatar) || name != _shownName)
        {
            Rebuild(avatar, name!);
            _shownAvatar = avatar;
            _shownName = name;
            _built = true;
        }

        // Follow + sender-zoom scale. Anchor = the mask's actual rendered bounds, re-read every
        // tick (they move with the head and change with mask style/size/zoom): tag CENTER sits
        // over the bounds center, tag BOTTOM sits ClearanceAboveMask (true sender scale) above
        // the bounds top — the half-height term uses the CLAMPED factor because that is the
        // height the tag actually renders at (MinScale floor). Fallback (no renderer yet, e.g.
        // the tick right after a mask swap destroyed the old visual): the legacy holder-relative
        // rise, whose max(1, MaskSize) keeps it clear of an enlarged mask without bounds.
        float scale = Mathf.Max(_owner.AppliedScale, MinScale);
        bool fromBounds = TryGetMaskBounds(out Bounds mask);
        if (fromBounds)
        {
            float lift = ClearanceAboveMask * _owner.AppliedScale + Height * 0.5f * scale;
            _billboard.position = new Vector3(mask.center.x, mask.max.y + lift, mask.center.z);
        }
        else
        {
            float rise = Rise * scale * Mathf.Max(1f, _owner.MaskSize);
            _billboard.position = _owner.HeadHolder!.position + Vector3.up * rise;
        }
        _billboard.localScale = Vector3.one * scale;

        // CENTRING PROOF (user report: "the tag sits noticeably left of the mask"). Logged here and
        // not in Rebuild because the ANCHOR only exists once the mask bounds have been read — one
        // line per rebuild (identity changes), never per frame. It states everything the next
        // screenshot complaint needs: where the row was anchored, how wide the row measured, and
        // how far the measured centring moved it against the old fixed-name-box layout.
        if (_logPlacement)
        {
            _logPlacement = false;
            VRLog.Info("Net", $"Name tag player {_owner.PlayerId} CENTRED over the mask: anchor "
                + $"({_billboard.position.x:F3},{_billboard.position.y:F3},{_billboard.position.z:F3}) "
                + $"[{(fromBounds ? $"mask bounds centre ({mask.center.x:F3},{mask.center.z:F3}), top y {mask.max.y:F3}" : "head-holder fallback (no mask renderer yet)")}]; "
                + $"group {_groupWidth * scale:F3} m wide at scale {scale:F2} "
                + $"(avatar {(_shownAvatar != null ? AvatarSize + Pad : 0f):F3} incl. pad + measured name "
                + $"{_measuredTextWidth:F3} of a {NameWidth:F3} box, unscaled {_groupWidth:F3}); centring offset "
                + $"{_centringOffset * scale:F3} m (+x = moved RIGHT vs. the old fixed-box row). The row's "
                + "geometric centre is now the anchor x/z — avatar and name are one centred unit at any "
                + "name length, avatar size or world scale.");
        }

        // Billboard: quad/TMP read from their −Z side, so aim +Z AWAY from the local head (the
        // OwnerTag/PingNameTag convention).
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head != null)
        {
            Vector3 away = _billboard.position - head.transform.position;
            if (away.sqrMagnitude > 1e-6f)
                _billboard.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }
    }

    /// <summary>
    /// Union of the world-space AABBs of every live, enabled mask renderer under the head holder.
    /// World-axis-aligned is exactly right here: the tag is a billboard for the LOCAL viewer, and
    /// the world Y axis is the viewer's up (the same up the billboard rotation uses), so
    /// <c>bounds.max.y</c> IS "the visual top of the mask" from the viewer's frame regardless of
    /// how the sender has tilted their head. The renderer LIST is cached — a mask style swap
    /// destroys the old HeadVisual, its entries read as Unity-null next tick and trigger a
    /// refetch (one <c>GetComponentsInChildren</c> per rebuild, not per frame). Bounds-based on
    /// purpose: Mask_0..2 and the placeholder need no per-style numbers, and a mask that never
    /// shipped degrades to the placeholder's bounds automatically.
    /// </summary>
    private bool TryGetMaskBounds(out Bounds bounds)
    {
        bounds = default;
        Transform? head = _owner.HeadHolder;
        if (head == null)
            return false;

        bool stale = _maskRenderers.Length == 0;
        for (int i = 0; !stale && i < _maskRenderers.Length; i++)
        {
            if (_maskRenderers[i] == null)
                stale = true;
        }
        if (stale)
            _maskRenderers = head.GetComponentsInChildren<Renderer>(includeInactive: false);

        bool any = false;
        for (int i = 0; i < _maskRenderers.Length; i++)
        {
            Renderer r = _maskRenderers[i];
            if (r == null || !r.enabled)
                continue;
            if (!any)
            {
                bounds = r.bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }
        return any;
    }

    private void Rebuild(Sprite? avatar, string name)
    {
        // Clear previous visuals (children of the billboard root) AND our material clone —
        // materials are assets, Unity never frees them with the GameObject that referenced them.
        for (int i = _billboard.childCount - 1; i >= 0; i--)
            Object.Destroy(_billboard.GetChild(i).gameObject);
        if (_avatarMat != null)
            Object.Destroy(_avatarMat);
        _avatarMat = null;

        Texture? t = avatar != null ? avatar.texture : null;
        bool hasAvatar = t != null;
        float avatarSpan = hasAvatar ? AvatarSize + Pad : 0f;

        // ---- name box: BUILT FIRST, then MEASURED -----------------------------------------
        // CENTRING (user report 2026-08-02: "the tag sits noticeably left of the mask"). The old
        // layout centred avatar + the NAME BOX — a fixed NameWidth (0.32 m) container. The visible
        // ink is avatar + the ACTUAL glyph run, which is left-aligned inside that box, so every
        // name shorter than the box left an invisible tail of empty box on the right and pushed the
        // whole visible group LEFT of the anchor by half of it (a 0.12 m name: 0.10 m left, exactly
        // the screenshot). The row is therefore laid out from the RENDERED text width now: build the
        // label, force its mesh so auto-sizing resolves, read the ink width, PIN the resolved font
        // size (so shrinking the box cannot re-trigger auto-sizing), shrink the box onto the ink,
        // and only then place avatar + name from the group's own half-width. Result: the group's
        // geometric centre IS the anchor at any name length, avatar size and world scale.
        var labelGo = new GameObject("Name");
        labelGo.transform.SetParent(_billboard, worldPositionStays: false);
        var label = labelGo.AddComponent<TextMeshPro>();
        label.text = name;
        label.alignment = TextAlignmentOptions.Center; // the box is the ink now — centred either way
        label.color = new Color(1f, 0.95f, 0.85f);     // OwnerTag's warm off-white
        label.fontStyle = FontStyles.Bold;
        TmpFit.Fit(label, NameWidth, Height, maxFontSize: 0.065f, wrap: false);
        float textWidth = MeasureInkWidth(label);

        float groupWidth = avatarSpan + textWidth;
        float left = -groupWidth * 0.5f;
        _measuredTextWidth = textWidth;
        _groupWidth = groupWidth;
        // How far this moved the visible row against the old fixed-box layout: the old ink centre
        // sat at (textWidth − NameWidth) / 2, i.e. always left of the anchor.
        _centringOffset = (NameWidth - textWidth) * 0.5f;
        _logPlacement = true;

        if (t != null)
        {
            _avatarMat = BoardVisual.Unlit(Color.white, t);
            // Map the sprite's atlas rect onto the quad so packed avatars show the right region.
            Rect r = avatar!.rect;
            _avatarMat.mainTextureOffset = new Vector2(r.x / t.width, r.y / t.height);
            _avatarMat.mainTextureScale = new Vector2(r.width / t.width, r.height / t.height);
            MeshRenderer quad = BoardVisual.Quad(_billboard, "Avatar",
                new Vector2(AvatarSize, AvatarSize), _avatarMat);
            quad.transform.localPosition = new Vector3(left + AvatarSize * 0.5f, 0f, 0f);
        }
        else if (!_loggedNameOnly)
        {
            _loggedNameOnly = true;
            VRLog.Info("Net", $"Name tag for player {_owner.PlayerId}: the game holds NO avatar "
                + "sprite at all right now — showing the name only. The fetch pump keeps asking "
                + "(see the 'Steam avatar fetch' lines) and the picture is swapped in the frame it "
                + "lands.");
        }

        labelGo.transform.localPosition =
            new Vector3(left + avatarSpan + textWidth * 0.5f, 0f, -0.001f);
        WorldUI.MrBacking.Label(label); // free-floating over the room in MR — sized from the box above

        VRLayers.Apply(_root); // mod layer, so the owned head camera renders it
    }

    /// <summary>
    /// Rendered ink width of a freshly built auto-sized label (local units), and the box shrunk
    /// onto it. WHY the font size is pinned first: <see cref="TmpFit"/> leaves auto-sizing ON, so
    /// assigning a narrower rect would make TMP re-fit the text to the new box and the measurement
    /// would chase its own tail. After <c>ForceMeshUpdate</c> the resolved size is in
    /// <c>fontSize</c>; pinning it makes the box a pure container. Falls back to the full
    /// <see cref="NameWidth"/> box when TMP reports nothing usable (empty string, generation
    /// deferred) — that is exactly the old behaviour, so a degenerate case can never make the tag
    /// worse than before.
    /// </summary>
    private static float MeasureInkWidth(TextMeshPro label)
    {
        label.ForceMeshUpdate();
        float resolved = label.fontSize;
        if (resolved > 0f)
        {
            label.enableAutoSizing = false;
            label.fontSize = resolved;
        }
        float ink = label.textBounds.size.x;
        if (float.IsNaN(ink) || float.IsInfinity(ink) || ink <= 0.001f || ink > NameWidth)
            ink = NameWidth;
        // A hair of air on both sides so the pinned font can never clip against its own box.
        float box = Mathf.Min(ink + 2f * InkPad, NameWidth);
        label.rectTransform.sizeDelta = new Vector2(box, Height);
        return box;
    }

    /// <summary>Air left and right of the measured glyph run inside the shrunk name box (metres at
    /// scale 1) — protects the pinned font size against sub-pixel measurement rounding.</summary>
    private const float InkPad = 0.004f;

    public void Destroy()
    {
        if (_avatarMat != null)
            Object.Destroy(_avatarMat); // asset — not freed with the GameObject tree
        _avatarMat = null;
        if (_root != null)
            Object.Destroy(_root);
    }
}
