using System.Collections.Generic;
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
            // Cull-Back material). A closed beveled keycap = 10 quads: top(1) + bevel ring(4) +
            // side walls(4) + BACK/bottom cap(1). The back cap lives in the WALL submesh (2) so it
            // shades dark like the walls. Expected watertight counts: 40 verts, 20 tris, submesh
            // index counts [6, 24, 30] (top 6 / bevel 24 / walls+back 30). Report the ACTUAL mesh
            // so the next hardware log confirms nothing is missing and the winding is now outward.
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
            bool closedSolid = vtx == 40 && triTotal == 20 && s0 == 6 && s1 == 24 && s2 == 30;
            float bevelMm = SquareCapBevel * Mathf.Abs(lossy.z) * 1000f;
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
                $"top/bevel/wall {s0}/{s1}/{s2}; back+bottom cap in wall submesh 2 — expect 40/20/6/24/30). " +
                "Winding is now OUTWARD (RH normal = +n), so no interior/underside shows through under Cull Back (item 7). " +
                "Antique palette (T4): dark-wood plaque / parchment-glow available / aged-brass bevel inlay / dark-wood walls.");
        }

        private TextMeshPro? _label;
        private Transform? _cap;
        private Color _accentColor;
        /// <summary>USER DEBUG OPTION: [ButtonColors] per-category cap-FACE tint (multiplier, default
        /// white = unchanged) — set once at <see cref="Create"/> from the button's category. Applied
        /// to every state colour AND the native sprite face so the user can darken this cap group.</summary>
        private Color _capTint = Color.white;
        private bool _enabledState;
        private bool _accent;
        private bool _confirmed;
        private float _press; // 0..1 press animation

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
        /// T4 antique restyle: IDLE/AVAILABLE cap TOP — a warm PARCHMENT glow over the
        /// wood grain. The solid, opaque base every enabled key rests at ("this one you
        /// can press"); state accents tint from here. Desaturated toward antique — no
        /// candy saturation on the board.
        /// </summary>
        private static readonly Color IdleColor = new(0.60f, 0.51f, 0.35f);

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

        private bool _ticked;      // false until the first Update — a hide before then is silent (initial state settling)
        private float _hideLeft;   // dissolve shrink countdown, seconds
        private float _showLeft;   // materialize-from-dust fade-in countdown, seconds
        private Color _appearTarget = Color.white; // the cap colour the materialize fade ramps UP to
        private Vector3 _shownScale = Vector3.one;

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
        /// </summary>
        internal static BoardButton Create(Transform anchor, Vector2 size, Color accent,
            string fallbackLabel, System.Action onClick,
            bool round = false, float diameter = 0f, float thickness = 0.01f,
            bool overlay = false, bool boxy = false, float travel = CapTravel,
            WorldUI.ButtonTuning.CapCategory capCategory = WorldUI.ButtonTuning.CapCategory.Rest)
        {
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
            GameObject basePlate;
            if (round)
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
            Tint(basePlate, new Color(0.15f, 0.12f, 0.08f), overlay: overlay);

            // Native look (test #25 item 3): when a live game button has been sampled,
            // the travelling cap is an EMPTY holder carrying the game's own 9-sliced
            // button sprite (WorldUI.NativeButtonSkin) on a unit-scale face — so it is
            // indistinguishable from a native widget. Otherwise it falls back to the
            // procedural grey cube cap. Either way the holder sits at CapRestZ and
            // travels on press; the collider (on go) is independent of it.
            Material? capMaterial = null;
            Material? capBevelMaterial = null; // item 4: the bright bevel-ring material instance (boxy caps only)
            Material? capWallMaterial = null;  // item 4: the dark warm side-wall material instance (boxy caps only)
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
                capDisc.AddComponent<MeshFilter>().sharedMesh = CardMesh.GetRoundCap(size.x, capThickR);
                capDisc.AddComponent<MeshRenderer>();
                capFrontZ = CapRestZ - capThickR * 0.5f; // disc protrudes half its thickness toward the viewer
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
                if (shader != null)
                {
                    capMaterial = NewKeycapMaterial(shader, DisabledColor);
                    capDisc.GetComponent<MeshRenderer>().sharedMaterial = capMaterial;
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
                        capDisc.GetComponent<MeshRenderer>().sharedMaterial = capMaterial;
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
                Shader? shader = BoxCapShader();
                if (shader != null)
                {
                    var mf = capCube.GetComponent<MeshFilter>();
                    if (mf != null)
                        mf.sharedMesh = CardMesh.BuildBeveledKeycap(size.x, size.y, capThick, SquareCapBevel);
                    // Task #5a: each submesh material carries the shared carved-grain texture on
                    // _MainTex (grayscale grain × the state/bevel/wall tint) when the bundle ships
                    // it — a real wood/parchment surface — else EXACTLY the prior plain tint.
                    capMaterial = NewKeycapMaterial(shader, DisabledColor);                 // [0] top
                    capBevelMaterial = NewKeycapMaterial(shader, BevelTint(DisabledColor)); // [1] bright bevel
                    capWallMaterial = NewKeycapMaterial(shader, WallTint(DisabledColor));   // [2] dark warm wall
                    capCube.GetComponent<MeshRenderer>().sharedMaterials =
                        new[] { capMaterial, capBevelMaterial, capWallMaterial };
                }
                capMeshRenderer = capCube.GetComponent<MeshRenderer>();
                labelZ = -(capThick + 0.002f); // proud of the protruding plateau (front face sits at -capThick)
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
            labelGo.transform.localPosition = new Vector3(0f, 0f, labelZ); // proud of the cap face (viewer side, -Z)
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
            Core.TmpFit.Fit(tmp, size.x * 0.92f, size.y * 0.85f, maxFontSize: 0.40f);

            // Item 3 (laser fix): the trigger collider must SPAN the full protruding cap so a
            // laser ray aimed at the visible cap FACE registers a hit. The old fixed box
            // (size.z 0.02, centre -0.004 → front face -0.014) fell 20 mm SHORT of a boxy
            // beveled keycap's front plateau (CapRestZ - capThick ≈ -0.034): the laser passed
            // over the box and never landed, so gear/pin (and any Square-shaped cap) could not
            // be laser-pressed even though poke worked (the fingertip reaches the box from its
            // 35 mm hover). Size the box from the cap's own frontmost local-Z back to the base
            // plate's rear face, so it hugs the whole visible cap for every shape.
            const float baseBackZ = 0.007f;                     // base plate rear face (localPos.z 0.004 + half depth 0.003)
            float boxFrontZ = Mathf.Min(capFrontZ, -0.006f);    // never shallower than the old front
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(size.x, size.y, baseBackZ - boxFrontZ);
            box.center = new Vector3(0f, 0f, (boxFrontZ + baseBackZ) * 0.5f);
            box.isTrigger = true;

            var button = go.AddComponent<BoardButton>();
            button._onClick = onClick;
            button._capMaterial = capMaterial;
            button._capBevelMaterial = capBevelMaterial; // item 4: bright bevel-ring instance (null on non-boxy caps)
            button._capWallMaterial = capWallMaterial;   // item 4: dark warm side-wall instance (null on non-boxy caps)
            button._capFace = capFace;
            button._capMeshRenderer = capMeshRenderer;
            button._label = tmp;
            button._cap = cap.transform;
            button._accentColor = accent;
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
            return button;
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

        /// <summary>This cap's live ENABLED state — the multiplayer cap-STATE read seam (board-UI
        /// record byte 2). Reading the flag the renderer itself obeys is what makes the mirrored
        /// cap's colour the owner's colour by construction, instead of a second derivation of the
        /// game rules that can drift from it.</summary>
        internal bool StateEnabled => _enabledState;

        /// <summary>This cap's live ACCENT state — see <see cref="StateEnabled"/>.</summary>
        internal bool StateAccent => _accent;

        /// <summary>This cap's live CONFIRMED (readied) state — see <see cref="StateEnabled"/>.</summary>
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
            text = WorldUI.NativeButtonSkin.SanitizeLabel(_label, text);
            if (_label.text != text)
                _label.text = text;
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
                // APPEAR = MATERIALIZE FROM DUST (user: emerge from dust, matched to the crumble —
                // NOT a scale/grow pop): converging dust motes settle onto the cap while its surface
                // fades up from the dust to its full state colour, IN PLACE (no scaling). Only once
                // the button has ticked (suppresses the build-then-settle storm) and while the
                // animation is enabled ([ButtonAnim] Enable). Input/collider are already live above.
                _showLeft = _ticked && WorldUI.ButtonTuning.ButtonAnimEnabled ? WorldUI.ButtonTuning.AppearSeconds : 0f;
                if (_showLeft > 0f)
                {
                    _appearTarget = CurrentCapColor(); // the colour the surface fades UP to
                    WorldUI.ButtonTuning.LogAnim(name, "appear (materialize-from-dust)");
                    if (WorldUI.ButtonTuning.AppearParticlesEnabled)
                    {
                        Vector3 c = _cap != null ? _cap.position : transform.position;
                        float fp = Collider is BoxCollider b ? Mathf.Max(b.size.x, b.size.y) : 0.05f;
                        WorldUI.ButtonDissolveFx.PlayMaterialize(c, -transform.forward,
                            fp * Mathf.Abs(transform.lossyScale.x), _appearTarget);
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
            if (!_ticked || !gameObject.activeInHierarchy || !WorldUI.ButtonTuning.ButtonAnimEnabled)
            {
                // Initial state settling (built then hidden the same frame), already invisible
                // with the tray, or the animation is disabled — pop away silently, no dust.
                gameObject.SetActive(false);
                return;
            }
            // Dust dissolve (user #7): cap shrinks out over DissolveSeconds while the pooled
            // burst sweeps face-colored powder sideways; Update deactivates at the end.
            _shownScale = transform.localScale;
            _showLeft = 0f;
            _hideLeft = WorldUI.ButtonTuning.DissolveSeconds;
            WorldUI.ButtonTuning.LogAnim(name, "disappear (dust dissolve)");
            Vector3 center = _cap != null ? _cap.position : transform.position;
            float footprint = Collider is BoxCollider bc ? Mathf.Max(bc.size.x, bc.size.y) : 0.05f;
            WorldUI.ButtonDissolveFx.Play(center, -transform.forward,
                footprint * Mathf.Abs(transform.lossyScale.x), CurrentCapColor());
        }

        /// <summary>The cap's face color right now (native face, tinted material, or the palette fallback).</summary>
        private Color CurrentCapColor() =>
            _capFace != null ? _capFace.color
            : _capMaterial != null ? _capMaterial.color
            : StateColor();

        /// <summary>Resting cap color for the current state (procedural fallback; dwell ramps AWAY
        /// from this). USER DEBUG OPTION: the shared state palette is multiplied by this button's
        /// per-category <see cref="_capTint"/> (default white = unchanged).</summary>
        private Color StateColor() =>
            (!_enabledState ? DisabledColor : _confirmed ? ConfirmedColor : _accent ? _accentColor : IdleColor)
            * _capTint;

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
                _capFace.color *= _capTint;
                return;
            }
            if (_capMaterial == null)
                return;
            SetCapColor(StateColor());
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
            if (_capMaterial != null && _capMaterial.color != top)
                _capMaterial.color = top;
            if (_capBevelMaterial != null)
            {
                Color bevel = BevelTint(top);
                if (_capBevelMaterial.color != bevel)
                    _capBevelMaterial.color = bevel;
            }
            if (_capWallMaterial != null)
            {
                Color wall = WallTint(top);
                if (_capWallMaterial.color != wall)
                    _capWallMaterial.color = wall;
            }
        }

        private void Update()
        {
            // Dust-dissolve shrink-out (user #7): the button is logically gone already
            // (collider off) — finish the visual shrink, then deactivate for real.
            if (_hideLeft > 0f)
            {
                _hideLeft -= Time.deltaTime;
                float k = Mathf.Max(0f, _hideLeft / WorldUI.ButtonTuning.DissolveSeconds);
                transform.localScale = _shownScale * k;
                if (_hideLeft <= 0f)
                {
                    transform.localScale = _shownScale; // restore for the next show
                    gameObject.SetActive(false);
                }
                return;
            }
            _ticked = true;

            // MATERIALIZE-FROM-DUST fade-in (user: emerge from dust, NOT a scale pop) — runs
            // alongside the normal press logic. The cap stays at full scale IN PLACE while its
            // OPAQUE surface brightens from the dust (0.15×) up to its true state colour (a real
            // fade with no transparency needed — safe on the BoardLit/Standard caps), under the
            // converging dust cloud. On completion UpdateColor() restores the exact state colours.
            if (_showLeft > 0f)
            {
                _showLeft -= Time.deltaTime;
                float k = 1f - Mathf.Max(0f, _showLeft / WorldUI.ButtonTuning.AppearSeconds);
                transform.localScale = _shownScale; // materialize in place — no grow/scale pop
                float b = Mathf.SmoothStep(0.15f, 1f, k);
                Color faded = _appearTarget * b; faded.a = _appearTarget.a;
                if (_capFace != null)
                    _capFace.color = faded;
                else
                    SetCapColor(faded);
                if (_showLeft <= 0f)
                {
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

            // Spring the click impulse back down (framerate-independent decay).
            if (_press > 0f)
                _press = Mathf.MoveTowards(_press, 0f, Time.deltaTime * 6f);

            // Finger-follow (feature 6a): while a fingertip hovers this button the cap
            // tracks how deep the tip has pushed past the face, so the puck sinks under
            // the finger 1:1 (up to the full travel) and rises as it retracts. When no
            // finger is present it falls back to the _press spring. The two combine as a
            // max so a quick laser/click still shows its dip even mid-hover.
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

            float depth01 = Mathf.Max(follow, _press);

            // Nothing to drive and already seated → leave it (avoids per-frame churn).
            if (depth01 <= 0f && Mathf.Approximately(_cap.localPosition.z, CapRestZ))
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
                _capFace.color = Color.Lerp(WorldUI.NativeButtonSkin.ColorFor(FaceState()) * _capTint, DwellChargeColor, progress);
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
            _press = 0f;
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
            _press = 1f;
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
