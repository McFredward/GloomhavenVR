namespace GloomhavenVR.Core.SteamLink
{
    // Frozen with tests/steamlink-scene-payload/profile.json. Guard recognition
    // does not authenticate the scene, coverage provenance or frame freshness.
    internal static class SceneAtlasProfile
    {
        public const string Profile = "scene-contribution-atlas-v1";
        public const int Revision = 1;
        public const string JsonSha256 = "04b47a78192aa883c264b1e04b88d0e17e7d373ab8ed1f1f6b15ee1a20601904";
        public const string SignatureSha256 = "6fd08068e3fbffc166a80f5a1044d4c8135b37087441aac85f6223c121fccc5e";
        public const string ShaderName = "GloomhavenVR/SteamLinkSceneAtlas";
        public const int Width = 2112, Height = 2752;
        public const int SceneX = 32, SceneY = 352, SceneSize = 2048;
        public const int CoverageSize = 1024, TileRows = 256, Halo = 16;
        public const int TileWidth = 1056, TileHeight = 288, TopTileY = 32, BottomTileY = 2432;
        public const int GuardX = 64, GuardY = 2400, GuardSize = 32, GuardStride = 160, GuardCount = 12;
    }
}
