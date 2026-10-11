# Steam Frame run: ModBuild 596 (2026-10-01)

The maintainer tested the map/town at 3408 pixels per eye, native `Fastest`,
MSAA 0 and the saved wall cadence. `LogOutput.log`, `Player.log`, and two
screenshots are under the main checkout's ignored `.planning/debug/steam_frame/`.
This run did not include a scenario, so it cannot establish scenario gains or
compare loading-screen stalls with interactive frames.

## Observed

- The first priestess approach produced a 487.82 ms frame (log line 4883),
  including 320.16 ms in mod scopes. The visit cost 192.91 ms; opening the
  original folio cost 170.13 ms, of which `TownTemple.Folio.Inscriptions`
  cost 139.62 ms. The second visit was much cheaper (103.15 ms total,
  18.35 ms folio open). Code inspection found the first book-page geometry
  samples scanned all original triangles and recomputed triangle bounds.
- The original temple donation callback executed (line 5031), followed by
  native gold/prosperity/story work in `Player.log`. The screenshot
  `20261001131314_1.jpg` shows motes still above the covered bowl. The old
  effect intentionally emitted for 2.45 s with motes surviving up to 1.75 s;
  the unavailable-cover pose could begin sooner. A screenshot alone does not
  establish that the particles lived past their configured 4.20 s.
- On first enchantress approach, the native off-bar selection threw a
  `NullReferenceException` before its window opened (line 5397); the
  screenshot `20261001131350_1.jpg` shows no card-offer overlay. Build 596
  prefilled 18 inactive native enhancement slots before `EnterShop`, after
  which the game's normal slot normalization ran. The old catch discarded
  the stack, so the exact throwing instruction cannot be proven from this
  run. Removing the prefill restores native ownership of that lifecycle.
- Sustained town/map windows still took about 36–42 ms/frame in main-thread
  logic and 52–68 ms/frame total. `TownPublicStock.Catalog.Cards` spent
  roughly 4.35–4.86 ms/frame while even the fully hidden next cassette page
  kept polling art and refreshing its original widget. A low visible-page
  cost remains after that work is skipped, so this is not a claimed target FPS.
- Window materialization itself measured under 0.2 ms CPU per rendered frame
  in the sampled completions. Its 0.35 s appearance occasionally completed
  after only one to four rendered images (for example lines 3910, 4397 and
  5251). Frame stalls therefore skip intermediate animation states even when
  the effect's own CPU work is cheap.

## Build 597 response

- Index the priestess book's original triangles spatially while preserving
  the same intersection/sampling math. Keep native UI/controller creation at
  its own entry point; the prior early native-pool warmup was unsafe.
- Let blessing motes finish before transitioning to the unavailable-cover
  pose; clear them once at completion. Preserve a smooth later cover pose and
  do not replay effects for stale multiplayer revisions.
- Stop hidden merchant-page art/widget/mip work; refresh synchronously on
  its first exposed tick before it can render or be published.
- Show the existing VR loading indicator after an immersive-NPC off→on
  settings change until unlocked residents and the public merchant cabinet
  are ready. It sits in front of the still-open VR options pane while this
  happens without suppressing native fallback or rescue windows. A bounded
  failure exit prevents a broken optional asset from leaving the player
  behind a permanent spinner.
- Cap *visual* materialization progress to 50 ms per rendered frame. The
  watchdog still counts actual elapsed time, so a window cannot remain held
  forever after a stall. This preserves more intermediate states on slow
  devices; it does not create extra rendered frames or fix the underlying
  52–68 ms frame time.

The next headset run should verify enchantress card offering on first
approach, priestess first-entry hitch and blessing completion, cabinet card
visibility/turning, NPC off→on loading indicator, and window open/close
smoothness. Compare fresh `SPLIT`, `STEPS` and `WINDOW MATERIALISE` lines
against this run. These hardware outcomes remain unverified here.
