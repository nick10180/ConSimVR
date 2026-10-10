using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Logging;
using BepInEx.Preloader.Core.Patching;
using HarmonyLib;

namespace ConSimVR.Il2CppFix
{
    /// <summary>
    /// Il2CppInterop finds the libil2cpp functions it detours by walking calls out of il2cpp_* exports.
    /// Construction Simulator's GameAssembly.dll wraps every export as <c>call &lt;stub&gt;; jmp &lt;body&gt;</c>,
    /// which makes those walks throw ("Sequence contains more than one element") or, worse, pick the
    /// wrong function. This patcher resolves the correct addresses up front and feeds them to
    /// Il2CppInterop. On unwrapped builds it does nothing.
    /// </summary>
    [PatcherPluginInfo(Guid, "ConSimVR Il2CppInterop Fix", "1.0.0")]
    public sealed class Il2CppFixPatcher : BasePatcher
    {
        public const string Guid = "com.consimvr.il2cppfix";
        private const string HooksNamespace = "Il2CppInterop.Runtime.Injection.Hooks.";

        private static readonly string[] HookTypes =
        {
            "GenericMethod_GetMethod_Hook",
            "MetadataCache_GetTypeInfoFromTypeDefinitionIndex_Hook",
            "Class_GetFieldDefaultValue_Hook",
            "Class_FromIl2CppType_Hook",
            "Class_FromName_Hook",
        };

        private static ManualLogSource s_log;
        private static Dictionary<string, ulong> s_resolved;

        public override void Initialize()
        {
            s_log = Log;
            try
            {
                Apply();
            }
            catch (Exception e)
            {
                Log.LogError($"Failed to apply Il2CppInterop fix, falling back to stock behaviour: {e}");
            }
        }

        private void Apply()
        {
            ProcessModule gameAssembly = null;
            foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
            {
                if (string.Equals(module.ModuleName, "GameAssembly.dll", StringComparison.OrdinalIgnoreCase))
                    gameAssembly = module;
            }
            if (gameAssembly == null)
            {
                Log.LogWarning("GameAssembly.dll is not loaded yet; skipping.");
                return;
            }

            ulong baseAddress = (ulong)gameAssembly.BaseAddress;
            var memory = new LoadedModuleMemory(NativeLibrary.Load(gameAssembly.FileName), baseAddress, (ulong)gameAssembly.ModuleMemorySize);
            var resolver = new ProtectedExportResolver(memory);
            if (!resolver.ExportsAreWrapped)
            {
                Log.LogInfo("IL2CPP exports are not wrapped; Il2CppInterop's own scanning will be used.");
                return;
            }

            s_resolved = resolver.ResolveAll();
            foreach (var pair in s_resolved)
            {
                if (pair.Value == 0)
                    Log.LogWarning($"{pair.Key}: could not resolve, Il2CppInterop will scan for it itself.");
                else
                    Log.LogInfo($"{pair.Key}: GameAssembly+0x{pair.Value - baseAddress:X}");
            }

            var interop = Assembly.Load("Il2CppInterop.Runtime");
            var harmony = new Harmony(Guid);
            var prefix = new HarmonyMethod(typeof(Il2CppFixPatcher).GetMethod(nameof(FindTargetMethodPrefix), BindingFlags.NonPublic | BindingFlags.Static));
            foreach (string hookName in HookTypes)
            {
                var hookType = interop.GetType(HooksNamespace + hookName);
                var method = hookType?.GetMethod("FindTargetMethod", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (method == null)
                {
                    Log.LogWarning($"{hookName}.FindTargetMethod not found in this Il2CppInterop version.");
                    continue;
                }
                harmony.Patch(method, prefix: prefix);
            }

            var injectorHelpers = interop.GetType("Il2CppInterop.Runtime.Injection.InjectorHelpers");
            var setup = injectorHelpers?.GetMethod("Setup", BindingFlags.NonPublic | BindingFlags.Static);
            if (setup != null)
                harmony.Patch(setup, prefix: new HarmonyMethod(typeof(Il2CppFixPatcher).GetMethod(nameof(SetupPrefix), BindingFlags.NonPublic | BindingFlags.Static)));
        }

        private static bool FindTargetMethodPrefix(object __instance, ref IntPtr __result)
        {
            string key = __instance.GetType().Name;
            if (s_resolved != null && s_resolved.TryGetValue(key, out ulong address) && address != 0)
            {
                __result = (IntPtr)(long)address;
                return false;
            }
            return true;
        }

        /// <summary>Pre-seeds InjectorHelpers.ClassInit so Setup's <c>ClassInit ??= FindClassInit()</c> keeps ours.</summary>
        private static void SetupPrefix(MethodBase __originalMethod)
        {
            if (s_resolved == null || !s_resolved.TryGetValue("ClassInit", out ulong address) || address == 0)
                return;
            var field = __originalMethod.DeclaringType?.GetField("ClassInit", BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null || field.GetValue(null) != null)
                return;
            field.SetValue(null, Marshal.GetDelegateForFunctionPointer((IntPtr)(long)address, field.FieldType));
            s_log?.LogInfo("Class::Init delegate seeded.");
        }
    }
}
