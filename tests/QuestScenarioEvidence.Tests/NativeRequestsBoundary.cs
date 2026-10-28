using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

// The only controlled boundary in the native-request fixture. Original .Net
// scheduling/decoding and Unity entity/engine callbacks remain actual DLL code.
public static class NativeRequestsBoundary
{
    public static int NoiseHandle, Builds, Pops, TickCalls;
    public static bool Noise;
    static int _handle = -1, _key = 1;
    static IntPtr _lease;
    static readonly Queue<int> Ready = new Queue<int>();
    public static int Create(int old) { return old != 0 ? old : _handle--; }
    public static void Build(int handle) { Builds++; Ready.Enqueue(handle); }
    public static void Tick() { TickCalls++; }
    public static int Pop(out int size, out IntPtr bytes)
    {
        Pops++; size = 0; bytes = IntPtr.Zero;
        if (_lease != IntPtr.Zero) { Marshal.FreeHGlobal(_lease); _lease = IntPtr.Zero; }
        int handle = Ready.Count > 0 ? Ready.Dequeue() : Noise ? NoiseHandle : 0;
        if (handle == 0) return 0;
        // One task exceeds the ORIGINAL five-ms task-first scheduling budget.
        Thread.Sleep(9);
        byte[] data;
        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(0x41414141); writer.Write(_key++); writer.Write(0);
                writer.Write(0f); writer.Write(0f); writer.Write(0f);
                writer.Write(0x4f4f4f4f); writer.Write(0);
                writer.Write(0x47474747); writer.Write(0);
                data = stream.ToArray();
            }
        }
        _lease = Marshal.AllocHGlobal(data.Length); Marshal.Copy(data, 0, _lease, data.Length);
        size = data.Length; bytes = _lease; return handle;
    }
    public static int NoTask(out int size, out IntPtr bytes) { size = 0; bytes = IntPtr.Zero; return 0; }
    public static string NoAsset(out int context, out int id) { context = id = 0; return null; }
    public static void Reset() { Builds = Pops = 0; Ready.Clear(); Noise = false; }
}
