using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Обновление 14: NPC-деревни. Девять деревень, которые живут на сервере сами (3 на Цитадели Ур.1, 3 на Ур.2, 3 на Ур.3).
/// Их находят во вкладке «Бой» вместе с деревнями игроков, бой идёт как с деревней игрока, которого нет в игре
/// (на копии деревни). Отличия от игроков:
///  - щит после набега короткий: 2–12 минут вместо 30 минут – 16 часов (NpcShieldMinutes);
///  - деревня восстанавливается сама: руины встают за NpcRebuildMinutes, казна пополняется за NpcRestockMinutes,
///    погибшие защитники возвращаются за NpcArmyMinutes. Каждый набег хранится как «потеря» (NpcLoss) и
///    со временем тает; город NPC каждый раз собирается заново из постоянной расстановки (шаблона) минус потери;
///  - рейтинг NPC не меняется, нападающий получает за NPC половину обычного рейтинга;
///  - в таблице лиг, в «Выписать всем» и в числе игроков NPC нет, войти в их аккаунты нельзя (у них нет пароля).
/// Расстановка строится из Seed и уровня (NpcCity) - одна и та же при каждом запуске сервера.
/// </summary>
public partial class ServerCore
{
    // ---------- Настройки ----------

    const int NpcVersion = 1;                 // поменялась расстановка - города NPC пересобираются при запуске
    const double NpcRebuildMinutes = 10;      // руины встают
    const double NpcRestockMinutes = 25;      // унесённое возвращается в казну (равномерно)
    const double NpcArmyMinutes = 20;         // погибшие защитники обучены заново (равномерно)
    static readonly int[] NpcRating = { 150, 350, 600 };   // рейтинг NPC по уровню Цитадели (для поправки за соперника)

    /// <summary>Щит NPC после набега: намного короче, чем у игрока (у игрока 30 мин – 16 ч).</summary>
    static int NpcShieldMinutes(int playerMinutes)
    {
        if (playerMinutes <= 30) return 2;     // отбились - почти сразу снова открыты
        if (playerMinutes <= 120) return 5;
        if (playerMinutes <= 240) return 8;
        if (playerMinutes <= 480) return 10;
        return 12;
    }

    partial class StoredAccount   // [Serializable] - в ServerCore.cs
    {
        public bool npc;                                          // NPC-деревня сервера (обновление 14)
        public List<NpcLoss> npcLosses = new List<NpcLoss>();     // набеги на NPC, от которых деревня ещё не оправилась
    }

    [Serializable]
    class NpcLoss
    {
        public string timeUtc;
        public RaidLoot loot = new RaidLoot();
        public List<string> killed = new List<string>();
        public List<string> ruinedAt = new List<string>();
    }

    partial class Db   // [Serializable] - в ServerCore.cs
    {
        public int npcVersion;   // какой версии расстановки сейчас города NPC
    }

    class NpcDef
    {
        public string id, login, name;
        public int level, seed;
        public int path, school, eng, war;   // PathKind, MagicSchool, EngSchool, WarSchool (числа - как в CityData)
        public bool walls, river;
    }

    // PathKind: 1 - Магия, 2 - Военное дело, 4 - Инженерия. MagicSchool: 2 - Жизни. EngSchool: 1 - Военный инженер.
    // WarSchool: 2 - Военный строитель. Путь выбирают с Цитадели Ур.2, школы - с Ур.3.
    static readonly NpcDef[] NpcDefs =
    {
        new NpcDef { id = "4e504300000000000000000000000001", login = "npc-1", name = "Ольховка",       level = 1, seed = 41017 },
        new NpcDef { id = "4e504300000000000000000000000002", login = "npc-2", name = "Сосновый Яр",    level = 1, seed = 52331, river = true },
        new NpcDef { id = "4e504300000000000000000000000003", login = "npc-3", name = "Малые Броды",    level = 1, seed = 63949, walls = true },
        new NpcDef { id = "4e504300000000000000000000000004", login = "npc-4", name = "Каменный Брод",   level = 2, seed = 70313, path = 1, walls = true, river = true },
        new NpcDef { id = "4e504300000000000000000000000005", login = "npc-5", name = "Вересковая Падь", level = 2, seed = 81667, path = 2, walls = true },
        new NpcDef { id = "4e504300000000000000000000000006", login = "npc-6", name = "Ржаное Поле",     level = 2, seed = 92461, path = 4, walls = true },
        new NpcDef { id = "4e504300000000000000000000000007", login = "npc-7", name = "Чёрный Утёс", level = 3, seed = 13579, path = 1, school = 2, walls = true },
        new NpcDef { id = "4e504300000000000000000000000008", login = "npc-8", name = "Медный Холм", level = 3, seed = 24683, path = 2, war = 2, walls = true, river = true },
        new NpcDef { id = "4e504300000000000000000000000009", login = "npc-9", name = "Ледяной Рог", level = 3, seed = 35797, path = 4, eng = 1, walls = true },
    };

    static NpcDef NpcDefOf(string id)
    {
        foreach (var d in NpcDefs) if (d.id == id) return d;
        return null;
    }

    static bool IsNpc(StoredAccount a) { return a != null && a.npc; }

    readonly Dictionary<string, string> npcTemplates = new Dictionary<string, string>();   // id → город без потерь (JSON)

    string NpcTemplateJson(NpcDef d)
    {
        if (!npcTemplates.TryGetValue(d.id, out string json))
            npcTemplates[d.id] = json = JsonUtility.ToJson(NpcCity.Build(d.level, d.seed, d.path, d.school, d.eng, d.war, d.walls, d.river));
        return json;
    }

    // ---------- Заведение и обновление ----------

    /// <summary>При запуске: NPC есть у сервера, их города собраны (и пересобраны, если поменялась расстановка).</summary>
    void EnsureNpcs()
    {
        bool rebuild = db.npcVersion != NpcVersion;
        bool dirty = rebuild;
        foreach (var d in NpcDefs)
        {
            StoredAccount a = FindById(d.id);
            if (a == null)
            {
                a = new StoredAccount
                {
                    profile = new AccountProfile { id = d.id, login = d.login, displayName = d.name, createdUtc = Now(), lotSlots = 0, noPassword = true },
                    npc = true,
                };
                db.accounts.Add(a);   // в конец: админ сервера - первый аккаунт игрока, NPC им не станет (IsAdmin их пропускает)
                dirty = true;
                Debug.Log($"[Сервер] NPC-деревня: {d.name} (Цитадель Ур.{d.level})");
            }
            a.npc = true;
            a.salt = null;
            a.hash = null;   // войти в NPC нельзя
            a.profile.displayName = d.name;
            a.profile.rating = NpcRating[Mathf.Clamp(d.level, 1, 3) - 1];
            if (a.npcLosses == null) a.npcLosses = new List<NpcLoss>();
            a.pending.Clear(); a.sales.Clear(); a.grants.Clear();
            if (rebuild || ReadCity(d.id) == null) NpcRefresh(a);
        }
        db.npcVersion = NpcVersion;
        if (dirty) Save();
    }

    /// <summary>Город NPC сейчас: расстановка минус потери, которые ещё не восстановились. Пишется в файл города.</summary>
    CityData NpcRefresh(StoredAccount a)
    {
        NpcDef d = NpcDefOf(a.profile.id);
        if (d == null) return ReadCity(a.profile.id);
        DateTime now = DateTime.UtcNow;
        double keep = Math.Max(NpcRebuildMinutes, Math.Max(NpcRestockMinutes, NpcArmyMinutes));
        if (a.npcLosses == null) a.npcLosses = new List<NpcLoss>();
        a.npcLosses.RemoveAll(l => l == null || (now - ParseUtc(l.timeUtc)).TotalMinutes >= keep);

        CityData c = JsonUtility.FromJson<CityData>(NpcTemplateJson(d));
        string nowIso = now.ToString("o");
        foreach (var l in a.npcLosses)
        {
            double m = Math.Max(0.0, (now - ParseUtc(l.timeUtc)).TotalMinutes);
            float res = (float)Math.Max(0.0, 1.0 - m / NpcRestockMinutes);
            int dead = l.killed != null ? (int)Math.Ceiling(l.killed.Count * Math.Max(0.0, 1.0 - m / NpcArmyMinutes)) : 0;
            var rep = new RaidReport { timeUtc = nowIso, loot = ScaleLoot(l.loot, res) };   // руины - «только что»: встанут, когда их уберёт сервер
            if (l.killed != null) for (int i = 0; i < dead && i < l.killed.Count; i++) rep.killed.Add(l.killed[i]);
            if (m < NpcRebuildMinutes && l.ruinedAt != null) rep.ruinedAt.AddRange(l.ruinedAt);
            CityLoss.Apply(c, rep);
        }
        c.ownerId = a.profile.id;
        c.ownerName = a.profile.displayName;
        c.savedUtc = nowIso;
        c.npc = true;
        WriteSafe(CityPath(a.profile.id), JsonUtility.ToJson(c));
        return c;
    }

    static RaidLoot ScaleLoot(RaidLoot l, float k)
    {
        var r = new RaidLoot();
        if (l == null || k <= 0f) return r;
        int S(int v) { return Mathf.Max(0, (int)Math.Round(v * (double)k)); }
        r.gold = S(l.gold); r.wood = S(l.wood); r.stone = S(l.stone); r.food = S(l.food); r.iron = S(l.iron);
        r.oats = S(l.oats); r.berries = S(l.berries); r.herbs = S(l.herbs); r.potions = S(l.potions); r.resin = S(l.resin);
        for (int i = 0; i < 3; i++)
        {
            r.planks[i] = S(RaidLoot.At(l.planks, i)); r.plates[i] = S(RaidLoot.At(l.plates, i));
            r.steel[i] = S(RaidLoot.At(l.steel, i)); r.reinforced[i] = S(RaidLoot.At(l.reinforced, i));
        }
        return r;
    }

    /// <summary>Набег на NPC окончен: потери запоминаются (деревня от них оправится), щит короткий, рейтинг - только нападающему.</summary>
    int NpcFinishRaid(StoredAccount me, StoredAccount npc, RaidLoot loot, List<string> killed, List<string> ruinedAt, int playerShieldMinutes,
                      int outcome, int damagePct, bool counted)
    {
        int shield = 0;
        if (counted)
        {
            var l = new NpcLoss { timeUtc = Now(), loot = loot ?? new RaidLoot() };
            if (killed != null) l.killed.AddRange(killed);
            if (ruinedAt != null) l.ruinedAt.AddRange(ruinedAt);
            if (npc.npcLosses == null) npc.npcLosses = new List<NpcLoss>();
            npc.npcLosses.Add(l);
            if (npc.npcLosses.Count > 20) npc.npcLosses.RemoveRange(0, npc.npcLosses.Count - 20);
            shield = NpcShieldMinutes(playerShieldMinutes);
            DateTime until = DateTime.UtcNow.AddMinutes(shield);
            if (until > ParseUtc(npc.shieldUtc)) npc.shieldUtc = until.ToString("o");
            NpcRaidRating(me, npc, outcome, damagePct);
            Debug.Log($"[Сервер] Набег {me.profile.displayName} → NPC {npc.profile.displayName}: {loot.ShortText}, погибло защитников {(killed != null ? killed.Count : 0)}, щит NPC {shield} мин");
        }
        NpcRefresh(npc);
        return shield;
    }

    /// <summary>Рейтинг за набег на NPC: как за игрока, но вполовину, и у NPC рейтинг не меняется.</summary>
    void NpcRaidRating(StoredAccount attacker, StoredAccount npc, int outcome, int damagePct)
    {
        if (attacker == null || npc == null || outcome == 0) return;
        int ra = attacker.profile.rating, rd = npc.profile.rating;
        int dA;
        string what;
        if (outcome > 0)
        {
            dA = Math.Max(2, Leagues.RaidWin + Leagues.Bonus(ra, rd, 6));
            if (outcome == 1) dA = Math.Max(1, dA / 2);
            dA = Math.Max(1, dA / 2);
            what = outcome == 2 ? "победа" : "небольшая победа";
        }
        else
        {
            dA = -Math.Max(1, (Leagues.RaidFail + Leagues.Bonus(rd, ra, 4) / 2) / 2);
            what = "не удался";
        }
        ChangePlayerRating(attacker, dA, $"Набег на NPC {npc.profile.displayName}: {what} (разрушено {damagePct}%)");
    }

    /// <summary>Сколько настоящих игроков (без NPC).</summary>
    int RealPlayers()
    {
        int n = 0;
        foreach (var a in db.accounts) if (a != null && a.profile != null && !a.npc) n++;
        return n;
    }
}

/// <summary>
/// Расстановка NPC-деревни: Цитадель в центре (клетки -1..1, как у игрока), кольцо дорожек вокруг неё и дороги
/// к воротам, стены кольцом, башни у стен, пушки внутри, хозяйство снаружи. Набор зданий - по правилам игры
/// для этого уровня Цитадели и пути (лимиты казарм и обороны, уровни зданий, здания путей и школ).
/// </summary>
public static class NpcCity
{
    const char Free = '\0', Bld = 'B', WallC = 'W', Road = 'R';

    class Spec
    {
        public string type;
        public int w, h, level;
        public float rMin, rMax;      // где ставить: доли радиуса (внутри стен) или клетки за стенами (outside)
        public bool outside;
        public float angle = -1f;     // оборона: под каким углом (градусы), -1 - где удобно
        public int gold, wood, stone, food, iron, oats, berries, herbs, resin, leaves;
    }

    struct Cell : IEquatable<Cell>
    {
        public readonly int x, y;
        public Cell(int x, int y) { this.x = x; this.y = y; }
        public bool Equals(Cell o) { return x == o.x && y == o.y; }
        public override bool Equals(object o) { return o is Cell c && Equals(c); }
        public override int GetHashCode() { return x * 7919 + y; }
    }

    public static CityData Build(int level, int seed, int path, int school, int eng, int war, bool walls, bool river)
    {
        int L = Mathf.Clamp(level, 1, 3);
        var rng = new System.Random(seed);
        int size = L == 1 ? 50 : (L == 2 ? 54 : 60);
        int half = size / 2;
        var grid = new char[size, size];
        int R = L == 1 ? 10 : (L == 2 ? 12 : 14);      // стены (или граница застройки) - как зона Цитадели
        int outer = L == 1 ? 13 : (L == 2 ? 17 : 21);  // дальше - только лес и поле

        var c = new CityData
        {
            seed = seed, mapSize = size, path = path, school = school, eng = eng, war = war,
            day = 6 + L * 4, timeInDay = 0.35f,
        };

        bool Inside(int x, int y) { return x >= -half && y >= -half && x < size - half && y < size - half; }
        char At(int x, int y) { return Inside(x, y) ? grid[x + half, y + half] : WallC; }
        void Set(int x, int y, char v) { if (Inside(x, y)) grid[x + half, y + half] = v; }
        double Dist(float x, float y) { return Math.Sqrt(x * x + y * y); }

        void AddBuilding(string type, int x, int y, int w, int h, int lvl, Spec s = null)
        {
            for (int i = 0; i < w; i++) for (int j = 0; j < h; j++) Set(x + i, y + j, type == "Стена" || type == "Ворота" ? WallC : Bld);
            var sb = new SavedBuilding { type = type, x = x, y = y, w = w, h = h, level = Mathf.Max(1, lvl) };
            if (s != null)
            {
                sb.gold = s.gold; sb.wood = s.wood; sb.stone = s.stone; sb.food = s.food; sb.iron = s.iron;
                sb.oats = s.oats; sb.berries = s.berries; sb.herbs = s.herbs; sb.resin = s.resin; sb.leaves = s.leaves;
            }
            c.buildings.Add(sb);
        }

        // ---------- Цитадель и дороги ----------
        AddBuilding("Ратуша", -1, -1, 3, 3, L);
        string roadType = L >= 2 ? "Брусчатка" : "Дорожка";
        var roads = new List<Cell>();
        for (int x = -2; x <= 2; x++) for (int y = -2; y <= 2; y++)
            if (Math.Max(Math.Abs(x), Math.Abs(y)) == 2) roads.Add(new Cell(x, y));
        int spokeTo = walls ? R + 2 : R;
        for (int k = 3; k <= spokeTo; k++)
        {
            roads.Add(new Cell(0, k)); roads.Add(new Cell(0, -k));
            roads.Add(new Cell(k, 0)); roads.Add(new Cell(-k, 0));
        }

        // ---------- Стены: кольцо (каждая клетка касается соседней стороной - без щелей по диагонали) ----------
        var wallCells = new List<Cell>();
        var gateCells = new HashSet<Cell>();
        if (walls)
        {
            double rr = R + 0.5;
            for (int x = -R - 1; x <= R + 1; x++)
                for (int y = -R - 1; y <= R + 1; y++)
                {
                    if (Dist(x, y) >= rr) continue;
                    bool edge = false;
                    for (int dx = -1; dx <= 1 && !edge; dx++)
                        for (int dy = -1; dy <= 1 && !edge; dy++)
                            if (Dist(x + dx, y + dy) >= rr) edge = true;
                    if (edge) wallCells.Add(new Cell(x, y));
                }
            // Ворота - где кольцо пересекают дороги, по две клетки
            foreach (var w in wallCells)
            {
                bool onSpoke = (w.x == 0 || w.x == 1) && Math.Abs(w.y) >= R - 1 || (w.y == 0 || w.y == 1) && Math.Abs(w.x) >= R - 1;
                if (onSpoke) gateCells.Add(w);
            }
        }
        foreach (var r in roads) if (!wallCells.Contains(r)) Set(r.x, r.y, Road);
        int wallLvl = Mathf.Clamp(L, 1, 3);
        foreach (var w in wallCells)
        {
            if (At(w.x, w.y) == Bld) continue;
            AddBuilding(gateCells.Contains(w) ? "Ворота" : "Стена", w.x, w.y, 1, 1, wallLvl);
        }

        // ---------- Что строим ----------
        var list = Composition(L, path, school, eng, war, rng);

        // Оборона с углами: равномерно по кругу, мимо дорог
        double startAngle = rng.NextDouble() * 360.0;
        var towers = list.FindAll(s => s.angle >= 0f);
        for (int i = 0; i < towers.Count; i++)
        {
            double a = startAngle + 360.0 * i / Math.Max(1, towers.Count);
            double m = a % 90.0;
            if (m < 14.0) a += 14.0 - m; else if (m > 76.0) a -= m - 76.0;   // не на дороге к воротам
            towers[i].angle = (float)a;
        }

        // Большие здания - раньше мелких (иначе 4×4 Инженерному двору может не хватить места); оборона - первой
        var order = new List<Spec>(list);
        order.Sort((a, b) =>
        {
            int da = a.angle >= 0f ? 0 : 1, db = b.angle >= 0f ? 0 : 1;
            if (da != db) return da.CompareTo(db);
            int byArea = (b.w * b.h).CompareTo(a.w * a.h);
            return byArea != 0 ? byArea : list.IndexOf(a).CompareTo(list.IndexOf(b));
        });
        list = order;

        // ---------- Расстановка ----------
        bool Fits(int ox, int oy, int w, int h, Spec s, int margin)
        {
            for (int i = -margin; i < w + margin; i++)
                for (int j = -margin; j < h + margin; j++)
                {
                    int x = ox + i, y = oy + j;
                    bool body = i >= 0 && j >= 0 && i < w && j < h;
                    char v = At(x, y);
                    if (body)
                    {
                        if (v != Free) return false;
                        double dc = Dist(x + 0.5f, y + 0.5f);
                        if (!s.outside && dc > (walls ? R - 1.2 : R + 0.4)) return false;    // внутри стен / зоны
                        if (s.outside && (dc < R + 2.0 || dc > outer)) return false;          // снаружи стен, но не дальше поля
                        if (x < -half + 2 || y < -half + 2 || x >= size - half - 2 || y >= size - half - 2) return false;
                    }
                    else if (v == Bld || v == WallC) return false;   // проход вокруг здания (дорожка рядом - можно)
                }
            return true;
        }

        foreach (var s in list)
        {
            bool placed = false;
            for (int pass = 0; pass < 4 && !placed; pass++)
            {
                int margin = pass < 2 ? 1 : 0;
                float widen = pass % 2 == 0 ? 0f : 0.25f;
                double bestScore = double.MaxValue;
                int bx = 0, by = 0;
                double lo = s.outside ? R + 2 : s.rMin * R - widen * R, hi = s.outside ? outer : s.rMax * R + widen * R;
                double target = (lo + hi) / 2.0;
                double dirX = 0, dirY = 0;
                if (s.angle >= 0f) { dirX = Math.Cos(s.angle * Math.PI / 180.0); dirY = Math.Sin(s.angle * Math.PI / 180.0); }
                for (int ox = -outer - 2; ox <= outer + 2; ox++)
                    for (int oy = -outer - 2; oy <= outer + 2; oy++)
                    {
                        float cx = ox + s.w / 2f, cy = oy + s.h / 2f;
                        double d = Dist(cx, cy);
                        if (d < lo - 0.5 || d > hi + 0.5) continue;
                        double score;
                        if (s.angle >= 0f)
                        {
                            float px = (float)(dirX * target), py = (float)(dirY * target);
                            score = Dist(cx - px, cy - py);
                        }
                        else score = Math.Abs(d - target) * 0.6 + rng.NextDouble() * 3.0;
                        if (score >= bestScore) continue;
                        if (!Fits(ox, oy, s.w, s.h, s, margin)) continue;
                        bestScore = score; bx = ox; by = oy;
                    }
                if (bestScore < double.MaxValue) { AddBuilding(s.type, bx, by, s.w, s.h, s.level, s); placed = true; }
            }
            if (!placed) Debug.LogWarning($"[Сервер] NPC-деревня (Ур.{L}, {seed}): не нашлось места для «{s.type}»");
        }

        // Дорожки (после зданий: где дорога легла под здание - её нет)
        foreach (var r in roads)
            if (At(r.x, r.y) == Road) c.buildings.Add(new SavedBuilding { type = roadType, x = r.x, y = r.y, w = 1, h = 1, level = 1 });

        // ---------- Армия ----------
        Army(c, L, path, eng, rng);

        // ---------- Казна ----------
        Treasury(c, L, path, rng);

        int houses = 0;
        foreach (var b in c.buildings) if (b.type == "Жилой дом") houses += 3 + 2 * b.level;
        c.population = houses + c.army.Count;

        // ---------- Местность: лес по краям поля, у некоторых - река за стенами ----------
        var forest = new HashSet<Cell>();
        int blobs = 6 + rng.Next(4);
        for (int i = 0; i < blobs; i++)
        {
            double a = rng.NextDouble() * Math.PI * 2;
            double d = outer + 3 + rng.NextDouble() * 4;
            int fx = (int)Math.Round(Math.Cos(a) * d), fy = (int)Math.Round(Math.Sin(a) * d);
            int fr = 2 + rng.Next(4);
            for (int x = fx - fr; x <= fx + fr; x++)
                for (int y = fy - fr; y <= fy + fr; y++)
                    if (Dist(x - fx, y - fy) <= fr + rng.NextDouble() * 0.8 && Dist(x, y) > outer + 1.5) forest.Add(new Cell(x, y));
        }
        var water = new HashSet<Cell>();
        if (river)
        {
            int side = rng.Next(2) == 0 ? 1 : -1;   // восточнее или западнее города
            double baseX = side * (outer + 3.5);
            for (int y = -half; y < size - half; y++)
            {
                int x0 = (int)Math.Round(baseX + Math.Sin(y / 4.5 + seed) * 1.6);
                for (int x = x0; x < x0 + 2; x++) { water.Add(new Cell(x, y)); forest.Remove(new Cell(x, y)); }
            }
        }
        c.terrain = TerrainCode.Encode(size, (x, y) =>
        {
            if (At(x, y) != Free) return TerrainCode.Grass;   // под зданием и дорогой - земля
            var p = new Cell(x, y);
            if (water.Contains(p)) return TerrainCode.Water;
            if (forest.Contains(p)) return TerrainCode.Forest;
            return TerrainCode.Grass;
        });
        return c;
    }

    // ---------- Набор зданий по уровню ----------

    static Spec S(string type, int w, int h, int level, float rMin, float rMax) { return new Spec { type = type, w = w, h = h, level = level, rMin = rMin, rMax = rMax }; }
    static Spec Out(string type, int w, int h, int level) { return new Spec { type = type, w = w, h = h, level = level, outside = true }; }
    static Spec Def(string type, int level, float r) { return new Spec { type = type, w = 2, h = 2, level = level, rMin = r, rMax = r, angle = 0f }; }

    static List<Spec> Composition(int L, int path, int school, int eng, int war, System.Random rng)
    {
        var l = new List<Spec>();
        int Lv(int a, int b) { return a + rng.Next(b - a + 1); }   // уровень от a до b

        // Оборона - первой: ей нужны места у стен. Башни / магические пушки / пушки / аванпосты - в лимите обороны (3 / 7 / 12)
        int towers = L == 1 ? 3 : (L == 2 ? 5 : 6);
        for (int i = 0; i < towers; i++) l.Add(Def("Башня", L == 1 ? Lv(1, 2) : (L == 2 ? Lv(2, 3) : 3), 0.78f));
        if (L >= 2) l.Add(S("Магическая пушка", 2, 2, L == 2 ? 2 : 3, 0.35f, 0.55f));
        if (L >= 3) { l.Add(S("Магическая пушка", 2, 2, 3, 0.4f, 0.6f)); l.Add(S("Пушка", 2, 2, 3, 0.55f, 0.75f)); l.Add(S("Пушка", 2, 2, 2, 0.55f, 0.75f)); }

        // Казармы - ближе к центру (лимит 2 / 3 / 5)
        if (L == 1) { l.Add(S("Казарма", 2, 2, 2, 0.25f, 0.5f)); l.Add(S("Казарма", 2, 2, 1, 0.3f, 0.6f)); }
        if (L == 2) { l.Add(S("Казарма", 2, 2, 3, 0.25f, 0.5f)); l.Add(S("Казарма", 2, 2, 3, 0.3f, 0.55f)); l.Add(S("Казарма", 2, 2, 2, 0.3f, 0.6f)); }
        if (L == 3) { l.Add(S("Казарма", 2, 2, 4, 0.2f, 0.45f)); l.Add(S("Казарма", 2, 2, 4, 0.25f, 0.5f)); l.Add(S("Казарма", 2, 2, 3, 0.3f, 0.55f)); l.Add(S("Казарма", 2, 2, 3, 0.3f, 0.6f)); }

        // Склады и мастерские - у Цитадели
        int wh = L;   // Хранилищ 1 / 2 / 3
        for (int i = 0; i < wh; i++) l.Add(S("Хранилище", 3, 2, L == 1 ? 1 : Lv(L - 1, L), 0.2f, 0.5f));
        for (int i = 0; i < (L == 1 ? 1 : 2); i++) l.Add(S("Мастерская", 2, 2, L == 1 ? Lv(1, 2) : Lv(2, 3), 0.25f, 0.6f));
        l.Add(S("Дом целителей", 2, 2, L == 1 ? 1 : Lv(2, 3), 0.3f, 0.6f));
        if (L == 3) l.Add(S("Дом целителей", 2, 2, 2, 0.4f, 0.7f));
        l.Add(S("Курьерская", 2, 2, L == 1 ? 1 : Lv(1, L), 0.35f, 0.7f));

        // Цитадель Ур.2: снабжение и торговля
        if (L >= 2)
        {
            l.Add(S("Оружейная", 3, 2, Lv(2, 3), 0.3f, 0.6f));
            l.Add(S("Конюшня", 3, 2, Lv(2, 3), 0.45f, 0.8f));
            l.Add(S("Кухня", 2, 2, Lv(1, 2), 0.35f, 0.7f));
            l.Add(S("Торговый дом", 3, 3, Lv(1, L), 0.3f, 0.65f));
            l.Add(S("Гостевой дом", 2, 2, Lv(1, 2), 0.45f, 0.85f));
        }
        if (L >= 3) l.Add(S("Обозный двор", 3, 2, 2, 0.5f, 0.85f));

        // Здания пути (с Цитадели Ур.2) и школы / направления (с Ур.3)
        if (path == 1)
        {
            l.Add(S("Зельеварня", 2, 2, L == 2 ? 2 : 3, 0.35f, 0.7f));
            if (L >= 3) l.Add(S("Магическая оружейная", 2, 2, 2, 0.3f, 0.6f));
            if (L >= 3 && school == 2) l.Add(S("Алтарь жизни", 2, 2, 2, 0.1f, 0.4f));
        }
        else if (path == 2)
        {
            l.Add(S("Кузня мастера", 2, 2, L == 2 ? 2 : 3, 0.3f, 0.65f));
            if (L >= 3 && war == 2) l.Add(S("Двор военного строителя", 3, 2, 2, 0.35f, 0.7f));
        }
        else if (path == 4)
        {
            l.Add(S("Литейная", 3, 2, L == 2 ? 1 : 2, 0.4f, 0.8f));
            l.Add(S("Инженерный двор", 4, 4, L == 2 ? 1 : 2, 0.35f, 0.8f));
            if (L >= 3 && eng == 1) l.Add(S("Инженерная казарма", 3, 3, 2, 0.3f, 0.7f));
        }

        // Жильё - по всей деревне
        int houses = L == 1 ? 5 : (L == 2 ? 7 : 8);
        for (int i = 0; i < houses; i++) l.Add(S("Жилой дом", 2, 2, L == 1 ? Lv(1, 2) : Lv(L - 1, L), 0.3f, 0.95f));

        // Аванпосты - за стенами (лимит 0 / 1 / 3 и общий лимит обороны)
        if (L == 2) l.Add(Out("Аванпост", 2, 2, 2));
        if (L == 3) { l.Add(Out("Аванпост", 2, 2, 3)); l.Add(Out("Аванпост", 2, 2, 2)); }

        // Хозяйство: у хутора - по краю деревни, у села и крепости - за стенами, у поля
        bool outF = L >= 2;
        Spec Farm(int lv) { var s = outF ? Out("Ферма", 3, 3, lv) : S("Ферма", 3, 3, lv, 0.6f, 1f); s.food = 25 * lv; if (lv >= 3) s.oats = 10 * lv; return s; }
        Spec Saw(int lv) { var s = outF ? Out("Лесопилка", 2, 2, lv) : S("Лесопилка", 2, 2, lv, 0.6f, 1f); s.wood = 20 * lv; if (lv >= 3) s.resin = 5 * lv; if (lv >= 4) s.leaves = 12; return s; }
        Spec Mine(int lv) { var s = outF ? Out("Шахта", 2, 2, lv) : S("Шахта", 2, 2, lv, 0.6f, 1f); s.stone = 18 * lv; s.gold = 4 * lv; if (lv >= 3) s.iron = 6 * lv; return s; }
        Spec Berry(int lv) { var s = outF ? Out("Станция собирателей", 2, 2, lv) : S("Станция собирателей", 2, 2, lv, 0.6f, 1f); s.berries = 15 * lv; return s; }
        if (L == 1) { l.Add(Farm(2)); l.Add(Farm(1)); l.Add(Saw(2)); l.Add(Mine(Lv(1, 2))); l.Add(Berry(1)); }
        if (L == 2) { l.Add(Farm(3)); l.Add(Farm(3)); l.Add(Saw(3)); l.Add(Saw(2)); l.Add(Mine(3)); l.Add(Mine(2)); l.Add(Berry(2)); }
        if (L == 3) { l.Add(Farm(4)); l.Add(Farm(3)); l.Add(Farm(3)); l.Add(Saw(4)); l.Add(Saw(3)); l.Add(Mine(3)); l.Add(Mine(3)); l.Add(Berry(3)); }
        return l;
    }

    // ---------- Защитники ----------

    static void Army(CityData c, int L, int path, int eng, System.Random rng)
    {
        var barracks = c.buildings.FindAll(b => b.type == "Казарма");
        barracks.Sort((a, b) => b.level.CompareTo(a.level));
        SavedBuilding engB = c.buildings.Find(b => b.type == "Инженерная казарма");
        int k = 0;
        void Add(string unit, int n, int minLvl = 1, bool engineer = false)
        {
            for (int i = 0; i < n; i++)
            {
                SavedBuilding home = engineer ? engB : null;
                if (home == null)
                {
                    var ok = barracks.FindAll(b => b.level >= minLvl);
                    if (ok.Count == 0) return;   // такой казармы нет - такого бойца нет
                    home = ok[k++ % ok.Count];
                }
                c.army.Add(new SavedSoldier { unit = unit, bx = home.x, by = home.y, hearty = L >= 3 && rng.Next(3) == 0 });
            }
        }
        if (L == 1)
        {
            Add("Мечник", 4); Add("Лучник", 3); Add("Рыцарь", 1); Add("Тяжёлый Рыцарь", 1, 2); Add("Разведчик", 1, 2);
        }
        else if (L == 2)
        {
            Add("Мечник", 5); Add("Лучник", 4); Add("Рыцарь", 2); Add("Тяжёлый Рыцарь", 1, 2); Add("Полевой маг", 1, 3); Add("Разведчик", 1, 2);
            if (path == 1) { Add("Маг-отравитель", 3, 2); Add("Маг стихий", 1, 3); }
            else if (path == 2) { Add("Поджигатель", 3, 3); Add("Полевая пушка", 1, 3); }
            else { Add("Таран", 2, 2); Add("Лучник", 2); }
        }
        else
        {
            Add("Мечник", 6); Add("Лучник", 5); Add("Рыцарь", 3); Add("Тяжёлый Рыцарь", 2, 2); Add("Полевой маг", 2, 3);
            Add("Всадник", 2, 4); Add("Конный лучник", 2, 4); Add("Разведчик", 1, 2);
            if (path == 1) { Add("Маг стихий", 2, 3); Add("Маг-отравитель", 3, 2); }
            else if (path == 2) { Add("Поджигатель", 3, 3); Add("Полевая пушка", 2, 3); }
            else { if (eng == 1) Add("Катапульта", 2, 1, true); Add("Таран", 3, 2); }
        }
    }

    // ---------- Казна ----------

    static void Treasury(CityData c, int L, int path, System.Random rng)
    {
        int V(int n) { return (int)Math.Round(n * (0.85 + rng.NextDouble() * 0.3)); }   // ±15%, у каждой деревни своё
        void Ref(RefinedKind k, int tier, int n) { if (n > 0) c.refined.Add(new SavedRefined { kind = (int)k, tier = tier, amount = n }); }
        if (L == 1)
        {
            c.gold = V(700); c.wood = V(600); c.stone = V(450); c.food = V(500); c.iron = V(40); c.berries = V(60); c.herbs = V(20);
            Ref(RefinedKind.Planks, 0, V(15)); Ref(RefinedKind.StonePlates, 0, V(10));
        }
        else if (L == 2)
        {
            c.gold = V(2000); c.wood = V(1500); c.stone = V(1200); c.food = V(1200); c.iron = V(150); c.oats = V(120);
            c.berries = V(120); c.herbs = V(80); c.resin = V(60); c.potions = path == 1 ? V(12) : 0;
            Ref(RefinedKind.Planks, 0, V(40)); Ref(RefinedKind.Planks, 1, V(15));
            Ref(RefinedKind.StonePlates, 0, V(30)); Ref(RefinedKind.StonePlates, 1, V(10)); Ref(RefinedKind.Steel, 0, V(10));
        }
        else
        {
            c.gold = V(4500); c.wood = V(3500); c.stone = V(3000); c.food = V(2500); c.iron = V(400); c.oats = V(300);
            c.berries = V(250); c.herbs = V(200); c.resin = V(200); c.potions = path == 1 ? V(25) : V(6); c.parts = path == 4 ? V(60) : 0;
            Ref(RefinedKind.Planks, 0, V(80)); Ref(RefinedKind.Planks, 1, V(40)); Ref(RefinedKind.Planks, 2, V(10));
            Ref(RefinedKind.StonePlates, 0, V(60)); Ref(RefinedKind.StonePlates, 1, V(30)); Ref(RefinedKind.StonePlates, 2, V(10));
            Ref(RefinedKind.Steel, 0, V(30)); Ref(RefinedKind.Steel, 1, V(15)); Ref(RefinedKind.Steel, 2, V(5));
            Ref(RefinedKind.ReinforcedPlates, 0, V(10)); Ref(RefinedKind.ReinforcedPlates, 1, V(5));
        }
    }
}
