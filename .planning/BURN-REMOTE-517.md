# Build 517: remote burn continuity

## Evidence and scope

The new report describes a short-rest card returning to blue after its burn has begun,
then restarting. Supplied hardware evidence remains local build 515 and historical remote
build 500; neither establishes the newest build's precise call sequence. The following
are source-proven replay/reset paths, independently of the owner's native restart fix.

## Changes

- `RemoteBurnFx` previously keyed burn discovery by `AbilityCardUI` and removed claims
  whenever a widget temporarily disappeared from the pile UI. Replacing a pooled widget
  for the same lost original therefore admitted another presentation. Discovery now
  keys the original `CAbilityCard` and retains its claim while that original remains in
  either lost model population. UI gaps, replacements, duplicate wrappers and a move
  between lost/permanently lost populations cannot restart a burn. Actual recovery
  relinquishes the claim so a later independent loss can play normally.
- Initial/focus seeding includes the complete lost model population, including originals
  whose UI has not materialised. Loading their historical widget later is not a burn.
  Invalid/placeholder widgets do not consume a genuinely new pending loss.
- `RemoteCardArt.ApplyNativeAppearance` previously retained a missing-address picture
  only on a recess and only before its first Lost sample. Once that sample arrived,
  another card's pile-count change or an empty native UI frame erased the material state
  to blue. Every remote surface now retains already painted owner output for the same
  still-lost original while awaiting its next valid native sample. Recovery and actor/card
  retargeting still release the old output. No animation clock, material value or native
  frame is fabricated, rewound, clamped, or replayed by this change.

## Other paths checked

Short/long rest, damage sacrifice, played ability loss and expired active cards share the
original-identity discovery and native appearance paths above. The flight release remains
causal on the owner's completion watermark and fully presented native output; this change
does not dispatch a second flight. Duplicate terminal receipts, historical completion on
joining, delayed owner progress and actor-switch layout holds remain covered by the
existing regression harness. `RemoteCardFx` already keeps launched flights moving while
queuing later ones behind an unfinished burn. Item appearance uses its separate native
output stream and does not enter this ability-card discovery ledger. Remote card clones
remain inert; no gameplay controllers or native burn coroutine are enabled on them.

Legacy `DriveUsedCardFx`, `TickUsedCardFx` and `DriveHeldCardLook` helpers have no callers;
current owner material snapshots remain authoritative. Existing reveal rules, including
covered remote short-rest flights and the public 3D map, are untouched.

## Validation

- `bash scripts/remote-burn-sequencing-tests.sh`: 108 production-path runtime assertions,
  all 21 negative controls rejected. New tests execute the actual production discovery,
  native appearance and retargeting methods with narrow rendering/model adapters. They
  cover five successive UI replacements/gaps, stale recovered UI, real re-burn, lost-pile
  transfer, historical delayed widgets, pending placeholders, duplicate wrappers, and
  six presentation surfaces losing/resuming owner samples and then recovering/retargeting.
- `bash scripts/ci-build.sh Release`: 0 warnings, 0 errors.
- `git diff --check`: clean.

A headset test is still required to establish end-to-end visual continuity. In particular,
this change cannot hide or correct a reset that genuinely originates in an owner's native
output; that must be prevented at its local source and then mirrored unchanged.
