# GitHub initiative placement and reported turn-stall follow-up

Base: delivered dev583c2282e, Build756. Investigation date2026-11-03.
Issues: https://github.com/McFredward/GloomhavenVR/issues/24 and
https://github.com/McFredward/GloomhavenVR/issues/27.

## Evidence boundaries

Original public PNG/MP4/ZIP/log and issue/comment JSON are preserved in ignored
debug/github-issues-20261103/hardware/capture-220837Z with per-file hashes. The
two issues attach identical log/ZIP bytes, not independent host/observer logs.
Both streams identify one Build753 client, ShootMyFace77/player10, hosted by
DeepRifter/player1. All four advertised peers use753. The screenshot is older,
has no build banner and shows a very distant small initiative strip above a
normal board. The complete13.6s video shows a stationary Round11 symptom, not
its initiating EndTurn press or recovery. Its13 selected chronological frames
were inspected. Audio was not reliably transcribed.

The supplied logs instead contain resumed Round14, a successful scenario ending,
then another scenario's Rounds1–4 and an explicit MainMenu quit. All52 original
ReadyButton callbacks complete, including15 EndTurn→Pass actions. All83
end-ability,36 end-turn and three end-round synchronization barriers complete.
These observations exclude those captured calls from the hypothesized lost
callback/ACK failure; they cannot exclude an unrecorded stall or invisible choice.
The video and logs have not been established as the same game start. A request
for clarification is pending with the maintainer.

Ownership takeover is not proof of an ACK failure: native
Choreographer.OnControllableOwnershipChanged also transfers processor state,
closes popups, cancels dialog choices and refreshes action controls. No native
ACK, phase, permission or rule callback may be synthesized to guess at recovery.
The separate ESC confirmation placement regression was repaired in754.

## Classified scope before implementation

ClassB for two disjoint bounded presentation repairs, inheriting the freshly
delivered756 qualified complete271-suite checkpoint,16 source suites and306739
wire assertions. No protocol, actor authority, global capture coordinator or
renderer infrastructure change is proposed. Escalate toC if that boundary fails.

- Initiative worker: execute the actual slide/coordinator and Unity layout with
  the native world-X writer; verify the changed-row foreign-Z latch candidate
  and separately first conversion during an existing native batch. Fix only
  reproduced depth/row ownership. Native actor order, animation timing, board
  pose and manual zoom must remain intact. Existing binding fixtures substitute
  the slide and cannot certify this path.
- Static-copy worker: StatPanelSurface.StripLogicComponents removes singleton
  controllers then tries UIWindow once. Ordinary Popup controllers can retain
  their required window and survive activation, despite the inert-copy contract.
  PropInfoSurface reuses that helper. Remove only clone logic before activation,
  preserving original presentation/layout and original source/singleton state.
  This independent hole predates1.2.1; neither copy-creation marker appears in
  the supplied logs. It is not an established cause or repair of issue27.
- Integrator: review both implementations and causal controls, register any
  second suite, stamp757, strict final build, all source gates, affected suites
  with exact worker-input reuse, public-surface and compiled-scope comparison.
  Do not repeat756's successful full checkpoint for these bounded changes.

Workers use separate worktrees from583c2282e and disjoint source ownership.
Heavy Unity runs are coordinated. Every completed run uses canonical cache
cleanup; source/manifest/capture/log proof is retained. Remaining756 integration
proof is losslessly archived before its worktree is retired. No GitHub comments,
issue closure, release publication or local ZIP is authorized by this review.

## Delivery status

Both bounded repairs are integrated. Initiative tests reproduce the old cold
adoption and changed-row errors using original LeanTween IL, actual Unity layout
and the production coordinator. Original layout supplies destinations; adoption
captures origins before a foreign world-X update. Rising-edge sampling, the
first visible frame and the Abort-before-batch path preserve canonical planes.
New pooled entry roots use the verified original prefab rootZ0, leaving nested
portrait styling alone. Source-bound EOF scheduling is an explicit Editor test
port; a full native scene/Photon bootstrap is not claimed. Parent inspected the
retained corrected/control camera images too: they are colored geometry/depth
witnesses, not original portrait artwork or a headset capture.

Final runtime coverage is1239 assertions and19 compiling causal controls:
initiative1029/7 (six production cases,40.21s), existing binding146/7, static-copy
64/5 (23.43s plus15.81s for its remaining legacy control). Static-copy coverage
is qualified across two final-input runs; earlier compile/black-raster failures
remain failed and retained. Native setup and test-order failures for initiative
are likewise retained. Final row, binding and copy source/dependency hashes are
verified against the merged source; generated original-font/prefab exports bind
to their immutable archive members. The initial parent verifier had mistakenly
looked for those generated files in the live checkout; the corrected mapping
does not change any production or fixture input.

Actual strict Release/Debug pass0W0E15.39/13.38s, all16 source suites pass64.22s,
and the scheduler's19 tests pass with two expected external-mode skips in2.07s.
Inventory is273local/119CI/16source. The compiled continuation takes19.37s and
shows exactly four behavior types plus nine exact756→757 inlined build literals;
no added/removed types and all32 resources byte-identical. Adjacent756 surface
census remains670config/274patches/4846tokens with no removals. Its first private
baseline reconstruction lacked the census's Python helper; adding that exact
historical helper repairs only the verification setup. No shared baseline was
overwritten. Source, strict, compiled, surface and worker-input receipts live in
ignored debug/issues757/integration; original756 qualified271/306739 proof is
inherited for unchanged systems. No fresh full-gate or golden run is claimed.

Own workers and build caches are retired after verified evidence preservation.
The canonical cleanup rejected two synthetic build-run names before accepting
its exact eight-character naming contract; no protections were bypassed. Eight
actual Debug/Release assembly/symbol artifacts remain,1.793GB of byte-identical
output movie copies are removed, and canonical cleanup removes427.96MB caches.
The older756 integration proof is fully verified in solid-zstd before retiring
its worktree; pins and hardware originals remain, other worktrees untouched.

Issue24 has a demonstrated source repair; attribution of the older screenshot
and actual headset acceptance remain unverified. Issue27's stall itself remains
unresolved without a matching captured failure. The static-copy fix is an
independent safety repair; it does not force turns or acknowledgments. The
maintainer's pending video/log clarification remains useful. No issue is closed
or commented on automatically. Delivery is dev only, no release or local ZIP.
