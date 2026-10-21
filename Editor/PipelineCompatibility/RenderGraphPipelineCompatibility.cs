using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Unity.Pipeline.Commands;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

[assembly: InternalsVisibleTo("MartinCalander.AlphaCompatibility.Editor.Tests")]

namespace MartinCalander.AlphaCompatibility.Editor
{
    // SRP Core 17.7 still uses dotted command names that Pipeline 0.9 no longer accepts.
    [NoAutoStaticsCleanup]
    internal static partial class RenderGraphPipelineCompatibility
    {
        internal const string HarmonyId = "com.martincalander.alphacompatibility.render-graph-pipeline";
        const string ProviderType = "UnityEditor.Rendering.CLI.RenderGraphDebugCLI";
        const string ProviderAssembly = "Unity.RenderPipelines.Core.Editor.CLI";
        static MethodInfo s_declarationMethod;
        static MethodInfo s_clearCache;
        static Harmony s_harmony;

        [InitializeOnLoadMethod]
        static void InitializeAfterAssemblyLoad()
        {
            Initialize();
        }

        [OnCodeInitializing]
        internal static void Initialize()
        {
            if (s_harmony != null || AssetDatabase.IsAssetImportWorkerProcess())
                return;

            var registry = typeof(CommandRegistry);
            var provider = Type.GetType(ProviderType + ", " + ProviderAssembly, throwOnError: false)?.Assembly;
            if (provider == null || !IsAffectedVersionPair(
                    PackageInfo.FindForAssembly(registry.Assembly)?.version,
                    PackageInfo.FindForAssembly(provider)?.version))
                return;

            s_declarationMethod = registry.GetMethod("ReadDeclaration", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(MethodInfo), typeof(CliGroupCommandAttribute), typeof(CliCommandAttribute),
                    typeof(string).MakeByRefType(), typeof(string).MakeByRefType() }, null);
            s_clearCache = registry.GetMethod("ClearCache", BindingFlags.Static | BindingFlags.NonPublic);
            if (s_declarationMethod == null || s_declarationMethod.ReturnType != typeof(string) || s_clearCache == null)
                throw new InvalidOperationException("The Pipeline 0.9 command registry no longer matches the Alpha Compatibility fix.");

            var harmony = new Harmony(HarmonyId);
            harmony.Patch(s_declarationMethod, prefix: new HarmonyMethod(typeof(RenderGraphPipelineCompatibility),
                nameof(NormalizeRenderGraphDeclaration)));
            s_harmony = harmony;
            // Rebuild any discovery result cached before this initializer ran.
            s_clearCache.Invoke(null, null);
        }

        internal static bool IsAffectedVersionPair(string pipelineVersion, string srpCoreVersion)
        {
            return pipelineVersion == "0.9.0-exp.1" && srpCoreVersion == "17.7.0";
        }

        static void NormalizeRenderGraphDeclaration(MethodInfo method, CliGroupCommandAttribute grouped,
            ref CliCommandAttribute cliCommand)
        {
            if (grouped != null || cliCommand == null || cliCommand.Group != "global" ||
                method.DeclaringType?.FullName != ProviderType ||
                method.DeclaringType.Assembly.GetName().Name != ProviderAssembly ||
                !cliCommand.Name.StartsWith("render_graph.", StringComparison.Ordinal))
                return;

            int separator = cliCommand.Name.LastIndexOf('.');
            cliCommand = new CliCommandAttribute(cliCommand.Name.Substring(0, separator),
                cliCommand.Name.Substring(separator + 1), cliCommand.Description)
            {
                MainThreadRequired = cliCommand.MainThreadRequired,
                Tags = cliCommand.Tags
            };
            // Pipeline still validates the translated declaration and discovers its original handler.
        }

        [OnCodeUnloading]
        internal static void Cleanup()
        {
            if (s_harmony == null)
                return;

            s_harmony.Unpatch(s_declarationMethod, HarmonyPatchType.Prefix, HarmonyId);
            s_harmony = null;
            s_clearCache.Invoke(null, null);
            s_declarationMethod = null;
            s_clearCache = null;
        }
    }
}
