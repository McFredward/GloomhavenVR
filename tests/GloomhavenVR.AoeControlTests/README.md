# AoE controller and tutorial regression coverage

Run `python3 tests/GloomhavenVR.AoeControlTests/run.py`. The executable compiles the
production `AoeControl` and `TutorialAoeHint` against narrow native/input test doubles.
It checks physical handedness, native targeting eligibility, immediate flick/repeat,
attack versus object-pattern redraw, world-grab exclusion, tutorial key resolution,
and six deliberately broken implementations. It does not emulate native rendering.

Source evidence for the September 22 report:

- `resources.assets` LanguageSource objects 10357 and 10358 contain the desktop term
  `SCENARIO_PUZZLE_5B_08` (R) and console term
  `Consoles/SCENARIO_PUZZLE_5B_ROTATE_MESSAGE` (`{UI_RETRY}`). The exact page/title
  override works even after the unsupported gamepad glyph has resolved to nothing.
- `WorldspaceStarHexDisplay.ListenForTargetingInputEvents` uses mouse-facing for
  range <= 1, keyboard rotation for larger ranges, and redraws either attack or
  object-placement stars. The VR consumer retains those native rules.
- `Choreographer` handles `PlayerSelectingObjectPosition` and
  `ActorIsSelectingDamageFocus` with native `TargetSelection` without requiring the
  `WaitingForAreaAttackFocusSelection` choreography state. The older VR-mode gate
  rejected these effects even though the native display accepted them.
- Native target submission includes `AreaEffectAngle` in `TargetSelectionToken`.
  This change still uses that submission and its existing ownership checks.

Headset checks remain necessary: Tinkerer rotation text, both stick directions,
rotatable placement/damage effects, left-main-controller settings, turning and world
grab, and target confirmation observed by another multiplayer participant.
