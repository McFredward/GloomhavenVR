using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace GloomhavenVR.Core.SteamLink
{
    // Fixed reusable CPU destinations. Their mutable bytes belong to the caller;
    // they must never be restamped while an upload/GPU consumer still uses them.
    internal sealed class RegistrationSourceBuffers
    {
        public readonly byte[] Color = new byte[RegistrationSource.SourceWidth * RegistrationSource.SourceHeight * 3];
        public readonly byte[] Coverage = new byte[RegistrationSource.SourceWidth * RegistrationSource.SourceHeight];
        public readonly byte[] Canvas = new byte[RegistrationSource.CanvasWidth * RegistrationSource.CanvasHeight * 3];
        public readonly byte[] Header = new byte[ProbeManifest.HeaderBytes];
    }

    // Explicitly reusable CPU preparation workspace, not a GPU resource. Refill
    // only after Stamp has copied its bytes; do not retain old tuple cache entries
    // pointing at a workspace being refilled. No global cache/history is owned.
    internal sealed class RegistrationPattern
    {
        public string Session { get; private set; } = string.Empty;
        public ProbeEye Eye { get; private set; } = ProbeEye.Unknown;
        public int Phase { get; private set; }
        public string ColorSha256 { get; private set; } = string.Empty;
        public string CoverageSha256 { get; private set; } = string.Empty;
        private readonly byte[] _color = new byte[RegistrationSource.SourceWidth * RegistrationSource.SourceHeight * 3];
        private readonly byte[] _coverage = new byte[RegistrationSource.SourceWidth * RegistrationSource.SourceHeight];
        private readonly byte[] _canvas = new byte[RegistrationSource.CanvasWidth * RegistrationSource.CanvasHeight * 3];

        internal void Refill(string session, ProbeEye eye, int phase)
        {
            bool reset = Session != session || Eye != eye;
            Session = string.Empty; Eye = ProbeEye.Unknown;
            RegistrationSource.FillPatternArrays(session, eye, phase, _color, _coverage, _canvas, reset);
            ColorSha256 = ProbeManifest.Sha256(_color);
            CoverageSha256 = ProbeManifest.Sha256(_coverage);
            Session = session; Eye = eye; Phase = phase;
        }

        internal void CopyTo(RegistrationSourceBuffers target)
        {
            Array.Copy(_color, target.Color, _color.Length);
            Array.Copy(_coverage, target.Coverage, _coverage.Length);
            Array.Copy(_canvas, target.Canvas, _canvas.Length);
        }
    }

    [DataContract]
    internal sealed class RegistrationSourceRecord
    {
        [DataMember(Name = "schema")] public string Schema = "frame-app-registration-source-v1";
        [DataMember(Name = "profile")] public string Profile = RegistrationSource.Profile;
        [DataMember(Name = "profile_revision")] public int ProfileRevision = 1;
        [DataMember(Name = "session")] public string Session = string.Empty;
        [DataMember(Name = "pair_id")] public ulong PairId;
        [DataMember(Name = "eye")] public string Eye = string.Empty;
        [DataMember(Name = "phase")] public int Phase;
        [DataMember(Name = "engine_frame_cohort")] public long EngineFrameCohort;
        [DataMember(Name = "content_id_meaning")] public string ContentIdMeaning = "app_owned_pair_id_not_native_video_frame";
        [DataMember(Name = "color_numeric_encoding")] public string ColorEncoding = "uninterpreted_rgb8_codes";
        [DataMember(Name = "coverage_numeric_encoding")] public string CoverageEncoding = "linear_coverage_r8";
        [DataMember(Name = "manifest")] public ProbeManifest Manifest = new ProbeManifest();
        [DataMember(Name = "native_frame_identity")] public string? NativeFrameIdentity = null;
        [DataMember(Name = "native_spatial_mapping")] public string? NativeSpatialMapping = null;
        [DataMember(Name = "physical_eye_proven")] public bool PhysicalEyeProven = false;
        [DataMember(Name = "source_frame_association_proven")] public bool SourceFrameAssociationProven = false;
        [DataMember(Name = "gpu_completion_proven")] public bool GpuCompletionProven = false;
        [DataMember(Name = "opaque_fallback_required")] public bool OpaqueFallbackRequired = true;
        [DataMember(Name = "passthrough_authorized")] public bool PassthroughAuthorized = false;

        public byte[] ToJsonBytes()
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(RegistrationSourceRecord)).WriteObject(stream, this);
                return stream.ToArray();
            }
        }
    }

    // Pure CPU synthetic source only. PreparePattern and Stamp both belong in a
    // cold/Update preparation lane, NEVER an eye/compositor rendering callback.
    // No texture, file, clock, Unity or sender state is accessed here.
    internal static class RegistrationSource
    {
        public const string Profile = "dense-asymmetric-registration-v1";
        public const int SourceWidth = 320, SourceHeight = 192, CanvasWidth = 688, CanvasHeight = 548;
        public const int PhaseCount = 128;
        private const int PlaneY = 244;
        private static readonly byte[] Levels = { 0, 64, 128, 191, 255, 192 };

        public static RegistrationSourceBuffers CreateBuffers() => new RegistrationSourceBuffers();

        public static int PhaseForPair(ulong pairId)
        {
            if (pairId == 0) throw new ArgumentException("Pair ID zero is not a reservation.", nameof(pairId));
            return (int)((pairId - 1) % PhaseCount);
        }

        // Offline expectations can be reconstructed from this explicit tuple.
        // Constructing a context is not an output, callback or GPU ownership proof.
        public static RegistrationPairContext ExpectedContext(string session, ulong pairId, long engineFrameCohort = 0) =>
            new RegistrationPairContext(session, pairId, engineFrameCohort);

        public static RegistrationPattern PreparePattern(string session, ProbeEye eye, int phase, RegistrationPattern? reusableWorkspace = null)
        {
            if (!IsSession(session) || !KnownEye(eye) || phase < 0 || phase >= PhaseCount)
                throw new ArgumentException("Unsupported registration pattern tuple.");
            RegistrationPattern pattern = reusableWorkspace ?? new RegistrationPattern();
            pattern.Refill(session, eye, phase);
            return pattern;
        }

        internal static void FillPatternArrays(string session, ProbeEye eye, int phase,
                                                byte[] color, byte[] coverage, byte[] canvas, bool resetBackground)
        {
            uint seed = Seed(session, eye);
            DensePattern(color, SourceWidth, SourceHeight, seed, 16);
            if (resetBackground) DensePattern(canvas, CanvasWidth, CanvasHeight, seed ^ 0x51f026a9u, 24);
            int dx = Triangle(phase), dy = Triangle((phase + 29) % PhaseCount) / 2;
            for (int y = 0; y < SourceHeight; y++)
                for (int x = 0; x < SourceWidth; x++)
                {
                    int cell = (x / 16) + (y / 16) * 20;
                    int code = (int)((cell * 197u + seed) & 255u);
                    byte value = Levels[(code + (x % 16) / 4 + 2 * ((y % 16) / 4)) % 5];
                    int distance = Math.Abs(x - SourceWidth / 2 - dx) + 2 * Math.Abs(y - SourceHeight / 2 - dy);
                    // A moving irregular overlap patch and thin edge change only
                    // a small local area per neighboring pair; no whole-field flash.
                    if (distance < 51 + ((x / 11 + y / 7) % 3) * 4) value = 192;
                    if (x == (y * 3 + phase + (eye == ProbeEye.Right ? 19 : 0)) % SourceWidth) value = 191;
                    if (y >= SourceHeight - 24)
                    {
                        value = Levels[Math.Min(5, x * 6 / SourceWidth)];
                        // Exactly the same red and green occur at every alpha.
                        Pixel(color, SourceWidth, x, y, y < SourceHeight - 12 ? 208 : 32,
                              y < SourceHeight - 12 ? 32 : 208, 48);
                    }
                    coverage[y * SourceWidth + x] = value;
                }
            // Asymmetric moving L marks distinguish rotations/reflections and eyes.
            Mark(color, SourceWidth, SourceHeight, 78 + dx, 54 + dy, eye == ProbeEye.Left ? 240 : 64, 48, 224);
            Mark(color, SourceWidth, SourceHeight, 248 - dx / 2, 128 - dy, 40, eye == ProbeEye.Left ? 224 : 96, 56);
            for (int y = 0; y < SourceHeight; y++)
            {
                Array.Copy(color, y * SourceWidth * 3, canvas, ((PlaneY + y) * CanvasWidth + 16) * 3, SourceWidth * 3);
                for (int x = 0; x < SourceWidth; x++)
                {
                    byte value = coverage[y * SourceWidth + x];
                    Pixel(canvas, CanvasWidth, 352 + x, PlaneY + y, value, value, value);
                }
            }
            Mark(canvas, CanvasWidth, CanvasHeight, 4, 448, 240, 48, 72);
            Mark(canvas, CanvasWidth, CanvasHeight, 644, 502, 48, 208, eye == ProbeEye.Left ? 64 : 224);

        }

        public static RegistrationSourceRecord Stamp(RegistrationPattern pattern, RegistrationPairContext context,
                                                       RegistrationSourceBuffers target)
        {
            if (pattern == null || context == null || target == null || pattern.Session != context.Session
                || pattern.Phase != context.Phase || !KnownEye(pattern.Eye))
                throw new ArgumentException("Pattern does not belong to the frozen registration pair.");
            pattern.CopyTo(target);
            string eye = pattern.Eye == ProbeEye.Left ? "left" : "right";
            var manifest = new ProbeManifest
            {
                Schema = "frame-app-payload-reference-v1", Session = context.Session, ContentId = context.PairId,
                Eye = eye, Convention = "straight", SourceExtent = new[] { SourceWidth, SourceHeight }, LayoutRevision = 1,
                Placements = new ProbePlacement { Canvas = new[] { CanvasWidth, CanvasHeight },
                    Color = new[] { 16, PlaneY, SourceWidth, SourceHeight }, Coverage = new[] { 352, PlaneY, SourceWidth, SourceHeight } },
                HeaderCopies = 3, ColorSha256 = pattern.ColorSha256, CoverageSha256 = pattern.CoverageSha256,
                OpaqueFallbackRequired = true, PassthroughAuthorized = false
            };
            Header(manifest, target.Header);
            PaintHeaders(target.Canvas, target.Header);
            manifest.CanvasSha256 = ProbeManifest.Sha256(target.Canvas);
            manifest.Check(eye);
            return new RegistrationSourceRecord { Session = context.Session, PairId = context.PairId, Eye = eye,
                Phase = context.Phase, EngineFrameCohort = context.EngineFrameCohort, Manifest = manifest };
        }

        public static bool IsSession(string session)
        {
            return session != null && session.Length == 32 && Guid.TryParseExact(session, "N", out Guid value)
                   && value != Guid.Empty && value.ToString("N") == session;
        }

        private static bool KnownEye(ProbeEye eye) => eye == ProbeEye.Left || eye == ProbeEye.Right;
        private static int Triangle(int phase) => phase < 32 ? phase : phase < 96 ? 64 - phase : phase - 128;

        private static uint Seed(string session, ProbeEye eye)
        {
            uint hash = 2166136261u;
            unchecked
            {
                foreach (char value in session) hash = (hash ^ value) * 16777619u;
                return (hash ^ (uint)eye) * 16777619u;
            }
        }

        private static void DensePattern(byte[] pixels, int width, int height, uint seed, int cellSize)
        {
            int columns = (width + cellSize - 1) / cellSize, bits = cellSize == 16 ? 8 : 10;
            uint mask = (1u << bits) - 1;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int cell = x / cellSize + (y / cellSize) * columns;
                    uint id = unchecked(((uint)cell * (bits == 8 ? 197u : 405u) + seed) & mask);
                    int localX = x % cellSize, localY = y % cellSize;
                    if (localY < 2 && localX < bits * 2)
                    {
                        bool on = ((id >> (localX / 2)) & 1u) != 0;
                        int value = on == (localX % 2 == 0) ? 216 : 40;
                        Pixel(pixels, width, x, y, value, value, value);
                    }
                    else Pixel(pixels, width, x, y,
                        32 + (int)(((uint)(x * 173 / width) + id * 11u) % 192u),
                        32 + (int)(((uint)(y * 157 / height) + (id >> 3) * 13u) % 192u),
                        32 + (int)(((uint)(localX * 7 + localY * 13) + seed) % 192u));
                }
        }

        private static void Mark(byte[] pixels, int width, int height, int x, int y, int r, int g, int b)
        {
            for (int dy = 0; dy < 13; dy++)
                for (int dx = 0; dx < 11; dx++)
                    if ((dx < 3 || dy > 9) && x + dx >= 0 && x + dx < width && y + dy >= 0 && y + dy < height)
                        Pixel(pixels, width, x + dx, y + dy, r, g, b);
        }

        private static void Pixel(byte[] bytes, int width, int x, int y, int r, int g, int b)
        {
            int at = (y * width + x) * 3;
            bytes[at] = (byte)r; bytes[at + 1] = (byte)g; bytes[at + 2] = (byte)b;
        }

        private static void Header(ProbeManifest manifest, byte[] header)
        {
            Array.Clear(header, 0, header.Length);
            Array.Copy(Encoding.ASCII.GetBytes("FAPPROB1"), header, 8);
            header[9] = header[11] = 1;
            HexTo(manifest.Session, header, 12);
            for (int i = 0; i < 8; i++) header[28 + i] = (byte)(manifest.ContentId >> (56 - i * 8));
            header[36] = (byte)(manifest.Eye == "right" ? 1 : 0);
            int[] coordinates = { SourceWidth, SourceHeight, CanvasWidth, CanvasHeight, 16, PlaneY,
                SourceWidth, SourceHeight, 352, PlaneY, SourceWidth, SourceHeight };
            for (int i = 0; i < coordinates.Length; i++)
            {
                header[40 + i * 2] = (byte)(coordinates[i] >> 8);
                header[41 + i * 2] = (byte)coordinates[i];
            }
            HexTo(manifest.ColorSha256, header, 64); HexTo(manifest.CoverageSha256, header, 96);
            uint crc = 0xffffffffu;
            for (int index = 0; index < 128; index++)
            {
                crc ^= header[index];
                for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u);
            }
            crc ^= 0xffffffffu;
            for (int i = 0; i < 4; i++) header[128 + i] = (byte)(crc >> (24 - i * 8));
        }

        private static void HexTo(string value, byte[] target, int offset)
        {
            for (int i = 0; i < value.Length / 2; i++)
                target[offset + i] = Convert.ToByte(value.Substring(i * 2, 2), 16);
        }

        private static void PaintHeaders(byte[] canvas, byte[] header)
        {
            for (int copy = 0; copy < 3; copy++)
                for (int bit = 0; bit < ProbeManifest.HeaderBytes * 8; bit++)
                {
                    bool on = ((header[bit / 8] >> (7 - bit % 8)) & 1) != 0;
                    int x = 16 + (bit % 64) * 8, y = 16 + copy * 76 + (bit / 64) * 4;
                    for (int side = 0; side < 2; side++)
                        for (int dy = 0; dy < 4; dy++)
                            for (int dx = 0; dx < 4; dx++)
                            {
                                int value = on == (side == 0) ? 255 : 0;
                                Pixel(canvas, CanvasWidth, x + side * 4 + dx, y + dy, value, value, value);
                            }
                }
        }
    }
}
