using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Вкладка «Война» в окне клана: начать войну (глава), поиск соперника, расписание по дням,
/// схема расстановки городов (первый день, глава), счёт, итоги, маленькая карта войны и кнопка «На карту войны».
/// Сама большая карта и живая военная деревня - WarView и WarCityMode.
/// </summary>
public class WarPanel
{
    public WarInfo Info { get; private set; }
    bool loading, busy;
    float timer;
    string msg = "";
    string pick;          // глава выбрал игрока - теперь щёлкает место на схеме
    float contentH = 2000f;   // высота содержимого вкладки с прошлого кадра (для прокрутки)

    public void Update(bool visible)
    {
        timer -= Time.unscaledDeltaTime;
        if (timer > 0f || loading || !AccountManager.LoggedIn || string.IsNullOrEmpty(AccountManager.Token)) return;
        timer = visible ? 3f : 20f;
        loading = true;
        AccountManager.Backend.Clan(Api.WarInfo, AccountManager.Token, new ApiRequest(), res =>
        {
            loading = false;
            if (res.ok && res.war != null) Info = res.war;
        });
    }

    void Call(string path, ApiRequest req)
    {
        if (busy) return;
        busy = true;
        AccountManager.Backend.Clan(path, AccountManager.Token, req, res =>
        {
            busy = false;
            if (!res.ok) { msg = UiKit.Col(UiKit.BadHex, res.error); return; }
            msg = path == Api.WarAdmin && !string.IsNullOrEmpty(res.error) ? UiKit.Col(UiKit.GoodHex, res.error) : "";
            if (res.war != null) Info = res.war;
        });
    }

    static string Dur(float s)
    {
        int t = Mathf.Max(0, Mathf.CeilToInt(s));
        int d = t / 86400, h = t % 86400 / 3600, m = t % 3600 / 60, sec = t % 60;
        if (d > 0) return $"{d} д {h} ч";
        if (h > 0) return $"{h} ч {m} мин";
        if (m > 0) return $"{m} мин {sec} с";
        return $"{sec} с";
    }

    /// <summary>Вкладка целиком в прокрутке: на телефоне окно клана низкое, а схема и правила длинные.</summary>
    public void Draw(Rect r, ClanView clan)
    {
        Rect body = new Rect(r.x, r.y, r.width, r.height - 22f);
        UiKit.Scroll("clan.war", body, Mathf.Max(40f, contentH), c => { contentH = DrawBody(c, clan) - c.y + 6f; });
        LocGUI.Label(new Rect(r.x, r.yMax - 20f, r.width, 20f), msg, UiKit.Small);
    }

    static Color LeagueColor(int lg) { return ColorUtility.TryParseHtmlString(Leagues.Hex(lg), out Color c) ? c : UiKit.Accent; }
    static readonly Color MineC = new Color(0.45f, 0.70f, 1f), FoeC = new Color(1f, 0.45f, 0.38f);

    // final-13-fix-3: вкладка оформлена карточками: рейтинг, «клан против клана» со счётом, дни войны, что делать сейчас
    float DrawBody(Rect r, ClanView clan)
    {
        WarInfo w = Info;
        float y = r.y;

        // Рейтинг клана и ваш - две карточки
        int cl = Leagues.Of(clan.rating);
        bool two = AccountManager.Profile != null;
        float cw = two ? (r.width - 10f) * 0.5f : r.width;
        GameUi.StatCard(new Rect(r.x, y, cw, 58f), "ui_crown", "Рейтинг клана", clan.rating.ToString(), Leagues.Label(cl), LeagueColor(cl),
            "Клан получает рейтинг за клановые войны: победа — больше, поражение — меньше. Лиги и мировая таблица — во вкладке «Лиги»");
        if (two)
        {
            int pr = AccountManager.Profile.rating, pl = Leagues.Of(pr);
            GameUi.StatCard(new Rect(r.x + cw + 10f, y, cw, 58f), "ui_star", "Ваш рейтинг", pr.ToString(), Leagues.Label(pl), LeagueColor(pl),
                "Ваш рейтинг растёт за набеги на игроков и за клановые войны");
        }
        y += 66f;
        if (w == null) { LocGUI.Label(new Rect(r.x, y, r.width, 22f), UiKit.Col(UiKit.MutedHex, "Загрузка…"), UiKit.Body); return y + 24f; }

        WarStage st = (WarStage)w.stage;
        bool war = w.inWar && st >= WarStage.Planning && st < WarStage.Finished;
        if (!war) return DrawNoWar(r, y, w, st);

        // Справа - маленькая карта войны (со 2-го дня): щелчок - на большую карту
        float mapBottom = y;
        if (!string.IsNullOrEmpty(w.plots) && w.rows > 0 && r.width > 480f)
        {
            WarGeo g = w.Geo;
            float mw = Mathf.Min(230f, r.width * 0.34f);
            float mh = mw * g.Rows / Mathf.Max(1, g.Cols);
            if (mh > 260f) { mw *= 260f / mh; mh = 260f; }
            Rect mr = new Rect(r.xMax - mw, y, mw, mh);
            DrawMap(mr, w);
            LocGUI.Label(new Rect(mr.x, mr.yMax + 2f, mr.width, 16f), UiKit.Col(UiKit.MutedHex, "нажмите — большая карта"), new GUIStyle(UiKit.Small) { alignment = TextAnchor.MiddleCenter, fontSize = 10 });
            mapBottom = mr.yMax + 22f;
            r = new Rect(r.x, r.y, r.width - mw - 14f, r.height);
        }

        // Клан против клана
        string me = w.mySide == 1 ? w.nameA : w.nameB, them = w.mySide == 1 ? w.nameB : w.nameA;
        int myScore = w.mySide == 1 ? w.scoreA : w.scoreB, theirScore = w.mySide == 1 ? w.scoreB : w.scoreA;
        y = DrawVersus(r, y, me, them, myScore, theirScore, st >= WarStage.Prepare, w.fast);

        // Дни войны и время
        y = DrawDays(r, y, w, st);
        if (w.admin) y = DrawAdmin(r, y, w);

        // Что делать сейчас
        y = GameUi.InfoRow(r.x, y, r.width, "ui_info", "Что делать сейчас", StageHint(st, w.leader), UiKit.Accent, true) + 8f;

        if (st >= WarStage.Prepare)
        {
            if (w.sources != null && w.sources.Count > 0 && !string.IsNullOrEmpty(w.plots))
            {
                int mineN = 0, theirN = 0;
                foreach (var src in w.sources)
                {
                    int o = src != null && src.plot >= 0 && src.plot < w.plots.Length ? w.plots[src.plot] - '0' : 0;
                    if (o == w.mySide) mineN++; else if (o != 0) theirN++;
                }
                GameUi.IconLine(new Rect(r.x, y, r.width, 22f), "ui_flag",
                    $"Источники ресурсов: {UiKit.Col(UiKit.GoodHex, "<b>" + mineN + "</b> у нас")} · {UiKit.Col(UiKit.BadHex, "<b>" + theirN + "</b> у врага")} · {UiKit.Col(UiKit.MutedHex, "ничьих " + (w.sources.Count - mineN - theirN))}",
                    new GUIStyle(UiKit.Body) { wordWrap = false, clipping = TextClipping.Clip, richText = true });
                UiKit.Tip(new Rect(r.x, y, r.width, 22f), "Лесопилки, каменоломни, рудники и деревни на нейтральных землях. Займите участок — доход пойдёт в деревни клана");
                y += 28f;
            }
            bool there = WarCityMode.Active || WarView.IsOpen;
            bool myVillage = WarView.MyCityInWar;
            string label = there ? "К моей деревне" : myVillage ? "На войну: моя деревня" : "Смотреть карту войны";
            string tip = there ? "Показать свою деревню (Пробел)"
                       : myVillage ? "Перейти в свою военную деревню: она живёт посреди общей карты, казармы учат бойцов, они выходят на карту. Домой вернётся только рейтинг клана"
                              : "Вашей деревни нет на этой войне — можно смотреть карту, счёт и бои";
            if (UiKit.Button(new Rect(r.x, y, Mathf.Min(r.width, 280f), 38f), label, !GameRestart.Running, UiKit.Kind.Primary, tip))
                GoToMap();
            y += 48f;
        }

        if (st == WarStage.Planning) y = DrawScheme(r, y, w);
        else y = DrawRules(r, y);
        y = Mathf.Max(y, mapBottom);

        return DrawLog(r.x, y, r.width, w);
    }

    static float DrawVersus(Rect r, float y, string me, string them, int myScore, int theirScore, bool scored, bool fast)
    {
        Rect c = new Rect(r.x, y, r.width, 72f);
        GameUi.Card(c, UiKit.Accent, 1f, 0.15f, false);
        float mid = c.center.x;
        var nameL = new GUIStyle(UiKit.Body) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = true, clipping = TextClipping.Clip, richText = true };
        var nameR = new GUIStyle(nameL) { alignment = TextAnchor.MiddleRight };
        var cap = new GUIStyle(UiKit.Small) { fontSize = 10, wordWrap = false, richText = true };
        var capR = new GUIStyle(cap) { alignment = TextAnchor.UpperRight };
        float side = (c.width - 120f) * 0.5f;
        LocGUI.Label(new Rect(c.x + 12f, c.y + 6f, side - 12f, 14f), UiKit.Col(UiKit.InfoHex, "ваш клан"), cap);
        LocGUI.Label(new Rect(c.x + 12f, c.y + 18f, side - 12f, 46f), UiKit.Col(UiKit.InfoHex, Loc.Keep(me)), nameL);
        LocGUI.Label(new Rect(c.xMax - side, c.y + 6f, side - 12f, 14f), UiKit.Col(UiKit.BadHex, "соперник"), capR);
        LocGUI.Label(new Rect(c.xMax - side, c.y + 18f, side - 12f, 46f), UiKit.Col(UiKit.BadHex, Loc.Keep(them)), nameR);
        GameUi.Icon(new Rect(mid - 14f, c.y + 4f, 28f, 28f), "ui_swords");
        var score = new GUIStyle(UiKit.Body) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, richText = true };
        string sc = scored ? $"{UiKit.Col(UiKit.InfoHex, myScore.ToString())} : {UiKit.Col(UiKit.BadHex, theirScore.ToString())}" : UiKit.Col(UiKit.MutedHex, "— : —");
        Rect sr = new Rect(mid - 60f, c.y + 30f, 120f, 34f);
        GUI.Label(sr, sc, score);
        UiKit.Tip(sr, "Счёт: занятые участки карты и по 2 очка за каждую целую деревню. Считается со 2-го дня");
        if (fast) LocGUI.Label(new Rect(mid - 60f, c.yMax - 14f, 120f, 14f), UiKit.Col(UiKit.WarnHex, "быстрая война"), new GUIStyle(cap) { alignment = TextAnchor.MiddleCenter });
        return c.yMax + 8f;
    }

    // Семь дней войны полоской: прошедшие - тусклые, сегодня - подсвечен; под ней - этап и время
    static float DrawDays(Rect r, float y, WarInfo w, WarStage st)
    {
        int days = WarRules.Days;
        float gap = 4f, dw = (r.width - gap * (days - 1)) / days;
        var ds = new GUIStyle(UiKit.Small) { alignment = TextAnchor.MiddleCenter, wordWrap = false, fontStyle = FontStyle.Bold, richText = true };
        for (int d = 1; d <= days; d++)
        {
            Rect dr = new Rect(r.x + (d - 1) * (dw + gap), y, dw, 24f);
            bool now = d == w.day, past = d < w.day;
            Color c = d == 1 ? UiKit.Muted : d == 2 ? UiKit.Info : d <= 4 ? UiKit.Accent : UiKit.Bad;
            GameUi.Pill(dr, now ? c : UiKit.Muted, past ? 0.45f : now ? 1f : 0.8f);
            if (now)
            {
                Color old = GUI.color;
                GUI.color = new Color(c.r, c.g, c.b, 0.35f);
                if (Event.current.type == EventType.Repaint) GUI.DrawTexture(new Rect(dr.x + 4f, dr.y + 4f, dr.width - 8f, dr.height - 8f), Texture2D.whiteTexture);
                GUI.color = old;
            }
            GUI.Label(dr, past ? UiKit.Col(UiKit.MutedHex, d.ToString()) : d.ToString(), ds);
            UiKit.Tip(dr, DayTip(d));
        }
        y += 28f;
        GameUi.IconLine(new Rect(r.x, y, r.width, 22f), "ui_hourglass",
            $"{UiKit.Col(UiKit.AccentHex, "<b>" + WarRules.StageName(st) + "</b>")}  {UiKit.Col(UiKit.MutedHex, $"следующий этап через {Dur(w.stageLeft)} · до конца войны {Dur(w.warLeft)}")}",
            new GUIStyle(UiKit.Body) { wordWrap = false, clipping = TextClipping.Clip, richText = true });
        return y + 28f;
    }

    static string DayTip(int d)
    {
        if (d == 1) return "День 1 — глава расставляет деревни клана на схеме";
        if (d == 2) return "День 2 — открылась карта: подготовка на своей земле";
        if (d <= 4) return $"День {d} — открыты нейтральные земли с источниками";
        return $"День {d} — открыта вся карта, можно идти на вражеские деревни";
    }

    static string StageHint(WarStage st, bool leader)
    {
        switch (st)
        {
            case WarStage.Planning:
                return leader ? "Расставьте деревни клана на схеме ниже: сильные — в передний столбец, к врагу, слабые — назад. Не успеете — сервер расставит сам."
                              : "Глава расставляет деревни клана. Завтра откроется общая карта — заходите в свою военную деревню.";
            case WarStage.Prepare:
                return "Ходить можно только по своей земле. Зайдите в военную деревню: пусть казармы учат бойцов, мастерские делают доски для таранов. Подарите ресурсы соседям с края.";
            case WarStage.Neutral:
                return "Нейтральные земли открыты. Займите участки с источниками — их доход идёт в деревни клана. Держите отряды у своих границ.";
            case WarStage.Open:
                return "Открыта вся карта — можно идти на вражеские деревни. Снесёте все вражеские Цитадели — победа сразу. Каждый занятый участок и целая деревня — очки.";
            default:
                return "";
        }
    }

    // Админ сервера: перейти на любой день войны (для проверки) или закончить её
    float DrawAdmin(Rect r, float y, WarInfo w)
    {
        LocGUI.Label(new Rect(r.x, y + 3f, 110f, 22f), UiKit.Col(UiKit.WarnHex, "Админ: день"), UiKit.Body);
        float x = r.x + 110f;
        for (int d = 1; d <= WarRules.Days; d++)
        {
            bool now = d == w.day;
            string tip = d == 1 ? "Начало войны: расстановка городов (только пока нет карты)" : $"Перейти на начало {d}-го дня";
            if (UiKit.Button(new Rect(x, y, 34f, 26f), d.ToString(), !busy && !now, now ? UiKit.Kind.Primary : UiKit.Kind.Normal, tip))
                Call(Api.WarAdmin, new ApiRequest { day = d });
            x += 38f;
        }
        if (UiKit.Button(new Rect(x + 8f, y, 110f, 26f), "Закончить", !busy, UiKit.Kind.Danger, "Закончить войну сейчас: победит тот, у кого больше очков"))
            ConfirmDialog.Ask("Закончить войну сейчас?", "Победит клан, у которого больше очков. Вернуть войну будет нельзя.", "Закончить", () => Call(Api.WarAdmin, new ApiRequest { day = WarRules.Days + 1 }));
        return y + 32f;
    }

    float DrawNoWar(Rect r, float y, WarInfo w, WarStage st)
    {
        if (!string.IsNullOrEmpty(w.result))
        {
            bool won = w.winner == w.mySide, draw = w.winner == 0;
            string head = draw ? "Прошлая война: ничья" : won ? "Прошлая война: победа!" : "Прошлая война: поражение";
            y = GameUi.InfoRow(r.x, y, r.width, won ? "ui_crown" : draw ? "ui_flag" : "ui_skull", head, w.result, draw ? UiKit.Accent : won ? UiKit.Good : UiKit.Bad, true) + 8f;
        }
        if (w.searching || st == WarStage.Searching)
        {
            y = GameUi.InfoRow(r.x, y, r.width, "ui_hourglass", "Ищем клан-соперника…", "Война начнётся, как только другой клан тоже нажмёт «Начать войну».", UiKit.Warn, true) + 8f;
            if (w.leader && UiKit.Button(new Rect(r.x, y, 220f, 30f), "Отменить поиск", !busy, UiKit.Kind.Ghost, null))
                ConfirmDialog.Ask("Отменить поиск соперника?", "Клан перестанет искать войну. Начать поиск снова можно в любой момент.", "Отменить поиск", () => Call(Api.WarCancel, new ApiRequest()));
            y += 40f;
        }
        else
        {
            y = GameUi.InfoRow(r.x, y, r.width, "ui_swords", "Сейчас войны нет",
                w.leader ? "Нажмите «Начать войну» — сервер найдёт клан-соперника вашего уровня. Война длится 7 дней, в ней участвует весь клан."
                         : "Начать войну может глава клана или заместитель. Война длится 7 дней, в ней участвует весь клан.", UiKit.Accent, true) + 8f;
            if (w.leader)
            {
                if (UiKit.Button(new Rect(r.x, y, Mathf.Min(r.width, 280f), 38f), "Начать войну", !busy, UiKit.Kind.Primary, "Сервер найдёт клан-соперника. Война длится 7 дней"))
                    ConfirmDialog.Ask("Начать войну?", "Сервер найдёт клан-соперника. Война длится 7 дней, все участники клана будут в ней.", "Начать войну", () => Call(Api.WarStart, new ApiRequest()), UiKit.Kind.Primary);
                y += 48f;
            }
        }
        return DrawRules(r, y);
    }

    // final-13-fix-3: правила - не стена серого текста, а шаги со значками
    static readonly string[,] Rules =
    {
        { "ui_flag",     "День 1 — расстановка", "Глава ставит деревни клана на схеме: кто впереди, кто сзади." },
        { "ui_hammer",   "День 2 — живые деревни", "Открывается общая карта. На ней копия деревни каждого игрока, и она живёт как дома: жители добывают, казармы учат бойцов, бойцы сами выходят на карту. Строить нельзя, ресурсы свои, лимиты склада как дома." },
        { "ui_scroll",   "Дни 3–4 — нейтральные земли", "Открываются земли с источниками: деревни, лесопилки, каменоломни, рудники. Занятый участок даёт доход деревням клана." },
        { "ui_swords",   "Дни 5–7 — вся карта", "Можно идти на вражеские деревни. Снесите все вражеские Цитадели — победа сразу." },
        { "ui_star",     "Счёт", "Занятые участки и по 2 очка за каждую целую деревню. В конце побеждает клан, у которого больше очков." },
        { "ui_bell",     "Помогайте своим", "Дарите ресурсы соседям по клану (особенно деревням с края) и торгуйте на рынке клана." },
        { "ui_crown",    "Что получаете дома", "Домой с войны возвращается только рейтинг клана. Домашняя деревня, её ресурсы и жители от войны не меняются." },
    };

    static float DrawRules(Rect r, float y)
    {
        y = UiKit.Section(r.x, y, r.width, "Как идёт клановая война (7 дней)");
        for (int i = 0; i < Rules.GetLength(0); i++)
            y = GameUi.InfoRow(r.x, y, r.width, Rules[i, 0], Rules[i, 1], Rules[i, 2], i == 0 ? UiKit.Muted : i == 1 ? UiKit.Info : i <= 3 ? UiKit.Warn : UiKit.Accent) + 2f;
        return y + 8f;
    }

    float DrawScheme(Rect r, float y, WarInfo w)
    {
        y = UiKit.Section(r.x, y, r.width, w.leader ? "Схема: выберите игрока, потом место" : "Схема (расставляет глава или заместитель)");
        if (w.slots == null) return y;
        int gc = Mathf.Max(1, w.gc);
        int slots = Mathf.Max(1, w.Geo.Slots);
        int gridRows = Mathf.Max(1, (slots + gc - 1) / gc);
        float cell = UiKit.Phone ? 34f : 30f;
        float cw = Mathf.Min(240f, (r.width - 4f) / gc);
        float gx = r.x;

        // Города клана стоят сеткой вплотную друг к другу (как на карте); сбоку - нейтральные земли, за ними враг
        bool frontRight = w.mySide != 2;   // первый клан слева от середины карты: передний столбец - справа
        string side = gc > 1 ? (frontRight ? ", враг - справа" : ", враг - слева") : "";
        LocGUI.Label(new Rect(gx, y, r.width, 18f), UiKit.Col(UiKit.MutedHex, "города клана, как на карте" + side), UiKit.Small);
        y += 18f;
        for (int row = gridRows - 1; row >= 0; row--)
        {
            for (int col = 0; col < gc; col++)
            {
                int c = frontRight ? gc - 1 - col : col;   // c = 0 - столбец у нейтральных земель
                int slot = row * gc + c;
                if (slot >= slots) continue;
                WarSlot s = w.slots.Find(x => x.slot == slot);
                Rect cr = new Rect(gx + col * cw, y, cw - 4f, cell - 4f);
                string label = s != null ? $"{s.name} {UiKit.Col(UiKit.MutedHex, "ур." + s.hall)}" : UiKit.Col(UiKit.MutedHex, "—");
                bool picked = s != null && s.accountId == pick;
                if (w.leader && pick != null)
                {
                    if (UiKit.Button(cr, label, !busy, picked ? UiKit.Kind.Primary : UiKit.Kind.Ghost, "Поставить сюда"))
                    {
                        Call(Api.WarScheme, new ApiRequest { memberId = pick, slot = slot });
                        pick = null;
                    }
                }
                else if (w.leader && s != null)
                {
                    if (UiKit.Button(cr, label, !busy, UiKit.Kind.Normal, "Выбрать, чтобы переставить")) pick = s.accountId;
                }
                else
                {
                    LocGUI.Box(cr, GUIContent.none);
                    LocGUI.Label(new Rect(cr.x + 6f, cr.y, cr.width - 8f, cr.height), label, new GUIStyle(UiKit.Small) { alignment = TextAnchor.MiddleLeft, wordWrap = false });
                }
            }
            y += cell;
        }
        y += 4f;
        string hint = pick != null ? UiKit.Col(UiKit.AccentHex, "Щёлкните место для выбранного игрока (занятое место — поменяются местами).")
                                   : UiKit.Col(UiKit.MutedHex, (w.gc > 1 ? "Города переднего столбца первыми встречают врага, задние и средние прикрыты соседями." : "Города в середине столбца прикрыты соседями, крайние — открыты с края.") + " Если глава не расставит, сервер расставит сам.");
        float hw = pick != null ? r.width - 110f : r.width;
        LocGUI.Label(new Rect(r.x, y, hw, 34f), hint, UiKit.Small);
        if (pick != null && UiKit.Button(new Rect(r.x + r.width - 104f, y + 2f, 104f, 26f), "Отмена", UiKit.Kind.Ghost, "Не переставлять этого игрока")) pick = null;
        return y + 38f;
    }

    static void GoToMap()
    {
        ClanWindow.CloseWindow();   // окно не закрывает карту
        WarView.Open();
    }

    // Маленькая карта: участки сторон, города (свой - обведён), павшие - серые
    static void DrawMap(Rect mr, WarInfo w)
    {
        Color old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(mr.x - 2f, mr.y - 2f, mr.width + 4f, mr.height + 4f), Texture2D.whiteTexture);
        WarGeo g = w.Geo;
        int cols = g.Cols, rows = g.Rows;
        float kx = mr.width / cols, ky = mr.height / rows;
        Color mineC = new Color(0.30f, 0.55f, 0.95f), foeC = new Color(0.88f, 0.28f, 0.22f), neutral = new Color(0.30f, 0.42f, 0.24f);
        for (int py = 0; py < rows; py++)
            for (int px = 0; px < cols; px++)
            {
                int i = py * cols + px;
                int owner = i < w.plots.Length ? w.plots[i] - '0' : 0;
                Color c = owner == 0 ? neutral : owner == w.mySide ? mineC : foeC;
                if (owner != 0) c = Color.Lerp(neutral, c, 0.75f);
                GUI.color = c;
                GUI.DrawTexture(new Rect(mr.x + px * kx, mr.yMax - (py + 1) * ky, kx - 0.5f, ky - 0.5f), Texture2D.whiteTexture);
            }
        string tip = "Карта войны: синие участки - ваши, красные - врага, квадратики - источники ресурсов.\nЩёлкните, чтобы открыть большую карту";
        if (w.sources != null)
            foreach (var src in w.sources)
            {
                if (src == null) continue;
                int o = src.plot >= 0 && src.plot < w.plots.Length ? w.plots[src.plot] - '0' : 0;
                int spx = src.plot % cols, spy = src.plot / cols;
                Rect sr = new Rect(mr.x + spx * kx + kx * 0.3f, mr.yMax - (spy + 1) * ky + ky * 0.3f, kx * 0.4f, ky * 0.4f);
                GUI.color = new Color(0.1f, 0.08f, 0.04f, 0.9f);
                GUI.DrawTexture(new Rect(sr.x - 1f, sr.y - 1f, sr.width + 2f, sr.height + 2f), Texture2D.whiteTexture);
                GUI.color = o == 0 ? new Color(0.95f, 0.80f, 0.35f) : o == w.mySide ? new Color(0.62f, 0.80f, 1f) : new Color(1f, 0.55f, 0.45f);
                GUI.DrawTexture(sr, Texture2D.whiteTexture);
                if (sr.Contains(Event.current.mousePosition))
                    tip = WarRules.SourceName(src.kind) + (o == 0 ? " — ничей" : o == w.mySide ? " — ваш клан" : " — у врага");
            }
        if (w.cities != null)
            foreach (var c in w.cities)
            {
                if (c == null) continue;
                float n = WarRules.CityPlots;
                Rect cr = new Rect(mr.x + c.plotX * kx + kx * 0.15f, mr.yMax - (c.plotY + n) * ky + ky * 0.15f, n * kx - kx * 0.3f, n * ky - ky * 0.3f);
                if (c.mine)
                {
                    GUI.color = Color.white;
                    GUI.DrawTexture(new Rect(cr.x - 2f, cr.y - 2f, cr.width + 4f, cr.height + 4f), Texture2D.whiteTexture);
                }
                GUI.color = c.fallen ? Color.gray : c.side == w.mySide ? new Color(0.62f, 0.80f, 1f) : new Color(1f, 0.55f, 0.45f);
                GUI.DrawTexture(cr, Texture2D.whiteTexture);
                if (cr.Contains(Event.current.mousePosition)) tip = Loc.Keep(c.name) + (c.mine ? " (вы)" : "") + (c.fallen ? " — город пал" : "");
            }
        GUI.color = old;
        UiKit.Tip(mr, tip);
        if (GUI.Button(mr, GUIContent.none, GUIStyle.none)) GoToMap();
    }

    // События: последние 12 (вся вкладка и так листается), полосками со значком
    static GUIStyle logSt;
    float DrawLog(float x, float y, float width, WarInfo w)
    {
        if (w.log == null || w.log.Count == 0) return y;
        y = UiKit.Section(x, y, width, "События");
        if (logSt == null) logSt = new GUIStyle(UiKit.Small) { wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft, richText = true };
        float lh = 22f;
        int n = Mathf.Min(12, w.log.Count);
        Color old = GUI.color;
        for (int i = 0; i < n; i++)
        {
            Rect lr = new Rect(x, y + i * lh, width, lh - 2f);
            if (i % 2 == 0 && Event.current.type == EventType.Repaint) { GUI.color = new Color(1f, 1f, 1f, 0.04f); GUI.DrawTexture(lr, Texture2D.whiteTexture); GUI.color = old; }
            string t = w.log[w.log.Count - 1 - i];
            GameUi.Icon(new Rect(lr.x + 4f, lr.y + 2f, 16f, 16f), GameUi.IconFor(t, true), i == 0 ? 1f : 0.7f);
            LocGUI.Label(new Rect(lr.x + 26f, lr.y, lr.width - 28f, lr.height), i == 0 ? t : UiKit.Col(UiKit.MutedHex, t), logSt);
        }
        return y + n * lh + 4f;
    }
}
