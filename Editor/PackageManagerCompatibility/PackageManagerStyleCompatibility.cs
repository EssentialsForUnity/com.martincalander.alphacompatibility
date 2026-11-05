using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

[assembly: InternalsVisibleTo("MartinCalander.AlphaCompatibility.Editor.PackageManager.Tests")]

namespace MartinCalander.AlphaCompatibility.Editor
{
    [NoAutoStaticsCleanup]
    internal static partial class PackageManagerStyleCompatibility
    {
        internal const string HarmonyId = "com.martincalander.alphacompatibility.package-manager-style";
        const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static Harmony s_harmony;
        static MethodInfo s_getter;

        [InitializeOnLoadMethod]
        static void InitializeAfterAssemblyLoad() => Initialize();

        [OnCodeInitializing]
        internal static void Initialize()
        {
            if (s_harmony != null || Application.unityVersion != "7000.0.0a16" ||
                AssetDatabase.IsAssetImportWorkerProcess())
                return;

            var loader = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.PackageManager.UI.Internal.ResourceLoader");
            s_getter = loader?.GetProperty("packageManagerWindowStyleSheet", Members)?.GetGetMethod(true);
            if (s_getter == null || s_getter.ReturnType != typeof(StyleSheet))
                throw new InvalidOperationException("The alpha 16 Package Manager stylesheet contract has changed.");

            var harmony = new Harmony(HarmonyId);
            harmony.Patch(s_getter, postfix: new HarmonyMethod(typeof(PackageManagerStyleCompatibility), nameof(RepairResult)));
            s_harmony = harmony;
        }

        static void RepairResult(StyleSheet __result) => Repair(__result, Application.unityVersion);

        internal static bool IsAffected(string version, string name, string assetPath, HideFlags flags)
        {
            return version == "7000.0.0a16" && string.IsNullOrEmpty(assetPath) &&
                flags == HideFlags.HideAndDontSave &&
                (name == "PackageManagerWindowDark" || name == "PackageManagerWindowLight");
        }

        internal static int Repair(StyleSheet sheet, string version)
        {
            if (sheet == null || !IsAffected(version, sheet.name, AssetDatabase.GetAssetPath(sheet), sheet.hideFlags))
                return 0;

            var sheetType = typeof(StyleSheet);
            var rules = (Array)sheetType.GetProperty("rules", Members).GetValue(sheet);
            var strings = (string[])sheetType.GetField("strings", Members).GetValue(sheet);
            var floats = (float[])sheetType.GetField("floats", Members).GetValue(sheet);
            var colors = (Color[])sheetType.GetField("colors", Members).GetValue(sheet);
            var properties = new List<object>();
            foreach (var rule in rules)
                foreach (var property in (Array)rule.GetType().GetProperty("properties", Members).GetValue(rule))
                    properties.Add(property);

            object background = null, border = null;
            bool backgroundAmbiguous = false, borderAmbiguous = false;
            bool backgroundAliasExists = false, borderAliasExists = false;
            foreach (var property in properties)
            {
                var type = property.GetType();
                var name = (string)type.GetProperty("name", Members).GetValue(property);
                backgroundAliasExists |= name == "--theme-help-box-background-color";
                borderAliasExists |= name == "--theme-help-box-border-color";
                if (name != "--unity-colors-helpbox-background" && name != "--unity-colors-helpbox-border")
                    continue;
                var values = (Array)type.GetProperty("values", Members).GetValue(property);
                if (values.Length != 1 || Kind(values.GetValue(0)) != "Color" ||
                    Index(values.GetValue(0)) < 0 || Index(values.GetValue(0)) >= colors.Length)
                {
                    if (name == "--unity-colors-helpbox-background") backgroundAmbiguous = true;
                    else borderAmbiguous = true;
                    continue;
                }
                var candidate = values.GetValue(0);
                if (name == "--unity-colors-helpbox-background")
                {
                    backgroundAmbiguous |= background != null && !colors[Index(background)].Equals(colors[Index(candidate)]);
                    background = candidate;
                }
                else
                {
                    borderAmbiguous |= border != null && !colors[Index(border)].Equals(colors[Index(candidate)]);
                    border = candidate;
                }
            }
            if (backgroundAmbiguous || backgroundAliasExists) background = null;
            if (borderAmbiguous || borderAliasExists) border = null;

            int changed = 0;
            foreach (var property in properties)
            {
                var type = property.GetType();
                var name = (string)type.GetProperty("name", Members).GetValue(property);
                var valuesProperty = type.GetProperty("values", Members);
                var values = (Array)valuesProperty.GetValue(property);
                if (values.Length != 3 || Kind(values.GetValue(0)) != "Function" ||
                    Kind(values.GetValue(1)) != "Float" || Kind(values.GetValue(2)) != "Variable")
                    continue;
                var functionType = sheetType.Assembly.GetType("UnityEngine.UIElements.StyleValueFunction");
                if (Index(values.GetValue(0)) != Convert.ToInt32(Enum.Parse(functionType, "Env")))
                    continue;
                int countIndex = Index(values.GetValue(1)), nameIndex = Index(values.GetValue(2));
                if (countIndex < 0 || countIndex >= floats.Length || floats[countIndex] != 1 ||
                    nameIndex < 0 || nameIndex >= strings.Length)
                    continue;
                object replacement = null;
                if (name == "background-color" && strings[nameIndex] == "--theme-help-box-background-color")
                    replacement = background;
                else if (name == "border-color" && strings[nameIndex] == "--theme-help-box-border-color")
                    replacement = border;
                if (replacement == null)
                    continue;

                // Reuse this theme's public color handle instead of the removed legacy alias.
                var resolved = Array.CreateInstance(values.GetType().GetElementType(), 1);
                resolved.SetValue(replacement, 0);
                valuesProperty.SetValue(property, resolved);
                changed++;
            }
            if (changed != 0)
                sheetType.GetMethod("MarkAsChanged", Members).Invoke(sheet, null);
            return changed;
        }

        static string Kind(object handle) => handle.GetType().GetProperty("valueType", Members).GetValue(handle).ToString();
        static int Index(object handle) => (int)handle.GetType().GetField("valueIndex", Members).GetValue(handle);

        [OnCodeUnloading]
        internal static void Cleanup()
        {
            if (s_harmony == null)
                return;
            s_harmony.Unpatch(s_getter, HarmonyPatchType.Postfix, HarmonyId);
            s_harmony = null;
            s_getter = null;
        }
    }
}
