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
/// above the miniature and billboards it to the HMD. Size is FIXED in board space
/// by default ([WorldUI] BarFixedSize, test #14 item 4); the legacy distance-growth
/// clamp is opt-in.
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

        /// <summary>True once the one-shot per-bar log line fired.</summary>
        public bool DepthLogged;
    }

    /// <summary>Rescan cadence for late-spawned bar graphics (HealthBar mark pooling).</summary>
    private const float DepthScanIntervalSeconds = 2f;

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
        s_barsOccluded = s_barsConfigFile.Bind("WorldUI", "BarsOccluded", true,
            "Actor HP/effect bars depth-test against the world: walls occlude them like "
            + "any world object instead of the bar shining through. Look-preserving — "
            + "bars stay enabled and billboarding, they are simply hidden pixel-by-pixel "
            + "where a wall is in front. Disable to get the vanilla draw-on-top bars.");
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

        foreach (KeyValuePair<WorldspacePanelUIController, Adopted> pair in Adoptions)
        {
            WorldspacePanelUIController controller = pair.Key;
            Adopted adopted = pair.Value;
            ConvertedPanel panel = adopted.Panel;
            if (controller == null || panel.HostGo == null)
                continue;

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
            }

            // Anchor in BOARD units (P6 fix #4): the cached bounds-derived offset
            // scales with the diorama by construction — zooming the table keeps the
            // bar exactly above the miniature instead of inside it.
            bool haveTrack = TryGetTrackPoint(controller, out Vector3 track);
            Vector3 pos = track + Vector3.up * pair.Value.AnchorOffsetWU;

            // Wall occlusion ([WorldUI] BarsOccluded gate): the bar's graphics run per-instance
            // materials with unity_GUIZTestMode=LEqual so wall depth occludes them naturally —
            // the bar itself stays enabled and billboarding (no toggling; the old linecast+hide
            // probe is retired). Slow rescan catches graphics pooled after adopt (health marks).
            if (BarsOccluded)
            {
                float now = Time.unscaledTime;
                if (now >= adopted.NextDepthScan)
                {
                    adopted.NextDepthScan = now + DepthScanIntervalSeconds;
                    ApplyBarDepthTest(adopted, controller.name);
                }
            }
            else if (adopted.DepthMats.Count > 0)
            {
                // Live config-off: give every graphic its original material back.
                RestoreBarDepthTest(adopted);
            }

            if (panel.HostGo.activeSelf == hide)
                panel.HostGo.SetActive(!hide);
            if (hide)
                continue;

            if (!haveTrack)
                continue;

            // Billboard: uGUI front faces -forward → +Z away from the viewer.
            Vector3 fromHead = pos - headPos;
            if (fromHead.sqrMagnitude < 1e-6f)
                continue;
            Quaternion rot = Quaternion.LookRotation(fromHead.normalized, Vector3.up);

            // Bar size (test #14 item 4): FIXED board-space size by default
            // ([WorldUI] BarFixedSize) — the bar scales only with the diorama, like
            // the miniature it belongs to. The old distance compensation (growing
            // up to 2.5x with head distance) made bars visibly GROW when the player
            // stepped away and is now the opt-in legacy path.
            float grow = 1f;
            if (!WorldUIConfig.BarFixedSize.Value)
            {
                // Legacy: distance in HMD-relative REAL meters (world ÷ diorama scale).
                float realDistance = fromHead.magnitude / worldScale;
                grow = Mathf.Clamp(realDistance / 0.6f, 1f, 2.5f);
            }

            Transform t = panel.HostGo.transform;
            t.SetPositionAndRotation(pos, rot);
            t.localScale = Vector3.one * (metersPerPixel * worldScale * 0.35f * grow);
        }
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
            Bounds b = RendererScratch[i].bounds;
            if (b.max.y > maxY) maxY = b.max.y;
            if (b.min.y < minY) minY = b.min.y;
        }
        RendererScratch.Clear();

        float height = maxY - minY;
        if (height <= 0.01f)
            return fallback; // degenerate bounds (still spawning) — vanilla height

        // Clear the top of the mini by ~12% of its own height, everything in board units.
        float offset = (maxY - track.y) + 0.12f * height;
        return Mathf.Clamp(offset, 0.05f, fallback + height);
    }

    private static void Adopt(WorldspacePanelUIController controller)
    {
        ConvertedPanel? panel = CanvasConversion.Convert(
            controller.transform as RectTransform, "ActorBar", pokeable: false);
        if (panel == null)
            return;

        Adoptions[controller] = new Adopted
        {
            Controller = controller,
            Panel = panel,
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
    /// Force every Graphic under the adopted bar host to DEPTH-TEST against walls: assign a
    /// per-instance copy of its material with <c>unity_GUIZTestMode</c> = LEqual(4). Unity's
    /// UI/Default shader declares <c>ZTest [unity_GUIZTestMode]</c> and the per-material value
    /// beats the global, so world-space bar pixels are occluded by wall depth while the bar stays
    /// enabled and billboarding — no toggling. TMP distance-field text: its SDF shaders use the
    /// same <c>unity_GUIZTestMode</c> bracket in UI mode; some variants expose <c>_ZTestMode</c>
    /// instead — both are set (unconditionally for the former, since it is a bracket lookup and
    /// not a declared Property, HasProperty-guarded for the latter). Idempotent per graphic
    /// (instance-ID set); originals snapshotted for restore. Only graphics under hosts ActorBars
    /// owns/adopts are ever touched.
    /// </summary>
    private static void ApplyBarDepthTest(Adopted adopted, string barName)
    {
        GameObject host = adopted.Panel.HostGo;
        if (host == null)
            return;

        GraphicScratch.Clear();
        host.GetComponentsInChildren(includeInactive: true, GraphicScratch);
        int added = 0;
        for (int i = 0; i < GraphicScratch.Count; i++)
        {
            Graphic g = GraphicScratch[i];
            if (g == null)
                continue;
            int id = g.GetInstanceID();
            if (adopted.DepthMatIds.Contains(id))
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
    }

    private static void Release(WorldspacePanelUIController controller)
    {
        // NOTE: the key may be Unity-dead ("== null" true) but the CLR reference is
        // still a valid dictionary key — always use it for the map ops.
        if (Adoptions.TryGetValue(controller, out Adopted adopted))
        {
            RestoreBarDepthTest(adopted);
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
            CanvasConversion.Release(pair.Value.Panel);
        }
        Adoptions.Clear();
        Owned.Clear();
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
