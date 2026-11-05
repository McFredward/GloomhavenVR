# Confirmation visibility across world scales

Review base: `074f42adb`, runtime Build758. The initial November5 hardware logs
identified **Build753**. While the review was running, the maintainer supplied
new **Build758** logs and explicitly reproduced the failure again. The corrected
diagnosis below accounts for that later evidence rather than treating the earlier
placement repair as a complete solution.

## Evidence and cause

The first, historical native ESC confirmation is allocated and assigned to
`UI Scenario Esc Menu` at log line9858. The menu's accepted host scale is about
0.001. A second opening at line11285 has host scale0.050 after zooming out
(rig47.69 world units per perceived metre). Both retain the native persistent
confirmation and its original callbacks. Allocation failure is not recorded.
Log identities are pinned in `.planning/debug/popup-scale759/hardware-inputs.json`.

Build753 used the native producer's transform origin to place its confirmation.
The ESC screen is authored as a narrow left column in a1920px full-screen root.
Content fitting centers that visible column, leaving the root origin displaced
by hundreds of native pixels. The modal therefore appeared beside its visible
parent, where environment geometry could obscure it. Scaling out can make that
misplacement less troublesome; it does not mean the dialog failed to load.

The visible-owner-center correction already integrated for Build754 uses
`Panel.HostRect.position` for a whole-window producer, and preserves the exact
native mount for a nested producer. The32px foreground offset, rotation, native
scale and callbacks remain unchanged. That is a source-proven earlier defect,
but cannot explain away the maintainer's reproduced Build758 failure.

### Current failure: the blackout guard rejects a valid owned presentation

The new Build758 log records ownership at7189 and successful reveal at7197,
frame9285. At7198–7201, in that **same frame**, the general pre-conversion
blackout hides `Confirmation Box_MainMenu_pc` again. Its invariant reports
`tracked=True, floated=False`; later the native confirmation's visible-alpha
census is0 even though the correct owner/member geometry exists. The dialog
has been loaded and placed, but a second presentation writer disables its Canvas.
The copied inputs and hashes live under
`.planning/debug/popup-scale759/hardware758/` and `hardware758-inputs.json`.

The producer-owned persistent confirmation deliberately is not a generic
`ModalFallback.Converted` window. The general blackout guard considered only
that list and misclassified this separate valid lease as an unconverted window.
The repair gives its eight admission, standing-blackout, invariant and retirement
checks one bounded predicate: generic conversion **or exact confirmation-owner
lease**. Diagnostic `floated` remains truthful and a separate `ownedConfirmation`
field makes this distinction visible in future logs. Genuine unconverted windows
still receive the original protective blackout. Closing/releasing the lease
returns them to the normal admission path; no native decision is dispatched.

The audit found no additional clipping defect at the two recorded geometries:
the first head-to-owner distance is approximately2.36 world units, outside the
0.05 near plane. The32px foreground lies inside the owner capture's half-height
slab at every scale for this1080px ESC frame. Owner/member ordering already uses
a scale-aware distance tie and the explicit member tier. A generic numerical
weakness at extremely tiny laser planes is unrelated to these recorded sizes;
it is not claimed as their cause or changed speculatively in this review.

The popup audit follows the actual presentation routes rather than treating every
native class as an independent placement implementation:

| Popup family | Placement and authority |
|---|---|
| ESC Main Menu, Exit, restart/quit/tutorial confirmations | Explicit original producer scope, fitted whole-window center or exact nested mount; original Yes/No callbacks. |
| Character, level-up, perks, equipment and ordinary shop confirmations | The same `ConfirmationOnOwner.Apply` geometry and pooled-lease restoration; unrelated active windows cannot claim the request. |
| Generic scenario/map warnings without a surviving explicit producer | `DialogSurface` retains one original request and uses the first available head pose; accepted/manual poses remain fixed. |
| Global main-menu/platform/session prompts | Original captured-menu/ordinary fallback presentation, rather than guessed attachment to the most recently focused window. |
| Immersive NPC decisions and map-specific choice owners | Existing explicit external ownership wins; the generic confirmation adapter cannot add a competing float. Their qualified Build758 suites are inherited, not claimed as new multiscale hardware tests. |

Native source identity, default modal permissions and multiplayer gameplay
authority do not vary with zoom. This review does not add per-viewer rescaling
to shared windows or change their common world-space placement contract.

## Validation scope

ClassB: repair the identified confirmation admission predicates and strengthen
their final-image and native-input consumers. Shared capture scheduling,
placement metrics, native authority and protocol remain unchanged. Reuse the complete qualified
Build758 checkpoint recorded in `DEADLOCK-758-REVIEW.md` for unchanged areas.
The earlier cropped-owner fixture used orthographic views and direct graphic
raycasts; small numeric host scales alone did not exercise realistic perspective
or the laser-plane intersection. New tests must close those specific gaps.

Required routes are the owned/cropped ESC dialog (including nested owners and
accepted manual movement), the actual owned capture coordinator/final head
pixels, and the standalone generic confirmation through delayed head availability
and pooled reopen. Perspective samples include the legal0.02 scale-minimum
configuration and rig scales0.05,0.2,1,10,47.69,200,
with the actual near-plane policy `clamp(0.05*rigScale,0.01,0.5)` and both original
option graphics. A compiling historical-center removal control must fail at its
intended visible-pixel/input assertion. No test may invoke an exit callback to
recover presentation, change a shared opening's absolute geometry, or move an
already accepted window with the head.

## Focused popup results

- Native producer ownership, pooled close/reopen, accepted manual transforms,
  source-bound blackout admission/disable loop, actual laser-plane kernel and
  native graphic hits: **616 assertions and two compiling causal controls** over
  seven scale inputs. The old fitted-center and old ownership predicates fail
  their respective intended assertions.
- Actual SS/MenuDepth final-head coordinator: **68 assertions and two compiling
  controls**, two eyes and motion, opaque ESC column, scales1.15 (current hardware),
  1,47.69,200. The old ownership predicate disables the original nested Canvas and
  removes the dialog from the final eye image. The fixed predicate preserves it.
  An actually unconverted window still receives blackout; lease retirement removes
  the exemption. Owner-lease identity is an explicit input in this capture fixture;
  the separate native fixture executes the full production lease lifecycle.
- Standalone generic confirmation: **113 assertions and two existing controls**,
  perspective scale inputs0.9538,2.3845,9.538,1,10,47.69,200, independently imported
  original foreground, original option graphics and delayed first placement.

The first two routes combine full production owner logic, source-pinned guard
kernel/disable loop and the actual final-image consumer. They do not bootstrap
the entire native game or execute every outer fallback traversal. The repeated
real hardware guard firing is established separately by the Build758 log.

An exploratory absolute0.02 capture probe rendered plate/options but zero glyphs;
its direct original reference and early input ports also exposed scale/typed-canvas
limitations. Failed runs are retained, not converted into passing controls. That
absolute-scale glyph probe remains an explicit diagnostic outside the default
passing suite. It is not evidence that the tested headset's1.15 scale still fails,
nor a claim that all conceivable tiny SDF raster inputs have been certified.

Verified popup archives and per-input receipts are under
`.planning/debug/popup759/{standalone,native}/`; native current/old pictures were
also visually inspected by the integrator. See
`.planning/debug/popup759/native/validation-summary.json` for final wrapper/input
binding details and the preserved failed attempts. All worker run caches were
cleaned and both popup worktrees retired. GL/editor rendering and original widget
tests cannot certify Windows D3D11, headset visibility or a live Photon session.

## Additional maintainer requests in this delivery

The maintainer subsequently requested a finite animated recovery instead of a
lost-story snap, and three modes for additional action explanations: Always,
Never, AfterDelay, with a configurable delay. The final requested default is150s
(2.5 minutes), superseding the initial300s request. The board owner selects
the actual text/absence; the existing mirrored banner transports that result.
Observers must not run a second delay clock or apply their own preference.
Original mandatory choices and controls remain independent of these extra hints.

These are separate bounded consumers of existing story-pose and banner channels.
They do not broaden lost-window recovery to ordinary in-bounds windows, add new
wire grammar or change native continuation. Final union validation and their
specific controls are recorded after integration.

## Final union qualification

The integrated runtime is `ebce10774` (Build759); later prose-only descendants
retain this proof. This is a **classB focused delivery**, not a new complete
276-suite invocation. The full qualified Build758 checkpoint remains the baseline.
All17 worker-owned production paths matched their tested contributions before
the maintainer's final default correction, with no uncovered production change
beyond the integrator's build identity. The final150s delta is independently
bounded, replayed and reviewed as described in DECISION-HELP-759.md.

- Strict final Release and Debug: **0 warnings / 0 errors**,9.66/9.96s.
- Final complete source group: **16/16**,32.76s.
- Fresh actual wire executable: **306740 assertions**,6.79s (build3.18s).
  This invokes the golden executable directly and does not misrepresent it as
  another full `wire-tests.sh` local-suite invocation.
- Final default continuation: **462 production assertions + one affected
  compiling control**,11.45s; the earlier affected suites/controls retain their
  documented scoped proof. The actual owner sampler and remote Apply are bound.
- Production surface: **670→672 config keys**,278 patches unchanged,4849 search
  markers unchanged, no unapproved removal.
- Compiled comparison: **nine existing behavior types**,nine build/tool identity
  types and three new types; zero removed types, unchanged resource files and
  project references. The metadata differences are758→759 plus the recorded
  private-worktree branch name. The actual sender differs only by the finite
  recovery pulse in its existing rig-rate expression and build literals.
- Initial story proof:93 Unity assertions/16 controls; finite-cadence continuation:
  106/four controls; full containing reflow suite84 assertions plus seven
  source-extracted sender-expression checks/seven controls. Core optional guidance
  has62/five, and the unchanged common options/video/closure consumers have the
  exact qualified coverage recorded in DECISION-HELP-759.md.
- Both initial and final candidate manifests, stage durations, failed exploratory
  runs and final default-only compiled diff are retained. No unrelated complete
  gate was rerun after the bounded default correction. Bilingual guide checks and
  whitespace checks pass; no player guide or installation archive is changed.

The actor attention arrow remains at its existing20-second single-player wait;
the150-second option applies to extra board explanation text. Hardware outcome,
Windows/D3D11 final imagery and live Photon latency remain unverified in this
delivery. Current capture proof diagnoses/fixes the recorded1.15-scale blackout;
the separate tiny absolute0.02 SDF diagnostic is retained without a solved claim.
