using UnityEngine;
using System.Collections.Generic;
using System.Text;

[System.Serializable]
public struct Refined
{
    public RefinedKind kind;
    public int amount;
}

public class UpgradeStep
{
    public QualityTier minTier;   // материалы не хуже этого качества
    public Refined[] items;
}

/// <summary>
/// Правила улучшений и постройки "копий" зданий.
///  1. Улучшение до Ур.2 и Ур.3: обычные ресурсы + доски/плиты из мастерской.
///     На Ур.2 подходит любое качество, на Ур.3 - не хуже "Хорошего".
///  2. Второе и следующие здания одного типа (например, вторая казарма) требуют доски/плиты.
/// Материалы списываются автоматически: сначала самого низкого подходящего качества.
/// Все числа - в таблицах ниже, их можно спокойно менять.
/// </summary>
public static class UpgradeRules
{
    public const int MaxLevel = 3;
    public const int FreeCopies = 1;   // сколько зданий одного типа можно построить без материалов

    // Казарма растёт до Ур.4 (конница), остальные здания - до Ур.3
    public const int BarracksMaxLevel = 4;
    public static int MaxLevelFor(BuildingInstance b)
    {
        if (b == null || b.type == null) return MaxLevel;
        if (b.type.name == BarracksController.TypeName) return BarracksMaxLevel;
        if (b.type.name == OutpostController.TypeName) return OutpostController.MaxLevel;   // Ур.4 - отстреливается
        if (b.type.name == BuildingInstance.BridgeName) return 1;
        if (b.type.name == BuildingInstance.SawmillName) return BuildingInstance.LeavesFromLevel;   // Ур.4 - листва   // мост не улучшается (раздвижной - модуль Инженерного двора)
        if (b.type.name == BuildingInstance.FarmName) return BuildingInstance.FarmMaxLevel;        // Ур.4 - большое хозяйство: больше еды и овса
        return MaxLevel;
    }

    static Refined P(int a) { return new Refined { kind = RefinedKind.Planks, amount = a }; }
    static Refined S(int a) { return new Refined { kind = RefinedKind.StonePlates, amount = a }; }
    static Refined St(int a) { return new Refined { kind = RefinedKind.Steel, amount = a }; }
    static Refined RP(int a) { return new Refined { kind = RefinedKind.ReinforcedPlates, amount = a }; }
    static Refined TB(int a) { return new Refined { kind = RefinedKind.TarredBeams, amount = a }; }
    static Refined M(int a) { return new Refined { kind = RefinedKind.Monolith, amount = a }; }
    static UpgradeStep Step(QualityTier min, params Refined[] items) { return new UpgradeStep { minTier = min, items = items }; }

    // Материалы для улучшения. Первый элемент - до Ур.2, второй - до Ур.3.
    static readonly Dictionary<string, UpgradeStep[]> Upgrades = new Dictionary<string, UpgradeStep[]>
    {
        // Цитадель: растёт зелёная зона застройки (см. Territory)
        { "Ратуша",     new[] { Step(QualityTier.Medium, P(10), S(10)), Step(QualityTier.Good, P(16), S(16), St(8)) } },
        // Аванпост: растёт радиус зоны
        // Ур.3 - укреплённый аванпост: укреплённые плиты, зато смотрители носят еду с ферм рядом
        // Ур.4 - сторожевой аванпост: смотрители отстреливаются (слабее башни)
        { OutpostController.TypeName, new[] { Step(QualityTier.Medium, P(8), S(8)), Step(QualityTier.Good, P(14), S(10), St(5), RP(6)),
                        Step(QualityTier.Good, P(16), S(12), St(8), RP(8)) } },
        { BuildingInstance.WallName, new[] { Step(QualityTier.Medium, S(2)), Step(QualityTier.Good, S(4)) } },
        // Ворота (финал): улучшение дороже стены - больше плит, ещё доски и сталь (раньше ворота улучшались вовсе без материалов)
        { BuildingInstance.GateName, new[] { Step(QualityTier.Medium, S(6), P(4)), Step(QualityTier.Good, S(10), P(6), St(3)) } },
        // Ур.4 лесопилки - листва (накидки Мастера маскировки и др.)
        { "Лесопилка",  new[] { Step(QualityTier.Medium, P(6)),        Step(QualityTier.Good, P(12)), Step(QualityTier.Good, P(16), S(6), St(2)) } },
        { "Шахта",      new[] { Step(QualityTier.Medium, S(6)),        Step(QualityTier.Good, S(12)) } },
        // Ур.4 фермы - большое хозяйство: амбар и силос (больше еды, склада и овса)
        { "Ферма",      new[] { Step(QualityTier.Medium, P(8)),        Step(QualityTier.Good, P(14)), Step(QualityTier.Good, P(18), S(8), St(3)) } },
        { "Жилой дом",  new[] { Step(QualityTier.Medium, P(8)),        Step(QualityTier.Good, P(14), S(8)) } },
        { "Мастерская", new[] { Step(QualityTier.Medium, P(6), S(6)),  Step(QualityTier.Good, P(10), S(10)) } },
        // Ур.3 казармы - сталь: больше мест и полевой маг-целитель (см. BarracksController)
        { BarracksController.TypeName, new[] { Step(QualityTier.Medium, P(8), S(8)),  Step(QualityTier.Good, P(14), S(14), St(8)),
                        // Ур.4 - конница (нужны лошади из Конюшни, см. BarracksController)
                        Step(QualityTier.Good, P(20), S(16), St(16), RP(8)) } },
        { "Башня",      new[] { Step(QualityTier.Medium, S(8)),        Step(QualityTier.Good, S(12), RP(4)) } },
        { HealerHouse.TypeName, new[] { Step(QualityTier.Medium, P(10), S(8)), Step(QualityTier.Good, P(14), S(12)) } },
        // Ур.3 пушки - сталь
        { MagicCannonController.TypeName, new[] { Step(QualityTier.Medium, S(10), P(8)), Step(QualityTier.Good, S(16), P(12), St(6)) } },
        // Пушка: ствол из стали - и улучшения со сталью
        { GroundCannonController.TypeName, new[] { Step(QualityTier.Medium, S(10), P(6), St(6)), Step(QualityTier.Good, S(14), P(8), St(12), RP(4)) } },
        // Магическая оружейная (Магия): пачки следующей марки, больше полок, быстрее зачаровка
        { MagicArmory.TypeName, new[] { Step(QualityTier.Medium, P(8), S(8), St(2)), Step(QualityTier.Good, P(12), S(12), St(6)) } },
        // Алтарь жизни (Магия жизни): голем крепче и сильнее (+12% за уровень)
        { LifeAltar.TypeName, new[] { Step(QualityTier.Medium, S(10), P(6)), Step(QualityTier.Good, S(16), P(10), St(4)) } },
        // Курьерская: и улучшения требуют сталь
        { CourierHouse.TypeName, new[] { Step(QualityTier.Medium, P(10), S(8), St(4)), Step(QualityTier.Good, P(16), S(12), St(8)) } },
        // Хранилище: больше места
        { WarehouseController.TypeName, new[] { Step(QualityTier.Medium, P(8), S(4)), Step(QualityTier.Good, P(14), S(10)) } },
        // Оружейная: лучше качество оружия, больше склад, доступны улучшения оружия за золото
        { ArmoryController.TypeName, new[] { Step(QualityTier.Medium, P(10), S(10), St(6)), Step(QualityTier.Good, P(16), S(16), St(12)) } },
        // Конюшня: больше лошадей и быстрее растут
        { StableController.TypeName, new[] { Step(QualityTier.Medium, P(8), S(4)), Step(QualityTier.Good, P(14), S(10)) } },
        // Станция собирателей: больше собирателей, больше ягод за поход, дальше ходят
        { BerryStation.TypeName, new[] { Step(QualityTier.Medium, P(6)), Step(QualityTier.Good, P(10), S(4)) } },
        // Кухня: больше поваров, быстрее варка, больше похлёбки хранится
        { KitchenController.TypeName, new[] { Step(QualityTier.Medium, P(8), S(6)), Step(QualityTier.Good, P(14), S(10), St(4)) } },
        // Обозный двор: больше рабочих и быстрее обоз (60 / 45 / 30 с за кадром)
        { WagonYard.TypeName, new[] { Step(QualityTier.Medium, P(10), S(8)), Step(QualityTier.Good, P(16), S(12), St(4)) } },
        // Зельеварня (Магия): больше полок под зелья и быстрее варка
        { PotionHouse.TypeName, new[] { Step(QualityTier.Medium, P(6), S(6)), Step(QualityTier.Good, P(10), S(8), St(2)) } },
        // Кузня мастера (Война): доводка даёт больше
        { WeaponForge.TypeName, new[] { Step(QualityTier.Medium, P(6), S(4), St(4)), Step(QualityTier.Good, P(10), S(8), St(8), RP(2)) } },
        // Литейная (Инженерия): котлы прочнее - греются медленнее, плавят быстрее, реже горят; Ур.2 - «Большая плавка»
        { FoundryController.TypeName, new[] { Step(QualityTier.Medium, P(6), S(10), St(4)), Step(QualityTier.Good, P(10), S(14), St(8), RP(4)) } },
        // Инженерный двор (Инженерия): модули следующей марки (Мк.II / Мк.III), больше склад, быстрее сборка
        { EngineerYard.TypeName, new[] { Step(QualityTier.Medium, P(12), S(10), St(6)), Step(QualityTier.Good, P(18), S(16), St(10), RP(4)) } },
        // Торговый дом: больше лавок и лотов, ниже комиссия, больше заказов гильдии, купцы быстрее
        { MarketHouse.TypeName, new[] { Step(QualityTier.Medium, P(12), S(10)), Step(QualityTier.Good, P(18), S(14), St(6)) } },
        // Гостевой дом: больше мест для наёмников (6 / 12 / 18)
        { BuildingInstance.GuestHouseName, new[] { Step(QualityTier.Medium, P(8), S(4)), Step(QualityTier.Good, P(12), S(8)) } },
        // Инженерная казарма (Военный инженер): больше мест под катапульты (8 / 16 / 24); ещё и детали (UpgradeParts)
        { EngineerBarracks.TypeName, new[] { Step(QualityTier.Medium, P(10), S(6), St(4)), Step(QualityTier.Good, P(16), S(10), St(8), RP(4)) } },
        // Завод (Индустриализатор): плиты, сталь; ещё и детали (UpgradeParts)
        { IndustrialPlant.TypeName, new[] { Step(QualityTier.Medium, S(10), St(6), P(4)), Step(QualityTier.Good, S(16), St(10), RP(4)) } },
        // Двор военного строителя (Военный строитель): больше строителей, чинят быстрее, шире зона, больше отметок
        { WarBuilderYard.TypeName, new[] { Step(QualityTier.Medium, P(12), S(6), St(2)), Step(QualityTier.Good, P(18), S(10), St(6), TB(4)) } },
    };

    // Детали из Литейной (ResourceManager.parts) для улучшения: первый элемент - до Ур.2, второй - до Ур.3
    static readonly Dictionary<string, int[]> UpgradeParts = new Dictionary<string, int[]>
    {
        { EngineerBarracks.TypeName, new[] { 6, 12 } },
        { IndustrialPlant.TypeName, new[] { 8, 14 } },
    };

    /// <summary>Сколько деталей нужно для улучшения до targetLevel (0 - не нужны).</summary>
    public static int PartsFor(string typeName, int targetLevel)
    {
        if (typeName == null || !UpgradeParts.TryGetValue(typeName, out var arr)) return 0;
        int i = targetLevel - 2;
        return i >= 0 && i < arr.Length ? arr[i] : 0;
    }

    // Здания, которым доски/плиты нужны уже для ПЕРВОЙ постройки.
    // 2-е здание стоит вдвое больше материалов, 3-е - втрое и т.д.
    static readonly Dictionary<string, UpgradeStep> BaseBuildRules = new Dictionary<string, UpgradeStep>
    {
        { HealerHouse.TypeName, Step(QualityTier.Medium, P(8), S(6)) },
        { MagicCannonController.TypeName, Step(QualityTier.Medium, S(10), P(6)) },
        { GroundCannonController.TypeName, Step(QualityTier.Medium, S(8), P(4), St(6)) },
        { MagicArmory.TypeName, Step(QualityTier.Medium, P(6), S(6)) },
        { LifeAltar.TypeName, Step(QualityTier.Medium, S(8), P(4)) },
        // Курьерская: уже первая требует доски, плиты и сталь (2-я - вдвое больше и т.д.)
        { CourierHouse.TypeName, Step(QualityTier.Medium, P(10), S(8), St(5)) },
        // Оружейная: уже первая требует доски и плиты (2-я - вдвое больше и т.д.)
        { ArmoryController.TypeName, Step(QualityTier.Medium, P(8), S(8)) },
        // Аванпост: дорогое здание - уже первый требует доски и плиты
        { OutpostController.TypeName, Step(QualityTier.Medium, P(10), S(10)) },
        // Кухня: печь и котлы - уже первая требует доски и плиты
        { KitchenController.TypeName, Step(QualityTier.Medium, P(6), S(4)) },
        { PotionHouse.TypeName, Step(QualityTier.Medium, P(6)) },
        { WeaponForge.TypeName, Step(QualityTier.Medium, P(4), St(4)) },
        { FoundryController.TypeName, Step(QualityTier.Medium, P(4), S(8)) },
        { EngineerYard.TypeName, Step(QualityTier.Medium, P(10), S(8), St(4)) },
        // Торговый дом: уже первый требует доски, плиты, смоляной брус и монолит (строится только один)
        { MarketHouse.TypeName, Step(QualityTier.Medium, P(10), S(8), TB(4), M(2)) },
        // Обозный двор: тяжёлые склады и навесы для фургонов - смоляной брус и монолитное основание уже для первого
        { WagonYard.TypeName, Step(QualityTier.Medium, P(10), S(8), TB(6), M(2)) },
        // Инженерная казарма: мастерская для сборки катапульт - доски и сталь уже для первой
        { EngineerBarracks.TypeName, Step(QualityTier.Medium, P(8), St(4)) },
        // Завод: тяжёлое основание и сталь уже для первого
        { IndustrialPlant.TypeName, Step(QualityTier.Medium, S(10), St(6)) },
        // Двор военного строителя: леса, козлы и инструмент - доски уже для первого
        { WarBuilderYard.TypeName, Step(QualityTier.Medium, P(10), S(4)) },
    };

    // Материалы, которые нужны КАЖДОЙ постройке одинаково (не растут с числом зданий): брусчатка - плитка на клетку
    static readonly Dictionary<string, UpgradeStep> FlatBuildRules = new Dictionary<string, UpgradeStep>
    {
        { BuildingInstance.CobbleName, Step(QualityTier.Medium, S(1)) },
        // Мост: доска на каждую клетку настила
        { BuildingInstance.BridgeName, Step(QualityTier.Medium, P(1)) },
    };

    // Материалы для постройки 2-го, 3-го... здания этого типа.
    // Количество умножается на (номер здания - 1): 2-я казарма = 1x, 3-я = 2x и т.д.
    static readonly Dictionary<string, UpgradeStep> CopyRules = new Dictionary<string, UpgradeStep>
    {
        { BarracksController.TypeName, Step(QualityTier.Medium, P(10), S(5)) },
        { "Башня",      Step(QualityTier.Medium, S(8)) },
        { "Мастерская", Step(QualityTier.Medium, P(6), S(6)) },
        { WarehouseController.TypeName, Step(QualityTier.Medium, P(8), S(4)) },
        { StableController.TypeName, Step(QualityTier.Medium, P(8), S(4)) },
        { BerryStation.TypeName, Step(QualityTier.Medium, P(4)) },
    };

    // ---------- Общие мелочи ----------

    public static string KindLabel(RefinedKind k)
    {
        switch (k)
        {
            case RefinedKind.Planks: return "Доски";
            case RefinedKind.StonePlates: return "Плиты";
            case RefinedKind.ReinforcedPlates: return "Укреплённые плиты";
            case RefinedKind.TarredBeams: return "Смоляной брус";
            case RefinedKind.Monolith: return "Монолитное основание";
            default: return "Сталь";
        }
    }

    static string QualityNote(QualityTier min)
    {
        return min == QualityTier.Medium ? "любое качество" : TownHallStorage.TierName(min).ToLower() + " и лучше";
    }

    public static UpgradeStep StepFor(string typeName, int targetLevel)
    {
        if (typeName != null && Upgrades.TryGetValue(typeName, out var arr))
        {
            int i = targetLevel - 2;
            if (i >= 0 && i < arr.Length) return arr[i];
        }
        return null;
    }

    public static void BaseCost(BuildingInstance b, out int g, out int w, out int s, out int f)
    {
        // Цена растёт с уровнем быстрее, чем раньше (×1.5 / ×3 / ×5 от цены постройки; Цитадель - своей таблицей), см. Balance
        Balance.UpgradeCost(b.type, b.level, out g, out w, out s, out f);
    }

    static string FormatBase(int g, int w, int s, int f)
    {
        List<string> parts = new List<string>();
        if (g > 0) parts.Add(g + "г");
        if (w > 0) parts.Add(w + "д");
        if (s > 0) parts.Add(s + "к");
        if (f > 0) parts.Add(f + "е");
        return parts.Count > 0 ? string.Join(" ", parts) : "бесплатно";
    }

    // Сколько материала не хуже заданного качества: на складе Цитадели и во всех Хранилищах
    public static int CountAtLeast(TownHallStorage st, RefinedKind kind, QualityTier min)
    {
        int total = 0;
        foreach (QualityTier t in System.Enum.GetValues(typeof(QualityTier)))
            if (t >= min) total += st.Get(kind, t) + WarehouseController.TotalStored(kind, t);
        return total;
    }

    // source - Хранилище, откуда взяли (null - из Цитадели): оттуда житель и понесёт материалы
    public struct Spent { public RefinedKind kind; public QualityTier tier; public int amount; public BuildingInstance source; }

    // Списывает материал: сначала самого низкого подходящего качества, чтобы беречь лучшие.
    // В каждом качестве сначала берётся склад Цитадели, потом Хранилища.
    // log (если задан) запоминает, что именно и какого качества списали - чтобы вернуть при сорванном рейсе.
    public static void SpendAtLeast(TownHallStorage st, RefinedKind kind, QualityTier min, int amount, List<Spent> log = null)
    {
        int left = amount;
        foreach (QualityTier t in System.Enum.GetValues(typeof(QualityTier)))
        {
            if (t < min || left <= 0) continue;

            int take = Mathf.Min(left, st.Get(kind, t));
            if (take > 0 && st.TrySpend(kind, t, take))
            {
                left -= take;
                if (log != null) log.Add(new Spent { kind = kind, tier = t, amount = take });
            }

            if (left > 0)
            {
                int fromWh = WarehouseController.Take(kind, t, left, out BuildingInstance src);
                if (fromWh > 0)
                {
                    left -= fromWh;
                    if (log != null) log.Add(new Spent { kind = kind, tier = t, amount = fromWh, source = src });
                }
            }
        }
    }

    public static void RefundSpent(List<Spent> log)
    {
        TownHallStorage st = TownHallStorage.Instance;
        if (st == null || log == null) return;
        foreach (var sp in log)
        {
            st.Add(sp.kind, sp.tier, sp.amount);
        }
    }

    // ---------- Улучшение ----------

    /// <summary>
    /// Какая Цитадель нужна, чтобы улучшить здание до targetLevel: Ур.2 - сразу, Ур.3 - с Цитадели Ур.2,
    /// Ур.4 (конница казармы, листва лесопилки, большое хозяйство фермы) - с Цитадели Ур.3. Саму Цитадель улучшают без условий.
    /// </summary>
    public static int HallLevelForUpgrade(BuildingInstance b, int targetLevel)
    {
        if (b == null || b.IsTownHall) return 1;
        return Mathf.Clamp(targetLevel - 1, 1, MaxLevel);
    }

    static int HallLevelNow()
    {
        if (BuildingPlacer.Instance != null) return BuildingPlacer.Instance.HallLevelNow();
        BuildingInstance hall = PopulationManager.Instance != null ? PopulationManager.Instance.Hall : null;
        return hall != null ? Mathf.Max(1, hall.level) : 1;
    }

    public static bool HallAllowsUpgrade(BuildingInstance b)
    {
        return b != null && HallLevelNow() >= HallLevelForUpgrade(b, b.level + 1);
    }

    /// <summary>На деревню идёт набег (враги на карте, соседи, живой или чужой набег игрока) - улучшать нельзя.</summary>
    public static bool RaidBlocksUpgrade => WarManager.IsWar || HomeSiege.UnderRaid;

    // ---------- Что нужно построить для следующего уровня Цитадели (final-8) ----------
    // Раньше Цитадель улучшали одними ресурсами, и игроки уходили на Ур.3 с пустым городом (без казарм и башен),
    // а соседи и игроки потом приходили к ним по уровню Цитадели. Теперь город должен дорасти.

    public struct HallReq { public string label; public int have, need; }

    static int CountBuilt(System.Func<BuildingInstance, bool> f)
    {
        int n = 0;
        foreach (var o in BuildingInstance.All)
            if (o != null && o.type != null && !o.UnderConstruction && !o.IsRealRuin && f(o)) n++;
        return n;
    }

    public static List<HallReq> HallReqs(int target)
    {
        var list = new List<HallReq>();
        void Add(string label, int need, System.Func<BuildingInstance, bool> f) { list.Add(new HallReq { label = label, need = need, have = CountBuilt(f) }); }
        if (target == 2)
        {
            Add("Казарма", 1, o => o.type.name == BarracksController.TypeName);
            Add("Башни", 2, o => o.type.name == "Башня");
            Add("Курьерская", 1, o => o.type.name == CourierHouse.TypeName);
        }
        else if (target >= 3)
        {
            Add("Казарма Ур.2", 1, o => o.type.name == BarracksController.TypeName && o.level >= 2);
            Add("Башни", 4, o => o.type.name == "Башня");
            Add("Хранилище", 1, o => o.type.name == WarehouseController.TypeName);
            Add("Стена или ворота", 1, o => o.IsWall || o.IsGate);
        }
        return list;
    }

    /// <summary>"" - всё построено; иначе - чего не хватает для следующего уровня Цитадели.</summary>
    public static string HallReqProblem(BuildingInstance b)
    {
        if (b == null || !b.IsTownHall) return "";
        var lack = new List<string>();
        foreach (var r in HallReqs(b.level + 1)) if (r.have < r.need) lack.Add($"{r.label} {r.have}/{r.need}");
        return lack.Count == 0 ? "" : $"Для Цитадели Ур. {b.level + 1} постройте: " + string.Join(", ", lack);
    }

    public static bool CanUpgrade(BuildingInstance b)
    {
        if (b == null || b.type == null || b.level >= MaxLevelFor(b)) return false;
        if (!HallAllowsUpgrade(b)) return false;  // нужна Цитадель выше
        if (HallReqProblem(b) != "") return false;   // город не дорос (final-8)
        if (b.upgradeInProgress) return false; // материалы уже несут
        if (b.IsRuined) return false;          // руины не улучшают
        if (WarCityMode.Active) return false;  // в военном городе не улучшают
        if (RaidBlocksUpgrade) return false;   // во время набега не улучшают

        ResourceManager rm = ResourceManager.Instance;
        if (rm == null) return false;

        BaseCost(b, out int g, out int w, out int s, out int f);
        if (!rm.HasEnough(g, w, s, f)) return false;
        if (rm.parts < PartsFor(b.type.name, b.level + 1)) return false;

        UpgradeStep step = StepFor(b.type.name, b.level + 1);
        if (step == null) return true; // для этого здания материалы не нужны

        TownHallStorage st = TownHallStorage.Instance;
        if (st == null) return false;
        foreach (var it in step.items)
            if (CountAtLeast(st, it.kind, step.minTier) < it.amount) return false;

        return true;
    }

    /// <summary>
    /// Заказывает улучшение: деньги и материалы списываются сразу, а уровень вырастет, когда житель
    /// (рабочий этого здания, иначе курьер Цитадели, иначе любой свободный) принесёт материалы из Цитадели.
    /// </summary>
    public static bool TryUpgrade(BuildingInstance b)
    {
        if (!CanUpgrade(b)) return false;

        BaseCost(b, out int g, out int w, out int s, out int f);
        int partsNeed = PartsFor(b.type.name, b.level + 1);
        if (ResourceManager.Instance.parts < partsNeed) return false;
        if (!ResourceManager.Instance.Spend(g, w, s, f)) return false;
        if (partsNeed > 0) ResourceManager.Instance.SpendParts(partsNeed);

        List<Spent> spent = new List<Spent>();
        UpgradeStep step = StepFor(b.type.name, b.level + 1);
        if (step != null)
            foreach (var it in step.items)
                SpendAtLeast(TownHallStorage.Instance, it.kind, step.minTier, it.amount, spent);

        int target = b.level + 1;
        b.upgradeInProgress = true;

        var pm = PopulationManager.Instance;
        BuildingInstance hall = pm != null ? pm.Hall : null;

        // Материалы брали в Хранилище - житель идёт за ними туда, а не в Цитадель
        BuildingInstance pickup = hall;
        foreach (var sp in spent)
            if (sp.source != null) { pickup = sp.source; break; }

        Logistics.Request(new HaulJob
        {
            from = pickup,
            to = b,
            prefer = b,
            onDeliver = () =>
            {
                if (b == null) { Refund(g, w, s, f, spent); RefundParts(partsNeed); return; }
                // Материалы на месте - теперь идёт время улучшения (Balance), уровень вырастет в конце
                b.StartUpgrade(target);
            },
            onAbort = loaded =>
            {
                Refund(g, w, s, f, spent);
                RefundParts(partsNeed);
                if (b != null) b.upgradeInProgress = false;
            }
        });
        return true;
    }

    static void RefundParts(int n)
    {
        if (n > 0 && ResourceManager.Instance != null) ResourceManager.Instance.AddParts(n);
    }

    static void Refund(int g, int w, int s, int f, List<Spent> spent)
    {
        if (ResourceManager.Instance != null) ResourceManager.Instance.Add(g, w, s, f);
        RefundSpent(spent);
    }

    // ---------- Постройка 2-го, 3-го... здания ----------

    static readonly Dictionary<string, int> CopyMultCap = new Dictionary<string, int>
    {
        { "Башня", 2 },                          // не больше 16 плит
        { BarracksController.TypeName, 2 },      // не больше 20 досок и 10 плит
    };

    public static Refined[] BuildExtra(string typeName, int existingCount, out QualityTier minTier)
    {
        minTier = QualityTier.Medium;
        if (typeName == null) return null;

        if (FlatBuildRules.TryGetValue(typeName, out var flat))
        {
            minTier = flat.minTier;
            return (Refined[])flat.items.Clone();
        }

        int copyNumber = existingCount + 1;
        int mult;
        if (BaseBuildRules.TryGetValue(typeName, out var rule)) mult = copyNumber;   // материалы нужны всегда
        else if (CopyRules.TryGetValue(typeName, out rule)) mult = copyNumber - FreeCopies;
        else return null;
        if (mult <= 0) return null;
        // Обновление 12: материалы башен и казарм растут не бесконечно (их число и так ограничено уровнем Цитадели).
        // Раньше 4-я башня просила 24 плиты, 5-я - 32: «башня как башня, а цена растёт»
        if (CopyMultCap.TryGetValue(typeName, out int capMul)) mult = Mathf.Min(mult, capMul);

        minTier = rule.minTier;
        Refined[] res = new Refined[rule.items.Length];
        for (int i = 0; i < res.Length; i++)
            res[i] = new Refined { kind = rule.items[i].kind, amount = rule.items[i].amount * mult };
        return res;
    }

    public static bool CanPayBuild(string typeName, int existingCount)
    {
        Refined[] items = BuildExtra(typeName, existingCount, out QualityTier min);
        if (items == null) return true;

        TownHallStorage st = TownHallStorage.Instance;
        if (st == null) return false;
        foreach (var it in items)
            if (CountAtLeast(st, it.kind, min) < it.amount) return false;
        return true;
    }

    public static void PayBuild(string typeName, int existingCount)
    {
        Refined[] items = BuildExtra(typeName, existingCount, out QualityTier min);
        if (items == null) return;

        TownHallStorage st = TownHallStorage.Instance;
        if (st == null) return;
        foreach (var it in items)
            SpendAtLeast(st, it.kind, min, it.amount);
    }

    /// <summary>
    /// Короткая строка для карточки в окне построек: "Здание №2: Плиты 3/8".
    /// Пусто, если доски/плиты для этого здания не нужны. ok = хватает ли их на складе.
    /// </summary>
    public static string ShortBuildNeeds(string typeName, int existingCount, out bool ok)
    {
        ok = true;
        Refined[] items = BuildExtra(typeName, existingCount, out QualityTier min);
        if (items == null) return "";

        TownHallStorage st = TownHallStorage.Instance;
        List<string> parts = new List<string>();
        foreach (var it in items)
        {
            int have = st != null ? CountAtLeast(st, it.kind, min) : 0;
            if (have < it.amount) ok = false;
            string col = have >= it.amount ? UiKit.GoodHex : UiKit.BadHex;
            parts.Add($"{KindLabel(it.kind)} <color={col}>{have}/{it.amount}</color>");
        }

        string quality = min == QualityTier.Medium ? "" : $" ({QualityNote(min)})";
        string prefix = FlatBuildRules.ContainsKey(typeName) ? "На клетку: " : $"Здание №{existingCount + 1}: ";
        return prefix + string.Join(", ", parts) + quality;
    }

    public static string DescribeBuild(string typeName, int existingCount)
    {
        Refined[] items = BuildExtra(typeName, existingCount, out QualityTier min);
        if (items == null) return "";

        TownHallStorage st = TownHallStorage.Instance;
        StringBuilder sb = new StringBuilder();
        sb.Append($"<b>Здание №{existingCount + 1}: нужны материалы</b>");
        foreach (var it in items)
        {
            int have = st != null ? CountAtLeast(st, it.kind, min) : 0;
            string col = have >= it.amount ? UiKit.GoodHex : UiKit.BadHex;
            sb.Append($"\n {KindLabel(it.kind)} ({QualityNote(min)}): <color={col}>{have}/{it.amount}</color>");
        }
        return sb.ToString();
    }

    // ---------- Панель улучшения (рисуется внутри панелей зданий) ----------

    // ---------- Укрепление: смоляной брус + монолитное основание ----------
    // Здание один раз укрепляют: +50% прочности навсегда (и после восстановления из руин).
    // Цена растёт с размером здания. Материалы несут из Цитадели, как при улучшении.

    public const float FortifyHpMul = 1.5f;

    public static bool CanBeFortified(BuildingInstance b)
    {
        return b != null && b.type != null && b.IsDamageable && !b.IsRoad;
    }

    public static void FortifyCost(BuildingInstance b, out int beams, out int monolith, out int gold)
    {
        int cells = b != null && b.type != null ? Mathf.Max(1, b.type.size.x * b.type.size.y) : 1;
        beams = Mathf.Max(1, cells / 2);
        monolith = cells >= 4 ? Mathf.Max(1, cells / 4) : 0;
        gold = 10 * cells;
    }

    static bool ShowFortify(BuildingInstance b)
    {
        if (!CanBeFortified(b)) return false;
        if (b.fortified || b.fortifyInProgress) return true;
        var st = TownHallStorage.Instance;
        return st != null && CountAtLeast(st, RefinedKind.TarredBeams, QualityTier.Medium) > 0;
    }

    public static string FortifyProblem(BuildingInstance b)
    {
        if (!CanBeFortified(b)) return "Это не укрепить";
        if (b.fortified) return "Уже укреплено";
        if (b.fortifyInProgress) return "Материалы несут...";
        if (b.IsRuined) return "Сначала восстановите";
        FortifyCost(b, out int beams, out int mono, out int gold);
        var rm = ResourceManager.Instance;
        var st = TownHallStorage.Instance;
        if (rm == null || st == null) return "Нет склада";
        if (rm.gold < gold) return $"Нужно {gold} золота";
        if (CountAtLeast(st, RefinedKind.TarredBeams, QualityTier.Medium) < beams) return $"Нужно смоляного бруса: {beams}";
        if (CountAtLeast(st, RefinedKind.Monolith, QualityTier.Medium) < mono) return $"Нужно монолита: {mono}";
        return "";
    }

    public static bool TryFortify(BuildingInstance b)
    {
        if (FortifyProblem(b) != "") return false;
        FortifyCost(b, out int beams, out int mono, out int gold);
        if (!ResourceManager.Instance.Spend(gold, 0, 0, 0)) return false;
        List<Spent> spent = new List<Spent>();
        SpendAtLeast(TownHallStorage.Instance, RefinedKind.TarredBeams, QualityTier.Medium, beams, spent);
        if (mono > 0) SpendAtLeast(TownHallStorage.Instance, RefinedKind.Monolith, QualityTier.Medium, mono, spent);
        b.fortifyInProgress = true;

        var pm = PopulationManager.Instance;
        BuildingInstance pickup = pm != null ? pm.Hall : null;
        foreach (var sp in spent)
            if (sp.source != null) { pickup = sp.source; break; }

        Logistics.Request(new HaulJob
        {
            from = pickup,
            to = b,
            prefer = b,
            onDeliver = () =>
            {
                if (b == null) { Refund(gold, 0, 0, 0, spent); return; }
                float frac = b.MaxHp > 0f ? b.Hp / b.MaxHp : 1f;
                b.fortified = true;
                b.fortifyInProgress = false;
                if (!b.IsRuined) b.hp = b.MaxHp * Mathf.Max(frac, 0.5f);
                WarManager.Notify($"{b.type.name} укреплено: прочность +{(FortifyHpMul - 1f) * 100f:F0}%", true);
            },
            onAbort = loaded =>
            {
                Refund(gold, 0, 0, 0, spent);
                if (b != null) b.fortifyInProgress = false;
            }
        });
        return true;
    }

    static float FortifyHeight(BuildingInstance b) { return ShowFortify(b) ? 30f : 0f; }

    // Короткие названия материалов для строки цены
    static string ShortKind(RefinedKind k)
    {
        switch (k)
        {
            case RefinedKind.Planks: return "Доски";
            case RefinedKind.StonePlates: return "Плиты";
            case RefinedKind.ReinforcedPlates: return "Укр. плиты";
            case RefinedKind.TarredBeams: return "Брус";
            case RefinedKind.Monolith: return "Монолит";
            default: return "Сталь";
        }
    }

    // Цена обычными ресурсами: цвета ресурсов, нехватка - красным
    static string BaseCostLine(int g, int w, int s, int f)
    {
        var rm = ResourceManager.Instance;
        List<string> parts = new List<string>();
        AddRes(parts, g, rm != null ? rm.gold : 0, "зол", UiKit.GoldHex);
        AddRes(parts, w, rm != null ? rm.wood : 0, "дер", UiKit.WoodHex);
        AddRes(parts, s, rm != null ? rm.stone : 0, "кам", UiKit.StoneHex);
        AddRes(parts, f, rm != null ? rm.food : 0, "еды", UiKit.FoodHex);
        return parts.Count > 0 ? string.Join(UiKit.Col(UiKit.MutedHex, " · "), parts) : UiKit.Col(UiKit.MutedHex, "бесплатно");
    }

    static void AddRes(List<string> parts, int need, int have, string what, string hex)
    {
        if (need <= 0) return;
        parts.Add(UiKit.Col(have >= need ? hex : UiKit.BadHex, need + " " + what));
    }

    // Почему нельзя улучшить ("" - можно)
    static string UpgradeProblem(BuildingInstance b)
    {
        if (b.UnderConstruction) return "Здание ещё строится";
        if (b.IsRuined) return "Сначала восстановите здание";
        if (RaidBlocksUpgrade) return "Идёт набег — улучшать нельзя";
        if (!HallAllowsUpgrade(b)) return $"Ур. {b.level + 1} — только с Цитадели Ур. {HallLevelForUpgrade(b, b.level + 1)}";
        string req = HallReqProblem(b);
        if (req != "") return req;
        ResourceManager rm = ResourceManager.Instance;
        if (rm == null) return "Нет казны";
        BaseCost(b, out int g, out int w, out int s, out int f);
        List<string> lack = new List<string>();
        if (rm.gold < g) lack.Add($"{g - rm.gold} золота");
        if (rm.wood < w) lack.Add($"{w - rm.wood} дерева");
        if (rm.stone < s) lack.Add($"{s - rm.stone} камня");
        if (rm.food < f) lack.Add($"{f - rm.food} еды");
        int pn = PartsFor(b.type.name, b.level + 1);
        if (rm.parts < pn) lack.Add($"{pn - rm.parts} деталей");

        UpgradeStep step = StepFor(b.type.name, b.level + 1);
        TownHallStorage st = TownHallStorage.Instance;
        if (step != null)
        {
            foreach (var it in step.items)
            {
                int have = st != null ? CountAtLeast(st, it.kind, step.minTier) : 0;
                if (have < it.amount) lack.Add($"{KindLabel(it.kind).ToLower()} ({QualityNote(step.minTier)}): {it.amount - have}");
            }
        }
        return lack.Count > 0 ? "Не хватает: " + string.Join(", ", lack) : "";
    }

    static void DrawFortifyRow(float x, float y, float w, BuildingInstance b)
    {
        if (b.fortified)
        {
            Rect fr = new Rect(x, y + 4f, w, 22f);
            LocGUI.Label(fr, UiKit.Col("#D8A060", "■ Укреплено") + UiKit.Col(UiKit.MutedHex, $"  прочность ×{FortifyHpMul:F1}"), UiKit.MutedStyle);
            UiKit.Tip(fr, "Здание укреплено смоляным брусом: прочность выше навсегда (и после восстановления из руин).");
            return;
        }
        FortifyCost(b, out int beams, out int mono, out int gold);
        string p = FortifyProblem(b);
        string cost = $"{gold} зол, смоляной брус: {beams}" + (mono > 0 ? $", монолит: {mono}" : "");
        string tip = $"Прочность +{(FortifyHpMul - 1f) * 100f:F0}% навсегда.\nЦена: {cost}." + (p != "" ? "\n" + UiKit.Col(UiKit.BadHex, p) : "");
        string label = b.fortifyInProgress ? "Укрепление: несут..." : $"Укрепить (+{(FortifyHpMul - 1f) * 100f:F0}%)";
        if (UiKit.Button(new Rect(x, y, w, 26f), label, p == "", UiKit.Kind.Normal, tip)) TryFortify(b);
    }

    public static float SectionHeight(BuildingInstance b)
    {
        return CoreSectionHeight(b) + FortifyHeight(b) + MassHeight(b) + BoostHeight(b);
    }

    public static float DrawUpgradeSection(float x, float y, float w, BuildingInstance b)
    {
        float core = DrawCoreSection(x, y, w, b);
        float f = FortifyHeight(b);
        if (f > 0f) DrawFortifyRow(x, y + core, w, b);
        float m = MassHeight(b);
        if (m > 0f) DrawMassRow(x, y + core + f, w, b);
        float bo = BoostHeight(b);
        if (bo > 0f) DrawBoostRow(x, y + core + f + m, w, b);
        return core + f + m + bo;
    }

    // ---------- Ускорение мастерских за марки (финал, BuildingInstance_Boost) ----------

    static float BoostHeight(BuildingInstance b)
    {
        if (b == null || !b.CanBoost || b.UnderConstruction || WarCityMode.Active) return 0f;
        return (b.BoostLeft > 0f ? 22f : 0f) + 28f + 6f;
    }

    static void DrawBoostRow(float x, float y, float w, BuildingInstance b)
    {
        float cy = y + 2f;
        float left = b.BoostLeft;
        if (left > 0f)
        {
            Rect bar = new Rect(x, cy, w, 18f);
            UiKit.Bar(bar, Mathf.Clamp01(left / (Balance.BoostMaxMinutes * 60f)), new Color(0.45f, 0.75f, 1f), $"Работа ×{Balance.BoostMul:F0} · ещё {Balance.Dur(left)}");
            UiKit.Tip(bar, "Изготовление идёт быстрее, пока не кончится время (оно идёт и без вас).");
            cy += 22f;
        }
        int cost = b.BoostCost;
        string p = b.BoostProblem();
        string label = left > 0f ? $"Продлить ×{Balance.BoostMul:F0} на {Balance.BoostMinutes:F0} мин · {cost} ◈" : $"Ускорить работу ×{Balance.BoostMul:F0} на {Balance.BoostMinutes:F0} мин · {cost} ◈";
        string tip = p == "" ? $"Изготовление в этом здании пойдёт в {Balance.BoostMul:F0} раза быстрее {Balance.BoostMinutes:F0} минут. Не мгновенно: просто быстрее. Всего не больше {Balance.BoostMaxMinutes:F0} мин." : p;
        if (UiKit.Button(new Rect(x, cy, w, 28f), label, p == "", UiKit.Kind.Normal, tip))
        {
            BuildingInstance target = b;
            int have = ResourceManager.Instance != null ? ResourceManager.Instance.marks : 0;
            ConfirmDialog.Ask($"Ускорить за {cost} марок?",
                $"{b.type.name} будет работать в {Balance.BoostMul:F0} раза быстрее ещё {Balance.BoostMinutes:F0} минут. У вас {have} марок.",
                "Ускорить", () => { if (target != null) target.BuyBoost(); }, UiKit.Kind.Primary);
        }
    }

    // ---------- Улучшение сразу многих: стены и ворота ----------
    // Под кнопкой «Улучшить» у стены (и ворот) - ещё две: улучшить весь участок стены или все такие стены в городе.
    // Берутся постройки того же вида и того же уровня; ближние к выбранной - первыми, сколько хватит ресурсов.
    // Каждую улучшают как обычно (цена списывается сразу, материалы несут жители).

    static bool MassKind(BuildingInstance b) { return b != null && b.type != null && (b.IsWall || b.IsGate); }

    static bool JoinsWall(BuildingInstance b)
    {
        return b != null && b.type != null && b.isActiveAndEnabled && (b.IsWall || b.IsGate || b.type.name == "Башня");
    }

    static bool SameForUpgrade(BuildingInstance b, BuildingInstance o)
    {
        return o != null && o.type != null && o.isActiveAndEnabled && o.type.name == b.type.name && o.level == b.level
            && !o.upgradeInProgress && !o.IsRuined && o.level < MaxLevelFor(o);
    }

    /// <summary>
    /// Такие же стены (вид и уровень как у b), которые можно улучшить: на участке, соединённом с b
    /// стенами, воротами и башнями (segment), или во всём городе. Ближние к b - первыми.
    /// </summary>
    public static List<BuildingInstance> MassTargets(BuildingInstance b, bool segment)
    {
        var res = new List<BuildingInstance>();
        if (!MassKind(b)) return res;

        if (!segment)
        {
            foreach (var o in BuildingInstance.All) if (SameForUpgrade(b, o)) res.Add(o);
        }
        else
        {
            var cells = new Dictionary<Vector2Int, BuildingInstance>();
            foreach (var o in BuildingInstance.All)
            {
                if (!JoinsWall(o)) continue;
                Vector2Int oc = o.OriginCell, sz = o.type.size;
                for (int dx = 0; dx < Mathf.Max(1, sz.x); dx++)
                    for (int dy = 0; dy < Mathf.Max(1, sz.y); dy++)
                        cells[new Vector2Int(oc.x + dx, oc.y + dy)] = o;
            }
            var seen = new HashSet<BuildingInstance> { b };
            var queue = new Queue<BuildingInstance>();
            queue.Enqueue(b);
            Vector2Int[] dirs = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (SameForUpgrade(b, cur)) res.Add(cur);
                Vector2Int oc = cur.OriginCell, sz = cur.type.size;
                for (int dx = 0; dx < Mathf.Max(1, sz.x); dx++)
                    for (int dy = 0; dy < Mathf.Max(1, sz.y); dy++)
                        foreach (var d in dirs)
                            if (cells.TryGetValue(new Vector2Int(oc.x + dx, oc.y + dy) + d, out var nb) && nb != null && seen.Add(nb))
                                queue.Enqueue(nb);
            }
        }

        Vector2Int c = b.OriginCell;
        res.Sort((p, q) => (p.OriginCell - c).sqrMagnitude.CompareTo((q.OriginCell - c).sqrMagnitude));
        return res;
    }

    /// <summary>На сколько построек из списка (по порядку) хватит ресурсов, и сколько это стоит.</summary>
    public static int MassAffordable(List<BuildingInstance> list, out int gold, out int wood, out int stone, out int food,
                                     out int parts, out Dictionary<RefinedKind, int> mats)
    {
        gold = wood = stone = food = parts = 0;
        mats = new Dictionary<RefinedKind, int>();
        var rm = ResourceManager.Instance;
        var st = TownHallStorage.Instance;
        if (rm == null || list == null || WarCityMode.Active) return 0;

        int n = 0;
        var have = new Dictionary<RefinedKind, int>();   // сколько материала на складах (считаем один раз)
        foreach (var o in list)
        {
            BaseCost(o, out int g, out int w, out int s, out int f);
            int p = PartsFor(o.type.name, o.level + 1);
            UpgradeStep step = StepFor(o.type.name, o.level + 1);
            var next = new Dictionary<RefinedKind, int>(mats);
            bool ok = rm.HasEnough(gold + g, wood + w, stone + s, food + f) && rm.parts >= parts + p;
            if (ok && step != null && step.items != null)
                foreach (var it in step.items)
                {
                    next.TryGetValue(it.kind, out int had);
                    next[it.kind] = had + it.amount;
                    if (st == null) { ok = false; continue; }
                    if (!have.TryGetValue(it.kind, out int inStock)) have[it.kind] = inStock = CountAtLeast(st, it.kind, step.minTier);
                    if (inStock < had + it.amount) ok = false;
                }
            if (!ok) break;
            gold += g; wood += w; stone += s; food += f; parts += p;
            mats = next;
            n++;
        }
        return n;
    }

    /// <summary>Заказать улучшение построек из списка по порядку, пока хватает ресурсов. Возвращает, сколько заказано.</summary>
    public static int UpgradeMany(List<BuildingInstance> list)
    {
        int n = 0;
        if (list == null) return 0;
        foreach (var o in list)
        {
            if (o == null) continue;
            if (!TryUpgrade(o)) break;
            n++;
        }
        return n;
    }

    // Списки считаем не чаще раза в кадр (OnGUI зовётся несколько раз за кадр)
    static BuildingInstance massFor;
    static int massFrame = -1;
    static List<BuildingInstance> massSegment, massAll;

    static void RefreshMass(BuildingInstance b)
    {
        if (massFor == b && massFrame == Time.frameCount) return;
        massFor = b;
        massFrame = Time.frameCount;
        massSegment = MassTargets(b, true);
        massAll = MassTargets(b, false);
    }

    static bool ShowMass(BuildingInstance b)
    {
        if (!MassKind(b) || WarCityMode.Active || b.level >= MaxLevelFor(b)) return false;
        RefreshMass(b);
        // Есть смысл, только если кроме этой есть ещё хоть одна такая же
        int others = 0;
        foreach (var o in massAll) if (o != b) others++;
        return others > 0;
    }

    static float MassHeight(BuildingInstance b) { return ShowMass(b) ? 32f : 0f; }

    static void DrawMassRow(float x, float y, float w, BuildingInstance b)
    {
        RefreshMass(b);
        float gap = 6f, bw = (w - gap) / 2f, by = y + 4f;
        MassButton(new Rect(x, by, bw, 26f), b, massSegment, "Участок", "на этом участке стены (соединены стенами, воротами и башнями)");
        MassButton(new Rect(x + bw + gap, by, bw, 26f), b, massAll, "Все в городе", "во всём городе");
    }

    static void MassButton(Rect r, BuildingInstance b, List<BuildingInstance> list, string title, string where)
    {
        int total = list.Count;
        int can = MassAffordable(list, out int g, out int wd, out int s, out int f, out int p, out var mats);
        int target = b.level + 1;
        string what = b.IsGate ? "ворота" : "стены";

        // Цена за всех, на кого хватает (если не хватает ни на одну - цена за одну, чтобы было видно, чего нет)
        if (can == 0 && total > 0)
        {
            BaseCost(list[0], out g, out wd, out s, out f);
            p = PartsFor(list[0].type.name, target);
            mats = new Dictionary<RefinedKind, int>();
            UpgradeStep st1 = StepFor(list[0].type.name, target);
            if (st1 != null && st1.items != null) foreach (var it in st1.items) mats[it.kind] = it.amount;
        }
        List<string> cost = new List<string>();
        if (g > 0) cost.Add(g + " зол");
        if (wd > 0) cost.Add(wd + " дер");
        if (s > 0) cost.Add(s + " кам");
        if (f > 0) cost.Add(f + " еды");
        if (p > 0) cost.Add(p + " дет");
        foreach (var kv in mats) cost.Add(ShortKind(kv.Key).ToLower() + " " + kv.Value);
        string costLine = cost.Count > 0 ? string.Join(", ", cost) : "бесплатно";

        string tip = $"Улучшить до Ур. {target} все {what} Ур. {b.level} {where}: {total} шт.\n";
        if (can == 0) tip += UiKit.Col(UiKit.BadHex, "Ресурсов не хватает даже на одно улучшение.") + $"\nЦена одного: {costLine}.";
        else if (can < total) tip += $"Ресурсов хватит на {can} (ближние к выбранной — первыми).\nЦена за {can}: {costLine}.";
        else tip += $"Цена за все: {costLine}.";
        tip += "\nЦена списывается сразу; уровень вырастет, когда жители донесут материалы.";

        string label = can >= total ? $"{title}: {total}" : $"{title}: {can}/{total}";
        if (UiKit.Button(r, label, can > 0, UiKit.Kind.Normal, tip))
        {
            int done = UpgradeMany(list);
            if (done > 0) WarManager.Notify($"Заказано улучшение до Ур. {target}: {what}, {done} шт.", true);
            massFrame = -1;
        }
    }

    // Компактный блок: строка цены, строка материалов (если нужны), кнопка «Улучшить»
    const float CostRowH = 22f, MatRowH = 18f, UpBtnH = 28f, UpGap = 6f;

    static float CoreSectionHeight(BuildingInstance b)
    {
        if (b == null || b.type == null) return 0f;
        if (b.level >= MaxLevelFor(b)) return 24f;
        if (b.Upgrading) return 20f + 4f + 26f + 6f;   // полоса времени + «Ускорить»
        if (b.upgradeInProgress) return 24f;

        UpgradeStep step = StepFor(b.type.name, b.level + 1);
        bool mats = (step != null && step.items != null && step.items.Length > 0) || PartsFor(b.type.name, b.level + 1) > 0;
        return CostRowH + (mats ? MatRowH : 0f) + (b.IsTownHall ? MatRowH : 0f) + UpBtnH + UpGap;
    }

    static float DrawCoreSection(float x, float y, float w, BuildingInstance b)
    {
        float total = CoreSectionHeight(b);
        if (total <= 0f) return 0f;

        if (b.level >= MaxLevelFor(b))
        {
            LocGUI.Label(new Rect(x, y, w, 22f), UiKit.Col(UiKit.GoodHex, "★") + UiKit.Col(UiKit.MutedHex, $" Максимальный уровень (Ур. {b.level})"), UiKit.MutedStyle);
            return total;
        }

        if (b.Upgrading)
        {
            DrawTimer(x, y, w, b);
            return total;
        }

        if (b.upgradeInProgress)
        {
            Rect pr = new Rect(x, y, w, 22f);
            LocGUI.Label(pr, UiKit.Col(UiKit.InfoHex, $"Улучшение до Ур. {b.level + 1}: несут материалы..."), UiKit.MutedStyle);
            UiKit.Tip(pr, "Уровень вырастет, когда житель донесёт материалы до здания.");
            return total;
        }

        int target = b.level + 1;
        UpgradeStep step = StepFor(b.type.name, target);
        TownHallStorage storage = TownHallStorage.Instance;
        float cy = y;

        // Обычные ресурсы - одной строкой
        BaseCost(b, out int g, out int wd, out int s, out int f);
        GUIStyle line = new GUIStyle(UiKit.Body) { wordWrap = false, clipping = TextClipping.Clip };
        LocGUI.Label(new Rect(x, cy, w, CostRowH), UiKit.Col(UiKit.MutedHex, $"До Ур. {target} ({Balance.Dur(Balance.UpgradeSeconds(b.type, target))}):  ") + BaseCostLine(g, wd, s, f), line);
        cy += CostRowH;

        // Доски / плиты / сталь (и детали) - короткой строкой, качество - в подсказке
        int partsNeed = PartsFor(b.type.name, target);
        if ((step != null && step.items != null && step.items.Length > 0) || partsNeed > 0)
        {
            List<string> parts = new List<string>();
            StringBuilder tip = new StringBuilder();
            QualityTier minT = step != null ? step.minTier : QualityTier.Medium;
            tip.Append($"Материалы со склада Цитадели и Хранилищ ({QualityNote(minT)}):");
            if (partsNeed > 0)
            {
                int haveP = ResourceManager.Instance != null ? ResourceManager.Instance.parts : 0;
                parts.Add($"Детали {UiKit.Col(haveP >= partsNeed ? UiKit.GoodHex : UiKit.BadHex, haveP + "/" + partsNeed)}");
                tip.Append($"\n Детали (Литейная): {haveP}/{partsNeed}");
            }
            if (step != null && step.items != null) foreach (var it in step.items)
            {
                int have = storage != null ? CountAtLeast(storage, it.kind, step.minTier) : 0;
                string col = have >= it.amount ? UiKit.GoodHex : UiKit.BadHex;
                parts.Add($"{ShortKind(it.kind)} {UiKit.Col(col, have + "/" + it.amount)}");
                tip.Append($"\n {KindLabel(it.kind)}: {have}/{it.amount}");
            }
            string q = minT == QualityTier.Medium ? "" : UiKit.Col(UiKit.MutedHex, $"  ({TownHallStorage.TierName(minT).ToLower()}+)");
            GUIStyle sm = new GUIStyle(UiKit.Small) { wordWrap = false, clipping = TextClipping.Clip };
            Rect mr = new Rect(x, cy, w, MatRowH);
            LocGUI.Label(mr, string.Join(UiKit.Col(UiKit.MutedHex, " · "), parts) + q, sm);
            UiKit.Tip(mr, tip.ToString());
            cy += MatRowH;
        }

        // Цитадель: что нужно построить в городе (зелёное - есть, красное - нет)
        if (b.IsTownHall)
        {
            var reqs = HallReqs(target);
            var parts2 = new List<string>();
            foreach (var r in reqs) parts2.Add($"{r.label} {UiKit.Col(r.have >= r.need ? UiKit.GoodHex : UiKit.BadHex, Mathf.Min(r.have, r.need) + "/" + r.need)}");
            Rect rr = new Rect(x, cy, w, MatRowH);
            GUIStyle sm2 = new GUIStyle(UiKit.Small) { wordWrap = false, clipping = TextClipping.Clip };
            LocGUI.Label(rr, UiKit.Col(UiKit.MutedHex, "В городе: ") + string.Join(UiKit.Col(UiKit.MutedHex, " · "), parts2), sm2);
            UiKit.Tip(rr, $"Чтобы поднять Цитадель до Ур. {target}, город должен дорасти: постройте эти здания (готовые, не в руинах).");
            cy += MatRowH;
        }

        string problem = UpgradeProblem(b);
        string btnTip = problem == "" ? $"Цена списывается сразу. Житель донесёт материалы, потом улучшение идёт {Balance.Dur(Balance.UpgradeSeconds(b.type, target))}." : problem;
        string btnText = RaidBlocksUpgrade ? "Идёт набег — улучшать нельзя" : !HallAllowsUpgrade(b) ? $"Ур. {target} — с Цитадели Ур. {HallLevelForUpgrade(b, target)}"
                       : HallReqProblem(b) != "" ? "Сначала постройте нужное" : $"Улучшить до Ур. {target}";
        if (UiKit.Button(new Rect(x, cy, w, UpBtnH), btnText, CanUpgrade(b), UiKit.Kind.Primary, btnTip))
            TryUpgrade(b);

        return total;
    }

    /// <summary>Полоса «идёт улучшение / стройка» и под ней строка (или кнопка «Ускорить за марки», если включена; высота 50).</summary>
    public static void DrawTimer(float x, float y, float w, BuildingInstance b)
    {
        bool build = b.UnderConstruction;
        float frac = build ? b.BuildProgress : b.UpgradeProgress;
        string label = build ? $"Стройка · ещё {Balance.Dur(b.BuildLeft)}" : $"До Ур. {b.UpgradeTarget} · ещё {Balance.Dur(b.UpgradeLeft)}";
        Rect bar = new Rect(x, y, w, 20f);
        UiKit.Bar(bar, frac, UiKit.Info, label);
        UiKit.Tip(bar, build ? "Здание строится и пока не работает." : "Здание работает на старом уровне, пока идёт улучшение.");

        if (!SkipForMarksOn)
        {
            LocGUI.Label(new Rect(x, y + 24f, w, 26f), UiKit.Col(UiKit.MutedHex, "Идёт само, даже когда вы не в игре"), UiKit.Small);
            return;
        }
        // Цена растёт с оставшимся временем и уровнем (Balance.SkipMarks): Цитадель за пару марок не ускорить
        int cost = b.SkipCost;
        int have = ResourceManager.Instance != null ? ResourceManager.Instance.marks : 0;
        string tip = have >= cost
            ? $"Закончить сразу за {cost} марок (у вас {have}). Цена зависит от того, сколько осталось ждать, и от уровня: чем ближе к концу, тем дешевле. Можно и не ускорять: идёт само, даже когда вы не в игре."
            : $"Не хватает марок: нужно {cost}, у вас {have}. Идёт само, даже когда вы не в игре.";
        if (UiKit.Button(new Rect(x, y + 24f, w, 26f), $"Ускорить · {cost} ◈", b.CanSkipForMarks, UiKit.Kind.Normal, tip))
        {
            BuildingInstance target = b;
            string what = b.type != null ? b.type.name : "Здание";
            ConfirmDialog.Ask($"Ускорить за {cost} марок?",
                $"{what} будет готово сразу. Осталось ждать {Balance.Dur(b.TimerLeft)}. У вас {have} марок.",
                "Ускорить", () => { if (target != null) target.SkipForMarks(); }, UiKit.Kind.Primary);
        }
    }

    /// <summary>
    /// Кнопка «Ускорить за марки» у стройки и улучшения. Обновление 18 её выключало (было 1 марка за 2 минуты -
    /// слишком дёшево), в обновлении 21 включена с новой ценой (Balance.SkipMarks). Выключить - поставить false.
    /// </summary>
    public static readonly bool SkipForMarksOn = true;
}
