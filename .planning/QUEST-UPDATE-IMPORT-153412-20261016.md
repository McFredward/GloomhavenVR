# Quest compact update: plugin import and explicit rebuild consent

## Observed evidence

Support153412 identifies source45faa64a5, runtimeB673 and an actual Mod Update
of the retained B672 APK, SHA256
`191411bf837ced926cd32e2d182e47806aab4a471d213b9696997cbb09fd4ab5`.
Its original input is `d4e76da00ec2...`. Inspection selects compact-code, with
code/native-helper changes, no authored-art change and no rebuild reasons.

The real Editor later cannot load generated plugins. GH.Runtime and TMP contain
self assembly references; GH.Runtime and SM.Consoles also reference the host
System.Private.CoreLib, which this Unity player does not provide. Cascading
import errors hide ApparanceAbout from the runtime MonoScript census. The helper
reports the missing script, and the old host automatically routes the typed
exception into the full game's conversion path. This is an import defect,
not demonstrated serialized asset incompatibility.

The fallback really copies the full selected game and launches AssetRipper;
it is not merely a confusing progress label. The report's current full PC
inventory differs from the signed APK inventory in exactly one subsystem
manifest, UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json. The compact
update correctly qualifies original managed inputs independently of that delta.

## Existing results and interrupted data

The latest completed build still names the original B672 APK and hash. No
compact repack or installation is reached before this fallback. The new raw
workspace `1ca9ea820183...` has no closed core-recovery receipt at capture time.
The older workspace `8296c91adce9...` still has core and full-recovery progress
receipts. Thus the log and keyed write paths show an additional incomplete
conversion, not a deletion of the existing APK or old completed recovery.

This support report is not an independent byte scan of the user's disk, nor
proof that all caches are intact after the capture. Do not claim such a scan,
require a blanket rescan, or delete either tree to fix this code import failure.
Keep the original APK, banks, profiles, signing identity, full projects and
compiler Library. Repair the affected small generated code artifacts through
their current source producer, then let Unity invalidate its affected imports.

## Targeted compiler repair

Cecil's default importer turns a hook signature's reference back to its target
assembly into a self AssemblyRef. The target-aware metadata importer maps only
an exactly identical assembly identity to its local module; another version
with the same name remains external. The resolver applies that importer to the
actual loaded game assemblies, not just the mod discovery module.

The standalone producer imported OfflineProfile methods through host reflection.
Import those signatures from the actual netstandard Runtime DLL metadata instead.
This removes the accidental System.Private.CoreLib dependency without changing
the intended profile/DLC behavior or suppressing Unity reference validation.
Actual fully woven, adapted and package-bound plugins are audited, followed by
a small real Editor load/MonoScript fixture. No original game asset is repaired
or regenerated to fix these metadata errors.

## Rebuild decision contract

The maintainer explicitly requires confirmation before a necessary full rebuild
(2026-10-16). Both an incompatible initial plan and a typed registry rejection
now pause the Mod Update. The host records rebuildDecisionRequired with concrete
reasons, preserves the selected route/results and never calls rebuild_update.
It must not create an original game snapshot, raw export or full project on that
path. A fresh plugin import error plus a missing MonoScript stays an import
failure; it is not promoted to serialized migration. Stale Editor logs retain
the existing refusal to classify a new failure.

The Wizard offers retained update retry/diagnostics and a separate explicit
new-build decision. Reviewing or cancelling that decision does not start any
producer or clear the existing APK/workspace. Normal ownership, signature,
package ABI, script layout and matched IL2CPP/metadata checks remain mandatory.

## Validation boundary

Focused update tests exercise initial incompatibility, late typed rejection,
repeat attempts, fresh import failures and retained file/APK bytes. The real
managed producer and small Editor import/load fixture are recorded in the
ignored B673-update-route-153412-20261016 evidence directory. No full game
conversion, APK build, Quest transfer or hardware picture is claimed for this
repair. This host compiler repair does not bump runtimeB673.
