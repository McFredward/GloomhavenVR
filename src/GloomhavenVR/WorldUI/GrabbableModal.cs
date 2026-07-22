using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Makes a floated modal window (<see cref="ModalFallback"/>) a GRABBABLE + SCALABLE
/// world element — exactly like the control board / combat log — by reusing the SHARED
/// grab core (<see cref="PanelGrabHandle"/> + <see cref="IPanelGrabOwner"/>): one hand
/// grips the brass bar under the panel to MOVE it, two hands RESIZE it (0.5x-2x). No new
/// grab mechanism is invented; this only owns a small mod-owned holder/frame the same way
/// <see cref="Surfaces.CombatLogSurface"/> does, and lets the game-owned world-space host
/// FOLLOW that frame each tick.
///
/// TRANSFORM LAYOUT (mirrors CombatLogSurface): holder (identity pose, localScale =
/// diorama WorldScale) → frame (grab ROOT at the PANEL CENTER; localScale = user size
/// factor 0.5-2) → bar visual (a child just under the panel's bottom edge). The
/// grab-zone collider lives on the frame with its centre offset down to the bar, so the
/// grip lands on the visible handle while the frame origin stays at the panel centre.
///
/// HOST FOLLOW: the game-owned host is never re-parented (mount-seam reversibility rule);
/// each <see cref="Tick"/> its world pose is copied from the frame and its scale is
/// metersPerPixel × WorldScale × <c>extraScale</c> × factor — the SAME convention
/// <see cref="ModalFallback"/> places it with, plus the live user grab factor. When the
/// user is not gripping, the frame is static, so the host is static too (no drift).
///
/// INPUT: poke/laser clicks on the menu widgets are unaffected — they drive the real
/// uGUI through the host's raycaster (UguiPokeSurfaces / RayUguiDriver), a different path
/// than the grip-grab, and the bar sits BELOW the content so it never overlaps a widget.
/// The world-grab yields any grip that starts on a highlighted/held grabbable, so gripping
/// the bar moves the menu instead of the diorama (PanelGrabHandle's documented arbitration).
/// </summary>
internal sealed class GrabbableModal : IPanelGrabOwner
{
    /// <summary>Gap below the panel's bottom edge to the bar centre (frame-local, scale-1 metres).</summary>
    private const float BarGapMeters = 0.03f;
    private const float BarThickness = 0.024f;
    private const float BarWidthFraction = 0.55f;
    private const float ZoneWidthFraction = 0.62f;
    private const float MinBarWidth = 0.04f;

    /// <summary>
    /// LOST-MENU FIX: cross-section pad of the LASER-only bar collider, in bar-local units
    /// (the bar cube is unit-sized, scaled to barWidth × BarThickness × BarThickness — so
    /// 1.5 ≈ a 3.6 cm strip). Just enough slack to point at the 2.4 cm visible bar
    /// comfortably, WITHOUT re-growing the swallow-everything zone the incident showed:
    /// the palm ZONE collider (5 cm, 62% width) had been the laser target too, and since
    /// the floated menu sits between the user and the board, every trigger aimed at the
    /// cards hit it and dragged the (possibly off-view) menu instead.
    /// </summary>
    private const float BarColliderPad = 1.5f;

    /// <summary>
    /// Item 3: the brass grab bar must OCCLUDE the menu content behind it (foreground is
    /// foreground). The floated menu host is a WORLD-space canvas at
    /// <c>ModalFallback.ModalHostSortingOrder = 1000</c>, and Unity sorts EVERY renderer by
    /// sortingLayer → SORTINGORDER first (only then by renderQueue / distance — see
    /// <c>RayInteractor.RayVisualSortingOrder = 5000</c>, which is exactly how the laser dot
    /// draws over the same order-1000 modal). Giving the bar's MeshRenderer a sortingOrder
    /// ABOVE the menu makes it composite unambiguously ON TOP of the canvas; an opaque
    /// (ZWrite-on) brass material then makes it read solid rather than semi-transparent. Kept
    /// below the ray visuals (5000) so the pointer dot still lands on top of the bar.
    /// </summary>
    private const int BarSortingOrder = 1100;

    /// <summary>
    /// Problem #4 (HUD bleed-through) — DEPTH MASK render queue. The floated menu host is a
    /// WORLD-space canvas that writes NO depth on purpose (ZWrite OFF, so hands/board still occlude
    /// the menu — a hard requirement), so transparent HUD (the initiative track, button-cluster
    /// labels) sitting BEHIND the menu is never depth-occluded and bleeds through. The mask is a
    /// color-invisible depth-WRITING per-graphic quad mesh coplanar with the menu (task #5); it must render AFTER all opaque
    /// geometry (so nearer hands/board depth is already in the buffer and their LEqual wins) and
    /// BEFORE the menu's own transparent UI (~queue 3000, so the menu draws on top and its LEqual
    /// passes at the stamped plane, while HUD-behind fails). Geometry-Last+... i.e. one below the
    /// Transparent boundary (2999): strictly &gt; 2500 (transparent → after every opaque draw) and
    /// strictly &lt; 3000 (before the UI). Mirrors <see cref="Core.SkyBackdrop"/>'s reset-queue idea,
    /// localized to the menu plane. The mask's MeshRenderer keeps the DEFAULT sortingOrder 0 (below
    /// the host canvas's 1000), so on EITHER transparent-sort axis — renderQueue or sortingOrder —
    /// it composites before the menu content.
    /// </summary>
    private static readonly int DepthMaskQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 1; // 2999

    /// <summary>
    /// Problem #4: coplanar offset (real metres at diorama scale 1) placing the depth mask a HAIR
    /// BEHIND the menu content plane — +Z is AWAY from the viewer (the canvas front faces −Z toward
    /// the player; see <c>ModalFallback.ComputeHmdPose</c>), i.e. toward the far plane. Just enough
    /// that the mask never z-fights the menu's own graphics, far smaller than any HUD gap behind it.
    /// </summary>
    private const float DepthMaskBehindMeters = 0.002f;

    /// <summary>
    /// Task #5: padding (host px) around EACH per-graphic quad of the depth-mask mesh — just
    /// enough to bridge antialiased edges without re-closing the gaps between settings rows
    /// (the old single-union mask used 12 px around the whole union; per-graphic quads must
    /// stay tight or adjacent-row pads merge and the gap is masked again).
    /// </summary>
    private const float DepthMaskQuadPaddingPx = 3f;

    /// <summary>
    /// Task #5: cap on per-graphic depth-mask quads. The biggest options submenu measures
    /// ~100 visible graphics; past the cap the remaining graphics merge into the LAST quad's
    /// union (coverage kept, gap fidelity degrades) — see
    /// <see cref="CanvasConversion.CollectVisibleMaskRects"/>.
    /// </summary>
    private const int DepthMaskMaxQuads = 256;

    private ConvertedPanel _panel = null!;
    private float _extraScale = 1f;             // ModalFallback.WindowScaleFactor (host shrink)

    /// <summary>
    /// Item 2: the diorama scale CAPTURED ONCE at spawn. The menu SIZE is derived from this
    /// fixed reference instead of the live <see cref="PanelLayout.WorldScale"/>, so zooming the
    /// diorama after the menu opens no longer grows/shrinks it (position stays a fixed world
    /// point regardless). Only the user's two-hand grab factor still resizes it on top.
    /// </summary>
    private float _spawnWorldScale = 1f;
    private string _logName = "Menu";

    /// <summary>DIAG-throttle (spam fix): seconds between MODAL DIAG snapshot lines while the host moves.</summary>
    private const float DiagThrottleSeconds = 1f;

    // DIAG-throttle state: last host pose (movement detection) + per-panel next-allowed stamp.
    private Vector3 _diagLastPos;
    private Quaternion _diagLastRot = Quaternion.identity;
    private float _diagNextAllowed;

    private Transform? _holder;                 // identity pose, localScale = diorama WorldScale
    private Transform? _frame;                  // grab root at the panel centre; localScale = user factor
    private Transform? _bar;
    private Transform? _depthMask;              // problem #4: coplanar depth-writing mesh (menu family only)
    private Mesh? _depthMaskMesh;               // task #5: one quad per visible graphic (rebuilt on change)
    private int _depthMaskHash;                 // task #5: quantized hash of the emitted rects (rebuild gate)
    private bool _wantDepthMask;               // set by Build for the pause/options/confirmation family

    // Task #5 scratch (mask mesh rebuild only; static — ticks run sequentially on the main thread).
    private static readonly List<Vector4> MaskRectScratch = new(DepthMaskMaxQuads);
    private static readonly List<Vector3> MaskVertScratch = new(DepthMaskMaxQuads * 4);
    private static readonly List<int> MaskTriScratch = new(DepthMaskMaxQuads * 6);

    /// <summary>Task #6 diag: the emitting Graphic per collected rect (1:1 with
    /// <see cref="MaskRectScratch"/>; null = overflow union slot) — lets the rebuild
    /// diagnostic NAME the wide quads in the hardware log.</summary>
    private static readonly List<Graphic?> MaskSourceScratch = new(DepthMaskMaxQuads);

    /// <summary>Task #6 diag: min seconds between rebuild-diagnostic lines per panel (scrolling
    /// rebuilds the mesh per notch — the log must not scroll with it).</summary>
    private const float MaskDiagMinIntervalSeconds = 2f;

    /// <summary>Task #6 diag: a quad is "WIDE" (a hard-cut suspect worth naming) when it spans at
    /// least this fraction of the host rect width — settings rows sit well under it, a faint
    /// full-width container / banner strip well over it.</summary>
    private const float MaskDiagWideFraction = 0.45f;

    /// <summary>Task #6 diag: cap on named wide quads per line (log hygiene).</summary>
    private const int MaskDiagMaxListed = 12;

    /// <summary>Task #6 diag: next allowed rebuild-diagnostic stamp (unscaled time).</summary>
    private float _maskDiagNextAllowed;
    private BoxCollider? _grabZone;
    private PanelGrabHandle? _handle;

    /// <summary>True while a hand grips the bar (owner skips no writes — the host just follows).</summary>
    internal bool IsGrabbed => _handle != null && _handle.IsGrabbed;

    /// <summary>
    /// Item 1 (pause-menu size): re-seat the board-relative host shrink AFTER a full-screen menu's
    /// one-shot content fit shrank the host rect. The fit runs a few frames after Build, so the
    /// extraScale first derived from the pre-fit (full 1920) rect would leave the fitted panel
    /// mis-sized; the owner recomputes it from the fitted width and pushes it here. The next
    /// <see cref="Tick"/> applies it (host localScale = mpp × worldScale × extraScale × factor).
    /// </summary>
    internal void SetExtraScale(float extraScale) => _extraScale = extraScale;

    /// <summary>
    /// Build the grab affordance for a freshly floated, freshly placed modal host. The
    /// frame is seeded at the host's CURRENT world pose (the host was just placed at the
    /// HMD), so the first follow tick keeps the panel exactly where it spawned — no jump.
    /// </summary>
    /// <param name="depthMask">Problem #4: create the coplanar depth mask (pause/options/confirmation
    /// family only) so transparent HUD behind the floated menu is depth-occluded by it.</param>
    internal void Build(ConvertedPanel panel, float extraScale, string logName, bool depthMask = false)
    {
        _panel = panel;
        _extraScale = extraScale;
        _logName = logName;
        _wantDepthMask = depthMask;
        // Item 2: snapshot the diorama scale now — the menu keeps THIS size regardless of later zoom.
        _spawnWorldScale = Mathf.Max(PanelLayout.WorldScale, 0.01f);
        EnsureFrame();
        if (_frame != null && panel.HostGo != null)
        {
            Transform h = panel.HostGo.transform;
            _frame.SetPositionAndRotation(h.position, h.rotation);
            _frame.localScale = Vector3.one; // user factor 1x
        }
        Tick(); // place host from the frame + size the bar immediately
    }

    /// <summary>
    /// Re-seat the frame (and thus the whole panel) at a fresh pose — used on presence
    /// regain (RefloatOpenWindows), where the user may have physically moved while the HMD
    /// was off and a menu stranded out of view would be un-dismissable.
    /// </summary>
    internal void PlaceFrameAt(Vector3 position, Quaternion rotation)
    {
        EnsureFrame();
        if (_frame == null)
            return;
        _frame.SetPositionAndRotation(position, rotation);
        Tick();
    }

    // ---- IPanelGrabOwner --------------------------------------------------------------------

    Transform? IPanelGrabOwner.GrabRoot => _frame;

    bool IPanelGrabOwner.GrabVisible =>
        _panel != null && _panel.IsAlive && _holder != null && _holder.gameObject.activeInHierarchy;

    // Carry the yaw with the hand like the tray/combat log — nothing else authors the
    // modal's rotation, so there is no two-writer jitter.
    bool IPanelGrabOwner.GrabCarriesYaw => true;

    // Free placement: the menu stays wherever the user left it while open; a re-open
    // re-floats it at the HMD (ModalFallback), so there is nothing to persist here.
    void IPanelGrabOwner.OnGrabFinished() { }

    // ---- per-frame follow -------------------------------------------------------------------

    /// <summary>The game-owned host follows the mod-owned grab frame (position, rotation, scale).</summary>
    internal void Tick()
    {
        if (_panel == null || !_panel.IsAlive || _panel.HostGo == null || _panel.HostRect == null)
            return;
        EnsureFrame();
        if (_holder == null || _frame == null)
            return;

        // Item 2 (no auto-scale with world zoom): the menu SIZE uses the diorama scale CAPTURED
        // ONCE at spawn (_spawnWorldScale), NOT the live PanelLayout.WorldScale — so zooming the
        // diorama after the menu opens no longer grows/shrinks it. POSITION is the frame's world
        // point (copied below), independent of worldScale.
        //
        // CRITICAL (deadlock fix): the holder MUST stay at identity scale. It used to be scaled by
        // worldScale, which tied the frame's WORLD position to worldScale — frame.position =
        // holder.scale(worldScale) × frame.localPosition. worldScale settles/animates at scenario
        // start, so the modal's position (and size) collapsed toward the origin in lock-step and the
        // start dialog flew away, undismissable. With the holder at identity, frame.position is a
        // true world point — grab-stable and immune to worldScale drift.
        float worldScale = _spawnWorldScale;
        _holder.localScale = Vector3.one;
        if (!_holder.gameObject.activeSelf)
            _holder.gameObject.SetActive(true);

        // Item 4: the user grab factor rides the SAME [MinScale, MaxScale] range the shared handle
        // clamps to — a higher local floor here would silently re-cap what the two-hand pinch shrank.
        float factor = Mathf.Clamp(_frame.localScale.x, PanelGrabHandle.MinScale, PanelGrabHandle.MaxScale);
        float metersPerPixel = WorldUIConfig.CanvasScaleMm.Value * 0.001f;

        Transform host = _panel.HostGo.transform;
        SyncHostToFrame(host, factor, metersPerPixel, worldScale);

        // DIAG SPAM FIX: while the host is being carried/moved, its position changes every
        // frame, so CanvasConversion's change-gated MODAL DIAG snapshot (host pos rounded to
        // cm) emitted one line PER FRAME for the whole drag (hundreds of lines in the incident
        // log). Throttle it to ~1 line/s per panel by gating the panel's Diagnostic opt-in.
        ThrottleDiagWhileMoving(host);

        // Bar/zone track the live host rect. The holder is now identity, so these frame-local
        // metres must carry worldScale themselves to reach the host's world size (the frame's
        // own localScale contributes the user grab factor). unit = world metres per host pixel.
        Rect rect = _panel.HostRect.rect;
        float unit = metersPerPixel * _extraScale * worldScale;
        float halfHeight = rect.height * unit * 0.5f;
        float width = rect.width * unit;
        SyncBar(halfHeight, width, worldScale);
        SyncDepthMask(unit, worldScale);
    }

    /// <summary>
    /// Copy the frame pose/scale onto the game-owned host (shared by the Update-time
    /// <see cref="Tick"/> and the LateUpdate re-sync in <see cref="HostLateSync"/>).
    /// </summary>
    private void SyncHostToFrame(Transform host, float factor, float metersPerPixel, float worldScale)
    {
        host.SetPositionAndRotation(_frame!.position, _frame.rotation);
        host.localScale = Vector3.one * (metersPerPixel * worldScale * _extraScale * factor);
    }

    /// <summary>
    /// DRAG-FLICKER FIX (tabs blink / submenu pane vanishes ONLY while moving the window):
    /// re-sync the host from the frame in LateUpdate, AFTER every Update-time frame writer ran.
    ///
    /// Root cause: <see cref="Tick"/> (the frame→host copy) runs from
    /// <c>ModalFallback.Tick</c> inside the WorldUI module's <c>Update</c>, while
    /// <see cref="PanelGrabHandle"/> moves the FRAME from its OWN MonoBehaviour
    /// <c>Update</c> — the relative script order is undefined. Whenever the handle's Update
    /// runs after the module's, the frame (and every rigid CHILD of it: the DEPTH MASK, the
    /// bar) renders at the NEW pose while the host — synced earlier from the STALE pose —
    /// renders one frame behind. A fast drag moves the frame 5–30 cm per frame (hardware
    /// log), dwarfing the mask's 2 mm behind-plane offset: dragging toward the viewer puts
    /// the mask plane IN FRONT of the (lagging) menu content, the menu fails its own
    /// ZTest-LEqual under every mask quad, and the content blinks out — exactly the
    /// "tabs flicker / pane briefly vanishes while moving" report. Re-copying the pose here
    /// in LateUpdate (after ALL Updates, before rendering) makes host, mask and bar agree
    /// at render time every frame, regardless of script order; a static (ungrabbed) frame
    /// makes it a change-free no-op write.
    /// </summary>
    internal void LateSyncHost()
    {
        if (_panel == null || !_panel.IsAlive || _panel.HostGo == null || _frame == null)
            return;
        float factor = Mathf.Clamp(_frame.localScale.x, PanelGrabHandle.MinScale, PanelGrabHandle.MaxScale);
        float metersPerPixel = WorldUIConfig.CanvasScaleMm.Value * 0.001f;
        SyncHostToFrame(_panel.HostGo.transform, factor, metersPerPixel, _spawnWorldScale);
    }

    /// <summary>
    /// Mod-owned holder component whose ONLY job is the LateUpdate host re-sync (see
    /// <see cref="LateSyncHost"/>). Lives on the holder GameObject, so it is destroyed with
    /// it in <see cref="GrabbableModal.Destroy"/> — no explicit lifecycle management.
    /// </summary>
    private sealed class HostLateSync : MonoBehaviour
    {
        internal GrabbableModal? Owner;

        private void LateUpdate() => Owner?.LateSyncHost();
    }

    /// <summary>
    /// Problem #4 + task #5: keep the depth mask coplanar with the (grabbable, resizable,
    /// content-fittable) menu — as a PER-GRAPHIC quad mesh, one quad per visible graphic's
    /// host-local rect (<see cref="CanvasConversion.CollectVisibleMaskRects"/>), NOT one union
    /// rect. The old union stamped menu-plane depth across the GAPS between settings rows: the
    /// WORLD still showed through them (drawn before the mask, colour already in the buffer)
    /// but OTHER TRANSPARENT MENUS behind did not (drawn after, depth-tested against the stamp).
    /// With per-graphic quads, depth is stamped only where content approximately renders and
    /// every gap — between rows, beside the rail, around the dialog — stays open for menus
    /// behind too. Rect-level approximation: a graphic's transparent padding INSIDE its own
    /// rect still masks (per-pixel would need alpha-aware shaders — out of scope).
    ///
    /// Mesh economy: the (cheap, depth-only, hugely overdraw-tolerant) quads live in HOST-LOCAL
    /// px in the mesh; the transform's localScale carries px→frame-metres (<paramref name="unit"/>)
    /// and the frame's own localScale the user grab factor, so per tick only the transform is
    /// written. The mesh itself rebuilds ONLY when the quantized rect set changes (hash gate) —
    /// scrolling/toggling rebuilds, a static menu costs just the per-tick Graphic walk (~100
    /// graphics on the biggest menu, only for the masked pause/options family). No visible
    /// content → mask disabled entirely.
    /// </summary>
    private void SyncDepthMask(float unit, float worldScale)
    {
        if (_depthMask == null || _depthMaskMesh == null)
            return;
        int count = CanvasConversion.CollectVisibleMaskRects(_panel, MaskRectScratch, DepthMaskMaxQuads,
            MaskSourceScratch);
        if (count == 0)
        {
            // Nothing visible (window still fading in / everything hidden) — no depth stamp at all.
            if (_depthMask.gameObject.activeSelf)
                _depthMask.gameObject.SetActive(false);
            return;
        }

        // Rebuild gate: hash the rect set quantized to whole host pixels — sub-pixel jitter
        // never rebuilds, any real scroll/expand/collapse does.
        int hash = 17;
        for (int i = 0; i < count; i++)
        {
            Vector4 r = MaskRectScratch[i];
            hash = hash * 31 + Mathf.RoundToInt(r.x);
            hash = hash * 31 + Mathf.RoundToInt(r.y);
            hash = hash * 31 + Mathf.RoundToInt(r.z);
            hash = hash * 31 + Mathf.RoundToInt(r.w);
        }
        if (hash != _depthMaskHash)
        {
            _depthMaskHash = hash;
            RebuildDepthMaskMesh(count);
            LogDepthMaskRebuild(count); // task #6: name the wide quads (hard-cut suspects)
        }

        if (!_depthMask.gameObject.activeSelf)
            _depthMask.gameObject.SetActive(true);
        _depthMask.localScale = new Vector3(unit, unit, 1f);
        _depthMask.localPosition = new Vector3(0f, 0f, DepthMaskBehindMeters * worldScale);
    }

    /// <summary>
    /// Task #5: emit one (padded) quad per collected rect into the depth-mask mesh. Vertices are
    /// HOST-LOCAL px around the host centre (pivot 0.5,0.5 — the frame origin), z = 0 (the
    /// transform carries the coplanar offset). Winding is irrelevant: the mask material renders
    /// two-sided (<c>_Cull=0</c>). ≤256 quads / ≤1024 verts — a trivially small dynamic mesh.
    /// </summary>
    private void RebuildDepthMaskMesh(int count)
    {
        MaskVertScratch.Clear();
        MaskTriScratch.Clear();
        for (int i = 0; i < count; i++)
        {
            Vector4 r = MaskRectScratch[i];
            float x0 = r.x - DepthMaskQuadPaddingPx;
            float y0 = r.y - DepthMaskQuadPaddingPx;
            float x1 = r.z + DepthMaskQuadPaddingPx;
            float y1 = r.w + DepthMaskQuadPaddingPx;
            int b = MaskVertScratch.Count;
            MaskVertScratch.Add(new Vector3(x0, y0, 0f));
            MaskVertScratch.Add(new Vector3(x1, y0, 0f));
            MaskVertScratch.Add(new Vector3(x1, y1, 0f));
            MaskVertScratch.Add(new Vector3(x0, y1, 0f));
            MaskTriScratch.Add(b);
            MaskTriScratch.Add(b + 1);
            MaskTriScratch.Add(b + 2);
            MaskTriScratch.Add(b);
            MaskTriScratch.Add(b + 2);
            MaskTriScratch.Add(b + 3);
        }
        _depthMaskMesh!.Clear();
        _depthMaskMesh.SetVertices(MaskVertScratch);
        _depthMaskMesh.SetTriangles(MaskTriScratch, 0);
    }

    /// <summary>
    /// Task #6 diagnostic (pause window hard-cut behind the options menu): after every mask
    /// mesh REBUILD, log the quad count and NAME every WIDE quad — a quad spanning ≥45 % of
    /// the host width is exactly the class of depth writer that visually reads as "nothing
    /// there" yet hard-cuts another floated menu behind the plane along one straight edge
    /// (faint full-width layout container, gradient title-banner strip, frame element). Each
    /// entry carries the graphic's parent/name, type, sprite, own alpha × inherited alpha and
    /// its host-local rect, so the next hardware log names the culprit directly. Throttled to
    /// one line per <see cref="MaskDiagMinIntervalSeconds"/> per panel (a scroll rebuilds the
    /// mesh every notch); the very first rebuild after Build always logs (throttle starts at 0).
    /// </summary>
    private void LogDepthMaskRebuild(int count)
    {
        float now = Time.unscaledTime;
        if (now < _maskDiagNextAllowed)
            return;
        _maskDiagNextAllowed = now + MaskDiagMinIntervalSeconds;

        Rect host = _panel.HostRect != null ? _panel.HostRect.rect : default;
        float wideMin = host.width * MaskDiagWideFraction;
        var sb = new System.Text.StringBuilder(128);
        int wide = 0;
        for (int i = 0; i < count; i++)
        {
            Vector4 r = MaskRectScratch[i];
            float w = r.z - r.x;
            if (w < wideMin)
                continue;
            wide++;
            if (wide > MaskDiagMaxListed)
                continue; // counted but not listed (log hygiene)
            Graphic? g = i < MaskSourceScratch.Count ? MaskSourceScratch[i] : null;
            sb.Append(" [").Append(i).Append("] ")
              .Append(g == null ? "<overflow union>" : DescribeMaskSource(g))
              .Append($" rect=({r.x:F0},{r.y:F0})..({r.z:F0},{r.w:F0}) {w:F0}x{r.w - r.y:F0}px;");
        }
        // Task #6b: name the invisible emitters the collection EXCLUDED this pass (and the rule
        // that caught each — 'Main Area/Viewport' should appear here, not in the quads above).
        string excluded = CanvasConversion.LastMaskExclusions.Count > 0
            ? $" EXCLUDED non-rendering emitter(s): {string.Join("; ", CanvasConversion.LastMaskExclusions)}."
            : "";
        VRLog.Info("WorldUI", $"MODAL DEPTH-MASK DIAG: '{_logName}' rebuilt {count} quad(s), host " +
                              $"{host.width:F0}x{host.height:F0} px, mask alpha floor " +
                              $"{CanvasConversion.MaskMinAlpha:F2}. WIDE quads (≥{MaskDiagWideFraction * 100f:F0}% " +
                              $"host width, hard-cut suspects): {wide}" +
                              (wide > 0
                                  ? $" —{sb}"
                                  : " — none (a remaining straight cut through another menu would NOT be a mask " +
                                    "quad of this panel: check the grab bar / plane order instead).") +
                              excluded);
    }

    /// <summary>Task #6 diag: one wide-quad source — 'parent/name' (Type, sprite, ownAlpha×inheritedAlpha).</summary>
    private static string DescribeMaskSource(Graphic g)
    {
        string sprite = g is Image img && img.sprite != null ? $", sprite='{img.sprite.name}'" : "";
        string parent = g.transform.parent != null ? g.transform.parent.name : "<root>";
        float inherited = g.canvasRenderer != null ? g.canvasRenderer.GetInheritedAlpha() : 1f;
        return $"'{parent}/{g.name}' ({g.GetType().Name}{sprite}, a={g.color.a:F2}x{inherited:F2})";
    }

    /// <summary>
    /// DIAG SPAM FIX: gate <c>ConvertedPanel.Diagnostic</c> so the change-gated MODAL DIAG
    /// snapshot fires at most ~1/s per panel WHILE the host pose is actually changing (a
    /// carry/laser-drag/recall); a static host keeps Diagnostic permanently ON, so every
    /// state CHANGE (open/close/adopt, canvas/order flips, the settle line after a drag
    /// ends) still logs immediately and unthrottled. Diagnostic also gates the per-frame
    /// adopted-sorting re-assert in CanvasConversion — while a panel is mid-drag that guard
    /// runs at the throttle cadence instead, which the 30-frame adoption sweep already
    /// backstops (pre-guard behavior, only ever during active movement of THIS panel).
    /// </summary>
    private void ThrottleDiagWhileMoving(Transform host)
    {
        // Movement epsilon: 5 mm at diorama scale — below the snapshot's own cm rounding,
        // so anything smaller never spammed in the first place. Rotation guards a pure spin.
        float eps = 0.005f * _spawnWorldScale;
        bool moving = (host.position - _diagLastPos).sqrMagnitude > eps * eps
                      || Quaternion.Angle(host.rotation, _diagLastRot) > 0.5f;
        _diagLastPos = host.position;
        _diagLastRot = host.rotation;

        if (!moving)
        {
            _panel.Diagnostic = true; // static host: change-gated DIAG stays fully live
            return;
        }
        float now = Time.unscaledTime;
        if (now >= _diagNextAllowed)
        {
            _diagNextAllowed = now + DiagThrottleSeconds;
            _panel.Diagnostic = true; // one snapshot line for this second of movement
        }
        else
        {
            _panel.Diagnostic = false; // swallow the per-frame pos-churn lines
        }
    }

    // ---- build ------------------------------------------------------------------------------

    private void EnsureFrame()
    {
        if (_holder != null && _frame != null)
            return;

        var holderGo = new GameObject($"GloomhavenVR.ModalGrab_{_logName}");
        _holder = holderGo.transform;
        // DRAG-FLICKER FIX: LateUpdate re-sync of the host from the frame — see LateSyncHost.
        holderGo.AddComponent<HostLateSync>().Owner = this;

        var frameGo = new GameObject("Frame");
        _frame = frameGo.transform;
        _frame.SetParent(_holder, worldPositionStays: false);

        var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = "Bar";
        // LOST-MENU FIX: keep the primitive's BoxCollider as the LASER-only drag-bar target
        // instead of destroying it. The unit box scaled by the bar transform matches the
        // VISIBLE brass strip exactly (padded slightly via BarColliderPad); handed to the
        // shared handle as BarCollider so RayGrabDriver ray-tests ONLY this strip. It is a
        // trigger on the mod render layer, so the physics ray (RayInteractor) still ignores
        // it, and it is NOT registered with VRInteractables — the palm grab keeps using the
        // generous frame zone below (near-grab is deliberate; the laser was the problem).
        var barCollider = bar.GetComponent<BoxCollider>();
        barCollider.isTrigger = true;
        barCollider.size = new Vector3(1f, BarColliderPad, BarColliderPad);
        bar.transform.SetParent(_frame, worldPositionStays: false);
        bar.transform.localScale = new Vector3(0.2f, BarThickness, BarThickness);
        var mr = bar.GetComponent<MeshRenderer>();
        // Item 3: opaque brass that OCCLUDES the menu. The bundled GloomhavenVR/Overlay shader
        // (overlay:true) exposes _ZWrite/_ZTest; force ZWrite ON so the bar draws solid (not the
        // Sprites/Default alpha-blend that read semi-transparent), while leaving ZTest at the
        // default LEqual so a hand held physically in front still occludes the solid handle. The
        // sortingOrder below is what actually lifts it OVER the depthless menu canvas.
        Material barMat = WorldUIAssets.CreateFlatMaterial(new Color(0.62f, 0.5f, 0.28f), overlay: true);
        if (barMat.HasProperty("_ZWrite"))
            barMat.SetInt("_ZWrite", 1);
        mr.sharedMaterial = barMat;
        mr.sortingOrder = BarSortingOrder; // composite ON TOP of the order-1000 menu host canvas
        _bar = bar.transform;

        // Grab zone + shared grab core (collider BEFORE the handle: its OnEnable registers it).
        _grabZone = frameGo.AddComponent<BoxCollider>();
        _grabZone.isTrigger = true;
        _grabZone.size = new Vector3(0.25f, 0.05f, 0.05f);
        _handle = frameGo.AddComponent<PanelGrabHandle>();
        _handle.Init(this, mr, "WorldUI", $"{_logName} menu");
        // LOST-MENU FIX: split laser vs palm — the far ray grabs ONLY the visible bar strip.
        _handle.SetBarCollider(barCollider);

        // Problem #4: the coplanar depth mask (menu family only). Built BEFORE VRLayers.Apply so it
        // is swept onto the mod layer 27 with the rest of the holder — only the HMD head camera draws
        // it, never the game's mono UI Camera.
        if (_wantDepthMask && _depthMask == null)
            BuildDepthMask();

        // Render-only mod layer — grabs/pokes route through the registries, not layers.
        VRLayers.Apply(holderGo);
        VRLog.Info("WorldUI", $"MODAL GRAB: '{_logName}' is now a grabbable/scalable world element " +
                              "(grip the bar to move, two hands to resize 0.5x-2x).");
    }

    /// <summary>
    /// Problem #4: build the color-invisible depth-writing MESH that stamps the floated menu's depth
    /// into the buffer, exactly the <see cref="Core.SkyBackdrop"/> depth-reset material state but
    /// LEqual (not Always) and localized to the menu plane. Task #5: the geometry is a dynamic
    /// per-graphic quad mesh (one quad per visible graphic, rebuilt on change by
    /// <see cref="SyncDepthMask"/>) instead of a single union quad, so the gaps BETWEEN content
    /// stay depth-open for other transparent menus behind.
    ///
    /// MATERIAL: the bundled <c>GloomhavenVR/Overlay</c> shader (the only one exposing the render
    /// state as properties) forced to <c>_ZWrite=1</c> (WRITE depth), <c>_ZTest=4</c> (LEqual — closer
    /// hands/board still win), <c>_Cull=0</c> (two-sided), <c>_SrcBlend=0 (Zero)/_DstBlend=1 (One)</c>
    /// so <c>colour = 0*src + 1*dst = dst</c> — the framebuffer colour is UNCHANGED (nothing hidden,
    /// no tint), only depth is written. renderQueue <see cref="DepthMaskQueue"/> (2999) draws it after
    /// every opaque object and before the menu's transparent UI (~3000).
    ///
    /// ORDERING (why it works): opaque hands/board (queue ≤2500) draw first, laying down their near
    /// depth. The mask (2999, transparent) draws next: where a hand/board is NEARER than the menu
    /// plane its LEqual FAILS (mask depth &gt; the nearer depth) so that near depth is preserved — the
    /// menu stays occludable by closer things; elsewhere it WRITES the menu-plane depth. The menu UI
    /// (3000, ZWrite off, LEqual) draws last and PASSES at its own plane (equal ≤ the mask depth a
    /// hair behind it), so the menu is fully visible. Transparent HUD behind the menu (also ~3000,
    /// but FARTHER than the mask) FAILS LEqual against the stamped depth → correctly occluded. Nothing
    /// is disabled and no colour changes anywhere.
    /// </summary>
    private void BuildDepthMask()
    {
        if (_frame == null)
            return;

        // Task #5: a mod-owned dynamic mesh (per-graphic quads), not a primitive — no collider,
        // never a poke/laser target. Vertices are host-local px; localScale carries px→metres.
        var maskGo = new GameObject("DepthMask");
        maskGo.transform.SetParent(_frame, worldPositionStays: false);
        maskGo.transform.localRotation = Quaternion.identity;
        maskGo.transform.localPosition = new Vector3(0f, 0f, DepthMaskBehindMeters); // real z set per-tick
        maskGo.transform.localScale = new Vector3(1e-4f, 1e-4f, 1f);                 // real scale set per-tick
        _depthMaskMesh = new Mesh { name = "GloomhavenVR.ModalDepthMask" };
        _depthMaskMesh.MarkDynamic(); // rebuilt whenever the visible rect set changes (scroll/toggle)
        _depthMaskHash = int.MinValue; // sentinel: the first Sync always builds the mesh
        var filter = maskGo.AddComponent<MeshFilter>();
        filter.sharedMesh = _depthMaskMesh;

        var mr = maskGo.AddComponent<MeshRenderer>();
        // Colour is irrelevant (Zero/One blend discards src) — clear keeps intent obvious.
        Material mat = WorldUIAssets.CreateFlatMaterial(Color.clear, overlay: true);
        bool depthCapable = mat.HasProperty("_ZWrite") && mat.HasProperty("_ZTest");
        if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 1);   // WRITE depth (stamp the menu plane)
        if (mat.HasProperty("_ZTest")) mat.SetInt("_ZTest", 4);     // LEqual — closer hands/board still occlude
        if (mat.HasProperty("_Cull")) mat.SetInt("_Cull", 0);       // two-sided (menu can be viewed from either face)
        if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", 0); // Zero  ┐ colour = 0*src + 1*dst
        if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", 1); // One   ┘   = dst (UNCHANGED)
        mat.renderQueue = DepthMaskQueue;                            // 2999: after opaque, before the menu UI
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        // Deliberately DO NOT set mr.sortingOrder — default 0 keeps the mask below the host canvas's
        // sortingOrder 1000, so on the sortingOrder axis too it composites BEFORE the menu content.

        _depthMask = maskGo.transform;

        if (depthCapable)
            VRLog.Info("WorldUI", $"MODAL DEPTH-MASK: '{_logName}' created — depth-writing PER-GRAPHIC quad mesh " +
                                  $"(≤{DepthMaskMaxQuads} quads, task #5) on mod layer {Core.VRLayers.ModLayer}, " +
                                  $"renderQueue {DepthMaskQueue}, ZWrite 1 / ZTest LEqual / Cull Off / Blend Zero One, " +
                                  $"coplanar +{DepthMaskBehindMeters * 1000f:F1} mm behind the menu plane, mask alpha " +
                                  $"floor {CanvasConversion.MaskMinAlpha:F2} (task #6: faint full-width containers no " +
                                  "longer stamp depth). Transparent HUD/menus behind fail ZTest only under actual " +
                                  "content rects; the gaps between rows stay depth-open, the menu still draws (LEqual " +
                                  "at its plane) and closer hands/board still occlude both the mask and the menu.");
        else
            VRLog.Warn("WorldUI", $"MODAL DEPTH-MASK: '{_logName}' — the Overlay shader (gloomhavenvr.bundle) is " +
                                  "unavailable, so the mask material cannot write depth; HUD may still bleed through the " +
                                  "menu until the bundle ships the 'GloomhavenVR/Overlay' shader.");
    }

    private void SyncBar(float halfHeight, float width, float worldScale)
    {
        if (_bar == null || _grabZone == null)
            return;
        // All dims are frame-local metres. With the holder now at identity scale (deadlock
        // fix) the fixed constants must carry worldScale themselves so the bar/zone keep the
        // same WORLD size relative to the (worldScale-sized) panel as before.
        float gap = BarGapMeters * worldScale;
        float thickness = BarThickness * worldScale;
        float minWidth = MinBarWidth * worldScale;
        float zoneDepth = 0.05f * worldScale;
        float y = -(halfHeight + gap);
        float barWidth = Mathf.Max(width * BarWidthFraction, minWidth);
        _bar.localPosition = new Vector3(0f, y, 0f);
        _bar.localScale = new Vector3(barWidth, thickness, thickness);
        _grabZone.center = new Vector3(0f, y, 0f);
        _grabZone.size = new Vector3(Mathf.Max(width * ZoneWidthFraction, minWidth), zoneDepth, zoneDepth);
    }

    // ---- teardown ---------------------------------------------------------------------------

    /// <summary>Destroy the mod-owned holder (the game host is released separately by the caller).</summary>
    internal void Destroy()
    {
        // Problem #4: the depth mask is a child of the holder → destroyed with it below. Log once so a
        // hardware run can pair each create with its destroy (menu name).
        if (_depthMask != null)
            VRLog.Info("WorldUI", $"MODAL DEPTH-MASK: '{_logName}' destroyed with the menu.");
        if (_depthMaskMesh != null)
            Object.Destroy(_depthMaskMesh); // task #5: the dynamic mesh is an asset — free it explicitly
        if (_holder != null)
            Object.Destroy(_holder.gameObject);
        _holder = null;
        _frame = null;
        _bar = null;
        _depthMask = null;
        _depthMaskMesh = null;
        _grabZone = null;
        _handle = null;
    }
}
