using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Кланы (как в Clash of Clans): кнопка «Клан» вверху справа. Всё про клан в одном окне:
///  - Сверху карточка клана: лига и рейтинг клана, описание, ваша роль, «Выйти».
///  - «Чат»: чат клана на всю ширину.
///  - «Участники»: таблица (кто в сети, роль, Цитадель, лига); справа - что можно сделать с выбранным игроком
///    или, пока никто не выбран, что умеет каждая роль.
///  - «Война»: клановая война (WarPanel) с маленькой картой войны; с неё же - на большую карту.
///  - «Лиги»: рейтинг игрока и клана, мировая таблица (LeaguePanel).
///  - Не в клане: найти клан (поиск по названию) и вступить, или создать свой; лиги видны и без клана.
/// Окно открывается и поверх карты войны: чат и участники под рукой во время войны.
/// Нажать на чужое сообщение - «Пожаловаться» (ReportDialog) и «Заблокировать» (его сообщения больше не видны; список - ⚙ Настройки → Аккаунт).
/// То же - у выбранного игрока во вкладке «Участники».
/// Чат обновляется раз в пару секунд, пока окно открыто; закрыто - раз в 15 секунд (счётчик новых сообщений на кнопке).
/// Создаётся вместе с AccountManager.
/// </summary>
public class ClanWindow : MonoBehaviour
{
    static ClanWindow inst;
    static bool open;

    const float OpenEvery = 2f, ClosedEvery = 15f;

    ClanView clan;
    List<ClanBrief> clans = new List<ClanBrief>();
    readonly List<ClanMessage> chat = new List<ClanMessage>();
    int since;            // номер последнего полученного сообщения
    int seen;             // ... и последнего прочитанного (для счётчика на кнопке)
    bool known;           // хоть раз получили ответ
    bool loading, busy;
    float timer = 3f;
    string msg = "";

    int tab;              // не в клане: 0 - найти, 1 - создать, 2 - лиги
    string search = "", newName = "", newDesc = "", say = "";
    Vector2 listScroll, chatScroll, memberScroll;
    bool stickBottom = true;
    int clanTab;          // в клане: TabChat, TabMembers, TabWar, TabLeagues
    string selectedId;    // выбранный в списке участник (действия под списком)
    int pickedN;          // нажатое сообщение чата (под чатом - «Пожаловаться» и «Заблокировать»)
    readonly WarPanel warPanel = new WarPanel();
    readonly LeaguePanel leaguePanel = new LeaguePanel();
    int escFrame = -1;    // в этом кадре Esc закрыл окно (карта войны его уже не берёт)
    public static WarInfo War => inst != null ? inst.warPanel.Info : null;

    /// <summary>Esc достался окну клана (оно открыто или только что закрылось по Esc).</summary>
    public static bool EatsEscape => inst != null && AccountManager.LoggedIn && (open || inst.escFrame == Time.frameCount);

    public static void CloseWindow() { if (inst != null && open) inst.Close(); }

    bool ChatShown => open && clan != null && clanTab == TabChat;   // чат на экране - сообщения прочитаны
    bool LeaguesShown => open && (clan != null ? clanTab == TabLeagues : tab == 2);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { inst = null; open = false; }

    void Awake() { inst = this; }
    void OnDestroy() { if (inst == this) inst = null; }

    // Дома - левее кнопки «Путь» (тот же зазор 6, что между остальными кнопками); на отдельной карте войны - левее кнопок «Мой город» и «Домой»
    static Rect ButtonRect => WarView.IsOpen ? WarView.ClanButtonRect : new Rect(CityButtonLeft, UiKit.TopBtnY, cityButtonW, UiKit.TopBtnH);

    // Ширина кнопки в городе - по надписи: «Клан ⚔ 12» не влезал в 76 точек
    static float cityButtonW = 76f;

    /// <summary>Левый край кнопки «Клан» в городе (она левее «Путь», ширина которой зависит от надписи).</summary>
    public static float CityButtonLeft => DevelopmentPath.ButtonLeft - 6f - cityButtonW;
    static Rect WindowRect
    {
        get
        {
            float w = Mathf.Min(840f, UiKit.W - 40f), h = Mathf.Min(580f, UiKit.H - 90f);
            if (UiKit.Phone) return UiKit.CenterWindow(840f, 580f);
            return new Rect(UiKit.W - w - 20f, 46f, w, h);
        }
    }

    public static bool IsPointerOver(Vector2 p)
    {
        if (inst == null || !AccountManager.LoggedIn) return false;
        return ButtonRect.Contains(p) || (open && WindowRect.Contains(p));
    }

    // Тревога: свернуть окно
    void OnEnable() { WarManager.OnAlarm += CollapseForAlarm; }
    void OnDisable() { WarManager.OnAlarm -= CollapseForAlarm; }
    bool CollapseForAlarm()
    {
        if (!open) return false;
        open = false;
        return true;
    }

    // ---------- Сервер ----------

    void Update()
    {
        if (!AccountManager.LoggedIn || string.IsNullOrEmpty(AccountManager.Token) || AccountManager.Blocking) return;
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (open && kb != null && kb.escapeKey.wasPressedThisFrame) { Close(); escFrame = Time.frameCount; }
        timer -= Time.unscaledDeltaTime;
        if (timer <= 0f) Refresh();
        if (clan != null || WarView.IsOpen) warPanel.Update(open && clanTab == TabWar);
        leaguePanel.Update(LeaguesShown);
    }

    void Close()
    {
        open = false;
        GUIUtility.keyboardControl = 0;   // снять фокус с поля ввода - горячие клавиши снова работают
    }

    void Call(string path, ApiRequest req, System.Action<ApiResponse> after = null)
    {
        if (busy) return;
        busy = true;
        req.since = since;
        AccountManager.Backend.Clan(path, AccountManager.Token, req, res =>
        {
            busy = false;
            if (!res.ok) { msg = UiKit.Col(UiKit.BadHex, res.error); return; }
            msg = "";
            Take(res);
            if (after != null) after(res);
        });
    }

    void Refresh()
    {
        timer = open ? OpenEvery : ClosedEvery;
        if (loading || busy) return;
        loading = true;
        AccountManager.Backend.Clan(Api.ClanInfo, AccountManager.Token, new ApiRequest { since = since, clanName = search }, res =>
        {
            loading = false;
            if (res.ok) Take(res);
        });
    }

    void Take(ApiResponse res)
    {
        known = true;
        // Каждый ответ клана говорит, в клане ли мы (JsonUtility не передаёт null - пустой клан = «не в клане»)
        bool inClan = res.clan != null && !string.IsNullOrEmpty(res.clan.id);
        if (inClan)
        {
            if (clan != null && clan.id != res.clan.id) { chat.Clear(); since = seen = 0; }
            clan = res.clan;
        }
        else
        {
            clan = null;
            clans = res.clans ?? new List<ClanBrief>();
            chat.Clear();
            since = seen = 0;
        }
        if (res.messages != null)
            foreach (var m in res.messages)
                if (m != null && m.n > since)
                {
                    since = m.n;
                    if (!m.system && Blocked(m.authorId)) continue;   // заблокированный игрок (старый сервер их ещё присылает)
                    chat.Add(m);
                }
        if (chat.Count > Api.ClanChatKeep) chat.RemoveRange(0, chat.Count - Api.ClanChatKeep);
        if (ChatShown) seen = since;
    }

    static bool Blocked(string id) { return AccountManager.Profile != null && AccountManager.Profile.Blocks(id); }

    /// <summary>
    /// Заблокировать (on) или разблокировать игрока: его сообщения в чате клана не видны. done - текст для игрока.
    /// Вызывают чат, список участников и ⚙ Настройки → Аккаунт.
    /// </summary>
    public static void SetBlocked(string id, string name, bool on, System.Action<string> done)
    {
        if (string.IsNullOrEmpty(id) || !AccountManager.LoggedIn) return;
        AccountManager.Backend.Call(Api.ChatBlock, AccountManager.Token, new ApiRequest { memberId = id, role = on ? 1 : 0 }, res =>
        {
            if (!res.ok) { if (done != null) done(UiKit.Col(UiKit.BadHex, res.error)); return; }
            if (AccountManager.Profile != null && res.profile != null) AccountManager.Profile.blocked = res.profile.blocked ?? new List<BlockedPlayer>();
            if (inst != null)
            {
                inst.pickedN = 0;
                if (on) inst.chat.RemoveAll(m => m != null && m.authorId == id);
                else { inst.chat.Clear(); inst.since = inst.seen = 0; inst.timer = 0f; }   // вернуть его сообщения - загрузим чат заново
            }
            string who = Loc.Keep(name ?? "");
            if (done != null) done(UiKit.Col(UiKit.GoodHex, on ? $"{who} заблокирован(а): его сообщений больше не видно" : $"{who} разблокирован(а)"));
        });
    }

    void AskBlock(string id, string name)
    {
        ConfirmDialog.Ask($"Заблокировать {Loc.Keep(name)}?", "Вы больше не увидите сообщения этого игрока в чате клана. Снять блокировку можно в ⚙ Настройки → Аккаунт.", "Заблокировать",
            () => SetBlocked(id, name, true, m => msg = m));
    }

    int Unread()
    {
        if (clan == null) return 0;
        int n = 0;
        string me = AccountManager.Profile != null ? AccountManager.Profile.id : "";
        foreach (var m in chat) if (m.n > seen && m.authorId != me && !m.system) n++;
        return n;
    }

    void Send()
    {
        string t = say.Trim();
        if (t == "" || busy) return;
        say = "";
        stickBottom = true;
        Call(Api.ClanSay, new ApiRequest { text = t });
    }

    // ---------- Окно ----------

    void OnGUI()
    {
        if (!AccountManager.LoggedIn || AccountManager.Blocking) return;
        if (BuildingPlacer.Instance == null || !BuildingPlacer.Instance.WorldReady || RaidManager.ViewingBattle) return;
        UiKit.Apply();
        if (WarView.IsOpen) GUI.depth = -10;   // поверх карты войны

        int unread = Unread();
        bool warOn = WarMapOn;
        string label = "Клан" + (warOn ? $" <color={UiKit.WarnHex}>⚔</color>" : "") + (unread > 0 ? $" <color={UiKit.WarnHex}>{unread}</color>" : "");
        string tip = "Клан, война и лиги: участники, чат, карта войны, мировой рейтинг" + (warOn ? "\nИдёт война: карта войны — во вкладке «Война»" : "");
        if (open && UiKit.Behind("clan")) Close();   // открыли другое большое окно
        if (!WarView.IsOpen) cityButtonW = Mathf.Clamp(UiKit.ButtonStyle(UiKit.Kind.Normal).CalcSize(Loc.Content(label)).x + 4f, 76f, 150f);
        if (UiKit.Button(ButtonRect, label, open ? UiKit.Kind.Primary : warOn ? UiKit.Kind.Primary : UiKit.Kind.Normal, tip))
        {
            if (open) Close();
            else
            {
                open = true; timer = 0f; stickBottom = true;
                UiKit.Front("clan");
                if (WarView.IsOpen && clan != null) clanTab = TabWar;   // с карты войны - сразу к войне
                if (ChatShown) seen = since;
                if (LeaguesShown) leaguePanel.Refresh();
            }
        }
        if (!open) return;

        UiKit.MarkTextWindow();
        GUI.depth = -10;
        CameraController.BlockZoom();
        string title = clan != null ? $"Клан «{clan.name}»" : "Кланы";
        Rect c = UiKit.Window(WindowRect, title, out bool close);
        if (close) { Close(); return; }

        if (!known) { LocGUI.Label(c, UiKit.Col(UiKit.MutedHex, "Загрузка…"), UiKit.Body); return; }
        Rect body = new Rect(c.x, c.y, c.width, c.height - 24f);
        if (clan == null) DrawNoClan(body);
        else DrawClan(body);
        LocGUI.Label(new Rect(c.x, c.yMax - 20f, c.width, 20f), msg, UiKit.Small);
    }

    /// <summary>Идёт война и уже есть карта (со второго дня).</summary>
    static bool WarMapOn
    {
        get
        {
            WarInfo w = War;
            if (w == null || !w.inWar) return false;
            WarStage s = (WarStage)w.stage;
            return s >= WarStage.Prepare && s < WarStage.Finished;
        }
    }

    void DrawNoClan(Rect r)
    {
        int nt = UiKit.Tabs(new Rect(r.x, r.y, Mathf.Min(r.width, 420f), UiKit.BtnH), new[] { "Найти клан", "Создать клан", "Лиги" }, tab);
        if (nt != tab) { tab = nt; msg = ""; if (tab == 2) leaguePanel.Refresh(); }
        float y = r.y + UiKit.BtnH + 10f;
        if (tab == 2) { leaguePanel.Draw(new Rect(r.x, y, r.width, r.yMax - y)); return; }

        // Зачем нужен клан - одной строкой
        string why = "В клане есть общий чат, клановые войны и рейтинг клана. Вступите в готовый клан или создайте свой.";
        float wh = UiKit.TextH(why, r.width, UiKit.Small);
        LocGUI.Label(new Rect(r.x, y, r.width, wh), UiKit.Col(UiKit.InfoHex, why), UiKit.Small);
        y += wh + 8f;

        if (tab == 1)
        {
            LocGUI.Label(new Rect(r.x, y, r.width, 20f), "Название (3–20 символов):", UiKit.Small); y += 20f;
            newName = MobileInput.Field(new Rect(r.x, y, Mathf.Min(r.width, 320f), 26f), "ClanNewName", newName, 20); y += 34f;
            LocGUI.Label(new Rect(r.x, y, r.width, 20f), "Описание (необязательно):", UiKit.Small); y += 20f;
            newDesc = MobileInput.Field(new Rect(r.x, y, r.width, 26f), "ClanNewDesc", newDesc, 120); y += 36f;
            bool can = newName.Trim().Length >= 3 && !busy;
            if (UiKit.Button(new Rect(r.x, y, 220f, 32f), "Создать клан", can, UiKit.Kind.Primary, can ? "Вы станете главой клана" : "Название — от 3 символов"))
                Call(Api.ClanCreate, new ApiRequest { clanName = newName, text = newDesc }, _ => { newName = newDesc = ""; chatScroll = Vector2.zero; });
            return;
        }

        LocGUI.Label(new Rect(r.x, y + 3f, 70f, 22f), "Поиск:", UiKit.Body);
        float sw = Mathf.Min(260f, r.width - 190f);
        search = MobileInput.Field(new Rect(r.x + 70f, y, sw, 26f), "ClanSearch", search, 20);
        // Кнопка сразу за полем (на узком окне поле короче - кнопка раньше лежала на нём)
        if (UiKit.Button(new Rect(r.x + 70f + sw + 10f, y, 110f, 26f), loading ? "…" : "Найти", !loading, UiKit.Kind.Normal, null)) { timer = 0f; Refresh(); }
        y += 34f;

        Rect view = new Rect(r.x, y, r.width, r.yMax - y);
        if (clans.Count == 0)
        {
            LocGUI.Label(view, UiKit.Col(UiKit.MutedHex, "Кланов пока нет (или все полны). Создайте свой во вкладке «Создать клан»."), UiKit.Body);
            return;
        }
        const float rowH = 46f;
        listScroll = UiKit.BeginScrollView(view, listScroll, new Rect(0f, 0f, view.width - 16f, clans.Count * rowH));
        float w = view.width - 16f;
        GUIStyle line = new GUIStyle(UiKit.Body) { wordWrap = false, clipping = TextClipping.Clip };
        GUIStyle small = new GUIStyle(UiKit.Small) { wordWrap = false, clipping = TextClipping.Clip };
        for (int i = 0; i < clans.Count; i++)
        {
            ClanBrief b = clans[i];
            float y0 = i * rowH;
            LocGUI.Box(new Rect(0f, y0, w, rowH - 4f), GUIContent.none);
            LocGUI.Label(new Rect(10f, y0 + 3f, w - 140f, 22f), $"<b>{Loc.Keep(b.name)}</b>  {UiKit.Col(UiKit.MutedHex, $"{b.members}/{Api.MaxClanMembers}")}   {Leagues.Label(Leagues.Of(b.rating))} {UiKit.Col(UiKit.MutedHex, b.rating.ToString())}", line);
            LocGUI.Label(new Rect(10f, y0 + 22f, w - 140f, 18f), string.IsNullOrEmpty(b.description) ? UiKit.Col(UiKit.MutedHex, "без описания") : Loc.Keep(UiKit.Col(UiKit.MutedHex, b.description)), small);
            if (UiKit.Button(new Rect(w - 120f, y0 + 8f, 110f, 26f), "Вступить", !busy, UiKit.Kind.Primary, null))
                Call(Api.ClanJoin, new ApiRequest { clanId = b.id }, _ => { stickBottom = true; });
        }
        GUI.EndScrollView();
    }

    // Роль участника (старый сервер присылает только «глава»)
    static int RoleOf(ClanMember m)
    {
        if (m == null) return ClanRoles.Member;
        if (m.role > 0) return m.role;
        return m.leader ? ClanRoles.Leader : ClanRoles.Member;
    }

    // ---------- В клане ----------
    // Сверху - карточка клана (название, лига, ваша роль, выйти), под ней вкладки:
    // «Чат» (на всю ширину), «Участники» (таблица + справа что можно сделать с выбранным), «Война», «Лиги».

    const int TabChat = 0, TabMembers = 1, TabWar = 2, TabLeagues = 3;

    void DrawClan(Rect r)
    {
        string me = AccountManager.Profile != null ? AccountManager.Profile.id : "";
        ClanMember self = clan.members.Find(m => m.id == me);
        int myRole = RoleOf(self);

        float y = DrawClanHeader(r, myRole);

        WarInfo wi = warPanel.Info;
        bool atWar = wi != null && wi.inWar;
        int online = 0;
        foreach (var m in clan.members) if (m.online) online++;
        int unread = Unread();
        string[] labels =
        {
            "Чат" + (unread > 0 && clanTab != TabChat ? $" {UiKit.Col(UiKit.WarnHex, unread.ToString())}" : ""),
            $"Участники {UiKit.Col(UiKit.MutedHex, clan.members.Count.ToString())}",
            atWar ? $"Война {UiKit.Col(UiKit.WarnHex, "●")}" : "Война",
            "Лиги",
        };
        int nt = UiKit.Tabs(new Rect(r.x, y, r.width, UiKit.BtnH), labels, clanTab);
        if (nt != clanTab)
        {
            clanTab = nt; msg = "";
            if (clanTab == TabLeagues) leaguePanel.Refresh();
            if (clanTab == TabChat) { stickBottom = true; seen = since; }
        }
        y += UiKit.BtnH + 10f;
        Rect body = new Rect(r.x, y, r.width, r.yMax - y);

        switch (clanTab)
        {
            case TabMembers: DrawMembers(body, me, myRole, online); break;
            case TabWar: warPanel.Draw(body, clan); break;
            case TabLeagues: leaguePanel.Draw(body); break;
            default: DrawChat(body, online); break;
        }
    }

    /// <summary>Карточка клана над вкладками. Возвращает y под ней.</summary>
    float DrawClanHeader(Rect r, int myRole)
    {
        const float h = 52f;
        Rect box = new Rect(r.x, r.y, r.width, h);
        LocGUI.Box(box, GUIContent.none);
        Color old = GUI.color;
        GUI.color = new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.08f);
        GUI.DrawTexture(new Rect(box.x + 2f, box.y + 2f, box.width - 4f, box.height - 4f), Texture2D.whiteTexture);
        GUI.color = old;

        // Справа: ваша роль и «Выйти»
        const float leaveW = 92f;
        Rect leave = new Rect(box.xMax - leaveW - 10f, box.y + (h - 26f) / 2f, leaveW, 26f);
        GUIStyle right = new GUIStyle(UiKit.Small) { alignment = TextAnchor.MiddleRight, wordWrap = false };
        Rect roleR = new Rect(leave.x - 170f, box.y, 160f, h);
        LocGUI.Label(roleR, "Вы: " + UiKit.Col(RoleHex(myRole), "<b>" + ClanRoles.Name(myRole) + "</b>"), right);
        UiKit.Tip(roleR, ClanRoles.Name(myRole) + "\n" + RoleRights(myRole));
        if (UiKit.Button(leave, "Выйти", !busy, UiKit.Kind.Danger,
                myRole == ClanRoles.Leader && clan.members.Count > 1 ? "Выйти из клана. Главой станет заместитель (или другой участник)" : "Выйти из клана"))
        {
            string text = clan.members.Count <= 1 ? "Вы последний в клане — клан будет распущен."
                : myRole == ClanRoles.Leader ? "Вы глава. Главой станет заместитель, а если его нет — старейшина или другой участник."
                : "Вы останетесь без клана. Вернуться можно, если в клане будет место.";
            ConfirmDialog.Ask($"Выйти из клана «{clan.name}»?", text, "Выйти", () => Call(Api.ClanLeave, new ApiRequest()));
        }

        // Слева: лига клана и описание
        float tw = roleR.x - box.x - 20f;
        int lg = Leagues.Of(clan.rating);
        GUIStyle line = new GUIStyle(UiKit.Body) { wordWrap = false, clipping = TextClipping.Clip };
        Rect lr = new Rect(box.x + 12f, box.y + 6f, tw, 20f);
        LocGUI.Label(lr, $"{Leagues.Label(lg)}  {UiKit.Col(UiKit.MutedHex, "рейтинг клана " + clan.rating)}", line);
        UiKit.Tip(lr, "Лига клана. Рейтинг клан получает за клановые войны");
        GUIStyle small = new GUIStyle(UiKit.Small) { wordWrap = false, clipping = TextClipping.Clip };
        string desc = string.IsNullOrEmpty(clan.description)
            ? UiKit.Col(UiKit.MutedHex, "Без описания")
            : Loc.Keep(UiKit.Col(UiKit.MutedHex, clan.description));   // слова игроков не переводим
        Rect dr = new Rect(box.x + 12f, box.y + 27f, tw, 18f);
        LocGUI.Label(dr, desc, small);
        if (!string.IsNullOrEmpty(clan.description)) UiKit.Tip(dr, Loc.Keep(clan.description));
        return box.yMax + 8f;
    }

    static string RoleHex(int role)
    {
        return role == ClanRoles.Leader ? UiKit.AccentHex : role == ClanRoles.CoLeader ? UiKit.InfoHex
             : role == ClanRoles.Elder ? UiKit.WarnHex : UiKit.MutedHex;
    }

    // ---------- Чат ----------

    // Сообщения игроков - главное: плашка, имя цветом роли, текст крупнее. Свои - с золотой полоской слева.
    // Служебные строки («вступил», «повысил»…) - мелко, бледно и по центру: видны, но не мешают читать.
    // Объявления войны и лиги - тоже служебные, но чуть ярче (их важно не пропустить).
    // Подряд идущие сообщения одного игрока (до 5 минут между ними) - без повторного имени.

    const float ChatPadX = 9f, ChatPadY = 5f, ChatNameH = 17f, ChatGap = 6f, ChatGapSame = 2f;
    const double ChatGroupMinutes = 5.0;

    struct ChatLine
    {
        public ClanMessage m;
        public bool header;      // показывать имя и время
        public bool important;   // служебная, но заметная
        public string name, text;
        public float y, h, gapAbove;
    }

    readonly List<ChatLine> chatLines = new List<ChatLine>();

    static bool ImportantSystem(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return text.StartsWith("Война") || text.Contains("войну") || text.Contains("лигу");
    }

    static bool ChatTime(ClanMessage m, out System.DateTime t)
    {
        bool ok = System.DateTime.TryParse(m.timeUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out t);
        if (ok) t = t.ToLocalTime();
        return ok;
    }

    void DrawChat(Rect r, int online)
    {
        LocGUI.Label(new Rect(r.x, r.y - 2f, r.width, 18f),
            UiKit.Col(UiKit.GoodHex, "●") + UiKit.Col(UiKit.MutedHex, $" в сети {online} из {clan.members.Count}"), UiKit.Small);
        float top = r.y + 18f;
        Rect view = new Rect(r.x, top, r.width, r.yMax - top - UiKit.BtnH - 8f);
        LocGUI.Box(view, GUIContent.none);
        Rect inside = new Rect(view.x + 6f, view.y + 6f, view.width - 10f, view.height - 12f);
        float inner = inside.width - 14f;
        float textW = inner - ChatPadX * 2f - 4f;

        GUIStyle msgStyle = new GUIStyle(UiKit.Body) { fontSize = 14 };
        GUIStyle nameStyle = new GUIStyle(UiKit.Small) { wordWrap = false, clipping = TextClipping.Clip, fontSize = 12 };
        GUIStyle sysStyle = new GUIStyle(UiKit.Small) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Italic };

        string me = AccountManager.Profile != null ? AccountManager.Profile.id : "";
        var roles = new Dictionary<string, int>();
        foreach (var cm in clan.members) if (cm != null && cm.id != null) roles[cm.id] = RoleOf(cm);

        // Раскладка
        chatLines.Clear();
        float total = 0f;
        ClanMessage prev = null;
        System.DateTime prevT = default;
        bool prevHasT = false;
        foreach (var m in chat)
        {
            bool hasT = ChatTime(m, out System.DateTime t);
            string time = hasT ? t.ToString("HH:mm") : "";
            var line = new ChatLine { m = m };
            if (m.system)
            {
                line.important = ImportantSystem(m.text);
                line.text = line.important
                    ? UiKit.Col(UiKit.WarnHex, (time != "" ? time + " · " : "") + m.text)
                    : UiKit.Col(UiKit.MutedHex + "A0", (time != "" ? time + " · " : "") + m.text);
                line.gapAbove = line.important ? ChatGap + 2f : 3f;
                line.h = UiKit.TextH(line.text, inner - 40f, sysStyle) + (line.important ? 4f : 0f);
            }
            else
            {
                bool same = prev != null && !prev.system && prev.authorId == m.authorId
                    && hasT && prevHasT && (t - prevT).TotalMinutes < ChatGroupMinutes;
                line.header = !same;
                int role = m.authorId != null && roles.TryGetValue(m.authorId, out int rr) ? rr : ClanRoles.Member;
                string hex = m.authorId == me ? UiKit.AccentHex : role == ClanRoles.Member ? UiKit.InfoHex : RoleHex(role);
                line.name = Loc.Keep($"{UiKit.Col(hex, "<b>" + m.author + "</b>")}   {UiKit.Col(UiKit.MutedHex, time)}");
                line.text = Loc.Keep(m.text);   // слова игроков не переводим
                line.gapAbove = same ? ChatGapSame : ChatGap;
                line.h = (line.header ? ChatNameH : 0f) + UiKit.TextH(line.text, textW, msgStyle) + ChatPadY * 2f - (line.header ? 2f : 0f);
            }
            if (chatLines.Count == 0) line.gapAbove = 2f;
            total += line.gapAbove;
            line.y = total;
            total += line.h;
            chatLines.Add(line);
            prev = m; prevT = t; prevHasT = hasT;
        }
        total += 4f;

        if (stickBottom) chatScroll.y = 1e6f;   // GUI сам упрёт в низ (с запасом под последнюю строку)
        Vector2 before = chatScroll;
        chatScroll = UiKit.BeginScrollView(inside, chatScroll, new Rect(0f, 0f, inner, Mathf.Max(total, inside.height)));
        Color old = GUI.color;
        for (int i = 0; i < chatLines.Count; i++)
        {
            ChatLine l = chatLines[i];
            if (l.y + l.h < chatScroll.y - 4f || l.y > chatScroll.y + inside.height + 4f) continue;   // вне экрана
            if (l.m.system)
            {
                Rect sr = new Rect(20f, l.y + (l.important ? 2f : 0f), inner - 40f, l.h);
                if (l.important)
                {
                    GUI.color = new Color(UiKit.Warn.r, UiKit.Warn.g, UiKit.Warn.b, 0.07f);
                    GUI.DrawTexture(new Rect(0f, l.y, inner, l.h + 2f), Texture2D.whiteTexture);
                    GUI.color = old;
                }
                LocGUI.Label(sr, l.text, sysStyle);
                continue;
            }

            bool mine = l.m.authorId == me;
            bool picked = !mine && l.m.n == pickedN;
            // Подряд идущие сообщения одного автора - одна плашка без щели
            float bgTop = l.y - (l.header ? 0f : l.gapAbove);
            Rect bg = new Rect(0f, bgTop, inner, l.y + l.h - bgTop);
            GUI.color = mine ? new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.10f) : picked ? new Color(UiKit.Info.r, UiKit.Info.g, UiKit.Info.b, 0.16f) : new Color(1f, 1f, 1f, 0.05f);
            GUI.DrawTexture(bg, Texture2D.whiteTexture);
            // Нажали на чужое сообщение - под чатом «Пожаловаться» и «Заблокировать»
            if (!mine && !string.IsNullOrEmpty(l.m.authorId) && GUI.Button(new Rect(0f, l.y, inner, l.h), GUIContent.none, GUIStyle.none))
                pickedN = picked ? 0 : l.m.n;
            if (mine)
            {
                GUI.color = new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.75f);
                GUI.DrawTexture(new Rect(0f, bgTop, 3f, bg.height), Texture2D.whiteTexture);
            }
            GUI.color = old;

            float yy = l.y + ChatPadY - (l.header ? 2f : 0f);
            if (l.header)
            {
                LocGUI.Label(new Rect(ChatPadX + 2f, yy, textW, ChatNameH), l.name, nameStyle);
                yy += ChatNameH;
            }
            LocGUI.Label(new Rect(ChatPadX + 2f, yy, textW, l.y + l.h - yy), l.text, msgStyle);
        }
        if (chatLines.Count == 0)
            LocGUI.Label(new Rect(0f, 0f, inner, 40f), UiKit.Col(UiKit.MutedHex, "Сообщений пока нет. Поздоровайтесь!"), UiKit.Body);
        GUI.EndScrollView();
        if (chatScroll.y != before.y) stickBottom = chatScroll.y >= total - inside.height - 4f;   // прокрутили вверх - не дёргаем вниз

        // Выбрано сообщение: вместо поля ввода - что сделать с его автором
        ClanMessage pick = pickedN > 0 ? chat.Find(m => m != null && m.n == pickedN && !m.system && m.authorId != me) : null;
        if (pick == null) pickedN = 0;
        else
        {
            Rect ar = new Rect(r.x, r.yMax - UiKit.BtnH, r.width, UiKit.BtnH);
            float bw = Mathf.Min(170f, (ar.width - 16f) / 3f);
            string pid = pick.authorId, pname = pick.author;
            int pn = pick.n;
            GUIStyle who = new GUIStyle(UiKit.Small) { alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip };
            LocGUI.Label(new Rect(ar.x, ar.y, ar.width - bw * 3f - 16f, ar.height), Loc.Keep(UiKit.Col(UiKit.MutedHex, "Сообщение: ") + UiKit.B(pname)), who);
            if (UiKit.Button(new Rect(ar.xMax - bw * 3f - 16f, ar.y, bw, ar.height), "⚑ Пожаловаться", UiKit.Kind.Normal, "Сообщить модератору: оскорбления, спам, мошенничество"))
            { pickedN = 0; ReportDialog.Open(pid, pname, pn, m => msg = m); }
            if (UiKit.Button(new Rect(ar.xMax - bw * 2f - 8f, ar.y, bw, ar.height), "⊘ Заблокировать", UiKit.Kind.Danger, "Не видеть сообщения этого игрока"))
            { pickedN = 0; AskBlock(pid, pname); }
            if (UiKit.Button(new Rect(ar.xMax - bw, ar.y, bw, ar.height), "Отмена", UiKit.Kind.Ghost, null)) pickedN = 0;
            return;
        }

        // Чат закрыт модератором - писать нельзя
        if (AccountManager.Profile != null && AccountManager.Profile.Muted)
        {
            double left = (AccountManager.Profile.MutedUntil - System.DateTime.UtcNow).TotalSeconds;
            string until = left > 365 * 86400.0 ? "Модератор закрыл вам чат за нарушение правил" : $"Модератор закрыл вам чат ещё на {ShieldRules.Duration(left)} — на ваши сообщения жаловались";
            LocGUI.Label(new Rect(r.x, r.yMax - UiKit.BtnH, r.width, UiKit.BtnH), UiKit.Col(UiKit.WarnHex, until), new GUIStyle(UiKit.Small) { alignment = TextAnchor.MiddleCenter });
            return;
        }

        // Ввод: Enter - отправить
        Rect fr = new Rect(r.x, r.yMax - UiKit.BtnH, r.width - 128f, UiKit.BtnH);
        Event e = Event.current;
        if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && GUI.GetNameOfFocusedControl() == "ClanSay")
        {
            Send();
            e.Use();
        }
        // На телефоне - своё поле (MobileInput): строку видно над клавиатурой, курсор ставится пальцем (final-1)
        say = MobileInput.Field(fr, "ClanSay", say, Api.ClanMessageMax, MobileInput.On ? "Написать клану…" : null);
        if (MobileInput.Submitted("ClanSay")) Send();
        if (!MobileInput.On && say == "" && GUI.GetNameOfFocusedControl() != "ClanSay")
            LocGUI.Label(new Rect(fr.x + 6f, fr.y, fr.width - 12f, fr.height), UiKit.Col(UiKit.MutedHex, "Написать клану…"),
                new GUIStyle(UiKit.Body) { alignment = TextAnchor.MiddleLeft, wordWrap = false });
        if (UiKit.Button(new Rect(fr.xMax + 8f, fr.y, 120f, UiKit.BtnH), "Отправить", say.Trim() != "" && !busy, UiKit.Kind.Primary, "Или Enter")) Send();
    }

    // ---------- Участники ----------

    // Порядок в списке: глава, заместители, старейшины, участники; внутри - кто в сети, потом по рейтингу
    static int MemberOrder(ClanMember a, ClanMember b)
    {
        int c = RoleOf(b).CompareTo(RoleOf(a));
        if (c != 0) return c;
        c = b.online.CompareTo(a.online);
        if (c != 0) return c;
        return b.rating.CompareTo(a.rating);
    }

    void DrawMembers(Rect r, string me, int myRole, int online)
    {
        var list = new List<ClanMember>(clan.members);
        list.Sort(MemberOrder);
        ClanMember sel = list.Find(m => m.id == selectedId && m.id != me);
        if (sel == null) selectedId = null;

        float sideW = Mathf.Clamp(r.width * 0.36f, 220f, 300f);
        float lw = r.width - sideW - 14f;

        // Шапка таблицы
        float colRole = lw * 0.44f, colHall = lw * 0.66f, colLeague = lw * 0.78f;
        GUIStyle head = new GUIStyle(UiKit.Small) { wordWrap = false, clipping = TextClipping.Clip };
        string hc = UiKit.MutedHex;
        LocGUI.Label(new Rect(r.x + 26f, r.y, colRole - 26f, 18f), UiKit.Col(hc, $"Игрок · в сети {online}/{clan.members.Count}, мест {Api.MaxClanMembers}"), head);
        LocGUI.Label(new Rect(r.x + colRole, r.y, colHall - colRole, 18f), UiKit.Col(hc, "Роль"), head);
        LocGUI.Label(new Rect(r.x + colHall, r.y, colLeague - colHall, 18f), UiKit.Col(hc, "Ратуша"), head);
        LocGUI.Label(new Rect(r.x + colLeague, r.y, lw - colLeague, 18f), UiKit.Col(hc, "Лига"), head);
        UiKit.Line(r.x, r.y + 19f, lw);

        const float rowH = 28f;
        float top = r.y + 22f;
        Rect mv = new Rect(r.x, top, lw, r.yMax - top);
        float iw = list.Count * rowH > mv.height ? lw - 16f : lw;
        memberScroll = UiKit.BeginScrollView(mv, memberScroll, new Rect(0f, 0f, iw, list.Count * rowH));
        GUIStyle cell = new GUIStyle(UiKit.Body) { wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft };
        GUIStyle rowBtn = new GUIStyle(GUIStyle.none);
        for (int i = 0; i < list.Count; i++)
        {
            ClanMember m = list[i];
            int role = RoleOf(m);
            bool mine = m.id == me;
            Rect lr = new Rect(0f, i * rowH, iw, rowH - 2f);
            Color oc = GUI.color;
            float a = m.id == selectedId ? 0.20f : mine ? 0.08f : (i % 2 == 0 ? 0.035f : 0f);
            if (a > 0f)
            {
                Color tint = m.id == selectedId || mine ? UiKit.Accent : Color.white;
                GUI.color = new Color(tint.r, tint.g, tint.b, a);
                GUI.DrawTexture(lr, Texture2D.whiteTexture);
                GUI.color = oc;
            }
            string dot = m.online ? UiKit.Col(UiKit.GoodHex, "●") : UiKit.Col(UiKit.MutedHex, "○");
            string name = Loc.Keep(mine ? $"<b>{m.name}</b>" : m.name) + (mine ? UiKit.Col(UiKit.MutedHex, " (вы)") : "");
            int lg = Leagues.Of(m.rating);
            LocGUI.Label(new Rect(lr.x + 6f, lr.y, 18f, lr.height), dot, cell);
            LocGUI.Label(new Rect(lr.x + 26f, lr.y, colRole - 30f, lr.height), name, cell);
            LocGUI.Label(new Rect(colRole, lr.y, colHall - colRole, lr.height), UiKit.Col(RoleHex(role), ClanRoles.Mark(role) + " " + ClanRoles.Name(role)), cell);
            LocGUI.Label(new Rect(colHall, lr.y, colLeague - colHall, lr.height), "ур. " + m.hall, cell);
            LocGUI.Label(new Rect(colLeague, lr.y, iw - colLeague, lr.height), $"<color={Leagues.Hex(lg)}>{Leagues.Mark(lg)}</color> {m.rating}", cell);
            UiKit.Tip(lr, (m.online ? "В игре" : "Не в игре") + $"\nЛига: {Leagues.Name(lg)} ({m.rating})" + (!mine ? "\nЩёлкните, чтобы выбрать" : ""));
            if (!mine && GUI.Button(lr, GUIContent.none, rowBtn)) selectedId = selectedId == m.id ? null : m.id;
        }
        GUI.EndScrollView();

        // Справа: выбранный игрок или подсказка про роли
        Rect side = new Rect(r.xMax - sideW, r.y, sideW, r.height);
        LocGUI.Box(side, GUIContent.none);
        Rect sc = new Rect(side.x + 10f, side.y + 8f, side.width - 20f, side.height - 16f);
        if (sel != null) DrawMemberActions(sc, sel, myRole);
        else DrawRolesHelp(sc, myRole);
    }

    void DrawRolesHelp(Rect r, int myRole)
    {
        float y = UiKit.Section(r.x, r.y, r.width, "Роли в клане");
        int[] roles = { ClanRoles.Leader, ClanRoles.CoLeader, ClanRoles.Elder, ClanRoles.Member };
        foreach (int role in roles)
        {
            string text = UiKit.Col(RoleHex(role), $"<b>{ClanRoles.Mark(role)} {ClanRoles.Name(role)}</b>") + "\n" + UiKit.Col(UiKit.MutedHex, RoleRights(role));
            float h = UiKit.TextH(text, r.width, UiKit.Small);
            if (y + h > r.yMax - 40f) break;
            LocGUI.Label(new Rect(r.x, y, r.width, h), text, UiKit.Small);
            y += h + 6f;
        }
        string hint = myRole > ClanRoles.Member
            ? "Щёлкните игрока в списке, чтобы повысить, понизить или исключить его."
            : "Щёлкните игрока в списке, чтобы посмотреть его роль.";
        LocGUI.Label(new Rect(r.x, r.yMax - 36f, r.width, 36f), UiKit.Col(UiKit.InfoHex, hint), UiKit.Small);
    }

    void DrawMemberActions(Rect r, ClanMember m, int myRole)
    {
        int role = RoleOf(m);
        string name = m.name, id = m.id;
        int lg = Leagues.Of(m.rating);

        GUIStyle t = new GUIStyle(UiKit.Title) { wordWrap = false, clipping = TextClipping.Clip };
        LocGUI.Label(new Rect(r.x, r.y, r.width - 24f, 24f), Loc.Keep(m.name), t);
        if (UiKit.Button(new Rect(r.xMax - 22f, r.y + 1f, 22f, 22f), "×", UiKit.Kind.Ghost, "Снять выбор")) { selectedId = null; return; }
        float y = r.y + 26f;
        string info = UiKit.Col(RoleHex(role), ClanRoles.Mark(role) + " " + ClanRoles.Name(role)) + UiKit.Col(UiKit.MutedHex, "  ·  ") +
                      (m.online ? UiKit.Col(UiKit.GoodHex, "в игре") : UiKit.Col(UiKit.MutedHex, "не в игре")) + "\n" +
                      UiKit.Col(UiKit.MutedHex, $"Цитадель ур. {m.hall}  ·  ") + Leagues.Label(lg) + UiKit.Col(UiKit.MutedHex, $" ({m.rating})");
        float ih = UiKit.TextH(info, r.width, UiKit.Small);
        LocGUI.Label(new Rect(r.x, y, r.width, ih), info, UiKit.Small);
        y += ih + 8f;

        // Внизу всегда: пожаловаться и заблокировать (может любой участник)
        float sb = (r.width - 6f) * 0.5f, sy = r.yMax - UiKit.BtnH;
        bool isBlocked = Blocked(id);
        if (UiKit.Button(new Rect(r.x, sy, sb, UiKit.BtnH), "⚑ Пожаловаться", UiKit.Kind.Normal, "Сообщить модератору о нарушении правил"))
            ReportDialog.Open(id, name, 0, t => msg = t);
        if (UiKit.Button(new Rect(r.x + sb + 6f, sy, sb, UiKit.BtnH), isBlocked ? "Разблокировать" : "⊘ Заблокировать", UiKit.Kind.Normal,
                isBlocked ? "Снова видеть сообщения этого игрока" : "Не видеть сообщения этого игрока"))
        {
            if (isBlocked) SetBlocked(id, name, false, t => msg = t);
            else AskBlock(id, name);
        }

        y = UiKit.Section(r.x, y, r.width, "Действия");
        int up = role + 1, down = role - 1;
        bool canUp = up < ClanRoles.Leader && ClanRoles.CanSet(myRole, role, up);
        bool canDown = down >= ClanRoles.Member && ClanRoles.CanSet(myRole, role, down);
        bool canLead = myRole == ClanRoles.Leader;
        bool canKick = ClanRoles.CanKick(myRole, role);
        if (!canUp && !canDown && !canLead && !canKick)
        {
            string why = myRole == ClanRoles.Member
                ? "Участник не может менять роли. Повысить вас может старейшина или выше."
                : "Менять роль можно только тем, у кого роль ниже вашей.";
            LocGUI.Label(new Rect(r.x, y, r.width, 54f), UiKit.Col(UiKit.MutedHex, why), UiKit.Small);
            return;
        }

        float bh = UiKit.BtnH;
        if (canUp && UiKit.Button(new Rect(r.x, y, r.width, bh), "▲ Повысить: " + ClanRoles.Name(up), !busy, UiKit.Kind.Normal, RoleRights(up)))
            ConfirmDialog.Ask($"Повысить {name}?", $"Новая роль: {ClanRoles.Name(up)}.\n{RoleRights(up)}", "Повысить",
                () => Call(Api.ClanRole, new ApiRequest { memberId = id, role = up }), UiKit.Kind.Primary);
        if (canUp) y += bh + 6f;
        if (canDown && UiKit.Button(new Rect(r.x, y, r.width, bh), "▼ Понизить: " + ClanRoles.Name(down), !busy, UiKit.Kind.Normal, null))
            ConfirmDialog.Ask($"Понизить {name}?", $"Новая роль: {ClanRoles.Name(down)}.", "Понизить",
                () => Call(Api.ClanRole, new ApiRequest { memberId = id, role = down }));
        if (canDown) y += bh + 6f;
        if (canLead && UiKit.Button(new Rect(r.x, y, r.width, bh), "★ Сделать главой", !busy, UiKit.Kind.Normal, "Передать главенство: вы станете заместителем"))
            ConfirmDialog.Ask($"Передать главенство {name}?", "Игрок станет главой клана, а вы — заместителем. Вернуть главенство сможет только новый глава.", "Передать",
                () => Call(Api.ClanRole, new ApiRequest { memberId = id, role = ClanRoles.Leader }));
        if (canLead) y += bh + 6f;
        if (canKick && UiKit.Button(new Rect(r.x, y + 6f, r.width, bh), "Исключить из клана", !busy, UiKit.Kind.Danger, null))
            ConfirmDialog.Ask($"Исключить {name} из клана?", "Игрок сразу покинет клан. Вступить снова он сможет сам, если будет место.", "Исключить",
                () => { selectedId = null; Call(Api.ClanKick, new ApiRequest { memberId = id }); });
    }

    static string RoleRights(int role)
    {
        switch (role)
        {
            case ClanRoles.Leader: return "Может всё: начинает войну, назначает и снимает любые роли, передаёт главенство.";
            case ClanRoles.CoLeader: return "Начинает войну, расставляет города, исключает старейшин и участников.";
            case ClanRoles.Elder: return "Может исключать участников.";
            default: return "Общается в чате и воюет вместе с кланом.";
        }
    }
}
