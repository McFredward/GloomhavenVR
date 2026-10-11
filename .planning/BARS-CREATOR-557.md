# Native character creation and overhead bars — ModBuild 557

The supplied local Player.log and LogOutput.log identify NPC feature ModBuild 577.
`abgeschnitten.jpg` shows the right side of the native character-creation view cut
off. In the same session, `New Party display`'s supersample reports the campaign
assembly view active, while every fixed-fit report says that no sub-view is open;
the last report records zero sub-view seats. The game's `AdventureCharacterCreator`
opens its own `UICharacterCreatorWindow` independently of the party selector's
window state; the party display also hides during later creation steps.
The fixed fit previously enumerated only the selector. It now includes the creator's
original window in the fit, open-set signature and pre-seat visibility hold. The
creator is placed beside the permanent character column without rescaling either.
The existing fixed-fit report will name the creator and any remaining spill in the
next headset test. A headset picture is still required to establish the final edge.

The reported overhead-bar settings have two source causes. `BarsOccluded=false`
restored native materials, whose global UI depth mode remains LEqual; the bars were
hidden by walls in both positions. The actor-bar presenter now gives each original
Graphic an instance of its material with explicit `unity_GUIZTestMode` and, where
declared, `_ZTestMode`: Always when the saved setting is off and LEqual when on.
The mode changes immediately, and pooled graphics are still collected by the
existing bounded rescan. The persisted key and its polarity are unchanged.

At extreme table zoom, the previous bar-follow factor was clamped to 0.7–1.5,
breaking the bar-to-figure ratio. New `[WorldUI] BarFollowFigureScale` defaults
to true and uses the uncapped factor for characters and enemies. Turning it off
restores the exact former clamp; `[WorldUI] BarFixedSize` remains independent.
The new option and the clarified wall-visibility label have English and German
product strings. This affects presentation only and adds no network record.

Validation: strict Release build, focused actor-bar source contract with negative
controls, options coverage and bilingual documentation. The full refactor guard's
source, runtime, wire, bundle and surface gates passed; its compiled-form diff
returns 1 because the stored baseline predates this change (115 changed and
115 added/removed types). No config key, patch registration or log marker was
removed.
The visual shader result and creator edge require hardware confirmation.
