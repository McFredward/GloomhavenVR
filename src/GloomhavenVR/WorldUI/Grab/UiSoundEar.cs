using System.Collections.Generic;
using System.Reflection;
using System.Text;
using GloomhavenVR.Core;
using GloomhavenVR.Hands.Interact;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// THE EAR THE GAME AIMS ITS UI SOUNDS AT.
///
/// ════════════════════════════════════════════════════════════════════════ THE USER'S RULING ══
///
/// <para>Verbatim, 2026-08-21: <i>"5) Die Knöpfe machen keine Geräusche - weder beim mouseover noch
/// beim Drücken - guck das die richtigen Sounds auch hier abgespielt werden."</i> — the buttons make
/// no sound, neither on mouse-over nor on press; make sure the RIGHT sounds play here too. "The
/// right sounds" is the binding half: nothing here may invent a sound, pick a clip or add an asset.
/// Every sound this file is about is authored on the game's own button prefabs and played by the
/// game's own code; this file does not play anything at all.</para>
///
/// ══════════════════════════════════════════════ WHAT MAKES A BUTTON SOUND IN THE FLAT GAME ══
///
/// <para>READ FROM SOURCE, in the decompiled GH.Runtime. Two button classes carry every UI sound in
/// the game and both do it the same way — serialized audio-item IDs on the component, with a shared
/// <c>AudioButtonProfile</c> ScriptableObject (AudioButtonProfile.cs:6-16) as the fallback:</para>
/// <list type="bullet">
/// <item><c>ExtendedButton : Button</c> — HOVER is <c>OnPointerEnter</c> (ExtendedButton.cs:325-333)
///   → <c>OnHighlight()</c> (:410-421) → <c>PlaySound(mouseEnterAudio, mouseEnterAudioItem ??
///   audioProfile.mouseEnterAudioItem)</c> (:418). UN-HOVER is <c>OnPointerExit</c> (:335-339) →
///   <c>OnUnhighlight()</c> (:423-432) → the mouseExit item (:430). CLICK is
///   <c>OnPointerClick</c> (:168-184) → the mouseClick item (:182). Press/release additionally play
///   the mouseDown/mouseUp items (:200-236) — note the down sound is gated on
///   <c>isHighlighted</c> (:208), i.e. on the button having been ENTERED first.</item>
/// <item><c>UnityEngine.UI.UIButtonExtended : Button</c> — the same four, straight in the pointer
///   handlers: down (UIButtonExtended.cs:64-75), up (:77-84), enter (:86-93), exit (:95-102).</item>
/// </list>
///
/// <para>Every one of those funnels into <c>ExtendedButton.PlaySound</c> (:238-255) or
/// <c>AudioControllerUtils.PlaySound</c>, and <b>that</b> is one line:
/// <c>if (AudioController.IsValidAudioID(id)) return AudioController.Play(id);</c>
/// (AudioControllerUtils.cs:7-26). So the whole of the game's UI audio is the single-argument
/// <c>AudioController.Play(string)</c> overload — and that overload is where VR broke it.</para>
///
/// ════════════════════════════════════════════════════════════ THE CAUSE (read from source) ══
///
/// <para><c>AudioController.Play(audioID)</c> does NOT play a 2D sound. It spawns the pooled
/// <c>AudioObject</c> — a real, 3D-configured <see cref="AudioSource"/> — ONE WORLD UNIT IN FRONT
/// OF THE LISTENER:</para>
/// <code>
///     AudioListener l = GetCurrentAudioListener();                       // AudioController.cs:631
///     return Play(audioID, l.transform.position + l.transform.forward,   // AudioController.cs:637
///                 null, 1f, 0f, 0f, source);
/// </code>
/// <para>That is the whole trick the flat game relies on: the listener both PLACES the sound and
/// HEARS it, so a 3D pooled source is always at the ear and distance attenuation can never bite.
/// (If those items were 2D the overload would be pointless — the game would place them anywhere.
/// The pool really is 3D: the game's own <c>GetAudioItemMaxDistance</c> reads
/// <c>category.GetAudioObjectPrefab().GetComponent&lt;AudioSource&gt;().maxDistance</c>,
/// AudioController.cs:1203-1211. This build MEASURES the prefab's <c>spatialBlend</c> and prints it
/// — see the state line — so the next round does not have to take that on trust.)</para>
///
/// <para>And <c>GetCurrentAudioListener()</c> CACHES (AudioController.cs:940-952):</para>
/// <code>
///     if (i._currentAudioListener != null &amp;&amp; i._currentAudioListener.gameObject == null)
///         i._currentAudioListener = null;                    // only a DESTROYED one is dropped
///     if (i._currentAudioListener == null)
///         i._currentAudioListener = (AudioListener)FindObjectOfType(typeof(AudioListener));
///     return i._currentAudioListener;
/// </code>
///
/// <para>Meanwhile <c>Core.EnvSound.TakeListener()</c> (EnvSound.cs:2692-2726) — shipped for the
/// ambience, which needs a head-mounted ear — <b>disables</b> every enabled listener and adds the
/// mod's own on <c>GloomhavenVR.HeadCamera</c>. Disabling a component does not make it Unity-null.
/// So the game's cache keeps pointing at the AUTHORED listener, which sits on the parked flat
/// camera (kept alive and enabled on purpose, FlatScreen.3.Desktop.cs:225-233, with its transform
/// writers prefix-skipped, Rig/CameraControllerPatches.cs:30-34).</para>
///
/// <para><b>Result: the game places every UI sound at an ear that no longer hears, and the ear that
/// does hear is on the head camera — at rigScale 198…256 world units per perceived metre, that is
/// hundreds of world units away.</b> A 3D source at that distance is silent. Nothing errors:
/// <c>AudioController.Play</c> returns a live <c>AudioObject</c> and every "played=True" line in the
/// hardware log stays true. Hover and click go silent together, which is exactly the report — and
/// the buttons still WORK, which is exactly why the cause cannot be event dispatch.</para>
///
/// <para>EnvSound's own header argued the takeover was safe for game audio because "the game's own
/// UI items are 2D (<c>AudioItem.spatialBlend</c> defaults to 0)". That reads a C# field
/// INITIALIZER as if it were the authored asset value, and that field is only consulted at all when
/// <c>sndItem.overrideAudioSourceSettings</c> is set (AudioController.cs:1678-1685); otherwise the
/// pooled prefab's own AudioSource settings stand. The argument also only covered the POSITIONAL
/// overload — for the listener-ANCHORED one the takeover is precisely what breaks it, because it
/// splits the placing listener from the hearing one. A comment is not consent, and it was not
/// evidence either.</para>
///
/// ═══════════════════════════════════════════════════════════════════════════════════ THE FIX ══
///
/// <para>Do not play anything, do not dispatch anything: put the game's cached listener back on the
/// ear that is actually enabled. One field write (<c>protected AudioListener
/// _currentAudioListener</c>, AudioController.cs:70 — reflection, there is no setter), applied
/// whenever the mod's pointer is about to make the game speak. Then
/// <c>AudioController.Play(id)</c> places the sound one world unit in front of the VR head — 1/198
/// of a perceived metre, i.e. at the ear, exactly as in the flat game — and the button's OWN
/// authored item plays, by the game's OWN code. Nothing is substituted and nothing is bundled.</para>
///
/// <para>It repairs, rather than replaces, five call sites: the two listener-anchored
/// <c>Play</c> overloads (:629-649) and the no-parent music/ambience placements (:1290-1318). All
/// five want the ear; there is no consumer of <c>GetCurrentAudioListener</c> that wants the
/// disabled one.</para>
///
/// <para>SELF-HEALING ON TEARDOWN, so the flat game is never left pointing at a corpse:
/// <c>EnvSound.ReleaseListener()</c> <c>Object.Destroy</c>s our listener and re-enables the ones it
/// suppressed (EnvSound.cs:2728-2745). A DESTROYED component IS Unity-null, so the game's own
/// guard at :943-950 drops the cache and re-resolves with <c>FindObjectOfType</c> on the very next
/// sound. We also stop claiming the ear the moment <see cref="OurEar"/> stops finding an enabled
/// listener on the head camera, so nothing here has to be torn down.</para>
///
/// ══════════════════════════════════════════════════════════════════════════ WHY NOT THE OTHERS ══
///
/// <list type="bullet">
/// <item><b>REJECTED: dispatch more pointer events</b> — <i>on the uGUI POINTER path, and only
///   there.</i> (This bullet is scoped as of ModBuild 195: it was written about the laser/fingertip
///   pointer over the game's own converted windows, and for THAT path it still holds. It never
///   covered the mod's own PHYSICAL props, which do not go through
///   <see cref="Hands.Interact.UguiPointer"/> at all — see <see cref="NativeUiPress"/>, which is
///   exactly the "dispatch more pointer events" this bullet rejected, applied where the events
///   were genuinely missing.) Already there and already correct —
///   <see cref="Hands.Interact.UguiPointer.SetHovered"/> walks the ancestor chain with
///   <c>pointerEnter</c>/<c>pointerExit</c> exactly as <c>BaseInputModule.HandlePointerExitAndEnter</c>
///   does, and <c>Press</c>/<c>Release</c> send down/up/click. The hardware log names real game
///   buttons taking them ("uGUI hover ENTER: 'Sell'", "uGUI click: 'Options'"). Adding dispatch
///   would only double-fire what already arrives.</item>
/// <item><b>REJECTED: call the game's audio entry point from the pointer.</b> The brief's last
///   resort, and it would be wrong here twice over: the sound is already being played by the game
///   (it is merely inaudible), so a second call would either double it or, placed the same way,
///   be just as silent.</item>
/// <item><b>NOT DONE, and noted for whoever owns it:</b> <c>ExtendedButton.PlaySound</c> has a
///   second path for buttons with <c>useAudioController == false</c> —
///   <c>UIManager.Instance.PlayUISound(clip)</c> → <c>uiAudioSource.PlayOneShot</c>
///   (UIManager.cs:287-293), a scene AudioSource on the UIManager object. If that source is 3D it
///   is inaudible for the same reason and the cure is <c>spatialBlend = 0</c> on it. It is not
///   touched here because the field defaults to true and no logged button has used that path.</item>
/// </list>
///
/// ═══════════════════════════════════════════════════════════════════════ DOUBLE-FIRING, MULTIPLAYER ══
///
/// <para>DOUBLE-FIRING IS IMPOSSIBLE BY CONSTRUCTION: this file dispatches no uGUI event, plays no
/// sound and subscribes to nothing. It writes one field. The number of hover and click sounds is
/// unchanged — it is whatever the game's own handlers produce for the enter/exit/down/up/click the
/// pointer already sent, arbitrated once per object by <c>UguiHoverTracker</c> so two mod pointers
/// on one widget still highlight and sound it once.</para>
///
/// <para>MULTIPLAYER: nothing on the wire, and nothing here could put anything there. A field on a
/// local audio singleton is not game state, is not read by any rules code, and cannot commit an
/// action — the only thing that can commit an action is a pointer event, and no pointer event is
/// created, suppressed or redirected here. Sound is local by nature: the peer never made this
/// click.</para>
/// </summary>
internal static class UiSoundEar
{
    private const string Scope = "WorldUI";

    // ---- the game's cached-listener field -------------------------------------------------------

    private static FieldInfo? _listenerField;
    private static bool _fieldResolved;

    // ---- our ear (cached; re-fetched when the head camera or the listener changes) ---------------

    private static Camera? _headCam;
    private static AudioListener? _ourEar;

    /// <summary>The listener reference we last wrote into the game's cache. Reference-compared, so a
    /// re-created listener (new head camera, environment rebuilt after a stand-down) re-applies.</summary>
    private static AudioListener? _applied;

    private static int _repairs;
    private static bool _stateLogged;
    private static bool _failureLogged;

    /// <summary>
    /// Called immediately BEFORE the mod's pointer dispatches anything that can make the game play a
    /// UI sound — hover enter/exit, press, release/click. Must run before the dispatch, because the
    /// game plays its sound synchronously inside the handler.
    ///
    /// <para>Cheap enough for that: the common case is one Unity-null check plus one reference
    /// compare, and the call sites are edge-driven (a hover CHANGE, a press, a release), never
    /// per-frame. Never throws — an exception on this path would starve VR input.</para>
    /// </summary>
    internal static void BeforeUiEvent()
    {
        try
        {
            AudioListener? ours = OurEar();
            if (ours == null)
            {
                // The mod does not own the ear (VR down, environment torn down, listener handed
                // back). The game's own cache is then correct or self-heals; claim nothing.
                _applied = null;
                return;
            }
            if (ReferenceEquals(ours, _applied))
                return; // already pointed at this exact listener — nothing can have moved it

            Repair(ours);
        }
        catch (System.Exception ex)
        {
            LogFailureOnce("BeforeUiEvent threw " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>
    /// One-shot proof line, emitted the first time the pointer hovers a widget that resolves to one
    /// of the game's two audio-carrying button classes. Diagnostic ONLY — it reads serialized fields
    /// and plays nothing. <paramref name="widget"/> is the object that took the enter; the button
    /// that owns the sound is normally an ANCESTOR of it (the raycast lands on a label or plate),
    /// which is why the lookup walks up.
    /// </summary>
    internal static void NoticeHoveredWidget(GameObject? widget)
    {
        if (_stateLogged || widget == null)
            return;
        try
        {
            ReportState(widget);
        }
        catch (System.Exception ex)
        {
            LogFailureOnce("NoticeHoveredWidget threw " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    // ---- the ear ---------------------------------------------------------------------------------

    /// <summary>
    /// The enabled <see cref="AudioListener"/> on the mod's head camera, or null when the mod does
    /// not own the ear. Deliberately NOT a scene search: <c>EnvSound.TakeListener</c> puts exactly
    /// one listener on <c>Rig.VRRigDriver.HeadCamera</c> and disables every other, so that component
    /// IS the ear whenever it exists and is enabled — and asking the head camera costs a
    /// <c>GetComponent</c> at most once per camera lifetime instead of a
    /// <c>FindObjectsOfType</c> per hover.
    /// </summary>
    private static AudioListener? OurEar()
    {
        Camera? head = Rig.VRRigDriver.HeadCamera;
        if (head == null)
        {
            _headCam = null;
            _ourEar = null;
            return null;
        }
        // Unity-null on _ourEar covers the destroyed case (stand-down destroys it), so a rebuilt
        // listener on the same camera is picked up without any teardown hook here.
        if (!ReferenceEquals(head, _headCam) || _ourEar == null)
        {
            _headCam = head;
            _ourEar = head.GetComponent<AudioListener>();
        }
        return _ourEar != null && _ourEar.enabled ? _ourEar : null;
    }

    private static void Repair(AudioListener ours)
    {
        AudioController? ac = ClockStone.SingletonMonoBehaviour<AudioController>.DoesInstanceExist();
        if (ac == null)
            return; // no audio system yet (early boot) — nothing to repair, retry on the next event

        FieldInfo? field = ListenerField();
        if (field == null)
            return;

        AudioListener? before = field.GetValue(ac) as AudioListener;
        if (ReferenceEquals(before, ours))
        {
            _applied = ours; // the game already found us by itself; stop re-checking
            return;
        }

        field.SetValue(ac, ours);
        _applied = ours;
        _repairs++;

        float rigScale = RigScale();
        Vector3 earPos = ours.transform.position;
        string wasWhere = before != null
            ? $"'{before.gameObject.name}' at {Fmt(before.transform.position)}, " +
              $"{Vector3.Distance(before.transform.position, earPos):F0} world units " +
              $"(= {Vector3.Distance(before.transform.position, earPos) / Mathf.Max(rigScale, 0.0001f):F1} perceived m) " +
              $"from the VR ear, enabled={before.enabled}"
            : "nothing (the game had not resolved a listener yet)";

        VRLog.Info(Scope, $"UI SOUND EAR repaired (#{_repairs}) — the game's cached AudioListener was {wasWhere}; " +
                          $"it now points at '{ours.gameObject.name}' at {Fmt(earPos)}, rigScale {rigScale:F2} world units " +
                          "per perceived metre. HOW TO READ THIS: AudioController.Play(id) — the ONE call every game UI " +
                          "sound goes through (AudioControllerUtils.cs:7-26) — spawns a 3D pooled AudioSource one world " +
                          "unit in front of THE CACHED LISTENER (AudioController.cs:631-637), while the ear that actually " +
                          "hears is the enabled one on the head camera. A large distance on this line is the silence the " +
                          "user reported; it should now be 0 for every sound played after it. One line per takeover is " +
                          "expected (environment build / rebuild); a line per second would mean something is fighting us " +
                          "for the field. THE PATHS THIS RESTORES, all of them the game's own: hover = " +
                          "ExtendedButton.OnPointerEnter -> OnHighlight -> PlaySound(mouseEnterAudioItem) " +
                          "(ExtendedButton.cs:325-333/410-421), un-hover = OnPointerExit -> OnUnhighlight (:335-339/423-432), " +
                          "press/release = OnPointerDown/OnPointerUp (:200-236, and UIButtonExtended.cs:64-84), click = " +
                          "OnPointerClick (:168-184) — each one ending in AudioController.Play(id). The mod already " +
                          "dispatches every one of those events (see the 'uGUI hover ENTER/EXIT' and 'uGUI click' lines); " +
                          "this line is about where the resulting sound was PUT, not about whether it was triggered.");
    }

    private static FieldInfo? ListenerField()
    {
        if (_fieldResolved)
            return _listenerField;
        _fieldResolved = true;

        _listenerField = typeof(AudioController).GetField(
            "_currentAudioListener",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        if (_listenerField != null && !typeof(AudioListener).IsAssignableFrom(_listenerField.FieldType))
            _listenerField = null;

        if (_listenerField == null)
            VRLog.Warn(Scope, "UI SOUND EAR cannot be repaired: AudioController has no instance field " +
                              "'_currentAudioListener' of type AudioListener (expected at AudioController.cs:70). The " +
                              "game version changed. Every button hover/click sound will stay INAUDIBLE while the mod " +
                              "owns the AudioListener, because the game keeps placing them at the disabled one.");

        return _listenerField;
    }

    // ---- the state line --------------------------------------------------------------------------

    private static void ReportState(GameObject widget)
    {
        // Which button owns this hover. GetComponentInParent asks "which button is this graphic part
        // of", which is the right question here (the enter handler sits on the ancestor) and is used
        // for a LOG LINE only — nothing branches on it.
        ExtendedButton? ext = widget.GetComponentInParent<ExtendedButton>();
        UnityEngine.UI.UIButtonExtended? ube =
            ext == null ? widget.GetComponentInParent<UnityEngine.UI.UIButtonExtended>() : null;
        if (ext == null && ube == null)
            return; // not one of the game's audio-carrying buttons — wait for one that is

        _stateLogged = true;

        string kind;
        string enterItem;
        string exitItem;
        string clickItem;
        string downItem;
        string extra;
        if (ext != null)
        {
            kind = "ExtendedButton";
            AudioButtonProfile? p = ext.audioProfile;
            enterItem = Effective(ext.mouseEnterAudioItem, p != null ? p.mouseEnterAudioItem : null);
            exitItem = Effective(ext.mouseExitAudioItem, p != null ? p.mouseExitAudioItem : null);
            clickItem = Effective(ext.mouseClickAudioItem, p != null ? p.mouseClickAudioItem : null);
            downItem = Effective(ext.mouseDownAudioItem, p != null ? p.mouseDownAudioItem : null);
            extra = $"useAudioController={ext.useAudioController}, profile={(p != null ? p.name : "<none>")}";
        }
        else
        {
            kind = "UIButtonExtended";
            AudioButtonProfile? p = ube!.audioProfile;
            enterItem = Effective(ube.mouseEnterAudioItem, p != null ? p.mouseEnterAudioItem : null);
            exitItem = Effective(ube.mouseExitAudioItem, p != null ? p.mouseExitAudioItem : null);
            clickItem = string.Empty; // this class has no click item — down/up carry the press sound
            downItem = Effective(ube.mouseDownAudioItem, p != null ? p.mouseDownAudioItem : null);
            extra = $"profile={(p != null ? p.name : "<none>")}";
        }

        AudioListener? ours = OurEar();
        float rigScale = RigScale();
        AudioListener? cached = CachedListenerNoResolve();

        string earLine;
        if (ours == null)
            earLine = "the mod does NOT own the AudioListener right now (no enabled listener on the head camera), " +
                      "so the game's own cache is authoritative and nothing was repaired";
        else if (cached == null)
            earLine = $"our ear is '{ours.gameObject.name}' at {Fmt(ours.transform.position)}; the game's cache could " +
                      "not be read (see the WARN line above)";
        else
        {
            float d = Vector3.Distance(cached.transform.position, ours.transform.position);
            earLine = $"our ear is '{ours.gameObject.name}' at {Fmt(ours.transform.position)}; the game places its UI " +
                      $"sounds at '{cached.gameObject.name}' (enabled={cached.enabled}), {d:F0} world units " +
                      $"= {d / Mathf.Max(rigScale, 0.0001f):F1} perceived m away. THAT NUMBER MUST BE 0";
        }

        VRLog.Info(Scope, $"UI SOUND STATE — first game button the VR pointer hovered: '{widget.name}' inside " +
                          $"{kind} '{(ext != null ? ext.gameObject.name : ube!.gameObject.name)}'. " +
                          $"THE GAME'S OWN PATHS: hover = OnPointerEnter -> OnHighlight -> AudioController.Play(item) " +
                          $"[item '{Show(enterItem)}' {Describe(enterItem)}]; un-hover = OnPointerExit " +
                          $"[item '{Show(exitItem)}' {Describe(exitItem)}]; press = OnPointerDown " +
                          $"[item '{Show(downItem)}' {Describe(downItem)}]; click = OnPointerClick " +
                          $"[item '{Show(clickItem)}' {Describe(clickItem)}]. {extra}. " +
                          $"THE MOD REACHES THEM: yes — UguiPointer dispatches pointerEnter/Exit up the ancestor chain " +
                          $"and pointerDown/Up/Click on press and release; the 'uGUI hover ENTER/EXIT' and 'uGUI click' " +
                          $"lines in this log name the widgets that took them. AUDIBILITY: {earLine}. " +
                          $"rigScale {rigScale:F2} world units per perceived metre, repairs so far {_repairs}, " +
                          $"soundMuted={MutedText()}, UI category volume {CategoryVolumeText("UI")}. " +
                          "HOW TO READ A FAILURE: (a) an item shown as 'unknown to AudioController' or '<none>' means the " +
                          "button is silent in the FLAT game too and this is not a VR bug; (b) a non-zero distance above " +
                          "means the ear repair did not run before the sound — look for a 'UI SOUND EAR repaired' line " +
                          "BEFORE the first 'uGUI hover ENTER'; (c) distance 0, spatialBlend 3D and still silent means the " +
                          "listener was never the cause — read soundMuted and the category volume on this line next, then " +
                          "the mixer group, before touching the pointer again.");
    }

    // ---- the state line for ONE PHYSICAL BUTTON (ModBuild 195) -----------------------------------

    /// <summary>Physical props already reported on, keyed by the game GameObject's instance id.
    /// Instance ids, never the GameObject: a destroyed <c>UnityEngine.Object</c> compares EQUAL to
    /// every other destroyed one, which would corrupt a set keyed by the object itself (the same
    /// reason <c>UguiHoverTracker</c> gives).</summary>
    private static readonly HashSet<int> ReportedProps = new(16);

    /// <summary>
    /// ONE LINE PER PHYSICAL BUTTON, the first time the mod drives it. This is the line the next
    /// round is judged on, and it answers, in order, the four questions the ModBuild 195 brief asks:
    /// WHAT the physical prop dispatches, WHICH audio items the game has authored on the real
    /// widget, WHETHER <c>AudioController</c> knows those items (and whether their pooled source is
    /// 3D), and WHERE the listener is at that moment.
    ///
    /// <para>Diagnostic ONLY — it reads serialized fields through reflection and plays nothing. The
    /// item fields are found by NAME PATTERN (<c>*AudioItem</c>) over every component on the object
    /// rather than by enumerating the game's button classes, because the game has at least five of
    /// them (<c>ExtendedButton</c>, <c>ExtendedToggle</c>, <c>UIButtonExtended</c>,
    /// <c>TrackedButton</c>, <c>TrackedToggle</c>) and a version that adds a sixth must still be
    /// reported honestly instead of silently as "no audio".</para>
    /// </summary>
    /// <param name="target">The GAME GameObject the physical prop dispatches into.</param>
    /// <param name="what">Human name of the physical prop, e.g. "map table cap 'Shop'".</param>
    /// <param name="dispatch">Exactly what the prop sends, for the record.</param>
    internal static void ReportPhysicalButton(GameObject? target, string what, string dispatch)
    {
        if (target == null)
            return;
        try
        {
            if (!ReportedProps.Add(target.GetInstanceID()))
                return;

            var sb = new StringBuilder(1024);
            sb.Append("PHYSICAL BUTTON SOUND STATE — ").Append(what)
              .Append(" dispatches into the game object '").Append(target.name).Append("'. ")
              .Append("WHAT IT SENDS: ").Append(dispatch).Append(". ");

            sb.Append("WHAT THE GAME HAS ON THAT OBJECT: ");
            Component[] comps = target.GetComponents<Component>();
            int audioCarriers = 0;
            for (int i = 0; i < comps.Length; i++)
            {
                Component? c = comps[i];
                if (c == null)
                    continue;
                if (AppendAudioFields(sb, c))
                    audioCarriers++;
            }
            if (audioCarriers == 0)
            {
                sb.Append("NO component on it carries a *AudioItem field at all (components: ");
                for (int i = 0; i < comps.Length; i++)
                    sb.Append(i > 0 ? ", " : "").Append(comps[i] != null ? comps[i].GetType().Name : "<missing>");
                sb.Append("). READ THIS AS: the FLAT game is silent on this button too, and no amount "
                          + "of event dispatch can make it speak — the next round must find which "
                          + "object in the game's own hierarchy actually plays the sound. ");
            }

            AudioListener? ours = OurEar();
            AudioListener? cached = CachedListenerNoResolve();
            float rigScale = RigScale();
            sb.Append("WHERE THE LISTENER IS RIGHT NOW: ");
            if (ours == null)
                sb.Append("the mod does NOT own the AudioListener (no enabled listener on the head "
                          + "camera), so the game's own cache is authoritative and nothing was repaired");
            else if (cached == null)
                sb.Append("our ear is '").Append(ours.gameObject.name)
                  .Append("'; the game's cache could not be read (see the WARN line above)");
            else
            {
                float d = Vector3.Distance(cached.transform.position, ours.transform.position);
                sb.Append("our ear is '").Append(ours.gameObject.name).Append("' at ")
                  .Append(Fmt(ours.transform.position)).Append("; the game places its UI sounds at '")
                  .Append(cached.gameObject.name).Append("' (enabled=").Append(cached.enabled)
                  .Append("), ").Append(d.ToString("F0")).Append(" world units = ")
                  .Append((d / Mathf.Max(rigScale, 0.0001f)).ToString("F1"))
                  .Append(" perceived m away. THAT NUMBER MUST BE 0");
            }
            sb.Append(". rigScale ").Append(rigScale.ToString("F2"))
              .Append(" world units per perceived metre, ear repairs so far ").Append(_repairs)
              .Append(", soundMuted=").Append(MutedText())
              .Append(", UI category volume ").Append(CategoryVolumeText("UI")).Append(". ");

            sb.Append("HOW TO READ A FAILURE. (a) An item shown as 'UNKNOWN to AudioController' or "
                      + "'<none>' is silent in the FLAT game too — not a VR bug, and never a reason to "
                      + "substitute a clip. (b) A non-zero listener distance above means the ear repair "
                      + "did not run before this dispatch; look for a 'UI SOUND EAR repaired' line "
                      + "EARLIER in the log. (c) If the DOWN item is authored, the distance is 0, and "
                      + "there is still no press sound, then the button's own gate refused it: both "
                      + "ExtendedButton (:208) and ExtendedToggle gate the DOWN sound on isHighlighted "
                      + "&& interactable, i.e. on a pointerEnter having ARRIVED FIRST and not been "
                      + "undone — count the NATIVE UI PRESS enter/exit balance on the press lines. "
                      + "(d) If only the CLICK item is missing, note that ExtendedToggle has no click "
                      + "item at all: on a toggle the press sound IS the down/up pair, which is why a "
                      + "click-only dispatch was silent by construction before ModBuild 195.");

            VRLog.Info(Scope, sb.ToString());
        }
        catch (System.Exception ex)
        {
            LogFailureOnce("ReportPhysicalButton threw " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>
    /// Append every <c>*AudioItem</c> string field this component declares (own value, the shared
    /// <c>AudioButtonProfile</c> fallback the game itself applies, the effective id, and whether
    /// <c>AudioController</c> can play it). Returns true when the component carried any.
    /// </summary>
    private static bool AppendAudioFields(StringBuilder sb, Component c)
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public
                                   | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        AudioButtonProfile? profile = null;
        List<KeyValuePair<string, string>>? items = null;

        System.Type? cur = c.GetType();
        for (int depth = 0; cur != null && depth < 8 && cur != typeof(MonoBehaviour); depth++, cur = cur.BaseType)
        {
            FieldInfo[] fields = cur.GetFields(Flags);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo f = fields[i];
                if (profile == null && f.FieldType == typeof(AudioButtonProfile))
                    profile = f.GetValue(c) as AudioButtonProfile;
                else if (f.FieldType == typeof(string)
                         && f.Name.IndexOf("AudioItem", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    items ??= new List<KeyValuePair<string, string>>(6);
                    items.Add(new KeyValuePair<string, string>(f.Name, f.GetValue(c) as string ?? string.Empty));
                }
            }
        }
        if (items == null)
            return false;

        sb.Append('[').Append(c.GetType().Name).Append(": profile=")
          .Append(profile != null ? profile.name : "<none>");
        if (c is Selectable sel)
            sb.Append(", interactable=").Append(sel.IsInteractable());
        for (int i = 0; i < items.Count; i++)
        {
            string own = items[i].Value;
            string fromProfile = profile != null
                ? (typeof(AudioButtonProfile).GetField(items[i].Key)?.GetValue(profile) as string ?? string.Empty)
                : string.Empty;
            string eff = Effective(own, fromProfile);
            sb.Append("; ").Append(items[i].Key).Append(" = '").Append(Show(eff)).Append("' (")
              .Append(own.Length > 0 ? "on the component" : (fromProfile.Length > 0 ? "from the shared profile" : "nowhere"))
              .Append(", ").Append(Describe(eff)).Append(')');
        }
        sb.Append("] ");
        return true;
    }

    /// <summary>The game's cached listener WITHOUT triggering its re-resolve — we want to report what
    /// it holds, not change it as a side effect of reporting.</summary>
    private static AudioListener? CachedListenerNoResolve()
    {
        AudioController? ac = ClockStone.SingletonMonoBehaviour<AudioController>.DoesInstanceExist();
        FieldInfo? field = ListenerField();
        if (ac == null || field == null)
            return null;
        return field.GetValue(ac) as AudioListener;
    }

    /// <summary>The game's own resolution order: the per-button override wins, the shared profile is
    /// the fallback (e.g. ExtendedButton.cs:418).</summary>
    private static string Effective(string? own, string? fromProfile) =>
        string.IsNullOrEmpty(own) ? (fromProfile ?? string.Empty) : own!;

    private static string Show(string item) => item.Length == 0 ? "<none>" : item;

    /// <summary>
    /// Is this id something the game can actually play, and is the pooled source 3D? The second half
    /// is the whole reason the stale listener silences anything: a 2D source ignores position, a 3D
    /// one does not. Measured off the game's own category prefab
    /// (AudioController.cs:1203-1211 reads the same AudioSource for maxDistance) rather than assumed.
    /// </summary>
    private static string Describe(string item)
    {
        if (item.Length == 0)
            return "not authored on this button";
        try
        {
            if (ClockStone.SingletonMonoBehaviour<AudioController>.DoesInstanceExist() == null)
                return "unchecked (no AudioController yet)";
            if (!AudioController.IsValidAudioID(item))
                return "UNKNOWN to AudioController";

            var audioItem = AudioController.GetAudioItem(item);
            if (audioItem == null)
                return "valid";
            if (audioItem.overrideAudioSourceSettings)
                return $"valid, item overrides the source: spatialBlend {audioItem.spatialBlend:F2} " +
                       $"({Dimensionality(audioItem.spatialBlend)}), rolloff " +
                       $"{audioItem.audioSource_MinDistance:F1}..{audioItem.audioSource_MaxDistance:F1} world units";

            GameObject? prefab = audioItem.category != null ? audioItem.category.GetAudioObjectPrefab() : null;
            AudioSource? src = prefab != null ? prefab.GetComponent<AudioSource>() : null;
            if (src == null)
                return $"valid, category '{(audioItem.category != null ? audioItem.category.Name : "?")}', " +
                       "pool prefab not readable";
            return $"valid, category '{(audioItem.category != null ? audioItem.category.Name : "?")}', pool source " +
                   $"spatialBlend {src.spatialBlend:F2} ({Dimensionality(src.spatialBlend)}), rolloff " +
                   $"{src.minDistance:F1}..{src.maxDistance:F1} world units";
        }
        catch (System.Exception ex)
        {
            return "uncheckable (" + ex.GetType().Name + ")";
        }
    }

    private static string Dimensionality(float spatialBlend) =>
        spatialBlend <= 0.001f
            ? "2D — position cannot silence it"
            : "3D — placing it away from the ear DOES silence it";

    private static string MutedText()
    {
        try
        {
            if (ClockStone.SingletonMonoBehaviour<AudioController>.DoesInstanceExist() == null)
                return "?";
            return AudioController.IsSoundMuted().ToString();
        }
        catch (System.Exception)
        {
            return "?";
        }
    }

    private static string CategoryVolumeText(string category)
    {
        try
        {
            if (ClockStone.SingletonMonoBehaviour<AudioController>.DoesInstanceExist() == null)
                return "?";
            var c = AudioController.GetCategory(category);
            return c != null ? c.VolumeTotal.ToString("F2") : "<no such category>";
        }
        catch (System.Exception)
        {
            return "?";
        }
    }

    private static float RigScale()
    {
        Transform? rig = Rig.VRRigDriver.RigRoot;
        return rig != null ? rig.lossyScale.x : 1f;
    }

    private static string Fmt(Vector3 v) => $"({v.x:F0}, {v.y:F0}, {v.z:F0})";

    private static void LogFailureOnce(string what)
    {
        if (_failureLogged)
            return;
        _failureLogged = true;
        VRLog.Warn(Scope, "UI SOUND EAR " + what + " — button hover/click sounds may stay inaudible. This is logged " +
                          "once per session; the pointer path itself is unaffected because every call here is guarded.");
    }
}

/// <summary>
/// THE PRESS A PHYSICAL PROP MAKES INTO A GAME BUTTON — enter, down, up, click, exit.
///
/// ════════════════════════════════════════════════════════════════════════ THE USER'S RULING ══
///
/// <para>Verbatim, ModBuild 195: <i>"Immer noch keine Geräusche wenn ich die physischen buttons
/// drücke wie zB 'Händler', ich will das die selben Geräusche kommen die auch im normalen Spiel
/// hörbar sind, wenn die Knöpfe gedrückt werden."</i> — still no sound when he presses the mod's
/// own PHYSICAL buttons; he wants THE SAME sounds the flat game plays. "The same sounds" is the
/// binding half and it is a construction requirement, not a matching requirement: nothing here
/// picks a clip, names an id, or adds a bundled asset. This class plays no sound whatsoever. It
/// sends the game the events the game itself listens for, and the game plays its own authored item
/// with its own code, or it does not — and if it does not, the log says why.</para>
///
/// ════════════════════════════════════════ WHY THE PHYSICAL CAPS WERE SILENT (READ FROM SOURCE) ══
///
/// <para>ModBuild 194 repaired WHERE the game puts a UI sound (<see cref="UiSoundEar"/>). That was a
/// real bug and it is fixed — the ModBuild 194 hardware log shows <c>UI SOUND EAR repaired (#1)</c>
/// moving the game's cached listener 241 world units onto the head camera, and the later
/// <c>UI SOUND STATE</c> line reporting the distance back to <b>0</b>. So audibility is not the
/// remaining problem. <b>The remaining problem is that the game was never asked to speak.</b></para>
///
/// <para>READ FROM SOURCE. The mod's physical props all dispatched exactly ONE event —
/// <c>ExecuteEvents.pointerClickHandler</c> — modelled on the game's own hotkey bridge
/// (<c>BaseButtons.clickButton</c>). Follow that single event into the two classes the props
/// actually target:</para>
/// <list type="bullet">
/// <item>The map room's table caps target the guildmaster bar's <c>Toggle</c>
///   (<c>UIGuildmasterButton</c> holds it in a private <c>[SerializeField] Toggle toggle</c>,
///   decompiled UIGuildmasterButton.cs:24). In this build that toggle is one of the game's
///   audio-carrying toggle classes, and <b><c>ExtendedToggle.OnPointerClick</c> plays NOTHING</b>
///   (ExtendedToggle.cs: it calls <c>InteractabilityManager.ShouldAllowClickForExtendedToggle</c>,
///   the AutoTest recorder, then <c>base.OnPointerClick</c> — and returns). The class has no
///   <c>mouseClickAudioItem</c> field at all. EVERY sound it owns hangs off
///   <c>OnPointerDown</c> / <c>OnPointerUp</c> / <c>OnPointerEnter</c> / <c>OnPointerExit</c>.
///   A click-only dispatch is therefore silent BY CONSTRUCTION, at any listener distance.</item>
/// <item>The board keycaps' Ready/Undo/Skip targets are <c>ExtendedButton</c>s.
///   <c>ExtendedButton.OnPointerClick</c> does play an item (:182) — but only
///   <c>mouseClickAudioItem</c>, and the ModBuild 194 log's own <c>UI SOUND STATE</c> line shows a
///   real game button whose click item is <c>&lt;none&gt;</c> while its shared profile
///   ("General Hover Button SFX") carries only the HOVER item. On a button authored that way the
///   press sound is again the down/up pair, and again a click-only dispatch is silent.</item>
/// </list>
///
/// <para>AND THE DOWN SOUND HAS A SECOND GATE. <c>ExtendedButton.OnPointerDown</c> plays its item
/// only <c>if (isHighlighted &amp;&amp; interactable)</c> (:208), and <c>isHighlighted</c> is set
/// by <c>ToggleHighlight</c> from <c>OnHighlight()</c>, which only runs from <c>OnPointerEnter</c>
/// (:325-333, :410-421). <c>ExtendedToggle</c> gates its down sound identically. A physical prop
/// that never sends <c>pointerEnter</c> cannot make a down sound even once the down event arrives.
/// <b>So the hover half is not a nicety here — it is half the mechanism.</b></para>
///
/// ═══════════════════════════════════════════════════════════════════════════════════ THE FIX ══
///
/// <para>Send the four events a real left-mouse press sends, in the order the input module sends
/// them, on the same GameObject: <c>pointerEnter</c> → <c>pointerDown</c> → <c>pointerUp</c> →
/// <c>pointerClick</c> (→ <c>pointerExit</c> when the prop has no hover of its own to hand back).
/// The sounds are then the game's by construction, because they are played by the game's own
/// handlers off the game's own serialized ids.</para>
///
/// <para>ORDER IS LOAD-BEARING, and this is READ FROM SOURCE, not preference: down/up MUST precede
/// the click. <c>UIGuildmasterButton.RefreshSelected</c> sets <c>toggle.interactable = !toggle.isOn</c>
/// (UIGuildmasterButton.cs), so the click that turns the toggle ON makes the toggle
/// non-interactable in the same call stack. Dispatching down after that would fail
/// <c>isHighlighted &amp;&amp; interactable</c> and play the NON-interactable item instead of the
/// press item.</para>
///
/// <para>ONE GAMEOBJECT, NOT THE ANCESTOR CHAIN. <c>ExecuteEvents.Execute</c> delivers to the
/// handlers on the target object only (ExecuteEvents.cs:248-278). That is deliberate: the sound is
/// authored on the <c>Selectable</c> itself, and for the guildmaster bar the owning
/// <c>UIGuildmasterButton</c> is on that SAME GameObject anyway — its
/// <c>[RequireComponent]</c> partner <c>GuildmasterModeSelectable</c> resolves the toggle with a
/// plain <c>GetComponent&lt;Selectable&gt;()</c> (GuildmasterModeSelectable.cs), which is only
/// possible if the two share an object. Walking further up would additionally drive the flat HUD's
/// tooltip and hover animation (<c>UIGuildmasterHUD.OnHovered</c>, :762-783), which have no VR
/// surface — churn with no user-visible gain. <see cref="Hands.Interact.UguiPointer"/> keeps doing
/// the full chain walk for the game's own converted windows, where it IS needed.</para>
///
/// ══════════════════════════════════════════════════════ A PRESS STILL COMMITS EXACTLY ONCE ══
///
/// <para>THIS IS THE HARD CONSTRAINT — the "Händler" cap opens the merchant, and one push must open
/// it once. The proof is that the commit lives in exactly one of the five events, and the other
/// four are read from source to be incapable of it:</para>
/// <list type="bullet">
/// <item><c>pointerClick</c> → <c>Toggle.OnPointerClick</c> → <c>InternalToggle()</c> →
///   <c>isOn = !isOn</c> (ugui Toggle.cs:312-329) → <c>onValueChanged</c> →
///   <c>UIGuildmasterButton.OnToggled</c> → <c>OnSelected.Invoke(mode)</c>. <b>THE COMMIT.</b>
///   Sent once per press, exactly as before this change. For a <c>Button</c> the same role is
///   <c>Button.OnPointerClick</c> → <c>Press()</c> → <c>onClick.Invoke()</c>.</item>
/// <item><c>pointerDown</c> → <c>Selectable.OnPointerDown</c> (Selectable.cs:1205-1216): sets
///   <c>EventSystem.current.SetSelectedGameObject</c> and <c>isPointerDown</c>, then a colour
///   transition. No toggle, no <c>onClick</c>. The subclass overrides
///   (<c>ExtendedButton</c>:200-222, <c>ExtendedToggle</c>) add a LeanTween scale and a
///   <c>PlaySound</c> — nothing else.</item>
/// <item><c>pointerUp</c> → <c>Selectable.OnPointerUp</c> (:1245-1252): clears
///   <c>isPointerDown</c>. Overrides add a scale write and a <c>PlaySound</c>.</item>
/// <item><c>pointerEnter</c>/<c>pointerExit</c> → <c>Selectable</c>:1277-1311: two bools and a
///   colour transition. Overrides add <c>onMouseEnter</c>/<c>onMouseExit</c> (on the guildmaster
///   bar: a tooltip label and a hover animation, UIGuildmasterHUD.cs:762-789), a highlight tween
///   and a <c>PlaySound</c>.</item>
/// <item>We never send <c>ExecuteEvents.submitHandler</c>. <c>Toggle.OnSubmit</c> DOES call
///   <c>InternalToggle()</c> (Toggle.cs:331-334) and would be a second commit — it is named here
///   so that a later round cannot add it by accident.</item>
/// </list>
/// <para>And a handler that THROWS cannot swallow the commit either: <c>ExecuteEvents.Execute</c>
/// catches per handler (ExecuteEvents.cs:270-277), so a broken down-handler logs and the click
/// still runs. Each dispatch here additionally sits inside this class's own try/catch, because an
/// unguarded exception on a VR input path starves input entirely.</para>
///
/// <para>DOUBLE-FIRING IS COUNTED, NOT ASSUMED. Enter/exit are reference-counted through the
/// mod-wide <see cref="Hands.Interact.UguiHoverTracker"/> — the same arbiter the laser and the
/// fingertip pointers use — so a cap held by two hands still enters once and exits once. Every
/// dispatch increments a counter, and the counters are printed on every press line
/// (<see cref="Counters"/>): if <c>clicks</c> ever exceeds the number of "pressed" lines, or if
/// <c>enters</c> and <c>exits</c> drift apart while nothing is hovered, the next log shows it
/// without needing a new build.</para>
///
/// ════════════════════════════════════════════════════════════════════════════════ MULTIPLAYER ══
///
/// <para>NOTHING GOES ON THE WIRE, and nothing here could put anything there. This class creates no
/// network message, touches no Bolt/FFSNet state and calls no rules code; it calls
/// <c>ExecuteEvents.Execute</c> on a game GameObject. The one event that commits — the click — is
/// the SAME single dispatch the props already made before ModBuild 195, on the same object, with
/// the same guards (<c>InteractabilityManager</c>, <c>IsInteractable</c>, the button's own
/// <c>canToggle</c> predicate). So whatever the game chose to send for a press, it still sends
/// exactly that, exactly once: the wire cannot tell this build from the last one. The four events
/// ADDED are enter/down/up/exit, which are hover highlighting, a scale tween and a local sound —
/// no commit path leads out of any of them (proved item by item above). Sound itself is local by
/// nature: the peer did not make this press and must not hear it.</para>
///
/// <para>TURNING AND HEAD-TRACKING: untouched. This class has no Update, holds no transform and
/// never re-orients anything.</para>
/// </summary>
internal static class NativeUiPress
{
    private const string Scope = "WorldUI";

    /// <summary>
    /// The pointer id every PHYSICAL PROP dispatch carries. Distinct from the laser (-111/-112) and
    /// fingertip (-101/-102) uGUI pointers so uGUI never sees one pointer teleporting, and below
    /// <c>UguiPointer.ModPointerIdCeiling</c> (-100) so the mod's own "is this event ours" tests
    /// (<c>Cards.CardFaceRaycaster</c>, <c>WorldUI.Patches.TooltipRaiseGuard</c>) still recognise it
    /// as a mod event rather than as the game's parked desktop mouse.
    /// </summary>
    internal const int PropPointerId = -121;

    private static int _enters;
    private static int _exits;
    private static int _downs;
    private static int _ups;
    private static int _clicks;
    private static bool _failureLogged;

    /// <summary>Running dispatch tally, for the press log lines. HOW TO READ IT: enters and exits
    /// must be equal whenever nothing is hovered; clicks must equal the number of press lines.</summary>
    internal static string Counters =>
        $"enters={_enters} exits={_exits} (balance {_enters - _exits}) downs={_downs} ups={_ups} clicks={_clicks}";

    /// <summary>
    /// Hover a game widget from a physical prop. Reference-counted through
    /// <see cref="Hands.Interact.UguiHoverTracker"/>: the FIRST claim dispatches
    /// <c>pointerEnter</c>, the LAST release dispatches <c>pointerExit</c>, everything in between
    /// is silent. <b>Every <c>true</c> call must be balanced by a <c>false</c> call</b>, including
    /// on teardown — an unbalanced enter leaves the game's button highlighted forever.
    /// </summary>
    /// <returns>True when an event was actually dispatched (i.e. this was the first enter or the
    /// last exit), for the caller's log only. The claim is taken either way.</returns>
    internal static bool SetHovered(GameObject? target, bool hovered, string what)
    {
        // REFERENCE null, not Unity-null: a DESTROYED widget still has to balance its refcount on
        // the exit path (its managed wrapper's GetInstanceID keeps working), and only a genuinely
        // absent reference has nothing to hand back. Same rule UguiHoverTracker.Release states.
        if (target is null)
            return false;
        try
        {
            if (!hovered)
            {
                if (!UguiHoverTracker.Release(target))
                    return false; // another pointer still holds it, or it was never claimed
                if (target == null)
                    return false; // destroyed with the count balanced — no event left to send
                UiSoundEar.BeforeUiEvent();
                Dispatch(target, ExecuteEvents.pointerExitHandler, target);
                _exits++;
                return true;
            }

            if (target == null)
                return false; // never enter a destroyed widget

            UiSoundEar.ReportPhysicalButton(target, what, "pointerEnter on hover, then "
                                                          + "pointerDown -> pointerUp -> pointerClick on press, "
                                                          + "then pointerExit when the hover ends");

            // THE EAR, BEFORE THE EVENT. The game plays its hover item SYNCHRONOUSLY inside the
            // dispatch below; the repair must already have happened when it does.
            UiSoundEar.BeforeUiEvent();

            if (!UguiHoverTracker.Acquire(target))
                return false; // another pointer already holds this widget entered (claim IS taken)
            Dispatch(target, ExecuteEvents.pointerEnterHandler, target);
            _enters++;
            return true;
        }
        catch (System.Exception ex)
        {
            LogFailureOnce("SetHovered", what, ex);
            return false;
        }
    }

    /// <summary>
    /// Press a game widget from a physical prop: <c>pointerDown</c> → <c>pointerUp</c> →
    /// <c>pointerClick</c>, with <c>pointerEnter</c>/<c>pointerExit</c> wrapped around them when
    /// the caller does not already hold a hover on this widget.
    ///
    /// <para>The click is dispatched LAST and exactly once — see the class doc for the item-by-item
    /// reading of why none of the other four events can commit the action.</para>
    /// </summary>
    /// <param name="alreadyHovered">True when the caller has an outstanding
    /// <see cref="SetHovered"/>(true) on this widget. When false, this call synthesizes the
    /// enter/exit pair itself, because the DOWN sound is gated on the button having been entered.</param>
    internal static void Press(GameObject? target, bool alreadyHovered, string what)
    {
        if (target == null)
            return;

        bool synthesized = false;
        try
        {
            UiSoundEar.ReportPhysicalButton(target, what,
                alreadyHovered
                    ? "pointerDown -> pointerUp -> pointerClick (the enter came from the prop's own hover)"
                    : "pointerEnter -> pointerDown -> pointerUp -> pointerClick -> pointerExit, "
                      + "synthesized around this one press because the prop carries no hover of its own");

            // NOTE the flag is set from "did I ask for a claim", NOT from SetHovered's return
            // value: SetHovered returns false when ANOTHER pointer already holds the widget
            // entered, but it has still taken a reference — reading the return value here would
            // leak that reference and leave the game's button highlighted for good.
            if (!alreadyHovered)
            {
                synthesized = true;
                SetHovered(target, hovered: true, what);
            }

            UiSoundEar.BeforeUiEvent();

            // Down and up BEFORE the click: the click can make the widget non-interactable in the
            // same call stack (UIGuildmasterButton.RefreshSelected sets interactable = !isOn), and
            // both button classes gate their press sound on `interactable`. See the class doc.
            if (Dispatch(target, ExecuteEvents.pointerDownHandler, target, press: true))
                _downs++;
            if (Dispatch(target, ExecuteEvents.pointerUpHandler, target, press: true))
                _ups++;

            // THE COMMIT. Counted only when it really went out, so the log cannot claim a press the
            // game never received. If the widget vanished during down/up the action does NOT commit
            // — that would be a regression against the click-only dispatch this replaced, so it is
            // shouted rather than swallowed. (No handler read from source does this: Selectable's
            // down/up set two bools and a colour, and the subclass overrides add a tween and a
            // PlaySound. The guard exists so a future game version cannot make it silent.)
            if (Dispatch(target, ExecuteEvents.pointerClickHandler, target, press: true))
                _clicks++;
            else
                VRLog.Warn(Scope, $"NATIVE UI PRESS: '{what}' could not deliver its pointerClick — the game "
                                  + "object was destroyed by its own pointerDown/pointerUp handlers. THE "
                                  + "ACTION DID NOT COMMIT. Read this as a game-version change, not as a "
                                  + "double: the press is LOST, not repeated.");
        }
        catch (System.Exception ex)
        {
            LogFailureOnce("Press", what, ex);
        }
        finally
        {
            // Hand the synthesized hover back even if a dispatch threw — an unbalanced enter would
            // leave the game's own button highlighted for the rest of the session. Unity-null safe:
            // the click may have destroyed the widget (a mode switch rebuilds the guildmaster bar).
            if (synthesized)
            {
                try
                {
                    SetHovered(target, hovered: false, what);
                }
                catch (System.Exception ex)
                {
                    LogFailureOnce("Press/unhover", what, ex);
                }
            }
        }
    }

    /// <summary>
    /// One event, with a freshly built <see cref="PointerEventData"/> shaped the way
    /// <c>PointerInputModule</c> shapes a real left-mouse event. Allocating per dispatch is
    /// deliberate and cheap: these calls are EDGE-driven (a hover change, a press), never per-frame,
    /// and a shared instance would carry stale press/drag bookkeeping between unrelated props.
    /// </summary>
    /// <returns>True when the event was actually handed to <c>ExecuteEvents</c> — false only when
    /// the target was destroyed in the meantime, so the counters can never overstate what the game
    /// received.</returns>
    private static bool Dispatch<T>(GameObject target, ExecuteEvents.EventFunction<T> functor,
                                    GameObject enterTarget, bool press = false)
        where T : UnityEngine.EventSystems.IEventSystemHandler
    {
        if (target == null)
            return false;
        var data = new PointerEventData(EventSystem.current)
        {
            pointerId = PropPointerId,
            button = PointerEventData.InputButton.Left,
            // REQUIRED, not cosmetic: Selectable.OnPointerEnter returns immediately unless
            // eventData.pointerEnter resolves back to this same Selectable (Selectable.cs:1277-1280),
            // so without this the base highlight transition would silently not happen. (The audio
            // overrides play their item either way — they call PlaySound after base — but the
            // visual state the game itself keeps would have been wrong, and a later round reading
            // isHighlighted would have been misled.)
            pointerEnter = enterTarget,
            // A plausible screen point, so any handler that reads it (tooltip placement) gets a
            // finite value instead of (0,0). Nothing in VR is positioned from it.
            position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f),
        };
        data.pressPosition = data.position;
        if (press)
        {
            data.pointerPress = target;
            data.rawPointerPress = target;
            data.eligibleForClick = true;
            data.clickCount = 1;
            data.clickTime = Time.unscaledTime;
        }
        ExecuteEvents.Execute(target, data, functor);
        return true;
    }

    private static void LogFailureOnce(string where, string what, System.Exception ex)
    {
        if (_failureLogged)
            return;
        _failureLogged = true;
        VRLog.Warn(Scope, $"NATIVE UI PRESS {where} threw for {what}: {ex.GetType().Name}: {ex.Message}. "
                          + "Logged once per session. The physical button itself keeps working — every "
                          + "dispatch here is guarded, and the game action commits on the click, which "
                          + "ExecuteEvents runs even when an earlier handler throws "
                          + "(ExecuteEvents.cs:270-277). What you lose is the sound, not the press.");
    }
}
