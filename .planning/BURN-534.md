# Short-rest original playback across inactive hierarchy — build 534

## Evidence and cause

The supplied owner Debug capture is ModBuild 533. The remote log is still historical
build 500 and cannot verify the current peer rendering.

`LogOutput.log:2388–2390` records the decisive first confirmation: FlameStrike's original
CardEffects (`fx=-36008`) starts and terminates in frame 2841 with `active=False`, native
clock 27.021 and raw GreyOut 0. Its renderer has no bound draw material. Native
`CardEffects.BurnCardTimeline` immediately yields break when the face is inactive and
`playOnDisabled` is false. No running or completed episode can therefore protect that
first attempt. The game's coroutine host is Choreographer, not the inactive card.

The same original returns active in frame 2842 (`:2407`). Its spent floor is retained,
but Burn and flame animation are still zero. The mod's no-ramp recovery paints the burnt
endpoint at elapsed 0.445 (`:2410–2411`). The subsequent native LostMode refresh is allowed,
restores shader channels to zero and starts a new animated ramp (`:2416–2420`); that ramp
finishes two seconds later (`:2444`). This explains why retaining only the spent floor or
blocking duplicate *running* animations did not remove the flash.

## Change

The existing BurnCardTimeline Harmony patch now permits the native `playOnDisabled` path
for an animated burn on a verified original: its resolved AbilityCardUI must still own
that exact full face and a card. Both adopted faces and native-parent originals qualify.
Unbound, mismatched and clone-like faces retain native behavior. Nonanimated calls retain
their requested flag. No object is activated and no clock, duration, gameplay state or
continuation callback is changed.

The first original burn can now run through the short hierarchy transition. Existing
spent-floor retention, model episode history and owner appearance capture see that same
running iterator; later LostMode, RefreshPile and RestoreCard requests preserve it rather
than resetting and replaying it. Existing native loss-sequence and artwork completion
barriers still determine when the card can leave the board. Actual recovery continues to
clear the retained artwork and permits a later genuine burn.

## Validation

The production-method replay harness now represents the native inactive early-exit gate
and binds the new prefix before constructing the native iterator. Fixtures reproduce
inactive confirmation, next-frame reactivation, Discarded-to-Lost membership, repeated
LostMode/no-ramp/reset refreshes, retained spent material during local/remote sampling,
completion and genuine recovery for both adopted and native-parent ownership. Unbound and
mismatched originals retain native inactivity behavior. New planted regressions remove
inactive permission or broaden it to clones and must fail the targeted assertions.

Focused validation passes: 672 runtime assertions, seven source bindings and 32 negative
controls. Strict Release build passes with zero warnings and errors. Hardware confirmation is
still required: one continuous grey/brown short-rest burn without a blue reset, followed
by its normal flight, locally and on the peer board. The current log proves the prior
native early exit and later replay; it does not establish the new headset outcome.
