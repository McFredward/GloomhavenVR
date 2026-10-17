using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Core.SteamLink
{
    // Main-thread, explicitly requested synthetic output only. Update owns every
    // upload/fence query; eye callbacks only borrow immutable textures and commit
    // their actual blit. A CPU callback commit is never called GPU completion.
    internal sealed class RegistrationTextures : IDisposable
    {
        public const int PoolSize = 3;
        internal struct EyeSubmission
        {
            public bool ActualXrCallback;
            public Matrix4x4 View, Projection;
            public int SourceWidth, SourceHeight, DestinationWidth, DestinationHeight;
            public bool HasDestination, SourceSrgb, DestinationSrgb, SrgbWrite;
            public ColorSpace ColorSpace;
        }
        private sealed class Slot
        {
            public readonly RegistrationSourceBuffers Left = RegistrationSource.CreateBuffers();
            public readonly RegistrationSourceBuffers Right = RegistrationSource.CreateBuffers();
            public readonly byte[] Upload = new byte[RegistrationSource.CanvasWidth * RegistrationSource.CanvasHeight * 3];
            public Texture2D? LeftTexture, RightTexture;
            public GraphicsFence Fence;
            public bool Retiring;
            public int Begun;
            public RegistrationPairContext? Context;
            public RegistrationSourceRecord? LeftRecord, RightRecord;
            public EyeSubmission LeftSubmission, RightSubmission;
        }

        private readonly Slot[] _slots = new Slot[PoolSize];
        private readonly RegistrationPairPolicy _pairs = new RegistrationPairPolicy();
        private readonly bool _captureSubmissionMetadata;
        private readonly bool _presenterProfile;
        private RegistrationPattern? _leftWorkspace, _rightWorkspace;
        private Slot? _active;
        private bool _disposed;
        private long _preparedCohort = -1;
        private int _receiptsReported;
        private RegistrationSourceRecord? _receiptLeft, _receiptRight;
        private EyeSubmission _receiptLeftSubmission, _receiptRightSubmission;
        public EyeSubmission LastReportedLeftSubmission { get; private set; }
        public EyeSubmission LastReportedRightSubmission { get; private set; }
        public string Session => _pairs.Session;
        public string Profile => _presenterProfile ? PresenterSource.Profile : RegistrationSource.Profile;
        public int ProfileRevision => _presenterProfile ? PresenterSource.ProfileRevision : 1;
        public RegistrationPairContext? ActiveContext => _active?.Context;
        public uint CompletedPairs { get; private set; }
        public uint SkippedCohorts { get; private set; }
        public bool NeedsSubmissionReceipt => _captureSubmissionMetadata && _receiptsReported < 4;

        public RegistrationTextures(bool captureSubmissionMetadata = false, bool presenterProfile = false)
        {
            _captureSubmissionMetadata = captureSubmissionMetadata;
            _presenterProfile = presenterProfile;
            // This is a capability check, not a backend-name guess. D3D11 has
            // not been tested by the local OpenGL/Vulkan production fixtures.
            if (!SystemInfo.supportsGraphicsFence)
                throw new NotSupportedException("Dynamic source registration requires CPU graphics fences.");
            try
            {
                GraphicsFence cold = Graphics.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation,
                    SynchronisationStageFlags.AllGPUOperations);
                _ = cold.passed; // Reject a provider that advertises but refuses this CPU query.
                for (int index = 0; index < _slots.Length; index++)
                {
                    var slot = new Slot();
                    _slots[index] = slot;
                    slot.LeftTexture = CreateTexture("left", index);
                    slot.RightTexture = CreateTexture("right", index);
                }
            }
            catch { Dispose(); throw; }
        }

        // Call once in Update, after closing the preceding frame's callback
        // latch. The fence is queued AFTER all prior Graphics.Blit consumers.
        // Do not replace it with a fixed frame delay or a read of source pixels.
        public void Prepare(long engineFrameCohort)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RegistrationTextures));
            if (engineFrameCohort < 0 || engineFrameCohort <= _preparedCohort)
                throw new InvalidOperationException("Registration Update cohort did not advance.");
            _preparedCohort = engineFrameCohort;
            Slot? previous = _active;
            _active = null; // No callback can borrow a retiring slot again.
            if (previous != null)
            {
                RegistrationPairContext context = previous.Context!;
                if (previous.Begun == 0)
                {
                    if (!_pairs.DiscardPrepared(context, out RegistrationPairRefusal reason))
                        throw Refused(reason);
                }
                else if (!_pairs.IsPairComplete(context))
                {
                    _pairs.Stop();
                    throw new InvalidOperationException("Incomplete stereo source pair; output stopped.");
                }
                else
                {
                    if (CompletedPairs != uint.MaxValue) CompletedPairs++;
                    // Bounded first-pair receipts only. No growing per-frame log/history.
                    if (_receiptsReported < 4)
                    {
                        _receiptLeft = previous.LeftRecord;
                        _receiptRight = previous.RightRecord;
                        _receiptLeftSubmission = previous.LeftSubmission;
                        _receiptRightSubmission = previous.RightSubmission;
                    }
                }
                previous.Fence = Graphics.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation,
                    SynchronisationStageFlags.AllGPUOperations);
                previous.Retiring = true;
            }

            Slot? available = null;
            for (int index = 0; index < _slots.Length; index++)
            {
                Slot slot = _slots[index];
                if (slot.Retiring && slot.Fence.passed) slot.Retiring = false;
                if (!slot.Retiring && available == null) available = slot;
            }
            if (available == null)
            {
                if (SkippedCohorts != uint.MaxValue) SkippedCohorts++;
                return; // Skip the WHOLE cohort, never overwrite a busy/half pair.
            }
            if (!_pairs.TryPreparePair(engineFrameCohort, out RegistrationPairContext? next,
                    out RegistrationPairRefusal refusal)) throw Refused(refusal);
            available.Context = next;
            available.Begun = 0;
            available.LeftSubmission = available.RightSubmission = default;
            _leftWorkspace = RegistrationSource.PreparePattern(Session, ProbeEye.Left, next!.Phase, _leftWorkspace);
            _rightWorkspace = RegistrationSource.PreparePattern(Session, ProbeEye.Right, next.Phase, _rightWorkspace);
            available.LeftRecord = _presenterProfile
                ? PresenterSource.Stamp(_leftWorkspace, next, available.Left)
                : RegistrationSource.Stamp(_leftWorkspace, next, available.Left);
            available.RightRecord = _presenterProfile
                ? PresenterSource.Stamp(_rightWorkspace, next, available.Right)
                : RegistrationSource.Stamp(_rightWorkspace, next, available.Right);
            Upload(available.LeftTexture!, available.Left.Canvas, available.Upload);
            Upload(available.RightTexture!, available.Right.Canvas, available.Upload);
            _active = available; // Publish only after BOTH complete textures are uploaded.
        }

        public bool TryBeginEye(ProbeEye eye, long engineFrameCohort, out Texture2D? texture)
        {
            texture = null;
            Slot? slot = _active;
            if (_disposed || slot == null) return false;
            if (!_pairs.TryBeginEye(slot.Context!, engineFrameCohort, eye, out RegistrationPairRefusal reason))
            {
                _active = null;
                throw Refused(reason);
            }
            slot.Begun |= eye == ProbeEye.Left ? 1 : 2;
            texture = eye == ProbeEye.Left ? slot.LeftTexture : slot.RightTexture;
            return true;
        }

        public void CompleteEye(ProbeEye eye) => CompleteEye(eye, default);

        public void CompleteEye(ProbeEye eye, EyeSubmission submission)
        {
            Slot? slot = _active;
            if (_disposed || slot == null
                || !_pairs.CompleteEye(slot.Context!, eye, out RegistrationPairRefusal reason))
                throw new InvalidOperationException("Registration eye commit has no admitted pair.");
            if (eye == ProbeEye.Left) slot.LeftSubmission = submission;
            else slot.RightSubmission = submission;
        }

        public bool TakeCompletedReceipt(out RegistrationSourceRecord? left, out RegistrationSourceRecord? right)
        {
            left = _receiptLeft; right = _receiptRight;
            if (left == null || right == null) return false;
            LastReportedLeftSubmission = _receiptLeftSubmission;
            LastReportedRightSubmission = _receiptRightSubmission;
            _receiptLeft = _receiptRight = null;
            _receiptsReported++;
            return true;
        }

        private static Texture2D CreateTexture(string eye, int index)
        {
            var texture = new Texture2D(RegistrationSource.CanvasWidth, RegistrationSource.CanvasHeight,
                TextureFormat.RGB24, false, false);
            try
            {
                texture.name = "GloomhavenVR.SteamLink.Registration." + eye + "." + index;
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
                return texture;
            }
            catch { UnityEngine.Object.Destroy(texture); throw; }
        }

        private static void Upload(Texture2D texture, byte[] topDown, byte[] bottomUp)
        {
            int row = RegistrationSource.CanvasWidth * 3;
            for (int y = 0; y < RegistrationSource.CanvasHeight; y++)
                Buffer.BlockCopy(topDown, y * row, bottomUp,
                    (RegistrationSource.CanvasHeight - 1 - y) * row, row);
            // Unity copies the managed data into this owned texture's CPU store.
            // The texture itself cannot be uploaded again before its fence passes.
            texture.LoadRawTextureData(bottomUp);
            texture.Apply(false, false);
        }

        private static Exception Refused(RegistrationPairRefusal reason) =>
            new InvalidOperationException("Registration pair refused: " + reason);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _pairs.Stop();
            _active = null;
            _receiptLeft = _receiptRight = null;
            // Normal deferred Unity destruction after closing admission. Never
            // DestroyImmediate, block the CPU, or reuse these retired resources.
            foreach (Slot? slot in _slots)
                if (slot != null)
                {
                    if (slot.LeftTexture != null) UnityEngine.Object.Destroy(slot.LeftTexture);
                    if (slot.RightTexture != null) UnityEngine.Object.Destroy(slot.RightTexture);
                }
        }
    }
}
