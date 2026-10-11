# Town resident motion and native foley — build 552 lane

Hardware evidence: local build 551; mage idle screenshot
`VirtualDesktop.Android-20260924-160835.jpg`. Remote logs are older build 500,
not evidence about this local NPC test.

The mage's two mirrored arms received the same positive roll. The left palm also
blended toward a different height-dependent reference frame while the right used
a level frame. Mirror the left pronation and keep both on the same contact frame;
retain the existing bounded elbow solver, distributed forearm skin supports and
continuous attention transition. The final imported mage pose was rendered and
visually inspected; headset appearance remains unverified.

The merchant's generated hand path had hard x/z clamps. Replace the resulting
flattened piston with distinct minimum-jerk approach, pickup, curved inspection,
deposit and withdrawal. Vary each phase's duration and the inspection point from
the replicated occupation clock. Recorded torso/shoulder motion follows the same
phase. Original coins remain resting or rigidly pinched, with ownership changing
only while the hand is stationary at the original seat. Long quiet intervals and
interruptible work clocks remain. This improves measurable motion properties; it
does not establish subjective naturalness without hardware observation.

Foley uses two bounded mod-owned spatial AudioSources per resident, the existing
HeadEar claim, and the game's live master/effects levels. Native purchase,
enhancement and donation sound callbacks remain untouched. No new wire record,
voice generation, audio asset copy or per-frame scan is introduced. A pure contact
clock suppresses historical events on late join, authority/epoch changes, rewinds,
large seeks, frame stalls, disable and map exit. Actual sounds follow coin release,
spell onset and occasional cloth motion during attention/prayer changes.

Read-only evidence: `resources.assets` AudioMaster GameObject 5564,
AudioController component 11504. The original serialized item records directly
reference these clips:

| Original item ID | Clip | Path ID | Full length |
| --- | --- | ---: | ---: |
| PlaySound_ScenarioUIEquipmentToggle_Trinkets | SFX_UI_ToggleEquipmentTrinkets2 | 3144 | 0.776 s |
| PlaySound_ScenarioUIEquipmentToggle_Body | SFX_UI_ToggleEquipment_Body | 1599 | 1.172 s |
| PlaySound_ScenarioUIAugmentLight | SFX_UI_AugmentLight | 3829 | 3.625 s |

The helper resolves the original AudioItem's clip, without invoking AudioController.Play.
Quiet 0.65 s metal/cloth excerpts and a 2.8 s magical excerpt have a short end fade.
Waveform inspection confirms the excerpts include the attacks and largely decayed
tails. Playback timbre and relative loudness were not auditioned here; review them
in the headset, especially alongside narration and native transaction sounds.
The normal log only reports bounded missing-asset/failure anomalies.

Validation: final imported motion/activity checks 657,322 assertions; all 31
compiled negative controls pass. Portable phase/network/audio-clock cases also
pass. Resident lifecycle 153 assertions/10 controls; station setting/geometry
1,569 + station lifecycle 74 + grounding 243 assertions/16 controls; strict Release
zero warnings/errors. Native audio boundaries use real Unity AudioSources and
synthetic clip data, testing position, master/effects mute, lifetime and no duplicate
voices; they do not claim native sound timbre.

Full imported skin: merchant 1,602, priestess 1,219, mage 1,602 poses; no arm/torso
or opposite-arm intersections. Maximum cuff-seam growth: 0.123 / 4.307 / 0.141 mm;
all six penetration/seam negative controls were detected. Merchant geometry was
resampled after the final variable timing change. Furniture contact additionally
has 1,602 poses without torso penetration and catches the former thick rear slab.
Temporary evidence is under `/tmp/town552-motion-*`, `/tmp/town552-anatomy`,
`/tmp/town552-skin-*`, and `/tmp/town552-native-foley-evidence.json`.
