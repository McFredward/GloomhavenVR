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
/// PASSIVE READING IS NOT ENOUGH (MP-test defect 2): the game's automatic fetch on join
/// (<c>NetworkPlayer.Attached()</c>) feeds Steam a 32-BIT AccountId where a 64-bit SteamId is
/// required, so it never lands and <c>.Avatar</c> stays null on every machine until one of the
/// game's flat UI rows happens to call <c>UpdatePlayerProfileAvatar()</c> (host: on character
/// assignment; VR client: typically never). While the sprite is missing this tag therefore
/// re-triggers that same seam itself via <see cref="NetPlayerActors.RequestAvatarFetch"/> —
/// one attempt per <see cref="FetchRetrySeconds"/>, hard-capped at
/// <see cref="FetchMaxAttempts"/>, stopped for good when Steam is not running locally
/// (non-Steam session, logged once). Still zero wire, still the game's own data path.
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

    /// <summary>Re-trigger the game's avatar fetch at most this often while the sprite is missing
    /// (seconds, unscaled). Generous: the fetch is async anyway, and each attempt costs a
    /// reflection invoke into the game's UI seam.</summary>
    private const float FetchRetrySeconds = 5f;

    /// <summary>Hard cap on fetch attempts per peer (≈40 s with <see cref="FetchRetrySeconds"/>).
    /// After it the tag stays name-only exactly as before — the cap only ends the retrying.</summary>
    private const int FetchMaxAttempts = 8;

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

    // Mask-bounds anchor: the renderers under the head holder, cached until one of them dies
    // (mask style swap destroys the old HeadVisual → Unity-null entries → refetch next tick).
    private Renderer[] _maskRenderers = System.Array.Empty<Renderer>();

    // Active avatar-fetch retry state (defect 2) — per peer, capped, never throws.
    private int _fetchAttempts;
    private float _nextFetchAt;            // Time.unscaledTime gate between attempts
    private bool _fetchStopped;            // permanent: no Steam / cap reached
    private bool _loggedFetchStart;        // one line when we first re-trigger the game's fetch
    private bool _loggedFetchStop;         // one line when we stop for good

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
        if (avatar == null)
            MaybeRequestAvatarFetch(); // the game's own join-time fetch is broken — nudge its UI seam
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
        if (TryGetMaskBounds(out Bounds mask))
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

    /// <summary>
    /// Retry policy for the ACTIVE avatar fetch (class remarks): while the registry sprite is
    /// missing, ask <see cref="NetPlayerActors.RequestAvatarFetch"/> to re-run the game's own
    /// <c>UpdatePlayerProfileAvatar()</c> seam — at most once per <see cref="FetchRetrySeconds"/>,
    /// at most <see cref="FetchMaxAttempts"/> times, and never again once Steam reads as not
    /// running (non-Steam session — the game path is a hard no-op then). A landed sprite ends the
    /// loop implicitly: the caller only invokes this while the sprite is null, and non-Steam PEERS
    /// land the game's default sprite through the very same call. Never throws (the reflection
    /// seam swallows); an attempt that reports <c>Unavailable</c> (peer not registered yet, seam
    /// missing) just burns one capped attempt.
    /// </summary>
    private void MaybeRequestAvatarFetch()
    {
        if (_fetchStopped)
            return;
        float now = Time.unscaledTime;
        if (now < _nextFetchAt)
            return;
        _nextFetchAt = now + FetchRetrySeconds;

        if (_fetchAttempts >= FetchMaxAttempts)
        {
            _fetchStopped = true;
            if (!_loggedFetchStop)
            {
                _loggedFetchStop = true;
                VRLog.Info("Net", $"Name tag for player {_owner.PlayerId}: no avatar sprite after "
                    + $"{FetchMaxAttempts} re-triggered fetches — giving up, staying name-only "
                    + "(the picture still appears by itself if the game fills it later).");
            }
            return;
        }
        _fetchAttempts++;

        switch (NetPlayerActors.RequestAvatarFetch(_owner.PlayerId))
        {
            case NetPlayerActors.AvatarFetch.NoSteam:
                _fetchStopped = true;
                if (!_loggedFetchStop)
                {
                    _loggedFetchStop = true;
                    VRLog.Info("Net", $"Name tag for player {_owner.PlayerId}: local Steam client "
                        + "not running (non-Steam session) — avatar fetch impossible, staying "
                        + "name-only without further retries.");
                }
                break;
            case NetPlayerActors.AvatarFetch.Requested:
                if (!_loggedFetchStart)
                {
                    _loggedFetchStart = true;
                    VRLog.Info("Net", $"Name tag for player {_owner.PlayerId}: re-triggered the "
                        + "game's own avatar fetch (NetworkPlayer.UpdatePlayerProfileAvatar — the "
                        + "seam its MP user rows call, which uses the full 64-bit SteamId; the "
                        + "automatic join-time fetch feeds Steam a 32-bit AccountId and never "
                        + $"lands). Retrying every {FetchRetrySeconds:0}s until a sprite arrives, "
                        + $"max {FetchMaxAttempts} attempts.");
                }
                break;
                // Unavailable: quiet — transient (peer not in the registry yet) or the bridge is
                // disabled; the attempt cap bounds both.
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
                + "game's registry yet — showing the name only while the active fetch retries "
                + "(see the RequestAvatarFetch lines); the picture appears by itself once a "
                + "sprite lands.");
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
        WorldUI.MrBacking.Label(label); // free-floating over the room in MR

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
