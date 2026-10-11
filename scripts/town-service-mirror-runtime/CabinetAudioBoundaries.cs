// Only native preferences and game clip lookup are boundaries. The emitter lifetime,
// rack capture, codec, hierarchy validation and observer playback are production code.
public sealed class GlobalData { public float MasterVolume = 100f, SFXVolume = 100f; }
public sealed class SaveData
{
    public static SaveData? Instance = new();
    public GlobalData Global = new();
}
namespace GloomhavenVR.WorldUI
{
    internal sealed class TownAudioPreference { internal bool Value = true; }
    internal static class WorldUIConfig
    { internal static readonly TownAudioPreference ImmersiveTownSoundEffects = new(); }
}
