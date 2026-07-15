using GloomhavenVR.Core;

namespace GloomhavenVR.Hands;

/// <summary>
/// Hand presence and interaction primitives: hand models, finger curling, poke/ray/grab,
/// palm gate, haptics, VR event bus. Phase 2 (feat/hands).
/// The primitives defined here become the frozen interface consumed by Cards/Board/WorldUI.
/// </summary>
internal sealed class HandsModule : IVRModule
{
    public string Name => "Hands";

    public void Init() => VRLog.Debug(Name, "stub initialized (Phase 2 implements hands & interaction primitives).");
}
