# Native card pool lifetime — build 528

## Hardware evidence

The latest local logs identify release 1.0.4 / ModBuild 527 / main a6d044c4b.
The run is offline; remote files still identify build 500 and are historical.
No new screenshot accompanies this card-initialization failure.

`Player.log:25509–25522` reports `ABILITY_CARD_SwiftBow` during native hand spawning,
then `Unable to initialise ability card` and the game's `GlobalErrorMessage`.
The stack enters `AbilityCardUI.SetUpInteractabilityRelationship` at offset 0x89.
The shipped assembly's IL at that point dereferences `fullAbilityCard.topActionButton`
via `Component.gameObject`: a serialized native component has been destroyed.
This does not prove that the full face still lives: its managed serialized reference
can survive destruction too. Native `CardsHandUI.SpawnCards` catches the exception,
shows `ERROR_UI_00021`, and offers the return-to-menu path.

The same Swift Bow previously completed a short-rest burn (2.00-second wait, final
GreyOut 1). Scene-release diagnostics already run before the later load. The prior
borrowed-face release was therefore insufficient for this observed failure.
Unrelated Hydra DNS failures and native unload exceptions also exist; these are not
identified as the source of this specific error window.

## Source and repair

The native pool trusts a surviving recycled root and does not validate its children.
`CardFace` restores a captured parent, which can be a temporary native dialog; a yielded
original can also leave the adoption registry. Returning only currently borrowed faces
therefore does not guarantee a self-contained native widget at scene teardown. This is
a source-supported destruction route, not proof of the exact writer in the hardware run.

- After returning VR borrows and before native scene loading, return all original hand
  faces to their native widgets, including previously yielded faces.
- At native ability-card recycling, release any remaining borrow before returning the
  face. Preserve the native parent-change plus `CancelLostAnimation` pair and world pose.
- Before native pool selection, retire only recycled copies with missing/foreign full
  faces or missing action halves. Require an intact index-zero native template first.
  Native selection then chooses a healthy copy or clones that original template; native
  initialization, callbacks and gameplay card data remain unchanged.
- Long-rest widgets still require both action components: the native initialization
  dereferences both even though only one action layout is presented.

No action is acknowledged, no exception dialog is merely hidden, and no partially
initialized card is allowed through by bypassing native initialization.

## Validation and limits

`card-pool-lifetime-tests.sh` executes the production helper: 169 assertions, six source
bindings, four deliberately broken variants rejected. Cases cover temporary dialog
ownership, repeated scene returns, native loss-cancellation preservation, missing
individual action halves, destroyed whole faces, foreign references, healthy copies,
long rest, nulls, other card types and a damaged original template. A separate worker
review caught and corrected the cancellation and long-rest requirements.

The lightweight Unity adapter cannot reproduce deferred Destroy or native OnDestroy
scheduling. A broken original template is deliberately not reconstructed, and the guard
does not assert complete integrity of every field on a native card. Final headset checks
must repeat short rests followed by scenario/map transitions and hand respawning. Look
for `CARD POOL REPAIR` and any recurrence of `Unable to initialise ability card`.
