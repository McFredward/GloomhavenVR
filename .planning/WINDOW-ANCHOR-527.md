# Anchored map-window openings — build 527

## Evidence

The new local logs identify ModBuild 526, commit 8c7e9b1c. Remote logs still identify
build 500 and do not establish the current peer outcome. No new screenshot accompanies
this placement report. The user confirms the previous temple/MR reopen repair works.

Local LogOutput.log:461, 562 and 624 explicitly record UI Quest Popup opening room making
with **one moving window**, followed by successful completion at 465, 566 and 628. The
solver included the newcomer among movable members; fixed corner windows became obstacles
that could cause this sole newcomer to be packed elsewhere. This proves an actual reflow,
not a manual grab or head-following effect.

The initial spawn also has a separate historical preference: lines 449/457, 552/559 and
614/621 choose the seat immediately inside the standing Quest Log (about 6–7 degrees from
spawn gaze). That preference ran before a free centre candidate. Earlier private-selection
requirements explicitly place the popup on the right corner when the quest log is hidden;
that case is distinct and retained.

The reflow measurement used host union hit rectangles, including transparent layout. It
could therefore report overlap without visible pictures overlapping. The old logs do not
record the exact pair of offending rectangles, so this is a source-proven admission defect,
not proof that a specific invisible graphic caused these three movements.

## Repair

- Incoming windows always remain fixed solver obstacles. Only older colliding movable
  members can animate. Protected corners and unrelated windows retain their poses.
- Converted-window order gates movement, so a late fit of an older opening cannot acquire
  permission to move a newer dialog.
- Read original painted text/image geometry, native alpha, masks and actual displayed crop.
  No transparent host/hit fallback and no dependency on enabling MR mode. Do not mutate
  native layout or input geometry to change the visual overlap decision.
- Retry unsettled materialisation and pending meshes only inside the existing opening
  deadline. No reveal, input, close or native continuation waits on layout success.
- Cancel if the anchored incoming window disappears, is grabbed or changes shared revision.
  The existing single-author stream continues to carry affected older-window animation.
- With a standing quest log, prefer the free gaze centre before the historical adjacent
  fallback. With the quest log hidden, retain the private-selection corner rule.

If the anchored newcomer plus fixed obstacles leaves no readable solution, do not move it
as a fallback; leave native UI and its grab bars available. No recurring room rearrangement,
head following, window scaling, wire record or bundle change is introduced.

## Validation

- Reflow layout/pose: 1,322 runtime assertions, 18 runtime source bindings and three mutation
  negatives (unnecessary movement, moving newcomer, off-centre pivot).
- Quest seat: 43 assertions, three mutation negatives, including hidden-log private selection.
- Native painted occupancy: 453 assertions, 28 runtime mutations and one binding negative.
  New cases cover large transparent hosts, empty text layout, zero image alpha, missing text
  meshes, completely empty windows and actual capture cropping without native layout writes.
- Shared reflow: 74 production assertions, six bindings and four mutation negatives.
- Strict Release: zero warnings/errors. Frame-order, partial-order, bilingual docs, Actionlint,
  shell syntax and whitespace pass. Config/patch/log census remains 625 / 172 / 4,733.
  Only affected runtime harnesses were run; no full-suite claim is made.

Headset check: open quest details while the quest log stands at the right table edge; reopen
and try different viewing directions. A clear centre must stay put. Then trigger a real
story/encounter overlap: only the older window should move, visibly and within view. Test
manual grabs and closing during that motion, private quest selection, and a matching-build
peer. Automated checks do not establish the final headset image.
