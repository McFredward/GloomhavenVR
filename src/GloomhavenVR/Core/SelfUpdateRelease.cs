using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GloomhavenVR.Core;

/// <summary>
/// Everything the mod needs to know about the newest published release, plus the two pieces of
/// PURE STRING WORK that produce it: a small JSON reader and a semantic-version comparison.
///
/// <para>WHY A HAND-WRITTEN JSON READER. The requirement is that a malformed or unexpected answer
/// is SILENT — no window, no stall, no warning that cries wolf. <c>JsonUtility</c> throws on
/// malformed input and needs serializable mirror types for every nested shape; a reader that
/// simply returns null and names the term that failed is a better fit for "silence is the correct
/// behaviour". It is deliberately small: objects, arrays, strings, numbers, the three literals,
/// a depth cap and an input cap, no reviver, no dates, no numbers wider than long/double.</para>
///
/// <para>WHY THE COMPARISON IS NOT <c>string.CompareOrdinal</c>. "0.10.0" is newer than "0.9.0"
/// and ordinally smaller. And a pre-release ("0.3.0-rc1") is OLDER than the release it leads to,
/// which is the case that decides whether the mod offers to "update" a release build back to a
/// release candidate.</para>
/// </summary>
internal sealed class SelfUpdateRelease
{
    /// <summary>The release tag exactly as GitHub published it, e.g. "v0.2.0".</summary>
    internal string Tag { get; private set; } = string.Empty;

    /// <summary>The tag with a leading "v" stripped — what gets compared against BuildInfo.Version.</summary>
    internal string Version { get; private set; } = string.Empty;

    /// <summary>File name of the chosen .zip asset.</summary>
    internal string AssetName { get; private set; } = string.Empty;

    /// <summary>Download URL of the chosen .zip asset.</summary>
    internal string AssetUrl { get; private set; } = string.Empty;

    /// <summary>The size GitHub PUBLISHES for that asset, in bytes. The download is checked against it.</summary>
    internal long AssetSize { get; private set; }

    /// <summary>Hosts a release asset URL is allowed to point at, matched WHOLE. Anything else is
    /// treated as malformed: the mod downloads and installs what this URL returns, so it does not
    /// follow one off GitHub just because an answer said so.</summary>
    private static readonly string[] AllowedHosts =
    {
        "github.com",
        "www.github.com",
        "api.github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
    };

    /// <summary>Dotted suffixes accepted in addition to <see cref="AllowedHosts"/>. The leading dot
    /// is load-bearing: without it "evilgithub.com" would end with "github.com" and pass.</summary>
    private static readonly string[] AllowedHostSuffixes =
    {
        ".github.com",
        ".githubusercontent.com",
    };

    /// <summary>
    /// Read the <c>/releases/latest</c> answer. Returns false with <paramref name="failedTerm"/>
    /// naming WHICH part was missing or wrong — never throws, never partially fills the result.
    /// </summary>
    internal static bool TryParse(string json, out SelfUpdateRelease release, out string failedTerm)
    {
        release = new SelfUpdateRelease();
        failedTerm = string.Empty;

        object? root = SelfUpdateJson.Parse(json);
        if (root is not Dictionary<string, object?> obj)
        {
            failedTerm = "body is not a JSON object";
            return false;
        }

        if (obj.TryGetValue("draft", out object? draft) && draft is bool isDraft && isDraft)
        {
            failedTerm = "release is a draft";
            return false;
        }

        if (obj.TryGetValue("tag_name", out object? tagValue) && tagValue is string tag && tag.Length > 0)
        {
            release.Tag = tag;
            release.Version = StripTagPrefix(tag);
        }
        else
        {
            failedTerm = "tag_name missing";
            return false;
        }

        if (!TryParseVersion(release.Version, out _, out _, out _, out _))
        {
            failedTerm = $"tag_name '{release.Tag}' is not a version";
            return false;
        }

        if (!obj.TryGetValue("assets", out object? assetsValue) || assetsValue is not List<object?> assets)
        {
            failedTerm = "assets missing";
            return false;
        }

        foreach (object? entry in assets)
        {
            if (entry is not Dictionary<string, object?> asset)
                continue;
            if (!asset.TryGetValue("name", out object? nameValue) || nameValue is not string name)
                continue;
            if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!asset.TryGetValue("browser_download_url", out object? urlValue) || urlValue is not string url)
                continue;
            if (!IsAllowedAssetUrl(url))
                continue;

            long size = 0;
            if (asset.TryGetValue("size", out object? sizeValue) && sizeValue is double sizeNumber
                && sizeNumber > 0d && sizeNumber < 4294967296d)
            {
                size = (long)sizeNumber;
            }

            if (size <= 0)
                continue;

            release.AssetName = name;
            release.AssetUrl = url;
            release.AssetSize = size;
            break;
        }

        if (release.AssetUrl.Length == 0)
        {
            failedTerm = "no .zip asset with a published size on an allowed host";
            return false;
        }

        return true;
    }

    /// <summary>Scheme + host allow-list for the asset URL. Deliberately strict and deliberately
    /// not a Uri parse — an unparsable URL must be a "no" and not an exception.</summary>
    internal static bool IsAllowedAssetUrl(string url)
    {
        const string scheme = "https://";
        if (url == null || !url.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            return false;

        int start = scheme.Length;
        int end = url.IndexOf('/', start);
        string host = end < 0 ? url.Substring(start) : url.Substring(start, end - start);
        int at = host.IndexOf('@');
        if (at >= 0)
            return false; // userinfo in an asset URL is never legitimate here
        int colon = host.IndexOf(':');
        if (colon >= 0)
            host = host.Substring(0, colon);
        host = host.ToLowerInvariant();

        foreach (string exact in AllowedHosts)
        {
            if (host.Equals(exact, StringComparison.Ordinal))
                return true;
        }
        foreach (string suffix in AllowedHostSuffixes)
        {
            if (host.EndsWith(suffix, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>"v0.2.0" and "V0.2.0" both mean 0.2.0; anything else is returned untouched.</summary>
    internal static string StripTagPrefix(string tag) =>
        tag.Length > 1 && (tag[0] == 'v' || tag[0] == 'V') && char.IsDigit(tag[1])
            ? tag.Substring(1)
            : tag;

    /// <summary>
    /// Split a semver-ish string into three numbers and a pre-release tail. Missing components are
    /// zero, so "0.2" parses as 0.2.0. Returns false when the leading component is not a number —
    /// the caller then treats the whole answer as malformed and shows nothing.
    /// </summary>
    internal static bool TryParseVersion(string version, out int major, out int minor, out int patch,
        out string prerelease)
    {
        major = minor = patch = 0;
        prerelease = string.Empty;
        if (string.IsNullOrEmpty(version))
            return false;

        string core = version;
        int plus = core.IndexOf('+');
        if (plus >= 0)
            core = core.Substring(0, plus);
        int dash = core.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = core.Substring(dash + 1);
            core = core.Substring(0, dash);
        }

        string[] parts = core.Split('.');
        if (parts.Length == 0
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out major))
        {
            return false;
        }
        if (parts.Length > 1
            && !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minor))
        {
            return false;
        }
        if (parts.Length > 2
            && !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out patch))
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// Standard semver ordering, narrowed to what this mod ships: numeric core first, then
    /// "a release outranks its own pre-releases", then an ordinal tie-break between two
    /// pre-releases. Returns <see cref="int.MinValue"/> when either side is unparsable, which the
    /// caller reads as "no verdict" and shows nothing.
    /// </summary>
    internal static int Compare(string left, string right)
    {
        if (!TryParseVersion(left, out int lMajor, out int lMinor, out int lPatch, out string lPre))
            return int.MinValue;
        if (!TryParseVersion(right, out int rMajor, out int rMinor, out int rPatch, out string rPre))
            return int.MinValue;

        if (lMajor != rMajor)
            return lMajor < rMajor ? -1 : 1;
        if (lMinor != rMinor)
            return lMinor < rMinor ? -1 : 1;
        if (lPatch != rPatch)
            return lPatch < rPatch ? -1 : 1;

        bool lHas = lPre.Length > 0;
        bool rHas = rPre.Length > 0;
        if (lHas && !rHas)
            return -1;
        if (!lHas && rHas)
            return 1;
        if (!lHas)
            return 0;
        int ordinal = string.CompareOrdinal(lPre, rPre);
        return ordinal == 0 ? 0 : ordinal < 0 ? -1 : 1;
    }

    public override string ToString() =>
        $"tag={Tag} version={Version} asset={AssetName} bytes={AssetSize}";
}

/// <summary>
/// A minimal, non-throwing JSON reader. <see cref="Parse"/> returns
/// <c>Dictionary&lt;string, object?&gt;</c> for objects, <c>List&lt;object?&gt;</c> for arrays,
/// <c>string</c>, <c>double</c>, <c>bool</c> or null — and plain null for ANY malformed input.
/// </summary>
internal static class SelfUpdateJson
{
    /// <summary>Refuse an answer bigger than this outright. A release payload is tens of KB.</summary>
    private const int MaxInputChars = 4 * 1024 * 1024;

    /// <summary>Nesting cap: a hostile or broken payload must not recurse the stack away.</summary>
    private const int MaxDepth = 24;

    internal static object? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text) || text!.Length > MaxInputChars)
            return null;
        try
        {
            int index = 0;
            object? value = ReadValue(text, ref index, 0);
            SkipWhitespace(text, ref index);
            return index >= text.Length ? value : null;
        }
        catch (Exception)
        {
            // Every parse failure is the same outcome to every caller: no answer.
            return null;
        }
    }

    private static object? ReadValue(string s, ref int i, int depth)
    {
        if (depth > MaxDepth)
            throw new FormatException("depth");
        SkipWhitespace(s, ref i);
        if (i >= s.Length)
            throw new FormatException("eof");

        char c = s[i];
        switch (c)
        {
            case '{':
                return ReadObject(s, ref i, depth);
            case '[':
                return ReadArray(s, ref i, depth);
            case '"':
                return ReadString(s, ref i);
            case 't':
                Expect(s, ref i, "true");
                return true;
            case 'f':
                Expect(s, ref i, "false");
                return false;
            case 'n':
                Expect(s, ref i, "null");
                return null;
            default:
                return ReadNumber(s, ref i);
        }
    }

    private static Dictionary<string, object?> ReadObject(string s, ref int i, int depth)
    {
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        i++; // consume the opening brace
        SkipWhitespace(s, ref i);
        if (i < s.Length && s[i] == '}')
        {
            i++;
            return map;
        }
        while (true)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length || s[i] != '"')
                throw new FormatException("key");
            string key = ReadString(s, ref i);
            SkipWhitespace(s, ref i);
            if (i >= s.Length || s[i] != ':')
                throw new FormatException("colon");
            i++;
            map[key] = ReadValue(s, ref i, depth + 1);
            SkipWhitespace(s, ref i);
            if (i >= s.Length)
                throw new FormatException("eof");
            if (s[i] == ',')
            {
                i++;
                continue;
            }
            if (s[i] == '}')
            {
                i++;
                return map;
            }
            throw new FormatException("object");
        }
    }

    private static List<object?> ReadArray(string s, ref int i, int depth)
    {
        var list = new List<object?>();
        i++; // consume the opening bracket
        SkipWhitespace(s, ref i);
        if (i < s.Length && s[i] == ']')
        {
            i++;
            return list;
        }
        while (true)
        {
            list.Add(ReadValue(s, ref i, depth + 1));
            SkipWhitespace(s, ref i);
            if (i >= s.Length)
                throw new FormatException("eof");
            if (s[i] == ',')
            {
                i++;
                continue;
            }
            if (s[i] == ']')
            {
                i++;
                return list;
            }
            throw new FormatException("array");
        }
    }

    private static string ReadString(string s, ref int i)
    {
        i++; // consume the opening quote
        var sb = new StringBuilder(32);
        while (i < s.Length)
        {
            char c = s[i++];
            if (c == '"')
                return sb.ToString();
            if (c != '\\')
            {
                sb.Append(c);
                continue;
            }
            if (i >= s.Length)
                break;
            char esc = s[i++];
            switch (esc)
            {
                case '"': sb.Append('"'); break;
                case '\\': sb.Append('\\'); break;
                case '/': sb.Append('/'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'u':
                    if (i + 4 > s.Length)
                        throw new FormatException("escape");
                    sb.Append((char)ushort.Parse(s.Substring(i, 4), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                default:
                    throw new FormatException("escape");
            }
        }
        throw new FormatException("string");
    }

    private static double ReadNumber(string s, ref int i)
    {
        int start = i;
        if (i < s.Length && (s[i] == '-' || s[i] == '+'))
            i++;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E'
                                || s[i] == '-' || s[i] == '+'))
        {
            i++;
        }
        if (i == start)
            throw new FormatException("number");
        return double.Parse(s.Substring(start, i - start), NumberStyles.Float,
            CultureInfo.InvariantCulture);
    }

    private static void Expect(string s, ref int i, string literal)
    {
        if (i + literal.Length > s.Length
            || string.CompareOrdinal(s, i, literal, 0, literal.Length) != 0)
        {
            throw new FormatException("literal");
        }
        i += literal.Length;
    }

    private static void SkipWhitespace(string s, ref int i)
    {
        while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r'))
            i++;
    }
}
