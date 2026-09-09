# MB489 decision and damage-preview review

Base: dev `3d0057b8`. Both supplied LogOutput.log banners are MB488 (line17).
Inspected `boni_healthbar1.jpg` and `boni_healthbar2.jpg`: the orange damage label remains2
while the green endpoint changes under shield1. This is picture evidence, not proof of the
exact sampled panel fields.

## Native pending-health contract

`CActor.Damaged` saves original health before subtracting tentative damage. The rules retain
that original value in `GameState.CurrentDamageData.PreDamageHealth` throughout the damage
response. Read that value while the matching response is pending; never reconstruct it from
the first observed health frame. `TakeDamagePanel.CalculateCurrentHealth` supplies projected
health after local choices. Previously `DamagePreviewSurface` combined that projection with
an independently stale damage amount, which extended the total green-plus-orange bar.
The repaired bridge fixes the original-health endpoint and subtracts projected health to obtain
its actual cost. It uses original HealthBar/InfoBar presentation methods and original comparison
colors. Record57 transmits the actor-addressed public picture; no model mutation/card identity.

## Original bonus tooltips

`UIUseActiveBonusTooltip.Show` reparents actual content through `UIManager.HighlightElement`
onto the flat highlight holder, outside the converted owner bar. The peer's inert original clone
stays under its world canvas, explaining the asymmetric tooltip. Restore only those highlighted
original tooltip roots through the paired native `UnhighlightElement(..., unlockUI:false)`
presentation operation during Update and LateUpdate. Native content and visibility remain intact;
no selection/controller method is invoked on a mirror.

## Validation

Health/tooltip code: strict Release build0 errors/0 warnings. New golden/negative codec vectors
are in DamageDecisionPreviewVectors (root integrator registers the new source and test file).
Headset output and replay timing remain hardware checks after integration.

## Character attribution and spectator presentation

Record60 separates pending actor attribution from whether that actor's decision is visible on
the sender's board. Existing decision records continue sampling native pending content while
focus hides its canvases. CharacterDecisionPresentation provides two views over that same content:
board visibility follows60; a character viewer gets the pending character's content regardless
of the owner's current focus. Actual claimant lookup supplies the owner, never the peer who
happens to be viewing that character.

CharacterDecisionMirror reuses the existing original decision row and use-bar clone renderers,
including native intermediate animation histories and mandatory image state. It is attached to
local foreign-character views and to remote boards displaying another character. The latter
resolves canonical owner data directly, so there is no rebroadcast/echo and no transfer of
interaction authority. When the character belongs to this client, that remote board reads the
same original local producer data through a read-only adapter.

The shared prompt now clones the game's original HelpBoxLine instead of drawing a new TextMeshPro
label. All stage objects stay under an inactive parent until native behaviours have been stripped.
No spectator canvas is registered with laser/poke routers and no mirror runs gameplay callbacks.

Remote health-preview application validates the actual character claimant, finds the original
world-space controller by stable actor GUID-derived ID, and repaints the same native methods as
the owner. Closing/removing a peer drops only the dedicated peer focus request.

Current strict integration build:0 errors/0 warnings, with a temporary copy of the integrator's
owner-lookup seam for compilation (that copy is intentionally excluded from worker commits).

Full worker wire suite with the two new test registrations:249519 assertions passed (35 added),
including explicit negative controls removing actual claimant lookup and original tooltip restore.
The actor-attribution review also covers short-rest/confirmation prompts through the dock's
existing PromptOwner resolver, and detached mirror hosts now hide when their board mount hides.
