# Build670 merchant onboarding source review

Worker base: `dev` `bdb9206d9140c03bae3e9440caa96cf755da4e4d`,
ModBuild669, initialized worktree `gloomhaven_vr_npc670_tutorial`.
The review adds regression evidence; `TownServiceTutorialPatches.cs` and
`TownWindowCloseScope.cs` require no additional production change.

## Supplied evidence

The frozen local `LogOutput.log:17` and `Player.log:42` identify ModBuild669,
assembly1.1.0.0. The root integrator inspected `seperates_fenster.jpg` before
assigning the separate confirmation-window defect to the seating lane. The
input manifest retains its hash. The frozen remote `Player.log` is an older
unmodified-host report with no mod banner; it is not a matching observer log
for this newer local tutorial run.

Local `Player.log:6648-6658` records the immersive VisitMerchant and BuyItem
completion. Later `Player.log:19137-19138` starts/shows the original BuyItem
step in the converted shop. It contains no corresponding real shop-exit
trace establishing a current deadlock reproduction. The single managed
NullReferenceException at19655 belongs to `VoiceChat.BoltVoiceChatService.OnDestroy`
during shutdown. It is not evidence of an exception in merchant/tutorial
continuation. The maintainer's intermittent post-purchase deadlock is a
historical report; this run establishes the separate-window symptom, not
the historical deadlock's current cause.

## Native contract

- `UIShopItemWindow.EnterShop` shows the original merchant introduction on
  its first entry. Both normal completion and cancellation of the original
  introduction invoke `FinishEnterShop`; that method starts the serialized
  `ftueStep`. The merchant exit method is `Exit`; `ExitShop` belongs to the
  enchantress window.
- `UIShopItemInventory.OnSelectedItemBuy` opens the original confirmation.
  Its confirm callback calls `BuyItem`; cancellation restores focus.
  Neither method completes an FTUE step. Spending gold is not the native
  BuyItem lesson's completion criterion. The historical serialized WorldMap
  completion listener and silent-toggle defect are documented in
  [SAVEGAME-507.md](SAVEGAME-507.md).
- `Exit` hides the introduction and shop. `OnHidden` disables party selection,
  clears the shop subscriptions and invokes `onExit`. Original `UIWindow.Hide`
  dispatches `onHidden` before changing `IsOpen`; the visual fade follows.
  Original `UIIntroduceProcess.Cancel` invokes its cancellation callback
  synchronously. Fade completion must not gate map tutorial continuation.
- The existing converted close bridge captures the exact manager, shop,
  current serialized step, pending promise and loaded map. A real native
  exit or scoped VR X may request completion after native cleanup. A
  temporary borrowed-shop hide for another panel cannot consume the lesson.
  A later real X also covers a borrowed original which is already hidden.
- Completion remains `MapFTUEManager.CompleteStep -> UIMapFTUEStep.FinishStep
  -> OnFinishedStep -> original CallbackPromise -> MapFTUEManager.OnCompleteStep`.
  The original `UIMapFTUEStepStartListener` then starts InteractWithMap.
  Replaced managers/maps/promises and reentrant or delayed closes remain guarded.
  No purchase-triggered completion, saved FTUE mutation or forced map unlock
  is added.
- Native `MapFTUEManager.HasToShowFTUE` suppresses this campaign FTUE online;
  `OnSwitchedToMultiplayer` calls native `Finish`. The patch remains conditional
  on the live manager playing. Native `LoadState` treats an already introduced
  merchant as VisitMerchant/BuyItem complete. These whole save/network methods
  were reviewed, not executed by this portable fixture.

## Regression proof and limits

The local runner now binds18 original method bodies: shop entry/exit/hidden,
manager start/complete/completed dispatch, step start/finish/finished dispatch,
the original next-step and WorldMap completion listeners, and six original
promise dispatch methods. Hashes of these read-only bodies are retained in
`sources.json`; only the callback name `OnFinishedStep` is adapted to the
fixture's explicit `CompleteHide` port. Two purchase bodies are also checked
and hashed for their absence of an FTUE completion path.

Inventory and confirmation, Unity event containers, introduction presentation,
shop fade and manager side effects remain explicit boundary ports. This does
not execute native `MPBuyItem`, the full save lifecycle, UI layout, or headset
pixels. Artificially delayed FTUE hides stress the existing one-shot bridge;
they do not assert that native introduction cancellation waits for a fade.

The added matrix covers first cold introduction, purchase confirm/cancel,
native Exit, a notified WorldMap exit and scoped X of an already hidden
borrowed shop. Each real close advances the original next-step listener only
after cleanup; buying or cancelling alone keeps BuyItem pending. It also
covers independent shop fades, repeated exits, a stopped old manager's delayed
promise followed by a second save, inactive uncompleted FTUE and an already
completed loaded lesson. Existing immersive option-transition and exact-close
cases/controls are retained.

Final worker validation:

- Native-bound onboarding:250 assertions, all20 executable negative controls;
  `.planning/debug/npc670-tutorial-final-v2/results.json`.
- Portable onboarding:250 assertions, all20 executable negative controls;
  `.planning/debug/npc670-tutorial-portable-v2/town-onboarding.log`.
- Actual-source converted close:113 assertions, all13 controls, retained from
  `.planning/debug/npc670-tutorial-focus/results.json`; its owned source did
  not change in the later fixture-only corrections.
- Source group16/16 and strict Debug0 errors/0 warnings, retained for unchanged
  production source from `.planning/debug/npc670-tutorial-source/results.json`
  and `.planning/debug/npc670-tutorial-debug.log`. Whitespace checks pass.

The central runner killed/reaped suite processes and removed each evidenced
generated project's bin/obj caches. Standalone portable runs used the same
`cleanup_completed_run` API after process-group termination/reaping and kept
their cleanup receipts. Logs, results, copied fixture sources and source hashes
remain. This is focused worker evidence, not a new complete local gate or
hardware confirmation of tutorial or multiplayer progression.
