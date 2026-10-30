namespace GloomhavenVR.Core;

internal static partial class PerfFrameSplit
{
    // Camera.Render can nest inside another camera's onPreCull. A single open
    // id discarded the outer pass, making its inclusive callback interval vanish
    // from the camera report. This bounded stack observes callbacks only; it never
    // changes their subscription order, cameras or render state. Inclusive parent
    // and child intervals overlap and must never be added as independent work.
    private struct CameraPass
    {
        internal int Id;
        internal long Open, CullDone;
    }

    private static readonly CameraPass[] CameraPassStack = new CameraPass[32];
    private static int _cameraPassDepth, _cameraPassFrame = -1;
    private static int _cameraPassFaults, _cameraPassPeak;

    private static void ResetCameraPasses(int frame)
    {
        _cameraPassDepth = 0;
        _cameraPassFrame = frame;
        _cameraPassFaults = 0;
        _cameraPassPeak = 0;
    }

    private static void AlignCameraPassFrame(int frame)
    {
        if (_cameraPassFrame == frame) return;
        // A missed host boundary or a callback retained across frames is not a
        // multi-frame camera render. Discard it explicitly, never price its gap.
        _cameraPassFaults += _cameraPassDepth + 1;
        _cameraPassDepth = 0;
        _cameraPassFrame = frame;
    }

    private static int CameraPassIndex(int id)
    {
        for (int i = _cameraPassDepth - 1; i >= 0; i--)
            if (CameraPassStack[i].Id == id) return i;
        return -1;
    }

    private static void OpenCameraPass(int id, int frame, long timestamp)
    {
        AlignCameraPassFrame(frame);
        int previous = CameraPassIndex(id);
        // Unity cannot recursively render the same camera. A repeated pre-cull
        // therefore abandons an unmatched pass and its orphaned children.
        if (previous >= 0)
        {
            _cameraPassFaults += _cameraPassDepth - previous;
            _cameraPassDepth = previous;
        }
        if (_cameraPassDepth == CameraPassStack.Length)
        {
            _cameraPassFaults++;
            return;
        }
        CameraPassStack[_cameraPassDepth++] = new CameraPass { Id = id, Open = timestamp };
        if (_cameraPassDepth > _cameraPassPeak) _cameraPassPeak = _cameraPassDepth;
    }

    private static void MarkCameraPassCull(int id, int frame, long timestamp)
    {
        AlignCameraPassFrame(frame);
        int index = CameraPassIndex(id);
        if (index >= 0) CameraPassStack[index].CullDone = timestamp;
    }

    private static bool CloseCameraPass(int id, int frame, out CameraPass pass)
    {
        AlignCameraPassFrame(frame);
        int index = CameraPassIndex(id);
        if (index < 0)
        {
            _cameraPassFaults++;
            pass = default;
            return false;
        }
        _cameraPassFaults += _cameraPassDepth - index - 1;
        pass = CameraPassStack[index];
        _cameraPassDepth = index;
        return true;
    }
}
