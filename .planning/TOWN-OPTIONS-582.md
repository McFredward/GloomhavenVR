# Town presentation controls, ModBuild 582

The existing `WorldUI/ImmersiveTownServices` boolean remains the live, persisted
presentation setting, defaulting to `true`. It now appears as an ordinary native
on/off toggle in **Environment → Campaign map**, immediately below `Rig/Vanilla2DMap`.
The speech and sound-effect toggles follow it. All three settings fold away when
the original 2D map is selected; the two audio settings also fold when immersive
residents are disabled. This changes only menu presentation and does not migrate
or rewrite saved values.

`scripts/town-service-options-tests.sh` compiles the production curated tree,
dependency filter and bool-row binding against a small Unity/config fixture.
It checks both languages, both master states, the 2D-map fold, persistence,
donor callback removal and eight negative controls. It cannot establish headset
layout or hit-target appearance; those require the next hardware check.
