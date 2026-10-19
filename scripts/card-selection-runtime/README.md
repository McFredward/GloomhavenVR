# Editable scenario-card selection regression proof

Run from an initialized checkout:

```sh
python3 scripts/check-card-selection-runtime.py
```

This is a local-only native fixture. It requires the original read-only game
assemblies under `ressources/GH_Data/Managed`, Unity 2021.3.5f1, .NET, and the
existing HarmonyX/Publicizer NuGet dependencies. `--source-root` can read a
different initialized checkout without changing it. `--output-dir` selects an
owned evidence directory. `--production-only` omits all causal controls and is
explicitly partial validation.

The native section compiles the actual ordinary release branch, fan-key/order
methods, initiative reconciliation and per-drop token methods. The original
GH.Runtime `SelectCard`, `UnselectCard`, `OnClick`, `ToggleSelect`, selection
callbacks and initiative swap execute against the original ScenarioRuleLibrary
queue consumer and card moves. No ScenarioRuleLibrary method is patched.

Its 32 independent combinations cover four game modes, gap/no-gap release, and
named/null/empty/missing identities. They repeatedly place, withdraw and replace
cards, change the presented actor before queued callbacks, verify authoritative
native piles and widget selection, preserve separate fan orders, and retain
captured left-card initiative. Rapid release cases execute the real native FIFO
and production token/supersession methods. Native action/preview and toggle
guards execute too; phase transitions are supplied inputs, not a Ready-button
or complete scenario simulation.

Explicit native boundaries are the already-loaded scenario/save lookup, offline
transport, picture-only widget methods, telemetry and initiative-track visuals.
The physical wrapper supplies release targets and hand geometry. Real physical
and laser call sites are source-bound to the shared release path, but controller
tracking, complete rebuild execution, tutorial isolation profiles, connected
multiplayer and headset pictures are outside this proof.

The complementary portable projection section runs exact production
`SyncFromGameState`, `FillHandFan`, placement/removal, occupancy and token methods
between explicitly supplied native acknowledgements. Its Unity hierarchy,
`SetHome` and model acknowledgements are named substitutes. It proves stable
membership/home/occupied bits and bounded pending lifetime; it does not render
a Unity or headset picture. The native section independently proves the actual
acknowledgement sequence. Source bindings retain pump-before-rebuild order and
both placement/withdrawal call sites.

Controls restore the null-name abort, omit native withdrawal, remove GUID order
or identity separation, restore foreign/pending initiative writes, project an
old hand after focus changes, consume a newer drop with an old acknowledgement,
lose captured left intent, and ignore deliberate withdrawal. Four projection
controls separately restore intermediate eviction, duplicate fan membership,
stale-token completion and ignored explicit withdrawal. A compile/start failure
never counts as a passing control.

Exact counts, source/DLL hashes, per-variant build logs, results and cleanup
receipts are written below `.planning/debug/card-selection-runtime/`. Failed
runs remain evidence. Every stopped standalone run reaps its processes and uses
the shared `cleanup_completed_run` API to remove regenerable project caches.

At introduction, the frozen production proof passes **3,496 native assertions
and ten causal controls**, plus **471 projection assertions and four controls**.
The native pass and repaired projection pass are composed evidence: the first
combined command correctly failed on an outdated projection source-read count
after the captured-left fix added two reads. Only that projection binding was
repaired and rerun; the unchanged native proof was verified against the final
production and fixture hashes. This is not a second full native run.
