# Shared city-event and quest readiness review — Build733

The maintainer reports that one participant sees a city-event window while the
others do not, and explicitly requests a combined review of quest windows,
quest readiness, city events and city readiness. The campaign's permanent city
button remains present and uses the game's normal disabled state; none of the
following changes removes it.

Build731 repaired two pooled-control lifetime defects, but did not make the
city-event **preflight** public. The full event card and its preflight are
different native windows. The spoken video alone does not identify a UIWindow;
the native `Cancel City Event` caption supports the preflight interpretation.
Current local729 and remote717 logs are different runs and cannot establish a
paired reproduction of this issue. Original media and the detailed source audit
are retained under ignored `debug/issues18-24`.

## Findings and repairs

| Boundary | Source finding | Result |
|---|---|---|
| Native city proposal | `ProxySelectedCityEvent` initializes `CityEvents` with `show:false`. The desktop `ShowCityEncounterAction` presenter is empty. | Observe the exact native proposal and reveal its original ready control. Do not invoke the supplied city callback: unlike quest preview, it automatically consents. |
| Personal permission | Native `ShouldBeVisible` also excludes nonparticipants; native gamepad `InputToggle` does not independently enforce interactability. | Allow disabled observation of this exact public offer while retaining native visibility blockers, interactability and input permission. Never add a vote. |
| Shared physical window | The existing encounter pose lane resolves only `UIEventPanel`. | Temporarily address the exact preflight on that same lane using the native `SelectCityEvent.ActionID` and stable run identity. Only one window owns the kind; the actual event card takes over after native retirement. No wire layout changes. |
| Quest grouping | `WindowGroups` groups any `UIReadyToggle` under a live quest popup. | Require its actual native role to be `Quests`; a leftover quest cannot swallow city readiness. |
| Character/loadout parking | The loadout parker accepts every role except `Quests`. | Accept exactly native `NotSet`, retire old conversion before adoption and restore the original home on handoff. City, reward and retirement are separate roles. |
| Pooled park leases | Object identity remains the same across native roles; the old parker can also clear a successor's lease. | Capture the native role in the lease, invalidate immediately on role change and release only the retiring owner's role. |
| City private dismissal | Generic X/Escape hides only the local control without withdrawing its native vote. | Do not add an X to this public preflight; refuse stale private-close requests. The original ready/cancel control remains the sole consent/withdrawal route. |
| Visible readiness | The original tracker bar remains in the replaced flat HUD. | Reuse it in the city window before its first conversion. Reject stale quest/loadout offers and exclude the adopted row from the button's own ink measurement. Original markers, order, readiness and animation remain native. |
| Participant departure | City initialization disables native departure validation. Two ready participants can remain blocked after an unready third leaves. | Enable the existing native participant-departure quorum/ACK path in the two map readiness phases. The game owns ACK waiting, timeout, `ReadyProceed` and reset. |
| Error propagation | These seams execute beneath the game's network dispatcher. | Isolate presentation failures, retain original native results and keep disabled-observer input closed on probe failure. A diagnostic must not cause a game desynchronization. |
| Incidental quest focus | Party/quest focus refresh can call `ToggleReadyUpUI(Quests)` without retiring the native city role. | Preserve the exact city lease through these refreshes and the all-ready ACK wait. A genuine role initialization or native withdrawal still retires it. |
| Original 2D confirmation | The original city's user callback initializes `CityEvents` again before sending its one readiness vote. | Same-role initialization retains the known public occurrence; returning to 3D neither loses it nor repeats the vote. |
| Competing quest browsing | An unready client has no native full-screen map lock during a city proposal or event. Questlog selection also bypasses table icon dispatch. | Bound new browsing to the public decision; guard original UI entry points, not gameplay selection methods. Retire cached/pending cosmetic openings so they cannot replay after the event. |

## Authority and transitions

The host's native city proposal creates the occurrence. Clients use the action
they actually received; no mod message selects or draws an event. Local unready
does not withdraw the host's public proposal. Host withdrawal, singleton/controller
replacement, native reset, a new readiness role or actual event entry retires it.
Temporary 3D-map departure does not replay an answered proposal or vote.

The raw native role, not the mutable controller display state, owns the city
decision through the ACK wait. The visible preflight closes at all-ready, but
this does not permit an incidental quest focus callback to replace city consent.
The native 2D city callback remains valid: its same-role initialization preserves
the occurrence and its original user action sends exactly one vote.

Quest browsing cannot supersede an exact public city proposal, its ACK wait or
the actual native event card. Both local table input and incoming cosmetic
selection metadata observe that boundary; late pre-event browsing is retired
with the existing causal register. The original questlog/map pointer entry
points are covered too, including gamepad dispatch. Pure native `Select`,
`Deselect`, scripted location selection and event actions are not blocked.
Only unaccepted local browsing can be cleared with the original `CancelTravel`
UI callback; an accepted native host quest is never hidden or deselected by
presentation cleanup. Native 2D input remains outside the VR browsing gate.
The native event's content lease lasts through hidden reward distribution until
`OnDistributedRewards` clears `eventData`; closing/fading its card is not event
completion. Causal retirement also consumes the exact remote adoption's input
revision before 2D stand-down removes `ActiveLocations`. Returning to 3D cannot
misclassify that adoption as a new local click. A later real user click remains
publishable. Tests cover both sides of the first presence sample with the
production participation getter, including its inactive `null` result.

The full `UIEventPanel` remains mandatory. Its content and decisions already use
native `ContinueRoadEvent` actions; only physical placement uses the mod channel.
Quest callbacks still require their exact controller, quest and `Quests` role.
Quest readiness stays inside its quest window; loadout readiness stays inside its
character window. City readiness cannot be adopted by either one.

The existing host/surrogate spawn authority, captured common world reference,
manual move/resize and source-frame transport apply to the preflight too. A
viewer must not substitute its own zoom or gaze. Native flat clients receive
ordinary game actions only. The mod does not write participant collections,
readiness dictionaries, ACKs or original event content.

One suspected cross-phase consent race was investigated and **not** patched:
native `Initialize` temporarily retains old ready entries, but the reliable
ordered stream and FIFO action queue deliver their auto-validated withdrawals
before later ACKs. The unchanged native withdrawal kills the old ACK coroutine.
An executable temporal proof with two/four peers and both map phases confirms
that only fresh city votes and fresh ACKs can proceed. This is bounded scheduler
and transport-boundary evidence, not a formal network or headset proof.

## Validation and limits

Focused integration covers production city proposal/permission/lifecycle logic,
actual HarmonyX registration under Unity Mono, original native departure/ACK
bodies, modal close, quest/native proposal handling, pooled group/park handoffs,
roster ink/clip/layer behavior, shared identity, source-frame pose transport and
real Unity shared geometry across independent zooms. Causal negative controls
must fail at their intended assertion, not merely during compilation.

The roster tests execute the actual coordinator and ink union before any grab
exists. The capture source unions visible child ink without clamping to the
original 65-pixel control root, so the adopted markers are included. This is not
a claim about final headset pixels or simultaneous four-headset timing.

Passing evidence for unchanged areas is inherited. A focused suite invocation
is not a new full local gate. Final commands, exact counts, source pins, build
artifacts, compiled-scope review and cleanup receipts are retained under ignored
`debug/issues18-24/city-sync733`. No local release ZIP is built.
