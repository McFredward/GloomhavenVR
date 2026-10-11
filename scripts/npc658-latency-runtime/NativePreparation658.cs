using System;
using System.Collections;
using System.IO;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;

public static partial class MirrorProgram
{
    private static IEnumerator NativePreparation658()
    {
        TownServiceMirror.Shutdown(); Baselines.Clear();
        Transform widget = Source(Go("658 first map owner").transform);
        NativeTemplates.FixtureInstallFrozenBank625(Go("658 initial inactive bank"), widget, 65812);
        bool active = MapRoomDriver.Active;
        try
        {
            NativeTemplates.Ready = false;
            WorldUIConfig.ImmersiveTownServices.Value = false;
            TownServicePopulation.HasRemoteVisitors = false;
            NativeTemplates.Initializations658 = 0;
            MapRoomDriver.Active = false;
            TownServiceSync.FixturePrepare658();
            Check(NativeTemplates.Initializations658 == 0,
                "map preparation remains bounded to the active map lifecycle");
            MapRoomDriver.Active = true;
            TownServiceSync.FixturePrepare658();
            Check(NativeTemplates.Initializations658 == 1 && NativeTemplates.Ready,
                "immersive-off first map prepares originals before any remote visitor");
            Check(NativeTemplateStore625("NativeTemplateBases").Count == 1,
                "first Initialize return starts bounded native original preparation immediately");
            Check(!widget.gameObject.activeInHierarchy,
                "first original preparation never activates its hidden native bank");
            Check(TownServiceNativeAssets.Ticks658 == 1 && NativeTemplates.Purses658 == 1
                && NetAvatarDriver.MerchantPreparation658 == 1,
                "ordinary first map preparation retains all existing original preparation calls");
            for (int frame = 0; frame < 16; frame++)
            { TownServiceSync.FixturePrepare658(); yield return null; }
            Check(NativeTemplateStore625("NativeTemplateBases").Count == 3,
                "subsequent bounded map turns prepare widget and selected public card originals");
            Check(!widget.gameObject.activeInHierarchy,
                "completed preparation leaves the original graphics inactive");
            File.WriteAllText(Path.Combine(_output, "native-preparation658.txt"),
                "Actual PrepareCore + first Initialize return tail + unchanged complete EnhancementPreparation.\n"
                + "Local immersion off, no remote visitors, inactive bank: first turn one base; bounded later turns three bases.\n"
                + "Native bank/pool availability is an explicit fixture port; full original bank cloning and game controllers are not run.\n");
        }
        finally
        {
            MapRoomDriver.Active = active;
            WorldUIConfig.ImmersiveTownServices.Value = true;
            NativeTemplates.FixtureClearFrozenBank625();
        }
    }
}

namespace GloomhavenVR.WorldUI
{
    internal static partial class NativeTemplates
    {
        private static bool _ready;
        internal static int Initializations658, Purses658;
        internal static bool Initialize()
        {
            Initializations658++;
            bool result = FixtureInitialReturn658();
            Ready = _ready;
            return result;
        }
        internal static void PreparePhysicalPurses() { Purses658++; }
    }
    internal static partial class TownServiceNativeAssets
    {
        internal static int Ticks658;
        internal static void Tick() { Ticks658++; }
    }
    internal sealed partial class TownServiceSync
    {
        private float _prepareAfter;
        internal static void FixturePrepare658() => Private.PrepareCore();
    }
    internal static partial class WorldUIConfig
    { internal static readonly TownAudioPreference ImmersiveTownServices = new(); }
    internal static partial class TownServicePopulation
    { internal static bool HasRemoteVisitors; }
}
namespace GloomhavenVR.Net
{
    internal sealed partial class NetAvatarDriver
    {
        internal static int MerchantPreparation658;
        internal static void PrepareMerchantCardsForLoading() { MerchantPreparation658++; }
    }
}
