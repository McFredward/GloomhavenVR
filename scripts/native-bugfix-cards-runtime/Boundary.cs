namespace GloomhavenVR.Core
{
    // The only production boundary: whether the independent VR session is running.
    internal static class VRSession { internal static bool IsRunning = true; }
    internal static class VRLog
    {
        internal static int Failures;
        internal static bool Throw;
        internal static void Warn(string scope, string text)
        {
            Failures++;
            if (Throw) throw new System.InvalidOperationException("explicit diagnostic boundary failure");
        }
    }
}
