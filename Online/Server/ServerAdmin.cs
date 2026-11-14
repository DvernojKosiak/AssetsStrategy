using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using UnityEngine;

/// <summary>
/// Инструменты администратора на странице модерации (тот же ключ adminKey, только сервер, игру менять не нужно):
///   /admin/players?key=...           - все игроки: Ратуша, рейтинг, клан, марки, армия, когда заходили;
///   /admin/player?key=...&amp;login=...  - база игрока: карта, здания по уровням, казна, армия, история рейтинга;
///   /admin/grant (форма)              - выписать игроку марки и ресурсы. Доходят так же, как награды за войну:
///                                       в игре - при ближайшем сохранении, если игрок не в игре - при входе.
/// </summary>
public partial class ServerCore
{
    partial class Db
    {
        public string adminAccountId;                          // «мой аккаунт» администратора (кнопка на странице игрока)
        public List<string> adminLog = new List<string>();     // что выписывал администратор
    }

    const int AdminGrantMax = 1000000;   // за один раз, на каждый ресурс
    const int AdminLogMax = 300;

    string AdminKeyArg(Dictionary<string, string> args) { return WebUtility.UrlEncode(Arg(args, "key")); }

    string AdminNav(string key)
    {
        return $"<p><a class=\"btn ghost\" href=\"{Api.WebAdmin}?key={key}\">Жалобы</a>"
             + $"<a class=\"btn ghost\" href=\"{Api.WebAdmin}/players?key={key}\">Игроки</a>"
             + (string.IsNullOrEmpty(db.adminAccountId) || FindById(db.adminAccountId) == null ? ""
                 : $"<a class=\"btn\" href=\"{Api.WebAdmin}/player?key={key}&id={WebUtility.UrlEncode(db.adminAccountId)}\">Мой аккаунт</a>")
             + "</p>";
    }

    static WebReply AdminDenied() { return new WebReply { code = 403, body = Page("Нет доступа", "<h1>Нет доступа</h1><p>Откройте ссылку модерации из окна сервера (ключ — adminKey в server-config.json).</p>") }; }

    bool IsOnline(string id) { return lastSeen.TryGetValue(id, out DateTime t) && (DateTime.UtcNow - t).TotalSeconds < 60; }

    static string N(long v) { return v.ToString("#,0", CultureInfo.InvariantCulture).Replace(",", " "); }

    static string PlayTime(float seconds)
    {
        int m = (int)(seconds / 60f);
        if (m < 60) return m + " мин";
        int h = m / 60;
        return h < 48 ? $"{h} ч {m % 60} мин" : $"{h / 24} д {h % 24} ч";
    }

    static string Ago(string utc)
    {
        DateTime d = ParseUtc(utc);
        if (d == DateTime.MinValue) return "—";
        TimeSpan t = DateTime.UtcNow - d.ToUniversalTime();
        if (t.TotalMinutes < 1) return "только что";
        if (t.TotalHours < 1) return (int)t.TotalMinutes + " мин назад";
        if (t.TotalDays < 1) return (int)t.TotalHours + " ч назад";
        return (int)t.TotalDays + " д назад";
    }

    string ClanName(StoredAccount a)
    {
        if (a == null || string.IsNullOrEmpty(a.clanId)) return "";
        StoredClan c = db.clans.Find(x => x != null && x.id == a.clanId);
        return c != null ? c.name : "";
    }

    StoredAccount AdminFindPlayer(Dictionary<string, string> args)
    {
        string id = Arg(args, "id"), login = Arg(args, "login").Trim();
        StoredAccount a = id != "" ? FindById(id) : null;
        if (a == null && login != "") a = FindByLogin(login);
        if (a == null && login != "")   // по имени в игре
            a = db.accounts.Find(x => x != null && x.profile != null && string.Equals((x.profile.displayName ?? "").Trim(), login, StringComparison.OrdinalIgnoreCase));
        return a;
    }

    // ---------- Все игроки ----------

    WebReply AdminPlayers(Dictionary<string, string> args)
    {
        if (!AdminOk(args)) return AdminDenied();
        string key = AdminKeyArg(args);
        string q = Arg(args, "q").Trim();
        string sort = Arg(args, "sort");
        var sb = new StringBuilder();
        sb.Append($"<h1>Игроки {Game}</h1>");
        sb.Append(AdminNav(key));
        sb.Append($"<form method=\"get\" action=\"{Api.WebAdmin}/players\" class=\"box\"><input type=\"hidden\" name=\"key\" value=\"{H(Arg(args, "key"))}\">"
            + $"<label>Найти по логину или имени<input name=\"q\" value=\"{H(q)}\" placeholder=\"например, vasya\"></label><button type=\"submit\">Найти</button></form>");

        var rows = new List<(StoredAccount a, CityData c, int hall)>();
        foreach (var a in db.accounts)
        {
            if (a == null || a.profile == null) continue;
            if (q != "" && (a.profile.login ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0
                        && (a.profile.displayName ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
            CityData c = ReadCity(a.profile.id);
            rows.Add((a, c, c != null ? CityLoss.HallLevel(c) : 0));
        }
        if (sort == "hall") rows.Sort((x, y) => y.hall != x.hall ? y.hall.CompareTo(x.hall) : y.a.profile.rating.CompareTo(x.a.profile.rating));
        else if (sort == "rating") rows.Sort((x, y) => y.a.profile.rating.CompareTo(x.a.profile.rating));
        else if (sort == "time") rows.Sort((x, y) => y.a.profile.playSeconds.CompareTo(x.a.profile.playSeconds));
        else rows.Sort((x, y) => ParseUtc(y.a.profile.lastLoginUtc).CompareTo(ParseUtc(x.a.profile.lastLoginUtc)));

        string qs = q == "" ? "" : "&q=" + WebUtility.UrlEncode(q);
        string sortLink(string s, string label) => sort == s || (s == "" && sort == "") ? $"<b>{label}</b>" : $"<a href=\"{Api.WebAdmin}/players?key={key}{qs}{(s == "" ? "" : "&sort=" + s)}\">{label}</a>";
        sb.Append($"<p class=\"muted\">Найдено: {rows.Count}. Сортировка: {sortLink("", "по входу")} · {sortLink("hall", "по Ратуше")} · {sortLink("rating", "по рейтингу")} · {sortLink("time", "по времени в игре")}</p>");
        sb.Append("<div style=\"overflow-x:auto\"><table><tr><th>Игрок</th><th>Ратуша</th><th>Рейтинг</th><th>Марки</th><th>Золото</th><th>Армия</th><th>Заходил</th><th>В игре</th></tr>");
        int shown = 0;
        foreach (var (a, c, hall) in rows)
        {
            if (++shown > 500) break;
            var p = a.profile;
            string clan = ClanName(a);
            string dot = IsOnline(p.id) ? "<span class=\"ok\" title=\"в игре\">●</span> " : "";
            sb.Append("<tr>");
            sb.Append($"<td>{dot}<a href=\"{Api.WebAdmin}/player?key={key}&id={WebUtility.UrlEncode(p.id)}\"><b>{H(p.displayName)}</b></a>"
                + (p.id == db.adminAccountId ? " <span class=\"ok\">(вы)</span>" : "")
                + (a.npc ? " <span class=\"muted\">(NPC-деревня)</span>" : "")
                + $"<br><span class=\"muted\">{H(p.login)}{(clan == "" ? "" : " · " + H(clan))}</span></td>");
            sb.Append(c == null ? "<td colspan=\"4\" class=\"muted\">города ещё нет</td>"
                : $"<td>{hall}</td><td>{p.rating}<br><span class=\"muted\">{H(Leagues.Name(Leagues.Of(p.rating)))}</span></td><td>{N(c.marks)}</td><td>{N(CityLoss.Available(c).gold)}</td>");
            sb.Append(c == null ? "" : $"<td>{(c.army != null ? c.army.Count : 0)}</td>");
            sb.Append($"<td>{H(Ago(p.lastLoginUtc))}</td><td>{H(PlayTime(p.playSeconds))}</td>");
            sb.Append("</tr>");
        }
        sb.Append("</table></div>");
        if (rows.Count == 0) sb.Append("<p>Никого не нашлось.</p>");
        // Обновление 12: марки и ресурсы всем сразу (тестерам на закрытом тесте, подарок к празднику)
        string allMsg = Arg(args, "msg");
        if (allMsg != "") sb.Append($"<p class=\"ok\">{H(allMsg)}</p>");
        sb.Append("<h2>Выписать всем игрокам</h2>");
        sb.Append($"<form method=\"post\" action=\"{Api.WebAdmin}/grantall\" class=\"box\" onsubmit=\"return confirm('Выписать это ВСЕМ игрокам?')\">"
            + $"<input type=\"hidden\" name=\"key\" value=\"{H(WebUtility.UrlDecode(key))}\">"
            + "<div style=\"display:grid;grid-template-columns:repeat(auto-fit,minmax(130px,1fr));gap:0 12px\">");
        foreach (var (name, label) in new[] { ("marks", "Марки ◈"), ("gold", "Золото"), ("wood", "Дерево"), ("stone", "Камень"), ("food", "Еда"), ("iron", "Железо") })
            sb.Append($"<label>{label}<input name=\"{name}\" type=\"number\" min=\"0\" max=\"{AdminGrantAllMax}\" inputmode=\"numeric\" placeholder=\"0\"></label>");
        sb.Append("</div><label>Надпись в игре<input name=\"text\" maxlength=\"80\" placeholder=\"Подарок от администрации\"></label>"
            + "<button type=\"submit\">Выписать всем</button>"
            + $"<p class=\"muted\">Каждому игроку с городом. Не больше {N(AdminGrantAllMax)} каждого. Придёт так же, как подарок одному игроку.</p></form>");
        sb.Append("<p class=\"muted\" style=\"margin-top:30px\">Не делитесь этой ссылкой: по ней видно всех игроков и можно выписывать марки.</p>");
        return WebReply.Html(Page("Игроки", sb.ToString()));
    }

    // ---------- База одного игрока ----------

    WebReply AdminPlayer(Dictionary<string, string> args)
    {
        if (!AdminOk(args)) return AdminDenied();
        string key = AdminKeyArg(args);
        StoredAccount a = AdminFindPlayer(args);
        var sb = new StringBuilder();
        if (a == null)
        {
            sb.Append("<h1>Игрок не найден</h1>" + AdminNav(key));
            sb.Append($"<p>Нет игрока с логином или именем «{H(Arg(args, "login"))}».</p>");
            return WebReply.Html(Page("Игрок не найден", sb.ToString()));
        }
        var p = a.profile;
        string idEnc = WebUtility.UrlEncode(p.id);
        CityData c = ReadCity(p.id);

        sb.Append($"<h1>{H(p.displayName)}{(p.id == db.adminAccountId ? " <span class=\"ok\" style=\"font-size:16px\">(ваш аккаунт)</span>" : "")}</h1>");
        sb.Append(AdminNav(key));
        string msg = Arg(args, "msg");
        if (msg != "") sb.Append($"<div class=\"box\"><p class=\"ok\"><b>{H(msg)}</b></p></div>");

        // Профиль
        string clan = ClanName(a);
        sb.Append("<div class=\"box\"><table>");
        void Row(string k, string v) { sb.Append($"<tr><td class=\"muted\" style=\"width:40%\">{k}</td><td>{v}</td></tr>"); }
        Row("Логин", H(p.login) + (p.googleLinked ? " <span class=\"muted\">· Google Play Games</span>" : ""));
        Row("Сейчас", IsOnline(p.id) ? "<span class=\"ok\">в игре</span>" : "не в игре");
        Row("Последний вход", H(LocalTime(p.lastLoginUtc)) + " · " + H(Ago(p.lastLoginUtc)));
        Row("Аккаунт создан", H(LocalTime(p.createdUtc)));
        Row("Входов / время в игре", $"{p.logins} / {H(PlayTime(p.playSeconds))}");
        Row("Рейтинг", $"{p.rating} · {H(Leagues.Name(Leagues.Of(p.rating)))}");
        Row("Клан", clan == "" ? "—" : H(clan));
        Row("Щит", Shielded(a) ? "до " + H(LocalTime(a.shieldUtc)) : "нет");
        if (p.Muted) Row("Чат", "<span class=\"bad\">закрыт до " + H(LocalTime(p.mutedUtc)) + "</span>");
        if (c != null) Row("Город сохранён", H(LocalTime(c.savedUtc)) + " · " + H(Ago(c.savedUtc)));
        sb.Append("</table></div>");

        // Выписать марки и ресурсы
        sb.Append(GrantForm(key, a));

        if (c == null)
        {
            sb.Append("<p>Города ещё нет: игрок не начал игру.</p>");
        }
        else
        {
            CityReport(sb, c);
        }

        // История рейтинга
        if (a.ratingLog != null && a.ratingLog.Count > 0)
        {
            sb.Append("<h2>Рейтинг: последние изменения</h2><table>");
            int n = 0;
            for (int i = a.ratingLog.Count - 1; i >= 0 && n < 15; i--, n++)
            {
                var r = a.ratingLog[i];
                if (r == null) continue;
                sb.Append($"<tr><td>{(r.delta > 0 ? "+" : "")}{r.delta}</td><td>{r.rating}</td><td>{H(r.text)}</td><td class=\"muted\">{H(LocalTime(r.timeUtc))}</td></tr>");
            }
            sb.Append("</table>");
        }

        sb.Append("<h2>Это мой аккаунт</h2>");
        if (p.id == db.adminAccountId)
            sb.Append($"<p>Этот аккаунт отмечен как ваш: кнопка «Мой аккаунт» наверху ведёт сюда. <a class=\"btn ghost\" href=\"{Api.WebAdmin}/act?key={key}&do=notme&id={idEnc}\">Снять отметку</a></p>");
        else
            sb.Append($"<p class=\"muted\">Отметьте свой аккаунт, чтобы открывать его одной кнопкой.</p><p><a class=\"btn ghost\" href=\"{Api.WebAdmin}/act?key={key}&do=me&id={idEnc}\">Это мой аккаунт</a></p>");

        sb.Append("<p class=\"muted\" style=\"margin-top:30px\">Не делитесь этой ссылкой: по ней видно всех игроков и можно выписывать марки.</p>");
        return WebReply.Html(Page(p.displayName, sb.ToString()));
    }

    string GrantForm(string key, StoredAccount a)
    {
        var sb = new StringBuilder();
        sb.Append("<h2>Выписать марки и ресурсы</h2>");
        sb.Append($"<form method=\"post\" action=\"{Api.WebAdmin}/grant\" class=\"box\">"
            + $"<input type=\"hidden\" name=\"key\" value=\"{H(WebUtility.UrlDecode(key))}\"><input type=\"hidden\" name=\"id\" value=\"{H(a.profile.id)}\">"
            + "<div style=\"display:grid;grid-template-columns:repeat(auto-fit,minmax(130px,1fr));gap:0 12px\">");
        foreach (var (name, label) in new[] { ("marks", "Марки ◈"), ("gold", "Золото"), ("wood", "Дерево"), ("stone", "Камень"), ("food", "Еда"), ("iron", "Железо") })
            sb.Append($"<label>{label}<input name=\"{name}\" type=\"number\" min=\"0\" max=\"{AdminGrantMax}\" inputmode=\"numeric\" placeholder=\"0\"></label>");
        sb.Append("</div><label>Надпись в игре<input name=\"text\" maxlength=\"80\" placeholder=\"Подарок от администрации\"></label>"
            + "<button type=\"submit\">Выписать</button>"
            + $"<p class=\"muted\">Не больше {N(AdminGrantMax)} каждого за раз. Если игрок в игре, всё придёт в течение минуты (при сохранении города), "
            + "если нет — при следующем входе. Ресурсы сверх вместимости складов игра не примет, марки — без ограничений.</p></form>");

        if (a.grants != null && a.grants.Count > 0)
        {
            sb.Append("<p class=\"muted\">Ещё не дошло до игры:</p><ul>");
            foreach (var g in a.grants) if (g != null) sb.Append($"<li>{H(g.text)}: {H(GrantText(g))}</li>");
            sb.Append("</ul>");
        }
        return sb.ToString();
    }

    static string GrantText(WarGrant g)
    {
        var parts = new List<string>();
        if (g.marks > 0) parts.Add(N(g.marks) + " марок");
        if (g.gold > 0) parts.Add(N(g.gold) + " золота");
        if (g.wood > 0) parts.Add(N(g.wood) + " дерева");
        if (g.stone > 0) parts.Add(N(g.stone) + " камня");
        if (g.food > 0) parts.Add(N(g.food) + " еды");
        if (g.iron > 0) parts.Add(N(g.iron) + " железа");
        return parts.Count == 0 ? "ничего" : string.Join(", ", parts);
    }

    static readonly (string field, string label)[] ResLabels =
    {
        ("marks", "Марки ◈"), ("gold", "Золото"), ("wood", "Дерево"), ("stone", "Камень"), ("food", "Еда"), ("iron", "Железо"),
        ("oats", "Овёс"), ("berries", "Ягоды"), ("herbs", "Травы"), ("potions", "Зелья"), ("slowPotions", "Зелья замедления"),
        ("parts", "Детали"), ("leaves", "Листья"), ("resin", "Смола"), ("tar", "Дёготь"), ("rubber", "Каучук"),
    };

    static readonly string[] RefinedNames = { "Доски", "Каменные плиты", "Сталь", "Укреплённые плиты", "Просмолённые брусья", "Монолит" };
    static readonly string[] TierNames = { "обычн.", "хорош.", "отличн.", "редк.", "легенд." };

    static int ResField(CityData c, string f)
    {
        switch (f)
        {
            case "marks": return c.marks; case "gold": return c.gold; case "wood": return c.wood; case "stone": return c.stone;
            case "food": return c.food; case "iron": return c.iron; case "oats": return c.oats; case "berries": return c.berries;
            case "herbs": return c.herbs; case "potions": return c.potions; case "slowPotions": return c.slowPotions; case "parts": return c.parts;
            case "leaves": return c.leaves; case "resin": return c.resin; case "tar": return c.tar; case "rubber": return c.rubber;
        }
        return 0;
    }

    static int InBuildings(CityData c, string f)
    {
        int s = 0;
        if (c.buildings != null)
            foreach (var b in c.buildings)
            {
                if (b == null) continue;
                switch (f)
                {
                    case "gold": s += b.gold; break; case "wood": s += b.wood; break; case "stone": s += b.stone; break;
                    case "food": s += b.food; break; case "iron": s += b.iron; break; case "oats": s += b.oats; break;
                    case "berries": s += b.berries; break; case "herbs": s += b.herbs; break; case "resin": s += b.resin; break;
                    case "leaves": s += b.leaves; break;
                }
            }
        return s;
    }

    void CityReport(StringBuilder sb, CityData c)
    {
        int hall = CityLoss.HallLevel(c);
        int buildings = c.buildings != null ? c.buildings.FindAll(b => b != null).Count : 0;
        sb.Append($"<h2>Город</h2><p>Ратуша <b>{hall}</b> ур. · зданий <b>{buildings}</b> · жителей <b>{c.population}</b> · армия <b>{(c.army != null ? c.army.Count : 0)}</b> · день {c.day}</p>");

        // Казна
        sb.Append("<h2>Казна</h2><table><tr><th>Ресурс</th><th>В казне</th><th>На складах зданий</th></tr>");
        foreach (var (f, label) in ResLabels)
        {
            int v = ResField(c, f), inB = InBuildings(c, f);
            if (v == 0 && inB == 0 && f != "marks" && f != "gold") continue;
            sb.Append($"<tr><td>{label}</td><td><b>{N(v)}</b></td><td>{(inB > 0 ? N(inB) : "")}</td></tr>");
        }
        if (c.refined != null)
            foreach (var r in c.refined)
            {
                if (r == null || r.amount <= 0) continue;
                string name = r.kind >= 0 && r.kind < RefinedNames.Length ? RefinedNames[r.kind] : "Материал " + r.kind;
                string tier = r.tier >= 0 && r.tier < TierNames.Length ? TierNames[r.tier] : r.tier.ToString();
                sb.Append($"<tr><td>{H(name)} <span class=\"muted\">({H(tier)})</span></td><td><b>{N(r.amount)}</b></td><td></td></tr>");
            }
        sb.Append("</table>");

        // Путь развития
        sb.Append($"<p class=\"muted\">Путь развития: {c.path} · школа: {c.school} · инженеры: {c.eng} · военное дело: {c.war}</p>");

        // Карта
        sb.Append("<h2>Карта</h2>");
        sb.Append(CityMap(c));

        // Здания по типам
        if (c.buildings != null && c.buildings.Count > 0)
        {
            var byType = new SortedDictionary<string, List<int>>(StringComparer.CurrentCulture);
            foreach (var b in c.buildings)
            {
                if (b == null) continue;
                string t = string.IsNullOrEmpty(b.type) ? "?" : b.type;
                if (!byType.TryGetValue(t, out List<int> lv)) byType[t] = lv = new List<int>();
                lv.Add(b.level);
            }
            sb.Append("<h2>Здания</h2><table><tr><th>Здание</th><th>Сколько</th><th>Уровни</th></tr>");
            foreach (var kv in byType)
            {
                kv.Value.Sort((x, y) => y.CompareTo(x));
                sb.Append($"<tr><td>{H(kv.Key)}</td><td>{kv.Value.Count}</td><td>{string.Join(", ", kv.Value)}</td></tr>");
            }
            sb.Append("</table>");
        }

        // Армия
        if (c.army != null && c.army.Count > 0)
        {
            var units = new SortedDictionary<string, int[]>(StringComparer.CurrentCulture);   // всего, с оружием, со щитом, в броне, наёмники
            foreach (var s in c.army)
            {
                if (s == null) continue;
                string u = string.IsNullOrEmpty(s.unit) ? "?" : s.unit;
                if (!units.TryGetValue(u, out int[] n)) units[u] = n = new int[5];
                n[0]++;
                if (s.hasWeapon) n[1]++;
                if (s.hasShield) n[2]++;
                if (s.hasBody) n[3]++;
                if (s.merc) n[4]++;
            }
            sb.Append("<h2>Армия</h2><table><tr><th>Бойцы</th><th>Всего</th><th>С оружием</th><th>Со щитом</th><th>В броне</th><th>Наёмники</th></tr>");
            foreach (var kv in units)
                sb.Append($"<tr><td>{H(kv.Key)}</td><td><b>{kv.Value[0]}</b></td><td>{kv.Value[1]}</td><td>{kv.Value[2]}</td><td>{kv.Value[3]}</td><td>{kv.Value[4]}</td></tr>");
            sb.Append("</table>");
        }
        else sb.Append("<h2>Армия</h2><p>Бойцов нет.</p>");

        // Остальное (ловушки, модули, снаряжение...) - как есть
        if (c.extra != null && c.extra.Count > 0)
        {
            sb.Append($"<details><summary class=\"muted\" style=\"cursor:pointer;margin-top:18px\">Прочие запасы и настройки города ({c.extra.Count} записей, как в сохранении)</summary><ul style=\"font-size:13px;word-break:break-all\">");
            int n = 0;
            foreach (var e in c.extra) { if (++n > 400) break; sb.Append($"<li>{H(e)}</li>"); }
            sb.Append("</ul></details>");
        }
    }

    /// <summary>Карта города картинкой SVG: трава, вода, лес и здания.</summary>
    static string CityMap(CityData c)
    {
        int size = c.mapSize;
        if (size <= 0 || size > 1000)
        {
            // Старое сохранение без местности: карта по зданиям
            size = 40;
            if (c.buildings != null)
                foreach (var b in c.buildings) if (b != null) size = Mathf.Max(size, 2 * (Mathf.Max(Mathf.Abs(b.x), Mathf.Abs(b.y)) + 4));
        }
        int half = size / 2;
        const int cell = 8;
        var sb = new StringBuilder();
        sb.Append($"<div style=\"overflow:auto;background:#0f1013;border-radius:10px;padding:6px\"><svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {size * cell} {size * cell}\" style=\"width:100%;max-width:{size * cell}px;display:block;margin:auto\">");
        sb.Append($"<rect width=\"{size * cell}\" height=\"{size * cell}\" fill=\"#35512f\"/>");

        // Местность: строки снизу вверх, сжатые «буква+число»
        if (!string.IsNullOrEmpty(c.terrain) && c.mapSize == size)
        {
            int idx = 0;
            string t = c.terrain;
            int i = 0;
            while (i < t.Length)
            {
                char ch = t[i++];
                int j = i;
                while (j < t.Length && char.IsDigit(t[j])) j++;
                int run = j > i && int.TryParse(t.Substring(i, j - i), out int r) ? r : 1;
                i = j;
                string fill = ch == TerrainCode.Water ? "#2f6d9e" : ch == TerrainCode.Forest ? "#1f3a1e" : null;
                while (run > 0 && idx < size * size)
                {
                    int row = idx / size, col = idx % size;
                    int take = Mathf.Min(run, size - col);
                    if (fill != null)
                        sb.Append($"<rect x=\"{col * cell}\" y=\"{(size - 1 - row) * cell}\" width=\"{take * cell}\" height=\"{cell}\" fill=\"{fill}\"/>");
                    idx += take;
                    run -= take;
                }
            }
        }

        // Здания
        if (c.buildings != null)
            foreach (var b in c.buildings)
            {
                if (b == null) continue;
                int w = b.w > 0 ? b.w : 2, h = b.h > 0 ? b.h : 2;
                int x = (b.x + half) * cell, y = (size - (b.y + half) - h) * cell;
                bool hall = b.type == "Ратуша";
                string fill = hall ? "#f4cc6b" : b.fortified ? "#c9a06a" : "#b98a56";
                sb.Append($"<g><title>{H(b.type)} · ур. {b.level}</title><rect x=\"{x}\" y=\"{y}\" width=\"{w * cell}\" height=\"{h * cell}\" fill=\"{fill}\" stroke=\"#1b1608\" stroke-width=\"1\"/>");
                if (w * cell >= 14 && h * cell >= 12)
                    sb.Append($"<text x=\"{x + w * cell / 2}\" y=\"{y + h * cell / 2 + 4}\" font-size=\"10\" text-anchor=\"middle\" fill=\"#1b1608\" font-family=\"sans-serif\" font-weight=\"700\">{b.level}</text>");
                sb.Append("</g>");
            }
        sb.Append("</svg></div><p class=\"muted\">Цифра на здании — его уровень. Наведите или нажмите на здание, чтобы увидеть название. Светлое — Ратуша, синее — вода, тёмно-зелёное — лес.</p>");
        return sb.ToString();
    }

    // ---------- Выписать марки и ресурсы ----------

    static int GrantArg(Dictionary<string, string> args, string k)
    {
        string s = Arg(args, k).Trim().Replace(" ", "");
        if (s == "" || !long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v)) return 0;
        return (int)Math.Max(0, Math.Min(AdminGrantMax, v));
    }

    WebReply AdminGrant(Dictionary<string, string> args)
    {
        if (!AdminOk(args)) return AdminDenied();
        string key = AdminKeyArg(args);
        StoredAccount a = FindById(Arg(args, "id"));
        if (a == null) return WebReply.Go($"{Api.WebAdmin}/players?key={key}");
        var g = new WarGrant
        {
            id = Guid.NewGuid().ToString("N"),
            marks = GrantArg(args, "marks"), gold = GrantArg(args, "gold"), wood = GrantArg(args, "wood"),
            stone = GrantArg(args, "stone"), food = GrantArg(args, "food"), iron = GrantArg(args, "iron"),
        };
        string text = Arg(args, "text").Trim();
        if (text.Length > 80) text = text.Substring(0, 80);
        g.text = text == "" ? "Подарок от администрации" : text;
        string msg;
        if (g.marks + g.gold + g.wood + g.stone + g.food + g.iron <= 0) msg = "Ничего не выписано: впишите, сколько дать";
        else
        {
            if (a.grants == null) a.grants = new List<WarGrant>();
            a.grants.Add(g);
            if (db.adminLog == null) db.adminLog = new List<string>();
            db.adminLog.Add($"{LocalTime(Now())} · {a.profile.displayName} ({a.profile.login}): {GrantText(g)}");
            if (db.adminLog.Count > AdminLogMax) db.adminLog.RemoveRange(0, db.adminLog.Count - AdminLogMax);
            Save();
            Debug.Log($"[Админ] {a.profile.displayName}: выписано {GrantText(g)}");
            msg = $"Выписано: {GrantText(g)}. " + (IsOnline(a.profile.id) ? "Придёт в игру в течение минуты." : "Придёт, когда игрок зайдёт в игру.");
        }
        return WebReply.Go($"{Api.WebAdmin}/player?key={key}&id={WebUtility.UrlEncode(a.profile.id)}&msg={WebUtility.UrlEncode(msg)}");
    }

    const int AdminGrantAllMax = 100000;

    WebReply AdminGrantAll(Dictionary<string, string> args)
    {
        if (!AdminOk(args)) return AdminDenied();
        string key = AdminKeyArg(args);
        int Lim(string k) => Math.Min(AdminGrantAllMax, GrantArg(args, k));
        int marks = Lim("marks"), gold = Lim("gold"), wood = Lim("wood"), stone = Lim("stone"), food = Lim("food"), iron = Lim("iron");
        string text = Arg(args, "text").Trim();
        if (text.Length > 80) text = text.Substring(0, 80);
        if (text == "") text = "Подарок от администрации";
        string msg;
        if (marks + gold + wood + stone + food + iron <= 0) msg = "Ничего не выписано: впишите, сколько дать";
        else
        {
            int n = 0;
            foreach (var a in db.accounts)
            {
                if (a == null || a.profile == null || a.npc || ReadCity(a.profile.id) == null) continue;   // NPC подарки не нужны
                if (a.grants == null) a.grants = new List<WarGrant>();
                a.grants.Add(new WarGrant { id = Guid.NewGuid().ToString("N"), marks = marks, gold = gold, wood = wood, stone = stone, food = food, iron = iron, text = text });
                n++;
            }
            var sample = new WarGrant { marks = marks, gold = gold, wood = wood, stone = stone, food = food, iron = iron };
            if (db.adminLog == null) db.adminLog = new List<string>();
            db.adminLog.Add($"{LocalTime(Now())} · ВСЕМ ({n}): {GrantText(sample)}");
            if (db.adminLog.Count > AdminLogMax) db.adminLog.RemoveRange(0, db.adminLog.Count - AdminLogMax);
            Save();
            Debug.Log($"[Админ] всем ({n}): выписано {GrantText(sample)}");
            msg = $"Выписано {n} игрокам: {GrantText(sample)}";
        }
        return WebReply.Go($"{Api.WebAdmin}/players?key={key}&msg={WebUtility.UrlEncode(msg)}");
    }

    /// <summary>Кнопки «Это мой аккаунт» / «Снять отметку». true - это была такая кнопка.</summary>
    bool AdminMarkMe(string what, StoredAccount t)
    {
        if (t == null || (what != "me" && what != "notme")) return false;
        db.adminAccountId = what == "me" ? t.profile.id : (db.adminAccountId == t.profile.id ? null : db.adminAccountId);
        Save();
        return true;
    }

    /// <summary>Что выписывал администратор (на главной странице модерации).</summary>
    string AdminLogHtml()
    {
        if (db.adminLog == null || db.adminLog.Count == 0) return "";
        var sb = new StringBuilder("<h2>Что выписано</h2><ul>");
        int n = 0;
        for (int i = db.adminLog.Count - 1; i >= 0 && n < 20; i--, n++) sb.Append($"<li class=\"muted\">{H(db.adminLog[i])}</li>");
        sb.Append("</ul>");
        return sb.ToString();
    }
}
