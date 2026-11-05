using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace MartinCalander.AlphaCompatibility.Editor.Tests
{
    public class PackageManagerStyleCompatibilityTests
    {
        const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly Assembly UiAssembly = typeof(StyleSheet).Assembly;
        static readonly Type PropertyType = UiAssembly.GetType("UnityEngine.UIElements.StyleProperty");
        static readonly Type RuleType = UiAssembly.GetType("UnityEngine.UIElements.StyleRule");
        static readonly Type HandleType = UiAssembly.GetType("UnityEngine.UIElements.StyleValueHandle");
        static readonly Type ValueType = UiAssembly.GetType("UnityEngine.UIElements.StyleValueType");
        StyleSheet m_Sheet;
        object m_Background;
        object m_Border;

        [SetUp]
        public void SetUp()
        {
            m_Sheet = ScriptableObject.CreateInstance<StyleSheet>();
            m_Sheet.name = "PackageManagerWindowDark";
            m_Sheet.hideFlags = HideFlags.HideAndDontSave;
            SetField("strings", new[] { "--theme-help-box-background-color", "--theme-help-box-border-color" });
            SetField("floats", new[] { 1f });
            SetField("colors", new[] { new Color(.2f, .3f, .4f, 1), new Color(.7f, .6f, .5f, 1) });
            m_Background = Property("background-color", Env(0));
            m_Border = Property("border-color", Env(1));
            SetRules(
                Property("--unity-colors-helpbox-background", Handles(Handle("Color", 0))),
                Property("--unity-colors-helpbox-border", Handles(Handle("Color", 1))),
                m_Background, m_Border);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(m_Sheet);

        [TestCase("PackageManagerWindowDark")]
        [TestCase("PackageManagerWindowLight")]
        public void ResolvesLegacyAliasesUsingTheSheetsOwnColors(string name)
        {
            m_Sheet.name = name;
            var colors = typeof(StyleSheet).GetField("colors", Members).GetValue(m_Sheet);
            var strings = typeof(StyleSheet).GetField("strings", Members).GetValue(m_Sheet);
            var rules = typeof(StyleSheet).GetField("m_Rules", Members).GetValue(m_Sheet);
            Assert.That(PackageManagerStyleCompatibility.Repair(m_Sheet, "7000.0.0a16"), Is.EqualTo(2));
            Assert.That(Values(m_Background).Length, Is.EqualTo(1));
            Assert.That(Values(m_Border).Length, Is.EqualTo(1));
            Assert.That(Values(m_Background).GetValue(0), Is.EqualTo(Handle("Color", 0)));
            Assert.That(Values(m_Border).GetValue(0), Is.EqualTo(Handle("Color", 1)));
            Assert.That(typeof(StyleSheet).GetField("colors", Members).GetValue(m_Sheet), Is.SameAs(colors));
            Assert.That(typeof(StyleSheet).GetField("strings", Members).GetValue(m_Sheet), Is.SameAs(strings));
            Assert.That(typeof(StyleSheet).GetField("m_Rules", Members).GetValue(m_Sheet), Is.SameAs(rules));
            Assert.That(PackageManagerStyleCompatibility.Repair(m_Sheet, "7000.0.0a16"), Is.Zero);
        }

        [TestCase("7000.0.0a12", "PackageManagerWindowDark", "", HideFlags.HideAndDontSave)]
        [TestCase("7000.0.0a17", "PackageManagerWindowDark", "", HideFlags.HideAndDontSave)]
        [TestCase("7000.0.0a160", "PackageManagerWindowDark", "", HideFlags.HideAndDontSave)]
        [TestCase("7000.0.0a16", "AuthoredSheet", "", HideFlags.HideAndDontSave)]
        [TestCase("7000.0.0a16", "PackageManagerWindowDark", "Assets/Window.uss", HideFlags.HideAndDontSave)]
        [TestCase("7000.0.0a16", "PackageManagerWindowDark", "", HideFlags.None)]
        public void RejectsOtherVersionsAndAuthoredSheets(string version, string name, string path, HideFlags flags)
        {
            Assert.That(PackageManagerStyleCompatibility.IsAffected(version, name, path, flags), Is.False);
        }

        [Test]
        public void UnsupportedVersionDoesNotMutateValues()
        {
            var before = EditorJsonUtility.ToJson(m_Sheet);
            Assert.That(PackageManagerStyleCompatibility.Repair(m_Sheet, "7000.0.0a17"), Is.Zero);
            Assert.That(EditorJsonUtility.ToJson(m_Sheet), Is.EqualTo(before));
        }

        [Test]
        public void MissingNativeColorsLeaveTheOriginalErrorVisible()
        {
            SetRules(m_Background, m_Border);
            var before = EditorJsonUtility.ToJson(m_Sheet);
            Assert.That(PackageManagerStyleCompatibility.Repair(m_Sheet, "7000.0.0a16"), Is.Zero);
            Assert.That(EditorJsonUtility.ToJson(m_Sheet), Is.EqualTo(before));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void AmbiguousColorsOrAnExistingLegacyDefinitionAreNotReplaced(bool ambiguous)
        {
            SetRules(
                Property("--unity-colors-helpbox-background", Handles(Handle("Color", 0))),
                Property(ambiguous ? "--unity-colors-helpbox-background" : "--theme-help-box-background-color", Handles(Handle("Color", 1))),
                m_Background);
            var before = EditorJsonUtility.ToJson(m_Sheet);
            Assert.That(PackageManagerStyleCompatibility.Repair(m_Sheet, "7000.0.0a16"), Is.Zero);
            Assert.That(EditorJsonUtility.ToJson(m_Sheet), Is.EqualTo(before));
        }

        [Test]
        public void AnUnsupportedDuplicateColorDefinitionIsNotGuessed()
        {
            SetRules(
                Property("--unity-colors-helpbox-background", Handles(Handle("Color", 0))),
                Property("--unity-colors-helpbox-background", Env(0)), m_Background);
            var before = EditorJsonUtility.ToJson(m_Sheet);
            Assert.That(PackageManagerStyleCompatibility.Repair(m_Sheet, "7000.0.0a16"), Is.Zero);
            Assert.That(EditorJsonUtility.ToJson(m_Sheet), Is.EqualTo(before));
        }

        [TestCase("unknown-name")]
        [TestCase("var-function")]
        [TestCase("wrong-property")]
        [TestCase("fallback")]
        [TestCase("invalid-index")]
        [TestCase("argument-count")]
        public void LeavesOtherExpressionsUntouched(string shape)
        {
            var values = Env(0);
            if (shape == "unknown-name") SetField("strings", new[] { "--other-background", "--other-border" });
            if (shape == "var-function") values.SetValue(Handle("Function", Function("Var")), 0);
            if (shape == "wrong-property") PropertyType.GetProperty("name", Members).SetValue(m_Background, "color");
            if (shape == "fallback") values = Handles(Handle("Function", Function("Env")), Handle("Float", 0), Handle("Variable", 0), Handle("Color", 1));
            if (shape == "invalid-index") values.SetValue(Handle("Variable", 1000), 2);
            if (shape == "argument-count") SetField("floats", new[] { 2f });
            PropertyType.GetProperty("values", Members).SetValue(m_Background, values);
            SetRules(Property("--unity-colors-helpbox-background", Handles(Handle("Color", 0))), m_Background);
            var before = EditorJsonUtility.ToJson(m_Sheet);
            Assert.That(PackageManagerStyleCompatibility.Repair(m_Sheet, "7000.0.0a16"), Is.Zero);
            Assert.That(EditorJsonUtility.ToJson(m_Sheet), Is.EqualTo(before));
        }

        [Test]
        public void InitializationAndCleanupOwnOnlyOnePostfix()
        {
            if (Application.unityVersion != "7000.0.0a16")
                Assert.Ignore("The native Package Manager hook is restricted to alpha 16.");
            var getter = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.PackageManager.UI.Internal.ResourceLoader")
                .GetProperty("packageManagerWindowStyleSheet", Members).GetGetMethod(true);
            try
            {
                PackageManagerStyleCompatibility.Initialize();
                PackageManagerStyleCompatibility.Initialize();
                Assert.That(Harmony.GetPatchInfo(getter).Postfixes.Count(p => p.owner == PackageManagerStyleCompatibility.HarmonyId), Is.EqualTo(1));
                PackageManagerStyleCompatibility.Cleanup();
                PackageManagerStyleCompatibility.Cleanup();
                Assert.That(Harmony.GetPatchInfo(getter)?.Postfixes.Any(p => p.owner == PackageManagerStyleCompatibility.HarmonyId) ?? false, Is.False);
            }
            finally
            {
                PackageManagerStyleCompatibility.Initialize();
            }
        }

        void SetField(string name, object value) => typeof(StyleSheet).GetField(name, Members).SetValue(m_Sheet, value);

        void SetRules(params object[] properties)
        {
            var rule = Activator.CreateInstance(RuleType, Members, null, new object[] { m_Sheet }, null);
            var array = Array.CreateInstance(PropertyType, properties.Length);
            for (int i = 0; i < properties.Length; i++) array.SetValue(properties[i], i);
            RuleType.GetMethod("SetProperties", Members).Invoke(rule, new object[] { array });
            var rules = Array.CreateInstance(RuleType, 1);
            rules.SetValue(rule, 0);
            SetField("m_Rules", rules);
        }

        static object Property(string name, Array values)
        {
            var property = Activator.CreateInstance(PropertyType, true);
            PropertyType.GetProperty("name", Members).SetValue(property, name);
            PropertyType.GetProperty("values", Members).SetValue(property, values);
            return property;
        }

        static Array Values(object property) => (Array)PropertyType.GetProperty("values", Members).GetValue(property);
        static int Function(string name) => Convert.ToInt32(Enum.Parse(UiAssembly.GetType("UnityEngine.UIElements.StyleValueFunction"), name));
        static object Handle(string kind, int index) => Activator.CreateInstance(HandleType, Members, null, new[] { (object)index, Enum.Parse(ValueType, kind) }, null);
        static Array Env(int index) => Handles(Handle("Function", Function("Env")), Handle("Float", 0), Handle("Variable", index));
        static Array Handles(params object[] values)
        {
            var result = Array.CreateInstance(HandleType, values.Length);
            for (int i = 0; i < values.Length; i++) result.SetValue(values[i], i);
            return result;
        }
    }
}
