using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using Mono.Cecil;
using QuestWeaver;

internal static class DlcTests
{
    internal static void Run(string projectRoot, Action<bool, string> check)
    {
        string managed = Path.Combine(projectRoot, "ressources/GH_Data/Managed");
        if (!File.Exists(Path.Combine(managed, "GH.Runtime.dll"))) return;
        string temp = Path.Combine(Path.GetTempPath(), "quest-dlc-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string profile = Path.Combine(temp, "profile.json"), output = Path.Combine(temp, "adapted");
            object Identity(object ownership) => new { schema = 1, provider = "steam", displayName = "DUMMY DLC fixture", steamId = "0", accountId = 0, isDummy = true, dlcOwnership = ownership };
            object Owned(int mask, int[] ids, string id = "0") => new { schema = 1, provider = "steam", appId = 780290, steamId = id, ownedMask = mask, installedAppIds = ids };
            File.WriteAllText(profile, JsonSerializer.Serialize(Identity(Owned(3, new[] { 1809490, 1958560 }))));
            StandaloneReport report = Standalone.Write(managed, null, profile, output);
            using (var before = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "GH.Runtime.dll")))
            using (var after = AssemblyDefinition.ReadAssembly(Path.Combine(output, "GH.Runtime.dll")))
            {
                TypeDefinition source = before.MainModule.GetType("PlatformDLC"), actual = after.MainModule.GetType("PlatformDLC");
                foreach (string name in new[] { "UserInstalledDLC", "OpenPlatformStoreDLCOverlay" })
                {
                    source.Methods.Remove(source.Methods.Single(m => m.Name == name));
                    actual.Methods.Remove(actual.Methods.Single(m => m.Name == name));
                }
                check(ProtectedTypes.Fingerprint(source) == ProtectedTypes.Fingerprint(actual),
                    "Original DLC playability/file, party/save validation and promotional asset methods remain unchanged.");
                check(report.UnchangedTypesVerified > 4000 && report.ProtectedTypesVerified > 0, "DLC platform adaptation retains protected/unrelated verification.");
            }
            var context = new AssemblyLoadContext("actual-offline-dlc-fixture", isCollectible: true);
            context.Resolving += (_, name) =>
            {
                string file = Path.Combine(output, name.Name + ".dll");
                if (!File.Exists(file)) file = Path.Combine(managed, name.Name + ".dll");
                return File.Exists(file) ? context.LoadFromAssemblyPath(file) : null;
            };
            try
            {
                Type dlc = context.LoadFromAssemblyPath(Path.Combine(output, "GH.Runtime.dll")).GetType("PlatformDLC", true)!;
                object instance = RuntimeHelpers.GetUninitializedObject(dlc);
                MethodInfo installed = dlc.GetMethod("UserInstalledDLC")!, owns = dlc.GetMethod("UserOwnsDLC")!;
                Type flag = installed.GetParameters()[0].ParameterType;
                foreach (int value in new[] { 0, 1, 2, 3, 4, 7, 8, 128, -1 })
                {
                    bool expected = value == 1 || value == 2;
                    object key = Enum.ToObject(flag, value);
                    check((bool)installed.Invoke(instance, new[] { key })! == expected, "Baked installed DLC flag " + value);
                    check((bool)owns.Invoke(instance, new[] { key })! == expected, "Original ownership delegates to baked availability " + value);
                }
                dlc.GetMethod("OpenPlatformStoreDLCOverlay")!.Invoke(instance, new[] { Enum.ToObject(flag, 4) });
                check(true, "Unavailable DLC purchase has no native store call or exception.");
            }
            finally { context.Unload(); }
            foreach (object invalid in new[] { Owned(3, new[] { 1809490 }), Owned(8, new[] { 2584170 }), Owned(1, new[] { 1809490, 1809490 }), Owned(1, new[] { 480 }), Owned(3, new[] { 1809490, 1958560 }, "1") })
            {
                File.WriteAllText(profile, JsonSerializer.Serialize(Identity(invalid)));
                try { Standalone.ReadProfile(profile); throw new InvalidOperationException("Invalid DLC declaration accepted."); }
                catch (InvalidDataException) { check(true, "Invalid/mismatched offline DLC declaration rejected."); }
            }
        }
        finally { Directory.Delete(temp, true); }
    }
}
