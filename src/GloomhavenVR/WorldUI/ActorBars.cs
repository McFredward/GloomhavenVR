using System.Collections.Generic;
using BepInEx.Configuration;
using GloomhavenVR.Board.FigureGrab;
using GloomhavenVR.Core;
using HarmonyLib;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// True world-space actor HP/effect bars (ROADMAP P3c #4).
///
/// The game's "worldspace" bars are fake: one shared Screen-Space canvas whose
/// panels are re-projected every LateUpdate via <c>WorldToScreenPoint</c>
/// (UI-ARCH §3.3). In VR that plane is head-locked garbage. This class adopts every
/// <c>WorldspacePanelUIController</c> (per-actor panel holding HealthBar/EffectsBar/
/// ShieldBar/AttackModBar/InfoBar), moves it onto its own world-space host canvas
/// above the miniature and billboards it to the HMD. Size is measured in REAL MILLIMETRES AT
/// THE EYE and tuned by [WorldUI] BarSizeScale, following the table zoom inside the fixed
/// <see cref="ZoomFollowMin"/>–<see cref="ZoomFollowMax"/> band (see <see cref="ResolveZoomFollow"/>);
/// the legacy distance-growth is opt-in ([WorldUI] BarFixedSize, test #14 item 4).
/// The DATA flow (UpdateHealth/UpdateEffects/ShowDamage/...) is untouched — the game
/// keeps feeding the very same components.
///
/// TRANSFORM OWNERSHIP (UI-ARCH §9.5 LateUpdate-fight): the game's per-frame writers
/// are prefix-skipped for ADOPTED panels only —
/// verified via ilspycmd (GH.Runtime.dll, WorldspaceDisplayPanelBase):
/// <code>
///   public void TrackCharacter()      // IL 318 B — WorldToScreenPoint projection
///   private void LateUpdate()         // calls TrackCharacter() + distance scaling
/// </code>
/// Both belong to <c>WorldspaceDisplayPanelBase</c>; the LateUpdate skip also stops
/// the screen-space scaling curve from fighting our world scale. Panels NOT adopted
/// (other subclasses, VR off) run 100% vanilla — the prefixes return true.
///
/// Occlusion ordering (WorldspaceUITools.OrderWorldspaceUIPanels) keeps running; it
/// only re-sorts sibling order, which is meaningless-but-harmless once each panel is
/// alone under its own host canvas (real depth testing orders world canvases).
/// </summary>
internal static class ActorBars
{
    /// <summary>
    /// Fixed zoom pushed to each adopted panel so its <c>HealthBar</c> segments itself
    /// (item 5a part 1). The flat game feeds <c>WorldspacePanelUIController.OnUpdatedZoom</c>
    /// from the RTS camera zoom via a UnityEvent wired only in the prefab/scene — there is
    /// NO code caller (verified: the only references to <c>OnUpdatedZoom</c> in the
    /// decompiled sources are the two method definitions). That event never fires in VR, so
    /// <c>HealthBar.zoom</c> stays at its -1 sentinel and <c>AdjustHealthBarMarks</c>
    /// early-returns (HealthBar.cs:205) — every bar keeps the prefab-default marks and looks
    /// identically segmented.
    ///
    /// Pushing any zoom >= 0 leaves the sentinel and lets <c>AdjustHealthBarMarks(maxHealth)</c>
    /// (called from <c>UpdateHealth</c>) pool <c>maxHealth-1</c> division marks, giving the
    /// per-character segment count the flat game shows. The value must be non-zero: the
    /// controller's own idempotence guard (<c>Mathf.Abs(_zoom - zoom) &lt; 0.0001f</c>,
    /// WorldspacePanelUIController.cs:786) skips the call when zoom equals its 0f default.
    /// 1f also drives the health/shield-root counter-scale in the same method — harmless in
    /// VR because we prefix-skip the distance-scaling LateUpdate, so the bar transform stays
    /// at prefab scale and the counter-scale resolves to identity.
    ///
    /// The exact zoom only selects which <c>HealthZoomConfigUI</c> styles the marks (widths /
    /// every-5th emphasis); the segment COUNT is <c>maxHealth-1</c> at any zoom >= 0. A
    /// constant keeps every VR bar consistent since our board-space bars are a fixed size and
    /// have no live zoom of their own.
    /// </summary>
    private const float BarZoom = 1f;

    /// <summary>
    /// Host-canvas sortingOrder for adopted actor bars — the BASE tier (0), the same tier every
    /// other converted host uses, and NOT a statement about who occludes whom.
    ///
    /// <para>TRANSPARENCY ROUND: this is the CONVERSION TIER only, not the draw order. A bar's live
    /// <c>Canvas.sortingOrder</c> is rewritten every LateUpdate from its eye distance like every
    /// other converted panel's (CanvasConversion.8.Order.cs), so a bar in FRONT of the Infotafel is
    /// painted after it and visible, and a bar behind it is painted before it and covered — the
    /// user ruling that perspective must be respected everywhere, now delivered by ORDER rather
    /// than by a depth stamp. The stamp is gone precisely because a bar's stamp was a box around
    /// its segments and glyphs, and that box cut a rectangle out of the info panel behind it
    /// (healthbar_problem.jpg); the measured-ink round only shrank that box by 10 % on hardware.</para>
    ///
    /// <para>Naming the value still earns its keep: the tier is what breaks a tie between two
    /// panels whose distances are indistinguishable, so leaving bars on the BASE tier stops a
    /// future 'give the bars a dominant order' edit from putting a bar in front of a menu it is
    /// coplanar with.</para>
    /// </summary>
    private const int BarHostSortingOrder = 0;

    /// <summary>
    /// The bar's SHIPPED size, in uGUI pixels per <see cref="WorldUIConfig.CanvasScaleMm"/>: one bar
    /// pixel is <c>CanvasScaleMm × 0.35</c> millimetres AT THE EYE — 0.35 mm at the shipped 1 mm/px
    /// canvas scale. It is the base every size dial below multiplies, and it is expressed in real
    /// millimetres rather than world units on purpose (see <see cref="ResolveZoomFollow"/>).
    /// </summary>
    private const float BarPixelSize = 0.35f;

    /// <summary>
    /// The table zoom at which <c>[WorldUI] BarSizeScale = 1</c> means "exactly the size the bars
    /// shipped at": the mod's own default pinch multiplier. Anchoring the follow here — rather than
    /// at the unzoomed base scale — is what makes the shipped defaults a NO-OP for a player sitting
    /// at the shipped zoom, so this feature changes the look only for someone who has zoomed away
    /// from it or who touches the dials.
    /// </summary>
    private const float ReferenceScaleMultiplier = Defaults.SavedScaleMultiplier;

    /// <summary>
    /// How far the table zoom may carry a bar BELOW / ABOVE the size <c>[WorldUI] BarSizeScale</c>
    /// asks for. Constants, not dials — this pair used to be <c>[WorldUI] BarZoomMinScale</c> and
    /// <c>BarZoomMaxScale</c>, and they are REMOVED (user ruling 2026-08-13: "Mindest und
    /// Maximalgröße der Lebensbalken haben keinen sehbaren einfluss. Es macht irgendwas, aber man
    /// versteht nicht wirklich was - ziemlich unintuitiv").
    ///
    /// <para>WHY THEY COULD NOT WORK AS DIALS. They never clamped a SIZE; they clamped the
    /// intermediate FOLLOW factor of <see cref="ResolveZoomFollow"/>, which is
    /// <c>ReferenceScaleMultiplier ÷ live pinch multiplier</c> and therefore exactly 1.0 whenever
    /// the player sits at the shipped table zoom — strictly inside 0.7…1.5. At that zoom NEITHER
    /// bound is reachable, so moving either dial changed nothing whatsoever; his own tuned cfg has
    /// <c>SavedScaleMultiplier = 3.368283</c> against a shipped reference of 3.3683, i.e. a follow
    /// of 1.000. He would have had to pinch past 4.81× (floor) or below 2.25× (ceiling) before a
    /// single pixel moved. A dial whose effect is invisible at the setting it ships at is not a
    /// setting, and the standing settings ruling only allows optional content and comfort.</para>
    ///
    /// <para>WHAT IS KEPT. The GUARANTEE he asked for ("ein minimum und maximum der Größe, damit
    /// sie sich trotz zoomen nie über die Grenzen hinaus skalieren können") is exactly this band,
    /// and it stays — unconditionally, on BOTH size paths, at the two values that shipped. At the
    /// widest zoom the mod allows (0.1×…12× of base while [Comfort] FreeMovement is on) the raw
    /// follow runs 0.28…33.7; without the band the bars would be 3.6× too small when the table is
    /// pushed away and 33× too large when it is pulled in. The band is what keeps them readable at
    /// every zoom, which is why it is code and not configuration.</para>
    /// </summary>
    private const float ZoomFollowMin = 0.7f;

    /// <summary>Ceiling of the zoom-follow band; see <see cref="ZoomFollowMin"/>.</summary>
    private const float ZoomFollowMax = 1.5f;

    private sealed class Adopted
    {
        public WorldspacePanelUIController Controller = null!;
        public ConvertedPanel Panel = null!;

        /// <summary>
        /// BOARD-SPACE (world-unit) anchor height above the TRACK point, cached at
        /// adopt (P6 fix #4): derived from the miniature's renderer bounds, so the
        /// bar clears the mini at EVERY diorama zoom — a real-meter offset (the old
        /// 0.045 × worldScale) shrank in board units when the player pinch-zoomed
        /// the table larger and sank the bars into the miniatures.
        /// </summary>
        public float AnchorOffsetWU;

        /// <summary>
        /// How many more times <see cref="ResampleAnchor"/> may re-measure this bar's anchor
        /// before latching it forever, and when the next of those samples is due. See
        /// <see cref="AnchorSampleBudget"/> for why one measurement at adopt is not enough.
        /// </summary>
        public int AnchorSamplesLeft;

        /// <summary>Unscaled time of this bar's next anchor resample.</summary>
        public float NextAnchorSample;

        /// <summary>
        /// Is the actor this bar tracks an ATTACHED-PROP actor — the invisible
        /// <c>PropDummyObject</c> the rule library gives a prop configured for health? Decided ONCE
        /// at adopt, from a field that cannot change for the life of an actor
        /// (<c>CObjectActor.AttachedProp</c>).
        ///
        /// <para>It is a field rather than a call because <see cref="ResampleAnchor"/> consults it
        /// on the frame its budget runs out, i.e. on EVERY frame for the rest of the session, for
        /// every bar on the board. That is precisely the shape of cost this project has shipped
        /// twice by calling something "near-free"; a bool test is not.</para>
        /// </summary>
        public bool AttachedPropActor;

        /// <summary>
        /// The <see cref="ActorBehaviour"/> this bar tracks, resolved from the controller's
        /// tracked figure at adopt (item 6): lets <see cref="LateTick"/> query
        /// <see cref="HeldFigures.Owns"/> so a bar is HIDDEN while its mini is held in the hand
        /// (redundant with the docked info panel) and shown again on release. May be null if the
        /// figure was not yet resolvable at adopt — re-resolved lazily while something is held.
        /// </summary>
        public ActorBehaviour? Actor;

        // ---- depth-test state ([WorldUI] BarsOccluded gate) ----------------------------------
        // Health bars are world-space UI: Unity's UI shaders declare `ZTest [unity_GUIZTestMode]`,
        // which effectively resolves to Always, so bar pixels bleed through walls. The old fix
        // CORRECTED ModBuild 396 — "effectively resolves to Always" IS WRONG, and the hardware
        // settled it. Three other files in this mod (OnTopUiGraphics, HandGhost,
        // WindowMaterialiseDebris) all state the global is LEqual, and the user's 2026-09-03
        // ghost-hand report is the falsifier: BarsOccluded ships FALSE, so these bars run on the
        // GLOBAL value — and they were being ERASED by a back-face depth stamp the ghost hand wrote
        // at renderQueue 3099. A fragment can only be rejected by depth if it is depth-TESTED, so
        // the global is LEqual, not Always. The per-instance LEqual copy described below is still
        // correct and still the mechanism for the [WorldUI] BarsOccluded gate; what is wrong is only
        // this sentence's claim about the DEFAULT. Left in place rather than reworded because the
        // paragraph is quoted from elsewhere; read this clause as superseding it.
        // (line-of-sight probe + SetActive hide) TOGGLED the bar and is retired per user mandate.
        // Instead every Graphic under the adopted host gets a PER-INSTANCE copy of its material
        // with unity_GUIZTestMode forced to LEqual — the per-material value beats the global, so
        // bar pixels depth-test against walls while the bar stays enabled and billboarding
        // (occluded naturally, no toggling). Rescanned on a slow cadence because HealthBar pools
        // new division-mark Graphics after adopt.

        /// <summary>Per-graphic (graphic, original material, our LEqual instance) for restore.</summary>
        public readonly List<(Graphic g, Material orig, Material inst)> DepthMats = new();

        /// <summary>Instance IDs of graphics already given a depth-testing material.</summary>
        public readonly HashSet<int> DepthMatIds = new();

        /// <summary>Next unscaled time this bar is rescanned for new (pooled) graphics.</summary>
        public float NextDepthScan;

        /// <summary>
        /// Per-bar PHASE OFFSET (seconds, 0 ≤ phase &lt; <see cref="DepthScanIntervalSeconds"/>)
        /// subtracted from this bar's FIRST reschedule so ~20 bars stop rescanning in one frame
        /// forever (S2 defect 7). See <see cref="ScanPhased"/> for the no-regression argument.
        /// </summary>
        public float ScanPhase;

        /// <summary>
        /// False until the phase offset above has been spent. The FIRST scan is deliberately NOT
        /// staggered — it is the scan that installs the depth-test materials and clears
        /// raycastTarget, and delaying it would be a visible bleed-through/pick window — so the
        /// stagger is applied to the first RESCHEDULE and is a SUBTRACTION: every bar's period is
        /// therefore ≤ <see cref="DepthScanIntervalSeconds"/> at every moment, i.e. no bar is ever
        /// rescanned LATER than it is today. Only the phases differ, and after the offset is spent
        /// every bar runs at exactly the shipped 2 s period, so they stay spread apart.
        /// </summary>
        public bool ScanPhased;

        /// <summary>True once the one-shot per-bar log line fired.</summary>
        public bool DepthLogged;

        // ---- pose change gate (S2 defect 1) ---------------------------------------------------
        // The last pose/scale THIS class wrote onto the host transform. Compared with EXACT float
        // equality (see ActorBars.Same) — no epsilon anywhere, so a write is skipped only when the
        // value we would write is bit-identical to the value we last wrote, and no error can
        // accumulate over frames by construction.
        //
        // A SHADOW rather than a read-back of the transform, because a read-back would cost three
        // interop GETS to save two interop SETS. The shadow is only sound while this class is the
        // sole writer of an adopted bar host's transform, which it is: the game's own two writers
        // are Harmony prefix-skipped for adopted panels (WorldspaceDisplayPanelBase.TrackCharacter
        // and its LateUpdate, see the patches at the bottom of this file); MrBacking is suppressed
        // per adoption (Adopt sets MrBackingSuppressed); a bar is converted pokeable:false and is
        // never floated, grabbed or spawn-resolved, so ModalFallback/GrabbableModal never see it;
        // and CanvasConversion.PlaceHost is only ever called by the surface that owns a panel — a
        // bar has no surface. The remaining external event is the hide/show toggle below, which
        // clears HasPose explicitly.
        public bool HasPose;
        public Vector3 LastPos;
        public Quaternion LastRot;
        public Vector3 LastScale;

        // ---- raycast state -------------------------------------------------------------------
        // A bar is DISPLAY ONLY: it is converted with pokeable:false, so it is never registered in
        // UguiPokeSurfaces and the VR laser/poke path cannot address it. But CanvasConversion.Convert
        // adds a GraphicRaycaster to EVERY host (CanvasConversion.1.Core.cs:209) and the game's bar
        // Images ship with raycastTarget=true, so the bar's rect — which extends well past the
        // visible segments, exactly the band the user reported — is still a live hit target for any
        // EventSystem.RaycastAll consumer (IsPointerOverUI, the virtual-mouse bridge). An INVISIBLE
        // band that eats picks on the figure/panel behind it is the same defect as the depth stamp,
        // one input layer up. Cleared per graphic on the shared scan below, restored like everything
        // else this class mutates. The game's own damage preview is NOT affected: it is driven by
        // Choreographer targeting messages off the board cursor (see Adopt), never by a UI raycast.

        /// <summary>Per-graphic (graphic, original raycastTarget) for restore.</summary>
        public readonly List<(Graphic g, bool raycast)> RaycastOff = new();

        /// <summary>Instance IDs of graphics whose raycastTarget was already cleared.</summary>
        public readonly HashSet<int> RaycastOffIds = new();
    }

    /// <summary>Rescan cadence for late-spawned bar graphics (HealthBar mark pooling).</summary>
    private const float DepthScanIntervalSeconds = 2f;

    /// <summary>Number of distinct rescan phases handed out round-robin (<see cref="Adopted.ScanPhase"/>).
    /// A power of two so the counter wraps with a mask; 16 buckets over the 2 s period puts at most
    /// two of ~20 bars in the same frame instead of all of them.</summary>
    private const int DepthScanPhaseBuckets = 16;

    /// <summary>Round-robin source for <see cref="Adopted.ScanPhase"/>. Monotonic and masked, so a
    /// long session of pooled adopt/release churn keeps spreading the phases rather than drifting
    /// back into a single bucket.</summary>
    private static int s_depthScanPhaseSeq;

    // ---- per-frame work counters (S2 perf round) --------------------------------------------
    // Priced so the next hardware capture can answer "did the pose gate land?" arithmetically:
    // Bars.PoseWrites / Bars.ScaleWrites against Bars.Bars is the hit rate of the change gate, and
    // Bars.DepthScans per frame shows whether the 2 s rescans are still landing in one frame.
    private static int s_barPoseWrites;
    private static int s_barScaleWrites;
    private static int s_barDepthScans;

    /// <summary>EXACT component-wise equality — deliberately not Vector3's <c>==</c>, which is an
    /// approximate compare with a 1e-5 distance epsilon. A gate that skips a write only on
    /// bit-identical values is invisible by construction and cannot accumulate drift; an epsilon
    /// gate could hold a permanent sub-epsilon error, which is a look change however small.</summary>
    private static bool Same(in Vector3 a, in Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;

    /// <summary>EXACT component-wise equality for a quaternion; see <see cref="Same(in Vector3, in Vector3)"/>.</summary>
    private static bool Same(in Quaternion a, in Quaternion b) =>
        a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;

    // ---- BarsOccluded config (standalone binding) --------------------------------------------
    // FRESH key in its OWN module file (dev.gloomhavenvr.bars.cfg) on purpose: the BepInEx
    // persisted-config trap means flipping a default on an EXISTING key does nothing once a
    // stale value is saved — a brand-new key name guarantees the default (true) actually
    // applies on every rig. Not in WorldUIConfig because that file predates the trap lesson.
    private static ConfigFile? s_barsConfigFile;
    private static ConfigEntry<bool>? s_barsOccluded;
    private static ConfigEntry<float>? s_barHeightOffset;

    /// <summary>
    /// Bind-once for the [WorldUI] BarsOccluded entry. Extracted from the property getter so the
    /// in-VR config browser can force the file into existence (<c>ConfigCatalog.EnsureBound</c>)
    /// instead of hiding this setting until the first bar scan happens to run. Pure — it creates
    /// the config file and binds one key, and touches nothing else.
    /// </summary>
    internal static void BindConfig()
    {
        if (s_barsOccluded != null && s_barHeightOffset != null)
            return;
        s_barsConfigFile = ModuleConfig.Create("bars");
        s_barHeightOffset = s_barsConfigFile.Bind("WorldUI", "BarHeightOffset",
            Defaults.BarHeightOffset,
            new ConfigDescription(
                "Hoehe der Lebensbalken ueber ALLEN Figuren, in Weltmasseinheiten (eine Kachel ist "
                + "rund 1.7). Positiv hebt sie an, negativ senkt sie ab, 0 ist die gemessene Hoehe. "
                + "Der Mod setzt den Balken automatisch dicht ueber das Kopfgelenk der jeweiligen "
                + "Figur; dieser Wert verschiebt ALLE Balken gemeinsam, ohne diese Messung zu "
                + "ersetzen — grosse und kleine Figuren behalten also ihr Verhaeltnis zueinander. "
                + "Wirkt sofort, ohne Neustart.",
                new AcceptableValueRange<float>(-2f, 2f)));
        s_barsOccluded = s_barsConfigFile.Bind("WorldUI", "BarsOccluded", Defaults.BarsOccluded,
            "Actor HP/effect bars depth-test against the world: walls occlude them like "
            + "any world object instead of the bar shining through. Look-preserving — "
            + "bars stay enabled and billboarding, they are simply hidden pixel-by-pixel "
            + "where a wall is in front. Disable to get the vanilla draw-on-top bars.");
        // NOTE: the short-lived [WorldUI] BarsDepthStamp key of an earlier round is GONE, not
        // re-defaulted. It gated the bars OUT of the panel-vs-panel compose, which the user overruled
        // ("Perspektive soll im gesamten Mod respektiert werden"), and the BepInEx persisted-config
        // trap makes a re-defaulted key worthless anyway: every rig that already ran that build has
        // `BarsDepthStamp = false` saved, so flipping the shipped default would have changed
        // nothing where it mattered. Dropping the binding leaves the stale line in the cfg as an
        // inert orphan and puts every rig on the fixed behaviour.
    }

    /// <summary>
    /// USER OFFSET on every bar's anchor height, in board world units, read live.
    ///
    /// <para>User, 2026-09-02: <i>"Geb mir eine offset Einstellung in Erweitert, in dem ich die
    /// Hoehe der Healtbars fuer alle Figuren selber noch etwas anpassen kann."</i></para>
    ///
    /// <para>It is added AFTER every rule in <see cref="MeasureAnchorOffsetWU"/> has run — after the
    /// head-joint measurement, after the artists' authored floor, after both ceilings. That is the
    /// point: those rules answer "where is this creature's head", which differs per figure and which
    /// he is not being asked to re-tune; this answers "and how far above that do I want the bar",
    /// which is one number for the whole board. Applying it before the clamps would let the ceiling
    /// silently eat his adjustment on exactly the tall figures where he is most likely to want it.</para>
    ///
    /// <para>Bounded only by the same hard ceiling, so a mistyped value cannot launch a bar out of
    /// the room, and floored at zero so a bar can never sink below the track point.</para>
    /// </summary>
    private static float BarHeightOffsetWU
    {
        get
        {
            BindConfig();
            return s_barHeightOffset!.Value;
        }
    }

    /// <summary>Config gate for the bar depth-test (lazily bound, read live every scan).</summary>
    private static bool BarsOccluded
    {
        get
        {
            BindConfig();
            return s_barsOccluded!.Value;
        }
    }

    private static readonly HashSet<WorldspaceDisplayPanelBase> Owned = new();
    private static readonly Dictionary<WorldspacePanelUIController, Adopted> Adoptions = new();
    private static readonly List<WorldspacePanelUIController> Scratch = new(32);
    private static readonly List<Renderer> RendererScratch = new(16);

    /// <summary>Second renderer scratch, used ONLY by <see cref="CountAllRenderers"/> so the
    /// census walk can never clobber the measurement walk it is describing.</summary>
    private static readonly List<Renderer> RendererScratchAll = new(16);

    /// <summary>Patch gate: true when the game must NOT drive this panel's transform.</summary>
    internal static bool Owns(WorldspaceDisplayPanelBase panel) => Owned.Contains(panel);

    internal static void Tick()
    {
        // [WorldUI] ActorBars is GONE (user ruling 2026-08-13): its OFF released every adopted
        // bar back to the game's screen-projected presentation, which is invisible from inside
        // the HMD — i.e. it switched off the health of every figure on the board. The SIZE dials
        // ([WorldUI] BarSizeScale / BarZoomMin/MaxScale / BarFixedSize / BarsOccluded) stay.
        bool want = WorldUIConfig.ConversionActive
                    && Choreographer.s_Choreographer != null;

        if (!want)
        {
            if (Adoptions.Count > 0)
                ReleaseAll();
            return;
        }

        WorldspaceUITools tools = WorldspaceUITools.Instance;
        if (tools == null)
            return;

        // Adopt new panels (list is the game's own registry; publicized private).
        List<WorldspacePanelUIController> controllers = tools._panelUIControllers;
        for (int i = 0; i < controllers.Count; i++)
        {
            WorldspacePanelUIController controller = controllers[i];
            if (controller == null || Adoptions.ContainsKey(controller))
                continue;
            Adopt(controller);
        }

        // Drop adoptions whose controller died (actor removed / scene unloading).
        if (Adoptions.Count > 0)
        {
            Scratch.Clear();
            foreach (KeyValuePair<WorldspacePanelUIController, Adopted> pair in Adoptions)
            {
                if (pair.Key == null || !pair.Value.Panel.IsAlive)
                    Scratch.Add(pair.Key!);
            }
            for (int i = 0; i < Scratch.Count; i++)
                Release(Scratch[i]);
        }
    }

    /// <summary>Placement pass — runs in the driver's LateUpdate, allocation-free.</summary>
    internal static void LateTick()
    {
        if (Adoptions.Count == 0)
            return;

        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;

        Vector3 headPos = head.transform.position;
        float worldScale = PanelLayout.WorldScale;
        float metersPerPixel = WorldUIConfig.CanvasScaleMm.Value * 0.001f;

        bool anyHeld = HeldFigures.Count > 0;

        // Hoisted out of the per-bar loop (S2): three values that CANNOT change while this loop
        // runs — a config entry is not written from inside it, and Time.unscaledTime is constant
        // for the whole frame by definition. Reading them once instead of once per bar is the same
        // arithmetic with the same inputs, and it removes ~40 BepInEx property reads per frame at
        // 20 bars. BarsOccluded in particular ran its lazy BindConfig null-check twice per bar.
        bool barsOccluded = BarsOccluded;
        bool barFixedSize = WorldUIConfig.BarFixedSize.Value;
        float now = Time.unscaledTime;

        // ---- SIZE (user: "Größe der Healthbars sollen einstellbar sein - sowie ein minimum und
        // maximum der Größe, damit sie sich trotz zoomen nie über die Grenzen hinaus skalieren
        // können"). ONE dial and a FIXED band: the two bound dials are gone (see ZoomFollowMin),
        // the band they configured is now constant, so there is nothing left to sort or validate —
        // a hand-edited cfg can no longer put the floor above the ceiling.
        float barSizeScale = Mathf.Max(0.01f, WorldUIConfig.BarSizeScale.Value);
        const float sizeLo = ZoomFollowMin;
        const float sizeHi = ZoomFollowMax;
        float zoomFollow = ResolveZoomFollow(worldScale);
        // The default path's factor is frame-constant, so it is resolved once here rather than per
        // bar; the legacy distance path re-clamps per bar because its growth term is per bar.
        float fixedSizeFactor = barSizeScale * Mathf.Clamp(zoomFollow, sizeLo, sizeHi);

        // One sampled bar's uGUI pixel height turns the factor into the MILLIMETRE number a
        // "still too big / too small" report can be answered with. Sampled only on a frame that
        // will actually log (rare), so the extra RectTransform read is not a per-frame cost.
        bool wantSizeLog = WantSizeLog(now, fixedSizeFactor, worldScale);
        float sampleRectPx = 0f;

        s_barPoseWrites = 0;
        s_barScaleWrites = 0;
        s_barDepthScans = 0;

        foreach (KeyValuePair<WorldspacePanelUIController, Adopted> pair in Adoptions)
        {
            WorldspacePanelUIController controller = pair.Key;
            Adopted adopted = pair.Value;
            ConvertedPanel panel = adopted.Panel;
            if (controller == null || panel.HostGo == null)
                continue;

            if (wantSizeLog && sampleRectPx <= 0f && panel.Target != null)
                sampleRectPx = panel.Target.rect.height;

            // Item 6: hide this bar while its own mini is held in the hand (redundant with the
            // docked held-figure info panel, and it clutters the hand). Cheap fast-path — when
            // nothing is held the whole check is skipped and every bar stays shown. Toggling is
            // guarded on activeSelf so it only flips on the held→released edges; on release the
            // bar re-activates and resumes normal placement below. Only THIS actor is affected.
            bool hide = false;
            if (anyHeld)
            {
                if (adopted.Actor == null && controller.m_ObjectToTrack != null)
                    adopted.Actor = ActorBehaviour.GetActorBehaviour(controller.m_ObjectToTrack);
                hide = adopted.Actor != null && HeldFigures.Owns(adopted.Actor);

                // …UNLESS THIS BAR IS RESOLVING AN ATTACK. THIS LINE IS THE DEADLOCK MECHANISM
                // (user, hardware, ModBuild 107: "SEHR WICHTIG: DEADLOCK - ich habe ein Skelet
                // hochgehoben während es dran war, dann ist plötzlich nichts mehr passiert die
                // Gegner haben nicht mehr weitergemacht").
                //
                // The chain, read from the log and the decompiled source: AttackModBar's flow runs
                // as a coroutine STARTED ON THIS CONTROLLER. Deactivating a GameObject kills its
                // coroutines permanently — re-activating does not resume them — and FinalizeFlow(),
                // the only writer of IsFlowActive = false, is that coroutine's last statement. So
                // the flag latches true, and Choreographer's WaitingForPlayerIdle, which has NO
                // tick timeout unlike every neighbouring wait state, never calls StepComplete().
                // The rule engine's last word in the reported session is Player.log:18635; the
                // attack flow it belongs to logs "ShowAttackModifDamage show label" at :18640 and
                // is the ONLY one of the session's seven with no matching "finish"; the figure was
                // grabbed at :18660 and :18695. 3600 further lines, and the enemies never move again.
                //
                // FigureGrab's FigureBusy gate now refuses the grab outright during those waits, so
                // this branch should already be unreachable in the failing case. This is the second
                // layer, and on a defect that ends the user's session it is worth the cost: it takes
                // away the MECHANISM, not just the trigger. Price: the bar of a figure you are
                // holding stays visible for the ~2 s an attack resolves. That is a far better trade
                // than a dead turn machine, and it is the smaller half of what this hide is for
                // anyway — the clutter it removes is mostly the long tail of just standing there
                // holding a mini.
                // No `controller != null` here on purpose: the loop already returned on that at the
                // top (see the guard above with panel.HostGo), and re-testing it reset the compiler's
                // flow state, which turned the TryGetTrackPoint call below into a fresh CS8604. The
                // seventh nullability warning in this project's build was born and died right here.
                if (hide && controller.FlowControlActive())
                    hide = false;
            }

            // Anchor in BOARD units (P6 fix #4): the cached bounds-derived offset
            // scales with the diorama by construction — zooming the table keeps the
            // bar exactly above the miniature instead of inside it. The cache is no longer
            // written once and trusted forever: ResampleAnchor re-measures it a bounded number
            // of times after adopt, because the game finishes assembling a character (child
            // prefab + streamed materials) AFTER its bar controller registers itself.
            ResampleAnchor(adopted, controller, now);
            bool haveTrack = TryGetTrackPoint(controller, out Vector3 track);
            Vector3 pos = track + Vector3.up * adopted.AnchorOffsetWU;

            // ---- bars TAKE PART in the perspective compose like every other panel ----------------
            //
            // An earlier round suppressed this bar's per-panel depth stamp outright because the
            // stamp covered the bar's whole RECT — invisible margin included — and cut a clean
            // rectangle out of the enemy info panel behind it (healthbar_problem.jpg). That was an
            // OVER-CORRECTION and the user reported the consequence immediately: "Die Health bars
            // werden jetzt komplett verdeckt auch wenn die Infotafel DAHINTER ist." The round after
            // it shrank the stamp to the measured ink instead, which the hardware log shows removed
            // 10 % of an actor bar's stamp — still a box, still a hole.
            //
            // Both are gone. Bars are ordered by distance among all converted panels
            // (CanvasConversion.8.Order.cs) and write no depth against other panels at all, so a
            // bar in front of the Infotafel occludes it, a bar behind it is occluded by it, and the
            // transparent space around the bar's segments shows the panel through it completely.
            //
            // Wall occlusion ([WorldUI] BarsOccluded gate) is a DIFFERENT question and is unchanged:
            // the bar's graphics run per-instance materials with unity_GUIZTestMode=LEqual so wall
            // depth occludes them naturally — the bar itself stays enabled and billboarding (no
            // toggling; the old linecast+hide probe is retired). Slow rescan catches graphics pooled
            // after adopt (health marks). The SAME scan clears raycastTarget on every bar graphic
            // (see Adopted.RaycastOff) — that part is ungated: the bar's invisible band must not eat
            // picks whatever the occlusion setting is.
            if (now >= adopted.NextDepthScan)
            {
                // STAGGER (S2 defect 7). Every bar shipped with NextDepthScan = 0, and Tick adopts
                // every controller of a freshly revealed room in ONE frame, so all ~20 bars did
                // their FIRST scan together and — because each then rescheduled by exactly the same
                // 2 s — stayed locked in the same frame for the rest of the session: a synchronised
                // 20-bar walk (~178 graphics each) once every 2 s, self-inflicted.
                //
                // The first scan stays immediate (it installs the depth materials and clears
                // raycastTarget; delaying it would be visible). The stagger is SUBTRACTED from the
                // first reschedule only, so this bar's next scan comes EARLIER than it does today,
                // never later — no pooled graphic is picked up any later than in the shipped build,
                // which is what makes this invisible — and from then on the period is exactly the
                // shipped 2 s again, with the bars now spread across the phase.
                if (!adopted.ScanPhased)
                {
                    adopted.ScanPhased = true;
                    adopted.NextDepthScan = now + DepthScanIntervalSeconds - adopted.ScanPhase;
                }
                else
                {
                    adopted.NextDepthScan = now + DepthScanIntervalSeconds;
                }
                ScanBarGraphics(adopted, controller.name, depthTest: barsOccluded);
                s_barDepthScans++;
            }
            if (!barsOccluded && adopted.DepthMats.Count > 0)
            {
                // Live config-off: give every graphic its original material back.
                RestoreBarDepthTest(adopted);
            }

            if (panel.HostGo.activeSelf == hide)
            {
                panel.HostGo.SetActive(!hide);
                // The pose shadow only speaks for a host this class has been writing continuously.
                // Force the next shown frame to write unconditionally rather than reason about what
                // happened while the host was off.
                adopted.HasPose = false;
            }
            if (hide)
                continue;

            if (!haveTrack)
                continue;

            // Billboard: uGUI front faces -forward → +Z away from the viewer.
            Vector3 fromHead = pos - headPos;
            if (fromHead.sqrMagnitude < 1e-6f)
                continue;
            Quaternion rot = Quaternion.LookRotation(fromHead.normalized, Vector3.up);

            // Bar size — real millimetres at the eye, tuned by [WorldUI] BarSizeScale and following
            // the table zoom inside the fixed band (ResolveZoomFollow). The default path's
            // factor is the hoisted frame constant; only the opt-in legacy path (BarFixedSize off,
            // test #14 item 4: bars grow up to 2.5x with head distance) is per bar — and its growth
            // goes THROUGH THE SAME CLAMP, so the min/max guarantee holds on that path too rather
            // than being quietly bypassed by the one setting that scales bars for another reason.
            float sizeFactor = fixedSizeFactor;
            if (!barFixedSize)
            {
                // Legacy: distance in HMD-relative REAL meters (world ÷ diorama scale).
                float realDistance = fromHead.magnitude / worldScale;
                float grow = Mathf.Clamp(realDistance / 0.6f, 1f, 2.5f);
                sizeFactor = barSizeScale * Mathf.Clamp(zoomFollow * grow, sizeLo, sizeHi);
            }

            // CHANGE GATE (S2 defect 1). Both writes used to be unconditional, so every bar dirtied
            // its world-space canvas transform every frame whether or not anything about it had
            // moved — and a dirtied canvas transform is paid for again in PostLateUpdate's canvas
            // update, which lands in the frame's BLOCKED span.
            //
            // INVISIBLE BY CONSTRUCTION: the comparison is EXACT (see Same) against the value THIS
            // class last wrote, so a write is skipped only when the transform already holds exactly
            // the bits the write would deposit. There is no epsilon, therefore no residual error to
            // accumulate. With the default [WorldUI] BarFixedSize the scale term is
            // metersPerPixel × worldScale × BarPixelSize × fixedSizeFactor — four frame-constant
            // factors — so the scale gate holds every frame the diorama is not being zoomed.
            //
            // DURING a zoom it depends on which side of the clamp the follow is on, and the two
            // cases are worth naming because they are the feature: while the follow is INSIDE its
            // bounds the factor is ×(reference·base ÷ worldScale) and the worldScale in the term
            // cancels exactly — the bar holds one WORLD size, which is what "it grows with the
            // miniature" means, and the gate keeps holding through the whole pinch. Once the clamp
            // bites, the factor freezes and the term is ∝ worldScale again — the bar holds one REAL
            // size and the gate writes every frame of the pinch, which is correct: that is a bar
            // whose size at the eye is genuinely being kept still while the world moves.
            //
            // The pose gate, by contrast, only holds while the head is genuinely still (rot is
            // derived from the head position, and VR head tracking moves it by sub-millimetres
            // every frame). The counters below report which.
            Vector3 scale = Vector3.one * (metersPerPixel * worldScale * BarPixelSize * sizeFactor);
            Transform t = panel.HostGo.transform;
            if (!adopted.HasPose || !Same(pos, adopted.LastPos) || !Same(rot, adopted.LastRot))
            {
                t.SetPositionAndRotation(pos, rot);
                adopted.LastPos = pos;
                adopted.LastRot = rot;
                s_barPoseWrites++;
            }
            if (!adopted.HasPose || !Same(scale, adopted.LastScale))
            {
                t.localScale = scale;
                adopted.LastScale = scale;
                s_barScaleWrites++;
            }
            adopted.HasPose = true;
        }

        if (wantSizeLog)
            LogSize(now, fixedSizeFactor, barSizeScale, zoomFollow, sizeLo, sizeHi,
                    worldScale, metersPerPixel, sampleRectPx, barFixedSize);

        PerfMonitor.Count("Bars.Bars", Adoptions.Count);
        PerfMonitor.Count("Bars.PoseWrites", s_barPoseWrites);
        PerfMonitor.Count("Bars.ScaleWrites", s_barScaleWrites);
        PerfMonitor.Count("Bars.DepthScans", s_barDepthScans);
    }

    // ==============================================================================================
    //  SIZE — real millimetres at the eye, and what the table zoom is allowed to do to them
    // ==============================================================================================

    /// <summary>
    /// How far the TABLE ZOOM is allowed to carry the bars away from the size the player set — the
    /// raw follow factor, before <see cref="ZoomFollowMin"/>/<see cref="ZoomFollowMax"/> clamp it.
    ///
    /// <para>WHICH SIZE IS "THE SIZE". The mod's zoom is a scale on the RIG, not on the board
    /// (<c>VRRigDriver</c>: <c>rigRoot.localScale = baseScale × ClampedSavedMultiplier</c>), so a
    /// world-unit size and a real-metre size are two different quantities that drift apart every
    /// time the player pinches: <c>worldUnits = realMetres × WorldScale</c>. The bar is a
    /// READABILITY OVERLAY, so the size that matters is the one at the EYE, in real millimetres —
    /// it is the number the player judges ("too big"), the number this class logs, and the only one
    /// a minimum and a maximum can be stated in without the bound itself moving when the player
    /// zooms. The dial and both bounds here are therefore factors of a REAL-MILLIMETRE base
    /// (<see cref="BarPixelSize"/>), and the existing <c>× worldScale</c> in the scale term is
    /// exactly the real-metres→world-units conversion, not a size decision.</para>
    ///
    /// <para>WHAT ZOOMING DOES. A bar belongs to a miniature, so it follows the table: pinch the
    /// table larger and the bar grows with the mini, pinch it away and it shrinks with it. That is
    /// what "sie skalieren beim Zoomen" describes, and it is the thing the bounds exist to bound.
    /// The follow is the ratio of the reference zoom to the live one, which is where a bar's world
    /// size would be constant: <c>follow = ReferenceMultiplier × BaseWorldScale / WorldScale</c>,
    /// and since <c>WorldScale = BaseWorldScale × liveMultiplier</c> the base cancels — the follow
    /// is purely <c>reference ÷ live pinch multiplier</c>, i.e. it does not care which scenario's
    /// tile size set the base scale.</para>
    ///
    /// <para>THE GUARANTEE the band then gives, in the same unit as the size: at ANY zoom the bar
    /// is between 0.7× and 1.5× of <c>BarSizeScale</c> of its shipped millimetres — 0.25 mm/px at
    /// the far end of zooming out and 0.53 mm/px at the near end, never more, never less. THIS IS
    /// THE ZOOM-READABILITY CHECK: the mod's pinch runs 0.1×…12× of the base scale while [Comfort]
    /// FreeMovement is on, i.e. a raw follow of 33.7 down to 0.28, so it is the band and nothing
    /// else that stops a pushed-away table from shrinking the bars to a quarter of legibility and a
    /// pulled-in one from letting them swallow the board. It is therefore code, not a setting.</para>
    ///
    /// <para>Returns 1 (no follow) while no rig has published a base scale — the menu rig, the dev
    /// harness, the frames before <c>BuildRig</c>. A zoom factor derived from a scale nobody has
    /// established yet would be a size change nobody asked for.</para>
    /// </summary>
    private static float ResolveZoomFollow(float worldScale)
    {
        float baseScale = Rig.VRRigDriver.BaseWorldScale;
        if (baseScale <= 0f || worldScale <= 0f)
            return 1f;
        return ReferenceScaleMultiplier * baseScale / worldScale;
    }

    // ---- size log (one line, on change, throttled) ----------------------------------------------
    // "Noch zu groß" has to be answerable from numbers rather than from a second look, so the line
    // carries the RESOLVED SIZE IN REAL MILLIMETRES and the world scale it was resolved at, plus
    // every term in between (dial, raw follow, the bounds, and which one bit).
    private static float s_loggedSizeFactor = float.NaN;
    private static float s_loggedWorldScale = float.NaN;
    private static float s_nextSizeLog;

    /// <summary>Minimum seconds between two size lines — a pinch-zoom changes the factor on every
    /// frame it runs, and the interesting number is the one it settles at.</summary>
    private const float SizeLogIntervalSeconds = 2f;

    /// <summary>
    /// Is there a NEW size worth a line? Never on an unchanged frame (the common case, which must
    /// cost one float compare), and never twice inside <see cref="SizeLogIntervalSeconds"/>.
    /// </summary>
    private static bool WantSizeLog(float now, float sizeFactor, float worldScale)
    {
        if (float.IsNaN(s_loggedSizeFactor))
            return true;
        if (now < s_nextSizeLog)
            return false;
        // Relative, not absolute: the same 1 % that is invisible on a 0.3 factor is invisible on a
        // 3.0 one, and an absolute epsilon would either spam the small end or go silent at the big.
        return Mathf.Abs(sizeFactor - s_loggedSizeFactor) > 0.01f * Mathf.Max(0.01f, s_loggedSizeFactor)
               || Mathf.Abs(worldScale - s_loggedWorldScale) > 0.02f * Mathf.Max(0.01f, s_loggedWorldScale);
    }

    private static void LogSize(float now, float sizeFactor, float dial, float follow,
                                float lo, float hi, float worldScale, float metersPerPixel,
                                float sampleRectPx, bool barFixedSize)
    {
        s_loggedSizeFactor = sizeFactor;
        s_loggedWorldScale = worldScale;
        s_nextSizeLog = now + SizeLogIntervalSeconds;

        float mmPerPixel = metersPerPixel * 1000f * BarPixelSize * sizeFactor;
        float shippedMmPerPixel = metersPerPixel * 1000f * BarPixelSize;
        float baseScale = Rig.VRRigDriver.BaseWorldScale;
        float liveMultiplier = baseScale > 0f ? worldScale / baseScale : 0f;
        string bound = follow < lo ? " (held at the MINIMUM)"
                     : follow > hi ? " (held at the MAXIMUM)"
                     : string.Empty;
        string sample = sampleRectPx > 0f
            ? $"a {sampleRectPx:F0} px bar is {sampleRectPx * mmPerPixel:F1} mm tall at the eye"
            : "no bar rect measured this frame";

        VRLog.Info("WorldUI",
            $"BAR SIZE: {mmPerPixel:F3} mm per uGUI px at the eye (shipped {shippedMmPerPixel:F3}) — " +
            $"{sample}. Size dial [WorldUI] BarSizeScale {dial:F2}x, zoom follow {follow:F2} clamped " +
            $"into the FIXED band [{lo:F2}, {hi:F2}]{bound} — the band is no longer configurable " +
            $"(BarZoomMin/MaxScale removed 2026-08-13: at the shipped zoom the follow is 1.00 and " +
            $"neither bound was ever reachable) ⇒ resolved {sizeFactor:F2}x. World scale {worldScale:F2} " +
            $"(base {baseScale:F2}, table zoom {liveMultiplier:F2}x, reference " +
            $"{ReferenceScaleMultiplier:F2}x), {Adoptions.Count} bars." +
            (barFixedSize
                ? string.Empty
                : " [WorldUI] BarFixedSize is OFF, so each bar additionally grows up to 2.5x with " +
                  "its own head distance THROUGH THE SAME CLAMP — the numbers above are the " +
                  "distance-1 case, and the bounds hold for every bar."));
    }

    /// <summary>
    /// Track point: same selection the game's own <c>TrackCharacter</c> uses (head
    /// bone / base / base + head offset; decompiled WorldspaceDisplayPanelBase.cs:185
    /// — publicized private fields).
    /// </summary>
    private static bool TryGetTrackPoint(WorldspacePanelUIController controller, out Vector3 track)
    {
        Transform headBone = controller.m_HeadBonePoint;
        Transform basePoint = controller.m_BasePoint;
        if (controller.m_PointToTrackOnActor == WorldspaceDisplayPanelBase.PoinToTrack.HeadBone && headBone != null)
        {
            track = headBone.position;
            return true;
        }
        if (basePoint != null)
        {
            track = controller.m_PointToTrackOnActor == WorldspaceDisplayPanelBase.PoinToTrack.Base
                ? basePoint.position
                : basePoint.position + controller.m_HeadBaseOffset;
            return true;
        }
        track = default;
        return false;
    }

    /// <summary>
    /// THE HARD CEILING on a bar's anchor offset, world units.
    ///
    /// <para>Its original comment read "the tallest boss mini is ~5 wu, so any larger figure
    /// height is a mismeasured bound, not a figure". ModBuild 291's hardware log is the first
    /// measurement of that claim and it cuts both ways. The claim is FALSE about the box — the
    /// boss's mesh AABB is 6.84 wu tall — and very nearly TRUE about the figure: measured LIVE
    /// (<see cref="FigureBody.TryLiveExtentY"/>) the boss's own skeleton is what the ceiling was
    /// always trying to describe. So the ceiling is right about content and was wrong only about
    /// what it was measuring.</para>
    ///
    /// <para><b>IT NO LONGER BINDS ANYTHING, AND IT STAYS.</b> In ModBuild 291 this arm bound the
    /// boss (raw 6.37 -> 6.00) and that clamp was the visible defect. What it still guards is the
    /// case the live rule cannot see: a renderer with no bone array whose baked box is garbage (a
    /// mesh folded into a foreign batch that survives the isPartOfStaticBatch filter, a VFX mesh
    /// that is neither particle nor trail). A bar 6 wu up is wrong; a bar 40 wu up is a bar the
    /// player will never find again. <see cref="MeasureAnchorOffsetWU"/> names this arm in its line
    /// when it bites, and a hardware line naming it is genuine news rather than the expected
    /// case.</para>
    /// </summary>
    private const float AnchorHardCeilingWU = 6f;

    /// <summary>The clamp's lower arm — a bar may never sit ON the track point.</summary>
    private const float AnchorFloorWU = 0.05f;

    /// <summary>
    /// How many times an adopted bar RE-MEASURES its anchor after adopt before latching.
    ///
    /// <para>WHY THIS EXISTS. The measurement below used to run exactly once, at adopt, and the
    /// comment asserted that was enough ("a rare, per-actor-spawn event"). An actor is adopted the
    /// frame its <c>WorldspacePanelUIController</c> registers itself, which is inside
    /// <c>ActorBehaviour.SetActor</c> — and the game loads a character's child prefab and its
    /// MATERIALS asynchronously through Addressables
    /// (<c>CharacterManager.InitialiseCharacterAsync</c>, <c>MaterialLoaderData.LoadMaterials</c>,
    /// which sets <c>Renderer.enabled = false</c> until the load completes). A measurement taken on
    /// that frame can see a hierarchy that is not yet the figure the player will look at, and the
    /// one value it produced was then kept forever.</para>
    ///
    /// <para><b>WHAT THE HARDWARE LOG SAID ABOUT THAT MECHANISM (ModBuild 291) — the guess above
    /// is not what happened.</b> The boss WAS measured mid-assembly and the resample DID correct
    /// it (box 2.37 -> 6.84 wu, head joint 1.52 -> 3.41), so the remedy works. But the renderer
    /// census is BIT-IDENTICAL across the two samples — "5 mesh renderer(s) measured of 36 on
    /// ACTIVE objects (48 incl. inactive) ... 2 measured with Renderer.enabled=false" both times.
    /// The same renderers reported bounds 2.89x apart. Nothing was streamed in between; the
    /// figure was RESCALED (or its skeleton re-posed) under a renderer set that never changed.
    /// The material-loader story is a real mechanism and it is not this one, and a census that
    /// does not move is exactly the evidence that would otherwise have been read as "nothing
    /// happened".</para>
    ///
    /// <para><b>THE BUDGET IS SPENT IN FULL, AND THAT IS A CHANGE.</b> The original latch dropped
    /// the budget to zero as soon as two consecutive samples agreed. In the log it never fired for
    /// an ANIMATED figure: BruteID's samples read 2.59, 2.55, 2.54, 2.55, 2.54, 2.55, 2.54 — its
    /// skinned bounds breathe with the animation, <c>Mathf.Approximately</c> is a relative epsilon
    /// of about 1e-6, and so the Brute burned all eight samples and wrote seven log lines, which
    /// is seven of the sixteen anchor lines in that session. So the latch is gone: the budget is
    /// always spent, and the ADOPT-AND-LOG step is gated on
    /// <see cref="AnchorResampleTolerance"/> instead. That trades six extra subtree walks per
    /// figure over four seconds (48 objects at the largest, ~40 walks per second across a full
    /// board, none of them a scene sweep) for two things worth more: the log stops repeating
    /// itself, and a figure that finishes assembling LATER than its first two samples can no
    /// longer latch a wrong number forever.</para>
    /// </summary>
    private const int AnchorSampleBudget = 8;

    /// <summary>Seconds between anchor resamples; see <see cref="AnchorSampleBudget"/>.</summary>
    private const float AnchorSampleIntervalSeconds = 0.5f;

    /// <summary>
    /// How much a resample must RAISE the anchor, AS A FRACTION OF THE ANCHOR ITSELF, before it is
    /// adopted and written to the log.
    ///
    /// <para>Replaces <c>Mathf.Approximately</c>, which asked "are these two floats the same
    /// number?" when the question is "is this a different ANSWER?". A skinned figure's measured
    /// height breathes with its animation — BruteID's eight samples in the ModBuild 291 log span
    /// 2.54 to 2.59 wu — so the float test was never true and every sample logged. 2 % of the
    /// anchor height is 5 cm of board on a Brute and 10 cm on the boss: below the width of the bar
    /// itself, and far below anything a player could see move.</para>
    ///
    /// <para><b>AND IT IS NOW ONE-SIDED — see <see cref="ResampleAnchor"/>.</b> The sampling window
    /// keeps the HIGHEST anchor it has seen, never a later lower one.</para>
    /// </summary>
    private const float AnchorResampleTolerance = 0.02f;

    /// <summary>
    /// Board-space anchor height above the track point, from the miniature's LIVE, ANIMATED extent
    /// (<see cref="FigureBody"/> — bone transforms for skinned meshes, own transform for props;
    /// already in board units). Vanilla equivalent: the game adds <c>m_WorldspaceOffsetY</c> (world
    /// units, from <c>Init(..., float height = 1.8f)</c>) before projecting to the screen
    /// (decompiled WorldspaceDisplayPanelBase.cs:186, WorldspacePanelUIController.cs:94); we anchor
    /// at the figure's actual top (tighter than the fixed 1.8) with the vanilla offset as the
    /// fallback.
    ///
    /// <para><b>NOT <c>Renderer.bounds</c> ANY MORE, AND NOT A CORRECTION TO IT.</b> Read
    /// <see cref="FigureBody"/> before changing anything here: the baked box is measured and
    /// printed on every line this writes, and it decides nothing.</para>
    ///
    /// <para><paramref name="report"/> is the ONE LINE that makes a misplaced bar answerable —
    /// see <see cref="LogAnchor"/> for why this function had no instrument at all until the boss
    /// dragon arrived. <paramref name="measured"/> is false exactly when the vanilla fallback was
    /// returned, i.e. when the bounds rule never ran at all.</para>
    /// </summary>
    private static float MeasureAnchorOffsetWU(
        WorldspacePanelUIController controller, bool wantReport, out string report, out bool measured)
    {
        measured = false;
        report = string.Empty;
        float fallback = Mathf.Max(controller.m_WorldspaceOffsetY, 0.2f);

        GameObject tracked = controller.m_ObjectToTrack;
        if (tracked == null)
        {
            if (wantReport)
                report = "MEASUREMENT NEVER RAN — the controller has no object to track; "
                         + $"vanilla fallback {fallback:F2} wu{Hex(fallback)}";
            return fallback;
        }
        if (!TryGetTrackPoint(controller, out Vector3 track))
        {
            if (wantReport)
                report = "MEASUREMENT NEVER RAN — no track point (head bone AND base are both "
                         + $"missing); vanilla fallback {fallback:F2} wu{Hex(fallback)}";
            return fallback;
        }

        string trackMode = !wantReport
            ? string.Empty
            : controller.m_PointToTrackOnActor == WorldspaceDisplayPanelBase.PoinToTrack.HeadBone
              && controller.m_HeadBonePoint != null
                ? "HeadBone"
                : controller.m_PointToTrackOnActor == WorldspaceDisplayPanelBase.PoinToTrack.Base
                    ? "Base"
                    : "HeadBoneStatic (base + the head offset captured at Init)";

        RendererScratch.Clear();
        tracked.GetComponentsInChildren(includeInactive: false, RendererScratch);
        int onActiveObjects = RendererScratch.Count;

        int batched = 0;
        int notMesh = 0;
        int used = 0;
        int componentDisabled = 0;
        int bonelessSkinned = 0;
        int totalBones = 0;
        // THE LIVE EXTENT (bones for skinned meshes, own transform for props) and, BESIDE IT AND
        // DECIDING NOTHING, the baked Renderer.bounds box the pre-ModBuild-294 rule used. The two
        // are printed together on every line this method writes: that pair is what makes the claim
        // "the box is not the figure" checkable on the next hardware log instead of asserted here.
        float liveMaxY = float.MinValue;
        float liveMinY = float.MaxValue;
        float boxMaxY = float.MinValue;
        float boxMinY = float.MaxValue;
        string tallest = "?";
        string tallestKind = "?";
        for (int i = 0; i < RendererScratch.Count; i++)
        {
            Renderer r = RendererScratch[i];
            // A statically batched renderer reports the bounds of its ENTIRE combined batch
            // (documented Unity behaviour), not its own mesh — a mini folded into a 'Maps'
            // batch would read as tall as the whole map chunk. Skip; the vanilla fixed offset
            // below is the fallback, exactly the height the flat game uses.
            if (r.isPartOfStaticBatch)
            {
                batched++;
                continue;
            }
            // Only the miniature's SURFACE geometry may define "the top of the figure".
            // Particle, trail and line renderers report their effect VOLUME: the Elementalist's
            // infusion VFX measured sky-high and parked the health bar far above the figure
            // (ground_and_healthbar.jpg). VFX say nothing about where the mini's head is.
            if (r is not MeshRenderer && r is not SkinnedMeshRenderer)
            {
                notMesh++;
                continue;
            }
            // COUNTED, NOT SKIPPED. `includeInactive: false` filters by GAMEOBJECT active state,
            // never by Renderer.enabled, and the game's async material loader disables the
            // COMPONENT while its materials stream in (MaterialLoaderData.LoadMaterials). Such a
            // renderer still has live bones, so it is measured — but the count is printed, because
            // "this figure was measured mid-stream" is precisely the state that would otherwise be
            // invisible.
            if (!r.enabled)
                componentDisabled++;

            Bounds b = r.bounds;
            if (b.max.y > boxMaxY) boxMaxY = b.max.y;
            if (b.min.y < boxMinY) boxMinY = b.min.y;

            if (!FigureBody.TryLiveExtentY(r, out float rMinY, out float rMaxY, out int bones))
                continue;
            totalBones += bones;
            if (bones == 0 && r is SkinnedMeshRenderer)
                bonelessSkinned++;

            if (rMaxY > liveMaxY)
            {
                liveMaxY = rMaxY;
                tallest = r.name;
                tallestKind = r is SkinnedMeshRenderer
                    ? (bones > 0
                        ? $"SkinnedMeshRenderer, {bones} LIVE bone(s)"
                        : "SkinnedMeshRenderer with NO bone array — fell back to its BAKED box, "
                          + "which is the one state in which this rule is as stale as the old one")
                    : "MeshRenderer (its own live transform)";
            }
            if (rMinY < liveMinY)
                liveMinY = rMinY;
            used++;
        }
        RendererScratch.Clear();

        // ── THE ATTACHED PROP'S OWN BODY, AND THE ARCH IT STANDS IN ──────────────────────
        // USER, 2026-09-03: "die health bar soll dann ÜBER der tür schweben so wie bei Figuren
        // auch", and then "Die Tür selber ist ja in einen Torbogen eingebettet, ich will auch
        // nicht, dass die healtbar dann IN dem torbogen drin ist sondern darüber".
        //
        // THIS ACTOR HAS NO BODY OF ITS OWN. A prop configured for health is given an invisible
        // CObjectActor (a "PropDummyObject": one MeshRenderer, no mesh, no material, a
        // zero-size box), and that actor is what carries the bar. Measured against its own
        // subtree the loop above returns a degenerate extent, the vanilla fallback is used, and
        // the bar lands at the actor origin — inside the door. Its real body is its HOST PROP's
        // visual; see ActorPropBody for the link (CObjectActor.AttachedProp, the game's own
        // field, written only for a prop configured for health) and for why an ordinary door is
        // untouched by construction rather than by a name test.
        //
        // FOLDED IN AS AN EXTRA MEASURED RENDERER rather than handled as a special case, so it
        // rescues the two failure returns below (no usable renderer / degenerate extent) by the
        // same arithmetic that serves every other figure. It can only RAISE the answer.
        bool propBody = TryMeasureAttachedPropBody(tracked, out float propTopY, out float propMinY,
                                                   out float propArchTopY, out bool propArchKnown,
                                                   out string propReport);
        if (propBody)
        {
            if (propTopY > liveMaxY)
            {
                liveMaxY = propTopY;
                tallest = "the attached prop's own body";
                tallestKind = "the ATTACHED PROP (this actor's own subtree is empty — see "
                              + "ActorPropBody)";
            }
            if (propMinY < liveMinY) liveMinY = propMinY;
            if (propTopY > boxMaxY) boxMaxY = propTopY;
            if (propMinY < boxMinY) boxMinY = propMinY;
            used++;
        }

        // THE SECOND WALK IS PAID FOR ONLY BY THE LINES THAT PRINT. This string used to be
        // built on EVERY call — including the seven silent resamples per figure — and
        // CountAllRenderers inside it is a full includeInactive subtree walk.
        string census = !wantReport
            ? string.Empty
            : $"{used} mesh renderer(s) measured of {onActiveObjects} on ACTIVE objects "
              + $"({CountAllRenderers(tracked)} incl. inactive), {batched} static-batched "
              + $"and {notMesh} non-mesh skipped, {componentDisabled} measured with "
              + $"Renderer.enabled=false (materials still streaming); {totalBones} live bone(s) "
              + $"read across them, {bonelessSkinned} skinned renderer(s) had NO bones";

        if (used == 0)
        {
            if (wantReport)
                report = $"track {trackMode} y={track.y:F2}; MEASUREMENT FAILED — no usable "
                         + $"renderer: {census}; vanilla fallback {fallback:F2} wu{Hex(fallback)}"
                         + $"; ATTACHED PROP: {propReport}";
            return fallback;
        }

        float liveHeight = liveMaxY - liveMinY;
        if (liveHeight <= 0.01f)
        {
            // A one-bone skeleton, or every bone at the same height. The live rule has nothing to
            // say; the baked box is the only measurement left and the line says so rather than
            // returning a degenerate number.
            if (boxMaxY - boxMinY > 0.01f)
            {
                liveMinY = boxMinY;
                liveMaxY = boxMaxY;
                liveHeight = boxMaxY - boxMinY;
            }
            else
            {
                if (wantReport)
                    report = $"track {trackMode} y={track.y:F2}; MEASUREMENT FAILED — degenerate "
                             + $"live extent {liveHeight:F3} wu and a degenerate baked box too "
                             + $"({census}); vanilla fallback {fallback:F2} wu{Hex(fallback)}"
                             + $"; ATTACHED PROP: {propReport}";
                return fallback;
            }
        }

        // ── THE TOP OF THE FIGURE ─────────────────────────────────────────────────────────
        // The live extent, raised to the head joint if the skeleton did not reach it. NO slack
        // subtraction and NO box cap: see FigureBody for the two ModBuild 293 readings that
        // retired both. m_BasePoint is still read, but only so the line can print how far the
        // BAKED box reached below the figure's own ground — the number the retired correction used
        // to consume, now a diagnostic and nothing else.
        //
        // HOW THE GAME RESOLVES THE TWO LANDMARKS, read out of WorldspaceDisplayPanelBase.Init
        // (decompiled, lines 103-124):
        //   * the head joint is found BY NAME, once, at Init: FindInChildren("C_headSkel01_JNT").
        //     A character without that transform simply leaves m_HeadBonePoint null — UNLESS the
        //     panel is set to track the head bone, in which case the game logs "Unable to find
        //     head bone on character" and DEACTIVATES THE PANEL. So "no head joint" never means a
        //     bar in the wrong place; it means either this floor is absent (everything else
        //     unchanged) or there is no bar at all.
        //   * the base is found the same way, FindInChildren("Base"), and its absence likewise
        //     DEACTIVATES THE PANEL.
        //   * m_HeadBaseOffset is captured ONCE at Init as (head - base) and only feeds the
        //     HeadBoneStatic track mode; on a figure that rescales after Init it is stale for the
        //     session. None of the five figures in the ModBuild 293 log use that mode.
        // THE HEAD JOINT IS A LIVE TRANSFORM, which is the whole reason it can be trusted here: in
        // the 293 log SpittingDrakeID's head joint moved 0.57 -> 2.10 -> 2.16 wu while its baked
        // box never moved at all.
        Transform? basePoint = controller.m_BasePoint;
        bool baseKnown = basePoint != null;
        float baseY = baseKnown ? basePoint!.position.y : track.y;
        Transform? headBone = controller.m_HeadBonePoint;
        bool headKnown = headBone != null;
        float headY = headKnown ? headBone!.position.y : 0f;

        // ── THE HEAD WINS WHEN THERE IS ONE (ModBuild 335) ───────────────────────────────
        // USER, 2026-09-02, with hb_problem.jpg: "sie sind jetzt ZU hoch [...] wenn du die Hoehe
        // der Spielfigur und der der Draechen vergleichst, dass die Hoehen hier nicht passen."
        //
        // In that screenshot the hero's bar sits ~6 % of its own height above its helmet, and the
        // boss dragon's sits above its outstretched WINGTIPS — which are roughly twice as high as
        // its head. Both bars obey this file's rule exactly. The rule was the problem: `top` was
        // max(live extent, head joint), and on a spread-winged flyer the live extent IS the wings.
        //
        // ModBuild 294 replaced the baked box with the live extent for a good reason (a
        // SkinnedMeshRenderer's baked box is carried by the root bone and does not move), and that
        // stands. What it also did, unnoticed, was hand the anchor to whichever bone happens to be
        // highest — a wingtip, a raised weapon, a banner. "How tall is this creature" and "what is
        // the topmost point of its silhouette this frame" are different questions, and only the
        // first one is what a health bar is answering.
        //
        // THE EVIDENCE FOR THE HEAD WAS ALREADY IN THIS FILE, in the table below: on five figures
        // out of five, the artists' own m_WorldspaceOffsetY lands within 0.15 wu of the LIVE HEAD
        // JOINT — including ElderDrakeID (authored 3.50, head 3.28..3.43), the very boss whose bar
        // is wrong. Two independent sources agree on the head; nothing agrees on the wingtip. That
        // table was used to justify a FLOOR while the wings were still allowed to raise the bar
        // above it. Now it decides the anchor.
        //
        // The live extent is NOT deleted — it is the answer for every figure with no head joint
        // (props, obstacles, chests, and any character whose rig lacks m_HeadBonePoint), which is
        // exactly the population the head rule cannot serve. And the authored floor below still
        // rescues the quadruped whose head hangs BELOW its back (RendingDrakeElite, head 0.74
        // against authored 1.00).
        bool fromHead = headKnown;
        float top = headKnown
            ? headY
            : FigureBody.LiveTopY(liveMaxY, headY, headKnown: false, out _);

        // ── AND THE ARCH WINS OVER BOTH, WHEN THERE IS ONE ───────────────────────────────
        // ONE EXPRESSION, THREE CASES, and the middle one is the proof that this is not a door
        // special case:
        //   * a DESTRUCTIBLE DOOR standing in a stone frame — the arch top wins, so the bar
        //     clears the whole doorway and not just the leaf (leaf 3.31 wu, arch 4.6 wu in the
        //     ModBuild 396 log: that 1.3 wu gap IS the complaint);
        //   * a DESTRUCTIBLE PROP THAT IS NOT A DOOR — the query finds no gate column, says so
        //     in words, and the prop's own body decides, exactly as before;
        //   * an ORDINARY DOOR — no attached actor at all, therefore no bar, therefore nothing
        //     here is ever reached for it.
        // The arch number is NOT re-derived: it is read from WallSegmentFade, which computes it
        // for its own reason (the 2026-08-07 doorway ruling — the arch stays solid while the
        // wall around it fades). Two measurements of one arch are two numbers that drift.
        //
        // ONE-SIDED, like the head floor above it: this term may only RAISE the top. A figure
        // whose bar is already correct cannot be moved by it.
        bool fromProp = false;
        bool fromArch = false;
        if (propBody)
        {
            float propTop = propArchKnown ? Mathf.Max(propTopY, propArchTopY) : propTopY;
            if (propTop > top)
            {
                top = propTop;
                fromHead = false;
                fromProp = true;
                fromArch = propArchKnown && propArchTopY >= propTopY;
            }
        }

        // The height the clearance is a percentage OF is the live figure, not a padded box.
        float height = Mathf.Max(top - track.y, 0.01f);
        // Clear the top of the mini by ~12% of its own height, everything in board units.
        float clearance = 0.12f * height;
        float raw = (top - track.y) + clearance;
        // THE SOFT CEILING IS `fallback + height` BECAUSE `fallback` IS EVIDENCE — the artists'
        // own m_WorldspaceOffsetY for THIS character, which the table below shows agreeing with the
        // live head joint five times out of five. On an attached-prop actor it is nothing of the
        // kind: it is whatever the shared invisible PropDummyObject prefab was authored with, and
        // it says nothing about the door that actor is standing in. Letting it cap the arch term
        // would eat exactly the clearance the arch term exists to buy. So for that one population
        // the hard ceiling is the only bound, and it still bounds it.
        float softCeiling = fromProp ? AnchorHardCeilingWU : fallback + height;
        float hi = Mathf.Min(softCeiling, AnchorHardCeilingWU);
        float clamped = Mathf.Clamp(raw, AnchorFloorWU, hi);

        // ── THE GAME'S OWN AUTHORED OFFSET IS A FLOOR ─────────────────────────────────────
        // NEW EVIDENCE, and it is the strongest single reading in the ModBuild 293 log because it
        // comes from a source this class had been treating as a mere fallback. m_WorldspaceOffsetY
        // is authored PER CHARACTER by the game's artists, and on all five figures in that log it
        // lands within 0.15 wu of that character's LIVE HEAD JOINT:
        //
        //     BruteID              authored 1.87   head 1.84
        //     MindthiefID          authored 1.10   head 1.03
        //     SpittingDrakeID      authored 2.10   head 2.10 / 2.16  (measured IN FLIGHT)
        //     RendingDrakeEliteID  authored 1.00   head 0.74
        //     ElderDrakeID         authored 3.50   head 3.28 .. 3.43
        //
        // Two independent sources — what the artists typed and where the live skeleton is —
        // agreeing five times out of five. That is worth more than any rule this file can compute,
        // so the authored number stops being the value we fall back to when measurement FAILS and
        // becomes the value measurement may never go BELOW. It costs nothing where the figure is
        // taller than the artists assumed (every hero) and it rescues the two cases where the live
        // measurement legitimately under-reports: a quadruped whose head hangs below its back
        // (RendingDrakeElite, 0.83 measured against 1.00 authored) and a flyer measured while it
        // happens to be standing on the ground (SpittingDrake at adopt, head 0.57).
        //
        // It can only RAISE, and it is re-clamped to the hard ceiling so a nonsense authored value
        // cannot launch a bar out of the room.
        float beforeVanillaFloor = clamped;
        if (fallback > clamped)
            clamped = Mathf.Min(fallback, AnchorHardCeilingWU);

        // ── AND FINALLY THE PLAYER'S OWN OFFSET ──────────────────────────────────────────
        // LAST, after every rule above, and that ordering is the whole design. Everything before
        // this line answers "where is THIS creature's head"; this answers "how far above that do I
        // want the bar", which is one number for the whole board. Applied before the clamps it
        // would be silently eaten by the ceiling on exactly the tall figures where he is most
        // likely to reach for it. Bounded by the same hard ceiling so a mistyped value cannot
        // launch a bar out of the room, and floored so a bar can never sink below the track point.
        float userOffset = BarHeightOffsetWU;
        float beforeUserOffset = clamped;
        if (userOffset != 0f)
            clamped = Mathf.Clamp(clamped + userOffset, 0f, AnchorHardCeilingWU);

        measured = true;
        if (!wantReport)
            return clamped;

        string arm =
            clamped > beforeVanillaFloor
                ? $"the GAME'S OWN AUTHORED OFFSET, used as a floor ({beforeVanillaFloor:F2} -> "
                  + $"{clamped:F2} wu) — the live measurement came out BELOW what this character's "
                  + "own m_WorldspaceOffsetY asks for, which on a quadruped means its head hangs "
                  + "below its back, and on a flyer means it was measured while grounded"
                : raw < AnchorFloorWU
                    ? $"the {AnchorFloorWU:F2} wu FLOOR"
                    : raw > hi
                        ? (softCeiling <= AnchorHardCeilingWU
                            ? $"the fallback+height CEILING ({fallback:F2}+{height:F2}={softCeiling:F2} wu)"
                            : $"the {AnchorHardCeilingWU:F1} wu HARD CEILING (fallback+height would have "
                              + $"allowed {softCeiling:F2})")
                        : "NOTHING — the raw offset stands";

        // WHICH TERM PRODUCED THE TOP. A rule that silently stops applying is a rule nobody can
        // falsify, so the line names the winner rather than leaving it to arithmetic.
        string topWhy = fromProp
            ? (fromArch
                ? "the DOORWAY ARCH this prop stands in (2026-09-03 ruling: the bar must clear the "
                  + "whole Torbogen, not the door leaf). The arch top is READ from WallSegmentFade, "
                  + "never re-derived here"
                : "the ATTACHED PROP'S OWN BODY — this actor is an invisible PropDummyObject and has "
                  + "no geometry of its own; its host prop is what the player sees. No arch was "
                  + "involved (see ATTACHED PROP below for whether there was none or none found)")
            : fromHead
            ? $"the LIVE HEAD JOINT (ModBuild 335: the head DECIDES when a figure has one). It "
              + $"stands {headY - liveMaxY:F2} wu above the live extent of every renderer measured "
              + "— a NEGATIVE number here is the normal case for a winged or weapon-carrying "
              + "figure and is exactly what this rule exists to ignore"
            : "the LIVE EXTENT (bones for skinned meshes, own transform for props) — this figure "
              + "has NO head joint, which is the only case the extent still decides";

        string head = headKnown
            ? $"head joint at {headY - track.y:F2} wu above the track point"
            : "no head joint on this character (the floor is absent; nothing else changes)";

        // THE BAKED BOX, PRINTED AND USED FOR NOTHING. It is here so the next hardware log can
        // check the claim this whole rule rests on — that the box is not the figure — instead of
        // taking it on trust. On the ModBuild 293 boss the two differed by more than a metre of
        // board; on a humanoid they should very nearly agree.
        float boxUnderBase = baseKnown ? Mathf.Max(0f, baseY - boxMinY) : 0f;
        string baked = $"BAKED BOX (decides nothing now) y {boxMinY:F2}..{boxMaxY:F2}"
                       + (baseKnown
                           ? $", reaching {boxUnderBase:F2} wu below the base at y {baseY:F2}"
                           : ", no base point on this controller")
                       + $"; the old ModBuild 293 rule would have given "
                       + $"{(boxMaxY - boxUnderBase - track.y) + 0.12f * Mathf.Max(boxMaxY - boxUnderBase - track.y, 0.01f):F2} wu";

        string userSays = userOffset == 0f
            ? "; [WorldUI] BarHeightOffset is 0, so the measurement stands unmodified"
            : $"; then the PLAYER'S OWN [WorldUI] BarHeightOffset of {userOffset:+0.00;-0.00} wu "
              + $"moved it {beforeUserOffset:F2} -> {clamped:F2} wu"
              + (Mathf.Approximately(clamped, AnchorHardCeilingWU)
                  ? " (AT the hard ceiling — a larger value will do nothing)"
                  : Mathf.Approximately(clamped, 0f) ? " (AT the floor — it cannot go lower)" : "");
        report = $"track {trackMode} y={track.y:F2}; {census}; {head}; LIVE extent y "
                 + $"{liveMinY:F2}..{liveMaxY:F2} => live height {liveHeight:F2} wu{Hex(liveHeight)}; "
                 + $"TALLEST '{tallest}' ({tallestKind}); "
                 + $"TOP {top:F2} wu from {topWhy}; raw offset = (top-track) "
                 + $"{top - track.y:F2} + 12% clearance {clearance:F2} = {raw:F2} wu; BOUND BY "
                 + $"{arm} => {beforeUserOffset:F2} wu{Hex(beforeUserOffset)}{userSays}; {baked}; the game's own authored "
                 + $"m_WorldspaceOffsetY is {fallback:F2} wu, which is "
                 + (headKnown
                     ? $"{Mathf.Abs(fallback - (headY - track.y)):F2} wu from this character's live "
                       + "head joint — the agreement that made it a floor"
                     : "the only landmark this character offers")
                 + $"; ATTACHED PROP: {propReport}";
        return clamped;
    }

    /// <summary>
    /// THE BODY AN INVISIBLE ACTOR ACTUALLY HAS, and the arch it stands in.
    ///
    /// <para>Both terms and every failure reason land in <paramref name="report"/>, which the
    /// caller prints on the anchor line. A MISSING ARCH AND AN ARCH OF HEIGHT ZERO MUST NOT READ
    /// THE SAME — that is why the arch term is a bool plus a sentence and not a float that happens
    /// to be 0.</para>
    ///
    /// <para>Returns false for every ordinary miniature (no attached prop) and while a health
    /// prop's visual has not spawned yet — in both cases the caller's existing rules stand
    /// untouched. It also returns false while the prop is IN SOMEBODY'S HAND: the body is then
    /// riding the hand, its world bounds are wherever the player is holding it, and measuring an
    /// anchor off that would park the bar in mid-air. The bar keeps the height it had.</para>
    /// </summary>
    private static bool TryMeasureAttachedPropBody(
        GameObject tracked, out float topY, out float minY,
        out float archTopY, out bool archKnown, out string report)
    {
        topY = 0f;
        minY = 0f;
        archTopY = 0f;
        archKnown = false;

        ActorBehaviour behaviour = ActorBehaviour.GetActorBehaviour(tracked);
        CObjectProp? prop = ActorPropBody.PropFor(behaviour);
        if (prop == null)
        {
            report = "none — this actor is not attached to a prop, which is every ordinary "
                     + "miniature and every summon. Nothing on this path applied";
            return false;
        }
        if (ActorPropBody.IsHeld(behaviour))
        {
            report = $"'{prop.InstanceName}' {prop.ObjectType} is IN A HAND right now, so its body "
                     + "is wherever the player is holding it; no re-measurement, the bar keeps the "
                     + "height it had";
            return false;
        }
        GameObject? visual = ActorPropBody.BodyFor(behaviour);
        if (visual == null)
        {
            report = $"'{prop.InstanceName}' {prop.ObjectType} — configured for health, but its "
                     + "visual has not been resolved yet (retried; see the HEALTH-PROP BODY census "
                     + "line). The anchor falls back to this actor's own empty subtree, which is "
                     + "the pre-fix behaviour";
            return false;
        }

        RendererScratchAll.Clear();
        visual.GetComponentsInChildren(includeInactive: false, RendererScratchAll);
        int onActive = RendererScratchAll.Count;
        int measured = 0;
        float hi = float.MinValue, lo = float.MaxValue;
        for (int i = 0; i < RendererScratchAll.Count; i++)
        {
            Renderer r = RendererScratchAll[i];
            // Same two exclusions the miniature walk makes, and for the same reasons: a statically
            // batched renderer reports its whole batch, and a particle/trail/line renderer reports
            // an effect VOLUME rather than a surface.
            if (r == null || r.isPartOfStaticBatch)
                continue;
            if (r is not MeshRenderer && r is not SkinnedMeshRenderer)
                continue;
            Bounds b = r.bounds;
            if (b.size.sqrMagnitude <= 1e-8f)
                continue;
            if (b.max.y > hi) hi = b.max.y;
            if (b.min.y < lo) lo = b.min.y;
            measured++;
        }
        RendererScratchAll.Clear();

        archKnown = WallSegmentFade.TryGetArchTopY(visual, out archTopY, out string archHow);

        if (measured == 0)
        {
            report = $"'{prop.InstanceName}' {prop.ObjectType} resolved to '{visual.name}', but NOT "
                     + "ONE of its renderers has a non-degenerate box — the body is there and has "
                     + $"nothing to measure. ARCH: {(archKnown ? $"top {archTopY:F2} wu from " + archHow : archHow)}";
            return false;
        }

        topY = hi;
        minY = lo;
        report = $"'{prop.InstanceName}' {prop.ObjectType} -> '{visual.name}', body y {lo:F2}..{hi:F2} "
                 + $"from {measured} mesh renderer(s) of {onActive} on active objects; "
                 + "ARCH: " + (archKnown
                     ? $"top {archTopY:F2} wu from {archHow} — the arch stands {archTopY - hi:F2} wu "
                       + "above the prop's own body, and a POSITIVE number here is the whole of the "
                       + "2026-09-03 Torbogen complaint"
                     : archHow);
        return true;
    }

    /// <summary>
    /// Renderer count INCLUDING inactive GameObjects, for the census in the anchor line. Walked
    /// only on a frame that is about to LOG (adopt, or a resample that changed the answer), never
    /// on the silent samples — the difference between this number and the active-object count is
    /// the whole "was the figure finished when we measured it?" question, and it is worth one
    /// extra walk on the rare frames that print.
    /// </summary>
    private static int CountAllRenderers(GameObject tracked)
    {
        RendererScratchAll.Clear();
        tracked.GetComponentsInChildren(includeInactive: true, RendererScratchAll);
        int n = RendererScratchAll.Count;
        RendererScratchAll.Clear();
        return n;
    }

    /// <summary>
    /// A world-unit length re-stated in HEX WIDTHS — the only unit a reader of this log has any
    /// intuition for. Empty when the game's tile size is not resolvable, so the line never invents
    /// a number.
    /// </summary>
    private static string Hex(float worldUnits)
    {
        float hex = UnityGameEditorRuntime.s_TileSize.x;
        return hex > 1e-4f ? $" ({worldUnits / hex:F2} hex)" : string.Empty;
    }

    /// <summary>
    /// ONE LINE PER ADOPTION saying where this bar was parked and WHY.
    ///
    /// <para>WRITTEN BECAUSE THERE WAS NOTHING. Every <c>ActorBar</c> line this class has ever
    /// produced is about DRAW ORDER or SIZE; not one of them names a height. So the boss-dragon
    /// report ("die Health-Bar von allen Drachen ist falsch … mitten in ihm statt darüber",
    /// Drachen.jpg) arrived with a hardware log that could not distinguish a clamp that BIT from a
    /// measurement that never RAN — two causes with two different fixes. The line therefore names
    /// the ARM, not just the number: a clamp that bound the result must be visible without doing
    /// arithmetic on the other fields.</para>
    /// </summary>
    private static void LogAnchor(string what, string label, float offset, string report)
    {
        // HW-VERIFY: the 2026-09-03 round asks where a destructible door's health bar was parked and
        // WHY, and this is the only line that answers it (the ATTACHED PROP clause names the prop
        // body and the arch, and which of the two won). It was VRLog.Info — the DEBUG tier, absent
        // from a default-level log — so the tier is promoted and the text is left exactly as it was.
        // Bounded by construction: one line per adoption plus at most AnchorSampleBudget resamples
        // that actually CHANGED the answer, per bar.
        VRLog.Note("WorldUI",
            $"BAR ANCHOR {what} '{label}': {offset:F2} wu{Hex(offset)} above the track point — {report}");
    }

    /// <summary>
    /// The figure's class id (the same vocabulary <c>FigureGrab</c> writes, so a hardware log
    /// reads as one story), falling back to the tracked object's name.
    /// </summary>
    private static string LabelOf(WorldspacePanelUIController controller, ActorBehaviour? actor)
    {
        CActor? ca = actor != null ? actor.Actor : null;
        if (ca != null && ca.Class != null)
            return ca.Class.ID;
        GameObject tracked = controller.m_ObjectToTrack;
        return tracked != null ? tracked.name : "?";
    }

    /// <summary>
    /// Re-measure this bar's anchor while its sample budget lasts (see
    /// <see cref="AnchorSampleBudget"/>), and adopt the new value when it RISES by more than
    /// <see cref="AnchorResampleTolerance"/>. Silent otherwise.
    ///
    /// <para><b>THE WINDOW KEEPS ITS MAXIMUM, AND THAT IS THE FIX FOR THE JITTER.</b> In the
    /// ModBuild 293 log <c>ElderDrakeID</c>'s anchor was re-adopted SEVEN times in one session —
    /// 5.09, 4.79, 5.14, 4.81, 5.05, 4.88, 5.10 wu — oscillating 0.35 wu with no trend, because the
    /// number it was measuring (a baked box carried by a bobbing root bone) oscillated. A bar that
    /// bobs 0.35 wu of board is visible, and no tolerance fixes it: the samples genuinely differ.
    /// What fixes it is asking a different question. The anchor must clear the figure at the
    /// TALLEST moment of its animation cycle, because an anchor set at the bottom of the cycle dips
    /// inside the figure at the top of it — which is precisely the "mitten drin" complaint. So the
    /// window keeps the highest reading it has seen and ignores every lower one. That is monotone,
    /// so it converges and stops logging on its own: the budget still bounds the work, but the
    /// value latches because it runs out of things to rise to, not because a timer said so.</para>
    ///
    /// <para>The cost of being wrong in this direction is a bar that sits a little high on a figure
    /// that briefly reared up and then settled; the cost of the other direction is a bar drawn
    /// through the figure's chest. The user has reported the second one twice and the first one
    /// never.</para>
    /// </summary>
    private static void ResampleAnchor(Adopted adopted, WorldspacePanelUIController controller, float now)
    {
        // A BAR WHOSE BODY HAS NOT ARRIVED HAS NOT BEEN MEASURED YET, so its budget must not run
        // out (2026-09-03). An attached-prop actor's body is its host prop's visual, and that
        // visual is resolved asynchronously — the ModBuild 396 [Props] census still read
        // "14 still unresolved" seconds into the scenario. Eight samples over four seconds is a
        // budget sized for an animation cycle, not for a prop spawn, so while the body is genuinely
        // missing the budget is re-armed rather than spent. It is re-armed and never merely held:
        // the moment the body resolves, the ordinary budget applies and this stops.
        if (adopted.AnchorSamplesLeft <= 0 && adopted.AttachedPropActor
            && ActorPropBody.BodyFor(adopted.Actor) == null)
        {
            adopted.AnchorSamplesLeft = 1;
        }
        if (adopted.AnchorSamplesLeft <= 0 || now < adopted.NextAnchorSample)
            return;
        adopted.AnchorSamplesLeft--;
        adopted.NextAnchorSample = now + AnchorSampleIntervalSeconds;

        // Silent sample: no census walk, no strings. The report costs a SECOND subtree walk and is
        // paid for only by the samples that turn out to be news.
        float offset = MeasureAnchorOffsetWU(controller, wantReport: false, out _, out _);
        float current = adopted.AnchorOffsetWU;
        if (offset <= current + AnchorResampleTolerance * Mathf.Max(Mathf.Abs(offset), Mathf.Abs(current)))
            return;

        // It rose. Measure once more WITH the report so the line explains the value it prints
        // rather than the one before it — and if that confirming read comes back at or below the
        // adopted value (the animation moved between the two reads), keep the adopted value and say
        // nothing. A log line is a claim; a claim about a number we then discard is noise.
        offset = MeasureAnchorOffsetWU(controller, wantReport: true, out string report, out _);
        if (offset <= current)
            return;
        LogAnchor($"RESAMPLED UP (was {current:F2} wu)",
                  LabelOf(controller, adopted.Actor), offset, report);
        adopted.AnchorOffsetWU = offset;
    }

    private static void Adopt(WorldspacePanelUIController controller)
    {
        ConvertedPanel? panel = CanvasConversion.Convert(
            controller.transform as RectTransform, "ActorBar", pokeable: false,
            sortingOrder: BarHostSortingOrder);
        if (panel == null)
            return;

        // MR backing opt-out (user ruling 2026-08-04): MrBacking's panel sweep plates EVERY
        // converted host by default, and the bar's host rect is far larger than the thin
        // health/summon band actually drawn inside it — in see-through mode the host-rect plate
        // rendered as a solid dark RECTANGLE floating over the miniature / through the middle of
        // the health bar. The bar sits over the board (never over bare room), so it needs no
        // readability plate at all. Set per ADOPT on the fresh ConvertedPanel, so the constant
        // create/destroy churn of pooled bar controllers re-applies it on every re-adoption.
        panel.MrBackingSuppressed = true;

        // Round-robin rescan phase (S2 defect 7). Handed out at ADOPT, which is where the
        // synchronisation was created: every controller of a revealed room is adopted in the same
        // Tick, so every bar shipped with an identical scan clock. The offset is strictly less than
        // one full interval, and it is only ever SUBTRACTED from a reschedule (see LateTick), so it
        // can only make a scan happen earlier.
        float phase = DepthScanIntervalSeconds
                      * (s_depthScanPhaseSeq++ & (DepthScanPhaseBuckets - 1))
                      / DepthScanPhaseBuckets;

        float anchorOffset = MeasureAnchorOffsetWU(controller, wantReport: true, out string anchorReport, out _);

        Adoptions[controller] = new Adopted
        {
            Controller = controller,
            Panel = panel,
            ScanPhase = phase,
            AnchorOffsetWU = anchorOffset,
            AnchorSamplesLeft = AnchorSampleBudget,
            NextAnchorSample = Time.unscaledTime + AnchorSampleIntervalSeconds,
            Actor = controller.m_ObjectToTrack != null
                ? ActorBehaviour.GetActorBehaviour(controller.m_ObjectToTrack)
                : null,
            AttachedPropActor = controller.m_ObjectToTrack != null
                && ActorPropBody.PropFor(
                       ActorBehaviour.GetActorBehaviour(controller.m_ObjectToTrack)) != null,
        };
        Owned.Add(controller);
        LogAnchor("at ADOPT", LabelOf(controller, Adoptions[controller].Actor), anchorOffset, anchorReport);

        // Item 5a part 1 — segment the HealthBar per max-HP. Push a valid zoom exactly
        // once at adopt: the RTS-camera UnityEvent that normally does this never fires in
        // VR (see BarZoom). Once suffices — HealthBar.zoom then holds a valid value and
        // every later UpdateHealth re-pools maxHealth-1 marks on its own. Idempotent if a
        // pooled controller is re-adopted (the controller guards on its cached zoom).
        //
        // Item 5a part 2 (damage preview) needs NO hook here: it is driven entirely by the
        // game's Choreographer targeting-focus messages — OnSelectingAttackFocus /
        // OnSelectingDamageFocus / PreviewDamage → HealthBar.PreviewAttack + Focus(true)
        // (Choreographer.cs, DistributeDamageService.cs) — off the SHARED game cursor, which
        // the VR board pick already feeds (BoardPick → MF.FindInteractableAtMousePosition +
        // InputManager.CursorPosition patches). Because we adopt the game's LIVE panel, any
        // preview the rules engine drives lands on this same component and billboards with
        // the bar. If a preview is ever observed missing, the gap is upstream in the
        // targeting message flow (the VR pick not yielding the target-selection the engine
        // keys those messages off) — to be fixed in the board pick/click path, never by
        // fabricating a CAttackSummary here (that would risk showing wrong damage).
        //
        // GUARD: OnUpdatedZoom is a GAME method and it NREs on a panel whose HealthBar isn't
        // wired yet (observed on freshly-spawned 100x100 placeholder ActorBars — attributed
        // by TickGuard to ActorBars.Tick → Adopt → WorldspacePanelUIController.OnUpdatedZoom).
        // The adoption itself already succeeded above (recorded in Adoptions/Owned), so this
        // initial zoom is best-effort only: skipping it just defers segmentation to the next
        // UpdateHealth, which re-pools the marks on its own. Swallow + log once so one unready
        // bar can't spew a per-frame exception into the game's own logger.
        try
        {
            controller.OnUpdatedZoom(BarZoom);
        }
        catch (System.Exception ex)
        {
            if (!s_zoomWarned)
            {
                s_zoomWarned = true;
                VRLog.Warn("WorldUI", $"ActorBar OnUpdatedZoom deferred — bar not ready at adopt " +
                                      $"({ex.GetType().Name}); segmentation applies on the next health update.");
            }
        }
    }

    /// <summary>One-shot guard so a not-ready ActorBar's OnUpdatedZoom NRE is logged once, not per frame.</summary>
    private static bool s_zoomWarned;

    private static readonly List<Graphic> GraphicScratch = new(64);

    /// <summary>
    /// ONE walk over every Graphic under the adopted bar host, doing the two per-graphic
    /// treatments this class owns. Slow cadence (<see cref="DepthScanIntervalSeconds"/>) because
    /// <c>HealthBar</c> pools new division-mark Graphics long after adopt; idempotent per graphic
    /// via the instance-ID sets, originals snapshotted for restore, and only graphics under hosts
    /// ActorBars owns/adopts are ever touched.
    ///
    /// <para>1. DEPTH TEST (<paramref name="depthTest"/> = the [WorldUI] BarsOccluded gate): assign
    /// a per-instance copy of the graphic's material with <c>unity_GUIZTestMode</c> = LEqual(4).
    /// Unity's UI/Default shader declares <c>ZTest [unity_GUIZTestMode]</c> and the per-material
    /// value beats the global, so world-space bar pixels are occluded by wall depth while the bar
    /// stays enabled and billboarding — no toggling. TMP distance-field text: its SDF shaders use
    /// the same <c>unity_GUIZTestMode</c> bracket in UI mode; some variants expose
    /// <c>_ZTestMode</c> instead — both are set (unconditionally for the former, since it is a
    /// bracket lookup and not a declared Property, HasProperty-guarded for the latter).</para>
    ///
    /// <para>2. RAYCAST (always): clear <c>raycastTarget</c>. See <see cref="Adopted.RaycastOff"/> —
    /// the bar is display-only, but its host still carries the GraphicRaycaster every conversion
    /// adds, so its wide invisible rect would otherwise register hits for any
    /// <c>EventSystem.RaycastAll</c> consumer in front of the figure/panel behind it.</para>
    /// </summary>
    private static void ScanBarGraphics(Adopted adopted, string barName, bool depthTest)
    {
        GameObject host = adopted.Panel.HostGo;
        if (host == null)
            return;

        GraphicScratch.Clear();
        host.GetComponentsInChildren(includeInactive: true, GraphicScratch);
        int added = 0;
        int unraycast = 0;
        for (int i = 0; i < GraphicScratch.Count; i++)
        {
            Graphic g = GraphicScratch[i];
            if (g == null)
                continue;
            int id = g.GetInstanceID();

            if (!adopted.RaycastOffIds.Contains(id))
            {
                try
                {
                    adopted.RaycastOffIds.Add(id);
                    if (g.raycastTarget)
                    {
                        adopted.RaycastOff.Add((g, true));
                        g.raycastTarget = false;
                        unraycast++;
                    }
                }
                catch
                {
                    // Leave this graphic vanilla; the ID stays recorded (a graphic that throws
                    // here throws every scan) — the depth pass below is independent of it.
                }
            }

            if (!depthTest || adopted.DepthMatIds.Contains(id))
                continue;
            try
            {
                Material src = g.material;
                if (src == null)
                    continue; // not wired yet — retried next scan
                var inst = new Material(src);
                // Not a declared shader Property (bracket lookup only) ⇒ HasProperty is false;
                // SetInt still creates the per-material override that wins over the global.
                inst.SetInt("unity_GUIZTestMode", (int)CompareFunction.LessEqual);
                if (inst.HasProperty("_ZTestMode"))
                    inst.SetInt("_ZTestMode", (int)CompareFunction.LessEqual);
                g.material = inst;
                adopted.DepthMatIds.Add(id);
                adopted.DepthMats.Add((g, src, inst));
                added++;
            }
            catch
            {
                // Leave this graphic vanilla; retried on the next scan.
            }
        }
        GraphicScratch.Clear();

        if (unraycast > 0)
        {
            VRLog.Debug("WorldUI",
                $"bar raycast: cleared raycastTarget on {unraycast} graphics ('{barName}') — the " +
                "bar's invisible rect no longer catches pointer hits meant for what is behind it.");
        }

        if (added > 0)
        {
            if (!adopted.DepthLogged)
            {
                adopted.DepthLogged = true;
                VRLog.Info("WorldUI",
                    $"bar depth-test: forced unity_GUIZTestMode=LEqual on {added} graphics ('{barName}').");
            }
            else
            {
                VRLog.Debug("WorldUI",
                    $"bar depth-test: +{added} late graphics on '{barName}' ({adopted.DepthMats.Count} total).");
            }
        }
    }

    /// <summary>
    /// Mirror of the adopted-state restore: give every touched Graphic its original material back
    /// and destroy our per-instance copies. Called on release, shutdown (ReleaseAll) and live
    /// config-off.
    /// </summary>
    private static void RestoreBarDepthTest(Adopted adopted)
    {
        for (int i = 0; i < adopted.DepthMats.Count; i++)
        {
            (Graphic g, Material orig, Material inst) = adopted.DepthMats[i];
            if (g != null)
            {
                try { g.material = orig; }
                catch { /* graphic destroyed under us */ }
            }
            if (inst != null)
            {
                try { Object.Destroy(inst); }
                catch { /* already gone with the scene */ }
            }
        }
        adopted.DepthMats.Clear();
        adopted.DepthMatIds.Clear();
        adopted.DepthLogged = false;
        adopted.NextDepthScan = 0f;
        // A live config flip calls this for EVERY bar in one frame, which would re-synchronise the
        // rescan clocks the phase offset exists to separate. Re-arm the offset so the first
        // reschedule after the forced scan spreads them again (still a subtraction: never later).
        adopted.ScanPhased = false;
    }

    /// <summary>
    /// Mirror of the raycast treatment: give every touched Graphic its original
    /// <c>raycastTarget</c> back. Called on release and shutdown (ReleaseAll) — the game keeps
    /// these panels in a pool, so a bar handed back must be exactly as it was handed over.
    /// </summary>
    private static void RestoreBarRaycast(Adopted adopted)
    {
        for (int i = 0; i < adopted.RaycastOff.Count; i++)
        {
            (Graphic g, bool raycast) = adopted.RaycastOff[i];
            if (g == null)
                continue;
            try { g.raycastTarget = raycast; }
            catch { /* graphic destroyed under us */ }
        }
        adopted.RaycastOff.Clear();
        adopted.RaycastOffIds.Clear();
    }

    private static void Release(WorldspacePanelUIController controller)
    {
        // NOTE: the key may be Unity-dead ("== null" true) but the CLR reference is
        // still a valid dictionary key — always use it for the map ops.
        if (Adoptions.TryGetValue(controller, out Adopted adopted))
        {
            RestoreBarDepthTest(adopted);
            RestoreBarRaycast(adopted);
            CanvasConversion.Release(adopted.Panel);
        }
        Adoptions.Remove(controller);
        Owned.Remove(controller);
    }

    internal static void ReleaseAll()
    {
        foreach (KeyValuePair<WorldspacePanelUIController, Adopted> pair in Adoptions)
        {
            RestoreBarDepthTest(pair.Value);
            RestoreBarRaycast(pair.Value);
            CanvasConversion.Release(pair.Value.Panel);
        }
        Adoptions.Clear();
        Owned.Clear();
        // Let the next scenario state its bar size once more: the base world scale is derived per
        // scenario from the tile size, so the same dials can resolve to different millimetres.
        s_loggedSizeFactor = float.NaN;
        s_loggedWorldScale = float.NaN;
        s_nextSizeLog = 0f;
    }
}

/// <summary>
/// Prefix-skips the game's per-frame transform writers for ADOPTED actor bars only.
/// Vanilla behavior everywhere else (returns true). Signatures verified via ilspycmd
/// (GH.Runtime.dll): <c>public void TrackCharacter()</c> (IL 318 B, PATCH-TARGETS §1.7 ✅)
/// and <c>private void LateUpdate()</c> (Unity magic method — invoked directly by the
/// engine, never inlined).
/// </summary>
[HarmonyPatch(typeof(WorldspaceDisplayPanelBase))]
internal static class WorldspaceDisplayPanelBase_Patches
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(WorldspaceDisplayPanelBase.TrackCharacter))]
    private static bool TrackCharacter_Prefix(WorldspaceDisplayPanelBase __instance)
        => !ActorBars.Owns(__instance);

    [HarmonyPrefix]
    [HarmonyPatch("LateUpdate")]
    private static bool LateUpdate_Prefix(WorldspaceDisplayPanelBase __instance)
        => !ActorBars.Owns(__instance);
}
