#if GHVR_QUEST_GAME
using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace GloomhavenVR.Quest
{
    /// <summary>Loads a small signed APK asset before original scene Awake callbacks.</summary>
    public static class QuestOfflineProfile
    {
        public const string ApkAsset = "assets/Quest/offline-profile.json";
        [Serializable] sealed class Profile
        {
            public int schema;
            public string provider, providerId, steamId, displayName;
            public uint accountId;
            public bool isDummy;
            public Ownership dlcOwnership;
        }
        [Serializable] sealed class Ownership { public int schema, appId, ownedMask; public string provider, providerId, steamId; public int[] installedAppIds; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize()
        {
            try
            {
                string json = null;
                // Application.dataPath is the installed APK on Android. Reading
                // this bounded signed member avoids services, external overrides
                // and blocking network requests on Unity's main thread.
                if (Application.platform == RuntimePlatform.Android)
                {
                    using (var apk = ZipFile.OpenRead(Application.dataPath))
                    {
                        ZipArchiveEntry found = null;
                        foreach (var entry in apk.Entries) if (entry.FullName == ApkAsset)
                        { if (found != null) throw new InvalidDataException("Duplicate signed Quest profile."); found = entry; }
                        if (found != null)
                        {
                            if (found.Length <= 0 || found.Length > 4096) throw new InvalidDataException("Signed Quest profile is excessive.");
                            using (var source = found.Open()) using (var reader = new StreamReader(source, new UTF8Encoding(false, true)))
                                json = reader.ReadToEnd();
                        }
                    }
                }
                if (json == null)
                {
                    var resource = Resources.Load<TextAsset>("quest-profile");
                    if (resource == null) throw new InvalidDataException("Signed Quest profile is missing.");
                    json = resource.text;
                }
                var p = JsonUtility.FromJson<Profile>(json);
                Validate(p);
                string id = string.IsNullOrEmpty(p.providerId) ? p.steamId : p.providerId;
                QuestWeaver.Runtime.OfflineProfile.Initialize(p.displayName, id,
                    p.accountId.ToString(CultureInfo.InvariantCulture),
                    p.provider == "gog" ? "GoGGalaxy" : p.provider == "epic" ? "EpicGamesStore" : "Steam", p.dlcOwnership == null ? 0 : p.dlcOwnership.ownedMask);
                Debug.Log("[Quest platform] signed offline profile initialized; provider=" + p.provider + " dummy=" + p.isDummy);
            }
            catch (Exception error)
            {
                QuestWeaver.Runtime.OfflineProfile.Reject();
                Debug.LogError("[Quest platform] signed offline profile rejected: " + error);
                throw;
            }
        }

        static void Validate(Profile p)
        {
            if (p == null || p.schema != 1 || string.IsNullOrWhiteSpace(p.displayName) || Encoding.UTF8.GetByteCount(p.displayName) > 256
                || Array.Exists(p.displayName.ToCharArray(), c => c < 32 || c == 127))
                throw new InvalidDataException("Quest profile identity is invalid.");
            string id = string.IsNullOrEmpty(p.providerId) ? p.steamId : p.providerId;
            ulong parsed;
            bool valid;
            if (p.provider == "steam")
                valid = id == p.steamId && ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out parsed)
                    && (p.isDummy ? parsed == 0 && p.accountId == 0 && p.displayName.IndexOf("DUMMY", StringComparison.OrdinalIgnoreCase) >= 0
                        : id.Length == 17 && parsed > 76561197960265728UL && parsed - 76561197960265728UL <= uint.MaxValue
                            && parsed - 76561197960265728UL == p.accountId);
            else
            {
                uint expected;
                using (var sha = SHA256.Create())
                {
                    byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(p.provider + ":" + id));
                    expected = (uint)bytes[0] << 24 | (uint)bytes[1] << 16 | (uint)bytes[2] << 8 | bytes[3];
                }
                if (expected == 0) expected = 1;
                valid = !p.isDummy && p.steamId == "0" && p.accountId == expected
                    && (p.provider == "gog" && ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) && parsed > 0
                        || p.provider == "epic" && id != null && id.Length == 32 && Array.TrueForAll(id.ToCharArray(), c => "0123456789abcdef".IndexOf(c) >= 0));
            }
            if (!valid) throw new InvalidDataException("Quest profile provider/account mismatch.");
            if (p.dlcOwnership != null)
            {
                var o = p.dlcOwnership;
                string owner = string.IsNullOrEmpty(o.providerId) ? o.steamId : o.providerId;
                if (o.schema != 1 || o.appId != 780290 || o.provider != p.provider || owner != id || o.installedAppIds == null)
                    throw new InvalidDataException("Quest DLC ownership differs from the offline profile.");
                int mask = 0;
                foreach (int app in o.installedAppIds)
                {
                    int flag = app == 1809490 ? 1 : app == 1958560 ? 2 : app == 2584170 ? 4 : 0;
                    if (flag == 0 || (mask & flag) != 0) throw new InvalidDataException("Quest DLC ownership is invalid or duplicated.");
                    mask |= flag;
                }
                if (mask != o.ownedMask) throw new InvalidDataException("Quest DLC ownership flags are inconsistent.");
            }
        }
    }
}
#endif
