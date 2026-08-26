using BepInEx.Configuration;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Voice;

// =================================================================================================
//  VOICE MODULE — the dials, and the one GameObject that ticks the spatialiser. Registered after
//  NetModule because it reads the avatars NetModule owns.
// =================================================================================================

/// <summary>
/// Binds <c>[Voice]</c> and stands up <see cref="VoiceDriver"/>.
///
/// <para><b>WHY A MODULE OF ITS OWN RATHER THAN A PHASE INSIDE <c>NetAvatarDriver</c>.</b> The
/// obvious home is the driver that already owns the avatar dictionary and already ticks per peer.
/// It was rejected for one reason: <c>[Net] Enabled = false</c> would then silently take spatial
/// voice with it, and the two features are not the same promise. This module reaches into the
/// avatars through one read-only accessor
/// (<c>NetAvatarDriver.TryGetPeerHeadHolder</c>) and degrades to plain 2D voice when there are no
/// avatars — which is also exactly what happens for a flat-screen teammate, so the code path gets
/// exercised either way rather than being a branch nobody ever takes.</para>
///
/// <para><b>IT IS ALSO DELIBERATELY NOT ON <c>SkyAlternative</c>'s environment step</b>, which is
/// where <c>EnvSound</c> hangs. That step is only reached on the mixed-reality-OFF branch and is
/// stood down entirely for the <c>Default</c>/<c>OffBlack</c> environment styles; a teammate's
/// voice must not stop being spatial because the player turned on passthrough or chose a plain
/// backdrop.</para>
///
/// <para><b>NO FRAME-ORDER LOCK ENTRY.</b> The tick reads the avatars' current transforms and writes
/// only its own AudioSources, so it has no ordering adjacency with anything: a frame late is a
/// frame of position lag on a sound source, which is inaudible. Freezing an order the code does not
/// depend on is a behaviour change wearing a tidy-up costume
/// (<c>.planning/refactor/FRAME-ORDER.lock</c>'s own words), so no marker is added.</para>
/// </summary>
internal sealed class VoiceModule : IVRModule
{
    public string Name => "Voice";

    private static ConfigFile? _config;
    private GameObject? _driverGo;

    /// <summary>Master switch. Off = the game's voice chat behaves exactly as it does without the
    /// mod: still audible, still 2D, still on its own options page. LIVE.</summary>
    internal static ConfigEntry<bool> Enabled = null!;

    /// <summary>How 3D a peer's voice is. 1 = fully positional; 0 = the vanilla non-positional
    /// voice. Between the two, Unity crossfades, which is a usable comfort setting for anyone who
    /// finds a fully placed voice harder to follow. LIVE.</summary>
    internal static ConfigEntry<float> SpatialBlend = null!;

    /// <summary>PERCEIVED metres of flat, full-volume plateau around a talker. Inside it, moving
    /// your head does not change their level at all — this is the region conversation happens in
    /// and it is flat on purpose. LIVE.</summary>
    internal static ConfigEntry<float> FullLevelMeters = null!;

    /// <summary>PERCEIVED metres at which a voice reaches exactly zero. LIVE.</summary>
    internal static ConfigEntry<float> SilenceMeters = null!;

    /// <summary>Falloff exponent between the plateau and silence. 1 is a straight line; higher
    /// holds the level up longer and drops faster at the end. LIVE.</summary>
    internal static ConfigEntry<float> RolloffShape = null!;

    /// <summary>Angular width of a voice, in degrees. 0 is a pinpoint that hard-pans when a peer
    /// stands beside you; 180 is fully diffuse and unlocatable. LIVE.</summary>
    internal static ConfigEntry<float> Spread = null!;

    /// <summary>The user's "(deaktivierbar)": the loudspeaker badge in the corner of a peer's Steam
    /// picture. LIVE.</summary>
    internal static ConfigEntry<bool> SpeakingBadge = null!;

    /// <summary>Badge size as a fraction of the Steam avatar quad. LIVE.</summary>
    internal static ConfigEntry<float> BadgeScale = null!;

    internal static void BindConfig()
    {
        if (_config != null)
            return;
        _config = ModuleConfig.Create("voice");

        Enabled = _config.Bind("Voice", "Enabled", Defaults.Voice_Enabled,
            "Make the game's own voice chat SPATIAL in VR: a teammate's voice comes out of their "
            + "mask instead of from everywhere at once. This does not add, replace or configure a "
            + "voice chat — Gloomhaven ships one over Photon and it keeps its own options page, its "
            + "own per-user mute and its own push-to-talk. Off = that voice chat, untouched.");

        SpatialBlend = _config.Bind("Voice", "SpatialBlend", Defaults.VoiceSpatialBlend,
            new ConfigDescription(
                "How positional a teammate's voice is. 1 = fully placed at their mask. 0 = the "
                + "vanilla non-positional voice. Values in between crossfade, which helps if a fully "
                + "placed voice is harder for you to follow than a centred one.",
                new AcceptableValueRange<float>(0f, 1f)));

        FullLevelMeters = _config.Bind("Voice", "FullLevelMeters", Defaults.VoiceFullLevelMeters,
            new ConfigDescription(
                "Radius in REAL metres inside which a teammate is at full volume and moving your "
                + "head changes nothing. Raise it if voices seem to pump as people shift about; lower "
                + "it if you want distance to matter sooner.",
                new AcceptableValueRange<float>(0.25f, 10f)));

        SilenceMeters = _config.Bind("Voice", "SilenceMeters", Defaults.VoiceSilenceMeters,
            new ConfigDescription(
                "Distance in REAL metres at which a teammate's voice fades to nothing. Well beyond "
                + "any table, so it only bites when somebody has walked away across the room.",
                new AcceptableValueRange<float>(2f, 60f)));

        RolloffShape = _config.Bind("Voice", "RolloffShape", Defaults.VoiceRolloffShape,
            new ConfigDescription(
                "How the volume falls off between the full-volume radius and silence. 1 is a "
                + "straight line. Above 1 keeps speech loud longer and then drops away quickly; "
                + "below 1 makes distance bite immediately.",
                new AcceptableValueRange<float>(0.25f, 4f)));

        Spread = _config.Bind("Voice", "Spread", Defaults.VoiceSpread,
            new ConfigDescription(
                "How wide a voice is, in degrees. 0 makes it a pinpoint that jumps hard to one ear "
                + "when somebody stands beside you; large values make it impossible to tell where a "
                + "voice came from. The default is a compromise that stays locatable and comfortable.",
                new AcceptableValueRange<float>(0f, 180f)));

        SpeakingBadge = _config.Bind("Voice", "SpeakingBadge", Defaults.VoiceSpeakingBadge,
            "Show a small loudspeaker in the corner of a teammate's picture above their head while "
            + "they are talking, with the arcs lighting up as they get louder. Needs the name tags "
            + "themselves ([Net] NameTags); with those off there is no picture to draw it on and the "
            + "voice is still spatial.");

        BadgeScale = _config.Bind("Voice", "BadgeScale", Defaults.VoiceBadgeScale,
            new ConfigDescription(
                "Size of that loudspeaker as a fraction of the picture it sits on. Larger is easier "
                + "to see across a table; too large and it covers the face.",
                new AcceptableValueRange<float>(0.15f, 0.8f)));
    }

    public void Init()
    {
        BindConfig();

        _driverGo = new GameObject("GloomhavenVR.VoiceSpatial");
        Object.DontDestroyOnLoad(_driverGo);
        _driverGo.hideFlags = HideFlags.HideAndDontSave;
        _driverGo.AddComponent<VoiceDriver>();

        VRLog.Info(Name, $"Spatial voice installed (enabled={Enabled.Value}). It READS the game's own voice "
                         + "chat and moves each peer's AudioSource onto that peer's mask; it installs no Harmony "
                         + "patch, never writes a volume or a mute, and never touches Photon or Bolt. With voice "
                         + "chat not running this costs one static property read per frame.");
    }

    public void Shutdown()
    {
        VoiceSpatial.StandDown("the mod is shutting down");
        if (_driverGo != null)
        {
            Object.Destroy(_driverGo);
            _driverGo = null;
        }
    }
}

/// <summary>
/// The one MonoBehaviour: pumps <see cref="VoiceSpatial.Tick"/> once a frame inside the project's
/// throw-isolating guard, so a defect in here can cost this feature and nothing else.
/// </summary>
internal sealed class VoiceDriver : MonoBehaviour
{
    private static readonly System.Action Step = () => VoiceSpatial.Tick(Time.unscaledDeltaTime);

    private void Update()
    {
        using (PerfMonitor.Scope("Voice.Spatial"))
        {
            TickGuard.Run("Voice.Spatial", Step, VoiceChatBridge.Scope);
        }
    }

    private void OnDestroy()
    {
        VoiceSpatial.StandDown("the voice driver was destroyed");
    }
}
