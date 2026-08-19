using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// WALL-MOUNTED DRESSING (user report 2026-08-02, schwebende_items.png): "Wenn Wände
/// ausgeblendet werden, bleiben trotzdem noch die Elemente daran zurück wie die Flammen der
/// Fackeln oder Kerzen. Das schwebt dann in der Luft. Ich will dass alles ausgeblendet wird
/// was auch in der Wand hängt und sonst frei in der Luft schweben würde."
///
/// WHY THE EXISTING ATTACHMENT TYPES DID NOT COVER IT — both are structural, this dressing is
/// not:
/// <list type="bullet">
/// <item>FOLIAGE rides its wall via a SHADER family test (Amp_Basic_Foliage…). A torch sconce,
///   a candle rack or a flame billboard runs an ordinary opaque/particle shader, so no foliage
///   verdict ever matches them.</item>
/// <item>ASSET SIBLINGS ride their wall via the HIERARCHY (nearest mixed fade/non-fade ancestor,
///   <see cref="FadeDriver.FindAssetRoot"/>) and are collected for ADOPTED groups only — cache
///   walls are explicitly excluded there. The hardware log of this report shows the faded wall
///   as a CACHE wall ('Wall 3', 41 renderers, +0 foliage +0 asset-sibling), i.e. the sibling
///   pass never even looked at it, and Apparance hangs the dressing props in a different
///   subtree anyway.</item>
/// </list>
///
/// THE RULE IS THE USER'S OWN PHRASING, taken literally and geometrically: a prop rides a wall's
/// fade when it is AIRBORNE — at least <see cref="FadeDriver.MountedClearanceWU"/> above that
/// room's tile-anchored floor plane, so it cannot be resting on the floor and WOULD float once
/// the wall goes — and HUGS that wall (horizontal gap ≤ <see cref="FadeDriver.MountedLinkMaxXZ"/>,
/// inside the wall's own vertical span plus a small cap overhang). Floor-standing braziers,
/// chests, tokens, obstacles and figures fail the first test by construction; a chandelier in the
/// middle of the room fails the second. Nearest wall wins, one owner per renderer.
///
/// WHICH GEOMETRY THE TEST READS (round 2 — the "candles blink" bug): for a MESH the renderer's
/// AABB is the right handle, but for a PARTICLE SYSTEM it is not: its bounds enclose the LIVE
/// particles and therefore drift every frame. The first hardware log caught it red-handed — the
/// same 'p_Moths_Torch_Wall' reads y[2.3..2.3] on one rescan and y[0.7..1.8] on the next, so it
/// was attached, hidden, released (bounds now "floor-supported"), restored, re-attached… which
/// is exactly the reported "Kerzen verschwinden kurz, tauchen wieder auf". Particle props are
/// therefore judged by their EMITTER (transform position, which is where the torch is bolted to
/// the wall and does not move), and the dressing-size cap — meaningless for a smoke plume, it
/// was rejecting the torches' own 'distort' heat haze at y[1.2..4.7] — applies to meshes only.
///
/// OWNERSHIP IS STICKY WHILE THE WALL IS FADED (same round): a prop attached to a segment that is
/// mid-fade or held faded is never re-evaluated and never released. Even if some future geometry
/// test flickers, a prop cannot come back while its wall is gone — the release happens only once
/// the wall is solid again.
///
/// LIGHTS ARE NEVER TOUCHED (user, same message: "Die Lichter selber sollen nie ausgeblendet
/// werden — also an den Lichtverhältnissen darf sich durch das Ausblenden nie etwas ändern").
/// Nothing here writes to a <see cref="Light"/>, and no GameObject is ever deactivated (which
/// WOULD take the light with it). The mutations are: the renderer's own MaterialPropertyBlock
/// (alpha / cutoff ramp), a particle system's start colour, start size and emission RATE, and
/// finally <c>Renderer.enabled</c>. Range, colour, intensity, shadows and cookies of every Light
/// stay exactly as authored; halos, lens flares and light probes are untouched. Every one of
/// those writes is snapshotted at attach time and restored bit-for-bit on unfade.
///
/// DISSOLVE, SYNCHRONOUS WITH THE WALL (round 2 — the "es ploppt" report): the props used to
/// switch off at the END of the wall's dissolve, so the flame outlived the wall and then popped.
/// They now ride the SAME <c>seg.Fade</c> 0→1 the wall's own cutoff sweep runs on, through
/// whichever channel the prop's material actually offers (the tier is decided once at attach and
/// LOGGED, so a remaining pop is decidable from the log):
/// <list type="bullet">
/// <item>ALPHA — the material exposes <c>_TintColor</c> / <c>_Color</c> / <c>_BaseColor</c>:
///   per-renderer MPB ramps the authored colour's alpha to 0. Works on live particles
///   immediately.</item>
/// <item>CUTOFF — no colour property but a <c>_Cutoff</c> ("Mask Clip Value"): the foliage
///   dissolve, an alpha-cutoff ramp that eats the cutout texels away.</item>
/// <item>PARTICLES — additionally and independently of the material: start-colour alpha, start
///   size and emission rate all ramp to zero, so the fire visibly dies down instead of being
///   switched off. This channel needs no shader support at all.</item>
/// </list>
/// The renderer is still disabled at the very end (<see cref="FadeDriver.FoliageHideFade"/>) as
/// the guarantee that nothing survives — by then it is transparent, unlit and emitting nothing.
///
/// MULTIPLAYER: local rendering only (property blocks, particle modules and renderer.enabled on
/// locally-owned scenery), nothing on the wire, peers unaffected — same contract as every other
/// WallSegmentFade attachment.
/// </summary>
internal static partial class WallSegmentFade
{
    private sealed partial class Segment
    {
        /// <summary>WALL-MOUNTED props (torch flames, candles, sconces, wall coins — see the file
        /// header): airborne props hugging this wall, dissolved and hidden with it. Never contains
        /// a Light (Lights are not Renderers and are never written to).</summary>
        public readonly List<MountedProp> Mounted = new();
        public readonly List<MountedProp> PrevMounted = new();
        /// <summary>0 = restored/untouched, 1 = dissolving, 2 = hidden.</summary>
        public int MountedState;
    }

    /// <summary>
    /// One wall-mounted prop plus everything needed to dissolve it and to put it back EXACTLY as
    /// authored. Built once when the prop is attached (see <see cref="FadeDriver.ClassifyProp"/>).
    /// </summary>
    private sealed class MountedProp
    {
        public Renderer Renderer = null!;
        /// <summary>Non-null for particle props — the system driving this renderer.</summary>
        public ParticleSystem? System;
        /// <summary>Colour property the alpha ramp writes, or -1.</summary>
        public int ColorId = -1;
        /// <summary>Authored value of <see cref="ColorId"/> (RGB preserved, alpha ramped).</summary>
        public Color BaseColor = Color.white;
        /// <summary>Cutoff property the dissolve ramp writes when there is no colour, or -1.</summary>
        public int CutoffId = -1;
        /// <summary>Authored "Mask Clip Value" the cutoff ramp starts from.</summary>
        public float BaseCutoff = 0.35f;
        /// <summary>Amp dissolve pair (round 7, defect a — the figure shaders expose
        /// <c>_Toggle_Dissolve</c>/<c>_InvisibilityControl</c>, family siblings may too):
        /// when BOTH exist, the ramp drives a real dissolve (toggle=1, control authored→1;
        /// assumption: 1 = invisible, per the property's figure-invisibility semantics — if
        /// a shader inverts it, the ramp is a no-op and the guaranteed end-of-ramp disable
        /// still lands, no worse than the plain pop). -1 when absent.</summary>
        public int DissolveToggleId = -1;
        public int DissolveControlId = -1;
        /// <summary>Authored <c>_InvisibilityControl</c> the dissolve ramp starts from.</summary>
        public float BaseDissolveControl;
        // Particle-module snapshot (restored bit-for-bit on unfade).
        public ParticleSystem.MinMaxGradient StartColor;
        public bool HasStartColor;
        public float StartSize;
        public float EmissionRate;
        /// <summary>Which channel(s) this prop dissolves through — for the census line.</summary>
        public string Tier = "none";
        // ---- round-11/15 dissolve channel (user: "ALLE assets die faden sollen das immer mit
        // der Animation tun") — see WallSegmentFade.Dissolve.cs. A piece whose materials ALL
        // carry a live wall-fade toggle is driven by the wall renderers' own map/_Cutoff ramp
        // (NativeFade, nothing swapped); a piece with channel-less slots gets COPIES on the
        // game's masonry fade shader for those slots and is driven by the same ramp.
        /// <summary>Drive this piece through the wall's NATIVE map/_Cutoff MPB ramp (round 15).
        /// True for toggle-native materials AND for swapped copies.</summary>
        public bool NativeFade;
        /// <summary>The renderer's authored sharedMaterials array (restored on unfade).</summary>
        public Material[]? SwapOriginals;
        /// <summary>The array currently assigned to the renderer while swapped: our fade-shader
        /// copies in the channel-less slots, the AUTHORED material in slots that were already
        /// toggle-native. Non-null = a swap is in place.</summary>
        public Material[]? SwapCopies;
        /// <summary>Per slot: did WE create <see cref="SwapCopies"/>[i]? Only those may be
        /// destroyed on restore — a kept authored slot is a shared game material.</summary>
        public bool[]? SwapOwned;
        /// <summary>The channel decision was evaluated once (cheap re-entry guard).</summary>
        public bool SwapChecked;
        /// <summary>Why this piece has NO dissolve channel (enabled-only), for the round-15
        /// DISSOLVE CENSUS line. Null when it dissolves.</summary>
        public string? DissolveWhy;
    }

    private sealed partial class FadeDriver
    {
        /// <summary>AIRBORNE bar: a prop this far (wu) above its room's floor plane cannot be
        /// standing ON the floor — it hangs, and would float once the wall is gone. Deliberately
        /// the same 1.0 wu the ground exclusion uses (≈ half a hex tile), so "ground" and
        /// "airborne" are complementary by construction.</summary>
        private const float MountedClearanceWU = GroundExclusionHeightWU;
        /// <summary>Max horizontal gap (wu) between a prop and the wall slab it rides. A sconce
        /// touches its wall (gap ≈ 0); the next parallel wall run is ≥ a hex (~1.72 wu) of clear
        /// floor away, so this cannot reach across a room.</summary>
        private const float MountedLinkMaxXZ = 0.9f;
        /// <summary>Widened link for TINY emissive FX meshes (round-9 audit alarm: 26
        /// 'CandleFlame'/'Glow' quads at gap 0.97–1.63 fell through every path and floated
        /// when their wall opened). A mesh whose AABB fits within
        /// <see cref="MountedTinyFxSpanWU"/> in every axis is dressing-FX by construction —
        /// too small to be architecture — and may ride its wall from farther out. Lights
        /// themselves stay untouched, as always.</summary>
        private const float MountedTinyFxLinkMaxXZ = 1.8f;
        private const float MountedTinyFxSpanWU = 0.7f;
        /// <summary>How far (wu) above the wall's own AABB top a prop may still start — cap-mounted
        /// dressing sits slightly proud of the wall top.</summary>
        private const float MountedLinkMaxAboveTopWU = 0.6f;
        /// <summary>MESHES only: a prop bigger than this in any axis (wu) is architecture, not
        /// dressing, and is left alone (fail-open). Never applied to particle systems — their
        /// bounds are a smoke plume, not an object size (it was rejecting the torches' own heat
        /// haze at y[1.2..4.7]).</summary>
        private const float MountedMaxSpanWU = 3.0f;
        /// <summary>MESHES only, second architecture guard (stacked-shell hardware round 2):
        /// the two-fat-axes span test deliberately admits long+thin dressing (banners, hanging
        /// bars) — but a battlement run is long+thin TOO, and once the stacked pass had raised
        /// a wall's AABB top, the fort's rejected superstructure meshes slipped in here as
        /// "dressing" (fade ON 'Wall 2': +22 mounted props incl. TO_Fort_WallTop02/polySurface1)
        /// — riding the fade while contributing ZERO occlusion, silently starving the coverage
        /// trigger. AABB VOLUME separates them: sconces/candles ≪ 0.5 wu³, a big banner with
        /// its bar ≈ 0.5–1 wu³, the smallest fort course ≥ ~2 wu³. Anything above this cap is
        /// architecture — stacked-shell territory or nothing, never sconce dressing.</summary>
        private const float MountedMaxMeshVolumeWU3 = 1.5f;
        /// <summary>Runaway guard — no wall run carries more dressing than this.</summary>
        private const int MountedMaxPerSegment = 32;
        /// <summary>Diagnostic radius (wu): an airborne renderer this close to a wall but NOT
        /// attached is logged with its rejection reason, so a leftover that still floats in a
        /// hardware screenshot is decidable from the log alone.</summary>
        private const float MountedNearMissXZ = 2.5f;
        /// <summary>Cap on the per-census list (log hygiene).</summary>
        private const int MountedCensusCap = 12;
        /// <summary>Cap on the near-miss list — larger than the census cap because this is the
        /// side that answers "why does THAT thing still float".</summary>
        private const int MountedRejectCap = 24;
        /// <summary>How small a particle system's start size gets at full fade (relative) — the
        /// flame shrinks as it dims instead of just thinning out.</summary>
        private const float MountedParticleShrink = 0.15f;
        /// <summary>The prop ramp LEADS the wall's own sweep by this factor: it starts at the same
        /// instant (what "gleichzeitig" means here) but reaches zero at ~80% of the dissolve. The
        /// reason is particle lifetime — a flame particle emitted at fade 0 is still alive 0.35s
        /// later, so a strictly 1:1 ramp would leave a few bright stragglers for the final
        /// renderer-disable to cut off, i.e. exactly the pop this round is removing.</summary>
        private const float MountedFadeLead = 1.25f;

        private static readonly int TintColorId = Shader.PropertyToID("_TintColor");
        private static readonly int ColorPropId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ToggleDissolvePropId = Shader.PropertyToID("_Toggle_Dissolve");
        private static readonly int InvisibilityControlPropId =
            Shader.PropertyToID("_InvisibilityControl");

        /// <summary>Renderers owned by a mounted list THIS rescan (one owner per renderer).</summary>
        private readonly HashSet<Renderer> _mountedOwned = new();
        /// <summary>Every renderer WE currently hold hidden or ramped — the orphan guard's ledger.
        /// A prop in here whose owner segment died is restored by the next rescan even if no
        /// explicit restore path fired.</summary>
        private readonly Dictionary<Renderer, MountedProp> _mountedTouched = new();
        /// <summary>Renderers already spoken for by another attachment type (wall renderers,
        /// foliage, asset siblings) — rebuilt each rescan. The value carries WHO owns it, so a
        /// leftover that was skipped structurally can name its owner (and that owner's fade) in
        /// the census instead of vanishing from the diagnostics.</summary>
        private readonly Dictionary<Renderer, OwnerRef> _attachmentOwned = new();

        /// <summary>Who already owns a renderer, for the structural-skip diagnostic.</summary>
        private readonly struct OwnerRef
        {
            public readonly Segment Seg;
            public readonly string Kind;
            public OwnerRef(Segment seg, string kind) { Seg = seg; Kind = kind; }
        }
        private readonly List<MountedProp> _mountedScratch = new();
        private readonly List<string> _mountedCensus = new();
        private readonly List<string> _mountedRejects = new();
        private MaterialPropertyBlock? _mountedMpb;
        private int _censusMounted;
        private int _censusMountedRejected;
        private int _lastLoggedMountedCount = -1;
        private int _lastLoggedMountedRejected = -1;
        /// <summary>This pass's airborne bar, so the structural-skip diagnostic can use it.</summary>
        private float _mountedAirborneBar = float.PositiveInfinity;

        // ---- delivery -----------------------------------------------------------------------

        /// <summary>
        /// Decide ONCE how this prop can dissolve and snapshot everything the restore needs. The
        /// snapshot is the whole reversibility contract: authored colour, authored cutoff, and the
        /// three particle-module values. Never reads or writes a Light.
        /// </summary>
        private static MountedProp ClassifyProp(Renderer r)
        {
            var p = new MountedProp { Renderer = r };
            Material? mat = r.sharedMaterial;
            if (mat != null)
            {
                if (mat.HasProperty(TintColorId)) p.ColorId = TintColorId;
                else if (mat.HasProperty(ColorPropId)) p.ColorId = ColorPropId;
                else if (mat.HasProperty(BaseColorId)) p.ColorId = BaseColorId;
                if (p.ColorId >= 0)
                    p.BaseColor = mat.GetColor(p.ColorId);
                else if (mat.HasProperty(CutoffId))
                {
                    p.CutoffId = CutoffId;
                    p.BaseCutoff = Mathf.Clamp01(mat.GetFloat(CutoffId));
                }
                // Amp dissolve pair (round 7): a REAL dissolve where the shader offers one.
                if (mat.HasProperty(ToggleDissolvePropId)
                    && mat.HasProperty(InvisibilityControlPropId))
                {
                    p.DissolveToggleId = ToggleDissolvePropId;
                    p.DissolveControlId = InvisibilityControlPropId;
                    p.BaseDissolveControl = Mathf.Clamp01(mat.GetFloat(InvisibilityControlPropId));
                }
            }
            if (r is ParticleSystemRenderer)
            {
                ParticleSystem? ps = r.GetComponent<ParticleSystem>();
                if (ps != null)
                {
                    p.System = ps;
                    ParticleSystem.MainModule main = ps.main;
                    p.StartColor = main.startColor;
                    p.HasStartColor = main.startColor.mode == ParticleSystemGradientMode.Color
                        || main.startColor.mode == ParticleSystemGradientMode.TwoColors;
                    p.StartSize = main.startSizeMultiplier;
                    p.EmissionRate = ps.emission.rateOverTimeMultiplier;
                }
            }
            // ROUND 15: a bare "cutoff" verdict was misleading — the keep's masonry exposes
            // _Cutoff but only dissolves through the wall's map/_Cutoff ramp behind its live
            // wall-fade toggle. Name that class explicitly so the census lines distinguish
            // "dissolves natively" from "needs the material swap".
            bool nativeToggle = mat != null && HasLiveWallFadeToggle(mat);
            p.Tier = (p.DissolveControlId >= 0 ? "dissolve+" : string.Empty)
                + (p.ColorId >= 0 ? "alpha"
                    : nativeToggle ? "wallfade-native"
                    : p.CutoffId >= 0 ? "cutoff" : "no-material-channel")
                + (p.System != null ? "+particles" : string.Empty);
            return p;
        }

        /// <summary>Alpha-scaled copy of a start-colour snapshot (Color / TwoColors modes only —
        /// gradient modes are left alone and rely on the emission ramp).</summary>
        private static ParticleSystem.MinMaxGradient ScaledStartColor(MountedProp p, float alpha)
        {
            if (p.StartColor.mode == ParticleSystemGradientMode.TwoColors)
            {
                Color a = p.StartColor.colorMin, b = p.StartColor.colorMax;
                a.a *= alpha;
                b.a *= alpha;
                return new ParticleSystem.MinMaxGradient(a, b);
            }
            Color c = p.StartColor.color;
            c.a *= alpha;
            return new ParticleSystem.MinMaxGradient(c);
        }

        /// <summary>Drive one prop to the given fade (0 = authored, 1 = gone). Pure delivery — the
        /// caller owns the state machine.</summary>
        private void DriveProp(MountedProp p, float fade)
        {
            Renderer r = p.Renderer;
            if (r == null)
                return;
            if (p.NativeFade)
            {
                // Round 15: the piece runs the game's own masonry fade branch — either on its
                // OWN toggle-native materials or on swapped copies. Either way it needs the
                // WALL renderers' map/_Cutoff ramp, not the foliage cutoff lerp below (that
                // lerp is what made the gate's toggle-native courses pop: _Cutoff alone,
                // without the occlusion map or the fade gate, is not a dissolve). Opaque
                // per-pixel clip, no alpha blending — MR chroma-key ruling.
                DriveNativeProp(p, fade);
                return;
            }
            float visible = Mathf.Clamp01(1f - fade);
            if (p.ColorId >= 0 || p.CutoffId >= 0 || p.DissolveControlId >= 0)
            {
                _mountedMpb ??= new MaterialPropertyBlock();
                _mountedMpb.Clear();
                if (p.ColorId >= 0)
                {
                    Color c = p.BaseColor;
                    c.a *= visible;
                    _mountedMpb.SetColor(p.ColorId, c);
                }
                else if (p.CutoffId >= 0)
                {
                    _mountedMpb.SetFloat(p.CutoffId,
                        Mathf.Lerp(p.BaseCutoff, FoliageCutoffEnd, fade));
                }
                if (p.DissolveControlId >= 0)
                {
                    // Real Amp dissolve (round 7): open the gate, sweep the control toward
                    // fully invisible. Per-renderer MPB — the shared material is untouched.
                    _mountedMpb.SetFloat(p.DissolveToggleId, 1f);
                    _mountedMpb.SetFloat(p.DissolveControlId,
                        Mathf.Lerp(p.BaseDissolveControl, 1f, fade));
                }
                r.SetPropertyBlock(_mountedMpb);
            }
            if (p.System != null)
            {
                ParticleSystem.MainModule main = p.System.main;
                if (p.HasStartColor)
                    main.startColor = ScaledStartColor(p, visible);
                main.startSizeMultiplier = p.StartSize * Mathf.Lerp(1f, MountedParticleShrink, fade);
                ParticleSystem.EmissionModule em = p.System.emission;
                em.rateOverTimeMultiplier = p.EmissionRate * visible;
            }
        }

        /// <summary>Put one prop back exactly as authored: property block cleared, particle
        /// modules restored from the snapshot, renderer visible again.</summary>
        private void RestoreProp(MountedProp p)
        {
            _mountedTouched.Remove(p.Renderer);
            Renderer r = p.Renderer;
            // Did we ever write a property block on this renderer? (Read BEFORE the swap
            // restore clears NativeFade — a natively-driven piece has no colour/cutoff id of
            // its own to infer it from.)
            bool wroteBlock = p.NativeFade
                || p.ColorId >= 0 || p.CutoffId >= 0 || p.DissolveControlId >= 0;
            RestorePropSwap(p, r); // round 15: authored materials back, only OUR copies destroyed
            if (r == null)
                return;
            NoteOwnershipChange(r, "released"); // churn tripwire (round 11)
            if (wroteBlock)
                r.SetPropertyBlock(null);
            if (p.System != null)
            {
                ParticleSystem.MainModule main = p.System.main;
                if (p.HasStartColor)
                    main.startColor = p.StartColor;
                main.startSizeMultiplier = p.StartSize;
                ParticleSystem.EmissionModule em = p.System.emission;
                em.rateOverTimeMultiplier = p.EmissionRate;
            }
            if (!r.enabled)
                r.enabled = true;
        }

        /// <summary>Restore ALL of a segment's mounted props — called on every path where the
        /// segment stops owning them (unfade, segment drop, group split, toggle-off, teardown),
        /// so no torch can stay hidden without an owner.</summary>
        private void RestoreSegmentMounted(Segment seg)
        {
            if (seg.MountedState == 0)
                return;
            seg.MountedState = 0;
            foreach (MountedProp p in seg.Mounted)
                RestoreProp(p);
        }

        /// <summary>
        /// Drive the segment's mounted dressing alongside its fade — the SAME 0→1 the wall's own
        /// cutoff sweep runs on, so flame and wall go together instead of the flame outliving the
        /// wall and popping. The renderer is disabled at the very end as the guarantee that
        /// nothing survives; everything reverses exactly on unfade.
        /// </summary>
        private void ApplyMounted(Segment seg)
        {
            if (seg.Mounted.Count == 0)
                return;
            int want = seg.Fade >= FoliageHideFade ? 2 : seg.Fade > 0f ? 1 : 0;
            if (want == 0)
            {
                RestoreSegmentMounted(seg);
                return;
            }
            // No held-state early-out (round 5, the regen-churn lesson — see ApplyStacked):
            // a prop adopted or re-enabled while the segment is already held faded must be
            // hidden THIS frame. Held steady state = one enabled compare per prop.
            float ramp = Mathf.Clamp01(seg.Fade * MountedFadeLead);
            bool lost = false;
            foreach (MountedProp p in seg.Mounted)
            {
                if (p.Renderer == null)
                {
                    lost = true;
                    continue;
                }
                if (want == 2)
                {
                    if (p.Renderer.enabled)
                    {
                        _mountedTouched[p.Renderer] = p;
                        EnsureDissolveChannel(p);
                        DriveProp(p, ramp);
                        p.Renderer.enabled = false;
                    }
                }
                else
                {
                    _mountedTouched[p.Renderer] = p;
                    EnsureDissolveChannel(p); // round 15: dressing without a channel animates too
                    DriveProp(p, ramp);
                    if (!p.Renderer.enabled)
                        p.Renderer.enabled = true;
                }
            }
            if (lost)
                _nextRescan = 0f; // prop regenerated away mid-fade — re-collect promptly
            seg.MountedState = want;
        }

        /// <summary>Re-authorize EVERY prop we ever touched and empty the ledger (teardown / mod
        /// disable): after this call the mod holds nothing hidden or ramped, owner or not.</summary>
        private void RestoreAllMountedProps()
        {
            if (_mountedTouched.Count == 0)
                return;
            _mountedScratch.Clear();
            _mountedScratch.AddRange(_mountedTouched.Values);
            foreach (MountedProp p in _mountedScratch)
                RestoreProp(p);
            _mountedScratch.Clear();
            _mountedTouched.Clear();
            foreach (Segment seg in _segments.Values)
            {
                seg.MountedState = 0;
                seg.StackedState = 0; // stacked pieces share the ledger just emptied
                seg.BodyState = 0;    // …as do the plain wall-body meshes
            }
        }

        // ---- collection ---------------------------------------------------------------------

        /// <summary>Renderer families that can be wall dressing. SkinnedMeshRenderer was IN
        /// from round 3 (skinned banners) until ROUND 7 REVOKED it: the BRUTE's horned head
        /// accessory was adopted by a wall sweep and permanently hidden (mauern_problem_neu
        /// .png) — figures are NEVER touched (Lights-rule severity), and "skinned = possibly
        /// a character" is exactly the ambiguity the airtight guard forbids. Skinned banners
        /// stay visible (fail-open, accepted). Line/Trail renderers stay out: effects, never
        /// scenery.</summary>
        private static bool IsMountableRendererType(Renderer r) =>
            r is MeshRenderer || r is ParticleSystemRenderer || r is SpriteRenderer;

        /// <summary>
        /// Log a renderer that left the sweep BEFORE any geometric test (wrong renderer family,
        /// already owned by another attachment list, fade-capable) — but only when it is airborne
        /// and near a wall, i.e. only when it could actually be a floating leftover. Bounded by
        /// the reject-list cap, which is checked first so the common case is one int compare.
        /// </summary>
        private void NoteStructuralSkip(Renderer c, string why)
        {
            if (_mountedRejects.Count >= MountedRejectCap || float.IsInfinity(_mountedAirborneBar))
                return;
            Bounds b = c.bounds;
            float anchorY = c is ParticleSystemRenderer ? c.transform.position.y : b.min.y;
            if (anchorY < _mountedAirborneBar)
                return; // rests on something — would not float even if the wall went
            float nearest = float.PositiveInfinity;
            foreach (Segment seg in _segments.Values)
            {
                if (!seg.HasBounds)
                    continue;
                float gap = HorizontalGap(seg.Bounds, b);
                if (gap < nearest)
                    nearest = gap;
            }
            NoteMountedReject(c, anchorY, nearest, why);
        }

        /// <summary>Horizontal (XZ) gap between two AABBs; 0 when their footprints overlap.</summary>
        private static float HorizontalGap(Bounds a, Bounds b)
        {
            float dx = Mathf.Max(0f, Mathf.Max(a.min.x - b.max.x, b.min.x - a.max.x));
            float dz = Mathf.Max(0f, Mathf.Max(a.min.z - b.max.z, b.min.z - a.max.z));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Horizontal (XZ) gap between an AABB and a point.</summary>
        private static float HorizontalGap(Bounds a, Vector3 p)
        {
            float dx = Mathf.Max(0f, Mathf.Max(a.min.x - p.x, p.x - a.max.x));
            float dz = Mathf.Max(0f, Mathf.Max(a.min.z - p.z, p.z - a.max.z));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// Re-attach, per rescan, every airborne dressing renderer hugging a wall segment (see the
        /// file header for the rule, the emitter-anchor decision and the light guarantee). Runs
        /// LAST in <c>Rescan</c>: it needs the final segment table, their room association and
        /// their ground-stripped AABBs. Leavers and orphans are restored here, so nothing can stay
        /// hidden without an owner.
        /// </summary>
        /// <param name="sceneRenderers">The rescan's single scene sweep (shared with the wall
        /// adoption pass — one FindObjectsOfType per rescan, not two).</param>
        private void CollectWallMountedProps(Renderer[] sceneRenderers)
        {
            _mountedOwned.Clear();
            _attachmentOwned.Clear();
            _mountedCensus.Clear();
            _mountedRejects.Clear();
            _censusMounted = 0;
            _censusMountedRejected = 0;

            // STACKED SHELL pieces (adopted by the pass right before this one) are spoken for
            // FIRST: they must never be double-claimed by a sticky mounted list, the sweep
            // below, or the orphan guard (which restores any ledger entry missing from
            // _mountedOwned — a stacked piece IS in the shared ledger while ramped/hidden).
            foreach (Segment seg in _segments.Values)
            {
                foreach (MountedProp p in seg.Stacked)
                {
                    if (p.Renderer == null)
                        continue;
                    _mountedOwned.Add(p.Renderer);
                    _attachmentOwned[p.Renderer] = new OwnerRef(seg, "stacked shell piece");
                }
                foreach (MountedProp p in seg.Body)
                {
                    if (p.Renderer == null)
                        continue;
                    _mountedOwned.Add(p.Renderer);
                    _attachmentOwned[p.Renderer] = new OwnerRef(seg, "wall body mesh");
                }
            }
            // Shared corner pieces (round 7) are spoken for too — never sconce dressing,
            // and the orphan guard must not release them while their neighbors are faded.
            RegisterCornerOwnership();

            // Park the previous lists and record who is already spoken for. STICKY OWNERSHIP: a
            // segment that is mid-fade or held faded keeps every prop it already owns — releasing
            // one while its wall is gone is exactly the blink the first hardware round produced.
            foreach (Segment seg in _segments.Values)
            {
                seg.PrevMounted.Clear();
                seg.PrevMounted.AddRange(seg.Mounted);
                seg.Mounted.Clear();
                bool sticky = seg.MountedState != 0 || seg.Fade > 0f;
                if (sticky)
                {
                    foreach (MountedProp p in seg.PrevMounted)
                    {
                        if (p.Renderer == null || !_mountedOwned.Add(p.Renderer))
                            continue;
                        // Figures are NEVER carried, sticky or not (round-7 ruling).
                        if (IsFigureOrActorRenderer(p.Renderer))
                            continue;
                        seg.Mounted.Add(p);
                        _censusMounted++;
                    }
                }
                foreach (MeshRenderer r in seg.Renderers)
                {
                    if (r != null) _attachmentOwned[r] = new OwnerRef(seg, "wall renderer");
                }
                foreach (MeshRenderer f in seg.Foliage)
                {
                    if (f != null) _attachmentOwned[f] = new OwnerRef(seg, "foliage");
                }
                foreach (MeshRenderer s in seg.Siblings)
                {
                    if (s != null) _attachmentOwned[s] = new OwnerRef(seg, "asset sibling");
                }
            }

            // Cheap pre-filter for "airborne": the LOWEST tile-anchored floor plane in the scene.
            // Rooms without an anchor are fail-safe solid anyway (their walls never fade).
            float minFloorY = float.PositiveInfinity;
            for (int i = 0; i < _roomFloorY.Count && i < _roomFloorAnchored.Count; i++)
            {
                if (_roomFloorAnchored[i] && _roomFloorY[i] < minFloorY)
                    minFloorY = _roomFloorY[i];
            }

            _mountedAirborneBar = minFloorY + MountedClearanceWU;
            if (!float.IsInfinity(minFloorY) && sceneRenderers != null)
            {
                float airborneBar = _mountedAirborneBar;
                foreach (Renderer c in sceneRenderers)
                {
                    if (c == null)
                        continue;
                    if (IsModObject(c))
                        continue; // mod-owned visual (hands, cards, panels, MR backing — by
                                  // layer OR 'GloomhavenVR.' name prefix) — never scenery,
                                  // never a candidate, never in the diagnostics
                    if (_mountedOwned.Contains(c))
                        continue; // already attached this rescan (sticky or earlier in the sweep)
                    // STRUCTURAL SKIPS — the three ways a renderer leaves this sweep before any
                    // geometric test runs. Each is LOGGED when it stands near a wall (round 3: a
                    // banner's wooden bar survived a fade and appeared in no reject list at all,
                    // because it left here silently). NoteStructuralSkip itself is cheap: it does
                    // nothing unless the renderer is airborne AND close to a segment.
                    if (!IsMountableRendererType(c))
                    {
                        NoteStructuralSkip(c, $"renderer type {c.GetType().Name} is not scenery");
                        continue;
                    }
                    if (_attachmentOwned.TryGetValue(c, out OwnerRef owner))
                    {
                        string wall = owner.Seg.Anchor != null ? owner.Seg.Anchor.name : "<dead>";
                        NoteStructuralSkip(c,
                            $"already the {owner.Kind} of '{wall}' (that wall's fade {owner.Seg.Fade:F2})");
                        continue;
                    }
                    if (c is MeshRenderer mr && RendererUsesWallFade(mr))
                    {
                        // ROUND-11 SCONCE EXCEPTION: torch-fire bowls carry the WallFade
                        // shader too, and the narrowed doorway grouping now leaves the
                        // non-arch ones UNCLAIMED — a SMALL unclaimed fade-shader mesh is
                        // sconce dressing and proceeds into the geometric tests below so it
                        // rides its wall (gate columns included). Wall-sized fade meshes
                        // keep the old skip: they are walls, not dressing.
                        Bounds fb = mr.bounds;
                        bool sconceScale = fb.size.x <= MountedMaxSpanWU
                            && fb.size.y <= MountedMaxSpanWU
                            && fb.size.z <= MountedMaxSpanWU
                            && fb.size.x * fb.size.y * fb.size.z <= MountedMaxMeshVolumeWU3;
                        if (!sconceScale)
                        {
                            // A wall in its own right — it has its own fade decision. If it
                            // belongs to a segment that is NOT fading while its neighbour
                            // is, that is exactly how a piece of wall trim survives; the
                            // owner label above says which.
                            NoteStructuralSkip(c,
                                "carries a WallFade shader — no segment claimed it");
                            continue;
                        }
                    }

                    // WHICH GEOMETRY DECIDES (see the file header): a mesh is judged by its AABB,
                    // a particle system by its EMITTER — its bounds enclose the live particles and
                    // drift every frame, which is what made the candles blink.
                    bool particles = c is ParticleSystemRenderer;
                    Bounds b = c.bounds;
                    // Round-13: particles are arch-tested by their EMITTER, not their live
                    // particle bounds (which drift every frame and made the arch fires'
                    // protection flicker) — the same anchor rule the mounting itself uses.
                    Bounds archProbe = particles
                        ? new Bounds(c.transform.position, Vector3.zero)
                        : b;
                    if (IsArchProtected(archProbe, c.name))
                        continue; // the doorway's arch stays solid (user ruling 2026-08-07)
                    // WATER FEATURE (user ruling 2026-08-09, brunnen.png): the fountain's own
                    // waterfall/spark emitters sit inside its basin — they must not be mounted
                    // onto a wall and dragged out with it while the water plane stays.
                    if (IsWaterProtected(archProbe))
                        continue;
                    float anchorY = particles ? c.transform.position.y : b.min.y;
                    float topY = particles ? c.transform.position.y : b.max.y;
                    // Tiny emissive FX quads (candle flames, glows) may ride from farther out
                    // (round-9 audit alarm — they float when their wall opens).
                    bool tinyFx = !particles
                        && b.size.x <= MountedTinyFxSpanWU
                        && b.size.y <= MountedTinyFxSpanWU
                        && b.size.z <= MountedTinyFxSpanWU;
                    float linkMax = tinyFx ? MountedTinyFxLinkMaxXZ : MountedLinkMaxXZ;

                    // Anything sitting essentially ON the floor is not a candidate at all and is
                    // dropped here (the cheap bulk filter). Between that and the airborne bar lies
                    // the ONE failure mode this rule can plausibly get wrong — a sconce whose mesh
                    // reaches far enough down to look floor-supported — so those still run the
                    // wall search and are LOGGED with their exact height instead of vanishing
                    // silently from the diagnostics.
                    if (anchorY < minFloorY + MountedClearanceWU * 0.25f)
                        continue;
                    bool belowBar = anchorY < airborneBar;

                    // Nearest eligible wall wins. Doorway segments (never fade) and segments
                    // without a trusted room plane attach nothing.
                    Segment? best = null;
                    float bestGap = float.PositiveInfinity;
                    float nearestAny = float.PositiveInfinity;
                    foreach (Segment seg in _segments.Values)
                    {
                        if (!seg.HasBounds || seg.DoorRoot != null || !RoomDecisionValid(seg.RoomIndex))
                            continue;
                        float gap = particles
                            ? HorizontalGap(seg.Bounds, c.transform.position)
                            : HorizontalGap(seg.Bounds, b);
                        if (gap < nearestAny)
                            nearestAny = gap;
                        if (belowBar || gap > linkMax || gap >= bestGap)
                            continue;
                        if (anchorY < _roomFloorY[seg.RoomIndex] + MountedClearanceWU)
                            continue; // airborne against THIS room's plane, not just the lowest
                        if (anchorY > seg.Bounds.max.y + MountedLinkMaxAboveTopWU)
                            continue; // floats above the wall, not in it
                        if (topY < seg.Bounds.min.y)
                            continue; // below the wall's span
                        if (seg.Mounted.Count >= MountedMaxPerSegment)
                            continue;
                        bestGap = gap;
                        best = seg;
                    }
                    if (belowBar)
                    {
                        NoteMountedReject(c, anchorY, nearestAny,
                            $"anchor {anchorY:F2} under the airborne bar {airborneBar:F2} — "
                            + "reads as floor-supported, so it would NOT float");
                        continue;
                    }
                    if (best == null)
                    {
                        NoteMountedReject(c, anchorY, nearestAny, "no wall within reach / outside its span");
                        continue;
                    }
                    // Size cap for MESHES only — a particle system's bounds are a smoke plume, not
                    // an object size (it was rejecting the torches' own heat haze). TWO fat axes
                    // are required: dressing is routinely long and thin (a banner and the bar it
                    // hangs from span a whole wall), while architecture is bulky in two.
                    int fatAxes = (b.size.x > MountedMaxSpanWU ? 1 : 0)
                        + (b.size.y > MountedMaxSpanWU ? 1 : 0)
                        + (b.size.z > MountedMaxSpanWU ? 1 : 0);
                    if (!particles && fatAxes >= 2)
                    {
                        NoteMountedReject(c, anchorY, bestGap, "too big for dressing (architecture)");
                        continue;
                    }
                    // Architecture-scale VOLUME guard (stacked-shell round 2): long+thin passes
                    // the span test, but a wall course is long+thin too — see the constant.
                    float volume = b.size.x * b.size.y * b.size.z;
                    if (!particles && volume > MountedMaxMeshVolumeWU3)
                    {
                        NoteMountedReject(c, anchorY, bestGap,
                            $"architecture-scale (AABB volume {volume:F1} wu³ > "
                            + $"{MountedMaxMeshVolumeWU3:F1}) — stacked-shell territory, "
                            + "never sconce dressing");
                        continue;
                    }
                    if (IsFigureOrActorRenderer(c))
                    {
                        NoteMountedReject(c, anchorY, bestGap,
                            "FIGURE (never touched — round-7 ruling, Lights-rule severity)");
                        continue;
                    }
                    // FLOOR-STANDING PROP (skelet.jpg, fourth round): the wall path refuses these
                    // at CollectWallFadeInfo, which leaves them UNCLAIMED — and this sweep runs
                    // afterwards and is purely geometric, so without this line it would adopt the
                    // skeleton's skull as sconce dressing and fade it anyway. The guard above
                    // covered the rule's FIGURE arm by coincidence (a figure is refused here on
                    // its own account); the FLOOR arm has no such cover. The mounted rule itself
                    // is unchanged: a prop whose UNIT hangs a metre over the floor is not a unit
                    // that reaches the floor, so nothing this pass protects can meet it. See
                    // WallSegmentFade.Standing.cs.
                    if (IsStandingFigureProp(c))
                    {
                        NoteStandingPropBlocked(c, null);
                        NoteMountedReject(c, anchorY, bestGap,
                            "part of a prop unit that STANDS ON THE FLOOR — never wall dressing "
                            + "(WallSegmentFade.Standing.cs)");
                        continue;
                    }
                    if (c.GetComponentInParent<TileBehaviour>() != null
                        || c.GetComponentInParent<Canvas>() != null
                        || c.GetComponent<TMPro.TMP_Text>() != null)
                    {
                        NoteMountedReject(c, anchorY, bestGap, "game logic / worldspace UI");
                        continue;
                    }
                    // A renderer the GAME disabled is not ours to manage — except one WE hold
                    // hidden (dropping it now would re-enable + re-hide it in a one-frame flash).
                    if (!c.enabled && !_mountedTouched.ContainsKey(c))
                        continue;
                    // Reuse the existing record when we already know this prop (keeps the authored
                    // snapshot — re-reading a material we are CURRENTLY ramping would snapshot our
                    // own ramp as the "authored" value).
                    if (!_mountedTouched.TryGetValue(c, out MountedProp? prop))
                        prop = ClassifyProp(c);
                    best.Mounted.Add(prop);
                    _mountedOwned.Add(c);
                    NoteOwnershipChange(c,
                        $"mounted:'{(best.Anchor != null ? best.Anchor.name : "?")}'");
                    _censusMounted++;
                    if (_mountedCensus.Count < MountedCensusCap)
                    {
                        string wall = best.Anchor != null ? best.Anchor.name : "<dead>";
                        _mountedCensus.Add(
                            $"'{c.name}'[{RendererKind(c)}→{prop.Tier}] anchor {anchorY:F1} "
                            + $"gap {bestGap:F2} → '{wall}'");
                    }
                }
            }

            // Leavers: restore anything this segment held that it no longer owns.
            foreach (Segment seg in _segments.Values)
            {
                if (seg.MountedState != 0)
                {
                    foreach (MountedProp prev in seg.PrevMounted)
                    {
                        if (prev.Renderer != null && !seg.Mounted.Contains(prev))
                            RestoreProp(prev);
                    }
                    if (seg.Mounted.Count == 0)
                        seg.MountedState = 0;
                }
                seg.PrevMounted.Clear();
            }

            // ORPHAN GUARD (the foliage-orphan lesson, made unconditional): anything in our ledger
            // that no live segment owns any more is re-authorized NOW — even if the segment died
            // on a path that forgot to restore. Worst case a prop stays hidden for one rescan.
            if (_mountedTouched.Count > 0)
            {
                _mountedScratch.Clear();
                foreach (MountedProp p in _mountedTouched.Values)
                {
                    if (p.Renderer == null || !_mountedOwned.Contains(p.Renderer))
                        _mountedScratch.Add(p);
                }
                foreach (MountedProp p in _mountedScratch)
                    RestoreProp(p);
                _mountedScratch.Clear();
            }

            // Apparance streams the dressing in over several rescans, so the scenario's first
            // heartbeat would report a half-built table forever: re-log whenever the attached set
            // actually changed. Steady state prints nothing.
            if (_censusMounted != _lastLoggedMountedCount
                || _censusMountedRejected != _lastLoggedMountedRejected)
                LogMountedCensus();
        }

        // ---- diagnostics --------------------------------------------------------------------

        /// <summary>First few mounted prop names for the fade-ON line (static — LogStateFlip is).</summary>
        private static string MountedNames(Segment seg)
        {
            if (seg.Mounted.Count == 0)
                return "none";
            var sb = new System.Text.StringBuilder();
            int listed = 0;
            foreach (MountedProp p in seg.Mounted)
            {
                if (p.Renderer == null)
                    continue;
                if (listed++ >= 4) { sb.Append(", …"); break; }
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(p.Renderer.name).Append('/').Append(p.Tier);
            }
            return sb.Length > 0 ? sb.ToString() : "none";
        }

        private static string RendererKind(Renderer r) =>
            r is ParticleSystemRenderer ? "particles"
            : r is SpriteRenderer ? "sprite"
            : r is SkinnedMeshRenderer ? "skinned"
            : r is MeshRenderer ? "mesh"
            : r.GetType().Name;

        private void NoteMountedReject(Renderer c, float anchorY, float gap, string why)
        {
            if (gap > MountedNearMissXZ)
                return; // not near any wall — not a leftover candidate at all
            _censusMountedRejected++;
            if (_mountedRejects.Count < MountedRejectCap)
                _mountedRejects.Add($"'{c.name}'[{RendererKind(c)}] anchor {anchorY:F1} gap {gap:F2}: {why}");
        }

        /// <summary>
        /// Heartbeat forensics for the "schwebende Items" class: WHAT rides a wall's fade, through
        /// WHICH dissolve channel (alpha / cutoff / particles / none — a prop stuck on
        /// "no-material-channel" is the one that can still pop), and which airborne renderer NEAR
        /// a wall was rejected and why.
        /// </summary>
        private void LogMountedCensus()
        {
            _lastLoggedMountedCount = _censusMounted;
            _lastLoggedMountedRejected = _censusMountedRejected;
            if (_censusMounted == 0 && _censusMountedRejected == 0)
                return;
            string riding = _mountedCensus.Count > 0 ? string.Join("; ", _mountedCensus) : "none new";
            string misses = _mountedRejects.Count > 0
                ? " | NEAR-MISS (stays visible): " + string.Join("; ", _mountedRejects)
                : string.Empty;
            VRLog.Info(Name,
                $"WALL-MOUNTED DRESSING: {_censusMounted} prop(s) dissolve WITH their wall "
                + $"(airborne ≥{MountedClearanceWU:0.0} wu over the room floor — meshes by AABB, "
                + $"particles by EMITTER anchor; XZ gap ≤{MountedLinkMaxXZ:0.00} wu; ownership "
                + $"sticky while faded; Lights are NEVER written to): {riding}"
                + $"{misses} ({_censusMountedRejected} near-miss total).");
        }
    }
}
