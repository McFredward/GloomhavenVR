using BepInEx.Configuration;

namespace GloomhavenVR.Core;

/// <summary>
/// Opt-in app-owned output experiment inside GloomhavenVR. This is a manual transport test,
/// not a claim that Steam Link carries a usable passthrough mask. Never resume a saved test
/// automatically: its temporary full-eye image must always follow a fresh explicit request.
/// </summary>
internal static class SteamLinkMaskConfig
{
    private static ConfigFile? _file;

    internal static ConfigEntry<bool> PayloadTestEnabled = null!;

    internal static bool Requested => PayloadTestEnabled?.Value ?? false;

    internal static void Bind()
    {
        if (_file != null)
            return;

        _file = ModuleConfig.Create("steamlink");
        PayloadTestEnabled = _file.Bind("SteamLink", "PayloadTestEnabled",
            Defaults.SteamLinkPayloadTestEnabled,
            "Experimental Steam Link mask-transmission test. After closing VR options, replaces the game image with a diagnostic pattern without a time limit. Stop it with the switch, the ordinary menu button, or by reopening VR options. Does not enable passthrough. Off at every game start.");

        // Config files auto-save user edits. Clear a persisted request once at bootstrap,
        // not on every menu bind; otherwise reopening the catalog would consume a fresh request.
        SetRequested(false);
    }

    internal static void SetRequested(bool requested)
    {
        if (PayloadTestEnabled != null && PayloadTestEnabled.Value != requested)
            PayloadTestEnabled.Value = requested;
    }
}
