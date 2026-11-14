using System;
using System.Collections.Generic;
using UnityEngine;
#if STRATEGYCA_FCM && UNITY_ANDROID && !UNITY_EDITOR
using Firebase;
using Firebase.Messaging;
#endif

/// <summary>
/// Push-уведомления (Firebase Cloud Messaging). Шлёт их сервер, даже когда игра закрыта:
///  - «На вас напали!» - другой игрок начал набег (и «Набег окончен» - что унесли и какой щит);
///  - «Стройка готова» - игра сообщает серверу, когда закончатся стройки и улучшения (Api.PushPlan);
///  - «Щит скоро кончится» - за 30 минут (короткий щит - за 10).
/// Что включено - ⚙ Настройки → Аккаунт → Уведомления (AccountProfile.pushOff, хранит сервер).
///
/// Работает в сборке для Android, когда в проект добавлен Firebase Cloud Messaging (FirebaseMessaging.unitypackage
/// и google-services.json): символ STRATEGYCA_FCM ставится сам (Assets/Online/Editor/GooglePlayDefines.cs).
/// Без него игра работает как раньше, просто без уведомлений.
/// Ещё здесь: игра свернулась (телефон) - сразу сохраняем город, чтобы ничего не потерять.
/// </summary>
public class PushNotifications : MonoBehaviour
{
    static PushNotifications inst;

    /// <summary>Токен этого телефона в Firebase (null - уведомлений нет).</summary>
    public static string Token { get; private set; }

#if STRATEGYCA_FCM && UNITY_ANDROID && !UNITY_EDITOR
    public static bool Available => true;
#else
    public static bool Available => false;
#endif

    const float PlanEvery = 20f;
    const string AskedKey = "Strategyca.PushAsked";

    bool started;
    string sentToken, sentFor;      // какой токен и для какого аккаунта уже отдали серверу
    float planTimer = 5f;
    readonly List<PushPlanItem> sentPlan = new List<PushPlanItem>();
    bool planSent;
    float askAt = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { inst = null; Token = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (inst != null) return;
        var go = new GameObject("PushNotifications");
        DontDestroyOnLoad(go);
        inst = go.AddComponent<PushNotifications>();
    }

    static bool Online => AccountManager.LoggedIn && !AccountManager.IsOffline && !string.IsNullOrEmpty(AccountManager.Token);

    void Update()
    {
        if (!Online || !Available) return;
        if (!started) { started = true; StartFirebase(); askAt = Time.unscaledTime + 4f; }
        if (askAt > 0f && Time.unscaledTime > askAt && !AccountManager.Blocking) { askAt = -1f; AskPermission(); }

        string me = AccountManager.Profile.id;
        if (!string.IsNullOrEmpty(Token) && (Token != sentToken || me != sentFor)) Register(me);

        planTimer -= Time.unscaledDeltaTime;
        if (planTimer <= 0f) { planTimer = PlanEvery; SendPlan(); }
    }

    // Телефон: игру свернули или закрыли - сохраняем город и отдаём серверу время строек
    void OnApplicationPause(bool paused)
    {
        if (!paused || !Online) return;
        SendPlan();
        // И во время боя тоже: иначе закрыл игру посреди набега - и город вернулся к виду до него.
        // Враги на карте сохраняются вместе с городом (WarManager.SaveState), бой продолжится или досчитается при входе.
        if (AccountManager.Instance != null) AccountManager.Instance.SaveCityNow(true);
    }

    // ---------- Firebase ----------

    void StartFirebase()
    {
#if STRATEGYCA_FCM && UNITY_ANDROID && !UNITY_EDITOR
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWith(t =>
        {
            if (t.IsFaulted || t.IsCanceled || t.Result != DependencyStatus.Available)
            {
                Debug.LogWarning("[Push] Firebase недоступен: " + (t.IsFaulted ? t.Exception?.Message : t.Result.ToString()));
                return;
            }
            MainThreadQueue.Post(() =>
            {
                FirebaseMessaging.TokenReceived += (s, e) => MainThreadQueue.Post(() => Token = e.Token);
                FirebaseMessaging.MessageReceived += (s, e) => { };   // игра открыта: о набеге и стройке она и так говорит сама
                FirebaseMessaging.GetTokenAsync().ContinueWith(tt =>
                {
                    if (!tt.IsFaulted && !tt.IsCanceled && !string.IsNullOrEmpty(tt.Result)) MainThreadQueue.Post(() => Token = tt.Result);
                });
            });
        });
#endif
    }

    // Android 13+: уведомления надо разрешить. Спрашиваем один раз, когда город уже открыт
    static void AskPermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        const string perm = "android.permission.POST_NOTIFICATIONS";
        if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(perm)) return;
        if (PlayerPrefs.GetInt(AskedKey, 0) >= 2) return;   // дважды отказали - больше не пристаём (включить можно в настройках телефона)
        PlayerPrefs.SetInt(AskedKey, PlayerPrefs.GetInt(AskedKey, 0) + 1);
        PlayerPrefs.Save();
        UnityEngine.Android.Permission.RequestUserPermission(perm);
#endif
    }

    // ---------- Сервер ----------

    static string Lang => Loc.LangCodes[(int)Loc.Current];

    void Register(string me)
    {
        string t = Token;
        sentToken = t;
        sentFor = me;
        planSent = false;
        AccountManager.Backend.Call(Api.PushToken, AccountManager.Token, new ApiRequest { pushToken = t, lang = Lang, pushOff = AccountManager.Profile.pushOff }, res =>
        {
            if (!res.ok) { sentToken = null; return; }   // повторим (старый сервер ответит «Неизвестный запрос» - тоже не страшно)
            SendPlan();
        });
    }

    /// <summary>Переключили уведомления в настройках - сообщаем серверу (он и решает, что слать).</summary>
    public static void SaveSettings()
    {
        if (!Online) return;
        AccountManager.Backend.Call(Api.PushToken, AccountManager.Token, new ApiRequest { pushToken = Token, lang = Lang, pushOff = AccountManager.Profile.pushOff }, null);
    }

    /// <summary>Сменили язык - уведомления тоже на новом языке.</summary>
    void OnEnable() { Loc.Changed += LangChanged; }
    void OnDisable() { Loc.Changed -= LangChanged; }
    void LangChanged() { sentToken = null; }

    // Стройки и улучшения: когда закончатся (по часам сервера). Шлём, только если что-то поменялось
    void SendPlan()
    {
        if (!Online || string.IsNullOrEmpty(sentToken) || !WorldClock.Active || WarCityMode.Active) return;
        if (BuildingPlacer.Instance == null || !BuildingPlacer.Instance.WorldReady) return;
        var plan = new List<PushPlanItem>();
        DateTime now = WorldClock.ServerUtc;
        foreach (var b in BuildingInstance.All)
        {
            if (b == null || b.type == null || b.TimerLeft <= 0f || b.IsRoad || b.IsWall) continue;
            bool up = !b.UnderConstruction;
            string name = Loc.T(Loc.Hall(b.type.name));
            plan.Add(new PushPlanItem { atUtc = now.AddSeconds(b.TimerLeft).ToString("o"), text = up ? name + " " + Loc.T("Ур.") + b.UpgradeTarget : name, upgrade = up });
        }
        plan.Sort((x, y) => string.CompareOrdinal(x.atUtc, y.atUtc));
        if (plan.Count > 40) plan.RemoveRange(40, plan.Count - 40);
        if (planSent && SamePlan(plan)) return;
        sentPlan.Clear();
        sentPlan.AddRange(plan);
        planSent = true;
        AccountManager.Backend.Call(Api.PushPlan, AccountManager.Token, new ApiRequest { pushPlan = plan }, res => { if (!res.ok) planSent = false; });
    }

    // Те же стройки и время почти то же (часы плывут на доли секунды) - заново не шлём
    bool SamePlan(List<PushPlanItem> plan)
    {
        if (plan.Count != sentPlan.Count) return false;
        for (int i = 0; i < plan.Count; i++)
        {
            if (plan[i].text != sentPlan[i].text) return false;
            DateTime a = DateTime.Parse(plan[i].atUtc, null, System.Globalization.DateTimeStyles.RoundtripKind);
            DateTime b = DateTime.Parse(sentPlan[i].atUtc, null, System.Globalization.DateTimeStyles.RoundtripKind);
            if (Math.Abs((a - b).TotalSeconds) > 60) return false;
        }
        return true;
    }
}
