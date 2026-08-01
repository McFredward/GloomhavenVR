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
/// SCALE FOLLOWS THE SENDER'S ZOOM: the tag's visual size and its rise above the head both use
/// <see cref="RemoteAvatar.AppliedScale"/> — the sender's rig <c>WorldScale</c> from the rig
/// packet, the EXACT factor <c>RemoteAvatar.SetTarget</c> writes onto the head/hand holders — so
/// when a peer zooms their world and their avatar shrinks or grows on our screen, the tag stays
/// visually attached and proportionate instead of dwarfing a tiny head. Clamped at
/// <see cref="MinScale"/> so a far-zoomed-in peer keeps a readable label (see the constant's
/// remark for the trade-off). The rise additionally clears an enlarged head mask
/// (<see cref="RemoteAvatar.MaskSize"/> &gt; 1) so the tag never intersects the mask.
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

    /// <summary>Tag center above the head-HOLDER center, at scale 1 / mask size 1: half the
    /// placeholder head (0.10) + the tag's own half height + comfortable air.</summary>
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

        // Follow + sender-zoom scale. Both the size and the rise use the clamped factor: the
        // rise must clear the TAG's own (possibly floored) height, and a floored tag hovering a
        // floored rise above a tiny head reads attached — a true-scale rise would sink the
        // oversized label into the mask. MaskSize only ever ADDS clearance (max(1, …)): a
        // shrunken mask needs no less air than the default one.
        float scale = Mathf.Max(_owner.AppliedScale, MinScale);
        float rise = Rise * scale * Mathf.Max(1f, _owner.MaskSize);
        _billboard.position = _owner.HeadHolder!.position + Vector3.up * rise;
        _billboard.localScale = Vector3.one * scale;

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
        // Centre the whole row on the anchor.
        float totalWidth = avatarSpan + NameWidth;
        float left = -totalWidth * 0.5f;

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
            VRLog.Info("Net", $"Name tag for player {_owner.PlayerId}: no Steam avatar sprite in the "
                + "game's registry (non-Steam peer, or the async fetch has not landed yet) — showing "
                + "the name only; the picture appears by itself if the sprite arrives later.");
        }

        var labelGo = new GameObject("Name");
        labelGo.transform.SetParent(_billboard, worldPositionStays: false);
        labelGo.transform.localPosition =
            new Vector3(left + avatarSpan + NameWidth * 0.5f, 0f, -0.001f);
        var label = labelGo.AddComponent<TextMeshPro>();
        label.text = name;
        label.alignment = hasAvatar ? TextAlignmentOptions.Left : TextAlignmentOptions.Center;
        label.color = new Color(1f, 0.95f, 0.85f); // OwnerTag's warm off-white
        label.fontStyle = FontStyles.Bold;
        TmpFit.Fit(label, NameWidth, Height, maxFontSize: 0.065f, wrap: false);

        VRLayers.Apply(_root); // mod layer, so the owned head camera renders it
    }

    public void Destroy()
    {
        if (_avatarMat != null)
            Object.Destroy(_avatarMat); // asset — not freed with the GameObject tree
        _avatarMat = null;
        if (_root != null)
            Object.Destroy(_root);
    }
}
