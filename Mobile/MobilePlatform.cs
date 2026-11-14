using UnityEngine;

/// <summary>
/// Всё, что нужно игре на телефоне, кроме управления:
///  - только альбомная ориентация, экран не гаснет, 60 кадров в секунду;
///  - при сворачивании игры город сохраняется (это делает PushNotifications): телефон может закрыть свёрнутую игру без предупреждения.
/// На компьютере этот скрипт ничего не меняет.
/// </summary>
public class MobilePlatform : MonoBehaviour
{
    public static MobilePlatform Instance { get; private set; }

    /// <summary>Во сколько раз уменьшено разрешение. Всегда 1: игра рисуется в родном разрешении экрана.</summary>
    public static float RenderScale { get; private set; } = 1f;

    /// <summary>Родное разрешение экрана телефона (длинная и короткая сторона).</summary>
    public static int NativeLong { get; private set; }
    public static int NativeShort { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { RenderScale = 1f; NativeLong = 0; NativeShort = 0; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("MobilePlatform");
        DontDestroyOnLoad(go);
        go.AddComponent<MobilePlatform>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        int w = Display.main != null ? Display.main.systemWidth : Screen.width;
        int h = Display.main != null ? Display.main.systemHeight : Screen.height;
        if (w <= 0 || h <= 0) { w = Screen.currentResolution.width; h = Screen.currentResolution.height; }
        NativeLong = Mathf.Max(w, h);
        NativeShort = Mathf.Min(w, h);

        if (!TouchSettings.IsPhone) return;

        // Игра рассчитана на широкий экран - только альбомная ориентация, переворачивать можно
        Screen.autorotateToPortrait = false;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft = true;
        Screen.autorotateToLandscapeRight = true;
        Screen.orientation = ScreenOrientation.AutoRotation;

        Screen.sleepTimeout = SleepTimeout.NeverSleep;   // идёт набег или война - экран не должен гаснуть
        Application.targetFrameRate = 60;                // на Android по умолчанию 30
        phone = true;
    }

    // ---------- Экономия батареи (final-8) ----------
    // Раньше игра всегда шла 60 кадров/с и экран никогда не гас - тестеры жаловались, что сажает батарею.
    // Теперь: 60 кадров - пока палец на экране (и 3 с после), идёт война, набег или бой; иначе - 30.
    // Экран не гаснет только во время войны и боя, в мирное время - как настроено в телефоне.
    // Выключается в ⚙ → «Экономия батареи».
    bool phone;
    float lastTouch, tick;
    int fpsNow = 60;
    bool keepAwake = true;

    public static bool BatterySaver
    {
        get { return PlayerPrefs.GetInt("battery_saver", 1) == 1; }
        set { PlayerPrefs.SetInt("battery_saver", value ? 1 : 0); PlayerPrefs.Save(); }
    }

    void Update()
    {
        if (!phone) return;
        if (TouchInput.FingerDown || MobileInput.Busy) lastTouch = Time.unscaledTime;
        tick -= Time.unscaledDeltaTime;
        if (tick > 0f) return;
        tick = 0.25f;

        bool fight = WarManager.IsWar || RaidManager.ViewingBattle || HomeSiege.UnderRaid;
        bool busy = Time.unscaledTime - lastTouch < 3f || fight;
        int want = !BatterySaver || busy ? 60 : 30;
        if (want != fpsNow) { fpsNow = want; Application.targetFrameRate = want; }

        // Армия в набеге: экран не гаснет (свёрнутая игра не может вести живой бой), но кадров хватит и 30
        bool awake = fight || !BatterySaver || (RaidManager.Instance != null && RaidManager.Instance.ArmyAway);
        if (awake != keepAwake)
        {
            keepAwake = awake;
            Screen.sleepTimeout = awake ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;
        }
    }

    // Сохранение города при сворачивании игры делает PushNotifications.OnApplicationPause
    // (заодно отправляет серверу план строек для уведомлений; во время войны не сохраняет) - второе здесь не нужно.
}
