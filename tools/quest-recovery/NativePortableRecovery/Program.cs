using System.Reflection;
using AssetRipper.TextureDecoder.Bc;
using AssetRipper.TextureDecoder.Rgb.Formats;
using Fmod5Sharp;
using System.Text.Json;

if (args[0] == "inspect")
{
    foreach (var type in typeof(FsbLoader).Assembly.GetTypes().Where(type => type.Name.Contains("Sample") || type.Name.Contains("Header")))
    {
        Console.WriteLine(type.FullName);
        foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
            if (member.MemberType is MemberTypes.Field or MemberTypes.Property) Console.WriteLine("  " + member);
    }
    return;
}
if (args[0] == "fsb")
{
    var original = File.ReadAllBytes(args[1]);
    if (!FsbLoader.TryLoadFsbFromByteArray(original, out var bank) || bank == null || bank.Samples.Count != 1)
        throw new InvalidDataException("Original FSB must contain exactly one native sample.");
    var sample = bank.Samples.Single();
    var channelField = sample.Metadata.GetType().GetField("NumChannels", BindingFlags.NonPublic | BindingFlags.Instance);
    if (channelField == null || channelField.FieldType != typeof(int)) throw new InvalidDataException("Pinned Fmod channel field changed.");
    Console.WriteLine(JsonSerializer.Serialize(new { channels = sample.Metadata.Channels, baseChannels = channelField.GetValue(sample.Metadata),
        frequency = sample.Metadata.Frequency, frames = sample.Metadata.SampleCount }));
    channelField.SetValue(sample.Metadata, int.Parse(args[3]));
    if (!sample.RebuildAsStandardFileFormat(out var data, out var extension) || extension != "ogg" || data == null)
        throw new InvalidDataException("Original Vorbis FSB could not be rebuilt.");
    File.WriteAllBytes(args[2], data);
    return;
}
if (args[0] == "bc6h")
{
    int width = int.Parse(args[3]), mips = int.Parse(args[4]);
    var original = File.ReadAllBytes(args[1]);
    using var output = File.Create(args[2]);
    int offset = 0;
    for (int face = 0; face < 6; face++)
        for (int mip = 0; mip < mips; mip++)
        {
            int size = Math.Max(1, width >> mip);
            int compressed = ((size + 3) / 4) * ((size + 3) / 4) * 16;
            var pixels = new byte[size * size * 8];
            Bc6h.Decompress<ColorRGBA<Half>, Half>(original.AsSpan(offset, compressed), size, size, false, pixels.AsSpan());
            output.Write(pixels);
            offset += compressed;
        }
    if (offset != original.Length) throw new InvalidDataException("Original BC6H cubemap mip/face bytes were not fully consumed.");
    return;
}
throw new InvalidDataException("Unknown native recovery mode.");
