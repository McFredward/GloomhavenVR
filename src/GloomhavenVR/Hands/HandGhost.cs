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
/// the art through. Toggleable per cause ([Hands] GhostHandOnFan / GhostHandOnHeldCard — both
/// ship ON, see Defaults.Hands.cs), strength tunable ([Hands] GhostHandStrength) — see
/// <see cref="HandGhosts"/> for the policy side.
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
/// companion Unity project baked. <see cref="MakeTransparent"/> owns that problem: it flips the
/// whole Standard/URP "Fade" recipe where the shader exposes it, and RE-SHADERS the clone where it
/// does not — a shader with hard-coded opaque blending cannot be faded by writing properties (root
/// cause recorded there). The engage log names the shaders actually found, so an unexpected one is
/// diagnosable from a hardware log instead of guesswork.
///
/// DEPTH: a ghosted hand is alpha-blended, and every recipe <see cref="MakeTransparent"/> can put on
/// it has <c>ZWrite</c> off — so unlike the opaque hand it stamps NO silhouette into the depth
/// buffer, and a world-space uGUI panel drawn after it (which every converted panel is, by
/// sortingOrder) paints straight over the fingers. A second, colour-free BACK-FACE pass restores
/// that silhouette without touching a single pixel the ghost draws; the full root cause, the
/// screenshot measurement behind it and why it is back faces are on
/// <see cref="GhostDepthQueue"/>.
/// </summary>
internal sealed class HandGhost
{
    // Standard-shader (and URP) plumbing, resolved once.
    private static readonly int ModeId = Shader.PropertyToID("_Mode");
    private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
    private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
    private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
    private static readonly int ZTestId = Shader.PropertyToID("_ZTest");
    private static readonly int CullId = Shader.PropertyToID("_Cull");

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

    /// <summary>
    /// Render queue of the ghost's DEPTH PREPASS — one step BEFORE the visible ghost pass, so a
    /// renderer wearing both draws the depth stamp first.
    ///
    /// <para>ROOT CAUSE IT FIXES (user hardware report, ModBuild 348: "Die Geisterhand respektiert
    /// die Perspektive nicht zusammen mit dem Entscheidungstext. Dieser ist dann im Vordergrund.
    /// Das ist nur der Fall mit der Geisterhand, nicht mit der normalen."). The whole lead is in
    /// that last sentence, and the answer is the one thing the ghost takes away from the hand:
    /// ITS DEPTH. <see cref="MakeTransparent"/> flips the clone into an alpha-blended recipe with
    /// <c>ZWrite 0</c>, and the fallback shaders it re-shaders to (<c>Sprites/Default</c>,
    /// <c>UI/Default</c>, <c>Unlit/Transparent</c>) all bake <c>ZWrite Off</c> anyway. A ghosted
    /// hand therefore contributes NOTHING to the depth buffer.</para>
    ///
    /// <para>The take-damage decision panel is a converted world-space uGUI canvas. Unity's UI
    /// shader family declares <c>ZTest [unity_GUIZTestMode]</c>, which on a world-space canvas
    /// resolves to LEqual (see <c>WorldUI.OnTopUiGraphics</c>, which exists to override exactly
    /// that for the ONE popup that needs it — the decision panel is NOT in its list, and the
    /// hardware log's ON-TOP UI lines name only the initiative hover popup). And every converted
    /// panel rides the ladder at <c>CanvasConversion.PanelOrderBase</c> (100) or above, while a
    /// hand renderer sits at <c>sortingOrder</c> 0 — and Unity resolves transparents by
    /// sortingLayer → sortingOrder → renderQueue → distance, so the panel ALWAYS draws after the
    /// ghost whatever queue the ghost is on. With no depth to reject it, the panel paints over the
    /// hand. The NORMAL hand is opaque, draws in the geometry queue and writes depth, so the same
    /// panel is z-rejected over the hand's silhouette — the exact difference the user reports.</para>
    ///
    /// <para>IT IS ALPHA COMPOSITING, NOT A LOST SURFACE, and the screenshot proves it: sampled
    /// across geisterhand-perspektive.jpg the ghost hand adds ~52 luma counts where it is
    /// unoccluded and still ~10 counts THROUGH the button bar, i.e. ~19 % of it survives under a
    /// translucent panel. The hand is painted over, not culled — which is why the fix is to give
    /// the panel something to fail its ZTest against rather than to re-order anything.</para>
    ///
    /// <para>WHY A SEPARATE PASS INSTEAD OF JUST TURNING ZWrite BACK ON. An alpha-blended surface
    /// with ZWrite on self-occludes in triangle order, which for a hand mesh means fingers can
    /// randomly block the palm behind them. The prepass writes depth with NO colour
    /// (<c>Blend Zero One</c>) and renders BACK FACES ONLY (<c>_Cull Front</c>): the visible ghost
    /// pass is left byte-for-byte as it ships — same shader, same premultiplied blend, same
    /// front+back double layer — so the hand-tuned <c>[Hands] GhostHandStrength</c> still means
    /// exactly what it meant before. The depth that lands in the buffer is the hand's FAR shell,
    /// which is nearer than any board-docked panel and farther than anything the player is holding,
    /// so it rejects the panel without touching the layering the ghost exists to preserve.</para>
    ///
    /// <para>3099 keeps the stamp after the card faces (~3000, sortingOrder 0) — those have already
    /// rasterised, so the cards the ghost exists to reveal are unaffected — and one step under the
    /// visible ghost pass.</para>
    /// </summary>
    private const int GhostDepthQueue = 3099;

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
        int depthArmed = 0;
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

            // DEPTH PREPASS (see GhostDepthQueue): one extra, colour-free material on the SAME
            // renderer that stamps the hand's far shell into the depth buffer, so a later-drawn
            // ZTest-LEqual world-space panel is rejected over the hand exactly as it is over the
            // opaque hand. Only for a single-material renderer: with more than one submesh Unity
            // applies a surplus material to the LAST submesh only, which would stamp a partial
            // silhouette — worse than none, because a half-covered hand reads as a glitch.
            Material[] assigned = clones;
            if (clones.Length == 1 && clones[0] != null)
            {
                Material? depth = MakeDepthPrepass();
                if (depth != null)
                {
                    assigned = new[] { clones[0], depth };
                    depthArmed++;
                }
            }

            _ghosts[i] = assigned;
            r.sharedMaterials = assigned;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        _appliedAlpha = alpha;
        ReportDepthPrepassOnce(depthArmed, _renderers.Length);
        VRLog.Info("Hands", $"Ghost hand ON ({_label}) — alpha {alpha:0.00} " +
                            $"(strength {(1f - alpha) * 100f:0}%), {_renderers.Length} renderer(s) " +
                            $"cloned onto private materials, {tinted} material(s) tinted, " +
                            $"{depthArmed}/{_renderers.Length} carrying the back-face DEPTH PREPASS " +
                            "(without it a world-space panel drawn after the ghost has no depth to " +
                            "fail against and paints over the hand); " +
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

    // ---- depth prepass ------------------------------------------------------------------------

    /// <summary>One-shot session report per OUTCOME (see <see cref="ReportDepthPrepassOnce"/>).
    /// Two flags, not one: the bundle loads asynchronously, so the very first ghost of a session can
    /// legitimately find no shader, and a single latch would freeze the log on that "not armed"
    /// verdict for the whole run — a held instrument reading as dead.</summary>
    private static bool s_depthArmedReported;
    private static bool s_depthMissingReported;

    /// <summary>
    /// The one shader in reach that lets a material be told to WRITE DEPTH: the bundled
    /// <c>GloomhavenVR/Overlay</c> exposes <c>_ZWrite</c>, <c>_ZTest</c>, <c>_Cull</c> and the two
    /// blend factors as real properties, which is precisely why it was written (its own header
    /// records the three rounds lost to <c>Sprites/Default</c>/<c>Standard</c> baking those states
    /// in). Resolved through <see cref="Core.BundleShaders"/>, never a bare <c>Shader.Find</c> — a
    /// bundled shader is not discoverable by name until something loads it, a trap this project has
    /// paid for twice.
    ///
    /// <para>Asked EVERY time rather than latched: the bundle loads asynchronously, so a miss on the
    /// first ghost of a session is a normal timing answer and not a verdict.
    /// <see cref="Core.BundleShaders"/> caches SUCCESSES only and logs its miss once, so re-asking
    /// is a dictionary hit once the bundle is up and silent before that.</para>
    /// </summary>
    private static Shader? DepthPrepassShader()
    {
        return Core.BundleShaders.Resolve(
            "GloomhavenVR/Overlay", "Hands",
            "the ghost hand gets a colour-free back-face depth prepass, so a world-space panel "
            + "drawn after it (the take-damage decision text) is z-rejected over the hand instead "
            + "of painting over it.",
            "The ghost hand cannot write depth at all and a world-space panel keeps painting over "
            + "it — the ModBuild 348 'Geisterhand respektiert die Perspektive nicht' report.");
    }

    /// <summary>
    /// A material that draws NOTHING and writes DEPTH: <c>Blend Zero One</c> (the destination is
    /// returned unchanged, so the colour buffer cannot tell this pass ran), <c>ZWrite 1</c>,
    /// <c>ZTest LEqual</c> and <c>_Cull Front</c> — back faces only, so the depth that lands is the
    /// hand's FAR shell and the visible ghost pass, which still has <c>ZWrite</c> off and tests
    /// LEqual, is completely unaffected by it. See <see cref="GhostDepthQueue"/> for why that
    /// choice is what keeps the shipped look identical.
    ///
    /// <para>Owned exactly like every other clone here: <c>HideAndDontSave</c>, stored in
    /// <c>_ghosts</c> and destroyed by <see cref="Release"/> with the rest.</para>
    /// </summary>
    private static Material? MakeDepthPrepass()
    {
        Shader? s = DepthPrepassShader();
        if (s == null)
            return null;
        var m = new Material(s)
        {
            name = "GloomhavenVR ghost hand (depth prepass)",
            hideFlags = HideFlags.HideAndDontSave,
        };
        if (m.HasProperty(SrcBlendId))
            m.SetInt(SrcBlendId, (int)UnityEngine.Rendering.BlendMode.Zero);
        if (m.HasProperty(DstBlendId))
            m.SetInt(DstBlendId, (int)UnityEngine.Rendering.BlendMode.One);
        if (m.HasProperty(ZWriteId))
            m.SetInt(ZWriteId, 1);
        if (m.HasProperty(ZTestId))
            m.SetInt(ZTestId, (int)UnityEngine.Rendering.CompareFunction.LessEqual);
        if (m.HasProperty(CullId))
            m.SetInt(CullId, (int)UnityEngine.Rendering.CullMode.Front);
        m.renderQueue = GhostDepthQueue;
        return m;
    }

    /// <summary>
    /// Say ONCE per session whether the prepass is actually on the hand — the single fact the next
    /// hardware test has to answer for the "Entscheidungstext im Vordergrund" report. Printed at the
    /// shipped default level deliberately: the engage line beside it is DEBUG, and a co-player
    /// running at INFO would otherwise send back a log that answers nothing.
    /// </summary>
    private static void ReportDepthPrepassOnce(int armed, int renderers)
    {
        if (armed > 0 ? s_depthArmedReported : s_depthMissingReported)
            return;
        if (armed > 0)
            s_depthArmedReported = true;
        else
            s_depthMissingReported = true;
        // HW-VERIFY
        VRLog.Note("Hands", armed > 0
            ? $"GHOST HAND DEPTH: prepass armed on {armed} of {renderers} ghosted renderer(s) — a "
              + "colour-free back-face pass at renderQueue " + GhostDepthQueue + " on the bundled "
              + "'GloomhavenVR/Overlay' shader (Blend Zero One, ZWrite 1, ZTest LEqual, Cull Front). "
              + "The ghost hand now stamps a depth silhouette again, so the take-damage decision "
              + "panel — a world-space uGUI canvas at ZTest LEqual and sortingOrder >= 100, i.e. "
              + "always drawn AFTER the hand — is rejected over the hand instead of painting over "
              + "it. THE TEST: open the fan, trigger the damage prompt, and hold the ghosted hand "
              + "between the eye and the decision buttons; the hand must cover them exactly as the "
              + "normal hand does. The ghost's own look must be UNCHANGED (same strength, fingers "
              + "still layering over each other) — the prepass writes no colour."
            : $"GHOST HAND DEPTH: prepass NOT armed ({armed} of {renderers} ghosted renderer(s)) — "
              + (renderers == 0
                  ? "no renderer was ghosted at all, so this line says nothing about the shader."
                  : "either 'GloomhavenVR/Overlay' did not resolve (see the BUNDLED SHADER line "
                    + "above) or every ghosted renderer carries more than one material, which "
                    + "cannot take a surplus pass without stamping a partial silhouette. ")
              + "The ghost hand writes no depth, so a world-space panel drawn after it will keep "
              + "painting over it — the ModBuild 348 report is expected to REPRODUCE.");
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
    /// different. The wrist was tested FIRST, and <see cref="HandRig.Wrist"/> was then the hand
    /// root itself (<c>HandVisuals</c> assigns <c>rig.Wrist = handRoot</c> for the procedural hand,
    /// and falls back to the whole prefab instance for a glove without an <c>Anchor_Wrist</c>), so
    /// "is any ancestor the wrist" matched EVERY renderer in the hand and the <c>rig.Root</c>
    /// escape below could never be reached.
    ///
    /// So: the hand ROOT is checked FIRST — reaching it means we walked up through nothing but hand
    /// geometry — and the wrist only counts as a socket when it is genuinely a separate node. (At
    /// HEAD it always is: <c>HandVisuals</c> interposes a zero-offset <c>Socket_Wrist</c> child. The
    /// order and the wrist!=root guard stay anyway — they cost nothing and are what makes this
    /// correct for a rig of any shape.) The wrist HUD, which really does hang off the wrist and must
    /// stay solid, is excluded by its own identity instead: it is a mod-owned widget on the UI layer
    /// (<see cref="UiLayer"/>, set in WristHud.Build), which no hand mesh ever uses. That is a
    /// property of the thing itself rather than of where it happens to be parented, so it keeps
    /// working whatever the rig's shape.
    /// </summary>
    private static bool IsAttachment(Transform t, HandRig rig)
    {
        // Mod UI riding the hand (the wrist HUD and anything it spawns): never hand geometry.
        if (t.gameObject.layer == UiLayer)
            return true;

        for (Transform? c = t; c != null; c = c.parent)
        {
            // Checked FIRST: reaching the root means we walked up through hand geometry only.
            // Getting this order wrong once excluded the whole hand (see the doc above).
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
    /// Everything is guarded by <c>HasProperty</c>; a shader that exposes none of the knobs has
    /// its blending hard-coded and is re-shadered instead (root cause below).
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
        // blends with, and "1 material(s) tinted" reported a write that could not possibly show.
        //
        // A shader with no blend state cannot be made to fade by setting properties — the fix has
        // to REPLACE it, on our private clone (see Engage), which is as reversible as everything
        // else here. Sprites/Default is the right target: unlit and alpha-blended, and already the
        // proven choice for hands here (CreateHandMaterial picks it for the procedural hand because
        // the VR void and the menu scenes have NO lights, so a lit shader renders the hand pitch
        // black). Base map and tint carry across so the ghost keeps the glove's own colour rather
        // than turning into a white silhouette.
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
    /// case we ship), so no amount of property writing will ever fade it — see the root cause on
    /// <see cref="MakeTransparent"/>.
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
/// <see cref="LocalLeft"/>/<see cref="LocalRight"/> are the single source of truth the other
/// two renderings of this same player read: <see cref="WorldUI.AvatarMirror"/> ghosts the
/// mirrored hands from them per side, and <see cref="Net.NetAvatarDriver"/> stamps their mask
/// (plus <see cref="Strength"/>) onto the extras packet so peers fade the matching hands of our
/// remote avatar. Everything is config-guarded: with the toggle off nothing is ever scanned,
/// cloned or transmitted.
/// </summary>
internal static class HandGhosts
{
    private static readonly HandGhost LeftGhost = new("local Left");
    private static readonly HandGhost RightGhost = new("local Right");

    /// <summary>Which hand is ghosted right now (null = none). LEGACY single-side view — kept
    /// ONLY for the wire's original ghost flag (<c>extras.GhostHand</c>); when both hands are
    /// ghosted it names the fan side. The full truth is <see cref="LocalLeft"/>/<see
    /// cref="LocalRight"/> (mask form: <see cref="LocalSidesMask"/>) — the mirror used to read
    /// THIS and therefore could never ghost a second (held-card) hand; see the 2026-08-11 note
    /// in <see cref="WorldUI.AvatarMirror"/>.Tick.</summary>
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
        // …EXCEPT WHEN THE HAND IS REALLY HOLDING IT (user 2026-08-29, verbatim: "Die Geisterhand
        // soll nur normalen Modus sein"). The ghost exists to stop the hand mesh covering a card
        // the player is trying to READ, and in the reading mode that is exactly what the hand is
        // doing — it is a carrier, and the card is billboarded past it at the head. The in-hand
        // mode inverts the premise: the hand is not in the way of the card, the hand IS the point.
        // The player is showing a card to somebody, the fingers are closed on its bottom edge in a
        // modelled grip, and fading them turns a held card into a floating one. LocalLeft/LocalRight
        // carry this all the way out, so the mirror and every peer's copy of these hands stop
        // fading with them — a peer must see a solid hand holding the card they are being shown.
        bool heldCards = HeldCardEnabled;
        // AT THE HALFWAY MARK of the grasp, not at its start. The ghost is a material swap and has
        // no midpoint — it cannot fade along with the fingers — so the only choice is WHERE to put
        // the one discontinuity, and the least conspicuous place is the middle of a motion that is
        // already carrying the eye.
        bool ghostLeft = (fanHand != null && fanHand.Side == HandSide.Left)
                         || (heldCards && left != null && IsHeldCard(left.Grabber.Held)
                             && !HeldCardGrip.PastHalf(HandSide.Left));
        bool ghostRight = (fanHand != null && fanHand.Side == HandSide.Right)
                          || (heldCards && right != null && IsHeldCard(right.Grabber.Held)
                              && !HeldCardGrip.PastHalf(HandSide.Right));

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

    /// <summary>
    /// "Is the thing this hand holds a CARD?" — the held-card ghost gate's predicate. ROOT CAUSE
    /// this exists (user: the ghost hand appears for a held ability card but NEVER for a held
    /// ITEM card): the gate used to be the literal type test <c>Held is VRCard</c>, and an item
    /// card is not a VRCard — it is <see cref="ItemsPile.ItemChip"/>, a separate grabbable that
    /// hosts the game's own ItemCardUI. Both are "a card lifted in front of the face to read",
    /// which is precisely the occlusion the ghost exists to relieve, so both must pass the SAME
    /// gate: same config toggle, same strength, and — because <see cref="LocalLeft"/>/<see
    /// cref="LocalRight"/> feed the mirror and the net-extras mask unchanged — the same mirror
    /// and remote-avatar fade a held ability card already gets. Type checks, not a capability
    /// interface: exactly two card grabbables exist and each is named here on purpose, so a NEW
    /// grabbable (figures, tokens) can never start ghosting hands by accident.
    /// </summary>
    private static bool IsHeldCard(Interact.IGrabbable? held) =>
        held is VRCard or ItemsPile.ItemChip;

    /// <summary>Module shutdown / hot reload: restore both hands unconditionally.</summary>
    internal static void Shutdown()
    {
        LocalSide = null;
        LeftGhost.Release();
        RightGhost.Release();
    }
}
