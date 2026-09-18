namespace GloomhavenVR.WorldUI;

/// <summary>Distinguish ordinary Guildmaster dialogs from the accepted quest's story.</summary>
internal static class MapStoryCurtainPolicy
{
    internal static bool HidesForMessage(bool campaign, bool hidesOtherUi,
        bool partyCommitted, bool loadoutOpen, bool nativeJourney) =>
        hidesOtherUi && (campaign || partyCommitted || loadoutOpen || nativeJourney);
}
