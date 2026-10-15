using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace GloomhavenVR.Core.SteamLink
{
    [DataContract]
    internal sealed class ProbePlacement
    {
        [DataMember(Name = "canvas")] public int[] Canvas = Array.Empty<int>();
        [DataMember(Name = "color")] public int[] Color = Array.Empty<int>();
        [DataMember(Name = "coverage")] public int[] Coverage = Array.Empty<int>();
    }

    [DataContract]
    internal sealed class ProbeManifest
    {
        public const int HeaderBytes = 132;
        public const int PlaneY = 244;
        public const int MaximumSource = 512;
        public const int MaximumPngBytes = 8 * 1024 * 1024;
        [DataMember(Name = "schema")] public string Schema = string.Empty;
        [DataMember(Name = "session")] public string Session = string.Empty;
        [DataMember(Name = "content_id")] public ulong ContentId = 0;
        [DataMember(Name = "eye")] public string Eye = string.Empty;
        [DataMember(Name = "rgb_convention")] public string Convention = string.Empty;
        [DataMember(Name = "source_extent")] public int[] SourceExtent = Array.Empty<int>();
        [DataMember(Name = "layout_revision")] public int LayoutRevision = 0;
        [DataMember(Name = "placements")] public ProbePlacement Placements = new ProbePlacement();
        [DataMember(Name = "header_copies")] public int HeaderCopies = 0;
        [DataMember(Name = "color_sha256")] public string ColorSha256 = string.Empty;
        [DataMember(Name = "coverage_sha256")] public string CoverageSha256 = string.Empty;
        [DataMember(Name = "canvas_rgb_sha256")] public string CanvasSha256 = string.Empty;
        [DataMember(Name = "opaque_fallback_required")] public bool OpaqueFallbackRequired = false;
        [DataMember(Name = "passthrough_authorized")] public bool PassthroughAuthorized = false;

        public static ProbeManifest Load(byte[] bytes, string expectedEye)
        {
            if (bytes.Length < 1 || bytes.Length > 8192)
                throw new InvalidDataException("Embedded source record exceeds its fixed limit.");
            ProbeManifest result;
            using (var stream = new MemoryStream(bytes, false))
                result = (ProbeManifest?)new DataContractJsonSerializer(typeof(ProbeManifest)).ReadObject(stream)
                    ?? throw new InvalidDataException("Missing embedded source manifest.");
            if (result == null)
                throw new InvalidDataException("Missing source manifest.");
            result.Check(expectedEye);
            return result;
        }

        public void Check(string expectedEye)
        {
            if (Schema != "frame-app-payload-reference-v1" || Eye != expectedEye
                || (Eye != "left" && Eye != "right") || Convention != "straight"
                || LayoutRevision != 1 || HeaderCopies != 3 || !OpaqueFallbackRequired || PassthroughAuthorized
                || !Hex(Session, 32) || Session == new string('0', 32)
                || !Hex(ColorSha256, 64) || !Hex(CoverageSha256, 64) || !Hex(CanvasSha256, 64)
                || SourceExtent == null || SourceExtent.Length != 2
                || SourceExtent[0] < 32 || SourceExtent[0] > MaximumSource
                || SourceExtent[1] < 32 || SourceExtent[1] > MaximumSource || Placements == null)
                throw new InvalidDataException("Unsupported or inconsistent source manifest.");
            int width = SourceExtent[0], height = SourceExtent[1];
            if (!Equal(Placements.Canvas, Math.Max(width * 2 + 48, 544), PlaneY + height + 112)
                || !Equal(Placements.Color, 16, PlaneY, width, height)
                || !Equal(Placements.Coverage, 32 + width, PlaneY, width, height))
                throw new InvalidDataException("Source layout is not the known immutable revision.");
        }

        public static void CheckPair(ProbeManifest left, ProbeManifest right)
        {
            left.Check("left");
            right.Check("right");
            if (left.Session != right.Session || left.ContentId != right.ContentId
                || !Equal(left.SourceExtent, right.SourceExtent) || left.Convention != right.Convention)
                throw new InvalidDataException("Both eyes must belong to the same immutable source generation.");
        }

        public static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var text = new StringBuilder(64);
                foreach (byte value in sha.ComputeHash(bytes))
                    text.Append(value.ToString("x2"));
                return text.ToString();
            }
        }

        public static bool Equal(int[] values, params int[] expected)
        {
            if (values == null || values.Length != expected.Length)
                return false;
            for (int index = 0; index < values.Length; index++)
                if (values[index] != expected[index])
                    return false;
            return true;
        }

        private static bool Hex(string text, int length)
        {
            if (text == null || text.Length != length)
                return false;
            foreach (char value in text)
                if (!((value >= '0' && value <= '9') || (value >= 'a' && value <= 'f')))
                    return false;
            return true;
        }
    }
}
