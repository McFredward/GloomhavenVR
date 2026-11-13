#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ExitGames.Client.Photon;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace GloomhavenVR.Quest
{
    /// <summary>
    /// Concrete roots for the original Protocol16/18 typed-dictionary reader.
    /// Its MakeGenericType/Activator path cannot request missing value-type
    /// native code from an AOT player. No serializer or wire type is replaced.
    /// </summary>
    public static class QuestNetworkAot
    {
        public static void ValidatePayloadCrypto()
        {
            // The selected original client uses PayloadEncryption. Its
            // optional EncryptorNet datagram fallback is unimplemented in the
            // publisher's DLL; do not present that dormant class as a working
            // crypto provider or change the original negotiated mode.
            Type provider = Type.GetType("Photon.SocketServer.Security.DiffieHellmanCryptoProvider, Photon3Unity3D", true);
            object first = Activator.CreateInstance(provider), second = Activator.CreateInstance(provider);
            try
            {
                var publicKey = provider.GetProperty("PublicKey");
                var derive = provider.GetMethod("DeriveSharedKey");
                var encrypt = provider.GetMethod("Encrypt", new[] { typeof(byte[]) });
                var decrypt = provider.GetMethod("Decrypt", new[] { typeof(byte[]) });
                derive.Invoke(first, new[] { publicKey.GetValue(second) });
                derive.Invoke(second, new[] { publicKey.GetValue(first) });
                byte[] payload = Enumerable.Range(0, 64).Select(value => (byte)(value * 7)).ToArray();
                byte[] ciphertext = (byte[])encrypt.Invoke(first, new object[] { payload });
                byte[] received = (byte[])decrypt.Invoke(second, new object[] { ciphertext });
                if (!payload.SequenceEqual(received) || ciphertext.SequenceEqual(payload))
                    throw new InvalidOperationException("Original managed Photon payload crypto cold-path differs.");
                ciphertext = (byte[])encrypt.Invoke(second, new object[] { payload });
                received = (byte[])decrypt.Invoke(first, new object[] { ciphertext });
                if (!payload.SequenceEqual(received))
                    throw new InvalidOperationException("Original managed Photon reverse payload crypto differs.");
            }
            finally { ((IDisposable)first).Dispose(); ((IDisposable)second).Dispose(); }
        }

        public static int ValidatePhotonWire()
        {
            int count = 0;
            foreach (IProtocol protocol in new IProtocol[] { new Protocol16(), new Protocol18() })
            {
                count += Key<byte>(protocol); count += Key<short>(protocol);
                count += Key<int>(protocol); count += Key<long>(protocol);
                if (protocol is Protocol16) count += Key<bool>(protocol);
                count += Key<float>(protocol);
                count += Key<double>(protocol); count += Key<string>(protocol);
                count += Key<object>(protocol);
            }
            return count;
        }

        static int Key<TKey>(IProtocol protocol)
        {
            Case<TKey, byte>(protocol); Case<TKey, short>(protocol);
            Case<TKey, int>(protocol); Case<TKey, long>(protocol);
            Case<TKey, bool>(protocol); Case<TKey, float>(protocol);
            Case<TKey, double>(protocol); Case<TKey, string>(protocol);
            Case<TKey, object>(protocol); Case<TKey, byte[]>(protocol);
            Case<TKey, Hashtable>(protocol);
            // V16 explicitly rejects typed Array/Dictionary value headers.
            // Keep that genuine protocol limitation; only V18 accepts these.
            if (protocol is Protocol16) return 11;
            Case<TKey, int[]>(protocol); Case<TKey, string[]>(protocol);
            Case<TKey, object[]>(protocol);
            Case<TKey, float[]>(protocol); Case<TKey, double[]>(protocol);
            Case<TKey, long[]>(protocol); Case<TKey, short[]>(protocol);
            Case<TKey, bool[]>(protocol);
            return 19;
        }

        static void Case<TKey, TValue>(IProtocol protocol)
        {
            var dictionary = new Dictionary<TKey, TValue>();
            TKey key = (TKey)Sample(typeof(TKey));
            dictionary[key] = (TValue)Sample(typeof(TValue));
            byte[] bytes = protocol.Serialize(dictionary);
            IDictionary received = protocol.Deserialize(bytes) as IDictionary;
            if (received == null || received.Count != 1 || !received.Contains(key)
                || !protocol.Serialize(received).SequenceEqual(bytes))
                throw new InvalidOperationException("Original Photon " + protocol.ProtocolType
                    + " typed dictionary cold-path differs: " + typeof(TKey).Name + "/" + typeof(TValue).Name);
            // Interface dispatch is part of the original reflective reader's
            // path. Root the concrete constructors and value-type dictionary
            // indexer bodies rather than retaining open generic metadata only.
            IDictionary writer = dictionary;
            writer[key] = writer[key];
        }

        static object Sample(Type type)
        {
            if (type == typeof(byte)) return (byte)7;
            if (type == typeof(short)) return (short)-23;
            if (type == typeof(int)) return -1048577;
            if (type == typeof(long)) return 4294967297L;
            if (type == typeof(bool)) return true;
            if (type == typeof(float)) return 0.25f;
            if (type == typeof(double)) return 0.125;
            if (type == typeof(string) || type == typeof(object)) return "Quest wire self-test";
            if (type == typeof(byte[])) return new byte[] { 0, 255, 1 };
            if (type == typeof(short[])) return new short[] { -23, 0, 7 };
            if (type == typeof(int[])) return new[] { -1048577, 0, 7 };
            if (type == typeof(long[])) return new[] { 4294967297L, -1L };
            if (type == typeof(bool[])) return new[] { true, false };
            if (type == typeof(float[])) return new[] { 0.25f, -0.125f };
            if (type == typeof(double[])) return new[] { 0.125, -0.0625 };
            if (type == typeof(string[])) return new[] { "A", "Ü" };
            if (type == typeof(object[])) return new object[] { "A", 7, true, new byte[] { 255 } };
            if (type == typeof(Hashtable)) return new Hashtable { ["Room"] = "local-test", [1] = true };
            throw new InvalidOperationException("Original Photon AOT root has no concrete sample: " + type.FullName);
        }
    }
}
#endif
