# Relocation presentation generations

`python3 scripts/check-town-service-mirror.py --suite relocation` runs the production
publisher's `Tick`, `Reset`, generation fields and mirror capture/codec/playback in
Unity. Native catalog lookup, module discovery and game-controller state are explicit
fixture boundaries; the fixture publication sink registers its real source with the
actual mirror. It does not replace the generation decision or interpolation code.

The reproduction completely discards every packet sampled at zero relocation opacity,
including the boundary manifest. The next nonzero-opacity capture must provide
independently decodable new-generation modules. The observer must create those modules
at the new owner pose immediately and must never interpolate from the previous location.
Native window/session identity and original session age stay unchanged. Ordinary fades
and small held-card movement retain their original generation and normal interpolation.
Native reopen and network reset cannot reuse an earlier generation. At uint exhaustion,
publication stops with one report instead of wrapping; native gameplay continues.

Four compiled mutations verify the generation boundary, first-visible baseline,
non-reused IDs and overflow guard. The workspace suite separately binds the revision
increment to the actual invisible `ApplyTarget` and checks that initial placement,
ordinary fade samples and stable rosters do not increment it. The interaction suite
retains the original native lifetime and inspection-versus-purchase checks.

This prevents a reconstructed flight through the map. It cannot recreate fade samples
that never reach the observer: under packet coalescing/loss, the observer may retain the
last old-location opacity until the new generation arrives, then appear directly at the
new location with its first received opacity. Delivery-perfect intermediate appearance
is not established by this bounded discontinuity correction.
