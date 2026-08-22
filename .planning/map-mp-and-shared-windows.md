# 3D-Map multiplayer, and windows that everybody sees — design

> Status: **DESIGN ONLY.** Nothing under `src/` was changed to write this, nothing is committed.
> Every claim below is marked either **READ** (file + line, quoted) or **INFERRED** (with what
> would confirm it). This project has been burned repeatedly by confident inference, so the two
> are never mixed in one sentence.
>
> Baseline: `NetProtocol.ModBuild = 219` (`src/GloomhavenVR/Net/NetProtocol.cs:419`), HEAD
> `44084c1`. **`.planning/STATE.md` is stale** — it says "ModBuild 148" and "Records 1–17 and
> 22–29 are used; 30+ free". Both are wrong (see §F.1). Anyone who follows STATE.md to pick a
> record id will collide. Fixing that line is a one-minute edit and should ride the first commit
> of this work.

---

## 0. The two requests, verbatim

> **2) Multiplayer für die 3D-Map:**
> a) Welche Map angezeigt wird (Gloomhaven oder World-Map) soll synchronisiert werden.
> b) Welche Quest gerade angeklickt ist soll synchronisiert werden.
> c) Die mouseover Infotafeln sollen synchronisiert werden.
> d) Wenn eine Quest angeklickt wird das erscheinende Fenster soll voll synchronisiert werden in
> der man die Quest bestätigen kann, genauso wie die darauffolgendene Story-Fenster. Im Flat-Spiel
> sieht jeder seine eigne Story und kann in seiner eigenen Geschwindigkeit durchklicken und der
> Geschichte zuhören - das soll hier nur für die Spieler gelten die die 3D-Worldmap ausgeschaltet
> haben - aller anderen synchronsieren sich den aktuellen Stand der Story. Klickt einer weiter ist
> es für alle im 3d-Worldmap-Raum weitergeklickt worden. Im Szenario selber gilt das Selbe für die
> Dialogfenster, das sollte bereits implementiert worden sein.

> **3)** Die Fenster die für alle Spieler sichtbar sind sollen eine andere Farbe beim dem
> Greifbalken haben (zB Blau) um anzuzeigen, dass es ein Fenster ist das alle sehen. Die Position
> dieser Fenster sollen voll synchronsiert werden, auch wenn es jemand woanders hinverschiebt.

Two rulings inside those paragraphs govern everything below and are quoted again where they bite:

* **The 3D-map scope.** *"das soll hier nur für die Spieler gelten die die 3D-Worldmap
  ausgeschaltet haben"* — a player with the 3D map **off** keeps the flat game's per-player story
  pacing, untouched. *"Klickt einer weiter ist es für alle im 3d-Worldmap-Raum weitergeklickt
  worden"* — the sync population is exactly "the people standing in the 3D room".
* **The scenario is already done.** *"Im Szenario selber gilt das Selbe für die Dialogfenster, das
  sollte bereits implementiert worden sein."* — **READ, confirmed:** that is wire record 19,
  `src/GloomhavenVR/Net/RemoteStorySync.cs` + `NetProtocol.cs:11006-11199`. Nothing in this design
  changes record 19's bytes or its behaviour.

---

## 1. The one-paragraph answer

Three of the four map facts are genuinely local and one is already on the game's own host-
authoritative wire. The map's world↔city surface (2a) has **no `GameActionType` at all** and is a
local UI toggle — though two already-synced actions (`MoveToNewNode`, `SelectQuest`) drag it as a
side effect, so only the *browsing* case is left to build; the mouseover placards (2c) are the
mod's own hover machinery; the map story windows (2d) are driven by **`MapStoryController` — a
completely different singleton from the `StoryController` record 19 syncs**, which the mod has
never referenced, and for the Gloomhaven-intro trigger an unclicked map story leaves that client's
`ActionProcessor` **`Halted`** in an online session, so 2d is partly a stall fix and not only a
comfort feature. The committed quest
selection (2b) *is* already synced by the game, by name, with a string id, and adding a second
channel for it is forbidden — but the host's **pre-commit staging** ("which quest is currently
clicked", before the host readies up) is local, and that is probably what the request means. The
crux the whole task depends on — a stable cross-client identity for a map location — **exists and
is the identity the game itself puts on its own wire**: `MapLocation.Location.ID`, a string from
the campaign YML. Two new extension records (**20** and **21**, the last two of the reserved
parallel-work block) carry everything; both are written only while the 3D map room stands, so
every packet of every player who has the feature off stays byte-identical to ModBuild 219's. Task
3 reduces to one new predicate file, one one-line change to `PanelPoseWatch`'s `peerOwned` term
(without which the pose sync silently loses a write war), and a two-line tint.

---

## 2. Section A — what is already synced, and by whom

### 2a. The world-map ↔ city-map switch — **GENUINELY LOCAL**

**READ.** The writers are two lines in `MapChoreographer`:

```csharp
// decompiled/GH.Runtime/MapChoreographer.cs:3714-3735  OpenWorldMap(bool transition, Action onSwitched)
    SetMapConfig(worldMapConfig);
    worldMap?.SetActive(value: true);
    if (cityMap != null) { cityMap?.SetActive(value: false); }

// decompiled/GH.Runtime/MapChoreographer.cs:3737-3756  OpenCityMap(bool transition, Action onSwitched)
    SetMapConfig(cityMapConfig);
    worldMap.SetActive(value: false);
    cityMap.SetActive(value: true);
```

with the fields at `MapChoreographer.cs:64` (`private GameObject worldMap;`) and `:70`
(`private GameObject cityMap;`).

**Every caller of those two methods, exhaustively** (`grep -rn "OpenCityMap\|OpenWorldMap"
decompiled/`):

| caller | file:line |
|---|---|
| `WorldMapMode.Enter` | `decompiled/GH.Runtime/WorldMapMode.cs:26` |
| `CityMapMode.Enter` | `decompiled/GH.Runtime/CityMapMode.cs:31` |
| `UIGuildmasterHUD` map button listener | `decompiled/GH.Runtime/UIGuildmasterHUD.cs:216` |
| `UIGuildmasterHUD` city button listener | `decompiled/GH.Runtime/UIGuildmasterHUD.cs:234` |
| `UIPersonalQuestResultManager` | `decompiled/GH.Runtime/UIPersonalQuestResultManager.cs:185` |
| `MapChoreographer` internal (init/travel) | `:1234`, `:1238`, `:1273`, `:3682`, `:3689` |

and the trigger chain for the user-facing case is
`UIGuildmasterButton.OnSelected → UIGuildmasterHUD.UpdateCurrentMode(newMode)`
(`UIGuildmasterHUD.cs:208-237`, `:435`) `→ GuildmasterMode.Enter → OpenWorldMap / OpenCityMap`.

**Is it on the game's wire? No.** `grep -n "SendGameAction" UIGuildmasterHUD.cs CityMapMode.cs
WorldMapMode.cs GuildmasterMode.cs` returns **nothing**, and the entire `GameActionType` enum
(`decompiled/GH.Runtime/FFSNet/GameActionType.cs`, 121 values) contains **no** member for a
map/city/world switch. The nearest members are `SelectQuest`, `SelectCityEvent`,
`SelectTownRecords`, `MoveToNewNode`, `EnterScenario`; none of them names the surface. Also
`GuildmasterMode.Enter()` (`decompiled/GH.Runtime/GuildmasterMode.cs:31-38`) only logs analytics,
selects the button, sets the controller focus area and invokes `onEnter` — no networking.

**BUT — three synced actions already drag the surface as a SIDE EFFECT, and our record must not
fight them. READ:**

| site | what it does |
|---|---|
| `MapChoreographer.ClientMoveToNode(GameAction)` — `MapChoreographer.cs:2966` | the receiver of `GameActionType.MoveToNewNode` calls `UpdateCurrentMode(quest.Type != City ? WorldMap : City)` |
| `UIMapMultiplayerController.PreviewQuest()` — `UIMapMultiplayerController.cs:667-679` | reached from the `SelectQuest` receive path; sets `City` or `WorldMap` on the client |
| `MapChoreographer.MultiplayerStartup()` — `MapChoreographer.cs:3003` (and startup `:342`) | `UpdateCurrentMode(EGuildmasterMode.WorldMap)` |

So at the two moments that matter most — the host selects a quest, and the party travels — the
surface is **already** aligned by the game itself. Record 20's surface field therefore covers only
the *browsing* case (somebody presses the city/world cap with nothing staged), and the receiver
must be **change-gated against its own live `MapIconLayer.CurrentSurface`** so it never presses a
button the game is already about to press. *A record that finds the surface already correct is a
no-op, and that is the common case.*

**Verdict: genuinely local → needs a mod record.** The seam is already built and already used by
the mod: `MapRoomDriver.PressGuildmasterMode(EGuildmasterMode mode, string source)`
(`src/GloomhavenVR/WorldUI/MapRoom/MapRoomDriver.cs:117-118`), which dispatches
`ExecuteEvents.pointerClickHandler` on the real `UIGuildmasterButton` `Toggle` through
`MapButtonRail` — whose class doc states the contract we need (`MapButtonRail.cs:44-48`): *"A
press dispatches `ExecuteEvents.pointerClickHandler` on the real uGUI `Toggle` … The full guard
chain runs — interactability, the game's own `canToggle` predicate, whatever it does about
multiplayer authority."* So a remote-driven surface change runs the game's own path, including its
refusal when the city is not unlocked.

**Reading the local surface** is already solved twice over:

* `MapIconLayer.CurrentSurface` — `src/GloomhavenVR/WorldUI/MapRoom/MapIconLayer.cs:712-715`,
  `enum MapSurface { Unknown, World, City }` at `:672-677`, latched once per frame from
  `MapParchment.ResolveActiveMapGo` *"so the icon dial and the parchment can never disagree about
  which map the player is looking at"*.
* `MapParchment.IsCity` — `MapRoom/MapParchment.cs:67` — used in the driver's own report line,
  `MapRoomDriver.cs:467`.

`MapRoomDriver` itself computes `shown` at `:158-161` but keeps it private (`_verdictShown`,
`:195`). **Use `MapIconLayer.CurrentSurface`**; `Unknown` is a real, reachable state (both map
GameObjects are down for the frames of a transition, `MapIconLayer.cs:666-671`) and must be
published as "I do not currently know", never as "world".

### 2b. "Which quest is currently clicked" — **THE COMMITTED SELECTION IS ALREADY SYNCED; THE STAGING IS NOT**

This is the one item where the answer is *both*, and getting the distinction wrong would put a
second source of truth on the wire.

**READ — the committed selection travels on the game's own wire, host-authoritatively, keyed by a
string id:**

```csharp
// decompiled/GH.Runtime/UIMapMultiplayerController.cs:259-266
public void ConfirmSelectedLocation()
{
    if (FFSNetwork.IsOnline && FFSNetwork.IsHost && InQuestSelectionPhase())
    {
        OnReady(ready: true);
        IProtocolToken supplementaryDataToken =
            new LocationToken(Singleton<AdventureMapUIManager>.Instance.LocationToTravel.Location.ID);
        Synchronizer.SendGameAction(GameActionType.SelectQuest, ActionPhaseType.MapHQ,
                                    validateOnServerBeforeExecuting: false,
                                    disableAutoReplication: false, 0, 0, 0, 0,
                                    supplementaryDataBoolean: false, default(Guid),
                                    supplementaryDataToken);
    }
}
```

and the **receiver**, which I traced end to end:

```csharp
// decompiled/GH.Runtime/FFSNet/GameAction.cs:210-217
{ GameActionType.SelectQuest,
  delegate(GameAction a, ref bool v, ref bool f)
  { global::Singleton<MapChoreographer>.Instance.ProxySelectedLocation(a); } },

// decompiled/GH.Runtime/MapChoreographer.cs:3373-3390
public void ProxySelectedLocation(GameAction action)
{
    string locationId = ((LocationToken)action.SupplementaryDataToken).ID;
    FFSNet.Console.LogInfo("MAP: Host Selected Location " + locationId);
    MapLocation mapLocation = m_Scenarios.SingleOrDefault(x => x.Location.ID == locationId);
    if (mapLocation == null) mapLocation = m_Villages.SingleOrDefault(x => x.Location.ID == locationId);
    if (mapLocation == null) Debug.LogError("Failed to find " + locationId);
    InitializeSelectQuestReadyUp();
    Singleton<UIMapMultiplayerController>.Instance.ProxyHostSelectedLocation(mapLocation);
}

// decompiled/GH.Runtime/UIMapMultiplayerController.cs:759-781
public void ProxyHostSelectedLocation(MapLocation location)
{
    if (!FFSNetwork.IsClient) return;
    UIMultiplayerNotifications.ShowSelectedQuest();
    ...
    hostSelectedLocation = location;
    GuildmasterConfirmAction.ShowQuestSelectedAction(location.LocationQuest, () => PreviewQuest());
}
```

and the client's own quest card is opened by the game too —
`UIGuildmasterConfirmActionButtonPresenter.cs:69` calls
`Singleton<UIQuestPopupManager>.Instance.ShowMultiplayerPreview(quest)`, which drives a *dedicated*
`multiplayerQuestPopup` (`UIQuestPopupManager.cs:15`, `:179-186`).

**→ For the committed selection there is NOTHING TO BUILD, and a second channel is forbidden**
(`.planning/worldmap-3d.md:721-725`: *"The map state itself: nothing new. The game already syncs
it, host-authoritatively … Adding a second channel for the same facts would be a second source of
truth. Do not."*).

**READ — what is NOT on the game's wire:** the host's *staging*. `AdventureMapUIManager.
OnSelectedMapLocation(mapLocation, cb)` sets `locationToTravel` and shows the travel options
locally (quoted in `src/GloomhavenVR/WorldUI/MapRoom/MapTravelConfirm.cs:16-37`, which reproduces
the decompiled source verbatim). Online, `EnableTravelOptions` does
`travelButton.gameObject.SetActive(!FFSNetwork.IsOnline)` (`MapTravelConfirm.cs:35`) — so the host
has no Reisen button online and commits through `UIReadyToggle` instead
(`UIMapMultiplayerController.OnSelectedLocation`, `:182-205`, calls
`MapChoreographer.InitReadyToggle(EReadyUpToggleStates.Quests)` and `ToggleReadyUpUI(show: true, …)`).
Between the host's click and the host's ready-up, **no client knows which icon the host is looking
at.** Highlight is likewise local: `MapLocation.OnPointerEnter → Highlight(active:true, …)`
(`decompiled/GH.Runtime/MapLocation.cs:253-262`, `:540-556`) touches only `MeshParent.localScale`,
the decal material and `UpdateMarkers` — no network call anywhere in that path.

**Which one is he seeing? — THE AMBIGUITY, AND THE FREE ANSWER.** *INFERRED, not measured:* he is
seeing the staging, because in the 3D room the host clicks an icon, the quest window opens for the
host, and the other players' rooms show nothing until the host readies. **The free falsification
(one click, flat game, two clients, no build):** host clicks a quest icon on the campaign map and
*does not* confirm. If the client's screen changes at that moment, the staging is already synced and
2b is fully closed with nothing to build. If it does not change until the host readies up, the
staging is local and needs the record below. **A "nothing changes" result is evidence, not a
non-result.**

**Recommendation, either way:** carry the staged pick as **presentation only**, in the same key
field as the hover (§F, record 20, flag bit `PickStaged`). That is the same class of fact as the
hover placard — "which icon is lit" — and it never touches the commit path, so it cannot become a
second source of truth for *whether* a quest was selected. What it must never do is drive
`Select()` on a receiver.

### 2c. The mouseover placards — **GENUINELY LOCAL**

**READ.** In the 3D room the hover is decided entirely by the mod:

* the hit targets are mod-owned scene-root colliders built to match the *drawn* icon —
  `MapIconHoverPads` (`MapRoom/MapIconHoverPads.cs:33-53`, *"NOTHING IS WRITTEN TO A GAME OBJECT.
  The pads are separate scene-root GameObjects owned by this class"*);
* the arbitration is `MapLocationInteractor.PickFrom` (`MapRoom/MapLocationInteractor.cs:537-597`,
  rule at `:523-526`: *"the nearest DRAWN ICON wins, and an authored hit box only decides when no
  icon is on the ray at all"*), with the hover field `private MapLocation? _hover;` at `:108`;
* the card itself is the **game's own** `UIQuestPreviewPopup`, floated by the conversion layer and
  re-posed every frame by `MapRoom/HoverCardPose.Place(...)` (`HoverCardPose.cs:108-134`, called
  from `ModalFallback.4.Tick.cs:342-352`), with the game's own screen-space follower forcibly
  disabled each frame (`HoverCardPose.cs:195`);
* `MapHoverVerdict` is a pure diagnostic — it logs which gate refused a card and changes nothing
  (`MapHoverVerdict.cs:84-174`).

Nothing on that path sends anything. The apply seam on the receiver is the one the mod already
uses for its *own* hover: `want.OnPointerEnter(null)` at `MapLocationInteractor.cs:663`. (The
game also exposes `MapLocation.ForceHighlight(bool)`, `decompiled/GH.Runtime/MapLocation.cs:293`,
which is the gamepad/quest-tracker seam; **use `OnPointerEnter`/`OnPointerExit` anyway**, because
then a remote hover is *literally* the same code path as a local one and shares its teardown.)

#### THE CRUX — a stable cross-client identity for a map location. **SOLVED, and read from source.**

The mod today has **no** stable identity. Every store is reference-keyed:
`List<MapLocation> _locations` (`MapLocationInteractor.cs:81`),
`Dictionary<Collider, MapLocation> _byCollider` (`MapIconHoverPads.cs:98`), and the interactor's
own comment refuses to rely on order (`MapLocationInteractor.cs:428-430`: *"Set comparison, NOT
index comparison: **neither route guarantees an order**"*). The GameObject name is useless too —
every location is a clone of one prefab (`MapChoreographer.cs:54`, instantiated at `:601, 611,
635, 659, 686, 701`) and `MapLocation.Init` never renames it, so `loc.name` is the *same string*
for every location. **A spawn-order index is NOT an acceptable identity** and must not be used.

But the identity exists, one hop away, and **it is the identity the game itself puts on its own
wire**:

```
decompiled/MapRuleLibrary/MapRuleLibrary.MapState/CLocationState.cs:13
    public string ID { get; private set; }

decompiled/GH.Runtime/MapLocation.cs:181
    public CLocationState Location => m_Location;          // assigned in Init(), :382
```

and the proof that it is cross-client stable is `ProxySelectedLocation` above — the game sends
`new LocationToken(location.Location.ID)` and the receiver resolves it with
`SingleOrDefault(x => x.Location.ID == locationId)`. If that string were not identical on both
machines the vanilla game's own quest selection would be broken.

It travels as an ASCII string: `LocationToken.Write` is
`packet.WriteString(ID, Encoding.ASCII)` (`decompiled/GH.Runtime/LocationToken.cs:19`), and the
same token carries `MoveToNewNode` too (`MapChoreographer.cs:1440-1441`).

There is a sibling for quests: `CQuestState.ID` is likewise a `string`
(`decompiled/MapRuleLibrary/MapRuleLibrary.MapState/CQuestState.cs:54`, sourced from
`MapRuleLibrary.YML.Quest/CQuest.cs:23`), reachable as `MapLocation.LocationQuest`
(`MapLocation.cs:183`). Note the **location id and the quest id are different strings** —
`SelectQuest` carries the *location* id.

**⚠️ DO NOT USE `CMapScenarioState.ScenarioID`.** It is RNG-rolled per playthrough —
`ScenarioID = MapScenario.RollForScenario();`
(`decompiled/MapRuleLibrary/MapRuleLibrary.MapState/CMapScenarioState.cs:433`; `RoadEventID` at
`:463` likewise). Cross-client determinism for those is maintained separately through
`ScenarioGenerationRNGState` (`:51`) and the `RegenerateAllScenarios` action, which is a different
guarantee from "identical right now". Also do not use `MapLocation` instance ids (every location is
a runtime `Instantiate` of one prefab) or `QuestManager.locations` keys (they are `CQuestState`
object references, `decompiled/GH.Runtime/QuestManager.cs:29`).

**Card identity never goes on the wire** — this is not card identity; it is a campaign map node id
from the shared YML, the same class of datum as a wall key (record 17) or an actor id (records
8/16/22/23/27).

**Payload:** do NOT send the string. Send `FNV-1a(Location.ID)` as a `u32`, exactly the shape
`RemoteStorySync.HashDialog` uses (`RemoteStorySync.cs:254-286`), with the same `h == 0 → 1` fold
so 0 stays reserved for "none". A **key is a MATCH GATE, never an instruction**
(`RemoteStorySync.cs:249-252`): the only thing a receiver does with an unresolvable key is
nothing. A collision could at worst light the wrong icon; it can never select anything.

The receiver needs a `u32 → MapLocation` map. `MapLocationInteractor` already rescans on a
cadence and already rebuilds its adapters by set comparison (`:428-430`); the key cache is one
more parallel array rebuilt in the same place. **This is mandatory, not optional:** locations are
destroyed and respawned by `InitMap` (`MapChoreographer.cs:585-596`), so a cached key→object map
that is not rebuilt will point at dead objects.

### 2d. The quest-confirm window and the story windows — **GENUINELY LOCAL, AND A DIFFERENT CONTROLLER**

This is the finding that reshapes the task.

**READ.** The map-phase narrative is **not** `StoryController`. It is `MapStoryController`, a
separate `Singleton` with *its own* `UICharacterStoryBox` and *its own* `UIWindow`:

```csharp
// decompiled/GH.Runtime/MapStoryController.cs:11-45
public class MapStoryController : Singleton<MapStoryController>
{
    public class MapDialogInfo : StoryController.DialogInfo { … }
    [SerializeField] private UICharacterStoryBox dialogBox;
    [SerializeField] private UIWindow window;
    [SerializeField] private List<StoryHide> elementToHide;
    public static bool DisplayDelayInEffect;
    private Queue<MapDialogInfo> m_PendingMessages = new Queue<MapDialogInfo>();
    …
}
```

`ShowImmediately` (`:142-149`) calls `dialogBox.Show(message.DialogPages, OnFinishShow, …)` and
`window.Show(instant: true)` — the identical shape record 19 already drives, on the identical type
(`UICharacterStoryBox`, hence the identical `ShowLine(int)` seam and the identical
`List<DialogLineDTO> dialogs` / `currentDialogIndex` fields).

**Record 19 cannot see it.** `RemoteStorySync.Box()` (`src/GloomhavenVR/Net/RemoteStorySync.cs:290-298`)
reads `Singleton<StoryController>` and nothing else. And `grep -rn "MapStoryController"
src/GloomhavenVR/` returns **zero hits** — the mod has never referenced this class.

**Who raises it, exhaustively** (`grep -rn "MapStoryController>.Instance" decompiled/`): the
Gloomhaven/travel intros and outros (`MapChoreographer.cs:1495, 1809, 1823, 1837`), quest
unlock/completion (`:2531`), achievements (`:855, 2392, 2562`), the temple (`:987`), the generic
map-message pump (`:2587`), plus `UITownRecordsWindow.cs:107`, `UITrainerWindow.cs:97`,
`UILoadoutQuestWindow.cs:93`, `UIRetirementManager.cs:175`, `UIGuildmasterHUD.cs:848`,
`CampaignRewardsManager.cs:260`. The trigger taxonomy is `EMapMessageTrigger` (19 members,
`decompiled/MapRuleLibrary/MapRuleLibrary.YML.Message/EMapMessageTrigger.cs`).

**Record 19 is INERT on the map, and that is why 2d needs its own record rather than a wider
`Box()`.** *INFERRED, strongly:* `Singleton<T>` is not a persistent singleton — it is
`_instance = this as T` in `Awake` and `_instance = null` in `OnDestroy`
(`decompiled/GH.Runtime/Singleton.cs:11-19`), with no `DontDestroyOnLoad`. `StoryController` is
additionally hard-wired to things that do not exist on the map:
`Choreographer.s_Choreographer.AddUpdateBlocker()` (`StoryController.cs:216`),
`LevelEventsController.s_Instance.MessageWasDisplayed/Dismissed` (`:176`, `:206`),
`PhaseManager.PhaseType` / `EGameState.Scenario` (`:125`), and it enters
`PopupStateTag.DialogueMessage` (`:146`) while `MapStoryController` enters
`CampaignMapStateTag.MapStory` (`MapStoryController.cs:84`, `:151`). Two of its consumers already
guard defensively with `!Singleton<StoryController>.IsInitialized`
(`decompiled/GH.Runtime/BaseButtons.cs:68`, `:86`). **What would confirm it outright:** one log
line printing `Singleton<StoryController>.IsInitialized` while the campaign map is open. A `false`
means record 19 cannot see anything on the map; a `true` would mean the two controllers coexist and
`SharedWindows.KindOf` must disambiguate by instance, which it does anyway.

**How much of 2d is a stall fix rather than a comfort feature — the answer is "partly", and it
matters.** Record 19 exists because a peer who walked away from the *scenario* story box **locked
the session**: `StoryController.TryBlockedUpdate` holds `ActionProcessor.LockProcessingAction()` +
`Choreographer.AddUpdateBlocker()` (quoted at `RemoteStorySync.cs:27-35`). `MapStoryController`
itself contains **no** such call — I read all 184 lines; no lock, no update blocker, no
`GameLoadedAndClientReady`. **But its caller does, for at least one trigger. READ:**

```csharp
// decompiled/GH.Runtime/MapChoreographer.cs:1488-1500  CheckCampaignIntro(...)
    if (m_IntroGloomhavenLines.Count > 0 && (…))
    {
        m_QueuedMoveLocation = moveToLocation;
        var message = new MapStoryController.MapDialogInfo(m_IntroGloomhavenLines, null);
        Singleton<MapStoryController>.Instance.Show(EMapMessageTrigger.IntroGloomhaven, message,
                                                    FinishedShowingIntroGloomhavenMessages);
        if (FFSNetwork.IsOnline)
        {
            ActionProcessor.SetState(ActionProcessorStateType.Halted);   // ← the stall
        }
    }
```

and the release is **not** in the finish callback — `FinishedShowingIntroGloomhavenMessages`
(`:1635-1639`) only queues the move message; `SetState(ProcessFreely, MapHQ)` happens later down
that chain (`:2094`, `:2429`, `:3196`). **So a player who leaves the Gloomhaven intro story
standing leaves their own `ActionProcessor` `Halted` and stops processing the shared action
queue** — the same class of stall record 19 was built to remove, on a different controller. Also
`MapChoreographer.cs:1653` and `:2244` halt for two further map flows.

**Therefore: for most triggers 2d is a shared-experience feature (cost of error = annoyance), and
for `EMapMessageTrigger.IntroGloomhaven` in an online session it is also a stall fix (cost of error
= a stuck party).** Either way the 3D-map gate stays an opt-in: a player who wants their own pacing
keeps it by leaving the 3D map off, exactly as the request says — and that player is no worse off
than they are today, because today *every* player has that stall available to them.

`MapStoryController`'s only state write is
`MapChoreographer.OnMapMessageShown(m_CurrentMessage.MapMsg)` (`MapStoryController.cs:174-178` →
`MapChoreographer.cs:2595-2599` → `mapMessageState.MapMessageShown()`), which every client already
performs for itself when it clicks its own copy through — so driving it earlier is the same
operation, at a different moment.

**Two API notes the implementer needs.** (1) The mod **publicizes** the game assemblies —
`<Reference Include="GH.Runtime" … Publicize="true" />`
(`src/GloomhavenVR/GloomhavenVR.csproj:43-48`, via `BepInEx.AssemblyPublicizer.MSBuild`) — so
`MapStoryController.dialogBox`, `MapStoryController.window`, `MapChoreographer.worldMap/cityMap`
and `UICharacterStoryBox.ShowLine(int)` are all reachable as ordinary members with **no
reflection**, exactly as `RemoteStorySync.cs:656` already calls `box.ShowLine(target)` despite
`ShowLine` being `private` in the decompiled source (`UICharacterStoryBox.cs:172`). (2) There is
**no map equivalent of record 19's display-delay guard**: `MapStoryController.DisplayDelayInEffect`
is declared (`MapStoryController.cs:47`) and, by a tree-wide grep, **never read or written
anywhere**. The receiver's remaining guard is the one that matters anyway —
`currentDialogIndex < 0` means the box has not painted its first line yet
(`UICharacterStoryBox.cs:63`, `:123`), and driving a page then would be undone backwards
(`RemoteStorySync.cs:624-634`).

**"das erscheinende Fenster … in der man die Quest bestätigen kann"** is `UIQuestPopup`
(`UIWindowID.QuestPopup`), which the mod already floats and already looks up by id:

```csharp
// src/GloomhavenVR/WorldUI/MapRoom/MapRoomDriver.cs:377
MapTravelConfirm.Reconcile(ModalFallback.FloatedWindowWithId(UIWindowID.QuestPopup));
```

(`FloatedWindowWithId` at `ModalFallback.4.Tick.cs:361-372`.) Its **content** is already synced —
the client's copy is opened by the game's own `ShowMultiplayerPreview` path (2b). Its **pose** in
VR is not. So for the quest window, only the pose needs a record; the confirm click stays on the
game's own host-authoritative path and no mod code may drive it.

**Story identity (`StoryKey`) on the map.** Record 19's `HashDialog` hashes
`pages.Count` then, per page, `DialogLineDTO.text` (which is the *localization key*, not the
translated string — `RemoteStorySync.cs:240-247` citing `DialogLineDTO.cs:25/45`) and
`.character`. `MapStoryController` builds its pages from the same `DialogLineDTO` type through the
same `StoryController.DialogInfo` base (`MapStoryController.cs:15-35`), so **the identical hash
function works unchanged and is per-message distinguishable** — two different map messages have
different localization keys. *INFERRED but strong:* per-quest distinguishability follows because
quest unlock messages carry their quest's own text keys. **What would confirm it:** one log line
printing the computed key next to `EMapMessageTrigger` for two consecutive map messages; two
different keys confirm it, two identical keys mean the hash must also fold in the trigger.

---

## 3. Section B — the 3D-map-only scope

### Was `ExtIdMapRoom` ever built? **NO.**

`grep -rn "ExtIdMapRoom" src/` → nothing. Nothing in `src/GloomhavenVR/Net/` references the map
room at all: no `PresenceState` field, no TLV doc entry, no `Remote*` consumer.
`.planning/worldmap-3d.md:733` specified it (*"One extension record, `ExtIdMapRoom`, payload 1
byte: bit 0 = 'I am in the 3D map room'"*) as **Phase 8**, which is unbuilt — `MapRoomDriver`'s own
class doc says so (`MapRoomDriver.cs:40-42`): *"MULTIPLAYER: phase 1 changes nothing on the wire.
Peers are already visible on the map today, unconditionally, and every client's menu rig sits at
the same authored vantage — so avatars pile up. That is phase 8's problem, deliberately not fixed
here."*

The only map-room thing in `tests/` is `MapRoomSeatVectors.cs` — arithmetic, not a packet test.

### The flag to publish, and the flag to gate on

**READ.** `internal static bool MapRoomDriver.Active { get; private set; }` —
`src/GloomhavenVR/WorldUI/MapRoom/MapRoomDriver.cs:88`, doc at `:86-87`: *"True while the MAP rig
is actually standing. **This — not `Wanted` — is what other subsystems (the flat screen) test**, so
nothing hides before there is a room."* Raised in `Engage` (`:318`), lowered in `StandDown`
(`:415`), ~25 existing consumers. There is a `public` mirror if `Net` prefers not to reach into
`WorldUI.MapRoom`: `Core/Events/VRModeStateMachine.ModRoomStands` (`VRModeStateMachine.cs:116`,
pushed from `MapRoomDriver.cs:332`/`:420`).

`MapRoomDriver.Wanted` (`:84`) is the *predicate* and is true one or more frames before `Active`.
**Publish and gate on `Active`, never `Wanted`** — publishing `Wanted` would tell peers you are in
a room that has not been built yet.

### The three rules, stated precisely

1. **Receiver rule.** A client whose `MapRoomDriver.Active` is false **ignores records 20 and 21
   entirely**: no page is driven, no pose is applied, no hover placard is shown, no surface is
   followed, no peer is drawn in a room it is not rendering. Its picture is byte-for-byte
   ModBuild 219's. The gate lives in the consumers' `Resolve()`, one early return each, so a burst
   of packets costs one comparison — not in `TryRead`, because a parser that discards data cannot
   be tested for what it discarded.

2. **Sender rule.** A client whose `MapRoomDriver.Active` is false **writes neither record**, and
   its `HasMapRoom` / `HasSharedWindow` flags stay false, so the `extensions` tail-open gate
   (`PresenceState.cs:1249-1341`) is unmoved and **the idle packet is byte-identical to ModBuild
   219's**. This project treats that as a hard property and it is the reason the two records carry
   their own sampler-side emptiness test (the record-32 / record-19 rule,
   `PresenceState.cs:1325-1341`).

3. **Local settings take precedence; the flag REPORTS, it never INSTRUCTS.** Nothing arriving on
   the wire may switch anybody's 3D map on, and nothing may advance a story for a player who opted
   out. `.planning/worldmap-3d.md:777-781` states it: *"a player with the feature off is never
   corrected. Nothing arriving on the wire may switch the 3D map on for someone who switched it
   off; the flag is reporting, never instructing."*
   **One deliberate exception, and it must be named as such:** the *surface* field (2a) and the
   *page* field (2d) **do** instruct — that is what the user asked for — but only within the set of
   clients that have already opted in by switching the 3D map on. Opting in is the consent. Write
   that sentence into the record's doc comment, because the next reader will otherwise think rule 3
   has been violated.

---

## 4. Section C — the shared-window predicate

### The definition

> **A floated window is SHARED when its on-screen state is driven from the wire for this client
> right now** — i.e. when some record makes another player's click or drag change what this
> client sees in that window.

That is deliberately *this client, right now*, not *this window class, in principle*. It is the
only definition under which the blue bar is a true statement to the person looking at it.

### The current members of the set

| kind | window | how it is identified | which record |
|---|---|---|---|
| `ScenarioStory` | `StoryController.window` | instance compare against the singleton — the enum has **no** Story member (`ModalFallback.3.WindowPanel.cs:195-198`) | **19, already shipped** |
| `MapStory` | `MapStoryController.window` | instance compare against the singleton — reachable directly, the assemblies are publicized (`GloomhavenVR.csproj:43`) | **21, new** |
| `QuestConfirm` | `UIWindowID.QuestPopup` | `window.ID == UIWindowID.QuestPopup` | **21, new** (pose only) |

Not members, and each for a stated reason:
* **The road/city event panel (`UIEventPanel`)** — **already synced by the game.**
  `Synchronizer.SendGameAction(GameActionType.ContinueRoadEvent, ActionPhaseType.MapEvent, …)` at
  `decompiled/GH.Runtime/UIEventPanel.cs:606`, `:610`, `:724`, receiver
  `UIEventPanel.ClientContinueRoadEvent(GameAction)` at `:869` (dispatch entry
  `FFSNet/GameAction.cs:189-194`), carrying a `RoadEventToken` with `EventID` +
  `CurrentScreenName` (`FFSNet/Synchronizer.cs:126-131`). It looks like a story window and it is
  not one: its page advance is a **game action**. Touching it would be the forbidden second
  channel. *It is listed here because it is the single most likely mistake in lane C.*
* **The guildmaster destination windows** (merchant, temple, trainer, town records) — the user's
  own §0 ruling for the map room: *"Da jeder seine eigene UI sieht, sollen diese UI Element nicht
  synchronisiert werden"* (`.planning/worldmap-3d.md:18-19`).
* **The hover card** — it has no grab bar and no X at all (`ModalFallback.8.Convert.cs:315`),
  so there is nothing to tint, and its pose is owned externally every frame
  (`ModalFallback.9.Spawn.cs:1489`).
* **The scenario decision/results windows** — records 12/24/29 are cosmetic *mirrors* of a peer's
  own board, not a shared object; nobody else's drag moves your copy.

### Where it lives, and its exact signature

**New file, landed by the integrator as the shared contract before any lane starts:**
`src/GloomhavenVR/WorldUI/SharedWindows.cs`

```csharp
namespace GloomhavenVR.WorldUI;

/// <summary>Which floated windows every player in the same room sees the same state of.</summary>
internal enum SharedWindowKind : byte
{
    None          = 0,
    ScenarioStory = 1,   // StoryController.window        — wire record 19 (shipped, untouched)
    MapStory      = 2,   // MapStoryController.window     — wire record 21, entry kind 1
    QuestConfirm  = 3,   // UIWindowID.QuestPopup         — wire record 21, entry kind 2 (pose only)
}

internal static class SharedWindows
{
    /// <summary>What kind of shared window this is, INDEPENDENT of whether this client currently
    /// participates. A pure lookup against three singletons and one UIWindowID — it never
    /// converts, places or releases anything, so the predicate cannot change which windows float.</summary>
    internal static SharedWindowKind KindOf(UIWindow? window);

    /// <summary>True when THIS client is a participant in that kind's sync right now. False for
    /// every map-phase kind while MapRoomDriver.Active is false — the user's ruling: the sync is
    /// only for players who have the 3D world map switched ON.</summary>
    internal static bool ParticipatesHere(SharedWindowKind kind);

    /// <summary>THE PREDICATE both lanes call.
    /// == KindOf(window) != None &amp;&amp; ParticipatesHere(KindOf(window)).</summary>
    internal static bool IsShared(UIWindow? window);

    /// <summary>The grab frame of the floated window of that kind, or false when it is not
    /// converted / still behind the reveal gate / not open. The generalisation of
    /// ModalFallback.TryGetStoryGrab, which becomes one case of it.</summary>
    internal static bool TryGetGrab(SharedWindowKind kind, out GrabbableModal? grab);

    /// <summary>The grab-bar tint a shared window's bar carries. Private windows keep brass.</summary>
    internal static Color BarTint { get; }
}
```

**Why `WorldUI` and not `Net`:** the colour lane must call `IsShared` from
`ModalFallback`/`GrabbableModal`, and the chrome must not take a dependency on `Net`. The
dependency direction that already exists is `Net → WorldUI` (`RemoteStorySync.cs:360` calls
`ModalFallback.TryGetStoryGrab`), and this keeps it.

**`TryGetGrab` supersedes `ModalFallback.TryGetStoryGrab`** (`ModalFallback.3.WindowPanel.cs:204-221`),
which is currently the *only* accessor of that shape in the repo. Keep the old method as a
one-line forwarder so `RemoteStorySync.cs:360/384/711` and `ModalFallback.9.Spawn.cs:1482` are
untouched by the contract commit.

### "Shared" for a window synced only among 3D-map players — the colour question

**Recommendation: NO. A player whose 3D map is off must see the ordinary brass bar on the map
story window and the quest window.**

The argument is the definition above: the bar's job is to tell *this* player what moving it will
do. For a map-off player, moving that window moves nothing for anybody and nobody else's drag
moves theirs — the window is, for them, exactly as private as the merchant. A blue bar would be a
false statement, and the first time they moved it and nothing happened elsewhere they would report
it as a bug.

**The other reading, stated so a future round does not re-litigate it:** one could argue the colour
describes the window *class* ("this is the kind of window that is shared in this game"), so it
should be blue for everybody. Rejected, because it makes the colour un-actionable and because the
same window is genuinely private for that player.

**Consequence that must be implemented:** because participation can flip mid-life (the player
toggles `[Rig] Experimental3DMap` while a window stands — `MapRoomDriver.cs:126` reads the config
live, so it takes effect next frame), **the tint must be re-evaluated per tick, change-gated**, not
written once at build. One `Color` comparison per floated window per frame.

The scenario story window is blue for **everybody** (record 19 has no opt-in gate and must not
acquire one — the user said the scenario case is already done).

---

## 5. Section D — the grab-bar colour

### Where the bar is built and coloured

**READ.** One method, one line:

```csharp
// src/GloomhavenVR/WorldUI/GrabbableModal.cs:491  EnsureFrame()
// :505
    var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
// :518
    bar.transform.localScale = new Vector3(0.2f, BarThickness, BarThickness);
// :519
    var mr = bar.GetComponent<MeshRenderer>();
// :525-528  ← THE COLOUR LINE
    Material barMat = WorldUIAssets.CreateFlatMaterial(new Color(0.62f, 0.5f, 0.28f), overlay: true);
    if (barMat.HasProperty("_ZWrite"))
        barMat.SetInt("_ZWrite", 1);
    mr.sharedMaterial = barMat;
```

It is a **`MeshRenderer` on a primitive cube**, not a uGUI `Image` and not a sprite. **The material
is per-instance**: `WorldUIAssets.CreateFlatMaterial` does `new Material(shader) { color = color }`
(`WorldUIAssets.cs:48`), so tinting one bar cannot tint another. `MaterialPropertyBlock` is not
involved anywhere on this path and must not be introduced — the standing ruling is quoted at
`NetProtocol.cs:4765` (*"`MaterialPropertyBlock` CANNOT set or clear a shader keyword"*), and the
endorsed pattern for exactly this case is stated at `MapRoom/MapButtonRail.cs:544`.

### What determines the colour today — exactly two states

```csharp
// src/GloomhavenVR/WorldUI/PanelGrab.cs:224   (PanelGrabHandle.Init snapshots the base)
    _barBaseColor = bar.sharedMaterial != null ? bar.sharedMaterial.color : Color.white;

// src/GloomhavenVR/WorldUI/PanelGrab.cs:309-313
    public void OnGrabHighlight(VRHand hand, bool highlighted)
    {
        if (_bar != null && _bar.sharedMaterial != null)
            _bar.sharedMaterial.color = highlighted ? new Color(0.95f, 0.8f, 0.4f) : _barBaseColor;
    }
```

* **idle/base** — brass `(0.62, 0.5, 0.28)`, written once at build.
* **hovered/held** — `(0.95, 0.8, 0.4)`. There is **no separate held colour** (a held bar stays
  highlighted because `IGrabHighlight` stays on) and **no disabled colour** — "disabled" is
  expressed as *invisible* via `IPanelGrabOwner.GrabVisible` (`GrabbableModal.cs:208-210`) plus the
  render-root hide (`GrabbableModal.cs:553`).
* **no config entry** anywhere: `ConfigCatalog`/`ConfigSteps` have no bar-colour key. The brass
  literal appears in three files (`GrabbableModal.cs:525`, `Surfaces/CombatLogSurface.cs:437-444`,
  `Cards/PlayTray.1.Core.cs:870-875`).

### The smallest change

**Two writers must agree, and this is the trap.** Writing only the material is undone by the next
hover-off, which restores `_barBaseColor` (`PanelGrab.cs:312`). So:

1. **`src/GloomhavenVR/WorldUI/PanelGrab.cs`, next to `Init` (~`:224`)** — add
   ```csharp
   internal void SetBarBaseColor(Color c)
   {
       _barBaseColor = c;
       if (!_highlighted && _bar != null && _bar.sharedMaterial != null)
           _bar.sharedMaterial.color = c;
   }
   ```
   (the handle already knows whether it is highlighted; if it does not track it explicitly, latch
   the flag in `OnGrabHighlight`.)
2. **`src/GloomhavenVR/WorldUI/GrabbableModal.cs`** — add
   ```csharp
   internal void SetBarTint(Color c)   // forwards to _handle.SetBarBaseColor and the material
   ```
   and call it, change-gated against a cached last-written colour, from `GrabbableModal.Tick`
   (already invoked per frame from `ModalFallback.4.Tick.cs:2320-2324`) with
   `SharedWindows.IsShared(window) ? SharedWindows.BarTint : BrassDefault`.

The `overlay: true` + `_ZWrite = 1` at `:525-527` must be preserved verbatim — that is what makes
the bar read solid over the depthless menu canvas (`GrabbableModal.cs:74-89`, user item 3).

Hover and held are untouched: the highlight colour is unchanged and still wins while the hand is
near, so a shared window still lights up like every other one and the blue is what it returns to.

**Colour value.** The user wrote "zB Blau". A first proposal to be judged on hardware:
`(0.24, 0.45, 0.80)` — the same value/saturation register as the brass so the two read as one
family under the room's lighting, and far enough from the highlight `(0.95, 0.8, 0.4)` that
hovered-vs-idle is still unambiguous. This is a **look** decision; ship it, photograph it, let him
retune. Do **not** add a config key for it (settings configure optional content and comfort only).

---

## 6. Section E — window position sync

### What identifies a window across clients

`SharedWindowKind` — a single byte. There are three of them and there will not be many more. The
alternative (a hash of the window's name or `UIWindowID`) is worse: `UIWindowID` has **no** Story
member (`ModalFallback.3.WindowPanel.cs:196-198`), and the GameObject name is a per-build detail.
An enum makes the wire self-describing and makes an unknown kind trivially skippable.

Within a kind, the **content key** (`u32`) is the match gate: for `MapStory` the dialog hash, for
`QuestConfirm` the FNV-1a of the location id the popup is showing. A receiver whose own window
holds different content ignores the entry, which is record 19's discipline verbatim
(`RemoteStorySync.cs:249-252`).

### Authority model: last-mover-wins, by a wrapping stamp

Copy record 19 exactly — it is shipped, it is pinned by 64 assertions, and its reasoning is
written down (`NetProtocol.cs:11045-11048`): *"poseStamp — a wrapping counter the sender bumps once
per COMPLETED local move/resize. It is the last-mover arbitration, not a clock … Ties cannot
deadlock — a stamp that never changes simply never wins."*

**Two players grab the same window at once.** Already answered, and the answer must be carried
over:

* `RemoteStorySync.ResolvePose` returns immediately `if (_localMoving)`
  (`RemoteStorySync.cs:679-680`) — *"a hand owns it right now; never fight a hand."*
* `TrackFrame` clears `_followingPeer` the moment local motion is detected
  (`RemoteStorySync.cs:414-418`) — *"Never yank a panel out of a hand, and never fight a hand at
  5 Hz."*
* The stamp bumps only after `MoveSettleSeconds = 0.25f` of stillness (`:116`, `:420-424`), so a
  drag is one stamp, not one per frame.

So while both hands are on it, both players see their own window follow their own hand; when the
first lets go their stamp lands, and when the second lets go theirs lands later and wins. Both
converge on the second releaser's pose within one packet (≤ 200 ms). **State the residual
honestly:** for the duration of a simultaneous two-handed tug, the two clients disagree. There is
no fix for that which does not introduce a lock, and a lock on a window pose is worse than a
disagreement that lasts as long as two people are both holding the thing.

### DO NOT WIN A WRITE WAR — and there IS one waiting

**READ, and this is the single most important line in the whole feature.**
`PanelPoseWatch` actively fights unattributed pose writes:

```csharp
// src/GloomhavenVR/WorldUI/PanelPlacement.cs:631-637
    bool restorable = grab != null && !entry.Conceded && entry.Corrections < MaxCorrections;
    if (restorable)
    {
        entry.Corrections++;
        grab!.PlaceFrameAt(entry.LockedPos, entry.LockedRot);
    }
```

with `MaxCorrections = 8` (`PanelPlacement.cs:202`) and the ruling it enforces quoted at
`PanelPlacement.cs:135-151` (ModBuild 193/197). A remote write is spared **only** for the story
window, because `ModalFallback.9.Spawn.cs:1490` computes

```csharp
peerOwned: wp.Grab != null && ReferenceEquals(wp.Grab, storyGrab)
```

from `TryGetStoryGrab` at `:1482`, and the `Peer` verdict re-baselines instead of reverting
(`PanelPlacement.cs:229-233`, handled `:523-542`).

**Any newly shared window would be classified `Unattributed`, put back eight times, and only then
conceded.** The symptom on hardware would be "the window snaps back a few times and then works",
which reads exactly like network jitter and would cost a build round to diagnose.

**The fix is one line:** `peerOwned:` becomes `SharedWindows.IsShared(wp.Window)`. It belongs in
the integrator's contract commit, not in a lane.

**Is there any other per-frame pose writer?** No. `GrabbableModal.Tick` /
`LateSyncHost` (`GrabbableModal.cs:335-339`, `:360-403`, `:417-422`) copy the *frame* to the
*host* every frame in both Update and LateUpdate — that is a mirror, not a placement, and it is
precisely why a remote write must go to `GrabbableModal.PlaceFrameAt` (`:181-188`) and never to
the host (`PanelPlacement.cs:398-400`). The one genuine per-frame pose writer, the map room's
hover cards (`ModalFallback.4.Tick.cs:342-352`), is already exempted by
`poseOwnedExternally` (`ModalFallback.9.Spawn.cs:1489`) and is not a shared window.

### Reconciling "windows must not move once spawned" (193) and "in view wins" (193)

The ModBuild 193 ruling, verbatim (`PanelPlacement.cs:138-141`):

> *"Die Fenster verändern ständig ihre Position wenn ein neues Fenster gespawned wird oder
> schließt. Das soll nicht sein — **ohne explizite Bewegung vom User**, sollen sie ihre Position
> nicht verändern."*

The operative words are **"ohne explizite Bewegung vom User"**. What 193 forbids is *the mod*
re-arranging windows on its own — the deleted `RelayoutMapRoomArc` (`ModalFallback.4.Tick.cs:399-406`).
**A remote player deliberately dragging a shared window IS an explicit user movement**; it is
simply a different user's. The two rulings coexist without amendment, and the sentence to write
into the code is:

> *A shared window's pose changes for exactly two reasons: a hand on this client, or a hand on a
> peer's client. Neither is the mod re-arranging anything, which is what ModBuild 193 forbade.*

There is, however, a comment that must be **corrected** rather than merely coexisted with:
`ModalFallback.4.Tick.cs:444-447` currently reads *"MULTIPLAYER: nothing here goes on the wire,
and nothing may. Which windows a client has open, and where in ITS room they stand, is local
presentation."* That remains true of **slot allocation** and of every private window, and false of
the shared set. Amend it in place; do not delete it (the slot-reservation ruling it protects is
still live).

**"In view wins over non-overlap"** (`ModalFallback.4.Tick.cs:496-511`) is a **spawn-time** rule —
the packer runs once per spawn, never per frame (`ModalFallback.9.Spawn.cs:874-877`,
`ModalFallback.4.Tick.cs:1091`). **Recommendation: a remote pose is NOT clamped into the local
cone.** Clamping would silently break *"voll synchronsiert … auch wenn es jemand woanders
hinverschiebt"* — the whole point is that the window is where the other player put it. Instead:
apply it verbatim and **log once** when the applied pose lands outside the local usable cone
(`UsableHalfConeDeg` already exists, `ModalFallback.4.Tick.cs:473-479`), so a "I can't see the
window" report arrives with its own evidence attached. The recovery is the one the user already
has: grab it, which moves it for everyone.
*The other reading, stated:* clamp into the cone and accept that two clients then genuinely see it
in different places. Rejected — it contradicts the request's own words.

### The frame the pose travels in — **record 19's frame does NOT survive the map room**

**READ — what record 19 does.** `RemoteStorySync.TryToAnchor` / `TryToWorld`
(`RemoteStorySync.cs:464-491`):

```csharp
localPos = Quaternion.Inverse(yaw) * (worldPos - anchor) / scale;   // anchor,yaw = PanelLayout.TryGetAnchor
localRot = Quaternion.Inverse(yaw) * worldRot;                      // scale     = PanelLayout.WorldScale
```

with the measured justification at `NetProtocol.cs:11053-11062`: in the 2026-08-15 session the same
story box stood at world `(15.55, 6.59, −2.65)` scale `0.008` on the host and `(1.44, 2.37, −0.34)`
scale `0.001` on peer 1. In the **scenario** that is the right answer, because players sit at
different points of the shared world **and at different zooms**.

**READ — why the map room is different, in both directions.**

* `PanelLayout.WorldScale` is `RigRoot.lossyScale.x` (`PanelLayout.cs:85-92`), and the map rig
  writes `_rigRoot.transform.localScale = Vector3.one * seat.Scale` with
  `BaseWorldScale = seat.Scale` (`Rig/VRRigDriver.MapRig.cs:67`, `:96`). And
  ```csharp
  // src/GloomhavenVR/WorldUI/MapRoom/MapRoomSeat.cs:38, :174-176
  internal const float TargetMapWidthMeters = 1.20f;
  float rawScale = widest / TargetMapWidthMeters;
  float scale = Mathf.Clamp(rawScale, MinScale, MaxScale);      // MinScale 1, MaxScale 2000 (:70,:73)
  ```
  `widest` is the parchment renderer's larger horizontal world extent, and `TargetMapWidthMeters` is
  a **compile-time constant, not a config entry**. **So in the map room every client of the same
  build derives the SAME rig scale from the SAME shared parchment.** The scale-divergence that
  forced record 19's frame does not exist here.
* But `PanelLayout.TryGetAnchor` (`PanelLayout.cs:100-126`) returns
  `position = CameraController.s_CameraController.FocusPoint` — the game orbit camera's focal point
  (`decompiled/GH.Runtime/CameraController.cs:218`, `FocusPoint => m_FocalPoint`) — and
  `yaw` = the cached rig yaw, which in the map room is the *seat* yaw solved per client from that
  client's own map camera direction (`MapRoomDriver.TrySolveSeat`, `:226-250`). **Both are
  per-client.** Each player pans their own map before entering, and the seat side is solved from
  their own camera. *INFERRED, and this is the one inference in §E that matters:* two clients in
  the map room have **different** `FocusPoint` values and possibly different seat yaws.

So: record 19's frame in the map room is still *arithmetically* sound (encode and decode both use
local values) but it no longer means "the same place" — it means "the same offset from wherever I
happen to be looking". That is not what *"auch wenn es jemand woanders hinverschiebt"* asks for.

**Recommendation: a second, explicitly-tagged frame — PARCHMENT-LOCAL — and a `frame` byte on the
wire so the numbers say which frame they are in.**

```
frame 0 = SEAT-ANCHOR REAL METRES   (record 19's frame, unchanged; used for ScenarioStory)
frame 1 = PARCHMENT-LOCAL REAL METRES (map room)
              origin = MapRoomDriver.ParchmentRenderer.bounds.center     (world)
              axes   = world axes (no rotation applied)
              unit   = real metres: divide by MapRoomSeat scale s = clamp(widest / 1.20f, 1, 2000)
    encode:  local    = (worldPos - center) / s ;   localRot = worldRot
    decode:  worldPos = center + local * s      ;   worldRot = localRot
```

Both `center` and `s` are pure functions of the same parchment bounds, so both clients compute
them identically. Rotation travels as an absolute world rotation because world axes are already
shared (`Net/IBoardAnchor.cs:5-16`: *"The correct shared frame is world space"* — the mod's avatar
poses have always travelled that way). `MapRoomDriver.ParchmentRenderer` is already exposed
(`MapRoomDriver.cs:94`).

Use `BaseWorldScale`, **not** the live `PanelLayout.WorldScale`, for `s` — the live value includes
the player's own pinch-zoom (`MapLocationInteractor.cs:142-144` reads the live one on purpose for
its hover lift), and a shared frame must not move when one player zooms.

**A receiver that does not recognise the `frame` byte keeps its own placement** and applies the
rest of the entry. That is the fail-closed direction: a window in the wrong place is worse than a
window where you left it.

**The honest cost of frame 1, stated:** a window a peer parks on their side of the table will be on
their side of the table for you too — possibly behind you. That is what a physical object does, it
is what the request describes, and the remedy is one grab. **The alternative reading** — "same
placement relative to each player", i.e. keep frame 0 everywhere — is defensible and is what record
19 already ships; it guarantees reachability and loses shared-object semantics. If the user, shown
both, prefers frame 0, the change is one constant and no wire change.

**The free measurement that decides whether frame 1 is even possible:** the map-room report line
already prints the derived scale (`MapRoomDriver.cs:467-471`). Take two clients into the 3D map
room and compare that number in the two `Player.log`s. **Identical ⇒ the parchment frame is shared
and frame 1 is safe. Different ⇒ the parchment bounds are not identical across clients, frame 1 is
dead, and §E must be re-made on frame 0.** That costs one two-player session with no new build.

---

## 7. Section F — the wire contract

### 7.1 Record ids — the claim, justified

**READ — the most recent claim note** (`src/GloomhavenVR/Net/NetProtocol.cs:8186-8189`):

```
// ---- record 32: DEBUG TEST-TRIGGER OVERRIDE ------------------------------------------------
// RECORD-ID CLAIM, 2026-08-15: this change takes id 32 — the first of the free range the
// record-31 note left open. Ids in use are now 1..17, 22..32; 18..21 stay reserved for the
// parallel round that claimed them; 33+ are free. No existing record was widened for it.
```

That note is stale in one direction: ids **18** (`ExtIdSlotOrder`, `NetProtocol.cs:8942`) and
**19** (`ExtIdStorySync`, `:11069`) were later taken out of the reserved block, and record 19's own
header (`:11006-11008`) does not restate the census. **The true census at HEAD: 1..19 and 22..32
are in use; 20 and 21 are free (still carrying the parallel-work reservation); 33..255 are
unclaimed.** No id has ever been retired.

**READ — why the reservation exists** (`NetProtocol.cs:9096-9114`, the record-22 header):

> *"Ids 18..21 are DELIBERATELY SKIPPED here: they are reserved for records developed in parallel
> with this one (a record id, once shipped, can never be renumbered, so two workers must not both
> take 'the next free id')… THE RESERVATION ABOVE EARNED ITSELF: both of those records were written
> in parallel and BOTH authors independently took 'the next free id', 23. Caught at merge and 24
> renumbered before either shipped… **If you are about to claim an id while other work is in
> flight, take one of 18..21 or say in your report which you took.**"*

**This work will itself be split into parallel lanes, so it takes exactly the two ids the
reservation exists for: 20 and 21.** The claim note in the commit must say so, and — because that
empties the reservation — it must **open a new one: 33..36 are reserved for the next parallel
round; 37+ are free.**

Also: **`.planning/STATE.md` §1.3 must be corrected in the same commit** (it currently claims
"Records 1–17 and 22–29 are used; 30+ free"). A stale census is how the 23/24 collision happened.

### 7.2 Record 20 — `ExtIdMapRoom`

**Meaning.** One per sender, FULL STATE while the sender's 3D map room stands, ABSENT otherwise.
Answers, in one 5-byte payload: am I in the room, am I the host, which map surface am I showing,
and which location am I pointing at (or staging).

**Why one record and not three.** All three facts share exactly one lifetime — they exist while and
only while `MapRoomDriver.Active` — and all three are meaningless without the room bit. One record
means one TLV header instead of three (saving 4 bytes per packet), one absence contract instead of
three, and it makes it structurally impossible for a receiver to act on a hover from a peer who is
not in the room.

**Byte layout — payload 5 bytes, 7 on the wire (`[id][len]` + 5):**

| off | size | field | meaning |
|---|---|---|---|
| 0 | 1 | `flags` | bit0 `MapRoomInRoomBit` — the 3D map room stands on this client<br>bit1 `MapRoomHostBit` — sender reports `FFSNetwork.IsHost`<br>bit2 `MapRoomSurfaceKnownBit` — bit3 is meaningful<br>bit3 `MapRoomSurfaceCityBit` — 1 = city map, 0 = world map<br>bit4 `MapRoomPickValidBit` — bytes 1..4 name a location<br>bit5 `MapRoomPickStagedBit` — the pick is a STAGED selection, not a hover<br>bits 6–7 reserved, masked on write **and** on read (`MapRoomDefinedMask = 0x3F`) |
| 1..4 | 4 | `u32 pickKey` LE | `FNV-1a(MapLocation.Location.ID)`, folded so 0 is reserved for "none" |

`MapRoomRecordBytes = 5`.

**Rate and pre-empt.** Rides the ordinary `ExtrasSendRateHz = 5f` (`NetProtocol.cs:50-52`). Three
edges **pre-empt the rate gate outright**, following the founding pattern at
`NetAvatarDriver.cs:832-836` ("a queued event therefore PRE-EMPTS the rate gate and goes out on the
frame it happened"): the room bit changing, the surface changing, and the staged-pick changing. All
three are discrete human acts whose whole purpose is to be looked at. The **hover** key change is
also an edge but can be produced at 90 Hz by sweeping the beam across a row of icons, so it uses
the *capped* idiom instead — `_extrasAccumulator >= fastInterval` (`NetAvatarDriver.cs:945` and the
nine existing sites), i.e. up to the rig rate and no faster. That distinction is the established one
(`NetAvatarDriver.cs:1276-1282`, `:1306-1313`).

**Receiver rules.**
* Mask `flags` with `MapRoomDefinedMask` (the board-UI overlay discipline,
  `PresenceState.cs:2493-2498`).
* `MapRoomInRoomBit` clear, or the record absent ⇒ **that peer is not in the room**: no hover, no
  surface authority, not drawn in the room. Identical to the pre-record picture.
* `pickKey == 0` or unresolvable against this client's live locations ⇒ ignore the pick. Log once
  per changed key at most (the `NoteIgnored` throttle pattern, `RemoteStorySync.cs:764-776`).
* Surface: adopt only from the peer whose `MapRoomHostBit` is set, **and only when it differs from
  this client's own live `MapIconLayer.CurrentSurface`** — never when that reads `Unknown`, because
  a transition is then in flight (`MapIconLayer.cs:666-671`). Retry the press at most once per
  second and give up after 5 attempts with a stated reason: **the game legitimately refuses** when
  the mode is unavailable (`UIGuildmasterHUD.IsAvailable(EGuildmasterMode)`,
  `decompiled/GH.Runtime/UIGuildmasterHUD.cs:753`; the city map is campaign-only,
  `MapChoreographer.cs:3740-3742`), and a per-frame press would be a press storm. The change gate
  is also what keeps the record from fighting the three synced actions that already move the
  surface on their own (§2a).
* Staleness: reuse `PeerStaleSeconds = 3f` (`RemoteStorySync.cs:74`) — fifteen missed 5 Hz packets.

**Surface authority — HOST, and this is a user ruling, not a technical one.** Recommend
host-authoritative because it cannot oscillate: there is exactly one host, so *"everyone in the
room shows what the host shows"* converges in one step and there is no write war with anybody's
hand. A symmetric last-mover election on a **binary** value provably can swap (A adopts B's edge
while B adopts A's), which the continuous pose case tolerates and a binary toggle does not.
Consequence: a non-host in the 3D room must have its world/city rail caps **disabled** rather than
fought — *"concede the flag, own the number"*. `MapButtonRail` already mirrors the game's own
interactability, so this is the same mechanism, and it is the same truth the flat game shows for
quest selection.
**The alternative, for the user to rule on:** anybody may switch, adopt-on-edge only (a peer's
change is applied once when their stamp changes, never continuously), which costs one extra
`surfaceStamp` byte and carries the swap hazard above. **Ask before building.**

### 7.3 Record 21 — `ExtIdSharedWindow`

**Meaning.** The map room's shared windows, full state while they stand and absent otherwise: which
absolute page the map story box is on, and where each shared window stands.

**Why not extend record 19.** Record 19 is shipped, is pinned by 64 golden assertions, and its
layout has no spare flag bit that an old reader would ignore safely — an old reader masks unknown
bits off with `StoryDefinedMask` and would then read a *map* story record as a *scenario* story
record. A separate id is skipped by length by every old build with zero interpretation risk
(`PresenceState.cs:2605-2621`, *"THE SKIP IS THE POINT"*), which is the safest available path and
the one this codebase always takes.

**Byte layout — `[n]` then `n` entries, `n ≤ 2`:**

```
[0]      n                      entry count, 0..2 (clamped on read; >2 stops parsing)
then per entry:
  [+0]   kind                   1 = MapStory, 2 = QuestConfirm; 0 and 3..255 unknown → entry skipped
  [+1]   flags                  bit0 SharedOpenBit    — the window stands on the sender right now
                                bit1 SharedPoseBit    — a pose block follows this entry's head
                                bit2 SharedFinishedBit— sender clicked THROUGH the last page
                                bits3-7 reserved, masked on write AND read (SharedDefinedMask = 0x07)
  [+2]   page                   ABSOLUTE 0-based page, or StoryPageNone (0xFF). Always 0xFF for kind 2.
  [+3]   pageCount              sender's own page count; diagnostic only, receiver clamps to its own
  [+4..7] u32 contentKey LE     kind 1: FNV-1a over the dialog (RemoteStorySync.HashDialog, unchanged)
                                kind 2: FNV-1a(Location.ID) of the quest the popup is showing
  === 8 bytes; then, only when SharedPoseBit is set:
  [+8]   poseStamp              wrapping, bumped once per COMPLETED local move/resize
  [+9]   sizeCode               grab factor × 100, clamped to [15, 200] (PanelGrabHandle Min/MaxScale)
  [+10]  frame                  0 = seat-anchor real metres, 1 = parchment-local real metres
  [+11..30] pose (20 B)         AvatarSerializer.WritePoseShared — 3×f32 pos + 4×i16 quat @ 32767
  === 31 bytes
```

`SharedWindowEntryMinBytes = 8`, `SharedWindowEntryBytesWithPose = 31`,
`SharedWindowMaxEntries = 2`, `SharedWindowMaxRecordBytes = 1 + 2 * 31 = 63`.

**Why a `frame` byte and not a doc sentence.** Record 19 shipped with an implicit frame, and the
map room already breaks it (§6). One byte that says which frame the numbers are in is cheaper than
the next round's ambiguity, and it lets an unknown frame fail closed to "keep my own placement".

**Why `n ≤ 2`.** The shared set in the map room has exactly two members that can stand at once (the
map story window and the quest window; `MapStoryController.ShowImmediately` calls
`ShowOtherGUI(!message.HideOtherGUI)` at `MapStoryController.cs:145`, and `hideOtherUI: false` is
used at `MapChoreographer.cs:855, 987, 2392`, so they genuinely can overlap). Two is the honest
worst case; a third kind would raise the cap in its own commit.

**Rate and pre-empt.** 5 Hz. **The page and the FINISHED bit pre-empt the rate gate outright.**
Note that record 19 does *not* — `RemoteStorySync.Sample` is called at `NetAvatarDriver.cs:1578`,
i.e. **after** the early-return gate at `:1508-1528`, and no `storyChanged` term appears in it. For
a lock-releasing statement 200 ms of latency was acceptable; for *"Klickt einer weiter ist es für
alle … weitergeklickt worden"* the latency is what the feature is judged on, so the new record
pre-empts. Say so in the record's doc, and note record 19's omission there rather than silently
diverging. The **pose** uses the capped idiom (`>= fastInterval`), like every other dragged thing.

**Receiver rules — sanitise field by field, never drop the record whole**
(`PresenceState.cs:3331-3351` is the model):
* `n` clamped to `SharedWindowMaxEntries`; a truncated entry ends the parse and keeps the entries
  before it.
* unknown `kind` ⇒ that entry is skipped by its own computed length (which is known from its flags
  byte); the rest of the record still applies.
* `flags` masked with `SharedDefinedMask`.
* `page > StoryPageMax` ⇒ `StoryPageNone`; the receiver then re-clamps against **its own**
  `dialogs.Count` via the existing `NetProtocol.ResolveStoryPage` (`NetProtocol.cs:11188-11199`),
  which is reused verbatim — its three properties (idempotence, ordering, no skipping) are exactly
  what is needed and are already test-pinned.
* `sizeCode` outside `[StorySizeMinCode, StorySizeMaxCode]` ⇒ `StorySizeDefaultCode` (100).
* NaN/Inf in the pose position ⇒ drop the pose block, keep the page (record 19's rule,
  `PresenceState.cs:3345-3347`: *"a window flung to infinity is unreachable and the local placement
  is always usable"*).
* unknown `frame` ⇒ drop the pose block, keep the page.
* `contentKey` is **not** validated — it is an opaque match gate whose whole job is to fail to
  match (`PresenceState.cs:3348-3351`).

**Absent = fail closed to the pre-record picture.** No record 21 from a peer ⇒ that peer has no
shared map window ⇒ nothing is driven and the local placement stands. That is precisely what a peer
on ModBuild 219 gives us, so an old peer degrades to "not participating" and never to a clamped
extreme.

**The 3D-map gate is on BOTH sides** (§B): written only while `MapRoomDriver.Active`, applied only
while `MapRoomDriver.Active`.

### 7.4 `MaxSize` delta

**READ.** The current sum is **1357** with `MaxSize = 1600`, margin **243**
(`PresenceState.cs:1113-1152`, `:1220`), and the rule (`:1216-1219`): *"every new record adds its
worst case to the sum above IN ITS OWN COMMIT, and keeps a margin of at least one record's
worth."*

```
+ 7  (MAP ROOM:      2 + NetProtocol.MapRoomRecordBytes 5)
+ 65 (SHARED WINDOW: 2 + NetProtocol.SharedWindowMaxRecordBytes 63)
1357 → 1429
```

Margin at 1600 would be **171**, which is **less than the largest single record (257, board
tuning)** and therefore violates the stated rule. **So `MaxSize` must go 1600 → 1800 in the same
commit**, restoring a margin of 371. The precedent and its reasoning are already written
(`PresenceState.cs:1199-1214`): *"this sizes ONE local send buffer … and appears in no packet,
header or contract"*, and it has been raised twice before for exactly this reason (848 → 1280,
1280 → 1600). Add the new paragraph in the established form:

> *1357 → 1429 on \<date>: the MAP ROOM record (20) added its worst case of 7 bytes and the SHARED
> WINDOW record (21) its worst case of 65 bytes — `[id][len]` plus `[n]` and two pose-carrying
> entries — in their own commit, per the rule below. `MaxSize` was raised 1600 → 1800 in the same
> commit because the margin at 1600 would have been 171, thinner than the largest single record
> (257, board tuning) — the same reasoning as the two earlier raises, and invisible to every peer
> because what goes out is the byte count each writer returns.*

### 7.5 The six edits, in the project's established order

There is **no written checklist** in the repo (I searched `src/`, `scripts/`, `tests/`, `docs/`,
`*.md` for "six edits", "checklist", "to add a record"). The nearest statements are the three-line
rule at `NetProtocol.cs:368-370` and the `MaxSize` rule at `PresenceState.cs:1216-1219`. The
de-facto set below is derived by tracing record 19 end to end, and **writing it down as a comment
next to record 21 is itself worth doing.**

Per record:

1. **`Net/NetProtocol.cs`** — the `ExtId*` constant with its **RECORD-ID CLAIM** note (restating
   the census and re-opening the reservation), the size constants, the bit constants and the
   `*DefinedMask`, any codecs, and the full byte-layout doc paragraph. Record 21 additionally
   *reuses* `StoryPageNone` / `StoryPageMax` / `EncodeStoryPage` / `StorySize*` /
   `EncodeStorySize` / `DecodeStorySize` / `ResolveStoryPage` by reference — **do not duplicate the
   values.**
2. **`Net/PresenceState.cs`** — the `Has<Record>` flag and the payload fields on `struct
   PresenceState`, next to the existing groups (`:871-916` is the story group's shape).
3. **`Net/PresenceState.cs`** — the entry in the extras TLV doc header (`:942-1084`). *Note the
   existing gap: record 18 has no entry there. Do not repeat it.*
4. **`Net/PresenceState.cs`** — the `MaxSize` per-record sum term, the total, the dated paragraph,
   and (this time) the `MaxSize` raise itself (`:1113-1220`).
5. **`Net/PresenceState.cs`** — `PresenceSerializer.Write`: the `|| state.Has<Record>` clause on the
   `extensions` tail-open gate (`:1249-1341`) **with the sampler-owns-emptiness comment**, and the
   write block appended **LAST, behind every existing record**, with a worst-case bounds check and
   `records++` (`:2088-2126` is the model). Record 20 goes before record 21.
6. **`Net/PresenceState.cs`** — the `TryRead` branch, length-gated (`else if (id == … && len >= …)`),
   sanitising field by field (`:3325-3399` is the model).

Plus, per record:

7. **`Net/NetAvatarDriver.cs`** — `<Consumer>.Sample(ref extras)` inside `TickExtrasSend` (record
   19 sits at `:1578`), the pre-empt terms in the gate condition (`:1508-1528`),
   `<Consumer>.Observe(kv.Key, in p)` in the per-peer apply loop (`:2817`),
   `<Consumer>.Resolve()` once per frame after the loop (`:2828`), and `<Consumer>.Reset()` at both
   session boundaries (`:624`, `:726`).
8. **`tests/GloomhavenVR.WireTests/MapSyncVectors.cs`** (new) + one line in `Program.cs`.
   Golden vectors are **derived from the spec, never pasted from the writer's output**
   (`StoryVectors.cs:4-7`) — which is what makes the test lane writable from this document before
   the implementation exists.

**What the coverage checker demands: nothing.** `scripts/check-wire-coverage.py` is exclusively
about record 28's `Tune*` field ids vs. board config dials (`TUNE_ID_RE` at `:242`,
`BOARD_SECTIONS` at `:68-76`); it never reads `ExtId`. `[MapRoom]` is not a board section, so the
map-room dials are not even considered. **There is no automated check that a new record has tests,
a doc-header entry or a `MaxSize` term** — those are convention, and `PresenceState.cs:1143-1148`
records that three records shipped without their term precisely because nothing checks it. Treat
the six edits as a manual gate and put the list in the commit message.

**`ModBuild`** 219 → next (`NetProtocol.cs:419`), with a full build note beside it — that note
history is the project's real changelog. **The wire `Version` byte stays 3**; these are additive
TLV records and additive records never touch it (`NetProtocol.cs:38`).

---

## 8. Section G — the split into implementation lanes

### The integrator lands the contract FIRST, in one commit, with no behaviour change

| file | what lands |
|---|---|
| `src/GloomhavenVR/WorldUI/SharedWindows.cs` **(new)** | the enum, `KindOf`, `ParticipatesHere`, `IsShared`, `TryGetGrab`, `BarTint` |
| `src/GloomhavenVR/WorldUI/ModalFallback.3.WindowPanel.cs` | `TryGetStoryGrab` becomes a one-line forwarder to `SharedWindows.TryGetGrab(ScenarioStory, …)` |
| `src/GloomhavenVR/WorldUI/ModalFallback.9.Spawn.cs:1490` | `peerOwned:` ← `SharedWindows.IsShared(wp.Window)` — **the write-war line** |
| `src/GloomhavenVR/WorldUI/MapRoom/MapRoomDriver.cs` | expose `IsCityMap` (forwarding `Parchment.IsCity`) and `BaseSeatScale`; nothing else |
| `src/GloomhavenVR/Net/NetProtocol.cs` | **both** record ids, all constants, all doc, the claim note, the new 33..36 reservation |
| `src/GloomhavenVR/Net/PresenceState.cs` | **both** records: fields, TLV doc, `MaxSize` (+ the 1600→1800 raise), `Write`, `TryRead` |
| `src/GloomhavenVR/Net/NetAvatarDriver.cs` | the six call slots for two consumer classes, wired to empty stubs |
| `src/GloomhavenVR/Net/RemoteMapRoom.cs`, `RemoteMapStory.cs` **(new, stubs)** | `Reset/Sample/Observe/Resolve`, doing nothing |
| `.planning/STATE.md` | correct the ModBuild and the record census |

After that commit the four single-owner files (`NetProtocol.cs`, `PresenceState.cs`,
`NetAvatarDriver.cs`, `ModalFallback.*`) are **closed to the lanes**. Every lane then owns files
nobody else touches. This is the same discipline `.planning/worldmap-3d.md:698-701` demands
(*"This phase must not run in parallel with another lane that also edits
`NetProtocol.cs`/`PresenceState.cs`"*).

### The lanes

| lane | owns | independently shippable? |
|---|---|---|
| **A — grab-bar colour** (task 3, presentation half) | `WorldUI/GrabbableModal.cs`, `WorldUI/PanelGrab.cs` | **Yes.** Blue bar on the scenario story window, today, zero wire change. A one-round hardware verdict on the colour before anything else exists. |
| **B — map room presence, surface, hover** (2a, 2b-staging, 2c) | `Net/RemoteMapRoom.cs`, `WorldUI/MapRoom/MapLocationInteractor.cs`, `WorldUI/MapRoom/MapButtonRail.cs`, `WorldUI/MapRoom/MapIconHoverPads.cs` | **Yes.** Two clients, one hovers, the other sees the placard. |
| **C — map story + quest-window sync** (2d) | `Net/RemoteMapStory.cs`, `Net/RemoteStorySync.cs` (refactor the pose half into a shared helper — **C owns it after the contract lands**), `WorldUI/MapRoom/MapTravelConfirm.cs` (read-only) | **Yes.** One clicks, both advance. |
| **D — wire tests** | `tests/GloomhavenVR.WireTests/MapSyncVectors.cs` | **Yes, and it can be written before B and C compile** — golden vectors are derived from this document's §7, never from the code (`StoryVectors.cs:4-7`). Run it against the contract commit; it should fail only on unwritten samplers. |

Lane A and lane D can start the moment the contract lands. B and C are disjoint and can run
together in worktrees (`isolation: worktree`, per the standing parallel-worker rule).

**Do not put the colour and the pose sync in one lane.** They share only the predicate, they have
different failure modes (a wrong colour is a look, a lost pose is a write war), and A has a
hardware verdict of its own that is worth getting early.

---

## 9. Section H — risks, ranked, each with its cheapest falsification

1. **`PanelPoseWatch` reverts every remote pose on any window that is not marked `peerOwned`.**
   Eight corrections, then a concede-and-log (`PanelPlacement.cs:202`, `:631-647`). Symptom on
   hardware: "the window snaps back a few times and then works", which reads exactly like network
   jitter and would cost a build round. **Falsification, cheap and single-player:** from the debug
   page, call `PlaceFrameAt` on a floated non-story window and read `Player.log`. A
   `PanelPoseWatch` correction line means the hazard is real and the `peerOwned` line is mandatory;
   silence means the window was already exempt for another reason and the line is belt-and-braces.
   *(This one is already answered by reading the source — it is listed first because the cost of
   forgetting it is the highest.)*

2. **Is the parchment frame actually shared?** Everything in §6's frame-1 recommendation rests on
   `MapRoomSeat.Seat.Scale` and `ParchmentRenderer.bounds.center` being identical on two clients.
   **Falsification, free, no new build:** two clients enter the 3D map room; compare the map-room
   report line (`MapRoomDriver.cs:467-471`, which already prints the derived scale) in the two
   `Player.log`s. **Identical ⇒ frame 1 is safe. Different ⇒ frame 1 is dead and §E must be re-made
   on frame 0.** A "they are identical" result is the evidence, not a non-result.

3. **Which panel does the MAP story window float as, and does it float at all?** The mod has never
   referenced `MapStoryController` (grep: zero hits), `UIWindowID` has no Story member, and the
   window is a private `[SerializeField]` reachable only by `AccessTools`. If it is not converted,
   lane C's pose half has nothing to place (the page half still works — record 19's separation of
   page from pose exists for exactly this, `RemoteStorySync.cs:62-66`). **Falsification, free:**
   open the campaign map, trigger any map message (the Gloomhaven intro, a quest unlock), grep
   `Player.log` for `MODAL DIAG` / `MODAL REVEAL` and read the panel name. Lane C cannot write
   `KindOf` without it.

4. **2b means the staged pick, not the committed one — INFERRED.** If it means the committed one,
   the answer is "already synced, nothing to build" and part of lane B evaporates. **Falsification,
   free, one click:** two clients, flat game, host clicks a quest icon and does not confirm. Client
   screen changes ⇒ already synced. Nothing changes ⇒ staged is local and the record is needed.

5. **The surface authority is a user ruling, not a technical choice** (§7.2). Host-authoritative
   means a non-host cannot switch to the city map while in the 3D room. **Falsification: ask him**,
   with the two options written out. Building the wrong one costs a round and an apology.

6. **A remote-driven hover plays the game's own mouse-enter sound.**
   `MapLocation.OnPointerEnter` → `AudioControllerUtils.PlaySound(m_AudioProfile.mouseEnterAudioItem)`
   (`decompiled/GH.Runtime/MapLocation.cs:253-261`). With three players sweeping beams, that is a
   lot of clicking. **Falsification, free:** hover an icon locally in the 3D room and listen; then
   decide whether a remote hover should suppress it (which needs a different seam —
   `ForceHighlight`, `MapLocation.cs:293` — since the sound is inside `OnPointerEnter`).

7. **One preview popup, several peers hovering.** `UIQuestPopupManager` owns exactly one
   `questPreviewPopup` (`:11`) and previews only while `selectedQuest == null` (`:72-78`). So at
   most one remote hover can be shown, with no indication whose it is. Design answer: local hover
   always wins; among remotes, the most recently changed key wins. **Falsification needs two
   clients**; it is a look question and should be photographed before it is tuned.

8. **The key→location cache goes stale across `InitMap`.** Locations are destroyed and respawned
   (`MapChoreographer.cs:585-596`), and both mod collectors already refuse to depend on order
   (`MapLocationInteractor.cs:428-430`). If the cache is not rebuilt on the interactor's existing
   rescan cadence, a remote hover targets a dead object. **Falsification, cheap:** log the cache
   size on every rescan; a size that does not change across a city↔world switch means it is stale.

9. **`Location.ID` is unique per collection, not provably across both.** The game itself searches
   `m_Scenarios` then `m_Villages` (`MapChoreographer.cs:3378-3382`). Our resolver must search the
   same two collections in the same order and **log a collision** rather than pick one silently.
   **Falsification, free:** one log line dumping every live location's id at rescan, sorted; a
   duplicate is visible immediately.

10. **In online play the mod's travel-confirm has no Reisen button to park.**
    `travelButton.gameObject.SetActive(!FFSNetwork.IsOnline)` (quoted at
    `MapTravelConfirm.cs:35`); online the host commits through `UIReadyToggle`
    (`UIMapMultiplayerController.cs:182-205`). So `MapTravelConfirm` — built and tuned across five
    ModBuilds — may park an empty container in the quest window in multiplayer, and the host may
    have no reachable commit affordance in the 3D room at all. **This is out of scope for the two
    requests but it is adjacent and would surface in the first hardware round of this work.**
    **Falsification, free:** one two-client session; look at the quest window in the 3D room as the
    host and see whether a Reisen cap is there.

11. **`MaxSize` must be raised 1600 → 1800 in the same commit** or the sum violates its own stated
    margin rule (§7.4). Invisible to peers, but a `MaxSize` that is not raised is discovered by
    overflow — the exact failure mode the two earlier raises were made to avoid
    (`PresenceState.cs:1205-1210`). **Falsification:** arithmetic, above.

12. **`.planning/STATE.md` is stale on both the ModBuild and the record census.** The next parallel
    round that trusts it will claim a colliding id — which has already happened once
    (`NetProtocol.cs:9106-9114`). **Falsification:** read it. Fix it in the contract commit.

13. **Record 19 does not pre-empt the rate gate** (`NetAvatarDriver.cs:1578` sits after the gate at
    `:1508-1528`). For the scenario that was defensible; if the user reports the *scenario* story
    feeling laggy too, the fix is one term in the gate condition. Not in scope, but worth naming so
    it is not rediscovered as a mystery.

14. **Lane C could mistake `UIEventPanel` for a story window.** Road and city events look exactly
    like a narrative box and their page advance is a **game action**
    (`GameActionType.ContinueRoadEvent`, `decompiled/GH.Runtime/UIEventPanel.cs:606/610/724`,
    receiver `:869`). Syncing it would be the forbidden second channel. **Falsification, free:**
    two clients, open a road event, click "continue" on one. If the other advances, it is already
    synced — leave it alone. *An "it already advances" result is the whole answer.*

15. **The Gloomhaven-intro halt is asymmetric to its release.**
    `MapChoreographer.CheckCampaignIntro` sets `ActionProcessor.SetState(Halted)` at `:1498` when
    online, and `FinishedShowingIntroGloomhavenMessages` (`:1635-1639`) does **not** release it —
    the release comes later through the move-message chain (`SetState(ProcessFreely, MapHQ)` at
    `:2094`, `:2429`, `:3196`). So driving a peer's map story past its last page must run the
    *game's own* `ShowLine → Hide → onFinish → OnFinishShow → ShowNext` chain and nothing of ours,
    exactly as record 19 does (`RemoteStorySync.cs:648-656`). **If the release does not happen, the
    remedy is NOT to call `SetState` ourselves** — that would be writing game state, which this
    project does not do. **Falsification:** two clients, one leaves the Gloomhaven intro standing;
    check whether travel proceeds for the other. Stalls ⇒ the halt is real and 2d is load-bearing;
    proceeds ⇒ the halt is released elsewhere and 2d is purely comfort.

---

## 10. What I would NOT do

* **I would not add a record for the committed quest selection.** The game sends it, host-
  authoritatively, keyed by `Location.ID`, and resolves it by name on every client
  (§2b). A second channel is a second source of truth.
* **I would not drive `MapLocation.Select()` from the wire, ever.** Selection is the game's own
  host-gated path (`UIMapMultiplayerController.cs:183-186`); the mod's whole map-room design is
  *"NO NEW AUTHORITY IS CREATED"* (`MapLocationInteractor.cs:19-27`). Remote records may light an
  icon and place a window; they may never commit anything.
* **I would not identify a map location by spawn index.** The two mod collectors walk the two
  parent objects in **opposite orders** (`MapIconLayer.cs:2234-2235` vs.
  `MapLocationInteractor.cs:271-292`), both have `FindObjectsOfType` fallbacks with no defined
  order, and the interactor's own comment says order is not guaranteed. `Location.ID` exists; use
  it.
* **I would not extend record 19.** It is shipped, test-pinned and correct for the scenario, and it
  has no flag bit that an old reader would ignore safely.
* **I would not clamp a remote pose into the local view cone.** It silently breaks *"voll
  synchronsiert … auch wenn es jemand woanders hinverschiebt"*. Log it instead.
* **I would not turn the grab bar blue for a player whose 3D map is off.** For them the window is
  private and the colour would be a lie.
* **I would not add a config key for the blue.** Settings configure optional content and comfort
  only; this is a look, and the look is his to approve.
* **I would not ship A, B, C and D as one branch.** Each has its own hardware verdict, and lane A's
  verdict (the colour) can change nothing else, which is exactly why it should go first.
