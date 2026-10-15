namespace GloomhavenVR.Core.SteamLink
{
    internal enum ProbeEye { Unknown, Left, Right }
    internal enum ProbePhase { Prepared, Running, Stopped }

    // Pure admission/lifecycle code shared with portable controls, not an XR implementation.
    internal sealed class ProbePolicy
    {
        public const string HeadCameraName = "GloomhavenVR.HeadCamera";
        public ProbePhase Phase { get; private set; } = ProbePhase.Prepared;
        public bool IsRunning => Phase == ProbePhase.Running;

        public bool Start(bool uniqueNamedCamera, bool builtIn, bool xrActive,
                          bool multiPass, bool bothEyes)
        {
            if (Phase != ProbePhase.Prepared || !uniqueNamedCamera
                || !builtIn || !xrActive || !multiPass || !bothEyes)
                return false;
            Phase = ProbePhase.Running;
            return true;
        }

        public void Stop() { Phase = ProbePhase.Stopped; }

        public ProbeEye Select(bool sameAttachedCamera, bool currentCameraMatches,
                               bool builtIn, bool xrActive, bool multiPass, bool bothEyes, ProbeEye eye)
        {
            if (!IsRunning || !sameAttachedCamera || !currentCameraMatches || !builtIn
                || !xrActive || !multiPass || !bothEyes)
                return ProbeEye.Unknown;
            return eye == ProbeEye.Left || eye == ProbeEye.Right ? eye : ProbeEye.Unknown;
        }
    }
}
