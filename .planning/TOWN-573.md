# NPC hardware follow-up — ModBuild 573

The supplied Debug log and images are from ModBuild 572. The merchant log records a stock-card
offer, replacement by an owned card, and closure of the second native confirmation. It does not
identify the caller that hid that window. The screenshots show the left-side red cabinet appendage,
a detached lantern support, controls offset from their sculpted recesses, and two ability-card
prints over the enchantress's palm. The priestess video shows an arm moving across her torso and
an abrupt-looking pose change. None of these observations proves the new headset rendering.

## Source findings and corrections

- `UIWindowManager.Escape` and other native owners can hide the underlying `UIWindow` directly
  while `UIItemConfirmationBox.IsActive` remains true. Merchant handoff previously trusted only
  `IsActive`, retaining its old callback and blocking later offers. It now reconciles the wrapper
  after the hide animation, clears a canceled trade, preserves a new card through a transient
  native-tab rebuild, and retries an unexplained hide at most twice. Explicit confirm/cancel
  decisions never reopen the dialog.
- The merchant's experimental red side banner and its rod, rings, grip and cloth runner are
  removed from the authored mesh and prefab. The lantern flange meets the cabinet body; each
  category control uses the measured center of its corresponding imported sculpt recess. The
  runtime cloth contract now expects no merchant side runner, while keeping the two temple
  runners and enchantress cloth.
- The priestess's cover and return paths now use the imported shoulder, elbow and bowl geometry
  directly. The availability blend no longer pulls the arm through an additional hip pose.
- The enchantment card holder is the game's original `UIEnhancementCardHighlighter` and
  `UINewEnhancementWindow.HighlightButtons` supplies one animated, clickable game control per
  enhanceable ability container. Its converted world canvas remains on the physical offering
  for laser input. Only the duplicated pooled card artwork is masked while offered, with
  restoration when the card returns to the game pool. The actual card and original highlights
  remain synchronized to observers through their existing presentation modules.
- The enchantress invitation now follows a real native visit or accepted card offering. The
  prior animation-attention threshold could already have passed when sampling began and thus
  miss the voice line. The elected face author still chooses and publishes the exact cue to peers.

## Verification

- The Windows town bundle was rebuilt from the integrated assets for the full-install feature
  build: 104,430,645 bytes, SHA-256
  `d366782af1fc9a410966fa0c0c3af717cefbf6694415f6da22880788f7dfbcf6`.
  The Release compilation finished without warnings or errors. The full refactor guard passed
  14/14 source gates and 80/80 suites. It returns a nonzero comparison status because the
  feature branch adds NPC classes and assets absent from its older `dev` baseline; no suite
  failed. The merchant cabinet asset check passed 951,288 assertions and nine negative
  controls. Merchant handoff and catalog, priestess pose and activity (629,160 assertions,
  46 negative controls), cloth, native enchantment handoff, service mirror and voice checks
  also passed. Documentation/i18n and whitespace checks passed. Pose and cabinet renders
  are retained under the main checkout's `.planning/debug/npc573_review/`.
- The source and automated checks establish ownership, layout and transition math. They do not
  establish the headset picture, stereo depth or controller feel.

## Headset checklist

1. Give the merchant a stock card, replace it with an owned card, then confirm, cancel and try
   another offer. Also close a confirmation directly and revisit him. The target and buttons
   must remain usable throughout.
2. Inspect the cabinet from front and both sides. Verify the red side appendage is gone, the
   lantern attaches, and every category button sits inside its recess and remains clickable.
3. Watch the priestess enter/leave prayer and cover, including walking away during cover. Her
   arms should neither cross nor snap through the torso.
4. Offer the enchantress a card with multiple enhanceable regions. There should be one legible
   card and the game's animated highlights; point and click each valid region with the laser.
5. Approach the enchantress, offer and reclaim a card, and complete an enhancement. Confirm the
   appropriate voice response plays once and remains synchronized in multiplayer.
