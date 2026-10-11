# NPC hardware follow-up — ModBuild 574

The supplied `Player.log` and `LogOutput.log` both identify ModBuild 573, commit
`c3e318647`. Seven screenshots in the main checkout's
`.planning/debug/npc_probleme/` show the merchant's lost confirmation after a
stock-to-owned item change, crossed-looking priestess arms and merchant thumbs,
the enchantress's narrow vertical card effect, a loose rod beside the cabinet
lantern, and a disconnected branch under the enchantress worktop. No new video
was supplied for this test. The headset result of Build 574 remains unverified.

## Findings and corrections

- The native merchant confirmation `UIWindow` is pooled. The previous invisible
  mask could still be waiting through three render frames when the next card
  opened the same window. The new mask then became its child; retirement of the
  old mask destroyed the new buttons. Reuse now retires only that exact window's
  old wrapper before capturing the new parent. A Unity test clicks the newly
  opened sell button after the old retirement deadline.
- Unity reported `All cloth particles are fixed so the Cloth component is not
  initialized` on all three stand cloths. All particles started with zero
  movement allowance, so later hand contact could not start the solver. A
  submillimetre idle allowance cooks the solver at startup; the existing
  fingertip response and gravity/recovery stay intact. The cloth test now
  rejects that engine warning and its old all-fixed configuration.
- The previous pose render froze the native animation at one frame, and its
  checks compared markers rather than skin triangles. On the actual imported
  meshes, the old priestess sequence intersected arms/torso in 243 of 403
  frames, and the merchant's hip pose in 69 of 91 frames. Retargeted resting,
  prayer, bowl-cover and hip paths yielded zero measured skin intersections
  over the same sequences. Headset-height, overhead and side renders are in
  `.planning/debug/npc574_review/` in the main checkout. This is geometry
  evidence, not proof of final stereo appearance.
- The unused long cabinet riser and the unattached enchantress root stretcher
  were removed from the authored meshes. The functional lamp hook and cabinet
  mounting seat remain.
- The added voice clips made the ordinary-Git town bundle 310 KiB too large
  for GitHub's 100 MiB blob limit. The monastery stone floor normal map now
  imports at 512 rather than 1024 pixels; source art, resident faces, cabinet
  textures, and every voice clip remain intact. The Windows bundle is
  104,138,042 bytes (SHA-256
  `54dfde99ab29ce09cbc1c25b9802ec9b2db29037c8766cec4f477ba3fc6af8c6`).
  For future growth, the maintainer prefers multiple separately loaded bundles
  under the per-file limit instead of reducing more asset detail.
- The enchantress's original `UIEnhancementButtonHighlight` controls still
  belong to the game. The physical offered card's reclaim collider previously
  won the same laser pull before those controls could receive `OnClick`.
  Only a valid original ability-area hit on that exact offered card now bypasses
  its collider; the rest of the card remains reclaimable. The native full-card
  `GUI_LevelUp_Frame` is a sibling of the pooled print in the actual
  `CardHilight` asset. Its flat GUI animation compressed X when converted to a
  world canvas. The original frame is aligned to the complete physical card
  at the render boundary and made non-raycastable, while each native
  enhancement-area frame retains its own input and animation.
- Build 573 played `enchantress-invite` lines such as a request to hand over a
  card after `Offer` had already accepted one. Accepted cards now select one of
  five newly recorded inspection lines in the same resident voice. A card
  offer retires queued or speaking greetings/invitations; completion retires
  stale inspection lines. The existing private cosmetic voice relay accepts
  the new reaction, and TLV80 carries the chosen exact cue and mouth timing to
  observers. The five MiniMax Speech 2.8 HD calls were planned at about
  USD 0.03 at the provider's listed character price; no runtime API is used.

## Validation and remaining hardware checks

The Release compilation has zero errors and warnings. The native enchantment
handoff Unity fixture passed 1,143 runtime assertions and all 31 negative
controls, including a late flat-animation resquash. Laser arbitration passed
92 assertions and eight negative controls; the town voice fixture passed
3,055 assertions and 17 negative controls; the multiplayer voice relay passed
its focused production and negative test. The merchant handoff fixture passed
1,401 assertions and 32 negative controls. The cloth fixture loaded the final
Windows bundle, initialized all three solvers and measured 0.09595 m visible
contact motion against its zero-motion null control. The asset render check
passed 951,288 assertions and nine visual negative controls. The imported-skin
pose check passed 629,160 assertions and 46 negative controls in its worker
worktree. Both UnityFS bundle-format checks and developer-docs i18n passed.
The repository gate passed 14/14 source checks and 80/80 local suites with
four jobs. Its final `check --summary` exit code is 1 solely because this
feature branch's compiled form differs from the older private refactor
baseline (0 order-only moves, 129 changed and 214 added/removed types).
That baseline comparison is not a failed test. `git diff --check` passed.

For the next headset test:

1. Offer the merchant a stock card, immediately replace it with an owned card,
   and confirm/cancel. The buy/sell prompt and its buttons must reappear for
   every valid replacement.
2. Approach and leave the priestess both before and after donating. Watch the
   complete prayer-to-cover transition, the bowl-cover hand and the merchant's
   thumbs at the hips from several angles.
3. Brush each table cloth with a fingertip, release it, and check that it moves
   and settles. Verify the loose lantern rod and under-table branch are gone.
4. Offer the enchantress a card with multiple enhanceable regions. The full
   card frame should surround the card; click each original region with the
   laser without picking the card back up. Click outside a region to reclaim it.
5. In multiplayer, listen to the enchantress before and after offering a card,
   then complete an enhancement. All players should hear the same relevant line
   and see the same mouth movement.
