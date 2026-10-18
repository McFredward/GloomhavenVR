# Steam Link source startup admission

Run `python3 tests/steamlink-scene-startup/run.py`.

The entire production `SteamLinkSceneRuntime.cs`, including its actual
`SceneHeadOutput` callbacks, compiles into the portable fixture. External
Unity/XR/GPU interfaces supply controlled metadata and count ordinary/packed
draw requests. The fixture checks mid-pair component creation, complete-pair
readiness on the next frame, reordered eyes, duplicate labels and unrelated
frames, non-stereo and single-eye startup, a late XR
attachment change, transient Mono recovery, both current eyes, immediate policy
loss and permanent refusal of genuine camera/packer faults. Dashboard coverage
uses the actual cached XR input-focus contract: false or unknown focus suspends
source admission and ownership without changing the native transport request,
MR preference or quality lease. Repeated open/close, late focus loss after
pre-cull and a mid-pair focus loss all revoke the epoch without latching a fault;
resumption requires a fresh complete-eye cohort. Nine semantic
mutants restore unready startup admission, permanent Mono failure, missing
late attachment admission, absent cohort readiness, activation on the second
eye of the same frame, cross-frame label accumulation, unfocused source admission,
unfocused callback ownership and failure to revoke late-unfocused epochs; each must compile
and fail at its specific assertion.

A separate strict net472 compilation uses the real Unity2021 metadata and the
complete original coverage, packer and material sources. Local runs use the
installed Unity2021.3.5 Editor metadata when present; hosted runs automatically
use the already restored `UnityEngine.Modules/2021.3.5` NuGet package. Both paths
record hashes of all five module inputs. The fallback copies references into an
owned temporary layout and never writes a shared reference directory.

Use `--typecheck-only --metadata nuget` to check only that hosted metadata branch
without repeating unchanged state/mutant cases. `--metadata unity` requires the
installed Editor metadata; `--unity-managed PATH` selects its Managed directory.
The NuGet/package root respects `NUGET_PACKAGES`. Actual camera callbacks,
GPU raster, command-buffer detachment, retained shipping-bank pixels and material
restore remain covered by `steamlink-loading-source`. This portable suite does
not prove native receiver pair acceptance or the headset's loading picture.
It also cannot recall an eye already submitted before an asynchronous focus event,
or establish a SteamVR compositor/dashboard picture from supplied XR metadata.

The first explicitly labelled complete Left+Right frame stays ordinary in both
eyes. Packed output begins only at the next actual frame boundary. Only frame
metadata and eye bits are kept, with no source texture, old eye/image, active
timer or live ownership check frozen for a later eye. Render processes are reaped before the shared
completion cleanup removes owned caches; source, logs and mutation receipts stay.
