# Town-service item provenance / build 540

## Hardware evidence

The build-539 `Player.log`, lines 8490–8522, records two calls from
`TownServiceNativeAssets.PrepareItem` to `CItem.YMLData` for item ID 0. The second
passes through `PrepareRoot` → `NativeTemplates.Freeze`. Both occur while
`EnsureCard` constructs an inactive borrowed item template for publication.
`DLLDebug.LogError` opens the native error UI before `YMLData` throws. Catching
that exception cannot prevent its modal side effect.

## Correction

- Artwork reads the immutable `ScenarioRuleClient.SRLYML.ItemCards` definition
  by positive ID, without invoking `CItem.YMLData` or mutating its lazy cache.
  Zero/uninitialized prefab models are expected and ignored. Unknown positive
  IDs receive bounded normal-level diagnostics; duplicate definitions remain
  explicit exceptions.
- Validate a requested item definition before calling the native pool. Its own
  invalid-definition error path must not be entered by presentation code.
- Initialize the inactive borrowed widget's model with the validated requested
  ID. Never trust the pooled managed reference to be populated or current.
  Restore that previous reference in `finally`, including failed construction,
  and return the object without activating it or calling `Show`/`UpdateState`.
- All three services share `PrepareRoot`'s safe item scanner. Ability templates
  retain their original validated, inactive initialization.

## Validation boundaries

`python3 scripts/check-town-service-provenance.py` extracts and compiles the
production `PrepareItem`, `FindItemData`, `PrepareItemId` and `EnsureCard` methods.
Adversarial native boundary fixtures simulate the error-before-throw getter,
original catalogue, artwork requests and inactive pool ownership. Nineteen
behavioral assertions and four compiled negative controls pass. They exercise
ID zero, unknown/duplicate definitions, valid background and class artwork,
default/stale pooled identities, successful and failed restoration, and valid
and absent ability models. Source hashes and per-control logs are retained in
`.planning/debug/town-service-provenance/`.

Strict Release builds with zero warnings/errors. These checks prove the guarded
model/resource boundary, not NPC rendering, rack composition or headset behavior.
