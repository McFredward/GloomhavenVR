# Build703 — live PCVR resolution and native dashboard scope

## Actual Build702 evidence

Fresh Player/LogOutput files identify Build702, source f2b962a66. The two eyes
and current atlas carriers are4040x4040. Settings accepted4096, but the Frame's
actual vrlink decode format is3264x6400 at08:38:43.699. Previous701 decoded
3072x6144. These are delivered video formats, not proof of usable unpadded eye
width or the decoder's maximum. The acknowledged33% width increase produced
only about4% more decoded per-eye chart-height budget. It does not establish a
matching increase in game detail. Lease teardown preserved one external change;
the old log does not identify that key, so it cannot prove a delayed width clamp.

The existing v3 scene occupies2048/3168 of each carrier axis. Even before video
encoding, a4040 carrier represents about2612 scene samples. A settings readback
or larger source allocation cannot by itself undo that packing loss. Do not
label this build a restored-full-quality MR transport.

Inputs and SHA receipts are retained in `.planning/debug/steamlink-703-review/`.
Frame logs were copied read-only using the previously trusted host key; no Frame
file, setting, process or service was changed. The temporary local SSH control
connection was closed after those reads.

## Bounded production correction

Frame628's native allocation-crash containment unintentionally governed PCVR as
well. On PC, the eye-resolution slider now changes the running display's actual
allocation in the existing safe Update phase, after its existing0.35s slider
quiet period. It works in either direction and when returning to1.0, without a
VR restart. A previous viewport reduction is released so the requested scale is
not applied twice. There is no MR-specific scale, camera crop or FOV change.

The existing Frame standalone marker selects its unchanged stable-allocation
path. Read that marker only at the cold session boundary, never on every frame.
Standalone increases beyond startup capacity still require a new VR session;
the native crash-sensitive path is not restored there. PC provider refusals are
reported with bounded output and no automatic allocation retry loop. Engine
getters and modeled dimensions remain distinct from actual headset evidence.

Independent review caught two bounded follow-up defects before distribution:
the bootstrap can mark the session active before its display is running, so
waiting for that display must not consume a resolution request; and the new
net472 typecheck must permit automatic reference-package restore on a cold CI
cache. The readiness notification is deduplicated separately from actual setter
attempts, and the framework override is conditional on existing references.

## Chosen menu behavior and remaining work

The controller Steam button opens the LOCAL Frame dashboard in this run:
systemui reports `bSourceIsVRLinkRemote=false` at08:39:51 and08:40:00, with native
bar/footer overlays. The remote XR runtime subsequently loses input focus. This
does not mean that the PC video contains those local menu panels.

The maintainer explicitly chose game + local menu + camera simultaneously. A
tested source focus-pause checkpoint was therefore reverted before this build;
hiding the game or selecting ordinary opaque output is not the chosen feature.
The receiver currently returns to the exact original shader when its current
atlas guard fails. Showing the whole packed atlas instead of its scene rectangle
can explain black output and a changed scale, but no current dashboard-time GPU
guard or mapping record establishes that as the measured trigger.

The local RGBA menu panels can be retained. The next bounded native investigation
must compare one adjacent closed/open/closed transition: same selected video
route, per-eye image/extent, current native chart and reconstruction, and current
format-cell/guard result. Repair the proven changed route; identify any opaque
local backdrop separately. Preserve original overlay input/tracking, scene
projection/reprojection and current same-eye coverage. Do not guess-toggle fade
or theater settings, borrow an old mask, bypass guards or patch the Steam sender.

This is a medium-to-large receiver extension, estimated as several focused
implementation/validation days after that route is established, not a promised
number of headset rounds. No additional Frame test is requested by this build.

Full-quality RGB is a separate larger transport task: preserve the original RGB
and foveation path and carry genuine coverage with a proven current color-frame,
session, eye and coordinate association. Existing packet/reassembly foundations
do not provide that missing association or a production Vulkan mask binding.
App frame identity alone does not prove matching compositor reprojection. The
scoped architecture reports are retained in `steamlink-703-source/BRIDGE-DESIGN.md`
and `steamlink-703-review/ideal-dashboard/ASSESSMENT.md` under the debug directory.

## Verification boundary

The final production lifecycle passes181 assertions with strict net472 typecheck
and a separate cold-reference MSBuild policy check. Earlier full-focused evidence
passes171 assertions,27 semantic mutants and two bootstrap controls. The later
profile-cache and pending-display repairs each have their own new rejected
causal control. That composed evidence is not a new full-focused-suite or entire
project gate pass. The runner registers all29 semantic mutants for normal CI.
Four affected source checks pass; public surfaces remain669config/241patches/
4806log tokens. The final pending-display field changes only the diagnostic-field
census among those checks; its affected check is repeated on the integrated tree.

Only the live-resolution lifecycle and its affected source/build checks are
rerun. The accepted699 v3 source bank/native receiver and702 helper remain
unchanged; their prior GPU, codec, transport, wire and unrelated presentation
evidence is inherited. Final focused test and actual build/resource receipts are
retained under `.planning/debug/steamlink-703-review/`. Automated results do not
establish restored MR picture quality or the requested native dashboard result.
