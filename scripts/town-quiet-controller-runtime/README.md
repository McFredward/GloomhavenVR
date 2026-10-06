# Quiet native controller fixture

`check-town-quiet-controller.py` binds the complete current
`TownServiceQuietController.cs` and `TownServiceWindowMask.cs` without replacing
their preparation, reflection, masking, proxy or release methods. Real Unity
2021.3.5 runs the RectTransforms, CanvasGroups and object hierarchy. Source hashes
include the callback registration owner `WorldUIModule.cs`.

The fixture verifies independent hidden-source activation and exact restoration,
one original inventory/pool initialization per owner context, native callback and
selection routing, current-party changes, repeated visit teardown, successful
owned proxy audio and refresh, invalid/unowned/visible proxy guards, and bounded
cosmetic failure handling. A separate source gate requires all five quiet Harmony
callbacks to be installed once. Nine causal controls deliberately remove these
guarantees and must fail at their named assertions.

`Boundaries.cs` declares native models, payments, save callbacks, source widgets,
and presentation lookup. Their implementations are explicit counters, not copies
of the game's transaction rules. This proves the production controller's lifecycle
and callback routing across those boundaries. It does not establish headset
appearance, network delivery, native payment correctness, or the separate
`TownServicePresentation` toggle/fallback implementation. The existing merchant
and enhancement handoff fixtures retain their full transaction/outcome matrices.
