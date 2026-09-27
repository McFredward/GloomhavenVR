# NPC hardware follow-up — ModBuild 577

The supplied `Player.log` and `LogOutput.log` identify ModBuild 576, commit
`b9d98e13b`, in a VirtualDesktopXR session on 2026-09-27. The supplied
merchant screenshot shows a card still parked at the merchant without its
buy/sell controls. The enchantress screenshot and video show that the offered
card's original circular effect changes apparent aspect with orientation; the
laser passes over the card without selecting an enhancement area. In the
priestess video, her forearm briefly crosses the bowl/torso during the blend
into the otherwise accepted idle pose. In the cloth video, a hand pushes into
the enchantress's red side drape while it stays largely planar. The Debug log
records alternating cloth-contact edges for that runner while the hand remains
near it. No remote log was supplied for this run.

## Source-grounded findings and corrections

- Merchant offers lost their `_tradeItem` state eight seconds after the card was
  placed, even when the native decision was still open. The timeout now starts
  only after the native confirmation. A rejected show may also leave the native
  wrapper active without a visible window; only a fully closed native window
  can be reconciled, without stealing another prompt or an outgoing fade.
  While a native decision remains open, its original controls are rebound if
  their physical surface was lost. An offer that cannot produce a native
  confirmation within three seconds returns its card to the source and emits
  one bounded warning per visit. The headset report establishes the symptom,
  and the source establishes these failure paths; the supplied log does not
  trace enough transaction state to identify the exact late failure path.
- The build-576 Debug log shows `Panel_TownService.11` discarded before uGUI
  selection because the physical offered card is the nearer solid hit (72.75
  versus 75.69 world-distance units in one recorded ray). The exception now
  asks the original native GraphicRaycaster for a live enhancement area first,
  then crosses only that exact offered card. It rescans the board, fan, resident,
  other town props and physical colliders up to the area plane; all other
  obstructions and unrelated windows retain ordinary occlusion. A synthetic
  reversed-Winding fixture initially suggested a GraphicRaycaster flag change,
  but the game logs did not support that as a cause. The flag change was
  discarded; a correctly oriented rendered Unity canvas hits the native area
  without it.
- The aura code measured the rendered effect in a rotated child but corrected
  scale on an ancestor's different axes. The original ink is now fitted on its
  own axes, with a world-basis correction for nonuniform and rotated ancestors.
  Native pulse changes remain live, repeated render callbacks do not accumulate
  scale, and the original effects are restored when the offer ends.

- The priestess's intermediate prayer-to-visitor pose shortened both arm
  targets toward the shoulder before lowering them, forcing the two-bone solver
  to swing its elbows sideways. The hand path now descends in front of her
  torso, while the elbow pole moves inward only during the transition. The
  accepted neutral pose and unavailable-donation return are unchanged. Actual
  imported-rig frames 30, 36 and 39 were rendered and reviewed, rather than
  judging endpoint target markers alone.
- The enchantress's lower red drape was authored up to 2.8 cm inside a modeled
  furniture root at rest. The source furniture and matching PhysX driver now
  place it clear of that root, while a vertical support stops later penetration.
  A short contact hold bridges alternating solver-side reads without dropping
  the visible contact episode. The narrow drape's rendered displacement is
  spatially smoothed to avoid a local inverted triangle/pinhole under a finger;
  physics collision remains native and the pinned table edge is unchanged.
  Rendered rest, top and held-contact images were reviewed. The connected
  table-top cloth edge can appear as a narrow strip from a low angle; the top
  view confirms that it is not a detached piece.

## Validation and headset checks

The merchant handoff fixture passed 1,440 production assertions and 41 active
negative controls. The new native transaction fixture passed 10 production
assertions and three active negative controls. The existing town-interaction
fixture passed 1,337 assertions, and the isolated Release build completed with
zero errors and warnings. Remaining integrated gates and headset checks follow
after the other lanes join. Source checks cannot establish the appearance or
physical interaction in a headset.

The final enchantress Unity runtime passed 1,187 assertions and 37 negative
mutations. An OpenGL render run passed 1,188 assertions, including a real native
GraphicRaycaster area hit without changing its Winding flag. The physical
collider rescan uses a reusable 64-hit buffer and fails closed if full; its
positive, foreign-blocker, overflow and buffer-reuse cases passed.

The imported priestess rig passed 629,160 frame/pose assertions. The cloth
fixture passed 27 negative source controls. Its final 90-frame gradual contact
showed no root penetration, about 5.4 cm local finger displacement and 11.57
mm maximum visible edge stretch, below the new 15 mm bound. The authored rest
gap from the real root surface is 24.47 mm. The updated art bundle passed the
UnityFS format check as Unity 2021.3.5f1 format 7; it is 101,845,184 bytes.
This build therefore requires a full install with the updated `ghvr-town.bundle`.
The integrated project checks and the next headset verification remain open.
