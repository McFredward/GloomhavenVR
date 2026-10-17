# Steam Frame / Steam Link connection reader

This is one focused Linux CI suite for the cached PC availability gate. It does
not connect to a headset, start SteamVR, or execute the Windows helper. A portable
fake public OpenVR exports/table exercise the helper's actual Background
initialization, lookup, metadata reader and shutdown sequence; managed
controls exercise classification, bounded protocol/PE/path checks, both real
embedded payloads and their pins, fresh/repeated extraction and corrupt-cache
refusal, Windows folder fallback, OpenVR/Proton environment inheritance, logging,
finite retries, stale-query cancellation, and real owned-child deadline/reaping.

```sh
python3 -B tests/steamlink-connection/run.py --output .planning/debug/connection-check
```

Optional local checks: `--sanitize` instruments the native metadata/lifetime path;
`--rebuild-native` requires `x86_64-w64-mingw32-gcc` and compares the executable
byte for byte; `--typecheck` checks net472 against the game's Unity CoreModule and
the existing UnityEngine.Modules JSONSerializeModule. `DOTNET`, `CC`, and
`UNITY_MODULES` can select installed tools/references. Each output directory
receives a fresh child run, so repeated suite runs preserve earlier evidence.
`--native-only` runs only the native controls and optional executable rebuild;
it is a focused subset, not a new managed availability/typecheck result.
Logs and results remain; the completion retention API and exact owned
cache cleanup run after all children have been reaped. No global cache is deleted.

The shipped helper is built from `tools/steamlink-connection-reader.c` with
`scripts/build-steamlink-connection-reader.sh`. It contains owned code and MinGW
runtime only, with no SteamVR library. The managed resource SHA/length and PE64
check must change together if rebuilt with a different compiler. The Linux suite
checks the committed PE and its exact managed pin. The repeatable build uses
MinGW GCC 13-win32, `-Os`, no PE timestamp, NX and dynamic-base flags; the output is
42,496 bytes, SHA256
`cec82e68968441902e717c6bc595ff0ae0b0b03ffe72bc5eb26cbfe19c40a30f`.
Exact helper source/compiler pins are in
`Assets/SteamLink/connection-reader-provenance.json`.

The root caller must independently prove a running SteamVR OpenXR session in the
Windows player, including when that Windows player runs under Proton.
`Tick(eligible, now)` never blocks Unity: at most one owned five-second child runs,
with at most three startup attempts and at least two seconds between starts.
A stable result is cached until explicit session/HMD invalidation. `Available`
is a cached read; `Reset()` and `Invalidate()` cancel only the owned child.
Normal logs announce a positive Frame/Link transition and warn once per session
for an unreadable runtime registry/API or unavailable embedded reader. Ordinary
unsupported headsets/transports and detailed properties stay at Debug.

The public Valve OpenVR1.26.7 SDK loader is also embedded:826,688 bytes, SHA256
`5e591aa3f4a991edcc8910921833edafa67a1b117849bbb4e235df9833bed7a3`.
Its BSD3 license ships in `Licenses/`. Production extracts both checked payloads
under their own source-hash directories. Only the child loads the SDK DLL; no
installed SteamVR SDK copy, hand-written registry parser or second OpenVR client
in Unity is required. Valve's SDK selects the runtime/config, including its public
environment overrides and ordered path history. No runtime/provider binary is
redistributed. The owned helper has a separate committed EXE pin.

The helper creates only an isolated public `VRApplication_Background` client
(type3, no startup payload). Valve's public contract forbids Background from
starting SteamVR or keeping it alive after other clients quit. Native error121
(`Init_NoServerForBackgroundApp`) becomes `not_running`, without an interface
lookup, retry, Scene fallback or Shutdown of a failed initialization. A successful
initialization gets exactly one matching Shutdown, including metadata/interface
failures. The portable fixture executes this same production function with fake
exports; it verifies the argument/order/count and refusal boundaries. It does not
prove an actual native runtime obeys its contract.

There is no Windows process-name census. Proton routes the PE SDK through its
`C:\vrclient` bridge to native Linux SteamVR; native vrserver/vrcompositor are
not required to appear as Windows `.exe` processes. The helper remains a PE64
binary and preserves the public SDK/Proton runtime selection. It never initializes
OpenVR in Unity, starts a Scene app, writes SteamVR settings, or kills another
application's process. It queries connected HMD0/class and stable public model/tracking/registered
strings using the exact public `FnTable:IVRSystem_022` ABI. Required model is
`Steam Frame` and driver is `vrlink` (tracking or registered identity). Generic
SteamVR names and Virtual Desktop do not qualify. These cooperative driver
properties are not security attestation or proof of the receiver installation.
`Snapshot.ReceiverVerified` is always false; absence of the receiver cannot be
detected by this gate.

Public ABI sources: [Valve OpenVR v1.26.7 C API](https://github.com/ValveSoftware/openvr/blob/v1.26.7/headers/openvr_capi.h),
[versioned C# function table](https://github.com/ValveSoftware/openvr/blob/v1.26.7/headers/openvr_api.cs),
the [Background and error121 declarations](https://github.com/ValveSoftware/openvr/blob/751538d6cbde9d060e5906c1d45c8cedeaf0ee18/headers/openvr.h),
the [public initialization/interface implementation](https://github.com/ValveSoftware/openvr/blob/751538d6cbde9d060e5906c1d45c8cedeaf0ee18/src/openvr_api_public.cpp),
and [Proton's OpenVR path conversion](https://github.com/ValveSoftware/Proton/blob/proton_11.0/proton).
The private evidence retains exact source hashes. No Windows runtime or live HMD
or Proton result is claimed by these tests; that remains a hardware validation boundary.
