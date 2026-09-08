using System.Collections;
using System.Collections.Generic;
using System.Text;
using EPOOutline;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace GloomhavenVR.Core;

// =================================================================================================
//  HAUNT FIGURES — THE CLONE FACTORY. Turn an enemy prefab into a decorative apparition that can
//  never touch the game, and take it apart again without leaking.
// =================================================================================================

internal static partial class HauntFigures
{
    /// <summary>
    /// Builds, drives and destroys the ONE apparition that can exist at a time.
    ///
    /// <para><b>THE RECIPE, AND WHY EVERY STEP IS IN THE ORDER IT IS IN.</b> An enemy prefab is not
    /// a model; it is a live game object with a network identity, a pathing footprint, an outline
    /// registration and an object-pool relationship, and it is in TWO PARTS. The root carries
    /// <c>CharacterManager</c>, whose <c>CharacterChildPrefabReference</c> (CharacterManager.cs:42)
    /// is an Addressables handle to a SECOND prefab holding the mesh, the skeleton and the animator
    /// — instantiate the root alone and you get an invisible, mesh-less object. So the build is:</para>
    /// <list type="number">
    /// <item><b>Instantiate under an INACTIVE nursery.</b> A clone born inactive runs no
    /// <c>Awake</c> and no <c>Start</c>, which is the only moment at which the dangerous components
    /// can be removed before they have done anything. The worst of them is
    /// <c>UnityGameEditorObject</c>, whose <c>Start</c> puts the object on the "Hovering" layer and,
    /// if it has <c>Coverage</c> children, writes
    /// <c>ScenarioManager.PathFinder.Nodes[x,y].Blocked = true</c>
    /// (UnityGameEditorObject.cs:63-81). That is real gameplay mutation on ONE client — the enemy
    /// AI on this machine would path around a square nothing is standing on, and no peer would
    /// agree. It is the classic silent multiplayer bug and it is prevented by never letting the
    /// method run.</item>
    /// <item><b>Clear the weapon lists.</b> <c>InitialiseCharacterStepTwo</c> equips a weapon per
    /// list through <c>DoEquip</c>, which calls <c>ObjectPool.Spawn</c> (CharacterManager.cs:307) —
    /// the SHARED pool, which the must-not-touch list forbids — and picks the index through a
    /// static dictionary keyed on <c>gameObject.name + weaponList.name</c> (:211, :391-411) that
    /// nothing ever cleans up. Emptying the list makes both problems structurally impossible rather
    /// than carefully avoided: the loop body never runs. The cost is an unarmed monster, which at
    /// 5.8 m through a doorway or 13 m behind trunks is not a silhouette anyone can miss.</item>
    /// <item><b>Initialise with <c>isPreview: true</c>, pumped as a coroutine.</b> <c>isPreview</c>
    /// is exactly the guard that skips the two things a decorative clone must not do: dereference
    /// <c>CharacterActor</c> (null here — CharacterManager.cs:221) and call
    /// <c>ControllableRegistry.CreateControllable</c> (:230/234/239), which is a BOLT NETWORK
    /// REGISTRATION. The ASYNC form is used and driven by hand from <see cref="Drive"/>: the
    /// synchronous <c>InitialiseCharacter</c> ends in <c>WaitForCompletion()</c>
    /// (CharacterManager.cs:173), a blocking main-thread Addressables load, and a blocking load
    /// inside a 90 Hz stereo frame is a hitch the player feels in their inner ear. The coroutine
    /// only ever yields <c>null</c> (:188-191), so pumping it once per frame is exactly equivalent
    /// and costs nothing.</item>
    /// <item><b>Strip the child.</b> It arrives inactive too (its parent is), so the same window is
    /// still open for it.</item>
    /// <item><b>Reparent, scale, layer, activate</b> — and write full invisibility in the same frame,
    /// before anything is rendered, so the figure is never seen to pop in.</item>
    /// </list>
    ///
    /// <para><b>WHAT <c>isPreview</c> DOES TO THE LOOK — AND THE PARAGRAPH THAT USED TO STAND HERE,
    /// WHICH HARDWARE FALSIFIED.</b> <c>isPreview</c> is not only a guard: it also forces the game's
    /// PREVIEW appearance — <c>m_InvisibilityValue = 1, m_GlowValue = glowMaterialPreview (10),
    /// m_OpacityValue = oppacityMaterialPreview (0.588)</c> (CharacterManager.cs:243-245) — and
    /// <c>RefreshVisibility</c> (:413-436) is what writes those into <c>_InvisibilityControl</c>,
    /// <c>_Glow</c>, <c>_Opacity</c> and <c>_Toggle_Dissolve</c> on every material.</para>
    ///
    /// <para><b>THE CLAIM THAT WAS HERE AND IS NOW WITHDRAWN.</b> This file used to argue that the
    /// preview look was "not kept and not fought", because <c>RefreshVisibility</c> is a no-op at
    /// that moment — <c>m_Renderers</c> having been gathered from an INACTIVE hierarchy (:242 uses
    /// <c>GetComponentsInChildren&lt;Renderer&gt;()</c> without <c>includeInactive</c>) — "so the
    /// materials are never touched by the game at all and arrive at their authored, SOLID values".
    /// <b>USER REPORT FROM HARDWARE, verbatim:</b> "Die neuen Assets sind von der größe und
    /// Animation passend, allerdings sieht man nur so leuchtenden Siluetten statt die Figure selber.
    /// Ich will das die Figur selber mit allen Texturen etc angezeigt wird ABER sich entsprechend in
    /// die Lichtverhältnisse einfügt." A glowing, translucent shape is exactly
    /// <c>_Glow = 10</c> with <c>_Opacity = 0.588</c>, so the conclusion was wrong however sound the
    /// premise was.</para>
    ///
    /// <para><b>WHERE THE OLD ARGUMENT ACTUALLY BREAKS, and it is the second link and not the
    /// first.</b> Whether <c>m_Renderers</c> came back empty or not, the inference
    /// "the game never touched them" ⇒ "therefore they are SOLID" does not follow, because
    /// <b>the game normalises these four properties on EVERY character at init</b>: the same
    /// <c>InitialiseCharacterStepTwo</c> that sets the preview triple sets
    /// <c>0 / 0 / 1</c> for a real monster (:243-245) and then calls <c>RefreshVisibility</c>
    /// unconditionally. Nothing in the game ever reads the authored values, so nothing in the game
    /// ever had to keep them right — a character material left at a preview-scene glow would look
    /// perfect in every scenario and only ever show up here. The old paragraph asserted the solidity
    /// of values that no shipped code path depends on. That is the flaw, and it is the reason the
    /// two candidate root causes (the preview write landed / the authored values were never solid)
    /// are indistinguishable from the outside — <b>and why they are both NEUTRALISED rather than
    /// tested</b>: <see cref="Shade"/> now writes <c>_Glow = 0</c> and <c>_Opacity = 1</c> itself,
    /// on every material, every time it writes the dissolve, and its writes come strictly after
    /// <c>InitialiseCharacterStepTwo</c> (the coroutine is fully drained in <see cref="Step"/>
    /// before <see cref="Collect"/> runs), so this side wins whatever happened earlier.
    /// <see cref="Diag"/> then LOGS which of the two it was — see its own block.</para>
    ///
    /// <para><b>THE TWO OTHER SUSPECTS, AND THE EVIDENCE THAT CLEARED THEM.</b> (1) <c>Outlinable</c>
    /// — the EPOOutline component that adds itself to a STATIC list an outline pass walks
    /// (Outlinable.cs:264-267, 293-300); an outline drawn around a nearly-invisible body is
    /// literally "a glowing silhouette". It shows up in the shipped per-creature census
    /// (<c>components after stripping = … Outlinablex1 …</c>) which looks damning, but that census
    /// ALSO lists <c>CapsuleColliderx6</c> and <c>Clothx1</c>, and colliders and cloth are destroyed
    /// unconditionally by <see cref="Strip"/> in the very same call — so the census is simply a
    /// snapshot taken in the same frame as the child strip, where <c>Object.Destroy</c> has not
    /// landed yet, and it is not evidence of a strip failure at all. Independently:
    /// <c>Outlinable.OnEnable</c> calls <c>UpdateVisibility</c>, whose FIRST test is
    /// <c>if (!enabled) { outlinables.Remove(this); return; }</c> (:243-245), and the generic
    /// MonoBehaviour sweep in <see cref="Strip"/> sets <c>enabled = false</c> on it (it is still
    /// enumerable that frame, which is the same fact) before anything is activated. It can never
    /// register. (2) The VFX-shader renderers <c>FigureOverlay.HasVfxShader</c> warns about: those
    /// are destroyed in <see cref="Collect"/>, and had they been the whole set the build would have
    /// failed loudly on "produced no renderers after stripping" instead of showing a figure.</para>
    ///
    /// <para><b>WHAT THE FIGURE'S MATERIALS ARE NOW — AND THE DISSOLVE IS GONE.</b> Untouched game
    /// materials, every texture and every submesh the artists shipped, with the game's own
    /// non-preview visibility triple pinned by <see cref="Shade"/> (<c>_Glow = 0</c>,
    /// <c>_Opacity = 1</c>, <c>_InvisibilityControl = 0</c>, <c>_Toggle_Dissolve = 0</c>) and ONE
    /// property of ours on top: a multiplicative colour tint (<see cref="TintNames"/>), which
    /// carries BOTH the event's envelope and the room's light level. See the two blocks below.</para>
    ///
    /// <para><b>THE DISSOLVE WAS THE BUG, AND THIS PARAGRAPH OVERTURNS THE ONE THAT SHIPPED IT.</b>
    /// The withdrawn text read: "The burn edge of the dissolve is the material's own
    /// <c>_Burn</c>/<c>_Burn_ColourTint</c> and is deliberately left alone: that is the game's
    /// materialisation, and it is what keeps the reveal from being a pop." <b>USER REPORT FROM
    /// HARDWARE, verbatim:</b> "Die Figuren brauchen irgendwie eine längere Zeit bis ihre texturen
    /// richtig geladen haben bzw voll sichtbar sind bis dahin sind viele schwarze flecken in ihnen
    /// zu erkennen. Manchmal sind die Texturen voll da und manchmal nicht." <b>THE EVIDENCE IS TWO
    /// SCREENSHOTS</b> (<c>.planning/debug/nachladen1.jpg</c>, <c>nachladen2.jpg</c>): in the wood
    /// the watcher's legs are EATEN AWAY and its torso is broken into a high-contrast marbled noise
    /// pattern; in the cellar the same marbling covers the whole body. That is not an asset stream
    /// arriving late — a half-streamed texture is flat grey or flat white, never a swirl with holes
    /// in it, and it would not be shaped like a noise field. It is <c>_InvisibilityControl</c>
    /// partway with <c>_Toggle_Dissolve</c> on, i.e. this file's own reveal, photographed
    /// mid-envelope. THE "MISSING TEXTURES" AND THE "BLACK PATCHES" ARE THE SAME ONE FAULT.</para>
    ///
    /// <para><b>AND THE BURN EDGE IS WHY THE FIGURES GLOWED.</b> A dissolve burn edge is EMISSIVE by
    /// construction — that is what makes a materialisation read as one — so no lighting term can
    /// darken it: not the room's SH, not a tint, not the absence of every light in the scene. In a
    /// pitch-black cellar a mid-dissolve figure is therefore a WHITE LANTERN, which is exactly what
    /// both screenshots show and exactly what the user rejected ("Die Figuren sind VOLL sichtbar in
    /// der schwarzen Nacht als wären sie voll angestrahlt"). It also made the second fault worse in a
    /// way nobody would have guessed: the envelope's ends are where the burn is widest, so the
    /// moments the design intended as "not yet there" and "already gone" were the BRIGHTEST moments
    /// of the whole event, and a looping test trigger reset the walk during precisely those moments
    /// (see <see cref="HauntFigures.TickBody"/>) — which is why a hidden restart was seen as a
    /// teleport.</para>
    ///
    /// <para><b>SO THE REVEAL IS NOW A FADE OUT OF THE DARK, not a materialisation.</b> The envelope
    /// multiplies the figure's colour instead of eating its geometry: at presence 0 the renderers are
    /// switched OFF outright (a tint of zero would still be an opaque black hole punched in the star
    /// patch behind the cellar window), and from there the creature rises out of black to the room's
    /// own light level. Nothing is emissive, nothing is transparent, no pixel is ever half-there. The
    /// user never asked for a materialisation — he asked for creatures that fit the dark — and the
    /// honest reading of both reports is that the effect that was supposed to hide the arrival was
    /// the only thing anyone could see.</para>
    ///
    /// <para><b>ANIMATION — THE ONE FACT EVERYTHING IS BUILT AROUND.</b> There is NO WALK STATE in
    /// this game's character controllers. The state table (Choreographer.cs:262-302) has Idle-Run,
    /// Attack, Damage, Hit, Death, Push, Pull, Loot, SleepIdle and so on — and "Idle-Run" is a
    /// BLEND, driven by a float parameter <c>RunBlend</c> (ActorBehaviour.cs:79), with the travel
    /// landing on the transform in <c>ActorBehaviour.DoTransform</c>/<c>ApplyMotion</c>. So
    /// "vorbeilaufen" is: hold "Idle-Run", write <c>RunBlend</c>, and move the transform yourself.
    /// That is what <see cref="Drive"/> does.</para>
    ///
    /// <para><b>THE SECOND HALF OF THAT SENTENCE USED TO SAY "AND NO ROOT MOTION", AND THAT WAS
    /// WRONG</b> — ModBuild 149, from the decompiled source rather than from inference.
    /// <c>ActorBehaviour.ApplyMotion</c> (:571-612) reads <c>m_AnimatedGameObject.transform.position</c>
    /// in <c>LateUpdate</c>, writes it to the root, and then sets
    /// <c>m_AnimatedGameObject.transform.localPosition = Vector3.zero</c>; and
    /// <c>m_AnimatedGameObject</c> IS <c>m_Animator.gameObject</c> (:115). That is a harvest-and-cancel
    /// of root motion, every frame — which is why nothing in the game ever assigns
    /// <c>applyRootMotion</c>: the prefabs ship with it ON and this method is what consumes it. This
    /// feature strips <c>ActorBehaviour</c> with every other MonoBehaviour, so it has to do BOTH
    /// halves itself: <c>applyRootMotion = false</c> at spawn, and <see cref="AnimPin"/> as the
    /// belt to that brace. See <see cref="AnimPin"/> for the user report that made it necessary and
    /// for the measurement it publishes.</para>
    ///
    /// <para><b>ONLY IDLE-FAMILY STATES ARE EVER PLAYED, and this is a hard rule rather than a
    /// preference.</b> A controller's <c>StateMachineBehaviour</c>s are instantiated by Unity per
    /// Animator and CANNOT be stripped as components. One of them,
    /// <c>SetProgressModifierCallbackSMB.OnStateEnter</c> (SetProgressModifierCallbackSMB.cs:11-45),
    /// calls <c>TimeManager.FreezeTime()</c>/<c>UnfreezeTime()</c> and sets
    /// <c>AttackModBar.s_AttackModifierBarFlowCanBegin</c> — global gameplay and UI state — from the
    /// ATTACK states. Playing "Attack" on a decorative clone would freeze the game's clock for every
    /// player at the table. Hence <see cref="IdleStates"/>, and hence nothing else.</para>
    ///
    /// <para><b>AUDIO.</b> <c>Animator.fireEvents</c> is set to FALSE, which is the whole audio
    /// decision in one line, and it is not the obvious one. The prefabs carry <c>FootstepSound</c>
    /// and <c>AudioEffectPlayer</c>, both driven by animation events, and leaving them on would make
    /// the figure sound itself for free — except that <c>FootstepSound</c> plays through
    /// <c>AudioController.Play(id, GameObject)</c> (FootstepSound.cs:24), which resolves to
    /// <b>the listener's own position</b> (AudioController.cs:629-638) with no distance falloff at
    /// all. A creature 13 m away in the trees would put its footsteps inside the player's head, at
    /// full volume, which is both wrong and startling — and startling is forbidden. So the events
    /// are muted at the source, the two components are stripped with everything else, and the sound
    /// comes from two places that ARE distance-correct: <see cref="EnvSound"/>'s existing per-card
    /// cue, which now lands on the figure and follows it (see <see cref="Arm"/>), and ONE quiet
    /// positional one-shot from the creature's OWN <c>&lt;Family&gt;_SFX</c> bank played through
    /// <c>AudioController.Play(id, Vector3, …)</c> (AudioController.cs:661) — see
    /// <c>Roster.Voice</c>.</para>
    /// </summary>
    private static class Clone
    {
        // ---- the animator states we are allowed to ask for ----------------------------------------

        /// <summary>Tried in order; the first one the controller actually has wins. This is the
        /// game's own "try a list, take the first that exists" pattern
        /// (<c>Choreographer.PlayFirstAvailableAnimationStateOnObject</c>, :13093-13110),
        /// re-implemented rather than called because that method is private, has no instance state,
        /// and reaching for it through the publicizer would be a dependency on a private member for
        /// nine lines of code. <c>MF.AnimatorPlay</c> is <c>HasState</c>-guarded, so an absent state
        /// is a silent false and never a throw.
        ///
        /// <para>EVERY ONE OF THESE IS IDLE-FAMILY. See the class doc for what happens if that ever
        /// stops being true.</para></summary>
        private static readonly string[] IdleStates = { "Idle-Run", "Idle-Run2", "SleepIdle", "CheerEnemyIdle" };

        private const string RunBlendParam = "RunBlend";

        // ---- the session-lifetime prefab cache -----------------------------------------------------

        /// <summary>The ONE enemy prefab kept resident. Swapping models releases the previous one
        /// before loading the next, so this feature's memory cost is bounded at one npc bundle no
        /// matter how many creatures the roster holds.</summary>
        private static AsyncOperationHandle<GameObject> _asset;
        private static string _assetModel = string.Empty;
        private static bool _assetValid;

        // ---- the live apparition --------------------------------------------------------------------

        private static GameObject? _nursery;
        private static GameObject? _go;
        private static CharacterManager? _cm;
        private static IEnumerator? _init;
        private static Animator? _animator;
        private static string _state = string.Empty;
        private static bool _hasRunBlend;

        /// <summary>The one-line reimplementation of <c>ActorBehaviour.ApplyMotion</c>'s last
        /// statement. See <see cref="AnimPin"/>.</summary>
        private static AnimPin? _pin;

        /// <summary>Creatures whose drift readout has already been written. PER CREATURE and no longer
        /// once per process: the answer cannot change between two apparitions OF THE SAME MODEL, but it
        /// is a different answer for a walker than for a stander, and the once-per-process latch spent
        /// the ModBuild 150 session's only line on the treeline watcher — which stands. See
        /// <see cref="AnimPin"/>.</summary>
        private static readonly HashSet<string> DriftLogged = new(8);

        /// <summary>
        /// PIN THE ANIMATOR'S OWN TRANSFORM. One <c>LateUpdate</c>, one compare, one conditional
        /// write — and it is the exact line this feature deleted when it stripped the game's own
        /// motion script.
        ///
        /// <para><b>THE USER'S REPORT IS A DESCRIPTION OF THIS MECHANISM</b> (ModBuild 148): "Ich
        /// vermute es liegt daran das die Animation wiederholt wird und sie eben immer von einem
        /// punkt weiter vorne startet. Kannst du die Laufanimationen nicht loopen ohne eine bewegung
        /// und die Bewegung selber koordinieren damit keine Teleportation stattfindet?" That is
        /// clip-carried travel with a loop in it: the mesh creeps forward through the cycle and snaps
        /// back when the cycle wraps.</para>
        ///
        /// <para><b>THE GAME HAS EXACTLY THIS PROBLEM AND EXACTLY THIS FIX.</b>
        /// <c>ActorBehaviour.ApplyMotion</c> runs in <c>LateUpdate</c> and ends:</para>
        /// <code>
        ///   m_RootGameObject.transform.position = position;          // ActorBehaviour.cs:610
        ///   m_AnimatedGameObject.transform.localPosition = Vector3.zero;   // :611
        /// </code>
        /// <para>where <c>m_AnimatedGameObject</c> is <c>m_Animator.gameObject</c> (:115). That
        /// second line is a HARVEST-AND-CANCEL: it takes whatever the animation put on the animator's
        /// own transform, hands it to the root, and zeroes the local offset every single frame. The
        /// game's monsters travel BY that harvest — which is why nothing in the decompiled sources
        /// ever writes <c>applyRootMotion</c>: the prefabs ship with it on.</para>
        ///
        /// <para><b>AND <see cref="Strip"/> DESTROYS ActorBehaviour</b>, along with every other
        /// MonoBehaviour except <c>CharacterManager</c> — deliberately and correctly, because it is
        /// gameplay code. <c>applyRootMotion = false</c> (see the animator block) is this side's
        /// answer to the same problem and it should be sufficient. This component is the belt to that
        /// pair of braces, and it is worth its twelve lines for three reasons: it costs one vector
        /// compare per frame; it cannot make anything worse (with no drift it writes nothing); and it
        /// converts an UNTESTABLE hypothesis into a MEASUREMENT — <see cref="MaxDrift"/> is the
        /// largest offset the animation ever managed to accumulate before being cancelled, and it is
        /// printed once per process, so the next hardware round reads the answer off Player.log
        /// instead of arguing about it.</para>
        ///
        /// <para><b>LateUpdate AND NOT THE FEATURE'S OWN TICK.</b> Unity evaluates animation between
        /// <c>Update</c> and <c>LateUpdate</c>; this feature is driven from <c>SkyAlternative.Tick</c>,
        /// which is an <c>Update</c>. A pin written there would always be cancelling the PREVIOUS
        /// frame's drift — enough to stop it accumulating, not enough to stop it being drawn. So the
        /// pin lives on a component of its own, in the same phase the game does it in.</para>
        ///
        /// <para>It is added to the clone's ROOT (which <see cref="Strip"/> has already finished
        /// with) rather than to the animator's object, so nothing that walks the animated hierarchy
        /// can see it, and <see cref="Release"/> destroys it with the clone.</para>
        ///
        /// <para><b>ModBuild 151 — THE MEASUREMENT ABOVE WAS TAKEN ON THE WRONG TRANSFORM AND, AS IT
        /// HAPPENS, ALSO ON THE WRONG CREATURE. BOTH FAULTS ARE FIXED HERE.</b> The ModBuild 150 log
        /// reads "the animation put at most 0.00 mm on the animator's OWN transform" (Player.log:7161)
        /// and that was read as "the clips do not carry travel". It does not follow, twice over:</para>
        /// <list type="number">
        /// <item><b>WRONG TRANSFORM.</b> <c>applyRootMotion = false</c> does not mean a clip has no
        /// translation in it — it means Unity does not EXTRACT translation onto the GameObject
        /// transform. These are the game's own Generic monster rigs, not humanoids this project
        /// controls (the decompiled sources contain no <c>GetBoneTransform</c>, no
        /// <c>HumanBodyBones</c>, no <c>AvatarMask</c> and no read of <c>Animator.avatar</c> anywhere;
        /// bones are addressed by string, e.g. <c>C_headSkel01_JNT</c>,
        /// WorldspaceDisplayPanelBase.cs:103). On such a rig the forward motion stays in the BONE
        /// CURVES: a root or hip joint translates inside the hierarchy, the whole skinned mesh slides
        /// with it, and at the end of the cycle it snaps back. The old pin watched the ONE transform
        /// that provably does not move, so it could not have seen any of it.</item>
        /// <item><b>WRONG CREATURE.</b> <c>_driftLogged</c> was a once-per-PROCESS latch, and the first
        /// apparition of the ModBuild 150 session was <c>'Living Spirit'</c> — the treeline watcher,
        /// which is STANDING (Player.log:6965). The single line the walkers needed was spent on a
        /// creature that never takes a step. It is now latched per creature and says which.</item>
        /// </list>
        ///
        /// <para><b>THE USER'S REPORT IS THE SPECIFICATION, verbatim (ModBuild 150):</b> "Es ist ein
        /// 'Zurück-Teleportieren'. Die Figur rennt schnell vor und teleportiert sich dann wieder ein
        /// Stück nach hinten. Das ganze passiert in einem Loop — aber es bewegt sich insgesamt weiter
        /// nach vorne. ... Also als ob die Animation selber die Figur zusätzlich zu deinem Bewegen auch
        /// nach vorne drückt." A sawtooth riding on top of the mod's own smooth travel is precisely a
        /// clip-carried translation curve on a bone, and it is a better observation than two rounds of
        /// inference were.</para>
        ///
        /// <para><b>SO THIS COMPONENT NOW DOES TWO THINGS AND PUBLISHES BOTH.</b> It walks EVERY
        /// descendant of the animator once per <c>LateUpdate</c>, measures each one's horizontal local
        /// offset from the rest pose it was born in, and keeps per node: the peak excursion, the running
        /// mean, and a WRAP COUNT — a wrap being the collapse the user described, the excursion falling
        /// by more than half its own peak between two frames. From those three the log names the node
        /// that carries the travel, its depth and path, its peak-to-peak amplitude in millimetres and
        /// its period in seconds. That single line settles the question permanently, whichever way it
        /// falls.</para>
        ///
        /// <para><b>AND IT CANCELS.</b> The pin set is the ROOT CHAIN: every direct child of the
        /// animator, plus the single-child descent below each of them down to and including the first
        /// node that branches. On a normal rig that is exactly {root, hips} — the branch point is the
        /// pelvis, where the spine and the two legs part company — and whole-body travel is always
        /// authored at or above it. Arms and legs are BELOW the branch and are never touched, so the
        /// gait itself is untouched: the legs cycle, the feet plant, and only the body's horizontal
        /// slide is removed.</para>
        ///
        /// <para><b>HORIZONTAL ONLY, AND THAT IS THE POINT.</b> A walk cycle bobs; cancelling the
        /// vertical as well would turn a gait into a glide, which is the failure this whole area has
        /// been chasing since ModBuild 148. The cost is any authored lateral weight-shift on the root
        /// itself, which is a few centimetres of sway at 6-13 m and is worth a figure that never steps
        /// backwards.</para>
        ///
        /// <para><b>WHY NOT <c>applyRootMotion = true</c> WITH AN <c>OnAnimatorMove</c> THAT DISCARDS
        /// <c>deltaPosition</c>.</b> It was considered and rejected. It would only help clips that have
        /// an EXTRACTABLE root, which is the case this side has no evidence for and which the 0.00 mm
        /// measurement is actually good evidence against; it would hand Unity back a job this feature
        /// already does correctly (the mod owns the travel); and the game itself does not use it —
        /// there is no <c>OnAnimatorMove</c> and no <c>Animator.deltaPosition</c> anywhere in the
        /// decompiled sources, and <c>applyRootMotion</c> is never assigned. <c>ActorBehaviour.ApplyMotion</c>
        /// harvests the animator transform's WORLD position into the actor root and re-seats the child
        /// at local zero (:610-611), which is what the pin above already reimplements. The bone-curve
        /// case it does NOT cover is what this scan adds.</para>
        /// </summary>
        private sealed class AnimPin : MonoBehaviour
        {
            /// <summary>Enough for any character rig in this game and a hard stop against a pathological
            /// hierarchy: the scan is per frame and its cost is linear in this number.</summary>
            private const int MaxNodes = 192;

            /// <summary>Below this a horizontal excursion is a modelling offset or float noise rather
            /// than travel. 2 mm, i.e. a hundredth of the shortest stride in the roster.</summary>
            private const float TravelFloorMetres = 0.002f;

            internal Transform? Target;
            internal Vector3 Seat;

            /// <summary>Largest offset, in local units, the animation managed to put on the
            /// animator's transform before this component took it off again. Kept because it is the
            /// ModBuild 148 measurement and it is still worth having — see the class doc for what it
            /// does and does not mean.</summary>
            internal float MaxDrift;

            private Transform[] _nodes = System.Array.Empty<Transform>();
            private Vector3[] _rest = System.Array.Empty<Vector3>();
            private bool[] _pin = System.Array.Empty<bool>();
            private int[] _depth = System.Array.Empty<int>();
            private float[] _peak = System.Array.Empty<float>();
            private float[] _lastMag = System.Array.Empty<float>();
            private Vector2[] _sum = System.Array.Empty<Vector2>();
            private int[] _wraps = System.Array.Empty<int>();
            private float[] _firstWrap = System.Array.Empty<float>();
            private float[] _lastWrap = System.Array.Empty<float>();
            private int _samples;
            private int _pinned;

            /// <summary>Take the rest pose and choose the pin set. Called once, at spawn, with the
            /// animator already live and already told which state to play — the localPositions read
            /// here are the ones the rig was authored with.</summary>
            internal void Seed(Animator a)
            {
                Transform root = a.transform;
                Target = root;
                Seat = root.localPosition;

                var nodes = new List<Transform>(64);
                var depth = new List<int>(64);
                Gather(root, 0, nodes, depth);

                int n = nodes.Count;
                _nodes = nodes.ToArray();
                _depth = depth.ToArray();
                _rest = new Vector3[n];
                _pin = new bool[n];
                _peak = new float[n];
                _lastMag = new float[n];
                _sum = new Vector2[n];
                _wraps = new int[n];
                _firstWrap = new float[n];
                _lastWrap = new float[n];
                for (int i = 0; i < n; i++)
                {
                    _rest[i] = _nodes[i].localPosition;
                    _firstWrap[i] = -1f;
                }

                // THE ROOT CHAIN — see the class doc. Every direct child of the animator, then the
                // single-child descent below it, stopping at (and including) the first node that
                // branches. Two shapes of rig both come out right: animator -> root -> hips -> {spine,
                // legs} pins {root, hips}; animator -> {hips, mesh} pins {hips, mesh}, and a mesh node
                // sitting at its own origin is a write of the value it already holds.
                for (int c = 0; c < root.childCount; c++)
                {
                    Transform t = root.GetChild(c);
                    while (true)
                    {
                        int idx = System.Array.IndexOf(_nodes, t);
                        if (idx < 0)
                            break;
                        if (!_pin[idx])
                        {
                            _pin[idx] = true;
                            _pinned++;
                        }
                        if (t.childCount != 1)
                            break;
                        t = t.GetChild(0);
                    }
                }
            }

            private static void Gather(Transform t, int depth, List<Transform> nodes, List<int> depths)
            {
                for (int i = 0; i < t.childCount && nodes.Count < MaxNodes; i++)
                {
                    Transform c = t.GetChild(i);
                    nodes.Add(c);
                    depths.Add(depth + 1);
                    Gather(c, depth + 1, nodes, depths);
                }
            }

            private void LateUpdate()
            {
                // The ModBuild 148 pin on the animator's own transform, unchanged.
                if (Target != null)
                {
                    float d = (Target.localPosition - Seat).magnitude;
                    if (d > MaxDrift)
                        MaxDrift = d;
                    // The write is CONDITIONAL: a transform write dirties Unity's hierarchy even when
                    // the value is unchanged, and on a skinned figure that is a bind-pose recompute.
                    if (d > 1e-6f)
                        Target.localPosition = Seat;
                }

                if (_nodes.Length == 0)
                    return;

                float now = Time.time;
                _samples++;

                for (int i = 0; i < _nodes.Length; i++)
                {
                    Transform t = _nodes[i];
                    if (t == null)
                        continue;

                    // MEASURE FIRST, CANCEL SECOND, and the order is the whole instrument. Unity
                    // re-evaluates a clip from its curves every frame rather than integrating it, so
                    // what is read here is the animation's ABSOLUTE authored value at this time — a
                    // node cancelled last frame still reports its full excursion this frame, and the
                    // amplitude below is the clip's own and not a residue.
                    Vector3 lp = t.localPosition;
                    Vector3 r = _rest[i];
                    float ox = lp.x - r.x;
                    float oz = lp.z - r.z;
                    float mag = Mathf.Sqrt(ox * ox + oz * oz);

                    if (mag > _peak[i])
                        _peak[i] = mag;
                    _sum[i] += new Vector2(ox, oz);

                    // A WRAP IS THE SNAP BACK THE USER DESCRIBED: the horizontal excursion collapsing
                    // by more than half of the largest it has ever reached, between two frames. An
                    // oscillation (an arm swinging) passes through its rest pose smoothly and never
                    // trips this; a sawtooth trips it exactly once per cycle, which is what makes the
                    // period below a real measurement rather than the clip length restated.
                    if (_peak[i] > TravelFloorMetres && _lastMag[i] - mag > 0.5f * _peak[i])
                    {
                        _wraps[i]++;
                        if (_firstWrap[i] < 0f)
                            _firstWrap[i] = now;
                        _lastWrap[i] = now;
                    }
                    _lastMag[i] = mag;

                    if (!_pin[i] || mag <= 1e-6f)
                        continue;
                    // THE CANCELLATION. Horizontal only — the vertical bob is the gait.
                    t.localPosition = new Vector3(r.x, lp.y, r.z);
                }
            }

            /// <summary>The descendant scan, as one line for the log. Names the node with the largest
            /// horizontal excursion, how deep it is, how far it travels, and how often it snaps
            /// back.</summary>
            internal string Report()
            {
                if (_nodes.Length == 0 || _samples == 0)
                    return "the descendant scan never ran (no animator, or no frame was drawn)";

                int best = -1;
                for (int i = 0; i < _nodes.Length; i++)
                    if (best < 0 || _peak[i] > _peak[best])
                        best = i;

                if (best < 0 || _peak[best] <= TravelFloorMetres)
                    return $"{_nodes.Length} descendant(s) of the animator were measured over "
                           + $"{_samples} frame(s) and NOT ONE of them moved horizontally by more than "
                           + $"{TravelFloorMetres * 1000f:F1} mm (the largest was "
                           + $"{_peak[Mathf.Max(best, 0)] * 1000f:F2} mm). So the clips really do NOT "
                           + "carry travel, on the animator's transform OR in the bone curves, and the "
                           + "remaining suspect for a figure that appears to jump is the GAIT — compare "
                           + "the m/s on the 'armed at shared clock' line with the walk clip length on "
                           + "the CENSUS line for the same creature.";

                float pk = _peak[best] * 1000f;
                Vector2 mean = _sum[best] / _samples;
                float meanMm = mean.magnitude * 1000f;
                int wraps = _wraps[best];
                float period = wraps >= 2 && _lastWrap[best] > _firstWrap[best]
                                   ? (_lastWrap[best] - _firstWrap[best]) / (wraps - 1)
                                   : -1f;

                return $"{_nodes.Length} descendant(s) measured over {_samples} frame(s). THE NODE THAT "
                       + $"MOVES is '{Path(_nodes[best])}' at depth {_depth[best]} below the animator: "
                       + $"peak horizontal excursion {pk:F1} mm from its rest pose, time-averaged offset "
                       + $"{meanMm:F1} mm, {wraps} snap-back(s)"
                       + (period > 0f
                              ? $" at a period of {period:F2}s"
                              : " (too few to time a period)")
                       + $". It is {(_pin[best] ? "IN" : "NOT IN")} the pin set, which holds "
                       + $"{_pinned} node(s). A large peak with a NON-ZERO mean is the sawtooth the user "
                       + "described — the clip pushes the body forward and drops it back — and the pin "
                       + "is what removes it. A large peak with a mean near zero is an oscillation (an "
                       + "arm, a chain) and is none of this feature's business. IF THE NODE NAMED HERE "
                       + "IS NOT IN THE PIN SET AND ITS MEAN IS LARGE, that is the next edit: the root "
                       + "chain did not reach it and the pin set has to be widened to this depth.";
            }

            private static string Path(Transform t)
            {
                var sb = new StringBuilder(64);
                sb.Append(t.name);
                Transform? p = t.parent;
                for (int guard = 0; p != null && guard < 8; guard++, p = p.parent)
                    sb.Insert(0, p.name + "/");
                return sb.ToString();
            }
        }

        /// <summary>Renderer materials, INSTANTIATED once (Unity's <c>Renderer.materials</c> getter
        /// clones the shared set) and owned outright from that moment: Unity does not destroy
        /// instantiated materials with their renderer, which is the leak <c>OverlayMaterialOwner</c>
        /// exists for on the figure-grab side. These are destroyed explicitly in
        /// <see cref="Release"/>.</summary>
        private static readonly List<Material> Mats = new(16);
        private static readonly List<Renderer> Rends = new(8);

        private static int _dissolveId = -1;
        private static int _toggleId = -1;

        /// <summary>The other two halves of the game's own visibility triple
        /// (CharacterManager.cs:243-245, :427-429). Owned by this side since the glowing-silhouette
        /// report — see the class doc.</summary>
        private static int _glowId = -1;
        private static int _opacityId = -1;

        /// <summary>THE MATERIALISE AND THE DISSOLVE, taken from the game's own two runtime drivers
        /// rather than from the property NAMES. <c>SummonAppear</c> (decompiled :61-166) and
        /// <c>DeathDissolve</c> (decompiled :135-196) both drive <c>_Cutout</c> — 0 solid, 1 gone —
        /// under <c>_Toggle_Dissolve</c>, and NEITHER of them ever writes <c>_DeathDissolvePos</c> or
        /// <c>_DeathDissolveTop/_Bottom</c>. Leaving those three exactly as the artists authored them
        /// is therefore the reading with two shipped precedents, and it is what makes the
        /// object-space-versus-world-space question moot: every monster death in the game sweeps
        /// through the same authored band on the same meshes.</summary>
        private static int _cutoutId = -1;

        /// <summary>The burn edge's colour and strength. Driven from <see cref="Shade"/> off
        /// <see cref="CinderGlow"/> and deliberately NOT from the generic emission loop — see THE
        /// CINDER TRIO IS EXEMPT in <see cref="BindEmissive"/>.</summary>
        private static int _cinderColId = -1;
        private static int _cinderGlowId = -1;

        /// <summary>The authored cinder colour, captured once per apparition before anything is
        /// written, so the hue that survives the darkening is the artists' and not a value this side
        /// invented. Defaults to the shipped (1.000, 0.443, 0.051).</summary>
        private static Color _cinderColour = new(1f, 0.443f, 0.051f, 1f);

        /// <summary>
        /// THE DARKENING LEVER, in priority order — the first one a material actually declares wins,
        /// and only ONE is ever driven per material.
        ///
        /// <para><b><c>_MOD_TINT</c> IS FIRST BECAUSE THE GAME ITSELF WRITES IT ON EXACTLY THESE
        /// MATERIALS, which is the strongest evidence available without hardware.</b>
        /// <c>Choreographer</c> spawns every character, tests
        /// <c>skinnedMeshRenderer.material.shader.name == "Amp_Char_Shader"</c> and then writes
        /// <c>material.SetColor("_MOD_TINT", colour)</c> from the creature's own
        /// <c>MonsterYML.ColourHTML</c> (Choreographer.cs:826, :851-853, and again at :1034-1036).
        /// So the character shader is named, it declares the property, and the property is the
        /// whole-model colour identity rather than a mask or a decal — a monster whose YML colour is
        /// dark IS dark all over. Multiplying it is therefore a multiply on the albedo, which is the
        /// one lever that works NO MATTER WHAT THE LIGHTING DOES, and that is the point (see
        /// <see cref="Lighting"/>'s "WHY A TINT AND NOT ONLY SH" block).</para>
        ///
        /// <para>The rest are ordinary Unity/Amplify albedo tints, tried only if <c>_MOD_TINT</c> is
        /// absent. ONE PER MATERIAL, never two: if a shader declared both <c>_MOD_TINT</c> and
        /// <c>_Color</c> and both multiplied the albedo, writing both would square the darkening and
        /// a figure meant to sit at a fiftieth of its albedo would sit at a two-thousandth — black, in a
        /// black room, i.e. an event that never happened.</para>
        ///
        /// <para><b>ModBuild 150: <c>_Diffuse</c> IS THE ONE THAT BIT, AND IT BIT THROUGH A MISSING
        /// TYPE CHECK.</b> The ModBuild 149 hardware log carries an ordinary Unity error next to the
        /// material dump (Player.log:4606, :11405):
        /// <c>Material 'MO_Cultist_HighPriest_MAT_2Side (Instance)' with Shader
        /// 'Amp_Char_Shader_2Side' doesn't have a color property '_Diffuse'</c>. On that shader
        /// <c>_Diffuse</c> is the albedo TEXTURE — <c>Material.HasProperty</c> answers TRUE for it
        /// (it exists, it is simply not a colour), <c>GetColor</c> logs that error and returns
        /// <c>(0,0,0,0)</c>, and the near-black guard below then dropped the material silently. So a
        /// material with no colour lever at all went through the same door as a material authored
        /// black, and the log said nothing except a stray engine error nobody attributed. That is why
        /// <see cref="Bind"/> now resolves the property TYPE from the shader before reading it, and
        /// why <see cref="Diag.Materials"/> prints, per material, which of these names it declares
        /// AND with what type.</para>
        /// </summary>
        private static readonly string[] TintNames = { "_MOD_TINT", "_Color", "_Tint", "_TintColor", "_Diffuse" };

        /// <summary>Second pass, and only reached when NONE of <see cref="TintNames"/> is a colour on
        /// this shader: any Color-typed property whose name reads like an albedo tint. Substring
        /// match, lower-cased, so a shader nobody in this project has ever seen still gets a lever.
        /// <see cref="TintNever"/> is checked FIRST and wins — it is what keeps this off the
        /// emissive, rim and highlight colours, which are added to the albedo rather than
        /// multiplying it and would BRIGHTEN a figure this side is trying to put out.</summary>
        private static readonly string[] TintLike = { "tint", "colour", "color", "albedo", "diffuse", "base" };

        /// <summary>Colour properties that are never an albedo multiply. Checked before
        /// <see cref="TintLike"/>, so <c>_GlowColor</c> and <c>_HighlightColour</c> — both of which
        /// Amp_Char_Shader_2Side really does declare (Player.log:11417) — can never be picked.</summary>
        private static readonly string[] TintNever =
        {
            "glow", "emiss", "rim", "outline", "highlight", "spec", "fog", "shadow", "cinder",
            "dissolve", "burn", "fresnel", "sss", "subsurface",
        };

        /// <summary>
        /// EMISSION — THE TERM AN ALBEDO MULTIPLY CANNOT REACH, and the whole of what was left of the
        /// "viel zu hell" report once the body had gone dark.
        ///
        /// <para><b>USER REPORT, ModBuild 150, verbatim:</b> "Die Figur ist immer noch viel zu hell, in
        /// der dunklen Ecke wo die Figur steht sollte nur eine Silhouette sichtbar sein da es so dunkel
        /// ist." This is NOT the previous complaint repeated — it is a different picture with a
        /// different cause, and the picture says so. Measured off
        /// <c>.planning/debug/Figur_hell2.jpg</c>, linearised to Rec.709 luminance: of 8 294 400 pixels
        /// exactly <b>190</b> exceed 0.05, and every one of those 190 lies inside
        /// x[1804..1908] y[1026..1183] — the eyes, the brow ridge and a pendant on the sternum. Their
        /// peak is <b>0.892</b>; the mean linear colour of the brightest 0.03% of the frame is
        /// (0.030, 0.042, 0.055), i.e. CYAN; and the median luminance of the body inside the same crop
        /// is <b>0.00015</b>. A ratio of roughly 6000:1 between the face and the torso of ONE creature
        /// cannot be produced by any multiply on any one albedo property — last round's lever landed,
        /// and what is left is a second term that was never touched.</para>
        ///
        /// <para><b>WHAT IT IS, read off the ModBuild 150 log rather than inferred</b>
        /// (Player.log:7110 — the AFTER dump, taken while the albedo was already multiplied by 0.048).
        /// All six materials of the treeline watcher carry:</para>
        /// <code>
        ///   _UseEmissiveMap = 1.000   _EmissiveMap = MO_LivingSpirit_emissive   _EmissiveMapBoost = 2.000
        /// </code>
        /// <para>Emission is ADDED after the albedo and after lighting, so <c>_MOD_TINT</c> cannot reach
        /// it, and the boost DOUBLES it. Nor does the game ever normalise it: the ONLY four properties
        /// <c>CharacterManager.RefreshVisibility</c> writes are <c>_InvisibilityControl</c>,
        /// <c>_Glow</c>, <c>_Opacity</c> and <c>_Toggle_Dissolve</c> (CharacterManager.cs:413-436). The
        /// emissive channel is authored on the material asset and NOTHING in the shipped game ever
        /// touches it — which is exactly the trap the class doc already documents for the other four,
        /// arrived at a second time by a different route.</para>
        ///
        /// <para><b>THE OTHER TWO CANDIDATES ARE RULED OUT BY THE SAME EVIDENCE.</b> There is no
        /// separate eye or face material and no second renderer on the head: the full renderer census
        /// for the watcher is six SkinnedMeshRenderers (<c>_BackCloth, _Chains, _Cloth, _Main,
        /// _SleeveCloth, _WristChains</c>) sharing two materials, and no material, submesh or renderer
        /// anywhere in the game contains "eye", "iris" or "pupil" — the head exists only as the BONE
        /// <c>C_headSkel01_JNT</c> (WorldspaceDisplayPanelBase.cs:103). And nothing this side pins is
        /// inverted: the AFTER dump shows <c>_Glow = 0</c>, <c>_Opacity = 1</c>,
        /// <c>_InvisibilityControl = 0</c>, <c>_Toggle_Dissolve = 0</c> exactly as intended.</para>
        ///
        /// <para>Names are matched as SUBSTRINGS, lower-cased, so a creature on a shader nobody in this
        /// project has looked at is still caught. <c>_Glow</c> is deliberately NOT driven from here:
        /// <see cref="Shade"/> already pins it to a hard 0, which is strictly darker than any scale of
        /// the authored 10 could be.</para>
        /// </summary>
        private static readonly string[] EmissiveLike =
        {
            "emissive", "emission", "glow", "cinder", "highlight", "selfillum", "rim", "fresnel",
        };

        /// <summary>
        /// The emission properties that are 0/1 SWITCHES rather than strengths, and which are therefore
        /// driven to a hard 0 instead of being scaled.
        ///
        /// <para><b>WHY A SWITCH IS NOT SCALED, and this is the honest half of this round.</b> There is
        /// no ShaderLab source for these shaders anywhere — the character family ships as four compiled
        /// Amplify graphs (<c>Amp_CharShader</c>, <c>Amp_CharShader_2Side</c>, <c>Amp_CharDistort</c>,
        /// <c>Amp_CharShader_Alpha_Ether</c>, Player.log:1497/:1501) and this project can read their
        /// property LIST but never their wiring. So what <c>_UseEmissiveMap = 0.048</c> would mean is
        /// genuinely unknown: a lerp alpha would give 4.8% of the map, a step or a branch node would
        /// give either all of it or none. 0 is the one value that means the same thing under every one
        /// of those readings, and it is the value that guarantees the outcome the user asked for five
        /// times. The un-gated STRENGTHS are scaled instead — see <see cref="EmissiveShare"/>.</para>
        /// </summary>
        private static readonly string[] EmissiveGate = { "_use", "_toggle", "_animate", "asmask" };

        /// <summary>Emission properties that are RATES rather than amounts, and which are therefore
        /// left exactly as authored. The same argument as <see cref="EmissiveGate"/> from the other
        /// end: <c>_Emissive_Anim_Time</c> is a period, and multiplying a period by 0.004 does not make
        /// a pulse dimmer, it makes it two hundred and fifty times faster. Amplitudes beside it
        /// (<c>_Emissive_Anim_Min</c>, <c>_Emissive_Anim_Max</c>) are amounts and ARE scaled.</summary>
        private static readonly string[] EmissiveRate = { "time", "speed", "freq", "phase", "scroll" };

        /// <summary>
        /// How bright an un-gated emissive STRENGTH may leave a texel, as a fraction of what the same
        /// room light makes of a mid-grey albedo texel on the same creature.
        ///
        /// <para><b>0.25, and the arithmetic is one line.</b> An emissive map peaks at 1.0 by
        /// construction while a character's albedo peaks around 0.5, so at the wood's k = 0.048 the
        /// body's brightest pixel lands at 0.5 x 0.048 = 0.024, and an emissive texel driven at
        /// 1.0 x 2.0 (the authored <c>_EmissiveMapBoost</c>) x 0.048 x 0.25 lands at 0.024 as well.
        /// THE EMISSIVE DETAIL COMES OUT AT EXACTLY THE BRIGHTNESS OF THE BODY, which is the definition
        /// of a silhouette.</para>
        ///
        /// <para>It is a FRACTION and not an absolute because the whole point is that it tracks the
        /// room: in the cellar (k = 0.015) both sides fall together to 0.0075 and the relationship
        /// holds without a second number to tune.</para>
        /// </summary>
        private const float EmissiveShare = 0.25f;

        /// <summary>One material's emission control: which property, whether it is a switch or a
        /// strength, and what it shipped with. Same shape and same discipline as
        /// <see cref="TintTarget"/> — the ORIGINAL is captured in <see cref="Collect"/>, strictly
        /// before this side has written anything, and every write is derived from it.</summary>
        private readonly struct EmissiveTarget
        {
            internal readonly Material Mat;
            internal readonly int Prop;
            internal readonly bool IsColour;
            internal readonly bool IsGate;
            internal readonly float OrigF;
            internal readonly Color OrigC;

            internal EmissiveTarget(Material mat, int prop, bool isColour, bool isGate,
                                    float origF, Color origC)
            {
                Mat = mat; Prop = prop;
                IsColour = isColour; IsGate = isGate;
                OrigF = origF; OrigC = origC;
            }
        }

        private static readonly List<EmissiveTarget> Emissives = new(32);

        /// <summary>One line per material saying what the emission census found and what was done with
        /// it, printed by <see cref="Diag.Materials"/>. Parallel to <see cref="Verdicts"/>, and it is
        /// the line that answers the NEXT question rather than this one: a creature that is still too
        /// bright after this build has either no emission at all (in which case the albedo lever is the
        /// only suspect left) or one this list did not name.</summary>
        private static readonly List<string> EmissiveVerdicts = new(16);

        /// <summary>One material's darkening lever plus the value it shipped with. The ORIGINAL is
        /// captured before this side writes anything, and every write is
        /// <c>original * k</c> — so the creature's own colour identity survives the darkening
        /// instead of being replaced by a grey.</summary>
        private readonly struct TintTarget
        {
            internal readonly Material Mat;
            internal readonly int Prop;
            internal readonly Color Original;

            /// <summary>True when this lever is <c>_MOD_TINT</c> itself — the property the GAME
            /// writes — rather than one of the generic albedo tints. It selects the ALPHA policy and
            /// nothing else; see THE ALPHA GATE in <see cref="Shade"/>.</summary>
            internal readonly bool IsModTint;

            internal TintTarget(Material mat, int prop, Color original)
            {
                Mat = mat; Prop = prop; Original = original;
                IsModTint = prop == ModTintId;
            }
        }

        /// <summary>The id of <c>_MOD_TINT</c>, resolved once. Compared against
        /// <see cref="TintTarget.Prop"/> rather than the property NAME, because the name is not kept
        /// per target and a string compare per material per frame would be the one allocation this
        /// path does not have.</summary>
        private static readonly int ModTintId = Shader.PropertyToID("_MOD_TINT");

        private static readonly List<TintTarget> Tints = new(16);

        /// <summary>Which property name <see cref="Collect"/> settled on, for the log. Empty when no
        /// material declared any of them, which is the case the renderer fallback exists for.</summary>
        private static string _tintName = string.Empty;

        /// <summary>One human-readable verdict per material, in <see cref="Mats"/> order, written by
        /// <see cref="Bind"/> and printed by <see cref="Diag.Materials"/>. THIS IS THE LINE THE LAST
        /// ROUND DID NOT HAVE: "2 of 3 material(s)" said that one material carried no lever and
        /// nothing at all about WHICH, on what shader, or why — and the missing one turned out to be
        /// three quarters of what the player could actually see.</summary>
        private static readonly List<string> Verdicts = new(16);

        /// <summary>
        /// A shader that DOES declare a working <c>_MOD_TINT</c>, kept for the session so a creature
        /// whose every material is un-tintable can still borrow one. It is captured from a material
        /// on a real apparition rather than looked up: <c>Shader.Find</c> only sees shaders in
        /// Resources or in the always-included list, and these live in an npc asset bundle.
        ///
        /// <para>See <see cref="Donate"/> for what it is used for and for the one naming rule that
        /// stops it being used on an unrelated shader.</para>
        /// </summary>
        private static Shader? _donor;

        /// <summary>How many materials this apparition had to be moved onto <see cref="_donor"/>,
        /// for the census line. Diagnostics only.</summary>
        private static int _donated;

        /// <summary>The last COMBINED scalar written (envelope × room light), so the per-frame write
        /// is skipped when neither moved. -1 forces the first write.</summary>
        private static float _lastShade = -1f;

        /// <summary>The last <c>_Cutout</c> written, so the write-skip test cannot swallow a frame of
        /// the materialise or the dissolve. -1 rather than 0 so the first write always happens.
        /// </summary>
        private static float _lastCutout = -1f;

        private static bool _spawned;
        private static bool _voiced;
        private static string _wantModel = string.Empty;
        private static CClass.ENPCModel _wantEnum = CClass.ENPCModel.None;
        private static float _wantHeight;
        private static Transform? _wantParent;

        /// <summary>The PLACED environment room root, handed down from <see cref="HauntFigures.Tick"/>
        /// through <c>Arm</c>. It is the only thing that knows what the room is lit by — see
        /// <see cref="Light"/>.</summary>
        private static Transform? _wantRoom;

        private static SkyStyle _wantStyle = SkyStyle.Default;

        /// <summary>Half the figure's finished height, in world metres. The point the room's light
        /// rig is sampled AT (see <see cref="Lighting.Apply"/>): a creature's chest, not its feet.</summary>
        private static float _halfHeight = 0.9f;

        private static int _fails;

        // ---- THE GAIT ------------------------------------------------------------------------------

        /// <summary>How fast the path this apparition is walking moves it, in world metres per
        /// second, handed down from <see cref="HauntFigures.Arm"/>. Zero for a figure that stands.</summary>
        private static float _gaitSpeed;

        /// <summary>The <c>RunBlend</c> weight <see cref="Gait"/> derived from
        /// <see cref="_gaitSpeed"/>, or -1 before it has run (in which case the event's authored
        /// value is used).</summary>
        private static float _gaitBlend = -1f;

        /// <summary>The implied gait speed the EVENT assumed when it sized this run's path, in m/s at
        /// full blend. Diagnostics only — see the <c>impliedEstimate</c> parameter of
        /// <see cref="Request"/>.</summary>
        private static float _gaitEstimate;

        /// <summary>
        /// THE GROUND A HUMANOID GAIT CYCLE COVERS AT SCALE 1, in metres, and it is the ONE number in
        /// this derivation that is an assumption rather than a measurement — so it is named, stated
        /// once, and printed in the log beside everything it produces.
        ///
        /// <para>1.35 m is two steps of a ~1.8 m biped at an unhurried walk. The clips this feature
        /// plays are single gait cycles (<c>LivingCorpse_Walk_v001</c> 0.87 s,
        /// <c>LivingBones_Run_v001</c> 0.93 s, <c>Savvas_LavaFlow_Move_v001</c> 1.67 s,
        /// <c>Hound_Move</c> 0.50 s), so a creature at scale <c>s</c> whose cycle lasts <c>T</c>
        /// implies a ground speed of <c>1.35 * s / T</c> at full blend. The value cannot be read off
        /// the clip because the travel is INSIDE the clip's root motion and this feature switches
        /// root motion off (see <see cref="AnimPin"/>) — <c>AnimationClip.averageSpeed</c> would
        /// answer it, but only for clips that carry root curves, and the ModBuild 149 measurement
        /// says these do not carry them on the animator's own transform.</para>
        ///
        /// <para>IF THE NEXT ROUND WANTS TO MOVE ONE NUMBER, IT IS THIS ONE. The log line printed by
        /// <see cref="Gait"/> gives the speed, the scale, the clip length and the implied stride
        /// together, so raising or lowering it is a single arithmetic step from what the log
        /// says.</para>
        /// </summary>
        private const float StrideMetres = 1.35f;

        /// <summary>Names that mark the locomotion clip in a controller that has no walk STATE — see
        /// the ANIMATION block in the class doc for why "Idle-Run" is a blend and not a state
        /// machine. Tried in order; the first match wins.</summary>
        private static readonly string[] WalkClipNames = { "walk", "_run", "run_", "move", "locomotion" };

        /// <summary>Below this the Idle-Run tree is more idle than locomotion and the pose reads as
        /// standing still while the body glides — which is the user's "teleportiert sich" seen from
        /// the outside. Under it the blend is held here and <c>Animator.speed</c> takes the
        /// difference instead. See <see cref="Gait"/>.</summary>
        private const float MinBlend = 0.60f;

        /// <summary>The most the clip may be played faster than authored. A creature hurrying by 60%
        /// still looks like the creature; one at 3x looks like a fast-forward.</summary>
        private const float MaxClipSpeed = 1.60f;

        /// <summary>Three consecutive failures and the whole feature stands down for the session —
        /// see <see cref="Disable"/>. Three rather than one because a single failure is
        /// indistinguishable from a bundle that happened to be mid-eviction, and rather than "keep
        /// trying forever" because a room that silently skips every apparition is worse than one
        /// that goes back to the shader-drawn ones.</summary>
        private const int MaxFails = 3;

        // ---- request ---------------------------------------------------------------------------------

        /// <summary>Begin building an apparition. Returns immediately; <see cref="Drive"/> finishes
        /// the job over the following frames.</summary>
        /// <param name="metresPerSecond">How fast this run's path moves the figure. 0 for a figure
        /// that stands — see <see cref="Gait"/> for what a non-zero value does to the animator.</param>
        /// <param name="impliedEstimate">What the EVENT believed this creature's gait speed to be
        /// when it chose that path, from <c>HauntFigures.ImpliedSpeed</c>. Diagnostics only: it is
        /// printed beside the value <see cref="Gait"/> measures off the real prefab, so a table that
        /// has drifted from the assets says so in the log instead of quietly mis-timing an event.</param>
        internal static void Request(string model, CClass.ENPCModel picked, in HauntEvent ev,
                                     Transform parent, Transform? room, SkyStyle style,
                                     float metresPerSecond, float impliedEstimate)
        {
            Release("a new apparition was armed");
            _gaitSpeed = Mathf.Max(metresPerSecond, 0f);
            _gaitEstimate = Mathf.Max(impliedEstimate, 0f);
            _wantModel = model;
            _wantEnum = picked;
            _wantHeight = ev.Height;
            _wantParent = parent;
            _wantRoom = room;
            _wantStyle = style;
            _voiced = false;

            if (_assetValid && _assetModel == model)
                return;   // already resident — the spawn happens on the next Drive

            ReleasePrefab($"the next apparition needs '{model}' instead of '{_assetModel}'");

            // THE PATH IS A MIRROR of AssetBundleManager.GetBundleLoadConfig's non-DLC branch
            // (AssetBundleManager.cs:295-301). It is mirrored rather than called for two reasons:
            // that method is private, and — much more importantly — the public route through it,
            // GetCharacterPrefabFromBundle (:311), ends in WaitForCompletion() and would block the
            // frame. The DLC branch is deliberately NOT mirrored because this feature never picks a
            // DLC model (Roster.cs explains why that is a correctness requirement and not a
            // simplification).
            string path = "Assets/_AssetBundles/npcs/" + model + ".prefab";
            try
            {
                _asset = Addressables.LoadAssetAsync<GameObject>(path);
                _assetModel = model;
                _assetValid = true;
            }
            catch (System.Exception ex)
            {
                _assetValid = false;
                Fail($"the enemy prefab '{path}' could not be requested ({ex.GetType().Name}: {ex.Message})");
            }
        }

        // ---- the gait ---------------------------------------------------------------------------------

        /// <summary>
        /// MATCH THE LEGS TO THE TRAVEL. One <c>RunBlend</c> weight and one <c>Animator.speed</c>,
        /// derived once per apparition from the path's real speed, the creature's real scale and the
        /// real length of its own locomotion clip.
        ///
        /// <para><b>THE USER REPORT THIS ANSWERS, verbatim (ModBuild 149):</b> "Das Teleportieren der
        /// Figuren die sich bewegen ist nach wie vor da." The ModBuild 148 instrument that was built
        /// to find it has already ruled out the obvious cause: <see cref="AnimPin"/> measured 0.00 mm
        /// of clip-carried travel on the animator's own transform over a whole apparition
        /// (Player.log:11824's neighbour at :4678), so the ANCHOR is smooth and
        /// <c>applyRootMotion = false</c> is holding. What is left is the gait.</para>
        ///
        /// <para><b>WHAT <c>RunBlend</c> ACTUALLY IS, from the game rather than from inference.</b>
        /// It is the blend weight of the "Idle-Run" tree, driven per frame in
        /// <c>ActorBehaviour.DoTransform</c> (:546-547) toward <c>m_TargetAnimSpeed</c>. And
        /// <c>m_TargetAnimSpeed</c> is DIMENSIONLESS, in roughly 0..1 — it carries no metres and no
        /// seconds:</para>
        /// <list type="bullet">
        /// <item>a move of more than one hex ramps it at <c>+1.0 per second</c> up to a hard
        /// <c>Mathf.Min(..., 1f)</c> (:493) — so the game's own SUSTAINED travel sits at <b>1.0</b>,
        /// not at a half;</item>
        /// <item>a single-hex hop runs an ease-in/ease-out triangle on the remaining distance with a
        /// floor of +0.2 (:503-509), i.e. 0.2 at the ends and about 1.2 in the middle;</item>
        /// <item>every "stop" path writes it to exactly 0 (:353, :523, :538, :598).</item>
        /// </list>
        /// <para>So 0.55 — which every event in this file held, at every speed, for every creature —
        /// is the game's value for a figure that is <b>barely moving</b>, played while the body glides
        /// at 1.41 m/s (Player.log:11824) or 1.62 m/s (:13130). Half the pose is a standing idle: the
        /// feet do not keep up, so the body appears to jump forward between footfalls, which is
        /// exactly what "teleportiert sich" describes from the outside.</para>
        ///
        /// <para><b>THE MAPPING.</b> The Idle-Run tree interpolates between a clip that covers no
        /// ground and one that covers a full gait cycle, so to first order — exactly at both
        /// endpoints — ground speed is <c>blend x implied</c>, where</para>
        /// <code>
        ///   implied = StrideMetres * creatureScale / walkClipSeconds     [m/s at full blend]
        ///   want    = pathSpeed / implied                                [dimensionless]
        /// </code>
        /// <para>which for the roster in the ModBuild 149 census gives, at that build's 1.62 m/s:
        /// Living Corpse (scale 0.878, 0.87 s) implied 1.36 -> 1.19; Living Bones (1.258, 0.93 s)
        /// implied 1.83 -> 0.89; High Cultist (1.636, 1.67 s) implied 1.32 -> 1.22. Every one of them
        /// wanted roughly TWICE the 0.55 it was given.</para>
        ///
        /// <para><b>AND WHERE THE BLEND ALONE CANNOT ANSWER, THE CLIP RATE DOES.</b> Below
        /// <see cref="MinBlend"/> the tree is more idle than locomotion and the pose reads as
        /// standing while gliding — the very failure this method exists to remove — so the blend is
        /// held at that floor and <c>Animator.speed</c> takes the remainder; above 1 the blend is
        /// pinned at 1 and the clip is played faster, up to <see cref="MaxClipSpeed"/>. That is what
        /// keeps the Hound honest: its <c>Hound_Move</c> cycle is 0.50 s, so it implies well over
        /// 2 m/s and a shared path speed would otherwise leave it half-blended.</para>
        ///
        /// <para><b>THE OTHER DIRECTION OF THE FIX IS IN <c>HauntFigures.Events.cs</c></b>, where
        /// <c>ForestCross</c>'s hold was lengthened so the crossing is a stroll rather than a march.
        /// Both halves were needed: this one stops the feet sliding at any speed, that one chooses a
        /// speed worth walking at.</para>
        ///
        /// <para><b>AND ModBuild 152 REVERSED WHICH OF THE TWO LEADS.</b> USER, verbatim: "Es wird
        /// ein Wolf angezeigt der schnell rennt aber die Geschwindigkeit in der er sich bewegt ist
        /// deutlich langsamer, dann mach die Bewegung auch schneller." The Hound's line in the
        /// ModBuild 151 log is this method working exactly as designed and producing the wrong
        /// picture: path 1.06 m/s against an implied 4.95 m/s, i.e. a run played at 0.63x while the
        /// body crawled. So the EVENT now sizes its path from the creature's own implied speed
        /// (<c>HauntFigures.ImpliedSpeed</c> and THE CROSSING'S SPEED, Events.cs) and this method
        /// keeps its job unchanged — it is simply handed a speed the legs already agree with, so the
        /// blend lands near the target instead of on the floor and <c>Animator.speed</c> stays at 1.
        /// The clamps below are now a guard rather than the normal path.</para>
        /// </summary>
        private static void Gait(float scale)
        {
            if (_animator == null)
                return;

            // A figure that stands gets the event's authored value, which is 0 for all three of them.
            if (_gaitSpeed <= 0.01f)
            {
                _animator.speed = 1f;
                _gaitBlend = -1f;
                return;
            }

            float clip = WalkClipSeconds(_animator);
            float implied = StrideMetres * Mathf.Max(scale, 0.05f) / Mathf.Max(clip, 0.05f);
            float want = _gaitSpeed / Mathf.Max(implied, 0.01f);

            float blend = Mathf.Clamp(want, MinBlend, 1f);
            float rate = Mathf.Clamp(want / blend, 1f / MaxClipSpeed, MaxClipSpeed);
            _gaitBlend = blend;
            _animator.speed = rate;

            VRLog.Info("Core", $"HAUNT FIGURES gait for '{_wantModel}': the path moves it at "
                + $"{_gaitSpeed:F2} m/s; its own locomotion clip is {clip:F2}s long and it is built at "
                + $"scale {scale:F3}, so at full blend it covers {StrideMetres:F2} m x {scale:F3} / "
                + $"{clip:F2}s = {implied:F2} m/s. The blend that speed deserves is therefore "
                + $"{want:F2}, driven as RunBlend {blend:F2} with Animator.speed {rate:F2} "
                + $"(the blend is clamped to [{MinBlend:F2}, 1.00] because below that the Idle-Run "
                + "tree is more idle than locomotion and the clip rate carries the remainder "
                + $"instead, capped at {MaxClipSpeed:F2}x). IT USED TO BE A CONSTANT 0.55 FOR EVERY "
                + "CREATURE AT EVERY SPEED, and that is what the user photographed as 'teleportiert "
                + "sich': RunBlend is the Idle-Run BLEND WEIGHT (ActorBehaviour.cs:546-547), not a "
                + "speed, and the game's own sustained travel drives it to 1.0 "
                + "(m_TargetAnimSpeed, ActorBehaviour.cs:493 — dimensionless, 0..1). The one "
                + $"ASSUMED number here is the {StrideMetres:F2} m a gait cycle covers at scale 1; "
                + "everything else on this line is measured, so moving it is one arithmetic step. "
                + "AND SINCE ModBuild 152 THE PATH IS DERIVED FROM THIS SPEED RATHER THAN THE OTHER "
                + "WAY ROUND (user: \"dann mach die Bewegung auch schneller\"), so the blend above "
                + "should now sit near the target rather than at the floor. THE ARM-TIME ESTIMATE "
                + $"THAT SIZED THE PATH was {_gaitEstimate:F2} m/s against the {implied:F2} m/s "
                + $"measured here — a ratio of "
                + $"{(_gaitEstimate > 0.01f ? implied / _gaitEstimate : 0f):F2}. IT MUST BE NEAR "
                + "1.00: the estimate comes from HauntFigures.ImpliedSpeed, a small table of censused "
                + "scales and clip lengths that exists only because the real prefab has not finished "
                + "loading when the path has to be chosen. A ratio far from 1 means that table has "
                + "drifted from the assets and is the one thing to correct from this line.");
        }

        /// <summary>The length of this controller's locomotion clip, in seconds — the cycle whose
        /// ground travel <see cref="Gait"/> is matching. Falls back to 1.0 s, which is close enough
        /// to every clip in the ModBuild 149 census that a miss costs a blend step rather than a
        /// gait.</summary>
        private static float WalkClipSeconds(Animator a)
        {
            RuntimeAnimatorController? rac = a.runtimeAnimatorController;
            if (rac == null)
                return 1f;
            AnimationClip[] clips = rac.animationClips;
            for (int n = 0; n < WalkClipNames.Length; n++)
            {
                for (int i = 0; i < clips.Length; i++)
                {
                    AnimationClip c = clips[i];
                    if (c == null || c.length <= 0.05f)
                        continue;
                    if (c.name.ToLowerInvariant().Contains(WalkClipNames[n]))
                        return c.length;
                }
            }
            return 1f;
        }

        // ---- drive ------------------------------------------------------------------------------------

        /// <summary>
        /// One frame: finish the build if it is still in flight, then pose, animate and shade what
        /// exists. <paramref name="presence"/> is the event's envelope, 0..1.
        ///
        /// <para><paramref name="runBlend"/> is the event's AUTHORED value and is used only for a
        /// figure that stands. A figure that travels is driven by <see cref="Gait"/> instead, from
        /// its own speed, scale and clip length — see there for why a constant was the whole of the
        /// teleport report.</para>
        ///
        /// <para><paramref name="cutout"/> is the game shader's own materialise/dissolve progress —
        /// 0 solid, 1 gone — computed by <see cref="DissolveCutout"/> from the shared clock and this
        /// apparition's own length. It is passed in rather than derived here because it and the
        /// presence envelope must come off ONE clock; see THE MATERIALISE AND THE DISSOLVE in
        /// HauntFigures.Math.cs.</para>
        /// </summary>
        internal static void Drive(float presence, float cutout, float runBlend)
        {
            if (!_spawned)
            {
                using (PerfMonitor.Scope("Env.HauntFigSpawn"))
                    Step();
                if (!_spawned)
                    return;
            }

            using (PerfMonitor.Scope("Env.HauntFigDrive"))
            {
                if (_animator != null && _hasRunBlend)
                    _animator.SetFloat(RunBlendParam, _gaitBlend >= 0f ? _gaitBlend : runBlend);

                // THE ROOM'S LIGHT, RE-SAMPLED WHERE THE FIGURE NOW STANDS, AND IT RUNS FIRST.
                // Inside the same [Perf] step as the rest of the drive on purpose: it is a handful
                // of dot products and one property-block write per renderer, and giving it a step of
                // its own would suggest it were a cost worth watching separately. THE ORDER IS
                // LOAD-BEARING since the darkening landed — Apply() is what measures
                // Lighting.Level, and Shade multiplies the envelope by it, so a Shade before an
                // Apply would spend one frame on the previous position's light every time the
                // figure moved. See the Lighting block for the numbers.
                Lighting.Apply();

                Shade(presence, cutout);

                // ONE quiet voice per event, once, when the figure is properly there — early enough
                // to be part of the arrival and late enough that it is not heard before there is
                // anything to hear. Gated on the environment-sound switch, for two independent
                // reasons that happen to agree: the standing ruling is that apparition sound rides
                // that one toggle ("nur wenn die Umgebungssounds aktiviert sind",
                // Haunt.Schedule.cs:19), AND the positional overload is only CORRECT while the ear
                // is on the VR head — EnvSound claims it through Core/HeadEar while its switch is
                // on, and with the switch off the listener is back on the game's 2D camera, metres
                // from anything, where a 3D item attenuates to silence exactly as GameAudio's class
                // doc describes.
                if (!_voiced && presence >= 0.45f && _go != null)
                {
                    _voiced = true;
                    Roster.Voice(_wantEnum, _go.transform.position);
                }
            }
        }

        /// <summary>
        /// The SAME creature is starting its run again — a latched test trigger looped. Everything
        /// that is per-RUN rather than per-CREATURE goes back to its start; nothing is destroyed and
        /// nothing is loaded.
        ///
        /// <para>Today that is exactly one thing, and it is worth the method rather than a field
        /// poke: the creature's own one-shot voice. Before this round a loop tore the clone down and
        /// built a new one, so the voice came back for free on every repetition — which is what a
        /// tester judging a cue against the picture actually needs (<see cref="EnvSound"/>'s per-card
        /// cue does the same, because it fires once per (StartClock, Card) pair and the start clock
        /// moves each loop). Keeping the creature alive would silently have made the voice a
        /// once-per-latch event instead.</para>
        /// </summary>
        internal static void Restart()
        {
            _voiced = false;
        }

        /// <summary>Pump the build one step. Each call does at most one frame's worth of work.</summary>
        private static void Step()
        {
            if (_wantParent == null || _wantModel.Length == 0)
                return;

            // 1 — wait for the prefab.
            if (!_assetValid)
                return;
            if (!_asset.IsDone)
                return;
            if (_asset.Status != AsyncOperationStatus.Succeeded || _asset.Result == null)
            {
                string why = $"the enemy prefab '{_assetModel}' failed to load "
                             + $"({_asset.Status}; {_asset.OperationException?.Message ?? "no exception"})";
                // RELEASED, not merely forgotten: this path can be reached once per attempt, and a
                // failed Addressables handle that is dropped without a Release is a refcount that
                // never comes back.
                ReleasePrefab("the load failed");
                Fail(why);
                return;
            }

            // 2 — instantiate under the inactive nursery and strip the root, ONCE.
            if (_go == null)
            {
                if (_nursery == null)
                {
                    _nursery = new GameObject("GloomhavenVR.HauntFigures.Nursery");
                    _nursery.SetActive(false);       // ← the whole trick: children are born inactive
                    Object.DontDestroyOnLoad(_nursery);
                }

                _go = Object.Instantiate(_asset.Result, _nursery.transform, worldPositionStays: false);
                // A NAME OF OUR OWN, so a log reader can tell this object from a real monster at a
                // glance and so nothing that keys on names can confuse the two.
                _go.name = "GhvrHauntFigure_" + _wantModel;

                Strip(_go, "root");

                _cm = _go.GetComponent<CharacterManager>();
                if (_cm == null)
                {
                    Fail($"the enemy prefab '{_wantModel}' has no CharacterManager, so its mesh child "
                         + "cannot be resolved");
                    return;
                }

                // See the class doc, step 2. Emptying the list is what makes the ObjectPool path and
                // the static weapon dictionary structurally unreachable.
                _cm.WeaponLists?.Clear();

                _init = _cm.InitialiseCharacterAsync(isPreview: true);
                return;
            }

            // 3 — pump the child's async instantiate. It yields null until Addressables is done.
            if (_init != null)
            {
                bool more;
                try
                {
                    more = _init.MoveNext();
                }
                catch (System.Exception ex)
                {
                    _init = null;
                    Fail($"initialising '{_wantModel}' threw {ex.GetType().Name}: {ex.Message}");
                    return;
                }
                if (more)
                    return;
                _init = null;
            }

            // 4 — the child is there (or it is not, and we say so rather than shipping an empty
            // object). The handle is the authoritative answer; a mesh-less apparition is exactly
            // the "half-built figure" the brief forbids.
            if (_cm == null || !_cm.CharacterChildPrefabInstance.IsValid()
                || _cm.CharacterChildPrefabInstance.Status != AsyncOperationStatus.Succeeded
                || _cm.CharacterChildPrefabInstance.Result == null)
            {
                Fail($"the mesh child of '{_wantModel}' did not load, so there would be nothing to see");
                return;
            }

            Strip(_go, "child");
            Collect(_go);
            if (Rends.Count == 0)
            {
                Fail($"'{_wantModel}' produced no renderers after stripping");
                return;
            }

            // 5 — scale, seat, layer, activate.
            float scale = 1f;
            if (_wantHeight > 0.01f && _cm != null && _cm.Height > 0.01f)
                scale = _wantHeight / _cm.Height;   // the prefab states its own height (:63)

            // The sampling point for the room's light rig, in world metres. The prefab's own stated
            // height times the scale we just chose, halved: the middle of a creature's body rather
            // than the ground it stands on, which is what decides whether a candle two metres away
            // pools on its chest or on its ankles.
            _halfHeight = Mathf.Max(0.2f, (_cm != null ? _cm.Height : 1.8f) * scale * 0.5f);

            _go.transform.SetParent(_wantParent, worldPositionStays: false);
            _go.transform.localPosition = Vector3.zero;
            _go.transform.localRotation = Quaternion.identity;
            _go.transform.localScale = Vector3.one * scale;

            // The ROOM is put on the mod layer (SkyAlternative.cs:1158) and only the VR head camera
            // renders that layer, so a figure standing in the room has to be on it too or it would
            // be drawn by cameras that must never see it and not by the one that must. This is a
            // mod-OWNED object — created here, destroyed here, never handed back — so re-layering
            // it does not violate VRLayers' "never re-layer game-owned objects" rule, which is about
            // reversibility of scene objects the mod merely borrows.
            VRLayers.Apply(_go);

            _go.SetActive(true);
            _spawned = true;
            _lastShade = -1f;
            _lastCutout = -1f;
            // Renderers OFF in the SAME frame the object becomes active, before anything is drawn:
            // never a pop-in. Cutout 1 = fully gone, which is the state a materialise starts from
            // (SummonAppear.OnEnable writes exactly that), so even a frame in which the renderer
            // switch lost a race shows nothing.
            Shade(0f, 1f);

            // THE ROOM'S LIGHT RIG, read ONCE per apparition off a room material and then only
            // evaluated. Deliberately after SetActive so a failure to find it cannot stop a figure
            // from existing: an unlit apparition is a bad apparition, no apparition is a bug.
            Lighting.Bind(_wantRoom, _wantStyle, _wantModel);
            Lighting.Apply();
            _fails = 0;

            // 6 — THE ANIMATOR, AND ONLY NOW. Both of the calls below need a LIVE object and it is
            // worth stating why, because doing this one step earlier looks tidier and silently does
            // nothing: MF.GetGameObjectAnimator is GetComponentsInChildren<Animator>() with the
            // default includeInactive: false (MF.cs:135-146), so on an inactive hierarchy it returns
            // null — and Animator.Play on a component whose GameObject has never been active hits an
            // uninitialised animator, which Unity answers with a warning and no state change. This
            // still happens inside Update, i.e. before anything is rendered, and presence is 0 for
            // this frame regardless, so there is no window in which a default pose can be seen.
            _animator = MF.GetGameObjectAnimator(_go);
            if (_animator != null)
            {
                _animator.applyRootMotion = false;   // we own the travel — see ANIMATION in the class doc
                _animator.fireEvents = false;        // ...and the audio decision, also in the class doc
                // ALWAYS ANIMATE, deliberately, and it is the more expensive of the two choices.
                // The figures are peripheral by design: they cross at the very edge of what the
                // player is looking at, so a culled animator would be resumed mid-glimpse, and the
                // pose it resumes into is the pose it froze in — a figure that visibly snaps as it
                // enters view. One skinned animator at a time is a price worth paying for that, and
                // the [Perf] step "Env.HauntFigDrive" is what will say whether that judgement
                // survives hardware.
                _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                _hasRunBlend = MF.AnimatorControllerHasParameter(RunBlendParam, _animator);
                for (int i = 0; i < IdleStates.Length; i++)
                {
                    if (!MF.AnimatorPlay(_animator, IdleStates[i]))
                        continue;
                    _state = IdleStates[i];
                    break;
                }

                // ...AND THE ONE LINE OF ActorBehaviour THE STRIP TOOK AWAY, plus the descendant scan
                // and the root-chain pin that the ModBuild 150 report made necessary. Seeded AFTER the
                // idle state has been chosen so the rest pose it records is the one the rig is
                // actually going to be animated away from. See AnimPin.
                AnimPin pin = _go.AddComponent<AnimPin>();
                pin.Seed(_animator);
                _pin = pin;

                // ...and the GAIT, which needs the finished scale and the live controller. See Gait.
                Gait(scale);
            }

            PerfMonitor.Count("HauntFig.Spawns");
            Roster.CensusFigure(_wantModel, _go, _animator, _state, _hasRunBlend, scale);
            Diag.Scene();
            Diag.RendererCensus(_wantModel, _cm);
        }

        // ---- the strip -------------------------------------------------------------------------------

        /// <summary>
        /// Remove everything that could reach the game, run logic, cost frames or catch a pointer.
        /// Run TWICE per figure — once on the root before the mesh child exists, and once after —
        /// because the two halves arrive at different times and each must be disarmed before it is
        /// ever activated.
        ///
        /// <para><b>EVERY MonoBehaviour EXCEPT <c>CharacterManager</c> GOES.</b> An allow-list
        /// rather than a deny-list, and the reason is that the deny-list cannot be written: these
        /// prefabs carry dozens of scripts across a hundred-odd creatures, several are DLC-only, and
        /// a single one that registers itself into a static on <c>Start</c> is a leak or a desync
        /// that nobody would find. <c>FigureOverlay.BuildFrozenGhost</c> (:177-183) already ships
        /// this exact policy for the figure-grab ghost and it has held. <c>CharacterManager</c>
        /// survives because the child instance handle lives on it and releasing that handle is the
        /// only correct way to give the mesh back.</para>
        ///
        /// <para>The named types below are destroyed by name as well, ahead of the sweep, purely so
        /// that this list documents WHICH hazards were known about — <c>UnityGameEditorObject</c>
        /// (pathing + the object cache), <c>Outlinable</c> (a STATIC list the outline pass walks,
        /// Outlinable.cs:264-267), and the three dissolve/appear scripts that would fight the
        /// dissolve this feature drives.</para>
        ///
        /// <para><b>AND THE CREATURE'S OWN LIGHTS GO, WHICH IS A CLASS OF MISS THE MonoBehaviour SWEEP
        /// STRUCTURALLY CANNOT CATCH.</b> <c>UnityEngine.Light</c> derives from <c>Behaviour</c>, NOT
        /// from <c>MonoBehaviour</c>, so the allow-list sweep below walks straight past it — and the
        /// ModBuild 150 census proves two of them survived onto the mod layer: <c>Lightx2</c> in the
        /// component list for 'Living Spirit' (Player.log:6998), with the game's own
        /// <c>RFX4_LightCurves</c> beside them to ANIMATE them. The scene dump taken in the same frame
        /// names one outright (Player.log:6999):</para>
        /// <code>
        ///   'LivingSpirit_Light (1)' Point colour=RGBA(0.298, 0.400, 0.557) intensity=20.00 range=1.0
        /// </code>
        /// <para><b>THAT IS A SECOND CONTRIBUTOR TO THE "viel zu hell" REPORT AND IT IS PROBABLY THE
        /// LARGER ONE.</b> Intensity 20 at a range of 1 m, on a light parented INSIDE the creature, is
        /// a lamp a few centimetres from its own face — and no albedo multiply and no emissive scale
        /// can reach it, because it is not a property of the figure's materials at all. The
        /// chromaticity says the same thing independently: the light is (0.298, 0.400, 0.557), which
        /// normalises to 0.535 : 0.718 : 1.000, and the brightest 0.03% of Figur_hell2.jpg measures
        /// 0.545 : 0.764 : 1.000. Those are the same colour to within 6%.</para>
        ///
        /// <para><b>AND IT BREAKS THIS FEATURE'S CONTRACT OUTRIGHT, which is the reason that does not
        /// depend on any measurement.</b> An apparition is lit BY THE ROOM AND BY NOTHING ELSE — "in
        /// an unlit corner you will barely make one out, and that is deliberate". A live point light
        /// hanging off a figure lights the trunks and the ground around it as well as itself, so the
        /// haunt would be announcing its own position with a lamp. The lights are therefore DISABLED
        /// first and destroyed second: <c>Object.Destroy</c> is deferred to the end of the frame (which
        /// is exactly why this census lists <c>Clothx1</c> and <c>CapsuleColliderx1</c> that <b>are</b>
        /// destroyed here), and <c>enabled = false</c> takes effect on the instant, so there is no
        /// window at all rather than a window this side has argued itself out of.</para>
        ///
        /// <para>NOTE FOR THE NEXT READER on how to read that census line: it is a snapshot taken in
        /// the SAME FRAME as the strip, so every MonoBehaviour in it — <c>Outlinablex1</c>,
        /// <c>DeathDissolvex1</c>, <c>RFX4_LightCurvesx1</c> — is a deferred-destroy artifact and not a
        /// survivor. <c>Lightx2</c> was the one entry that was neither, and it took a second pair of
        /// eyes to see that the artifact argument did not cover it.</para>
        ///
        /// <para><b>PARTICLE SYSTEMS GO TOO</b>, and that is a deliberate loss. Some creatures carry
        /// idle VFX; those particles do not have the <c>_InvisibilityControl</c> property, so they
        /// cannot dissolve with the body and would instead pop in and out around a figure that was
        /// fading. A hard pop is worse than a missing wisp of smoke at 13 m, and it is the exact
        /// tell that gives an apparition away as an effect. This is also why the cast lists in
        /// HauntFigures.Events.cs prefer creatures whose look is geometry rather than FX.</para>
        /// </summary>
        private static void Strip(GameObject root, string phase)
        {
            foreach (UnityGameEditorObject u in root.GetComponentsInChildren<UnityGameEditorObject>(true))
                if (u != null) Object.Destroy(u);
            foreach (Outlinable o in root.GetComponentsInChildren<Outlinable>(true))
                if (o != null) Object.Destroy(o);
            foreach (Cloth c in root.GetComponentsInChildren<Cloth>(true))
                if (c != null) Object.Destroy(c);
            foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
                if (c != null) Object.Destroy(c);
            foreach (Rigidbody r in root.GetComponentsInChildren<Rigidbody>(true))
                if (r != null) Object.Destroy(r);

            // THE CREATURE'S OWN LIGHTS. Light is a Behaviour and not a MonoBehaviour, so the sweep
            // below cannot see it — see the LIGHTS paragraph in this method's doc for the two lights
            // this missed through ModBuild 150 and for why an apparition may never carry one.
            // DISABLED THEN DESTROYED: the disable lands on the instant, the destroy at end of frame.
            foreach (UnityEngine.Light l in root.GetComponentsInChildren<UnityEngine.Light>(true))
            {
                if (l == null)
                    continue;
                l.enabled = false;
                Object.Destroy(l);
            }

            // ...and the two other light-emitting Behaviours in the same blind spot. Neither has been
            // seen on a creature in this roster; they cost one GetComponentsInChildren each and they
            // close the CLASS of miss rather than the one instance of it.
            foreach (LensFlare f in root.GetComponentsInChildren<LensFlare>(true))
            {
                if (f == null)
                    continue;
                f.enabled = false;
                Object.Destroy(f);
            }
            foreach (Projector p in root.GetComponentsInChildren<Projector>(true))
            {
                if (p == null)
                    continue;
                p.enabled = false;
                Object.Destroy(p);
            }

            // The system must go before its renderer (RequireComponent), and it is stopped and
            // cleared first so nothing emits during the frame Destroy is deferred to.
            foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null)
                    continue;
                ps.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);
                Object.Destroy(ps);
            }

            foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || mb is CharacterManager)
                    continue;
                mb.enabled = false;    // stop it ticking before the deferred Destroy lands
                Object.Destroy(mb);
            }

            _ = phase;   // named for readability at the call sites; nothing branches on it
        }

        /// <summary>
        /// Gather the renderers and take ownership of their materials.
        ///
        /// <para>VFX-family renderers are DESTROYED rather than kept — the lesson
        /// <c>FigureOverlay.HasVfxShader</c> (:225-244) records: a distort/particle/fog shader such
        /// as <c>Amp_CharDistort_Low</c> keeps simulating and renders as a mist blob with no
        /// relationship to the body it belongs to. They also have no dissolve control, so they could
        /// not fade with the figure even if they behaved.</para>
        /// </summary>
        private static void Collect(GameObject root)
        {
            if (_dissolveId < 0)
            {
                // The same two properties WallSegmentFade drives on the game's masonry
                // (WallSegmentFade.Mounted.cs:213-214) — this is the game's OWN dissolve, which is
                // why an apparition made of a game monster can materialise without a mod shader.
                _dissolveId = Shader.PropertyToID("_InvisibilityControl");
                _toggleId = Shader.PropertyToID("_Toggle_Dissolve");
                // ...and the two that turned out to be the whole bug. See the class doc.
                _glowId = Shader.PropertyToID("_Glow");
                _opacityId = Shader.PropertyToID("_Opacity");
                // The game's own materialise/dissolve driver — see the field doc for the two
                // decompiled components this name comes from.
                _cutoutId = Shader.PropertyToID("_Cutout");
                _cinderColId = Shader.PropertyToID("_CindersColour");
                _cinderGlowId = Shader.PropertyToID("_CindersGlow");
            }

            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null)
                    continue;
                if (r is ParticleSystemRenderer or TrailRenderer or LineRenderer || IsVfx(r))
                {
                    r.enabled = false;
                    Object.Destroy(r);
                    continue;
                }
                // NO SHADOW, and the reason is now an argument rather than an assumption. All four
                // framings hide the ground the shadow would fall on: the cellar window figure is
                // SUNK behind an opaque black ground plane and seen head-only through a slot; the
                // stair figure stands inside an alcove that ends in a pitch-black cap
                // (BuildEnvironmentRooms.cs:3563-3567) with no light in it to cast by; and both
                // forest events stand past the ground-darkness knee, where the floor is at 0.015 of
                // its lit value (:9297). A real-time shadow map, doubled by MultiPass, would buy four
                // invisible shadows. It is now doubly moot: the figures are multiplied down to
                // roughly an eighth of their albedo or less (HauntFigures.Math.cs, THE DARKENING),
                // so even a lit-ground framing would want a shadow of a creature that is barely
                // brighter than the floor. If a future event ever
                // puts a figure in a candle pool this is the line to revisit, and the room's own moon
                // direction (see Lighting) is already the direction it would have to be cast from.
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                // NOTE: lightProbeUsage is NOT switched to CustomProvided here. It is switched in
                // Lighting.Bind, and only once the room's rig has actually been read — because
                // CustomProvided with no property block yet written means an SH of exactly ZERO,
                // i.e. a figure with no ambient at all. Claiming the channel before there is
                // anything to put in it would turn "lit by the wrong thing" into "black".
                Rends.Add(r);
                foreach (Material m in r.materials)            // ← instantiates; we own them now
                {
                    if (m == null)
                        continue;
                    Mats.Add(m);
                    // The artists' own burn-edge hue, captured before anything here writes. See
                    // _cinderColour.
                    if (ColourPropId(m.shader, "_CindersColour") >= 0)
                        _cinderColour = m.GetColor(_cinderColId);
                    // A DONOR IS A SHADER THAT DECLARES _MOD_TINT, not merely one that bound: Donate
                    // binds that exact property, so a donor picked because it happened to declare
                    // _Color would fail its own verification and revert every time.
                    Bind(m);
                    // ...AND THE SECOND TERM, which no albedo lever can reach. See EmissiveLike for
                    // the measurement that put this line here.
                    BindEmissive(m);
                    if (_donor == null && ColourPropId(m.shader, TintNames[0]) >= 0)
                        _donor = m.shader;
                }
            }

            // ...and only now, with a donor in hand, the materials that could not find a lever of
            // their own. See Donate.
            for (int i = 0; i < Mats.Count; i++)
                Donate(Mats[i], i);

            // ...and only AFTER Donate, because a donated shader is a different property set and the
            // albedo slots must describe the shader the material will actually be drawn with. This is
            // the lever that reaches Amp_Char_Shader; the colour one above does not. See
            // HauntFigures.Albedo.cs.
            for (int i = 0; i < Mats.Count; i++)
                Albedo.Register(Mats[i]);

            Diag.Materials(_wantModel, "BEFORE", -1f);
        }

        // ---- the darkening lever, per material -----------------------------------------------------

        /// <summary>Is this shader property a COLOUR, and what is it called? Returns -1 when the
        /// shader does not declare <paramref name="name"/> at all or declares it as something else —
        /// a texture, a float, a vector.
        ///
        /// <para><b>THIS FUNCTION IS THE ModBuild 150 FIX.</b> <c>Material.HasProperty</c> answers
        /// "does this name exist", NOT "is it a colour", and <c>Amp_Char_Shader_2Side</c> declares
        /// <c>_Diffuse</c> as the albedo TEXTURE. The old binder asked <c>HasProperty</c>, called
        /// <c>GetColor</c>, got an engine error and an all-zero colour back, and then dropped the
        /// material through the near-black guard as if it had been authored black. Three of the High
        /// Cultist's four materials — its skirt and both ribbon sets, i.e. every saturated magenta
        /// pixel in Figur_hell.jpg — went out at full authored brightness for that reason.</para>
        /// </summary>
        private static int ColourPropId(Shader? sh, string name)
        {
            if (sh == null)
                return -1;
            int count = sh.GetPropertyCount();
            for (int p = 0; p < count; p++)
            {
                if (sh.GetPropertyName(p) != name)
                    continue;
                return sh.GetPropertyType(p) == ShaderPropertyType.Color
                           ? Shader.PropertyToID(name)
                           : -1;
            }
            return -1;
        }

        /// <summary>
        /// Find this material's darkening lever and remember what it shipped with. Returns true when
        /// one was found, and always appends exactly one line to <see cref="Verdicts"/> so the census
        /// can say per material what happened.
        ///
        /// <para><b>THREE PASSES, IN THIS ORDER.</b> (1) the named candidates, type-checked; (2) any
        /// Color-typed property whose name reads like an albedo tint and is not on
        /// <see cref="TintNever"/>; (3) nothing, and the material is handed to <see cref="Donate"/>.
        /// The second pass exists because the first is a list of names this project happens to know,
        /// and the shader that broke ModBuild 149 was one nobody had looked at.</para>
        ///
        /// <para><b>A NEAR-BLACK ORIGINAL IS REFUSED AND THE NEXT CANDIDATE IS TRIED</b>, which is the
        /// one guard this needs. The write is <c>original * k</c>, so a property that happens to be
        /// authored black is a lever with no travel: multiplying it changes nothing, and if the
        /// shader really does multiply the albedo by it the model would already be black before this
        /// side touched it — i.e. the property is not what this code thinks it is. Skipping to the
        /// next candidate turns a silent no-op into a working lever on the shaders where one exists,
        /// and into the donor swap where none does.</para>
        /// </summary>
        private static bool Bind(Material m)
        {
            Shader? sh = m.shader;
            string shName = sh != null ? sh.name : "<null>";

            // WHAT THIS SHADER DECLARES, recorded whether or not a lever is found — this is the half
            // of the answer the last round had to guess at.
            var declared = new StringBuilder(96);
            for (int i = 0; i < TintNames.Length; i++)
            {
                int id = ColourPropId(sh, TintNames[i]);
                if (id < 0)
                    continue;
                Color c = m.GetColor(id);
                if (declared.Length > 0)
                    declared.Append(", ");
                // THE ALPHA IS PRINTED, and the fact that it was not is the whole of ModBuild 151's
                // wasted round: the per-renderer property dump carried '(1.000,1.000,1.000,0.000)'
                // for three builds and this summary quoted only the first three components, so the
                // one component that turned out to be the gate was invisible in the line a reader
                // actually reads. See THE ALPHA GATE in Shade().
                declared.Append($"{TintNames[i]}=({c.r:F3},{c.g:F3},{c.b:F3},a={c.a:F3})");
                if (c.r + c.g + c.b < 0.02f)
                {
                    declared.Append(" [near-black, refused]");
                    continue;
                }
                Tints.Add(new TintTarget(m, id, c));
                if (_tintName.Length == 0)
                    _tintName = TintNames[i];
                Verdicts.Add($"'{m.name}' [{shName}] LEVER {TintNames[i]} "
                             + $"= ({c.r:F3},{c.g:F3},{c.b:F3},a={c.a:F3}); declares {declared}"
                             + (id == ModTintId
                                    ? $"; ALPHA WILL BE WRITTEN AS {ModTintAlpha:F3} rather than kept at "
                                      + $"{c.a:F3} — the game's own writer (Choreographer.cs:851-853) "
                                      + "always writes 1 because ColourHTML is a 6-digit string that "
                                      + "defaults to #FFFFFF (MonsterYMLData.cs:79), so an authored 0 is a "
                                      + "state the shipped game never renders. THIS IS THE ModBuild 152 "
                                      + "CHANGE: if the figure is still fully lit after it, the alpha is "
                                      + "not the gate and the next lever is the albedo TEXTURE, not the "
                                      + "RGB again."
                                    : "; alpha kept as authored — on this property the fourth component "
                                      + "is opacity, not a tint weight"));
                return true;
            }

            // PASS 2 — anything Color-typed that reads like an albedo tint.
            int scanned = 0;
            int count = sh != null ? sh.GetPropertyCount() : 0;
            for (int p = 0; p < count; p++)
            {
                if (sh!.GetPropertyType(p) != ShaderPropertyType.Color)
                    continue;
                scanned++;
                string name = sh.GetPropertyName(p);
                string low = name.ToLowerInvariant();
                if (Any(low, TintNever) || !Any(low, TintLike))
                    continue;
                Color c = m.GetColor(name);
                if (c.r + c.g + c.b < 0.02f)
                    continue;
                Tints.Add(new TintTarget(m, Shader.PropertyToID(name), c));
                if (_tintName.Length == 0)
                    _tintName = name;
                Verdicts.Add($"'{m.name}' [{shName}] LEVER {name} (found by scanning the shader's "
                             + $"{scanned} colour propertie(s), not by name) "
                             + $"= ({c.r:F3},{c.g:F3},{c.b:F3},a={c.a:F3}); alpha kept as authored");
                return true;
            }

            Verdicts.Add($"'{m.name}' [{shName}] NO LEVER — of {TintNames.Length} named candidates it "
                         + $"declares {(declared.Length > 0 ? declared.ToString() : "NONE as a colour")}, "
                         + $"and none of its {scanned} colour propertie(s) is an albedo tint. This is "
                         + "the material that was drawn at FULL authored brightness in ModBuild 149.");
            return false;
        }

        /// <summary>
        /// Find every emission-like control this material declares, record what it shipped with, and
        /// append exactly one census line. Called once per material from <see cref="Collect"/>,
        /// strictly before anything on this side has written to it.
        ///
        /// <para>TEXTURES ARE RECORDED BUT NEVER DRIVEN: the lever is the strength that multiplies the
        /// map, not the map itself — swapping an artist's emissive texture for a black one would be a
        /// second way to do the same job and a much harder one to undo. Switches go to 0 and strengths
        /// are scaled; see <see cref="EmissiveGate"/> for why those two are treated differently and
        /// <see cref="EmissiveLike"/> for the measurement that produced the whole block.</para>
        /// </summary>
        private static void BindEmissive(Material m)
        {
            Shader? sh = m.shader;
            if (sh == null)
            {
                EmissiveVerdicts.Add($"'{m.name}' [<null shader>] EMISSION: no shader, nothing readable");
                return;
            }

            var found = new StringBuilder(192);
            int driven = 0;
            int count = sh.GetPropertyCount();
            for (int p = 0; p < count; p++)
            {
                string name = sh.GetPropertyName(p);
                string low = name.ToLowerInvariant();
                if (!Any(low, EmissiveLike))
                    continue;

                if (found.Length > 0)
                    found.Append(", ");

                // The game's own preview glow. Shade() pins it to a hard 0 every time it writes, which
                // is strictly darker than any scale of the authored 10 would be, so it is REPORTED
                // here and driven there — two places writing one property is how a pin gets lost.
                if (name == "_Glow")
                {
                    found.Append($"{name}={m.GetFloat(name):F3} [pinned to 0 by Shade, not scaled here]");
                    continue;
                }

                // ---- THE CINDER TRIO IS EXEMPT, and it has to be or the burn edge goes out --------
                //
                // This loop scales COLOURS and STRENGTHS by the same k, so a term whose brightness is
                // colour x strength comes out as k SQUARED. On every other emissive that is harmless
                // because only one of the pair is authored non-trivially. On the cinders both are:
                // _CindersColour (1.000,0.443,0.051) AND _CindersGlow 2.0, so at the cellar's
                // k x 0.25 = 0.030 the edge would land at 0.0009 of its authored brightness — an
                // invisible dissolve, i.e. the hard cut the user has now reported twice.
                //
                // They are driven from Shade instead, off HauntFigures.CinderGlow, which holds the
                // colour at its authored hue and puts the glow at a share of the room level chosen
                // AGAINST THE FIGURE'S OWN p90 rather than against the room. And they can only ever
                // add anything at all while _Toggle_Dissolve is 1, which is only inside the two short
                // windows at the ends of an apparition. See THE BURN EDGE in HauntFigures.Math.cs.
                if (name is "_CindersColour" or "_CindersColor" or "_CindersGlow" or "_CindersDist"
                         or "_CindersDistance")
                {
                    found.Append($"{name} [cinder — driven by Shade from CinderGlow, exempt from k x "
                                 + $"{EmissiveShare:F2} so the burn edge cannot go as k squared]");
                    continue;
                }

                ShaderPropertyType type = sh.GetPropertyType(p);

                // A RATE IS NOT AN AMOUNT — see EmissiveRate. Reported, never driven. The type is
                // checked before the read for the reason ColourPropId exists: asking a material for a
                // float that is really a texture is an engine error and a zero, and ModBuild 149 lost
                // three of the High Cultist's four materials to exactly that mistake.
                if (Any(low, EmissiveRate))
                {
                    found.Append(type is ShaderPropertyType.Float or ShaderPropertyType.Range
                                     ? $"{name}={m.GetFloat(name):F3} [rate, left as authored]"
                                     : $"{name} [rate, left as authored]");
                    continue;
                }

                bool gate = Any(low, EmissiveGate);
                switch (type)
                {
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                    {
                        float v = m.GetFloat(name);
                        Emissives.Add(new EmissiveTarget(m, Shader.PropertyToID(name),
                                                         isColour: false, isGate: gate, v, default));
                        driven++;
                        found.Append($"{name}={v:F3} ")
                             .Append(gate ? "[SWITCH -> hard 0]" : $"[strength x k x {EmissiveShare:F2}]");
                        break;
                    }

                    case ShaderPropertyType.Color:
                    {
                        Color c = m.GetColor(name);
                        Emissives.Add(new EmissiveTarget(m, Shader.PropertyToID(name),
                                                         isColour: true, isGate: false, 0f, c));
                        driven++;
                        found.Append($"{name}=({c.r:F3},{c.g:F3},{c.b:F3}) [colour x k x {EmissiveShare:F2}]");
                        break;
                    }

                    case ShaderPropertyType.Texture:
                    {
                        Texture? tex = m.GetTexture(name);
                        found.Append($"{name}={(tex != null ? tex.name : "<none>")} [map, deliberately "
                                     + "not driven — the strength beside it is the lever]");
                        break;
                    }

                    default:
                        found.Append($"{name}=? [type not drivable]");
                        break;
                }
            }

            EmissiveVerdicts.Add(found.Length > 0
                ? $"'{m.name}' [{sh.name}] EMISSION: {driven} control(s) driven — {found}"
                : $"'{m.name}' [{sh.name}] EMISSION: this shader declares NO emission-like property at "
                  + "all, so nothing on this material can light itself and its brightness is ENTIRELY "
                  + "the albedo lever. If a creature made only of materials like this one still reads "
                  + "as too bright, emission is not the cause and the light level is.");
        }

        private static bool Any(string haystack, string[] needles)
        {
            for (int i = 0; i < needles.Length; i++)
                if (haystack.Contains(needles[i]))
                    return true;
            return false;
        }

        /// <summary>
        /// THE LAST LEVER: move a material that has none of its own onto a SIBLING shader that has
        /// one.
        ///
        /// <para><b>WHY THIS IS LEGITIMATE AND NOT A HACK.</b> The shader that broke ModBuild 149 is
        /// <c>Amp_Char_Shader_2Side</c> and the one right beside it on the same creature is
        /// <c>Amp_Char_Shader</c>. They are two variants of ONE Amplify graph — the ModBuild 149
        /// material dump prints both property sets side by side (Player.log:4612-4620) and they agree
        /// on every albedo, dissolve, emissive and opacity input; the 2Side variant adds
        /// <c>_2Sided_Invis_Fix</c>, <c>_HighlightStrength</c> and friends and drops
        /// <c>_MOD_TINT</c>. Unity re-binds a material's saved property values BY NAME when the
        /// shader is assigned, so the swap keeps the albedo texture, the cutout and the emissive map
        /// and gains the one property this side needs.</para>
        ///
        /// <para><b>THE NAMING RULE IS THE SAFETY PROPERTY.</b> The swap only happens when the
        /// donor's name is a PREFIX of the material's own shader name — <c>Amp_Char_Shader</c> ⊂
        /// <c>Amp_Char_Shader_2Side</c>. That is what makes "sibling variant of the same graph" a
        /// test rather than a hope, and it is why an unrelated shader can never be moved onto a
        /// character shader by accident.</para>
        ///
        /// <para><b>WHAT IT COSTS, stated honestly.</b> The donor is single-sided, so the skirt and
        /// the ribbons lose their back faces. At the albedo multiplier these figures are drawn with
        /// (0.120 in the cellar, 0.200 in the wood — HauntFigures.Math.cs, re-fitted at ModBuild
        /// 153) a missing back face is a slightly darker dark,
        /// and the alternative is the saturated magenta the user photographed. <c>_Cull</c> is set to
        /// 0 where the donor exposes it, which restores two-sidedness outright on any shader that
        /// declares the pass state as a property; where it does not, the loss stands.</para>
        ///
        /// <para><b>IT IS VERIFIED AND REVERTED.</b> If the material comes out of the swap still
        /// without a colour lever, the original shader is put back and the material is left alone —
        /// a figure with one bright piece is bad, a figure with one INVISIBLE piece is broken.</para>
        /// </summary>
        private static void Donate(Material m, int index)
        {
            if (index >= Verdicts.Count || !Verdicts[index].Contains("NO LEVER"))
                return;
            Shader? donor = _donor;
            Shader? own = m.shader;
            if (donor == null || own == null || donor == own || !own.name.StartsWith(donor.name))
                return;

            m.shader = donor;
            int id = ColourPropId(donor, TintNames[0]);
            if (id < 0)
            {
                m.shader = own;   // it did not take — see THE LAST LEVER
                Verdicts[index] += $" The donor swap to '{donor.name}' was tried and REVERTED: the "
                                   + $"donor does not declare {TintNames[0]} as a colour either.";
                return;
            }

            Color c = m.GetColor(id);
            if (c.r + c.g + c.b < 0.02f)
                c = Color.white;   // the donor's default for an input the 2Side variant never had
            Tints.Add(new TintTarget(m, id, c));
            _donated++;
            if (_tintName.Length == 0)
                _tintName = TintNames[0];

            // Two-sidedness back where the donor exposes the pass state as a property. Guarded by
            // type, for exactly the reason ColourPropId exists.
            int cullCount = donor.GetPropertyCount();
            for (int p = 0; p < cullCount; p++)
            {
                if (donor.GetPropertyName(p) != "_Cull")
                    continue;
                ShaderPropertyType t = donor.GetPropertyType(p);
                if (t is ShaderPropertyType.Float or ShaderPropertyType.Range)
                    m.SetFloat("_Cull", 0f);   // CullMode.Off
                break;
            }

            Verdicts[index] += $" DONOR SWAP: moved from '{own.name}' onto '{donor.name}', which does "
                               + $"declare {TintNames[0]}, so it is darkened with everything else. It "
                               + "loses back-face rendering unless the donor exposes _Cull.";
        }

        private static bool IsVfx(Renderer r)
        {
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                Shader? s = mats[i] != null ? mats[i].shader : null;
                if (s == null)
                    continue;
                if (s.name.Contains("Distort") || s.name.Contains("Particle")
                    || s.name.Contains("Fog") || s.name.Contains("FX"))
                    return true;
            }
            return false;
        }

        // ---- shading ---------------------------------------------------------------------------------

        /// <summary>
        /// Put the figure at the brightness the moment calls for: the event's envelope times the
        /// light the room is actually giving it. ONE scalar, two jobs, and they are the same job.
        ///
        /// <para><b>THE TWO USER REPORTS THIS ANSWERS, verbatim.</b> (a) "Die Figuren sind VOLL
        /// sichtbar in der schwarzen Nacht als wären sie voll angestrahlt. Das soll nicht sein. Sie
        /// MÜSSEN an die Lichtverhältnisse angeglichen werden, sonst geht der Gruselfaktor
        /// verloren." (b) "Die Figuren brauchen irgendwie eine längere Zeit bis ihre texturen richtig
        /// geladen haben ... viele schwarze flecken in ihnen". The class doc argues at length that
        /// (b) was this file's own dissolve and that its emissive burn edge was half of (a). Both are
        /// answered by deleting the dissolve and multiplying the albedo instead.</para>
        ///
        /// <para><b>WHY THE ENVELOPE AND THE LIGHT LEVEL ARE ONE NUMBER.</b> "How present is it" and
        /// "how bright may it be" are the same question for a creature whose whole business is being
        /// in the dark: an apparition at half presence in a cellar lit to a tenth is a creature at a
        /// twentieth of its albedo, and there is no second thing for the two to disagree about. So
        /// they multiply, they are written as one colour per material, and the early-out compares
        /// the product rather than either factor — a figure walking past a candle re-shades even
        /// though its envelope is flat, and a watcher standing still under an unchanging rig does
        /// not re-shade for eight and a half seconds.</para>
        ///
        /// <para><b>PRESENCE 0 SWITCHES THE RENDERERS OFF RATHER THAN WRITING BLACK, and that is not
        /// a micro-optimisation.</b> A tint of zero is an OPAQUE BLACK BODY: at the cellar window it
        /// would punch a person-shaped hole in the star patch behind the opening
        /// (<c>AddNightOutsideWindow</c>), and in the wood it would blot out the trunks behind the
        /// watcher. "Gone" has to mean not drawn. It is also what makes a looping test trigger's
        /// restart invisible — the walk is repositioned during frames on which nothing is on
        /// screen (see <see cref="HauntFigures.TickBody"/>).</para>
        ///
        /// <para><b>THE GAME'S OWN VISIBILITY TRIPLE IS PINNED HERE, EVERY TIME</b>, to the
        /// non-preview values <c>InitialiseCharacterStepTwo</c> would have written for a real monster
        /// (<c>_Glow = 0</c>, <c>_Opacity = 1</c>, CharacterManager.cs:244-245 with
        /// <c>isPreview: false</c>) plus <c>_InvisibilityControl = 0</c> and
        /// <c>_Toggle_Dissolve = 0</c> — which is now a HARD OFF for the dissolve rather than a
        /// value that moves. Re-asserted rather than written once: a <c>StateMachineBehaviour</c>
        /// cannot be stripped (class doc), two of the game's own SMBs write <c>_Opacity</c>
        /// (FireEquippedProjectileSMB.cs:177, Jump_OutOf_SMB.cs:37), and <c>isPreview: true</c> is
        /// what sets the preview triple in the first place. Four floats per material on frames the
        /// scalar moves at all.</para>
        ///
        /// <para>The game's <c>RefreshVisibility</c> is still NOT called: it reads
        /// <c>renderer.materials</c> on every call and that getter allocates a fresh
        /// <c>Material[]</c> each time — a few thousand needless allocations per apparition at
        /// 90 Hz.</para>
        ///
        /// <para><b>THE FALLBACK, if no material declares any tint property at all:</b> the figure is
        /// switched on at the halfway point of its own envelope and off again. Visibly cruder, and
        /// still better than an apparition that cannot appear — and <see cref="Diag.Materials"/>
        /// prints every property each shader declares, so the next round can name the right one
        /// instead of guessing again.</para>
        /// </summary>
        private static void Shade(float presence, float cutout)
        {
            presence = Mathf.Clamp01(presence);
            cutout = Mathf.Clamp01(cutout);

            // THE RAMP IS SHAPED FOR THE EYE AND NOT FOR THE FRAMEBUFFER. See PerceptualGamma.
            float shown = presence >= 1f ? 1f : Mathf.Pow(presence, PerceptualGamma);

            // THE ROOM'S LEVEL, measured where the figure stands (Lighting.Apply ran first this
            // frame). With no rig readable this is a fixed dim constant rather than 1: a figure the
            // room cannot be measured for is still a figure in the dark.
            float k = shown * Lighting.Level;

            // ---- THE LEVER THAT ACTUALLY REACHES A PIXEL ------------------------------------------
            //
            // Latched on the QUANTISED level inside Apply, so the steady state is one integer compare
            // per frame and the GPU work happens once per (texture, room level) — which for a
            // creature whose four materials share one _Diffuse is once per apparition. It is called
            // BEFORE the early-out below for the same reason the census dump is: the early-out is a
            // scalar test on a value that stops moving, and a lever gated behind it would never fire
            // for a figure that reaches its hold and stays there.
            Albedo.Apply(Lighting.Level);

            // ...AND ONLY NOW MAY THE LIGHT-LEVEL LINE BE PRINTED. It is composed inside
            // Lighting.Apply, which runs earlier in the frame than this statement, so a line printed
            // there could only ever describe the DESIGN — and in ModBuild 153 it described a
            // multiply that had not happened. Printed here it carries Albedo.Outcome, which is a
            // measured fact. See Lighting._levelText.
            Lighting.LogLevel();

            // THE DUMP IS TAKEN BEFORE THE EARLY-OUT, and that is not where it looks like it
            // belongs. It used to sit at the bottom of this method, where it could be missed
            // entirely: the last write of a rising envelope happens when the scalar stops moving by
            // the threshold below, which for a cellar-dark figure is around presence 0.985 — so a
            // dump gated on presence >= 0.995 inside the written path would never have run for the
            // rooms this feature is actually for. Diag.Materials is latched to once per creature per
            // process by its own HashSet, so calling it every frame of the hold costs one failed
            // Add.
            if (presence >= 0.995f)
                Diag.Materials(_wantModel, "AFTER", k);

            // THE SWITCH-ON POINT AND THE WRITE STEP ARE FRACTIONS OF THE ROOM'S OWN LEVEL, not the
            // absolute 0.0015 they were through ModBuild 150 — and that constant was a real
            // interaction between this round's two halves. The darker a room is made, the smaller the
            // whole ramp becomes: at the cellar's Level of 0.015 an absolute 0.0015 threshold is a
            // TENTH of everything the envelope can express, and an absolute 0.0015 write step resolved
            // the entire appearance in ten visible stairs. Both now scale with the room, so the wood
            // and the cellar get the same number of steps and the same fraction of ramp spent below
            // the switch — which is what makes the entrance read the same in both.
            float on = Mathf.Max(Lighting.Level * OnFraction, 1e-5f);
            float step = Mathf.Max(Lighting.Level * StepFraction, 1e-6f);

            // GONE IS GONE — except on a creature with no tint lever at all, where the only
            // visibility this side has is the renderer switch and the honest place to throw it is
            // the middle of the envelope. See THE FALLBACK in the doc.
            bool visible = Tints.Count > 0 ? k > on : presence >= 0.5f;

            // Both tests are needed: the state test catches the frame the figure becomes visible or
            // invisible, the scalar test skips everything else. THE CUTOUT IS THE THIRD TERM and it
            // is not decoration: the brightness envelope and the dissolve window start together but
            // an event with its own long authored fade can hold `k` still while the dissolve sweep is
            // running, and a skipped frame there is a dissolve that jumps.
            if (Mathf.Abs(k - _lastShade) < step && (_lastShade > on) == visible
                && Mathf.Abs(cutout - _lastCutout) < 0.002f)
                return;
            _lastShade = k;
            _lastCutout = cutout;

            for (int i = 0; i < Rends.Count; i++)
                if (Rends[i] != null)
                    Rends[i].enabled = visible;

            if (!visible)
                return;

            for (int i = 0; i < Mats.Count; i++)
            {
                Material m = Mats[i];
                if (m == null)
                    continue;
                if (m.HasProperty(_glowId))
                    m.SetFloat(_glowId, 0f);
                if (m.HasProperty(_opacityId))
                    m.SetFloat(_opacityId, 1f);
                if (m.HasProperty(_dissolveId))
                    m.SetFloat(_dissolveId, 0f);

                // ---- THE MATERIALISE AND THE DISSOLVE, and it is the game's own -------------------
                //
                // ModBuild 152 pinned _Toggle_Dissolve to a hard 0 here, on the correct observation
                // that its emissive burn edge was half of the "voll angestrahlt" report. The user has
                // since asked for exactly that effect back — "ich fänd es cooler wenn sich die figur
                // eher in partikel auflösen bzw materialisieren würde (nur ganz kurz)" — so the pin
                // becomes a WINDOW instead: the toggle is 1 only while the cutout is doing something,
                // which is the first and last ~0.35 s of a run, and 0 for everything between. The
                // steady state is therefore bit-identical to ModBuild 152's, which is what keeps
                // this from re-opening the complaint it is built on top of.
                //
                // _Cutout AND NOT _DeathDissolvePos: both of the game's runtime drivers write this
                // property and neither writes that one. See the _cutoutId field doc.
                bool dissolving = cutout > 1e-4f;
                if (m.HasProperty(_toggleId))
                    m.SetFloat(_toggleId, dissolving ? 1f : 0f);
                if (m.HasProperty(_cutoutId))
                    m.SetFloat(_cutoutId, cutout);

                // ---- THE BURN EDGE, held under the figure it is eating ---------------------------
                //
                // _CindersGlow is ADDED after lighting, so nothing on the albedo side can reach it
                // and its authored 2.0 in a room this dark is a bright orange band — the "voll
                // angestrahlt" complaint arriving through a new door. It rides the room level at
                // CinderShare, which puts the wood's edge at about twice the figure's own brightest
                // pixel and the cellar's at 0.60x of the wood's from the same one constant. The
                // COLOUR keeps its authored hue: scaling colour and strength together is what would
                // make the edge go as the square of the level and vanish. See THE BURN EDGE in
                // HauntFigures.Math.cs for the arithmetic and the number chosen.
                if (m.HasProperty(_cinderGlowId))
                    m.SetFloat(_cinderGlowId, dissolving ? CinderGlow(Lighting.Level) : 0f);
                if (m.HasProperty(_cinderColId))
                    m.SetColor(_cinderColId, _cinderColour);
            }

            Vector3 w = Lighting.Chroma;
            for (int i = 0; i < Tints.Count; i++)
            {
                TintTarget t = Tints[i];
                if (t.Mat == null)
                    continue;
                Color c = t.Original;

                // ---- THE DESATURATION, and it is the half of "an die Lichtverhältnisse
                // angeglichen" a multiply cannot express ------------------------------------------
                //
                // A UNIFORM MULTIPLY PRESERVES SATURATION. That is arithmetic: scaling all three
                // channels by k leaves (max-min)/max exactly where it was, so a saturated colour
                // comes out of the darkening exactly as saturated as it went in, and the eye finds a
                // saturated patch in a dark surround far more easily than a neutral one of the same
                // luminance. Figur_hell.jpg is that failure photographed: linearising it and
                // splitting the figure by saturation gives 9057 magenta pixels and 15831 neutral
                // ones at the SAME mean luminance (0.0190 against 0.0184) — and it is only the
                // magenta the user's sentence is about. Between ModBuild 148 and 149 the figure's
                // luminance ratio against the trunk behind it fell from 16.0 to 5.8 while the mean
                // saturation of everything still visible ROSE from 0.253 to 0.437. The darkening
                // worked and made the problem more colourful.
                //
                // WHAT AN OBJECT SEEN BY ALMOST NO LIGHT ACTUALLY LOOKS LIKE is nearly colourless,
                // twice over: the light itself is one narrow colour (a moon, a candle) so every
                // surface reflects that colour rather than its own, and below about 0.01 cd/m^2 the
                // eye is running on rods, which have no colour channel at all. So the albedo is
                // pulled toward its OWN Rec.709 luma times the ROOM'S chromaticity — the figure
                // keeps its light and dark, loses its hue, and what hue survives is the room's.
                //
                // LUMINANCE IS PRESERVED BY CONSTRUCTION, which is why this is a separate lever from
                // Level and not a second, hidden darkening: luma(lerp(c, luma(c)*w, s)) = luma(c)
                // for any s, because luma(w) is 1 by definition (see Lighting.Chroma).
                float y = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                float s = Desaturation;
                float r = Mathf.Lerp(c.r, y * w.x, s);
                float g = Mathf.Lerp(c.g, y * w.y, s);
                float b = Mathf.Lerp(c.b, y * w.z, s);

                // ---- THE ALPHA GATE — and it is why FOUR ROUNDS OF DARKENING CHANGED NOTHING ------
                //
                // The line this replaces read "ALPHA IS NOT TOUCHED ... on every one of the candidate
                // properties it means opacity or nothing at all". That sentence was wrong about the
                // one property that matters, and the ModBuild 151 log had the counter-evidence in it
                // all along (Player.log:7681, the Cultist AFTER dump, live off the rendering
                // material):
                //
                //     _MOD_TINT=(0.012,0.014,0.020,0.000)      <- and the figure rendered FULLY LIT
                //
                // An RGB of 0.012/0.014/0.020 is black. A figure carrying a black albedo multiply and
                // reading as a normal, fully-textured character is not a figure that was darkened too
                // little; it is a figure that was not darkened AT ALL. So the write lands, the value
                // is live at dump time, and the shader ignores it — which leaves the fourth component
                // as the only thing in the tuple nobody had read.
                //
                // WHAT THE GAME ITSELF WRITES, from the decompiled source rather than from inference:
                //   * Choreographer.cs:851-853 (and again at :1034-1036) — on EVERY character whose
                //     shader is named Amp_Char_Shader:
                //         if (mat.HasProperty("_MOD_TINT") && ColorUtility.TryParseHtmlString(s, out c))
                //             mat.SetColor("_MOD_TINT", c);
                //   * ColorUtility.TryParseHtmlString returns ALPHA = 1 for a 6-digit "#RRGGBB".
                //   * and the string is never absent: MonsterYMLData.cs:79 initialises
                //     ColourHTML = "#FFFFFF" for every monster, MonstersYML.cs:282 builds it as
                //     "#" + a six-digit value, and CharacterYMLData.cs:113 does the same for heroes.
                // THEREFORE: every character the shipped game draws on this shader carries
                // _MOD_TINT.a = 1, and the (1,1,1,0) this file captured is the MATERIAL ASSET'S
                // AUTHORING DEFAULT — a state the game itself never renders. We copied that 0
                // forward on every write and have been driving a colour through a gate we were
                // holding shut.
                //
                // THE PHOTOGRAPHS AGREE, AND THAT IS THE INDEPENDENT HALF OF THE ARGUMENT. Three
                // frames of the SAME forest event were measured across three builds whose nominal
                // multiplier differed by a factor of 4.1 (Level 0.321 -> 0.078):
                //     Sichtbarkeit.jpg (Level 0.321): figure p90 0.0290, trunk 0.0042
                //     Figur_hell.jpg   (Level 0.078): figure p90 0.0291, trunk 0.0050
                // The multiplier fell fourfold and the figure moved by 0.3%. A lever whose output
                // does not respond to its input is inert, and the user said so in one sentence a
                // build before the measurement did: "ich sehe keinen Unterschied".
                //
                // AND IT CANNOT MAKE THE FIGURE SEE-THROUGH, which is the one way this change could
                // do harm — a translucent monster is the "leuchtende Silhouette" ModBuild 150
                // removed. If this component were an OPACITY, the figure would already be invisible:
                // it is carrying 0 today and it renders solid. Raising an opacity from 0 to 1 is the
                // direction that makes something MORE opaque in any case, so both readings are safe.
                //
                // SO THE ALPHA IS WRITTEN, AND ONLY ON _MOD_TINT. On _Color and its family the fourth
                // component really is opacity and forcing it to 1 could make a legitimately
                // translucent material solid, so those keep the alpha they shipped with. On
                // _MOD_TINT the game's own writer IS the specification and it always writes 1.
                //
                // THE RESULT IS CORRECT UNDER BOTH READINGS OF WHAT THE ALPHA DOES, which matters
                // because the Amplify graph is not readable from here:
                //   * a GATED MULTIPLY, albedo * lerp(1, tint, a): at a = 1 the figure is
                //     albedo x (original x k), i.e. the darkening this file has always intended;
                //   * a BLEND TO A FLAT COLOUR, lerp(albedo, tint, a): at a = 1 the figure becomes
                //     the flat colour original x k — a SILHOUETTE, a shape with no readable texture,
                //     which is the thing the user has now asked for five times.
                // Both are LINEAR IN k, so the reveal and the dissolve fade correctly either way.
                float a = t.IsModTint ? ModTintAlpha : c.a;

                // ---- WHICH SCALAR THIS COLOUR CARRIES, and it is a two-lever interlock ------------
                //
                // The room LEVEL is now multiplied into the albedo TEXTURE for every material that
                // has one (HauntFigures.Albedo.cs). Writing it here as well would darken those
                // materials by the SQUARE of the level — 0.0144 in the cellar instead of 0.120 — and
                // "ich finde sie gar nicht" is the one failure this round is most exposed to, because
                // it is the first build in which a darkening lever does anything at all.
                //
                // So the two levers split cleanly and by material: where the texture carries the
                // level, this colour carries the ENVELOPE alone; where no albedo texture was found
                // (Albedo's verdict line names those materials), it carries level x envelope exactly
                // as ModBuild 152 did. On Amp_Char_Shader the colour is inert either way, and this
                // branch costs one hash lookup per material per write; it exists so that a creature
                // on a shader that DOES honour a tint comes out right rather than black.
                float scalar = Albedo.Wears(t.Mat) ? shown : k;
                t.Mat.SetColor(t.Prop, new Color(r * scalar, g * scalar, b * scalar, a));
            }

            // ---- THE EMISSION, driven by the SAME scalar the albedo is -----------------------------
            //
            // This is the loop that was missing, and its absence is the whole of Figur_hell2.jpg: the
            // treeline watcher's six materials all ship _UseEmissiveMap = 1 with an _EmissiveMapBoost
            // of 2.0, emission is ADDED after the albedo, and nothing above this line can reach it.
            // Switches go hard off; strengths and colours ride k times EmissiveShare so an emissive
            // texel lands at the brightness of a mid-grey albedo texel in the same room. See
            // EmissiveLike, EmissiveGate and EmissiveShare — the argument, the honesty about what
            // cannot be read, and the arithmetic are one block each.
            float kEm = k * EmissiveShare;
            for (int i = 0; i < Emissives.Count; i++)
            {
                EmissiveTarget e = Emissives[i];
                if (e.Mat == null)
                    continue;
                if (e.IsGate)
                    e.Mat.SetFloat(e.Prop, 0f);
                else if (e.IsColour)
                    e.Mat.SetColor(e.Prop, new Color(e.OrigC.r * kEm, e.OrigC.g * kEm,
                                                     e.OrigC.b * kEm, e.OrigC.a));
                else
                    e.Mat.SetFloat(e.Prop, e.OrigF * kEm);
            }
        }

        /// <summary>
        /// THE EXPONENT THAT MAKES THE APPEARANCE A FADE INSTEAD OF A POP, and it is the one number
        /// that answers "das Auftauchen geschieht abrupt von einem Frame auf den anderen".
        ///
        /// <para><b>THE ENVELOPE WAS ALREADY A SMOOTHSTEP, SO WHY DID IT POP.</b> Because the envelope
        /// was spent in LINEAR LIGHT and the eye does not work in linear light. The albedo multiplier
        /// is proportional to presence, so radiance is proportional to presence; perceived lightness
        /// goes roughly as the 1/2.2 power of radiance. A presence of 0.10 — reached 12% of the way
        /// into the reveal — therefore already LOOKS like 0.10^(1/2.2) = 0.36 of the final figure. A
        /// third of the appearance happens in the first eighth of the time it was given, and the
        /// remaining seven eighths carry a change the eye can barely find. That is a pop with a long
        /// tail, and it is exactly what a photograph of a rising envelope would show.</para>
        ///
        /// <para><b>2.2 INVERTS IT EXACTLY.</b> Raising presence to 2.2 before it multiplies the albedo
        /// makes perceived lightness proportional to presence itself, so the smoothstep the envelope
        /// was always shaped like is the curve the player actually sees: slow at both ends, quickest in
        /// the middle, and no part of the ramp wasted. It costs one <c>Mathf.Pow</c> on frames the
        /// scalar moves, it cannot be mistaken for a rendering fault (the whole figure changes
        /// brightness together — no holes, no noise field, no per-texel anything), and it is
        /// deliberately NOT the ModBuild 148 dissolve, which the class doc records as the effect the
        /// user read as half-loaded textures.</para>
        ///
        /// <para>The exact-1 short circuit is not a micro-optimisation: <c>Mathf.Pow(1, 2.2)</c> is
        /// 0.99999994 on some runtimes and the full-presence dump is gated on 0.995, so a figure that
        /// never quite reaches 1 would never quite reach its own light level.</para>
        /// </summary>
        private const float PerceptualGamma = 2.2f;

        /// <summary>The fraction of the ROOM'S OWN LEVEL at which the renderers are switched on. At the
        /// cellar's Level of 0.015 this is a k of 6e-5, i.e. a body pixel at 3e-5 — three orders below
        /// anything a headset resolves, so nothing is ever seen to appear at a step. It is a fraction
        /// rather than the absolute constant it replaced so that a darker room does not silently spend
        /// a larger share of its ramp below the switch. See the block in <see cref="Shade"/>.</summary>
        private const float OnFraction = 0.004f;

        /// <summary>The fraction of the room's level by which the combined scalar must move before the
        /// materials are re-written. 0.01 gives about a hundred steps across the whole ramp in EVERY
        /// room — where the absolute constant it replaced gave ten in the cellar and thirty-two in the
        /// wood. At 90 Hz over a 2.1 s reveal that is a write on roughly half the frames, each one a
        /// few dozen <c>SetFloat</c>s on one skinned figure.</summary>
        private const float StepFraction = 0.01f;

        /// <summary>
        /// How far toward colourless the albedo is pulled before it is darkened, 0..1. See THE
        /// DESATURATION in <see cref="Shade"/> for the argument and for the photograph it is fitted
        /// to.
        ///
        /// <para><b>0.85 AND NOT 1.0, deliberately.</b> A full collapse to luma would make every
        /// creature in the roster the same grey-blue, and the last 15% of hue is what still tells a
        /// Living Corpse from a High Cultist when a player is looking straight at one — which is the
        /// state the whole feature is FOR. On the magenta that owns Figur_hell.jpg (roughly
        /// (0.80,0.10,0.60), saturation 0.88) it lands the result at saturation 0.16 without moving
        /// its luminance at all.</para>
        /// </summary>
        private const float Desaturation = 0.85f;

        /// <summary>
        /// The fourth component written into <c>_MOD_TINT</c>, and the one number this whole round
        /// turns on. See THE ALPHA GATE in <see cref="Shade"/> for the evidence.
        ///
        /// <para><b>1.0 IS NOT A GUESS, IT IS WHAT THE GAME WRITES.</b>
        /// <c>ColorUtility.TryParseHtmlString</c> on a 6-digit <c>#RRGGBB</c> returns alpha 1, the
        /// string is never absent (<c>MonsterYMLData.cs:79</c> defaults it to <c>#FFFFFF</c>), and
        /// <c>Choreographer.cs:851-853</c> writes the result onto every Amp_Char_Shader character in
        /// the game. So alpha 1 is the shader's designed operating point and alpha 0 — the material
        /// ASSET's authoring default, which this file was faithfully preserving — is a state the
        /// shipped game never renders.</para>
        ///
        /// <para><b>IF THE NEXT HARDWARE ROUND SHOWS NO CHANGE, THIS IS THE SWEEP.</b> The AFTER dump
        /// now prints the alpha explicitly, so the first question is whether the value arrived
        /// (<c>_MOD_TINT=(...,1.000)</c>). If it did and the figure is still fully lit, then the
        /// alpha is not the gate, the tint is inert for some other reason, and the answer is NOT to
        /// move the RGB again — it is to change lever: the albedo TEXTURE <c>_Diffuse</c> is declared
        /// by both character shaders and replacing it with a 1x1 dark texture would darken the figure
        /// whatever the graph does with its colours. That was deliberately not shipped this round
        /// because it also destroys the texture's alpha, which the fur and ribbon cards cut out
        /// against (<c>_Cutoff = 0.5</c>), i.e. it can only be judged with a photograph in hand.</para>
        /// </summary>
        private const float ModTintAlpha = 1f;

        // ---- teardown ---------------------------------------------------------------------------------

        /// <summary>
        /// Give the apparition back. Complete, symmetric and in the one order that does not leak or
        /// crash.
        ///
        /// <para><b><c>DeinitializeCharacter</c> IS DELIBERATELY NOT CALLED</b>, which is a
        /// correction to the obvious recipe and worth stating plainly: its first act is
        /// <c>WorldspaceUITools.Instance.DeregisterActorOutlinable(CharacterActor)</c>
        /// (CharacterManager.cs:251), <c>CharacterActor</c> is null on a decorative clone, and that
        /// method is <c>m_ActorOutlinables.Remove(actor)</c> on a Dictionary
        /// (WorldspaceUITools.cs:227-230) — a null key, i.e. an <c>ArgumentNullException</c>, every
        /// single time, in any live scenario. What that method is actually NEEDED for is one thing,
        /// releasing the child instance handle, and that handle is a PUBLIC field, so it is released
        /// here directly through the same public helper the game uses.</para>
        ///
        /// <para><b><c>UnloadAsyncCharacterPrefabFromBundle</c> IS ALSO NOT CALLED</b>, for a
        /// sharper reason: it releases <c>_instantiateHandles[path]</c> (AssetBundleManager.cs:305,
        /// :160-169), which is the map the game's OWN async instantiates go into — this side never
        /// adds to it, so the call would do nothing except destroy live instances belonging to the
        /// scenario if a real monster of the same model happened to be on the board.</para>
        /// </summary>
        internal static void Release(string why)
        {
            if (_go == null && !_spawned && _init == null)
            {
                _wantModel = string.Empty;
                _wantParent = null;
                _wantRoom = null;
                return;
            }

            // The one dump that would otherwise never be written: an event cut short before its
            // envelope ever reached 1 (a stand-down mid-reveal, a style change). Says so in the
            // line, so a reader is never misled into reading a mid-dissolve state as the final one.
            Diag.Materials(_wantModel, "AFTER (cut short before full presence)", _lastShade);

            // THE ANSWER TO "DOES THE CLIP MOVE THE FIGURE", once per process, either way. A
            // hardware round can settle the ModBuild 148 teleport report off this one line: a
            // non-zero drift means the game's clips really do carry travel that `applyRootMotion =
            // false` did not stop and AnimPin now cancels; a zero means they do not, and the
            // remaining suspect is the GAIT — the path speed against the walk clip's own, both of
            // which are printed on the `armed at shared clock` and CENSUS lines.
            if (_pin != null && _wantModel.Length > 0 && DriftLogged.Add(_wantModel))
            {
                float drift = _pin.MaxDrift;
                VRLog.Info("Core", $"HAUNT FIGURES animation drift for '{_wantModel}' (once per creature "
                    + $"per process; it {(_gaitSpeed > 0.01f ? $"WALKED at {_gaitSpeed:F2} m/s" : "STOOD STILL")} "
                    + "— which matters, because through ModBuild 150 this line was latched once per "
                    + "PROCESS and the first apparition of that session was the STANDING treeline "
                    + "watcher, so the one measurement the walkers needed was spent on a creature that "
                    + "never takes a step).\n"
                    + $"  ON THE ANIMATOR'S OWN TRANSFORM: at most {drift * 1000f:F2} mm before the pin "
                    + "cancelled it. A zero here does NOT mean the clips carry no travel — "
                    + "'applyRootMotion = false' only stops Unity EXTRACTING translation onto the "
                    + "GameObject; on a Generic rig the travel stays in the bone curves, which is what "
                    + "the next line measures and what the old instrument could not see.\n"
                    + $"  IN THE BONE CURVES: {_pin.Report()}");
            }
            _pin = null;

            Lighting.Forget();

            // Materials first: they are instances this side created and Unity will not collect them
            // with the objects that reference them.
            Tints.Clear();          // holds Material references — cleared BEFORE they are destroyed
            Emissives.Clear();      // ...and so does this one, for the same reason
            // ...and this one, which holds them too. The DARKENED COPIES deliberately survive: they
            // are keyed by (source texture, level) and a creature that appears a second time in the
            // same room must not re-blit. They are destroyed in Shutdown, which is the teardown that
            // ends the feature rather than the apparition.
            Albedo.Forget();
            for (int i = 0; i < Mats.Count; i++)
                if (Mats[i] != null)
                    Object.Destroy(Mats[i]);
            Mats.Clear();
            Rends.Clear();

            // The child, through the public handle. releaseInstance: true is what actually destroys
            // it — Addressables was told trackHandle: false (CharacterManager.cs:172), so nothing
            // else ever will.
            if (_cm != null && _cm.CharacterChildPrefabInstance.IsValid())
            {
                AssetBundleManager.ReleaseHandle(_cm.CharacterChildPrefabInstance, releaseInstance: true);
                _cm.CharacterChildPrefabInstance = default;
            }

            if (_go != null)
                Object.Destroy(_go);

            _go = null;
            _cm = null;
            _init = null;
            _animator = null;
            _state = string.Empty;
            _hasRunBlend = false;
            _spawned = false;
            _lastShade = -1f;
            _lastCutout = -1f;
            _cinderColour = new Color(1f, 0.443f, 0.051f, 1f);
            _tintName = string.Empty;
            Verdicts.Clear();
            EmissiveVerdicts.Clear();
            _donated = 0;
            _gaitSpeed = 0f;
            _gaitBlend = -1f;
            _voiced = false;
            _wantModel = string.Empty;
            _wantParent = null;
            _wantRoom = null;
            _ = why;
        }

        /// <summary>Full teardown: the resident prefab AND the inactive nursery, which is the one
        /// object this feature keeps alive across scenes (<c>DontDestroyOnLoad</c>, because a
        /// nursery that a scene load destroyed would take the half-built figure inside it with
        /// it).</summary>
        internal static void Shutdown(string why)
        {
            Release(why);
            ReleasePrefab(why);
            // Every RenderTexture this feature ever created, released and destroyed. Release() above
            // deliberately does NOT do this — see the note there.
            Albedo.Shutdown();
            if (_nursery != null)
            {
                Object.Destroy(_nursery);
                _nursery = null;
            }
        }

        /// <summary>Release the resident prefab ASSET handle. Separate from <see cref="Release"/>
        /// because the asset outlives an apparition on purpose — a second event using the same
        /// creature must not pay for the bundle twice — but it must not outlive the session.</summary>
        internal static void ReleasePrefab(string why)
        {
            if (!_assetValid)
                return;
            _assetValid = false;
            if (_asset.IsValid())
                Addressables.Release(_asset);
            _asset = default;
            VRLog.Info("Core", $"HAUNT FIGURES released the resident enemy prefab '{_assetModel}' — {why}.");
            _assetModel = string.Empty;
        }

        /// <summary>One log line, and after enough of them the feature hands the room back to the
        /// shader.</summary>
        private static void Fail(string why)
        {
            _fails++;
            Release("the build failed");
            // The retry is per ATTEMPT and not per slot: the driver re-arms on the next frame while
            // the slot's window is still open, so a genuine fault converges on the stand-down below
            // within a few frames rather than over a few minutes. That is deliberate — an asset that
            // did not load will not load a frame later either, and a room that silently skips every
            // apparition is worse than one that hands itself back to the shader.
            VRLog.Warn("Core", $"HAUNT FIGURES: no apparition this time — {why}. This attempt is simply "
                               + $"quiet; the schedule is untouched and the next one tries again ({_fails} "
                               + $"of {MaxFails} consecutive failures).");
            if (_fails >= MaxFails)
                Disable($"{MaxFails} apparitions in a row could not be built — the last said: {why}");
        }

        // =========================================================================================
        //  THE ROOM'S LIGHT, ON THE FIGURE
        // =========================================================================================

        /// <summary>
        /// Make the apparition stand in the same light as the wall behind it.
        ///
        /// <para><b>USER REPORT, verbatim</b> (the second half of the one that produced this whole
        /// round): "Ich will das die Figur selber mit allen Texturen etc angezeigt wird ABER sich
        /// entsprechend in die Lichtverhältnisse einfügt." — the figure itself, with its textures,
        /// but FITTING INTO THE LIGHTING CONDITIONS.</para>
        ///
        /// <para><b>THE PROBLEM, STATED EXACTLY.</b> The environment rooms contain NO Unity lights
        /// at all. They are lit entirely by their own shaders from a baked rig that
        /// <c>BuildEnvironmentRooms.ApplyRig</c> (:419-511) writes per material: hemisphere ambient
        /// (<c>_AmbUp</c>/<c>_AmbDown</c>), one directional — the moon — (<c>_DirDir</c>/
        /// <c>_DirCol</c>) and up to three points — the candles — (<c>_L0Pos</c>/<c>_L0Col</c> …),
        /// plus <c>_PtHard</c>. A game monster dropped into that room is lit by whatever the GAME
        /// scene happens to provide, which has no relationship to the moon coming through the
        /// cellar window. It would read as pasted on even once it is solid, and the direction is
        /// what gives that away long before the level does: a figure lit from the wrong side is
        /// instantly fake, a figure that is merely a little too bright is only a little wrong.</para>
        ///
        /// <para><b>THE MECHANISM: PER-RENDERER SPHERICAL HARMONICS, NOT REAL LIGHTS.</b> The
        /// obvious answer — build real <c>Light</c> components from the rig and aim them at the
        /// figure alone with <c>Light.cullingMask</c> — was costed first and is REJECTED, on a fact
        /// about this mod rather than on principle: <see cref="VRLayers"/> owns exactly ONE mod
        /// layer, by design, and everything the mod draws is on it (hands, lasers, cards, panels,
        /// the tray, the vignette, AND the environment room itself — SkyAlternative.cs:1158). A
        /// culling mask can therefore not separate the apparition from the rest of the mod's
        /// visuals, so a "moon" aimed at the figure would also rake the player's own hands. Taking
        /// a second layer would mean the VR head camera had to render it, and that camera's mask is
        /// owned by <c>VRRigDriver</c> under docs/CAMERA-POLICY.md §2 ("no other camera gets the
        /// bit") — a contract this lane does not get to reopen for a decoration.</para>
        ///
        /// <para>So the light is delivered the one way the built-in pipeline offers PER RENDERER:
        /// <c>LightProbeUsage.CustomProvided</c>, which tells Unity to take that renderer's
        /// light-probe SH out of its <c>MaterialPropertyBlock</c> and from nowhere else. The room's
        /// whole rig is a CONSTANT plus a LINEAR term in the surface normal — the hemisphere is
        /// literally <c>lerp(_AmbDown, _AmbUp, N.y*0.5+0.5)</c> (EnvRoom.shader:546), i.e. a
        /// midpoint plus a y-gradient — and an SH L0/L1 set is exactly a constant plus a linear
        /// term: <c>unity_SHAr = float4(gradient.rgb→r, constant.r)</c> reconstructs
        /// <c>dot(unity_SHAr, float4(N,1))</c> = <c>A + B·N</c> with no approximation whatsoever.
        /// The clamped-cosine lobes (the moon's and each candle's <c>saturate(dot(N, L))</c>) are
        /// the only approximation: they are projected onto the same linear basis as
        /// <c>0.25 + 0.5·(N·L)</c>, the standard L1 fit, which is right at the terminator, a little
        /// soft at the highlight and cannot be told apart at 5.8 m through a doorway.</para>
        ///
        /// <para><b>WHAT THIS BUYS AND WHAT IT DOES NOT.</b> It buys the DIRECTION — the moon rakes
        /// the figure from the same bearing it rakes the wall behind it, and the candle pools swell
        /// on it as it passes them, because the point terms are re-evaluated at the figure's chest
        /// every frame. It does not buy a hard terminator, a specular highlight or a cast shadow,
        /// because SH cannot express them. Shadows are refused on their own evidence — see the
        /// comment on <c>shadowCastingMode</c> in <see cref="Collect"/>.</para>
        ///
        /// <para><b>THE HONEST LIMIT, and the probe for it.</b> SH reaches a shader only if that
        /// shader samples light probes (<c>ShadeSH9</c>, i.e. a <c>ForwardBase</c> pass — every
        /// Standard/Amplify PBR shader does; a hand-written unlit one does not).
        /// <see cref="Diag"/> prints each material's shader name and whether it has a
        /// <c>ForwardBase</c> pass, once per creature. Equally: this cannot REMOVE the game scene's
        /// own lights from the figure, since <c>_LightColor0</c> is a per-pass global and not a
        /// per-renderer one — so <see cref="Diag.Scene"/> lists every real light in the scene and
        /// says whether its culling mask includes the mod layer.</para>
        ///
        /// <para><b>WHY A TINT AND NOT ONLY SH — the paragraph that used to end "those two lines
        /// decide whether the next round has anything left to do here", answered by hardware.</b>
        /// <b>USER REPORT, verbatim:</b> "Die Figuren sind VOLL sichtbar in der schwarzen Nacht als
        /// wären sie voll angestrahlt. Das soll nicht sein. Sie MÜSSEN an die Lichtverhältnisse
        /// angeglichen werden, sonst geht der Gruselfaktor verloren." Both screenshots
        /// (<c>.planning/debug/nachladen1.jpg</c>, <c>nachladen2.jpg</c>) show a WHITE figure in a
        /// pitch-black room. SH did not darken it, and there are two independent reasons why it
        /// could not have, only one of which the last round listed:
        /// <list type="number">
        /// <item>SH is ADDITIVE AMBIENT. It can only ever ADD light to a figure. Nothing about
        /// handing a renderer a very dark probe set makes it dark — it makes it dark ONLY IF nothing
        /// else lights it, and something else does: <c>Diag.Scene</c> exists precisely because the
        /// game's own scene lights reach the mod layer and cannot be masked off per renderer. A
        /// figure lit by the game's sun stays lit by the game's sun however black the probes are.
        /// </item>
        /// <item>The burn edge of the dissolve was EMISSIVE, and no lighting term of any kind
        /// darkens an emissive pixel (class doc). That half is fixed by deleting the dissolve.</item>
        /// </list>
        /// So the darkening lever has to be MULTIPLICATIVE and has to sit on the albedo, ahead of
        /// every lighting term including the ones this side cannot see —
        /// <see cref="TintNames"/>, driven from <see cref="Level"/> below. THE SH IS KEPT: it costs
        /// nothing, it is already written, and where the shader does sample probes it is what makes
        /// the moon rake the figure from the same bearing it rakes the wall. It is now the
        /// DIRECTION and the tint is the AMOUNT.</para>
        ///
        /// <para><b>THE ELEMENT MIRROR, KEPT TO FOUR CONSTANTS AND THREE LINES.</b> A mirror is a
        /// liability in this project and the rule is to mirror the minimum, so this mirrors the
        /// element response ONLY where its absence would be worse than its drift: under Dark the
        /// room's moon is all but eclipsed and under Light it swells, and an apparition that stayed
        /// evenly lit while the room went black would be far more wrong than one that is a few
        /// percent off. <b>The other side is <c>EnvElement.cginc</c>:
        /// <c>GhvrAmbGain</c> :274-278, <c>GhvrDirGain</c> :339-343, <c>GhvrMoonLightAt</c> :590-597,
        /// and the four defines <c>GHVR_AMB_LIFT_OUT/IN</c> :203-206, <c>GHVR_DIR_LIFT_OUT/IN</c>
        /// :209-211, <c>GHVR_MOON_SWELL</c> :543 and <c>GHVR_ECL_FLOOR</c> :540</b>, as consumed by
        /// EnvRoom.shader:445-463. Whoever retunes those must retune these, and the failure mode is
        /// specific and visible: a figure that is brighter or darker than the wall it walks past
        /// while an element is up. It cannot desync anything — every client runs the same mirror off
        /// the same shared element channel.</para>
        ///
        /// <para>Deliberately NOT mirrored: the candle FLICKER (EnvRoom.shader's <c>Flicker</c>,
        /// which would drag in the shared clock, the Air channel and a sine per slot per frame) and
        /// the eclipse COVERAGE (a constant 1.0 since MOON HELD, and the cginc says so at
        /// :561-567). The flicker is worth naming as a known omission: the figure's candle pool does
        /// not shiver with the flame. None of the four events is anywhere near a candle — the cellar
        /// window figure is OUTSIDE the wall, the stair figure is inside a black shaft, and the
        /// forest's three "points" are a wisp, a far lantern and a firefly swarm whose flicker alpha
        /// is near zero by construction (EnvRoom.shader:464-468) — so the omission is invisible in
        /// every framing this feature actually has.</para>
        ///
        /// <para><b>COST.</b> Per apparition: one walk of the room's renderers at spawn to find a
        /// material carrying the rig (a few dozen <c>HasProperty</c> tests, once). Per frame: three
        /// point evaluations, two normalises and seven <c>MaterialPropertyBlock.SetVector</c> calls,
        /// then one <c>SetPropertyBlock</c> per renderer — four or five renderers on these
        /// creatures. No new draw call, no extra pass, no shadow map, and nothing that MultiPass
        /// doubles: SH is per-renderer constant data, so both eyes read the same block. The expected
        /// cost is under 0.05 ms and it is folded into the existing <c>Env.HauntFigDrive</c> step,
        /// which is where hardware will price it. It is skipped entirely on frames where neither the
        /// figure nor the element gains moved, which is most of the watcher's eight and a half
        /// seconds.</para>
        ///
        /// <para><b>MULTIPLAYER.</b> Nothing here is state, nothing is sent and nothing is read from
        /// the game: it is a property block on a renderer this side created, computed from a
        /// material this side only reads and from the element channel both clients already share.
        /// Two players see the same creature in the same place at the same second, lit the
        /// same way.</para>
        /// </summary>
        private static class Lighting
        {
            // ---- the rig's property names, as the bake writes them -------------------------------
            private static readonly int AmbUpId = Shader.PropertyToID("_AmbUp");
            private static readonly int AmbDownId = Shader.PropertyToID("_AmbDown");
            private static readonly int DirDirId = Shader.PropertyToID("_DirDir");
            private static readonly int DirColId = Shader.PropertyToID("_DirCol");
            private static readonly int PtHardId = Shader.PropertyToID("_PtHard");

            // ---- THE TWO THE SURFACE TAKES AND THE FIGURE DOES NOT (diagnostics only) -----------
            //
            // WHAT THESE ARE FOR, stated up front because they are deliberately NOT wired into the
            // Level the figure gets. The rig above is read off a ROOM MATERIAL, and in the forest
            // that material is 'S_Ground'. That shader does not consume _DirCol raw: it multiplies
            // the moon by _DirScale = 0.38 (EnvGround.shader:494, authored to a user ruling — "der
            // erscheint viel zu hell ... er soll eher leicht angestrahlt werden von Mond") and then
            // again by the CANOPY SHADOW, which under a closed crown bottoms out at MinVis = 0.25
            // (BuildEnvironmentRooms.cs floorLook). So the floor an apparition stands on keeps
            // 0.38 x 0.25 = 9.5% of the moon this side hands the figure at 100%, and the moon is 92%
            // of the constant term out there. That is a real ten-fold overstatement and it is what
            // the ModBuild 148 round set out to find.
            //
            // IT IS MEASURED AND PRINTED AND NOT APPLIED, and that is a measured decision rather
            // than caution — see THE DARKENING block below for the two photographs and the
            // arithmetic that rules it out. Reading them costs two property tests once per
            // apparition, and having the number in Player.log is what lets the next round act on it
            // without another probe.
            private static readonly int DirScaleId = Shader.PropertyToID("_DirScale");
            private static readonly int CsFltId = Shader.PropertyToID("_CsFlt");
            private static readonly int[] PtPosId =
            {
                Shader.PropertyToID("_L0Pos"), Shader.PropertyToID("_L1Pos"), Shader.PropertyToID("_L2Pos"),
            };
            private static readonly int[] PtColId =
            {
                Shader.PropertyToID("_L0Col"), Shader.PropertyToID("_L1Col"), Shader.PropertyToID("_L2Col"),
            };

            // ---- Unity's own per-renderer SH slots ------------------------------------------------
            private static readonly int ShArId = Shader.PropertyToID("unity_SHAr");
            private static readonly int ShAgId = Shader.PropertyToID("unity_SHAg");
            private static readonly int ShAbId = Shader.PropertyToID("unity_SHAb");
            private static readonly int ShBrId = Shader.PropertyToID("unity_SHBr");
            private static readonly int ShBgId = Shader.PropertyToID("unity_SHBg");
            private static readonly int ShBbId = Shader.PropertyToID("unity_SHBb");
            private static readonly int ShCId = Shader.PropertyToID("unity_SHC");

            // ---- THE MIRROR. Four constants; see the class doc for the other side and its lines ----
            private const float AmbLiftOut = 0.85f;    // GHVR_AMB_LIFT_OUT — the clearing lifts
            private const float AmbLiftIn = 0.00f;     // GHVR_AMB_LIFT_IN  — the cellar does not
            private const float DirLiftOut = 0.90f;    // GHVR_DIR_LIFT_OUT
            private const float DirLiftIn = 1.40f;     // GHVR_DIR_LIFT_IN — the cellar's moon takes
                                                       //   the ambient's share as well as its own
            private const float MoonSwell = 0.34f;     // GHVR_MOON_SWELL
            private const float EclipseFloor = 0.05f;  // GHVR_ECL_FLOOR
            // ...and the two ModBuild 150 added for the LIGHT+DARK MIXTURE. USER: "Licht und
            // Dunkelheit zusammen ist im Keller garnicht sichtbar, es sollte schon so ein Mittelweg
            // sein, dass beides grob erkennbar ist." Both carry `li * dk`, so no state with either
            // element at zero can move by a single bit — which is what makes adding them here safe
            // without re-verifying every one of the 64 subsets on this side too.
            private const float AmbDefend = 0.35f;     // GHVR_AMB_DEFEND — Light gives back part of
                                                       //   Dark's crush on the ambient
            private const float EclipseRelief = 0.38f; // GHVR_ECL_RELIEF — Light lifts the eclipse's
                                                       //   ATTENUATION; no geometry moves, so the sky
                                                       //   keeps its total eclipse and its copper

            /// <summary>Element indices in the game's own <c>EElement</c> order, as
            /// <see cref="ElementMood.Live"/> takes them.</summary>
            private const int ElemLight = 4;
            private const int ElemDark = 5;

            /// <summary>The L1 projection of a clamped cosine lobe: <c>saturate(N·L) ≈ 0.25 +
            /// 0.5·(N·L)</c>. Exact at the terminator, 0.75 instead of 1.0 straight into the light —
            /// the standard trade for expressing a directional source in a linear basis.</summary>
            private const float LobeConst = 0.25f;
            private const float LobeLinear = 0.50f;

            // ---- the bound rig, in WORLD space -----------------------------------------------------
            private static bool _valid;
            private static Vector3 _ambUp, _ambDown, _dirCol, _dirWorld;
            private static readonly Vector3[] PtPos = new Vector3[3];
            private static readonly Vector3[] PtCol = new Vector3[3];
            private static readonly float[] PtInvRange = new float[3];
            private static float _ptHard;
            private static bool _indoor;

            /// <summary>The share of the moon the SURFACE this rig was read from actually keeps —
            /// <c>_DirScale</c> times the canopy shadow's minimum visibility. 1 when the material
            /// declares neither (the cellar). Diagnostics only; see the block at
            /// <see cref="DirScaleId"/>.</summary>
            private static float _surfMoonShare = 1f;
            private static float _surfDirScale = 1f;
            private static float _surfCanopyMinVis = 1f;

            // ---- THE DARKENING, and the four numbers a tuning drop would move -------------------
            //
            // THE MAPPING. `lum` is the Rec.709 luminance of the SH CONSTANT TERM — i.e. of all the
            // light the room's own rig delivers to the figure's chest, ambient plus moon plus
            // candles, with the element gains already folded in. `Level` is what the figure's albedo
            // is multiplied by:
            //
            //     Level = clamp(DarkFloor + LightGain * lum, DarkFloor, MaxLevel)
            //
            // WHAT THAT ACTUALLY COMES OUT AT, from the bake's own rig constants, because a mapping
            // nobody has evaluated is a mapping nobody can judge:
            //   * CELLAR (BuildEnvironmentRooms.cs:2927-2944 — ambUp (0.028,0.032,0.045), ambDown
            //     (0.020,0.018,0.015), dirCol (0.048,0.070,0.128)): away from the candles the
            //     constant term is (0.0360,0.0425,0.0620) — the hardware log's own numbers,
            //     Player.log:21368 — lum 0.0425, so Level 0.021. A creature at a FIFTIETH of its
            //     albedo, which is what an unlit cellar corner has to mean.
            //   * NIGHT FOREST (:10520-10540 — the moon is 0.70/0.79/0.94 out there): the constant
            //     term is (0.1895,0.2150,0.2575) (Player.log:7469), lum 0.2126, so Level 0.078. The
            //     wood is genuinely brighter than the cellar and the figure follows it.
            // Walking into a candle pool raises it; Dark eclipsing the moon lowers it; both fall out
            // of the rig evaluation that was already here.
            //
            // ====================== ModBuild 149: ALL FOUR ARE NOW MEASURED ======================
            //
            // USER RULING, ModBuild 148 hardware, verbatim and twice: "Die Figuren die einem im Wald
            // beobachten sind in ihren Texturen immer noch voll Sichtbar (siehe Sichtbarkeit.jpg).
            // Sie sollen aus der Dunkelheit beobachten kaum sichtbar weil dort kein Licht ist. So
            // extrem sichtbar ist es kein Horror." — and for the cellar window: "Weiterhin gilt auch
            // hier die Lichtverhältnisse - die Figur ist voll sichtbar mit allen Texturen aktuell."
            //
            // THE PARAGRAPH ABOVE SAID THESE FOUR COULD NOT BE TUNED FROM THIS MACHINE. THAT WAS
            // TRUE AND IS NOT ANY MORE, because the user shipped two photographs of the shipped
            // build and a photograph is a photometer. Both frames are 3840x2160 sRGB; both were
            // linearised and sampled over the figure and over the surface behind it (Rec.709
            // luminance, 90th percentile so that the number is the figure's LIT side rather than the
            // mean of a box that is half background):
            //
            //   .planning/debug/Sichtbarkeit.jpg      (forest, Level was 0.321)
            //       figure p90 0.0290   nearest trunk p90 0.0042   ->  6.9x the surface behind it
            //   .planning/debug/Kellerfenster_figur.jpg (cellar, Level was 0.100)
            //       figure p90 0.0458   masonry p90 0.0027         -> 17.0x the surface behind it
            //
            // "Kaum sichtbar" is not 7x and it is certainly not 17x. The target taken here is that
            // the figure may be about TWICE the surface it stands in front of — enough that a player
            // who is looking at it resolves a silhouette, not enough that it pulls the eye of one who
            // is not. Dividing through by the Level that produced each measurement gives what the
            // figure would be at Level 1, i.e. the part this side does not control (albedo, plus the
            // GAME's own lights, which no per-renderer setting can take away):
            //
            //       B_forest = 0.0290 / 0.321 = 0.090     B_cellar = 0.0458 / 0.100 = 0.458
            //
            // so the Level each room WANTS is 2 x background / B:
            //
            //       forest  2 x 0.0042 / 0.090  = 0.093        cellar  2 x 0.0027 / 0.458 = 0.012
            //
            // THE CELLAR NEEDS AN EIGHTH OF WHAT THE FOREST NEEDS, and the reason is in the log
            // rather than in the rooms' own light: the cellar window figure stands 4.7 m from
            // 'FireTorch_PointLight' (intensity 8.59, range 7 m, "-> REACHES the apparition",
            // Diag.Scene) while the forest figure is 13 m from anything the game lights. B_cellar is
            // five times B_forest for that reason alone.
            //
            // FITTING THE TWO POINTS. The room luminances the rig delivers are 0.2126 (forest) and
            // 0.0425 (cellar) — a ratio of 5.0 against a wanted-Level ratio of 7.8 — so a straight
            // line through them with a floor near zero lands both within a factor of 1.5, which is
            // well inside what a photograph through a headset can settle:
            //
            //       Level = 0.006 + 0.34 x lum   ->   forest 0.078 (1.7x trunk), cellar 0.021 (3.5x wall)
            //
            // AND THIS IS WHY THE CANOPY OCCLUSION IS MEASURED BUT NOT APPLIED. Subtracting the
            // forest floor's own moon response from the measurement (see the block at DirScaleId —
            // _DirScale 0.38 times canopy MinVis 0.25) takes the forest's luminance from 0.2126 to
            // 0.066, i.e. it collapses the two rooms' luminances to a ratio of 1.55 while the ratio
            // the photographs demand is 7.8. Solving the same two-point fit against the corrected
            // numbers gives LightGain 3.4 and a DarkFloor of MINUS 0.13 — the mapping stops being
            // expressible. The occlusion is a true statement about the bake and a false lever here:
            // what separates the two rooms is not their own light, it is the game's, and the
            // uncorrected rig luminance happens to track that far better. The hypothesis was tested
            // rather than assumed, and the numbers are left in the log so the next round can retest
            // it against a THIRD photograph instead of re-deriving them.
            //
            // WHAT EACH KNOB IS NOW FOR, so a tuning drop moves the right one:
            //   * DarkFloor 0.006 (was 0.045). The old floor was three times the cellar's whole
            //     answer, so in the darkest room in the mod the floor WAS the answer and the
            //     measurement did nothing. It still exists — a present creature is never a pure
            //     black hole — but it is now well under the darkest room's own level.
            //   * LightGain 0.34 (was 1.30). This is the knob the user's two sentences move.
            //   * MaxLevel 0.30 (was 0.80). 0.80 needed lum 0.58 and was unreachable in either room,
            //     i.e. it was not a clamp at all. 0.30 is about four times the forest's answer, so it
            //     bites exactly where it should: a figure that walks into a candle pool.
            //   * UnlitLevel 0.05 (was 0.20). The fallback for "the rig could not be read" was
            //     brighter than either room's real answer, which made a bundle mismatch look like a
            //     spotlight. It is now at the darker end of what the rooms really deliver.
            //
            // ================ ModBuild 150: WHY 149's FIVEFOLD CUT READ AS "NO DIFFERENCE" =======
            //
            // USER RULING on the ModBuild 149 build, verbatim: "Die Figuren sind alle voll und hell
            // sichtbar wie zuvor, ich sehe keinen Unterschied." (cellar) and "Auch hier sind die
            // Figuren noch viel zu gut zu sehen statt eine Silhouette im dunklen Schatten zu sein
            // (siehe Figur_hell.jpg)." (wood). The cellar figure's multiplier had just gone from
            // 0.100 to 0.020 — a factor of five — so "no difference" is a statement about a
            // mechanism, not about a number, and TWO mechanisms turned out to be behind it:
            //
            //   1. THREE OF THE HIGH CULTIST'S FOUR MATERIALS WERE NEVER TINTED AT ALL
            //      (Player.log:11409, "'_MOD_TINT' on 1 of 4 material(s)"), and the Living Bones'
            //      cloth was the third of its three (Player.log:4608). Those materials are exactly
            //      the magenta skirt and ribbons in Figur_hell.jpg, and they carry 37% of everything
            //      the figure emits in that frame. Multiplying the other 63% by five while a third
            //      of the light does not move is a change a person can miss. See TintNames and Bind
            //      for the type bug that caused it.
            //   2. THE REMAINDER GOT MORE SATURATED AS IT GOT DARKER — see THE DESATURATION in
            //      Shade for the measurement.
            //
            // AND THE MAPPING ITSELF IS STILL A LITTLE HIGH, measured off Figur_hell.jpg the same way
            // the two ModBuild 148 frames were: over the figure's already-darkened NEUTRAL pixels the
            // 90th-percentile linear luminance is 0.0291 against 0.0050 for the trunk beside it, so
            // 5.8x the surface behind it where the standing target is about 2x. Holding DarkFloor and
            // taking LightGain 0.34 -> 0.20 puts the wood at Level 0.049 (was 0.078) and the cellar
            // at 0.015 (was 0.021), i.e. that same neutral measurement at about 3.6x before the two
            // mechanisms above are counted at all. It is a factor of 1.7 and not the full 2.9,
            // because points 1 and 2 are the large corrections in this build and over-darkening on
            // top of them risks the failure the user has NOT reported — a figure nobody can find.
            //
            // ============ ModBuild 152: EVERY FIT ABOVE THIS LINE DIVIDED BY THE WRONG NUMBER ======
            //
            // THE LEVER WAS INERT WHILE ALL THREE PHOTOGRAPHS WERE TAKEN. See THE ALPHA GATE in
            // Shade(): _MOD_TINT was written with the material asset's authored alpha of 0, the
            // shader ignored it, and the multiplier the arithmetic above divides through by never
            // reached a pixel. So every "Level was 0.321" / "Level was 0.100" / "Level was 0.078" in
            // the derivations above is really LEVEL = 1, and each fit was solving for the wrong
            // unknown.
            //
            // THE SAME THREE MEASUREMENTS, DIVIDED BY 1 INSTEAD. Wanted Level = 2 x background /
            // figure, with the figure now known to have been photographed at full albedo:
            //     Sichtbarkeit.jpg      (forest) 2 x 0.0042 / 0.0290 = 0.290
            //     Figur_hell.jpg        (forest) 2 x 0.0050 / 0.0291 = 0.344
            //     Kellerfenster_figur.jpg (cellar) 2 x 0.0027 / 0.0458 = 0.118
            //
            // AND THIS IS THE REASON TO BELIEVE IT RATHER THAN THE OLD READING. The two FOREST
            // frames are the same event in the same room under the same rig, so any correct model
            // must give them the same answer. The old reading gives 0.093 and 0.027 — a factor of
            // 3.5 apart, which is why each round needed a new correction to explain why the last
            // one "made no difference". The inert-lever reading gives 0.290 and 0.344, which agree
            // to within 19% — inside what a headset photograph can settle. One hypothesis makes
            // three independent frames consistent; the other makes two frames of the same room
            // contradict each other.
            //
            // THE NEW FIT is therefore a straight line through the cellar point and the mean of the
            // two forest points (0.317), against the rig luminances those rooms deliver — and it
            // needs no compromise at all, because the wanted-Level ratio (2.7) and the room
            // luminance ratio (5.0) are finally the same order:
            //     Level = 0.070 + 1.15 x lum   ->   cellar (lum 0.0425) 0.119, forest (0.2126) 0.315
            //
            // WHICH KNOB A TUNING DROP MOVES, restated for the new numbers:
            //   * DarkFloor 0.070 (was 0.006). It is now the cellar's answer to within a third,
            //     because the cellar's own rig delivers almost nothing and what the figure is really
            //     lit by is the game's FireTorch point light 4.7 m away (Diag.Scene).
            //   * LightGain 1.15 (was 0.20). Still "too bright" -> lower this one.
            //   * MaxLevel 0.60 (was 0.30). 0.30 is now BELOW the forest's own answer, i.e. it would
            //     have clamped the normal case and quietly re-flattened the two rooms. 0.60 is about
            //     twice the forest's answer, so it bites where it should: a figure that walks into a
            //     candle pool.
            //   * UnlitLevel 0.12 (was 0.05). The fallback for "the rig could not be read" sits just
            //     under the darker room's real answer.
            //
            // THE HONEST CAVEAT: these four have never been observed WORKING. Everything above is
            // measured, but it is measured off frames in which the multiply did nothing, so the
            // first hardware round with the gate open is the first real test of the mapping. The
            // failure to watch for has flipped: it is no longer "still too bright", it is "I cannot
            // find it at all" — and the knob for that is DarkFloor.
            //
            // ============ ModBuild 153: THE CAVEAT ABOVE CAME TRUE, AND THE FIT MOVED OUT =========
            //
            // The ModBuild 152 hatch fired exactly as it was written: the log dumped
            // _MOD_TINT=(1.000,1.000,1.000,0.000) with `lit=NO (no ForwardBase pass)` and the user
            // photographed three more fully-lit figures. So the ALPHA was not the gate either, the
            // colour property is inert on Amp_Char_Shader whatever is written into it, and 0.070 /
            // 1.15 / 0.60 / 0.12 were the FOURTH fit through a lever that never reached a pixel.
            //
            // THE FOUR CONSTANTS NOW LIVE IN HauntFigures.Math.cs, re-fitted from scratch against the
            // three ModBuild 152 photographs, and the multiply itself has moved onto the albedo
            // TEXTURE (HauntFigures.Albedo.cs). They are in that file rather than this one for a
            // reason that is not tidiness: it is compiled into the wire tests, so the mapping is now
            // driven by vectors instead of being a constant nobody outside a headset can observe.
            // EVERYTHING ABOVE THIS LINE IS HISTORY — it records how four rounds were spent, and not
            // one of its numbers is live.

            /// <summary>
            /// The albedo multiplier the room's measured light justifies, 0..1 — the AMOUNT half of
            /// "an die Lichtverhältnisse angeglichen". <see cref="Shade"/> multiplies the event's
            /// envelope by it.
            ///
            /// <para>It is a cached measurement rather than a computation: <see cref="Apply"/> writes
            /// it on the frames it runs, and those are exactly the frames on which the figure moved
            /// or the elements did. A watcher standing still under an unchanging rig keeps the value
            /// it was given, which is correct and free.</para>
            /// </summary>
            internal static float Level { get; private set; } = UnlitRoomLevel;

            /// <summary>
            /// The COLOUR of the light the room delivers, normalised so its own Rec.709 luminance is
            /// exactly 1 — i.e. the room's chromaticity with its brightness divided out. The forest's
            /// (0.1895,0.2150,0.2575) comes out as (0.891,1.011,1.211): moonlight, slightly blue.
            ///
            /// <para><b>WHAT IT IS FOR.</b> <see cref="Shade"/> pulls the creature's albedo toward
            /// <c>luma(albedo) * Chroma</c> before multiplying it down — see THE DESATURATION there.
            /// It is a separate property from <see cref="Level"/> because the two answer different
            /// questions: Level is how much light there is, this is what colour it is, and a uniform
            /// multiply can only ever answer the first.</para>
            ///
            /// <para>The default is the cool neutral a moonlit room converges on, so a figure whose
            /// rig could not be read still desaturates toward something plausible rather than toward
            /// a colour of exactly zero, which would be a fade to black.</para>
            /// </summary>
            internal static Vector3 Chroma { get; private set; } = new(0.92f, 1.00f, 1.16f);

            /// <summary>The raw room luminance the last <see cref="Level"/> was computed from, and a
            /// once-per-apparition latch for the line that prints it. Diagnostics only — this is the
            /// number a tuning drop for the four constants above would be read off.</summary>
            private static float _measured;
            private static bool _levelLogged;

            /// <summary>
            /// THE LINE, COMPOSED HERE AND HELD BACK ONE STEP SO IT CAN REPORT AN OUTCOME.
            ///
            /// <para>The <c>HAUNT FIGURES light level</c> line used to be emitted from inside
            /// <see cref="Apply"/> — which runs BEFORE <see cref="Shade"/>, i.e. before
            /// <c>Albedo.Apply</c> has tried to put the multiply onto a texture. It therefore had no
            /// choice but to describe the DESIGN, and it said "IT IS MULTIPLIED INTO THE ALBEDO
            /// TEXTURE" in a ModBuild 153 log in which the multiply had never run: the blit shader
            /// had not resolved, the feature had taken its fail-dark branch, and this line asserted
            /// the opposite. A whole hardware round was spent on a build whose central mechanism was
            /// bypassed at its first line, and this log line is why nobody noticed.</para>
            ///
            /// <para>So the measurement is composed here and PRINTED from <see cref="Shade"/>,
            /// immediately after <c>Albedo.Apply</c>, with <c>Albedo.Outcome</c> — a fact by then —
            /// as its first clause. The standing rule it now obeys: a log line must report an
            /// outcome, not an intention.</para>
            /// </summary>
            private static string _levelText = string.Empty;
            private static bool _levelPending;

            /// <summary>
            /// Print the composed <c>HAUNT FIGURES light level</c> line, once per apparition, with
            /// what ACTUALLY happened to the albedo at its head. Called from <see cref="Shade"/>
            /// right after <c>Albedo.Apply</c>; a no-op on every other frame and in every other room.
            /// </summary>
            internal static void LogLevel()
            {
                if (!_levelPending)
                    return;
                _levelPending = false;
                VRLog.Info("Core", "HAUNT FIGURES light level — " + Albedo.Outcome + ". " + _levelText);
                _levelText = string.Empty;
            }

            private static MaterialPropertyBlock? _block;
            private static Vector3 _lastAt = new(1e9f, 1e9f, 1e9f);
            private static float _lastAmb = -1f, _lastDir = -1f;
            private static readonly HashSet<string> Announced = new(4);

            internal static void Forget()
            {
                _valid = false;
                _lastAt = new Vector3(1e9f, 1e9f, 1e9f);
                _lastAmb = -1f;
                _lastDir = -1f;
                _surfDirScale = 1f;
                _surfCanopyMinVis = 1f;
                _surfMoonShare = 1f;
                // BACK TO THE DIM CONSTANT, not to 1. A figure whose room could not be measured is
                // still a figure in the dark, and the one value this must never fall back to is
                // "full albedo" — that is the picture the user rejected.
                Level = UnlitRoomLevel;
            }

            /// <summary>
            /// Read the room's rig off the first room material that carries it and convert it into
            /// world space. Once per apparition — a few dozen property tests against a room of a few
            /// hundred renderers, on a frame that has just finished loading an asset bundle.
            ///
            /// <para>The values are written by <c>ApplyRig</c> in each material's OWN OBJECT SPACE
            /// (that is the whole reason it takes a <c>Transform</c>), so every one of them is pulled
            /// back out through the transform of the renderer it was read from: a direction through
            /// <c>TransformDirection</c>, a candle position through <c>TransformPoint</c>, and the
            /// packed <c>1/range</c> through the same mean scale <c>s</c> the bake divided by
            /// (BuildEnvironmentRooms.cs:462 writes <c>s / range</c>, so world <c>1/range</c> is
            /// <c>w / s</c>). Getting that wrong would not look wrong — it would look like a candle
            /// with the wrong reach, which is the kind of error nobody finds.</para>
            /// </summary>
            internal static void Bind(Transform? room, SkyStyle style, string model)
            {
                Forget();
                _levelLogged = false;   // one measured-level line per apparition, not per frame
                _levelPending = false;
                _indoor = style == SkyStyle.Cellar;
                if (room == null)
                    return;

                try
                {
                    foreach (Renderer r in room.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r == null)
                            continue;
                        Material m = r.sharedMaterial;
                        if (m == null || !m.HasProperty(AmbUpId) || !m.HasProperty(DirDirId)
                            || !m.HasProperty(PtPosId[0]))
                            continue;

                        Transform xf = r.transform;
                        Vector3 ls = xf.lossyScale;
                        float s = (Mathf.Abs(ls.x) + Mathf.Abs(ls.y) + Mathf.Abs(ls.z)) / 3f;
                        if (s < 1e-4f)
                            continue;

                        _ambUp = Rgb(m.GetColor(AmbUpId));
                        _ambDown = Rgb(m.GetColor(AmbDownId));
                        _dirCol = Rgb(m.GetColor(DirColId));

                        Vector4 dd = m.GetVector(DirDirId);
                        Vector3 dw = xf.TransformDirection(new Vector3(dd.x, dd.y, dd.z));
                        _dirWorld = dw.sqrMagnitude > 1e-8f ? dw.normalized : Vector3.up;

                        _ptHard = m.HasProperty(PtHardId) ? m.GetFloat(PtHardId) : 0f;

                        // WHAT THIS SURFACE ITSELF DOES WITH THE MOON — diagnostics only, never a
                        // factor in Level. _CsFlt.w is the canopy shadow's STRENGTH, i.e. 1 - MinVis
                        // (BuildEnvironmentRooms.cs, CanopyShadowBake.Apply), so the share a fully
                        // occluded fragment keeps is 1 - w. Both default to 1, which is exactly right
                        // for a material that declares neither: the cellar has no canopy and no
                        // ground-only moon response, so its surfaces take the moon in full and this
                        // whole term collapses to 1.
                        _surfDirScale = m.HasProperty(DirScaleId) ? Mathf.Max(m.GetFloat(DirScaleId), 0f) : 1f;
                        _surfCanopyMinVis = m.HasProperty(CsFltId)
                            ? Mathf.Clamp01(1f - m.GetVector(CsFltId).w)
                            : 1f;
                        _surfMoonShare = Mathf.Clamp01(_surfDirScale * _surfCanopyMinVis);

                        for (int i = 0; i < 3; i++)
                        {
                            Vector4 lp = m.GetVector(PtPosId[i]);
                            PtPos[i] = xf.TransformPoint(new Vector3(lp.x, lp.y, lp.z));
                            PtInvRange[i] = lp.w / s;
                            PtCol[i] = Rgb(m.GetColor(PtColId[i]));
                        }

                        _valid = true;

                        // CLAIM THE CHANNEL ONLY NOW. CustomProvided means "this renderer's
                        // light-probe SH comes from its MaterialPropertyBlock and from nowhere
                        // else" — which is exactly what is wanted once there is a rig to put in it,
                        // and is a renderer with no ambient at all before there is. Apply() runs in
                        // the same frame, so no frame is ever drawn with the claim made and the
                        // block unwritten.
                        for (int i = 0; i < Rends.Count; i++)
                            if (Rends[i] != null)
                                Rends[i].lightProbeUsage = LightProbeUsage.CustomProvided;

                        if (Announced.Add(model + "|" + style))
                        {
                            VRLog.Info("Core", "HAUNT FIGURES lighting bound — the apparition now stands in "
                                + $"the ROOM'S light rig, read off '{m.name}' ({m.shader?.name ?? "<no shader>"}) "
                                + $"in {style}: moon from world direction {_dirWorld:F2} at colour "
                                + $"({_dirCol.x:F3},{_dirCol.y:F3},{_dirCol.z:F3}), hemisphere "
                                + $"({_ambUp.x:F3},{_ambUp.y:F3},{_ambUp.z:F3}) over "
                                + $"({_ambDown.x:F3},{_ambDown.y:F3},{_ambDown.z:F3}), point hardness "
                                + $"{_ptHard:F1}, candles at {PtPos[0]:F2} r={Rng(0):F2}m, {PtPos[1]:F2} "
                                + $"r={Rng(1):F2}m, {PtPos[2]:F2} r={Rng(2):F2}m. This is delivered as "
                                + "per-renderer light-probe SH (LightProbeUsage.CustomProvided), so it "
                                + "reaches ONLY the apparition — no Unity light is created and nothing else "
                                + "the mod draws is touched. It reaches the figure at all only if its shader "
                                + "samples light probes; the material census line says whether it does.");
                        }
                        return;
                    }

                    if (Announced.Add(model + "|" + style + "|none"))
                    {
                        VRLog.Warn("Core", $"HAUNT FIGURES lighting NOT bound in {style} — no renderer under the "
                            + "environment room carries a material with _AmbUp/_DirDir/_L0Pos, i.e. nothing in "
                            + "the room went through BuildEnvironmentRooms.ApplyRig. That is a plugin/bundle "
                            + "mismatch rather than a fault here. The apparition still appears, solid and "
                            + "textured; it is simply lit by whatever the game scene provides, which is the "
                            + "'pasted on' look this pass exists to remove.");
                    }
                }
                catch (System.Exception ex)
                {
                    // Diagnostics-grade failure: an unlit apparition is worse than a lit one and
                    // better than none, so this is never allowed to reach the driver's catch.
                    _valid = false;
                    VRLog.Warn("Core", $"HAUNT FIGURES could not read the room's light rig ({ex.GetType().Name}: "
                                       + $"{ex.Message}). The apparition appears unlit-by-the-room.");
                }
            }

            private static float Rng(int i) => PtInvRange[i] > 1e-5f ? 1f / PtInvRange[i] : 0f;

            private static Vector3 Rgb(Color c) => new(c.r, c.g, c.b);

            /// <summary>
            /// THE COLOUR-SPACE HANDSHAKE, and it is worth its ten lines because getting it wrong is
            /// a five-fold brightness error rather than a subtle one.
            ///
            /// <para><c>ShadeSH9</c> (UnityCG.cginc) ends with
            /// <c>#ifdef UNITY_COLORSPACE_GAMMA res = LinearToGammaSpace(res); #endif</c> — i.e. in a
            /// GAMMA project it treats whatever it is handed as linear and converts it on the way
            /// out. The room's rig values are not linear in that case: they are exactly what the
            /// room's own shaders consume, and those consume them raw. So in a gamma project they
            /// are pre-inverted here and the two conversions cancel; in a LINEAR project — which is
            /// what a game with this art almost certainly is — this function is a single enum
            /// compare and a return, and nothing is touched.</para>
            ///
            /// <para>The constant term converts exactly. The gradient is scaled by the constant's own
            /// per-channel conversion ratio, which is exact where the gradient contributes nothing
            /// and is far closer than leaving it alone everywhere else; a per-normal conversion is
            /// not expressible in a linear basis at all. <see cref="Diag.Scene"/> prints the active
            /// colour space, so which branch ran is in the log rather than in an assumption.</para>
            /// </summary>
            private static void ToLinearIfGamma(ref Vector3 a, ref Vector3 br, ref Vector3 bg,
                                                ref Vector3 bb)
            {
                if (QualitySettings.activeColorSpace != ColorSpace.Gamma)
                    return;
                float ar = Mathf.Max(a.x, 1e-5f), ag = Mathf.Max(a.y, 1e-5f), ab = Mathf.Max(a.z, 1e-5f);
                float lr = Mathf.GammaToLinearSpace(ar);
                float lg = Mathf.GammaToLinearSpace(ag);
                float lb = Mathf.GammaToLinearSpace(ab);
                br *= lr / ar;
                bg *= lg / ag;
                bb *= lb / ab;
                a = new Vector3(lr, lg, lb);
            }

            /// <summary>
            /// Evaluate the rig where the figure now stands and hand it to the renderers as SH.
            /// </summary>
            internal static void Apply()
            {
                if (!_valid || _go == null || Rends.Count == 0)
                    return;

                // THE ELEMENT MIRROR — three lines, and with nothing up every one of them is
                // exactly 1.0, so the zero state costs three multiplies and changes nothing. That is
                // the same property the shader side relies on to keep its zero state bit-identical
                // (EnvElement.cginc's "with no element up it does not execute").
                float li = ElementMood.Live(ElemLight);
                float dk = ElementMood.Live(ElemDark);
                float ind = _indoor ? 1f : 0f;
                float ambGain = Mathf.Max(1f + Mathf.Lerp(AmbLiftOut, AmbLiftIn, ind) * li * (1f - dk)
                                          - 0.80f * dk * (1f - AmbDefend * li), 0f);
                float moon = (1f + MoonSwell * li)
                             * (1f - dk * (1f - EclipseFloor) * (1f - EclipseRelief * li));
                float dirGain = Mathf.Max(1f + Mathf.Lerp(DirLiftOut, DirLiftIn, ind) * li
                                          - 0.45f * dk * (1f - li), 0f) * moon;

                Vector3 at = _go.transform.position + Vector3.up * _halfHeight;
                if ((at - _lastAt).sqrMagnitude < 1e-4f
                    && Mathf.Abs(ambGain - _lastAmb) < 1e-4f && Mathf.Abs(dirGain - _lastDir) < 1e-4f)
                    return;   // the watcher stands still for eight and a half seconds
                _lastAt = at;
                _lastAmb = ambGain;
                _lastDir = dirGain;

                // ---- the hemisphere: EXACT. lerp(down, up, N.y*0.5+0.5) IS a constant plus a
                // y-gradient, which is what an L0/L1 set holds.
                Vector3 a = (_ambUp + _ambDown) * (0.5f * ambGain);
                Vector3 hemi = (_ambUp - _ambDown) * (0.5f * ambGain);
                Vector3 br = new(0f, hemi.x, 0f);
                Vector3 bg = new(0f, hemi.y, 0f);
                Vector3 bb = new(0f, hemi.z, 0f);

                // ---- the moon.
                Vector3 d = _dirCol * dirGain;
                a += d * LobeConst;
                br += _dirWorld * (d.x * LobeLinear);
                bg += _dirWorld * (d.y * LobeLinear);
                bb += _dirWorld * (d.z * LobeLinear);

                // ---- the candles, at the figure's chest. Same window as EnvRoom.PointLight
                // (:251-261): (1-(d/r)^2)^2 over a near-field hardness term.
                for (int i = 0; i < 3; i++)
                {
                    Vector3 c = PtCol[i];
                    if (c.sqrMagnitude < 1e-8f)
                        continue;
                    Vector3 lv = PtPos[i] - at;
                    float d2 = Mathf.Max(Vector3.Dot(lv, lv), 1e-8f);
                    float q = d2 * PtInvRange[i] * PtInvRange[i];
                    float x = Mathf.Clamp01(1f - q);
                    float atten = x * x / (1f + _ptHard * q);
                    if (atten <= 1e-5f)
                        continue;
                    Vector3 cc = c * atten;
                    Vector3 l = lv / Mathf.Sqrt(d2);
                    a += cc * LobeConst;
                    br += l * (cc.x * LobeLinear);
                    bg += l * (cc.y * LobeLinear);
                    bb += l * (cc.z * LobeLinear);
                }

                // ---- THE MEASUREMENT, taken BEFORE the colour-space handshake. `a` is at this
                // point exactly what the room's own shaders consume — the same numbers the wall
                // behind the figure is shaded with — which is the frame the comparison "as bright as
                // the room" has to be made in. After ToLinearIfGamma it is in ShadeSH9's frame
                // instead, and in a gamma project that would read five times too bright.
                float lum = 0.2126f * a.x + 0.7152f * a.y + 0.0722f * a.z;
                Level = RoomLevel(Mathf.Max(lum, 0f));
                _measured = lum;
                // ...and the same constant term with its brightness divided out. Guarded: a room
                // whose rig evaluates to nothing keeps the cool neutral default rather than
                // publishing a chromaticity of zero, which Shade would read as "desaturate to black".
                if (lum > 1e-5f)
                    Chroma = a / lum;

                if (!_levelLogged)
                {
                    _levelLogged = true;
                    // COMPOSED HERE, PRINTED FROM Shade — see _levelText. Nothing below may claim
                    // that the multiply reached a texture, because at this point in the frame that
                    // has not been attempted yet.
                    _levelPending = true;
                    // ...and what the SURFACE behind the figure keeps of that same moon. Everything
                    // in `a` except the directional term is common to both, so the occluded
                    // luminance is the delivered one minus the share of the moon the surface loses.
                    Vector3 moonTerm = d * LobeConst;
                    float lumMoon = 0.2126f * moonTerm.x + 0.7152f * moonTerm.y + 0.0722f * moonTerm.z;
                    float lumSurf = Mathf.Max(lum - lumMoon * (1f - _surfMoonShare), 0f);
                    _levelText = ("The room delivers luminance "
                        + $"{_measured:F4} at the figure's chest (SH constant term "
                        + $"({a.x:F4},{a.y:F4},{a.z:F4}), the same numbers the wall behind it is shaded "
                        + $"with), so the apparition's albedo is TO BE multiplied by {Level:F4} — an "
                        + "intended value, and the outcome clause at the head of this line is the "
                        + "measured one. THIS IS THE "
                        + "LINE TO TUNE FROM — BUT ONLY IF THE OUTCOME CLAUSE AT THE HEAD OF THIS "
                        + "LINE SAYS 'ALBEDO DARKENING RAN'. The mapping is DarkFloor 0.100 + "
                        + "LightGain 0.470 x luminance, clamped to [0.100, 0.28] "
                        + "(HauntFigures.Math.cs), and its DESTINATION is the albedo TEXTURE rather "
                        + "than a colour property. Whether it reached that texture is not something "
                        + "this sentence may assert: it is reported, measured, by the outcome clause "
                        + "above and by the ALBEDO TEXTURE lines in the material census. THROUGH "
                        + "ModBuild 153 this line claimed the multiply unconditionally, and in that "
                        + "build it had never run — the blit shader had not resolved. If the clause "
                        + "above says DID NOT RUN or DID NOT LAND, every number below describes an "
                        + "intention only and NOTHING may be re-tuned from the photograph that "
                        + "accompanies it. ALL FOUR WERE RE-FITTED IN ModBuild 153 AND NONE OF THE "
                        + "ModBuild 152 VALUES SURVIVES, because 152's own escape hatch fired: it "
                        + "wrote _MOD_TINT with alpha 1.000 and a near-black RGB, the log still "
                        + "reported 'lit=NO (no ForwardBase pass)', and the user photographed three "
                        + "more fully-lit figures (Figur_hell7/8/9.jpg). So the COLOUR property is "
                        + "inert on Amp_Char_Shader whatever is written into it, and the four "
                        + "constants above are fitted directly off those three photographs: figure "
                        + "p50 0.1630/0.1605/0.0966 and p90 0.3870/0.3386/0.2257 are placed at p50 "
                        + "0.019 and p90 0.045, which wants 0.12 in the cellar and 0.20 in the wood "
                        + "under BOTH criteria independently. Predicted result: the figure sits at "
                        + "3.3x the black stair shaft, 0.30x the lit window alcove and 2.3x the "
                        + "forest floor — a pale shape in two framings and a dark shape in the third, "
                        + "both of which are silhouettes. Too dark to find at all means raise "
                        + "DarkFloor; still 'voll angestrahlt' means lower LightGain. "
                        + $"The colour property '{_tintName}' is still written, but where the texture "
                        + "lever landed it now carries the ENVELOPE ONLY and not the level, so a "
                        + "shader that does honour it cannot darken by the square. "
                        + "THE OCCLUSION THE SURFACE HAS AND THE FIGURE DOES NOT, measured off the "
                        + "same material and deliberately NOT applied: the surface keeps "
                        + $"_DirScale {_surfDirScale:F2} x canopy MinVis {_surfCanopyMinVis:F2} = "
                        + $"{_surfMoonShare:F3} of the moon, so the luminance an equally-treated "
                        + $"figure would measure is {lumSurf:F4} instead of {_measured:F4} — a factor "
                        + $"of {(lumSurf > 1e-6f ? _measured / lumSurf : 0f):F2}. Applying it was "
                        + "tried and REJECTED: it collapses the two rooms' luminance ratio to 1.55 "
                        + "against a wanted-Level ratio of 2.7, i.e. it would need a gain the two "
                        + "rooms cannot share (the full arithmetic is in the THE DARKENING block in "
                        + "HauntFigures.Clone.cs). "
                        + "Retest it against a third photograph rather than re-deriving it.\n"
                        + "  THE ESCAPE HATCH FOR THE NEXT ROUND, in the form ModBuild 152 left one "
                        + "and this round's answer came out of. READ THE 'PER-MATERIAL ALBEDO "
                        + "TEXTURE' LINES FIRST — they carry a measured ratio, not an intention:\n"
                        + "    * READING (a), AND IT IS THE ONE THAT COST ModBuild 153: if the "
                        + "outcome clause at the head of this line says 'ALBEDO DARKENING DID NOT "
                        + "RUN' (or DID NOT LAND, or HAD NOTHING TO HOLD), then the mechanism was "
                        + "bypassed and the photograph tests NOTHING. Search the log for that phrase "
                        + "and for 'BUNDLED SHADER' before reading any number below. Do not re-tune.\n"
                        + "    * If the ratio is NOT the intended level (say 0.12 intended but 0.36 "
                        + "or 0.02 measured), the multiply landed in the wrong colour space and the "
                        + "lever is the RenderTexture's sRGB flag, NOT the four constants. A ratio "
                        + "near level^2.2 or level^(1/2.2) is that signature exactly.\n"
                        + "    * If READ-BACK says 'NOT the darkened copy', the write did not land at "
                        + "all and nothing about brightness can be concluded from the photograph.\n"
                        + "    * If the ratio matches, the read-back says the copy is on the material, "
                        + "AND the figure still photographs fully lit, THEN _Diffuse IS NOT WHAT THIS "
                        + "SHADER DRAWS EITHER — and at that point every property-level lever on this "
                        + "shader has been tried and failed, so the next lever is to REPLACE THE "
                        + "SHADER with a mod cutout shader that draws the skinned mesh flat and dark. "
                        + "That costs the dissolve, which belongs to the game shader, and it is not "
                        + "worth doing speculatively.\n"
                        + "    * If the ratio matches and the figure is now too DARK to find, that is "
                        + "the first time in five rounds that this feature will have had a working "
                        + "lever pointed the wrong way, and the knob is DarkFloor alone — LightGain "
                        + "is the slope BETWEEN the rooms and moving it would uncouple them.\n"
                        + "    * If the figure is right but a coloured patch (a sash, a pauldron) is "
                        + "findable while the body is not, the cause is SATURATION and not level: the "
                        + "per-texel desaturation the colour lever used to do is not expressible "
                        + "through a per-channel blit, and the fix is a bundle blit shader with a "
                        + "luma dot product in it.");
                }

                ToLinearIfGamma(ref a, ref br, ref bg, ref bb);

                _block ??= new MaterialPropertyBlock();
                // unity_SHAr = float4(gradient, constant) per channel; ShadeSH9's linear term is
                // dot(unity_SHAr, float4(N,1)). The quadratic bands are published as ZERO rather
                // than left alone: SetPropertyBlock replaces the whole block, and an unwritten
                // unity_SHB* would be read as whatever the renderer last had.
                _block.SetVector(ShArId, new Vector4(br.x, br.y, br.z, a.x));
                _block.SetVector(ShAgId, new Vector4(bg.x, bg.y, bg.z, a.y));
                _block.SetVector(ShAbId, new Vector4(bb.x, bb.y, bb.z, a.z));
                _block.SetVector(ShBrId, Vector4.zero);
                _block.SetVector(ShBgId, Vector4.zero);
                _block.SetVector(ShBbId, Vector4.zero);
                _block.SetVector(ShCId, Vector4.zero);

                for (int i = 0; i < Rends.Count; i++)
                    if (Rends[i] != null)
                        Rends[i].SetPropertyBlock(_block);
            }
        }

        // =========================================================================================
        //  THE DIAGNOSTICS THAT WERE MISSING
        // =========================================================================================

        /// <summary>
        /// Say what the figure's materials ACTUALLY are, what the room's lighting ACTUALLY is, and
        /// what the game scene is doing to both — once per creature per process, bounded.
        ///
        /// <para><b>WHY THIS EXISTS.</b> The first hardware round on this feature produced a report
        /// ("nur leuchtende Silhouetten") that could not be attributed, because there was no log line
        /// anywhere saying what a single material on the figure was set to. Every candidate root
        /// cause had to be reasoned about from the decompiled sources, and one of them — the
        /// paragraph this file used to carry about <c>RefreshVisibility</c> — turned out to have been
        /// asserted rather than tested. That must not be possible a second time, so these three lines
        /// are shipped alongside the fix rather than instead of it, and they are written so that the
        /// NEXT round can answer the question from the log without a single new probe.</para>
        ///
        /// <para><b>WHAT EACH LINE SETTLES.</b> <see cref="RendererCensus"/> prints
        /// <c>CharacterManager.m_Renderers.Length</c> — the private array
        /// <c>InitialiseCharacterStepTwo</c> gathers at :242 and <c>RefreshVisibility</c> walks at
        /// :417. <b>Zero means the preview write never landed and the glow came from the material
        /// asset's own authored values; non-zero means it did land.</b> That is the single number
        /// which distinguishes the two candidates the class doc says are neutralised rather than
        /// tested, and it costs one field read (the reference is publicized at build time).
        /// <see cref="Materials"/> prints, per renderer and per material, the shader name, whether
        /// that shader has a <c>ForwardBase</c> pass (i.e. whether the room's SH can reach it at
        /// all), and every visibility-ish property the shader declares with its live value — BEFORE
        /// this side has written anything and again at full presence. <see cref="Scene"/> prints the
        /// game's ambient settings and every real light in the scene with its culling mask, so that
        /// "the figure is lit by something we did not put there" stops being a hypothesis.</para>
        ///
        /// <para>All three are unconditional (not behind a config flag) and all three are capped: one
        /// scene line per process, one census and two material dumps per creature per process, at
        /// most 8 renderers × 4 materials × 24 properties each. A wrong guess in the next round costs
        /// a hardware session; these lines cost a few kilobytes of log, once.</para>
        /// </summary>
        private static class Diag
        {
            private const int MaxRenderers = 8;
            private const int MaxMaterialsPerRenderer = 4;

            /// <summary>Was 24, AND THE CAP WAS ITSELF A BUG. <c>Amp_Char_Shader</c> declares exactly 24
            /// properties this census calls interesting, so the ModBuild 150 dumps were truncated at
            /// precisely the point where the list would have reached <c>_MOD_TINT</c> — the AFTER dump
            /// never once printed the value it had just written, and "the tint landed" stayed an
            /// inference through two rounds of hardware. <see cref="Named"/> is now printed FIRST for
            /// the same reason: a cap must never be able to eat the property the round is about.</summary>
            private const int MaxPropsPerMaterial = 64;

            private const int MaxLights = 10;

            private static bool _scene;
            private static readonly HashSet<string> Dumped = new(16);

            /// <summary>Named outright because they are the ones the game itself drives on a
            /// character (CharacterManager.cs:427-431, ActorBehaviour.cs:409-415) plus the albedo
            /// inputs, which are what "mit allen Texturen" means in a log line.</summary>
            private static readonly string[] Named =
            {
                "_InvisibilityControl", "_Glow", "_Opacity", "_Toggle_Dissolve",
                "_MainTex", "_Alb", "_BumpMap", "_Color", "_Tint", "_TintColor", "_Cutoff",
                // ...and the lever itself, which was NOT on this list through ModBuild 149 — so the
                // AFTER dump never actually showed the number it had just written, and "the tint
                // landed" stayed an inference rather than a reading. _Diffuse is here for the
                // opposite reason: on Amp_Char_Shader_2Side it is a TEXTURE, and seeing it printed
                // as one is what closes the question this round opened.
                "_MOD_TINT", "_Diffuse",
            };

            /// <summary>...and anything else that could plausibly be making a figure glow or
            /// vanish. Substring match, so a shader nobody has seen still gets caught.</summary>
            private static readonly string[] Smells =
            {
                "Glow", "Opac", "Dissolve", "Invis", "Burn", "Fade", "Alpha", "Emiss",
                "Cutout", "Ghost", "Highlight", "Rim", "Outline",
            };

            /// <summary>The two material dumps. <paramref name="phase"/> is BEFORE (nothing written
            /// by this side yet) or AFTER (the state the player is looking at), and
            /// <paramref name="shade"/> is the COMBINED scalar the albedo was multiplied by —
            /// envelope times the room's light level — or negative when there is none yet.</summary>
            internal static void Materials(string model, string phase, float shade)
            {
                if (model.Length == 0 || Rends.Count == 0)
                    return;
                // KEYED BY ROOM AS WELL AS BY CREATURE, and that was a real hole: the ModBuild 150
                // session showed the same Living Bones in the wood (k = 0.048) and at the cellar
                // window (k = 0.015), and because the latch was per creature the CELLAR instance —
                // the one in the photograph — never dumped at all. One dump per creature per room per
                // phase is still bounded and now covers every framing the user can photograph.
                if (!Dumped.Add(model + "|" + _wantStyle + "|" + (phase[0] == 'B' ? "B" : "A")))
                    return;

                try
                {
                    var sb = new StringBuilder(2048);
                    sb.Append($"HAUNT FIGURES MATERIALS for '{model}' — {phase}");
                    if (shade >= 0f)
                        sb.Append($" (albedo multiplied by {shade:F3})");
                    sb.Append(" (once per creature per process). This is the line whose absence made the "
                              + "last round guesswork: shader, whether the room's light-probe SH can reach "
                              + "it, and every visibility-ish property with its LIVE value.\n  DARKENING "
                              + "LEVER: "
                              + (_tintName.Length > 0
                                     ? $"'{_tintName}' on {Tints.Count} of {Mats.Count} material(s) — the "
                                       + "albedo is multiplied by the envelope times the room's measured "
                                       + "light level (see the HAUNT FIGURES light level line). "
                                       + "_MOD_TINT is the property Choreographer.cs:851-853 writes on "
                                       + "Amp_Char_Shader, which is why it is tried first. "
                                       + "THE ALPHA IS THE ModBuild 152 CHANGE AND IT IS THE FIRST "
                                       + "THING TO READ ON THE PROPERTY LINES BELOW: through 151 this "
                                       + "side preserved the material asset's authored _MOD_TINT alpha "
                                       + "of 0 and wrote a near-black RGB through it, and the figure "
                                       + "rendered at full brightness — a black multiply that changes "
                                       + "nothing is a gate that is shut. The game's own writer always "
                                       + "writes alpha 1 (ColorUtility.TryParseHtmlString on a 6-digit "
                                       + "ColourHTML, defaulted to #FFFFFF at MonsterYMLData.cs:79), so "
                                       + $"alpha is now written as {ModTintAlpha:F3}. IF THE PROPERTY "
                                       + "LINES BELOW SHOW a=1.000 WITH A NEAR-BLACK RGB AND THE FIGURE "
                                       + "IS STILL FULLY LIT, the alpha was not the gate and the next "
                                       + "round must change LEVER (the albedo texture) rather than "
                                       + "tune the RGB a fifth time."
                                     : "NONE — no material declared _MOD_TINT, _Color, _Tint, _TintColor "
                                       + "or _Diffuse with a usable value, so this creature falls back to "
                                       + "being switched on at half presence and cannot be darkened at "
                                       + "all. The property list below is what the next round should pick "
                                       + "a lever from.")
                              + $" It is also DESATURATED by {Desaturation:F2} toward the room's own "
                              + $"light colour {Lighting.Chroma:F3} before being multiplied, because a "
                              + "uniform multiply preserves saturation and a saturated patch survives "
                              + "darkening far better than a neutral one — see THE DESATURATION in "
                              + "Shade()."
                              + "\n  THE COLOUR LEVER IS NO LONGER THE DARKENING, AND THAT IS THE "
                              + "ModBuild 153 CHANGE. 152's escape hatch fired exactly as written — it "
                              + "wrote _MOD_TINT with alpha 1.000 and a near-black RGB, the shader "
                              + "still reported 'lit=NO (no ForwardBase pass)', and the user "
                              + "photographed three more fully-lit figures. So the room LEVEL is "
                              + "DESTINED for the albedo TEXTURE, by a blit — and whether it got "
                              + "there in THIS run is not something this sentence may assert: "
                              + $"{Albedo.Outcome}. On any material that actually got that lever the "
                              + "colour above carries the ENVELOPE ALONE — writing the level through "
                              + "both would darken by its square — and on any material that did NOT, "
                              + "the colour above still carries level x envelope, exactly as ModBuild "
                              + "152 wrote it, which is a lever this shader ignores.\n"
                              + "  THE DISSOLVE IS BACK, as two short windows rather than as a state. "
                              + "152 pinned _Toggle_Dissolve to a hard 0 because its emissive burn "
                              + "edge was the 'schwarze Flecken' and half of the 'voll angestrahlt' "
                              + "report; the user has since asked for exactly that effect ('in "
                              + "partikel auflösen bzw materialisieren, nur ganz kurz'). The toggle is "
                              + "now 1 only while _Cutout is moving — the first and last ~0.35 s of a "
                              + "run — and 0 for everything between, so the STEADY STATE is "
                              + "bit-identical to 152's. _Cutout and not _DeathDissolvePos because "
                              + "that is the property the game's own SummonAppear and DeathDissolve "
                              + "both drive, and neither of them writes _DeathDissolveTop/_Bottom, "
                              + "which are therefore left exactly as authored.\n"
                              + "  PER-MATERIAL VERDICT — which lever each material got, on which "
                              + "shader, or exactly why it got none. THIS IS THE LINE THAT WAS "
                              + "MISSING: 'on 2 of 3 material(s)' named a count and not a culprit, and "
                              + "the culprit was three quarters of what the player could see.\n");
                    for (int i = 0; i < Verdicts.Count && i < MaxRenderers * MaxMaterialsPerRenderer; i++)
                        sb.Append($"    {i}: {Verdicts[i]}\n");
                    if (_donated > 0)
                        sb.Append($"    {_donated} material(s) were moved onto a sibling shader to gain a "
                                  + "lever at all — see Donate() for what that costs.\n");

                    // ---- THE EMISSION CENSUS, which is what ModBuild 151 exists to answer ----------
                    sb.Append("  PER-MATERIAL EMISSION — every emission-like property this shader "
                              + "declares, with its LIVE value and what was done with it. THIS IS THE "
                              + "SECOND LINE THAT WAS MISSING: an albedo multiply cannot darken a term "
                              + "that is ADDED after lighting, and on the treeline watcher that term "
                              + "(_UseEmissiveMap = 1, _EmissiveMapBoost = 2.0) was 190 pixels of face, "
                              + "eyes and pendant at up to luminance 0.892 over a body at 0.00015. "
                              + "Switches go to a hard 0 because the Amplify graphs are not readable "
                              + $"and a scaled switch is undefined; strengths ride k x {EmissiveShare:F2} "
                              + "so an emissive texel lands at the brightness of a mid-grey albedo texel "
                              + "in the same room. IF A CREATURE IS STILL TOO BRIGHT AFTER THIS BUILD "
                              + "AND ITS LINES BELOW ALL SAY 'NO emission-like property', THEN EMISSION "
                              + "IS NOT THE CAUSE FOR IT AND THE LIGHT LEVEL IS.\n");
                    for (int i = 0; i < EmissiveVerdicts.Count
                                    && i < MaxRenderers * MaxMaterialsPerRenderer; i++)
                        sb.Append($"    {i}: {EmissiveVerdicts[i]}\n");

                    // ---- THE ALBEDO TEXTURE CENSUS, which is what ModBuild 153 exists to answer ---
                    //
                    // The house rule for this feature is that every claim must be falsifiable FROM
                    // THE LOG. Four rounds printed a multiplier and none of them printed evidence
                    // that a multiplier had reached a pixel. These lines print two things that
                    // cannot be argued with: the mean luminance of the darkened copy against the
                    // mean luminance of its source, sampled off the render targets themselves, and
                    // the result of READING THE TEXTURE REFERENCE BACK off the material afterwards.
                    sb.Append("  PER-MATERIAL ALBEDO TEXTURE — the lever that reaches this shader, and "
                              + "the numbers that say whether it landed. THE MULTIPLY IS ON THE "
                              + "TEXTURE, so it cannot be ignored the way a colour property can: the "
                              + "ratio below is measured off the render target, not asserted. "
                              + "Alpha is multiplied by exactly 1 because _Cutoff alpha-tests that "
                              + "channel and flattening it would turn cloth and ribbons into solid "
                              + "slabs. WHAT WAS GIVEN UP: the per-texel DESATURATION the colour "
                              + "lever did (0.85 toward the room's chromaticity) cannot be expressed "
                              + "by a per-channel blit multiply, so figure saturation stays at its "
                              + "authored 0.214/0.319 instead of falling to 0.191/0.151 — accepted "
                              + "because the saturated population's luminance (p99 0.049) lands inside "
                              + "the same band as the rest of the creature (p90 0.045) rather than "
                              + "above it. IF THE NEXT PHOTOGRAPH SHOWS A COLOURED PATCH — a sash, a "
                              + "pauldron — findable while the body is not, THEN THE CAUSE IS "
                              + "SATURATION AND NOT LEVEL, and the next lever is a bundle blit shader "
                              + "with a luma dot product in it.\n"
                              + $"    OUTCOME: {Albedo.Outcome}.\n"
                              + $"    cache: {Albedo.CacheReport()}; blit shader: {Albedo.Why}; "
                              + $"applied level {Albedo.AppliedLevel:F4}\n");
                    for (int i = 0; i < Albedo.Verdicts.Count
                                    && i < MaxRenderers * MaxMaterialsPerRenderer; i++)
                        sb.Append($"    {i}: {Albedo.Verdicts[i]}\n");

                    int shown = 0;
                    for (int i = 0; i < Rends.Count && shown < MaxRenderers; i++)
                    {
                        Renderer r = Rends[i];
                        if (r == null)
                            continue;
                        shown++;
                        sb.Append($"  [{r.GetType().Name} '{r.name}' enabled={r.enabled} "
                                  + $"probes={r.lightProbeUsage}]\n");
                        Material[] ms = r.sharedMaterials;
                        for (int j = 0; j < ms.Length && j < MaxMaterialsPerRenderer; j++)
                            Describe(sb, ms[j]);
                    }

                    VRLog.Info("Core", sb.ToString());
                }
                catch (System.Exception ex)
                {
                    VRLog.Info("Core", $"HAUNT FIGURES MATERIALS for '{model}' ({phase}) could not be written "
                                       + $"({ex.GetType().Name}: {ex.Message}). Diagnostics only.");
                }
            }

            private static void Describe(StringBuilder sb, Material? m)
            {
                if (m == null)
                {
                    sb.Append("    <null material>\n");
                    return;
                }
                Shader? sh = m.shader;
                string shName = sh != null ? sh.name : "<null>";
                sb.Append($"    mat '{m.name}' shader '{shName}' "
                          + $"queue={m.renderQueue} lit={Lit(sh)}\n      ");

                if (sh == null)
                {
                    sb.Append("<no shader — no properties readable>\n");
                    return;
                }

                // TWO PASSES, AND THE ORDER IS LOAD-BEARING. The NAMED list goes out first regardless
                // of the shader's own property order, so the cap can never again truncate away the one
                // property the round is about (see MaxPropsPerMaterial). The second pass then adds
                // everything that merely SMELLS interesting and was not already printed.
                int n = 0;
                int count = sh.GetPropertyCount();
                for (int pass = 0; pass < 2 && n < MaxPropsPerMaterial; pass++)
                {
                    for (int p = 0; p < count && n < MaxPropsPerMaterial; p++)
                    {
                        string name = sh.GetPropertyName(p);
                        bool named = IsNamed(name);
                        if (pass == 0 ? !named : named || !Smelly(name))
                            continue;
                        n++;
                        switch (sh.GetPropertyType(p))
                        {
                            case ShaderPropertyType.Color:
                                Color c = m.GetColor(name);
                                sb.Append($"{name}=({c.r:F3},{c.g:F3},{c.b:F3},{c.a:F3}) ");
                                break;
                            case ShaderPropertyType.Vector:
                                sb.Append($"{name}={m.GetVector(name):F3} ");
                                break;
                            case ShaderPropertyType.Float:
                            case ShaderPropertyType.Range:
                                sb.Append($"{name}={m.GetFloat(name):F4} ");
                                break;
                            case ShaderPropertyType.Texture:
                                Texture? t = m.GetTexture(name);
                                sb.Append($"{name}={(t != null ? t.name : "<none>")} ");
                                break;
                            default:
                                sb.Append($"{name}=? ");
                                break;
                        }
                    }
                }
                if (n == 0)
                    sb.Append("<no visibility-ish property on this shader at all — the dissolve fallback "
                              + "in Shade() is what drives this renderer>");
                else if (n >= MaxPropsPerMaterial)
                    sb.Append($"... [TRUNCATED at {MaxPropsPerMaterial}; the NAMED list was printed "
                              + "first, so nothing this round depends on was lost]");
                sb.Append('\n');
            }

            /// <summary>Does this shader have a <c>ForwardBase</c> pass, i.e. will Unity feed it the
            /// light-probe SH the room's rig is delivered through? A definite "no" here is the whole
            /// explanation if a figure comes out solid, textured and still not sitting in the
            /// room.</summary>
            private static string Lit(Shader? sh)
            {
                if (sh == null)
                    return "unknown";
                try
                {
                    var tag = new ShaderTagId("LightMode");
                    var tags = new StringBuilder(48);
                    bool untagged = false;
                    for (int p = 0; p < sh.passCount; p++)
                    {
                        string v = sh.FindPassTagValue(p, tag).name;
                        if (tags.Length > 0)
                            tags.Append('/');
                        tags.Append(string.IsNullOrEmpty(v) ? "<untagged>" : v);
                        if (string.IsNullOrEmpty(v))
                            untagged = true;
                        else if (v == "ForwardBase")
                            return $"YES [{sh.passCount} pass(es), LightMode {tags}] — ForwardBase, "
                                   + "so the room SH reaches it";
                    }

                    // ---- THE EVIDENCE IS PRINTED AND NOT JUST THE VERDICT -------------------------
                    //
                    // ModBuild 152 printed "NO (no ForwardBase pass)" and ModBuild 153 built on it.
                    // The verdict is right, but as a LINE it was under-specified in a way that
                    // matters: in the built-in pipeline a pass carrying NO LightMode tag at all is
                    // still drawn in the forward base slot and DOES receive light-probe SH, so
                    // "found no pass tagged ForwardBase" and "this shader is unlit" are two different
                    // findings and the old line could not tell them apart. The pass count and every
                    // tag value verbatim are now in the line, so the next reader can.
                    return $"NO [{sh.passCount} pass(es), LightMode {tags}, none tagged ForwardBase]"
                           + (untagged
                                  ? ". CAUTION: at least one pass is UNTAGGED, and an untagged pass is "
                                    + "drawn in the base slot and CAN receive light-probe SH. If a "
                                    + "figure ever responds to the rig despite this line, that is why"
                                  : ", and none is untagged either, so this shader genuinely does not "
                                    + "sample light probes and the room's rig cannot reach it")
                           + ". Either way the DARKENING is not supposed to depend on this: since "
                           + "ModBuild 153 it is aimed at the albedo TEXTURE, which is sampled by any "
                           + "pass that draws the creature at all. WHETHER IT GOT THERE IS REPORTED, "
                           + "NOT ASSUMED, on the 'HAUNT FIGURES light level' line and in the OUTCOME "
                           + "line of the PER-MATERIAL ALBEDO TEXTURE block below — ModBuild 153 "
                           + "asserted this multiply in a log in which it had never run.";
                }
                catch (System.Exception ex)
                {
                    return $"unknown ({ex.GetType().Name})";
                }
            }

            private static bool IsNamed(string name)
            {
                for (int i = 0; i < Named.Length; i++)
                    if (Named[i] == name)
                        return true;
                return false;
            }

            private static bool Smelly(string name)
            {
                for (int i = 0; i < Smells.Length; i++)
                    if (name.IndexOf(Smells[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                return false;
            }

            /// <summary>The number that settles which of the two glow candidates it was.</summary>
            internal static void RendererCensus(string model, CharacterManager? cm)
            {
                if (!Dumped.Add(model + "|R"))
                    return;
                try
                {
                    int n = cm != null && cm.m_Renderers != null ? cm.m_Renderers.Length : -1;
                    VRLog.Info("Core", $"HAUNT FIGURES: CharacterManager.m_Renderers.Length = {n} for "
                        + $"'{model}' (once per creature per process). This is the array "
                        + "InitialiseCharacterStepTwo gathers at CharacterManager.cs:242 and "
                        + "RefreshVisibility walks at :417, and it is the ONE number that says which of the "
                        + "two glow candidates was real. ZERO: the preview write (_Glow = 10, _Opacity = "
                        + "0.588, _InvisibilityControl = 1) never landed, and the glowing silhouette came "
                        + "from the material ASSET's own authored values — which the game never has to keep "
                        + "solid, because it overwrites all four on every character at init. NON-ZERO: the "
                        + "hierarchy was not as inactive as this file assumed and the preview look was "
                        + "written onto the materials. Either way Shade() now pins _Glow = 0 and _Opacity = "
                        + "1 after the fact, so the picture is the same; this line is for the next reader.");
                }
                catch (System.Exception ex)
                {
                    VRLog.Info("Core", $"HAUNT FIGURES: m_Renderers could not be read ({ex.GetType().Name}: "
                                       + $"{ex.Message}). Diagnostics only.");
                }
            }

            /// <summary>What the GAME is lighting this figure with, which nothing on this side can
            /// take away: <c>_LightColor0</c> is a per-pass global, not a per-renderer one, so a real
            /// scene light whose culling mask includes the mod layer WILL reach the apparition on top
            /// of the room's SH. Once per process.</summary>
            internal static void Scene()
            {
                if (_scene)
                    return;
                _scene = true;
                try
                {
                    var sb = new StringBuilder(1024);
                    sb.Append("HAUNT FIGURES SCENE LIGHTING (once per process) — what the GAME is lighting "
                        + $"the apparition with in addition to the room's rig. Mod layer = "
                        + $"{VRLayers.ModLayer} (mask 0x{VRLayers.ModLayerMask:X8}); the figure is on it. "
                        + $"colourSpace={QualitySettings.activeColorSpace} (Gamma means ShadeSH9 converts "
                        + "the SH this lane writes; Lighting.ToLinearIfGamma pre-inverts it) "
                        + $"ambientMode={RenderSettings.ambientMode} "
                        + $"ambientLight={RenderSettings.ambientLight} "
                        + $"intensity={RenderSettings.ambientIntensity:F3} "
                        + $"sky={RenderSettings.ambientSkyColor} eq={RenderSettings.ambientEquatorColor} "
                        + $"gnd={RenderSettings.ambientGroundColor} fog={RenderSettings.fog}\n");

                    UnityEngine.Light[] lights = Object.FindObjectsOfType<UnityEngine.Light>();
                    sb.Append($"  {lights.Length} active Light component(s) in the scene");
                    int shown = 0;
                    for (int i = 0; i < lights.Length && shown < MaxLights; i++)
                    {
                        UnityEngine.Light l = lights[i];
                        if (l == null || !l.enabled)
                            continue;
                        shown++;
                        bool hits = (l.cullingMask & VRLayers.ModLayerMask) != 0;
                        sb.Append($"\n    '{l.name}' {l.type} colour={l.color} intensity={l.intensity:F2} "
                                  + $"range={l.range:F1} shadows={l.shadows} mask=0x{l.cullingMask:X8} "
                                  + $"-> {(hits ? "REACHES the apparition" : "does not reach it")}");
                    }
                    sb.Append("\n  A directional light that REACHES the apparition is the one thing this "
                              + "lane cannot cancel per-renderer, and it would rake the figure from the "
                              + "game's own sun direction rather than from the room's moon. If the figure "
                              + "still reads as pasted on after this build, this is the first line to look "
                              + "at.");
                    VRLog.Info("Core", sb.ToString());
                }
                catch (System.Exception ex)
                {
                    VRLog.Info("Core", $"HAUNT FIGURES SCENE LIGHTING could not be written "
                                       + $"({ex.GetType().Name}: {ex.Message}). Diagnostics only.");
                }
            }
        }
    }
}
