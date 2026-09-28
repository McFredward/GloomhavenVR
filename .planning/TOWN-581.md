# Town service and window input follow-up — ModBuild 581

## Hardware evidence and scope

The supplied local `Player.log` and `LogOutput.log` identify NPC ModBuild 580.
The available remote logs are older and cannot establish this run's peer view.
The maintainer observed three failures: the merchant looked at a visitor before
speaking, the enchantress could extend her hand without a card-placement cue,
and the laser passed through scenario story and combat-log close controls.
The latter blocked native continuation. No headset result exists yet for 581.

## Merchant greeting

The face chooses a visible visitor before the merchant's body enters attention.
Body entry is deliberately held until a gripped coin finishes its transfer.
Build 580 moved the greeting from native shop entry to the body-attention edge,
so the head could already look at a more distant visitor while speech still
waited. The face author's exact visitor decision now cues the shared line.
Later body motion or native shop entry cannot queue a duplicate. Leaving before
the line starts cancels it. Event-bounded Debug records expose queue and start.

## Enchantress approach

The second approach in the Build 580 log occurred while Merchant remained the
native guildmaster destination. The enhancement handoff retained intent but
required destination `None`, so it never opened the native enchantress service.
The physical approach now switches between idle town service destinations via
the existing native path, while preserving the selected character and deferring
to a live modal confirmation. The narrow temple/enchantress approach overlap
uses one stable nearest-resident decision so a later same-frame temple tick
cannot immediately steal the visit.

## Transparent controls

The NPC branch's visible-ink ray filter discarded alpha-zero graphics before
the normal uGUI raycast. The native story box uses a transparent full-area
click target to advance; mod close controls, including combat log X, use a
transparent hit plane under their painted button. A transparent graphic now
counts as a laser surface only when it is an active raycast target with a live
pointer handler. Passive transparent layout, hidden CanvasGroups and disabled
controls remain non-interactive. This applies to every window using the shared
ray path rather than a story-only exception.

## Verification

- Merchant schedule regression: a gaze with body attention still zero queues and
  starts one greeting; subsequent body attention/native entry does not repeat it;
  departure cancels a pending greeting. Town service codec suite passes.
- Transparent-control Unity 2021.3.5f1 harness: 101 assertions and eight
  negative controls pass, including story/close hit planes and hidden/decorative
  geometry.
- Enhancement and temple runtime checks: see final integration result below.
- Full Release build, wire and source gates: see final integration result below.

The merchant's visible hand-to-belly gap remains open. Its geometry findings
are documented in [TOWN-580.md](TOWN-580.md); this input/voice build does not
claim that the asset was fixed.
