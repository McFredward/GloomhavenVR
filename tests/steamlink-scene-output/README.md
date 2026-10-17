# Current scene-eye output controls

The source helper consumes the current RGBA eye image. Coverage authorization,
head-camera/XR ownership and enable/menu policy belong to the caller. This suite
does not use the removed synthetic-pattern setting or its old runtime.

```sh
python3 -B tests/steamlink-scene-output/run.py --cpu-only
python3 -B tests/steamlink-scene-output/run.py
python3 -B tests/steamlink-scene-output/run.py --gpu --color-space Gamma
python3 -B tests/steamlink-scene-output/run.py --gpu --color-space Gamma \
  --gpu-mode edge-witness --shipping-bank /private/actual/scene.bundle
python3 -B tests/steamlink-scene-output/run.py --gpu --color-space Linear \
  --shipping-bank /private/actual/scene.bundle
```

Default execution checks the exact frozen profile/guard and actual net472 Unity
API typing. Optional GPU execution requires Unity2021.3.5, Xvfb and the selected
provider (`--backend vulkan`, or an explicit separate GL investigation). It is a
functional check, never a GPU timing benchmark. Coordinate GPU use first.

`full` tests2048/3408 sources and the four ordinary MSAA routes; `edges` isolates
four small real geometric MSAA cases, `edge-witness` one4x case, and `negatives`
isolates format/array/explicit-MS/alias/disposal refusal. The separate Linear
project checks that the Gamma-only helper draws nothing and the caller retains
ordinary output. The controlled Mono-camera sink has deliberately supplied
eye/policy labels; live XR authority is not asserted. Result row label/result
records the last fallback callback, not the earlier admitted callback.

The owning camera renders once per tested current frame. Its real OnRenderImage
callback copies an independent ordinary resolve, then calls the production
helper. Reference alpha comes from individually probed original source samples
and independent CPU bilinear weights. The readback/probe and extra reference
copy exist only in the test; the helper has neither. RGB is checked against that
same callback's ordinary source, including alpha-zero positive emission and
actual partially covered geometric MSAA pixels. A new repaint must appear at
once. Menu/off/unknown-frame and unknown-eye controls retain ordinary output.

The shader uses normalized profile placement into the caller's actual target,
with source alpha for the tiled mask and no second RGB premultiplication. The
profile guard is atY2400. A pinned shipping bundle can be loaded to test actual
compiled code; its path/hash/inode/size is recorded and checked after the run.
The module manifest must enable assetbundle/jsonserialize. A merely present
Shader object without an AssetBundle container is not a loadable package.

Every input is source-pinned and each terminated child is reaped before shared
cleanup. Bounded source/result/failure artifacts remain; only exact disposable
test caches may be deleted. Passing scope is recorded per case and targeted
repair; failed aggregates are preserved and never relabeled complete passes.
