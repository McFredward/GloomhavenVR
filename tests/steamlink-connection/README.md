# Steam Frame / Steam Link connection reader

This is one focused Linux CI suite for the cached PC availability gate. It does
not connect to a headset, start SteamVR, or execute the Windows helper. A portable
fake public OpenVR table exercises the helper's actual metadata reader; managed
controls exercise classification, bounded protocol/PE/path checks, logging,
finite retries, stale-query cancellation, and real owned-child deadline/reaping.

```sh
python3 -B tests/steamlink-connection/run.py --output .planning/debug/connection-check
```

Optional local checks: `--sanitize` instruments the native metadata reader;
`--rebuild-native` requires `x86_64-w64-mingw32-gcc` and compares the executable
byte for byte; `--typecheck` checks net472 against the game's Unity CoreModule and
the existing UnityEngine.Modules JSONSerializeModule. `DOTNET`, `CC`, and
`UNITY_MODULES` can select installed tools/references. Every output directory must
be fresh. Logs and results remain; the completion retention API and exact owned
cache cleanup run after all children have been reaped. No global cache is deleted.

The shipped helper is built from `tools/steamlink-connection-reader.c` with
`scripts/build-steamlink-connection-reader.sh`. It contains owned code and MinGW
runtime only, with no SteamVR library. The managed resource SHA/length and PE64
check must change together if rebuilt with a different compiler. The Linux suite
checks the committed PE and its exact managed pin. The repeatable build uses
MinGW GCC 13-win32, `-Os`, no PE timestamp, NX and dynamic-base flags; the output is
42,496 bytes, SHA256
`1c3a2c92e188d095cb80bc56b6452237b977571beac97da9159281491990284d`.

The root caller must independently prove a running Windows SteamVR OpenXR session.
`Tick(eligible, now)` never blocks Unity: at most one owned five-second child runs,
with at most three startup attempts and at least two seconds between starts.
A stable result is cached until explicit session/HMD invalidation. `Available`
is a cached read; `Reset()` and `Invalidate()` cancel only the owned child.
Normal logs announce a positive Frame/Link transition and warn once per session
for an unreadable runtime registry/API or unavailable embedded reader. Ordinary
unsupported headsets/transports and detailed properties stay at Debug.

The helper requires existing vrserver and vrcompositor processes and creates an
isolated public `VRApplication_Background` client. It never initializes OpenVR in
Unity, starts a Scene app, writes SteamVR settings, or kills another application's
process. It queries connected HMD0/class and stable public model/tracking/registered
strings using the exact public `FnTable:IVRSystem_022` ABI. Required model is
`Steam Frame` and driver is `vrlink` (tracking or registered identity). Generic
SteamVR names and Virtual Desktop do not qualify. These cooperative driver
properties are not security attestation or proof of the receiver installation.
`Snapshot.ReceiverVerified` is always false; absence of the receiver cannot be
detected by this gate.

Public ABI sources: [Valve OpenVR v1.26.7 C API](https://github.com/ValveSoftware/openvr/blob/v1.26.7/headers/openvr_capi.h),
[versioned C# function table](https://github.com/ValveSoftware/openvr/blob/v1.26.7/headers/openvr_api.cs),
and [public initialization/interface implementation](https://github.com/ValveSoftware/openvr/blob/master/src/openvr_api_public.cpp).
The private evidence retains exact source hashes. No Windows runtime or live HMD
result is claimed by these tests; that remains a hardware validation boundary.
