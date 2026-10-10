using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ConSimVR.Il2CppFix
{
    /// <summary>Read-only view of native code plus the module's export table.</summary>
    public interface ICodeMemory
    {
        /// <summary>Copies bytes at <paramref name="address"/>; returns false if the range is not readable.</summary>
        bool TryRead(ulong address, Span<byte> buffer);

        /// <summary>Address of an exported symbol, or 0 when it is not exported.</summary>
        ulong GetExport(string name);
    }

    /// <summary>The loaded GameAssembly.dll inside the running game process.</summary>
    public sealed unsafe class LoadedModuleMemory : ICodeMemory
    {
        private readonly IntPtr _handle;
        private readonly ulong _start;
        private readonly ulong _end;

        public LoadedModuleMemory(IntPtr moduleHandle, ulong baseAddress, ulong size)
        {
            _handle = moduleHandle;
            _start = baseAddress;
            _end = baseAddress + size;
        }

        public bool TryRead(ulong address, Span<byte> buffer)
        {
            if (address < _start || address + (ulong)buffer.Length > _end)
                return false;
            new ReadOnlySpan<byte>((void*)address, buffer.Length).CopyTo(buffer);
            return true;
        }

        public ulong GetExport(string name) =>
            NativeLibrary.TryGetExport(_handle, name, out var address) ? (ulong)address : 0;
    }

    /// <summary>
    /// A PE file on disk mapped by RVA (image base 0). Used by tests to run the resolvers
    /// against the real GameAssembly.dll without launching the game.
    /// </summary>
    public sealed class PeFileMemory : ICodeMemory
    {
        private readonly byte[] _data;
        private readonly List<(uint Va, uint Size, uint RawPtr, uint RawSize)> _sections = new();
        private readonly Dictionary<string, ulong> _exports = new(StringComparer.Ordinal);

        public PeFileMemory(string path)
        {
            _data = File.ReadAllBytes(path);
            int pe = BitConverter.ToInt32(_data, 0x3C);
            int sectionCount = BitConverter.ToUInt16(_data, pe + 6);
            int optionalHeaderSize = BitConverter.ToUInt16(_data, pe + 20);
            int optionalHeader = pe + 24;
            uint exportRva = BitConverter.ToUInt32(_data, optionalHeader + 112);

            for (int i = 0; i < sectionCount; i++)
            {
                int s = optionalHeader + optionalHeaderSize + i * 40;
                _sections.Add((
                    BitConverter.ToUInt32(_data, s + 12),
                    BitConverter.ToUInt32(_data, s + 8),
                    BitConverter.ToUInt32(_data, s + 20),
                    BitConverter.ToUInt32(_data, s + 16)));
            }

            if (exportRva != 0)
                ReadExports(exportRva);
        }

        private void ReadExports(uint exportRva)
        {
            int dir = RvaToOffset(exportRva);
            uint nameCount = BitConverter.ToUInt32(_data, dir + 24);
            int functions = RvaToOffset(BitConverter.ToUInt32(_data, dir + 28));
            int names = RvaToOffset(BitConverter.ToUInt32(_data, dir + 32));
            int ordinals = RvaToOffset(BitConverter.ToUInt32(_data, dir + 36));
            for (int i = 0; i < nameCount; i++)
            {
                int nameOffset = RvaToOffset(BitConverter.ToUInt32(_data, names + i * 4));
                int end = Array.IndexOf(_data, (byte)0, nameOffset);
                string name = Encoding.ASCII.GetString(_data, nameOffset, end - nameOffset);
                ushort ordinal = BitConverter.ToUInt16(_data, ordinals + i * 2);
                _exports[name] = BitConverter.ToUInt32(_data, functions + ordinal * 4);
            }
        }

        private int RvaToOffset(ulong rva)
        {
            foreach (var (va, size, rawPtr, rawSize) in _sections)
            {
                if (rva >= va && rva < va + Math.Max(size, rawSize))
                    return (int)(rva - va + rawPtr);
            }
            return -1;
        }

        public bool TryRead(ulong address, Span<byte> buffer)
        {
            int offset = RvaToOffset(address);
            if (offset < 0 || offset + buffer.Length > _data.Length)
                return false;
            _data.AsSpan(offset, buffer.Length).CopyTo(buffer);
            return true;
        }

        public ulong GetExport(string name) => _exports.TryGetValue(name, out var rva) ? rva : 0;
    }
}
