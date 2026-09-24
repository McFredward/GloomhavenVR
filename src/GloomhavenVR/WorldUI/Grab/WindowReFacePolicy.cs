using GloomhavenVR.Core;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// <b>THE ONE ANSWER TO "DOES THIS RELEASE TURN THE WINDOW TOWARD ME?"</b> — the policy behind
/// <c>[WorldUI] WindowFacing</c>, for every object in the mod that re-faces on release.
///
/// <para><b>WHY IT EXISTS (ModBuild 439, the redundancy survey's row R1 — its highest-ranked
/// entry).</b> The dial landed with exactly ONE reader: <c>GrabbableModal</c>. Twelve days later
/// <c>SurfaceGrabBar</c> was written with its own unconditional re-face and never wired it, so a
/// player who set the dial to <i>Nie</i> watched every floating decision, reward and enemy-reveal
/// panel keep snapping round while his modal windows obeyed. The German description promises the
/// opposite in as many words — <c>Loc.ConfigDescriptions.German.cs</c>: <i>"'Immer': beides (das
/// bisherige Verhalten <b>aller</b> Fenster)"</i>. A setting the user asked for reached one quarter
/// of the objects it names, and nothing could notice, because the second site did not so much as
/// mention the key.</para>
///
/// <para><b>WHAT IS SHARED AND WHAT IS DELIBERATELY NOT.</b> Only the POLICY moved here — the two
/// gates, in order, and the epsilon below which a turn is not worth writing. The rotation
/// ARITHMETIC stays with each owner because the owners legitimately differ: <c>GrabbableModal</c>
/// turns about the INK UNION's centre (ModBuild 240 — a window whose ink sat 859 mm off its frame
/// origin travelled two thirds of a metre on a 44° re-face), <c>SurfaceGrabBar</c> turns about the
/// frame origin, which for a pivot-centred decision host IS the drawn centre, and
/// <c>PlayTray</c> turns around its visible board centre while preserving its authored tilt.
/// Merging those four bodies would flatten a real difference; merging the question they all
/// have to ask first does not.</para>
///
/// <para><b>THE TWO GATES, IN ORDER.</b> Both are user rulings and the first outranks the second.
/// <list type="number">
/// <item><b>A SHARED WINDOW NEVER RE-FACES, ON ANY CLIENT, AND THIS IS NOT CONFIGURABLE.</b>
/// Verbatim: <i>"Da es ein Fenster für alle ist, sollen diese Fenster nach dem Greifen auch nicht
/// die Orientierung nach dem Spieler ändern, wie es die anderen Fenster tun."</i> The rotation on a
/// window that belongs to everybody is a shared fact: correcting it toward the person who last
/// moved it turns it away from everyone else, and it is worse than cosmetic on the SENDER, whose
/// published pose would be corrected locally one frame later. The dial cannot reach past this.</item>
/// <item><b>FOR LOCAL WINDOWS THE PLAYER'S THREE-WAY DIAL DECIDES</b> — <c>Never</c>,
/// <c>LaserOnly</c> (his own default) or <c>Always</c>. The axis is the grab MODALITY, latched from
/// the grabber's identity at the gesture start (<c>PanelGrabHandle.LastGrabWasLaser</c>) and never
/// guessed from how far away the hand was: a laser carry translates only, so the window arrives
/// still facing the way it used to and would be read edge-on; a hand carry has already yawed the
/// window with the wrist for the whole drag, so the player aimed it himself.</item>
/// </list></para>
///
/// <para><b>WHAT IS NOT TOUCHED.</b> This is a ONE-SHOT ON RELEASE in every mode. Nothing here
/// makes a window follow the head, and the standing project rule that nothing re-orients with head
/// movement is unaffected — in particular <c>CombatLogSurface</c>'s test-#20 ruling against a
/// per-tick billboard is a separate, still-binding decision that this policy neither asks about nor
/// relaxes. The dial governs the re-face ON RELEASE, nothing else.</para>
///
/// <para><b>MULTIPLAYER:</b> nothing here goes on the wire. It is a pure function of one config
/// value, one latched bool and the caller's own shared flag. A peer's mirror of a shared window is
/// refused by gate 1 on every client independently, which is what makes the 1:1 pose hold without a
/// wire field.</para>
/// </summary>
internal static class WindowReFacePolicy
{
    /// <summary>
    /// Below this the release re-face writes nothing (and says nothing). Shared because
    /// <c>GrabbableModal</c> and <c>SurfaceGrabBar</c> each declared their own <c>0.5f</c> under the
    /// same name — the copy-by-name-and-value that this class exists to end.
    /// </summary>
    internal const float ReFaceEpsilonDeg = 0.5f;

    /// <summary>
    /// Does THIS release re-derive the facing? The two gates of the class doc, in order.
    ///
    /// <para>Logged once per release rather than silently, because "my window did not turn" and "my
    /// window turned" are the same complaint from opposite directions and the log has to say which
    /// rule decided it — the shared-window rule reads identically to the <c>Never</c> mode from the
    /// outside, and confusing the two would send the next round looking at the wrong file.</para>
    /// </summary>
    /// <param name="shared">Is this a shared (blue-bar) window whose orientation belongs to the
    /// whole room? Gate 1, which outranks the dial.</param>
    /// <param name="laserGrab">Was the gesture that just ended started with the LASER? Only read in
    /// the <c>LaserOnly</c> mode.</param>
    /// <param name="kind">The log prefix the owner already ships — <c>"MODAL WINDOW"</c>,
    /// <c>"SURFACE WINDOW"</c>. A parameter and not a constant so that every wording below stays
    /// byte-identical to the line that owner was already printing.</param>
    /// <param name="logName">The window's own log name.</param>
    internal static bool WantsReFaceOnRelease(bool shared, bool laserGrab, string kind, string logName)
    {
        WindowFaceMode mode = WorldUIConfig.WindowFacing != null
            ? WorldUIConfig.WindowFacing.Value
            : Defaults.WindowFacing;   // a release before Bind completed (scene load): ship the default

        bool wants;
        string why;
        if (shared)
        {
            wants = false;
            why = "it is a SHARED window, which the dial cannot reach";
            VRLog.Info("WorldUI", $"{kind}: '{logName}' released after a move and NOT re-faced " +
                                  "— it is a SHARED (blue-bar) window, whose orientation belongs to " +
                                  "the whole room. Turning it toward the player who moved it would " +
                                  "turn it away from everyone else, and it would silently disagree " +
                                  "with the pose this client just published on the wire. This is the " +
                                  "user's own rule ('Remote-Fenster (blau) sollen das gar nicht " +
                                  "haben') and it is NOT what [WorldUI] WindowFacing configures.");
        }
        else if (mode == WindowFaceMode.Always)
        {
            wants = true;
            why = "the dial is Always";
        }
        else if (mode == WindowFaceMode.Never)
        {
            wants = false;
            why = "the dial is Never";
            VRLog.Info("WorldUI", $"{kind}: '{logName}' released after a move and NOT re-faced " +
                                  "— [WorldUI] WindowFacing is Never, so a released window keeps " +
                                  "exactly the orientation it was let go at.");
        }
        else
        {
            // LaserOnly (default). A laser carry translates only — PanelGrabHandle's laser branch
            // writes position and returns — so the window arrives still facing the way it used to
            // and would be read edge-on; that is the case the release turn exists for. A HAND carry has
            // already yawed the window with the wrist for the whole drag.
            wants = laserGrab;
            why = laserGrab
                ? "the dial is LaserOnly and this was a LASER carry"
                : "the dial is LaserOnly and this was a HAND carry";
            if (!laserGrab)
                VRLog.Info("WorldUI", $"{kind}: '{logName}' released after a HAND move and NOT " +
                                      "re-faced — [WorldUI] WindowFacing is LaserOnly (the default): a " +
                                      "hand carry yaws the window with your wrist for the whole drag, so " +
                                      "the orientation you let go at is the one you aimed. A LASER drag " +
                                      "on the same window still turns toward you, because that carry never " +
                                      "rotates it at all.");
        }

        // HW-VERIFY
        VRLog.Note("WorldUI", $"WINDOW RE-FACE: {kind} '{logName}' released — " +
                              $"{(wants ? "RE-FACING" : "keeping its angle")}, because {why}. " +
                              $"[WorldUI] WindowFacing = {mode}, shared = {shared}, laser carry = " +
                              $"{laserGrab}. THIS LINE IS PRINTED BY EVERY OWNER THAT RE-FACES ON " +
                              "RELEASE — one policy, so a dial set to Never must produce 'keeping " +
                              "its angle' for modal windows AND for floating decision panels alike. " +
                              "Two different verdicts for the same dial in one session is the R1 " +
                              "defect (a second re-face site that never read the key) coming back.");
        return wants;
    }
}
