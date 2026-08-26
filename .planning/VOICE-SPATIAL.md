# Spatial voice chat — the seam, the mapping, the curve, and what could not be verified

**Status:** built, gated, measured offline. **Never run on hardware and never run with a second
client.** Read the last section before believing any of the rest.

**User request (2026-08-24, verbatim):**

> "Bitte gehe den ingame Voice-Chat als erstes an. Wie du bereits erwähnt hast soll er räumlich
> sein, also die Stimme kommt von der jeweiligen Maske. Optional möchte ich auch, dass wenn jemand
> spricht das entsprechend im Stem-Logo sichtbar ist (deaktivierbar). zB mit einem Lautsprechersymbol
> in einer Ecke das ausschlägt bei Ton. Versuch es so gut es geht bei dir zu verifizieren und zu
> testen."

**His own correction, same round:** *"Steam-Logo über dem Kopf; sorry typo"* — so "Stem-Logo" was
**Steam**, and the indicator belongs on the Steam profile picture the mod already floats above every
peer's head (`Net/RemoteNameTag.cs`), not on the mask and not in a corner of the view.

---

## 0. The one-paragraph version

The game already has a voice chat, over Photon, with its own options page, its own per-user mute and
its own hotkey. **Nothing here replaces it, adds a dependency, or asks anybody for an account.** The
mod reads it, moves each remote peer's `AudioSource` onto that peer's VR mask every frame with a
speech-shaped rolloff in *perceived* metres, and draws a small loudspeaker in the corner of that
peer's Steam picture while they talk. It installs **no Harmony patch at all** — the patch inventory
is unchanged at 80 classes / 132 methods — and it never writes a volume, a mute, or any game state.

---

## 1. The seam

Everything the feature needs is **public**. This is a correction to the brief, which named
`BoltVoiceChatService.VoiceBridgeOnEventAddIncomingVoiceUser` (a private method) as "the seam" and
assumed a patch was required.

```
Singleton<BoltVoiceChatService>.Instance              GH.Runtime/VoiceChat/BoltVoiceChatService.cs:14
  .PlayerVoices          IReadOnlyList<ConnectedUserVoice>                                     :28
  .SelfUserVoice         SelfUserVoice                                                         :26
  .IsVoiceChatConnected  bool                                                                  :30

ConnectedUserVoice                                    GH.Runtime/VoiceChat/ConnectedUserVoice.cs:7
  .PlatformAccountID  string   the join column onto FFSNet                                     :33
  .VoiceChatUserId    int      the Photon actor number = the speaker-registry key              :37
  .IsSpeaking         bool     => _speaker.IsPlaying — the game's own talk flag                :41
  .IsMuted / .Name                                                                          :13,:31

BoltVoiceBridge.Instance                              GH.Runtime/VoiceChat/BoltVoiceBridge.cs:25
  .GetSpeaker(int playerID, out GameObject speaker)                                           :269
```

`GetSpeaker` is what makes a patch unnecessary: it is public, its dictionary is keyed by the same
int `ConnectedUserVoice.VoiceChatUserId` holds, and its `out` parameter is a **`GameObject`**, not a
Photon `Speaker`. So the `AudioSource` is reachable without touching the private `_audioSource` /
`_speaker` fields and without naming a Photon type.

### Proof the seam resolves — compiler-checked, not read off a decompile

I wrote the direct calls and compiled them against the game's shipped `GH.Runtime.dll`. The split
was **measured, not assumed**, and the assumption I started with was half wrong:

| Member | Direct C# reference | Evidence |
|---|---|---|
| `Singleton<BoltVoiceChatService>.Instance` / `.IsInitialized` | **compiles** | build 0 errors |
| `BoltVoiceChatService.PlayerVoices` / `.SelfUserVoice` / `.IsVoiceChatConnected` | **compiles** | build 0 errors |
| `ConnectedUserVoice.{PlatformAccountID,VoiceChatUserId,IsSpeaking,IsMuted,Name}` | **compiles** | build 0 errors |
| `SelfUserVoice.PlatformAccountID` | **compiles** | build 0 errors |
| `BoltVoiceBridge.Instance` / `.GetSpeaker` | **fails** | `error CS0012: the type 'GlobalEventListener' is defined in an assembly that is not referenced… 'bolt.user'` |

So twelve members are now **verified by the compiler against the real DLL** rather than being
reflection strings that might be wrong, and the per-frame `IsSpeaking` read is a direct property
call. Reflection survives for `BoltVoiceBridge.GetSpeaker` alone, resolved once per binding.

### Where the "may not patch" line was drawn

The standing ruling is: never patch `ScenarioRuleLibrary`, Photon Bolt, or `FFSNet.NetworkManager`.

`VoiceChat.BoltVoiceChatService` is **game code in `GH.Runtime`**, not `bolt.dll`, so a Harmony
patch on it would have been within existing convention — the mod already patches
`FFSNet.ActionProcessor` (`Net/FfsNetTransport.cs`) on exactly that reasoning. The line that is not
crossed under any circumstance is `bolt.dll`, `PhotonVoice.dll` and `FFSNet` internals.

**In the end the question is moot in fact as well as in principle: no patch was needed.** The
feature adds zero Harmony patches, zero assembly references and zero net messages. It also never
names a Photon type — which is why `bolt.user` and `PhotonVoice.dll` stayed off the compile line
even though `GH.Runtime` is already referenced and publicized.

---

## 2. The voice-user ↔ avatar mapping

**This is the game's own join, not a heuristic, and explicitly not "the only other player in the
room" — which is right for two players and silently wrong for three.**

```
ConnectedUserVoice.PlatformAccountID
   == NetworkPlayer.PlatformNetworkAccountPlayerID          <-- string equality
   -> NetworkPlayer.PlayerID   (int)
   -> NetAvatarDriver._avatars[playerId] -> RemoteAvatar.HeadHolder
```

The shipped game performs that identical comparison in **two** places to decide which portrait and
which name belong to a voice row:

- `Script.GUI.IngameMenu.EscMenuVoiceChat/PlayerPortraitVoiceComponent.cs:41-45`
- `Script.GUI.IngameMenu.EscMenuVoiceChat/PlayerNameVoiceComponent.cs:146-150`

both literally
`PlayerRegistry.AllPlayers.FirstOrDefault(x => x.PlatformNetworkAccountPlayerID == accountId)`.
The game even ships it as a helper at `FFSNet/PlayerRegistry.cs:266`.

Both sides are filled from the same source, which is why the equality is exact and not a coincidence:

- **voice side** — `BoltVoiceBridge.SetUpUsername()` builds `PhotonUserData` from
  `PlatformLayer.UserData.PlatformNetworkAccountPlayerID` (`BoltVoiceBridge.cs:172`, console path
  `:176`, signed-out fallback `"0"` at `:182`), which comes back out at `:230` and lands in
  `ConnectedUserVoice.PlatformAccountID`.
- **player side** — `NetworkPlayer.Attached()` sets it from the connect token
  (`FFSNet/NetworkPlayer.cs:146`, via `PlayerRegistry.CreatePlayer` `:166-190`).

Implementation: `Net/NetPlayerActors.PlayerIdForNetworkAccount(string)`. It is reflected (not
directly typed) because `NetworkPlayer` is Bolt-derived, which is the same reason the rest of that
file reflects.

### The trap next door, which I nearly walked into

An earlier exploration proposed joining on `NetworkPlayer.PlatformPlayerId`. **That is wrong.** On
Steam, `PlatformNetworkAccountPlayerID` is the **32-bit `SteamId.AccountId`**
(`PlatformUserData.cs:95-96, :106`) while `PlatformPlayerId` is the **64-bit SteamID64**. Crossing
those two is the exact mistake that once made every peer show a grey avatar; `NetPlayerActors.cs`
already carries the post-mortem, and the new accessor's doc comment repeats the warning next to the
code that could reintroduce it.

### What a mismatch looks like, and what the log says

A voice user we cannot resolve keeps `spatialBlend = 0`: **audible exactly as it is today, from
everywhere, never silenced.** One line per unmatched user, naming what it compared against —

```
VOICE SPATIAL: no network player for voice user 'Bob' (account 4711, voice id 3) —
roster was [1: 815 'Alice', 2: 1234 'Carol']. Their voice stays NON-SPATIAL (2D), which is
exactly vanilla behaviour — it is NOT muted and NOT quieter. The join is
ConnectedUserVoice.PlatformAccountID == NetworkPlayer.PlatformNetworkAccountPlayerID, the
game's own comparison; if the roster above shows the right person under a DIFFERENT id string,
that is the bug and this line is the evidence. An empty account or "0" means the peer is signed
out of their platform, which the game itself cannot map either.
```

A *successful* bind logs `VOICE SPATIAL: voice user 'Bob' … bound to network player 2 … IF THE VOICE
COMES OUT OF THE WRONG MASK, THIS LINE NAMES THE BINDING THAT WAS WRONG.`

**A voice bound to the wrong player is the one failure this design cannot detect by itself**, because
the game's own portrait UI would be wrong in the same way for the same reason. It is detectable only
by a human hearing Bob out of Carol's mask, and the log line then names the binding.

---

## 3. The curve, and its dials

`Voice/VoiceCurve.cs` — pure arithmetic in **perceived metres**, free of Unity beyond `Mathf`, so it
can be linked into the wire tests and asserted in decibels.

```
gain(d) = 1                                              d <= full
        = ((silence - d) / (silence - full)) ^ shape     full < d < silence
        = 0                                              d >= silence
```

Shipped defaults: `full = 2 m`, `silence = 25 m`, `shape = 1.6`, all PERCEIVED metres.

| distance | level | | distance | level |
|---|---|---|---|---|
| 0–2.0 m | 0.00 dB (flat) | | 10 m | −5.94 dB |
| 3 m | −0.62 dB | | 14 m | −10.25 dB |
| 4 m | −1.26 dB | | 20 m | −21.21 dB |
| 6 m | −2.66 dB | | 25 m | silence |
| 8 m | −4.20 dB | | | |

**Audible range: a teammate is within 6 dB of full level anywhere inside 10 perceived metres, and
within 1 dB across a table.** Both are asserted by the gate, not claimed.

### Why not `AudioRolloffMode.Logarithmic`

`EnvSound` uses logarithmic and calls it "physical, and the default Unity tunes for". That is right
for a dripping ceiling and **wrong for a person talking**. Unity's logarithmic rolloff is
`minDistance / d`: −6 dB per doubling, forever, with no flat region. Two players around a table are
1–3 perceived metres apart, so under a logarithmic curve a teammate leaning in and leaning back
differ by ~6 dB — a level that pumps every time anybody shifts their weight, on the one signal in
the game that must stay intelligible. Hence a **custom** curve with a deliberately flat plateau.

### The dials (`[Voice]`, all live, all localised EN/DE)

| Key | Default | Range | What it does |
|---|---|---|---|
| `Enabled` | `true` | — | Master switch. Off = the game's voice chat, untouched. |
| `SpatialBlend` | `1.0` | 0–1 | 1 = fully placed at the mask, 0 = vanilla non-positional. |
| `FullLevelMeters` | `2.0` | 0.25–10 | Radius of the flat, full-volume plateau. |
| `SilenceMeters` | `25.0` | 2–60 | Where a voice fades to nothing. |
| `RolloffShape` | `1.6` | 0.25–4 | Falloff exponent; 1 is a straight line. |
| `Spread` | `35°` | 0–180 | Angular width — stops a peer beside you slamming to one ear. |
| `SpeakingBadge` | `true` | — | His "(deaktivierbar)". |
| `BadgeScale` | `0.38` | 0.15–0.8 | Badge size as a fraction of the Steam picture. |

`dopplerLevel` is **0 and is not a dial**, for `EnvSound`'s reason exactly: the sources are static
but the listener is not, and at rigScale ~22 a comfortable head movement is ~22 world units/second,
which Unity would hear as a supersonic listener and pitch-shift accordingly.

### Perceived metres, and the ear

Distances are multiplied by `rigScale = RigRoot.lossyScale.x` in exactly one place
(`VoiceSpatial.ApplyScale`), re-applied whenever the player zooms, on the same 0.1 % tolerance
`EnvSound` uses. This is `Core/EnvSound.cs:128-155`'s convention, followed rather than reinvented.

**A defect found on the way, and fixed:** the only code in the mod that ever put an `AudioListener`
on the head was `EnvSound`, which owns it **only while an environment is standing with environment
sounds switched on** — a switch the user is explicitly offered. With them off, the enabled listener
is the game's *parked* camera, many world units from the head, and every distance and bearing above
would have been computed against the wrong point: not a degraded feature, a wrong one, and invisible
to every gate in this repo. The listener is now owned by `Core/HeadEar.cs` with named claims;
`EnvSound` and `VoiceSpatial` each claim it and it survives until the last one lets go. `EnvSound`'s
external behaviour is unchanged.

While reading that code I also found `EnvSound.cs:4630-4632` asserts the game's voice-chat prefab
carries a live `AudioListener` (`BoltVoicePlayerController.cs:18`). **That assertion is wrong.**
`BoltVoicePlayerController` is dead code: `IVoicePlayer` has no definition anywhere in the decompiled
tree, there is no `BoltPrefabs.cs`, and nothing references the type. It is leftover Photon Bolt Voice
sample code — and, interestingly, a working sketch of exactly the reparenting this feature needed.
The correction is recorded in `HeadEar`'s class doc.

---

## 4. What is written, and what is deliberately not

Per remote speaker, per frame: `transform.position` only. Change-gated (written once, and again only
when the binding or a dial changes): `spatialBlend`, `rolloffMode` + custom curve, `minDistance`,
`maxDistance`, `spread`, `dopplerLevel`, `bypassReverbZones`, `priority`. **Every one is saved
before the first write and restored on stand-down** — which matters because the game *pools and
recycles* speaker GameObjects across users (`BoltVoiceBridge.cs:199-208`), so an unrestored rolloff
would leak onto the next person to join.

**Never written:** `AudioSource.volume` (that IS the game's per-user volume slider,
`ConnectedUserVoice.Volume`), `Mute`/`UnMute`, `Speaker.StartPlayback`/`StopPlayback` (that IS the
game's per-user mute, re-asserted every update by `VoiceChatUserBlockerController.UpdateMuteStates`),
and any game state whatsoever. That is *why* the flat game's voice UI, its options page and
`ToggleVoiceChatControl` keep working: this feature does not participate in any of them.

`ToggleVoiceChatControl`, incidentally, does **not** toggle voice — it is a UI-navigation hotkey that
shows/hides the ESC voice panel. Nothing in the game consumes it to mute or unmute.

### Lifecycle

- **Peer joins mid-session** — the voice list is *polled*, so a new entry binds on the next tick.
- **Voice arrives before the avatar** — the common case (the voice room is joined long before a
  peer's first VR rig packet, and a flat-screen peer never sends one). Binding retries once a
  second; the voice stays 2D meanwhile. **The feature never trades audibility for position.**
- **Peer leaves while talking** — the entry vanishes from `PlayerVoices`, we restore their
  `AudioSource` and forget them.
- **The local player's own voice** — `PlayerVoices` only holds *incoming* users, so it should never
  contain us; we compare against `SelfUserVoice.PlatformAccountID` anyway and skip with a loud line.

**Events were rejected on purpose.** `BoltVoiceChatService.OnEventStateUpdate` (`:118-130`) raises
`EventUserDisconnected` for **every** entry on **every** Bolt state update — the `Invoke` at `:127`
sits *outside* the `if (!IsLinked)` block at `:123-126`. The game's own UI works around this with a
blunt unbind-and-rebind. Polling the list *is* the unbind-and-rebind, without the wrong intermediate
state.

---

## 5. The speaking badge

`Voice/VoiceBadge.cs`, drawn as a **sibling** of the Steam-avatar quad in `Net/RemoteNameTag.cs`
(a child would inherit the quad's non-uniform `localScale` — the same trap `AvatarTurnRing` already
solved), seated in its lower-right corner, proud of both the picture and the turn ring.

Putting it there is better than a free-standing indicator for three reasons worth recording:

1. The tag is constructed per `RemoteAvatar` and keyed by `PlayerId`, so the badge **inherits** the
   identity mapping — there is no second place a mis-binding could appear.
2. It is already gated by `[Net] NameTags`, so that needed no second rule.
3. It is already above the head, beside the face the voice now comes from, so picture and sound
   agree *by construction*.

**With `[Net] NameTags` off there is no badge and the voice is still spatial.** That is intended, not
an oversight: the two dials answer different questions, and the badge is a feature *of* the label.

**The local player never sees one on themselves**, for two independent reasons: a `RemoteNameTag`
exists only per `RemoteAvatar` and the local player has no `RemoteAvatar` (the driver drops its own
echo at `NetAvatarDriver.cs:2766`); and `VoiceSpatial` only publishes state for voice users bound to
a *remote* network player.

### The level — a correction to the brief

The brief said to read the level from the game's own `VoiceDetector`. **There is no remote
`VoiceDetector`.** Self and remote implement the same interface with different semantics:

```
SelfUserVoice.IsSpeaking      => _recorder.VoiceDetector.Detected   // a real VAD          :61
ConnectedUserVoice.IsSpeaking => _speaker.IsPlaying                 // a playback flag     :41
```

There is no per-remote amplitude anywhere in the game. Photon's `ILevelMeter` exists in
`PhotonVoice.API.dll` but hangs off the *local* voice only and is not exposed on a remote `Speaker`.
So "ein Lautsprechersymbol das ausschlägt bei Ton" cannot be served by the game's detector alone.
The split is therefore:

1. **WHETHER someone is speaking is the game's flag, unmodified** — the badge is gated on
   `ConnectedUserVoice.IsSpeaking`, the identical expression the flat game's roster uses to light its
   talk icon (`PlayerTalkVoiceComponent.cs:20`). The two can never disagree about *who* is talking,
   which is the disagreement that would have been undebuggable.
2. **HOW FAR it deflects is the RMS of that peer's own `AudioSource`** (`GetOutputData`, 256
   samples), smoothed with a fast attack / slow release and quantised to four steps with hysteresis.
   That is not a second opinion about who is speaking — it is a measurement of *the exact audio the
   player is hearing*, taken from the game's own source object, and it is never asked whether
   somebody is talking.

### Cost per frame

One dictionary lookup, one int compare, and — only when the level crosses a step boundary — one
`Material.mainTexture` assignment. The four 64×64 frames are rasterised once for the session and
shared by every peer (~88 KB VRAM total). The carrier's own renderer cache refreshes only every 90
frames, so the badge additionally reports when it changes visibility and forces that cache stale
immediately — otherwise a badge that just appeared would spend up to a second at draw order 0 while
its row rides the panel ladder at ~96, and a menu window behind the peer would paint over it.

### Legibility — the arithmetic, because "it's a small icon" is not an answer

The avatar quad is `AvatarSize = 0.075` units at sender scale 1, drawn at the sender's rig scale.
With both players at comparable scale it subtends what a 7.5 cm square would at the same real
distance. At `BadgeScale = 0.38`:

```
badge edge = 0.075 * 0.38 = 2.85 cm perceived
at 1.5 m   ->  19.0 mrad = 1.09 deg  ->  ~22-27 px on a Quest 3 (~20-25 PPD via Virtual Desktop)
at 2.5 m   ->  11.4 mrad = 0.65 deg  ->  ~13-16 px
```

So it is legible as "a speaker symbol with something happening in it" across the range, but **the
individual arcs will not be separable at 2.5 m**. That is why the glyph carries its own dark contrast
rim, why the arcs are widely spaced, and why the badge appears and disappears with speech rather than
only changing internally — the ON/OFF transition is legible wherever the tag itself is.
**If he reports he cannot see it, the first dial to move is `[Voice] BadgeScale` toward 0.6.** This
is a prediction from arithmetic and a pixel-density estimate, not a measurement.

---

## 6. Verification — what was actually done, without hardware

**I have no headset and no second client.** Everything below is what can be done without them.

### 6.1 The rolloff, measured from Unity's real mixed output

`scripts/voice-spatial-probe.sh` builds a headless Linux player, drives a real `AudioSource` around
a real `AudioListener` at a real rig scale, and reads back **the final mix** — not the settings we
asked for.

- config: `.planning/voice/shipped.json` — **generated by the wire tests from the shipped
  `VoiceCurve`**, so the thing measured *is* the thing that ships, not a hand-copied lookalike.
- data: `.planning/voice/shipped.csv` (258 rows) · plot: `.planning/voice/shipped.png`
- `rigScale = 13.75`, a real logged session value (`Core/EnvSound.cs:1046`), not a round number.

**Measured vs authored, at perceived metres (world = ×13.75):**

| d (m) | world | measured | authored | Δ |
|---|---|---|---|---|
| 0.5 – 1.0 | 6.9 – 13.8 | 0.00 | 0.00 | 0.00 |
| 1.5 | 20.6 | −0.04 | 0.00 | −0.04 |
| 2.0 | 27.5 | −0.09 | 0.00 | −0.09 |
| 3.0 | 41.3 | −0.62 | −0.62 | 0.00 |
| 4.0 | 55.0 | −1.26 | −1.26 | 0.00 |
| 6.0 | 82.5 | −2.65 | −2.65 | 0.00 |
| 8.0 | 110.0 | −4.20 | −4.20 | 0.00 |
| 12.0 | 165.0 | −7.93 | −7.93 | 0.00 |
| 14.0 | 192.5 | −10.25 | −10.25 | 0.00 |
| 20.0 | 275.0 | −21.16 | −21.21 | +0.05 |
| 22.0 | 302.5 | −28.20 | −28.31 | +0.10 |
| 25.0 / 30.0 | 343.8 / 412.5 | floor (−120) | silence | — |

**Worst error 0.10 dB across the whole audible range.** The −0.09 dB inside the plateau is the
`AnimationCurve`'s smoothed tangents dipping fractionally below 1 between keys; it is an order of
magnitude below the ~1 dB just-noticeable difference.

**This settles a question I could not answer by reading.** Unity documents the custom rolloff curve
as spanning 0..1 over 0..`maxDistance`, but the alternative reading is `minDistance`..`maxDistance`,
and the two disagree by exactly the plateau width — the difference between a correct curve and one
that reaches silence three metres early. The measurement says **normalised to `maxDistance`**, which
is what `VoiceCurve.SampleKeys` assumes. It is now a measured fact rather than a hopeful comment.

### 6.2 The pan curve, and a real limitation of the approach

| bearing | shipped (spread 35°) | spread 0° | null control |
|---|---|---|---|
| 0° (ahead) | 0.0000 | 0.0000 | 0.0000 |
| 30° | +0.3662 | +0.3780 | 0.0000 |
| 60° | +0.7291 | +0.7746 | 0.0000 |
| **90° (right)** | **+0.9133** | **+1.0000** | 0.0000 |
| 120° | +0.7291 | +0.7746 | 0.0000 |
| **180° (behind)** | **0.0000** | **0.0000** | 0.0000 |
| 270° (left) | −0.9133 | −1.0000 | 0.0000 |

`balance = (R−L)/(R+L)`. Level across all 24 bearings varies by **0.20 dB** — Unity's panner is
constant-power, so a peer walking around you does not change loudness, only side.

Two things fall out of this:

- **`spread = 35°` does measurably what it is claimed to do**: it caps the pan at ±0.913 instead of
  ±1.000, so a teammate standing beside you is clearly to that side without the voice slamming
  entirely into one ear. That dial's justification is now a number.
- **Unity's built-in panner has NO front/back discrimination.** 0° and 180° both read exactly
  0.0000; 30° and 150° both read +0.3662. A teammate directly behind you sounds identical to one
  directly in front. This is a limitation of Unity's default spatialisation, not of this code, and
  it cannot be fixed by any dial here — it would need an HRTF spatializer plugin. Distance and
  left/right work; front/back does not. **He should be told this**, because it is exactly the kind
  of thing that reads as "the feature is broken" if it arrives unannounced.

### 6.3 Controls — a null and a positive, per this project's habit

| case | balance range | level |
|---|---|---|
| `null_control` (`spatialBlend = 0`) | **+0.000000 … +0.000000** (24 bearings) | **flat, 0.0000 dB spread over 19 distances** |
| `hardpan_left` (`panStereo = −1`) | **−1.000000** | flat |
| `hardpan_right` (`panStereo = +1`) | **+1.000000** | flat |
| `silence` (not playing) | 0 | −120 dB (bit-exact zero samples) |
| `shipped` | −0.913 … +0.913 | 0 … −120 dB |

The hard-panned-LEFT case reads **negative**, which proves L and R are not swapped. The null control
is flat in both axes, which proves the spatial case is not the null control plus noise: they differ
by a full unit of balance and 111 dB of level. The noise floor is *literally zero*, not a small
number.

The probe carries its own honesty check: it reports the peak absolute sample over the entire run
(0.5, the authored tone amplitude). During development that check caught the probe having been
accidentally routed around the listener's filter chain — every other indicator read healthy and
every captured sample was silence.

**`AudioRenderer` is broken on this box** and the rig says so rather than hiding it: `Start()`
returns `true`, then `GetSampleCountForCaptureFrame()` returns 0 forever *and `AudioSettings.dspTime`
freezes* — starting the recorder stops the mixer. The rig detects this after 3 s, logs
`PROBE FALLBACK` loudly, and switches to an `OnAudioFilterRead` tap on the **AudioListener** (the
post-mix, post-pan buffer; the same script on the *source* would be pre-spatialisation and wrong).
The backend actually used is read out of the player log and printed on the plot.

### 6.4 The curve and the glyph, gated on every build

`tests/GloomhavenVR.WireTests/VoiceVectors.cs` links `VoiceCurve.cs` and `VoiceIcon.cs` verbatim.
**Wire tests: 150784 → 151307 assertions** (+523). It asserts the decibel table above, the plateau
(21 points at exactly 1.0), monotonicity over 300 distances, degenerate hand-edited configs, the
sampled keys, the badge's hysteresis (a level parked exactly on a threshold, 50 times, must not
oscillate), frame-rate independence of the envelope at 90 vs 45 Hz, and six properties of the glyph
raster.

> The first version of that decibel table had two values wrong (−2.62 and −10.29, arithmetic done in
> my head; the true values are −2.657 and −10.251) **and the gate caught both on its first run.**
> The whole argument for asserting a curve in decibels is that nobody can eyeball a power function,
> and the first thing it did was prove that on its own author. The corrected block says so in place.

### 6.5 The badge renders, including its off state

`.planning/voice/voice-icon-sheet.png` (+ per-step PNG/PPM). The frames are rasterised **by the
shipped `VoiceIcon`**, dumped by the wire tests, and only scaled and labelled by
`scripts/voice-icon-render.py` — so the picture is of the code that runs, not of a lookalike. They
are composited over mid grey on purpose: the badge is drawn over whatever the room is, and its dark
contrast rim would be invisible over black. The sheet includes the **off** column, showing that
nothing at all is drawn — not a dimmed glyph, not a placeholder.

### 6.6 Gates

| gate | before | after |
|---|---|---|
| build | 0 errors / 4 warnings | **0 / 4** |
| wire tests | 150784 | **151307** |
| mirrors | 18 | **18** |
| frame order | 7 | **7** |
| patch inventory | 80 / 132 | **80 / 132** (no patch added) |
| remote defaults | 76 | **76** |
| refasm | 16 | **16** |
| bundle | 70,204,340 B | **untouched** (no bundle asset; the glyph is generated) |

---

## 7. WHAT I COULD NOT VERIFY WITHOUT HARDWARE OR A SECOND CLIENT

This section is long because it should be. Everything above was established at a desk.

**The mapping, end to end.** I have proved the join is the game's own comparison and that both sides
are filled from the same field, by reading the game's source. I have **not** seen two real Steam
accounts in a real lobby produce equal strings. If Photon's `UserData` round-trip mangles the string
(it goes through a `BinaryFormatter` as custom type code 228), or a platform fills it differently
than the decompile suggests, every peer falls back to 2D and the "no network player for voice user"
line names the mismatch. **That log line is the first thing to read after the first two-client test.**

**Whether a voice ever binds to the WRONG player.** Undetectable from here by construction — the
game's own portrait UI would be wrong identically. Only a human hearing Bob out of Carol's mask can
find it, and the `bound to network player N` line then names the binding.

**Three or more players.** Everything is written for N peers and nothing assumes two, but I have not
run N > 0. The pooled-speaker recycling path (`BoltVoiceBridge.cs:199-208`) in particular is
reasoned about, not exercised.

**That `PlayerVoices` is ever non-empty at all.** The whole feature hangs off it. `SetupAndConnect`
has exactly one caller — the button on the game's own voice options page — so voice is **opt-in per
session and off until somebody presses it**. If he tests without pressing it, nothing here runs and
the log will say `VOICE SPATIAL` nowhere at all. **Tell him to switch voice on in the game's menu
first.** Absence of the log lines is not evidence of a broken feature.

**Whether the ear is where I think it is at the moment of the first claim.** `HeadEar.Claim` disables
every enabled `AudioListener` it finds and puts one on the head camera. I cannot see what a live
multiplayer scene actually contains at that instant. A listener enabled *after* we take ownership
would break panning silently — I deliberately did **not** add a cadenced `FindObjectsOfType` sweep
for it (a per-tick scene sweep has cost this project a whole frame budget before), on the evidence
that the only game code that would enable one is dead. **If a second-client test shows Unity's
"There are N audio listeners in the scene" warning, that reasoning is what was wrong, and a cadenced
re-sweep is the fix.**

**Whether the game's speaker prefab starts 2D or 3D.** I save and restore whatever it has, and I set
what I need, so it should not matter — but the values themselves are in an asset bundle I cannot
read. The `took over the AudioSource … it was spatialBlend X` log line reports them on first
binding, and it is worth reading once.

**Whether it sounds right.** No ear has heard this. The plateau, the shape and the 25 m range are
reasoned choices with measured decibels behind them, not tuned values. Specifically unknown: whether
2 m of plateau feels natural or unnaturally "stuck"; whether spread 35° is the right compromise;
whether a voice at −4 dB across the table reads as "further away" or just "quieter"; and whether the
front/back ambiguity in §6.2 is a non-issue or the first thing he notices.

**Whether the RMS thresholds in `VoiceCurve.StepFor` sit anywhere near real Photon voice levels.**
`0.020 / 0.070 / 0.180` are guesses against typical post-gain-staging RMS. If they are far too high
the badge shows step 1 permanently; far too low and it pins at step 3. **The hysteresis and the
smoothing are gated and correct; the three numbers they operate on are not measured.** They are the
single most likely thing to need retuning after the first real test, and they are deliberately low
so a quiet talker still moves the icon at all.

**Whether the badge is legible in the headset.** §5 gives the arithmetic and a pixel-density
estimate. Nobody has looked through the lens at a 2.85 cm glyph at 1.5 m.

**Whether `AudioSource.GetOutputData` returns anything on a Photon `Speaker`'s source.** It reads the
source's own output and should, but the Photon playback path uses its own buffering
(`AudioOutDelayControl`), and I have not confirmed it against a real stream. **If it returns silence,
the badge still appears and disappears correctly** — that is gated on the game's flag — and only the
arc count would be stuck at step 1. Photon's own `AudioOutCapture.OnAudioFrame` is the documented
alternative and would be the fix.

**Frame cost on the Quest 3.** The per-frame work is one transform write and one 256-sample RMS per
talking peer, inside a `PerfMonitor.Scope("Voice.Spatial")`. That should be far under a microsecond,
but it has never been measured on the rig, and this project has been wrong about "near-free" before.

**The mixed-reality and map-table paths.** The feature deliberately does not hang off
`SkyAlternative`'s environment step (which dies with passthrough on and with plain backdrops), and
`ApplyScale` handles the map table's much larger rig scale by the same one multiply — but the map
room's scale comes from parchment bounds at runtime and I have no value for it. A very large
`rigScale` simply widens the world-unit distances proportionally, so it should be correct; unverified.

**Interaction with the game's per-user volume slider.** I never write `volume`, so the slider should
compose with the rolloff multiplicatively. Untested against the actual UI.

---

## 8. Files

| Path | What |
|---|---|
| `src/GloomhavenVR/Voice/VoiceSpatial.cs` | the driver: binding, placement, scale, level |
| `src/GloomhavenVR/Voice/VoiceChatBridge.cs` | read-only access to the game's voice chat |
| `src/GloomhavenVR/Voice/VoiceCurve.cs` | the rolloff + indicator maths (Unity-free, gated) |
| `src/GloomhavenVR/Voice/VoiceIcon.cs` | the loudspeaker rasteriser (dependency-free, gated) |
| `src/GloomhavenVR/Voice/VoiceBadge.cs` | the badge on the Steam picture |
| `src/GloomhavenVR/Voice/VoiceModule.cs` | dials + lifecycle + the one MonoBehaviour |
| `src/GloomhavenVR/Core/HeadEar.cs` | **new** — refcounted `AudioListener` ownership |
| `src/GloomhavenVR/Defaults/Defaults.Voice.cs` | the eight shipped defaults |
| `src/GloomhavenVR/Net/NetPlayerActors.cs` | `PlayerIdForNetworkAccount`, `CollectRoster`, `LocalPlayerId` |
| `src/GloomhavenVR/Net/NetAvatarDriver.cs` | `TryGetPeerHeadHolder` |
| `src/GloomhavenVR/Net/RemoteNameTag.cs` | hosts the badge |
| `tests/GloomhavenVR.WireTests/VoiceVectors.cs` | the gate (+523 assertions) |
| `scripts/voice-spatial-probe.sh` | the headless Unity measurement |
| `scripts/voice-icon-render.py` | PPM → PNG for the badge frames |
| `.planning/voice/` | measured CSVs, plots, badge renders, generated probe config |
