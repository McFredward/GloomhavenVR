# Resident audio, cloth response and merchant return lifecycle — ModBuild 570

The supplied Debug log is from ModBuild 569. Its cloth probes show that production runners were
ready and real hand contacts occurred; the missing visible response was therefore downstream of
collision. The supplied screenshots show three separate presentation defects: the priestess still
passed through an unsuitable attentive pose, a returned merchant item retained a rotated native
face inside its backing, and the complete merchant lantern hung below the cabinet ring.

## Changes

- Merchant and enchantress lines now use `fal-ai/minimax/speech-2.8-hd`, the renderer used for the
  accepted priestess voice. Existing cue names, ordering and synchronized wire ids remain intact.
  The enchantress has five quiet, invented Latin-like casting phrases instead of English casting
  dialogue. Five restrained spell effects replace the loud native augment/UI sound; their choice is
  derived from the replicated activity clock so every peer selects the same take.
- A genuine cloth collision now presents the native PhysX displacement at full weight immediately.
  Build 569 applied a second 120 ms visual fade after PhysX had already smoothed the response, which
  made short fingertip contacts effectively invisible. Release still returns through the existing
  480 ms gravity-driven recovery. Added measurements remain bounded and Debug-only.
- The priestess's available attention pose puts both hands beside the robe with a narrow anatomical
  elbow path. When an unavailable state is already known on approach, prayer blends directly into
  the covered-bowl pose. A donation made while she is attentive uses the replicated transition age;
  every peer therefore evaluates the same intermediate pose.
- Physical item cards explicitly detach their native `ItemCardUI` into the original object pool
  before the chip host is destroyed. Spawn, merchant reclaim and steady maintenance restore the
  inner face's anchors, pivot, position, rotation and scale. This closes the exact leave, return and
  reopen path that preserved the grey backing and twisted card face.
- The normalized native merchant lantern moves four centimetres upward. Its complete 32 cm mesh now
  meets the authored cabinet ring while retaining clearance from the cabinet wall.

No wire grammar, cue id, setting or release version changed. The town asset bundle changed, so this
build requires a full install. Paid generation used 40 MiniMax speech calls, five ElevenLabs sound
effect calls and three audio reviews. Listed-price estimates total USD 0.17084275; the guarded upper
bound including two rejected pre-inference review requests was USD 0.1781. FAL did not return a final
billing receipt.

## Validation

- The shipping town bundle is UnityFS 2021.3.5f1, contains 136 assets and is 97,524,402 bytes
  (`SHA256 94dff01efecb45c2f106fd803458c1e6ce6eccf9980c8856922973a7c1865856`).
- Native cloth tests measure 8.252 cm of production contact response and at least 14.557 cm during a deep
  hold; null/dead controls remain at zero and the cloth does not fall through its table.
- Shipping-prefab renders cover front and side views of the available priestess. A 192-frame
  prayer-to-cover-to-prayer sequence shows no available-pose stop or one-frame pose edge.
- The merchant handoff harness passes 1,365 assertions and 27 negative controls, including an actual
  destroy, pool, recreate cycle using the same native object after deliberate pooled and live drift.
- The asset harness passes 951,251 assertions and nine visual negative controls. A render built from
  the original lantern meshes measures native bounds Y 1.27–1.59 m and a 0.00000003 m ring gap.
- Voice playback passes 3,046 assertions and 16 negative controls. The integrated guard passes all
  14 source suites and all 79 local runtime/Unity suites; wire validation completes 286,569
  assertions. Release compilation reports zero warnings and errors. Documentation/i18n, both bundle
  format checks and `git diff --check` pass. The guard's final non-zero status is the expected
  compiled-form report against the pre-feature `080c505e9` baseline; no guarded surface was removed.

Automated checks establish source ownership, native object survival, imported-mesh geometry,
deterministic shared timing and actual Unity solver movement. Perceived voice quality, spatial audio,
finger feel and the Windows D3D headset image remain hardware outcomes.

## Hardware checklist

1. Listen to several merchant and enchantress lines. Confirm consistent voices, quiet mystical
   non-English casting, varied restrained spell sounds and matching playback/mouth timing remotely.
2. Push and drag every resident table cloth with fingertips, including short taps. It must respond
   immediately, retain gravity and recover without entering the table.
3. Approach the priestess while a donation is available: both arms must hang naturally. Approach
   after donating: she must move directly from prayer to covering the bowl without that pose between.
4. Give the merchant an item, leave his area, return and reopen the wrist fan. The same card must
   show its front upright within the backing. Repeat with reclaim, cancel, purchase and replacement.
5. Inspect the merchant cabinet from several angles. The complete lantern hook must meet the ring
   and remain outside the cabinet wall while the cabinet moves or changes pages.
