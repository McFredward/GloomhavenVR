# Build 620: player-facing VR settings help

The PC hardware report found that VR-setting tooltips contained implementation
history and instructions addressed to the maintainer. The menu had two sources:
short curated `h_*` strings, and raw/translated config-file descriptions for
fallback and Advanced rows. Updating only the Graphics tab would leave the
same problem elsewhere.

## Presentation contract

`Loc.PlayerHelp.cs` provides separate, lazy English/German help tables through
`ConfigHelpForPlayers(section, key)` and `ModHelpForPlayers(hintKey)`.
The former checks exact keys, then uses the **unchanged original** `FamilyKey`
resolver for hand styles, boards and card-pile suffixes. Unknown addresses return
null so callers retain their existing fallback. Unsupported languages use
English. Settings, captions, defaults, saved keys and log tokens are not changed
by this table.

The integrator wires both `ConfigCatalog.Hint` and `ConfigCatalog.Tooltip` to the
new setting descriptions. `VROptionsTab.HintFor` prefers these descriptions over
old curated text; header/action hover paths use the dedicated mod-help table.
This covers ordinary tabs and Advanced without editing developer-oriented
BepInEx descriptions. Default/range/restart metadata may still accompany the
short description where the existing browser requires it.

Descriptions answer what changes, relevant controls/units, and visible or
performance tradeoffs. They omit request history, hardware-test instructions,
build numbers, implementation excerpts, and unsupported performance promises.
Technical diagnostic settings explain what they record and their cost, rather
than telling the maintainer how to investigate a particular previous test.

## Coverage and corrected claims

The table contains **537 exact/family description entries and 141 heading/action
entries**, each in English and German. All **138 original `h_*` IDs** are covered.
The existing options census finds 653 distinct literal/expanded binding addresses.
Its two unresolved card-pile loops are expanded from the actual three-member
`pileNames` array, yielding **659 source addresses**. This is a source coverage
count, not a claim that there are 659 persisted runtime config entries. Hidden
compatibility/migration keys have descriptions too, but remain hidden.

Audited topics include movement, hands, card fans and held cards, every board
style's geometry, buttons, figures, windows, healthbars, graphics, mixed reality,
environments, audio, multiplayer, startup, diagnostics and cheats.

Specific misleading descriptions corrected include:

- Map card fans can be inspected and arranged; the old text claimed they could
  neither be arranged nor synchronized.
- NPC and materialization descriptions explain the selected behavior rather
  than which platform previously received which defaults.
- Environment shading describes eligible opaque floors/static masonry and
  explicitly distinguishes original fading-wall shading.
- Graphics Jobs no longer promises the single biggest performance gain.
- Advanced headings no longer explain why controls moved between earlier menus,
  native callback internals or the maintainer's particle preferences.
- Healthbar depth text includes numbers and symbols and states both toggle modes.

The map-icon animation setting is being retired by the integration lane. Its
stored key's help is inert; it is not reintroduced into the menu by this audit.

## Focused verification and limits

`python3 scripts/check-player-settings-help.py --self-test` checks the real source
binding surface and complete bilingual tables, length limits and narrowly scoped
historical/developer-prose markers. Five causal content controls remove a setting,
a pile family, a heading or a German explanation, or restore developer-directed
text. Each must fail with its specific cause.

The same command compiles and executes **actual** `Loc.PlayerHelp.cs`,
`Loc.ConfigDescriptions.cs` and its unchanged German table. The only fixture
boundaries are selected-language input and the pair factory; `Mod` is supplied
only to satisfy a documentation reference and throws if called. The production
lookups, original family resolver and translation fallback are not reimplemented.
The positive fixture passes **2,272 assertions**, including all covered addresses,
all original hints, hand/board/pile families, exact prefixed keys, unknown keys,
English/German selection and unsupported-language fallback. Three separately
compiled negative controls remove family resolution, force English or make the
fallback German; all fail at the intended assertion.

The fixture suppresses only CS8600 arising from the unchanged net472 resolver's
explicit `TryGetValue` outputs when compiled against net8's additional nullable
annotations. Remaining warnings are errors. Production framework settings are
unchanged.

This is evidence for content coverage and actual lookup behavior. It does not
establish headset tooltip geometry, font readability, or rendering performance.
The independently owned menu layout changes and complete integration gate must
be verified by the primary agent before push. Focused evidence lives in
`.planning/debug/frame620/tooltips/`; initial fixture-boundary failures remain
alongside the corrected proof.
