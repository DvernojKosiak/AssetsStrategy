using System;
using System.Collections.Generic;

// ---------- Клановая война: общие правила и обмен игры с сервером ----------
// Этот файл компилируется и в Unity, и в программе сервера, поэтому здесь только данные и чистый C#.
//
// Клановая война (klan-voina-1) - отдельная игра на общей онлайн-карте. Из домашней деревни на войну переносится
// только расстановка зданий (снимок в первый день войны). Всё остальное - своё: ресурсы, бойцы, найм, улучшения,
// ремонт. Всё считает сервер (WarSim), даже когда игроков нет в игре; игра только показывает карту и шлёт приказы.
// Обратно в домашнюю деревню после войны приходит только рейтинг клана.
//
// Карта квадратная (WarGeo): слева земли первого клана (города сеткой), справа - второго, посередине и по краям -
// нейтральные земли с реками, лесами, озёрами и источниками ресурсов.

public enum WarStage { None, Searching, Planning, Prepare, Neutral, Open, Finished }

/// <summary>
/// Форма карты войны (в участках по Plot клеток): столбцы городов у клана, ряды городов, ширина нейтральной полосы
/// и кайма. Одна и та же у сервера и игры: строится из полей WarInfo / WarStatic (gc, rows, nc, mx, my).
/// </summary>
public class WarGeo
{
    public readonly int gc;          // столбцов городов у клана
    public readonly int cityRows;    // рядов городов
    public readonly int neutral;     // нейтральных столбцов посередине
    public readonly int mx, my;      // кайма слева-справа и сверху-снизу (участков)
    public readonly int Cols, Rows, W, H;
    public int SideCols => WarRules.CityPlots * gc;
    public int Slots => gc * cityRows;

    public WarGeo(int gc, int cityRows, int neutral, int mx, int my)
    {
        this.gc = Math.Max(1, gc);
        this.cityRows = Math.Max(1, cityRows);
        this.neutral = Math.Max(2, neutral);
        this.mx = Math.Max(1, mx);
        this.my = Math.Max(1, my);
        Cols = this.mx * 2 + SideCols * 2 + this.neutral;
        Rows = this.my * 2 + WarRules.CityPlots * this.cityRows;
        W = Cols * WarRules.Plot;
        H = Rows * WarRules.Plot;
    }

    /// <summary>Из полей сервера: rows - всего рядов участков; nc, mx, my = 0 - старая война (4 и 2).</summary>
    public static WarGeo Of(int rows, int gc, int nc, int mx, int my)
    {
        int m = my > 0 ? my : 2;
        int cr = Math.Max(1, (rows - m * 2) / WarRules.CityPlots);
        return new WarGeo(gc, cr, nc > 0 ? nc : 4, mx > 0 ? mx : 2, m);
    }

    /// <summary>
    /// Квадратная карта под столько игроков в большем клане: перебираем, сколько столбцов городов поставить, и берём
    /// самую маленькую сторону; недостающее до квадрата отдаём нейтральным землям (середине или кайме сверху и снизу).
    /// </summary>
    public static WarGeo Square(int maxMembers)
    {
        int n = Math.Max(1, maxMembers);
        WarGeo best = null;
        int bestSide = int.MaxValue;
        for (int g = 1; g <= 4; g++)
        {
            int r = Math.Max(2, (n + g - 1) / g);
            int cols = 2 * 2 + 2 * WarRules.CityPlots * g + 4;
            int rows = 2 * 2 + WarRules.CityPlots * r;
            int side = Math.Max(cols, rows);
            if (side < bestSide || (side == bestSide && g * r - n < best.gc * best.cityRows - n))
            {
                bestSide = side;
                int neutral = 4 + (side - cols);
                int extra = side - rows;
                best = new WarGeo(g, r, neutral, 2, 2 + extra / 2);
                // Нечётный остаток - ещё один нейтральный ряд сверху не делаем: карта на участок ниже, это не страшно
            }
        }
        return best;
    }

    /// <summary>Чей участок: 1 - первый клан, 2 - второй, 0 - нейтральный.</summary>
    public int ZoneOfPlot(int px, int py)
    {
        if (py < my || py >= Rows - my) return 0;
        if (px >= mx && px < mx + SideCols) return 1;
        int b = mx + SideCols + neutral;
        if (px >= b && px < b + SideCols) return 2;
        return 0;
    }

    public int ZoneOfCell(int x, int y)
    {
        return ZoneOfPlot(Math.Max(0, Math.Min(Cols - 1, x / WarRules.Plot)), Math.Max(0, Math.Min(Rows - 1, y / WarRules.Plot)));
    }

    /// <summary>Номер места в схеме → столбец участка на карте. Место 0 - ближе всего к центру карты.</summary>
    public int SlotPlotCol(int side, int slot)
    {
        int c = Math.Max(0, slot) % gc;   // 0 - столбец у нейтральных земель
        return side == 1 ? mx + (gc - 1 - c) * WarRules.CityPlots : mx + SideCols + neutral + c * WarRules.CityPlots;
    }

    /// <summary>Место в схеме - ряд городов снизу вверх (в ряду gc мест).</summary>
    public int SlotPlotRow(int slot) { return my + Math.Max(0, slot) / gc * WarRules.CityPlots; }

    /// <summary>Первый и последний+1 нейтральный столбец посередине (клетки).</summary>
    public int MidX0 => (mx + SideCols) * WarRules.Plot;
    public int MidX1 => (mx + SideCols + neutral) * WarRules.Plot;
}

public static class WarRules
{
    public const int Version = 3;          // klan-voina-2: живые деревни на общей квадратной карте
    public const int Plot = 50;            // участок: столько клеток захватывают разом
    public const int CitySize = 200;       // город игрока (как карта города в игре): CityPlots x CityPlots участков
    public const int CityPlots = CitySize / Plot;
    public const int Days = 7;
    public const double DaySeconds = 86400.0;

    public const float Tick = 0.2f;               // шаг расчёта на сервере, секунд
    public const int MaxUnitsPerCity = 40;
    public const float UnitVision = 8f, BuildingVision = 6f, TowerVision = 10f;
    /// <summary>klan-voina-14: насколько близко (клеток от края казармы) боец должен подойти, чтобы зайти в неё.</summary>
    public const float BarracksReach = 4f;
    public const float CaptureSeconds = 60f;      // столько стоять на участке без врагов, чтобы он стал твоим
    public const float AggroRange = 7f;           // на таком расстоянии бойцы сами замечают врага
    public const float TrainMul = 4f;             // найм на войне медленнее, чем дома
    public const int StartGold = 500, StartFood = 500, StartWood = 400, StartStone = 300, StartIron = 60;   // запас живой деревни в начале войны
    public const float FastIncomeMul = 10f;       // в быстрой войне доход больше
    public const int Squads = 6;                  // отрядов у города (1..6; 0 - без отряда)
    public static readonly string[] SquadDefault = { "Пехота", "Стрелки", "Конница", "Маги", "Осада", "Особые" };

    /// <summary>В какой отряд сам встаёт боец, вышедший из казармы: по роду войск.</summary>
    public static int SquadOf(WarUnitDef d)
    {
        if (d == null) return 6;
        if (d.engineer || d.ram || d.bldMul >= 2f) return 5;
        if (d.mounted) return 3;
        if (d.art == "mage") return 4;
        if (d.range > 3f) return 2;
        if (d.vision > 0f) return 6;
        return 1;
    }

    // ---------- Улучшения (на всю войну, для всех бойцов города) ----------
    public const int MaxResearch = 3;
    public const float ResearchStep = 0.12f;      // +12% урона (оружие) или здоровья (броня) за уровень
    public const int ResWeapon = 0, ResArmor = 1;

    /// <summary>Цена улучшения на следующий уровень: золото, дерево, камень, железо.</summary>
    public static void ResearchCost(int kind, int nextLevel, out int gold, out int wood, out int stone, out int iron)
    {
        int L = Math.Max(1, nextLevel);
        gold = 150 * L; wood = 0; stone = 0; iron = 0;
        if (kind == ResWeapon) { iron = 30 * L; wood = 40 * L; }
        else { stone = 60 * L; iron = 20 * L; }
    }

    public static string ResearchName(int kind) { return kind == ResWeapon ? "Оружие" : "Броня"; }

    // ---------- Ремонт ----------
    public const float RepairPerSecond = 0.04f;   // доля здоровья здания за секунду
    public const float RepairSafeRange = 12f;     // враг ближе - ремонт стоит

    /// <summary>Цена ремонта: дерево и камень за недостающее здоровье (разрушенное - в полтора раза дороже).</summary>
    public static void RepairCost(float missingHp, bool destroyed, out int wood, out int stone)
    {
        float k = destroyed ? 1.5f : 1f;
        wood = (int)Math.Ceiling(missingHp / 8f * k);
        stone = (int)Math.Ceiling(missingHp / 16f * k);
    }

    /// <summary>Стадия войны по прошедшему времени.</summary>
    public static WarStage StageOf(double elapsed, double daySeconds)
    {
        int day = (int)Math.Floor(elapsed / daySeconds);
        if (day <= 0) return WarStage.Planning;
        if (day == 1) return WarStage.Prepare;
        if (day <= 3) return WarStage.Neutral;
        if (day < Days) return WarStage.Open;
        return WarStage.Finished;
    }

    /// <summary>Когда (в секундах от начала) начнётся следующая стадия.</summary>
    public static double NextStageAt(WarStage s, double daySeconds)
    {
        switch (s)
        {
            case WarStage.Planning: return daySeconds;
            case WarStage.Prepare: return 2 * daySeconds;
            case WarStage.Neutral: return 4 * daySeconds;
            case WarStage.Open: return Days * daySeconds;
            default: return Days * daySeconds;
        }
    }

    public static string StageName(WarStage s)
    {
        switch (s)
        {
            case WarStage.Searching: return "ищем соперника";
            case WarStage.Planning: return "день 1: расстановка городов";
            case WarStage.Prepare: return "день 2: подготовка на своей земле";
            case WarStage.Neutral: return "дни 3–4: нейтральные земли открыты";
            case WarStage.Open: return "дни 5–7: открыта вся карта";
            case WarStage.Finished: return "война окончена";
            default: return "нет войны";
        }
    }

    /// <summary>Коротко - для верхней полосы на телефоне.</summary>
    public static string StageShort(WarStage s)
    {
        switch (s)
        {
            case WarStage.Planning: return "День 1: расстановка";
            case WarStage.Prepare: return "День 2: своя земля";
            case WarStage.Neutral: return "Дни 3–4: нейтральные земли";
            case WarStage.Open: return "Дни 5–7: вся карта";
            case WarStage.Finished: return "Война окончена";
            default: return "";
        }
    }

    /// <summary>Может ли сторона side сейчас заходить в зону zone.</summary>
    public static bool CanEnter(int side, int zone, WarStage st)
    {
        if (zone == side) return st >= WarStage.Prepare && st < WarStage.Finished;
        if (zone == 0) return st >= WarStage.Neutral && st < WarStage.Finished;
        return st == WarStage.Open;
    }

    /// <summary>Центр города на карте (клетки): в игре это начало координат города.</summary>
    public static float CityCenterX(int plotX) { return plotX * Plot + CitySize / 2f; }
    public static float CityCenterY(int plotY) { return plotY * Plot + CitySize / 2f; }

    /// <summary>Участок (px, py) - часть города с началом в (plotX, plotY).</summary>
    public static bool PlotInCity(int px, int py, int plotX, int plotY) { return px >= plotX && px < plotX + CityPlots && py >= plotY && py < plotY + CityPlots; }

    // ---------- Местность (одинаково у сервера и игры: строится из seed) ----------

    // Grove - светлый лес: по нему ходят (медленнее). Shallow - река: переходят вброд, медленно (как дома).
    // Forest - глухая чаща и Water - озеро: не пройти.
    public const byte Grass = 0, Forest = 1, Water = 2, Ford = 3, Field = 4, Grove = 5, Shallow = 6;

    public static bool Passable(byte t) { return t != Forest && t != Water; }

    public static bool IsWet(byte t) { return t == Water || t == Shallow || t == Ford; }

    /// <summary>Во сколько раз медленнее идут по этой местности (река вброд, лес).</summary>
    public static float SpeedMul(byte t, bool mounted)
    {
        if (t == Shallow) return mounted ? 0.6f : 0.35f;
        if (t == Grove) return 0.8f;
        return 1f;
    }

    /// <summary>
    /// Местность всей карты и на участках городов - местность самих городов (реки, лес), как у игрока дома.
    /// Игра и сервер считают одинаково: города и их местность приходят в WarStatic.
    /// </summary>
    public static byte[] Terrain(int seed, WarGeo g, List<WarCityView> cities)
    {
        byte[] t = Terrain(seed, g);
        if (cities == null) return t;
        int w = g.W, h = g.H;
        foreach (var c in cities)
        {
            if (c == null || string.IsNullOrEmpty(c.terrain) || c.mapSize <= 0) continue;
            int ox = c.plotX * Plot + CitySize / 2, oy = c.plotY * Plot + CitySize / 2;
            int x0 = c.plotX * Plot, y0 = c.plotY * Plot;
            TerrainCode.Decode(c.mapSize, c.terrain, (x, y, ch) =>
            {
                int gx = ox + x, gy = oy + y;
                if (gx < x0 || gy < y0 || gx >= x0 + CitySize || gy >= y0 + CitySize || gx >= w || gy >= h) return;
                t[gy * w + gx] = ch == TerrainCode.Water ? Shallow : ch == TerrainCode.Forest ? Grove : ch == BridgeCode ? Ford : Grass;
            });
        }
        // Реки и леса городов не обрываются на краю: текут и растут дальше по нейтральным землям
        foreach (var c in cities) if (c != null && !string.IsNullOrEmpty(c.terrain)) ExtendFromCity(t, g, c, seed);
        return t;
    }

    static void ExtendFromCity(byte[] t, WarGeo g, WarCityView c, int seed)
    {
        int w = g.W, h = g.H;
        int x0 = c.plotX * Plot, y0 = c.plotY * Plot, x1 = x0 + CitySize - 1, y1 = y0 + CitySize - 1;
        var rng = new Random(seed * 31 + c.plotX * 977 + c.plotY * 131);
        for (int side = 0; side < 4; side++)
        {
            int nx = side == 0 ? -1 : side == 1 ? 1 : 0, ny = side == 2 ? -1 : side == 3 ? 1 : 0;
            int run = 0, kind = 0;
            for (int k = 0; k <= CitySize; k++)
            {
                int ex = side == 0 ? x0 : side == 1 ? x1 : x0 + k, ey = side == 2 ? y0 : side == 3 ? y1 : y0 + k;
                int now = 0;
                if (k < CitySize && ex >= 0 && ey >= 0 && ex < w && ey < h)
                {
                    byte b = t[ey * w + ex];
                    now = b == Shallow ? 1 : b == Grove ? 2 : 0;
                }
                if (now == kind && now != 0) { run++; continue; }
                if (kind != 0 && run >= 2)
                {
                    int mid = k - run / 2 - 1;
                    int sx = side == 0 ? x0 : side == 1 ? x1 : x0 + mid, sy = side == 2 ? y0 : side == 3 ? y1 : y0 + mid;
                    int ox = sx + nx, oy = sy + ny;
                    if (ox >= 0 && oy >= 0 && ox < w && oy < h && g.ZoneOfCell(ox, oy) == 0)
                    {
                        if (kind == 1) GrowRiver(t, g, sx, sy, nx, ny, Math.Min(6, run), rng);
                        else GrowGrove(t, g, sx, sy, nx, ny, run, rng);
                    }
                }
                kind = now;
                run = now != 0 ? 1 : 0;
            }
        }
    }

    // Река течёт от края города наружу, петляя, пока не встретит другую воду, чужие земли или край карты
    static void GrowRiver(byte[] t, WarGeo g, int sx, int sy, int nx, int ny, int width, Random rng)
    {
        int w = g.W, h = g.H;
        int tx = ny != 0 ? 1 : 0, ty = nx != 0 ? 1 : 0;   // поперёк течения
        double off = 0, drift = 0, wid = Math.Max(2, width);
        for (int s = 1; s < 500; s++)
        {
            drift = Math.Max(-0.8, Math.Min(0.8, drift + (rng.NextDouble() - 0.5) * 0.3));
            off += drift;
            wid = Math.Max(2.0, Math.Min(5.5, wid + (rng.NextDouble() - 0.5) * 0.4));
            int cx = sx + nx * s + (int)Math.Round(tx * off), cy = sy + ny * s + (int)Math.Round(ty * off);
            if (cx < 0 || cy < 0 || cx >= w || cy >= h) return;
            if (g.ZoneOfCell(cx, cy) != 0) return;
            byte cur = t[cy * w + cx];
            if (s > 3 && IsWet(cur)) return;   // влилась в другую реку или озеро
            int half = (int)(wid / 2);
            for (int d = -half; d <= half; d++)
            {
                int x = cx + tx * d, y = cy + ty * d;
                if (x < 0 || y < 0 || x >= w || y >= h || g.ZoneOfCell(x, y) != 0) continue;
                if (t[y * w + x] != Ford) t[y * w + x] = Shallow;
            }
        }
    }

    // Лес с края города растёт наружу купами и редеет
    static void GrowGrove(byte[] t, WarGeo g, int sx, int sy, int nx, int ny, int run, Random rng)
    {
        int tx = ny != 0 ? 1 : 0, ty = nx != 0 ? 1 : 0;
        int depth = Math.Min(50, 8 + run / 2 + rng.Next(16));
        double off = 0;
        for (int s = 2; s <= depth; s += 3)
        {
            double k = 1.0 - (double)s / (depth + 2);
            off += (rng.NextDouble() - 0.5) * 3;
            double r = Math.Max(2.0, run / 2.0 * k + rng.NextDouble() * 3);
            int cx = sx + nx * s + (int)(tx * off), cy = sy + ny * s + (int)(ty * off);
            Blob(t, g, cx, cy, r, Grove, rng);
        }
    }

    static void Blob(byte[] t, WarGeo g, int cx, int cy, double r, byte kind, Random rng)
    {
        int w = g.W, h = g.H;
        int ri = (int)Math.Ceiling(r);
        for (int y = cy - ri; y <= cy + ri; y++)
            for (int x = cx - ri; x <= cx + ri; x++)
            {
                if (x < 0 || y < 0 || x >= w || y >= h || g.ZoneOfCell(x, y) != 0) continue;
                double d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) + rng.NextDouble() * 1.2;
                if (d > r) continue;
                byte cur = t[y * w + x];
                if (cur == Grass || cur == Field || (kind == Forest && cur == Grove)) t[y * w + x] = kind;
            }
    }

    // Плавный шум (одинаковый у игры и сервера): из него - большие связные леса
    static double Hash(int seed, int x, int y)
    {
        unchecked
        {
            uint hsh = (uint)(seed * 374761393 + x * 668265263 + y * 1274126177);
            hsh = (hsh ^ (hsh >> 13)) * 1274126177;
            hsh ^= hsh >> 16;
            return (hsh & 0xFFFFFF) / (double)0x1000000;
        }
    }

    static double Noise(int seed, double x, double y)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        double a = Hash(seed, x0, y0), b = Hash(seed, x0 + 1, y0), c = Hash(seed, x0, y0 + 1), d = Hash(seed, x0 + 1, y0 + 1);
        return (a + (b - a) * fx) * (1 - fy) + (c + (d - c) * fx) * fy;
    }

    /// <summary>Мост в местности города (по нему через реку ходят и на карте войны).</summary>
    public const char BridgeCode = 'B';

    public static byte[] Terrain(int seed, WarGeo g)
    {
        int w = g.W, h = g.H;
        var t = new byte[w * h];
        var rng = new Random(seed);
        int n0 = g.MidX0, n1 = g.MidX1;

        // Река через нейтральные земли с бродами (сверху донизу, по середине карты)
        double rx = n0 + (n1 - n0) * (0.35 + rng.NextDouble() * 0.3);
        int fordEvery = Math.Max(40, h / 6);
        int fordOffset = rng.Next(fordEvery);
        for (int y = 0; y < h; y++)
        {
            rx += (rng.NextDouble() - 0.5) * 1.6;
            rx = Math.Max(n0 + 20, Math.Min(n1 - 20, rx));
            bool ford = (y + fordOffset) % fordEvery < 6;
            int half = 2 + (int)(Math.Sin(y * 0.05 + seed) * 1.2 + 1.2);
            for (int x = (int)rx - half; x <= (int)rx + half; x++)
                if (x >= 0 && x < w) t[y * w + x] = ford ? Ford : Shallow;   // реку переходят вброд (медленно), на бродах - быстро
        }

        // Большая кайма сверху и снизу (квадратная карта): там течёт поперечная река - тоже с бродами
        int bandH = (g.my - 1) * Plot;
        if (bandH >= 2 * Plot)
            for (int band = 0; band < 2; band++)
            {
                double ry = band == 0 ? bandH * (0.45 + rng.NextDouble() * 0.2) : h - bandH * (0.45 + rng.NextDouble() * 0.2);
                int fo = rng.Next(fordEvery);
                for (int x = 0; x < w; x++)
                {
                    ry += (rng.NextDouble() - 0.5) * 1.4;
                    ry = band == 0 ? Math.Max(12, Math.Min(bandH - 12, ry)) : Math.Max(h - bandH + 12, Math.Min(h - 12, ry));
                    bool ford = (x + fo) % fordEvery < 7;
                    int half = 2 + (int)(Math.Sin(x * 0.04 + seed + band) * 1.1 + 1.1);
                    for (int y = (int)ry - half; y <= (int)ry + half; y++)
                        if (y >= 0 && y < h && g.ZoneOfCell(x, y) == 0) t[y * w + x] = ford ? Ford : (t[y * w + x] == Ford ? Ford : Shallow);
                }
            }

        // Леса во всех нейтральных землях: большие связные массивы (светлый лес - проходимый, в середине - чаща)
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (g.ZoneOfCell(x, y) != 0 || IsWet(t[y * w + x])) continue;
                double n = Noise(seed, x / 38.0, y / 38.0) * 0.7 + Noise(seed + 17, x / 9.0, y / 9.0) * 0.3;
                if (n > 0.71) t[y * w + x] = Forest;
                else if (n > 0.58) t[y * w + x] = Grove;
            }

        // Озёра
        int blobs = w * h / 9000;
        for (int i = 0; i < blobs; i++)
        {
            int cx = rng.Next(w), cy = rng.Next(h);
            if (g.ZoneOfCell(cx, cy) != 0) continue;
            int r = 3 + rng.Next(4);
            for (int y = cy - r; y <= cy + r; y++)
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (x < 0 || x >= w || y < 0 || y >= h || g.ZoneOfCell(x, y) != 0) continue;
                    double d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) + rng.NextDouble() * 1.5;
                    if (d > r) continue;
                    if (IsWet(t[y * w + x])) continue;
                    t[y * w + x] = Water;
                }
        }

        // Поля у кромки нейтральных земель (просто для вида)
        for (int i = 0; i < h / 25; i++)
        {
            int cx = n0 + 4 + rng.Next(12), cy = rng.Next(h);
            if (rng.NextDouble() < 0.5) cx = n1 - 16 + rng.Next(12);
            for (int y = cy; y < cy + 5; y++)
                for (int x = cx; x < cx + 7; x++)
                    if (x >= 0 && x < w && y >= 0 && y < h && t[y * w + x] == Grass) t[y * w + x] = Field;
        }

        // Вокруг земель кланов - полоса без чащи и озёр: из города всегда можно выйти
        int pad = 6;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                byte b = t[y * w + x];
                if (b != Forest && b != Water) continue;
                if (g.ZoneOfCell(Math.Max(0, x - pad), y) != 0 || g.ZoneOfCell(Math.Min(w - 1, x + pad), y) != 0 ||
                    g.ZoneOfCell(x, Math.Max(0, y - pad)) != 0 || g.ZoneOfCell(x, Math.Min(h - 1, y + pad)) != 0)
                    t[y * w + x] = b == Water ? Shallow : Grove;
            }

        return t;
    }

    // ---------- Бойцы ----------

    public static readonly WarUnitDef[] Units =
    {
        new WarUnitDef { name = "Мечник", gold = 20, food = 15, train = 3, hp = 100, dmg = 12, range = 1.1f, cd = 1f, speed = 1.6f, space = 1, art = "sword" },
        new WarUnitDef { name = "Лучник", gold = 35, food = 20, wood = 5, train = 5, hp = 70, dmg = 9, range = 6f, cd = 1.4f, speed = 1.5f, space = 1, art = "archer" },
        new WarUnitDef { name = "Разведчик", gold = 30, food = 15, train = 4, hp = 60, dmg = 5, range = 1.1f, cd = 1f, speed = 2.4f, space = 1, barracks = 2, art = "scout", vision = 12f },
        new WarUnitDef { name = "Рыцарь", gold = 80, food = 40, iron = 10, train = 8, hp = 240, dmg = 24, range = 1.2f, cd = 1.3f, speed = 1.3f, space = 3, art = "knight" },
        new WarUnitDef { name = "Полевой маг", gold = 90, food = 40, train = 10, hp = 70, dmg = 4, range = 4f, cd = 1.5f, speed = 1.6f, space = 2, barracks = 3, heal = 4f, art = "mage" },
        new WarUnitDef { name = "Всадник", gold = 120, food = 50, iron = 10, train = 10, hp = 200, dmg = 20, range = 1.3f, cd = 1f, speed = 2.8f, space = 4, barracks = 4, mounted = true, art = "sword" },
        new WarUnitDef { name = "Конный лучник", gold = 130, food = 50, wood = 10, train = 11, hp = 140, dmg = 11, range = 6.5f, cd = 1.2f, speed = 2.6f, space = 4, barracks = 4, mounted = true, art = "archer" },
        new WarUnitDef { name = "Маг-отравитель", gold = 70, food = 25, train = 7, hp = 70, dmg = 10, range = 5.5f, cd = 2.2f, speed = 1.5f, space = 1, barracks = 2, path = 1, aoe = 1.6f, art = "mage" },
        new WarUnitDef { name = "Маг стихий", gold = 180, food = 60, iron = 10, train = 16, hp = 160, dmg = 22, range = 5.5f, cd = 1.6f, speed = 1.4f, space = 4, barracks = 3, path = 1, aoe = 1.2f, art = "mage" },
        new WarUnitDef { name = "Поджигатель", gold = 45, food = 20, wood = 10, train = 6, hp = 95, dmg = 14, range = 3.2f, cd = 1.6f, speed = 1.9f, space = 1, barracks = 3, path = 2, aoe = 1.9f, bldMul = 2f, art = "bomber" },
        new WarUnitDef { name = "Полевая пушка", gold = 220, food = 40, wood = 20, iron = 30, train = 18, hp = 260, dmg = 34, range = 7.5f, cd = 4.2f, speed = 0.95f, space = 5, barracks = 3, path = 2, aoe = 1.2f, bldMul = 3f, art = "civilian" },
        new WarUnitDef { name = "Катапульта", gold = 180, food = 30, wood = 40, train = 20, hp = 220, dmg = 45, range = 13f, cd = 5f, speed = 0.8f, space = 4, path = 4, engineer = true, aoe = 1.6f, bldMul = 1.5f, minRange = 3f, art = "civilian" },
        // Таран: на карте войны каждый носильщик - полтарана (двое вместе бьют как целый). По бойцам не бьёт, стены - первыми
        new WarUnitDef { name = "Таран", gold = 18, food = 12, wood = 10, train = 3, hp = 85, dmg = 30, range = 1.3f, cd = 1.6f, speed = 2.1f, space = 1, barracks = 2, bldMul = 1.6f, wallMul = 2.6f, ram = true, art = "sword" },
    };

    public const int HorseFood = 30;   // конь для конного - ещё столько еды

    public static int UnitIndex(string name)
    {
        for (int i = 0; i < Units.Length; i++) if (Units[i].name == name) return i;
        return -1;
    }

    /// <summary>Чем силён боец - для подсказки в окне найма.</summary>
    public static string UnitRole(WarUnitDef d)
    {
        if (d.ram) return "ломает стены и ворота, по бойцам не бьёт";
        if (d.heal > 0) return "лечит своих рядом";
        if (d.engineer) return "бьёт здания издалека, вблизи бесполезна";
        if (d.bldMul >= 2f) return "сильнее всех по зданиям";
        if (d.vision > 0) return "быстрый, далеко видит";
        if (d.aoe > 0) return "бьёт по площади";
        if (d.mounted) return "быстрый, на коне";
        if (d.range > 3f) return "стреляет издалека";
        return d.space >= 3 ? "крепкий, держит удар" : "дешёвый, бьёт вблизи";
    }

    // ---------- Здания ----------

    public const string HallName = "Ратуша", TowerName = "Башня", WallName = "Стена", GateName = "Ворота",
        BarracksName = "Казарма", EngBarracksName = "Инженерная казарма", StableName = "Конюшня",
        FarmName = "Ферма", SawmillName = "Лесопилка", MineName = "Шахта",
        MagicCannonName = "Магическая пушка", GunName = "Пушка";

    /// <summary>Здания, которых на карте войны нет (дорожки и мосты).</summary>
    public static bool Skipped(string type) { return type == "Дорожка" || type == "Брусчатка" || type == "Мост"; }

    public static float BuildingHp(string type, int level)
    {
        float hp;
        switch (type)
        {
            case HallName: hp = 1500; break;
            case FarmName: hp = 300; break;
            case MineName: hp = 500; break;
            case BarracksName: case EngBarracksName: hp = 700; break;
            case TowerName: hp = 600; break;
            case MagicCannonName: hp = 550; break;
            case GunName: hp = 650; break;
            case GateName: hp = 500; break;
            case WallName: hp = 400; break;
            default: hp = 420; break;
        }
        return hp * (1f + 0.25f * (Math.Max(1, Math.Min(5, level)) - 1));
    }

    /// <summary>Размер здания, если город сохранён старой версией игры (без размеров).</summary>
    public static void DefaultSize(string type, out int w, out int h)
    {
        w = h = 2;
        switch (type)
        {
            case HallName: case FarmName: case EngBarracksName: case "Завод": w = h = 3; break;
            case "Двор военного строителя": w = 3; h = 2; break;
            case WallName: case GateName: case "Мост": case "Дорожка": case "Брусчатка": w = h = 1; break;
        }
    }

    /// <summary>Стреляет ли здание: дальность, урон, перезарядка, удар по площади. Цитадель тоже отстреливается (слабо).</summary>
    public static bool Shoots(string type, int level, out float range, out float dmg, out float cd, out float aoe)
    {
        float mul = 1f + 0.25f * (Math.Max(1, level) - 1);
        range = dmg = cd = aoe = 0;
        if (type == TowerName) { range = 6.5f; dmg = 9f * mul; cd = 1.3f; return true; }
        if (type == MagicCannonName || type == GunName) { range = 8f; dmg = 20f * mul; cd = 3.5f; aoe = 1.4f; return true; }
        if (type == HallName) { range = 6f; dmg = 7f * mul; cd = 1.6f; return true; }
        return false;
    }

    /// <summary>Доход здания в минуту (обычная война): золото, еда, дерево, камень, железо.</summary>
    public static void Income(string type, int level, int[] add)
    {
        int L = Math.Max(1, level);
        switch (type)
        {
            case HallName: add[0] += 6 * L; add[1] += 3 * L; add[2] += L; add[3] += L; break;
            case FarmName: add[1] += 4 * L; break;
            case SawmillName: add[2] += 3 * L; break;
            case MineName: add[3] += 2 * L; add[4] += L; break;
            case "Жилой дом": add[0] += 1; break;
            case "Торговый дом": add[0] += 3 * L; break;
            case "Хранилище": add[0] += 1; add[2] += 1; break;
            case "Литейная": case "Кузня мастера": add[4] += L; break;
            case "Кухня": add[1] += 2 * L; break;
        }
    }

    public const int NeutralPlotGold = 3, NeutralPlotFood = 2;   // доход каждому городу за занятый чужой участок, в минуту

    // ---------- Источники в нейтральных землях ----------
    // Деревни, лесопилки, каменоломни и рудники посреди нейтральных земель. Ничьи, пока их участок никто не занял.
    // Чей участок - того и источник: доход идёт всему клану и делится поровну между его целыми городами.
    // Стоят парами, зеркально: у каждого клана свой «ближний» источник и спорный ближе к врагу.

    public const int SrcVillage = 0, SrcSawmill = 1, SrcQuarry = 2, SrcMine = 3;
    public static readonly string[] SourceNames = { "Деревня", "Лесопилка", "Каменоломня", "Рудник" };
    public const int SourceSize = 4;   // клеток: столько занимает картинка на карте (по ней ходят)

    public static string SourceName(int kind) { return SourceNames[Math.Max(0, Math.Min(SourceNames.Length - 1, kind))]; }

    /// <summary>Доход источника в минуту на весь клан (обычная война): золото, еда, дерево, камень, железо.</summary>
    public static void SourceIncome(int kind, int[] add)
    {
        switch (kind)
        {
            case SrcVillage: add[0] += 25; add[1] += 30; break;
            case SrcSawmill: add[2] += 40; break;
            case SrcQuarry: add[3] += 30; break;
            case SrcMine: add[4] += 15; add[3] += 10; break;
        }
    }

    static readonly string[] resNames = { "золота", "еды", "дерева", "камня", "железа" };

    /// <summary>«12 золота, 16 еды» - доход (в минуту), делённый на столько городов.</summary>
    public static string IncomeText(int[] add, float div = 1f)
    {
        var parts = new List<string>();
        for (int i = 0; i < add.Length && i < resNames.Length; i++)
        {
            if (add[i] <= 0) continue;
            float v = add[i] / Math.Max(1f, div);
            parts.Add((v >= 10f || Math.Abs(v - Math.Round(v)) < 0.05f ? Math.Round(v).ToString() : Math.Round(v, 1).ToString()) + " " + resNames[i]);
        }
        return string.Join(", ", parts);
    }

    /// <summary>
    /// Где стоят источники. Считается одинаково всегда (из seed и местности), сервер шлёт готовый список в WarStatic.
    /// Ставятся на нейтральных участках левой половины карты и зеркально - на правой, не ближе двух участков друг к другу.
    /// </summary>
    public static List<WarSource> Sources(int seed, WarGeo g, byte[] terrain)
    {
        var list = new List<WarSource>();
        if (terrain == null) return list;
        var rng = new Random(seed * 7919 + g.Rows * 31 + 5);
        var cand = new List<int>();
        int half = g.Cols / 2;
        for (int py = 1; py < g.Rows - 1; py++)
            for (int px = 1; px < half; px++)
                if (g.ZoneOfPlot(px, py) == 0 && g.ZoneOfPlot(g.Cols - 1 - px, py) == 0) cand.Add(py * g.Cols + px);
        for (int i = cand.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); int tmp = cand[i]; cand[i] = cand[j]; cand[j] = tmp; }
        // Сначала - у середины (спорные), потом остальные: сортируем по «близости к реке» с примесью случайности
        int want = Math.Max(3, Math.Min(cand.Count, g.Slots / 2 + 3));
        var kinds = new List<int> { SrcVillage, SrcSawmill, SrcQuarry, SrcMine };
        for (int i = kinds.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); int tmp = kinds[i]; kinds[i] = kinds[j]; kinds[j] = tmp; }
        var chosen = new List<int>();
        foreach (int p in cand)
        {
            if (chosen.Count >= want) break;
            int px = p % g.Cols, py = p / g.Cols;
            bool far = true;
            foreach (int q in chosen)
                if (Math.Abs(q % g.Cols - px) < 3 && Math.Abs(q / g.Cols - py) < 3) { far = false; break; }
            if (!far) continue;
            // Хоть один источник - рядом с серединой
            chosen.Add(p);
        }
        for (int k = 0; k < chosen.Count; k++)
        {
            int px = chosen[k] % g.Cols, py = chosen[k] / g.Cols;
            for (int side = 0; side < 2; side++)
            {
                int qx = side == 0 ? px : g.Cols - 1 - px;
                var s = new WarSource { id = list.Count, kind = kinds[k % kinds.Count], plot = py * g.Cols + qx };
                PlaceSource(s, qx, py, terrain, g);
                list.Add(s);
            }
        }
        return list;
    }

    // Ищем сухое открытое место поближе к середине участка (спиралью); не нашли - годится и светлый лес
    static void PlaceSource(WarSource s, int px, int py, byte[] t, WarGeo g)
    {
        int cx = px * Plot + Plot / 2, cy = py * Plot + Plot / 2;
        s.x = cx - SourceSize / 2; s.y = cy - SourceSize / 2;
        if (!FindSpot(s, px, py, cx, cy, t, g, false)) FindSpot(s, px, py, cx, cy, t, g, true);
    }

    static bool FindSpot(WarSource s, int px, int py, int cx, int cy, byte[] t, WarGeo g, bool grove)
    {
        int w = g.W, h = g.H;
        for (int r = 0; r < Plot / 2 - SourceSize; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    int x0 = cx + dx - SourceSize / 2, y0 = cy + dy - SourceSize / 2;
                    if (x0 < px * Plot + 1 || y0 < py * Plot + 1 || x0 + SourceSize > (px + 1) * Plot - 1 || y0 + SourceSize > (py + 1) * Plot - 1) continue;
                    bool ok = true;
                    for (int y = y0 - 1; y <= y0 + SourceSize && ok; y++)
                        for (int x = x0 - 1; x <= x0 + SourceSize && ok; x++)
                        {
                            if (x < 0 || y < 0 || x >= w || y >= h) { ok = false; break; }
                            byte b = t[y * w + x];
                            if (!Passable(b) || IsWet(b) || (b == Grove && !grove)) ok = false;
                        }
                    if (ok) { s.x = x0; s.y = y0; return true; }
                }
        return false;
    }
}

/// <summary>Источник ресурсов в нейтральных землях: чей участок plot - того и доход.</summary>
[Serializable]
public class WarSource
{
    public int id, kind;
    public int plot;                // номер участка (py * Cols + px)
    public int x, y;                // левый нижний угол картинки (клетки карты), размер WarRules.SourceSize
}

[Serializable]
public class WarUnitDef
{
    public string name;
    public int gold, food, wood, iron;
    public float train, hp, dmg, range, cd, speed;
    public int space = 1;
    public int barracks = 1;        // нужен уровень Казармы
    public int path;                // нужен путь развития (PathKind: 1 Магия, 2 Военное дело, 4 Инженерия)
    public bool mounted;            // нужна Конюшня
    public bool engineer;           // нанимается в Инженерной казарме
    public float aoe, heal, minRange;
    public float bldMul = 1f;       // во сколько раз сильнее бьёт по зданиям
    public float vision;            // 0 - обычный обзор
    public string art;
    public bool ram;                // таран: по бойцам не бьёт, сам ищет стены и ворота
    public float wallMul = 1f;      // по стенам и воротам (вместо bldMul)
}

// ---------- Окно клана: состояние войны ----------

[Serializable]
public class WarInfo
{
    public bool inWar;
    public bool searching;
    public string warId;
    public int stage;               // WarStage
    public float stageLeft;         // секунд до следующей стадии
    public float warLeft;           // секунд до конца войны
    public bool fast;
    public int mySide;
    public string nameA, nameB;
    public bool leader;             // я глава своего клана
    public bool admin;              // я админ сервера: могу переключать дни войны
    public int day;                 // какой сейчас день войны (1..7)
    public int rows;
    public int gc;                  // столбцов городов у клана
    public int nc, mx, my;          // форма карты (WarGeo): 0 - старая война
    public List<WarSlot> slots;     // мой клан: кто на каком месте схемы
    public int scoreA, scoreB;
    public int winner;              // 0 - ещё идёт или ничья, 1 / 2 - победитель
    public string result;
    public List<string> log;
    // Маленькая карта в окне клана (со 2-го дня)
    public string plots;            // хозяин каждого участка: '0' - ничей, '1' / '2' - сторона (строки снизу вверх)
    public List<WarCityDot> cities; // города на карте
    public List<WarSource> sources; // источники в нейтральных землях
    public int version;             // WarRules.Version сервера

    public WarGeo Geo => WarGeo.Of(rows, gc, nc, mx, my);
}

[Serializable]
public class WarCityDot
{
    public string name;
    public int side, plotX, plotY;
    public bool fallen, mine;
}

[Serializable]
public class WarSlot
{
    public string accountId;
    public string name;
    public int slot = -1;
    public int hall;
}

// ---------- Карта войны: постоянное (присылается редко) ----------

[Serializable]
public class WarStatic
{
    public string warId;
    public int version;             // меняется, когда открыли новые здания врага или перенесли здание
    public int seed, rows;
    public int gc;
    public int nc, mx, my;          // форма карты (WarGeo)
    public int mySide;
    public int myCity = -1;
    public int myPath;              // путь развития моего города (для найма)
    public bool fast;
    public string nameA, nameB;
    public List<WarCityView> cities;
    public List<WarBldView> buildings;
    public List<WarSource> sources;

    public WarGeo Geo => WarGeo.Of(rows, gc, nc, mx, my);
}

[Serializable]
public class WarCityView
{
    public int idx;
    public string accountId, name;
    public int side, plotX, plotY;
    public bool fallen;
    public int hall;
    public int mapSize;             // местность города (TerrainCode, мосты - BridgeCode)
    public string terrain;
}

[Serializable]
public class WarBldView
{
    public int id, city;
    public string type;
    public int level, x, y, w, h;
    public float maxHp;
}

// ---------- Карта войны: то, что меняется (присылается несколько раз в секунду) ----------

[Serializable]
public class WarFrame
{
    public int version;
    public int stage;
    public float stageLeft, warLeft;
    public double time;             // время расчёта на сервере, секунд
    // Бойцы, которых видно: "id,вид,сторона,x*10,y*10,здоровье%,флаги,цель,отряд;..." (флаги: 1 мой, 2 бьёт, 4 идёт)
    public string units;
    // Повреждённые здания: "id,здоровье%,чинят(0/1);..." (разрушенные - 0); остальные целые
    public string blds;
    public string plots;            // владелец каждого участка: '0', '1', '2' (по строкам снизу вверх)
    public string capture;          // кто захватывает участок: "номер,сторона,прогресс%;..."
    public string shots;            // выстрелы башен: "id здания,x*10,y*10,время*10;..." (один выстрел приходит в нескольких кадрах)
    public string dead;             // кто погиб только что (из тех, кого видно): "id;..."
    public int gold, food, wood, stone, iron;
    public int army, armyCap, space, spaceCap;
    public List<WarQueueItem> queue;
    public List<string> events;     // новые события с прошлого запроса
    public int eventN;
    public int scoreA, scoreB;
    public int winner;
    public bool fallen;             // мой город пал
    // klan-voina-1
    public int weaponLv, armorLv;   // улучшения моего города
    public List<string> squadNames; // названия отрядов 1..6 (пусто - «Отряд N»)
    public int income0, income1, income2, income3, income4;   // доход моего города в минуту (все источники)
    public int kills, losses;       // мой город: убито врагов, потеряно своих
    public List<WarGrant> gifts;    // klan-voina-2: подарки соседей по клану моей деревне (приходят один раз)
    public List<WarSlot> mates;     // klan-voina-2: соседи по клану на этой войне (кому можно дарить)
    // klan-voina-14: ответ на WarCommand.Recall - кто зашёл в казарму (вид, казарма bx/by, снаряжение kit, здоровье - в hpMul долей 0..1)
    public List<WarRecruit> back;
}

[Serializable]
public class WarQueueItem
{
    public string unit;
    public float left;              // секунд до готовности (у первого в очереди)
    public int squad;
}

/// <summary>Приказ с карты войны.</summary>
[Serializable]
public class WarCommand
{
    public const int Move = 1, Attack = 2, Stop = 3, Train = 4, MoveBuilding = 5, CancelTrain = 6, AttackMove = 7,
        SetSquad = 8, Repair = 9, Research = 10, RenameSquad = 11, Hold = 12, Gift = 13,
        Recall = 14;                // klan-voina-14: бойцы рядом со своей казармой заходят в неё (units) - возвращаются в деревню
    public int kind;
    public List<int> units;
    public float x, y;
    public int target = -1;         // id бойца (Attack) или здания (Attack по зданию: -id - 1)
    public string unit;             // Train: вид бойца; RenameSquad: новое название
    public int count = 1;
    public int building = -1;       // MoveBuilding / Repair: id здания (Repair: -1 - все повреждённые)
    public int squad;               // Train / SetSquad / RenameSquad: номер отряда (0 - без отряда); Research: что улучшать
    // Gift (klan-voina-2): подарок деревне соседа по клану - кому (accountId) и сколько
    public string memberId;
    public int gold, food, wood, stone, iron;
}

/// <summary>Боец, которого выводили из казармы военного города (сборка 11 и раньше). Оставлено для совместимости протокола.</summary>
[Serializable]
public class WarRecruit
{
    public string unit;
    public float dmgMul = 1f;
    public float hpMul = 1f;
    public bool here;
    public float x, y;
    public int squad;               // в какой отряд (0 - сам по роду войск)
    // klan-voina-14: боец приписан к казарме деревни (bx, by - её левая нижняя клетка в деревне) и несёт своё снаряжение (kit -
    // SavedSoldier в JSON): у казармы он может снова зайти в неё со всем, что у него было (WarCommand.Recall)
    public bool bound;
    public int bx, by;
    public string kit;
    public int id;                  // Recall: id бойца на карте
}

/// <summary>Награда на домашнюю базу (магазин марок, лиги, админ).</summary>
[Serializable]
public class WarGrant
{
    public string id;
    public string text;
    public int gold, wood, stone, food, iron;
    public int marks;   // купленные марки (магазин марок)
}
