# Build618: ordinary-play hitch reduction

Hardware baseline is the actual Build617 / e1da52eaa Frame scenario capture at
3408 pixels per eye. Its twelve fully loaded/worn windows average 51.37 ms;
loading and deliberate slider reconfiguration are excluded. This implementation
addresses source-proven work behind the three selected targets. It has no new
headset performance result yet and does not claim a smooth frame rate.

## Wall publications from native presentation effects

[Wall changes](FRAME-618-WALL-IMPLEMENTATION.md) identify original published
waypoint particles and original combat-effect prefab instances, including their
active/parked pool lifecycle. Signatures and every live collector use the same
ownership boundary. Reparented effects are restituted from sticky wall carry.
Actual water/wall-shader inputs and unknown content stay conservative, including
the 30-cycle safety publication. This targets the repeated 133–210 ms publications
caused by effects which do not belong in a wall table; it does not remove every
scene publication or the remaining approximately 162 ms ceiling publication.

Bounded Debug camera-delivery traces reserve restored endpoints and actual route
coverage. They remain MPB/material delivery evidence, not native HIGH shader
pixel evidence. Hardware wall popping is still an open verification item.

## Remaining card and stat resources

[Card changes](FRAME-618-CARD-IMPLEMENTATION.md) close the actual private
Consume/Infuse widget arrays and original area-effect atlas missed by the previous
collector. Exact native prefab references are read, not instantiated. Temporary
atlas sprite metadata is disposed after warming the existing heavy texture/region
caches. Original/local/remote face assignments and effects are unchanged.

[Figure/stat changes](FRAME-618-FIGURE-IMPLEMENTATION.md) resolve the original
portraits of existing actors, including native/custom player, monster, object and
summon paths. Late native UI resources retain a bounded deferred discovery job
instead of silently ending preparation. The existing loading spinner covers the
same shared pin/mip jobs; original Show/selection/loading requests are untouched.
Prepared ghost identity checks remain strict. New Debug miss categories tell the
next capture why a valid native change required immediate live construction.

These changes do not promise every `Cards.Driver` or stat conversion becomes
cheap. Existing fan construction, late summons and necessary native widgets keep
their live paths; new subscopes separate them from image readbacks.

## Native callback work

[Native changes](FRAME-618-NATIVE-IMPLEMENTATION.md) remove the two private flat
card root-geometry writers only for verified actual adopted VR ownership. Native
selection, ViewSettings, highlights, validity, callbacks, exception paths and
returned/dialog/remote-clone cards retain original behavior. The shipped native
method bodies are covered in a real Unity fixture.

The fixed Debug SPIKE ledger records original ProcessMessage enums and selected
hand/card/bonus/item substeps after the initial sampling window. It preserves
message dispatch, budgets, queue order and exceptions. Off-main queue publication
is excluded from main-thread frame costs. Inclusive times must not be summed.
This exposes the remainder of the 207.26 ms Choreographer callback rather than
claiming the entire callback or unassigned 365 ms frame has been eliminated.

## Compatibility and evidence

All work reduction applies to every VR platform. No Frame defaults, resolution,
render detail, network record or gameplay rule changed. 2D map and original
non-NPC windows retain their paths; local/remote privacy and 1:1 geometry/effects
contracts are unchanged. Diagnostics are bounded at Debug; normal logs retain
only bounded preparation failures and the existing lifecycle context.

Focused fixtures prove causal ownership, original field-shape resource discovery,
GPU card-cache lifecycle, current native sleeping/flying pose preservation,
adopted native-body geometry suppression and unmodified message failure/dispatch.
The integrated gate and compiled comparison are recorded in `.planning/STATE.md`
and `.planning/debug/frame618-implementation/`; individual successful worker runs
alone are partial evidence.

The integrated local attempt recorded 121/121 suites in 829 seconds: 120 passed
directly. The remaining MR suite's original production assertions passed but its
text binding expected the previous wall-owner spelling; its bounded corrected
79-assertion/six-control resume passed without a runtime source change. Original
failures and early runner-inventory corrections are retained. Passing suites were
not rerun. Fourteen source gates, golden vectors, strict zero-warning Release,
five i18n pairs, bundle/figure-bank and surface checks also passed. Compiled review
against Build617 found eleven intended changed types, four additions and eight
build/branch stamp differences only. The continuation of the guard's unchanged
post-wire phases and raw ledger are retained with the evidence.

The next Frame run should revisit ordinary movement/attacks, first fan display,
first figure/stat pickup and later summons after loading has finished. Existing
Debug configuration is sufficient. Compare wall publication reasons/costs,
Consume/Infuse/area/portrait new-vs-cache mip jobs, prepared ghost miss categories
and previous-frame message/substep costs. Loading or slider stalls remain accepted
costs. Head motion prevents a controlled FPS benchmark, but per-operation evidence
still distinguishes avoided work from remaining stalls.
