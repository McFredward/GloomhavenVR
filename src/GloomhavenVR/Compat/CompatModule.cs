using GloomhavenVR.Core;

namespace GloomhavenVR.Compat;

/// <summary>
/// Stereo/perf fixups on top of everything else: PPv2 kill-switches, EPOOutline/fog
/// stereo artifacts, scene variant handling (Game vs Game_gamepad), performance toggles.
/// Cross-phase (mainly Phase 1 and Phase 5). Registered last on purpose.
/// </summary>
internal sealed class CompatModule : IVRModule
{
    public string Name => "Compat";

    public void Init() => VRLog.Debug(Name, "stub initialized (Phases 1/5 implement compat fixups).");

    public void Shutdown()
    {
        // Stub — nothing to undo yet.
    }
}
