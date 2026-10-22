using System;
using System.Reflection;
using NUnit.Framework;
using Unity.Scripting.LifecycleManagement;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace MartinCalander.AlphaCompatibility.Editor.Tests
{
    public sealed class RenderingUtilsLifecycleTests
    {
        const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo m_release;
        FieldInfo m_meshField;
        FieldInfo m_materialField;
        PropertyInfo m_meshProperty;
        PropertyInfo m_materialProperty;
        Object m_previousMesh;
        Object m_previousMaterial;
        bool m_isolated;

        [OneTimeSetUp]
        public void FindOptionalSourceFix()
        {
            Type owner = typeof(RenderingUtils);
            Assert.That(PackageInfo.FindForAssembly(owner.Assembly).version, Is.EqualTo("17.7.0"));
            m_release = owner.GetMethod("ReleaseCachedResources", StaticMembers);
            if (m_release == null)
                Assert.Ignore("The optional URP 17.7.0 source fix is not applied. Run Tools~/apply-source-fixes.py with --fix urp-rendering-lifecycle-17.7.0 to verify it.");

            m_meshField = owner.GetField("s_FullscreenMesh", StaticMembers);
            m_materialField = owner.GetField("s_ErrorMaterial", StaticMembers);
            m_meshProperty = owner.GetProperty("fullscreenMesh", StaticMembers);
            m_materialProperty = owner.GetProperty("errorMaterial", StaticMembers);
            Assert.That(m_meshField, Is.Not.Null);
            Assert.That(m_materialField, Is.Not.Null);
            Assert.That(m_meshProperty, Is.Not.Null);
            Assert.That(m_materialProperty, Is.Not.Null);
        }

        [SetUp]
        public void IsolateCachedResources()
        {
            Assert.That(Application.isPlaying, Is.False, "These tests must run in EditMode.");
            m_previousMesh = (Object)m_meshField.GetValue(null);
            m_previousMaterial = (Object)m_materialField.GetValue(null);
            // Keep the Editor's existing resources alive while exercising temporary caches.
            m_isolated = true;
            m_meshField.SetValue(null, null);
            m_materialField.SetValue(null, null);
        }

        [TearDown]
        public void RestoreCachedResources()
        {
            if (!m_isolated)
                return;

            try
            {
                DestroyTemporaryCache(m_meshField, m_previousMesh);
                DestroyTemporaryCache(m_materialField, m_previousMaterial);
            }
            finally
            {
                m_meshField.SetValue(null, m_previousMesh);
                m_materialField.SetValue(null, m_previousMaterial);
                m_isolated = false;
            }
        }

        [TestCase(typeof(OnEnteringPlayModeAttribute))]
        [TestCase(typeof(OnExitingPlayModeAttribute))]
        [TestCase(typeof(OnCodeUnloadingAttribute))]
        public void Lifecycle_ReleasesNativeResourcesAndAllowsRecreation(Type callback)
        {
            var mesh = (Mesh)m_meshProperty.GetValue(null);
            var material = (Material)m_materialProperty.GetValue(null);
            Assert.That(mesh != null, Is.True);
            Assert.That(material != null, Is.True, "URP's fallback error shader must be imported.");

            InvokeLifecycle(callback);
            InvokeLifecycle(callback);

            Assert.That(mesh == null, Is.True, "Release the native mesh before discarding its static reference.");
            Assert.That(material == null, Is.True, "Release the native material before discarding its static reference.");
            Assert.That(m_meshField.GetValue(null), Is.Null);
            Assert.That(m_materialField.GetValue(null), Is.Null);

            var recreatedMesh = (Mesh)m_meshProperty.GetValue(null);
            var recreatedMaterial = (Material)m_materialProperty.GetValue(null);
            Assert.That(recreatedMesh != null, Is.True);
            Assert.That(recreatedMaterial != null, Is.True);
            Assert.That(recreatedMesh, Is.Not.SameAs(mesh));
            Assert.That(recreatedMaterial, Is.Not.SameAs(material));
        }

        [TestCase(typeof(OnEnteringPlayModeAttribute))]
        [TestCase(typeof(OnExitingPlayModeAttribute))]
        [TestCase(typeof(OnCodeUnloadingAttribute))]
        public void Lifecycle_EmptyCachesCanBeReleasedRepeatedly(Type callback)
        {
            InvokeLifecycle(callback);
            InvokeLifecycle(callback);
            Assert.That(m_meshField.GetValue(null), Is.Null);
            Assert.That(m_materialField.GetValue(null), Is.Null);
        }

        void InvokeLifecycle(Type callback)
        {
            Assert.That(m_release.IsDefined(callback, false), Is.True,
                $"RenderingUtils must register {callback.Name}.");
            m_release.Invoke(null, null);
        }

        static void DestroyTemporaryCache(FieldInfo field, Object previous)
        {
            var current = (Object)field.GetValue(null);
            if (current != null && !ReferenceEquals(current, previous))
                Object.DestroyImmediate(current);
        }
    }
}
