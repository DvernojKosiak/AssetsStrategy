using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Награда за победу в набеге - сверх добычи в мешках:
///  - Цитадель разрушена: премия по силе обороны деревни (башни, пушки, стены, заслоны, войско дома).
///    Чем крепче деревня, тем больше. Пример: хутор с 2 башнями - ~500 золота, крепость - несколько тысяч и сталь.
///  - Каждая разрушенная башня / пушка - небольшая премия (по её прочности).
/// Премия идёт сразу в казну (Цитадель + Хранилища), а не в мешки: даже если все бойцы погибнут, она остаётся.
/// У хозяина деревни её не отнимают - это не его ресурсы, а награда за победу.
/// Что не влезло в лимит казны (Цитадель + Хранилища), пропадает, как и любая добыча.
/// Все числа ниже можно спокойно менять.
/// </summary>
public partial class RaidManager
{
    // ---------- Числа (можно менять) ----------
    const int HallBountyGoldPerLevel = 150;    // за Цитадель: золото на каждый уровень деревни
    const int HallBountyRawPerLevel = 80;      // ... дерево и камень на каждый уровень
    const int HallBountyGoldPerPoint = 60;     // ... плюс за каждое очко обороны
    const int HallBountyRawPerPoint = 30;
    const float HallBountyIronPerPoint = 4f;
    const float HallBountyPointsPerSteel = 8f; // штука стали за столько очков обороны
    const float BountyMaxPoints = 120f;        // выше - не растёт (чтобы не было бесконечных наград)
    const int TowerBountyGoldPerPoint = 40;    // за разрушенную башню / пушку
    const int TowerBountyStonePerPoint = 25;
    const float ArmyPointsShare = 0.5f;        // войско дома: столько очков за единицу силы (BotVillage.UnitPower)

    float defensePoints;                          // сила обороны деревни на момент прибытия
    readonly Loot bountyPaid = new Loot();        // что уже зачислено за этот набег (для отчёта)
    readonly Loot bountyPending = new Loot();     // не смогли зачислить сразу (военный город) - зачислим дома

    // ---------- Сила обороны ----------

    /// <summary>Очки одного оборонного здания: башня - 1, пушки - 1.5, крепче (выше уровень) - больше.</summary>
    static float DefenseWeight(RaidBuildingKind kind, float hp, int villageLevel)
    {
        float baseHp, w;
        switch (kind)
        {
            case RaidBuildingKind.Tower: baseHp = 600f; w = 1f; break;
            case RaidBuildingKind.Cannon: baseHp = 550f; w = 1.5f; break;
            case RaidBuildingKind.Gun: baseHp = 650f; w = 1.5f; break;
            default: return 0f;
        }
        return w * Mathf.Max(0.5f, hp / baseHp) * BotVillage.PowerMul(villageLevel);
    }

    /// <summary>Сила обороны деревни в очках: башни и пушки, стены и ворота, заслоны, войско дома.</summary>
    public static float DefensePoints(BotVillage.Plan p, RaidTarget t)
    {
        if (p == null) return 0f;
        float s = 0f;
        foreach (var b in p.Buildings)
        {
            if (b == null || b.ruined) continue;   // руины не обороняют
            s += DefenseWeight(b.kind, b.hp, p.Level);
            if (b.kind == RaidBuildingKind.Barricade) s += 0.3f;
        }
        if (p.Walls) s += 1f;
        if (p.Gates) s += 0.5f;
        if (t != null) s += ArmyPointsShare * t.HomePower;
        return Mathf.Clamp(s, 0f, BountyMaxPoints);
    }

    /// <summary>Премия за Цитадель деревни такой силы.</summary>
    public static Loot HallBountyFor(int level, float points)
    {
        int L = Mathf.Clamp(level, 1, 3);
        float s = Mathf.Clamp(points, 0f, BountyMaxPoints);
        var l = new Loot();
        l.gold = HallBountyGoldPerLevel * L + Mathf.RoundToInt(HallBountyGoldPerPoint * s);
        l.wood = HallBountyRawPerLevel * L + Mathf.RoundToInt(HallBountyRawPerPoint * s);
        l.stone = l.wood;
        l.iron = Mathf.RoundToInt(HallBountyIronPerPoint * s);
        l.steel[(int)QualityTier.Medium] = Mathf.FloorToInt(s / HallBountyPointsPerSteel);
        return l;
    }

    /// <summary>Сколько обещано за Цитадель этой деревни (для меню набега).</summary>
    public static Loot HallBountyEstimate(RaidTarget t)
    {
        if (t == null) return new Loot();
        BotVillage.Plan p = t.GetPlan();
        return HallBountyFor(p != null ? p.Level : t.Level, DefensePoints(p, t));
    }

    void RememberDefense(BotVillage.Plan plan)
    {
        defensePoints = DefensePoints(plan, current);
    }

    // ---------- Выдача ----------

    void PayHallBounty()
    {
        if (current == null) return;
        if (defensePoints <= 0f) defensePoints = DefensePoints(arrivedPlan ?? current.GetPlan(), current);   // старое сохранение
        int level = arrivedPlan != null ? arrivedPlan.Level : current.Level;
        Loot l = HallBountyFor(level, defensePoints);
        PayBounty(l);
        WarManager.Notify($"Награда за Цитадель {current.Name} — сразу в казну: {l.ShortText}", true);
    }

    void PayTowerBounty(RaidBuilding b)
    {
        int level = arrivedPlan != null ? arrivedPlan.Level : (current != null ? current.Level : 1);
        float w = DefenseWeight(b.Kind, b.MaxHp, level);
        if (w <= 0f) return;
        var l = new Loot
        {
            gold = Mathf.RoundToInt(TowerBountyGoldPerPoint * w),
            stone = Mathf.RoundToInt(TowerBountyStonePerPoint * w),
        };
        PayBounty(l);
        WarManager.Notify($"{b.Title} разрушена — награда в казну: {l.ShortText}", false);
    }

    void PayBounty(Loot l)
    {
        if (l == null || l.IsEmpty) return;
        bountyPaid.Add(l);
        bountyPending.Add(l);
        FlushBounty();
    }

    // Зачислить, что ждёт (в военном городе казна чужая - ждём возвращения домой)
    void FlushBounty()
    {
        if (bountyPending.IsEmpty || WarCityMode.Active) return;
        if (BuildingPlacer.Instance == null || !BuildingPlacer.Instance.WorldReady) return;
        bountyPending.DepositToTown();
    }

    // ---------- Сохранение (RaidManager_Save) ----------

    void SaveBounty(SaveBag bag)
    {
        bag.Set(Pre + "defense", defensePoints);
        bag.Set(Pre + "bounty", bountyPaid.IsEmpty ? null : JsonUtility.ToJson(RaidLoot.From(bountyPaid)));
    }

    void LoadBounty(SaveBag bag)
    {
        defensePoints = bag.Float(Pre + "defense");
        bountyPaid.Clear();
        string json = bag.Get(Pre + "bounty");
        if (string.IsNullOrEmpty(json)) return;
        try { var r = JsonUtility.FromJson<RaidLoot>(json); if (r != null) bountyPaid.Add(r.ToLoot()); }
        catch (System.Exception) { }
    }

    // ---------- Склады ----------

    /// <summary>
    /// Сколько из этой добычи не влезет в казну (лимит Цитадели + Хранилищ). Материалы (доски, плиты, сталь)
    /// сначала идут в Цитадель, лишнее - в Хранилища. Пусто - всё влезет.
    /// </summary>
    static string OverflowText(Loot l)
    {
        if (l == null || l.IsEmpty || !StorageLimits.Active) return "";
        var parts = new List<string>();
        void Raw(int have, StoreRes r)
        {
            int over = have - StorageLimits.Free(r);
            if (have > 0 && over > 0) parts.Add($"{over} {StorageLimits.Unit(r)}");
        }
        Raw(l.gold, StoreRes.Gold); Raw(l.wood, StoreRes.Wood); Raw(l.stone, StoreRes.Stone); Raw(l.food, StoreRes.Food);
        Raw(l.iron, StoreRes.Iron); Raw(l.oats, StoreRes.Oats); Raw(l.berries, StoreRes.Berries); Raw(l.herbs, StoreRes.Herbs);
        Raw(l.potions, StoreRes.Potions); Raw(l.resin, StoreRes.Resin);

        int refined = l.RefinedTotal;
        if (refined > 0)
        {
            // Хранилища общие для всех материалов: считаем вместе
            int hallFree = 0;
            foreach (RefinedKind k in new[] { RefinedKind.Planks, RefinedKind.StonePlates, RefinedKind.Steel, RefinedKind.ReinforcedPlates })
            {
                int want = 0;
                for (int q = 0; q < 3; q++)
                    want += k == RefinedKind.Planks ? l.planks[q] : k == RefinedKind.StonePlates ? l.plates[q] : k == RefinedKind.Steel ? l.steel[q] : l.reinforced[q];
                hallFree += Mathf.Min(want, StorageLimits.HallRefinedFree(k));
            }
            int over = refined - hallFree - WarehouseController.TotalFree;
            if (over > 0) parts.Add($"{over} материалов");
        }
        return string.Join(", ", parts);
    }
}
