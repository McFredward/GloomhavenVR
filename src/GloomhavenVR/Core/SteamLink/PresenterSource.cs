namespace GloomhavenVR.Core.SteamLink
{
    // Synthetic format recognition only. This deliberately changes the source
    // profile: the original registration-v1 generator and CRC headers remain
    // untouched. A copied/frozen valid guard cannot authenticate body or liveness.
    internal static class PresenterSource
    {
        public const string Profile = "dense-asymmetric-registration-presenter-v2";
        public const int ProfileRevision = 2;
        public const int GuardCells = 12, GuardSize = 32, GuardX = 16, GuardY = 464, GuardStride = 52;
        public const int CodeTolerance = 2;
        public const string SignaturePreimage = "GVR-SP1;profile=dense-asymmetric-registration-presenter-v2;revision=2;canvas=688x548;color=16,244,320,192;coverage=352,244,320,192;rgb=straight;coverage=linear;component=native-G;guard=12,32,16,464,52;tol=2/255";
        public const string SignatureSha256 = "6f999b336f525cdb66e379bd3038c50fc9d1237f534a66323ab0215b503ed586";
        private static readonly byte[] Codes = { 111, 153, 155, 51, 111, 82, 92, 219, 102, 227, 121, 189 };

        // Cold/Update preparation only, before either immutable eye texture is
        // published. Never stamp into a texture or a borrowed render callback.
        public static RegistrationSourceRecord Stamp(RegistrationPattern pattern,
            RegistrationPairContext context, RegistrationSourceBuffers target)
        {
            RegistrationSourceRecord record = RegistrationSource.Stamp(pattern, context, target);
            for (int cell = 0; cell < GuardCells; cell++)
                for (int y = GuardY; y < GuardY + GuardSize; y++)
                    for (int x = GuardX + cell * GuardStride; x < GuardX + cell * GuardStride + GuardSize; x++)
                    {
                        int at = (y * RegistrationSource.CanvasWidth + x) * 3;
                        target.Canvas[at] = target.Canvas[at + 1] = target.Canvas[at + 2] = Codes[cell];
                    }
            record.Profile = Profile;
            record.ProfileRevision = ProfileRevision;
            record.Manifest.CanvasSha256 = ProbeManifest.Sha256(target.Canvas);
            return record;
        }
    }
}
