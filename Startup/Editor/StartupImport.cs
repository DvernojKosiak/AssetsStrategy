using UnityEditor;
using UnityEngine;

/// <summary>Настройки импорта для картинок заставки (Resources/Startup): без уменьшения до 1024, прозрачные края не темнеют.
/// Фон деревни сжимается (он большой), логотипы и название - без сжатия, чтобы края букв были чистыми.</summary>
public class StartupImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        string path = assetPath.Replace('\\', '/');
        if (!path.Contains("/Resources/Startup/")) return;
        TextureImporter ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Default;
        ti.alphaSource = TextureImporterAlphaSource.FromInput;
        ti.alphaIsTransparency = true;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.mipmapEnabled = true;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.filterMode = FilterMode.Trilinear;
        ti.maxTextureSize = 2048;
        ti.textureCompression = path.EndsWith(".jpg") ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Uncompressed;
    }
}
