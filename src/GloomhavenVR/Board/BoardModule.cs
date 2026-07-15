using GloomhavenVR.Core;

namespace GloomhavenVR.Board;

/// <summary>
/// Board touch/ray targeting: hex/actor/door/chest picking, AoE placement & rotation.
/// Phase 3a (feat/board). Key seams: MF.FindInteractableAtMousePosition,
/// InputManager.CursorPosition, TileBehaviour.s_Callback commit path.
/// </summary>
internal sealed class BoardModule : IVRModule
{
    public string Name => "Board";

    public void Init() => VRLog.Debug(Name, "stub initialized (Phase 3a implements board targeting).");

    public void Shutdown()
    {
        // Stub — nothing to undo yet.
    }
}
