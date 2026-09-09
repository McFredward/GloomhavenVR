# MB487 peer card visibility

The user's latest multiplayer rule supersedes earlier active, lost and item face exemptions: action-phase cards are open; selection-phase fan, held and placed cards are covered. The short-rest burn flight explicitly stays covered. Naming remains separate and no card identities are added to the wire.

| Peer surface | Action phase, including damage selection | Selection phase |
| --- | --- | --- |
| Hand and damage-pick fan | Front | Back |
| Held hand, active, discard, lost or item card | Front | Back |
| Round and decision recesses | Front | Back |
| Active matrix | Front | Back |
| Discard, lost and item browser fans | Front | Back |
| Ordinary semantic flight / damage burn | Front | Back |
| Short-rest burn flight | Back for the entire originating flight | Back |

Map loadouts retain their existing public source. Offline/local ownership semantics remain unchanged. Unknown model addresses still fail closed; an open phase cannot manufacture missing artwork.

## Evidence and causes

Both supplied LogOutput logs advertise MB486 at line 17 (the host also advertises its network build at 4220). No current card screenshot was supplied; the new bonus screenshots belong to the separate widget review.

Host lines 41229–41661 contain action-phase open recesses alongside `LENGTH BELT` failures (four model-filtered cards versus six owner slabs), including held hand seats four and zero of six. Owner remote lines 27314, 27378 and 27425 send those six-card hand addresses. Later host lines 42860/42956 show three versus five. These are address-resolution failures, not evidence of a privacy refusal. Both logs also contain unnamed held addresses, while active fronts succeed later (remote 13914/20823; host 43081).

Source review finds two UI/model ordering defects. `CardsGameApi.HandFanMember` rejected a non-Hand widget before checking authoritative `HandAbilityCards`, although the shared exit classifier already gives that model list priority. Viewer widgets can lag their replicated model; the shared membership now preserves authoritative hand members and still rejects stale Hand widgets whose model has exited. Parent integration separately changes `LocalRigSampler.NameCard` to name model-active membership before stale widget pile branches and prevents a model hand member entering the stale discard/lost arm. The historical logs do not record each rejected widget's exact CardType, so they establish the failed list belt, not that unrecorded field value.

The short-rest leak is source-proven: card-aware RevealGate overloads reopened the selection gate for cards just committed to lost lists. Population exceptions also reopened active/item surfaces. All CardFaces overloads now use one phase policy. RemotePileFronts additionally had an item path through its BurnException branch without a per-card recheck; that transition is removed. Active, round, held, hand/browser/item fans and card plume source resolution all use the central gate. Semantic and burn flights now recheck visibility each frame rather than retaining start-time permission.

## Validation

Strict Release build: zero errors and zero warnings. Final full wire run: 238645 assertions passed. New vectors cover the phase matrix, stale UI/model membership, model-active sender precedence, removed population bypasses and per-frame flight checks, with mutations that restore historical defects as negative controls. Existing identity-mask tests and Python lint retain sender naming isolation while rejecting obsolete face exceptions. Final integration must register the new vector and both pure helpers explicitly in the wire-test project and invoke `CardVisibilityVectors.Run(t, repoRoot)`.

These are source and automated-test results, not a headset image verification. Sender-latched short-rest provenance closes a separate ordering hole: RemoteAvatar used to replace the short-rest state before dispatching the same packet's burn event.

## Short-rest origin across context changes

The owner marks the actual offered model card before acceptance; redraw/cancel removes the mark. Every real or fallback burn-flight producer consumes that mark into the corresponding queued event. Weak model keys cannot confuse a recycled UI widget with another card, and consuming once prevents a recovered card's later damage burn retaining the short-rest tag. The bounded outbox now stores endpoint and visibility flags together. Parent-owned additive record 54 carries flags and the existing sequence, with no new card identity. Redundant packets retain the original event's provenance.

The receiver keeps flags through early-event deferral and fallback; a matching model burn only gains coverage, never loses it. It also captures the existing record-39 sacrifice model locally before a closing snapshot overwrites the seat and short-rest context. An already identified short-rest burn stays covered through phase transitions for its whole flight. Tests cover repeat sampling, cancel/redraw, unrelated/recovered cards, queue ordering, sequence advancement and eviction.

Limit: a peer that never received any short-rest context and first discovers a model burn after its local phase has advanced cannot infer the lost historical context before the matching semantic event arrives. The event narrows that presentation as soon as delivered. This is a loss/order limit, not a hardware-proven absolute guarantee. The normal selection-phase model-first hold remains covered independently of the event. Pure test registration additionally links `Net/CardFlightVisibility.cs`.

Candidate-cache follow-up: prior offers are invalidated when the authoritative card returns to hand, round or active resources, when its actor disappears, or when a later round begins without a committed loss. The closing short-rest bit alone does not clear a still-discarded candidate, because that packet can beat the host model loss. A committed loss retains its provenance until presentation consumes it. The consumer checks both existing record-39 recesses; the current owner implementation explicitly uses recess zero. Lifecycle vectors distinguish those transitions and preserve the delayed-loss negative control.
