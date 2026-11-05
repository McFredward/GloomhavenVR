# Build760 input review

Worker base: `dev6a390f198`. This bounded input lane is part of the integrator's
ClassC union. The parent owns the final build/source/full-local gate and delivery.
Hardware inputs are pinned under main `.planning/debug/mp760/hardware759`;
both players ran759 and **remote is the host**.

## Starting-card overview: source-proven wheel interception

The user clarified that “zoom” meant **scrolling**. There is no new zoom feature
and no locomotion-priority override. The original `UICharacterCreatorCardsViewer`
puts full ability cards in a vertical `ExtendedScrollRect`. Its original `Show`
method spawns those cards into that list and enables the full-card presentation.

The original four full-card prefabs have twelve ordinary `EventTrigger` adapters
on the card or its ability buttons. They contain only PointerEnter, PointerExit,
Select and Deselect entries, with no Scroll entry. Nevertheless, `EventTrigger`
implements `IScrollHandler`. The previous `UguiPointer.Scroll` called
`ExecuteEvents.ExecuteHierarchy` from the hovered card graphic. Unity stopped at
the pointer-only adapter, which did nothing, so the actual ancestor list never
received the wheel. A list gap had no adapter and reached the original list.

The correction walks the original ancestor chain for the nearest active wheel
owner, skipping **only exact ordinary `EventTrigger` instances without a Scroll
entry**. Explicit Scroll entries, subclasses with custom behavior, other
`IScrollHandler` components and nested lists retain their original priority.
The original `ExecuteEvents` callback, `ScrollRect`, wheel response and per-hand
arbitration remain responsible for movement. Hover, click, drag and game choices
are unchanged. A component buffer is reused; no scene search is introduced.

`scripts/ugui-scroll760-runtime/run.py` compiles the actual production
`RayUguiDriver.TickStickScroll`, `UguiPointer.Scroll`/`ScrollTarget`/`GetData` and
the complete `UiScrollFocus`. Real Unity `GraphicRaycaster` resolves a viewport
gap and a full-card descendant. Real `EventTrigger`, `ScrollRect`, hover callback
and nested list exercise the dispatch failure and correction. Tracked stick
values and the settled hover assignment are explicit fixture inputs.

Evidence in worker `.planning/debug/ugui-scroll760-runtime`:

| Run | Result |
|---|---|
| `run-f0m65jn4` | Production16 assertions passed; four causal controls passed: old empty-adapter interception, explicit Scroll entry, derived adapter, disabled handler. The deadzone control escaped because the fixture started at the clamped top and its erroneous reverse scroll was clamped away. |
| `run-o_ha_eqx` | Production16 assertions and the repaired deadzone control passed,10.86s. The deadzone test now starts at the midpoint. |

The four retained controls terminate **before** the changed deadzone-test tail;
their executed source/input prefixes are unchanged. This is aggregate focused
coverage of16 production assertions and five compiling causal controls, not a
new complete gate. Native metadata audit binds both original campaign scenes,
four original full-card prefabs and all twelve pointer-only adapters. All run
processes were reaped and `cleanup_completed_run` removed regenerable caches.

Limits: this is actual Unity2021.3.5 UI behavior, not live OpenXR, Windows input
or a new hardware acceptance. It does not establish the cause of any earlier
turn deadlock. Original state, network authority and the protocol are untouched.
