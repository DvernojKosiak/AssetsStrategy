using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Баланс в одном месте: цена и время постройки каждого здания, цена и время улучшений,
/// рост цены за 2-е, 3-е... здание одного вида, цена и время обучения войск.
/// Все числа - в таблицах ниже, их можно спокойно менять. Таблица перекрывает и то,
/// что задано в инспекторе (BuildingPlacer.buildingTypes, список войск казармы).
///
/// Как считается:
///  - Постройка: цена из таблицы × рост за копию. final-8: рост не прибавкой, а в разы - каждое следующее
///    здание вида дороже предыдущего в (1 + copyStep) раза (жильё ×1,25, добыча ×1,28, башни ×1,3, остальное ×1,45).
///    Десятый дом - уже в 7,5 раза дороже первого, пятнадцатый - в 23 раза: на большой казне новые дома не копеечные,
///    и выгоднее улучшать то, что есть (улучшение стоит ×1,5 / ×3 / ×5 от первой цены и не дорожает от числа копий).
///    Каждый ресурс цены - не больше 90% того, что вмещает город (CostCapShare): построить можно всегда,
///    а чтобы дальше было куда расти - Хранилища и Цитадель.
///  - Улучшение до Ур.N: цена постройки × UpgradeCostMul[N] (Цитадель - своей таблицей HallUpgrade).
///  - Время стройки - из таблицы × рост за копию (1 + TimeCopyStep × сколько таких уже стоит, не больше TimeCopyMax;
///    стены, дорожки, мосты и ворота не дольше). Улучшение до Ур.N идёт (время из таблицы) × UpgradeTimeMul[N].
///  - vse-v-odnom-5: первые жилые и добывающие здания строятся быстрее (легче начать), улучшения на высокие
///    уровни и Цитадель - дольше (в онлайне время идёт и без игрока, есть смысл вернуться позже).
///  - Пока здание строится, оно не работает (как руины), но и не ломается. Улучшаемое здание работает
///    на старом уровне, пока не кончится время.
/// </summary>
public static class Balance
{
    public struct Cost
    {
        public int gold, wood, stone, food;
        public float seconds;      // время постройки (0 - сразу)
        public float copyStep;     // насколько дороже каждое следующее здание этого вида
        public Cost(int g, int w, int s, int f, float sec, float step)
        { gold = g; wood = w; stone = s; food = f; seconds = sec; copyStep = step; }
    }

    // Рост цены за копию по видам: добыча, жильё, всё остальное. Стены, дорожки, мосты и ворота не дорожают.
    // final-8: множитель за каждую копию (было +25% / +15% / +40% прибавкой и не дороже ×4)
    const float Prod = 0.28f, Home = 0.25f, Other = 0.45f, Flat = 0f;
    // Обновление 12: казармы и пушки дорожают мягче (было ×1,45): их число и так ограничено уровнем Цитадели
    const float Defense = 0.25f;
    public const float CopyMax = 200f;         // на деле упирается в CostCapShare
    public const float CostCapShare = 0.9f;    // ресурс цены - не больше этой доли вместимости города
    const float OldCopyMax = 4f;               // старая формула - для возврата за снос (дома, построенные до final-8)

    // Время стройки тоже растёт за каждое такое же здание: +15%, но не дольше, чем в 2,5 раза
    public const float TimeCopyStep = 0.15f, TimeCopyMax = 2.5f;

    //                                         золото дерево камень еда   секунд  рост за копию
    static readonly Dictionary<string, Cost> Buildings = new Dictionary<string, Cost>
    {
        // Цитадель: цена нужна только для подсказок, улучшения - своей таблицей ниже
        { "Ратуша",                          new Cost( 200, 150, 150,   0,    0f, Flat) },

        // Дорожки, стены, мосты: рисуются мазком, поэтому почти мгновенно
        { BuildingInstance.RoadName,         new Cost(   0,   2,   1,   0,    0f, Flat) },
        { BuildingInstance.CobbleName,       new Cost(   0,   0,   2,   0,    0f, Flat) },
        { BuildingInstance.WallName,         new Cost(   3,   5,   9,   0,    4f, Flat) },
        // Ворота (финал): заметно дороже стены, и каждые следующие ворота дороже прошлых на 10% (не больше чем в 4 раза).
        // Раньше ворота стоили почти как несколько стен, но крепче и пропускают своих - стену выгоднее было ставить из одних ворот
        { BuildingInstance.GateName,         new Cost(  60,  70, 110,   0,   30f, 0.1f) },
        { BuildingInstance.BridgeName,       new Cost(   0,  10,   6,   0,    8f, Flat) },

        // Основа города
        { "Жилой дом",                       new Cost(  60,  80,  40,   0,   20f, Home) },
        { BuildingInstance.GuestHouseName,   new Cost( 110, 110,  60,   0,   60f, Home) },
        { "Лесопилка",                       new Cost(  45,  40,  35,  15,   25f, Prod) },
        { "Шахта",                           new Cost(  70,  80,  20,  20,   35f, Prod) },
        { "Ферма",                           new Cost(  45,  70,  25,  10,   25f, Prod) },
        { BerryStation.TypeName,             new Cost(  70,  90,  40,  20,   45f, Prod) },
        { "Мастерская",                      new Cost( 120, 100,  90,  20,   90f, Other) },
        { CourierHouse.TypeName,             new Cost( 140, 100,  90,  30,   90f, Other) },
        { WarehouseController.TypeName,      new Cost( 100, 160, 130,   0,  150f, Other) },
        { HealerHouse.TypeName,              new Cost( 100,  80,  60,  30,   90f, Other) },
        { KitchenController.TypeName,        new Cost( 180, 130, 120,  40,  150f, Other) },
        { StableController.TypeName,         new Cost( 150, 180, 100,  40,  150f, Other) },
        { OutpostController.TypeName,        new Cost( 380, 240, 260,  60,  240f, Other) },
        { WagonYard.TypeName,                new Cost( 450, 300, 260,   0,  300f, Other) },
        { MarketHouse.TypeName,              new Cost( 260, 190, 170,  30,  240f, Other) },

        // Война
        { BarracksController.TypeName,       new Cost( 170, 140, 120,  50,  120f, Defense) },
        { "Башня",                           new Cost( 130,  60, 160,  30,   90f, 0.15f) },  // обновление 12: было ×1,3 за копию; башен и так не больше 9 (лимит Цитадели)
        { ArmoryController.TypeName,         new Cost( 160, 110, 120,   0,  120f, Other) },
        { MagicCannonController.TypeName,    new Cost( 190,  70, 160,  30,  150f, Defense) },
        { GroundCannonController.TypeName,   new Cost( 280,  90, 210,  30,  240f, Defense) },

        // Пути развития
        { PotionHouse.TypeName,              new Cost( 180,  90, 110,   0,  180f, Other) },
        { WeaponForge.TypeName,              new Cost( 230,  90, 170,   0,  240f, Other) },
        { FoundryController.TypeName,        new Cost( 210, 110, 190,  30,  240f, Other) },
        { EngineerYard.TypeName,             new Cost( 340, 220, 240,  40,  300f, Other) },
        { MagicArmory.TypeName,              new Cost( 250, 100, 140,  30,  240f, Other) },
        { LifeAltar.TypeName,                new Cost( 300,  70, 190,  40,  300f, Other) },
        { EngineerBarracks.TypeName,         new Cost( 380, 180, 150,  30,  300f, Other) },
        { IndustrialPlant.TypeName,          new Cost( 450, 150, 260,   0,  360f, Other) },
        { WarBuilderYard.TypeName,           new Cost( 340, 230, 150,  30,  300f, Other) },
    };

    // Улучшение до Ур.2 / 3 / 4: множитель цены постройки и времени постройки
    static readonly float[] UpgradeCostMul = { 0f, 0f, 1.5f, 3f, 5f };
    static readonly float[] UpgradeTimeMul = { 0f, 0f, 2.5f, 6f, 12f };   // было 2 / 4 / 7

    // Цитадель: улучшение до Ур.2 и Ур.3 (цена влезает в склад текущего уровня Цитадели)
    static readonly Cost[] HallUpgrade =
    {
        new Cost(1200, 1000, 1000, 300, 1200f, Flat),   // до Ур.2 - 20 минут (было 15)
        new Cost(4500, 3000, 3200, 800, 3600f, Flat),   // до Ур.3 - 1 час (было 45 минут)
    };

    // Ускорение стройки и улучшения за марки (обновление 21, в финале - примерно в 2,5 раза дороже).
    // Цена = (SkipBase + SkipPerMin × минут^SkipPow) × множитель уровня, округление вверх.
    // Минуты в степени 0,85: час дороже минуты, но не в 60 раз. Множитель: стройка ×1, до Ур.2 ×1,3,
    // до Ур.3 ×1,6, до Ур.4 ×1,9; Цитадель ×2. Примеры: жилой дом (20 с) - 10 марок (было 5), казарма до Ур.3 (12 мин) - 192 (было 78),
    // Цитадель до Ур.2 (20 мин) - 366 (было 158), до Ур.3 (1 ч) - 918 (было 394). Ждать осталось меньше - дешевле.
    public const float SkipBase = 4f, SkipPerMin = 14f, SkipPow = 0.85f;
    public const float SkipLevelStep = 0.3f, SkipHallMul = 2f;

    // ---------- Ускорение мастерских за марки (финал) ----------
    // Не мгновенно: работа идёт в BoostMul раза быстрее BoostMinutes минут. Купить ещё раз - время добавляется,
    // но всего не больше BoostMaxMinutes. Цена растёт с уровнем здания (BoostMarks).
    public const float BoostMul = 2f, BoostMinutes = 10f, BoostMaxMinutes = 30f;
    public const int BoostBaseMarks = 45, BoostMarksPerLevel = 15;

    public static int BoostMarks(int level) { return BoostBaseMarks + BoostMarksPerLevel * Mathf.Max(0, level - 1); }

    public static int SkipMarks(float secondsLeft, int targetLevel = 1, bool hall = false)
    {
        if (secondsLeft <= 0f) return 0;
        float minutes = secondsLeft / 60f;
        float mul = hall ? SkipHallMul : 1f + SkipLevelStep * Mathf.Max(0, targetLevel - 1);
        return Mathf.Max(1, Mathf.CeilToInt((SkipBase + SkipPerMin * Mathf.Pow(minutes, SkipPow)) * mul));
    }

    // ---------- Войска: цена и время обучения ----------
    //                                              золото еда секунд
    static readonly Dictionary<string, Vector3Int> Units = new Dictionary<string, Vector3Int>
    {
        { "Мечник",                               new Vector3Int( 30, 20,  15) },
        { "Лучник",                               new Vector3Int( 50, 25,  20) },
        { RaidManager.ScoutUnitName,              new Vector3Int( 40, 20,  15) },
        { BarracksController.RamName,             new Vector3Int( 55, 30,  25) },
        { "Рыцарь",                               new Vector3Int(120, 55,  40) },
        { BarracksController.HeavyKnightName,     new Vector3Int(110, 50,  45) },
        { "Полевой маг",                          new Vector3Int(140, 55,  45) },
        { "Всадник",                              new Vector3Int(180, 70,  50) },
        { "Конный лучник",                        new Vector3Int(195, 70,  55) },
        { BarracksController.FireBomberName,      new Vector3Int( 70, 30,  25) },
        { BarracksController.PoisonerName,        new Vector3Int(100, 35,  30) },
        { BarracksController.ElementalistName,    new Vector3Int(260, 85,  75) },
        { BarracksController.FieldCannonName,     new Vector3Int(320, 60,  90) },
        { BarracksController.CatapultName,        new Vector3Int(260, 45,  90) },
    };

    // ---------- Содержание зданий (CityConsumption: раз в цикл, × уровень здания) ----------
    // Раньше все здания ели только дерево. Теперь часть зданий ест камень: лесопилке нужен камень (точила, жернова),
    // шахте - дерево (крепь), военным и тяжёлым зданиям - и то и другое. Кого нет в таблице - 1 дерево.
    //                                                  золото дерево камень
    static readonly Dictionary<string, Vector3Int> Upkeep = new Dictionary<string, Vector3Int>
    {
        { "Лесопилка",                     new Vector3Int(0, 0, 1) },
        { "Шахта",                         new Vector3Int(0, 1, 0) },
        { "Ферма",                         new Vector3Int(0, 1, 0) },
        { BerryStation.TypeName,           new Vector3Int(0, 1, 0) },
        { "Мастерская",                    new Vector3Int(0, 1, 1) },
        { BarracksController.TypeName,     new Vector3Int(0, 1, 1) },
        { "Башня",                         new Vector3Int(0, 0, 1) },
        { MagicCannonController.TypeName,  new Vector3Int(0, 0, 1) },
        { GroundCannonController.TypeName, new Vector3Int(0, 0, 1) },
        { ArmoryController.TypeName,       new Vector3Int(0, 1, 1) },
        { WeaponForge.TypeName,            new Vector3Int(0, 1, 1) },
        { FoundryController.TypeName,      new Vector3Int(0, 1, 1) },
        { IndustrialPlant.TypeName,        new Vector3Int(0, 1, 1) },
        { EngineerYard.TypeName,           new Vector3Int(0, 1, 1) },
        { EngineerBarracks.TypeName,       new Vector3Int(0, 1, 1) },
        { WarehouseController.TypeName,    new Vector3Int(0, 0, 1) },
        { LifeAltar.TypeName,              new Vector3Int(0, 0, 1) },
        { KitchenController.TypeName,      new Vector3Int(0, 1, 1) },
    };

    /// <summary>Содержание здания за один цикл CityConsumption (уже × уровень). def* - что задано в CityConsumption.</summary>
    public static void UpkeepOf(BuildingInstance b, int defGold, int defWood, int defStone, out int g, out int w, out int s)
    {
        g = w = s = 0;
        if (b == null || b.type == null) return;
        Vector3Int u = Upkeep.TryGetValue(b.type.name, out Vector3Int v) ? v : new Vector3Int(defGold, defWood, defStone);
        int lv = Mathf.Max(1, b.level);
        g = u.x * lv; w = u.y * lv; s = u.z * lv;
    }

    // ---------- Применение ----------

    /// <summary>Переписать цены типов зданий по таблице (и заданных в инспекторе тоже).</summary>
    public static void ApplyBuildings(IEnumerable<BuildingType> types)
    {
        if (types == null) return;
        foreach (var t in types)
        {
            if (t == null || !Buildings.TryGetValue(t.name, out Cost c)) continue;
            t.costGold = c.gold; t.costWood = c.wood; t.costStone = c.stone; t.costFood = c.food;
        }
    }

    /// <summary>Переписать цену и время обучения войск по таблице.</summary>
    public static void ApplyUnits(List<UnitType> units)
    {
        if (units == null) return;
        foreach (var u in units)
        {
            if (u == null || !Units.TryGetValue(u.unitName, out Vector3Int v)) continue;
            u.costGold = v.x; u.costFood = v.y; u.trainTime = v.z;
        }
    }

    static Cost Of(BuildingType t)
    {
        if (t != null && Buildings.TryGetValue(t.name, out Cost c)) return c;
        // Нет в таблице - берём цену из типа, время по размеру
        int cells = t != null ? Mathf.Max(1, t.size.x * t.size.y) : 1;
        return t == null ? new Cost() : new Cost(t.costGold, t.costWood, t.costStone, t.costFood, 15f * cells, Other);
    }

    /// <summary>Во сколько раз дороже здание, если таких уже стоит existing.</summary>
    public static float CopyMul(BuildingType t, int existing)
    {
        if (t == null || existing <= 0) return 1f;
        float step = Of(t).copyStep;
        if (step <= 0f) return 1f;
        return Mathf.Min(CopyMax, Mathf.Pow(1f + step, existing));
    }

    /// <summary>Насколько (в %) следующее здание этого вида дороже текущего. 0 - не дорожает.</summary>
    public static int CopyStepPercent(BuildingType t) { return t == null ? 0 : Mathf.RoundToInt(Of(t).copyStep * 100f); }

    // Как было до final-8 (+шаг за копию, не дороже ×4)
    static float OldCopyMul(BuildingType t, int existing)
    {
        if (t == null || existing <= 0) return 1f;
        float step = Of(t).copyStep;
        float old = step == Prod ? 0.25f : (step == Home ? 0.15f : (step == Other ? 0.4f : step));
        return Mathf.Min(OldCopyMax, 1f + old * existing);
    }

    /// <summary>Цена постройки, если таких зданий уже стоит existing.</summary>
    public static void BuildCost(BuildingType t, int existing, out int g, out int w, out int s, out int f)
    {
        g = w = s = f = 0;
        if (t == null) return;
        float k = CopyMul(t, existing);
        g = Capped(t.costGold, k, StoreRes.Gold);
        w = Capped(t.costWood, k, StoreRes.Wood);
        s = Capped(t.costStone, k, StoreRes.Stone);
        f = Capped(t.costFood, k, StoreRes.Food);
    }

    /// <summary>
    /// От чего считать возврат за снос готового здания: меньшая из новой и старой (до final-8) цены. Иначе дома,
    /// построенные дёшево, можно было бы снести и получить больше, чем за них заплатили.
    /// </summary>
    public static void RefundBase(BuildingType t, int existing, out int g, out int w, out int s, out int f)
    {
        BuildCost(t, existing, out g, out w, out s, out f);
        if (t == null) return;
        float k = OldCopyMul(t, existing);
        g = Mathf.Min(g, Mathf.RoundToInt(t.costGold * k));
        w = Mathf.Min(w, Mathf.RoundToInt(t.costWood * k));
        s = Mathf.Min(s, Mathf.RoundToInt(t.costStone * k));
        f = Mathf.Min(f, Mathf.RoundToInt(t.costFood * k));
    }

    // Цена × рост, но не больше 90% вместимости города (и не дешевле первой цены)
    static int Capped(int baseCost, float k, StoreRes r)
    {
        if (baseCost <= 0) return 0;
        int v = Mathf.RoundToInt(baseCost * k);
        if (k > 1f && StorageLimits.Limited(r))
        {
            int cap = Mathf.FloorToInt(StorageLimits.Cap(r) * CostCapShare);
            v = Mathf.Max(baseCost, Mathf.Min(v, cap));
        }
        return v;
    }

    /// <summary>Цена следующего здания этого вида (с учётом уже построенных).</summary>
    public static void NextBuildCost(BuildingType t, out int g, out int w, out int s, out int f)
    {
        int n = BuildingPlacer.Instance != null && t != null ? BuildingPlacer.Instance.CountOfType(t.name) : 0;
        BuildCost(t, n, out g, out w, out s, out f);
    }

    /// <summary>Сколько секунд строится первое здание этого вида.</summary>
    public static float BuildSeconds(BuildingType t) { return t == null ? 0f : Of(t).seconds; }

    /// <summary>Сколько секунд строится здание, если таких уже стоит existing (каждое следующее - чуть дольше).</summary>
    public static float BuildSeconds(BuildingType t, int existing)
    {
        if (t == null) return 0f;
        Cost c = Of(t);
        if (existing <= 0 || c.copyStep <= 0f) return c.seconds;   // стены, дорожки, мосты, ворота - всегда одинаково
        return c.seconds * Mathf.Min(TimeCopyMax, 1f + TimeCopyStep * existing);
    }

    /// <summary>Сколько секунд будет строиться следующее здание этого вида.</summary>
    public static float NextBuildSeconds(BuildingType t)
    {
        int n = BuildingPlacer.Instance != null && t != null ? BuildingPlacer.Instance.CountOfType(t.name) : 0;
        return BuildSeconds(t, n);
    }

    /// <summary>Цена улучшения с уровня fromLevel до fromLevel + 1.</summary>
    public static void UpgradeCost(BuildingType t, int fromLevel, out int g, out int w, out int s, out int f)
    {
        g = w = s = f = 0;
        if (t == null) return;
        int target = fromLevel + 1;
        if (t.name == "Ратуша")
        {
            Cost h = HallUpgrade[Mathf.Clamp(target - 2, 0, HallUpgrade.Length - 1)];
            g = h.gold; w = h.wood; s = h.stone; f = h.food;
            return;
        }
        float k = UpgradeCostMul[Mathf.Clamp(target, 0, UpgradeCostMul.Length - 1)];
        g = Mathf.RoundToInt(t.costGold * k);
        w = Mathf.RoundToInt(t.costWood * k);
        s = Mathf.RoundToInt(t.costStone * k);
        f = Mathf.RoundToInt(t.costFood * k);
    }

    /// <summary>Сколько секунд идёт улучшение до targetLevel (после того, как принесли материалы).</summary>
    public static float UpgradeSeconds(BuildingType t, int targetLevel)
    {
        if (t == null || targetLevel < 2) return 0f;
        if (t.name == "Ратуша") return HallUpgrade[Mathf.Clamp(targetLevel - 2, 0, HallUpgrade.Length - 1)].seconds;
        float baseSec = Mathf.Max(Of(t).seconds, 4f);
        return baseSec * UpgradeTimeMul[Mathf.Clamp(targetLevel, 0, UpgradeTimeMul.Length - 1)];
    }

    /// <summary>«1 ч 05 мин», «4 мин 10 с», «25 с».</summary>
    public static string Dur(float seconds)
    {
        int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
        if (s >= 3600) return $"{s / 3600} ч {(s % 3600) / 60:00} мин";
        if (s >= 60) return s % 60 == 0 ? $"{s / 60} мин" : $"{s / 60} мин {s % 60:00} с";
        return s + " с";
    }
}
