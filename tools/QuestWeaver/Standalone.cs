using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace QuestWeaver;

public sealed class StandaloneReport
{
    public int Schema { get; set; } = 1;
    public string Scope { get; set; } = "startup-offline-platform";
    public bool StartupAdapterComplete { get; set; }
    public bool ModLifecycleComplete { get; set; }
    public bool BepInExAdapterGenerated { get; set; }
    public bool FullGameReady { get; set; }
    public bool EosAuthorised { get; set; }
    public bool ProceduralRuntimeAvailable { get; set; }
    public bool VoiceNativeAvailable { get; set; }
    public string ContentRelativeRoot { get; set; } = "quest-owned-game";
    public List<string> Modifications { get; set; } = new();
    public List<IntegrationIssue> Issues { get; set; } = new();
    public List<IntegrationIssue> RemainingGates { get; set; } = new();
    public Dictionary<string, string> InputAssemblies { get; set; } = new();
    public Dictionary<string, string> OutputAssemblies { get; set; } = new();
    public int ProtectedTypesVerified { get; set; }
    public int UnchangedTypesVerified { get; set; }
}

internal sealed record OfflineProfile(string DisplayName, string SteamId, string AccountId, int OwnedDlcMask = 0);

/// <summary>Transforms staged platform seams, never original rules, saves or transports.</summary>
internal static class Standalone
{
    internal static OfflineProfile ReadProfile(string path)
    {
        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement p = json.RootElement;
        string name = p.GetProperty("displayName").GetString() ?? "";
        string id = p.GetProperty("steamId").GetString() ?? "";
        uint account = p.GetProperty("accountId").GetUInt32();
        bool dummy = p.TryGetProperty("isDummy", out JsonElement d) && d.GetBoolean();
        if (p.GetProperty("schema").GetInt32() != 1 || p.GetProperty("provider").GetString() != "steam"
            || string.IsNullOrWhiteSpace(name) || !ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed)
            || (dummy ? parsed != 0 || account != 0 || !name.Contains("DUMMY", StringComparison.OrdinalIgnoreCase)
                : parsed <= uint.MaxValue || (uint)parsed != account))
            throw new InvalidDataException("Offline profile is missing, inconsistent, or an unlabelled dummy.");
        int dlcMask = 0;
        if (p.TryGetProperty("dlcOwnership", out JsonElement ownership))
        {
            if (ownership.GetProperty("schema").GetInt32() != 1 || ownership.GetProperty("provider").GetString() != "steam"
                || ownership.GetProperty("appId").GetInt32() != 780290 || ownership.GetProperty("steamId").GetString() != id)
                throw new InvalidDataException("Offline DLC ownership differs from the selected game/account.");
            dlcMask = ownership.GetProperty("ownedMask").GetInt32();
            int[] apps = ownership.GetProperty("installedAppIds").EnumerateArray().Select(a => a.GetInt32()).ToArray();
            if ((dlcMask & ~7) != 0 || dlcMask < 0 || apps.Distinct().Count() != apps.Length
                || apps.Any(a => a != 1809490 && a != 1958560 && a != 2584170)
                || apps.Sum(a => a == 1809490 ? 1 : a == 1958560 ? 2 : 4) != dlcMask)
                throw new InvalidDataException("Offline DLC ownership has invalid or inconsistent flags.");
        }
        return new OfflineProfile(name, id, account.ToString(CultureInfo.InvariantCulture), dlcMask);
    }

    internal static StandaloneReport Write(string managed, string? overrides, string profilePath, string output, string? bepinexPath = null, string? modPath = null)
    {
        OfflineProfile profile = ReadProfile(profilePath);
        managed = Path.GetFullPath(managed); output = Path.GetFullPath(output);
        if (output == managed || output.StartsWith(managed + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || overrides != null && (output == Path.GetFullPath(overrides) || output.StartsWith(Path.GetFullPath(overrides) + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            throw new ArgumentException("Standalone output must be separate from inputs.");
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new ArgumentException("Output directory must be empty.");
        var report = new StandaloneReport();
        using var resolver = new DefaultAssemblyResolver();
        if (overrides != null) resolver.AddSearchDirectory(Path.GetFullPath(overrides));
        resolver.AddSearchDirectory(managed);
        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
        string Input(string name)
        {
            string path = overrides != null && File.Exists(Path.Combine(overrides, name)) ? Path.Combine(overrides, name) : Path.Combine(managed, name);
            report.InputAssemblies[name] = Hash(path); return path;
        }
        using AssemblyDefinition game = AssemblyDefinition.ReadAssembly(Input("GH.Runtime.dll"), new ReaderParameters { AssemblyResolver = resolver, InMemory = true });
        using AssemblyDefinition platforms = AssemblyDefinition.ReadAssembly(Input("SM.Consoles.dll"), new ReaderParameters { AssemblyResolver = resolver, InMemory = true });
        using AssemblyDefinition apparance = AssemblyDefinition.ReadAssembly(Input("Apparance.Unity.dll"), new ReaderParameters { AssemblyResolver = resolver, InMemory = true });
        if (game.MainModule.Resources.Any(r => r.Name == "QuestGame.Standalone.v1")) throw new InvalidDataException("Standalone input was already adapted.");
        var assemblies = new[] { game, platforms, apparance };
        var snapshots = assemblies.ToDictionary(a => a, a => Discovery.AllTypes(a.MainModule).ToDictionary(t => t.FullName, ProtectedTypes.Fingerprint));
        var protectedSnapshots = assemblies.ToDictionary(a => a, ProtectedTypes.Snapshot);
        var changedTypes = new HashSet<string>(StringComparer.Ordinal);
        MethodDefinition Method(AssemblyDefinition assembly, string typeName, string name, string returnType, params string[] parameters)
        {
            TypeDefinition type = Discovery.AllTypes(assembly.MainModule).SingleOrDefault(t => t.FullName == typeName)
                ?? throw new InvalidDataException("Required platform type is missing: " + typeName);
            MethodDefinition? method = type.Methods.SingleOrDefault(m => m.Name == name && m.ReturnType.FullName == returnType
                && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameters));
            return method ?? throw new InvalidDataException("Required platform ABI changed: " + typeName + "::" + name);
        }
        void Replace(MethodDefinition method, Action<ILProcessor> emit)
        {
            if (!method.HasBody || Discovery.Protected(method.DeclaringType)) throw new InvalidDataException("Cannot replace protected/abstract platform method: " + method.FullName);
            method.Body = new MethodBody(method) { InitLocals = true, MaxStackSize = 16 };
            emit(method.Body.GetILProcessor()); changedTypes.Add(method.DeclaringType.FullName);
            report.Modifications.Add(method.FullName);
        }
        void Constant(AssemblyDefinition a, string type, string method, object? value, string result)
        {
            Replace(Method(a, type, method, result), il => { EmitConstant(il, value); il.Emit(OpCodes.Ret); });
        }
        void Noop(string type, string method, params string[] parameters) => Replace(Method(game, type, method, "System.Void", parameters), il => il.Emit(OpCodes.Ret));

        MethodDefinition factory = Method(platforms, "Platforms.Utils.PlatformConstructor", "BuildPlatform", "Platforms.IPlatform",
            "Platforms.IGameProvider", "System.Boolean", "System.Boolean", "System.Boolean", "System.Boolean");
        if (!factory.Body.Instructions.Any(i => i.OpCode == OpCodes.Newobj && i.Operand is MethodReference c && c.DeclaringType.FullName == "Platforms.Steam.PlatformSteam"))
            throw new InvalidDataException("Platform factory no longer constructs the expected original Steam implementation.");
        MethodDefinition ctor = Method(platforms, "Platforms.Generic.PlatformGeneric", ".ctor", "System.Void",
            "Platforms.IGameProvider", "System.Boolean", "System.Boolean", "System.Boolean", "System.Boolean");
        Replace(factory, il => { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Newobj, ctor); il.Emit(OpCodes.Ret); });
        // Generic platform originally creates users only for desktop keyboards/gamepads.
        // Preserve its existing local user implementation, with no online sign-in assertion.
        MethodDefinition buildUsers = Method(platforms, "Platforms.Generic.PlatformInputGeneric", "BuildUnityUsers", "System.Void");
        FieldDefinition users = buildUsers.DeclaringType.Fields.Single(f => f.Name == "_userManagement" && f.FieldType.FullName == "Platforms.Generic.UserManagementGeneric");
        MethodDefinition addUser = Method(platforms, "Platforms.Generic.UserManagementGeneric", "AddPlatformUser", "System.Void", "Platforms.IPlatformUserData");
        MethodDefinition userCtor = Method(platforms, "Platforms.Generic.UserDataGeneric", ".ctor", "System.Void", "System.String", "System.String", "System.Int32", "UnityEngine.InputSystem.Users.InputUser");
        Replace(buildUsers, il =>
        {
            var inputUser = new VariableDefinition(userCtor.Parameters[3].ParameterType); buildUsers.Body.Variables.Add(inputUser);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, users); il.Emit(OpCodes.Ldstr, profile.DisplayName); il.Emit(OpCodes.Ldstr, profile.AccountId); il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloca, inputUser); il.Emit(OpCodes.Initobj, inputUser.VariableType); il.Emit(OpCodes.Ldloc, inputUser);
            il.Emit(OpCodes.Newobj, userCtor); il.Emit(OpCodes.Callvirt, addUser); il.Emit(OpCodes.Ret);
        });
        // XR devices need not have a paired generic user. Original disconnect
        // routing can pass null when pairing is disabled. Preserve every original
        // non-null removal/event/logging path, adding only the absent-user return.
        MethodDefinition removeUser = Method(platforms, "Platforms.Generic.UserManagementGeneric", "RemovePlatformUser", "System.Void", "Platforms.IPlatformUserData");
        GuardMissingPlatformUser(removeUser);
        changedTypes.Add(removeUser.DeclaringType.FullName); report.Modifications.Add(removeUser.FullName);
        MethodDefinition initialize = Method(game, "PlatformLayer", "Initialize", "System.Void", "Platforms.IPlatform");
        if (!initialize.Body.Instructions.Any(i => i.Operand is MethodReference call && (call.DeclaringType.FullName == "Steamworks.SteamClient" || call.DeclaringType.FullName == "PlatformLayer" && call.Name == "Init")))
            throw new InvalidDataException("Platform initialization no longer contains the expected desktop service seam.");
        MethodDefinition setInitialised = Method(game, "PlatformLayer", "set_Initialised", "System.Void", "System.Boolean");
        Replace(initialize, il => { il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Call, setInitialised); il.Emit(OpCodes.Ret); });
        Noop("PlatformLayer", "Init", "System.UInt32"); Noop("PlatformLayer", "Update"); Noop("PlatformLayer", "Dispose");
        Constant(game, "PlatformLayer", "get_IsValid", false, "System.Boolean");
        Constant(game, "PlatformLayer", "get_SessionTicket", "", "System.String");
        Constant(game, "PlatformLayer", "get_SteamAppId", 780290, "System.UInt32");
        // The PC builder captures local DLC availability once. Preserve original
        // CanPlayDLC/file checks, party/save validation and promotional selection;
        // only the unavailable live-store seam uses baked ownership on Quest.
        MethodDefinition installedDlc = Method(game, "PlatformDLC", "UserInstalledDLC", "System.Boolean", "ScenarioRuleLibrary.DLCRegistry/EDLCKey");
        int[] originalDlcIds = installedDlc.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldc_I4)
            .Select(i => (int)i.Operand).Where(i => i > 100000).OrderBy(i => i).ToArray();
        if (!originalDlcIds.SequenceEqual(new[] { 1809490, 1958560, 2584170 })
            || !installedDlc.Body.Instructions.Any(i => i.Operand is MethodReference c && c.DeclaringType.FullName == "Steamworks.SteamApps" && c.Name == "IsDlcInstalled"))
            throw new InvalidDataException("Original DLC ownership mapping changed; review the new source before building.");
        Replace(installedDlc, il =>
        {
            Instruction yes = Instruction.Create(OpCodes.Ldc_I4_1);
            foreach (int flag in new[] { 1, 2, 4 })
                if ((profile.OwnedDlcMask & flag) != 0)
                { il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4, flag); il.Emit(OpCodes.Beq, yes); }
            il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
            il.Append(yes); il.Emit(OpCodes.Ret);
        });
        Noop("PlatformDLC", "OpenPlatformStoreDLCOverlay", "ScenarioRuleLibrary.DLCRegistry/EDLCKey");
        // Stored EpicLogin may request this entry point during SaveData initialization.
        // Keep the save unchanged and explicitly reject authentication on this offline target.
        MethodDefinition eos = Method(game, "PlatformLayer", "EOSInitialise", "System.Void");
        MethodReference warning = eos.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().FirstOrDefault(m => m.DeclaringType.FullName == "UnityEngine.Debug" && m.Name == "LogWarning")
            ?? Discovery.AllTypes(game.MainModule).SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Select(i => i.Operand).OfType<MethodReference>()
                .First(m => m.DeclaringType.FullName == "UnityEngine.Debug" && m.Name == "LogWarning" && m.Parameters.Count == 1);
        Replace(eos, il => { il.Emit(OpCodes.Ldstr, "[Quest startup] EOS unavailable in offline startup target; stored EpicLogin retained. Multiplayer authentication remains unverified."); il.Emit(OpCodes.Call, game.MainModule.ImportReference(warning)); il.Emit(OpCodes.Ret); });
        Noop("PlatformNetworking", "Initialize", "Platforms.IPlatform");
        Constant(game, "PlatformNetworking", "get_PlatformInvitesSupported", false, "System.Boolean");
        Constant(game, "PlatformNetworking", "get_EPICInvitesSupported", false, "System.Boolean");
        Constant(game, "PlatformModding", "get_ModdingSupported", false, "System.Boolean");
        Constant(game, "PlatformModding", "get_LevelEditorSupported", false, "System.Boolean");
        Constant(game, "PlatformUserData", "get_UserName", profile.DisplayName, "System.String");
        Constant(game, "PlatformUserData", "get_PlatformPlayerID", profile.SteamId, "System.String");
        Constant(game, "PlatformUserData", "get_PlatformAccountID", profile.AccountId, "System.String");
        Constant(game, "PlatformUserData", "get_IsSignedIn", true, "System.Boolean");
        void ConnectionIdentity(string name, string tokenProperty, string localValue)
        {
            MethodDefinition method = Method(game, "PlatformUserData", name, "System.String", "Photon.Bolt.BoltConnection");
            MethodReference[] calls = method.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().ToArray();
            MethodReference[] connections = calls.Where(c => c.DeclaringType.FullName == "Photon.Bolt.BoltConnection" && c.Name == "get_ConnectToken")
                .GroupBy(c => c.FullName).Select(g => g.First()).ToArray();
            MethodReference[] tokens = calls.Where(c => c.DeclaringType.FullName == "FFSNet.UserToken" && c.Name == tokenProperty && c.ReturnType.FullName == "System.String")
                .GroupBy(c => c.FullName).Select(g => g.First()).ToArray();
            if (connections.Length != 1 || tokens.Length != 1 || !connections[0].HasThis || !tokens[0].HasThis)
                throw new InvalidDataException("Original connection identity token ABI changed: " + method.FullName);
            // Retain the original remote token/property boundary. Only null means the
            // local offline owner; never replace or reinterpret another player's token.
            Replace(method, il =>
            {
                Instruction remote = Instruction.Create(OpCodes.Ldarg, method.Parameters[0]);
                il.Emit(OpCodes.Ldarg, method.Parameters[0]); il.Emit(OpCodes.Brtrue, remote);
                il.Emit(OpCodes.Ldstr, localValue); il.Emit(OpCodes.Ret); il.Append(remote);
                il.Emit(OpCodes.Callvirt, connections[0]); il.Emit(OpCodes.Castclass, tokens[0].DeclaringType);
                il.Emit(OpCodes.Callvirt, tokens[0]); il.Emit(OpCodes.Ret);
            });
        }
        ConnectionIdentity("GetUserNameForConnection", "get_Username", profile.DisplayName);
        ConnectionIdentity("GetPlatformIDForConnection", "get_PlatformPlayerID", profile.SteamId);
        MethodDefinition voiceSwitch = Method(game, "VoiceChat.VoceChatOptions", "SwitchStatus", "System.Void");
        if (!voiceSwitch.Body.Instructions.Any(i => i.Operand is MethodReference c && c.DeclaringType.FullName == "VoiceChat.BoltVoiceChatService" && c.Name == "get_IsVoiceChatConnected"))
            throw new InvalidDataException("Original voice UI connection gate changed: " + voiceSwitch.FullName);
        // The SDK starts the native Opus encoder from its own joined-room callback.
        // Guard the original opt-in UI before connecting, never fabricate room success
        // or alter the original Bolt/Photon transport and SDK callbacks.
        Replace(voiceSwitch, il =>
        {
            il.Emit(OpCodes.Ldstr, "[Quest startup] voice chat unavailable in this startup diagnostic: original native Opus encoder is not available on Android; no voice room connection attempted.");
            il.Emit(OpCodes.Call, game.MainModule.ImportReference(warning)); il.Emit(OpCodes.Ret);
        });
        // IsSignedIn here means local save owner availability; the original generic user's
        // IsSignedInOnline and SteamClient.IsValid remain false, and EOS status is untouched.
        Noop("PlatformUserData", "StartLoginFlow");
        Noop("PlatformUserData", "EOSInitialise");
        // This explicitly menu-only target has no compatible native Apparance engine.
        // Suppress only its Unity lifecycle entry points; leave generation APIs untouched,
        // Instance unset, and report the unavailable procedural runtime rather than success.
        foreach (string callback in new[] { "Awake", "Start", "Update", "Stop", "OnDestroy" })
            Replace(Method(apparance, "ApparanceEngine", callback, "System.Void"), il => il.Emit(OpCodes.Ret));
        MethodDefinition unloadResources = Method(game, "ApparanceResourceListLoader", "UnloadAll", "System.Void");
        Instruction[] nativeRefresh = unloadResources.Body.Instructions.Where(i => i.OpCode == OpCodes.Callvirt && i.Operand is MethodReference c
            && c.DeclaringType.FullName == "ApparanceEngine" && c.Name == "RefreshResources" && c.HasThis && c.ReturnType.FullName == "System.Void" && c.Parameters.Count == 0).ToArray();
        if (nativeRefresh.Length != 1) throw new InvalidDataException("Original startup resource-unload native boundary changed: " + unloadResources.FullName);
        // Original SceneController startup calls UnloadAll even without a campaign.
        // Keep real AssetReference releases and both managed collection clears, but
        // consume the engine instance instead of entering its unavailable native cache.
        nativeRefresh[0].OpCode = OpCodes.Pop; nativeRefresh[0].Operand = null;
        changedTypes.Add(unloadResources.DeclaringType.FullName); report.Modifications.Add("menu-only native engine cache-refresh guard: " + unloadResources.FullName);

        // Keep the original Workshop row visible while excluding its callback. Global
        // ModdingSupported stays false so startup never scans/downloads Workshop content.
        MethodDefinition extras = Method(game, "GLOOM.MainMenu.MainOptionExtras", "BuildOptions", "System.Collections.Generic.List`1<GLOOM.MainMenu.MenuSuboption>");
        Instruction workshopGate = extras.Body.Instructions.Single(i => i.Operand is MethodReference c && c.Name == "get_ModdingSupported");
        workshopGate.OpCode = OpCodes.Pop; workshopGate.Operand = null;
        extras.Body.GetILProcessor().InsertAfter(workshopGate, Instruction.Create(OpCodes.Ldc_I4_1));
        changedTypes.Add(extras.DeclaringType.FullName); report.Modifications.Add("retain excluded original Workshop row: " + extras.FullName);
        MethodDefinition suboption = Method(game, "GLOOM.MainMenu.MenuSuboption", ".ctor", "System.Void", "System.String", "GLOOM.MainMenu.MenuOptionIcon", "System.Action", "System.Action", "System.Boolean", "System.String");
        MethodReference equals = new("op_Equality", game.MainModule.TypeSystem.Boolean, game.MainModule.TypeSystem.String);
        equals.Parameters.Add(new ParameterDefinition(game.MainModule.TypeSystem.String)); equals.Parameters.Add(new ParameterDefinition(game.MainModule.TypeSystem.String));
        Instruction end = suboption.Body.Instructions.Last();
        if (end.OpCode != OpCodes.Ret) throw new InvalidDataException("Original menu constructor shape changed.");
        ILProcessor si = suboption.Body.GetILProcessor();
        var suffix = new[] { Instruction.Create(OpCodes.Ldarg, suboption.Parameters[0]), Instruction.Create(OpCodes.Ldstr, "GUI_MODDING"), Instruction.Create(OpCodes.Call, equals), Instruction.Create(OpCodes.Brfalse, end),
            Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldc_I4_0), Instruction.Create(OpCodes.Stfld, suboption.DeclaringType.Fields.Single(f => f.Name == "interactable")),
            Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldnull), Instruction.Create(OpCodes.Stfld, suboption.DeclaringType.Fields.Single(f => f.Name == "onSelected")),
            Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldnull), Instruction.Create(OpCodes.Stfld, suboption.DeclaringType.Fields.Single(f => f.Name == "tooltip")) };
        foreach (Instruction instruction in suffix) si.InsertBefore(end, instruction);
        changedTypes.Add(suboption.DeclaringType.FullName); report.Modifications.Add("excluded Workshop callback and native row state: " + suboption.FullName);

        using AssemblyDefinition compatibility = PathsCompatibility.Create(game.MainModule);
        TypeDefinition paths = compatibility.MainModule.Types.Single(t => t.Name == "Paths");
        // B612 hardware reached native rule loading, then failed while resolving
        // global hero/item references. Its recent-logcat capture evicted the first
        // parse failure; that cause is still unproven. Independently, the original
        // protected rule DLL initializes LazyLoadingConstants from Android's jar
        // StreamingAssets path and scans it with Directory.GetFiles. Initialize
        // its existing public IO setting at the original initialization boundary,
        // after the verified owned files are available. Never rewrite the rule
        // DLL, suppress validation, change parser decisions, or skip shared rules.
        using (AssemblyDefinition rules = AssemblyDefinition.ReadAssembly(Input("ScenarioRuleLibrary.dll"), new ReaderParameters { AssemblyResolver = resolver, InMemory = true }))
        {
            TypeDefinition lazyPaths = rules.MainModule.GetType("ScenarioRuleLibrary.LazyLoadingConstants")
                ?? throw new InvalidDataException("Original lazy rule path type is missing.");
            FieldDefinition[] fields = lazyPaths.Fields.Where(f => f.Name == "RulesetPath" && f.FieldType.FullName == "System.String"
                && f.IsPublic && f.IsStatic && !f.IsInitOnly && !f.IsLiteral).ToArray();
            if (fields.Length != 1) throw new InvalidDataException("Original public lazy rule path ABI changed.");
            MethodDefinition initialisePath = new("InitializeRulebasePath", MethodAttributes.Public | MethodAttributes.Static, compatibility.MainModule.TypeSystem.Void);
            paths.Methods.Add(initialisePath);
            MethodReference combine = new("Combine", compatibility.MainModule.TypeSystem.String,
                new TypeReference("System.IO", "Path", compatibility.MainModule, compatibility.MainModule.TypeSystem.CoreLibrary));
            combine.Parameters.Add(new ParameterDefinition(compatibility.MainModule.TypeSystem.String));
            combine.Parameters.Add(new ParameterDefinition(compatibility.MainModule.TypeSystem.String));
            ILProcessor init = initialisePath.Body.GetILProcessor();
            init.Emit(OpCodes.Call, paths.Methods.Single(m => m.Name == "get_streamingAssetsPath"));
            init.Emit(OpCodes.Ldstr, "Rulebase"); init.Emit(OpCodes.Call, combine);
            init.Emit(OpCodes.Stsfld, compatibility.MainModule.ImportReference(fields[0])); init.Emit(OpCodes.Ret);

            MethodDefinition[] boundaries = Discovery.AllTypes(game.MainModule)
                .Where(t => t.DeclaringType?.FullName == "SceneController" && t.Name.StartsWith("<InitialiseGloomhavenCoroutine>", StringComparison.Ordinal))
                .SelectMany(t => t.Methods).Where(m => m.Name == "MoveNext" && m.HasBody && m.ReturnType.FullName == "System.Boolean").ToArray();
            if (boundaries.Length != 1) throw new InvalidDataException("Original rule initialization coroutine ABI changed.");
            MethodDefinition boundary = boundaries[0];
            Instruction[] calls = boundary.Body.Instructions.Where(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference call
                && call.DeclaringType.FullName == "ScenarioRuleLibrary.ScenarioRuleClient" && call.Name == "Initialise"
                && !call.HasThis && call.ReturnType.FullName == "System.Void" && call.Parameters.Count == 1
                && call.Parameters[0].ParameterType.FullName == "System.Boolean").ToArray();
            if (calls.Length != 1 || Discovery.Protected(boundary.DeclaringType))
                throw new InvalidDataException("Original rule initialization call boundary changed.");
            boundary.Body.GetILProcessor().InsertBefore(calls[0], Instruction.Create(OpCodes.Call, game.MainModule.ImportReference(initialisePath)));
            changedTypes.Add(boundary.DeclaringType.FullName);
            report.Modifications.Add("verified file-backed lazy rule path before original initialization: " + boundary.FullName);
        }
        PathsCompatibility.RebindCalls(game.MainModule, paths, changedTypes, report.Modifications);
        PathsCompatibility.BindErrorScreenshot(game.MainModule, paths, changedTypes, report.Modifications);
        game.MainModule.Resources.Add(new EmbeddedResource("QuestGame.Standalone.v1", ManifestResourceAttributes.Private, System.Text.Encoding.UTF8.GetBytes("startup-offline-platform\n")));
        report.RemainingGates.AddRange(new[] {
            new IntegrationIssue("ORIGINAL_MENU_RUNTIME", "Bootstrap -> Intro -> Gloomhaven_unified -> MainMenu", "Original scene execution, initial Android Addressables closure, native services and rendering require actual Unity/device evidence."),
            new IntegrationIssue("MOD_LIFECYCLE", "GloomhavenVR.Plugin", "This platform-only output does not instantiate or certify the mod. Standalone BepInEx lifecycle and XR/content seams are a separate gate."),
            new IntegrationIssue("EOS_CROSSPLAY", "original multiplayer", "No online authentication, sessions, invites or crossplay are claimed; legitimate Android EOS remains required if original transport requires it.") });
        report.RemainingGates.Add(new IntegrationIssue("PROCEDURAL_NATIVE_UNAVAILABLE", "ApparanceEngine", "Menu-only lifecycle and startup unload's native cache-refresh disabled because original engine imports Windows ApparanceEngine. Managed resource release remains original. No fake generation readiness; campaign requires verified Android native support or prebaked geometry."));
        report.RemainingGates.Add(new IntegrationIssue("VOICE_NATIVE_UNAVAILABLE", "VoiceChat.VoceChatOptions.SwitchStatus", "Original opt-in voice UI is guarded for this startup diagnostic. Native Opus encoder and real voice-room operation remain unavailable/unverified; game transport is unchanged."));
        string scratch = Path.Combine(Path.GetDirectoryName(output)!, ".quest-standalone-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            if (bepinexPath != null && modPath != null)
            {
                report.InputAssemblies["BepInEx.dll"] = Hash(bepinexPath);
                report.InputAssemblies["lifecycle-mod.dll"] = Hash(modPath);
                BepInExStandalone.Write(bepinexPath, modPath, Path.Combine(scratch, "BepInEx.dll"), report);
                report.OutputAssemblies["BepInEx.dll"] = Hash(Path.Combine(scratch, "BepInEx.dll"));
                report.BepInExAdapterGenerated = true;
            }
            foreach (AssemblyDefinition a in assemblies)
            {
                string path = Path.Combine(scratch, a.Name.Name + ".dll"); a.Write(path);
                using var reread = AssemblyDefinition.ReadAssembly(path);
                report.ProtectedTypesVerified += ProtectedTypes.Verify(protectedSnapshots[a], reread);
                foreach (TypeDefinition type in Discovery.AllTypes(reread.MainModule))
                    if (!changedTypes.Contains(type.FullName))
                    {
                        if (!snapshots[a].TryGetValue(type.FullName, out string? hash) || hash != ProtectedTypes.Fingerprint(type)) throw new InvalidDataException("Unrelated type changed: " + type.FullName);
                        report.UnchangedTypesVerified++;
                    }
                report.OutputAssemblies[a.Name.Name + ".dll"] = Hash(path);
            }
            compatibility.Write(Path.Combine(scratch, "QuestGame.Compatibility.dll"));
            report.OutputAssemblies["QuestGame.Compatibility.dll"] = Hash(Path.Combine(scratch, "QuestGame.Compatibility.dll"));
            File.WriteAllText(Path.Combine(scratch, "link.xml"), "<linker><assembly fullname=\"GH.Runtime\" preserve=\"all\"/><assembly fullname=\"SM.Consoles\" preserve=\"all\"/><assembly fullname=\"Apparance.Unity\" preserve=\"all\"/><assembly fullname=\"QuestGame.Compatibility\" preserve=\"all\"/>" + (report.BepInExAdapterGenerated ? "<assembly fullname=\"BepInEx\" preserve=\"all\"/>" : "") + "</linker>\n");
            if (Directory.Exists(output)) Directory.Delete(output);
            Directory.Move(scratch, output); report.StartupAdapterComplete = true;
            return report;
        }
        finally { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
    }

    private static void EmitConstant(ILProcessor il, object? value)
    {
        switch (value) { case bool b: il.Emit(b ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0); break; case int i: il.Emit(OpCodes.Ldc_I4, i); break; case string s: il.Emit(OpCodes.Ldstr, s); break; default: il.Emit(OpCodes.Ldnull); break; }
    }
    internal static void GuardMissingPlatformUser(MethodDefinition method)
    {
        if (method.DeclaringType.FullName != "Platforms.Generic.UserManagementGeneric" || method.Name != "RemovePlatformUser"
            || method.IsStatic || !method.HasThis || method.ExplicitThis || method.HasGenericParameters
            || method.ReturnType.FullName != "System.Void" || method.Parameters.Count != 1
            || method.Parameters[0].ParameterType.FullName != "Platforms.IPlatformUserData" || method.Parameters[0].ParameterType.Resolve()?.IsInterface != true
            || !method.HasBody || method.Body.Instructions.Count == 0 || Discovery.Protected(method.DeclaringType))
            throw new InvalidDataException("Original generic user-removal ABI changed.");
        FieldDefinition[] userLists = method.DeclaringType.Fields.Where(field => field.Name == "_users" && !field.IsStatic
            && field.FieldType.FullName == "System.Collections.Generic.List`1<Platforms.IPlatformUserData>").ToArray();
        if (userLists.Length != 1 || !method.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Callvirt
            && instruction.Operand is MethodReference call && call.DeclaringType.FullName == "Platforms.IPlatformUserData"
            && call.Name == "GetUnityInputUser" && call.HasThis && call.Parameters.Count == 0
            && call.ReturnType.FullName == "UnityEngine.InputSystem.Users.InputUser"))
            throw new InvalidDataException("Original generic user-removal body seam changed.");
        Instruction first = method.Body.Instructions[0];
        ILProcessor il = method.Body.GetILProcessor();
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_1));
        il.InsertBefore(first, il.Create(OpCodes.Brtrue, first));
        il.InsertBefore(first, il.Create(OpCodes.Ret));
        method.Body.MaxStackSize = Math.Max(1, method.Body.MaxStackSize);
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
