using System;
using System.Collections.Generic;
using Iced.Intel;

namespace ConSimVR.Il2CppFix
{
    /// <summary>Small x64 control-flow helpers over <see cref="ICodeMemory"/>.</summary>
    public static class X64Flow
    {
        private sealed class MemoryCodeReader : CodeReader
        {
            private readonly ICodeMemory _memory;
            private ulong _address;
            private readonly ulong _limit;
            private readonly byte[] _page = new byte[256];
            private ulong _pageStart = ulong.MaxValue;
            private int _pageLength;

            public MemoryCodeReader(ICodeMemory memory, ulong address, int maxBytes)
            {
                _memory = memory;
                _address = address;
                _limit = address + (ulong)maxBytes;
            }

            public override int ReadByte()
            {
                if (_address >= _limit)
                    return -1;
                if (_address < _pageStart || _address >= _pageStart + (ulong)_pageLength)
                {
                    _pageStart = _address;
                    _pageLength = _page.Length;
                    // Shrink the window near the end of readable memory.
                    while (_pageLength > 0 && !_memory.TryRead(_pageStart, _page.AsSpan(0, _pageLength)))
                        _pageLength /= 2;
                    if (_pageLength == 0)
                        return -1;
                }
                return _page[(int)(_address++ - _pageStart)];
            }
        }

        /// <summary>Decodes instructions from <paramref name="start"/> until int3, end of data or <paramref name="maxInstructions"/>.</summary>
        public static IEnumerable<Instruction> Decode(ICodeMemory memory, ulong start, int maxInstructions = 4096, int maxBytes = 64 * 1024)
        {
            var decoder = Decoder.Create(64, new MemoryCodeReader(memory, start, maxBytes), start);
            for (int i = 0; i < maxInstructions; i++)
            {
                decoder.Decode(out var instruction);
                if (decoder.LastError == DecoderError.NoMoreBytes || instruction.IsInvalid)
                    yield break;
                yield return instruction;
            }
        }

        /// <summary>
        /// Mirror of Il2CppInterop's <c>XrefScannerLowLevel.JumpTargets</c>: direct near call/jmp targets
        /// up to the first ret (unless <paramref name="ignoreRet"/>) or int3. A leading unconditional jump
        /// is treated as a thunk and ends the scan.
        /// </summary>
        public static List<ulong> JumpTargets(ICodeMemory memory, ulong start, bool ignoreRet = false)
        {
            var result = new List<ulong>();
            bool firstFlowControl = true;
            foreach (var instruction in Decode(memory, start))
            {
                if (instruction.Mnemonic == Mnemonic.Int3)
                    break;
                if (instruction.FlowControl == FlowControl.Return && !ignoreRet)
                    break;

                if ((instruction.FlowControl == FlowControl.UnconditionalBranch || instruction.FlowControl == FlowControl.Call)
                    && !instruction.IsJmpShort && IsNearBranch(instruction.Op0Kind))
                {
                    result.Add(instruction.NearBranchTarget);
                    if (firstFlowControl && instruction.FlowControl == FlowControl.UnconditionalBranch)
                        break;
                }

                if (instruction.FlowControl != FlowControl.Next)
                    firstFlowControl = false;
            }
            return result;
        }

        private static bool IsNearBranch(OpKind kind) =>
            kind == OpKind.NearBranch16 || kind == OpKind.NearBranch32 || kind == OpKind.NearBranch64;

        /// <summary>
        /// Some protected builds wrap every IL2CPP export as <c>call &lt;init-wait stub&gt;; jmp &lt;body&gt;</c>.
        /// Returns the body address for such wrappers, otherwise <paramref name="export"/> unchanged.
        /// </summary>
        public static ulong SkipExportWrapper(ICodeMemory memory, ulong export, out bool wrapped)
        {
            wrapped = false;
            using var e = Decode(memory, export, 2).GetEnumerator();
            if (!e.MoveNext())
                return export;
            var first = e.Current;
            if (first.Mnemonic != Mnemonic.Call || !IsNearBranch(first.Op0Kind))
                return export;
            if (!e.MoveNext())
                return export;
            var second = e.Current;
            if (second.Mnemonic != Mnemonic.Jmp || !IsNearBranch(second.Op0Kind))
                return export;
            if (!IsTrivialStub(memory, first.NearBranchTarget))
                return export;
            wrapped = true;
            return second.NearBranchTarget;
        }

        /// <summary>A stub that makes no calls of its own and returns within a few instructions.</summary>
        private static bool IsTrivialStub(ICodeMemory memory, ulong address)
        {
            int count = 0;
            foreach (var instruction in Decode(memory, address, 32))
            {
                count++;
                if (instruction.FlowControl == FlowControl.Call || instruction.FlowControl == FlowControl.IndirectCall)
                    return false;
                if (instruction.FlowControl == FlowControl.Return)
                    return count <= 16;
            }
            return false;
        }

        /// <summary>Finds the first direct call/jmp to <paramref name="target"/> inside the function at <paramref name="start"/>.</summary>
        public static bool TryFindBranchTo(ICodeMemory memory, ulong start, ulong target, out List<Instruction> precedingInstructions)
        {
            precedingInstructions = new List<Instruction>();
            foreach (var instruction in Decode(memory, start, 2048))
            {
                if (instruction.Mnemonic == Mnemonic.Int3)
                    break;
                if ((instruction.Mnemonic == Mnemonic.Call || instruction.Mnemonic == Mnemonic.Jmp)
                    && IsNearBranch(instruction.Op0Kind) && instruction.NearBranchTarget == target)
                    return true;
                precedingInstructions.Add(instruction);
            }
            return false;
        }
    }
}
