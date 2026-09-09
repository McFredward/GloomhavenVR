# MB490 card flight review

Base: `97ad6c6a` (ModBuild489). Isolated worktree `mp490-flights`, source and tests only.
Latest hardware remains MB488; the 489 changes and this review have no headset evidence yet.
Read the current rules, state and newest protocol build notes before implementation.

## Source-proven repairs

- A burn kept across a character switch previously suppressed the same numbered recess on the
  newly displayed character. Recess and active-cell ownership now require the original actor.
  Its retained world endpoints are not rebound to a different character's layout at release.
- Legacy `FlushedToActorId` aliases could consume another actor's semantic release. Matching
  now requires the actual actor and origin population; active count/seat and paired sacrifice
  recesses remain distinct. Early fallback receipts use the same addressed match.
- Recovered/undone or destroyed pending burn cards now cancel their rendering and release claims.
  They cannot remain as stationary burn slabs or silently consume a later unrelated event.
- A newly lost active card can render both its old active cell and the burn slab. The new
  `RemoteBurnFx.OwnsActiveCard` hook identifies the actually visible, actor-bound slab; integration
  must apply it to active-cell rendering through the avatar wrapper.
- Ordinary burn flights no longer permanently latch the selection phase as short-rest provenance.
  Phase visibility is still checked on every frame; explicit short-rest provenance stays covered.
- Active-origin events can arrive before the model reaches the destination pile. They now wait
  for the exact previous active source instead of falling through to an unrelated recess/default
  active-centre slab. The bounded retry lasts two seconds (the existing recent-event horizon).
  Unresolved identity never licenses a fabricated card or a guessed flight.
- A local read-only view's native loss timeline is not the canonical owner's native timeline.
  Its burn flight now waits for a validated canonical release through
  `CardFlightVisibility.ObserveOwnerRelease`. Receipt matching is actor/source/seat bound,
  consumed once, bounded to32 and expires eight seconds after release; scenario teardown clears it.
  This gate applies only when the foreign character has a known modded controller. Flat peers
  continue using the original native animation completion gate. Integration forwards canonical
  events to other remote boards currently displaying that character, without rebroadcasting them.
- Burn front resolution now uses the explicit model card and actor; a pooled FullAbilityCard's
  stale/null AbilityCard field is not treated as the identity authority.

- The local recycled-card BurnSlab fallback explicitly documented an unresolved back-only
  violation. It now owns an inert original-card clone with a frozen native appearance frame,
  keeping short-rest provenance covered and withholding unresolved open art instead of flashing
  a back. Source widget callbacks never run; source objects are never reparented. Destruction
  releases clone resources. The same review found its board-up arc and fixed source scale: it
  now uses ceiling/world-up and the recorded original world width like the live VRCard path.

- Per-character hand teardown previously cleared pending sources/poses for every character.
  It now removes only widgets belonging to the destroyed hand, preserving other pending holds.

- Public front readiness now holds the flight clock as well as withholding the wrong back.
  Original art gets a bounded two-second resolve window; known selection/short-rest backs fly
  immediately. Missing art cannot consume an entire hidden arc, nor strand a permanent claim.

- The latest short-rest flight ruling applies locally and remotely. All three live local burn
  launch paths now consume provenance once and pass the same covered bit to both the semantic
  event and VRCard flight. The face lane's temporary cover restores on landing/cancel/reuse;
  ordinary damage sacrifices keep their fronts.

- Authoritative release flags supersede tentative model-watch context. A delayed ordinary burn
  cannot inherit the owner's later short-rest UI, and a proven short-rest burn remains covered.

## Coverage

Reviewed local round-recess exits, active expiry to Discard/Lost/PermanentlyLost, return/retrieve
and restart flights, short/long-rest loss, one/two-card damage sacrifice, native shader plus
owning-hand loss completion, focus switches, source retention, duplicate and paired release
claims, board geometry capture, renderer pooling and scenario/hand destruction. Existing
MB489 completion gate still never interrupts a running native timeline with a wall-clock limit.

Tests extend BurnFlightCompletionVectors with actual addressed-match/receipt cases and source
binding guards, including negative controls for cancellation and deferred active-source resolve.
Release builds passed with zero errors/warnings during development. Final combined checks and
hardware conclusions belong to the integration report; automated checks do not prove VR pixels.
