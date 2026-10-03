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
            var button = new QuestGameButtonGate();
            check(!button.Step(false, true) && !button.Step(true, true), "Unavailable/startup-held diagnostic action was accepted.");
            check(!button.Step(true, false) && button.Step(true, true) && !button.Step(true, true), "Tracked neutral-to-press action was missed or repeated while held.");
            check(!button.Step(false, false) && !button.Step(true, true) && !button.Step(true, true), "Tracking resume replayed a held diagnostic action.");
            check(!button.Step(true, false) && button.Step(true, true), "Fresh press after tracking recovery failed.");
            button.Reset(); check(!button.Step(true, true) && !button.Step(true, false) && button.Step(true, true), "Lifecycle reset failed to require a fresh neutral sample.");
            string logPath = Path.Combine(temp, "quest-startup.log");
            // Exercise actual file persistence and the previous-run cap, including an
            // unbounded log produced by an older version of the diagnostic adapter.
            File.WriteAllText(logPath, new string('x', QuestGameStartupLog.MaxBytes * 2));
            var startupLog = new QuestGameStartupLog(logPath, "fixture build/input");
            check(new FileInfo(Path.ChangeExtension(logPath, ".previous.log")).Length == QuestGameStartupLog.MaxBytes, "Legacy previous startup log was not bounded.");
            check(File.ReadAllText(logPath).Contains("run fixture build/input", StringComparison.Ordinal) && new FileInfo(logPath).Length < 256, "Per-run startup log was not truncated/stamped.");
            startupLog.Append("first gate", "actual exception stack"); startupLog.Append("first gate", "duplicate stack");
            check(File.ReadAllText(logPath).Split("first gate").Length == 2 && File.ReadAllText(logPath).Contains("actual exception stack", StringComparison.Ordinal), "Startup gate deduplication lost the first exception or repeated records.");
            startupLog.Append(new string('漢', 20000), new string('界', 20000));
            check(new FileInfo(logPath).Length < QuestGameStartupLog.MaxBytes && File.ReadAllText(logPath).Contains("[truncated]", StringComparison.Ordinal), "Unicode exception diagnostics were not clipped/bounded.");
            for (int i = 0; i < QuestGameStartupLog.MaxRecords * 2; i++) startupLog.Append("transition " + i);
            long cappedSize = new FileInfo(logPath).Length;
            check(cappedSize <= QuestGameStartupLog.MaxBytes && File.ReadAllText(logPath).Contains("log limit reached", StringComparison.Ordinal), "Startup record limit was not recorded or file exceeded byte bound.");
            startupLog.Append("after cap");
            check(new FileInfo(logPath).Length == cappedSize && File.ReadAllText(logPath).Contains("run fixture build/input", StringComparison.Ordinal), "Capped startup log grew or lost boot provenance.");
            new QuestGameStartupLog(logPath, "next run");
            check(File.ReadAllText(logPath).Contains("next run", StringComparison.Ordinal) && !File.ReadAllText(logPath).Contains("first gate", StringComparison.Ordinal)
                && File.ReadAllText(Path.ChangeExtension(logPath, ".previous.log")).Contains("fixture build/input", StringComparison.Ordinal), "Run rotation lost previous boot evidence or appended across runs.");
            string byteLogPath = Path.Combine(temp, "byte-cap.log"); var byteLog = new QuestGameStartupLog(byteLogPath, "byte-cap");
            for (int i = 0; i < 100; i++) byteLog.Append(i + new string('漢', 4096), new string('界', 8192));
            check(new FileInfo(byteLogPath).Length <= QuestGameStartupLog.MaxBytes && File.ReadAllText(byteLogPath).Contains("log limit reached", StringComparison.Ordinal), "Byte cap failed before the record cap for long Unicode exceptions.");
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
            RulesBoundaryTests.Run(managed, output, profile, temp, check);
            check(report.StartupAdapterComplete && !report.FullGameReady && !report.ModLifecycleComplete && !report.EosAuthorised && !report.VoiceNativeAvailable, "Platform adapter falsely claimed full-game/mod/EOS/voice readiness.");
            check(report.ProtectedTypesVerified == 7 && report.UnchangedTypesVerified > 4000, "Actual original-game protected and unrelated type invariance is incomplete.");
            check(QuestGameContent.Hash(Path.Combine(managed, "GH.Runtime.dll")) == gameHash, "Original input bytes changed.");
            using (var emitted = AssemblyDefinition.ReadAssembly(Path.Combine(output, "GH.Runtime.dll")))
            {
                MethodDefinition options = emitted.MainModule.GetType("GLOOM.MainMenu.MainOptionExtras").Methods.Single(m => m.Name == "BuildOptions");
                check(!options.Body.Instructions.Any(i => i.Operand is MethodReference c && c.Name == "get_ModdingSupported"), "Excluded original Workshop row was removed rather than retained.");
                MethodDefinition optionCtor = emitted.MainModule.GetType("GLOOM.MainMenu.MenuSuboption").Methods.Single(m => m.IsConstructor);
                check(optionCtor.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "GUI_MODDING") && optionCtor.Body.Instructions.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "onSelected"), "Workshop native callback was not excluded.");
                MethodDefinition voiceUi = emitted.MainModule.GetType("VoiceChat.VoceChatOptions").Methods.Single(m => m.Name == "SwitchStatus");
                check(voiceUi.Body.Instructions.Count == 3 && voiceUi.Body.Instructions[0].OpCode == OpCodes.Ldstr && ((string)voiceUi.Body.Instructions[0].Operand).Contains("no voice room connection attempted", StringComparison.Ordinal)
                    && voiceUi.Body.Instructions[1].Operand is MethodReference log && log.Name == "LogWarning" && voiceUi.Body.Instructions[2].OpCode == OpCodes.Ret,
                    "Original voice opt-in UI did not fail explicitly before native-room entry.");
                TypeDefinition loader = emitted.MainModule.GetType("ApparanceResourceListLoader");
                MethodDefinition unload = loader.Methods.Single(m => m.Name == "UnloadAll");
                check(!unload.Body.Instructions.Any(i => i.Operand is MethodReference c && c.DeclaringType.FullName == "ApparanceEngine" && c.Name == "RefreshResources")
                    && unload.Body.Instructions.Any(i => i.Operand is MethodReference c && c.Name == "ReleaseAsset")
                    && unload.Body.Instructions.Count(i => i.Operand is MethodReference c && c.Name == "Clear") == 2,
                    "Startup unload still entered native Apparance or lost managed asset/collection cleanup.");
                using var original = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "GH.Runtime.dll"));
                TypeDefinition originalLoader = original.MainModule.GetType("ApparanceResourceListLoader");
                Instruction originalRefresh = originalLoader.Methods.Single(m => m.Name == "UnloadAll").Body.Instructions.Single(i => i.Operand is MethodReference c && c.DeclaringType.FullName == "ApparanceEngine" && c.Name == "RefreshResources");
                originalRefresh.OpCode = OpCodes.Pop; originalRefresh.Operand = null;
                check(ProtectedTypes.Fingerprint(loader) == ProtectedTypes.Fingerprint(originalLoader), "Resource loader changed beyond the one scoped native-refresh call.");
                foreach (string nativeKeyboardType in new[] { "UIKeyboard", "Script.GUI.Controller.ControllerInputKeyboard", "Script.GUI.Controller.Keyboard.UIKeyboardKey" })
                    check(ProtectedTypes.Fingerprint(emitted.MainModule.GetType(nativeKeyboardType)) == ProtectedTypes.Fingerprint(original.MainModule.GetType(nativeKeyboardType)),
                        "Original keyboard implementation was forked or changed: " + nativeKeyboardType);
                TypeDefinition keyboardProcessor = emitted.MainModule.GetType("Script.GUI.Controller.ControllerInputKeyboard");
                MethodDefinition processor = keyboardProcessor.Methods.Single(m => m.Name == "ProcessKeyCode");
                check(processor.Body.Instructions.Any(i => i.Operand is MethodReference c && c.DeclaringType.FullName == "TMPro.TMP_InputField" && c.Name == "ProcessEvent")
                    && processor.Body.Instructions.Any(i => i.Operand is MethodReference c && c.DeclaringType.FullName == "TMPro.TMP_InputField" && c.Name == "ForceLabelUpdate"),
                    "Original keyboard no longer routes native KeyCodes through field validation/labels.");
                check(keyboardProcessor.Fields.Single(f => f.Name == "m_KeyboardInputField").FieldType.FullName == "TMPro.TMP_InputField"
                    && keyboardProcessor.Fields.Single(f => f.Name == "keyboard").FieldType.FullName == "UIKeyboard"
                    && emitted.MainModule.GetType("GLOOM.MainMenu.UIMultiplayerJoinSessionWindow").Fields.Single(f => f.Name == "inviteCodeInput").FieldType.FullName == "TMPro.TMP_InputField",
                    "Exact invite field/keyboard metadata ABI changed.");
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
                MethodInfo connectionName = userType.GetMethod("GetUserNameForConnection")!, connectionId = userType.GetMethod("GetPlatformIDForConnection")!;
                check((string)connectionName.Invoke(user, new object?[] { null })! == "Fixture original owner" && (string)connectionId.Invoke(user, new object?[] { null })! == "76561198000000000",
                    "Actual null-connection local identity lost embedded name or ID.");
                Type connectionType = connectionName.GetParameters()[0].ParameterType;
                object connection = RuntimeHelpers.GetUninitializedObject(connectionType);
                Type tokenType = game.GetType("FFSNet.UserToken", true)!;
                object token = Activator.CreateInstance(tokenType, 0, "", "Remote owner", "76561198987654321", "original-version", "", "Steam", true, "remote-account", null)!;
                FieldInfo tokenField;
                using (var bolt = AssemblyDefinition.ReadAssembly(connectionType.Assembly.Location))
                {
                    MethodDefinition getter = bolt.MainModule.GetType("Photon.Bolt.BoltConnection").Methods.Single(m => m.Name == "get_ConnectToken");
                    FieldReference field = getter.Body.Instructions.Select(i => i.Operand).OfType<FieldReference>().Single();
                    check(field.DeclaringType.FullName == connectionType.FullName, "Actual Bolt token accessor fixture shape changed.");
                    tokenField = connectionType.GetField(field.Name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
                    tokenField.SetValue(connection, token);
                }
                check((string)connectionName.Invoke(user, new[] { connection })! == "Remote owner" && (string)connectionId.Invoke(user, new[] { connection })! == "76561198987654321",
                    "Actual remote identity was replaced by the local profile.");
                tokenType.GetProperty("Username")!.SetValue(token, "Different remote"); tokenType.GetProperty("PlatformPlayerID")!.SetValue(token, "other-remote-id");
                check((string)connectionName.Invoke(user, new[] { connection })! == "Different remote" && (string)connectionId.Invoke(user, new[] { connection })! == "other-remote-id",
                    "Remote token changes were cached or overwritten.");
                tokenField.SetValue(connection, null);
                foreach (MethodInfo api in new[] { connectionName, connectionId })
                {
                    try { api.Invoke(user, new[] { connection }); check(false, "Missing remote token was silently replaced by local identity."); }
                    catch (TargetInvocationException e) { check(e.InnerException is NullReferenceException, "Missing remote token no longer preserved original failure semantics."); }
                }
                Type platformType = game.GetType("PlatformLayer", true)!;
                object platform = RuntimeHelpers.GetUninitializedObject(platformType);
                check(!(bool)platformType.GetProperty("IsValid")!.GetValue(platform)!, "Offline adapter faked a live Steam session.");
                check(!(bool)platformType.GetProperty("EOSInitialised")!.GetValue(null)!, "Offline adapter faked EOS authorization.");
                platformType.GetMethod("Initialize")!.Invoke(platform, new object?[] { null });
                check((bool)platformType.GetProperty("Initialised")!.GetValue(null)!, "Actual platform boundary did not signal local initialization.");
                check((string)platformType.GetProperty("SessionTicket")!.GetValue(platform)! == "", "Offline adapter manufactured a Steam ticket.");
            }
            finally { context.Unload(); }
            string badIdentityOverrides = Path.Combine(temp, "bad-identity-overrides"), badIdentityOutput = Path.Combine(temp, "bad-identity-output"); Directory.CreateDirectory(badIdentityOverrides);
            using var mutationResolver = new DefaultAssemblyResolver(); mutationResolver.AddSearchDirectory(managed); mutationResolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
            using (var altered = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "GH.Runtime.dll"), new ReaderParameters { AssemblyResolver = mutationResolver }))
            {
                MethodDefinition identity = altered.MainModule.GetType("PlatformUserData").Methods.Single(m => m.Name == "GetUserNameForConnection");
                foreach (Instruction instruction in identity.Body.Instructions)
                    if (instruction.Operand is MethodReference call && call.DeclaringType.FullName == "FFSNet.UserToken" && call.Name == "get_Username")
                        instruction.Operand = new MethodReference("UnknownIdentityABI", call.ReturnType, call.DeclaringType) { HasThis = call.HasThis };
                altered.Write(Path.Combine(badIdentityOverrides, "GH.Runtime.dll"));
            }
            Reject(() => Standalone.Write(managed, badIdentityOverrides, profile, badIdentityOutput), "Changed original remote-token ABI accepted.");
            check(!Directory.Exists(badIdentityOutput), "Failed identity adaptation leaked partial output.");
            string badVoiceOverrides = Path.Combine(temp, "bad-voice-overrides"); Directory.CreateDirectory(badVoiceOverrides);
            using (var altered = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "GH.Runtime.dll"), new ReaderParameters { AssemblyResolver = mutationResolver }))
            {
                MethodDefinition voiceUi = altered.MainModule.GetType("VoiceChat.VoceChatOptions").Methods.Single(m => m.Name == "SwitchStatus");
                foreach (Instruction instruction in voiceUi.Body.Instructions)
                    if (instruction.Operand is MethodReference call && call.DeclaringType.FullName == "VoiceChat.BoltVoiceChatService" && call.Name == "get_IsVoiceChatConnected")
                        instruction.Operand = new MethodReference("UnknownVoiceConnectionABI", call.ReturnType, call.DeclaringType) { HasThis = call.HasThis };
                altered.Write(Path.Combine(badVoiceOverrides, "GH.Runtime.dll"));
            }
            Reject(() => Standalone.Write(managed, badVoiceOverrides, profile, Path.Combine(temp, "bad-voice-output")), "Changed original voice opt-in ABI accepted.");
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
