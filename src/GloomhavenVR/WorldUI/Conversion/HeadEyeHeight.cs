using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// THE PLAYER'S EYE HEIGHT ABOVE THEIR OWN TRACKING FLOOR — REMEMBERED, BECAUSE THE HEAD CAMERA
/// DOES NOT ALWAYS HAVE ONE.
///
/// <para>THE BUG THIS EXISTS FOR (ModBuild 198 hardware log, the user's photograph
/// <c>Tischbeine.jpg</c>: "Alle Fenster spawnen jetzt UNTER dem Tisch"). The map room's permanent
/// windows — character screen, ESC menu, quest log, options — are converted during the map scene's
/// LOAD, in the gap between <c>BuildMapRig</c> creating the head camera and the first tracked XR
/// pose reaching it. <c>CreateHeadCamera</c> parents the camera to the rig root at
/// <c>localPosition = Vector3.zero</c>, and the map rig's root IS the tracking floor, so for those
/// ~35 frames the head camera reports the player's eyes ON THE FLOOR. The log proves it three ways
/// in the same session: the rig line says "tracking floor at (-184.45, -154.55, 0.18)", the
/// spawn-clamp lines say the head was at exactly y = -154.55 with a perfectly horizontal forward
/// (which no real HMD ever has), and the peer-head diagnostic printed "local head: (-184.45,
/// -154.55, 0.18)". 0.7 s later "Map rig recentered" reported the same head at y = 216.87, i.e.
/// 1.87 m above that floor — the windows had been placed 1.77 m too low and, by the ModBuild 193
/// ruling that a spawned window never moves again, they stayed there.</para>
///
/// <para>WHY A REMEMBERED VALUE AND NOT A DEFERRED SPAWN. Deferring is the obvious fix and it is
/// not available from here: <c>ModalFallback</c>'s convert loop puts a window that fails to place
/// into its <c>Failed</c> set, which "retries only after a close/re-open" — so refusing to place
/// would strand the map room's permanent windows for the whole visit. Re-placing later is not
/// available either: the pre-reveal re-place is the only sanctioned second write and in this very
/// log it arrived AFTER the reveal deadline and was correctly refused by
/// <see cref="PanelPoseWatch"/>. The window therefore gets exactly ONE placement and it has to be
/// right the first time — which means the vertical reference must be right the first time.</para>
///
/// <para>WHY <c>localPosition.y</c> IS THE RIGHT QUANTITY IN EVERY RIG. The head camera is always
/// parented to the rig root and its local pose is written by the TrackedPoseDriver, i.e. it is the
/// raw XR pose. That makes <c>localPosition.y</c> the player's real eye height above their real
/// tracking floor whatever the rig root is doing — scaled diorama, map seat or 1:1 menu — which is
/// the same number <c>VRRigDriver.RecenterMap</c> prints as "N m real above the tracking floor".
/// Nothing here is scaled: metres in, metres out, and the caller applies the rig scale.</para>
/// </summary>
internal static class HeadEyeHeight
{
    /// <summary>
    /// Below this height above the tracking floor (real metres) a "head" pose is not a head. A
    /// seated adult's eyes are ~1.10–1.30 m up and even sitting on the floor puts them near 0.80 m,
    /// so 0.40 m is far under anything a player can produce — while both readings the ModBuild 198
    /// log actually contains (0.00 m for the map-load spawns, −0.245 m for the quest-preview popup)
    /// are unambiguously under it. It is a "this is not a measurement" test, not a comfort dial:
    /// widening it towards real eye heights would start correcting poses that are simply low.
    /// </summary>
    internal const float MinPlausibleMeters = 0.40f;

    /// <summary>
    /// Stand-in used ONLY when the headset has not reported a single usable pose yet in this
    /// session, i.e. when there is nothing to remember. It is an assumption and it is labelled as
    /// one on every log line that uses it. In practice it is close to unreachable: the player passes
    /// through the menu rig — where <see cref="Observe"/> runs every tick with tracking live — for
    /// seconds before any room can be entered, so the remembered measurement is normally already in
    /// hand by the time the first window spawns.
    /// </summary>
    internal const float AssumedMeters = 1.60f;

    private static float _lastMeasured = float.NaN;
    private static int _comparisons;
    private static int _tracked;
    private static int _untracked;

    /// <summary>How many head-height tests have been made this session (the instrument's own
    /// sample count, printed beside every value it produces).</summary>
    internal static int Comparisons => _comparisons;

    // (THE TrackedCount / UntrackedCount ACCESSORS ARE GONE — 2026-09 refactor, F-56: no reader
    // anywhere, while their siblings Comparisons and HaveMeasured do have external ones, which is
    // why only these two fell out. The backing _tracked / _untracked are live and are named in
    // this instrument's own describe string.)

    /// <summary>True once a real tracked eye height has been measured at least once.</summary>
    internal static bool HaveMeasured => !float.IsNaN(_lastMeasured);

    /// <summary>
    /// One head-height test. Returns true when <paramref name="head"/> carries a usable tracked
    /// pose, in which case <paramref name="eyeMeters"/> is the live height and it is remembered.
    /// A camera with no rig parent (the <c>Camera.main</c> fallback outside the mod's own rig) has
    /// no tracking floor to measure against, so it is reported as tracked and left entirely alone —
    /// this class corrects the mod's rig, never somebody else's camera.
    /// </summary>
    internal static bool Observe(Camera? head, out float eyeMeters)
    {
        eyeMeters = float.NaN;
        Transform? t = head != null ? head!.transform : null;
        Transform? rig = t != null ? t.parent : null;
        if (t == null || rig == null)
            return true;

        _comparisons++;
        float live = t.localPosition.y;
        if (live < MinPlausibleMeters)
        {
            _untracked++;
            return false;
        }
        _tracked++;
        _lastMeasured = live;
        eyeMeters = live;
        return true;
    }

    /// <summary>Per-tick sampler (see <see cref="PanelPoseWatch.BeginTick"/>).</summary>
    internal static void Observe(Camera? head) => Observe(head, out _);

    /// <summary>The eye height to place from, real metres above the tracking floor, plus where it
    /// came from (log material — the caller must print both).</summary>
    internal static float Resolve(out string source)
    {
        if (HaveMeasured)
        {
            source = $"the last tracked head pose this session ({_lastMeasured:F2} m)";
            return _lastMeasured;
        }
        source = $"NO tracked head pose has been seen yet this session — assumed {AssumedMeters:F2} m";
        return AssumedMeters;
    }

    /// <summary>
    /// Correct <paramref name="headPos"/>'s HEIGHT when the camera it came from has no tracked
    /// pose. Only the vertical term is touched: the horizontal position and the forward are the
    /// camera's own and stay the camera's own, so a spawn placed through this still faces and
    /// follows whatever direction the rig is aimed in. Returns true when a substitution was made,
    /// with <paramref name="note"/> carrying the full falsifiable statement for the log.
    /// </summary>
    internal static bool CorrectVerticalReference(Camera? head, ref Vector3 headPos, out string note)
    {
        note = string.Empty;
        if (Observe(head, out _))
            return false;

        Transform t = head!.transform;
        Transform rig = t.parent!;
        float live = t.localPosition.y;
        float eyeMeters = Resolve(out string source);

        Vector3 local = t.localPosition;
        local.y = eyeMeters;
        Vector3 corrected = rig.TransformPoint(local);
        note = $"HEAD HEIGHT SUBSTITUTED — the head camera reported its eyes {live:F2} m above the "
               + $"tracking floor, below the {MinPlausibleMeters:F2} m any real eye can reach, so it "
               + "carries no tracked XR pose yet (the map rig parents the camera to the rig root at "
               + "localPosition zero, and that root IS the tracking floor). Placing from "
               + $"{eyeMeters:F2} m instead — {source}; world y {headPos.y:F2} → {corrected.y:F2}. "
               + $"{_comparisons} head-height comparison(s) this session: {_tracked} tracked, "
               + $"{_untracked} not";
        headPos = corrected;
        return true;
    }

    /// <summary>The player's tracking floor in world units — the rig root, which every rig flavour
    /// places ON that floor. Used to express placement heights in real metres as well as world
    /// units, so a clamp line can be checked against the room instead of only against itself.</summary>
    internal static bool TryTrackingFloorY(out float y)
    {
        Transform? rigRoot = Rig.VRRigDriver.RigRoot;
        if (rigRoot == null)
        {
            y = 0f;
            return false;
        }
        y = rigRoot.position.y;
        return true;
    }
}
