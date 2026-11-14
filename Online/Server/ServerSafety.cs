using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

/// <summary>
/// Настройки сервера: файл server-config.json в папке данных сервера (создаётся сам при первом запуске).
/// Почта и имя разработчика попадают на страницы /privacy и /terms; ключи Google - для входа через Google Play Games
/// и push-уведомлений (их использует отдельная программа сервера, см. Assets/ServerApp~/GoogleServices.cs).
/// </summary>
[Serializable]
public class ServerConfig
{
    public string contactEmail = "";                 // почта для игроков: политика конфиденциальности, удаление аккаунта, жалобы
    public string developerName = "Cinetica Games";  // кто выпускает игру (как в Google Play Console)
    public string gameName = "Strategyca";
    public string adminKey = "";                     // ключ страницы модерации /admin (пусто - сервер придумает сам)
    public string googleClientId = "";               // Play Games Services → учётные данные «Игровой сервер» (OAuth-клиент «Веб-приложение»)
    public string googleClientSecret = "";
    public string firebaseKeyFile = "firebase-service-account.json";   // ключ сервисного аккаунта Firebase (push), лежит рядом с этим файлом
}

/// <summary>Кто вошёл через Google Play Games (ответ Google на одноразовый код). error - не получилось.</summary>
public class GoogleIdentity
{
    public string playerId;
    public string name;
    public string error;
}

/// <summary>Одно push-уведомление на одно устройство.</summary>
public class PushMessage
{
    public string token;
    public string title;
    public string body;
    public string tag;     // уведомления с одним tag заменяют друг друга на телефоне
    public int kind;       // PushKinds
}

/// <summary>
/// Всё, что Google Play требует от онлайн-игры с чатом, на стороне сервера:
///  - удаление аккаунта из игры (Api.DeleteAccount) и с сайта (страница /delete-account, ServerWeb.cs);
///  - жалобы на игроков (Api.ChatReport) и их разбор на странице /admin: заглушить чат, сбросить имя, стереть сообщения;
///  - блокировка игроков (Api.ChatBlock): их сообщения в чате клана не приходят;
///  - вход через Google Play Games (Api.GoogleLogin / GoogleLink): аккаунт привязан к Google и находится на новом телефоне;
///  - push-уведомления: «на вас напали» (начало и итог набега), «стройка готова» (Api.PushPlan), «щит скоро кончится».
/// Запросы к Google делает отдельная программа сервера (GoogleResolver, PushSender); сервер в Unity работает без них.
/// </summary>
public partial class ServerCore
{
    partial class StoredAccount
    {
        public string googleId;                                          // игрок Google Play Games (playerId)
        public List<PushDevice> pushDevices = new List<PushDevice>();    // телефоны для push
        public List<PushPlanItem> pushPlan = new List<PushPlanItem>();   // стройки, о которых сообщить
        public string shieldSeenFor;      // щит (shieldUtc), длину которого уже запомнили
        public int shieldMinutes;         // ... его длина в минутах
        public string shieldWarnedFor;    // щит, о конце которого уже предупредили
        public List<string> reportTimes = new List<string>();   // когда этот игрок жаловался (не больше Api.ReportsPerDay в сутки)
    }

    [Serializable]
    class PushDevice
    {
        public string token;
        public string lang;
        public string updatedUtc;
    }

    [Serializable]
    class ChatReportRecord
    {
        public string id;
        public string timeUtc;
        public string reporterId, reporterName;
        public string targetId, targetName;
        public string reason;
        public string message;           // на какое сообщение пожаловались
        public List<string> context = new List<string>();   // последние сообщения этого игрока в чате клана
        public string clanName;
        public bool done;                // разобрана
        public string action;            // что сделали
    }

    partial class Db
    {
        public List<ChatReportRecord> reports = new List<ChatReportRecord>();
        public int deletedAccounts;      // сколько аккаунтов игроки удалили сами
    }

    /// <summary>Обменять одноразовый код Google Play Games на игрока. null - вход через Google не настроен.</summary>
    public static Func<string, GoogleIdentity> GoogleResolver;
    /// <summary>Отправить push (не ждёт ответа). null - push не настроены.</summary>
    public static Action<PushMessage> PushSender;

    const string ConfigFile = "server-config.json";
    const int MaxPushDevices = 5, MaxPlan = 40, MaxBlocked = 200, MaxReports = 3000;
    const double ReportKeepDays = 90;

    public ServerConfig Config { get; private set; }

    void FixSafetyData()
    {
        if (db.reports == null) db.reports = new List<ChatReportRecord>();
        foreach (var a in db.accounts)
        {
            if (a == null) continue;
            if (a.pushDevices == null) a.pushDevices = new List<PushDevice>();
            if (a.pushPlan == null) a.pushPlan = new List<PushPlanItem>();
            if (a.reportTimes == null) a.reportTimes = new List<string>();
            if (a.profile != null && a.profile.blocked == null) a.profile.blocked = new List<BlockedPlayer>();
        }
        LoadConfig();
    }

    void LoadConfig()
    {
        string p = Path.Combine(dataDir, ConfigFile);
        ServerConfig c = null;
        try { if (File.Exists(p)) c = JsonUtility.FromJson<ServerConfig>(File.ReadAllText(p)); }
        catch (Exception e) { Debug.LogWarning("[Сервер] Не удалось прочитать " + p + ": " + e.Message); }
        bool write = c == null;
        if (c == null) c = new ServerConfig();
        if (string.IsNullOrWhiteSpace(c.adminKey)) { c.adminKey = RandomKey(); write = true; }
        if (c.contactEmail == null) c.contactEmail = "";
        if (string.IsNullOrWhiteSpace(c.developerName)) c.developerName = "Cinetica Games";
        if (string.IsNullOrWhiteSpace(c.gameName)) c.gameName = "Strategyca";
        if (string.IsNullOrWhiteSpace(c.firebaseKeyFile)) c.firebaseKeyFile = "firebase-service-account.json";
        Config = c;
        if (write)
            try { WriteSafe(p, JsonUtility.ToJson(c, true)); }
            catch (Exception e) { Debug.LogWarning("[Сервер] Не удалось записать " + p + ": " + e.Message); }
        if (string.IsNullOrWhiteSpace(c.contactEmail))
            Debug.LogWarning($"[Сервер] В {p} не указана почта (contactEmail) - она нужна для политики конфиденциальности в Google Play");
    }

    static string RandomKey()
    {
        byte[] b = new byte[18];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
        return Convert.ToBase64String(b).Replace('+', 'x').Replace('/', 'y').Replace("=", "");
    }

    // ---------- Удаление аккаунта ----------

    ApiResponse DeleteAccountReq(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        if (!AccountRules.IsDeleteWord(r.text)) return ApiResponse.Fail($"Чтобы удалить аккаунт, введите слово {AccountRules.DeleteWord}");
        DeleteAccountData(me, "из игры");
        Save();
        return new ApiResponse { ok = true };
    }

    /// <summary>Стереть аккаунт и всё, что с ним связано: город, место в клане и его сообщения, лоты, набеги, сессии, push, привязку Google.</summary>
    void DeleteAccountData(StoredAccount a, string how)
    {
        string id = a.profile.id, name = a.profile.displayName;
        StoredClan c = ClanOf(a);
        if (c != null) RemoveMember(c, a, $"{name} покинул(а) клан");
        foreach (var clan in db.clans) clan.chat.RemoveAll(m => m != null && m.authorId == id);   // его сообщения больше никто не увидит
        db.lots.RemoveAll(x => x == null || x.sellerId == id);
        db.raids.RemoveAll(x => x == null || x.attackerId == id || x.defenderId == id);
        db.sessions.RemoveAll(s => s == null || s.accountId == id);
        foreach (var p in db.purchases) if (p != null && p.accountId == id) p.accountId = "";   // сама покупка остаётся: чек нельзя засчитать второй раз
        foreach (var other in db.accounts) if (other != null && other.profile != null && other.profile.blocked != null) other.profile.blocked.RemoveAll(b => b == null || b.id == id);
        if (db.adminId == id) db.adminId = null;
        try { if (File.Exists(CityPath(id))) File.Delete(CityPath(id)); }
        catch (Exception e) { Debug.LogWarning("[Сервер] Не удалось удалить город: " + e.Message); }
        db.accounts.Remove(a);
        db.deletedAccounts++;
        lastSeen.Remove(id);
        homeSeen.Remove(id);
        Debug.Log($"[Сервер] Аккаунт удалён {how}: {a.profile.login} ({name})");
    }

    // ---------- Жалобы и блокировка ----------

    /// <summary>Сообщения чата клана для игрока: без тех, кого он заблокировал.</summary>
    List<ClanMessage> ChatFor(StoredAccount me, StoredClan c, int since)
    {
        return c.chat.FindAll(m => m != null && m.n > since && (m.system || me.profile == null || !me.profile.Blocks(m.authorId)));
    }

    static string MutedText(StoredAccount a)
    {
        double s = (a.profile.MutedUntil - DateTime.UtcNow).TotalSeconds;
        return s > 365 * 86400.0 ? "решения модератора" : "истечения " + ShieldRules.Duration(s);
    }

    ApiResponse ChatReportReq(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        StoredAccount who = FindById(r.memberId);
        if (who == null) return ApiResponse.Fail("Игрок не найден");
        if (who == me) return ApiResponse.Fail("На себя пожаловаться нельзя");
        DateTime now = DateTime.UtcNow;
        me.reportTimes.RemoveAll(t => (now - ParseUtc(t)).TotalHours > 24);
        if (me.reportTimes.Count >= Api.ReportsPerDay) return ApiResponse.Fail("Слишком много жалоб за сутки — попробуйте завтра");
        me.reportTimes.Add(Now());

        StoredClan c = ClanOf(me);
        var rep = new ChatReportRecord
        {
            id = Guid.NewGuid().ToString("N"), timeUtc = Now(),
            reporterId = me.profile.id, reporterName = me.profile.displayName,
            targetId = who.profile.id, targetName = who.profile.displayName,
            reason = string.IsNullOrWhiteSpace(r.text) ? "Другое" : CleanText(r.text, 60),
            clanName = c != null ? c.name : "",
        };
        if (c != null)
        {
            ClanMessage m = r.since > 0 ? c.chat.Find(x => x != null && x.n == r.since && x.authorId == who.profile.id) : null;
            if (m != null) rep.message = m.text;
            var theirs = c.chat.FindAll(x => x != null && x.authorId == who.profile.id);
            for (int i = Mathf.Max(0, theirs.Count - 5); i < theirs.Count; i++) rep.context.Add(theirs[i].text);
        }
        db.reports.Add(rep);
        db.reports.RemoveAll(x => x == null || (x.done && (now - ParseUtc(x.timeUtc)).TotalDays > ReportKeepDays));
        if (db.reports.Count > MaxReports) db.reports.RemoveRange(0, db.reports.Count - MaxReports);
        Save();
        Debug.LogWarning($"[Жалоба] {me.profile.displayName} → {who.profile.displayName}: {rep.reason}" + (string.IsNullOrEmpty(rep.message) ? "" : $" «{rep.message}»") + $"  (разобрать: {Api.WebAdmin}?key=...)");
        return new ApiResponse { ok = true };
    }

    ApiResponse ChatBlockReq(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        if (me.profile.blocked == null) me.profile.blocked = new List<BlockedPlayer>();
        if (r.role == 1)
        {
            StoredAccount who = FindById(r.memberId);
            if (who == null) return ApiResponse.Fail("Игрок не найден");
            if (who == me) return ApiResponse.Fail("Себя заблокировать нельзя");
            if (!me.profile.Blocks(who.profile.id))
            {
                if (me.profile.blocked.Count >= MaxBlocked) return ApiResponse.Fail($"Заблокировать можно не больше {MaxBlocked} игроков");
                me.profile.blocked.Add(new BlockedPlayer { id = who.profile.id, name = who.profile.displayName });
            }
        }
        else me.profile.blocked.RemoveAll(b => b == null || b.id == r.memberId);
        Save();
        return Ok(me, r.token);
    }

    // ---------- Вход через Google Play Games ----------

    [ThreadStatic] static GoogleIdentity pendingGoogle;   // ответ Google на код из этого запроса (см. Handle)
    readonly Dictionary<string, KeyValuePair<GoogleIdentity, DateTime>> googleTickets = new Dictionary<string, KeyValuePair<GoogleIdentity, DateTime>>();

    void ResolveGoogleCode(string path, ApiRequest req)
    {
        pendingGoogle = null;
        if (req == null || string.IsNullOrEmpty(req.authCode) || (path != Api.GoogleLogin && path != Api.GoogleLink)) return;
        Func<string, GoogleIdentity> resolve = GoogleResolver;
        if (resolve == null) { pendingGoogle = new GoogleIdentity { error = "Вход через Google Play Games на этом сервере не настроен" }; return; }
        try { pendingGoogle = resolve(req.authCode) ?? new GoogleIdentity { error = "Google не ответил" }; }
        catch (Exception e) { pendingGoogle = new GoogleIdentity { error = "Google не ответил: " + e.Message }; }
    }

    GoogleIdentity TakeGoogle(ApiRequest r, out string error)
    {
        error = null;
        GoogleIdentity g = pendingGoogle;
        pendingGoogle = null;
        if (g == null && !string.IsNullOrEmpty(r.googleTicket) && googleTickets.TryGetValue(r.googleTicket, out var t) && t.Value > DateTime.UtcNow) g = t.Key;
        if (g == null) { error = "Войдите в Google Play Games ещё раз"; return null; }
        if (!string.IsNullOrEmpty(g.error) || string.IsNullOrEmpty(g.playerId)) { error = g.error ?? "Google не узнал игрока"; return null; }
        return g;
    }

    string NewTicket(GoogleIdentity g)
    {
        var drop = new List<string>();
        foreach (var kv in googleTickets) if (kv.Value.Value < DateTime.UtcNow) drop.Add(kv.Key);
        foreach (var k in drop) googleTickets.Remove(k);
        string t = Guid.NewGuid().ToString("N");
        googleTickets[t] = new KeyValuePair<GoogleIdentity, DateTime>(g, DateTime.UtcNow.AddMinutes(15));
        return t;
    }

    StoredAccount FindByGoogle(string playerId)
    {
        return string.IsNullOrEmpty(playerId) ? null : db.accounts.Find(a => a != null && a.profile != null && a.googleId == playerId);
    }

    ApiResponse GoogleLoginReq(ApiRequest r)
    {
        GoogleIdentity g = TakeGoogle(r, out string err);
        if (g == null) return ApiResponse.Fail(err);
        StoredAccount a = FindByGoogle(g.playerId);
        if (a != null) return Ok(a, OpenSession(a));

        string gname = CleanText(g.name, AccountRules.MaxName);
        if (!r.create)   // аккаунта нет: игра спросит - новый город или войти в старый и привязать
            return new ApiResponse { ok = true, googleNew = true, googleTicket = NewTicket(g), googleName = gname };

        string name = AccountRules.CheckName(r.displayName) == null ? CleanText(r.displayName, AccountRules.MaxName) : gname;
        if (AccountRules.CheckName(name) != null) name = "Игрок";
        string login;
        do login = "g_" + Guid.NewGuid().ToString("N").Substring(0, 10); while (FindByLogin(login) != null);
        a = new StoredAccount
        {
            profile = new AccountProfile
            {
                id = Guid.NewGuid().ToString("N"), login = login, displayName = name, createdUtc = Now(),
                lotSlots = LotRules.StartSlots, googleLinked = true, noPassword = true,
            },
            salt = "", hash = "",   // без пароля: вход только через Google Play Games (пароль можно задать в настройках)
            googleId = g.playerId,
        };
        db.accounts.Add(a);
        Debug.Log($"[Сервер] Новый игрок через Google Play Games: {name}");
        return Ok(a, OpenSession(a));
    }

    ApiResponse GoogleLinkReq(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        GoogleIdentity g = TakeGoogle(r, out string err);
        if (g == null) return ApiResponse.Fail(err);
        StoredAccount other = FindByGoogle(g.playerId);
        if (other != null && other != me) return ApiResponse.Fail($"Этот аккаунт Google Play уже привязан к игроку «{other.profile.displayName}»");
        me.googleId = g.playerId;
        me.profile.googleLinked = true;
        Save();
        return Ok(me, r.token);
    }

    // ---------- Push-уведомления ----------

    ApiResponse PushTokenReq(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        if (r.pushOff >= 0) me.profile.pushOff = r.pushOff & (PushKinds.Attack | PushKinds.Build | PushKinds.Shield);
        string t = (r.pushToken ?? "").Trim();
        if (t.Length > 0 && t.Length < 4096)
        {
            if (r.text == "remove") me.pushDevices.RemoveAll(d => d == null || d.token == t);
            else
            {
                // Телефон теперь у этого аккаунта: другим аккаунтам на него больше не пишем
                foreach (var a in db.accounts) if (a != null && a != me && a.pushDevices != null) a.pushDevices.RemoveAll(d => d == null || d.token == t);
                me.pushDevices.RemoveAll(d => d == null || d.token == t);
                me.pushDevices.Add(new PushDevice { token = t, lang = CleanText(r.lang, 5), updatedUtc = Now() });
                if (me.pushDevices.Count > MaxPushDevices) me.pushDevices.RemoveRange(0, me.pushDevices.Count - MaxPushDevices);
            }
        }
        Save();
        return Ok(me, r.token);
    }

    ApiResponse PushPlanReq(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        var plan = new List<PushPlanItem>();
        DateTime now = DateTime.UtcNow;
        if (r.pushPlan != null)
            foreach (var p in r.pushPlan)
            {
                if (p == null || plan.Count >= MaxPlan) continue;
                DateTime at = ParseUtc(p.atUtc);
                if (at <= now || at > now.AddDays(30)) continue;
                plan.Add(new PushPlanItem { atUtc = at.ToString("o"), text = CleanText(p.text, 60), upgrade = p.upgrade });
            }
        me.pushPlan = plan;
        Save();
        return new ApiResponse { ok = true };
    }

    /// <summary>Сервер может отправлять push (настроен Firebase).</summary>
    public static bool PushReady => PushSender != null;

    static string L(string lang, string ru, string en, string lt)
    {
        return lang == "en" ? en : lang == "lt" ? lt : ru;
    }

    void Notify(StoredAccount a, int kind, string tag, Func<string, string> title, Func<string, string> body)
    {
        Action<PushMessage> send = PushSender;
        if (send == null || a == null || a.profile == null || a.pushDevices == null || a.pushDevices.Count == 0) return;
        if ((a.profile.pushOff & kind) != 0) return;
        foreach (var d in a.pushDevices)
        {
            if (d == null || string.IsNullOrEmpty(d.token)) continue;
            try { send(new PushMessage { token = d.token, title = title(d.lang), body = body(d.lang), tag = tag, kind = kind }); }
            catch (Exception e) { Debug.LogWarning("[Push] " + e.Message); }
        }
    }

    void PushRaidStarted(StoredAccount def, StoredAccount att)
    {
        string n = att.profile.displayName;
        Notify(def, PushKinds.Attack, "raid",
            l => L(l, "⚔ На вас напали!", "⚔ You are under attack!", "⚔ Jus puola!"),
            l => L(l, $"{n} ведёт войско к вашей деревне. Зайдите в игру, чтобы командовать обороной.",
                      $"{n} is marching on your village. Open the game to command the defence.",
                      $"{n} žygiuoja į jūsų kaimą. Atidarykite žaidimą ir vadovaukite gynybai."));
    }

    void PushRaidResult(StoredAccount def, StoredAccount att, RaidLoot loot, bool hallSacked, int ruined, int shieldMin)
    {
        string n = att.profile.displayName;
        int total = loot != null ? loot.Total : 0;
        string shield = shieldMin > 0 ? ShieldRules.Duration(shieldMin * 60.0) : "";
        string shieldEn = shieldMin >= 60 ? $"{shieldMin / 60} h" : $"{shieldMin} min";
        Notify(def, PushKinds.Attack, "raid",
            l => L(l, "Набег окончен", "The raid is over", "Antpuolis baigėsi"),
            l => L(l,
                (total > 0 ? $"{n} унёс: {loot.ShortText}" : $"Вы отбились от {n}: ничего не унесли")
                    + (ruined > 0 ? $", разрушено зданий: {ruined}" : "") + (hallSacked ? ", Цитадель разорена" : "")
                    + (shield != "" ? $". Щит на {shield}" : ""),
                (total > 0 ? $"{n} took {total} resources" : $"You held off {n}: nothing was taken")
                    + (ruined > 0 ? $", buildings destroyed: {ruined}" : "") + (hallSacked ? ", Citadel sacked" : "")
                    + (shieldMin > 0 ? $". Shield for {shieldEn}" : ""),
                (total > 0 ? $"{n} išsinešė {total} išteklių" : $"Atrėmėte {n}: nieko neišsinešė")
                    + (ruined > 0 ? $", sugriauta pastatų: {ruined}" : "") + (hallSacked ? ", Citadelė nusiaubta" : "")
                    + (shieldMin > 0 ? $". Skydas {shieldEn.Replace("min", "min.").Replace(" h", " val.")}" : "")));
    }

    DateTime lastPushTick = DateTime.MinValue;

    /// <summary>Раз в 15 секунд: щит скоро кончится, стройки готовы. Вызывает StrategycaServer.</summary>
    public void TickPush()
    {
        lock (sync)
        {
            DateTime now = DateTime.UtcNow;
            bool dirty = false;
            foreach (var a in db.accounts)
            {
                if (a == null || a.profile == null) continue;

                // Щит: предупреждаем за 30 минут (короткий щит - за 10)
                DateTime until = string.IsNullOrEmpty(a.shieldUtc) ? DateTime.MinValue : ParseUtc(a.shieldUtc);
                if (until > now)
                {
                    if (a.shieldSeenFor != a.shieldUtc) { a.shieldSeenFor = a.shieldUtc; a.shieldMinutes = (int)(until - now).TotalMinutes; dirty = true; }
                    int warnAt = a.shieldMinutes > 60 ? 30 : 10;
                    double left = (until - now).TotalMinutes;
                    if (a.shieldWarnedFor != a.shieldUtc && left <= warnAt && a.shieldMinutes > warnAt)
                    {
                        a.shieldWarnedFor = a.shieldUtc;
                        dirty = true;
                        int m = Math.Max(1, (int)Math.Ceiling(left));
                        Notify(a, PushKinds.Shield, "shield",
                            l => L(l, "🛡 Щит скоро кончится", "🛡 Your shield is ending", "🛡 Skydas netrukus baigsis"),
                            l => L(l, $"Через {m} мин ваш город снова смогут атаковать. Проверьте оборону!",
                                      $"In {m} min other players can attack your town again. Check your defences!",
                                      $"Po {m} min. jūsų miestą vėl galės pulti. Patikrinkite gynybą!"));
                    }
                }

                // Стройки и улучшения: что уже готово - одним уведомлением
                if (a.pushPlan != null && a.pushPlan.Count > 0)
                {
                    var done = a.pushPlan.FindAll(p => p == null || ParseUtc(p.atUtc) <= now);
                    if (done.Count == 0) continue;
                    a.pushPlan.RemoveAll(p => p == null || ParseUtc(p.atUtc) <= now);
                    dirty = true;
                    done.RemoveAll(p => p == null || (now - ParseUtc(p.atUtc)).TotalHours > 2);   // сервер долго стоял - старое уже не новость
                    if (done.Count == 0) continue;
                    bool up = done.TrueForAll(p => p.upgrade), build = done.TrueForAll(p => !p.upgrade);
                    string list = done[0].text + (done.Count > 1 ? $" +{done.Count - 1}" : "");
                    Notify(a, PushKinds.Build, "build",
                        l => build ? L(l, "⚒ Стройка готова", "⚒ Construction complete", "⚒ Statyba baigta")
                           : up ? L(l, "▲ Улучшение готово", "▲ Upgrade complete", "▲ Patobulinimas baigtas")
                           : L(l, "⚒ Стройки готовы", "⚒ Work complete", "⚒ Darbai baigti"),
                        l => L(l, $"{list}. Заходите строить дальше!", $"{list}. Come back and keep building!", $"{list}. Grįžkite ir statykite toliau!"));
                }
            }
            if (dirty) Save();
        }
    }

    /// <summary>Google ответил, что такого устройства больше нет (приложение удалено) - забываем его.</summary>
    public void DropPushToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return;
        lock (sync)
        {
            bool any = false;
            foreach (var a in db.accounts)
                if (a != null && a.pushDevices != null && a.pushDevices.RemoveAll(d => d == null || d.token == token) > 0) any = true;
            if (any) Save();
        }
    }
}
