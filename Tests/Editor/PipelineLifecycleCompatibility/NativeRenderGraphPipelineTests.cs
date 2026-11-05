#if ALPHA_COMPATIBILITY_PIPELINE_0_10 && ALPHA_COMPATIBILITY_SRP_CORE_17_7
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Pipeline.Commands;
using UnityEditor.PackageManager;
using UnityEngine.Assemblies;

namespace MartinCalander.AlphaCompatibility.Editor.Tests
{
    public sealed class NativeRenderGraphPipelineTests
    {
        const string ProviderAssembly = "Unity.RenderPipelines.Core.Editor.CLI";
        const BindingFlags StaticMethods = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        Type m_provider;

        [OneTimeSetUp]
        public void RequireNativeCommandSupport()
        {
            Assert.That(PackageInfo.FindForAssembly(typeof(CommandRegistry).Assembly).version,
                Is.EqualTo("0.10.0-exp.1"));
            m_provider = Type.GetType("UnityEditor.Rendering.CLI.RenderGraphDebugCLI, " + ProviderAssembly,
                throwOnError: true);
            Assert.That(PackageInfo.FindForAssembly(m_provider.Assembly).version, Is.EqualTo("17.7.0"));
            Assert.That(CurrentAssemblies.GetLoadedAssemblies().Any(assembly =>
                assembly.GetName().Name == "MartinCalander.AlphaCompatibility.Editor.Pipeline"), Is.False,
                "Pipeline 0.10 must discover the handlers with the old shim excluded.");
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
            Assert.That(declared.Name, Is.EqualTo(name));
            Assert.That(declared.Group, Is.EqualTo(group));

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
                .Where(method => method.GetCustomAttribute<CliCommandAttribute>()?.Group
                    .StartsWith("render_graph", StringComparison.Ordinal) == true).ToArray();
            Assert.That(legacy, Has.Length.EqualTo(15));
            CommandInfo[] commands = CommandRegistry.DiscoverCommands().ToArray();
            foreach (MethodInfo method in legacy)
                Assert.That(commands.Count(command => command.Method == method), Is.EqualTo(1), method.Name);
        }

    }
}
#endif
