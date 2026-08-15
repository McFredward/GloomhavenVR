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

        /// <summary>The drift readout is written ONCE per process: it answers a yes/no question
        /// about the game's clips, and the answer cannot change between two apparitions.</summary>
        private static bool _driftLogged;

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
        /// </summary>
        private sealed class AnimPin : MonoBehaviour
        {
            internal Transform? Target;
            internal Vector3 Seat;

            /// <summary>Largest offset, in local units, the animation managed to put on the
            /// animator's transform before this component took it off again.</summary>
            internal float MaxDrift;

            private void LateUpdate()
            {
                if (Target == null)
                    return;
                float d = (Target.localPosition - Seat).magnitude;
                if (d > MaxDrift)
                    MaxDrift = d;
                // The write is CONDITIONAL: a transform write dirties Unity's hierarchy even when
                // the value is unchanged, and on a skinned figure that is a bind-pose recompute.
                if (d > 1e-6f)
                    Target.localPosition = Seat;
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
        /// </summary>
        private static readonly string[] TintNames = { "_MOD_TINT", "_Color", "_Tint", "_TintColor", "_Diffuse" };

        /// <summary>One material's darkening lever plus the value it shipped with. The ORIGINAL is
        /// captured before this side writes anything, and every write is
        /// <c>original * k</c> — so the creature's own colour identity survives the darkening
        /// instead of being replaced by a grey.</summary>
        private readonly struct TintTarget
        {
            internal readonly Material Mat;
            internal readonly int Prop;
            internal readonly Color Original;

            internal TintTarget(Material mat, int prop, Color original)
            {
                Mat = mat; Prop = prop; Original = original;
            }
        }

        private static readonly List<TintTarget> Tints = new(16);

        /// <summary>Which property name <see cref="Collect"/> settled on, for the log. Empty when no
        /// material declared any of them, which is the case the renderer fallback exists for.</summary>
        private static string _tintName = string.Empty;

        /// <summary>The last COMBINED scalar written (envelope × room light), so the per-frame write
        /// is skipped when neither moved. -1 forces the first write.</summary>
        private static float _lastShade = -1f;

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

        /// <summary>Three consecutive failures and the whole feature stands down for the session —
        /// see <see cref="Disable"/>. Three rather than one because a single failure is
        /// indistinguishable from a bundle that happened to be mid-eviction, and rather than "keep
        /// trying forever" because a room that silently skips every apparition is worse than one
        /// that goes back to the shader-drawn ones.</summary>
        private const int MaxFails = 3;

        // ---- request ---------------------------------------------------------------------------------

        /// <summary>Begin building an apparition. Returns immediately; <see cref="Drive"/> finishes
        /// the job over the following frames.</summary>
        internal static void Request(string model, CClass.ENPCModel picked, in HauntEvent ev,
                                     Transform parent, Transform? room, SkyStyle style)
        {
            Release("a new apparition was armed");
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

        // ---- drive ------------------------------------------------------------------------------------

        /// <summary>
        /// One frame: finish the build if it is still in flight, then pose, animate and shade what
        /// exists. <paramref name="presence"/> is the event's envelope, 0..1.
        /// </summary>
        internal static void Drive(float presence, float runBlend)
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
                    _animator.SetFloat(RunBlendParam, runBlend);

                // THE ROOM'S LIGHT, RE-SAMPLED WHERE THE FIGURE NOW STANDS, AND IT RUNS FIRST.
                // Inside the same [Perf] step as the rest of the drive on purpose: it is a handful
                // of dot products and one property-block write per renderer, and giving it a step of
                // its own would suggest it were a cost worth watching separately. THE ORDER IS
                // LOAD-BEARING since the darkening landed — Apply() is what measures
                // Lighting.Level, and Shade multiplies the envelope by it, so a Shade before an
                // Apply would spend one frame on the previous position's light every time the
                // figure moved. See the Lighting block for the numbers.
                Lighting.Apply();

                Shade(presence);

                // ONE quiet voice per event, once, when the figure is properly there — early enough
                // to be part of the arrival and late enough that it is not heard before there is
                // anything to hear. Gated on the environment-sound switch, for two independent
                // reasons that happen to agree: the standing ruling is that apparition sound rides
                // that one toggle ("nur wenn die Umgebungssounds aktiviert sind",
                // Haunt.Schedule.cs:19), AND the positional overload is only CORRECT while
                // EnvSound holds the AudioListener on the VR head — with the switch off the
                // listener is back on the game's 2D camera, metres from anything, and a 3D item
                // would attenuate to silence exactly as GameAudio's class doc describes.
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
            // Renderers OFF in the SAME frame the object becomes active, before anything is drawn:
            // never a pop-in. This used to write a full dissolve, which is the effect this round
            // deleted — see the class doc.
            Shade(0f);

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

                // ...AND THE ONE LINE OF ActorBehaviour THE STRIP TOOK AWAY. See AnimPin.
                AnimPin pin = _go.AddComponent<AnimPin>();
                pin.Target = _animator.transform;
                pin.Seat = _animator.transform.localPosition;
                _pin = pin;
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
                // invisible shadows. It is now doubly moot: the figures are multiplied down to a
                // fiftieth of their albedo (see Shade), so even a lit-ground framing would want a
                // shadow of a creature that is barely brighter than the floor. If a future event ever
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
                    BindTint(m);
                }
            }

            Diag.Materials(_wantModel, "BEFORE", -1f);
        }

        /// <summary>
        /// Find this material's darkening lever and remember what it shipped with.
        ///
        /// <para><b>A NEAR-BLACK ORIGINAL IS REFUSED AND THE NEXT CANDIDATE IS TRIED</b>, which is the
        /// one guard this needs. The write is <c>original * k</c>, so a property that happens to be
        /// authored black is a lever with no travel: multiplying it changes nothing, and if the
        /// shader really does multiply the albedo by it the model would already be black before this
        /// side touched it — i.e. the property is not what this code thinks it is. Skipping to the
        /// next candidate turns a silent no-op into a working lever on the shaders where one exists,
        /// and into the renderer fallback where none does.</para>
        /// </summary>
        private static void BindTint(Material m)
        {
            for (int i = 0; i < TintNames.Length; i++)
            {
                int id = Shader.PropertyToID(TintNames[i]);
                if (!m.HasProperty(id))
                    continue;
                Color c = m.GetColor(id);
                if (c.r + c.g + c.b < 0.02f)
                    continue;
                Tints.Add(new TintTarget(m, id, c));
                if (_tintName.Length == 0)
                    _tintName = TintNames[i];
                return;
            }
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
        private static void Shade(float presence)
        {
            presence = Mathf.Clamp01(presence);

            // THE ROOM'S LEVEL, measured where the figure stands (Lighting.Apply ran first this
            // frame). With no rig readable this is a fixed dim constant rather than 1: a figure the
            // room cannot be measured for is still a figure in the dark.
            float k = presence * Lighting.Level;

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

            // GONE IS GONE — except on a creature with no tint lever at all, where the only
            // visibility this side has is the renderer switch and the honest place to throw it is
            // the middle of the envelope. See THE FALLBACK in the doc.
            bool visible = Tints.Count > 0 ? k > 0.0015f : presence >= 0.5f;

            // Both tests are needed: the state test catches the frame the figure becomes visible or
            // invisible, the scalar test skips everything else.
            if (Mathf.Abs(k - _lastShade) < 0.0015f && (_lastShade > 0.0015f) == visible)
                return;
            _lastShade = k;

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
                if (m.HasProperty(_toggleId))
                    m.SetFloat(_toggleId, 0f);
            }

            for (int i = 0; i < Tints.Count; i++)
            {
                TintTarget t = Tints[i];
                if (t.Mat == null)
                    continue;
                Color c = t.Original;
                // ALPHA IS NOT TOUCHED. On every one of the candidate properties it means opacity or
                // nothing at all, and this pass is a darkening rather than a fade to transparent —
                // a see-through monster is the "leuchtende Silhouette" the last round removed.
                t.Mat.SetColor(t.Prop, new Color(c.r * k, c.g * k, c.b * k, c.a));
            }
        }

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
            if (!_driftLogged && _pin != null)
            {
                _driftLogged = true;
                float drift = _pin.MaxDrift;
                VRLog.Info("Core", "HAUNT FIGURES animation drift (once per process) — over the whole of "
                    + $"the first apparition ('{_wantModel}'), the animation put at most {drift * 1000f:F2} mm "
                    + "on the animator's OWN transform before AnimPin cancelled it. "
                    + (drift > 0.0005f
                           ? "THAT IS THE TELEPORT: the walk clip carries its own travel, it loops, and "
                             + "before this build nothing took it off again — the mesh crept forward "
                             + "through the cycle and snapped back at the wrap. The game cancels exactly "
                             + "this in ActorBehaviour.ApplyMotion (:611), which this feature strips, and "
                             + "AnimPin is that line restored."
                           : "So the clips do NOT carry travel here and 'applyRootMotion = false' is "
                             + "holding: whatever is left of the teleport report is the GAIT rather than "
                             + "the position — compare the m/s on the 'armed at shared clock' line with "
                             + "the walk clip length on the CENSUS line for the same creature."));
            }
            _pin = null;

            Lighting.Forget();

            // Materials first: they are instances this side created and Unity will not collect them
            // with the objects that reference them.
            Tints.Clear();          // holds Material references — cleared BEFORE they are destroyed
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
            _tintName = string.Empty;
            _voiced = false;
            _wantModel = string.Empty;
            _wantParent = null;
            _wantRoom = null;
            _ = why;
        }

        /// <summary>Release the resident prefab ASSET handle. Separate from <see cref="Release"/>
        /// because the asset outlives an apparition on purpose — a second event using the same
        /// creature must not pay for the bundle twice — but it must not outlive the session.</summary>
        /// <summary>Full teardown: the resident prefab AND the inactive nursery, which is the one
        /// object this feature keeps alive across scenes (<c>DontDestroyOnLoad</c>, because a
        /// nursery that a scene load destroyed would take the half-built figure inside it with
        /// it).</summary>
        internal static void Shutdown(string why)
        {
            Release(why);
            ReleasePrefab(why);
            if (_nursery != null)
            {
                Object.Destroy(_nursery);
                _nursery = null;
            }
        }

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
            private const float DarkFloor = 0.006f;   // a creature is never fully black while present
            private const float LightGain = 0.34f;    // room luminance -> albedo multiplier
            private const float MaxLevel = 0.30f;     // never at full albedo: it is a thing in the dark
            private const float UnlitLevel = 0.05f;   // the rig could not be read at all

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
            internal static float Level { get; private set; } = UnlitLevel;

            /// <summary>The raw room luminance the last <see cref="Level"/> was computed from, and a
            /// once-per-apparition latch for the line that prints it. Diagnostics only — this is the
            /// number a tuning drop for the four constants above would be read off.</summary>
            private static float _measured;
            private static bool _levelLogged;

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
                Level = UnlitLevel;
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
                                          - 0.80f * dk, 0f);
                float moon = (1f + MoonSwell * li) * (1f - dk * (1f - EclipseFloor));
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
                Level = Mathf.Clamp(DarkFloor + LightGain * Mathf.Max(lum, 0f), DarkFloor, MaxLevel);
                _measured = lum;

                if (!_levelLogged)
                {
                    _levelLogged = true;
                    // ...and what the SURFACE behind the figure keeps of that same moon. Everything
                    // in `a` except the directional term is common to both, so the occluded
                    // luminance is the delivered one minus the share of the moon the surface loses.
                    Vector3 moonTerm = d * LobeConst;
                    float lumMoon = 0.2126f * moonTerm.x + 0.7152f * moonTerm.y + 0.0722f * moonTerm.z;
                    float lumSurf = Mathf.Max(lum - lumMoon * (1f - _surfMoonShare), 0f);
                    VRLog.Info("Core", "HAUNT FIGURES light level — the room delivers luminance "
                        + $"{_measured:F4} at the figure's chest (SH constant term "
                        + $"({a.x:F4},{a.y:F4},{a.z:F4}), the same numbers the wall behind it is shaded "
                        + $"with), so the apparition's albedo is multiplied by {Level:F3}. THIS IS THE "
                        + "LINE TO TUNE FROM: the mapping is DarkFloor "
                        + $"{DarkFloor:F3} + LightGain {LightGain:F2} x luminance, clamped to "
                        + $"[{DarkFloor:F3}, {MaxLevel:F2}], and ALL FOUR ARE NOW FITTED TO TWO "
                        + "HARDWARE PHOTOGRAPHS (ModBuild 148: Sichtbarkeit.jpg and "
                        + "Kellerfenster_figur.jpg — the figure measured 6.9x and 17x the surface "
                        + "behind it and the target is about 2x). Too dark to find at all means raise "
                        + "DarkFloor; still reading as 'voll angestrahlt' means lower LightGain. The "
                        + $"multiply is on the ALBEDO (property '{_tintName}' where the shader has "
                        + "one), so it holds whatever else is lighting the figure — including the "
                        + "game's own scene lights, which no per-renderer setting can take away, and "
                        + "which are why the two rooms want Levels an order apart (see the HAUNT "
                        + "FIGURES SCENE LIGHTING line). "
                        + "THE OCCLUSION THE SURFACE HAS AND THE FIGURE DOES NOT, measured off the "
                        + "same material and deliberately NOT applied: the surface keeps "
                        + $"_DirScale {_surfDirScale:F2} x canopy MinVis {_surfCanopyMinVis:F2} = "
                        + $"{_surfMoonShare:F3} of the moon, so the luminance an equally-treated "
                        + $"figure would measure is {lumSurf:F4} instead of {_measured:F4} — a factor "
                        + $"of {(lumSurf > 1e-6f ? _measured / lumSurf : 0f):F2}. Applying it was "
                        + "tried and REJECTED: it collapses the two rooms' luminance ratio to 1.55 "
                        + "against a wanted-Level ratio of 7.8 and forces a negative DarkFloor (the "
                        + "full arithmetic is in the THE DARKENING block in HauntFigures.Clone.cs). "
                        + "Retest it against a third photograph rather than re-deriving it.");
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
            private const int MaxPropsPerMaterial = 24;
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
                if (!Dumped.Add(model + "|" + (phase[0] == 'B' ? "B" : "A")))
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
                                       + "Amp_Char_Shader, which is why it is tried first."
                                     : "NONE — no material declared _MOD_TINT, _Color, _Tint, _TintColor "
                                       + "or _Diffuse with a usable value, so this creature falls back to "
                                       + "being switched on at half presence and cannot be darkened at "
                                       + "all. The property list below is what the next round should pick "
                                       + "a lever from.")
                              + "\n  The DISSOLVE is switched hard off (_Toggle_Dissolve = 0, "
                              + "_InvisibilityControl = 0): its emissive burn edge was the 'schwarze "
                              + "Flecken' and half of the 'voll angestrahlt' report.\n");

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

                int n = 0;
                int count = sh.GetPropertyCount();
                for (int p = 0; p < count && n < MaxPropsPerMaterial; p++)
                {
                    string name = sh.GetPropertyName(p);
                    if (!Interesting(name))
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
                if (n == 0)
                    sb.Append("<no visibility-ish property on this shader at all — the dissolve fallback "
                              + "in Shade() is what drives this renderer>");
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
                    for (int p = 0; p < sh.passCount; p++)
                    {
                        string v = sh.FindPassTagValue(p, tag).name;
                        if (v == "ForwardBase")
                            return "YES (ForwardBase — room SH reaches it)";
                    }
                    return "NO (no ForwardBase pass — this shader does NOT sample light probes, so the "
                           + "room's rig cannot reach it and the next round needs a different lever)";
                }
                catch (System.Exception ex)
                {
                    return $"unknown ({ex.GetType().Name})";
                }
            }

            private static bool Interesting(string name)
            {
                for (int i = 0; i < Named.Length; i++)
                    if (Named[i] == name)
                        return true;
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
