# MB492: retain the actual spent card through burn start

## Evidence and scope

Both supplied hardware logs identify ModBuild 491 at line 17. The logs establish that both resident recess cards and the independent burn slab were used: main log 60318 records Grab and Go in the recess for 155 frames with no slab hold; remote log 30507 records Gnawing Horde for 22 recess frames followed by 1.27 seconds on the slab. Owner logs record their native burn holds ending at approximately two seconds. Those observations do not prove a shader's visible color or establish that an entire remote board disappeared. No new screenshot was present for this report.

The source proves two distinct reset seams:

- `CardEffects.ToggleEffect` calls `Initialize` and `RestoreCard` before starting the native burn. `RestoreCard` zeroes `_GreyOut`, `_Flow`, and `_Dissolve`; the burn timeline then ramps them from zero. A visibly spent card therefore briefly returns to its unspent material state.
- Changing a discarded card to Lost invalidates its old mutable appearance address. `RemoteCardArt` cleared the previously drawn native picture before the replacement sample arrived, and the independent remote burn slab also started its fallback ramp at zero.

The rest candidate is the original `hand.cardsUI` widget (`ShortRestedCardWidget` / native `GetCardUI`), not a separately invented dialog face. The integrator's companion change addresses that same model through its canonical discard/lost appearance address when the presented card has no pile-origin stamp. Sending native channels now preserves the source widget's actual spent appearance.

## Implementation

A prefix captures initialized original material channels immediately before the native reset, including a card first presented in VR after burn starts. During discard membership, continuity retains only values actually observed on that model. Recovery/model replacement clears continuity. Activated cards do not retain historical maximum ghost paint: only their actual immediate pre-burn output is captured, so a currently blue active card stays blue.

An enumerator wrapper restores raw native channels before each native burn step and applies the spent floor after it. It preserves native yields, completion, disposal and exceptions. This also handles native writes after `WaitForEndOfFrame`, before the next visible frame. `PaintProgress` reports raw native progress rather than the cosmetic floor; a gray floor of 1 cannot mask a canceled or bailed timeline. Native no-ramp settling clears the override. Fire, native tint, text and the native animation clock continue through the original timeline.

Owner VR late updates and appearance sampling reapply the floor after ordinary writers (integrator-owned call sites). Weak records belong to the original effects component/model. Detaching a VR wrapper retires idle history but preserves a still-running original burn until completion. Every native `ToggleEffect` call observes model replacement or Hand/Round recovery before filtering burn starts, including inactive toggles while the widget has no VR wrapper. Thus a recovered card cannot carry an old discard floor into a later hand sacrifice whose model already moved to Lost. Destroying the original component releases its weak entry.

Remote recesses retain their already drawn actual spent output across the same model's transition to Lost. A separate per-sender history retains owner samples bound to immutable model provenance, for the fallback slab's three material channels only. It refuses another actor/card, recovery, unresolved immutable provenance and samples without provenance. Old mutable pile seats are never re-resolved to guess a replacement model. The existing native owner frame takes precedence as soon as its new address arrives.

No face policy, clone readiness, original loader callback, burn release deadline, flight curve or destination changed. Local completed-burn slabs already capture the final original output and retain it during flight.

## Validation

- Release build: zero errors and zero warnings.
- Standalone harness compiles the actual production continuity/history/enumerator helpers and runs 26 runtime assertions, including first pre-reset observation, Lost membership before callback, native zero reset, recovery, active blue state, identity/actor/sender separation, exact yields, final raw jump to the cosmetic floor, cancellation and native exceptions.
- Negative control: a private copy of the production continuity helper with its `Math.Max` floor removed fails the runtime test at the first native zero-reset case (exit 134). The working source is unchanged by this experiment.
- The integrator registers these same vectors in the complete wire suite and registers both Harmony patch types.

The previous tests covered face privacy, appearance transport and native clone readiness; they did not execute a native reset between a spent observation and a burn step. This adds that lifecycle coverage. Correct headset color and remote parity remain hardware acceptance checks, especially burn confirmation/redraw, discarded long-rest sacrifices, and an already blue active card burned at turn end.
