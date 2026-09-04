using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using GloomhavenVR.Core;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// <b>NO CANVAS THE MOD DOES NOT OWN MAY REFERENCE A MOD-OWNED CAMERA, AT ANY TIME.</b>
///
/// <para>WHAT ModBuild 424 DID, AND WHY IT WAS NECESSARY BUT NOT SUFFICIENT. 424 added
/// <see cref="CanvasConversion.SafeOriginalWorldCamera"/>: a game canvas's recorded "original"
/// <see cref="Canvas.worldCamera"/> may never be a value the MOD wrote. That guard is correct and
/// it stays. It fired <b>23 times</b> in the ModBuild 425 hardware log (the `27` a naive grep
/// returns includes four lines that merely quote the marker), and the defect it was aimed at
/// survived it verbatim: the same canvas, `UI Map Esc Menu`, was measured by the eye census at
/// scene ROOT, <c>mode=ScreenSpaceCamera</c>, <c>cam='GloomhavenVR.HeadCamera'</c>, layer 5, alpha
/// 0.845 — the 423 sighting to the field. A guard that refuses a bad RECORDED value and leaves the
/// bad LIVE value standing catches nothing; that is the whole lesson of this file.</para>
///
/// <para><b>THE MECHANISM, FROM THE 425 LOG AND THE CODE.</b> It is the first of the three
/// candidates — the guard corrects the record and the live value is never corrected — and the
/// reason the restore that should have corrected it does not is structural:</para>
/// <list type="number">
///   <item><b>The capture reads an INHERITED value.</b> <c>Convert</c> re-parents the game window
///     under the mod's world-space host (CanvasConversion.1.Core.cs) and only THEN calls
///     <c>AdoptNestedCanvases</c>. For a nested canvas that is not its own sorting root, Unity's
///     <c>Canvas.worldCamera</c> / <c>Canvas.sortingOrder</c> getters report the ROOT canvas's
///     values — ours. The 425 log proves it twice over: 23 of the 24 adoptions in the whole session
///     "leaked", and all fourteen `Content` canvases plus `UI Map Esc Menu` report
///     <c>sortingOrder=1000</c> at adoption, which is the mod host's SEED order and not their own
///     (the census reads the same ESC-menu canvas at <c>order=1200</c> once it is a root again).
///     The single adoption that did NOT leak is `UI Party Inventory Item Tooltip` — the one canvas
///     in the log whose <c>overrideSorting</c> the game holds TRUE, which makes it its own sorting
///     root, which is exactly the case where the getters report the canvas's own values. One
///     exception, and it is the exception the model predicts.</item>
///   <item><b>The restore writes on the wrong side of the re-parent.</b> <see cref="CanvasConversion.Release"/>
///     restored <c>worldCamera</c> in its adopted-canvas loop, which runs BEFORE the target is
///     re-parented out of the host. While the canvas is still nested under our world-space host,
///     Unity keeps the nested canvas's effective camera equal to the root's, so the restore is
///     discarded before it can mean anything. The 425 log has the release running in full — the
///     stack trace at Player.log:7151 names <c>CanvasConversion.Release</c>, and the
///     `release: restored 2D window forced hidden` line further down the same method printed — and
///     the census reads <c>cam='GloomhavenVR.HeadCamera'</c> on that canvas immediately
///     afterwards.</item>
/// </list>
///
/// <para><b>WHAT THIS FILE ENFORCES INSTEAD.</b> Three things, none of them a guard on a recorded
/// value:</para>
/// <list type="bullet">
///   <item><b>Capture before the re-parent.</b> <see cref="PreCaptureGameCameras"/> records every
///     canvas's OWN camera while the window is still at its 2D home, so the recorded original is
///     the game's real value and not <see cref="CanvasConversion.FindGameUiCamera"/>'s guess.</item>
///   <item><b>Restore after the re-parent, then READ BACK.</b> <see cref="RestoreAdoptedCameras"/>
///     runs at the end of <see cref="CanvasConversion.Release"/>, once the window is a root canvas
///     again; it writes, re-reads, and if the live value is STILL mod-owned it writes the game's
///     own UI camera and re-reads again. "Caught" and "corrected" can no longer look the same in a
///     log because both values are on the line.</item>
///   <item><b>A repair pass over the population the mod already tracks.</b>
///     <see cref="TickCameraOwnership"/> walks the canvases this mod has ever pointed at a mod
///     camera — a bounded list, strided, no scene sweep, no allocation — and corrects any that is
///     no longer held by a live panel and still references one of ours.</item>
/// </list>
///
/// <para><b>AND A BELT THAT DOES NOT DEPEND ON ANY OF THAT BEING RIGHT.</b> Eight rounds say the
/// mechanism just proved is not necessarily the only one. So while the mod has handed a window
/// back and the GAME reports it CLOSED, the window's own root canvas is held disabled through a
/// one-for-one ledger (<see cref="HoldReleasedWindowDark"/>) and handed back the instant the game
/// reopens it, the mod re-converts it, or the module shuts down. It is bounded by OWNERSHIP —
/// never by a frame count and never by a timer — and it restores exactly what it hid. User ruling,
/// verbatim: <i>"wichtig ist dass es für den User nicht sichtbar ist"</i>, and on this very object:
/// <i>"Dieser Streifen muss ja irgendwo ein Objekt sein. Du hattest es doch schon gefunden
/// eigentlich - kannst du es nicht einfach dauerhaft deaktivieren?"</i>. The standing ruling that
/// the pause menu must ALWAYS be openable is what makes the lift conditions, not a timeout, the
/// safety here.</para>
///
/// <para>MULTIPLAYER: local rendering only. Every write in this file is a <c>Canvas.worldCamera</c>
/// or a <c>Canvas.enabled</c> on the local player's own UI. No game state, no wire traffic, no
/// config key, nothing another peer can observe.</para>
/// </summary>
internal static partial class CanvasConversion
{
    // ---- ownership test -------------------------------------------------------------------

    /// <summary>
    /// Is this camera one the MOD owns? True for the head camera, for the camera converted panels
    /// are bound to, and for every camera this mod creates — all of ours are named
    /// <c>GloomhavenVR.*</c>, which is the same ownership answer
    /// <c>CanvasConversion.FindGameContent</c> and <see cref="FindGameUiCamera"/> already use.
    /// Deliberately broader than "is it the head camera": the invariant is about mod ownership, not
    /// about one camera, so a panel supersample capture camera left on a game canvas would be
    /// caught by the same rule.
    /// </summary>
    internal static bool IsModOwnedCamera(Camera? cam)
    {
        if (cam == null)
            return false;
        if (ReferenceEquals(cam, Rig.VRRigDriver.HeadCamera) || ReferenceEquals(cam, WorldCamera))
            return true;
        return cam.name.StartsWith("GloomhavenVR.", StringComparison.Ordinal);
    }

    /// <summary>The game's own UI camera right now, or null when this build has none.</summary>
    private static Camera? GameUiCameraNow()
    {
        Camera? head = Rig.VRRigDriver.HeadCamera;
        return head != null ? FindGameUiCamera(head) : null;
    }

    private static string CamName(Camera? cam) => cam != null ? cam.name : "<null>";

    // ---- capture, BEFORE the re-parent ------------------------------------------------------

    private static readonly List<Canvas> PreCaptureScratch = new(16);

    /// <summary>
    /// Record the OWN <see cref="Canvas.worldCamera"/> of every canvas in the window's subtree
    /// while it is still at its 2D home. Called from <see cref="Convert"/> immediately before the
    /// target is re-parented under the float host, which is the last moment at which those getters
    /// report the canvas's own value rather than the mod host's — see this file's header. One
    /// component walk per conversion; the lists are cleared by <see cref="Release"/>.
    /// </summary>
    private static void PreCaptureGameCameras(ConvertedPanel panel, RectTransform target)
    {
        panel.PreCapturedCanvases.Clear();
        panel.PreCapturedCameras.Clear();
        PreCaptureScratch.Clear();
        target.GetComponentsInChildren(includeInactive: true, PreCaptureScratch);
        for (int i = 0; i < PreCaptureScratch.Count; i++)
        {
            Canvas c = PreCaptureScratch[i];
            if (c == null)
                continue;
            panel.PreCapturedCanvases.Add(c);
            // The value as the GAME left it. It may legitimately be null (an overlay canvas), and
            // null is recorded as null — this is an observation, never a fallback.
            panel.PreCapturedCameras.Add(c.worldCamera);
        }
        PreCaptureScratch.Clear();
    }

    /// <summary>
    /// The camera this canvas carried at its 2D home, if <see cref="PreCaptureGameCameras"/> saw
    /// it. <paramref name="found"/> separates "the game had null here" from "we never saw it".
    /// </summary>
    private static Camera? PreCapturedCameraOf(ConvertedPanel panel, Canvas nested, out bool found)
    {
        for (int i = 0; i < panel.PreCapturedCanvases.Count; i++)
        {
            if (!ReferenceEquals(panel.PreCapturedCanvases[i], nested))
                continue;
            found = true;
            return panel.PreCapturedCameras[i];
        }
        found = false;
        return null;
    }

    // ---- the tracked population --------------------------------------------------------------

    /// <summary>
    /// Every GAME canvas this mod has ever pointed at a mod-owned camera. This is the population
    /// the repair pass walks — the mod's own collection, never a scene sweep. Capped; the cap is
    /// stated once in the log if it is ever reached, so a silently truncated population can never
    /// masquerade as a clean one ([[a-truncated-list-is-not-absence]]).
    /// </summary>
    private static readonly List<Canvas> CameraWatch = new(32);

    /// <summary>The value each watched canvas should be handed back — the pre-capture where there
    /// was one, else the game's UI camera at the time it was watched.</summary>
    private static readonly List<Camera?> CameraWatchWanted = new(32);

    private const int CameraWatchCap = 64;
    private const int CameraWatchStride = 8;
    private static int s_cameraWatchCursor;
    private static bool s_cameraWatchCapLogged;
    private static int s_cameraRepairs;
    private static int s_cameraRestoreSecondWrites;

    /// <summary>Take note that <paramref name="nested"/> now carries a mod camera, and what it
    /// should be handed back. Called from the adoption; idempotent per canvas.</summary>
    private static void WatchAdoptedCamera(Canvas nested, Camera? wanted)
    {
        for (int i = 0; i < CameraWatch.Count; i++)
        {
            if (!ReferenceEquals(CameraWatch[i], nested))
                continue;
            // Keep the freshest non-mod answer; never downgrade a real observation to a guess.
            if (!IsModOwnedCamera(wanted) && wanted != null)
                CameraWatchWanted[i] = wanted;
            return;
        }
        if (CameraWatch.Count >= CameraWatchCap)
        {
            if (!s_cameraWatchCapLogged)
            {
                s_cameraWatchCapLogged = true;
                // HW-VERIFY
                VRLog.Alert("WorldUI", $"CANVAS CAMERA WATCH FULL at {CameraWatchCap} canvas(es): the repair "
                    + "pass tracks the game canvases this mod has pointed at one of its own cameras, and it "
                    + "is now refusing new entries. A canvas adopted from here on is still restored by the "
                    + "release path, but it is NOT covered by the repair pass — so a `] CANVAS CAMERA "
                    + "REPAIR` count of zero after this line is not evidence that nothing leaked. Raise "
                    + "CameraWatchCap if this ever appears.");
            }
            return;
        }
        CameraWatch.Add(nested);
        CameraWatchWanted.Add(wanted);
    }

    /// <summary>Is this canvas currently inside a live panel's adoption set — i.e. does the mod own
    /// it right now? Bounded by the mod's own lists (a handful of panels, a few records each); no
    /// hierarchy walk and no scene query.</summary>
    private static bool HeldByActivePanel(Canvas c)
    {
        for (int p = 0; p < Active.Count; p++)
        {
            ConvertedPanel panel = Active[p];
            for (int i = 0; i < panel.AdoptedCanvases.Count; i++)
            {
                if (ReferenceEquals(panel.AdoptedCanvases[i].Canvas, c))
                    return true;
            }
        }
        return false;
    }

    // ---- the enforcement: write, read back, write again -------------------------------------

    /// <summary>
    /// Hand <paramref name="c"/> back a camera that is NOT ours, and prove it took. Returns true
    /// when the live value was mod-owned on entry (i.e. when there was something to correct).
    /// <paramref name="stillOurs"/> reports the outcome of the read-back after both attempts —
    /// which is the field that separates "caught" from "corrected".
    /// </summary>
    private static bool EnforceGameCamera(Canvas c, Camera? wanted, string where,
        out Camera? before, out Camera? after, out bool stillOurs, out bool secondWrite)
    {
        before = c.worldCamera;
        after = before;
        secondWrite = false;
        stillOurs = false;
        bool wasOurs = IsModOwnedCamera(before);
        Canvas modeRoot = c.rootCanvas != null ? c.rootCanvas : c;

        // AN OBSERVATION, OR NOTHING. `wanted` is trustworthy only when it is a real camera the mod
        // does not own — which after ModBuild 426 is the value captured before the re-parent. Never
        // hand a mod camera back as an "original": that is 424's rule, applied to the value about to
        // be WRITTEN rather than to the value recorded.
        bool haveOriginal = wanted != null && !IsModOwnedCamera(wanted);

        // NOTHING TO RESTORE AND NOTHING WRONG → TOUCH NOTHING. Without this, a canvas whose live
        // camera is legitimately null (the overlay path) and whose original we never observed would
        // be pushed onto the game's UI camera by the fallback below — a restore inventing a state
        // the game never had, on a canvas that was never a problem. Restores are permanent damage
        // when wrong.
        if (!haveOriginal && !wasOurs)
            return false;

        // THE GUESS IS ONLY ALLOWED WHERE THE DEFECT LIVES. An exact recorded original is written
        // back whatever the canvas's mode is — it is the game's own value. The game-UI-camera
        // FALLBACK is not: on a WORLD-SPACE canvas `worldCamera` is the event camera, and the mod
        // deliberately binds the head camera there (WorldTooltips flips the game's tooltip canvas to
        // world space and points it at the head so the laser projects against the right camera).
        // Substituting a flat UI camera into that would break hit-testing to fix a rim that a
        // world-space canvas cannot produce: the rim is a SCREEN-SPACE canvas parked at its
        // planeDistance in front of the HMD. So the fallback is gated on the mode, and a world-space
        // canvas with no recorded original is left exactly as the mod set it.
        Camera? give = haveOriginal
            ? wanted
            : (modeRoot.renderMode != RenderMode.WorldSpace ? GameUiCameraNow() : null);
        if (give == null && !haveOriginal)
        {
            stillOurs = wasOurs;
            if (modeRoot.renderMode != RenderMode.WorldSpace)
            {
                // HW-VERIFY: a screen-space GAME canvas holding our camera that this build cannot
                // hand anything better to — no observed original and no game UI camera in the scene.
                // Nothing is written: null would turn it into a Screen-Space-OVERLAY canvas, and the
                // two shipped subsystems that reason about overlays in VR disagree about whether the
                // HMD sees one. This says so instead of pretending it was fixed.
                VRLog.Alert("WorldUI", $"CANVAS CAMERA RESTORE ({where}) COULD NOT ACT: game canvas "
                    + $"'{c.name}' (mode={modeRoot.renderMode}, layer {c.gameObject.layer}) holds the "
                    + $"mod-owned camera '{CamName(before)}', no original was observed for it before "
                    + "the re-parent, and this scene has no game UI camera to hand it to. LEFT AS IS "
                    + "— writing null would make it an OVERLAY canvas, which is a state the mod "
                    + "cannot state the visibility of. The belt (] MODAL DARK HOLD) is what covers "
                    + "this case.");
            }
            return wasOurs;
        }

        if (!ReferenceEquals(c.worldCamera, give))
            c.worldCamera = give;

        after = c.worldCamera;
        stillOurs = IsModOwnedCamera(after);
        if (stillOurs)
        {
            // The write did not take (Unity re-derives a nested canvas's camera from its root, and
            // a refused detach can leave the canvas nested). Try the game's own UI camera outright
            // and re-read. There is no third attempt: the repair pass owns anything that survives
            // this, on a later frame when the canvas is a root again.
            secondWrite = true;
            s_cameraRestoreSecondWrites++;
            Camera? gameUi = modeRoot.renderMode != RenderMode.WorldSpace ? GameUiCameraNow() : null;
            if (!IsModOwnedCamera(gameUi) && !ReferenceEquals(c.worldCamera, gameUi))
                c.worldCamera = gameUi;
            after = c.worldCamera;
            stillOurs = IsModOwnedCamera(after);
        }

        if (wasOurs || stillOurs || secondWrite)
        {
            string verdict = stillOurs
                ? "STILL OURS AFTER THE WRITE — the repair pass owns it from here"
                : "CORRECTED";
            // HW-VERIFY: BOTH values are on this line on purpose. `] MODAL ADOPT CAMERA LEAK`
            // reported a catch that corrected nothing for a whole build; a restore line that does
            // not state the LIVE value after the write can do exactly the same.
            VRLog.Note("WorldUI", $"CANVAS CAMERA RESTORE ({where}): game canvas '{c.name}' "
                + $"(root={(c.isRootCanvas ? "itself" : CanvasName(c.rootCanvas))}, "
                + $"layer {c.gameObject.layer}) live camera BEFORE '{CamName(before)}', wanted "
                + $"'{CamName(wanted)}', live camera AFTER '{CamName(after)}'"
                + (secondWrite ? ", second write to the game's own UI camera was needed" : "")
                + $". VERDICT: {verdict}. A ScreenSpaceCamera canvas is parked at its planeDistance "
                + "in front of whatever camera it names, so a GAME canvas naming OUR head camera is "
                + "head-locked in the eye by construction — that is the left-edge rim.");
        }
        return wasOurs;
    }

    private static string CanvasName(Canvas? c) => c != null ? c.name : "<null>";

    /// <summary>
    /// Restore the cameras of everything the released panel adopted, AFTER the window has
    /// been re-parented out of the float host. Consumes the snapshot
    /// <see cref="Release"/> took before it cleared the adoption list. Returns true when at least
    /// one canvas was found still carrying a mod camera — the input to the release verdict line.
    /// </summary>
    private static bool RestoreAdoptedCameras(string where)
    {
        bool anyWasOurs = false;
        for (int i = 0; i < ReleaseCameraCanvases.Count; i++)
        {
            Canvas c = ReleaseCameraCanvases[i];
            if (c == null)
                continue;
            anyWasOurs |= EnforceGameCamera(c, ReleaseCameraWanted[i], where,
                out _, out Camera? after, out _, out _);
            // Keep it under the repair pass with the best answer we have, so a write that did not
            // take is corrected on a later frame instead of being reported and forgotten.
            WatchAdoptedCamera(c, IsModOwnedCamera(after) ? ReleaseCameraWanted[i] : after);
        }
        ReleaseCameraCanvases.Clear();
        ReleaseCameraWanted.Clear();
        return anyWasOurs;
    }

    /// <summary>Snapshot taken by <see cref="Release"/> in its adopted-canvas loop and consumed by
    /// <see cref="RestoreAdoptedCameras"/> after the re-parent. Release is not re-entrant (it is
    /// called from the WorldUI frame phases and from <see cref="ReleaseAll"/>'s sequential loop),
    /// and both are cleared at the top of Release, so a torn snapshot is not reachable.</summary>
    private static readonly List<Canvas> ReleaseCameraCanvases = new(8);
    private static readonly List<Camera?> ReleaseCameraWanted = new(8);

    // ---- the repair pass ---------------------------------------------------------------------

    /// <summary>
    /// Correct any watched canvas that is no longer held by a live panel and still references a
    /// mod-owned camera. Strided over the mod's own list — at most
    /// <see cref="CameraWatchStride"/> entries per frame, no allocation, no scene sweep, no
    /// component walk. Dead entries are pruned as they are met.
    /// </summary>
    private static void TickCameraOwnership()
    {
        if (CameraWatch.Count == 0)
            return;
        int steps = Mathf.Min(CameraWatchStride, CameraWatch.Count);
        for (int n = 0; n < steps; n++)
        {
            if (CameraWatch.Count == 0)
                return;
            if (s_cameraWatchCursor >= CameraWatch.Count)
                s_cameraWatchCursor = 0;
            int i = s_cameraWatchCursor;
            Canvas c = CameraWatch[i];
            if (c == null)
            {
                CameraWatch.RemoveAt(i);
                CameraWatchWanted.RemoveAt(i);
                continue; // cursor now points at the next entry
            }
            s_cameraWatchCursor++;

            // While the mod owns it, our camera is the CORRECT value — that is what adoption is.
            if (HeldByActivePanel(c))
                continue;

            // A nested canvas inherits its root's camera, so the value that decides what is drawn
            // is the ROOT's. Judge and correct the root; when the canvas IS its own root the two
            // are the same object and this is one write.
            Canvas root = c.rootCanvas != null ? c.rootCanvas : c;
            if (!IsModOwnedCamera(root.worldCamera))
                continue;
            if (HeldByActivePanel(root))
                continue;
            // A WORLD-SPACE canvas naming the head camera is naming its EVENT camera, which is
            // correct and is what WorldTooltips sets on purpose. The rim is a screen-space canvas
            // parked at its planeDistance in front of the HMD; only that shape is repaired here.
            if (root.renderMode == RenderMode.WorldSpace)
                continue;

            Camera? wanted = CameraWatchWanted[i];
            EnforceGameCamera(root, wanted, "repair pass",
                out Camera? before, out Camera? after, out bool stillOurs, out _);
            s_cameraRepairs++;
            // HW-VERIFY: the repair pass's own line. `] CANVAS CAMERA REPAIR` firing at all means
            // the release path did not finish the job on that canvas; it firing repeatedly for the
            // same canvas means the write is being undone by a writer nothing here has found.
            VRLog.Note("WorldUI", $"CANVAS CAMERA REPAIR #{s_cameraRepairs} this session: game canvas "
                + $"'{root.name}' (mode={root.renderMode}, layer {root.gameObject.layer}, "
                + $"root={(root.isRootCanvas ? "yes" : "no")}) was found referencing the mod-owned camera "
                + $"'{CamName(before)}' with no live panel holding it. SET TO '{CamName(after)}'"
                + (stillOurs ? " — AND IT IS STILL OURS, so something outside this file writes it" : "")
                + ". No canvas the mod does not own may reference a mod-owned camera, at any time.");
        }
    }

    // ---- the belt ----------------------------------------------------------------------------

    /// <summary>One entry of the released-window dark hold: exactly what was hidden, and what to
    /// hand back.</summary>
    private struct DarkHold
    {
        public UIWindow Window;
        public Canvas Canvas;
        public string Name;
        public bool CanvasWasEnabled;
    }

    private static readonly List<DarkHold> DarkHolds = new(2);
    private static int s_darkHolds;
    private static int s_darkHoldLifts;

    /// <summary>Holds whose canvas or window the game destroyed before they could be handed back.
    /// Counted separately so "taken" and "handed back" read as a BALANCE: taken = handed back +
    /// dropped + still standing. Any other arithmetic on the release line means a window is dark
    /// and nobody is going to turn it back on.</summary>
    private static int s_darkHoldsDropped;

    /// <summary>
    /// THE BELT. The mod has handed <paramref name="window"/> back and the GAME reports it CLOSED,
    /// so nothing about it is meant to be on screen — yet the 425 census measured exactly this
    /// state drawing at alpha 0.845, full height, docked left. Disable the window's own root canvas
    /// and record what was disabled, one for one.
    ///
    /// <para>THIS IS NOT A TIMER AND NOT A LATCH. It is lifted by ownership: the game reopening the
    /// window (<c>UIWindow.IsOpen</c>, which flips in <c>Show()</c> BEFORE the fade —
    /// [[open-is-not-drawing]] — so it is the earliest possible signal), the mod re-converting it,
    /// or the module shutting down. The pause menu must ALWAYS be openable; that standing ruling is
    /// what these lift conditions exist to honour, and it is why there is no time limit here.</para>
    ///
    /// <para>SCOPE. Only a target that IS a <c>UIWindow</c> root, only when the game reports it
    /// closed, and only a canvas that is currently ENABLED — a canvas we found already disabled is
    /// not recorded and not touched, so the restore can never enable something the game had
    /// off.</para>
    /// </summary>
    private static bool HoldReleasedWindowDark(UIWindow window, RectTransform target)
    {
        Canvas? own = target.GetComponent<Canvas>();
        if (own == null || !own.enabled)
            return false;
        for (int i = 0; i < DarkHolds.Count; i++)
        {
            if (ReferenceEquals(DarkHolds[i].Canvas, own))
                return false; // already held; never record a second, stale "was enabled"
        }
        own.enabled = false;
        DarkHolds.Add(new DarkHold
        {
            Window = window,
            Canvas = own,
            Name = target.name,
            CanvasWasEnabled = true,
        });
        s_darkHolds++;
        return true;
    }

    /// <summary>Hand back every dark hold on <paramref name="target"/> — called from
    /// <see cref="Convert"/> so a re-conversion never floats a canvas we switched off.</summary>
    private static void LiftDarkHoldFor(Transform target)
    {
        for (int i = DarkHolds.Count - 1; i >= 0; i--)
        {
            Canvas c = DarkHolds[i].Canvas;
            if (c != null && c.transform != target)
                continue;
            LiftDarkHoldAt(i, "the mod is converting the window again");
        }
    }

    /// <summary>Hand every dark hold back — module shutdown / VR off. Nothing may outlive the
    /// mod.</summary>
    private static void LiftAllDarkHolds(string why)
    {
        for (int i = DarkHolds.Count - 1; i >= 0; i--)
            LiftDarkHoldAt(i, why);
    }

    private static void LiftDarkHoldAt(int i, string why)
    {
        DarkHold hold = DarkHolds[i];
        DarkHolds.RemoveAt(i);
        if (hold.Canvas == null)
        {
            s_darkHoldsDropped++;
            return;
        }
        if (hold.CanvasWasEnabled && !hold.Canvas.enabled)
            hold.Canvas.enabled = true;
        s_darkHoldLifts++;
        // HW-VERIFY: every hold that is taken must be seen to be handed back. A hold count that
        // runs ahead of the lift count is a window the player cannot open.
        VRLog.Note("WorldUI", $"MODAL DARK HOLD LIFTED ({s_darkHoldLifts} of {s_darkHolds} taken this "
            + $"session): '{hold.Name}' — {why}. Its own root canvas is ENABLED again, exactly as it "
            + "was when the hold was taken. The window is openable.");
    }

    /// <summary>
    /// Per-frame service for the belt: hand a hold back the moment the game reopens the window, and
    /// drop entries whose window or canvas the game destroyed. Bounded by the hold list, which is
    /// one entry per released-and-closed floated window (two in the whole 425 session).
    /// </summary>
    private static void TickDarkHolds()
    {
        for (int i = DarkHolds.Count - 1; i >= 0; i--)
        {
            DarkHold hold = DarkHolds[i];
            if (hold.Canvas == null || hold.Window == null)
            {
                DarkHolds.RemoveAt(i);
                s_darkHoldsDropped++;
                continue;
            }
            if (hold.Window.IsOpen)
                LiftDarkHoldAt(i, "the game reopened the window (UIWindow.IsOpen)");
        }
    }

    // ---- the per-frame entry point -----------------------------------------------------------

    /// <summary>
    /// Both halves' per-frame work, called from the top of <see cref="Tick"/> so it runs whether or
    /// not any panel is converted. Bounded: a strided pass over the watch list and a walk of the
    /// (one- or two-entry) dark-hold list.
    /// </summary>
    private static void TickOwnershipGuards()
    {
        TickDarkHolds();
        TickCameraOwnership();
    }

    // ---- the verdict line --------------------------------------------------------------------

    /// <summary>
    /// ONE LINE PER RELEASE saying WHICH HALF did the work, so the next hardware log states whether
    /// half 1 is sufficient on its own. If this line never reports the belt catching anything, the
    /// camera fix is proven and the belt can be retired; if it keeps reporting a catch, there is
    /// still a leak and we will know it rather than inferring it.
    /// </summary>
    private static void NoteReleaseOwnership(string name, bool cameraWasWrong, bool beltHid,
        bool gameSaysClosed, bool isWindow)
    {
        // Every UIWindow release, plus any release that actually caught something. NOT every panel
        // release: the stat-panel and tooltip surfaces release several panels a turn and none of
        // them is a window, so an unconditional line here would be the flood ModBuild 331 removed.
        if (!isWindow && !cameraWasWrong && !beltHid)
            return;
        string verdict = cameraWasWrong
            ? (beltHid
                ? "THE CAMERA WAS WRONG AND WAS CORRECTED, AND THE BELT ALSO HELD THE CANVAS DARK"
                : "THE CAMERA WAS WRONG AND WAS CORRECTED; the belt did not run here")
            : (beltHid
                ? "THE CAMERA WAS ALREADY CORRECT — THE BELT HID A CANVAS WHOSE CAMERA WAS FINE"
                : "THE CAMERA WAS ALREADY CORRECT AND NOTHING NEEDED HIDING");
        // HW-VERIFY: this is the line that decides next round which half is load-bearing.
        VRLog.Note("WorldUI", $"MODAL RELEASE OWNERSHIP for '{name}': {verdict}. The game reports the "
            + $"window {(gameSaysClosed ? "CLOSED" : "OPEN")}. Holds taken {s_darkHolds}, handed back "
            + $"{s_darkHoldLifts}, dropped with a destroyed window {s_darkHoldsDropped}, still standing "
            + $"{DarkHolds.Count}; repair-pass corrections {s_cameraRepairs}; releases needing a second "
            + $"camera write {s_cameraRestoreSecondWrites}. ModBuild 424's recorded-value guard fired 23 "
            + "times in the 425 log and the canvas still carried our head camera — it was necessary and "
            + "not sufficient, and this line exists so that can never again be invisible.");
    }
}
