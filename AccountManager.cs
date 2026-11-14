using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Аккаунт игрока и его город на сервере: экран входа при запуске, загрузка и автосохранение города,
/// системное меню (кнопка ⚙ в правом верхнем углу). Создаётся сам (добавлять в сцену не нужно).
///
/// Для остальной игры:
///   AccountManager.LoggedIn / IsOffline / Profile - кто играет;
///   AccountManager.Blocking - открыт экран входа или нет связи с сервером (карта и время стоят).
/// Играть можно только с сервером: при запуске - заставка Cinetica Games и экран загрузки (StartupScreen), пока игра
/// подключается, входит и строит город; пропала связь - игра стоит
/// и ждёт сервер, а если он так и не вернулся - закрывается.
/// Вход через Google Play Games, удаление аккаунта, уведомления и заблокированные игроки - в AccountManager_GooglePlay.cs.
/// </summary>
public partial class AccountManager : MonoBehaviour
{
    public static AccountManager Instance { get; private set; }

    static IAccountBackend backend;
    /// <summary>Связь с сервером. Адрес меняется на экране входа.</summary>
    public static IAccountBackend Backend
    {
        get { if (backend == null) backend = new OnlineAccountBackend(SavedServer); return backend; }
        set { backend = value; }
    }

    static string defaultServer;

    /// <summary>
    /// Адрес сервера по умолчанию. Для телефона его вшивают в игру: меню Unity «Strategyca → Телефон → Адрес сервера…»
    /// пишет Assets/Resources/strategyca_server.txt. Файла нет - тестовый сервер на этом компьютере (localhost).
    /// </summary>
    public static string DefaultServer
    {
        get
        {
            if (defaultServer != null) return defaultServer;
            var ta = Resources.Load<TextAsset>("strategyca_server");
            string a = ta != null ? ta.text.Trim() : "";
            defaultServer = a != "" ? a : Api.DefaultAddress;
            return defaultServer;
        }
    }

    /// <summary>
    /// Строка «Сервер: адрес ✎» нужна только для проверок. В готовой игре на телефоне (адрес вшит, сборка не тестовая)
    /// игроку её не показываем: адрес ему ни к чему. Если адрес уже меняли вручную - строка видна, чтобы вернуть.
    /// </summary>
    static bool ShowServerRow
    {
        get
        {
            if (!Application.isMobilePlatform || Debug.isDebugBuild) return true;
            var ta = Resources.Load<TextAsset>("strategyca_server");
            if (ta == null || ta.text.Trim() == "") return true;
            return Instance != null && Instance.serverAddress != DefaultServer;
        }
    }

    /// <summary>Адрес, выбранный на экране входа, или адрес по умолчанию. На телефоне «localhost» не бывает нужен - берём вшитый.</summary>
    static string SavedServer
    {
        get
        {
            string a = PlayerPrefs.GetString(ServerKey, DefaultServer);
            if (Application.isMobilePlatform && (a.Contains("localhost") || a.Contains("127.0.0.1"))) a = DefaultServer;
            return a;
        }
    }

    public static AccountProfile Profile { get; private set; }
    /// <summary>Токен сессии (для запросов живого боя).</summary>
    public static string Token => Instance != null ? Instance.token : null;
    public static bool LoggedIn => Profile != null;
    public static bool IsSaving => Instance != null && Instance.saving;
    /// <summary>Игра без аккаунта: город не сохраняется.</summary>
    public static bool IsOffline => Instance != null && Instance.offline;
    /// <summary>Открыт экран входа или нет связи с сервером: игра на паузе, карта не принимает клики.</summary>
    public static bool Blocking => UpdateGate.Active || (Instance != null && (Instance.screenOpen || Instance.link != Link.Online || StartupScreen.Covering));

#if UNITY_EDITOR
    const string TokenKey = "Strategyca.AccountToken.Editor";   // у редактора и у собранной игры - свои сессии (можно играть двумя аккаунтами)
#else
    const string TokenKey = "Strategyca.AccountToken";
#endif
    const string LastLoginKey = "Strategyca.LastLogin";
    const string ServerKey = "Strategyca.Server";
    const float AutoSaveSeconds = 30f;
    const float FirstSaveSeconds = 3f;
    const float SignatureCheckSeconds = 1f;

    string token;
    bool screenOpen;
    bool busy;
    bool offline;
    float savedTimeScale = 1f;

    // Сохранение города
    float saveTimer = FirstSaveSeconds;
    bool saving;
    bool saveFailedShown;
    float lastSavedAt = -1f;
    bool quitSaveDone;
    int lastSignature;
    float signatureTimer;

    // Набеги на нас
    readonly System.Collections.Generic.List<string> appliedReports = new System.Collections.Generic.List<string>();   // учли, ждём подтверждения сервера
    readonly System.Collections.Generic.List<string> raidLog = new System.Collections.Generic.List<string>();          // для системного меню
    string incoming;

    // Связь с сервером: без неё играть нельзя
    enum Link { Connecting, Online, Lost }
    Link link = Link.Connecting;
    const float HeartbeatSeconds = 5f;       // в игре проверяем связь так часто
    const float RetrySeconds = 2f;           // нет связи - проверяем так часто
    const int LostAfterFails = 2;            // столько проверок подряд без ответа - связь потеряна
    const float QuitAfterLostSeconds = 90f;  // сервер не вернулся за это время - игра закрывается
    float linkTimer;
    int linkFails;
    bool pinging, serverSeen;
    float lostAt;
    float cityShownAt = -1f;                 // когда город открылся (экран загрузки ждёт, пока он построится)
    string linkError = "";
    string resumeToken;                      // сохранённая сессия: войдём, когда появится связь
    bool resumeRemember;
    float pausedTimeScale = 1f;

    // Системное меню (кнопка ⚙)
    bool systemOpen;

    // Экран входа
    int tab;               // 0 - вход, 1 - регистрация
    string login = "", password = "", password2 = "", displayName = "";
    bool remember = true;
    string error = "";
    string note = "";
    string serverAddress = "";
    Vector2 loginScroll;
    bool editServer;

    // Вкладка «Аккаунт»: смена имени и пароля
    string newName = "";
    string oldPass = "", newPass = "";
    string pageMsg = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; Profile = null; backend = null; defaultServer = null; loggedOut = false; firstCopy = 0; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("AccountManager");
        DontDestroyOnLoad(go);
        go.AddComponent<AccountManager>();
        go.AddComponent<HomeSiege>();   // набеги игроков на нас: бой вживую в нашей деревне
        go.AddComponent<PlayerMarket>();
        go.AddComponent<ClanWindow>();
        go.AddComponent<WarView>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        login = PlayerPrefs.GetString(LastLoginKey, "");
        serverAddress = SavedServer;
        Application.wantsToQuit += WantsToQuit;
        Application.runInBackground = true;   // онлайн-игра не замирает, когда окно не в фокусе (бой идёт дальше)
        OpenScreen();

        // Есть сохранённая сессия - входим без пароля (только в первой запущенной копии игры:
        // вторые окна - другие игроки, им нужен свой вход)
        // Переход в военный город и обратно (перезапуск игры) - входим тем же токеном
        string pending = WarCityMode.TakeToken();
        string saved = pending ?? (IsFirstCopy() ? PlayerPrefs.GetString(TokenKey, "") : "");
        bool remember = pending == null || (IsFirstCopy() && PlayerPrefs.HasKey(TokenKey));
        if (saved != "") { resumeToken = saved; resumeRemember = remember; }

        // Сначала - связь с сервером, пока идут заставка и экран загрузки; переход в военный город и обратно - без заставки
        StartupScreen.Begin(pending == null);
        link = Link.Connecting;
        linkTimer = 0f;
    }

    void ResumeSaved()
    {
        string saved = resumeToken;
        resumeToken = null;
        if (string.IsNullOrEmpty(saved)) return;
        bool remember = resumeRemember;
        busy = true;
        note = WarCityMode.Active ? "Переходим в военный город…" : "Вход…";
        Backend.Resume(saved, r =>
        {
            busy = false;
            note = "";
            if (r.ok) SignedIn(r, remember, false);
            else
            {
                if (r.error != null && r.error.StartsWith("Сессия")) PlayerPrefs.DeleteKey(TokenKey);
                note = r.error;
                TryGoogleSilent();   // сессия устарела - может, войдём через Google Play Games
            }
        });
    }

    // ---------- Связь с сервером ----------

    void TickLink()
    {
        linkTimer -= Time.unscaledDeltaTime;
        if (link == Link.Lost && Time.unscaledTime - lostAt > QuitAfterLostSeconds && !IsVirtualPlayer) { QuitNoServer(); return; }
        if (link == Link.Connecting && serverSeen) { GoOnline(); return; }
        if (pinging || linkTimer > 0f) return;
        pinging = true;
        Backend.Ping(r =>
        {
            pinging = false;
            if (r.ok)
            {
                linkFails = 0;
                linkError = "";
                if (link == Link.Connecting) serverSeen = true;
                else if (link == Link.Lost) GoOnline();
                linkTimer = HeartbeatSeconds;
                return;
            }
            linkFails++;
            linkError = r.error ?? "";
            linkTimer = RetrySeconds;
            if (link == Link.Online && linkFails >= LostAfterFails) LoseLink();
        });
    }

    void GoOnline()
    {
        bool wasLost = link == Link.Lost;
        link = Link.Online;
        linkFails = 0;
        linkTimer = HeartbeatSeconds;
        if (wasLost)
        {
            if (!screenOpen) Time.timeScale = pausedTimeScale;
            WarManager.Notify("Связь с сервером восстановлена, игра продолжается", true);
            if (CanSave) saveTimer = 0.5f;   // сразу сохранить город
        }
        else
        {
            if (string.IsNullOrEmpty(resumeToken)) TryGoogleSilent();   // сохранённой сессии нет (новый телефон) - входим через Google Play Games
            ResumeSaved();
        }
    }

    void LoseLink()
    {
        link = Link.Lost;
        lostAt = Time.unscaledTime;
        if (!screenOpen) pausedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        systemOpen = false;
    }

    static void QuitNoServer()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // Тревога (WarManager.RaiseAlarm): свернуть системное меню
    void OnEnable() { WarManager.OnAlarm += CollapseForAlarm; }
    void OnDisable() { WarManager.OnAlarm -= CollapseForAlarm; }
    bool CollapseForAlarm()
    {
        if (!systemOpen) return false;
        systemOpen = false;
        return true;
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        Application.wantsToQuit -= WantsToQuit;
        if (screenOpen) Time.timeScale = savedTimeScale;
        Instance = null;
    }

    // ---------- Вход ----------

    void OpenScreen()
    {
        if (screenOpen) return;
        screenOpen = true;
        savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        password = password2 = "";
        error = "";
    }

    void CloseScreen()
    {
        if (!screenOpen) return;
        screenOpen = false;
        cityShownAt = Time.unscaledTime;
        Time.timeScale = savedTimeScale;
        // Пока вводили логин, буквы могли открыть окна игры (F, L, T, B…) - закрываем их
        WarManager.CollapseWindows();
    }

    static int firstCopy;   // 0 - ещё не проверяли, 1 - первая копия игры, 2 - второе окно

    /// <summary>
    /// Эта копия игры запущена первой? Вторые окна (Multiplayer Play Mode, игра запущена ещё раз) - это другие игроки:
    /// они не входят в запомненный аккаунт и не перезаписывают его. Первая копия пишет свой номер процесса в файл.
    /// </summary>
    static bool IsFirstCopy()
    {
        if (firstCopy != 0) return firstCopy == 1;
        firstCopy = 1;
        try
        {
            string path = System.IO.Path.Combine(Application.persistentDataPath, "main_copy.pid");
            int me = System.Diagnostics.Process.GetCurrentProcess().Id;
            if (System.IO.File.Exists(path) && int.TryParse(System.IO.File.ReadAllText(path).Trim(), out int other) && other != me)
            {
                bool alive;
                try { alive = !System.Diagnostics.Process.GetProcessById(other).HasExited; }
                catch (System.Exception) { alive = false; }
                if (alive) { firstCopy = 2; return false; }
            }
            System.IO.File.WriteAllText(path, me.ToString());
        }
        catch (System.Exception) { }
        return true;
    }

    void SignedIn(AccountResult r, bool rememberMe, bool fresh)
    {
        Profile = r.profile;
        token = r.token;
        if (IsFirstCopy())
        {
            if (rememberMe) PlayerPrefs.SetString(TokenKey, token);
            else PlayerPrefs.DeleteKey(TokenKey);
            PlayerPrefs.SetString(LastLoginKey, Profile.login);
        }
        PlayerPrefs.Save();
        password = password2 = "";
        error = "";
        newName = Profile.displayName;

        if (WarCityMode.Active) { LoadWarCity(); return; }

        // Город игрока: с сервера или новый
        busy = true;
        note = "Загрузка города…";
        Backend.LoadCity(token, res =>
        {
            busy = false;
            note = "";
            if (!res.ok) { error = "Не удалось загрузить город: " + res.error; return; }

            CityData city = null;
            if (res.city != null)
            {
                try { city = JsonUtility.FromJson<CityData>(res.city); }
                catch (System.Exception) { city = null; }
                if (city == null || city.buildings == null || city.buildings.Count == 0)
                {
                    error = "Город на сервере повреждён. Сообщите об этом.";
                    return;
                }
            }

            // Последнее сохранение не дошло до сервера (игру закрыли) - берём копию с устройства (sohranenie-1)
            CityData fromServer = city;
            city = CityBackup.Prefer(Profile.id, city, res);
            bool fromBackup = city != fromServer;
            if (city != null) ResumeCatchUp.FixSavedTime(city);   // свернули игру: время считаем с момента сворачивания
            // Трофеи (обновление 23): сложные вещи, которые у нас отняли в набегах, сервер не знает - вычитаем сами,
            // пока город ещё не построен (потом он сохранится уже без них)
            if (city != null && res.reports != null)
                foreach (var rep in res.reports) if (rep != null && !rep.live) Spoils.RemoveFromCity(city, rep.ruinedAt);
            CityBoot.Provide(city);
            CloseScreen();
            saveTimer = FirstSaveSeconds;   // город из копии тоже уйдёт на сервер в первые секунды
            lastSignature = 0;
            if (fromBackup) Debug.Log("[Копия города] город взят с устройства, отправим его на сервер");
            if (city == null) WarManager.Notify(fresh ? $"Аккаунт создан. Добро пожаловать, {Profile.displayName}! Это твой город." : $"{Profile.displayName}, это твой новый город", true);
            else WarManager.Notify($"С возвращением, {Profile.displayName}!", true);
            AfterHomeCity();   // привязать Google Play Games, если ещё нет

            // Пока вас не было, на деревню нападали: сервер уже вычел потери, просто рассказываем
            if (res.reports != null)
                foreach (var rep in res.reports) if (rep != null) TellReport(rep, true);
            if (res.sales != null)
                foreach (var sale in res.sales) if (sale != null) TellSale(sale, true);
            // Награды за клановую войну сервер уже положил в город
            if (res.grants != null)
                foreach (var g in res.grants) if (g != null) TellGrant(g);
            NoteIncoming(res.incomingName);
        });
    }

    // ---------- Военный город ----------

    string pendingRuined;

    void LoadWarCity()
    {
        busy = true;
        note = "Переход на войну…";
        Backend.Clan(Api.WarCityLoad, token, new ApiRequest(), res =>
        {
            busy = false;
            note = "";
            CityData city = null;
            if (res.ok && res.city != null)
            {
                try { city = JsonUtility.FromJson<CityData>(res.city); }
                catch (System.Exception) { city = null; }
            }
            if (city == null || city.buildings == null || city.buildings.Count == 0)
            {
                error = "На войну сейчас не попасть" + (res.ok ? "" : ": " + res.error) + ". Возвращаемся домой…";
                WarCityMode.Leave(false);
                return;
            }
            CityBoot.Provide(city);
            CloseScreen();
            saveTimer = FirstSaveSeconds;
            lastSignature = 0;
            pendingRuined = res.ruined;
            if (res.sales != null)   // золото за них уже в городе
                foreach (var sale in res.sales) if (sale != null) TellSale(sale, true);
            WarManager.Notify("Война: ваша деревня стоит на общей карте и живёт как дома — строить и улучшать нельзя, ресурсы свои. Казармы учат бойцов, обученные сами выходят на карту. Отдалите камеру — вокруг вся война.", true);
        });
    }

    void SaveWarCity(CityData c, System.Action<bool> done)
    {
        Backend.Clan(Api.WarCitySave, token, new ApiRequest { city = JsonUtility.ToJson(c) }, res =>
        {
            saving = false;
            if (res.ok)
            {
                lastSavedAt = Time.unscaledTime;
                if (BuildingPlacer.Instance != null) lastSignature = BuildingPlacer.Instance.BuildingSignature();
                saveFailedShown = false;
                WarCityMode.SetRuined(res.ruined);
                WarGrant g = res.warIncome;
                if (g != null && (g.gold > 0 || g.food > 0 || g.wood > 0 || g.stone > 0 || g.iron > 0) && ResourceManager.Instance != null)
                {
                    CityIncome.Skip();
                    ResourceManager.Instance.Add(g.gold, g.wood, g.stone, g.food, g.iron);
                    WarManager.Notify("Захваченные земли и источники принесли: " + WarRules.IncomeText(new[] { g.gold, g.food, g.wood, g.stone, g.iron }), true);
                }
                if (res.sales != null && ResourceManager.Instance != null)
                    foreach (var sale in res.sales)
                    {
                        if (sale == null) continue;
                        CityIncome.Skip();
                        ResourceManager.Instance.Add(Mathf.Max(0, sale.price), 0, 0, 0, 0);
                        TellSale(sale, false);
                    }
            }
            else if (res.error != null && res.error.StartsWith("Война окончена"))
            {
                WarManager.Notify("Клановая война окончена — возвращаемся домой", true);
                WarCityMode.Leave(false);
            }
            else if (!saveFailedShown)
            {
                saveFailedShown = true;
                WarManager.Notify("Город на войне не сохранён: " + res.error, false);
            }
            if (done != null) done(res.ok);
        });
    }

    void SubmitLogin()
    {
        if (busy) return;
        ApplyServerAddress();
        busy = true;
        error = "";
        bool rem = remember;
        Backend.Login(login, password, r =>
        {
            busy = false;
            if (r.ok) SignedIn(r, rem, false);
            else error = r.error;
        });
    }

    void SubmitRegister()
    {
        if (busy) return;
        if (password != password2) { error = "Пароли не совпадают"; return; }
        ApplyServerAddress();
        busy = true;
        error = "";
        bool rem = remember;
        Backend.Register(login, displayName, password, r =>
        {
            busy = false;
            if (r.ok) SignedIn(r, rem, true);
            else error = r.error;
        });
    }

    void ApplyServerAddress()
    {
        string a = (serverAddress ?? "").Trim();
        if (a == "") a = DefaultServer;
        if (Backend is OnlineAccountBackend ob && ob.Address == new OnlineAccountBackend(a).Address) return;
        backend = new OnlineAccountBackend(a);
        PlayerPrefs.SetString(ServerKey, a);
        PlayerPrefs.Save();
    }

    void CheckServer()
    {
        if (busy) return;
        ApplyServerAddress();
        busy = true;
        error = note = "";
        Backend.Ping(r =>
        {
            busy = false;
            if (r.ok) note = "Сервер на связи: " + r.error;
            else error = r.error;
        });
    }

    /// <summary>Выйти из аккаунта: город сохраняется, игра закрывается (войти можно при следующем запуске).</summary>
    public void Logout()
    {
        SaveCityNow(true, ok =>
        {
            string t = token;
            Profile = null;
            token = null;
            PlayerPrefs.DeleteKey(TokenKey);
            PlayerPrefs.Save();
            if (!string.IsNullOrEmpty(t)) Backend.Call(Api.Logout, t, new ApiRequest { pushToken = PushNotifications.Token }, _ => Quit());   // уведомления на этот телефон больше не придут
            else Quit();
        });
    }

    // Окно дополнительного игрока Unity (Multiplayer Play Mode): само остановить игру оно не может -
    // после выхода показываем экран «вы вышли», войти снова - перезапустить Play в главном окне
    static bool loggedOut;
    static bool IsVirtualPlayer => Application.isEditor && (Application.dataPath.Replace('\\', '/').Contains("/Library/VP/") || !IsFirstCopy());

    static void Quit()
    {
        if (IsVirtualPlayer) { loggedOut = true; return; }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---------- Сохранение города ----------

    bool CanSave => LoggedIn && !string.IsNullOrEmpty(token) && !deleting && CityBoot.Started
        && BuildingPlacer.Instance != null && BuildingPlacer.Instance.WorldReady && !CityArmy.Restoring;

    /// <summary>Сохранить город и профиль на сервере сейчас.</summary>
    public void SaveCityNow(bool force, System.Action<bool> done = null)
    {
        if (!CanSave || (saving && !force)) { if (done != null) done(false); return; }
        CityData c = BuildingPlacer.Instance.CaptureCity();
        if (c == null) { if (done != null) done(false); return; }

        saving = true;
        saveTimer = AutoSaveSeconds;
        Backend.SaveProfile(token, Profile, null);
        if (WarCityMode.Active) { SaveWarCity(c, done); return; }
        var acks = new System.Collections.Generic.List<string>(appliedReports);
        string cityJson = JsonUtility.ToJson(c);
        // Сначала - копия на устройстве: если запрос на сервер оборвётся (игру закрыли), город не потеряется (sohranenie-1)
        int backup = CityBackup.Write(Profile != null ? Profile.id : null, cityJson, BuildingInstance.RuinClockUtc);
        Backend.SaveCity(token, cityJson, acks, res =>
        {
            saving = false;
            bool ok = res.ok;
            string err = res.error;
            if (ok)
            {
                CityBackup.Confirm(backup);
                // Подтверждённые сервером отчёты больше не нужны; новые - учитываем в городе сейчас
                appliedReports.RemoveAll(acks.Contains);
                if (res.reports != null)
                    foreach (var rep in res.reports)
                    {
                        if (rep == null || string.IsNullOrEmpty(rep.id) || appliedReports.Contains(rep.id)) continue;
                        ApplyReportLive(rep);
                        appliedReports.Add(rep.id);
                        TellReport(rep, false);
                        saveTimer = 0.5f;   // сразу сохраняем город с потерями и подтверждаем отчёт
                    }
                HandleSales(res.sales);
                HandleGrants(res.grants);
                NoteIncoming(res.incomingName);
                lastSavedAt = Time.unscaledTime;
                if (BuildingPlacer.Instance != null) lastSignature = BuildingPlacer.Instance.BuildingSignature();
                if (saveFailedShown) WarManager.Notify("Связь с сервером есть, город сохранён", true);
                saveFailedShown = false;
            }
            else if (!saveFailedShown)
            {
                saveFailedShown = true;
                WarManager.Notify("Город не сохранён: " + err, false);
            }
            if (done != null) done(ok);
        });
    }

    // ---------- Набеги других игроков на нас ----------

    void TellReport(RaidReport r, bool whileAway)
    {
        CityIncome.Skip();
        string killed = r.killed != null && r.killed.Count > 0 ? $", погибло бойцов: {r.killed.Count}" : "";
        string hall = r.hallSacked ? ", Цитадель разграблена" : "";
        if (r.rating != 0)
        {
            hall += $", рейтинг {(r.rating > 0 ? "+" : "")}{r.rating}";
            if (Profile != null) Profile.rating = Mathf.Max(0, Profile.rating + r.rating);
        }
        string items = Spoils.TokensText(r.ruinedAt);   // отнятые сложные вещи (трофеи нападающего)
        if (items != "") killed = $", отняты вещи: {items}" + killed;
        bool repelled = r.loot.Total == 0 && items == "" && (r.killed == null || r.killed.Count == 0) && r.ruined == 0 && !r.hallSacked;
        string shield = r.shieldMinutes > 0 ? $". Щит города на {ShieldRules.Duration(r.shieldMinutes * 60.0)}: другие игроки вас пока не найдут" : "";
        string what = repelled ? "отбились, ничего не унесли" + hall
                    : r.loot.Total == 0 && items != "" ? $"{killed.TrimStart(',', ' ')}{hall}"
                    : $"унесено {r.loot.ShortText}{killed}{hall}";
        string line = $"{(whileAway ? "Пока вас не было, был " : "Был ")}набег игрока {r.attackerName} на вашу деревню: {what}{shield}";
        ReplayViewer.Note(r);   // запись боя (до Notify: окно «Пока вас не было» покажет кнопку повтора)
        if (r.replay) line += ". Бой можно посмотреть: «▶ Повтор»";
        WarManager.Notify(line, repelled);
        raidLog.Insert(0, $"{r.attackerName}: {what}");
        if (raidLog.Count > 8) raidLog.RemoveAt(raidLog.Count - 1);
    }

    // ---------- Рынок: продажи ----------

    /// <summary>Кто-то купил наши лоты: золото - в казну (один раз на продажу), сервер подтвердит при сохранении.</summary>
    public static void HandleSales(System.Collections.Generic.List<MarketSale> sales)
    {
        CityIncome.Skip();
        if (Instance == null || sales == null) return;
        foreach (var sale in sales)
        {
            if (sale == null || string.IsNullOrEmpty(sale.id) || Instance.appliedReports.Contains(sale.id)) continue;
            if (ResourceManager.Instance != null) ResourceManager.Instance.AddMarks(Mathf.Max(0, sale.price));
            Instance.appliedReports.Add(sale.id);
            Instance.TellSale(sale, false);
            SaveSoon();
        }
    }

    // ---------- Награды за клановую войну ----------

    /// <summary>Награды с сервера (купленные марки - StampShop): в казну один раз, сервер подтвердит при сохранении.</summary>
    public static void TakeGrants(System.Collections.Generic.List<WarGrant> grants) { if (Instance != null) Instance.HandleGrants(grants); }

    /// <summary>Война окончилась, пока мы в игре: ресурсы - в казну (один раз), сервер подтвердит при сохранении.</summary>
    void HandleGrants(System.Collections.Generic.List<WarGrant> grants)
    {
        CityIncome.Skip();
        if (grants == null) return;
        foreach (var g in grants)
        {
            if (g == null || string.IsNullOrEmpty(g.id) || appliedReports.Contains(g.id)) continue;
            if (ResourceManager.Instance != null)
            {
                ResourceManager.Instance.Add(g.gold, g.wood, g.stone, g.food, g.iron);
                ResourceManager.Instance.AddMarks(g.marks);
            }
            appliedReports.Add(g.id);
            TellGrant(g);
            saveTimer = 0.5f;
        }
    }

    void TellGrant(WarGrant g)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (g.gold > 0) parts.Add($"{g.gold} золота");
        if (g.wood > 0) parts.Add($"{g.wood} дерева");
        if (g.stone > 0) parts.Add($"{g.stone} камня");
        if (g.food > 0) parts.Add($"{g.food} еды");
        if (g.iron > 0) parts.Add($"{g.iron} железа");
        if (g.marks > 0) parts.Add($"{g.marks} ◈");
        string got = parts.Count > 0 ? ": " + string.Join(", ", parts) : "";
        WarManager.Notify((string.IsNullOrEmpty(g.text) ? "Награда за клановую войну" : g.text) + got, true);
    }

    void TellSale(MarketSale s, bool whileAway)
    {
        string what = s.item > 0 ? (s.name ?? "вещь") : MarketGoods.UnitsText(new GoodId((GoodKind)s.kind, (QualityTier)s.tier), s.units);
        WarManager.Notify($"{(whileAway ? "Пока вас не было, на рынке продано" : "Продано на рынке")}: {what}, покупатель {s.buyerName} — вам {s.price} {(WarCityMode.Active ? "золота" : "марок")}", true);
    }

    /// <summary>Сохранить город в ближайшие полсекунды (после сделки на рынке и т.п.).</summary>
    public static void SaveSoon()
    {
        if (Instance != null && Instance.saveTimer > 0.5f) Instance.saveTimer = 0.5f;
    }

    void NoteIncoming(string name)
    {
        incoming = name;   // о набеге сообщает окно обороны (DefenseWindow)
    }

    // Набег был, пока игра открыта: вычитаем потери из казны, складов и казарм прямо сейчас
    static void ApplyReportLive(RaidReport r)
    {
        if (r == null || r.live) return;   // бой шёл у нас вживую: добычу уже вынесли, бойцы уже погибли
        RaidLoot l = r.loot ?? new RaidLoot();
        var rm = ResourceManager.Instance;
        if (rm != null)
        {
            Take(ref rm.gold, l.gold, b => b.storedGold, (b, v) => b.storedGold = v);
            Take(ref rm.wood, l.wood, b => b.storedWood, (b, v) => b.storedWood = v);
            Take(ref rm.stone, l.stone, b => b.storedStone, (b, v) => b.storedStone = v);
            Take(ref rm.food, l.food, b => b.storedFood, (b, v) => b.storedFood = v);
            Take(ref rm.iron, l.iron, b => b.storedIron, (b, v) => b.storedIron = v);
            Take(ref rm.oats, l.oats, b => b.storedOats, (b, v) => b.storedOats = v);
            Take(ref rm.berries, l.berries, b => b.storedBerries, (b, v) => b.storedBerries = v);
            Take(ref rm.herbs, l.herbs, b => b.storedHerbs, (b, v) => b.storedHerbs = v);
            Take(ref rm.resin, l.resin, b => b.storedResin, (b, v) => b.storedResin = v);
            rm.potions = Mathf.Max(0, rm.potions - l.potions);
        }
        Spoils.RemoveLive(r.ruinedAt);   // трофеи нападающего: отнятые сложные вещи (обновление 23)

        TownHallStorage st = TownHallStorage.Instance;
        if (st != null)
            for (int q = 0; q < 3; q++)
            {
                Spend(st, RefinedKind.Planks, q, RaidLoot.At(l.planks, q));
                Spend(st, RefinedKind.StonePlates, q, RaidLoot.At(l.plates, q));
                Spend(st, RefinedKind.Steel, q, RaidLoot.At(l.steel, q));
                Spend(st, RefinedKind.ReinforcedPlates, q, RaidLoot.At(l.reinforced, q));
            }

        // Погибшие защитники: бойцы того же вида (сначала те, кто дома)
        if (r.killed != null)
            foreach (var name in r.killed)
            {
                CitizenAgent victim = null;
                foreach (var b in BuildingInstance.All)
                {
                    if (b == null) continue;
                    BarracksController br = b.GetComponent<BarracksController>();
                    if (br == null) continue;
                    foreach (var a in br.Soldiers)
                    {
                        if (a == null || a.IsDead || !a.IsSoldier || a.UnitName != name) continue;
                        if (victim == null || (victim.IsOnExpedition && !a.IsOnExpedition)) victim = a;
                    }
                }
                if (victim != null) victim.KillOutright();
            }

        // Разрушенные здания - руины с момента набега
        if (r.ruinedAt != null)
            foreach (var cell in r.ruinedAt)
            {
                string[] a = (cell ?? "").Split(',');
                if (a.Length < 2 || !int.TryParse(a[0], out int x) || !int.TryParse(a[1], out int y)) continue;
                BuildingInstance b = CitySave.BuildingAt(x, y);
                if (b != null) b.RuinFromReport(r.timeUtc);
            }
    }

    static void Take(ref int treasury, int n, System.Func<BuildingInstance, int> get, System.Action<BuildingInstance, int> set)
    {
        int t = Mathf.Min(Mathf.Max(0, treasury), n);
        treasury -= t;
        n -= t;
        foreach (var b in BuildingInstance.All)
        {
            if (n <= 0) break;
            if (b == null) continue;
            int k = Mathf.Min(get(b), n);
            if (k <= 0) continue;
            set(b, get(b) - k);
            n -= k;
        }
    }

    static void Spend(TownHallStorage st, RefinedKind k, int q, int n)
    {
        n = Mathf.Min(n, st.Get(k, (QualityTier)q));
        if (n > 0) st.TrySpend(k, (QualityTier)q, n);
    }

    /// <summary>Сервер: найти противника (для окна набега).</summary>
    public static void FindOpponent(System.Action<ApiResponse> done)
    {
        if (WarCityMode.Active) { done(ApiResponse.Fail("На войне в набеги на игроков не ходят — воюйте на карте")); return; }
        if (Instance == null || !LoggedIn) { done(ApiResponse.Fail("Нужно войти в аккаунт")); return; }
        Backend.FindOpponent(Instance.token, done);
    }

    public static void StartRaid(string raidId, System.Action<ApiResponse> done)
    {
        if (Instance == null || !LoggedIn) { done(ApiResponse.Fail("Нужно войти в аккаунт")); return; }
        Backend.StartRaid(Instance.token, raidId, done);
    }

    public static void FinishRaid(ApiRequest result, System.Action<ApiResponse> done = null)
    {
        if (Instance == null || !LoggedIn) { if (done != null) done(ApiResponse.Fail("Нужно войти в аккаунт")); return; }
        Backend.FinishRaid(Instance.token, result, res =>
        {
            if (!res.ok) WarManager.Notify("Итог набега не отправлен на сервер: " + res.error, false);
            else if (res.profile != null && Profile != null && res.profile.id == Profile.id) NoteRating(res.profile.rating, "за набег");
            if (done != null) done(res);
        });
    }

    /// <summary>Сервер прислал новый рейтинг: запомнить и сказать игроку, на сколько он изменился и не сменилась ли лига.</summary>
    public static void NoteRating(int rating, string why)
    {
        if (Profile == null) return;
        int was = Profile.rating;
        Profile.rating = rating;
        if (rating == was || string.IsNullOrEmpty(why)) return;
        int d = rating - was;
        int lw = Leagues.Of(was), ln = Leagues.Of(rating);
        string league = ln > lw ? $". Новая лига: {Leagues.Name(ln)}!" : ln < lw ? $". Лига: {Leagues.Name(ln)}" : "";
        WarManager.Notify($"Рейтинг {why}: {(d > 0 ? "+" : "")}{d} (теперь {rating}){league}", d > 0);
    }

    // Закрытие игры: сначала сохраняем город, потом выходим
    bool WantsToQuit()
    {
        if (quitSaveDone || !CanSave) return true;
        quitSaveDone = true;
        SaveCityNow(true, _ => Application.Quit());
        return false;
    }

    // В редакторе (выход из Play) - отправляем последнее сохранение, редактор его дождётся
    void OnApplicationQuit()
    {
        if (!quitSaveDone && CanSave) { quitSaveDone = true; SaveCityNow(true); }
    }

    // ---------- Время ----------

    void Update()
    {
        if (loggedOut || TickDeleted()) return;
        TickLink();
        if (link != Link.Online)
        {
            Time.timeScale = 0f;
            return;
        }
        if (screenOpen)
        {
            Time.timeScale = 0f;   // часы игры могли выставить скорость на старте
            Keyboard kb = Keyboard.current;
            if (kb != null && !busy && !StartupScreen.Covering && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
            {
                if (tab == 0) SubmitLogin(); else SubmitRegister();
            }
            return;
        }

        if (systemOpen && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) systemOpen = false;
        if (CanSave) CityIncome.Tick();
        if (CanSave) WarCityMode.Tick();
        if (CanSave && pendingRuined != null) { WarCityMode.SetRuined(pendingRuined); pendingRuined = null; }

        if (!LoggedIn) return;
        Profile.playSeconds += Time.unscaledDeltaTime;

        // Постройки изменились (стройка, снос, перенос, улучшение) - сохраняем сразу
        signatureTimer -= Time.unscaledDeltaTime;
        if (signatureTimer <= 0f && CanSave && !saving && lastSavedAt >= 0f)
        {
            signatureTimer = SignatureCheckSeconds;
            if (BuildingPlacer.Instance.BuildingSignature() != lastSignature && saveTimer > 0.5f) saveTimer = 0.5f;
        }

        // Автосохранение. Обновление 12: и во время боя тоже (враги на карте сохраняются вместе с городом,
        // WarManager.SaveState). Раньше бой не сохранялся: вышел посреди набега - и город возвращался к виду до него,
        // а при входе тот же набег начинался заново (жалобы тестеров: «перезашёл - и всё восстановилось»)
        saveTimer -= Time.unscaledDeltaTime;
        if (saveTimer <= 0f) SaveCityNow(false);
    }

    void LateUpdate()
    {
        if (screenOpen || (link != Link.Online && !loggedOut)) Time.timeScale = 0f;
    }

    // ---------- Экран входа ----------

    // ---------- Системное меню (⚙) ----------

    // Шестерёнка настроек - в самом углу, правее кнопок «Путь» и «Меню»
    public static Rect SystemButtonRect => UiKit.Phone ? new Rect(UiKit.W - 48f, 4f, 42f, 34f) : new Rect(UiKit.W - 42f, 8f, 32f, 30f);
    const float SystemW = 340f;
    int settingsTab;   // 0 - общие, 1 - аккаунт, 2 - журнал сообщений

    Rect SystemPanelRect
    {
        get
        {
            float cw = SystemW - UiKit.Pad * 2f;
            float h = UiKit.WindowH(UiKit.BtnH + 10f + (settingsTab == 0 ? GameSettings.GeneralHeight(cw) : settingsTab == 2 ? MessageLog.TabHeight(cw) : PageHeight(cw)));
            h = Mathf.Min(h, Mathf.Max(220f, UiKit.H - 42f - 96f));   // не до самого низа: там «В бой!» и малые кнопки
            return new Rect(UiKit.W - SystemW - 10f, 42f, SystemW, h);
        }
    }

    /// <summary>Мышь над кнопкой или окном системного меню (карта под ним не кликается).</summary>
    public static bool IsPointerOver(Vector2 guiPoint)
    {
        if (Instance == null || Instance.screenOpen) return false;
        return SystemButtonRect.Contains(guiPoint) || (Instance.systemOpen && Instance.SystemPanelRect.Contains(guiPoint)) || DefenseWindow.IsPointerOver(guiPoint) || ClanWindow.IsPointerOver(guiPoint) || WarView.IsPointerOver(guiPoint)
            || Tutorial.IsPointerOver(guiPoint)    // карточка обучения
            || StampShop.IsPointerOver(guiPoint)   // кнопка и окно магазина марок
            || CityShield.IsPointerOver(guiPoint)  // плашка и окно щита города
            || AwayReport.IsPointerOver(guiPoint)  // окно «Пока вас не было»
            || ReplayViewer.IsOpen                 // повтор набега: всё под затемнением
            || ReportDialog.IsOpen;                // жалоба на игрока: карта под затемнением не кликается
    }

    void DrawSystem()
    {
        if (BuildingPlacer.Instance == null || !BuildingPlacer.Instance.WorldReady) return;
        if (RaidManager.ViewingBattle || WarView.IsOpen) return;
        Rect gb = SystemButtonRect;
        if (systemOpen && UiKit.Behind("settings")) systemOpen = false;   // открыли меню «Город», клан и т.п. - не лежим под ними
        if (UiKit.Button(gb, "", systemOpen ? UiKit.Kind.Primary : UiKit.Kind.Normal, "Настройки: язык, звук, аккаунт, журнал сообщений"))
        {
            systemOpen = !systemOpen;
            if (systemOpen) UiKit.Front("settings");
        }
        Color oc = GUI.color;
        GUI.color = systemOpen ? new Color(0.12f, 0.10f, 0.06f) : UiKit.Text;
        GUI.DrawTexture(new Rect(gb.x + (gb.width - gb.height + 8f) * 0.5f, gb.y + 4f, gb.height - 8f, gb.height - 8f), GameSettings.GearIcon, ScaleMode.ScaleToFit);
        // Точка на шестерёнке: в журнале есть непрочитанные сообщения
        if (MessageLog.Unread > 0 && !(systemOpen && settingsTab == 2))
        {
            GUI.color = UiKit.Accent;
            GUI.DrawTexture(new Rect(gb.xMax - 9f, gb.y + 2f, 7f, 7f), Texture2D.whiteTexture);
        }
        GUI.color = oc;
        if (!systemOpen) return;

        Rect p = SystemPanelRect;
        UiKit.MarkTextWindow();   // поля имени и пароля
        GUI.depth = -5;
        Rect c = UiKit.Window(p, "Настройки", out bool close);
        if (close) { systemOpen = false; return; }
        string logTab = MessageLog.Unread > 0 && settingsTab != 2 ? $"Журнал ({MessageLog.Unread})" : "Журнал";
        settingsTab = UiKit.Tabs(new Rect(c.x, c.y, c.width + 2f, UiKit.BtnH), new[] { "Общие", "Аккаунт", logTab, "Вопросы" }, settingsTab);
        Rect body = new Rect(c.x, c.y + UiKit.BtnH + 10f, c.width, c.height - UiKit.BtnH - 10f);
        // Окно ниже своего содержимого (невысокий экран) - вкладка прокручивается, а не вылезает за низ окна
        if (settingsTab == 0) UiKit.Scroll("settings-general", body, GameSettings.GeneralHeight(body.width), GameSettings.DrawGeneral);
        else if (settingsTab == 2) MessageLog.Draw(body);
        else if (settingsTab == 3) UiKit.Scroll("settings-faq", body, Faq.Height(body.width - 14f), rr => Faq.Draw(new Rect(rr.x, rr.y, Mathf.Min(rr.width, body.width - 14f), rr.height)));
        else UiKit.Scroll("settings-account", body, PageHeight(body.width), DrawPage);
    }

    void DrawLoggedOut()
    {
        UiKit.Apply();
        GUI.depth = -100;
        Color old = GUI.color;
        GUI.color = new Color(0.03f, 0.035f, 0.045f, 1f);
        GUI.DrawTexture(UiKit.FullRect, Texture2D.whiteTexture);
        GUI.color = old;
        float w = Mathf.Min(520f, UiKit.W - 40f);
        Rect r = new Rect((UiKit.W - w) / 2f, UiKit.H / 2f - 70f, w, 140f);
        LocGUI.Label(new Rect(r.x, r.y, w, 30f), "Вы вышли из аккаунта", UiKit.Title);
        LocGUI.Label(new Rect(r.x, r.y + 40f, w, 90f),
            "Это окно дополнительного игрока Unity. Чтобы войти снова (можно другим аккаунтом), остановите игру и нажмите Play в главном окне Unity.",
            new GUIStyle(UiKit.Body) { wordWrap = true });
    }

    void OnGUI()
    {
        if (loggedOut) { DrawLoggedOut(); return; }
        if (DrawDeleted()) return;   // аккаунт удалён - игра закрывается
        if (StartupScreen.Active && link == Link.Lost) StartupScreen.End();   // связь пропала посреди загрузки - обычный экран «Связь потеряна»
        if (StartupScreen.Covering) { DrawStartup(); return; }
        if (link != Link.Online) { DrawSplash(); return; }
        if (!screenOpen)
        {
            UiKit.Apply();
            if (StartupScreen.Active) DrawStartup();   // экран загрузки гаснет, под ним уже город
            else DrawSystem();
            return;
        }
        UiKit.Apply();
        GUI.depth = -100;   // поверх всех окон игры

        StartupScreen.DrawBackdrop(1f, 0.3f);
        // Низкий экран (телефон в альбомной ориентации): название слева, окно входа справа - иначе окно ляжет на название
        bool side = UiKit.H < 600f && UiKit.W >= 640f;
        if (side) StartupScreen.DrawTitle(1f, new Rect(0f, 0f, UiKit.W * 0.5f, UiKit.H));
        else StartupScreen.DrawTitle(1f);

        float w = side ? Mathf.Min(400f, UiKit.W * 0.5f - 24f) : 380f;
        float rows = tab == 0 ? 2 : 4;
        float contentH = UiKit.BtnH + 10f              // вкладки
            + rows * (18f + 28f + 6f)                  // подпись + поле
            + 24f                                      // «Запомнить меня»
            + 36f                                      // ошибка / подсказка
            + UiKit.BtnH + 10f                         // кнопка
            + StoreLoginHeight(w - UiKit.Pad * 2f)     // Google Play Games, правила и политика
            + (editServer ? 18f + 28f + 6f + UiKit.BtnH : (ShowServerRow ? 22f : 0f));   // сервер
        float h = UiKit.WindowH(contentH);
        float top = Mathf.Max(StartupScreen.TitleBottom + 10f, (UiKit.H - h) * 0.5f);
        Rect r = new Rect((UiKit.W - w) * 0.5f, Mathf.Min(top, UiKit.H - h - 10f), w, h);
        if (side) { h = Mathf.Min(h, UiKit.H - 8f); r = new Rect(UiKit.W * 0.5f + (UiKit.W * 0.5f - w) * 0.5f, Mathf.Max(4f, (UiKit.H - h) * 0.5f), w, h); }
        else if (r.y < 4f) { r.y = 4f; r.height = Mathf.Min(h, UiKit.H - 8f); }
        Rect c = UiKit.Window(r, "Вход");
        // Низкий экран телефона: окно не влезает - содержимое листается (пальцем или колесом)
        bool scrollLogin = contentH > c.height + 1f;
        if (scrollLogin)
        {
            loginScroll = UiKit.BeginScrollView(c, loginScroll, new Rect(0f, 0f, c.width - UiKit.ScrollBarW, contentH));
            c = new Rect(0f, 0f, c.width - UiKit.ScrollBarW, contentH);
        }

        float x = c.x, y = c.y, cw = c.width;
        int newTab = UiKit.Tabs(new Rect(x, y, cw + 2f, UiKit.BtnH), new[] { "Вход", "Регистрация" }, tab);
        if (newTab != tab) { tab = newTab; error = ""; }
        y += UiKit.BtnH + 10f;

        bool en = GUI.enabled;
        GUI.enabled = en && !busy;

        DrawStoreLoginTop(ref y, x, cw);   // «Войти через Google Play Games»

        login = Field(ref y, x, cw, "Логин", login, false);
        if (tab == 1) displayName = Field(ref y, x, cw, "Имя в игре", displayName, false);
        password = Field(ref y, x, cw, "Пароль", password, true);
        if (tab == 1) password2 = Field(ref y, x, cw, "Повторите пароль", password2, true);

        remember = LocGUI.Toggle(new Rect(x, y, cw, 22f), remember, " Запомнить меня");
        y += 24f;

        string msg = error != "" ? UiKit.Col(UiKit.BadHex, error) : (note != "" ? UiKit.Col(UiKit.MutedHex, note) : "");
        LocGUI.Label(new Rect(x, y, cw, 36f), msg, UiKit.Small);
        y += 36f;

        string main = busy ? "Подождите…" : (tab == 0 ? "Войти" : "Создать аккаунт");
        if (UiKit.Button(new Rect(x, y, cw, UiKit.BtnH), main, UiKit.Kind.Primary, "Enter"))
        {
            if (tab == 0) SubmitLogin(); else SubmitRegister();
        }
        y += UiKit.BtnH + 10f;
        DrawStoreLegal(ref y, x, cw);   // правила игры и политика конфиденциальности

        // Адрес сервера: обычно не нужен (тестовый сервер запускается вместе с Unity).
        // Доступен и пока идёт попытка входа - иначе неверный адрес не исправить
        GUI.enabled = en;
        if (!editServer && !ShowServerRow) { }
        else if (!editServer)
        {
            Rect sr = new Rect(x, y, cw, 20f);
            if (LocGUI.Button(sr, UiKit.Col(UiKit.MutedHex, "Сервер: " + serverAddress + "  ✎"), UiKit.Small)) editServer = true;
        }
        else
        {
            serverAddress = Field(ref y, x, cw, "Адрес сервера", serverAddress, false);
            float bw = (cw - 8f) * 0.5f;
            if (UiKit.Button(new Rect(x, y, bw, UiKit.BtnH), "Проверить связь", UiKit.Kind.Normal)) CheckServer();
            if (UiKit.Button(new Rect(x + bw + 8f, y, bw, UiKit.BtnH), "Готово", UiKit.Kind.Primary)) { ApplyServerAddress(); editServer = false; }
        }

        if (scrollLogin) GUI.EndScrollView();
        GUI.enabled = en;

        // Клики мимо окна не доходят до окон игры под затемнением
        EventType et = Event.current.type;
        if (et == EventType.MouseDown || et == EventType.MouseUp || et == EventType.ScrollWheel) Event.current.Use();

        if (StartupScreen.Active) DrawStartup();   // экран загрузки гаснет поверх окна входа
    }

    // ---------- Заставка и экран загрузки (StartupScreen), связь с сервером ----------

    /// <summary>Экран запуска: что сейчас делаем и готово ли всё, чтобы открыть вход или город.</summary>
    void DrawStartup()
    {
        UiKit.Apply();
        GUI.depth = -200;   // поверх всего, даже экрана входа
        string status;
        float target;
        bool ready = false, toLogin = false;
        if (link != Link.Online)
        {
            if (linkFails > 0 && !serverSeen) { status = "Сервер не отвечает. Пробуем снова…"; target = 0.2f; }
            else if (serverSeen) { status = "Сервер на связи"; target = 0.4f; }
            else { status = "Подключение к серверу…"; target = 0.3f; }
        }
        else if (screenOpen)
        {
            if (busy || resumeToken != null)
            {
                status = note != "" ? note : "Вход…";
                target = note == "Загрузка города…" || note == "Переход на войну…" ? 0.8f : 0.55f;
            }
            else { status = "Готово"; target = 1f; ready = true; toLogin = true; }   // сохранённой сессии нет - на вход
        }
        else
        {
            status = "Строим город…";
            target = 0.95f;
            ready = (BuildingPlacer.Instance != null && BuildingPlacer.Instance.WorldReady) || StartupScreen.CityTimedOut(cityShownAt);
        }

        Rect free = StartupScreen.Draw(status, target, ready, toLogin);

        // Сервер не отвечает: подсказка, адрес сервера и выход - под названием игры
        if (StartupScreen.Covering && link != Link.Online && linkFails > 0)
            DrawLinkPanel(free.y + 8f, false);
    }

    /// <summary>Связь с сервером потеряна посреди игры: игра на паузе, ждём сервер.</summary>
    void DrawSplash()
    {
        UiKit.Apply();
        GUI.depth = -200;   // поверх всего, даже экрана входа
        StartupScreen.DrawBackdrop(1f, 0.35f);
        float y = StartupScreen.DrawTitle(1f);
        DrawLinkPanel(y + 18f, true);

        EventType et = Event.current.type;
        if (et == EventType.MouseDown || et == EventType.MouseUp || et == EventType.ScrollWheel) Event.current.Use();
    }

    /// <summary>Что со связью, подсказка, адрес сервера и выход. head - с заголовком и бегущей полоской.</summary>
    void DrawLinkPanel(float y, bool head)
    {
        bool lost = link == Link.Lost;
        float w = Mathf.Min(460f, UiKit.W - 40f);
        float x = (UiKit.W - w) / 2f;
        var center = new GUIStyle(UiKit.Body) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
        var small = new GUIStyle(UiKit.Small) { alignment = TextAnchor.MiddleCenter, wordWrap = true };

        string title = "", sub = "";
        if (lost)
        {
            title = UiKit.Col(UiKit.BadHex, "<b>Связь с сервером потеряна</b>");
            int left = Mathf.Max(0, Mathf.CeilToInt(QuitAfterLostSeconds - (Time.unscaledTime - lostAt)));
            sub = "Игра на паузе: без сервера играть нельзя. Как только сервер ответит, игра продолжится сама." +
                  (IsVirtualPlayer ? "" : $"\nЕсли сервер не вернётся, игра закроется через {left} с.");
        }
        else if (linkFails > 0) { title = UiKit.Col(UiKit.WarnHex, "Сервер не отвечает"); sub = Application.isMobilePlatform ? (ShowServerRow ? "Проверьте интернет на телефоне и адрес сервера ниже. Пробуем снова…" : "Проверьте интернет на телефоне. Пробуем снова…") : "Он запущен? Проверьте окно StrategycaServer (или Unity с сервером). Пробуем снова…"; }
        else title = "Подключение к серверу…";

        // Подложка, чтобы текст читался на фоне деревни
        Color old = GUI.color;
        GUI.color = new Color(0.03f, 0.035f, 0.05f, 0.72f);
        GUI.DrawTexture(new Rect(x - 16f, y - 10f, w + 32f, PanelHeight(head, w, small) + 20f), Texture2D.whiteTexture);
        GUI.color = old;

        if (head)
        {
            LocGUI.Label(new Rect(x, y, w, 26f), title, center);
            y += 30f;
            // Полоска бежит, пока ждём сервер
            Rect bar = new Rect(x + w * 0.1f, y, w * 0.8f, 6f);
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            float t = Mathf.Repeat(Time.unscaledTime * 0.6f, 1.3f) - 0.3f;
            float a = Mathf.Max(0f, t), b = Mathf.Min(1f, t + 0.3f);
            GUI.color = lost ? UiKit.Bad : UiKit.Accent;
            if (b > a) GUI.DrawTexture(new Rect(bar.x + bar.width * a, bar.y, bar.width * (b - a), bar.height), Texture2D.whiteTexture);
            GUI.color = old;
            y += 16f;
        }

        if (sub != "")
        {
            float sh = UiKit.TextH(sub, w, small) + 4f;
            LocGUI.Label(new Rect(x, y, w, sh), UiKit.Col(UiKit.MutedHex, sub), small);
            y += sh + 4f;
        }
        if (linkFails > 0 && !string.IsNullOrEmpty(linkError))
        {
            LocGUI.Label(new Rect(x, y, w, 34f), UiKit.Col(UiKit.MutedHex, linkError), small);
            y += 36f;
        }

        // Адрес сервера (тот же, что на экране входа) и выход
        if (linkFails > 0 || lost)
        {
            UiKit.MarkTextWindow();
            if (!editServer && !ShowServerRow) { }
            else if (!editServer)
            {
                if (LocGUI.Button(new Rect(x, y, w, 20f), UiKit.Col(UiKit.MutedHex, "Сервер: " + serverAddress + "  ✎"), small)) editServer = true;
                y += 26f;
            }
            else
            {
                serverAddress = Field(ref y, x, w, "Адрес сервера", serverAddress, false);
                if (UiKit.Button(new Rect(x, y, w, UiKit.BtnH), "Подключиться", UiKit.Kind.Primary, "Проверить связь с этим адресом"))
                {
                    ApplyServerAddress();
                    editServer = false;
                    linkTimer = 0f;
                }
                y += UiKit.BtnH + 8f;
            }
            if (!IsVirtualPlayer && UiKit.Button(new Rect(x + w * 0.25f, y, w * 0.5f, UiKit.BtnH), "Выйти из игры", UiKit.Kind.Ghost, null)) QuitNoServer();
        }
    }

    float PanelHeight(bool head, float w, GUIStyle small)
    {
        bool lost = link == Link.Lost;
        float h = head ? 46f : 0f;
        if (lost || linkFails > 0) h += UiKit.TextH(lost ? "Игра на паузе: без сервера играть нельзя. Как только сервер ответит, игра продолжится сама.\nЕсли сервер не вернётся, игра закроется через 90 с." : "Он запущен? Проверьте окно StrategycaServer (или Unity с сервером). Пробуем снова…", w, small) + 8f;
        if (linkFails > 0 && !string.IsNullOrEmpty(linkError)) h += 36f;
        if (linkFails > 0 || lost) h += (editServer ? 18f + 28f + 6f + UiKit.BtnH + 8f : (ShowServerRow ? 26f : 0f)) + (IsVirtualPlayer ? 0f : UiKit.BtnH);
        return h;
    }

    static string Field(ref float y, float x, float w, string label, string value, bool secret)
    {
        LocGUI.Label(new Rect(x, y, w, 18f), label, UiKit.MutedStyle);
        y += 18f;
        Rect fr = new Rect(x, y, w, 28f);
        if (secret) fr.width -= 40f;   // справа - «глазик»: показать / скрыть пароль
        string v = MobileInput.Field(fr, "Acc" + label, value ?? "", secret ? 32 : 64, null, secret && !showPasswords);   // телефон: удобный ввод (final-1)
        if (secret && UiKit.Button(new Rect(fr.xMax + 4f, y, 36f, 28f), showPasswords ? "⊘" : "◉", UiKit.Kind.Ghost,
                                   showPasswords ? "Скрыть пароль" : "Показать пароль"))
            showPasswords = !showPasswords;
        y += 28f + 6f;
        return v;
    }

    static bool showPasswords;   // «глазик» у полей пароля: один на все поля, сбрасывается при перезапуске

    // ---------- Вкладка «Аккаунт» в окне настроек ----------

    float PageHeight(float w)
    {
        if (!LoggedIn) return 66f;
        return 22f * 6f + 6f + UiKit.BtnH + 8f   // строки профиля + «Сохранить сейчас»
            + (ReplayViewer.Offers.Count > 0 ? UiKit.BtnH + 8f : 0f)   // «Повторы набегов»
            + 18f + 28f + 6f + UiKit.BtnH + 8f   // имя
            + UiKit.SectionH + 2f * (18f + 28f + 6f) + UiKit.BtnH + 8f   // пароль
            + StorePageHeight(w)                 // Google Play, уведомления, заблокированные, удаление аккаунта
            + 22f + UiKit.BtnH;                  // сообщение + выход
    }

    Vector2 pageScroll;

    // Вкладка длиннее окна (маленький экран) - прокручивается
    void DrawPage(Rect r)
    {
        float need = PageHeight(r.width - 16f);
        if (!LoggedIn || need <= r.height) { DrawPageInner(r); return; }
        pageScroll = UiKit.BeginScrollView(r, pageScroll, new Rect(0f, 0f, r.width - 16f, need));
        DrawPageInner(new Rect(0f, 0f, r.width - 16f, need));
        GUI.EndScrollView();
    }

    void DrawPageInner(Rect r)
    {
        float x = r.x, y = r.y, w = r.width;

        if (!LoggedIn)
        {
            LocGUI.Label(new Rect(x, y, w, 60f), "Ты играешь офлайн, город не сохраняется. Чтобы войти в аккаунт, перезапусти игру.", UiKit.Body);
            return;
        }

        UiKit.Row(new Rect(x, y, w, 22f), "Игрок", UiKit.B(Profile.displayName)); y += 22f;
        UiKit.Row(new Rect(x, y, w, 22f), "Логин", Profile.login); y += 22f;
        UiKit.Row(new Rect(x, y, w, 22f), "Создан", Profile.Created == System.DateTime.MinValue ? "—" : Profile.Created.ToLocalTime().ToString("dd.MM.yyyy")); y += 22f;
        int mins = Mathf.FloorToInt(Profile.playSeconds / 60f);
        UiKit.Row(new Rect(x, y, w, 22f), "В игре", mins >= 60 ? $"{mins / 60} ч {mins % 60} мин" : $"{mins} мин"); y += 22f;
        string saved = saving ? "сохраняется…" : lastSavedAt < 0f ? "ещё нет" : $"{Mathf.FloorToInt(Time.unscaledTime - lastSavedAt)} с назад";
        UiKit.Row(new Rect(x, y, w, 22f), "Город сохранён", saved); y += 22f;
        Rect raids = new Rect(x, y, w, 22f);
        UiKit.Row(raids, "Набеги на вас", raidLog.Count == 0 ? "не было" : UiKit.Col(UiKit.BadHex, raidLog.Count.ToString()) + " (наведите)");
        if (raidLog.Count > 0) UiKit.Tip(raids, string.Join("\n", raidLog));
        y += 22f + 6f;
        if (ReplayViewer.Offers.Count > 0)
        {
            if (UiKit.Button(new Rect(x, y, w, UiKit.BtnH), $"▶ Повторы набегов ({ReplayViewer.Offers.Count})", UiKit.Kind.Normal, "Посмотреть, как шли набеги, пока вас не было")) ReplayViewer.OpenList();
            y += UiKit.BtnH + 8f;
        }
        if (UiKit.Button(new Rect(x, y, w, UiKit.BtnH), "Сохранить сейчас", !saving, UiKit.Kind.Normal,
                "Город и так сохраняется сам каждые 30 секунд, и во время боя тоже"))
            SaveCityNow(false, ok => pageMsg = ok ? UiKit.Col(UiKit.GoodHex, "Город сохранён") : UiKit.Col(UiKit.BadHex, "Не удалось сохранить"));
        y += UiKit.BtnH + 8f;

        newName = Field(ref y, x, w, "Имя в игре", newName, false);
        bool nameChanged = newName.Trim() != Profile.displayName && AccountRules.CheckName(newName) == null;
        if (UiKit.Button(new Rect(x, y, w, UiKit.BtnH), "Сохранить имя", nameChanged, UiKit.Kind.Normal,
                nameChanged ? null : AccountRules.CheckName(newName) ?? "Имя не изменилось"))
        {
            Profile.displayName = newName.Trim();
            Backend.SaveProfile(token, Profile, ok => pageMsg = ok ? UiKit.Col(UiKit.GoodHex, "Имя сохранено") : UiKit.Col(UiKit.BadHex, "Не удалось сохранить"));
        }
        y += UiKit.BtnH + 8f;

        y = UiKit.Section(x, y, w, "Пароль");
        // Аккаунт Google Play Games: пароля нет, его можно задать (тогда войти можно и по логину)
        oldPass = Profile.noPassword ? "" : Field(ref y, x, w, "Старый пароль", oldPass, true);
        if (Profile.noPassword) { LocGUI.Label(new Rect(x, y, w, 46f), UiKit.Col(UiKit.MutedHex, $"Вы входите через Google Play Games. Задайте пароль, чтобы входить и по логину {Loc.Keep(Profile.login)}."), UiKit.Small); y += 52f; }
        newPass = Field(ref y, x, w, "Новый пароль", newPass, true);
        if (UiKit.Button(new Rect(x, y, w, UiKit.BtnH), Profile.noPassword ? "Задать пароль" : "Сменить пароль", (oldPass != "" || Profile.noPassword) && newPass != "", UiKit.Kind.Normal, null))
        {
            Backend.ChangePassword(token, oldPass, newPass, res =>
            {
                pageMsg = res.ok ? UiKit.Col(UiKit.GoodHex, "Пароль изменён") : UiKit.Col(UiKit.BadHex, res.error);
                if (res.ok) { oldPass = newPass = ""; if (Profile != null) Profile.noPassword = false; }
            });
        }
        y += UiKit.BtnH + 8f;

        y = DrawStorePage(x, y, w);

        LocGUI.Label(new Rect(x, y, w, 22f), pageMsg, UiKit.Small);
        y += 22f;
        if (UiKit.Button(new Rect(x, y, w, UiKit.BtnH), "Выйти из аккаунта", UiKit.Kind.Danger, "Город сохранится, игра закроется"))
            ConfirmDialog.Ask("Выйти из аккаунта?", "Город сохранится, а игра закроется. Войти снова можно в любой момент.", "Выйти", Logout);
    }
}
