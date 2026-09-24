# Native window continuation — dev 1.0.8 / ModBuild 553

## Scope and evidence

The maintainer requested a comprehensive progression-window review after a user
reported unresponsive post-quest dialogue/rewards, a level-up card reveal, and an
unidentified third popup. This work starts at dev `e63fb284` (released 1.0.7 runtime,
build 546); it contains no NPC feature changes. Builds 547–552 are reserved by that
feature branch, so the new hotfix build is 553. This is not a release.

No log or screenshot of that user's affected run is available. Existing local logs
identify NPC build 551; remote logs identify historical build 500. The findings are
source-proven failure paths, not a reconstruction of that specific run.

## Implemented changes

- Level-up new-card reveals now have a visible, hover-responsive Continue button.
  It invokes the original `ClickTrackerExtended.ProcessClick`, which previously
  waited for raw desktop mouse input rather than a world-space uGUI click. Native
  animation readiness, SkipNextClick, character ownership and the subsequent card
  choice remain authoritative. Continue cannot award a card or finish a level-up.
- Generic native confirmations now have a VR owner on the 3D map as well as inside
  a scenario. The former scenario-Choreographer check excluded map confirmations
  even though the modal catch-all reserved them for this owner. This includes the
  required confirmation after choosing an ability at level-up.
- Confirmation presentation reads current native open state, recovering an opening
  before subscription and manager notifications removed by native reset. It
  releases for the desktop view and restores on return without hiding the game
  window. Conversion failure hands the original window to generic modal/screen
  recovery; partial restoration retains ownership for retry. One warning per
  failed opening remains visible at normal log level.
- `UIMessage` close routes through its original close-button event, preserving
  both the per-message callback and `MessageHandler.ShowNext`. A queued successor
  can reuse the same window immediately without inheriting UserClosing.
- Dismissible `DialogPopup` uses its native cancel/cleanup route. Disabled choices
  cannot become forced hides; a reused text dialog cannot invoke a stale content
  dialog's cancellation callback.
- Level-up, unlocked-location reveal, character creation, generic confirmation and
  tutorial windows cannot be raw-hidden through the mod X/chord. They retain their
  original Continue, choice or Cancel action; the existing desktop rescue remains
  available for a mandatory decision whose VR presentation fails.
- Clicking a location-unlock window targets its exact original Continue button,
  never an arbitrary first child button; camera-focus/animation disablement is
  respected. Original unlock/Guildmaster reward button bindings are additionally
  repaired for native windows that predate VR activation. Normal VR mouse-mode
  protection already covers ordinary creation; these bindings are defensive, not
  an identified cause of this report.
- Repeated openings cannot classify an interactive unknown window as a cycling HUD
  banner. Mandatory decisions and windows with native selectors/click trackers are
  exempt from both churn counting and inherited name suppression, including controls
  temporarily inactive during an animation. Already-known HUD is still excluded and
  unknown content-only cycling banners retain their existing fuse.

No automatic answers, forced gameplay unlocks, new network messages, native game
asset edits or NPC branch changes are involved. Information windows use their
native labeled buttons/body-click behavior; mandatory choices keep their actual
options. Multiplayer decisions retain the game's owner/host authority.

## Audit coverage

- [Full window-family matrix](WINDOWS-553-AUDIT.md): 152 native UIWindow-reference
  files classified into progression, decision, optional menu and passive families;
  explicit callback/ownership review for over 30 families.
- [Reward and result chains](WINDOWS-553-REWARDS.md).
- [Level-up and announcement flows](WINDOWS-553-ANNOUNCEMENTS.md).

## Validation

Focused regression suites execute the production adapters and retained native
continuation methods where available. Unity boundaries are modeled explicitly;
passing tests do not establish headset layout or actual network delivery.

| Suite | Assertions | Rejected behavioral negative controls |
|---|---:|---:|
| Native reward showcase and exact unlock dispatch | 351 | 18 |
| Native level-up reveal continuation | 345 | 6 |
| Queued messages / semantic popup cancellation | 355 | 8 |
| Complete confirmation surface lifecycle | 105 | 6 |
| Final mandatory close admission | 116 | 4 |
| Menu lifecycle / repeated interactive popup admission | 7,336 | 21 |

Combined source, wire, production regression and strict Release verification is
recorded below when complete. The three new suites are registered locally and in CI.

## Hardware check

1. Complete a campaign quest with NPC dialogue, rewards and a newly unlocked quest;
   continue every page and confirm the map becomes usable again.
2. Level up: wait for each card reveal, press Continue, select a card, cancel its
   confirmation, then select/confirm. No card may be chosen by Continue itself.
3. Repeat the sequence/confirmations, including a personal-quest completion or
   retirement when available. Read and close multiple queued notices.
4. Check tutorial Continue versus task-completed hints and character creation
   Back/Cancel. These must not be bypassed with a generic close.
5. Repeat with another player, respecting each character's ownership and shared
   story/reward continuation. Dev build 553 must match on participating VR clients.

No hardware acceptance is claimed yet, and no finite source audit guarantees the
absence of unrelated game, engine or network faults.
