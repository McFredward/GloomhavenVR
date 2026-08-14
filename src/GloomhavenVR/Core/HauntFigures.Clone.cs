using System.Collections;
using System.Collections.Generic;
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
    /// <para><b>WHAT <c>isPreview</c> DOES TO THE LOOK, AND WHAT IS DONE ABOUT IT.</b> It is not
    /// only a guard: it also forces the game's PREVIEW appearance —
    /// <c>m_InvisibilityValue = 1, m_GlowValue = 10, m_OpacityValue = 0.588</c>
    /// (CharacterManager.cs:243-245) — a glowing, half-dissolved hologram. That is precisely the
    /// "obvious effect" look this feature exists to escape, so it is NOT kept. It is also not
    /// actively fought: <c>RefreshVisibility</c> is a no-op at that moment because
    /// <c>m_Renderers</c> was gathered from an INACTIVE hierarchy and came back empty
    /// (:242 uses <c>GetComponentsInChildren&lt;Renderer&gt;()</c> without <c>includeInactive</c>),
    /// so the materials are never touched by the game at all and arrive at their authored,
    /// SOLID values. This side then owns them outright and drives <c>_InvisibilityControl</c> — the
    /// game's own dissolve, the same property <c>WallSegmentFade</c> drives on the masonry — from 1
    /// to 0 for the reveal and back for the fade, with <c>_Glow</c> left at 0. The apparition is
    /// therefore a SOLID, correctly-lit monster that materialises and dematerialises with the game's
    /// own dissolve, not a ghost. That is the deliberate answer to "translucent or solid": a
    /// translucent figure announces itself as a special effect on the frame it appears, and this
    /// feature's entire premise is that the thing you glimpse should be indistinguishable from a
    /// monster that is really there.</para>
    ///
    /// <para><b>ANIMATION — THE ONE FACT EVERYTHING IS BUILT AROUND.</b> There is NO WALK STATE and
    /// NO ROOT MOTION anywhere in this game's character controllers. The state table
    /// (Choreographer.cs:262-302) has Idle-Run, Attack, Damage, Hit, Death, Push, Pull, Loot,
    /// SleepIdle and so on — and "Idle-Run" is a BLEND, driven by a float parameter
    /// <c>RunBlend</c> (ActorBehaviour.cs:79), with the actual travel written by the game onto the
    /// transform (<c>ActorBehaviour.DoTransform</c>/<c>ApplyMotion</c>). So "vorbeilaufen" is:
    /// hold "Idle-Run", write <c>RunBlend</c>, and move the transform yourself. That is what
    /// <see cref="Drive"/> does, and it is what the game does.</para>
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

        /// <summary>Renderer materials, INSTANTIATED once (Unity's <c>Renderer.materials</c> getter
        /// clones the shared set) and owned outright from that moment: Unity does not destroy
        /// instantiated materials with their renderer, which is the leak <c>OverlayMaterialOwner</c>
        /// exists for on the figure-grab side. These are destroyed explicitly in
        /// <see cref="Release"/>.</summary>
        private static readonly List<Material> Mats = new(16);
        private static readonly List<Renderer> Rends = new(8);

        private static int _dissolveId = -1;
        private static int _toggleId = -1;
        private static float _lastPresence = -1f;
        private static bool _spawned;
        private static bool _voiced;
        private static string _wantModel = string.Empty;
        private static CClass.ENPCModel _wantEnum = CClass.ENPCModel.None;
        private static float _wantHeight;
        private static Transform? _wantParent;
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
        internal static void Request(string model, CClass.ENPCModel picked, in HauntEvent ev, Transform parent)
        {
            Release("a new apparition was armed");
            _wantModel = model;
            _wantEnum = picked;
            _wantHeight = ev.Height;
            _wantParent = parent;
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

            _go.transform.SetParent(_wantParent, worldPositionStays: false);
            _go.transform.localPosition = Vector3.zero;
            _go.transform.localRotation = Quaternion.identity;
            _go.transform.localScale = Vector3.one * scale;

            // The ROOM is put on the mod layer (SkyAlternative.cs:1146) and only the VR head camera
            // renders that layer, so a figure standing in the room has to be on it too or it would
            // be drawn by cameras that must never see it and not by the one that must. This is a
            // mod-OWNED object — created here, destroyed here, never handed back — so re-layering
            // it does not violate VRLayers' "never re-layer game-owned objects" rule, which is about
            // reversibility of scene objects the mod merely borrows.
            VRLayers.Apply(_go);

            _go.SetActive(true);
            _spawned = true;
            _lastPresence = -1f;
            Shade(0f);   // fully dissolved in the SAME frame it becomes active: never a pop-in
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
            }

            PerfMonitor.Count("HauntFig.Spawns");
            Roster.CensusFigure(_wantModel, _go, _animator, _state, _hasRunBlend, scale);
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
                r.shadowCastingMode = ShadowCastingMode.Off;   // the rooms are baked-lit; a real-time
                r.receiveShadows = false;                      // shadow would cost and would not match
                Rends.Add(r);
                foreach (Material m in r.materials)            // ← instantiates; we own them now
                    if (m != null) Mats.Add(m);
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
        /// Apply the envelope. <c>_InvisibilityControl</c> is the game's dissolve ramp — 0 solid,
        /// 1 gone — and <c>_Toggle_Dissolve</c> switches the effect on at all, exactly as
        /// <c>CharacterManager.RefreshVisibility</c> does it (:413-436).
        ///
        /// <para>The game's own method is NOT called, and that is a small but real decision:
        /// <c>RefreshVisibility</c> reads <c>renderer.materials</c> on every call, and that getter
        /// allocates a fresh <c>Material[]</c> every time — at 90 Hz for the length of an event that
        /// is a few thousand needless allocations per apparition. The materials are therefore
        /// cached once and written directly, and only when the value actually moved.</para>
        ///
        /// <para>If the creature's shader turns out to have no dissolve control at all, the figure
        /// falls back to being switched on and off at the halfway point of its own envelope. That is
        /// visibly cruder, and it is still better than an apparition that cannot appear.</para>
        /// </summary>
        private static void Shade(float presence)
        {
            presence = Mathf.Clamp01(presence);
            if (Mathf.Abs(presence - _lastPresence) < 0.004f)
                return;
            _lastPresence = presence;

            float dissolve = 1f - presence;
            bool any = false;
            for (int i = 0; i < Mats.Count; i++)
            {
                Material m = Mats[i];
                if (m == null || !m.HasProperty(_dissolveId))
                    continue;
                any = true;
                m.SetFloat(_dissolveId, dissolve);
                if (m.HasProperty(_toggleId))
                    m.SetFloat(_toggleId, dissolve > 0.001f ? 1f : 0f);
            }

            if (any)
                return;

            for (int i = 0; i < Rends.Count; i++)
                if (Rends[i] != null)
                    Rends[i].enabled = presence >= 0.5f;
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
                return;
            }

            // Materials first: they are instances this side created and Unity will not collect them
            // with the objects that reference them.
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
            _lastPresence = -1f;
            _voiced = false;
            _wantModel = string.Empty;
            _wantParent = null;
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
    }
}
