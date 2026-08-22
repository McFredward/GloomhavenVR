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
/// <para><b>MODBUILD 198 — THE SENTENCE ABOVE IS WHERE THIS CLASS WENT WRONG, AND THE ModBuild 197
/// LOG MEASURES IT.</b> <i>"The rasterization then happens at 1:1 (or better) … and the only thing
/// the eye minifies is a single, properly band-limited texture."</i> The first half was shipped and
/// the second half does not follow from it. At 1:1 the capture holds ONE texel per authored pixel,
/// and the log's own sampling field says the eye then draws that window at 1.0 to 2.2 authored
/// pixels per RENDERED pixel — so the texture IS minified, and the only band-limited thing available
/// is mip LEVEL 1, which trilinear does not fully reach until 2 texels per rendered pixel. At factor
/// 1.0 the eye therefore keeps reading unfiltered LEVEL 0 — 45 of the 70 ModBuild 197 state lines at
/// half weight or more, 19 of them at 90 % or more, and factor measured as exactly 1.00 on every
/// single line. <b>The "supersample" path was supersampling nothing.</b></para>
///
/// <para>THE SYMPTOM THAT FOLLOWS IS THE ONE THE USER KEEPS REPORTING, both halves of it. A bilinear
/// fetch at ~1 texel per pixel has a sub-texel PHASE; its response at Nyquist runs from full contrast
/// at phase 0 to ZERO at phase 0.5. Still window = one fixed phase = looks sharp and fixed. Window in
/// the hand = the phase sweeps every frame = the same alias pattern crawls = <i>"the ORIGINAL flicker
/// problem is BACK as soon as you have the window IN YOUR HAND"</i>. Release = the phase LOCKS =
/// <i>"the state as it was during the flickering gets FROZEN, which can lead to certain elements not
/// being displayed"</i>, and a second release lands on a different phase, which is his ModBuild 195
/// <i>"bewege ich es nochmal und lasse los, sieht es wieder anders aus"</i>. The fix and the whole
/// derivation are at <see cref="BandLimitFactor"/>; the instrument that decides it from one line of
/// log is the RESAMPLE VERDICT field.</para>
///
/// <para>AND IT EXPLAINS WHY THREE ROUNDS OF CAPTURE-SIDE COUNTERS FOUND NOTHING. They were right:
/// captures equal resolves exactly, moving and still; the quad never showed the raw target; no
/// resolve ever mismatched; and captures continue at ~900 per 10 s report window after a release, so
/// nothing about the TEXTURE is stale or frozen. The defect is one step later, between the finished
/// texture and the eye, where no capture counter can see it.</para>
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
/// <para><b>MODBUILD 194 — ONE CAPTURE LAYER PER PANEL, AND THE MOTION INSTRUMENT WAS NEVER BLIND.</b>
/// Two findings, one of which corrects a premise this file used to state.
/// <list type="number">
/// <item><b>THE SHARED CAPTURE LAYER WAS PUTTING ONE WINDOW INSIDE ANOTHER.</b> The user:
/// <i>"Das Quest Window hat ein komisches Problem wenn es vor dem Händler-Window ist — ist es nah
/// genug am Händler-Window dran, stellt es teile davon dar."</i> ModBuild 193 resolved ONE capture
/// layer for the whole mod and gave every per-panel camera <c>cullingMask = 1 &lt;&lt; thatLayer</c>,
/// so each camera drew EVERY supersampled panel inside its own frustum into its own target — and
/// that frustum is as deep as the window is tall (<see cref="SyncProjection"/>), on windows that
/// stand side by side on an arc. Each panel now owns a PRIVATE layer out of a pool for as long as it
/// is engaged; the pool, the census, the three rejected alternatives and the falsifier that measures
/// how often 193 was firing all live in PanelSupersample.5.Isolation.cs. Its price is stated there
/// too: the pool is smaller than <see cref="MaxPanels"/>, so <see cref="EffectiveMaxPanels"/> is the
/// real cap and a window beyond it is REFUSED rather than made to share.</item>
/// <item><b>THE ModBuild 193 MOTION DETECTOR WORKED — THAT HYPOTHESIS IS DEAD.</b> It was suspected
/// of never firing, which would have meant every movement remedy in 193 was gated off. The hardware
/// log falsifies that outright: of its 226 <c>PANEL SUPERSAMPLE</c> state lines, 23 carry a non-zero
/// motion count — 1, 2, 12, 21, 27, 29, 35, 38, 49, 55, 64, 69, 104, 132, 235, 290, 308, 542 frames
/// in a ten-second window (a 900-frame window at 90 Hz), with several reading <c>currently MOVING</c>
/// and one showing 14 re-allocations. So <see cref="NoticeGeometry"/> saw the drags, the per-frame
/// layer sweep DID run while moving, the forced re-measure DID run and the reallocation check DID
/// run — and the user's verdict was <i>"Am Flackern beim Verschieben hat sich nichts geändert"</i>.
/// Those three remedies are therefore FALSIFIED as the cause of the moving shimmer, not untested.
/// The detector is now self-falsifying instead of merely correct: it reports how many comparisons it
/// made, the largest single-frame step it saw and the epsilon it tested against, in world units AND
/// in authored pixels, so "0 motion frames" can never again be confused with "the instrument did not
/// run" (<see cref="Entry.MotionTicks"/>).</item>
/// </list></para>
///
/// <para><b>MODBUILD 196 — THE BROKEN-ON-RELEASE IMAGE IS NOT UNDERSAMPLING, AND THE MOVEMENT
/// REMEDY WAS PAYING FOR ITSELF OUT OF THE FRAME BUDGET.</b> Two findings, one of which retires this
/// file's own prior diagnosis for one specific symptom.
/// <list type="number">
/// <item><b>THE "KAPUTTE ANZEIGE" IS PER-GLYPH DROPOUT WITH THE LAYOUT INTACT, WHICH UNDERSAMPLING
/// CANNOT PRODUCE.</b> The user, after 195: <i>"Beim Loslassen kann es passieren, dass die
/// dargestellte Anzeige kaputt ist ... bewege ich es nochmal und lasse los, sieht es wieder anders
/// aus."</i> The photograph (.planning/debug/kaputte_anzeige.jpg) was MEASURED off the pixels rather
/// than described, and four measurements each kill a candidate. (i) THE ADVANCES ARE FULL WIDTH: the
/// trailing colons of <i>Verstärkungen:</i> (14 characters) and <i>Verbesserungen:</i> (15) sit one
/// character's advance apart, so nothing was substituted, shortened or removed — every character is
/// still in the layout and its quad simply put no pixels down. (ii) THE GAPS ARE EMPTY, NOT DIM: peak
/// luminance where <i>d h e</i> of "Gesundheit" belongs is 24, against a 20 background and a 147 ink,
/// so this is not a contrast or alpha artifact with a faint residue. (iii) THE SAME WINDOW'S SMALLER
/// TEXT IS PERFECT: <i>"Schließe sechs Basisspiel-Nebenszenarien ab."</i> renders every character
/// while <i>Gold:</i> renders "Go" — minification below Nyquist dims and blurs uniformly and cannot
/// delete some glyphs of one label while leaving a smaller label whole. (iv) THE GAPS DO NOT ALIGN:
/// the ink runs of the six rows form no vertical stripes, which is what a sampling-phase artifact
/// would look like, and gaps sit INSIDE words with ink on both sides, which is what rules occlusion
/// out (an occluder is a rectangle). WHAT THAT LEAVES is a text-generation fault, and
/// <see cref="MeasureContent"/> is the instrument that decides which one from the log alone: glyphs
/// the font asset does not have, characters the layout marked not visible, and characters that are
/// visible with a zero-area quad are three separate counters, each printed next to the number of
/// lookups it came from. The repair prefers FORCING THE REGENERATION — re-request the characters into
/// the atlas, then re-parse and re-generate the mesh — over re-taking the capture, because a stale
/// mesh re-captured is still a stale mesh. It runs twice per release and once per detected atlas
/// repack, never per frame. NOTE WHAT IT CANNOT SEE: it measures the text SOURCE, so a graphic that
/// drew correctly and was then painted over is invisible to it; that question belongs to the draw
/// order, and the report says so rather than letting a clean scan read as "nothing is covering
/// anything".</item>
/// <item><b>THE PER-FRAME MOTION SWEEP CAUGHT ONE ARRIVAL AND COST A TENTH OF EVERY DRAG FRAME.</b>
/// ModBuild 193 swept the capture layer EVERY frame while a window moved. The ModBuild 195 log prices
/// it in two independent numbers: of the 23 sweeps that actually moved a late transform, <b>22 ran on
/// the periodic cadence and exactly ONE on the per-frame motion cadence</b> (the log line names its
/// own cadence), while the sweep itself averaged 1.48–1.94 ms — on a session reading <c>frametime
/// mean 18.31 p50 17.33 p95 24.98 p99 29.93 max 51.75 ms</c> against an 11.11 ms budget, of which
/// <c>logic</c> is 14–16 ms. At the same time the INSTRUMENT SELF-CHECK field measured real drags at
/// 12, 19, 33, 47, 74, 94, 122 and 133 RENDERED eye pixels per frame, which is the case its own
/// ModBuild 194 clause identifies as JUDDER rather than sampling: at that rate a dropped frame is
/// over a hundred pixels of positional error. So the movement remedy was spending ~10 % of the
/// main-thread frame on the one activity the user reports as broken, and buying one arrival with it.
/// The motion cadence is now <see cref="MovingSweepIntervalFrames"/>, the release is covered outright
/// and unconditionally by <see cref="ReleaseSettleFrames"/>, and the state line now reports frame
/// time split into MOTION and STILL buckets against a stated 11.11 ms threshold so the next round can
/// price this instead of arguing about it.</item>
/// </list></para>
///
/// <para><b>MODBUILD 197 — THE ModBuild 196 INSTRUMENT WAS SOUND AND ITS REMEDY NEVER RAN, AND
/// BOTH OF THOSE ARE READ OFF ITS OWN LOG.</b> The user, after 196: <i>"das Flackerproblem WÄHREND
/// DER BEWEGUNG ist noch da — inklusive der möglichen kaputten Darstellung, wenn man nach der
/// Bewegung ABRUPT loslässt."</i> ABRUPT is a new word and it is a claim about release VELOCITY.
/// This round changed the METHOD rather than the hypothesis, and it produced four findings.
/// <list type="number">
/// <item><b>THE GLYPH SCAN RAN, WORKED, AND MEASURES THE WRONG QUANTITY.</b> Its whole output was
/// counted rather than sampled: <b>236 readings — 152 reading <c>0 not-in-atlas / 0 not-visible /
/// 0 zero-area</c> and 84 reading exactly one parsed-but-not-visible — with 0 atlas repacks, in a
/// session containing real drags and up to 140 release repairs on one window.</b> The photograph
/// shows dozens of missing glyphs across six rows. So the instrument is not blind and it is not
/// coincidence-limited; it is measuring something upstream of the fault. Every ModBuild 196 counter
/// reads <see cref="TMPro.TMP_CharacterInfo"/>, which is the LAYOUT RECORD the text engine writes
/// before any mesh exists. <see cref="ScanTmpMesh"/> is the new measurement and it reads the vertex
/// and UV arrays that are actually uploaded. A glyph whose four atlas UVs have collapsed onto one
/// texel keeps its full advance, keeps a full-area quad, reports <c>isVisible</c>, exists in the
/// atlas — and draws a flat SDF value, i.e. no ink. That is the photograph's exact signature, and
/// <b>nothing before this build read a single UV</b>.</item>
/// <item><b>THE RELEASE REPAIR WAS A NO-OP FOR TEXT.</b> ModBuild 196's own documentation said it
/// forces every text component to re-request its glyphs and re-generate its mesh. It regenerates
/// only components its scan marked bad (<c>if ((!repairAll &amp;&amp; bad == 0) || ...) return
/// false;</c>), and that count is permanently zero — so across 236 state lines it re-generated <b>at
/// most ONE component per release, and 151 lines read zero</b>, on windows carrying up to 297 text
/// components and 4600 glyphs. THE USER REPORTING NO IMPROVEMENT IS THEREFORE THE EXPECTED RESULT
/// AND CARRIES NO INFORMATION about the text hypothesis. <see cref="ReleaseRepair"/> now passes
/// <c>repairAll</c>; the report cadence's scan still does not, so a settled window costs what it
/// costs today.</item>
/// <item><b>A DRAGGED WINDOW IS FULLY SUPERSAMPLED, WHICH KILLS "IT SWITCHES OFF WHILE I DRAG".</b>
/// The ModBuild 196 log's capture counts are 1:1 with its frame counts (894 captures against 894
/// sampled frames in a report window), <see cref="CaptureIntervalFrames"/> is 1, and
/// <see cref="ResolveAndMip"/> runs in the capture camera's own <c>onPostRender</c>. So the moving
/// case takes the identical path to the still case the user has already accepted as fixed. That is
/// now a COUNT rather than an argument from source: see <c>AppendCapturePath</c>, which splits
/// captures and resolves into MOTION and STILL buckets and carries three expected-zero falsifiers.</item>
/// <item><b>THE MOVING CASE LOOKS LIKE JUDDER, AND THE ModBuild 196 LOG ALREADY SAYS SO.</b> Its
/// MOTION BUDGET field measured 18 report windows with real drags: while MOVING mean 11.05–11.27 ms
/// and 48–50 % of frames over the 11.11 ms budget, while STILL mean 11.09–13.03 ms and 45–50 % over.
/// <b>The drag frames are not measurably worse than the still frames</b> — the ModBuild 196 throttle
/// of the motion sweep worked and this class no longer costs anything extra during a drag. What
/// remains is that the whole session sits ON the budget with half its frames just over it, while the
/// same log measures real drags at up to 32.9 RENDERED eye pixels of window travel per frame. A
/// re-shown frame during a drag is therefore tens of pixels of positional error on a high-contrast
/// edge, and the compositor cannot correct it: reprojection compensates HEAD motion, not a window
/// travelling under the player's hand. <see cref="Entry.DragDroppedFrames"/> counts that directly
/// against <see cref="DroppedFrameMs"/> and the RELEASE line prints it next to the travel per frame,
/// because the PRODUCT of those two is what the eye reads as flicker. If it is judder, the remedy is
/// not on the panel surface at all and this class should stop being asked for one.</item>
/// </list></para>
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
    ///
    /// <para><b>MODBUILD 194 CAPS THIS AGAINST THE LAYER POOL.</b> Every supersampled panel now owns
    /// a PRIVATE capture layer (<see cref="PoolSize"/>, and the whole argument in
    /// PanelSupersample.5.Isolation.cs), because ModBuild 193 gave them all the same one and each
    /// camera therefore captured every neighbour inside its frustum. Unity has 32 layers, most of
    /// them named by the game, so the real cap is <see cref="EffectiveMaxPanels"/> =
    /// min(this, pool) — expected to be SEVEN on this game (layers 20-26). Read THAT everywhere,
    /// never this constant.</para>
    /// </summary>
    private const int MaxPanels = 8;

    /// <summary>Hard per-axis RT ceiling. 4096 is the conservative floor of what every desktop GPU
    /// this mod runs on supports as a render target; the factor dial cannot exceed it.</summary>
    private const int MaxRtDimension = 4096;

    /// <summary>
    /// Hard VRAM ceiling for ONE panel: capture target + its depth/stencil + its MSAA surfaces +
    /// the separate mipped display target, all together. The allocator steps MSAA down 4 → 2 → 1
    /// until the estimate fits and logs which step it took.
    /// <para>160 MB (ModBuild 198, raised from 128). THE ARITHMETIC, from the ModBuild 197 hardware
    /// log's own numbers rather than from an estimate. The largest window this mod floats is the
    /// party display at a measured 1971x1458 capture frame. At the OLD operating point (factor 1.0,
    /// MSAA 4x) it cost 124.2 MB: capture 109.6 (colour 11.0 + depth/stencil 11.0 + 4x multisample
    /// 87.7) + mipped display 14.6. At the NEW one (<see cref="BandLimitFactor"/> 2.0, MSAA 1x) it
    /// costs 146.2 MB: capture 87.7 (colour 43.9 + depth/stencil 43.9, no multisample surfaces) +
    /// mipped display 58.5. That is +18 % for the same window, and it buys the whole of the fix
    /// described in <see cref="BandLimitFactor"/>. 128 MB would refuse it outright, and a cap a real
    /// window cannot fit under is not a cap, it is an outage.</para>
    /// </summary>
    private const long MaxPanelVramBytes = 160L * 1024 * 1024;

    /// <summary>
    /// Hard VRAM ceiling across every supersampled panel — THE real bound, and the one the cap
    /// message quotes. 448 MB (ModBuild 198, raised from 384) so the ceiling still holds exactly
    /// THREE full-size windows after the factor/MSAA trade above: 3 x 146.2 = 438.6 MB, where 384 MB
    /// held 3 x 124.2 = 372.6 MB. How many windows this path can serve is therefore unchanged; only
    /// what each of them buys changed. All eight of the arc's typical ~14–30 MB windows still fit
    /// with room to spare. A panel that cannot fit even at MSAA 1x is refused with a Warn and keeps
    /// today's rendering.
    /// </summary>
    private const long MaxTotalVramBytes = 448L * 1024 * 1024;

    /// <summary>
    /// MSAA sample count the allocator ASKS for (stepped down by the budget, and again if the driver
    /// refuses to create the surface).
    ///
    /// <para><b>ONE, from FOUR — ModBuild 198, and this is a TRADE, not a saving.</b> MSAA 4x was
    /// costing 87.7 MB of the party window's 124.2 MB — <b>71 % of the entire per-panel budget</b> —
    /// and on this content it buys almost nothing, for a reason specific to what this camera renders.
    /// The capture camera is ORTHOGRAPHIC and exactly aligned to the panel plane
    /// (<see cref="SyncProjection"/>), and uGUI draws axis-aligned quads in that plane, so at an
    /// integer factor a quad's edges land on exact texel boundaries and there is no geometry edge for
    /// multisampling to resolve. What the eye actually sees the edges of — glyphs, icons, card art —
    /// is TEXTURE content (TMP samples an SDF atlas), and MSAA has never touched texture content.
    /// Only rotated or fractionally-placed quads benefit, and they are a small minority.</para>
    ///
    /// <para><b>AND SUPERSAMPLING SUBSUMES IT.</b> Rendering at <see cref="BandLimitFactor"/> = 2 and
    /// box-filtering down one mip level is four samples per authored pixel applied to EVERYTHING —
    /// geometry edges AND texture content — which is strictly more than 4x MSAA delivered, at less
    /// memory than 4x MSAA cost. The two were documented here as "complementary, not redundant";
    /// against a 1:1 factor that was true, but the money was in the wrong one of the two.</para>
    ///
    /// <para>The step-down machinery is kept — it is what degrades gracefully if a future factor or a
    /// larger window pushes past the budget — and simply has nothing to step down from at 1.</para>
    /// </summary>
    private const int PreferredMsaa = 1;

    /// <summary>
    /// <b>THE MINIMUM RENDER-TARGET TEXELS PER AUTHORED PIXEL — the ModBuild 198 fix, and the whole
    /// of the "the flicker is back as soon as the window is IN MY HAND" report.</b>
    ///
    /// <para><b>WHAT THE ModBuild 197 LOG MEASURED.</b> All 70 of its state lines report
    /// <c>texelsPerPixel / authoredPerPixel = 1.00</c>, i.e. the session ran at
    /// <c>[WorldUI] PanelSupersampleFactor = 1.0</c> — its shipped default — so <b>the "supersample"
    /// path was allocating exactly one render-target texel per authored window pixel and
    /// supersampling nothing</b>. Read the whole distribution of the sampling field rather than its
    /// mode: authored pixels per RENDERED eye pixel runs 0.73 to 2.18 with the mode at ~1.05, so the
    /// quad is displayed at roughly <b>one RT texel per rendered eye pixel</b> for most of a session.
    /// 45 of those 70 lines have half or more of every texture sample coming from UNFILTERED mip
    /// level 0, and 19 of them have 90 % or more.</para>
    ///
    /// <para><b>WHY THAT IS EXACTLY "STILL = FIXED, IN MY HAND = BROKEN".</b> Sampling a texture at
    /// ~1 texel per pixel with a bilinear filter is a resample whose kernel is <c>[1-p, p]</c> for
    /// the sub-texel phase p. At p = 0 it reproduces the source exactly; at p = 0.5 it averages two
    /// neighbours and its response at Nyquist is ZERO. A one-pixel glyph stroke therefore swings
    /// between full contrast and NO CONTRAST as p sweeps — and p is a function of where the window
    /// sits relative to the eye's pixel grid. <b>A still window has a CONSTANT phase: the pattern
    /// freezes and reads as "sharp".</b> A window in the hand sweeps the phase every frame: the same
    /// pattern crawls, which is the flicker. <b>And on release the phase LOCKS at whatever value the
    /// hand left behind</b> — which is the user's "the state as it was during the flickering gets
    /// FROZEN, which can lead to certain elements not being displayed", and his earlier ModBuild 195
    /// words <i>"bewege ich es nochmal und lasse los, sieht es wieder anders aus"</i>: another
    /// release, another resting phase, another set of strokes washed out. No content defect can
    /// produce that signature; a resampling phase produces exactly it.</para>
    ///
    /// <para><b>WHY ModBuild 197's "894 CAPTURES / 894 FRAMES" WAS RIGHT AND STILL MISSED IT.</b> It
    /// measured the CAPTURE, and the capture is flawless — this log confirms it on every panel:
    /// captures equal resolves exactly, moving and still; 0 frames on which the quad showed the raw
    /// target; 0 resolves with mismatched sizes; and ~900 captures per 10 s report window continuing
    /// forever after a release, so the displayed texture is re-captured at 90 Hz and is never stale.
    /// The defect is one step LATER, in how the eye samples the finished texture, and no
    /// capture-side counter can see it.</para>
    ///
    /// <para><b>WHY 2.0 AND NOT A MIP BIAS.</b> A positive <c>mipMapBias</c> would also stop the eye
    /// reading level 0 and costs no memory — but it forces a level COARSER than the eye's rate, i.e.
    /// it pays for stability in blur, and the still-window sharpness is this project's accepted win.
    /// Factor 2.0 instead makes mip level 1 — the level the eye lands on at ~1 authored px per
    /// rendered px — a proper 2x2 box downsample of a 2x rasterization. That level carries the
    /// authored resolution with its near-Nyquist content already attenuated by the box filter, so the
    /// phase term has far less to modulate AND the image is 4x-supersampled rather than merely
    /// rasterized. The still window gets SHARPER, not softer.</para>
    ///
    /// <para><b>THE COST IS PAID FOR IN <see cref="PreferredMsaa"/>,</b> not in new memory: +18 % per
    /// full-size window, and the same three of them fit the session ceiling as before.</para>
    ///
    /// <para><b>THIS IS A FLOOR ON THE DEFAULT, NEVER AN OVERRIDE OF A TUNED VALUE.</b> It applies
    /// only while <c>[WorldUI] PanelSupersampleFactor</c> still reads its shipped default; a value
    /// the user has actually set is taken verbatim (this project's "tuned cfg drops are current"
    /// rule). Which of the two happened is printed once per panel at engage, with both numbers.</para>
    /// </summary>
    private const float BandLimitFactor = 2f;

    /// <summary>
    /// RT texels per RENDERED eye pixel at and above which the hardware's trilinear filter stops
    /// blending in unfiltered mip level 0 at all — i.e. the threshold the resample instrument judges
    /// every measurement against. TWO, and it is arithmetic, not a preference: the selected LOD is
    /// <c>log2(texels per pixel)</c> and level 0 keeps a weight of <c>1 - LOD</c> until LOD reaches
    /// 1, which is exactly 2 texels per pixel.
    /// <para>Anisotropic filtering makes this a LOWER BOUND rather than a guarantee: aniso selects
    /// the LOD from the MINOR axis' rate, so a window yawed away from the head reads a lower LOD —
    /// more level 0 — than this figure states. That is a deliberate trade (see
    /// <see cref="AnisoLevel"/>) and the instrument says so on the line.</para>
    /// </summary>
    private const float BandLimitedTexelsPerPixel = 2f;

    /// <summary>How much the allocator gives back per step when a panel does not fit its VRAM budget,
    /// and the floor it stops at. 1.0 is deliberately the floor and not lower: at factor 1.0 the
    /// window has exactly ModBuild 197's behaviour, which is a known, shipped, playable state, whereas
    /// below 1.0 the capture would be COARSER than the authored art and would soften text that the
    /// direct path renders sharp. See the step-down loop in <see cref="Engage"/> for why a factor
    /// concession replaced the MSAA concession.</summary>
    private const float FactorStepDown = 0.25f;

    /// <summary>Floor for <see cref="FactorStepDown"/> — see it.</summary>
    private const float MinStepDownFactor = 1f;

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
    /// <b>THE CAPTURE FRAME IS QUANTISED TO THIS GRID (host-local uGUI px), AND THAT IS THE FLICKER
    /// HALF OF ModBuild 201.</b>
    ///
    /// <para><b>WHAT THE ModBuild 200 LOG SHOWS.</b> Every window in that session holds a rock-steady
    /// capture frame — <c>Quest Log Manager</c> 390x880 on every one of its state lines,
    /// <c>UI Map Esc Menu</c> 405x1080 on every one of its. The character window does not:
    /// 1964x1453, then 1715x1080, 1109, 1082, 1110, 1200, 1099, 1107 — its height wanders by up to
    /// 120 authored px across the session, because <see cref="MeasureFrame"/> takes the union of what
    /// LIVE content draws and that window's content animates and scrolls. <b>It is the only window
    /// whose frame moves, and it is the only window the user reports as flickering.</b></para>
    ///
    /// <para><b>WHY A MOVING FRAME IS A DEFECT AND NOT JUST AN ALLOCATION CHURN.</b> The capture
    /// camera is orthographic and framed on <c>Frame</c>; the render target is
    /// <c>round(Frame.size * Factor)</c>. So the mapping from an authored pixel to a render-target
    /// TEXEL is <c>(authored - Frame.min) * Factor</c> — and <c>Frame.min</c> is an arbitrary float
    /// that the union hands over. When the frame wanders by 19 px, every authored pixel in the window
    /// lands on a DIFFERENT SUB-TEXEL PHASE, and the whole image is re-rasterised at a new phase. For
    /// content that has texels to spare that is invisible. For content sampled at about one texel per
    /// authored pixel it decides, glyph by glyph and hairline by hairline, which strokes land on a
    /// texel centre and survive and which fall between two and disappear. That is a re-roll of the
    /// picture on the measurement cadence — every 15 frames while still, every 2 frames while the
    /// geometry is changing — and it is precisely the user's <i>"es ist ziemlich zufällig, es kommt
    /// darauf an, wann man loslässt"</i> and his <i>"mit etwas Glück kann ich es wieder lesen"</i>.
    /// The capture is otherwise pose-invariant, so nothing else in this path can produce a picture
    /// that changes while the canvas underneath it does not.</para>
    ///
    /// <para><b>WHAT THE GRID DOES, AND WHAT IT IS ANCHORED ON.</b> What is quantised is the frame's
    /// OVERSPILL PAST THE HOST RECT, per edge, rounded OUTWARD (never inward — this must not be able
    /// to crop a pixel ModBuild 200 kept), and the expansion limit is rounded outward too. It is
    /// deliberately NOT the absolute edge: a host rect is 390x880 or 1143x1080 px and its half-
    /// extents are multiples of nothing, so snapping absolute edges would inflate EVERY window
    /// (390x880 -> 448x896) and destroy this path's "the capture frame IS the host rect, so the
    /// dial's ON and OFF geometry are identical" promise on windows that overspill by nothing at all.
    /// Anchored on the host edges, a non-overspilling frame stays exactly the host rect and the
    /// property the grid exists for still holds: the host rect does not move between two content
    /// measurements, so every frame edge differs from the last by a whole number of grid cells, and
    /// with the rate quantised to <see cref="RateQuantum"/> that is a whole number of TEXELS. The
    /// residual phase drift is the rounding of <c>hostWidth * rate</c> to an integer texel count,
    /// which is under 0.01 authored px across a 1143 px window — against the arbitrary fractional
    /// offsets an unquantised union hands over today.</para>
    ///
    /// <para>32 px, against windows of 390–1964 px: coarse enough that the observed 19–120 px wander
    /// costs at most one or two grid steps, fine enough that the worst-case wasted margin is 31 px on
    /// an edge — under 2 % of the smallest window here, i.e. below
    /// <see cref="RectChangeFraction"/>'s own reallocation threshold.</para>
    /// </summary>
    private const float FrameQuantumPx = 32f;

    /// <summary>
    /// The capture rate (render-target texels per authored pixel) is quantised to a multiple of this.
    /// <para>It exists only to keep <see cref="FrameQuantumPx"/>'s promise once the rate is no longer
    /// a round number: with a 32 px grid and a rate that is a multiple of 0.25, one grid cell is a
    /// whole number of texels (32 x 0.25 = 8), so <c>round(Frame.size * rate)</c> is exact and the
    /// texel grid stays locked to the authored grid. Without it, a rate of, say, 2.388 (what
    /// <see cref="MaxRtDimension"/> leaves for the character window) would put a fresh fractional
    /// offset back into the mapping and undo the quantisation.</para>
    /// </summary>
    private const float RateQuantum = 0.25f;

    /// <summary>
    /// A graphic must cover at least this fraction of the host rect before it is allowed to lower
    /// <see cref="Entry.MinContentScale"/> and therefore raise the whole window's render target.
    /// <para>A bare minimum over every graphic is the wrong statistic: one decorative pip authored at
    /// 512 px and drawn at 24 would read 0.05 and ask for a 40x render target. 1 % of a 1143x1080
    /// host is a 111x111 px element — small enough that every real sub-view and every plate inside
    /// one qualifies, large enough that no icon can hijack the allocation.</para>
    /// </summary>
    private const float MinScaledAreaFraction = 0.01f;

    /// <summary>Below this scale a graphic is counted as MATERIALLY DOWNSCALED in the report's
    /// content-scale census. Purely a reporting threshold; it gates nothing.</summary>
    private const float ScaledContentThreshold = 0.95f;

    /// <summary>Hard floor on <see cref="Entry.MinContentScale"/>. Caps the boost this can ask for at
    /// <c>BandLimitFactor / 0.25 = 8x</c> BEFORE the dimension and VRAM ceilings cut it down, so a
    /// pathological measurement can never turn into a pathological allocation request.</summary>
    private const float MinContentScaleFloor = 0.25f;

    /// <summary>
    /// While a window is being MOVED, RESIZED or RE-FACED — and for this many frames after the last
    /// change — the capture-layer sweep runs on the faster <see cref="MovingSweepIntervalFrames"/>
    /// cadence instead of on <see cref="SweepIntervalFrames"/>.
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

    /// <summary>
    /// Layer-sweep cadence WHILE the window is inside its motion window, in frames.
    ///
    /// <para><b>THIS NUMBER USED TO BE 1, AND THE ModBuild 195 HARDWARE LOG RETIRED THAT.</b> ModBuild
    /// 193 swept EVERY frame while a window moved, on the argument in
    /// <see cref="SweepAfterMotionFrames"/> — a pooled child born mid-drag would otherwise sit on the
    /// game's UI layer for tens of frames. The argument is sound; the CADENCE was not, and the log
    /// says so in two independent numbers.
    /// <list type="number">
    /// <item><b>THE PER-FRAME CADENCE CAUGHT ONE ARRIVAL IN A WHOLE SESSION.</b> Every sweep that
    /// actually moved a late transform prints which cadence it ran on. Of the 23 such lines in the
    /// ModBuild 195 log, <b>22 read "reached its periodic cadence" and exactly ONE read "is MOVING
    /// (per-frame cadence)"</b>. So the arrivals this class exists to catch are not, in practice,
    /// born during drags at all — they are born when a window repopulates, and the 15-frame cadence
    /// catches them.</item>
    /// <item><b>AND IT COSTS 1.5 ms OF A FRAME THAT IS ALREADY OVER BUDGET.</b> The same log reports
    /// the sweep at 1.48–1.94 ms EACH (its own averaged field) on the party window, and the session's
    /// frame telemetry reads <c>frametime mean 18.31 p50 17.33 p95 24.98 p99 29.93 max 51.75 ms</c>
    /// against an 11.11 ms budget at 90 Hz, of which <c>logic</c> is 14–16 ms. Sweeping per frame
    /// therefore spent ~10 % of the main-thread frame on the one activity that the user reports as
    /// broken, and it did so ONLY while the window moved — i.e. it made the drag frame the worst
    /// frame in the session. A window dragged at the measured 30–133 RENDERED eye pixels per frame
    /// cannot survive a dropped frame: one missed frame is 30–133 px of positional error, which is
    /// the "flackern beim Verschieben" the movement remedy was supposed to fix.</item>
    /// </list></para>
    ///
    /// <para>FIVE frames keeps three times the settled cadence's coverage for a fifth of the
    /// per-frame cost, and the case the per-frame cadence was really aimed at — the RELEASE — is now
    /// covered outright and unconditionally by <see cref="ReleaseSettleFrames"/>, which sweeps,
    /// re-measures and re-generates the moment the window comes to rest whether or not a cadence is
    /// due. The report prints the sweep count and cost split into MOTION and STILL frames so the next
    /// round can price this change instead of arguing about it.</para>
    /// </summary>
    private const int MovingSweepIntervalFrames = 5;

    /// <summary>
    /// Frames of stillness after the last pose/scale/rect change at which the RELEASE REPAIR runs:
    /// one forced content re-measure, one forced layer sweep and one forced text regeneration, all
    /// unconditional. This is the answer to <i>"beim Loslassen kann es passieren, dass die
    /// dargestellte Anzeige kaputt ist"</i> — the moment a window stops moving is exactly the moment
    /// its content fit has finished, its pooled children have arrived and its glyphs are final, and
    /// it is the only moment at which repairing all three costs nothing per frame.
    /// <para>TWO frames, not zero: the content fit and the release re-face both write in the same
    /// frame the grab ends, and repairing before they land would repair the wrong state.</para>
    /// </summary>
    private const int ReleaseSettleFrames = 2;

    /// <summary>A SECOND release repair this many frames after the first. The content fit can trim a
    /// window a few frames after the release (the log's host rect flips 328 -> 716 -> 1920 uGUI px on
    /// one window in one session), and a repair that ran before that flip repaired the previous
    /// layout. Cheap: it is two passes per release, not per frame.</para></summary>
    private const int ReleaseSecondRepairFrames = 12;

    /// <summary>The per-frame budget a 90 Hz headset gives, in milliseconds. Used ONLY as the
    /// threshold the motion/still frame-time instrument reports against, so "N of M motion frames
    /// were over budget" is a statement with a stated bar rather than an adjective.</summary>
    private const float FrameBudgetMs = 1000f / 90f;

    /// <summary>Hard ceiling on how many GLYPH lookups one content-integrity scan may make, across
    /// every text component of one panel. The party window carries ~2700 transforms; an unbounded
    /// character scan on the report cadence would be a spike of its own. When the ceiling bites the
    /// report says so, so a truncated scan can never be mistaken for a clean one.</summary>
    private const int MaxGlyphChecksPerScan = 6000;

    /// <summary>Zero-area threshold for a generated glyph quad, in the text object's own local units.
    /// A quad below this cannot put a pixel anywhere at any resolution.</summary>
    private const float DegenerateQuadArea = 1e-6f;

    /// <summary>
    /// Zero-area threshold for a glyph's ATLAS UV rectangle, in normalised atlas units squared.
    /// <para>Sized against the real numbers rather than picked: this game's labels draw from a
    /// 2048x1024 'Sarala-Regular SDF' atlas, where even a full stop occupies roughly 10x10 atlas
    /// texels = 0.0049 x 0.0098 = 4.8e-5 in UV area, and a body glyph is an order of magnitude
    /// larger. 1e-9 is therefore four orders below the smallest legitimate glyph and far above
    /// single-precision noise on values of that size, so a quad counted here is one whose four UVs
    /// have genuinely collapsed onto a single atlas point.</para>
    /// </summary>
    private const float DegenerateUvArea = 1e-9f;

    /// <summary>
    /// A frame this long counts as a DROPPED frame during a drag: the display had to show the
    /// previous image again. 1.5 budgets, i.e. 16.67 ms at 90 Hz — comfortably above ordinary
    /// v-sync jitter around 11.11 ms and below two whole frames, so it counts a miss and not a
    /// wobble. Reported with the comparison count and the worst value on the same line.
    /// </summary>
    private const float DroppedFrameMs = FrameBudgetMs * 1.5f;

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

    private static bool _hooksInstalled;

    // ---- CONTENT-INTEGRITY HOOKS (ModBuild 196) -----------------------------------------------
    // The three states the "kaputte Anzeige" report has to be able to tell apart, and the only
    // reason these are static rather than per-entry: a font atlas repack and a canvas update are
    // PROCESS-wide events, and a per-entry copy of them would say the same thing N times.

    /// <summary>Frame number of the last <see cref="Font.textureRebuilt"/> (a DYNAMIC font atlas was
    /// re-packed, which invalidates every already-generated text mesh that samples it) or of the last
    /// TMP atlas change this class detected. -1000 = never.</summary>
    private static int _fontRebuildFrame = -1000;

    /// <summary>How many font atlas repacks this session — legacy <see cref="Font"/> events plus
    /// detected TMP atlas-texture changes. Printed with the coincidence counts so "no repack
    /// happened" and "repacks happened and no capture coincided" are different sentences.</summary>
    private static int _fontRebuilds;

    /// <summary>Name of the font whose atlas was re-packed last, for the report.</summary>
    private static string _fontRebuildName = string.Empty;

    /// <summary>Frame number of the last <see cref="Canvas.willRenderCanvases"/>, i.e. the last frame
    /// uGUI actually ran its layout + graphic rebuild queue. A capture taken on a frame where this is
    /// NOT the current frame ran BEFORE the canvas finished rebuilding — failure mode (b).</summary>
    private static int _canvasUpdateFrame = -1000;

    /// <summary>Atlas fingerprints of every <see cref="TMPro.TMP_FontAsset"/> this class has seen, so
    /// a TMP repack (which does NOT raise <see cref="Font.textureRebuilt"/>) is still detectable.
    /// Keyed by font asset instance id; the value is (atlasTextureCount, atlasTexture instance id).</summary>
    private static readonly Dictionary<int, (int Count, int TextureId)> TmpAtlasSeen = new(8);

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

        /// <summary>THIS PANEL'S PRIVATE CAPTURE LAYER, taken from the pool at engage and handed back
        /// on stand-down. -1 = none held. It is the entry's whole isolation guarantee: the camera's
        /// culling mask is <c>1 &lt;&lt; Layer</c> and nothing else in the scene is ever on it. See
        /// PanelSupersample.5.Isolation.cs for why a shared layer was the ModBuild 193 defect.</summary>
        internal int Layer = -1;

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

        /// <summary>Render-target texels per AUTHORED window pixel this entry was allocated at — the
        /// value <see cref="ResolveFactor"/> returned, i.e. AFTER the
        /// <see cref="BandLimitFactor"/> floor. What the target actually ACHIEVED can still be lower
        /// if <see cref="MaxRtDimension"/> clipped an axis; that is <see cref="AchievedFactor"/>.</summary>
        internal float Factor;

        /// <summary>The raw <c>[WorldUI] PanelSupersampleFactor</c> value, before the floor. Printed
        /// next to <see cref="Factor"/> so the log never has to be guessed at.</summary>
        internal float ConfigFactor;

        /// <summary>True when <see cref="BandLimitFactor"/> raised an untouched DEFAULT config value;
        /// false when the user's own tuned value was taken verbatim. See <see cref="ResolveFactor"/>.</summary>
        internal bool FactorFloored;

        /// <summary>
        /// THE FACTOR THE RENDER TARGET ACTUALLY GOT, per axis, as
        /// <c>min(RtW / Frame.width, RtH / Frame.height)</c>. It differs from <see cref="Factor"/>
        /// only when <see cref="MaxRtDimension"/> clipped an axis (a capture frame wider than
        /// 4096 / factor), and then the whole ModBuild 198 argument is silently only partly in force
        /// on this window. It is printed with <see cref="Factor"/> and the ceiling on one line so
        /// "asked 2.00, achieved 2.00" and "asked 2.00, achieved 1.31 (clipped by the 4096 px
        /// ceiling)" can never look alike.
        /// </summary>
        internal float AchievedFactor;

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

        // ---- the content-scale census (ModBuild 201) --------------------------------------------

        /// <summary>
        /// <b>THE SMALLEST SCALE AT WHICH THIS WINDOW DRAWS A SUBSTANTIAL PIECE OF CONTENT, relative
        /// to its own host. THIS IS THE ANSWER TO "WHAT DO THOSE TWO VIEWS DO DIFFERENTLY".</b>
        ///
        /// <para>The map room's character window hosts six sub-views. The ModBuild 200 log's own
        /// FIXED FIT lines give each one's scale: the perks view and the mercenary selector are
        /// written to <c>0.496</c> and <c>0.487</c> (<i>"the open sub-view group needs 1648x1080 px at
        /// scale 1 and fits a 803 px slot ... at scale 0.487 — SCALED TO FIT the slot"</i>); the other
        /// four sit at <c>1.000</c>. The user reports exactly those two as broken and exactly the
        /// other four as fine. The correlation is complete.</para>
        ///
        /// <para><b>AND THE CAPTURE PATH TURNS THAT SCALE STRAIGHT INTO A SAMPLING RATE.</b> The
        /// render target is sized from the HOST frame: <c>Rt = Frame.size * Factor</c>. So every
        /// authored pixel of an UNSCALED sub-view gets <see cref="BandLimitFactor"/> = 2.00 texels,
        /// and every authored pixel of the 0.487 sub-view gets <c>2.00 x 0.487 = 0.97</c>. <b>Those
        /// two sub-views are the only content in this mod that is captured at ONE texel per authored
        /// pixel — which is exactly the un-supersampled state that the ModBuild 198 band-limit floor
        /// exists to abolish, and exactly the state the whole shimmer complaint came from.</b> At that
        /// rate there is no headroom above Nyquist at all: a hairline or a glyph stem either lands on
        /// a texel centre or falls between two, and nothing downstream — not the mip chain, not
        /// anisotropy, not the eye — can put back a stroke the rasteriser never wrote. Combine it with
        /// a capture frame whose phase moves (<see cref="FrameQuantumPx"/>) and the picture re-rolls
        /// on every measurement: <i>perks.jpg</i>, a view that is almost entirely blank with a few
        /// surviving glyph fragments.</para>
        ///
        /// <para><b>WHAT THIS FIELD IS USED FOR, AND THE HONEST LIMIT OF IT.</b>
        /// <see cref="EffectiveFactor"/> asks for <c>BandLimitFactor / MinContentScale</c> so that the
        /// SCALED subtree, not the host, is what gets 2.00 texels per authored pixel. On the character
        /// window that is an ask of 4.11 against a 1715x1107 capture frame — 7049 px wide, which
        /// <see cref="MaxRtDimension"/> (4096) and <see cref="MaxPanelVramBytes"/> (160 MB at
        /// 13.3 bytes per texel, i.e. ~12.6 Mtexel) both refuse. The reachable rate is about 2.4–2.6,
        /// which lifts that sub-view from 0.97 to roughly 1.2 texels per authored pixel. That is a
        /// real improvement and it is NOT the band limit, so the report prints the achieved
        /// per-subtree rate as its own number rather than letting the host's 2.00 stand in for it.
        /// <b>Within this path's budget, a 0.487-scaled subtree CANNOT be given its band limit back.
        /// The only complete cure is for the sub-view not to be scaled at all.</b></para>
        /// </summary>
        internal float MinContentScale = 1f;

        /// <summary>Host-local area (uGUI px²) of the graphic that produced
        /// <see cref="MinContentScale"/>, and its name — so a suspicious reading can be traced to an
        /// object instead of argued about.</summary>
        internal float MinContentScaleArea;
        internal string MinContentScaleName = string.Empty;

        /// <summary>How many graphics the last frame measurement was able to read a scale from. A
        /// zero here means the census did not run, which must never read as "nothing is scaled".</summary>
        internal int ContentScaleSamples;

        /// <summary>Measurements that produced no scale sample at all. Kept so an unmeasured window
        /// and an unscaled one print differently.</summary>
        internal int ContentScaleUnmeasured;

        /// <summary>How many graphics the last measurement found below
        /// <see cref="ScaledContentThreshold"/>, and the largest host-local area among them.</summary>
        internal int ScaledGraphics;
        internal float ScaledGraphicsArea;

        /// <summary>THE WHOLE DISTRIBUTION of <see cref="MinContentScale"/> since engage, because the
        /// last reading is not the operating point and a summary field quoted as one has already cost
        /// this project a round. Printed as LOWEST / MEAN / HIGHEST with the count.</summary>
        internal int ContentScaleReadings;
        internal float ContentScaleSum;
        internal float MinContentScaleLowest = 1f;
        internal float MinContentScaleHighest = 1f;

        /// <summary>
        /// THE RATE THE RENDER TARGET IS ACTUALLY SIZED FROM — <see cref="Factor"/> raised by
        /// <c>1 / MinContentScale</c> and then cut down by <see cref="MaxRtDimension"/>, by the VRAM
        /// budget and by <see cref="RateQuantum"/>. Equal to <see cref="Factor"/> on every window
        /// whose content is unscaled, which is every window but one.
        /// </summary>
        internal float EffectiveFactor;

        /// <summary>What <see cref="EffectiveFactor"/> WOULD have been if nothing had cut it down.
        /// Printed next to it so a request that was refused never reads like one that was granted.</summary>
        internal float AskedFactor;

        /// <summary>The <see cref="MinContentScale"/> the current render target was sized for. A
        /// sub-view switch changes the content scale without touching the host rect or the capture
        /// frame, so this — not the frame — is what notices that the allocation is now for the wrong
        /// content.</summary>
        internal float ScaleAtAllocation = 1f;

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

        // ---- MOTION INSTRUMENT SELF-CHECK (ModBuild 194) ---------------------------------------
        // A zero in MotionFrames used to be ambiguous: it reads the same whether the window was
        // genuinely still, whether the detector never ran, or whether its epsilon was so large that
        // a real drag fell under it. The three fields below make those cases DISTINGUISHABLE from
        // the log alone, which is the only thing that stops a future round re-litigating whether
        // this instrument works. (For the record, and read from the ModBuild 193 hardware log: it
        // DOES — 23 of that log's 226 state lines carry a non-zero motion count, the largest being
        // 542 frames of a 900-frame report window, and several read "currently MOVING".)

        /// <summary>How many frames <see cref="NoticeGeometry"/> actually COMPARED a pose in this
        /// report window. "0 motion frames out of 0 comparisons" is a detector that never ran;
        /// "0 out of 900" is a window that genuinely did not move.</summary>
        internal int MotionTicks;

        /// <summary>The LARGEST single-frame world-space host translation seen in this report window,
        /// in world units. Reported next to the epsilon it was tested against, so a detector blinded
        /// by a wrong epsilon shows up as a large delta with a zero count.</summary>
        internal float MaxStepWorld;

        /// <summary>The epsilon the last comparison used, in world units. Derived from the panel's
        /// own lossy scale (half an authored uGUI pixel), never in metres — the map room runs ~198
        /// world units per real metre, so a fixed metric epsilon would be blind or always tripped.</summary>
        internal float MotionEpsWorld;

        /// <summary>The host's world units per authored uGUI pixel at the last comparison, so the
        /// two numbers above can be read in authored pixels as well as in world units.</summary>
        internal float WorldPerAuthoredPx;

        /// <summary>Authored uGUI pixels per RENDERED eye pixel, as
        /// <see cref="SamplingSentence"/> last measured it. 0 = not measured this report. It exists
        /// so the largest drag step can also be quoted in RENDERED pixels, which is the unit that
        /// decides what a drag looks like: a window sliding 8 rendered px per frame is a
        /// strobing/judder problem (one frame of pose lag is 8 px of error), while one sliding 0.4
        /// rendered px per frame can only be a sub-pixel SAMPLING problem. Those two want opposite
        /// fixes and the log could not tell them apart before ModBuild 194.</summary>
        internal float AuthoredPerRenderedPx;

        // ---- THE RESAMPLE INSTRUMENT (ModBuild 198) --------------------------------------------
        // The one thing three rounds of capture-side counters could not see: what the EYE does with
        // the finished texture. Every field below is written by SamplingSentence on the report
        // cadence AND re-read on every release, and every one of them is printed next to the
        // comparison it is judged against — see BandLimitFactor for the physics and AppendResample
        // for the sentence.

        /// <summary>How many times <see cref="SamplingSentence"/> COMPLETED a measurement in this
        /// report window, and how many times it bailed out and why. Without these a session in which
        /// the head camera was never found and a session in which everything measured clean print
        /// the same absence.</summary>
        internal int SamplingMeasured;
        internal int SamplingNoHead;
        internal int SamplingNoEyeTarget;
        internal int SamplingOffScreen;

        /// <summary>RT texels per RENDERED eye pixel at the last completed measurement — the number
        /// the whole ModBuild 198 argument turns on. Compared against
        /// <see cref="BandLimitedTexelsPerPixel"/> (2.0), the rate at which the eye stops reading
        /// unfiltered mip level 0 at all.</summary>
        internal float TexelsPerRenderedPx;

        /// <summary>The LOWEST <see cref="TexelsPerRenderedPx"/> seen since engage — the most
        /// MAGNIFIED sample, i.e. the window held closest to the face.
        /// <para><b>ModBuild 199 PRINTED THIS ALONE AND CALLED IT "WORST", AND IT MISLED THE READ OF
        /// THE WHOLE BUILD.</b> Lowest is the worst case for ALIASING, which is what ModBuild 198 was
        /// hunting, and it is the BEST case for LEGIBILITY, which is what the user reports now. The
        /// 199 hardware log's 97 completed measurements run 0.95 to 3.69 with a MEDIAN of 2.50, and
        /// only 7 of the 97 sit near 1.0 — all of them windows pulled up to the face mid-drag, several
        /// projecting larger than the entire 3072x3264 eye target. Quoting 0.95 as the operating point
        /// turned a 7-sample tail into a conclusion ("factor 2 traded aliasing for blur") that the
        /// other 90 samples contradict. Both ends and the mean are now printed together, over the
        /// comparison count, so no single number can stand in for the distribution again — this
        /// project's "read the whole distribution" rule, applied to the instrument itself.</para></summary>
        internal float TexelsPerRenderedPxWorst;

        /// <summary>The HIGHEST <see cref="TexelsPerRenderedPx"/> seen since engage — the most
        /// MINIFIED sample, and therefore the LEAST LEGIBLE one. This is the end of the distribution
        /// the "man kann den Text kaum lesen" report lives at, and ModBuild 199 did not record it at
        /// all.</summary>
        internal float TexelsPerRenderedPxMax;

        /// <summary>Running sum of <see cref="TexelsPerRenderedPx"/> over
        /// <see cref="SamplingMeasured"/>, so the line can print a MEAN next to the two extremes.
        /// Neither extreme is the operating point on its own.</summary>
        internal double TexelsPerRenderedPxSum;

        /// <summary>Completed measurements on which the window was MINIFIED
        /// (<see cref="AuthoredPerRenderedPx"/> &gt; 1), i.e. the eye was given FEWER pixels than the
        /// window has authored pixels. Printed over <see cref="SamplingMeasured"/>: it is the single
        /// number that separates a LEGIBILITY complaint from an ALIASING one, and no capture-side
        /// factor can move it.</summary>
        internal int MinifiedReadings;

        /// <summary>The most authored pixels ever squeezed into one rendered eye pixel since engage —
        /// the peak minification, and the direct measure of how much of the window's authored
        /// resolution the eye never receives.</summary>
        internal float AuthoredPerRenderedPxMax;

        /// <summary>Weight the hardware's trilinear filter gives to UNFILTERED mip level 0 at
        /// <see cref="TexelsPerRenderedPx"/>, 0..1. This is the aliasing that crawls under motion and
        /// freezes on release. A LOWER BOUND, not an exact figure: anisotropic filtering selects the
        /// LOD from the MINOR axis rate and therefore raises this on any yawed window.</summary>
        internal float Level0Weight;

        /// <summary>Completed measurements in this report window on which the eye read ANY unfiltered
        /// level 0 (<see cref="Level0Weight"/> above zero) while the window was MINIFIED, i.e. frames
        /// on which the ModBuild 198 defect was live. Printed over
        /// <see cref="SamplingMeasured"/>.</summary>
        internal int Level0Readings;

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

        /// <summary>Transforms the sweep left alone because they were already on ANOTHER live
        /// entry's private capture layer. The expected value is ZERO — converted panels' subtrees
        /// are disjoint — and a non-zero value is a real finding: it means two panels share a
        /// transform, which without this guard would be a per-frame layer write war between them.</summary>
        internal int ForeignLayerSkipped;
        internal int NestedTotal;
        internal int NestedCaptured;
        internal bool MipWarned;

        // ---- CONTENT INTEGRITY: the three states the release report must separate ---------------
        // The user, still open after ModBuild 195: "beim Loslassen kann es passieren, dass die
        // dargestellte Anzeige kaputt ist ... bewege ich es nochmal und lasse los, sieht es wieder
        // anders aus". The photograph (.planning/debug/kaputte_anzeige.jpg) shows PER-GLYPH dropout
        // with the layout intact: 'Gesundheit:' renders as "Ge n i", 'Verbesserungen:' as "erb s e
        // :", while the SAME window's smaller quest line renders every character. Measured off that
        // image rather than assumed: the surviving glyphs sit exactly where a full-width layout puts
        // them (the trailing ':' of 'Verstärkungen:' and of 'Verbesserungen:' are 12 px apart for one
        // extra character, i.e. NOTHING was substituted or removed), the gaps read background
        // luminance (max 24 against a 20 background and a 147 ink), and the gaps do NOT line up into
        // vertical stripes across the six rows. So: the character ADVANCES are correct and the glyph
        // QUADS put no pixels down. These counters exist to decide WHY, from the log alone.

        /// <summary>How many times the capture ran while this entry existed — the denominator for
        /// every coincidence count below. Zero here means the instrument never ran, which is a
        /// different statement from "it ran and found nothing".</summary>
        internal int CaptureTicks;

        /// <summary>(a) Captures taken on the same frame as a font atlas repack. A non-zero value is
        /// the ONLY thing that makes "the capture caught the atlas mid-flight" a live explanation.</summary>
        internal int CapturesDuringFontRebuild;

        /// <summary>(b) Captures taken on a frame in which <see cref="Canvas.willRenderCanvases"/> had
        /// NOT yet fired, i.e. before uGUI rebuilt the layout and the graphics for that frame.
        /// EXPECTED ZERO — the capture camera renders inside the camera loop, which Unity runs after
        /// <c>PostLateUpdate.PlayerUpdateCanvases</c>. A non-zero value overturns that reading.</summary>
        internal int CapturesBeforeCanvasUpdate;

        /// <summary>Content-integrity scans made (<see cref="MeasureContent"/>). The comparison count
        /// that makes every number below readable.</summary>
        internal int ContentScans;
        internal double ContentScanMs;

        /// <summary>Content scans that THREW. Counted so a scan that failed can never be read as a
        /// scan that came back clean — the same rule as every other instrument in this file.</summary>
        internal int ContentScanFailures;
        internal bool ContentScanFailWarned;

        /// <summary>Text components found by the last scan, and the glyph lookups it made.</summary>
        internal int TextComponents;
        internal int GlyphsChecked;

        /// <summary>(c-1) Characters the text asks for that its font asset does NOT have, fallbacks
        /// included. This is the direct test of the dynamic-font-atlas hypothesis: non-zero means the
        /// atlas genuinely cannot serve the string and no amount of re-capturing will help.</summary>
        internal int GlyphsNotInAtlas;

        /// <summary>(c-2) Non-whitespace characters the text layout marked NOT VISIBLE — the text
        /// engine itself decided not to draw them (overflow truncation, a missing-glyph replacement,
        /// a maxVisibleCharacters clamp).</summary>
        internal int GlyphsNotVisible;

        /// <summary>(c-3) Non-whitespace characters that ARE visible but whose generated quad has
        /// (near) zero area, i.e. they hold their advance and put no pixels down. THAT IS THE EXACT
        /// SHAPE OF THE PHOTOGRAPH, so this is the counter that confirms or kills it.</summary>
        internal int GlyphsBlankQuad;

        /// <summary>The worst single text component of the last scan, so the log names a suspect
        /// instead of only a total.</summary>
        internal string WorstText = string.Empty;
        internal int WorstTextBad;
        internal int WorstTextChecked;

        /// <summary>(c-4) Text components uGUI/TMP has switched OFF at the renderer: the
        /// <see cref="CanvasRenderer"/> reports <c>cull</c>, or its effective alpha is zero. Those put
        /// no pixels into the CAPTURE at all, for reasons that have nothing to do with glyphs.
        ///
        /// <para><b>THIS COUNTER EXISTS TO KEEP TWO INDISTINGUISHABLE-LOOKING FAULTS APART.</b> "An
        /// element is missing from the capture" and "an element IS in the capture and something is
        /// painted over it" look identical to a player and can look identical to a probe that only
        /// asks whether an object was captured. Everything else in this scan measures the TEXT SOURCE
        /// — whether the glyph exists, whether the layout marked it visible, whether its quad has
        /// area — which is upstream of BOTH. This one field is the only one that speaks to whether
        /// the component draws at all. Neither this scan nor any other field here can see a graphic
        /// that drew correctly and was then painted over: that question is answered by the draw order,
        /// i.e. by the ISOLATION field's sortingOrder and by CanvasConversion's own order ladder, not
        /// here. The report says so in as many words so a future round cannot read a clean scan as
        /// "nothing is covering anything".</para></summary>
        internal int TextCulled;

        // ---- THE TMP SUB-MESH FAMILY (ModBuild 200) --------------------------------------------
        // WHAT THE CONTENT SCAN HAS NEVER LOOKED AT, and the reason four builds of clean readings sit
        // next to a photograph of dozens of missing glyphs.
        //
        // A TextMeshProUGUI component does NOT draw all of its own glyphs. Whenever a string needs a
        // SECOND ATLAS PAGE, a FALLBACK FONT or an INLINE SPRITE, TMP splits the run across extra
        // materials and hands those quads to TMP_SubMeshUI components on CHILD GameObjects — each
        // with its own CanvasRenderer, its own material, its own texture, its own GameObject layer
        // and its own active state. textInfo.meshInfo[1..] holds their VERTEX DATA, which is what
        // ScanTmpMesh reads and finds clean; NoteRendererState then asks the PARENT's CanvasRenderer
        // whether it was drawn and never asks theirs. And MeasureContentCore's walk classifies a
        // TMP_SubMeshUI as "a Graphic that is neither TMP_Text nor Text" and skips it outright.
        //
        // So the instrument's answer to "were these quads drawn?" is a measurement of a DIFFERENT
        // OBJECT than the one that draws them. That is exactly the shape this project keeps paying
        // for: a flawless measurement of the wrong stage looks like proof. The 199 log reads 0
        // SUBMITTED-MESH defects out of 3,471 quads on the very window whose photograph shows the
        // stat block rendering "c l e h Ba el n z ari b." where whole words belong — and the glyphs
        // that DO survive are crisp and correctly positioned, which is what a per-material split
        // looks like and is NOT what any content defect looks like.
        //
        // The capture path adds one failure mode of its own on top: this panel's camera culls by its
        // PRIVATE LAYER, so a sub-mesh child born between two layer sweeps is on the game's UI layer
        // and is INVISIBLE TO THE CAPTURE while its parent is captured normally. That is repaired
        // here unconditionally rather than reported, because a remedy gated behind its own diagnostic
        // is the other mistake this lane has already made.

        /// <summary>TMP sub-mesh child components the last scan visited — the denominator for every
        /// count below. Zero means the window's text needs no second material at all, and that is a
        /// different statement from "they were all fine".</summary>
        internal int SubMeshesSeen;

        /// <summary>
        /// <b>POOLED SUB-MESHES — the denominator ModBuild 200 was missing, and without it that
        /// build's own numbers could not be read.</b> TMP creates one sub-mesh child per material
        /// reference a string has EVER needed and leaves the surplus in place with an EMPTIED mesh. A
        /// pooled sub-mesh is legitimately culled, legitimately transparent and legitimately without a
        /// bound texture — its normal life looks exactly like the abuse every counter below is
        /// hunting. The ModBuild 200 log read <c>19 TMP SUB-MESH(ES) ... of which 17
        /// culled/transparent</c> on the character window and <c>0 of 0</c> or <c>0 of 1</c> on every
        /// other window in the session; that gap is NOT evidence, because the character window is
        /// simply the only one with enough text to pool any. From ModBuild 201 the counters below are
        /// scoped to <see cref="SubMeshesInUse"/> and this holds the rest.
        /// </summary>
        internal int SubMeshesEmpty;

        /// <summary>Sub-meshes whose mesh actually carries vertices — glyphs the text engine handed
        /// over and that CAN therefore be missing from the picture. The real denominator for every
        /// count below.</summary>
        internal int SubMeshesInUse;

        /// <summary>Sub-meshes whose GameObject was INACTIVE, so their quads drew nothing.</summary>
        internal int SubMeshesInactive;

        /// <summary>Sub-meshes whose CanvasRenderer was culled or fully transparent.</summary>
        internal int SubMeshesCulled;

        /// <summary>Sub-meshes found on a layer OTHER than this panel's private capture layer — i.e.
        /// present in the mesh, present in the eye, MISSING FROM THE CAPTURE. Repaired on the spot;
        /// this is the count of how many needed it.</summary>
        internal int SubMeshesWrongLayer;

        /// <summary>Sub-meshes with no material or no main texture bound — their quads sample
        /// nothing.</summary>
        internal int SubMeshesNoTexture;

        /// <summary>The worst sub-mesh found, named, for the report.</summary>
        internal string SubMeshWorst = string.Empty;

        /// <summary>Text components the last scan found completely clean. Printed as "K of M", so a
        /// scan that found nothing is visibly a scan that CHECKED something.</summary>
        internal int TextClean;

        /// <summary>The scan hit <see cref="MaxGlyphChecksPerScan"/> — it is a LOWER BOUND, not a
        /// total, and the report says so rather than letting a truncated scan read as a clean one.</summary>
        internal bool ContentScanTruncated;

        /// <summary>What the font assets behind this window are, measured not assumed: population
        /// mode, atlas size, atlas count. A STATIC atlas cannot repack, which kills the whole
        /// hypothesis for that window by construction.</summary>
        internal string AtlasNote = string.Empty;

        // ---- the repairs ------------------------------------------------------------------------

        /// <summary>Release repairs run (forced re-measure + forced sweep + forced text
        /// regeneration on the frame the window comes to rest).</summary>
        internal int ReleaseRepairs;

        /// <summary>Text regenerations forced by a font atlas repack, as opposed to by a release.</summary>
        internal int RebuildRepairs;

        /// <summary>Text components whose glyphs were re-requested and whose mesh was re-generated by
        /// the last repair, and how many characters were re-requested.</summary>
        internal int RegeneratedComponents;
        internal int RegeneratedChars;

        /// <summary>The <see cref="LastMotionFrame"/> the release repair has already been run for, so
        /// one release costs exactly two repairs and never one per frame.</summary>
        internal int ReleaseRepairedMotionFrame = -1000;
        internal int ReleaseRepairStage;

        /// <summary>Text components the release repair actually re-generated, and what that cost in
        /// milliseconds. ModBuild 196 reported the regeneration as if it always ran; it did not (see
        /// <see cref="ReleaseRepair"/>), so from ModBuild 197 the count and the cost are both
        /// measured and both printed.</summary>
        internal int ReleaseRegenComponents;
        internal double ReleaseRegenMs;

        // ---- THE RELEASE EDGE ITSELF (ModBuild 197) ---------------------------------------------
        // The user, after 196: "das Flackerproblem WÄHREND DER BEWEGUNG ist noch da — inklusive der
        // möglichen kaputten Darstellung, wenn man nach der Bewegung ABRUPT loslässt". ABRUPT is a
        // new word and it is a measurement request: the broken image correlates with letting go AT
        // SPEED, not with letting go. Nothing in ModBuild 196 measured the release EVENT — only the
        // aggregate number of repairs — so "the gate opened late", "the gate never opened" and "the
        // gate opened on time and the repair found nothing" all printed the same character. These
        // fields are per DRAG, reset when a new drag begins, and are printed at the release edge.

        /// <summary>Frame the current (or last) drag began, i.e. the first motion frame after a
        /// stretch of stillness. -1000 = no drag seen yet.</summary>
        internal int DragStartFrame = -1000;

        /// <summary>Frames in the current/last drag that carried a pose, scale or rect change.</summary>
        internal int DragMotionFrames;

        /// <summary>Largest single-frame world-space host step inside the current/last drag.</summary>
        internal float DragMaxStepWorld;

        /// <summary>The LAST single-frame world step before the hand let go — the "abrupt" number.
        /// A release at speed has a large value here; a release from a standstill has ~0. Read next
        /// to <see cref="DragMaxStepWorld"/>: the two being equal means the drag was still
        /// accelerating when it ended.</summary>
        internal float DragLastStepWorld;

        /// <summary>Frames inside the current/last drag whose unscaled duration exceeded
        /// <see cref="DroppedFrameMs"/> — i.e. frames on which the headset showed the previous image
        /// again while the window was travelling. The judder counter.</summary>
        internal int DragDroppedFrames;

        /// <summary>Worst unscaled frame time inside the current/last drag, ms.</summary>
        internal float DragWorstFrameMs;

        /// <summary>Releases whose settle gate opened in this report window, and the largest number
        /// of frames any of them took from the last motion frame to the gate. A gate that never opens
        /// (the "stillness gate versus an external writer" failure) is a zero here in a window whose
        /// MOTION count is non-zero — those two used to be unrelatable.</summary>
        internal int ReleasesThisWindow;
        internal int ReleaseGateFramesMax;

        /// <summary>Longest unbroken run of motion frames in this report window. If a window's pose
        /// is rewritten every frame by somebody else, this equals the whole window and no release can
        /// ever be detected — which is the shape the memory "a settle gate never opens for state
        /// someone else rewrites each frame" describes.</summary>
        internal int LongestMotionRun;
        internal int CurrentMotionRun;

        // ---- WHAT PATH A DRAGGED FRAME ACTUALLY TAKES (ModBuild 197) -----------------------------
        // The honest question for the moving flicker is whether a dragged window is supersampled AT
        // ALL while it moves, or whether something short-circuits to the raw canvas. Before this
        // build the log carried ONE capture count and one resolve was implied by it; neither was
        // split by what the window was doing, so the question could only be answered by reading the
        // source. These four pairs answer it with counts over a drag.

        /// <summary>Captures taken while the window was inside its motion window / at rest.</summary>
        internal int MotionCaptures;
        internal int StillCaptures;

        /// <summary>Resolve+mip passes that actually ran while moving / at rest. A capture without a
        /// matching resolve means the quad showed the PREVIOUS frame's resolved image.</summary>
        internal int MotionResolves;
        internal int StillResolves;

        /// <summary>Frames on which the quad was showing the RAW multisampled capture target instead
        /// of the resolved, mipped one (the <see cref="MipFallback"/> path). Expected 0.</summary>
        internal int RawQuadFrames;

        /// <summary>Frames on which the panel was visible and the capture camera was NOT enabled, so
        /// no capture was taken at all for a frame the eye then drew the quad on. Expected 0.</summary>
        internal int CameraOffWhileVisible;

        /// <summary>Resolves whose source and destination targets disagreed in size, i.e. the target
        /// was re-allocated between the capture and the resolve. Expected 0; a non-zero value is the
        /// "the capture, not the content, is what breaks" finding.</summary>
        internal int ResolveSizeMismatch;

        // ---- THE SUBMITTED MESH, WHICH ModBuild 196 NEVER LOOKED AT ------------------------------
        // ModBuild 196's scan reads TMP_CharacterInfo — the LAYOUT record written while the text is
        // being laid out. The photograph's fault (full advances, correct quad geometry, no ink) is
        // downstream of that: it lives in the vertex/UV arrays that were actually written into the
        // mesh and uploaded. A glyph whose four UVs collapsed to a point samples ONE atlas texel and
        // puts down a flat SDF value, i.e. NOTHING, while every counter 196 owned still reads clean.
        // That is why 236 readings of a working instrument found nothing.

        /// <summary>Glyph quads whose MESH data was examined — the denominator for the four counters
        /// below, and the number that separates "the mesh instrument never ran" from "it ran".</summary>
        internal int MeshQuadsChecked;

        /// <summary>Visible characters whose layout says they were laid out but whose vertex index is
        /// past the end of the mesh that was actually generated: the quad was never written.</summary>
        internal int MeshMissingQuads;

        /// <summary>Visible characters whose MESH quad (not the layout record) has ~zero area.</summary>
        internal int MeshDegenerateQuads;

        /// <summary>Visible characters whose ATLAS UV rectangle has ~zero area (threshold
        /// <see cref="DegenerateUvArea"/>): the quad samples a single atlas point and draws a flat
        /// value, which is ink-free at any resolution. THE PHOTOGRAPH'S EXACT SHAPE.</summary>
        internal int MeshDegenerateUv;

        /// <summary>Visible characters whose atlas UVs fall outside [0,1] — a stale UV against a
        /// re-packed or replaced atlas. Also ink-free, for a different reason.</summary>
        internal int MeshUvOutOfRange;

        /// <summary>Visible characters with a non-finite vertex or UV.</summary>
        internal int MeshNonFinite;

        /// <summary>Worst single component of the last mesh scan, so the log names a suspect.</summary>
        internal string MeshWorst = string.Empty;
        internal int MeshWorstBad;

        /// <summary>Frame at (or after) which a font-atlas repack repair is due. -1 = none armed.</summary>
        internal int RebuildRepairFrame = -1;

        // ---- MOTION BUDGET: what this class costs during a drag, split from what it costs at rest -
        // ModBuild 193's movement remedy (sweep every frame while moving) is FALSIFIED as a fix and
        // the ModBuild 195 log priced it: 22 of the 23 sweeps that actually found a late transform
        // ran on the PERIODIC cadence and exactly one on the per-frame motion cadence, while the
        // sweep itself averaged 1.48-1.94 ms on a frame whose measured p50 is 17.33 ms against an
        // 11.11 ms budget. These fields make that trade visible per report window instead of once.
        internal int MotionSweeps;
        internal double MotionSweepMs;
        internal int StillSweeps;
        internal double StillSweepMs;

        /// <summary>Frames sampled while this window was inside its motion window, the total and worst
        /// unscaled frame time over them, and how many exceeded <see cref="FrameBudgetMs"/>.</summary>
        internal int MotionFrameSamples;
        internal double MotionFrameMs;
        internal float MotionFrameMsMax;
        internal int MotionFramesOverBudget;

        internal int StillFrameSamples;
        internal double StillFrameMs;
        internal float StillFrameMsMax;
        internal int StillFramesOverBudget;
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
            if (PoolSize <= 0)
            {
                if (!_noLayerLogged)
                {
                    _noLayerLogged = true;
                    VRLog.Warn(Scope, "PANEL SUPERSAMPLE stands down: the capture-layer POOL is empty "
                                      + "(every layer 8-31 is named, and layer "
                                      + $"{VRLayers.ModLayer} is already the mod layer), and a panel "
                                      + "without a PRIVATE layer would have to share one with its "
                                      + "neighbours — which is the ModBuild 193 defect in which one "
                                      + "window's capture contained another window's content. THE "
                                      + "CONSEQUENCE: every floated window keeps being rasterized "
                                      + "directly into the eye at ~1.86 authored pixels per rendered "
                                      + "pixel, i.e. exactly today's behaviour including the reported "
                                      + "text and edge shimmer. Nothing else changes. The one-time "
                                      + "POOL census line above lists exactly which layers are named "
                                      + "and by whom.");
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

                bool moving = IsMoving(e);
                SampleFrameBudget(e, moving);

                // THE MOVEMENT CADENCE. While the window is moving (and for SweepAfterMotionFrames
                // after), sweep on the faster MovingSweepIntervalFrames cadence: that is the interval
                // in which the panel's own per-frame nested-canvas adoption is throttled OFF by
                // GrabbableModal.ThrottleDiagWhileMoving, so a child born mid-drag would otherwise
                // stay on the game's UI layer — missing from the capture and double-drawn into the
                // eye. It is a CADENCE and no longer every frame, because the ModBuild 195 log priced
                // per-frame sweeping at 1.5 ms of a 17 ms frame and measured that it caught exactly
                // ONE of the session's 23 real arrivals; see MovingSweepIntervalFrames.
                if (Time.frameCount >= e.NextSweepFrame)
                {
                    e.NextSweepFrame = Time.frameCount
                                       + (moving ? MovingSweepIntervalFrames : SweepIntervalFrames);
                    ApplyCaptureLayer(e, initial: false);
                }

                // THE RELEASE REPAIR and the FONT-REPACK REPAIR — both unconditional, both bounded,
                // and both aimed squarely at "die dargestellte Anzeige ist kaputt". See ReleaseRepair.
                ServiceRepairs(e);

                if (report)
                {
                    // Scan on the report cadence as well as on every release, so the CONTENT
                    // INTEGRITY field is never a stale reading from the last time the window was
                    // touched. The window in the user's photograph had been STANDING STILL when the
                    // pause menu opened next to it, so "it only moved ten minutes ago" must not mean
                    // "measured ten minutes ago". Once per 10 s per panel, ~1 ms.
                    MeasureContent(e);
                    Report(e);
                }
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
        int cap = EffectiveMaxPanels;
        if (Entries.Count >= cap)
            return;
        IReadOnlyList<ConvertedPanel> live = CanvasConversion.ActivePanels;
        for (int i = 0; i < live.Count && Entries.Count < cap; i++)
        {
            ConvertedPanel panel = live[i];
            if (!Eligible(panel) || OwnsPanelLayers(panel)
                || Refused.Contains(panel.HostGo.GetInstanceID()))
                continue;
            Engage(panel);
        }
        if (Entries.Count >= cap && !_capLogged)
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
                string capSource = PoolSize < MaxPanels
                    ? $"the LAYER POOL ({PoolSize} private capture layer(s) exist; MaxPanels is "
                      + $"{MaxPanels}). A ninth window cannot be given a tenth layer, and it must "
                      + "never SHARE one — sharing is the ModBuild 193 defect in which one window's "
                      + "capture contained its neighbour's content. The only honest lever here is to "
                      + "free a named layer, not to raise MaxPanels."
                    : $"MaxPanels ({MaxPanels}); the layer pool holds {PoolSize} and is not the bound.";
                VRLog.Warn(Scope, $"PANEL SUPERSAMPLE cap reached: {cap} panel(s) are "
                                  + $"supersampled and {candidates} further eligible window(s) are "
                                  + "NOT. THE CAP THAT BOUND IS " + capSource
                                  + " THE CONSEQUENCE for those windows: they keep today's direct "
                                  + "rendering — the same text and edge shimmer as before this build. "
                                  + "HOW TO SIZE THE NEXT VALUE from this line rather than from an "
                                  + $"estimate: the {Entries.Count} panel(s) actually engaged cost "
                                  + $"{Mb(_vramTotal)} MB together, i.e. {Mb(average)} MB each on "
                                  + $"average, against a session ceiling of {Mb(MaxTotalVramBytes)} MB "
                                  + $"and a per-panel ceiling of {Mb(MaxPanelVramBytes)} MB — so "
                                  + $"{(average > 0 ? (MaxTotalVramBytes / average).ToString() : "many")} "
                                  + "panel(s) of THIS size fit in the session budget. If that number "
                                  + "is comfortably above the cap above, the VRAM ceiling is NOT the "
                                  + "bound and the line named the one that is; if it is below, the "
                                  + "VRAM ceiling is the real bound and neither MaxPanels nor the "
                                  + "layer pool is what refused these windows.");
            }
        }
    }

    /// <summary>
    /// The render-target texels per AUTHORED window pixel this panel will be allocated at, and where
    /// that number came from.
    ///
    /// <para>The whole argument is in <see cref="BandLimitFactor"/>. The rule here is narrow: a
    /// <c>[WorldUI] PanelSupersampleFactor</c> the user has actually SET is taken verbatim, because a
    /// tuned value is a ruling and this project does not re-express one; a factor still sitting on
    /// its shipped default is a policy nobody chose, and the ModBuild 197 log proves that policy was
    /// allocating one texel per authored pixel and supersampling nothing. Both numbers reach the
    /// engage line so the log always says which branch ran.</para>
    /// </summary>
    private static float ResolveFactor(out float configured, out bool floored)
    {
        configured = WorldUIConfig.PanelSupersampleFactor != null
            ? WorldUIConfig.PanelSupersampleFactor.Value
            : Defaults.PanelSupersampleFactor;
        // Mathf.Approximately, not ==, because BepInEx round-trips the value through the .cfg text.
        // ModBuild 199: THE FLOOR NO LONGER ASKS WHETHER THE VALUE LOOKS LIKE THE DEFAULT.
        // ModBuild 198 wrote `Mathf.Approximately(configured, Defaults.PanelSupersampleFactor)` here,
        // so that a value the user had tuned would be taken verbatim. I verified that logic against
        // the source and told the user the remedy would run. It did not: all 153 RESAMPLE VERDICT
        // lines of the next hardware log read `config 1.00, taken verbatim — this is a value the user
        // set`, because his cfg holds a hand-written 1.0 that is not bit-equal to the constant. The
        // whole ModBuild 198 experiment silently did not execute — the second time in three builds a
        // remedy shipped behind a condition that was never true.
        // A factor below the band limit is not a preference, it is a no-op: at 1.0 the capture target
        // is the window's own resolution and nothing is band-limited. So the floor is unconditional
        // now, and it SAYS SO ON EVERY REPORT LINE (see FactorFloored) naming the value it overrode,
        // rather than being a silent correction. If a deliberate 1.0 is ever wanted, it needs its own
        // explicit escape — not the accident of matching a constant.
        floored = configured < BandLimitFactor;
        return Mathf.Clamp(floored ? BandLimitFactor : configured, 0.5f, 2f);
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
        float factor = ResolveFactor(out float configured, out bool floored);

        var e = new Entry
        {
            Panel = panel,
            Window = window,
            Factor = factor,
            ConfigFactor = configured,
            FactorFloored = floored,
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

        // THE RATE, THE MSAA STEP-DOWN AND THE FACTOR STEP-DOWN, in one place shared with
        // Reallocate (ModBuild 201 — before it, engaging and re-allocating derived the render-target
        // size by two separate copies of this arithmetic, and only one of them stepped down).
        //
        // The MSAA step-down answers to the SESSION headroom as well as to the per-panel cap. Before
        // ModBuild 193 it only saw the per-panel cap, so a window that could have fitted at MSAA 2x
        // or 1x was refused outright once the session filled — the cap message then blamed MaxPanels
        // for what was really a budget decision. A degraded-MSAA panel still gets the supersampled
        // rasterization AND the mip chain, which is the whole of the fix; MSAA only resolves the RT's
        // own internal edges.
        //
        // THE FACTOR STEP-DOWN (ModBuild 198) exists because ModBuild 198 REMOVED the lever the MSAA
        // loop used to be. With PreferredMsaa at 4 a window that did not fit was degraded 4x -> 2x ->
        // 1x, a 3.4x reduction, and was refused only after that; at PreferredMsaa 1 that loop cannot
        // step at all, so without this a memory-pressured window would go straight from
        // "supersampled" to "refused, keeps today's shimmer" — a graceful degradation silently traded
        // for an outage. Stepping the RATE is the same shape of concession and a better one: a window
        // at 1.5 still has half the fix, and one at 1.0 is exactly ModBuild 197's behaviour rather
        // than nothing at all. What it actually got is on the engage line and in the report's
        // ACHIEVED field, so a degraded window never reads like a full-strength one.
        int msaa = PreferredMsaa;
        int askedMsaa = msaa;
        long budget = System.Math.Min(MaxPanelVramBytes, MaxTotalVramBytes - _vramTotal);
        ResolveRate(e, frame, ref msaa, budget, out factor, out int rtW, out int rtH, out long vram);
        float askedFactor = e.AskedFactor;
        e.EffectiveFactor = factor;
        e.ScaleAtAllocation = e.MinContentScale;
        if (vram > budget)
        {
            Refused.Add(panel.HostGo.GetInstanceID());
            VRLog.Warn(Scope, $"PANEL SUPERSAMPLE refused '{window}': its {rtW}x{rtH} capture + mip "
                              + $"targets would cost {Mb(vram)} MB even at MSAA {msaa}x and factor "
                              + $"{factor:F2} (stepped down from {askedFactor:F2}, floor "
                              + $"{MinStepDownFactor:F2}), against a remaining budget of "
                              + $"{Mb(budget)} MB (per-panel cap {Mb(MaxPanelVramBytes)} MB, session "
                              + $"{Mb(_vramTotal)} of {Mb(MaxTotalVramBytes)} MB across "
                              + $"{Entries.Count} panel(s)). THE CONSEQUENCE: this window keeps "
                              + "today's direct rendering and will still shimmer. BOTH automatic "
                              + "concessions are already exhausted here, so the remaining levers are "
                              + "closing another floated window or lowering [WorldUI] "
                              + "PanelSupersampleFactor below "
                              + $"{MinStepDownFactor:F2} by hand.");
            return;
        }

        // THE PRIVATE CAPTURE LAYER — taken BEFORE anything is allocated, because a panel that
        // cannot get one must not be engaged at all. ModBuild 193 handed every panel the same layer
        // and every capture camera therefore rendered every panel inside its frustum; the whole
        // argument, the photograph that proves it and the three designs that were rejected are in
        // PanelSupersample.5.Isolation.cs.
        int layer = AcquireLayer();
        if (layer < 0)
        {
            Refused.Add(panel.HostGo.GetInstanceID());
            VRLog.Warn(Scope, $"PANEL SUPERSAMPLE refused '{window}': the capture-layer pool is "
                              + $"exhausted ({PoolSize} private layer(s) exist and all are held by "
                              + $"the {Entries.Count} panel(s) already engaged). It is NOT given a "
                              + "shared layer, because a shared layer is exactly what made one "
                              + "window's capture contain another window's content in ModBuild 193 "
                              + "(.planning/debug/window_merge.jpg). THE CONSEQUENCE: this window "
                              + "keeps today's direct rendering — the dial's OFF behaviour, "
                              + "including the shimmer — until another window closes and hands its "
                              + "layer back, at which point this one is re-considered automatically.");
            return;
        }
        e.Layer = layer;

        RenderTexture? rt = CreateRt(rtW, rtH, ref msaa, window);
        if (rt == null)
        {
            ReleaseLayer(e.Layer);
            e.Layer = -1;
            Refused.Add(panel.HostGo.GetInstanceID());
            return;
        }

        e.Rt = rt;
        e.RtW = rtW;
        e.RtH = rtH;
        e.Msaa = msaa;
        e.VramBytes = VramBytesFor(rtW, rtH, msaa);
        RecordAchievedFactor(e);
        AttachMipTarget(e, rtW, rtH);

        if (!BuildCamera(e, panel) || !BuildDisplay(e, panel))
        {
            DestroyEntryObjects(e);
            ReleaseLayer(e.Layer);
            e.Layer = -1;
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
        // THE FACTOR DECISION, stated with both numbers (ModBuild 198). A build that "ships factor 2"
        // and a session running at 1.0 because the user tuned it down must never read alike, and a
        // target the 4096 px ceiling clipped must not be reported as the factor it asked for.
        string factorNote = " FACTOR: "
            + (e.FactorFloored
                ? $"{e.ConfigFactor:F2} RT texels per authored px is still the SHIPPED DEFAULT, so it "
                  + $"was raised to the {BandLimitFactor:F2} band-limit floor"
                : $"{e.ConfigFactor:F2} RT texels per authored px is a value the USER SET, so it was "
                  + "taken verbatim and the band-limit floor did not apply")
            + (e.MinContentScale < ScaledContentThreshold
                ? $", then RAISED to {askedFactor:F2} because this window's smallest substantial "
                  + $"content is drawn at {e.MinContentScale:F3} of host scale "
                  + $"('{e.MinContentScaleName}', {e.MinContentScaleArea:F0} uGUI px²), which would "
                  + $"otherwise leave that subtree at {(e.Factor * e.MinContentScale):F2} texels per "
                  + "ITS OWN authored px — see Entry.MinContentScale"
                : string.Empty)
            + (factor < askedFactor - 1e-3f
                ? $", then CUT DOWN {askedFactor:F2} -> {factor:F2} by the {MaxRtDimension} px "
                  + $"per-axis ceiling and the {Mb(budget)} MB left for this panel (floor "
                  + $"{MinStepDownFactor:F2} = ModBuild 197's behaviour); below 2.00 the eye still "
                  + "reads some unfiltered mip level 0, so the carried-window flicker is only partly "
                  + "removed on THIS window"
                : string.Empty)
            + $"; the target achieves {e.AchievedFactor:F2}"
            + (e.MinContentScale < ScaledContentThreshold
                ? $" for host-scale content and {(e.AchievedFactor * e.MinContentScale):F2} for the "
                  + $"{e.MinContentScale:F3}-scaled subtree, against a band limit of "
                  + $"{BandLimitedTexelsPerPixel:F2} — if the second number is below the band limit "
                  + "then NO capture setting within this path's budget can reach it and the only "
                  + "complete cure is for that sub-view not to be scaled."
                : ".")
            + " WHY THIS NUMBER IS THE FIX: at one texel per authored pixel the eye resamples this "
            + "window at ~1 texel per rendered pixel, and a bilinear resample at that rate has a "
            + "sub-texel PHASE whose response at Nyquist runs from full contrast to zero. A still "
            + "window holds one phase and reads sharp; a carried window sweeps it every frame and "
            + "reads as the flicker; a released window LOCKS it, which is why letting go freezes "
            + "whatever the drag left behind. Two texels per authored pixel puts the eye on a mip "
            + "level that was box-filtered down from a 2x rasterization instead.";
        VRLog.Info(Scope, $"PANEL SUPERSAMPLE engaged on '{window}': the window's canvas now renders "
                          + $"into a {rtW}x{rtH} capture target (MSAA {msaa}x, D24S8) which is "
                          + $"resolved after every frame into a {rtW}x{rtH} MIPPED display target "
                          + $"(mips {(e.MipRt != null ? e.MipRt.mipmapCount : rt.mipmapCount)}, "
                          + $"Trilinear, aniso {AnisoLevel}) — {Mb(e.VramBytes)} MB for the pair — "
                          + "through a dedicated orthographic camera, and a mod-owned quad at the "
                          + $"window's exact world pose shows that texture.{factorNote}{msaaNote}{frameNote} The "
                          + $"host canvas itself is moved to this panel's PRIVATE capture layer "
                          + $"{e.Layer} (mask 0x{1 << e.Layer:X8}; {FreeLayers.Count} of "
                          + $"{PoolSize} pool layer(s) still free) — no other panel is ever on it, "
                          + "so this camera cannot capture a neighbouring window the way ModBuild "
                          + "193's shared layer did. It stays "
                          + "ENABLED, REGISTERED and RAYCASTABLE, so every laser/poke hit, hover, "
                          + "drag and scroll takes the same path as before; only the camera that "
                          + $"draws it changed. {e.LayersMoved} transform(s) moved, "
                          + $"{e.ForeignSkipped} foreign render subtree(s) left alone.");
    }
}
