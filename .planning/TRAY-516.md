# Pick tray page lifetime — build 516

## Report and evidence

The maintainer reports that a mandatory three-card discard correctly offers two
recesses on page 1 and only the left recess on page 2. After placing the final card,
the native confirmation buttons appear, but the empty right recess incorrectly
starts pulsing again. It must remain dark until the decision is confirmed or reset.

Current local evidence is release 1.0.3 / ModBuild 515, commit `fd76de86b`:
`.planning/debug/Player.log:42,97` and `LogOutput.log:17`. The files under
`debug/remote` are historical ModBuild 500 and do not establish current observer output.
There is no screenshot of this card issue; the supplied mixed-reality screenshot
belongs to the separate window lane.

The local log records the sequence twice:

- `LogOutput.log:637–727`: native three-card discard, field counts zero, one, two.
- `:733`: page 2/2 correctly asks for the remaining one card.
- `:737–739`: field count is now **one**, native maximum remains three, and the
  final native confirmation owns the banner.
- `:769–810`: the second character repeats the same sequence.

## Source cause and correction

`Rebuild` pruned every parked card from `_fieldCards`, decreasing
`_pickLockedCount` and removing `_pickExitFlown` at the same time. Earlier discard
pages intentionally fly into the pile and park **before** the native whole-pick
confirmation. A later rebuild therefore forgot the two still-selected cards from
page 1. With one visible card and a native maximum of three, the overlay arithmetic
then advertised the empty right recess. The same lost claims also prevented undo
from queuing the earlier pages' normal return flights.

The existing prune is extracted to `PrunePickField` with one ownership qualification:
a parked card remains bookkeeping when it belongs to the locked prefix and has the
explicit page-exit claim. Dead widgets and native deselections still retire; ordinary
historical parked burns still retire. The existing queued reopen guard remains.
`RelayoutField` and the input affordance pass already exclude these page-exit claims,
so retaining them neither reseats parked cards nor makes invisible cards grabbable.
The mode-exit/reset paths still clear all page state.

The wanted-slot path additionally refuses placement hints while the native
confirmation dialog stands, including the interval before a queued grab-to-reopen
actually cancels it. Once native cancel lands, the existing restart clears the page
prefix, queues all completed page returns and resumes page 1. Normal two-card
selection resumes after native confirm and the existing exit-animation barrier.
Integration review additionally identified native recycling of a locked page card:
`OnCardRecycling` removed the entry without decreasing the locked prefix, incorrectly
classifying the next visible card as locked. `RetirePickFieldCard` now updates the
prefix and exit claim before relayout and wrapper destruction. The production tests
cover first/middle locked entries, visible entries and absent cards.
No gameplay selection, native callback, wire record or card-face policy changes.

Remote boards receive the owner's exact wanted mask through existing board-UI
record 4: `WantedSlotMask` → sender overlay bits → `WantedGlowMask` →
`RemoteBoardFurniture.SetWanted`. No independent observer-side arithmetic or new
transport is needed. This is source-level parity evidence; current observer headset
output is not captured by the supplied historical remote files.

## Validation

- `bash scripts/pick-tray-tests.sh`: **245 production assertions**, **8 source
  bindings**, **6 runtime negative controls**. Extracts and executes the actual
  production prune, wanted-mask, placement target and restart methods. Tests one
  through eight total cards, odd/even pages, partial and complete batches, final
  confirmation, undo return claims, native deselection inside a locked prefix,
  stale/dead parked cards, queued reopen, two-card damage sacrifice/recovery,
  normal selection, modal/ownership/rest/end-flow gates and exit-animation waiting.
- Negative controls restore unconditional parked pruning, remove confirmation
  admission and retain deselected parked pages; the added controls remove recycle-prefix
  adjustment, retain a dead flight claim or incorrectly shrink the prefix for a live-page removal. Each fails its intended behavioral
  assertion; compilation failures are not accepted as a passing negative control.
- `bash scripts/ci-build.sh Release`: **zero warnings / zero errors**.
- Existing `bash scripts/burn-layout-tests.sh`: **204 assertions** and all **9**
  runtime negative controls pass alongside the new suite.
- `python3 scripts/check-partial-order.py`: 27 multi-part types / 197 parts,
  zero declared cross-part dependencies; no static initializer dependency.
- `git diff --check`: clean.

The integrator registers `scripts/pick-tray-tests.sh` beside the other production
harnesses in the local required tests and both hosted workflows, bumps the build
once for all lanes and runs the full integration gates. Automated checks establish
these transitions, not headset pixels; the next hardware pass should confirm the
last one-card page stays dark during confirmation, both reset recesses return after
undo, earlier page cards return visibly, and the observer board matches the owner.
