using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using UnityEngine;

/// <summary>Ответ страницы сервера для браузера.</summary>
public class WebReply
{
    public int code = 200;
    public string type = "text/html";
    public string body = "";
    public string location;   // перенаправить сюда (код 303)

    public static WebReply Html(string body) { return new WebReply { body = body }; }
    public static WebReply Go(string location) { return new WebReply { code = 303, location = location, body = "" }; }
}

/// <summary>
/// Страницы сервера, которые открываются в браузере (адреса нужны в Google Play Console):
///   /privacy         - политика конфиденциальности;
///   /terms           - правила игры (что нельзя писать в чате и как модерируем);
///   /delete-account  - удалить аккаунт без игры (логин и пароль);
///   /admin?key=...   - модерация: жалобы игроков, заглушить чат, сбросить имя, стереть сообщения (ключ - в server-config.json).
/// </summary>
public partial class ServerCore
{
    /// <summary>Страница по адресу. null - такой страницы нет (покажется страница «Сервер работает»).</summary>
    public WebReply HandleWeb(string method, string path, Dictionary<string, string> args)
    {
        path = (path ?? "/").TrimEnd('/');
        if (path == "") path = "/";
        lock (sync)
        {
            switch (path)
            {
                case Api.WebPrivacy: return WebReply.Html(PrivacyPage());
                case Api.WebTerms: return WebReply.Html(TermsPage());
                case Api.WebDelete: return method == "POST" ? DeleteByForm(args) : WebReply.Html(DeletePage(null, false));
                case Api.WebAdmin: return AdminPage(args);
                case Api.WebAdmin + "/act": return AdminAct(args);
                case Api.WebAdmin + "/players": return AdminPlayers(args);
                case Api.WebAdmin + "/player": return AdminPlayer(args);
                case Api.WebAdmin + "/update": return AdminUpdate(args);
                case Api.WebAdmin + "/grant": return method == "POST" ? AdminGrant(args) : WebReply.Go($"{Api.WebAdmin}/players?key={AdminKeyArg(args)}");
                case Api.WebAdmin + "/grantall": return method == "POST" ? AdminGrantAll(args) : WebReply.Go($"{Api.WebAdmin}/players?key={AdminKeyArg(args)}");
                default: return null;
            }
        }
    }

    static string Arg(Dictionary<string, string> a, string k) { return a != null && a.TryGetValue(k, out string v) ? v ?? "" : ""; }
    static string H(string s) { return WebUtility.HtmlEncode(s ?? ""); }

    string Contact
    {
        get
        {
            string m = Config != null ? (Config.contactEmail ?? "").Trim() : "";
            return m == "" ? "<i>(почта не указана — администратор сервера, впишите contactEmail в server-config.json)</i>" : $"<a href=\"mailto:{H(m)}\">{H(m)}</a>";
        }
    }

    string Dev => H(Config != null ? Config.developerName : "Cinetica Games");
    string Game => H(Config != null ? Config.gameName : "Strategyca");

    static string Page(string title, string body)
    {
        return "<!doctype html><html lang=\"ru\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
            + $"<title>{H(title)}</title><style>"
            + "body{margin:0;background:#16181d;color:#e9e6df;font:16px/1.55 system-ui,-apple-system,Segoe UI,Roboto,sans-serif}"
            + "main{max-width:760px;margin:0 auto;padding:28px 18px 60px}h1{color:#f4cc6b;font-size:26px;margin:0 0 4px}"
            + "h2{color:#f4cc6b;font-size:19px;margin:28px 0 8px;border-bottom:1px solid #3a372f;padding-bottom:4px}"
            + "a{color:#8ccaff}.muted{color:#9da0a6;font-size:14px}ul{padding-left:22px}li{margin:4px 0}"
            + "input{font:inherit;padding:10px;border-radius:6px;border:1px solid #4a463c;background:#0f1013;color:#e9e6df;width:100%;box-sizing:border-box;margin:4px 0 12px}"
            + "button,.btn{font:inherit;padding:9px 16px;border-radius:6px;border:0;background:#f4cc6b;color:#1b1608;font-weight:600;cursor:pointer;text-decoration:none;display:inline-block;margin:2px}"
            + ".danger{background:#e8645a;color:#fff}.ghost{background:#2c2f36;color:#e9e6df}.box{background:#1f2228;border:1px solid #33363d;border-radius:10px;padding:14px 16px;margin:12px 0}"
            + ".ok{color:#86e08a}.bad{color:#ff7a6e}table{border-collapse:collapse;width:100%}td,th{border-bottom:1px solid #33363d;padding:6px;text-align:left;vertical-align:top;font-size:14px}"
            + "</style></head><body><main>" + body + "</main></body></html>";
    }

    string Footer()
    {
        return $"<p class=\"muted\" style=\"margin-top:40px\"><a href=\"{Api.WebPrivacy}\">Политика конфиденциальности</a> · <a href=\"{Api.WebTerms}\">Правила игры</a> · <a href=\"{Api.WebDelete}\">Удалить аккаунт</a></p>";
    }

    // ---------- Политика конфиденциальности ----------

    string PrivacyPage()
    {
        string g = Game, d = Dev, c = Contact;
        var sb = new StringBuilder();
        sb.Append($"<h1>Политика конфиденциальности {g}</h1><p class=\"muted\">Действует с 27 октября 2026 г. Разработчик: {d}. Связь: {c}</p>");
        sb.Append($"<p>Эта политика объясняет, какие данные собирает онлайн-игра {g} (приложение для Android и сервер игры), зачем и как ими можно управлять. Мы собираем только то, без чего игра не работает. Мы не показываем рекламу, не пользуемся сервисами аналитики и не продаём данные.</p>");
        sb.Append("<h2>Какие данные мы храним</h2><ul>");
        sb.Append("<li><b>Аккаунт:</b> логин, имя в игре, пароль (только в виде хеша с солью, сам пароль не хранится), номер аккаунта, дата создания, время последнего входа, сколько раз вы входили и сколько времени провели в игре.</li>");
        sb.Append("<li><b>Игровые данные:</b> ваш город и ресурсы, армия, рейтинг и лига, клан и роль в нём, лоты на рынке игроков, отчёты о набегах, участие в клановых войнах.</li>");
        sb.Append("<li><b>Сообщения в чате клана:</b> текст, имя автора и время. Сервер хранит последние 200 сообщений каждого клана; их видят участники клана.</li>");
        sb.Append("<li><b>Google Play Games (если вы входите через него):</b> идентификатор игрока Google Play Games и имя игрока. Пароль от Google мы не получаем.</li>");
        sb.Append("<li><b>Push-уведомления (если вы их разрешили):</b> токен устройства Firebase Cloud Messaging, язык уведомлений, какие уведомления включены, время окончания ваших строек.</li>");
        sb.Append("<li><b>Покупки марок:</b> номер покупки Google Play, товар и сколько марок засчитано. Данные карты обрабатывает Google, мы их не получаем.</li>");
        sb.Append("<li><b>Жалобы:</b> кто, на кого и за что пожаловался, текст сообщения, на которое пожаловались, и решение модератора. Список игроков, которых вы заблокировали.</li>");
        sb.Append("<li><b>Технические данные:</b> IP-адрес нужен только для соединения с сервером и не сохраняется.</li></ul>");
        sb.Append("<h2>Зачем</h2><ul><li>чтобы вы могли войти в аккаунт и продолжить игру на любом устройстве;</li><li>чтобы работали онлайн-функции: набеги на других игроков, кланы и чат, рынок, клановые войны, лиги;</li>"
            + "<li>чтобы засчитывать покупки и защищать их от повторного зачисления;</li><li>чтобы присылать уведомления, которые вы включили;</li><li>чтобы разбирать жалобы и защищать игроков от оскорблений и мошенничества.</li></ul>");
        sb.Append("<h2>Кому передаются данные</h2><ul>");
        sb.Append("<li>Другим игрокам видны ваше имя в игре, клан, рейтинг, уровень Цитадели, ваши сообщения в чате клана, лоты на рынке, а при набеге — ваш город.</li>");
        sb.Append("<li>Google: вход через Google Play Games, push-уведомления через Firebase Cloud Messaging, оплата через Google Play. Google обрабатывает эти данные по своей политике: <a href=\"https://policies.google.com/privacy\">policies.google.com/privacy</a>.</li>");
        sb.Append("<li>Хостинг-провайдер, у которого арендован сервер игры, хранит данные на этом сервере и не имеет права ими пользоваться.</li>");
        sb.Append("<li>Игра сделана на движке Unity, покупки идут через Unity In-App Purchasing. Unity может обрабатывать технические данные устройства (модель, версия системы, сбои) по своей политике: <a href=\"https://unity.com/legal/game-player-and-app-user-privacy-policy\">unity.com/legal</a>.</li>");
        sb.Append("<li>Мы не продаём и не передаём данные рекламодавцам и другим третьим лицам. Раскрыть данные мы можем только по законному требованию государственных органов.</li></ul>");
        sb.Append("<h2>Сколько храним</h2><p>Пока существует аккаунт. Когда вы удаляете аккаунт, сразу и безвозвратно удаляются профиль, город, ваши сообщения в чатах, лоты, токены уведомлений и привязка к Google Play Games. "
            + "Запись о покупке остаётся без привязки к вам — только чтобы ту же покупку нельзя было засчитать повторно. Разобранные жалобы хранятся до 90 дней.</p>");
        sb.Append($"<h2>Как удалить аккаунт и данные</h2><ul><li>В игре: <b>⚙ Настройки → Аккаунт → Удалить аккаунт</b>.</li><li>Без игры: на странице <a href=\"{Api.WebDelete}\">удаления аккаунта</a> (логин и пароль).</li>"
            + $"<li>Если вы входили только через Google Play Games и игры уже нет на телефоне — напишите нам: {c}, укажите имя в игре. Мы удалим аккаунт в течение 7 дней.</li></ul>");
        sb.Append("<h2>Ваши права</h2><p>Вы можете узнать, какие данные о вас хранятся, исправить их (имя в игре меняется в настройках), удалить аккаунт и отозвать согласие на уведомления (в настройках игры или телефона). "
            + $"С вопросами пишите: {c}.</p>");
        sb.Append("<h2>Безопасность</h2><p>Пароли хранятся только в виде хеша с солью. Доступ к серверу и к странице модерации есть только у разработчика.</p>");
        sb.Append("<h2>Дети</h2><p>Игра не предназначена для детей младше 13 лет. Мы сознательно не собираем данные детей; если вы узнали, что ребёнок завёл аккаунт, напишите нам — мы его удалим.</p>");
        sb.Append("<h2>Изменения</h2><p>Если политика изменится, новая версия появится на этой странице с новой датой.</p>");

        // English version (for Google Play review and players who read English)
        sb.Append($"<h1 style=\"margin-top:48px\">{g} Privacy Policy</h1><p class=\"muted\">Effective 27 October 2026. Developer: {d}. Contact: {c}</p>");
        sb.Append($"<p>{g} is an online game. We collect only what the game needs to work. We show no ads, use no analytics services and never sell data.</p><h2>Data we store</h2><ul>"
            + "<li><b>Account:</b> login, in-game name, password (stored only as a salted hash), account ID, creation date, last login, login count and play time.</li>"
            + "<li><b>Game data:</b> your town and resources, army, rating and league, clan and role, market listings, raid reports, clan war participation.</li>"
            + "<li><b>Clan chat:</b> message text, author name and time. The server keeps the last 200 messages of each clan; clan members can see them.</li>"
            + "<li><b>Google Play Games (if you sign in with it):</b> Play Games player ID and player name. We never receive your Google password.</li>"
            + "<li><b>Push notifications (if allowed):</b> Firebase Cloud Messaging device token, language, which notifications are on, when your constructions finish.</li>"
            + "<li><b>Purchases:</b> Google Play purchase ID, product and marks credited. Card data is handled by Google.</li>"
            + "<li><b>Reports:</b> who reported whom and why, the reported message and the moderator's decision; the list of players you blocked.</li>"
            + "<li><b>Technical:</b> your IP address is used only to connect and is not stored.</li></ul>");
        sb.Append("<h2>Why</h2><p>To let you sign in and continue on any device; to run online features (player raids, clans and chat, market, clan wars, leagues); to credit purchases once; to send notifications you enabled; to handle reports and protect players.</p>");
        sb.Append("<h2>Sharing</h2><p>Other players see your in-game name, clan, rating, Citadel level, your clan chat messages, market listings and, during a raid, your town. "
            + "Google processes data for Play Games sign-in, Firebase Cloud Messaging and Google Play billing under <a href=\"https://policies.google.com/privacy\">its privacy policy</a>. "
            + "The game is built with Unity and uses Unity In-App Purchasing; Unity may process technical device data (model, OS version, crashes) under <a href=\"https://unity.com/legal/game-player-and-app-user-privacy-policy\">its policy</a>. "
            + "The hosting provider stores data on the game server only. We do not sell or share data with advertisers or other third parties.</p>");
        sb.Append($"<h2>Retention and deletion</h2><p>We keep data while your account exists. Deleting the account immediately and permanently removes your profile, town, chat messages, listings, notification tokens and Google Play Games link; a purchase record is kept without any link to you so it cannot be credited twice; handled reports are kept up to 90 days. "
            + $"Delete your account in the game (<b>⚙ Settings → Account → Delete account</b>), on the <a href=\"{Api.WebDelete}\">account deletion page</a>, or by email: {c}.</p>");
        sb.Append($"<h2>Your rights, security, children</h2><p>You may access, correct and delete your data and withdraw consent to notifications. Passwords are stored only as salted hashes. The game is not directed at children under 13. Questions: {c}.</p>");
        sb.Append(Footer());
        return Page($"Политика конфиденциальности — {Config?.gameName}", sb.ToString());
    }

    // ---------- Правила игры ----------

    string TermsPage()
    {
        string g = Game, c = Contact;
        var sb = new StringBuilder();
        sb.Append($"<h1>Правила игры {g}</h1><p class=\"muted\">Действуют с 27 октября 2026 г. Создавая аккаунт, вы принимаете эти правила и <a href=\"{Api.WebPrivacy}\">политику конфиденциальности</a>.</p>");
        sb.Append("<h2>В чате, в именах игроков и кланов нельзя</h2><ul><li>оскорблять, травить и угрожать другим игрокам;</li><li>писать что-то, что разжигает ненависть по национальности, религии, полу, ориентации и т.п.;</li>"
            + "<li>публиковать сексуальный или жестокий контент, ссылки на запрещённое;</li><li>спамить, рекламировать, обманывать игроков, выманивать пароли;</li><li>продавать аккаунты, ресурсы и марки за реальные деньги;</li><li>выдавать себя за разработчика или модератора.</li></ul>");
        sb.Append("<h2>В игре нельзя</h2><ul><li>пользоваться читами, ботами, изменёнными версиями игры и ошибками игры ради выгоды;</li><li>мешать работе сервера.</li></ul>");
        sb.Append("<h2>Жалобы и модерация</h2><p>Если игрок нарушает правила, нажмите на его сообщение в чате клана или на него в списке участников и выберите <b>«Пожаловаться»</b>. Модератор разбирает жалобы и может закрыть нарушителю чат на время или навсегда, сбросить имя, стереть сообщения, а за тяжёлые нарушения — удалить аккаунт. "
            + "Чтобы больше не видеть сообщения игрока, выберите <b>«Заблокировать»</b>; снять блокировку можно в <b>⚙ Настройки → Аккаунт</b>.</p>");
        sb.Append("<h2>Покупки</h2><p>Марки — внутриигровая валюта без денежной стоимости, их нельзя вывести или обменять на деньги. Возврат платежей — по правилам Google Play.</p>");
        sb.Append($"<h2>Аккаунт</h2><p>Вы отвечаете за свой аккаунт и пароль. Удалить аккаунт можно в игре или на <a href=\"{Api.WebDelete}\">этой странице</a>. Игра предоставляется «как есть»; мы можем менять баланс и правила, сообщая об изменениях в игре.</p>");
        sb.Append($"<h2>Связь</h2><p>{c}</p>");
        sb.Append($"<h1 style=\"margin-top:48px\">{g} Rules</h1><p>By creating an account you accept these rules and the <a href=\"{Api.WebPrivacy}\">privacy policy</a>. In chat, player and clan names: no insults, harassment or threats; no hate speech; no sexual or violent content; no spam, ads, scams or phishing; no real-money trading; no impersonating staff. No cheats, bots or modified clients. "
            + "Use <b>Report</b> on a chat message or a clan member to report abuse and <b>Block</b> to hide a player's messages. Moderators may mute, reset names, delete messages or delete accounts. Marks are virtual currency with no cash value. Contact: " + c + "</p>");
        sb.Append(Footer());
        return Page($"Правила игры — {Config?.gameName}", sb.ToString());
    }

    // ---------- Удаление аккаунта с сайта ----------

    string DeletePage(string message, bool done)
    {
        var sb = new StringBuilder();
        sb.Append($"<h1>Удаление аккаунта {Game}</h1>");
        if (done)
        {
            sb.Append($"<div class=\"box\"><p class=\"ok\"><b>{H(message)}</b></p><p>Город, ресурсы, место в клане, сообщения в чате, лоты и уведомления удалены. Восстановить аккаунт нельзя.</p></div>");
            sb.Append(Footer());
            return Page("Аккаунт удалён", sb.ToString());
        }
        sb.Append("<p>Здесь можно удалить аккаунт, не открывая игру. Будут <b>безвозвратно</b> удалены: профиль, город и ресурсы, марки (в том числе купленные), место в клане и ваши сообщения в чате, лоты на рынке, рейтинг, токены уведомлений и привязка к Google Play Games.</p>");
        sb.Append($"<p class=\"muted\">В игре то же самое: ⚙ Настройки → Аккаунт → Удалить аккаунт. Если вы входили только через Google Play Games (без пароля), удалите аккаунт в игре или напишите: {Contact} — удалим в течение 7 дней.</p>");
        if (!string.IsNullOrEmpty(message)) sb.Append($"<p class=\"bad\"><b>{H(message)}</b></p>");
        sb.Append($"<form method=\"post\" action=\"{Api.WebDelete}\" class=\"box\"><label>Логин<input name=\"login\" autocomplete=\"username\" required></label>"
            + "<label>Пароль<input name=\"password\" type=\"password\" autocomplete=\"current-password\" required></label>"
            + $"<label>Для подтверждения введите слово <b>{AccountRules.DeleteWord}</b><input name=\"confirm\" required></label>"
            + "<button class=\"danger\" type=\"submit\">Удалить аккаунт навсегда</button></form>");
        sb.Append("<h2>Account deletion (English)</h2><p>Enter your login and password and type DELETE to permanently delete your account and all its data (profile, town, purchased marks, clan membership and chat messages, listings, rating, notification tokens, Google Play Games link). "
            + $"If you only used Google Play Games sign-in, delete the account in the game or email {Contact}.</p>");
        sb.Append(Footer());
        return Page("Удаление аккаунта", sb.ToString());
    }

    readonly Dictionary<string, List<DateTime>> deleteTries = new Dictionary<string, List<DateTime>>();

    WebReply DeleteByForm(Dictionary<string, string> f)
    {
        string login = Arg(f, "login").Trim();
        if (!AccountRules.IsDeleteWord(Arg(f, "confirm"))) return WebReply.Html(DeletePage($"Введите слово {AccountRules.DeleteWord}, чтобы подтвердить удаление", false));
        // Не больше 10 попыток в час на логин (подбор пароля)
        string k = Key(login);
        if (!deleteTries.TryGetValue(k, out List<DateTime> tries)) deleteTries[k] = tries = new List<DateTime>();
        tries.RemoveAll(t => (DateTime.UtcNow - t).TotalHours > 1);
        if (tries.Count >= 10) return WebReply.Html(DeletePage("Слишком много попыток. Попробуйте через час", false));
        tries.Add(DateTime.UtcNow);
        StoredAccount a = FindByLogin(login);
        if (a == null || string.IsNullOrEmpty(a.hash) || a.hash != Hash(a.salt, Arg(f, "password"))) return WebReply.Html(DeletePage("Неверный логин или пароль", false));
        string name = a.profile.displayName;
        DeleteAccountData(a, "на сайте");
        Save();
        deleteTries.Remove(k);
        return WebReply.Html(DeletePage($"Аккаунт «{name}» удалён", true));
    }

    // ---------- Модерация ----------

    bool AdminOk(Dictionary<string, string> a)
    {
        string key = Config != null ? Config.adminKey : null;
        return !string.IsNullOrEmpty(key) && Arg(a, "key") == key;
    }

    WebReply AdminPage(Dictionary<string, string> args)
    {
        if (!AdminOk(args)) return new WebReply { code = 403, body = Page("Нет доступа", "<h1>Нет доступа</h1><p>Откройте ссылку модерации из окна сервера (ключ — adminKey в server-config.json).</p>") };
        string key = WebUtility.UrlEncode(Arg(args, "key"));
        bool all = Arg(args, "all") == "1";
        var sb = new StringBuilder();
        sb.Append($"<h1>Администратор {Game}</h1><p class=\"muted\">Игроков: {RealPlayers()} (и NPC-деревень: {db.accounts.Count - RealPlayers()}). Удалили аккаунт сами: {db.deletedAccounts}. Новых жалоб: {db.reports.FindAll(x => x != null && !x.done).Count}.</p>");
        sb.Append(AdminNav(key));
        sb.Append(AdminUpdateBox(key));
        sb.Append($"<form method=\"get\" action=\"{Api.WebAdmin}/player\" class=\"box\"><input type=\"hidden\" name=\"key\" value=\"{H(Arg(args, "key"))}\">"
            + "<label>Открыть базу игрока по логину<input name=\"login\" placeholder=\"логин или имя в игре\"></label><button type=\"submit\">Открыть</button></form>");
        sb.Append("<h2>Жалобы</h2>");
        sb.Append(all ? $"<p><a class=\"btn ghost\" href=\"{Api.WebAdmin}?key={key}\">Только новые</a></p>" : $"<p><a class=\"btn ghost\" href=\"{Api.WebAdmin}?key={key}&all=1\">Показать и разобранные</a></p>");

        var list = new List<ChatReportRecord>(db.reports);
        list.Reverse();
        int shown = 0;
        foreach (var r in list)
        {
            if (r == null || (!all && r.done)) continue;
            if (++shown > 150) break;
            StoredAccount t = FindById(r.targetId);
            string who = t != null ? H(t.profile.displayName) + (t.profile.Muted ? " <span class=\"bad\">(чат закрыт)</span>" : "") : H(r.targetName) + " <span class=\"muted\">(аккаунта нет)</span>";
            sb.Append("<div class=\"box\">");
            sb.Append($"<p><b>{who}</b> — {H(r.reason)} <span class=\"muted\">· пожаловался {H(r.reporterName)} · {H(LocalTime(r.timeUtc))}{(string.IsNullOrEmpty(r.clanName) ? "" : " · клан «" + H(r.clanName) + "»")}</span></p>");
            if (!string.IsNullOrEmpty(r.message)) sb.Append($"<p>Сообщение: «{H(r.message)}»</p>");
            if (r.context != null && r.context.Count > 0)
            {
                sb.Append("<p class=\"muted\">Последние сообщения игрока:</p><ul>");
                foreach (var m in r.context) sb.Append($"<li>{H(m)}</li>");
                sb.Append("</ul>");
            }
            if (r.done) sb.Append($"<p class=\"ok\">Разобрано: {H(r.action)}</p>");
            else if (t != null)
            {
                string act(string what, string label, string cls) => $"<a class=\"btn {cls}\" href=\"{Api.WebAdmin}/act?key={key}&do={what}&id={WebUtility.UrlEncode(t.profile.id)}&r={WebUtility.UrlEncode(r.id)}\">{label}</a>";
                sb.Append("<p>" + act("mute1", "Закрыть чат на сутки", "") + act("mute7", "на неделю", "") + act("muteall", "навсегда", "danger")
                    + act("rename", "Сбросить имя", "ghost") + act("purge", "Стереть его сообщения", "ghost") + act("done", "Нарушения нет", "ghost") + "</p>");
            }
            else sb.Append($"<p><a class=\"btn ghost\" href=\"{Api.WebAdmin}/act?key={key}&do=done&r={WebUtility.UrlEncode(r.id)}\">Закрыть жалобу</a></p>");
            sb.Append("</div>");
        }
        if (shown == 0) sb.Append("<p>Жалоб нет.</p>");

        var muted = db.accounts.FindAll(x => x != null && x.profile != null && x.profile.Muted);
        if (muted.Count > 0)
        {
            sb.Append("<h2>Закрыт чат</h2><table><tr><th>Игрок</th><th>До</th><th></th></tr>");
            foreach (var m in muted)
                sb.Append($"<tr><td>{H(m.profile.displayName)}</td><td>{H(LocalTime(m.profile.mutedUtc))}</td><td><a class=\"btn ghost\" href=\"{Api.WebAdmin}/act?key={key}&do=unmute&id={WebUtility.UrlEncode(m.profile.id)}\">Открыть чат</a></td></tr>");
            sb.Append("</table>");
        }
        sb.Append(AdminLogHtml());
        sb.Append("<p class=\"muted\" style=\"margin-top:30px\">Не делитесь этой ссылкой: по ней можно модерировать игроков.</p>");
        return WebReply.Html(Page("Модерация", sb.ToString()));
    }

    static string LocalTime(string utc)
    {
        DateTime d = ParseUtc(utc);
        return d == DateTime.MinValue ? "" : d.ToString("dd.MM.yyyy HH:mm") + " UTC";
    }

    WebReply AdminAct(Dictionary<string, string> args)
    {
        if (!AdminOk(args)) return new WebReply { code = 403, body = Page("Нет доступа", "<h1>Нет доступа</h1>") };
        string what = Arg(args, "do"), rid = Arg(args, "r");
        StoredAccount t = FindById(Arg(args, "id"));
        if (AdminMarkMe(what, t)) return WebReply.Go($"{Api.WebAdmin}/player?key={WebUtility.UrlEncode(Arg(args, "key"))}&id={WebUtility.UrlEncode(t.profile.id)}");
        string action = null;
        if (t != null)
            switch (what)
            {
                case "mute1": t.profile.mutedUtc = DateTime.UtcNow.AddDays(1).ToString("o"); action = "чат закрыт на сутки"; break;
                case "mute7": t.profile.mutedUtc = DateTime.UtcNow.AddDays(7).ToString("o"); action = "чат закрыт на неделю"; break;
                case "muteall": t.profile.mutedUtc = DateTime.UtcNow.AddYears(100).ToString("o"); action = "чат закрыт навсегда"; break;
                case "unmute": t.profile.mutedUtc = null; action = "чат открыт"; break;
                case "rename":
                    string old = t.profile.displayName;
                    t.profile.displayName = "Игрок " + t.profile.id.Substring(0, 5).ToUpperInvariant();
                    action = $"имя «{old}» сброшено";
                    break;
                case "purge":
                    foreach (var c in db.clans) c.chat.RemoveAll(m => m != null && m.authorId == t.profile.id);
                    action = "сообщения стёрты";
                    break;
                case "done": action = "нарушения нет"; break;
            }
        else if (what == "done") action = "закрыта";
        if (action != null)
        {
            foreach (var r in db.reports)
                if (r != null && !r.done && (r.id == rid || (t != null && r.targetId == t.profile.id && what != "unmute" && what != "done")))
                {
                    r.done = true;
                    r.action = action;
                }
            Save();
            if (t != null) Debug.Log($"[Модерация] {t.profile.displayName}: {action}");
        }
        return WebReply.Go($"{Api.WebAdmin}?key={WebUtility.UrlEncode(Arg(args, "key"))}");
    }
}
