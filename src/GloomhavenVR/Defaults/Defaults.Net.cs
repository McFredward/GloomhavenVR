// Shipped config defaults — one entry per line. The full rules, and the reason this family
// exists, live at the top of Defaults.Core.cs; they are deliberately not restated here.
//
// The trailing `// => [Section] Key` annotation is the machine-readable part:
// scripts/rebase-defaults.py finds an entry by it, so a line may move but its annotation
// must stay exact.


using UnityEngine;
using GloomhavenVR.Net;

namespace GloomhavenVR;

internal static partial class Defaults
{
    // ---- Net/NetModule.cs ----------------------------------------------------------
    internal const bool Net_Enabled = true;                                            // => [Net] Enabled
    internal const int MaskId = 2;                                                     // => [Net] MaskId
    internal const float MaskSize = 1.29148f;                                          // => [Net] MaskSize
    internal const bool NameTags = true;                                               // => [Net] NameTags
    internal const bool MirrorEnabled = false;                                         // => [Net] MirrorEnabled
    internal const bool Net_VersionGuard = true;                                       // => [Net] VersionGuard
    internal const RemoteBoardVisibility RemoteBoards = RemoteBoardVisibility.Always;  // => [Net] RemoteBoards
}
