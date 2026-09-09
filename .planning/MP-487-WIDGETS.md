# MB487 native widgets and remote cap sizes

Both supplied `LogOutput.log` files report ModBuild 486 on line 17. The three supplied screenshots were inspected before assigning causes: `buttongrößen.jpg` shows unequal remote cap sizes; `bonussymbole_lokal.jpg` shows the shield-1 preview above the class icon, whereas `bonussymbole_remote.jpg` contains serialized `Ability Card Name` tooltip text and a differently shaped mandatory damage outline.

## Remote cap sizes

The local and remote constructors use the same `BoardAnchors.FitCapSize` and `CardMesh.BuildBeveledKeycap` inputs. The defect is later: `InertCap.SetShown` treated the still-active object during dissolve as logical visibility. Any other button-mask edge reasserted hide, restarting `RemoteCapFx.PlayDissolve`, which saved the already reduced transform scale into `_shownScale`. That permanently reduced subsequent appear/cancel poses. Different cap histories therefore produced different settled sizes without any differing size dial.

`RemoteCapVisibility` now tracks requested visibility independently. Repeated hides do not restart a transition, and `Init` alone captures the authored scale. The rendering animator also refuses a duplicate dissolve directly. The new regression vectors exercise unrelated button edges during hide, reversal before deactivation, repeated show, and independent roles. The follow-up vectors bind the real SetShown/PlayDissolve production methods and include negative controls restoring both historical defects. Release compilation: zero warnings and errors. Shared test registration remains the integrator's file ownership.

## Bonus template content

The inactive original slot prefab bypassed `UIUseActiveBonusTooltip.Init/Reset` and `OnPointerExit`, leaving its serialized example tooltip descendants visible. The stage initialized `previewEffect` but never initialized or hid the tooltip. Logs confirm the serialized original prefab path (`USE BAR WIDGETS`, local lines 37073, 37296 and later), rather than the retired handmade replica.

`RemoteUseBarTooltip` now initializes the original `UIActiveAbility` description/tracker and original `UIItemTooltip` card under the inactive stage. Original pools are populated while inactive before the native visual initializer, preventing `NormalizePool` from waking standalone prefab controllers. `CActiveBonus.Layout` writes `Ability.ActiveBonusYML`, so both bonus and ability are detached copies and the layout is copied before generation. No active-bonus/gameplay operation or pointer callback runs. Hover visibility follows record25 and the existing record47 picker-open state. Serialized examples remain hidden until original content is ready, and the tooltip is hidden whenever the owner closes it or opens a picker.

Item cards use an inactive native pool borrow, cloned before the borrow is recycled in `finally`. Original background/class-icon references load through bounded `ImageLoadingContext` instances with cancellation on destruction. Clone materials use the shared `RemoteCardArt.PrepareNativeItem` visual adapter. Rebuilds also respond to item spent/consumed state. Material leases and asynchronous loading contexts are released on normal stage replacement, destruction, and partial initialization failure.

The owner's visible item tooltip remains visible during selection, with front/back exclusivity controlled by the shared card-face reveal gate. `ItemCardUI` has no native back asset. The user-approved card coverage therefore uses the existing shared VR item back and silhouette (`CardMesh.CreateBackMaterial` / `BindSilhouette`) on the original tooltip card's pixel rect. Action phase restores the native front. This is the authorized privacy cover, not a new widget replacement. Active-bonus bars always use their original prefab stage so a viewer-local tooltip cannot bypass that coverage. Bonus tooltip generation itself creates original effect descriptions/summon icons, not nested full private card artwork.

## Mandatory damage outline

The screenshots establish a rectangular owner outline and oval receiver outline; they do not isolate a single numerical border parameter. `TakeDamagePanel` writes its original `mandatoryTakeDamageHighlight` active state (native lines229/322). The mirror deliberately uses `driveFromSource:false` because the viewer's native decision row is stale. Record29 previously carried only the mandatory boolean, leaving the actual original Image geometry, border settings and renderer colors behind.

Record55 now carries that one actual public decoration: source time, full original RectTransform pose/anchors/pivot/size, Image/CanvasRenderer colors, enabled/active state, Image type/fill/aspect/PPU settings and the original canvas reference PPU. Sprite+texture names select the existing original UI asset; VR mip replacements resolve through `CardFaceMipBake.OriginalFor`, without guessing names or border constants. Maximum payload is254 bytes with two64-byte UTF8 names; invalid geometry/settings/names are refused with an attributable sampler diagnostic. No card or gameplay identity is transmitted.

The receiver applies these sampled outputs after both mirror/layout fitting and pointer paints. Duplicate decoded packets do not restart the source clock; continuous intermediate samples interpolate, while idle gaps over250ms start at the actual new picture. The host canvas reference PPU is restored when the descriptor/row clears so the following prompt cannot inherit a prior highlight's border scale. Missing original assets produce a diagnostic and no substituted outline.

## Validation and limits

Release compilation passes with zero warnings/errors. The focused wire suite checks exact little-endian output, the254-byte maximum, offset guards, every truncated payload, strict UTF8/asset bounds, malformed flags, finite values and unit quaternions, immutable snapshots, and stable-picture comparison. Cap production binding tests include mutation negative controls. Final run totals are reported in the worker handoff; shared registration remains integrator-owned.

Both local/remote native decision rows logged the same fitted720×48px at0.5208mm/px (local26029–26030, remote14598–14599), so an arbitrary remote scale override is not supported by the evidence. Source inspection and automated checks prove the corrected data/state paths; no headset or two-client runtime was available and final pixel parity still requires the next hardware test.
