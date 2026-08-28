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
    internal const int MaskId = 0;                                                     // => [Net] MaskId
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
    // THE TWO UN-FADE DWELLS ARE THE WALL'S, BY THE USER'S WORD (2026-08-28: "Pass die default
    // configs fuer das transparent machen/ausblenden von fremden boards an die der Waende an").
    // They were 2.5 / 7 -- the values the wall fade shipped when this feature was hand-copied from
    // it, and the board's own class doc still called them "the wall's" long after they were not:
    // the wall's pair became 0.5 / 3.6 on 2026-08-26 from the user's own tuned cfg drop (84679759).
    // A hand-copy is a snapshot, and this is the same drift the ModBuild 310 unification removed
    // from the DECISION code; these two are the last of it that lived in the numbers.
    //
    // WHY THESE TWO TRANSFER AND THE BARS DO NOT: a dwell is SECONDS OF CONTINUOUS AGREEMENT --
    // the same unit and the same meaning on both subsystems, so one number really is the same
    // number. The Schmitt bars are a coverage FRACTION measured against different denominators
    // (the wall's is its room's WHOLE floor grid, the board's is the IN-VIEW play-field samples
    // only), so 0.35 on one is not the same physical situation as 0.35 on the other and copying
    // them across would be a retune wearing an alignment's clothes.
    internal const float PeerBoardExitDwellMoved = 0.5f;                               // => [PeerBoardFade] ExitDwellMovedSeconds
    internal const float PeerBoardExitDwellStationary = 3.6f;                          // => [PeerBoardFade] ExitDwellStationarySeconds
}
