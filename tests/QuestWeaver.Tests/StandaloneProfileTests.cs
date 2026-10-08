using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using QuestWeaver;

internal static class StandaloneProfileTests
{
    internal static void Run(string projectRoot, Action<bool, string> check)
    {
        string managed = Path.Combine(projectRoot, "ressources/GH_Data/Managed");
        if (!File.Exists(Path.Combine(managed, "GH.Runtime.dll"))) return;
        string root = Path.Combine(Path.GetTempPath(), "quest-provider-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var identity in new[]
            {
                (provider: "epic", id: "d9ab87bb8d304442b401529011e1c4f6", account: 352981215u, label: "EpicGamesStore"),
                (provider: "gog", id: "13200510471900007", account: 253897492u, label: "GoGGalaxy")
            })
            {
                string profile = Path.Combine(root, identity.provider + ".json");
                byte[] digest = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity.provider + ":" + identity.id));
                uint account = (uint)digest[0] << 24 | (uint)digest[1] << 16 | (uint)digest[2] << 8 | digest[3];
                if (account == 0) account = 1;
                object declaration(string owner, int mask = 3) => new
                {
                    schema = 1, provider = identity.provider, providerId = owner, steamId = "0", accountId = account,
                    displayName = "Original " + identity.provider + " owner", isDummy = false,
                    dlcOwnership = new { schema = 1, provider = identity.provider, providerId = identity.id, appId = 780290,
                        ownedMask = mask, installedAppIds = new[] { 1809490, 1958560 } }
                };
                File.WriteAllText(profile, JsonSerializer.Serialize(declaration(identity.id)));
                OfflineProfile parsed = Standalone.ReadProfile(profile);
                check(parsed.SteamId == identity.id && parsed.Provider == identity.provider && parsed.OwnedDlcMask == 3,
                    "Original provider identity and DLC ownership are retained independently of Steam.");
                string output = Path.Combine(root, identity.provider);
                StandaloneReport report = Standalone.Write(managed, null, profile, output);
                var context = new AssemblyLoadContext("original-provider-" + identity.provider, isCollectible: true);
                context.Resolving += (_, name) =>
                {
                    string path = Path.Combine(output, name.Name + ".dll");
                    if (!File.Exists(path)) path = Path.Combine(managed, name.Name + ".dll");
                    return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
                };
                try
                {
                    Assembly assembly = context.LoadFromAssemblyPath(Path.Combine(output, "GH.Runtime.dll"));
                    Type user = assembly.GetType("PlatformUserData", true)!;
                    object nativeUser = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(user);
                    check((string)user.GetProperty("PlatformPlayerID")!.GetValue(nativeUser)! == identity.id,
                        "Actual original platform player getter returns the complete local provider ID.");
                    check((string)user.GetProperty("PlatformAccountID")!.GetValue(nativeUser)! == account.ToString(),
                        "Actual original local save account uses its separate bounded account ID.");
                    Type platform = assembly.GetType("PlatformLayer", true)!;
                    object nativePlatform = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(platform);
                    check((string)platform.GetProperty("PlatformID")!.GetValue(nativePlatform)! == identity.label,
                        "Actual original platform metadata uses the original protocol's provider label.");
                    check(!(bool)platform.GetProperty("IsValid")!.GetValue(nativePlatform)!, "Local baked profile never asserts a live store session.");
                    check(report.UnchangedTypesVerified > 4000 && !report.FullGameReady, "Metadata alone never grants full-game readiness.");
                }
                finally { context.Unload(); }
                File.WriteAllText(profile, JsonSerializer.Serialize(declaration(identity.id + "0")));
                try { Standalone.ReadProfile(profile); check(false, "Mismatched provider ownership was accepted."); }
                catch (InvalidDataException) { check(true, "Provider ownership mismatch rejected."); }
                File.WriteAllText(profile, JsonSerializer.Serialize(declaration(identity.id, 1)));
                try { Standalone.ReadProfile(profile); check(false, "Inconsistent provider DLC mask was accepted."); }
                catch (InvalidDataException) { check(true, "Provider DLC mask mismatch rejected."); }
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
