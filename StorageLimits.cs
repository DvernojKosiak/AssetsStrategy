using UnityEngine;
using System.Collections.Generic;

/// <summary>Ресурсы казны (ResourceManager), у каждого свой лимит.</summary>
public enum StoreRes { Gold, Wood, Stone, Food, Iron, Oats, Berries, Herbs, Potions, Parts, Leaves, Resin, Tar, Rubber, SlowPotions, Marks }

/// <summary>
/// Лимиты хранения: сколько каждого ресурса может лежать в городе.
///  - Казна (золото, дерево, камень, еда, руда, овёс, ягоды, травы, зелья, детали, смола, пропитка, резина, листва):
///    лимит даёт Цитадель (растёт с её уровнем), каждое целое Хранилище добавляет к нему ещё (тоже по уровню).
///  - Материалы Цитадели (доски, плиты, сталь, укреплённые плиты, брус, монолит): у каждого вида свой лимит
///    в Цитадели (все качества вместе). Хранилища держат материалы сами (общая вместимость 100 / 200 / 320).
///  - Сверх лимита ничего не копится: курьеры не вывозят продукцию из зданий, пока в Цитадели нет места,
///    мастерская не берёт заказ, рынок не продаёт, обоз оставляет лишнее в Дальнем складе.
///    Что всё-таки пришло сверх лимита (добыча набега, награды и т.п.), пропадает - об этом пишет сообщение.
///  - Лимиты не действуют, пока город загружается, и в военном городе (клановая война).
/// Все числа - в таблицах ниже, их можно спокойно менять.
/// </summary>
public static class StorageLimits
{
    public const int ResCount = 16;

    //                                         Цитадель Ур.1  Ур.2   Ур.3        Хранилище Ур.1  Ур.2  Ур.3
    static readonly int[,] HallCap =
    {
        { 3000, 8000, 20000 },   // золото (последняя ячейка Дальнего склада - 30000: Цитадель Ур.3 + Хранилища)
        { 1500, 4000, 10000 },   // дерево
        { 1500, 4000, 10000 },   // камень
        { 1000, 2500,  6000 },   // еда
        {  200,  500,  1200 },   // железная руда
        {  200,  500,  1200 },   // овёс
        {  200,  500,  1200 },   // ягоды
        {  150,  400,  1000 },   // травы
        {   40,  100,   200 },   // зелья отравления (ещё лежат в Хранилищах - там своё место)
        {  150,  400,  1000 },   // детали
        {  100,  250,   600 },   // листва
        {  150,  400,  1000 },   // смола
        {  100,  250,   600 },   // смоляная пропитка
        {  100,  250,   600 },   // резина
        {   30,   80,   160 },   // зелья замедления
        {    0,    0,     0 },   // марки гильдии - без лимита (см. Limited)
    };

    static readonly int[,] WarehouseBonus =
    {
        { 2000, 4000, 7000 },    // золото
        { 1000, 2000, 3500 },    // дерево
        { 1000, 2000, 3500 },    // камень
        {  600, 1200, 2000 },    // еда
        {  150,  300,  500 },    // руда
        {  150,  300,  500 },    // овёс
        {  150,  300,  500 },    // ягоды
        {  100,  200,  400 },    // травы
        {    0,    0,    0 },    // зелья отравления - Хранилище держит их само (занимают место наравне с материалами)
        {  100,  200,  400 },    // детали
        {   60,  120,  250 },    // листва
        {  100,  200,  400 },    // смола
        {   60,  120,  250 },    // пропитка
        {   60,  120,  250 },    // резина
        {   20,   40,   80 },    // зелья замедления
        {    0,    0,    0 },    // марки
    };

    // Материалы в Цитадели: вид (порядок RefinedKind), все качества вместе
    static readonly int[,] RefinedCap =
    {
        {  80, 200, 400 },   // доски
        {  80, 200, 400 },   // каменные плиты
        {  60, 150, 300 },   // сталь
        {  30,  80, 160 },   // укреплённые плиты
        {  30,  80, 160 },   // смоляной брус
        {  10,  30,  60 },   // монолит
    };

    static readonly string[] Names = { "Золото", "Дерево", "Камень", "Еда", "Железная руда", "Овёс", "Ягоды", "Травы", "Зелья отравления",
                                       "Детали", "Листва", "Смола", "Смоляная пропитка", "Резина", "Зелья замедления", "Марки" };
    static readonly string[] Units = { "золота", "дерева", "камня", "еды", "руды", "овса", "ягод", "трав", "зелий отравления",
                                       "деталей", "листвы", "смолы", "пропитки", "резины", "зелий замедления", "марок" };

    public static string Name(StoreRes r) { return Names[(int)r]; }
    public static string Unit(StoreRes r) { return Units[(int)r]; }

    // ---------- Когда лимиты действуют ----------

    /// <summary>Город построен и загружен, Цитадель стоит. Иначе лимитов нет.
    /// final-13-fix-2: лимиты и в военной деревне войны кланов (раньше там копились миллионы)</summary>
    public static bool Active
    {
        get
        {
            var rm = ResourceManager.Instance;
            if (rm == null || !rm.limitsEnabled) return false;
            if (BuildingPlacer.Instance == null || !BuildingPlacer.Instance.WorldReady) return false;
            if (CityArmy.Restoring) return false;
            return HallBuilding != null;
        }
    }

    static BuildingInstance HallBuilding => PopulationManager.Instance != null ? PopulationManager.Instance.Hall : null;

    static int Lvl(int level) { return Mathf.Clamp(level, 1, 3) - 1; }

    public static int HallLevel { get { var h = HallBuilding; return h != null ? Mathf.Clamp(h.level, 1, 3) : 1; } }

    // ---------- Казна ----------

    /// <summary>Лимит Цитадели заданного уровня (без Хранилищ).</summary>
    public static int HallCapAt(StoreRes r, int hallLevel) { return HallCap[(int)r, Lvl(hallLevel)]; }

    /// <summary>Сколько одно Хранилище этого уровня добавляет к лимиту.</summary>
    public static int WarehouseBonusAt(StoreRes r, int whLevel) { return WarehouseBonus[(int)r, Lvl(whLevel)]; }

    /// <summary>Прибавка от всех Хранилищ города. Разрушенное в набеге тоже считается (стены стоят, его починят) -
    /// иначе после набега казна разом теряла бы всё сверх лимита Цитадели.</summary>
    public static int WarehousesBonus(StoreRes r)
    {
        int n = 0;
        foreach (var w in WarehouseController.All)
            if (w != null && w.Building != null && !w.Building.UnderConstruction) n += WarehouseBonusAt(r, w.Building.level);   // стройка места ещё не даёт
        return n;
    }

    /// <summary>У этого ресурса есть лимит. Марки гильдии - валюта рынка, их копить можно сколько угодно.</summary>
    public static bool Limited(StoreRes r) { return r != StoreRes.Marks; }

    /// <summary>Сколько всего может лежать в казне.</summary>
    public static int Cap(StoreRes r) { return Limited(r) ? HallCapAt(r, HallLevel) + WarehousesBonus(r) : int.MaxValue; }

    /// <summary>Сколько ещё влезет (int.MaxValue - лимиты сейчас не действуют).</summary>
    public static int Free(StoreRes r)
    {
        if (!Active) return int.MaxValue;
        var rm = ResourceManager.Instance;
        return Mathf.Max(0, Cap(r) - rm.Get(r));
    }

    /// <summary>Не больше want и не больше свободного места.</summary>
    public static int Room(StoreRes r, int want) { return Mathf.Max(0, Mathf.Min(want, Free(r))); }

    // ---------- Материалы Цитадели ----------

    public static int RefinedCapAt(RefinedKind k, int hallLevel) { return RefinedCap[(int)k, Lvl(hallLevel)]; }
    public static int RefinedCapOf(RefinedKind k) { return RefinedCapAt(k, HallLevel); }

    /// <summary>Сколько этого материала ещё влезет в Цитадель (все качества вместе).</summary>
    public static int HallRefinedFree(RefinedKind k)
    {
        if (!Active) return int.MaxValue;
        var st = TownHallStorage.Instance;
        int have = st != null ? st.Total(k) : 0;
        return Mathf.Max(0, RefinedCapOf(k) - have);
    }

    /// <summary>Сколько материала влезет в город: в Цитадель и в свободное место Хранилищ.</summary>
    public static int RefinedRoom(RefinedKind k)
    {
        int hall = HallRefinedFree(k);
        if (hall == int.MaxValue) return int.MaxValue;
        return hall + WarehouseController.TotalFree;
    }

    /// <summary>Сколько товара рынка ещё влезет (для проверки покупки).</summary>
    public static int RoomForGood(GoodKind g)
    {
        switch (g)
        {
            case GoodKind.Gold: return Free(StoreRes.Gold);
            case GoodKind.Wood: return Free(StoreRes.Wood);
            case GoodKind.Stone: return Free(StoreRes.Stone);
            case GoodKind.Food: return Free(StoreRes.Food);
            case GoodKind.Iron: return Free(StoreRes.Iron);
            case GoodKind.Oats: return Free(StoreRes.Oats);
            case GoodKind.Berries: return Free(StoreRes.Berries);
            case GoodKind.Herbs: return Free(StoreRes.Herbs);
            case GoodKind.Potions: return Free(StoreRes.Potions);
            case GoodKind.Parts: return Free(StoreRes.Parts);
            case GoodKind.Resin: return Free(StoreRes.Resin);
            case GoodKind.Tar: return Free(StoreRes.Tar);
            case GoodKind.Rubber: return Free(StoreRes.Rubber);
            case GoodKind.SlowPotions: return Free(StoreRes.SlowPotions);
            case GoodKind.Leaves: return Free(StoreRes.Leaves);
            case GoodKind.Planks: return RefinedRoom(RefinedKind.Planks);
            case GoodKind.Plates: return RefinedRoom(RefinedKind.StonePlates);
            case GoodKind.Steel: return RefinedRoom(RefinedKind.Steel);
            case GoodKind.Reinforced: return RefinedRoom(RefinedKind.ReinforcedPlates);
            case GoodKind.Beams: return RefinedRoom(RefinedKind.TarredBeams);
            case GoodKind.Monolith: return RefinedRoom(RefinedKind.Monolith);
            default: return int.MaxValue;
        }
    }

    // ---------- Сообщение «склад полон» ----------
    // Что пропало сверх лимита, копим и пишем одним сообщением раз в несколько секунд (иначе - спам каждый кадр)

    const float ReportEvery = 4f;
    static readonly Dictionary<string, int> lost = new Dictionary<string, int>();
    static float nextReport;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { lost.Clear(); nextReport = 0f; }

    public static void ReportLost(string what, int n)
    {
        if (n <= 0 || string.IsNullOrEmpty(what)) return;
        lost.TryGetValue(what, out int had);
        lost[what] = had + n;
    }

    /// <summary>Вызывает ResourceManager каждый кадр.</summary>
    public static void Flush()
    {
        if (lost.Count == 0 || Time.unscaledTime < nextReport) return;
        nextReport = Time.unscaledTime + ReportEvery;
        var parts = new List<string>();
        foreach (var kv in lost) parts.Add($"{kv.Key} {kv.Value}");
        lost.Clear();
        WarManager.Notify("Склад полон, лишнее пропало: " + string.Join(", ", parts) +
                          ". Улучшите Цитадель или постройте Хранилище", false);
    }
}
