using System;

namespace QuestWeaver.Runtime
{
    /// <summary>One offline, APK-signed profile snapshot. No credentials or provider services.</summary>
    public static class OfflineProfile
    {
        private static string? name, player, account, platform;
        private static int? dlcMask;
        private static bool failed;

        public static void Initialize(string displayName, string playerId, string accountId, string platformId, int ownedDlcMask = 0)
        {
            if (name != null || failed) throw new InvalidOperationException("Quest offline profile was already initialized.");
            if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrEmpty(playerId)
                || string.IsNullOrEmpty(accountId) || string.IsNullOrEmpty(platformId) || ownedDlcMask < 0 || (ownedDlcMask & ~7) != 0)
                throw new ArgumentException("Quest offline profile is incomplete.");
            name = displayName; player = playerId; account = accountId; platform = platformId; dlcMask = ownedDlcMask;
        }
        public static void Reject() { failed = true; }
        private static string Select(string? current, string fallback)
        {
            if (failed) throw new InvalidOperationException("The signed Quest offline profile could not be initialized.");
            return current ?? fallback;
        }
        public static string DisplayName(string fallback) => Select(name, fallback);
        public static string PlayerId(string fallback) => Select(player, fallback);
        public static string AccountId(string fallback) => Select(account, fallback);
        public static string PlatformId(string fallback) => Select(platform, fallback);
        public static int OwnedDlcMask(int fallback)
        {
            if (failed) throw new InvalidOperationException("The signed Quest offline profile could not be initialized.");
            return dlcMask ?? fallback;
        }
    }
}
