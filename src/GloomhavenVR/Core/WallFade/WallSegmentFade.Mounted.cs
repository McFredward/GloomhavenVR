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
    /// WHERE ONE PIECE IS IN ITS RETURN (ModBuild 265). The whole of the "a piece is not shown
    /// until it can be drawn as authored" rule needs to know two things the renderer cannot be
    /// asked: did WE hide this piece, and has it already come back as authored this episode.
    ///
    /// <para>WHY NOT <c>Renderer.enabled</c>. It answers the first question and not the second.
    /// A cutoff piece shown as authored is <c>enabled</c> from that frame on, so a rule reading
    /// only the renderer would hand it back to the ramp on the very next frame and re-write the
    /// clip value that discards it — the defect would last one frame less and be otherwise
    /// identical. It is also not proof that we hid it: Apparance re-enables regenerated pieces
    /// over a held wall (that is why no applier here has a held-state early-out), and such a
    /// piece must keep the ordinary ramp rather than be gated.</para>
    ///
    /// <para>Armed in every applier's <c>want == 2</c> branch, cleared in
    /// <see cref="FadeDriver.RestoreProp(MountedProp, Segment, string)"/> — i.e. exactly when the
    /// piece goes back to being untouched. Foliage records need no clear: <c>RestoreSegmentFoliage</c>
    /// destroys <c>seg.FoliageProps</c> at fade 0, so every episode starts with fresh records.</para>
    /// </summary>
    private enum ReturnPhase : byte
    {
        /// <summary>Not held by us — outbound, or a piece we never hid. The applier ramps it
        /// exactly as it always did. An outbound piece is normally in this state, but NOT
        /// always: a return interrupted by a new fade is outbound while still reading
        /// <see cref="ReturnedAuthored"/>, and <c>ShowAttachmentPiece</c> hands it back here the
        /// moment the fade climbs past the piece's own stagger threshold.</summary>
        Free = 0,
        /// <summary>We disabled it at the held threshold. It may only be turned on again with
        /// its authored look.</summary>
        HeldHidden = 1,
        /// <summary>It came back this episode carrying its authored values. The ramp may not
        /// touch it again until the next hide re-arms <see cref="HeldHidden"/>.</summary>
        ReturnedAuthored = 2,
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

        /// <summary>Was this piece DRAWING after the previous frame's applier ran? The edge
        /// <c>false → true</c> is the frame the piece becomes visible again, which is the only
        /// frame the ModBuild-261 SHOW EDGE audit reads (WallSegmentFade.FadeCensus.cs).</summary>
        public bool WasDrawing;

        /// <summary>MODBUILD 271 — is an applier currently DRIVING this piece (ramping or
        /// holding it hidden)? Written only by the appliers and cleared by
        /// <see cref="FadeDriver.RestoreProp(MountedProp, Segment, string)"/>.
        ///
        /// <para>WHY IT EXISTS. Before the union rule a lane's fade was a SEGMENT property, so
        /// "is this piece driven" was answerable from <c>seg.MountedState</c> alone and the
        /// restore could be done for the whole list at once. Under the union rule two pieces of
        /// the SAME segment can disagree — one hangs over a neighbour's hole and rides that
        /// wall's fade, the other does not — so the release edge is per PIECE. Without this
        /// latch the per-piece restore would run every frame for every undriven piece of a
        /// segment that has any driven one, and <see cref="FadeDriver.RestoreProp"/> ends in
        /// <c>NoteOwnershipChange</c>: it would feed the round-11 churn tripwire a "released"
        /// transition per prop per frame. A restore is an EDGE, and this is the edge.</para></summary>
        public bool Driven;

        /// <summary>Where this piece is in its RETURN — see <see cref="ReturnPhase"/>. Written
        /// only by the appliers' hide branches, by <see cref="FadeDriver.ShowAttachmentPiece"/>
        /// and by <see cref="FadeDriver.RestoreProp(MountedProp, Segment, string)"/>; never by an
        /// instrument, so the SHOW EDGE audit stays free to contradict it.</summary>
        public ReturnPhase Return;

        // ModBuild 261: there is deliberately NO cached StaggerAt here. A record is reused out of
        // _mountedTouched across rescans and nothing reset the cache, so a piece Apparance had
        // regenerated kept a threshold hashed from an instance id that no longer existed, while
        // the foliage applier recomputed one from the new id — the same prop, two return points.
        // FadeDriver.StaggerThresholdFor is an O(1) memo read; there is nothing to cache.

        /// <summary>The fade at which this piece's prop unit was last seen returning, recorded by
        /// the SHOW EDGE audit so a unit that comes back IN PIECES can be named.</summary>
        public float ShownAtFade = -1f;
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

        /// <summary>MODBUILD 266 — THE ACCEPTANCE NUMBER for "die Flagge inklusive der Stange
        /// vollständig mit faden": how many pieces reached the mounted ledger ONLY because the
        /// wall generator built them, i.e. how many of the three refusals
        /// <see cref="FadeDriver.IsWallGeneratedDressing"/> lifts actually turned into an
        /// adoption. Counted at the ADOPTION and never at the refusal, and read off the sweep
        /// rather than off the ledger's opinion of itself. A ZERO in the next hardware log while
        /// the [FLOATING] / [WALL MEMBER] classes still name a hanging renderer falsifies this
        /// change outright: the exemption then never fired and the refusal is somewhere else.
        /// Named per piece on the same line ([WALL-BUILT]).</summary>
        private int _censusMountedWallBuilt;

        /// <summary>MODBUILD 268 — THE SECOND HALF OF THAT ACCEPTANCE NUMBER, and without it the
        /// first half reads as a regression the moment this round works. The 266 counter is set
        /// at the ADOPTION only, and a piece is adopted exactly once: from the next rescan on it
        /// is CARRIED by the sticky loop, which is why the 266 log oscillates between 12-18 and
        /// 0 on that field — it was being released and re-adopted every rescan, which is the
        /// defect this round closes. This counts the pieces the sticky loop KEPT that the
        /// round-7 figure guard would otherwise have handed back, i.e. exactly the population
        /// that produced the 165 x <c>RELEASED OVER A FADED WALL … term that failed: FIGURE —
        /// never carried</c> lines. Acceptance: this &gt; 0 while that grep reads 0.</summary>
        private int _censusMountedWallBuiltCarried;

        /// <summary>MODBUILD 271 — THE SIXTH EXEMPTION SITE'S OWN ACCEPTANCE NUMBER, deliberately
        /// NOT folded into <see cref="_censusMountedWallBuilt"/>. That counter answers "how many
        /// pieces did the ModBuild-266 provenance lift rescue from a renderer-TYPE or FIGURE
        /// refusal"; this one answers "how many did the AIRBORNE BAR lift rescue", and the two
        /// lifts have different blast radii and different falsifiers. A later log that could not
        /// tell them apart would make the crystal ruling unauditable — hence the separate count
        /// and the separate <c>[WALL-BUILT BELOW THE BAR]</c> tag. See
        /// <see cref="IsWallBuiltUnitDressing"/>.</summary>
        private int _censusMountedWallBuiltBelowBar;
        private int _lastLoggedMountedWallBuiltBelowBar = -1;

        /// <summary>Props that changed OWNER this rescan without ever being restored to visible —
        /// the leavers loop's ModBuild-265 handover. Reported so a handover that quietly loses
        /// its target is a number and not a silence.</summary>
        private int _censusMountedHandover;

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
        private int _lastLoggedMountedHandover = -1;
        private int _lastLoggedMountedWallBuilt = -1;
        private int _lastLoggedMountedWallBuiltCarried = -1;

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

        // ---- ModBuild 271: THE UNION RULE ------------------------------------------------------

        /// <summary>
        /// THE UNION RULE (user, 2026-08-25 — two reports, one shape).
        ///
        /// <para><b>THE REPORTS.</b> (1) <c>schwebende_kerzen.jpg</c>: "Manche Kerzen verschwinden
        /// noch nicht und schweben dann an der Wand … Es betrifft nicht alle Kerzen, nur manche."
        /// (2) <c>benachbarte_stange1/2.jpg</c>: "Wenn eine benachbarte Wand nicht gefaded hat und
        /// dort eine Stange in die Wand rausguckt die gefaded ist, dann gibt es nach wie vor die
        /// Situation, dass die Stange voll sichtbar ist auf der unsichtbaren Wand. Kümmere dich
        /// darum, dass auch in dieser Situation die Stange mitfaded OHNE DIE BENACHBARTE WAND ZU
        /// BEEINFLUSSEN."</para>
        ///
        /// <para><b>THE ROOT CAUSE, and it is one cause.</b> A renderer's visibility is decided by
        /// EXACTLY ONE segment — its owner — but the renderer physically belongs to more than one.
        /// This subsystem's own log has named the class since ModBuild 258: <c>N unit(s) have TWO
        /// OR MORE OWNERS on independent fades</c>, which reads 49 on 24 of the 114 census prints
        /// in the ModBuild-270 hardware log and 54 on 4 more. The unit-affinity rule was supposed
        /// to hold that at zero and does not. "Only some candles" is exactly what a rule keyed on
        /// the nearest wall produces: a sconce in the middle of a wall run has one wall over it, a
        /// sconce at a junction has two, and only the second kind can be orphaned by the wrong
        /// one.</para>
        ///
        /// <para><b>THE RULE.</b> The fade applied to a wall-ATTACHMENT renderer is the MAXIMUM
        /// over the fade of the segment that owns it and the fade of every OTHER fade-eligible
        /// segment whose decision AABB its own world AABB actually INTERSECTS. Max, never min and
        /// never a sum: a prop hanging over a hole must go with the hole, and a prop whose own
        /// wall is going must go with its own wall.</para>
        ///
        /// <para><b>INTERSECTION, NOT PROXIMITY — and this is the whole safety argument.</b> The
        /// test is a real AABB overlap widened by <see cref="MountedUnionSlackWU"/>, NOT the
        /// <see cref="MountedLinkMaxXZ"/> = 0.90 wu reach the OWNERSHIP election uses. A prop
        /// merely NEAR a faded wall keeps its own wall's fade; a prop whose geometry is literally
        /// inside the faded wall's slab rides it. Two parallel wall runs are ≥ one hex (1.72 wu)
        /// of clear floor apart, so an overlap cannot reach across a room by construction.</para>
        ///
        /// <para><b>WHAT IT MAY NOT DO, the user's own constraint.</b> No segment's Fade, State,
        /// PendingRaw, Smooth, coverage or decision is written anywhere in this feature. It reads
        /// <c>Segment.Fade</c> and <c>Segment.Bounds</c> and writes only what the PROP reads. The
        /// neighbouring wall is not touched; it is only CONSULTED.</para>
        ///
        /// <para><b>THE CORNER CARVE-OUT.</b> Registered shared corner pieces are EXEMPT and keep
        /// <c>Mathf.Min(cp.A.Fade, cp.B.Fade)</c> (round-7 ruling: a corner shared by two walls
        /// must stay while either neighbour stands). They are never entered into the map — see
        /// the explicit skip in <see cref="BuildMountedUnionOverlaps"/> — and
        /// <c>ApplyCornerPieces</c> never consults it.</para>
        ///
        /// <para><b>WHICH LANES.</b> The four DRESSING lanes only: mounted dressing, prop-unit
        /// dressing, foliage and asset siblings. The wall BODY meshes and the STACKED shell are
        /// the neighbouring wall rather than something hanging on it, and raising their fade
        /// would be precisely the "benachbarte Wand beeinflussen" the user forbade.</para>
        ///
        /// <para><b>COST.</b> The overlap SET is geometry and is built ONCE per rescan, at the end
        /// of <see cref="CollectWallMountedProps"/> when every lane list is final; the FADES are
        /// read live, so the per-frame read is one dictionary probe plus a walk of a list that is
        /// empty for almost every prop and has one or two entries otherwise. No
        /// <c>FindObjectsOfType</c>, no per-frame scene walk — the mistake that has shipped twice
        /// in this project and once cost 12.6 ms of an 11.11 ms frame.</para>
        ///
        /// <para><b>MULTIPLAYER.</b> Receiver-side presentation only: local bounds, local fades,
        /// local property blocks. No wire field, no <c>NetProtocol</c> change, no networked
        /// state.</para>
        ///
        /// <para><b>FALSIFIED BY.</b> The UNION RULE census line. A prop named there that is NOT
        /// standing over the named wall's hole means the slack is too large; an
        /// <c>applied 0</c> while <c>eligible &gt; 0</c> and a wall at fade 1.00 means the map is
        /// built but the appliers never read it.</para>
        /// </summary>
        /// <remarks>THE TOLERANCE. 0.10 wu ≈ 6 % of a hex step (1.72 wu) and ≈ 1/9 of the
        /// ownership reach. It exists to absorb the fact that a segment's decision AABB is
        /// ground-STRIPPED and rebuilt from its surviving renderers, so a prop bolted flush to
        /// its face can measure a hair outside it. It is far too small to bridge the gap between
        /// two parallel walls. FALSIFIER: a prop that fades while standing clear of the hole —
        /// the census prints the overlap DEPTH that decided it, so that is a number, not an
        /// argument.</remarks>
        private const float MountedUnionSlackWU = 0.10f;
        /// <summary>How many union overrides one census line NAMES. The line states its own cap
        /// and how many it dropped — a silently truncated list has cost this project three wrong
        /// diagnoses.</summary>
        private const int MountedUnionListCap = 12;

        /// <summary>One foreign segment overlapping a dressing renderer, with the measure that
        /// decided it: the SMALLEST axis of the AABB intersection box (wu), i.e. how deep the
        /// prop reaches into that wall's slab.</summary>
        internal readonly struct UnionHit
        {
            public readonly Segment Seg;
            public readonly float Depth;
            public UnionHit(Segment seg, float depth) { Seg = seg; Depth = depth; }
        }

        /// <summary>The per-renderer overlap record: who OWNS it, and every OTHER fade-eligible
        /// segment its own AABB reaches into. Pooled — one instance per overlapping renderer,
        /// reused across rescans.</summary>
        internal sealed class UnionEntry
        {
            public Segment Owner = null!;
            public string Lane = string.Empty;
            public readonly List<UnionHit> Hits = new(2);
        }

        private readonly Stack<UnionEntry> _unionPool = new();
        private readonly HashSet<Renderer> _unionCornerExempt = new();
        /// <summary>Renderers with at least one foreign overlapping segment — the DENOMINATOR, so
        /// a zero on the census is distinguishable from an instrument that never ran.</summary>
        private int _censusUnionEligible;
        /// <summary>Dressing renderers refused REGISTRATION because they are architecture-scale
        /// (see <see cref="RegisterUnionOverlaps"/>) — logging failures, not only successes.</summary>
        private int _censusUnionOversize;
        /// <summary>Dressing renderers walked that overlapped no foreign segment at all.</summary>
        private int _censusUnionNoOverlap;
        /// <summary>Prop-FRAMES in which an applier actually raised a piece's fade above its
        /// owner's, and the frames counted — live numbers, so a change-gated line can never look
        /// like a stopped tick.</summary>
        private int _unionRaisedPropFrames;
        private int _unionRaisedPieces;
        private int _lastLoggedUnionEligible = -1;
        private int _lastLoggedUnionApplied = -1;
        private int _lastLoggedUnionOversize = -1;
        private int _lastLoggedUnionNoOverlap = -1;
        private readonly List<string> _unionCensus = new();

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
            // ModBuild 271: the drive latch is spent — cleared FIRST, so every restore path
            // (unfade, drop, handover, orphan guard, teardown) leaves it false whatever it does
            // next. See MountedProp.Driven for why the per-piece edge exists.
            p.Driven = false;
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
            // Read the picture BEFORE the enable, so the denominator below counts an actual
            // transition and not a no-op restore.
            bool wasDrawing = IsActuallyDrawing(r);
            if (!r.enabled)
                r.enabled = true;
            // ModBuild 261: this piece is visible again, and BY CONSTRUCTION as authored —
            // authored materials reassigned, property block cleared, particle modules restored
            // above. It is counted in the SHOW EDGE denominator so the line's "0 not as authored"
            // is a ratio and not an empty set, but it is not re-audited: there is nothing left
            // for the audit to read that the restore did not just write, and the un-fade edge is
            // the one frame this round may not add a per-prop ancestor walk to.
            //
            // ONLY ON A REAL EDGE. RestoreProp runs for EVERY prop of a segment on every restore
            // path — RestoreSegmentMounted, RestoreSegmentUnitDressing, FinishPropUnitDressing,
            // the orphan guard, teardown — including props that were never hidden. Counting those
            // inflated the denominator of a line whose whole purpose is the ratio "N became
            // visible again, F of them not as authored", i.e. it made the round's own acceptance
            // bar easier to pass the more restores ran. The condition is read off the renderer,
            // never off p.WasDrawing, which the restore paths also write.
            if (!wasDrawing)
                _showEdgeTotal++;
            p.WasDrawing = true;
            p.ShownAtFade = 0f;
            // ModBuild 265: the piece is untouched again, so the RETURN latch is spent. Leaving
            // it set would make the NEXT fade-out skip the ramp for this piece (the ramp is
            // suppressed while it reads ReturnedAuthored) and turn its disappearance into a pop.
            p.Return = ReturnPhase.Free;
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
            if (owner == null || _releaseOverFadedWarns >= ReleaseOverFadedWarnCap)
                return;
            // MODBUILD 271 — THE SECOND HOLE THIS WARN CAN BE LET GO OVER. Until the union rule
            // the only wall that could be missing under a released prop was its OWNER's, so a
            // solid owner meant a safe release. It no longer does: the reported defect (2) is a
            // pole hanging over a NEIGHBOUR's hole while its own wall stands. Log the failure,
            // not only the success — with the foreign wall's live fade and the overlap depth, so
            // the next log decides it without another hardware round.
            Segment? foreign = HottestUnionSegment(r, out float foreignFade, out float depth);
            if (owner.Fade <= 0f && foreignFade <= 0f)
                return;
            _releaseOverFadedWarns++;
            string wall = owner.Anchor != null ? owner.Anchor.name : "<dead>";
            string over = foreign != null && foreignFade > owner.Fade
                ? $" It also overlaps '{(foreign.Anchor != null ? foreign.Anchor.name : "<dead>")}'"
                  + $" whose fade is {foreignFade:F2} (AABB overlap depth {depth:F2} wu) — the"
                  + " UNION RULE should have been holding it and did not."
                : string.Empty;
            VRLog.Warn(Name,
                $"RELEASED OVER A FADED WALL: '{r.name}' [{RendererKind(r)}] let go by '{wall}' "
                + $"while that wall's fade is {owner.Fade:F2} (mounted state "
                + $"{owner.MountedState}) — term that failed: {reason}.{over} The restore re-enables "
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
            int segWant = seg.Fade >= FoliageHideFade ? 2 : seg.Fade > 0f ? 1 : 0;
            // MODBUILD 271 — THE UNION RULE'S ENTRY POINT, and it has to be HERE rather than at
            // the ramp below. The early-out is a whole-SEGMENT decision, and defect (2) is
            // precisely a piece whose own wall is the one that has NOT faded: returning here
            // would mean no piece of this lane is ever looked at. See _live.MountedUnion.
            if (segWant == 0 && !LaneHasUnionFade(seg.Mounted, seg))
            {
                RestoreSegmentMounted(seg);
                return;
            }
            // No held-state early-out (round 5, the regen-churn lesson — see ApplyStacked):
            // a prop adopted or re-enabled while the segment is already held faded must be
            // hidden THIS frame. Held steady state = one enabled compare per prop.
            bool lost = false;
            int highest = 0;
            int raisedBefore = _unionRaisedPropFrames;
            foreach (MountedProp p in seg.Mounted)
            {
                if (p.Renderer == null)
                {
                    lost = true;
                    continue;
                }
                // THE ONE LINE THE UNION RULE CHANGES: the fade this PIECE reads. Never the
                // segment's own — nothing here writes seg.Fade, seg.State or seg.PendingRaw.
                float eff = UnionFade(p.Renderer, seg);
                int want = eff >= FoliageHideFade ? 2 : eff > 0f ? 1 : 0;
                if (want == 0)
                {
                    // Neither this piece's own wall nor any wall it hangs over is fading. The
                    // restore is an EDGE (MountedProp.Driven) — without that latch this would
                    // feed the churn tripwire a "released" transition per prop per frame.
                    if (p.Driven)
                        RestoreProp(p, seg, "wall solid again, and nothing it overlaps is fading");
                    continue;
                }
                p.Driven = true;
                if (want > highest)
                    highest = want;
                float ramp = Mathf.Clamp01(eff * MountedFadeLead);
                if (want == 2)
                {
                    // MODBUILD 261: evaluate on ADOPTION, not on the frame the piece is drawn
                    // again — the `enabled` guard now covers only the write. See
                    // WallSegmentFade.Body.cs for the ModBuild-260 evidence and the falsifier.
                    bool drawing = p.Renderer.enabled;
                    if (drawing || !p.SwapChecked)
                    {
                        _mountedTouched[p.Renderer] = p;
                        EnsureDissolveChannel(p);
                        DriveProp(p, ramp);
                    }
                    p.Return = ReturnPhase.HeldHidden; // ModBuild 265 — see ShowAttachmentPiece
                    if (drawing)
                        p.Renderer.enabled = false;
                    ShowEdge(p, false, eff);
                }
                else
                {
                    _mountedTouched[p.Renderer] = p;
                    EnsureDissolveChannel(p); // round 15: dressing without a channel animates too
                    // ModBuild 265: the ramp, the return gate, the audit call and the enable are
                    // ONE implementation for all five appliers — the ModBuild-261 lesson about
                    // two lanes writing "the same" rule twice.
                    ShowAttachmentPiece(p, p.Renderer, ramp, eff);
                }
            }
            if (_unionRaisedPropFrames != raisedBefore)
                _unionRaisedPieces++;
            if (lost)
                _nextRescan = 0f; // prop regenerated away mid-fade — re-collect promptly
            seg.MountedState = highest;
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
            // Same reasoning for the ModBuild-261 SHOW EDGE unit ledger: it is keyed by scene
            // Transforms, and a destroyed one must never be compared against a new prop that
            // happens to reuse the slot.
            _showEdgeUnitReturn.Clear();
            foreach (Segment seg in _live.Segments.Values)
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
            foreach (Segment seg in _live.Segments.Values)
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

        /// <summary>
        /// IS THIS SEGMENT STILL HIDING WHAT IT OWNS — the ONE predicate behind every
        /// "ownership sticky while faded" rule in this subsystem. <paramref name="laneState"/>
        /// is the attachment lane's own state field (<see cref="Segment.MountedState"/>,
        /// <see cref="Segment.UnitDressingState"/>, …): non-zero means that lane's applier is
        /// currently holding pieces off the picture, and <c>Fade &gt; 0</c> means the masonry
        /// itself is gone or going. Either way an ownership change made now hands a piece to
        /// somebody else while the wall behind it is not there, and the release re-enables it.
        ///
        /// <para>MODBUILD 265. It was a local <c>bool</c> inside the mounted sweep and the
        /// prop-unit dressing lane had no equivalent at all, which is why the ModBuild-264
        /// session reads 66 <c>RELEASED OVER A FADED WALL</c> naming
        /// <c>prop-unit dressing — this unit no longer fades with this wall</c> and ZERO naming
        /// the mounted lane. One concept, one predicate; see
        /// <c>WallPropUnit.ChooseOwner</c> rule 1, which is the same rule for the OWNER of a
        /// unit rather than for the members of a lane.</para>
        ///
        /// <para>FALSIFIED BY: a <c>RELEASED OVER A FADED WALL</c> count above 0 for any reason
        /// that is not a FIGURE or a MOBILE prop in the next hardware log.</para>
        /// </summary>
        private static bool SegmentStillHiding(Segment seg, int laneState)
            => laneState != 0 || seg.Fade > 0f;

        /// <summary>
        /// Horizontal (XZ) gap between two AABBs; 0 when their footprints overlap.
        ///
        /// <para>PERF E (ModBuild 279) — THE DEGENERATE AXES SKIP THE SQUARE ROOT, AND NOTHING
        /// ELSE CHANGES. This is the innermost expression of three separate
        /// O(candidates x segments) walks in the mounted and stacked passes, so the root is paid
        /// per pair. On a board of axis-aligned wall runs and axis-aligned prop boxes, one of the
        /// two separations is EXACTLY zero for most pairs — <c>Mathf.Max(0f, …)</c> returns a
        /// hard zero the moment the footprints overlap on that axis — and
        /// <c>sqrt(fl(d*d) + 0)</c> is <c>d</c> bit for bit under IEEE-754 round-to-nearest for
        /// every d this board can produce. So the two early returns give the identical float,
        /// not a near one, and every comparison and every printed <c>F2</c> downstream reads the
        /// same value it read before.</para>
        ///
        /// <para>WHAT WAS DELIBERATELY NOT DONE, and it was on the round's list: replacing the
        /// gap with its SQUARE and comparing against squared reach constants. Two reasons, both
        /// of them "this changes a decision". First, the gap is not only compared — it escapes
        /// into <c>NoteMountedReject</c>, <c>NoteStackReject</c> and the leftover census as a
        /// printed distance, so a root has to come back at the end anyway. Second and
        /// disqualifying: the adoption loops pick an owner with <c>gap &gt;= bestGap</c>, and
        /// <c>sqrt</c> is monotonic but not injective in floating point — two distinct squared
        /// distances can round to the SAME root. Under the squared form the later candidate wins
        /// such a tie; under the shipped form the earlier one keeps it. That is a different wall
        /// owning a prop, which is a picture change, and this round's invariant is that not one
        /// decision predicate moves.</para>
        /// </summary>
        private static float HorizontalGap(Bounds a, Bounds b)
        {
            float dx = Mathf.Max(0f, Mathf.Max(a.min.x - b.max.x, b.min.x - a.max.x));
            float dz = Mathf.Max(0f, Mathf.Max(a.min.z - b.max.z, b.min.z - a.max.z));
            if (dz == 0f)
                return dx;
            if (dx == 0f)
                return dz;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Horizontal (XZ) gap between an AABB and a point. Same degenerate-axis skip as
        /// the AABB pair above, and the same argument for why it is exact.</summary>
        private static float HorizontalGap(Bounds a, Vector3 p)
        {
            float dx = Mathf.Max(0f, Mathf.Max(a.min.x - p.x, p.x - a.max.x));
            float dz = Mathf.Max(0f, Mathf.Max(a.min.z - p.z, p.z - a.max.z));
            if (dz == 0f)
                return dx;
            if (dx == 0f)
                return dz;
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
            _mountedLeftoverAllowed.Clear();
            _mountedLeftoverByClass.Clear();
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
            _censusMountedWallBuilt = 0;
            _censusMountedWallBuiltCarried = 0;
            _censusMountedWallBuiltBelowBar = 0; // ModBuild 271 — the sixth site's own number
            _censusMountedHandover = 0;
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
            foreach (Segment seg in _live.Segments.Values)
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
                // PROP-UNIT DRESSING (ModBuild 259) is spoken for too. Without this the sweep
                // below would adopt the same renderer a SECOND time — one prop with two owners,
                // which is the exact class the blue flame of wandproblem3.jpg belonged to — and
                // the orphan guard would release it every rescan for not being in _mountedOwned.
                foreach (MountedProp p in seg.UnitDressing)
                {
                    if (p.Renderer == null || inertDoorway)
                        continue;
                    _mountedOwned.Add(p.Renderer);
                    _attachmentOwned[p.Renderer] = new OwnerRef(seg, "prop-unit dressing");
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
            foreach (Segment seg in _live.Segments.Values)
            {
                seg.PrevMounted.Clear();
                seg.PrevMounted.AddRange(seg.Mounted);
                seg.Mounted.Clear();
            }

            // STICKY OWNERSHIP: a segment that is mid-fade or held faded keeps every prop it
            // already owns — releasing one while its wall is gone is exactly the blink the first
            // hardware round produced.
            foreach (Segment seg in _live.Segments.Values)
            {
                bool sticky = SegmentStillHiding(seg, seg.MountedState);
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
                        //
                        // MODBUILD 268 — THE FIFTH SITE. ModBuild 266 lifted this same refusal
                        // for wall-generated dressing at FOUR places (the `!f.Mountable`
                        // structural skip, the sweep's FIGURE reject, the sweep's standing
                        // reject and PurgeFigureRenderers) and MISSED this one, which is the
                        // only one that RELEASES a piece it already owns. The 266 hardware log
                        // says so in one line and with one reason: 165 x
                        // `RELEASED OVER A FADED WALL … term that failed: FIGURE — never
                        // carried (round-7 ruling)`, 86 of them 'EN_CR_Hanging_01_Cloth_Post'
                        // and 79 'CR_BT_BanditBanner_Wall' — the two subjects the 266 exemption
                        // adopts, and NOTHING else in the histogram. The sweep below adopted
                        // them on provenance; this loop handed them straight back on the next
                        // rescan and RestoreProp ends with `r.enabled = true` over a wall at
                        // fade 1.00, which is the user's "ab und zu ploppen sie weg, aber
                        // tauchen wieder auf" (2026-08-25) exactly — the same two-rescan
                        // oscillation ModBuild 265 fixed for the scrub, in a different loop.
                        //
                        // THE ROUND-7 RULING IS NOT RELAXED. IsWallGeneratedDressing is a
                        // CONJUNCTION with the actor veto, never a widening: it keeps
                        // ActorBehaviour / CInteractableActor in the parent chain as an
                        // absolute veto and adds ProceduralWall provenance on top, so it is
                        // strictly narrower on figures than the guard it stands beside. It is
                        // also the same predicate, at the same severity, that already decides
                        // whether this piece may be ADOPTED — a carry rule that disagreed with
                        // the adoption rule is what produced this round.
                        bool carriedWallBuilt = false;
                        if (IsFigureOrActorRenderer(p.Renderer))
                        {
                            if (!IsWallGeneratedDressing(p.Renderer))
                            {
                                RestoreProp(p, seg, "FIGURE — never carried (round-7 ruling)");
                                _mountedReleased.Add(p);
                                continue;
                            }
                            carriedWallBuilt = true;
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
                            if (carriedWallBuilt)
                                _censusMountedWallBuiltCarried++;
                            NoteOwnershipChange(p.Renderer,
                                $"mounted:'{stickyHome.Anchor!.name}'(prop unit)");
                            continue;
                        }
                        seg.Mounted.Add(p);
                        _censusMounted++;
                        if (carriedWallBuilt)
                            _censusMountedWallBuiltCarried++;
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
            for (int i = 0; i < _live.RoomFloorY.Count && i < _live.RoomFloorAnchored.Count; i++)
            {
                if (_live.RoomFloorAnchored[i] && _live.RoomFloorY[i] < minFloorY)
                    minFloorY = _live.RoomFloorY[i];
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
            foreach (Segment seg in _live.Segments.Values)
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
                    // MODBUILD 266 — THE RENDERER TYPE IS NOT THE QUESTION WHEN THE WALL
                    // GENERATOR BUILT THE PIECE. 'EN_CR_Hanging_01_Cloth_Post' is a cloth
                    // SkinnedMeshRenderer hanging under 'Wall 4/Generated Content/…' and left
                    // here silently for every build: the ModBuild-265 log has it as
                    // "[FLOATING] … not adopted because: renderer type SkinnedMeshRenderer is
                    // not scenery", 2.26 wu over the floor beside a wall at fade 1.00, which is
                    // the pole in Flaggen.jpg. See FadeDriver.IsWallGeneratedDressing for the
                    // provenance test and for why it cannot widen the round-7 figure gate.
                    // THE ACCEPTANCE NUMBER (see LogMountedCensus): how many pieces reached the
                    // ledger ONLY because the wall generator built them. It is set at the
                    // refusal each exemption lifts and counted at the ADOPTION, never here —
                    // a census that counts intentions is the failure this file has paid for
                    // twice, and every one of these candidates can still be refused below.
                    bool wallBuilt = false;
                    if (!f.Mountable)
                    {
                        if (!IsWallGeneratedDressing(c))
                        {
                            if (StructuralSkipArmed)
                            {
                                NoteStructuralSkip(c,
                                    $"renderer type {c.GetType().Name} is not scenery");
                            }
                            continue;
                        }
                        wallBuilt = true;
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
                    // PERF E (ModBuild 279) — THE NAME IS ALREADY IN HAND, so it is not read
                    // again. `c.name` is an interop call that allocates, it was built as an
                    // ARGUMENT (so it allocated even when _live.ArchRects is empty and the callee's
                    // loop never runs), and it was paid once per surviving sweep candidate per
                    // commit. `f.Name` is the very string ClassifyMaterialsAndName already
                    // allocated for this renderer this cycle (RendererFact.Name, ModBuild 278) —
                    // the same value, not an equivalent one. It is re-read on a COLD classify
                    // like every other fact, so a renderer RENAMED between two cold classifies
                    // would be tested under its old name for at most one cycle; nothing in this
                    // tileset renames a GameObject at runtime, and the arch rect's own name test
                    // is a "Door" substring on a prefab name.
                    if (IsArchProtected(archProbe, f.Name ?? c.name))
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
                    // MODBUILD 271 — THE SIXTH EXEMPTION SITE: THE AIRBORNE BAR ITSELF.
                    //
                    // The bar is a GEOMETRIC PROXY for "was the wall holding this up?". For a
                    // piece the wall generator built, PROVENANCE answers that question directly
                    // and answers it better — which is the argument ModBuild 266 made at four
                    // sites (the renderer-type skip, the FIGURE reject, the standing reject and
                    // PurgeFigureRenderers) and ModBuild 268 at the fifth (the sticky carry).
                    // The bar was never among them, and it is the refusal the ModBuild-270 log
                    // actually prints for the two subjects of this round's report: 'anchor 0.44
                    // under the airborne bar 1.00 — reads as floor-supported, so it would NOT
                    // float' for EN_CR_Hanging_01_Mesh and 'anchor 0.33 …' for EN_CR_Curtain_Mesh.
                    //
                    // THE CONDITION IS A CONJUNCTION, NOT THE PROVENANCE TERM ALONE — see
                    // IsWallBuiltUnitDressing. Provenance alone would also adopt
                    // 'CV_Ice_Crystal_Form_02/03' (STANDING USER RULING 2026-08-24: the crystal
                    // formation stays) and 'LightShaft_Prefab (1)', both of which read
                    // ProceduralWall provenance at the same 3-4 levels as the targets. The term
                    // that separates them in every one of the 4,111 leftover-audit rows of the
                    // ModBuild-270 log is whether the four-level prop-unit walk resolves a UNIT:
                    // 146/146 crystal rows, 146/146 for form 03 and 145/145 light-shaft rows read
                    // 'no prop unit at all', while 383/383 curtain rows resolve
                    // 'PCG_CR_Curtain_Red'. Depth does not separate them and neither does foot
                    // height, renderer type or XZ gap — see WallProvenanceProbeLevels.
                    //
                    // ONE BOOL, FOUR GATES. The bar refuses this candidate at four places (the
                    // nearest-wall election, the per-room floor plane, the unit-affinity override
                    // and the refusal itself), and lifting only the last of them would drop the
                    // piece into "no wall within reach / outside its span" instead — the earlier
                    // sites would already have discarded every segment. `belowBar` is cleared for
                    // the three that read it; the per-room plane reads the hoisted bool directly.
                    bool barProvenance = false;
                    bool wallGenBelowBar =
                        belowBar && IsWallBuiltUnitDressing(c, out barProvenance);
                    if (wallGenBelowBar)
                        belowBar = false;

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
                    foreach (Segment seg in _live.Segments.Values)
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
                        // GATE 2 OF THE FOUR (ModBuild 271): the PER-ROOM floor plane. This one
                        // does not read `belowBar`, so clearing that bool above does not reach it
                        // — it has to consult the hoisted provenance bool itself or a wall-built
                        // hanging is discarded here, silently, in the segment loop.
                        if (!wallGenBelowBar
                            && anchorY < _live.RoomFloorY[seg.RoomIndex] + MountedClearanceWU)
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
                        // MODBUILD 271 — LOG THE FAILURE, NOT ONLY THE SUCCESS. A below-bar
                        // candidate the wall generator DID build but whose prop-unit walk found
                        // no unit is refused here on the second half of the conjunction, and the
                        // reject has to say which half — otherwise the crystal ruling and a
                        // genuinely missed hanging produce the same sentence.
                        // PERF E: the sentence is built only for a candidate the reject list can
                        // actually record — see MountedRejectReasonWanted. This is the hottest
                        // of the eight sites: everything resting on the floor leaves here.
                        NoteMountedReject(c, anchorY, nearestAny,
                            MountedRejectReasonWanted(nearestAny)
                                ? $"anchor {anchorY:F2} under the airborne bar {airborneBar:F2} — "
                                  + "reads as floor-supported, so it would NOT float"
                                  + (barProvenance
                                      ? " — the wall generator DID build it, but the four-level "
                                        + "prop-unit walk found no unit root (or an actor "
                                        + "component vetoes it), so the ModBuild-271 exemption "
                                        + "does not reach it: this is the term that keeps "
                                        + "'CV_Ice_Crystal_Form_02/03' and 'LightShaft_Prefab (1)' "
                                        + "standing (user ruling 2026-08-24)"
                                      : " — no ProceduralWall provenance either")
                                : MountedRejectReasonNotBuilt);
                        continue;
                    }
                    if (best == null)
                    {
                        // PERF E: `cappedBy.Anchor.name` is an interop read that allocates, and
                        // the sentence around it a string.Format — both were paid for every
                        // candidate with no wall in reach, which is most of the scene.
                        NoteMountedReject(c, anchorY, nearestAny,
                            cappedBy == null
                            ? "no wall within reach / outside its span"
                            : MountedRejectReasonWanted(nearestAny)
                              ? $"'{(cappedBy.Anchor != null ? cappedBy.Anchor.name : "<dead>")}' "
                                + $"is SATURATED at {MountedMaxPerSegment} mounted props and no "
                                + "other wall is in reach — this candidate is lost to the runaway "
                                + "cap, not to geometry"
                              : MountedRejectReasonNotBuilt);
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
                            MountedRejectReasonWanted(bestGap) // PERF E — see the helper
                            ? $"architecture-scale (AABB volume {volume:F1} wu³ > "
                              + $"{MountedMaxMeshVolumeWU3:F1}) — stacked-shell territory, "
                              + "never sconce dressing"
                            : MountedRejectReasonNotBuilt);
                        continue;
                    }
                    // MODBUILD 266 — "GetComponentInParent<Animator>() != null" ANSWERS "is
                    // there an Animator anywhere above me", NOT "am I a creature", and the
                    // in-repo lesson for exactly that confusion is containment-is-not-identity.
                    // 'CR_BT_BanditBanner_Wall' is a MeshRenderer under 'Wall N/Generated
                    // Content/…' whose banner WAVES, and the ModBuild-265 log refuses it here:
                    // "[FLOATING] … not adopted because: FIGURE (never touched — round-7
                    // ruling, Lights-rule severity)", 2.98 wu over the floor beside a wall at
                    // fade 1.00. The round-7 ruling is NOT relaxed: IsWallGeneratedDressing
                    // keeps the ActorBehaviour/CInteractableActor chain as an absolute veto and
                    // adds a provenance term on top, so it is strictly narrower on figures than
                    // the guard it stands beside. See its doc for the game-source evidence that
                    // a figure is never a child of a wall's Generated Content.
                    if (IsFigureOrActorRenderer(c))
                    {
                        if (!IsWallGeneratedDressing(c))
                        {
                            NoteMountedReject(c, anchorY, bestGap,
                                "FIGURE (never touched — round-7 ruling, Lights-rule severity)");
                            continue;
                        }
                        wallBuilt = true;
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
                        // MODBUILD 266 — ONE ARM OF THAT RULE, AND ONLY ONE. The standing rule
                        // has two (WallSegmentFade.Standing.cs): the FIGURE arm, which protects
                        // a unit because something above it carries ActorBehaviour /
                        // CInteractableActor / Animator, and the FLOOR arm, which is the
                        // skelet.jpg arm and protects floor-standing SCENERY that has no figure
                        // ancestry at all. IsStandingFigureOnlyProp is the FIGURE arm on its own
                        // — the same memoised measurement, no second walk — so this exemption
                        // can reach a unit protected for ANIMATING and can never reach one
                        // protected for STANDING ON THE FLOOR. The 2026-08-19 skull keeps every
                        // renderer it has: its unit has no figure ancestry, so this arm reads
                        // false for it and the refusal below stands unchanged.
                        if (!(IsStandingFigureOnlyProp(c) && IsWallGeneratedDressing(c)))
                        {
                            NoteStandingPropBlocked(c, null);
                            NoteMountedReject(c, anchorY, bestGap,
                                "part of a prop unit that STANDS ON THE FLOOR — never wall "
                                + "dressing (WallSegmentFade.Standing.cs)");
                            continue;
                        }
                        wallBuilt = true;
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
                            MountedRejectReasonWanted(bestGap) // PERF E — see the helper
                            ? $"MOBILE — {DriftText(candidateDrift)} against "
                              + $"'{(best.Anchor != null ? best.Anchor.name : "<dead>")}' since the "
                              + "last rescan, so it follows something rather than hanging on that "
                              + "wall (figure VFX / carried prop) — never wall dressing"
                            : MountedRejectReasonNotBuilt);
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
                    if (wallBuilt)
                        _censusMountedWallBuilt++;
                    // ModBuild 271: a DISTINCT counter and a DISTINCT tag for the sixth site, so
                    // the ModBuild-266 provenance lift and this one can never be confused in a
                    // later log. Counted at the ADOPTION like every other acceptance number in
                    // this file — a census that counts intentions is the failure this subsystem
                    // has paid for twice.
                    if (wallGenBelowBar)
                        _censusMountedWallBuiltBelowBar++;
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
                            + (byUnitHome ? " [its PROP UNIT's wall, not the nearest]" : string.Empty)
                            + (wallBuilt ? " [WALL-BUILT: adopted on provenance, ModBuild 266]"
                                         : string.Empty)
                            + (wallGenBelowBar
                                ? " [WALL-BUILT BELOW THE BAR: adopted on provenance + a resolved "
                                  + $"prop unit at anchor {anchorY:F2} < {airborneBar:F2}, "
                                  + "ModBuild 271]"
                                : string.Empty));
                    }
                }
            }

            // Leavers: restore anything this segment held that it no longer owns.
            foreach (Segment seg in _live.Segments.Values)
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
                        // MODBUILD 265 — A HANDOVER IS NOT A RELEASE, and it must carry the
                        // HIDDEN state across. When _attachmentOwned names an owner for this
                        // renderer it was rebuilt THIS rescan, a dozen lines above, out of the
                        // final lists (stacked / body / prop-unit dressing / corner / renderers
                        // / foliage / siblings) — so that owner's applier drives the piece on
                        // the same frame, in BOTH directions. RestoreProp would first turn the
                        // renderer back ON (and destroy the swap copies, and drop the record out
                        // of _mountedTouched that the new lane is already holding, so the next
                        // frame rebuilds the whole dissolve channel) before the new owner hid it
                        // again. That is the ModBuild-258 argument for the sticky loop's own
                        // handover, made in the leavers loop instead of only in the sticky one.
                        //
                        // THE NUMBER THAT MADE THIS CHANGE: 10 of the ModBuild-264 session's 76
                        // RELEASED OVER A FADED WALL warns read "lost the claim to the prop-unit
                        // dressing of 'FR_Pillar_Tree_Trunk_0*' (that owner's fade 0.03/0.09)" —
                        // a named, live, faded new owner, and the restore switched the piece back
                        // on anyway. THE NUMBER THAT WOULD FALSIFY IT: any renderer in the next
                        // log's LEFTOVER line ("drawing over a fully faded wall") or in the
                        // SHOW EDGE line's "not as authored" fraction that this hand-over path
                        // touched — i.e. a piece left wearing our swap copies with nobody driving
                        // it. The orphan guard immediately below is the backstop: a handover
                        // target that is not in _mountedOwned is restored on this very rescan.
                        //
                        // THE GUARD IS `_mountedOwned`, NOT `_attachmentOwned` ALONE, and the two
                        // differ exactly where it matters. The lanes that go into BOTH sets
                        // (stacked shell, wall body, prop-unit dressing, shared corner) drive the
                        // piece through this same shared MountedProp ledger — they own its
                        // `enabled` and its swap copies, so conceding hands over a complete state.
                        // The lanes that go into `_attachmentOwned` only (wall renderer, foliage,
                        // asset sibling) drive it through the WALL's own channel and know nothing
                        // about our swap copies, so those still take the restore — otherwise the
                        // piece would be drawn wearing our dissolve materials with nobody ramping
                        // them, which is the SHOW EDGE line's "not as authored" fault.
                        Segment? newSeg = null;
                        string newKind = string.Empty;
                        if (_attachmentOwned.TryGetValue(prev.Renderer, out OwnerRef newOwner))
                        {
                            newSeg = newOwner.Seg;
                            newKind = newOwner.Kind;
                        }
                        if (newSeg != null && _mountedOwned.Contains(prev.Renderer))
                        {
                            _censusMountedHandover++;
                            NoteOwnershipChange(prev.Renderer,
                                $"{newKind}:'{(newSeg.Anchor != null ? newSeg.Anchor.name : "<dead>")}'");
                            continue;
                        }
                        RestoreProp(prev, seg, newSeg != null
                            ? $"lost the claim to the {newKind} of "
                              + $"'{(newSeg.Anchor != null ? newSeg.Anchor.name : "<dead>")}' "
                              + $"(that owner's fade {newSeg.Fade:F2}), and that lane drives the "
                              + "piece through the wall's own channel rather than through this "
                              + "prop ledger, so the authored materials have to go back"
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

            // MODBUILD 271 — THE UNION MAP, built here and only here. Every lane list is final at
            // this point (mounted, prop-unit dressing, foliage, siblings; leavers restored, orphans
            // released), which is the whole reason it is at the end of the rescan rather than
            // inside the sweep: the sweep's per-candidate segment walk skips every prop that is
            // already adopted or carried sticky, i.e. almost all of them in the steady state.
            // GEOMETRY here, FADES live at the applier — see _live.MountedUnion.
            BuildMountedUnionOverlaps();

            // Apparance streams the dressing in over several rescans, so the scenario's first
            // heartbeat would report a half-built table forever: re-log whenever the attached set
            // actually changed. Steady state prints nothing.
            if (_censusMounted != _lastLoggedMountedCount
                || _censusMountedRejected != _lastLoggedMountedRejected
                || _censusMountedLeftover != _lastLoggedMountedLeftover
                || _censusMountedHandover != _lastLoggedMountedHandover
                // ModBuild 266: the acceptance number gets its own trigger, so a session in
                // which only the provenance population moves still re-prints the line the
                // number lives on. A held instrument reads as a dead one.
                || _censusMountedWallBuilt != _lastLoggedMountedWallBuilt
                // ModBuild 268: same argument for the CARRY half. Once the fifth site stops
                // releasing them, the adoption half settles at 0 and this one carries the
                // signal — a trigger on the adoption half alone would print the line once and
                // then look like a stopped tick.
                || _censusMountedWallBuiltCarried != _lastLoggedMountedWallBuiltCarried
                // ModBuild 271: and the AIRBORNE-BAR lift gets its own trigger for the same
                // reason — a held instrument reads as a dead one.
                || _censusMountedWallBuiltBelowBar != _lastLoggedMountedWallBuiltBelowBar)
                LogMountedCensus();
            // ModBuild 259: the SECOND leftover class — a whole split-run PIECE left standing
            // beside its faded run (neues_wandproblem.jpg). Measured here so both classes reach
            // the one line below and one grep still finds every leftover.
            SweepRunLeftovers();
            // The two alarms stand alone: a leftover is the reported defect, and a mobile prop
            // is the round-7 ruling being enforced against a class the ancestry test cannot see.
            LogMountedLeftovers();
            LogMountedMobile();
            // ModBuild 271: the union rule's own line, and it stands alone for the same reason
            // the two alarms above do — it is the whole of this round and must not be a clause
            // inside a line about something else.
            LogMountedUnion();
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
            foreach (Segment seg in _live.Segments.Values)
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

        /// <summary>
        /// MODBUILD 271 — THE AIRBORNE-BAR EXEMPTION'S PREDICATE, and it is a CONJUNCTION of
        /// three terms, never the provenance term on its own.
        ///
        /// <list type="number">
        /// <item>THE WALL GENERATOR BUILT IT — <see cref="IsWallGeneratedDressing"/>, i.e.
        ///   membership in a <c>ProceduralWall</c> subtree, the same discriminator ModBuild 266
        ///   shipped at four sites and 268 at a fifth.</item>
        /// <item>THE ACTOR VETO — carried INSIDE that predicate and never relaxed:
        ///   <c>ActorBehaviour</c> / <c>CInteractableActor</c> anywhere above the renderer is an
        ///   absolute refusal (round-7 ruling, Lights-rule severity).</item>
        /// <item>AND THE FOUR-LEVEL PROP-UNIT WALK RESOLVES A UNIT ROOT. This is the term this
        ///   build adds, and it is the ONLY field that separates the five renderer families the
        ///   lift's measured blast radius contains. From the 4,111 leftover-audit rows of the
        ///   ModBuild-270 log (complete — the leftover list caps at 40 and never exceeded 17):
        ///   <c>EN_CR_Curtain_Mesh</c> resolves <c>PCG_CR_Curtain_Red</c> on 383 of 383 rows and
        ///   <c>EN_CR_Hanging_01_Mesh</c> resolves <c>PCG_Test_Feature_Small_2</c> on its 'Wall 4'
        ///   and 'Wall 6' instances, while <c>CV_Ice_Crystal_Form_02</c> (146/146),
        ///   <c>CV_Ice_Crystal_Form_03</c> (146/146) and <c>LightShaft_Prefab (1)</c> (145/145)
        ///   print <c>no prop unit at all (the four-level walk found no unit root)</c> on EVERY
        ///   row. Ancestry DEPTH does not separate them (3–4 levels on both sides), and neither
        ///   does foot height, top height, renderer type or XZ gap — see
        ///   <see cref="WallProvenanceProbeLevels"/> for that whole interleaved table.</item>
        /// </list>
        ///
        /// <para>WHICH DIRECTION THE UNKNOWN FAILS IN. A piece whose unit walk happens to resolve
        /// nothing on a given rescan is simply NOT adopted that rescan, which is the status quo
        /// and not a regression; a piece that IS adopted is then carried sticky while its wall is
        /// faded, so the adoption does not have to be re-won every rescan. The failure mode this
        /// arrangement cannot have is the dangerous one — the crystal formation being taken.</para>
        ///
        /// <para>OUT OF THE BLAST RADIUS BY EARLIER TERMS, confirmed against the same log and
        /// unchanged by this build: rubble (<c>EN_CR_StoneBlock_*</c>) reads
        /// <c>NO ProceduralWall anywhere above it</c> and fails term 1; ground foliage
        /// (<c>FR_Floor_LargeBush_*</c>, <c>FR_Floor_Detail_Grass_*</c>) and <c>CR_RU_Vines</c>
        /// are claimed as <c>seg.Foliage</c> before the bar is reached or refused earlier still
        /// by <see cref="IsWaterProtected"/> under the 2026-08-09 water ruling.</para>
        ///
        /// <para>FALSIFIED BY: the <c>[WALL-BUILT BELOW THE BAR]</c> tag in the mounted census
        /// naming <c>CV_Ice_Crystal_Form_02/03</c> or <c>LightShaft_Prefab (1)</c>. Then the unit
        /// walk resolves for them in some scenario and this term is not the discriminator — the
        /// rule must be withdrawn rather than retuned.</para>
        /// </summary>
        /// <param name="provenance">Term 1+2 alone, so the refusal below the bar can say WHICH
        /// half of the conjunction failed instead of printing one sentence for both.</param>
        private bool IsWallBuiltUnitDressing(Renderer r, out bool provenance)
        {
            provenance = IsWallGeneratedDressing(r);
            return provenance && StandingFloorUnitRootOf(r) != null;
        }

        // ---- ModBuild 271: the union map ------------------------------------------------------

        /// <summary>
        /// Build the per-renderer overlap map ONCE per rescan — see <see cref="CommittedTable.MountedUnion"/>
        /// for the rule, the constraint and the falsifier. Called at the very end of
        /// <see cref="CollectWallMountedProps"/>, where every lane list is final.
        /// </summary>
        private void BuildMountedUnionOverlaps()
        {
            foreach (UnionEntry e in _live.MountedUnion.Values)
            {
                e.Hits.Clear();
                e.Owner = null!;
                _unionPool.Push(e);
            }
            _live.MountedUnion.Clear();
            _live.UnionOwners.Clear();
            _censusUnionEligible = 0;
            _censusUnionOversize = 0;
            _censusUnionNoOverlap = 0;

            // THE CORNER CARVE-OUT, made explicit rather than left to follow from the fact that a
            // corner piece never lands in a dressing list. A shared corner keeps
            // Mathf.Min(cp.A.Fade, cp.B.Fade) (round-7 ruling: it must stay while EITHER
            // neighbour stands), which is the exact opposite of this rule — so it is named here
            // and skipped, and ApplyCornerPieces never reads the map at all.
            _unionCornerExempt.Clear();
            foreach (CornerPiece cp in _live.CornerPieces)
            {
                if (cp.Prop.Renderer != null)
                    _unionCornerExempt.Add(cp.Prop.Renderer);
            }

            foreach (Segment owner in _live.Segments.Values)
            {
                foreach (MountedProp p in owner.Mounted)
                    RegisterUnionOverlaps(p.Renderer, owner, "mounted dressing");
                foreach (MountedProp p in owner.UnitDressing)
                    RegisterUnionOverlaps(p.Renderer, owner, "prop-unit dressing");
                foreach (MeshRenderer f in owner.Foliage)
                    RegisterUnionOverlaps(f, owner, "foliage");
                foreach (MeshRenderer s in owner.Siblings)
                    RegisterUnionOverlaps(s, owner, "asset sibling");
            }
        }

        /// <summary>
        /// Enter one dressing renderer into the overlap map, if anything foreign overlaps it.
        ///
        /// <para>THE ARCHITECTURE GUARD IS NOT OPTIONAL HERE. This rule can only ever RAISE a
        /// piece's fade, so it must only ever reach pieces that are unambiguously DRESSING — and
        /// the foliage and sibling lanes carry things the mounted sweep would never have adopted,
        /// including whole tree canopies whose AABB spans several wall slabs at once (the
        /// in-repo lesson is that 113 of 172 blocked rays were trees). The two tests are the ones
        /// the mounted sweep already uses to separate dressing from architecture, verbatim: two
        /// fat axes, and AABB volume. Particles are exempt from both for the same reason they are
        /// everywhere else in this file — their bounds are a smoke plume, not an object size —
        /// and are measured at their EMITTER instead, the same anchor the mounting itself
        /// uses.</para>
        ///
        /// <para>THE PARTICLE PROBE IS A POINT, and that is a KNOWN, NAMED LIMIT rather than an
        /// oversight. A particle system's live bounds enclose its particles and drift every
        /// frame — the "Kerzen blinken" bug of round 2 — so a union SET built from them would
        /// flicker between rescans and take a flame off a standing wall and put it back. The
        /// emitter point, expanded only by the slack on both boxes, is stable; the cost is that a
        /// torch flame standing PROUD of a neighbour's slab is not raised by this rule. FALSIFIER:
        /// a particle emitter named in the next log's LEFTOVER audit as drawing over a fully
        /// faded wall. The remedy would be to probe the emitter's own sconce MESH, not to widen
        /// this point into a radius.</para>
        /// </summary>
        private void RegisterUnionOverlaps(Renderer? r, Segment owner, string lane)
        {
            if (r == null || _live.MountedUnion.ContainsKey(r) || _unionCornerExempt.Contains(r))
                return;
            bool particles = r is ParticleSystemRenderer;
            Bounds b = particles
                ? new Bounds(r.transform.position, Vector3.zero)
                : r.bounds;
            if (!particles)
            {
                Vector3 sz = b.size;
                int fatAxes = (sz.x > MountedMaxSpanWU ? 1 : 0)
                    + (sz.y > MountedMaxSpanWU ? 1 : 0)
                    + (sz.z > MountedMaxSpanWU ? 1 : 0);
                if (fatAxes >= 2 || sz.x * sz.y * sz.z > MountedMaxMeshVolumeWU3)
                {
                    _censusUnionOversize++;
                    return; // architecture-scale — never raised by a foreign wall
                }
            }
            UnionEntry? entry = null;
            foreach (Segment seg in _live.Segments.Values)
            {
                if (ReferenceEquals(seg, owner) || !seg.HasBounds || seg.Anchor == null)
                    continue;
                // FADE-ELIGIBLE ONLY. A doorway never fades (user ruling 2026-08-02) and a
                // segment without a trusted room plane makes no decision at all, so neither has
                // a fade worth riding — and a doorway's arch must never drag dressing out with
                // it (user ruling 2026-08-07).
                if (seg.DoorRoot != null || !RoomDecisionValid(seg.RoomIndex))
                    continue;
                if (!UnionOverlapDepth(seg.Bounds, b, out float depth))
                    continue;
                entry ??= RentUnionEntry(owner, lane);
                entry.Hits.Add(new UnionHit(seg, depth));
            }
            if (entry == null)
            {
                _censusUnionNoOverlap++;
                return;
            }
            _live.MountedUnion[r] = entry;
            _live.UnionOwners.Add(owner);
            _censusUnionEligible++;
        }

        private UnionEntry RentUnionEntry(Segment owner, string lane)
        {
            UnionEntry e = _unionPool.Count > 0 ? _unionPool.Pop() : new UnionEntry();
            e.Owner = owner;
            e.Lane = lane;
            e.Hits.Clear();
            return e;
        }

        /// <summary>Do these two AABBs actually INTERSECT, allowing at most
        /// <see cref="MountedUnionSlackWU"/> of SEPARATION on every axis? <paramref name="depth"/>
        /// is the smallest signed per-axis overlap — how deep the prop reaches into the slab,
        /// negative when it is merely within the slack — which is the number the census prints so
        /// the tolerance can be argued with instead of trusted.</summary>
        private static bool UnionOverlapDepth(in Bounds seg, in Bounds prop, out float depth)
        {
            const float s = MountedUnionSlackWU;
            // The RAW signed overlap per axis: positive = the boxes interpenetrate by that much,
            // negative = they are separated by that much. The slack is applied ONCE, as the
            // acceptance threshold, and NOT folded into the reported number — a prop bolted flush
            // to its wall face measures ≈0 or a hair negative, and the census has to print that
            // honestly so the constant can be argued with rather than trusted.
            float ox = Mathf.Min(seg.max.x, prop.max.x) - Mathf.Max(seg.min.x, prop.min.x);
            if (ox <= -s) { depth = 0f; return false; }
            float oy = Mathf.Min(seg.max.y, prop.max.y) - Mathf.Max(seg.min.y, prop.min.y);
            if (oy <= -s) { depth = 0f; return false; }
            float oz = Mathf.Min(seg.max.z, prop.max.z) - Mathf.Max(seg.min.z, prop.min.z);
            if (oz <= -s) { depth = 0f; return false; }
            depth = Mathf.Min(ox, Mathf.Min(oy, oz));
            return true;
        }

        /// <summary>
        /// THE ONE READ. The fade this attachment piece must actually ride: the MAX of its
        /// owner's fade and of every fade-eligible segment its own AABB reaches into. O(1) plus a
        /// walk of a list that is empty for almost every prop; nothing is measured here and no
        /// segment is written.
        /// </summary>
        private float UnionFade(Renderer? r, Segment owner)
        {
            float own = owner.Fade;
            if (r == null || _live.MountedUnion.Count == 0 || !_live.UnionOwners.Contains(owner)
                || !_live.MountedUnion.TryGetValue(r, out UnionEntry? e))
                return own;
            float best = own;
            foreach (UnionHit h in e.Hits)
            {
                if (h.Seg.Fade > best)
                    best = h.Seg.Fade;
            }
            if (best > own)
                _unionRaisedPropFrames++;
            return best;
        }

        /// <summary>Does ANY piece of this lane read a higher fade than the segment's own? The
        /// appliers' <c>want == 0</c> early-out is a whole-segment decision and would otherwise
        /// return before a single piece could be looked at — which is defect (2) exactly: the
        /// pole's OWN wall is the one that has not faded.</summary>
        private bool LaneHasUnionFade(List<MountedProp> lane, Segment owner)
        {
            if (_live.MountedUnion.Count == 0 || !_live.UnionOwners.Contains(owner))
                return false;
            foreach (MountedProp p in lane)
            {
                if (p.Renderer != null && ForeignFade(p.Renderer) > owner.Fade)
                    return true;
            }
            return false;
        }

        /// <summary>The highest FOREIGN fade over this renderer, ignoring its owner — the term
        /// the union rule adds. Counts nothing: it is asked by the lane pre-tests, which run
        /// before any piece is driven.</summary>
        private float ForeignFade(Renderer r)
        {
            if (!_live.MountedUnion.TryGetValue(r, out UnionEntry? e))
                return 0f;
            float best = 0f;
            foreach (UnionHit h in e.Hits)
            {
                if (h.Seg.Fade > best)
                    best = h.Seg.Fade;
            }
            return best;
        }

        /// <summary>The foreign segment currently holding this renderer's fade up, or null.
        /// Used by the RELEASE warn so a piece let go over SOMEONE ELSE'S hole is named with the
        /// same severity as one let go over its owner's.</summary>
        private Segment? HottestUnionSegment(Renderer r, out float fade, out float depth)
        {
            fade = 0f;
            depth = 0f;
            if (_live.MountedUnion.Count == 0 || !_live.MountedUnion.TryGetValue(r, out UnionEntry? e))
                return null;
            Segment? best = null;
            foreach (UnionHit h in e.Hits)
            {
                if (h.Seg.Fade <= fade)
                    continue;
                fade = h.Seg.Fade;
                depth = h.Depth;
                best = h.Seg;
            }
            return best;
        }

        /// <summary>
        /// THE UNION RULE CENSUS — one line per rescan, and it must be able to disagree with the
        /// rule it reports on. It carries: how many renderers are taking a fade from a segment
        /// OTHER than their owner RIGHT NOW (named, with owner + owner's fade, the overriding
        /// segment + its fade, and the overlap depth that decided it), how many the rule makes NO
        /// difference to, the cap and how many names were dropped, and the two REFUSAL
        /// populations. Every number is read live at print time from the live segment fades.
        /// </summary>
        private void LogMountedUnion()
        {
            int applied = 0, noDiff = 0;
            _unionCensus.Clear();
            foreach (KeyValuePair<Renderer, UnionEntry> kv in _live.MountedUnion)
            {
                Renderer r = kv.Key;
                UnionEntry e = kv.Value;
                if (r == null || e.Owner == null)
                    continue;
                Segment? hot = HottestUnionSegment(r, out float foreign, out float depth);
                if (hot == null || foreign <= e.Owner.Fade)
                {
                    noDiff++;
                    continue;
                }
                applied++;
                if (_unionCensus.Count < MountedUnionListCap)
                {
                    string ownerName = e.Owner.Anchor != null ? e.Owner.Anchor.name : "<dead>";
                    string hotName = hot.Anchor != null ? hot.Anchor.name : "<dead>";
                    _unionCensus.Add(
                        $"'{r.name}'[{RendererKind(r)}, {e.Lane}] owner '{ownerName}' fade "
                        + $"{e.Owner.Fade:F2} → rides '{hotName}' fade {foreign:F2} "
                        + $"(AABB overlap depth {depth:F2} wu, of {e.Hits.Count} overlapping "
                        + "segment(s))");
                }
            }
            // The gate is on LIVE NUMBERS, never on a constant reason string: Apparance streams
            // the dressing in over several rescans, so the two refusal populations move for
            // several seconds after a scenario opens and the line follows them. The FIRST print
            // happens unconditionally (both watermarks start at -1), so a session in which this
            // rule finds nothing still says so once — a zero that was never printed and an
            // instrument that never ran look identical, and that has cost this project builds.
            bool changed = _censusUnionEligible != _lastLoggedUnionEligible
                || applied != _lastLoggedUnionApplied
                || _censusUnionOversize != _lastLoggedUnionOversize
                || _censusUnionNoOverlap != _lastLoggedUnionNoOverlap;
            if (!changed && _unionRaisedPropFrames == 0)
                return;
            _lastLoggedUnionEligible = _censusUnionEligible;
            _lastLoggedUnionApplied = applied;
            _lastLoggedUnionOversize = _censusUnionOversize;
            _lastLoggedUnionNoOverlap = _censusUnionNoOverlap;
            int dropped = applied - _unionCensus.Count;
            string named = _unionCensus.Count > 0
                ? string.Join("; ", _unionCensus)
                  + (dropped > 0
                      ? $" [list CAPPED at {MountedUnionListCap} — {dropped} override(s) NOT "
                        + "shown; the counts above are complete]"
                      : string.Empty)
                : "none right now";
            VRLog.Info(Name,
                $"UNION RULE: {applied} attachment renderer(s) are taking their fade from a "
                + $"segment OTHER than their owner, {noDiff} where the rule makes NO difference "
                + $"(the owner is already at or above every wall it reaches into), out of "
                + $"{_censusUnionEligible} renderer(s) that overlap any foreign fade-eligible "
                + $"segment at all: {named}. The overriding term is a real AABB INTERSECTION "
                + $"(slack {MountedUnionSlackWU:0.00} wu on every axis), never the "
                + $"{MountedLinkMaxXZ:0.00} wu ownership reach — a prop merely NEAR a hole keeps "
                + "its own wall's fade, a prop whose geometry is INSIDE the hole rides it "
                + "(user 2026-08-25: 'ohne die benachbarte Wand zu beeinflussen' — no segment's "
                + "fade, state, coverage or decision is written by this rule, it only reads "
                + $"them). REFUSED REGISTRATION: {_censusUnionOversize} dressing renderer(s) are "
                + $"architecture-scale (2 fat axes > {MountedMaxSpanWU:0.0} wu or volume > "
                + $"{MountedMaxMeshVolumeWU3:0.0} wu³) and may never be raised by a foreign wall, "
                + $"{_censusUnionNoOverlap} overlap nothing foreign. LANE COVERAGE, stated so a "
                + "gap between the two numbers below is readable: the mounted-dressing and "
                + "prop-unit-dressing lanes carry the rule WHOLE (their whole-segment early-out "
                + "is lifted, so a piece whose own wall is solid can still be taken by a wall it "
                + "hangs over — defect (2)); the foliage and asset-sibling lanes apply the MAX at "
                + "their driving call sites but keep their segment-scoped early-out, because "
                + "their release clears the whole FoliageProps/SiblingProps record set and undoes "
                + "the material swaps in one go. The wall BODY, the STACKED shell and the shared "
                + "CORNER piece are EXEMPT by design — the first two are the neighbouring wall "
                + "rather than something hanging on it, and the corner keeps its round-7 "
                + "Min(A.Fade, B.Fade) so it stays while either neighbour stands. SINCE THE LAST LINE the "
                + $"appliers actually RAISED a piece's fade {_unionRaisedPropFrames} prop-frame(s) "
                + $"across {_unionRaisedPieces} applier pass(es) — this reading 0 while the count "
                + "above is > 0 means the map is built and NO applier reads it, which is a "
                + "different defect from the rule not matching.");
            _unionRaisedPropFrames = 0;
            _unionRaisedPieces = 0;
        }

        /// <summary>Record a segment that has filled its dressing quota (see
        /// <see cref="MountedMaxPerSegment"/>) — once per segment per rescan.</summary>
        private void NoteMountedSaturated(Segment seg)
        {
            // PERF E (ModBuild 279) — THE CAP FIRST. This is called from inside the adoption
            // loop, once per (candidate x saturated segment), and it read `seg.Anchor.name` — an
            // interop call that allocates a managed string — and then walked the list linearly,
            // BEFORE asking whether the list could take another entry. Once the list holds 8 the
            // rest of the method cannot write: the dedupe `return` and the capped `Add` are both
            // no-ops, so returning here records exactly what returning below recorded.
            if (_mountedSaturated.Count >= 8)
                return;
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

        /// <summary>
        /// MODBUILD 268 — HOW FAR UP IS THE WALL, for a named leftover. The one measurement no
        /// hardware log has yet carried, and the one that decides whether the airborne bar at
        /// the mounted sweep can ever be opened for hangings without also opening it for the
        /// ice formation, the rigged skeleton and the light shaft.
        ///
        /// <para><b>WHY IT IS NEEDED, from the two logs and not from an opinion.</b> Every term
        /// already on that line was measured against the six subjects and every one of them is
        /// interleaved — must-FADE against must-STAY, in the ModBuild-267 log
        /// (<c>.planning/debug/second_logs/</c>):
        /// <list type="bullet">
        /// <item>FOOT over the room floor: hanging 0.44, curtain 0.33, shelf 0.90 — crystal
        ///   0.27/0.52, skeleton limbs 0.76/0.77/1.00, light shaft 0.47;</item>
        /// <item>TOP: hanging 2.28, curtain 2.97, shelf 1.31 — crystal 2.11/2.80, limbs
        ///   1.27/1.29, light shaft 7.69;</item>
        /// <item>XZ GAP to the nearest eligible wall, for every subject the capped NEAR-MISS
        ///   list actually names: hanging 0.00 and shelf 0.00 — crystal 0.00, light shaft
        ///   0.00, skull 0.00, geranium 0.00, knife 0.00. So "it touches its wall" separates
        ///   nothing. (The curtain and the skeleton limbs are NOT in that list — it caps at
        ///   <see cref="MountedRejectCap"/> — so their gap is unmeasured, not 0.00.)</item>
        /// <item>RENDERER TYPE: only the hanging is skinned; the curtain and the shelf are
        ///   plain meshes, exactly like the crystal and the light shaft;</item>
        /// <item>WALL-GENERATOR PROVENANCE: all nine are tagged <c>[WALL MEMBER]</c> by the
        ///   leftover classifier, which IS
        ///   <c>GetComponentInParent&lt;ProceduralWall&gt;() != null</c>, so the unbounded
        ///   provenance climb answers YES for every one of them;</item>
        /// <item>the ModBuild-266/267 <c>wallCut</c> term ("a WALL sits immediately above this
        ///   unit inside the unit walk's four-level window"). <b>CORRECTED IN ModBuild 269 —
        ///   the claim written here was read off a truncated list.</b> It said the 267 log
        ///   reads <c>under a wall</c> ZERO times and therefore "does not fire at all in this
        ///   scenario". The phrase is indeed absent, but only from the SUBJECT ROLL-CALL, which
        ///   <c>LogStandingPropCensus</c> cuts at a 2200-character budget and ends with an
        ///   ellipsis — 26 of its ≤48 names survive. The census's own COUNTER on the same line
        ///   says otherwise: <c>6 unit(s) refused by this term this rescan</c> in 20 of the 23
        ///   STANDING PROP prints (2 and 4 in the other three). So the term fires; what is
        ///   unknown is whether it fires on any of the six subjects, because none of them
        ///   survives the truncation. Absence from a list that ends in "…" is not evidence, and
        ///   this is the third clause in this subsystem to have been read that way.</item>
        /// </list>
        /// A BOUNDED provenance window is the one term left untried, and the only evidence for
        /// or against it is the DEPTH of the ProceduralWall above each subject — which no log
        /// prints. The 266 doc asserts the hangings sit under <c>Wall N/Generated Content/…</c>
        /// and the 267 doc asserts the crystal sits under a tile's <c>Generated Content/Full/…</c>
        /// with the wall far above; the only path either log actually prints is the shelf's
        /// (<c>Wall 4/Generated Content/PCG_Test_Feature_Small_2/CR_ST_Shelves_Stone_Wood</c>,
        /// depth 3). This makes the rest measurable instead of asserted.</para>
        ///
        /// <para>COST: a climb of at most <see cref="WallProvenanceProbeLevels"/> transforms,
        /// for NAMED leftovers only — the list is capped at <see cref="MountedLeftoverCap"/> =
        /// 40 — at rescan cadence, never per frame and never a scene sweep. MULTIPLAYER: reads
        /// local scene hierarchy for a log string; decides nothing and goes nowhere near the
        /// wire.</para>
        /// </summary>
        private const int WallProvenanceProbeLevels = 12;

        private static string WallProvenanceNote(Renderer r)
        {
            Transform? t = r.transform;
            for (int depth = 0; t != null && depth <= WallProvenanceProbeLevels; depth++)
            {
                if (t.GetComponent<ProceduralWall>() != null)
                {
                    return $", ProceduralWall '{t.name}' {depth} level(s) above it";
                }
                t = t.parent;
            }
            return t == null
                ? ", NO ProceduralWall anywhere above it"
                : $", no ProceduralWall within {WallProvenanceProbeLevels} levels";
        }

        /// <summary>
        /// MODBUILD 269 — THE SECOND BOUNDED-PROVENANCE CANDIDATE, MEASURED IN THE SAME BREATH.
        ///
        /// <para>ModBuild 268 added the ProceduralWall DEPTH above a named leftover because a
        /// BOUNDED provenance window was "the one term left untried". It is not the only one: a
        /// bounded window keyed on DEPTH needs a constant nobody can choose yet, whereas the
        /// unit-affinity map this file already builds every rescan
        /// (<see cref="BuildMountedUnitHomes"/>, ModBuild 258) answers a strictly stronger
        /// question with no constant at all — <i>does a wall segment already own OTHER renderers
        /// of this renderer's own prop unit?</i> The unit root comes from the prop-unit walk's
        /// own four-level window, never from
        /// <c>GetComponentInParent&lt;ProceduralWall&gt;()</c>, so it is subject to the same
        /// bound the 267 round argued for and to none of that probe's scene-root reach.</para>
        ///
        /// <para><b>WHY BOTH COLUMNS, AND WHY NEITHER IS ACTED ON YET.</b> The ModBuild-267 log
        /// prints a hierarchy path for exactly ONE of the six subjects (the shelf,
        /// <c>Wall 4/Generated Content/PCG_Test_Feature_Small_2/CR_ST_Shelves_Stone_Wood</c>,
        /// depth 3). It prints none for the ice crystal, the light shaft or the skeleton limbs,
        /// so neither term is decidable from it and no threshold may be chosen from it — four
        /// shape terms have already been shipped and falsified in this subsystem. Printing both
        /// columns on the same entries means ONE hardware log now separates them:</para>
        /// <list type="bullet">
        /// <item>if the must-FADE subjects read a small depth AND a unit home while the must-STAY
        ///   ones read neither, either term works and the cheaper one wins;</item>
        /// <item>if the crystal reads a small depth but NO unit home, the depth window is the
        ///   wrong lever and unit affinity is the right one — which is the outcome the shelf's
        ///   own hierarchy predicts, since its unit root
        ///   (<c>PCG_Test_Feature_Small_2</c>) is shared with <c>Blocks (1)</c> and
        ///   <c>Pillar</c>, both logged as <c>wall renderer of 'Wall 4'</c>;</item>
        /// <item>if the crystal reads a unit home, unit affinity is refuted outright and must be
        ///   withdrawn rather than retuned.</item>
        /// </list>
        ///
        /// <para>COST: one dictionary probe plus the memoised unit walk, for NAMED leftovers only
        /// (capped at <see cref="MountedLeftoverCap"/> = 40), at rescan cadence. Reads nothing
        /// the sweep has not already computed this rescan. MULTIPLAYER: a log string; decides
        /// nothing and touches no wire field.</para>
        /// </summary>
        private string PropUnitHomeNote(Renderer r)
        {
            Transform? root = StandingFloorUnitRootOf(r);
            if (root == null)
                return ", no prop unit at all (the four-level walk found no unit root)";
            if (!_mountedUnitHome.TryGetValue(root, out Segment? home) || home.Anchor == null)
            {
                return $", prop unit '{root.name}' — NO wall segment owns any renderer of it";
            }
            return $", prop unit '{root.name}' is owned by wall '{home.Anchor.name}' "
                   + $"(fade {home.Fade:F2})";
        }

        /// <summary>
        /// PERF E (ModBuild 279) — <see cref="NoteMountedReject"/>'s OWN FIRST TEST, hoisted so a
        /// call site can decide whether to BUILD its reason string at all.
        ///
        /// <para>When this is false <see cref="NoteMountedReject"/> returns on its first line
        /// without touching a counter, a list or a renderer, so the <c>why</c> it was handed is
        /// read by nothing. Four of the eight reject sites in the mounted sweep build an
        /// INTERPOLATED reason — on net472 that is a <c>string.Format(string, object[])</c>, an
        /// array plus a box per float — and the sweep walks every candidate in the scene, so
        /// those four were formatting and discarding strings for every renderer that is simply
        /// not standing near a wall. Same shape and same argument as
        /// <see cref="StructuralSkipArmed"/> two hundred lines above, which PERF S2 applied to
        /// the structural skips and not to these.</para>
        ///
        /// <para>THIS IS NOT A CAP TEST AND MUST NOT BECOME ONE. It is the NEAR-MISS WINDOW: a
        /// candidate outside it is not counted anywhere, which is why eliding its sentence is a
        /// no-op. <c>_mountedRejects</c>' own 24-entry cap is deliberately NOT hoisted here —
        /// <c>_censusMountedRejected</c>, <c>_censusMountedLeftover</c> and the leftover CLASS
        /// histogram keep counting past it, and a population count truncated at a presentation
        /// cap is the "a truncated list is not absence" defect this file already carries a
        /// ledger entry for.</para>
        /// </summary>
        private static bool MountedRejectReasonWanted(float gap) => gap <= MountedNearMissXZ;

        /// <summary>The placeholder handed to <see cref="NoteMountedReject"/> when
        /// <see cref="MountedRejectReasonWanted"/> is false. It can never be printed — the callee
        /// returns before reading it — and it says so rather than being an empty string, so a
        /// copy of it appearing in a log is immediately legible as an instrument bug.</summary>
        private const string MountedRejectReasonNotBuilt =
            "<no reason was built: this candidate is outside the near-miss window, where the "
            + "reject is not recorded at all>";

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
            Segment faded = _leftoverFadedNear;
            // THE VERDICT, NOT JUST THE POPULATION (ModBuild 262). The 260 log printed this list
            // 122 times, steady at 71 renderers, and NOT ONE of the 4,880 name slots said whether
            // the user minds: ~21 of the 40 named per line were already ALLOWED by the reject
            // text's own words ("reads as floor-supported", FIGURE, "STANDS ON THE FLOOR") while
            // the ~19 architecture-scale ones carried a foot height rounded to one decimal and no
            // obstruction measurement at all. Same three classes and same two threshold-free
            // definitions as the split-run sweep — see ClassifyLeftover; nothing new is tuned.
            //
            // WHICH ROOM. The one the FADED segment beside it belongs to: that is the floor the
            // piece is standing over in the photograph, and the samples he is looking at when the
            // wall goes. Falls back to "no anchored floor" when that segment has no valid room,
            // which is reported as its own class rather than silently read as ALLOWED.
            string cls = ClassifyLeftover(c, faded.RoomIndex, out int blockedSamples,
                                          out float foot, out float top, out int visibleSamples);
            _mountedLeftoverByClass.TryGetValue(cls, out int clsSeen);
            _mountedLeftoverByClass[cls] = clsSeen + 1;
            // ALLOWED gets its own list for the reason the 260 log needed and did not have: a
            // large healthy population must never be readable as a large defect. Both lists are
            // capped and the string is built only when one of them has room — a 71-renderer
            // population would otherwise allocate 71 long strings per rescan to throw most away.
            bool allowed = cls == "ALLOWED";
            List<string> into = allowed ? _mountedLeftoverAllowed : _mountedLeftovers;
            if (into.Count >= MountedLeftoverCap)
                return;
            string wall = faded.Anchor != null ? faded.Anchor.name : "<dead>";
            into.Add(
                (allowed ? string.Empty : $"[{cls}] ")
                + $"'{c.name}'[{RendererKind(c)}] foot {foot:F2} wu / top {top:F2} wu over room "
                + $"{faded.RoomIndex}'s floor, hides {blockedSamples} of {visibleSamples} in-view "
                + $"playable-tile sample(s){LeftoverExemptionNote(c)}"
                // ModBuild 268: the DEFECT entries carry the wall-provenance DEPTH, because
                // every other term on this line was measured against the six named subjects
                // and all of them are interleaved. See WallProvenanceNote for the table.
                + (allowed ? string.Empty : WallProvenanceNote(c))
                // ModBuild 269: and the SECOND bounded-provenance candidate beside it, so one
                // log separates the two instead of measuring one and inferring the other.
                + (allowed ? string.Empty : PropUnitHomeNote(c))
                + ", "
                + $"DRAWING {_leftoverFadedGap:F2} wu from "
                + $"'{wall}' whose fade is {faded.Fade:F2} — not adopted because: {why}");
        }

        /// <summary>ModBuild 262: the mounted leftover population by the user's three classes,
        /// complete and untruncated — the count the name list cannot carry. See
        /// <see cref="ClassifyLeftover"/>.</summary>
        private readonly Dictionary<string, int> _mountedLeftoverByClass = new();

        /// <summary>ModBuild 262: the leftovers the user expressly permits, listed apart from the
        /// defects so their number can never be mistaken for one.</summary>
        private readonly List<string> _mountedLeftoverAllowed = new();

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
            _lastLoggedMountedHandover = _censusMountedHandover;
            _lastLoggedMountedWallBuilt = _censusMountedWallBuilt;
            _lastLoggedMountedWallBuiltCarried = _censusMountedWallBuiltCarried;
            _lastLoggedMountedWallBuiltBelowBar = _censusMountedWallBuiltBelowBar;
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
                + $"{_censusMountedHandover} HANDED OVER to another lane's owner without ever "
                + $"being switched back on — ModBuild 265: a change of owner may not make a piece "
                + $"more visible, so the leavers loop concedes the piece instead of restoring it; "
                + $"{_censusMountedUnitHome} attached to the wall that owns their PROP UNIT "
                + $"rather than to the nearest one — ModBuild 258, wandproblem3.jpg: one prop "
                + $"with two owners is one prop that half-survives every fade; "
                // MODBUILD 266 — THE ACCEPTANCE NUMBER for Flaggen.jpg, read off the sweep and
                // not off the ledger. It must be > 0 in any scenario whose leftover audit used
                // to name a hanging, and the [FLOATING] / [WALL MEMBER] classes on the LEFTOVER
                // line must stop naming one. If this reads 0 while a hanging is still named
                // there, the exemption never fired and the refusal is elsewhere — which is a
                // different defect and this line says so instead of implying success.
                + $"{_censusMountedWallBuilt} adopted ONLY because the WALL GENERATOR built them "
                + "(inside a ProceduralWall subtree, no ActorBehaviour/CInteractableActor "
                + "anywhere above them) — ModBuild 266, Flaggen.jpg: 'Ich möchte, dass die "
                + "Flagge inklusive der Stange vollständig mit faded'. These are the pieces the "
                + "renderer-type test, the round-7 figure gate or the standing rule's FIGURE arm "
                + "used to refuse; each is tagged [WALL-BUILT] in the list above. A figure is "
                + "never among them: every figure the game spawns is parented to the BOARD root, "
                + "so no ProceduralWall is on its ancestor chain, and the actor pair is an "
                + "absolute veto on top of that; "
                // MODBUILD 268 — THE CARRY HALF, and the acceptance number for this round. The
                // count above is set at the ADOPTION, which happens ONCE per piece; every later
                // rescan the sticky loop carries it instead. Until this build that loop asked
                // the bare round-7 figure guard and handed the piece straight back — 165 x
                // 'RELEASED OVER A FADED WALL … term that failed: FIGURE — never carried' in
                // the ModBuild-266 log, 86 'EN_CR_Hanging_01_Cloth_Post' + 79
                // 'CR_BT_BanditBanner_Wall', with no second reason in the histogram — which is
                // why the count above oscillated between 12-18 and 0 rather than settling.
                + $"{_censusMountedWallBuiltCarried} of them CARRIED sticky over that same "
                + "guard rather than released (ModBuild 268 — the fifth exemption site, the "
                + "only one that lets a piece go instead of refusing it). Read this against "
                + "the RELEASED OVER A FADED WALL warn: this > 0 while that names no hanging "
                + "is the whole of ModBuild 268; "
                // MODBUILD 271 — THE SIXTH SITE, THE AIRBORNE BAR. Its own number, never folded
                // into the 266 count: the two lifts have different blast radii and the crystal
                // ruling has to stay auditable from one grep. This > 0 while the leftover line
                // stops naming 'EN_CR_Hanging_01_Mesh'/'EN_CR_Curtain_Mesh' is the acceptance;
                // this > 0 while the list above names 'CV_Ice_Crystal_Form_02/03' or
                // 'LightShaft_Prefab (1)' with the same tag is the falsification, and the rule
                // must then be withdrawn rather than retuned (user ruling 2026-08-24).
                + $"{_censusMountedWallBuiltBelowBar} adopted BELOW the airborne bar "
                + $"{MountedClearanceWU:0.0} wu because the wall generator built them AND the "
                + "four-level walk resolved a prop unit for them — ModBuild 271, tagged "
                + $"[WALL-BUILT BELOW THE BAR] above){full}.");
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
            if (_censusMountedLeftover == 0 && _runLeftover == 0)
                return;
            if (_censusMountedLeftover == 0)
            {
                // Only the ModBuild 259 class fired. Printed on its own rather than folded into
                // a line whose every clause is about airborne dressing.
                VRLog.Warn(Name,
                    $"LEFTOVER OVER A FADED WALL: {_runLeftover} SPLIT-RUN PIECE(S) are actually "
                    + "drawing (renderer enabled + active in hierarchy — read off the renderer, "
                    + "never off our ledger) while the wall run they belong to is faded. This is "
                    + "the 2026-08-24 photograph (neues_wandproblem.jpg): "
                    + "\"nur manche Bäume einzeln und Teilwände bleiben stehen\". ModBuild 261: "
                    + "a ZERO here is NO LONGER the claim — the same day's refinement allows a "
                    + "piece that stands on the floor and hides nothing to stay, so the verdict "
                    + "is the class split, not the count."
                    + SplitRunPiecesClause());
                return;
            }
            _mountedLeftoverByClass.TryGetValue("FLOATING", out int mlFloating);
            _mountedLeftoverByClass.TryGetValue("OBSTRUCTING", out int mlObstructing);
            _mountedLeftoverByClass.TryGetValue("WALL MEMBER", out int mlWallMember);
            _mountedLeftoverByClass.TryGetValue("ALLOWED", out int mlAllowed);
            int mlOther = _censusMountedLeftover - mlFloating - mlObstructing - mlWallMember
                          - mlAllowed;
            VRLog.Warn(Name,
                $"LEFTOVER OVER A FADED WALL: {_censusMountedLeftover} renderer(s) are actually "
                // THE VERDICT FIRST (ModBuild 262). The 260 log printed this count 122 times,
                // steady at 71, with no statement anywhere on the line about whether the user
                // minds any of them — so the number was quoted for two rounds as if it were a
                // defect population, and ~21 of the 40 it named per line were ALLOWED by the
                // line's own reject text. FLOATING and OBSTRUCTING are the defect; ALLOWED is
                // what he expressly permits (the well, the low stone formation), and a large
                // ALLOWED number here is a HEALTHY reading.
                + $"— BY THE USER'S THREE CLASSES (ruling 2026-08-24): {mlFloating} FLOATING "
                + $"(foot above the {WallStandingProp.FootBandWU:0.0} wu floor band — the wall "
                + $"was holding it up and is gone), {mlObstructing} OBSTRUCTING (on the floor and "
                + "still hiding ≥1 frustum-visible playable-tile sample, measured with the fade "
                + $"trigger's own ray test), {mlWallMember} WALL MEMBER (the wall generator "
                + "built it — inside a ProceduralWall subtree, above the ground band, outside "
                + "every water rect; a defect whatever its height and whatever it hides, and the "
                + "class that must read ZERO), "
                + $"{mlAllowed} ALLOWED (belongs to no wall, on the floor, hides nothing)"
                + (mlOther > 0
                    ? $", {mlOther} UNJUDGED — an input was missing (no anchored floor plane, no playable-tile grid for the room, or no sample of it in view this tick); the class tally below names which, and NONE of them is read as ALLOWED"
                    : string.Empty)
                + $". ALLOWED, named separately (up to {MountedLeftoverCap}): ["
                + string.Join("; ", _mountedLeftoverAllowed)
                + "]. A renderer tagged [EXEMPT] carries a standing user ruling of its own (the "
                + "fountain 2026-08-09, the doorway arch 2026-08-02) and stays whatever its "
                + "geometry says. The population itself: renderers "
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
                + $"({_censusMountedCarriedParticles} this rescan). DEFECT names only — FLOATING "
                + $"and OBSTRUCTING and WALL MEMBER, up to {MountedLeftoverCap} of the "
                + $"{mlFloating + mlObstructing + mlWallMember + mlOther} non-ALLOWED (the "
                + "ALLOWED ones are "
                + "listed above, which is why a plain 'total minus names' subtraction would "
                + "over-count the omission — the same arithmetic slip ModBuild 261 fixed on the "
                + "SPLIT-RUN clause): "
                + string.Join("; ", _mountedLeftovers)
                + (mlFloating + mlObstructing + mlWallMember + mlOther > _mountedLeftovers.Count
                    ? $"; … ({mlFloating + mlObstructing + mlWallMember + mlOther - _mountedLeftovers.Count} "
                      + "more defect(s) not named)"
                    : string.Empty)
                + ". This is the shape of the 2026-08-24 report (wandproblem3.jpg): the wall is "
                + "gone and the thing that hung on it is not — but ONLY for the FLOATING and "
                + "OBSTRUCTING rows above."
                // ModBuild 259's own class, on the same line so one grep covers both: a whole
                // PIECE of a split wall run still drawing while its run is faded. ModBuild 261:
                // the count is no longer a verdict — see SplitRunPiecesClause.
                + SplitRunPiecesClause());
        }

        /// <summary>
        /// The <c>SPLIT-RUN PIECES</c> trailer, and why it no longer reads as a pass/fail count.
        ///
        /// <para>ModBuild 259 wrote it as "0 = every piece of every faded run went with it", and
        /// that was the right bar for "alles muss faden". The user's 2026-08-24 REFINEMENT retired
        /// it: <i>"Es gibt Dinge die stehen bleiben dürfen … Aber es dürfen keine Elemente
        /// 'herumfliegen' … Und es muss niedrig genug sein, dass es nicht stört."</i> A piece that
        /// stands on the floor and hides no playable tile is now ALLOWED, so a non-zero count is
        /// not a defect and the old wording would read as one. <c>_runLeftover</c> counts all three
        /// classes; <c>_runLeftoverNames</c> deliberately holds only FLOATING and OBSTRUCTING (the
        /// ALLOWED ones go to <c>_runLeftoverAllowed</c>), which is why the old
        /// "<c>N − names.Count</c> more" arithmetic over-counted the omission. Both are stated
        /// here instead of inferred.</para>
        ///
        /// <para>MODBUILD 264 narrowed ALLOWED again: a piece now also has to belong to NO wall to
        /// earn it (see <see cref="FadeDriver.IsWallGeneratedMember"/>), and the pieces that fail
        /// that test are counted and named as WALL MEMBER — the class that must read zero.
        /// Nothing else on this line moved.</para>
        /// </summary>
        private string SplitRunPiecesClause()
        {
            if (_runLeftover == 0)
            {
                return " SPLIT-RUN PIECES: 0 drawing beside a faded run. Since the 2026-08-24 "
                    + "refinement this is no longer the acceptance bar — a piece standing on the "
                    + "floor that hides nothing may stay — so read the SPLIT-RUN LEFTOVER line's "
                    + "FLOATING and OBSTRUCTING counts, not this one.";
            }
            _runLeftoverByClass.TryGetValue("FLOATING", out int floating);
            _runLeftoverByClass.TryGetValue("OBSTRUCTING", out int obstructing);
            _runLeftoverByClass.TryGetValue("WALL MEMBER", out int wallMember);
            _runLeftoverByClass.TryGetValue("ALLOWED", out int allowed);
            int other = _runLeftover - floating - obstructing - wallMember - allowed;
            return $" SPLIT-RUN PIECES: {_runLeftover} RENDERER(S) across {_runLeftoverSegments} "
                + "piece(s) still DRAWING beside a faded run — "
                + $"{floating} FLOATING, {wallMember} WALL MEMBER, {obstructing} OBSTRUCTING, "
                + $"{allowed} ALLOWED"
                + (other > 0 ? $", {other} UNJUDGED (an input was missing — see the SPLIT-RUN LEFTOVER class tally for which)" : string.Empty)
                + ". FLOATING, WALL MEMBER and OBSTRUCTING are the defect (user rulings "
                + "2026-08-24); ALLOWED standing on the floor and hiding nothing is what he "
                + "expressly permits. WALL MEMBER is the ModBuild-264 class and it is the one "
                + "that must now read ZERO: a piece the wall generator BUILT, still drawing while "
                + "its run is gone, is a defect whatever its height and whatever it hides — which "
                + "is what stops this line reporting '99 x ALLOWED' at a photograph of a solid "
                + $"hedge. The {_runLeftoverNames.Count} name(s) below are those three defect "
                + "classes only, capped at " + MountedLeftoverCap + " — the ALLOWED pieces are "
                + "named on the SPLIT-RUN LEFTOVER line, which also carries the complete "
                + "per-reason distribution: " + string.Join("; ", _runLeftoverNames) + ".";
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
