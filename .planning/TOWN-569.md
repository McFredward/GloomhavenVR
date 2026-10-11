# Merchant presentation, resident contact and story silence — ModBuild 569

The supplied Debug log identifies ModBuild 568. The five supplied screenshots establish four
presentation defects: the native merchant item list was independently floated behind the physical
resident, the priestess attentive silhouette retained an abrupt elbow/shoulder bend, the merchant's
thumbs entered his coat and belly, and returned item fronts could remain brown or incorrectly
oriented. The cloth report adds a contact failure after the simulated runner had moved away from its
authored rest surface.

## Changes

- Immersive service ownership now covers exact controllers and their descendant `UIWindow`s before
  generic modal conversion. An auxiliary already detached by an earlier frame is restored through
  its recorded original parent. Native controller state and transaction callbacks remain alive;
  disabling immersive services retains the original flat behavior.
- A returned merchant card synchronously releases stale hidden-window veil ownership only below its
  own renderer subtree. Parent restoration, relayout, return glide and the terminal home rotation
  remain separate owners, so repeated take-back cannot retain merchant-palm space or a reversed
  transform. Missing async art stays hidden until the original front is ready.
- Cloth contact uses the previous rendered solver surface as its contact frame, with the authored
  rest surface only as startup fallback. A deep finger push therefore keeps contact while the sheet
  moves instead of cancelling its own deformation on the next frame.
- The priestess attentive pose uses a narrower, lower anatomical elbow path from the imported
  clavicle and rests both hands beside her hips. The merchant retains the accepted whole-body pose
  while attentive thumb curl relaxes against the convex belly surface.
- Merchant confirmation opening selects the buy or sell voice family from the actual transaction
  direction. An unavailable temple visit selects one of five English explanations from the elected
  face author; cue identity, age and mouth curve use the existing synchronized face stream.
- `StoryComposite.PointOfNoReturn` is the single boundary for resident interaction. Attention,
  targets, offering poses, blessing reactions, activity sound and new speech stop there. Existing
  body and face solvers ease back to work/neutral, and an active cue is retired as cue zero so every
  observer stops the same utterance.

No wire grammar, record id, player setting or release version changed. Cue ids 56–60 append to the
existing `ushort` voice field and retain the elected-author presentation model. The town asset bundle
changed, so this build requires a full install.

## Validation

- The newly built shipping bundle is UnityFS 2021.3.5f1, contains 131 assets and is 97,482,850 bytes
  (`SHA256 2955b3149321024ebf151f65e7594433623b52094c6301c56fd93c5712a7818a`).
- Production-bundle renders cover front and side views of both attentive poses. The priestess's
  upper arms descend continuously from the clavicles without the reported lateral kink; the
  merchant's thumbs remain visible outside the coat and belly. Diagnostic magenta materials are an
  editor-shader limitation and were excluded from silhouette/contact assessment.
- The activity harness passes 629,150 assertions with 45 negative controls. Resident, face and
  voice harnesses pass 175, 2,089 and 3,049 assertions respectively, including local and remote
  point-of-no-return silence and relay ownership.
- The merchant handoff harness passes 1,344 assertions and 23 production/negative variants. It runs
  four art-ready palm-to-rotated-hand return cycles, fan close/reopen and replacement/cancel. Separate
  mutations prove renderer unveiling, canonical parent restoration and terminal rotation settle.
- The integrated guard passes all 14 structural source suites and all 79 local runtime/Unity suites;
  wire validation completes 286,569 assertions. Its final non-zero status is the expected compiled-
  form report against the older `080c505e9` pre-feature baseline: no guarded configuration, Harmony
  or log-token surface was removed.
- Strict integrated Release compilation reports zero warnings and zero errors. Documentation/i18n,
  both UnityFS bundle-format checks and `git diff --check` also pass.

These checks establish source ownership, native callback continuity, imported-bundle geometry and
shared state transitions. They do not establish headset cloth feel, D3D presentation, perceived pose
naturalness or multiplayer timing; those remain hardware outcomes.

## Hardware checklist

1. Buy, sell, cancel, reclaim and swap several items. No flat item list or confirmation window may
   appear behind the merchant. Reopen the wrist fan after each path; every card must keep its front,
   upright orientation and ordinary fan behavior.
2. Approach the priestess and merchant from the front and sides. Confirm the priestess has no abrupt
   shoulder/elbow kink and the merchant's thumbs follow the belly without entering the body.
3. Push and drag every table cloth with fingers after it has already deformed. The contact must
   remain physical and the sheet must continue to recover without passing through the table.
4. Attempt one unavailable donation. Confirm one fitting explanation, then leave and return to hear
   varied lines. In multiplayer every peer must hear the same take and see the same mouth timing.
5. Start both a purchase and a sale. Confirm the merchant addresses the correct direction and does
   not repeat the same line after the inventory mutation.
6. Enter the point-of-no-return story flow while residents are looking or speaking. All three must
   smoothly return to neutral, expose no interaction target and immediately stop speech on every
   peer for the entire committed story.
