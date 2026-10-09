# Build653: native Steam Frame standalone mixed reality

Date: 2026-10-09. Base: the independent Build652 integration. Development
version remains1.1.0; this is a dev delivery, not a published release.

## Implemented behavior

The existing installed Frame launch opt-in marker selects the standalone
backend. Its value is cached when the XR instance starts. A PC streaming to
Frame keeps the existing chromakey backend; headset name alone is insufficient
to distinguish those execution paths.

The existing Unity OpenXR feature queries the actual Wine/OpenXR system's
PrimaryStereo environment blend modes once per instance/system. AlphaBlend,
plus a begun live session, makes MR available. Missing capability, query failure
and activation failure leave normal VR usable. The grey choice retains its
original native tooltip: update SteamVR and the Proton version selected for
Gloomhaven, then restart. Pending checks have a separate bilingual explanation.
No guessed minimum Proton version or SteamVR version allowlist is used.

Unity1.10 queues blend requests. The controller requests alpha once and waits
for actual readback before changing camera clearing or suppressing the sky.
Its bounded deadline includes a maintenance grace for suspended applications.
Off cancels pending activation, rapid off/on cancels pending restoration,
external mode authors take precedence, and lost/destroyed handles are not called.

Native MR uses transparent-black clearing on the original head camera and
temporarily removes its HDR permission to prevent RGB-only intermediates.
Off/VR stop restores saved clear flags, colours, head HDR, sky and owned blend
selection. Other camera HDR permissions, MSAA, resolution, game materials,
camera count, assets, gameplay, network grammar and existing defaults are unchanged.
Existing MR readability backings remain UI-only, after accepted native activation.

Curated tiles, generic/Advanced switches and the fallback environment dropdown
share the compatibility guard. Already-open controls update on state changes,
without rebuilding every frame. An incompatible saved-on preference can always
be turned off. Native Frame hides the meaningless chromakey-colour control;
PC retains that control and its saved value.

## Native submission evidence

The packaged UnityOpenXR.dll SHA256 is
`2275da2750ebc9c815386604f73f0450b03fed6f44dafdeb15e978633e4866f5`.
Two disassembly receipts establish the official feature API and frame order:

- Setter RVA0x37850 queues the mode; getter RVA0x37170 reads actual submission.
- Every submitted primary projection frame calls the projection builder before
  frame-end mode adjustment. The builder resets layer flags6 (source alpha2,
  unpremultiplied alpha4); actual AlphaBlend3 retains source alpha and other
  modes clear it. An earlier opaque frame cannot permanently clear alpha.
- The actual blend mode is written into XrFrameEndInfo. No xrEndFrame replacement,
  private compositor hook, room-camera API or second XR instance is needed.

This proves the shipped plugin path, not SteamVR's actual headset composition.
Full reset-order and submission receipts are retained under
`.planning/debug/frame653-mr/native-submission/`.

## Executed validation and limits

The integrated runtime checks execute production source, not a copy of the
intended algorithm. Explicit boundaries are recorded with every fixture.

| Check | Result | Boundary |
|---|---|---|
| OpenXR probe/control |910 assertions | Fake native enumeration/control functions; production feature and facade lifecycle |
| MR presentation |27 assertions +2 causal controls | Actual Unity2021.3.5 camera state and rendered RGBA; XR/config/sky discovery are explicit inputs |
| MR settings |84 assertions +6 causal controls | Actual Unity uGUI/TMP, original game tooltip and raycast filter; XR/config and whole-menu skin are explicit inputs |
| Existing VR Options |7339 assertions +21 controls | Production options lifecycle fixture |
| Existing VR Options closure |1796 assertions +6 controls | Native closure/reopen ordering fixture |
| MR backings |258 assertions,56 binding assertions +17 controls | Existing UI-only fit/ownership fixture |
| MR materialization |35833 assertions +10 controls | Existing native backing materialization fixture |
| MR backing animation |576 assertions +7 controls | Existing animation/materialization switch fixture |
| MR scenario ownership |79 assertions +6 controls | Existing scenery-retirement and sky-furniture fixture |
| Options notes |462 assertions +3 controls | Actual Unity native row layout/glyphs |
| Graphics profiles |281 assertions | Existing profile/default boundary |
| Player help |2302 lookup assertions +8 controls | Exact bilingual setting/help coverage |
| Direct golden vectors |299715 assertions | Complete wire executable; not a new complete local-suite invocation |
| Source group |16/16 | All registered source gates |
| Strict Debug/Release |0 warnings,0 errors | Integrated production builds |
| Bundles |All game-format checks pass | Existing bundle parts and1594 figure derivatives; no asset change |
| Player docs |5 EN/DE pairs | Switchers, headings and links |

The new presentation registry entry includes both causal controls, and the new
MR options registry entry includes all seven native variants. Native probe/control
coverage remains in both local and hosted CI groups; real Unity graphics cases
are local-only, as with the existing graphics harnesses.

Initial integrated twelve-suite evidence records ten passes and two failures:
the MR scenario fixture's obsolete exact chromakey-only source assertion, and
Build652's overlong wall-mode tooltip. The MR assertion now explicitly preserves
the old VR/config guards and pins the new accepted-alpha guard; only its affected
scope was repeated, passing79 assertions and all six causal controls. The wall
help repair is owned by the independent652 integration. Rebase onto6aa8ca33f
and its focused repeat pass2302 lookup assertions and all eight controls. These
original failures remain in the receipts; all twelve scopes now have passing
composite evidence, without repeating the ten unchanged successful scopes.

Final independent source review found that Shutdown reaches Forget without
ClearRows. Forget now also releases the MR availability delegates without
touching dead native objects; the actual UI production variant repeats84
assertions and binds both cleanup paths. The earlier six unchanged UI causal
controls remain inherited rather than falsely reported as freshly repeated.

The current private compiled comparison adds three MR types and changes only
MixedReality, OpenXrEnvironmentBlendFeature, Loc and VROptionsTab behavior.
Eight existing build consumers differ only by652→653. BuildInfo differs only
by the truthful worker branch stamp. No unrelated runtime type is changed.
The final652 follow-up comparison is recorded before push.

Do not describe the focused scope as a new complete184-suite pass. Unchanged
areas inherit the independent652 integration's full-catalog evidence; the
maintainer explicitly asks not to repeat already-passing unrelated checks.

## Hardware acceptance still required

Latest supplied Frame evidence is Build651, SteamVR/OpenXR2.17.10 through
`C:\openxr\wineopenxr64.json`; it contains no core blend-mode enumeration.
Absence of FB/HTC vendor passthrough extensions does not rule out core alpha.
This run cannot establish whether that particular Proton bridge supports MR.

On Build653, retain the bounded `OpenXR environment blend capabilities` and
`Steam Frame native passthrough` reports. Test the grey hover explanation when
unsupported; if available, check solid game/windows over the real room in both
eyes, rapid toggles, map/scenario changes and VR restart. Water/particle edges
and the real Proton/compositor result remain unverified by local pixel fixtures.
PC chromakey regression coverage passes, but there is no new connected headset
or stock Steam Link passthrough claim and no measured MR FPS improvement.

Implementation rationale and primary references:
[FRAME-PASSTHROUGH.md](../docs/performance/FRAME-PASSTHROUGH.md).
