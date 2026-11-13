# Quest741 menu-depth AOT preflight

This is an isolated preparatory check, before the explicit dev740 integration
ready signal. Its input is dev739 `063918b6b73288c091740ced8f888421c6f428b9`
plus the integrator's borrowed, published Quest735 source/tool/test delta from
`7975c7e8c`. The upstream Build739 protocol stamp is retained. It does not certify
the later dev740 DefaultSky integration or the final Quest741 feature head.

## Exact compatibility change

The upstream preview-shadow and lifetime changes add seven members before
`MenuDepthCharacterSource.CreateOriginalReader`. The optimized compiler moves
its generated closure from `<>c__DisplayClass76_0` to `<>c__DisplayClass83_0`.
Comparison with the retained published Quest735 Release DLL proves that the
entire reader factory and closure are identical after that exact name change.
This includes every instruction, local, branch target and exception boundary.

| Contract | Previous SHA256 | Current SHA256 |
| --- | --- | --- |
| `CreateOriginalReader` optimized method | `556c6d86adab4230e0b88ef4751de6b5ca74d92d100a09911bf01ce5c2451bd3` | `f42b3dc473e39002f6493fd43ea50fff2d77f215f2518050ec96c6dcc638aa2e` |
| Complete original-reader closure type, 76 to 83 | `7b88fdf1685006a3f64859d340f686229810c707442f18ba9949f8250470ab3f` | `374a3715fa0b0ff19d04b7dbac961d84a92ac910db333246b795d16a5a839263` |

Only this exact pair is enrolled. Unknown names, IL and metadata remain rejected.
The Android branch still returns the cached native `FieldInfo.GetValue(manager)`
delegate. Native `character3D` remains the non-static reference field of type
`Character3DDisplayManager/Character3D`; native `isHidden` remains a non-static
Boolean. The original lookup flags, null/type checks, delegate target, hidden
reader catch/fallback and desktop emitter are unchanged in the input. The
converted Android methods retain the original validated closures and omit only
the proven unreachable desktop emitter instructions. Hidden-reader, lifecycle,
descriptor and loose-story-directory pins are unchanged.

## Executed proof and limits

This bounded qualification repair belongs to the integrator's Class C rebase.
The worker ran the affected actual-mod consumers; the integrator owns the final
union's source checks and complete local gate. The unchanged portable Weaver
fixtures inherit the integrator's existing complete Weaver-suite proof.

- Actual optimized Release mod and Release Weaver fixtures: zero warnings/errors.
- `--menu-depth-only`: 33 assertions, including all 25 exact native lifecycle
  targets, original closure fingerprints, reflection roots, protected original
  game types and an actual `Weaver.Write` output. All 13 causal controls reject
  changed contracts. Three new controls alter the original-reader Android guard,
  its captured manager identity and its cached field's instance authority.
- `--current-mod-only`: 843 assertions, including actual-mod conversion, static
  registration/write controls and protected output verification.
- Separate actual `audit`: 227 patch classes, 589 hooks, zero unresolved gates.
  Separate actual `weave`: 370 targets, `complete=true`.
- Release build/initial rejected audit: 23.5 seconds. Final focused build,
  controls, audit and weave: 112.9 seconds. Input immutability checks pass.

The Release DLL is
`3d99218decc83aa7e91bdad9c86c311382caeded6b19b1f095971836620f108d`.
It embeds the existing Android bank/provenance pair from cache key
`ba75e6e9a5b5ea37c4a3f4b0b48c1f324298875e4aaf67dcdfa8aeccd2116318`
through `GloomhavenVRMenuDepthBundle`, `GloomhavenVRMenuDepthProvenance` and
`GloomhavenVRMenuDepthSkipStoryCopy=true`. The bank and provenance hashes are
`2cff6bea9f4255dfb3e0136cda8e3c30dd951d6c788278e7ff4a01a1da62dcf1` and
`3e0df51e18d2741671b625f784280e0eb6b429e244fa0920299fecf345c627c0`.

Evidence is retained under
`/home/claw/quest3-local/build/evidence/quest-dev740-rebase-20261028/aot`:
`menu-il-before-after.json`, `exact-il-comparison.json`,
`source-and-resource-bindings.json`, initial/final audit reports,
`weave-report.json`, run logs, command timings and cleanup receipts. The first
audit fails with the old pins, demonstrating the original closed gate. Both run
processes are reaped and use `cleanup_completed_run`. Ordinary copied movie
inputs exceeded report retention limits; their generated copies were verified
against unchanged source payloads and removed before the final cleanup pass.
Source, native references, resources, logs and current proof DLLs are retained.

No Unity editor, complete local gate, APK or installation archive was built.
These Cecil metadata/conversion checks do not establish IL2CPP execution,
character-window final pixels or headset appearance. The final dev740-based
candidate still needs its own exact binding and integration validation.
