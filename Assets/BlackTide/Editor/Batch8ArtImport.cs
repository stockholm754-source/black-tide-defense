using System.IO;
using UnityEditor;
using UnityEngine;

namespace BlackTide.EditorTools
{
    public static class Batch8ArtImport
    {
        [MenuItem("BlackTide/Art/Import Batch 8 UI and VFX")]
        public static void Configure()
        {
            if (EditorApplication.isPlaying) { Debug.LogError("BATCH8_IMPORT FAIL: stop Play first"); return; }
            AssetDatabase.Refresh();
            int ui = 0, vfx = 0;
            foreach (var file in Directory.GetFiles("Assets/BlackTide/Resources/UI/Batch8", "*.png"))
            {
                var importer = AssetImporter.GetAtPath(file.Replace('\\', '/')) as TextureImporter;
                if (!importer) continue;
                importer.textureType = TextureImporterType.Default;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.alphaIsTransparency = true;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport(); ui++;
            }
            foreach (var file in Directory.GetFiles("Assets/BlackTide/Resources/VFX", "*.png"))
            {
                var importer = AssetImporter.GetAtPath(file.Replace('\\', '/')) as TextureImporter;
                if (!importer) continue;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.mipmapEnabled = false;
                importer.wrapMode = Path.GetFileNameWithoutExtension(file) == "slash_noise" ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                importer.SaveAndReimport(); vfx++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"BATCH8_IMPORT RESULT=PASS ui={ui} vfx={vfx}");
        }
    }
}
