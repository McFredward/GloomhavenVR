# Town service hardware follow-up — ModBuild 563

The supplied local `LogOutput.log` identifies ModBuild 562. The files under
`.planning/debug/remote/` still identify ModBuild 500 and therefore are not matching evidence
for this round. The five screenshots in `.planning/debug/npc_probleme/` show the priestess's
visitor hands held at table height, alternating brown/back-facing cards in the merchant item
fan, the ordinary ability-card fan in front of the priestess, unequal merchant-palm card sizes,
and a rigid altar runner crossing the table and the enchantress's legs. These are hardware
observations of build 562; every visual change below still requires a headset retest.

The donation log reaches the original temple ritual but then records the same bounded warning
six times: the original confirmation button stayed unavailable and the pending donation was
cancelled. Decompiled game source explains the repeat. In gamepad mode the confirmation box
does not attach its continuation to the visible button's `onClick`; polling and clicking that
button can therefore never advance. The physical donation now enters the confirmation box's
own `OnConfirm()` method after the existing ownership, item, affordability and session guards.
That method preserves the game's hidden transition, delayed callback and transaction authority.
Foreign town-service state also clears the stale temple approach latch before a retry, so a
merchant visit cannot permanently replace the donation purse with the ordinary card fan.

All free merchant-item return paths now converge before layout. The card first returns to the
owned-item fan root, preventing a palm-relative parent from turning canonical fan coordinates
into a brown back or rotated card. If the original asynchronous front is temporarily absent,
the card remains at zero scale with its collider disabled until that original art arrives. The
same path covers reclaim, distance cancellation, completed transactions and inventory refresh.
Owned-item and cabinet-stock offerings derive one physical palm width from the cabinet card,
so their presentation size no longer depends on their source.

The altar runners now use Unity 2021.3's native PhysX `Cloth` instead of the previous two-edge
spring. A single 25-by-13 source layer avoids simulating the imported Solidify back shell against
the front. The approved visible mesh and its decorations follow the complete driver surface.
Curved capsule rows cover the table lip and top support; tapered palm-to-fingertip capsules cover
both local hands and up to three peers, with local and remote head probes retained for mask
contact. Every generated collider is on Ignore Raycast and cannot become an invisible laser
target. Existing shared cloth controls remain additive owner correction; no wire record changes.
The render path takes one allocating `Cloth.vertices` snapshot per runner, then reuses it for
visible deformation, owner measurement and observer correction. Three active runners at 90 Hz
produce about 1.05 MB/s of short-lived vertex arrays because this Unity version exposes no
non-allocating Cloth vertex API; hardware CPU and GC profiling remains required.

Priestess attention now targets both hands below the table edge and gives the elbow solver a
downward route rather than a table-bracing pose. Merchant cabinet cards use the existing
`CardArtWatch` and mip-bake path that prevents the one-frame aliased source art on other physical
cards. Capture, per-frame arrival polling and pool restoration preserve original artwork.

The remaining sound on service entry and exit was the normal map fan's automatic close/reopen
edge while town inspection temporarily replaces it. Exactly that automatic edge is suppressed
when an actually open fan is replaced or restored; manual fan and card sounds remain unchanged.
Cabinet category/page motion now has a restrained spatial mechanism sound keyed by its already
shared turn epoch, including observers, while late observers do not replay an old start. Two
independent, default-on VR options control immersive NPC speech and NPC/furniture sound effects.
They are local listening preferences: disabling either stops playback immediately without
changing the shared cue, facial timeline, cabinet state or another player's audio.

The completed listening revision replaces all fifteen priestess cues with performances derived
from one immutable close-miked elderly female embedding. It also replaces the broken
`merchant-sell-2.wav` take (`Sold. Here is a fair price.`) from a close, calm, deep merchant
reference. Rhubarb regenerated every affected mouth curve from the final WAV. Local tiny.en ASR
recovered every intended sentence; all revised clips are mono 24 kHz, cluster around -27 dBFS
RMS and contain no clipped samples. Independent audio analysis describes the final merchant take
as close, clean, natural, middle-aged and calmly conversational, with the complete sentence
intelligible. It describes the final priestess as close, elderly, feminine, smoky, hoarse, gentle
and devotional, without echo, processing or glitches. Automated listening does not establish
perceived voice quality in a headset, so both residents remain on the hardware checklist.

Focused validation before integration passed 198 temple runtime assertions plus 18 mutation
controls, 1,278 merchant-handoff assertions plus 14 mutation controls, 466 options assertions
plus six controls, 2,834 voice assertions plus twelve controls, 180,270 activity assertions plus
thirty controls, and 22,892 catalog/cabinet assertions plus 31 controls. The native Unity cloth
player measured gravity displacement 0.08488 and a hand-collider sweep 0.07007 against a 0.00008
no-collider control; six source mutations cover local/remote hands, local/remote heads, table
support, Ignore Raycast and the single-snapshot boundary. The strict Release build was clean in
each isolated lane. Final integration, wire, bundle and complete-suite results are recorded below.

Automated checks cannot establish the final drape against the imported furniture in stereo,
the perceived strength of finger and mask contact, the priestess arm silhouette, async artwork
arrival in the real game, completed native donation, four-player cloth convergence or headset
frame-time/GC peaks. Those remain the build-563 hardware checklist.

Final integration validation passed all 14 source groups, all 78 local suites and 286,560 wire
assertions. `ci-build.sh Release` completed with zero warnings and zero errors; documentation
localization and both bundle-format checks passed. The rebuilt `ghvr-town.bundle` was 96,813,959
bytes in that pre-audio integration pass. After the final speech revision, the focused voice
suite passed 2,801 runtime assertions and twelve negative controls. Unity rebuilt the final
bundle at 96,777,432 bytes with SHA-256
`ec354624f16da369b95ac1214311b3da1218536c9c8652c1e3b4bcb82dfc7411`; its UnityFS format check
passed for Unity 2021.3.5f1.
The refactor guard completed those checks and then returned its expected nonzero result because
the feature branch differs from the private `080c505e9` compiled-form baseline: no tracked
configuration, Harmony-patch or log-token surface was removed; the current comparison contains
129 changed and 211 added/removed compiled files.
