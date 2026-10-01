// The standalone codec test compiles the production codec with only its two
// additive record identifiers. Protocol identity itself is covered by WireTests.
namespace GloomhavenVR.Net
{
    internal static class NetProtocol
    {
        internal const byte MsgTownGrant = 25;
        internal const byte ExtIdTownWorkspaceCloth = 90;
        internal const byte ExtIdTownInteraction = 91;
        internal const byte ExtIdTownTransaction = 92;
        internal const byte ExtIdTownDonationClock = 93;
    }
}

namespace GloomhavenVR.Net.TownServices
{
    internal static class TownCassetteMotion
    {
        internal const byte RecordId = 86;
        internal const byte RollerRecordId = 87;
    }
}
