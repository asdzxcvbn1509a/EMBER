using UnityEditor;
using UnityEngine;

namespace Ember.EditorTools
{
    /// <summary>Import rules for the Blender kit: metres, recalculated normals (35 deg), no rigs.</summary>
    public class ModelImport : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(EmberPaths.Models)) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = false;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importBlendShapes = false;
            mi.importNormals = ModelImporterNormals.Calculate;
            mi.normalCalculationMode = ModelImporterNormalCalculationMode.AreaAndAngleWeighted;
            mi.normalSmoothingAngle = 35f;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.isReadable = true;
            mi.addCollider = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
        }

        void OnPreprocessTexture()
        {
            // pre-rendered UI text (team names): crisp, transparent sprite
            if (assetPath.StartsWith(EmberPaths.UI + "/"))
            {
                var ui = (TextureImporter)assetImporter;
                ui.textureType = TextureImporterType.Sprite;
                ui.spriteImportMode = SpriteImportMode.Single;
                ui.alphaIsTransparency = true;
                ui.mipmapEnabled = true;
                ui.wrapMode = TextureWrapMode.Clamp;
                ui.textureCompression = TextureImporterCompression.Uncompressed;
                ui.maxTextureSize = 4096;
                return;
            }
            // class textures are large photos; 2K is plenty for 2 m tiles
            if (assetPath.StartsWith(EmberPaths.ClassS2 + "/Textures") || assetPath.StartsWith(EmberPaths.ClassS3 + "/Textures"))
            {
                var ti = (TextureImporter)assetImporter;
                if (ti.maxTextureSize > 2048) ti.maxTextureSize = 2048;
            }
        }
    }
}
