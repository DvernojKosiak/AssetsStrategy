using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Клановая война на сервере - отдельная игра на общей карте, вживую: бойцы ходят и дерутся, башни и Цитадели стреляют,
/// казармы нанимают, здания и источники приносят доход, участки захватываются, здания чинят, оружие и броню улучшают.
/// Без Unity - только данные. Сервер вызывает Step несколько раз в секунду (и когда игроков нет в игре);
/// игра получает Frame (что видит её сторона) и шлёт Command.
/// Состояние (State) целиком сохраняется в файл и подхватывается после перезапуска сервера.
///
/// Быстродействие (klan-voina-1): карта бывает до 2000×2000 клеток, поэтому дальний путь ищется по крупной сетке
/// (блоки 5×5 клеток), а подробный - только в небольшом окне рядом с бойцом. На всё вместе за шаг - ограниченный
/// бюджет: кто не успел, ищет путь в следующем шаге. Сервер не «зависает» из-за войны.
/// </summary>
public class WarSim
{
    // ---------- Состояние (сохраняется) ----------

    [Serializable]
    public class State
    {
        public int seed, rows;
        public int gc, nc, mx, my;    // форма карты (WarGeo)
        public bool fast;
        public string nameA, nameB;
        public double time;
        public int nextId = 1;
        public int version = 1;
        public List<SimCity> cities = new List<SimCity>();
        public List<SimBld> blds = new List<SimBld>();
        public List<SimUnit> units = new List<SimUnit>();
        public string plots = "";
        public List<float> capture = new List<float>();     // прогресс захвата участка (0..1)
        public List<int> captureSide = new List<int>();
        public List<string> events = new List<string>();
        public int eventN;                                    // номер последнего события
        public bool started;                                  // стартовые отряды выданы
    }

    [Serializable]
    public class SimCity
    {
        public int idx;
        public string accountId, name;
        public int side, plotX, plotY;
        public bool fallen;
        public int path;           // путь развития (PathKind)
        public int hall;
        public int mapSize;        // местность города (реки, лес, мосты) - она же на его участке карты войны
        public string terrain;
        public float gold, food, wood, stone, iron;
        public List<string> queue = new List<string>();
        public List<int> queueSquad = new List<int>();
        public float queueLeft;
        public float earnedGold, earnedFood, earnedWood, earnedStone, earnedIron;   // всего получено (для итогов)
        public int weaponLv, armorLv;
        public List<string> squadNames = new List<string>();
        public int kills, losses;
        public List<int> income = new List<int>();   // доход в минуту (последний расчёт): золото, еда, дерево, камень, железо
    }

    [Serializable]
    public class SimBld
    {
        public int id, city;
        public string type;
        public int level, x, y, w, h;
        public float hp, maxHp, cd;
        public bool seen1, seen2;
        public float repair;       // сколько здоровья ещё вернут (уже оплачено)
    }

    [Serializable]
    public class SimUnit
    {
        public int id, city, side, type;
        public float x, y, hp;
        public int order;              // WarCommand: 0 стоит, Move, Attack, AttackMove
        public float tx, ty;           // куда идёт / где стоит
        public int target = -1;        // >= 0 боец, < -1 здание (-id - 1)
        [NonSerialized] public List<int> path = new List<int>();   // точки пути (номера клеток); после перезапуска считается заново
        [NonSerialized] public int pathPos;
        [NonSerialized] public int fixedAt = -1;                   // для какой точки пути уже искали обход
        public float cd, think, repath;
        public double strikeAt = -9;   // когда ударил (игра покажет выпад и стрелу)
        public int lastTarget = -1;
        public float dmgMul = 1f;
        public float maxHp;            // 0 - как у вида войск (без брони)
        public int squad;              // отряд 1..6 (0 - без отряда)
        public bool hold;              // «стоять»: не гоняться за врагами, бить тех, кто подошёл
        public int hitBy = -1;         // чей город ударил последним (для счёта убитых)
        // klan-voina-14: своя казарма в деревне (id здания на карте, -1 - нет) и снаряжение бойца (SavedSoldier в JSON)
        public int home = -1;
        public string kit;
    }

    public readonly State S;
    public readonly WarGeo G;

    // ---------- Рабочие данные (строятся заново) ----------

    readonly int W, H;
    readonly byte[] terrain;
    /// <summary>Источники в нейтральных землях (не сохраняются: каждый раз те же, из seed и местности).</summary>
    public readonly List<WarSource> Sources;
    readonly Dictionary<int, WarSource> sourceAt = new Dictionary<int, WarSource>();   // участок -> источник
    int[] occ;                 // id здания + 1 на клетке (0 - пусто)
    int[] plotCity;            // участок -> город на нём (-1 - нет)
    readonly Dictionary<int, SimUnit> unitById = new Dictionary<int, SimUnit>();
    readonly Dictionary<int, SimBld> bldById = new Dictionary<int, SimBld>();
    readonly Random rng;

    const int Bucket = 8;
    int bw, bh;
    List<SimUnit>[] buckets;

    // Обзор: клетки 2x2, отдельно для каждой стороны
    const int VisCell = 2;
    int vw, vh;
    bool[] vis1, vis2, base1, base2;
    float visTimer, incomeTimer, captureTimer, repairTimer;

    WarStage stage = WarStage.Prepare;
    public WarStage Stage
    {
        get { return stage; }
        set { if (value != stage) { stage = value; ClearEdgeCache(); RebuildBaseVision(); } }
    }

    public WarSim(State s)
    {
        S = s;
        G = WarGeo.Of(s.rows, s.gc, s.nc, s.mx, s.my);
        W = G.W;
        H = G.H;
        terrain = WarRules.Terrain(s.seed, G, CityViews(s));
        Sources = WarRules.Sources(s.seed, G, terrain);
        foreach (var src in Sources) sourceAt[src.plot] = src;
        rng = new Random(s.seed ^ (int)(s.time * 10));
        if (string.IsNullOrEmpty(S.plots) || S.plots.Length != G.Cols * G.Rows) InitPlots();
        while (S.capture.Count < G.Cols * G.Rows) { S.capture.Add(0); S.captureSide.Add(0); }
        foreach (var c in S.cities)
        {
            if (c.queueSquad == null) c.queueSquad = new List<int>();
            while (c.queueSquad.Count < c.queue.Count) c.queueSquad.Add(0);
            if (c.squadNames == null) c.squadNames = new List<string>();
            while (c.squadNames.Count < WarRules.Squads) c.squadNames.Add("");
            if (c.income == null) c.income = new List<int>();
            while (c.income.Count < 5) c.income.Add(0);
        }
        Rebuild();
    }

    static List<WarCityView> CityViews(State s)
    {
        var list = new List<WarCityView>();
        foreach (var c in s.cities) list.Add(View(c));
        return list;
    }

    static WarCityView View(SimCity c)
    {
        return new WarCityView { idx = c.idx, accountId = c.accountId, name = c.name, side = c.side, plotX = c.plotX, plotY = c.plotY, fallen = c.fallen, hall = c.hall,
                                 mapSize = c.mapSize, terrain = c.terrain };
    }

    void InitPlots()
    {
        var sb = new StringBuilder();
        for (int py = 0; py < G.Rows; py++)
            for (int px = 0; px < G.Cols; px++)
                sb.Append((char)('0' + G.ZoneOfPlot(px, py)));
        S.plots = sb.ToString();
    }

    void Rebuild()
    {
        occ = new int[W * H];
        bldById.Clear();
        foreach (var b in S.blds)
        {
            bldById[b.id] = b;
            if (b.hp > 0) Occupy(b, true);
        }
        unitById.Clear();
        foreach (var u in S.units)
        {
            if (u.path == null) u.path = new List<int>();
            u.fixedAt = -1;
            unitById[u.id] = u;
        }
        plotCity = new int[G.Cols * G.Rows];
        for (int i = 0; i < plotCity.Length; i++) plotCity[i] = -1;
        foreach (var c in S.cities)
            for (int py = c.plotY; py < c.plotY + WarRules.CityPlots && py < G.Rows; py++)
                for (int px = c.plotX; px < c.plotX + WarRules.CityPlots && px < G.Cols; px++)
                    plotCity[py * G.Cols + px] = c.idx;
        bw = (W + Bucket - 1) / Bucket;
        bh = (H + Bucket - 1) / Bucket;
        buckets = new List<SimUnit>[bw * bh];
        for (int i = 0; i < buckets.Length; i++) buckets[i] = new List<SimUnit>();
        vw = (W + VisCell - 1) / VisCell;
        vh = (H + VisCell - 1) / VisCell;
        vis1 = new bool[vw * vh];
        vis2 = new bool[vw * vh];
        base1 = new bool[vw * vh];
        base2 = new bool[vw * vh];
        BuildCoarse();
        RebuildBaseVision();
        UpdateVision();
    }

    void Occupy(SimBld b, bool on)
    {
        for (int y = b.y; y < b.y + b.h; y++)
            for (int x = b.x; x < b.x + b.w; x++)
                if (x >= 0 && y >= 0 && x < W && y < H) occ[y * W + x] = on ? b.id + 1 : 0;
        ClearEdgeCache();
    }

    // ---------- Создание ----------

    /// <summary>Новая война: города по местам схемы (у каждого - снимок построек из домашней деревни).</summary>
    public static State Create(int seed, WarGeo g, bool fast, List<SimCity> cities, List<List<SavedBuilding>> buildings)
    {
        var s = new State { seed = seed, rows = g.Rows, gc = g.gc, nc = g.neutral, mx = g.mx, my = g.my, fast = fast };
        for (int i = 0; i < cities.Count; i++)
        {
            SimCity c = cities[i];
            c.idx = i;
            // Ресурсы города - в живой деревне (военный город игрока); здесь копится только доход с карты: участки и источники
            c.gold = c.food = c.wood = c.stone = c.iron = 0f;
            s.cities.Add(c);
            int ox = c.plotX * WarRules.Plot, oy = c.plotY * WarRules.Plot;
            int half = WarRules.CitySize / 2;
            foreach (var sb in buildings[i])
            {
                if (sb == null || string.IsNullOrEmpty(sb.type) || WarRules.Skipped(sb.type)) continue;
                int bw = sb.w, bh = sb.h;
                if (bw <= 0 || bh <= 0) WarRules.DefaultSize(sb.type, out bw, out bh);
                int x = ox + sb.x + half, y = oy + sb.y + half;
                if (x < ox || y < oy || x + bw > ox + WarRules.CitySize || y + bh > oy + WarRules.CitySize) continue;   // за краем города
                int lv = Math.Max(1, sb.level);
                float hp = WarRules.BuildingHp(sb.type, lv);
                s.blds.Add(new SimBld { id = s.nextId++, city = i, type = sb.type, level = lv, x = x, y = y, w = bw, h = bh, hp = hp, maxHp = hp,
                                        seen1 = c.side == 1, seen2 = c.side == 2 });
                if (sb.type == WarRules.HallName) c.hall = Math.Max(c.hall, lv);
            }
        }
        return s;
    }

    /// <summary>Стартовые отряды каждому городу (один раз, когда карта открылась): 6 мечников и 4 лучника.</summary>
    public void GiveStartArmy()
    {
        if (S.started) return;
        S.started = true;
        int sword = WarRules.UnitIndex("Мечник"), archer = WarRules.UnitIndex("Лучник");
        foreach (var c in S.cities)
        {
            if (c.fallen) continue;
            if (c.squadNames.Count >= 2) { c.squadNames[0] = "Мечники"; c.squadNames[1] = "Лучники"; }
            for (int i = 0; i < 6; i++) Spawn(c, sword).squad = 1;
            for (int i = 0; i < 4; i++) Spawn(c, archer).squad = 2;
        }
        S.version++;
    }

    // ---------- Шаг расчёта ----------

    public void Step(float dt)
    {
        S.time += dt;
        bool active = Stage >= WarStage.Prepare && Stage < WarStage.Finished;

        foreach (var list in buckets) list.Clear();
        foreach (var u in S.units)
        {
            int bi = BucketOf(u.x, u.y);
            if (bi >= 0) buckets[bi].Add(u);
        }

        if (active)
        {
            nodeBudget = NodesPerStep;
            healedNow.Clear();   // final-13: сколько лекарей уже лечат каждого бойца в этом шаге
            foreach (var u in S.units) StepUnit(u, dt);
            foreach (var b in S.blds) StepBuilding(b, dt);
            Separate(dt);
            RemoveDead();
            StepTraining(dt);

            incomeTimer += dt;
            if (incomeTimer >= 1f) { Income(incomeTimer); incomeTimer = 0f; }
            captureTimer += dt;
            if (captureTimer >= 1f) { Capture(captureTimer); captureTimer = 0f; }
            repairTimer += dt;
            if (repairTimer >= 1f) { StepRepair(repairTimer); repairTimer = 0f; }
        }

        visTimer += dt;
        if (visTimer >= 0.5f) { visTimer = 0f; UpdateVision(); }
    }

    int BucketOf(float x, float y)
    {
        int bx = (int)(x / Bucket), by = (int)(y / Bucket);
        if (bx < 0 || by < 0 || bx >= bw || by >= bh) return -1;
        return by * bw + bx;
    }

    WarUnitDef Def(SimUnit u) { return WarRules.Units[Math.Max(0, Math.Min(WarRules.Units.Length - 1, u.type))]; }
    float ArmorMul(SimUnit u) { return 1f + WarRules.ResearchStep * CityOf(u).armorLv; }
    float WeaponMul(SimUnit u) { return 1f + WarRules.ResearchStep * CityOf(u).weaponLv; }
    SimCity CityOf(SimUnit u) { return S.cities[Math.Max(0, Math.Min(S.cities.Count - 1, u.city))]; }
    float MaxHp(SimUnit u) { return (u.maxHp > 0f ? u.maxHp : Def(u).hp) * ArmorMul(u); }

    void StepUnit(SimUnit u, float dt)
    {
        if (u.hp <= 0) return;
        WarUnitDef d = Def(u);
        if (u.cd > 0) u.cd -= dt;
        u.think -= dt;
        u.repath -= dt;

        // Лекарь лечит раненых рядом
        if (d.heal > 0) Heal(u, d, dt);

        // Цель: приказ или сам замечает врага
        if (u.order != WarCommand.Move && u.think <= 0f)
        {
            u.think = 0.5f + (float)rng.NextDouble() * 0.2f;
            if (u.order == WarCommand.Attack && !TargetAlive(u.target)) { u.order = 0; u.target = -1; u.tx = u.x; u.ty = u.y; }
            if (u.order != WarCommand.Attack)
            {
                int t = FindTarget(u, d);
                if (t != u.target) { u.target = t; u.repath = 0f; }
                // Стоящий боец не уходит далеко от своего места
                if (u.order == 0 && t != -1 && Dist(u.tx, u.ty, u.x, u.y) > 14f) { u.target = -1; MoveTo(u, u.tx, u.ty, false); }
            }
        }

        if (u.order != WarCommand.Move && u.target != -1 && TargetAlive(u.target))
        {
            TargetPoint(u, u.target, out float px, out float py, out float dist);
            float minR = d.minRange;
            if (dist <= d.range && dist >= minR - 0.01f)
            {
                u.path.Clear();
                if (u.cd <= 0f) Strike(u, d, u.target, px, py);
                return;
            }
            if (u.hold && u.order != WarCommand.Attack) return;   // «стоять»: за врагом не бежим
            // Подходим к цели (путь пересчитываем, когда цель ушла)
            if (u.repath <= 0f || u.path.Count == 0 || u.pathPos >= u.path.Count)
            {
                u.repath = 1.5f;
                if (Dist(u.x, u.y, px, py) < 4f && LineClear(u.side, u.x, u.y, px, py)) u.path.Clear();
                else if (!FindPath(u, px, py, out bool got)) u.repath = 0.3f;
                else if (!got) u.repath = 3.5f;   // не дойти (за стеной) - не пересчитываем каждую секунду
            }
            if (u.path.Count == 0 || u.pathPos >= u.path.Count) StepToward(u, px, py, d.speed * GroundMul(u, d) * dt);
            else FollowPath(u, d.speed * GroundMul(u, d) * dt);
            return;
        }

        if (u.target != -1 && !TargetAlive(u.target)) u.target = -1;

        // Идём по пути
        if (u.path.Count > 0 && u.pathPos < u.path.Count)
        {
            FollowPath(u, d.speed * GroundMul(u, d) * dt);
            if (u.pathPos >= u.path.Count)
            {
                if (u.order == WarCommand.Move || u.order == WarCommand.AttackMove) u.order = 0;
            }
        }
        else if (u.order == WarCommand.Move || u.order == WarCommand.AttackMove || (u.order == 0 && u.target == -1))
        {
            float left = Dist(u.x, u.y, u.tx, u.ty);
            if (left > 2f && u.repath <= 0f)
            {
                u.repath = 3f;
                if (!FindPath(u, u.tx, u.ty, out bool got)) u.repath = 0.3f;
                else if (!got) u.repath = 8f;   // дальше не пройти (стены, лес): стоим у края и бьём, что рядом
            }
            else if (left > 0.3f) StepToward(u, u.tx, u.ty, d.speed * GroundMul(u, d) * dt);
            else if (u.order != 0) u.order = 0;
        }
    }

    float GroundMul(SimUnit u, WarUnitDef d)
    {
        int x = (int)u.x, y = (int)u.y;
        if (x < 0 || y < 0 || x >= W || y >= H) return 1f;
        return WarRules.SpeedMul(terrain[y * W + x], d.mounted);
    }

    bool TargetAlive(int t)
    {
        if (t >= 0) return unitById.TryGetValue(t, out SimUnit e) && e.hp > 0;
        if (t < -1) return bldById.TryGetValue(-t - 1, out SimBld b) && b.hp > 0;
        return false;
    }

    void TargetPoint(SimUnit u, int t, out float px, out float py, out float dist)
    {
        if (t >= 0)
        {
            SimUnit e = unitById[t];
            px = e.x; py = e.y;
            dist = Dist(u.x, u.y, px, py);
            return;
        }
        SimBld b = bldById[-t - 1];
        px = Clamp(u.x, b.x, b.x + b.w);
        py = Clamp(u.y, b.y, b.y + b.h);
        dist = Dist(u.x, u.y, px, py);
    }

    int FindTarget(SimUnit u, WarUnitDef d)
    {
        float reach = u.hold ? d.range + 0.6f : Math.Max(WarRules.AggroRange, d.range + 1f);
        int best = -1;
        float bestD = float.MaxValue;
        if (d.ram && !u.hold) reach = Math.Max(reach, 12f);   // таран ищет стены дальше
        ForNear(u.x, u.y, reach, e =>
        {
            if (d.ram || e.side == u.side || e.hp <= 0) return;   // таран людей не трогает
            float dd = Dist(u.x, u.y, e.x, e.y);
            if (dd > reach || !SeenBy(u.side, e.x, e.y)) return;
            if (dd < bestD) { bestD = dd; best = e.id; }
        });
        if (best != -1) return best;

        // Врагов рядом нет - вражеские здания поблизости (башни - в первую очередь)
        int cx = (int)u.x, cy = (int)u.y, r = (int)reach + 2;
        float bestB = float.MaxValue;
        for (int y = Math.Max(0, cy - r); y <= Math.Min(H - 1, cy + r); y++)
            for (int x = Math.Max(0, cx - r); x <= Math.Min(W - 1, cx + r); x++)
            {
                int o = occ[y * W + x];
                if (o == 0) continue;
                SimBld b = bldById[o - 1];
                if (b.hp <= 0 || S.cities[b.city].side == u.side) continue;
                float dd = Dist(u.x, u.y, x + 0.5f, y + 0.5f);
                if (WarRules.Shoots(b.type, b.level, out _, out _, out _, out _)) dd -= 3f;
                if (d.ram && (b.type == WarRules.WallName || b.type == WarRules.GateName)) dd -= 6f;   // таран - к стенам
                if (dd < bestB && dd <= reach) { bestB = dd; best = -b.id - 1; }
            }
        return best;
    }

    void Strike(SimUnit u, WarUnitDef d, int t, float px, float py)
    {
        u.cd = d.cd;
        u.strikeAt = S.time;
        u.lastTarget = t;
        float dmg = d.dmg * u.dmgMul * WeaponMul(u);
        if (d.aoe > 0f)
        {
            // Удар по площади: всем врагам рядом с точкой и зданиям
            ForNear(px, py, d.aoe, e =>
            {
                if (e.side != u.side && Dist(e.x, e.y, px, py) <= d.aoe) { e.hp -= dmg; e.hitBy = u.city; }
            });
            if (t < -1) HitBuilding(bldById[-t - 1], dmg * d.bldMul, u.side);
            return;
        }
        if (t >= 0) { if (!d.ram) { SimUnit e = unitById[t]; e.hp -= dmg; e.hitBy = u.city; } }   // таран по бойцам не бьёт
        else
        {
            SimBld hb = bldById[-t - 1];
            float bm = d.ram && (hb.type == WarRules.WallName || hb.type == WarRules.GateName) ? d.wallMul : d.bldMul;
            HitBuilding(hb, dmg * bm, u.side);
        }
    }

    void HitBuilding(SimBld b, float dmg, int bySide)
    {
        if (b.hp <= 0) return;
        b.hp -= dmg;
        if (b.hp > 0) return;
        b.hp = 0;
        b.repair = 0f;
        Occupy(b, false);
        SimCity c = S.cities[b.city];
        if (b.type == WarRules.HallName && !c.fallen) CityFalls(c, bySide);
    }

    void CityFalls(SimCity c, int bySide)
    {
        c.fallen = true;
        c.queue.Clear();
        c.queueSquad.Clear();
        for (int py = c.plotY; py < c.plotY + WarRules.CityPlots && py < G.Rows; py++)
            for (int px = c.plotX; px < c.plotX + WarRules.CityPlots && px < G.Cols; px++)
                SetPlot(py * G.Cols + px, bySide);
        // Половина казны города достаётся победителям (поровну их городам)
        var winners = S.cities.FindAll(x => x.side == bySide && !x.fallen);
        if (winners.Count > 0)
        {
            float k = 0.5f / winners.Count;
            foreach (var w in winners)
            {
                w.gold += c.gold * k; w.food += c.food * k; w.wood += c.wood * k; w.stone += c.stone * k; w.iron += c.iron * k;
                w.earnedGold += c.gold * k; w.earnedFood += c.food * k; w.earnedWood += c.wood * k; w.earnedStone += c.stone * k; w.earnedIron += c.iron * k;
            }
        }
        c.gold = c.food = c.wood = c.stone = c.iron = 0;
        foreach (var b in S.blds) if (b.city == c.idx) b.repair = 0f;
        Event($"Цитадель города {c.name} разрушена — город пал");
        S.version++;
    }

    void Heal(SimUnit u, WarUnitDef d, float dt)
    {
        SimUnit best = null;
        float bestK = 2f;
        ForNear(u.x, u.y, 4f, e =>
        {
            if (e.side != u.side || e.hp <= 0 || e == u) return;
            float k = e.hp / MaxHp(e);
            if (k >= 1f) return;
            healedNow.TryGetValue(e.id, out int already);
            k += already * 0.25f;   // final-13: сначала тех, кого ещё никто не лечит
            if (k < bestK && Dist(e.x, e.y, u.x, u.y) <= 4f) { bestK = k; best = e; }
        });
        if (best == null) return;
        // final-13: «усталость от лечения». Каждый следующий лекарь на того же бойца лечит слабее:
        // 100%, 60%, 35%, дальше 15%. Толпа магов больше не делает бойца бессмертным (лимита на число магов нет)
        healedNow.TryGetValue(best.id, out int n);
        healedNow[best.id] = n + 1;
        float k = n == 0 ? 1f : n == 1 ? 0.6f : n == 2 ? 0.35f : 0.15f;
        best.hp = Math.Min(MaxHp(best), best.hp + d.heal * k * dt);
    }

    readonly Dictionary<int, int> healedNow = new Dictionary<int, int>();

    void StepBuilding(SimBld b, float dt)
    {
        if (b.hp <= 0) return;
        if (!WarRules.Shoots(b.type, b.level, out float range, out float dmg, out float cd, out float aoe)) return;
        SimCity c = S.cities[b.city];
        if (c.fallen) return;
        if (b.cd > 0) { b.cd -= dt; return; }
        float cx = b.x + b.w / 2f, cy = b.y + b.h / 2f;
        SimUnit best = null;
        float bestD = float.MaxValue;
        ForNear(cx, cy, range, e =>
        {
            if (e.side == c.side || e.hp <= 0) return;
            float dd = Dist(cx, cy, e.x, e.y);
            if (dd <= range && dd < bestD) { bestD = dd; best = e; }
        });
        if (best == null) { b.cd = 0.4f; return; }   // никого - посмотрим чуть позже
        b.cd = cd;
        shots.Add(new Shot { from = b.id, x = best.x, y = best.y, at = S.time });
        if (aoe > 0)
        {
            float tx = best.x, ty = best.y;
            ForNear(tx, ty, aoe, e => { if (e.side != c.side && Dist(e.x, e.y, tx, ty) <= aoe) { e.hp -= dmg; e.hitBy = c.idx; } });
        }
        else { best.hp -= dmg; best.hitBy = c.idx; }
    }

    /// <summary>Выстрелы башен за последний шаг (игра рисует снаряды).</summary>
    public struct Shot { public int from; public float x, y; public double at; }
    readonly List<Shot> shots = new List<Shot>();
    readonly List<Shot> recentShots = new List<Shot>();
    readonly List<Shot> recentDead = new List<Shot>();

    void RemoveDead()
    {
        recentShots.AddRange(shots);
        recentShots.RemoveAll(x => S.time - x.at > 1.0);   // игра спрашивает раз в 0.25-0.6 с и сама убирает повторы
        shots.Clear();
        recentDead.RemoveAll(x => S.time - x.at > 1.2);
        for (int i = S.units.Count - 1; i >= 0; i--)
        {
            SimUnit u = S.units[i];
            if (u.hp > 0) continue;
            S.units.RemoveAt(i);
            unitById.Remove(u.id);
            recentDead.Add(new Shot { from = u.id, x = u.x, y = u.y, at = S.time });
            CityOf(u).losses++;
            if (u.hitBy >= 0 && u.hitBy < S.cities.Count && S.cities[u.hitBy].side != u.side) S.cities[u.hitBy].kills++;
        }
    }

    // Бойцы расталкивают друг друга, чтобы не стоять в одной точке
    void Separate(float dt)
    {
        foreach (var u in S.units)
        {
            if (u.hp <= 0) continue;
            float pushX = 0, pushY = 0;
            ForNear(u.x, u.y, 0.7f, e =>
            {
                if (e == u) return;
                float dx = u.x - e.x, dy = u.y - e.y;
                float dd = (float)Math.Sqrt(dx * dx + dy * dy);
                if (dd >= 0.7f) return;
                if (dd < 0.01f) { dx = (float)rng.NextDouble() - 0.5f; dy = (float)rng.NextDouble() - 0.5f; dd = 0.5f; }
                float k = (0.7f - dd) / 0.7f;
                pushX += dx / dd * k;
                pushY += dy / dd * k;
            });
            if (pushX == 0 && pushY == 0) continue;
            float nx = u.x + pushX * dt * 1.5f, ny = u.y + pushY * dt * 1.5f;
            if (Walkable(u.side, (int)nx, (int)ny)) { u.x = nx; u.y = ny; }
        }
    }

    // ---------- Движение ----------

    void FollowPath(SimUnit u, float step)
    {
        int guard = 0;
        while (step > 0f && u.pathPos < u.path.Count && guard++ < 8)
        {
            int c = u.path[u.pathPos];
            float px = c % W + 0.5f, py = c / W + 0.5f;
            if (!Walkable(u.side, c % W, c / W) && u.pathPos == u.path.Count - 1 && occ[c] == 0) { u.path.Clear(); u.repath = 0f; return; }   // зона закрылась
            float dd = Dist(u.x, u.y, px, py);
            if (dd <= step) { u.x = px; u.y = py; step -= dd; u.pathPos++; continue; }
            float nx = u.x + (px - u.x) / dd * step, ny = u.y + (py - u.y) / dd * step;
            if (Walkable(u.side, (int)nx, (int)ny)) { u.x = nx; u.y = ny; step = 0f; continue; }
            // Впереди препятствие (между точками крупного пути): обходим подробным путём до этой точки, один раз
            if (u.fixedAt != u.pathPos && Detour(u, c)) { u.fixedAt = u.pathPos; continue; }
            // Не вышло - скользим вдоль препятствия, а если и это никак - бросаем точку
            if (Walkable(u.side, (int)nx, (int)u.y)) { u.x = nx; step = 0f; continue; }
            if (Walkable(u.side, (int)u.x, (int)ny)) { u.y = ny; step = 0f; continue; }
            u.pathPos++;
            if (u.pathPos >= u.path.Count) u.repath = 0f;
            step = 0f;
        }
    }

    // Подробный путь от бойца до точки пути (вставляется перед ней)
    bool Detour(SimUnit u, int cell)
    {
        if (nodeBudget <= 0) return false;
        int gx = cell % W, gy = cell / W, sx = (int)u.x, sy = (int)u.y;
        var tmp = new List<int>();
        if (!FineSearch(u.side, sx, sy, gx, gy, 18, 2500, tmp, true) || tmp.Count == 0) return false;
        tmp.RemoveAt(tmp.Count - 1);   // последняя - сама точка, она уже в пути
        u.path.InsertRange(u.pathPos, tmp);
        return true;
    }

    void StepToward(SimUnit u, float px, float py, float step)
    {
        float dd = Dist(u.x, u.y, px, py);
        if (dd < 0.05f) return;
        float k = Math.Min(1f, step / dd);
        float nx = u.x + (px - u.x) * k, ny = u.y + (py - u.y) * k;
        if (Walkable(u.side, (int)nx, (int)ny)) { u.x = nx; u.y = ny; }
        else if (Walkable(u.side, (int)nx, (int)u.y)) u.x = nx;
        else if (Walkable(u.side, (int)u.x, (int)ny)) u.y = ny;
    }

    /// <summary>По прямой можно пройти (цель-здание само по себе не мешает).</summary>
    bool LineClear(int side, float ax, float ay, float bx, float by)
    {
        float dd = Dist(ax, ay, bx, by);
        int n = (int)(dd * 2) + 1;
        for (int i = 1; i < n; i++)
        {
            float t = i / (float)n;
            int x = (int)(ax + (bx - ax) * t), y = (int)(ay + (by - ay) * t);
            if (x < 0 || y < 0 || x >= W || y >= H) return false;
            if (!Walkable(side, x, y) && occ[y * W + x] == 0) return false;
        }
        return true;
    }

    bool LineWalk(int side, int ax, int ay, int bx, int by)
    {
        int dx = Math.Abs(bx - ax), dy = Math.Abs(by - ay);
        int n = Math.Max(dx, dy);
        for (int i = 1; i < n; i++)
        {
            int x = ax + (bx - ax) * i / n, y = ay + (by - ay) * i / n;
            if (!Walkable(side, x, y)) return false;
        }
        return true;
    }

    public bool Walkable(int side, int x, int y)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return false;
        int i = y * W + x;
        if (!WarRules.Passable(terrain[i])) return false;
        int o = occ[i];
        if (o != 0)
        {
            SimBld b = bldById[o - 1];
            // final-13-fix-2: по фермам ходят, как в деревне (раньше в войне кланов ферма была стеной)
            if (b.type != WarRules.FarmName && (b.type != WarRules.GateName || S.cities[b.city].side != side)) return false;   // свои ворота открыты
        }
        return WarRules.CanEnter(side, G.ZoneOfCell(x, y), Stage);
    }

    void MoveTo(SimUnit u, float x, float y, bool attackMove)
    {
        u.tx = x; u.ty = y;
        u.order = attackMove ? WarCommand.AttackMove : WarCommand.Move;
        u.repath = 3f;
        if (!FindPath(u, x, y, out _)) u.repath = 0.2f;
    }

    // ---------- Поиск пути ----------
    // Ближний (до FineRange клеток) - A* по клеткам в окне вокруг бойца и цели.
    // Дальний - A* по блокам 5×5 клеток (переход между соседними блоками проверяется маленьким поиском и запоминается),
    // точки пути - «центры» блоков; если между ними что-то мешает, боец обходит подробным путём (Detour).

    const int NodesPerStep = 60000;     // столько клеток/блоков на все поиски за шаг расчёта
    const int FineRange = 45;
    const int CB = 5;                   // блок крупной сетки
    int nodeBudget;

    int cw, ch;
    int[] rep;                          // блок -> клетка-представитель (проходимая по местности), -1 - сплошь лес и вода
    readonly Dictionary<long, bool> edgeCache = new Dictionary<long, bool>();

    void ClearEdgeCache() { edgeCache.Clear(); }

    void BuildCoarse()
    {
        cw = (W + CB - 1) / CB;
        ch = (H + CB - 1) / CB;
        rep = new int[cw * ch];
        for (int by = 0; by < ch; by++)
            for (int bx = 0; bx < cw; bx++)
            {
                int best = -1;
                float bestD = float.MaxValue;
                float mx = bx * CB + CB / 2f, my = by * CB + CB / 2f;
                for (int y = by * CB; y < Math.Min(H, by * CB + CB); y++)
                    for (int x = bx * CB; x < Math.Min(W, bx * CB + CB); x++)
                    {
                        if (!WarRules.Passable(terrain[y * W + x])) continue;
                        float d = Dist(x + 0.5f, y + 0.5f, mx, my) + (occ[y * W + x] != 0 ? 4f : 0f);
                        if (d < bestD) { bestD = d; best = y * W + x; }
                    }
                rep[by * cw + bx] = best;
            }
        cg = new float[cw * ch];
        cparent = new int[cw * ch];
        cstamp = new int[cw * ch];
        cclosed = new int[cw * ch];
    }

    bool BlockOpen(int side, int b)
    {
        int r = rep[b];
        if (r < 0) return false;
        if (Walkable(side, r % W, r / W)) return true;
        // Представитель занят зданием - ищем другую свободную клетку блока
        int bx = b % cw, by = b / cw;
        for (int y = by * CB; y < Math.Min(H, by * CB + CB); y++)
            for (int x = bx * CB; x < Math.Min(W, bx * CB + CB); x++)
                if (Walkable(side, x, y)) { rep[b] = y * W + x; return true; }
        return false;
    }

    // Можно ли пройти из блока a в соседний блок b (маленький поиск между представителями, результат запоминается)
    bool EdgeOpen(int side, int a, int b)
    {
        long key = ((long)Math.Min(a, b) * cw * ch + Math.Max(a, b)) * 4 + side;
        if (edgeCache.TryGetValue(key, out bool ok)) return ok;
        int ra = rep[a], rb = rep[b];
        int ax = ra % W, ay = ra / W, bx2 = rb % W, by2 = rb / W;
        ok = LineWalk(side, ax, ay, bx2, by2);
        if (!ok)
        {
            tmpEdge.Clear();
            ok = FineSearch(side, ax, ay, bx2, by2, 4, 220, tmpEdge, false);
        }
        nodeBudget -= 6;
        edgeCache[key] = ok;
        if (edgeCache.Count > 400000) edgeCache.Clear();
        return ok;
    }
    readonly List<int> tmpEdge = new List<int>();

    /// <summary>Путь бойца к точке. false - не хватило бюджета этого шага (попробуем чуть позже). reached - путь доходит до цели.</summary>
    bool FindPath(SimUnit u, float tx, float ty, out bool reached)
    {
        reached = false;
        u.path.Clear();
        u.pathPos = 0;
        u.fixedAt = -1;
        if (nodeBudget <= 0) return false;
        int sx = Math.Max(0, Math.Min(W - 1, (int)u.x)), sy = Math.Max(0, Math.Min(H - 1, (int)u.y));
        int gx = Math.Max(0, Math.Min(W - 1, (int)tx)), gy = Math.Max(0, Math.Min(H - 1, (int)ty));
        if (sx == gx && sy == gy) { reached = true; return true; }
        if (Math.Max(Math.Abs(gx - sx), Math.Abs(gy - sy)) <= FineRange)
        {
            bool near = u.order == WarCommand.Move || u.order == WarCommand.AttackMove;
            reached = FineSearch(u.side, sx, sy, gx, gy, 20, near ? 9000 : 2500, u.path, true);
            return true;
        }
        reached = CoarsePath(u.side, sx, sy, gx, gy, u.path);
        return true;
    }

    // Куда (сторона, блок) недавно не нашли пути: туда ищем коротко, не обшаривая всю карту
    readonly Dictionary<long, double> noWay = new Dictionary<long, double>();

    // ---------- Подробный A* в окне ----------

    const int WinMax = 180;
    readonly float[] fg = new float[WinMax * WinMax];
    readonly int[] fparent = new int[WinMax * WinMax];
    readonly int[] fstamp = new int[WinMax * WinMax];
    readonly int[] fclosed = new int[WinMax * WinMax];
    int fsearch;
    readonly MinHeap fheap = new MinHeap(), cheap = new MinHeap();
    static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
    static readonly int[] DY = { 0, 0, 1, -1, 1, -1, 1, -1 };

    /// <summary>
    /// A* по клеткам в окне вокруг старта и цели (с запасом margin). out - точки пути (номера клеток карты), без старта.
    /// partial - если до цели не дойти, путь до самой близкой клетки. Вернёт true, если дошли до цели (или до соседней с ней).
    /// </summary>
    bool FineSearch(int side, int sx, int sy, int gx, int gy, int margin, int limit, List<int> outPath, bool partial)
    {
        int x0 = Math.Max(0, Math.Min(sx, gx) - margin), y0 = Math.Max(0, Math.Min(sy, gy) - margin);
        int x1 = Math.Min(W - 1, Math.Max(sx, gx) + margin), y1 = Math.Min(H - 1, Math.Max(sy, gy) + margin);
        if (x1 - x0 + 1 > WinMax) { int c = (sx + gx) / 2; x0 = Math.Max(0, c - WinMax / 2); x1 = Math.Min(W - 1, x0 + WinMax - 1); }
        if (y1 - y0 + 1 > WinMax) { int c = (sy + gy) / 2; y0 = Math.Max(0, c - WinMax / 2); y1 = Math.Min(H - 1, y0 + WinMax - 1); }
        int ww = x1 - x0 + 1;
        sx = Math.Max(x0, Math.Min(x1, sx)); sy = Math.Max(y0, Math.Min(y1, sy));
        int lgx = Math.Max(x0, Math.Min(x1, gx)), lgy = Math.Max(y0, Math.Min(y1, gy));
        fsearch++;
        if (fsearch == int.MaxValue) { fsearch = 1; Array.Clear(fstamp, 0, fstamp.Length); Array.Clear(fclosed, 0, fclosed.Length); }
        int start = (sy - y0) * ww + (sx - x0), goal = (lgy - y0) * ww + (lgx - x0);
        fheap.Clear();
        fg[start] = 0; fparent[start] = -1; fstamp[start] = fsearch;
        fheap.Push(start, Heur(sx, sy, lgx, lgy));
        int best = start;
        float bestH = Heur(sx, sy, lgx, lgy);
        int expanded = 0;
        bool reached = false;
        while (fheap.Count > 0 && expanded < limit)
        {
            int cur = fheap.Pop();
            if (fclosed[cur] == fsearch) continue;
            fclosed[cur] = fsearch;
            expanded++;
            int cx = cur % ww + x0, cy = cur / ww + y0;
            float hh = Heur(cx, cy, lgx, lgy);
            if (hh < bestH) { bestH = hh; best = cur; }
            if (cur == goal || (hh <= 1.5f && !Walkable(side, lgx, lgy))) { best = cur; reached = true; break; }
            for (int k = 0; k < 8; k++)
            {
                int nx = cx + DX[k], ny = cy + DY[k];
                if (nx < x0 || ny < y0 || nx > x1 || ny > y1) continue;
                int n = (ny - y0) * ww + (nx - x0);
                if (!Walkable(side, nx, ny)) continue;
                if (k >= 4 && (!Walkable(side, cx + DX[k], cy) || !Walkable(side, cx, cy + DY[k]))) continue;   // не срезаем углы
                float ng = fg[cur] + (k >= 4 ? 1.414f : 1f);
                if (fstamp[n] == fsearch && ng >= fg[n]) continue;
                fstamp[n] = fsearch;
                fg[n] = ng;
                fparent[n] = cur;
                fheap.Push(n, ng + Heur(nx, ny, lgx, lgy) * 1.05f);
            }
        }
        nodeBudget -= expanded;
        if (!reached && !partial) return false;
        var rev = new List<int>();
        for (int c = best; c != -1 && c != start; c = fparent[c]) rev.Add((c / ww + y0) * W + c % ww + x0);
        int from = outPath.Count;
        for (int i = rev.Count - 1; i >= 0; i--) outPath.Add(rev[i]);
        Smooth(side, sx, sy, outPath, from);
        return reached;
    }

    // Убираем лишние точки: оставляем только те, до которых по прямой уже не дойти
    void Smooth(int side, int sx, int sy, List<int> p, int from)
    {
        if (p.Count - from < 3) return;
        var res = new List<int>();
        int ax = sx, ay = sy;
        int i = from;
        while (i < p.Count)
        {
            int far = i;
            for (int j = Math.Min(p.Count - 1, i + 14); j > i; j--)
                if (LineWalk(side, ax, ay, p[j] % W, p[j] / W)) { far = j; break; }
            res.Add(p[far]);
            ax = p[far] % W; ay = p[far] / W;
            i = far + 1;
        }
        p.RemoveRange(from, p.Count - from);
        p.AddRange(res);
    }

    // ---------- Крупный A* по блокам ----------

    float[] cg;
    int[] cparent, cstamp, cclosed;
    int csearch;

    bool CoarsePath(int side, int sx, int sy, int gx, int gy, List<int> outPath)
    {
        int sb = (sy / CB) * cw + sx / CB, gb = (gy / CB) * cw + gx / CB;
        long nk = (long)gb * 4 + side;
        bool knownBad = noWay.TryGetValue(nk, out double badUntil) && badUntil > S.time;
        int gbx = gb % cw, gby = gb / cw;
        csearch++;
        if (csearch == int.MaxValue) { csearch = 1; Array.Clear(cstamp, 0, cstamp.Length); Array.Clear(cclosed, 0, cclosed.Length); }
        cheap.Clear();
        cg[sb] = 0; cparent[sb] = -1; cstamp[sb] = csearch;
        cheap.Push(sb, Heur(sb % cw, sb / cw, gbx, gby));
        int best = sb;
        float bestH = Heur(sb % cw, sb / cw, gbx, gby);
        int expanded = 0, limit = Math.Max(600, Math.Min(knownBad ? 1500 : 26000, nodeBudget / 3));
        bool reached = false;
        bool goalOpen = BlockOpen(side, gb);
        while (cheap.Count > 0 && expanded < limit)
        {
            int cur = cheap.Pop();
            if (cclosed[cur] == csearch) continue;
            cclosed[cur] = csearch;
            expanded++;
            int cx = cur % cw, cy = cur / cw;
            float hh = Heur(cx, cy, gbx, gby);
            if (hh < bestH) { bestH = hh; best = cur; }
            if (cur == gb || (!goalOpen && hh <= 1.5f)) { best = cur; reached = true; break; }
            for (int k = 0; k < 8; k++)
            {
                int nx = cx + DX[k], ny = cy + DY[k];
                if (nx < 0 || ny < 0 || nx >= cw || ny >= ch) continue;
                int n = ny * cw + nx;
                if (cclosed[n] == csearch) continue;
                if (!BlockOpen(side, n) || (cur != sb && !EdgeOpen(side, cur, n))) continue;
                float ng = cg[cur] + (k >= 4 ? 1.414f : 1f);
                if (cstamp[n] == csearch && ng >= cg[n]) continue;
                cstamp[n] = csearch;
                cg[n] = ng;
                cparent[n] = cur;
                cheap.Push(n, ng + Heur(nx, ny, gbx, gby) * 1.05f);
            }
        }
        nodeBudget -= expanded * 3;
        var rev = new List<int>();
        for (int c = best; c != -1 && c != sb; c = cparent[c]) rev.Add(rep[c]);
        for (int i = rev.Count - 1; i >= 0; i--) outPath.Add(rev[i]);
        // В конце - сама точка (если в неё можно встать)
        if (best == gb && Walkable(side, gx, gy)) outPath.Add(gy * W + gx);
        // Лишние точки на прямых: оставляем каждую, до которой по прямой уже не дойти
        Smooth(side, sx, sy, outPath, 0);
        if (!reached && expanded < limit) noWay[nk] = S.time + 20.0;   // обошли всё доступное - туда не пройти
        if (noWay.Count > 5000) noWay.Clear();
        return reached;
    }

    static float Heur(int x, int y, int gx, int gy)
    {
        int dx = Math.Abs(x - gx), dy = Math.Abs(y - gy);
        return Math.Max(dx, dy) + 0.414f * Math.Min(dx, dy);
    }

    // Двоичная куча для A* (у подробного и крупного поиска - своя: крупный зовёт подробный посреди поиска)
    sealed class MinHeap
    {
        readonly List<int> n = new List<int>();
        readonly List<float> f = new List<float>();
        public int Count => n.Count;
        public void Clear() { n.Clear(); f.Clear(); }

        public void Push(int node, float pri)
        {
            n.Add(node); f.Add(pri);
            int i = n.Count - 1;
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (f[p] <= f[i]) break;
                Swap(i, p);
                i = p;
            }
        }

        public int Pop()
        {
            int top = n[0];
            int last = n.Count - 1;
            n[0] = n[last]; f[0] = f[last];
            n.RemoveAt(last); f.RemoveAt(last);
            int i = 0;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, m = i;
                if (l < n.Count && f[l] < f[m]) m = l;
                if (r < n.Count && f[r] < f[m]) m = r;
                if (m == i) break;
                Swap(i, m);
                i = m;
            }
            return top;
        }

        void Swap(int a, int b)
        {
            int t = n[a]; n[a] = n[b]; n[b] = t;
            float x = f[a]; f[a] = f[b]; f[b] = x;
        }
    }

    // ---------- Найм ----------

    /// <summary>Что город может нанять: уровень казармы, путь развития, конюшня.</summary>
    public string CanTrain(SimCity c, WarUnitDef d)
    {
        if (c.fallen) return "Город пал";
        int barracks = 0;
        bool stable = false, eng = false;
        foreach (var b in S.blds)
        {
            if (b.city != c.idx || b.hp <= 0) continue;
            if (b.type == WarRules.BarracksName) barracks = Math.Max(barracks, b.level);
            if (b.type == WarRules.StableName) stable = true;
            if (b.type == WarRules.EngBarracksName) eng = true;
        }
        if (d.engineer) { if (!eng) return "Нужна Инженерная казарма"; }
        else if (barracks < d.barracks) return barracks == 0 ? "Нужна Казарма" : $"Нужна Казарма Ур.{d.barracks}";
        if (d.path != 0 && c.path != d.path) return "Другой путь развития";
        if (d.mounted && !stable) return "Нужна Конюшня";
        return null;
    }

    public int Army(SimCity c, out int space)
    {
        int n = 0;
        space = 0;
        foreach (var u in S.units) if (u.city == c.idx && u.hp > 0) { n++; space += Def(u).space; }
        foreach (var q in c.queue) { int i = WarRules.UnitIndex(q); if (i >= 0) { n++; space += WarRules.Units[i].space; } }
        return n;
    }

    /// <summary>Сколько места в казармах города (больше казарм и уровней - больше бойцов). Не меньше 12: стартовые отряды.</summary>
    public int SpaceCap(SimCity c)
    {
        int cap = 0;
        foreach (var b in S.blds)
            if (b.city == c.idx && b.hp > 0 && (b.type == WarRules.BarracksName || b.type == WarRules.EngBarracksName)) cap += 10 * b.level;
        return Math.Max(12, Math.Min(cap, 120));
    }

    public string Train(SimCity c, string unit, int count, int squad)
    {
        int ti = WarRules.UnitIndex(unit);
        if (ti < 0) return "Нет такого бойца";
        WarUnitDef d = WarRules.Units[ti];
        string err = CanTrain(c, d);
        if (err != null) return err;
        count = Math.Max(1, Math.Min(10, count));
        squad = Math.Max(0, Math.Min(WarRules.Squads, squad));
        for (int i = 0; i < count; i++)
        {
            int n = Army(c, out int space);
            if (n >= WarRules.MaxUnitsPerCity) return i > 0 ? null : $"Больше {WarRules.MaxUnitsPerCity} бойцов у города быть не может";
            if (space + d.space > SpaceCap(c)) return i > 0 ? null : "Нет места в казармах";
            int food = d.food + (d.mounted ? WarRules.HorseFood : 0);
            if (c.gold < d.gold || c.food < food || c.wood < d.wood || c.iron < d.iron)
                return i > 0 ? null : "Не хватает: " + CostText(d.gold, food, d.wood, 0, d.iron);
            c.gold -= d.gold; c.food -= food; c.wood -= d.wood; c.iron -= d.iron;
            if (c.queue.Count == 0) c.queueLeft = d.train * WarRules.TrainMul;
            c.queue.Add(unit);
            c.queueSquad.Add(squad);
        }
        return null;
    }

    public static string CostText(int gold, int food, int wood, int stone, int iron)
    {
        var p = new List<string>();
        if (gold > 0) p.Add(gold + " золота");
        if (food > 0) p.Add(food + " еды");
        if (wood > 0) p.Add(wood + " дерева");
        if (stone > 0) p.Add(stone + " камня");
        if (iron > 0) p.Add(iron + " железа");
        return string.Join(", ", p);
    }

    public void CancelTrain(SimCity c)
    {
        if (c.queue.Count == 0) return;
        int last = c.queue.Count - 1;
        int ti = WarRules.UnitIndex(c.queue[last]);
        if (ti >= 0)
        {
            WarUnitDef d = WarRules.Units[ti];
            c.gold += d.gold; c.food += d.food + (d.mounted ? WarRules.HorseFood : 0); c.wood += d.wood; c.iron += d.iron;
        }
        c.queue.RemoveAt(last);
        if (c.queueSquad.Count > last) c.queueSquad.RemoveAt(last);
        if (c.queue.Count == 0) c.queueLeft = 0f;
    }

    void StepTraining(float dt)
    {
        foreach (var c in S.cities)
        {
            if (c.fallen || c.queue.Count == 0) continue;
            c.queueLeft -= dt;
            if (c.queueLeft > 0f) continue;
            string unit = c.queue[0];
            int squad = c.queueSquad.Count > 0 ? c.queueSquad[0] : 0;
            c.queue.RemoveAt(0);
            if (c.queueSquad.Count > 0) c.queueSquad.RemoveAt(0);
            int ti = WarRules.UnitIndex(unit);
            if (ti >= 0) Spawn(c, ti).squad = squad;
            if (c.queue.Count > 0)
            {
                int ni = WarRules.UnitIndex(c.queue[0]);
                c.queueLeft = ni >= 0 ? WarRules.Units[ni].train * WarRules.TrainMul : 1f;
            }
        }
    }

    /// <summary>
    /// Бойцы вышли из казарм живой деревни на карту (klan-voina-2): там, где стояли в городе (here, x/y - клетки от центра города).
    /// Оружие и щит из города - множители урона и здоровья. Отряд по роду войск: пехота, стрелки, конница, маги, осада, прочие.
    /// </summary>
    public int Deploy(SimCity c, List<WarRecruit> list, out string error, List<int> ids = null)
    {
        error = null;
        if (c.fallen) { error = "Город пал"; return 0; }
        if (Stage < WarStage.Prepare || Stage >= WarStage.Finished) { error = "Сейчас выводить бойцов нельзя"; return 0; }
        int n = 0;
        foreach (var r in list)
        {
            if (r == null) continue;
            int ti = WarRules.UnitIndex(r.unit);
            if (ti < 0) continue;
            if (Army(c, out int _) >= WarRules.MaxUnitsPerCity) { if (n == 0) error = $"Больше {WarRules.MaxUnitsPerCity} бойцов на карте у города быть не может"; break; }
            SimUnit u = Spawn(c, ti);
            if (r.here && Math.Abs(r.x) < WarRules.CitySize / 2f && Math.Abs(r.y) < WarRules.CitySize / 2f)
            {
                float ox = WarRules.CityCenterX(c.plotX) + r.x, oy = WarRules.CityCenterY(c.plotY) + r.y;
                FreeSpot(c.side, ref ox, ref oy);
                u.x = u.tx = ox; u.y = u.ty = oy;
            }
            u.dmgMul = Math.Max(0.5f, Math.Min(3.5f, r.dmgMul));
            u.maxHp = WarRules.Units[ti].hp * Math.Max(1f, Math.Min(2.5f, r.hpMul));
            u.hp = MaxHp(u);
            u.squad = r.squad > 0 ? Math.Min(WarRules.Squads, r.squad) : WarRules.SquadOf(WarRules.Units[ti]);
            if (r.bound)
            {
                // Приписан к казарме деревни: она стоит на карте там же, где в деревне (клетка от центра города)
                SimBld hb = HomeBarracks(c, r.bx, r.by);
                if (hb != null)
                {
                    u.home = hb.id;
                    u.kit = r.kit != null && r.kit.Length <= 4000 ? r.kit : null;
                }
            }
            if (ids != null) ids.Add(u.id);
            n++;
        }
        if (n == 0 && error == null) error = "Таких бойцов на карте войны нет";
        if (n > 0) S.version++;
        return n;
    }

    /// <summary>Казарма города на карте по её клетке в деревне (OriginCell).</summary>
    SimBld HomeBarracks(SimCity c, int bx, int by)
    {
        int x = (int)WarRules.CityCenterX(c.plotX) + bx, y = (int)WarRules.CityCenterY(c.plotY) + by;
        foreach (var b in S.blds)
            if (b.city == c.idx && b.x == x && b.y == y && (b.type == WarRules.BarracksName || b.type == WarRules.EngBarracksName)) return b;
        return null;
    }

    /// <summary>Сколько клеток от бойца до края здания (0 - внутри).</summary>
    static float DistToBld(SimUnit u, SimBld b)
    {
        float dx = Math.Max(0f, Math.Max(b.x - u.x, u.x - (b.x + b.w)));
        float dy = Math.Max(0f, Math.Max(b.y - u.y, u.y - (b.y + b.h)));
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// klan-voina-14: бойцы у своей казармы заходят в неё и возвращаются в деревню (не гибнут: потерь не считаем).
    /// Только свои, живые, приписанные к целой казарме и не дальше BarracksReach от неё. back - кто зашёл (для деревни).
    /// </summary>
    public string Recall(SimCity c, List<int> ids, List<WarRecruit> back)
    {
        if (c.fallen) return "Город пал";
        if (ids == null || ids.Count == 0) return "Не выбраны бойцы";
        int far = 0, homeless = 0;
        foreach (int id in ids)
        {
            if (!unitById.TryGetValue(id, out SimUnit u) || u.city != c.idx || u.hp <= 0) continue;
            if (u.home < 0 || !bldById.TryGetValue(u.home, out SimBld b) || b.hp <= 0) { homeless++; continue; }
            if (DistToBld(u, b) > WarRules.BarracksReach) { far++; continue; }
            int bx = b.x - (int)WarRules.CityCenterX(c.plotX), by = b.y - (int)WarRules.CityCenterY(c.plotY);
            back.Add(new WarRecruit { unit = WarRules.Units[u.type].name, bound = true, bx = bx, by = by, kit = u.kit,
                                      hpMul = Math.Max(0.05f, Math.Min(1f, u.hp / MaxHp(u))), squad = u.squad, id = u.id });
            // Уходит с карты сразу (корзины ForNear строятся заново в начале шага): не погиб, потерь нет
            S.units.Remove(u);
            unitById.Remove(u.id);
        }
        if (back.Count > 0) { S.version++; return null; }
        if (far > 0) return $"До казармы далеко: подведите бойцов к ней (не дальше {WarRules.BarracksReach:0} клеток)";
        if (homeless > 0) return "Их казарма разрушена или они не из казармы";
        return "Некого заводить";
    }

    SimUnit Spawn(SimCity c, int ti)
    {
        WarUnitDef d = WarRules.Units[ti];
        SimBld home = null;
        foreach (var b in S.blds)
            if (b.city == c.idx && b.hp > 0 && (d.engineer ? b.type == WarRules.EngBarracksName : b.type == WarRules.BarracksName)) { home = b; break; }
        if (home == null)
            foreach (var b in S.blds) if (b.city == c.idx && b.hp > 0 && b.type == WarRules.HallName) { home = b; break; }
        float ox = WarRules.CityCenterX(c.plotX), oy = WarRules.CityCenterY(c.plotY);
        if (home != null) { ox = home.x + home.w / 2f; oy = home.y - 0.5f; }
        FreeSpot(c.side, ref ox, ref oy);
        var u = new SimUnit { id = S.nextId++, city = c.idx, side = c.side, type = ti, x = ox, y = oy, tx = ox, ty = oy };
        u.hp = MaxHp(u);
        S.units.Add(u);
        unitById[u.id] = u;
        return u;
    }

    void FreeSpot(int side, ref float x, ref float y)
    {
        int cx = (int)x, cy = (int)y;
        for (int r = 0; r < 16; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    if (Walkable(side, cx + dx, cy + dy) || (Passable(cx + dx, cy + dy) && Stage < WarStage.Prepare))
                    { x = cx + dx + 0.5f; y = cy + dy + 0.5f; return; }
                }
    }

    bool Passable(int x, int y)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return false;
        return WarRules.Passable(terrain[y * W + x]) && occ[y * W + x] == 0;
    }

    // ---------- Улучшения и ремонт ----------

    public string Research(SimCity c, int kind)
    {
        if (c.fallen) return "Город пал";
        if (kind != WarRules.ResWeapon && kind != WarRules.ResArmor) return "Нет такого улучшения";
        int lv = kind == WarRules.ResWeapon ? c.weaponLv : c.armorLv;
        if (lv >= WarRules.MaxResearch) return "Уже наилучшее";
        WarRules.ResearchCost(kind, lv + 1, out int gold, out int wood, out int stone, out int iron);
        if (c.gold < gold || c.wood < wood || c.stone < stone || c.iron < iron) return "Не хватает: " + CostText(gold, 0, wood, stone, iron);
        c.gold -= gold; c.wood -= wood; c.stone -= stone; c.iron -= iron;
        if (kind == WarRules.ResWeapon) c.weaponLv++;
        else
        {
            // Броня сразу у всех бойцов города: здоровье растёт в той же доле
            float before = 1f + WarRules.ResearchStep * c.armorLv;
            c.armorLv++;
            float after = 1f + WarRules.ResearchStep * c.armorLv;
            foreach (var u in S.units) if (u.city == c.idx && u.hp > 0) u.hp *= after / before;
        }
        Event($"{c.name}: {WarRules.ResearchName(kind).ToLowerInvariant()} бойцов улучшено до ур.{lv + 1}", c.side);
        return null;
    }

    bool EnemyNear(SimBld b, int side, float r)
    {
        float cx = b.x + b.w / 2f, cy = b.y + b.h / 2f;
        bool found = false;
        ForNear(cx, cy, r + Math.Max(b.w, b.h) / 2f, e => { if (!found && e.side != side && e.hp > 0) found = true; });
        return found;
    }

    /// <summary>Починить здание (id) или все повреждённые (-1). Платим сразу, чинят постепенно, пока рядом нет врагов.</summary>
    public string Repair(SimCity c, int id)
    {
        if (c.fallen) return "Город пал";
        var list = new List<SimBld>();
        if (id >= 0)
        {
            if (!bldById.TryGetValue(id, out SimBld b) || b.city != c.idx) return "Это не ваше здание";
            list.Add(b);
        }
        else foreach (var b in S.blds) if (b.city == c.idx && b.hp + b.repair < b.maxHp - 0.5f) list.Add(b);
        if (list.Count == 0) return "Чинить нечего — все здания целы";
        int done = 0;
        string lack = null;
        foreach (var b in list)
        {
            if (b.type == WarRules.HallName && b.hp <= 0) continue;
            float missing = b.maxHp - b.hp - b.repair;
            if (missing < 0.5f) { if (id >= 0) return "Здание уже чинят"; continue; }
            WarRules.RepairCost(missing, b.hp <= 0, out int wood, out int stone);
            if (c.wood < wood || c.stone < stone) { lack = "Не хватает: " + CostText(0, 0, wood, stone, 0); continue; }
            c.wood -= wood; c.stone -= stone;
            b.repair += missing;
            done++;
        }
        if (done == 0) return lack ?? "Чинить нечего";
        return null;
    }

    /// <summary>Сколько стоит починить здание (id) или все повреждённые (-1): платит живая деревня (klan-voina-2).</summary>
    public void RepairCostOf(SimCity c, int id, out int wood, out int stone)
    {
        wood = 0; stone = 0;
        foreach (var b in S.blds)
        {
            if (b.city != c.idx || (id >= 0 && b.id != id)) continue;
            if (b.type == WarRules.HallName && b.hp <= 0) continue;
            float missing = b.maxHp - b.hp - b.repair;
            if (missing < 0.5f) continue;
            WarRules.RepairCost(missing, b.hp <= 0, out int w, out int s);
            wood += w; stone += s;
        }
    }

    void StepRepair(float seconds)
    {
        foreach (var b in S.blds)
        {
            if (b.repair <= 0f) continue;
            SimCity c = S.cities[b.city];
            if (c.fallen) { b.repair = 0f; continue; }
            if (EnemyNear(b, c.side, WarRules.RepairSafeRange)) continue;
            float add = Math.Min(b.repair, b.maxHp * WarRules.RepairPerSecond * seconds);
            if (b.hp <= 0f)
            {
                // Разрушенное здание встаёт снова: кто стоит на его месте - отходит
                foreach (var u in S.units)
                    if (u.x >= b.x && u.x < b.x + b.w && u.y >= b.y && u.y < b.y + b.h)
                    {
                        float nx = b.x - 0.5f, ny = u.y;
                        Occupy(b, true);
                        FreeSpot(u.side, ref nx, ref ny);
                        Occupy(b, false);
                        u.x = nx; u.y = ny; u.path.Clear();
                    }
                Occupy(b, true);
                b.hp = 1f;
                S.version++;
            }
            b.hp = Math.Min(b.maxHp, b.hp + add);
            b.repair -= add;
            if (b.hp >= b.maxHp - 0.01f) { b.hp = b.maxHp; b.repair = 0f; }
        }
    }

    // ---------- Приказы ----------

    public string Command(SimCity c, WarCommand cmd)
    {
        if (cmd == null) return "Пустой приказ";
        if (Stage < WarStage.Prepare) return "Война ещё не началась";
        if (Stage >= WarStage.Finished) return "Война окончена";
        switch (cmd.kind)
        {
            case WarCommand.Train: return Train(c, cmd.unit, cmd.count, cmd.squad);
            case WarCommand.CancelTrain: CancelTrain(c); return null;
            case WarCommand.MoveBuilding: return MoveBuilding(c, cmd.building, (int)cmd.x, (int)cmd.y);
            case WarCommand.Repair: return Repair(c, cmd.building);
            case WarCommand.Research: return Research(c, cmd.squad);
            case WarCommand.RenameSquad:
            {
                if (cmd.squad < 1 || cmd.squad > WarRules.Squads) return "Нет такого отряда";
                string name = (cmd.unit ?? "").Trim();
                if (name.Length > 18) name = name.Substring(0, 18);
                c.squadNames[cmd.squad - 1] = name;
                return null;
            }
        }

        var mine = new List<SimUnit>();
        if (cmd.units != null)
            foreach (int id in cmd.units)
                if (unitById.TryGetValue(id, out SimUnit u) && u.city == c.idx && u.hp > 0) mine.Add(u);
        if (mine.Count == 0) return "Не выбраны бойцы";

        switch (cmd.kind)
        {
            case WarCommand.SetSquad:
            {
                int sq = Math.Max(0, Math.Min(WarRules.Squads, cmd.squad));
                foreach (var u in mine) u.squad = sq;
                return null;
            }
            case WarCommand.Stop:
                foreach (var u in mine) { u.order = 0; u.target = -1; u.path.Clear(); u.tx = u.x; u.ty = u.y; u.hold = false; }
                return null;
            case WarCommand.Hold:
                foreach (var u in mine) { u.order = 0; u.target = -1; u.path.Clear(); u.tx = u.x; u.ty = u.y; u.hold = true; }
                return null;
            case WarCommand.Attack:
                if (!TargetAlive(cmd.target)) return "Цели уже нет";
                foreach (var u in mine) { u.order = WarCommand.Attack; u.target = cmd.target; u.repath = 0f; u.path.Clear(); u.hold = false; }
                return null;
            case WarCommand.Move:
            case WarCommand.AttackMove:
            {
                int zoneTo = G.ZoneOfCell((int)cmd.x, (int)cmd.y);
                if (!WarRules.CanEnter(c.side, zoneTo, Stage)) return ZoneClosedText(c.side, zoneTo);
                // Строй: каждому своё место вокруг точки
                var spots = Formation(c.side, cmd.x, cmd.y, mine.Count);
                SimUnit lead = null;
                for (int i = 0; i < mine.Count; i++)
                {
                    SimUnit u = mine[i];
                    u.target = -1;
                    u.hold = false;
                    // Кто стоит рядом с первым, идёт его путём (считать путь каждому - долго)
                    if (lead != null && lead.path.Count > 0 && Dist(u.x, u.y, lead.x, lead.y) < 6f)
                    {
                        u.tx = spots[i].Item1; u.ty = spots[i].Item2;
                        u.order = cmd.kind == WarCommand.AttackMove ? WarCommand.AttackMove : WarCommand.Move;
                        u.repath = 3f;
                        u.path.Clear();
                        u.path.AddRange(lead.path);
                        u.path[u.path.Count - 1] = (int)u.ty * W + (int)u.tx;
                        u.pathPos = 0;
                        u.fixedAt = -1;
                        continue;
                    }
                    MoveTo(u, spots[i].Item1, spots[i].Item2, cmd.kind == WarCommand.AttackMove);
                    if (lead == null) lead = u;
                }
                return null;
            }
        }
        return "Неизвестный приказ";
    }

    string ZoneClosedText(int side, int zone)
    {
        if (zone == 0) return "Нейтральные земли откроются на 3-й день";
        if (zone != side) return "Земли врага откроются на 5-й день";
        return "Сюда пока нельзя";
    }

    List<Tuple<float, float>> Formation(int side, float x, float y, int n)
    {
        var res = new List<Tuple<float, float>>();
        int cx = (int)x, cy = (int)y;
        for (int r = 0; r < 30 && res.Count < n; r++)
            for (int dy = -r; dy <= r && res.Count < n; dy++)
                for (int dx = -r; dx <= r && res.Count < n; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    if (Walkable(side, cx + dx, cy + dy)) res.Add(Tuple.Create(cx + dx + 0.5f, cy + dy + 0.5f));
                }
        while (res.Count < n) res.Add(Tuple.Create(x, y));
        return res;
    }

    string MoveBuilding(SimCity c, int id, int x, int y)
    {
        if (!bldById.TryGetValue(id, out SimBld b) || b.city != c.idx) return "Это не ваше здание";
        if (b.hp <= 0) return "Здание разрушено";
        if (c.fallen) return "Город пал";
        int ox = c.plotX * WarRules.Plot, oy = c.plotY * WarRules.Plot;
        if (x < ox || y < oy || x + b.w > ox + WarRules.CitySize || y + b.h > oy + WarRules.CitySize) return "Только в пределах своего города";
        foreach (var u in S.units)
            if (u.side != c.side && u.x >= ox && u.x < ox + WarRules.CitySize && u.y >= oy && u.y < oy + WarRules.CitySize) return "В городе враг — переносить нельзя";
        for (int yy = y; yy < y + b.h; yy++)
            for (int xx = x; xx < x + b.w; xx++)
            {
                int i = yy * W + xx;
                if (!WarRules.Passable(terrain[i])) return "Там лес или вода";
                if (occ[i] != 0 && occ[i] != b.id + 1) return "Место занято";
                foreach (var u in S.units) if ((int)u.x == xx && (int)u.y == yy) return "Там стоят бойцы";
            }
        Occupy(b, false);
        b.x = x; b.y = y;
        Occupy(b, true);
        S.version++;
        return null;
    }

    // ---------- Доход и захват участков ----------

    void Income(float seconds)
    {
        float mul = seconds / 60f * (S.fast ? WarRules.FastIncomeMul : 1f);
        var bonus = new int[3];
        for (int i = 0; i < S.plots.Length; i++)
        {
            int owner = S.plots[i] - '0';
            int zone = G.ZoneOfPlot(i % G.Cols, i / G.Cols);
            if (owner > 0 && zone != owner) bonus[owner]++;
        }
        // Источники: доход всему клану, поровну между его целыми городами
        var src = new int[3][] { new int[5], new int[5], new int[5] };
        foreach (var s in Sources)
        {
            int owner = s.plot >= 0 && s.plot < S.plots.Length ? S.plots[s.plot] - '0' : 0;
            if (owner == 1 || owner == 2) WarRules.SourceIncome(s.kind, src[owner]);
        }
        var alive = new int[3];
        foreach (var c in S.cities) if (!c.fallen && c.side >= 1 && c.side <= 2) alive[c.side]++;
        // Здания дают доход в самой живой деревне (их жители добывают и производят) - здесь не считаем
        var own = new int[S.cities.Count][];
        for (int i = 0; i < own.Length; i++) own[i] = new int[5];
        foreach (var c in S.cities)
        {
            if (c.fallen || c.side < 1 || c.side > 2) { for (int r = 0; r < 5; r++) c.income[r] = 0; continue; }
            var add = new float[5];
            for (int r = 0; r < 5; r++) add[r] = own[c.idx][r];
            add[0] += bonus[c.side] * WarRules.NeutralPlotGold;
            add[1] += bonus[c.side] * WarRules.NeutralPlotFood;
            for (int r = 0; r < 5; r++) add[r] += src[c.side][r] / (float)Math.Max(1, alive[c.side]);
            for (int r = 0; r < 5; r++) c.income[r] = (int)Math.Round(add[r] * (S.fast ? WarRules.FastIncomeMul : 1f));
            c.gold += add[0] * mul; c.food += add[1] * mul; c.wood += add[2] * mul; c.stone += add[3] * mul; c.iron += add[4] * mul;
            c.earnedGold += add[0] * mul; c.earnedFood += add[1] * mul; c.earnedWood += add[2] * mul; c.earnedStone += add[3] * mul; c.earnedIron += add[4] * mul;
        }
    }

    void Capture(float seconds)
    {
        int cols = G.Cols;
        var count1 = new int[S.plots.Length];
        var count2 = new int[S.plots.Length];
        foreach (var u in S.units)
        {
            int px = (int)(u.x / WarRules.Plot), py = (int)(u.y / WarRules.Plot);
            if (px < 0 || py < 0 || px >= cols || py >= G.Rows) continue;
            if (u.side == 1) count1[py * cols + px]++; else count2[py * cols + px]++;
        }
        for (int i = 0; i < S.plots.Length; i++)
        {
            int owner = S.plots[i] - '0';
            int side = count1[i] > 0 && count2[i] == 0 ? 1 : count2[i] > 0 && count1[i] == 0 ? 2 : 0;
            // Участок с целой Цитаделью не захватить: сначала нужно взять город
            int ci = plotCity[i];
            if (ci >= 0 && !S.cities[ci].fallen) side = 0;
            if (side == 0 || side == owner)
            {
                if (S.capture[i] > 0f)
                {
                    S.capture[i] = Math.Max(0f, S.capture[i] - seconds / WarRules.CaptureSeconds);
                    if (S.capture[i] <= 0f) S.captureSide[i] = 0;
                }
                continue;
            }
            if (S.captureSide[i] != side) { S.captureSide[i] = side; S.capture[i] = 0f; }
            S.capture[i] += seconds / WarRules.CaptureSeconds;
            if (S.capture[i] >= 1f)
            {
                S.capture[i] = 0f;
                S.captureSide[i] = 0;
                SetPlot(i, side);
                if (sourceAt.TryGetValue(i, out WarSource ws))
                    Event($"Клан {(side == 1 ? S.nameA : S.nameB)} занял {WarRules.SourceName(ws.kind)} (участок {i % cols + 1}:{i / cols + 1}) — доход теперь идёт ему");
                else
                    Event($"Клан {(side == 1 ? S.nameA : S.nameB)} занял участок {i % cols + 1}:{i / cols + 1}");
            }
        }
    }

    void SetPlot(int i, int side)
    {
        var chars = S.plots.ToCharArray();
        chars[i] = (char)('0' + side);
        S.plots = new string(chars);
    }

    /// <summary>Событие для всех на карте (side - только для одной стороны); возвращает тот же текст (для журнала войны).</summary>
    public string EventText(string text) { Event(text); return text; }

    public void Event(string text, int side = 0)
    {
        S.eventN++;
        S.events.Add(S.eventN + "|" + side + "|" + text);
        if (S.events.Count > 80) S.events.RemoveAt(0);
    }

    // ---------- Обзор (туман войны) ----------

    // Свои земли видно всегда: это не меняется до смены этапа, поэтому считаем один раз
    void RebuildBaseVision()
    {
        if (base1 == null) return;
        Array.Clear(base1, 0, base1.Length);
        Array.Clear(base2, 0, base2.Length);
        for (int py = 0; py < G.Rows; py++)
            for (int px = 0; px < G.Cols; px++)
            {
                int zone = G.ZoneOfPlot(px, py);
                if (zone == 0) continue;
                MarkRect(zone == 1 ? base1 : base2, px * WarRules.Plot, py * WarRules.Plot, WarRules.Plot, WarRules.Plot);
            }
    }

    void UpdateVision()
    {
        Array.Copy(base1, vis1, vis1.Length);
        Array.Copy(base2, vis2, vis2.Length);
        foreach (var u in S.units)
        {
            float r = Def(u).vision > 0 ? Def(u).vision : WarRules.UnitVision;
            MarkCircle(u.side == 1 ? vis1 : vis2, u.x, u.y, r);
        }
        foreach (var b in S.blds)
        {
            if (b.hp <= 0) continue;
            SimCity c = S.cities[b.city];
            if (c.fallen) continue;
            if (!WarRules.Shoots(b.type, b.level, out _, out _, out _, out _) && b.type != WarRules.HallName) continue;   // свои земли и так видно
            float r = b.type == WarRules.TowerName ? WarRules.TowerVision : WarRules.BuildingVision;
            MarkCircle(c.side == 1 ? vis1 : vis2, b.x + b.w / 2f, b.y + b.h / 2f, r);
        }
        // Здания врага, которые увидели, остаются известны
        foreach (var b in S.blds)
        {
            if (!b.seen1 && SeenRect(vis1, b)) { b.seen1 = true; S.version++; }
            if (!b.seen2 && SeenRect(vis2, b)) { b.seen2 = true; S.version++; }
        }
    }

    void MarkRect(bool[] v, int x, int y, int w, int h)
    {
        for (int yy = y / VisCell; yy <= (y + h - 1) / VisCell && yy < vh; yy++)
            for (int xx = x / VisCell; xx <= (x + w - 1) / VisCell && xx < vw; xx++)
                v[yy * vw + xx] = true;
    }

    void MarkCircle(bool[] v, float cx, float cy, float r)
    {
        int x0 = Math.Max(0, (int)((cx - r) / VisCell)), x1 = Math.Min(vw - 1, (int)((cx + r) / VisCell));
        int y0 = Math.Max(0, (int)((cy - r) / VisCell)), y1 = Math.Min(vh - 1, (int)((cy + r) / VisCell));
        float r2 = r * r;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = x * VisCell + 1f - cx, dy = y * VisCell + 1f - cy;
                if (dx * dx + dy * dy <= r2) v[y * vw + x] = true;
            }
    }

    bool SeenRect(bool[] v, SimBld b)
    {
        for (int y = b.y / VisCell; y <= (b.y + b.h - 1) / VisCell && y < vh; y++)
            for (int x = b.x / VisCell; x <= (b.x + b.w - 1) / VisCell && x < vw; x++)
                if (v[y * vw + x]) return true;
        return false;
    }

    public bool SeenBy(int side, float x, float y)
    {
        int vx = (int)(x / VisCell), vy = (int)(y / VisCell);
        if (vx < 0 || vy < 0 || vx >= vw || vy >= vh) return false;
        return (side == 1 ? vis1 : vis2)[vy * vw + vx];
    }

    // ---------- Счёт ----------

    public void Score(out int a, out int b)
    {
        a = b = 0;
        foreach (char ch in S.plots) { if (ch == '1') a++; else if (ch == '2') b++; }
        foreach (var c in S.cities) if (!c.fallen) { if (c.side == 1) a += 2; else b += 2; }
    }

    /// <summary>У стороны не осталось ни одной целой Цитадели.</summary>
    public bool AllFallen(int side)
    {
        bool any = false;
        foreach (var c in S.cities)
        {
            if (c.side != side) continue;
            any = true;
            if (!c.fallen) return false;
        }
        return any;
    }

    // ---------- Что видит сторона ----------

    public WarStatic Static(int side, int myCity, string warId, string nameA, string nameB)
    {
        var st = new WarStatic
        {
            warId = warId, version = S.version, seed = S.seed, rows = S.rows, gc = S.gc, nc = S.nc, mx = S.mx, my = S.my,
            mySide = side, myCity = myCity, fast = S.fast,
            myPath = myCity >= 0 && myCity < S.cities.Count ? S.cities[myCity].path : 0,
            nameA = nameA, nameB = nameB, cities = new List<WarCityView>(), buildings = new List<WarBldView>(),
            sources = new List<WarSource>(Sources),
        };
        foreach (var c in S.cities)
            st.cities.Add(View(c));
        foreach (var b in S.blds)
            if (side == 1 ? b.seen1 : b.seen2)
                st.buildings.Add(new WarBldView { id = b.id, city = b.city, type = b.type, level = b.level, x = b.x, y = b.y, w = b.w, h = b.h, maxHp = b.maxHp });
        return st;
    }

    public WarFrame Frame(int side, SimCity mine, int sinceEvent)
    {
        var f = new WarFrame { version = S.version, time = S.time, plots = S.plots, queue = new List<WarQueueItem>(), events = new List<string>() };
        var sb = new StringBuilder(S.units.Count * 26);
        foreach (var u in S.units)
        {
            if (u.hp <= 0) continue;
            bool own = u.side == side;
            if (!own && !SeenBy(side, u.x, u.y)) continue;
            bool struck = S.time - u.strikeAt < 0.5;
            bool my = mine != null && u.city == mine.idx;
            int flags = (my ? 1 : 0) | (struck ? 2 : 0) | (u.path.Count > u.pathPos ? 4 : 0) | (my && u.hold ? 8 : 0);
            int hpPct = (int)Math.Ceiling(u.hp / MaxHp(u) * 100f);
            sb.Append(u.id).Append(',').Append(u.type).Append(',').Append(u.side).Append(',')
              .Append((int)(u.x * 10)).Append(',').Append((int)(u.y * 10)).Append(',').Append(hpPct).Append(',')
              .Append(flags).Append(',').Append(struck ? u.lastTarget : -1).Append(',').Append(my ? u.squad : 0)
              .Append(',').Append(my ? u.home : -1).Append(';');   // klan-voina-14: своя казарма (id здания)
        }
        f.units = sb.ToString();

        sb.Clear();
        foreach (var b in S.blds)
        {
            if (!(side == 1 ? b.seen1 : b.seen2) || (b.hp >= b.maxHp && b.repair <= 0f)) continue;
            sb.Append(b.id).Append(',').Append((int)Math.Ceiling(b.hp / b.maxHp * 100f)).Append(',').Append(b.repair > 0f && S.cities[b.city].side == side ? 1 : 0).Append(';');
        }
        f.blds = sb.ToString();

        sb.Clear();
        for (int i = 0; i < S.capture.Count; i++)
            if (S.capture[i] > 0f && S.captureSide[i] != 0) sb.Append(i).Append(',').Append(S.captureSide[i]).Append(',').Append((int)(S.capture[i] * 100)).Append(';');
        f.capture = sb.ToString();

        sb.Clear();
        foreach (var sh in recentShots)
            if (bldById.TryGetValue(sh.from, out SimBld sb2) && (side == 1 ? sb2.seen1 : sb2.seen2))
                sb.Append(sh.from).Append(',').Append((int)(sh.x * 10)).Append(',').Append((int)(sh.y * 10)).Append(',').Append((long)(sh.at * 10)).Append(';');
        f.shots = sb.ToString();

        sb.Clear();
        foreach (var d in recentDead) if (SeenBy(side, d.x, d.y)) sb.Append(d.from).Append(';');
        f.dead = sb.ToString();

        if (mine != null)
        {
            f.gold = (int)mine.gold; f.food = (int)mine.food; f.wood = (int)mine.wood; f.stone = (int)mine.stone; f.iron = (int)mine.iron;
            f.army = Army(mine, out int space);
            f.space = space;
            f.armyCap = WarRules.MaxUnitsPerCity;
            f.spaceCap = SpaceCap(mine);
            for (int i = 0; i < mine.queue.Count; i++)
                f.queue.Add(new WarQueueItem { unit = mine.queue[i], left = i == 0 ? Math.Max(0f, mine.queueLeft) : 0f, squad = i < mine.queueSquad.Count ? mine.queueSquad[i] : 0 });
            f.fallen = mine.fallen;
            f.weaponLv = mine.weaponLv;
            f.armorLv = mine.armorLv;
            f.squadNames = new List<string>(mine.squadNames);
            f.income0 = mine.income[0]; f.income1 = mine.income[1]; f.income2 = mine.income[2]; f.income3 = mine.income[3]; f.income4 = mine.income[4];
            f.kills = mine.kills;
            f.losses = mine.losses;
        }
        foreach (var e in S.events)
        {
            // "номер|сторона|текст" (старые - "номер|текст")
            int bar = e.IndexOf('|');
            if (bar <= 0 || !int.TryParse(e.Substring(0, bar), out int n) || n <= sinceEvent) continue;
            string rest = e.Substring(bar + 1);
            int bar2 = rest.IndexOf('|');
            if (bar2 > 0 && bar2 <= 2 && int.TryParse(rest.Substring(0, bar2), out int only))
            {
                if (only != 0 && only != side) continue;
                rest = rest.Substring(bar2 + 1);
            }
            f.events.Add(rest);
        }
        f.eventN = S.eventN;
        Score(out f.scoreA, out f.scoreB);
        return f;
    }

    // ---------- Мелочи ----------

    void ForNear(float x, float y, float r, Action<SimUnit> a)
    {
        int x0 = Math.Max(0, (int)((x - r) / Bucket)), x1 = Math.Min(bw - 1, (int)((x + r) / Bucket));
        int y0 = Math.Max(0, (int)((y - r) / Bucket)), y1 = Math.Min(bh - 1, (int)((y + r) / Bucket));
        for (int by = y0; by <= y1; by++)
            for (int bx = x0; bx <= x1; bx++)
                foreach (var u in buckets[by * bw + bx]) a(u);
    }

    static float Dist(float ax, float ay, float bx, float by)
    {
        float dx = ax - bx, dy = ay - by;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    static float Clamp(float v, float a, float b) { return v < a ? a : v > b ? b : v; }
}
