using System;
using System.Collections.Generic;
using System.Linq;
using Iced.Intel;

namespace ConSimVR.Il2CppFix
{
    /// <summary>
    /// Locates the libil2cpp internals that Il2CppInterop detours, for builds whose exports are wrapped
    /// as <c>call &lt;stub&gt;; jmp &lt;body&gt;</c>. Every resolver validates the code shape it expects and
    /// returns 0 when unsure, so the caller can fall back to Il2CppInterop's own logic.
    /// </summary>
    public sealed class ProtectedExportResolver
    {
        private readonly ICodeMemory _memory;

        public ProtectedExportResolver(ICodeMemory memory) => _memory = memory;

        /// <summary>True when the il2cpp exports in this module use the protection wrapper.</summary>
        public bool ExportsAreWrapped
        {
            get
            {
                ulong export = _memory.GetExport("il2cpp_class_from_name");
                if (export == 0)
                    return false;
                X64Flow.SkipExportWrapper(_memory, export, out bool wrapped);
                return wrapped;
            }
        }

        /// <summary>Body of an IL2CPP API export, or 0 when the export is missing or not wrapped.</summary>
        public ulong ApiBody(string exportName)
        {
            ulong export = _memory.GetExport(exportName);
            if (export == 0)
                return 0;
            ulong body = X64Flow.SkipExportWrapper(_memory, export, out bool wrapped);
            return wrapped ? body : 0;
        }

        private ulong SingleJumpTarget(ulong address)
        {
            if (address == 0)
                return 0;
            var targets = X64Flow.JumpTargets(_memory, address);
            return targets.Count == 1 ? targets[0] : 0;
        }

        /// <summary><c>Class::FromIl2CppType(const Il2CppType*, bool)</c>: the API body tail-calls it.</summary>
        public ulong ClassFromIl2CppType() => SingleJumpTarget(ApiBody("il2cpp_class_from_il2cpp_type"));

        /// <summary><c>Class::FromName(const Il2CppImage*, const char*, const char*)</c>: the API body tail-calls it.</summary>
        public ulong ClassFromName() => SingleJumpTarget(ApiBody("il2cpp_class_from_name"));

        /// <summary>
        /// <c>GenericMethod::GetMethod(const Il2CppGenericMethod*, bool copyMethodPtr)</c>.
        /// Path: il2cpp_object_get_virtual_method -> Object::GetVirtualMethod -> (tail) generic virtual
        /// shim -> GetMethod. The shim builds the Il2CppGenericMethod on the stack and passes
        /// <c>copyMethodPtr = true</c> via <c>mov dl, 1</c>, which is what we validate.
        /// </summary>
        public ulong GenericMethodGetMethod()
        {
            ulong getVirtualMethod = SingleJumpTarget(ApiBody("il2cpp_object_get_virtual_method"));
            if (getVirtualMethod == 0)
                return 0;

            var xrefs = X64Flow.JumpTargets(_memory, getVirtualMethod);
            if (xrefs.Count == 0)
                return 0;
            ulong shim = xrefs[^1];

            var shimXrefs = X64Flow.JumpTargets(_memory, shim);
            if (shimXrefs.Count != 1)
                return 0;
            ulong candidate = shimXrefs[0];

            if (!X64Flow.TryFindBranchTo(_memory, shim, candidate, out var preceding))
                return 0;
            bool passesCopyMethodPtrTrue = preceding.Skip(Math.Max(0, preceding.Count - 8)).Any(i =>
                i.Mnemonic == Mnemonic.Mov && i.Op0Kind == OpKind.Register && i.Op0Register == Register.DL
                && i.Op1Kind == OpKind.Immediate8 && i.Immediate8 == 1);
            return passesCopyMethodPtrTrue ? candidate : 0;
        }

        /// <summary>
        /// <c>MetadataCache::GetTypeInfoFromTypeDefinitionIndex(TypeDefinitionIndex)</c>. With Image::GetType
        /// inlined, il2cpp_image_get_class computes the index and tail-calls it. Validated by the
        /// <c>index == kTypeIndexInvalid (-1)</c> check at its start.
        /// </summary>
        public ulong GetTypeInfoFromTypeDefinitionIndex()
        {
            ulong candidate = SingleJumpTarget(ApiBody("il2cpp_image_get_class"));
            if (candidate == 0)
                return 0;
            bool checksInvalidIndex = X64Flow.Decode(_memory, candidate, 12).Any(i =>
                i.Mnemonic == Mnemonic.Cmp && i.Op0Kind == OpKind.Register && i.Op0Register.IsGPR32()
                && IsImmediate(i.Op1Kind) && unchecked((int)i.GetImmediate(1)) == -1);
            return checksInvalidIndex ? candidate : 0;
        }

        /// <summary>
        /// <c>Class::GetFieldDefaultValue(const FieldInfo*, const Il2CppType**)</c>. Path:
        /// il2cpp_field_static_get_value -> Field::StaticGetValue -> (tail) StaticGetValueInternal, whose
        /// first call is guarded by <c>field->type->attrs &amp; FIELD_ATTRIBUTE_LITERAL (0x40)</c>.
        /// </summary>
        public ulong ClassGetFieldDefaultValue()
        {
            ulong staticGetValue = SingleJumpTarget(ApiBody("il2cpp_field_static_get_value"));
            if (staticGetValue == 0)
                return 0;
            var targets = X64Flow.JumpTargets(_memory, staticGetValue);
            if (targets.Count == 0)
                return 0;
            ulong getValueInternal = targets[^1];

            var internalTargets = X64Flow.JumpTargets(_memory, getValueInternal);
            if (internalTargets.Count == 0)
                return 0;
            ulong candidate = internalTargets[0];

            if (!X64Flow.TryFindBranchTo(_memory, getValueInternal, candidate, out var preceding))
                return 0;
            bool guardedByLiteralCheck = preceding.Any(i =>
                i.Mnemonic == Mnemonic.Test && IsImmediate(i.Op1Kind) && i.GetImmediate(1) == 0x40);
            return guardedByLiteralCheck ? candidate : 0;
        }

        /// <summary>
        /// <c>Class::Init(Il2CppClass*)</c>: first call in il2cpp_class_has_references. Validated by the
        /// early-out on the <c>initialized</c> bit (<c>test byte ptr [rcx+X], 1</c>).
        /// </summary>
        public ulong ClassInit()
        {
            ulong body = ApiBody("il2cpp_class_has_references");
            if (body == 0)
                return 0;
            var targets = X64Flow.JumpTargets(_memory, body);
            if (targets.Count == 0)
                return 0;
            ulong candidate = targets[0];
            bool testsInitializedBit = X64Flow.Decode(_memory, candidate, 6).Any(i =>
                i.Mnemonic == Mnemonic.Test && i.Op0Kind == OpKind.Memory && i.MemoryBase == Register.RCX
                && IsImmediate(i.Op1Kind) && i.GetImmediate(1) == 1);
            return testsInitializedBit ? candidate : 0;
        }

        private static bool IsImmediate(OpKind kind) =>
            kind == OpKind.Immediate8 || kind == OpKind.Immediate8to32 || kind == OpKind.Immediate8to64
            || kind == OpKind.Immediate16 || kind == OpKind.Immediate32 || kind == OpKind.Immediate32to64;

        /// <summary>All resolutions keyed by the Il2CppInterop hook class they replace.</summary>
        public Dictionary<string, ulong> ResolveAll() => new()
        {
            ["GenericMethod_GetMethod_Hook"] = GenericMethodGetMethod(),
            ["MetadataCache_GetTypeInfoFromTypeDefinitionIndex_Hook"] = GetTypeInfoFromTypeDefinitionIndex(),
            ["Class_GetFieldDefaultValue_Hook"] = ClassGetFieldDefaultValue(),
            ["Class_FromIl2CppType_Hook"] = ClassFromIl2CppType(),
            ["Class_FromName_Hook"] = ClassFromName(),
            ["ClassInit"] = ClassInit(),
        };
    }
}
