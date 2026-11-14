using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// ---------- Что лежит внутри построек и в запасах города: сохранение и загрузка ----------
// Раньше с городом сохранялись только сами постройки, казна и армия. Курьеры, рабочие, склады Хранилищ,
// Оружейных, Конюшен, модули на башнях, ловушки и прочее после перезахода пропадали.
// Теперь каждое здание с «начинкой» пишет её в свой SavedBuilding.state (ISavedBuilding),
// а запасы города (ловушки, модули, Дальний склад, рационы...) - в CityData.extra.

/// <summary>Здание умеет сохранить то, что у него внутри. Ключи у каждого вида свои (с приставкой).</summary>
public interface ISavedBuilding
{
    void SaveState(SaveBag bag);
    void LoadState(SaveBag bag);
}

/// <summary>
/// Записи «ключ=значение» поверх списка строк (так их понимает JsonUtility и в игре, и на сервере).
/// Списки вещей: значения через «;», поля одной вещи через «,». Дробные числа - с точкой.
/// </summary>
public class SaveBag
{
    readonly List<string> list;
    public SaveBag(List<string> list) { this.list = list ?? new List<string>(); }
    public bool Empty => list.Count == 0;

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public void Set(string key, string value)
    {
        string pre = key + "=";
        list.RemoveAll(s => s != null && s.StartsWith(pre, StringComparison.Ordinal));
        if (!string.IsNullOrEmpty(value)) list.Add(pre + value);
    }

    public string Get(string key)
    {
        string pre = key + "=";
        foreach (var s in list)
            if (s != null && s.StartsWith(pre, StringComparison.Ordinal)) return s.Substring(pre.Length);
        return null;
    }

    public bool Has(string key) { return Get(key) != null; }

    public void Set(string key, int v) { Set(key, v.ToString(Inv)); }
    public void Set(string key, float v) { Set(key, v.ToString("R", Inv)); }
    public void Set(string key, bool v) { Set(key, v ? "1" : "0"); }

    public int Int(string key, int def = 0) { return ParseInt(Get(key), def); }
    public float Float(string key, float def = 0f) { return ParseFloat(Get(key), def); }
    public bool Bool(string key, bool def = false) { string s = Get(key); return s == null ? def : s == "1"; }

    /// <summary>Список записей (каждая - массив полей).</summary>
    public void SetList(string key, List<string[]> items)
    {
        if (items == null || items.Count == 0) { Set(key, (string)null); return; }
        var parts = new List<string>(items.Count);
        foreach (var it in items) parts.Add(string.Join(",", it));
        Set(key, string.Join(";", parts));
    }

    public List<string[]> List(string key)
    {
        var res = new List<string[]>();
        string s = Get(key);
        if (string.IsNullOrEmpty(s)) return res;
        foreach (var part in s.Split(';'))
            if (part.Length > 0) res.Add(part.Split(','));
        return res;
    }

    public static string I(int v) { return v.ToString(Inv); }
    public static string F(float v) { return v.ToString("R", Inv); }
    public static string B(bool v) { return v ? "1" : "0"; }
    public static int ParseInt(string s, int def = 0) { return int.TryParse(s, NumberStyles.Integer, Inv, out int v) ? v : def; }
    public static float ParseFloat(string s, float def = 0f) { return float.TryParse(s, NumberStyles.Float, Inv, out float v) ? v : def; }
    public static int At(string[] a, int i, int def = 0) { return a != null && i < a.Length ? ParseInt(a[i], def) : def; }
    public static float AtF(string[] a, int i, float def = 0f) { return a != null && i < a.Length ? ParseFloat(a[i], def) : def; }
    public static bool AtB(string[] a, int i) { return a != null && i < a.Length && a[i] == "1"; }

    // ---------- Вещи ----------

    public static string[] Pack(Weapon w)
    {
        return new[] { I((int)w.kind), I((int)w.quality), I(w.upgrade), F(w.durability), F(w.maxDurability), I(w.poisonCharges), I(w.slowCharges),
                       B(w.forged), F(w.forgeDamage), F(w.forgeDurability), F(w.forgeSpeed), I(w.forgeStars), I(w.saluteCharges), I(w.saluteMk) };
    }

    public static Weapon UnpackWeapon(string[] a)
    {
        if (a == null || a.Length < 5) return null;
        var w = new Weapon((WeaponKind)At(a, 0), (QualityTier)Mathf.Clamp(At(a, 1), 0, 2))
        {
            upgrade = At(a, 2), poisonCharges = At(a, 5), slowCharges = At(a, 6),
            forged = AtB(a, 7), forgeDamage = AtF(a, 8), forgeDurability = AtF(a, 9), forgeSpeed = AtF(a, 10), forgeStars = At(a, 11),
            saluteCharges = At(a, 12), saluteMk = At(a, 13),   // салют; в старых сохранениях его нет
        };
        float max = AtF(a, 4);
        if (max > 0f) w.maxDurability = max;
        w.durability = Mathf.Clamp(AtF(a, 3), 0f, w.maxDurability);
        return w;
    }

    // 5-й и 6-й - ядро хаоса на щите (марка и заряды); в старых сохранениях их нет
    public static string[] Pack(Shield s) { return new[] { I((int)s.quality), I(s.upgrade), F(s.durability), F(s.maxDurability), I(s.chaosMk), I(s.chaosCharges) }; }

    public static Shield UnpackShield(string[] a)
    {
        if (a == null || a.Length < 4) return null;
        var s = new Shield((QualityTier)Mathf.Clamp(At(a, 0), 0, 2)) { upgrade = At(a, 1), chaosMk = At(a, 4), chaosCharges = At(a, 5) };
        float max = AtF(a, 3);
        if (max > 0f) s.maxDurability = max;
        s.durability = Mathf.Clamp(AtF(a, 2), 0f, s.maxDurability);
        return s;
    }

    // 5-й - усиление брони; в старых сохранениях его нет (0)
    public static string[] Pack(BodyGear g) { return new[] { I((int)g.kind), I((int)g.quality), F(g.durability), F(g.maxDurability), I(g.upgrade) }; }

    public static BodyGear UnpackBody(string[] a)
    {
        if (a == null || a.Length < 4) return null;
        var g = new BodyGear((BodyKind)At(a, 0), (QualityTier)Mathf.Clamp(At(a, 1), 0, 2));
        if (g.IsArmor) g.upgrade = Mathf.Clamp(At(a, 4), 0, BodyGear.MaxUpgrade);
        float max = AtF(a, 3);
        if (max > 0f) g.maxDurability = max;
        g.durability = Mathf.Clamp(AtF(a, 2), 0f, g.maxDurability);
        return g;
    }

    public static string[] Pack(ModuleKit k) { return new[] { I((int)k.kind), I(k.mk), I(k.charges) }; }
    public static ModuleKit UnpackKit(string[] a) { return new ModuleKit { kind = (ModuleKind)At(a, 0), mk = At(a, 1, 1), charges = At(a, 2) }; }

    public static string[] Pack(TrapKit k) { return new[] { I((int)k.type), I((int)k.mods), I(k.mk), B(k.combat), F(k.power), I(k.charges) }; }

    public static TrapKit UnpackTrap(string[] a)
    {
        if (a == null || a.Length < 6) return null;
        return new TrapKit { type = (TrapBase)At(a, 0), mods = (TrapMod)At(a, 1), mk = At(a, 2, 1), combat = AtB(a, 3), power = AtF(a, 4, 1f), charges = At(a, 5, -1) };
    }

    public static List<string[]> PackAll<T>(IEnumerable<T> items, Func<T, string[]> pack)
    {
        var l = new List<string[]>();
        if (items != null) foreach (var it in items) if (it != null) l.Add(pack(it));
        return l;
    }
}

/// <summary>Сохранение «начинки» зданий и запасов города (вызывает BuildingPlacer).</summary>
public static class CitySave
{
    /// <summary>Здание по левой нижней клетке (для ссылок между зданиями: курьеры и т.п.).</summary>
    public static BuildingInstance BuildingAt(int x, int y)
    {
        foreach (var b in BuildingInstance.All)
            if (b != null && b.OriginCell.x == x && b.OriginCell.y == y) return b;
        return null;
    }

    // ---------- Здание ----------

    public static void CaptureBuilding(BuildingInstance inst, SavedBuilding sb)
    {
        sb.manualWorkers = !inst.autoWorkers;
        sb.wantWorkers = inst.desiredWorkers;
        sb.state = new List<string>();
        var bag = new SaveBag(sb.state);
        foreach (var part in inst.GetComponents<ISavedBuilding>())
        {
            try { part.SaveState(bag); }
            catch (Exception e) { Debug.LogWarning("[Сохранение] " + inst.type.name + ": " + e.Message); }
        }
        foreach (var host in inst.GetComponents<IModuleHost>())
            if (host.HasModule) { bag.Set("module", SaveBag.I((int)host.Module) + "," + SaveBag.I(host.ModuleMk)); break; }
        // Финал: все гнёзда здания по порядку (у пушки их два: подъёмник и магические снаряды) и остаток снарядов в пачке.
        // Раньше сохранялось только первое гнездо, а пачка после входа становилась полной.
        var mods = new List<string[]>();
        var hosts = inst.GetComponents<IModuleHost>();
        for (int i = 0; i < hosts.Length; i++)
        {
            if (hosts[i] == null || !hosts[i].HasModule) continue;
            int charges = hosts[i] is CannonShellSlot slot ? slot.Charges : 0;
            mods.Add(new[] { SaveBag.I(i), SaveBag.I((int)hosts[i].Module), SaveBag.I(hosts[i].ModuleMk), SaveBag.I(charges) });
        }
        bag.SetList("modules", mods);
        bag.Set(CityLoss.RuinKey, inst.RuinState());   // руины (BuildingInstance_Ruins); целое - записи нет
    }

    /// <summary>Вызывается, когда все здания уже стоят (ссылки на другие здания находятся).</summary>
    public static void ApplyBuilding(BuildingInstance inst, SavedBuilding sb)
    {
        if (inst == null || sb == null) return;
        if (sb.manualWorkers)
        {
            inst.autoWorkers = false;
            inst.desiredWorkers = Mathf.Max(0, sb.wantWorkers);
        }
        inst.WorkersFromSave = true;

        if (sb.state == null || sb.state.Count == 0) return;
        var bag = new SaveBag(sb.state);
        if (!WarCityMode.Active) inst.RestoreRuin(bag.Get(CityLoss.RuinKey));   // руины и восстановление за время без игрока
        foreach (var part in inst.GetComponents<ISavedBuilding>())
        {
            try { part.LoadState(bag); }
            catch (Exception e) { Debug.LogWarning("[Загрузка] " + inst.type.name + ": " + e.Message); }
        }
        var mods = bag.List("modules");
        if (mods.Count > 0)
        {
            var hosts = inst.GetComponents<IModuleHost>();
            foreach (var a in mods)
            {
                int i = SaveBag.At(a, 0, -1);
                ModuleKind k = (ModuleKind)SaveBag.At(a, 1);
                if (i < 0 || i >= hosts.Length || hosts[i] == null || k == ModuleKind.None || hosts[i].HasModule) continue;
                hosts[i].SetModule(k, SaveBag.At(a, 2, 1));
                if (hosts[i] is CannonShellSlot slot) slot.SetCharges(SaveBag.At(a, 3));
            }
            return;
        }
        string m = bag.Get("module");
        if (m != null)
        {
            string[] a = m.Split(',');
            ModuleKind k = (ModuleKind)SaveBag.At(a, 0);
            foreach (var host in inst.GetComponents<IModuleHost>())
            {
                if (k != ModuleKind.None && !host.HasModule) host.SetModule(k, SaveBag.At(a, 1, 1));
                break;
            }
        }
    }

    // ---------- Город ----------

    public static void CaptureCity(CityData c)
    {
        c.extra = new List<string>();
        var bag = new SaveBag(c.extra);
        try
        {
            // Ловушки и модули в запасе Цитадели
            bag.SetList("traps", SaveBag.PackAll(TrapStock.Hall, SaveBag.Pack));
            bag.SetList("trapsRaid", SaveBag.PackAll(TrapStock.RaidKit, SaveBag.Pack));
            var kits = new List<string[]>();
            foreach (var k in EngineerYard.HallStock) kits.Add(SaveBag.Pack(k));
            bag.SetList("kits", kits);

            // Ловушки, расставленные по городу: боевые после войны и так уходят в запас - их кладём в запас сразу
            var placed = new List<string[]>();
            var combat = new List<string[]>();
            foreach (var t in Trap.All)
            {
                if (t == null || !t.Alive || t.Raid || t.Hostile || t.Kit == null) continue;
                TrapKit kit = t.Kit.Clone();
                kit.charges = t.Charges;
                if (kit.combat) { combat.Add(SaveBag.Pack(kit)); continue; }
                var p = new List<string>(SaveBag.Pack(kit)) { SaveBag.F(t.Position.x), SaveBag.F(t.Position.y) };
                placed.Add(p.ToArray());
            }
            bag.SetList("trapsPlaced", placed);
            bag.SetList("trapsBack", combat);

            // Полевое снаряжение и понтоны (стоящие переправы - секциями в запас)
            var gear = new List<string[]>();
            foreach (var it in FieldGear.All()) gear.Add(new[] { SaveBag.I((int)it.kind), SaveBag.F(it.strength), SaveBag.F(it.pool) });
            bag.SetList("fieldGear", gear);

            // Укрепления и пункты, поставленные у нас в городе: после входа стоят там же
            var posts = new List<string[]>();
            foreach (var p in FieldPost.All)
            {
                if (p == null || !p.Alive || p.Raid || p.Foreign || p.Puppet) continue;
                float frac = p.CurMaxHp > 0f ? p.Hp / p.CurMaxHp : 1f;
                posts.Add(new[] { SaveBag.I((int)p.Kind), SaveBag.F(p.Position.x), SaveBag.F(p.Position.y), SaveBag.F(p.Angle),
                                  SaveBag.F(p.Strength), SaveBag.F(p.Pool), SaveBag.F(p.Progress), SaveBag.F(frac) });
            }
            bag.SetList("postsHome", posts);
            int pontoons = PontoonStock.Count;
            foreach (var cr in Crossing.All) if (cr != null) pontoons += cr.Held;
            bag.Set("pontoons", pontoons);

            // Дальний склад
            FarStorage fs = FarStorage.Existing;
            if (fs != null) fs.SaveState(bag);

            // Рационы и режим накопления
            if (CityConsumption.Instance != null)
            {
                var r = CityConsumption.Instance.ration;
                bag.Set("rations", string.Join(",", Array.ConvertAll(r, SaveBag.F)));
            }
            bag.Set("fogHome", FogOfWar.SaveHomeExplored());   // разведанный туман нашей карты
            bag.Set("accumulate", AccumulationMode.On);
            bag.Set("accumulateSince", AccumulationMode.OnSeconds);
            bag.Set("accumulateRate", AccumulationMode.On ? CityIncome.ModeRateText() : "");
            if (!WarCityMode.Active) SpoilsChest.Save(bag);   // сундук трофеев (обновление 23)
            CarryLedger.Save(bag);                            // вещи в руках у жителей (final-1)
            SaveCourierCargo(bag);                            // груз курьеров по дороге в Цитадель (sohranenie-1)
            if (TradeGuild.Instance != null && !WarCityMode.Active) TradeGuild.Instance.SaveState(bag);   // лавки и покупки гильдии (sohranenie-1)
            bool bonus = (TradeGuild.Instance != null && TradeGuild.Instance.BonusGiven) || MarketHouse.Main != null;
            bag.Set("guild.bonus", bonus);                    // подъёмные гильдии выданы - больше не давать (final-1)
        }
        catch (Exception e) { Debug.LogWarning("[Сохранение] запасы города: " + e.Message); }

        // Идущие набеги: наш поход и враги на нашей карте - продолжатся после входа
        try
        {
            RaidManager.SaveState(bag);
            WarManager.SaveState(bag);
            SquadSave.SaveState(bag);
            CamoScouts.SaveState(bag);   // дозоры (final-4)
            CitizenFavorites.SaveState(bag);   // избранные жители (обновление 12)
        }
        catch (Exception e) { Debug.LogWarning("[Сохранение] набег: " + e.Message); }
    }

    // ---------- Груз курьеров (sohranenie-1) ----------
    // Курьер забрал склад здания и несёт его в Цитадель. Раньше при выходе из игры в этот момент груз пропадал.
    // Теперь он записывается и после входа уже лежит в казне (донесли, пока игрока не было).

    static void SaveCourierCargo(SaveBag bag)
    {
        int[] load = new int[10];   // золото, дерево, камень, еда, руда, овёс, ягоды, травы, смола, листва
        bool any = false;
        foreach (var a in CitizenAgent.All)
            if (a != null && a.CourierCargoForSave(load)) any = true;
        bag.Set("carry.res", any ? string.Join(",", Array.ConvertAll(load, SaveBag.I)) : null);
    }

    static void LoadCourierCargo(SaveBag bag)
    {
        string t = bag.Get("carry.res");
        var rm = ResourceManager.Instance;
        if (string.IsNullOrEmpty(t) || rm == null) return;
        string[] a = t.Split(',');
        int[] g = new int[10];
        for (int i = 0; i < g.Length; i++) g[i] = Mathf.Max(0, SaveBag.At(a, i));
        CityIncome.Skip();   // это не доход за время отсутствия, а донесённое
        rm.Add(g[0], g[1], g[2], g[3], g[4], g[5], g[6], g[7], 0, g[8]);
        rm.AddLeaves(g[9]);
    }

    public static void ApplyCity(CityData c)
    {
        if (c == null || c.extra == null || c.extra.Count == 0) return;
        var bag = new SaveBag(c.extra);
        try
        {
            TrapStock.Hall.Clear();
            foreach (var a in bag.List("traps")) { var k = SaveBag.UnpackTrap(a); if (k != null) TrapStock.Hall.Add(k); }
            foreach (var a in bag.List("trapsBack")) { var k = SaveBag.UnpackTrap(a); if (k != null) TrapStock.Hall.Add(k); }
            TrapStock.RaidKit.Clear();
            foreach (var a in bag.List("trapsRaid")) { var k = SaveBag.UnpackTrap(a); if (k != null) TrapStock.RaidKit.Add(k); }
            EngineerYard.HallStock.Clear();
            foreach (var a in bag.List("kits")) EngineerYard.HallStock.Add(SaveBag.UnpackKit(a));

            foreach (var a in bag.List("trapsPlaced"))
            {
                TrapKit k = SaveBag.UnpackTrap(a);
                if (k == null) continue;
                Vector2 pos = new Vector2(SaveBag.AtF(a, 6), SaveBag.AtF(a, 7));
                Trap.Restore(k, pos);
            }

            foreach (var a in bag.List("fieldGear"))
            {
                int kind = SaveBag.At(a, 0);
                if (kind < 0 || kind >= FieldGear.Kinds) continue;
                var it = new FieldGear.Item((PostKind)kind, SaveBag.AtF(a, 1, 1f));
                it.pool = Mathf.Clamp(SaveBag.AtF(a, 2, it.MaxPool), 0f, it.MaxPool);
                FieldGear.Add(it);
            }
            PontoonStock.Set(bag.Int("pontoons", PontoonStock.Count));

            if (!WarCityMode.Active)
                foreach (var a in bag.List("postsHome"))
                {
                    int kind = SaveBag.At(a, 0, -1);
                    if (kind < 0 || kind >= FieldPost.Specs.Length) continue;
                    FieldPost p = FieldPost.Create((PostKind)kind, new Vector2(SaveBag.AtF(a, 1), SaveBag.AtF(a, 2)), SaveBag.AtF(a, 3), false,
                                                   SaveBag.AtF(a, 4, 1f), SaveBag.AtF(a, 5, -1f));
                    if (p != null) p.RestoreSaved(SaveBag.AtF(a, 6, 1f), SaveBag.AtF(a, 7, 1f));
                }

            if (bag.Has("farCells")) FarStorage.Get().LoadState(bag);

            string rs = bag.Get("rations");
            if (rs != null && CityConsumption.Instance != null)
            {
                string[] a = rs.Split(',');
                var r = CityConsumption.Instance.ration;
                for (int i = 0; i < r.Length && i < a.Length; i++) r[i] = Mathf.Clamp01(SaveBag.ParseFloat(a[i], 1f));
            }
            AccumulationMode.Restore(bag.Bool("accumulate"), bag.Float("accumulateSince"));
            CityIncome.LoadModeRate(bag.Get("accumulateRate"));
            FogOfWar.LoadHomeExplored(bag.Get("fogHome"));
            if (!WarCityMode.Active) SpoilsChest.Load(bag);
            CarryLedger.Load(bag);   // что жители несли при сохранении - на склады (final-1)
            LoadCourierCargo(bag);   // груз курьеров - в казне (sohranenie-1)
            // Подъёмные Торгового дома - один раз. Старое сохранение без записи: если Торговый дом уже стоит, их уже давали
            bool hadMarket = false;
            if (c.buildings != null) foreach (var sb in c.buildings) if (sb != null && sb.type == MarketHouse.TypeName) { hadMarket = true; break; }
            if (bag.Bool("guild.bonus") || (!bag.Has("guild.bonus") && hadMarket)) TradeGuild.Ensure().MarkBonusGiven();
            if (!WarCityMode.Active && (bag.Has("guild.stat") || bag.Has("guild.lots") || bag.Has("guild.items") || bag.Has("guild.back") || bag.Has("guild.due")))
            {
                try { TradeGuild.Ensure().LoadState(bag); }
                catch (Exception e) { Debug.LogWarning("[Загрузка] гильдия: " + e.Message); }
            }
        }
        catch (Exception e) { Debug.LogWarning("[Загрузка] запасы города: " + e.Message); }

        try
        {
            CityArmy.PendingGear.Clear();
            CityArmy.PendingGear.AddRange(bag.List("army.gear"));   // полевые пушки (sohranenie-1)
            CityArmy.PendingNames.Clear();
            CityArmy.PendingNames.AddRange(bag.List("army.names"));   // имена и избранные бойцы (обновление 12)
            // Добыча, которую бойцы несли домой, когда игрок вышел (обновление 14): раньше пропадала, теперь - в казне
            var armyLoot = bag.List("army.loot");
            if (armyLoot.Count > 0 && !WarCityMode.Active)
            {
                Loot carriedHome = LootSave.Unpack(armyLoot[0], 0);
                if (!carriedHome.IsEmpty) JobSave.AfterLoad(() => carriedHome.DepositToTown());
            }
            CitizenFavorites.LoadState(bag);
            RaidManager.LoadState(c, bag);
            WarManager.LoadState(c, bag);
            SquadSave.LoadState(bag);
            CamoScouts.LoadState(bag);
        }
        catch (Exception e) { Debug.LogWarning("[Загрузка] набег: " + e.Message); }
    }
}
