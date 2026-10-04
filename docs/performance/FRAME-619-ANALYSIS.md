# Build618 Frame capture and Build619 targets

Evidence captured from `.planning/debug/steam_frame` on 2026-10-04 is preserved in
`.planning/debug/frame619/inputs` with SHA-256 hashes. LogOutput and Player banners
both identify 1.1.0, ModBuild618, commit c108555cd. No new screenshot accompanies
this capture. Existing user descriptions are observations, not inferred pixels.

## Separate loading from ordinary play

This run contains two Game/ProcGen loads and a CampaignMap interval. It is not a
single matched scenario benchmark. The preparation spinner times out twice at
its 90-second safety ceiling (Player lines 8581 and 24234). Native load completion
and visible play precede these timeouts. The old queue handled even already-warm
widget/sprite metadata one item on each alternating frame, and portrait owners
could each wait anew for 30 seconds. These source paths explain prolonged
preparation without establishing the exact duration of each individual resource.

Build619 batches cheap/cache-hit jobs under count/time bounds, permits only one
potentially cold texture/geometry operation per frame, deduplicates original
owners and gives portrait discovery a shared absolute deadline. Native async
loading priority is restored when actual native loading ends. Progress receipts
are bounded Debug diagnostics, not an ordinary-player per-frame stream.

New native room visibility events now cover real procedural/material requests
and presentation queues. They append preparation for new actors without retiring
existing local/remote caches or writing gameplay/loading flags. Camera movement
and routine periodic scans cannot re-arm the spinner by themselves.

## Remaining frame cost

The production frame report contains 39 summary windows, including loading and
transitions. The first loaded scenario's later windows remain roughly 52–60 ms
per frame with 31–36 ms logic. The map interval is roughly 32–54 ms. The second
scenario's late windows vary 54–81 ms, with a 97.9 ms transition window. Mixing these
windows into one improvement percentage would confound scenes, viewpoint,
preparation and native loading. XR runtime intervals/waits are not GPU-busy
measurements, and nested native/mod attribution cannot be added together.

The capture establishes that ordinary play is still expensive. Build619 addresses
the requested loading/effect/readability defects; it does not establish a new
headset FPS result. Test after loading with the existing bounded per-setting
measurements, retaining scene and settings identities.

## Whole-game effects and wall/bar findings

The old figure effect classifier named four demon families. Original LivingSpirit
prefabs contain 13 particle renderers and three additional glow surfaces, so that
scope could not meet the user's whole-game requirement. The new read-only native
prefab/PCG census supplies exact provenance across heroes, summons, normal/elite
enemies, bosses and scenery. Unknown bodies and gameplay-significant particle
solvers remain intact. Coverage is not a claim that every original asset can
safely disappear.

Wall delivery receipts use actual native HIGH/toggle shaders, with zero cheap
wall shaders. The first scenario also reports zero simplified environment
materials. Therefore the simple environment shading setting cannot by itself
explain that scene's reported wall pop. The original HIGH shader's handling of
continuous masking requires separate delivery/pixel analysis, documented in the
wall lane report. Native Windows shader bytes are distinct from Linux proof
surrogates; headset transition parity must be rechecked.

The original bar's circled number is TMPUGUI. Writing Graphic.material does not
update TMP's fontSharedMaterial/materialForRendering route. Build619 applies the
depth material through the actual text/submesh routes while retaining clipping,
masking and original-material restoration. Actual Unity wall/text pixel checks
provide stronger evidence than source-only shader names; they still do not
replace the Frame hardware image.

Companion reports: [loading](FRAME-619-LOADING.md),
[detail coverage](FRAME-619-DETAIL-COVERAGE.md),
[bars/walls](FRAME-619-BARS-WALLS.md),
[graphics audit](FRAME-619-GRAPHICS-AUDIT.md).
