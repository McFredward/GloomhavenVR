# Steam Frame standalone: interactive CPU candidate after Build 594

The baseline is the Build 594 Debug run in `.planning/debug/steam_frame/` and the
measurements in [STEAM-FRAME-EIGHTH-HARDWARE.md](STEAM-FRAME-EIGHTH-HARDWARE.md).
Build 595 only corrected the wall-setting range. This candidate changes
presentation scheduling and diagnostic work without reducing visible content.
The runtime optimizations apply on PC VR and Steam Frame alike. The 2D map and
the original non-immersive town windows retain their native paths; generic
window dormancy still wakes and processes an original window in its first
visible frame.

## Changes to measure

| Area | Baseline evidence | Candidate behavior | Hardware comparison |
|---|---|---|---|
| Merchant cabinet | `TownPublicStock.Catalog.Cards` averaged 4.6–5.9 ms/frame in populated town windows. | The current, next, transitioning and moving pages tick. Cold pages do not repeat card, row-clone or tooltip walks; leaving pages lose their ink and collision in the same frame. The original item widgets, prices, stock and page motion still drive local and shared presentation. | Compare the same filled cabinet at rest, page changes, held-card returns, and a second player's view. |
| Town windows | `CanvasConversion` and `ModalFallback` had several milliseconds of recurring cost, but the sampled windows mixed active panels and transitions. | Dormant floated windows skip visible conversion and grab-follow work. Their pooled-child veil continues to release stale culling. Wake-up processes the complete panel in that frame. New Debug substeps identify the active-panel cost. | Compare stationary town windows and repeated modal open/close; check no stale or blank window on wake. |
| Map card reveal | Ten already prepared local map card clones incurred 247.8 ms in mip work and 70.0 ms in activation/fitting on the first visible fan frame. | Parked clones now fill the shared mip replacement cache, one card per loading frame, without running layout-dependent silhouette/poke/effect maintenance early. Activation retains the normal full rescan. Remote map fans pin their publicly visible class artwork while closed; scenario selection remains face-down remotely, and the original remote count/order belt still decides every face. Per-peer first-face scopes separate remote reveal cost in a multiplayer trace. | First local map fan reveal, character switch, and remote fan/held-card reveal with two clients. Verify no grey/black/incorrect face, correct order, and no secret selection front. |
| Enchantress first entry | The first native enhancement opening cost 211 ms inside a 461 ms approach frame; a later visit still reached 242 ms. | While the 3D map is loading, one inactive native card-slot wrapper is added to the original pool per frame, up to the largest assigned character's card count (32 maximum). Cloning occurs beneath an inactive staging parent, so an active prefab cannot run `OnEnable` before it is parked. Original `Display`/`Init`, selection and game state still run only on the real visit. The 2D map and original windows do not use this prewarm. | Enter the enchantress first and twice again, both locally and with a peer. Check upgrade slots, card selection and points; compare first-visit and repeat-visit scopes. |
| Wall diagnostic census | One post-load `WallFade.Census` frame cost 65.7 ms; the detailed room/tile census performs full-scene searches solely for forensic logging. | Fresh profiles on every platform leave `DeepSceneCensus` off. The ordinary heartbeat, budget, signatures and error reports remain; the full census is available as a live opt-in. | Compare `WallFade.Census.Deep`/`.Summary` and the worst post-load wall frame. |
| Wall commit provenance | Genuine scene replacements still trigger an atomic commit over thousands of renderers. | Repeated classification of the same renderer during one commit reuses its pure dressing verdict. No renderer result persists across commits, so changed scenes retain their original classification. | Compare commit phase time and cache hit/miss counts after a room transition. |
| Residual scenario CPU | The post-load scenario has about 24 ms/frame between the first Update and last LateUpdate markers, more than the named mod scopes explain. | A Debug-only capture samples at most 120 frames per Perf window, splitting Update, the native/interphase gap and LateUpdate with two components disabled outside that interval. Available Unity timing markers are reported with sample counts; unavailable markers are `n/a`, and nested markers are never summed. Setup/stop time is reported separately. | Use matched stationary scenario intervals to identify the largest phase, then target that code. The additional native profiler collection overhead itself is not isolated until hardware A/B. |

The 25-phase changed-scene wall commit still runs atomically. Those later
post-load commits classify thousands of genuinely new/replaced renderers and
cannot be split in place without freezing fades or publishing a partial wall
table. A safe staged replacement needs a separate table, renderer-write intents,
ownership transfer and an atomic publish. The diagnostic change above does not
claim to remove this remaining hitch.

## Platform settings and compatibility

The Frame already receives a separate fresh-install profile: native `Fastest`,
mod MSAA 0 rather than PC's 4, a 900 MB streaming-budget floor rather than
4096 MB, and a 4 s wall-table rescan rather than PC's shorter default. The
installer suggests 3408 pixels per eye; the mod's own eye scale stays 1.00.
These are deliberate Frame quality/latency compromises, not global downgrades.
Existing BepInEx, game and SteamVR settings remain authoritative. Build 594's
1728-vs-3408 comparison did not show a useful frame-time gain, so this round
does not reduce eye resolution or window sharpness again. Further Frame-only
compromises should follow the next matched trace after the CPU changes; a
slower panel refresh or reduced distant NPC effects would change visible
animation and multiplayer parity, and are not silently enabled.

Compatibility checks cover the immersive 3D town, the 3D map with original
town windows, the 2D map, and scenario windows. Merchant page scheduling only
exists in immersive mode; the enchantress prewarm is gated to the immersive 3D
map's loading phase. Generic window scheduling and card mip caching remain
valid for all map modes. In multiplayer the local and remote map hand fronts
are public; scenario selection fronts remain concealed only on remote boards.

Neither the Build 594 logs nor these source changes establish a new headset
frame rate. Keep 3408 per eye for the next comparable CPU run and capture a
stationary merchant interval, first enchantress approach, first local and remote
map-hand reveal, and a scenario after the loading indicator disappears. The
Debug `STEPS`/`SPLIT` and new nested scopes can then distinguish CPU gains from
composition or GPU limits.
