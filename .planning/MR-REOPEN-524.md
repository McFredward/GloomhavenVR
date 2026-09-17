# Map-window MR reopening investigation — build 524

## Evidence

The user reports that the first merchant/map destination opening is correct and subsequent
openings have the same excessive upper background as `händler_mixed_reality.jpg`. The latest
local `LogOutput.log` and `Player.log` identify build 523. Remote logs identify historical
build 500 and cannot establish the current peer outcome.

Local `LogOutput.log` first shop opening, line 138: host 1920x1080, hit bounds y -540..540.
Second opening, line 168: same host, hit bounds y -540..1370. Later shop bounds reach
y 1938 and 2160; temple also reaches 2160. Capture allocation grows from 3904x2160 to
3416x2842 with a reported 544 authored-pixel vertical expansion. These measurements are
capture/input envelopes, **not actual MR backing measurements or named MR contributors**.

The previous `MR PLATE EXTENT` and `MR backings ON` messages used `VRLog.Info`, which is
Debug-only in this project. Their absence in the supplied normal-level log cannot establish
that MR was disabled. The virtual room visible behind the windows also cannot establish that.

## Audit and limits

Root conversion/release preserves parent, sibling, anchors, pivot, size and local pose.
Window materialisation restores renderer alpha and does not write native layout transforms.
Pooled native item tooltips screen-clamp themselves and can enlarge capture bounds, but the
MR walk excludes their known component families. Hidden-window summaries report zero drawable
disagreements and zero late lifts. A release-phase warning occurs on the first close; it can
also arise from stale `Camera.current`, so it is not established as causal.

Build 523's source corrections did not resolve the hardware report. No exact offending graphic
is identifiable from the available log. Do not claim a rendering fix or introduce another
speculative clamp that could crop genuine content.

## Diagnostic change

Bounded normal-level logs identify MR activation, the actual fitted rectangle and its native
extremal contributors. Target and host instance IDs distinguish a reused game window from
a new converted host. Contributor diagnostics reuse the actual MR union traversal and report
the clipped contribution, native sprite/material, renderer alpha and mask/group ancestry.
Unchanged samples do not format messages or perform extra hierarchy walks; state is weakly
owned by the conversion and records are capped. Diagnostic failures cannot invalidate bounds.

No native visibility, layout, capture, callbacks, animation, bundle or wire change is intended.

## Required hardware evidence

With build 524 and MR enabled, open the merchant once, close it, reopen it and wait briefly
for the oversized background. Repeat once with another affected map destination. Normal
logging suffices; save the logs from this run and a screenshot of the bad second opening.
This is a focused diagnostic reproduction, not a claim that the visible defect is fixed.

## Validation

Integrated strict Release passes with zero warnings and errors. The production-linked ink
and diagnostics harness passes 398 assertions, 23 runtime negative controls and one placement
binding negative. MR layout/accessor checks pass 258 assertions, 48 bindings and 16 negatives;
animation lifecycle checks pass 545 assertions, three bindings and three negatives. Eleven
frame-order locks, bilingual documentation checks, shell syntax and whitespace pass.

Config keys and patch surface remain 625 and 172; log tokens grow from 4,732 to 4,733 with
`MR BOUNDS SOURCE`. Existing tokens are preserved. Only affected local suites were run.
These checks establish diagnostic behavior and compilation, not a corrected headset image.
