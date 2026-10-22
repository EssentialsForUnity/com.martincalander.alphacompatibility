using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MartinCalander.AlphaCompatibility.Editor.Tests
{
    // Run Prepare in one Editor, close it, then run VerifyAndCleanup in a fresh Editor
    // for the same host. A separate process is essential to exercise CoreCLR hashing.
    public static class AsepriteCrossProcessVerification
    {
        private const string Checkpoint = "Library/AlphaCompatibilityAsepriteCrossProcess.json";

        public static string Prepare()
        {
            if (File.Exists(Checkpoint))
                throw new InvalidOperationException("An Aseprite cross-process checkpoint already exists. Verify it before preparing another.");
            var path = AsepriteTileFixture.Create();
            try
            {
                var snapshot = AsepriteTileFixture.Capture(path);
                AssetDatabase.WriteImportSettingsIfDirty(path);
                var json = JsonUtility.ToJson(snapshot, true);
                File.WriteAllText(Checkpoint, json);
                return json;
            }
            catch
            {
                AssetDatabase.DeleteAsset(Path.GetDirectoryName(path));
                throw;
            }
        }

        public static string VerifyAndCleanup()
        {
            var before = JsonUtility.FromJson<AsepriteTileFixture.IdentitySnapshot>(File.ReadAllText(Checkpoint));
            Assert.That(System.Diagnostics.Process.GetCurrentProcess().Id, Is.Not.EqualTo(before.processId),
                "Close the first Editor and reopen this host before verifying.");
            AssetDatabase.ImportAsset(before.path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var after = AsepriteTileFixture.Capture(before.path);
            Assert.That(after.matchingKey, Is.Not.EqualTo(before.matchingKey),
                "The new process must have a different tileset hash to prove this regression.");
            AsepriteTileFixture.AssertSameSprites(before, after);
            var json = JsonUtility.ToJson(after, true);
            AssetDatabase.DeleteAsset(Path.GetDirectoryName(before.path));
            File.Delete(Checkpoint);
            return json;
        }
    }
}
