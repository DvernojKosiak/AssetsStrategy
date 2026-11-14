using System;
using System.Collections.Generic;
using UnityEngine;
#if STRATEGYCA_LOCALNOTIF && UNITY_ANDROID && !UNITY_EDITOR
using Unity.Notifications.Android;
#endif

/// <summary>
/// Уведомления на телефоне без Firebase (final-8). Когда игру сворачивают, телефон сам запоминает, когда сказать:
///  - «Стройка готова» / «Улучшение готово» (по таймерам зданий);
///  - «Армия вернулась домой» / «Армия дошла до …»;
///  - «Щит кончится через 30 минут»;
///  - через сутки без игры - «Город ждёт вас».
/// Вернулись в игру - всё запланированное снимается (игра скажет сама).
/// «На вас напали» так не сделать: это знает только сервер, и для этого нужен Firebase (PushNotifications).
/// Если Firebase работает (есть токен), стройки и щит присылает сервер, а здесь остаются армия и напоминание.
/// Что включено - те же галочки ⚙ Настройки → Аккаунт → Уведомления (AccountProfile.pushOff).
///
/// Работает в сборке для Android, когда в проект добавлен пакет Mobile Notifications (com.unity.mobile.notifications):
/// символ STRATEGYCA_LOCALNOTIF ставится сам (Online/Editor/GooglePlayDefines.cs). Без пакета - просто ничего не делает.
/// </summary>
public class LocalNotifications : MonoBehaviour
{
#if STRATEGYCA_LOCALNOTIF && UNITY_ANDROID && !UNITY_EDITOR
    public static bool Available => true;
    const string Channel = "strategyca_city";
#else
    public static bool Available => false;
#endif

    const string AskedKey = "Strategyca.PushAsked";   // тот же счётчик, что у PushNotifications: спрашиваем не больше двух раз
    const int MaxItems = 20;
    const double MergeSeconds = 120.0;                 // стройки, которые кончаются почти разом, - одним уведомлением
    const double ComebackHours = 24.0;

    static LocalNotifications inst;
    bool ready;
    float askAt = -1f;

    struct Item { public double at; public string title, text; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { inst = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (inst != null || !Available) return;
        var go = new GameObject("LocalNotifications");
        DontDestroyOnLoad(go);
        inst = go.AddComponent<LocalNotifications>();
    }

    static bool InCity => AccountManager.LoggedIn && BuildingPlacer.Instance != null && BuildingPlacer.Instance.WorldReady && !WarCityMode.Active;

    static bool Off(int kind) { return AccountManager.Profile != null && (AccountManager.Profile.pushOff & kind) != 0; }

    // Firebase уже шлёт стройки и щит - их здесь не дублируем
    static bool ServerPush => PushNotifications.Available && !string.IsNullOrEmpty(PushNotifications.Token) && AccountManager.LoggedIn && !AccountManager.IsOffline;

    void Start()
    {
        Init();
        ClearAll();
    }

    void Update()
    {
        if (!ready || !InCity) return;
        // Разрешение на уведомления (Android 13+). Если есть Firebase, спрашивает он
        if (askAt < 0f && !PushNotifications.Available) askAt = Time.unscaledTime + 6f;
        if (askAt > 0f && Time.unscaledTime > askAt && !AccountManager.Blocking) { askAt = 0f; AskPermission(); }
    }

    void OnApplicationPause(bool paused)
    {
        if (paused) Schedule(); else ClearAll();
    }

    void OnApplicationFocus(bool focus)
    {
        if (focus) ClearAll();
    }

    void OnApplicationQuit() { Schedule(); }

    // ---------- что сказать ----------

    static List<Item> Plan()
    {
        var list = new List<Item>();
        if (!InCity) return list;

        if (!ServerPush && !Off(PushKinds.Build))
        {
            var builds = new List<Item>();
            foreach (var b in BuildingInstance.All)
            {
                if (b == null || b.type == null || b.TimerLeft <= 1f || b.IsRoad || b.IsWall) continue;
                string name = Loc.T(Loc.Hall(b.type.name));
                bool up = !b.UnderConstruction;
                builds.Add(new Item
                {
                    at = b.TimerLeft,
                    title = Loc.T(up ? "Улучшение готово" : "Стройка готова"),
                    text = up ? name + " " + Loc.T("Ур.") + " " + b.UpgradeTarget : name,
                });
            }
            builds.Sort((x, y) => x.at.CompareTo(y.at));
            // Кончаются почти разом - одно уведомление «Готово: Дом, Ферма, Склад»
            for (int i = 0; i < builds.Count; i++)
            {
                Item first = builds[i];
                var names = new List<string> { first.text };
                while (i + 1 < builds.Count && builds[i + 1].at - first.at < MergeSeconds) { i++; names.Add(builds[i].text); }
                if (names.Count > 1)
                {
                    first.at = builds[i].at;
                    first.title = Loc.T("Готово");
                    first.text = names.Count <= 3 ? string.Join(", ", names) : string.Join(", ", names.GetRange(0, 3)) + " +" + (names.Count - 3);
                }
                list.Add(first);
            }
        }

        if (!Off(PushKinds.Attack) && RaidManager.Instance != null)
        {
            float left = RaidManager.Instance.TripSecondsLeft(out bool outbound);
            if (left > 5f)
            {
                string where = RaidManager.Instance.TripTarget;
                list.Add(outbound
                    ? new Item { at = left, title = Loc.T("Армия у цели"), text = Loc.T("Армия дошла до деревни") + " " + where + ". " + Loc.T("Загляните в бой") }
                    : new Item { at = left, title = Loc.T("Армия вернулась"), text = Loc.T("Армия вернулась домой с добычей") });
            }
        }

        if (!ServerPush && !Off(PushKinds.Shield) && CityShield.Active)
        {
            double left = CityShield.SecondsLeft;
            double warn = left > 3600.0 ? 1800.0 : 600.0;
            if (left > warn + 30.0)
                list.Add(new Item { at = left - warn, title = Loc.T("Щит скоро кончится"), text = Loc.T(warn >= 1800.0 ? "Щит города кончится через 30 минут" : "Щит города кончится через 10 минут") });
        }

        if (!Off(PushKinds.Build))
            list.Add(new Item { at = ComebackHours * 3600.0, title = "Strategyca", text = Loc.T("Ваш город ждёт: жители накопили ресурсы, пора строить дальше") });

        list.Sort((x, y) => x.at.CompareTo(y.at));
        if (list.Count > MaxItems) list.RemoveRange(MaxItems, list.Count - MaxItems);
        return list;
    }

    // ---------- телефон ----------

    void Init()
    {
#if STRATEGYCA_LOCALNOTIF && UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            AndroidNotificationCenter.Initialize();
            AndroidNotificationCenter.RegisterNotificationChannel(new AndroidNotificationChannel
            {
                Id = Channel,
                Name = "Strategyca",
                Description = Loc.T("Стройки, армия и щит"),
                Importance = Importance.Default,
            });
            ready = true;
        }
        catch (Exception e) { Debug.LogWarning("[Уведомления] " + e.Message); }
#endif
    }

    void Schedule()
    {
        if (!ready) return;
        ClearAll();
        List<Item> plan;
        try { plan = Plan(); }
        catch (Exception e) { Debug.LogWarning("[Уведомления] " + e.Message); return; }
#if STRATEGYCA_LOCALNOTIF && UNITY_ANDROID && !UNITY_EDITOR
        foreach (var it in plan)
        {
            try
            {
                AndroidNotificationCenter.SendNotification(new AndroidNotification
                {
                    Title = it.title,
                    Text = it.text,
                    FireTime = DateTime.Now.AddSeconds(it.at),
                }, Channel);
            }
            catch (Exception e) { Debug.LogWarning("[Уведомления] " + e.Message); break; }
        }
#endif
    }

    void ClearAll()
    {
        if (!ready) return;
#if STRATEGYCA_LOCALNOTIF && UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            AndroidNotificationCenter.CancelAllScheduledNotifications();
            AndroidNotificationCenter.CancelAllDisplayedNotifications();
        }
        catch (Exception) { }
#endif
    }

    static void AskPermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        const string perm = "android.permission.POST_NOTIFICATIONS";
        if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(perm)) return;
        if (PlayerPrefs.GetInt(AskedKey, 0) >= 2) return;
        PlayerPrefs.SetInt(AskedKey, PlayerPrefs.GetInt(AskedKey, 0) + 1);
        PlayerPrefs.Save();
        UnityEngine.Android.Permission.RequestUserPermission(perm);
#endif
    }
}
