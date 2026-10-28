# Campaign movie routing

The full source review at `63406dfd` found eight native VideoPlayers in the
thirteen recovered scenes. Four had embedded clip references and received the
Quest camera-plane output adapter: Intro, MainMenu, MainMenu_gamepad and the
persistent Gloomhaven_unified background. Four had no embedded clip and therefore
escaped the startup-only binding recipe: CampaignMap, CampaignMap_gamepad,
NewAdventureMap and NewAdventureMap_gamepad. Their original VideoCamera
controllers select story/hero movie URLs later and use CameraNearPlane output.

The movie manifest keeps schema1 and its existing `clips` entries. Complete
Campaign staging additionally writes `dynamicPlayers` with the original scene,
hierarchy, name and serialized player ID, and `nativePlayerCount` for the complete
census. Runtime checks reject duplicate or missing bindings and require the
declared count. Legacy startup manifests without these fields remain supported.
Dynamic bindings attach output and bounded diagnostics without assigning clip,
source, URL, rendering mode, audio, clock or playback flags. Native owners retain
their Play, completion and URL-selection behavior. Destroyed players release
their global output hooks; no recurring scene discovery is added.

Focused evidence:

- Python startup suite: 25 tests, including full eight-player staging, unchanged
  dynamic scene bytes, duplicate native paths and legacy startup scope.
- Managed router: 208 assertions, with 22 planted defects rejected. New controls
  cover a rewritten dynamic URL, omitted adapter and destroyed-player hook leak.
- Actual recovered source census: thirteen scenes, two embedded clips/four static
  bindings, four dynamic bindings and nineteen original external movies. Each
  dynamic binding matches the original controller's player and camera PPtrs;
  all four scene hashes remain unchanged. This bounded identity experiment uses
  a media-copy seam and does not claim a new codec/container proof.

Private receipts are in `/home/claw/quest3-local/full-assets/`:
`fullgame-video-routing-review.json`, `fullgame-video-native-players.json`,
`fullgame-video-native-movies.json` and
`fullgame-video-dynamic-binding-proof.json`. The router's mutation receipts are
under this worktree's `.planning/debug/quest-startup-videos/run-1ynl147i`.

Intro and menu already have the intended source binding. The shared eye consumer
completes the movie capture before its depth-shift blits, and the full target uses
the mod's MultiPass rendering with ordinary 2D captures and validated Vulkan
camera/world-screen shaders. None of these source/compiler checks establish that
the previously reported grey headset picture is resolved. The final delivered
APK and bounded decoded/captured/per-eye hardware evidence must establish that.
