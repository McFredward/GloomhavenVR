using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace GloomhavenVR.WorldUI;

// THE DIGITS IN THESE FILENAMES ARE THE SPLIT — do not rename them (rule and reasoning:
// FlatScreen.1.Core.cs). Part 1 is the argument, the state and the lifecycle; part 2 is everything
// that allocates, renders or re-layers; part 3 is the measurement and the log line.

/// <summary>
/// THE FLICKER, ROUND 10 — the first build that stops trying to make the panel's HUNDREDS of
/// point-sampled graphics behave and instead turns the whole window into ONE band-limited texture.
///
/// <para>WHAT NINE ROUNDS ESTABLISHED, so this class does not re-litigate any of it. The panels'
/// C# state is steady and both MultiPass eye passes agree on it (<see cref="PanelFlickerProbe"/>);
/// no camera renders between the two eye passes, so both eyes read identical RenderTexture content
/// (<see cref="CameraOrderProbe"/>); the source RenderTexture content is bit-identical frame to
/// frame, 567/567 readbacks (<see cref="RenderTargetProbe"/>); and ModBuild 189/190 mip-baked every
/// mipless texture a floated window samples — the hardware log reads "mips=10 Trilinear aniso 8,
/// MIP-BAKED" — and the user reports the flicker UNCHANGED. Texture-space shimmer of mipped IMAGES
/// is therefore measured out.</para>
///
/// <para>WHAT IS LEFT IS NOT A TEXTURE PROBLEM AT ALL, and no mip chain can ever reach it.
/// <see cref="PanelSamplingProbe"/>'s hardware line: <i>"PANEL SAMPLING 'UI Shop Item Window':
/// host 1920x1080 uGUI px drawn into 1032x605 rendered px = panel scale 1.86 authored px per
/// rendered px … 475 graphic(s) … 474 at or above 1.35x minification"</i>, ranging 0.47–1.93 across
/// one session as the head moved. At 1.86 authored pixels per rendered pixel the uGUI GEOMETRY
/// itself — quad edges, sliced-sprite borders, and above all MIPLESS SDF TEXT, which draws from a
/// font atlas through TMP's own shader and was never a mip-bake candidate — is RASTERIZED below
/// Nyquist. A 1 px authored stroke is asked to land on 0.54 of a rendered pixel, and whether it
/// survives is decided anew by sub-pixel head motion every frame. That is the reported
/// "es flackert", and the user's own screenshot shows its frozen form: "H LDE DIE 2 E",
/// "esundheit", every tens digit gone from the stat column (.planning/debug/flackern.jpg, read in
/// full at <see cref="PanelSamplingProbe"/>). Nothing but SUPERSAMPLING fixes undersampled
/// rasterization, and making the window physically bigger is not available: 1:1 on a 1920 px window
/// needs ~57° of view, i.e. a ~1.3 m slab at arm's length, which the user has already rejected.</para>
///
/// <para>THE MOVE. Render the floated window's canvas with a dedicated ORTHOGRAPHIC camera into a
/// RenderTexture AT (or above) its AUTHORED resolution, with MSAA, and generate a mip chain after
/// every capture. Display THAT texture on a mod-owned quad at the window's exact world pose, with
/// trilinear filtering and anisotropic filtering. The rasterization then happens at 1:1 (or better)
/// where a 1 px stroke really is 1 px, and the only thing the eye minifies is a single, properly
/// band-limited texture — the same trade every offline renderer makes. Text shimmer, geometry
/// shimmer and any per-pixel screen-space rivalry go together, because there is no longer a
/// per-pixel race between 475 graphics and the eye's sample grid; there is one texture fetch.</para>
///
/// <para>THIS IS PRIOR ART IN THIS CODEBASE, NOT AN INVENTION. <see cref="FlatScreen"/> already
/// renders the game's flat UI into a RenderTexture and shows it on a world quad, and the RT
/// descriptor is reused verbatim from that family: <c>FlatScreenStereo.CreateColorRt</c>, which
/// forces <c>D24_UNorm_S8_UInt</c> whenever a depth buffer is asked for. That stencil buffer is not
/// cosmetic here — uGUI <see cref="Mask"/> is a STENCIL effect, and a floated window without a
/// stencil buffer loses every masked scroll viewport in it.</para>
///
/// <para><b>INPUT IS NOT TOUCHED, AND THAT IS THE WHOLE REASON THIS DESIGN WAS CHOSEN.</b> The
/// floated-panel raycast path is: <c>RayUguiDriver.Tick</c> iterates the registered host canvases
/// (<c>RayUguiDriver.cs:123-149</c>), intersects the aim ray with each host RectTransform's WORLD
/// CORNERS (<c>TryIntersect</c>, <c>RayUguiDriver.cs:607-638</c>), converts the world hit to a
/// screen point through <c>canvas.worldCamera</c> (<c>ToScreen</c>, <c>RayUguiDriver.cs:665-669</c>)
/// and hands it to <c>UguiPointer.TryRaycast</c> → <see cref="GraphicRaycaster"/>
/// (<c>UguiPointer.cs:135-206</c>). Not one step of that reads which CAMERA draws the panel. So the
/// host canvas is left exactly where it is, at its world pose, enabled, registered and raycastable;
/// only its LAYER changes, and layers are invisible to every line above. Hover, press, drag, scroll,
/// the depth-aware portrait pick and the settings fall-through all keep working byte for byte.</para>
///
/// <para>THE ALTERNATIVE WAS REJECTED FOR A CONCRETE REASON. FlatScreen's model (option b: render
/// into the RT and map the quad-hit UV back to RT-camera screen coordinates) works there because the
/// game's flat UI is a SCREEN-SPACE canvas whose GraphicRaycaster already thinks in screen pixels
/// and whose RT is Screen-sized, so an RT pixel IS a screen pixel
/// (<c>FlatScreen.6.Pointer.cs:74-87</c> → <c>DirectClick</c> at <c>:486-542</c>). A floated panel is
/// the opposite: it is a WORLD-SPACE canvas, and to make its raycast go through the capture camera
/// the mod would have to re-point <c>HostCanvas.worldCamera</c> at that camera — a value
/// <c>CanvasConversion.Tick</c> RE-ASSERTS to the head camera every single frame
/// (<c>CanvasConversion.4.Lifecycle.cs:241-242</c>). That is a per-frame write war with a file this
/// lane does not own, and this project has already paid for one of those ("Don't win a write war":
/// the game re-set <c>overrideSorting</c>, the guard re-cleared it, the value ALTERNATED and the
/// MultiPass eyes disagreed). Option (a) needs no such fight.</para>
///
/// <para>HIDING THE PANEL FROM THE EYE — the one mutation that is not free, and how it avoids the
/// same trap. A world-space canvas is drawn by EVERY camera whose culling mask contains its layer,
/// so layer culling is the only mechanism that can show it to the capture camera and not to the eye.
/// The head camera's mask cannot simply be edited: <c>VRRigDriver.TickHeadCullingMask</c> re-writes
/// it every frame from the anchor camera's mask, which in a scenario is 0xFFFFFFFF — every layer
/// (<c>VRRigDriver.HeadCamera.cs:137-171</c>). So the bit is cleared in
/// <see cref="Camera.onPreCull"/> and restored in <see cref="Camera.onPostRender"/>, i.e. INSIDE
/// each camera's own render and symmetrically for both MultiPass eye passes — the identical idiom
/// <c>FlatScreenStereo</c> already uses for its per-eye texture swap. Nothing observes an
/// alternating value: VRRigDriver's per-frame write always sees the mask it wrote.</para>
///
/// <para>LAYER OWNERSHIP IS TAKEN WHOLE, never shared. While a panel is supersampled this class is
/// the ONLY writer of its subtree's layers: <see cref="OwnsPanelLayers"/> stands
/// <c>CanvasConversion.ApplyModLayer</c> down for exactly those panels (the two re-assert call sites
/// in CanvasConversion.4.Lifecycle.cs), and this class runs the same sweep on the same cadence with
/// the same rule — a subtree carrying a real <see cref="Renderer"/> or <see cref="Camera"/> is
/// skipped WHOLE. That rule is not a nicety: relayering the live 3D character rig is a bug this
/// project has already shipped and reverted ("CanvasRenderer is not a Renderer" — the preview camera
/// stopped seeing its subject and the head camera drew the raw model in the world, twice). Every
/// moved transform's original layer is recorded before the first write and restored on stand-down,
/// on release and on teardown — and only while the transform is still on OUR layer, so a restore
/// can never fight <c>CanvasConversion.Release</c>'s own restore whichever runs first.</para>
///
/// <para>MULTIPLAYER: THIS CANNOT DESYNC ANYTHING. Everything here is a local RENDERING choice —
/// a camera, a RenderTexture, a quad and a culling bit. No game state is read for a decision, no
/// game method is patched, nothing is written that another client could observe, and nothing goes on
/// the wire. The dial is per-installation and two clients running different values see identical
/// game state; the only difference is how sharp their own windows look. The mod's version handshake
/// (<c>NetProtocol.ModBuild</c>) is untouched by this lane.</para>
///
/// <para>DEGRADATION: every failure path stands the whole thing down and says so once, naming the
/// consequence. No capture layer available, no head camera, an RT that will not allocate, a rect
/// that will not measure — each ends in the panel keeping TODAY's direct rendering, which is exactly
/// the OFF behaviour. Nothing throws out of a tick.</para>
/// </summary>
internal static partial class PanelSupersample
{
    private const string Scope = "WorldUI";

    // ---- caps (constants on purpose: the user gets one switch and one factor, not five knobs) ----

    /// <summary>How many panels may be supersampled at once. Two is the realistic maximum a player
    /// reads at the same time, and it bounds the worst case to twice
    /// <see cref="MaxPanelVramBytes"/>. Panels past the cap keep today's direct rendering and the
    /// stand-down line names them.</summary>
    private const int MaxPanels = 2;

    /// <summary>Hard per-axis RT ceiling. 4096 is the conservative floor of what every desktop GPU
    /// this mod runs on supports as a render target; the factor dial cannot exceed it.</summary>
    private const int MaxRtDimension = 4096;

    /// <summary>Hard VRAM ceiling for ONE panel's render target, mips, depth/stencil and MSAA
    /// surfaces together. The allocator steps MSAA down 4 → 2 → 1 until the estimate fits, and logs
    /// which step it took, rather than silently allocating a third of a gigabyte for one window.</summary>
    private const long MaxPanelVramBytes = 96L * 1024 * 1024;

    /// <summary>Hard VRAM ceiling across every supersampled panel. A panel that would cross it is
    /// refused with a Warn and keeps today's rendering.</summary>
    private const long MaxTotalVramBytes = 256L * 1024 * 1024;

    /// <summary>MSAA sample count the allocator ASKS for (stepped down by the budget, and again if
    /// the driver refuses to create the surface). MSAA resolves the RT's own geometry edges; the mip
    /// chain resolves everything the eye then minifies. The two are complementary, not redundant.</summary>
    private const int PreferredMsaa = 4;

    /// <summary>Anisotropic level on the RT. The floated windows in the map room are yawed away from
    /// the head by up to 85°, which is precisely the case aniso exists for — at 0/1 the far edge of
    /// an angled window would blur to a smear that trilinear alone cannot avoid.</summary>
    private const int AnisoLevel = 8;

    /// <summary>
    /// Frames between captures. ONE, and the reason is CORRECTNESS, not smoothness — this is the
    /// finding of the on-demand evaluation the design asked for.
    ///
    /// <para>Unity's <see cref="GraphicRaycaster"/> skips any graphic whose <c>Graphic.depth</c> is
    /// -1, and uGUI's own comment for that test reads <i>"-1 means it hasn't been processed by the
    /// canvas, which means it isn't actually drawn"</i>. On the supersample path OUR capture camera
    /// is the only camera that draws this canvas, so a skipped capture frame is a frame in which the
    /// canvas may be processed by nobody — and every click, hover and drag on that window could die
    /// on it. A 50 %-of-frames-dead pointer is a far worse defect than the one being fixed, and it
    /// would be nearly impossible to attribute from a hardware log.</para>
    ///
    /// <para>The animation argument points the same way and is the cheaper of the two: the live
    /// character render is a game RenderTexture re-rendered every frame by the game's own preview
    /// camera, and the game's hover feedback is a LeanTween scale of ~0.15 s
    /// (<c>ExtendedButton.OnHighlight</c>) — at 90 Hz a 2-frame throttle samples that tween 6 times
    /// and reads as a step. So: capture every frame, and buy the cost back with
    /// <see cref="MaxPanels"/> instead. The measured per-capture cost is printed in the report line
    /// so a future round can revisit this with numbers rather than with this argument.</para>
    /// </summary>
    private const int CaptureIntervalFrames = 1;

    /// <summary>Layer sweep cadence for pooled/late children, mirroring
    /// <c>CanvasConversion.CanvasSweepIntervalFrames</c>. A window that repopulates brings new
    /// GameObjects on the game's UI layer; they must reach the capture layer before they are drawn
    /// straight into the eye.</summary>
    private const int SweepIntervalFrames = 15;

    /// <summary>How often the per-panel state line is printed, whether or not anything changed. A
    /// silent path and a path that never ran must never look the same (the RenderTargetProbe rule).</summary>
    private const float ReportIntervalSeconds = 10f;

    /// <summary>Re-allocate the RT when the host rect changes by more than this fraction on either
    /// axis. A content fit that trims 2 % must not cost a 21 MB reallocation.</summary>
    private const float RectChangeFraction = 0.02f;

    /// <summary>Minimum host rect (uGUI px, either axis) worth supersampling. Below this the panel
    /// is a badge, not a window, and its RT would cost more than it buys.</summary>
    private const float MinAuthoredPx = 64f;

    // ---- state -------------------------------------------------------------------------------

    private static readonly List<Entry> Entries = new(MaxPanels);

    /// <summary>Cameras whose capture-layer bit this frame's <see cref="Camera.onPreCull"/> cleared,
    /// with the mask to hand back in <see cref="Camera.onPostRender"/>. Keyed by camera; empty at
    /// every LateUpdate (asserted, and swept if not).</summary>
    private static readonly Dictionary<Camera, int> MaskedCameras = new(4);

    /// <summary>Host GameObject instance ids this path has already REFUSED (over budget, or the
    /// driver would not allocate the target). Never re-asked while that conversion lives: the
    /// refusal is already on the record as one Warn, and re-asking every Update would turn a
    /// bounded diagnosis into a per-frame log flood. Cleared when the conversion goes away.</summary>
    private static readonly HashSet<int> Refused = new(4);

    /// <summary>-2 = not resolved yet, -1 = none available (the path cannot run).</summary>
    private static int _captureLayer = -2;

    private static bool _hooksInstalled;
    private static bool _noLayerLogged;
    private static bool _noHeadLogged;
    private static bool _errorLogged;
    private static bool _capLogged;
    private static float _nextReport;
    private static int _poseSyncFrame = -1;
    private static long _vramTotal;

    private static readonly List<Transform> Scratch = new(256);
    private static readonly Vector3[] Corners = new Vector3[4];
    private static readonly StringBuilder Sb = new(1024);

    /// <summary>One supersampled panel: everything allocated for it and everything to hand back.</summary>
    private sealed class Entry
    {
        internal ConvertedPanel Panel = null!;
        internal string Window = string.Empty;

        internal GameObject CamGo = null!;
        internal Camera Cam = null!;
        internal RenderTexture Rt = null!;
        internal GameObject DisplayGo = null!;
        internal RectTransform DisplayRect = null!;
        internal Canvas DisplayCanvas = null!;
        internal RawImage DisplayImage = null!;

        /// <summary>Every transform this entry moved onto the capture layer, with the layer it had
        /// before the first write. Restored on stand-down — and only while the transform is still on
        /// OUR layer (see <see cref="RestoreLayers"/>).</summary>
        internal readonly List<LayerRecord> Relayered = new(128);

        internal Vector2 Authored;
        internal int RtW;
        internal int RtH;
        internal int Msaa;
        internal int MipCount;
        internal long VramBytes;
        internal float Factor;

        internal int NextSweepFrame;
        internal int NextCaptureFrame;
        internal int Captures;
        internal double CaptureMs;
        internal float LastCaptureStart;

        internal int LayersMoved;
        internal int ForeignSkipped;
        internal int NestedTotal;
        internal int NestedCaptured;
        internal bool OverflowWarned;
        internal bool MipWarned;
    }

    // ---- public seams -------------------------------------------------------------------------

    /// <summary>
    /// Does this class currently own <paramref name="panel"/>'s subtree layers? Read by
    /// <c>CanvasConversion</c>'s two mod-layer re-assert call sites so exactly one writer touches a
    /// given panel's layers at a time (see the class doc, "LAYER OWNERSHIP IS TAKEN WHOLE").
    /// </summary>
    internal static bool OwnsPanelLayers(ConvertedPanel? panel)
    {
        if (panel == null || Entries.Count == 0)
            return false;
        for (int i = 0; i < Entries.Count; i++)
        {
            if (ReferenceEquals(Entries[i].Panel, panel))
                return true;
        }
        return false;
    }

    /// <summary>
    /// A panel is about to be released (or was pruned dead). Stand its supersample down FIRST, while
    /// its transforms still carry our layer, so <c>CanvasConversion.Release</c>'s own layer restore
    /// then puts the game's window back on the game's layer. Idempotent and never throws.
    /// </summary>
    internal static void NoticeRelease(ConvertedPanel? panel)
    {
        if (panel == null || Entries.Count == 0)
            return;
        for (int i = Entries.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(Entries[i].Panel, panel))
                StandDown(Entries[i], i, "the window closed");
        }
    }

    /// <summary>Full teardown (module shutdown / VR off / hot reload): every panel back to direct
    /// rendering, every camera mask handed back, every allocation released.</summary>
    internal static void Shutdown()
    {
        try
        {
            for (int i = Entries.Count - 1; i >= 0; i--)
                StandDown(Entries[i], i, "module shutdown");
            RestoreMaskedCameras();
            UninstallHooks();
            Refused.Clear();
        }
        catch (System.Exception ex)
        {
            VRLog.Warn(Scope, $"PANEL SUPERSAMPLE shutdown failed ({ex.GetType().Name}: {ex.Message}) "
                              + "— a floated window may keep a mod capture layer or a camera may keep "
                              + "a narrowed culling mask until the next scene load.");
        }
    }

    // ---- per-frame service --------------------------------------------------------------------

    /// <summary>
    /// Update phase: decide which panels are supersampled, engage new ones, stand ineligible ones
    /// down. Called from <c>CanvasConversion.Tick</c>. Guarded — an unguarded exception in an Update
    /// starves VR input.
    /// </summary>
    internal static void Tick()
    {
        try
        {
            bool wanted = WorldUIConfig.PanelSupersample != null && WorldUIConfig.PanelSupersample.Value;
            if (!wanted)
            {
                if (Entries.Count > 0)
                    StandDownAll("[WorldUI] PanelSupersample was switched off");
                return;
            }
            if (CaptureLayer < 0)
            {
                if (!_noLayerLogged)
                {
                    _noLayerLogged = true;
                    VRLog.Warn(Scope, "PANEL SUPERSAMPLE stands down: no free unnamed layer for the "
                                      + "capture pass (every layer 8-31 is named, and layer "
                                      + $"{VRLayers.ModLayer} is already the mod layer). THE "
                                      + "CONSEQUENCE: every floated window keeps being rasterized "
                                      + "directly into the eye at ~1.86 authored pixels per rendered "
                                      + "pixel, i.e. exactly today's behaviour including the reported "
                                      + "text and edge shimmer. Nothing else changes.");
                }
                return;
            }

            PruneDeparted();
            Consider();
        }
        catch (System.Exception ex)
        {
            Fail(ex, "the eligibility pass");
        }
    }

    /// <summary>
    /// LateUpdate phase: keep every live entry's geometry, visibility and allocation in step with its
    /// panel. The CAPTURE itself is not driven from here — the capture camera is an ordinary enabled
    /// camera at <c>depth = -200</c>, so Unity renders it in its own camera loop AFTER every
    /// LateUpdate and BEFORE the head camera's two eye passes. Both eyes therefore read one finished,
    /// identical RenderTexture, which is the invariant <see cref="CameraOrderProbe"/> measured and
    /// this path must not break.
    /// </summary>
    internal static void LateTick()
    {
        if (Entries.Count == 0)
            return;
        try
        {
            // Safety net: onPostRender always runs, but if a camera was destroyed mid-render its
            // saved mask would leak. At LateUpdate no camera is rendering, so the table must be
            // empty; anything left is handed back here.
            RestoreMaskedCameras();

            bool report = Time.unscaledTime >= _nextReport;
            if (report)
                _nextReport = Time.unscaledTime + ReportIntervalSeconds;

            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                Entry e = Entries[i];
                if (!Eligible(e.Panel))
                {
                    StandDown(e, i, "the window is no longer eligible (closed, hidden or re-fitted "
                                    + "below the minimum size)");
                    continue;
                }
                SyncGeometry(e);
                SyncVisibility(e);
                if (Time.frameCount >= e.NextSweepFrame)
                {
                    e.NextSweepFrame = Time.frameCount + SweepIntervalFrames;
                    ApplyCaptureLayer(e, initial: false);
                }
                if (report)
                    Report(e);
            }
        }
        catch (System.Exception ex)
        {
            Fail(ex, "the per-frame sync");
        }
    }

    // ---- eligibility + engage -----------------------------------------------------------------

    /// <summary>
    /// Which panels may take this path. Deliberately narrow, and every clause is load-bearing:
    /// <list type="bullet">
    /// <item><see cref="ConvertedPanel.ModLayerEnabled"/> — the FLOATED-WINDOW family, and the only
    /// family whose subtree layers are already mod-owned. Docked HUD hosts (initiative track, actor
    /// bars, tooltips) sit on the game's UI layer, are drawn by the game's own UI Camera as well, and
    /// are not what the user reports.</item>
    /// <item>NOT <see cref="ConvertedPanel.FlattenEnabled"/> — <c>CanvasConversion.FlattenSubtree</c>
    /// re-asserts local z = 0 on every transform in the subtree each LateUpdate, which would drag the
    /// capture camera (a child of the host, standing off along local -z) onto the panel plane and
    /// capture nothing. The floated modal family does not flatten; this clause makes that explicit
    /// instead of assumed.</item>
    /// <item>visible — a panel behind the reveal gate has its canvases disabled, so a capture would
    /// bake an empty frame and the display would show a blank window at the pop-in.</item>
    /// </list>
    /// </summary>
    private static bool Eligible(ConvertedPanel? panel)
    {
        if (panel == null || !panel.IsAlive || panel.HostGo == null || panel.HostCanvas == null
            || panel.HostRect == null)
            return false;
        if (!panel.ModLayerEnabled || panel.FlattenEnabled)
            return false;
        if (panel.RevealPending || panel.RenderHidden || panel.OwnerRenderHidden)
            return false;
        Rect r = panel.HostRect.rect;
        return r.width >= MinAuthoredPx && r.height >= MinAuthoredPx;
    }

    private static void PruneDeparted()
    {
        IReadOnlyList<ConvertedPanel> live = CanvasConversion.ActivePanels;
        if (live.Count == 0 && Refused.Count > 0)
            Refused.Clear(); // no conversion alive: no refusal can still refer to one
        for (int i = Entries.Count - 1; i >= 0; i--)
        {
            Entry e = Entries[i];
            bool stillLive = false;
            for (int p = 0; p < live.Count; p++)
            {
                if (ReferenceEquals(live[p], e.Panel))
                {
                    stillLive = true;
                    break;
                }
            }
            if (!stillLive)
                StandDown(e, i, "the panel left the conversion registry");
        }
    }

    private static void Consider()
    {
        if (Entries.Count >= MaxPanels)
            return;
        IReadOnlyList<ConvertedPanel> live = CanvasConversion.ActivePanels;
        for (int i = 0; i < live.Count && Entries.Count < MaxPanels; i++)
        {
            ConvertedPanel panel = live[i];
            if (!Eligible(panel) || OwnsPanelLayers(panel)
                || Refused.Contains(panel.HostGo.GetInstanceID()))
                continue;
            Engage(panel);
        }
        if (Entries.Count >= MaxPanels && !_capLogged)
        {
            int candidates = 0;
            for (int i = 0; i < live.Count; i++)
            {
                if (Eligible(live[i]) && !OwnsPanelLayers(live[i]))
                    candidates++;
            }
            if (candidates > 0)
            {
                _capLogged = true;
                VRLog.Warn(Scope, $"PANEL SUPERSAMPLE cap reached: {MaxPanels} panel(s) are "
                                  + $"supersampled and {candidates} further eligible window(s) are "
                                  + "NOT. THE CONSEQUENCE for those windows: they keep today's direct "
                                  + "rendering — the same text and edge shimmer as before this build. "
                                  + "The cap is a VRAM bound, not a preference; the per-panel state "
                                  + "lines say what each supersampled panel costs.");
            }
        }
    }

    private static void Engage(ConvertedPanel panel)
    {
        Camera? head = VRRigDriver.HeadCamera;
        if (head == null)
        {
            if (!_noHeadLogged)
            {
                _noHeadLogged = true;
                VRLog.Warn(Scope, "PANEL SUPERSAMPLE stands down: there is no head camera "
                                  + "(VRRigDriver.HeadCamera is null), so the capture-layer bit "
                                  + "cannot be kept out of the eye and the panel would be drawn "
                                  + "TWICE. THE CONSEQUENCE: floated windows keep today's direct "
                                  + "rendering. Retried on the next tick.");
            }
            return;
        }

        string window = panel.Target != null ? panel.Target.name : panel.HostGo.name;
        Rect rect = panel.HostRect.rect;
        float factor = Mathf.Clamp(
            WorldUIConfig.PanelSupersampleFactor != null ? WorldUIConfig.PanelSupersampleFactor.Value : 1f,
            0.5f, 2f);

        int rtW = Mathf.Clamp(Mathf.RoundToInt(rect.width * factor), 16, MaxRtDimension);
        int rtH = Mathf.Clamp(Mathf.RoundToInt(rect.height * factor), 16, MaxRtDimension);

        int msaa = PreferredMsaa;
        long vram = VramBytesFor(rtW, rtH, msaa);
        while (msaa > 1 && vram > MaxPanelVramBytes)
        {
            msaa /= 2;
            vram = VramBytesFor(rtW, rtH, msaa);
        }
        if (vram > MaxPanelVramBytes || _vramTotal + vram > MaxTotalVramBytes)
        {
            Refused.Add(panel.HostGo.GetInstanceID());
            VRLog.Warn(Scope, $"PANEL SUPERSAMPLE refused '{window}': its {rtW}x{rtH} render target "
                              + $"would cost {Mb(vram)} MB (per-panel cap {Mb(MaxPanelVramBytes)} MB, "
                              + $"session total {Mb(_vramTotal)} of {Mb(MaxTotalVramBytes)} MB). THE "
                              + "CONSEQUENCE: this window keeps today's direct rendering and will "
                              + "still shimmer. Lower [WorldUI] PanelSupersampleFactor to fit it.");
            return;
        }

        RenderTexture? rt = CreateRt(rtW, rtH, ref msaa, window);
        if (rt == null)
        {
            Refused.Add(panel.HostGo.GetInstanceID());
            return;
        }

        var e = new Entry
        {
            Panel = panel,
            Window = window,
            Rt = rt,
            RtW = rtW,
            RtH = rtH,
            Msaa = msaa,
            MipCount = rt.mipmapCount,
            VramBytes = VramBytesFor(rtW, rtH, msaa),
            Factor = factor,
            Authored = rect.size,
            NextSweepFrame = Time.frameCount + SweepIntervalFrames,
        };

        if (!BuildCamera(e, panel) || !BuildDisplay(e, panel))
        {
            DestroyEntryObjects(e);
            Refused.Add(panel.HostGo.GetInstanceID());
            return;
        }

        Entries.Add(e);
        _vramTotal += e.VramBytes;
        InstallHooks();
        ApplyCaptureLayer(e, initial: true);
        SyncGeometry(e);
        SyncVisibility(e);

        VRLog.Info(Scope, $"PANEL SUPERSAMPLE engaged on '{window}': the window's canvas now renders "
                          + $"into a {rtW}x{rtH} RenderTexture (MSAA {msaa}x, mips "
                          + $"{rt.mipmapCount}, Trilinear, aniso {AnisoLevel}, {Mb(e.VramBytes)} MB) "
                          + $"through a dedicated orthographic camera, and a mod-owned quad at the "
                          + "window's exact world pose shows that texture. The host canvas itself is "
                          + $"moved to capture layer {CaptureLayer} — it stays ENABLED, REGISTERED and "
                          + "RAYCASTABLE, so every laser/poke hit, hover, drag and scroll takes the "
                          + "same path as before; only the camera that draws it changed. "
                          + $"{e.LayersMoved} transform(s) moved, {e.ForeignSkipped} foreign render "
                          + "subtree(s) left alone.");
    }
}
