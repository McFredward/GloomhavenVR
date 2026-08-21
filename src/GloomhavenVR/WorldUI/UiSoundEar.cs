using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;

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
/// <item><b>REJECTED: dispatch more pointer events.</b> Already there and already correct —
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
