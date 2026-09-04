using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// THE YAW-ONLY WINDOW FACING RULE, IN ONE PLACE (user ruling 2026-09-04, hardware).
///
/// <para>In the user's own words, reporting the encounter-decision window (the one that makes you
/// assign an outcome to a character): <i>"Das Entscheidungsfenster (bei dem man Characteren wegen
/// einer Entscheidung zB von einer Begegnung etwas zuweisen muss), dreht sich auch in der pitch
/// achse zum Spieler beim spawn. Das soll generell bei keinem Fenster der Fall sein. Ausschließlich
/// yaw-achse."</i> He named one window and then generalised it himself: <b>no window is ever
/// pitched or rolled — window facing is yaw-only, always.</b></para>
///
/// <para><b>WHY A SHARED HELPER AND NOT FIVE COPIES OF <c>fwd.y = 0f</c>.</b> The rule was already
/// implemented — correctly — in three places when the report came in: <c>ModalFallback.Upright</c>
/// (the modal spawn path, ModBuild 189), <see cref="PanelPlacement"/>'s private <c>Flatten</c>, and
/// <c>FlatScreen.PlaceScreen</c>. It was the SURFACE family that never got the memo, because there
/// was nothing for a new surface to call: every author who wanted "in front of the head" wrote
/// <c>Quaternion.LookRotation(head.forward, Vector3.up)</c> from scratch, and that expression is
/// pitched by construction. The value of this type is not that it flattens a vector — it is that
/// the next surface someone writes cannot get it wrong without deliberately avoiding the one
/// obvious call.</para>
///
/// <para><b>THE SITES, and what each of them used to do.</b> The five that were WRONG — every one of
/// them built <c>Quaternion.LookRotation(head.forward, Vector3.up)</c>:
/// <list type="bullet">
/// <item><c>FloatingDecisionSurfaces.Place</c> — the window the ruling was reported against.</item>
/// <item><c>DecisionDockSurface.TryPlaceAtHmd</c> — the same family's HMD fallback float.</item>
/// <item><c>UseBarsSurface.PlaceFloatingStack</c> — and its stack step, which is derived from that
/// rotation and therefore used to march along a tilted line.</item>
/// <item><c>DialogSurface.ConvertNow</c> — the confirmation box.</item>
/// <item><c>FlatScreen.TickStartingIndicator</c> — the pre-menu "starting…" label.</item>
/// </list>
/// The two that were already RIGHT and now route through here so the rule has one home:
/// <c>FlatScreen.PlaceScreen</c> and <c>PanelPlacement.Flatten</c> (via <see cref="Flatten"/>).
/// <c>ModalFallback.Upright</c> is deliberately NOT converted: it strips a tilt out of an
/// already-built rotation rather than deriving one from a head, it is not this lane's file, and its
/// own instrument has been reporting 0.0/0.0 since ModBuild 189.</para>
///
/// <para><b>ROTATION IS UNCONDITIONAL, POSITION IS NOT.</b> The complaint is that the panel TURNS in
/// pitch, and that is what this fixes everywhere. Where a call site then places the panel ALONG a
/// forward vector, it chooses for itself between <see cref="Facing.FlatForward"/> and the raw gaze
/// and says why in a comment at the call: an upright panel put on the flattened forward rides at eye
/// height, which pushes it toward the top edge of the view — or out of it — exactly when the player
/// is looking down at the diorama, which is the common case in this game. Silently moving where
/// panels land is not what was asked for, so no site does it without an argument written down.</para>
///
/// <para><b>THE DEGENERATE CASE IS RESOLVED IN FOUR NAMED TIERS</b>, and the tier that fired is
/// printed on the placement line rather than inferred. A player looking straight down at the board
/// has a forward vector with no horizontal part at all, and a naive flatten hands the caller a zero
/// vector — <c>Quaternion.LookRotation</c> of which is identity, i.e. a window facing world north no
/// matter where the player stands. See <see cref="YawOnly"/>.</para>
/// </summary>
internal static class HeadFacing
{
    /// <summary>Below this squared length a flattened vector carries no usable yaw. Matches the
    /// threshold <c>PanelPlacement.Flatten</c> and <c>FlatScreen.PlaceScreen</c> already use.</summary>
    private const float DegenerateSqr = 1e-4f;

    /// <summary>A stripped pitch/roll below this is quaternion round-trip noise, not a writer
    /// authoring a tilt. Same value and same reasoning as <c>ModalFallback.UprightNoiseDeg</c>.</summary>
    private const float NoiseDeg = 0.5f;

    /// <summary>The last horizontal facing this helper could actually derive from a head — tier 3 of
    /// the degenerate fallback. Static and shared on purpose: it is "where the player was last known
    /// to be looking", which is a property of the player, not of one window.</summary>
    private static Vector3 _lastGoodFlat = Vector3.forward;

    /// <summary>True once the tilt alarm below has fired, so it is a ONE-TIME regression alarm and
    /// not a per-placement flood.</summary>
    private static bool _tiltWarned;

    /// <summary>Horizontal unit projection of an arbitrary vector, falling back to world forward when
    /// it has no horizontal part. The plain-vector form of the rule, for the callers that already
    /// hold a direction rather than a head transform (<c>PanelPlacement.Flatten</c> delegates here).
    /// It has no last-good memory on purpose: its input is a panel→head vector, not a gaze, and
    /// substituting a stale GAZE for a degenerate one of those would be a different quantity.</summary>
    internal static Vector3 Flatten(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > DegenerateSqr ? v.normalized : Vector3.forward;
    }

    /// <summary>
    /// A head pose reduced to what a window is allowed to use: a horizontal forward, a yaw-only
    /// rotation, and the measurement of exactly what was thrown away to get there.
    /// </summary>
    internal readonly struct Facing
    {
        /// <summary>Unit horizontal projection of the head's forward (y == 0), never zero.</summary>
        internal readonly Vector3 FlatForward;

        /// <summary>Rotation looking along <see cref="FlatForward"/> with world up — zero pitch,
        /// zero roll. uGUI/TMP fronts render along −forward, so a host given this rotation and
        /// placed in front of the head shows its face TO the head, the same convention
        /// <see cref="PanelPlacement.Facing"/> uses.</summary>
        internal readonly Quaternion Rotation;

        /// <summary>Signed head pitch that was stripped, degrees. Unity's convention, so + means the
        /// player was looking DOWN (Quaternion.Euler(30,0,0) has a forward with a negative y) — the
        /// common case in this game, where the board is a table.</summary>
        internal readonly float StrippedPitchDeg;

        /// <summary>Signed head roll that was stripped, degrees.</summary>
        internal readonly float StrippedRollDeg;

        /// <summary>Yaw actually kept, degrees, for the log line.</summary>
        internal readonly float YawDeg;

        /// <summary>Which of the four tiers produced <see cref="FlatForward"/> — see
        /// <see cref="YawOnly"/>. Printed, never inferred.</summary>
        internal readonly string Source;

        internal Facing(Vector3 flatForward, Quaternion rotation,
                        float strippedPitchDeg, float strippedRollDeg, float yawDeg, string source)
        {
            FlatForward = flatForward;
            Rotation = rotation;
            StrippedPitchDeg = strippedPitchDeg;
            StrippedRollDeg = strippedRollDeg;
            YawDeg = yawDeg;
            Source = source;
        }
    }

    /// <summary>
    /// Reduce a head/camera transform to the yaw-only facing every window must use.
    ///
    /// <para>THE FOUR TIERS, in order, each named in <see cref="Facing.Source"/>:
    /// <list type="number">
    /// <item><b>gaze</b> — the normal case: the head's forward with its vertical component removed.</item>
    /// <item><b>head-up</b> — the player is looking straight down or straight up, so the forward
    /// vector has no horizontal part. The head's OWN up vector is horizontal exactly then and
    /// carries the yaw; it points backwards when looking up, so it is negated in that case. Both
    /// axes vertical at once is impossible for a unit quaternion, so this tier all but always
    /// succeeds.</item>
    /// <item><b>last-good</b> — neither axis was usable (only reachable through a degenerate or
    /// non-orthonormal transform). Falls back to the last horizontal facing this helper derived,
    /// which keeps a window facing where the player was last looking rather than snapping it to
    /// world north.</item>
    /// <item><b>world-forward</b> — nothing has ever been derived this session. The final constant,
    /// present so this can never divide by zero or hand back an identity rotation by accident.</item>
    /// </list></para>
    /// </summary>
    internal static Facing YawOnly(Transform head)
    {
        Vector3 rawFwd = head.forward;
        string source = "gaze";

        Vector3 flat = rawFwd;
        flat.y = 0f;
        if (flat.sqrMagnitude < DegenerateSqr)
        {
            // Straight down or straight up: the head's own up vector is the horizontal one.
            source = "head-up";
            flat = head.up * (rawFwd.y > 0f ? -1f : 1f);
            flat.y = 0f;
            if (flat.sqrMagnitude < DegenerateSqr)
            {
                source = "last-good";
                flat = _lastGoodFlat;
                if (flat.sqrMagnitude < DegenerateSqr)
                {
                    source = "world-forward";
                    flat = Vector3.forward;
                }
            }
        }

        flat.Normalize();
        if (source == "gaze" || source == "head-up")
            _lastGoodFlat = flat;

        Quaternion rotation = Quaternion.LookRotation(flat, Vector3.up);

        // The residual is what `rotation` does NOT contain: express the head rotation in the upright
        // frame and read its x (pitch) and z (roll) back, wrapped to a signed ±180° so "2° up" does
        // not print 358. Same construction as ModalFallback.Upright, so the two instruments' numbers
        // are directly comparable in one log.
        Vector3 residual = (Quaternion.Inverse(rotation) * head.rotation).eulerAngles;
        float pitch = Mathf.DeltaAngle(0f, residual.x);
        float roll = Mathf.DeltaAngle(0f, residual.z);

        return new Facing(flat, rotation, pitch, roll, rotation.eulerAngles.y, source);
    }

    /// <summary>
    /// ONE line per placement — never per frame; every caller either runs behind a spawn latch or
    /// gates this itself. It is what the next hardware log is read for: a window that still pitches
    /// has either no line at all (a placement site nobody routed through here) or a line whose
    /// stripped pitch is zero while the panel visibly slopes (something downstream re-writes the
    /// rotation). Both are answerable from the line alone, which is the whole point of printing the
    /// baseline as well as the exception.
    ///
    /// <para><paramref name="positionNote"/> is the call site's own justification for where it put
    /// the panel, because the ruling constrains the ROTATION only and a reader of the log has to be
    /// able to tell a deliberate raw-gaze placement from an oversight.</para>
    /// </summary>
    internal static void LogPlaced(string surface, in Facing facing, Vector3 position, string positionNote)
    {
        // HW-VERIFY
        VRLog.Note("WorldUI", $"WINDOW FACING: '{surface}' placed YAW-ONLY at " +
                              $"({position.x:F2},{position.y:F2},{position.z:F2}) — yaw {facing.YawDeg:F1}° kept, " +
                              $"pitch/roll stripped {facing.StrippedPitchDeg:F1}°/{facing.StrippedRollDeg:F1}° " +
                              $"(forward from '{facing.Source}'). POSITION: {positionNote}. " +
                              "Yaw-only by user ruling 2026-09-04: \"Das soll generell bei keinem Fenster der " +
                              "Fall sein. Ausschließlich yaw-achse.\"");

        if (!_tiltWarned
            && (Mathf.Abs(facing.StrippedPitchDeg) > NoiseDeg || Mathf.Abs(facing.StrippedRollDeg) > NoiseDeg))
        {
            _tiltWarned = true;
            VRLog.Note("WorldUI", "WINDOW FACING: the head was tilted at a window placement " +
                                  $"(pitch {facing.StrippedPitchDeg:F1}° / roll {facing.StrippedRollDeg:F1}°) and " +
                                  "the tilt was stripped, as the ruling requires — this line is the proof the " +
                                  "guard actually engaged rather than never being reached, and is reported ONCE " +
                                  "per session. A window that still looks pitched after this line has a SECOND " +
                                  "writer of its rotation downstream of the placement.");
        }
    }
}
