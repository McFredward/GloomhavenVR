# Ordinary-RGBA Steam Link coverage source

The full-resolution source retains the ordinary RGB image across the complete
current eye texture. Genuine coverage remains in that same texture's alpha.
There is no RGB atlas/header, separate eye-resolution override, colour key,
second scene camera, CPU pixel readback, retained temporary texture or game-frame
counter sent to another process.

The production entry is `MixedReality.Tick` → `SteamLinkCoverageBridge.Tick` and
`SteamLinkSceneRuntime.TryEnter`. The existing same-attachment depth prefix and
transparent-material variants still construct coverage. A negotiated source
uses `SceneEyeOutput.TryRenderOrdinary` in the actual head `OnRenderImage`;
otherwise it preserves ordinary opaque output. The retired atlas is not an
output alternative or a missing-peer fallback.
The source mode is immutable for one prepared camera epoch. A mode change retires
that epoch at the cold boundary and primes another complete stereo cohort.
Capability loss revokes authority and retains the ordinary same-callback image.

The source does not acquire a uniform-width/foveation quality lease or write
the user's resolution, MSAA, FOV or native video dimensions. The former packed
source and its quality controls are retired; their history is recorded in the
[companion archive](https://github.com/McFredward/steam-link-native-passthrough/blob/main/docs/history/ATLAS-RECEIVER.md).

## Live source capability protocol

This Windows client runs directly in the game process, off the Unity thread.
It finds a running `vrcompositor.exe` or `vrserver.exe` at a canonical registered SteamVR runtime path,
then opens `\\.\pipe\SFNP.StreamBridge.V1.<ownerPID>`. Before writing, it verifies
`GetNamedPipeServerProcessId` against that process. The provider must create a
per-user pipe and verify `GetNamedPipeClientProcessId` against both the claimed
app PID and its actual current DirectMode swapset owners. No helper process
claims to be the game. AppData lookup uses the existing Mono-tested environment/
known-folder fallback; all registry/process/pipe work stays off render callbacks.

Both directions use a fixed 128-byte little-endian record:

| Offset | Bytes | Field |
| --- | --- | --- |
| 0 | 4 | ASCII `SBN1` |
| 4 | 2 | Kind:1 request,2 reply |
| 6 | 2 | Version:1 |
| 8 | 2 | Total bytes:128 |
| 10 | 2 | Reply flags; request zero |
| 12 | 4 | Source profile:1 |
| 16 | 4 | Actual game PID |
| 20 | 4 | Actual server PID; request zero |
| 24 | 16 | Fresh nonzero client CSPRNG nonce, echoed in every reply |
| 40 | 16 | Current provider/peer binding; request zero |
| 56 | 8 | Current encoder generation; request zero |
| 64 | 64 | Reserved, all zero |

Source profile 1 is Gamma SDR UNorm, MultiPass, contribution RGB plus coverage A.
Positive RGB at zero coverage is valid emission; never multiply RGB by A again.

Reply flag value 1 means `canAcceptOrdinaryRgba`: the paired compatible provider
and current native composition/encoder route can accept this app's ordinary
RGBA. Value 2 means `coverageOperational`, and requires value 1. Unknown flags,
wrong nonces/PIDs/profile/framing/reserved bytes are refused. Either positive
flag requires a nonzero binding and encoder generation. Flags 0 may carry a
binding/generation while preparing; they never admit the source. The first flag
breaks the preparation cycle without pretending that a mask was already used.

The live connection has no feature-expiry timer. EOF, invalid reply, MR/session
Off or Reset revokes cached permission. Epoch checks prevent late replies and
new observers cannot overlap an old one still cleaning up. Missing endpoints
have at most three bounded attempts; an existing healthy connection is not
restarted. This protocol transports no mask, texture pointer or frame ID.

## Identity responsibility and evidence limits

A successful negotiation alone does not authorize Frame passthrough. The native
provider must identify the actual DirectMode shared texture/bounds/projection,
compose coverage with the very same current RGB mapping, bind it to its exact
encoder input/AU, and the receiver must bind that AU to its current selected
native image. A Unity frame counter, approximate time, cached latest mask or
successful handshake is insufficient. Unity's null `OnRenderImage` destination
is its current XR target, not a shareable texture identifier.

Focused managed checks execute the real wire parser, observer epochs and source
callback bodies with explicit OS/GPU boundaries. The ordinary-RGBA Unity2021
Vulkan GPU fixture checks full-frame RGB/A, explicit Left/Right labels, 256/4040
extents and supported 1/2/4/8 MSAA. This establishes the Unity blit output; it does
not establish Windows/D3D11/OpenXR submit retention, encoder/receiver identity,
headset quality or installation readiness. No Frame/PC process was modified.
The initial live provider is Windows-specific; no Linux-native provider support
is inferred from the portable managed tests or a Windows game using Proton.
