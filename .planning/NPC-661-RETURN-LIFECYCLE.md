# NPC661 native return lifecycle follow-up

This follows the NPC661 merchant artwork repair and the final gate's native
return660 failures. It distinguishes a faulty transport fixture from actual
source lifecycle defects. The worker's focused proofs are engine/software
receipts, not new headset evidence. Production checkpoints are `9d3c6222a` and
`dc290946d`, integrated by the root as `f3e0f400a` and `04a626591`.

## Original fixture failure

The original 24-part changed-layout fixture delayed every third capture by 12
frames. After frozen canvas recipes increased the snapshot from two to three
bounded events, that schedule delayed every final subset. A later partial
snapshot replaced each completed predecessor before a render tick. In the
failed full gate, the combined check hid the actual values: the no-loss case
observed and repaired its partial, but the loss case had `partialObserved=False`,
`repaired=False`, `dropped=True`. That did not represent its intended one-loss,
one-delay contract.

The repaired loss fixture first activates the genuine complete initial cohort,
then drops exactly one changed-member packet and delays exactly one changed
partition. Assertions explicitly count both actions. Its original 84 frames,
.7s native duration, 15Hz capture, event budgets and strict geometry checks are
unchanged. `FrozenReturnRoot660` recognizes only a complete same-packet cohort
companion with exact lane/service/session/public claim/hand/member/structure,
visibility/alpha, physical root and canvas recipe. Such a Kind1 companion is
not an ordinary root before the native sampler ends.

The previous end-overlap fixture also implicitly depended on the old subset
count. It now delays one *actual fresh preterminal* source subset until after
its sampler ends. The source-derived active instant and terminal transition
are recorded; no duration or endpoint is invented. The .65s native duration,
108 frames, 15Hz cadence and geometry thresholds remain unchanged. The phase3
start prevents the extracted active-only native step from running before the
flight begins; real Update's inactive branch would not run that step either.
The explicit packet delay, rather than that phase alone, guarantees overlap
when the codec changes its partition count.

## Source: completed predecessor lost between render ticks

`ReceiveReturnCohort` used to replace a fully assembled but not-yet-activated
snapshot when a newer partial arrived in the same inbound drain.
`NetAvatarDriver.ApplyTownServices` drains multiple packets before the single
`TickRemote`, so this is a real receive boundary, not only an artificial test.
Without a prior activated picture, repeated late final subsets can prevent
first presentation indefinitely even when complete valid snapshots arrived.

The source now retains at most one complete predecessor per cohort key. Its
Parts and Roots belong to one exact source instant; they are never mixed into
the latest partial. Exact lane/service/session/public claim/hand/revision and
all member/structure identities must match to retain it. The latest complete
snapshot has priority. Otherwise the complete prior activates once its exact
original modules exist. The retained chain is explicitly cut, so the bound is
one latest and one complete prior, under existing cohort/member caps. Ordinary
root cancellation, session replacement and network reset remove the whole
assembly, including its predecessor. Existing StaleTimeout remains the outer
lifetime bound. Native clock/gameplay continuation is not gated by layout.

The warm and cold-original proofs create the critical queue order explicitly:
one final prior subset immediately followed by a newer partial, then one
render. The final candidate derives that boundary from actual packet content,
not a fixed two- or three-event timing assumption. A later repair completes
normally while the retained older physical picture remains coherent.

## Source: terminal inherited an expired active progress floor

A 24-part receipt captured while the native flight was active completed after
its native terminal branch. The source then published its exact final pose and
changed Child/Rect geometry. At frame90 the observer still extrapolated the
previous active sample beyond duration+.25. `CardReturnClock`'s monotone floor
made the new terminal appear expired before `ApplyCardReturnMotion` could
apply its final geometry. The actual mismatch was 25.886mm at the root and
63.353mm at the printed center; preserving a wrong pivot was visible evidence,
not a tolerance failure.

An authoritative terminal is recognized narrowly: 28- or 38-float source
record, age equal to duration, all ten From values equal to To, and zero arc.
Only this exact settle receipt bypasses the old active progress floor. Normal
active receipts keep their native recipe, observed-rate mapping, monotone
progress and expiry. Terminal age is duration plus elapsed time from actual
atomic activation, so it applies the final picture once and then retains the
existing .25s grace; it is not clamped forever. Production clock constructors
receive actual receipt time. The optional historical probe default uses
sample+offset, not unrelated Unity time.

A pending terminal also cannot expire at native duration+.25 before its
geometry is complete. A real 64-member final snapshot takes longer than .25s
to deliver. Its partial assembly stays bounded by StaleTimeout and explicit
cancellation until full atomic activation starts its module/assembly grace.
Cold originals and one retransmitted missing partition are included.

## Source: complete terminal arrives before mapped source instant

A faster later transport can deliver all final geometry while the retained
source-to-observer mapping still points to an older active receipt. Applying
the terminal canvas immediately would change layout before its clock is due.
`TryActivateReturnCohort` now checks that the clock has reached the header's
source instant before applying any physical/canvas payload.

The proof includes a genuine .5s owner frame hitch at frame31. The extracted
native Update still caps that frame's progress at .05s and the flight duration
stays .65s. The source terminal timestamp can therefore remain future after
the previously admitted active extrapolation floor has expired. The complete
pending terminal must survive that expiry; the old body/front picture remains
visible and coherent until the mapped source instant, then all final root,
child and rect geometry applies together and retires within the existing grace.
No direct per-render network event, guessed delta, early final canvas or
future native branch is used to bridge that interval.

## Focused receipts and proof boundaries

The final native proof is `npc661-return660-final115-native/run-8zssz58d`:
22,174 assertions. It binds production `43d50f39e` plus the two owned source
checkpoints above. Raw Motion, Cohorts, Budget, Codec and ReturnRoots hashes
match integrated `04a626591` exactly; the equality receipt is archived.

- The changed-layout loss cases count exactly one loss and one delay. Warm
  prior activation is frame24; cold exact originals arrive frame25. All original
  partial/repair and every-frame geometry assertions pass.
- The actual 24-part native terminal is at100.6778, with exactly one delayed
  preterminal subset. Both actual delivery overlap and source ACK overlap are
  recorded true.
- The 64-part exact terminal source is101.6333 and eight bounded events finish
  at102.1, exceeding .25s. Cold/lost-part reconstruction applies the final
  picture atomically. Future replay has first receipt102.3, mapped due102.8333
  and a demonstrably expired prior active floor; it preserves old geometry
  before due and exact final geometry after due.

The separate harder pre-packing 64-part proof `run-fzd0o772` passes16,548
assertions with nine events and the same future-expiry case. The earlier
`run-yt0091_m` correctly failed its own expired-floor precondition because that
source trace had no owner hitch; its raw receipt remains preserved. Earlier
RED source pictures, initial inactive-step NaN fixture failure and the original
repeated-delay scheduling failure are preserved, not overwritten.

Actual VRCard and ItemChip source samplers, capped native update statements,
merchant terminal settle, original procedural body, production capture,
assembly, codecs, bindings, clock and renderer are compiled. Native gameplay,
model initialization and most print construction remain declared fixture
ports. The 24/64 dynamic child geometry is deliberately a controlled native
hierarchy; it does not claim a real game prefab contains64 print partitions.
The native basic-return cases retain strict owner/observer pixels. All dynamic
geometry cases compare unadjusted absolute physical/printed centers per frame
within50 micrometres; observer poses are never normalized or repaired by the
fixture. The separate NPC661 merchant suite establishes actual armor artwork.

The maintained runner adds four isolated source controls: remove only terminal
progress-floor bypass, complete-prior retention, pending-terminal age exemption,
or the future-activation guard. Each mutation checks its exact binding before
building and must compile and reach its named strict runtime assertion. The
existing external-pose control now removes both actual pre-native adoption
calls; leaving the newly added call in place had caused an escaped control.
This was a stale control binding, not a production regression. Its earlier
`run-46uljpwt` escape and all other successful causal failures are preserved.

Repeatable commands:

```sh
python3 scripts/npc660-return-runtime/run.py --no-negative-controls
python3 scripts/npc660-return-runtime/run.py --only-negative-controls
```

The default runner executes both. Root owns the complete gate and strict main
builds; this worker reports focused native runtime and causal control evidence.

The maintained final control receipt is
`npc661-return660-final115-maintained/run-po40joeb`: all nine controls pass by
reaching their expected strict runtime failure. The source-ACK-only control
fails actual geometry at78; the terminal-progress-only control fails the same
25.886mm/63.353mm physical mismatch at90. The complete-prior, pending-native-age
and future-activation controls independently fail their dedicated runtime
contracts. No control is accepted through a compile failure or changed tolerance.
