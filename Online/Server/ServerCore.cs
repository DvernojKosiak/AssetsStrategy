using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// Логика сервера: аккаунты, сессии, города. Не зависит от сети и от сцены -
/// её вызывает StrategycaServer (HTTP) из своих потоков, поэтому всё под одной блокировкой.
/// Данные - JSON-файлы в папке dataDir: accounts.json и cities/&lt;id аккаунта&gt;.json.
/// </summary>
public partial class ServerCore
{
    const float SessionDays = 30f;

    [Serializable]
    partial class StoredAccount   // ещё поля - в ServerSafety.cs
    {
        public AccountProfile profile;
        public string salt;
        public string hash;
        public string shieldUtc;                                     // до этого времени на деревню не нападают
        public List<RaidReport> pending = new List<RaidReport>();    // набеги, которые игра ещё не учла
        public List<MarketSale> sales = new List<MarketSale>();      // проданные лоты, золото за которые игра ещё не получила
        public string clanId;
        public List<WarGrant> grants = new List<WarGrant>();         // награды за войну, которые игра ещё не получила
        public int bestLeague;                                       // лучшая лига, до которой доходил (награда - один раз)
        public List<RatingChange> ratingLog = new List<RatingChange>();   // последние изменения рейтинга
    }

    [Serializable]
    class StoredClan
    {
        public string id;
        public string name;
        public string description;
        public string leaderId;
        public List<string> coLeaders = new List<string>();   // заместители главы
        public List<string> elders = new List<string>();      // старейшины
        public string createdUtc;
        public List<string> members = new List<string>();
        public List<ClanMessage> chat = new List<ClanMessage>();
        public int lastN;
        public int rating;
        public string warSearchUtc;    // ищет соперника с этого времени ("" - не ищет)
        public string warId;           // идущая война
        public string lastWarId;       // последняя война (итоги показываем, пока не начнут новую)
        public int bestLeague;         // лучшая лига клана (награда участникам - один раз)
        public List<RatingChange> ratingLog = new List<RatingChange>();
    }

    [Serializable]
    class StoredRaid
    {
        public string id;
        public string attackerId;
        public string defenderId;
        public string createdUtc;
        public bool started;
        public bool live;     // бой идёт вживую в деревне защитника (его игра считает бой)
        public int lockMin;   // сколько минут сервер держит набег (0 - старая запись: Api.OldRaidLockMinutes)
    }

    [Serializable]
    class StoredSession
    {
        public string token;
        public string accountId;
        public string expiresUtc;
    }

    [Serializable]
    partial class Db   // ещё поля - в ServerSafety.cs
    {
        public int version = 1;
        public List<StoredAccount> accounts = new List<StoredAccount>();
        public List<StoredSession> sessions = new List<StoredSession>();
        public List<StoredRaid> raids = new List<StoredRaid>();      // идущие набеги (на деревню - не больше одного)
        public List<MarketLot> lots = new List<MarketLot>();         // рынок между игроками
        public List<StoredPurchase> purchases = new List<StoredPurchase>();   // засчитанные покупки марок (каждая - один раз)
        public List<StoredClan> clans = new List<StoredClan>();
        public string worldStartUtc;   // начало общего игрового времени (первый понедельник, 06:00)
        public List<StoredWar> wars = new List<StoredWar>();
        public string adminId;         // админ сервера (кнопки дней войны); пусто - первый аккаунт
    }

    readonly object sync = new object();
    readonly string dataDir;
    readonly string dbPath;
    readonly string cityDir;
    Db db;

    // Живые бои (только в памяти): последний снимок от нападающего и приказы защитника
    class LiveBattle
    {
        public BattleSnapshot snapshot;
        public readonly List<BattleCommand> commands = new List<BattleCommand>();
        public DateTime defenderSeen = DateTime.MinValue;
        // Живой набег
        public List<SiegeUnit> army;          // армия, которую выставит игра защитника
        public bool camp;
        public int side;                      // с какой стороны деревни защитника подходит армия (final-8)
        public DateTime arriveAt, lastPush, lastPull;
        public DateTime snapAt = DateTime.MinValue;   // final-13: когда пришёл последний снимок нападающего
        public bool spawned;                  // защитник выставил армию и шлёт снимки
        public SiegeFrame frame;              // последний снимок деревни защитника
        public readonly List<SiegeShot> shots = new List<SiegeShot>();   // выстрелы, которые нападающий ещё не видел
        // Запись боя для повтора (final-9)
        public BattleReplay rec;
        public DateTime recStart, recLast;
        public readonly HashSet<int> recNamed = new HashSet<int>();
        public readonly List<ReplayShot> recShots = new List<ReplayShot>();   // выстрелы до следующего кадра (обновление 12)
        public readonly Dictionary<string, int> recBuild = new Dictionary<string, int>();
    }
    readonly Dictionary<string, LiveBattle> battles = new Dictionary<string, LiveBattle>();
    readonly Dictionary<string, DateTime> lastSeen = new Dictionary<string, DateTime>();
    readonly Dictionary<string, DateTime> homeSeen = new Dictionary<string, DateTime>();   // защитник в своей деревне (может принять живой бой)

    LiveBattle BattleOf(string raidId)
    {
        if (!battles.TryGetValue(raidId, out LiveBattle b)) battles[raidId] = b = new LiveBattle();
        return b;
    }

    public string DataDir => dataDir;

    public ServerCore(string dataDir)
    {
        this.dataDir = dataDir;
        dbPath = Path.Combine(dataDir, "accounts.json");
        cityDir = Path.Combine(dataDir, "cities");
        Directory.CreateDirectory(cityDir);
        Load();
    }

    public int PlayerCount { get { lock (sync) return RealPlayers(); } }   // без NPC-деревень

    // ---------- Файлы ----------

    void Load()
    {
        try
        {
            if (File.Exists(dbPath)) db = JsonUtility.FromJson<Db>(File.ReadAllText(dbPath));
        }
        catch (Exception e) { Debug.LogWarning("[Сервер] Не удалось прочитать " + dbPath + ": " + e.Message); }
        if (db == null) db = new Db();
        if (db.accounts == null) db.accounts = new List<StoredAccount>();
        if (db.sessions == null) db.sessions = new List<StoredSession>();
        if (db.raids == null) db.raids = new List<StoredRaid>();
        if (db.lots == null) db.lots = new List<MarketLot>();
        if (db.clans == null) db.clans = new List<StoredClan>();
        if (db.wars == null) db.wars = new List<StoredWar>();
        if (string.IsNullOrEmpty(db.worldStartUtc)) { db.worldStartUtc = Now(); Save(); }
        FixShopData();
        FixSafetyData();
        foreach (var c in db.clans) { if (c.members == null) c.members = new List<string>(); if (c.chat == null) c.chat = new List<ClanMessage>(); if (c.coLeaders == null) c.coLeaders = new List<string>(); if (c.elders == null) c.elders = new List<string>(); if (c.ratingLog == null) c.ratingLog = new List<RatingChange>(); }
        foreach (var a in db.accounts)
        {
            if (a == null) continue;
            if (a.pending == null) a.pending = new List<RaidReport>();
            if (a.sales == null) a.sales = new List<MarketSale>();
            if (a.grants == null) a.grants = new List<WarGrant>();
            if (a.ratingLog == null) a.ratingLog = new List<RatingChange>();
        }
        EnsureNpcs();   // обновление 14: NPC-деревни
    }

    // Пишем во временный файл и подменяем - чтобы при сбое не остался обрезанный файл
    static void WriteSafe(string path, string text)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);
    }

    void Save() { WriteSafe(dbPath, JsonUtility.ToJson(db, true)); }

    string CityPath(string accountId) { return Path.Combine(cityDir, accountId + ".json"); }

    // ---------- Вспомогательное ----------

    static string Key(string login) { return (login ?? "").Trim().ToLowerInvariant(); }
    static string Now() { return DateTime.UtcNow.ToString("o"); }

    static string NewSalt()
    {
        byte[] b = new byte[16];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
        return Convert.ToBase64String(b);
    }

    static string Hash(string salt, string password)
    {
        using (var sha = SHA256.Create())
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(salt + ":" + password)));
    }

    static DateTime ParseUtc(string s)
    {
        return DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime d) ? d.ToUniversalTime() : DateTime.MinValue;
    }

    StoredAccount FindByLogin(string login)
    {
        string k = Key(login);
        return db.accounts.Find(a => a.profile != null && Key(a.profile.login) == k);
    }

    StoredAccount FindById(string id) { return db.accounts.Find(a => a.profile != null && a.profile.id == id); }

    StoredAccount FindByToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        db.sessions.RemoveAll(s => s == null || ParseUtc(s.expiresUtc) < DateTime.UtcNow);
        StoredSession ss = db.sessions.Find(s => s.token == token);
        if (ss != null) lastSeen[ss.accountId] = DateTime.UtcNow;   // для «в сети» в клане
        return ss != null ? FindById(ss.accountId) : null;
    }

    string OpenSession(StoredAccount a)
    {
        string token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        db.sessions.Add(new StoredSession { token = token, accountId = a.profile.id, expiresUtc = DateTime.UtcNow.AddDays(SessionDays).ToString("o") });
        a.profile.logins++;
        a.profile.lastLoginUtc = Now();
        Save();
        return token;
    }

    static ApiResponse Ok(StoredAccount a, string token)
    {
        return new ApiResponse { ok = true, profile = a.profile.Copy(), token = token };
    }

    const string SessionGone = "Сессия устарела, войдите заново";

    // ---------- Запросы ----------

    /// <summary>Выполнить запрос по пути (см. Api). Вызывается из любого потока.</summary>
    public ApiResponse Handle(string path, ApiRequest req)
    {
        ResolveGoogleCode(path, req);   // запрос к Google - до блокировки, чтобы не держать остальных игроков
        ApiResponse res = HandleInner(path, req);
        if (res != null)
        {
            res.serverUtc = Now();
            res.worldStartUtc = db.worldStartUtc;
        }
        return res;
    }

    ApiResponse HandleInner(string path, ApiRequest req)
    {
        if (req == null) return ApiResponse.Fail("Пустой запрос");
        lock (sync)
        {
            ApiResponse res = Route(path, req);
            if (res != null && res.ok) AddShield(res, req.token);
            return res;
        }
    }

    ApiResponse Route(string path, ApiRequest req)
    {
        {
            switch (path)
            {
                case Api.Ping: return new ApiResponse { ok = true, serverName = "Strategyca (тестовый сервер)", players = RealPlayers() };
                case Api.Register: return Register(req);
                case Api.Login: return Login(req);
                case Api.Resume: return Resume(req);
                case Api.Logout: return Logout(req);
                case Api.SaveProfile: return SaveProfile(req);
                case Api.ChangePassword: return ChangePassword(req);
                case Api.LoadCity: return LoadCity(req);
                case Api.SaveCity: return SaveCity(req);
                case Api.FindOpponent: return FindOpponent(req);
                case Api.StartRaid: return StartRaid(req);
                case Api.FinishRaid: return FinishRaid(req);
                case Api.MarketList: return MarketList(req);
                case Api.MarketPost: return MarketPost(req);
                case Api.MarketBuy: return MarketBuy(req);
                case Api.MarketCancel: return MarketCancel(req);
                case Api.MarketSlotBuy: return MarketSlotBuy(req);
                case Api.ShopClaim: return ShopClaim(req);
                case Api.ShieldDrop: return ShieldDrop(req);
                case Api.ClanInfo: return ClanInfo(req);
                case Api.ClanCreate: return ClanCreate(req);
                case Api.ClanJoin: return ClanJoin(req);
                case Api.ClanLeave: return ClanLeave(req);
                case Api.ClanKick: return ClanKick(req);
                case Api.ClanRole: return ClanRole(req);
                case Api.ClanSay: return ClanSay(req);
                case Api.BattlePush: return BattlePush(req);
                case Api.BattleWatch: return BattleWatch(req);
                case Api.BattleReplay: return BattleReplayGet(req);
                case Api.SiegeArrive: return SiegeArrive(req);
                case Api.SiegePush: return SiegePush(req);
                case Api.SiegePull: return SiegePull(req);
                case Api.WarInfo: return WarInfoReq(req);
                case Api.WarStart: return WarStartReq(req);
                case Api.WarCancel: return WarCancelReq(req);
                case Api.WarScheme: return WarSchemeReq(req);
                case Api.WarStatic: return WarStaticReq(req);
                case Api.WarFrame: return WarFrameReq(req);
                case Api.WarCmd: return WarCmdReq(req);
                case Api.WarAdmin: return WarAdminReq(req);
                case Api.WarCityLoad: return WarCityLoadReq(req);
                case Api.WarCitySave: return WarCitySaveReq(req);
                case Api.WarDeploy: return WarDeployReq(req);
                case Api.WarMarketList: return WarMarketListReq(req);
                case Api.WarMarketPost: return WarMarketPostReq(req);
                case Api.WarMarketBuy: return WarMarketBuyReq(req);
                case Api.WarMarketCancel: return WarMarketCancelReq(req);
                case Api.LeagueTop: return LeagueTopReq(req);
                case Api.DeleteAccount: return DeleteAccountReq(req);
                case Api.ChatReport: return ChatReportReq(req);
                case Api.ChatBlock: return ChatBlockReq(req);
                case Api.GoogleLogin: return GoogleLoginReq(req);
                case Api.GoogleLink: return GoogleLinkReq(req);
                case Api.PushToken: return PushTokenReq(req);
                case Api.PushPlan: return PushPlanReq(req);
                default: return ApiResponse.Fail("Неизвестный запрос " + path);
            }
        }
    }

    ApiResponse Register(ApiRequest r)
    {
        string err = AccountRules.CheckLogin(r.login) ?? AccountRules.CheckName(r.displayName) ?? AccountRules.CheckPassword(r.password);
        if (err != null) return ApiResponse.Fail(err);
        if (FindByLogin(r.login) != null) return ApiResponse.Fail("Такой логин уже занят");

        string salt = NewSalt();
        var a = new StoredAccount
        {
            profile = new AccountProfile
            {
                id = Guid.NewGuid().ToString("N"),
                login = r.login.Trim(),
                displayName = r.displayName.Trim(),
                createdUtc = Now(),
                lotSlots = LotRules.StartSlots,
            },
            salt = salt,
            hash = Hash(salt, r.password),
        };
        db.accounts.Add(a);
        string token = OpenSession(a);
        Debug.Log($"[Сервер] Новый игрок: {a.profile.login} ({a.profile.displayName})");
        return Ok(a, token);
    }

    ApiResponse Login(ApiRequest r)
    {
        StoredAccount a = FindByLogin(r.login);
        if (a == null || string.IsNullOrEmpty(a.hash) || a.hash != Hash(a.salt, r.password ?? "")) return ApiResponse.Fail("Неверный логин или пароль");
        return Ok(a, OpenSession(a));
    }

    ApiResponse Resume(ApiRequest r)
    {
        StoredAccount a = FindByToken(r.token);
        if (a == null) return ApiResponse.Fail(SessionGone);
        a.profile.logins++;
        a.profile.lastLoginUtc = Now();
        Save();
        return Ok(a, r.token);
    }

    ApiResponse Logout(ApiRequest r)
    {
        StoredAccount a = FindByToken(r.token);
        if (a != null && !string.IsNullOrEmpty(r.pushToken)) a.pushDevices.RemoveAll(d => d == null || d.token == r.pushToken);   // уведомления на этот телефон больше не нужны
        db.sessions.RemoveAll(s => s == null || s.token == r.token);
        Save();
        return new ApiResponse { ok = true };
    }

    ApiResponse SaveProfile(ApiRequest r)
    {
        StoredAccount a = FindByToken(r.token);
        if (a == null) return ApiResponse.Fail(SessionGone);
        if (r.profile == null || r.profile.id != a.profile.id) return ApiResponse.Fail("Чужой профиль");
        // Логин, id и даты меняет только сервер; игра присылает имя и время в игре
        if (AccountRules.CheckName(r.profile.displayName) == null) a.profile.displayName = r.profile.displayName.Trim();
        a.profile.playSeconds = Mathf.Max(a.profile.playSeconds, r.profile.playSeconds);
        Save();
        return Ok(a, r.token);
    }

    ApiResponse ChangePassword(ApiRequest r)
    {
        StoredAccount a = FindByToken(r.token);
        if (a == null) return ApiResponse.Fail(SessionGone);
        if (!string.IsNullOrEmpty(a.hash) && a.hash != Hash(a.salt, r.password ?? "")) return ApiResponse.Fail("Старый пароль неверный");   // у аккаунта Google Play пароля ещё нет
        string err = AccountRules.CheckPassword(r.newPassword);
        if (err != null) return ApiResponse.Fail(err);
        a.salt = NewSalt();
        a.hash = Hash(a.salt, r.newPassword);
        a.profile.noPassword = false;
        // Остальные сессии закрываются, текущая остаётся
        db.sessions.RemoveAll(s => s == null || (s.accountId == a.profile.id && s.token != r.token));
        Save();
        return Ok(a, r.token);
    }

    ApiResponse LoadCity(ApiRequest r)
    {
        StoredAccount a = FindByToken(r.token);
        if (a == null) return ApiResponse.Fail(SessionGone);
        string p = CityPath(a.profile.id);
        if (!File.Exists(p)) return new ApiResponse { ok = true, hasCity = false };

        // Пока игрока не было, на него нападали: потери вычитаются из города прямо здесь
        var reports = new List<RaidReport>(a.pending);
        var sales = new List<MarketSale>(a.sales);
        var grants = new List<WarGrant>(a.grants);
        if (reports.Count > 0 || sales.Count > 0 || grants.Count > 0)
        {
            CityData c = ReadCity(a.profile.id);
            if (c != null)
            {
                foreach (var rep in reports) if (!rep.live) CityLoss.Apply(c, rep);   // живой бой: потери уже в городе
                foreach (var s in sales) c.marks += Mathf.Max(0, s.price);   // марки за проданное на рынке
                foreach (var g in grants) { c.gold += g.gold; c.wood += g.wood; c.stone += g.stone; c.food += g.food; c.iron += g.iron; c.marks += Mathf.Max(0, g.marks); }   // награды за войну и купленные марки
                WriteSafe(p, JsonUtility.ToJson(c));
            }
            a.pending.Clear();
            a.sales.Clear();
            a.grants.Clear();
            Save();
        }
        return new ApiResponse { ok = true, hasCity = true, city = File.ReadAllText(p), reports = reports, reportsApplied = true,
                                 sales = sales, salesApplied = true, grants = grants, incomingName = IncomingName(a) };
    }

    ApiResponse SaveCity(ApiRequest r)
    {
        StoredAccount a = FindByToken(r.token);
        if (a == null) return ApiResponse.Fail(SessionGone);
        if (string.IsNullOrEmpty(r.city) || r.city.Length > Api.MaxCityJson) return ApiResponse.Fail("Город не подходит для сохранения");

        CityData c = null;
        try { c = JsonUtility.FromJson<CityData>(r.city); }
        catch (Exception) { }
        if (c == null || c.buildings == null || c.buildings.Count == 0) return ApiResponse.Fail("Город повреждён, не сохранён");

        // Город всегда принадлежит владельцу аккаунта
        c.ownerId = a.profile.id;
        c.ownerName = a.profile.displayName;
        c.savedUtc = Now();
        WriteSafe(CityPath(a.profile.id), JsonUtility.ToJson(c));

        // Отчёты, которые игра уже учла (вычла потери у себя), убираем; остальные - отдаём ей
        if (r.acks != null && r.acks.Count > 0)
        {
            int before = a.pending.Count + a.sales.Count + a.grants.Count;
            a.pending.RemoveAll(rep => rep == null || r.acks.Contains(rep.id));
            a.sales.RemoveAll(s => s == null || r.acks.Contains(s.id));
            a.grants.RemoveAll(g => g == null || r.acks.Contains(g.id));
            if (a.pending.Count + a.sales.Count + a.grants.Count != before) Save();
        }
        return new ApiResponse { ok = true, reports = new List<RaidReport>(a.pending), sales = new List<MarketSale>(a.sales),
                                 grants = new List<WarGrant>(a.grants), incomingName = IncomingName(a) };
    }

    // ---------- Набеги ----------

    CityData ReadCity(string accountId)
    {
        string p = CityPath(accountId);
        if (!File.Exists(p)) return null;
        try { return JsonUtility.FromJson<CityData>(File.ReadAllText(p)); }
        catch (Exception) { return null; }
    }

    // Набеги старше RaidLockMinutes считаются брошенными (обновление 14: у каждого набега свой срок - по сборке нападающего)
    void DropStaleRaids()
    {
        DateTime now = DateTime.UtcNow;
        db.raids.RemoveAll(x => x == null || ParseUtc(x.createdUtc) < now.AddMinutes(-RaidLock(x)));
    }

    static double RaidLock(StoredRaid x) { return x.lockMin > 0 ? x.lockMin : Api.OldRaidLockMinutes; }

    // Новая сборка - короткий набег (12 минут на бой), старая - по-старому (30 минут на бой)
    static int LockFor(ApiRequest r) { return (int)(r != null && r.build >= Api.ShortRaidBuild ? Api.RaidLockMinutes : Api.OldRaidLockMinutes); }

    bool Shielded(StoredAccount a) { return ParseUtc(a.shieldUtc) > DateTime.UtcNow; }

    string IncomingName(StoredAccount a)
    {
        DropStaleRaids();
        StoredRaid raid = db.raids.Find(x => x.defenderId == a.profile.id && x.started);
        if (raid == null) return null;
        StoredAccount att = FindById(raid.attackerId);
        return att != null ? att.profile.displayName : "?";
    }

    ApiResponse FindOpponent(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        DropStaleRaids();
        if (db.raids.Exists(x => x.attackerId == me.profile.id && x.started)) return ApiResponse.Fail("Ваш набег ещё идёт");
        // Прежний найденный, но не начатый противник освобождается
        db.raids.RemoveAll(x => x.attackerId == me.profile.id && !x.started);

        CityData mine = ReadCity(me.profile.id);
        int myLevel = CityLoss.HallLevel(mine);
        StoredClan myClan = ClanOf(me);   // своих по клану в поиске нет (final-8)

        // Кандидаты: у кого есть город, кто не под щитом и на кого сейчас не идёт другой набег.
        // Похожие по уровню Цитадели - чаще (вес падает с разницей уровней)
        var cand = new List<StoredAccount>();
        var weight = new List<float>();
        float sum = 0f;
        var rnd = new System.Random();
        foreach (var a in db.accounts)
        {
            if (a == null || a.profile == null || a.profile.id == me.profile.id) continue;
            if (Shielded(a) || db.raids.Exists(x => x.defenderId == a.profile.id)) continue;
            if (myClan != null && myClan.members.Contains(a.profile.id)) continue;
            CityData c = ReadCity(a.profile.id);
            if (c == null || c.buildings == null || c.buildings.Count == 0) continue;
            if (CityLoss.IsNewbie(c)) continue;   // защита новичка: ещё нет казармы и Цитадель Ур.1
            float w = 1f / (1f + 2f * Mathf.Abs(CityLoss.HallLevel(c) - myLevel));
            cand.Add(a);
            weight.Add(w);
            sum += w;
        }
        if (cand.Count == 0) return ApiResponse.Fail("Сейчас не на кого напасть: другие деревни под защитой, в вашем клане, новички или их нет");

        double pick = rnd.NextDouble() * sum;
        int idx = 0;
        for (; idx < cand.Count - 1; idx++) { pick -= weight[idx]; if (pick <= 0) break; }
        StoredAccount target = cand[idx];
        if (IsNpc(target)) NpcRefresh(target);   // NPC: деревня с тем, что она успела восстановить после прошлых набегов

        var raid = new StoredRaid { id = Guid.NewGuid().ToString("N"), attackerId = me.profile.id, defenderId = target.profile.id, createdUtc = Now(), lockMin = LockFor(r) };
        db.raids.Add(raid);
        Save();
        return new ApiResponse { ok = true, raidId = raid.id, hasCity = true, city = File.ReadAllText(CityPath(target.profile.id)) };
    }

    ApiResponse StartRaid(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        DropStaleRaids();
        StoredRaid raid = db.raids.Find(x => x.id == r.raidId && x.attackerId == me.profile.id);
        if (raid == null) return ApiResponse.Fail("Противник больше не доступен — найдите другого");
        StoredAccount def = FindById(raid.defenderId);
        if (def != null && Shielded(def)) { db.raids.Remove(raid); Save(); return ApiResponse.Fail("Деревня уже под защитой — найдите другую"); }
        raid.started = true;
        raid.createdUtc = Now();   // блокировка - от начала похода
        raid.lockMin = LockFor(r);
        Save();
        if (def != null) PushRaidStarted(def, me);
        return new ApiResponse { ok = true, raidId = raid.id };
    }

    // ---------- Рынок ----------

    ApiResponse MarketList(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        var lots = new List<MarketLot>(db.lots);
        lots.Reverse();   // новые - первыми
        if (lots.Count > 300) lots.RemoveRange(300, lots.Count - 300);
        return new ApiResponse { ok = true, lots = lots, sales = new List<MarketSale>(me.sales) };
    }

    static string Cut(string s, int n) { return string.IsNullOrEmpty(s) ? s : (s.Length > n ? s.Substring(0, n) : s); }

    ApiResponse MarketPost(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        MarketLot l = r.lot;
        if (l == null || l.units <= 0 || l.price <= 0 || l.units > 100000 || l.price > 1000000) return ApiResponse.Fail("Неверный лот");
        if (l.item > 0 && (l.thing == null || string.IsNullOrEmpty(l.name))) return ApiResponse.Fail("Неверный лот");
        int slots = LotSlots(me);
        if (db.lots.FindAll(x => x.sellerId == me.profile.id).Count >= slots)
            return ApiResponse.Fail(slots < Api.MaxLotsPerPlayer ? $"Все места под лоты заняты ({slots}) — снимите лот или откройте новое место"
                                                                 : $"Больше {Api.MaxLotsPerPlayer} лотов выставить нельзя — снимите какой-нибудь");
        var lot = new MarketLot
        {
            id = Guid.NewGuid().ToString("N"), sellerId = me.profile.id, sellerName = me.profile.displayName,
            kind = l.kind, tier = l.tier, units = l.units, price = l.price, createdUtc = Now(),
            item = l.item, name = l.item > 0 ? Cut(l.name, 60) : null, info = l.item > 0 ? Cut(l.info, 160) : null, thing = l.item > 0 ? l.thing : null,
        };
        db.lots.Add(lot);
        Save();
        return new ApiResponse { ok = true, lot = lot };
    }

    ApiResponse MarketBuy(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        MarketLot lot = db.lots.Find(x => x.id == r.lotId);
        if (lot == null) return ApiResponse.Fail("Этот лот уже купили или сняли");
        if (lot.sellerId == me.profile.id) return ApiResponse.Fail("Свой лот купить нельзя — его можно снять");
        db.lots.Remove(lot);
        StoredAccount seller = FindById(lot.sellerId);
        if (seller != null)
            seller.sales.Add(new MarketSale
            {
                id = Guid.NewGuid().ToString("N"), buyerName = me.profile.displayName,
                kind = lot.kind, tier = lot.tier, units = lot.units, price = lot.price, timeUtc = Now(),
                item = lot.item, name = lot.name,
            });
        Save();
        return new ApiResponse { ok = true, lot = lot };
    }

    ApiResponse MarketCancel(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        MarketLot lot = db.lots.Find(x => x.id == r.lotId && x.sellerId == me.profile.id);
        if (lot == null) return ApiResponse.Fail("Лот уже продан");
        db.lots.Remove(lot);
        Save();
        return new ApiResponse { ok = true, lot = lot };
    }

    // ---------- Кланы ----------

    StoredClan ClanOf(StoredAccount a)
    {
        if (a == null || string.IsNullOrEmpty(a.clanId)) return null;
        StoredClan c = db.clans.Find(x => x.id == a.clanId);
        if (c == null || !c.members.Contains(a.profile.id)) { a.clanId = null; return null; }
        return c;
    }

    void SystemMessage(StoredClan c, string text)
    {
        c.lastN++;
        c.chat.Add(new ClanMessage { n = c.lastN, text = text, timeUtc = Now(), system = true });
        if (c.chat.Count > Api.ClanChatKeep) c.chat.RemoveRange(0, c.chat.Count - Api.ClanChatKeep);
    }

    ClanView ViewOf(StoredClan c)
    {
        var v = new ClanView { id = c.id, name = c.name, description = c.description, rating = c.rating };
        foreach (var id in c.members)
        {
            StoredAccount m = FindById(id);
            if (m == null) continue;
            bool online = lastSeen.TryGetValue(id, out DateTime t) && (DateTime.UtcNow - t).TotalSeconds < 60;
            v.members.Add(new ClanMember { id = id, name = m.profile.displayName, leader = id == c.leaderId, role = RoleOf(c, id), online = online, hall = CityLoss.HallLevel(ReadCity(id)), rating = m.profile.rating });
        }
        v.members.Sort((a, b) => a.role != b.role ? b.role.CompareTo(a.role) : b.hall.CompareTo(a.hall));
        return v;
    }

    static int RoleOf(StoredClan c, string id)
    {
        if (c == null || id == null) return ClanRoles.Member;
        if (id == c.leaderId) return ClanRoles.Leader;
        if (c.coLeaders != null && c.coLeaders.Contains(id)) return ClanRoles.CoLeader;
        if (c.elders != null && c.elders.Contains(id)) return ClanRoles.Elder;
        return ClanRoles.Member;
    }

    static void SetRole(StoredClan c, string id, int role)
    {
        c.coLeaders.Remove(id);
        c.elders.Remove(id);
        if (role == ClanRoles.CoLeader) c.coLeaders.Add(id);
        else if (role == ClanRoles.Elder) c.elders.Add(id);
    }

    /// <summary>Глава или заместитель: начинает войну, расставляет города.</summary>
    static bool CanLeadWar(StoredClan c, StoredAccount a) { return a != null && ClanRoles.CanLeadWar(RoleOf(c, a.profile.id)); }

    static string CleanText(string s, int max)
    {
        s = (s ?? "").Replace("<", "‹").Replace(">", "›").Replace("\n", " ").Trim();   // без разметки и переносов
        return s.Length > max ? s.Substring(0, max) : s;
    }

    ApiResponse ClanInfo(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        StoredClan c = ClanOf(me);
        if (c == null)
        {
            // Не в клане: список кланов (поиск по названию), где есть место
            string q = (r.clanName ?? "").Trim().ToLowerInvariant();
            var list = new List<ClanBrief>();
            foreach (var x in db.clans)
            {
                if (x.members.Count >= Api.MaxClanMembers) continue;
                if (q != "" && !x.name.ToLowerInvariant().Contains(q)) continue;
                list.Add(new ClanBrief { id = x.id, name = x.name, description = x.description, members = x.members.Count, rating = x.rating });
            }
            list.Sort((a, b) => b.members.CompareTo(a.members));
            if (list.Count > 50) list.RemoveRange(50, list.Count - 50);
            return new ApiResponse { ok = true, clans = list };
        }
        var msgs = ChatFor(me, c, r.since);
        return new ApiResponse { ok = true, clan = ViewOf(c), messages = msgs };
    }

    ApiResponse ClanCreate(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        if (ClanOf(me) != null) return ApiResponse.Fail("Вы уже в клане — сначала выйдите из него");
        string name = CleanText(r.clanName, 20);
        if (name.Length < 3) return ApiResponse.Fail("Название клана - от 3 до 20 символов");
        if (db.clans.Exists(x => x.name.ToLowerInvariant() == name.ToLowerInvariant())) return ApiResponse.Fail("Клан с таким названием уже есть");
        var c = new StoredClan { id = Guid.NewGuid().ToString("N"), name = name, description = CleanText(r.text, 120), leaderId = me.profile.id, createdUtc = Now() };
        c.members.Add(me.profile.id);
        SystemMessage(c, $"{me.profile.displayName} основал(а) клан");
        db.clans.Add(c);
        me.clanId = c.id;
        Save();
        return new ApiResponse { ok = true, clan = ViewOf(c), messages = ChatFor(me, c, 0) };
    }

    ApiResponse ClanJoin(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        if (ClanOf(me) != null) return ApiResponse.Fail("Вы уже в клане — сначала выйдите из него");
        StoredClan c = db.clans.Find(x => x.id == r.clanId);
        if (c == null) return ApiResponse.Fail("Клан не найден");
        if (c.members.Count >= Api.MaxClanMembers) return ApiResponse.Fail("В клане нет мест");
        c.members.Add(me.profile.id);
        me.clanId = c.id;
        SystemMessage(c, $"{me.profile.displayName} вступил(а) в клан");
        Save();
        return new ApiResponse { ok = true, clan = ViewOf(c), messages = ChatFor(me, c, 0) };
    }

    // Убрать игрока из клана; ушёл глава - главным становится следующий; никого не осталось - клан распускается
    void RemoveMember(StoredClan c, StoredAccount a, string why)
    {
        c.members.Remove(a.profile.id);
        c.coLeaders.Remove(a.profile.id);
        c.elders.Remove(a.profile.id);
        a.clanId = null;
        if (c.members.Count == 0) { db.clans.Remove(c); return; }
        SystemMessage(c, why);
        if (c.leaderId == a.profile.id)
        {
            // Новый глава - заместитель, иначе старейшина, иначе первый в списке
            c.leaderId = c.coLeaders.Count > 0 ? c.coLeaders[0] : c.elders.Count > 0 ? c.elders[0] : c.members[0];
            SetRole(c, c.leaderId, ClanRoles.Member);
            StoredAccount nl = FindById(c.leaderId);
            if (nl != null) SystemMessage(c, $"Новый глава клана: {nl.profile.displayName}");
        }
    }

    ApiResponse ClanLeave(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        StoredClan c = ClanOf(me);
        if (c == null) return new ApiResponse { ok = true };
        RemoveMember(c, me, $"{me.profile.displayName} вышел(а) из клана");
        Save();
        return ClanInfo(r);
    }

    ApiResponse ClanKick(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        StoredClan c = ClanOf(me);
        if (c == null) return ApiResponse.Fail("Вы не в клане");
        StoredAccount who = FindById(r.memberId);
        if (who == null || who == me || !c.members.Contains(who.profile.id)) return ApiResponse.Fail("Такого игрока в клане нет");
        if (!ClanRoles.CanKick(RoleOf(c, me.profile.id), RoleOf(c, who.profile.id)))
            return ApiResponse.Fail("Исключать можно только тех, у кого роль ниже вашей (старейшина - участников, заместитель - и старейшин)");
        RemoveMember(c, who, $"{me.profile.displayName} исключил(а) из клана: {who.profile.displayName}");
        Save();
        return new ApiResponse { ok = true, clan = ViewOf(c), messages = ChatFor(me, c, r.since) };
    }

    ApiResponse ClanRole(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        StoredClan c = ClanOf(me);
        if (c == null) return ApiResponse.Fail("Вы не в клане");
        StoredAccount who = FindById(r.memberId);
        if (who == null || who == me || !c.members.Contains(who.profile.id)) return ApiResponse.Fail("Такого игрока в клане нет");
        int mine = RoleOf(c, me.profile.id), was = RoleOf(c, who.profile.id), to = r.role;
        if (!ClanRoles.CanSet(mine, was, to)) return ApiResponse.Fail("Так поменять роль вы не можете");
        string name = who.profile.displayName;
        if (to == ClanRoles.Leader)
        {
            // Передать главенство: прежний глава становится заместителем
            SetRole(c, who.profile.id, ClanRoles.Member);
            c.leaderId = who.profile.id;
            SetRole(c, me.profile.id, ClanRoles.CoLeader);
            SystemMessage(c, $"Новый глава клана: {name}");
        }
        else
        {
            SetRole(c, who.profile.id, to);
            if (to > was) SystemMessage(c, $"{me.profile.displayName} повысил(а) {name}: {ClanRoles.Name(to)}");
            else SystemMessage(c, $"{me.profile.displayName} понизил(а) {name}: {ClanRoles.Name(to)}");
        }
        Save();
        return new ApiResponse { ok = true, clan = ViewOf(c), messages = ChatFor(me, c, r.since) };
    }

    ApiResponse ClanSay(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        StoredClan c = ClanOf(me);
        if (c == null) return ApiResponse.Fail("Вы не в клане");
        if (me.profile.Muted) return ApiResponse.Fail("Чат для вас закрыт до " + MutedText(me) + " — на ваши сообщения пожаловались игроки");
        string text = CleanText(r.text, Api.ClanMessageMax);
        if (text.Length == 0) return ApiResponse.Fail("Пустое сообщение");
        c.lastN++;
        c.chat.Add(new ClanMessage { n = c.lastN, authorId = me.profile.id, author = me.profile.displayName, text = text, timeUtc = Now() });
        if (c.chat.Count > Api.ClanChatKeep) c.chat.RemoveRange(0, c.chat.Count - Api.ClanChatKeep);
        Save();
        return new ApiResponse { ok = true, clan = ViewOf(c), messages = ChatFor(me, c, r.since) };
    }

    ApiResponse BattlePush(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        StoredRaid raid = db.raids.Find(x => x.id == r.raidId && x.attackerId == me.profile.id && x.started);
        if (raid == null) return ApiResponse.Fail("Набег не найден");
        LiveBattle b = BattleOf(raid.id);
        if (r.battle != null) { r.battle.attackerName = me.profile.displayName; b.snapshot = r.battle; b.snapAt = DateTime.UtcNow; Record(b, r.battle); }
        var cmds = new List<BattleCommand>(b.commands);
        b.commands.Clear();
        return new ApiResponse { ok = true, commands = cmds, defenderLive = (DateTime.UtcNow - b.defenderSeen).TotalSeconds < 5 };
    }

    ApiResponse BattleWatch(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        if (r.home) homeSeen[me.profile.id] = DateTime.UtcNow;
        DropStaleRaids();
        StoredRaid raid = db.raids.Find(x => x.defenderId == me.profile.id && x.started);
        if (raid == null)
        {
            // Бой только что закончился - один раз отдаём последний снимок (с отметкой «окончен»)
            foreach (var kv in battles)
                if (kv.Value.snapshot != null && kv.Value.snapshot.phase == 3 && kv.Value.defenderSeen > DateTime.MinValue && r.raidId == kv.Key)
                {
                    BattleSnapshot last = kv.Value.snapshot;
                    battles.Remove(kv.Key);
                    return new ApiResponse { ok = true, raidId = kv.Key, battle = last };
                }
            return new ApiResponse { ok = true };
        }
        LiveBattle b = BattleOf(raid.id);
        if (r.live) b.defenderSeen = DateTime.UtcNow;
        if (r.commands != null) foreach (var c in r.commands) if (c != null && b.commands.Count < 200) b.commands.Add(c);
        var res = new ApiResponse { ok = true, raidId = raid.id, incomingName = IncomingName(me), battle = b.snapshot, siegeLive = raid.live };
        if (raid.live && !b.spawned && b.army != null) { res.siegeArmy = b.army; res.camp = b.camp; res.siegeSide = b.side; }   // выставить армию у себя
        // final-13: общий таймер набега - защитник видит, сколько осталось (как и нападающий)
        double snapAge = (DateTime.UtcNow - b.snapAt).TotalSeconds;
        if (b.snapshot != null && snapAge < 15 && b.snapshot.phase <= 2)
        {
            res.raidLeft = (float)Math.Max(0, b.snapshot.timeLeft - snapAge);
            res.raidPhase = b.snapshot.phase;
        }
        else if (raid.live && b.arriveAt > DateTime.MinValue)
        {
            res.raidLeft = (float)Math.Max(0, Api.RaidBattleSeconds - (DateTime.UtcNow - b.arriveAt).TotalSeconds);
            res.raidPhase = 1;
        }
        return res;
    }

    // ---------- Живой набег ----------

    bool AtHome(string accountId)
    {
        return homeSeen.TryGetValue(accountId, out DateTime t) && (DateTime.UtcNow - t).TotalSeconds < Api.SiegeHomeSeconds;
    }

    // Нападающий: армия подошла. Защитник дома - бой пойдёт у него вживую, иначе - как раньше (на копии деревни)
    ApiResponse SiegeArrive(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        StoredRaid raid = db.raids.Find(x => x.id == r.raidId && x.attackerId == me.profile.id && x.started);
        if (raid == null) return ApiResponse.Fail("Набег не найден");
        if (r.siegeArmy == null || r.siegeArmy.Count == 0 || r.siegeArmy.Count > 300 || !AtHome(raid.defenderId))
            return new ApiResponse { ok = true, siegeLive = false };
        LiveBattle b = BattleOf(raid.id);
        b.army = r.siegeArmy;
        b.camp = r.camp;
        b.side = Math.Max(0, Math.Min(3, r.siegeSide));
        b.arriveAt = b.lastPull = DateTime.UtcNow;
        b.spawned = false;
        b.frame = null;
        b.commands.Clear();
        b.shots.Clear();
        raid.live = true;
        raid.createdUtc = Now();   // блокировка деревни - от начала боя
        Save();
        StoredAccount def = FindById(raid.defenderId);
        Debug.Log($"[Сервер] Живой набег: {me.profile.displayName} у деревни {(def != null ? def.profile.displayName : "?")}");
        return new ApiResponse { ok = true, siegeLive = true };
    }

    // Защитник: снимок деревни; в ответ - приказы нападающего
    ApiResponse SiegePush(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        homeSeen[me.profile.id] = DateTime.UtcNow;
        StoredRaid raid = db.raids.Find(x => x.id == r.raidId && x.defenderId == me.profile.id && x.started && x.live);
        if (raid == null || !battles.TryGetValue(raid.id, out LiveBattle b))
            return new ApiResponse { ok = true, siegeOver = true };   // нападающий закончил (или набег брошен)
        b.spawned = true;
        b.lastPush = DateTime.UtcNow;
        if (r.siegeFrame != null)
        {
            if (r.siegeFrame.shots != null)
                foreach (var sh in r.siegeFrame.shots) if (sh != null && b.shots.Count < 120) b.shots.Add(sh);
            r.siegeFrame.shots = new List<SiegeShot>();
            b.frame = r.siegeFrame;
        }
        var cmds = new List<BattleCommand>(b.commands);
        b.commands.Clear();
        // Нападающий давно не спрашивал (игра закрыта) - войско уходит само
        bool gone = (DateTime.UtcNow - b.lastPull).TotalSeconds > 60;
        return new ApiResponse { ok = true, commands = cmds, siegeLive = true, siegeOver = gone };
    }

    // Нападающий: приказы; в ответ - что сейчас в деревне защитника
    ApiResponse SiegePull(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        StoredRaid raid = db.raids.Find(x => x.id == r.raidId && x.attackerId == me.profile.id && x.started);
        if (raid == null) return ApiResponse.Fail("Набег не найден");
        if (!raid.live || !battles.TryGetValue(raid.id, out LiveBattle b)) return new ApiResponse { ok = true, siegeLive = false };
        DateTime now = DateTime.UtcNow;
        b.lastPull = now;

        // Игра защитника так и не выставила армию (вышел из игры) - бой пойдёт без него, на копии деревни
        if (!b.spawned)
        {
            if ((now - b.arriveAt).TotalSeconds > Api.SiegeSpawnSeconds)
            {
                raid.live = false;
                b.army = null;
                Save();
                return new ApiResponse { ok = true, siegeLive = false };
            }
            return new ApiResponse { ok = true, siegeLive = true };
        }

        if (r.commands != null) foreach (var c in r.commands) if (c != null && b.commands.Count < 200) b.commands.Add(c);
        var shots = new List<SiegeShot>(b.shots);
        b.shots.Clear();
        SiegeFrame f = b.frame;
        if (f != null) f.shots = shots;
        bool lost = (now - b.lastPush).TotalSeconds > Api.SiegeLostSeconds;   // защитник пропал: бой окончен
        return new ApiResponse { ok = true, siegeLive = true, siegeFrame = f, siegeOver = lost };
    }

    ApiResponse FinishRaid(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        StoredRaid raid = db.raids.Find(x => x.id == r.raidId && x.attackerId == me.profile.id);
        if (raid == null) return ApiResponse.Fail("Набег не найден (прошло слишком много времени?)");
        db.raids.Remove(raid);
        bool live = raid.live;
        BattleReplay rec = !live && battles.TryGetValue(raid.id, out LiveBattle rb) ? rb.rec : null;
        if (live) battles.Remove(raid.id);   // игра защитника узнает об этом при следующем снимке (siegeOver)
        else if (battles.TryGetValue(raid.id, out LiveBattle lb))
        {
            // Защитник смотрел бой - он ещё раз заглянет и увидит конец; не смотрел - бой больше не нужен
            if (lb.snapshot != null && (DateTime.UtcNow - lb.defenderSeen).TotalSeconds < 15) lb.snapshot.phase = 3;
            else battles.Remove(raid.id);
        }

        StoredAccount def = FindById(raid.defenderId);
        CityData city = def != null ? ReadCity(def.profile.id) : null;
        if (def == null || city == null) { Save(); return new ApiResponse { ok = true }; }

        // Унести больше, чем было в деревне, нельзя; погибнуть - не больше бойцов, чем было
        RaidLoot loot = r.loot ?? new RaidLoot();
        if (!live) loot.ClampTo(CityLoss.Available(city));   // вживую добычу уже вынесли из его казны (город мог сохраниться без неё)
        var killed = new List<string>();
        if (live && r.killed != null) { foreach (var n in r.killed) if (!string.IsNullOrEmpty(n) && killed.Count < 300) killed.Add(n); }   // погибли у него в игре
        else if (r.killed != null && city.army != null)
        {
            var left = new List<SavedSoldier>(city.army);
            foreach (var n in r.killed)
            {
                int i = left.FindIndex(s => s != null && s.unit == n);
                if (i < 0) continue;
                left.RemoveAt(i);
                killed.Add(n);
            }
        }

        // Разрушенные здания (только не вживую: вживую руины уже стоят у него в игре)
        var ruinedAt = new List<string>();
        if (!live && r.ruinedAt != null)
            foreach (var cell in r.ruinedAt) if (!string.IsNullOrEmpty(cell) && cell.Length <= 16 && ruinedAt.Count < 300) ruinedAt.Add(cell);

        bool anything = loot.Total > 0 || killed.Count > 0 || r.ruined > 0 || r.hallSacked;
        int shieldMin = r.fought || anything ? ShieldFor(city, r, loot, killed.Count) : 0;
        if (IsNpc(def))
        {
            // NPC-деревня (обновление 14): потери она восстановит сама, щит короткий, рейтинг меняется только у нападающего
            float npcShare = DamageShare(city, r);
            int npcOutcome = r.hallSacked || npcShare >= Leagues.RaidWinShare ? 2 : npcShare >= Leagues.RaidSmallWinShare ? 1 : npcShare < Leagues.RaidFailShare ? -1 : 0;
            NpcFinishRaid(me, def, loot, killed, ruinedAt, shieldMin, npcOutcome, (int)Math.Round(npcShare * 100f), anything || r.fought);
            battles.Remove(raid.id);
            Save();
            return new ApiResponse { ok = true, profile = me.profile.Copy() };
        }
        // Рейтинг: снесли Цитадель или разрушили 3+ здания - победа; ничего не смогли - поражение; что-то между - без изменений
        // Обновление 13: рейтинг - по доле разрушенного (без стен, ворот, дорожек и мостов), а не «сломал 3 чего угодно».
        // Раньше +13 давали и за три куска стены. Теперь: снёс Цитадель или половину города - победа,
        // четверть - небольшая победа, меньше 10% - набег не удался (нападающему минус, защитнику плюс), между - без изменений
        float share = DamageShare(city, r);
        int outcome = r.hallSacked || share >= Leagues.RaidWinShare ? 2 : share >= Leagues.RaidSmallWinShare ? 1 : share < Leagues.RaidFailShare ? -1 : 0;
        int defDelta = r.fought || anything ? RaidRating(me, def, outcome, (int)Math.Round(share * 100f)) : 0;
        if (anything || r.fought)
        {
            string repId = Guid.NewGuid().ToString("N");
            bool hasReplay = rec != null && rec.frames.Count >= 2 && WriteReplay(def.profile.id, repId, rec);
            def.pending.Add(new RaidReport
            {
                id = repId,
                replay = hasReplay,
                attackerName = me.profile.displayName,
                timeUtc = Now(),
                loot = loot,
                killed = killed,
                hallSacked = r.hallSacked,
                ruined = Mathf.Max(0, r.ruined),
                ruinedAt = ruinedAt,
                rating = defDelta,
                live = live,
                shieldMinutes = shieldMin,
            });
            // Щит: не короче того, что уже стоит
            DateTime until = DateTime.UtcNow.AddMinutes(shieldMin);
            if (until > ParseUtc(def.shieldUtc)) def.shieldUtc = until.ToString("o");
            Debug.Log($"[Сервер] Набег {me.profile.displayName} → {def.profile.displayName}: {loot.ShortText}, погибло защитников {killed.Count}, щит {shieldMin} мин");
            if (!live) PushRaidResult(def, me, loot, r.hallSacked, Mathf.Max(0, r.ruined), shieldMin);
        }
        Save();
        return new ApiResponse { ok = true, profile = me.profile.Copy() };   // новый рейтинг нападающего
    }

    // ---------- Запись набега (final-9) ----------
    // Нападающий шлёт снимок боя каждые 0.4 с; раз в step секунд сервер кладёт кадр в запись.
    // Кадров больше MaxFrames - оставляем каждый второй и пишем вдвое реже. Набег прошёл без защитника - запись
    // ложится в replays/<защитник>_<отчёт>.json.gz, защитник смотрит её из отчёта (Api.BattleReplay).

    const int ReplayKeepDays = 7;   // обновление 12: неделю (было 3 дня)
    string ReplayDir => Path.Combine(dataDir, "replays");

    static void Record(LiveBattle b, BattleSnapshot s)
    {
        if (s.phase < 1 || s.phase > 2) return;   // осмотр не пишем, конец - уже не нужен
        DateTime now = DateTime.UtcNow;
        // Выстрелы копятся между кадрами (снимки идут чаще, чем кадры записи)
        if (s.shots != null && s.real)
            foreach (var sh in s.shots)
                if (sh != null && b.recShots.Count < 120)
                    b.recShots.Add(new ReplayShot { k = sh.kind, x0 = RoundI(sh.x0 * 10f), y0 = RoundI(sh.y0 * 10f), x1 = RoundI(sh.x1 * 10f), y1 = RoundI(sh.y1 * 10f) });
        if (b.rec == null)
        {
            b.rec = new BattleReplay { attackerName = s.attackerName, size = s.size, real = s.real };
            b.recStart = now;
        }
        else if ((now - b.recLast).TotalSeconds < b.rec.step - 0.05) return;
        bool real = b.rec.real && s.real;
        BattleReplay rec = b.rec;
        b.recLast = now;
        var f = new ReplayFrame { t = (int)Math.Round((now - b.recStart).TotalSeconds) };
        var hp = new Dictionary<int, int>();
        if (s.buildings != null)
            foreach (var sb in s.buildings)
            {
                if (sb == null) continue;
                string key = sb.x + "," + sb.y;
                if (!b.recBuild.TryGetValue(key, out int idx))
                {
                    if (rec.buildings.Count >= 400) continue;
                    idx = rec.buildings.Count;
                    b.recBuild[key] = idx;
                    rec.buildings.Add(real
                        ? new SnapBuilding { kind = sb.kind, title = sb.title, x = sb.rx, y = sb.ry, w = sb.rw, h = sb.rh, type = sb.type, lv = sb.lv }
                        : new SnapBuilding { kind = sb.kind, title = sb.title, x = sb.x, y = sb.y, w = sb.w, h = sb.h });
                }
                hp[idx] = sb.ruined ? -1 : Mathf.Clamp(RoundI(sb.hp * 100f), 0, 100);
            }
        for (int i = 0; i < rec.buildings.Count; i++) f.b.Add(hp.TryGetValue(i, out int v) ? v : -1);
        if (s.units != null)
            foreach (var u in s.units)
            {
                if (u == null || u.hp <= 0f || f.u.Count >= 300) continue;
                if (b.recNamed.Add(u.id) && rec.names.Count < 1000)
                    rec.names.Add(new ReplayName { i = u.id, n = u.name, a = u.attacker, r = u.ranged, k = u.art, hr = u.horse, c = u.tint });
                float ux = real ? u.rx : u.x, uy = real ? u.ry : u.y;
                f.u.Add(new ReplayUnit { i = u.id, x = RoundI(ux * 10f), y = RoundI(uy * 10f), h = Mathf.Clamp(RoundI(u.hp * 100f), 1, 100) });
            }
        if (real && b.recShots.Count > 0) { f.s.AddRange(b.recShots); b.recShots.Clear(); }
        rec.frames.Add(f);
        if (rec.frames.Count > BattleReplay.MaxFrames)
        {
            var keep = new List<ReplayFrame>();
            for (int i = 0; i < rec.frames.Count; i += 2)
            {
                if (i + 1 < rec.frames.Count && rec.frames[i + 1].s != null && rec.frames[i + 1].s.Count > 0)
                    rec.frames[i].s.AddRange(rec.frames[i + 1].s);   // выстрелы выброшенного кадра не теряем
                keep.Add(rec.frames[i]);
            }
            rec.frames.Clear();
            rec.frames.AddRange(keep);
            rec.step *= 2;
        }
    }

    bool WriteReplay(string defenderId, string reportId, BattleReplay rec)
    {
        try
        {
            Directory.CreateDirectory(ReplayDir);
            foreach (var old in Directory.GetFiles(ReplayDir, "*.json.gz"))
                if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(old)).TotalDays > ReplayKeepDays) File.Delete(old);
            string path = Path.Combine(ReplayDir, defenderId + "_" + reportId + ".json.gz");
            byte[] raw = Encoding.UTF8.GetBytes(JsonUtility.ToJson(rec));
            using (var fs = File.Create(path))
            using (var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionLevel.Optimal))
                gz.Write(raw, 0, raw.Length);
            return true;
        }
        catch (Exception e) { Debug.LogWarning("[Сервер] Запись набега не сохранена: " + e.Message); return false; }
    }

    static int RoundI(float v) { return (int)Math.Round(v); }

    static bool IsHexId(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length != 32) return false;
        foreach (char ch in s) if (!((ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f'))) return false;
        return true;
    }

    ApiResponse BattleReplayGet(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        if (!IsHexId(r.raidId) || !IsHexId(me.profile.id)) return ApiResponse.Fail("Записи этого боя нет");
        string path = Path.Combine(ReplayDir, me.profile.id + "_" + r.raidId + ".json.gz");   // только свои: имя файла начинается с id защитника
        if (!File.Exists(path)) return ApiResponse.Fail("Запись боя уже удалена (записи хранятся неделю)");
        try
        {
            using (var fs = File.OpenRead(path))
            using (var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress))
            using (var rd = new StreamReader(gz, Encoding.UTF8))
                return new ApiResponse { ok = true, replay = JsonUtility.FromJson<BattleReplay>(rd.ReadToEnd()) };
        }
        catch (Exception e) { return ApiResponse.Fail("Запись боя не читается: " + e.Message); }
    }

    // ---------- Щит города ----------

    // Сколько минут щита дать защитнику после набега: чем сильнее разорили, тем дольше (ShieldRules)
    static int ShieldFor(CityData city, ApiRequest r, RaidLoot loot, int killed)
    {
        // Доля разрушенного: игра нападающего считает её сама (без стен и ворот); старая игра - по числу руин.
        // Доли здесь не обрезаются: ShieldRules.Severity сама приводит их к 0..1
        float damage;
        if (r.damage >= 0) damage = r.damage / 100f;
        else
        {
            int total = 0;
            if (city.buildings != null)
                foreach (var b in city.buildings) if (b != null && CountsForShield(b.type)) total++;
            damage = total > 0 ? (float)Mathf.Max(0, r.ruined) / total : 0f;
        }
        int had = CityLoss.Available(city).Total;
        float lootShare = had > 0 ? (float)loot.Total / had : 0f;
        int defenders = city.army != null ? city.army.Count : 0;
        float killShare = defenders > 0 ? (float)killed / defenders : 0f;
        return ShieldRules.MinutesFor(ShieldRules.Severity(damage, lootShare, killShare, r.hallSacked));
    }

    // Доля разрушенного 0..1 (как для щита)
    static float DamageShare(CityData city, ApiRequest r)
    {
        if (r.damage >= 0) return Math.Max(0f, Math.Min(1f, r.damage / 100f));
        int total = 0;
        if (city != null && city.buildings != null)
            foreach (var b in city.buildings) if (b != null && CountsForShield(b.type)) total++;
        return total > 0 ? Math.Min(1f, (float)Math.Max(0, r.ruined) / total) : 0f;
    }

    // Стены, ворота, дорожки и мосты в долю разрушенного не входят
    static bool CountsForShield(string type)
    {
        switch (type)
        {
            case "Стена": case "Ворота": case "Дорожка": case "Брусчатка": case "Мост": return false;
            default: return !string.IsNullOrEmpty(type);
        }
    }

    // В каждый ответ игроку - его щит (так игра узнаёт о нём при загрузке, сохранении и после набега)
    void AddShield(ApiResponse res, string token)
    {
        if (string.IsNullOrEmpty(token)) return;
        StoredAccount a = FindByToken(token);
        if (a == null) return;
        res.shieldKnown = true;
        res.shieldUtc = Shielded(a) ? a.shieldUtc : "";
    }

    // Игрок сам снял щит: его город снова можно найти во вкладке «Бой»
    ApiResponse ShieldDrop(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        if (!Shielded(me)) return new ApiResponse { ok = true };
        me.shieldUtc = null;
        Save();
        Debug.Log($"[Сервер] {me.profile.displayName} снял щит");
        return new ApiResponse { ok = true };
    }
}
