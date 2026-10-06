# Build630 integration: Frame628 post-load review

Integration starts from current dev/Build629 `e6ecf3d4532`, preserving the NPC
original-presentation and transaction-flight changes. Both new supplied logs
identify Build628/176beb741 and the actual Frame adapter, not a Gaming PC.

## Delivered changes

- Completely remove visible idle sampling, private baked proxies, original LOD
  substitutions and both associated controls, per the explicit2026-10-06 user
  withdrawal. Native visible animations remain continuous. Independent native
  offscreen culling and configurable body/cloth/FX budgets remain.
- End the initial spinner when native loading ends, retaining genuine room/
  material/boot gates. Optional card/figure caches continue in the background
  instead of adding the evidenced61.81s cosmetic overhang. Loading presentation
  no longer lowers native async priority.
- Keep XR allocations stable throughout a session. Coalesced live resolution
  edits use the existing eye viewport; larger-than-capacity requests remain saved
  for the next VR restart, with explicit English/German help. MSAA changes also
  coalesce; they still inherently change native multisample resources.
- Reuse repeated wall mesh admission and ancestor component reads only within
  one synchronous invocation, and reject inactive native terrain before costly
  pre-cull work. No cross-frame cache or weaker missing-room safeguards.
- Close Debug measurement windows on Optimize edits; retain long sparse windows
  discarded by the previous120-frame rule. Correct reported-refresh and WallFade
  inclusive-phase interpretations. No diagnostic stream is promoted to normal.

Code/assets remain common to PC and Frame; only missing-entry defaults differ.
Remaining compromises stay adjustable. No gameplay rule, wire protocol or
transport cadence changes. Protected logs remain; the two withdrawn keys and one
obsolete clone patch are the sole narrow authorized surface retirements.

## Evidence and boundaries

[Settings audit](FRAME-630-SETTINGS-AUDIT.md) records actual delivered terrain,
chunk, body/cloth/FX and UI effects, repeated cheap-wall toggles, zero explicit
instancing and the offline limitation. It distinguishes one-room complete
windows from settled three-room spike samples. No isolated FPS comparison can be
recovered for silently changed controls or the crash-end resolution requests.

[Terrain CPU review](FRAME-630-RENDER-CPU.md) explains repeated native read costs
and source-safe further strategies. [Resolution review](FRAME-630-RESOLUTION.md)
records exact .80/.85 allocation transitions and the modeled-provider proof;
[loading](FRAME-630-LOADING.md) and
[idle removal](FRAME-630-IDLE-REMOVAL.md) record their real Unity boundaries.

Worker focused evidence establishes native idle187/15 controls, terrain293 and
its new causal read guards, and full production render-quality48/13 controls.
The XR provider is explicitly modeled; no new headset picture, crash-free run,
FPS target or multiplayer capacity is certified by those fixtures.

Final integrated gate status is pending until the source-frozen dev check ends.

## Next hardware run

Use the same fully loaded three-room view. Rapidly select resolution1.00/.80/.85
and return to1.00, verify full uncropped stereo and no texture recreations, then
compare parked cheap-wall/geometry/batching controls individually with the new
Debug boundaries. Let a stable window finish before the next edit. Loading
hitches remain acceptable; inspect when the spinner actually disappears.

The remaining priorities are generation-local FastReclaim with bounded fallback,
owned settled wall-write deduplication without stepping visible fades, wider
verified cheap shading/coarse geometry, and a separate stereo architecture
prototype. Every new visual compromise must have its own setting. A paired run
must measure multiplayer separately: this capture is offline.
