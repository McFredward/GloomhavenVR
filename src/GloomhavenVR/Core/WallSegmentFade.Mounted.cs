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
/// WallSegmentFade attachment. ModBuild 257 adds no networked state either: the mobility test,
/// the leftover audit and the release reasons are all derived from local scene geometry and
/// local renderer state.
///
/// MODBUILD 257 — "die blaue Flamme faded manchmal nicht mit, daher schwebt sie da in der Luft"
/// (user, 2026-08-24, wand_problem2.jpg; "ziemlich random, ich konnte kein Muster erkennen").
/// Four things came out of the ModBuild 256 log, and the first one is a correction:
/// <list type="number">
/// <item>THE 59 "released" TRANSITIONS ARE NOT A BROKEN STICKY GUARD. They arrive in exactly two
///   contiguous blocks — log lines 2088–2114 (27 warnings) immediately after
///   <c>fade OFF 'Wall 3'</c> at 2078, and 2198–2228 (31 warnings) immediately after
///   <c>fade OFF 'Wall 1'</c> at 2188 — and 'Wall 3' owned exactly 27 mounted props while
///   'Wall 1' owned exactly 31. Every one of them is <see cref="FadeDriver.RestoreSegmentMounted"/>
///   on the UN-FADE edge, one call per prop, which is the system working correctly. The
///   round-11 churn tripwire fires at 3 transitions in 60 s, and a wall that fades and unfades
///   twice inside a minute gives every prop it owns exactly that: it was counting the player
///   walking around. Releases now carry their REASON, so an unfade can no longer be mistaken
///   for an ownership loss.</item>
/// <item>THE ONE GENUINE OWNERSHIP CONTEST in that log is 'Glow' flapping between
///   <c>stacked:'ThickDoor : (1a01…)'</c> and <c>mounted:'Wall 3'</c>. A doorway segment NEVER
///   fades, so every rescan it won the claim left 'Glow' restored to visible with no owner that
///   could ever hide it again. An owner that cannot fade is no longer conceded to.</item>
/// <item>FIGURE VFX WERE BEING ADOPTED AS DRESSING — see
///   <see cref="FadeDriver.MountedAnchorDriftWU"/>.</item>
/// <item>NO INSTRUMENT IN THIS FILE COULD SEE THE REPORTED DEFECT. The DISSOLVE CENSUS printed
///   "0 still ENABLED-ONLY … nothing pops" for every fade in the session, and it could not have
///   printed anything else: it walks <c>seg.Mounted</c>, and a leftover is by construction a
///   renderer that is NOT in <c>seg.Mounted</c>. Meanwhile the NEAR-MISS list — the one
///   diagnostic that answers "why does THAT thing still float" — was capped OUT at 24 of 24
///   entries, about 18 of them "already the foliage of 'Wall 1'", i.e. renderers that are owned
///   and cannot float. The LEFTOVER AUDIT (see <see cref="FadeDriver._mountedLeftovers"/>) reads
///   the renderer and the emitter instead of the ledger, and has its own capacity.</item>
/// </list>
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
        /// <summary>Runaway guard — no wall run carries more dressing than this.
        /// <para>ModBuild 257 RAISED IT FROM 32, on a measurement: in the ModBuild 256 hardware
        /// log every one of the three <c>fade ON</c> lines for 'Wall 1' reads "+31 mounted
        /// prop(s)" and every one for 'Wall 3' reads "+27". Thirty-one against a cap of
        /// thirty-two is not a runaway guard, it is a live constraint one prop wide — and when
        /// it binds it does so SILENTLY and in the worst possible way: the segment drops out of
        /// the owner search below, so the candidate falls through to
        /// <c>best == null</c> and is filed under "no wall within reach / outside its span",
        /// a reason that is simply false. A torch turned away for that reason then stays lit
        /// over a wall that fades, which is the report this round is answering, and it is
        /// "random" because whether the cap binds depends on how many TRANSIENT props (see
        /// <see cref="MountedAnchorDriftWU"/>) happen to be standing next to that wall at
        /// rescan time.</para>
        /// <para>FALSIFIER: <c>MountedSaturated</c> in the census line. A segment that reports
        /// SATURATED at 64 is a real runaway and this number is wrong; a session with no such
        /// line proves the cap is back to being a guard rather than a policy.</para></summary>
        private const int MountedMaxPerSegment = 64;
        /// <summary>
        /// MOBILE PROPS ARE NOT WALL DRESSING (ModBuild 257, from the ModBuild 256 log).
        ///
        /// <para>THE MEASUREMENT. The wall-mounted census adopted <c>P_Elementalist_Chest</c>,
        /// <c>P_Elementalist_Eye_L</c>, <c>P_Elementalist_Eye_R</c>,
        /// <c>P_Elementalist_Hand_L (1)</c> and <c>P_Elementalist_Hand_R (1)</c> — a player
        /// character's own spell VFX — as dressing on 'Wall 3' AND, minutes later, on 'Wall 1'.
        /// They share a signature with eight more adopted emitters ('Particle System (3)'…'(6)',
        /// 'center (1)', 'center (2)', 'Fog (6)', 'Fog (7)'): tier <c>dissolve+…</c> (the Amp
        /// figure pair <c>_Toggle_Dissolve</c>/<c>_InvisibilityControl</c>), emitter anchor
        /// 1.2–1.4 wu — chest height for a figure standing on the floor — and adoption by TWO
        /// different walls in one session. The genuine dressing in the same log has none of
        /// that: 'Candle_Fire_FX_02 (3)', 'p_fire_torch (8)', 'p_Moths_Torch_Wall (1)',
        /// 'fx_sparks (1)', 'distort', 'Glow' sit at 2.2–2.6 wu, carry no dissolve pair, and
        /// never change wall.</para>
        ///
        /// <para>WHY THE ROUND-7 FIGURE GUARD MISSES THEM: <see cref="IsFigureOrActorRenderer"/>
        /// asks for a SkinnedMeshRenderer or an ActorBehaviour / CInteractableActor / Animator
        /// ANCESTOR. These emitters have none — they are world-rooted effect objects driven to
        /// follow their figure by script, so the ancestry test answers "not a figure" perfectly
        /// correctly and the ruling is still violated. Containment is not identity.</para>
        ///
        /// <para>THE TEST IS THE DEFINING PROPERTY OF DRESSING: a sconce is bolted to its wall
        /// and does not move relative to it; a character's aura does. The drift is measured in
        /// the OWNING WALL ANCHOR'S OWN FRAME and re-scaled by that anchor's lossy scale, so a
        /// world grab (translate, rotate, zoom — WorldGrab.cs scales the whole diorama) moves
        /// prop and wall together and reads ZERO. This bar is well under one hex step
        /// (1.72 wu, the smallest move a figure can make) and well over any authored idle
        /// wobble.</para>
        ///
        /// <para>FAIL-OPEN, and that direction is deliberate: a prop judged mobile is NOT
        /// adopted and is restored if we were holding it, i.e. it stays visible — the same
        /// safe side the Lights and figure rules take. FALSIFIER: the <c>MOBILE PROP</c> warn
        /// names every renderer this rejects with its measured drift. A real sconce in that
        /// list (a swinging lantern would be the honest candidate) means the bar is too low.
        /// </para></summary>
        private const float MountedAnchorDriftWU = 0.5f;
        /// <summary>Cap on the mobile-prop warn list (log hygiene) and on the anchor ledger.</summary>
        private const int MountedMobileWarnCap = 8;
        private const int MountedAnchorLedgerCap = 512;
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

        // ---- ModBuild 257 instrumentation and guards ------------------------------------------

        /// <summary>Where a prop sat LAST rescan, in its owning wall anchor's own frame — the
        /// input to the mobility test (see <see cref="MountedAnchorDriftWU"/>).</summary>
        private readonly struct PropAnchor
        {
            public readonly Segment Owner;
            public readonly Vector3 Local;
            public PropAnchor(Segment owner, Vector3 local) { Owner = owner; Local = local; }
        }
        private readonly Dictionary<Renderer, PropAnchor> _mountedAnchorLedger = new();
        /// <summary>Renderers proven MOBILE. Latched for the renderer's lifetime: a thing that
        /// walked once is not scenery, and Apparance gives a genuinely rebuilt prop a new
        /// Renderer instance (and therefore a clean slate) anyway.</summary>
        private readonly HashSet<Renderer> _mountedMobile = new();
        private readonly List<string> _mountedMobileWarns = new();
        private int _censusMountedMobile;

        /// <summary>Segments that hit <see cref="MountedMaxPerSegment"/> this rescan — the cap
        /// used to bind silently and then mis-attribute the loss (see the constant).</summary>
        private readonly List<string> _mountedSaturated = new();

        /// <summary>Props the LEAVERS loop must not touch: either already restored earlier in
        /// this collection pass (the sticky loop's figure/mobile releases) or handed straight to
        /// another owner by the ModBuild-258 unit-affinity rule. Without this the leavers loop
        /// would restore them a second time, and a second <c>NoteOwnershipChange</c> with a
        /// different reason string counts as another transition — i.e. the fix would feed the very
        /// churn tripwire it exists to quieten. A HANDOVER is deliberately not a restore: the new
        /// owner's <see cref="ApplyMounted"/> drives the prop to ITS fade on the same frame (and
        /// to solid through <see cref="RestoreSegmentMounted"/> when that fade is 0), so the piece
        /// never blinks on its way between two walls.</summary>
        private readonly HashSet<MountedProp> _mountedReleased = new();

        /// <summary>
        /// UNIT AFFINITY (ModBuild 258) — WHICH SEGMENT ALREADY OWNS THIS PROP'S OWN PROP UNIT.
        ///
        /// <para>THE DEFECT, from the ModBuild-257 log rather than from reasoning. The
        /// <c>FADE WRITE</c> census reports <c>TORN 'CA_ICY_WallLight' 4/9 written</c> and prints
        /// both halves of the tear: <c>'CV_Ice_Crystal_Form_01'[mesh] under 'Walls/Wall 4/Generated
        /// Content/CA_ICY_WallLight' … ← wall renderer of 'Wall 4' fade 1.00</c> against
        /// <c>'center'[particles] under 'Wall 4/Generated Content/CA_ICY_WallLight/p_fire_torch (8)'
        /// … ← mounted dressing of 'Wall 1' fade 1.00</c>, with
        /// <c>LEFT SOLID under the same root: p_fire_torch (8), fx_sparks (1), distort, +2 more</c>
        /// on one rescan and <c>LEFT SOLID … CV_Ice_Crystal_Form_01, …</c> on another. ONE prop,
        /// TWO owners, two independent fades — so whichever wall goes first, half the wall light
        /// survives it. The material of the surviving half is <c>FireTorchSparks_Blue_MAT</c>:
        /// that is the blue flame in <c>wandproblem3.jpg</c>.</para>
        ///
        /// <para>WHY THE PROP-UNIT PASS DOES NOT ALREADY HEAL IT. <c>EnforcePropUnitCohesion</c>
        /// groups <c>List&lt;MeshRenderer&gt;</c> and reads claims out of <c>seg.Renderers</c>
        /// only. A <c>ParticleSystemRenderer</c> is not a MeshRenderer and mounted dressing is not
        /// <c>seg.Renderers</c>, so a unit split across those two lists is invisible to it — by
        /// construction, in every build it has ever shipped.</para>
        ///
        /// <para>THE RULE, and it is structural rather than numeric: a mounted candidate whose
        /// prop unit is already owned by a segment attaches to THAT segment, whatever the nearest
        /// -wall search says. Hierarchy beats distance here because it is the stronger statement —
        /// the candidate is literally a child of the same prop root as that wall's own renderers,
        /// which is why <c>StandingFloorUnitRootOf</c> (the ModBuild-167 walk, stopping at any
        /// segment anchor or <c>ProceduralWall</c>) answers <c>CA_ICY_WallLight</c> for the torch
        /// emitters and for the ice meshes alike. Every other test in the sweep still runs: a
        /// figure, a mobile prop, an arch-protected piece and a water feature are refused exactly
        /// as before, and a host that cannot carry dressing is not eligible to be the home.</para>
        ///
        /// <para>MULTIPLAYER: local and deterministic. The map is derived from scene hierarchy and
        /// the local segment table; the majority vote is broken by the segment anchor's name
        /// compared ordinally, so two peers cannot pick different homes for the same unit.</para>
        /// </summary>
        private readonly Dictionary<Transform, Segment> _mountedUnitHome = new(64);
        private readonly Dictionary<Transform, int> _mountedUnitHomeVotes = new(64);
        private int _censusMountedUnitHome;

        /// <summary>
        /// THE LEFTOVER AUDIT (ModBuild 257) — the one list in this file that is NOT the
        /// ledger's opinion of itself.
        ///
        /// <para>The lesson this subsystem has paid for twice is that a falsifier reading the
        /// DRIVER agrees with every broken build: the DISSOLVE CENSUS printed "0 still
        /// ENABLED-ONLY … nothing pops" for every fade in the ModBuild 256 log while the user
        /// was photographing a lit flame hanging in the air. It could not have said anything
        /// else — it walks <c>seg.Mounted</c>, and a leftover is by definition a renderer that
        /// is NOT in <c>seg.Mounted</c>.</para>
        ///
        /// <para>This list is built from the other side: every airborne renderer standing next
        /// to a segment that is at FULL fade, that we did not adopt, and that
        /// <see cref="IsActuallyDrawing"/> confirms is putting pixels on the screen RIGHT NOW —
        /// <c>enabled</c> + <c>activeInHierarchy</c>, and for a particle system a live
        /// <c>particleCount</c>, read off the renderer and the emitter, not off a record. It
        /// carries its own capacity so the reject list's flood of already-owned foliage (24 of
        /// 24 slots in the ModBuild 256 log, ~18 of them "already the foliage of 'Wall 1'"
        /// entries that cannot float by construction) can no longer starve it.</para>
        /// </summary>
        private readonly List<string> _mountedLeftovers = new();

        /// <summary>How many leftovers one line NAMES. Raised from 12 to 40 in ModBuild 258
        /// because the ModBuild-257 log printed
        /// <c>LEFTOVER OVER A FADED WALL: 22 renderer(s) are actually drawing …</c> and then
        /// named twelve and said "(10 more)" — a list that truncates is a list that cannot be
        /// used to prove a class is closed. 40 is the same budget the STANDING PROP near-miss
        /// list settled on for the same reason.</summary>
        private const int MountedLeftoverCap = 40;
        private int _censusMountedLeftover;
        private int _lastLoggedMountedLeftover = -1;

        /// <summary>How many of the leftovers are ParticleSystemRenderers, and how many particle
        /// candidates were skipped for being already carried.
        ///
        /// <para>WHY THE BREAKDOWN IS ON THE LINE. The ModBuild-257 log's LEFTOVER list contains
        /// no <c>[particles]</c> entry at all, and there are two completely different reasons that
        /// could be true: the audit cannot see particle systems, or the scene's particle systems
        /// were all adopted. It is the second — <c>p_fire_torch (8)</c> and its
        /// <c>FireTorchSparks_Blue_MAT</c> emitter ARE owned, as <c>mounted dressing of 'Wall 1'</c>,
        /// while the ice meshes of their own prop root <c>CA_ICY_WallLight</c> ride 'Wall 4' — so
        /// they leave the sweep silently at the already-owned skip and never reach a reject at
        /// all. A count of 0 that cannot distinguish "none" from "invisible to me" is worth
        /// nothing, so the line now states both numbers.</para></summary>
        private int _censusMountedLeftoverParticles;
        private int _censusMountedCarriedParticles;

        /// <summary>Candidates that left the sweep at the very first skip — already in
        /// <c>_mountedOwned</c>, i.e. adopted by some wall this rescan (sticky, a unit handover,
        /// or an earlier segment). That skip is silent by design and produces no reject, no
        /// leftover entry and, until ModBuild 258, no number at all — which is why the flame in
        /// <c>wandproblem3.jpg</c> could be adopted by the wrong wall and appear in nothing.</summary>
        private int _censusMountedAdopted;
        /// <summary>Candidates skipped because another attachment of a FADING segment already
        /// owns them — counted rather than listed, because they cannot float (see the skip
        /// site). This count is what the near-miss list used to be spending itself on.</summary>
        private int _censusMountedCarried;
        /// <summary>The nearest FULLY FADED segment to the candidate currently under test, or
        /// null. Set by the owner search (and by <see cref="NoteStructuralSkip"/>, which does
        /// its own segment walk) and consumed by <see cref="NoteMountedReject"/>.</summary>
        private Segment? _leftoverFadedNear;
        private float _leftoverFadedGap;

        /// <summary>Releases logged this rescan/frame as happening ABOVE a wall that is still
        /// faded — the exact defect shape, capped for log hygiene.</summary>
        private int _releaseOverFadedWarns;
        private const int ReleaseOverFadedWarnCap = 6;

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
        /// modules restored from the snapshot, renderer visible again. Callers outside this
        /// file keep the unattributed form; see the overload for why the reason matters.</summary>
        private void RestoreProp(MountedProp p) => RestoreProp(p, null, "unattributed");

        /// <summary>
        /// The same restore, with the OWNER and the REASON it is happening.
        ///
        /// <para>WHY THE REASON EXISTS (ModBuild 257). ModBuild 256's log carries 61
        /// <c>OWNERSHIP CHURN</c> warnings, 59 of which contain the transition "released", and
        /// that reads as a broken sticky guard until the line numbers are counted: they arrive
        /// in exactly two contiguous blocks, 2088–2114 (27 lines, immediately after
        /// <c>fade OFF 'Wall 3'</c> at 2078 — and 'Wall 3' owned exactly 27 mounted props) and
        /// 2198–2228 (31 lines, immediately after <c>fade OFF 'Wall 1'</c> at 2188 — 31 props).
        /// Every one of them is <see cref="RestoreSegmentMounted"/> on the UN-FADE edge, one
        /// call per prop, which is the system working. A wall that fades twice inside the
        /// churn tripwire's 60 s window makes every prop it owns cross the 2-transition bar;
        /// the tripwire was counting the player walking around.</para>
        ///
        /// <para>So the transition is no longer reported as a bare "released": it names its
        /// cause, and the tripwire can tell an unfade from an ownership loss. And the one
        /// release shape that IS the reported defect — letting go of a prop while its wall is
        /// still gone, which leaves it lit in mid-air — now WARNS with the wall's fade at that
        /// instant instead of being indistinguishable from the other 59.</para>
        /// </summary>
        private void RestoreProp(MountedProp p, Segment? owner, string reason)
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
            NoteOwnershipChange(r, "released(" + reason + ")"); // churn tripwire (round 11)
            NoteReleaseOverFadedWall(r, owner, reason);
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
            // The wall is solid again on every ApplyMounted path that reaches here (want == 0
            // ⇒ seg.Fade <= 0); the other callers are drops and teardowns, which also end with
            // the wall visible. Naming it separates this — 59 of ModBuild 256's 61 churn
            // warnings — from a real ownership loss.
            string why = seg.Fade > 0f ? "segment dropped mid-fade" : "wall solid again";
            foreach (MountedProp p in seg.Mounted)
                RestoreProp(p, seg, why);
        }

        /// <summary>
        /// A prop let go while its wall is STILL GONE is the reported defect ("die blaue Flamme
        /// faded nicht mit, daher schwebt sie da in der Luft"), because
        /// <see cref="RestoreProp"/> ends by re-enabling the renderer. Print the wall's fade at
        /// that instant and the term that failed, so the next log decides it without another
        /// hardware round. Silent — as it must be — on the un-fade edge, where the wall is
        /// already solid and re-enabling the prop is the whole point.
        /// </summary>
        private void NoteReleaseOverFadedWall(Renderer r, Segment? owner, string reason)
        {
            if (owner == null || owner.Fade <= 0f
                || _releaseOverFadedWarns >= ReleaseOverFadedWarnCap)
                return;
            _releaseOverFadedWarns++;
            string wall = owner.Anchor != null ? owner.Anchor.name : "<dead>";
            VRLog.Warn(Name,
                $"RELEASED OVER A FADED WALL: '{r.name}' [{RendererKind(r)}] let go by '{wall}' "
                + $"while that wall's fade is {owner.Fade:F2} (mounted state "
                + $"{owner.MountedState}) — term that failed: {reason}. The restore re-enables "
                + "the renderer, so unless another owner hides it THIS is a lit prop hanging in "
                + "mid-air over a wall that is not there (user report 2026-08-24, "
                + "wand_problem2.jpg). A release above a faded wall is never correct except for "
                + "a FIGURE or a MOBILE prop, which are named as such in the reason.");
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
                RestoreProp(p, null, "teardown / wall fade disabled");
            _mountedScratch.Clear();
            _mountedTouched.Clear();
            // The mobility ledger is scene state, not fade state: a teardown / scene change
            // invalidates every baseline in it, and a latched verdict must not survive into a
            // scenario where the same Renderer id belongs to something else.
            _mountedAnchorLedger.Clear();
            _mountedMobile.Clear();
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
        /// PERF S2: the cheap half of <see cref="NoteStructuralSkip"/>'s own first guard,
        /// exposed so the two hot call sites can decide whether to BUILD the reason string at
        /// all. The reject list is capped at <see cref="MountedRejectCap"/> = 24 entries, and
        /// the sweep it guards used to run over every renderer in the scene — so the old shape
        /// formatted an interpolated string (one of them with a <c>GetType().Name</c>
        /// reflection call) thousands of times per rescan and discarded all but two dozen.
        /// This changes nothing about WHICH skips are recorded: when this is false
        /// NoteStructuralSkip returns without recording anything anyway.
        /// </summary>
        private bool StructuralSkipArmed =>
            _mountedRejects.Count < MountedRejectCap && !float.IsInfinity(_mountedAirborneBar);

        /// <summary>
        /// Log a renderer that left the sweep BEFORE any geometric test (wrong renderer family,
        /// already owned by another attachment list, fade-capable) — but only when it is airborne
        /// and near a wall, i.e. only when it could actually be a floating leftover. Bounded by
        /// the reject-list cap, which is checked first so the common case is one int compare;
        /// <see cref="StructuralSkipArmed"/> is that same cap, hoisted so a caller can skip
        /// building the reason string when it is already full.
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
            _leftoverFadedNear = null;
            _leftoverFadedGap = float.PositiveInfinity;
            foreach (Segment seg in _segments.Values)
            {
                if (!seg.HasBounds)
                    continue;
                float gap = HorizontalGap(seg.Bounds, b);
                if (gap < nearest)
                    nearest = gap;
                if (seg.Fade >= FoliageHideFade && gap < _leftoverFadedGap)
                {
                    _leftoverFadedGap = gap;
                    _leftoverFadedNear = seg;
                }
            }
            NoteMountedReject(c, anchorY, nearest, why);
            _leftoverFadedNear = null;
        }

        /// <summary>
        /// Has this prop MOVED relative to the wall it would ride? See
        /// <see cref="MountedAnchorDriftWU"/> for the measurement that produced this test and
        /// for why the drift is taken in the wall anchor's own frame. Records the current
        /// position either way, so the next rescan has a baseline; a first sighting, a change
        /// of owner and an anchorless segment all read NOT mobile (fail-open).
        /// </summary>
        private bool IsMobileProp(Renderer c, Segment owner, out float drift)
        {
            drift = float.NaN; // "already latched" — see DriftText
            if (_mountedMobile.Contains(c))
                return true;
            drift = 0f;
            if (owner.Anchor == null)
                return false;
            Transform frame = owner.Anchor.transform;
            Vector3 local = frame.InverseTransformPoint(c.transform.position);
            bool mobile = false;
            if (_mountedAnchorLedger.TryGetValue(c, out PropAnchor prev)
                && ReferenceEquals(prev.Owner, owner))
            {
                // Local units × the frame's own scale = world units, so the bar stays a real
                // distance even while the player is zooming the diorama.
                float scale = Mathf.Abs(frame.lossyScale.x);
                drift = Vector3.Distance(local, prev.Local) * (scale > 0f ? scale : 1f);
                mobile = drift > MountedAnchorDriftWU;
            }
            if (_mountedAnchorLedger.Count >= MountedAnchorLedgerCap
                && !_mountedAnchorLedger.ContainsKey(c))
                _mountedAnchorLedger.Clear(); // bounded scratch — worst case a fresh baseline
            _mountedAnchorLedger[c] = new PropAnchor(owner, local);
            if (!mobile)
                return false;
            if (_mountedMobile.Count >= MountedAnchorLedgerCap)
                _mountedMobile.Clear(); // bounded — a still-mobile prop re-earns its verdict
            _mountedMobile.Add(c);
            _censusMountedMobile++;
            if (_mountedMobileWarns.Count < MountedMobileWarnCap)
            {
                string wall = owner.Anchor != null ? owner.Anchor.name : "<dead>";
                _mountedMobileWarns.Add(
                    $"'{c.name}'[{RendererKind(c)}] {DriftText(drift)} against '{wall}'");
            }
            return true;
        }

        /// <summary>A drift reading for a log line: NaN means the verdict was latched by an
        /// earlier measurement rather than taken now.</summary>
        private static string DriftText(float drift) =>
            float.IsNaN(drift) ? "already latched as mobile" : $"moved {drift:F2} wu";

        /// <summary>
        /// Is this renderer putting pixels on the screen RIGHT NOW? Read off the renderer and
        /// (for particles) off the emitter — never off our own ledger, which is the mistake
        /// ModBuild 252's animation instrument made and which the DISSOLVE CENSUS still makes
        /// when it reports "nothing pops" for a wall that has a lit flame floating over it.
        /// <c>isVisible</c> is deliberately NOT consulted: it is per-camera and false for a
        /// perfectly drawn object the frame a cull test has not run for.
        /// </summary>
        private static bool IsActuallyDrawing(Renderer r)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                return false;
            if (r is ParticleSystemRenderer)
            {
                ParticleSystem? ps = r.GetComponent<ParticleSystem>();
                if (ps != null)
                    return ps.particleCount > 0; // an emitter with no live particles draws nothing
            }
            return true;
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
        /// <remarks>PERF S2: the input is the rescan cycle's RendererFact census (see
        /// <c>WallSegmentFade.cs</c>), not a fresh scene sweep.</remarks>
        private void CollectWallMountedProps()
        {
            _mountedOwned.Clear();
            _attachmentOwned.Clear();
            _mountedCensus.Clear();
            _mountedRejects.Clear();
            _mountedLeftovers.Clear();
            _mountedMobileWarns.Clear();
            _mountedSaturated.Clear();
            _censusMounted = 0;
            _censusMountedRejected = 0;
            _censusMountedLeftover = 0;
            _censusMountedMobile = 0;
            _censusMountedCarried = 0;
            _censusMountedCarriedParticles = 0;
            _censusMountedLeftoverParticles = 0;
            _censusMountedAdopted = 0;
            _censusMountedUnitHome = 0;
            _releaseOverFadedWarns = 0;
            _leftoverFadedNear = null;
            _mountedReleased.Clear();

            // STACKED SHELL pieces (adopted by the pass right before this one) are spoken for
            // FIRST: they must never be double-claimed by a sticky mounted list, the sweep
            // below, or the orphan guard (which restores any ledger entry missing from
            // _mountedOwned — a stacked piece IS in the shared ledger while ramped/hidden).
            //
            // ONE EXCEPTION, ModBuild 257 — AN OWNER THAT CANNOT FADE IS NOT AN OWNER.
            // Conceding is refused when the doorway is demonstrably doing nothing with the piece
            // — solid, and holding neither a stacked nor a body state — so no write war is
            // possible: ApplyStacked/ApplyBody on such a segment take the want == 0 branch, and
            // RestoreSegment{Stacked,Body} return immediately on state 0. The orphan guard cannot
            // mis-release it either: a segment that has never hidden anything has no entry in the
            // shared ledger.
            //
            // WHAT ModBuild 258 MEASURED ABOUT THIS PREDICATE, and it is a correction. The skip
            // was written for 'Glow', which in the ModBuild 256 AND 257 logs still flaps
            //   mounted:'Wall 3' → released(wall solid again)
            //     → stacked:'ThickDoor : (1a01…)' → mounted:'Wall 3'
            // three times in 60s. The false term is `seg.DoorRoot != null`, and it is false BY
            // CONSTRUCTION for the Stacked loop below: StackEligible (WallSegmentFade.Stacked.cs)
            // requires `seg.DoorRoot == null`, so a segment can never hold a stacked piece AND
            // satisfy this skip. The skip is unreachable on `seg.Stacked` and can only ever fire
            // on `seg.Body`. It is left standing for the Body case and deliberately not widened.
            //
            // AND THE SEGMENT IS NOT A DOORWAY. The ModBuild-257 log prints
            //   GATE COLUMN 'ThickDoor : (1a010af9…)': arch rect x[-2.6..-0.8] z[1.9..4.1] topY 4.7
            // — 'ThickDoor : (1a01…)' names TWO segments with the same GameObject name: the
            // permanently-solid arch segment (keyed on the door root Transform, DoorRoot stamped
            // at WallSegmentFade.cs's adoption sweep) and the GATE COLUMN (keyed on the
            // UnityGameEditorDoorProp, created in WallSegmentFade.Gate.cs, DoorRoot never stamped
            // because the adoption sweep's gate-column branch `continue`s before that line). The
            // one holding 'Glow' is the gate column, and a gate column FADES LIKE ANY WALL ("only
            // the arch rect stays solid", user ruling 2026-08-07). So the ModBuild-257 diagnosis
            // — "an owner that never fades" — was wrong about this piece: both owners can fade,
            // they simply disagree about when. That is a one-prop-two-owners problem, and the
            // ModBuild-258 unit-affinity rule (see _mountedUnitHome) is what settles it.
            foreach (Segment seg in _segments.Values)
            {
                bool inertDoorway = seg.DoorRoot != null && seg.Fade <= 0f
                    && seg.StackedState == 0 && seg.BodyState == 0;
                foreach (MountedProp p in seg.Stacked)
                {
                    if (p.Renderer == null || inertDoorway)
                        continue;
                    _mountedOwned.Add(p.Renderer);
                    _attachmentOwned[p.Renderer] = new OwnerRef(seg, "stacked shell piece");
                }
                foreach (MountedProp p in seg.Body)
                {
                    if (p.Renderer == null || inertDoorway)
                        continue;
                    _mountedOwned.Add(p.Renderer);
                    _attachmentOwned[p.Renderer] = new OwnerRef(seg, "wall body mesh");
                }
            }
            // Shared corner pieces (round 7) are spoken for too — never sconce dressing,
            // and the orphan guard must not release them while their neighbors are faded.
            RegisterCornerOwnership();

            // UNIT AFFINITY (ModBuild 258) — built HERE, from the final segment table, because
            // the prop-unit pass has already run (CommitPhase.PropUnits precedes .Mounted) and
            // seg.Renderers is therefore the authoritative "who owns this unit's wall half".
            BuildMountedUnitHomes();

            // PARK THE PREVIOUS LISTS FIRST, IN A LOOP OF THEIR OWN. This used to share the sticky
            // loop below, and it cannot any more: the ModBuild-258 handover writes into ANOTHER
            // segment's Mounted list, and a segment that had not been reached yet would clear the
            // handover back out again on its own iteration. Two loops, so every list is empty
            // before anything is put in one.
            foreach (Segment seg in _segments.Values)
            {
                seg.PrevMounted.Clear();
                seg.PrevMounted.AddRange(seg.Mounted);
                seg.Mounted.Clear();
            }

            // STICKY OWNERSHIP: a segment that is mid-fade or held faded keeps every prop it
            // already owns — releasing one while its wall is gone is exactly the blink the first
            // hardware round produced.
            foreach (Segment seg in _segments.Values)
            {
                bool sticky = seg.MountedState != 0 || seg.Fade > 0f;
                if (sticky)
                {
                    foreach (MountedProp p in seg.PrevMounted)
                    {
                        // The claim comes FIRST and stays first: a renderer another list has
                        // already spoken for (a stacked shell piece, a body mesh, a corner, or
                        // an earlier segment's sticky carry) is that list's to drive, and
                        // restoring it here would be a write war with its applier. The two
                        // releases below therefore only ever touch a prop THIS segment owns.
                        if (p.Renderer == null || !_mountedOwned.Add(p.Renderer))
                            continue;
                        // Figures are NEVER carried, sticky or not (round-7 ruling).
                        if (IsFigureOrActorRenderer(p.Renderer))
                        {
                            RestoreProp(p, seg, "FIGURE — never carried (round-7 ruling)");
                            _mountedReleased.Add(p);
                            continue;
                        }
                        // MODBUILD 257: nor is anything that has MOVED relative to this wall.
                        // Sticky exists because a particle system's live AABB drifts every
                        // frame and made the candles blink — it was never meant to hold a
                        // character's spell VFX hidden after the character walked away, which
                        // is what it does today for the Elementalist's five emitters (see
                        // MountedAnchorDriftWU). The release is deliberate and is reported by
                        // NoteReleaseOverFadedWall with the wall's live fade, so if this ever
                        // takes a real sconce the next log says so in one line.
                        if (IsMobileProp(p.Renderer, seg, out float drift))
                        {
                            RestoreProp(p, seg,
                                $"MOBILE — {DriftText(drift)} against this wall, so it is not "
                                + "dressing bolted to it");
                            _mountedReleased.Add(p);
                            continue;
                        }
                        // UNIT AFFINITY (ModBuild 258) — sticky ownership must not outlive being
                        // WRONG. The blue torch of 'CA_ICY_WallLight' is held sticky by 'Wall 1'
                        // while the ice meshes of its own prop root ride 'Wall 4'; sticky then
                        // re-asserts that split on every rescan, so without this the sweep below
                        // never gets to look at the piece at all (it is already in _mountedOwned).
                        // A HANDOVER, not a release: the new owner's ApplyMounted drives it on the
                        // same frame, so nothing blinks. See _mountedUnitHome.
                        Segment? stickyHome = MountedUnitHomeOf(p.Renderer);
                        if (stickyHome != null && !ReferenceEquals(stickyHome, seg))
                        {
                            stickyHome.Mounted.Add(p);
                            _mountedReleased.Add(p); // the leavers loop must not undo the handover
                            _censusMounted++;
                            _censusMountedUnitHome++;
                            NoteOwnershipChange(p.Renderer,
                                $"mounted:'{stickyHome.Anchor!.name}'(prop unit)");
                            continue;
                        }
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

            // PERF S2 — THE TWO HOISTED PREFILTERS, and why each rejects only what the pass
            // below already rejected. Until 2026-08-23 this loop ran its full body for every
            // one of the scene's 8630 renderers: an interop name allocation (IsModObject), a
            // GetSharedMaterials, a live bounds read, an arch-rect scan, a water-rect scan and
            // — for anything airborne — a walk of every tracked segment, EAGERLY formatting an
            // interpolated reject string per skip. It was a large share of the 118 ms rescan.
            //
            //  (1) THE FLOOR GATE. The body's own cheap bulk filter drops anything whose anchor
            //      sits below `minFloorY + 0.25·MountedClearanceWU`, silently. Everything that
            //      ran BEFORE that filter is either a pure predicate (IsModObject, the shader
            //      test, IsArchProtected, IsWaterProtected) or NoteStructuralSkip — and
            //      NoteStructuralSkip's own first act is to return unless the anchor clears
            //      `_mountedAirborneBar = minFloorY + MountedClearanceWU`, a bar FOUR TIMES
            //      higher. So for anything the floor gate rejects, the whole prologue was
            //      already a no-op with no side effect. Hoisting it changes nothing but cost.
            //
            //  (2) THE UNION REACH RECT. Every outcome of this loop needs the renderer to be
            //      near SOME bounded segment: adoption needs an XZ gap within `linkMax`
            //      (≤ MountedTinyFxLinkMaxXZ = 1.8 wu), and even the near-miss DIAGNOSTIC needs
            //      one within MountedNearMissXZ (2.5 wu) — NoteMountedReject returns outright
            //      past that, so a far renderer produced no adoption, no reject line and no
            //      counter. A candidate outside the union of every bounded segment's XZ rect
            //      grown by 2.5 wu therefore fails every individual segment too (the same
            //      necessary-condition argument the fast-reclaim sweep's union prefilter
            //      already makes). With no bounded segment at all the union is empty and the
            //      whole sweep was a no-op, so it is skipped outright.
            //
            // Both prefilters read CENSUS bounds and are widened by CensusBoundsSlackWU, so a
            // cached AABB can only ever reject what the live test would also reject; every
            // survivor is re-measured against its LIVE bounds below, exactly as before.
            float reachMinX = float.PositiveInfinity, reachMaxX = float.NegativeInfinity;
            float reachMinZ = float.PositiveInfinity, reachMaxZ = float.NegativeInfinity;
            foreach (Segment seg in _segments.Values)
            {
                if (!seg.HasBounds)
                    continue;
                if (seg.Bounds.min.x < reachMinX) reachMinX = seg.Bounds.min.x;
                if (seg.Bounds.max.x > reachMaxX) reachMaxX = seg.Bounds.max.x;
                if (seg.Bounds.min.z < reachMinZ) reachMinZ = seg.Bounds.min.z;
                if (seg.Bounds.max.z > reachMaxZ) reachMaxZ = seg.Bounds.max.z;
            }
            float reach = MountedNearMissXZ + CensusBoundsSlackWU;
            reachMinX -= reach; reachMaxX += reach;
            reachMinZ -= reach; reachMaxZ += reach;
            float floorGate = minFloorY + MountedClearanceWU * 0.25f - CensusBoundsSlackWU;

            if (!float.IsInfinity(minFloorY) && !float.IsInfinity(reachMinX))
            {
                float airborneBar = _mountedAirborneBar;
                for (int fi = 0; fi < _factCount; fi++)
                {
                    ref RendererFact f = ref _facts[fi];
                    if (f.R == null || f.Mod)
                        continue; // dead, or a mod-owned visual (hands, cards, panels, MR
                                  // backing — by layer OR 'GloomhavenVR.' name prefix): never
                                  // scenery, never a candidate, never in the diagnostics
                    if (f.Anchor.y < floorGate)
                        continue; // prefilter (1) — see above
                    // Prefilter (2). The gap tests downstream are AABB-to-AABB
                    // (HorizontalGap(seg.Bounds, b)), so the rect test has to be against the
                    // renderer's EXTENTS, never its centre — a banner whose centre sits outside
                    // the reach but whose end reaches into it must survive. A particle system is
                    // additionally gap-measured by its EMITTER POSITION in the adoption loop
                    // (round 13), so it survives if EITHER probe is in reach: the prefilter must
                    // never be narrower than the UNION of the tests it stands in front of.
                    bool inReach = f.Bounds.max.x >= reachMinX && f.Bounds.min.x <= reachMaxX
                        && f.Bounds.max.z >= reachMinZ && f.Bounds.min.z <= reachMaxZ;
                    if (!inReach && f.Particles)
                        inReach = f.Anchor.x >= reachMinX && f.Anchor.x <= reachMaxX
                            && f.Anchor.z >= reachMinZ && f.Anchor.z <= reachMaxZ;
                    if (!inReach)
                        continue;
                    Renderer c = f.R!;
                    if (_mountedOwned.Contains(c))
                    {
                        // Already attached this rescan (sticky, a handover, or earlier in the
                        // sweep). THIS is where the ModBuild-257 log's blue flame left: it was
                        // adopted, by the wrong wall, and an adopted renderer produces no reject
                        // and therefore no LEFTOVER entry. Counted since ModBuild 258 so the line
                        // can say "no particle leftover" and mean it.
                        if (c is ParticleSystemRenderer)
                            _censusMountedCarriedParticles++;
                        _censusMountedAdopted++;
                        continue;
                    }
                    // STRUCTURAL SKIPS — the three ways a renderer leaves this sweep before any
                    // geometric test runs. Each is LOGGED when it stands near a wall (round 3: a
                    // banner's wooden bar survived a fade and appeared in no reject list at all,
                    // because it left here silently). PERF S2: the reject STRING is now built
                    // only when the reject list can still take one (StructuralSkipArmed) —
                    // formatting 8630 interpolated strings per rescan to throw all but 24 away
                    // was pure waste, and `c.GetType().Name` is a reflection call on top.
                    if (!f.Mountable)
                    {
                        if (StructuralSkipArmed)
                            NoteStructuralSkip(c, $"renderer type {c.GetType().Name} is not scenery");
                        continue;
                    }
                    if (_attachmentOwned.TryGetValue(c, out OwnerRef owner))
                    {
                        // MODBUILD 257 — THE NEAR-MISS LIST WAS BLIND BECAUSE THIS LINE FLOODED
                        // IT. In the ModBuild 256 log the list is full (24 of 24, 28 near-misses
                        // total) and about eighteen of those entries read "already the foliage
                        // of 'Wall 1' (that wall's fade 0.00)" — several of them the same NAME
                        // three times over, i.e. sibling instances of the same grass tuft. Not
                        // one of them could ever be the thing the list exists to find: a piece
                        // owned by a segment that FADES is carried by that segment's own
                        // applier, so when the wall goes the piece goes. Meanwhile the airborne
                        // candidates that actually could float never got a slot.
                        //
                        // So an owner that can carry it costs no slot and is only counted. What
                        // is still reported in full is the case that CANNOT carry it: a doorway
                        // (never fades — user ruling 2026-08-02, and this is exactly how 'Glow'
                        // came to be held by 'ThickDoor : (1a01…)' while 'Wall 3' faded around
                        // it) or a dead anchor.
                        bool ownerCanCarryIt = owner.Seg.DoorRoot == null && owner.Seg.Anchor != null;
                        if (ownerCanCarryIt)
                        {
                            _censusMountedCarried++;
                            if (c is ParticleSystemRenderer)
                                _censusMountedCarriedParticles++;
                            continue;
                        }
                        if (StructuralSkipArmed)
                        {
                            string wall = owner.Seg.Anchor != null ? owner.Seg.Anchor.name : "<dead>";
                            NoteStructuralSkip(c,
                                $"already the {owner.Kind} of '{wall}' (that wall's fade "
                                + $"{owner.Seg.Fade:F2}) — an owner that NEVER FADES, so nothing "
                                + "will ever hide this piece with the wall it hugs");
                        }
                        continue;
                    }
                    if (f.WallFadeShader && f.Mesh != null)
                    {
                        MeshRenderer mr = f.Mesh!;
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
                    // ModBuild 257: the runaway cap used to be one more silent `continue` in
                    // this loop, so a wall that had filled up looked exactly like a wall that
                    // was out of reach. Remember whether the cap is what turned the candidate
                    // away, and name the segment it turned it away from.
                    Segment? cappedBy = null;
                    // The nearest FULLY FADED segment, for the leftover audit. Deliberately
                    // NOT the same search as `best`: the question this one answers is "is this
                    // thing standing next to a hole in the world", which does not care whether
                    // the segment was eligible to own it.
                    _leftoverFadedNear = null;
                    _leftoverFadedGap = float.PositiveInfinity;
                    foreach (Segment seg in _segments.Values)
                    {
                        if (!seg.HasBounds)
                            continue;
                        float gapAny = particles
                            ? HorizontalGap(seg.Bounds, c.transform.position)
                            : HorizontalGap(seg.Bounds, b);
                        if (seg.Fade >= FoliageHideFade && gapAny < _leftoverFadedGap)
                        {
                            _leftoverFadedGap = gapAny;
                            _leftoverFadedNear = seg;
                        }
                        if (seg.DoorRoot != null || !RoomDecisionValid(seg.RoomIndex))
                            continue;
                        float gap = gapAny;
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
                        {
                            cappedBy = seg;
                            NoteMountedSaturated(seg);
                            continue;
                        }
                        bestGap = gap;
                        best = seg;
                    }
                    // UNIT AFFINITY (ModBuild 258) — HIERARCHY OVERRULES THE NEAREST-WALL SEARCH.
                    // The search above is a distance test between AABBs and it put the icy wall
                    // light's torch emitters on 'Wall 1' while the ice meshes of the SAME prop
                    // root rode 'Wall 4' (see _mountedUnitHome for the log lines). Being a child
                    // of the same prop root as a wall's own renderers is a stronger statement than
                    // being 0.2 wu nearer to a different wall's box, so it wins — including when
                    // the geometric search found nothing at all, which is the case where a piece
                    // of a fading prop would otherwise be left standing. The floor gate
                    // (`belowBar`) is NOT overruled: a candidate that reads as floor-supported
                    // cannot float and has nothing to be rescued from.
                    bool byUnitHome = false;
                    if (!belowBar)
                    {
                        Segment? home = MountedUnitHomeOf(c);
                        if (home != null && !ReferenceEquals(home, best))
                        {
                            best = home;
                            bestGap = particles
                                ? HorizontalGap(home.Bounds, c.transform.position)
                                : HorizontalGap(home.Bounds, b);
                            // Counted at the ADOPTION, not here: this candidate can still be
                            // refused below as a figure, a mobile prop or architecture, and a
                            // census that counts intentions is the failure this subsystem has
                            // paid for twice.
                            byUnitHome = true;
                        }
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
                        NoteMountedReject(c, anchorY, nearestAny, cappedBy != null
                            ? $"'{(cappedBy.Anchor != null ? cappedBy.Anchor.name : "<dead>")}' "
                              + $"is SATURATED at {MountedMaxPerSegment} mounted props and no "
                              + "other wall is in reach — this candidate is lost to the runaway "
                              + "cap, not to geometry"
                            : "no wall within reach / outside its span");
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
                    if (HasGameLogicAncestry(c)   // PERF S3: memoised TileBehaviour+Canvas pair
                        || c.GetComponent<TMPro.TMP_Text>() != null)
                    {
                        NoteMountedReject(c, anchorY, bestGap, "game logic / worldspace UI");
                        continue;
                    }
                    // A renderer the GAME disabled is not ours to manage — except one WE hold
                    // hidden (dropping it now would re-enable + re-hide it in a one-frame flash).
                    if (!c.enabled && !_mountedTouched.ContainsKey(c))
                        continue;
                    // MOBILE (ModBuild 257) — the LAST test before adoption, so a prop rejected
                    // for any cheaper reason never pays for it and never enters the anchor
                    // ledger. The ancestry test above answers "is this renderer part of a
                    // figure's rig", and the Elementalist's five world-rooted spell emitters
                    // answer NO to it perfectly correctly while being a figure's VFX in every
                    // sense the round-7 ruling cares about — they were adopted onto 'Wall 3'
                    // and later onto 'Wall 1' in one ModBuild 256 session. This one asks the
                    // question the geometry can actually settle: did it MOVE relative to the
                    // wall it wants to ride. See MountedAnchorDriftWU for the measurement, the
                    // world-grab argument and the falsifier.
                    if (IsMobileProp(c, best, out float candidateDrift))
                    {
                        NoteMountedReject(c, anchorY, bestGap,
                            $"MOBILE — {DriftText(candidateDrift)} against "
                            + $"'{(best.Anchor != null ? best.Anchor.name : "<dead>")}' since the "
                            + "last rescan, so it follows something rather than hanging on that "
                            + "wall (figure VFX / carried prop) — never wall dressing");
                        continue;
                    }
                    // Reuse the existing record when we already know this prop (keeps the authored
                    // snapshot — re-reading a material we are CURRENTLY ramping would snapshot our
                    // own ramp as the "authored" value).
                    if (!_mountedTouched.TryGetValue(c, out MountedProp? prop))
                        prop = ClassifyProp(c);
                    best.Mounted.Add(prop);
                    _mountedOwned.Add(c);
                    if (byUnitHome)
                        _censusMountedUnitHome++;
                    NoteOwnershipChange(c,
                        $"mounted:'{(best.Anchor != null ? best.Anchor.name : "?")}'"
                        + (byUnitHome ? "(prop unit)" : string.Empty));
                    _censusMounted++;
                    if (_mountedCensus.Count < MountedCensusCap)
                    {
                        string wall = best.Anchor != null ? best.Anchor.name : "<dead>";
                        _mountedCensus.Add(
                            $"'{c.name}'[{RendererKind(c)}→{prop.Tier}] anchor {anchorY:F1} "
                            + $"gap {bestGap:F2} → '{wall}'"
                            + (byUnitHome ? " [its PROP UNIT's wall, not the nearest]" : string.Empty));
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
                        if (prev.Renderer == null || seg.Mounted.Contains(prev)
                            || _mountedReleased.Contains(prev))
                            continue;
                        // The reason matters here more than anywhere: if this fires while the
                        // segment is still faded, the prop is about to be re-enabled over a
                        // wall that is not there. NoteReleaseOverFadedWall turns exactly that
                        // into a WARN naming the wall's live fade — the shape the ModBuild 256
                        // log could not distinguish from the 59 perfectly correct un-fade
                        // releases.
                        RestoreProp(prev, seg, _attachmentOwned.TryGetValue(
                                prev.Renderer, out OwnerRef newOwner)
                            ? $"lost the claim to the {newOwner.Kind} of "
                              + $"'{(newOwner.Seg.Anchor != null ? newOwner.Seg.Anchor.name : "<dead>")}' "
                              + $"(that owner's fade {newOwner.Seg.Fade:F2})"
                            : "no longer adopted by this wall (geometry test)");
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
                    RestoreProp(p, null, "orphan guard — no live segment owns it any more");
                _mountedScratch.Clear();
            }

            // Apparance streams the dressing in over several rescans, so the scenario's first
            // heartbeat would report a half-built table forever: re-log whenever the attached set
            // actually changed. Steady state prints nothing.
            if (_censusMounted != _lastLoggedMountedCount
                || _censusMountedRejected != _lastLoggedMountedRejected
                || _censusMountedLeftover != _lastLoggedMountedLeftover)
                LogMountedCensus();
            // The two alarms stand alone: a leftover is the reported defect, and a mobile prop
            // is the round-7 ruling being enforced against a class the ancestry test cannot see.
            LogMountedLeftovers();
            LogMountedMobile();
        }

        /// <summary>
        /// Map every prop-unit root a segment already drives to that segment — see
        /// <see cref="_mountedUnitHome"/> for the defect this exists for. The input is
        /// <c>seg.Renderers</c> and <c>seg.Foliage</c>, i.e. the two lists a wall's OWN geometry
        /// lands in; deliberately NOT the attachment lists, because a mounted/stacked claim is the
        /// very thing this map is here to arbitrate and letting it vote would make the rule a
        /// fixed point of whatever it did last rescan.
        ///
        /// <para>MAJORITY, then anchor name ordinally. A unit whose renderers ended up on two
        /// walls has already been through <c>EnforcePropUnitCohesion</c>, so a tie is rare; the
        /// tie-break is a stable string precisely so two peers cannot disagree.</para>
        /// </summary>
        private void BuildMountedUnitHomes()
        {
            _mountedUnitHome.Clear();
            _mountedUnitHomeVotes.Clear();
            foreach (Segment seg in _segments.Values)
            {
                if (!seg.HasBounds || seg.Anchor == null)
                    continue;
                foreach (MeshRenderer r in seg.Renderers)
                    VoteMountedUnitHome(r, seg);
                foreach (MeshRenderer f in seg.Foliage)
                    VoteMountedUnitHome(f, seg);
            }
        }

        private void VoteMountedUnitHome(Renderer? r, Segment seg)
        {
            if (r == null)
                return;
            Transform? root = StandingFloorUnitRootOf(r);
            if (root == null)
                return; // no prop unit — nothing for the affinity rule to be affine to
            if (!_mountedUnitHome.TryGetValue(root, out Segment? held))
            {
                _mountedUnitHome[root] = seg;
                _mountedUnitHomeVotes[root] = 1;
                return;
            }
            if (ReferenceEquals(held, seg))
            {
                _mountedUnitHomeVotes[root]++;
                return;
            }
            // A contested unit. Count this segment's share; the incumbent keeps the root until
            // something outvotes it, and an exact tie is broken on the anchor name so the answer
            // is identical on every machine.
            int mine = 0;
            foreach (MeshRenderer m in seg.Renderers)
            {
                if (m != null && ReferenceEquals(StandingFloorUnitRootOf(m), root))
                    mine++;
            }
            int theirs = _mountedUnitHomeVotes[root];
            bool takeover = mine > theirs
                || (mine == theirs && held.Anchor != null && seg.Anchor != null
                    && string.CompareOrdinal(seg.Anchor.name, held.Anchor.name) < 0);
            if (!takeover)
                return;
            _mountedUnitHome[root] = seg;
            _mountedUnitHomeVotes[root] = mine;
        }

        /// <summary>The segment that owns this renderer's prop unit, or null when the renderer has
        /// no unit, its unit is unowned, or the owner could not carry dressing anyway.</summary>
        private Segment? MountedUnitHomeOf(Renderer r)
        {
            if (_mountedUnitHome.Count == 0)
                return null;
            Transform? root = StandingFloorUnitRootOf(r);
            if (root == null || !_mountedUnitHome.TryGetValue(root, out Segment? home))
                return null;
            return MountedHostEligible(home) ? home : null;
        }

        /// <summary>May this segment be handed a prop by the unit-affinity rule? The same three
        /// facts the sweep's own search requires — a doorway never fades (user ruling 2026-08-02),
        /// a segment without a trusted room plane makes no decision to ride, and the runaway cap
        /// is a cap.</summary>
        private bool MountedHostEligible(Segment s) =>
            s.HasBounds && s.Anchor != null && s.DoorRoot == null
            && RoomDecisionValid(s.RoomIndex) && s.Mounted.Count < MountedMaxPerSegment;

        /// <summary>Record a segment that has filled its dressing quota (see
        /// <see cref="MountedMaxPerSegment"/>) — once per segment per rescan.</summary>
        private void NoteMountedSaturated(Segment seg)
        {
            string wall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
            foreach (string s in _mountedSaturated)
            {
                if (s == wall)
                    return;
            }
            if (_mountedSaturated.Count < 8)
                _mountedSaturated.Add(wall);
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
            // THE LEFTOVER AUDIT (see the field). This is the only place in the file that asks
            // the renderer itself what it is doing, and it only asks when the answer matters:
            // the candidate stands next to a wall that has gone to FULL fade, so if it is
            // drawing, the user is looking at exactly what he photographed.
            if (_leftoverFadedNear == null || !IsActuallyDrawing(c))
                return;
            _censusMountedLeftover++;
            // A ParticleSystemRenderer CAN reach this list and always could: it is a
            // MountedCandidate (see the type test), five of this sweep's rejects are not
            // particle-gated (the airborne bar, "no wall in reach", FIGURE, standing prop and
            // MOBILE), and IsActuallyDrawing reads particleCount off the emitter. Counted so a
            // zero here is evidence rather than an absence.
            if (c is ParticleSystemRenderer)
                _censusMountedLeftoverParticles++;
            if (_mountedLeftovers.Count >= MountedLeftoverCap)
                return;
            Segment faded = _leftoverFadedNear;
            string wall = faded.Anchor != null ? faded.Anchor.name : "<dead>";
            _mountedLeftovers.Add(
                $"'{c.name}'[{RendererKind(c)}] anchor {anchorY:F1} is DRAWING {_leftoverFadedGap:F2} wu "
                + $"from '{wall}' whose fade is {faded.Fade:F2} — not adopted because: {why}");
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
            _lastLoggedMountedLeftover = _censusMountedLeftover;
            if (_censusMounted == 0 && _censusMountedRejected == 0)
                return;
            string riding = _mountedCensus.Count > 0 ? string.Join("; ", _mountedCensus) : "none new";
            string misses = _mountedRejects.Count > 0
                ? " | NEAR-MISS (stays visible): " + string.Join("; ", _mountedRejects)
                : string.Empty;
            // The reject list is capped and, in the ModBuild 256 log, was capped OUT: 24 of 24
            // slots, ~18 of them "already the foliage of 'Wall 1'" — entries for renderers that
            // are owned, cannot float, and had crowded out every candidate that could. Saying
            // so on the line is the cheap half of the fix; the LEFTOVER line is the other half.
            string starved = _censusMountedRejected > _mountedRejects.Count
                ? $" [reject list CAPPED at {MountedRejectCap} — "
                  + $"{_censusMountedRejected - _mountedRejects.Count} near-miss(es) not shown]"
                : string.Empty;
            string full = _mountedSaturated.Count > 0
                ? $" | SATURATED at {MountedMaxPerSegment} props: "
                  + string.Join(", ", _mountedSaturated)
                  + " — these walls turned further candidates away because of the runaway cap, "
                  + "not because of geometry"
                : string.Empty;
            VRLog.Info(Name,
                $"WALL-MOUNTED DRESSING: {_censusMounted} prop(s) dissolve WITH their wall "
                + $"(airborne ≥{MountedClearanceWU:0.0} wu over the room floor — meshes by AABB, "
                + $"particles by EMITTER anchor; XZ gap ≤{MountedLinkMaxXZ:0.00} wu; ownership "
                + $"sticky while faded unless the prop MOVED ≥{MountedAnchorDriftWU:0.00} wu "
                + $"against it; Lights are NEVER written to): {riding}"
                + $"{misses}{starved} ({_censusMountedRejected} near-miss total, "
                + $"{_censusMountedCarried} skipped as already carried by a wall that fades, "
                + $"{_censusMountedAdopted} skipped for being ALREADY ADOPTED this rescan — of "
                + $"those two silent populations {_censusMountedCarriedParticles} are particle "
                + $"systems, which is where the blue flame of wandproblem3.jpg was hiding: "
                + $"adopted, by the wrong wall, and therefore in no reject and no leftover line, "
                + $"{_censusMountedLeftover} drawing over a fully faded wall; "
                + $"{_censusMountedUnitHome} attached to the wall that owns their PROP UNIT "
                + $"rather than to the nearest one — ModBuild 258, wandproblem3.jpg: one prop "
                + $"with two owners is one prop that half-survives every fade){full}.");
        }

        /// <summary>
        /// THE PICTURE, not the ledger (see <see cref="_mountedLeftovers"/>). One WARN per
        /// rescan that found an airborne renderer DRAWING next to a segment at full fade —
        /// which is the user's photograph, stated in the log, with the reason it was not
        /// adopted. A session in which this line never appears is the only evidence this file
        /// can offer that the "schwebende Flamme" class is actually closed; the DISSOLVE
        /// CENSUS's "nothing pops" cannot say it, because it only ever walks props we already
        /// own.
        /// </summary>
        private void LogMountedLeftovers()
        {
            if (_censusMountedLeftover == 0)
                return;
            VRLog.Warn(Name,
                $"LEFTOVER OVER A FADED WALL: {_censusMountedLeftover} renderer(s) are actually "
                + "drawing (renderer enabled + active in hierarchy; particle systems with live "
                + "particles) within "
                + $"{MountedNearMissXZ:0.0} wu of a wall whose fade is ≥{FoliageHideFade:0.00} "
                + $"— read off the renderer and the emitter, not off our ledger. "
                + $"{_censusMountedLeftoverParticles} of them are ParticleSystemRenderers, and "
                + $"that number is now stated because a list with no [particles] entry used to be "
                + $"unreadable: a particle system reaches this list through five of the sweep's "
                + $"rejects (airborne bar, no wall in reach, FIGURE, standing prop, MOBILE) and "
                + $"IsActuallyDrawing reads its live particleCount, so 0 here means none were "
                + $"left over — NOT that none could be. The other place a drawing particle system "
                + $"can be is ADOPTED BY THE WRONG WALL, which produces no reject at all and is "
                + $"counted on the WALL-MOUNTED DRESSING line instead "
                + $"({_censusMountedCarriedParticles} this rescan). Names (up to "
                + $"{MountedLeftoverCap}, raised from 12 in ModBuild 258 because the previous log "
                + $"named 12 of 22): "
                + string.Join("; ", _mountedLeftovers)
                + (_censusMountedLeftover > _mountedLeftovers.Count
                    ? $"; … ({_censusMountedLeftover - _mountedLeftovers.Count} more)"
                    : string.Empty)
                + ". This is the shape of the 2026-08-24 report (wandproblem3.jpg): the wall is "
                + "gone and the thing that hung on it is not.");
        }

        /// <summary>The mobility guard's own falsifier — every renderer it refused, with the
        /// drift that decided it. A genuine sconce in this list means
        /// <see cref="MountedAnchorDriftWU"/> is too low.</summary>
        private void LogMountedMobile()
        {
            if (_mountedMobileWarns.Count == 0)
                return;
            VRLog.Warn(Name,
                $"MOBILE PROP (not wall dressing): {_censusMountedMobile} renderer(s) moved more "
                + $"than {MountedAnchorDriftWU:0.00} wu against the wall they were riding, "
                + "measured in that wall anchor's own frame (so a world grab reads zero) — they "
                + "are released and never re-adopted: "
                + string.Join("; ", _mountedMobileWarns)
                + (_censusMountedMobile > _mountedMobileWarns.Count
                    ? $"; … ({_censusMountedMobile - _mountedMobileWarns.Count} more)"
                    : string.Empty)
                + ". ModBuild 256 adopted a player character's own spell VFX "
                + "(P_Elementalist_Chest / _Eye_L / _Eye_R / _Hand_L / _Hand_R) as dressing on "
                + "'Wall 3' and then on 'Wall 1'; the round-7 figure ruling forbids it and the "
                + "ancestry test cannot see it. Anything in this list that is REAL dressing "
                + "(a swinging lantern would be the honest candidate) falsifies the bar.");
        }
    }
}
