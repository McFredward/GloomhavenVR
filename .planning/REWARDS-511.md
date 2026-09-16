# Reward continuation retest and passive panel materials — ModBuild 511

## Evidence and corrected diagnosis

The latest maintainer logs identify dev ModBuild 510 / 1.0.2. The retained remote logs
are build 500 and do not verify current multiplayer behavior. The supplied gegenstand.jpg
shows the original item reward, a Continue footer, and a reward heading clipped at both
horizontal edges. The game remains blocked after the reported click.

Player.log records Tutorial Selector at line 1469, LoadCustomLevelCoroutine at 1697,
and the native reward showcase at 5654. SceneController.LoadCustomLevelCoroutine sets
FrontEndTutorial or SingleScenario. Both differ from Guildmaster, even though their
scenario reward manager uses the same UIRewardsManager window. Build 510 incorrectly
treated the manager class as proof of the global game mode.

UIRewardsManager.ConfirmPressed sets its local input latch directly only in Guildmaster.
In the other modes it calls LongConfirmHandler.Pressed, which also requires a physical
CONFIRM_ACTION_BUTTON edge. A uGUI VR click supplies no such edge. The old test stub
modeled ConfirmPressed as an unconditional latch, so it could not expose this deadlock.

The VR button now supplies that window's local confirmation input latch after checking
the original eligibility/ownership terms. This is the native coroutine's alternative to
MouseClickLeft.WasPressed; it is not nextRewardOverride or an authoritative game-state write.
ProcessRewards still owns group advancement, ProcessNextReward multiplayer actions,
EndProcess and onProcessEnded. CampaignScenarioRewardManager's separate original button path
retains its own callback. First hover, accepted input and consumed input are observable
at the ordinary log level.

The previous footer's stock ColorTint varied only slightly. It now uses explicit native
button visual states for pointer hover/press and disabled state. The source routing review
finds the normal laser/finger path: a raycastable Image, non-raycastable label, converted
GraphicRaycaster and standard pointer-interface dispatch. Headset hit delivery remains
part of the acceptance test, rather than being assumed from a direct handler invocation.

## Reward text framing

The 510 reward capture reports a 1380x1376 target at 2x density (LogOutput.log:783),
and incorrectly says no content extends beyond the host. Its bounds walk measures the
text RectTransform, which can be narrower than the glyphs TMP actually draws. The fix
retains original text, font, artwork, animation and native clipping. Only the exact native
rewardAnnouncementText contributes its live glyph bounds to capture and chrome. Four
transformed glyph corners expand the draw union after the original mask geometry is
established. Authored layout still supplies placement/scale metrics. Empty or invalid
meshes fall back to their layout box; the usual refresh observes the completed mesh.

## Separate build-509 user report

The external RTX 5090 session in debug/user is release 1.0.1 / build 509, not this retest.
Its Player.log:5916–5985 contains one per-window and four global supersampling exceptions.
Reading TMP_SubMeshUI.material instantiates a material from a null native source. The
failure policy then tears down approximately 159–160 MB of render targets repeatedly
and finally disables supersampling for the session.

PanelGraphicMaterial reads TMP_SubMeshUI.sharedMaterial or TMP_Text.fontSharedMaterial
without instantiating either. Ordinary graphics retain their actual material; only the
existing GrabPass backdrop remedy can change presentation. Capture and its diagnostic
material reader use the same helper. This closes the demonstrated exception/rebuild
trigger; the logs contain other FPS dips and do not establish that every hitch is fixed.

## Validation and hardware acceptance

Integrated source review found no additional blocking defect. The independent review
confirmed the tutorial/custom-mode initializer, actor-owned interactionChecker, native
ProcessRewards → EndProcess → onProcessEnded → choreographer queue release, and original
mask intersection. This is source evidence; the native iterator fixture substitutes Unity
presentation/action-bus calls and does not prove a headset click was delivered.

- Strict Release: zero warnings and errors.
- All 17 source checkers and production harnesses pass; wire suite: 253,759 assertions.
- Reward showcase: 299 assertions, 13 runtime negative controls, native iterator fixture
  verified against the local read-only game reference.
- Panel materials: 2,031 assertions, four runtime negative controls.
- Panel ink/heading/placement: 237 assertions, five runtime negative controls and one
  placement binding negative.
- Retained build-502 compiled comparison: 35 changed types, 25 additions, no removals.
  Relative to the build-510 review, PanelSupersample is the additional changed type;
  PanelGraphicMaterial, RewardContinueButton and RewardHeadingBounds are the additions.
  Its compiled diff contains only the passive material reader and exact heading bounds.
  Guard exit 1 denotes these reviewed compiled differences, not a failed checker.
- Config/patch/log surfaces remain 625 / 161 / 4,726; runtime patch inventory remains
  117 classes / 184 methods. Bundle unchanged: UnityFS 7 / Unity 2021.3.5f1 / 74,943,763 bytes.
- Bilingual docs, shell syntax and whitespace checks pass.

Retest the tutorial/custom-scenario chest, Guildmaster and Campaign continuation, visible
laser/poke hover and press, complete German/English headings, multiple reward groups,
and shared multiplayer ownership/continuation. Verify character-card browsing retains
supersampling without the material exceptions. Source tests do not establish headset
appearance or prove that the external user's unrelated FPS drops disappeared.
