# Town services multiplayer follow-up — ModBuild 583 candidate

## Hardware evidence (ModBuild 582)

Both main-checkout `LogOutput.log` files identify `bd038ec09` / ModBuild 582.
The peer screenshot `keine_items.jpg` shows a physically present but empty merchant
cabinet while the host was holding an item. It does not show the host's cabinet,
so the observed asymmetry is established by the maintainer's paired report and
the peer image together.

The host log records 321 bounded material-budget capture failures, 20 ambiguous
BattleOverlayCanvas atlas bindings, and three Temple remote-template mismatches.
The peer log records 35 material-budget failures, 16 ambiguous `T_noise_shards`
bindings, and three early unregistered-template retries. The transient early
registration errors must not be mistaken for the persistent missing-card cause.
The capture failures drop whole original-widget modules, including cards and
their detail surfaces, before a peer can render them. The catalog/held-item
transport also needs its own source-to-observer audit: the ordinary scenario
item fan is not the merchant inspection fan.

Both Debug `Player.log` files record repeated `TOWN ENHANCEMENT approach pending`
with head proximity but no corresponding `cue shown` or card-park event. The
native entry radius was shorter than the NPC's gaze radius and a head-only
arrival next to another service did not elect the enchantress's native visit.
The authored activity also required a native cue before extending her hand.

The Temple's extra furniture was source-proven: each non-primary visitor got a
complete cloned `Shrine` from `TownServiceWorkspace.CreateForLocalVisitor`, and
that clone was published to peers. The three Temple template mismatches may
involve those retired modules, but their logs contain module IDs without
addresses. Do not claim all such mismatches are resolved without a matching
hardware trace.

## Build-583 implementation

The merchant's original material capture now admits the observed shader
property count within the existing byte-count wire format; atlas wrappers with
native content IDs share their identity, and the small effect texture that
collided is distinguished by content. The cabinet remains on its independently
elected public stream. Each private visitor stream retains original item-fan
and held-card modules, while shared cabinet geometry and purchase controls
still have one author. The merchant's stock-pickup reaction uses the same
visitor range as his face attention.

The enchantress's native visit now enters at the resident gaze boundary and
uses a short exit hysteresis. At overlapping stands, the nearer resident owns
an idle native destination unless the player deliberately focuses the ability
fan or brings an owned card to the enchantress. Her hand extends from shared
visitor attention, not from an optional native cue. Native enhancement choices
and their hover states continue to come from the interacting visitor's original
UI presentation, with the actual enhancement gated by the existing host grant.

The per-visitor cloned furniture path is retired; the priestess, enchantress
and merchant use their permanent resident stands. Each visitor may still carry
their own purse or card, and parking an offer claims only that NPC.

## Acceptance scope

- One permanent stand per service for one to four visitors. No per-visitor
  furniture or props; each player's legitimate card or purse may still be held.
- One public merchant catalog with the same original item cards, details,
  category, page, scroll transition, stock state and hover on every client.
  Inspecting a cabinet card, holding an owned item, and opening the merchant
  item fan remain visible to all observers, including a late joiner.
- Merchant stock-pickup reactions start at the same gaze range as greeting and
  use one synchronized voice cue. A different NPC's speech does not gate it.
- The enchantress extends her hand for any local or remote visitor in her gaze
  area. Native offering is available for the locally controlled player in that
  area; a parked card claims only this NPC. Original options and hover state
  remain mirrored through the existing presentation stream.
- Actual native purchase, donation and enhancement callbacks retain their
  original authority and host-grant checks. Inert replicas never run gameplay.

Automated checks can establish packet and source behavior, not headset pixels.
The next two-client hardware run must compare the same cabinet/page, held item,
item fan, enchantress hand/card/options/hover and Temple furniture on both views.
