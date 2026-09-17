# Map-window MR backing bounds — build 523

## Evidence and cause

The user supplied `händler_mixed_reality.jpg`: the merchant artwork and grab bar have
their normal lower edge, while a large empty MR rectangle extends above the artwork.
The updated local log identifies build 522. Remote logs remain historical build 500,
so this is not a matched multiplayer capture.

The shop's authored frame stays 1920x1080, y -540..540. Reopening produces content/hit
envelopes reaching y 1370 and later 2160; the capture log grows vertically by 544 px.
Those are content/capture diagnostics, not measurements naming the offending MR graphic.
They corroborate overlarge measured extents but do not prove which individual widget caused it.

Two concrete source gaps explain how unpainted space could enter the MR rectangle:

- Ordinary ink measurement uses layout rectangles. A text field can reserve much more
  space than its actual glyph mesh paints. Those rectangles serve chrome/layout purposes
  but are not a reliable tight background for the visible window.
- The separate `GlyphTrueRect` pass used on reveal and remote/non-modal paths did not
  consistently apply the original transient-family exclusions. It could reintroduce
  tooltip text excluded by the first walk. Modal steady rendering used a different path.

## Change

MR geometry now uses an explicit painted-geometry mode in the existing ink traversal.
It reads native TMP mesh vertices, cached legacy text vertices and original Image/RawImage
drawing dimensions without rebuilding native UI. Original
backdrop artwork, actual glyph overflow, transforms, masks, native visibility and scope
are retained; empty layout space and stencil-only mask faces are not painted content.
Transient-family exclusions remain in the same traversal. There is no second glyph pass
and no host-frame floor that could restore an oversized rectangle after measurement.

All local modal/non-modal, effect-snapshot and inert remote paths use this policy with
the existing small margin. The generic authored-layout measurement stays unchanged for
grab bars, capture, hit testing and native window placement. Missing initial meshes
defer only the backing and then join the already-running materialisation effect.
The build-522 fade timing, late visibility checks and smooth resize remain in place.

The shipped Unity API does not expose CanvasRenderer.GetMesh. Sliced/tiled images,
partial radial fills and unknown custom graphics therefore retain their own conservative
adjusted drawing rectangle; they never substitute the parent host. Text never falls back
to a large layout box when its native mesh is missing.

Geometry uses the existing sampling cadence and reusable buffers. Modal MR sampling is
skipped while MR is off. Native mesh data is read-only; no forced text/layout rebuild,
gameplay write, additional animation clock or window-continuation gate is introduced.
`MR PLATE EXTENT` now records fitted bounds for modal windows as well.

## Validation and hardware limits

Affected ink/geometry, MR integration and animation lifecycle harnesses plus the strict
Release build are the local checks for this change. Final counts are in STATE.md.
No bundle, config or wire grammar change; all VR peers use build 523.

Integrated checks passed: ink/painted geometry 368 assertions and 20 negative controls;
MR layout/accessor 258 assertions, 48 bindings and 16 negatives; actual animation
lifecycle 545 assertions, three bindings and three negatives. Strict Release has zero
warnings/errors. Eleven frame-order locks, 617 hardware markers, shell syntax, bilingual
docs and compatibility surfaces pass. No unrelated full suite was repeated.

In the next headset test, open/reopen merchant, temple and other map destinations,
hover their item/help controls and scroll their lists. Check that only a small background
margin remains, real artwork/text is fully backed, and appearance/dissolution still
matches the window. Include a second build-523 peer for remote confirmation. Automated
checks do not prove the final headset image.
