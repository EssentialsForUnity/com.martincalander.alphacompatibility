using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Assemblies;

namespace MartinCalander.AlphaCompatibility.Editor.Tests
{
    public sealed class PipelineLifecycleTests
    {
        const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const string TargetId = "PipelineLifecycleTests.Target.Value";
        const string JournalKey = "Pipeline.MethodReloadWatch.AppliedFiles";

        [OneTimeSetUp]
        public void RequireExplicitSourceFix()
        {
            var registry = FindType("Unity.Pipeline.MethodReload.MethodReloadRegistry");
            var startup = FindType("Unity.Pipeline.Editor.PipelineServerStartup");
            if (registry.GetMethod("ResetSessionState", StaticMembers) == null ||
                startup.GetNestedType("LifecycleCleanup", BindingFlags.NonPublic) == null)
                Assert.Ignore("Apply the optional source fix before running these tests: " +
                    "python3 Tools~/apply-source-fixes.py --project <project> --apply " +
                    "--fix pipeline-lifecycle-0.9.0-exp.1");
        }

        [TestCase(typeof(OnEnteringPlayModeAttribute))]
        [TestCase(typeof(OnExitingPlayModeAttribute))]
        public void SessionCleanupReleasesOverridesAndAllowsRegistrationAgain(Type callback)
        {
            var registry = FindType("Unity.Pipeline.MethodReload.MethodReloadRegistry");
            var driver = FindType("Unity.Pipeline.RuntimePipelineDriver");
            using var registryState = new StaticState(registry,
                "m_MethodOverrides", "m_ReloadableMethods", "m_LoadedMethodReloadTypes",
                "s_FirstReloadNotes", "s_FirstReloadNotesFlushed", "OverridesChanged",
                "<Dispatcher>k__BackingField", "<AllowedReloadRoots>k__BackingField");
            using var driverState = new StaticState(driver, "s_Discovered");
            var reset = registry.GetMethods(StaticMembers).Single(method => method.IsDefined(callback, false));
            reset.Invoke(null, null);

            for (int session = 0; session < 2; session++)
            {
                RegisterOverride(registry);
                object[] arguments = { TargetId, new Target(), null, null };
                Assert.That(registry.GetMethod("TryInvokeMethodReloadResult").Invoke(null, arguments), Is.True);
                Assert.That(arguments[3], Is.EqualTo(2));

                var dispatcher = registry.GetProperty("Dispatcher");
                dispatcher.SetValue(null, Activator.CreateInstance(dispatcher.PropertyType));
                registry.GetProperty("AllowedReloadRoots").SetValue(null, new[] { "/previous-session" });
                registry.GetField("OverridesChanged", StaticMembers).SetValue(null, (Action)(() => { }));
                driver.GetField("s_Discovered", StaticMembers).SetValue(null, true);
                ((IList)registry.GetField("s_FirstReloadNotes", StaticMembers).GetValue(null)).Add("Previous session");

                reset.Invoke(null, null);
                reset.Invoke(null, null);

                Assert.That(registry.GetMethod("HasOverride").Invoke(null, new object[] { TargetId }), Is.False);
                Assert.That(registry.GetMethod("TryInvokeMethodReloadResult").Invoke(null, arguments), Is.False);
                foreach (var name in new[] { "m_MethodOverrides", "m_ReloadableMethods", "m_LoadedMethodReloadTypes" })
                    Assert.That(((IDictionary)registry.GetField(name, StaticMembers).GetValue(null)).Count, Is.Zero, name);
                Assert.That(dispatcher.GetValue(null), Is.Null);
                Assert.That(registry.GetProperty("AllowedReloadRoots").GetValue(null), Is.Null);
                Assert.That(registry.GetField("OverridesChanged", StaticMembers).GetValue(null), Is.Null);
                Assert.That(driver.GetField("s_Discovered", StaticMembers).GetValue(null), Is.False);
                Assert.That(registry.GetField("s_FirstReloadNotesFlushed", StaticMembers).GetValue(null), Is.False);
                Assert.That(((IList)registry.GetField("s_FirstReloadNotes", StaticMembers).GetValue(null)).Count, Is.Zero);
            }
        }

        [TestCase(PlayModeStateChange.EnteredPlayMode)]
        [TestCase(PlayModeStateChange.EnteredEditMode)]
        public void ActiveWatcherRequeuesExistingJournalFilesAfterSessionCleanup(PlayModeStateChange state)
        {
            var watcher = FindType("Unity.Pipeline.Editor.EditorMethodReloadWatcher");
            using var watcherState = new StaticState(watcher, "s_Watching", "s_Pending");
            var previousJournal = SessionState.GetString(JournalKey, "");
            var previousEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            var previousOptions = EditorSettings.enterPlayModeOptions;
            var path = Path.GetTempFileName();
            var pending = (IDictionary)watcher.GetField("s_Pending", StaticMembers).GetValue(null);
            var watching = watcher.GetField("s_Watching", StaticMembers);
            var transition = watcher.GetMethod("OnPlayModeStateChanged", StaticMembers);
            try
            {
                pending.Clear();
                SessionState.SetString(JournalKey, path + "\n" + path + ".missing\n" + path);
                EditorSettings.enterPlayModeOptionsEnabled = true;
                EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
                watching.SetValue(null, false);
                transition.Invoke(null, new object[] { state });
                Assert.That(pending.Count, Is.Zero, "An inactive watch must not resume itself.");

                watching.SetValue(null, true);
                EditorSettings.enterPlayModeOptionsEnabled = false;
                transition.Invoke(null, new object[] { state });
                Assert.That(pending.Count, Is.Zero, "Domain reload already has its own watch resume path.");

                EditorSettings.enterPlayModeOptionsEnabled = true;
                transition.Invoke(null, new object[] { PlayModeStateChange.ExitingEditMode });
                Assert.That(pending.Count, Is.Zero, "Reapply must wait until session cleanup has completed.");

                transition.Invoke(null, new object[] { state });
                transition.Invoke(null, new object[] { state });
                Assert.That(pending.Count, Is.EqualTo(1));
                Assert.That(pending.Contains(path), Is.True);

                var afterCompile = (IList)watcher.GetMethod("ComputeResumeReapplies", StaticMembers)
                    .Invoke(null, new object[] { false, new[] { path } });
                Assert.That(afterCompile.Count, Is.Zero, "A real Edit Mode recompile must not replay old overrides.");
            }
            finally
            {
                SessionState.SetString(JournalKey, previousJournal);
                EditorSettings.enterPlayModeOptionsEnabled = previousEnabled;
                EditorSettings.enterPlayModeOptions = previousOptions;
                File.Delete(path);
            }
        }

        [TestCase("Unity.Pipeline.CommandLineBinder", "k_NoParameters")]
        [TestCase("IlInterpreter.Interpreter.ScriptInterpreter+ScriptDelegateAdapter", "s_NoArgs")]
        [TestCase("IlInterpreter.Interpreter.ScriptInterpreter+Vm", "T_byteA")]
        public void ImmutableMetadataSurvivesAutomaticStaticsCleanup(string owner, string fieldName)
        {
            var field = FindType(owner).GetField(fieldName, StaticMembers);
            Assert.That(field, Is.Not.Null);
            Assert.That(field.GetCustomAttributes(false).Any(attribute =>
                attribute.GetType().Name == "NoAutoStaticsCleanupAttribute"), Is.True);
            Assert.That(field.GetValue(null), Is.Not.Null);
        }

        [Test]
        public void EditorOwnersRegisterCodeUnloadCleanup()
        {
            var startup = FindType("Unity.Pipeline.Editor.PipelineServerStartup");
            var cleanup = startup.GetNestedType("LifecycleCleanup", BindingFlags.NonPublic);
            Assert.That(cleanup, Is.Not.Null);
            var unload = cleanup.GetMethod("Unload", StaticMembers);
            Assert.That(unload.GetCustomAttributes(false).Any(attribute =>
                attribute.GetType().Name == "OnCodeUnloadingAttribute"), Is.True);
            var watcher = FindType("Unity.Pipeline.Editor.EditorMethodReloadWatcher");
            var dispose = watcher.GetMethod("OnBeforeAssemblyReload", StaticMembers);
            Assert.That(dispose, Is.Not.Null);
            Assert.That(dispose.GetCustomAttributes(false).Any(attribute =>
                attribute.GetType().Name == "OnCodeUnloadingAttribute"), Is.True);
        }

        static void RegisterOverride(Type registry)
        {
            var attribute = Activator.CreateInstance(FindType("Unity.Pipeline.MethodReload.MethodReloadAttribute"));
            attribute.GetType().GetProperty("Id").SetValue(attribute, TargetId);
            registry.GetMethod("RegisterReloadableMethod").Invoke(null,
                new[] { typeof(Target).GetMethod(nameof(Target.Value)), attribute });
            registry.GetMethod("RegisterMethodReloadType").Invoke(null,
                new object[] { typeof(Target), "LifecycleTest" });
            var replacement = Activator.CreateInstance(
                FindType("Unity.Pipeline.MethodReload.MethodReloadOverrideMethodAttribute"), TargetId);
            var register = registry.GetMethods(StaticMembers)
                .Single(method => method.Name == "RegisterMethodOverride" && method.GetParameters().Length == 3);
            Assert.That(register.Invoke(null, new[]
            {
                typeof(PipelineLifecycleTests).GetMethod(nameof(ReloadedValue), StaticMembers),
                replacement, typeof(PipelineLifecycleTests)
            }), Is.True);
        }

        sealed class Target
        {
            public int Value() => 1;
        }

        static int ReloadedValue(Target target) => 2;

        static Type FindType(string name)
        {
            foreach (var assembly in CurrentAssemblies.GetLoadedAssemblies())
            {
                var type = assembly.GetType(name);
                if (type != null) return type;
            }
            Assert.Fail("Missing loaded type: " + name);
            return null;
        }

        // Tests borrow the live editor registry briefly and restore it before another editor tick.
        // They never stop the server, start a file watcher, or apply a source file.
        sealed class StaticState : IDisposable
        {
            readonly List<Action> m_Restore = new();

            public StaticState(Type owner, params string[] fields)
            {
                foreach (var name in fields)
                {
                    var field = owner.GetField(name, StaticMembers);
                    Assert.That(field, Is.Not.Null, name);
                    var value = field.GetValue(null);
                    if (value is IDictionary dictionary)
                    {
                        var entries = new List<DictionaryEntry>();
                        foreach (DictionaryEntry entry in dictionary) entries.Add(entry);
                        m_Restore.Add(() =>
                        {
                            dictionary.Clear();
                            foreach (var entry in entries) dictionary.Add(entry.Key, entry.Value);
                        });
                    }
                    else if (value is IList list && field.IsInitOnly)
                    {
                        var entries = new object[list.Count];
                        list.CopyTo(entries, 0);
                        m_Restore.Add(() =>
                        {
                            list.Clear();
                            foreach (var entry in entries) list.Add(entry);
                        });
                    }
                    else
                    {
                        m_Restore.Add(() => field.SetValue(null, value));
                    }
                }
            }

            public void Dispose()
            {
                foreach (var restore in m_Restore) restore();
            }
        }
    }
}
