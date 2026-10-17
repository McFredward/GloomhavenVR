# Unconfirmed card withdrawal and remote board parity — Build680

Primary integrator reserves ModBuild680 for the maintainer's October17 report:
Guildmaster scenario card withdrawal visually clears a slot but fails to deselect
in the native game; the wanted overlay stays dark and actor refocus reconstructs
the withdrawn card on the board. Campaign and repeated swap/withdraw/reorder flows
and complete remote-board masks/presentation are in scope.

Base dev64a9104e7 (runtime679). Main hardware inputs are frozen under ignored
`.planning/debug/card-selection680/hardware/`; both local banners are677, while
the remote Player.log is old October11 vanilla evidence. Seven local
ArgumentNullException release stacks identify the direct nullable CharacterName
lookup before native UnselectCard is queued. HandSuppression preserves native
manager.IsShown; the native hidden-hand early return is not established cause.

Disjoint initialized worktrees and ownership:

- card680_fix: Cards/Driver and Cards/Tray, plus CardsGameApi if necessary. Native
  selection callbacks remain the authority. The final repair also retains bounded
  pending presentation through acknowledgement, distinguishes explicit withdrawal
  from view release, and guards/captures the original hand's initiative input.
- card680_contract: new scripts/card-selection-runtime/* and its Python checker,
  actual GH.Runtime/SRL callback and actor-refocus regression proof.
- card680_remote: NetAvatarDriver/RemoteAvatar/RemoteBoardFurniture only if a
  separate proven defect requires it; new remote fixture/checker. Existing
  record4 masks should carry the corrected owner state without a wire change.
- Primary: source review, version/build notes/STATE, suite inventory, integration,
  focused final checks, Debug delivery, dev commit and push.

All workers start at current dev64a9104e7. Preserve unrelated Steam Link receiver
research, its coordination notes and trailer/Quest work. Do not push worker
branches, overwrite shared baselines or repeat the unchanged221-suite local gate
for this bounded repair. Fresh relevant native/remote checks and source checks
will be recorded with inherited unchanged evidence honestly distinguished.
After each stopped run clean regenerable project caches with the existing API;
retain source/manifest/log/capture evidence, pins and current deliverables.

Both new regression suites are local-only: original GH.Runtime/SRL callbacks
and the complete original presence codec require executable native Unity/game
assemblies, not hosted metadata references. Expected inventory:223 local,100 CI,
16 source suites. No surrogate codec is substituted for hosted green output.

Checkpoint2c233c116 is integrated as29ab1f078. Its existing focused card checks
pass and native callbacks pass the first2,478-assertion fixture. General review
has additionally reproduced an inter-frame incoming-card eviction during queued
replacement and a stale actor initiative reconciliation. The fix worker owns the
bounded projection/ownership followup in Driver/Rebuild, Interactions, Flows and
Tray/Slots; native fixture owns real callback/focus causal controls and remote
fixture owns actual record4/18 codec and application tests. These findings are
closed by worker f1ca7e4ab, integrated as7e90e1cd3. Additional actual-native tests
reproduced loss of the captured left-card choice after view release; the final
helper preserves that input through the original initiative callback. Exact-source
projection passes471 checks and four causal controls. The remote fixture is
integrated asf22216482; its worker proof passes836 checks and11 controls.

Final integrated16 source and seven affected existing card suites pass, as do
Debug/Release0/0. Remote836/11 passes the stable integrated rerun. Native3496/10
and projection471/4 are composed final proof, with final source/fixture hashes
matched; the corrected projection source-binding count did not require repeating
unchanged native controls. Inventory test passes after both fixture commits are
integrated. Independent final source review approves the frozen followup.
Source-bound hosted CI and artifact/cleanup receipts are recorded at publication
under ignored main evidence. No headset outcome or complete223-suite local pass
is claimed. Do not publish unfinished source under a concurrent skip-CI head.
