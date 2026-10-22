using System;
using UnityEditor;

namespace MartinCalander.AlphaCompatibility.Editor
{
    // Aseprite 7 identifies tile sets with string.GetHashCode(). CoreCLR changes that
    // hash between Editor processes. Match the current hash before import so the
    // importer keeps the existing tiles and their serialized sprite IDs.
    internal sealed class AsepriteTileIdentityPreserver : AssetPostprocessor
    {
        private void OnPreprocessAsset()
        {
            if (assetImporter.GetType().FullName != "UnityEditor.U2D.Aseprite.AsepriteImporter")
                return;

            using var data = new SerializedObject(assetImporter);
            var tileSets = data.FindProperty("m_TileSets");
            if (tileSets == null || !tileSets.isArray)
                throw new InvalidOperationException("Aseprite tile metadata changed; verify sprite identity preservation before importing.");

            for (var i = 0; i < tileSets.arraySize; i++)
            {
                var tileSet = tileSets.GetArrayElementAtIndex(i);
                var name = tileSet.FindPropertyRelative("m_Name");
                var id = tileSet.FindPropertyRelative("m_Id");
                var guid = tileSet.FindPropertyRelative("m_Guid");
                if (name == null || id == null || guid == null)
                    throw new InvalidOperationException("Aseprite tile identity fields changed; import cannot safely preserve existing sprites.");

                // This must match TileSet.GenerateGuid in the installed Aseprite 7 importer.
                guid.intValue = unchecked((name.stringValue.GetHashCode() * 397) ^ ((uint)id.longValue).GetHashCode());
            }

            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
