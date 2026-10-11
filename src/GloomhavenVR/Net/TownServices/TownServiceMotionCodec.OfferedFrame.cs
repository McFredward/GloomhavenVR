using System.IO;

namespace GloomhavenVR.Net.TownServices;

internal static partial class TownServiceMotionCodec
{
    // Additive TLV109; a reader of record97/98 can skip this exact relation.
    internal const byte OfferedFrameRecordId = 109;

    private static void WriteOfferedFrame(BinaryWriter writer, TownServiceMotionEntry entry)
    {
        writer.Write(entry.Binding);
        writer.Write(entry.Visible);
        writer.Write(entry.HasCanvasFrame);
        writer.Write(entry.OfferedLocalScale);
        writer.Write(entry.OfferedModule); writer.Write(entry.OfferedStructure); writer.Write(entry.OfferedBinding);
        Floats(writer, entry.Numbers);
        if (entry.HasCanvasFrame) Floats(writer, entry.CanvasPose);
    }

    private static void ReadOfferedFrame(BinaryReader reader, TownServiceMotionEntry entry)
    {
        entry.Binding = reader.ReadUInt32();
        entry.Visible = Bool(reader);
        entry.HasCanvasFrame = Bool(reader);
        entry.OfferedLocalScale = Bool(reader);
        entry.OfferedModule = reader.ReadUInt16(); entry.OfferedStructure = reader.ReadUInt32();
        entry.OfferedBinding = reader.ReadUInt32(); entry.Numbers = Floats(reader, 10);
        if (entry.HasCanvasFrame) entry.CanvasPose = Floats(reader, 10);
    }

    private static void ValidateOfferedFrame(TownServiceMotionEntry entry)
    {
        if (entry.Lane != 0 || entry.Service != 3 || entry.PublicClaim != 0
            || entry.Binding == 0
            || entry.Visible && (entry.OfferedBinding == 0 || entry.OfferedStructure == 0
                || entry.OfferedModule >= TownServiceFrame.VoiceModule || entry.OfferedModule == entry.Module)
            || !entry.Visible && (entry.OfferedModule != 0 || entry.OfferedStructure != 0 || entry.OfferedBinding != 0))
            throw new InvalidDataException("Invalid original offered print affinity.");
        if (!entry.Visible && entry.HasCanvasFrame) throw new InvalidDataException("Withdrawn offered frame has a canvas.");
        if (entry.OfferedLocalScale && !entry.HasCanvasFrame) throw new InvalidDataException("Original canvas scale has no canvas frame.");
        ValidateOfferedPose(entry.Numbers);
        if (entry.HasCanvasFrame) ValidateOfferedPose(entry.CanvasPose);
    }

    private static void ValidateOfferedPose(float[] pose)
    {
        CheckFloats(pose, 10);
        float q = 0f;
        for (int i = 3; i <= 6; i++) q += pose[i] * pose[i];
        if (q < .99f || q > 1.01f) throw new InvalidDataException("Invalid offered print rotation.");
        for (int i = 0; i < 3; i++)
            if (pose[i] < -100000f || pose[i] > 100000f
                || System.Math.Abs(pose[i + 7]) < .0000001f || System.Math.Abs(pose[i + 7]) > 100000f)
                throw new InvalidDataException("Invalid offered print frame.");
    }
}
