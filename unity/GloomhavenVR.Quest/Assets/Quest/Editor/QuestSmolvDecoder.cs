#if UNITY_EDITOR
// Decoder and opcode data ported from aras-p/smol-v55000efe742f56d8b51223b9ea7775a8f0501881.
// Copyright (c) 2016-2025 Aras Pranckevicius. MIT license, see tools/quest-shaders/vendor/smol-v/smolv.h.
using System;
using System.Collections.Generic;
namespace GloomhavenVR.Quest.Editor
{
    internal static class QuestSmolvDecoder
    {
        private static readonly byte[] OpData = {
            0,0,0,0, // Nop
            1,1,0,0, // Undef
            0,0,0,0, // SourceContinued
            0,0,0,1, // Source
            0,0,0,0, // SourceExtension
            0,0,0,0, // Name
            0,0,0,0, // MemberName
            0,0,0,0, // String
            0,0,0,1, // Line
            1,1,0,0, // #9
            0,0,0,0, // Extension
            1,0,0,0, // ExtInstImport
            1,1,0,1, // ExtInst
            1,1,2,1, // VectorShuffleCompact - new in SMOLV
            0,0,0,1, // MemoryModel
            0,0,0,1, // EntryPoint
            0,0,0,1, // ExecutionMode
            0,0,0,1, // Capability
            1,1,0,0, // #18
            1,0,0,1, // TypeVoid
            1,0,0,1, // TypeBool
            1,0,0,1, // TypeInt
            1,0,0,1, // TypeFloat
            1,0,0,1, // TypeVector
            1,0,0,1, // TypeMatrix
            1,0,0,1, // TypeImage
            1,0,0,1, // TypeSampler
            1,0,0,1, // TypeSampledImage
            1,0,0,1, // TypeArray
            1,0,0,1, // TypeRuntimeArray
            1,0,0,1, // TypeStruct
            1,0,0,1, // TypeOpaque
            1,0,0,1, // TypePointer
            1,0,0,1, // TypeFunction
            1,0,0,1, // TypeEvent
            1,0,0,1, // TypeDeviceEvent
            1,0,0,1, // TypeReserveId
            1,0,0,1, // TypeQueue
            1,0,0,1, // TypePipe
            0,0,0,1, // TypeForwardPointer
            1,1,0,0, // #40
            1,1,0,0, // ConstantTrue
            1,1,0,0, // ConstantFalse
            1,1,0,0, // Constant
            1,1,9,0, // ConstantComposite
            1,1,0,1, // ConstantSampler
            1,1,0,0, // ConstantNull
            1,1,0,0, // #47
            1,1,0,0, // SpecConstantTrue
            1,1,0,0, // SpecConstantFalse
            1,1,0,0, // SpecConstant
            1,1,9,0, // SpecConstantComposite
            1,1,0,0, // SpecConstantOp
            1,1,0,0, // #53
            1,1,0,1, // Function
            1,1,0,0, // FunctionParameter
            0,0,0,0, // FunctionEnd
            1,1,9,0, // FunctionCall
            1,1,0,0, // #58
            1,1,0,1, // Variable
            1,1,0,0, // ImageTexelPointer
            1,1,1,1, // Load
            0,0,2,1, // Store
            0,0,0,0, // CopyMemory
            0,0,0,0, // CopyMemorySized
            1,1,0,1, // AccessChain
            1,1,0,0, // InBoundsAccessChain
            1,1,0,0, // PtrAccessChain
            1,1,0,0, // ArrayLength
            1,1,0,0, // GenericPtrMemSemantics
            1,1,0,0, // InBoundsPtrAccessChain
            0,0,0,1, // Decorate
            0,0,0,1, // MemberDecorate
            1,0,0,0, // DecorationGroup
            0,0,0,0, // GroupDecorate
            0,0,0,0, // GroupMemberDecorate
            1,1,0,0, // #76
            1,1,1,1, // VectorExtractDynamic
            1,1,2,1, // VectorInsertDynamic
            1,1,2,1, // VectorShuffle
            1,1,9,0, // CompositeConstruct
            1,1,1,1, // CompositeExtract
            1,1,2,1, // CompositeInsert
            1,1,1,0, // CopyObject
            1,1,0,0, // Transpose
            1,1,0,0, // #85
            1,1,0,0, // SampledImage
            1,1,2,1, // ImageSampleImplicitLod
            1,1,2,1, // ImageSampleExplicitLod
            1,1,3,1, // ImageSampleDrefImplicitLod
            1,1,3,1, // ImageSampleDrefExplicitLod
            1,1,2,1, // ImageSampleProjImplicitLod
            1,1,2,1, // ImageSampleProjExplicitLod
            1,1,3,1, // ImageSampleProjDrefImplicitLod
            1,1,3,1, // ImageSampleProjDrefExplicitLod
            1,1,2,1, // ImageFetch
            1,1,3,1, // ImageGather
            1,1,3,1, // ImageDrefGather
            1,1,2,1, // ImageRead
            0,0,3,1, // ImageWrite
            1,1,1,0, // Image
            1,1,1,0, // ImageQueryFormat
            1,1,1,0, // ImageQueryOrder
            1,1,2,0, // ImageQuerySizeLod
            1,1,1,0, // ImageQuerySize
            1,1,2,0, // ImageQueryLod
            1,1,1,0, // ImageQueryLevels
            1,1,1,0, // ImageQuerySamples
            1,1,0,0, // #108
            1,1,1,0, // ConvertFToU
            1,1,1,0, // ConvertFToS
            1,1,1,0, // ConvertSToF
            1,1,1,0, // ConvertUToF
            1,1,1,0, // UConvert
            1,1,1,0, // SConvert
            1,1,1,0, // FConvert
            1,1,1,0, // QuantizeToF16
            1,1,1,0, // ConvertPtrToU
            1,1,1,0, // SatConvertSToU
            1,1,1,0, // SatConvertUToS
            1,1,1,0, // ConvertUToPtr
            1,1,1,0, // PtrCastToGeneric
            1,1,1,0, // GenericCastToPtr
            1,1,1,1, // GenericCastToPtrExplicit
            1,1,1,0, // Bitcast
            1,1,0,0, // #125
            1,1,1,0, // SNegate
            1,1,1,0, // FNegate
            1,1,2,0, // IAdd
            1,1,2,0, // FAdd
            1,1,2,0, // ISub
            1,1,2,0, // FSub
            1,1,2,0, // IMul
            1,1,2,0, // FMul
            1,1,2,0, // UDiv
            1,1,2,0, // SDiv
            1,1,2,0, // FDiv
            1,1,2,0, // UMod
            1,1,2,0, // SRem
            1,1,2,0, // SMod
            1,1,2,0, // FRem
            1,1,2,0, // FMod
            1,1,2,0, // VectorTimesScalar
            1,1,2,0, // MatrixTimesScalar
            1,1,2,0, // VectorTimesMatrix
            1,1,2,0, // MatrixTimesVector
            1,1,2,0, // MatrixTimesMatrix
            1,1,2,0, // OuterProduct
            1,1,2,0, // Dot
            1,1,2,0, // IAddCarry
            1,1,2,0, // ISubBorrow
            1,1,2,0, // UMulExtended
            1,1,2,0, // SMulExtended
            1,1,0,0, // #153
            1,1,1,0, // Any
            1,1,1,0, // All
            1,1,1,0, // IsNan
            1,1,1,0, // IsInf
            1,1,1,0, // IsFinite
            1,1,1,0, // IsNormal
            1,1,1,0, // SignBitSet
            1,1,2,0, // LessOrGreater
            1,1,2,0, // Ordered
            1,1,2,0, // Unordered
            1,1,2,0, // LogicalEqual
            1,1,2,0, // LogicalNotEqual
            1,1,2,0, // LogicalOr
            1,1,2,0, // LogicalAnd
            1,1,1,0, // LogicalNot
            1,1,3,0, // Select
            1,1,2,0, // IEqual
            1,1,2,0, // INotEqual
            1,1,2,0, // UGreaterThan
            1,1,2,0, // SGreaterThan
            1,1,2,0, // UGreaterThanEqual
            1,1,2,0, // SGreaterThanEqual
            1,1,2,0, // ULessThan
            1,1,2,0, // SLessThan
            1,1,2,0, // ULessThanEqual
            1,1,2,0, // SLessThanEqual
            1,1,2,0, // FOrdEqual
            1,1,2,0, // FUnordEqual
            1,1,2,0, // FOrdNotEqual
            1,1,2,0, // FUnordNotEqual
            1,1,2,0, // FOrdLessThan
            1,1,2,0, // FUnordLessThan
            1,1,2,0, // FOrdGreaterThan
            1,1,2,0, // FUnordGreaterThan
            1,1,2,0, // FOrdLessThanEqual
            1,1,2,0, // FUnordLessThanEqual
            1,1,2,0, // FOrdGreaterThanEqual
            1,1,2,0, // FUnordGreaterThanEqual
            1,1,0,0, // #192
            1,1,0,0, // #193
            1,1,2,0, // ShiftRightLogical
            1,1,2,0, // ShiftRightArithmetic
            1,1,2,0, // ShiftLeftLogical
            1,1,2,0, // BitwiseOr
            1,1,2,0, // BitwiseXor
            1,1,2,0, // BitwiseAnd
            1,1,1,0, // Not
            1,1,4,0, // BitFieldInsert
            1,1,3,0, // BitFieldSExtract
            1,1,3,0, // BitFieldUExtract
            1,1,1,0, // BitReverse
            1,1,1,0, // BitCount
            1,1,0,0, // #206
            1,1,0,0, // DPdx
            1,1,0,0, // DPdy
            1,1,0,0, // Fwidth
            1,1,0,0, // DPdxFine
            1,1,0,0, // DPdyFine
            1,1,0,0, // FwidthFine
            1,1,0,0, // DPdxCoarse
            1,1,0,0, // DPdyCoarse
            1,1,0,0, // FwidthCoarse
            1,1,0,0, // #216
            1,1,0,0, // #217
            0,0,0,0, // EmitVertex
            0,0,0,0, // EndPrimitive
            0,0,0,0, // EmitStreamVertex
            0,0,0,0, // EndStreamPrimitive
            1,1,0,0, // #222
            1,1,0,0, // #223
            0,0,3,0, // ControlBarrier
            0,0,2,0, // MemoryBarrier
            1,1,0,0, // #226
            1,1,0,0, // AtomicLoad
            0,0,0,0, // AtomicStore
            1,1,0,0, // AtomicExchange
            1,1,0,0, // AtomicCompareExchange
            1,1,0,0, // AtomicCompareExchangeWeak
            1,1,0,0, // AtomicIIncrement
            1,1,0,0, // AtomicIDecrement
            1,1,0,0, // AtomicIAdd
            1,1,0,0, // AtomicISub
            1,1,0,0, // AtomicSMin
            1,1,0,0, // AtomicUMin
            1,1,0,0, // AtomicSMax
            1,1,0,0, // AtomicUMax
            1,1,0,0, // AtomicAnd
            1,1,0,0, // AtomicOr
            1,1,0,0, // AtomicXor
            1,1,0,0, // #243
            1,1,0,0, // #244
            1,1,0,0, // Phi
            0,0,2,1, // LoopMerge
            0,0,1,1, // SelectionMerge
            1,0,0,0, // Label
            0,0,1,0, // Branch
            0,0,3,1, // BranchConditional
            0,0,0,0, // Switch
            0,0,0,0, // Kill
            0,0,0,0, // Return
            0,0,0,0, // ReturnValue
            0,0,0,0, // Unreachable
            0,0,0,0, // LifetimeStart
            0,0,0,0, // LifetimeStop
            1,1,0,0, // #258
            1,1,0,0, // GroupAsyncCopy
            0,0,0,0, // GroupWaitEvents
            1,1,0,0, // GroupAll
            1,1,0,0, // GroupAny
            1,1,0,0, // GroupBroadcast
            1,1,0,0, // GroupIAdd
            1,1,0,0, // GroupFAdd
            1,1,0,0, // GroupFMin
            1,1,0,0, // GroupUMin
            1,1,0,0, // GroupSMin
            1,1,0,0, // GroupFMax
            1,1,0,0, // GroupUMax
            1,1,0,0, // GroupSMax
            1,1,0,0, // #272
            1,1,0,0, // #273
            1,1,0,0, // ReadPipe
            1,1,0,0, // WritePipe
            1,1,0,0, // ReservedReadPipe
            1,1,0,0, // ReservedWritePipe
            1,1,0,0, // ReserveReadPipePackets
            1,1,0,0, // ReserveWritePipePackets
            0,0,0,0, // CommitReadPipe
            0,0,0,0, // CommitWritePipe
            1,1,0,0, // IsValidReserveId
            1,1,0,0, // GetNumPipePackets
            1,1,0,0, // GetMaxPipePackets
            1,1,0,0, // GroupReserveReadPipePackets
            1,1,0,0, // GroupReserveWritePipePackets
            0,0,0,0, // GroupCommitReadPipe
            0,0,0,0, // GroupCommitWritePipe
            1,1,0,0, // #289
            1,1,0,0, // #290
            1,1,0,0, // EnqueueMarker
            1,1,0,0, // EnqueueKernel
            1,1,0,0, // GetKernelNDrangeSubGroupCount
            1,1,0,0, // GetKernelNDrangeMaxSubGroupSize
            1,1,0,0, // GetKernelWorkGroupSize
            1,1,0,0, // GetKernelPreferredWorkGroupSizeMultiple
            0,0,0,0, // RetainEvent
            0,0,0,0, // ReleaseEvent
            1,1,0,0, // CreateUserEvent
            1,1,0,0, // IsValidEvent
            0,0,0,0, // SetUserEventStatus
            0,0,0,0, // CaptureEventProfilingInfo
            1,1,0,0, // GetDefaultQueue
            1,1,0,0, // BuildNDRange
            1,1,2,1, // ImageSparseSampleImplicitLod
            1,1,2,1, // ImageSparseSampleExplicitLod
            1,1,3,1, // ImageSparseSampleDrefImplicitLod
            1,1,3,1, // ImageSparseSampleDrefExplicitLod
            1,1,2,1, // ImageSparseSampleProjImplicitLod
            1,1,2,1, // ImageSparseSampleProjExplicitLod
            1,1,3,1, // ImageSparseSampleProjDrefImplicitLod
            1,1,3,1, // ImageSparseSampleProjDrefExplicitLod
            1,1,2,1, // ImageSparseFetch
            1,1,3,1, // ImageSparseGather
            1,1,3,1, // ImageSparseDrefGather
            1,1,1,0, // ImageSparseTexelsResident
            0,0,0,0, // NoLine
            1,1,0,0, // AtomicFlagTestAndSet
            0,0,0,0, // AtomicFlagClear
            1,1,0,0, // ImageSparseRead
            1,1,0,0, // SizeOf
            1,1,0,0, // TypePipeStorage
            1,1,0,0, // ConstantPipeStorage
            1,1,0,0, // CreatePipeFromPipeStorage
            1,1,0,0, // GetKernelLocalSizeForSubgroupCount
            1,1,0,0, // GetKernelMaxNumSubgroups
            1,1,0,0, // TypeNamedBarrier
            1,1,0,1, // NamedBarrierInitialize
            0,0,2,1, // MemoryNamedBarrier
            1,1,0,0, // ModuleProcessed
            0,0,0,1, // ExecutionModeId
            0,0,0,1, // DecorateId
            1,1,1,1, // GroupNonUniformElect
            1,1,1,1, // GroupNonUniformAll
            1,1,1,1, // GroupNonUniformAny
            1,1,1,1, // GroupNonUniformAllEqual
            1,1,1,1, // GroupNonUniformBroadcast
            1,1,1,1, // GroupNonUniformBroadcastFirst
            1,1,1,1, // GroupNonUniformBallot
            1,1,1,1, // GroupNonUniformInverseBallot
            1,1,1,1, // GroupNonUniformBallotBitExtract
            1,1,1,1, // GroupNonUniformBallotBitCount
            1,1,1,1, // GroupNonUniformBallotFindLSB
            1,1,1,1, // GroupNonUniformBallotFindMSB
            1,1,1,1, // GroupNonUniformShuffle
            1,1,1,1, // GroupNonUniformShuffleXor
            1,1,1,1, // GroupNonUniformShuffleUp
            1,1,1,1, // GroupNonUniformShuffleDown
            1,1,1,1, // GroupNonUniformIAdd
            1,1,1,1, // GroupNonUniformFAdd
            1,1,1,1, // GroupNonUniformIMul
            1,1,1,1, // GroupNonUniformFMul
            1,1,1,1, // GroupNonUniformSMin
            1,1,1,1, // GroupNonUniformUMin
            1,1,1,1, // GroupNonUniformFMin
            1,1,1,1, // GroupNonUniformSMax
            1,1,1,1, // GroupNonUniformUMax
            1,1,1,1, // GroupNonUniformFMax
            1,1,1,1, // GroupNonUniformBitwiseAnd
            1,1,1,1, // GroupNonUniformBitwiseOr
            1,1,1,1, // GroupNonUniformBitwiseXor
            1,1,1,1, // GroupNonUniformLogicalAnd
            1,1,1,1, // GroupNonUniformLogicalOr
            1,1,1,1, // GroupNonUniformLogicalXor
            1,1,1,1, // GroupNonUniformQuadBroadcast
            1,1,1,1, // GroupNonUniformQuadSwap
        };
        public static byte[] Decode(byte[] data)
        {
            if (data == null || data.Length < 24 || Word(data, 0) != 0x534D4F4Cu)
                throw new InvalidOperationException("Native SMOL-V header is absent.");
            uint version = Word(data, 4), encoding = version >> 24;
            if (encoding > 1 || (version & 0x00ffffffu) < 0x10000u || (version & 0x00ffffffu) > 0x10600u)
                throw new InvalidOperationException("Native SMOL-V version is unsupported.");
            uint size = Word(data, 20);
            if (size < 20 || size > 64 * 1024 * 1024 || (size & 3) != 0)
                throw new InvalidOperationException("Native SMOL-V decoded extent is invalid.");
            var output = new List<uint>((int)size / 4) { 0x07230203u, version & 0x00ffffffu, Word(data, 8), Word(data, 12), Word(data, 16) };
            int cursor = 24, known = encoding == 0 ? 331 : 367;
            uint previousResult = 0, previousDecorate = 0;
            while (cursor < data.Length)
            {
                uint packed = Var(data, ref cursor);
                uint length = ((packed >> 20) << 4) | ((packed >> 4) & 15);
                uint op = Remap(((packed >> 4) & 0xfff0) | (packed & 15));
                length++;
                if (op == 79 || op == 13) length += 4;
                if (op == 71) length += 2;
                if (op == 61 || op == 65) length += 3;
                bool shuffle = op == 13;
                if (shuffle) op = 79;
                if (length > 65535) throw new InvalidOperationException("Native SMOL-V instruction extent is invalid.");
                output.Add((length << 16) | op);
                uint offset = 1;
                int table = op < known ? (int)op * 4 : -1;
                if (table >= 0 && OpData[table + 1] != 0) { output.Add(Var(data, ref cursor)); offset++; }
                if (table >= 0 && OpData[table] != 0)
                {
                    previousResult = unchecked(previousResult + Zig(Var(data, ref cursor)));
                    output.Add(previousResult); offset++;
                }
                if (op == 71 || op == 72)
                {
                    previousDecorate = unchecked(previousDecorate + Zig(Var(data, ref cursor)));
                    output.Add(previousDecorate); offset++;
                }
                if (op == 72)
                {
                    if (cursor >= data.Length) throw new InvalidOperationException("Native SMOL-V member row is truncated.");
                    int count = data[cursor++]; uint previousIndex = 0, previousOffset = 0;
                    if (count == 0) throw new InvalidOperationException("Native SMOL-V member row is empty.");
                    for (int member = 0; member < count; member++)
                    {
                        previousIndex += Var(data, ref cursor);
                        uint decoration = Var(data, ref cursor);
                        int extra = decoration == 0 || decoration >= 2 && decoration <= 5 ? 0 : decoration >= 29 && decoration <= 37 ? 1 : -1;
                        uint memberLength = extra < 0 ? Var(data, ref cursor) + 4 : (uint)(4 + extra);
                        if (member == 0 && memberLength != length) throw new InvalidOperationException("Native SMOL-V member extent differs.");
                        if (member != 0) { output.Add((memberLength << 16) | op); output.Add(previousDecorate); }
                        output.Add(previousIndex); output.Add(decoration);
                        if (decoration == 35)
                        {
                            if (memberLength != 5) throw new InvalidOperationException("Native SMOL-V offset extent differs.");
                            previousOffset += Var(data, ref cursor); output.Add(previousOffset);
                        }
                        else for (uint i = 4; i < memberLength; i++) output.Add(Var(data, ref cursor));
                    }
                    continue;
                }
                int relative = table >= 0 ? OpData[table + 2] : 0;
                for (int i = 0; i < relative && offset < length; i++, offset++)
                    output.Add(unchecked(previousResult - Zig(Var(data, ref cursor))));
                if (shuffle && length <= 9)
                {
                    if (cursor >= data.Length) throw new InvalidOperationException("Native SMOL-V shuffle is truncated.");
                    uint lanes = data[cursor++];
                    if (length > 5) output.Add((lanes >> 6) & 3);
                    if (length > 6) output.Add((lanes >> 4) & 3);
                    if (length > 7) output.Add((lanes >> 2) & 3);
                    if (length > 8) output.Add(lanes & 3);
                }
                else for (; offset < length; offset++)
                    output.Add(table >= 0 && OpData[table + 3] != 0 ? Var(data, ref cursor) : Raw(data, ref cursor));
                if (output.Count > size / 4) throw new InvalidOperationException("Native SMOL-V writes outside decoded extent.");
            }
            if (output.Count != size / 4) throw new InvalidOperationException("Native SMOL-V decoded size differs.");
            byte[] bytes = new byte[size];
            for (int i = 0; i < output.Count; i++) Buffer.BlockCopy(BitConverter.GetBytes(output[i]), 0, bytes, i * 4, 4);
            return bytes;
        }
        private static uint Remap(uint op)
        {
            uint[] pairs = { 71,0, 61,1, 62,2, 65,3, 79,4, 72,7, 248,8, 59,9, 133,10, 129,11, 32,14, 127,15 };
            for (int i = 0; i < pairs.Length; i += 2) { if (op == pairs[i]) return pairs[i + 1]; if (op == pairs[i + 1]) return pairs[i]; }
            return op;
        }
        private static uint Zig(uint value) { return (value & 1) != 0 ? ~(value >> 1) : value >> 1; }
        private static uint Word(byte[] data, int offset)
        {
            if (offset < 0 || offset + 4 > data.Length) throw new InvalidOperationException("Native SMOL-V word is truncated.");
            return BitConverter.ToUInt32(data, offset);
        }
        private static uint Raw(byte[] data, ref int cursor) { uint value = Word(data, cursor); cursor += 4; return value; }
        private static uint Var(byte[] data, ref int cursor)
        {
            uint value = 0;
            for (int i = 0; i < 5; i++)
            {
                if (cursor >= data.Length) throw new InvalidOperationException("Native SMOL-V varint is truncated.");
                byte part = data[cursor++];
                if (i == 4 && (part & 0xf0) != 0) throw new InvalidOperationException("Native SMOL-V varint overflows.");
                value |= (uint)(part & 127) << (i * 7);
                if ((part & 128) == 0) return value;
            }
            throw new InvalidOperationException("Native SMOL-V varint is incomplete.");
        }
    }
}
#endif
