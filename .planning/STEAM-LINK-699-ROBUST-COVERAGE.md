# Robust full-resolution native Steam Link coverage

## Scope and evidence boundary

The maintainer requests continuation after698's temporary v1 containment.
The target remains coverage with the same spatial resolution as contribution
RGB, without window classification, opacity thresholds or a modified PC sender.
The v2 failure review remains [authoritative for that rejected headset run](STEAM-LINK-698-EYE-DIVERGENCE.md).
Neither new offline proof nor a corrected format identifies the actual failed
run's unrecorded guard samples, codec settings or current R1 chart.

V3 changes only the format header. Scene and coverage remain2048-square in the
same3168-square windmill carrier, with identical source sampling, contribution
semantics, four strip orientations and16-pixel global halos. Frozen v1/v2 JSONs
and shaders remain intact. The new immutable contract is
`tests/steamlink-scene-payload/profile-v3.json`, byte-identical to the receiver
repository's `docs/protocols/scene-contribution-atlas-v3.json`:

- File SHA256 `cf5f71fb00877573a9d2aa8b69405ec7af03523563e216b4e07738605a11863a`.
- Canonical SHA256 `7efb2d98cc99d17d9bd920780797b6b647bc4b74441d2b97f8e72dc61f002521`.
- Signature SHA256 `681763eda11a55db2e43d18d52bf791a9c41720840471c0f9e473f71934786b9`.

## Correct the reproduced weakness

V2's twelve32-square cells and ±2-code windows were not robust to the owned lossy
codec comparison. A rejected packed frame follows exact native opaque output,
which then displays the atlas at another scale. V3 uses twelve128-square neutral
cells in two disjoint unused corners, separated by32 pixels. Its four possible
levels are32/96/160/224; deterministic signature-derived ordered values are
32,224,224,96,96,160,96,224,160,224,96,96. Inclusive ±16-code windows only identify
this new layout; they never turn fractional opacity into a binary mask.

The private proposal's final24-case comparison uses4040-square owned producers,
1152/576-square charts, three backgrounds, H264/HEVC and QP23/32. V3 passes24/24,
maximum3.909-code error, minimum12.091-code margin. The initial study's different
four-level draft is retained but is not substituted for these final numbers.
The same frozen v2 controls pass4/24. Another131 structural/partial/old-format
controls and26,834 finite synthetic ordinary vectors reject the required
negatives. These are bounded surrogates, not a measured natural-image false
positive rate, proprietary Steam Link codec proof or headset picture.

Root independently checks signature derivation, byte-identical geometry and all
17 scene/halo/header rectangles for overlap and canvas bounds. Larger flat cells
intentionally do not establish exact registration: small shifts can leave all
sampled centers inside their cells. Native current-image/R1 association remains
separate. No sticky header decision, previous-eye or mask queue is introduced.

## Delivery and validation plan

The production producer selects the new v3 shader from the rebuilt real bank.
The new native package selects v3 first, then frozen legacy v1, otherwise exact
original output. It retains12/13/2 guard-fetch budgets without a third v2 guard.
Historical `transform_dual` must remain byte-identical to the31960-byte v1/v2
candidate9ad1b56a20db039132ad6ec4557fe8ece3fff277316370bcefc9f70ec7aed367.
All ten original runtime fingerprints stay unchanged; only the owned candidate
and package/source pins intentionally change.

The actual Unity2021.3.5 compiled bank is181740 bytes, SHA256
`cb9fc34466df92c64f53f10f5ef3a64b9168de8933d5aaa0b3cf8c61cc6e5bf0`.
Its70 metadata checks and109 retained-shader comparisons establish all18 existing
compiled program banks/states are unchanged. The bounded paired source test
passes227 checks, including24 actual header cells and30 source/scene/mask probes
with zero measured error. Both sources are4040-square with actual8xMSAA and
independent content. The old/new packed pictures are byte-identical outside the
union of their headers, across both complete4040-square carriers.

The receiver candidate is31384 bytes, SHA256
`44c4a6c6bf7543b53d14ab6d350d8498143d6c03f70bfb45b9c6812e6b900a49`.
Seven focused new contract methods, unchanged historical-profile checks,23
package methods and13 session methods pass; one existing platform skip remains
explicit. C++ admission passes nine controls normally and with sanitizers.
A narrow measured-vector GPU replay passes77 checks/twelve eye cases, with no
validation errors. This last replay is not substituted for complete picture proof.

The new whole-picture fixture consumes the actual compiled producer PNGs without
repainting a header. Eight H264/HEVC QP23/32 conditions at1152/576 chart sizes
preserve both eyes through the actual native candidate. Each condition samples
30 source positions across the two eyes, including glass0.25/0.5/0.75 and
positive E at A0, plus real source-over and same-command ordinary OFF/recovery.
Maximum measured header error is3.909 codes; source RGBA error0.013006; native
output versus the decoded-raster oracle differs by at most0.0000275. GPU and
synchronization validation report zero errors. Its required actual v2/HEVC23
control reproduces accepted-left/refused-right with6.552-code right header error;
the coupled valid-stream criterion rejects it instead of treating fallback as a
successful valid stream. This directly detects the old regression.

The fixture explicitly reconstructs the original encoded-channel convention
from decoded RGB before providing a complete native Float texture. It models
the retained quarter R1 charts and an owned codec path, not the current headset's
decoder output, foveation, actual late metadata or proprietary encoder settings.
No new headset success is inferred. Evidence stays in main debug under
`steamlink-699-source` / `steamlink-699-composed` and in the receiver's ignored
`build/private-native-robust699-20261018`, `private-robust699-shipping` and
`private-robust-scene-stream-699-20261018`.

Strict net472 Debug/Release and actual Mono artifact/resource probes pass. Real
host and AArch64 release libraries compile with the actual release switches.
Three focused installer methods plus two new stopped upgrade preferences preserve
enabled/disabled, replace the old candidate and never restart/activate a service.
The controlled unit/runtime-verification boundaries are recorded; this is not
a real device update. Suite inventory is232 local/101 CI/16 source. Unrelated
movie/UI/gameplay/wire behavior inherits unchanged prior evidence; this is not
a new complete-suite pass.

No Frame installation, capture or restart occurred during preparation. That
preparation did not reuse697's completed deployment permission. After a fresh
explicit approval, public receiver82fb122 was cold-prepared on the Frame and
installed through the public helper, preserving the enabled preference and all
ten original runtime fingerprints. The exact31384-byte candidate is loaded from
the installed package. One approved SteamVR restart completed; the same current
invocation retains NRestarts0 and passes the complete native UI health gate.

The first health observation incorrectly pinned the service's initial MainPID0
and zero start timestamp before systemd populated them. A read-only recheck from
the original pre-restart journal/log cursors confirms all required startup
markers and process identities in that same invocation. The initial false alarm
and an intervening incorrect reader-cursor recheck remain in the private record;
neither caused another service action. This is a health-observation defect, not
evidence of a compositor crash or a visual passthrough result.

Private deployment/rollback receipts are retained in the receiver repository's
`build/private-robust699-deployment`. The exact predecessor package is backed up
on the Frame, and completed duplicate/compiler caches are removed. There is no
pixel capture, automatic expiry or rollback timer. The maintainer has been
signalled to stream Build699 and compare both eyes and window edges. The visible
hardware result remains pending; native status alone cannot establish it.
