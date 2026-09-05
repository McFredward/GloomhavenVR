using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// The "WHO owns this board" tag pinned to a corner of a remote player's
/// <see cref="RemoteControlBoard"/>: an unlit quad showing the owner's Steam avatar
/// (<see cref="NetPlayerActors.AvatarFor"/>) next to their (bad-word-masked) username
/// (<see cref="NetPlayerActors.NameFor"/>). It BILLBOARDS toward the local
/// <see cref="VRRigDriver.HeadCamera"/> every frame so the label always reads face-on.
///
/// Everything is UNLIT (the void/scenario lighting is not ours to control — the head/hand rule).
/// Degrades gracefully: no avatar sprite → name only (centred); no username → "Player &lt;id&gt;".
/// Purely cosmetic; never touches game state. Owned/torn down by <see cref="RemoteControlBoard"/>.
/// </summary>
/// <remarks>CLASSIFICATION: PER-ACTOR MODEL — ZERO wire on OUR side channel. The Steam avatar and
/// the masked username are read out of the GAME's own netcode by reflection
/// (<c>NetPlayerActors.AvatarFor</c> / <c>NameFor</c>), which already replicates them; adding an
/// identity field to our packet would be a second source of truth for something the game is
/// authoritative about. See INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal sealed class OwnerTag
{
    private const float AvatarSize = 0.055f;   // square avatar quad, local metres
    private const float NameWidth = 0.16f;      // username box width
    private const float Height = 0.06f;         // tag row height
    private const float Pad = 0.008f;           // gap between avatar and name

    private readonly int _playerId;
    private readonly GameObject _root;
    private readonly Transform _billboard;
    private MeshRenderer? _avatarQuad;
    private TextMeshPro? _nameLabel;

    private Sprite? _shownAvatar;
    private string? _shownName;
    private bool _built;

    // The tag's own renderers for the panel-ladder compositing (BoardVisual.OrderWithPanels —
    // the "Steam logo mixes with the menu behind it" fix; same cache contract as
    // RemoteNameTag: invalidated by Rebuild, periodically refetched for MrBacking's lazy MR
    // backing plate, which is ADDED without any entry dying).
    private Renderer[] _tagRenderers = System.Array.Empty<Renderer>();
    private int _tagRenderersRefreshAt;
    private const int TagRenderersRefreshFrames = 90; // ~1 s at 90 Hz, see RemoteNameTag

    /// <summary>The live Steam-avatar quad, or null while this owner has no picture yet (non-Steam
    /// platform, fetch still in flight). Exposed so <see cref="RemoteFocusOutline"/> can seat the
    /// "this player owns the character at turn" ring around the picture — the ring is a SIBLING of
    /// the quad, so it is not squashed by the quad's non-uniform stretch scale.</summary>
    internal Transform? AvatarQuad => _avatarQuad != null ? _avatarQuad.transform : null;

    /// <summary>The avatar quad's authored size in tag-local metres (square), for the ring around it.</summary>
    internal static Vector2 AvatarQuadSize => new(AvatarSize, AvatarSize);

    /// <summary>This tag's root. Exposed for ONE consumer: the board's draw-order sweep
    /// (<c>BoardVisual.AdoptBoardOrder</c>) must EXCLUDE this subtree. The tag hangs off the board
    /// root for its pose, but it is a free-floating billboard, not a surface in the board's plane —
    /// it ranks against the panel ladder every frame through <see cref="BoardVisual.OrderWithPanels"/>,
    /// and letting the cluster adopt it too would be two writers on one field.</summary>
    internal Transform? Root => _root != null ? _root.transform : null;

    /// <summary>Build the tag under <paramref name="boardRoot"/> at the given board-LOCAL corner
    /// (so it inherits the board's world pose + scale for positioning). Facing is re-solved each
    /// <see cref="Tick"/> in world space, independent of the board's rotation.</summary>
    public OwnerTag(int playerId, Transform boardRoot, Vector3 localCorner)
    {
        _playerId = playerId;

        _root = new GameObject($"OwnerTag[{playerId}]");
        _root.transform.SetParent(boardRoot, worldPositionStays: false);
        _root.transform.localPosition = localCorner;
        _billboard = _root.transform;
    }

    /// <summary>Refresh the avatar/name (change-gated) and billboard toward the local head.</summary>
    public void Tick()
    {
        if (_root == null)
            return;

        // LIVE [Net] NameTags GATE — the SAME read the head tag makes (NetModule.NameTagsWanted),
        // because the setting is about the object and this board corner carries the other one of
        // its two rows. It used to be built unconditionally and never asked, so switching name tags
        // off left every peer's username readable here. Re-read every tick (one bool) so a flip
        // applies live, exactly as the description promises.
        //
        // The turn RING is seated under this root as a sibling of the avatar quad
        // (Net.AvatarTurnRing), so switching the row off switches that cue off with it — the same
        // relationship, and the same one-hide rule, RemoteNameTag records for its own children.
        bool want = NetModule.NameTagsWanted;
        if (_root.activeSelf != want)
        {
            _root.SetActive(want);
            // HW-VERIFY: this line decides R24 — that [Net] NameTags reaches BOTH identity rows, not
            // just the one above the head. Change-gated on the flip itself, so it fires once per
            // press of a setting a human operates and never per frame.
            VRLog.Note("Net", $"Board owner tag [{_playerId}] {(want ? "SHOWN" : "HIDDEN")} by "
                + "[Net] NameTags — the board-corner row follows the same live gate as the "
                + "head-mask row now, which is what the setting's own description promises "
                + "(turn OFF to hide ALL tags).");
        }
        if (!want)
            return;

        Sprite? avatar = NetPlayerActors.AvatarFor(_playerId);
        string? name = NetPlayerActors.NameFor(_playerId);
        if (string.IsNullOrEmpty(name))
            name = $"Player {_playerId}";

        if (!_built || !ReferenceEquals(avatar, _shownAvatar) || name != _shownName)
        {
            Rebuild(avatar, name!);
            _shownAvatar = avatar;
            _shownName = name;
            _built = true;
        }

        // Billboard: the quad/TMP read from their −Z side, so aim +Z AWAY from the head (same
        // convention as VRCard's held-card billboard).
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head != null)
        {
            Vector3 away = _billboard.position - head.transform.position;
            if (away.sqrMagnitude > 1e-6f)
                _billboard.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
            // PANEL COMPOSITING (user report 2026-08-04): rank the tag's renderers on the
            // converted-panel distance ladder so a menu window BEHIND the tag can no longer
            // alpha-blend over the Steam avatar — and a window in FRONT fully occludes it.
            // Full root cause: BoardVisual.OrderWithPanels.
            RefreshTagRenderers();
            BoardVisual.OrderWithPanels(_tagRenderers, away.magnitude);
        }
    }

    /// <summary>Lazy-stale cache of the tag's own renderers (see the field note): refetch when
    /// empty, when a Rebuild dropped it, when an entry died, or on the periodic MR-plate pickup.</summary>
    private void RefreshTagRenderers()
    {
        bool stale = _tagRenderers.Length == 0 || Time.frameCount >= _tagRenderersRefreshAt;
        for (int i = 0; !stale && i < _tagRenderers.Length; i++)
        {
            if (_tagRenderers[i] == null)
                stale = true;
        }
        if (!stale)
            return;
        _tagRenderers = _root.GetComponentsInChildren<Renderer>(includeInactive: false);
        _tagRenderersRefreshAt = Time.frameCount + TagRenderersRefreshFrames;
    }

    private void Rebuild(Sprite? avatar, string name)
    {
        // Clear previous visuals (children of the billboard root).
        for (int i = _billboard.childCount - 1; i >= 0; i--)
            Object.Destroy(_billboard.GetChild(i).gameObject);
        _avatarQuad = null;
        _nameLabel = null;

        Texture? t = avatar != null ? avatar.texture : null;
        bool hasAvatar = t != null;
        float avatarSpan = hasAvatar ? AvatarSize + Pad : 0f;

        // ---- name box: BUILT FIRST, then MEASURED --------------------------------------------
        // CENTRED ON MEASURED INK, NOT ON A FIXED BOX. This row used to centre avatar + the fixed
        // NameWidth CONTAINER, with the label left-aligned inside it — which RemoteNameTag's own
        // header names as a fixed DEFECT (user report 2026-08-02): the visible ink is avatar + the
        // ACTUAL glyph run, so every name shorter than the box left an invisible tail of empty box
        // on the right and pushed the whole visible group LEFT of the anchor by half of it. The
        // report was about the layout, not about which carrier was being looked at, and this is the
        // second carrier of the same row: on a peer's board corner a short name hung ~5 cm left of
        // its anchor on a 0.64 m board whose anchor is already at x = −0.30, while the SAME
        // person's head tag was centred correctly.
        //
        // The measurement is RemoteNameTag.MeasureInkWidth — the one that was fixed, called rather
        // than re-typed, with this row's own authored box as its ceiling and fallback.
        var labelGo = new GameObject("Name");
        labelGo.transform.SetParent(_billboard, worldPositionStays: false);
        _nameLabel = labelGo.AddComponent<TextMeshPro>();
        _nameLabel.text = name;
        _nameLabel.alignment = TextAlignmentOptions.Center; // the box is the ink now
        _nameLabel.color = new Color(1f, 0.95f, 0.85f);
        _nameLabel.fontStyle = FontStyles.Bold;
        TmpFit.Fit(_nameLabel, NameWidth, Height, maxFontSize: 0.05f, wrap: false);
        float textWidth = RemoteNameTag.MeasureInkWidth(_nameLabel, NameWidth, Height);

        float totalWidth = avatarSpan + textWidth;
        float left = -totalWidth * 0.5f;

        if (t != null)
        {
            Material mat = BoardVisual.Unlit(Color.white, t);
            // Map the sprite's atlas rect onto the quad so packed avatars show the right region.
            Rect r = avatar!.rect;
            mat.mainTextureOffset = new Vector2(r.x / t.width, r.y / t.height);
            mat.mainTextureScale = new Vector2(r.width / t.width, r.height / t.height);
            _avatarQuad = BoardVisual.Quad(_billboard, "Avatar",
                new Vector2(AvatarSize, AvatarSize), mat);
            _avatarQuad.transform.localPosition = new Vector3(left + AvatarSize * 0.5f, 0f, 0f);
        }

        labelGo.transform.localPosition =
            new Vector3(left + avatarSpan + textWidth * 0.5f, 0f, -0.001f);
        WorldUI.MrBacking.Label(_nameLabel); // free-floating over the room in MR

        // HW-VERIFY: this line decides R17 — that the board tag is centred on its MEASURED ink like
        // the head tag, not on the fixed container. Rebuild runs only on an identity change (sprite
        // or name), so this is change-gated by construction and can never be per-frame.
        VRLog.Note("Net", $"Board owner tag [{_playerId}] CENTRED on measured ink: avatar "
            + $"{avatarSpan:F3} incl. pad + name {textWidth:F3} of a {NameWidth:F3} box = group "
            + $"{totalWidth:F3} m; centring offset {(NameWidth - textWidth) * 0.5f:F3} m "
            + "(+x = moved RIGHT vs. the old fixed-box row, which is the same defect "
            + "RemoteNameTag fixed for the head row on 2026-08-02).");

        VRLayers.Apply(_root);
        _tagRenderers = System.Array.Empty<Renderer>(); // rebuilt children → re-cache next Tick
    }

    public void Destroy()
    {
        if (_root != null)
            Object.Destroy(_root);
    }
}
