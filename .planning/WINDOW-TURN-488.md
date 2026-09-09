# MB488 window release turn

The user requests a short visible turn toward the player when releasing a laser-grabbed
window. Both existing release owners assigned the facing rotation immediately:
`GrabbableModal` corrected the frame around its drawn ink centre, and `SurfaceGrabBar`
rotated about its centred frame origin. This is a source-proven instantaneous write;
no new hardware evidence was supplied for this request.

The release turn uses the existing grab-bar duration (150 ms by default) and cubic
ease-out. Capture the target at release; do not follow subsequent head movement.
Preserve the original pivot throughout the interpolation so an off-centre modal does
not travel sideways. A new grab must continue from the currently visible orientation.

`WindowReFacePolicy` still decides whether a release turns at all. In particular,
shared windows retain the user's explicit exemption recorded in that policy: their
orientation belongs to the whole room and must not turn toward the last mover.
Their existing network pose/easing path is therefore outside this release transition.
No new wire record, configuration setting or asset is required.

The modal pose lock recognizes only a short frame-count grace period after a grab.
An animation measured in seconds must explicitly identify its remaining writes as the
user's release movement, including at high refresh rates where that grace period is
shorter than 150 ms. Disable, destruction and external placement must cancel a pending turn.

Integration against dev `975f76e3` passes all 17 guard checkers and 249484 assertions,
with a strict Release build at zero warnings/errors. Documentation-language checks and
all 16 metadata-only reference checks pass. Configuration, patch and log-token surfaces
are unchanged. The compiled comparison changes the three existing animation/owner types,
adds one release-tween type, and changes seven other types only through the inlined build
number. The guard's exit1 reflects this intended compiled delta.

These automated checks do not verify the perceived timing or pivot stability in a
headset. The next test should include an off-centre window, immediate regrab, closing
during a turn, and release while the game's clock is paused.
