# NPC634 regression and resident-independence review

## Evidence and causes

The latest supplied singleplayer capture is Build633. Its `Player.log` records
seven offered cards with zero original selectable enhancement highlights. The
supplied `weisse_verbesserungspunkte.jpg` shows the empty white backing behind
the original points heading. Immutable inputs and raw-row references are in
`.planning/debug/npc634/inputs/` and `log-evidence.json`. The remote directory
still contains Build629; it is not paired hardware evidence for this repair.

Quiet preparation called `UITab.Activate` even when the native buy toggle was
already on. Real Unity toggles emit their callback only when their value changes.
Native `EnterShop` explicitly calls `ShowBuyOptions` in that already-on case.
Omitting that branch left mode NONE or retained SELL; the game's own
`HighlightButtons` filter then excluded every empty enhancement area. The old
quiet fixture incorrectly invoked the callback on every activation and hid the
regression. Preparation now follows both original branches exactly. No original
window opening, selected-character write or substitute enhancement controller
is introduced.

The serialized Info2235 header Image14852 is opaque white with no sprite.
Native `CardsDisplay.Display` normally assigns its portrait. Quiet preparation
intentionally avoids that flat-display step; the existing points update changes
text and header alpha, leaving the empty backing visible. Surface13 now uses the
existing reversible full-cover background filter. Original texts, icon layers,
layout and point updates remain; other sections retain their backgrounds.

## Local visits and multiplayer ownership

Quiet native source leases restore their exact source parent, activation and
ownership/event subscriptions when released. Mage cancellation and merchant
withdrawal release their respective transaction; ordinary departure returns the
offered card. Reentry establishes the original buy mode again. A held or pending
native transaction retains its source until return/cancel/commit; this protects
the real payment callback rather than reserving all residents.

The actual grant ledger stores owners/session/nonces separately at indices1–3.
`SetOffer`, `MayCommit` and `Release` address only the requested service. The
mirror independently elects each resident and preserves other residents when a
visitor closes or disconnects. A player using the merchant therefore cannot
block another player at the mage or temple. Temple grants are brief callback
arbitration, not occupation of the priestess. Observer construction is inert and
does not prepare native gameplay controllers or change the viewer's selection.

The corrected owner highlights/options continue through existing exact-original
publication. The points heading's enabled/background/text/icon state is read
from the owner and applied to the observer; no observer defaults rebuild it.
The original native counter fixture verifies four EN/DE pictures and5→4 updates,
with world pose and graphic corners checked before render recentering. The
visitor-local card and purse pre-drop guides remain the maintainer-approved
exceptions; offered cards, actual purses, options and confirmations remain shared.

## Focused validation and limitations

- Native mode fixture:225 assertions and3 causal controls. It binds the game's
  original tab callback, mode, selection, filters, highlights and option routing.
  The previous always-Activate branch reproduces the zero-highlight failure.
- Quiet lifecycle:171 assertions and2 affected controls; enhancement handoff
  outcome matrix:1,418 assertions. The local multi-resident cycle tests actual
  quiet-controller leases with a logical temple boundary, not full
  `TownServicePresentation`/donation orchestration.
- Original points heading:171 assertions and1 causal white-header control.
  Actual native layout and serialized10-node widget, production Surface/background
  filtering/restoration and TLV110 replay produce zero differing pixels across
  all four owner/observer comparisons. Equivalent editor TMP atlas, conversion
  staging and grab registration are explicit boundaries; full headset/book
  composition is not certified by those renders.
- Actual grant lifecycle:49 assertions and2 causal controls. Direct town vectors:
  153 grant assertions,50,277 codec assertions and12,975 original-pool assertions.
- Additional shared-interaction Unity proof:64,095 assertions on its passing
  production run, plus2 selected readiness causal controls. Counts in per-frame
  heartbeat loops vary with scheduling. Separate resident leases, partial-card
  partitions, cancellation/disconnect, native public-bank replacement and direct
  numeric readiness are exercised. Readiness generation is a declared handoff
  port; this transport fixture does not run tracked hands or actual NPC poses.

This additional review exposed three stale fixture assumptions, repaired without
relaxing production gates: orphan Images lacked active native Canvases, an empty
backing had no drawable mesh, and sequential simulated clients shared one local
public-author claim. The public fixture now uses its existing per-process identity
adapter. The old fast-offer fixture also expected private guide artwork to carry
shared readiness; it now exercises actual Kind6 numeric readiness and explicitly
rejects transmitted guide artwork. Two causal controls remove readiness and its
heartbeat independently. Failed original attempts are preserved.

No new complete146-local-suite pass is claimed. Final source/build/golden and
compiled receipts are in `.planning/debug/npc634/final/`; source-bound Frame634
proof is inherited from its reviewed integration at9fb092a72. Frame-owned files
remain byte-identical to that candidate. The unchanged broader NPC632 evidence
is inherited under the maintainer's focused-testing instruction. A new real
paired headset test on634 remains necessary to accept picture and latency.

New native-reference fixtures are developer tools, not unconditional hosted CI
entries: they require the private game data and read-only decompiled sources.
Run `python3 scripts/check-town-enhancement-mode.py` and
`python3 scripts/check-town-mage-counter634.py` from the initialized main checkout.
