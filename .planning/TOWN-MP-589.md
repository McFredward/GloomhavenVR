# Town multiplayer follow-up — ModBuild 589

## Evidence and scope

The main checkout's `debug/LogOutput.log` and `debug/Player.log` are the 29 September
Build 587 host run. The host log confirms a connected player 2 with peer ModBuild
587. Nine JPGs in `debug/npc_probleme` show the merchant's returning flat window,
gray remote item surfaces, an empty remote cabinet, and displaced offer art.
The files currently under `debug/remote` have a Build 582 banner from 28 September;
they are **not** the peer trace for this Build 587 run. This limits any conclusion
about the peer's actual publication cadence and the offered card's wrong position.

## Source-proven corrections

| Symptom | Cause in source / log | Build 589 change |
| --- | --- | --- |
| Merchant item leaves the palm and the flat shop appears after a delay | The host self-granted the town transaction but did not write `LastResponse`; `Unavailable` treated that live grant as a three-second unanswered request. Host log then reported `Town transaction coordinator unavailable; restored the original VR window for this visit`. | Record self-grants as responses and recognize a still-valid grant before declaring it unavailable. A genuine merchant coordinator or physical confirmation failure now returns the uncommitted offer, keeps the native shop masked, and shows a localized in-world retry cue. |
| Gray peer item fan, offered item and cabinet cards; cabinet empties after category changes | The host logged 264 failed captures of `Sarala-Regular SDF Atlas` and 40 of `AbilityCardSpriteAtlas`. These are original art/font textures reused through multiple Unity wrappers, so the mirror rejected entire card/price modules as ambiguous. | Bind only the unique verified original serialized descriptors (name, size, format, mip count). Keep unknown same-name assets rejected. The existing shared rack/page clock remains the animation authority. |
| Peer cannot use merchant category buttons, especially without a character or during another offer | The public cabinet's input gate required an already-present observer rack with no detached members and a free transaction lease. Browsing has neither gameplay requirement. | Let any visitor browse an unlocked cabinet; category selection claims the public catalog authority. Placing a buy/sell card still obeys its separate transaction lease. |
| Merchant decision text/buttons turn in visible steps | The author-facing palm rotation copied yaw immediately; the receiver's tween ended at 100 ms before the next roughly 5 Hz sample. | Ease local yaw once per frame and interpolate peer decision parts through the bounded sample interval. |
| Enchantress extends a hand but offers no card target | Debug log repeatedly says `no owned map card in active fan`, although the map fan reported ten local cards. The handoff reused the stricter fan-reorder controller-id reflection gate, which can be unknown while the room connects. | Require a live card from the selected local loadout and native local ownership, without the reorder-only reflection gate. |
| Priestess has no purse / hand mode | The map character floor was skipped whenever a stale scenario singleton remained while the Guildmaster map was active. Temple purse focus also depended on a parked transaction at another resident. | Use the map room driver's active lifetime, consult actual assignment when known, and let purse inspection depend on the local priestess visit and owned character; eligibility still gates the real donation. |
| Character UI intermittently shows nobody | Nine host `SELECTION GUARD DROP EDGE: map` lines had no following restore result because the stale scenario singleton made `InMapPhase` false. The selected slot's cached local flag may also lag a new assignment. | Restore during the actual map phase and use the authoritative controllable owner before the cached flag when the registry is answerable. Unassigned multiplayer visitors may still have no selection. |

The displaced gray offered card has no separately demonstrated pose error in the
available logs. Source review found that its original item-card transform is
published relative to the shared map frame and replayed under the visitor's
private town module. Restoring its original art and removing missing-module
gaps may change the observed picture, but this position still requires a fresh
two-client headset comparison before calling it fixed.

## Verification

The merchant-handoff and town-interaction runtime suites, asset-identity and
public-catalog suites, and strict Release builds passed in isolated worktrees.
The integrated tree must pass the full `refactor-guard`, strict Release build,
and EN/DE documentation checks before distribution. A headset comparison must
use Build 589 on both players and collect both players' logs. Check the original
item art on the owner's fan and hand, the public cabinet after every category
switch, each resident's handoff, the selection floor, and the offered card's
world position from both viewpoints. Automated checks cannot certify 1:1 pixels.
