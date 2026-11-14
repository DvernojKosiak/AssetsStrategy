using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Повтор набега, который прошёл без игрока (final-9). Сервер записывает снимки боя, пока нападающий воюет
/// у нашей деревни, и отдаёт запись по id отчёта (Api.BattleReplay, хранится неделю).
/// Окно рисует план деревни сверху: здания (серые - руины), бойцы нападающего красные, наши синие.
/// Пуск / пауза, скорость ×1…×8 и полоса времени, по которой можно перемотать.
/// Отчёты с записью запоминаются (PlayerPrefs), их видно в окне «Пока вас не было» и в системном меню.
/// </summary>
public class ReplayViewer : MonoBehaviour
{
    const int KeepOffers = 6;
    const string PrefKey = "replays.v1";

    public struct Offer { public string id, name, timeUtc; }
    static readonly List<Offer> offers = new List<Offer>();
    static readonly HashSet<string> fresh = new HashSet<string>();   // пришли в эту игру (их показывает «Пока вас не было»)
    static bool loaded;

    static ReplayViewer inst;
    static bool open, listOpen;
    BattleReplay rep;
    string loadingId, error, title;
    float time;           // секунд записи
    bool playing = true;
    int speed = 2;
    readonly Dictionary<int, ReplayName> names = new Dictionary<int, ReplayName>();
    Rect rect;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { offers.Clear(); fresh.Clear(); loaded = false; inst = null; open = listOpen = false; }

    public static bool IsOpen => (open || listOpen) && inst != null;
    public static IReadOnlyList<Offer> Offers { get { Load(); return offers; } }

    static string Key => PrefKey + "." + (AccountManager.Profile != null ? AccountManager.Profile.id : "");

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        offers.Clear();
        string s = PlayerPrefs.GetString(Key, "");
        foreach (var line in s.Split('\n'))
        {
            string[] p = line.Split('\t');
            if (p.Length >= 3 && p[0].Length == 32) offers.Add(new Offer { id = p[0], name = p[1], timeUtc = p[2] });
        }
    }

    static void Store()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var o in offers) sb.Append(o.id).Append('\t').Append((o.name ?? "").Replace('\t', ' ').Replace('\n', ' ')).Append('\t').Append(o.timeUtc).Append('\n');
        PlayerPrefs.SetString(Key, sb.ToString());
        PlayerPrefs.Save();
    }

    /// <summary>Пришёл отчёт о набеге: если у сервера есть запись - запоминаем её.</summary>
    public static void Note(RaidReport r)
    {
        if (r == null || !r.replay || string.IsNullOrEmpty(r.id)) return;
        Load();
        if (offers.Exists(o => o.id == r.id)) return;
        offers.Insert(0, new Offer { id = r.id, name = r.attackerName, timeUtc = r.timeUtc });
        if (offers.Count > KeepOffers) offers.RemoveRange(KeepOffers, offers.Count - KeepOffers);
        fresh.Add(r.id);
        Store();
    }

    /// <summary>Записи, пришедшие в эту игру (для окна «Пока вас не было»).</summary>
    public static List<Offer> Fresh()
    {
        Load();
        return offers.FindAll(o => fresh.Contains(o.id));
    }

    static void Ensure()
    {
        if (inst != null) return;
        var go = new GameObject("ReplayViewer");
        DontDestroyOnLoad(go);
        inst = go.AddComponent<ReplayViewer>();
    }

    // Обновление 13: плашка под щитом города - есть новые повторы набегов (раньше их не могли найти)
    static Rect chipRect;
    static int chipFrame = -10;
    public static bool ChipOver(Vector2 gp) { return Time.frameCount - chipFrame <= 2 && chipRect.Contains(gp); }

    /// <summary>Рисует плашку «▶ Повторы набегов», если есть записи. Возвращает нижний край (r.y - если плашки нет).</summary>
    public static float DrawChip(Rect r)
    {
        if (WarCityMode.Active || !AccountManager.LoggedIn) return r.y - 6f;
        int n = Offers.Count;
        if (n == 0) return r.y - 6f;
        int freshN = 0;
        foreach (var o in offers) if (fresh.Contains(o.id)) freshN++;
        chipRect = r;
        chipFrame = Time.frameCount;
        string label = freshN > 0 ? $"▶ Новые повторы набегов: {freshN}" : $"▶ Повторы набегов ({n})";
        if (UiKit.Button(r, label, freshN > 0 ? UiKit.Kind.Primary : UiKit.Kind.Normal, "Как на вас нападали, пока вас не было: полный повтор боя")) OpenList();
        return r.yMax;
    }

    public static void OpenList() { Load(); Ensure(); listOpen = true; open = false; }

    public static void Play(Offer o)
    {
        Ensure();
        listOpen = false;
        open = true;
        inst.Fetch(o);
    }

    void Fetch(Offer o)
    {
        rep = null; error = null; time = 0f; playing = true; names.Clear();
        if (stage != null) { stage.Destroy(); stage = null; }
        events.Clear();
        title = "Набег игрока " + o.name;
        loadingId = o.id;
        if (!AccountManager.LoggedIn || AccountManager.Backend == null) { error = "Нужно войти в аккаунт"; return; }
        AccountManager.Backend.Call(Api.BattleReplay, AccountManager.Token, new ApiRequest { raidId = o.id }, res =>
        {
            if (loadingId != o.id) return;
            loadingId = null;
            if (res == null || !res.ok || res.replay == null || res.replay.frames == null || res.replay.frames.Count == 0)
            {
                error = res != null && !string.IsNullOrEmpty(res.error) ? res.error : "Запись боя не пришла";
                if (res != null && res.ok == false && error.Contains("удалена")) Forget(o.id);
                return;
            }
            rep = res.replay;
            if (rep.step < 1) rep.step = 1;
            foreach (var n in rep.names) if (n != null) names[n.i] = n;
        });
    }

    static void Forget(string id)
    {
        offers.RemoveAll(x => x.id == id);
        fresh.Remove(id);
        Store();
    }

    float Length => rep == null || rep.frames.Count == 0 ? 0f : rep.frames[rep.frames.Count - 1].t;

    void Update()
    {
        if ((open || listOpen) && Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
        if (!open || rep == null || !playing) return;
        time += Time.unscaledDeltaTime * speed;
        if (time >= Length) { time = Length; playing = false; }
    }

    void Close()
    {
        open = false; listOpen = false; rep = null; loadingId = null;
        if (stage != null) { stage.Destroy(); stage = null; }
        events.Clear();
    }

    void OnDestroy() { if (stage != null) stage.Destroy(); stage = null; }

    void OnGUI()
    {
        if (!open && !listOpen) return;
        UiKit.Apply();
        GUI.depth = -40;
        if (listOpen)
        {
            // Затемнение: карта под окном не кликается
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(UiKit.FullRect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            DrawList();
        }
        else DrawReplay();
    }

    void DrawList()
    {
        float w = Mathf.Min(440f, UiKit.W - 24f);
        float h = Mathf.Min(UiKit.WindowH(Mathf.Max(offers.Count == 0 ? 2 : 1, offers.Count) * (UiKit.BtnH + 6f) + 30f), UiKit.H - 30f);
        rect = new Rect((UiKit.W - w) * 0.5f, (UiKit.H - h) * 0.5f, w, h);
        Rect c = UiKit.Window(rect, "Повторы набегов", out bool close);
        if (close) { Close(); return; }
        float y = c.y;
        LocGUI.Label(new Rect(c.x, y, c.width, 24f), UiKit.Col(UiKit.MutedHex, "Набеги, пока вас не было. Запись хранится неделю."), UiKit.Small);
        y += 28f;
        if (offers.Count == 0)
            LocGUI.Label(new Rect(c.x, y, c.width, 66f),
                "Записей пока нет. Повтор появляется, когда на вашу деревню нападают, пока вас нет в игре: после входа он будет здесь. " +
                UiKit.Col(UiKit.MutedHex, "Бой, который вы отбивали сами, не записывается — вы его видели."), UiKit.Small);
        foreach (var o in offers.ToArray())
        {
            if (UiKit.Button(new Rect(c.x, y, c.width, UiKit.BtnH), "▶ " + o.name + "  " + UiKit.Col(UiKit.MutedHex, When(o.timeUtc)), UiKit.Kind.Normal)) { Play(o); return; }
            y += UiKit.BtnH + 6f;
        }
    }

    static string When(string utc)
    {
        if (!System.DateTime.TryParse(utc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t)) return "";
        return t.ToLocalTime().ToString("dd.MM HH:mm");
    }

    // ---------- Полный повтор (обновление 12) ----------

    ReplayStage stage;
    bool scheme;                                   // поверх - маленькая схема всей деревни
    readonly List<string> events = new List<string>();
    Vector2 dragLast;
    bool dragging;

    void DrawReplay()
    {
        Rect full = UiKit.FullRect;
        Rect safe = new Rect(0f, 0f, UiKit.W, UiKit.H);
        float topH = 34f;
        float ctrlH = UiKit.BtnH + 34f + 40f;
        if (rep == null)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(full, Texture2D.whiteTexture);
            GUI.color = Color.white;
            float w0 = Mathf.Min(UiKit.W - 16f, 420f);
            rect = new Rect((UiKit.W - w0) * 0.5f, (UiKit.H - 150f) * 0.5f, w0, 150f);
            Rect c0 = UiKit.Window(rect, title ?? "Повтор набега", out bool close0);
            if (close0) { Close(); return; }
            string t = error != null ? UiKit.Col(UiKit.BadHex, error) : "Загружаем запись боя…";
            LocGUI.Label(new Rect(c0.x, c0.y + 10f, c0.width, 40f), t, UiKit.Center);
            if (UiKit.Button(new Rect(c0.x + (c0.width - 160f) * 0.5f, c0.yMax - UiKit.BtnH, 160f, UiKit.BtnH), "Закрыть", UiKit.Kind.Normal)) Close();
            return;
        }
        if (stage == null) { stage = new ReplayStage(rep, names); events.Clear(); }

        Rect view = new Rect(safe.x, safe.y + topH, safe.width, safe.height - topH - ctrlH);
        rect = full;
        stage.SetViewport(view);

        // Рамка вокруг сцены: сверху заголовок, снизу управление (сцену снимает своя камера, GUI её не закрывает)
        Color bg = new Color(0.09f, 0.09f, 0.1f, 1f);
        GUI.color = bg;
        GUI.DrawTexture(Rect.MinMaxRect(full.xMin, full.yMin, full.xMax, view.yMin), Texture2D.whiteTexture);
        GUI.DrawTexture(Rect.MinMaxRect(full.xMin, view.yMax, full.xMax, full.yMax), Texture2D.whiteTexture);
        GUI.DrawTexture(Rect.MinMaxRect(full.xMin, view.yMin, view.xMin, view.yMax), Texture2D.whiteTexture);
        GUI.DrawTexture(Rect.MinMaxRect(view.xMax, view.yMin, full.xMax, view.yMax), Texture2D.whiteTexture);
        GUI.color = Color.white;

        int fi = FrameAt(time, out float k);
        var info = stage.Show(fi, k, time, events);
        if (events.Count > 6) events.RemoveRange(0, events.Count - 6);
        stage.DrawBars(view, fi);

        // Карту тянут пальцем / мышью, колесо - приблизить
        Event e = Event.current;
        if (e.type == EventType.MouseDown && view.Contains(e.mousePosition)) { dragging = true; dragLast = e.mousePosition; e.Use(); }
        else if (e.type == EventType.MouseDrag && dragging) { stage.Pan(e.mousePosition - dragLast, view); dragLast = e.mousePosition; e.Use(); }
        else if (e.type == EventType.MouseUp) dragging = false;
        else if (e.type == EventType.ScrollWheel && view.Contains(e.mousePosition)) { stage.Zoom(e.delta.y > 0f ? 1.12f : 0.89f); e.Use(); }

        // Заголовок и счёт
        GUIStyle head = new GUIStyle(UiKit.Body) { fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip };
        LocGUI.Label(new Rect(8f, 6f, safe.width * 0.45f, 24f), "▶ " + (title ?? "Повтор набега"), head);
        string score = UiKit.Col("#f26a5f", $"● нападающие {info.attackers}") + "   " + UiKit.Col("#6aa8ff", $"● ваши {info.defenders}") + "   " + UiKit.Col(UiKit.MutedHex, $"руин {info.ruined}");
        GUIStyle sc = new GUIStyle(UiKit.Small) { alignment = TextAnchor.MiddleRight, wordWrap = false };
        LocGUI.Label(new Rect(safe.width * 0.45f, 6f, safe.width * 0.55f - 50f, 24f), score, sc);
        if (UiKit.Button(new Rect(safe.width - 42f, 3f, 38f, 28f), "✕", UiKit.Kind.Ghost, "Закрыть повтор")) { Close(); return; }

        // Что случилось (последние события) - в углу сцены
        if (events.Count > 0)
        {
            GUIStyle ev = new GUIStyle(UiKit.Small) { wordWrap = false, clipping = TextClipping.Clip };
            float eh = events.Count * 17f + 6f;
            Rect er = new Rect(view.x + 6f, view.y + 6f, Mathf.Min(300f, view.width - 12f), eh);
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(er, Texture2D.whiteTexture);
            GUI.color = Color.white;
            for (int i = 0; i < events.Count; i++) LocGUI.Label(new Rect(er.x + 6f, er.y + 3f + i * 17f, er.width - 8f, 17f), events[i], ev);
        }

        // Приблизить / отдалить / следить
        float zb = UiKit.Phone ? 40f : 30f;
        float zx = view.xMax - zb - 8f, zy = view.y + 8f;
        if (UiKit.Button(new Rect(zx, zy, zb, zb), "+", UiKit.Kind.Normal, "Приблизить")) stage.Zoom(0.8f);
        if (UiKit.Button(new Rect(zx, zy + zb + 4f, zb, zb), "−", UiKit.Kind.Normal, "Отдалить")) stage.Zoom(1.25f);
        if (UiKit.Button(new Rect(zx - 120f, zy, 116f, zb), stage.Follow ? "✓ Следить" : "Следить", stage.Follow ? UiKit.Kind.Primary : UiKit.Kind.Normal,
                "Камера идёт за нападающими. Потяните карту - и смотрите сами")) stage.Follow = !stage.Follow;
        if (UiKit.Button(new Rect(zx - 120f, zy + zb + 4f, 116f, zb), scheme ? "✓ Схема" : "Схема", scheme ? UiKit.Kind.Primary : UiKit.Kind.Normal, "Маленькая схема всей деревни")) scheme = !scheme;
        if (scheme)
        {
            float ms = Mathf.Min(180f, view.height * 0.4f);
            DrawMap(new Rect(view.x + 6f, view.yMax - ms - 6f, ms, ms));
        }

        // Полоса времени
        float cy = view.yMax + 8f;
        float len = Mathf.Max(1f, Length);
        Rect bar = new Rect(10f, cy, safe.width - 20f, 18f);
        GUI.color = new Color(1f, 1f, 1f, 0.15f);
        GUI.DrawTexture(bar, Texture2D.whiteTexture);
        GUI.color = UiKit.Accent;
        GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(time / len), bar.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
        LocGUI.Label(bar, $"{Clock(time)} / {Clock(len)}", UiKit.Center);
        if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && new Rect(bar.x, bar.y - 10f, bar.width, bar.height + 20f).Contains(e.mousePosition))
        {
            time = Mathf.Clamp01((e.mousePosition.x - bar.x) / bar.width) * len;
            e.Use();
        }
        cy += 26f;

        float bw = (safe.width - 20f - 3 * 6f) / 4f;
        if (UiKit.Button(new Rect(10f, cy, bw, UiKit.BtnH), playing ? "❚❚ Пауза" : "▶ Пуск", UiKit.Kind.Primary))
        {
            if (!playing && time >= Length) time = 0f;
            playing = !playing;
        }
        if (UiKit.Button(new Rect(10f + (bw + 6f), cy, bw, UiKit.BtnH), "Скорость ×" + speed, UiKit.Kind.Normal)) speed = speed >= 8 ? 1 : speed * 2;
        if (UiKit.Button(new Rect(10f + 2 * (bw + 6f), cy, bw, UiKit.BtnH), "⟲ Сначала", UiKit.Kind.Normal)) { time = 0f; playing = true; events.Clear(); }
        if (UiKit.Button(new Rect(10f + 3 * (bw + 6f), cy, bw, UiKit.BtnH), "Закрыть", UiKit.Kind.Ghost)) { Close(); return; }
        if (!rep.real)
            LocGUI.Label(new Rect(10f, cy + UiKit.BtnH + 4f, safe.width - 20f, 18f),
                UiKit.Col(UiKit.MutedHex, "Запись старой версии игры: деревня показана по плану набега, без выстрелов."), UiKit.Small);
    }

    static string Clock(float s) { int i = Mathf.FloorToInt(s); return $"{i / 60}:{i % 60:00}"; }

    // Кадр, после которого стоит время (и доля до следующего)
    int FrameAt(float t, out float k)
    {
        var fr = rep.frames;
        int lo = 0, hi = fr.Count - 1;
        while (lo < hi) { int mid = (lo + hi + 1) / 2; if (fr[mid].t <= t) lo = mid; else hi = mid - 1; }
        k = 0f;
        if (lo + 1 < fr.Count)
        {
            float span = fr[lo + 1].t - fr[lo].t;
            if (span > 0f) k = Mathf.Clamp01((t - fr[lo].t) / span);
        }
        return lo;
    }

    void DrawMap(Rect map)
    {
        GUI.color = new Color(0.22f, 0.30f, 0.18f, 1f);
        GUI.DrawTexture(map, Texture2D.whiteTexture);
        int size = Mathf.Max(8, rep.size);
        float cell = map.width / size;
        float half = size * 0.5f;
        System.Func<float, float, Vector2> P = (x, y) => new Vector2(map.x + (x + half) * cell, map.yMax - (y + half) * cell);

        int fi = FrameAt(time, out float k);
        ReplayFrame f = rep.frames[fi];
        ReplayFrame g = fi + 1 < rep.frames.Count ? rep.frames[fi + 1] : f;

        for (int i = 0; i < rep.buildings.Count; i++)
        {
            if (i >= f.b.Count) continue;   // здания ещё не было (заслон поставили позже)
            var b = rep.buildings[i];
            int hp = f.b[i];
            Vector2 tl = P(b.x, b.y + b.h);
            Rect r = new Rect(tl.x + 0.5f, tl.y + 0.5f, b.w * cell - 1f, b.h * cell - 1f);
            GUI.color = hp < 0 ? new Color(0.35f, 0.33f, 0.31f, 0.9f) : new Color(0.72f, 0.60f, 0.42f, 1f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            if (hp >= 0 && hp < 100)
            {
                GUI.color = Color.Lerp(UiKit.Bad, UiKit.Good, hp / 100f);
                GUI.DrawTexture(new Rect(r.x, r.yMax - 3f, r.width * hp / 100f, 3f), Texture2D.whiteTexture);
            }
        }

        var next = new Dictionary<int, ReplayUnit>();
        foreach (var u in g.u) next[u.i] = u;
        float dot = Mathf.Clamp(cell * 0.9f, 4f, 12f);
        foreach (var u in f.u)
        {
            float x = u.x / 10f, y = u.y / 10f;
            if (next.TryGetValue(u.i, out ReplayUnit n)) { x = Mathf.Lerp(x, n.x / 10f, k); y = Mathf.Lerp(y, n.y / 10f, k); }
            names.TryGetValue(u.i, out ReplayName nm);
            bool att = nm != null && nm.a;
            Vector2 p = P(x, y);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(p.x - dot * 0.5f - 1f, p.y - dot * 0.5f - 1f, dot + 2f, dot + 2f), Texture2D.whiteTexture);
            GUI.color = att ? new Color(0.95f, 0.30f, 0.25f) : new Color(0.35f, 0.62f, 1f);
            if (nm != null && nm.r) GUI.color = Color.Lerp(GUI.color, Color.white, 0.3f);
            GUI.DrawTexture(new Rect(p.x - dot * 0.5f, p.y - dot * 0.5f, dot, dot), Texture2D.whiteTexture);
            if (u.h < 100)
            {
                GUI.color = Color.Lerp(UiKit.Bad, UiKit.Good, u.h / 100f);
                GUI.DrawTexture(new Rect(p.x - dot * 0.5f, p.y - dot * 0.5f - 3f, dot * u.h / 100f, 2f), Texture2D.whiteTexture);
            }
        }
        GUI.color = Color.white;

        // Счёт: сколько бойцов у каждой стороны в этом кадре
        int a = 0, d = 0;
        foreach (var u in f.u) { if (names.TryGetValue(u.i, out ReplayName nm) && nm.a) a++; else d++; }
        int ruined = 0;
        foreach (int hp in f.b) if (hp < 0) ruined++;
        string info = UiKit.Col("#f26a5f", $"● нападающие {a}") + "   " + UiKit.Col("#6aa8ff", $"● ваши {d}") + "   " + UiKit.Col(UiKit.MutedHex, $"руин {ruined}");
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(map.x, map.y, map.width, 22f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        LocGUI.Label(new Rect(map.x + 6f, map.y + 2f, map.width - 12f, 20f), info, UiKit.Small);
    }
}
