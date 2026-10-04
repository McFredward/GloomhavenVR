# Build 622 paired NPC review

Status: integrated implementation and focused checks; final gate pending. No Build622 headset result.

## Inputs

Immutable hardware inputs and SHA256 hashes are retained in
`.planning/debug/npc622/inputs.json`. Both build banners identify Build620;
the host Player log contains Debug records, the observer is at normal Info.
All nine new NPC screenshots plus `npc_fenster_problem.jpg` were inspected.
The screenshot proves an empty cabinet, malformed fan presentation, an oversized
held purse, an edge-on enchantment aura and absent enhancement rows. Those visual
facts do not prove every transport root cause by themselves.

## Causal findings and corrections

- Merchant inspection bypassed the approved scenario avatar/item-fan transport.
  It published full town modules for each fan face/body, waiting on template and
  immutable presentation delivery before visibility. The canonical metadata/rig
  path is restored, with separate originals only for palm offerings/returns. The
  actual inspection pile now supplies its immutable character key; the previous
  ability-fan owner could be zero while the item fan was visible. Public map item
  metadata is sampled atomically with its held pose, and its native list count,
  seat and item ID reject stale inventory and same-length reorder mismatches.
- The observer reports missing `shader|GloomhavenVR/TownNpc` while applying public
  cabinet modules. Bare Shader.Find cannot load a shader asset in an open bank.
  Resolve now uses the existing BundleShaders original-path loader, and template
  material registration records its original shader before texture properties.
- Current/transition public shelf cards and mechanics were not consistently
  prioritized together. Visible native modules now precede invisible prewarm work;
  same-author page updates retain ready original clones rather than retiring the
  entire cabinet. Future member epochs wait for their matching owner clock; the
  original physical cassette follows that clock during dependency delivery.
  Author changes still use atomic handover and every original card/holder.
- Purse motion was measured against a styled hand root on the sender but consumed
  relative to the unstyled remote holder. The world/style scale matrix must match
  the approved holder frame for both offsets and size.
- Purse input used a label rectangle as its pick target while its PCG body sits
  65 mm below that rectangle. Mesh bounds, grip and bowl-release sampling must
  follow the actual original purse body, leaving text readable.
- Fixed purse template/material preparation was deferred until first temple use.
  Both original wrist/held template keys are now prepared during normal town
  preparation when their native prop is ready; the body publishes before its label.
  The labelled template root and its body now both follow the actual holding
  hand, or no hand while in the bowl. Closed-wrist visibility does not interrupt
  a donation sink or ordinary return animation.
- The native enchantress aura tween writes WORLD Euler-Z, changing the normal of
  a tilted card. The original tween phase now transfers to the actual offered
  card plane after final pose and before publication. Native highlights retain
  their original geometry/callback owner; observer copies stay inert.
- Offered faces depended on NativeSource from an independently pooled/hidden
  card-list widget. Publication now follows the actual offered face and retained
  immutable OfferedCardId, just as original return flights do.
- The actual enhancement rows come from native slotsPool in CollectDynamic,
  not temple book inscriptions. The old coarse root-rectangle visibility test
  discarded complete branches even with visible native child graphics. All
  native enhancement rows publish with priority/prewarm; the original inherited
  viewport/masks still own actual clipping and scrolling.
- Non-immersive town mode changes run Exit, clearing temple rows/selection and
  rewriting the shared borrowed banner. A scoped original map-surface switch
  preserves the live service controller, title, rows and decision state. Native
  map location visibility reads the requested map during the scoped call;
  explicit service close and all non-converted/closed windows retain native flow.

## Explicit visual exception

The maintainer's 2026-10-04 instruction makes the temple purse pre-drop ghost
visitor-local, extending the already approved town CARD pre-drop guide exception.
Actual wrist/held/donated purses, offered NPC poses, cards, widgets, hover effects,
return flights, blessing effects and audio remain shared. Scenario guides are
unchanged. AGENTS.md records the new source; historical Build614 notes are history.

## Story reconnect

The failed join uses `PPF3G8JSD`; the current host room is `PPF3G8JSD6`.
The full code subsequently connects successfully. This specific error is
GameDoesNotExist, not evidence of a native Story admission refusal.

Native GHNetworkCallbacks.ConnectRequest already accepts Campaign/Guildmaster
story connections and sends GameToken.WaitUntilSavePoint. Native client handling
keeps the connection pending until SavePointReachedEvent, then requests a fresh
host save. Clearing that wait would load an old save: GHHostCallbacks only writes
a new campaign/guildmaster save immediately for MapHQ. Do not change Photon,
NetworkManager, gameplay actions or save-phase safety merely to bypass that gate.
The native join-window and NetworkManager front doors have no Story-phase gate;
checks there concern code, privilege/crossplay and an already active connection.
The native continuation is also checked: SavePointReachedEvent requests a fresh
host save; the host waits for its SaveQueue before streaming it. Pending connections
remain JoiningPlayers until PlayerEntityInitialized and do not enter the AllPlayers
story action target. The native city-event save completion notifies joining players;
round-start notifications provide the scenario equivalent. The MapHQ lobby keeps
ready controls gated while the actual player initializes. No synthetic completion,
ready/assignment change or forced old-save load is introduced.

This is a verified existing story connection plus safe-checkpoint wait, not newly
implemented immediate story viewing. The supplied failed attempt used an incomplete
code, so it does not justify changing native admission rules. If a later run with the
complete code receives an actual admission rejection, retain that exact native error
and phase for a distinct investigation.

## Proof limits and repeat policy

Earlier probes used an empty purse rectangle/styleScale=1 and an axis-aligned
card aura; neither represents the supplied headset geometry. Corrected checks
exercise original PCG mesh bounds, world/style combinations, native world-Z writes
and pitched/rolled cards. Source tests alone cannot prove a headset picture.
Retain original failures; after a limited fix, repeat only its affected scope.
Run the complete integrated gate once on the final changed dev tree before push.

## Additional 1:1 review

The offered-face/aura path is sampled after the final owner-facing pose. The
native enhancement rows retain the original masks, scroll and hover animation;
observer copies do not run gameplay callbacks. Existing TownServiceMotion
interpolates shared head-facing rotations at render cadence, including modules
whose position is rigidly attached to an interpolated avatar hand. No second
rotational smoothing path or observer-facing decision window is introduced.

The canonical map item renderer borrows the original ItemCardUI from the native
pool with activation disabled, uses resident original background/class icon
sprites and retains its initialized native text/layout. Pending original fronts
do not draw gray public placeholders. Preparation warms the same original pool
before normal town loading completes, including the public stock and real party
items. Lazy recovery remains when a pool or late item is replaced.

The standing mixed-language ruling is explicit, not a new exception invented for
this review: Core/Loc/Loc.cs records the maintainer's 2026-08-28 acceptance. Text
transmitted as rendered text retains the owner's words; key-derived originals
use the receiving game's localization. No new language divergence policy is added.

The complete existing scenario item/fan path remains covered by the integrated
gate. Public map metadata is guarded out of scenario sampling and reception;
selection secrecy and local controlled-card visibility are unchanged.
