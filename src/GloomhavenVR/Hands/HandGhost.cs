using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Hands;

/// <summary>
/// REVERSIBLE semi-transparency ("ghost hand") for ONE hand-visual subtree.
///
/// WHY this exists: with the card fan open on the palm, the hand MESH sits between the eye
/// and the cards — fingers, knuckles and the glove cuff cover card details exactly where the
/// player is trying to read them. Fading that one hand while its fan is open keeps the hand
/// present (you still see where your fingers are, so grabbing a card stays intuitive) but lets
/// the art through. Optional and OFF by default ([Hands] GhostHandOnFan), strength tunable
/// ([Hands] GhostHandStrength) — see <see cref="HandGhosts"/> for the policy side.
///
/// SAFETY — why this never corrupts anything:
///  * NO SHARED MATERIAL IS EVER MUTATED. Hand materials are shared: the procedural hand builds
///    ONE material per side and puts it on ~40 primitives, the glove prefab's materials are the
///    bundle assets themselves — and the very same assets are used by the mirrored self-preview
///    (<see cref="WorldUI.AvatarMirror"/>) and by EVERY remote player's hands
///    (<see cref="Net.RemoteAvatar"/>). Writing an alpha into those would fade every hand in the
///    room (and permanently, because bundle assets survive the object that referenced them).
///    Instead we clone each material into a per-RENDERER instance (the lesson
///    <see cref="Cards.VRCard"/>'s render-on-top and FigureGrabbable's glow already encode),
///    hand the clones to the renderer via <c>sharedMaterials</c> (assigning that property does
///    NOT make Unity instantiate anything behind our back, unlike <c>.materials</c>), and
///    DESTROY the clones on release. The original array is kept verbatim and put back.
///  * ATTACHMENTS ARE EXCLUDED. The card fan is parented to <c>Rig.PalmCenter</c> — which, for a
///    glove prefab, lives INSIDE the hand subtree — and grabbed objects hang off
///    <c>Rig.GrabAnchor</c>, the wrist HUD off <c>Rig.Wrist</c>. A naive subtree sweep would
///    fade the very cards we are trying to reveal, so anything under one of the three
///    attachment SOCKETS is skipped (see <see cref="IsAttachment"/>).
///  * SHADOWS are switched off while ghosted and restored on release — a fully solid shadow
///    under a see-through hand reads as a bug.
///
/// SHADER HANDLING: the hands do not all use one shader. The procedural fallback uses
/// <c>Sprites/Default</c> (unlit, already alpha-blended — the VR void has no lights, see
/// <see cref="HandVisuals"/>'s CreateHandMaterial), while a bundle glove ships whatever the
/// companion Unity project baked (Standard, in practice). An OPAQUE Standard material ignores
/// the alpha channel entirely until its rendering mode is switched, so
/// <see cref="MakeTransparent"/> flips the whole Standard "Fade" recipe — <c>_Mode</c>,
/// <c>_SrcBlend</c>/<c>_DstBlend</c>/<c>_ZWrite</c>, the keyword trio and the render queue —
/// plus the URP <c>_Surface</c>/<c>_SURFACE_TYPE_TRANSPARENT</c> equivalents, all guarded by
/// <c>HasProperty</c> so a shader that has none of them (the unlit fallback) is simply tinted.
/// The engage log names the shaders actually found, so an unexpected one is diagnosable from a
/// hardware log instead of guesswork.
/// </summary>
internal sealed class HandGhost
{
    // Standard-shader (and URP) plumbing, resolved once.
    private static readonly int ModeId = Shader.PropertyToID("_Mode");
    private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
    private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
    private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

    /// <summary>Colour properties probed in order — first one the shader has carries the alpha.</summary>
    private static readonly int[] ColorIds =
    {
        Shader.PropertyToID("_Color"),
        Shader.PropertyToID("_BaseColor"),
        Shader.PropertyToID("_TintColor"),
        Shader.PropertyToID("_MainColor"),
        Shader.PropertyToID("_UnlitColor"),
    };

    private readonly string _label;

    private HandRig? _rig;
    private Renderer[]? _renderers;
    private Material[][]? _originals;   // parallel to _renderers: the untouched shared arrays
    private Material[][]? _ghosts;      // parallel to _renderers: OUR clones (we destroy these)
    private UnityEngine.Rendering.ShadowCastingMode[]? _shadows;
    private float _appliedAlpha = -1f;

    /// <summary><paramref name="label"/> identifies this ghost in the log ("local Left",
    /// "mirror Right", "remote[3]") — the same mechanism runs for the local hands, the mirror
    /// clone and every remote avatar, so the log must say WHICH one moved.</summary>
    internal HandGhost(string label)
    {
        _label = label;
    }

    /// <summary>True while material clones are installed (i.e. the hand is faded).</summary>
    internal bool Engaged => _renderers != null;

    /// <summary>How many renderers the ghost currently covers (0 while released).</summary>
    internal int RendererCount => _renderers != null ? _renderers.Length : 0;

    /// <summary>
    /// Drive the ghost: fade <paramref name="rig"/> to <paramref name="alpha"/> (0 = invisible,
    /// 1 = opaque), or release when <paramref name="rig"/> is null / torn down. Idempotent and
    /// allocation-free once engaged — a strength edit only rewrites the clones' colours, and a
    /// DIFFERENT rig (hand rebuilt after a style switch, remote player flipping their dominant
    /// hand) restores the old one first and re-scans.
    /// </summary>
    internal void Apply(HandRig? rig, float alpha)
    {
        if (rig == null || rig.Root == null)
        {
            Release();
            return;
        }

        if (!ReferenceEquals(rig, _rig))
        {
            Release();
            Engage(rig, alpha);
            return;
        }

        if (_renderers == null)
        {
            Engage(rig, alpha);
            return;
        }

        if (!Mathf.Approximately(alpha, _appliedAlpha))
            RefreshAlpha(alpha);
    }

    /// <summary>
    /// Put the ORIGINAL shared material arrays (and shadow modes) back and destroy every clone
    /// we made. Null-safe against a subtree that was destroyed under us (hand style switch tears
    /// the hand tree down while the fan may still be open) — a destroyed renderer simply has no
    /// state left to restore, and its clones died with it.
    /// </summary>
    internal void Release()
    {
        if (_renderers != null)
        {
            int restored = 0;
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer r = _renderers[i];
                if (r != null && _originals != null && _shadows != null)
                {
                    r.sharedMaterials = _originals[i];
                    r.shadowCastingMode = _shadows[i];
                    restored++;
                }
                if (_ghosts == null)
                    continue;
                Material[] clones = _ghosts[i];
                for (int m = 0; m < clones.Length; m++)
                {
                    if (clones[m] != null)
                        Object.Destroy(clones[m]);
                }
            }
            VRLog.Info("Hands", $"Ghost hand OFF ({_label}) — was alpha {_appliedAlpha:0.00} " +
                                $"(strength {(1f - _appliedAlpha) * 100f:0}%); {restored}/{_renderers.Length} " +
                                "renderer(s) restored to their original shared materials, " +
                                "every cloned material destroyed.");
        }

        _rig = null;
        _renderers = null;
        _originals = null;
        _ghosts = null;
        _shadows = null;
        _appliedAlpha = -1f;
    }

    // ---- engage / refresh -------------------------------------------------------------------

    private void Engage(HandRig rig, float alpha)
    {
        _rig = rig; // remembered even when nothing was found, so we scan once, not per frame

        var found = new List<Renderer>(24);
        foreach (Renderer r in rig.Root.GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            if (r == null)
                continue;
            // VFX-family renderers have their own blending; fading them is meaningless and can
            // look broken. (No hand style ships one today — this is belt-and-braces.)
            if (r is ParticleSystemRenderer or TrailRenderer or LineRenderer)
                continue;
            // The fan/held object/wrist HUD hang off the hand's sockets — never fade those.
            if (IsAttachment(r.transform, rig))
                continue;
            found.Add(r);
        }

        _renderers = found.ToArray();
        _originals = new Material[_renderers.Length][];
        _ghosts = new Material[_renderers.Length][];
        _shadows = new UnityEngine.Rendering.ShadowCastingMode[_renderers.Length];

        var shaders = new HashSet<string>();
        int tinted = 0;
        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer r = _renderers[i];
            Material[] source = r.sharedMaterials;
            _originals[i] = source;
            _shadows[i] = r.shadowCastingMode;

            var clones = new Material[source.Length];
            for (int m = 0; m < source.Length; m++)
            {
                Material src = source[m];
                if (src == null)
                    continue;
                if (src.shader != null)
                    shaders.Add(src.shader.name);
                var clone = new Material(src)
                {
                    name = src.name + " (GloomhavenVR ghost)",
                    // Never let a clone leak into a scene save / the editor's asset list; it is
                    // ours for exactly as long as the ghost is engaged.
                    hideFlags = HideFlags.HideAndDontSave,
                };
                MakeTransparent(clone);
                if (SetAlpha(clone, alpha))
                    tinted++;
                clones[m] = clone;
            }

            _ghosts[i] = clones;
            r.sharedMaterials = clones;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        _appliedAlpha = alpha;
        VRLog.Info("Hands", $"Ghost hand ON ({_label}) — alpha {alpha:0.00} " +
                            $"(strength {(1f - alpha) * 100f:0}%), {_renderers.Length} renderer(s) " +
                            $"cloned onto private materials, {tinted} material(s) tinted; " +
                            $"shaders: {(shaders.Count > 0 ? string.Join(", ", shaders) : "none")}.");
    }

    private void RefreshAlpha(float alpha)
    {
        if (_ghosts == null)
            return;
        for (int i = 0; i < _ghosts.Length; i++)
        {
            Material[] clones = _ghosts[i];
            for (int m = 0; m < clones.Length; m++)
            {
                if (clones[m] != null)
                    SetAlpha(clones[m], alpha);
            }
        }
        _appliedAlpha = alpha;
    }

    // ---- helpers ----------------------------------------------------------------------------

    /// <summary>Layer the mod's UI widgets live on — see <see cref="IsAttachment"/>.</summary>
    private const int UiLayer = 5;

    /// <summary>
    /// True when <paramref name="t"/> hangs off one of the hand's attachment SOCKETS rather than
    /// being hand geometry. The card fan (<see cref="CardFan.Open"/> parents its root to
    /// <c>Rig.PalmCenter</c>), a grabbed card/figure (<c>GrabAnchor</c>) and the wrist HUD all
    /// live there — and for a glove PREFAB those sockets sit inside the hand subtree, so a plain
    /// <c>GetComponentsInChildren</c> would sweep them up and fade exactly the content the ghost
    /// exists to reveal.
    ///
    /// ROOT CAUSE FIX (user: "die Geisterhand funktioniert nicht, die Hand wird immer noch genauso
    /// angezeigt obwohl die Option an ist und der Fächer auf"). The hardware log named it exactly:
    ///   Ghost hand ON (local Left) — alpha 0.45 …, 1 renderer(s) cloned …
    /// ONE renderer. The sweep was excluding almost the entire hand, so of course nothing looked
    /// different. The reason is that <see cref="HandRig.Wrist"/> is NOT a socket at all in the
    /// shipped rigs: <c>HandVisuals</c> assigns <c>rig.Wrist = handRoot</c> for the procedural hand
    /// and falls back to the whole prefab instance for a glove. Testing "is any ancestor the wrist"
    /// therefore matched EVERY renderer in the hand, and the <c>rig.Root</c> escape below could
    /// never be reached because the wrist test ran first and the two are the same object.
    ///
    /// So: the hand ROOT is checked FIRST — reaching it means we walked up through nothing but hand
    /// geometry — and the wrist only counts as a socket when it is genuinely a separate node. The
    /// wrist HUD, which really does hang off the wrist and must stay solid, is excluded by its own
    /// identity instead: it is a mod-owned widget on the UI layer (<see cref="UiLayer"/>, set in
    /// WristHud.Build), which no hand mesh ever uses. That is a property of the thing itself rather
    /// than of where it happens to be parented, so it keeps working whatever the rig's shape.
    /// </summary>
    private static bool IsAttachment(Transform t, HandRig rig)
    {
        // Mod UI riding the hand (the wrist HUD and anything it spawns): never hand geometry.
        if (t.gameObject.layer == UiLayer)
            return true;

        for (Transform? c = t; c != null; c = c.parent)
        {
            // Checked FIRST: with rig.Wrist == rig.Root (the shipped case) this is what tells us
            // we walked up through hand geometry only. Getting the order wrong excluded the hand.
            if (ReferenceEquals(c, rig.Root))
                return false;
            if (ReferenceEquals(c, rig.PalmCenter) || ReferenceEquals(c, rig.GrabAnchor))
                return true;
            // Only a wrist that is a distinct node is an attachment socket; when it IS the hand
            // root the branch above has already returned.
            if (ReferenceEquals(c, rig.Wrist) && !ReferenceEquals(rig.Wrist, rig.Root))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Switch a CLONED material into an alpha-blended rendering mode. Unity's Standard shader
    /// ignores the colour's alpha entirely while it is in Opaque mode: the blend factors, the
    /// depth write, the shader keywords AND the render queue all have to be flipped together
    /// (this is exactly what the Standard shader's own inspector does when you pick "Fade").
    /// Fade — not Transparent — is the right preset for a ghost: it dims the WHOLE surface
    /// including highlights, which is what "the hand is barely there" should look like.
    /// Everything is guarded by <c>HasProperty</c>, so the unlit <c>Sprites/Default</c> fallback
    /// (already premultiplied-alpha blended with ZWrite off) passes through untouched and only
    /// gets its colour tinted.
    /// </summary>
    private static void MakeTransparent(Material m)
    {
        if (m.HasProperty(ModeId))
            m.SetFloat(ModeId, 2f); // Standard: 0 Opaque, 1 Cutout, 2 Fade, 3 Transparent
        if (m.HasProperty(SurfaceId))
            m.SetFloat(SurfaceId, 1f); // URP Lit/Unlit: 0 Opaque, 1 Transparent
        if (m.HasProperty(SrcBlendId))
            m.SetInt(SrcBlendId, (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (m.HasProperty(DstBlendId))
            m.SetInt(DstBlendId, (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty(ZWriteId))
            m.SetInt(ZWriteId, 0);

        m.DisableKeyword("_ALPHATEST_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.EnableKeyword("_ALPHABLEND_ON");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); // URP counterpart of _ALPHABLEND_ON

        // Draw after the opaque queue so whatever is behind the hand (the cards!) is already in
        // the frame buffer to blend against. Only ever raised — a material that already lives in
        // the transparent range (the unlit fallback) keeps its own queue.
        if (m.renderQueue < (int)UnityEngine.Rendering.RenderQueue.Transparent)
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    /// <summary>Write <paramref name="alpha"/> into the material's colour. True when a colour
    /// property existed (false = this shader has no tint we can fade — reported in the log).</summary>
    private static bool SetAlpha(Material m, float alpha)
    {
        for (int i = 0; i < ColorIds.Length; i++)
        {
            if (!m.HasProperty(ColorIds[i]))
                continue;
            Color c = m.GetColor(ColorIds[i]);
            c.a = Mathf.Clamp01(alpha);
            m.SetColor(ColorIds[i], c);
            return true;
        }
        return false;
    }
}

/// <summary>
/// POLICY side of the ghost hand: reads the two [Hands] config entries and drives the LOCAL
/// pair of <see cref="HandGhost"/>s once per frame from <see cref="HandsDriver"/>.
///
/// Rule: while the palm card fan is OPEN, the hand it is open ON becomes semi-transparent.
/// The fan publishes both facts itself (<see cref="CardFan.Current"/> is non-null only while
/// open, and <see cref="CardFan.Hand"/> names the hand), so there is no extra state to keep in
/// sync and closing the fan — for ANY reason, including a teardown — releases the ghost on the
/// next tick.
///
/// <see cref="LocalSide"/> is the single source of truth the other two renderings of this same
/// player read: <see cref="WorldUI.AvatarMirror"/> ghosts the mirrored hand from it, and
/// <see cref="Net.NetAvatarDriver"/> stamps it (plus <see cref="Strength"/>) onto the extras
/// packet so peers fade the matching hand of our remote avatar. Everything is config-guarded:
/// with the toggle off nothing is ever scanned, cloned or transmitted.
/// </summary>
internal static class HandGhosts
{
    private static readonly HandGhost LeftGhost = new("local Left");
    private static readonly HandGhost RightGhost = new("local Right");

    /// <summary>Which hand is ghosted right now (null = none). Read by the mirror and the net
    /// sender so all three renderings of this player agree.</summary>
    internal static HandSide? LocalSide { get; private set; }

    /// <summary>The ghost-hand feature toggle ([Hands] GhostHandOnFan); false before Bind.</summary>
    internal static bool Enabled
    {
        get
        {
            try
            {
                return HandsConfig.GhostHandOnFan != null && HandsConfig.GhostHandOnFan.Value;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Transparency STRENGTH 0..1 ([Hands] GhostHandStrength; higher = more
    /// see-through), clamped so the hand always stays visible.</summary>
    internal static float Strength
    {
        get
        {
            try
            {
                return HandsConfig.GhostHandStrength != null
                    ? Mathf.Clamp(HandsConfig.GhostHandStrength.Value, MinStrength, MaxStrength)
                    : DefaultStrength;
            }
            catch
            {
                return DefaultStrength;
            }
        }
    }

    internal const float DefaultStrength = 0.55f;
    internal const float MinStrength = 0.05f;
    internal const float MaxStrength = 0.95f;

    /// <summary>Material alpha for a given strength (1 = opaque). Shared by the mirror and the
    /// receiving side of the wire so the fade reads identically everywhere.</summary>
    internal static float AlphaFor(float strength) =>
        Mathf.Clamp01(1f - Mathf.Clamp(strength, MinStrength, MaxStrength));

    /// <summary>Material alpha of the LOCAL ghost.</summary>
    internal static float Alpha => AlphaFor(Strength);

    /// <summary>
    /// Per-frame policy step (called under <see cref="TickGuard"/> from
    /// <see cref="HandsDriver"/>): ghost the hand whose fan is open, release the other.
    /// </summary>
    internal static void Tick()
    {
        VRHand? fanHand = null;
        if (Enabled)
        {
            CardFan? fan = CardFan.Current; // non-null only while a fan is OPEN
            if (fan != null && fan.IsOpen)
                fanHand = fan.Hand;
        }

        HandSide? side = fanHand != null ? fanHand.Side : null;
        LocalSide = side;

        float alpha = Alpha;
        LeftGhost.Apply(side == HandSide.Left ? fanHand!.Rig : null, alpha);
        RightGhost.Apply(side == HandSide.Right ? fanHand!.Rig : null, alpha);
    }

    /// <summary>Module shutdown / hot reload: restore both hands unconditionally.</summary>
    internal static void Shutdown()
    {
        LocalSide = null;
        LeftGhost.Release();
        RightGhost.Release();
    }
}
