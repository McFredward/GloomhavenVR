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

    // ---- Net/PeerBoardFade.cs ------------------------------------------------------
    // THESE SIX WERE LITERALS AT THE BIND CALL until the 2026-08-22 settings audit found them
    // (§6 "Housekeeping"). That is not a style point: scripts/rebase-defaults.py joins the tuned
    // cfg drop against the annotated lines in this folder and REFUSES to guess — "a cfg entry it
    // cannot map to exactly one annotated Defaults line is REPORTED, not silently skipped". With
    // the numbers living in PeerBoardFade.Bind there was no line to map to, so the first tuned
    // dev.gloomhavenvr.boardfade.cfg would have come back UNMAPPED and the tuning would have been
    // dropped on the floor at the next re-base — the exact failure mode that cost six builds on
    // [Water] RippleSpeed. The values are the shipped ones, unchanged, moved verbatim.
    internal const PeerBoardFadeMode PeerBoardFade_Mode = PeerBoardFadeMode.Off;       // => [PeerBoardFade] Mode
    internal const float PeerBoardOccludedAlpha = 0.25f;                               // => [PeerBoardFade] OccludedAlpha
    internal const float PeerBoardOnFraction = 0.12f;                                  // => [PeerBoardFade] OnFraction
    internal const float PeerBoardOffFraction = 0.05f;                                 // => [PeerBoardFade] OffFraction
    internal const float PeerBoardExitDwellMoved = 2.5f;                               // => [PeerBoardFade] ExitDwellMovedSeconds
    internal const float PeerBoardExitDwellStationary = 7f;                            // => [PeerBoardFade] ExitDwellStationarySeconds
}
