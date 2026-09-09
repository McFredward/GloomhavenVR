# MB489 card appearance lane

Base: `3d0057b8` (`dev`, ModBuild488). Worker: `codex/mp489-cards`.

## Evidence and source findings

Both supplied `LogOutput.log` banners identify ModBuild488 at line17. The card findings
have no dedicated supplied screenshots; the two health screenshots belong to the decision lane.

- Host18360 reports WardingStrength as `REMOTE ACTIVE WASH ... BURN`; host20175 reports
  TheMindsWeakness as `GHOST`. `RemoteActiveCards` unconditionally chose this wash from the
  card's eventual destination. That is a source-proven prediction, not an observation of the
  owner's visible widget. The user's locally blue card can therefore disagree with every peer.
- Host51752 and remote22124 report `REMOTE HAND CARD WASH` / `[HandFan] Ghost`. The driver
  queried this receiver's hidden `AbilityCardUI.fullAbilityCard` task flags. A restored model
  hand does not guarantee that this inactive pooled UI has run the owner's `RestoreCard`.
  Removing that receiver-side ramp prevents recovery from starting an invented two-second wash.
- Host51451's `PEER CARD FIRE [Pile] DRAWN` only covers the first card per surface. It cannot
  establish that every burnt card displayed fire. The mirror inherited its local pooled proxy's
  overlay active state and canvas-group opacity, then called that the owner's picture. That
  equivalence was never established. Native output now supplies those actual owner properties.
- `RemoteAvatar.UpdateHeldCard` previously activated a slab from rig0 before the independently
  delivered presence36 address was available. `RemoteHeldCardFace` explicitly chose a back on
  the unresolved path. The root lane couples pose/address/actor in the rig packet; the held
  adapter now waits for the matching actor and complete front before exposing a public card.
  Selection still displays covered card bodies. Ordinary front hosting already switches the
  body's front material away from the back before the frame renders.

## Implementation

`CardAppearanceSampler` reads visible factory cards owned by this client after native presentation.
It samples original serialized `CardEffects` references: seven Images, four affected TMP labels,
the original flame Image, and up to eight original CanvasGroups. All reads are cosmetic. It never
starts a gameplay controller, mutates a model, spawns a game widget or sends a card identity.

Additive58 in dedicated messages12/13 carries existing actor/list/seat/count addresses, exact
Graphic and CanvasRenderer RGBA, each available native card/flame shader channel, authored
burn/ghost texture roles, TMP gradients, and original group alpha/enabled/ignore/active state.
Groups have stable hierarchy binding hashes; mismatched role bindings refuse the whole card.
The maximum32-card frame with every supported role/channel is **40966 bytes**, below the
**45056-byte** codec allocation. No existing grammar changes. Unknown records skip by length;
known node/card prefixes never publish partial snapshots.

The receiver validates source list/count against its replicated model, interpolates continuous
owner values on the native playback clock, and applies them last to owned clone materials.
The shader footprint remains locally measured in the mirror canvas, as required for world-space
rendering. Original controllers remain stripped. Hand, held, active and pile surfaces bind the
same owner output. The flight lane binds its burn/flight art through the same API.

An initially exposed clone clears inherited proxy wash/flame state while its first owner frame
is pending; it does not restart a two-second estimate from local task flags. Card-face permission
still goes through `RevealGate` before any front or native output is drawn.

## Validation and limits

Standalone `CardAppearanceVectors.Run(t, repoRoot)` adds **746 assertions**: independent golden
bytes, unknown records, complete-prefix/truncation rejection, finite values and address domains,
duplicate roles, maximum population, all node boundaries, and source bindings. Full worker wire
suite: **250230 passed**. Deliberately zeroing the serialized owner's renderer RGBA causes
**34 failures**; the mutation was restored. Temporary registration files were restored because
the root owns permanent test registration.

The surface/group checkpoint builds Release with **0 warnings, 0 errors**. The final held-front
adapter depends on the root's new `HeldFaceAddressReady` / `HeldFaceActorId` APIs and is checked
by the integrated build. No headset appearance or animation timing has been verified on MB489.

The transport intentionally refuses an unresolvable address or an unsupported complete native
population rather than applying a partial or guessed owner frame. A temporarily unresolved
public held card waits for its front; it does not flash a secret back. The next hardware test
must inspect both owner and peer pictures, including all burnt browser seats and recovered hands.

## Integration review follow-up

The review additionally found two presentation-state gaps. Losing the previously resolved
owner address now clears stale wash once, including a recovered card that keeps the same widget
identity. Explicit burn/card flight surfaces retain their separate semantic burn while their
source list disappears. A native hidden root can also reappear: playback checks the policy-owned
host's activity, not the native clone activity that a previous frame may have switched off.

`CardEffects._useLowEffect` reads the platform's SimplifiedUI setting; Initialize substitutes its
serialized `_lowMaterial` on all seven images. The owner now transmits the actual assigned
material variant, and the mirror reads the original `misc_gui/AbilityCard/gui` prefab materials
(the asset used by `PersistentData.CreateAbilityCard1`) or the original serialized low material.
No prefab is instantiated. World-space FX bounds and flame draw ordering are applied to private
material copies for either variant. The wire size is unchanged; the variant uses a validated flag.
The new variant and continuity vectors add six assertions to the original746.

## Ordinary recess review completion

Ordinary round/decision recess clones still called `DriveUsedCardFx`, a receiver-clock replay
of whole-card state, although active cells, hands, piles and explicit flights had native bindings.
Every successfully cloned ordinary recess now binds the owner appearance immediately. Its
spent-half adapter retains interaction state but no longer starts the synthetic whole-card clock;
the owner shader, group and flame output paints in LateUpdate.

Map fan clones (including their cached-face path) and held map cards now bind the actual model
context as well. They have no scenario actor address, so this clears inherited pool decoration
without claiming a nonexistent owner snapshot. Explicit burn/card flight fallback ownership is
unchanged. This covers all ability-card `RemoteCardArt` construction sites together with the
active/flight worker integrations; native items use their separate existing presentation pipeline.

The full worker wire suite passed **250238 assertions**, including **754 appearance assertions**.
Two added source regressions reject a missing ordinary recess binding/synthetic replay revival
and loss of map model context. Shared registration was temporary and restored. Full strict
compilation of the held adapter uses the root integration's atomic-address Avatar APIs. No
headset visual outcome is asserted.
