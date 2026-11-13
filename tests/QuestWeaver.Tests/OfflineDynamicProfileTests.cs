using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;
using QuestWeaver;

internal static class OfflineDynamicProfileTests
{
    internal static void Run(string projectRoot, Action<bool, string> check)
    {
        string managed = Path.Combine(projectRoot, "ressources/GH_Data/Managed");
        if (!File.Exists(Path.Combine(managed, "GH.Runtime.dll"))) throw new InvalidOperationException("Actual owned game assemblies are required.");
        string root = Path.Combine(Path.GetTempPath(), "quest-dynamic-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string profile = Path.Combine(root, "profile.json"), output = Path.Combine(root, "game");
            File.WriteAllText(profile, JsonSerializer.Serialize(new { schema = 1, provider = "steam", steamId = "0", accountId = 0,
                displayName = "Original profile (DUMMY)", isDummy = true }));
            var report = Standalone.Write(managed, null, profile, output, target: "game");
            check(report.StartupAdapterComplete && report.UnchangedTypesVerified > 4000, "Actual game standalone conversion retains unrelated types.");
            using (var game = AssemblyDefinition.ReadAssembly(Path.Combine(output, "GH.Runtime.dll")))
            using (var original = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "GH.Runtime.dll")))
            using (var platforms = AssemblyDefinition.ReadAssembly(Path.Combine(output, "SM.Consoles.dll")))
            {
                foreach (var pair in new[] { ("get_UserName", "DisplayName"), ("get_PlatformPlayerID", "PlayerId"), ("get_PlatformAccountID", "AccountId") })
                {
                    var body = game.MainModule.GetType("PlatformUserData").Methods.Single(m => m.Name == pair.Item1).Body;
                    check(body.Instructions.Any(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference m
                        && m.DeclaringType.FullName == "QuestWeaver.Runtime.OfflineProfile" && m.Name == pair.Item2), "Actual original local identity getter uses the dynamic signed snapshot: " + pair.Item1);
                }
                foreach (var pair in new[] { ("GetUserNameForConnection", "get_Username"), ("GetPlatformIDForConnection", "get_PlatformPlayerID") })
                {
                    var body = game.MainModule.GetType("PlatformUserData").Methods.Single(m => m.Name == pair.Item1).Body;
                    check(body.Instructions.Any(i => i.OpCode == OpCodes.Brtrue), "Only null connection reaches the local signed identity.");
                    check(body.Instructions.Any(i => i.OpCode == OpCodes.Callvirt && i.Operand is MethodReference m && m.DeclaringType.FullName == "FFSNet.UserToken" && m.Name == pair.Item2), "Remote identity retains the original token getter.");
                }
                var users = platforms.MainModule.GetType("Platforms.Generic.PlatformInputGeneric").Methods.Single(m => m.Name == "BuildUnityUsers");
                check(users.Body.Instructions.Count(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "QuestWeaver.Runtime.OfflineProfile") == 2,
                    "Original generic user construction reads dynamic name and account ID.");
                var protectedBefore = ProtectedTypes.Snapshot(original); var protectedAfter = ProtectedTypes.Snapshot(game);
                check(protectedBefore.OrderBy(p => p.Key).SequenceEqual(protectedAfter.OrderBy(p => p.Key)), "Original rules, tokens and game network transport stay invariant.");
            }
            var context = new AssemblyLoadContext("actual-dynamic-profile", isCollectible: true);
            context.Resolving += (_, name) => name.Name == "QuestWeaver.Runtime" ? typeof(QuestWeaver.Runtime.OfflineProfile).Assembly
                : File.Exists(Path.Combine(output, name.Name + ".dll")) ? context.LoadFromAssemblyPath(Path.Combine(output, name.Name + ".dll"))
                : File.Exists(Path.Combine(managed, name.Name + ".dll")) ? context.LoadFromAssemblyPath(Path.Combine(managed, name.Name + ".dll")) : null;
            try
            {
                var assembly = context.LoadFromAssemblyPath(Path.Combine(output, "GH.Runtime.dll"));
                var type = assembly.GetType("PlatformUserData", true)!;
                var instance = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
                check((string)type.GetProperty("UserName")!.GetValue(instance)! == "Original profile (DUMMY)", "Legacy signed resource fallback stays available before the snapshot boundary.");
                QuestWeaver.Runtime.OfflineProfile.Initialize("Updated owner", "76561197960265729", "1", "Steam", 1);
                check((string)type.GetProperty("UserName")!.GetValue(instance)! == "Updated owner", "Actual woven original getter returns changed offline profile without weaving again.");
                check((string)type.GetProperty("PlatformPlayerID")!.GetValue(instance)! == "76561197960265729", "Full updated Steam ID is retained.");
                check((string)type.GetProperty("PlatformAccountID")!.GetValue(instance)! == "1", "Updated account ID remains separate.");
                check((string)type.GetMethod("GetUserNameForConnection")!.Invoke(instance, new object?[] { null })! == "Updated owner", "Null-connection local multiplayer identity uses the same snapshot.");
                Type dlc = assembly.GetType("PlatformDLC", true)!;
                object dlcOwner = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(dlc);
                var installed = dlc.GetMethod("UserInstalledDLC")!;
                var enumType = installed.GetParameters()[0].ParameterType;
                check((bool)installed.Invoke(dlcOwner, new[] { Enum.ToObject(enumType, 1) })!, "Actual original DLC getter uses the updated purchased mask.");
                check(!(bool)installed.Invoke(dlcOwner, new[] { Enum.ToObject(enumType, 2) })!, "Changing profile cannot retain another owner's baked DLC flags.");
                check(!(bool)installed.Invoke(dlcOwner, new[] { Enum.ToObject(enumType, 3) })!, "Unknown combined DLC key remains rejected as in the original mapping.");
                try { QuestWeaver.Runtime.OfflineProfile.Initialize("second", "2", "2", "Steam"); check(false, "Repeated identity initialization was accepted."); }
                catch (InvalidOperationException) { check(true, "A process cannot change profile after initialization."); }
                QuestWeaver.Runtime.OfflineProfile.Reject();
                try { QuestWeaver.Runtime.OfflineProfile.DisplayName("fallback"); check(false, "Rejected signed profile fell back silently."); }
                catch (InvalidOperationException) { check(true, "Invalid signed identity blocks local getters."); }
            }
            finally { context.Unload(); }
        }
        finally { Directory.Delete(root, true); }
    }
}
