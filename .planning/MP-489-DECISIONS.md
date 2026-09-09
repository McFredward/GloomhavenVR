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
