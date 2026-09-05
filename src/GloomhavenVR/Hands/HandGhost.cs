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
/// DEPTH AND ORDER: a ghosted hand is alpha-blended, and every recipe <see cref="MakeTransparent"/>
/// can put on it has <c>ZWrite</c> off — so unlike the opaque hand it stamps NO silhouette into the
/// depth buffer, and a world-space uGUI panel drawn after it (which every converted panel is, by
/// sortingOrder) would paint straight over the fingers. That is settled by DRAW ORDER, not by a
/// depth stamp: while engaged, every ghosted renderer is ranked on the converted-panel distance
/// ladder from the hand's own eye distance, so it draws after everything measurably behind it and
/// before everything in front — and gets there by BLENDING, which is what lets the panel read
/// through the hand instead of being rejected by it. The full root cause of both halves, and the
/// depth prepass this replaced, are on <see cref="GhostPanelLift"/>.
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
    /// <c>GloomhavenVR/BoardLit</c>'s OPACITY SCALAR — and the whole of the ModBuild 392 ghost
    /// regression.
    ///
    /// <para>ROOT CAUSE (user 2026-09-03: "Obwohl die Geisterhand an ist, funktioniert sie nicht
    /// mehr … jetzt sind beide Hände aber keine Geisterhand mehr, weder beim Fächer noch wenn ich
    /// eine Karte grabbe", and his own guess at WHEN — "seit dem letzten Problem das ich damit
    /// gemeldet habe" — is exactly right: it is the SAME COMMIT, c1cadde0.) That commit gave
    /// BoardLit an opt-in transparency block for <c>Net.Board.PeerBoardFade</c>: <c>_SrcBlend</c>,
    /// <c>_DstBlend</c>, <c>_ZWrite</c> and this scalar, with the pass rewritten to
    /// <c>Blend [_SrcBlend] [_DstBlend]</c>. Correct for the board. Fatal here, for two reasons
    /// that only bite together:</para>
    ///
    /// <para>(1) <see cref="CanBlend"/> is a PROPERTY-EXISTENCE test, and BoardLit now has
    /// <c>_SrcBlend</c> AND <c>_DstBlend</c>. So it flipped from false to true, and
    /// <see cref="SwapToBlendableShader"/> — the re-shader onto <c>Sprites/Default</c> that was
    /// the ONLY thing making this hand fade at all — stopped being called. The engage log went
    /// from naming the swap to saying "all blendable as shipped", which is true about the shader's
    /// property list and says nothing whatsoever about the picture.</para>
    ///
    /// <para>(2) BoardLit's fragment ends <c>return fixed4(col, _FadeAlpha)</c>. It never reads
    /// <c>_Color.a</c> — deliberately, and its own comment says so, because the alpha channel of
    /// every existing BoardLit material is data nobody had read. <see cref="SetAlpha"/> writes
    /// <c>_Color</c>. So the clone was correctly flipped to <c>Blend SrcAlpha OneMinusSrcAlpha</c>
    /// and then handed a source alpha of exactly 1.0 — i.e. <c>dst*(1-1) + src*1</c>, which is
    /// pixel-for-pixel the opaque hand. "1 material(s) tinted" reported a write that reached a
    /// channel the shader throws away. Both hands present, neither ghosted, at the fan and on a
    /// held card alike — precisely the report.</para>
    ///
    /// <para>THE FIX IS TO DRIVE THE CHANNEL THE SHADER ACTUALLY CONSUMES, not to swap the shader
    /// back: keeping BoardLit keeps the glove's baked studio lighting and its specular across the
    /// fade, which is the same conclusion <c>PeerBoardFade</c> reached for the slab, and it avoids
    /// re-introducing a material swap (a shipped defect class here — the fade-in plop). No bundle
    /// change is involved: this property has been in the shipped bundle since c1cadde0.</para>
    /// </summary>
    private static readonly int FadeAlphaId = Shader.PropertyToID("_FadeAlpha");

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
    ///
    /// <para>SINCE THE LADDER (see <see cref="GhostPanelLift"/>) THIS IS THE TIE-BREAK, NOT THE
    /// DECISION. Unity resolves transparents by sortingLayer → sortingOrder → renderQueue →
    /// distance, and the ghost's sortingOrder is now written every frame, so the queue only decides
    /// against surfaces that land on the SAME order. It is kept exactly as it was because that case
    /// is real — a card face canvas sits at order 0 and the ladder's floor for a hand with no panel
    /// behind it is 96+ — and because nothing here is worth re-tuning: the queue costs nothing and
    /// the angle-flip it removed is a shipped defect.</para>
    /// </summary>
    private const int GhostRenderQueue = 3100;

    /// <summary>
    /// The ghost hand's LIFT above the converted-panel distance ladder. It must stay under
    /// <c>CanvasConversion.PanelOrderStep</c> (16) so the hand can never climb into the NEXT
    /// panel's slot, and it must clear every registered panel decoration (close X +2, grab bar +4,
    /// badge +5, menu-laid tooltip +10) — a hand held in front of a window covers that window's
    /// furniture too.
    ///
    /// <para>ONE BELOW THE FREE-PLATE FAMILY (12: <c>WorldUI.FreeLabelOrder.LabelPanelLift</c>,
    /// <c>Cards.Art.CardGlow.CuePanelLift</c>, <c>Net.Board.BoardVisual.TagPanelLift</c>,
    /// <c>WorldUI.WristHud.PanelLift</c>), and that ONE is the whole tie rule. Those plates are
    /// annotations — a pile fan's title, a card cue, a peer's name tag, the wrist HUD — and two
    /// subjects inside the same ladder slot have distances the ladder has already declined to
    /// separate, so the tie has to be decided by what each thing IS rather than by where it is.
    /// This report is what decides it: the complaint is that text vanishes behind the ghosted hand,
    /// so on a tie the readable thing wins and the hand goes under it. 11 still clears the panel
    /// slot itself and every decoration on it, which is the guarantee the lift exists for.</para>
    ///
    /// <para>ROOT CAUSE IT FIXES (user hardware report, 2026-09-03, <c>Geisterhand_problem.jpg</c>:
    /// "Die Geisterhände sollen ganz normal Perspektive Respektieren, aber das sie transparent sind
    /// soll man auch alles dahinter sehen können. Aktuell verschwinden Elemente des Controllboards
    /// wie die Initiativreihenfolge, die Healthbars oder der Text und die Nummer bei den Piles").
    /// ModBuild 352 answered the OPPOSITE report ("Die Geisterhand respektiert die Perspektive nicht
    /// zusammen mit dem Entscheidungstext. Dieser ist dann im Vordergrund.") with a colour-free
    /// back-face depth prepass at renderQueue 3099 (<c>Blend Zero One</c>, <c>ZWrite 1</c>,
    /// <c>Cull Front</c>, on the bundled <c>GloomhavenVR/Overlay</c>): the ghost stamped its
    /// silhouette into the depth buffer so a later-drawn ZTest-LEqual panel was z-rejected over the
    /// hand. It did exactly that. It was also never seen doing it, because the hand was opaque for
    /// its whole life — <c>_FadeAlpha</c> (see <see cref="FadeAlphaId"/>) was not reaching the
    /// fragment until ModBuild 394 — and the moment the fade landed, the cost showed.</para>
    ///
    /// <para>DEPTH REJECTION IS BINARY, AND THAT IS THE WHOLE DEFECT. Every control-board element
    /// the user names draws AFTER the hand and tests LEqual with no depth of its own: the
    /// initiative track and the actor bars are converted world-space canvases riding the panel
    /// ladder (<c>sortingOrder</c> ≥ 100, measured 148…276), the pile captions and counts are
    /// TextMeshPro renderers adopted into the control board's furniture band (95…98). All three
    /// therefore failed the stamp and were ERASED along the hand's silhouette instead of showing
    /// through it. The screenshot is unambiguous about which of the two failure classes it is: the
    /// caption glyphs are cut with a razor edge ("WO|", "|NT", "|GEGENSTÄNDE") and absent inside it,
    /// while the pile-card QUADS behind those same fingers are still there and merely greyed by the
    /// 45 % blend — painted THROUGH, because they draw before the hand.</para>
    ///
    /// <para>THIS IS A SOLVED CLASS ONE DIRECTORY OVER. <c>CanvasConversion.8.Order.cs</c> records a
    /// depth stamp being REMOVED from the panel machinery for this precise failure — "a depth stamp
    /// is a per-QUAD statement, so … in every non-crossing case, a guaranteed hard-edged hole … a
    /// grey block around the initiative portraits every single time the pause menu was open" — and
    /// replaced by a far-to-near DISTANCE LADDER on <c>sortingOrder</c>. A ghosted hand is a
    /// free-floating surface at a measurable eye distance, so it belongs on that ladder too:
    /// <see cref="RankAgainstPanels"/> seats it every frame through
    /// <c>CanvasConversion.OrderAboveDistanceAndClusters</c> — the CLUSTER-aware variant, because
    /// the pile captions are board furniture and not panels, and only that variant can rank a plate
    /// above or below a whole furniture band.</para>
    ///
    /// <para>IT SATISFIES BOTH HALVES OF THE REPORT AT ONCE, which the prepass structurally could
    /// not. PERSPECTIVE: the hand's order is above every subject measurably behind it, so it draws
    /// after that subject and is visibly in front of it — the ModBuild 352 requirement, kept, and
    /// kept in the other direction too (a menu pulled nearer than the hand still covers it, because
    /// the ladder never lifts the hand into a nearer panel's slot). SEE-THROUGH: it gets there by
    /// BLENDING at the ghost's own alpha rather than by rejecting a fragment, so whatever is behind
    /// reads through at 1 − alpha. The residual is the ladder's own accepted trade: one order per
    /// hand, so two surfaces that genuinely CROSS get one answer where the honest answer would be
    /// two. Real depth still does the per-pixel work everywhere it exists — the ghost keeps
    /// <c>ZTest LEqual</c>, so the board slab, walls, and a card's depth-writing AlphaTest backing
    /// (queue 2450) occlude the hand per pixel exactly as they did before.</para>
    /// </summary>
    private const int GhostPanelLift = 11;

    /// <summary>Floor between two <c>GHOST HAND ORDER</c> lines from ONE hand. The line is
    /// change-gated first; this only caps the burst rate if a hand hovers exactly on a ladder
    /// boundary. A change the throttle swallows is NOT lost: <see cref="_orderPrevious"/> is left
    /// unadvanced, so the next allowed tick still sees a difference and prints the CURRENT order —
    /// the same shape <c>WorldUI.FreeLabelOrder</c> uses, and the reason it uses it.</summary>
    private const float OrderLogMinIntervalSeconds = 3f;

    /// <summary>Colour properties probed in order — first one the shader has carries the alpha.</summary>
    private static readonly int[] ColorIds =
    {
        Shader.PropertyToID("_Color"),
        Shader.PropertyToID("_BaseColor"),
        Shader.PropertyToID("_TintColor"),
        Shader.PropertyToID("_MainColor"),
        Shader.PropertyToID("_UnlitColor"),
    };

    /// <summary>Names of <see cref="ColorIds"/>, index for index — the readback instrument has to
    /// be able to say WHICH property it read the alpha out of, and a PropertyToID is one-way.</summary>
    private static readonly string[] ColorNames =
    {
        "_Color", "_BaseColor", "_TintColor", "_MainColor", "_UnlitColor",
    };

    private readonly string _label;

    private HandRig? _rig;
    private Renderer[]? _renderers;
    private Material[][]? _originals;   // parallel to _renderers: the untouched shared arrays
    private Material[][]? _ghosts;      // parallel to _renderers: OUR clones (we destroy these)
    private UnityEngine.Rendering.ShadowCastingMode[]? _shadows;
    private int[]? _sortingOrders;      // parallel to _renderers: the untouched authored orders
    private float _appliedAlpha = -1f;

    /// <summary>Ladder order currently written onto every ghosted renderer; <c>int.MinValue</c>
    /// means "never written", so the first tick always seats the hand.</summary>
    private int _appliedOrder = int.MinValue;

    /// <summary>Last order the instrument REPORTED, and the throttle it is reported under. Both
    /// are written and read only by <see cref="ReportGhostOrderOnChange"/>; nothing else reads
    /// them, so switching that line off cannot change a pixel.</summary>
    private int _orderPrevious = int.MinValue;
    private float _nextOrderLogAt;

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
        }
        else if (_renderers == null)
        {
            Engage(rig, alpha);
        }
        else if (!Mathf.Approximately(alpha, _appliedAlpha))
        {
            RefreshAlpha(alpha);
        }

        // PER FRAME, on every path that leaves the hand engaged: the hand MOVES, and its seat on
        // the panel ladder is a function of where it is. This is the whole perspective mechanism —
        // see GhostPanelLift.
        RankAgainstPanels(rig);
    }

    /// <summary>
    /// Seat every ghosted renderer on the converted-panel DISTANCE LADDER for this hand's own eye
    /// distance, so the ghost draws after everything measurably behind it and before everything in
    /// front — and does so by blending, not by rejecting. The full root cause, and why this
    /// replaced ModBuild 352's depth prepass, are on <see cref="GhostPanelLift"/>.
    ///
    /// <para>MEASURED AT THE PALM, not at the hand root or a renderer bound: the palm is the part
    /// of the hand the fan, the held card and the control board are all judged against, and a
    /// bounds centre would move with finger curl. Falls back to the rig root when a rig has no
    /// palm node.</para>
    ///
    /// <para>COST: one <c>Vector3.Distance</c> plus one walk over the live panels (~30) and the ≤2
    /// furniture clusters, per ghosted hand per frame — the price <c>WorldUI.FreeLabelOrder</c>,
    /// <c>Net.Board.BoardVisual</c> and <c>WorldUI.WristHud</c> already pay. The renderer writes
    /// themselves are change-gated, so a still hand writes nothing at all. No scene sweep.</para>
    ///
    /// <para>MULTIPLAYER: local presentation only, exactly like every other ladder caller. WHETHER
    /// a hand is ghosted still follows its OWNER (the wire's ghost-side mask, unchanged); WHERE
    /// that ghost sits in the draw order is geometry, and it is computed on each client from that
    /// client's own head pose against that client's own panels — the mirror and every
    /// <c>Net.Remote.RemoteAvatar</c> hand run this same method through <see cref="Apply"/>. No
    /// wire field, no config key, no viewer-side dial.</para>
    /// </summary>
    private void RankAgainstPanels(HandRig rig)
    {
        if (_renderers == null || _sortingOrders == null)
            return;
        Camera? cam = WorldUI.CanvasConversion.WorldCamera;
        if (cam == null)
            return;
        Transform? anchor = rig.PalmCenter != null ? rig.PalmCenter : rig.Root;
        if (anchor == null)
            return;

        float eyeDistance = Vector3.Distance(cam.transform.position, anchor.position);
        int order = WorldUI.CanvasConversion.OrderAboveDistanceAndClusters(eyeDistance, GhostPanelLift);
        if (order != _appliedOrder)
        {
            _appliedOrder = order;
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer r = _renderers[i];
                if (r != null)
                    r.sortingOrder = order;
            }
        }
        ReportGhostOrderOnChange(eyeDistance);
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
                if (r != null && _originals != null && _shadows != null && _sortingOrders != null)
                {
                    r.sharedMaterials = _originals[i];
                    r.shadowCastingMode = _shadows[i];
                    // The ladder seat is OURS for exactly as long as the ghost is engaged; the
                    // un-ghosted hand is opaque geometry and must go back to the order it was
                    // authored with (see RankAgainstPanels).
                    r.sortingOrder = _sortingOrders[i];
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
        _sortingOrders = null;
        _appliedAlpha = -1f;
        _appliedOrder = int.MinValue;
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
        _sortingOrders = new int[_renderers.Length];

        var shaders = new HashSet<string>();
        int tinted = 0;
        // The clone the OUTCOME instrument reads back from (see ReportGhostBlendOnChange). A
        // reference to a material we have finished configuring, never a copy of our intent.
        Material? probe = null;
        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer r = _renderers[i];
            Material[] source = r.sharedMaterials;
            _originals[i] = source;
            _shadows[i] = r.shadowCastingMode;
            _sortingOrders[i] = r.sortingOrder;

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
                probe ??= clone;
            }

            // NO DEPTH PASS IS ADDED HERE. ModBuild 352 hung a second, colour-free back-face
            // material off this renderer to stamp the ghost's silhouette into the depth buffer;
            // that is what erased the initiative track, the health bars and the pile captions the
            // moment the hand actually became translucent. Draw order does the job instead, and it
            // does it by blending — see GhostPanelLift and RankAgainstPanels.
            _ghosts[i] = clones;
            r.sharedMaterials = clones;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        _appliedAlpha = alpha;
        _appliedOrder = int.MinValue; // force the first RankAgainstPanels to seat this hand
        ReportGhostDepthOnce();
        ReportGhostBlendOnChange(probe);
        VRLog.Info("Hands", $"Ghost hand ON ({_label}) — alpha {alpha:0.00} " +
                            $"(strength {(1f - alpha) * 100f:0}%), {_renderers.Length} renderer(s) " +
                            $"cloned onto private materials, {tinted} material(s) tinted, " +
                            $"{_renderers.Length} authored sortingOrder(s) saved before the hand " +
                            "was seated on the converted-panel distance ladder (restored on " +
                            "release); " +
                            $"shaders: {(shaders.Count > 0 ? string.Join(", ", shaders) : "none")}" +
                            (s_swappedShader
                                ? " — at least one had NO blend state (hard-coded opaque) and its CLONE was " +
                                  "re-shadered to an unlit alpha-blended one, texture + tint carried over; " +
                                  "that is what makes the fade visible at all."
                                : " — all blendable as shipped. That verdict is a PROPERTY-LIST "
                                  + "test and nothing more: it says the shader exposes blend "
                                  + "factors, never that it consumes the alpha written here. "
                                  + "BoardLit passed it while emitting a hard 1.0, which is how "
                                  + "this feature stayed invisible; the readback line printed "
                                  + "beside this one carries the alpha actually emitted.")
                            + ".");
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

    // ---- depth verdict ------------------------------------------------------------------------

    /// <summary>One-shot session verdict (see <see cref="ReportGhostDepthOnce"/>). One flag, not
    /// two: unlike the shader lookup it replaced, this line depends on nothing that loads late, so
    /// there is no "not yet" answer it could latch onto.</summary>
    private static bool s_depthReported;

    /// <summary>
    /// Say ONCE per session what the ghost does about depth, and what carries the perspective
    /// requirement instead — the single fact the next hardware round has to be able to check
    /// against the picture. Printed at the shipped default level deliberately: the engage line
    /// beside it is DEBUG, and a co-player running at INFO would otherwise send back a log that
    /// answers nothing.
    /// </summary>
    private static void ReportGhostDepthOnce()
    {
        if (s_depthReported)
            return;
        s_depthReported = true;
        // HW-VERIFY
        VRLog.Note("Hands",
            "GHOST HAND DEPTH: the ghost writes NO depth, deliberately. ModBuild 352's colour-free "
            + "back-face DEPTH PREPASS (renderQueue 3099, Blend Zero One, ZWrite 1, ZTest LEqual, "
            + "Cull Front) is retired: a depth stamp is binary, so every control-board element that "
            + "draws after the hand and tests LEqual with no depth of its own — the initiative "
            + "track and the actor bars (converted world-space canvases on the panel ladder, "
            + "sortingOrder 100+), the pile captions and counts (TextMeshPro in the control board's "
            + "furniture band, 95-98) — was erased along the hand's silhouette instead of showing "
            + "through it. Perspective is carried by DRAW ORDER now, per frame, from this hand's "
            + "own eye distance; the GHOST HAND ORDER line beside this one names the rank it "
            + "actually got and the two subjects that decided it. The ghost still keeps ZTest "
            + "LEqual, so real depth-writing geometry — the board slab, walls, a card's AlphaTest "
            + "backing at queue 2450 — occludes it per pixel exactly as before. THE TEST, and it "
            + "has two halves that must BOTH hold: hold the ghosted hand between the eye and the "
            + "control board — the initiative row, the health bars and the pile text must stay "
            + "readable THROUGH the fingers, and the fingers must still read as being in front of "
            + "them; then trigger the take-damage prompt and hold the hand over the decision "
            + "buttons — the hand must be in front of them, not behind. A half that fails names "
            + "which one: text erased means something is still writing depth, text on top of the "
            + "fingers means the order went the wrong way.");
    }

    // ---- outcome instrument -------------------------------------------------------------------

    /// <summary>Signature of the last blend state reported, so the line below is CHANGE-GATED
    /// rather than once-per-session: a hand style switch, a bundle that loaded late, a strength
    /// edit that lands on a different shader — each of those is a new signature and must print.
    /// A constant reason printing once and then falling silent is an instrument that reads as
    /// dead, which this file has already paid for on the depth line.</summary>
    private static string s_lastBlendSignature = string.Empty;

    /// <summary>
    /// Blend state READ BACK OFF THE LIVE CLONE, formatted for a human. Never a restatement of
    /// what <see cref="MakeTransparent"/> intended to write — every property here is guarded by
    /// <c>HasProperty</c> at write time, so "we set SrcAlpha" and "the material is on SrcAlpha"
    /// are different claims and it was the gap between them that hid this defect.
    /// </summary>
    private static string BlendReadback(Material m)
    {
        string src = m.HasProperty(SrcBlendId)
            ? ((UnityEngine.Rendering.BlendMode)(int)m.GetFloat(SrcBlendId)).ToString()
            : "baked-in";
        string dst = m.HasProperty(DstBlendId)
            ? ((UnityEngine.Rendering.BlendMode)(int)m.GetFloat(DstBlendId)).ToString()
            : "baked-in";
        string zw = m.HasProperty(ZWriteId)
            ? ((int)m.GetFloat(ZWriteId) != 0 ? "on" : "off")
            : "baked-in";
        return $"Blend {src} {dst}, ZWrite {zw}, renderQueue {m.renderQueue}";
    }

    /// <summary>
    /// The alpha the fragment will actually EMIT, and the property it comes out of — the single
    /// number an eye can disagree with. 1.00 under <c>Blend SrcAlpha OneMinusSrcAlpha</c> is
    /// <c>dst*(1-1) + src*1</c>, i.e. the opaque hand, whatever the tint says.
    ///
    /// <para><c>_FadeAlpha</c> is probed FIRST because a shader that has it emits it and ignores
    /// the tint alpha (<see cref="FadeAlphaId"/>). "none" means this shader exposes no fadeable
    /// term at all and the hand cannot be ghosted by writing properties — the
    /// <see cref="SwapToBlendableShader"/> path is what has to carry it.</para>
    /// </summary>
    private static float EmittedAlpha(Material m, out string via)
    {
        if (m.HasProperty(FadeAlphaId))
        {
            via = "_FadeAlpha";
            return m.GetFloat(FadeAlphaId);
        }
        for (int i = 0; i < ColorIds.Length && i < ColorNames.Length; i++)
        {
            if (!m.HasProperty(ColorIds[i]))
                continue;
            via = ColorNames[i];
            return m.GetColor(ColorIds[i]).a;
        }
        via = "none";
        return 1f;
    }

    /// <summary>
    /// Say what the ghost material IS, not what we asked it to be.
    ///
    /// <para>WHY THIS LINE EXISTS. The engage line beside it reported cloned / tinted / armed, and
    /// every one of those numbers was TRUE through forty builds in which the feature was completely
    /// invisible — it stated the mod's own intent, which no picture can contradict. This one reads
    /// the live clone back and prints the one quantity the eye is looking at: the alpha the shader
    /// will emit. It is also at the shipped level, unlike the engage line, so a hardware log
    /// answers the question without a debug build.</para>
    /// </summary>
    private static void ReportGhostBlendOnChange(Material? probe)
    {
        if (probe == null)
            return;
        string shader = probe.shader != null ? probe.shader.name : "(null shader)";
        string blend = BlendReadback(probe);
        float emitted = EmittedAlpha(probe, out string via);
        string signature = $"{shader}|{blend}|{via}|{emitted:0.000}";
        if (signature == s_lastBlendSignature)
            return;
        s_lastBlendSignature = signature;

        bool opaque = emitted >= 0.999f;
        // HW-VERIFY
        VRLog.Note("Hands", $"GHOST HAND BLEND: clone reads back as {shader}, {blend}; the alpha "
                            + $"its fragment will EMIT is {emitted:0.00}, taken from {via}. "
                            + (opaque
                                ? "THAT IS OPAQUE — under SrcAlpha/OneMinusSrcAlpha a source alpha "
                                  + "of 1 leaves the destination out entirely, so the hand will "
                                  + "look exactly like the un-ghosted hand no matter what the "
                                  + "engage line beside this reports about tinting. If you can see "
                                  + "this line and the hand still covers the cards, the shader is "
                                  + "carrying its opacity in a property nothing here writes; name "
                                  + "it and add it to SetAlpha."
                                : "THE TEST: open the card fan and look at a card THROUGH the "
                                  + "fingers and the cuff. Card art must be readable through the "
                                  + "hand; the hand must still be visible enough to aim with. Same "
                                  + "on a card taken into the hand before the grasp closes. If the "
                                  + "hand reads solid while this line says otherwise, the fade is "
                                  + "being lost after the material — a later opaque pass over the "
                                  + "same pixels, not a material problem.")
                            + " DEPTH ORDER: this pass sits at renderQueue " + GhostRenderQueue
                            + " with ZWrite off and writes no depth at all, so Unity resolves it "
                            + "against every other transparent surface by sortingLayer, then "
                            + "sortingOrder, then queue — and the hand's sortingOrder is rewritten "
                            + "every frame from its eye distance onto the converted-panel ladder "
                            + "(lift " + GhostPanelLift + "). A card in front of the hand is supposed "
                            + "to be unaffected by any of those orders, because its backing slab is "
                            + "depth-writing AlphaTest geometry in the opaque tier and has already "
                            + "stamped its footprint before this pass runs — but that is a claim "
                            + "ABOUT THE CARD, not about this material, and this line cannot check "
                            + "it. The CARD BODY DEPTH STAMP line (Cards) is the one that reads a "
                            + "real card body back and says whether it still stamps; if the hand is "
                            + "showing through a card, read that line first, because a body wearing "
                            + "the face-hosted mesh has no front fan and stamps nothing. The GHOST "
                            + "HAND ORDER line carries the rank this hand actually got.");
    }

    /// <summary>
    /// The ghost hand's LADDER SEAT, read back off a live renderer — the outcome the perspective
    /// half of the report is judged on, and the one number a picture can disagree with.
    ///
    /// <para>WHY IT IS A READBACK AND NOT THE VALUE WE COMPUTED. This file has already shipped a
    /// line that reported the mod's own intent ("1 material(s) tinted") and stayed true through
    /// forty builds in which the feature was invisible. So the order printed here is fetched from
    /// <see cref="Renderer.sortingOrder"/> AFTER the write, and the two subjects that decided it
    /// are named by the ladder's own describer, which walks the same panels and clusters the
    /// arithmetic walked — it cannot name a subject the decision did not consider.</para>
    ///
    /// <para>CHANGE-GATED AND THROTTLED. Called every frame, it costs two int compares while the
    /// hand is still; the neighbours string is only allocated on a line that actually prints. A
    /// change the throttle swallows is not lost — <see cref="_orderPrevious"/> is left unadvanced,
    /// so the next allowed tick still sees a difference and prints the CURRENT order.</para>
    /// </summary>
    private void ReportGhostOrderOnChange(float eyeDistance)
    {
        if (_renderers == null || _appliedOrder == _orderPrevious)
            return;
        float now = Time.unscaledTime;
        if (now < _nextOrderLogAt)
            return;

        Renderer? live = null;
        int written = 0;
        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] == null)
                continue;
            written++;
            live ??= _renderers[i];
        }
        if (live == null)
            return; // the whole subtree died under us; Release will tidy up

        _nextOrderLogAt = now + OrderLogMinIntervalSeconds;
        string was = _orderPrevious == int.MinValue ? "unranked" : _orderPrevious.ToString();
        _orderPrevious = _appliedOrder;
        // HW-VERIFY
        VRLog.Note("Hands", $"GHOST HAND ORDER: ({_label}) palm at d={eyeDistance:F2} m -> "
                            + $"sortingOrder {live.sortingOrder} read back off the live renderer "
                            + $"(asked for {_appliedOrder}, was {was}), written to {written} "
                            + $"renderer(s); " + WorldUI.CanvasConversion.DescribeOrderNeighbours(eyeDistance)
                            + ". The ghost now BLENDS over every subject listed as behind it "
                            + "instead of z-rejecting it, so those must be readable through the "
                            + "fingers AND the fingers must read as in front; anything listed as "
                            + "in front covers the hand. If the readback disagrees with the value "
                            + "asked for, something else is writing this renderer's sortingOrder.");
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

    /// <summary>
    /// Write <paramref name="alpha"/> into EVERY channel this material's shader could be reading
    /// its opacity from. True when at least one existed (false = this shader has no fadeable term
    /// at all — reported in the log).
    ///
    /// <para>BOTH, not the first one found. Writing only the tint is the whole ghost-hand
    /// regression: <c>GloomhavenVR/BoardLit</c> has a <c>_Color</c> — so this method returned true
    /// and the log counted a tint — while its fragment emits <c>_FadeAlpha</c> and discards the
    /// tint's alpha entirely (full root cause on <see cref="FadeAlphaId"/>). The two writes are NOT
    /// a double multiply anywhere: a shader that consumes <c>_Color.a</c> has no <c>_FadeAlpha</c>,
    /// and BoardLit's colour term is <c>alb.rgb</c>, which never sees <c>_Color.a</c>. They reach
    /// different halves of one fragment, which is the same reason
    /// <c>PeerBoardFade.FadeSurface</c> writes both.</para>
    ///
    /// <para>ABSOLUTE, not multiplied into whatever the material shipped with: this runs on a fresh
    /// clone AND again on every strength edit (<see cref="RefreshAlpha"/>), so a multiply would
    /// compound itself down to black. The one writer of <c>_FadeAlpha</c> in the mod is
    /// <c>PeerBoardFade</c>, and it drives it through a MaterialPropertyBlock on a peer board
    /// renderer — it never touches a material asset, so every hand material reaching us carries the
    /// shader's own default of 1.</para>
    /// </summary>
    private static bool SetAlpha(Material m, float alpha)
    {
        float a = Mathf.Clamp01(alpha);
        bool any = false;

        // The scalar BoardLit's fragment actually returns. Written FIRST so a shader carrying both
        // terms can never be left half-faded by an early return.
        if (m.HasProperty(FadeAlphaId))
        {
            m.SetFloat(FadeAlphaId, a);
            any = true;
        }

        for (int i = 0; i < ColorIds.Length; i++)
        {
            if (!m.HasProperty(ColorIds[i]))
                continue;
            Color c = m.GetColor(ColorIds[i]);
            c.a = a;
            m.SetColor(ColorIds[i], c);
            any = true;
            break; // one tint per material; the rest are aliases this shader does not have
        }
        return any;
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
