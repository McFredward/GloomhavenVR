# Steam Link source startup admission

Run `python3 tests/steamlink-scene-startup/run.py`.

The entire production `SteamLinkSceneRuntime.cs`, including its actual
`SceneHeadOutput` callbacks, compiles into the portable fixture. External
Unity/XR/GPU interfaces supply controlled metadata and count ordinary/packed
draw requests. The fixture checks mid-pair component creation, complete-pair
readiness on the next frame, reordered eyes, duplicate labels and unrelated
frames, non-stereo and single-eye startup, a late XR
attachment change, transient Mono recovery, both current eyes, immediate policy
loss and permanent refusal of genuine camera/packer faults. Six semantic
mutants restore unready startup admission, permanent Mono failure, missing
late attachment admission, absent cohort readiness, activation on the second
eye of the same frame and cross-frame label accumulation; each must compile
and fail at its specific assertion.

A separate strict net472 compilation uses the real Unity2021 metadata and the
complete original coverage, packer and material sources. Actual camera callbacks,
GPU raster, command-buffer detachment, retained shipping-bank pixels and material
restore remain covered by `steamlink-loading-source`. This portable suite does
not prove native receiver pair acceptance or the headset's loading picture.

The first explicitly labelled complete Left+Right frame stays ordinary in both
eyes. Packed output begins only at the next actual frame boundary. Only frame
metadata and eye bits are kept, with no source texture, old eye/image, active
timer or live ownership check frozen for a later eye. Render processes are reaped before the shared
completion cleanup removes owned caches; source, logs and mutation receipts stay.
