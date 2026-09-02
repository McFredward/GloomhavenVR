using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

// ModalFallback part 12 — THE WINDOW NOBODY RENDERS, AND THE VERDICT LINE THAT SAYS SO.
//
// THE REPORT (ModBuild 197, translated): "For testing I jumped back to the 2D map and closed the
// MENU there — after that I could not open it again, no matter how often I pressed the button."
//
// WHAT THE LOG ACTUALLY SHOWS — the press arrives, the game opens the window, and the mod does
// nothing at all. Eleven consecutive open/close pairs, every one of them complete:
//
//   [OptionsToggle] X tap: actuallyOpen=False (esc=False …) -> OPEN
//   MODAL FALLBACK: window 'UI Map Esc Menu' (ID ESCMenu) opened without a VR conversion
//     (room=False, of which scenario board=False) → ModalUI + floating window (or screen) …
//   OPTIONS TAP: pause menu OPENED (X tap) — floats in front of the player in VR
//     (activeInHierarchy=True)
//   [OptionsToggle] X tap: actuallyOpen=True (esc=True …) -> CLOSE      ← the NEXT press
//
// So all four candidate failures are settled by that quartet: the button press ARRIVES, the
// game's own open RUNS, the visibility choke point FIRES (that middle line is the postfix on
// UIWindow's transition), and the toggle's own state read agrees the window is open on the next
// press. Nothing is stuck, nothing is latched, no set is stale — the window is genuinely OPEN
// and the player cannot see it. ModBuild 196's ancestor refusal is NOT involved and cannot be.
// (THE ARGUMENT RECORDED HERE HAS CHANGED, BECAUSE THE FACT IT RESTED ON DID. It read:
// "RendersInsideFloatedAncestor's first statement is `!MapRoom.MapRoomDriver.Active → false`, and
// every one of those opens prints room=False." ModBuild 226 deleted that map-room gate — the rule
// now holds wherever a floatable ancestor is open. The CONCLUSION is unchanged and now rests on a
// different, stronger clause of the same method: the ESC/Options family is EXEMPT from the ancestor
// rule outright — `MenuWindowFamily.IsGameOwnedMenu(window)` returns before the hierarchy is walked at
// all — and ESCMenu and Options are both in that set. So the refusal still cannot fire here, in the
// map room or out of it.)
//
// WHY IT IS INVISIBLE — THE CANVAS IS AN OVERLAY, AND IN VR AN OVERLAY IS DRAWN BY NOBODY. The
// same hardware log dumps the scene's root canvases, and the ESC menu is the odd one out:
//
//   'Campaign Canvas'      mode=ScreenSpaceCamera cam='UI Camera' order=0     ← the player sees it
//   'Map Canvas'           mode=ScreenSpaceCamera cam='UI Camera' order=-1    ← the player sees it
//   'Persistent UI_unified' mode=ScreenSpaceCamera cam='UI Camera' order=1100 ← the Options window
//   'Story Canvas'         mode=ScreenSpaceCamera cam='UI Camera' order=1
//   'UI Map Esc Menu'      mode=ScreenSpaceOverlay cam='<none>'    order=1200 ← THE ONE HE LOST
//
// A Screen-Space-OVERLAY canvas is not rendered by any camera: Unity draws it straight to the
// display, after the cameras, and in XR that display is the desktop mirror window — never the
// HMD eye textures. The mod's flat screen composites the 2D game by RETARGETING the game's
// cameras into a RenderTexture (FlatScreen.2.CameraStack: `cam.targetTexture = TargetFor(record)`)
// and drawing that RT on a quad, so an overlay canvas is not in the composite either. The window
// is therefore shown in exactly two places: on the flat monitor next to the player's head, and
// nowhere else. Every other window on the 2D map rides 'UI Camera' and is composited normally,
// which is precisely why this report names one menu and not "the UI".
//
// AND THIS IS MODBUILD 177's BUG, ONE MODE OVER. 177's report was the same sentence ("das
// Optionsmenu öffnet sich nicht") behind the same log line, in the 3D map room; 178 fixed it by
// promoting ModalFallback's scenario gate from `ScenarioBoardExists` to `TableInFrontOfPlayer`,
// i.e. "is there a room to float a window IN". Outside a room that gate is false by design and
// the class hands the window to the flat screen — 178 assumed the flat screen would show it.
// For an overlay canvas that assumption is false, and the standing lesson applies verbatim:
// SUPPRESSING X BECAUSE Y HANDLES IT REQUIRES Y TO ACTUALLY HANDLE IT.
//
// THE FIX, IN THE ORDER IT IS TRIED:
//  1. BIND IT TO THE CAMERA THE PLAYER IS ALREADY LOOKING AT. Switch the window's own root
//     canvas from Overlay to ScreenSpaceCamera on the UI camera the flat screen is capturing —
//     the same move the game itself makes for its persistent canvases (CanvasManager binds
//     persistentUICanvas/tooltipCanvas to the "UICamera"-tagged camera on every scene load).
//     The window then composites into the flat screen exactly like the Options window beside
//     it, is hit by the same flat-screen pointer, and keeps its own sortingOrder 1200 so it
//     draws over the map UI and under the tooltips, which is the game's own intent.
//  2. FAIL OPEN — FLOAT IT. If there is no captured UI camera to bind to (or the flat screen is
//     not up), the window is added to <see cref="StrandedFloat"/> and the tick floats it in
//     front of the player even though there is no room, because an unreachable menu is strictly
//     worse than a menu in an unusual place. The X button on the panel and the controller's own
//     X tap both still close it, so the fallback can never trap the player either.
//
// IT CANNOT BECOME PERMANENT — LEVEL-TRIGGERED IN BOTH DIRECTIONS. Nothing here is latched:
// StrandedFloat is CLEARED and rebuilt every tick, and every bind is re-asked every tick and
// handed back the moment its reason ends (window closed, window floated, flat screen gone, VR
// off, module shutdown, camera destroyed). A canvas the mod re-bound is restored to the exact
// renderMode / worldCamera / planeDistance it had.
//
// THE VERDICT LINE. The 197 log could not distinguish "the mod refused to float it" from "the
// mod floated it and something else ate it" — there was simply no line after the open. Every
// tracked window now gets a change-gated PRESENTATION VERDICT naming where it is being shown and
// why, with the running counts per outcome, so silence is never again the evidence.
//
// MULTIPLAYER: local rendering only. One renderMode + worldCamera write on the local player's own
// UI canvas. No game state, no NetProtocol surface, nothing on the wire.

internal static partial class ModalFallback
{
    /// <summary>One game canvas the mod re-bound onto the flat screen's UI camera.</summary>
    private sealed class ScreenBoundCanvas
    {
        public UIWindow Window = null!;
        public string Name = "<window>";
        public Canvas Canvas = null!;
        public RenderMode OriginalMode;
        public Camera? OriginalCamera;
        public float OriginalPlaneDistance;
        public int BoundAtFrame;
    }

    private static readonly List<ScreenBoundCanvas> ScreenBound = new(2);

    /// <summary>
    /// FAIL-OPEN SET, rebuilt from scratch every tick: the windows this tick floats even though
    /// there is no room, because nothing else in the build can draw them. Read through
    /// <see cref="FloatWantedFor"/> by BOTH the release and the convert loop.
    /// </summary>
    private static readonly List<UIWindow> StrandedFloat = new(2);

    /// <summary>
    /// The fail-open CLAIM — why this is a set and not a per-tick re-test.
    ///
    /// <para>The strandedness test reads the window's own root canvas. The moment the fail-open
    /// float lands, that canvas is re-parented under a world-space mod host and its root canvas
    /// is the HOST's — the test would answer "not stranded", the claim would drop, the release
    /// loop would release the float, and the next tick would strand it again: a convert/release
    /// oscillation, i.e. the unreachable menu again at one frame per cycle. So the claim is
    /// remembered for as long as the window stays open, and it ends level-triggered on exactly
    /// three events, all of which are re-checked every tick: the game closes the window, a room
    /// appears (the ordinary float path owns it from then on), or VR/the module stops. It can
    /// only ever keep a window VISIBLE, which is the direction this whole part errs in.</para>
    /// </summary>
    private static readonly HashSet<UIWindow> StrandedClaim = new();

    /// <summary>Static predicate (no per-tick delegate allocation) for pruning dead claims.</summary>
    private static readonly System.Predicate<UIWindow> ClaimIsStale = w => w == null || !w.IsOpen;

    // ---- counts: an instrument that can only be silent is not an instrument ----------------
    private static int _screenBindCount;
    private static int _screenBindReleaseCount;
    private static int _screenBindNoCameraCount;
    private static int _strandedFloatCount;

    /// <summary>Per-window change gate for the verdict line (window name → last verdict).</summary>
    private static readonly Dictionary<string, string> LastVerdict = new(8);

    /// <summary>
    /// The base conditions for floating ANY window, with the "is there a room" question left
    /// out — that question is <c>want</c>'s, and the stranded set is exactly the case where the
    /// answer is "no room, and no screen either".
    /// </summary>
    private static bool ConvertBaseActive =>
        WorldUIConfig.ModalWindowStyle && !FlatScreen.ManualScreenActive && WorldUIConfig.ConversionActive;

    /// <summary>
    /// Does this window want a float THIS tick? Either the ordinary answer (<paramref name="convertWanted"/>
    /// — there is a room and a window is open), or the fail-open one: it is stranded, i.e. proven
    /// to be drawn by nothing at all. Consulted by BOTH the release loop and the convert loop so a
    /// stranded float is not torn down again by the next tick's release pass.
    /// </summary>
    private static bool FloatWantedFor(UIWindow? window, bool convertWanted)
    {
        if (window == null)
            return false;
        if (convertWanted)
            return true;
        return ConvertBaseActive && ContainsWindow(StrandedFloat, window);
    }

    /// <summary>
    /// Decide, for every open tracked window, WHERE it is being shown — and make that true.
    /// Called from <see cref="Tick"/>'s decide phase, after <see cref="OpenWindows"/> is built
    /// and before the release/convert loops read <see cref="StrandedFloat"/>.
    /// </summary>
    /// <param name="floatPathOwnsWindows">
    /// True when the ordinary float path will take these windows (a room exists and the
    /// conversion is active). Then there is nothing to do: the float IS the presentation.
    /// </param>
    private static void TickScreenBind(bool floatPathOwnsWindows)
    {
        StrandedFloat.Clear();

        bool vrUp = VRSession.IsRunning;
        // The flat screen is the ONLY thing that composites the game's 2D UI for the HMD. When it
        // is hidden the game's cameras are retargeted to an offscreen sink instead
        // (FlatScreen.3.Desktop's scrub), so "screen visible" is also what makes a camera's
        // non-null targetTexture mean "captured" rather than "discarded" below.
        bool screenUp = vrUp && FlatScreen.ScreenVisible;

        // ---- 1. hand back every bind whose reason has ended (level-triggered) ---------------
        for (int i = ScreenBound.Count - 1; i >= 0; i--)
        {
            ScreenBoundCanvas entry = ScreenBound[i];
            if (entry.Window == null || entry.Canvas == null)
            {
                ScreenBound.RemoveAt(i); // destroyed with its scene — the canvas died with it
                continue;
            }
            string? reason =
                !vrUp ? "VR is no longer running"
                : !screenUp ? "the flat screen is gone — the window belongs to the float path now"
                : !entry.Window.IsOpen ? "the game closed the window"
                : IsConverted(entry.Window) ? "the window is floated now, which draws it itself"
                : null;
            if (reason == null)
                continue;
            RestoreScreenBind(entry, reason);
            ScreenBound.RemoveAt(i);
        }

        if (!vrUp || floatPathOwnsWindows)
        {
            // A room exists (or the conversion is off and nothing here could help anyway): the
            // ordinary float path is the presentation. Drop every fail-open claim — this is one
            // of the three level-triggered ends of a claim, and it is why entering a room can
            // never leave a window floated "because part 12 said so" once the real rule applies.
            if (StrandedClaim.Count > 0)
            {
                VRLog.Info("WorldUI", $"MODAL PRESENTATION: {StrandedClaim.Count} fail-open float claim(s) "
                                      + "dropped — the ordinary float path owns these windows again "
                                      + $"(room={VRModeStateMachine.TableInFrontOfPlayer}, VR "
                                      + $"{(vrUp ? "running" : "stopped")}).");
                StrandedClaim.Clear();
            }
            return;
        }

        // A claim ends with the window it was made for. Pruned BEFORE the loop so a window that
        // closed since the last tick can never hold a float open.
        if (StrandedClaim.Count > 0)
            StrandedClaim.RemoveWhere(ClaimIsStale);

        // ---- 2. every open window that nothing draws ---------------------------------------
        for (int i = 0; i < OpenWindows.Count; i++)
        {
            UIWindow window = OpenWindows[i];
            if (window == null || !window.IsOpen)
                continue;
            if (StrandedClaim.Contains(window))
            {
                // Ours already — re-assert it so the release loop keeps the float alive. See the
                // StrandedClaim doc for why this is not re-derived from the canvas.
                StrandedFloat.Add(window);
                continue;
            }
            if (IsConverted(window))
                continue; // floated for some other reason; that float is the presentation

            // "Which canvas draws this window" is a CONTAINMENT question — the canvas an element
            // renders under is genuinely its nearest enclosing one — so GetComponentInParent is
            // the right call here, and .rootCanvas is what decides the render mode (a nested
            // canvas inherits the root's mode and camera).
            Canvas? own = window.GetComponentInParent<Canvas>();
            Canvas? root = own != null ? own.rootCanvas : null;
            if (root == null)
                continue; // no canvas at all: nothing this step can reason about

            // ORDER MATTERS: ask "is this one of ours" BEFORE reading the render mode. A canvas
            // this step already re-bound reads ScreenSpaceCamera precisely BECAUSE we wrote that,
            // and the branch below would then congratulate the game on a binding we made.
            if (FindScreenBound(window) != null)
                continue; // already bound by us; the release pass above owns its end

            if (root.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                // A camera draws this canvas (Screen-Space-Camera, or World-Space on a layer a
                // camera renders), so the flat screen captures it with the rest of the 2D UI —
                // which is exactly what this class has always assumed of every window. Say so
                // once, so the assumption is visible in the log instead of implied by silence.
                Verdict(window, screenUp
                    ? $"ON THE FLAT SCREEN (its canvas is {root.renderMode}, which a camera draws)"
                    : $"waiting (its canvas is {root.renderMode}, which a camera draws, but the "
                      + "flat screen is not up this instant)");
                continue;
            }

            if (screenUp)
            {
                Camera? cam = FindCompositedUiCamera();
                if (cam != null)
                {
                    BindToScreenCamera(window, root, cam);
                    continue;
                }
                _screenBindNoCameraCount++;
            }

            // FAIL OPEN. Overlay canvas, and no captured UI camera to put it on: nothing in the
            // build can draw this window. Float it, room or no room.
            string why = screenUp
                ? "its canvas is Screen-Space-OVERLAY, which no camera renders, and no captured UI "
                  + "camera was found to move it onto"
                : "its canvas is Screen-Space-OVERLAY, which no camera renders, and the flat screen "
                  + "is not up to composite it";
            if (!ConvertBaseActive)
            {
                // The float is switched off (style=screen, the manual full-screen chord, or the
                // conversion disabled outright). Nothing here can make this window visible — say
                // so plainly rather than booking a claim that will never be honoured.
                Verdict(window, $"SHOWN NOWHERE — {why}, and the mod's floated-window path is "
                                + "switched off (window style / manual screen / conversion off)");
                continue;
            }
            StrandedFloat.Add(window);
            StrandedClaim.Add(window);
            _strandedFloatCount++;
            Verdict(window, $"FLOATED (fail-open): {why}");
        }
    }

    /// <summary>
    /// Move a game window's own root canvas onto the UI camera the flat screen is capturing.
    /// Records everything it overwrites so <see cref="RestoreScreenBind"/> is exact.
    /// </summary>
    private static void BindToScreenCamera(UIWindow window, Canvas root, Camera cam)
    {
        var entry = new ScreenBoundCanvas
        {
            Window = window,
            Name = window.name,
            Canvas = root,
            OriginalMode = root.renderMode,
            OriginalCamera = root.worldCamera,
            OriginalPlaneDistance = root.planeDistance,
            BoundAtFrame = Time.frameCount,
        };
        ScreenBound.Add(entry);

        // planeDistance only has to put the canvas INSIDE the camera's frustum; canvases on one
        // camera sort against each other by sortingOrder, not by distance, and this UI camera
        // draws the UI layer alone (mask 0x00000020 in the 197 census), so there is no geometry
        // to sort against either. Keep the canvas's own value when it already fits and clamp it
        // into the frustum when it does not.
        float near = Mathf.Max(cam.nearClipPlane, 0.0001f);
        float far = Mathf.Max(cam.farClipPlane, near * 2f);
        float plane = Mathf.Clamp(root.planeDistance, near * 1.5f, Mathf.Max(near * 1.5f, far * 0.5f));

        root.renderMode = RenderMode.ScreenSpaceCamera;
        root.worldCamera = cam;
        root.planeDistance = plane;
        _screenBindCount++;

        string target = cam.targetTexture != null ? cam.targetTexture.name : "BACKBUFFER";
        Verdict(window, "ON THE FLAT SCREEN (re-bound)");
        VRLog.Info("WorldUI", $"MODAL SCREEN BIND: '{entry.Name}' (ID {window.ID}) had a Screen-Space-OVERLAY "
                              + "root canvas, which NO camera renders — in VR that reaches the desktop mirror "
                              + "and nothing else, and the flat screen composites cameras, so the window was "
                              + $"open and drawn nowhere. Re-bound '{root.name}' to camera '{cam.name}' "
                              + $"(depth {cam.depth:F0}, target {target}) at planeDistance {plane:F1} "
                              + $"(was {entry.OriginalPlaneDistance:F1}); sortingOrder {root.sortingOrder} is "
                              + "unchanged, so it draws over the map UI and under the tooltips exactly as the "
                              + $"game intends. Handed back the moment it closes or is floated. COUNTS: "
                              + $"{_screenBindCount} bind(s), {_screenBindReleaseCount} hand-back(s), "
                              + $"{_screenBindNoCameraCount} refusal(s) for want of a captured UI camera, "
                              + $"{_strandedFloatCount} fail-open float(s).");
    }

    /// <summary>
    /// The UI camera whose output the flat screen is capturing. Same classification the screen
    /// itself uses (<c>FlatScreen.IsUiCamera</c>: the "UICamera" tag the game's own CanvasManager
    /// keys on, or an orthographic camera), plus the OUTCOME test that matters: it must render
    /// into a RenderTexture. A UI camera still on the backbuffer is one the screen did not
    /// capture, and binding to it would move the window from one place the HMD cannot see to
    /// another. Only called while the flat screen is visible, which is what rules out the
    /// scenario-side desktop scrub sink (that runs only while the screen is HIDDEN).
    /// </summary>
    private static Camera? FindCompositedUiCamera()
    {
        int count = VRCameraPolicy.GetAllCamerasNonAlloc(out Camera[] cams);
        Camera? best = null;
        for (int i = 0; i < count; i++)
        {
            Camera cam = cams[i];
            if (cam == null || cam.targetTexture == null)
                continue;
            if (!cam.CompareTag("UICamera") && !cam.orthographic)
                continue;
            if (best == null || cam.depth > best.depth)
                best = cam; // the topmost UI camera composites last, which is where a menu belongs
        }
        return best;
    }

    private static ScreenBoundCanvas? FindScreenBound(UIWindow? window)
    {
        if (window == null)
            return null;
        for (int i = 0; i < ScreenBound.Count; i++)
        {
            if (ReferenceEquals(ScreenBound[i].Window, window))
                return ScreenBound[i];
        }
        return null;
    }

    /// <summary>
    /// Hand one window's canvas back. Called from <see cref="TickScreenBind"/>'s release pass,
    /// from <see cref="TryConvertWindow"/> (the float takes over) and from the hide branch of
    /// <see cref="OnWindow"/> (the game closed it). Idempotent.
    /// </summary>
    private static void ReleaseScreenBind(UIWindow? window, string reason)
    {
        ScreenBoundCanvas? entry = FindScreenBound(window);
        if (entry == null)
            return;
        RestoreScreenBind(entry, reason);
        ScreenBound.Remove(entry);
    }

    /// <summary>Hand every bind back (module shutdown / VR off) — a canvas the mod moved must
    /// never outlive the module that moved it.</summary>
    private static void ReleaseAllScreenBind(string reason)
    {
        for (int i = ScreenBound.Count - 1; i >= 0; i--)
            RestoreScreenBind(ScreenBound[i], reason);
        ScreenBound.Clear();
        StrandedFloat.Clear();
        StrandedClaim.Clear();
        LastVerdict.Clear();
    }

    private static void RestoreScreenBind(ScreenBoundCanvas entry, string reason)
    {
        _screenBindReleaseCount++;
        Canvas canvas = entry.Canvas;
        if (canvas != null)
        {
            canvas.renderMode = entry.OriginalMode;
            canvas.worldCamera = entry.OriginalCamera;
            canvas.planeDistance = entry.OriginalPlaneDistance;
        }
        LastVerdict.Remove(entry.Name);
        VRLog.Info("WorldUI", $"MODAL SCREEN BIND: '{entry.Name}' handed back after "
                              + $"{Time.frameCount - entry.BoundAtFrame} frame(s) — {reason} "
                              + $"(canvas restored to {entry.OriginalMode}). COUNTS: {_screenBindCount} bind(s), "
                              + $"{_screenBindReleaseCount} hand-back(s), {_screenBindNoCameraCount} refusal(s) "
                              + $"for want of a captured UI camera, {_strandedFloatCount} fail-open float(s).");
    }

    /// <summary>
    /// WHERE IS THIS WINDOW BEING SHOWN — change-gated, one line per window per verdict, with the
    /// running counts. The whole point of this line is that ModBuild 197's log had NOTHING after
    /// "opened without a VR conversion": a refusal and a successful hand-off to the flat screen
    /// were indistinguishable, and both looked like silence.
    /// </summary>
    private static void Verdict(UIWindow window, string verdict)
    {
        string name = window.name;
        if (LastVerdict.TryGetValue(name, out string? last) && last == verdict)
            return;
        LastVerdict[name] = verdict;
        VRLog.Info("WorldUI", $"MODAL PRESENTATION VERDICT '{name}' (ID {window.ID}): {verdict} "
                              + $"[room={VRModeStateMachine.TableInFrontOfPlayer}, "
                              + $"mode={VRModeStateMachine.CurrentMode}, "
                              + $"flat screen {(FlatScreen.ScreenVisible ? "SHOWN" : "HIDDEN")}]. COUNTS: "
                              + $"{_screenBindCount} screen bind(s), {_screenBindReleaseCount} hand-back(s), "
                              + $"{_screenBindNoCameraCount} refusal(s) for want of a captured UI camera, "
                              + $"{_strandedFloatCount} fail-open float(s).");
    }
}
