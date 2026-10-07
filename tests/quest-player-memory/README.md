# Campaign Player memory handoff

The actual f392 build completed the original native content bank, then the kernel
OOM killer terminated Unity during native Player scene assembly (about13GB
anonymous RSS). The imported-object validator already unloads unused assets in
bounded groups; the completed content-build graph and later validation scopes
have no explicit full managed collection before Player compilation.

The game-only boundary runs inside content exclusion immediately before
`BuildPipeline.BuildPlayer`: collect unreachable managed objects, finish their
finalizers, request the existing safe public Editor unused-assets unload, collect
again. It preserves live references, scenes, assets, settings and disk caches.
A two-sample receipt records actual managed/Mono/native and process memory;
it makes no full-Player or headset success claim.

`Witness.cs` executes the exact extracted source method under the pinned Unity
Mono runtime with narrow Unity API boundaries.10 checks verify an actual abandoned
64MB graph/finalizer is released, a live scene reference is retained, one native
unload is requested and diagnostics remain bounded/truthful. Native Unity unload
is a controlled boundary in this fixture, not an executed native Editor callback.
The exact method separately compiles against real Unity2021.3.5 Editor/Core/JSON
DLLs. Two source controls retain the target/scope/call-order contract.

Private evidence: `/home/claw/quest3-local/full-shader-validation/player-memory-v1/`.
Actual full-project peak RSS improvement remains a measurement of the next
Player build; collection cannot guarantee that Unity returns all memory to the OS.
