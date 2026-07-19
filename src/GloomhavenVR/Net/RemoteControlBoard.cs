// FILLED BY WORKER B — do not add logic here.
// Foundation stub: exact signatures only, all bodies are strict no-ops so RemoteAvatar
// compiles and owns/ticks/destroys it until the remote control-board visual lands.

namespace GloomhavenVR.Net;

/// <summary>
/// A read-only visual of a remote player's control board at <c>owner.BoardPosition/Rotation/
/// BoardScale</c>, gated by <c>NetModule.RemoteBoards</c> + the anti-cheat <see cref="RevealGate"/>.
/// STUB — worker B fills the body.
/// </summary>
internal sealed class RemoteControlBoard
{
    public RemoteControlBoard(RemoteAvatar owner) { }

    public void Tick(float dt) { }

    public void Destroy() { }
}
