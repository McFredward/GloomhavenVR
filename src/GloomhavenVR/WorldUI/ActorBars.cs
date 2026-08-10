using System.Collections.Generic;
using BepInEx.Configuration;
using GloomhavenVR.Board.FigureGrab;
using GloomhavenVR.Core;
using HarmonyLib;
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
/// THE EYE and tuned by [WorldUI] BarSizeScale, following the table zoom between the
/// [WorldUI] BarZoomMinScale / BarZoomMaxScale bounds (see <see cref="ResolveZoomFollow"/>);
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

    /// <summary>
    /// Bind-once for the [WorldUI] BarsOccluded entry. Extracted from the property getter so the
    /// in-VR config browser can force the file into existence (<c>ConfigCatalog.EnsureBound</c>)
    /// instead of hiding this setting until the first bar scan happens to run. Pure — it creates
    /// the config file and binds one key, and touches nothing else.
    /// </summary>
    internal static void BindConfig()
    {
        if (s_barsOccluded != null)
            return;
        s_barsConfigFile = ModuleConfig.Create("bars");
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

    /// <summary>Patch gate: true when the game must NOT drive this panel's transform.</summary>
    internal static bool Owns(WorldspaceDisplayPanelBase panel) => Owned.Contains(panel);

    internal static void Tick()
    {
        bool want = WorldUIConfig.ActorBars.Value && WorldUIConfig.ConversionActive
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
        // können"). Three more frame-constant reads, hoisted for the same reason as the three above.
        float barSizeScale = Mathf.Max(0.01f, WorldUIConfig.BarSizeScale.Value);
        float rawLo = WorldUIConfig.BarZoomMinScale.Value;
        float rawHi = WorldUIConfig.BarZoomMaxScale.Value;
        // A hand-edited cfg can put the floor above the ceiling. Sorting them is the only reading
        // that keeps BOTH numbers meaningful; Mathf.Clamp with min > max would silently return the
        // min for every input and turn the pair into one value.
        float sizeLo = Mathf.Min(rawLo, rawHi);
        float sizeHi = Mathf.Max(rawLo, rawHi);
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
                if (hide && controller != null && controller.FlowControlActive())
                    hide = false;
            }

            // Anchor in BOARD units (P6 fix #4): the cached bounds-derived offset
            // scales with the diorama by construction — zooming the table keeps the
            // bar exactly above the miniature instead of inside it.
            bool haveTrack = TryGetTrackPoint(controller, out Vector3 track);
            Vector3 pos = track + Vector3.up * pair.Value.AnchorOffsetWU;

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
            // the table zoom inside the Min/Max bounds (ResolveZoomFollow). The default path's
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
    /// raw follow factor, before <c>[WorldUI] BarZoomMinScale</c>/<c>BarZoomMaxScale</c> clamp it.
    ///
    /// <para>WHICH SIZE IS "THE SIZE". The mod's zoom is a scale on the RIG, not on the board
    /// (<c>VRRigDriver</c>: <c>rigRoot.localScale = baseScale × ClampedSavedMultiplier</c>), so a
    /// world-unit size and a real-metre size are two different quantities that drift apart every
    /// time the player pinches: <c>worldUnits = realMetres × WorldScale</c>. The bar is a
    /// READABILITY OVERLAY, so the size that matters is the one at the EYE, in real millimetres —
    /// it is the number the player judges ("too big"), the number this class logs, and the only one
    /// a minimum and a maximum can be stated in without the bound itself moving when the player
    /// zooms. Every dial and every bound here is therefore a factor of a REAL-MILLIMETRE base
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
    /// <para>THE GUARANTEE the bounds then give, in the same unit as the size: at ANY zoom the bar
    /// is between <c>BarSizeScale × Min</c> and <c>BarSizeScale × Max</c> of its shipped
    /// millimetres. With the shipped 0.7/1.5 that is 0.25 mm/px at the far end of zooming out and
    /// 0.53 mm/px at the near end, never more, never less — and setting Min = Max pins the bar to
    /// one real size at every zoom, which is the behaviour that shipped before this dial existed.
    /// </para>
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
            $"bar size: {mmPerPixel:F3} mm per uGUI px at the eye (shipped {shippedMmPerPixel:F3}) — " +
            $"{sample}. Size dial {dial:F2}x, zoom follow {follow:F2} clamped into " +
            $"[{lo:F2}, {hi:F2}]{bound} ⇒ resolved {sizeFactor:F2}x. World scale {worldScale:F2} " +
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
    /// Board-space anchor height above the track point, from the miniature's renderer
    /// bounds (world-space AABB — already in board units). Vanilla equivalent: the game
    /// adds <c>m_WorldspaceOffsetY</c> (world units, from <c>Init(..., float height =
    /// 1.8f)</c>) before projecting to the screen (decompiled WorldspaceDisplayPanel
    /// Base.cs:186, WorldspacePanelUIController.cs:94); we anchor at the actual bounds
    /// top (tighter than the fixed 1.8) with the vanilla offset as the fallback.
    /// Called once per adoption — the GetComponentsInChildren allocation is a rare,
    /// per-actor-spawn event, not per-frame.
    /// </summary>
    private static float ComputeAnchorOffsetWU(WorldspacePanelUIController controller)
    {
        float fallback = Mathf.Max(controller.m_WorldspaceOffsetY, 0.2f);

        GameObject tracked = controller.m_ObjectToTrack;
        if (tracked == null || !TryGetTrackPoint(controller, out Vector3 track))
            return fallback;

        RendererScratch.Clear();
        tracked.GetComponentsInChildren(includeInactive: false, RendererScratch);
        if (RendererScratch.Count == 0)
            return fallback;

        float maxY = float.MinValue;
        float minY = float.MaxValue;
        for (int i = 0; i < RendererScratch.Count; i++)
        {
            Renderer r = RendererScratch[i];
            // A statically batched renderer reports the bounds of its ENTIRE combined batch
            // (documented Unity behaviour), not its own mesh — a mini folded into a 'Maps'
            // batch would read as tall as the whole map chunk. Skip; the vanilla fixed offset
            // below is the fallback, exactly the height the flat game uses.
            if (r.isPartOfStaticBatch)
                continue;
            // Only the miniature's SURFACE geometry may define "the top of the figure".
            // Particle, trail and line renderers report their effect VOLUME: the Elementalist's
            // infusion VFX measured sky-high and parked the health bar far above the figure
            // (ground_and_healthbar.jpg). VFX say nothing about where the mini's head is.
            if (r is not MeshRenderer && r is not SkinnedMeshRenderer)
                continue;
            Bounds b = r.bounds;
            if (b.max.y > maxY) maxY = b.max.y;
            if (b.min.y < minY) minY = b.min.y;
        }
        RendererScratch.Clear();

        float height = maxY - minY;
        if (height <= 0.01f)
            return fallback; // degenerate bounds (still spawning) — vanilla height

        // Clear the top of the mini by ~12% of its own height, everything in board units.
        // Hard ceiling 6 wu on top of everything else: the tallest boss mini is ~5 wu, so any
        // larger figure "height" is a mismeasured bound, not a figure — better a bar slightly
        // low on a giant than one floating in the sky.
        float offset = (maxY - track.y) + 0.12f * height;
        return Mathf.Clamp(offset, 0.05f, Mathf.Min(fallback + height, 6f));
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

        Adoptions[controller] = new Adopted
        {
            Controller = controller,
            Panel = panel,
            ScanPhase = phase,
            AnchorOffsetWU = ComputeAnchorOffsetWU(controller),
            Actor = controller.m_ObjectToTrack != null
                ? ActorBehaviour.GetActorBehaviour(controller.m_ObjectToTrack)
                : null,
        };
        Owned.Add(controller);

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
