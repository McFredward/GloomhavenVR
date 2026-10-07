# Flat/VR quest selection review

Scope: selection identity and original popup lifetime, based on `dev` at
`819a9a9ee` / ModBuild 638. The parent review owns ready-up, travel parking,
modal conversion and final integration. This lane owns
`MapLocationInteractor.cs`, `NativeMapQuestSelection.cs` and its focused fixture.

The reported observation is a missing or briefly appearing client Accept panel
with both VR and flat hosts; rejoining restored it. No paired hardware logs or
screenshots were supplied for this report. The source defects below are proven;
they do not establish the exact sequence on that user's headset.

## Source findings and repair

`UIMapMultiplayerController.ConfirmSelectedLocation` sends the original
`SelectQuest` action with `LocationToken(Location.ID)`. Flat clients and hosts
retain this native path. A receiving client first gets
`ProxyHostSelectedLocation`, which stores `hostSelectedLocation` and installs a
native confirm callback. Its `PreviewQuest` subsequently selects the local
marker, opens the normal selected popup and enables the original ready-up.
`HostSelectedQuest` can therefore already exist while both local marker staging
and `UIQuestPopupManager.IsQuestShown` still say nothing.

The old VR decision observer read only its local/VR-record20 staging field.
This missed a native flat-host proposal and could publish absence while the
original prompt had a valid subject. It also missed native cancellation when
there was no VR sender. The observer now reads the native host proposal first on
online clients, and otherwise reads the native selected marker. Positive native
evidence protects the popup and is available to callers before this frame's
observer catches up. A measured cancellation still spends the original one-second
settle; missing initialization supplies no first-sight cancellation. Replacing a
quest changes readiness identity immediately, while recreating the same location
keeps its identity.

The comparison remains the game's **location** token ID. A travel quest's quest
ID can differ from its ending village's location ID; comparing quest ID with
record20's location identity would invent a decision change on preview.

There are two native `UIQuestPopup` instances. `multiplayerQuestPopup` is a
proposal hover, owned by `clientSelectedQuest`; `selectedQuestPopup` is owned by
`selectedQuest`. The original confirm button hides the former before invoking
`PreviewQuest`, which opens the latter. A type-wide popup search or enum-ID match
cannot distinguish them. The helper returns the exact current original and
checks each popup's own subject independently. When a host proposal exists,
unrelated client browsing never becomes its confirmation window. Reflection
supports both original private metadata and publicized references.

Automatic missing-popup deselection now preserves a live native host proposal.
Late VR browsing record20 edges, including null, cannot run a synthetic
`Deselect`/`Select` over an already host-authoritative native proposal. Ordinary
uncommitted browsing, manual native deselection and the original map interaction
mask remain exercised. The helper only reads game state; it sends no action and
writes no game field. No wire layout, gameplay authority, asset or default changes
are introduced.

## Focused validation

`bash scripts/check-map-quest-selection-runtime.sh` passes **358 assertions**
against original/private metadata and the same **358** against publicized metadata,
with **18 exact native source bindings** and **9 rejected causal controls**.
The suite links the full production helper and extracts the actual production
observer, accessors, adoption and deselection methods. Committed native proposal,
preview, cancellation, popup manager, selection and host sender methods are
verbatim checked against the read-only game source when available.

The matrix routes a native location token from flat or VR host input to a distinct
VR client world, with a flat or VR companion world, both with an already initialized
observer and with a rejoining first observer. VR host input uses production
`AdoptSelection`; flat host input uses native `Select`. All host variants use the
original `ConfirmSelectedLocation` sender. Additional paths cover offline/VR host
selection, native map lock, uncommitted shared browsing, linked-quest direct
preview, delayed native controller initialization, replacement objects, travel
location identity, and native cancellation without VR record20.

Controls inject native-proposal blindness, staging-only observation, the wrong
popup instance, late browsing overwrite, premature automatic deselection, unknown
initialization interpreted as null, wrong token identity, enum-ID substitution,
and shared popup subjects. Each reaches and fails its causal assertion.

Existing map-flow passes **2,012 assertions / 9 negative controls**. The source
group passes **15/15** at receipt
`.planning/debug/test-runs/20261008-000120-67a1b393/results.json`.
Strict Release and Debug both compile with **zero errors / zero warnings**.
These checks cover this lane; they do not constitute a complete integration gate.

An attempted source-only `refactor-guard.sh --suite source` invocation used an
unsupported flag and launched the complete runtime group after the source group.
That unintended runtime launch was stopped; its cancelled receipt is retained at
`.planning/debug/test-runs/20261008-000143-2fcd1d88/results.json` and is **not**
passing evidence. Before cancellation, unrelated `town-service-mirror` failed to
compile its partial fixture because `ClearPackedSprites`, `SpriteKey` and
`ScanPackedSprites` were missing. No town files were changed in this lane.

The new fixture executes native callback bodies and production decisions with
simulated time and audiovisual/Photon boundaries. It does not render the actual
headset picture or establish real network delivery. Native ready-up membership,
window conversion, docking and transport validation belong to the coordinated
integration review. Real mixed headset/flat lobby acceptance remains unverified.
