using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;
using QuestWeaver;

internal static class StandaloneGuildmasterTests
{
    internal static void Run(string projectRoot, Action<bool, string> check, bool requireOriginal = false)
    {
        string managed = Path.Combine(projectRoot, "ressources/GH_Data/Managed");
        string gamePath = Path.Combine(managed, "GH.Runtime.dll");
        if (!File.Exists(gamePath))
        {
            if (requireOriginal) throw new InvalidDataException("Original game input is required for the excluded-mode proof.");
            Console.WriteLine("Original Guildmaster admission proof skipped: owned game input is absent.");
            return;
        }
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(managed);
        using var source = AssemblyDefinition.ReadAssembly(gamePath, new ReaderParameters { InMemory = true, AssemblyResolver = resolver });
        using var actual = AssemblyDefinition.ReadAssembly(gamePath, new ReaderParameters { InMemory = true, AssemblyResolver = resolver });
        var runtime = new AssemblyNameReference("QuestGame.Campaign", new Version(0, 0, 0, 0));
        var report = new StandaloneReport();
        string[] changed = StandaloneGuildmaster.Apply(actual, runtime, report).ToArray();
        check(changed.SequenceEqual(new[] { "GHClientCallbacks", "SaveData" }), "Only the two excluded-mode boundaries are changed.");
        check(source.MainModule.Mvid == actual.MainModule.Mvid && report.Modifications.Count == 2, "Native identities and both reported scope changes are retained.");
        check(ProtectedTypes.Verify(ProtectedTypes.Snapshot(source), actual) > 0, "Original transport/rules protected types remain unchanged.");
        foreach (string type in changed)
            foreach (MethodDefinition method in actual.MainModule.GetType(type).Methods.Where(m => m.Name != (type == "SaveData" ? "LoadGuildmasterMode" : "Connected")))
                check(Body(method) == Body(source.MainModule.GetType(type).Methods.Single(m => m.FullName == method.FullName)), "An unrelated original save/client method changed: " + method.FullName);

        MethodDefinition connected = actual.MainModule.GetType("GHClientCallbacks").Methods.Single(m => m.Name == "Connected");
        MethodDefinition originalConnected = source.MainModule.GetType("GHClientCallbacks").Methods.Single(m => m.Name == "Connected");
        Instruction admission = connected.Body.Instructions.Single(i => i.OpCode == OpCodes.Brtrue_S && i.Previous?.Operand is FieldReference f && f.Name == "gameData");
        Instruction guardStart = (Instruction)admission.Operand;
        int guardIndex = connected.Body.Instructions.IndexOf(guardStart);
        Instruction[] guard = connected.Body.Instructions.Skip(guardIndex).Take(20).ToArray();
        Instruction nativeContinuation = (Instruction)guard[4].Operand;
        check(guard[3].Operand is int mode && mode == EnumValue(source, "EGameMode", "Guildmaster")
            && guard[4].OpCode == OpCodes.Bne_Un && guard.Last().OpCode == OpCodes.Ret,
            "Only the native Guildmaster enum takes rejection; all other modes resume original admission.");
        check(guard[15].Operand is MethodReference shutdown && shutdown.Name == "Shutdown"
            && guard[18].Operand is MethodReference notice && notice.Name == "NotifyGuildmasterUnavailable"
            && ((AssemblyNameReference)notice.DeclaringType.Scope).Name == "QuestGame.Campaign",
            "Native shutdown precedes a preserved, statically bound full-game notice.");
        check(nativeContinuation.Next.Next.Next.Operand is MethodReference read && read.Name == "get_GameModeID",
            "Non-Guildmaster enters the original closure capture before native privileges and save transfer.");

        // Remove only the actual generated seam, then compare complete native IL,
        // including missing-token errors, privilege closures, data and branches.
        admission.Operand = nativeContinuation;
        foreach (Instruction instruction in guard) connected.Body.Instructions.Remove(instruction);
        check(Body(connected) == Body(originalConnected), "Removing the guard restores the exact original Connected body.");
        foreach (Instruction instruction in guard) connected.Body.GetILProcessor().InsertBefore(nativeContinuation, instruction);
        admission.Operand = guardStart;
        MethodDefinition load = actual.MainModule.GetType("SaveData").Methods.Single(m => m.Name == "LoadGuildmasterMode");
        Instruction[] loadGuard = load.Body.Instructions.Take(4).ToArray();
        check(loadGuard[0].Operand == load.Parameters[2] && loadGuard[1].Operand == load.Parameters[4]
            && loadGuard[2].Operand is MethodReference helper && helper.Name == "NotifyGuildmasterUnavailable" && loadGuard[3].OpCode == OpCodes.Ret,
            "Local/imported loads restore the original caller without evaluating or rewriting party/save data.");
        foreach (Instruction instruction in loadGuard) load.Body.Instructions.Remove(instruction);
        check(Body(load) == Body(source.MainModule.GetType("SaveData").Methods.Single(m => m.Name == "LoadGuildmasterMode")),
            "The complete original Guildmaster load body remains behind the capability seam.");
        Instruction nativeLoad = load.Body.Instructions[0];
        foreach (Instruction instruction in loadGuard) load.Body.GetILProcessor().InsertBefore(nativeLoad, instruction);

        int originalNullCheck = connected.Body.Instructions.IndexOf(admission.Previous.Previous);
        Instruction[] admissionAndGuard = connected.Body.Instructions.Skip(originalNullCheck)
            .Take(connected.Body.Instructions.IndexOf(nativeContinuation) - originalNullCheck).ToArray();
        ExecuteGuard(admissionAndGuard, guard, loadGuard, check);
        guard[4].OpCode = OpCodes.Beq;
        try
        {
            ExecuteGuard(admissionAndGuard, guard, loadGuard, (ok, message) => { if (!ok) throw new InvalidOperationException(message); });
            check(false, "Inverted excluded-mode branch negative control escaped the executable fixture.");
        }
        catch (InvalidOperationException) { check(true, "Inverted actual generated branch is rejected by the executable fixture."); }
        finally { guard[4].OpCode = OpCodes.Bne_Un; }
        ExpectRejected(source, runtime, check, a => a.MainModule.GetType("GameToken").Methods.Single(m => m.Name == "get_GameModeID").Name = "ModeChanged", "Changed token mode accessor");
        ExpectRejected(source, runtime, check, a => a.MainModule.GetType("GHClientCallbacks").Fields.Single(f => f.Name == "gameData").Name = "RenamedData", "Changed client token field");
        ExpectRejected(source, runtime, check, a => a.MainModule.GetType("SaveData").Methods.Single(m => m.Name == "LoadGuildmasterMode").Parameters[4].ParameterType = a.MainModule.TypeSystem.Object, "Changed cancellation ABI");
        ExpectRejected(source, runtime, check, a => a.MainModule.GetType("EGameMode").Fields.Single(f => f.Name == "Guildmaster").Name = "MissingMode", "Missing native Guildmaster enum");
        ExpectRejected(source, runtime, check, a =>
        {
            MethodDefinition m = a.MainModule.GetType("GHClientCallbacks").Methods.Single(m => m.Name == "Connected");
            m.Body.Instructions.Single(i => i.Operand is MethodReference reference && reference.Name == "get_OnConnectionFailed").Operand = new MethodReference("WrongFailureCallback", a.MainModule.TypeSystem.Void, a.MainModule.TypeSystem.Object);
        }, "Changed original failure/shutdown ABI");
        try { StandaloneGuildmaster.Apply(actual, runtime, new StandaloneReport()); check(false, "Repeated adaptation accepted."); }
        catch (InvalidDataException) { check(true, "Repeated adaptation rejected."); }
        actual.Write(new MemoryStream());
        check(true, "Adapted original full assembly writes successfully with valid branch offsets.");
    }

    static string Body(MethodDefinition method)
    {
        if (!method.HasBody) return "no body";
        Instruction[] instructions = method.Body.Instructions.ToArray();
        string Operand(object? value) => value switch
        {
            Instruction i => "branch:" + Array.IndexOf(instructions, i),
            Instruction[] branches => "switch:" + string.Join(",", branches.Select(i => Array.IndexOf(instructions, i))),
            ParameterDefinition p => "param:" + p.Index,
            VariableDefinition v => "local:" + v.Index,
            MemberReference m => m.FullName,
            _ => value?.ToString() ?? ""
        };
        return string.Join("\n", instructions.Select(i => i.OpCode + " " + Operand(i.Operand)));
    }

    static int EnumValue(AssemblyDefinition game, string type, string field) => Convert.ToInt32(game.MainModule.GetType(type).Fields.Single(f => f.Name == field).Constant);

    static void ExpectRejected(AssemblyDefinition original, AssemblyNameReference runtime, Action<bool, string> check, Action<AssemblyDefinition> change, string name)
    {
        using var buffer = new MemoryStream(); original.Write(buffer); buffer.Position = 0;
        using var input = AssemblyDefinition.ReadAssembly(buffer);
        change(input);
        try { StandaloneGuildmaster.Apply(input, runtime, new StandaloneReport()); check(false, name + " silently accepted."); }
        catch (InvalidDataException) { check(true, name + " rejected."); }
    }

    static void ExecuteGuard(Instruction[] admissionAndGuard, Instruction[] guard, Instruction[] localGuard, Action<bool, string> check)
    {
        // Execute the actual Cecil-generated instructions, with just native
        // external calls substituted by counter fixtures. This proves CLR stack,
        // null-delegate, return and nonmatching-mode behavior; it does not claim
        // a successful headset/PC session or emulate the native multiplayer body.
        using var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("QuestGuildmasterGuardFixture", new Version(1, 0)), "QuestGuildmasterGuardFixture", ModuleKind.Dll);
        ModuleDefinition module = assembly.MainModule;
        var holder = new TypeDefinition("", "GuardExercise", Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class, module.TypeSystem.Object);
        module.Types.Add(holder);
        var field = new FieldDefinition("gameData", Mono.Cecil.FieldAttributes.Public, module.ImportReference(typeof(GuildmasterRuntimeFixture.Token)));
        holder.Fields.Add(field);
        var enter = new MethodDefinition("Enter", Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Void);
        holder.Methods.Add(enter);
        ILProcessor il = enter.Body.GetILProcessor();
        Instruction allowed = il.Create(OpCodes.Call, module.ImportReference(typeof(GuildmasterRuntimeFixture).GetMethod(nameof(GuildmasterRuntimeFixture.OriginalAdmission))!));
        Dictionary<string, MethodInfo> calls = new()
        {
            ["GameToken.get_GameModeID"] = typeof(GuildmasterRuntimeFixture.Token).GetProperty(nameof(GuildmasterRuntimeFixture.Token.GameModeID))!.GetMethod!,
            ["FFSNetwork.get_Manager"] = typeof(GuildmasterRuntimeFixture).GetProperty(nameof(GuildmasterRuntimeFixture.Manager))!.GetMethod!,
            ["FFSNet.NetworkManager.get_OnConnectionFailed"] = typeof(GuildmasterRuntimeFixture.ManagerData).GetProperty(nameof(GuildmasterRuntimeFixture.ManagerData.OnConnectionFailed))!.GetMethod!,
            ["UnityEngine.Events.UnityAction`1<ConnectionErrorCode>.Invoke"] = typeof(GuildmasterRuntimeFixture.FailureAction).GetMethod("Invoke")!,
            ["FFSNetwork.Shutdown"] = typeof(GuildmasterRuntimeFixture).GetMethod(nameof(GuildmasterRuntimeFixture.Shutdown))!,
            ["FFSNet.Console.LogError"] = typeof(GuildmasterRuntimeFixture).GetMethod(nameof(GuildmasterRuntimeFixture.LogError))!,
            ["GloomhavenVR.Quest.QuestGameScope.NotifyGuildmasterUnavailable"] = typeof(GuildmasterRuntimeFixture).GetMethod(nameof(GuildmasterRuntimeFixture.Notify))!
        };
        var translated = admissionAndGuard.ToDictionary(i => i, _ => Instruction.Create(OpCodes.Nop));
        foreach (Instruction instruction in admissionAndGuard)
        {
            Instruction copy = translated[instruction]; copy.OpCode = instruction.OpCode;
            copy.Operand = instruction.Operand switch
            {
                MethodReference method => module.ImportReference(calls[method.DeclaringType.FullName + "." + method.Name]),
                FieldReference => field,
                Instruction target => translated.GetValueOrDefault(target) ?? allowed,
                _ => instruction.Operand
            };
            il.Append(copy);
        }
        il.Append(allowed); il.Emit(OpCodes.Ret);
        var load = new MethodDefinition("Load", Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Void);
        holder.Methods.Add(load);
        foreach (Type type in new[] { typeof(object), typeof(bool), typeof(bool), typeof(Action), typeof(Action), typeof(bool) })
            load.Parameters.Add(new ParameterDefinition(module.ImportReference(type)));
        ILProcessor loader = load.Body.GetILProcessor();
        foreach (Instruction instruction in localGuard)
        {
            if (instruction.Operand is ParameterDefinition parameter) loader.Emit(instruction.OpCode, load.Parameters[parameter.Index]);
            else if (instruction.Operand is MethodReference) loader.Emit(instruction.OpCode, module.ImportReference(typeof(GuildmasterRuntimeFixture).GetMethod(nameof(GuildmasterRuntimeFixture.Notify))!));
            else loader.Emit(instruction.OpCode);
        }
        loader.Emit(OpCodes.Call, module.ImportReference(typeof(GuildmasterRuntimeFixture).GetMethod(nameof(GuildmasterRuntimeFixture.OriginalSaveLoad))!));
        loader.Emit(OpCodes.Ret);
        using var bytes = new MemoryStream(); assembly.Write(bytes); bytes.Position = 0;
        var context = new AssemblyLoadContext("guildmaster-generated-guard", isCollectible: true);
        context.Resolving += (_, name) => name.Name == typeof(GuildmasterRuntimeFixture).Assembly.GetName().Name ? typeof(GuildmasterRuntimeFixture).Assembly : null;
        try
        {
            Type fixture = context.LoadFromStream(bytes).GetType("GuardExercise", true)!;
            object instance = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(fixture);
            int guildmaster = (int)guard[3].Operand, error = (int)guard[11].Operand;
            foreach (int mode in Enumerable.Range(0, 8).Concat(new[] { -999, 999 }))
            foreach (bool hasCallback in new[] { false, true })
            {
                GuildmasterRuntimeFixture.Reset(hasCallback);
                fixture.GetField("gameData")!.SetValue(instance, new GuildmasterRuntimeFixture.Token { GameModeID = mode });
                fixture.GetMethod("Enter")!.Invoke(instance, null);
                if (mode == guildmaster)
                {
                    check(GuildmasterRuntimeFixture.OriginalAdmissions == 0 && GuildmasterRuntimeFixture.Shutdowns == 1 && GuildmasterRuntimeFixture.Notices == 1,
                        "Guildmaster reached native admission or failed to shut down and explain.");
                    check(GuildmasterRuntimeFixture.Failures == (hasCallback ? 1 : 0) && (!hasCallback || GuildmasterRuntimeFixture.LastError == error),
                        "Original optional connection failure callback lost null safety/error identity.");
                    check(string.Join(",", GuildmasterRuntimeFixture.Trace) == (hasCallback ? "failure,shutdown,notice" : "shutdown,notice"),
                        "Native failure/shutdown order differs from the original flow.");
                }
                else check(GuildmasterRuntimeFixture.OriginalAdmissions == 1 && GuildmasterRuntimeFixture.Failures == 0
                    && GuildmasterRuntimeFixture.Shutdowns == 0 && GuildmasterRuntimeFixture.Notices == 0, "Campaign/unknown mode was blocked or changed.");
            }
            foreach (bool hasCallback in new[] { false, true })
            {
                GuildmasterRuntimeFixture.Reset(hasCallback);
                fixture.GetField("gameData")!.SetValue(instance, null);
                fixture.GetMethod("Enter")!.Invoke(instance, null);
                check(GuildmasterRuntimeFixture.OriginalAdmissions == 0 && GuildmasterRuntimeFixture.InvalidTokenLogs == 1
                    && GuildmasterRuntimeFixture.Failures == (hasCallback ? 1 : 0) && GuildmasterRuntimeFixture.Shutdowns == 1
                    && GuildmasterRuntimeFixture.Notices == 0, "Missing-token native error flow was bypassed or gained an excluded-mode notice.");
            }
            foreach (bool loadMenu in new[] { false, true }) foreach (bool hasCancel in new[] { false, true })
            {
                GuildmasterRuntimeFixture.Reset(false);
                Action? callback = hasCancel ? () => GuildmasterRuntimeFixture.Trace.Add("cancel") : null;
                fixture.GetMethod("Load")!.Invoke(instance, new object?[] { null, false, loadMenu, null, callback, false });
                check(GuildmasterRuntimeFixture.Notices == 1 && GuildmasterRuntimeFixture.OriginalSaveLoads == 0
                    && GuildmasterRuntimeFixture.LastLoadMenu == loadMenu && ReferenceEquals(GuildmasterRuntimeFixture.LastCancel, callback),
                    "Imported/null party data reached native writes or lost the original cancellation arguments.");
            }
        }
        finally { context.Unload(); }
    }
}

public static class GuildmasterRuntimeFixture
{
    public sealed class Token { public int GameModeID { get; set; } }
    public delegate void FailureAction(int error);
    public sealed class ManagerData { public FailureAction? OnConnectionFailed { get; set; } }
    public static ManagerData Manager { get; private set; } = new();
    public static int OriginalAdmissions, OriginalSaveLoads, Failures, Shutdowns, Notices, LastError, InvalidTokenLogs;
    public static bool LastLoadMenu;
    public static Action? LastCancel;
    public static readonly List<string> Trace = new();
    public static void Reset(bool callback)
    {
        OriginalAdmissions = OriginalSaveLoads = Failures = Shutdowns = Notices = LastError = InvalidTokenLogs = 0;
        LastLoadMenu = false; LastCancel = null; Trace.Clear(); Manager = new ManagerData();
        if (callback) Manager.OnConnectionFailed = error => { Failures++; LastError = error; Trace.Add("failure"); };
    }
    public static void OriginalAdmission() => OriginalAdmissions++;
    public static void OriginalSaveLoad() => OriginalSaveLoads++;
    public static void LogError(string error, string message, string stack, bool logToScreen) { InvalidTokenLogs++; }
    public static void Shutdown(object? token, Action? completed) { Shutdowns++; Trace.Add("shutdown"); }
    public static void Notify(bool loadMenu, Action? cancelled) { Notices++; LastLoadMenu = loadMenu; LastCancel = cancelled; Trace.Add("notice"); }
}
