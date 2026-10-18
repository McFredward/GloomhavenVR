# Render-quality lifecycle fixture

`scripts/check-render-quality-runtime.py` compiles the complete production
`Rig/RenderQuality.cs` against explicit XR-provider/configuration boundaries.
The defaults are copied from production source; the standalone marker is an
explicit mutable boundary, so the same implementation runs on both paths.

- Standalone retains saved startup allocation, allocation-free viewport changes,
  camera-phase gating, bounded delayed-reset repairs, and no live allocation
  fallback. Existing startup, MSAA, hot-reload and refusal controls remain.
- PCVR commits the selected scale to a running display's actual allocation after
  the existing350-ms slider pause, in either direction and back to1. It clears
  legacy viewport reductions, reports refused or mismatching readbacks, performs
  one attempt per distinct settled request, and never retries allocation every
  frame. The modeled provider updates its actual dimensions separately from the
  saved configuration; those changes are asserted directly.
- Twenty-eight semantic mutants and two bootstrap-order controls must fail for
  the expected reason. Five mutants specifically remove the PC path, omit its
  allocation write, replay stable lifecycle work, retain the old viewport cap, or reread the standalone profile marker during Update.
- A separate strict net472 build compiles the complete production class and
  provider boundaries without suppressing warnings. The executable fixture uses
  net8.0. Logs, source copies, hashes, results and cleanup receipts remain after
  generated compiler caches are removed through the shared completion API.

This establishes managed state/order behavior under an explicit provider model.
It does not execute OpenXR, allocate native eye textures, measure headset pixels,
or establish driver stability. No MR condition, projection/camera crop or extra
transport allocation participates in the PC resolution path.

After a bounded runner-only repair, `--production-only` reruns production and
net472 typechecking without repeating the semantic mutants. Its result explicitly
records the limited scope; it is not evidence of a new complete focused-suite pass.
