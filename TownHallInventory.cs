using UnityEngine;
using System.Collections.Generic;
using System.Text;

public enum QualityTier
{
    Medium, // Среднее
    Good,   // Хорошее
    Best    // Наилучшее
}

public class TownHallStorage : MonoBehaviour
{
    public static TownHallStorage Instance { get; private set; }

    // Словари хранения материалов с атрибутом NonSerialized для Unity 6
    [System.NonSerialized]
    public Dictionary<QualityTier, int> planks = new Dictionary<QualityTier, int>()
    {
        { QualityTier.Medium, 0 },
        { QualityTier.Good, 0 },
        { QualityTier.Best, 0 }
    };

    [System.NonSerialized]
    public Dictionary<QualityTier, int> stonePlates = new Dictionary<QualityTier, int>()
    {
        { QualityTier.Medium, 0 },
        { QualityTier.Good, 0 },
        { QualityTier.Best, 0 }
    };

    // Сталь: делают в мастерской Ур.2 из железной руды
    [System.NonSerialized]
    public Dictionary<QualityTier, int> steel = new Dictionary<QualityTier, int>()
    {
        { QualityTier.Medium, 0 },
        { QualityTier.Good, 0 },
        { QualityTier.Best, 0 }
    };

    // Укреплённые плиты: мастерская Ур.3 из плиты, стали и камня
    [System.NonSerialized]
    public Dictionary<QualityTier, int> reinforced = new Dictionary<QualityTier, int>()
    {
        { QualityTier.Medium, 0 },
        { QualityTier.Good, 0 },
        { QualityTier.Best, 0 }
    };

    // Смоляной брус и монолитное основание: мастерская Ур.3 (из смоляной пропитки)
    [System.NonSerialized]
    public Dictionary<QualityTier, int> beams = new Dictionary<QualityTier, int>()
    {
        { QualityTier.Medium, 0 },
        { QualityTier.Good, 0 },
        { QualityTier.Best, 0 }
    };

    [System.NonSerialized]
    public Dictionary<QualityTier, int> monolith = new Dictionary<QualityTier, int>()
    {
        { QualityTier.Medium, 0 },
        { QualityTier.Good, 0 },
        { QualityTier.Best, 0 }
    };

    Dictionary<QualityTier, int> Dict(RefinedKind kind)
    {
        switch (kind)
        {
            case RefinedKind.Planks: return planks;
            case RefinedKind.StonePlates: return stonePlates;
            case RefinedKind.ReinforcedPlates: return reinforced;
            case RefinedKind.TarredBeams: return beams;
            case RefinedKind.Monolith: return monolith;
            default: return steel;
        }
    }

    public void AddSteel(QualityTier quality, int amount)
    {
        Add(RefinedKind.Steel, quality, amount);
        Debug.Log($"[Склад Цитадели] Добавлено стали ({quality}): +{amount}. Всего: {steel[quality]}");
    }

    // F9: лишнее сверх лимита срезается молча
    [System.NonSerialized] public bool cheatSilent;

    /// <summary>
    /// Положить материал в Цитадель. У каждого вида свой лимит (StorageLimits): что не влезло,
    /// уходит в свободное место Хранилищ, остальное пропадает (пишется сообщение).
    /// </summary>
    public void Add(RefinedKind kind, QualityTier quality, int amount)
    {
        if (amount <= 0) return;
        int free = StorageLimits.HallRefinedFree(kind);
        int here = Mathf.Min(amount, free);
        if (here > 0) Dict(kind)[quality] += here;
        int rest = amount - here;
        if (rest <= 0) return;

        int toWh = WarehouseController.StoreOverflow(kind, quality, rest);
        rest -= toWh;
        if (rest > 0 && !cheatSilent) StorageLimits.ReportLost(UpgradeRules.KindLabel(kind).ToLower(), rest);
    }

    /// <summary>Сколько всего этого материала в Цитадели (все качества).</summary>
    public int Total(RefinedKind kind)
    {
        int n = 0;
        foreach (var v in Dict(kind).Values) n += v;
        return n;
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void AddPlanks(QualityTier quality, int amount)
    {
        Add(RefinedKind.Planks, quality, amount);
        Debug.Log($"[Склад Цитадели] Добавлено досок ({quality}): +{amount}. Всего: {planks[quality]}");
    }

    public void AddStonePlates(QualityTier quality, int amount)
    {
        Add(RefinedKind.StonePlates, quality, amount);
        Debug.Log($"[Склад Цитадели] Добавлено плит ({quality}): +{amount}. Всего: {stonePlates[quality]}");
    }

    // Сколько материала этого вида и качества лежит на складе
    public int Get(RefinedKind kind, QualityTier quality)
    {
        var d = Dict(kind);
        return d.TryGetValue(quality, out int v) ? v : 0;
    }

    // Списать материал со склада. false - не хватает, ничего не списано.
    public bool TrySpend(RefinedKind kind, QualityTier quality, int amount)
    {
        if (amount <= 0) return true;
        var d = Dict(kind);
        if (!d.TryGetValue(quality, out int v) || v < amount) return false;
        if (Event.current != null) CityIncome.Skip();   // тратит игрок (кнопка) - не доход города
        d[quality] = v - amount;
        return true;
    }

    // Список для панели Цитадели: только то, что реально есть на складе (нули не показываем)
    public string BuildListText(out int lines)
    {
        StringBuilder sb = new StringBuilder();
        lines = 0;

        foreach (QualityTier q in System.Enum.GetValues(typeof(QualityTier)))
        {
            int n = planks[q];
            if (n <= 0) continue;
            sb.Append($" • Доски ({TierName(q).ToLower()}): {n} шт.\n");
            lines++;
        }

        foreach (QualityTier q in System.Enum.GetValues(typeof(QualityTier)))
        {
            int n = stonePlates[q];
            if (n <= 0) continue;
            sb.Append($" • Плиты ({TierName(q).ToLower()}): {n} шт.\n");
            lines++;
        }

        foreach (QualityTier q in System.Enum.GetValues(typeof(QualityTier)))
        {
            int n = steel[q];
            if (n <= 0) continue;
            sb.Append($" • Сталь ({TierName(q).ToLower()}): {n} шт.\n");
            lines++;
        }

        foreach (QualityTier q in System.Enum.GetValues(typeof(QualityTier)))
        {
            int n = reinforced[q];
            if (n <= 0) continue;
            sb.Append($" • Укреплённые плиты ({TierName(q).ToLower()}): {n} шт.\n");
            lines++;
        }

        foreach (QualityTier q in System.Enum.GetValues(typeof(QualityTier)))
        {
            int n = beams[q];
            if (n <= 0) continue;
            sb.Append($" • <color=#D8A060>Смоляной брус ({TierName(q).ToLower()}): {n} шт.</color>\n");
            lines++;
        }

        foreach (QualityTier q in System.Enum.GetValues(typeof(QualityTier)))
        {
            int n = monolith[q];
            if (n <= 0) continue;
            sb.Append($" • <color=#C0B0A0>Монолитное основание ({TierName(q).ToLower()}): {n} шт.</color>\n");
            lines++;
        }

        var rm = ResourceManager.Instance;

        // Смола (Лесопилка Ур.3) и что из неё делают
        if (rm != null && rm.resin > 0) { sb.Append($" • <color=#E0A040>Смола: {rm.resin}</color>\n"); lines++; }
        if (rm != null && rm.tar > 0) { sb.Append($" • <color=#C08040>Смоляная пропитка: {rm.tar}</color>\n"); lines++; }
        if (rm != null && rm.rubber > 0) { sb.Append($" • <color=#A0A0B0>Резина: {rm.rubber}</color>\n"); lines++; }
        if (rm != null && rm.slowPotions > 0) { sb.Append($" • <color=#80C0FF>Зелья замедления: {rm.slowPotions}</color>\n"); lines++; }
        int kits = EngineerYard.HallKitCount;
        if (kits > 0) { sb.Append($" • <color=#FFC870>Модули для построек: {kits}</color>\n"); lines++; }

        // Железная руда лежит в казне (ResourceManager), но сверху не показывается - только здесь
        int ore = rm != null ? rm.iron : 0;
        if (ore > 0)
        {
            sb.Append($" • <color=#B0C4DE>Железная руда: {ore}</color>\n");
            lines++;
        }

        // Овёс (ферма Ур.3) - тоже только здесь
        int oats = rm != null ? rm.oats : 0;
        if (oats > 0)
        {
            sb.Append($" • <color=#E8D890>Овёс: {oats}</color>\n");
            lines++;
        }

        // Ягоды (Станция собирателей) - тоже только здесь
        int berries = rm != null ? rm.berries : 0;
        if (berries > 0)
        {
            sb.Append($" • <color=#F0A0C8>Ягоды: {berries}</color>\n");
            lines++;
        }

        // Травы и зелья (путь Магии) - только если они есть
        int herbs = rm != null ? rm.herbs : 0;
        if (herbs > 0)
        {
            sb.Append($" • <color=#90E060>Травы: {herbs}</color>\n");
            lines++;
        }
        int potionsAll = PotionHouse.TotalPotions;   // Цитадель + Хранилища
        if (potionsAll > 0)
        {
            sb.Append($" • <color=#B0E080>Зелья отравления: {potionsAll}</color>\n");
            lines++;
        }

        // Детали (путь Инженерии): отливает Литейная, тратит Инженерный двор
        int parts = rm != null ? rm.parts : 0;
        if (parts > 0)
        {
            sb.Append($" • <color=#FFC870>Детали: {parts}</color>\n");
            lines++;
        }

        // Похлёбка хранится на Кухнях - показываем общий запас
        int pottage = KitchenController.PortionsAvailable;
        if (pottage > 0)
        {
            sb.Append($" • <color=#FFB080>Похлёбка (на кухнях): {pottage} порц.</color>\n");
            lines++;
        }

        if (lines == 0)
        {
            sb.Append("<i>Склад пуст. Доски, плиты и сталь делают в мастерской.</i>");
            lines = 2;
        }
        return sb.ToString();
    }

    // Плитки с картинками для панели Цитадели: материалы по качеству, сырьё, которого нет в верхней строке,
    // зелья, детали, модули, ловушки и заслоны. Нули не показываем.
    public List<ItemIcons.Entry> BuildEntries()
    {
        var list = new List<ItemIcons.Entry>();
        bool lim = StorageLimits.Active;
        foreach (RefinedKind k in System.Enum.GetValues(typeof(RefinedKind)))
        {
            int total = Total(k), cap = StorageLimits.RefinedCapOf(k);
            foreach (QualityTier q in System.Enum.GetValues(typeof(QualityTier)))
            {
                int n = Get(k, q);
                if (n <= 0) continue;
                var e = new ItemIcons.Entry(ItemIcons.Refined(k), UpgradeRules.KindLabel(k), n, (int)q);
                if (lim) { e.limitText = $"всего в Цитадели {total} из {cap}"; e.full = total >= cap; }
                list.Add(e);
            }
        }

        var rm = ResourceManager.Instance;
        if (rm != null)
        {
            Add(list, "resin", "Смола", rm.resin, StoreRes.Resin, lim);
            Add(list, "tar", "Смоляная пропитка", rm.tar, StoreRes.Tar, lim);
            Add(list, "rubber", "Резина", rm.rubber, StoreRes.Rubber, lim);
            Add(list, "iron", "Железная руда", rm.iron, StoreRes.Iron, lim);
            Add(list, "oats", "Овёс", rm.oats, StoreRes.Oats, lim);
            Add(list, "berries", "Ягоды", rm.berries, StoreRes.Berries, lim);
            Add(list, "herbs", "Травы", rm.herbs, StoreRes.Herbs, lim);
            Add(list, "leaves", "Листва", rm.leaves, StoreRes.Leaves, lim);
            Add(list, "parts", "Детали", rm.parts, StoreRes.Parts, lim);
            Add(list, "slow_potion", "Зелья замедления", rm.slowPotions, StoreRes.SlowPotions, lim);
        }
        int potHall = rm != null ? rm.potions : 0;
        int potAll = PotionHouse.TotalPotions;
        if (potAll > 0)
        {
            var e = new ItemIcons.Entry("potion", "Зелья отравления (Цитадель и Хранилища)", potAll);
            if (lim) { e.limitText = $"в Цитадели {potHall} из {StorageLimits.Cap(StoreRes.Potions)}, остальное — в Хранилищах"; e.full = potHall >= StorageLimits.Cap(StoreRes.Potions); }
            list.Add(e);
        }
        int pottage = KitchenController.PortionsAvailable;
        if (pottage > 0) list.Add(new ItemIcons.Entry("pottage", "Похлёбка (на кухнях)", pottage, -1, pottage + " порц."));

        // Модули, ядра хаоса и снаряды - по виду
        var kits = new Dictionary<ModuleKind, int>();
        foreach (var kit in EngineerYard.HallStock) { kits.TryGetValue(kit.kind, out int c); kits[kit.kind] = c + 1; }
        foreach (var kv in kits)
            list.Add(new ItemIcons.Entry(ItemIcons.Module(kv.Key), TowerModules.Name(kv.Key), kv.Value));

        int traps = TrapStock.Count(false), combat = TrapStock.Count(true);
        Add(list, "trap", "Ловушки", traps);
        Add(list, "trap_combat", "Боевые ловушки", combat);

        var posts = new Dictionary<PostKind, int>();
        foreach (var g in FieldGear.All()) { posts.TryGetValue(g.kind, out int c); posts[g.kind] = c + 1; }
        foreach (var kv in posts) list.Add(new ItemIcons.Entry(ItemIcons.Post(kv.Key), FieldPost.Specs[(int)kv.Key].name, kv.Value));
        return list;
    }

    static void Add(List<ItemIcons.Entry> list, string icon, string name, int n)
    {
        if (n > 0) list.Add(new ItemIcons.Entry(icon, name, n));
    }

    // С лимитом казны: в подсказке «из N», упёрлись - число красное
    static void Add(List<ItemIcons.Entry> list, string icon, string name, int n, StoreRes r, bool lim)
    {
        if (n <= 0) return;
        var e = new ItemIcons.Entry(icon, name, n);
        if (lim) { int cap = StorageLimits.Cap(r); e.limitText = $"лимит {cap}"; e.full = n >= cap; }
        list.Add(e);
    }

    /// <summary>Что значит качество материалов (подсказка final-8: тестеры не понимали, зачем «хорошее» и «наилучшее»).</summary>
    public const string QualityHelp =
        "Качество материалов: среднее, хорошее (зелёная метка) и наилучшее (золотая). " +
        "Улучшить здание до Ур.2 и построить второе такое же можно из материалов любого качества, а до Ур.3 и выше — только не хуже «хорошего». " +
        "Хорошее и наилучшее чаще выходят в Мастерской высокого уровня: Ур.1 — 15% хорошего; Ур.2 — 40% хорошего и 10% наилучшего; Ур.3 — 50% и 30%. " +
        "Тратится всегда сначала самое простое подходящее, поэтому лучшие материалы сами копятся для Ур.3.";

    public static string TierName(QualityTier quality)
    {
        switch (quality)
        {
            case QualityTier.Medium: return "Среднее";
            case QualityTier.Good: return "Хорошее";
            case QualityTier.Best: return "Наилучшее";
            default: return "";
        }
    }

    public string GetQualityName(QualityTier quality)
    {
        return TierName(quality);
    }
}
