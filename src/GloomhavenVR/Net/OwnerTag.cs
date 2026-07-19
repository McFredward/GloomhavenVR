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
        }
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
        // Centre the whole row on the corner anchor.
        float totalWidth = avatarSpan + NameWidth;
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

        var labelGo = new GameObject("Name");
        labelGo.transform.SetParent(_billboard, worldPositionStays: false);
        labelGo.transform.localPosition =
            new Vector3(left + avatarSpan + NameWidth * 0.5f, 0f, -0.001f);
        _nameLabel = labelGo.AddComponent<TextMeshPro>();
        _nameLabel.text = name;
        _nameLabel.alignment = hasAvatar ? TextAlignmentOptions.Left : TextAlignmentOptions.Center;
        _nameLabel.color = new Color(1f, 0.95f, 0.85f);
        _nameLabel.fontStyle = FontStyles.Bold;
        TmpFit.Fit(_nameLabel, NameWidth, Height, maxFontSize: 0.05f, wrap: false);

        VRLayers.Apply(_root);
    }

    public void Destroy()
    {
        if (_root != null)
            Object.Destroy(_root);
    }
}
