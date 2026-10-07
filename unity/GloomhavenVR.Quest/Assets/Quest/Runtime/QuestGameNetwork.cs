#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using FFSNet;
using GloomhavenVR.Core;
using Photon.Bolt;
using Photon.Voice.Unity;
using Photon.Voice;
using POpusCodec;
using POpusCodec.Enums;
using UdpKit;
using UnityEngine;
using UnityEngine.Android;
using VoiceChat;

namespace GloomhavenVR.Quest
{
    /// <summary>Native microphone permission and original network lifecycle evidence.</summary>
    public sealed class QuestGameNetwork : MonoBehaviour
    {
        [DllImport("opus_egpv", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr opus_get_version_string();

        static QuestGameNetwork instance;
        BoltVoiceChatService pendingVoice;
        PermissionCallbacks permissionCallbacks;
        bool awaitingPermission, previousRunInBackground, initialized;
        float nextSample;
        string lastState;
        public static bool NativeVoiceAvailable { get; private set; }
        public static bool LocalSerializationReady { get; private set; }
        public static string LastState { get; private set; } = "not-started";

        void Awake()
        {
            if (Application.platform != RuntimePlatform.Android) { enabled = false; return; }
            if (instance != null && instance != this) { Destroy(this); return; }
            instance = this;
            // Bootstrap adds this before the shared mod publishes its central
            // platform capability. Defer all adapters until that real boundary.
            if (QuestStandalonePlatform.Enabled) Initialize();
        }

        void Initialize()
        {
            if (initialized || !QuestStandalonePlatform.Enabled) return;
            initialized = true;
            previousRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            try
            {
                string version = Marshal.PtrToStringAnsi(opus_get_version_string());
                if (string.IsNullOrEmpty(version) || !version.StartsWith("libopus ", StringComparison.Ordinal))
                    throw new InvalidOperationException("Native codec returned an invalid version.");
                NativeVoiceAvailable = true;
                ValidateLocalCodec();
                Debug.Log("[Quest network] Original Photon/Bolt protocol retained; native Opus available. Voice/backend connection is not implied.");
            }
            catch (Exception failure)
            {
                NativeVoiceAvailable = false;
                Debug.LogError("[Quest network] Native Opus load failed: " + failure.GetType().Name + ". Voice opt-in cannot start.");
            }
            try
            {
                ValidateLocalSerialization();
                if (QuestStandalonePlatform.DebugLogging)
                {
                    Debug.Log("[Quest network] Original Photon typed-dictionary cold-path cases=" + QuestNetworkAot.ValidatePhotonWire() + ".");
                    QuestNetworkAot.ValidatePayloadCrypto();
                }
                LocalSerializationReady = true;
                Debug.Log("[Quest network] Original local admission/save/side-channel serializers passed their actual player cold-path check. PC room admission remains unverified.");
            }
            catch (Exception failure)
            {
                LocalSerializationReady = false;
                Debug.LogError("[Quest network] Original player serializer cold-path failed: " + failure.GetType().Name + ": " + failure.Message);
            }
        }

        public static void BeginVoice(BoltVoiceChatService service)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (!QuestStandalonePlatform.Enabled) { BoltVoiceBridge.Instance.SetupAndConnect(); return; }
            if (instance == null) service.gameObject.AddComponent<QuestGameNetwork>();
            instance.Initialize();
            instance.RequestVoice(service);
        }

        void RequestVoice(BoltVoiceChatService service)
        {
            if (!NativeVoiceAvailable) { Debug.LogError("[Quest voice] Voice was requested, but its native codec is unavailable."); return; }
            if (awaitingPermission) return;
            pendingVoice = service;
            if (Application.platform != RuntimePlatform.Android || Permission.HasUserAuthorizedPermission(Permission.Microphone))
            { CompletePermission(true); return; }
            awaitingPermission = true;
            permissionCallbacks = new PermissionCallbacks();
            permissionCallbacks.PermissionGranted += OnPermissionGranted;
            permissionCallbacks.PermissionDenied += OnPermissionDenied;
            permissionCallbacks.PermissionDeniedAndDontAskAgain += OnPermissionDenied;
            Debug.Log("[Quest voice] Microphone permission requested by original voice opt-in.");
            Permission.RequestUserPermission(Permission.Microphone, permissionCallbacks);
        }

        void OnPermissionGranted(string permission) { if (permission == Permission.Microphone) CompletePermission(true); }
        void OnPermissionDenied(string permission) { if (permission == Permission.Microphone) CompletePermission(false); }

        void CompletePermission(bool granted)
        {
            awaitingPermission = false;
            ReleasePermissionCallbacks();
            BoltVoiceChatService service = pendingVoice;
            pendingVoice = null;
            if (!granted || service == null)
            { Debug.LogWarning("[Quest voice] Microphone permission denied or original voice service unavailable; no voice connection started."); return; }
            Recorder recorder = service.Recorder;
            BoltVoiceBridge bridge = BoltVoiceBridge.Instance;
            if (recorder == null || bridge == null)
            { Debug.LogError("[Quest voice] Original recorder/bridge has not initialized; no voice connection started."); return; }
            // The supplied SDK is Windows-compiled. Its Photon mic factory is a
            // WindowsAudioInPusher even under Android. Unity's real microphone
            // path is already in that exact SDK, including resampling, recorder
            // pause/resume and original Opus framing. Do not fall back to AudioIn.dll.
            recorder.MicrophoneType = Recorder.MicType.Unity;
            recorder.UseMicrophoneTypeFallback = false;
            recorder.StopRecordingWhenPaused = true;
            bridge.SetupAndConnect();
            Debug.Log("[Quest voice] Permission ready; original voice room connection requested with Unity microphone.");
        }

        void ReleasePermissionCallbacks()
        {
            if (permissionCallbacks == null) return;
            permissionCallbacks.PermissionGranted -= OnPermissionGranted;
            permissionCallbacks.PermissionDenied -= OnPermissionDenied;
            permissionCallbacks.PermissionDeniedAndDontAskAgain -= OnPermissionDenied;
            permissionCallbacks = null;
        }

        void Update()
        {
            if (!QuestStandalonePlatform.Enabled) return;
            Initialize();
            if (Time.unscaledTime < nextSample) return;
            nextSample = Time.unscaledTime + 2;
            string state = !BoltNetwork.IsRunning ? "stopped" : BoltNetwork.IsServer ? "server-running" : "client-running";
            BoltVoiceBridge bridge = BoltVoiceBridge.Instance;
            state += bridge != null && bridge.IsConnected ? ";voice-connected" : ";voice-disconnected";
            LastState = state;
            if (state == lastState) return;
            lastState = state;
            Debug.Log("[Quest network] Original transport lifecycle " + state + ".");
        }

        void OnApplicationPause(bool paused)
        {
            if (!QuestStandalonePlatform.Enabled) return;
            nextSample = 0;
            Debug.Log("[Quest network] Application " + (paused ? "suspended" : "resumed")
                + "; original Bolt acknowledgements/timeouts and Recorder pause/resume remain owners.");
            // Never create a second connection or mutate authoritative gameplay
            // here. Original Photon/Bolt callbacks handle a real connection loss.
        }

        void OnDestroy()
        {
            ReleasePermissionCallbacks();
            if (instance != this) return;
            instance = null;
            if (initialized) Application.runInBackground = previousRunInBackground;
        }

        static byte[] Serialize(IProtocolToken token)
        {
            var packet = new UdpPacket(new byte[16384], null);
            token.Write(packet);
            return packet.DuplicateData();
        }

        static void ValidateLocalSerialization()
        {
            // No transport, server room, user identity or registration is
            // changed. These private tokens exercise the actual IL2CPP bodies
            // and BinaryFormatter generic constructors before a hardware join.
            var user = new UserToken(0, "", "Quest self-test (DUMMY)", "0", NetworkVersion.Current,
                "", "Steam", true, "0", null);
            byte[] bytes = Serialize(user);
            var received = new UserToken();
            received.Read(new UdpPacket(bytes, null));
            if (received.GameVersion != NetworkVersion.Current || received.Username != user.Username
                || !received.CrossplayEnabled || !Serialize(received).SequenceEqual(bytes))
                throw new InvalidOperationException("Original UserToken cold-path differs.");
            var custom = new CustomDataToken(new byte[] { 71, 86, 82, 49, 0, 255 });
            bytes = Serialize(custom);
            var customReceived = new CustomDataToken();
            customReceived.Read(new UdpPacket(bytes, null));
            if (!customReceived.CustomData.SequenceEqual(custom.CustomData) || !Serialize(customReceived).SequenceEqual(bytes))
                throw new InvalidOperationException("Original CustomDataToken cold-path differs.");
            var game = new GameToken();
            game.CurrentPlatformUsersInSession.Add("Steam");
            game.CurrentPlatformUsersInSession.Add("EpicGamesStore");
            game.CurrentPlatformUsersInSession.Add("GoGGalaxy");
            bytes = Serialize(game);
            var gameReceived = new GameToken();
            gameReceived.Read(new UdpPacket(bytes, null));
            if (!gameReceived.CurrentPlatformUsersInSession.SetEquals(game.CurrentPlatformUsersInSession)
                || gameReceived.DLCFlag != game.DLCFlag || !Serialize(gameReceived).SequenceEqual(bytes))
                throw new InvalidOperationException("Original GameToken/BinaryFormatter cold-path differs.");
            Type dynamic = Type.GetType("Photon.Bolt.BoltDynamicData, PhotonBolt", true);
            if (dynamic.GetMethod("Setup", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public) == null)
                throw new InvalidOperationException("Original reflected BoltDynamicData.Setup was stripped.");
        }

        static void ValidateLocalCodec()
        {
            using (var encoder = new OpusEncoder(SamplingRate.Sampling48000, Channels.Mono, 24000, OpusApplicationType.Voip, Delay.Delay20ms))
            using (var floatDecoder = new OpusDecoder<float>(SamplingRate.Sampling48000, Channels.Mono))
            using (var shortDecoder = new OpusDecoder<short>(SamplingRate.Sampling48000, Channels.Mono))
            {
                encoder.Bitrate = 32000;
                if (encoder.Bitrate != 32000) throw new InvalidOperationException("Original native Opus CTL get/set differs.");
                var floating = new float[960];
                var integer = new short[960];
                for (int i = 0; i < floating.Length; i++) { floating[i] = (float)(0.15 * Math.Sin(i * 2 * Math.PI * 440 / 48000)); integer[i] = (short)(floating[i] * short.MaxValue); }
                for (int i = 0; i < 2; i++)
                {
                    byte[] encodedFloat = encoder.Encode(floating).ToArray(), encodedShort = encoder.Encode(integer).ToArray();
                    if (encodedFloat.Length < 2 || encodedShort.Length < 2) throw new InvalidOperationException("Original Opus encoder produced an empty test packet.");
                    var floatFrame = new FrameBuffer(encodedFloat, (FrameFlags)0);
                    var shortFrame = new FrameBuffer(encodedShort, (FrameFlags)0);
                    float[] decodedFloat = floatDecoder.DecodePacket(ref floatFrame);
                    short[] decodedShort = shortDecoder.DecodePacket(ref shortFrame);
                    floatFrame.Release(); shortFrame.Release();
                    if (i == 1 && (decodedFloat.Length != 960 || decodedShort.Length != 960
                        || !decodedFloat.Any(value => Math.Abs(value) > 0.01) || !decodedShort.Any(value => Math.Abs(value) > 320)))
                        throw new InvalidOperationException("Original Opus decoder returned silent or invalid test PCM.");
                }
            }
        }
    }
}
#endif
