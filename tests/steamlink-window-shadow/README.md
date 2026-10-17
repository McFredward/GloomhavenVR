# Stock UI image effects in MR backing bounds

The game-exact Unity2021.3.5 API has no public CanvasRenderer.GetMesh. The
production measurement therefore keeps its existing Image/RawImage drawing
rectangle and includes only explicitly known UnityEngine.UI.Shadow/Outline
translations. It never calls native Graphic.OnPopulateMesh or ModifyMesh in
gameplay. TMP, legacy text, custom Graphics and custom effect subclasses retain
their previous paths.

Stock effects duplicate input vertices with byte-alpha and constant local
translations. Effects are applied in component order, including zero-alpha
intermediate pieces which a later useGraphicAlpha=false effect can reveal.
Merging equal-alpha bounds limits the working set to256bins instead of
exponentially many quads. Final alpha-zero pieces do not enlarge the backing.
Offsets are transformed as vectors into host space before unioning, preserving
nonuniform scale and rotated footprints. No active stock effect keeps exactly
the old drawing rectangle. Existing visibility, Mask.showMaskGraphic, sprite
aspect/padding, capture clipping and outer mask gates remain in their old order.

```sh
python3 tests/steamlink-window-shadow/run.py --output /private/evidence
python3 tests/steamlink-window-shadow/run.py --aspect-only --output /private/aspect
python3 tests/steamlink-window-shadow/run.py --old-control --output /private/control
```

The actual Unity fixture builds test-owned Image/RawImage meshes with the real
OnPopulateMesh and stock ModifyMesh implementations, independently compares the
visible vertex footprint, and renders real UI pixels. The production helper
itself is compiled unchanged by Roslyn against actual Unity/game UI references;
only a private friend attribute and unused TMP/constant ports are supplied.
The runtime does not run those test-owned population calls. Initial positive
raster:15,297checks/16geometry cases,15,200visible pixels,4,032pixels missed
by the previous drawing rectangle plus8pxmargin. Enable/disable, changed offsets,
Outline, mixed effects, transforms, zero effect/native alpha, native group hide
and outer clipping are covered. The narrow aspect/byte-zero delta prevents
expanding the layout rectangle or invisible modifier geometry instead of actual
image artwork. The old-control mode must fail the exact active-shadow xMax
comparison; unrelated compiler or native failures do not count as a rejection.

These measurements run on the owner's existing geometry-sampling cadence. They
do not add a watcher, a camera, a texture, a render pass or an immediate-frame
update promise. Linux/Vulkan raster evidence establishes this source defect,
not that it caused the reported older Story screenshot or any new693 headset
picture. The existing PanelInk suite separately covers the enclosing classification
and clipping contracts.

The runner pins/copies sources, reaps all owned compiler/Unity children and calls
cleanup_completed_run before removing only its private generated Unity project.
Failures, logs, hashes, images and current compiled helper evidence remain.
