# Priestess purse inspection and concurrent observers — ModBuild 582

The latest supplied hardware `LogOutput.log` has a ModBuild 580 banner. It shows
Temple openings and closings, but no `Temple purse release` or native donation
completion line. It cannot establish a runtime purse-drop cause for build 581.
The source path did establish a pickup defect: the fan presented an unavailable
or already-used purse, while `TownServiceRitual.Piece` still used native payment
eligibility for the token's `inspect` predicate and explicitly disabled its
physical collider. `TownServiceTempleOffering.AllowsHand` independently required
`Available` and `CanLocalBeginTransaction(2)`. A purse could therefore be
visible but ungrabbable if the character lacked gold, had already donated, the
native input was temporarily disabled, or another visitor had the priestess's
transaction.

Pickup now follows the current revealed Temple hand fan and its selected
controlled character. The existing fan `CanvasGroup` still prevents invisible
pickup. Native blessing availability, the per-NPC transaction claim, identity,
context, gesture, bowl volume, and callback grant remain in the **drop/commit**
path. An ineligible physical release returns the same purse without starting a
donation. The local bowl guide still lights only for an eligible held purse.
The existing native confirmation and original callback remain the only writers
of gameplay donation state.

The prior successful-donation sink left the token permanently settled at zero
visibility until character context changed. Once its complete 0.28-second sink
has reached zero opacity, the token now restores the original purse to the fan
seat. The native row stays unavailable for another donation, but the owner can
inspect and lift the returned purse. Neither a second callback nor a premature
flash during the sink is introduced.

The ordinary private town-service mirror elects one visual author per NPC.
That is correct for the shared stand and UI but would hide a second visitor's
own held purse. A held purse now receives a separate original-template address
(`ritual.purse.held|`). The observer retains that one physical module for
non-elected Temple visitors and ignores all of their stand/UI modules. The
module's pose remains owner-authored in the shared map frame, using the existing
town-service packet grammar; no purse identity or new transaction command is
sent. Release retires the held-only module through the existing manifest and
restores its normal fan/home module. Two visitors can inspect purses while the
host grant still serializes actual donations at that priestess.

Targeted evidence: `scripts/check-town-ritual-transactions.py` binds the
production native callback and guards, including denied/expired grants and
eligibility races. Its source-bound negative controls reject a return to
eligibility-gated pickup or dropping secondary-visitor purse playback.
`scripts/check-town-service-interaction.py` exercises the actual physical
token and Unity grabber with eligibility false at pickup, release and return,
and the successful-donation sink followed by fan regrab.
These checks establish code-path behavior, not the appearance of a purse in a
headset; a multiplayer hardware round must verify both visitors' held poses.

The stand-cloth simulation was retired separately in this build. The private
town mirror no longer samples, transmits or reconstructs its cloth controls.
The old TLV90 reader remains tolerant of historic packets, without restarting
a local or remote cloth solver.
