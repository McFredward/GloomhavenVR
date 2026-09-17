# MR backing lifetime and window effects — build 522

The user reports empty MR backgrounds remaining after a window disappears, sometimes
obscuring a newly opened window. They also request the window's materialisation and dust
dissolution instead of an independently appearing/disappearing rectangle.

## Source findings

`WindowMaterialiseRunner` fades the original CanvasRenderers over the first 58% of its
effect. The remaining interval contains only flying debris. MR backings were opaque
MeshRenderers and did not participate; host lifetime and four-frame ink samples could
keep them visible after their content was gone. Native visibility could also change
between cached ink measurements on local and remote surfaces.

The supplied log files remain build 519 locally and build 500 remotely. They are not
a matched build-522 multiplayer run. This diagnosis is source-proven and motivated by
the user's observation; there is no new headset capture proving the final appearance.

## Implementation

- Capture fitted native ink before the effect's first alpha write. Reuse the actual
  runner's element progress and host-space field; no separate backing timer or tail.
- Use the already bundled Overlay shader and a bounded reusable vertex-alpha grid.
  The original window's debris remains the visual dust effect. Partial backings blend
  without depth writes; at full dissolution their object is hidden in the same frame.
- Preserve close callbacks, exact-once release, cancellations and watchdog behavior.
  Decoration failures cannot prevent native continuation. Closed backings cannot
  reappear while Unity defers host destruction. Reopening explicitly resets the state.
- Restore the ordinary quad after appearance and continue resizing from its final
  bounds, avoiding a second initial growth. Release owned meshes/materials on close
  and MR-off; never destroy the borrowed primitive mesh.
- Retain effect progress and geometry even if MR was off when the window opened or
  closed. Enabling MR mid-effect cannot resurrect an opaque debris-only background.
  Existing modal geometry is reused where available; no GPU resources are created
  while MR is off, and completed off-state episodes remove their metadata.
- Record the native graphics already admitted by the ink walk, with its existing
  clipping, original content scope and transient exclusions. Read their live visibility
  and alpha independently of the geometry cadence, including the late presentation
  pass. Local and inert remote surfaces follow native hides/fades without another clock.

No bundle, config, gameplay, native callback, wire grammar or release branch changes.
All multiplayer participants must use build 522.

## Validation

Focused production harnesses and the strict Release build cover the changed MR, ink
and effect paths. The user requested affected checks rather than repeating unrelated
local suites. Exact final counts are recorded in STATE.md and the integration commit.
The new harnesses also run in the existing full dev CI job; main release CI continues
using the previously implemented trusted dev evidence.

Hardware checks still required:

1. In MR, open and close story, encounter and modal windows: backing and content form
   and dissolve together; flying dust must not leave an opaque empty rectangle.
2. Close and immediately open another window; rapidly reopen the same window.
3. Toggle MR during appearance and disappearance; verify no flash or invisible blocker.
4. Check hidden/empty local and remote rows, native fades, and resized initiative rows.
