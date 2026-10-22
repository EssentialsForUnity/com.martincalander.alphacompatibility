using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MartinCalander.AlphaCompatibility.Editor.Tests
{
    internal static class AsepriteTileFixture
    {
        internal const string TileSetName = "AlphaCompatibilityTiles";
        internal const uint TileSetId = 41;

        internal static string Create()
        {
            var folder = "Assets/AlphaCompatibilityAsepriteTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            var path = folder + "/tiles.aseprite";
            File.WriteAllBytes(path, BuildFile());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return path;
        }

        // A self-contained 4x2 tilemap: two opaque 2x2 tiles and one empty tile.
        // The source carries an explicit layer UUID so this test isolates tileset identity.
        private static byte[] BuildFile()
        {
            var layer = Chunk(0x2004, w =>
            {
                w.Write((ushort)3); w.Write((ushort)2); w.Write((ushort)0);
                w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0);
                w.Write((byte)255); w.Write(new byte[3]); WriteString(w, "Tiles");
                w.Write((uint)0);
                w.Write((uint)1); w.Write((uint)2); w.Write((uint)3); w.Write((uint)4);
            });
            var tileset = Chunk(0x2023, w =>
            {
                w.Write(TileSetId); w.Write((uint)6); w.Write((uint)3);
                w.Write((ushort)2); w.Write((ushort)2); w.Write((short)1);
                w.Write(new byte[14]); WriteString(w, TileSetName);
                var pixels = new byte[48];
                for (var i = 4; i < 12; i++)
                {
                    pixels[i * 4 + (i < 8 ? 0 : 1)] = 255;
                    pixels[i * 4 + 3] = 255;
                }
                var compressed = Compress(pixels);
                w.Write((uint)compressed.Length); w.Write(compressed);
            });
            var cel = Chunk(0x2005, w =>
            {
                w.Write((ushort)0); w.Write((short)0); w.Write((short)0);
                w.Write((byte)255); w.Write((ushort)3); w.Write((short)0); w.Write(new byte[5]);
                w.Write((ushort)2); w.Write((ushort)1); w.Write((ushort)32);
                w.Write(0x1FFFFFFFu); w.Write(0x80000000u); w.Write(0x40000000u); w.Write(0x20000000u);
                w.Write(new byte[10]);
                w.Write(Compress(new byte[] { 1, 0, 0, 0, 2, 0, 0, 0 }));
            });
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            var frameSize = 16 + layer.Length + tileset.Length + cel.Length;
            writer.Write((uint)(128 + frameSize)); writer.Write((ushort)0xA5E0);
            writer.Write((ushort)1); writer.Write((ushort)4); writer.Write((ushort)2);
            writer.Write((ushort)32); writer.Write((uint)5); writer.Write((ushort)100);
            writer.Write(new byte[8]); writer.Write((byte)0); writer.Write(new byte[3]);
            writer.Write((ushort)0); writer.Write((byte)1); writer.Write((byte)1);
            writer.Write((short)0); writer.Write((short)0); writer.Write((ushort)2); writer.Write((ushort)2);
            writer.Write(new byte[84]);
            writer.Write((uint)frameSize); writer.Write((ushort)0xF1FA); writer.Write((ushort)3);
            writer.Write((ushort)100); writer.Write((ushort)0); writer.Write((uint)0);
            writer.Write(layer); writer.Write(tileset); writer.Write(cel);
            return stream.ToArray();
        }

        private static byte[] Chunk(ushort kind, Action<BinaryWriter> body)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write((uint)0); writer.Write(kind); body(writer);
            stream.Position = 0; writer.Write((uint)stream.Length);
            return stream.ToArray();
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            writer.Write((ushort)bytes.Length); writer.Write(bytes);
        }

        private static byte[] Compress(byte[] bytes)
        {
            using var stream = new MemoryStream();
            stream.WriteByte(0x78); stream.WriteByte(0x9C);
            using (var deflate = new DeflateStream(stream, System.IO.Compression.CompressionLevel.Optimal, true))
                deflate.Write(bytes, 0, bytes.Length);
            uint a = 1, b = 0;
            foreach (var value in bytes) { a = (a + value) % 65521; b = (b + a) % 65521; }
            var checksum = (b << 16) | a;
            for (var shift = 24; shift >= 0; shift -= 8) stream.WriteByte((byte)(checksum >> shift));
            return stream.ToArray();
        }

        internal static IdentitySnapshot Capture(string path)
        {
            var importer = AssetImporter.GetAtPath(path);
            Assert.That(importer.GetType().FullName, Is.EqualTo("UnityEditor.U2D.Aseprite.AsepriteImporter"));
            using var data = new SerializedObject(importer);
            var sets = data.FindProperty("m_TileSets");
            Assert.That(sets.arraySize, Is.EqualTo(1));
            var set = sets.GetArrayElementAtIndex(0);
            var tiles = set.FindPropertyRelative("m_Tiles");
            Assert.That(tiles.arraySize, Is.EqualTo(2));
            var tileIds = Enumerable.Range(0, tiles.arraySize)
                .Select(i => tiles.GetArrayElementAtIndex(i).FindPropertyRelative("m_SpriteId").stringValue)
                .OrderBy(id => id).ToArray();
            Assert.That(tileIds.All(id => !string.IsNullOrEmpty(id)), Is.True);
            var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().OrderBy(s => s.name).ToArray();
            Assert.That(sprites.Length, Is.EqualTo(2));
            var identities = sprites.Select(sprite =>
            {
                Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string guid, out long localId), Is.True);
                return sprite.name + ":" + guid + ":" + localId;
            }).ToArray();
            return new IdentitySnapshot
            {
                path = path,
                processId = System.Diagnostics.Process.GetCurrentProcess().Id,
                matchingKey = set.FindPropertyRelative("m_Guid").intValue,
                spriteIds = tileIds,
                assetIdentities = identities
            };
        }

        internal static void AssertSameSprites(IdentitySnapshot before, IdentitySnapshot after)
        {
            Assert.That(after.spriteIds, Is.EqualTo(before.spriteIds), "Serialized tile sprite GUIDs changed.");
            Assert.That(after.assetIdentities, Is.EqualTo(before.assetIdentities), "Imported sprite GUID/local file IDs changed.");
        }

        [Serializable]
        internal sealed class IdentitySnapshot
        {
            public string path;
            public int processId;
            public int matchingKey;
            public string[] spriteIds;
            public string[] assetIdentities;
        }
    }
}
