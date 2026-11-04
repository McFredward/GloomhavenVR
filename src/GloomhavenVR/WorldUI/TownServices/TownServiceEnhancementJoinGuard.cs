using System.Collections;
using System.Reflection;
using FFSNet;
using GloomhavenVR.Core;
using HarmonyLib;

namespace GloomhavenVR.WorldUI;

/// <summary>Keep the native joining-player refusal ahead of its confirmation latch.</summary>
/// <remarks>
/// UINewEnhancementWindow.ConfirmSell sets _isConfirmationBoxOpened before checking
/// whether another player is joining. Its early return then creates no dialog and
/// no confirm/cancel callback can clear that flag. The current card and subsequent
/// visits consequently retain a confirmation which never existed. Refuse at the
/// same original admission boundary before that write; a later explicit click
/// retries normally. No transaction, readiness, native exception or join is changed.
/// </remarks>
[HarmonyPatch(typeof(UINewEnhancementWindow), "ConfirmSell")]
internal static class TownServiceEnhancementJoinGuard
{
    // The original collection is List<BoltConnection>. Read its count through
    // ICollection, as the existing quest roster does, without adding a compile-time
    // dependency on the game's network transport implementation.
    private static readonly PropertyInfo? JoiningPlayers = typeof(PlayerRegistry).GetProperty(
        "JoiningPlayers", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

    [HarmonyPrefix]
    internal static bool Prefix()
    {
        if (!VRSession.IsRunning || !WorldUIConfig.ConversionActive)
            return true;
        return !(FFSNetwork.IsHost && JoiningPlayers?.GetValue(null) is ICollection joining && joining.Count > 0
            || FFSNetwork.IsClient && PlayerRegistry.OtherClientsAreJoining);
    }
}
