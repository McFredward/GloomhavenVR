# Steam Link path binding — Build691

## Established cause and why previous rounds passed

New Windows logs identify689/f71a97db4 and explicitly refuse
`openvr_api.dll:cache-folder`. The actual produced689 DLL contains the call:

```
call string [GH.Runtime]StringExtensions::Skip(string, int32)
```

The game declares a global `Skip(this string,int)` overload that starts at
`Math.Min(count,value.Length)-1`. It is more specific than the LINQ enumerable
extension. Thus `path.Skip(2)` leaves the drive colon, and our own subsequent
colon guard rejects EVERY ordinary drive path. This also explains why supplying
correct runtime paths or switching folder APIs did not solve the shared guard.
The recent environment/known-folder-input hypothesis was not the established
cause; do not carry it forward as a confirmed Mono shell-folder defect.

The earlier linked-source fixtures did not reference GH.Runtime. Their same C#
text therefore compiled a DIFFERENT call to Enumerable.Skip and passed. Even the
initial source-only UnityMono witness repeated this scope gap. Testing the actual
produced689 plugin with readonly real GH.Runtime under actual Unity2021 Mono
reproduces the failure. Its original5-control proof and IL are retained under
`.planning/debug/steamlink-690-cache-review/` and `steamlink-690-binding-review/`.
The full IL is compressed; the exact relevant method remains directly readable.
A scan finds only this one game StringExtensions.Skip call in the689 plugin.

## Correction and lasting checks

Call `System.Linq.Enumerable.Skip(path,2)` explicitly. Do not weaken path admission,
change folder inputs again, alter native pins or install a different Frame patch.
Add the game's conflicting signature/semantics as a test-only adversary. The
ordinary path/cache checks now compile with that extension available. A frozen
causal mutant restores `path.Skip(2)` and must fail at path admission, before the
unchanged owned-child deadline test. This is not merely a second copy of the
expected new guard. The fixture cannot ship with the production plugin.

The optional produced-artifact probe loads the built plugin and actual readonly
GH.Runtime under Unity's Mono, rather than compiling a copy of the production
method without the game's dependency scope. Same probe/inputs in independent
processes: actual691 passes12 controls; retained689 passes9 controls proving its
old refusal. Reject unsafe network/relative/traversal/ADS inputs in both cases.
The tested691 DLL and game-runtime identities are pinned in the receipts.

This correction does not alter the public SDK/helper bytes, SteamVR/Proton
initialization, resource pinning, finite attempts, logging policy, exact
Steam Frame/vrlink rule, MR renderer, multiplayer or receiver installation.
Windows/Proton native HMD metadata lookup still requires a live stream; a Linux
Mono artifact-level path result is not such a headset result.

## Publication and scope

691 is the bounded source handover over published689.690 was an internal,
unreleased checkpoint of the independent unfinished stereo catalogue; its eventual
handover advances to692+. Merge this repair into shared local dev preserving all
movie checkpoints, but do not publish their incomplete22-file deployment roster.
No Frame write/restart, installer change or extra archive delivery occurs.

Checks/results, initial failures and completed-cache cleanup remain under
`.planning/debug/steamlink-691-binding-fix/`. Unchanged689 availability controls,
native sanitizer/reproducible-PE and prior wire/raster evidence are inherited;
this is not a new complete local gate.

## Final scoped validation

- Focused connection suite:124 managed and98+105 native checks; net472 typecheck
 0warnings/0errors. A freshly compiled old-call mutant exits3 at the exact new
 binding regression before child/deadline tests. Tested source SHA256
 `ce3722507ba7d9ce15b4eef343a1e674bc26839d56ca9eebc3bf917688eabdfa` exactly
 matches the public691 integration; do not repeat that unchanged suite.
- Actual final691 plugin/real GH.Runtime/UnityMono:12 checks pass. Same probe on
 retained689:9 checks prove the old refusal. Input hashes are unchanged.
- Strict complete Release compilation:0warnings/0errors. Affected source
 instrumentation1/1 passes. The unchanged availability suite37/4 and other
 source/native sanitizer/reproducible-PE/wire/raster evidence are inherited.
- No actual native Windows/Proton HMD metadata query or headset outcome is claimed.
