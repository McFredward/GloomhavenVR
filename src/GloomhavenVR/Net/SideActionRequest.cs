using System.Reflection;
using GloomhavenVR.Core;
using HarmonyLib;

namespace GloomhavenVR.Net;

/// <summary>
/// THE ONE REFLECTED CALL TO <c>FFSNet.Synchronizer.SendSideAction</c> FOR A CLIENT-TO-HOST
/// REQUEST — shared by every mod feature that lets a non-host player ask the host to press
/// something.
///
/// <para><b>WHY IT IS SHARED AND NOT COPIED.</b> There are now two such features
/// (<see cref="EncounterChoice"/>, <see cref="EnemyInfoContinue"/>) and the resolution, the
/// degrade-to-nothing contract and the eight-parameter argument order are identical in both. The
/// mod-wide ruling is to avoid a second mechanism beside one that already exists, and an argument
/// ORDER copied into two files is exactly the kind of duplicate that goes wrong silently: the two
/// data ints and the bool are positional and untyped through reflection.</para>
///
/// <para><b>WHY REFLECTION AT ALL.</b> Same reason <see cref="FfsNetTransport"/> gives: the mod
/// keeps ZERO compile-time dependency on the Photon-Bolt assemblies, and
/// <c>SendSideAction</c>'s second parameter is an <c>IProtocolToken</c> — a Bolt type. Every caller
/// here passes null for it, so nothing in this file names one.</para>
///
/// <para><b>WHAT IT DOES NOT DO.</b> It does not decide whether a request MAY be sent. Whether the
/// far side will honour it, whether this client has a <c>PlayerRegistry.MyPlayer</c> yet and
/// whether the session has already desynchronised are the caller's terms, because the caller is the
/// one that has to name them in its own refusal line.</para>
/// </summary>
internal static class SideActionRequest
{
    private static MethodInfo? _sendSideAction;
    private static bool _resolved;

    /// <summary>
    /// Reusable argument array. A press is event-driven and the invoke below is synchronous on the
    /// Unity main thread, so one array cannot be observed half-written by another caller.
    /// </summary>
    private static readonly object?[] Args = new object?[8];

    /// <summary>
    /// Did <c>FFSNet.Synchronizer.SendSideAction</c> resolve on this game build? FALSE turns every
    /// request feature off rather than throwing into the game's dispatch — an unexpected game build
    /// leaves the flat game's host-only behaviour standing, which is always a correct fallback.
    /// Logged ONCE, by name, so a whole class of "my press did nothing" reports is answered by one
    /// line at startup rather than by a silence.
    /// </summary>
    internal static bool Available
    {
        get
        {
            if (!_resolved)
            {
                _resolved = true;
                _sendSideAction = AccessTools.Method("FFSNet.Synchronizer:SendSideAction");
                if (_sendSideAction == null)
                    VRLog.Alert("Net", "SIDE-ACTION REQUEST: FFSNet.Synchronizer.SendSideAction "
                                     + "did not resolve on this game build, so no non-host "
                                     + "player's press can be forwarded to the host. The flat "
                                     + "game's host-only behaviour stands and nothing else "
                                     + "changes.");
            }
            return _sendSideAction != null;
        }
    }

    /// <summary>
    /// Send one request. <paramref name="actionType"/> is a boxed <c>FFSNet.GameActionType</c>.
    /// FALSE means it did not travel and the caller must say so rather than pretend it did.
    /// </summary>
    /// <param name="canBeUnreliable">Always false for a request: a dropped press is a stuck
    /// screen, so these ride Bolt's ReliableOrdered channel.</param>
    internal static bool Send(object actionType, int targetPlayerId, int dataInt, int dataInt2,
                              bool dataBool, bool canBeUnreliable = false)
    {
        if (!Available || _sendSideAction == null)
            return false;
        Args[0] = actionType;
        Args[1] = null;             // supplementaryDataToken (IProtocolToken): none
        Args[2] = canBeUnreliable;
        Args[3] = true;             // sendToHostOnly: this is a REQUEST, to the authority
        Args[4] = targetPlayerId;
        Args[5] = dataInt;
        Args[6] = dataInt2;
        Args[7] = dataBool;
        _sendSideAction.Invoke(null, Args);
        return true;
    }
}
