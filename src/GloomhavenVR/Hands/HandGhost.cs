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

    /// <summary>
    /// Render queue of every ghost material — deliberately ABOVE the transparent default (3000),
    /// where the card face canvases live.
    ///
    /// <para>At 3000 the ghost hand and a held card's face art shared a queue AND a sorting order
    /// (both 0), so Unity fell back to per-renderer CENTER DISTANCE — and for two interpenetrating
    /// objects that order flips with the wrist angle. Card center nearer: the card draws last and
    /// blanks EVERY ghost pixel, fingers in front included. Hand center nearer: everything pops
    /// back. That is precisely the reported "verschwindet bei einem gewissen Winkel und ploppt
    /// wieder auf" — a binary sort flip, not a fade.</para>
    ///
    /// <para>Above the card's queue the order is deterministic: the ghost always draws AFTER the
    /// card, and correctness comes from DEPTH instead of luck — the card's opaque backing slab
    /// (Standard shader, ZWrite on) is already in the depth buffer, so ghost pixels BEHIND the
    /// card fail the ZTest and stay hidden while pixels IN FRONT pass and stay visible. Per-pixel,
    /// at every angle. 3100 keeps the ghost well under the board-widget tiers (4003+).</para>
    /// </summary>
    private const int GhostRenderQueue = 3100;

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
                            $"shaders: {(shaders.Count > 0 ? string.Join(", ", shaders) : "none")}" +
                            (s_swappedShader
                                ? " — at least one had NO blend state (hard-coded opaque) and its CLONE was " +
                                  "re-shadered to an unlit alpha-blended one, texture + tint carried over; " +
                                  "that is what makes the fade visible at all."
                                : " — all blendable as shipped.") + ".");
        s_swappedShader = false; // per-engage report, not a sticky flag
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
        // ROUND 2 ROOT CAUSE (user: "Geisterhand hat immer noch keinen Einfluss"). The renderer
        // exclusion fixed in the previous round was only half the story. The hardware log of the
        // build that carried that fix still says:
        //   Ghost hand ON (local Left) — alpha 0.45 …, 1 renderer(s) cloned …; shaders: GloomhavenVR/BoardLit
        // and this time the "1" is CORRECT: the shipped glove is a single (skinned) renderer, not
        // the ~40-primitive procedural hand. What is wrong is the shader. Every knob below is
        // guarded by HasProperty, and GloomhavenVR/BoardLit — a bundled OPAQUE shader — exposes
        // none of them: no _Mode, no _Surface, no _SrcBlend/_DstBlend, no _ZWrite. So the whole
        // recipe silently no-opped, SetAlpha dutifully wrote alpha into a colour the shader never
        // blends with, and the hand rendered exactly as before. "1 material(s) tinted" in the log
        // was reporting a write that could not possibly show.
        //
        // A shader with no blend state cannot be made to fade by setting properties — the fix has
        // to REPLACE it. We are working on a private clone (see Engage), so swapping its shader is
        // as reversible as everything else here: the original material is untouched and restored
        // wholesale on release. Sprites/Default is the right target: unlit and alpha-blended, and
        // already the proven choice for hands in this project (CreateHandMaterial picks it for the
        // procedural hand precisely because the VR void and the menu scenes have NO lights, so a
        // lit shader renders the hand pitch black). The base map and tint are carried across so the
        // ghost keeps the glove's own colour rather than turning into a white silhouette.
        if (!CanBlend(m))
            SwapToBlendableShader(m);

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

        // Draw after the opaque queue AND after the card face canvases (see GhostRenderQueue —
        // sharing their queue made visibility flip with the wrist angle). Only ever raised — a
        // material already at or above the ghost tier keeps its own queue.
        if (m.renderQueue < GhostRenderQueue)
            m.renderQueue = GhostRenderQueue;
    }

    /// <summary>
    /// Can this material's shader blend at all? A shader that exposes neither the Standard/URP
    /// surface-mode switch nor the raw blend factors has its blending HARD-CODED (opaque, in every
    /// case we ship), so no amount of property writing will ever fade it — see
    /// <see cref="MakeTransparent"/> for how that produced a ghost hand that logged success and
    /// changed nothing.
    /// </summary>
    private static bool CanBlend(Material m) =>
        m.HasProperty(ModeId) || m.HasProperty(SurfaceId)
        || (m.HasProperty(SrcBlendId) && m.HasProperty(DstBlendId));

    /// <summary>Names of the texture slot a swapped-in shader should inherit, in probe order.</summary>
    private static readonly int[] MainTexIds =
    {
        Shader.PropertyToID("_MainTex"),
        Shader.PropertyToID("_BaseMap"),
        Shader.PropertyToID("_BaseColorMap"),
    };

    /// <summary>
    /// Replace a CLONE's un-blendable shader with an unlit alpha-blended one, carrying the base
    /// texture and tint across so the ghost still looks like the hand it came from. Only ever
    /// called on a material this class created (never a shared asset), and undone by the wholesale
    /// material restore in <see cref="Release"/>. No-op when no blendable shader can be found, in
    /// which case the engage log's shader list is the evidence.
    /// </summary>
    private static void SwapToBlendableShader(Material m)
    {
        Shader? target = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default")
                         ?? Shader.Find("Unlit/Transparent");
        if (target == null)
            return;

        // Read the look BEFORE the shader swap — property ids resolve against the current shader.
        Texture? tex = null;
        for (int i = 0; i < MainTexIds.Length && tex == null; i++)
        {
            if (m.HasProperty(MainTexIds[i]))
                tex = m.GetTexture(MainTexIds[i]);
        }
        Color tint = Color.white;
        for (int i = 0; i < ColorIds.Length; i++)
        {
            if (!m.HasProperty(ColorIds[i]))
                continue;
            tint = m.GetColor(ColorIds[i]);
            break;
        }

        m.shader = target;
        if (tex != null && m.HasProperty(MainTexIds[0]))
            m.SetTexture(MainTexIds[0], tex);
        if (m.HasProperty(ColorIds[0]))
            m.SetColor(ColorIds[0], tint); // alpha is written right after, by SetAlpha
        s_swappedShader = true;
    }

    /// <summary>True once a shader swap has happened — folded into the engage log so a hardware
    /// log states plainly WHY the hand can fade at all.</summary>
    private static bool s_swappedShader;

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

    /// <summary>Which hand is ghosted right now (null = none). LEGACY single-side view — kept for
    /// the wire's original ghost flag and the mirror; when both hands are ghosted it names the fan
    /// side. The full truth is <see cref="LocalSidesMask"/>.</summary>
    internal static HandSide? LocalSide { get; private set; }

    internal static bool LocalLeft { get; private set; }
    internal static bool LocalRight { get; private set; }

    /// <summary>Bitmask of ghosted hands (NetProtocol.GhostSideLeftBit/RightBit) for the wire's
    /// extension record — the representation that can say "the dominant one" or "both".</summary>
    internal static byte LocalSidesMask =>
        (byte)((LocalLeft ? Net.NetProtocol.GhostSideLeftBit : 0)
               | (LocalRight ? Net.NetProtocol.GhostSideRightBit : 0));

    /// <summary>The held-card ghost toggle ([Hands] GhostHandOnHeldCard); false before Bind.</summary>
    internal static bool HeldCardEnabled
    {
        get
        {
            try
            {
                return HandsConfig.GhostHandOnHeldCard != null && HandsConfig.GhostHandOnHeldCard.Value;
            }
            catch
            {
                return false;
            }
        }
    }

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

    internal const float DefaultStrength = Defaults.GhostHandStrength;
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

        // A HELD card ghosts its hand too, when [Hands] GhostHandOnHeldCard says so. Checked per
        // hand rather than as one side, because a held card can be in EITHER hand — or both —
        // while the fan is only ever on one. The card itself never fades: it hangs off a hand
        // socket, and Engage's IsAttachment filter excludes socketed objects by design.
        VRHand? left = VRHands.Left;
        VRHand? right = VRHands.Right;
        bool heldCards = HeldCardEnabled;
        bool ghostLeft = (fanHand != null && fanHand.Side == HandSide.Left)
                         || (heldCards && left != null && left.Grabber.Held is VRCard);
        bool ghostRight = (fanHand != null && fanHand.Side == HandSide.Right)
                          || (heldCards && right != null && right.Grabber.Held is VRCard);

        LocalLeft = ghostLeft;
        LocalRight = ghostRight;
        // The legacy single-side view prefers the fan side — that is what it always meant.
        LocalSide = fanHand != null ? fanHand.Side
            : ghostLeft ? HandSide.Left
            : ghostRight ? HandSide.Right
            : (HandSide?)null;

        float alpha = Alpha;
        LeftGhost.Apply(ghostLeft && left != null ? left.Rig : null, alpha);
        RightGhost.Apply(ghostRight && right != null ? right.Rig : null, alpha);
    }

    /// <summary>Module shutdown / hot reload: restore both hands unconditionally.</summary>
    internal static void Shutdown()
    {
        LocalSide = null;
        LeftGhost.Release();
        RightGhost.Release();
    }
}
