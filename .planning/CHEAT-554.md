# Scenario victory test action — dev 1.0.8 / ModBuild 554

The maintainer requested a shortcut to reproduce the post-quest windows reviewed in
[WINDOWS-553.md](WINDOWS-553.md), without playing each scenario to completion.

## Behavior

- The existing `[Cheats] Enabled` gate remains false by default, in
  `BepInEx/config/dev.gloomhavenvr.cheats.cfg`. Enable it and open VR Options →
  Advanced → Cheats → Win current scenario. No new config key was added.
- Two presses within eight seconds confirm for the same scenario instance. Opening
  the Cheats page again clears the armed state. The action clearly says that normal
  victory progression changes the savegame; it is not a preview of rewards.
- The handler rechecks the config gate, scene and offline session on the press. It
  refuses no scenario, loading, native story/damage decisions, stopped/restarting
  rules, an existing result, or a previously requested win of that scenario instance.
- Close the VR Options and sticky pause-menu presentation through their existing
  close routes. Native ESC Hide alone leaves a sticky float visible. Do not force
  unpause other native systems or hide gameplay decisions.
- Call the game's `DebugMenu.WinNoToggle()` once. Its normal `WinScenarioCoroutine`
  awaits `EndScenarioSafely`, records Win, applies round chest rewards and shows the
  appropriate Campaign/Guildmaster/single-scenario results. Continue using the
  original result controls to return to the map and receive quest rewards.
- Like the existing test menu, this action is offline-only. The game does have a
  network debug-win action, but this change does not expose a multiplayer shortcut.

## Evidence and test limits

Native source inspected: `DebugMenu.WinNoToggle`,
`Choreographer.WinScenarioCoroutine`, `SceneController.EndScenarioSafely`,
`ESCMenu.Hide/OnHide` and mod `OptionsToggle.CloseAll`.

This shortcut does not manufacture XP, personal-quest progress or other rewards a
scenario did not earn. To reproduce a level-up, choose a party close enough to its
XP threshold. It does not repair an already blocked scenario coroutine.

Hardware check: in a disposable test save, open a running campaign scenario, use
the two-press cheat, continue its result screen and verify the map's rewards/new
quest windows. Repeat in another scenario and verify one reward sequence per win.
The native flow and automated boundary tests cannot establish headset acceptance.
