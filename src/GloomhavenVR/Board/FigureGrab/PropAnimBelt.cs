using System.Collections.Generic;
using EPOOutline;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// A HELD PROP HOLDS STILL — the hush. <see cref="PropAnimWatch"/> is the instrument half and
/// writes nothing; this class writes and logs nothing that gates it.
///
/// <para><b>READ THIS FIRST: THIS FILE'S PURPOSE WAS INVERTED ON 2026-09-06, AND THE NAME IS A
/// LEFTOVER.</b> Until then this class existed to make a held chest KEEP animating — it wrote
/// <c>Animator.cullingMode = AlwaysAnimate</c> and re-ran the game's world-anchor feeders, and its
/// header argued at length that stopping the animation would be a workaround. That is no longer
/// what it does or what it is for. It now STOPS the animation for the length of the hold, on the
/// user's explicit instruction, and the paragraphs below say why that instruction is the right
/// call and not a retreat. The type is still called <c>PropAnimBelt</c> only because its call
/// sites live in <c>GrabbableProp.cs</c> and <c>PropGrab.cs</c>, which this lane does not own; read
/// "belt" as the belt that holds the thing STILL. The full four-round record is
/// <c>.planning/held-prop-flash-experiments.md</c> and it should be read before anything here is changed.</para>
///
/// <para><b>THE REPORT, 2026-09-06, verbatim.</b> <i>"Das Aufleuchten der Truhen und Fallen
/// funktioniert immer noch nicht richtig in der Hand. […] ich will erstmal, um es einfach zu
/// halten, diese Animation gar nicht mehr in der Hand haben stattdessen."</i> That is a decision,
/// not a bug report: four rounds failed to make the flash play correctly in a palm, so the flash
/// is to be absent from the palm instead. Simplicity over a correct animation, chosen explicitly.
/// A prop's attention-getting blink exists to draw the eye to a chest ON THE BOARD; a chest the
/// player is already holding in their hand needs no help being noticed.</para>
///
/// <para><b>ROUND FIVE, 2026-09-05: THE HUSH FIRED AND THE SHIMMER SURVIVED IT. WHAT FOLLOWS IS
/// WHY, AND IT IS THE WHOLE REASON THIS FILE GREW.</b> The user, verbatim: <i>"Die
/// Spiel-Highlighting Animation von Fallen und Truhen (dieser weiße Schimmer) ist immer noch auch
/// auf dem Asset sichtbar wenn es in der Hand ist. Das soll nicht der Fall sein."</i> The ModBuild
/// 447 logs from both machines say the suppression genuinely ran — <c>HELD-PROP ANIMATION HUSH for
/// 'BearTrap' Trap</c> reports <c>1 animator switched off</c> and <c>1 Outlinable switched off</c>
/// on the host and the same on the peer. So the hush is silencing something that is not what the
/// eye sees, which is a shape this project has written down twice: <i>"a fix passed its own green
/// readings and changed nothing — the instruments measured the bookkeeping"</i>.</para>
///
/// <para><b>THE 447 LOG ALSO RETIRED THE OLD "NOTHING WAS ANIMATING IN EITHER WINDOW" VERDICT,
/// AND THE HEADER USED TO REPEAT IT AS IF IT WERE STILL TRUE.</b> On ModBuild 447 the same
/// instrument reads, for the trap: <c>'Trap_BearTrap_PR' clipRate hand=n/a home=0.202/s</c>,
/// <c>anyLayerRate home=0.201/s</c>, <c>advancing hand=0/655 home=359/360</c>,
/// <c>layerCount hand=0 home=1</c>. Read it in order: the prop's animator runs a CONTINUOUS,
/// LOOPING clip while it stands on its hex — 359 of 360 home frames advancing, a ~5 s loop — and
/// in the hand it is stopped dead, which is this class doing its job (a disabled Animator reports
/// <c>layerCount 0</c>, which is why the hand column reads <c>n/a</c>). The old header's claim
/// that the animator was idle in BOTH windows was a reading of ModBuild 435/436 and it is no
/// longer the state of the evidence. The animator strand WORKS. It is simply not the shimmer.</para>
///
/// <para><b>AND THE MATERIAL READ-BACK THAT SAID "NOTHING MOVED" CANNOT CARRY THAT WEIGHT: IT IS
/// TRUNCATED, ON THIS EXACT SHADER, FOR THE SECOND TIME IN THIS PROJECT.</b> The A/B line's own
/// population clause reads <c>material(s) 1/1 on shader 'Amp_Char_Shader'; shader propert(y/ies)
/// 57 declared, 20 float/range/colour tracked</c> — <see cref="PropAnimWatch"/> samples the FIRST
/// TWENTY properties of a fifty-seven-property shader. ModBuild 151 lost a build to precisely
/// this: <i>"the census that was supposed to catch all this had a cap of 24 properties, while
/// Amp_Char_Shader declares exactly 24 interesting ones — so every dump was truncated precisely
/// where <c>_MOD_TINT</c> would have appeared"</i>. "Not one of the tracked slots moved" is
/// therefore not evidence that no material property moved. <see cref="EmitPostHushVerdict"/> reads
/// the whole table.</para>
///
/// <para><b>WHAT ACTUALLY PAINTS ON A HELD PROP THAT NO INSTRUMENT HAS EVER MEASURED AND NO
/// SUPPRESSION HAS EVER TOUCHED: LIGHTS.</b> The hush's two sweeps are TYPED on <c>Animator</c>
/// and <c>Outlinable</c>. The census's histogram is <c>GetComponentsInChildren&lt;MonoBehaviour&gt;</c>
/// — and <b><c>Light</c> derives from <c>Behaviour</c>, not from <c>MonoBehaviour</c></b>, so a
/// light is structurally invisible to it and appears only as the bare count at the end of the
/// line. That count is not zero: the 447 census reads <c>2 light(s)</c> under the held
/// <c>'BearTrap'</c> and <c>3 light(s)</c> under the held <c>'GoldPile'</c>, and the MonoBehaviour
/// histogram beside them names <c>RFX4_LightCurves x2</c> and <c>RFX4_LightCurves x3</c> — one
/// curve per light. <c>RFX4_LightCurves.Update</c> (decompiled :30-49) writes
/// <c>lightSource.intensity = LightCurve.Evaluate(t) * GraphIntensityMultiplier</c> EVERY FRAME
/// off <c>Time.time</c>, and re-seeds <c>startTime</c> for ever when its <c>IsLoop</c> is set. A
/// looping curve on a light's intensity IS a shimmer; it is not an animator, not an outline, not a
/// material property and not a renderer, which is exactly why four rounds of animator-and-material
/// measurement came back empty in BOTH windows.</para>
///
/// <para>This project has already paid for that blind spot once, on this codebase, in ModBuild
/// 151: <i>"A LIVE POINT LIGHT INSIDE THE CREATURE. 'LivingSpirit_Light (1)', Point, intensity
/// 20.00, range 1.0 m, parented in the prefab — a lamp centimetres from its own face. Strip's
/// sweep is GetComponentsInChildren&lt;MonoBehaviour&gt;() and Light derives from Behaviour, not
/// MonoBehaviour, so the sweep walked past it structurally."</i> The same sentence describes this
/// file before this build. And the geometry is the aggravating factor: a lamp that is unremarkable
/// on a prop lying on a hex a metre and a half away is a lamp held twenty centimetres from the eye
/// once the player picks the prop up — which is why the report is about the HAND and why nobody
/// has ever complained about it on the board.</para>
///
/// <para><b>WHAT IS SUPPRESSED (3), THEREFORE: EVERY <c>Behaviour</c>-DERIVED EMITTER —
/// <c>Light</c>, <c>Projector</c>, <c>LensFlare</c> — VIA <c>enabled</c>, ledgered per object.</b>
/// The class is taken whole rather than just <c>Light</c> because the type boundary is the defect:
/// naming one more type by hand is how the next one gets missed. None of the three is a renderer,
/// none carries game state, and each one only ever paints. <c>RFX4_LightCurves</c> is deliberately
/// NOT the thing switched off — disabling the WRITER freezes the intensity at whatever the curve
/// last wrote, which is a pinned shimmer rather than no shimmer; disabling the LIGHT removes the
/// picture whoever writes the number, and on release the light comes back at whatever value the
/// curve has meanwhile reached, which is exactly what the game would have had. Own the final
/// value, do not win a write war.</para>
///
/// <para><b>WHAT IS SUPPRESSED (4): PARTICLE SYSTEMS THAT WERE ACTUALLY PLAYING.</b>
/// <c>Stop(withChildren, StopEmittingAndClear)</c> on the ones found PLAYING at the walk, and
/// <c>Play</c> only on those same ones at the landing. Clearing rather than pausing is the point:
/// a paused system leaves its live particles hanging in the air, which is a frozen shimmer and not
/// an absent one. A system that was already stopped is never touched and never restarted, so the
/// restore cannot start an effect the game had finished with — and each one is re-checked at the
/// landing for still being a child of the visual, because these objects are POOLED
/// (<c>SpawnPFXOnEnable</c> hands them to <c>ObjectPool.Recycle</c>) and a recycled object may by
/// then belong to something else entirely.</para>
///
/// <para><b>WHAT IS SUPPRESSED (5), AND IT IS THE FIRST ONE THAT DOES NOT PAINT FROM INSIDE THE
/// PROP: THE SCREEN-SPACE OCCLUSION REGISTRATION (<c>ObjectOcclusionVolume</c>, 2026-09-06).</b>
/// ModBuild 448 shipped strands 1-4 and then measured the picture AFTER them, which is the whole
/// point of <see cref="EmitPostHushVerdict"/>. On both machines the answer was the same: over 358
/// sampled frames on a held gold pile the animators, outlines, lights and particles were at ZERO
/// with NON-ZERO pre-counts, not one of 192 material property slots moved — and the user reported
/// the effect unchanged. That is this file's own written falsifier firing: <i>"the shimmer is then
/// being painted by something that is NOT under this prop's subtree at all, which is the one place
/// no instrument in this file can look."</i></para>
///
/// <para>So the sixth round looked there. <c>ObjectOcclusionVolume.OnEnable</c> is one line —
/// <c>TilesOcclusionGenerator.s_Instance.AddObjectRenderer(GetComponent&lt;MeshRenderer&gt;())</c>
/// — and the generator is a component ON A CAMERA holding a <c>CommandBuffer</c> at
/// <c>CameraEvent.BeforeGBuffer</c> that draws every registered renderer with a replacement
/// material into a QUARTER-resolution target, blurs it twice, and publishes it as the GLOBAL
/// texture <c>_ObjectOcclusion</c> beside <c>_TilesOcclusionMap</c> and <c>_EnableOcclusionMap</c>
/// (decompiled <c>GH.Runtime/TilesOcclusionGenerator.cs:150-193</c>). Every term in that sentence
/// is outside the prop: the list is a scene singleton's, the draw is a camera's, and the result
/// arrives at the shader as GLOBAL state, which no <c>material.Get*</c> read-back can see. It is
/// not an Animator, a Light, a Projector, a LensFlare, a ParticleSystem, an Outlinable or a
/// material property — i.e. it is disjoint from every class four rounds of instruments sampled,
/// and ModBuild 448's own <c>HELD?</c> line had already named "the prop shader's own screen-space
/// occlusion term" as the next suspect without being able to test it.</para>
///
/// <para>WHY IT IS A HAND DEFECT AND NOT A BOARD ONE, which is the term that makes it fit the
/// report rather than merely fit the code: on its hex the prop's footprint in that map is small
/// and STILL, so the value sampled back is effectively constant and nobody has ever complained.
/// In a palm the prop fills a large part of the eye and MOVES every frame, so its own blurred
/// quarter-resolution silhouette sweeps across it — a soft moving wash with no animator, no lamp,
/// no particle and no material of its own behind it. That is a "highlighting/Licht-Effekt".</para>
///
/// <para>TAKEN THROUGH THE GAME'S OWN LIFECYCLE. The component's <c>enabled</c> flag is what is
/// written, not the generator's list: <c>OnDisable</c> IS <c>RemoveObjectRenderer</c> and
/// <c>OnEnable</c> IS <c>AddObjectRenderer</c>, and both set <c>m_RenderersUpdated</c> so the
/// command buffer is rebuilt. So one ledgered bool is the whole change and the whole undo, no game
/// state is written, and a volume that was already off is left alone. It shares the
/// <see cref="Belt.Emitters"/> ledger with strand 3 on purpose — "switch a <c>Behaviour</c> off,
/// remember what it was, write it back" is ONE restore, and a second copy of it would be a second
/// place for the next fix to land on only one of.</para>
///
/// <para><b>ROUND SEVEN, AND STRAND 6 IS THIS CLASS TURNED ON ITSELF.</b> ModBuild 450 read the
/// WORKING shape of strand 5 for the trap — one volume, two enabled beforehand, zero still
/// registered, the map live on 359 of 360 frames — and the user still reported it, in new words:
/// <i>"Fallen und Truhen werden immer noch manchmal WEISS wegen dieser Aufblitzen-Animation wenn
/// sie in der Hand sind."</i> Read the two halves of that sentence against the log and they name a
/// mechanism this file had never considered, because the mechanism is OURS.</para>
///
/// <para><c>Animator.enabled = false</c> does not undo a clip, it stops the clip WHERE IT IS. The
/// trap's idle is one looping clip advancing at 0.202 normalized/s on 359 of 360 frames while it
/// stands on its hex (the ModBuild 447 A/B line), i.e. a ~5 s cycle, and an attention flash is a
/// short bright part of such a cycle. Grab during the bright part and the ModBuild 445 hush LATCHES
/// the bright part for the whole hold; grab anywhere else and it does not. That is the user's
/// <i>manchmal</i>, it is <i>weiß</i> rather than a shimmer because it is a HELD value rather than
/// an animation, and — this is the part that cost six rounds — it is invisible to every reading
/// this file has ever taken, because all of them measure whether something MOVED and a latched
/// value does not move. "0 of 96 slots moved on any frame" is exactly what a prop frozen white
/// prints.</para>
///
/// <para>THE REMEDY IS ONE CALL, AND IT IS DELIBERATELY NOT <c>Play(state, layer, 0f)</c>: replaying
/// a state RE-ENTERS it and re-entering fires every <c>StateMachineBehaviour</c> on it, which on
/// these props is how <c>DelayedDeactivatePropAnimSMB</c> sends a rules message. <see
/// cref="RewindAndStop"/> calls <c>Animator.WriteDefaultValues</c> instead — the values the
/// animator recorded when it bound the controller, written to every channel the controller
/// animates, touching no state machine, firing no behaviour and no animation event — and only then
/// switches the animator off, so the frozen frame is the resting one. Nothing here needs to know
/// WHICH channel the flash lives in, and that is the point: the 450 read-back proves it is none of
/// the 48 float/range/colour properties while the clip advances on 359 of 360 home frames, so it is
/// something else the clip drives, and one call returns all of them at once.</para>
///
/// <para>THE RESTORE IS THE GAME'S OWN WRITER AND NOT A REMEMBERED COPY. <see cref="Restore"/>
/// hands the <c>enabled</c> flag back; the animator resumes from the state it retained and drives
/// every one of those channels itself on the first frame it evaluates, over our defaults. So the
/// highlight returns exactly as before by construction, and there is no saved value that could be
/// restored over somebody else's write — the recorded incident this project has already paid for
/// once. Its falsifier is counted rather than asserted, in
/// <see cref="Belt.RestoreForeignAnimators"/>.</para>
///
/// <para>IT REACHES THE MIRRORED COPY BY CONSTRUCTION, like every other strand: <c>NetProps</c>
/// calls <see cref="Engage"/> and <see cref="Release"/> for a REMOTE hold too, so a peer's mirrored
/// prop goes through the same <see cref="Apply"/>, the same ledger and the same restore. No wire
/// field, no second code path.</para>
///
/// <para>AND THE INSTRUMENT NOW LOOKS OUTWARD TOO (<see cref="AppendOutward"/>), because a fix
/// aimed outside the subtree cannot be verified by a census rooted inside it. The post-hush
/// verdict reports whether this prop is still IN the generator's list, whether the map is still
/// bound and still REBINDING frame to frame, what <c>_EnableOcclusionMap</c> reads, and every
/// <c>Light</c> in the SCENE standing near the prop but not under it — the arm that can see a lamp
/// on the hand or the rig, or a pooled effect parented to the scene and merely positioned to
/// follow the prop.</para>
///
/// <para><b>THE PRE-STATE OF EVERY CLASS IS COUNTED SEPARATELY, AND THAT IS WHAT KEEPS THE NEXT
/// ROUND ABLE TO ATTRIBUTE.</b> Two new suppressions ship together here, which normally means a
/// round that cannot say which one worked. It is answerable anyway because
/// <see cref="Announce"/> prints, per class, how many objects were LIVE before this class wrote
/// anything: <c>N light(s) of which M were ENABLED</c>, <c>K particle system(s) of which J were
/// PLAYING</c>. A class whose pre-count is zero made no writes and cannot be why anything changed,
/// in either direction.</para>
///
/// <para><b>THE SAME LOG ALSO UNDERCUT THE OLD BELT'S OWN MECHANISM, WHICH IS THE OTHER HALF OF
/// WHY IT IS GONE.</b> The belt DID apply — <c>cullingMode hand=AlwaysAnimate
/// home=CullUpdateTransforms</c>, <c>updateWhenOffscreen hand=3/3 home=0/3</c>, and at the grab
/// 3 skinned renderers were at <c>updateWhenOffscreen=false</c> with 1 of 3 animators not already
/// <c>AlwaysAnimate</c>, so its preconditions genuinely existed. But the term its argument rested
/// on read the SAME on both sides: <c>worst gap between a renderer's culling bounds centre and its
/// own transform hand=0.237 wu home=0.237 wu</c>. The reparent did not leave the bounds behind at
/// all, so the "Unity thinks the prop is still on its hex" story was never load-bearing.</para>
///
/// <para><b>WHAT IS SUPPRESSED (1): THE ANIMATOR, VIA <c>enabled</c>.</b> Every
/// <c>Animator</c> under the visual is switched off for the hold and switched back on with the
/// exact value it had, object for object. <c>Animator.cullingMode</c> is no longer written at all:
/// Unity does not evaluate a disabled animator, so the culling mode would decide nothing, and a
/// replaced value with no effect is one more thing that can be handed back wrong.</para>
///
/// <para><b>THE ONE ANIMATOR THIS REFUSES TO TOUCH, AND WHY IT IS NOT AN OVERSIGHT.</b> An
/// <c>Animator</c> whose controller carries a <see cref="DelayedDeactivatePropAnimSMB"/> is left
/// running and completely untouched. That state behaviour is NOT a look: its
/// <c>OnStateUpdate</c> counts a delay down and then calls <c>DeactivateProp</c>, which sends
/// <c>CDeactivatePropAnim_MessageData</c> into <c>ScenarioRuleClient.MessageHandler</c>
/// (DelayedDeactivatePropAnimSMB.cs:102-141) — a RULES message, for a sprung trap. It also holds
/// itself in a static list behind <c>DelayedDeactivationsAreInProgress()</c> (:144-151), which the
/// game polls to decide whether it may proceed. Freezing that animator mid-countdown would stall a
/// rules message and leave a global "still in progress" true for as long as the player keeps hold
/// of the prop — a phase deadline waiting to be missed, produced by a rendering lane writing game
/// state by accident. The user asked for the blink to stop, not for the trap to stop springing.
/// The count of animators skipped for this reason is in the census line, so a hold where it
/// mattered is visible in the log rather than inferred.</para>
///
/// <para><b>WHAT IS SUPPRESSED (2): <c>EPOOutline.Outlinable</c>, VIA THE COMPONENT'S OWN
/// <c>enabled</c> FLAG — AND THE FIELD CHOICE IS THE WHOLE POINT.</b> The game raises a glowing
/// silhouette around a hovered board object through
/// <c>WorldspaceUITools.EnableHoveredOutline</c> (decompiled WorldspaceUITools.cs:156-163), which
/// writes <c>outlinable.OutlineParameters.Enabled = true</c>. Writing <c>false</c> back into that
/// same property is the obvious move and it is the wrong one, for two independent reasons:</para>
/// <list type="number">
///   <item><b>It has nine other writers.</b> <c>OutlineParameters.Enabled</c> is assigned in NINE
///   places in <c>WorldspaceUITools</c> alone (:52, :71, :76, :147, :161, :172, :187, :198, :207)
///   — hover on, hover off, ability focus on and off, and a global <c>ActivateAllOutlines</c>
///   sweep. A single write at the grab edge would be re-stomped by the next hover the frame after,
///   and holding it down would be a per-frame write war against the game over a flag the game
///   believes it owns. This project has a standing ruling about that shape: concede the flag.</item>
///   <item><b>It only silences one of three.</b> An <c>Outlinable</c> carries THREE independent
///   parameter blocks — <c>OutlineParameters</c>, <c>FrontParameters</c> and
///   <c>BackParameters</c> (Outlinable.cs:160-184) — and clearing one leaves the other two free to
///   draw.</item>
/// </list>
///
/// <para>The component's own <c>enabled</c> flag has NEITHER problem. Grep the whole decompiled
/// tree for a write to an <c>Outlinable</c>'s <c>enabled</c> and nothing comes back: the game
/// never touches it, so there is no writer to fight. And it is the switch the outline system
/// itself is built on — <c>Outlinable.OnDisable</c> removes the component from the static
/// <c>outlinables</c> list the outline pass walks, and <c>UpdateVisibility</c>'s very first test is
/// <c>if (!enabled) { outlinables.Remove(this); return; }</c> (Outlinable.cs:236-245, 264-270). One
/// bool removes the object from the pass entirely, whatever any of the three parameter blocks say.
/// Handing the bool back re-runs the component's own <c>OnEnable</c>, which re-registers it at
/// whatever <c>OutlineParameters.Enabled</c> the GAME has meanwhile decided on. The restore is
/// therefore exact without this mod ever having read or written the game's own field.</para>
///
/// <para><b>WHETHER A HELD CHEST EVEN CARRIES ONE IS NOT KNOWN, AND IS NOT GUESSED.</b> No round
/// has ever counted <c>Outlinable</c> components on a prop; the only census this project has of
/// them is on CREATURES (Core/Haunt/HauntFigures.Clone.cs:101-118). If the count is zero this
/// strand makes zero writes and costs one list walk, and the census line says <c>0</c> — which is
/// itself the answer to a standing question. Do not read this file as a claim that the outline was
/// the flash.</para>
///
/// <para><b>WHAT IS DELIBERATELY STILL WRITTEN: <c>SkinnedMeshRenderer.updateWhenOffscreen</c>.</b>
/// This one survived the inversion because it was never an animation term. It decides whether a
/// skinned mesh is DRAWN AT ALL, by deciding whether Unity culls it against the pose it is in or
/// against authored <c>localBounds</c> carried by the root bone. Freezing the animator makes those
/// bounds MORE stale rather than less, so if anything the case for it is stronger now than it was
/// when the belt still ran the other way. A held prop that vanishes out of the player's hand
/// because a stale bounding box left the frustum would be a far worse defect than the blink this
/// change removes. It is written once per hold and handed back at the landing.</para>
///
/// <para><b>WHAT WAS REMOVED WITH THE INVERSION.</b> The world-anchor strand is gone. It re-ran
/// <c>ZephyrAnim.OnEnable</c> and <c>ObjectPosToMaterial.OnEnable</c> on a moving held prop by
/// toggling their <c>enabled</c> flags, so that a shader sweep anchored on the object's world
/// position would follow it into the palm. Its entire purpose was to make a sweep look RIGHT in
/// the hand; there is no longer a sweep to make look right, and it was the riskiest write in the
/// file (a component that threw out of its own <c>OnEnable</c> had to be caught and re-armed by
/// hand). The three feeder types are still COUNTED for the census — <c>PosToMat</c> especially,
/// because it pushes <c>_ObjPosY</c> into a material every single frame on its own
/// (decompiled PosToMat.cs:5-11) and is therefore a live per-frame material writer on a held prop
/// that no round has yet ruled in or out.</para>
///
/// <para><b>NOTHING HERE WRITES GAME STATE.</b> No <c>GameObject.activeSelf</c>, no
/// <c>Renderer.enabled</c>, no <c>Animator.speed</c>, no material property, no animator parameter,
/// and — see above — no animator that drives a rules message. Every field written is a per-client
/// rendering switch on this client's own copy of the prop.</para>
///
/// <para><b>EVERY SUPPRESSION HAS AN EXACT RESTORE, AND THAT IS THE LOAD-BEARING INVARIANT.</b>
/// A prop that came back out of a hand with its animation permanently off would be a worse defect
/// than the one being fixed. So: every replaced value is remembered per OBJECT in a parallel
/// ledger and never guessed or shared; <see cref="Restore"/> walks the ledgers and writes each one
/// back; both landing paths call <see cref="Release"/> (<c>GrabbableProp.FinishGlide</c> and
/// <c>GrabbableProp.Restore</c>); <see cref="ReleaseAll"/> covers a scenario change, the feature
/// dial going off and uninstall; <see cref="Reset"/> is <see cref="ReleaseAll"/> plus the log
/// budget; <see cref="Tick"/> runs ABOVE the feature gate for exactly this reason; and
/// <see cref="Engage"/> is idempotent per visual, so a re-grab during the release glide finds the
/// existing ledger and keeps it rather than overwriting the original values with the suppressed
/// ones — which would make the restore a no-op and strand the animator off for the session.</para>
///
/// <para><b>MULTIPLAYER — AND THE SENTENCE THAT USED TO STAND HERE WAS WRONG.</b> It read "a peer
/// does not render a held prop at all". A peer DOES: <c>NetProps</c> mirrors another player's held
/// prop into their hand (<c>[Props] HELD-PROP MIRROR</c>, 108 lines on the host and 135 on the
/// co-player in the 447 session) and it calls <see cref="Engage"/> at <c>NetProps.cs:288</c> and
/// <see cref="Release"/> at <c>NetProps.cs:550</c>, on the same <see cref="Live"/> ledger and the
/// same <see cref="Tick"/>. That is what makes this correction 1:1 by construction rather than by
/// promise: every suppression added to <see cref="Apply"/> reaches the mirrored copy on the peer's
/// machine through the SAME call, so a shimmer removed from the prop in my hand is removed from
/// the prop the peer sees in my hand, with no second code path to keep in step and no wire field.
/// Nothing here goes on the wire and no wire field is needed. Every field written is a per-client
/// rendering switch: whether Unity evaluates an animator, whether an outline pass walks a
/// component, whether a lamp is on, where Unity thinks a skinned mesh is. None of them changes the prop's transform, the
/// rules state, or anything a peer could observe. The state that justifies the suppression (a prop
/// in a hand) is itself local, which is exactly why the correction is local too. The whole-board
/// opt-out remains <c>[Net] RemoteBoards</c>; there is no per-sub-feature sync setting and this
/// needs none.</para>
///
/// <para><b>COST.</b> Three <c>GetComponentsInChildren</c> walks per belt, then again at most every
/// <see cref="RescanFrames"/> frames, over a prop subtree of under two dozen nodes, for at most the
/// two props a pair of hands can hold. <see cref="Tick"/> is a <c>Count</c> compare when nothing is
/// held and one frame-counter compare per held prop otherwise. No allocation on the per-frame path.
/// </para>
///
/// <para><b>ROUND EIGHT, 2026-09-06 — READ .planning/held-prop-flash-experiments.md §13 BEFORE
/// TOUCHING ANY STRAND HERE. STRAND 6 IS INERT AND THE STREAMING LEAD IS DEAD.</b> The ModBuild
/// 453 log reads <c>0 of 67 (material, property) slot(s) changed value</c> across the rewind,
/// which is strand 6 INERT by the falsifier <see cref="AppendRewind"/> prints for itself: the
/// clip was already at rest at the grab, so the freeze was latching nothing. This repo's own
/// <c>] [Perf] TEX</c> line reads <c>streamingMipmaps=False</c> (this mod forces it off), so
/// texture mip streaming cannot be the settle either. Every class this file suppresses reads
/// dark over 169 frames and the user still sees the white. What is left is not a component
/// state at all but three identities a COUNT cannot give — WHICH renderer of three is the one
/// drawing, whether the <c>GloomhavenVR/Overlay</c> material on the held prop is ours painting
/// it, and whether the decay in his video is a function of TIME or of the POSE of the thing in
/// his hand. <see cref="EmitRoster"/> is that reading and it writes nothing; round eight
/// shipped NO remedy on purpose, because every candidate left is measured by the thing that
/// would fix it (§13.6).</para>
/// </summary>
internal static class PropAnimBelt
{
    /// <summary>Frames between re-walks of a suppressed prop's subtree. Apparance content is
    /// destroyed and re-instantiated on a refresh (that is the whole of ModBuild 349), and a fresh
    /// animator arrives ENABLED — so a suppression written once at the grab can quietly stop
    /// covering the objects that are actually drawing. The hold also FREEZES
    /// <c>MonitorMovement</c> (GrabbableProp.FreezeApparance), which makes a rebuild mid-hold
    /// unlikely rather than impossible; half a second is cheap insurance against the case that
    /// survives.</summary>
    private const int RescanFrames = 45;

    /// <summary>How many census lines a session prints IN TOTAL.</summary>
    private const int LogBudget = 4;

    /// <summary>Prop KINDS the roster remembers. The census is the round's only new evidence and
    /// the report names two kinds — a chest AND a trap — so one budget spent entirely on chests
    /// would answer half the question. A kind that has already printed is skipped, which is what
    /// makes the total budget reach a second kind.</summary>
    private const int KindCap = 6;

    /// <summary>Component TYPE NAMES listed by the census before it stops naming them. The total
    /// distinct count is printed either way: a truncated list is not absence, and this project has
    /// already read an ellipsis as proof that something never appeared.</summary>
    private const int TypeListCap = 24;

    // ---- the POST-HUSH verdict (round five) ---------------------------------------------------
    //
    // The grab-edge census says what was suppressed. It cannot say whether the PICTURE stopped,
    // and the whole finding of round five is that the census was green while the user still saw
    // the shimmer. So a second window opens AFTER the hush has written everything it is going to
    // write, and reports what is still moving and what is still painting. A line that only says
    // "I hushed it" is worth nothing here; this one is worth something only because it observes
    // the prop afterwards.

    /// <summary>Frames the post-hush window samples — four seconds at 90 Hz, deliberately the same
    /// length as <see cref="PropAnimWatch"/>'s HOME window. The trap's own clip loops at
    /// ~0.20 normalized/s on its hex (ModBuild 447), i.e. a five-second period, so a window much
    /// shorter than this could miss a whole cycle and report an honest zero that reads exactly like
    /// a stopped animation. A hold that ends sooner emits what it has, with the frame count.</summary>
    private const int VerdictFrames = 360;

    /// <summary>How many post-hush verdicts a session prints IN TOTAL, at most one per prop kind.</summary>
    private const int VerdictBudget = 3;

    /// <summary>Materials whose FULL property table is read back per frame. Sixteen, not four: the
    /// old line printed "N material(s) on shader 'X'" with no population beside N, so a prop
    /// carrying a fifth material read as a prop carrying four.</summary>
    private const int VerdictMatCap = 16;

    /// <summary>Properties tracked per material. Ninety-six, not twenty and not sixty-four, and the
    /// number is the finding: <c>PropAnimWatch</c> tracks 20 of the 57 <c>Amp_Char_Shader</c>
    /// declares, and ModBuild 151 already lost a build to a 24-property cap on a shader with 24
    /// interesting properties. The declared count is printed beside the tracked count per material
    /// either way, so a truncation is visible rather than silent.</summary>
    private const int VerdictPropCap = 96;

    /// <summary>How many MOVING properties / lights the verdict names before it stops naming
    /// them. The totals are printed either way.</summary>
    private const int VerdictListCap = 6;

    /// <summary>Animators, lights, particle systems and renderers sampled per frame in the
    /// post-hush window. A prop carries a handful of each; the caps exist so a pathological
    /// prefab cannot turn a per-frame sampler into a stall, and every one of them is printed as
    /// found-vs-sampled.</summary>
    private const int VerdictObjCap = 12;

    /// <summary>A property (or a light's intensity) counts as MOVING when it changes by more than
    /// this between two consecutive frames. Small enough to catch a slow breathe, large enough
    /// that float noise in a value nothing writes does not read as an animation.</summary>
    private const float MoveEpsilon = 1e-4f;

    // ---- shared scratch, cleared per use (no per-frame allocation) -----------------------------
    private static readonly List<Animator> AnimScratch = new(8);
    private static readonly List<SkinnedMeshRenderer> SkinScratch = new(16);
    private static readonly List<Outlinable> OutlineScratch = new(8);
    private static readonly List<MonoBehaviour> BehaviourScratch = new(16);
    private static readonly List<ParticleSystem> ParticleScratch = new(8);
    private static readonly List<Renderer> RendScratch = new(16);

    /// <summary>The animators strand 6 is about to rewind and then stop. Held between the two
    /// halves of <see cref="Apply"/>'s animator pass so the property read-back either side of the
    /// rewind runs ONCE for the whole set rather than once per animator.</summary>
    private static readonly List<Animator> RewindScratch = new(4);

    /// <summary>Census-only scratch: distinct MonoBehaviour type names under the held prop and how
    /// many of each. Written and read by <see cref="Announce"/> alone.</summary>
    private static readonly List<string> TypeNames = new(32);
    private static readonly List<int> TypeCounts = new(32);

    /// <summary>One held prop's suppression: which objects were written, and the value each one
    /// had. Restoring walks these lists, so a value is never guessed and never shared between
    /// props.</summary>
    private sealed class Belt
    {
        internal GameObject? Visual;

        /// <summary>Animators switched off, and the <c>enabled</c> value each one had.</summary>
        internal readonly List<Animator> Animators = new(4);
        internal readonly List<bool> AnimEnabled0 = new(4);

        /// <summary>Outline components switched off, and the <c>enabled</c> value each one had.
        /// The GAME's <c>OutlineParameters.Enabled</c> is neither read nor written — see the type
        /// doc for why that field is the wrong one to take.</summary>
        internal readonly List<Outlinable> Outlines = new(4);
        internal readonly List<bool> OutlineEnabled0 = new(4);

        /// <summary>Skinned renderers made to cull against their real pose, and the
        /// <c>updateWhenOffscreen</c> value each one had. This is the one write that is NOT a
        /// suppression: it keeps a held prop DRAWN.</summary>
        internal readonly List<SkinnedMeshRenderer> Skins = new(8);
        internal readonly List<bool> SkinOffscreen0 = new(8);

        /// <summary>Every <c>Behaviour</c>-derived EMITTER switched off — <c>Light</c>,
        /// <c>Projector</c>, <c>LensFlare</c> — and the <c>enabled</c> value each one had. Held as
        /// <c>Behaviour</c> on purpose: the type boundary is the defect this strand exists for, and
        /// a per-type list is a fourth type waiting to be forgotten.</summary>
        internal readonly List<Behaviour> Emitters = new(8);
        internal readonly List<bool> EmitterEnabled0 = new(8);

        /// <summary>Particle systems this class STOPPED, i.e. only the ones that were PLAYING when
        /// it walked. A system that was already stopped is not in here and is never restarted.</summary>
        internal readonly List<ParticleSystem> Particles = new(8);

        /// <summary>Animators deliberately left running because their controller carries a
        /// <see cref="DelayedDeactivatePropAnimSMB"/>, which drives a rules message rather than a
        /// look. Counted, never written.</summary>
        internal int AnimatorsLeftForRules;

        /// <summary>How many of each set were ALREADY in the state this class wants. A suppression
        /// that changed nothing is a finding, not a non-event: it means the thing was not on in
        /// the first place, which narrows the search for whatever the flash actually is.</summary>
        internal int AnimatorsAlreadyOff, OutlinesAlreadyOff, SkinsAlready;

        /// <summary>THE PRE-STATE, PER CLASS, AND IT IS WHAT MAKES TWO SUPPRESSIONS IN ONE ROUND
        /// ATTRIBUTABLE. How many objects of each new class existed under the visual and how many
        /// of them were LIVE before this class wrote anything. A class whose live count is zero
        /// made no writes at all and cannot be why anything got better OR worse.</summary>
        internal int LightsFound, LightsOn0, ProjectorsFound, ProjectorsOn0, FlaresFound, FlaresOn0;
        internal int ParticlesFound, ParticlesPlaying0;

        /// <summary>STRAND 5 — the SCREEN-SPACE OCCLUSION REGISTRATION, and it is the first thing
        /// this class has ever switched off that does not paint from inside the prop at all.
        /// <c>ObjectOcclusionVolume.OnEnable</c> hands the prop's own <c>MeshRenderer</c> to
        /// <c>TilesOcclusionGenerator.s_Instance.AddObjectRenderer</c> — a list on a CAMERA,
        /// outside every subtree any instrument in this file has ever walked. Found and live
        /// counts, same attribution rule as the other classes.</summary>
        internal int OcclusionFound, OcclusionOn0;

        /// <summary>STRAND 6 — THE REWIND, and it is the first strand this class has ever aimed at
        /// its OWN side effect rather than at the game's. Switching an <c>Animator</c> off leaves
        /// every channel that clip drives LATCHED at whatever value it happened to hold on the
        /// frame of the grab; on a prop whose idle is a ~5 s attention flash that is a coin toss
        /// between "resting" and "at the peak of the flash", and the peak is what the user calls
        /// <i>weiß</i>. <see cref="AnimatorsRewound"/> is how many animators were taken back to
        /// their bound default values before being stopped; <see cref="RewindSkipped"/> is how many
        /// could not be (inactive object, or no controller to have defaults from).</summary>
        internal int AnimatorsRewound, RewindSkipped;

        /// <summary>THE PRE-COUNT FOR STRAND 6, AND IT IS WHAT MAKES IT ATTRIBUTABLE. How many
        /// (material, property) slots under the prop were READ across the rewind and how many
        /// actually CHANGED value because of it. Changed &gt; 0 says the freeze really was latching
        /// a non-default picture and this strand had work to do; changed == 0 says the animator's
        /// channels were already at rest at the grab, the strand wrote nothing visible, and it
        /// cannot be why anything got better OR worse.</summary>
        internal int RewindSlotsRead, RewindSlotsChanged;

        /// <summary>The properties the rewind moved, named with their before→after VALUES. Six
        /// rounds of this instrument reported MOVEMENT and never once a value, so a term latched
        /// at a wrong constant read exactly like a term that was correct. This is the value
        /// print.</summary>
        internal string RewindNamed = string.Empty;

        /// <summary>How many emitters, particle systems and animators were found in a state this
        /// class did NOT leave them in when <see cref="Restore"/> ran — i.e. somebody else wrote
        /// them during the hold. Zero is the expected reading and a non-zero one is the falsifier
        /// for the restore: it means the value handed back is being handed back over a foreign
        /// write.</summary>
        internal int RestoreForeignEmitters, RestoreForeignParticles, RestoreForeignAnimators, RestoreDead;

        internal int NextScanFrame;
        internal int Rescans;

        /// <summary>Label the belt was engaged with, so the restore line can name the prop without
        /// the caller having to pass it a second time.</summary>
        internal string Label = string.Empty;
    }

    private static readonly List<Belt> Live = new(2);
    private static readonly List<Belt> Pool = new(2);
    private static int _logsLeft = LogBudget;

    /// <summary>Prop kinds that have already printed their census. Held as the label strings the
    /// caller passes, which is what the log line is anchored on.</summary>
    private static readonly List<string> KindsDone = new(KindCap);

    /// <summary>How many restore lines a session prints IN TOTAL, at most one per prop kind.</summary>
    private const int RestoreLogBudget = 3;

    private static int _restoreLogsLeft = RestoreLogBudget;
    private static readonly List<string> RestoreKindsDone = new(KindCap);

    // ---- the POST-HUSH verdict's own state ----------------------------------------------------
    //
    // AT MOST ONE BELT IS EVER ARMED. Two props in two hands would otherwise need two of every
    // buffer below, and the question ("what is still painting on a hushed prop?") is answered by
    // one prop as well as by two. Everything here is static and fixed-size, so the per-frame path
    // allocates nothing.

    private static int _verdictsLeft = VerdictBudget;
    private static readonly List<string> VerdictKindsDone = new(KindCap);

    private static Belt? _vBelt;
    private static string _vLabel = string.Empty;
    private static int _vFrames;
    private static int _vEndFrame;

    private static Animator[] _vAnimators = System.Array.Empty<Animator>();
    private static Outlinable[] _vOutlines = System.Array.Empty<Outlinable>();
    private static UnityEngine.Light[] _vLights = System.Array.Empty<UnityEngine.Light>();
    private static ParticleSystem[] _vParticles = System.Array.Empty<ParticleSystem>();
    private static Renderer[] _vRenderers = System.Array.Empty<Renderer>();
    private static int _vFoundAnimators, _vFoundOutlines, _vFoundLights, _vFoundParticles, _vFoundRenderers;

    /// <summary>The materials whose whole property table is read back, and the table itself —
    /// float, range, int, colour, VECTOR and TEXTURE, and each material read through the property
    /// table of ITS OWN shader. The table is resolved ONCE when the window arms
    /// (<c>Shader.GetPropertyName</c> allocates a string, and doing that per frame would be an
    /// instrument that costs more than the thing it measures).</summary>
    private static readonly PropTable VTable = new(VerdictMatCap, VerdictPropCap);

    private static readonly Vector4[] VPrev = new Vector4[VerdictMatCap * VerdictPropCap];
    private static readonly Vector4[] VNow = new Vector4[VerdictMatCap * VerdictPropCap];
    private static readonly bool[] VNowValid = new bool[VerdictMatCap * VerdictPropCap];
    private static readonly bool[] VSeen = new bool[VerdictMatCap * VerdictPropCap];
    private static readonly int[] VMoves = new int[VerdictMatCap * VerdictPropCap];
    private static readonly float[] VLo = new float[VerdictMatCap * VerdictPropCap];
    private static readonly float[] VHi = new float[VerdictMatCap * VerdictPropCap];

    private static readonly float[] VLightPrev = new float[VerdictObjCap];
    private static readonly int[] VLightMoves = new int[VerdictObjCap];
    private static readonly float[] VLightLo = new float[VerdictObjCap];
    private static readonly float[] VLightHi = new float[VerdictObjCap];
    private static readonly float[] VAnimPrev = new float[VerdictObjCap];
    private static readonly int[] VAnimMoves = new int[VerdictObjCap];

    // Aggregates over the window: the WORST (highest) reading of each, because the question is
    // "did anything paint at all", and a maximum answers that where a mean would hide one frame
    // of it. A field that stays 0 across the whole window is the falsifier for its own strand.
    private static int _vAnimOnMax, _vOutOnMax, _vLightOnMax, _vEmitterOnMax;
    private static int _vPartPlayingMax, _vPartAliveMax;
    private static int _vRendDrawMax, _vRendVisMax;
    private static float _vLightIntensityMax;

    // ---- THE OUTWARD ARM: what is painting from OUTSIDE the prop's subtree --------------------
    //
    // EVERY INSTRUMENT IN THIS FILE AND IN PropAnimWatch IS ROOTED AT THE PROP, AND THAT IS WHY
    // FIVE ROUNDS ENDED THE SAME WAY. The post-hush window's own falsifier says it in as many
    // words: if every pre-count is non-zero and every after-count is zero and the user still sees
    // the effect, "the shimmer is being painted by something that is NOT under this prop's subtree
    // at all, which is the one place no instrument in this file can look". ModBuild 448 produced
    // exactly that reading on both machines. These fields ARE that place.
    //
    // They are cheap on purpose. The occlusion arm reads a list the GAME already maintains and two
    // GLOBAL shader slots — no scene sweep at all. The foreign-emitter arm sweeps the scene TWICE
    // per verdict (open and close), never per frame, and at most VerdictBudget verdicts print in a
    // session: this project has paid twice for a FindObjectsOfType on a per-frame path.

    /// <summary>Frames between membership tests against the generator's renderer list. The list is
    /// scene-sized, so this is deliberately not a per-frame walk.</summary>
    private const int OcclusionProbeFrames = 15;

    /// <summary>How far outside the held prop's bounds a foreign emitter still counts as "on it",
    /// in WORLD units. Generous: the point is to name what is near, not to adjudicate it.</summary>
    private const float ForeignReachWorld = 1.5f;

    /// <summary>Foreign emitters named in the verdict before it stops naming them.</summary>
    private const int ForeignNameCap = 6;

    private static readonly int IdEnableOcclusionMap = Shader.PropertyToID("_EnableOcclusionMap");
    private static readonly int IdObjectOcclusion = Shader.PropertyToID("_ObjectOcclusion");
    private static readonly int IdTilesOcclusionMap = Shader.PropertyToID("_TilesOcclusionMap");

    private static bool _vOccGenSeen;
    private static int _vOccProbeAt;
    private static int _vOccListMax, _vOccStillInMax, _vOccMapsBoundMax, _vOccMapRebinds;
    private static int _vOccMapId;
    private static float _vOccEnableLo, _vOccEnableHi;

    private static int _vForeignFound, _vForeignNear, _vForeignOn;
    private static readonly List<string> VForeignNames = new(ForeignNameCap);

    // ---- ROUND EIGHT: THE ROSTER AND THE TIMELINE -------------------------------------------------
    //
    // WHY THIS BLOCK EXISTS, AND IT IS A LESSON THIS PROJECT HAS ALREADY PAID FOR ONCE. Every
    // reading above is a COUNT. The ModBuild 453 log says, for the held trap, "RENDERERS: 3
    // sampled of 3 found, at most 1 drawing and 1 reported isVisible" and "2 material(s) sampled
    // of 2 found (mat0 'Amp_Char_Shader' …; mat1 'GloomhavenVR/Overlay' …)". Two of the prop's
    // three renderers never drew for 169 frames and one of its two materials is THIS MOD'S OWN
    // SHADER — and not one line in this file says WHICH renderer drew, WHICH one was dark, or
    // whether the material the player is looking at is the game's or ours. The recorded lesson is
    // "name the blocker, not the number": six rounds once tuned a coverage FRACTION where one
    // field naming WHICH renderer would have ended it.
    //
    // AND THE SECOND HALF IS TIME. The user's own words for the ModBuild 453 build are "deutlich
    // langsamer als wäre es zeitlupe", and the photometry of his video (.planning/debug/
    // fallen_weisses_aufblitzen.mp4) shows the held trap decaying from ivory white to its correct
    // bronze over roughly three and a half seconds and then staying correct. A ramp is a series of
    // VALUES; every instrument in this file reports one aggregate per hold. So everything sampled
    // here is also accumulated into quarter-second buckets and printed as a series, on the same
    // timebase as the video, so a curve can be compared with a curve.
    private const int RosterCap = 8;

    /// <summary>Quarter-second buckets, twenty-four of them: six seconds, which covers the ~3.4 s
    /// ramp measured in the user's video with room either side.</summary>
    private const int TimelineBuckets = 24;
    private const float TimelineBucketSeconds = 0.25f;

    /// <summary>Frames between the two sampled readings that are not free: the interpolated light
    /// probe (a tetrahedron lookup) and the reflection probe list. ~18 Hz at 90 fps, which is more
    /// than the four samples per bucket the timeline can show.</summary>
    private const int ProbeEvery = 5;

    /// <summary>Frames between keyword reads. <c>Material.shaderKeywords</c> ALLOCATES a string[],
    /// so this one is deliberately slower than the rest and is the only allocating read here.</summary>
    private const int KeywordEvery = 15;
    private const int KeywordNameCap = 10;

    private static readonly string[] RName = new string[RosterCap];
    private static readonly string[] RShaders = new string[RosterCap];
    private static readonly bool[] RMine = new bool[RosterCap];
    private static readonly int[] RDrawFrames = new int[RosterCap];
    private static readonly int[] RVisFrames = new int[RosterCap];
    private static readonly int[] RBlockFrames = new int[RosterCap];
    private static readonly int[] RBlockOverrideMax = new int[RosterCap];
    private static readonly int[] RDeadAtFrame = new int[RosterCap];

    /// <summary>The index in <c>_vRenderers</c> each roster entry was taken from. The roster
    /// SKIPS nulls, so the two arrays are only aligned when nothing was null at arm time;
    /// carrying the source index is what keeps a per-frame reading on the object it names.
    /// A set that shrank under a per-entry read is a recorded incident in this project.</summary>
    private static readonly int[] RSrc = new int[RosterCap];
    private static int _rCount, _rFound;

    /// <summary>Reused for every <c>GetPropertyBlock</c>: the read is per renderer per FRAME, which
    /// is the whole point — the grab-edge HELD? probe reads blocks ONCE, two frames after the grab,
    /// and a single sample cannot see a ramp.</summary>
    private static readonly MaterialPropertyBlock RBlock = new();
    private static int _blkOverrideWorst;
    private static int _blkFramesAny;
    private static readonly List<string> BlkNamed = new(8);

    /// <summary>Every property in the table whose NAME contains "dissolve", plus the two channels
    /// named in this mod's own <c>WallSegmentFade</c> path whether or not the shader declares them.
    /// A dissolve IS a ramp, which is why it is read by name rather than left inside an aggregate.</summary>
    private const int DissolveCap = 8;
    private static readonly int[] DissolveIds = new int[DissolveCap];
    private static readonly string[] DissolveNames = new string[DissolveCap];
    private static int _dissolveCount;
    private static readonly float[] DissolveLo = new float[DissolveCap];
    private static readonly float[] DissolveHi = new float[DissolveCap];
    private static readonly int[] DissolveSeen = new int[DissolveCap];

    private static readonly int[] KwMin = new int[VerdictMatCap];
    private static readonly int[] KwMax = new int[VerdictMatCap];
    private static readonly string[] KwFirst = new string[VerdictMatCap];
    private static readonly string[] KwLast = new string[VerdictMatCap];
    private static int _kwProbeAt, _kwProbes, _kwChanges;

    private static int _probeAt, _probeSamples;
    private static float _shLo, _shHi, _shFirst, _shLast;
    private static int _reflectId, _reflectChanges, _reflectCountMax;
    private static string _reflectName = "<none>";
    private static string _probeUsage = string.Empty;
    private static readonly List<UnityEngine.Rendering.ReflectionProbeBlendInfo> ReflectScratch = new(4);

    private static float _poseAngLo, _poseAngHi, _poseDistLo, _poseDistHi;
    private static float _tlT0;
    private static readonly int[] TlN = new int[TimelineBuckets];
    private static readonly float[] TlSh = new float[TimelineBuckets];
    private static readonly float[] TlAng = new float[TimelineBuckets];
    private static readonly float[] TlDist = new float[TimelineBuckets];
    private static readonly int[] TlDraw = new int[TimelineBuckets];
    private static readonly int[] TlBlk = new int[TimelineBuckets];
    private static readonly int[] TlShN = new int[TimelineBuckets];

    /// <summary>
    /// Suppress a prop entering a hand. Idempotent per visual: a re-grab during the release glide
    /// finds the existing ledger and keeps it, so the original values are never overwritten with
    /// the suppressed ones (which would make the restore a no-op and strand the animator off for
    /// the rest of the session).
    /// </summary>
    internal static void Engage(GameObject? visual, string label)
    {
        if (visual == null)
            return;
        if (Find(visual) != null)
            return;

        Belt b = Rent();
        b.Visual = visual;
        b.Label = label;
        b.NextScanFrame = Time.frameCount + RescanFrames;
        Live.Add(b);
        Apply(b);
        Announce(b, label);
        ArmVerdict(b, label);
    }

    /// <summary>One frame: re-walk any suppressed prop whose cadence has come round. A
    /// <c>Count</c> compare when nothing is held, and one int compare per held prop otherwise.
    /// Called from <c>PropGrab.Tick</c> ABOVE the feature gate, for the reason the Apparance thaw
    /// is: a suppression written on a prop still in a hand when the dial goes off must still be
    /// handed back, and the gate's <c>ReleaseAll</c> runs on that same frame.</summary>
    internal static void Tick()
    {
        for (int i = Live.Count - 1; i >= 0; i--)
        {
            Belt b = Live[i];
            if (b.Visual == null)
            {
                // The prop died under us (looted, broken, teardown). Nothing to hand back — the
                // objects that held the replaced values are gone with it. The verdict still
                // reports, because "the prop was destroyed mid-hold" is an answer and a silently
                // dropped window looks exactly like a window that never armed.
                CloseVerdict(b, "the prop was destroyed mid-hold");
                Retire(i);
                continue;
            }
            if (Time.frameCount >= b.NextScanFrame)
            {
                b.NextScanFrame = Time.frameCount + RescanFrames;
                b.Rescans++;
                Apply(b);
            }

            // THE POST-HUSH WINDOW. At most ONE belt is ever armed, so this is a reference compare
            // for every other held prop and for every frame once the window has closed.
            if (ReferenceEquals(b, _vBelt))
                SampleVerdict(b);
        }
    }

    /// <summary>Hand every replaced value back for one prop and drop the record. Idempotent, and
    /// safe on a prop that was never suppressed. Called from BOTH landing paths
    /// (<c>GrabbableProp.FinishGlide</c> and <c>GrabbableProp.Restore</c>), which is where
    /// <c>PropAnimWatch.NotifyLanded</c> already converges.</summary>
    internal static void Release(GameObject? visual)
    {
        if (visual == null)
            return;
        for (int i = Live.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(Live[i].Visual, visual))
                continue;
            // The verdict is emitted BEFORE the restore: it is a report about what the prop looked
            // like WHILE it was hushed, and one frame of it is still true here. Then the restore
            // runs, and the restore line reports what it found — including whether anything had
            // been written by somebody else during the hold.
            CloseVerdict(Live[i], "the prop was put down");
            Restore(Live[i]);
            AnnounceRestore(Live[i]);
            Retire(i);
            return;
        }
    }

    /// <summary>Hand everything back — a scenario change, the feature dial going off, uninstall.
    /// The suppression must never outlive the hold that justified it.</summary>
    internal static void ReleaseAll()
    {
        for (int i = Live.Count - 1; i >= 0; i--)
        {
            CloseVerdict(Live[i], "every hold was released at once (scenario change, dial off or uninstall)");
            Restore(Live[i]);
            AnnounceRestore(Live[i]);
            Retire(i);
        }
    }

    /// <summary>Re-arm the per-session census budget for a new scenario (a new scenario is a new
    /// hardware question), after handing every live suppression back.</summary>
    internal static void Reset()
    {
        ReleaseAll();
        _logsLeft = LogBudget;
        KindsDone.Clear();
        _verdictsLeft = VerdictBudget;
        VerdictKindsDone.Clear();
        _restoreLogsLeft = RestoreLogBudget;
        RestoreKindsDone.Clear();
    }

    // ---- the writes ------------------------------------------------------------------------------

    /// <summary>Walk the prop's subtree and suppress anything not already suppressed, remembering
    /// the value it replaced. Inactive objects are included on purpose: a subtree that is inactive
    /// now can be active two frames later (<c>ProceduralMapTile.ShowContent</c> deactivates whole
    /// generated subtrees), and the door belt's own account names covering only what is active
    /// right now as the way to arm the wrong subtree.</summary>
    private static void Apply(Belt b)
    {
        GameObject? go = b.Visual;
        if (go == null)
            return;

        AnimScratch.Clear();
        RewindScratch.Clear();
        go.GetComponentsInChildren(includeInactive: true, AnimScratch);
        for (int i = 0; i < AnimScratch.Count; i++)
        {
            Animator a = AnimScratch[i];
            if (a == null || Contains(b.Animators, a))
                continue;

            // THE RULES EXCEPTION. Not in the ledger at all, so the restore cannot touch it either:
            // an animator we never wrote is an animator we must not write on the way out.
            if (DrivesRules(a))
            {
                b.AnimatorsLeftForRules++;
                continue;
            }

            b.Animators.Add(a);
            b.AnimEnabled0.Add(a.enabled);
            if (!a.enabled)
                b.AnimatorsAlreadyOff++;
            else
                // NOT SWITCHED OFF HERE ANY MORE. An animator this class stops is first taken back
                // to its bound defaults (strand 6) and only then disabled, and the rewind has to be
                // measured across the whole set at once or the per-material read-back would run
                // once per animator. RewindAndStop does both, below.
                RewindScratch.Add(a);
        }
        AnimScratch.Clear();
        RewindAndStop(b, go);
        RewindScratch.Clear();

        OutlineScratch.Clear();
        go.GetComponentsInChildren(includeInactive: true, OutlineScratch);
        for (int i = 0; i < OutlineScratch.Count; i++)
        {
            Outlinable o = OutlineScratch[i];
            if (o == null || Contains(b.Outlines, o))
                continue;
            b.Outlines.Add(o);
            b.OutlineEnabled0.Add(o.enabled);
            if (!o.enabled)
                b.OutlinesAlreadyOff++;
            else
                o.enabled = false;   // Outlinable.OnDisable drops it from the pass's static list
        }
        OutlineScratch.Clear();

        SkinScratch.Clear();
        go.GetComponentsInChildren(includeInactive: true, SkinScratch);
        for (int i = 0; i < SkinScratch.Count; i++)
        {
            SkinnedMeshRenderer s = SkinScratch[i];
            if (s == null || Contains(b.Skins, s))
                continue;
            b.Skins.Add(s);
            b.SkinOffscreen0.Add(s.updateWhenOffscreen);
            if (s.updateWhenOffscreen)
                b.SkinsAlready++;
            else
                s.updateWhenOffscreen = true;
        }
        SkinScratch.Clear();

        // ---- STRAND 3: EVERY Behaviour-DERIVED EMITTER ----
        //
        // Light, Projector and LensFlare are the three components in Unity that PAINT without
        // being a Renderer. None of them is reachable from a GetComponentsInChildren<MonoBehaviour>
        // sweep — they derive from Behaviour — which is why the grab-edge census could report the
        // MonoBehaviour histogram in full and still never name the thing holding the lamp. Taken
        // as one class rather than three named types on purpose: the type boundary IS the defect.
        // The FOUND counts are the population of THIS walk (assigned, not accumulated — a rescan
        // re-counts the same objects); the ON counts accumulate, because an object that arrives
        // enabled on a later walk was genuinely live before this class wrote it.
        b.LightsFound = TakeEmitters<UnityEngine.Light>(b, go, ref b.LightsOn0);
        b.ProjectorsFound = TakeEmitters<Projector>(b, go, ref b.ProjectorsOn0);
        b.FlaresFound = TakeEmitters<LensFlare>(b, go, ref b.FlaresOn0);

        // ---- STRAND 5: THE SCREEN-SPACE OCCLUSION REGISTRATION ----
        //
        // THE ONE PAINTER THAT IS NOT UNDER THE PROP. Five rounds measured the prop's subtree and
        // the sixth measured it AFTER the hush: over 358 frames on a held gold pile the animators,
        // outlines, lights and particles were all at zero, no material property moved, and the user
        // still reported the effect. A census rooted at the prop cannot answer that by construction.
        //
        // ObjectOcclusionVolume.OnEnable is `TilesOcclusionGenerator.s_Instance.AddObjectRenderer(
        // GetComponent<MeshRenderer>())` and OnDisable is the matching Remove. The generator is a
        // component ON A CAMERA holding a CommandBuffer at CameraEvent.BeforeGBuffer which draws
        // every registered renderer with m_OcclusionObjectMaterial into a QUARTER-RESOLUTION target,
        // blurs it twice and publishes it as the GLOBAL texture `_ObjectOcclusion` (plus
        // `_TilesOcclusionMap` and `_EnableOcclusionMap`). A global shader texture is invisible to
        // `material.Get*` read-back, it is not a Light, an Animator, a ParticleSystem or an
        // Outlinable, and it hangs off no object in the prop's hierarchy — so it is dark to every
        // instrument this file has ever shipped, and it is exactly the "screen-space occlusion term"
        // the ModBuild 448 HELD? line named as the next suspect and then could not test.
        //
        // WHY IT ONLY SHOWS IN A PALM. On its hex the prop's footprint in that map is small and
        // still, so the term it samples back is effectively constant. In a hand the prop fills a
        // large part of the eye and MOVES every frame, so its own blurred quarter-res silhouette
        // sweeps across it — a soft moving wash with no animator, no lamp and no material of its
        // own. Twenty centimetres from the eye that is the "highlighting/Licht-Effekt".
        //
        // TAKEN THROUGH THE GAME'S OWN LIFECYCLE, NOT BY EDITING ITS LIST. Disabling the component
        // makes OnDisable call RemoveObjectRenderer, which sets m_RenderersUpdated and has the
        // generator REBUILD its command buffer without this prop; restoring `enabled` runs OnEnable
        // and puts it back. So the ledger below is the whole of the change and the whole of the
        // undo, no game state is written, and a prop whose volume was ALREADY off is left alone.
        b.OcclusionFound = TakeEmitters<ObjectOcclusionVolume>(b, go, ref b.OcclusionOn0);

        // ---- STRAND 4: PARTICLE SYSTEMS THAT WERE ACTUALLY PLAYING ----
        //
        // Stop-and-CLEAR, never Pause: a paused system leaves its live particles hanging in the
        // air, which is a frozen shimmer rather than an absent one. Only the ones found playing go
        // in the ledger, so the restore can never start an effect the game had already finished.
        ParticleScratch.Clear();
        go.GetComponentsInChildren(includeInactive: true, ParticleScratch);
        b.ParticlesFound = ParticleScratch.Count;
        for (int i = 0; i < ParticleScratch.Count; i++)
        {
            ParticleSystem p = ParticleScratch[i];
            if (p == null || Contains(b.Particles, p) || !p.isPlaying)
                continue;
            b.ParticlesPlaying0++;
            b.Particles.Add(p);
            p.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        ParticleScratch.Clear();
    }

    /// <summary>Switch off every <typeparamref name="T"/> under <paramref name="go"/> that is not
    /// already in the ledger, remembering the <c>enabled</c> value each one had. Returns how many
    /// were FOUND and adds how many were LIVE to <paramref name="liveBefore"/> — the pre-state
    /// counts that let a round with two new suppressions still say which one had anything to
    /// do.</summary>
    private static int TakeEmitters<T>(Belt b, GameObject go, ref int liveBefore) where T : Behaviour
    {
        // The ARRAY overload rather than the shared-list one: a List<T> scratch cannot be shared
        // across three different T, and this runs at the grab and then once every RescanFrames —
        // never on the per-frame path the class doc's cost note is about.
        T[] found = go.GetComponentsInChildren<T>(includeInactive: true);
        for (int i = 0; i < found.Length; i++)
        {
            T e = found[i];
            if (e == null || Contains(b.Emitters, e))
                continue;
            b.Emitters.Add(e);
            b.EmitterEnabled0.Add(e.enabled);
            if (!e.enabled)
                continue;
            liveBefore++;
            e.enabled = false;
        }
        return found.Length;
    }

    /// <summary>Does this animator's controller carry the state behaviour that sends a rules
    /// message? <c>GetBehaviours</c> allocates, so this runs once per animator per walk — at the
    /// grab and then at most every <see cref="RescanFrames"/> frames — and never on the per-frame
    /// path. A null controller has no behaviours and Unity answers with an empty array.</summary>
    private static bool DrivesRules(Animator a)
    {
        DelayedDeactivatePropAnimSMB[] found = a.GetBehaviours<DelayedDeactivatePropAnimSMB>();
        return found != null && found.Length > 0;
    }

    // ---- STRAND 6: the rewind ---------------------------------------------------------------------

    /// <summary>
    /// TAKE EVERY ANIMATOR THIS CLASS IS ABOUT TO STOP BACK TO ITS BOUND DEFAULTS FIRST, THEN STOP
    /// IT — and measure what that changed.
    ///
    /// <para><b>WHY, AND IT IS THIS CLASS'S OWN SIDE EFFECT.</b> <c>Animator.enabled = false</c>
    /// does not undo a clip; it stops the clip WHERE IT IS. Every channel the clip drives —
    /// a material colour, an emissive boost, a dissolve amount, a renderer's own enabled flag, a
    /// bone — keeps the value it happened to hold on the frame of the grab, for the whole hold. The
    /// trap's idle is a single looping clip running at 0.202 normalized/s (ModBuild 447's A/B line,
    /// <c>advancing home=359/360</c>), i.e. a ~5 s cycle, and an attention flash is a short bright
    /// part of such a cycle. Grabbing during the bright part therefore LATCHES the bright part for
    /// as long as the prop is held, and grabbing anywhere else does not. That is
    /// "<i>manchmal</i> weiß" exactly, it is a state and not a movement, and it is invisible to
    /// every reading this file has ever taken because all of them measure whether something MOVED.
    /// A latched value does not move.</para>
    ///
    /// <para><b><c>WriteDefaultValues</c>, not <c>Play(state, layer, 0f)</c>.</b> Replaying a state
    /// from its start re-enters it, and re-entering a state fires every <c>StateMachineBehaviour</c>
    /// on it — on these props that is how <c>DelayedDeactivatePropAnimSMB</c> sends a rules message
    /// and raises a global "deactivations in progress" flag, and this lane does not write game
    /// state. <c>Animator.WriteDefaultValues</c> writes the values the animator recorded when it
    /// bound the controller, touches no state machine, fires no behaviour and no animation event,
    /// and does it for EVERY channel the controller animates — which matters because nothing in
    /// this file knows WHICH channel the flash lives in (the 448/450 read-backs say it is not any
    /// of the 48 float/range/colour properties, and the clip still advances on 359 of 360 home
    /// frames, so it is something else the clip drives).</para>
    ///
    /// <para><b>THE RESTORE IS THE GAME'S OWN WRITER.</b> <see cref="Restore"/> hands the
    /// <c>enabled</c> flag back and the animator resumes from the state it retained; on the first
    /// frame it evaluates, it drives every one of those channels itself, over our defaults. So the
    /// highlight comes back exactly as before by construction rather than by a remembered copy —
    /// there is no second snapshot to restore over somebody else's write, which is the recorded
    /// incident this project already paid for once. The falsifier is counted rather than asserted:
    /// <see cref="Belt.RestoreForeignAnimators"/> is how many animators were found ENABLED at the
    /// landing although this class had switched them off.</para>
    ///
    /// <para><b>Refused, not forced, on an animator that cannot have defaults:</b> an inactive
    /// object (the animator was never initialised) or a null controller. Those are counted in
    /// <see cref="Belt.RewindSkipped"/> and simply stopped the way ModBuild 445 stopped them.</para>
    /// </summary>
    private static void RewindAndStop(Belt b, GameObject go)
    {
        if (RewindScratch.Count == 0)
            return;

        // The table is resolved against the prop's CURRENT materials every time, because a rescan
        // can find an animator on a subtree that was inactive at the grab and whose materials were
        // not in the earlier table at all.
        RewindTable.Resolve(go);
        RewindTable.Sample(RwBefore, RwValid);

        int rewound = 0;
        for (int i = 0; i < RewindScratch.Count; i++)
        {
            Animator a = RewindScratch[i];
            if (a == null)
                continue;
            if (!a.gameObject.activeInHierarchy || a.runtimeAnimatorController == null)
            {
                b.RewindSkipped++;
                continue;
            }
            a.WriteDefaultValues();
            rewound++;
        }
        b.AnimatorsRewound += rewound;

        if (rewound > 0)
            MeasureRewind(b);

        // Only now is the picture allowed to stop, so the frame that is frozen is the resting one.
        for (int i = 0; i < RewindScratch.Count; i++)
        {
            Animator a = RewindScratch[i];
            if (a != null)
                a.enabled = false;
        }
    }

    /// <summary>Read the table back after the rewind and record how many slots the rewind actually
    /// moved, naming the first few with their before→after values. Pure instrument: nothing outside
    /// the report reads what it writes.</summary>
    private static void MeasureRewind(Belt b)
    {
        RewindTable.Sample(RwAfter, RwValidAfter);

        int read = 0, changed = 0;
        var named = new System.Text.StringBuilder(256);
        int listed = 0;
        for (int slot = 0; slot < RewindTable.Capacity; slot++)
        {
            if (!RwValid[slot] || !RwValidAfter[slot])
                continue;
            read++;
            Vector4 before = RwBefore[slot], after = RwAfter[slot];
            if (!Moved(RewindTable.Kind[slot], before, after))
                continue;
            changed++;
            if (listed >= VerdictListCap)
                continue;
            if (listed > 0)
                named.Append("; ");
            named.Append(RewindTable.Name[slot] ?? "<unnamed>").Append(" (mat")
                 .Append(slot / RewindTable.PropCap).Append(' ')
                 .Append(RewindTable.ShaderOf(slot / RewindTable.PropCap)).Append(") ")
                 .Append(Show(RewindTable.Kind[slot], before)).Append('→')
                 .Append(Show(RewindTable.Kind[slot], after));
            listed++;
        }

        b.RewindSlotsRead += read;
        b.RewindSlotsChanged += changed;
        if (b.RewindNamed.Length == 0 && named.Length > 0)
            b.RewindNamed = named.ToString();
    }

    /// <summary>Did this slot change? Componentwise for a colour or a vector, EXACT for a texture
    /// (the value is a texture instance id and a "close" id means nothing), epsilon for a
    /// float.</summary>
    private static bool Moved(byte kind, Vector4 a, Vector4 c)
    {
        if (kind == PropTable.KindTexture)
            return a.x != c.x;
        if (kind == PropTable.KindFloat)
            return Mathf.Abs(a.x - c.x) > MoveEpsilon;
        return Mathf.Abs(a.x - c.x) > MoveEpsilon || Mathf.Abs(a.y - c.y) > MoveEpsilon
            || Mathf.Abs(a.z - c.z) > MoveEpsilon || Mathf.Abs(a.w - c.w) > MoveEpsilon;
    }

    /// <summary>A slot's value, printed the way its own class reads: one number for a float, four
    /// for a colour or a vector, a texture instance id for a texture.</summary>
    private static string Show(byte kind, Vector4 v)
    {
        if (kind == PropTable.KindTexture)
            return v.x == 0f ? "<no texture>" : $"tex#{(int)v.x}";
        if (kind == PropTable.KindFloat)
            return v.x.ToString("0.####");
        return $"({v.x:0.###},{v.y:0.###},{v.z:0.###},{v.w:0.###})";
    }

    /// <summary>A single comparable number per slot, for the min/max range the movement report
    /// prints: the value itself for a float, the mean of r/g/b for a colour, the magnitude for a
    /// vector, the instance id for a texture.</summary>
    private static float Fold(byte kind, Vector4 v)
    {
        if (kind == PropTable.KindColor)
            return (v.x + v.y + v.z) * (1f / 3f);
        if (kind == PropTable.KindVector)
            return Mathf.Sqrt((v.x * v.x) + (v.y * v.y) + (v.z * v.z) + (v.w * v.w));
        return v.x;
    }

    // ---- the property table -----------------------------------------------------------------------

    /// <summary>
    /// THE MATERIALS UNDER ONE PROP AND, PER MATERIAL, THE PROPERTY TABLE OF *ITS OWN* SHADER —
    /// float, range, int, colour, VECTOR and TEXTURE.
    ///
    /// <para><b>Two defects in the old read-back this type exists to remove.</b> (1) It built ONE
    /// property table, from <c>VMats[0]</c>'s shader, and then read every other material through
    /// it; every id that material's own shader does not declare was silently skipped by
    /// <c>HasProperty</c>, and the line still printed "N material(s) on shader 'X'" as though all
    /// of them were X. (2) It tracked float, range and colour only, so a VECTOR or a TEXTURE
    /// property could be rewritten every frame and the line would say "nothing moved" — which the
    /// ModBuild 450 line names as its own blind spot in so many words.</para>
    ///
    /// <para><b><c>sharedMaterials</c>, NEVER <c>material</c>.</b> <c>Renderer.material</c>
    /// INSTANTIATES a clone the first time it is touched, which is a permanent change to the scene
    /// made by an instrument. Once anything has instanced a renderer's material Unity stores the
    /// clone back into the renderer, so <c>sharedMaterials</c> returns the instanced values without
    /// ever creating one.</para>
    /// </summary>
    private sealed class PropTable
    {
        internal const byte KindFloat = 0, KindColor = 1, KindVector = 2, KindTexture = 3;

        private readonly int _matCap;
        private readonly int _propCap;
        private readonly List<Renderer> _rends = new(16);

        internal readonly Material[] Mats;
        internal readonly int[] Id;
        internal readonly string[] Name;
        internal readonly byte[] Kind;

        /// <summary>Properties tracked for material <c>m</c>, and the shader it actually uses.</summary>
        internal readonly int[] PerMat;
        internal readonly string[] Shader;
        internal readonly int[] DeclaredPerMat;

        /// <summary>Distinct materials KEPT, and distinct materials FOUND. A truncated list is not
        /// an absence and both numbers are printed.</summary>
        internal int MatCount, MatFound;

        internal PropTable(int matCap, int propCap)
        {
            _matCap = matCap;
            _propCap = propCap;
            Mats = new Material[matCap];
            Shader = new string[matCap];
            PerMat = new int[matCap];
            DeclaredPerMat = new int[matCap];
            Id = new int[matCap * propCap];
            Name = new string[matCap * propCap];
            Kind = new byte[matCap * propCap];
        }

        internal int PropCap => _propCap;

        internal int Capacity => _matCap * _propCap;

        /// <summary>Total (material, property) slots this table populates.</summary>
        internal int Slots
        {
            get
            {
                int n = 0;
                for (int m = 0; m < MatCount; m++)
                    n += PerMat[m];
                return n;
            }
        }

        /// <summary>Properties declared across every material this table kept — the population the
        /// tracked count is a fraction OF.</summary>
        internal int Declared
        {
            get
            {
                int n = 0;
                for (int m = 0; m < MatCount; m++)
                    n += DeclaredPerMat[m];
                return n;
            }
        }

        /// <summary>True when a material's own table hit the per-material cap, so a zero below it
        /// is a truncation rather than an absence.</summary>
        internal bool Truncated
        {
            get
            {
                if (MatFound > MatCount)
                    return true;
                for (int m = 0; m < MatCount; m++)
                {
                    if (PerMat[m] >= _propCap && DeclaredPerMat[m] > PerMat[m])
                        return true;
                }
                return false;
            }
        }

        internal string ShaderOf(int mat) => mat >= 0 && mat < MatCount ? Shader[mat] : "<none>";

        /// <summary>Per-material "'shader' D declared / T tracked", so a line can never again claim
        /// one shader for materials that do not share one.</summary>
        internal string Describe()
        {
            var sb = new System.Text.StringBuilder(128);
            for (int m = 0; m < MatCount; m++)
            {
                if (m > 0)
                    sb.Append("; ");
                sb.Append("mat").Append(m).Append(" '").Append(Shader[m]).Append("' ")
                  .Append(DeclaredPerMat[m]).Append(" declared / ").Append(PerMat[m])
                  .Append(" tracked");
            }
            return sb.Length == 0 ? "no material" : sb.ToString();
        }

        internal void Resolve(GameObject go)
        {
            MatCount = 0;
            MatFound = 0;
            for (int i = 0; i < _matCap; i++)
            {
                Mats[i] = null!;
                Shader[i] = "<none>";
                PerMat[i] = 0;
                DeclaredPerMat[i] = 0;
            }

            _rends.Clear();
            go.GetComponentsInChildren(includeInactive: true, _rends);
            for (int r = 0; r < _rends.Count; r++)
            {
                Renderer rend = _rends[r];
                if (rend == null)
                    continue;
                Material[] mats = rend.sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                {
                    Material mat = mats[m];
                    if (mat == null)
                        continue;
                    bool dup = false;
                    for (int k = 0; k < MatCount; k++)
                        dup |= ReferenceEquals(Mats[k], mat);
                    if (dup)
                        continue;
                    MatFound++;
                    if (MatCount >= _matCap)
                        continue;
                    Mats[MatCount++] = mat;
                }
            }
            _rends.Clear();

            for (int m = 0; m < MatCount; m++)
            {
                Material mat = Mats[m];
                Shader? sh = mat != null ? mat.shader : null;
                if (sh == null)
                    continue;
                Shader[m] = sh.name;
                int declared = sh.GetPropertyCount();
                DeclaredPerMat[m] = declared;
                int kept = 0;
                for (int i = 0; i < declared && kept < _propCap; i++)
                {
                    UnityEngine.Rendering.ShaderPropertyType t = sh.GetPropertyType(i);
                    byte kind;
                    switch (t)
                    {
                        case UnityEngine.Rendering.ShaderPropertyType.Color: kind = KindColor; break;
                        case UnityEngine.Rendering.ShaderPropertyType.Vector: kind = KindVector; break;
                        case UnityEngine.Rendering.ShaderPropertyType.Texture: kind = KindTexture; break;
                        case UnityEngine.Rendering.ShaderPropertyType.Float:
                        case UnityEngine.Rendering.ShaderPropertyType.Range:
                        case UnityEngine.Rendering.ShaderPropertyType.Int: kind = KindFloat; break;
                        default: continue;
                    }
                    int slot = (m * _propCap) + kept;
                    Id[slot] = sh.GetPropertyNameId(i);
                    Name[slot] = sh.GetPropertyName(i);
                    Kind[slot] = kind;
                    kept++;
                }
                PerMat[m] = kept;
            }
        }

        /// <summary>Read every slot's current value. Allocation-free: the caller owns both arrays
        /// and every getter returns a struct or a reference.</summary>
        internal void Sample(Vector4[] into, bool[] valid)
        {
            for (int slot = 0; slot < into.Length; slot++)
                valid[slot] = false;
            for (int m = 0; m < MatCount; m++)
            {
                Material mat = Mats[m];
                if (mat == null)
                    continue;
                int n = PerMat[m];
                for (int k = 0; k < n; k++)
                {
                    int slot = (m * _propCap) + k;
                    int id = Id[slot];
                    if (!mat.HasProperty(id))
                        continue;
                    Vector4 v;
                    switch (Kind[slot])
                    {
                        case KindColor:
                        {
                            Color c = mat.GetColor(id);
                            v = new Vector4(c.r, c.g, c.b, c.a);
                            break;
                        }
                        case KindVector:
                            v = mat.GetVector(id);
                            break;
                        case KindTexture:
                        {
                            Texture? tex = mat.GetTexture(id);
                            v = new Vector4(tex != null ? tex.GetInstanceID() : 0f, 0f, 0f, 0f);
                            break;
                        }
                        default:
                            v = new Vector4(mat.GetFloat(id), 0f, 0f, 0f);
                            break;
                    }
                    into[slot] = v;
                    valid[slot] = true;
                }
            }
        }
    }

    /// <summary>Materials a prop may carry before the rewind's read-back truncates. Generous on
    /// purpose: this number existing at all is what cost ModBuild 151 and 448 a build each.</summary>
    private const int RewindMatCap = 16;

    /// <summary>Properties per material for the rewind's read-back. <c>Amp_Char_Shader</c> declares
    /// 57; ninety-six leaves room for a shader half again as large before the word TRUNCATED can
    /// appear in the line.</summary>
    private const int RewindPropCap = 96;

    private static readonly PropTable RewindTable = new(RewindMatCap, RewindPropCap);
    private static readonly Vector4[] RwBefore = new Vector4[RewindMatCap * RewindPropCap];
    private static readonly Vector4[] RwAfter = new Vector4[RewindMatCap * RewindPropCap];
    private static readonly bool[] RwValid = new bool[RewindMatCap * RewindPropCap];
    private static readonly bool[] RwValidAfter = new bool[RewindMatCap * RewindPropCap];

    /// <summary>Give every replaced value back, object for object. A destroyed object is skipped
    /// rather than written — Unity's <c>!= null</c> answers that — and the lists are cleared by
    /// <see cref="Retire"/> whatever happens here.</summary>
    private static void Restore(Belt b)
    {
        for (int i = 0; i < b.Animators.Count && i < b.AnimEnabled0.Count; i++)
        {
            Animator a = b.Animators[i];
            if (a == null)
                continue;
            // THE FALSIFIER FOR STRAND 6'S RESTORE, counted the same way the emitters' is. This
            // class left every animator it wrote at enabled=false; finding one ENABLED means
            // somebody else turned it back on during the hold, and the value about to be handed
            // back is being handed back over their write. The rewind itself needs no restore — the
            // animator re-drives every channel it owns on the first frame it evaluates, so the
            // highlight returns because its own writer takes it back, not because we kept a copy.
            if (b.AnimEnabled0[i] && a.enabled)
                b.RestoreForeignAnimators++;
            a.enabled = b.AnimEnabled0[i];
        }
        for (int i = 0; i < b.Outlines.Count && i < b.OutlineEnabled0.Count; i++)
        {
            Outlinable o = b.Outlines[i];
            if (o != null)
                o.enabled = b.OutlineEnabled0[i];
        }
        for (int i = 0; i < b.Skins.Count && i < b.SkinOffscreen0.Count; i++)
        {
            SkinnedMeshRenderer s = b.Skins[i];
            if (s != null)
                s.updateWhenOffscreen = b.SkinOffscreen0[i];
        }

        // THE EMITTERS, AND THE FALSIFIER FOR THE RESTORE IS COUNTED HERE RATHER THAN ASSERTED.
        // This project has a recorded incident where a hide saved a FOREIGN mid-animation value
        // and then restored garbage over another system's restore. The guard against it is not a
        // comment: before writing the remembered value back, compare what is there NOW against
        // what this class LEFT there (false). A mismatch means somebody else wrote the object
        // during the hold and the value about to be handed back is being handed back over their
        // write. The count goes in the restore line; it is expected to be zero, and a non-zero
        // reading is the one that says this restore is not exact.
        for (int i = 0; i < b.Emitters.Count && i < b.EmitterEnabled0.Count; i++)
        {
            Behaviour e = b.Emitters[i];
            if (e == null)
            {
                b.RestoreDead++;
                continue;
            }
            if (e.enabled)
                b.RestoreForeignEmitters++;
            e.enabled = b.EmitterEnabled0[i];
        }

        // ONLY THE SYSTEMS THIS CLASS STOPPED, AND ONLY IF THEY ARE STILL THIS PROP'S. These
        // objects are POOLED — SpawnPFXOnEnable hands them to ObjectPool.Recycle — so a reference
        // taken at the grab can by the landing name an object that has been recycled into some
        // other effect entirely. Restarting THAT would be this lane spawning a foreign effect on
        // release. The parent test is what makes the restore safe, and the visual is checked first
        // because a destroyed prop has no transform to compare against.
        Transform? visual = b.Visual != null ? b.Visual.transform : null;
        for (int i = 0; i < b.Particles.Count; i++)
        {
            ParticleSystem p = b.Particles[i];
            if (p == null)
            {
                b.RestoreDead++;
                continue;
            }
            if (visual == null || !p.transform.IsChildOf(visual))
            {
                b.RestoreForeignParticles++;
                continue;
            }
            p.Play(withChildren: true);
        }
    }

    // ---- bookkeeping ------------------------------------------------------------------------------

    private static Belt? Find(GameObject visual)
    {
        for (int i = 0; i < Live.Count; i++)
        {
            if (ReferenceEquals(Live[i].Visual, visual))
                return Live[i];
        }
        return null;
    }

    private static bool Contains<T>(List<T> list, T item) where T : Object
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], item))
                return true;
        }
        return false;
    }

    private static Belt Rent()
    {
        int last = Pool.Count - 1;
        if (last < 0)
            return new Belt();
        Belt b = Pool[last];
        Pool.RemoveAt(last);
        return b;
    }

    private static void Retire(int index)
    {
        Belt b = Live[index];
        Live.RemoveAt(index);
        b.Visual = null;
        b.Animators.Clear();
        b.AnimEnabled0.Clear();
        b.Outlines.Clear();
        b.OutlineEnabled0.Clear();
        b.Skins.Clear();
        b.SkinOffscreen0.Clear();
        b.Emitters.Clear();
        b.EmitterEnabled0.Clear();
        b.Particles.Clear();
        b.AnimatorsLeftForRules = 0;
        b.AnimatorsAlreadyOff = 0;
        b.OutlinesAlreadyOff = 0;
        b.SkinsAlready = 0;
        b.LightsFound = b.LightsOn0 = 0;
        b.ProjectorsFound = b.ProjectorsOn0 = 0;
        b.FlaresFound = b.FlaresOn0 = 0;
        b.ParticlesFound = b.ParticlesPlaying0 = 0;
        b.RestoreForeignEmitters = b.RestoreForeignParticles = b.RestoreDead = 0;
        b.RestoreForeignAnimators = 0;
        b.AnimatorsRewound = b.RewindSkipped = 0;
        b.RewindSlotsRead = b.RewindSlotsChanged = 0;
        b.RewindNamed = string.Empty;
        b.Label = string.Empty;
        b.Rescans = 0;
        b.NextScanFrame = 0;
        if (Pool.Count < 4)
            Pool.Add(b);
    }

    // ---- the census ------------------------------------------------------------------------------

    /// <summary>
    /// Say what was suppressed AND what was left alive, once per prop kind. Pure print — the state
    /// machine above has already finished by the time this runs, so gating or deleting this method
    /// changes nothing but the log (the shape <c>scripts/check-instrument-writes.py</c> exists to
    /// keep true).
    ///
    /// <para><b>THE SECOND HALF OF THIS LINE IS THE POINT OF THE WHOLE ROUND.</b> Four rounds
    /// measured one animator's layers and one material's property table, and all four found them
    /// still. Nobody has ever simply ASKED what components a held chest carries. This enumerates
    /// them — the distinct MonoBehaviour type names under the visual with counts, the particle
    /// systems and whether they are playing, the lights, the renderers, the three world-anchor
    /// feeders — so the next hardware log NAMES the candidate instead of re-measuring the window
    /// that four rounds have already proved empty.</para>
    /// </summary>
    private static void Announce(Belt b, string label)
    {
        if (_logsLeft <= 0 || KindSpent(label))
            return;
        _logsLeft--;
        if (KindsDone.Count < KindCap)
            KindsDone.Add(label);

        GameObject? go = b.Visual;
        if (go == null)
            return;

        int animOff = b.Animators.Count - b.AnimatorsAlreadyOff;
        int outlineOff = b.Outlines.Count - b.OutlinesAlreadyOff;
        int skinChanged = b.Skins.Count - b.SkinsAlready;

        // ---- what is still alive under the prop ----
        ParticleScratch.Clear();
        go.GetComponentsInChildren(includeInactive: true, ParticleScratch);
        int particles = ParticleScratch.Count;
        int particlesPlaying = 0;
        for (int i = 0; i < ParticleScratch.Count; i++)
        {
            ParticleSystem p = ParticleScratch[i];
            if (p != null && p.isPlaying)
                particlesPlaying++;
        }
        ParticleScratch.Clear();

        RendScratch.Clear();
        go.GetComponentsInChildren(includeInactive: true, RendScratch);
        int renderers = RendScratch.Count;
        int renderersVisible = 0;
        for (int i = 0; i < RendScratch.Count; i++)
        {
            Renderer r = RendScratch[i];
            if (r != null && r.isVisible)
                renderersVisible++;
        }
        RendScratch.Clear();

        int lights = go.GetComponentsInChildren<UnityEngine.Light>(true).Length;

        int zephyr = 0, objPos = 0, posToMat = 0, behaviours = 0;
        TypeNames.Clear();
        TypeCounts.Clear();
        BehaviourScratch.Clear();
        go.GetComponentsInChildren(includeInactive: true, BehaviourScratch);
        for (int i = 0; i < BehaviourScratch.Count; i++)
        {
            MonoBehaviour c = BehaviourScratch[i];
            if (c == null)
                continue;
            behaviours++;
            if (c is ZephyrAnim)
                zephyr++;
            else if (c is ObjectPosToMaterial)
                objPos++;
            else if (c is PosToMat)
                posToMat++;
            Tally(c.GetType().Name);
        }
        BehaviourScratch.Clear();

        int distinctTypes = TypeNames.Count;

        var sb = new System.Text.StringBuilder(2048);
        sb.Append("[Props] HELD-PROP ANIMATION HUSH for ").Append(label).Append(" — this mod now ")
          .Append("SUPPRESSES the prop's own animation for the length of the hold instead of ")
          .Append("trying to keep it playing correctly, which is the user's explicit call after ")
          .Append("four rounds failed to make it play right in a palm (.planning/held-prop-flash-experiments.md). ")
          .Append("SUPPRESSED — ANIMATORS: ").Append(b.Animators.Count).Append(" under the visual, ")
          .Append(animOff).Append(" switched off, ").Append(b.AnimatorsAlreadyOff)
          .Append(" already off, ").Append(b.AnimatorsLeftForRules)
          .Append(" LEFT RUNNING on purpose because their controller carries ")
          .Append("DelayedDeactivatePropAnimSMB, which sends a rules message for a sprung trap and ")
          .Append("holds a global 'deactivations in progress' flag — freezing that one would stall ")
          .Append("game state, and this lane does not write game state. REWOUND FIRST (strand 6, ")
          .Append("new 2026-09-06): ").Append(b.AnimatorsRewound)
          .Append(" of them were taken back to their BOUND DEFAULT VALUES with ")
          .Append("Animator.WriteDefaultValues BEFORE being switched off, and ")
          .Append(b.RewindSlotsChanged).Append(" of ").Append(b.RewindSlotsRead)
          .Append(" (material, property) slot(s) changed value because of it — a non-zero ")
          .Append("count there says the grab caught the clip AWAY from rest and that ")
          .Append("stopping the animator would have LATCHED that picture for the whole hold, ")
          .Append("which is a state and not a movement and is therefore invisible to every ")
          .Append("'did anything move' reading this file has ever taken. ")
          .Append(b.RewindSkipped).Append(" could not be rewound (inactive object or no ")
          .Append("controller). SUPPRESSED — OUTLINES: ")
          .Append(b.Outlines.Count).Append(" EPOOutline.Outlinable under the visual, ")
          .Append(outlineOff).Append(" switched off, ").Append(b.OutlinesAlreadyOff)
          .Append(" already off. That count is itself an ANSWER: no round has ever measured ")
          .Append("whether a held chest carries one, and the game raises a glowing silhouette ")
          .Append("through WorldspaceUITools.EnableHoveredOutline. The component's own enabled ")
          .Append("flag is taken and not OutlineParameters.Enabled, because the game writes that ")
          .Append("property in nine places and would stomp us, and because it is only one of the ")
          .Append("three parameter blocks an Outlinable can draw from. NOT SUPPRESSED — ")
          .Append("SkinnedMeshRenderer.updateWhenOffscreen: ").Append(b.Skins.Count)
          .Append(" skin(s), ").Append(skinChanged).Append(" set true, ").Append(b.SkinsAlready)
          .Append(" already true — that one is not an animation term at all, it keeps a held prop ")
          .Append("DRAWN when its stale root-bone bounds leave the frustum, and a frozen animator ")
          .Append("makes those bounds staler still. ");

        // ROUND FIVE, AND THE PRE-COUNTS ARE WHAT MAKE TWO NEW SUPPRESSIONS ATTRIBUTABLE.
        sb.Append("SUPPRESSED — Behaviour-DERIVED EMITTERS (new 2026-09-05, and the reason is a "
                  + "TYPE boundary rather than a new idea): ").Append(b.LightsFound)
          .Append(" Light(s) of which ").Append(b.LightsOn0).Append(" were ENABLED, ")
          .Append(b.ProjectorsFound).Append(" Projector(s) of which ").Append(b.ProjectorsOn0)
          .Append(" were enabled, ").Append(b.FlaresFound).Append(" LensFlare(s) of which ")
          .Append(b.FlaresOn0).Append(" were enabled — all switched off for the hold, each "
                  + "restored to its own remembered value. Light derives from Behaviour and NOT "
                  + "from MonoBehaviour, so the histogram at the end of this very line cannot name "
                  + "one and the two typed sweeps above cannot reach one; ModBuild 151 already lost "
                  + "a round to that exact hole ('a live point light inside the creature, intensity "
                  + "20.00 at range 1.0 m'). A lamp that is unremarkable on a hex a metre and a half "
                  + "away is a lamp twenty centimetres from the eye once the prop is in a palm. "
                  + "RFX4_LightCurves — which the histogram below DOES name — writes light.intensity "
                  + "from an AnimationCurve every frame off Time.time and loops for ever, and this "
                  + "class switches the LIGHT off rather than that writer, because disabling the "
                  + "writer would pin the intensity at whatever it last wrote. ")
          .Append("SUPPRESSED — PARTICLE SYSTEMS: ").Append(b.ParticlesFound)
          .Append(" under the visual of which ").Append(b.ParticlesPlaying0)
          .Append(" were PLAYING and were stopped-and-CLEARED for the hold (a pause would leave "
                  + "the live particles hanging in the air, which is a frozen shimmer rather than "
                  + "an absent one); the ones already stopped are untouched and are never "
                  + "restarted. THE TWO PRE-COUNTS ABOVE ARE THE ATTRIBUTION: a class whose "
                  + "pre-count is 0 made no writes and cannot be why anything changed either way. ")
          .Append("SUPPRESSED — SCREEN-SPACE OCCLUSION REGISTRATION (new 2026-09-06, and it is the "
                  + "FIRST thing this class has ever switched off that does not paint from inside "
                  + "the prop): ").Append(b.OcclusionFound)
          .Append(" ObjectOcclusionVolume(s) under the visual of which ").Append(b.OcclusionOn0)
          .Append(" were ENABLED and are switched off for the hold, each restored to its own "
                  + "remembered value. ObjectOcclusionVolume.OnEnable hands the prop's MeshRenderer "
                  + "to TilesOcclusionGenerator.s_Instance.AddObjectRenderer — a list held on a "
                  + "CAMERA, which draws every entry with m_OcclusionObjectMaterial into a "
                  + "QUARTER-resolution target inside a CommandBuffer at CameraEvent.BeforeGBuffer, "
                  + "blurs it twice and publishes it as the GLOBAL texture _ObjectOcclusion. That "
                  + "is not an Animator, a Light, a ParticleSystem, an Outlinable or a material "
                  + "property, and it hangs off no object in this prop's hierarchy, so every "
                  + "instrument this file has shipped in five rounds was structurally blind to it "
                  + "— and the ModBuild 448 HELD? line named 'the prop shader's own screen-space "
                  + "occlusion term' as the next suspect without being able to test it. On a hex "
                  + "the prop's footprint in that map is small and still; in a palm it fills the "
                  + "eye and MOVES every frame, so its own blurred quarter-res silhouette sweeps "
                  + "across it — a soft moving wash with no animator and no lamp in it. The "
                  + "component's own enabled flag is taken rather than the generator's list, "
                  + "because OnDisable/OnEnable ARE the game's add/remove pair and they set "
                  + "m_RenderersUpdated so the command buffer is rebuilt; no game state is "
                  + "written. ");

        sb.Append("STILL ALIVE UNDER THIS PROP, AND THIS IS THE PART THE NEXT ROUND NEEDS: ")
          .Append(renderers).Append(" renderer(s) of which ").Append(renderersVisible)
          .Append(" reported isVisible (READ IT AS THE PREVIOUS FRAME'S ANSWER — isVisible is last "
                  + "frame's culling result and the prop has only just been reparented, so it "
                  + "describes the hex and not the hand); ")
          .Append(particles).Append(" particle system(s) of which ")
          .Append(particlesPlaying).Append(" playing; ").Append(lights).Append(" light(s); ")
          .Append("world-anchor feeders ZephyrAnim x").Append(zephyr)
          .Append(", ObjectPosToMaterial x").Append(objPos).Append(", PosToMat x").Append(posToMat)
          .Append(" (PosToMat pushes _ObjPosY into a material EVERY FRAME by itself, so a non-zero ")
          .Append("count there is a live per-frame material writer on a held prop that no round ")
          .Append("has ruled in or out); ").Append(behaviours).Append(" MonoBehaviour(s) in ")
          .Append(distinctTypes).Append(" distinct type(s)");

        // A TRUNCATED LIST IS NOT ABSENCE. Whether the list is complete is stated in words, not
        // left to an ellipsis: this project has already read "X never appears" off a capped census
        // and been wrong. The distinct count above is the whole population either way.
        int listed = distinctTypes < TypeListCap ? distinctTypes : TypeListCap;
        if (distinctTypes == 0)
        {
            sb.Append(" (none — this prop carries no MonoBehaviour at all, which is itself an "
                      + "answer: whatever the flash is, it is not a component on this object)");
        }
        else
        {
            sb.Append(listed == distinctTypes
                ? ", and ALL of them are named here: "
                : ", of which only the first " + listed + " are named here — THE LIST IS "
                  + "TRUNCATED and the distinct count above is the whole population: ");
            for (int i = 0; i < listed; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(TypeNames[i]).Append(" x").Append(TypeCounts[i]);
            }
        }

        sb.Append(". WHY THIS ENUMERATION EXISTS, AND THE PARAGRAPH THAT USED TO STAND HERE IS NOW ")
          .Append("WRONG: it said the prop's animator was IDLE IN BOTH WINDOWS, which was a "
                  + "reading of ModBuild 435/436. The 447 log says otherwise — 'Trap_BearTrap_PR' "
                  + "clipRate home=0.202/s, anyLayerRate home=0.201/s, advancing home=359/360, "
                  + "hand=0/655, layerCount hand=0 — i.e. the prop runs a CONTINUOUS ~5 s loop on "
                  + "its hex and this class stops it dead in the hand (a disabled Animator reports "
                  + "layerCount 0, which is why the hand column reads n/a and not because anything "
                  + "went unmeasured). The animator strand WORKS and it is not the shimmer, and the "
                  + "material read-back that agreed with it tracks only 20 of Amp_Char_Shader's 57 "
                  + "declared properties, which is the same truncation that cost ModBuild 151 a "
                  + "build on this same shader. Read the '] [Props] HELD-PROP PAINT AFTER HUSH' "
                  + "line for this prop: it is taken AFTER everything above was written, it reads "
                  + "the whole property table, and it counts the lights and particles this line "
                  + "only enumerates. ")
          .Append(_logsLeft).Append(" more prop hush line(s) this session, at most one per prop kind.");

        // HW-VERIFY: this line IS the round's deliverable — it is the first census anyone has taken
        // of what a held prop actually carries, and the standing question ("what is the flash, if
        // it is not the animator?") is answered by reading it. It must stay at a tier the DEFAULT
        // log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("FigureGrab", sb.ToString());
    }

    /// <summary>Has this prop KIND already printed its census? The roster is per SCENARIO and
    /// <see cref="Reset"/> clears it, because a new scenario is a new hardware question.</summary>
    private static bool KindSpent(string label) => Spent(KindsDone, label);

    // ---- the POST-HUSH verdict --------------------------------------------------------------------

    /// <summary>
    /// Arm the window that watches a prop AFTER it has been hushed. Pure instrument: it reads and
    /// it prints, and nothing outside this region reads a field it writes.
    ///
    /// <para><b>WHY IT EXISTS, IN ONE SENTENCE.</b> The grab-edge census said the hush fired and
    /// the user said the shimmer was still there, so the only reading worth having is one taken
    /// with the suppression already in place. A line that says "I hushed it" is worthless against
    /// that report; this one names, per class, what is still enabled, still playing and still
    /// moving on a prop this class has just silenced.</para>
    /// </summary>
    private static void ArmVerdict(Belt b, string label)
    {
        if (_vBelt != null || _verdictsLeft <= 0 || Spent(VerdictKindsDone, label))
            return;
        GameObject? go = b.Visual;
        if (go == null)
            return;

        _verdictsLeft--;
        if (VerdictKindsDone.Count < KindCap)
            VerdictKindsDone.Add(label);

        _vBelt = b;
        _vLabel = label;
        _vFrames = 0;
        _vEndFrame = Time.frameCount + VerdictFrames;

        _vAnimators = Take(go.GetComponentsInChildren<Animator>(true), out _vFoundAnimators);
        _vOutlines = Take(go.GetComponentsInChildren<Outlinable>(true), out _vFoundOutlines);
        _vLights = Take(go.GetComponentsInChildren<UnityEngine.Light>(true), out _vFoundLights);
        _vParticles = Take(go.GetComponentsInChildren<ParticleSystem>(true), out _vFoundParticles);
        _vRenderers = Take(go.GetComponentsInChildren<Renderer>(true), out _vFoundRenderers);

        _vAnimOnMax = _vOutOnMax = _vLightOnMax = _vEmitterOnMax = 0;
        _vPartPlayingMax = _vPartAliveMax = _vRendDrawMax = _vRendVisMax = 0;
        _vLightIntensityMax = 0f;
        for (int i = 0; i < VerdictObjCap; i++)
        {
            VLightMoves[i] = VAnimMoves[i] = 0;
            VLightLo[i] = float.MaxValue;
            VLightHi[i] = float.MinValue;
            VLightPrev[i] = float.NaN;
            VAnimPrev[i] = float.NaN;
        }
        for (int i = 0; i < VPrev.Length; i++)
        {
            VSeen[i] = false;
            VMoves[i] = 0;
            VLo[i] = float.MaxValue;
            VHi[i] = float.MinValue;
        }

        _vOccGenSeen = false;
        _vOccProbeAt = 0;
        _vOccListMax = _vOccStillInMax = _vOccMapsBoundMax = _vOccMapRebinds = 0;
        _vOccMapId = 0;
        _vOccEnableLo = float.MaxValue;
        _vOccEnableHi = float.MinValue;
        SweepForeignEmitters(go);

        ResolveVerdictMaterials();
        // ROUND EIGHT. Armed AFTER the material table is resolved, because the roster reads
        // the property blocks through that table's ids and the dissolve channels off its names.
        ArmRoster(go);
    }

    /// <summary>
    /// THE OUTWARD SWEEP — every <c>Light</c> in the SCENE that is not under the held prop and is
    /// close enough to put light on it. This is the arm no previous round had: a light on the HAND,
    /// on the rig, or on a POOLED effect object parented to the scene and merely positioned to
    /// follow the prop is invisible to <c>GetComponentsInChildren</c> by construction, and this
    /// project's own ruling is that <c>GetComponentIn{Parent,Children}</c> answers "related to an
    /// X", never "IS an X".
    ///
    /// <para>Run TWICE per verdict — once when the window arms and once when it closes — and never
    /// per frame. <c>FindObjectsOfType</c> on a per-frame path has cost this project two rounds
    /// and one 12.6 ms frame, and the question here ("what stands near the thing in his palm")
    /// does not need 90 Hz to be answered.</para>
    /// </summary>
    private static void SweepForeignEmitters(GameObject go)
    {
        _vForeignFound = _vForeignNear = _vForeignOn = 0;
        VForeignNames.Clear();

        Transform propT = go.transform;
        Vector3 centre = propT.position;
        float reach = ForeignReachWorld;

        // The prop's own drawn extent, so a big prop is not measured from a pivot that may sit off
        // the mesh entirely. Renderer bounds are world-space and already account for the reparent.
        var box = new Bounds(centre, Vector3.zero);
        bool boxed = false;
        Renderer[] mine = go.GetComponentsInChildren<Renderer>(includeInactive: true);
        for (int i = 0; i < mine.Length; i++)
        {
            Renderer r = mine[i];
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                continue;
            if (!boxed) { box = r.bounds; boxed = true; }
            else box.Encapsulate(r.bounds);
        }
        if (boxed)
            centre = box.center;

        UnityEngine.Light[] all = Object.FindObjectsOfType<UnityEngine.Light>();
        _vForeignFound = all.Length;
        for (int i = 0; i < all.Length; i++)
        {
            UnityEngine.Light l = all[i];
            if (l == null)
                continue;
            // FOREIGN means "not under the prop". The prop's own lamps are strand 3's business and
            // are already counted, named and switched off; counting them again here would make the
            // one number this arm exists for unreadable.
            if (l.transform.IsChildOf(propT))
                continue;
            float d = boxed
                ? Mathf.Sqrt(box.SqrDistance(l.transform.position))
                : Vector3.Distance(centre, l.transform.position);
            if (d > reach)
                continue;
            _vForeignNear++;
            bool on = l.enabled && l.gameObject.activeInHierarchy && l.intensity > 0f;
            if (on)
                _vForeignOn++;
            if (VForeignNames.Count >= ForeignNameCap)
                continue;
            // The PATH, not the name. "Point light (2)" names nothing; what decides this is which
            // subtree it hangs off — the rig, the hand, an effect root, a pooled object.
            VForeignNames.Add($"'{Describe(l.transform)}' {l.type}, intensity {l.intensity:0.##}, "
                              + $"range {l.range:0.##}, {(on ? "ON" : "off")}, {d:0.00} wu away");
        }
    }

    /// <summary>Root-down hierarchy path, capped, so a foreign emitter is attributable to the
    /// system that owns it rather than to a bare object name.</summary>
    private static string Describe(Transform t)
    {
        string path = t.name;
        Transform? p = t.parent;
        for (int depth = 0; depth < 4 && p != null; depth++)
        {
            path = p.name + "/" + path;
            p = p.parent;
        }
        return path;
    }

    /// <summary>Cap an array at <see cref="VerdictObjCap"/>, reporting the population it came
    /// from. A truncated list is not absence and every one of these is printed as
    /// sampled-of-found.</summary>
    private static T[] Take<T>(T[] all, out int found)
    {
        found = all.Length;
        if (all.Length <= VerdictObjCap)
            return all;
        var cut = new T[VerdictObjCap];
        System.Array.Copy(all, cut, VerdictObjCap);
        return cut;
    }

    /// <summary>
    /// Resolve the materials and the property table read back each frame.
    ///
    /// <para><b><c>sharedMaterials</c>, NEVER <c>material</c>.</b> <c>Renderer.material</c>
    /// INSTANTIATES a clone the first time it is touched, which is a permanent change to the scene
    /// made by an instrument. It is also unnecessary: once anything — an animation curve on
    /// <c>material._X</c>, an SMB writing through <c>.material</c> — has instanced a renderer's
    /// material, Unity stores that clone back into the renderer, and <c>sharedMaterials</c>
    /// afterwards returns the CLONE. So this reads the instanced values without ever creating
    /// one. (<c>PropAnimWatch</c>'s header lists "a write through .material is invisible to
    /// sharedMaterial" as a blind spot; it is not one, and the real blind spot was the twenty-slot
    /// cap next to it.)</para>
    ///
    /// <para>Both material SLOTS of every renderer are eligible, not just <c>sharedMaterial</c>
    /// (which is only the first): a chest whose glint lives on its second submesh would be
    /// invisible to a first-slot-only read.</para>
    /// </summary>
    private static void ResolveVerdictMaterials()
    {
        GameObject? go = _vBelt != null ? _vBelt.Visual : null;
        if (go == null)
            return;
        // Walked off the PROP, not off the capped renderer array the other strands sample: a
        // renderer past VerdictObjCap still draws, and its material still paints.
        VTable.Resolve(go);
    }

    /// <summary>One frame of the post-hush window. No allocation: every buffer is static and the
    /// component sets were resolved when the window armed.</summary>
    private static void SampleVerdict(Belt b)
    {
        if (b.Visual == null)
            return;
        _vFrames++;

        int animOn = 0;
        for (int i = 0; i < _vAnimators.Length; i++)
        {
            Animator a = _vAnimators[i];
            if (a == null || !a.enabled)
                continue;
            animOn++;
            // A DISABLED ANIMATOR REPORTS layerCount 0 — that is why PropAnimWatch's hand column
            // reads n/a on this build and it is not a missing measurement. Only an animator that
            // is still ON can advance, and the only ones this class leaves on are the rules ones.
            if (a.layerCount <= 0)
                continue;
            float t = a.GetCurrentAnimatorStateInfo(0).normalizedTime;
            if (!float.IsNaN(VAnimPrev[i]) && Mathf.Abs(t - VAnimPrev[i]) > MoveEpsilon)
                VAnimMoves[i]++;
            VAnimPrev[i] = t;
        }
        if (animOn > _vAnimOnMax)
            _vAnimOnMax = animOn;

        int outOn = 0;
        for (int i = 0; i < _vOutlines.Length; i++)
        {
            Outlinable o = _vOutlines[i];
            if (o != null && o.enabled)
                outOn++;
        }
        if (outOn > _vOutOnMax)
            _vOutOnMax = outOn;

        int lightOn = 0;
        for (int i = 0; i < _vLights.Length; i++)
        {
            UnityEngine.Light l = _vLights[i];
            if (l == null)
                continue;
            if (l.enabled && l.gameObject.activeInHierarchy)
            {
                lightOn++;
                if (l.intensity > _vLightIntensityMax)
                    _vLightIntensityMax = l.intensity;
            }
            // The INTENSITY is sampled whether or not the lamp is switched on, and that is the
            // point: a curve still writing a rising number into a light this class has disabled is
            // the reading that says the WRITER survived the suppression and would re-light the
            // moment anything re-enabled the component.
            float v = l.intensity;
            if (!float.IsNaN(VLightPrev[i]) && Mathf.Abs(v - VLightPrev[i]) > MoveEpsilon)
                VLightMoves[i]++;
            VLightPrev[i] = v;
            if (v < VLightLo[i]) VLightLo[i] = v;
            if (v > VLightHi[i]) VLightHi[i] = v;
        }
        if (lightOn > _vLightOnMax)
            _vLightOnMax = lightOn;

        int emitterOn = 0;
        for (int i = 0; i < b.Emitters.Count; i++)
        {
            Behaviour e = b.Emitters[i];
            if (e != null && e.enabled)
                emitterOn++;
        }
        if (emitterOn > _vEmitterOnMax)
            _vEmitterOnMax = emitterOn;

        int playing = 0, alive = 0;
        for (int i = 0; i < _vParticles.Length; i++)
        {
            ParticleSystem p = _vParticles[i];
            if (p == null)
                continue;
            if (p.isPlaying)
                playing++;
            alive += p.particleCount;
        }
        if (playing > _vPartPlayingMax) _vPartPlayingMax = playing;
        if (alive > _vPartAliveMax) _vPartAliveMax = alive;

        int drawing = 0, visible = 0;
        for (int i = 0; i < _vRenderers.Length; i++)
        {
            Renderer r = _vRenderers[i];
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                continue;
            drawing++;
            if (r.isVisible)
                visible++;
        }
        if (drawing > _vRendDrawMax) _vRendDrawMax = drawing;
        if (visible > _vRendVisMax) _vRendVisMax = visible;

        // EVERY property class, not float/range/colour only. A VECTOR or a TEXTURE rewritten
        // every frame is what the ModBuild 450 line named as its own blind spot, and a term that
        // is LATCHED rather than moving is why the min/max range beside each mover matters as much
        // as the count.
        VTable.Sample(VNow, VNowValid);
        for (int slot = 0; slot < VTable.Capacity; slot++)
        {
            if (!VNowValid[slot])
                continue;
            Vector4 v = VNow[slot];
            if (VSeen[slot] && Moved(VTable.Kind[slot], VPrev[slot], v))
                VMoves[slot]++;
            VPrev[slot] = v;
            VSeen[slot] = true;
            float f = Fold(VTable.Kind[slot], v);
            if (f < VLo[slot]) VLo[slot] = f;
            if (f > VHi[slot]) VHi[slot] = f;
        }

        SampleOutward();
        SampleRoster(b);

        if (Time.frameCount >= _vEndFrame)
            CloseVerdict(b, "the window ran to its full length with the prop still in the hand");
    }

    /// <summary>
    /// One frame of the OUTWARD arm: the screen-space occlusion map, which is published as GLOBAL
    /// shader state by a CommandBuffer on a camera and is therefore the one painter that no census
    /// rooted at the prop can reach.
    ///
    /// <para>Two readings, and they answer different halves. The MEMBERSHIP reading says whether
    /// this prop is still being DRAWN INTO the map (strand 5 should have taken it out — a non-zero
    /// count is that strand failing, out loud). The GLOBAL reading says whether the map is still
    /// being GENERATED AND BOUND at all while the prop is in the hand, which decides whether the
    /// prop is still SAMPLING it even after it stopped contributing to it — and those are two
    /// different fixes.</para>
    ///
    /// <para>Membership is tested on a cadence because the generator's list is scene-sized; the
    /// global reads are three id lookups and are taken every frame.</para>
    /// </summary>
    private static void SampleOutward()
    {
        float enable = Shader.GetGlobalFloat(IdEnableOcclusionMap);
        if (enable < _vOccEnableLo) _vOccEnableLo = enable;
        if (enable > _vOccEnableHi) _vOccEnableHi = enable;

        Texture? objMap = Shader.GetGlobalTexture(IdObjectOcclusion);
        Texture? tileMap = Shader.GetGlobalTexture(IdTilesOcclusionMap);
        int bound = (objMap != null ? 1 : 0) + (tileMap != null ? 1 : 0);
        if (bound > _vOccMapsBoundMax)
            _vOccMapsBoundMax = bound;

        // A CHANGING TEXTURE IDENTITY IS THE MAP BEING REBUILT. The generator allocates its targets
        // through GetTemporaryRT inside the command buffer, so a live map re-binds; a frozen id
        // across the whole window means the buffer is not running and this strand is inert.
        if (objMap != null)
        {
            int id = objMap.GetInstanceID();
            if (_vOccMapId != 0 && id != _vOccMapId)
                _vOccMapRebinds++;
            _vOccMapId = id;
        }

        if (Time.frameCount < _vOccProbeAt)
            return;
        _vOccProbeAt = Time.frameCount + OcclusionProbeFrames;

        TilesOcclusionGenerator? gen = TilesOcclusionGenerator.s_Instance;
        if (gen == null)
            return;
        _vOccGenSeen = true;

        List<MeshRenderer> list = gen.m_ObjectRenderers;
        if (list == null)
            return;
        if (list.Count > _vOccListMax)
            _vOccListMax = list.Count;

        int stillIn = 0;
        for (int i = 0; i < _vRenderers.Length; i++)
        {
            if (_vRenderers[i] is not MeshRenderer mr || mr == null)
                continue;
            for (int k = 0; k < list.Count; k++)
            {
                if (!ReferenceEquals(list[k], mr))
                    continue;
                stillIn++;
                break;
            }
        }
        if (stillIn > _vOccStillInMax)
            _vOccStillInMax = stillIn;
    }

    /// <summary>Close and emit the armed window, if <paramref name="b"/> is the belt that armed
    /// it. Idempotent and safe on any belt.</summary>
    private static void CloseVerdict(Belt b, string why)
    {
        if (!ReferenceEquals(b, _vBelt))
            return;
        _vBelt = null;
        if (_vFrames > 0)
        {
            EmitPostHushVerdict(b, why);
            // A SECOND LINE AND NOT A SECOND SECTION, on purpose: the verdict above is already
            // ten kilobytes and this one has its own grep token so a hardware round can pull it
            // alone. Anchor every grep on "] " - this file quotes other instruments' tokens
            // inside its own prose and an unanchored grep counts the explanation as a hit.
            EmitRoster(b);
        }
    }

    /// <summary>
    /// THE LINE THIS ROUND EXISTS FOR. What a held prop still carries and still does AFTER the
    /// hush has written everything it writes.
    /// </summary>
    private static void EmitPostHushVerdict(Belt b, string why)
    {
        var sb = new System.Text.StringBuilder(3072);
        sb.Append("[Props] HELD-PROP PAINT AFTER HUSH for ").Append(_vLabel)
          .Append(" — ").Append(_vFrames).Append(" frame(s) sampled with the suppression ALREADY IN "
                  + "PLACE, closed because ").Append(why)
          .Append(". THIS IS THE FALSIFIER FOR THE WHOLE HUSH: ModBuild 445 shipped it, the "
                  + "grab-edge census printed green on both machines, and the user reported the "
                  + "white shimmer STILL ON the asset in his hand. A census of what was switched "
                  + "off cannot answer that; only a reading taken afterwards can. ");

        sb.Append("ANIMATORS: ").Append(_vAnimators.Length).Append(" sampled of ")
          .Append(_vFoundAnimators).Append(" found, at most ").Append(_vAnimOnMax)
          .Append(" enabled on any frame (the ").Append(b.AnimatorsLeftForRules)
          .Append(" left running for DelayedDeactivatePropAnimSMB are inside that number and are "
                  + "supposed to be), advancing on ").Append(MaxOf(VAnimMoves, _vAnimators.Length))
          .Append(" frame(s) at worst. ");

        sb.Append("OUTLINES: ").Append(_vOutlines.Length).Append(" sampled of ")
          .Append(_vFoundOutlines).Append(" found, at most ").Append(_vOutOnMax)
          .Append(" enabled on any frame. Outlinable.OnDisable removes the component from the "
                  + "static list the outline pass walks and UpdateVisibility refuses to re-add a "
                  + "disabled one (Outlinable.cs:236-245, 264-270), so a zero here means the "
                  + "outline genuinely did not draw and the shimmer is not the outline. ");

        sb.Append("LIGHTS — AND THIS IS THE CLASS NO ROUND HAS EVER MEASURED: ")
          .Append(_vLights.Length).Append(" sampled of ").Append(_vFoundLights)
          .Append(" found (").Append(b.LightsOn0)
          .Append(" of them were ENABLED before this build wrote anything — that pre-count is what "
                  + "says whether this strand had anything to do at all), at most ")
          .Append(_vLightOnMax).Append(" lit on any frame, brightest intensity seen while lit ")
          .Append(_vLightIntensityMax.ToString("0.###")).Append(". ")
          .Append("Light derives from Behaviour, NOT from MonoBehaviour, so every "
                  + "GetComponentsInChildren<MonoBehaviour>() census this project has ever run "
                  + "walked past one structurally — that is ModBuild 151's 'LivingSpirit_Light, "
                  + "Point, intensity 20.00, range 1.0 m' word for word. RFX4_LightCurves.Update "
                  + "writes light.intensity from an AnimationCurve every frame off Time.time and "
                  + "re-seeds itself for ever when IsLoop is set, which is a shimmer with no "
                  + "animator, no material property and no renderer in it. ");
        AppendMovers(sb, "INTENSITY STILL MOVING ON A HUSHED LIGHT (the writer survived the "
                          + "suppression even though the lamp is off)", VLightMoves, VLightLo, VLightHi,
                     _vLights.Length, _vLights.Length, null);

        sb.Append(" OTHER Behaviour-DERIVED EMITTERS: ").Append(b.ProjectorsFound)
          .Append(" Projector(s) (").Append(b.ProjectorsOn0).Append(" were on), ")
          .Append(b.FlaresFound).Append(" LensFlare(s) (").Append(b.FlaresOn0)
          .Append(" were on); ").Append(_vEmitterOnMax)
          .Append(" of the whole emitter ledger was enabled on the worst frame, and that number "
                  + "must be 0 — a non-zero reading means something re-enabled an emitter under a "
                  + "held prop and the 45-frame rescan does not re-suppress an object already in "
                  + "the ledger. ");

        sb.Append("PARTICLES: ").Append(_vParticles.Length).Append(" sampled of ")
          .Append(_vFoundParticles).Append(" found (").Append(b.ParticlesPlaying0)
          .Append(" were PLAYING before this build stopped them), at most ")
          .Append(_vPartPlayingMax).Append(" playing on any frame and at most ")
          .Append(_vPartAliveMax).Append(" live particle(s) on screen at once. ");

        sb.Append("RENDERERS: ").Append(_vRenderers.Length).Append(" sampled of ")
          .Append(_vFoundRenderers).Append(" found, at most ").Append(_vRendDrawMax)
          .Append(" drawing and ").Append(_vRendVisMax).Append(" reported isVisible. ");

        sb.Append("MATERIAL PROPERTIES, READ WHOLE AND NOW INCLUDING VECTORS AND TEXTURES: ")
          .Append(VTable.MatCount).Append(" material(s) sampled of ").Append(VTable.MatFound)
          .Append(" found, EACH READ THROUGH ITS OWN SHADER'S TABLE (")
          .Append(VTable.Describe()).Append("), ").Append(VTable.Slots)
          .Append(" slot(s) of ").Append(VTable.Declared).Append(" declared property/ies tracked");
        if (VTable.Truncated)
            sb.Append(" — THE TABLE IS TRUNCATED at this class's own caps of ")
              .Append(VerdictMatCap).Append(" material(s) x ").Append(VerdictPropCap)
              .Append(" propert(y/ies), raise them before believing a zero below");
        sb.Append(". Two things changed here on 2026-09-06 and both were named as blind spots by "
                  + "the line they replace: VECTOR and TEXTURE properties are tracked now, and each "
                  + "material is read through the property table of ITS OWN shader instead of "
                  + "through the first material's — the old line printed 'N material(s) on shader "
                  + "X' for materials that need not have shared X, and every id X did not declare "
                  + "was skipped in silence. PropAnimWatch still tracks TWENTY of "
                  + "Amp_Char_Shader's fifty-seven, which is why four rounds read 'not one tracked "
                  + "slot moved' and meant nothing by it. ");
        AppendMovers(sb, "PROPERTIES THAT MOVED AFTER THE HUSH", VMoves, VLo, VHi,
                     VTable.Capacity, VTable.Slots, VTable.Name);

        AppendRewind(sb, b);

        AppendOutward(sb, b);

        sb.Append(" HOW TO READ THIS, AND WHAT WOULD MEAN THE FIX IS INERT. The subtree half is "
                  + "WORKING if lights-lit-on-any-frame is 0 with a non-zero pre-count of enabled "
                  + "lights, particles-playing is 0 with a non-zero pre-count of playing systems, "
                  + "emitters-enabled is 0, outlines-enabled is 0, animators-enabled is no more "
                  + "than the rules exception, and no material property moved. ModBuild 448 "
                  + "produced exactly that reading on BOTH machines and the user still reported "
                  + "the effect, which is why the OUTWARD section above now exists: every "
                  + "pre-count was non-zero, so the strands ran, and every after-count was zero, "
                  + "so the subtree really is dark. STRAND 5 IS WORKING if occlusion-volumes "
                  + "found is non-zero, of which a non-zero number were ON before this build "
                  + "wrote anything, AND still-registered-on-any-frame is 0; it is INERT if "
                  + "occlusion-volumes-found is 0. ModBuild 450 read exactly the WORKING shape for "
                  + "the trap (1 volume, 2 on beforehand, 0 still registered) and the user "
                  + "reported the defect unchanged, so strand 5 is not the painter either. "
                  + "STRAND 6 — THE ONE THIS ROUND EXISTS FOR — IS WORKING if animators-rewound "
                  + "is non-zero AND the slots-changed count beside it is non-zero: that pair says "
                  + "the freeze really was latching a non-resting picture, which is a STATE and "
                  + "therefore invisible to every 'did anything move' reading above it. It is "
                  + "INERT if animators-rewound is 0 (nothing to stop on this prop) or if "
                  + "slots-changed is 0 (the clip was already at rest at the grab). AND THIS IS "
                  + "THE READING THAT WOULD MEAN THE PAINTER IS STILL BEYOND THIS INSTRUMENT: "
                  + "animators rewound with a NON-ZERO slots-changed count — so the latch existed "
                  + "and has been removed — occlusion volumes taken and still-registered 0, no "
                  + "material property of ANY class (float, colour, VECTOR or TEXTURE) moving, no "
                  + "foreign lamp near the prop, every subtree class 0, AND THE PROP STILL "
                  + "REPORTED WHITE. That combination has removed the last state this file can "
                  + "reach, and what is left is what no state probe can see: a "
                  + "MaterialPropertyBlock written per frame (the HELD? probe samples blocks ONCE, "
                  + "two frames after the grab, and read 0 for the trap), a shader KEYWORD, the "
                  + "per-renderer light-probe SH and reflection probe Unity re-picks when a "
                  + "renderer moves across the room, or a replacement-shader/post pass drawing the "
                  + "whole frame. The round after that must measure the PICTURE — a per-eye frame "
                  + "difference with the prop held still versus moving, and with the prop grabbed "
                  + "at two different phases of its idle loop — because state probes cannot see "
                  + "sampling. ")
          .Append(_verdictsLeft).Append(" more post-hush verdict(s) this session, at most one per prop kind.");

        // HW-VERIFY: this line is the round-five deliverable and the falsifier for the ModBuild 445
        // hush, which passed its own census while the defect stood. It reports what was observed
        // AFTER the suppression, per class, with the pre-state beside it so a fix that changed
        // nothing cannot read as a fix that worked. It must stay at a tier the DEFAULT log level
        // prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("FigureGrab", sb.ToString());
    }

    /// <summary>
    /// THE OUTWARD SECTION — the half of the picture that is not under the prop.
    ///
    /// <para>Everything above this point is a census rooted at the held prop, and after five
    /// rounds that shape has been exhausted: on ModBuild 448 it read zero on every class, on both
    /// machines, while the user reported the effect unchanged. This section reports the two things
    /// that can paint a held prop from outside it and that no earlier build measured — the game's
    /// screen-space occlusion map, published as GLOBAL shader state by a CommandBuffer on a camera,
    /// and every Light in the scene standing near the prop but not under it.</para>
    /// </summary>
    private static void AppendOutward(System.Text.StringBuilder sb, Belt b)
    {
        sb.Append(" OUTWARD — WHAT IS PAINTING FROM OUTSIDE THE PROP'S SUBTREE, AND NO ROUND HAS "
                  + "EVER MEASURED THIS AT ALL. SCREEN-SPACE OCCLUSION: ")
          .Append(b.OcclusionFound).Append(" ObjectOcclusionVolume(s) under the visual, of which ")
          .Append(b.OcclusionOn0)
          .Append(" were ENABLED before this build wrote anything (that pre-count is what says "
                  + "whether this strand had anything to do); at most ").Append(_vOccStillInMax)
          .Append(" of this prop's MeshRenderer(s) were STILL IN TilesOcclusionGenerator's "
                  + "m_ObjectRenderers on any sampled frame, and that number must be 0 — a "
                  + "non-zero reading means the prop is still being drawn into the map it is "
                  + "sampling. The generator ")
          .Append(_vOccGenSeen ? "WAS present" : "was NEVER present (s_Instance null — this scene "
                                                 + "does not run the occlusion pass and the whole strand is inert here)")
          .Append(", its object list peaked at ").Append(_vOccListMax)
          .Append(" renderer(s) scene-wide. THE GLOBAL SLOTS IT PUBLISHES, read with "
                  + "Shader.GetGlobal* because a GLOBAL is invisible to every material read-back "
                  + "above: _EnableOcclusionMap ranged ");
        if (_vOccEnableLo > _vOccEnableHi)
            sb.Append("<never sampled>");
        else
            sb.Append(_vOccEnableLo.ToString("0.###")).Append("..").Append(_vOccEnableHi.ToString("0.###"));
        sb.Append("; ").Append(_vOccMapsBoundMax)
          .Append(" of the 2 map textures (_ObjectOcclusion, _TilesOcclusionMap) were bound on the "
                  + "worst frame; _ObjectOcclusion re-bound to a different texture on ")
          .Append(_vOccMapRebinds)
          .Append(" frame(s) — a NON-ZERO count there means the command buffer is live and the "
                  + "map is being regenerated every frame, i.e. the term is switched ON while the "
                  + "prop is in the hand; a ZERO with a bound map means the buffer is not running "
                  + "and this whole strand cannot be the painter. ");

        sb.Append("FOREIGN EMITTERS — every Light in the SCENE that is NOT under this prop and "
                  + "stands within ").Append(ForeignReachWorld.ToString("0.#"))
          .Append(" wu of its drawn box, swept once when the window armed: ").Append(_vForeignNear)
          .Append(" near of ").Append(_vForeignFound).Append(" in the scene, ").Append(_vForeignOn)
          .Append(" of them lit. This is the arm that can see a lamp on the HAND or on the RIG, or "
                  + "a POOLED effect object parented to the scene and merely positioned to follow "
                  + "the prop — none of which any GetComponentsInChildren census can reach, and "
                  + "this project's own ruling is that a containment test answers 'related to an "
                  + "X', never 'IS an X'. ");
        if (VForeignNames.Count == 0)
            sb.Append("NONE were near, so no foreign lamp is on this prop. ");
        else
            sb.Append("NAMED BY HIERARCHY PATH (up to ").Append(ForeignNameCap).Append("): ")
              .Append(string.Join("; ", VForeignNames)).Append(". ");

        sb.Append("PROPERTY CLASSES THIS READ-BACK CAN NOW SEE, AND THE ONES IT STILL CANNOT, "
                  + "stated so the next round does not read a zero as an absence. CLOSED on "
                  + "2026-09-06: VECTOR and TEXTURE properties are tracked, so a per-frame writer "
                  + "such as CustomObjectPositionToChildMaterials — which pushes the VECTOR "
                  + "_FadeSourcePos into every child material EVERY FRAME from a moving actor's "
                  + "world position (decompiled GH.Runtime/CustomObjectPositionToChildMaterials"
                  + ".cs:70-100) — would now show as a mover rather than as 'nothing moved'; and "
                  + "the grab-edge census's feeder count names that type too. STILL OPEN, in "
                  + "descending order of how much of the picture they own: (1) a "
                  + "MaterialPropertyBlock overrides a material value at DRAW time and appears in "
                  + "neither material nor sharedMaterial — the grab-edge HELD? probe counts the "
                  + "blocks and is the only reading on it, and it is a SNAPSHOT two frames after "
                  + "the grab rather than a per-frame one; (2) shader KEYWORDS, which switch whole "
                  + "branches on and off with no property moving at all; (3) per-renderer lighting "
                  + "bindings Unity re-picks when a renderer MOVES — light probe SH and the "
                  + "reflection probe — which change for a prop carried across the room and for no "
                  + "other reason; (4) any global slot not named in the OUTWARD section above. ");
    }

    /// <summary>
    /// STRAND 6'S OWN SECTION - what the hush was LATCHING, in values rather than in movement
    /// counts.
    ///
    /// <para>This is the reading six rounds did not have. Every material report in this file until
    /// now answered "did anything MOVE?", and a channel frozen by our own <c>Animator.enabled =
    /// false</c> does not move: it sits at whatever the clip happened to be showing on the frame of
    /// the grab, for the whole hold. The numbers here are the ones that decide it - how many
    /// (material, property) slots the rewind to bound defaults actually CHANGED, and which, with
    /// before-and-after values.</para>
    /// </summary>
    private static void AppendRewind(System.Text.StringBuilder sb, Belt b)
    {
        sb.Append(" THE FREEZE'S OWN LATCH (strand 6, new 2026-09-06, and it is the first thing "
                  + "this class has ever suppressed that this class itself caused): ")
          .Append(b.AnimatorsRewound)
          .Append(" animator(s) were taken back to their BOUND DEFAULT VALUES with "
                  + "Animator.WriteDefaultValues before being switched off, ").Append(b.RewindSkipped)
          .Append(" could not be (inactive object or no controller, so they were stopped the way "
                  + "ModBuild 445 stopped them). ACROSS THAT REWIND, ").Append(b.RewindSlotsChanged)
          .Append(" of ").Append(b.RewindSlotsRead)
          .Append(" (material, property) slot(s) CHANGED VALUE");
        if (b.RewindNamed.Length > 0)
            sb.Append(", naming up to ").Append(VerdictListCap).Append(": ").Append(b.RewindNamed);
        sb.Append(". READ IT LIKE THIS AND IN THIS ORDER. A NON-ZERO changed count is the whole "
                  + "finding: it says the prop's picture at the instant of the grab was NOT its "
                  + "resting picture, that stopping the animator would have latched that picture "
                  + "for the length of the hold, and that whether it looked wrong depended on WHEN "
                  + "in the loop the player grabbed it - which is the user's word 'manchmal', and "
                  + "it is a STATE and not a movement, so every 'nothing moved' this file has ever "
                  + "printed is consistent with it. A ZERO changed count with a non-zero rewound "
                  + "count means the animator's channels were already at rest at the grab, this "
                  + "strand wrote nothing visible on this hold, and it can be neither the cause of "
                  + "an improvement nor of a regression here. A ZERO REWOUND count means the prop "
                  + "carried no animator this class stops, and the strand is INERT for this prop "
                  + "kind. Note what the changed count does NOT depend on: nothing here needs to "
                  + "know WHICH channel the flash lives in, and that is the point - the ModBuild "
                  + "450 read-back proves it is none of the 48 float/range/colour properties while "
                  + "the clip advanced on 359 of 360 frames on the hex, so it is something else "
                  + "the clip drives, and WriteDefaultValues returns all of them at once. ONE MORE "
                  + "WAY TO READ A ZERO, and it names a different fix rather than a dead lead: a "
                  + "zero changed count on a prop whose clip was DEMONSTRABLY advancing on its hex "
                  + "(read anyLayerRate home on the ANIMATION A/B line for the same prop kind) does "
                  + "NOT say the clip was at rest — it says WriteDefaultValues did not land as an "
                  + "immediate write, because this class disables the animator in the same call and "
                  + "a deferred write would then never be evaluated. The lever for that reading is "
                  + "Animator.Rebind() before WriteDefaultValues, and the two readings are told "
                  + "apart by exactly that home rate. ");
    }

    /// <summary>Name the entries that MOVED, with the range each one covered, capped and with the
    /// total stated. Used for both the light intensities and the material property table.</summary>
    private static void AppendMovers(System.Text.StringBuilder sb, string title, int[] moves,
        float[] lo, float[] hi, int scan, int total, string[]? names)
    {
        int moved = 0;
        for (int i = 0; i < scan && i < moves.Length; i++)
        {
            if (moves[i] > 0)
                moved++;
        }
        sb.Append(title).Append(": ").Append(moved);
        if (moved == 0)
        {
            sb.Append(" — NONE of the ").Append(total).Append(" slot(s) moved on any frame.");
            return;
        }
        sb.Append(" of ").Append(total).Append(" slot(s), naming up to ").Append(VerdictListCap)
          .Append(": ");
        int listed = 0;
        for (int i = 0; i < scan && i < moves.Length && listed < VerdictListCap; i++)
        {
            if (moves[i] <= 0)
                continue;
            if (listed > 0)
                sb.Append(", ");
            if (names != null)
                sb.Append(names[i] ?? "<unnamed>").Append(" (mat")
                  .Append(i / VerdictPropCap).Append(' ').Append(VTable.ShaderOf(i / VerdictPropCap))
                  .Append(')');
            else
                sb.Append('#').Append(i);
            sb.Append(" moved on ").Append(moves[i]).Append(" frame(s), ")
              .Append(lo[i].ToString("0.####")).Append("..").Append(hi[i].ToString("0.####"));
            listed++;
        }
        sb.Append(listed < moved ? ", and the rest are counted but not named." : ".");
    }


    // ---- ROUND EIGHT: the roster, the per-frame property block, and the timeline -------------------

    /// <summary>
    /// Name every renderer under the held prop, once, when the window arms.
    ///
    /// <para>THE READING THAT MADE THIS NECESSARY. ModBuild 453, held bear trap, 169 frames:
    /// <c>RENDERERS: 3 sampled of 3 found, at most 1 drawing and 1 reported isVisible</c>, and
    /// <c>2 material(s) … mat0 'Amp_Char_Shader' … mat1 'GloomhavenVR/Overlay'</c>. Two of three
    /// renderers never drew and one of the two materials is OURS. Neither fact is attributable
    /// from a count: the line cannot say which renderer is dark, and it cannot say whether the
    /// surface the player is looking at is the game's or this mod's overlay clone. This project
    /// has three recorded incidents of its own machinery being the churn it was measuring, so
    /// "our shader is on the held prop" is ruled in or out here by name, not by assumption.</para>
    /// </summary>
    private static void ArmRoster(GameObject go)
    {
        _rCount = 0;
        _rFound = _vRenderers.Length;
        for (int i = 0; i < RosterCap; i++)
        {
            RName[i] = string.Empty;
            RShaders[i] = string.Empty;
            RMine[i] = false;
            RDrawFrames[i] = RVisFrames[i] = RBlockFrames[i] = RBlockOverrideMax[i] = 0;
            RDeadAtFrame[i] = -1;
            RSrc[i] = -1;
        }
        for (int i = 0; i < _vRenderers.Length && _rCount < RosterCap; i++)
        {
            Renderer r = _vRenderers[i];
            if (r == null)
                continue;
            int k = _rCount++;
            RSrc[k] = i;
            RName[k] = Describe(r.transform) + " [" + r.GetType().Name + "]";
            Material[] mats = r.sharedMaterials;
            var names = new System.Text.StringBuilder(64);
            for (int m = 0; m < mats.Length; m++)
            {
                Material mat = mats[m];
                if (m > 0)
                    names.Append(" + ");
                if (mat == null)
                {
                    names.Append("<null slot>");
                    continue;
                }
                Shader? sh = mat.shader;
                string shName = sh != null ? sh.name : "<no shader>";
                names.Append('\'').Append(mat.name).Append("' on ").Append(shName);
                // OURS or THEIRS. Every shader this mod bundles is namespaced GloomhavenVR/, so the
                // test is exact rather than a name guess.
                if (shName.StartsWith("GloomhavenVR/", System.StringComparison.Ordinal))
                    RMine[k] = true;
            }
            RShaders[k] = names.ToString();
        }

        // The dissolve channels, by NAME. A dissolve IS a ramp — it drives a cutout from 0 to 1
        // over time — which is the one shape the user's video shows and the one shape a single
        // snapshot two frames after the grab can never see. Read from the table so a shader that
        // spells it differently is still caught, plus the two this mod's own WallSegmentFade path
        // writes whether or not the shader declares them.
        _dissolveCount = 0;
        for (int slot = 0; slot < VTable.Capacity && _dissolveCount < DissolveCap; slot++)
        {
            string? n = VTable.Name[slot];
            if (string.IsNullOrEmpty(n))
                continue;
            if (n!.IndexOf("dissolve", System.StringComparison.OrdinalIgnoreCase) < 0
                && n.IndexOf("cutout", System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            bool dup = false;
            for (int d = 0; d < _dissolveCount; d++)
                dup |= DissolveIds[d] == VTable.Id[slot];
            if (dup)
                continue;
            DissolveIds[_dissolveCount] = VTable.Id[slot];
            DissolveNames[_dissolveCount] = n;
            _dissolveCount++;
        }
        AddDissolveChannel("_Toggle_Dissolve");
        AddDissolveChannel("_Dissolve");
        for (int d = 0; d < DissolveCap; d++)
        {
            DissolveLo[d] = float.MaxValue;
            DissolveHi[d] = float.MinValue;
            DissolveSeen[d] = 0;
        }

        _blkOverrideWorst = 0;
        _blkFramesAny = 0;
        BlkNamed.Clear();

        for (int m = 0; m < VerdictMatCap; m++)
        {
            KwMin[m] = int.MaxValue;
            KwMax[m] = -1;
            KwFirst[m] = string.Empty;
            KwLast[m] = string.Empty;
        }
        _kwProbeAt = 0;
        _kwProbes = _kwChanges = 0;

        _probeAt = 0;
        _probeSamples = 0;
        _shLo = float.MaxValue;
        _shHi = float.MinValue;
        _shFirst = _shLast = float.NaN;
        _reflectId = 0;
        _reflectChanges = 0;
        _reflectCountMax = 0;
        _reflectName = "<none>";
        _probeUsage = string.Empty;

        _poseAngLo = _poseDistLo = float.MaxValue;
        _poseAngHi = _poseDistHi = float.MinValue;
        _tlT0 = Time.unscaledTime;
        for (int i = 0; i < TimelineBuckets; i++)
        {
            TlN[i] = TlDraw[i] = TlBlk[i] = TlShN[i] = 0;
            TlSh[i] = TlAng[i] = TlDist[i] = 0f;
        }
    }

    /// <summary>Add a channel this mod writes even when the shader does not declare it — a
    /// <c>MaterialPropertyBlock</c> can carry an id the material never had.</summary>
    private static void AddDissolveChannel(string name)
    {
        if (_dissolveCount >= DissolveCap)
            return;
        int id = Shader.PropertyToID(name);
        for (int d = 0; d < _dissolveCount; d++)
        {
            if (DissolveIds[d] == id)
                return;
        }
        DissolveIds[_dissolveCount] = id;
        DissolveNames[_dissolveCount] = name;
        _dissolveCount++;
    }

    /// <summary>
    /// One frame of the roster, the property blocks and the timeline.
    ///
    /// <para>The property block read is the point. <c>MaterialPropertyBlock</c> overrides a value
    /// at DRAW time and appears in neither <c>material</c> nor <c>sharedMaterial</c>, so it is
    /// invisible to every material read-back in this file; the only reading this project has ever
    /// taken on it is a SNAPSHOT two frames after the grab, and a snapshot cannot see a ramp. This
    /// asks the block itself, every frame, which of the shader's own properties it is overriding
    /// and what value it is carrying.</para>
    /// </summary>
    private static void SampleRoster(Belt b)
    {
        int bucket = (int)((Time.unscaledTime - _tlT0) / TimelineBucketSeconds);
        if (bucket < 0)
            bucket = 0;
        if (bucket >= TimelineBuckets)
            bucket = TimelineBuckets - 1;

        int drawing = 0, blocksThisFrame = 0, overridesThisFrame = 0;
        Renderer? lead = null;
        for (int i = 0; i < _rCount; i++)
        {
            int src = RSrc[i];
            Renderer r = src >= 0 && src < _vRenderers.Length ? _vRenderers[src] : null!;
            if (r == null)
            {
                if (RDeadAtFrame[i] < 0)
                    RDeadAtFrame[i] = _vFrames;
                continue;
            }
            bool on = r.enabled && r.gameObject.activeInHierarchy;
            if (on)
            {
                RDrawFrames[i]++;
                drawing++;
                lead ??= r;
            }
            if (r.isVisible)
                RVisFrames[i]++;
            if (!r.HasPropertyBlock())
                continue;
            RBlockFrames[i]++;
            blocksThisFrame++;
            r.GetPropertyBlock(RBlock);
            int overrides = 0;
            // The POPULATED slots only. VTable.Capacity is matCap x propCap = 1536 and the trap's
            // real table is 67; walking the capacity would be 1536 iterations per renderer per
            // frame for nothing. This project has a recorded 12.6 ms frame owned by one line.
            for (int m = 0; m < VTable.MatCount && m < VerdictMatCap; m++)
            {
                int perMat = VTable.PerMat[m];
                for (int k = 0; k < perMat; k++)
                {
                    int slot = (m * VerdictPropCap) + k;
                    string? n = VTable.Name[slot];
                    if (string.IsNullOrEmpty(n))
                        continue;
                    int id = VTable.Id[slot];
                    bool has;
                    switch (VTable.Kind[slot])
                    {
                        case PropTable.KindColor: has = RBlock.HasColor(id); break;
                        case PropTable.KindVector: has = RBlock.HasVector(id); break;
                        case PropTable.KindTexture: has = RBlock.HasTexture(id); break;
                        default: has = RBlock.HasFloat(id) || RBlock.HasInt(id); break;
                    }
                    if (!has)
                        continue;
                    overrides++;
                    if (BlkNamed.Count < VerdictListCap && !BlkNamed.Contains(n!))
                        BlkNamed.Add(n!);
                }
            }
            // The dissolve channels are read BY NAME and by VALUE, whether or not the count above
            // saw them: the id may not be declared by this material's shader at all.
            for (int d = 0; d < _dissolveCount; d++)
            {
                int id = DissolveIds[d];
                if (!RBlock.HasFloat(id))
                    continue;
                float v = RBlock.GetFloat(id);
                DissolveSeen[d]++;
                if (v < DissolveLo[d])
                    DissolveLo[d] = v;
                if (v > DissolveHi[d])
                    DissolveHi[d] = v;
            }
            if (overrides > RBlockOverrideMax[i])
                RBlockOverrideMax[i] = overrides;
            overridesThisFrame += overrides;
        }
        if (blocksThisFrame > 0)
            _blkFramesAny++;
        if (overridesThisFrame > _blkOverrideWorst)
            _blkOverrideWorst = overridesThisFrame;

        // THE POSE CONFOUND, and it is this round's own falsifier. The photometry of the user's
        // video measures the bright-pixel FRACTION inside a FIXED window while the prop is being
        // turned over in the hand, so a term that fades as the object rotates away from the eye
        // and a term that fades with the CLOCK produce the same curve. Printing the angle and the
        // distance on the same timebase is what tells them apart, and nothing else in this file
        // can. This project has a recorded incident for exactly this shape: a diagnostic modelling
        // a SUBSET of what the eye sees agrees with the eye and names the wrong cause.
        float ang = float.NaN, dist = float.NaN;
        Camera? head = Rig.VRRigDriver.HeadCamera;
        if (head != null && lead != null)
        {
            Transform ht = head.transform;
            Vector3 toProp = lead.bounds.center - ht.position;
            dist = toProp.magnitude;
            if (dist > 1e-4f)
                ang = Vector3.Angle(lead.transform.forward, -toProp.normalized);
        }
        if (!float.IsNaN(ang))
        {
            if (ang < _poseAngLo)
                _poseAngLo = ang;
            if (ang > _poseAngHi)
                _poseAngHi = ang;
            TlAng[bucket] += ang;
        }
        if (!float.IsNaN(dist))
        {
            if (dist < _poseDistLo)
                _poseDistLo = dist;
            if (dist > _poseDistHi)
                _poseDistHi = dist;
            TlDist[bucket] += dist;
        }
        TlN[bucket]++;
        TlDraw[bucket] += drawing;
        TlBlk[bucket] += overridesThisFrame;

        if (Time.frameCount >= _probeAt && lead != null)
        {
            _probeAt = Time.frameCount + ProbeEvery;
            SampleLightingBindings(lead, bucket);
        }
        if (Time.frameCount >= _kwProbeAt)
        {
            _kwProbeAt = Time.frameCount + KeywordEvery;
            SampleKeywords();
        }
    }

    /// <summary>
    /// The two per-renderer lighting bindings Unity RE-PICKS when a renderer moves, and which
    /// therefore change for a prop carried across the room and for no other reason: the
    /// interpolated light probe and the reflection probe. Neither is a component, a material
    /// property or a global, so every instrument in this file is blind to both by construction.
    /// </summary>
    private static void SampleLightingBindings(Renderer lead, int bucket)
    {
        _probeSamples++;
        if (_probeUsage.Length == 0)
            _probeUsage = lead.lightProbeUsage + " / " + lead.reflectionProbeUsage;

        LightProbes.GetInterpolatedProbe(lead.bounds.center, lead,
            out UnityEngine.Rendering.SphericalHarmonicsL2 sh);
        // The L0 (constant) band IS the ambient the surface receives from the probe. Rec. 709
        // luminance of it, so one number can be put on a timeline beside a brightness curve.
        float lum = (0.2126f * sh[0, 0]) + (0.7152f * sh[1, 0]) + (0.0722f * sh[2, 0]);
        if (float.IsNaN(_shFirst))
            _shFirst = lum;
        _shLast = lum;
        if (lum < _shLo)
            _shLo = lum;
        if (lum > _shHi)
            _shHi = lum;
        TlSh[bucket] += lum;
        TlShN[bucket]++;

        ReflectScratch.Clear();
        lead.GetClosestReflectionProbes(ReflectScratch);
        if (ReflectScratch.Count > _reflectCountMax)
            _reflectCountMax = ReflectScratch.Count;
        int id = 0;
        if (ReflectScratch.Count > 0)
        {
            ReflectionProbe? p = ReflectScratch[0].probe;
            if (p != null)
            {
                id = p.GetInstanceID();
                _reflectName = Describe(p.transform) + ", weight "
                               + ReflectScratch[0].weight.ToString("0.###");
            }
        }
        if (_reflectId != 0 && id != _reflectId)
            _reflectChanges++;
        _reflectId = id;
        ReflectScratch.Clear();
    }

    /// <summary>Shader keywords, on a slow cadence because <c>Material.shaderKeywords</c>
    /// ALLOCATES. A keyword switches a whole shader branch on or off with NO property moving at
    /// all, which is why "not one of 67 slots moved" is not evidence that the shader did the same
    /// thing on every frame.</summary>
    private static void SampleKeywords()
    {
        _kwProbes++;
        for (int m = 0; m < VTable.MatCount && m < VerdictMatCap; m++)
        {
            Material mat = VTable.Mats[m];
            if (mat == null)
                continue;
            string[] kws = mat.shaderKeywords;
            int n = kws.Length;
            if (n < KwMin[m])
                KwMin[m] = n;
            if (n > KwMax[m])
                KwMax[m] = n;
            string joined = n == 0
                ? "<none>"
                : string.Join(",", kws, 0, n < KeywordNameCap ? n : KeywordNameCap);
            if (KwFirst[m].Length == 0)
                KwFirst[m] = joined;
            else if (!string.Equals(KwLast[m], joined, System.StringComparison.Ordinal))
                _kwChanges++;
            KwLast[m] = joined;
        }
    }

    /// <summary>
    /// THE LINE ROUND EIGHT EXISTS FOR. Everything above it in this file reports a COUNT over a
    /// whole hold; this one names the objects and prints VALUES on the same quarter-second
    /// timebase as the user's video.
    /// </summary>
    private static void EmitRoster(Belt b)
    {
        var sb = new System.Text.StringBuilder(4096);
        sb.Append("[Props] HELD-PROP ROSTER for ").Append(_vLabel).Append(" — ").Append(_vFrames)
          .Append(" frame(s). WHY THIS LINE EXISTS: every reading on the PAINT AFTER HUSH line "
                  + "beside it is a COUNT over the whole hold, and the ModBuild 453 log's two "
                  + "unexplained numbers are both identity questions a count cannot answer — "
                  + "'at most 1 drawing' of 3 renderers, and one of the prop's two materials on "
                  + "OUR OWN shader 'GloomhavenVR/Overlay'. The recorded lesson is 'name the "
                  + "blocker, not the number'. The second half is TIME: the user's word for "
                  + "ModBuild 453 is 'zeitlupe' and his video shows a ~3.4 s decay from ivory "
                  + "white to the correct bronze, so everything here is also bucketed at ")
          .Append(TimelineBucketSeconds.ToString("0.##"))
          .Append(" s and printed as a series. ");

        sb.Append("ROSTER — EVERY RENDERER UNDER THE PROP, BY HIERARCHY PATH, WITH ITS MATERIALS "
                  + "AND SHADERS: ").Append(_rCount).Append(" named of ").Append(_rFound)
          .Append(" sampled. ");
        for (int i = 0; i < _rCount; i++)
        {
            sb.Append('[').Append(i).Append("] '").Append(RName[i]).Append("' ")
              .Append(RMine[i]
                  ? "MOD-OWNED (a GloomhavenVR/ shader — this renderer is OURS)"
                  : "game-owned")
              .Append(", materials ").Append(RShaders[i].Length == 0 ? "<none>" : RShaders[i])
              .Append(", DRAWING on ").Append(RDrawFrames[i]).Append('/').Append(_vFrames)
              .Append(" frame(s), isVisible on ").Append(RVisFrames[i])
              .Append(", carried a MaterialPropertyBlock on ").Append(RBlockFrames[i])
              .Append(" frame(s) (worst ").Append(RBlockOverrideMax[i])
              .Append(" overridden slot(s))");
            if (RDeadAtFrame[i] >= 0)
                sb.Append(", DESTROYED at frame ").Append(RDeadAtFrame[i]).Append(" of the window");
            sb.Append(". ");
        }
        sb.Append("READ IT LIKE THIS: a MOD-OWNED renderer with a non-zero DRAWING count is this "
                  + "mod painting the prop the player is holding, and three separate incidents in "
                  + "this project's record are of exactly that shape ('we were the churn'). A "
                  + "game-owned renderer at DRAWING 0 for the whole window is a mesh somebody "
                  + "switched off; whether that is normal for this prop is answered by the HOME "
                  + "column of the ANIMATION A/B line, not here. ");

        sb.Append("PROPERTY BLOCKS, READ EVERY FRAME AND NOT ONCE — this closes the blind spot "
                  + "the ModBuild 453 verdict named first about itself: ").Append(_blkFramesAny)
          .Append(" of ").Append(_vFrames)
          .Append(" frame(s) had at least one renderer carrying a block, worst frame overrode ")
          .Append(_blkOverrideWorst).Append(" slot(s)");
        if (BlkNamed.Count > 0)
            sb.Append(", naming: ").Append(string.Join(", ", BlkNamed));
        sb.Append(". A block overrides a material value AT DRAW TIME and appears in neither "
                  + "material nor sharedMaterial, so a zero on the material read-back is "
                  + "consistent with a block painting the whole hold. THE DISSOLVE CHANNELS, BY "
                  + "NAME AND BY VALUE, because a dissolve IS a ramp and a ramp is what the video "
                  + "shows: ");
        if (_dissolveCount == 0)
            sb.Append("<no dissolve/cutout channel is declared by any material on this prop and "
                      + "neither of the two this mod writes was found in any block>");
        for (int d = 0; d < _dissolveCount; d++)
        {
            if (d > 0)
                sb.Append("; ");
            sb.Append(DissolveNames[d]).Append(" seen in a block on ").Append(DissolveSeen[d])
              .Append(" frame(s)");
            if (DissolveSeen[d] > 0)
                sb.Append(", value ").Append(DissolveLo[d].ToString("0.###")).Append("..")
                  .Append(DissolveHi[d].ToString("0.###"));
        }
        sb.Append(". ");

        sb.Append("SHADER KEYWORDS, sampled every ").Append(KeywordEvery)
          .Append(" frame(s) (").Append(_kwProbes)
          .Append(" sample(s)) — a keyword switches a whole shader BRANCH with no property moving "
                  + "at all, so 'not one slot moved' is not evidence the shader did the same "
                  + "thing every frame: ").Append(_kwChanges)
          .Append(" change(s) observed across all materials. ");
        for (int m = 0; m < VTable.MatCount && m < VerdictMatCap; m++)
        {
            sb.Append("mat").Append(m).Append(' ').Append(VTable.ShaderOf(m)).Append(": ");
            if (KwMax[m] < 0)
                sb.Append("<never sampled>; ");
            else
                sb.Append(KwMin[m]).Append("..").Append(KwMax[m]).Append(" keyword(s), first [")
                  .Append(KwFirst[m]).Append("], last [").Append(KwLast[m]).Append("]; ");
        }

        sb.Append("LIGHTING BINDINGS UNITY RE-PICKS WHEN A RENDERER MOVES, and this is the third "
                  + "still-open item the 453 verdict named about itself — neither is a component, "
                  + "a material property or a global, so nothing else in this file can see them: ")
          .Append(_probeSamples)
          .Append(" sample(s). INTERPOLATED LIGHT PROBE at the drawing renderer's bounds centre, "
                  + "Rec.709 luminance of the L0 band: ");
        if (_probeSamples == 0 || float.IsNaN(_shFirst))
            sb.Append("<never sampled — no renderer was drawing>");
        else
            sb.Append("first ").Append(_shFirst.ToString("0.####")).Append(", last ")
              .Append(_shLast.ToString("0.####")).Append(", range ")
              .Append(_shLo.ToString("0.####")).Append("..").Append(_shHi.ToString("0.####"));
        sb.Append(". REFLECTION PROBE: at most ").Append(_reflectCountMax)
          .Append(" probe(s) influenced this renderer, the closest was '").Append(_reflectName)
          .Append("', and the bound probe CHANGED IDENTITY on ").Append(_reflectChanges)
          .Append(" sample(s). Usage (lightProbe / reflectionProbe): ")
          .Append(_probeUsage.Length == 0 ? "<unsampled>" : _probeUsage)
          .Append(". A prop lifted off its hex and carried to the eye leaves the probe volume it "
                  + "was authored inside; a metal surface that then reflects the ambient/skybox "
                  + "instead of the room is bright, view-dependent and correct again the moment "
                  + "the object turns away — which is a shape no state probe can distinguish from "
                  + "a clock. ");

        AppendStaleAnchors(sb, b);

        sb.Append("THE POSE CONFOUND — THIS ROUND'S OWN FALSIFIER, and it is aimed at the "
                  + "photometry that briefed it. The bright-pixel fraction in the user's video is "
                  + "measured inside a FIXED window while the prop is being turned over in the "
                  + "hand, so a term that fades because the object ROTATES AWAY and a term that "
                  + "fades with the CLOCK draw the same curve. Head-to-prop distance ranged ");
        if (_poseDistLo > _poseDistHi)
            sb.Append("<never sampled>");
        else
            sb.Append(_poseDistLo.ToString("0.###")).Append("..")
              .Append(_poseDistHi.ToString("0.###")).Append(" wu");
        sb.Append("; the angle between the drawing renderer's own forward axis and the view ray "
                  + "ranged ");
        if (_poseAngLo > _poseAngHi)
            sb.Append("<never sampled>");
        else
            sb.Append(_poseAngLo.ToString("0.#")).Append("..").Append(_poseAngHi.ToString("0.#"))
              .Append(" deg");
        sb.Append(". ");

        AppendTimeline(sb);
        AppendMipResidency(sb);

        sb.Append("HOW TO READ THE WHOLE LINE. IT NAMES THE PAINTER if a MOD-OWNED renderer drew "
                  + "for a stretch of the window, or if a property block overrode a slot on a "
                  + "stretch of it, or if a dissolve channel carried a value that WALKED. IT "
                  + "NAMES A BINDING if the light-probe luminance or the reflection probe changed "
                  + "across the window while nothing else did. IT KILLS THIS ROUND'S OWN LEADS if "
                  + "every renderer that drew is game-owned with no block, no keyword changed, the "
                  + "probe luminance is flat and the reflection probe never re-bound — and in that "
                  + "case read the POSE line: if the view angle swept while every value above held "
                  + "still, the decay in the video is the object TURNING and not anything this "
                  + "process writes, which means the next round must grab the prop and hold it "
                  + "DEAD STILL before it measures anything at all. ");

        // HW-VERIFY: this line is round eight's whole deliverable. Seven rounds each ended on a
        // count that could not name an object; this one names every renderer and prints values on
        // the same timebase as the user's video. It must stay at a tier the DEFAULT log level
        // prints (Note/Alert/Error) — scripts/check-hw-verify.py enforces the position of this
        // marker directly above the call.
        VRLog.Note("FigureGrab", sb.ToString());
    }

    /// <summary>
    /// STALE WORLD ANCHORS. <c>ObjectPosToMaterial</c> bakes the object's WORLD POSITION into a
    /// shader vector in <c>OnEnable</c> and never again (decompiled
    /// <c>GH.Runtime/ObjectPosToMaterial.cs:29,34</c>), and the ModBuild 453 census counts one on
    /// this trap. A bake taken on the hex and carried into a palm is a CONSTANT wrong value: it
    /// never moves, so every "did anything move" reading in this file is consistent with it, which
    /// is the same shape strand 6 was built for and the same reason it could not be seen.
    ///
    /// <para>(§12.5 of the experiments file dismissed this component because it goes through
    /// <c>GetComponent&lt;Projector&gt;()</c> and the census counts 0 Projectors. That reads only
    /// the first of its two branches — <c>:34</c> is a SkinnedMeshRenderer branch and this prop
    /// carries two skins. An assertion in a comment is a hypothesis; this measures it.)</para>
    /// </summary>
    private static void AppendStaleAnchors(System.Text.StringBuilder sb, Belt b)
    {
        Vector3 here = b.Visual != null ? b.Visual.transform.position : Vector3.zero;
        sb.Append("STALE WORLD ANCHORS — every VECTOR slot on this prop's materials, against where "
                  + "the prop ACTUALLY IS (").Append(here.x.ToString("0.00")).Append(',')
          .Append(here.y.ToString("0.00")).Append(',').Append(here.z.ToString("0.00"))
          .Append("): ");
        int listed = 0, vectors = 0;
        for (int slot = 0; slot < VTable.Capacity && listed < VerdictListCap; slot++)
        {
            if (!VSeen[slot] || VTable.Kind[slot] != PropTable.KindVector)
                continue;
            vectors++;
            Vector4 v = VPrev[slot];
            var p = new Vector3(v.x, v.y, v.z);
            if (p.sqrMagnitude < 1e-6f)
                continue;
            float d = Vector3.Distance(p, here);
            sb.Append(VTable.Name[slot] ?? "<unnamed>").Append(" (mat")
              .Append(slot / VerdictPropCap).Append(") = ").Append(v.x.ToString("0.00"))
              .Append(',').Append(v.y.ToString("0.00")).Append(',').Append(v.z.ToString("0.00"))
              .Append(",w=").Append(v.w.ToString("0.00")).Append(" — ").Append(d.ToString("0.00"))
              .Append(" wu from the prop; ");
            listed++;
        }
        if (vectors == 0)
            sb.Append("<no vector slot on any material>");
        else if (listed == 0)
            sb.Append("all ").Append(vectors).Append(" vector slot(s) are zero, so nothing is baked");
        sb.Append(". A LARGE distance on a slot named for a position is a shader still being "
                  + "evaluated where the prop USED TO BE. ");
    }

    /// <summary>The series, on the same quarter-second timebase as the photometry of the user's
    /// video, so a curve can be compared with a curve instead of with an aggregate.</summary>
    private static void AppendTimeline(System.Text.StringBuilder sb)
    {
        sb.Append("TIMELINE, ").Append(TimelineBucketSeconds.ToString("0.##"))
          .Append(" s buckets from the grab (t=probe-luminance | ang=view angle deg | d=head "
                  + "distance wu | draw=renderers drawing | blk=block-overridden slots), and it is "
                  + "the half no earlier round had — the user's report is 'zeitlupe' and his video "
                  + "is a RAMP, so a single aggregate per hold cannot agree or disagree with it: ");
        int shown = 0;
        for (int i = 0; i < TimelineBuckets; i++)
        {
            if (TlN[i] == 0)
                continue;
            float t = i * TimelineBucketSeconds;
            sb.Append(t.ToString("0.00")).Append("s ");
            sb.Append(TlShN[i] > 0 ? (TlSh[i] / TlShN[i]).ToString("0.###") : "-").Append(" | ");
            sb.Append((TlAng[i] / TlN[i]).ToString("0.#")).Append(" | ");
            sb.Append((TlDist[i] / TlN[i]).ToString("0.##")).Append(" | ");
            sb.Append(((float)TlDraw[i] / TlN[i]).ToString("0.##")).Append(" | ");
            sb.Append(((float)TlBlk[i] / TlN[i]).ToString("0.##")).Append("   ");
            shown++;
        }
        if (shown == 0)
            sb.Append("<no bucket was sampled>");
        sb.Append(". ");
    }

    /// <summary>
    /// The mip residency of THIS PROP'S OWN textures — two integers, not an instrument.
    ///
    /// <para>Texture streaming was proposed as this round's lead and is already falsified
    /// SCENE-WIDE by this repository's own <c>] [Perf] TEX</c> line, which reads
    /// <c>streamingMipmaps=False</c> because this mod forces it off. It is recorded here only
    /// because the SAME line contradicts itself: its prose says "every mipped texture is resident
    /// in full" while its population clause counts "68 of them are streamed, 68 currently BELOW
    /// their desired mip level". One of those two is wrong, and neither is a reading of THIS prop.
    /// These figures settle it locally and cost two property reads at the close of the window.</para>
    /// </summary>
    private static void AppendMipResidency(System.Text.StringBuilder sb)
    {
        int textures = 0, streamed = 0, behind = 0, worstGap = 0;
        for (int m = 0; m < VTable.MatCount && m < VerdictMatCap; m++)
        {
            Material mat = VTable.Mats[m];
            if (mat == null)
                continue;
            int n = VTable.PerMat[m];
            for (int k = 0; k < n; k++)
            {
                int slot = (m * VerdictPropCap) + k;
                if (VTable.Kind[slot] != PropTable.KindTexture || !mat.HasProperty(VTable.Id[slot]))
                    continue;
                if (mat.GetTexture(VTable.Id[slot]) is not Texture2D tex || tex == null)
                    continue;
                textures++;
                if (!tex.streamingMipmaps)
                    continue;
                streamed++;
                int gap = tex.loadedMipmapLevel - tex.desiredMipmapLevel;
                if (gap <= 0)
                    continue;
                behind++;
                if (gap > worstGap)
                    worstGap = gap;
            }
        }
        sb.Append("MIP RESIDENCY ON THIS PROP'S OWN TEXTURES (two integers, not an instrument — "
                  + "the streaming lead is already falsified SCENE-WIDE by this repo's own "
                  + "'] [Perf] TEX' line, which reads streamingMipmaps=False because this mod "
                  + "forces it off; it is measured here only because that same line's prose and "
                  + "its own population clause CONTRADICT each other, and neither is a reading of "
                  + "this prop): ").Append(textures).Append(" texture(s) bound, ").Append(streamed)
          .Append(" of them streamed, ").Append(behind)
          .Append(" currently BELOW their desired mip level, worst gap ").Append(worstGap)
          .Append(" level(s). A streamed count of 0 closes the lead on this prop for good. ");
    }

    private static int MaxOf(int[] values, int count)
    {
        int best = 0;
        for (int i = 0; i < count && i < values.Length; i++)
        {
            if (values[i] > best)
                best = values[i];
        }
        return best;
    }

    // ---- the restore, and its own falsifier -------------------------------------------------------

    /// <summary>
    /// Say what the restore handed back and — the part that matters — whether it handed anything
    /// back OVER somebody else's write. This project has a recorded incident in which a hide saved
    /// a foreign mid-animation value and restored garbage over another system's restore, so
    /// "the restore is exact" is not left as a claim in a comment: <see cref="Restore"/> compares
    /// what is there against what this class LEFT there before writing the remembered value, and
    /// the mismatch count is printed here.
    /// </summary>
    private static void AnnounceRestore(Belt b)
    {
        if (_restoreLogsLeft <= 0 || Spent(RestoreKindsDone, b.Label))
            return;
        _restoreLogsLeft--;
        if (RestoreKindsDone.Count < KindCap)
            RestoreKindsDone.Add(b.Label);

        var sb = new System.Text.StringBuilder(1024);
        sb.Append("[Props] HELD-PROP HUSH RESTORE for ").Append(b.Label).Append(" — handed back ")
          .Append(b.Animators.Count).Append(" animator(s), ").Append(b.Outlines.Count)
          .Append(" outline(s), ").Append(b.Emitters.Count)
          .Append(" emitter(s) (Light/Projector/LensFlare AND, from 2026-09-06, the ")
          .Append(b.OcclusionFound)
          .Append(" ObjectOcclusionVolume(s) of strand 5 — they share this ledger deliberately, "
                  + "because 'switch a Behaviour off, remember what it was, write it back' is one "
                  + "restore and a second copy of it would be a second place to keep correct; "
                  + "handing the flag back runs the game's own OnEnable, which re-registers the "
                  + "renderer with TilesOcclusionGenerator, so the occlusion map contains the prop "
                  + "again the moment it is on its hex), ").Append(b.Skins.Count)
          .Append(" skinned renderer(s) and restarted ").Append(b.Particles.Count)
          .Append(" particle system(s) this class had stopped, each to the value IT had, object "
                  + "for object, after ").Append(b.Rescans).Append(" mid-hold rescan(s). ")
          .Append("THE FALSIFIER FOR THIS RESTORE: ").Append(b.RestoreForeignEmitters)
          .Append(" emitter(s) were found ENABLED at the landing although this class had switched "
                  + "them off — i.e. somebody else wrote them during the hold and the remembered "
                  + "value is being written over that; ").Append(b.RestoreForeignParticles)
          .Append(" particle system(s) were no longer children of this prop and were therefore NOT "
                  + "restarted (these objects are pooled through ObjectPool.Recycle, and "
                  + "restarting one that has been recycled into another effect would be this lane "
                  + "spawning a foreign effect on release); ").Append(b.RestoreDead)
          .Append(" object(s) had been destroyed under the ledger and were skipped rather than "
                  + "written; and ").Append(b.RestoreForeignAnimators)
          .Append(" animator(s) were found ENABLED at the landing although this class had switched "
                  + "them off. All four are expected to read 0; any of them above 0 is the reading "
                  + "that says the prop did NOT come back exactly as the game left it. "
                  + "HOW STRAND 6'S REWIND IS RESTORED, AND IT IS NOT BY A REMEMBERED COPY: ")
          .Append(b.AnimatorsRewound)
          .Append(" animator(s) were written back to their bound defaults before being stopped, "
                  + "and nothing here writes those values back — handing the enabled flag back "
                  + "gives the channels to the animator, which drives every one of them itself on "
                  + "the first frame it evaluates. That is why the highlight returns exactly as "
                  + "before: its own writer takes it back, so there is no saved value that could "
                  + "be restored over somebody else's write, which is the recorded incident this "
                  + "project has already paid for once. The animator count above is the falsifier "
                  + "for it. ")
          .Append(_restoreLogsLeft).Append(" more restore line(s) this session, at most one per prop kind.");

        // HW-VERIFY: the standing requirement on this feature is that a prop put back down looks
        // exactly as it did before it was picked up. This is the only line that can say whether
        // that held, and it must stay at a tier the DEFAULT log level prints (Note/Alert/Error).
        // scripts/check-hw-verify.py enforces it.
        VRLog.Note("FigureGrab", sb.ToString());
    }

    /// <summary>Has this prop KIND already spent its budget in <paramref name="roster"/>?</summary>
    private static bool Spent(List<string> roster, string label)
    {
        for (int i = 0; i < roster.Count; i++)
        {
            if (string.Equals(roster[i], label, System.StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>Count one component type name into the census histogram. Linear over a list that
    /// is at most a few dozen entries long and lives for the length of one log line.</summary>
    private static void Tally(string typeName)
    {
        for (int i = 0; i < TypeNames.Count; i++)
        {
            if (string.Equals(TypeNames[i], typeName, System.StringComparison.Ordinal))
            {
                TypeCounts[i]++;
                return;
            }
        }
        TypeNames.Add(typeName);
        TypeCounts.Add(1);
    }
}
