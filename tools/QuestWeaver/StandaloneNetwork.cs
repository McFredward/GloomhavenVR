using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("QuestNetwork.Tests")]

namespace QuestWeaver;

/// <summary>Only the original voice platform boundary; never transport or serializers.</summary>
internal static class StandaloneNetwork
{
    internal static readonly string[] PreservedAssemblies = {
        "GH.Runtime", "GH.Shared", "SM.Consoles", "bolt", "bolt.user", "PhotonBolt",
        "udpkit", "udpkit.common", "udpkit.platform.dotnet", "udpkit.platform.photon",
        "PhotonRealtime", "Photon3Unity3D", "PhotonVoice", "PhotonVoice.API"
    };

    /// <returns>Original game types changed by the narrow voice entry boundary.</returns>
    internal static IEnumerable<string> Apply(AssemblyDefinition game, AssemblyDefinition voiceApi, StandaloneReport report,
        string runtimeAssemblyName = "Assembly-CSharp")
    {
        TypeDefinition wrapper = voiceApi.MainModule.GetType("POpusCodec.Wrapper")
            ?? throw new InvalidDataException("Original Photon Voice Opus wrapper is absent.");
        foreach (string kind in new[] { "encoder", "decoder" })
            foreach (string operation in new[] { "get", "set" })
            {
                string name = "opus_" + kind + "_ctl_" + operation;
                MethodDefinition[] matches = wrapper.Methods.Where(m => m.Name == name && m.IsPInvokeImpl
                    && m.IsStatic && m.ReturnType.FullName == "System.Int32" && m.Parameters.Count == 3
                    && m.Parameters[0].ParameterType.FullName == "System.IntPtr"
                    && m.Parameters[1].ParameterType.FullName == "POpusCodec.Enums.OpusCtl" + (operation == "get" ? "Get" : "Set") + "Request"
                    && m.Parameters[2].ParameterType.FullName == (operation == "get" ? "System.Int32&" : "System.Int32")
                    && m.PInvokeInfo.Module.Name == "opus_egpv" && m.PInvokeInfo.EntryPoint == "opus_" + kind + "_ctl").ToArray();
                if (matches.Length != 1) throw new InvalidDataException("Original Photon Voice fixed CTL ABI changed: " + name);
                matches[0].PInvokeInfo.EntryPoint = "quest_" + name;
                report.Modifications.Add("Android ARM64 fixed-argument Opus CTL binding: " + matches[0].FullName);
            }
        TypeDefinition service = game.MainModule.GetType("VoiceChat.BoltVoiceChatService")
            ?? throw new InvalidDataException("Original Bolt voice service is absent.");
        MethodDefinition connect = service.Methods.Single(m => m.Name == "SetupAndConnect" && !m.IsStatic
            && m.ReturnType.FullName == "System.Void" && m.Parameters.Count == 0 && m.HasBody);
        MethodReference[] calls = connect.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().ToArray();
        if (calls.Length != 1 || calls[0].DeclaringType.FullName != "VoiceChat.BoltVoiceBridge"
            || calls[0].Name != "SetupAndConnect" || connect.Body.Instructions.Count != 4)
            throw new InvalidDataException("Original voice opt-in service boundary changed.");
        if (runtimeAssemblyName is not ("Assembly-CSharp" or "QuestGame.Campaign"))
            throw new InvalidDataException("Unsupported Quest runtime assembly identity.");
        AssemblyNameReference runtimeAssembly = game.MainModule.AssemblyReferences.SingleOrDefault(a => a.Name == runtimeAssemblyName)
            ?? new AssemblyNameReference(runtimeAssemblyName, new Version(0, 0, 0, 0));
        if (!game.MainModule.AssemblyReferences.Contains(runtimeAssembly)) game.MainModule.AssemblyReferences.Add(runtimeAssembly);
        TypeReference runtime = new("GloomhavenVR.Quest", "QuestGameNetwork", game.MainModule, runtimeAssembly);
        MethodReference begin = new("BeginVoice", game.MainModule.TypeSystem.Void, runtime);
        begin.Parameters.Add(new ParameterDefinition(game.MainModule.ImportReference(service)));
        connect.Body = new MethodBody(connect) { MaxStackSize = 1 };
        ILProcessor il = connect.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, begin); il.Emit(OpCodes.Ret);
        report.Modifications.Add("Android opt-in microphone callback before original voice connection: " + connect.FullName);

        MethodDefinition start = service.Methods.Single(m => m.Name == "Start" && m.HasBody && m.Parameters.Count == 0);
        Instruction[] permissionCalls = start.Body.Instructions.Where(i => i.Operand is MethodReference method
            && method.DeclaringType.FullName == "UnityEngine.Application" && method.Name == "RequestUserAuthorization"
            && method.Parameters.Count == 1 && method.Parameters[0].ParameterType.FullName == "UnityEngine.UserAuthorization").ToArray();
        if (permissionCalls.Length != 1 || permissionCalls[0].Previous.OpCode != OpCodes.Ldc_I4_2
            || permissionCalls[0].Next.OpCode != OpCodes.Pop)
            throw new InvalidDataException("Original eager microphone-permission boundary changed.");
        // Permission is requested at the player's original voice opt-in. Native
        // room callbacks, codec creation, user-data serialization and voice UI
        // remain original. Desktop inputs are never written by this builder.
        foreach (Instruction instruction in new[] { permissionCalls[0].Previous, permissionCalls[0], permissionCalls[0].Next })
        { instruction.OpCode = OpCodes.Nop; instruction.Operand = null; }
        Instruction[] announcements = start.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldstr
            && Equals(i.Operand, "Requesting user authorization for microphone")).ToArray();
        if (announcements.Length != 1) throw new InvalidDataException("Original microphone startup announcement changed.");
        announcements[0].Operand = "[Quest voice] Microphone permission is requested on voice opt-in.";
        report.Modifications.Add("Defer microphone permission to original voice opt-in: " + start.FullName);
        TypeDefinition multiplayer = game.MainModule.GetType("UIMultiplayerEscSubmenu")
            ?? throw new InvalidDataException("Original multiplayer escape submenu is absent.");
        MethodDefinition show = multiplayer.Methods.Single(m => m.Name == "Show" && !m.IsStatic && m.Parameters.Count == 0 && m.HasBody);
        Instruction[] epicReads = show.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldfld && i.Operand is FieldReference field
            && field.Name == "EpicLogin" && field.FieldType.FullName == "System.Boolean" && field.DeclaringType.FullName == "GlobalData").ToArray();
        if (epicReads.Length != 1 || epicReads[0].Next.Operand is not MethodReference active
            || active.DeclaringType.FullName != "UnityEngine.GameObject" || active.Name != "SetActive")
            throw new InvalidDataException("Original multiplayer Epic invite-panel boundary changed.");
        MethodDefinition networking = game.MainModule.GetType("PlatformLayer").Methods.Single(m => m.Name == "get_Networking"
            && m.IsStatic && m.ReturnType.FullName == "PlatformNetworking");
        MethodDefinition invites = game.MainModule.GetType("PlatformNetworking").Methods.Single(m => m.Name == "get_EPICInvitesSupported"
            && m.ReturnType.FullName == "System.Boolean" && m.Parameters.Count == 0);
        // A PC save may legitimately retain EpicLogin=true. Keep its bytes/state
        // and the original Show flow; presentation also requires the actual
        // platform capability, already false on this provider-free target.
        ILProcessor display = show.Body.GetILProcessor();
        Instruction capability = display.Create(OpCodes.Call, networking), allowed = display.Create(OpCodes.Callvirt, invites), both = display.Create(OpCodes.And);
        display.InsertAfter(epicReads[0], capability); display.InsertAfter(capability, allowed); display.InsertAfter(allowed, both);
        show.Body.MaxStackSize = Math.Max(show.Body.MaxStackSize, 3);
        report.Modifications.Add("Require excluded Epic invite capability without changing saved EpicLogin: " + show.FullName);
        return new[] { service.FullName, multiplayer.FullName };
    }
}
