using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Статистика города: сколько город добывает, сколько тратит и чего ему не хватает.
///  - Вкладка «Экономика» в меню «Город» (GameMenu): по золоту, дереву, камню и еде - добыча зданий,
///    содержание зданий, прочие траты (жители едят, мастерские, кухни, обучение) и итог в минуту;
///    на сколько хватит запаса; жители и жильё; список проблем.
///  - Под панелью каждого здания - плашка «Экономика здания»: что оно даёт и тратит в минуту и не
///    кончается ли в городе то, что ему нужно.
///  - Над стройками на карте - сколько осталось строить.
/// Добыча и содержание считаются по формулам зданий (как сейчас), «прочее» - по тому, как на самом деле
/// меняются запасы за последние 2 минуты (минуты, когда игрок сам что-то покупал или строил, не считаются).
/// Все числа - в минуту игры (на скорости x2 минута игры идёт вдвое быстрее).
/// </summary>
public class CityStats : MonoBehaviour
{
    public static CityStats Instance { get; private set; }

    const int GOLD = 0, WOOD = 1, STONE = 2, FOOD = 3;
    static readonly string[] Names = { "Золото", "Дерево", "Камень", "Еда" };
    static readonly string[] Units = { "золота", "дерева", "камня", "еды" };
    static readonly string[] Hex = { UiKit.GoldHex, UiKit.WoodHex, UiKit.StoneHex, UiKit.FoodHex };
    static readonly StoreRes[] Res = { StoreRes.Gold, StoreRes.Wood, StoreRes.Stone, StoreRes.Food };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance == null) new GameObject("CityStats").AddComponent<CityStats>();
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start() { GameMenu.AddPage("Экономика", PageHeight, DrawPage); }

    // ---------- Замеры запасов ----------

    const float SampleEvery = 5f;
    const int Samples = 24;                       // 24 × 5 с = 2 минуты
    static bool playerActed;
    readonly Queue<float[]> deltas = new Queue<float[]>();   // [золото, дерево, камень, еда, секунд]
    float[] last;
    float sampleTimer;

    /// <summary>Игрок сам что-то потратил или получил (CityIncome.Skip) - этот отрезок в замер не идёт.</summary>
    public static void MarkPlayer() { playerActed = true; }

    void Update()
    {
        sampleTimer += Time.deltaTime;
        if (sampleTimer < SampleEvery) return;
        float dt = sampleTimer;
        sampleTimer = 0f;

        float[] now = Totals();
        if (now == null) { last = null; deltas.Clear(); return; }
        if (last != null && !playerActed)
        {
            deltas.Enqueue(new[] { now[0] - last[0], now[1] - last[1], now[2] - last[2], now[3] - last[3], dt });
            while (deltas.Count > Samples) deltas.Dequeue();
        }
        playerActed = false;
        last = now;
    }

    static float[] Totals()
    {
        var rm = ResourceManager.Instance;
        if (rm == null || BuildingPlacer.Instance == null || !BuildingPlacer.Instance.WorldReady || WarCityMode.Active) return null;
        float[] t = { rm.gold, rm.wood, rm.stone, rm.food };
        foreach (var b in BuildingInstance.All)
        {
            if (b == null) continue;
            t[0] += b.storedGold; t[1] += b.storedWood; t[2] += b.storedStone; t[3] += b.storedFood;
        }
        return t;
    }

    /// <summary>Как на самом деле менялись запасы, в минуту. false - замеров ещё мало (меньше 30 с).</summary>
    bool Measured(float[] perMin)
    {
        float sec = 0f;
        for (int r = 0; r < 4; r++) perMin[r] = 0f;
        foreach (var d in deltas) { for (int r = 0; r < 4; r++) perMin[r] += d[r]; sec += d[4]; }
        if (sec < 30f) return false;
        for (int r = 0; r < 4; r++) perMin[r] = perMin[r] / sec * 60f;
        return true;
    }

    // ---------- Формулы зданий ----------

    /// <summary>Сколько здание добывает сейчас, в минуту (золото, дерево, камень, еда).</summary>
    public static void ProducePerMin(BuildingInstance b, float[] outRes)
    {
        for (int r = 0; r < 4; r++) outRes[r] = 0f;
        if (b == null || b.type == null || b.IsRoad || !b.isProducing || b.Capacity <= 0 || b.IsStorageFull) return;
        float eff = b.Efficiency * b.SpeedMultiplier * AccumulationMode.ProduceFactor * b.level * 60f;
        outRes[GOLD] = b.type.prodGold * eff * ResourceZones.BuildingMult(b, ZoneKind.Gold);
        outRes[WOOD] = b.type.prodWood * eff * ResourceZones.BuildingMult(b, ZoneKind.Forest);
        outRes[STONE] = b.type.prodStone * eff * ResourceZones.BuildingMult(b, ZoneKind.Stone);
        outRes[FOOD] = b.type.prodFood * eff * ResourceZones.BuildingMult(b, ZoneKind.Fertile);
    }

    /// <summary>Сколько здание просит на содержание, в минуту (CityConsumption). 0 - не ест (не работает, на паузе).</summary>
    public static void UpkeepPerMin(BuildingInstance b, float[] outRes)
    {
        for (int r = 0; r < 4; r++) outRes[r] = 0f;
        var cc = CityConsumption.Instance;
        if (b == null || b.type == null || cc == null || !b.IsConsumer || b.IsRuined || !b.isStaffed) return;
        if (AccumulationMode.On || b.IsStorageFull) return;   // режим накопления: содержания нет ни у кого (обновление 22)
        Balance.UpkeepOf(b, cc.goldPerBuilding, cc.woodPerBuilding, cc.stonePerBuilding, out int g, out int w, out int s);
        float k = 60f / Mathf.Max(1f, cc.tickInterval);
        outRes[GOLD] = g * k; outRes[WOOD] = w * k; outRes[STONE] = s * k;
    }

    // ---------- Сводка по городу ----------

    class Summary
    {
        public float[] prod = new float[4], upkeep = new float[4], paid = new float[4], real = new float[4], other = new float[4];
        public bool measured;
        public int unfed, unstaffed, full, building, upgrading, offZone;
        public int[] lackBy = new int[4];
    }

    Summary cache;
    int cacheFrame = -100;

    Summary Get()
    {
        if (cache != null && Time.frameCount - cacheFrame < 15) return cache;
        var s = new Summary();
        float[] p = new float[4], u = new float[4];
        foreach (var b in BuildingInstance.All)
        {
            if (b == null || b.type == null) continue;
            if (b.UnderConstruction) { s.building++; continue; }
            if (b.Upgrading) s.upgrading++;
            ProducePerMin(b, p);
            UpkeepPerMin(b, u);
            for (int r = 0; r < 4; r++)
            {
                s.prod[r] += p[r];
                s.upkeep[r] += u[r];
                if (b.isFed) s.paid[r] += u[r];
                if (!b.isFed && (b.missingMask & (1 << r)) != 0) s.lackBy[r]++;
            }
            if (b.IsRuined || AccumulationMode.Paused(b)) continue;
            if (!b.isFed) s.unfed++;
            if (!b.isStaffed) s.unstaffed++;
            if (b.IsStorageFull) s.full++;
            if (!b.InGreenZone) s.offZone++;
        }
        s.measured = Measured(s.real);
        // Прочее = что на самом деле изменилось - добыча + оплаченное содержание (жители едят, мастерские, обучение...)
        for (int r = 0; r < 4; r++) s.other[r] = s.measured ? s.real[r] - s.prod[r] + s.paid[r] : 0f;
        cache = s;
        cacheFrame = Time.frameCount;
        return s;
    }

    /// <summary>Итог в минуту по ресурсу (замер; пока замеров нет - добыча минус содержание).</summary>
    public static float NetPerMin(int r)
    {
        if (Instance == null) return 0f;
        var s = Instance.Get();
        return s.measured ? s.real[r] : s.prod[r] - s.paid[r];
    }

    static int Stock(int r)
    {
        var rm = ResourceManager.Instance;
        return rm != null ? rm.Get(Res[r]) : 0;
    }

    /// <summary>«хватит на 6 мин» / «склад полон через 12 мин» / «» - всё ровно.</summary>
    static string Forecast(int r, float net)
    {
        int have = Stock(r);
        if (net < -0.5f) return have <= 0 ? UiKit.Col(UiKit.BadHex, "кончилось!") : UiKit.Col(have / -net < 10f ? UiKit.BadHex : UiKit.WarnHex, "хватит на " + Balance.Dur(have / -net * 60f));
        if (net > 0.5f && StorageLimits.Active)
        {
            int cap = StorageLimits.Cap(Res[r]);
            if (have >= cap) return UiKit.Col(UiKit.WarnHex, "склад полон");
            return UiKit.Col(UiKit.MutedHex, "полон через " + Balance.Dur((cap - have) / net * 60f));
        }
        return "";
    }

    static string Signed(float v)
    {
        int n = Mathf.RoundToInt(v);
        if (n == 0) return UiKit.Col(UiKit.MutedHex, "0");
        return n > 0 ? UiKit.Col(UiKit.GoodHex, "+" + n) : UiKit.Col(UiKit.BadHex, n.ToString());
    }

    // ---------- Вкладка «Экономика» ----------

    const float LineH = 18f;

    List<string> Problems(Summary s)
    {
        var l = new List<string>();
        for (int r = 0; r < 4; r++)
            if (s.lackBy[r] > 0) l.Add(UiKit.Col(UiKit.BadHex, $"Не хватает {Units[r]}: {s.lackBy[r]} зд. на {Mathf.RoundToInt(BuildingInstance.StarvedEfficiency * 100f)}%"));
        if (s.unstaffed > 0) l.Add(UiKit.Col(UiKit.WarnHex, $"Нет рабочих: {s.unstaffed} зд. — нужны дома"));
        if (s.full > 0) l.Add(UiKit.Col(UiKit.WarnHex, $"Полный склад: {s.full} зд. — «Собрать всё»"));
        if (s.offZone > 0) l.Add(UiKit.Col(UiKit.WarnHex, $"Вне зелёной зоны: {s.offZone} зд."));
        var pm = PopulationManager.Instance;
        if (pm != null && pm.HousingCapacity > 0 && pm.Population - pm.Mercenaries >= pm.HousingCapacity) l.Add(UiKit.Col(UiKit.WarnHex, "Нет мест в домах — город не растёт"));
        for (int r = 0; r < 4; r++)
        {
            float net = s.measured ? s.real[r] : s.prod[r] - s.paid[r];
            int have = Stock(r);
            if (net < -0.5f && have / -net < 10f) l.Add(UiKit.Col(UiKit.BadHex, $"{Names[r]} кончится через {Balance.Dur(have / -net * 60f)}"));
        }
        if (s.building > 0 || s.upgrading > 0) l.Add(UiKit.Col(UiKit.InfoHex, $"Строится: {s.building} · улучшается: {s.upgrading}"));
        if (l.Count == 0) l.Add(UiKit.Col(UiKit.GoodHex, "Всё в порядке"));
        return l;
    }

    // Карточка ресурса: имя и итог за минуту, полоса запаса, полоса «приход / расход»
    const float CardH = 62f, CardGap = 6f, ProbH = 20f;

    float PageHeight(float w)
    {
        var s = Get();
        int probs = Mathf.Min(5, Problems(s).Count);
        return 4 * (CardH + CardGap) + 44f + UiKit.SectionH + probs * ProbH + 4f;
    }

    static void Fill(Rect r, Color c)
    {
        Color old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    static Color HexColor(string hex)
    {
        return ColorUtility.TryParseHtmlString(hex, out Color c) ? c : Color.white;
    }

    void DrawCard(Rect r, Summary s, int res)
    {
        float net = s.measured ? s.real[res] : s.prod[res] - s.paid[res];
        // Приход и расход: добыча (+ «прочее», если оно в плюс) против содержания (+ «прочее», если в минус)
        float other = s.measured ? s.other[res] : 0f;
        float income = s.prod[res] + Mathf.Max(0f, other);
        float spend = s.upkeep[res] + Mathf.Max(0f, -other);
        Color rc = HexColor(Hex[res]);

        Fill(r, new Color(1f, 1f, 1f, 0.04f));
        Fill(new Rect(r.x, r.y, 3f, r.height), rc);   // цветная кромка ресурса
        float x = r.x + 10f, w = r.width - 16f, y = r.y + 5f;

        // Строка 1: название слева, итог крупно справа
        GUIStyle name = new GUIStyle(UiKit.Body) { fontStyle = FontStyle.Bold, wordWrap = false };
        GUIStyle big = new GUIStyle(UiKit.Body) { fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperRight, wordWrap = false };
        LocGUI.Label(new Rect(x, y, w * 0.5f, 20f), UiKit.Col(Hex[res], Names[res]), name);
        int n = Mathf.RoundToInt(net);
        string netText = n == 0 ? UiKit.Col(UiKit.MutedHex, "±0 / мин") : UiKit.Col(n > 0 ? UiKit.GoodHex : UiKit.BadHex, (n > 0 ? "▲ +" : "▼ ") + n + " / мин");
        LocGUI.Label(new Rect(x + w * 0.4f, y, w * 0.6f, 20f), netText, big);
        y += 21f;

        // Строка 2: сколько лежит (полоса до лимита) и прогноз
        int have = Stock(res);
        int cap = StorageLimits.Active ? StorageLimits.Cap(Res[res]) : 0;
        float frac = cap > 0 ? Mathf.Clamp01(have / (float)cap) : 1f;
        Rect stockBar = new Rect(x, y, w, 14f);
        UiKit.Bar(stockBar, frac, new Color(rc.r, rc.g, rc.b, 0.75f), cap > 0 ? $"{have} / {cap}" : have.ToString());
        y += 17f;

        // Строка 3: тонкая полоса «приход (зелёный) / расход (красный)» и прогноз словами
        float total = income + spend;
        Rect flow = new Rect(x, y + 4f, w * 0.45f, 6f);
        Fill(flow, new Color(0f, 0f, 0f, 0.5f));
        if (total > 0.5f)
        {
            float g = flow.width * income / total;
            Fill(new Rect(flow.x, flow.y, g, flow.height), UiKit.Good);
            Fill(new Rect(flow.x + g, flow.y, flow.width - g, flow.height), UiKit.Bad);
        }
        GUIStyle sm = new GUIStyle(UiKit.Small) { wordWrap = false, clipping = TextClipping.Clip };
        string fc = Forecast(res, net);
        if (fc == "") fc = UiKit.Col(UiKit.MutedHex, total > 0.5f ? "ровно" : "не добывают и не тратят");
        LocGUI.Label(new Rect(x + w * 0.48f, y - 2f, w * 0.52f, 16f), fc, sm);

        // Подробности - в подсказке на всю карточку
        string tip = $"<b>{Names[res]}, в минуту</b>\n" +
                     $"Добывают здания: +{Mathf.RoundToInt(s.prod[res])}\n" +
                     $"Содержание зданий: −{Mathf.RoundToInt(s.upkeep[res])}\n" +
                     (s.measured ? $"Прочее (жители едят, мастерские, обучение): {Mathf.RoundToInt(other):+0;−0;0}\n" : "Прочее: замер ещё идёт\n") +
                     $"Итого: {n:+0;−0;0}\n" +
                     UiKit.Col(UiKit.MutedHex, "Зелёное на полоске — приход, красное — расход. Итог — как на самом деле меняется запас за последние 2 минуты (без ваших покупок и строек).");
        UiKit.Tip(r, tip);
    }

    void DrawPage(Rect area)
    {
        var s = Get();
        float x = area.x, w = area.width, y = area.y;

        int[] order = { FOOD, WOOD, STONE, GOLD };
        foreach (int res in order)
        {
            DrawCard(new Rect(x, y, w, CardH), s, res);
            y += CardH + CardGap;
        }

        // Жители: полоса «живёт / мест в домах» и строка «работают · солдаты · свободны»
        var pm = PopulationManager.Instance;
        if (pm != null)
        {
            int housed = Mathf.Max(0, pm.Population - pm.Mercenaries);
            int cap = Mathf.Max(1, pm.HousingCapacity);
            Rect bar = new Rect(x, y, w, 16f);
            UiKit.Bar(bar, housed / (float)cap, housed >= pm.HousingCapacity ? UiKit.Warn : UiKit.Info, $"Жители {housed} / {pm.HousingCapacity} мест");
            UiKit.Tip(bar, "Сколько жителей и сколько мест в Жилых домах. Места кончились — город не растёт.\nЖители едят еду из Цитадели: больше жителей — больше еды.");
            GUIStyle sm = new GUIStyle(UiKit.Small) { wordWrap = false, alignment = TextAnchor.MiddleCenter };
            LocGUI.Label(new Rect(x, y + 19f, w, 18f),
                $"⚒ работают {pm.Employed}   ⚔ солдат {pm.Soldiers}   " + UiKit.Col(pm.FreePeople > 0 ? UiKit.GoodHex : UiKit.MutedHex, $"☺ свободны {pm.FreePeople}"), sm);
        }
        y += 44f;

        // Что не так - короткие строки с цветной точкой
        y = UiKit.Section(x, y, w, "Что не так");
        var probs = Problems(s);
        GUIStyle pst = new GUIStyle(UiKit.Small) { wordWrap = false, clipping = TextClipping.Clip };
        for (int i = 0; i < probs.Count && i < 5; i++)
        {
            LocGUI.Label(new Rect(x, y, w, ProbH), "● " + probs[i], pst);
            y += ProbH;
        }
    }

    // ---------- Плашка под панелью здания ----------

    static readonly float[] tmpP = new float[4], tmpU = new float[4];

    static List<string> BuildingLines(BuildingInstance b)
    {
        var l = new List<string>();
        if (b == null || b.type == null || b.IsRoad || b.UnderConstruction) return l;

        ProducePerMin(b, tmpP);
        var prod = new List<string>();
        for (int r = 0; r < 4; r++) if (tmpP[r] >= 0.5f) prod.Add(UiKit.Col(Hex[r], $"+{Mathf.RoundToInt(tmpP[r])} {Units[r]}"));
        if (b.isProducing && b.ProducesIron) prod.Add(UiKit.Col(UiKit.MutedHex, $"+{BuildingInstance.IronPerSecond * b.Efficiency * b.SpeedMultiplier * 60f:0} руды"));
        if (prod.Count > 0) l.Add("Даёт в минуту: " + string.Join(", ", prod));
        else if (b.type.prodGold + b.type.prodWood + b.type.prodStone + b.type.prodFood > 0)
            l.Add(UiKit.Col(UiKit.WarnHex, b.IsStorageFull ? "Сейчас не добывает: склад здания полон — нужны курьеры" : "Сейчас не добывает: нет рабочих или здание не работает"));

        // Содержание - по таблице, даже если здание сейчас не ест (чтобы было видно, что ему нужно)
        var cc = CityConsumption.Instance;
        if (cc != null && b.IsConsumer)
        {
            Balance.UpkeepOf(b, cc.goldPerBuilding, cc.woodPerBuilding, cc.stonePerBuilding, out int g, out int w, out int s);
            float k = 60f / Mathf.Max(1f, cc.tickInterval);
            int[] need = { g, w, s, 0 };
            var up = new List<string>();
            for (int r = 0; r < 3; r++) if (need[r] > 0) up.Add(UiKit.Col(Hex[r], $"−{Mathf.RoundToInt(need[r] * k)} {Units[r]}"));
            if (up.Count > 0) l.Add("Содержание в минуту: " + string.Join(", ", up));

            // Что из нужного этому зданию в городе кончается
            for (int r = 0; r < 3; r++)
            {
                if (need[r] <= 0) continue;
                float net = NetPerMin(r);
                int have = Stock(r);
                if (!b.isFed && (b.missingMask & (1 << r)) != 0)
                    l.Add(UiKit.Col(UiKit.BadHex, $"Городу не хватает {Units[r]} — здание работает на {Mathf.RoundToInt(BuildingInstance.StarvedEfficiency * 100f)}%"));
                else if (net < -0.5f && have / -net < 10f)
                    l.Add(UiKit.Col(UiKit.WarnHex, $"{Names[r]} в городе кончится через {Balance.Dur(have / -net * 60f)} ({Mathf.RoundToInt(net)}/мин)"));
            }
        }

        // Рабочие едят еду - если её мало, предупредить у любого здания с рабочими и у домов
        bool eaters = b.workersNeeded > 0 || b.type.name == PopulationManager.HouseName;
        if (eaters)
        {
            float net = NetPerMin(FOOD);
            int have = Stock(FOOD);
            if (have <= 0) l.Add(UiKit.Col(UiKit.BadHex, "В городе нет еды — жители голодают и работают хуже"));
            else if (net < -0.5f && have / -net < 10f) l.Add(UiKit.Col(UiKit.WarnHex, $"Еда в городе кончится через {Balance.Dur(have / -net * 60f)} — нужны фермы"));
        }
        return l;
    }

    public static float BuildingBoxHeight(BuildingInstance b, float w)
    {
        int n = BuildingLines(b).Count;
        return n == 0 ? 0f : 8f + LineH + n * LineH;
    }

    public static void DrawBuildingBox(Rect r, BuildingInstance b)
    {
        var lines = BuildingLines(b);
        if (lines.Count == 0) return;
        LocGUI.Box(r, GUIContent.none);
        GUIStyle st = new GUIStyle(UiKit.Small) { wordWrap = false, clipping = TextClipping.Clip };
        float y = r.y + 4f;
        Rect hr = new Rect(r.x + 8f, y, r.width - 16f, LineH);
        LocGUI.Label(hr, UiKit.B("Экономика здания"), st);
        UiKit.Tip(hr, "Что здание даёт и тратит. Весь город — меню «Город» → вкладка «Экономика».");
        y += LineH;
        foreach (var line in lines)
        {
            LocGUI.Label(new Rect(r.x + 8f, y, r.width - 16f, LineH), line, st);
            y += LineH;
        }
    }

    // ---------- Таймеры над стройками на карте ----------

    void OnGUI()
    {
        if (WarView.IsOpen || RaidManager.ViewingBattle) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        UiKit.Apply();
        GUIStyle st = new GUIStyle(UiKit.Small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false };
        if (Event.current.type != EventType.Repaint) return;   // final-13: рисуем только при отрисовке
        float ppu = UiKit.PointsPerUnit(cam);
        foreach (var b in BuildingInstance.All)
        {
            if (b == null || b.type == null || b.TimerLeft <= 0f || b.IsWall || b.IsRoad) continue;
            Vector3 sp = cam.WorldToScreenPoint(b.Center);
            if (sp.z < 0f || sp.x < -50f || sp.x > Screen.width + 50f || sp.y < -50f || sp.y > Screen.height + 50f) continue;
            // final-13: таймер - скруглённая плашка со значком молотка (стройка) или стрелки (улучшение),
            // а не чёрный прямоугольник с текстом. Карта сильно отдалена - таймеры не загромождают экран
            if (ppu < 7f) continue;
            string t = Balance.Dur(b.TimerLeft);
            float tw = st.CalcSize(new GUIContent(t)).x;
            Vector2 g = UiKit.ToGui(sp);
            Rect rc = new Rect(g.x - (tw + 30f) / 2f, g.y - 11f, tw + 30f, 22f);
            if (TouchControls.UiCovers(rc)) continue;
            GameUi.Pill(rc, b.UnderConstruction ? UiKit.Accent : UiKit.Info, 0.92f);
            if (!GameUi.Icon(new Rect(rc.x + 4f, rc.y + 2f, 18f, 18f), b.UnderConstruction ? "ui_hammer" : "ui_star"))
                LocGUI.Label(new Rect(rc.x + 2f, rc.y, 20f, rc.height), b.UnderConstruction ? "⚒" : "▲", st);
            st.normal.textColor = b.UnderConstruction ? new Color(1f, 0.9f, 0.62f) : new Color(0.75f, 0.9f, 1f);
            GUI.Label(new Rect(rc.x + 22f, rc.y, rc.width - 26f, rc.height), t, st);
        }
    }
}
