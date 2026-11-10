# Build762 capability receipt ingress repair

This is a bounded Class B repair discovered during the final Class C review.
The parent keeps the7037750b5 complete integration gate frozen. This worker starts
from dev beaeb88a2 and changes only capability receipt ingress in the two owned
production files, the existing cheats CLR container and this review note. No
Unity, device access, wire/schema change or shared-cache mutation is involved.

## Cause and behavior

The transport recognizes an exact standalone version announcement, updates
VersionGuard and returns before the driver. The new native-identity capability
receipt was only fed by the driver's ordinary extras branch, so those real
announcements never established cheat admission. Meanwhile that ordinary branch
converted an omitted version into an explicit build0, erasing an earlier receipt.
VersionGuard already preserves its previous Build when the sparse field is absent;
the receipt ingress now follows the same distinction.

The transport records its actual explicit version-only advertisement before its
existing early return. The driver records a receipt only when HasModVersion is
present. An explicit incompatible version still invalidates the receipt. No
cached numeric version is promoted to fresh capability evidence: native entity
replacement and session-epoch retirement still require a new real advertisement.

## Actual source-derived proof

The existing multiplayer-cheats-runtime container now compiles the complete
production FfsNetTransport.RaiseReceived body, exact driver MsgExtras case,
VersionGuard.NotePacket/NoteExtras/PeerBuild and the actual referenced protocol
constants. Decoder, fragment, socket and presentation endpoints are declared
fixture ports. The surrounding driver fixture switch is expressly a wrapper,
not a handwritten replacement claimed as the complete OnPacketReceived method.
Originals, exact projection hashes and every generated variant are retained.

One complete invocation passes268 assertions and all18 compiling causal controls
in63.30seconds. The existing252-assertion Program is byte-identical after removing
only its partial declaration and appended ingress call; all16 old mutations stay.
Two new controls separately reproduce the missing version-only ingress and the
versionless sparse-packet revocation. New assertions also cover explicit ordinary
and standalone mismatches, replacement native identities, session epochs, actual
transport batch recursion, ordinary presentation and existing local/flat guards.
Canonical completion cleanup removed the stopped run's bin/obj caches. Sources,
logs, manifests and copied executed binaries remain.

Strict Release passes with0 warnings/errors in21.51seconds. The integrator runs
the final strict Release/Debug and all16 source gates once on the integrated
union. This updated complete CLR proof can be reused with exact source/toolchain
binding; the frozen original252+16 result remains historical, not overwritten.

## Bounded dependent-consumer qualification

All production source outside RaiseReceived and OnPacketReceived is byte-exact
against7037750b5. Twenty exact imported kernels remain unchanged across Results
sender/transport, town638 delivery, npc661 admission, peer retirement, shared
window/reward placement, town-final publication, card selection and banner help.
No older fixture imports either changed ingress method. The new complete cheats
container is the only actual direct ingress consumer.

A matching bare-DLL ILSpy comparison finds1429 forms with no additions/removals:
only FfsNetTransport and NetAvatarDriver behavior differs, in those two methods.
BuildInfo differs only by worker-branch metadata. All32 embedded resources match.
An initial decompile with additional reference resolution is retained separately;
only the second export, with the frozen baseline's exact bare-input resolution,
qualifies compiled scope. Source/recipe/projection/DLL/resource hashes are kept in
the consumer audit and compiled-scope receipts.

Whole-plugin visual consumers of the frozen gate execute no native socket/extras
ingress. Their proof can remain valid under this explicit changed-input
reachability and compiled-scope qualification. This is not a claim that the full
plugin DLL hash stayed equal. There is no new capture, layout, rendering, actor,
per-frame, native queue/model or registration change.

## Original native reflection and action construction

Fresh read-only ILSpy C#/raw-IL exports use the ORIGINAL GH.Runtime.dll, SHA256
fcfcd2e420299139b6cba9a5fedb52c9cae60bb5e1292adb84152c14d5e8f495.
They do not use publicizer output or the fixture's public ports as evidence.

AllPlayers is a public static field; MyPlayer and HostPlayer are public static
getter properties; JoiningPlayers and ConnectingUsers have public getters and
private setters. NetworkPlayer.PlayerID has a public getter/private setter.
BindingFlags.Public is correct. AutoExecuteServerAuthGameAction is public/static
with exact actionType, targetPhaseType and customBinaryData names. Raw metadata
marks all14 remaining parameters optional and supplies every .param default,
including null defaults for Guid/tokens/byte[]. No reflection mismatch was found.

Original GameAction initializes isValid=true and generically assigns the numeric
action type. Its constructors, generic host factory and queue admission do not
reject unknown enum127 before Execute. AutoExecute's host path queues it with
ExecuteLocallyFirst; the original dictionary lookup occurs only inside Execute,
after the exact mod prefix. An accepted prefix leaves IsValid=true, so original
FinishProcessingAction assigns/logs/forwards it. A received event constructor
also directly copies the numeric type and BinaryData. The previously reviewed
original Bolt metadata makes127 representable in its seven-bit action field.

## Retained evidence and limits

Owned evidence lives under .planning/debug/capability-ingress and the complete
multiplayer-cheats-runtime/run-ayt130_x. The parent-approved verified archive is
stored in main .planning/validation/build762/capability-ingress-lane with a full
member SHA256 index. All1239 inherited native/toolchain inputs were rehashed and
match; source and compiled input receipts identify this exact bounded repair.

These checks prove actual source routing, CLR queue/receipt behavior and bounded
compiled scope. They do not establish real Photon delivery, Windows/Mono native
reflection execution or headset acceptance. No device/release/push was performed.
