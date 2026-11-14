using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Город живёт и без игрока. Пока игрок в игре, раз в минуту меряем, насколько выросли запасы города
/// (казна, материалы Хранилищ, склады зданий) - это и есть доход: добыча минус то, что город тратит сам
/// (еда, мастерские). Минуты, в которые игрок сам тратил или получал (стройка, рынок, набеги, F9), не считаются.
/// Доход за последний час сохраняется с городом; при следующем входе город получает его за всё время отсутствия.
/// </summary>
public static class CityIncome
{
    const float Interval = 60f;          // как часто меряем, секунд
    const float Window = 3600f;          // доход считаем по последнему часу игры
    const float MinSeconds = 300f;       // меньше 5 минут замеров - не начисляем (слишком мало данных)
    const double MaxAwaySeconds = 14 * 86400.0;

    // Ключи: казна (без марок), потом материалы Хранилищ (вид * 3 + качество)
    static readonly string[] Treasury = { "gold", "wood", "stone", "food", "iron", "oats", "berries", "herbs", "potions", "parts", "leaves", "resin", "tar", "rubber", "slowPotions" };
    static int RefinedCount => System.Enum.GetValues(typeof(RefinedKind)).Length * 3;
    static int Size => Treasury.Length + RefinedCount;

    static float[] last;
    static float lastAt;
    static bool skip;
    static float[] sum;
    static float seconds;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { last = null; lastAt = 0f; skip = false; sum = null; seconds = 0f; modeRate = null; }

    /// <summary>Игрок сам потратил или получил что-то - эту минуту в доход не считаем.</summary>
    public static void Skip() { skip = true; CityStats.MarkPlayer(); }   // и замер вкладки «Экономика»

    /// <summary>
    /// Режим накопления включили или выключили: город теперь живёт по-другому. Доход прошлого часа (например,
    /// камень в минусе на мастерские) к новому режиму не подходит - меряем заново.
    /// </summary>
    public static void Restart() { last = null; sum = null; seconds = 0f; skip = false; CityStats.MarkPlayer(); }

    // ---------- Режим накопления: выработка добытчиков ----------
    // Если в режиме намерено меньше MinSeconds (включили и сразу вышли), за отсутствие начисляем по тому,
    // сколько добытчики дают в час. Считается при сохранении (тогда рабочие на местах) и лежит в сохранении города.

    static readonly int[] ModeRes = { 0, 1, 2, 4 };   // золото, дерево, камень, руда (индексы Treasury)
    static float[] modeRate;                           // из сохранения: в час, по ModeRes

    /// <summary>Выработка добытчиков в час (золото, дерево, камень, руда). Содержания в режиме нет, ночь не мешает.</summary>
    public static float[] ModeRateNow()
    {
        var r = new float[ModeRes.Length];
        var up = new float[4];
        GameClock clock = GameClock.Instance;
        foreach (var b in BuildingInstance.All)
        {
            if (b == null || b.type == null || b.IsRoad || !b.isProducing || b.Capacity <= 0) continue;
            if (!AccumulationMode.Works(b)) continue;
            // Сейчас ночь или день - неважно: берём среднее за неделю. В режиме накопления ночи и выходных нет (AverageShift = 1)
            float now = b.OffHoursFactor;
            float avg = clock != null ? clock.AverageShift(b.workers, b.level) : 1f;
            float perSec = b.AssignedEfficiency / Mathf.Max(0.05f, now) * avg * b.SpeedMultiplier * AccumulationMode.ProduceFactor;   // без игрока все на местах
            float eff = perSec * Mathf.Max(1, b.level) * 60f;
            r[0] += b.type.prodGold * eff * ResourceZones.BuildingMult(b, ZoneKind.Gold);
            r[1] += b.type.prodWood * eff * ResourceZones.BuildingMult(b, ZoneKind.Forest);
            r[2] += b.type.prodStone * eff * ResourceZones.BuildingMult(b, ZoneKind.Stone);
            if (b.ProducesIron) r[3] += BuildingInstance.IronPerSecond * perSec * 60f * ResourceZones.BuildingMult(b, ZoneKind.Iron);
            CityStats.UpkeepPerMin(b, up);
            r[0] -= up[0]; r[1] -= up[1]; r[2] -= up[2];
        }
        for (int i = 0; i < r.Length; i++) r[i] = Mathf.Max(0f, r[i] * 60f);
        return r;
    }

    public static string ModeRateText()
    {
        float[] r = ModeRateNow();
        return string.Join(",", System.Array.ConvertAll(r, SaveBag.F));
    }

    public static void LoadModeRate(string s)
    {
        modeRate = null;
        if (string.IsNullOrEmpty(s)) return;
        string[] a = s.Split(',');
        if (a.Length < ModeRes.Length) return;
        modeRate = new float[ModeRes.Length];
        for (int i = 0; i < modeRate.Length; i++) modeRate[i] = Mathf.Max(0f, SaveBag.ParseFloat(a[i], 0f));
    }

    /// <summary>Каждый кадр, пока город построен и игрок в онлайне.</summary>
    public static void Tick()
    {
        if (last != null && Time.unscaledTime - lastAt < Interval) return;
        float[] now = Totals();
        if (now == null) return;
        float dt = Time.unscaledTime - lastAt;
        if (sum == null) sum = new float[Size];
        if (last != null && !skip && dt > 1f && dt < Interval * 3f)
        {
            // Скользящее окно: старые замеры постепенно забываются
            float keep = seconds + dt > Window && seconds > 0f ? Mathf.Clamp01((Window - dt) / seconds) : 1f;
            for (int i = 0; i < sum.Length; i++) sum[i] = sum[i] * keep + (now[i] - last[i]);
            seconds = seconds * keep + dt;
        }
        last = now;
        lastAt = Time.unscaledTime;
        skip = false;
    }

    static float[] Totals()
    {
        var rm = ResourceManager.Instance;
        var st = TownHallStorage.Instance;
        if (rm == null || st == null) return null;
        var t = new float[Size];
        t[0] = rm.gold; t[1] = rm.wood; t[2] = rm.stone; t[3] = rm.food; t[4] = rm.iron; t[5] = rm.oats; t[6] = rm.berries;
        t[7] = rm.herbs; t[8] = rm.potions; t[9] = rm.parts; t[10] = rm.leaves; t[11] = rm.resin; t[12] = rm.tar; t[13] = rm.rubber; t[14] = rm.slowPotions;
        foreach (var b in BuildingInstance.All)
        {
            if (b == null) continue;
            t[0] += b.storedGold; t[1] += b.storedWood; t[2] += b.storedStone; t[3] += b.storedFood; t[4] += b.storedIron;
            t[5] += b.storedOats; t[6] += b.storedBerries; t[7] += b.storedHerbs; t[11] += b.storedResin; t[10] += b.storedLeaves;
        }
        int i = Treasury.Length;
        foreach (RefinedKind k in System.Enum.GetValues(typeof(RefinedKind)))
            for (int q = 0; q < 3; q++) t[i++] = st.Get(k, (QualityTier)q);
        return t;
    }

    /// <summary>Записать доход в час в сохранение города.</summary>
    public static void Capture(CityData c)
    {
        c.incomeSeconds = seconds;
        c.income = new List<float>();
        if (sum == null || seconds <= 0f) return;
        for (int i = 0; i < sum.Length; i++) c.income.Add(sum[i] / seconds * 3600f);
    }

    /// <summary>
    /// Город загружен: начислить доход за время, пока игрока не было, и продолжить замеры с сохранённого.
    /// Вернёт текст для игрока или null - начислять нечего.
    /// </summary>
    public static string Restore(CityData c, double awaySeconds)
    {
        float[] rate = modeRate;   // выработка из этого сохранения (CitySave.ApplyCity), нужна один раз
        ResetStatics();
        if (c == null) return null;
        bool measured = c.income != null && c.income.Count > 0;
        if (measured)
        {
            // Замеры продолжаются: сохранённый доход - как будто час игры
            seconds = Mathf.Min(c.incomeSeconds, Window);
            sum = new float[Size];
            for (int i = 0; i < sum.Length && i < c.income.Count; i++) sum[i] = c.income[i] / 3600f * seconds;
        }
        if (awaySeconds < 60.0) return null;
        bool enough = measured && c.incomeSeconds >= MinSeconds;
        double away = System.Math.Min(awaySeconds, MaxAwaySeconds);
        // Режим накопления, а замеров в нём мало: сырьё - по выработке добытчиков
        if (AccumulationMode.On && !enough && rate != null) return Credit(null, away, rate);
        if (!enough) return null;
        return Credit(c.income, away, null);
    }

    static string Credit(List<float> perHour, double away, float[] modeRates)
    {
        var rm = ResourceManager.Instance;
        var st = TownHallStorage.Instance;
        if (rm == null || st == null) return null;
        float hours = (float)(away / 3600.0);
        var got = new List<string>();
        var lost = new List<string>();
        int[] add = new int[Size];
        if (perHour != null)
            for (int i = 0; i < add.Length && i < perHour.Count; i++) add[i] = Mathf.FloorToInt(perHour[i] * hours);
        if (modeRates != null)
            for (int j = 0; j < ModeRes.Length && j < modeRates.Length; j++) add[ModeRes[j]] = Mathf.FloorToInt(modeRates[j] * hours);

        // Режим накопления: работали только добытчики - прибавляется сырьё, а не тратится ничего:
        // здания без содержания, жители не едят (обновление 22)
        if (AccumulationMode.On)
            for (int i = 0; i < add.Length; i++)
            {
                bool raw = i <= 7 || i == 10 || i == 11;   // золото, дерево, камень, еда, руда, овёс, ягоды, травы, листва, смола
                if (!raw || add[i] < 0) add[i] = 0;
            }

        rm.gold = Plus(rm.gold, add[0], "золота", got, lost); rm.wood = Plus(rm.wood, add[1], "дерева", got, lost);
        rm.stone = Plus(rm.stone, add[2], "камня", got, lost); rm.food = Plus(rm.food, add[3], "еды", got, lost);
        rm.iron = Plus(rm.iron, add[4], "руды", got, lost); rm.oats = Plus(rm.oats, add[5], "овса", got, lost);
        rm.berries = Plus(rm.berries, add[6], "ягод", got, lost); rm.herbs = Plus(rm.herbs, add[7], "трав", got, lost);
        rm.potions = Plus(rm.potions, add[8], "зелий", got, lost); rm.parts = Plus(rm.parts, add[9], "деталей", got, lost);
        rm.leaves = Plus(rm.leaves, add[10], "листвы", got, lost); rm.resin = Plus(rm.resin, add[11], "смолы", got, lost);
        rm.tar = Plus(rm.tar, add[12], "пропитки", got, lost); rm.rubber = Plus(rm.rubber, add[13], "резины", got, lost);
        rm.slowPotions = Plus(rm.slowPotions, add[14], "зелий замедления", got, lost);

        int i2 = Treasury.Length, made = 0;
        foreach (RefinedKind k in System.Enum.GetValues(typeof(RefinedKind)))
            for (int q = 0; q < 3; q++, i2++)
            {
                int n = add[i2];
                if (n > 0) { st.Add(k, (QualityTier)q, n); made += n; }
                else if (n < 0) st.TrySpend(k, (QualityTier)q, Mathf.Min(-n, Mathf.Min(AwayLossCap(st.Get(k, (QualityTier)q)), Mathf.FloorToInt(st.Get(k, (QualityTier)q) * MaxAwayLoss))));
            }
        if (made > 0) got.Add($"{made} материалов");
        last = null;   // запасы изменились не сами - следующий замер начнём заново
        if (got.Count == 0 && lost.Count == 0) return null;
        string text = $"Пока вас не было ({Dur(away)}), город работал";
        if (got.Count > 0) text += ": +" + string.Join(", +", got);
        if (lost.Count > 0) text += (got.Count > 0 ? "; " : ": ") + "потрачено " + string.Join(", ", lost);
        return text;
    }

    // Город сам перестаёт тратить, когда запас кончается: за время отсутствия уходит не больше половины запаса,
    // и ниже AwayKeep не тратится вовсе - вернувшись, игрок не сидит на нуле
    const float MaxAwayLoss = 0.5f;
    const int AwayKeep = 100;

    static int AwayLossCap(int have)
    {
        have = Mathf.Max(0, have);
        int keep = Mathf.Max(Mathf.CeilToInt(have * (1f - MaxAwayLoss)), Mathf.Min(have, AwayKeep));
        return Mathf.Max(0, have - keep);
    }

    static int Plus(int have, int add, string name, List<string> got, List<string> lost)
    {
        if (add > 0) got.Add($"{add} {name}");
        else if (add < 0)
        {
            int loss = Mathf.Min(-add, AwayLossCap(have));
            if (loss > 0) lost.Add($"{loss} {name}");
            return Mathf.Max(0, have - loss);
        }
        return Mathf.Max(0, have + add);
    }

    static string Dur(double s)
    {
        int m = (int)(s / 60.0), h = m / 60, d = h / 24;
        if (d > 0) return $"{d} д {h % 24} ч";
        if (h > 0) return $"{h} ч {m % 60} мин";
        return $"{m} мин";
    }
}
