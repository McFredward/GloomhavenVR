# MB487 original element animation output

## Evidence and cause

Both supplied hardware sessions identify ModBuild486. The owner log at `.planning/debug/LogOutput.log:13677` and peer log at `.planning/debug/remote/LogOutput.log:6279` report `native show animator does not expose original LeanTween settings`. The owner log then reports `ELEMENT PARITY:MIRROR BLANK via=NATIVE-PENDING` at line14014. These are refusal and blank-state diagnostics; they do not establish headset pixels.

`NativeUseBarAnimationBinding.Capture` supports original LeanTween setting bindings. The element's original creating/created animator can instead be `AnimationGUIAnimator`, backed by Unity legacy Animation. `AnimationExtensions.ResetToStart/End` calls Play, Sample and Stop, so stopped clip state cannot identify the currently held visual endpoint. Replaying clip time or declaring an unsupported animator to have no output would be incorrect.

## Change and ownership

The sampler reads bounded original element hierarchy output after native animation. The inert original prefab clone receives those channels after its existing record52 writers. No gameplay controller, animation callback, Play, Sample or clip event is invoked. Legacy animator bindings require record53 hierarchy output; unsupported original sprite identities refuse explicitly rather than substituting artwork.

Record52 bytes and grammar are unchanged. Optional canonical record53 pages append to message10, published atomically with52. An older52-only frame still decodes; a legacy-animation receiver cannot pretend that such a frame contains the missing output. Well-formed unknown future TLVs are skipped by length without weakening canonical known-page order, bounds or complete-frame validation.

Record53 contains six cells, each with at most32 original nodes. A node carries stable original hierarchy identity, parent/sibling, explicit component and active/enabled flags, local or anchored position, scale, normalized quaternion, RectTransform anchors/pivot/size, Graphic RGBA, independent CanvasRenderer RGBA, CanvasGroup alpha/enabled/ignoreParentGroups, Image fill plus separate base/override original sprite selectors, RawImage UV and TMP font size. Sprites resolve only through the original element prefab/configuration assets; the existing mip replacement map provides read-only asset identity. No new asset cache is retained.

The largest supported raw hierarchy is25351 bytes; the body bound is26000 and complete message bound40960. Lossless Deflate is used only when shorter. Exact bounded decompression rejects hidden compressed tails, expansion beyond claimed size and truncation. Field arity, finite geometry, near-unit quaternion, unique bindings/siblings and52/53 root coherence are validated before publication and before any clone write. Partial constructor failure releases clone-owned animation materials.

## Verification and limits

The strict Release build and focused wire vectors are run for this checkpoint; final counts and deliberate negative controls follow in the next checkpoint. Existing52 golden bytes remain a regression boundary. Hardware outcomes require the next headset test; no screenshot result is inferred from a green build.

This is a bounded element presentation readback, not a general Unity serializer. Existing52 continues to carry original `_FXAnim` output and LeanTween setting outputs. Arbitrary material/font-shader curves beyond those native bindings are not established by offline source inspection: the serialized original AnimationClip assets are unavailable here. The change fixes the source-proven unsupported legacy animator refusal and carries the original hierarchy's listed rendered channels; it does not claim that headless tests prove every shader pixel.
