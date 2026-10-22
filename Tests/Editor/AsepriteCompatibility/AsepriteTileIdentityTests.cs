using System.IO;
using NUnit.Framework;
using UnityEditor;

namespace MartinCalander.AlphaCompatibility.Editor.Tests
{
    public sealed class AsepriteTileIdentityTests
    {
        private string m_Path;

        [SetUp]
        public void SetUp() => m_Path = AsepriteTileFixture.Create();

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(m_Path)) AssetDatabase.DeleteAsset(Path.GetDirectoryName(m_Path));
        }

        [Test]
        public void StaleMatchingKeyDoesNotReplaceExistingSpriteIdentities()
        {
            var before = AsepriteTileFixture.Capture(m_Path);
            using (var data = new SerializedObject(AssetImporter.GetAtPath(m_Path)))
            {
                var key = data.FindProperty("m_TileSets").GetArrayElementAtIndex(0).FindPropertyRelative("m_Guid");
                key.intValue = unchecked(before.matchingKey + 1);
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.WriteImportSettingsIfDirty(m_Path);
            AssetDatabase.ImportAsset(m_Path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var after = AsepriteTileFixture.Capture(m_Path);
            Assert.That(after.matchingKey, Is.EqualTo(before.matchingKey));
            AsepriteTileFixture.AssertSameSprites(before, after);
        }

        [Test]
        public void RepeatedImportsPreserveSpriteIdentities()
        {
            var before = AsepriteTileFixture.Capture(m_Path);
            for (var i = 0; i < 2; i++)
            {
                AssetDatabase.ImportAsset(m_Path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                AsepriteTileFixture.AssertSameSprites(before, AsepriteTileFixture.Capture(m_Path));
            }
        }
    }
}
