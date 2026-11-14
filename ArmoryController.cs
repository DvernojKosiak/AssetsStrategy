using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class WeaponRecipe
{
    public string recipeName = "Сковать меч";
    public WeaponKind kind = WeaponKind.Sword;
    public bool isShield = false;  // true - получается щит (kind игнорируется)
    public bool isBody = false;    // true - броня или плащ (bodyKind), см. CitizenAgent_Gear
    public BodyKind bodyKind = BodyKind.Armor;
    public WarSchool school = WarSchool.None;   // рецепт только для этой ветки Военного дела
    public int steel = 4;          // сталь (любого качества)
    public int planks = 0;         // доски (любого качества)
    public int leaves = 0;         // листва из казны (Лесопилка Ур.4)
    public int gold = 0;           // золото из казны
    public float craftTime = 18f;

    public string CostText
    {
        get
        {
            var parts = new List<string>();
            if (steel > 0) parts.Add(steel + " стали");
            if (planks > 0) parts.Add(planks + " досок");
            if (leaves > 0) parts.Add(leaves + " листвы");
            if (gold > 0) parts.Add(gold + " зол");
            return parts.Count > 0 ? string.Join(" + ", parts) : "даром";
        }
    }
}

/// <summary>
/// Оружейная. Рабочие здания - оружейники.
///  1. Игрок заказывает рецепт: сталь (и доски) списываются со склада Цитадели / Хранилищ,
///     рабочий несёт их в Оружейную (как в мастерской, см. Logistics).
///  2. Идёт ковка. Качество выпадает случайно, чем выше уровень Оружейной - тем лучше.
///  3. Готовые мечи, луки и щиты лежат на общем складе Оружейной (вместимость 12 / 24 / 40).
///  4. Предметы можно улучшать за золото (Усиленный, Позолоченный, Золотой;
///     для 2-го и 3-го улучшения нужна Оружейная Ур.2 / Ур.3) и чинить за золото.
///     Броню усиливают сталью, камнем и золотом (Усиленная, Закалённая, Золочёная), плащи не усиливают.
///  5. Из казармы предметы выдают бойцам. Боец погиб - предметы погибли.
///     Бойца уволили - предметы возвращаются сюда.
/// Добавляется на здание в BuildingPlacer.
/// </summary>
public class ArmoryController : MonoBehaviour, ISavedBuilding
{
    public const string TypeName = "Оружейная";

    public static readonly List<ArmoryController> All = new List<ArmoryController>();

    static readonly int[] CapacityByLevel = { 12, 24, 40 };

    public List<WeaponRecipe> recipes = new List<WeaponRecipe>
    {
        new WeaponRecipe { recipeName = "Сковать меч", kind = WeaponKind.Sword, steel = 4, planks = 0, craftTime = 18f },
        new WeaponRecipe { recipeName = "Сделать лук", kind = WeaponKind.Bow, steel = 2, planks = 4, craftTime = 14f },
        new WeaponRecipe { recipeName = "Сделать щит", isShield = true, steel = 3, planks = 6, craftTime = 16f },
    };

    // Рецепты веток Военного дела (не сериализуются: ветку могут сменить на ходу - проверяем каждый кадр)
    static readonly WeaponRecipe[] BranchRecipes =
    {
        new WeaponRecipe { recipeName = "Сделать броню", isBody = true, bodyKind = BodyKind.Armor, school = WarSchool.Heavy, steel = 6, planks = 3, craftTime = 24f },
        new WeaponRecipe { recipeName = "Сшить плащ", isBody = true, bodyKind = BodyKind.Cloak, school = WarSchool.Camo, steel = 0, planks = 0, leaves = 12, gold = 15, craftTime = 14f },
    };

    /// <summary>Что можно заказать сейчас: оружие и щиты + броня (Тяжёлая стратегия) или плащ (Мастер маскировки).</summary>
    public List<WeaponRecipe> VisibleRecipes()
    {
        var res = new List<WeaponRecipe>(recipes);
        foreach (var r in BranchRecipes)
            if (r.school != WarSchool.None && DevelopmentPath.WarAllows(r.school)) res.Add(r);
        return res;
    }

    enum Stage { Idle, Fetching, Crafting }

    public BuildingInstance Building { get; private set; }
    int Lvl => Building != null ? Mathf.Clamp(Building.level, 1, 3) - 1 : 0;

    public int Capacity => CapacityByLevel[Lvl];
    public bool Works => Building != null && Building.isActiveAndEnabled && !Building.IsRuined;

    readonly List<Weapon> stock = new List<Weapon>();
    readonly List<Shield> shields = new List<Shield>();
    readonly List<BodyGear> bodies = new List<BodyGear>();   // броня и плащи
    public IReadOnlyList<Weapon> Stock => stock;
    public IReadOnlyList<Shield> Shields => shields;
    public IReadOnlyList<BodyGear> Bodies => bodies;
    public int Count => stock.Count;
    public int ItemCount => stock.Count + shields.Count + bodies.Count;
    public int CraftedTotal { get; private set; }

    // Обновление 23 (трофеи): вещь уносят из этой Оружейной
    public bool RemoveItem(Weapon w) { return w != null && stock.Remove(w); }
    public bool RemoveItem(Shield s) { return s != null && shields.Remove(s); }
    public bool RemoveItem(BodyGear g) { return g != null && bodies.Remove(g); }

    // ---------- Сохранение: склад оружия, щитов, брони и плащей ----------

    public void SaveState(SaveBag bag)
    {
        bag.SetList("arm.w", SaveBag.PackAll(stock, SaveBag.Pack));
        bag.SetList("arm.s", SaveBag.PackAll(shields, SaveBag.Pack));
        bag.SetList("arm.b", SaveBag.PackAll(bodies, SaveBag.Pack));
        bag.Set("arm.made", CraftedTotal);
        if (Queue.Count > 0) bag.Set("arm.queue", Queue.Save(AllRecipes()));
        bag.Set("arm.at", Queue.Count > 0 || stage != Stage.Idle ? BuildingInstance.RuinClockUtc.ToString("o") : null);
        int job = stage != Stage.Idle ? JobIndex() : -1;
        if (job >= 0)
        {
            bag.Set("arm.job", job);
            bag.Set("arm.left", stage == Stage.Crafting ? Mathf.Max(0.05f, craftTimer) : current.craftTime);
        }
    }

    public void LoadState(SaveBag bag)
    {
        stock.Clear(); shields.Clear(); bodies.Clear();
        foreach (var a in bag.List("arm.w")) { var w = SaveBag.UnpackWeapon(a); if (w != null) stock.Add(w); }
        foreach (var a in bag.List("arm.s")) { var s = SaveBag.UnpackShield(a); if (s != null) shields.Add(s); }
        foreach (var a in bag.List("arm.b")) { var g = SaveBag.UnpackBody(a); if (g != null) bodies.Add(g); }
        CraftedTotal = bag.Int("arm.made");
        Queue.Load(bag.Get("arm.queue"), AllRecipes());
        // Заказ в работе (обновление final-1)
        int ri = bag.Int("arm.job", -1);
        WeaponRecipe r = ri >= 100 ? (ri - 100 < BranchRecipes.Length ? BranchRecipes[ri - 100] : null)
                                   : (ri >= 0 && ri < recipes.Count ? recipes[ri] : null);
        double away = JobSave.Away;
        string at = bag.Get("arm.at");
        if (!string.IsNullOrEmpty(at) && System.DateTime.TryParse(at, null, System.Globalization.DateTimeStyles.RoundtripKind, out System.DateTime t))
            away = System.Math.Min(14 * 86400.0, System.Math.Max(0.0, (BuildingInstance.RuinClockUtc - t.ToUniversalTime()).TotalSeconds));
        if (r != null)
        {
            float left = bag.Float("arm.left", r.craftTime);
            current = r;
            if (away < left)
            {
                craftTimer = Mathf.Max(0.05f, left - (float)away);
                stage = Stage.Crafting;
                return;
            }
            stage = Stage.Crafting;
            FinishCraft();   // доковали, пока игрока не было
            away -= left;
        }
        // Обновление 12: очередь Оружейной идёт и без игрока
        if (Queue.Count > 0 && away > 1.0)
        {
            double rest = away;
            JobSave.AfterLoad(() => OfflineQueue(rest));
        }
    }

    void OfflineQueue(double rest)
    {
        if (this == null || !Works || IsBusy) return;
        while (Queue.Count > 0 && rest > 0.0)
        {
            WeaponRecipe r = Queue.Items[0];
            if (r == null || ItemCount >= Capacity || !CanAfford(r)) break;
            if (r.school != WarSchool.None && !DevelopmentPath.WarAllows(r.school)) break;
            var st = TownHallStorage.Instance;
            var spent = new List<UpgradeRules.Spent>();
            if (r.steel > 0) UpgradeRules.SpendAtLeast(st, RefinedKind.Steel, QualityTier.Medium, r.steel, spent);
            if (r.planks > 0) UpgradeRules.SpendAtLeast(st, RefinedKind.Planks, QualityTier.Medium, r.planks, spent);
            var rm = ResourceManager.Instance;
            if (r.leaves > 0 && rm != null) rm.SpendLeaves(r.leaves);
            if (r.gold > 0 && rm != null) rm.Spend(r.gold, 0, 0, 0);
            Queue.Items.RemoveAt(0);
            current = r;
            stage = Stage.Crafting;
            if (rest >= r.craftTime) { rest -= r.craftTime; FinishCraft(); continue; }
            craftTimer = (float)(r.craftTime - rest);
            break;
        }
    }

    int JobIndex()
    {
        if (current == null) return -1;
        int i = recipes.IndexOf(current);
        if (i >= 0) return i;
        i = System.Array.IndexOf(BranchRecipes, current);
        return i >= 0 ? 100 + i : -1;
    }

    Stage stage = Stage.Idle;
    WeaponRecipe current;
    float craftTimer;
    List<UpgradeRules.Spent> spentLog;
    int spentLeaves, spentGold;   // листва и золото заказа (вернуть, если рейс сорван)

    public bool IsBusy => stage != Stage.Idle;
    public float CraftProgress => stage == Stage.Crafting && current != null && current.craftTime > 0f
        ? 1f - craftTimer / current.craftTime : 0f;

    string StatusLine
    {
        get
        {
            switch (stage)
            {
                case Stage.Fetching: return "Несут материалы из Цитадели...";
                case Stage.Crafting: return $"Ковка: <b>{(current != null ? current.recipeName : "")}</b> ({CraftProgress * 100f:F0}%)";
                default: return ItemCount >= Capacity ? "<color=#FFB060>Склад полон</color>" : "Оружейная свободна";
            }
        }
    }

    void Awake() { Building = GetComponent<BuildingInstance>(); }
    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    // ---------- Очередь заказов (final-8) ----------
    public readonly CraftQueue<WeaponRecipe> Queue = new CraftQueue<WeaponRecipe>();

    List<WeaponRecipe> AllRecipes()   // для сохранения очереди: обычные + рецепты веток, порядок постоянный
    {
        var all = new List<WeaponRecipe>(recipes);
        all.AddRange(BranchRecipes);
        return all;
    }

    public void Order(WeaponRecipe r)
    {
        if (!IsBusy && Queue.Count == 0) StartCraft(r);
        else Queue.Add(r, TypeName, r.recipeName);
    }

    string QueueProblem(WeaponRecipe r)
    {
        if (r.school != WarSchool.None && !DevelopmentPath.WarAllows(r.school)) return "нужна другая ветка Военного дела";
        return CraftProblem(r, false);
    }

    void Update()
    {
        if (Building == null) Building = GetComponent<BuildingInstance>();
        Queue.Tick(IsBusy, QueueProblem, r => { StartCraft(r); return IsBusy; }, TypeName);
        if (stage != Stage.Crafting) return;
        if (Building != null && !Building.isProducing) return;

        // Голодные мастера и дефицит замедляют ковку
        craftTimer -= Time.deltaTime * (Building != null ? Building.SpeedMultiplier * Building.CraftCrewFactor * Building.HealthFactor * Building.CraftBoost : 1f);
        if (craftTimer > 0f) return;
        FinishCraft();
    }

    // Здание снесли - предметы переезжают в другие Оружейные, что не влезло - пропадает
    void OnDestroy()
    {
        int lost = 0;
        var items = new List<Weapon>(stock);
        var shs = new List<Shield>(shields);
        var bds = new List<BodyGear>(bodies);
        stock.Clear();
        shields.Clear();
        bodies.Clear();
        All.Remove(this);
        foreach (var w in items)
            if (!Stash(w)) lost++;
        foreach (var s in shs)
            if (!StashShield(s)) lost++;
        foreach (var g in bds)
            if (!StashBody(g)) lost++;
        if (lost > 0) WarManager.Notify($"Оружейную снесли: потеряно предметов — {lost}", false);
    }

    // ---------- Ковка ----------

    public bool CanAfford(WeaponRecipe r)
    {
        var st = TownHallStorage.Instance;
        if (st == null || r == null) return false;
        if (UpgradeRules.CountAtLeast(st, RefinedKind.Steel, QualityTier.Medium) < r.steel) return false;
        if (r.planks > 0 && UpgradeRules.CountAtLeast(st, RefinedKind.Planks, QualityTier.Medium) < r.planks) return false;
        var rm = ResourceManager.Instance;
        if ((r.leaves > 0 || r.gold > 0) && rm == null) return false;
        if (r.leaves > 0 && rm.leaves < r.leaves) return false;
        if (r.gold > 0 && rm.gold < r.gold) return false;
        return true;
    }

    public bool CanStart(WeaponRecipe r)
    {
        return Works && Building.isProducing && !IsBusy && ItemCount < Capacity && CanAfford(r);
    }

    public void StartCraft(WeaponRecipe r)
    {
        if (!CanStart(r)) return;
        var st = TownHallStorage.Instance;

        // Материалы списываются сразу: сначала самого низкого качества, склад Цитадели, потом Хранилища
        var spent = new List<UpgradeRules.Spent>();
        if (r.steel > 0) UpgradeRules.SpendAtLeast(st, RefinedKind.Steel, QualityTier.Medium, r.steel, spent);
        if (r.planks > 0) UpgradeRules.SpendAtLeast(st, RefinedKind.Planks, QualityTier.Medium, r.planks, spent);
        var rm = ResourceManager.Instance;
        spentLeaves = r.leaves > 0 && rm != null && rm.SpendLeaves(r.leaves) ? r.leaves : 0;
        spentGold = r.gold > 0 && rm != null && rm.Spend(r.gold, 0, 0, 0) ? r.gold : 0;

        current = r;
        spentLog = spent;
        stage = Stage.Fetching;

        var pm = PopulationManager.Instance;
        BuildingInstance pickup = pm != null ? pm.Hall : null;
        foreach (var sp in spent)
            if (sp.source != null) { pickup = sp.source; break; }   // брали в Хранилище - житель идёт туда

        Logistics.Request(new HaulJob
        {
            from = pickup,
            to = Building,
            prefer = Building,
            onDeliver = OnMaterialsDelivered,
            onAbort = loaded => OnMaterialsAborted()
        });
    }

    void OnMaterialsDelivered()
    {
        if (current == null) return;
        craftTimer = current.craftTime;
        stage = Stage.Crafting;
    }

    // Рейс сорван - материалы возвращаются на склад
    void OnMaterialsAborted()
    {
        UpgradeRules.RefundSpent(spentLog);
        spentLog = null;
        var rm = ResourceManager.Instance;
        if (rm != null)
        {
            rm.AddLeaves(spentLeaves);
            if (spentGold > 0) rm.Add(spentGold, 0, 0, 0);
        }
        spentLeaves = spentGold = 0;
        current = null;
        stage = Stage.Idle;
    }

    void FinishCraft()
    {
        WeaponRecipe r = current;
        current = null;
        spentLog = null;
        spentLeaves = spentGold = 0;
        stage = Stage.Idle;
        if (r == null) return;

        if (r.isBody)
        {
            BodyGear g = new BodyGear(r.bodyKind, RollQuality());
            bodies.Add(g);
            CraftedTotal++;
            WarManager.Notify($"Оружейная: готово — {g.Name}", true);
            return;
        }

        if (r.isShield)
        {
            Shield sh = new Shield(RollQuality());
            shields.Add(sh);
            CraftedTotal++;
            WarManager.Notify($"Оружейная: готово — {sh.Name}", true);
            return;
        }

        Weapon w = new Weapon(r.kind, RollQuality());
        stock.Add(w);
        CraftedTotal++;
        WarManager.Notify($"Оружейная: готово — {w.Name}", true);
    }

    // Шансы качества по уровню здания (как в мастерской)
    QualityTier RollQuality()
    {
        int lvl = Building != null ? Building.level : 1;
        float roll = Random.Range(0f, 100f);
        if (lvl <= 1) return roll < 85f ? QualityTier.Medium : QualityTier.Good;
        if (lvl == 2) return roll < 50f ? QualityTier.Medium : (roll < 90f ? QualityTier.Good : QualityTier.Best);
        return roll < 20f ? QualityTier.Medium : (roll < 70f ? QualityTier.Good : QualityTier.Best);
    }

    // ---------- Улучшение и починка за золото: оружие ----------

    public bool CanUpgradeWeapon(Weapon w)
    {
        if (!Works || w == null || !w.CanUpgrade) return false;
        if (Building.level < w.upgrade + 1) return false;   // 2-е улучшение - Ур.2, 3-е - Ур.3
        var rm = ResourceManager.Instance;
        return rm != null && rm.gold >= Weapon.UpgradeCost(w.upgrade + 1);
    }

    public bool TryUpgradeWeapon(Weapon w)
    {
        if (!CanUpgradeWeapon(w)) return false;
        int cost = Weapon.UpgradeCost(w.upgrade + 1);
        if (!ResourceManager.Instance.Spend(cost, 0, 0, 0)) return false;
        w.ApplyUpgrade();
        WarManager.Notify($"Оружейная: оружие улучшено — {w.Name}", true);
        return true;
    }

    public bool CanRepairWeapon(Weapon w)
    {
        if (!Works || w == null) return false;
        int cost = w.RepairCost;
        var rm = ResourceManager.Instance;
        return cost > 0 && rm != null && rm.gold >= cost;
    }

    public bool TryRepairWeapon(Weapon w)
    {
        if (!CanRepairWeapon(w)) return false;
        if (!ResourceManager.Instance.Spend(w.RepairCost, 0, 0, 0)) return false;
        w.Repair();
        return true;
    }

    // ---------- Улучшение и починка за золото: щиты ----------

    public bool CanUpgradeShield(Shield s)
    {
        if (!Works || s == null || !s.CanUpgrade) return false;
        if (Building.level < s.upgrade + 1) return false;
        var rm = ResourceManager.Instance;
        return rm != null && rm.gold >= Shield.UpgradeCost(s.upgrade + 1);
    }

    public bool TryUpgradeShield(Shield s)
    {
        if (!CanUpgradeShield(s)) return false;
        int cost = Shield.UpgradeCost(s.upgrade + 1);
        if (!ResourceManager.Instance.Spend(cost, 0, 0, 0)) return false;
        s.ApplyUpgrade();
        WarManager.Notify($"Оружейная: щит улучшен — {s.Name}", true);
        return true;
    }

    public bool CanRepairShield(Shield s)
    {
        if (!Works || s == null) return false;
        int cost = s.RepairCost;
        var rm = ResourceManager.Instance;
        return cost > 0 && rm != null && rm.gold >= cost;
    }

    public bool TryRepairShield(Shield s)
    {
        if (!CanRepairShield(s)) return false;
        if (!ResourceManager.Instance.Spend(s.RepairCost, 0, 0, 0)) return false;
        s.Repair();
        return true;
    }

    // ---------- Усиление брони: сталь + камень + золото ----------

    /// <summary>Почему нельзя усилить броню ("" - можно).</summary>
    public string BodyUpgradeProblem(BodyGear g)
    {
        if (!Works || g == null) return "Оружейная недоступна";
        if (!g.IsArmor) return "Плащ не усиливают";
        if (!g.CanUpgrade) return "Усилено до предела";
        int next = g.upgrade + 1;
        if (Building.level < next) return $"Нужна Оружейная Ур. {next}";
        var st = TownHallStorage.Instance;
        var rm = ResourceManager.Instance;
        if (st == null || rm == null) return "Склад недоступен";
        var miss = new List<string>();
        int steel = UpgradeRules.CountAtLeast(st, RefinedKind.Steel, QualityTier.Medium);
        if (steel < BodyGear.UpgradeSteelCost(next)) miss.Add($"стали {steel}/{BodyGear.UpgradeSteelCost(next)}");
        if (rm.stone < BodyGear.UpgradeStoneCost(next)) miss.Add($"камня {rm.stone}/{BodyGear.UpgradeStoneCost(next)}");
        if (rm.gold < BodyGear.UpgradeGoldCost(next)) miss.Add($"золота {rm.gold}/{BodyGear.UpgradeGoldCost(next)}");
        return miss.Count > 0 ? "Не хватает: " + string.Join(", ", miss) : "";
    }

    public bool CanUpgradeBody(BodyGear g) { return BodyUpgradeProblem(g) == ""; }

    public bool TryUpgradeBody(BodyGear g)
    {
        if (!CanUpgradeBody(g)) return false;
        int next = g.upgrade + 1;
        var rm = ResourceManager.Instance;
        if (!rm.Spend(BodyGear.UpgradeGoldCost(next), 0, BodyGear.UpgradeStoneCost(next), 0)) return false;
        var spent = new List<UpgradeRules.Spent>();
        UpgradeRules.SpendAtLeast(TownHallStorage.Instance, RefinedKind.Steel, QualityTier.Medium, BodyGear.UpgradeSteelCost(next), spent);
        g.ApplyUpgrade();
        WarManager.Notify($"Оружейная: броня усилена — {g.Name}", true);
        return true;
    }

    // ---------- Починка за золото: броня и плащи ----------

    public bool CanRepairBody(BodyGear g)
    {
        if (!Works || g == null) return false;
        int cost = g.RepairCost;
        var rm = ResourceManager.Instance;
        return cost > 0 && rm != null && rm.gold >= cost;
    }

    public bool TryRepairBody(BodyGear g)
    {
        if (!CanRepairBody(g)) return false;
        if (!ResourceManager.Instance.Spend(g.RepairCost, 0, 0, 0)) return false;
        g.Repair();
        return true;
    }

    // ---------- Переплавка: освободить место на складе ----------
    // Лишний меч, лук, щит, броню или плащ (часто трофеи из набегов) можно переплавить:
    // предмет исчезает, а в Цитадель возвращается совсем немного - 1 сталь или 1 доска (плащ - 2 листвы),
    // за каждое улучшение предмета ещё MeltGoldPerUpgrade золота. Эффекты (яд, салют, ядро хаоса) пропадают.
    // Нажать дважды: первое нажатие - «Точно?», второе - переплавить (чтобы не расплавить нужное случайно).

    public const int MeltGoldPerUpgrade = 5;
    const float MeltConfirmSeconds = 3f;
    object meltArmed;        // предмет, на котором нажали «Переплавить» один раз
    float meltArmedUntil;

    /// <summary>Что вернётся за предмет: материал, сколько, листва, золото.</summary>
    public static void MeltYield(object item, out RefinedKind kind, out int amount, out int leaves, out int gold)
    {
        kind = RefinedKind.Steel; amount = 0; leaves = 0; gold = 0;
        if (item is Weapon w)
        {
            kind = w.kind == WeaponKind.Bow ? RefinedKind.Planks : RefinedKind.Steel;
            amount = 1;
            gold = w.upgrade * MeltGoldPerUpgrade;
        }
        else if (item is Shield sh)
        {
            kind = RefinedKind.Planks;
            amount = 1;
            gold = sh.upgrade * MeltGoldPerUpgrade;
        }
        else if (item is BodyGear g)
        {
            if (g.IsArmor) { kind = RefinedKind.Steel; amount = 1; gold = g.upgrade * MeltGoldPerUpgrade; }
            else leaves = 2;
        }
    }

    public static string MeltYieldText(object item)
    {
        MeltYield(item, out RefinedKind kind, out int amount, out int leaves, out int gold);
        var parts = new List<string>();
        if (amount > 0) parts.Add(amount + " " + (kind == RefinedKind.Planks ? "доска" : "сталь"));
        if (leaves > 0) parts.Add(leaves + " листвы");
        if (gold > 0) parts.Add(gold + " зол");
        return parts.Count > 0 ? string.Join(" + ", parts) : "ничего";
    }

    /// <summary>Почему нельзя переплавить ("" - можно).</summary>
    public string MeltProblem(object item)
    {
        if (!Works) return "Оружейная разрушена";
        if (item is Weapon w && (poisonPending.Contains(w) || slowPending.Contains(w) || salutePending.Contains(w))) return "К этому луку несут эффект — подождите";
        return "";
    }

    /// <summary>Переплавить предмет со склада этой Оружейной.</summary>
    public bool Melt(object item)
    {
        if (item == null || MeltProblem(item) != "") return false;
        string name;
        if (item is Weapon w) { if (!stock.Remove(w)) return false; name = w.Name; }
        else if (item is Shield sh) { if (!shields.Remove(sh)) return false; name = sh.Name; }
        else if (item is BodyGear g) { if (!bodies.Remove(g)) return false; name = g.Name; }
        else return false;

        MeltYield(item, out RefinedKind kind, out int amount, out int leaves, out int gold);
        if (amount > 0 && TownHallStorage.Instance != null) TownHallStorage.Instance.Add(kind, QualityTier.Medium, amount);
        var rm = ResourceManager.Instance;
        if (rm != null)
        {
            if (leaves > 0) rm.AddLeaves(leaves);
            if (gold > 0) rm.Add(gold, 0, 0, 0);
        }
        if (effectItem == item) effectItem = null;
        Sfx.At("demolish", Building != null ? Building.Center : (Vector2)transform.position);
        WarManager.Notify($"Оружейная: переплавлено — {name} (+{MeltYieldText(item)})", true);
        return true;
    }

    // Кнопка «Переплавить» в строке склада: первое нажатие - «Точно?», второе - переплавить
    bool MeltButton(Rect r, object item)
    {
        bool armed = meltArmed == item && Time.unscaledTime < meltArmedUntil;
        string problem = MeltProblem(item);
        string tip = problem != "" ? problem
            : armed ? "Нажмите ещё раз — предмет исчезнет, в Цитадель вернётся: " + MeltYieldText(item)
            : "Переплавить, чтобы освободить место. Вернётся совсем немного: " + MeltYieldText(item) + ".\nЭффекты на предмете пропадут. Нажать два раза.";
        GUIStyle st = new GUIStyle(UiKit.ButtonStyle(armed ? UiKit.Kind.Danger : UiKit.Kind.Ghost)) { fontSize = 11, padding = new RectOffset(3, 3, 1, 1), wordWrap = false, clipping = TextClipping.Clip };
        GUI.enabled = problem == "";
        bool pressed = LocGUI.Button(r, armed ? "Точно?" : "Переплавить", st);
        GUI.enabled = true;
        UiKit.Tip(r, tip);
        if (!pressed) return false;
        if (armed) { meltArmed = null; return true; }
        meltArmed = item;
        meltArmedUntil = Time.unscaledTime + MeltConfirmSeconds;
        return false;
    }

    // ---------- Склад (общие функции для казармы и бойцов) ----------

    public struct StockEntry { public ArmoryController armory; public Weapon weapon; }
    public struct ShieldEntry { public ArmoryController armory; public Shield shield; }
    public struct BodyEntry { public ArmoryController armory; public BodyGear gear; }

    /// <summary>Броня и плащи во всех целых Оружейных (броня первой, внутри - лучшее первым).</summary>
    public static List<BodyEntry> AvailableBodies()
    {
        var res = new List<BodyEntry>();
        foreach (var a in All)
        {
            if (a == null || !a.Works) continue;
            foreach (var g in a.bodies)
                res.Add(new BodyEntry { armory = a, gear = g });
        }
        res.Sort((p, q) => p.gear.kind != q.gear.kind ? p.gear.kind.CompareTo(q.gear.kind) : q.gear.Score.CompareTo(p.gear.Score));
        return res;
    }

    public static int TotalBodies
    {
        get
        {
            int n = 0;
            foreach (var a in All)
                if (a != null && a.Works) n += a.bodies.Count;
            return n;
        }
    }

    /// <summary>Положить броню / плащ в любую целую Оружейную со свободным местом. false - места нет.</summary>
    public static bool StashBody(BodyGear g)
    {
        if (g == null) return true;
        foreach (var a in All)
        {
            if (a == null || !a.Works || a.ItemCount >= a.Capacity) continue;
            a.bodies.Add(g);
            return true;
        }
        return false;
    }

    /// <summary>Всё подходящее оружие во всех целых Оружейных, лучшее - первым.</summary>
    public static List<StockEntry> Available(WeaponKind kind)
    {
        var res = new List<StockEntry>();
        foreach (var a in All)
        {
            if (a == null || !a.Works) continue;
            foreach (var w in a.stock)
                if (w.kind == kind) res.Add(new StockEntry { armory = a, weapon = w });
        }
        res.Sort((p, q) => q.weapon.Score.CompareTo(p.weapon.Score));
        return res;
    }

    /// <summary>Все щиты во всех целых Оружейных, лучший - первым.</summary>
    public static List<ShieldEntry> AvailableShields()
    {
        var res = new List<ShieldEntry>();
        foreach (var a in All)
        {
            if (a == null || !a.Works) continue;
            foreach (var s in a.shields)
                res.Add(new ShieldEntry { armory = a, shield = s });
        }
        res.Sort((p, q) => q.shield.Score.CompareTo(p.shield.Score));
        return res;
    }

    /// <summary>Сколько ещё предметов влезет во все целые Оружейные.</summary>
    public static int FreeSlotsTotal
    {
        get
        {
            int n = 0;
            foreach (var a in All)
                if (a != null && a.Works) n += Mathf.Max(0, a.Capacity - a.ItemCount);
            return n;
        }
    }

    public static int TotalWeapons
    {
        get
        {
            int n = 0;
            foreach (var a in All)
                if (a != null && a.Works) n += a.stock.Count;
            return n;
        }
    }

    public static int TotalShields
    {
        get
        {
            int n = 0;
            foreach (var a in All)
                if (a != null && a.Works) n += a.shields.Count;
            return n;
        }
    }

    /// <summary>Положить оружие в любую целую Оружейную со свободным местом. false - места нет.</summary>
    public static bool Stash(Weapon w)
    {
        if (w == null) return true;
        foreach (var a in All)
        {
            if (a == null || !a.Works || a.ItemCount >= a.Capacity) continue;
            a.stock.Add(w);
            return true;
        }
        return false;
    }

    /// <summary>Положить щит в любую целую Оружейную со свободным местом. false - места нет.</summary>
    public static bool StashShield(Shield s)
    {
        if (s == null) return true;
        foreach (var a in All)
        {
            if (a == null || !a.Works || a.ItemCount >= a.Capacity) continue;
            a.shields.Add(s);
            return true;
        }
        return false;
    }

    // ---------- Эффекты на выбранный предмет (кнопка «Эффект» у лука или щита) ----------
    //  - Яд: 1 зелье отравления (Зельеварня или рынок) = Weapon.PoisonChargesPerPotion отравленных выстрелов.
    //  - Замедление: 1 зелье замедления = Weapon.SlowChargesPerPotion выстрелов.
    //  - Салют (Пиротехник): 1 Фейерверочный лук из Цитадели = Weapon.SaluteChargesOf(mk) взрывающихся стрел.
    //  - Ядро хаоса на щит (школа Хаоса или ядро с рынка): см. MagicArmory.ApplyCoreToShield.
    // Всё, кроме зелья замедления, житель несёт из Цитадели / Хранилища; накладывается, когда принесли.

    readonly HashSet<Weapon> poisonPending = new HashSet<Weapon>();   // луки, к которым уже несут зелье
    readonly HashSet<Weapon> slowPending = new HashSet<Weapon>();
    readonly HashSet<Weapon> salutePending = new HashSet<Weapon>();

    void StartPoison(Weapon bow)
    {
        if (!Works || bow == null || poisonPending.Contains(bow)) return;
        if (!PotionHouse.TakePotions(1, out BuildingInstance src)) return;
        poisonPending.Add(bow);
        int carry = CarryLedger.AddPotions(1, false);

        var pm = PopulationManager.Instance;
        BuildingInstance pickup = src != null ? src : (pm != null ? pm.Hall : null);

        Logistics.Request(new HaulJob
        {
            from = pickup,
            to = Building,
            prefer = Building,
            onDeliver = () =>
            {
                CarryLedger.Done(carry);
                poisonPending.Remove(bow);
                bow.poisonCharges += Weapon.PoisonChargesPerPotion;
                WarManager.Notify($"Отравлен: {bow.Name}", true);
            },
            onAbort = loaded =>
            {
                CarryLedger.Done(carry);
                poisonPending.Remove(bow);
                PotionHouse.ReturnPotions(1, src);
            }
        });
    }

    void StartSlow(Weapon bow)
    {
        var rm = ResourceManager.Instance;
        if (!Works || bow == null || slowPending.Contains(bow) || rm == null || !rm.SpendSlowPotions(1)) return;
        slowPending.Add(bow);
        int carry = CarryLedger.AddPotions(1, true);
        var pm = PopulationManager.Instance;

        Logistics.Request(new HaulJob
        {
            from = pm != null ? pm.Hall : null,
            to = Building,
            prefer = Building,
            onDeliver = () =>
            {
                CarryLedger.Done(carry);
                slowPending.Remove(bow);
                bow.slowCharges += Weapon.SlowChargesPerPotion;
                WarManager.Notify($"Лук смазан зельем замедления: {bow.Name}", true);
            },
            onAbort = loaded =>
            {
                CarryLedger.Done(carry);
                slowPending.Remove(bow);
                if (ResourceManager.Instance != null) ResourceManager.Instance.AddSlowPotions(1);
            }
        });
    }

    // Лучший Фейерверочный лук в Цитадели / дворах (выше марка - лучше)
    public static bool BestFireworkKit(out ModuleKit best)
    {
        best = default(ModuleKit);
        bool found = false;
        foreach (var k in EngineerYard.AllKits())
            if (k.kind == ModuleKind.FireworkBow && (!found || k.mk > best.mk)) { best = k; found = true; }
        return found;
    }

    void StartSalute(Weapon bow)
    {
        if (!Works || bow == null || bow.kind != WeaponKind.Bow || salutePending.Contains(bow)) return;
        if (!BestFireworkKit(out ModuleKit kit) || !EngineerYard.TakeKit(kit, out BuildingInstance src)) return;
        salutePending.Add(bow);
        int carry = CarryLedger.Add(kit);
        var pm = PopulationManager.Instance;

        Logistics.Request(new HaulJob
        {
            from = src != null ? src : (pm != null ? pm.Hall : null),
            to = Building,
            prefer = Building,
            onDeliver = () =>
            {
                CarryLedger.Done(carry);
                salutePending.Remove(bow);
                int mk = Mathf.Clamp(kit.mk, 1, 3);
                // Новый салют заменяет старый, если он не хуже; остаток старых зарядов не пропадает
                bow.saluteMk = Mathf.Max(bow.saluteMk, mk);
                bow.saluteCharges += Weapon.SaluteChargesOf(mk);
                WarManager.Notify($"На лук наложен эффект салюта: {bow.Name}", true);
            },
            onAbort = loaded =>
            {
                CarryLedger.Done(carry);
                salutePending.Remove(bow);
                EngineerYard.AddToHall(kit);
            }
        });
    }

    // Какие эффекты показать (видны своему пути и всем, у кого есть нужная вещь - например, купленная на рынке)
    public static bool ShowPoison(Weapon w) { return w.IsPoisoned || DevelopmentPath.IsMagic || PotionHouse.TotalPotions > 0; }
    public static bool ShowSlow(Weapon w) { return w.IsSlowing || DevelopmentPath.IsMagic || (ResourceManager.Instance != null && ResourceManager.Instance.slowPotions > 0); }
    public static bool ShowSalute(Weapon w) { return w.IsSalute || DevelopmentPath.IsPyro || EngineerYard.CountKits(ModuleKind.FireworkBow) > 0; }
    public static bool ShowCore(Shield s) { return s.HasChaos || DevelopmentPath.IsChaos || EngineerYard.CountKits(ModuleKind.ChaosCore) > 0; }

    /// <summary>У предмета есть кнопка «Эффект» (луки и щиты).</summary>
    static bool HasEffects(object item)
    {
        Weapon w = item as Weapon;
        if (w != null) return w.kind == WeaponKind.Bow;
        return item is Shield;
    }

    int EffectRowCount(object item)
    {
        Weapon w = item as Weapon;
        Shield s = item as Shield;
        int n = 0;
        if (w != null) { if (ShowPoison(w)) n++; if (ShowSlow(w)) n++; if (ShowSalute(w)) n++; }
        if (s != null && ShowCore(s)) n++;
        return Mathf.Max(1, n);
    }

    const float EffectRowH = 30f;

    float EffectPanelH()
    {
        if (!EffectTargetAlive()) return 0f;
        return UiKit.SectionH + EffectRowCount(effectItem) * EffectRowH + 4f;
    }

    // Выбранный предмет ещё на складе этой Оружейной
    bool EffectTargetAlive()
    {
        if (effectItem == null) return false;
        Weapon w = effectItem as Weapon;
        Shield s = effectItem as Shield;
        if ((w != null && stock.Contains(w)) || (s != null && shields.Contains(s))) return true;
        effectItem = null;
        return false;
    }

    // Одна строка эффекта: название и что даёт · кнопка
    bool EffectRow(float x, float y, float w, string icon, string title, string info, string tip, string problem, string button)
    {
        float il = ItemIcons.Lead(new Rect(x, y, 26f, 26f), icon);
        Rect lr = new Rect(x + il, y + 4f, w - il - 150f, 20f);
        LocGUI.Label(lr, title + "  " + UiKit.Col(UiKit.MutedHex, info), UiKit.Small);
        UiKit.Tip(lr, tip);
        bool ok = problem == "";
        return UiKit.Button(new Rect(x + w - 144f, y, 144f, 26f), button, ok, UiKit.Kind.Primary, ok ? tip : problem);
    }

    void DrawEffectPanel(float x, float y, float w)
    {
        if (!EffectTargetAlive()) return;
        Weapon bow = effectItem as Weapon;
        Shield sh = effectItem as Shield;
        string name = bow != null ? bow.Name : sh.Name;
        float cy = UiKit.Section(x, y, w, "Эффект: " + name);
        if (UiKit.Button(new Rect(x + w - 22f, y + 2f, 20f, 18f), "×", UiKit.Kind.Ghost, "Закрыть")) { effectItem = null; return; }

        int rows = 0;
        if (bow != null)
        {
            if (ShowPoison(bow))
            {
                int potions = PotionHouse.TotalPotions;
                string p = !Works ? "Оружейная разрушена" : poisonPending.Contains(bow) ? "Зелье уже несут"
                         : bow.poisonCharges > Weapon.PoisonChargesPerPotion / 3 ? "Яда на луке ещё много"
                         : potions <= 0 ? "Нет зелий отравления (Зельеварня или рынок)" : "";
                if (EffectRow(x, cy, w, "potion", UiKit.Col(UiKit.GoodHex, "Яд"),
                        bow.IsPoisoned ? $"осталось {bow.poisonCharges} выстр." : $"зелий {potions}",
                        $"1 зелье отравления = {Weapon.PoisonChargesPerPotion} выстрелов ядом. Житель принесёт его из Цитадели или Хранилища.",
                        p, "Отравить"))
                    StartPoison(bow);
                cy += EffectRowH; rows++;
            }
            if (ShowSlow(bow))
            {
                int n = ResourceManager.Instance != null ? ResourceManager.Instance.slowPotions : 0;
                string p = !Works ? "Оружейная разрушена" : slowPending.Contains(bow) ? "Зелье уже несут"
                         : bow.slowCharges > Weapon.SlowChargesPerPotion / 3 ? "Зелья на луке ещё много"
                         : n <= 0 ? "Нет зелий замедления (Зельеварня или рынок)" : "";
                if (EffectRow(x, cy, w, "slow_potion", UiKit.Col(UiKit.InfoHex, "Замедление"),
                        bow.IsSlowing ? $"осталось {bow.slowCharges} выстр." : $"зелий {n}",
                        $"1 зелье замедления = {Weapon.SlowChargesPerPotion} выстрелов: стрелы замедляют врагов.",
                        p, "Смазать"))
                    StartSlow(bow);
                cy += EffectRowH; rows++;
            }
            if (ShowSalute(bow))
            {
                int kits = EngineerYard.CountKits(ModuleKind.FireworkBow);
                int mk = BestFireworkKit(out ModuleKit k) ? k.mk : 1;
                string p = !Works ? "Оружейная разрушена" : salutePending.Contains(bow) ? "Фейерверочный лук уже несут"
                         : bow.saluteCharges > Weapon.SaluteChargesOf(1) / 3 ? "Салюта на луке ещё много"
                         : kits <= 0 ? (DevelopmentPath.IsPyro ? "Нет Фейерверочного лука в Цитадели — соберите его в Инженерном дворе" : "Нет Фейерверочного лука в Цитадели (рынок)") : "";
                if (EffectRow(x, cy, w, "firework", UiKit.Col("#FFB070", "Салют"),
                        bow.IsSalute ? $"Мк.{TowerModules.Roman(bow.saluteMk)}, осталось {bow.saluteCharges} выстр." : $"Фейерверочных луков {kits}",
                        $"Тратит 1 Фейерверочный лук (модуль башни) из Цитадели: Мк.{TowerModules.Roman(mk)} даёт {Weapon.SaluteChargesOf(mk)} выстрелов. " +
                        "Стрела-ракета рвётся у цели, как у башни: взрыв бьёт врагов рядом, искры жгут встречных. Урон самой стрелы не меняется.",
                        p, "Наложить"))
                    StartSalute(bow);
                cy += EffectRowH; rows++;
            }
        }
        else if (sh != null && ShowCore(sh))
        {
            string p = !Works ? "Оружейная разрушена" : MagicArmory.CoreProblemFor(sh);
            if (EffectRow(x, cy, w, "chaos_core", UiKit.Col("#C9A6FF", "Ядро хаоса"),
                    sh.HasChaos ? $"Мк.{TowerModules.Roman(sh.chaosMk)}, отражений {sh.chaosCharges}" : $"ядер {EngineerYard.CountKits(ModuleKind.ChaosCore)}",
                    "Щит с небольшим шансом отражает снаряды обратно — в стрелка или в башню. Тратится лучшее ядро хаоса из Цитадели.",
                    p, "Наложить"))
                MagicArmory.ApplyCoreToShield(sh, Building);
            cy += EffectRowH; rows++;
        }
        if (rows == 0)
            LocGUI.Label(new Rect(x, cy, w, EffectRowH), UiKit.Col(UiKit.MutedHex,
                bow != null ? "Эффектов пока нет: яд и замедление — зелья (путь Магии или рынок), салют — Пиротехник."
                            : "Эффектов пока нет: ядро хаоса делает школа Хаоса, бывает на рынке."), UiKit.Small);
    }

    public bool RemoveFromStock(Weapon w) { return Works && stock.Remove(w); }
    public void AddToStock(Weapon w) { if (w != null) stock.Add(w); }
    public bool RemoveShield(Shield s) { return Works && shields.Remove(s); }
    public void AddShield(Shield s) { if (s != null) shields.Add(s); }
    public bool RemoveBody(BodyGear g) { return Works && bodies.Remove(g); }
    public void AddBody(BodyGear g) { if (g != null) bodies.Add(g); }

    // ---------- Панель здания ----------

    Vector2 scroll;
    int tab = 0;   // 0 - Ковка, 1 - Склад
    object effectItem;   // лук или щит, для которого открыт выбор эффекта

    const float ItemRowH = 50f;
    const float RecipeRowH = 32f;

    // Одна строка склада: название, полоска прочности, кнопки "Починить" и "Улучшить"
    void ItemRow(float ry, float innerW, GUIStyle small, string title, string stats, float frac, int repairCost, bool canRepair,
                 bool maxed, int bldLevel, int nextUpgrade, string nextName, int nextCost, bool canUpgrade,
                 out bool repair, out bool upgrade, bool upgradable = true, string costText = null, string upgradeProblem = null, string icon = null,
                 object effectFor = null, float titleRightPad = 0f)
    {
        float ix = 0f;
        if (icon != null) { ItemIcons.Draw(new Rect(0f, ry, 24f, 24f), icon); ix = 28f; }
        Rect tr = new Rect(ix, ry, innerW - ix - titleRightPad, 18f);
        LocGUI.Label(tr, title, small);
        UiKit.Tip(tr, stats);
        UiKit.Bar(new Rect(ix, ry + 19f, innerW - ix, 5f), frac,
            Color.Lerp(UiKit.Bad, UiKit.Good, frac));

        // Кнопки: «Починить», «Улучшить» и (у луков и щитов) «Эффект»
        bool fx = effectFor != null && HasEffects(effectFor);
        int nb = 1 + (upgradable ? 1 : 0) + (fx ? 1 : 0);
        float bw = (innerW - 4f * (nb - 1)) / nb;
        if (fx)
        {
            bool open = effectItem == effectFor;
            if (UiKit.Button(new Rect(innerW - bw, ry + 26f, bw, 22f), open ? "Эффект ▾" : "Эффект", true, open ? UiKit.Kind.Primary : UiKit.Kind.Normal,
                    "Наложить эффект на этот предмет (выбор — под списком)"))
                effectItem = open ? null : effectFor;
        }

        string repairLabel = repairCost > 0 ? $"Починить ({repairCost} зол)" : "Целое";
        string repairTip = repairCost <= 0 ? "Предмет целый" : (canRepair ? $"Починить за {repairCost} золота" : "Не хватает золота");
        repair = UiKit.Button(new Rect(0f, ry + 26f, bw, 22f), repairLabel, canRepair, UiKit.Kind.Normal, repairTip);

        upgrade = false;
        if (!upgradable) return;   // плащи не улучшают - только чинят
        string price = costText ?? $"{nextCost} зол";
        string upLabel, upTip;
        if (maxed) { upLabel = "Макс."; upTip = "Улучшено до предела"; }
        else if (bldLevel < nextUpgrade) { upLabel = $"→ {nextName}"; upTip = $"Нужна Оружейная Ур. {nextUpgrade}"; }
        else
        {
            upLabel = costText != null ? $"→ {nextName}" : $"→ {nextName} ({nextCost} зол)";
            upTip = canUpgrade ? $"Улучшить до «{nextName}»: {price}" : (upgradeProblem ?? "Не хватает золота") + $"\nЦена: {price}";
        }
        upgrade = UiKit.Button(new Rect(bw + 4f, ry + 26f, bw, 22f), upLabel, canUpgrade, UiKit.Kind.Normal, upTip);
    }

    // Почему нельзя начать ковку ("" - можно)
    string CraftProblem(WeaponRecipe r, bool busy = true)
    {
        if (!Works) return "Оружейная разрушена";
        if (!Building.isProducing) return "Оружейная не работает" + (Building.StatusText != "" ? ": " + Building.StatusText : "");
        if (busy && IsBusy) return "Уже идёт ковка";
        if (ItemCount >= Capacity) return "Склад полон — переплавьте лишнее на вкладке «Склад»";
        if (!CanAfford(r)) return r.leaves > 0 || r.gold > 0 ? $"Не хватает в казне: {r.CostText}" : $"Не хватает материалов в Цитадели: {r.CostText} (среднего качества и выше)";
        return "";
    }

    float HeaderH(BuildingInstance b)
    {
        return UiKit.RowH * 2f + (b.isWorking ? 0f : UiKit.RowH) + 4f;
    }

    float ForgeTabH(BuildingInstance b)
    {
        return UiKit.SectionH + Queue.Height + VisibleRecipes().Count * RecipeRowH + 4f + 30f + 8f + UpgradeRules.SectionHeight(b);
    }

    float StockTabH()
    {
        float listH = Mathf.Clamp(ItemCount * ItemRowH, ItemRowH * 2f, ItemRowH * 5f);
        float h = listH + 6f;
        h += EffectPanelH();
        return h;
    }

    public Rect DrawPanel(float x, float y, float w)
    {
        BuildingInstance b = Building;
        int lvl = b.level;
        const float tabsH = 26f;

        float contentH = HeaderH(b) + tabsH + 8f + (tab == 1 ? StockTabH() : ForgeTabH(b));
        float h = UiKit.WindowH(contentH);
        Rect c = UiKit.Window(new Rect(x, y, w, h), $"{TypeName} · Ур. {lvl}");
        float cx = c.x, cw = c.width, cy = c.y;

        GUIStyle body = new GUIStyle(UiKit.Body) { wordWrap = false, alignment = TextAnchor.MiddleLeft };
        GUIStyle small = new GUIStyle(UiKit.Small) { wordWrap = false, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };

        // --- Что делает ---
        if (stage == Stage.Crafting)
        {
            LocGUI.Label(new Rect(cx, cy, cw - 150f, UiKit.RowH), $"Ковка: <b>{(current != null ? current.recipeName : "")}</b>", body);
            UiKit.Bar(new Rect(cx + cw - 140f, cy + 4f, 140f, 14f), CraftProgress, UiKit.Good, $"{CraftProgress * 100f:F0}%");
        }
        else if (stage == Stage.Fetching)
            LocGUI.Label(new Rect(cx, cy, cw, UiKit.RowH), UiKit.Col(UiKit.WarnHex, "Несут материалы из Цитадели..."), body);
        else
            LocGUI.Label(new Rect(cx, cy, cw, UiKit.RowH), ItemCount >= Capacity ? UiKit.Col(UiKit.WarnHex, "Склад полон") + UiKit.Col(UiKit.MutedHex, " — лишнее можно переплавить") : UiKit.Col(UiKit.MutedHex, "Свободна — закажите ковку"), body);
        cy += UiKit.RowH;

        Rect sr = new Rect(cx, cy, cw - 20f, UiKit.RowH);
        LocGUI.Label(sr, UiKit.Col(UiKit.MutedHex, "Склад: ") + UiKit.Col(ItemCount >= Capacity ? UiKit.WarnHex : UiKit.GoodHex, $"{ItemCount}/{Capacity}")
            + UiKit.Col(UiKit.MutedHex, $"   оружие {stock.Count} · щиты {shields.Count}" + (bodies.Count > 0 ? $" · броня и плащи {bodies.Count}" : "")), body);
        Rect qr = new Rect(cx + cw - 16f, cy, 16f, UiKit.RowH);
        LocGUI.Label(qr, UiKit.Col(UiKit.InfoHex, "?"), body);
        UiKit.Tip(qr, "Куёт мечи, луки и щиты из стали и досок. Качество случайное: чем выше уровень, тем лучше. " +
                      (DevelopmentPath.IsHeavy ? "Ветка «Тяжёлая стратегия»: броня (−25…36% урона, −10% скорости), её усиливают сталью, камнем и золотом. " : "") +
                      (DevelopmentPath.IsCamo ? "Ветка «Мастер маскировки»: плащи из листвы (враги замечают вдвое ближе). " : "") +
                      "Золото чинит и усиливает предметы, кнопка «Эффект» у лука или щита накладывает яд, замедление, салют или ядро хаоса. Выдаёт предметы казарма (вкладка «Снаряжение»). " +
                      "Лишнее (например, трофеи из набегов) можно переплавить на вкладке «Склад», чтобы освободить место: вернётся 1 сталь или 1 доска. " +
                      $"Вместимость склада растёт с уровнем: 12 / 24 / 40. Выковано всего: {CraftedTotal}.");
        cy += UiKit.RowH;

        if (!b.isWorking)
        {
            LocGUI.Label(new Rect(cx, cy, cw, UiKit.RowH), UiKit.Col(UiKit.BadHex, b.StatusText), body);
            cy += UiKit.RowH;
        }
        cy += 4f;

        int prevTab = tab;
        int newTab = UiKit.Tabs(new Rect(cx, cy, cw, tabsH), new[] { "Ковка", $"Склад ({ItemCount})" }, tab);
        cy += tabsH + 8f;

        if (tab == 0)
        {
            // --- Рецепты ---
            cy = UiKit.Section(cx, cy, cw, "Заказать");
            cy = Queue.Draw(cx, cy, cw, q => q.recipeName);
            GUIStyle cost = new GUIStyle(small) { wordWrap = false };
            foreach (var r in VisibleRecipes())
            {
                ItemIcons.Draw(new Rect(cx, cy + 1f, 24f, 24f), ItemIcons.Of(r));
                LocGUI.Label(new Rect(cx + 28f, cy, 106f, RecipeRowH - 4f), $"<b>{r.recipeName}</b>", body);
                LocGUI.Label(new Rect(cx + 134f, cy, cw - 134f - 100f, RecipeRowH - 4f),
                    r.CostText + UiKit.Col(UiKit.MutedHex, $" · {r.craftTime:0.#} с"), cost);
                string problem = CraftProblem(r);
                string what = r.isBody ? (r.bodyKind == BodyKind.Armor
                        ? "\nБроня: урон по бойцу −25…36% (по качеству), скорость −10%. Выдаётся в казарме."
                        : "\nПлащ: враги замечают бойца вдвое ближе, пока он не бьёт; разведчика — лишь вплотную. Выдаётся в казарме.") : "";
                bool busy = IsBusy || Queue.Count > 0;
                if (busy)
                {
                    string qp = CraftProblem(r, false);
                    bool can = !Queue.Full && Works;
                    if (UiKit.Button(new Rect(cx + cw - 92f, cy, 92f, RecipeRowH - 4f), "+ Очередь", can, UiKit.Kind.Normal,
                            (Queue.Full ? $"Очередь полна ({CraftQueue<WeaponRecipe>.Max})" : "Идёт ковка — поставить в очередь. Материалы возьмём, когда начнётся работа"
                             + (qp != "" ? "\nСейчас: " + qp : "")) + what))
                        Order(r);
                }
                else if (UiKit.Button(new Rect(cx + cw - 92f, cy, 92f, RecipeRowH - 4f), "Заказать", problem == "", UiKit.Kind.Primary,
                        (problem == "" ? $"{r.recipeName}: {r.CostText}, {r.craftTime:0.#} с" : problem) + what))
                    Order(r);
                cy += RecipeRowH;
            }
            cy += 4f;

            if (PopulationManager.Instance != null)
                PopulationManager.Instance.DrawWorkersRow(cx, cy, b);
            cy += 30f + 8f;

            UiKit.Line(cx, cy - 5f, cw);
            UpgradeRules.DrawUpgradeSection(cx, cy, cw, b);
        }
        else
        {
            // --- Склад ---
            int rows = ItemCount;
            float listH = Mathf.Clamp(rows * ItemRowH, ItemRowH * 2f, ItemRowH * 5f);
            float innerW = cw - 14f;
            scroll = UiKit.BeginScrollView(new Rect(cx, cy, cw, listH), scroll,
                new Rect(0f, 0f, innerW, Mathf.Max(listH, rows * ItemRowH)));

            if (rows == 0)
                LocGUI.Label(new Rect(0f, 0f, innerW, 40f), UiKit.Col(UiKit.MutedHex, "Склад пуст — закажите ковку"), UiKit.Small);

            Weapon repairW = null, upgradeW = null;
            Shield repairS = null, upgradeS = null;
            BodyGear repairB = null, upgradeB = null;
            object melt = null;
            const float meltW = 84f;
            Rect MeltRect(int row) { return new Rect(innerW - meltW, row * ItemRowH, meltW, 18f); }

            for (int i = 0; i < stock.Count; i++)
            {
                Weapon wp = stock[i];
                ItemRow(i * ItemRowH, innerW, small,
                    $"<b>{wp.Name}</b>  " + UiKit.Col(UiKit.MutedHex, $"{wp.Percent}%"), wp.StatsText, wp.Frac, wp.RepairCost, CanRepairWeapon(wp),
                    !wp.CanUpgrade, lvl, wp.upgrade + 1, Weapon.UpgradeName(wp.upgrade + 1), Weapon.UpgradeCost(wp.upgrade + 1), CanUpgradeWeapon(wp),
                    out bool rep, out bool upg, true, null, null, ItemIcons.Of(wp), wp, meltW + 4f);
                if (MeltButton(MeltRect(i), wp)) melt = wp;
                if (rep) repairW = wp;
                if (upg) upgradeW = wp;
            }

            for (int i = 0; i < shields.Count; i++)
            {
                Shield sh = shields[i];
                ItemRow((stock.Count + i) * ItemRowH, innerW, small,
                    $"<b>{sh.Name}</b>  " + UiKit.Col(UiKit.MutedHex, $"{sh.Percent}%"), sh.StatsText, sh.Frac, sh.RepairCost, CanRepairShield(sh),
                    !sh.CanUpgrade, lvl, sh.upgrade + 1, Shield.UpgradeName(sh.upgrade + 1), Shield.UpgradeCost(sh.upgrade + 1), CanUpgradeShield(sh),
                    out bool rep, out bool upg, true, null, null, ItemIcons.Of(sh), sh, meltW + 4f);
                if (MeltButton(MeltRect(stock.Count + i), sh)) melt = sh;
                if (rep) repairS = sh;
                if (upg) upgradeS = sh;
            }

            for (int i = 0; i < bodies.Count; i++)
            {
                BodyGear bg = bodies[i];
                string col = bg.IsArmor ? "#B8C4D0" : "#90D070";
                ItemRow((stock.Count + shields.Count + i) * ItemRowH, innerW, small,
                    UiKit.Col(col, $"<b>{bg.Name}</b>") + "  " + UiKit.Col(UiKit.MutedHex, $"{bg.Percent}%"), bg.StatsText, bg.Frac, bg.RepairCost, CanRepairBody(bg),
                    !bg.CanUpgrade, lvl, bg.upgrade + 1, BodyGear.UpgradeName(bg.upgrade + 1), BodyGear.UpgradeGoldCost(bg.upgrade + 1), CanUpgradeBody(bg),
                    out bool rep, out bool upg, bg.IsArmor, BodyGear.UpgradeCostText(bg.upgrade + 1), BodyUpgradeProblem(bg), ItemIcons.Of(bg), null, meltW + 4f);
                if (MeltButton(MeltRect(stock.Count + shields.Count + i), bg)) melt = bg;
                if (rep) repairB = bg;
                if (upg) upgradeB = bg;
            }
            GUI.EndScrollView();
            cy += listH + 6f;

            if (repairW != null) TryRepairWeapon(repairW);
            if (upgradeW != null) TryUpgradeWeapon(upgradeW);
            if (repairS != null) TryRepairShield(repairS);
            if (upgradeS != null) TryUpgradeShield(upgradeS);
            if (repairB != null) TryRepairBody(repairB);
            if (upgradeB != null) TryUpgradeBody(upgradeB);
            if (melt != null) Melt(melt);

            // --- Эффект на выбранный лук или щит ---
            DrawEffectPanel(cx, cy, cw);
        }

        if (newTab != prevTab) tab = newTab;
        return new Rect(x, y, w + 5f, h);
    }
}
