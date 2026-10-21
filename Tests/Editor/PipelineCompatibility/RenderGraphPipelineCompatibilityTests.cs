using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using NUnit.Framework;
using Unity.Pipeline.Commands;
using UnityEditor.PackageManager;

namespace MartinCalander.AlphaCompatibility.Editor.Tests
{
    public sealed class RenderGraphPipelineCompatibilityTests
    {
        const string ProviderName = "UnityEditor.Rendering.CLI.RenderGraphDebugCLI";
        const string ProviderAssembly = "Unity.RenderPipelines.Core.Editor.CLI";
        const string OtherOwner = "MartinCalander.AlphaCompatibility.Tests.OtherOwner";
        const BindingFlags StaticMethods = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo m_readDeclaration;
        Type m_provider;

        [OneTimeSetUp]
        public void FindAffectedRegistryAndProvider()
        {
            Assert.That(PackageInfo.FindForAssembly(typeof(CommandRegistry).Assembly).version,
                Is.EqualTo("0.9.0-exp.1"));
            m_provider = Type.GetType(ProviderName + ", " + ProviderAssembly, throwOnError: true);
            Assert.That(PackageInfo.FindForAssembly(m_provider.Assembly).version, Is.EqualTo("17.7.0"));
            m_readDeclaration = typeof(CommandRegistry).GetMethod("ReadDeclaration", StaticMethods);
            Assert.That(m_readDeclaration, Is.Not.Null);
            Assert.That(OwnedPrefixCount(RenderGraphPipelineCompatibility.HarmonyId), Is.EqualTo(1),
                "The compatibility patch must initialize automatically before any tests run.");
        }

        [TestCase("Capture", "render_graph", "capture")]
        [TestCase("Resume", "render_graph", "resume")]
        [TestCase("GetGraphs", "render_graph", "graphs")]
        [TestCase("GetExecutions", "render_graph", "executions")]
        [TestCase("GetPasses", "render_graph", "passes")]
        [TestCase("GetPass", "render_graph", "pass")]
        [TestCase("GetCulledPasses", "render_graph.passes", "culled")]
        [TestCase("GetUnmergedPasses", "render_graph.passes", "unmerged")]
        [TestCase("GetPassesByType", "render_graph.passes", "by_type")]
        [TestCase("GetExecutionCaptureInfo", "render_graph.execution", "info")]
        [TestCase("GetMetrics", "render_graph", "metrics")]
        [TestCase("GetResources", "render_graph", "resources")]
        [TestCase("GetResource", "render_graph", "resource")]
        [TestCase("GetPassesUsingResource", "render_graph.resource", "passes")]
        [TestCase("GetResourceProducerPass", "render_graph.resource", "producer")]
        public void Discovery_PreservesRenderGraphHandlersAndMetadata(string methodName, string group, string name)
        {
            MethodInfo method = m_provider.GetMethod(methodName, StaticMethods);
            Assert.That(method, Is.Not.Null);
            CliCommandAttribute declared = method.GetCustomAttribute<CliCommandAttribute>();
            Assert.That(declared.Name, Is.EqualTo(group + "." + name));
            Assert.That(declared.Group, Is.EqualTo(CommandInfo.GlobalGroup));

            CommandInfo[] matches = CommandRegistry.DiscoverCommands()
                .Where(command => command.FullName == group + " " + name).ToArray();
            Assert.That(matches, Has.Length.EqualTo(1));
            CommandInfo command = matches[0];
            Assert.That(command.Method, Is.EqualTo(method));
            Assert.That(command.Group, Is.EqualTo(group));
            Assert.That(command.Name, Is.EqualTo(name));
            Assert.That(command.Package, Is.EqualTo(ProviderAssembly));
            Assert.That(command.Description, Is.EqualTo(declared.Description));
            Assert.That(command.MainThreadRequired, Is.EqualTo(declared.MainThreadRequired));
            Assert.That(command.Tags, Is.EqualTo(declared.Tags));

            ParameterInfo[] parameters = method.GetParameters();
            Assert.That(command.Parameters.Count, Is.EqualTo(parameters.Length));
            for (int i = 0; i < parameters.Length; i++)
            {
                ParameterInfo parameter = parameters[i];
                CliArgAttribute argument = parameter.GetCustomAttribute<CliArgAttribute>();
                CommandParameterInfo actual = command.Parameters[i];
                Assert.That(actual.Name, Is.EqualTo(argument?.Name ?? parameter.Name));
                Assert.That(actual.ParameterType, Is.EqualTo(parameter.ParameterType));
                Assert.That(actual.Required, Is.EqualTo(argument?.Required ?? !parameter.HasDefaultValue));
                Assert.That(actual.DefaultValue, Is.EqualTo(parameter.HasDefaultValue
                    ? parameter.DefaultValue : argument?.DefaultValue));
                Assert.That(actual.Description, Is.EqualTo(argument?.Description ?? "Parameter: " + parameter.Name));
            }
        }

        [Test]
        public void Discovery_ContainsAllLegacyRenderGraphCommands()
        {
            MethodInfo[] legacy = m_provider.GetMethods(StaticMethods)
                .Where(method => method.GetCustomAttribute<CliCommandAttribute>()?.Name
                    .StartsWith("render_graph.", StringComparison.Ordinal) == true).ToArray();
            Assert.That(legacy, Has.Length.EqualTo(15));
            CommandInfo[] commands = CommandRegistry.DiscoverCommands().ToArray();
            foreach (MethodInfo method in legacy)
                Assert.That(commands.Count(command => command.Method == method), Is.EqualTo(1), method.Name);
        }

        [Test]
        public void Declaration_UnknownProviderStillRejectsDottedNames()
        {
            MethodInfo method = typeof(RenderGraphPipelineCompatibilityTests).GetMethod(nameof(UnannotatedHandler), StaticMethods);
            var declaration = new CliCommandAttribute(CommandInfo.GlobalGroup, "render_graph.graphs", "Synthetic declaration");
            string error = ReadDeclaration(method, null, declaration, out string group, out string name);
            Assert.That(error, Does.Contain("contains a '.'"));
            Assert.That(group, Is.EqualTo(CommandInfo.GlobalGroup));
            Assert.That(name, Is.EqualTo("render_graph.graphs"));
        }

        [TestCase("render_graph.")]
        [TestCase("render_graph..graphs")]
        [TestCase("render_graph.bad name")]
        [TestCase("other.graphs")]
        public void Declaration_ProviderDoesNotBypassInvalidNameValidation(string name)
        {
            var declaration = new CliCommandAttribute(CommandInfo.GlobalGroup, name, "Synthetic declaration");
            string error = ReadDeclaration(m_provider.GetMethod("GetGraphs", StaticMethods), null,
                declaration, out _, out _);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void Declaration_AlreadyGroupedAttributesKeepTheirNames()
        {
            var declaration = new CliCommandAttribute("custom.render_graph", "graphs", "Grouped declaration");
            string error = ReadDeclaration(m_provider.GetMethod("GetGraphs", StaticMethods), null,
                declaration, out string group, out string name);
            Assert.That(error, Is.Null);
            Assert.That(group, Is.EqualTo("custom.render_graph"));
            Assert.That(name, Is.EqualTo("graphs"));
        }

        [Test]
        public void Declaration_DuplicateAttributeKindsRemainRejected()
        {
            var grouped = new CliGroupCommandAttribute("render_graph", "Grouped declaration");
            var declaration = new CliCommandAttribute(CommandInfo.GlobalGroup, "render_graph.graphs", "Legacy declaration");
            string error = ReadDeclaration(m_provider.GetMethod("GetGraphs", StaticMethods), grouped,
                declaration, out _, out _);
            Assert.That(error, Does.Contain("both [CliCommand] and [CliGroupCommand]"));
        }

        [Test]
        public void InitializeAndCleanup_KeepOneOwnedPrefixAndPreserveOtherOwners()
        {
            var other = new Harmony(OtherOwner);
            MethodInfo sentinel = typeof(RenderGraphPipelineCompatibilityTests).GetMethod(nameof(OtherOwnerPrefix), StaticMethods);
            try
            {
                RenderGraphPipelineCompatibility.Initialize();
                RenderGraphPipelineCompatibility.Initialize();
                Assert.That(OwnedPrefixCount(RenderGraphPipelineCompatibility.HarmonyId), Is.EqualTo(1));
                other.Patch(m_readDeclaration, prefix: new HarmonyMethod(sentinel));
                Assert.That(OwnedPrefixCount(OtherOwner), Is.EqualTo(1));

                RenderGraphPipelineCompatibility.Cleanup();
                Assert.That(OwnedPrefixCount(RenderGraphPipelineCompatibility.HarmonyId), Is.Zero);
                Assert.That(OwnedPrefixCount(OtherOwner), Is.EqualTo(1));
            }
            finally
            {
                other.Unpatch(m_readDeclaration, HarmonyPatchType.Prefix, OtherOwner);
                RenderGraphPipelineCompatibility.Initialize();
            }

            Assert.That(OwnedPrefixCount(RenderGraphPipelineCompatibility.HarmonyId), Is.EqualTo(1));
            Assert.That(CommandRegistry.DiscoverCommands().Count(command => command.FullName == "render_graph graphs"),
                Is.EqualTo(1));
        }

        int OwnedPrefixCount(string owner)
        {
            return Harmony.GetPatchInfo(m_readDeclaration)?.Prefixes.Count(patch => patch.owner == owner) ?? 0;
        }

        string ReadDeclaration(MethodInfo method, CliGroupCommandAttribute grouped, CliCommandAttribute command,
            out string group, out string name)
        {
            object[] arguments = { method, grouped, command, null, null };
            string error = (string)m_readDeclaration.Invoke(null, arguments);
            group = (string)arguments[3];
            name = (string)arguments[4];
            return error;
        }

        static void UnannotatedHandler() { }
        static void OtherOwnerPrefix() { }
    }

    public sealed class RenderGraphPipelineCompatibilityVersionTests
    {
        [TestCase("0.9.0-exp.1", "17.7.0", true)]
        [TestCase("0.8.0-exp.1", "17.7.0", false)]
        [TestCase("0.9.0-exp.2", "17.7.0", false)]
        [TestCase("0.9.0", "17.7.0", false)]
        [TestCase("0.10.0-exp.1", "17.7.0", false)]
        [TestCase("0.9.0-exp.1", "17.6.0", false)]
        [TestCase("0.9.0-exp.1", "17.7.1", false)]
        [TestCase("0.9.0-exp.1", "17.7.0-exp.1", false)]
        [TestCase(null, "17.7.0", false)]
        [TestCase("0.9.0-exp.1", null, false)]
        [TestCase("", "17.7.0", false)]
        [TestCase("0.9.0-exp.1", "", false)]
        public void VersionGate_RequiresTheAffectedPackageVersions(string pipelineVersion, string srpCoreVersion,
            bool expected)
        {
            Assert.That(RenderGraphPipelineCompatibility.IsAffectedVersionPair(pipelineVersion, srpCoreVersion),
                Is.EqualTo(expected));
        }
    }
}
