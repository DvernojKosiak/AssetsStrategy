using UnityEditor;
using UnityEngine;

/// <summary>
/// Иконка игры (компас с буквой S на досках). Картинки лежат в Assets/Icon:
///  - strategyca_icon_1024.png - основная (обычная иконка Android и иконка по умолчанию);
///  - strategyca_icon_fg.png / strategyca_icon_bg.png - «адаптивная» иконка Android 8+ (передний план и фон):
///    телефон сам обрезает её кругом, скруглённым квадратом и т.п., компас при этом виден целиком.
/// Ставится сама в «Strategyca → Телефон: настроить Android» и перед каждой сборкой APK / AAB (PhoneBuild).
/// Можно и вручную: меню «Strategyca → Телефон: поставить иконку».
/// </summary>
public static class AppIcon
{
    const string Main = "Assets/Icon/strategyca_icon_1024.png";
    const string Fg = "Assets/Icon/strategyca_icon_fg.png";
    const string Bg = "Assets/Icon/strategyca_icon_bg.png";

    [MenuItem("Strategyca/Телефон: поставить иконку")]
    public static void ApplyMenu() { Debug.Log("Strategyca: " + Apply()); }

    /// <summary>Поставить иконку игры. Возвращает строку для отчёта.</summary>
    public static string Apply()
    {
        Texture2D main = Load(Main), fg = Load(Fg), bg = Load(Bg);
        if (main == null) return "иконка не найдена (нет " + Main + ")";

        // Иконка по умолчанию: из неё Unity делает иконки всех размеров
        PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new[] { main });

#if UNITY_ANDROID
        try
        {
            // Виды иконок спрашиваем у самого Unity: в Unity 6 «Legacy» и «Round» убрали,
            // осталась только адаптивная (на старых версиях поставятся и они)
            PlatformIconKind[] kinds = PlayerSettings.GetSupportedIconKindsForPlatform(BuildTargetGroup.Android);
            if (kinds != null)
                foreach (var kind in kinds)
                    if (kind != null) SetKind(kind, main, bg, fg);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("Strategyca: иконки Android по размерам не поставились (" + e.Message + "), будет иконка по умолчанию");
        }
#endif
        AssetDatabase.SaveAssets();
        return "компас Strategyca";
    }

#if UNITY_ANDROID
    // Все размеры одного вида иконки - одной картинкой (Unity сам уменьшит). Адаптивная - два слоя: фон и передний план.
    static void SetKind(PlatformIconKind kind, Texture2D main, Texture2D bg, Texture2D fg)
    {
        PlatformIcon[] icons = PlayerSettings.GetPlatformIcons(BuildTargetGroup.Android, kind);
        if (icons == null || icons.Length == 0) return;
        foreach (var icon in icons)
        {
            if (icon.maxLayerCount >= 2 && bg != null && fg != null) icon.SetTextures(bg, fg);   // адаптивная: фон и передний план
            else icon.SetTexture(main);
        }
        PlayerSettings.SetPlatformIcons(BuildTargetGroup.Android, kind, icons);
    }
#endif

    // Картинка иконки: без сжатия и мип-карт, чтобы иконка на телефоне была чёткой
    static Texture2D Load(string path)
    {
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) return null;
        bool dirty = false;
        if (imp.textureType != TextureImporterType.Default) { imp.textureType = TextureImporterType.Default; dirty = true; }
        if (imp.mipmapEnabled) { imp.mipmapEnabled = false; dirty = true; }
        if (imp.textureCompression != TextureImporterCompression.Uncompressed) { imp.textureCompression = TextureImporterCompression.Uncompressed; dirty = true; }
        if (imp.npotScale != TextureImporterNPOTScale.None) { imp.npotScale = TextureImporterNPOTScale.None; dirty = true; }
        if (imp.maxTextureSize < 1024) { imp.maxTextureSize = 1024; dirty = true; }
        if (imp.alphaIsTransparency != true) { imp.alphaIsTransparency = true; dirty = true; }
        if (dirty) imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
