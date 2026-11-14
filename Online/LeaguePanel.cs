using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Вкладка «Лиги» в окне клана: ваш рейтинг и лига, место в мире, рейтинг клана,
/// мировая таблица кланов и игроков (можно смотреть одну лигу) и последние изменения рейтинга.
/// Рейтинг игрока - за набеги на игроков и клановые войны, клана - за клановые войны.
/// </summary>
public class LeaguePanel
{
    LeagueBoard board;
    bool loading;
    float timer;
    string msg = "";
    int list;             // 0 - кланы, 1 - игроки
    int filter = -1;      // -1 - все лиги
    int histTab;          // 0 - мой рейтинг, 1 - клана
    Vector2 tableScroll, histScroll;

    public void Update(bool visible)
    {
        if (!visible) return;
        timer -= Time.unscaledDeltaTime;
        if (timer > 0f || loading || !AccountManager.LoggedIn || string.IsNullOrEmpty(AccountManager.Token)) return;
        Load();
    }

    /// <summary>Открыли вкладку - обновить сразу.</summary>
    public void Refresh() { timer = 0f; }

    void Load()
    {
        timer = 15f;
        loading = true;
        int asked = filter;
        AccountManager.Backend.Clan(Api.LeagueTop, AccountManager.Token, new ApiRequest { league = asked }, res =>
        {
            loading = false;
            if (!res.ok || res.league == null)
            {
                msg = UiKit.Col(UiKit.BadHex, string.IsNullOrEmpty(res.error) ? "Нет связи с сервером" : res.error);
                timer = 4f;
                return;
            }
            msg = "";
            if (asked != filter) { timer = 0f; return; }   // пока ждали, выбрали другую лигу
            board = res.league;
            AccountManager.NoteRating(board.myRating, null);
        });
    }

    public void Draw(Rect r)
    {
        if (board == null)
        {
            LocGUI.Label(new Rect(r.x, r.y, r.width, 22f), string.IsNullOrEmpty(msg) ? UiKit.Col(UiKit.MutedHex, "Загрузка…") : msg, UiKit.Body);
            return;
        }
        float lw = Mathf.Min(270f, r.width * 0.38f);
        DrawMine(new Rect(r.x, r.y, lw, r.height - 22f));
        DrawTable(new Rect(r.x + lw + 14f, r.y, r.width - lw - 14f, r.height - 22f));
        LocGUI.Label(new Rect(r.x, r.yMax - 20f, r.width, 20f), msg, UiKit.Small);
    }

    // ---------- Слева: мой рейтинг, рейтинг клана, история ----------

    void DrawMine(Rect r)
    {
        float y = UiKit.Section(r.x, r.y, r.width, "Ваш рейтинг");
        y = DrawRating(r.x, y, r.width, board.myRating, board.myPlace, board.totalPlayers, board.myBest, true);

        if (!string.IsNullOrEmpty(board.clanName))
        {
            y = UiKit.Section(r.x, y + 4f, r.width, "Клан «" + Loc.Keep(board.clanName) + "»");
            y = DrawRating(r.x, y, r.width, board.clanRating, board.clanPlace, board.totalClans, board.clanBest, false);
        }
        else
        {
            LocGUI.Label(new Rect(r.x, y, r.width, 34f), UiKit.Col(UiKit.MutedHex, "Вступите в клан, чтобы он тоже попал в мировую таблицу."), UiKit.Small);
            y += 38f;
        }

        // История изменений
        bool clan = !string.IsNullOrEmpty(board.clanName);
        if (clan)
        {
            int nt = UiKit.Tabs(new Rect(r.x, y, r.width, 24f), new[] { "Мои очки", "Очки клана" }, histTab);
            if (nt != histTab) { histTab = nt; histScroll = Vector2.zero; }
            y += 28f;
        }
        else
        {
            histTab = 0;
            y = UiKit.Section(r.x, y, r.width, "Последние изменения");
        }
        List<RatingChange> hist = histTab == 1 ? board.clanHistory : board.history;
        Rect view = new Rect(r.x, y, r.width, r.yMax - y);
        if (hist == null || hist.Count == 0)
        {
            string empty = histTab == 1 ? "Клан получает рейтинг за клановые войны." : "Рейтинг дают за набеги на игроков и клановые войны.";
            LocGUI.Label(view, UiKit.Col(UiKit.MutedHex, empty), UiKit.Small);
            return;
        }
        float inner = r.width - 16f;
        var texts = new List<string>(hist.Count);
        var heights = new List<float>(hist.Count);
        float total = 0f;
        for (int i = hist.Count - 1; i >= 0; i--)   // новые сверху
        {
            RatingChange h = hist[i];
            if (h == null) continue;
            string d = h.delta > 0 ? UiKit.Col(UiKit.GoodHex, "+" + h.delta) : h.delta < 0 ? UiKit.Col(UiKit.BadHex, h.delta.ToString()) : UiKit.Col(UiKit.MutedHex, "0");
            string t = $"<b>{d}</b> {Loc.Keep(h.text)} {UiKit.Col(UiKit.MutedHex, "→ " + h.rating)}";
            float th = UiKit.TextH(t, inner, UiKit.Small) + 2f;
            texts.Add(t);
            heights.Add(th);
            total += th;
        }
        histScroll = UiKit.BeginScrollView(view, histScroll, new Rect(0f, 0f, inner, Mathf.Max(total, view.height)));
        float yy = 0f;
        for (int i = 0; i < texts.Count; i++)
        {
            LocGUI.Label(new Rect(0f, yy, inner, heights[i]), texts[i], UiKit.Small);
            yy += heights[i];
        }
        GUI.EndScrollView();
    }

    // final-13-fix-3: карточка рейтинга: значок, крупное число, лига цветной таблеткой, полоска до следующей лиги, место в мире
    float DrawRating(float x, float y, float w, int rating, int place, int total, int best, bool player)
    {
        int lg = Leagues.Of(rating);
        Color lc = ColorOf(lg);
        int next = Leagues.Next(lg);
        Rect card = new Rect(x, y, w, 100f);
        GameUi.Card(card, lc, 1f, 0.15f, true);
        GameUi.Icon(new Rect(x + 12f, y + 10f, 36f, 36f), player ? "ui_star" : "ui_crown");
        var big = new GUIStyle(UiKit.Body) { fontSize = 24, fontStyle = FontStyle.Bold, wordWrap = false, alignment = TextAnchor.MiddleLeft };
        big.normal.textColor = new Color(1f, 0.92f, 0.70f);
        GUI.Label(new Rect(x + 56f, y + 8f, 90f, 32f), rating.ToString(), big);
        GameUi.LeagueBadge(new Rect(x + 56f + Mathf.Min(90f, big.CalcSize(new GUIContent(rating.ToString())).x + 10f), y + 12f, Mathf.Min(150f, w - 160f), 24f), Leagues.Label(lg), lc);
        float by = y + 50f;
        if (next > 0)
        {
            int from = Leagues.From[lg];
            float frac = (rating - from) / (float)Mathf.Max(1, next - from);
            Rect br = new Rect(x + 12f, by, w - 24f, 16f);
            UiKit.Bar(br, frac, lc, $"до лиги «{Leagues.Name(lg + 1)}»: {next - rating}");
            if (lg + 1 > best)
            {
                string reward = RewardText(lg + 1, player ? 1f : 0.5f);
                UiKit.Tip(br, (player ? "Награда за первый выход в эту лигу: " : "Каждому участнику, когда клан первый раз дойдёт до этой лиги: ") + reward);
            }
        }
        else LocGUI.Label(new Rect(x + 12f, by, w - 24f, 18f), UiKit.Col(UiKit.AccentHex, "<b>Высшая лига!</b>"), UiKit.Small);
        string where = place > 0 ? $"Место в мире: <b>{place}</b> из {total}" : "Место в мире: —";
        GameUi.IconLine(new Rect(x + 12f, y + 72f, w - 24f, 20f), "ui_flag", UiKit.Col(UiKit.MutedHex, where), new GUIStyle(UiKit.Small) { wordWrap = false, richText = true });
        return card.yMax + 8f;
    }

    static string RewardText(int league, float k)
    {
        var parts = new List<string>();
        int g = (int)(Leagues.RewardGold(league) * k), res = (int)(Leagues.RewardRes(league) * k), iron = (int)(Leagues.RewardIron(league) * k);
        if (g > 0) parts.Add($"{g} золота");
        if (res > 0) parts.Add($"по {res} дерева, камня и еды");
        if (iron > 0) parts.Add($"{iron} железа");
        return string.Join(", ", parts);
    }

    static Color ColorOf(int league)
    {
        return ColorUtility.TryParseHtmlString(Leagues.Hex(league), out Color c) ? c : UiKit.Accent;
    }

    // ---------- Справа: мировая таблица ----------

    void DrawTable(Rect r)
    {
        float y = r.y;
        int nl = UiKit.Tabs(new Rect(r.x, y, Mathf.Min(240f, r.width * 0.5f), UiKit.BtnH), new[] { "Кланы", "Игроки" }, list);
        if (nl != list) { list = nl; tableScroll = Vector2.zero; }

        // Выбор лиги: ◀ Все лиги ▶
        float fx = r.xMax - 220f;
        if (UiKit.Button(new Rect(fx, y, 30f, UiKit.BtnH), "◀", !loading, UiKit.Kind.Normal, "Лига ниже")) SetFilter(filter <= -1 ? Leagues.Count - 1 : filter - 1);
        string fl = filter < 0 ? "Все лиги" : Leagues.Label(filter);
        LocGUI.Label(new Rect(fx + 32f, y, 156f, UiKit.BtnH), fl, new GUIStyle(UiKit.Body) { alignment = TextAnchor.MiddleCenter, wordWrap = false });
        if (UiKit.Button(new Rect(fx + 190f, y, 30f, UiKit.BtnH), "▶", !loading, UiKit.Kind.Normal, "Лига выше")) SetFilter(filter >= Leagues.Count - 1 ? -1 : filter + 1);
        y += UiKit.BtnH + 6f;

        List<LeagueRow> rows = list == 0 ? board.clans : board.players;
        int total = list == 0 ? board.totalClans : board.totalPlayers;
        string head = list == 0 ? $"Лучшие кланы мира (всего {total})" : $"Лучшие игроки мира (всего {total})";
        LocGUI.Label(new Rect(r.x, y, r.width, 18f), UiKit.Col(UiKit.MutedHex, head + (loading ? "  …" : "")), UiKit.Small);
        y += 20f;

        // Шапка
        float w = r.width - 16f;
        float cPlace = 0f, cName = 44f, cLeague = w - 200f, cRating = w - 60f;
        GUIStyle hs = new GUIStyle(UiKit.Small) { wordWrap = false };
        if (Event.current.type == EventType.Repaint) { Color hc = GUI.color; GUI.color = new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.10f); GUI.DrawTexture(new Rect(r.x, y - 1f, w, 20f), Texture2D.whiteTexture); GUI.color = hc; }
        LocGUI.Label(new Rect(r.x + cPlace, y, 40f, 18f), UiKit.Col(UiKit.MutedHex, "№"), hs);
        LocGUI.Label(new Rect(r.x + cName, y, cLeague - cName, 18f), UiKit.Col(UiKit.MutedHex, list == 0 ? "Клан" : "Игрок"), hs);
        LocGUI.Label(new Rect(r.x + cLeague, y, 140f, 18f), UiKit.Col(UiKit.MutedHex, "Лига"), hs);
        LocGUI.Label(new Rect(r.x + cRating, y, 60f, 18f), UiKit.Col(UiKit.MutedHex, "Рейтинг"), hs);
        y += 20f;

        LeagueRow mine = rows != null ? rows.Find(x => x != null && x.me) : null;
        int myPlace = list == 0 ? board.clanPlace : board.myPlace;
        bool pin = myPlace > 0 && mine == null && (list == 1 || !string.IsNullOrEmpty(board.clanName)) && (filter < 0 || filter == Leagues.Of(list == 0 ? board.clanRating : board.myRating));
        const float rowH = 24f;
        Rect view = new Rect(r.x, y, r.width, r.yMax - y - (pin ? rowH + 4f : 0f));
        if (rows == null || rows.Count == 0)
        {
            LocGUI.Label(view, UiKit.Col(UiKit.MutedHex, filter >= 0 ? "В этой лиге пока никого нет." : "Пока никого нет."), UiKit.Body);
        }
        else
        {
            tableScroll = UiKit.BeginScrollView(view, tableScroll, new Rect(0f, 0f, w, rows.Count * rowH));
            for (int i = 0; i < rows.Count; i++) DrawRow(new Rect(0f, i * rowH, w, rowH), rows[i], cName, cLeague, cRating, i % 2 == 1);
            GUI.EndScrollView();
        }
        // Меня нет в первой сотне - моя строка внизу
        if (pin)
        {
            var me = new LeagueRow
            {
                place = myPlace, me = true,
                name = list == 0 ? board.clanName : AccountManager.Profile != null ? AccountManager.Profile.displayName : "Вы",
                rating = list == 0 ? board.clanRating : board.myRating,
            };
            DrawRow(new Rect(r.x, r.yMax - rowH, w, rowH), me, cName, cLeague, cRating, false);
        }
    }

    void SetFilter(int f)
    {
        filter = f;
        tableScroll = Vector2.zero;
        timer = 0f;
    }

    void DrawRow(Rect lr, LeagueRow row, float cName, float cLeague, float cRating, bool odd)
    {
        if (row == null) return;
        Color oc = GUI.color;
        if (row.me) { GUI.color = new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.20f); GUI.DrawTexture(lr, Texture2D.whiteTexture); }
        else if (odd) { GUI.color = new Color(1f, 1f, 1f, 0.04f); GUI.DrawTexture(lr, Texture2D.whiteTexture); }
        GUI.color = oc;

        GUIStyle line = new GUIStyle(UiKit.Body) { wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft };
        string place = row.place <= 3 ? UiKit.Col(row.place == 1 ? "#FFD54A" : row.place == 2 ? "#D7DCE2" : "#D08A4E", $"<b>{row.place}</b>") : row.place.ToString();
        if (row.place <= 3)
        {
            // final-13-fix-3: первые три места - корона цвета медали
            Color mc = row.place == 1 ? new Color(1f, 0.84f, 0.29f) : row.place == 2 ? new Color(0.85f, 0.87f, 0.9f) : new Color(0.82f, 0.54f, 0.31f);
            Color o2 = GUI.color; GUI.color = mc;
            GameUi.Icon(new Rect(lr.x + 2f, lr.y + 3f, 18f, 18f), "ui_crown");
            GUI.color = o2;
            LocGUI.Label(new Rect(lr.x + 22f, lr.y, cName - 24f, lr.height), place, line);
        }
        else LocGUI.Label(new Rect(lr.x + 4f, lr.y, cName - 6f, lr.height), place, line);

        string name = row.me ? $"<b>{Loc.Keep(row.name)}</b>" : Loc.Keep(row.name);
        string extra = list == 0 ? (row.members > 0 ? UiKit.Col(UiKit.MutedHex, $"  {row.members}/{Api.MaxClanMembers}") : "")
                                 : (!string.IsNullOrEmpty(row.clan) ? UiKit.Col(UiKit.MutedHex, "  «" + Loc.Keep(row.clan) + "»") : "")
                                   + (row.hall > 0 ? UiKit.Col(UiKit.MutedHex, "  ур. " + row.hall) : "");
        LocGUI.Label(new Rect(lr.x + cName, lr.y, cLeague - cName - 6f, lr.height), name + extra, line);
        int rl = Leagues.Of(row.rating);
        GameUi.LeagueBadge(new Rect(lr.x + cLeague, lr.y + 2f, Mathf.Min(150f, cRating - cLeague - 8f), lr.height - 4f), Leagues.Label(rl), ColorOf(rl));
        LocGUI.Label(new Rect(lr.x + cRating, lr.y, lr.width - cRating, lr.height), $"<b>{row.rating}</b>", line);
    }
}
