# Build768 independent asset and provenance review

Reviewed `dev` `c4683ad26`, ModBuild768, from the isolated initialized worktree
`/tmp/gvr-mp768-review-assets`. Ownership is this report and ignored private
review evidence only. Production, fixtures, native references and game assets
were not modified. This is a documentation-only review, with two small targeted
CLR defect reproductions; no full gate or Unity suite was repeated.

## Evidence boundary and verdict

The retained paired logs still identify `5dff1344c` / Build767: the local load
banner is line128 and the remote banner line77 of the respective
`build768/hardware767/LogOutput.log` files. The remote warning at line493 is
`Ambiguous native town-service asset: texture|noise_01|256|256|3|9`.
They support the old frozen-picture diagnosis. They cannot establish either a
Build768 regression or the cause of the newly reported wholly empty picture.
Correct Build768 host/visitor logs and actual device evidence remain necessary
for that attribution; this lane does not replace them with fixture output.

The new Results-only catalogue and exact root-sprite alias have no demonstrated
new asset-policy blocker in the reviewed diff. The catalogue addresses the
repeated native descriptor omissions, and the conflicting backdrop genuinely
needs the explicit native widget identity. However, existing shared asset code
has two reproducible context/lifetime defects and a conditional font-binding
hole which the 2407/22 and 4183/2 proofs do not cover. Consequently those green
counts do not certify the complete asset lifecycle or every original-output
case. None of the findings below is asserted to have caused the hardware report.

## Findings

### P2: a retired descriptor canonical cannot recover through discovery

Source: `src/GloomhavenVR/Net/TownServices/TownServiceAssets.cs`, `Register`
lines51–58, `Key` lines70/104–109 and `Resolve` lines114–135.

`Register` writes an object's reverse key, but only fills `_assets[key]` when
the dictionary has never contained that key. A Unity-destroyed object still
occupies a dictionary entry. `Key` correctly avoids treating the destroyed
canonical as a live conflicting original, yet calls `Register`, which leaves
that destroyed canonical in place. Future discovery immediately returns the
new object's cached reverse key and never repairs the forward reference.
`Resolve` therefore fails indefinitely, even while equivalent live originals
are available. Neither waiting for the two-second scan nor introducing another
new wrapper restores it.

Minimal reproduction with the unchanged production class:

1. A Results-scoped registry keys native-equivalent textures A and B under
   `texture|noise_01|256|256|3|9`; A is the forward canonical.
2. A retires while B remains live. Resolving the descriptor fails.
3. Key a further live equivalent C, advance the scan clock, and resolve again.
   It still fails with `Original town-service asset is not loaded: ...`.

The CLR proof models Unity's destroyed-object equality and Resources census;
it is exact production registry logic, not native-engine lifetime evidence.
It also verifies that `RegisterOriginal` *does* replace a destroyed original,
which is why the new root-sprite helper handles this case separately.
Results retains `_assets` across ordinary opening retirement and source-window
replacement; only `Reset` clears it. A fresh scene/native-wrapper lifetime is
therefore a relevant production contract, although this review has not observed
that destruction sequence in a real Build768 session.

Required regression: in the existing real-Unity consumer, retire the first
canonical Texture2D while an equivalent admitted wrapper remains; resolve,
validate and display a subsequent complete picture without registry Reset.
Keep unknown/conflicting descriptors strict. The smallest candidate repair is
to let descriptor registration replace a Unity-null forward canonical while
preserving live canonical and `_ambiguous` refusal decisions. Also repair the
cached reverse-key rediscovery path. An explicit bounded registry lifetime that
prevents the state is another option; weakening admission is insufficient.

### P2: material samples carry identities across unrelated registries

Source: `src/GloomhavenVR/Net/TownServices/TownServiceMaterial.cs`, lines15–21
and31–35. Relevant callers are `TownServiceBinding.Read` lines204–206,
`TownServiceMirror.Assets` and `ScenarioResultsPresentation._assets`.

The static sample cache is keyed only by Material and Unity frame count, but
its `TownServiceValue.Text` contains keys produced by the supplied registry.
Registries may legitimately name a texture through different original-template,
font-atlas or descriptor aliases. A second registry reading the same Material
in the same frame receives the first registry's complete published value;
its own `assets.Key` is never called. Registry identity and Generation are
absent from the cache contract.

The unchanged production material code reproduces both consequences:

- Registry A samples a shared material with
  `native-town|template|merchant|graphic|_MainTex`. B, whose valid identity is
  its native descriptor, receives the identical A sample. B performs zero
  texture key reads, and `Validate` fails on A's unresolvable alias.
- A first permissive/scoped read suppresses a later ordinary registry's required
  collision/refusal decision for that material in the same frame. On the next
  frame the decision is enforced again.

The next-frame counterexamples resolve correctly, distinguishing cache context
from missing data. This is an API/context defect, not proof that a normal town
capture and Results capture necessarily share one native Material in one frame:
their usual gameplay phases differ. Do not call it the proven empty-window
cause without actual call-order/material evidence.

Required regression: sample one real native Material through two differently
prepared registries during the same frame, in both orders; assert exact local
identity, strict ordinary policy and valid receiver binding. Include a registry
Clear/Generation change within the frame. The smallest candidate repair must include the registry instance and its
Generation in sample validity; also account for registrations that change an
existing object's alias in the same frame. Separating raw material sampling from
registry key projection avoids foreign keys while retaining shared getter work.

The global `SharedValues` material pool also keys by published value alone
(lines99–110). Exact native aliases are supposed to denote equivalent originals,
so this alone is not a proven additional defect. Any lifetime repair must still
ensure existing pooled materials cannot retain a retired texture merely because
the serialized value is unchanged; exercise final material pointers/pixels.

### Conditional gap: a cached TMP font never installs late atlas aliases

Source: `TownServiceAssets.Key`, lines70 and86–94.

The first `Key(font)` computes a font identity and registers its then-current
atlas array. Later calls return at the generic reverse-key cache before visiting
the font case. If its atlas array was initially empty, or gains a replacement
texture, its deterministic `tmpfont|...|atlas|N` aliases are not refreshed.
The exact production registry reproduction confirms that an initially empty
font followed by a populated atlas leaves `atlas|0` unresolved indefinitely.
A retired atlas's forward alias also has the canonical problem above.

The native Results audit finds177 TMP text components and no legacy Text. This
review has not established that their shipped static font assets experience
late atlas population or replacement. Therefore this remains a conditional
lifecycle defect/coverage requirement, rather than an asserted current Results
blocker. Test actual Results font population modes and replacement behavior
before promoting or dismissing it. If the lifetime is supported, refresh the
font's dependency aliases before returning its cached font identity, without
reclassifying mutable atlas pixels as audited static originals. Excluding mutable legacy `Font Texture`
groups does not by itself prove TMP lifecycle completeness.

## Catalogue and exact root alias review

The catalogue is an explicit constructor opt-in in Results. The only ordinary
registry algorithm change is the additional guarded predicate in the Texture2D
collision check. The Results predicate is readonly, ordinal catalogue membership with no side
effects, and no other production constructor opts in. It cannot globally widen
TownServiceMirror.Assets. The root alias key is used only for this native Image
dependency, and its Reset receipt follows the same owning presentation.
The public no-argument constructor, descriptor grammar,
unknown/collision refusal, original bindings, shader lookup, callbacks and wire
layout remain unchanged in the diff. `SameKnownNativeIdentity` retains its prior
exceptions, including the historical packed-atlas rule; those are not newly
introduced catalogue coverage.

Independently regrouping the retained raw/sampling audit reproduces1396 rows,
1380 descriptor groups,1375 admitted keys and the same five refusals. The raw
payload total is531,175,536 bytes. Conflicting `Black_Backdrop` and `decal_mask`,
plus three mutable `Font Texture` descriptor groups, remain excluded. Grouping
uses full native name/dimensions/format/mips and raw plus sampling/color-space
fingerprints. Runtime admission remains trusted descriptor identity: there is
no live GPU-content comparison. Matching arbitrary replacement pixels to a
trusted name is outside the stated original-content contract.

The corpus covers all28 loose serialized assets/scene files, all339 material
records and their206 native Texture2D references. The independent material
inventory agrees on names, shader names and saved property tables. This is a
substantive improvement over four prefab roots. It does not inspect every
addressable bundle, runtime-generated texture or unknown third-party asset.
The audited runtime `UIFX_MaterialFX_Control` copies serialized materials and
switches to `EffectDataBase` wave/sparks/bloom originals; its relevant paths do
not generate a new texture or replace the root backdrop. Texture dependency
closure over the audited original materials therefore addresses the reported
runtime-assignment omission without pretending to be arbitrary-game coverage.

Both Results scene roots reference sprite904 / texture62 in sharedassets1;
sprite1120 / texture282 has matching sprite geometry and different image data.
Neither Results hierarchy has a1120 descendant, RawImage backdrop or material
slot requiring the ambiguous background descriptor. The root-sprite-only alias
avoids selecting either texture globally and does not transmit a client instance
ID. `CollectRoots` starts with the exact native window, and the helper runs
before EnsureSources' identity early return and before same-shape Accept.
`CardFaceMipBake.OriginalFor` unwraps the presentation sprite before binding.
The first live canonical is retained; another wrapper of that audited immutable
slot receives the same alias. Unity-null canonical retirement uses
`RegisterOriginal` and Reset clears both the registry and the receipt.

I found no incorrect new root helper branch in source. The current presentation
source differs from the prior asset review only by the four-line incremental
Accept refresh. The existing controls causally remove both root provenance and
late refresh. They do not exercise every canonical-retirement sequence, mip-bake
adapter or cross-registry state; the fixture's CardFaceMipBake adapter is an
explicit identity port.

## Why the tests missed previous failures, and what the new proof covers

The old four-tree export described its eight materials/fourteen textures; that
selection was never the native runtime material closure. UIFX replaces original
material instances after initialization, so a green static-tree fixture could
miss the next required descriptor after every single-name repair. Cropped
RGBA sprite imports and fixture atlas names also changed native descriptor
identities, hiding the colliding root background until the importer retained
its true native texture and authoring.

The final consumer now uploads195 nonempty original texture streams and retains
thirteen deferred legacy fonts as metadata; it exercises actual format/mip
getters, the complete Texture2D material-dependency census, admitted duplicate
resolution, explicit strict exclusions, capture/codec/delta, inert application
and final camera pixels. Twenty-one GUI materials/six shader property contracts
join UI rendering. Non-readable late `noise_01`, cold904/1120 preseeding and
same-shape late binding have specific causal controls. This is useful evidence
for the scoped correction.

It is not a full native bootstrap or independent process proof. Source and
observer share this fixture's static texture/font/material banks. Their
materials often share the same in-process objects and same aliases, rather than
having intentionally different registry provenance. The older native-peer test
even explicitly calls `TownServiceMaterial.Reset` before a separate registry
capture (`scripts/town-native-peer-runtime/NativePeer.cs`, line105), removing
the material-cache condition described above. No new control
crosses registry context or destroys the first admitted texture canonical.
Fonts are imported with already-populated static atlas arrays. Native Windows
fragment programs are declared GL ports; head/view conversion, native gameplay
controllers, physical/network ordering and headset output remain boundaries.

The 4183-assertion CLR catalogue proof validates decisions over the whole audited
original corpus and two compiling policy controls. It cannot detect a stale
Unity object, a foreign alias in a Material sample, a late font dependency or
any visual output. The 2407 assertions plus22 controls form a complete affected
containing-suite union, not an entire local gate or hardware acceptance. Its
expectation table is frozen from the unmutated catalogue for mutation tests;
independent classification comes from the separate raw/sampling corpus proof.
Neither count should be presented as proof of the user's entire 1:1 contract.

## Verification, failures and retained evidence

I verified the prior asset archive's115 members against every recorded size and
SHA256, including the exact current catalogue and generator sources. Its archive
SHA256 is `41027744357b7bbf1e1e6966032ab299bf5611275dd7ed896ff38a22b5b41538`.
The Results archive SHA256 matches
`f31bb7823d0150f187e3d5125c5e1bac25b5053eb7dcd6aafd772b3d8bcd28c7`.
Both final invocations have identical49 source pins and30 generated source
hashes, and all49 current input files match. I read the retained production and
control results rather than relying on aggregate labels. No successful suite
was repeated.

New targeted review runs:

| Run | Actual outcome | Elapsed |
| --- | --- | ---: |
| `run-jzshdqae` | Production material cache violates context/default-policy contracts; deliberate executable exit23 reproduces both; next-frame controls recover |1.986s|
| `run-l6z2m3n3` | Production registry remains stuck after canonical retirement and misses late font alias; deliberate exit24; explicit-original replacement recovers |2.000s|

Both builds used exact production sources, zero warnings/errors, and explicit
CLR Unity/registry ports. They are qualified defect reproductions with failing
contracts, not green product tests. No native engine destruction, real dynamic
font usage, multiplayer schedule or HMD outcome was tested. After subprocesses
were reaped, each run invoked `cleanup_completed_run`; four bin/obj cache
directories (453,470 regenerated bytes) were removed, with zero skipped paths. Executed binaries, complete
sources, compile/runtime logs, source bindings and cleanup receipts were retained
before cache cleanup. The total new measured execution is3.986s.

The verified review archive, member manifest, this report and evidence receipts
are in the main checkout's ignored
`.planning/validation/build768-review/assets-lane/`. No game/reference payload,
foreign cache, original fixture or production path was modified or cleaned.
The integrator owns decisions on follow-up implementation and actual device
qualification; this lane makes no push or hardware-acceptance claim.
