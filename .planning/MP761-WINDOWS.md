# MP761 window and control review

Scope: maintainer's paired ModBuild759 multiplayer report, points1,5,6,10.
The source baseline is devf93ba368c (runtime760). This lane does not own card
flights, MR/material coverage, native figure audio or the wire implementation.
The final integrated multiplayer delivery is classC; the primary agent owns its
complete gate. Focused checks below are not a complete-gate claim.

## ESC multiplayer chooser

Matched hardware banners: both pinned LogOutput logs report ModBuild759,
version1.2.4-dev+f996308. Main inputs are preserved under
`.planning/validation/build761/hardware759` in the primary checkout.
Remote lines5755–5766 show the exact disagreement: closing the native multiplayer
submenu runs its native hide; the native chooser is listed among the windows the
Options key hid. The intervening sticky sweep explicitly leaves the chooser
alone because `MutiplayerPlayerPicker` was missing from the ESC-child family.
Thus the game cleaned up its assignment widgets while their old force-visible
VR float remained. This was not a native OnHidden failure.

The family now includes exactly `MutiplayerPlayerPicker`, the popup serialized
on `UIMultiplayerEscSubmenu.selectPlayerPopup`. Native OnHidden calls its Hide,
which hides the chooser and clears selection. The existing complete modal closer
then releases its already-native-hidden sticky float without replaying gameplay.
`MutiplayerHeroAssignPanel`, mandatory join assignment, stories and quests remain
outside the ESC family. The chooser is not added to global sticky player menus.

Focused proof: `scripts/check-esc-roster-close.py`, run-zp26b65z:
11 assertions execute the production family predicates, sticky sweep, complete
modal closer and FindPanel. The old missing-family mutation fails at the
already-native-hidden chooser release. The native game-controller/bootstrap and
other-flow eligibility are declared ports; this is not a native Photon session
or a headset picture. An initial fixture compilation failure (public enum XML
comment warning) is retained under run-jhxe8a2k and was fixed only in the fixture.
Both completed runs have canonical cleanup receipts.

## Remaining owned work

Rest glow, initiative-edge native geometry and results presentation are under
implementation. Their evidence and limits will be recorded before handoff.
