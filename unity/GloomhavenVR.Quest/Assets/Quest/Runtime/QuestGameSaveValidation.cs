#if GHVR_QUEST_STARTUP
using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;

namespace GloomhavenVR.Quest
{
    /// <summary>Bounded Debug proof using original serializers, without touching player saves.</summary>
    internal static class QuestGameSaveValidation
    {
        internal static void VerifyOriginalRootAndOwner()
        {
            try
            {
                var root = new RootSaveData { Version = 1, CurrentLanguage = "English" };
                var ownerInfo = new SerializationInfo(typeof(SaveOwner), new FormatterConverter());
                ownerInfo.AddValue("PlatformPlayerID", "0"); ownerInfo.AddValue("PlatformAccountID", "0");
                ownerInfo.AddValue("PlatformNetworkAccountID", "0"); ownerInfo.AddValue("PlatformName", "Steam");
                ownerInfo.AddValue("Username", "Quest serializer validation (DUMMY)");
                ownerInfo.AddValue("Avatar", null, typeof(SaveOwner.SerializedAvatar)); ownerInfo.AddValue("AvatarSet", false);
                var owner = new SaveOwner(ownerInfo, new StreamingContext());
                RootSaveData restored = RoundTrip(root);
                SaveOwner restoredOwner = RoundTrip(owner);
                if (restored.Version != root.Version || restored.CurrentLanguage != root.CurrentLanguage
                    || restoredOwner.PlatformPlayerID != owner.PlatformPlayerID || restoredOwner.PlatformAccountID != owner.PlatformAccountID
                    || restoredOwner.PlatformNetworkAccountID != owner.PlatformNetworkAccountID || restoredOwner.PlatformName != owner.PlatformName
                    || restoredOwner.Username != owner.Username || restoredOwner.Avatar != null || restoredOwner.AvatarSet)
                    throw new InvalidDataException("Original save serializer roundtrip changed root/owner fields.");
                Debug.Log("[Quest saves] Original BinaryFormatter/SerializationBinding root+owner roundtrip passed; player files unchanged. Full campaign/scenario continuation remains native gameplay evidence.");
            }
            catch (Exception failure)
            {
                Debug.LogError("[Quest saves] Original root/owner serializer roundtrip failed: " + failure);
            }
        }

        private static T RoundTrip<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                new BinaryFormatter().Serialize(stream, value); stream.Position = 0;
                return (T)new BinaryFormatter { Binder = new SerializationBinding() }.Deserialize(stream);
            }
        }
    }
}
#endif
