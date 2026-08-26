using GloomhavenVR.Core;
using GloomhavenVR.Net;
using UnityEngine;

namespace GloomhavenVR.Voice;

// =================================================================================================
//  VOICE BADGE — the loudspeaker in the corner of a peer's Steam picture, deflecting with their
//  voice. Drawn by RemoteNameTag, driven by VoiceSpatial, gated by [Voice] SpeakingBadge.
// =================================================================================================

/// <summary>
/// A small loudspeaker glyph seated in the lower-right corner of a peer's Steam-avatar quad, whose
/// arcs light up with how loudly that peer is talking.
///
/// <para><b>USER REQUEST, and it was corrected mid-round.</b> First: <i>"Optional möchte ich auch,
/// dass wenn jemand spricht das entsprechend im Stem-Logo sichtbar ist (deaktivierbar). zB mit einem
/// Lautsprechersymbol in einer Ecke das ausschlägt bei Ton."</i> Then, when asked: <i>"Steam-Logo
/// über dem Kopf; sorry typo"</i>. So "Stem-Logo" was STEAM logo, and it means the Steam profile
/// picture the mod already floats above every peer's head — <see cref="RemoteNameTag"/> — not the
/// mask, and not a corner of the view.</para>
///
/// <para><b>THAT PLACEMENT IS BETTER THAN A FREE-STANDING INDICATOR AND FOR REASONS WORTH
/// RECORDING.</b> (1) The tag is constructed per <see cref="RemoteAvatar"/> and keyed by
/// <c>PlayerId</c>, so the badge INHERITS the voice-to-avatar identity mapping instead of needing
/// its own — there is no second place a mis-binding could appear. (2) It is already gated by
/// <c>[Net] NameTags</c>, so a player who has tags off gets no badge and that needed no second
/// rule. (3) It is already above the head, beside the face the voice now comes from, so the picture
/// and the sound agree by construction rather than by coincidence.</para>
///
/// =============================================================================================
/// <para><b>IS IT BIG ENOUGH TO SEE? THE ARITHMETIC, BECAUSE THIS PROJECT HAS SHIPPED AN ENGRAVING
/// THAT WAS PRESENT, DRAWN, IN THE LINE OF SIGHT AND INVISIBLE.</b></para>
/// <para>The avatar quad is <c>RemoteNameTag.AvatarSize = 0.075</c> units at sender scale 1, and the
/// tag is drawn at the SENDER's rig scale. When both players are at a comparable scale, that quad
/// subtends the same angle as a 7.5 cm square held at the same real distance. The badge is
/// <c>[Voice] BadgeScale</c> of it, default 0.38, so:</para>
/// <code>
///     badge edge     = 0.075 * 0.38            = 2.85 cm perceived
///     at 1.5 m       = 0.0285 / 1.5  = 19.0 mrad = 1.09 degrees
///     at 2.5 m       = 0.0285 / 2.5  = 11.4 mrad = 0.65 degrees
/// </code>
/// <para>A Quest 3 through Virtual Desktop resolves roughly 20-25 pixels per degree near the centre
/// of the view, so the badge is about <b>22-27 px across at 1.5 m and 13-16 px at 2.5 m</b>. A
/// three-arc loudspeaker at 13 px is legible as "a speaker symbol with something happening in it"
/// but the individual arcs will NOT be separable at the far end of that range — which is why the
/// glyph carries its own dark rim (<c>VoiceIcon.RimWidth</c>), why the arcs are widely spaced, and
/// why the badge appears and disappears with speech rather than only changing internally: the
/// ON/OFF transition is legible at any size the tag itself is legible at, and the arc count is a
/// bonus at conversational distance. <b>If he reports he cannot see it, the first dial to move is
/// <c>[Voice] BadgeScale</c> toward 0.6, and the second option — which needs no new code — is to
/// raise <c>[Net] MaskSize</c>, since the whole tag rides the sender's scale.</b> This is a
/// prediction from arithmetic and a screen-pixel estimate, not a measurement; nobody has looked
/// through the headset at it.</para>
///
/// =============================================================================================
/// <para><b>PER FRAME, NOT PER SECOND.</b> The carrier refreshes its renderer cache only every
/// <c>TagRenderersRefreshFrames</c> (90, ~1 s) because the only thing that ADDS a renderer outside a
/// rebuild is the lazy mixed-reality backing plate. A badge that changed at that cadence would look
/// broken, so the glyph is driven every frame from <see cref="VoiceSpatial.TryGetVoice"/> and the
/// cost is stated rather than assumed: <b>one dictionary lookup, one int compare, and — only when
/// the level actually crosses a step boundary — one <c>Material.mainTexture</c> assignment.</b> The
/// four frames are rasterised once for the whole session and shared by every peer. In the steady
/// state of somebody talking continuously, this writes nothing at all.</para>
///
/// <para>It does return true on the tick it changes VISIBILITY, and the carrier uses that to force
/// its renderer cache stale immediately — otherwise a badge that just appeared would spend up to a
/// second at draw order 0 while the row it belongs to rides the panel ladder at ~96, and a menu
/// window behind the peer would paint over it. That is the same defect
/// <see cref="AvatarTurnRing"/> already had to solve, and the same fix.</para>
///
/// <para><b>THE LOCAL PLAYER NEVER SEES ONE ON THEMSELVES.</b> A <see cref="RemoteNameTag"/> exists
/// only per <see cref="RemoteAvatar"/>, and the local player has no <c>RemoteAvatar</c> — the driver
/// drops its own echo before an avatar is ever created (<c>NetAvatarDriver.cs:2766</c>). On top of
/// that, <see cref="VoiceSpatial"/> only ever publishes state for voice users it has bound to a
/// REMOTE network player, and it explicitly skips any voice whose account matches
/// <c>SelfUserVoice.PlatformAccountID</c>. Two independent reasons, neither relying on the
/// other.</para>
///
/// <para><b>WITH <c>[Net] NameTags</c> OFF THERE IS NO BADGE AND THE VOICE IS STILL SPATIAL.</b>
/// That is the intended behaviour and not an oversight: the two dials answer different questions
/// ("where does the sound come from" and "is there a label above their head"), and the badge is a
/// feature OF the label.</para>
/// </summary>
internal sealed class VoiceBadge
{
    /// <summary>Proud of the picture AND of the turn ring (which sits at -0.004), so the glyph is
    /// never z-fighting with either. +Z points away from the viewer, so proud is negative.</summary>
    private const float ProudZ = -0.007f;

    /// <summary>Inset from the quad's corner, as a fraction of the badge's own edge.</summary>
    private const float Inset = 0.10f;

    /// <summary>The tag's warm off-white, matching the username label.</summary>
    private static readonly Color Ink = new(1f, 0.95f, 0.85f);

    /// <summary>One rasterised frame per level step, shared by every peer for the whole session.</summary>
    private static Texture2D?[]? _frames;

    private readonly int _playerId;

    private GameObject? _go;
    private Material? _material;
    private Transform? _host;      // the avatar quad this badge is seated against
    private int _shownStep = -1;

    internal VoiceBadge(int playerId) => _playerId = playerId;

    /// <summary>
    /// Per-frame refresh. <paramref name="avatarQuad"/> is the live Steam-avatar quad, or null while
    /// there is none (no picture yet, or the carrier rebuilt its children).
    /// <paramref name="carrierVisible"/> is the tag's own visibility decision, so a hidden tag never
    /// leaves a badge hanging in the void.
    ///
    /// <para>Returns true on any tick that changed what is DRAWN (built, destroyed, or toggled
    /// visibility) — the carrier re-caches its renderers on that, so a badge that just appeared is
    /// on the panel ladder the same frame.</para>
    /// </summary>
    internal bool Tick(Transform? avatarQuad, float avatarSize, bool carrierVisible)
    {
        bool want = carrierVisible
                    && avatarQuad != null
                    && VoiceModule.SpeakingBadge != null && VoiceModule.SpeakingBadge.Value
                    && VoiceSpatial.TryGetVoice(_playerId, out bool speaking, out _)
                    && speaking;

        if (!want)
        {
            if (_go != null && _go.activeSelf)
            {
                _go.SetActive(false);
                return true;
            }
            return false;
        }

        VoiceSpatial.TryGetVoice(_playerId, out _, out int step);

        bool changed = false;
        if (_go == null || !ReferenceEquals(_host, avatarQuad))
        {
            // SIBLING of the quad, never a child: the quad is a unit Unity quad stretched to the
            // avatar size by a non-uniform localScale, which a child would inherit and be squashed
            // by. Exactly AvatarTurnRing's reasoning and exactly its fix.
            Destroy();
            Transform parent = avatarQuad!.parent != null ? avatarQuad.parent : avatarQuad;
            float edge = avatarSize * Mathf.Clamp(VoiceModule.BadgeScale!.Value, 0.05f, 1f);

            _material = BoardVisual.Unlit(Ink, Frame(step));
            MeshRenderer mr = BoardVisual.Quad(parent, $"GloomhavenVR.VoiceBadge[{_playerId}]",
                                               new Vector2(edge, edge), _material);
            _go = mr.gameObject;
            _host = avatarQuad;
            _shownStep = step;
            VRLayers.Apply(_go);
            changed = true;
        }

        // Follow the quad's own seat inside its row, then step into its lower-right corner. Done
        // every tick rather than at build time because the row is re-laid-out on every identity
        // change and the quad's seat moves with the measured name width.
        float e = avatarSize * Mathf.Clamp(VoiceModule.BadgeScale!.Value, 0.05f, 1f);
        Vector3 seat = avatarQuad!.localPosition;
        _go!.transform.localPosition = new Vector3(
            seat.x + avatarSize * 0.5f - e * 0.5f - e * Inset,
            seat.y - avatarSize * 0.5f + e * 0.5f + e * Inset,
            seat.z + ProudZ);
        _go.transform.localScale = new Vector3(e, e, 1f);

        if (!_go.activeSelf)
        {
            _go.SetActive(true);
            changed = true;
        }

        // THE ONLY PER-FRAME WRITE, and it is change-gated: the texture swaps only when the level
        // actually crosses a step boundary, which VoiceCurve.StepFor already damps with hysteresis.
        if (step != _shownStep && _material != null)
        {
            _shownStep = step;
            _material.mainTexture = Frame(step);
        }

        return changed;
    }

    internal void Destroy()
    {
        if (_go != null)
            Object.Destroy(_go);
        // BoardVisual.Unlit creates a clone; materials are assets and Unity never frees them with
        // the GameObject that referenced them. The name tag learned this the same way.
        if (_material != null)
            Object.Destroy(_material);
        _go = null;
        _material = null;
        _host = null;
        _shownStep = -1;
    }

    /// <summary>
    /// The shared texture for <paramref name="step"/>, rasterised on first use by
    /// <see cref="VoiceIcon"/>.
    ///
    /// <para>Generated rather than shipped in the asset bundle: the bundle is 70 MB and is rebuilt
    /// on the integrator's cadence, so a feature that needed a bundle rebuild could not ship in the
    /// round it was written. Four 64x64 RGBA textures with mips cost about 88 KB of VRAM in total,
    /// once, for the session.</para>
    /// </summary>
    private static Texture2D? Frame(int step)
    {
        if (step < 0)
            step = 0;
        if (step > VoiceCurve.Steps - 1)
            step = VoiceCurve.Steps - 1;

        if (_frames == null)
        {
            _frames = new Texture2D?[VoiceCurve.Steps];
            var rgba = new byte[VoiceIcon.ByteCount];
            for (int i = 0; i < VoiceCurve.Steps; i++)
            {
                VoiceIcon.Render(i, rgba);
                var tex = new Texture2D(VoiceIcon.Size, VoiceIcon.Size, TextureFormat.RGBA32, mipChain: true)
                {
                    name = $"GloomhavenVR.VoiceIcon[{i}]",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    // The badge is a few dozen pixels across and is read at a glance; anisotropy
                    // buys nothing at that size and the mip chain is what stops it crawling
                    // per-eye when the peer's head moves (spatial aliasing IS the stereo flicker).
                    anisoLevel = 1,
                };
                tex.SetPixelData(rgba, 0);
                tex.Apply(updateMipmaps: true, makeNoLongerReadable: false);
                _frames[i] = tex;
            }
            VRLog.Info(VoiceChatBridge.Scope, $"VOICE BADGE: rasterised {VoiceCurve.Steps} loudspeaker frames at "
                + $"{VoiceIcon.Size}x{VoiceIcon.Size} from VoiceIcon (no bundle asset, no Shader.Find). They are "
                + "shared by every peer for the whole session; the badge itself is one quad per peer and swaps "
                + "texture only when the talker's level crosses a step boundary.");
        }
        return _frames[step];
    }
}
