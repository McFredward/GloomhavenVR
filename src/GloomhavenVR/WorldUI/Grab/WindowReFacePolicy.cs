using GloomhavenVR.Core;

namespace GloomhavenVR.WorldUI;

/// <summary>One release-only facing policy for every window owner. The2026-10-29
/// multiplayer review supersedes the old shared-window veto: the last accepted
/// releaser may turn a shared window, and that author's animated pose is replicated.
/// Ownership arbitration belongs to the shared controller, not this config rule.
/// The target is captured once; head movement never steers an ongoing or settled window.</summary>
internal static class WindowReFacePolicy
{
    internal const float ReFaceEpsilonDeg = 0.5f;

    /// <summary>The releaser's normal dial applies to its accepted gesture. Each observer
    /// follows that author's pose rather than consulting its own facing preference.</summary>
    internal static bool WantsReFaceOnRelease(bool shared, bool laserGrab, string kind, string logName)
    {
        WindowFaceMode mode = WorldUIConfig.WindowFacing != null
            ? WorldUIConfig.WindowFacing.Value : Defaults.WindowFacing;
        bool wants = mode == WindowFaceMode.Always || (mode != WindowFaceMode.Never && laserGrab);
        string why = mode == WindowFaceMode.Always ? "the dial is Always"
            : mode == WindowFaceMode.Never ? "the dial is Never"
            : laserGrab ? "the dial is LaserOnly and this was a LASER carry"
            : "the dial is LaserOnly and this was a HAND carry";
        if (mode == WindowFaceMode.Never)
            VRLog.Info("WorldUI", $"{kind}: '{logName}' released after a move and NOT re-faced " +
                                  "— [WorldUI] WindowFacing is Never, so a released window keeps " +
                                  "exactly the orientation it was let go at.");
        else if (!wants)
            VRLog.Info("WorldUI", $"{kind}: '{logName}' released after a HAND move and NOT " +
                                  "re-faced — [WorldUI] WindowFacing is LaserOnly (the default): a " +
                                  "hand carry yaws the window with your wrist for the whole drag, so " +
                                  "the orientation you let go at is the one you aimed. A LASER drag " +
                                  "on the same window still turns toward you, because that carry never " +
                                  "rotates it at all.");
        // HW-VERIFY
        VRLog.Note("WorldUI", $"WINDOW RE-FACE: {kind} '{logName}' released — " +
                              $"{(wants ? "RE-FACING" : "keeping its angle")}, because {why}. " +
                              $"[WorldUI] WindowFacing = {mode}, shared = {shared}, laser carry = " +
                              $"{laserGrab}. THIS LINE IS PRINTED BY EVERY OWNER THAT RE-FACES ON RELEASE. " +
                              "Shared releases are authored by the accepted releaser; " +
                              "observers retain that same streamed world pose.");
        return wants;
    }
}
