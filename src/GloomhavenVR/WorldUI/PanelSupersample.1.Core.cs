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
/// <para><b>MODBUILD 193 — THE PATH WORKS, AND THE 192 LOG NAMED ITS OWN THREE DEFECTS.</b> The
/// user's verdict on 192: <i>"Durchbruch beim Flackern! Die Option 'Fenster scharf zeichnen' hat
/// das Flackern beendet. Allerdings tritt das flackern dann noch auf während dessen man das Fenster
/// verschiebt. Wenn man es dann mit in der Bewegung loslässt, werden manche Elemente nicht richtig
/// dargestellt oder andere fehlen im Fenster."</i> So the diagnosis above is confirmed for a still
/// window, and three things were still wrong — each of which the 192 log had already printed:
/// <list type="number">
/// <item><b>THE MIP CHAIN WAS NEVER BUILT.</b> Every state line read <c>mips 1 NONE</c> and the
/// class's own falsifier fired twice. CAUSE, read from the Unity contract and confirmed by the log:
/// a RenderTexture cannot be MULTISAMPLED and MIPMAPPED at the same time. <see cref="CreateRt"/>
/// asked for both (<c>antiAliasing = 4</c> and <c>useMipMap = true</c>), <c>Create()</c> returned
/// TRUE — so no allocation failure was logged — and the mip request was silently dropped;
/// <c>GenerateMips()</c> on a multisampled target is likewise a no-op. The win the user felt was
/// therefore MSAA plus 1:1 rasterization ALONE, with the eye still minifying an unfiltered texture
/// by up to 2.29x (the 192 line's own "RT texels per rendered pixel"). Unfiltered minification is
/// invisible while the head and the window are still — the alias pattern is frozen — and turns into
/// crawl the moment either moves, which is exactly the residual "flackert beim Verschieben". THE
/// FIX: two targets. The camera renders into a multisampled capture target; that target is resolved
/// with a <c>Graphics.Blit</c> into a single-sample MIPPED display target immediately after the
/// capture, and the mip chain is generated THERE. The RawImage shows the mipped one. The state line
/// now reports <c>mipmapCount</c> READ BACK FROM THE TEXTURE, and the Warn stays as the falsifier.</item>
/// <item><b>CONTENT WAS BEING CROPPED.</b> <i>"'New Party display' draws content larger than its
/// host frame (1920x1080 vs 328x1080 uGUI px)"</i>. The capture framed the host rect exactly, so
/// anything the window drew proud of that rect was cut out of the image — content the player COULD
/// see before the dial was switched on. THE RULE APPLIED: a user must never lose visible content to
/// this path, so the frame is now the UNION of the host rect and the measured drawn content
/// (<see cref="Entry.Frame"/>, <see cref="MeasureFrame"/>). WHAT IT COSTS: the "OFF and ON geometry
/// are identical" promise becomes "identical whenever the content fits the host rect, and otherwise
/// ON shows MORE — never less". That is the correct direction: OFF also showed that content.</item>
/// <item><b>THE CAP WAS SET FROM AN ESTIMATE, NOT A MEASUREMENT</b> — see <see cref="MaxPanels"/>
/// for the reconciled arithmetic and the new numbers.</item>
/// </list></para>
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

    /// <summary>
    /// How many panels may be supersampled at once. EIGHT, raised from two in ModBuild 193, and the
    /// reason the old value was wrong is worth writing down because it was set from an estimate
    /// rather than from a measurement.
    ///
    /// <para>THE 192 HARDWARE LOG: <i>"PANEL SUPERSAMPLE cap reached: 2 panel(s) are supersampled
    /// and 3 further eligible window(s) are NOT"</i> — i.e. the cap, not VRAM, was what sent three
    /// of the five open windows back to the shimmering direct path. The map room's window arc holds
    /// five at once and there are permanent windows besides, so two was never going to cover a
    /// normal session.</para>
    ///
    /// <para>WHAT A PANEL ACTUALLY COSTS, measured in that same log rather than estimated. The two
    /// engaged panels reported 14.0 MB (328x1080) and 13.5 MB (390x880); the party window then
    /// grew to its fitted 1920x1080 and RE-ALLOCATED to 81.7 MB (the "PANEL SUPERSAMPLE
    /// re-allocated 'New Party display' to 1920x1080" line). The session figure of 95.3 MB is
    /// 81.7 + 13.5 and is therefore NOT counted differently from the per-panel figures at all — the
    /// apparent discrepancy in the 192 reading was simply the engage-time number (pre-fit rect)
    /// being compared with the post-fit total. Both are right; only one of them is current.</para>
    ///
    /// <para>SO THE VRAM CEILING IS THE REAL BOUND, and this number is only there to stop an
    /// unbounded set of windows each holding a camera and a target. Eight of the SMALL windows cost
    /// ~210 MB with mips; a full-size 1920x1080 window at MSAA 4x costs ~90 MB, so
    /// <see cref="MaxTotalVramBytes"/> stops that family at four and the allocator degrades the
    /// fifth's MSAA rather than refusing it outright.</para>
    /// </summary>
    private const int MaxPanels = 8;

    /// <summary>Hard per-axis RT ceiling. 4096 is the conservative floor of what every desktop GPU
    /// this mod runs on supports as a render target; the factor dial cannot exceed it.</summary>
    private const int MaxRtDimension = 4096;

    /// <summary>
    /// Hard VRAM ceiling for ONE panel: capture target + its depth/stencil + its MSAA surfaces +
    /// the separate mipped display target, all together. The allocator steps MSAA down 4 → 2 → 1
    /// until the estimate fits and logs which step it took.
    /// <para>128 MB, sized from the measurement above: the largest window this mod has ever floated
    /// is the fitted 1920x1080 party display, which costs 79.1 MB of capture target (colour 7.9 +
    /// depth/stencil 7.9 + 4x multisample 63.3) plus 10.6 MB of mipped display target = 89.7 MB at
    /// factor 1.0. 96 MB (the ModBuild 192 value) no longer clears that once the mip target exists,
    /// and a cap a real window cannot fit under is not a cap, it is an outage.</para>
    /// </summary>
    private const long MaxPanelVramBytes = 128L * 1024 * 1024;

    /// <summary>
    /// Hard VRAM ceiling across every supersampled panel — THE real bound, and the one the cap
    /// message quotes. 384 MB holds four full-size 1920x1080 MSAA-4x windows, or all eight of the
    /// arc's typical ~14–30 MB windows with room to spare. A panel that cannot fit even at MSAA 1x
    /// is refused with a Warn and keeps today's rendering.
    /// </summary>
    private const long MaxTotalVramBytes = 384L * 1024 * 1024;

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

    /// <summary>
    /// Frames between CONTENT-FRAME measurements (<see cref="MeasureFrame"/>) while nothing about
    /// the panel is changing. The measurement is an O(subtree) walk — 2700 transforms on the party
    /// window in the ModBuild 192 log — so it is not run per frame for a settled window; every
    /// event that could invalidate it (host rect, scale, pose, a stand-up) forces it immediately
    /// instead, which is what <see cref="MarkGeometryDirty"/> is for.
    /// </summary>
    private const int ContentMeasureIntervalFrames = 15;

    /// <summary>
    /// Floor on how often the content-frame measurement may be forced by a geometry change. A
    /// two-hand resize changes the host's lossy scale EVERY frame for as long as the pinch lasts,
    /// and re-walking a 2700-transform subtree at 90 Hz for the whole gesture is a cost this path
    /// does not need to pay: two frames of lag in a rectangle the user is actively dragging is
    /// invisible, while the frame-rate cost would not be. A resize therefore re-measures at most
    /// every other frame, and the moment the gesture stops the ordinary cadence takes over.
    /// </summary>
    private const int ContentMeasureMinIntervalFrames = 2;

    /// <summary>
    /// How far the capture frame may grow beyond the host rect, as a multiple of the host rect on
    /// each axis. THIS IS THE ONLY PLACE VISIBLE CONTENT CAN STILL BE LOST, so it is generous (2x =
    /// half a host rect of overspill allowed on every edge) and it is reported: crossing it sets
    /// the state line's CONTENT field to CLAMPED and prints one Warn naming what was cut.
    /// </summary>
    private const float MaxContentExpansion = 2f;

    /// <summary>
    /// While a window is being MOVED, RESIZED or RE-FACED — and for this many frames after the last
    /// change — the capture-layer sweep runs EVERY frame instead of on
    /// <see cref="SweepIntervalFrames"/>.
    ///
    /// <para>WHY, read from source. <c>GrabbableModal.ThrottleDiagWhileMoving</c> sets
    /// <c>ConvertedPanel.Diagnostic = false</c> for up to a second at a time WHILE the host pose is
    /// changing (a deliberate log-spam fix, documented in its own header). <c>Diagnostic</c> is also
    /// what makes <c>CanvasConversion.Tick</c> run <c>AdoptNestedCanvases</c> and
    /// <c>ReassertAdoptedSorting</c> EVERY frame; while it is off, adoption falls back to the
    /// 30-frame <c>CanvasSweepNextFrame</c> schedule. So a nested canvas or a pooled child born
    /// during a drag can sit on the GAME's UI layer for tens of frames — which on this path means
    /// it is MISSING from the capture (our camera's mask is the capture layer alone) and
    /// simultaneously drawn straight into the eye at its own sorting order. That is exactly the
    /// user's ModBuild 192 report: <i>"Wenn man es dann mit in der Bewegung loslässt, werden manche
    /// Elemente nicht richtig dargestellt oder andere fehlen im Fenster."</i> Sweeping per frame
    /// while the window moves closes the window this path opened; the measured sweep cost is
    /// printed in the state line so a future round can revisit it with numbers.</para>
    /// </summary>
    private const int SweepAfterMotionFrames = 30;

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

        /// <summary>The CAPTURE target: MSAA, D24_UNorm_S8_UInt, and NO mip chain — a
        /// multisampled render target cannot carry one (see <see cref="CreateRt"/>). The camera
        /// renders here.</summary>
        internal RenderTexture Rt = null!;

        /// <summary>The DISPLAY target: single-sample, no depth, mipmapped. <see cref="Rt"/> is
        /// resolved into it after every capture and its mip chain is generated there. This is what
        /// the <see cref="RawImage"/> shows — unless <see cref="MipFallback"/>.</summary>
        internal RenderTexture MipRt = null!;

        /// <summary>The mipped display target could not be created: the RawImage falls back to the
        /// capture target, i.e. ModBuild 192's behaviour (MSAA but no mip chain). One Warn.</summary>
        internal bool MipFallback;

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

        /// <summary>
        /// THE CAPTURE FRAME, in HOST-LOCAL uGUI pixels — what the camera frames, what the display
        /// quad is sized to, and what the render target is sized from. It is the UNION of the host
        /// rect and everything the window actually draws (<see cref="MeasureFrame"/>), so it equals
        /// the host rect exactly whenever the content fits inside it — which is the normal case and
        /// keeps this path's geometry identical to the dial's OFF geometry. When the content spills
        /// (the ModBuild 192 party window drew 1920x1080 into a 328x1080 host frame) the frame
        /// grows to cover the spill instead of cropping it.
        /// </summary>
        internal Rect Frame;

        /// <summary>The host rect the <see cref="Frame"/> was measured against (uGUI px).</summary>
        internal Rect HostRectAtMeasure;

        /// <summary>How far <see cref="Frame"/> had to grow past the host rect, per axis, uGUI px.
        /// 0/0 = the promise "OFF and ON geometry are identical" holds literally.</summary>
        internal float ExpandX;
        internal float ExpandY;

        /// <summary>The expansion hit <see cref="MaxContentExpansion"/> — content IS being cropped
        /// and the state line says so every report.</summary>
        internal bool ExpandClamped;
        internal bool ExpandClampWarned;

        // ---- motion tracking (the ModBuild 192 "flackert beim Verschieben" case) ---------------
        internal bool HasPoseSnapshot;
        internal Vector3 LastPos;
        internal Quaternion LastRot;
        internal Vector3 LastScale;
        internal Vector2 LastRectSize;

        /// <summary>Frame number of the last host pose / scale / rect change. Everything within
        /// <see cref="SweepAfterMotionFrames"/> of it counts as "moving".</summary>
        internal int LastMotionFrame = -1000;

        /// <summary>Frames in this report window in which the host pose, scale or rect changed.</summary>
        internal int MotionFrames;

        /// <summary>Rect/scale changes that forced an immediate re-measure + re-sweep.</summary>
        internal int GeometryDirtyEvents;

        internal int NextSweepFrame;
        internal int NextContentFrame;
        internal int LastMeasureFrame = -1000;
        internal int NextCaptureFrame;
        internal int Captures;
        internal double CaptureMs;
        internal float LastCaptureStart;

        internal int Reallocations;
        internal int Sweeps;
        internal double SweepMs;
        internal int LateJoiners;
        internal int ContentMeasures;
        internal double ContentMs;

        internal int LayersMoved;
        internal int ForeignSkipped;
        internal int NestedTotal;
        internal int NestedCaptured;
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
                // THE MOVEMENT FIX. While the window is moving (and for SweepAfterMotionFrames
                // after), sweep every frame: that is exactly the interval in which the panel's own
                // per-frame nested-canvas adoption is throttled OFF by
                // GrabbableModal.ThrottleDiagWhileMoving, so a child born mid-drag would otherwise
                // stay on the game's UI layer — missing from the capture and double-drawn into the
                // eye. See SweepAfterMotionFrames for the full chain.
                if (IsMoving(e) || Time.frameCount >= e.NextSweepFrame)
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
    /// <item>THE FLATTEN CLAUSE IS GONE (ModBuild 193), and the reason it was here was FALSE. It
    /// read: <i>"NOT ConvertedPanel.FlattenEnabled — CanvasConversion.FlattenSubtree re-asserts local
    /// z = 0 on every transform in the subtree each LateUpdate, which would drag the capture camera
    /// (a child of the host, standing off along local -z) onto the panel plane and capture
    /// nothing."</i> That cannot happen, for three independent reasons, checked against the merged
    /// flatten lane rather than assumed:
    /// <list type="number">
    /// <item>THE WALK CANNOT REACH THE CAMERA. <c>CanvasConversion.FlattenSubtree</c> is
    /// <c>RunFlattenPass</c>, which seeds its walk at <c>panel.Target</c>
    /// (<c>CanvasConversion.2.Adopt.cs</c>, <c>panel.FlattenWalk.Add(panel.Target)</c>). The capture
    /// camera is parented to <c>panel.HostRect</c> (<see cref="BuildCamera"/>), and <c>Target</c> is
    /// ALSO a child of <c>HostRect</c> (<c>CanvasConversion.1.Core.cs</c>, <c>target.SetParent(
    /// hostRect, ...)</c>). The camera is <c>Target</c>'s SIBLING, not its descendant, so no flatten
    /// sweep of either family — the old <c>flatten2D</c> one or the new window-flatness guarantee —
    /// visits it.</item>
    /// <item>AND IT WOULD SKIP IT ANYWAY. Both flatten walks leave a subtree carrying a
    /// <see cref="Renderer"/> or a <see cref="Camera"/> alone ("foreign render subtree(s) LEFT
    /// ALONE" in their own census line), for the same reason this class's layer sweep does.</item>
    /// <item>AND THIS CLASS OWNS THE NUMBER REGARDLESS. <see cref="SyncProjection"/> rewrites the
    /// camera's local position, rotation and scale from the capture camera's OWN
    /// <see cref="Camera.onPreCull"/> — after every LateUpdate in the frame, whoever ran last, and
    /// inside the callback of the only camera that reads them. That is this project's "don't win a
    /// write war: own the number" rule applied where it is guaranteed to win, so even a future
    /// writer that DID reach the camera could not move what it renders.</item>
    /// </list>
    /// The clause was also DEAD: <c>useModLayer:true</c> is passed only by <c>ModalFallback</c> (the
    /// floated-window family) and <c>flatten2D:true</c> only by the docked-card surfaces
    /// (StatPanel, PropInfo, EnemyReveal, WorldSurface), so no panel in this codebase has ever had
    /// both — the <c>ModLayerEnabled</c> clause above already excluded every panel this one could
    /// have refused. Removing it therefore changes nothing today AND unblocks routing the floated
    /// window family through the ordinary <c>flatten2D</c> flag, which is the only reason the
    /// flatten lane needed a separate <c>ConvertedPanel.FlattenWindowGuarantee</c> flag and its own
    /// per-host driver. (That flag was never refused here in any case: this clause only ever read
    /// <c>FlattenEnabled</c>.)</item>
    /// <item>visible — a panel behind the reveal gate has its canvases disabled, so a capture would
    /// bake an empty frame and the display would show a blank window at the pop-in.</item>
    /// </list>
    /// </summary>
    private static bool Eligible(ConvertedPanel? panel)
    {
        if (panel == null || !panel.IsAlive || panel.HostGo == null || panel.HostCanvas == null
            || panel.HostRect == null)
            return false;
        if (!panel.ModLayerEnabled)
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
                long average = Entries.Count > 0 ? _vramTotal / Entries.Count : 0;
                VRLog.Warn(Scope, $"PANEL SUPERSAMPLE cap reached: {MaxPanels} panel(s) are "
                                  + $"supersampled and {candidates} further eligible window(s) are "
                                  + "NOT. THE CONSEQUENCE for those windows: they keep today's direct "
                                  + "rendering — the same text and edge shimmer as before this build. "
                                  + "HOW TO SIZE THE NEXT VALUE from this line rather than from an "
                                  + $"estimate: the {Entries.Count} panel(s) actually engaged cost "
                                  + $"{Mb(_vramTotal)} MB together, i.e. {Mb(average)} MB each on "
                                  + $"average, against a session ceiling of {Mb(MaxTotalVramBytes)} MB "
                                  + $"and a per-panel ceiling of {Mb(MaxPanelVramBytes)} MB — so "
                                  + $"{(average > 0 ? (MaxTotalVramBytes / average).ToString() : "many")} "
                                  + "panel(s) of THIS size fit in the session budget. If that number "
                                  + "is comfortably above MaxPanels, raise MaxPanels; if it is below, "
                                  + "the VRAM ceiling is the real bound and MaxPanels is not what "
                                  + "refused these windows.");
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

        var e = new Entry
        {
            Panel = panel,
            Window = window,
            Factor = factor,
            Frame = rect,
            HostRectAtMeasure = rect,
            Authored = rect.size,
            NextSweepFrame = Time.frameCount + SweepIntervalFrames,
            NextContentFrame = Time.frameCount + ContentMeasureIntervalFrames,
        };
        // Frame the CONTENT, not the host rect (see Entry.Frame): the ModBuild 192 log proved a
        // window whose content is six times wider than the frame it was measured into, and cropping
        // visible content is a worse defect than the shimmer this path removes.
        MeasureFrame(e);
        Rect frame = e.Frame;
        // `Authored` is what the render target was sized for, so it must be the FRAME, not the host
        // rect — otherwise a window whose content already overspills at engage time would look
        // "resized" to the very next LateTick and reallocate for nothing.
        e.Authored = frame.size;

        int rtW = Mathf.Clamp(Mathf.RoundToInt(frame.width * factor), 16, MaxRtDimension);
        int rtH = Mathf.Clamp(Mathf.RoundToInt(frame.height * factor), 16, MaxRtDimension);

        // The MSAA step-down now answers to the SESSION headroom as well as to the per-panel cap.
        // Before ModBuild 193 it only saw the per-panel cap, so a window that could have fitted at
        // MSAA 2x or 1x was refused outright once the session filled — the cap message then blamed
        // MaxPanels for what was really a budget decision. A degraded-MSAA panel still gets the
        // supersampled rasterization AND the mip chain, which is the whole of the fix; MSAA only
        // resolves the RT's own internal edges.
        int msaa = PreferredMsaa;
        long budget = System.Math.Min(MaxPanelVramBytes, MaxTotalVramBytes - _vramTotal);
        long vram = VramBytesFor(rtW, rtH, msaa);
        int askedMsaa = msaa;
        while (msaa > 1 && vram > budget)
        {
            msaa /= 2;
            vram = VramBytesFor(rtW, rtH, msaa);
        }
        if (vram > budget)
        {
            Refused.Add(panel.HostGo.GetInstanceID());
            VRLog.Warn(Scope, $"PANEL SUPERSAMPLE refused '{window}': its {rtW}x{rtH} capture + mip "
                              + $"targets would cost {Mb(vram)} MB even at MSAA 1x, against a "
                              + $"remaining budget of {Mb(budget)} MB (per-panel cap "
                              + $"{Mb(MaxPanelVramBytes)} MB, session {Mb(_vramTotal)} of "
                              + $"{Mb(MaxTotalVramBytes)} MB across {Entries.Count} panel(s)). THE "
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

        e.Rt = rt;
        e.RtW = rtW;
        e.RtH = rtH;
        e.Msaa = msaa;
        e.VramBytes = VramBytesFor(rtW, rtH, msaa);
        AttachMipTarget(e, rtW, rtH);

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

        string frameNote = e.ExpandX > 0.5f || e.ExpandY > 0.5f
            ? $" The capture frame was GROWN past the host rect by {e.ExpandX:F0}x{e.ExpandY:F0} uGUI "
              + "px to cover content the window draws proud of its own frame — without that, exactly "
              + "that content would be cropped out of the supersampled image."
            : " The capture frame IS the host rect to the pixel (no content draws outside it), so "
              + "this window's ON and OFF geometry are identical.";
        string msaaNote = msaa != askedMsaa
            ? $" MSAA was stepped {askedMsaa}x -> {msaa}x to fit the {Mb(budget)} MB budget left for "
              + "this panel; the supersampled rasterization and the mip chain are unaffected."
            : string.Empty;
        VRLog.Info(Scope, $"PANEL SUPERSAMPLE engaged on '{window}': the window's canvas now renders "
                          + $"into a {rtW}x{rtH} capture target (MSAA {msaa}x, D24S8) which is "
                          + $"resolved after every frame into a {rtW}x{rtH} MIPPED display target "
                          + $"(mips {(e.MipRt != null ? e.MipRt.mipmapCount : rt.mipmapCount)}, "
                          + $"Trilinear, aniso {AnisoLevel}) — {Mb(e.VramBytes)} MB for the pair — "
                          + "through a dedicated orthographic camera, and a mod-owned quad at the "
                          + $"window's exact world pose shows that texture.{msaaNote}{frameNote} The "
                          + $"host canvas itself is moved to capture layer {CaptureLayer} — it stays "
                          + "ENABLED, REGISTERED and RAYCASTABLE, so every laser/poke hit, hover, "
                          + "drag and scroll takes the same path as before; only the camera that "
                          + $"draws it changed. {e.LayersMoved} transform(s) moved, "
                          + $"{e.ForeignSkipped} foreign render subtree(s) left alone.");
    }
}
