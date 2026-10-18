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

Focused final evidence will include real compiled producer bank and independent
Left/Right4040-square sources with actual8xMSAA, the original refusal controls,
bounded measured-guard GPU controls, an actual producer PNG → lossy codec →
actual new native fragment paired-eye regression fixture, and strict final DLL
resource identities. Its source/native codec conversion and R1 surrogate limits
must remain explicit. Unrelated movie/UI/gameplay/wire behavior inherits its
unchanged prior evidence; this is not a new complete-suite pass.

No new Frame installation, capture or restart occurs during preparation. The
installed old receiver does not recognize v3. Both sides must update before a
new streamed headset test. Preparing code does not authorize device writes or
reuse the completed697 deployment permission; an additional concrete approval
is required when the new package, backup and manual rollback are ready.
