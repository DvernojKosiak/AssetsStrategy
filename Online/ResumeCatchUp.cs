using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Игру свернули (переключились на другое приложение) и вернулись. Пока игра свёрнута, телефон её не запускает,
/// поэтому без этого стройки, походы и набеги стояли бы на месте.
///  - При сворачивании город сохраняется (PushNotifications.OnApplicationPause), запоминаем время.
///  - Вернулись через MinAway секунд или позже - сохраняем город ещё раз (с часами на момент сворачивания:
///    первое сохранение телефон мог оборвать), затем перезагружаем его с сервера (WarCityMode.Reload, быстрый экран
///    загрузки без заставки). Загрузка сама досчитывает всё, что было бы за это время: стройки и улучшения, доход
///    добытчиков и курьеров, руины, поход армии, набеги на город, отряды в лагерях.
///  - Сервер ставит время сохранения, когда сохранение до него дошло. Если телефон отправил его не сразу, а только
///    после возвращения, отсчёт ведём с момента сворачивания (FixSavedTime).
/// </summary>
public class ResumeCatchUp : MonoBehaviour
{
    const float MinAway = 8f;                    // так недолго - просто продолжаем
    const double MaxBackdate = 14 * 86400.0;     // время сохранения сдвигаем назад не больше чем на столько

    // Не сбрасываются при перезапуске игры (GameRestart): нужны уже новой загрузке
    static DateTime pausedReal = DateTime.MinValue;   // часы телефона - сколько прошло
    static DateTime pausedServer = DateTime.MinValue; // часы сервера - для CityData.savedUtc
    static bool reloading;
    static Vector2 camPos;                            // куда смотрела камера - после перезагрузки смотрит туда же
    static float camSize = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindObjectOfType<ResumeCatchUp>() != null) return;
        var go = new GameObject("ResumeCatchUp");
        DontDestroyOnLoad(go);
        var me = go.AddComponent<ResumeCatchUp>();
        reloading = false;
        if (camSize > 0f) me.StartCoroutine(me.RestoreCamera());
    }

    // После перезагрузки: камера - туда, куда смотрел игрок
    IEnumerator RestoreCamera()
    {
        float wait = 0f;
        while ((BuildingPlacer.Instance == null || !BuildingPlacer.Instance.WorldReady) && wait < 30f) { wait += Time.unscaledDeltaTime; yield return null; }
        yield return null;
        var cc = CameraController.Instance;
        if (cc != null && camSize > 0f)
        {
            cc.FocusOn(camPos);
            cc.SetZoomNow(camSize);
        }
        camSize = -1f;
    }

    static bool Online => AccountManager.LoggedIn && !AccountManager.IsOffline;

    void OnApplicationPause(bool paused)
    {
        if (paused)
        {
            if (!Online) return;
            pausedReal = DateTime.UtcNow;
            pausedServer = WorldClock.Active ? WorldClock.ServerUtc : DateTime.UtcNow;
            Camera cam = Camera.main;
            if (cam != null && !RaidManager.ViewingBattle) { camPos = cam.transform.position; camSize = cam.orthographicSize; }
            else camSize = -1f;
            return;
        }
        if (pausedReal == DateTime.MinValue || reloading) return;
        double away = (DateTime.UtcNow - pausedReal).TotalSeconds;
        if (away < MinAway || !Online || GameRestart.Running) { pausedReal = DateTime.MinValue; return; }
        StartCoroutine(Reload());
    }

    IEnumerator Reload()
    {
        reloading = true;
        // Сохранение при сворачивании могло не дойти: телефон усыпляет игру сразу, и запрос часто обрывается.
        // Ждём его, потом сохраняем ещё раз - город такой же, как в момент сворачивания (игра стояла),
        // и время в нём ставим тоже на момент сворачивания. Так при загрузке ничего не теряется и всё досчитывается.
        float wait = 0f;
        while (AccountManager.IsSaving && wait < 10f) { wait += Time.unscaledDeltaTime; yield return null; }
        bool done = false;
        if (AccountManager.Instance != null)
        {
            BuildingInstance.ClockOverride = pausedServer;
            try { AccountManager.Instance.SaveCityNow(true, ok => done = true); }
            finally { BuildingInstance.ClockOverride = null; }
        }
        else done = true;
        wait = 0f;
        while (!done && wait < 15f) { wait += Time.unscaledDeltaTime; yield return null; }
        WarCityMode.Reload();
    }

    /// <summary>Город пришёл с сервера после перезагрузки: время сохранения - момент сворачивания игры.</summary>
    public static void FixSavedTime(CityData c)
    {
        DateTime at = pausedServer;
        pausedReal = pausedServer = DateTime.MinValue;
        reloading = false;
        if (c == null || at == DateTime.MinValue || string.IsNullOrEmpty(c.savedUtc)) return;
        if (!DateTime.TryParse(c.savedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime saved)) return;
        saved = saved.ToUniversalTime();
        double back = (saved - at).TotalSeconds;
        if (back > 0.0 && back <= MaxBackdate) c.savedUtc = at.ToString("o");
    }
}
