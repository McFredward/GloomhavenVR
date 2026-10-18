# Build702 — remove the added Steam Link video ceiling

## Hardware diagnosis

The maintainer explicitly accepts Build701's corrected passthrough edges and
reports new substantial image degradation. Current Build701 Player/LogOutput
inputs are copied/hash-pinned under `.planning/debug/steamlink-702-review/`.
The source output is4040x4040 per eye; the successful quality lease requests and
reads back3072. Unlike the previously refused700 lease, this policy really ran.
Thus the fixed override introduced an independent lower video-resolution ceiling.

Eye-resolution edits above1.0 exceeded that process's existing allocation and
were deferred to VR restart. A0.65 viewport attempt was reset by the XR provider
after three repairs. Those logs do not prove that the user's slider changed the
actual image dimensions during this run; explain this separately from our new
fixed-width regression.

## Bounded repair

- Pure integer policy derives an upward64-aligned request from the greater of
  the two actual XR carrier axes:4040→4096,3408→3456,4848→4864. It never writes
  render scale, eye allocation, viewport, camera target, mask or atlas dimensions.
- The separate MR-width row disappears from both curated and Advanced menus.
  Its persistent config key/default is retained inert; no consumer reads Value.
- The helper's numeric request range becomes64..16384. Observed smaller immediate
  readbacks produce `width_limited` and owned rollback. Existing larger manual
  widths remain preserved. Managed admission requires matching or proven higher
  manual readback; new executable/provenance/embedded verifier pins agree.
- Temporary unknown XR extents retain only a currently eligible generation's
  last valid extent. Initial unknown dimensions, Off, session end and Reset never
  borrow a previous generation. This prevents gratuitous load-time video-policy
  changes without inventing a size or adding a timer.
- Keep the accepted uniform/no-sharpening edge policy, Build701 stereo-startup
  cohort and the entire v3 bank/receiver unchanged. No Frame action/update.

## Explicit limits

The v3 scene occupies2048/3168 of each carrier's linear budget. A4040 carrier
contains about2612 scene samples across; encoding the full carrier at3072 lowered
that budget again to about1986. The repair removes this additional loss. It does
not restore a pixel-for-pixel4040 scene inside that unchanged atlas: a larger
independent submitted carrier would be needed, not simply a larger encoder width.
Do not present settings readback as actual video negotiation, arithmetic bounds
as proven decoder limits, or a passing check as visible headset quality. A later
asynchronous driver clamp is not observable through the immediate receipt.

## Focused evidence

- Integer policy:98352 assertions, all valid axis bounds/non-square sizes, four
  compiled failing negative controls, strict net4720 warnings/errors.
- Native helper:19307 checks under ASan/UBSan, real C serializers including
  width-limited rollback, byte-identical shipping PE rebuild, rejected original
  blind-restore and missing-setting mutants. No real OpenVR/Windows/Proton call.
- Managed monitor/protocol:100 passing checks with strict net472 compilation include unknown
  extent lifecycle, exact new width checks and borrowed actual native transcripts.
- Options:69 assertions and six compiled semantic negative controls; both menu
  builders hide exactly the intentionally retired width key. Options coverage
  passes with one documented INERT exception.
- All16 source gates pass; public surfaces stay669config/241patches/4806log tokens.
- Suite census:236local/105CI/16source with the new policy in local+CI.
- Independent review: `.planning/debug/steamlink-702-review/` and native checkout
  `build/private-resolution702-review/`. Its transient-extent and final-pin
  findings are addressed in this integration.

Final build/resource/source receipts are retained in the scoped evidence folder.
Unchanged bank/GPU/wire/other presentation evidence is inherited from701; this
bounded repair does not repeat the whole gate or claim a hardware outcome.
