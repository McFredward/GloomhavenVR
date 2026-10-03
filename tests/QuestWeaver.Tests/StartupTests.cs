using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using QuestWeaver;
using GloomhavenVR.Quest;

internal static class StartupTests
{
    internal static void Run(string projectRoot, Action<bool, string> check)
    {
        string temp = Path.Combine(Path.GetTempPath(), "quest-startup-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            string archive = Path.Combine(temp, "content.zip"), contentRoot = Path.Combine(temp, "content");
            byte[] payload = Encoding.UTF8.GetBytes("actual-file-payload\n");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            using (var entry = zip.CreateEntry("StreamingAssets/Rulebase/rules.dat").Open()) entry.Write(payload);
            string payloadPath = Path.Combine(temp, "payload"); File.WriteAllBytes(payloadPath, payload);
            var manifest = new QuestGameContentManifest { schema = 1, inputKey = "fixture", archive = "quest-startup-content.zip", archiveSha256 = QuestGameContent.Hash(archive), files = new[] {
                new QuestGameContentFile { path = "StreamingAssets/Rulebase/rules.dat", size = payload.Length, sha256 = QuestGameContent.Hash(payloadPath) } } };
            QuestGameContent.Validate(manifest, "fixture");
            check(!QuestGameContent.IsReady(manifest, contentRoot), "Missing content was declared ready.");
            QuestGameContent.Extract(manifest, archive, contentRoot);
            check(QuestGameContent.IsReady(manifest, contentRoot), "Verified content extraction did not become ready.");
            string extracted = QuestGameContent.ResolveVerifiedPath(manifest, contentRoot, manifest.files[0].path);
            DateTime before = File.GetLastWriteTimeUtc(extracted); QuestGameContent.Extract(manifest, archive, contentRoot);
            check(File.GetLastWriteTimeUtc(extracted) == before && File.ReadAllBytes(extracted).SequenceEqual(payload), "Valid file was needlessly replaced or changed.");
            File.WriteAllText(extracted, "corrupt");
            check(!QuestGameContent.IsReady(manifest, contentRoot), "Corrupt cached content was trusted.");
            QuestGameContent.Extract(manifest, archive, contentRoot);
            check(QuestGameContent.IsReady(manifest, contentRoot), "Corrupt cached content did not recover with verified original bytes.");
            Reject(() => QuestGameContent.Validate(manifest, "other-build"), "Cross-build content provenance accepted.");
            manifest.files[0].path = "../escape"; Reject(() => QuestGameContent.Extract(manifest, archive, contentRoot), "ZIP traversal accepted.");
            manifest.files[0].path = "StreamingAssets/Rulebase/rules.dat";
            string correctHash = manifest.archiveSha256; manifest.archiveSha256 = new string('0', 64);
            Reject(() => QuestGameContent.Extract(manifest, archive, contentRoot), "Wrong archive SHA accepted."); manifest.archiveSha256 = correctHash;
            string correctPayloadHash = manifest.files[0].sha256; manifest.files[0].sha256 = new string('0', 64);
            Reject(() => QuestGameContent.Extract(manifest, archive, contentRoot), "Wrong payload SHA accepted."); manifest.files[0].sha256 = correctPayloadHash;
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update)) zip.CreateEntry("unexpected.dat");
            manifest.archiveSha256 = QuestGameContent.Hash(archive);
            Reject(() => QuestGameContent.Extract(manifest, archive, contentRoot), "Unmanifested entry accepted.");
            check(!Directory.GetFiles(contentRoot, "*.tmp", SearchOption.AllDirectories).Any(), "Failed extraction leaked temporary files.");

            string managed = Path.Combine(projectRoot, "ressources/GH_Data/Managed");
            if (!File.Exists(Path.Combine(managed, "GH.Runtime.dll")))
            {
                Console.WriteLine("SKIP actual original platform/BepInEx execution: private owned-game assemblies are unavailable. Portable content and Harmony fixtures remain active.");
                return;
            }
            string profile = Path.Combine(temp, "profile.json");
            // Derive the low account bits rather than coupling the fixture to a typo.
            uint accountId = unchecked((uint)76561198000000000UL);
            File.WriteAllText(profile, System.Text.Json.JsonSerializer.Serialize(new { schema = 1, provider = "steam", steamId = "76561198000000000", accountId, displayName = "Fixture original owner", isDummy = false }));
            string gameHash = QuestGameContent.Hash(Path.Combine(managed, "GH.Runtime.dll"));
            string output = Path.Combine(temp, "adapted");
            StandaloneReport report = Standalone.Write(managed, null, profile, output);
            check(report.StartupAdapterComplete && !report.FullGameReady && !report.ModLifecycleComplete && !report.EosAuthorised, "Platform adapter falsely claimed full-game/mod/EOS readiness.");
            check(report.ProtectedTypesVerified == 7 && report.UnchangedTypesVerified > 4000, "Actual original-game protected and unrelated type invariance is incomplete.");
            check(QuestGameContent.Hash(Path.Combine(managed, "GH.Runtime.dll")) == gameHash, "Original input bytes changed.");
            using (var emitted = AssemblyDefinition.ReadAssembly(Path.Combine(output, "GH.Runtime.dll")))
            {
                MethodDefinition options = emitted.MainModule.GetType("GLOOM.MainMenu.MainOptionExtras").Methods.Single(m => m.Name == "BuildOptions");
                check(!options.Body.Instructions.Any(i => i.Operand is MethodReference c && c.Name == "get_ModdingSupported"), "Excluded original Workshop row was removed rather than retained.");
                MethodDefinition optionCtor = emitted.MainModule.GetType("GLOOM.MainMenu.MenuSuboption").Methods.Single(m => m.IsConstructor);
                check(optionCtor.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "GUI_MODDING") && optionCtor.Body.Instructions.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "onSelected"), "Workshop native callback was not excluded.");
            }
            using (var emitted = AssemblyDefinition.ReadAssembly(Path.Combine(output, "SM.Consoles.dll")))
            {
                var factory = emitted.MainModule.GetType("Platforms.Utils.PlatformConstructor").Methods.Single(m => m.Name == "BuildPlatform");
                check(factory.Body.Instructions.Any(i => i.OpCode == OpCodes.Newobj && i.Operand is MethodReference c && c.DeclaringType.FullName == "Platforms.Generic.PlatformGeneric")
                    && factory.Body.Instructions.Count == 7, "Original factory was not adapted to existing offline generic implementation.");
            }
            var context = new AssemblyLoadContext("actual-original-startup-boundary", isCollectible: true);
            context.Resolving += (_, name) =>
            {
                string path = Path.Combine(output, name.Name + ".dll");
                if (!File.Exists(path)) path = Path.Combine(managed, name.Name + ".dll");
                return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
            };
            try
            {
                Assembly game = context.LoadFromAssemblyPath(Path.Combine(output, "GH.Runtime.dll"));
                Type userType = game.GetType("PlatformUserData", true)!;
                object user = RuntimeHelpers.GetUninitializedObject(userType);
                check((string)userType.GetProperty("UserName")!.GetValue(user)! == "Fixture original owner", "Actual original user-name boundary returned a desktop name.");
                check((string)userType.GetProperty("PlatformPlayerID")!.GetValue(user)! == "76561198000000000", "Actual original full Steam ID was truncated.");
                check((string)userType.GetProperty("PlatformAccountID")!.GetValue(user)! == accountId.ToString(), "Actual original account ID differs from source account bits.");
                check((bool)userType.GetProperty("IsSignedIn")!.GetValue(user)!, "Actual local save-owner readiness was absent.");
                Type platformType = game.GetType("PlatformLayer", true)!;
                object platform = RuntimeHelpers.GetUninitializedObject(platformType);
                check(!(bool)platformType.GetProperty("IsValid")!.GetValue(platform)!, "Offline adapter faked a live Steam session.");
                check(!(bool)platformType.GetProperty("EOSInitialised")!.GetValue(null)!, "Offline adapter faked EOS authorization.");
                platformType.GetMethod("Initialize")!.Invoke(platform, new object?[] { null });
                check((bool)platformType.GetProperty("Initialised")!.GetValue(null)!, "Actual platform boundary did not signal local initialization.");
                check((string)platformType.GetProperty("SessionTicket")!.GetValue(platform)! == "", "Offline adapter manufactured a Steam ticket.");
            }
            finally { context.Unload(); }
            string bepPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget/packages/bepinex.baselib/5.4.20/lib/netstandard2.0/BepInEx.dll");
            string fixtureMod = Path.Combine(projectRoot, "tests/QuestWeaver.Tests/FixtureMod/bin", new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name, "net8.0/FixtureMod.dll");
            var bepReport = new StandaloneReport();
            string bepOutput = Path.Combine(temp, "BepInEx.dll"); BepInExStandalone.Write(bepPath, fixtureMod, bepOutput, bepReport);
            using (var bep = AssemblyDefinition.ReadAssembly(bepOutput))
            {
                check(!bep.MainModule.AssemblyReferences.Any(a => a.Name.StartsWith("Mono", StringComparison.Ordinal) || a.Name == "0Harmony"), "Standalone config/logger closure retained desktop detour dependencies.");
                check(!bep.MainModule.GetTypeReferences().Any(t => t.Namespace.StartsWith("System.Reflection.Emit", StringComparison.Ordinal)), "Standalone BepInEx retained runtime emit.");
                check(!bep.MainModule.Types.Any(t => t.FullName == "BepInEx.Bootstrap.Chainloader"), "Standalone BepInEx retained Chainloader.");
                check(bep.MainModule.Types[0].Name == "<Module>", "Standalone metadata lost the mandatory module type.");
            }
            var bepContext = new AssemblyLoadContext("actual-bepinex-config", isCollectible: true);
            bepContext.Resolving += (_, name) => File.Exists(Path.Combine(managed, name.Name + ".dll")) ? bepContext.LoadFromAssemblyPath(Path.Combine(managed, name.Name + ".dll")) : null;
            try
            {
                Assembly bep = bepContext.LoadFromAssemblyPath(bepOutput);
                string configRoot = Path.Combine(temp, "standalone-config"); Directory.CreateDirectory(Path.Combine(configRoot, "config"));
                bep.GetType("BepInEx.QuestStandalone", true)!.GetMethod("Initialize")!.Invoke(null, new object[] { configRoot });
                Type configType = bep.GetType("BepInEx.Configuration.ConfigFile", true)!;
                string configPath = Path.Combine(configRoot, "config/fixture.cfg");
                object config = Activator.CreateInstance(configType, configPath, false, null)!;
                MethodInfo bind = configType.GetMethods().Single(m => m.Name == "Bind" && m.IsGenericMethodDefinition && m.GetParameters().Length == 4 && m.GetParameters()[0].ParameterType == typeof(string) && m.GetParameters()[3].ParameterType == typeof(string)).MakeGenericMethod(typeof(float));
                object entry = bind.Invoke(config, new object[] { "Probe", "Scale", 1.5f, "fixture" })!;
                entry.GetType().GetProperty("Value")!.SetValue(entry, 2.75f);
                check(File.Exists(configPath) && File.ReadAllText(configPath).Contains("Scale = 2.75", StringComparison.Ordinal), "Original config persistence did not save a live value change.");
                object reloaded = Activator.CreateInstance(configType, configPath, false, null)!;
                object again = bind.Invoke(reloaded, new object[] { "Probe", "Scale", 1.5f, "fixture" })!;
                check((float)again.GetType().GetProperty("Value")!.GetValue(again)! == 2.75f, "Original config persistence did not round-trip.");
                object logger = bep.GetType("BepInEx.Logging.Logger", true)!.GetMethod("CreateLogSource")!.Invoke(null, new object[] { "Fixture standalone" })!;
                logger.GetType().GetMethod("LogInfo")!.Invoke(logger, new object[] { "actual logging implementation" });
                check((string)logger.GetType().GetProperty("SourceName")!.GetValue(logger)! == "Fixture standalone", "Original logger source was not retained.");
                check((string)bep.GetType("BepInEx.Paths", true)!.GetProperty("ConfigPath")!.GetValue(null)! == Path.Combine(configRoot, "config"), "Standalone paths did not preserve per-module configuration root.");
            }
            finally { bepContext.Unload(); }
            string unknownMod = Path.Combine(temp, "unknown-bep-api.dll");
            using (var unknown = AssemblyDefinition.ReadAssembly(fixtureMod))
            {
                var scope = new AssemblyNameReference("BepInEx", new Version(5, 4, 20, 0)); unknown.MainModule.AssemblyReferences.Add(scope);
                var type = new TypeReference("BepInEx.Configuration", "ConfigFile", unknown.MainModule, scope);
                var api = new MethodReference("UnsupportedFutureApi", unknown.MainModule.TypeSystem.Void, type);
                var caller = new MethodDefinition("UnknownBepApi", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, unknown.MainModule.TypeSystem.Void);
                unknown.MainModule.Types.First(t => t.Name == "Entry").Methods.Add(caller);
                caller.Body.GetILProcessor().Emit(OpCodes.Call, api); caller.Body.GetILProcessor().Emit(OpCodes.Ret); unknown.Write(unknownMod);
            }
            Reject(() => BepInExStandalone.Write(bepPath, unknownMod, Path.Combine(temp, "bad-bep.dll"), new StandaloneReport()), "Unknown current-mod BepInEx API accepted.");
            Reject(() => Standalone.Write(output, null, profile, Path.Combine(temp, "repeat")), "Already adapted input accepted.");
            string wrongProfile = Path.Combine(temp, "wrong-profile.json"); File.WriteAllText(wrongProfile, File.ReadAllText(profile).Replace(accountId.ToString(), "1"));
            Reject(() => Standalone.ReadProfile(wrongProfile), "Inconsistent embedded account accepted.");
            Console.WriteLine("Actual original platform getters/initialization executed; factory and 4,000+ unrelated types serialized and verified. This is not a Unity menu/device proof.");

            void Reject(Action action, string message)
            { try { action(); check(false, message); } catch (InvalidDataException) { check(true, message + " rejected"); } }
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
}
