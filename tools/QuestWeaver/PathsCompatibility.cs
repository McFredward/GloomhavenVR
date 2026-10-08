using Mono.Cecil;
using Mono.Cecil.Cil;

namespace QuestWeaver;

/// <summary>Emits an explicitly initialized, immutable local-path snapshot without Unity getters.</summary>
internal static class PathsCompatibility
{
    internal static AssemblyDefinition Create(ModuleDefinition game)
    {
        var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("QuestGame.Compatibility", new Version(1, 0, 0, 0)),
            "QuestGame.Compatibility", ModuleKind.Dll);
        ModuleDefinition module = assembly.MainModule;
        module.Runtime = game.Runtime;
        var type = new TypeDefinition("QuestGame.Compatibility", "Paths", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            module.TypeSystem.Object);
        module.Types.Add(type);
        var snapshot = new FieldDefinition("snapshot", FieldAttributes.Private | FieldAttributes.Static, module.TypeSystem.Object);
        type.Fields.Add(snapshot);
        var strings = new ArrayType(module.TypeSystem.String);
        TypeReference pathType = new("System.IO", "Path", module, module.TypeSystem.CoreLibrary);
        TypeReference comparisonType = new("System", "StringComparison", module, module.TypeSystem.CoreLibrary, true);
        MethodReference PathMethod(string name, TypeReference result, params TypeReference[] arguments)
        {
            var method = new MethodReference(name, result, pathType);
            foreach (TypeReference argument in arguments) method.Parameters.Add(new ParameterDefinition(argument));
            return method;
        }
        MethodReference combine = PathMethod("Combine", module.TypeSystem.String, module.TypeSystem.String, module.TypeSystem.String);
        MethodReference rooted = PathMethod("IsPathRooted", module.TypeSystem.Boolean, module.TypeSystem.String);
        MethodReference fullPath = PathMethod("GetFullPath", module.TypeSystem.String, module.TypeSystem.String);
        MethodReference pathRoot = PathMethod("GetPathRoot", module.TypeSystem.String, module.TypeSystem.String);
        var whitespace = new MethodReference("IsNullOrWhiteSpace", module.TypeSystem.Boolean, module.TypeSystem.String);
        whitespace.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        var indexOf = new MethodReference("IndexOf", module.TypeSystem.Int32, module.TypeSystem.String) { HasThis = true };
        indexOf.Parameters.Add(new ParameterDefinition(module.TypeSystem.String)); indexOf.Parameters.Add(new ParameterDefinition(comparisonType));
        var equals = new MethodReference("Equals", module.TypeSystem.Boolean, module.TypeSystem.String);
        equals.Parameters.Add(new ParameterDefinition(module.TypeSystem.String)); equals.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        equals.Parameters.Add(new ParameterDefinition(comparisonType));
        var exchange = new MethodReference("CompareExchange", module.TypeSystem.Object,
            new TypeReference("System.Threading", "Interlocked", module, module.TypeSystem.CoreLibrary));
        exchange.Parameters.Add(new ParameterDefinition(new ByReferenceType(module.TypeSystem.Object)));
        exchange.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object)); exchange.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
        MethodReference ExceptionConstructor(string name, int arguments)
        {
            var constructor = new MethodReference(".ctor", module.TypeSystem.Void,
                new TypeReference("System", name, module, module.TypeSystem.CoreLibrary)) { HasThis = true };
            for (int i = 0; i < arguments; i++) constructor.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
            return constructor;
        }
        MethodReference invalidOperation = ExceptionConstructor("InvalidOperationException", 1);
        MethodReference argumentException = ExceptionConstructor("ArgumentException", 2);

        // B614 reached YMLLoading.Init on its original worker and failed because the old
        // helper called Application.persistentDataPath there. The bootstrap now supplies
        // that Unity value once on the main thread, before creating the mod/original scenes.
        // Publish one complete snapshot with Interlocked: readers can observe either an
        // explicit initialization failure or all three paths, never a partially filled cache.
        // No getter may recover by querying Unity; a changed root requires an app restart.
        var initialize = new MethodDefinition("Initialize", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
        initialize.Parameters.Add(new ParameterDefinition("persistentDataPath", ParameterAttributes.None, module.TypeSystem.String));
        type.Methods.Add(initialize);
        initialize.Body.InitLocals = true;
        var normalized = new VariableDefinition(module.TypeSystem.String);
        var candidate = new VariableDefinition(strings);
        var existing = new VariableDefinition(module.TypeSystem.Object);
        initialize.Body.Variables.Add(normalized); initialize.Body.Variables.Add(candidate); initialize.Body.Variables.Add(existing);
        ILProcessor il = initialize.Body.GetILProcessor();
        Instruction invalid = Instruction.Create(OpCodes.Ldstr, "Quest persistentDataPath must be a fully qualified local filesystem path.");
        Instruction done = Instruction.Create(OpCodes.Ret);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, whitespace); il.Emit(OpCodes.Brtrue, invalid);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, rooted); il.Emit(OpCodes.Brfalse, invalid);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldstr, "://"); il.Emit(OpCodes.Ldc_I4, (int)StringComparison.Ordinal);
        il.Emit(OpCodes.Callvirt, indexOf); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Bge, invalid);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, fullPath); il.Emit(OpCodes.Stloc, normalized);
        // IsPathRooted also accepts drive-relative paths on Windows. Their normalized
        // root differs, so never resolve an ambiguous root through the process directory.
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, pathRoot);
        il.Emit(OpCodes.Ldloc, normalized); il.Emit(OpCodes.Call, pathRoot);
        il.Emit(OpCodes.Ldc_I4, (int)StringComparison.Ordinal); il.Emit(OpCodes.Call, equals); il.Emit(OpCodes.Brfalse, invalid);
        il.Emit(OpCodes.Ldc_I4_3); il.Emit(OpCodes.Newarr, module.TypeSystem.String); il.Emit(OpCodes.Stloc, candidate);
        il.Emit(OpCodes.Ldloc, candidate); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldloc, normalized);
        il.Emit(OpCodes.Ldstr, "quest-owned-game"); il.Emit(OpCodes.Call, combine); il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, candidate); il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, candidate); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldstr, "StreamingAssets"); il.Emit(OpCodes.Call, combine); il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, candidate); il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Ldloc, normalized); il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldsflda, snapshot); il.Emit(OpCodes.Ldloc, candidate); il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, exchange); il.Emit(OpCodes.Stloc, existing);
        il.Emit(OpCodes.Ldloc, existing); il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, existing); il.Emit(OpCodes.Castclass, strings); il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldloc, normalized); il.Emit(OpCodes.Ldc_I4, (int)StringComparison.Ordinal); il.Emit(OpCodes.Call, equals);
        il.Emit(OpCodes.Brtrue, done);
        il.Emit(OpCodes.Ldstr, "Quest paths are already initialized for another root; an app restart is required.");
        il.Emit(OpCodes.Newobj, invalidOperation); il.Emit(OpCodes.Throw); il.Append(done);
        il.Append(invalid); il.Emit(OpCodes.Ldstr, "persistentDataPath"); il.Emit(OpCodes.Newobj, argumentException); il.Emit(OpCodes.Throw);

        foreach ((string name, int index) in new[] { ("get_dataPath", 0), ("get_streamingAssetsPath", 1), ("get_persistentDataPath", 2) })
        {
            var getter = new MethodDefinition(name, MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.String);
            type.Methods.Add(getter);
            ILProcessor get = getter.Body.GetILProcessor();
            Instruction ready = Instruction.Create(OpCodes.Castclass, strings);
            get.Emit(OpCodes.Ldsflda, snapshot); get.Emit(OpCodes.Ldnull); get.Emit(OpCodes.Ldnull); get.Emit(OpCodes.Call, exchange);
            get.Emit(OpCodes.Dup); get.Emit(OpCodes.Brtrue, ready); get.Emit(OpCodes.Pop);
            get.Emit(OpCodes.Ldstr, "Quest paths are unavailable: call Paths.Initialize on the main thread before starting the mod or original Bootstrap.");
            get.Emit(OpCodes.Newobj, invalidOperation); get.Emit(OpCodes.Throw);
            get.Append(ready); get.Emit(OpCodes.Ldc_I4, index); get.Emit(OpCodes.Ldelem_Ref); get.Emit(OpCodes.Ret);
        }

        // Unity 2021.3 appends CaptureScreenshot's filename to persistentDataPath on
        // Android. The original error coroutine passes an already absolute save alias.
        // Adapt only that call argument; retain its end-of-frame yield, capture API,
        // original absolute ScreenCaptureImagePath and UI hide/show continuation.
        var screenshot = new MethodDefinition("GetMobileScreenshotFilename", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.String);
        screenshot.Parameters.Add(new ParameterDefinition("absolutePath", ParameterAttributes.None, module.TypeSystem.String));
        type.Methods.Add(screenshot);
        ILProcessor screen = screenshot.Body.GetILProcessor();
        Instruction screenshotReady = Instruction.Create(OpCodes.Ldstr, "ErrorSceenCap.png");
        screen.Emit(OpCodes.Ldarg_0); screen.Emit(OpCodes.Call, type.Methods.Single(m => m.Name == "get_persistentDataPath"));
        screen.Emit(OpCodes.Ldstr, "ErrorSceenCap.png"); screen.Emit(OpCodes.Call, combine);
        screen.Emit(OpCodes.Ldc_I4, (int)StringComparison.Ordinal); screen.Emit(OpCodes.Call, equals); screen.Emit(OpCodes.Brtrue, screenshotReady);
        screen.Emit(OpCodes.Ldstr, "Original error screenshot path no longer matches the initialized persistent root.");
        screen.Emit(OpCodes.Ldstr, "absolutePath"); screen.Emit(OpCodes.Newobj, argumentException); screen.Emit(OpCodes.Throw);
        screen.Append(screenshotReady); screen.Emit(OpCodes.Ret);
        return assembly;
    }

    internal static void RebindCalls(ModuleDefinition game, TypeDefinition paths, ISet<string> changedTypes, List<string> modifications)
    {
        foreach (MethodDefinition method in Discovery.AllTypes(game).SelectMany(t => t.Methods).Where(m => m.HasBody && !Discovery.Protected(m.DeclaringType)))
            foreach (Instruction instruction in method.Body.Instructions)
                if (instruction.Operand is MethodReference call && call.DeclaringType.FullName == "UnityEngine.Application"
                    && call.Name is "get_dataPath" or "get_streamingAssetsPath" or "get_persistentDataPath")
                {
                    if (instruction.OpCode != OpCodes.Call || call.HasThis || call.Parameters.Count != 0 || call.ReturnType.FullName != "System.String"
                        || call.HasGenericParameters || call.DeclaringType.IsGenericInstance)
                        throw new InvalidDataException("Original Unity filesystem getter ABI changed: " + method.FullName + " -> " + call.FullName);
                    instruction.Operand = game.ImportReference(paths.Methods.Single(m => m.Name == call.Name));
                    changedTypes.Add(method.DeclaringType.FullName); modifications.Add("cached local content path: " + method.FullName + "@" + instruction.Offset);
                }
    }

    internal static void BindErrorScreenshot(ModuleDefinition game, TypeDefinition paths, ISet<string> changedTypes, List<string> modifications)
    {
        MethodDefinition[] methods = Discovery.AllTypes(game)
            .Where(t => t.DeclaringType?.FullName == "GloomUtility" && t.Name.StartsWith("<TakeErrorScreenshot>", StringComparison.Ordinal))
            .SelectMany(t => t.Methods).Where(m => m.Name == "MoveNext" && m.HasBody && m.ReturnType.FullName == "System.Boolean").ToArray();
        if (methods.Length != 1 || Discovery.Protected(methods[0].DeclaringType))
            throw new InvalidDataException("Original error screenshot coroutine ABI changed.");
        MethodDefinition method = methods[0];
        Instruction[] captures = method.Body.Instructions.Where(i => i.Operand is MethodReference call
            && call.DeclaringType.FullName == "UnityEngine.ScreenCapture" && call.Name == "CaptureScreenshot").ToArray();
        if (captures.Length != 1 || captures[0].OpCode != OpCodes.Call || captures[0].Operand is not MethodReference capture
            || capture.HasThis || capture.ReturnType.FullName != "System.Void" || capture.Parameters.Count != 1
            || capture.HasGenericParameters || capture.DeclaringType.IsGenericInstance
            || capture.Parameters[0].ParameterType.FullName != "System.String" || captures[0].Previous?.OpCode != OpCodes.Call
            || captures[0].Previous?.Operand is not MethodReference originalPath || originalPath.DeclaringType.FullName != "RootSaveData"
            || originalPath.Name != "get_ScreenCaptureImagePath" || originalPath.HasThis || originalPath.Parameters.Count != 0
            || originalPath.ReturnType.FullName != "System.String" || originalPath.HasGenericParameters || originalPath.DeclaringType.IsGenericInstance)
            throw new InvalidDataException("Original error screenshot path/capture boundary changed: " + method.FullName);
        if (method.Body.Instructions.Any(i => i.Operand is Instruction branch && branch == captures[0]
            || i.Operand is Instruction[] branches && branches.Contains(captures[0]))
            || method.Body.ExceptionHandlers.Any(h => h.TryStart == captures[0] || h.TryEnd == captures[0]
                || h.HandlerStart == captures[0] || h.HandlerEnd == captures[0] || h.FilterStart == captures[0]))
            throw new InvalidDataException("Original error screenshot capture gained a separate control-flow boundary: " + method.FullName);
        method.Body.GetILProcessor().InsertBefore(captures[0], Instruction.Create(OpCodes.Call,
            game.ImportReference(paths.Methods.Single(m => m.Name == "GetMobileScreenshotFilename"))));
        changedTypes.Add(method.DeclaringType.FullName); modifications.Add("mobile error screenshot filename at original capture boundary: " + method.FullName);
    }
}
