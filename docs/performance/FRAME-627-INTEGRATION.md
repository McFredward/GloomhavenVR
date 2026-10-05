# Build627: common PC/Frame rendering integration

## Authority and baseline

The maintainer approved the separate Steam Frame handover on 2026-10-05 and
required the current NPC/multiplayer work to finish and push first. Build626
reached `origin/dev` as `ddfe9403a`; only then was the verified bundle imported.
Candidate `b603947e87327b4a1f5155af43bf017435b8eaed` is based on Build625
`01503d4c2`. The local merge is `19256ed12`, with common ModBuild627.

The original game, NPC626 return clocks, native enhancement admission and print
geometry, canonical avatar items, accepted temple path, public map faces and
visitor-local pre-drop guides remain outside this rendering change. The Quest
implementation branch is independent and is not integrated or modified.

## Reversible controls

All nine independent controls use the same PC/Frame runtime and package. Frame
only seeds unsaved values; saved entries survive. Explicit graphics-profile
selection sets a preset once, and every control remains editable. Eye-resolution
defaults are unchanged. There is no planar or 2.5D replacement for the board.

The new controls select verified private readable environment geometry, bounded
camera instancing, cheaper wall shading, near/distant wall and pillar detail,
visible event-free idle sampling, and exact shared material/UI source reads.
The full keys, profile values and tradeoffs are listed in the historical
[implementation report](FRAME-625-IMPLEMENTATION.md). Private original geometry
permits grouping unreadable native meshes without writing them. Optional coarse
geometry/shading and less frequent compatible idle motion are visual compromises;
exact read sharing does not select a different owner picture or callback cadence.

## Integration review

The new owners must restore native rendering before content cloning/writes,
unsupported cameras/consumers, option disable, interruption and teardown. They
never replace game authority, collision, rooms, doors or tactical contents.
Causal renderer tests cover the actual camera path, not just desired state.

The review found additional ownership boundaries which the candidate's fixtures
did not establish. Supplementary vertex streams and native static batches must
retain native submission; per-object probe lighting must not become one aggregate
sample. Terrain proxies must retain rendering layers. Material loading must
restore camera leases before both the initial native hide and later material
assignment, including idle proxies. These are correctness boundaries, not proof
of a larger batching population.

Idle snapshots must not bypass native LOD selection, cloth-bearing rigs, late sorting,
motion-vector or occlusion changes. Unsupported native ownership remains native.
Exact UI/mirror reads have separate causal tests for write/callback ordering and
virtual getter fallback; no broader UI or town-animation throttle is introduced.

The probe restriction deliberately makes grouping conservative. An independent
native census found BlendProbes/reflection flags on 1,132 of 1,134 Crypt renderer
records. All thirteen native level files and their shared/global assets contain
no ReflectionProbe objects; only levels 6/7/8 refer to a LightProbes asset, whose
baked coefficients and occlusion arrays are empty. Those offline facts do not
prove the current camera's dynamic probe inputs. This integration therefore keeps
flagged per-object probe owners native and does not claim a submitted grouping
gain for those records. The simpler environment shader uses SH9 and is not an
unlit exception. A future absence-aware admission needs causal live-camera proof.
Terrain geometry/shading and exact read sharing remain independent of grouping.
Rejected material/probe ownership now precedes private geometry preparation. The
real unreadable native floor fixture makes zero bank-entry requests while refused;
the probe-free control makes two and creates the original compatible chunk. This
avoids source hashing and mesh decoding for draws which cannot use them.

The tooling review also found that missing game sources could successfully export
an empty receipt and erase prepared originals during regeneration. Extraction now
rejects missing/unmatched/empty inputs before modifying prepared assets. Seven
actual-script/malformed-native-stream assertions cover that path. A fresh read-only
source census matches all 1,139 originals and six ambiguous exclusions. The
3,170 streams retain finite bounded channels, original vertex indexing, unchanged
attributes, submeshes and winding. The game-exact Unity loader decodes the actual
package through the production decoder. Null-graphics availability does not prove
shader output on a headset. No asset-quality reduction was made in this review.

## Executed review evidence

The focused environment follow-up passes 11,271 native-engine assertions and
the precise eager-bank-access causal control. Earlier focused controls cover
supplementary streams, probe safety and genuine loader hook/recovery dispatch.
Terrain passes 103 assertions with 31 controls, then 105 with the added
rendering-layer control. Native visible-idle proof passes 449 assertions; all
25 causal controls pass across explicitly retained focused runs. Its supported
pose/pixel fixture disables LOD; native LOD and cloth cases prove fallback, not
idle sampling savings on those rigs. A fixture-only pending-Destroy component
lookup was corrected by waiting one Unity frame after diagnostic Shutdown.

Exact shared local UI passes 1,651 production and 1,650 unsupported-version
fallback assertions plus eleven controls. Exact remote reads pass 625 assertions,
eight runtime controls and a scanner control. Neither production UI lane required
a review correction. See the dedicated read and visible-idle reports.

The final strict Release build has zero errors/warnings. Actual compiled
Build626-to-627 output changes 25 types and adds seven, with no removal; eight
changed types contain only the inlined common ModBuild. The remaining types
belong to the documented renderer/assets/settings/UI owners. Public surfaces
retain every existing entry: 669 config keys (+9), 215 literal patch registrations
(+1 loader-start hook), and unchanged 4,790 log tokens. All five EN/DE player
document pairs and all bundle/figure-bank checks pass. Actual packaging includes
the byte-exact environment bank at the plugin path and passes text, layout and
Steam Frame launcher checks.

The complete integrated attempt recorded **141 scopes** in 2,683.2 seconds:
140 direct passes and one retained Unity-startup timeout in the merchant catalogue.
All 52 already-compiled catalogue variants and all 29 bound source hashes match
the final tree. Continuing that same manifest passes 23,974 production assertions
and all 51 required causal controls; no fixture or production change was needed.
The failed original report remains failed and is retained. Successful unrelated
scopes were not repeated. The source-gate attempt passes all fourteen scopes, with
four affected review continuations and one final early-admission continuation.

The one bounded early-admission production correction occurred before its
scheduled environment scope ran. That actual scope passes all 63 variants and
matches the final production/fixture hashes. All 10,333 tracked runtime, test and
asset inputs remain stable after that correction. The separately executed final
wire binary passes **308,358 golden/assertion checks**; the wrapper had stopped at
the retained catalogue timeout before reaching the golden executable. Final
private receipts and continuation boundaries are retained in
`.planning/debug/frame627/validation-ledger.json`. The prior candidate's 82/141
restricted run remains historical partial evidence and cannot certify this tree.
No Build627 headset performance or appearance outcome is claimed.

## Next hardware evidence

Both NPC observers need Build627: verify stock and owned-item buy/sell/cancel,
replacement/manual reclaim, cabinet return after an unrelated release, immediate
re-pickup, and mage offered-card/options/hover/return admission. Public town faces
and the accepted priestess path retain their existing contracts. These remain
headset checks, even after source-bound motion/admission tests pass.

For Frame, preserve the selected eye resolution and compare the same loaded room
with each reversible control; changes during retuning/loading are outside steady
play. New keys seed only absent entries, so use the Standalone profile explicitly
when a saved profile should adopt all preset values. Log submitted/refused chunks,
terrain proxy ownership and idle admission rather than assuming a setting name
proves savings. Probe, LOD and cloth refusals are correct native fallback, not
reduced draw/animation cost. Hardware frame-time and visible-transition evidence
must establish which independently admitted lanes help on the device.
