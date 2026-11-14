using UnityEngine;

/// <summary>
/// Настройки телефона: включено ли сенсорное управление. Хранятся в PlayerPrefs.
/// Окно - ⚙ → «Управление пальцами и жесты» (TouchControls).
/// Размер окон теперь - общая настройка «Размер интерфейса» (GameSettings, ключ ui_size; на телефоне
/// окна и так рассчитаны по плотности экрана - UiKit). Старый выбор размера из telefon-1 и telefon-2
/// при первом запуске стирается.
/// </summary>
public static class TouchSettings
{
    const string KMode = "touch_mode", KHelp = "touch_help_seen";
    // Ключи бывшей настройки размера интерфейса: telefon-1 и telefon-2 (телефон и компьютер)
    static readonly string[] OldScaleKeys = { "touch_ui_scale", "touch_ui_scale2", "ui_scale_pc" };

    public enum Mode { Auto = 0, On = 1, Off = 2 }

    static bool loaded;
    static Mode mode = Mode.Auto;
    static bool helpSeen;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { loaded = false; }

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        mode = (Mode)Mathf.Clamp(PlayerPrefs.GetInt(KMode, 0), 0, 2);
        bool hadScale = false;
        foreach (string k in OldScaleKeys)
            if (PlayerPrefs.HasKey(k)) { PlayerPrefs.DeleteKey(k); hadScale = true; }
        if (hadScale) PlayerPrefs.Save();
        helpSeen = PlayerPrefs.GetInt(KHelp, 0) == 1;
    }

    static void Save()
    {
        PlayerPrefs.SetInt(KMode, (int)mode);
        PlayerPrefs.SetInt(KHelp, helpSeen ? 1 : 0);
        PlayerPrefs.Save();
    }

    public static Mode TouchMode { get { Load(); return mode; } set { Load(); mode = value; Save(); } }
    public static bool HelpSeen { get { Load(); return helpSeen; } set { Load(); helpSeen = value; Save(); } }

    // Прежние названия из telefon-2 - чтобы старые файлы собирались. Размер всегда родной.
    public static float UiScale => 1f;
    public static bool PhoneLayout => IsPhone || TouchInput.Active;
    public static float ScreenPxPerMm => PxPerMm;

    /// <summary>Сбросить размер интерфейса к стандартному (вызывается при запуске).</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void ResetOldScale() { Load(); }

    /// <summary>Игра запущена на телефоне или планшете (не в редакторе).</summary>
    public static bool IsPhone => Application.isMobilePlatform && !Application.isEditor;

    /// <summary>
    /// Нужно ли сенсорное управление. Авто: на телефоне всегда, в редакторе - как только пришло касание
    /// (Unity Remote или Device Simulator). На компьютере в собранной игре - только если включили вручную.
    /// </summary>
    public static bool Wanted(bool touchSeen)
    {
        switch (TouchMode)
        {
            case Mode.On: return true;
            case Mode.Off: return IsPhone;   // на телефоне без касаний играть нечем - там не выключаем
            default: return Application.isMobilePlatform || (Application.isEditor && touchSeen);
        }
    }

    /// <summary>Сколько точек экрана в одном миллиметре (с учётом уменьшенного разрешения).</summary>
    public static float PxPerMm
    {
        get
        {
            float dpi = Screen.dpi > 1f ? Screen.dpi : 160f;
            float k = MobilePlatform.RenderScale;
            return Mathf.Max(2.5f, dpi / 25.4f * k);
        }
    }
}
