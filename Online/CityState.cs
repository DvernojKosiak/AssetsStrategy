using System;
using System.Collections.Generic;
using UnityEngine;

// ---------- Город игрока: что сохраняется на сервере ----------
// Карта (реки, зоны ресурсов, кусты) не хранится - она строится заново из seed.
// Постройки, ресурсы, выбранный путь, жители и игровое время - хранятся.

/// <summary>
/// Город, с которого начинается игра. BuildingPlacer ждёт его (WhenReady), а AccountManager
/// отдаёт (Provide) после входа: сохранённый город или null - новый.
/// </summary>
public static class CityBoot
{
    static bool provided;
    static CityData data;
    static Action<CityData> waiting;

    /// <summary>Город уже передан игре (дальше его можно сохранять).</summary>
    public static bool Started { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { provided = false; data = null; waiting = null; Started = false; }

    public static void WhenReady(Action<CityData> build)
    {
        if (provided) { Started = true; build(data); return; }
        waiting = build;
    }

    public static void Provide(CityData city)
    {
        if (provided) return;
        provided = true;
        data = city;
        if (waiting != null)
        {
            Action<CityData> w = waiting;
            waiting = null;
            Started = true;
            w(data);
        }
    }

    // ---------- Казна, путь, жители, время ----------

    public static void CaptureCommon(CityData c)
    {
        ResourceManager rm = ResourceManager.Instance;
        if (rm != null)
        {
            c.gold = rm.gold; c.wood = rm.wood; c.stone = rm.stone; c.food = rm.food;
            c.iron = rm.iron; c.oats = rm.oats; c.berries = rm.berries; c.herbs = rm.herbs;
            c.potions = rm.potions; c.parts = rm.parts; c.leaves = rm.leaves; c.resin = rm.resin;
            c.tar = rm.tar; c.rubber = rm.rubber; c.slowPotions = rm.slowPotions; c.marks = rm.marks;
        }

        c.refined.Clear();
        TownHallStorage st = TownHallStorage.Instance;
        if (st != null)
            foreach (RefinedKind k in Enum.GetValues(typeof(RefinedKind)))
                foreach (QualityTier q in Enum.GetValues(typeof(QualityTier)))
                {
                    int n = st.Get(k, q);
                    if (n > 0) c.refined.Add(new SavedRefined { kind = (int)k, tier = (int)q, amount = n });
                }

        c.path = (int)DevelopmentPath.Chosen;
        c.school = (int)DevelopmentPath.School;
        c.eng = (int)DevelopmentPath.Eng;
        c.war = (int)DevelopmentPath.War;

        if (PopulationManager.Instance != null) c.population = PopulationManager.Instance.population;
        if (GameClock.Instance != null)
        {
            c.day = GameClock.Instance.DayIndex;
            c.timeInDay = GameClock.Instance.TimeInDay;
        }
    }

    public static void ApplyCommon(CityData c)
    {
        ResourceManager rm = ResourceManager.Instance;
        if (rm != null)
        {
            rm.gold = c.gold; rm.wood = c.wood; rm.stone = c.stone; rm.food = c.food;
            rm.iron = c.iron; rm.oats = c.oats; rm.berries = c.berries; rm.herbs = c.herbs;
            rm.potions = c.potions; rm.parts = c.parts; rm.leaves = c.leaves; rm.resin = c.resin;
            rm.tar = c.tar; rm.rubber = c.rubber; rm.slowPotions = c.slowPotions; rm.marks = c.marks;
        }

        TownHallStorage st = TownHallStorage.Instance;
        if (st != null && c.refined != null)
        {
            // Сначала обнуляем склад, потом кладём сохранённое
            foreach (RefinedKind k in Enum.GetValues(typeof(RefinedKind)))
                foreach (QualityTier q in Enum.GetValues(typeof(QualityTier)))
                    st.TrySpend(k, q, st.Get(k, q));
            foreach (var r in c.refined)
                if (r != null && r.amount > 0) st.Add((RefinedKind)r.kind, (QualityTier)r.tier, r.amount);
        }

        DevelopmentPath.RestoreSaved((PathKind)c.path, (MagicSchool)c.school, (EngSchool)c.eng, (WarSchool)c.war);

        // Бойцы армии появятся отдельно (CityArmy.Restore) и сами прибавятся к жителям
        int armyCount = c.army != null ? c.army.Count : 0;
        if (PopulationManager.Instance != null && c.population > 0) PopulationManager.Instance.population = Mathf.Max(1, c.population - armyCount);
        if (GameClock.Instance != null) GameClock.Instance.RestoreTime(c.day, c.timeInDay);
    }
}

/// <summary>Армия города: снимок бойцов из казарм и их возвращение после загрузки.</summary>
public static class CityArmy
{
    /// <summary>Бойцы ещё возвращаются в казармы - город пока не сохраняем (иначе потеряем армию).</summary>
    public static bool Restoring { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Restoring = false; PendingGear.Clear(); PendingNames.Clear(); }

    /// <summary>Имена и звёздочки бойцов из сохранения, по номеру в c.army (обновление 12).</summary>
    public static readonly List<string[]> PendingNames = new List<string[]>();

    /// <summary>Подъёмники и пачки снарядов полевых пушек из сохранения (CitySave.ApplyCity), по номеру бойца в c.army.</summary>
    public static readonly List<string[]> PendingGear = new List<string[]>();

    static string GearName(string unit) { return (unit ?? "").Replace(',', ' ').Replace(';', ' ').Replace('=', ' '); }

    public static void Capture(CityData c)
    {
        c.army.Clear();
        var gear = new List<string[]>();   // полевые пушки: подъёмник, снаряды, настройки (sohranenie-1)
        var names = new List<string[]>();  // имена и звёздочки бойцов, по порядку армии (обновление 12)
        var carriedHome = new Loot();      // что бойцы несут в казну (обновление 14): после входа - сразу в казне
        foreach (var b in BuildingInstance.All)
        {
            if (b == null) continue;
            BarracksController br = b.GetComponent<BarracksController>();
            if (br == null) continue;
            foreach (var a in br.Soldiers)
            {
                if (a == null || a.IsDead || !a.IsSoldier) continue;
                var s = new SavedSoldier { unit = a.UnitName, bx = b.OriginCell.x, by = b.OriginCell.y, hearty = a.IsHearty, merc = a.IsMercenary };
                Weapon w = a.EquippedWeapon;
                if (w != null)
                {
                    s.hasWeapon = true;
                    s.wKind = (int)w.kind; s.wQuality = (int)w.quality; s.wUpgrade = w.upgrade;
                    s.wDur = w.durability; s.wMaxDur = w.maxDurability;
                    s.wPoison = w.poisonCharges; s.wSlow = w.slowCharges;
                    s.wForged = w.forged; s.wForgeDamage = w.forgeDamage; s.wForgeDurability = w.forgeDurability;
                    s.wForgeSpeed = w.forgeSpeed; s.wStars = w.forgeStars;
                    s.wSalute = w.saluteCharges; s.wSaluteMk = w.saluteMk;
                }
                Shield sh = ShieldBearer.ShieldOf(a);
                if (sh != null)
                {
                    s.hasShield = true;
                    s.sQuality = (int)sh.quality; s.sUpgrade = sh.upgrade;
                    s.sDur = sh.durability; s.sMaxDur = sh.maxDurability;
                    s.sChaosMk = sh.chaosMk; s.sChaosCharges = sh.chaosCharges;
                }
                if (a.HasChaosCore) { s.cMk = a.ChaosCoreMk; s.cCharges = a.ChaosCharges; }
                if (a.IsRamCarrier) { s.ramBroken = a.RamBroken; s.ramWear = a.RamBroken ? 0f : a.RamWear; }
                BodyGear g = a.EquippedBody;
                if (g != null)
                {
                    s.hasBody = true;
                    s.bKind = (int)g.kind; s.bQuality = (int)g.quality;
                    s.bDur = g.durability; s.bMaxDur = g.maxDurability;
                    s.bUpgrade = g.upgrade;
                }
                if (a.CannonGearForSave(out int liftMk, out ModuleKind shell, out int shellMk, out int charges, out ModuleKind pref, out bool auto))
                {
                    // Бойца узнаём по виду, казарме и номеру среди таких же: набег мог убрать кого-то из списка армии
                    int ord = 0;
                    foreach (var o in c.army) if (o != null && o.unit == s.unit && o.bx == s.bx && o.by == s.by) ord++;
                    gear.Add(new[] { GearName(s.unit), SaveBag.I(s.bx), SaveBag.I(s.by), SaveBag.I(ord), SaveBag.I(liftMk), SaveBag.I((int)shell),
                                     SaveBag.I(shellMk), SaveBag.I(charges), SaveBag.I((int)pref), SaveBag.B(auto) });
                }
                c.army.Add(s);
                names.Add(CitizenFavorites.PackSoldier(a));
                if (a.HasLoot && !a.IsOnExpedition) carriedHome.Add(a.CarriedLoot);
            }
        }
        if (c.extra == null) c.extra = new List<string>();
        new SaveBag(c.extra).SetList("army.gear", gear);
        new SaveBag(c.extra).SetList("army.names", names);
        carriedHome.items.Clear();   // трофеи в руках сохраняет сундук трофеев (обновление 23) - тут только ресурсы
        carriedHome.copies.Clear();
        var lootList = new List<string[]>();
        if (!carriedHome.IsEmpty) lootList.Add(LootSave.Pack(carriedHome));
        new SaveBag(c.extra).SetList("army.loot", lootList);
    }

    /// <summary>Сколько бойцов сейчас в казармах (для быстрого сохранения при изменении армии).</summary>
    public static int Count()
    {
        int n = 0;
        foreach (var b in BuildingInstance.All)
        {
            if (b == null) continue;
            BarracksController br = b.GetComponent<BarracksController>();
            if (br == null) continue;
            foreach (var a in br.Soldiers) if (a != null && !a.IsDead && a.IsSoldier) n++;
        }
        return n;
    }

    /// <summary>Вернуть бойцов в их казармы. Вызывается через пару кадров после постройки города.</summary>
    public static System.Collections.IEnumerator Restore(List<SavedSoldier> army)
    {
        Restoring = true;
        yield return null;
        yield return null;
        Restoring = false;
        var pm = PopulationManager.Instance;
        if (pm == null || army == null) yield break;

        var restored = new List<CitizenAgent>();   // по порядку записи (null - не вернулся бойцом)
        foreach (var s in army)
        {
            restored.Add(null);
            if (s == null) continue;
            BuildingInstance home = null;
            foreach (var b in BuildingInstance.All)
                if (b != null && b.OriginCell.x == s.bx && b.OriginCell.y == s.by && b.GetComponent<BarracksController>() != null) { home = b; break; }

            Vector2 pos = home != null ? home.Center : (pm.Hall != null ? pm.Hall.Center : Vector2.zero);
            CitizenAgent a = pm.SpawnAgent(pos, s.merc);   // +1 житель
            UnitType u = BarracksController.FindType(s.unit);
            BarracksController br = home != null ? home.GetComponent<BarracksController>() : null;
            if (br == null && u != null) br = BarracksController.WithRoomFor(u.spaceOccupied);
            if (u == null || br == null) continue;   // казармы нет - остаётся простым жителем

            Weapon w = null;
            if (s.hasWeapon)
            {
                w = new Weapon((WeaponKind)s.wKind, (QualityTier)s.wQuality)
                {
                    upgrade = s.wUpgrade, poisonCharges = s.wPoison, slowCharges = s.wSlow,
                    forged = s.wForged, forgeDamage = s.wForgeDamage, forgeDurability = s.wForgeDurability,
                    forgeSpeed = s.wForgeSpeed, forgeStars = s.wStars, saluteCharges = s.wSalute, saluteMk = s.wSaluteMk,
                };
                if (s.wMaxDur > 0f) w.maxDurability = s.wMaxDur;
                w.durability = Mathf.Clamp(s.wDur, 0f, w.maxDurability);
            }
            Shield sh = null;
            if (s.hasShield)
            {
                sh = new Shield((QualityTier)s.sQuality) { upgrade = s.sUpgrade, chaosMk = s.sChaosMk, chaosCharges = s.sChaosCharges };
                if (s.sMaxDur > 0f) sh.maxDurability = s.sMaxDur;
                sh.durability = Mathf.Clamp(s.sDur, 0f, sh.maxDurability);
            }
            br.AdoptSoldier(a, u, s.hearty, w, sh);
            restored[restored.Count - 1] = a;
            if (a.IsRamCarrier) a.RestoreRam(s.ramBroken, s.ramWear);
            if (s.hasBody)
            {
                var g = new BodyGear((BodyKind)s.bKind, (QualityTier)Mathf.Clamp(s.bQuality, 0, 2));
                if (g.IsArmor) g.upgrade = Mathf.Clamp(s.bUpgrade, 0, BodyGear.MaxUpgrade);
                if (s.bMaxDur > 0f) g.maxDurability = s.bMaxDur;
                g.durability = Mathf.Clamp(s.bDur, 0f, g.maxDurability);
                if (!a.EquipBody(g)) ArmoryController.StashBody(g);
            }
            if (s.cCharges > 0) a.LoadChaosCore(new ModuleKit { kind = ModuleKind.ChaosCore, mk = Mathf.Clamp(s.cMk, 1, 3), charges = s.cCharges });
        }

        for (int i = 0; i < restored.Count && i < PendingNames.Count; i++)
            if (restored[i] != null) CitizenFavorites.RestoreSoldier(restored[i], PendingNames[i]);
        PendingNames.Clear();

        // Полевые пушки: подъёмник и пачка снарядов - снова на пушке (раньше пропадали после входа)
        foreach (var g in PendingGear)
        {
            if (g == null || g.Length < 10) continue;
            int ord = SaveBag.At(g, 3), seen = 0;
            CitizenAgent who = null;
            for (int i = 0; i < army.Count && i < restored.Count; i++)
            {
                var s = army[i];
                if (s == null || GearName(s.unit) != g[0] || s.bx != SaveBag.At(g, 1) || s.by != SaveBag.At(g, 2)) continue;
                if (seen++ == ord) { who = restored[i]; break; }
            }
            if (who == null || who.IsDead) continue;
            try
            {
                who.RestoreCannonGear(SaveBag.At(g, 4), (ModuleKind)SaveBag.At(g, 5), SaveBag.At(g, 6, 1), SaveBag.At(g, 7),
                                      (ModuleKind)SaveBag.At(g, 8), SaveBag.AtB(g, 9));
            }
            catch (System.Exception e) { Debug.LogWarning("[Загрузка] полевая пушка: " + e.Message); }
        }
        PendingGear.Clear();

        // Обновление 14: сначала отряды (кто в каком отряде), потом набег. Раньше набег продолжался раньше, бойцы похода
        // были ещё в общем отряде казармы, и после перезахода внизу пропадали карточки отрядов (оставалось старое меню без отрядов)
        try { SquadSave.Resume(restored); }   // отряды - снова в свои лагеря
        catch (System.Exception e) { Debug.LogWarning("[Загрузка] отряды: " + e.Message); }
        // Кто был в набеге, когда игрок вышел - снова уходит в поход (RaidManager_Save)
        RaidManager.ResumeSaved(restored);
        try { CamoScouts.Resume(restored); }   // дозорные - снова на свои места (final-4)
        catch (System.Exception e) { Debug.LogWarning("[Загрузка] дозоры: " + e.Message); }
    }
}
