using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Cards;

// PlayTray part 7 of 7 (see PlayTray.1.Core.cs for the split map and its rules) — the four
// NESTED types, in their original mutual order:
//
//     LaserTarget          (pre-split PlayTray.cs:327)   collider -> IPokeable pair
//     SlotPulse            (pre-split PlayTray.cs:2271)  self-animated wanted-slot hint
//     BoardSurfaceTarget   (pre-split PlayTray.cs:3386)  no-op full-board laser target
//     BoardButton          (pre-split PlayTray.cs:3401)  one physical board button (987 lines)
//
// They stay NESTED (not promoted to top-level types) because BoardButton reaches into
// PlayTray's private statics — Tint, NewKeycapMaterial, BoxCapShader, OverlayMaterial,
// SquareCapBevel, CapRestZ. Promoting them would mean widening that surface, which is a
// behavioural change wearing a tidy-up costume; moving them is not.
//
// Their MUTUAL order is load-bearing for verification, not for behaviour: ilspycmd hoists
// every nested type to the top of the parent's decompiled file in metadata order, so all
// four moving together as one block is what keeps the guard diff empty. Do not reorder them
// here, and do not split them across two files.

internal sealed partial class PlayTray
{
    internal readonly struct LaserTarget
    {
        public readonly Collider Collider;
        public readonly IPokeable Target;

        public LaserTarget(Collider collider, IPokeable target)
        {
            Collider = collider;
            Target = target;
        }
    }

    /// <summary>Self-animated soft pulse for a wanted-slot hint quad (no PlayTray Update).</summary>
    private sealed class SlotPulse : MonoBehaviour
    {
        private MeshRenderer? _renderer;
        private Color _base;

        internal void Init(MeshRenderer? renderer, Color baseColor)
        {
            _renderer = renderer;
            _base = baseColor;
        }

        private void Update()
        {
            if (_renderer == null || _renderer.sharedMaterial == null)
                return;
            // Breathe between ~0.30 and ~0.85 — a calm "waiting" pulse. Item 5: the glow
            // now uses the ADDITIVE Overlay shader (where rgb IS the emitted brightness and
            // alpha is unused), so scale the rgb by the breath; also breathe alpha so the
            // alpha-blended Sprites/Default fallback (Overlay absent) still pulses.
            float t = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3.2f);
            float k = Mathf.Lerp(0.30f, 0.85f, t);
            Color c = _base;
            c.r *= k;
            c.g *= k;
            c.b *= k;
            c.a = k;
            _renderer.sharedMaterial.color = c;
        }
    }

    /// <summary>
    /// Full-board laser-reticle target (test #24 item 7, see <see cref="BuildBoardSurface"/>).
    /// A no-op <see cref="IPokeable"/> — the ray beam already clamps to it via
    /// UiHitOverride, so it needs no behavior; a trigger on bare board must do
    /// nothing (never steal a click from a docked widget). DELIBERATELY a plain
    /// MonoBehaviour, not a <see cref="PokeableBehaviour"/>: it is registered ONLY as
    /// a laser target, never with the fingertip-poke registry.
    /// </summary>
    private sealed class BoardSurfaceTarget : MonoBehaviour, IPokeable
    {
        public void OnPokeEnter(VRHand hand) { }
        public void OnPokeExit(VRHand hand) { }
        public void OnPoke(VRHand hand) { }
    }

    /// <summary>
    /// One physical board button: base plate + travelling cap + label. Poke (P2
    /// registry) and laser (tray LaserTargets) both land in <see cref="OnPoke"/>.
    /// Cap presses in ~4 mm on click and springs back (transform anim, no Animator).
    /// </summary>
    internal sealed class BoardButton : PokeableBehaviour
    {
        private System.Action? _onClick;
        private Material? _capMaterial;      // item 4: the TOP-plateau material (carries the state colour)
        private Material? _capBevelMaterial; // item 4: the BRIGHT parchment-lit bevel-ring material (boxy caps); null otherwise
        private Material? _capWallMaterial;  // item 4: the dark warm side-wall material (boxy caps); null otherwise
        private SpriteRenderer? _capFace; // native-skin face (test #25 item 3); null on the procedural fallback
        private Renderer? _capMeshRenderer; // item A diagnostic: the cap body renderer (cube/disc/sprite)

        // ---- Item 1b: SOLID, on-theme keycap palette (aged brass / dark wood / parchment) ----
        // The 3D square cap renders as three submeshes, all driven together from the button
        // STATE colour (disabled / accent / confirmed / dwell) so state signalling is preserved:
        //   • TOP   — the state colour (semantic; T4 antique palette: dark-wood disabled,
        //             warm-parchment available, muted accents, worn-brass readied).
        //   • BEVEL — an AGED-BRASS 45° chamfer ring framing the top (T4: softened from the
        //             old near-white parchment). Still the "catch-light" edge: lighter than
        //             top and wall, angled so it stays visible even near top-down — the
        //             primary "this button is RAISED" cue, now reading as a brass inlay.
        //   • WALL  — a solid, WARM dark-WOOD side band so the cap separates from the board by both
        //             value AND hue, yet still reads as a physical material (not a black void).
        //
        // WHY THE CAP LOOKED "TRANSPARENT / GLASSY" (item 1b root cause): the material is fully
        // OPAQUE (BoardLit, renderQueue 2000, alpha 1 — no alpha-blend anywhere on the cap), but
        // the TOP + WALLS were near-black dark grey (top 0.24, wall 0.13) sitting on a near-black
        // board. Only the bright bevel RING carried any luminance, so the eye saw floating lit
        // edges around dark faces that sank into the background — a wireframe / glass read. The
        // cure is NOT "darker" but SOLID, WARM, opaque material colours on every face so each one
        // reads as a real control-panel key. All colours below are fully opaque (alpha 1); state
        // only TINTS the solid base, it is never the whole washed-out colour. One line each to tune.

        /// <summary>How dark the side WALLS start relative to the top (before the warm lean).</summary>
        private const float WallTintFactor = 0.50f;

        /// <summary>Solid dark-WOOD/iron hue the walls lean toward so they read as material, distinct from the board.</summary>
        private static readonly Color WallWarm = new(0.17f, 0.11f, 0.06f);

        /// <summary>How far (0..1) the wall leans from "darker top" toward <see cref="WallWarm"/>.</summary>
        private const float WallWarmLerp = 0.42f;

        /// <summary>
        /// The AGED-BRASS tone the lit bevel ring is pulled toward (T4 restyle: the ring
        /// reads as a worn brass frame set into the carved dark-wood plaque, not the old
        /// near-white parchment catch-light that over-glowed against the board).
        /// </summary>
        private static readonly Color BevelHighlight = new(0.66f, 0.53f, 0.32f);

        /// <summary>How far (0..1) the bevel is pulled from the top toward <see cref="BevelHighlight"/>.
        /// T4: softened from 0.62 — the raised read survives, the wireframe-bright rim does not.</summary>
        private const float BevelLerp = 0.48f;

        /// <summary>Item 4: dark, warm side-wall colour for a given top/state colour.</summary>
        private static Color WallTint(Color top)
        {
            var dark = new Color(top.r * WallTintFactor, top.g * WallTintFactor, top.b * WallTintFactor, top.a);
            Color w = Color.Lerp(dark, WallWarm, WallWarmLerp);
            w.a = top.a;
            return w;
        }

        /// <summary>Item 4: bright parchment-lit bevel-ring colour for a given top/state colour.</summary>
        private static Color BevelTint(Color top)
        {
            Color b = Color.Lerp(top, BevelHighlight, BevelLerp);
            b.a = top.a;
            return b;
        }

        /// <summary>
        /// Item A conclusive diagnostic (logged once per board, in the placed pose so lossyScale is
        /// real): the cap body's LOCAL scale, the tray/world lossyScale, the resulting REAL cap
        /// thickness in mm (localScale.z × lossyScale.z), plus the material's SHADER NAME and
        /// renderQueue. This settles whether the square cap reads flat because its side walls are
        /// too thin (small mm thickness) or because the material is wrong (not GloomhavenVR/BoardLit,
        /// or an unlit/overlay path that kills wall shading).
        /// </summary>
        internal void LogCapDiagnostics(string label)
        {
            if (_capMeshRenderer == null)
            {
                VRLog.Info("Cards", $"ITEMA cap diag — {label}: no cap mesh renderer (unexpected).");
                return;
            }
            Transform ct = _capMeshRenderer.transform;
            Vector3 lossy = ct.lossyScale;
            // Item 4: the cap mesh is now authored at REAL size with a UNIT-scaled holder, so read
            // the physical extent from the mesh bounds (× world lossyScale), not localScale.
            Mesh? sm = _capMeshRenderer is MeshRenderer meshR && meshR.GetComponent<MeshFilter>() is { sharedMesh: { } fm }
                ? fm : null;
            Vector3 bounds = sm != null ? sm.bounds.size : Vector3.zero;
            float mmThick = Mathf.Abs(bounds.z * lossy.z) * 1000f;
            float mmW = Mathf.Abs(bounds.x * lossy.x) * 1000f;
            float mmH = Mathf.Abs(bounds.y * lossy.y) * 1000f;
            Material? m = _capMeshRenderer.sharedMaterial;
            string shaderName = m != null && m.shader != null ? m.shader.name : "<none>";
            int queue = m != null ? m.renderQueue : -1;
            // Item 4: report the THREE-submesh split (top / bright bevel / dark warm wall). A
            // beveled cap has subMeshCount 3 plus distinct bevel + wall material instances — the
            // big top→bevel→wall value+hue gradient is what makes the raised shape unmistakable.
            int subMeshes = sm != null ? sm.subMeshCount : -1;
            bool split = _capBevelMaterial != null && _capWallMaterial != null && subMeshes >= 3;
            // Item 7: prove the keycap is a CLOSED, correctly-wound SOLID (the see-through
            // "you can see the button's own underside through it" was an inside-out winding on a
            // Cull-Back material). THE COUNTS MOVED IN ROUND 2 with the signet profile: the closed
            // square keycap is 18 quads now — field(1) + outer chamfer(4) + rim land(4) + inner
            // chamfer(4) + side walls(4) + BACK/bottom cap(1) — i.e. 72 verts, 36 tris, submesh
            // index counts [6, 72, 30] (field 6 / bezel 72 / walls+back 30), where it used to be
            // 40/20/[6,24,30]. The back cap still lives in the WALL submesh (2) so it shades dark
            // like the walls. Report the ACTUAL mesh so the next hardware log confirms nothing is
            // missing and the winding is still outward — and note that the ROUND caps are signet
            // plates too now (CardMesh.BuildRoundKeycap, 3 submeshes), which at 64 segments is
            // 642 verts / 640 tris and is NOT what these five numbers describe.
            int vtx = sm != null ? sm.vertexCount : -1;
            int triTotal = 0, s0 = -1, s1 = -1, s2 = -1;
            if (sm != null)
            {
                for (int si = 0; si < sm.subMeshCount; si++)
                    triTotal += (int)(sm.GetIndexCount(si) / 3);
                if (sm.subMeshCount > 0) s0 = (int)sm.GetIndexCount(0);
                if (sm.subMeshCount > 1) s1 = (int)sm.GetIndexCount(1);
                if (sm.subMeshCount > 2) s2 = (int)sm.GetIndexCount(2);
            }
            bool closedSolid = vtx == 72 && triTotal == 36 && s0 == 6 && s1 == 72 && s2 == 30;
            float bevelMm = CapFaceLayout.BezelTotal * Mathf.Min(_capSize.x, _capSize.y)
                            * Mathf.Abs(lossy.z) * 1000f;
            string tintInfo = _capWallMaterial != null
                ? $"top {(m != null ? m.color.ToString() : "<none>")}, bevel {(_capBevelMaterial != null ? _capBevelMaterial.color.ToString() : "<none>")} (lerp {BevelLerp:F2} → parchment), " +
                  $"wall {_capWallMaterial.color} (factor {WallTintFactor:F2}, warm lerp {WallWarmLerp:F2})"
                : "single-material cap (no bevel/wall split)";
            // Item 1b: prove the cap is genuinely OPAQUE (the "glassy/see-through" complaint). Every
            // cap material must be alpha 1 AND draw in the opaque queue (< 2500 = Geometry/AlphaTest,
            // NOT the Transparent 3000 band). If all three pass, the look is a COLOUR issue, never blend.
            float topA = m != null ? m.color.a : -1f;
            float bevA = _capBevelMaterial != null ? _capBevelMaterial.color.a : 1f;
            float wallA = _capWallMaterial != null ? _capWallMaterial.color.a : 1f;
            bool allAlpha1 = topA >= 0.999f && bevA >= 0.999f && wallA >= 0.999f;
            bool opaqueQueue = queue >= 0 && queue < 2500;
            bool opaque = allAlpha1 && opaqueQueue;
            VRLog.Info("Cards", $"ITEMA cap diag — {label}: real cap size {mmW:F1}×{mmH:F1}×{mmThick:F1} mm, " +
                $"bevel ≈ {bevelMm:F1} mm (lossyScale {lossy}), shader '{shaderName}', renderQueue {queue}. " +
                $"OPAQUE: {(opaque ? "YES" : "NO")} (alpha top/bevel/wall {topA:F2}/{bevA:F2}/{wallA:F2} all=1 {allAlpha1}, " +
                $"queue {queue} < 2500 {opaqueQueue} — no alpha-blend/Transparent). " +
                $"Walls read {(mmThick >= 6f ? "SOLID (thickness OK)" : "FLAT (too thin)")}; material is " +
                $"{(shaderName.Contains("BoardLit") ? "BoardLit (shades by normal → lit bevel)" : "NOT BoardLit — bevel/wall shading may be wrong")}. " +
                $"Three-material bevel split: {(split ? "YES" : "NO")} (submeshes {subMeshes}); {tintInfo}. " +
                $"CLOSED SOLID: {(closedSolid ? "YES" : "NO")} (verts {vtx}, tris {triTotal}, submesh indices " +
                $"field/bezel/wall {s0}/{s1}/{s2}; back+bottom cap in wall submesh 2 — expect 72/36/6/72/30 for a " +
                "SQUARE signet cap; a ROUND one is 642/640 and reads NO here by construction). " +
                "Winding is now OUTWARD (RH normal = +n), so no interior/underside shows through under Cull Back (item 7). " +
                "Antique palette (T4): dark-wood plaque / parchment-glow available / aged-brass bevel inlay / dark-wood walls.");
        }

        /// <summary>Throttle for <see cref="LogCapSurface"/> — a live tuning change rebuilds and
        /// re-seats every cap on the board inside one frame.</summary>
        private static float _nextCapSurfaceLogAt;

        /// <summary>
        /// KEYCAP SURFACE — the line that ends the "why was the button body missing while its text
        /// was fine?" question WITHOUT another hardware round of inference.
        ///
        /// <para>The existing <see cref="LogCapDiagnostics"/> fires once per board, at build, for
        /// three named caps only, and it reports geometry. It is silent about the two moments that
        /// matter (the moment a cap is BUILT and the moment it is REVEALED), about every other cap,
        /// and about the four facts that separate the surviving hypotheses from each other:</para>
        /// <list type="bullet">
        /// <item>MATERIAL SLOTS vs SUBMESHES — a body whose renderer carries fewer materials than
        /// its mesh has submeshes draws only the submeshes that HAVE one (zero materials = nothing
        /// at all), which is the documented reveal-clone failure shape from the static-batching
        /// experiment. Names both counts so it is read, not deduced.</item>
        /// <item>SHADER + RENDERQUEUE — separates "the bundle shader never resolved" and "something
        /// pushed this body out of the opaque band" from a colour problem.</item>
        /// <item>THE RENDERED COLOURS AGAINST THE WELL — the actual root cause of the 2026-08-09
        /// round-3 report. Prints top/bevel/wall next to <c>ButtonTuning.CapWellColor</c> and says
        /// outright whether the seat floor had to lift the face.</item>
        /// <item>LAYER — a cap off <c>VRLayers.ModLayer</c> is eligible for the MixedReality
        /// renderer sweeps and drops out of the head-camera/stereo masks.</item>
        /// </list>
        /// <para>Cheap by construction: no allocation until it actually logs, one line per 0.5 s
        /// across all caps, and it runs only at build and at reveal — never per frame.</para>
        /// </summary>
        internal void LogCapSurface(string when)
        {
            // BEFORE the shared throttle: the plate census is change-gated per cap and must not be
            // hidden behind a limiter that lets one cap in eight speak at build time.
            LogPlateCensus(when);
            if (Time.unscaledTime < _nextCapSurfaceLogAt)
                return;
            _nextCapSurfaceLogAt = Time.unscaledTime + 0.5f;
            DescribePieces(out string pieces, out _, out _, out string backing);
            Mesh? sm = _capMeshRenderer is MeshRenderer meshR && meshR.GetComponent<MeshFilter>() is { sharedMesh: { } fm }
                ? fm : null;
            int subMeshes = sm != null ? sm.subMeshCount : (_capFace != null ? 1 : -1);
            Material[] mats = _capMeshRenderer != null ? _capMeshRenderer.sharedMaterials : System.Array.Empty<Material>();
            int slots = mats.Length;
            int liveSlots = 0;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null)
                    liveSlots++;
            Material? m = _capMeshRenderer != null ? _capMeshRenderer.sharedMaterial : null;
            string shaderName = m != null && m.shader != null ? m.shader.name : "<none>";
            int queue = m != null ? m.renderQueue : -1;
            Color face = _capFace != null ? _capFace.color : m != null ? m.color : StateColor();
            bool seated = WorldUI.ButtonTuning.CapSeatFloorEngages(
                (!_enabledState ? DisabledColor : _confirmed ? ConfirmedColor : _accent ? _accentColor : IdleColor)
                * _capTint);
            bool bodyDraws = _capMeshRenderer != null && _capMeshRenderer.enabled && liveSlots > 0
                             && (subMeshes <= 0 || liveSlots >= subMeshes);
            // THE NAME IS A BUILD-TIME SNAPSHOT AND THIS LINE USED TO PRINT ONLY THE NAME - which
            // made it an instrument that asserted a caption it had not measured. `name` is
            // "BoardButton_" + the fallbackLabel the cap was CONSTRUCTED with (Create), and this
            // mod relabels caps live: the item-use cap wears an item-SURRENDER wording
            // ("ITEM ABGEBEN"), the UNDO cap wears a pick flow dialog CANCEL, the CONFIRM cap wears
            // whatever the game state says. On 2026-09-03 the log therefore read
            // 'BoardButton_BENUTZEN' at REVEAL through an entire surrender demand while the cap
            // itself displayed "ITEM ABGEBEN", and the next session read that as the caption defect
            // and went looking for it on the cap. It was on the recess CAPTION beside it. Printing
            // the LIVE label next to the name is what makes the two separable without a hardware
            // round; when they differ the name is the birth wording and `reads` is what a player
            // sees.
            string liveLabel = _label != null ? _label.text : "<no label>";
            VRLog.Info("Cards", $"KEYCAP SURFACE [{when}] '{name}' reads '{liveLabel}'" +
                $"{(name == "BoardButton_" + liveLabel ? "" : " (RELABELLED since build - the name is the birth wording, 'reads' is what the player sees)")}" +
                $": layer {gameObject.layer} " +
                $"(mod layer {Core.VRLayers.ModLayer}){(gameObject.layer == Core.VRLayers.ModLayer ? "" : " — OFF THE MOD LAYER")}, " +
                $"shader '{shaderName}', renderQueue {queue}, submeshes {subMeshes} vs material slots " +
                $"{slots} ({liveSlots} non-null){(subMeshes > liveSlots ? " — SLOTS MISSING: those submeshes draw NOTHING" : "")}. " +
                $"State enabled={_enabledState} accent={_accent} confirmed={_confirmed}, tint {_capTint}. " +
                $"Face {face}, bevel {(_capBevelMaterial != null ? _capBevelMaterial.color.ToString() : "<n/a>")}, " +
                $"wall {(_capWallMaterial != null ? _capWallMaterial.color.ToString() : "<n/a>")} " +
                $"vs the WELL it sits in {WorldUI.ButtonTuning.CapWellColor}. " +
                $"Seat floor {(seated ? "ENGAGED — the untinted-well comparison says this cap WOULD have rendered darker than its own recess (the 'invisible button, visible text' shape) and was lifted" : "not needed (face already clears its well)")}. " +
                $"Body draws: {(bodyDraws ? "YES" : "NO")}. " +
                $"Label tags: {_labelTags} rich-text tag(s) found in the game string this cap was last given" +
                $"{(_labelTags > 0 ? " (stripped before display — see KEYCAP LABEL)" : "")}. " +
                // 2026-09-04: WHAT THE KEY IS BUILT FROM, read off the hierarchy rather than off the
                // build path — so a plate that came back by any route shows up here as a named piece.
                $"Built from {pieces}; backing plate: {backing}.");
        }

        /// <summary>The last plate census <see cref="LogPlateCensus"/> printed for this cap — the
        /// change gate. Written and read by that diagnostic only.</summary>
        private string? _lastPlateCensus;

        /// <summary>
        /// EVERY RENDERER UNDER THIS KEY, sorted into the cap mesh, the label (the TMP object and any
        /// TMP_SubMesh child it spawns for fallback glyphs) and everything else — the "everything
        /// else" being what the user saw as a thin plate floating behind his keys (2026-09-04). The
        /// 'Base' well, when built, is a direct child named "Base"; <paramref name="backing"/> names
        /// it with its size, or says none.
        /// </summary>
        private void DescribePieces(out string pieces, out int foreign, out string foreignNames, out string backing)
        {
            Renderer[] rs = GetComponentsInChildren<Renderer>(true);
            var all = new System.Text.StringBuilder();
            var others = new System.Text.StringBuilder();
            foreign = 0;
            Transform? labelT = _label != null ? _label.transform : null;
            foreach (Renderer r in rs)
            {
                if (r == null)
                    continue;
                bool isCap = r == _capMeshRenderer;
                bool isLabel = !isCap && labelT != null && r.transform.IsChildOf(labelT); // IsChildOf is true of itself too
                if (all.Length > 0)
                    all.Append(", ");
                all.Append('\'').Append(r.name).Append('\'').Append(isCap ? " (cap)" : isLabel ? " (label)" : " (FOREIGN)");
                if (isCap || isLabel)
                    continue;
                foreign++;
                if (others.Length > 0)
                    others.Append(", ");
                Vector3 sz = r.bounds.size * 1000f;
                others.Append('\'').Append(r.name).Append("' under '").Append(r.transform.parent != null ? r.transform.parent.name : "<none>")
                      .Append("' ").Append(sz.x.ToString("F1")).Append(" x ").Append(sz.y.ToString("F1")).Append(" x ").Append(sz.z.ToString("F1"))
                      .Append(" mm world, enabled=").Append(r.enabled && r.gameObject.activeInHierarchy);
            }
            pieces = $"{rs.Length} renderer(s): {all}";
            foreignNames = others.ToString();
            Transform? plate = transform.Find("Base");
            if (plate == null)
                backing = "none";
            else
            {
                MeshFilter? mf = plate.GetComponent<MeshFilter>();
                Vector3 local = mf != null && mf.sharedMesh != null
                    ? Vector3.Scale(mf.sharedMesh.bounds.size, plate.localScale) : plate.localScale;
                backing = $"'Base' {local.x * 1000f:F1} x {local.y * 1000f:F1} x {local.z * 1000f:F1} mm, " +
                          $"centre {plate.localPosition.z * 1000f:F1} mm behind the seat plane";
            }
        }

        /// <summary>
        /// THE PLATE COUNT, change-gated per cap: prints once at build (a readable zero) and again
        /// only when the set of renderers that are neither the cap mesh nor its label changes. A
        /// zero says the key is the cap asset and its label and nothing else; a non-zero count
        /// names what else was drawn under it, with its parent and world size, so the next report
        /// of "a plate behind the button" is answered by this line rather than by a rebuild.
        /// </summary>
        private void LogPlateCensus(string when)
        {
            DescribePieces(out _, out int foreign, out string foreignNames, out string backing);
            string census = $"{foreign}|{foreignNames}|{backing}";
            if (census == _lastPlateCensus)
                return;
            _lastPlateCensus = census;
            // HW-VERIFY
            VRLog.Note("Cards", $"KEYCAP PLATE [{when}] '{name}': {foreign} renderer(s) under this key other than " +
                $"the cap mesh and its label{(foreign > 0 ? " — " + foreignNames : "")}; backing plate: {backing}. " +
                "Zero means the key is the cap asset and its label and nothing else (user 2026-09-04: no plate " +
                "behind the board's keys); a non-zero count names what was drawn behind or beside it.");
        }

        private TextMeshPro? _label;

        /// <summary>How many TMP rich-text tags the LAST string handed to <see cref="SetLabel"/>
        /// carried before the seam stripped them — reported by <see cref="LogCapSurface"/> so a
        /// log can say whether the cap ever received a tagged game string.</summary>
        private int _labelTags;
        private Transform? _cap;
        private Color _accentColor;

        /// <summary>WHICH control this cap is, i.e. which cell of the board's keycap atlas its top
        /// plateau samples (<see cref="CapSymbols"/>). Kept so the material can be re-aimed live
        /// (<see cref="SetCapRole"/>) and so the bounded shader heal re-skins the cap with the same
        /// symbol it was built with rather than a plain one.</summary>
        private CapRole _capRole = CapRole.Plain;

        /// <summary>WHICH NON-SYMBOL CELL THIS CAP'S BEZEL RING AND SIDE WALLS TAKE —
        /// <see cref="CapRole.Plain"/> on a square cap, <see cref="CapRole.PlainRound"/> on a disc
        /// (<see cref="CapCellMath.PlainCell"/>). Stored rather than re-derived because the bounded
        /// material heal re-mints those two materials minutes after the build, from a method that
        /// no longer knows the cap's shape; hard-coding the square cell there would have quietly
        /// put the mitred band back on exactly the discs that were unlucky enough to be built
        /// before the bundle was loadable.</summary>
        private CapRole _plainCell = CapRole.Plain;

        /// <summary>True once this cap's face really is wearing its role's atlas cell. False on a
        /// cap built before the bundle was loadable — which is what <see cref="TryHealCapMaterial"/>
        /// repairs, and what stops it re-skinning a cap that is already correct on every tick.</summary>
        private bool _symbolApplied;

        /// <summary>The cap's built footprint in metres, kept so the caption can be re-laid when a
        /// late atlas arrives (<see cref="ApplyLabelLayout"/>).</summary>
        private Vector2 _capSize = Vector2.one;

        /// <summary>
        /// Put the caption where this cap's CURRENT symbol state says it belongs: centred and full
        /// size with no symbol, dropped into the lower band under one, and not drawn at all on a
        /// symbol-only role.
        ///
        /// <para>It exists as a method rather than as the inline block in <see cref="Create"/>
        /// because the symbol can arrive LATE. A cap built before its board's keycap atlas was
        /// loadable is re-skinned in place by <see cref="TryHealCapMaterial"/>, and re-skinning
        /// alone would leave a rest disc showing its newly carved crescent AND the word underneath
        /// it — the one thing the board engraving exists to avoid. Cheap and idempotent, so the
        /// heal simply calls it.</para>
        /// </summary>
        private void ApplyLabelLayout(bool hasSymbol)
        {
            if (_label == null)
                return;
            float dy = CapSymbols.LabelCentreY(_capRole, hasSymbol) * _capSize.y;
            Vector2 box = CapSymbols.LabelBox(_capRole, hasSymbol);
            Vector3 p = _label.transform.localPosition;
            _label.transform.localPosition = new Vector3(0f, dy, p.z);
            Core.TmpFit.FitCapLabel(_label, _capSize.x * box.x, _capSize.y * box.y,
                                    maxFontSize: 0.40f, context: name);
            var mr = _label.GetComponent<MeshRenderer>();
            if (mr != null)
                mr.enabled = !(hasSymbol && CapSymbols.SymbolOnly(_capRole));
        }

        /// <summary>WHICH BOARD this cap belongs to — the atlas its role indexes into. Null on a
        /// cap that is deliberately not a board button (the map room's keycap-skinned furniture),
        /// which keeps the shared grain.</summary>
        private ControlBoard? _capStyle;

        /// <summary>
        /// Point this cap's face at a different <see cref="CapRole"/> in place.
        ///
        /// <para>Built for the follow/pin toggle, which is ONE control with TWO meanings — an
        /// anchor while the board is FIXIERT, two footprints while it FOLGEN — and has to say which
        /// one it currently is. A role is a sub-rectangle of one texture, so the swap is two float2
        /// writes on a material instance: no rebuild, no destroy, and therefore nothing for the
        /// dust dissolve to play over (a cap that is DESTROYED cannot crumble, which is the whole
        /// reason the item-use cap is built once and hidden rather than created on demand).</para>
        ///
        /// <para>Silently does nothing when this bundle has no atlas for the style — that cap still
        /// carries its word label, so its state is still readable.</para>
        /// </summary>
        internal void SetCapRole(CapRole role)
        {
            if (_capRole == role || _capStyle == null)
                return;
            if (!SetKeycapRole(_capMaterial, role, _capStyle.Value))
                return;
            _capRole = role;
        }
        /// <summary>USER DEBUG OPTION: [ButtonColors] per-category cap-FACE tint (multiplier, default
        /// white = unchanged) — set once at <see cref="Create"/> from the button's category. Applied
        /// to every state colour AND the native sprite face so the user can darken this cap group.</summary>
        private Color _capTint = Color.white;
        private bool _enabledState;
        private bool _accent;
        private bool _confirmed;
        /// <summary>
        /// SECONDS SINCE THIS CAP'S PRESS EDGE, or negative when no stroke is running.
        ///
        /// <para>It replaced a 0..1 AMPLITUDE that was set to 1 on the press and decayed
        /// linearly at 6/s — i.e. a cap that teleported to the bottom of its travel in one frame
        /// and rose back at constant speed. A phase is what lets the stroke have an attack, a
        /// detent and a rebound past rest (<c>WorldUI.ButtonStroke.Depth01</c>), and it is
        /// what lets the peer's mirror of this cap run the SAME function off the same phase
        /// instead of keeping a second copy of a decay rate.</para>
        /// </summary>
        private float _pressPhase = -1f;

        // Poke dwell state (test #19): the hand whose fingertip is charging the
        // press, hold start time, and the count of haptic ramp ticks already sent.
        private VRHand? _dwellHand;
        private float _dwellStart;
        private int _dwellTicks;

        // Finger-follow press (feature 6a): the hand currently hovering this button
        // in poke range. While set (and not dwelling) the cap continuously tracks the
        // fingertip's penetration depth so the puck physically sinks under the finger,
        // instead of a fire-then-spring flash. Cleared on OnPokeExit / SetVisible(false).
        private VRHand? _hoverHand;

        internal Collider? Collider { get; private set; }

        /// <summary>
        /// T4 antique restyle: DISABLED cap TOP — plain DARK WOOD, barely lighter than the
        /// board itself. A disabled key reads as an unlit carved plaque (the grain texture
        /// still shows), clearly "asleep" next to the parchment glow of an available one.
        /// </summary>
        private static readonly Color DisabledColor = new(0.21f, 0.16f, 0.11f);

        /// <summary>
        /// IDLE/AVAILABLE cap FIELD — the solid, opaque base every enabled key rests at
        /// ("this one you can press"); state accents tint from here.
        ///
        /// <para>PER BOARD SINCE ROUND 2, and it is an INSTANCE property for exactly that reason:
        /// the value depends on <see cref="_capStyle"/>. It used to be one warm parchment
        /// <c>(0.600, 0.510, 0.350)</c> shared by all three boards, which is what made an oak key
        /// and a bronze key measure ΔE 3.7 apart — the same colour. See
        /// <see cref="PlayTray.BoardIdleColor"/> for the three values, the old one, and the
        /// measurement that chose them.</para>
        /// </summary>
        private Color IdleColor => PlayTray.BoardIdleColor(_capStyle);

        /// <summary>
        /// Confirmed/readied state (test #19): ACTIVE = worn BRASS (T4: desaturated from
        /// the old saturated gold) — clearly distinct from both the muted CONFIRM accent
        /// and the parchment idle, matching the tray's brass "locked in" language. Shown
        /// while the game reports the player readied (revocable — pressing un-readies).
        /// </summary>
        private static readonly Color ConfirmedColor = new(0.68f, 0.52f, 0.24f);

        /// <summary>Charge tint the cap ramps toward while a poke dwell runs (test #19).
        /// T4: soft parchment, in key with the antique palette (was near-white).</summary>
        private static readonly Color DwellChargeColor = new(0.93f, 0.86f, 0.68f);

        /// <summary>
        /// How close the fingertip must stay to the cap for the dwell to keep
        /// charging — mirror of PokeInteractor.ReleaseRange (the interactor's
        /// re-arm hysteresis), meters at scale 1 × hand world scale.
        /// </summary>
        private const float DwellHoldRange = 0.02f;

        /// <summary>Cap rest position / authored full 4 mm press travel on the local Z (viewer side is -Z).</summary>
        private const float CapRestZ = -0.004f;
        private const float CapTravel = 0.004f;

        /// <summary>
        /// Per-instance press travel (user #9: configurable cap sink depth — also the
        /// distance the fingertip must push for the depth-fire press). Authored 4 mm;
        /// the CATEGORY-split ButtonTuning consumers pass their own section's Travel at
        /// <see cref="Create"/> (Confirm/Undo → [BoardButtons], gear/pin → [BoardDashboard])
        /// and rebuild on a config change, so a value never leaks across categories —
        /// uncategorized BoardButtons (rest discs) keep the authored constant.
        /// </summary>
        private float Travel { get; set; } = CapTravel;

        // Depth-fire press (user #6): fingertip contact no longer fires — the finger-follow
        // machinery fires exactly when the cap reaches PressFireFraction (~90%) of its full
        // travel, and re-arms only after the cap rose back past PressRearmFraction (~50%).
        // Releasing before the bottom = no fire, the cap springs back. Laser presses
        // (CardsDriver → Press(hand, "laser")) never pass through this and stay immediate.
        private bool _depthArmed = true;

        // Press DEBOUNCE (user: keycaps double-trigger like the pile stacks used to — port the
        // exact PileStack.OnPoke fix here). After a press commits, no second press fires until
        // the fingertip has both (a) retracted past PressRearmFraction so _depthArmed re-arms
        // AND (b) waited out this shared cooldown — killing the retract/re-entry and the
        // PokeInteractor hover-flicker (exit+enter inside one physical poke) re-fire the
        // hysteresis alone could not. The laser path (Press "laser") is cooldown-debounced too
        // (cross-path poke+laser double-fire), but never dwelled. Composes with the depth-fire:
        // the depth-fire at ~90% travel is still the press event; this only blocks the re-fire.
        private float _nextPressTime;

        // Dust-dissolve hide / materialize-from-dust show (user #7). The logical hide is INSTANT
        // (collider off, poke state dropped); only the visuals shrink out for
        // ButtonTuning.DissolveSeconds while the pooled dust burst plays. Appear reverses it:
        // converging dust + a surface fade-in, in place (no scale/grow pop).
        private bool _logicalVisible = true;

        /// <summary>The button's LOGICAL visibility (input + intent, not the dissolve visuals) —
        /// the state a peer's copy of this board must mirror. Read by the multiplayer board-UI
        /// seam (<c>PlayTray.ConfirmControlShown</c> / <c>UndoControlShown</c>).</summary>
        internal bool LogicalVisible => _logicalVisible;

        private float _hideLeft;   // dissolve shrink countdown, seconds
        private float _showLeft;   // materialize-from-dust fade-in countdown, seconds
        private Color _assemblyRest = Color.white; // the SETTLED cap colour the assembly ramp runs to (appear) / from (dissolve)
        private Vector3 _shownScale = Vector3.one;

        /// <summary>
        /// Unscaled wall-clock time this cap was created (<see cref="Create"/>) — the reference for
        /// <see cref="Settled"/>.
        ///
        /// <para>THIS REPLACED A `_ticked` FLAG, and the replacement is a bug fix, not a tidy-up
        /// (user report 2026-08-09: "die verschwinden und tauchen ohne die Animation die sonst kommt
        /// auf"). The flag was set in <see cref="Update"/> and gated both transitions, so it read
        /// "has this GameObject ever run a frame" — which is NOT the question. A cap that is built
        /// and immediately hidden (the item-use confirm, which now exists from the board's first
        /// frame and spends most of its life inactive) never runs a frame, so `_ticked` stayed false
        /// forever and EVERY show it ever played was suppressed as "initial state settling". The
        /// button that is shown and hidden the most often on the board was therefore the one button
        /// that could never animate. Wall-clock time answers the question the flag was standing in
        /// for — "is the board still assembling itself?" — for an active and an inactive cap
        /// alike.</para>
        /// </summary>
        private float _bornAt = float.NegativeInfinity;

        /// <summary>
        /// How long after a cap is created its show/hide stays SILENT. The board builds its keycaps
        /// and then immediately settles them against the live game state (TickStatus hides Confirm,
        /// Undo, the item-use cap on the very next tick) — animating that burst would mean every
        /// board build opens with a shower of dust for buttons the player never saw. One quarter of a
        /// second covers the build-then-settle storm with room for a bad frame, and is far below the
        /// gap between a build and any state change a player can cause.
        /// </summary>
        private const float BuildSettleSeconds = 0.25f;

        /// <summary>True once this cap is past its build-then-settle window, i.e. once a show/hide is
        /// a real event the player should see animate. See <see cref="_bornAt"/>.</summary>
        private bool Settled => Time.unscaledTime - _bornAt >= BuildSettleSeconds;

        // ---- SURFACE-FADE WATCHDOG ------------------------------------------------------------
        //
        // USER REPORT 2026-08-09: "Beim Testen hatte ich kurz die Situation, dass die Buttons wie
        // 'Bewegung bestätigen' und 'Wegpunkt löschen' unsichtbar waren - nur der Text auf den
        // Buttons war noch zu sehen - nach einer kurzen Zeit kamen sie wieder."
        //
        // THE ASYMMETRY IS THE WHOLE CLUE, and it points at exactly ONE place in this class. The cap
        // BODY and the cap LABEL are different renderers with different materials, and the ONLY code
        // that darkens the body without touching the label is the appear fade in Update. It USED TO
        // multiply the cap's top/bevel/wall colours by SmoothStep(0.15, 1, k), i.e. at k = 0 the cap
        // was at 15 % of its state colour. On this user's board that is not "dim", it is GONE: their
        // [ButtonColors] BoardCapTint is 0.5 (halved already), so the sage CONFIRM top
        // (0.35,0.46,0.28) rendered at 0.5 × 0.15 = (0.026,0.035,0.021) and the dark-wood disabled
        // top at (0.008,0.006,0.004) — black on a black board. The TMP label is a separate renderer
        // on its own font-material instance which the fade never touches, so it kept drawing at full
        // bright parchment. "Button invisible, only the text still there" was the literal rendered
        // result of a cap sitting in the first frames of that fade.
        //
        // THAT MULTIPLY IS GONE (2026-08-09 round 2 — the user reported it again, and the ModBuild 94
        // diagnostics below came back empty, which is what ruled out every OTHER explanation and left
        // the ramp itself). The appear no longer darkens anything: the cap now ASSEMBLES out of the
        // warm parchment-brass dust and cools into its state colour, and that ramp cannot render a
        // cap darker than its own rest colour at any user tint. See
        // WorldUI.ButtonTuning.AssemblyColor for the recipe and the invariant. The watchdog below
        // stays exactly as it was — a stalled animation is still a stalled animation, it just can no
        // longer strand the cap at "invisible", only at "a bit too bright".
        //
        // WHY IT LASTED LONG ENOUGH TO SEE, AND WHY IT HEALED BY ITSELF. The fade is authored at
        // 0.15 s and it had exactly ONE exit: the countdown reaching zero inside Update, which is
        // also the only call site that restores the true state colour (UpdateColor()). Two things
        // wrong with that:
        //   (1) IT RAN ON THE SCALED CLOCK (Time.deltaTime). Every other animation in this mod
        //       deliberately runs UNSCALED for the documented reason that the game stops simulation
        //       time behind menus and dialogs and during card phases (VRCard.ReleaseGlideSeconds,
        //       CardsDriver's BurnSlab, Rig/Flight, NonDominantHold — all say so at their fix site).
        //       Any frame where the scaled clock does not advance leaves the cap parked at 15 %.
        //   (2) NOTHING ELSE PUTS THE COLOUR BACK. SetState() early-returns when the state has not
        //       changed, so a cap left mid-fade is NOT repaired by the per-tick status pass — it
        //       stays dark until the countdown happens to run out or the game state changes on its
        //       own. That is the "it came back after a short time" half of the report: a recovery by
        //       luck (the next state flip), not by design.
        // The same freeze is reachable whenever this GameObject stops ticking mid-fade (its parent
        // anchor/tray deactivating), and the cluster twin of this code (ButtonCluster.PhysicalButton
        // .Animate) is worse still — its Animate() is skipped outright on any tick where the cluster
        // is not placed, so a frozen fade there needs no clock stall at all.
        //
        // THE FIX IS A DEADLINE, NOT A HOPE. Both countdowns now run on the UNSCALED clock like the
        // rest of the mod, AND each one is armed with a WALL-CLOCK deadline when it starts. Once the
        // deadline passes the animation is force-completed on the next tick — which runs the very
        // same completion path (scale restored, UpdateColor() re-seats the exact state colours), so
        // a cap can never be left dark by a stalled clock, a skipped tick or a deactivated parent.
        // The forced completion logs (throttled) with the wall-clock time the cap actually spent
        // faded, so the NEXT hardware log shows this window explicitly instead of depending on the
        // user noticing it.
        private float _showDeadline = float.PositiveInfinity; // unscaled wall clock; appear must be done by then
        private float _hideDeadline = float.PositiveInfinity; // unscaled wall clock; dissolve must be done by then
        private float _fadeStartedAt;                          // unscaled time the running fade was armed

        /// <summary>
        /// Wall-clock grace on top of a fade's authored duration before the watchdog force-completes
        /// it. Generous enough that a normal frame-time spike (this session's log carries 24–34 ms
        /// frames routinely and two &gt; 500 ms hitches) never trips it, tight enough that a genuinely
        /// stalled fade is repaired long before a player could read it as a broken button.
        /// </summary>
        private const float FadeWatchdogSlack = 0.35f;

        /// <summary>Throttle for the watchdog line — a relayout can force several caps at once.</summary>
        private static float _nextFadeHealLogAt;

        /// <summary>Names of the plate-less caps already reported (see <see cref="LogPlatelessCap"/>).
        /// A rebuild on every <c>ButtonTuning.Version</c> bump would otherwise print this line every
        /// time the user moves a slider.</summary>
        private static readonly System.Collections.Generic.HashSet<string> _platelessLogged = new();

        /// <summary>
        /// WHAT A CAP WITH NO WELL BEHIND IT IS SEATED AGAINST — once per named cap, at a tier the
        /// shipped log prints.
        ///
        /// <para><b>WHY IT EXISTS.</b> <c>ButtonTuning.SeatedCapColor</c> floors every cap FACE
        /// against <c>CapWellColor</c> so a cap can never render darker than the recess it sits in,
        /// and <c>LogCapSurface</c> states that comparison as "vs the WELL it sits in". Take the
        /// well away and that floor still runs, but its REFERENCE is gone: what is behind this cap
        /// now is the board's own face, or the scene. The number is deliberately UNCHANGED — the
        /// floor can only ever add light, this cap's shipped colour (0.58, 0.46, 0.26) clears it by
        /// a factor of three, and raising it would silently re-tint a button the user has already
        /// dialled in. But "unchanged and now measured against something else" is exactly the kind
        /// of fact that a later round re-derives from scratch because nothing wrote it down.</para>
        /// </summary>
        private static void LogPlatelessCap(string name, Color accent)
        {
            if (!_platelessLogged.Add(name))
                return;
            // HW-VERIFY
            VRLog.Note("Cards", $"PLATELESS CAP: '{name}' was built with NO 'Base' well behind it " +
                "(user request 6b: \"ich mag den schwebenden braunen Hintergrund nicht auf dem der " +
                "button sitzt - der button alleine reicht\"). It floats directly on whatever is " +
                "behind it. Its trigger collider is UNCHANGED — the seat plane the box is measured " +
                "back to is where the plate WOULD have been, so nothing about poke or laser reach " +
                $"moved. ButtonTuning.SeatedCapColor still floors its face against CapWellColor " +
                $"RGB({WorldUI.ButtonTuning.CapWellColor.r:F2}," +
                $"{WorldUI.ButtonTuning.CapWellColor.g:F2}," +
                $"{WorldUI.ButtonTuning.CapWellColor.b:F2}) even though no well is drawn there any " +
                $"more; this cap's accent RGB({accent.r:F2},{accent.g:F2},{accent.b:F2}) clears " +
                $"that floor already, so the floor is the identity here — floor engages: " +
                $"{WorldUI.ButtonTuning.CapSeatFloorEngages(accent)}. If the tester reports this " +
                "key reading as a hole rather than as a raised button, THAT boolean is the field " +
                "that decides whether the floor is the thing to raise.");
        }

        /// <summary>
        /// True when this cap's body was built WITHOUT the bundled <c>GloomhavenVR/BoardLit</c>
        /// shader (see <see cref="TryHealCapMaterial"/>). The competing hypothesis for the same user
        /// report is a cap whose material resolved to nothing, so the build path now records the
        /// fact and this flag drives both the log line and a bounded re-skin once the shader lands.
        /// </summary>
        private bool _capShaderFallback;

        /// <summary>Remaining bounded re-probe attempts for <see cref="TryHealCapMaterial"/>.</summary>
        private int _capHealBudget;

        /// <summary>Next unscaled time <see cref="TryHealCapMaterial"/> may re-probe.</summary>
        private float _nextCapHealAt;

        /// <summary>Re-probe cadence for the cap-material heal (cheap, but never per frame).</summary>
        private const float CapHealIntervalSeconds = 0.5f;

        /// <summary>Bounded re-probe attempts (~10 s of board life). A bundle that genuinely lacks
        /// the shader must not turn the heal into a probe storm of the mod's own making — the same
        /// budgeting rule <see cref="CardArtGuard"/> applies to its own reload heal.</summary>
        private const int CapHealAttempts = 20;

        /// <summary>
        /// Fingertip contact radius — mirror of <c>PokeInteractor.FingertipRadius</c>
        /// (private there), used by the finger-follow press to turn tip distance into
        /// a penetration depth. At tip-distance = this the finger just touches (depth 0).
        /// </summary>
        private const float FingertipRadius = 0.008f;

        /// <summary>
        /// Build a pressable board button. Rectangular by default (native 9-slice cap
        /// or procedural grey cube). Pass <paramref name="round"/> = true to build a
        /// ROUND disc sized to <paramref name="diameter"/> × <paramref name="thickness"/>
        /// that seats in the board's round rest-notches (feature 6a): the Base is a
        /// recessed dark "well" ring, the Cap a palette-tinted pressable puck, both SMOOTH
        /// generated 64-sided discs (<see cref="CardMesh.GetRoundCap"/> — user: Unity's
        /// primitive cylinder is only ~20-sided so a large round cap showed visible CORNERS;
        /// the disc is authored at real size in the button's local frame, front face toward
        /// the viewer along -Z, so no primitive rotation/scale is needed). Round buttons
        /// always use the procedural palette cap (the native skin's 9-slice is a rounded
        /// RECTANGLE, never a circle), so they read as turned-into-the-board discs. The
        /// existing cap-travel machinery, label and trigger collider are unchanged.
        ///
        /// <para><paramref name="wellPlate"/> = false builds the cap with NO "Base" well behind it —
        /// the key floats on whatever is behind it instead of on a plate of its own. User request 6b
        /// (2026-09-03), about the FIXIERT toggle specifically: <i>"ich mag den schwebenden braunen
        /// Hintergrund nicht auf dem der button sitzt - der button alleine reicht, lösche diese
        /// hintergrund mesh auf dem der button sitzt, er kann direkt unter dem controllboard
        /// schweben ohne Unterlage."</i></para>
        ///
        /// <para><b>WHY IT IS A PARAMETER AND NOT A DELETION.</b> This one method builds the well
        /// for EVERY board button — Confirm, Undo, Skip, the item-use confirm and both rest discs.
        /// Those five sit ON the board, in recesses the board's own art has, and their well is what
        /// makes the cap read as seated in one; deleting it here would take the plate off all six
        /// controls to answer a complaint about one. And it has to be a build-time parameter rather
        /// than a post-build cleanup, because <c>PlayTray.RebuildDashboardButtons</c> destroys and
        /// re-creates this cap on every <c>ButtonTuning.Version</c> bump — a cleanup pass would have
        /// to be remembered and re-run by every future caller, which is the same shape as the
        /// engraving that got re-created on every edit until somebody noticed the stack.</para>
        ///
        /// <para><b>2026-09-04: EVERY BOARD KEY IS BUILT WITHOUT IT NOW.</b> The user, about the
        /// control board's keys generally: they have "another thin plate floating BEHIND the button,
        /// sticking out at the back — I do not want it at all; the button asset alone, without these
        /// small plates behind it, is enough." That plate IS this well: a 6 mm slab 8 mm wider than
        /// the cap on both axes (round: 6 mm wider), seated 1..7 mm behind the seat plane while the
        /// cap holder sits 4 mm in front of it — so its rim shows around the cap as a brown border
        /// and its body sinks into the recess floor behind the key. Confirm, Undo, Skip, the
        /// item-use confirm (PlayTray.6.Build) and both rest discs (RestControls) pass
        /// <c>wellPlate: false</c> now, like the FIXIERT toggle already did. 2026-09-05: the combat
        /// log's pin passes it too, through <see cref="CreateFollowPin"/> — it is the SAME control as
        /// the board's toggle and hangs in the air beside a grab bar, so it lines no recess either.
        /// The parameter stays a parameter (default true) for the day a key is built into a recess
        /// again; nothing in the mod passes true today. The cap, its label and the trigger
        /// collider are exactly what they were — the collider's seat plane never depended on the
        /// plate being drawn. The KEYCAP SURFACE line now ends by naming the pieces a key is built
        /// from and the plate it has (none), and KEYCAP PLATE counts every renderer under a key that
        /// is neither its cap mesh nor its label, so "the plate is back" is a non-zero number in the
        /// log rather than a hardware round.</para>
        /// </summary>
        internal static BoardButton Create(Transform anchor, Vector2 size, Color accent,
            string fallbackLabel, System.Action onClick,
            bool round = false, float diameter = 0f, float thickness = 0.01f,
            bool overlay = false, bool boxy = false, float travel = CapTravel,
            WorldUI.ButtonTuning.CapCategory capCategory = WorldUI.ButtonTuning.CapCategory.Rest,
            CapRole capRole = CapRole.Plain, ControlBoard? capStyle = null,
            bool wellPlate = true)
        {
            // WHICH CONTROL THIS IS, and therefore which cell of which board's keycap atlas its
            // face wears (see CapSymbols). Resolved ONCE, here, because every downstream decision
            // — the material, the label's box, whether the cap carries a caption at all — is the
            // same decision. `capStyle` null (or a bundle with no atlas) leaves every one of them
            // exactly where it was before this existed: shared grain, centred caption, no symbol.
            bool hasSymbol = capStyle != null && capRole != CapRole.Plain
                             && CapSymbols.TryAtlas(capStyle.Value, out _, out _);
            // WHICH NON-SYMBOL CELL THIS CAP'S BEZEL RING AND WALLS TAKE. Resolved ONCE, here, for
            // the same reason capRole is: the build path writes it, the bounded material heal
            // re-writes it later, and the two must not be able to disagree. The round cell exists
            // because cell 0's gold band is registered against a SQUARE — a disc that sampled it
            // painted a mitred rectangle inside a circular cap, which is what the user reported.
            CapRole plainCell = CapCellMath.PlainCell(round);
            var go = new GameObject($"BoardButton_{fallbackLabel}");
            go.transform.SetParent(anchor, worldPositionStays: false);

            // Round buttons take their footprint from the diameter (square bounds for the
            // label fit / trigger box); rectangular buttons keep their explicit size.
            if (round && diameter > 0f)
                size = new Vector2(diameter, diameter);

            // Round caps (Base well + Cap puck) are SMOOTH generated discs now (user: the
            // 'round' buttons showed visible CORNERS from Unity's ~20-sided primitive cylinder) —
            // CardMesh.GetRoundCap authors the disc directly in the button's local frame (front
            // face toward the viewer, -Z), so no primitive rotation/scale is needed.
            // NO PLATE AT ALL when the caller opted out (user request 6b — see the parameter's note
            // on Create). Null, not disabled: an inactive renderer is still a renderer that every
            // sweep in this mod has to classify, the appear/dissolve pass would still be handed it,
            // and the next reader would have to work out whether "Base exists but is off" is the
            // shipped state or a bug. Nothing else in the button derives from it — the Cap sits at
            // its own CapRestZ under the button root, the caption is a sibling under the ANCHOR
            // (BoardEngraving, not a child of the plate) and the trigger collider is independent
            // geometry — so its absence removes exactly one mesh and nothing else.
            GameObject? basePlate = null;
            if (!wellPlate)
            {
                // deliberately nothing
            }
            else if (round)
            {
                // Smooth generated disc (user: the 'round' buttons showed visible CORNERS — Unity's
                // primitive cylinder is only ~20-sided). Identity rotation / unit scale: the mesh is
                // authored at REAL size (diameter × 6 mm thickness, centred on the origin), matching
                // the old flattened-cylinder footprint. Slightly wider than the cap → a visible
                // recessed well ring around the puck. Cached/shared across identical caps.
                basePlate = new GameObject("Base");
                basePlate.transform.SetParent(go.transform, worldPositionStays: false);
                basePlate.AddComponent<MeshFilter>().sharedMesh =
                    CardMesh.GetRoundCap(size.x + 0.006f, 0.006f);
                basePlate.AddComponent<MeshRenderer>();
                basePlate.transform.localPosition = new Vector3(0f, 0f, 0.004f);
            }
            else
            {
                basePlate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                basePlate.name = "Base";
                Object.Destroy(basePlate.GetComponent<Collider>());
                basePlate.transform.SetParent(go.transform, worldPositionStays: false);
                basePlate.transform.localScale = new Vector3(size.x + 0.008f, size.y + 0.008f, 0.006f);
                basePlate.transform.localPosition = new Vector3(0f, 0f, 0.004f);
            }
            // Item 5/6: board-HUD buttons (gear / follow-toggle) route their body to the
            // Overlay shader so RenderOnTop can force it over the opaque board; CONFIRM /
            // UNDO / rest buttons keep Standard (they stay depth-correct, no RenderOnTop).
            // Item 1b: a solid warm dark-WOOD surround (not a near-black void) so the recessed
            // well around the cap reads as part of the physical panel, not a hole under a glassy key.
            // The colour is ButtonTuning.CapWellColor because SeatedCapColor floors every cap face
            // against it (2026-08-09 round 3) — a floor and its reference must not be able to drift.
            if (basePlate != null)
                Tint(basePlate, WorldUI.ButtonTuning.CapWellColor, overlay: overlay);
            else
                LogPlatelessCap(go.name, accent);

            // Native look (test #25 item 3): when a live game button has been sampled,
            // the travelling cap is an EMPTY holder carrying the game's own 9-sliced
            // button sprite (WorldUI.NativeButtonSkin) on a unit-scale face — so it is
            // indistinguishable from a native widget. Otherwise it falls back to the
            // procedural grey cube cap. Either way the holder sits at CapRestZ and
            // travels on press; the collider (on go) is independent of it.
            Material? capMaterial = null;
            Material? capBevelMaterial = null; // item 4: the bright bevel-ring material instance (boxy caps only)
            Material? capWallMaterial = null;  // item 4: the dark warm side-wall material instance (boxy caps only)
            // 2026-08-09 invisible-cap round: true when the cap body did NOT get the bundled
            // GloomhavenVR/BoardLit shader (bundle not loadable yet). Drives the build-time log line
            // and the bounded in-place re-skin in TryHealCapMaterial.
            bool capShaderFallback = false;
            SpriteRenderer? capFace = null;
            Renderer? capMeshRenderer = null; // item A diagnostic: the cap body renderer (cube/disc/sprite)
            var cap = new GameObject("Cap");
            cap.transform.SetParent(go.transform, worldPositionStays: false);
            cap.transform.localPosition = new Vector3(0f, 0f, CapRestZ);
            float labelZ = -0.007f; // default: proud of the flat cap face (viewer side, -Z)
            // Item 3 (laser): the frontmost (viewer-side, -Z) local-Z of the cap BODY, so the
            // trigger collider below can be sized to span the WHOLE protruding cap — the laser
            // reticle then lands on the visible cap FACE. Default covers the flat native/procedural
            // face (≈ -8 mm); the round disc and boxy keycap branches override it with their real
            // protrusion (a beveled keycap reaches CapRestZ - capThick ≈ -34 mm).
            float capFrontZ = CapRestZ - 0.004f;

            if (round)
            {
                // Round pressable puck: a smooth GENERATED disc (user: the 'round' buttons showed
                // visible CORNERS — Unity's primitive cylinder is only ~20-sided, so a large round
                // cap read as a faceted polygon). CardMesh.GetRoundCap builds a 64-sided disc at
                // REAL size (diameter × thickness), identity-rotated / unit-scaled, so its front
                // face still protrudes thickness/2 toward the viewer (-Z) exactly like the old
                // flattened cylinder. Planar XY UVs let the shared carved-grain keycap _MainTex map
                // across the round face (see below); one submesh = one keycap material. Sits proud
                // of the well toward the viewer and travels with the cap holder.
                float capThickR = Mathf.Max(0.002f, thickness);
                var capDisc = new GameObject("CapMesh");
                capDisc.transform.SetParent(cap.transform, worldPositionStays: false);
                // ROUND 2: the disc is a SIGNET PLATE too — outer chamfer, flat rim land, inner
                // chamfer, recessed field — so the round rest pads and the square generic keys read
                // as one family of control rather than as a key beside a puck. Same three submeshes,
                // so the field / bezel / wall materials below drive either shape unchanged.
                // CardMesh.BuildRoundKeycap, NOT BuildRoundCap: that plain disc still has three
                // other callers (this cap's own backing ring, its mirror's, and the map room's
                // button rail) and every one of them assigns a single sharedMaterial.
                capDisc.AddComponent<MeshFilter>().sharedMesh = CardMesh.GetRoundKeycap(size.x, capThickR, capStyle);
                capDisc.AddComponent<MeshRenderer>();
                capFrontZ = CapRestZ - capThickR * 0.5f; // disc protrudes half its thickness toward the viewer
                // The caption floats just proud of the RECESSED FIELD, not of the rim land — the
                // field is one step BACK now, and a label seated at the old plane would hover a
                // visible gap in front of the plate it is supposed to be cut into.
                CardMesh.CapProfile(size.x, size.x * 0.5f, capThickR, out _, out _, out float discStep);
                labelZ = -(capThickR * 0.5f) + discStep - CardMesh.LabelProudOfField;
                // User (rest-cap alignment): the round rest puck now wears the SAME antique keycap
                // SURFACE as the square Confirm/Undo/gear/Fixiert caps. Previously this branch used a
                // bare Standard material with a flat state colour — a plain plastic puck — while the
                // boxy caps route through NewKeycapMaterial(BoxCapShader()), which shades via BoardLit
                // (lit even in unlit scenes) AND carries the shared carved wood/parchment grain on
                // _MainTex (grayscale grain × the per-state colour). Use that exact material path here
                // so the disc reads as one of the same keycap family. SHAPE STAYS ROUND (the user asked
                // for the same TEXTURE, not the same shape); the cylinder has one cap material (no
                // bevel/wall submeshes), and SetCapColor drives its top colour exactly as before, so the
                // per-rest accent tints (parchment-gold / slate-blue) and the [ButtonColors] RestCapTint
                // are untouched. The engraved parchment label (StyleEngravedLabel below) already matches.
                Shader? shader = BoxCapShader();
                capShaderFallback = shader == null || !shader.name.Contains("BoardLit");
                if (shader != null)
                {
                    capMaterial = NewKeycapMaterial(shader, DisabledColor, capRole, capStyle);       // [0] field
                    capBevelMaterial = NewKeycapMaterial(shader, BevelTint(DisabledColor), plainCell, capStyle); // [1] bezel — ROUND-registered band
                    capWallMaterial = NewKeycapMaterial(shader, WallTint(DisabledColor), plainCell, capStyle);   // [2] wall — ROUND-registered band
                    capDisc.GetComponent<MeshRenderer>().sharedMaterials =
                        new[] { capMaterial, capBevelMaterial, capWallMaterial };
                    Core.VRLog.Info("Cards", $"BoardButton '{fallbackLabel}': ROUND cap skinned with the shared " +
                                             "carved-grain keycap material (BoardLit + KeycapGrain _MainTex) — same antique " +
                                             "wood/parchment surface as the square board keycaps; shape stays round (64-seg " +
                                             "generated disc), accent tint kept.");
                }
                else
                {
                    // Shaderless environment: the generated disc has no default material (unlike the
                    // old CreatePrimitive cylinder), so seat a plain Standard-tinted material to match
                    // the pre-fix fallback look — never render an unmaterialed (magenta) disc.
                    Shader? fb = Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
                    if (fb != null)
                    {
                        capMaterial = new Material(fb) { color = DisabledColor };
                        CardMesh.ApplyEmissionFloor(capMaterial); // round 15: Standard is black in the dark scenes
                        // ALL THREE submeshes get the same instance. sharedMaterial alone would fill
                        // slot 0 and leave the bezel and the wall with a NULL material, which Unity
                        // draws as nothing at all — a rim-less floating field, i.e. a worse picture
                        // than the flat fallback this branch exists to provide.
                        capDisc.GetComponent<MeshRenderer>().sharedMaterials =
                            new[] { capMaterial, capMaterial, capMaterial };
                    }
                }
                capMeshRenderer = capDisc.GetComponent<MeshRenderer>();
            }
            else if (boxy)
            {
                // PART C / item 4/5: a REAL 3D square keycap with a CHAMFERED front edge — its
                // side must read as a solid protruding button, not "a floating square with text".
                // Previous attempts split a plain cube into top + darker walls, but BOTH stayed
                // dark (top ~0.24, wall ~0.11) → viewed near top-down against a dark board the
                // walls had near-zero contrast and vanished. "Darker" was the wrong lever. The cap
                // now has THREE submeshes with a big value+hue gradient, and a lit 45° BEVEL RING
                // that catches light and frames the top so it reads RAISED from any angle:
                //   [0] top plateau  = the state colour (semantic)
                //   [1] bevel ring   = BRIGHT parchment/brass (BevelTint) — the catch-light edge
                //   [2] side walls   = dark WARM band (WallTint), distinct from the neutral board
                // Geometry is authored at REAL size (CardMesh.BuildBeveledKeycap) so the holder
                // stays UNIT-scaled — uniform scale keeps the bevel a true 45° in world space so
                // BoardLit (shades by world normal, baked-lit, works in the unlit scenes) lights it
                // brighter than top or wall. NO RenderOnTop; the cap protrudes toward the viewer
                // (front plateau at −capThick) and travels inward on press. The three material
                // instances all track button state (UpdateColor → SetCapColor).
                float capThick = Mathf.Max(0.012f, thickness);
                var capCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                capCube.name = "CapMesh";
                Object.Destroy(capCube.GetComponent<Collider>());
                capCube.transform.SetParent(cap.transform, worldPositionStays: false);
                capCube.transform.localScale = Vector3.one;       // mesh is authored at real size
                capCube.transform.localPosition = Vector3.zero;   // mesh already spans −capThick..0
                capFrontZ = CapRestZ - capThick; // the beveled plateau protrudes the full cap thickness toward the viewer
                // GEOMETRY FIRST, MATERIAL SECOND (2026-08-09 invisible-cap round): the beveled
                // keycap mesh does not depend on any shader, so it is built unconditionally. It used
                // to sit INSIDE the `shader != null` branch, which meant a cap built before the
                // bundle was loadable kept Unity's 1×1×1 primitive cube — and could then never be
                // repaired in place, because the heal would have had to re-author the mesh too.
                var mf = capCube.GetComponent<MeshFilter>();
                if (mf != null)
                    mf.sharedMesh = CardMesh.BuildBeveledKeycap(size.x, size.y, capThick, capStyle);
                Shader? shader = BoxCapShader();
                capShaderFallback = shader == null || !shader.name.Contains("BoardLit");
                if (shader != null)
                {
                    // Task #5a: each submesh material carries the shared carved-grain texture on
                    // _MainTex (grayscale grain × the state/bevel/wall tint) when the bundle ships
                    // it — a real wood/parchment surface — else EXACTLY the prior plain tint.
                    // ONLY THE TOP PLATEAU TAKES THE ROLE CELL. The bevel ring and the walls take
                    // the PLAIN cell of the same board's atlas: they are the same material as the
                    // face and must read as one piece of it, but the symbol belongs on the face
                    // the player looks at, and a bevel quad's UVs run OUT to the cap's full
                    // footprint, so a role cell there would draw the symbol's outer edge smeared
                    // around the rim.
                    capMaterial = NewKeycapMaterial(shader, DisabledColor, capRole, capStyle);      // [0] top
                    capBevelMaterial = NewKeycapMaterial(shader, BevelTint(DisabledColor), plainCell, capStyle); // [1] bright bevel
                    capWallMaterial = NewKeycapMaterial(shader, WallTint(DisabledColor), plainCell, capStyle);   // [2] dark warm wall
                    capCube.GetComponent<MeshRenderer>().sharedMaterials =
                        new[] { capMaterial, capBevelMaterial, capWallMaterial };
                }
                capMeshRenderer = capCube.GetComponent<MeshRenderer>();
                // The caption floats just proud of the RECESSED FIELD (at -capThick + step), not of
                // the rim land (at -capThick). Seating it at the old plane would leave it hovering a
                // visible step in front of the plate whose surface it is meant to be cut into, and
                // that gap parallaxes the moment the player looks along the board.
                CardMesh.CapProfile(Mathf.Min(size.x, size.y), Mathf.Min(size.x, size.y) * 0.5f, capThick,
                                    out _, out _, out float capStep);
                labelZ = -capThick + capStep - CardMesh.LabelProudOfField;
            }
            else
            {
                // Face proud of the base plate (viewer side, -Z), just behind the label.
                // Item 5/6: the native 9-slice face renders through the Overlay material
                // (a SpriteRenderer samples the sprite via _MainTex) so a board-HUD button
                // cap (gear/toggle, RenderOnTop'd) can be forced ZTest-Always over the
                // board. CONFIRM/UNDO get the same material but look identical (LEqual)
                // since they are not RenderOnTop'd. Null (Overlay absent) → default sprite
                // material, the pre-fix look.
                capFace = WorldUI.NativeButtonSkin.CreateFace(cap.transform, size, localZ: -0.004f,
                    sortingOrder: 1, overrideMaterial: OverlayMaterial(Color.white));
                capMeshRenderer = capFace;
                if (capFace == null)
                {
                    // Procedural fallback: the original squashed grey cube cap.
                    var capCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    capCube.name = "CapMesh";
                    Object.Destroy(capCube.GetComponent<Collider>());
                    capCube.transform.SetParent(cap.transform, worldPositionStays: false);
                    capCube.transform.localScale = new Vector3(size.x, size.y, 0.008f);
                    // Item 5/6: board-HUD button (gear/toggle) procedural cap routes to
                    // Overlay so RenderOnTop's _ZTest takes; others keep Standard.
                    Shader? shader = overlay ? OverlayShader() : null;
                    shader ??= Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
                    if (shader != null)
                    {
                        capMaterial = new Material(shader) { color = DisabledColor };
                        CardMesh.ApplyEmissionFloor(capMaterial); // round 15: Standard is black in the dark scenes
                        capCube.GetComponent<MeshRenderer>().sharedMaterial = capMaterial;
                    }
                    capMeshRenderer = capCube.GetComponent<MeshRenderer>();
                }
            }

            // Item 4: the label is parented to the CAP HOLDER (which is unit scale — only
            // its child mesh/face is non-uniformly scaled, so no distortion) so it TRAVELS
            // WITH the cap when the button is pressed (it used to hang off the static root
            // while only the cap sank, reading as detached). Held slightly proud of the cap
            // face on the viewer side (-Z), above it by sortingOrder so it never clips.
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(cap.transform, worldPositionStays: false);
            // THE SYMBOL AND THE CAPTION SHARE ONE FACE, and the split is authored in ONE place:
            // CapSymbols mirrors the very numbers cap_atlas.py laid the symbol out with, so the
            // caption drops into the band BELOW the carved symbol instead of through it. With no
            // atlas in this bundle the offset is 0 and the box is the pair this call has always
            // passed, i.e. dead centre and full size.
            float labelDy = CapSymbols.LabelCentreY(capRole, hasSymbol) * size.y;
            Vector2 labelBox = CapSymbols.LabelBox(capRole, hasSymbol);
            labelGo.transform.localPosition = new Vector3(0f, labelDy, labelZ); // proud of the cap face (viewer side, -Z)
            var tmp = labelGo.AddComponent<TextMeshPro>();
            tmp.text = fallbackLabel;
            tmp.alignment = TextAlignmentOptions.Center;
            // Native label (test #25 item 3): the game's HUD font (MarcellusSC) + its
            // parchment-gold button-text colour when the skin has been sampled; plain
            // white otherwise (procedural fallback).
            tmp.color = WorldUI.NativeButtonSkin.HasFont ? WorldUI.NativeButtonSkin.LabelColor : Color.white;
            WorldUI.NativeButtonSkin.ApplyFont(tmp);
            WorldUI.NativeButtonSkin.StyleEngravedLabel(tmp); // T4: parchment glyphs carved into the cap
            // Draw the label ABOVE the native sprite face (test #26): both are transparent
            // renderers with ZWrite off, so sorting order — not the label's nearer z —
            // decides who wins. The face uses sortingOrder 1; without this the sprite drew
            // over the text and the button showed no readable label.
            var labelRenderer = labelGo.GetComponent<MeshRenderer>();
            if (labelRenderer != null)
                labelRenderer.sortingOrder = 3;
            // Fit inside the cap face: localized CONFIRM/UNDO strings (SetLabel
            // mirrors the game's texts) shrink/wrap inside the button instead of
            // spilling over its edges (TmpFit, test #12).
            Core.TmpFit.FitCapLabel(tmp, size.x * labelBox.x, size.y * labelBox.y,
                                    maxFontSize: 0.40f, context: go.name);
            // A SYMBOL-ONLY CAP DRAWS NO CAPTION AT ALL — the rest pads and the follow/pin toggle.
            // Their word is ENGRAVED INTO THE BOARD beside them (BoardEngraving), which is what
            // the user asked for ("nativ und immersiv in dem board verarbeitet, nicht einfach als
            // schwebender Text darüber"), so a caption here would be the same word twice. The
            // renderer is disabled rather than the object destroyed: the string is still the one
            // the multiplayer cap-label seam reads (PlayTray.ConfirmControlLabel and friends read
            // CurrentLabel), and a bundle without the atlas needs the caption back with no
            // rebuild.
            if (hasSymbol && CapSymbols.SymbolOnly(capRole) && labelRenderer != null)
                labelRenderer.enabled = false;

            // Item 3 (laser fix): the trigger collider must SPAN the full protruding cap so a
            // laser ray aimed at the visible cap FACE registers a hit. The old fixed box
            // (size.z 0.02, centre -0.004 → front face -0.014) fell 20 mm SHORT of a boxy
            // beveled keycap's front plateau (CapRestZ - capThick ≈ -0.034): the laser passed
            // over the box and never landed, so gear/pin (and any Square-shaped cap) could not
            // be laser-pressed even though poke worked (the fingertip reaches the box from its
            // 35 mm hover). Size the box from the cap's own frontmost local-Z back to the SEAT
            // PLANE behind it, so it hugs the whole visible cap for every shape.
            //
            // THE SEAT PLANE IS NOT THE PLATE, AND THAT DISTINCTION IS NOW LOAD-BEARING. This
            // number was written as "base plate rear face (localPos.z 0.004 + half depth 0.003)",
            // which is true and is also a comment that goes stale the moment a cap is built with no
            // plate (user request 6b — the FIXIERT toggle). What 7 mm actually is, and always was,
            // is the local-Z of the SURFACE THE CAP IS SEATED AGAINST: the plate is drawn there
            // when there is one, and the board face is there when there is not. So the collider is
            // deliberately IDENTICAL either way — the same reach, the same hit box, the same laser
            // and poke behaviour on a button the user has already tuned through six rounds. Taking
            // a mesh out of the picture must not quietly re-shape what the finger can hit.
            const float baseBackZ = 0.007f;                     // the cap's seat plane; see above
            float boxFrontZ = Mathf.Min(capFrontZ, -0.006f);    // never shallower than the old front
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(size.x, size.y, baseBackZ - boxFrontZ);
            box.center = new Vector3(0f, 0f, (boxFrontZ + baseBackZ) * 0.5f);
            box.isTrigger = true;

            var button = go.AddComponent<BoardButton>();
            button._bornAt = Time.unscaledTime; // starts the build-then-settle window (see the field)
            button._onClick = onClick;
            // 2026-08-09 invisible-cap round: a cap that could NOT resolve the bundled BoardLit
            // shader says so ONCE, right here, naming itself — so the next hardware log distinguishes
            // "the material never resolved" from "the surface fade stalled" without any inference.
            // TryHealCapMaterial then re-skins it in place the moment the shader turns up.
            button._capShaderFallback = capShaderFallback && capMaterial != null && capFace == null;
            button._capHealBudget = button._capShaderFallback ? CapHealAttempts : 0;
            if (button._capShaderFallback)
                VRLog.Warn("Cards", $"KEYCAP MATERIAL MISSING: '{go.name}' was built WITHOUT " +
                    "'GloomhavenVR/BoardLit' (the mod bundle was not loadable at build time) and wears " +
                    "the flat Standard/Sprites fallback — its walls and bevel will not shade. Re-probing " +
                    $"every {CapHealIntervalSeconds:F2} s for up to {CapHealAttempts} attempts; a " +
                    "'KEYCAP MATERIAL HEALED' line follows when the shader lands.");
            button._capMaterial = capMaterial;
            button._capBevelMaterial = capBevelMaterial; // item 4: bright bevel-ring instance (null on non-boxy caps)
            button._capWallMaterial = capWallMaterial;   // item 4: dark warm side-wall instance (null on non-boxy caps)
            button._capFace = capFace;
            button._capMeshRenderer = capMeshRenderer;
            button._label = tmp;
            button._cap = cap.transform;
            button._accentColor = accent;
            button._capRole = capRole;
            button._plainCell = plainCell;
            button._capStyle = capStyle;
            button._symbolApplied = hasSymbol;
            button._capSize = size;
            // A cap that WANTS a symbol and did not get one has to be given a heal runway, exactly
            // as a cap that missed the shader is. Without this the budget is 0 for every cap whose
            // shader resolved, and the atlas heal below could never run on the one case it exists
            // for — a gated remedy that never executes, which this project has shipped before.
            if (!button._capShaderFallback && capStyle != null && capRole != CapRole.Plain && !hasSymbol)
                button._capHealBudget = CapHealAttempts;
            // USER DEBUG OPTION: this button's [ButtonColors] cap-face tint (default white = no
            // change). Applied to every state colour (StateColor) and the native sprite face
            // (UpdateColor). The category defaults to Rest so RestControls — which does not pass
            // one — picks up the rest tint; Confirm/Undo pass Board, gear/follow pass Dashboard.
            WorldUI.ButtonTuning.Bind();
            button._capTint = WorldUI.ButtonTuning.CapTint(capCategory);
            button.Collider = box;
            button.Travel = travel; // per-category press travel (category split; rest discs keep the authored default)
            button.UpdateColor(); // seat the initial (disabled) native/procedural face tint

            // 2026-08-04 (status-placard defect family): the button's TRANSPARENT parts — the
            // native face sprite (sortingOrder 1) and the engraved TMP label (3) — write no
            // depth, so a converted panel BEHIND the board painted over them. Join the board's
            // furniture order group (opaque base/cap meshes are skipped by the queue filter);
            // the captured relative orders keep label-over-face intact inside the band. Runs
            // AFTER every sortingOrder write above; keycap rebuilds re-adopt here and the dead
            // renderers are pruned by the group on its next order apply.
            AdoptFurniture(go);
            // MOD LAYER (2026-08-09 round 3). PlayTray runs Core.VRLayers.Apply over the tray root
            // exactly ONCE, at board build, AFTER BuildButtons — so the caps of the FIRST build are
            // on the mod layer and every cap born later is not. RebuildAttachedControls,
            // RebuildDashboardButtons and RestControls.EnsureBuilt all DestroyImmediate their caps
            // and `new GameObject` the replacements (this session's log shows that happening ten
            // times over from live [BoardButtons] tuning alone), and Unity does not inherit a layer
            // on re-parenting: those caps sat on layer 0 while their siblings sat on the mod layer.
            // That is a real split-brain — the mod layer is what the head camera's mask, the stereo
            // mirror and every MixedReality renderer sweep key off (MixedReality's sky sweep skips
            // ModLayer/UI and then calls `r.enabled = false` on what is left). Layer the cap at the
            // ONE place every cap is born instead; idempotent for the first build, and the same
            // pattern RayInteractor's lazily-created laser/reticle already use.
            Core.VRLayers.Apply(go);
            button.LogCapSurface("BUILT");
            return button;
        }

        /// <summary>The FIXIERT/FOLGEN cap's face colour: aged brass, desaturated from the loud
        /// gold it wore before T4. One literal for every follow/pin toggle in the mod.</summary>
        private static readonly Color FollowPinAccent = new(0.58f, 0.46f, 0.26f);

        /// <summary>
        /// <b>THE FIXIERT/FOLGEN KEYCAP — ONE CONSTRUCTION, EVERY OWNER.</b> The control board's
        /// dashboard toggle (<c>PlayTray.CreateDashboardButtons</c>) and the combat log's pin
        /// (<c>CombatLogSurface</c>) are the same control doing the same job on two different pieces
        /// of furniture, so they are built by one call with one set of dials instead of by two call
        /// sites carrying the same eleven arguments.
        ///
        /// <para><b>THE REPORT THAT COLLAPSED THEM (user, 2026-09-05, verbatim):</b> <i>"der fixiert
        /// button sollte gleich sein wie der button am controllboard"</i>, with the standing
        /// instruction <i>"Am liebsten wäre es mir wenn du so wenig extra code nur für den Kampflog
        /// hast wie möglich und es wie ein normales Fenster behandelst."</i> The combat log's pin was
        /// a hand-sized rounded gold cap (<c>Vector2(0.068, 0.030)</c>, <c>Color(0.75,0.55,0.2)</c>,
        /// no <c>boxy</c>, no travel dial, no cap category, no engraved SYMBOL); the board's was the
        /// [BoardDashboard] tuning set with a symbol that swaps on every toggle. Two caps built from
        /// two argument lists cannot be kept equal by intention, which is what the user was
        /// looking at.</para>
        ///
        /// <para><b>WHAT IS DELIBERATELY NOT IN HERE.</b> The tray's own <c>WireCap</c> (the peer
        /// mirror of a board press) and its laser registration are set by the tray at its call site,
        /// because they are statements about the CONTROL BOARD and not about this control: the
        /// combat log's pin flips a local BepInEx key and nothing about it goes on the wire. The
        /// board's engraved caption beside the cap is likewise the board's — it is cut into the
        /// board's own bottom margin and there is no board under a combat-log panel.</para>
        ///
        /// <para><b>wellPlate: false</b> for the same reason the board's does: the well is a plate
        /// that makes a key read as seated in a recess, and neither of these two caps sits in one —
        /// the board's toggle hangs below the board's bottom edge and the combat log's hangs beside
        /// a grab bar in mid-air.</para>
        /// </summary>
        /// <param name="follow">The state the control is in RIGHT NOW: true = FOLGEN (two
        /// footprints), false = FIXIERT (an anchor). Only decides which symbol is baked at build
        /// time; <see cref="ApplyFollowPinState"/> owns every later flip.</param>
        internal static BoardButton CreateFollowPin(Transform anchor, bool follow,
                                                    System.Action onClick)
        {
            WorldUI.ButtonTuning.Bind();
            return Create(anchor,
                new Vector2(WorldUI.ButtonTuning.DashboardPinWidth, WorldUI.ButtonTuning.DashboardHeight),
                FollowPinAccent,
                // The FALLBACK label, i.e. the word this cap wears when the bundle ships no keycap
                // atlas. It is the constant "follow" string at BUILD time on both owners (it also
                // names the GameObject, `BoardButton_<label>`, and a GameObject whose name depends on
                // a config value is a name that changes under the player); the state word arrives
                // through ApplyFollowPinState on the very next line at both call sites.
                Core.Loc.Mod("follow"), onClick,
                thickness: WorldUI.ButtonTuning.DashboardDepth, boxy: true,
                travel: WorldUI.ButtonTuning.DashboardTravel,
                capCategory: WorldUI.ButtonTuning.CapCategory.Dashboard,
                capRole: follow ? CapRole.FixedFollow : CapRole.FixedPinned,
                capStyle: CardsConfig.CurrentBoard,
                wellPlate: false);
        }

        /// <summary>
        /// Put a follow/pin cap into the state it is actually in — the ACCENT, the WORD and the
        /// engraved SYMBOL, from one read of one bool, in one call, so a cap can never show an
        /// anchor while its own colour says FOLGEN.
        ///
        /// <para>The symbol swap is two floats on a material instance
        /// (<see cref="SetCapRole"/>), so there is no rebuild and the dust dissolve never sees it —
        /// which matters for a toggle the player presses repeatedly, and it is the half the combat
        /// log's pin was missing entirely before 2026-09-05.</para>
        ///
        /// <para>The tray ALSO cuts the word into the board beside the cap
        /// (<c>PlayTray.RefreshFollowEngraving</c>) and keeps that half at its own call site: an
        /// engraving needs a board to be cut into.</para>
        /// </summary>
        internal static void ApplyFollowPinState(BoardButton? cap, bool follow)
        {
            if (cap == null)
                return;
            cap.SetState(true, accent: !follow);
            cap.SetLabel(follow ? Core.Loc.Mod("follow") : Core.Loc.Mod("pinned"));
            cap.SetCapRole(follow ? CapRole.FixedFollow : CapRole.FixedPinned);
        }

        /// <summary>
        /// Built ONLY when a press is rejected — explains the disabled state
        /// (e.g. the CanConfirm gate inputs). Wired by <see cref="BuildButtons"/>.
        /// </summary>
        internal System.Func<string>? DisabledReason;

        /// <summary>
        /// Deliberate-poke dwell (test #19): &gt; 0 makes a fingertip CONTACT only
        /// start a hold — the tip must stay on the cap this many seconds before the
        /// press fires (cap sinks + brightens while charging; retracting cancels).
        /// 0 (default) keeps the fire-on-contact behavior. Laser presses are never
        /// dwelled — <see cref="Press"/> with source "laser" stays immediate.
        /// No button sets this anymore — every physical press fires on contact
        /// (user directive round 7); the accident protection that remains is
        /// <see cref="ActivationGuard"/>.
        ///
        /// CONFIGURED OFF BY USER DIRECTIVE — NOT vestigial, and the distinction matters in BOTH
        /// directions. `239acb5` removed the dwell because the user asked for it, while explicitly
        /// keeping the ActivationGuard accident window. So: do not delete DwellHoldRange /
        /// _dwellHand / TickDwell / BeginDwell / CancelDwell / DwellChargeColor as unused code
        /// (~90 lines that look dead only because this value is 0), and do not set this above 0
        /// as "clearly what it was for" — that reverses a decision the user made.
        /// </summary>
        internal float DwellSeconds = 0f;

        /// <summary>
        /// Optional activation suppressor: returns the seconds REMAINING of a
        /// suppression window (≤ 0 = free). Checked for EVERY source at fire time —
        /// the test #19 accident window after slot drops/plucks. Wired for CONFIRM
        /// by <see cref="BuildButtons"/>.
        /// </summary>
        internal System.Func<float>? ActivationGuard;

        /// <summary>This cap's live ACCENT state — the multiplayer cap-STATE read seam (board-UI
        /// record byte 2, via <c>PlayTray.ConfirmCapAccent</c>). Reading the flag the renderer
        /// itself obeys is what makes the mirrored cap's colour the owner's colour by construction,
        /// instead of a second derivation of the game rules that can drift from it. The ENABLED
        /// flag has no such reader: nothing on the wire carries it, so it is read only inside this
        /// class.</summary>
        internal bool StateAccent => _accent;

        /// <summary>
        /// This cap's live ENABLED state — the multiplayer cap-STATE read seam for the turn-flow
        /// SKIP (board-UI record byte 2's <c>BoardUiCapSkipEnabledBit</c>, via
        /// <c>PlayTray.SkipCapEnabled</c>).
        ///
        /// <para>The sentence above ("the ENABLED flag has no such reader") was true while the skip
        /// cap belonged to <c>WorldUI.ButtonCluster</c> and published its own static. It does not
        /// any more: the skip is a BoardButton on seat 2, and the enabled/disabled distinction is
        /// the one cap state a peer genuinely has to reproduce for it, because a dead skip is drawn
        /// as a lerp toward dark wood with a faded label rather than being hidden. Reading the flag
        /// the renderer itself obeys is the same argument <see cref="StateAccent"/> makes.</para>
        /// </summary>
        internal bool StateEnabled => _enabledState;

        /// <summary>This cap's live CONFIRMED (readied) state — see <see cref="StateAccent"/>.</summary>
        internal bool StateConfirmed => _confirmed;

        /// <summary>
        /// WHICH cap this is on the multiplayer press wire (<c>NetProtocol.CapPress*</c>), or
        /// <c>NetProtocol.CapPressNone</c> for a cap whose presses are not mirrored. Set by the
        /// builder that knows the cap's role (<see cref="BuildButtons"/>,
        /// <see cref="BuildDashboardControls"/>, <c>RestControls.EnsureBuilt</c>) rather than
        /// guessed from the name, and defaulted to "none" so a new cap has to opt IN.
        /// </summary>
        internal byte WireCap { get; set; } = Net.NetProtocol.CapPressNone;

        internal void SetState(bool enabled, bool accent, bool confirmed = false)
        {
            if (_enabledState == enabled && _accent == accent && _confirmed == confirmed)
                return;
            _enabledState = enabled;
            _accent = accent;
            _confirmed = confirmed;
            UpdateColor();
        }

        internal void SetLabel(string text)
        {
            if (_label == null)
                return;
            // Tofu fix (user report 2026-08-04, "Viereck vor 'Mach dich bereit'"): strip any
            // glyph this label's harvested font cannot render (e.g. the '✓ ' confirmed-state
            // prefix on a font without U+2713) instead of letting TMP draw a hollow box.
            // Same-instance fast path keeps the change gate below allocation-free; a null
            // font passes through and is re-judged on the next per-tick SetLabel.
            // The same seam strips TMP rich-text TAGS first (RichTextTags): the game's burn
            // wording arrives as '<sprite name="LOST"> Verbrennen …' and the cap printed the
            // tag as text (buttontext.jpg). The count is kept for the KEYCAP SURFACE line.
            text = WorldUI.NativeButtonSkin.SanitizeLabel(_label, text, out int tags);
            _labelTags = tags;
            if (_label.text == text)
                return;
            _label.text = text;
            // RE-FIT, EVERY TIME THE STRING CHANGES. This line is the fourth link of the ModBuild
            // 281 truncation: the caption was fitted ONCE, at build time, against whatever string
            // the cap was born with, and every later wording — which is most of them, since these
            // captions are live game text that changes with the dialog, the turn and the language —
            // inherited a size and a set of line breaks solved for a different string. A fit that is
            // not re-run is a fit for the wrong text. Cheap because it is gated on the change above.
            ApplyLabelLayout(_symbolApplied);
        }

        /// <summary>The label this cap currently DISPLAYS — read by the multiplayer cap-label
        /// seam (<c>PlayTray.ConfirmControlLabel</c>) so the wire carries the wording the owner
        /// actually sees, whichever TickStatus branch last wrote it. Null before a label exists.</summary>
        internal string? CurrentLabel => _label != null ? _label.text : null;

        /// <summary>
        /// Show/hide the whole button (test #23 item 4): the mod CONFIRM/UNDO buttons
        /// hide while the REAL ReadyButton/UndoButton dock at the same spot. Inactive
        /// = its collider is off too, so it produces no poke/laser events.
        /// </summary>
        internal void SetVisible(bool visible)
        {
            if (_logicalVisible == visible)
                return;
            _logicalVisible = visible;
            if (visible)
            {
                // Cancel a running dissolve, restore the true scale, re-enable input.
                _hideLeft = 0f;
                transform.localScale = _shownScale;
                if (Collider != null)
                    Collider.enabled = true;
                if (!gameObject.activeSelf)
                    gameObject.SetActive(true);
                // APPEAR = ASSEMBLE OUT OF DUST (user: emerge from dust, matched to the crumble —
                // NOT a scale/grow pop): converging dust motes settle onto the cap while the cap's
                // own surface cools out of that dust into its state colour, bevel ring first, IN
                // PLACE (no scaling). Runs once the cap is past its build-then-settle window (see
                // Settled) and while the animation is enabled ([ButtonAnim] Enable). Input/collider
                // are already live above.
                _showLeft = Settled && WorldUI.ButtonTuning.ButtonAnimEnabled ? WorldUI.ButtonTuning.AppearSeconds : 0f;
                // Watchdog armed on the UNSCALED wall clock (see the field header): whatever happens
                // to the frame clock or this object's ticking from here, the fade is over by then.
                _fadeStartedAt = Time.unscaledTime;
                _showDeadline = _showLeft > 0f ? _fadeStartedAt + _showLeft + FadeWatchdogSlack : float.PositiveInfinity;
                _hideDeadline = float.PositiveInfinity;
                // Aim the ramp at the cap's SETTLED colour, and paint frame ZERO of the assembly
                // right here. Waiting for the next Update would show one frame of the finished cap
                // (or, after a cancelled dissolve, one frame of a stale mid-ramp colour) before it
                // starts arriving — a pop in front of the anti-pop animation. UpdateColor does both
                // halves while an appear is running: it re-aims _assemblyRest (the native-face branch
                // is the only one whose settled colour is not simply StateColor()) and hands the
                // surface straight to ApplyAssembly. With no appear running it just re-seats the
                // exact state colours, which is what an instant (animation-off) show needs.
                _assemblyRest = StateColor();
                UpdateColor();
                // The REVEAL half of the keycap-surface diagnostic (see LogCapSurface): a cap can be
                // built perfectly and still arrive on screen with a missing slot, a stale shader, a
                // lost layer or a face sunk below its own well. Logged after UpdateColor so the
                // colours printed are the ones the player is about to look at. Throttled shared.
                LogCapSurface("REVEAL");
                if (_showLeft > 0f)
                {
                    WorldUI.ButtonTuning.LogAnim(name, "appear (assemble out of dust)");
                    if (WorldUI.ButtonTuning.AppearParticlesEnabled)
                    {
                        Vector3 c = _cap != null ? _cap.position : transform.position;
                        float fp = Collider is BoxCollider b ? Mathf.Max(b.size.x, b.size.y) : 0.05f;
                        WorldUI.ButtonDissolveFx.PlayMaterialize(c, -transform.forward,
                            fp * Mathf.Abs(transform.lossyScale.x), _assemblyRest);
                    }
                }
                return;
            }
            // LOGICAL hide is immediate (user #7 contract): input off now, visuals may linger.
            _hoverHand = null; // no poke events fire while hidden — drop stale follow
            _depthArmed = true;
            if (_dwellHand != null)
                CancelDwell();
            if (Collider != null)
                Collider.enabled = false;
            if (!Settled || !gameObject.activeInHierarchy || !WorldUI.ButtonTuning.ButtonAnimEnabled)
            {
                // Initial state settling (built then hidden inside the build window), already
                // invisible with the tray, or the animation is disabled — pop away silently, no dust.
                gameObject.SetActive(false);
                return;
            }
            // Dust dissolve (user #7): cap shrinks out over DissolveSeconds while the pooled
            // burst sweeps face-colored powder sideways; Update deactivates at the end.
            _shownScale = transform.localScale;
            _showLeft = 0f;
            // Re-seat the exact state colours before capturing them: an interrupted APPEAR would
            // otherwise hand the dissolve a mid-assembly colour to crumble from (and to tint its
            // dust burst with), so a fast show/hide pair would drift brighter each time.
            UpdateColor();
            _assemblyRest = CurrentCapColor();
            _hideLeft = WorldUI.ButtonTuning.DissolveSeconds;
            // Same wall-clock deadline for the shrink-out: a dissolve that stops advancing would
            // otherwise leave a part-shrunk cap parked on the board forever (it is the SAME early
            // -return in Update that also blocks the appear fade from ever completing).
            _fadeStartedAt = Time.unscaledTime;
            _hideDeadline = _fadeStartedAt + _hideLeft + FadeWatchdogSlack;
            _showDeadline = float.PositiveInfinity;
            WorldUI.ButtonTuning.LogAnim(name, "disappear (dust dissolve)");
            Vector3 center = _cap != null ? _cap.position : transform.position;
            float footprint = Collider is BoxCollider bc ? Mathf.Max(bc.size.x, bc.size.y) : 0.05f;
            WorldUI.ButtonDissolveFx.Play(center, -transform.forward,
                footprint * Mathf.Abs(transform.lossyScale.x), _assemblyRest);
        }

        /// <summary>The cap's face color right now (native face, tinted material, or the palette fallback).</summary>
        private Color CurrentCapColor() =>
            _capFace != null ? _capFace.color
            : _capMaterial != null ? _capMaterial.color
            : StateColor();

        /// <summary>Resting cap color for the current state (procedural fallback; dwell ramps AWAY
        /// from this). USER DEBUG OPTION: the shared state palette is multiplied by this button's
        /// per-category <see cref="_capTint"/> (default white = unchanged).
        ///
        /// <para>SEATED (2026-08-09 round 3 — "die buttons waren unsichtbar und nur der text darauf
        /// sichtbar", the third report of the same look): the tinted result is floored so the cap
        /// face can never render darker than the WELL this same widget paints behind it
        /// (<c>Tint(basePlate, ButtonTuning.CapWellColor)</c> in <see cref="Create"/>). Without it
        /// the DISABLED face at this user's 0.5 tint lands at (0.105, 0.080, 0.055) against a
        /// (0.15, 0.12, 0.08) well — darker than its own recess, i.e. a hole in the board under a
        /// fully lit label. See WorldUI.ButtonTuning.SeatedCapColor for why the well is the right
        /// reference and why this is not the AppearFadeFloor that round 2 deleted.</para></summary>
        private Color StateColor() =>
            WorldUI.ButtonTuning.SeatedCapColor(
                (!_enabledState ? DisabledColor : _confirmed ? ConfirmedColor : _accent ? _accentColor : IdleColor)
                * _capTint);

        /// <summary>Native-skin face state for the current button state (test #25 item 3).</summary>
        private WorldUI.NativeButtonSkin.FaceState FaceState() =>
            !_enabledState ? WorldUI.NativeButtonSkin.FaceState.Disabled
            : (_confirmed || _accent) ? WorldUI.NativeButtonSkin.FaceState.Accent
            : WorldUI.NativeButtonSkin.FaceState.Idle;

        private void UpdateColor()
        {
            if (_capFace != null)
            {
                WorldUI.NativeButtonSkin.Apply(_capFace, FaceState());
                // USER DEBUG OPTION: tint the native sprite face too (the beige/brass button art
                // the user reads white text on) — default white leaves the sampled sprite as-is.
                // SEATED like the procedural face (see StateColor): the sprite face is the same cap
                // body sitting in the same well, so the same floor keeps it out of the board.
                _capFace.color = WorldUI.ButtonTuning.SeatedCapColor(_capFace.color * _capTint);
                // A state change that lands DURING the assembly must re-aim it (see below) and then
                // hand the surface straight back to the ramp — writing the settled colour and waiting
                // for the next Update would flash the finished button inside its own arrival.
                if (_showLeft > 0f)
                {
                    _assemblyRest = _capFace.color;
                    ApplyAssembly(AssemblyProgress);
                }
                return;
            }
            if (_capMaterial == null)
                return;
            Color top = StateColor();
            // FADE RE-AIM (same 2026-08-09 report). TickStatus always calls SetVisible(true) BEFORE
            // SetState(), so _assemblyRest was captured from the colour the cap wore while it was
            // still HIDDEN — i.e. the PREVIOUS state's colour. The fade then spent its whole run
            // ramping toward the wrong (usually darker, disabled) colour and only snapped to the
            // right one at the very end. Re-aiming here makes a mid-fade state change ramp toward
            // what the cap is actually becoming, so the fade brightens toward the live colour
            // instead of dragging the stale one across the visible window.
            if (_showLeft > 0f)
            {
                _assemblyRest = top;
                ApplyAssembly(AssemblyProgress); // stay inside the ramp — see the face branch above
                return;
            }
            SetCapColor(top);
        }

        /// <summary>How far the running APPEAR has got (0 = pure dust, 1 = settled). Meaningless
        /// unless <c>_showLeft &gt; 0</c>; used to re-paint mid-ramp after a state change.</summary>
        private float AssemblyProgress =>
            1f - Mathf.Clamp01(_showLeft / WorldUI.ButtonTuning.AppearSeconds);

        /// <summary>
        /// Paint this cap at overall assembly progress <paramref name="k"/> (0 = pure dust, 1 =
        /// settled material), around its <see cref="_assemblyRest"/> colour. Shared by the APPEAR
        /// (k rising) and the DISSOLVE (k falling), which is what keeps the pair a matched
        /// crumble/assemble rather than two animations that merely happen to be opposites.
        ///
        /// <para>The three submeshes of a beveled keycap are staggered by
        /// <c>WorldUI.ButtonTuning.AssemblyPhase</c> — the BRIGHT bevel ring leads, the top plateau
        /// follows, the dark walls settle last — so the cap visibly builds itself edge-first instead
        /// of the whole silhouette changing brightness at once. Round pucks, native sprite faces and
        /// any single-material cap take the TOP phase alone.</para>
        /// </summary>
        private void ApplyAssembly(float k)
        {
            if (_capFace != null)
            {
                _capFace.color = WorldUI.ButtonTuning.AssemblyColor(_assemblyRest,
                    WorldUI.ButtonTuning.AssemblyPhase(k, WorldUI.ButtonTuning.CapPart.Top));
                return;
            }
            if (_capMaterial == null)
                return;
            Color top = _assemblyRest;
            _capMaterial.color = WorldUI.ButtonTuning.AssemblyColor(top,
                WorldUI.ButtonTuning.AssemblyPhase(k, WorldUI.ButtonTuning.CapPart.Top));
            if (_capBevelMaterial != null)
                _capBevelMaterial.color = WorldUI.ButtonTuning.AssemblyColor(BevelTint(top),
                    WorldUI.ButtonTuning.AssemblyPhase(k, WorldUI.ButtonTuning.CapPart.Bevel));
            if (_capWallMaterial != null)
                _capWallMaterial.color = WorldUI.ButtonTuning.AssemblyColor(WallTint(top),
                    WorldUI.ButtonTuning.AssemblyPhase(k, WorldUI.ButtonTuning.CapPart.Wall));
            // Round 15: keep the Standard-fallback emission floor tracking the animated tints
            // (per-frame during the short assembly only; no-op on BoardLit — no _EmissionColor).
            CardMesh.ApplyEmissionFloor(_capMaterial);
            CardMesh.ApplyEmissionFloor(_capBevelMaterial);
            CardMesh.ApplyEmissionFloor(_capWallMaterial);
        }

        /// <summary>
        /// Watchdog line for the 2026-08-09 "the buttons were invisible, only their text was left"
        /// report: a surface animation that did not finish inside its authored duration plus
        /// <see cref="FadeWatchdogSlack"/> was force-completed. This is the log the next hardware
        /// run needs — it names the cap, the animation and the WALL-CLOCK seconds the cap actually
        /// spent in the faded (near-black) state, so the window is visible in the log instead of
        /// depending on the player noticing it. Throttled: one relayout can force several caps.
        /// </summary>
        private void LogFadeForced(string anim, float authored)
        {
            float held = Time.unscaledTime - _fadeStartedAt;
            if (Time.unscaledTime < _nextFadeHealLogAt)
                return;
            _nextFadeHealLogAt = Time.unscaledTime + 0.5f;
            VRLog.Warn("Cards", $"KEYCAP FADE HEALED: '{name}' was still mid-'{anim}' after " +
                $"{held:F2} s of WALL-CLOCK time (authored {authored:F2} s " +
                $"+ {FadeWatchdogSlack:F2} s slack; Time.timeScale {Time.timeScale:F2}) — force-completed and the " +
                "exact state colours re-seated. Since the assembly ramp replaced the multiply-toward-black " +
                "fade, a stalled animation strands the cap BRIGHTER than its state colour (parchment-brass " +
                "dust), never invisible — so a line here is a clock/tick problem to investigate, and it is " +
                "no longer capable of producing the 'button invisible, only the text visible' report.");
        }

        /// <summary>
        /// BOUNDED CAP-MATERIAL HEAL — the second half of the 2026-08-09 report's differential.
        ///
        /// <para>The competing explanation for an invisible cap under a perfectly fine label is a cap
        /// whose MATERIAL resolved to nothing: <c>GloomhavenVR/BoardLit</c> lives in the mod's asset
        /// bundle, and <c>Shader.Find</c> cannot see a bundled shader until something has loaded it
        /// (the exact trap already documented on <c>PlayTray.OverlayShader</c>, and this session's log
        /// shows Overlay missing at line 78 and present at 461). A cap built inside that window took
        /// the Standard/Sprites fallback — and used to keep it FOREVER, because the caps are only
        /// re-skinned by a full rebuild, which happens when some unrelated thing (an item clipping
        /// into the use slot) happens to bump the tray. That is healing by luck.</para>
        ///
        /// <para>This re-probes on a <see cref="CapHealIntervalSeconds"/> cadence with a bounded
        /// budget (a bundle that genuinely lacks the shader must never become a per-tick probe storm
        /// of the mod's own making — the CardArtGuard rule), and re-skins the cap in place the moment
        /// BoardLit appears. Both the miss at build time and the heal are logged, so the next hardware
        /// log states which of the two mechanisms was in play instead of leaving it to inference.</para>
        /// </summary>
        private void TryHealCapMaterial()
        {
            // TWO THINGS CAN ARRIVE LATE OUT OF THE SAME BUNDLE, and until this round only one of
            // them was healed. The shader is the documented case. The per-board KEYCAP ATLAS is the
            // new one, and its failure is quieter: the cap renders perfectly, in the right colour,
            // with the right walls — it just has no symbol carved into it, for the rest of the
            // session, on a board that happened to be built a few frames before the bundle was
            // loadable. Both are repaired by exactly the same re-skin, so the gate is widened
            // rather than a second heal written beside it.
            bool wantsSymbol = _capStyle != null && _capRole != CapRole.Plain
                               && !_symbolApplied
                               && CapSymbols.TryAtlas(_capStyle.Value, out _, out _);
            if ((!_capShaderFallback && !wantsSymbol) || _capHealBudget <= 0
                || _capMaterial == null || _capFace != null)
                return;
            if (Time.unscaledTime < _nextCapHealAt)
                return;
            _nextCapHealAt = Time.unscaledTime + CapHealIntervalSeconds;
            _capHealBudget--;
            Shader? lit = BoardLitShader();
            if (lit == null)
            {
                if (_capHealBudget == 0)
                    VRLog.Warn("Cards", $"KEYCAP MATERIAL: '{name}' stays on its fallback shader — " +
                        "'GloomhavenVR/BoardLit' never turned up in any loaded bundle. The cap still " +
                        "renders (Standard/Sprites tint), but its walls/bevel do not shade.");
                return;
            }
            Color top = StateColor();
            // THE HEAL RE-SKINS WITH THE SAME ROLE IT WAS BUILT WITH. Re-skinning to CapRole.Plain
            // would silently strip the carved symbol off exactly the caps that were unlucky enough
            // to be built before the bundle was loadable — a defect that only ever appears on a
            // slow load and only on some of the caps, which is the hardest kind to be told about.
            // The BEZEL AND WALL cell is stored for the same reason (_plainCell): this method does
            // not know whether the cap is a disc, and the square band inside a round cap is the
            // very defect cell 9 was authored to remove.
            _capMaterial = NewKeycapMaterial(lit, top, _capRole, _capStyle);
            if (_capBevelMaterial != null && _capWallMaterial != null && _capMeshRenderer != null)
            {
                _capBevelMaterial = NewKeycapMaterial(lit, BevelTint(top), _plainCell, _capStyle);
                _capWallMaterial = NewKeycapMaterial(lit, WallTint(top), _plainCell, _capStyle);
                _capMeshRenderer.sharedMaterials = new[] { _capMaterial, _capBevelMaterial, _capWallMaterial };
            }
            else if (_capMeshRenderer != null)
            {
                _capMeshRenderer.sharedMaterial = _capMaterial;
            }
            bool healedShader = _capShaderFallback;
            _capShaderFallback = false;
            _symbolApplied = _capStyle != null && _capRole != CapRole.Plain
                             && CapSymbols.TryAtlas(_capStyle.Value, out _, out _);
            // The CAPTION has to follow the symbol. Re-skinning alone would leave a rest disc
            // wearing its newly carved crescent with the word still printed across it, which is
            // exactly the doubling the board engraving exists to remove.
            ApplyLabelLayout(_symbolApplied);
            if (!healedShader)
            {
                VRLog.Info("Cards", $"KEYCAP SYMBOL HEALED: '{name}' was built before its board's " +
                    "keycap atlas was loadable, so it wore the shared grain and no symbol; the atlas " +
                    $"has since turned up, and the cap has been re-skinned in place with the {_capRole} " +
                    "cell — no rebuild, no state change, and the dust dissolve never ran.");
                return;
            }
            VRLog.Info("Cards", $"KEYCAP MATERIAL HEALED: '{name}' was built before " +
                "'GloomhavenVR/BoardLit' was loadable and wore the flat Standard/Sprites fallback; the " +
                "shader has since resolved, so the cap has been re-skinned in place with the real " +
                "carved-grain keycap materials (top/bevel/wall) — no rebuild, no state change needed.");
        }

        /// <summary>
        /// Item 4: drive the cap TOP colour and — when the boxy cap carries the split bevel
        /// (<see cref="_capBevelMaterial"/>) and wall (<see cref="_capWallMaterial"/>)
        /// submeshes — the BRIGHT bevel ring and the dark warm WALL band together, so all three
        /// always track the button state (disabled / accent / confirmed / dwell charge): the
        /// bevel a fixed <see cref="BevelLerp"/> brighter, the wall a fixed value+hue darker.
        /// No-op bevel/wall steps on round/native caps (null instances).
        /// </summary>
        private void SetCapColor(Color top)
        {
            // Round 15: on the Standard-fallback cap (bundle without BoardLit) the emission floor
            // is derived from the albedo tint, so every change-gated tint write re-syncs it
            // (no-op on BoardLit, which has no _EmissionColor — see CardMesh.EmissionFloorFactor).
            if (_capMaterial != null && _capMaterial.color != top)
            {
                _capMaterial.color = top;
                CardMesh.ApplyEmissionFloor(_capMaterial);
            }
            if (_capBevelMaterial != null)
            {
                Color bevel = BevelTint(top);
                if (_capBevelMaterial.color != bevel)
                {
                    _capBevelMaterial.color = bevel;
                    CardMesh.ApplyEmissionFloor(_capBevelMaterial);
                }
            }
            if (_capWallMaterial != null)
            {
                Color wall = WallTint(top);
                if (_capWallMaterial.color != wall)
                {
                    _capWallMaterial.color = wall;
                    CardMesh.ApplyEmissionFloor(_capWallMaterial);
                }
            }
        }

        private void Update()
        {
            // Dust-dissolve shrink-out (user #7): the button is logically gone already
            // (collider off) — finish the visual shrink, then deactivate for real.
            if (_hideLeft > 0f)
            {
                // UNSCALED clock + wall-clock deadline (see the _showDeadline field header): the
                // shrink-out used Time.deltaTime, which stops advancing whenever the game stops
                // simulation time — the same stall that could park the appear fade at 15 % could
                // park this one at a fraction of the cap's size.
                _hideLeft -= Time.unscaledDeltaTime;
                if (_hideLeft > 0f && Time.unscaledTime >= _hideDeadline)
                {
                    LogFadeForced("dust dissolve", WorldUI.ButtonTuning.DissolveSeconds);
                    _hideLeft = 0f;
                }
                float k = Mathf.Max(0f, _hideLeft / WorldUI.ButtonTuning.DissolveSeconds);
                transform.localScale = _shownScale * k;
                // The surface runs the SAME assembly curve backwards: the walls go back to dust
                // first, the brass bevel frame is the last thing left. Purely additive brightness
                // (see ButtonTuning.AssemblyColor) — a crumbling cap never darkens toward the board,
                // which is what would make it vanish before it has finished crumbling, in MR most of
                // all (behind it is the player's real room, not a black board).
                ApplyAssembly(k);
                if (_hideLeft <= 0f)
                {
                    _hideDeadline = float.PositiveInfinity;
                    transform.localScale = _shownScale; // restore for the next show
                    UpdateColor();                      // leave the exact state colours behind for the next show
                    gameObject.SetActive(false);
                }
                return;
            }
            TryHealCapMaterial();

            // ASSEMBLE-OUT-OF-DUST appear (user: emerge from dust, NOT a scale pop) — runs alongside
            // the normal press logic. The cap stays at full scale IN PLACE while its OPAQUE surface
            // cools out of the warm parchment-brass dust into its true state colour, bevel ring
            // first, under the converging dust cloud. No transparency is needed or used (the
            // BoardLit/Standard caps are opaque, queue 2000). On completion UpdateColor() restores
            // the exact state colours.
            if (_showLeft > 0f)
            {
                // UNSCALED clock + wall-clock deadline. This countdown reaching zero is the ONLY
                // thing that ever restores the cap's true state colour (UpdateColor below), which is
                // exactly why a stalled clock or a skipped tick read to the player as a permanently
                // invisible button with its label still floating in place. The deadline guarantees
                // the restore happens whatever the frame clock does.
                _showLeft -= Time.unscaledDeltaTime;
                if (_showLeft > 0f && Time.unscaledTime >= _showDeadline)
                {
                    LogFadeForced("assemble-out-of-dust", WorldUI.ButtonTuning.AppearSeconds);
                    _showLeft = 0f;
                }
                float k = 1f - Mathf.Max(0f, _showLeft / WorldUI.ButtonTuning.AppearSeconds);
                transform.localScale = _shownScale; // materialize in place — no grow/scale pop
                ApplyAssembly(k);
                if (_showLeft <= 0f)
                {
                    _showDeadline = float.PositiveInfinity;
                    transform.localScale = _shownScale;
                    UpdateColor(); // snap back to the exact state colour (top/bevel/wall or native face)
                }
            }

            if (_dwellHand != null)
            {
                TickDwell();
                return;
            }
            if (_cap == null)
                return;

            // Advance the press STROKE (attack → detent → spring-back past rest → settle). The
            // shape is WorldUI.ButtonStroke.Depth01 and the peer's mirror of this cap calls
            // the same function off the same phase, so the two are one animation rather than two
            // that agree — see the block comment at that function for what it replaced and why the
            // clock is unscaled.
            if (_pressPhase >= 0f)
            {
                _pressPhase += Time.unscaledDeltaTime;
                if (_pressPhase >= WorldUI.ButtonStroke.StrokeSeconds)
                    _pressPhase = -1f;
            }

            // Finger-follow (feature 6a): while a fingertip hovers this button the cap
            // tracks how deep the tip has pushed past the face, so the puck sinks under
            // the finger 1:1 (up to the full travel) and rises as it retracts. When no
            // finger is present the press STROKE drives the cap on its own. The two combine
            // as a max so a quick laser/click still shows its dip even mid-hover.
            float follow = _hoverHand != null && _enabledState ? FollowDepth01(_hoverHand) : 0f;

            // DEPTH-FIRE (user #6): the press fires exactly when the cap reaches ~90% of
            // its full travel under the finger — not on contact. Re-arms only after the
            // finger retracted enough for the cap to rise back past ~50%, so resting at
            // the bottom cannot machine-gun and a shallow brush never fires at all.
            // Press() still runs the full gate chain (enabled/ActivationGuard/logs).
            if (_hoverHand != null && _enabledState)
            {
                if (follow >= WorldUI.ButtonTuning.PressFireFraction)
                {
                    // GRIP CHORD (hardware MP test 2026-08, requirement (a): "Das physische
                    // Drücken der Tasten … darf nur möglich sein, während der Grip-Knopf
                    // gehalten wird"): a fingertip press on a BOARD button commits only while
                    // the SAME hand's grip is held — the identical chord the fingertip-on-tile
                    // ping already requires, so poking anything on the board is one consistent
                    // gesture. The cap still follows the finger (the button visibly reacts),
                    // it just cannot FIRE from an accidental brush-through; laser presses
                    // arrive via Press(hand, "laser"/"click") and stay grip-free.
                    //
                    // EMPTY HAND added 2026-08 with the "alle buttons" round (the report that
                    // made the decision dock take this same chord, Hands.Interact.PokeInteractor
                    // .PressAllowed): the grip is ALSO what grabs (IGrabbable.GrabWithGrip), so a
                    // hand that is carrying something pressed the grip to CARRY, not to press.
                    // Without this half, dragging the control board itself — a grip grab, with
                    // that hand's fingertip inches from its own keycaps — fires every cap it
                    // sweeps, and walking a card over the board does the same. It is the exact
                    // second half BoardPick.TryNearPick has always carried for the hex ping; the
                    // two gates now read identically at every fingertip commit in the mod.
                    if (!_hoverHand.GripPressed || _hoverHand.Grabber.Held != null)
                    {
                        LogGripGate(_hoverHand);
                    }
                    // DEBOUNCE (user): fire only when re-armed (cap fully retracted past the
                    // hysteresis since the last press) AND the shared cooldown has elapsed —
                    // a retract-then-push or a hover flicker inside the same poke can no longer
                    // machine-gun a second press. Press() re-checks the cooldown and stamps it.
                    else if (_depthArmed && Time.unscaledTime >= _nextPressTime)
                    {
                        _depthArmed = false;
                        Press(_hoverHand, "poke-depth");
                    }
                }
                else if (follow <= WorldUI.ButtonTuning.PressRearmFraction)
                {
                    _depthArmed = true;
                }
            }
            else
            {
                _depthArmed = true;
            }

            // THE TWO SOURCES COMBINE AS A MAX ONLY WHILE A FINGER IS ON THE CAP, so a quick
            // laser click still shows its dip mid-hover — and the press stroke's REBOUND survives
            // when there is no finger. A plain Mathf.Max would clamp every negative phase of the
            // stroke to 0 against a follow of 0 and delete the overshoot silently, which is the
            // trap PressDepth01's own doc comment names.
            float impulse = _pressPhase >= 0f ? WorldUI.ButtonStroke.Depth01(_pressPhase) : 0f;
            float depth01 = follow > 0f ? Mathf.Max(follow, impulse) : impulse;

            // Nothing to drive and already seated → leave it (avoids per-frame churn).
            if (Mathf.Approximately(depth01, 0f) && Mathf.Approximately(_cap.localPosition.z, CapRestZ))
                return;

            Vector3 pos = _cap.localPosition;
            pos.z = CapRestZ + Travel * depth01;
            _cap.localPosition = pos;
        }

        /// <summary>
        /// Fingertip penetration for the finger-follow press, normalised to 0..1 of the
        /// cap travel. Same tip/collider probe as <see cref="TickDwell"/> and the
        /// PokeInteractor contact test: penetration = FingertipRadius·scale − distance
        /// (tip → nearest surface point). Converted through the button's WORLD depth
        /// scale so the puck follows the finger in real space (not local units), and
        /// clamped to one full travel. Framerate-independent — it is a pure function of
        /// where the fingertip is this frame, no accumulation.
        /// </summary>
        private float FollowDepth01(VRHand hand)
        {
            if (Collider == null || !hand.HasPose)
                return 0f;
            Vector3 tip = hand.Rig.IndexTip.position;
            float dist = Vector3.Distance(tip, Collider.ClosestPoint(tip));
            float penetration = FingertipRadius * hand.WorldScale - dist;
            if (penetration <= 0f)
                return 0f;
            float capTravelWorld = Travel * Mathf.Abs(transform.lossyScale.z);
            return capTravelWorld > 1e-6f ? Mathf.Clamp01(penetration / capTravelWorld) : 0f;
        }

        /// <summary>
        /// Poke path (P2 PokeInteractor — geometric fingertip test against this
        /// collider). DEPTH-FIRE (user #6): fingertip CONTACT no longer presses —
        /// the finger-follow machinery in <see cref="Update"/> fires when the cap
        /// reaches ~90% of its travel, so a brush against the button does nothing
        /// and the press feels like actually pushing the key in. The disabled case
        /// still routes to <see cref="Press"/> so every rejected attempt keeps its
        /// gate log (test #14); dwell buttons (none currently) keep their charge.
        /// Laser presses arrive via <see cref="Press"/> directly and stay immediate.
        /// </summary>
        public override void OnPoke(VRHand hand)
        {
            if (DwellSeconds > 0f && _enabledState)
            {
                // GRIP CHORD (requirement (a), same gate as the depth-fire in Update): a dwell
                // is still a PHYSICAL fingertip press, so it too only starts under a held grip
                // and an empty hand. Belt-and-braces since the "alle buttons" round: OnPoke is
                // now only DELIVERED to a fingertip press that already passed the identical gate
                // in Hands.Interact.PokeInteractor.PressAllowed (laser presses reach a board
                // button through Press(), not here). Kept anyway, for the reason BoardClickDriver
                // .TickNear keeps its re-check: it makes "a dwell cannot start without the chord"
                // true by construction instead of by chain of reasoning.
                if (!hand.GripPressed || hand.Grabber.Held != null)
                {
                    LogGripGate(hand);
                    return;
                }
                BeginDwell(hand);
                return;
            }
            if (!_enabledState)
            {
                Press(hand, "poke"); // keeps the REJECTED + reason log
                return;
            }
            _hoverHand = hand; // safety: ensure the depth-fire tracker is armed on this hand
        }

        public override void OnPokeExit(VRHand hand)
        {
            if (ReferenceEquals(hand, _dwellHand))
                CancelDwell();
            if (ReferenceEquals(hand, _hoverHand))
                _hoverHand = null; // stop finger-follow; the cap springs back via Update
        }

        private void BeginDwell(VRHand hand)
        {
            if (_dwellHand != null)
                return; // already charging (second hand / re-arm jitter)
            _dwellHand = hand;
            _dwellStart = Time.unscaledTime;
            _dwellTicks = 0;
            VRLog.Debug("Cards", $"Board: {name} poke dwell started ({hand.Side}) — " +
                                 $"hold {DwellSeconds:F2} s to press.");
        }

        /// <summary>
        /// Per-frame dwell charge: self-tracks the fingertip against the button's
        /// own collider (same math as PokeInteractor, which has no per-frame stay
        /// callback — its OnPokeExit only fires when the tip leaves the 3.5 cm
        /// hover range, too late for a press-intent test). The cap sinks its full
        /// travel and brightens toward <see cref="DwellChargeColor"/> as the fill
        /// ramp; a rising haptic tick marks each quarter of the charge.
        /// </summary>
        private void TickDwell()
        {
            VRHand hand = _dwellHand!;
            bool holding = _enabledState && Collider != null && hand.HasPose;
            if (holding)
            {
                Vector3 tip = hand.Rig.IndexTip.position;
                holding = Vector3.Distance(tip, Collider!.ClosestPoint(tip))
                          <= DwellHoldRange * hand.WorldScale;
            }
            if (!holding)
            {
                CancelDwell();
                return;
            }

            float progress = Mathf.Clamp01((Time.unscaledTime - _dwellStart) / DwellSeconds);
            if (_cap != null)
            {
                Vector3 pos = _cap.localPosition;
                pos.z = CapRestZ + Travel * progress;
                _cap.localPosition = pos;
            }
            if (_capFace != null)
                // USER DEBUG OPTION: tint the native face (default white = unchanged); StateColor is
                // already tinted, so the procedural branch below needs no extra multiply.
                _capFace.color = Color.Lerp(
                    WorldUI.ButtonTuning.SeatedCapColor(WorldUI.NativeButtonSkin.ColorFor(FaceState()) * _capTint),
                    DwellChargeColor, progress);
            else if (_capMaterial != null)
                SetCapColor(Color.Lerp(StateColor(), DwellChargeColor, progress));

            int tick = (int)(progress * 4f);
            if (tick > _dwellTicks)
            {
                _dwellTicks = tick;
                hand.SendHaptic(HapticPreset.HoverTick);
            }

            if (progress >= 1f)
            {
                _dwellHand = null;
                UpdateColor(); // drop the charge tint (Press animates the spring-back)
                Press(hand, "poke-dwell");
            }
        }

        private void CancelDwell()
        {
            if (_dwellHand == null)
                return;
            float held = Time.unscaledTime - _dwellStart;
            _dwellHand = null;
            _pressPhase = -1f;
            if (_cap != null)
            {
                Vector3 pos = _cap.localPosition;
                pos.z = CapRestZ;
                _cap.localPosition = pos;
            }
            UpdateColor(); // drop the charge tint
            VRLog.Info("Cards", $"Board: {name} poke dwell cancelled after {held:F2} s " +
                                $"(needs {DwellSeconds:F2} s — brushing the button no longer presses it).");
        }

        /// <summary>
        /// Single press entry for BOTH input paths (test #14): every attempt is
        /// logged with its source and, when rejected, the exact gate state — a
        /// silent dead button can no longer happen.
        /// </summary>
        internal void Press(VRHand hand, string source)
        {
            if (!_enabledState)
            {
                VRLog.Info("Cards", $"Board: {name} press REJECTED (source={source}, {hand.Side}) — " +
                                    $"disabled: {(DisabledReason != null ? DisabledReason() : "no reason hook")}.");
                return;
            }
            float guard = ActivationGuard != null ? ActivationGuard() : 0f;
            if (guard > 0f)
            {
                VRLog.Info("Cards", $"Board: {name} press SUPPRESSED (source={source}, {hand.Side}) — " +
                                    $"{guard:F2} s left of the slot-activity window " +
                                    "(accidental press right after handling cards, test #19).");
                return;
            }
            // DEBOUNCE (user): one press per physical poke/click. The depth-fire pre-checks this
            // window (so a valid poke-depth always passes here and re-stamps it); the laser path
            // arrives straight here and is cooldown-debounced too, so a retract/re-entry, a
            // hover flicker, or a poke+laser inside the same window cannot fire twice.
            if (Time.unscaledTime < _nextPressTime)
            {
                VRLog.Debug("Cards", $"Board: {name} press DEBOUNCED (source={source}, {hand.Side}) — " +
                                     $"within the {WorldUI.ButtonTuning.PokePressCooldownSeconds:F2}s press cooldown.");
                return;
            }
            _nextPressTime = Time.unscaledTime + WorldUI.ButtonTuning.PokePressCooldownSeconds;
            _pressPhase = 0f; // arm the stroke; Update rides ButtonStroke.Depth01 from here
            hand.SendHaptic(HapticPreset.ClickPulse);
            // MULTIPLAYER (1:1 ruling — "alle Interaktionen, ANIMATIONEN und Anzeigen des
            // Controllboards"): publish the press EDGE for the extras sampler, so the mirrored cap
            // on every peer's copy of this board dips and springs back with this one. Reported from
            // the single commit point both input paths share, AFTER every gate, so a rejected /
            // suppressed / debounced attempt never animates anywhere.
            BoardCapPress.Report(WireCap);
            VRLog.Info("Cards", $"Board: {name} pressed (source={source}, {hand.Side}).");
            _onClick?.Invoke();
        }

        /// <summary>Throttle clock for the grip-gate refusal line (once per second per button —
        /// the finger can sit at full travel for many frames while the gate holds it).</summary>
        private float _nextGripGateLogAt;

        /// <summary>Requirement (a) diagnostic: the fingertip reached the fire depth but the same
        /// hand's GRIP is not held, so the press is withheld. One throttled Info line per attempt
        /// burst — the next hardware log then explains a "button did not react" report itself.</summary>
        private void LogGripGate(VRHand hand)
        {
            if (Time.unscaledTime < _nextGripGateLogAt)
                return;
            _nextGripGateLogAt = Time.unscaledTime + 1f;
            VRLog.Info("Cards", $"Board: {name} poke WITHHELD ({hand.Side}) — physical board-button " +
                                "presses require the same hand's GRIP held AND that hand to be empty " +
                                $"(grip {(hand.GripPressed ? "held" : "open")}, hand " +
                                $"{(hand.Grabber.Held != null ? "carrying something" : "empty")}; " +
                                "accidental-press guard, same chord as the fingertip tile ping and " +
                                "the poked uGUI buttons); laser clicks are unaffected.");
        }

        public override void OnPokeEnter(VRHand hand)
        {
            _hoverHand = hand; // arm the finger-follow press (feature 6a)
            if (_enabledState)
                hand.SendHaptic(HapticPreset.HoverTick);
        }
    }
}
