# NPC666 native summon selection after late layout

## Evidence and cause

Both frozen hardware logs in main `.planning/debug/npc666/inputs` report
ModBuild665 at line17. The user reports the corrected small summon positions
but missing, unclickable upgrade fields. There is no new screenshot. The earlier
665 offset picture describes the previous placement defect, not current slot
availability. Host/remote native-service extracts show complete picture admission;
they contain no per-slot rectangle measurement and do not prove this cause on a
headset. Loading, card/front coupling, return flights and ring behavior are outside
this change.

The original `SummonContainer` prefab starts its stat RectTransforms at zero size.
`CreateLayout.CreateSummon` builds their native stat text and actual enhancement
entries. `UINewEnhancementWindow.HighlightButtons` creates/reparents the original
highlight onto each available entry's `ParentContainer`. Native `Highlight` copies
the target pivot, rectangle size, position and frame shader `_RectFormat` once.
The nested Unity layout groups assign the stat dimensions later. Build665's mask
corrects the highlight rotation/scale but never refreshes this earlier size copy.

The new causal fixture executes native highlight creation before the first layout.
With the exact old665 mask, actual card248's Health target becomes89x33.5 while
its native area remains0x0 and its frame shader remains0/0. The compiled old-source
control fails `native selectable summon rect follows its post-highlight layout target`.
Current source preserves the correct placement and follows the89x33.5 target.

## Previous proof limitations

The665 geometry proof deliberately instantiated test markers with a null Ability,
selected state and no choice callback. It rebuilt layout before every Highlight.
That proved the title-layout offset and renderer suppression, but excluded the
late-zero-size lifecycle and would fail the real VR gate requiring an Ability.
It did not establish native slot availability or click continuation.

An additional early geometry-only diagnostic is retained at worker
`.planning/debug/npc666/prelayout-probe/evidence/run-i4gp1xyl/proof/run-cik2bfkl`.
Its old665 source failed the corner invariant after targets began0x0. It is
explicitly not an availability/click proof.

During new fixture construction, reduced model-context, missing exported native
CanvasGroup and headless camera depth errors were preserved rather than treated
as product causes. The raycast diagnostic found all Graphics at depth-1 before
the disabled editor camera had submitted any UI batch. The fixture now renders
that camera before querying actual GraphicRaycaster depth. Production input and
deadlines were unchanged. Native CanvasRenderer transparent-culling flags are
also imported rather than assumed.

## Narrow repair

The existing mask follows only a highlight whose actual parent is one of the four
serialized `SummonContainer` stat references. It updates pivot, size and position
from that target and uses the original native frame/material/shader-property field
to refresh the shader size. Cached reflection is used only when rectangle size
changes; material writes require the original material to expose that property.
It does not rerun Highlight, alter native interactability or callbacks, create
slots, enable unavailable entries, or change full-width action rows. Original
Awake retains its per-highlight material clone. Existing title masking/restoration
and broad-area geometry remain intact.

## Maintained proof and limits

Run `python3 scripts/npc666-overlay-runtime/run.py --controls`. The root registers
this as `npc666-native-summon-selection`; this worker does not edit the registry.

The fixture imports the original SummonContainer, PreviewText, EnhancementContainer
and Enhancement hierarchy/layout components from `misc_gui_assets_all.bundle`.
It separately imports the native map selectable's fixed anchors/pivot, original
frame/fill, CanvasGroups, CanvasRenderer flag, colors and hover opacities from level4.
Native Awake executes on activation and clones the original frame material.

The original readonly ScenarioRuleLibrary, SharedLibrary and ThirdParty DLLs parse
the original ruleset and construct both selected native character/card banks.
The bound game methods are CreateSummon, CreateSummonStatText, CreateEnhancement,
EnhancementButton.Init/UpdateEnhancement, EnhancementButtonBase.Init/getters,
CardEnhancementElements, GetEnhancementLines/CanBeEnhanced, SetEnhanceFilters,
HighlightButtons, EnhancementLineFilter and the original highlight/Awake/OnClick.
An empty serialized enhancement list is an explicit initialization port; native
construction subsequently supplies all summon entries. Controller declarations
are retained; extracted native methods only expand indentation and disable nullable
analysis for original unannotated source.

Actual card248/SlimeSpirit has Health/Attack/Move slots and no Range slot.
Actual card80/BurningAvatar has Health/Move/Range slots and no Attack slot.
The four stat lines are therefore exercised without fabricating an unavailable
field. Native SetEnhanceFilters initially selects Health: it remains intentionally
non-interactable. Other free fields retain the native callbacks/interactability.
The actual bound filter records the selected ability/line through native OnClick;
the already selected field cannot fire a duplicate choice.

At both physical scales0.6/1.7 the proof covers late layout, longer German title
refresh, original renderer-alpha overwrite, unchanged wide action fields, actual
GraphicRaycaster/production VR native-area validation, native filter selection and
upgrade-option continuation, actual Sync publication/capture/codec/remote replay,
exact remote corners/dimensions/material values and inert observer controllers.
Isolated local/remote frame GPU readbacks are retained. The GPU shader is an explicit
`_RectFormat` port, not the shipped frame shader; its pixel test is not a headset
border/appearance proof. Font/settings, special-text layout, asset loader, shop/menu
shell, ExtendedButton audio/game UI-lock shell and physical full-card adoption are
also explicit boundaries. No purchase, gold deduction or headset tracking is
asserted. Native filter and OnClick bodies are real; the receiving shop-navigation
shell observes their continuation without executing payment.

Final worker evidence:

- Current native selection:688 assertions PASS,3 compiled controls reach their
  unchanged named engine assertions; worker
  `.planning/debug/npc666-overlay/run-dyg2lra3/proof/run-4xd922g0`.
- Controls: exact old665 source, stale original shader format, lost native selection
  callback. Compile/setup failures from earlier approaches are retained separately.
- Prior665 native geometry/masking:818 assertions PASS +5 named controls, worker
  `.planning/debug/npc665-overlay/run-k3uzlq1w/proof/run-mu4r50p9`.
- Source group16/16 PASS; `.planning/debug/npc666/source/results.json`.
- Strict Debug build0 errors/0 warnings; `.planning/debug/npc666/strict-debug.log`.

These are focused worker checks, not a new full local gate. Headset confirmation
of the current missing fields remains outstanding.

## Legacy handoff fixture field-port repair

The composed212 run failed to compile `town-enhancement-handoff` because its
`SummonContainer` boundary exposed only `SummonNameText`. No production assertion
executed in that failed variant. Readonly `GH.Runtime.dll` decompilation confirms
that `SummonLT`, `SummonLB`, `SummonMT` and `SummonMB` are original public
`GameObject` fields. The boundary now declares exactly those four references,
without supplying slots, availability, selection behavior or callbacks. Production
sources and existing runtime assertions/mutations are unchanged.

Focused validation binds the frozen NPC666 integration checkout:
`python3 scripts/check-town-enhancement-handoff.py --source-root
/home/claw/gloomhaven_vr_npc666_integration --only-mutation native-print-real-geometry
--only-mutation native-print-pivot-alignment --only-mutation native-print-restore
--only-mutation native-disabled --output-dir .planning/debug/npc666/handoff-fixture`.
Production passes1515 runtime assertions; all four compiled negative variants
reach their original named engine failures. Evidence is worker
`.planning/debug/npc666/handoff-fixture/run-8721rl7i`, with source hashes,
compiled sources/DLLs, mutation ledger and engine receipt. This focused result
repairs the fixture binding; it is not a fresh complete212-suite gate.
