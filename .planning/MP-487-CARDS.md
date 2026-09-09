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

Both supplied LogOutput logs advertise MB486. Host line 4220 is the local banner. No current card screenshot was supplied; the new bonus screenshots belong to the separate widget review.

Host lines 41229–41661 contain action-phase open recesses alongside `LENGTH BELT` failures (four model-filtered cards versus six owner slabs), including held hand seats four and zero of six. Owner remote lines 27314, 27378 and 27425 send those six-card hand addresses. Later host lines 42860/42956 show three versus five. These are address-resolution failures, not evidence of a privacy refusal. Both logs also contain unnamed held addresses, while active fronts succeed later (remote 13914/20823; host 43081).

Source review finds two UI/model ordering defects. `CardsGameApi.HandFanMember` rejected a non-Hand widget before checking authoritative `HandAbilityCards`, although the shared exit classifier already gives that model list priority. Viewer widgets can lag their replicated model; the shared membership now preserves authoritative hand members and still rejects stale Hand widgets whose model has exited. Parent integration separately changes `LocalRigSampler.NameCard` to name model-active membership before stale widget pile branches and prevents a model hand member entering the stale discard/lost arm. The historical logs do not record each rejected widget's exact CardType, so they establish the failed list belt, not that unrecorded field value.

The short-rest leak is source-proven: card-aware RevealGate overloads reopened the selection gate for cards just committed to lost lists. Population exceptions also reopened active/item surfaces. All CardFaces overloads now use one phase policy. RemotePileFronts additionally had an item path through its BurnException branch without a per-card recheck; that transition is removed. Active, round, held, hand/browser/item fans and card plume source resolution all use the central gate. Semantic and burn flights now recheck visibility each frame rather than retaining start-time permission.

## Validation

Strict Release build: zero errors and zero warnings. Initial full wire run: 238607 assertions passed. New vectors cover the phase matrix, stale UI/model membership, model-active sender precedence, removed population bypasses and per-frame flight checks, with mutations that restore historical defects as negative controls. Existing identity-mask tests and Python lint retain sender naming isolation while rejecting obsolete face exceptions. Final integration must register the new vector and pure helper explicitly in the wire-test project and invoke `CardVisibilityVectors.Run(t, repoRoot)`.

These are source and automated-test results, not a headset image verification. Sender-latched short-rest provenance is a follow-up in this same lane because a receiver can process a closing context before receiving its burn event.
