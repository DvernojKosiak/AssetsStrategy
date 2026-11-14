using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Клановые войны на сервере: поиск соперника, расписание на 7 дней, схема расстановки городов,
/// снимки городов (только расстановка зданий), живая карта (WarSim), итоги и рейтинг клана.
/// klan-voina-2: на войне у каждого игрока живая деревня (военный город) - копия расстановки домашней со своими
/// ресурсами, жителями и казармами; она живёт в игре игрока, сервер хранит её сохранение. Бойцы казарм выходят
/// на общую карту (WarDeploy). Внутри клана - рынок и подарки. Домой после войны приходит только рейтинг клана.
/// Карта считается сама (TickWars вызывает StrategycaServer несколько раз в секунду): расчёт идёт
/// под замком самой карты, а не всего сервера, поэтому война не тормозит остальные запросы.
/// </summary>
public partial class ServerCore
{
    [Serializable]
    class StoredWar
    {
        public string id;
        public string clanA, clanB;
        public string nameA, nameB;
        public string startUtc;
        public double daySeconds;
        public bool fast;
        public int seed, rows;
        public int gc;                // столбцов городов у клана (0 - старая война: один столбец; final-9)
        public int nc, mx, my;        // форма карты (WarGeo; klan-voina-1)
        public int v;                 // версия правил (WarRules.Version): старые войны останавливаются при обновлении
        public List<WarSlot> slotsA = new List<WarSlot>();
        public List<WarSlot> slotsB = new List<WarSlot>();
        public bool finished;
        public int winner;            // 0 - ничья
        public int scoreA, scoreB;
        public string result;
        public List<string> log = new List<string>();
        public List<MarketLot> lots = new List<MarketLot>();        // рынок клана (обоих кланов; видно только своим)
        public List<WarSaleNote> sales = new List<WarSaleNote>();   // проданные лоты: золото ждёт продавца в его военном городе
        public List<WarGiftNote> gifts = new List<WarGiftNote>();   // подарки соседей по клану: ждут деревню получателя
    }

    [Serializable]
    class WarGiftNote
    {
        public string toId;
        public WarGrant gift;
    }

    [Serializable]
    class WarSaleNote
    {
        public string sellerId;
        public MarketSale sale;
    }


    readonly Dictionary<string, WarSim> sims = new Dictionary<string, WarSim>();
    DateTime lastTick = DateTime.UtcNow;
    float warSaveTimer;

    string WarDir(string warId) { return Path.Combine(dataDir, "wars", warId); }
    string WarStatePath(string warId) { return Path.Combine(WarDir(warId), "map.json"); }
    string WarSnapshotPath(string warId, string accountId) { return Path.Combine(WarDir(warId), accountId + ".json"); }

    StoredWar WarOf(StoredClan c)
    {
        if (c == null || string.IsNullOrEmpty(c.warId)) return null;
        return db.wars.Find(w => w.id == c.warId);
    }

    double Elapsed(StoredWar w) { return (DateTime.UtcNow - ParseUtc(w.startUtc)).TotalSeconds; }
    WarStage StageOf(StoredWar w) { return w.finished ? WarStage.Finished : WarRules.StageOf(Elapsed(w), w.daySeconds); }

    void WarLog(StoredWar w, string text)
    {
        w.log.Add(text);
        if (w.log.Count > 30) w.log.RemoveAt(0);
    }

    // ---------- Админ: дни войны ----------

    bool IsAdmin(StoredAccount a)
    {
        if (a == null) return false;
        if (!string.IsNullOrEmpty(db.adminId) && FindById(db.adminId) != null) return a.profile.id == db.adminId;
        if (a.npc) return false;   // NPC-деревни (обновление 14) админами не бывают
        return db.accounts.Find(x => x != null && x.profile != null && !x.npc) == a;
    }

    /// <summary>Кто админ сервера (имя и логин) - для окна сервера.</summary>
    public string AdminName
    {
        get
        {
            lock (sync)
            {
                StoredAccount a = db.accounts.Find(IsAdmin);
                return a != null ? $"{a.profile.displayName} ({a.profile.login})" : "нет аккаунтов";
            }
        }
    }

    /// <summary>Сделать админом аккаунт с этим логином. Вернёт текст для окна сервера.</summary>
    public string SetAdmin(string login)
    {
        lock (sync)
        {
            StoredAccount a = FindByLogin(login);
            if (a == null) return "Нет аккаунта с логином " + login;
            db.adminId = a.profile.id;
            Save();
            return $"Админ: {a.profile.displayName} ({a.profile.login})";
        }
    }

    /// <summary>Идущие войны: id, название, день - для окна сервера.</summary>
    public List<KeyValuePair<string, string>> ActiveWars()
    {
        var list = new List<KeyValuePair<string, string>>();
        lock (sync)
            foreach (var w in db.wars)
                if (!w.finished)
                    list.Add(new KeyValuePair<string, string>(w.id,
                        $"«{w.nameA}» против «{w.nameB}», день {Math.Min(WarRules.Days, (int)Math.Floor(Elapsed(w) / w.daySeconds) + 1)}"));
        return list;
    }

    /// <summary>
    /// Перейти на день войны: 1..7 - начало этого дня, 8 - закончить войну сейчас (итоги по очкам).
    /// Сдвигает начало войны, всё остальное (карта, этапы, конец) идёт как обычно. warId пустой - все идущие войны.
    /// </summary>
    public string SetWarDay(string warId, int day)
    {
        lock (sync)
        {
            int n = 0;
            day = Math.Max(1, Math.Min(WarRules.Days + 1, day));
            foreach (var w in db.wars)
            {
                if (w.finished || (!string.IsNullOrEmpty(warId) && w.id != warId)) continue;
                if (StageOf(w) >= WarStage.Prepare && day <= 1 && sims.ContainsKey(w.id))
                    return "Карта войны уже есть: на первый день вернуться нельзя";
                double at = (day - 1) * w.daySeconds + 1.0;
                w.startUtc = DateTime.UtcNow.AddSeconds(-at).ToString("o");
                WarLog(w, day > WarRules.Days ? "Админ закончил войну" : $"Админ перевёл войну на день {day}");
                n++;
            }
            if (n == 0) return "Нет идущей войны";
            Save();
            return day > WarRules.Days ? "Война заканчивается" : $"Теперь день {day}";
        }
    }

    ApiResponse WarAdminReq(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        if (!IsAdmin(me)) return ApiResponse.Fail("Это может только админ сервера");
        StoredClan c = ClanOf(me);
        StoredWar w = WarOf(c);
        if (w == null || w.finished) return ApiResponse.Fail("Ваш клан сейчас не воюет");
        string text = SetWarDay(w.id, r.day);
        bool ok = text.StartsWith("Теперь") || text.StartsWith("Война заканч");
        if (!ok) return ApiResponse.Fail(text);
        return new ApiResponse { ok = true, error = text, war = InfoFor(c, me) };
    }

    // ---------- Запросы ----------

    ApiResponse WarInfoReq(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        StoredClan c = ClanOf(me);
        if (c == null) return ApiResponse.Fail("Вы не в клане");
        return new ApiResponse { ok = true, war = InfoFor(c, me) };
    }

    WarInfo InfoFor(StoredClan c, StoredAccount me)
    {
        var info = new WarInfo { leader = CanLeadWar(c, me), searching = !string.IsNullOrEmpty(c.warSearchUtc) };
        StoredWar w = WarOf(c) ?? db.wars.Find(x => x.id == c.lastWarId);
        if (w == null) { info.stage = (int)(info.searching ? WarStage.Searching : WarStage.None); return info; }
        WarStage st = StageOf(w);
        int side = w.clanA == c.id ? 1 : 2;
        info.inWar = !w.finished;
        info.warId = w.id;
        info.stage = (int)st;
        info.fast = w.fast;
        info.admin = IsAdmin(me);
        info.day = Math.Min(WarRules.Days, (int)Math.Floor(Elapsed(w) / w.daySeconds) + 1);
        info.mySide = side;
        info.nameA = w.nameA;
        info.nameB = w.nameB;
        info.rows = w.rows;
        info.gc = w.gc;
        info.nc = w.nc; info.mx = w.mx; info.my = w.my;
        info.version = w.v;
        double el = Elapsed(w);
        info.stageLeft = (float)Math.Max(0, WarRules.NextStageAt(st, w.daySeconds) - el);
        info.warLeft = (float)Math.Max(0, WarRules.Days * w.daySeconds - el);
        info.slots = new List<WarSlot>(side == 1 ? w.slotsA : w.slotsB);
        info.log = new List<string>(w.log);
        if (sims.TryGetValue(w.id, out WarSim sim))
        lock (sim)
        {
            sim.Score(out info.scoreA, out info.scoreB);
            // Маленькая карта для окна клана: чьи участки и где города
            info.plots = sim.S.plots;
            info.cities = new List<WarCityDot>();
            foreach (var sc in sim.S.cities)
                info.cities.Add(new WarCityDot { name = sc.name, side = sc.side, plotX = sc.plotX, plotY = sc.plotY, fallen = sc.fallen, mine = sc.accountId == me.profile.id });
            info.sources = new List<WarSource>(sim.Sources);
        }
        else { info.scoreA = w.scoreA; info.scoreB = w.scoreB; }
        info.winner = w.winner;
        info.result = w.result;
        if (w.finished && info.searching) info.stage = (int)WarStage.Searching;
        return info;
    }

    ApiResponse WarStartReq(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        StoredClan c = ClanOf(me);
        if (c == null) return ApiResponse.Fail("Вы не в клане");
        if (!CanLeadWar(c, me)) return ApiResponse.Fail("Начать войну может только глава клана или заместитель");
        if (WarOf(c) != null) return ApiResponse.Fail("Клан уже воюет");
        if (!c.members.Exists(id => File.Exists(CityPath(id)))) return ApiResponse.Fail("Ни у кого в клане ещё нет города");
        c.warSearchUtc = Now();
        SystemMessage(c, $"{me.profile.displayName} объявил(а) войну: ищем соперника");

        // Соперник - клан, который тоже ищет, с рейтингом поближе
        StoredClan best = null;
        foreach (var x in db.clans)
        {
            if (x == c || string.IsNullOrEmpty(x.warSearchUtc) || WarOf(x) != null) continue;
            if (!x.members.Exists(id => File.Exists(CityPath(id)))) continue;
            if (best == null || Math.Abs(x.rating - c.rating) < Math.Abs(best.rating - c.rating)) best = x;
        }
        if (best != null) CreateWar(best, c);
        Save();
        return new ApiResponse { ok = true, war = InfoFor(c, me) };
    }

    ApiResponse WarCancelReq(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        StoredClan c = ClanOf(me);
        if (c == null) return ApiResponse.Fail("Вы не в клане");
        if (!CanLeadWar(c, me)) return ApiResponse.Fail("Это решает глава клана или заместитель");
        if (string.IsNullOrEmpty(c.warSearchUtc)) return ApiResponse.Fail("Клан не ищет соперника");
        c.warSearchUtc = "";
        SystemMessage(c, "Поиск соперника остановлен");
        Save();
        return new ApiResponse { ok = true, war = InfoFor(c, me) };
    }

    void CreateWar(StoredClan a, StoredClan b)
    {
        var w = new StoredWar
        {
            id = Guid.NewGuid().ToString("N"), clanA = a.id, clanB = b.id, nameA = a.name, nameB = b.name,
            startUtc = Now(), daySeconds = WarRules.DaySeconds,
            seed = new System.Random().Next(1, int.MaxValue),
        };
        // Для проверок: длину суток войны можно задать переменной окружения (секунды)
        if (double.TryParse(Environment.GetEnvironmentVariable("STRATEGYCA_WAR_DAY"), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double testDay) && testDay > 0) { w.daySeconds = testDay; w.fast = true; }
        Directory.CreateDirectory(WarDir(w.id));
        int maxMembers = Math.Max(Snapshot(w, a, w.slotsA), Snapshot(w, b, w.slotsB));
        // klan-voina-1: карта квадратная, города клана - сеткой
        WarGeo g = WarGeo.Square(maxMembers);
        w.gc = g.gc; w.rows = g.Rows; w.nc = g.neutral; w.mx = g.mx; w.my = g.my;
        w.v = WarRules.Version;
        AutoSlots(w.slotsA, g);
        AutoSlots(w.slotsB, g);
        db.wars.Add(w);
        foreach (var c in new[] { a, b })
        {
            c.warSearchUtc = "";
            c.warId = w.id;
            c.lastWarId = w.id;
            SystemMessage(c, $"Война! Соперник: клан «{(c == a ? b.name : a.name)}». Первый день — глава расставляет города на схеме, со второго дня — бои на общей карте.");
        }
        WarLog(w, $"Война «{a.name}» против «{b.name}» началась");
        Debug.Log($"[Сервер] Клановая война: {a.name} против {b.name}{(w.fast ? " (быстрая)" : "")}");
    }

    /// <summary>Снимок городов клана: только расстановка построек и путь развития - без армии и ресурсов.</summary>
    int Snapshot(StoredWar w, StoredClan c, List<WarSlot> slots)
    {
        foreach (var id in c.members)
        {
            CityData home = ReadCity(id);
            StoredAccount acc = FindById(id);
            if (home == null || acc == null || home.buildings == null || home.buildings.Count == 0) continue;
            var snap = new CityData
            {
                ownerId = id, ownerName = acc.profile.displayName, seed = home.seed, savedUtc = Now(),
                path = home.path, school = home.school, eng = home.eng, war = home.war, population = home.population,
                day = home.day, timeInDay = home.timeInDay, mapSize = home.mapSize, terrain = home.terrain,
                // Военный город живёт так же, как дом (те же здания) - пока игрок не зашёл, доход по домашнему
                income = home.income != null ? new List<float>(home.income) : new List<float>(), incomeSeconds = home.incomeSeconds,
                // Запас на старт: живая деревня сама добывает, но с пустыми складами первые часы скучны
                gold = WarRules.StartGold, food = WarRules.StartFood, wood = WarRules.StartWood, stone = WarRules.StartStone, iron = WarRules.StartIron,
            };
            foreach (var sb in home.buildings)
            {
                if (sb == null) continue;
                var copy = new SavedBuilding { type = sb.type, x = sb.x, y = sb.y, level = sb.level, fortified = sb.fortified, w = sb.w, h = sb.h, starter = sb.starter,
                                               manualWorkers = sb.manualWorkers, wantWorkers = sb.wantWorkers };
                // Настройки здания (закреплённые курьеры, авторежимы) переходят, запасы - нет
                if (sb.state != null) copy.state = sb.state.FindAll(e => e != null && e.StartsWith("set.", StringComparison.Ordinal));
                snap.buildings.Add(copy);
            }
            WriteSafe(WarSnapshotPath(w.id, id), JsonUtility.ToJson(snap));
            slots.Add(new WarSlot { accountId = id, name = acc.profile.displayName, hall = CityLoss.HallLevel(home) });
        }
        return slots.Count;
    }

    // Места по умолчанию: сильные города - в середине и ближе к центру карты
    static void AutoSlots(List<WarSlot> slots, WarGeo g)
    {
        // Города клана стоят сеткой вплотную (gc столбцов): сильные - в середине и в столбце у нейтральных земель,
        // слабые - по краям и сзади
        int gc = g.gc;
        var order = new List<int>();
        int n = g.Slots;
        for (int k = 0; k < n; k++) order.Add(k);
        double mid = (n / gc - 1) / 2.0;
        System.Func<int, double> score = k => Math.Abs(k / gc - mid) + (k % gc) * 1.01;
        order.Sort((x, y) => { int c = score(x).CompareTo(score(y)); return c != 0 ? c : x.CompareTo(y); });
        var sorted = new List<WarSlot>(slots);
        sorted.Sort((x, y) => y.hall.CompareTo(x.hall));
        for (int i = 0; i < sorted.Count && i < order.Count; i++) sorted[i].slot = order[i];
    }

    static WarGeo GeoOf(StoredWar w) { return WarGeo.Of(w.rows, w.gc, w.nc, w.mx, w.my); }

    ApiResponse WarSchemeReq(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        StoredClan c = ClanOf(me);
        StoredWar w = WarOf(c);
        if (w == null) return ApiResponse.Fail("Клан не воюет");
        if (!CanLeadWar(c, me)) return ApiResponse.Fail("Расставляет города глава клана или заместитель");
        if (StageOf(w) != WarStage.Planning) return ApiResponse.Fail("Расставлять города можно только в первый день");
        List<WarSlot> slots = w.clanA == c.id ? w.slotsA : w.slotsB;
        WarSlot s = slots.Find(x => x.accountId == r.memberId);
        if (s == null) return ApiResponse.Fail("Этого игрока нет в войне");
        if (r.slot < 0 || r.slot >= GeoOf(w).Slots) return ApiResponse.Fail("Нет такого места");
        WarSlot other = slots.Find(x => x.slot == r.slot);
        if (other != null) other.slot = s.slot;   // меняются местами
        s.slot = r.slot;
        Save();
        return new ApiResponse { ok = true, war = InfoFor(c, me) };
    }

    // Мой клан, война, карта и мой город на ней
    string WarContext(ApiRequest r, out StoredAccount me, out StoredWar w, out WarSim sim, out WarSim.SimCity city, out int side)
    {
        w = null; sim = null; city = null; side = 0;
        me = FindByToken(r.token);
        if (me == null) return SessionGone;
        StoredClan c = ClanOf(me);
        string mid = me.profile.id;
        // Ушедший из клана игрок остаётся на войне своим городом
        w = WarOf(c) ?? db.wars.Find(x => !x.finished && (x.slotsA.Exists(s => s.accountId == mid) || x.slotsB.Exists(s => s.accountId == mid)));
        if (w == null && c != null) w = db.wars.Find(x => x.id == c.lastWarId);
        if (w == null) return "Клан не воюет";
        if (!sims.TryGetValue(w.id, out sim)) return StageOf(w) == WarStage.Planning ? "Карта появится на второй день войны" : "Карта войны недоступна";
        city = sim.S.cities.Find(x => x.accountId == mid);
        side = city != null ? city.side : (c != null && c.id == w.clanA ? 1 : 2);
        return null;
    }

    ApiResponse WarStaticReq(ApiRequest r)
    {
        string err = WarContext(r, out _, out StoredWar w, out WarSim sim, out WarSim.SimCity city, out int side);
        if (err != null) return ApiResponse.Fail(err);
        WarStatic ws;
        lock (sim) ws = sim.Static(side, city != null ? city.idx : -1, w.id, w.nameA, w.nameB);
        // since = 1: карта у игры уже нарисована - местность городов (самое тяжёлое) не шлём
        if (r.since == 1 && ws.cities != null) foreach (var c in ws.cities) c.terrain = null;
        return new ApiResponse { ok = true, warStatic = ws };
    }

    ApiResponse WarFrameReq(ApiRequest r)
    {
        string err = WarContext(r, out _, out StoredWar w, out WarSim sim, out WarSim.SimCity city, out int side);
        // Война уже окончена: карта закрыта, но итог показать можно
        if (err != null && w != null && w.finished)
            return new ApiResponse { ok = true, warFrame = new WarFrame { stage = (int)WarStage.Finished, winner = w.winner, scoreA = w.scoreA, scoreB = w.scoreB, eventN = r.since } };
        if (err != null) return ApiResponse.Fail(err);
        WarFrame f;
        lock (sim) f = sim.Frame(side, city, r.since);
        if (city != null)
        {
            // Подарки забирает только живая деревня (r.live): в окне зрителя их некуда положить
            if (r.live) f.gifts = TakeWarGifts(w, city.accountId);
            f.mates = MatesOf(w, city.accountId);
        }
        WarStage st = StageOf(w);
        double el = Elapsed(w);
        f.stage = (int)st;
        f.stageLeft = (float)Math.Max(0, WarRules.NextStageAt(st, w.daySeconds) - el);
        f.warLeft = (float)Math.Max(0, WarRules.Days * w.daySeconds - el);
        f.winner = w.winner;
        return new ApiResponse { ok = true, warFrame = f };
    }

    // ---------- Живая деревня на войне (военный город): полная копия расстановки, свои ресурсы и жители ----------

    string WarCityPath(string warId, string accountId) { return Path.Combine(WarDir(warId), accountId + "_city.json"); }

    /// <summary>Военный город игрока; ещё не заходил - снимок его города (без ресурсов и армии), живущий со второго дня.</summary>
    CityData ReadWarCity(StoredWar w, string accountId)
    {
        string p = WarCityPath(w.id, accountId);
        try { if (File.Exists(p)) return JsonUtility.FromJson<CityData>(File.ReadAllText(p)); }
        catch (Exception) { }
        CityData c = null;
        try { c = JsonUtility.FromJson<CityData>(File.ReadAllText(WarSnapshotPath(w.id, accountId))); }
        catch (Exception) { }
        if (c != null) c.savedUtc = ParseUtc(w.startUtc).AddSeconds(w.daySeconds).ToString("o");
        return c;
    }

    /// <summary>Здания этого города, разрушенные на карте войны: клетки в самом городе ("x,y;...").</summary>
    string RuinedCells(StoredWar w, string accountId)
    {
        if (!sims.TryGetValue(w.id, out WarSim sim)) return "";
        lock (sim) return RuinedCellsOf(sim, accountId);
    }

    static string RuinedCellsOf(WarSim sim, string accountId)
    {
        WarSim.SimCity c = sim.S.cities.Find(x => x.accountId == accountId);
        if (c == null) return "";
        int ox = c.plotX * WarRules.Plot + WarRules.CitySize / 2, oy = c.plotY * WarRules.Plot + WarRules.CitySize / 2;
        var sb = new System.Text.StringBuilder();
        foreach (var b in sim.S.blds)
            if (b.city == c.idx && b.hp <= 0) sb.Append(b.x - ox).Append(',').Append(b.y - oy).Append(';');
        return sb.ToString();
    }

    string WarCityContext(ApiRequest r, out StoredAccount me, out StoredClan c, out StoredWar w)
    {
        c = null; w = null;
        me = FindByToken(r.token);
        if (me == null) return SessionGone;
        c = ClanOf(me);
        string mid = me.profile.id;
        w = WarOf(c) ?? db.wars.Find(x => !x.finished && (x.slotsA.Exists(s => s.accountId == mid) || x.slotsB.Exists(s => s.accountId == mid)));
        if (w == null || w.finished) return "Война окончена: ваш клан сейчас не воюет";   // игра по этим словам возвращается домой
        WarStage st = StageOf(w);
        if (st < WarStage.Prepare) return "Карта войны откроется на второй день войны";
        if (st >= WarStage.Finished) return "Война окончена";
        return null;
    }

    ApiResponse WarCityLoadReq(ApiRequest r)
    {
        string err = WarCityContext(r, out StoredAccount me, out StoredClan c, out StoredWar w);
        if (err != null) return ApiResponse.Fail(err);
        CityData city = ReadWarCity(w, me.profile.id);
        if (city == null) return ApiResponse.Fail("Вашего города нет на этой войне");
        city.marks = 0;
        // Пока игрока не было, на рынке клана купили его лоты - золото уже в городе
        var sold = TakeWarSales(w, me.profile.id);
        foreach (var s in sold) city.gold += Math.Max(0, s.price);
        if (sold.Count > 0) { WriteSafe(WarCityPath(w.id, me.profile.id), JsonUtility.ToJson(city)); Save(); }
        return new ApiResponse { ok = true, city = JsonUtility.ToJson(city), war = c != null ? InfoFor(c, me) : null, ruined = RuinedCells(w, me.profile.id), sales = sold };
    }

    ApiResponse WarCitySaveReq(ApiRequest r)
    {
        string err = WarCityContext(r, out StoredAccount me, out _, out StoredWar w);
        if (err != null) return ApiResponse.Fail(err);
        CityData stored = ReadWarCity(w, me.profile.id);
        if (stored == null) return ApiResponse.Fail("Вашего города нет на этой войне");
        CityData c;
        try { c = JsonUtility.FromJson<CityData>(r.city); }
        catch (Exception) { return ApiResponse.Fail("Город не читается"); }
        if (c == null) return ApiResponse.Fail("Пустой город");

        // Строить и улучшать в военном городе нельзя: здания - только те, что были (склады зданий - от игры)
        var kept = new List<SavedBuilding>();
        foreach (var sb in stored.buildings)
        {
            if (sb == null) continue;
            SavedBuilding mine = c.buildings != null ? c.buildings.Find(x => x != null && x.x == sb.x && x.y == sb.y && x.type == sb.type) : null;
            var nb = new SavedBuilding { type = sb.type, x = sb.x, y = sb.y, level = sb.level, fortified = sb.fortified, starter = sb.starter, w = sb.w, h = sb.h,
                                         manualWorkers = sb.manualWorkers, wantWorkers = sb.wantWorkers, state = sb.state ?? new List<string>() };
            if (mine != null)
            {
                nb.gold = mine.gold; nb.wood = mine.wood; nb.stone = mine.stone; nb.food = mine.food; nb.iron = mine.iron;
                nb.oats = mine.oats; nb.berries = mine.berries; nb.herbs = mine.herbs; nb.resin = mine.resin; nb.leaves = mine.leaves;
                // Что внутри и как настроено (final-9): закреплённые курьеры, рабочие, авторежимы, склад, лошади, модули.
                // Раньше сервер это выбрасывал - вышли из военного города и вернулись, а все здания «забыли» настройки
                nb.manualWorkers = mine.manualWorkers;
                nb.wantWorkers = mine.wantWorkers;
                nb.state = mine.state != null ? new List<string>(mine.state) : new List<string>();
            }
            kept.Add(nb);
        }
        c.buildings = kept;
        c.marks = 0;
        c.seed = stored.seed;
        c.path = stored.path; c.school = stored.school; c.eng = stored.eng; c.war = stored.war;
        c.mapSize = stored.mapSize; c.terrain = stored.terrain;
        c.ownerId = me.profile.id; c.ownerName = me.profile.displayName; c.savedUtc = Now();

        // Продажи на рынке клана и доход с захваченных земель (копится на карте) - в военный город.
        // Сразу пишем их и в сохранение: игра добавит то же самое у себя, следующее сохранение придёт уже с ними.
        var res = new ApiResponse { ok = true, ruined = RuinedCells(w, me.profile.id), sales = TakeWarSales(w, me.profile.id) };
        foreach (var s in res.sales) c.gold += Math.Max(0, s.price);
        if (sims.TryGetValue(w.id, out WarSim sim))
        lock (sim)
        {
            WarSim.SimCity sc = sim.S.cities.Find(x => x.accountId == me.profile.id);
            if (sc != null && (sc.gold >= 1f || sc.food >= 1f || sc.wood >= 1f || sc.stone >= 1f || sc.iron >= 1f))
            {
                res.warIncome = new WarGrant { gold = (int)sc.gold, food = (int)sc.food, wood = (int)sc.wood, stone = (int)sc.stone, iron = (int)sc.iron };
                sc.gold -= res.warIncome.gold; sc.food -= res.warIncome.food; sc.wood -= res.warIncome.wood; sc.stone -= res.warIncome.stone; sc.iron -= res.warIncome.iron;
                c.gold += res.warIncome.gold; c.food += res.warIncome.food; c.wood += res.warIncome.wood; c.stone += res.warIncome.stone; c.iron += res.warIncome.iron;
            }
        }
        WriteSafe(WarCityPath(w.id, me.profile.id), JsonUtility.ToJson(c));
        if (res.sales.Count > 0) Save();
        return res;
    }

    ApiResponse WarDeployReq(ApiRequest r)
    {
        string err = WarContext(r, out _, out StoredWar w, out WarSim sim, out WarSim.SimCity city, out _);
        if (err != null) return ApiResponse.Fail(err);
        if (city == null) return ApiResponse.Fail("Вашего города нет на этой войне");
        if (r.recruits == null || r.recruits.Count == 0) return ApiResponse.Fail("Некого выводить");
        var ids = new List<int>();
        int n;
        string orderErr = null;
        lock (sim)
        {
            n = sim.Deploy(city, r.recruits, out err, ids);
            // Бойцов из города послали за его край: вышли - и сразу идут туда
            if (n > 0 && r.warCmd != null && ids.Count > 0) { r.warCmd.units = ids; orderErr = sim.Command(city, r.warCmd); }
        }
        if (n == 0) return ApiResponse.Fail(err);
        return new ApiResponse { ok = true, deployed = n, error = orderErr };
    }

    /// <summary>Что лежит в военном городе к концу войны: последнее сохранение и доход с тех пор (город живёт и без игрока).</summary>
    void WarCityStock(StoredWar w, string accountId, out float gold, out float wood, out float stone, out float food, out float iron)
    {
        gold = wood = stone = food = iron = 0f;
        CityData c = ReadWarCity(w, accountId);
        if (c == null) return;
        float hours = (float)Math.Max(0.0, Math.Min(14 * 24.0, (DateTime.UtcNow - ParseUtc(c.savedUtc)).TotalHours));
        float[] add = new float[5];
        if (c.income != null && c.incomeSeconds >= 300f)
            for (int i = 0; i < 5 && i < c.income.Count; i++) add[i] = c.income[i] * hours;
        gold = Math.Max(0f, c.gold + add[0]); wood = Math.Max(0f, c.wood + add[1]); stone = Math.Max(0f, c.stone + add[2]);
        food = Math.Max(0f, c.food + add[3]); iron = Math.Max(0f, c.iron + add[4]);
    }

    // ---------- Рынок клана на войне ----------

    List<MarketSale> TakeWarSales(StoredWar w, string accountId)
    {
        var list = new List<MarketSale>();
        if (w.sales == null) return list;
        foreach (var n in w.sales) if (n.sellerId == accountId && n.sale != null) list.Add(n.sale);
        w.sales.RemoveAll(n => n.sellerId == accountId);
        return list;
    }

    static int SideOf(StoredWar w, string accountId)
    {
        if (w.slotsA.Exists(s => s.accountId == accountId)) return 1;
        if (w.slotsB.Exists(s => s.accountId == accountId)) return 2;
        return 0;
    }

    ApiResponse WarMarketListReq(ApiRequest r)
    {
        string err = WarCityContext(r, out StoredAccount me, out _, out StoredWar w);
        if (err != null) return ApiResponse.Fail(err);
        int side = SideOf(w, me.profile.id);
        var lots = w.lots.FindAll(l => SideOf(w, l.sellerId) == side);
        lots.Reverse();
        return new ApiResponse { ok = true, lots = lots, sales = new List<MarketSale>() };
    }

    ApiResponse WarMarketPostReq(ApiRequest r)
    {
        string err = WarCityContext(r, out StoredAccount me, out _, out StoredWar w);
        if (err != null) return ApiResponse.Fail(err);
        if (SideOf(w, me.profile.id) == 0) return ApiResponse.Fail("Вашего города нет на этой войне");
        MarketLot l = r.lot;
        if (l == null || l.units <= 0 || l.price <= 0 || l.units > 100000 || l.price > 1000000) return ApiResponse.Fail("Неверный лот");
        if (l.item > 0 && (l.thing == null || string.IsNullOrEmpty(l.name))) return ApiResponse.Fail("Неверный лот");
        if (w.lots.FindAll(x => x.sellerId == me.profile.id).Count >= Api.MaxLotsPerPlayer)
            return ApiResponse.Fail($"Больше {Api.MaxLotsPerPlayer} лотов выставить нельзя — снимите какой-нибудь");
        var lot = new MarketLot
        {
            id = Guid.NewGuid().ToString("N"), sellerId = me.profile.id, sellerName = me.profile.displayName,
            kind = l.kind, tier = l.tier, units = l.units, price = l.price, createdUtc = Now(),
            item = l.item, name = l.item > 0 ? Cut(l.name, 60) : null, info = l.item > 0 ? Cut(l.info, 160) : null, thing = l.item > 0 ? l.thing : null,
        };
        w.lots.Add(lot);
        Save();
        return new ApiResponse { ok = true, lot = lot };
    }

    ApiResponse WarMarketBuyReq(ApiRequest r)
    {
        string err = WarCityContext(r, out StoredAccount me, out _, out StoredWar w);
        if (err != null) return ApiResponse.Fail(err);
        MarketLot lot = w.lots.Find(x => x.id == r.lotId);
        if (lot == null) return ApiResponse.Fail("Этот лот уже купили или сняли");
        if (lot.sellerId == me.profile.id) return ApiResponse.Fail("Свой лот купить нельзя — его можно снять");
        if (SideOf(w, lot.sellerId) != SideOf(w, me.profile.id)) return ApiResponse.Fail("Это лот другого клана");
        w.lots.Remove(lot);
        w.sales.Add(new WarSaleNote
        {
            sellerId = lot.sellerId,
            sale = new MarketSale
            {
                id = Guid.NewGuid().ToString("N"), buyerName = me.profile.displayName,
                kind = lot.kind, tier = lot.tier, units = lot.units, price = lot.price, timeUtc = Now(), item = lot.item, name = lot.name,
            },
        });
        Save();
        return new ApiResponse { ok = true, lot = lot };
    }

    ApiResponse WarMarketCancelReq(ApiRequest r)
    {
        string err = WarCityContext(r, out StoredAccount me, out _, out StoredWar w);
        if (err != null) return ApiResponse.Fail(err);
        MarketLot lot = w.lots.Find(x => x.id == r.lotId && x.sellerId == me.profile.id);
        if (lot == null) return ApiResponse.Fail("Лот уже продан");
        w.lots.Remove(lot);
        Save();
        return new ApiResponse { ok = true, lot = lot };
    }


    // ---------- Подарки внутри клана (klan-voina-2) ----------
    // Деревня отдаёт соседу по клану ресурсы: проверяем по её последнему сохранению (не больше, чем есть), снимаем там же,
    // а получатель забирает подарок с ближайшим кадром карты (WarFrame.gifts). Игра дарителя списывает у себя сама.

    string WarGift(StoredWar w, StoredAccount me, WarCommand cmd)
    {
        if (cmd == null || string.IsNullOrEmpty(cmd.memberId)) return "Кому подарить?";
        if (cmd.memberId == me.profile.id) return "Себе дарить нельзя";
        int side = SideOf(w, me.profile.id);
        if (side == 0 || SideOf(w, cmd.memberId) != side) return "Дарить можно только соседям по клану на этой войне";
        int[] amt = { Math.Max(0, cmd.gold), Math.Max(0, cmd.food), Math.Max(0, cmd.wood), Math.Max(0, cmd.stone), Math.Max(0, cmd.iron) };
        int total = 0;
        foreach (int v in amt) total += v;
        if (total <= 0) return "Подарок пустой";
        if (total > 100000) return "Слишком много за раз";
        CityData c = ReadWarCity(w, me.profile.id);
        if (c == null) return "Вашей деревни нет на этой войне";
        if (c.gold < amt[0] || c.food < amt[1] || c.wood < amt[2] || c.stone < amt[3] || c.iron < amt[4])
            return "В деревне столько нет (сервер видит последнее сохранение — подождите полминуты)";
        c.gold -= amt[0]; c.food -= amt[1]; c.wood -= amt[2]; c.stone -= amt[3]; c.iron -= amt[4];
        WriteSafe(WarCityPath(w.id, me.profile.id), JsonUtility.ToJson(c));
        if (w.gifts == null) w.gifts = new List<WarGiftNote>();
        w.gifts.Add(new WarGiftNote { toId = cmd.memberId, gift = new WarGrant { id = Guid.NewGuid().ToString("N"), text = me.profile.displayName,
                                                                                  gold = amt[0], food = amt[1], wood = amt[2], stone = amt[3], iron = amt[4] } });
        StoredAccount to = FindById(cmd.memberId);
        WarLog(w, $"{me.profile.displayName} подарил {(to != null ? to.profile.displayName : "соседу")}: " + WarRules.IncomeText(amt));
        Save();
        return null;
    }

    List<WarGrant> TakeWarGifts(StoredWar w, string accountId)
    {
        var list = new List<WarGrant>();
        if (w.gifts == null || w.gifts.Count == 0) return list;
        foreach (var g in w.gifts) if (g.toId == accountId && g.gift != null) list.Add(g.gift);
        if (list.Count > 0) { w.gifts.RemoveAll(g => g.toId == accountId); Save(); }
        return list;
    }

    List<WarSlot> MatesOf(StoredWar w, string accountId)
    {
        int side = SideOf(w, accountId);
        var list = new List<WarSlot>();
        foreach (var s in side == 1 ? w.slotsA : side == 2 ? w.slotsB : new List<WarSlot>())
            if (s != null && s.accountId != accountId) list.Add(new WarSlot { accountId = s.accountId, name = s.name, slot = s.slot, hall = s.hall });
        return list;
    }

    ApiResponse WarCmdReq(ApiRequest r)
    {
        string err = WarContext(r, out _, out StoredWar w, out WarSim sim, out WarSim.SimCity city, out _);
        if (err != null) return ApiResponse.Fail(err);
        if (city == null) return ApiResponse.Fail("Вашего города нет на этой войне");
        if (r.warCmd != null && r.warCmd.kind == WarCommand.Gift)
        {
            StoredAccount me = FindByToken(r.token);
            err = me == null ? SessionGone : WarStage.Prepare > StageOf(w) ? "Дарить можно со второго дня войны" : WarGift(w, me, r.warCmd);
            return err != null ? ApiResponse.Fail(err) : new ApiResponse { ok = true };
        }
        int kind = r.warCmd != null ? r.warCmd.kind : 0;
        // Живые деревни (klan-voina-2): бойцов учат казармы деревни, здания стоят, как в деревне
        if (kind == WarCommand.Train || kind == WarCommand.CancelTrain || kind == WarCommand.Research)
            return ApiResponse.Fail("Бойцов учат казармы вашей деревни: обученные сами выходят на карту");
        if (kind == WarCommand.MoveBuilding) return ApiResponse.Fail("На войне здания не переносят");
        if (kind == WarCommand.Repair)
        {
            // Чинит деревня своими досками и камнем: проверяем и списываем по её последнему сохранению
            StoredAccount me = FindByToken(r.token);
            if (me == null) return ApiResponse.Fail(SessionGone);
            CityData c = ReadWarCity(w, me.profile.id);
            if (c == null) return ApiResponse.Fail("Вашей деревни нет на этой войне");
            int wood, stone;
            lock (sim)
            {
                sim.RepairCostOf(city, r.warCmd.building, out wood, out stone);
                if (wood + stone <= 0) return ApiResponse.Fail("Чинить нечего — здание целое или его уже чинят");
                if (c.wood < wood || c.stone < stone)
                    return ApiResponse.Fail($"Не хватает: нужно {wood} дерева и {stone} камня (сервер видит последнее сохранение деревни)");
                city.wood += wood; city.stone += stone;   // деньги деревни - на счёт карты, Repair их тут же тратит
                err = sim.Command(city, r.warCmd);
                if (err != null) { city.wood -= wood; city.stone -= stone; }
            }
            if (err != null) return ApiResponse.Fail(err);
            c.wood -= wood; c.stone -= stone;
            WriteSafe(WarCityPath(w.id, me.profile.id), JsonUtility.ToJson(c));
            // warIncome здесь - сколько ушло из деревни (игра спишет у себя сама)
            return new ApiResponse { ok = true, warIncome = new WarGrant { wood = wood, stone = stone } };
        }
        if (kind == WarCommand.Recall)
        {
            // klan-voina-14: бойцы у своей казармы заходят в неё - деревня получает их обратно со снаряжением
            if (StageOf(w) >= WarStage.Finished) return ApiResponse.Fail("Война окончена");
            var back = new List<WarRecruit>();
            lock (sim) err = sim.Recall(city, r.warCmd.units, back);
            if (err != null) return ApiResponse.Fail(err);
            return new ApiResponse { ok = true, deployed = back.Count, warFrame = new WarFrame { back = back } };
        }
        lock (sim) err = sim.Command(city, r.warCmd);
        return err != null ? ApiResponse.Fail(err) : new ApiResponse { ok = true };
    }

    // ---------- Ход войн (вызывается несколько раз в секунду) ----------

    public void TickWars()
    {
        DateTime now = DateTime.UtcNow;
        float dt;
        bool saveMaps;
        var work = new List<KeyValuePair<StoredWar, WarSim>>();
        lock (sync)
        {
            dt = (float)Math.Min(1.0, (now - lastTick).TotalSeconds);
            lastTick = now;
            if (db.wars.Count == 0) return;
            bool changed = false;
            warSaveTimer += dt;
            saveMaps = warSaveTimer >= 30f;
            if (saveMaps) warSaveTimer = 0f;

            foreach (var w in db.wars)
            {
                if (w.finished) continue;
                // Война по старым правилам с новой игрой не сходится: останавливаем без последствий
                if (w.v < WarRules.Version) { StopOldWar(w); changed = true; continue; }
                WarStage st = StageOf(w);
                if (st >= WarStage.Prepare && !sims.ContainsKey(w.id))
                {
                    WarSim loaded = LoadSim(w) ?? CreateSim(w);
                    if (loaded == null) continue;
                    sims[w.id] = loaded;
                    lock (loaded) loaded.Stage = st;
                    changed = true;
                }
                if (!sims.TryGetValue(w.id, out WarSim sim)) continue;
                lock (sim)
                {
                    if (sim.Stage != st)
                    {
                        if (st == WarStage.Neutral) WarLog(w, sim.EventText("Третий день: нейтральные земли открыты — там встречаются армии. На землю врага пока нельзя"));
                        else if (st == WarStage.Open) WarLog(w, sim.EventText("Пятый день: открыта вся карта. Можно идти на города врага"));
                        changed = true;
                    }
                    sim.Stage = st;
                }
                work.Add(new KeyValuePair<StoredWar, WarSim>(w, sim));
            }
            if (changed) Save();
        }

        // Расчёт карт - вне общего замка сервера: запросы игроков в это время не ждут
        foreach (var kv in work)
        {
            WarSim sim = kv.Value;
            lock (sim)
                for (float left = dt; left > 0.001f; left -= WarRules.Tick) sim.Step(Math.Min(WarRules.Tick, left));
        }

        lock (sync)
        {
            bool changed = false;
            foreach (var kv in work)
            {
                StoredWar w = kv.Key;
                WarSim sim = kv.Value;
                if (w.finished) continue;
                WarStage st = StageOf(w);
                int win = 0;
                bool end = st == WarStage.Finished;
                lock (sim)
                {
                    bool aDown = sim.AllFallen(1), bDown = sim.AllFallen(2);
                    if (aDown != bDown) { end = true; win = aDown ? 2 : 1; }
                }
                if (end) { FinishWar(w, sim, win); changed = true; }
            }
            if (changed) Save();
        }

        // Карты - на диск раз в 30 секунд (тоже вне общего замка)
        if (saveMaps)
            foreach (var kv in work)
                if (!kv.Key.finished) SaveSim(kv.Key, kv.Value);
    }

    void StopOldWar(StoredWar w)
    {
        w.finished = true;
        w.winner = 0;
        w.result = "Война остановлена: вышло обновление клановой войны. Начните новую войну";
        WarLog(w, w.result);
        sims.Remove(w.id);
        foreach (var id in new[] { w.clanA, w.clanB })
        {
            StoredClan c = db.clans.Find(x => x.id == id);
            if (c == null) continue;
            if (c.warId == w.id) c.warId = null;
            SystemMessage(c, "Клановая война остановлена без последствий: вышло обновление — теперь на войне живые деревни на общей карте. Глава может начать новую войну.");
        }
        Debug.Log($"[Сервер] Старая война {w.nameA} — {w.nameB} остановлена (новые правила)");
    }

    WarSim CreateSim(StoredWar w)
    {
        var cities = new List<WarSim.SimCity>();
        var builds = new List<List<SavedBuilding>>();
        foreach (int side in new[] { 1, 2 })
        {
            List<WarSlot> slots = side == 1 ? w.slotsA : w.slotsB;
            // Без места - ставим на первое свободное
            var taken = new HashSet<int>();
            foreach (var s in slots) if (s.slot >= 0) taken.Add(s.slot);
            foreach (var s in slots)
            {
                if (s.slot >= 0) continue;
                for (int k = 0; k < GeoOf(w).Slots; k++) if (!taken.Contains(k)) { s.slot = k; taken.Add(k); break; }
            }
            foreach (var s in slots)
            {
                if (s.slot < 0) continue;
                CityData snap = null;
                try { snap = JsonUtility.FromJson<CityData>(File.ReadAllText(WarSnapshotPath(w.id, s.accountId))); }
                catch (Exception) { }
                if (snap == null) continue;
                cities.Add(new WarSim.SimCity
                {
                    accountId = s.accountId, name = s.name, side = side, path = snap.path,
                    plotX = GeoOf(w).SlotPlotCol(side, s.slot), plotY = GeoOf(w).SlotPlotRow(s.slot),
                    mapSize = snap.mapSize, terrain = TerrainWithBridges(snap),
                });
                builds.Add(snap.buildings);
            }
        }
        if (cities.Count == 0) return null;
        WarSim.State state = WarSim.Create(w.seed, GeoOf(w), w.fast, cities, builds);
        state.nameA = w.nameA;
        state.nameB = w.nameB;
        var sim = new WarSim(state);
        sim.Event("Второй день: деревни ожили на карте войны. Добывайте, учите бойцов в казармах — они сами выходят на карту. Нейтральные земли откроются завтра");
        WarLog(w, "Карта войны создана");
        SaveSim(w, sim);
        return sim;
    }

    /// <summary>Местность города для карты войны: под мостами - брод (по мосту через реку ходят).</summary>
    static string TerrainWithBridges(CityData c)
    {
        int n = c.mapSize;
        if (n <= 0 || n > 400 || string.IsNullOrEmpty(c.terrain)) return null;
        int half = n / 2;
        var grid = new char[n * n];
        for (int i = 0; i < grid.Length; i++) grid[i] = TerrainCode.Grass;
        TerrainCode.Decode(n, c.terrain, (x, y, ch) => grid[(y + half) * n + x + half] = ch);
        if (c.buildings != null)
            foreach (var b in c.buildings)
            {
                if (b == null || b.type != "Мост") continue;
                int bw = Math.Max(1, b.w), bh = Math.Max(1, b.h);
                for (int y = b.y; y < b.y + bh; y++)
                    for (int x = b.x; x < b.x + bw; x++)
                        if (x >= -half && y >= -half && x < n - half && y < n - half) grid[(y + half) * n + x + half] = WarRules.BridgeCode;
            }
        return TerrainCode.Encode(n, (x, y) => grid[(y + half) * n + x + half]);
    }

    WarSim LoadSim(StoredWar w)
    {
        string p = WarStatePath(w.id);
        if (!File.Exists(p)) return null;
        try
        {
            WarSim.State s = JsonUtility.FromJson<WarSim.State>(File.ReadAllText(p));
            return s != null && s.cities != null && s.cities.Count > 0 ? new WarSim(s) : null;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Сервер] Карта войны не прочиталась: " + e.Message);
            return null;
        }
    }

    void SaveSim(StoredWar w, WarSim sim)
    {
        try
        {
            string json;
            lock (sim) json = JsonUtility.ToJson(sim.S);
            Directory.CreateDirectory(WarDir(w.id));
            WriteSafe(WarStatePath(w.id), json);
        }
        catch (Exception e) { Debug.LogWarning("[Сервер] Карта войны не сохранилась: " + e.Message); }
    }

    // ---------- Итоги ----------

    void FinishWar(StoredWar w, WarSim sim, int win)
    {
        int a, b;
        lock (sim) sim.Score(out a, out b);
        if (win == 0 && a != b) win = a > b ? 1 : 2;
        w.finished = true;
        w.winner = win;
        w.scoreA = a;
        w.scoreB = b;
        string winName = win == 1 ? w.nameA : win == 2 ? w.nameB : null;
        w.result = win == 0 ? $"Ничья: {a}:{b}" : $"Победил клан «{winName}» ({a}:{b})";
        WarLog(w, w.result);
        lock (sim) sim.Event(w.result);
        SaveSim(w, sim);
        sims.Remove(w.id);

        // Домой с войны приходит только рейтинг клана (лиги): за сильного соперника больше, за слабого меньше.
        // Ресурсы, бойцы и всё остальное остаются на войне.
        StoredClan ca = db.clans.Find(x => x.id == w.clanA), cb = db.clans.Find(x => x.id == w.clanB);
        int rA = ca != null ? ca.rating : 0, rB = cb != null ? cb.rating : 0;
        foreach (var pair in new[] { Tuple.Create(ca, 1), Tuple.Create(cb, 2) })
        {
            StoredClan c = pair.Item1;
            if (c == null) continue;
            int mine = pair.Item2 == 1 ? rA : rB, theirs = pair.Item2 == 1 ? rB : rA;
            string foe = pair.Item2 == 1 ? w.nameB : w.nameA;
            int d = win == 0 ? Leagues.WarDrawClan
                  : win == pair.Item2 ? Math.Max(5, Leagues.WarWinClan + Leagues.Bonus(mine, theirs, 15))
                  : -Math.Max(3, Leagues.WarLoseClan - Leagues.Bonus(mine, theirs, 10));
            if (c.warId == w.id) c.warId = null;
            SystemMessage(c, $"Война окончена. {w.result}. Рейтинг клана: {Signed(d)}");
            ChangeClanRating(c, d, win == 0 ? $"Война с «{foe}»: ничья" : win == pair.Item2 ? $"Война с «{foe}»: победа" : $"Война с «{foe}»: поражение");
        }
        Debug.Log($"[Сервер] Клановая война окончена: {w.result}");
    }
}
