using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using QuestWeaver;

if (args.Length < 3 || args.Length > 4) throw new ArgumentException("Expected original managed directory, host Opus plugin, private proof directory and optional actual runtime-compile DLL.");
string managed = Path.GetFullPath(args[0]), native = Path.GetFullPath(args[1]), proof = Path.GetFullPath(args[2]);
Directory.CreateDirectory(proof);
int assertions = 0;
void Check(bool condition, string message) { assertions++; if (!condition) throw new InvalidDataException(message); }
Dictionary<string, string> originalHashes = StandaloneNetwork.PreservedAssemblies.ToDictionary(name => name,
    name => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(managed, name + ".dll")))).ToLowerInvariant());
using var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(managed);
using var game = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "GH.Runtime.dll"), new ReaderParameters { AssemblyResolver = resolver });
using var api = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "PhotonVoice.API.dll"), new ReaderParameters { AssemblyResolver = resolver });
Dictionary<string, string> types = Discovery.AllTypes(game.MainModule).ToDictionary(type => type.FullName, ProtectedTypes.Fingerprint);
var protectedTypes = ProtectedTypes.Snapshot(game);
var report = new StandaloneReport();
string[] changed = StandaloneNetwork.Apply(game, api, report).ToArray();
Check(changed.SequenceEqual(new[] { "VoiceChat.BoltVoiceChatService", "UIMultiplayerEscSubmenu", "GHClientCallbacks", "SaveData" }),
    "Network adaptation changed more than the original voice/invite capability and explicitly excluded Guildmaster admission/load boundaries.");
foreach (TypeDefinition type in Discovery.AllTypes(game.MainModule))
    if (!changed.Contains(type.FullName)) Check(types[type.FullName] == ProtectedTypes.Fingerprint(type), "Unrelated original game/network type changed: " + type.FullName);
Check(ProtectedTypes.Verify(protectedTypes, game) == protectedTypes.Count, "Protected original gameplay changed.");
TypeDefinition menu = game.MainModule.GetType("UIMultiplayerEscSubmenu");
MethodDefinition menuShow = menu.Methods.Single(method => method.Name == "Show");
var epicRead = menuShow.Body.Instructions.Single(instruction => instruction.Operand is FieldReference field && field.Name == "EpicLogin");
Check(epicRead.Next.Operand is MethodReference platform && platform.Name == "get_Networking"
    && epicRead.Next.Next.Operand is MethodReference permission && permission.Name == "get_EPICInvitesSupported"
    && epicRead.Next.Next.Next.OpCode == Mono.Cecil.Cil.OpCodes.And,
    "Epic invite display lost its genuine platform capability or changed the saved flag.");
using (var originalForMenu = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "GH.Runtime.dll")))
    foreach (MethodDefinition method in menu.Methods.Where(method => method.Name != "Show"))
    {
        MethodDefinition baseline = originalForMenu.MainModule.GetType(menu.FullName).Methods.Single(item => item.FullName == method.FullName);
        Check(method.HasBody == baseline.HasBody && (!method.HasBody || method.Body.Instructions.Select(instruction => instruction.ToString())
            .SequenceEqual(baseline.Body.Instructions.Select(instruction => instruction.ToString()))), "Unrelated original multiplayer UI method changed.");
    }
var metadataAssemblies = StandaloneNetwork.PreservedAssemblies.ToDictionary(name => name,
    name => AssemblyDefinition.ReadAssembly(Path.Combine(managed, name + ".dll"), new ReaderParameters { AssemblyResolver = resolver }));
try
{
    TypeDefinition dynamicData = metadataAssemblies["PhotonBolt"].MainModule.GetType("Photon.Bolt.BoltDynamicData");
    Check(dynamicData != null && dynamicData.Methods.Any(method => method.Name == "Setup" && method.IsStatic && method.IsPublic && method.HasBody), "Original BoltDynamicData reflected startup entry is absent.");
    foreach (string name in new[] { "SocketUdpAsync", "SocketTcpAsync" })
    {
        TypeDefinition socket = metadataAssemblies["Photon3Unity3D"].MainModule.GetType("ExitGames.Client.Photon." + name);
        Check(socket.Methods.Any(method => method.IsConstructor && method.IsPublic && method.Parameters.Count == 1
            && method.Parameters[0].ParameterType.FullName == "ExitGames.Client.Photon.PeerBase"), "Original reflected managed socket constructor is absent.");
    }
    foreach (TypeDefinition type in Discovery.AllTypes(metadataAssemblies["bolt.user"].MainModule)
        .Where(type => type.BaseType?.FullName == "Photon.Bolt.NetworkState_Meta"))
        Check(type.Fields.Any(field => field.Name == "Instance" && field.IsStatic && !field.IsPublic), "Original Bolt state meta reflected singleton is absent.");
    var tokens = new HashSet<string>();
    foreach (string callbackName in new[] { "FFSNet.NetworkCallbacks", "GHNetworkCallbacks" })
    {
        MethodDefinition callback = metadataAssemblies["GH.Runtime"].MainModule.GetType(callbackName).Methods.Single(method => method.Name == "BoltStartBegin");
        foreach (GenericInstanceMethod registration in callback.Body.Instructions.Select(instruction => instruction.Operand)
            .OfType<GenericInstanceMethod>().Where(method => method.Name == "RegisterTokenClass"))
        {
            TypeDefinition token = registration.GenericArguments.Single().Resolve();
            Check(token != null && token.Methods.Any(method => method.IsConstructor && method.IsPublic && method.Parameters.Count == 0), "Original reflected Bolt token constructor is absent.");
            tokens.Add(token!.FullName);
        }
    }
    Check(tokens.Count >= 40, "Original token registration closure unexpectedly shrank.");
    foreach (var pair in metadataAssemblies.Where(pair => !new[] { "GH.Runtime", "GH.Shared", "SM.Consoles" }.Contains(pair.Key)))
        foreach (MethodDefinition method in Discovery.AllTypes(pair.Value.MainModule).SelectMany(type => type.Methods).Where(method => method.HasBody))
            Check(!method.Body.Instructions.Any(instruction => instruction.Operand is MethodReference call
                && call.DeclaringType.FullName.StartsWith("System.Reflection.Emit.", StringComparison.Ordinal)), "Original network SDK contains an unhandled Reflection.Emit call.");
}
finally { foreach (AssemblyDefinition assembly in metadataAssemblies.Values) assembly.Dispose(); }
TypeDefinition wrapper = api.MainModule.GetType("POpusCodec.Wrapper");
foreach (MethodDefinition method in wrapper.Methods.Where(method => method.IsPInvokeImpl))
{
    bool ctl = method.Name.Contains("_ctl_", StringComparison.Ordinal);
    Check(method.PInvokeInfo.Module.Name == "opus_egpv", "Codec plugin module changed.");
    Check(method.PInvokeInfo.EntryPoint == (ctl ? "quest_" + method.Name : method.Name), "Codec export boundary changed beyond the four fixed CTL imports.");
}
string generated = Path.Combine(proof, "generated");
Directory.CreateDirectory(generated);
game.Write(Path.Combine(generated, "GH.Runtime.dll"));
api.Write(Path.Combine(generated, "PhotonVoice.API.dll"));
// The actual transformed SDK is executed; this is not a mirror of its managed codec implementation.
var original = new AssemblyLoadContext("quest-network-original", true);
var adapted = new AssemblyLoadContext("quest-network-adapted", true);
foreach (AssemblyLoadContext context in new[] { original, adapted })
    context.Resolving += (loader, name) =>
    {
        string candidate = Path.Combine(loader == adapted ? generated : managed, name.Name + ".dll");
        if (!File.Exists(candidate)) candidate = Path.Combine(managed, name.Name + ".dll");
        return File.Exists(candidate) ? loader.LoadFromAssemblyPath(candidate) : null;
    };
Assembly originalGame = original.LoadFromAssemblyPath(Path.Combine(managed, "GH.Runtime.dll"));
Assembly adaptedGame = adapted.LoadFromAssemblyPath(Path.Combine(generated, "GH.Runtime.dll"));

object Packet(AssemblyLoadContext loader)
{
    Type packet = loader.LoadFromAssemblyPath(Path.Combine(managed, "udpkit.dll")).GetType("UdpKit.UdpPacket", true)!;
    ConstructorInfo ctor = packet.GetConstructors().Single(item => item.GetParameters().Length == 2);
    return ctor.Invoke(new object?[] { new byte[1024 * 64], null });
}
byte[] Encode(object token, AssemblyLoadContext loader)
{
    object packet = Packet(loader);
    token.GetType().GetMethod("Write")!.Invoke(token, new[] { packet });
    return (byte[])packet.GetType().GetMethod("DuplicateData")!.Invoke(packet, null)!;
}
object Decode(byte[] bytes, string name, Assembly assembly, AssemblyLoadContext loader)
{
    Type packetType = loader.LoadFromAssemblyPath(Path.Combine(managed, "udpkit.dll")).GetType("UdpKit.UdpPacket", true)!;
    object packet = packetType.GetConstructors().Single(item => item.GetParameters().Length == 2).Invoke(new object?[] { bytes, null });
    object token = Activator.CreateInstance(assembly.GetType(name, true)!)!;
    token.GetType().GetMethod("Read")!.Invoke(token, new[] { packet });
    return token;
}
object User(Assembly assembly, bool crossplay, byte[]? key) => Activator.CreateInstance(assembly.GetType("FFSNet.UserToken", true)!,
    0, "private-test-password", "Local ünicode owner", "76561198000000000", "2", "", "Steam", crossplay, "39734272", key)!;
foreach (bool crossplay in new[] { false, true }) foreach (bool recentKey in new[] { false, true })
{
    byte[]? key = recentKey ? Enumerable.Range(0, 64).Select(i => (byte)(i * 3)).ToArray() : null;
    byte[] baseline = Encode(User(originalGame, crossplay, key), original);
    byte[] candidate = Encode(User(adaptedGame, crossplay, key), adapted);
    Check(baseline.SequenceEqual(candidate), "Original/Quest UserToken bytes differ.");
    object remote = Decode(baseline, "FFSNet.UserToken", adaptedGame, adapted);
    foreach (string property in new[] { "Username", "PlatformPlayerID", "GameVersion", "PlatformName", "PlatformNetworkAccountPlayerID", "CrossplayEnabled" })
        Check(Equals(remote.GetType().GetProperty(property)!.GetValue(remote), User(originalGame, crossplay, key).GetType().GetProperty(property)!.GetValue(User(originalGame, crossplay, key))), "Original-to-Quest UserToken property differs: " + property);
    Check(Encode(remote, adapted).SequenceEqual(baseline), "Quest reserialization changed original admission bytes.");
    Check(Encode(Decode(candidate, "FFSNet.UserToken", originalGame, original), original).SequenceEqual(candidate), "Quest-to-original UserToken differs.");
}
foreach (int bytes in new[] { 0, 1, 7680, 12288 })
{
    byte[] content = Enumerable.Range(0, bytes).Select(i => (byte)(i * 17)).ToArray();
    object token = Activator.CreateInstance(originalGame.GetType("FFSNet.CustomDataToken", true)!, content, false)!;
    byte[] baseline = Encode(token, original);
    object received = Decode(baseline, "FFSNet.CustomDataToken", adaptedGame, adapted);
    Check(((byte[])received.GetType().GetProperty("CustomData")!.GetValue(received)!).SequenceEqual(content), "Original save/side-channel byte payload changed.");
    Check(Encode(received, adapted).SequenceEqual(baseline), "Quest save/side-channel framing changed.");
}
object Game(Assembly assembly, uint dlc)
{
    object token = Activator.CreateInstance(assembly.GetType("GameToken", true)!)!;
    var properties = new Dictionary<string, object> {
        ["CustomData"] = new byte[] { 3, 7, 23 }, ["GameModeID"] = 1, ["SaveName"] = "Campaign ünicode",
        ["SaveHash"] = "test-save-hash", ["HostPlayerID"] = "76561198000000000", ["HostAccountID"] = "39734272",
        ["HostNetworkAccountID"] = "39734272", ["HostUsername"] = "Local owner", ["HostPlatformName"] = "Steam",
        ["RulesetHash"] = "original-rules-hash", ["DLCFlag"] = dlc, ["IsCrossplaySession"] = true,
        ["CurrentPlatformUsersInSession"] = new HashSet<string> { "Steam", "EpicGamesStore", "GoGGalaxy" }
    };
    foreach (var pair in properties)
    {
        PropertyInfo inherited = token.GetType().GetProperty(pair.Key)!;
        PropertyInfo property = inherited.DeclaringType!.GetProperty(pair.Key, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
        property.SetValue(token, pair.Value);
    }
    return token;
}
foreach (uint dlc in new uint[] { 0, 1, 2, 3, 4, 7 })
{
    byte[] bytes = Encode(Game(originalGame, dlc), original);
    Check(Encode(Game(adaptedGame, dlc), adapted).SequenceEqual(bytes), "Original/Quest host GameToken differs, including DLC or save payload.");
    object received = Decode(bytes, "GameToken", adaptedGame, adapted);
    Check((uint)received.GetType().GetProperty("DLCFlag")!.GetValue(received)! == dlc, "Received host DLC flags were hidden or altered.");
    Check(((byte[])received.GetType().GetProperty("CustomData")!.GetValue(received)!).SequenceEqual(new byte[] { 3, 7, 23 }), "Received host save bytes changed.");
    Check(Encode(received, adapted).SequenceEqual(bytes), "Quest host-token reserialization changed PC save/session metadata.");
}

Assembly voiceApi = adapted.LoadFromAssemblyPath(Path.Combine(generated, "PhotonVoice.API.dll"));
IntPtr nativeHandle = NativeLibrary.Load(native);
NativeLibrary.SetDllImportResolver(voiceApi, (name, _, _) => name == "opus_egpv" ? nativeHandle : IntPtr.Zero);
Type encoderType = voiceApi.GetType("POpusCodec.OpusEncoder", true)!;
ConstructorInfo encoderCtor = encoderType.GetConstructors().Single();
Type[] codecArgs = encoderCtor.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
var measurements = new List<object>();
foreach (int frequency in new[] { 8000, 16000, 48000 }) foreach (int channels in new[] { 1, 2 }) foreach (Type sampleType in new[] { typeof(float), typeof(short) })
{
    object encoder = encoderCtor.Invoke(new[] { Enum.ToObject(codecArgs[0], frequency), Enum.ToObject(codecArgs[1], channels), 24000,
        Enum.ToObject(codecArgs[3], 2048), Enum.ToObject(codecArgs[4], 40) });
    encoderType.GetProperty("Bitrate")!.SetValue(encoder, 32000);
    Check((int)encoderType.GetProperty("Bitrate")!.GetValue(encoder)! == 32000, "Original fixed-argument native CTL get/set failed.");
    Type decoderType = voiceApi.GetType("POpusCodec.OpusDecoder`1", true)!.MakeGenericType(sampleType);
    object decoder = Activator.CreateInstance(decoderType, Enum.ToObject(codecArgs[0], frequency), Enum.ToObject(codecArgs[1], channels))!;
    Type frameType = voiceApi.GetType("Photon.Voice.FrameBuffer", true)!, flags = voiceApi.GetType("Photon.Voice.FrameFlags", true)!;
    ConstructorInfo frameCtor = frameType.GetConstructors().Single(ctor => ctor.GetParameters().Length == 2);
    MethodInfo encode = encoderType.GetMethod("Encode", new[] { sampleType.MakeArrayType() })!;
    int samples = frequency / 50, decodedSamples = 0, nonempty = 0;
    double energy = 0;
    for (int frame = 0; frame < 12; frame++)
    {
        Array pcm = Array.CreateInstance(sampleType, samples * channels);
        for (int sample = 0; sample < samples; sample++) for (int channel = 0; channel < channels; channel++)
        {
            double tone = 0.15 * Math.Sin(2 * Math.PI * (330 + 110 * channel) * (sample + frame * samples) / frequency);
            if (sampleType == typeof(float)) pcm.SetValue((float)tone, sample * channels + channel);
            else pcm.SetValue((short)(tone * short.MaxValue), sample * channels + channel);
        }
        var compressed = (ArraySegment<byte>)encode.Invoke(encoder, new object[] { pcm })!;
        Check(compressed.Count > 1 && compressed.Count < 4000, "Actual original encoder produced no valid Opus packet.");
        object packet = frameCtor.Invoke(new[] { compressed.ToArray(), Enum.ToObject(flags, 0) });
        object?[] input = { packet };
        Array decoded = (Array)decoderType.GetMethod("DecodePacket")!.Invoke(decoder, input)!;
        frameType.GetMethod("Release")!.Invoke(input[0], null);
        if (decoded.Length > 0) nonempty++;
        Check(decoded.Length == 0 || decoded.Length == samples * channels, "Actual original decoder changed frame/channel duration.");
        foreach (object value in decoded) { double normalized = Convert.ToDouble(value) / (sampleType == typeof(short) ? short.MaxValue : 1); energy += normalized * normalized; decodedSamples++; }
    }
    double rms = Math.Sqrt(energy / Math.Max(1, decodedSamples));
    Check(nonempty >= 10 && rms > 0.02 && rms < 0.25, "Actual original Opus encode/decode returned silent or invalid PCM.");
    measurements.Add(new { frequency, channels, sampleType = sampleType.Name, decodedSamples, rms });
    ((IDisposable)decoder).Dispose(); ((IDisposable)encoder).Dispose();
}
foreach (var pair in originalHashes)
    Check(pair.Value == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(managed, pair.Key + ".dll")))).ToLowerInvariant(), "Canonical original managed network input was written.");
int dictionaryCases = 0;
if (args.Length == 4)
{
    Assembly runtimeCompile = adapted.LoadFromAssemblyPath(Path.GetFullPath(args[3]));
    dictionaryCases = (int)runtimeCompile.GetType("GloomhavenVR.Quest.QuestNetworkAot", true)!.GetMethod("ValidatePhotonWire")!.Invoke(null, null)!;
    Check(dictionaryCases == 251, "Actual original Protocol16/18 AOT dictionary cases were lost.");
    runtimeCompile.GetType("GloomhavenVR.Quest.QuestNetworkAot", true)!.GetMethod("ValidatePayloadCrypto")!.Invoke(null, null);
}
File.WriteAllText(Path.Combine(proof, "original-network-codec.json"), JsonSerializer.Serialize(new {
    schema = 1, assertions, originalHashes, adaptations = report.Modifications,
    actualOriginalCodecCases = measurements, dictionaryCases, nativeSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(native))).ToLowerInvariant(),
    androidConnected = false, pcRoomAdmissionTested = false, hardwareVoiceTested = false,
    hostRuntime = ".NET8 Linux (original managed bodies); Unity PC/Android acceptance remains a separate test"
}, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine($"Original network/codec proof: {assertions} assertions; 12 actual original codec paths; original admission/save/side-channel bytes preserved.");
