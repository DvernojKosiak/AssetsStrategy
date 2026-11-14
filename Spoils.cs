using UnityEngine;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Обновление 23: трофеи - «сложные» вещи из разрушенных зданий другого игрока.
///  - Сложные вещи: материалы всех видов и качеств (доски, плиты, сталь, укреплённые плиты, смоляной брус, монолит),
///    зелья отравления и замедления, детали, пропитка, резина, оружие, щиты, броня и плащи, модули и ловушки.
///    Лежат они в Цитадели, Хранилищах, Оружейных и Инженерных дворах.
///  - Разрушили такое здание - по каждой штуке в нём бросается жребий:
///      · копия (CopyChance): у хозяина вещь остаётся, у нападающего появляется такая же;
///      · кража (StealChance, реже): вещь переходит от хозяина к нападающему.
///  - Защищённая половина (ProtectedShare): из каждой стопки здания (и из оружия, щитов, брони, модулей, ловушек
///    отдельно) больше половины не украсть. Одну-единственную вещь украсть нельзя, только скопировать.
///  - Потолок: за один набег трофеев не больше, чем на Budget очков, за сутки - DailyRaids таких набегов
///    (у каждой вещи свой вес, Weight). Дальше копий и краж нет: набегами не заменить своё развитие.
///  - Трофеи несут бойцы, как обычную добычу (Loot.items - отнятое, Loot.copies - копии), но места в мешке они
///    не занимают. Погиб - вещи остаются на поле (или возвращаются хозяину). Засчитывается только то, что унесли.
///  - Нигде ничего не пропадает: то, что не влезает на склады, ждёт в сундуке трофеев (SpoilsChest) и само
///    раскладывается, когда появится место; сундук сохраняется вместе с городом.
/// </summary>
public static class SpoilsRules
{
    public const float CopyChance = 0.08f;     // копия: у хозяина остаётся
    public const float StealChance = 0.03f;    // кража: у хозяина пропадает
    public const float ProtectedShare = 0.5f;  // эту долю каждой стопки украсть нельзя
    public const int BudgetBase = 30, BudgetPerLevel = 20;   // очков трофеев за набег: Цитадель Ур.1 - 30, Ур.2 - 50, Ур.3 - 70
    public const int DailyRaids = 3;           // за сутки - не больше, чем за столько набегов
    public const int MaxTrials = 2000;         // больше штук одной стопки не перебираем

    public static int Budget(int hallLevel) { return BudgetBase + BudgetPerLevel * (Mathf.Clamp(hallLevel, 1, 3) - 1); }
    public static int DailyCap(int hallLevel) { return Budget(hallLevel) * DailyRaids; }
}

/// <summary>Набор вещей: ключ (Spoils) и количество. Порядок сохраняется.</summary>
public class SpoilBundle
{
    readonly List<string> keys = new List<string>();
    readonly List<int> counts = new List<int>();

    public int Kinds => keys.Count;
    public string KeyAt(int i) { return keys[i]; }
    public int CountAt(int i) { return counts[i]; }

    public int Units
    {
        get { int n = 0; foreach (var c in counts) n += c; return n; }
    }
    public bool IsEmpty => keys.Count == 0;

    public void Add(string key, int n)
    {
        if (string.IsNullOrEmpty(key) || n <= 0) return;
        int i = keys.IndexOf(key);
        if (i >= 0) counts[i] += n;
        else { keys.Add(key); counts.Add(n); }
    }

    public void Add(SpoilBundle o)
    {
        if (o == null || o == this) return;
        for (int i = 0; i < o.keys.Count; i++) Add(o.keys[i], o.counts[i]);
    }

    /// <summary>Убрать n штук (сколько есть). Возвращает, сколько убрали.</summary>
    public int Remove(string key, int n)
    {
        int i = keys.IndexOf(key);
        if (i < 0 || n <= 0) return 0;
        int m = Mathf.Min(n, counts[i]);
        counts[i] -= m;
        if (counts[i] <= 0) { keys.RemoveAt(i); counts.RemoveAt(i); }
        return m;
    }

    public void Clear() { keys.Clear(); counts.Clear(); }

    public SpoilBundle Copy() { var b = new SpoilBundle(); b.Add(this); return b; }

    /// <summary>Поштучно случайным частям (как Loot.Split делит доски).</summary>
    public void SplitInto(List<SpoilBundle> parts)
    {
        if (parts == null || parts.Count == 0) return;
        for (int i = 0; i < keys.Count; i++)
            for (int k = 0; k < counts[i]; k++) parts[Random.Range(0, parts.Count)].Add(keys[i], 1);
    }

    public int Points
    {
        get { int n = 0; for (int i = 0; i < keys.Count; i++) n += Spoils.Weight(keys[i]) * counts[i]; return n; }
    }

    /// <summary>«3 доски (хор.), меч» - для сообщений.</summary>
    public string Text
    {
        get
        {
            if (IsEmpty) return "ничего";
            var parts = new List<string>();
            for (int i = 0; i < keys.Count; i++) parts.Add(counts[i] > 1 ? $"{Spoils.Name(keys[i])} ×{counts[i]}" : Spoils.Name(keys[i]));
            return string.Join(", ", parts);
        }
    }

    // ---------- Запись: «ключ*кол-во;…» (в ключах нет «;», «*», «/» и «,») ----------

    public string Encode()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < keys.Count; i++)
        {
            if (i > 0) sb.Append(';');
            sb.Append(keys[i]).Append('*').Append(SaveBag.I(counts[i]));
        }
        return sb.ToString();
    }

    public static SpoilBundle Decode(string s)
    {
        var b = new SpoilBundle();
        if (string.IsNullOrEmpty(s)) return b;
        foreach (var part in s.Split(';'))
        {
            int star = part.LastIndexOf('*');
            if (star <= 0) continue;
            b.Add(part.Substring(0, star), SaveBag.ParseInt(part.Substring(star + 1)));
        }
        return b;
    }
}

/// <summary>Стопка вещей в здании: ключ, сколько, группа для защищённой половины, как взять у хозяина (только у себя дома).</summary>
public class SpoilStack
{
    public string key;
    public int count;
    public string group;
    public System.Func<int, int> take;   // забрать n штук у хозяина; null - забирать не надо (копия деревни на карте набега)
}

public static class Spoils
{
    // ---------- Ключи вещей ----------
    // R|вид|качество - материал; P, S, D, T, U - зелья отравления, зелья замедления, детали, пропитка, резина;
    // W|…, H|…, B|…, K|…, X|… - оружие, щит, броня/плащ, модуль, ловушка (поля - как в SaveBag.Pack, через «:»).

    public const string Potions = "P", SlowPotions = "S", Parts = "D", Tar = "T", Rubber = "U";
    static readonly string[] Counters = { Potions, SlowPotions, Parts, Tar, Rubber };

    public static string Refined(RefinedKind k, QualityTier q) { return "R|" + (int)k + "|" + (int)q; }
    public static string Of(Weapon w) { return "W|" + string.Join(":", SaveBag.Pack(w)); }
    public static string Of(Shield s) { return "H|" + string.Join(":", SaveBag.Pack(s)); }
    public static string Of(BodyGear g) { return "B|" + string.Join(":", SaveBag.Pack(g)); }
    public static string Of(ModuleKit k) { return "K|" + string.Join(":", SaveBag.Pack(k)); }
    public static string Of(TrapKit k) { return "X|" + string.Join(":", SaveBag.Pack(k)); }
    static string Row(string cat, string[] row) { return cat + "|" + string.Join(":", row); }

    static char Cat(string key) { return string.IsNullOrEmpty(key) ? '?' : key[0]; }
    static string[] Fields(string key)
    {
        int bar = key.IndexOf('|');
        return bar < 0 ? new string[0] : key.Substring(bar + 1).Split(key[0] == 'R' ? '|' : ':');
    }

    static GoodKind GoodOf(RefinedKind k)
    {
        switch (k)
        {
            case RefinedKind.Planks: return GoodKind.Planks;
            case RefinedKind.StonePlates: return GoodKind.Plates;
            case RefinedKind.Steel: return GoodKind.Steel;
            case RefinedKind.ReinforcedPlates: return GoodKind.Reinforced;
            case RefinedKind.TarredBeams: return GoodKind.Beams;
            default: return GoodKind.Monolith;
        }
    }

    static GoodKind GoodOf(string counter)
    {
        switch (counter)
        {
            case Potions: return GoodKind.Potions;
            case SlowPotions: return GoodKind.SlowPotions;
            case Parts: return GoodKind.Parts;
            case Tar: return GoodKind.Tar;
            default: return GoodKind.Rubber;
        }
    }

    static StoreRes ResOf(string counter)
    {
        switch (counter)
        {
            case Potions: return StoreRes.Potions;
            case SlowPotions: return StoreRes.SlowPotions;
            case Parts: return StoreRes.Parts;
            case Tar: return StoreRes.Tar;
            default: return StoreRes.Rubber;
        }
    }

    static bool IsCounter(string key) { return System.Array.IndexOf(Counters, key) >= 0; }

    static bool ParseRefined(string key, out RefinedKind k, out QualityTier q)
    {
        string[] a = Fields(key);
        int kk = SaveBag.At(a, 0, -1), qq = SaveBag.At(a, 1, -1);
        k = (RefinedKind)Mathf.Clamp(kk, 0, 5);
        q = (QualityTier)Mathf.Clamp(qq, 0, 2);
        return Cat(key) == 'R' && kk >= 0 && kk <= 5 && qq >= 0 && qq <= 2;
    }

    public static string Name(string key)
    {
        try
        {
            if (IsCounter(key)) return MarketGoods.Name(new GoodId(GoodOf(key)));
            switch (Cat(key))
            {
                case 'R': if (ParseRefined(key, out RefinedKind k, out QualityTier q)) return MarketGoods.Name(new GoodId(GoodOf(k), q)); break;
                case 'W': { var w = SaveBag.UnpackWeapon(Fields(key)); if (w != null) return w.Name; break; }
                case 'H': { var s = SaveBag.UnpackShield(Fields(key)); if (s != null) return s.Name; break; }
                case 'B': { var g = SaveBag.UnpackBody(Fields(key)); if (g != null) return g.Name; break; }
                case 'K': { var m = SaveBag.UnpackKit(Fields(key)); return TowerModules.Name(m.kind) + " " + TowerModules.Roman(m.mk); }
                case 'X': { var t = SaveBag.UnpackTrap(Fields(key)); if (t != null) return t.Name; break; }
            }
        }
        catch (System.Exception) { }
        return "вещь";
    }

    /// <summary>Картинка вещи (ItemIcons) и её качество для метки (-1 - без качества). Обновление final-1.</summary>
    public static string Icon(string key, out int tier)
    {
        tier = -1;
        try
        {
            if (IsCounter(key)) return ItemIcons.Good(GoodOf(key));
            switch (Cat(key))
            {
                case 'R': if (ParseRefined(key, out RefinedKind k, out QualityTier q)) { tier = (int)q; return ItemIcons.Refined(k); } break;
                case 'W': { var w = SaveBag.UnpackWeapon(Fields(key)); if (w != null) { tier = (int)w.quality; return ItemIcons.Of(w); } break; }
                case 'H': { var s = SaveBag.UnpackShield(Fields(key)); if (s != null) { tier = (int)s.quality; return ItemIcons.Of(s); } break; }
                case 'B': { var g = SaveBag.UnpackBody(Fields(key)); if (g != null) { tier = (int)g.quality; return ItemIcons.Of(g); } break; }
                case 'K': { var m = SaveBag.UnpackKit(Fields(key)); return ItemIcons.Module(m.kind); }
                case 'X': { var t = SaveBag.UnpackTrap(Fields(key)); if (t != null) return ItemIcons.Of(t); break; }
            }
        }
        catch (System.Exception) { }
        return null;
    }

    /// <summary>Трофеи плитками для ItemIcons.DrawGrid.</summary>
    public static void AddEntries(SpoilBundle b, List<ItemIcons.Entry> list)
    {
        if (b == null || list == null) return;
        for (int i = 0; i < b.Kinds; i++)
        {
            string id = Icon(b.KeyAt(i), out int tier);
            list.Add(new ItemIcons.Entry(id, Name(b.KeyAt(i)), b.CountAt(i), tier));
        }
    }

    /// <summary>Сколько очков потолка занимает одна штука.</summary>
    public static int Weight(string key)
    {
        if (key == Potions || key == SlowPotions) return 2;
        if (IsCounter(key)) return 1;
        switch (Cat(key))
        {
            case 'R':
                if (!ParseRefined(key, out RefinedKind k, out QualityTier q)) return 1;
                int b = k == RefinedKind.Steel ? 2 : k == RefinedKind.ReinforcedPlates || k == RefinedKind.TarredBeams ? 3 : k == RefinedKind.Monolith ? 6 : 1;
                return Mathf.CeilToInt(b * (1f + 0.5f * (int)q));   // хорошее - в полтора раза, наилучшее - вдвое
            case 'W': case 'H': case 'B': return 6;
            case 'K': return 8;
            case 'X': return 5;
        }
        return 1;
    }

    // Группа для защищённой половины: материал и зелья - каждая стопка своя, снаряжение - всё одного вида вместе
    static string Group(string key)
    {
        char c = Cat(key);
        return c == 'W' || c == 'H' || c == 'B' || c == 'K' || c == 'X' ? c.ToString() : key;
    }

    // ---------- Что лежит в зданиях города (копия деревни игрока на карте набега) ----------

    /// <summary>
    /// Сложные вещи его города по зданиям (ключ - левая нижняя клетка здания в его городе). То, что лежит
    /// в самой Цитадели (склад материалов, зелья, детали, запас модулей и ловушек), - у здания Цитадели.
    /// </summary>
    public static Dictionary<Vector2Int, List<SpoilStack>> StashOfCity(CityData c)
    {
        var res = new Dictionary<Vector2Int, List<SpoilStack>>();
        if (c == null || c.buildings == null) return res;

        void Put(SavedBuilding sb, string key, int n)
        {
            if (n <= 0) return;
            var at = new Vector2Int(sb.x, sb.y);
            if (!res.TryGetValue(at, out var list)) res[at] = list = new List<SpoilStack>();
            foreach (var s in list) if (s.key == key) { s.count += n; return; }
            list.Add(new SpoilStack { key = key, count = n, group = Group(key) });
        }

        SavedBuilding hall = null;
        foreach (var sb in c.buildings) if (sb != null && sb.type == "Ратуша") { hall = sb; break; }

        if (hall != null)
        {
            if (c.refined != null)
                foreach (var r in c.refined)
                    if (r != null && r.kind >= 0 && r.kind <= 5 && r.tier >= 0 && r.tier < 3) Put(hall, Refined((RefinedKind)r.kind, (QualityTier)r.tier), r.amount);
            Put(hall, Potions, c.potions);
            Put(hall, SlowPotions, c.slowPotions);
            Put(hall, Parts, c.parts);
            Put(hall, Tar, c.tar);
            Put(hall, Rubber, c.rubber);
            var extra = new SaveBag(c.extra);
            foreach (var a in extra.List("traps")) Put(hall, Row("X", a), 1);
            foreach (var a in extra.List("trapsBack")) Put(hall, Row("X", a), 1);
            foreach (var a in extra.List("kits")) Put(hall, Row("K", a), 1);
        }

        foreach (var sb in c.buildings)
        {
            if (sb == null || sb.state == null || sb.state.Count == 0) continue;
            var bag = new SaveBag(sb.state);
            if (sb.type == WarehouseController.TypeName)
            {
                foreach (var a in bag.List("wh.stock"))
                {
                    int k = SaveBag.At(a, 0, -1), q = SaveBag.At(a, 1, -1);
                    if (k >= 0 && k <= 5 && q >= 0 && q < 3) Put(sb, Refined((RefinedKind)k, (QualityTier)q), SaveBag.At(a, 2));
                }
                Put(sb, Potions, bag.Int("wh.potions"));
            }
            else if (sb.type == ArmoryController.TypeName)
            {
                foreach (var a in bag.List("arm.w")) Put(sb, Row("W", a), 1);
                foreach (var a in bag.List("arm.s")) Put(sb, Row("H", a), 1);
                foreach (var a in bag.List("arm.b")) Put(sb, Row("B", a), 1);
            }
            else if (sb.type == EngineerYard.TypeName)
                foreach (var a in bag.List("yard.kits")) Put(sb, Row("K", a), 1);
        }
        return res;
    }

    // ---------- Что лежит в здании нашего города (живой бой: нападающий ломает его у нас) ----------

    public static List<SpoilStack> StashOf(BuildingInstance b)
    {
        var list = new List<SpoilStack>();
        if (b == null) return list;

        void Add(string key, int n, System.Func<int, int> take)
        {
            if (n > 0) list.Add(new SpoilStack { key = key, count = n, group = Group(key), take = take });
        }

        if (b.IsTownHall)
        {
            var st = TownHallStorage.Instance;
            if (st != null)
                foreach (RefinedKind k in System.Enum.GetValues(typeof(RefinedKind)))
                    foreach (QualityTier q in System.Enum.GetValues(typeof(QualityTier)))
                    {
                        RefinedKind kk = k; QualityTier qq = q;
                        Add(Refined(k, q), st.Get(k, q), n => { int m = Mathf.Min(n, st.Get(kk, qq)); return m > 0 && st.TrySpend(kk, qq, m) ? m : 0; });
                    }
            var rm = ResourceManager.Instance;
            if (rm != null)
            {
                Add(Potions, rm.potions, n => { int m = Mathf.Min(n, rm.potions); rm.potions -= m; return m; });
                Add(SlowPotions, rm.slowPotions, n => { int m = Mathf.Min(n, rm.slowPotions); rm.slowPotions -= m; return m; });
                Add(Parts, rm.parts, n => { int m = Mathf.Min(n, rm.parts); rm.parts -= m; return m; });
                Add(Tar, rm.tar, n => { int m = Mathf.Min(n, rm.tar); rm.tar -= m; return m; });
                Add(Rubber, rm.rubber, n => { int m = Mathf.Min(n, rm.rubber); rm.rubber -= m; return m; });
            }
            foreach (var t in new List<TrapKit>(TrapStock.Hall))
            {
                TrapKit tt = t;
                if (tt != null) Add(Of(tt), 1, n => TrapStock.Remove(tt) ? 1 : 0);
            }
            for (int i = 0; i < EngineerYard.HallStock.Count; i++)
            {
                ModuleKit kit = EngineerYard.HallStock[i];
                Add(Of(kit), 1, n => TakeKit(EngineerYard.HallStock, kit));
            }
        }

        WarehouseController wh = b.GetComponent<WarehouseController>();
        if (wh != null)
        {
            foreach (RefinedKind k in System.Enum.GetValues(typeof(RefinedKind)))
                foreach (QualityTier q in System.Enum.GetValues(typeof(QualityTier)))
                {
                    RefinedKind kk = k; QualityTier qq = q;
                    Add(Refined(k, q), wh.Get(k, q), n => wh.Take(kk, qq, n));
                }
            Add(Potions, wh.Potions, n => wh.TakePotionsHere(n));
        }

        ArmoryController arm = b.GetComponent<ArmoryController>();
        if (arm != null)
        {
            foreach (var w in arm.Stock) { Weapon ww = w; Add(Of(ww), 1, n => arm.RemoveItem(ww) ? 1 : 0); }
            foreach (var s in arm.Shields) { Shield ss = s; Add(Of(ss), 1, n => arm.RemoveItem(ss) ? 1 : 0); }
            foreach (var g in arm.Bodies) { BodyGear gg = g; Add(Of(gg), 1, n => arm.RemoveItem(gg) ? 1 : 0); }
        }

        EngineerYard yard = b.GetComponent<EngineerYard>();
        if (yard != null)
            foreach (var kit in yard.Stock) { ModuleKit kk = kit; Add(Of(kk), 1, n => TakeKit(yard.Stock, kk)); }
        return list;
    }

    static int TakeKit(List<ModuleKit> from, ModuleKit kit)
    {
        int i = from.FindIndex(x => x.kind == kit.kind && x.mk == kit.mk && x.charges == kit.charges);
        if (i < 0) return 0;
        from.RemoveAt(i);
        return 1;
    }

    // ---------- Жребий ----------

    /// <summary>
    /// Разрушили здание: по каждой штуке - жребий на копию и на кражу (см. SpoilsRules). budget - сколько очков
    /// ещё можно взять (уменьшается). Украденное у себя дома сразу снимается со склада (SpoilStack.take).
    /// Возвращает, сколько очков потрачено.
    /// </summary>
    public static int Roll(List<SpoilStack> stash, ref int budget, SpoilBundle stolen, SpoilBundle copies)
    {
        if (stash == null || stash.Count == 0 || budget <= 0) return 0;

        // Сколько можно украсть из каждой группы: защищённая половина остаётся у хозяина
        var groupTotal = new Dictionary<string, int>();
        foreach (var s in stash)
        {
            if (s == null || s.count <= 0) continue;
            groupTotal.TryGetValue(s.group, out int t);
            groupTotal[s.group] = t + s.count;
        }
        var stealable = new Dictionary<string, int>();
        foreach (var kv in groupTotal) stealable[kv.Key] = Mathf.FloorToInt(kv.Value * (1f - SpoilsRules.ProtectedShare));

        // Жребий по каждой штуке; потом выпавшее - в случайном порядке, пока хватает потолка
        var events = new List<int>();
        for (int i = 0; i < stash.Count; i++)
        {
            SpoilStack s = stash[i];
            if (s == null || s.count <= 0) continue;
            int trials = Mathf.Min(s.count, SpoilsRules.MaxTrials);
            for (int u = 0; u < trials; u++)
            {
                float r = Random.value;
                if (r < SpoilsRules.StealChance) events.Add(i * 2 + 1);
                else if (r < SpoilsRules.StealChance + SpoilsRules.CopyChance) events.Add(i * 2);
            }
        }
        for (int i = events.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int t = events[i]; events[i] = events[j]; events[j] = t;
        }

        int used = 0;
        foreach (int ev in events)
        {
            SpoilStack s = stash[ev / 2];
            int w = Weight(s.key);
            if (w > budget) continue;
            if ((ev & 1) == 1)
            {
                if (s.count <= 0 || !stealable.TryGetValue(s.group, out int can) || can <= 0) continue;   // защищено
                int got = s.take != null ? s.take(1) : 1;
                if (got <= 0) continue;
                s.count--;
                stealable[s.group] = can - 1;
                stolen.Add(s.key, 1);
            }
            else copies.Add(s.key, 1);
            budget -= w;
            used += w;
        }
        return used;
    }

    // ---------- Положить к себе ----------

    /// <summary>
    /// Положить вещи на склады города (материалы - в Цитадель и Хранилища, снаряжение - в Оружейные...).
    /// Возвращает, сколько штук влезло; остальное вызывающий кладёт в сундук трофеев.
    /// </summary>
    public static int Store(string key, int n)
    {
        if (n <= 0) return 0;
        CityIncome.Skip();   // трофеи - не доход города
        if (IsCounter(key))
        {
            var rm = ResourceManager.Instance;
            if (rm == null) return 0;
            int m = StorageLimits.Room(ResOf(key), n);
            if (m <= 0) return 0;
            switch (key)
            {
                case Potions: rm.potions += m; break;
                case SlowPotions: rm.slowPotions += m; break;
                case Parts: rm.parts += m; break;
                case Tar: rm.tar += m; break;
                default: rm.rubber += m; break;
            }
            return m;
        }

        string[] a = Fields(key);
        switch (Cat(key))
        {
            case 'R':
            {
                var st = TownHallStorage.Instance;
                if (st == null || !ParseRefined(key, out RefinedKind k, out QualityTier q)) return 0;
                int m = Mathf.Min(n, StorageLimits.RefinedRoom(k));
                if (m > 0) st.Add(k, q, m);
                return Mathf.Max(0, m);
            }
            case 'W':
            {
                int put = 0;
                for (; put < n; put++) { Weapon w = SaveBag.UnpackWeapon(a); if (w == null || !ArmoryController.Stash(w)) break; }
                return put;
            }
            case 'H':
            {
                int put = 0;
                for (; put < n; put++) { Shield s = SaveBag.UnpackShield(a); if (s == null || !ArmoryController.StashShield(s)) break; }
                return put;
            }
            case 'B':
            {
                int put = 0;
                for (; put < n; put++) { BodyGear g = SaveBag.UnpackBody(a); if (g == null || !ArmoryController.StashBody(g)) break; }
                return put;
            }
            case 'K':
            {
                ModuleKit kit = SaveBag.UnpackKit(a);
                if (kit.kind == ModuleKind.None) return n;   // пустой модуль - ничего и нет
                for (int i = 0; i < n; i++) EngineerYard.AddToHall(kit);
                return n;
            }
            case 'X':
            {
                int put = 0;
                for (; put < n && !TrapStock.Full; put++) { TrapKit t = SaveBag.UnpackTrap(a); if (t == null) break; TrapStock.Add(t); }
                return put;
            }
        }
        return n;   // неизвестный вид (из будущей версии) - не держим вечно в сундуке
    }

    // ---------- Отчёт защитнику: что у него украли (через ruinedAt - старый сервер передаёт его как есть) ----------
    // Запись: «#код*кол-во», не длиннее 16 знаков (столько пропускает сервер). Код - вид без подробностей:
    // R{вид}{качество}, P/S/D/T/U, W{вид}.{кач}, H{кач}, B{вид}.{кач}, K{вид}.{марка}, X{вид}.{модиф}.{марка}.

    public const char TokenMark = '#';

    public static bool IsToken(string s) { return !string.IsNullOrEmpty(s) && s[0] == TokenMark; }

    static string Code(string key)
    {
        if (IsCounter(key)) return key;
        string[] a = Fields(key);
        switch (Cat(key))
        {
            case 'R': return "R" + SaveBag.At(a, 0) + SaveBag.At(a, 1);
            case 'W': return "W" + SaveBag.At(a, 0) + "." + SaveBag.At(a, 1);
            case 'H': return "H" + SaveBag.At(a, 0);
            case 'B': return "B" + SaveBag.At(a, 0) + "." + SaveBag.At(a, 1);
            case 'K': return "K" + SaveBag.At(a, 0) + "." + SaveBag.At(a, 1, 1);
            case 'X': return "X" + SaveBag.At(a, 0) + "." + SaveBag.At(a, 1) + "." + SaveBag.At(a, 2, 1);
        }
        return null;
    }

    /// <summary>Украденное - записи для отчёта защитнику.</summary>
    public static List<string> Tokens(SpoilBundle stolen)
    {
        var res = new List<string>();
        if (stolen == null) return res;
        var merged = new SpoilBundle();   // одинаковые коды (два разных меча) - одной записью
        for (int i = 0; i < stolen.Kinds; i++)
        {
            string c = Code(stolen.KeyAt(i));
            if (c != null) merged.Add(c, stolen.CountAt(i));
        }
        for (int i = 0; i < merged.Kinds; i++)
        {
            int left = merged.CountAt(i);
            while (left > 0)
            {
                int n = Mathf.Min(left, 999);
                res.Add(TokenMark + merged.KeyAt(i) + "*" + SaveBag.I(n));
                left -= n;
            }
        }
        return res;
    }

    static bool ParseToken(string s, out string code, out int n)
    {
        code = null; n = 0;
        if (!IsToken(s)) return false;
        int star = s.LastIndexOf('*');
        if (star <= 1) return false;
        code = s.Substring(1, star - 1);
        n = SaveBag.ParseInt(s.Substring(star + 1));
        return n > 0;
    }

    static int[] CodeNums(string code)
    {
        string body = code.Substring(1);
        if (code[0] == 'R') return body.Length == 2 ? new[] { body[0] - '0', body[1] - '0' } : new[] { -1, -1 };
        string[] a = body.Split('.');
        var r = new int[a.Length];
        for (int i = 0; i < a.Length; i++) r[i] = SaveBag.ParseInt(a[i], -1);
        return r;
    }

    static int Num(int[] a, int i) { return a != null && i < a.Length ? a[i] : -1; }

    // Строка вещи (SaveBag.Pack) подходит под код
    static bool Matches(char cat, int[] want, string[] row)
    {
        switch (cat)
        {
            case 'W': case 'B': return SaveBag.At(row, 0, -2) == Num(want, 0) && SaveBag.At(row, 1, -2) == Num(want, 1);
            case 'H': return SaveBag.At(row, 0, -2) == Num(want, 0);
            case 'K': return SaveBag.At(row, 0, -2) == Num(want, 0) && SaveBag.At(row, 1, 1) == Num(want, 1);
            case 'X': return SaveBag.At(row, 0, -2) == Num(want, 0) && SaveBag.At(row, 1, -2) == Num(want, 1) && SaveBag.At(row, 2, 1) == Num(want, 2);
        }
        return false;
    }

    /// <summary>
    /// Защитник: вычесть украденное из сохранённого города (вход в игру - сервер уже учёл остальные потери).
    /// Чего уже нет (успели потратить) - не вычитается. Возвращает, сколько штук вычли.
    /// </summary>
    public static int RemoveFromCity(CityData c, List<string> tokens)
    {
        if (c == null || tokens == null) return 0;
        int removed = 0;
        foreach (var tok in tokens)
        {
            if (!ParseToken(tok, out string code, out int n)) continue;
            try { removed += RemoveFromCity(c, code, n); }
            catch (System.Exception e) { Debug.LogWarning("[Трофеи] " + tok + ": " + e.Message); }
        }
        return removed;
    }

    static int Cut(ref int have, int n) { int m = Mathf.Clamp(have, 0, n); have -= m; return m; }

    static int RemoveFromCity(CityData c, string code, int n)
    {
        int left = n;
        char cat = code[0];
        int[] want = CodeNums(code);

        if (cat == 'R')
        {
            int k = Num(want, 0), q = Num(want, 1);
            if (c.refined != null)
                foreach (var r in c.refined)
                    if (left > 0 && r != null && r.kind == k && r.tier == q) left -= Cut(ref r.amount, left);
            if (c.refined != null) c.refined.RemoveAll(r => r == null || r.amount <= 0);
            if (c.buildings != null)
                foreach (var sb in c.buildings)
                {
                    if (left <= 0) break;
                    if (sb == null || sb.type != WarehouseController.TypeName || sb.state == null) continue;
                    var bag = new SaveBag(sb.state);
                    var rows = bag.List("wh.stock");
                    foreach (var a in rows)
                        if (left > 0 && SaveBag.At(a, 0, -1) == k && SaveBag.At(a, 1, -1) == q)
                        {
                            int have = SaveBag.At(a, 2);
                            int m = Mathf.Min(have, left);
                            a[2] = SaveBag.I(have - m);
                            left -= m;
                        }
                    rows.RemoveAll(a => SaveBag.At(a, 2) <= 0);
                    bag.SetList("wh.stock", rows);
                }
            return n - left;
        }

        switch (code)
        {
            case Potions:
                left -= Cut(ref c.potions, left);
                if (c.buildings != null)
                    foreach (var sb in c.buildings)
                    {
                        if (left <= 0) break;
                        if (sb == null || sb.type != WarehouseController.TypeName || sb.state == null) continue;
                        var bag = new SaveBag(sb.state);
                        int have = bag.Int("wh.potions");
                        int m = Mathf.Min(have, left);
                        if (m <= 0) continue;
                        bag.Set("wh.potions", have - m);
                        left -= m;
                    }
                return n - left;
            case SlowPotions: return Cut(ref c.slowPotions, n);
            case Parts: return Cut(ref c.parts, n);
            case Tar: return Cut(ref c.tar, n);
            case Rubber: return Cut(ref c.rubber, n);
        }

        // Снаряжение, модули, ловушки - построчно
        int TakeRows(SaveBag bag, string listKey)
        {
            if (left <= 0) return 0;
            var rows = bag.List(listKey);
            int took = 0;
            for (int i = rows.Count - 1; i >= 0 && left > 0; i--)
            {
                if (!Matches(cat, want, rows[i])) continue;
                rows.RemoveAt(i);
                left--;
                took++;
            }
            if (took > 0) bag.SetList(listKey, rows);
            return took;
        }

        if (cat == 'K' || cat == 'X')
        {
            var extra = new SaveBag(c.extra);
            if (cat == 'K') TakeRows(extra, "kits");
            else { TakeRows(extra, "traps"); TakeRows(extra, "trapsBack"); }
        }
        if (c.buildings != null)
            foreach (var sb in c.buildings)
            {
                if (left <= 0) break;
                if (sb == null || sb.state == null) continue;
                var bag = new SaveBag(sb.state);
                if (sb.type == ArmoryController.TypeName)
                {
                    if (cat == 'W') TakeRows(bag, "arm.w");
                    else if (cat == 'H') TakeRows(bag, "arm.s");
                    else if (cat == 'B') TakeRows(bag, "arm.b");
                }
                else if (sb.type == EngineerYard.TypeName && cat == 'K') TakeRows(bag, "yard.kits");
            }
        return n - left;
    }

    /// <summary>Защитник в игре: вычесть украденное прямо из складов (отчёт пришёл, пока игра открыта).</summary>
    public static int RemoveLive(List<string> tokens)
    {
        if (tokens == null) return 0;
        int removed = 0;
        foreach (var tok in tokens)
        {
            if (!ParseToken(tok, out string code, out int n)) continue;
            try { removed += RemoveLive(code, n); }
            catch (System.Exception e) { Debug.LogWarning("[Трофеи] " + tok + ": " + e.Message); }
        }
        return removed;
    }

    static int RemoveLive(string code, int n)
    {
        int left = n;
        char cat = code[0];
        int[] want = CodeNums(code);
        var rm = ResourceManager.Instance;

        if (cat == 'R')
        {
            int k = Num(want, 0), q = Num(want, 1);
            if (k < 0 || k > 5 || q < 0 || q > 2) return 0;
            var st = TownHallStorage.Instance;
            if (st != null)
            {
                int m = Mathf.Min(left, st.Get((RefinedKind)k, (QualityTier)q));
                if (m > 0 && st.TrySpend((RefinedKind)k, (QualityTier)q, m)) left -= m;
            }
            foreach (var w in WarehouseController.All)
                if (left > 0 && w != null) left -= w.Take((RefinedKind)k, (QualityTier)q, left);
            return n - left;
        }

        switch (code)
        {
            case Potions:
                if (rm != null) left -= Cut(ref rm.potions, left);
                foreach (var w in WarehouseController.All) if (left > 0 && w != null) left -= w.TakePotionsHere(left);
                return n - left;
            case SlowPotions: return rm != null ? Cut(ref rm.slowPotions, n) : 0;
            case Parts: return rm != null ? Cut(ref rm.parts, n) : 0;
            case Tar: return rm != null ? Cut(ref rm.tar, n) : 0;
            case Rubber: return rm != null ? Cut(ref rm.rubber, n) : 0;
        }

        if (cat == 'X')
        {
            for (int i = TrapStock.Hall.Count - 1; i >= 0 && left > 0; i--)
                if (TrapStock.Hall[i] != null && Matches(cat, want, SaveBag.Pack(TrapStock.Hall[i]))) { TrapStock.Hall.RemoveAt(i); left--; }
            return n - left;
        }
        if (cat == 'K')
        {
            left -= TakeKits(EngineerYard.HallStock, want, left);
            foreach (var y in EngineerYard.All) if (left > 0 && y != null) left -= TakeKits(y.Stock, want, left);
            return n - left;
        }
        foreach (var a in ArmoryController.All)
        {
            if (left <= 0) break;
            if (a == null) continue;
            if (cat == 'W') foreach (var w in new List<Weapon>(a.Stock)) { if (left > 0 && Matches(cat, want, SaveBag.Pack(w)) && a.RemoveItem(w)) left--; }
            else if (cat == 'H') foreach (var s in new List<Shield>(a.Shields)) { if (left > 0 && Matches(cat, want, SaveBag.Pack(s)) && a.RemoveItem(s)) left--; }
            else if (cat == 'B') foreach (var g in new List<BodyGear>(a.Bodies)) { if (left > 0 && Matches(cat, want, SaveBag.Pack(g)) && a.RemoveItem(g)) left--; }
        }
        return n - left;
    }

    static int TakeKits(List<ModuleKit> from, int[] want, int max)
    {
        int took = 0;
        for (int i = from.Count - 1; i >= 0 && took < max; i--)
            if (Matches('K', want, SaveBag.Pack(from[i]))) { from.RemoveAt(i); took++; }
        return took;
    }

    /// <summary>Записи украденного из отчёта: «доски ×3, меч» (для сообщения защитнику).</summary>
    public static string TokensText(List<string> tokens)
    {
        if (tokens == null) return "";
        var parts = new List<string>();
        foreach (var tok in tokens)
        {
            if (!ParseToken(tok, out string code, out int n)) continue;
            string name = CodeName(code);
            parts.Add(n > 1 ? $"{name} ×{n}" : name);
        }
        return string.Join(", ", parts);
    }

    static string CodeName(string code)
    {
        if (IsCounter(code)) return Name(code);
        int[] a = CodeNums(code);
        switch (code[0])
        {
            case 'R': return Num(a, 0) >= 0 && Num(a, 0) <= 5 && Num(a, 1) >= 0 && Num(a, 1) <= 2 ? Name(Refined((RefinedKind)Num(a, 0), (QualityTier)Num(a, 1))) : "материал";
            case 'W': return Weapon.KindName((WeaponKind)Mathf.Max(0, Num(a, 0)));
            case 'H': return "щит";
            case 'B': return Num(a, 0) == (int)BodyKind.Cloak ? "плащ" : "броня";
            case 'K': return TowerModules.Name((ModuleKind)Mathf.Max(0, Num(a, 0)));
            case 'X': return "ловушка";
        }
        return "вещь";
    }

    /// <summary>Отчёт защитнику: только записи о зданиях (без записей украденного).</summary>
    public static int CountTokens(List<string> list)
    {
        int n = 0;
        if (list != null) foreach (var s in list) if (IsToken(s)) n++;
        return n;
    }
}

/// <summary>
/// Сундук трофеев: вещи, которым пока нет места на складах (или трофеи набега, пока армия идёт домой).
/// Раз в несколько секунд сам раскладывает их по складам. Сохраняется с городом (CitySave, «spoils.*»).
/// Здесь же - сколько очков трофеев уже взято за сутки (потолок SpoilsRules.DailyCap).
/// </summary>
public static class SpoilsChest
{
    public static readonly SpoilBundle Box = new SpoilBundle();       // ждут места
    public static readonly SpoilBundle Pending = new SpoilBundle();   // украдено в набеге, ждём ответа сервера
    static int dayIndex = -1, dayPoints;
    static float tickTimer;
    static int lastToldUnits = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Box.Clear(); Pending.Clear(); dayIndex = -1; dayPoints = 0; tickTimer = 0f; lastToldUnits = -1; }

    static int Today => (int)(System.DateTime.UtcNow - new System.DateTime(2026, 1, 1)).TotalDays;

    /// <summary>Сколько очков трофеев ещё можно взять сегодня.</summary>
    public static int DailyLeft(int hallLevel)
    {
        if (dayIndex != Today) { dayIndex = Today; dayPoints = 0; }
        return Mathf.Max(0, SpoilsRules.DailyCap(hallLevel) - dayPoints);
    }

    public static void UseDaily(int points)
    {
        if (dayIndex != Today) { dayIndex = Today; dayPoints = 0; }
        dayPoints += Mathf.Max(0, points);
    }

    public static void Put(SpoilBundle b)
    {
        if (b == null || b.IsEmpty) return;
        Box.Add(b);
        tickTimer = 0f;
        AccountManager.SaveSoon();
    }

    /// <summary>Раскладывать сейчас можно: дома, город загружен, армия не в пути (трофеи она и несёт).</summary>
    static bool CanDeliver(bool armyHome) =>
        !WarCityMode.Active && CityBoot.Started && BuildingPlacer.Instance != null && BuildingPlacer.Instance.WorldReady &&
        (armyHome || RaidManager.Instance == null || RaidManager.Instance.State == RaidManager.Phase.Idle);

    public static void Tick(float dt)
    {
        if (Box.IsEmpty) return;
        tickTimer -= dt;
        if (tickTimer > 0f) return;
        tickTimer = 3f;
        Deliver(false);
    }

    /// <summary>Армия вернулась: разложить сейчас (RaidManager.ReturnHome), чтобы в отчёте было видно, что не влезло.</summary>
    public static void DeliverNow() { if (!Box.IsEmpty) Deliver(true); }

    static void Deliver(bool armyHome)
    {
        if (!CanDeliver(armyHome)) return;

        var stored = new SpoilBundle();
        for (int i = Box.Kinds - 1; i >= 0; i--)
        {
            string key = Box.KeyAt(i);
            int n = Box.CountAt(i);
            int put = Mathf.Clamp(Spoils.Store(key, n), 0, n);
            if (put <= 0) continue;
            Box.Remove(key, put);
            stored.Add(key, put);
        }
        if (!stored.IsEmpty)
        {
            WarManager.Notify($"Трофеи разложены по складам: {stored.Text}", true);
            AccountManager.SaveSoon();
        }
        int left = Box.Units;
        if (left > 0 && left != lastToldUnits)
            WarManager.Notify($"В сундуке трофеев ждут места: {Box.Text}. Освободите место на складах (Цитадель, Хранилище, Оружейная)", false);
        lastToldUnits = left;
    }

    public static string Line => Box.IsEmpty ? "" : $"Сундук трофеев (ждут места на складах): {Box.Text}";

    // ---------- Сохранение (CityData.extra) ----------

    public static void Save(SaveBag bag)
    {
        var all = Box.Copy();
        all.Add(Pending);   // ответ сервера не пришёл - при входе считаем, что набег засчитан
        all.Add(HomeSiege.HeldSpoils());   // живой бой у нас ещё идёт: унесённое врагами пока наше
        bag.Set("spoils.box", all.Encode());
        DailyLeft(1);   // сменились сутки - счёт с нуля
        bag.Set("spoils.day", SaveBag.I(dayIndex) + "," + SaveBag.I(dayPoints));
    }

    public static void Load(SaveBag bag)
    {
        Box.Clear();
        Pending.Clear();
        Box.Add(SpoilBundle.Decode(bag.Get("spoils.box")));
        string d = bag.Get("spoils.day");
        if (d != null)
        {
            string[] a = d.Split(',');
            dayIndex = SaveBag.At(a, 0, -1);
            dayPoints = SaveBag.At(a, 1);
        }
        tickTimer = 2f;
        lastToldUnits = -1;
    }
}
