# Frame654: cached XR input focus for wall Auto

The wall Auto integration uses the current OpenXR session's input focus rather
than Unity's desktop-window `Application.isFocused`. A standalone headset can
continue receiving XR input while its Wine desktop window lacks focus. The old
wall eligibility predicate therefore could reject otherwise usable samples.
The wall integration and hardware-log interpretation are reviewed separately;
this lane verifies the focus source and its lifecycle boundary.

## Shipped ABI, checked before delivery

The shipped Unity OpenXR1.10 managed assembly already provides the public
`UnityEngine.XR.OpenXR.NativeTypes.XrSessionState` enum:

| State | Integer |
| --- | ---: |
| Unknown | 0 |
| Idle | 1 |
| Ready | 2 |
| Synchronized | 3 |
| Visible | 4 |
| Focused | 5 |
| Stopping | 6 |
| LossPending | 7 |
| Exiting | 8 |

These values match the normative
[Khronos OpenXR registry](https://github.com/KhronosGroup/OpenXR-SDK-Source/blob/main/specification/registry/xr.xml).
They are also proven against the actual shipped native plugin, rather than
assuming Unity uses the raw OpenXR values:

- `session_GetSessionState`, export RVA`0x37930`, reads the prior and current
  session-state fields at singleton offsets`0x158` and`0x15c` and copies them
  directly into the two output pointers. There is no arithmetic or remapping.
- The state-event writer at RVA`0x3e124` through`0x3e13f` copies the raw event
  state's`[event+0x18]` value into the current-state field unchanged. The prior
  state becomes the other output field.
- The native state-name switch at RVA`0x40450` maps integer5 to the
  `XR_SESSION_STATE_FOCUSED` string and6 to`XR_SESSION_STATE_STOPPING`.
- The actual managed feature dispatcher calls `session_GetSessionState`, then
  passes both output integers directly to `OnSessionStateChange(oldState,
  newState)`.

The plugin image base is`0x180000000`. The checked files are:

| Input | SHA256 |
| --- | --- |
| `libs/RuntimeDeps/Unity.XR.OpenXR.dll` | `f149f0426de2f661b61b161b4c875860ebe3bcc13738926dbcabd6b745b0e423` |
| `libs/Natives/UnityOpenXR.dll` | `2275da2750ebc9c815386604f73f0450b03fed6f44dafdeb15e978633e4866f5` |

An initial worker checkpoint mistakenly labeled state6 as Focused. Its
1302-assertion fixture agreed with that incorrect assumption, so its passing
receipt is unqualified and retained for audit. The delivered production code
uses `(int)XrSessionState.Focused`, not a literal or shifted state mapping, and
all focus fixtures now supply the verified raw ABI. This checkpoint was corrected
before the integration was released.

## Cache and ownership

`VRSession.InputFocus` is nullable. A newly created instance/session starts
unknown; absence of an observation cannot become a permanent false veto.
Only an actual current, begun session can publish state observations. Focused
reports true; all other observed states report false. Matched session end,
exit, loss or destruction revokes focus. Mismatched teardown handles cannot
alter the current session. Restart resets the prior ended state to unknown;
a duplicate begin preserves the current observation.

The currently created feature owns the shared cache. A previous feature's
state or teardown callbacks cannot change a recreated feature's observation.
The state callback itself carries no session handle. The check therefore guards
against an ended/unbegun session and an obsolete feature; it cannot identify a
misordered event delivered to an already begun replacement session. No such
runtime event disorder has been established.

This adds no native calls, enumeration, desktop-focus polling, marker reads,
logging or per-frame lifecycle sampling. The existing Unity callback assigns
one nullable value; Auto reads that cached value. The existing blend controller,
capability probe, compositor mode selection and restoration behavior remain.

## Local validation

The entire `openxr-blend-probe` production-linked fixture passes1304 assertions:
all910 previous native blend/probe/controller assertions are retained; all392
focus/lifecycle assertions from the initial fixture are corrected to the real
ABI; two explicit Stopping6 rejection assertions are added for PC and Frame
profiles. It compiles the actual `VRSession`, blend feature, blend probe and
native-passthrough facade. Native functions and Unity dispatch remain explicit
fixture boundaries, not a running compositor.

Four isolated source mutants fail at their intended assertions:

| Mutation | Causal rejection |
| --- | --- |
| Grant focus only for literal6 | Actual Focused5 must grant focus |
| Treat Stopping6 as focused too | Stopping6 must revoke focus |
| Publish state before matched begin | Unbegun session cannot grant focus |
| Remove feature cache ownership | Obsolete feature cannot alter current focus |

Both profiles also verify that state changes and100 repeated cached reads add
zero blend getter/setter calls, capability/procedure queries, profile-marker
reads or log messages. Lifecycle tests cover all six matched ending paths,
instance numerical-handle reuse, replacement sessions, mismatched handles,
duplicate begin and stale features.

Final source16/16 and strict Debug/Release builds pass with zero warnings/errors.
These are focused worker checks, not a new complete local gate. Receipts,
ABI disassembly, manifests and causal-control logs are under the worker's
`.planning/debug/frame654-xr-focus/` and must be archived by the integrator before
removing the worktree. The earlier unqualified numeric-focus receipts remain
separate from the final evidence.

A headset run must still establish that Steam Frame reports the expected
Focused callback and that Auto subsequently triggers under sustained load.
These fixtures do not measure Frame FPS or establish the headset picture.
