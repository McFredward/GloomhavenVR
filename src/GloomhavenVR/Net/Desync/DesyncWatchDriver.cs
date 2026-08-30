using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Net.Desync;

/// <summary>
/// The seat for <see cref="DesyncWatch.Tick"/>. Its own GameObject rather than a ride on the
/// avatar driver, because the watch has to run when the embodiment sync is switched OFF: it
/// observes the GAME's netcode, and "turn the avatars off if multiplayer misbehaves" is exactly
/// the moment its report is worth the most.
///
/// <para>Routed through <see cref="TickGuard"/> like every other mod driver: a throw here is
/// isolated and attributed rather than becoming a per-frame anonymous NRE flood — and this
/// particular driver reads game statics that are legitimately absent for whole scenes.</para>
/// </summary>
internal sealed class DesyncWatchDriver : MonoBehaviour
{
    private static readonly System.Action TickFn = DesyncWatch.Tick;

    private void Update() => TickGuard.Run("Net.DesyncWatch", TickFn);
}
