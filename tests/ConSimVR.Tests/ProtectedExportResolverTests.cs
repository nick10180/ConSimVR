using ConSimVR.Il2CppFix;
using Xunit;

namespace ConSimVR.Tests
{
    /// <summary>
    /// Runs the resolver against the installed GameAssembly.dll (game build 1.0.0.4349). The expected
    /// RVAs were verified by hand from the disassembly; a game update will move them.
    /// </summary>
    public class ProtectedExportResolverTests
    {
        private static ProtectedExportResolver Resolver() => new(new PeFileMemory(TestPaths.GameAssembly));

        [GameFact]
        public void Detects_wrapped_exports()
        {
            Assert.True(Resolver().ExportsAreWrapped);
        }

        [GameFact]
        public void Stock_scan_sees_the_wrapper_and_would_throw_on_Single()
        {
            var memory = new PeFileMemory(TestPaths.GameAssembly);
            var targets = X64Flow.JumpTargets(memory, memory.GetExport("il2cpp_object_get_virtual_method"));
            Assert.Equal(2, targets.Count); // call <stub>; jmp <body> -> Il2CppInterop's .Single() throws
        }

        [GameFact]
        public void Resolves_all_hook_targets()
        {
            var resolved = Resolver().ResolveAll();
            Assert.Equal(0x282B00ul, resolved["GenericMethod_GetMethod_Hook"]);
            Assert.Equal(0x24CEB0ul, resolved["MetadataCache_GetTypeInfoFromTypeDefinitionIndex_Hook"]);
            Assert.Equal(0x247630ul, resolved["Class_GetFieldDefaultValue_Hook"]);
            Assert.Equal(0x2434C0ul, resolved["Class_FromIl2CppType_Hook"]);
            Assert.Equal(0x24E2F0ul, resolved["Class_FromName_Hook"]);
            Assert.Equal(0x246010ul, resolved["ClassInit"]);
        }
    }
}
