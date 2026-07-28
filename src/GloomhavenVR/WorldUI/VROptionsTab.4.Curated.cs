using System.Collections.Generic;
using GloomhavenVR.Core;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// The curated everyday view: six categories of settings a player actually reaches for, chosen by
/// hand, plus "Erweitert" which still reaches every one of the mod's ~440 entries.
///
/// <para>WHY CURATION IS AN EXPLICIT LIST AND NOT A RULE. Every mechanical shortcut tried on this
/// data fails on real entries. Sorting by topic leaves migration flags like
/// <c>TableScaleDefault25Applied</c> and <c>BoardScaleDefault04Applied</c> sitting between
/// "Free Movement" and "Turn Mode" — internal bookkeeping the player must never touch, with names
/// that look like settings. Filtering on a key-name pattern would take <c>DebugGizmos</c> out and
/// leave <c>SavedScaleMultiplier</c> in. The catalog's <c>Pin</c> rank covers only the [Perf]
/// section. So the everyday list is written down, one entry at a time, and everything not on it
/// stays reachable under "Erweitert" rather than being hidden.</para>
///
/// <para>NOTHING IS LOST BY BEING LEFT OFF. That is what makes an explicit list safe here: the
/// advanced view is the complete catalog, so a curation mistake costs one extra click, never
/// access to a setting.</para>
/// </summary>
internal static partial class VROptionsTab
{
    /// <summary>One everyday category: a caption and the entries it shows, in the order shown.</summary>
    internal sealed class CuratedCategory
    {
        internal string LocKey = string.Empty;
        internal (string Section, string Key)[] Entries = System.Array.Empty<(string, string)>();

        internal string Label => Loc.Mod(LocKey);
    }

    /// <summary>
    /// Order is deliberate: what a player changes on the first day first (how they move, how their
    /// hands behave), then how it looks, then the table, then the things touched once — multiplayer
    /// identity and start-up.
    /// </summary>
    internal static readonly CuratedCategory[] Curated =
    {
        new()
        {
            LocKey = "vr_cat_comfort",
            Entries = new[]
            {
                ("Comfort", "FreeMovement"),
                ("Comfort", "TurnMode"),
                ("Comfort", "SnapTurnDegrees"),
                ("Comfort", "SmoothTurnSpeed"),
                ("Comfort", "TurnHand"),
                ("Comfort", "WorldGrabEnabled"),
                ("Comfort", "VerticalDrag"),
                ("Comfort", "RotateEnabled"),
                ("Comfort", "ScaleEnabled"),
                ("Comfort", "TableHeightOffset"),
                ("Comfort", "RecenterHoldSeconds"),
                ("Rig", "WorldScale"),
                ("Rig", "WorldTiltDegrees"),
            },
        },
        new()
        {
            LocKey = "vr_cat_hands",
            Entries = new[]
            {
                ("Hands", "PrimaryHand"),
                ("Hands", "HandStyle"),
                ("Hands", "HandColor"),
                ("Hands", "RayAlwaysOn"),
                ("Hands", "LaserFingerOrigin"),
                ("Hands", "ModalRayConeDegrees"),
                ("Hands", "HandForwardOffset"),
                ("Hands", "HandVerticalOffset"),
                ("Hands", "HandLateralOffset"),
                ("Hands", "GripPitchOffsetDegrees"),
            },
        },
        new()
        {
            LocKey = "vr_cat_view",
            Entries = new[]
            {
                ("Compat", "DisablePostProcessing"),
                ("Compat", "DisableVolumetricFog"),
                ("Compat", "WallFade"),
                ("Rig", "ForwardRendering"),
                ("Rig", "VoidColor"),
                ("Rig", "MenuRig"),
                ("Rig", "SpawnInCircle"),
            },
        },
        new()
        {
            LocKey = "vr_cat_table",
            Entries = new[]
            {
                ("Cards", "Board"),
                ("Cards", "TrayScale"),
                ("Cards", "TrayFollow"),
                ("Cards", "InspectScale"),
                ("Cards", "RevealMode"),
                ("Cards", "GrabButton"),
                ("WorldUI", "CombatLog"),
                ("WorldUI", "ActorBars"),
                ("WorldUI", "ActionElementHints"),
                ("WorldUI", "ButtonCluster"),
                ("WorldUI", "Dialogs"),
            },
        },
        new()
        {
            LocKey = "vr_cat_net",
            Entries = new[]
            {
                ("Net", "Enabled"),
                ("Net", "MirrorEnabled"),
                ("Net", "MaskId"),
                ("Net", "MaskSize"),
                ("Net", "RemoteBoards"),
            },
        },
        new()
        {
            LocKey = "vr_cat_system",
            Entries = new[]
            {
                ("Core", "EnableGraphicsJobs"),
                ("Core", "AutoRestartForGraphicsJobs"),
                ("Core", "RuntimePriority"),
                ("General", "Enabled"),
            },
        },
    };

    /// <summary>
    /// Section+Key → the catalog entry, rebuilt whenever the catalog is. Section and key alone
    /// are enough: the mod's config files do not repeat a section name between them.
    /// </summary>
    private static readonly Dictionary<string, ConfigCatalog.ConfigItem> ByKey = new(512);

    private static int _lookupSignature = -1;

    /// <summary>
    /// Refresh the lookup when the catalog has changed. Keyed on the entry count rather than a
    /// dirty flag, because the catalog rebuilds itself on its own schedule and the tab only ever
    /// sees the result.
    /// </summary>
    private static void EnsureLookup()
    {
        if (_lookupSignature == ConfigCatalog.TotalEntries && ByKey.Count > 0)
            return;

        ByKey.Clear();
        for (int t = 0; t < ConfigCatalog.TopicCount; t++)
        {
            IReadOnlyList<ConfigCatalog.ConfigGroup> groups = ConfigCatalog.Groups((ConfigCatalog.ConfigTopic)t);
            for (int g = 0; g < groups.Count; g++)
            {
                List<ConfigCatalog.ConfigItem> items = groups[g].Items;
                for (int i = 0; i < items.Count; i++)
                {
                    string id = Id(items[i].Section, items[i].Key);
                    if (!ByKey.ContainsKey(id))
                        ByKey[id] = items[i];
                }
            }
        }

        _lookupSignature = ConfigCatalog.TotalEntries;
    }

    private static string Id(string section, string key) => section + "/" + key;

    /// <summary>
    /// Look one curated entry up. A miss is REPORTED, not silently skipped: it means a setting was
    /// renamed or removed and the everyday list has quietly lost a row — exactly the kind of decay
    /// that is invisible until a player goes looking for something that used to be there.
    /// </summary>
    private static ConfigCatalog.ConfigItem? Lookup(string section, string key)
    {
        EnsureLookup();
        if (ByKey.TryGetValue(Id(section, key), out ConfigCatalog.ConfigItem item))
            return item;

        VRLog.Warn("WorldUI", $"VR options tab: curated entry [{section}] {key} no longer exists — "
                              + "the row is skipped. It was renamed or removed; the curated list in "
                              + "VROptionsTab.4.Curated.cs needs updating.");
        return null;
    }
}
