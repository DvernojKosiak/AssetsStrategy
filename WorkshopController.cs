using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class CraftRecipe
{
    public string recipeName = "Обработка дерева";
    public RefinedKind kind = RefinedKind.Planks;  // что получается: доски, плиты или сталь
    public int minLevel = 1;                         // с какого уровня мастерской доступен рецепт

    [Header("Стоимость сырья")]
    public int costWood = 15;
    public int costStone = 0;
    public int costIron = 0;     // железная руда

    [Header("Материалы со склада (любое качество)")]
    public int costPlates = 0;   // каменные плиты
    public int costSteel = 0;    // сталь
    public int costPlanks = 0;       // доски
    public int costReinforced = 0;   // укреплённые плиты
    public int costBeams = 0;        // смоляной брус

    [Header("Смола и пропитка (из казны)")]
    public int costResin = 0;
    public int costTar = 0;
    public bool outputsTar = false;  // результат - смоляная пропитка (без качества, в Ратушу)

    // "20д", "20к", "15 руды"
    public string CostText
    {
        get
        {
            var parts = new List<string>();
            if (costWood > 0) parts.Add(costWood + "д");
            if (costStone > 0) parts.Add(costStone + "к");
            if (costIron > 0) parts.Add(costIron + " руды");
            if (costPlates > 0) parts.Add(costPlates + " плит");
            if (costSteel > 0) parts.Add(costSteel + " стали");
            if (costPlanks > 0) parts.Add(costPlanks + " дос.");
            if (costReinforced > 0) parts.Add(costReinforced + " укр.");
            if (costBeams > 0) parts.Add(costBeams + " брус");
            if (costResin > 0) parts.Add(costResin + " смолы");
            if (costTar > 0) parts.Add(costTar + " проп.");
            return parts.Count > 0 ? string.Join(" ", parts) : "даром";
        }
    }

    [Header("Результат")]
    public int outputAmount = 5;
    public float craftTime = 4f;
}

/// <summary>
/// Мастерская. Теперь всё делается руками жителей:
///  1. Игрок заказывает рецепт - сырьё списывается из казны, рабочий мастерской идёт в Ратушу,
///     берёт сырьё и несёт в мастерскую.
///  2. Когда сырьё доставлено, идёт крафт.
///  3. Готовые доски/плиты рабочий несёт обратно в Ратушу, и только там они попадают на склад.
/// Пока рабочего нет (все заняты), заказ ждёт в очереди (см. Logistics).
/// С Ур.2 мастерская делает сталь из железной руды (руду добывает шахта Ур.3).
/// С Ур.3 - укреплённые плиты: каменная плита + сталь + немного камня (для башен Ур.3, аванпостов Ур.3, казармы Ур.4).
/// </summary>
public class WorkshopController : MonoBehaviour, ISavedBuilding
{
    enum Stage { Idle, FetchingRaw, Crafting }

    private BuildingInstance buildingInstance;

    public List<CraftRecipe> availableRecipes = new List<CraftRecipe>
    {
        new CraftRecipe { recipeName = "Сделать доски", kind = RefinedKind.Planks, costWood = 20, outputAmount = 5, craftTime = 4f },
        new CraftRecipe { recipeName = "Сделать плиты", kind = RefinedKind.StonePlates, costStone = 20, outputAmount = 5, craftTime = 4f },
        new CraftRecipe { recipeName = "Выплавить сталь", kind = RefinedKind.Steel, costIron = 15, outputAmount = 4, craftTime = 8f, minLevel = 2 },
        ReinforcedRecipe()
    };

    // Укреплённые плиты: 2 плиты + 1 сталь + 10 камня -> 2 укреплённые плиты (мастерская Ур.3)
    static CraftRecipe ReinforcedRecipe()
    {
        return new CraftRecipe { recipeName = "Укрепить плиты", kind = RefinedKind.ReinforcedPlates, costWood = 0, costStone = 10,
                                 costPlates = 2, costSteel = 1, outputAmount = 2, craftTime = 10f, minLevel = 3 };
    }

    // Смоляная пропитка (Ур.2): смола + дерево. Из неё - смоляной брус и монолитное основание (Ур.3),
    // которыми укрепляют здания (+50% прочности, см. UpgradeRules - «Укрепить»)
    static CraftRecipe TarRecipe()
    {
        return new CraftRecipe { recipeName = "Сварить пропитку", kind = RefinedKind.Planks, outputsTar = true,
                                 costWood = 10, costResin = 6, outputAmount = 4, craftTime = 8f, minLevel = 2 };
    }

    static CraftRecipe BeamsRecipe()
    {
        return new CraftRecipe { recipeName = "Смоляной брус", kind = RefinedKind.TarredBeams, costWood = 0,
                                 costPlanks = 4, costTar = 2, outputAmount = 3, craftTime = 9f, minLevel = 3 };
    }

    static CraftRecipe MonolithRecipe()
    {
        return new CraftRecipe { recipeName = "Монолит", kind = RefinedKind.Monolith, costWood = 0, costStone = 10,
                                 costReinforced = 2, costBeams = 1, costTar = 3, outputAmount = 1, craftTime = 14f, minLevel = 3 };
    }

    // Сколько секунд делается одна партия (на Ур.1, без ускорений): простое - пара минут, сложное - дольше
    public static float CraftSeconds(CraftRecipe r)
    {
        if (r.outputsTar) return 150f;                       // пропитка - 2,5 мин
        switch (r.kind)
        {
            case RefinedKind.Planks: return 90f;             // доски - 1,5 мин
            case RefinedKind.StonePlates: return 120f;       // плиты - 2 мин
            case RefinedKind.Steel: return 240f;             // сталь - 4 мин
            case RefinedKind.ReinforcedPlates: return 300f;  // укреплённые плиты - 5 мин
            case RefinedKind.TarredBeams: return 300f;       // смоляной брус - 5 мин
            case RefinedKind.Monolith: return 480f;          // монолит - 8 мин
            default: return Mathf.Max(60f, r.craftTime);
        }
    }

    // Список мог сохраниться в сцене старым - новые рецепты добавляем сами
    void Awake()
    {
        if (availableRecipes == null) availableRecipes = new List<CraftRecipe>();
        if (!availableRecipes.Exists(r => r != null && r.kind == RefinedKind.ReinforcedPlates))
            availableRecipes.Add(ReinforcedRecipe());
        if (!availableRecipes.Exists(r => r != null && r.outputsTar)) availableRecipes.Add(TarRecipe());
        if (!availableRecipes.Exists(r => r != null && !r.outputsTar && r.kind == RefinedKind.TarredBeams)) availableRecipes.Add(BeamsRecipe());
        if (!availableRecipes.Exists(r => r != null && !r.outputsTar && r.kind == RefinedKind.Monolith)) availableRecipes.Add(MonolithRecipe());
        // Время изготовления - здесь, а не в сцене (там могли остаться старые секунды)
        foreach (var r in availableRecipes) if (r != null) r.craftTime = CraftSeconds(r);
        // Баланс (final-5): сталь - узкое место пути Военного дела (меч - 4 стали, броня - 6). Партия стали - 4 штуки
        // вместо 3 (руды так же 15). Здесь, а не в сцене: в сцене могла остаться старая цифра
        foreach (var r in availableRecipes) if (r != null && !r.outputsTar && r.kind == RefinedKind.Steel) r.outputAmount = 4;
    }

    private Stage stage = Stage.Idle;
    private CraftRecipe currentRecipe = null;
    private float craftTimer = 0f;
    private int outputsInTransit = 0;   // сколько партий готового сейчас несут в Ратушу

    public bool IsCrafting => stage == Stage.Crafting;
    public bool IsBusy => stage != Stage.Idle;
    public string CurrentRecipeName => currentRecipe != null ? currentRecipe.recipeName : "";
    public float CraftProgress => stage == Stage.Crafting && currentRecipe != null && currentRecipe.craftTime > 0
        ? (1f - (craftTimer / currentRecipe.craftTime)) : 0f;

    // Короткая строка состояния для панели
    public string StatusLine
    {
        get
        {
            switch (stage)
            {
                case Stage.FetchingRaw:
                    return "Несут сырьё из Ратуши...";
                case Stage.Crafting:
                    if (buildingInstance != null && buildingInstance.CraftCrewFactor <= 0f)
                        return $"Крафт стоит: <b>{CurrentRecipeName}</b> ({CraftProgress * 100f:F0}%) — в мастерской никого нет";
                    return $"Крафт: <b>{CurrentRecipeName}</b> ({CraftProgress * 100f:F0}%)";
                default:
                    return outputsInTransit > 0 ? $"Свободна. Готовое несут: {outputsInTransit}" : "Мастерская свободна";
            }
        }
    }

    void Start()
    {
        buildingInstance = GetComponent<BuildingInstance>();
    }

    // ---------- Очередь заказов (final-8) ----------
    public readonly CraftQueue<CraftRecipe> Queue = new CraftQueue<CraftRecipe>();

    /// <summary>Почему рецепт нельзя начать прямо сейчас ("" - можно; занятость не считается).</summary>
    public string Problem(CraftRecipe r)
    {
        if (buildingInstance == null) buildingInstance = GetComponent<BuildingInstance>();
        if (r == null) return "нет рецепта";
        if (buildingInstance != null && !buildingInstance.isProducing) return "мастерская не работает";
        if (!Unlocked(r)) return "нужна мастерская выше уровнем";
        int room = r.outputsTar ? StorageLimits.Free(StoreRes.Tar) : StorageLimits.RefinedRoom(r.kind);
        if (room < r.outputAmount) return "склад полон";
        if (!CanAfford(r)) return "не хватает сырья: " + r.CostText;
        return "";
    }

    /// <summary>Кнопка рецепта: свободна - начать сразу, занята - в очередь.</summary>
    public void Order(CraftRecipe r)
    {
        if (!IsBusy && Queue.Count == 0) StartCraft(r);
        else Queue.Add(r, "Мастерская", r.recipeName);
    }

    void Update()
    {
        Queue.Tick(IsBusy, Problem, r => { StartCraft(r); return IsBusy; }, "Мастерская");
        // Сырьё так и не принесли (некому нести, рейс потерялся) - не висим вечно: мастера начинают сами
        if (stage == Stage.FetchingRaw && Time.time - fetchSince > FetchGiveUp) OnRawDelivered();
        if (stage != Stage.Crafting) return;
        if (buildingInstance != null && !buildingInstance.isProducing) return;

        // При дефиците мастерская работает медленнее, но не встаёт
        // Голодные и раненые мастера работают медленнее (HealthFactor)
        craftTimer -= Time.deltaTime * LevelSpeed * (buildingInstance != null ? buildingInstance.SpeedMultiplier * buildingInstance.CraftCrewFactor * buildingInstance.HealthFactor * buildingInstance.CraftBoost : 1f);
        if (craftTimer > 0f) return;

        FinishCraft();
    }

    // ---------- Заказ ----------

    // Баланс (final-5): мастерская выше уровнем работает быстрее. Раньше уровень давал только новые рецепты
    // и качество, а доски и плиты (главное узкое место середины игры) шли одинаково медленно.
    static readonly float[] SpeedByLevel = { 1f, 1.3f, 1.6f };
    public float LevelSpeed
    {
        get
        {
            if (buildingInstance == null) buildingInstance = GetComponent<BuildingInstance>();
            int lvl = buildingInstance != null ? buildingInstance.level : 1;
            return SpeedByLevel[Mathf.Clamp(lvl, 1, 3) - 1];
        }
    }

    // Рецепт доступен на этом уровне мастерской
    public bool Unlocked(CraftRecipe recipe)
    {
        if (buildingInstance == null) buildingInstance = GetComponent<BuildingInstance>();
        int lvl = buildingInstance != null ? buildingInstance.level : 1;
        return recipe != null && lvl >= recipe.minLevel;
    }

    public bool CanAfford(CraftRecipe recipe)
    {
        var rm = ResourceManager.Instance;
        if (rm == null || !rm.HasEnough(0, recipe.costWood, recipe.costStone, 0) || rm.iron < recipe.costIron) return false;
        if (rm.resin < recipe.costResin || rm.tar < recipe.costTar) return false;
        if (recipe.costPlates <= 0 && recipe.costSteel <= 0 && recipe.costPlanks <= 0 && recipe.costReinforced <= 0 && recipe.costBeams <= 0) return true;
        var st = TownHallStorage.Instance;
        if (st == null) return false;
        return UpgradeRules.CountAtLeast(st, RefinedKind.StonePlates, QualityTier.Medium) >= recipe.costPlates
            && UpgradeRules.CountAtLeast(st, RefinedKind.Steel, QualityTier.Medium) >= recipe.costSteel
            && UpgradeRules.CountAtLeast(st, RefinedKind.Planks, QualityTier.Medium) >= recipe.costPlanks
            && UpgradeRules.CountAtLeast(st, RefinedKind.ReinforcedPlates, QualityTier.Medium) >= recipe.costReinforced
            && UpgradeRules.CountAtLeast(st, RefinedKind.TarredBeams, QualityTier.Medium) >= recipe.costBeams;
    }

    // Что списали со склада под текущий заказ (вернём, если рейс за сырьём сорвётся)
    readonly List<UpgradeRules.Spent> spentRefined = new List<UpgradeRules.Spent>();

    public void StartCraft(CraftRecipe recipe)
    {
        if (buildingInstance == null) buildingInstance = GetComponent<BuildingInstance>();
        if (buildingInstance != null && !buildingInstance.isProducing) return;
        if (IsBusy || !Unlocked(recipe)) return;

        var rm = ResourceManager.Instance;
        if (rm == null || !CanAfford(recipe) || !rm.Spend(0, recipe.costWood, recipe.costStone, 0))
        {
            Debug.Log("Недостаточно сырья для мастерской!");
            return;
        }
        rm.SpendIron(recipe.costIron);
        rm.SpendResin(recipe.costResin);
        rm.SpendTar(recipe.costTar);

        // Материалы со склада: сначала самые простые по качеству
        spentRefined.Clear();
        var st = TownHallStorage.Instance;
        if (st != null)
        {
            if (recipe.costPlates > 0) UpgradeRules.SpendAtLeast(st, RefinedKind.StonePlates, QualityTier.Medium, recipe.costPlates, spentRefined);
            if (recipe.costSteel > 0) UpgradeRules.SpendAtLeast(st, RefinedKind.Steel, QualityTier.Medium, recipe.costSteel, spentRefined);
            if (recipe.costPlanks > 0) UpgradeRules.SpendAtLeast(st, RefinedKind.Planks, QualityTier.Medium, recipe.costPlanks, spentRefined);
            if (recipe.costReinforced > 0) UpgradeRules.SpendAtLeast(st, RefinedKind.ReinforcedPlates, QualityTier.Medium, recipe.costReinforced, spentRefined);
            if (recipe.costBeams > 0) UpgradeRules.SpendAtLeast(st, RefinedKind.TarredBeams, QualityTier.Medium, recipe.costBeams, spentRefined);
        }

        currentRecipe = recipe;
        stage = Stage.FetchingRaw;
        fetchSince = Time.time;
        int job = ++jobId;

        var pm = PopulationManager.Instance;
        BuildingInstance hall = pm != null ? pm.Hall : null;

        // Рабочий мастерской идёт в Ратушу за сырьём и несёт его сюда
        Logistics.Request(new HaulJob
        {
            from = hall,
            to = buildingInstance,
            prefer = buildingInstance,
            onDeliver = () => { if (job == jobId) OnRawDelivered(); },
            onAbort = loaded => { if (job == jobId) OnRawAborted(recipe, loaded); }
        });
    }

    // final-9: у каждого заказа свой номер - запоздалый «рейс сорван» от прошлого рейса больше не сбрасывает
    // уже идущий крафт (было: рабочий приносил сырьё, а мастерская тут же становилась свободной)
    int jobId;
    float fetchSince;
    const float FetchGiveUp = 300f;   // столько ждём сырьё; дольше - мастера сами забирают его и начинают

    void OnRawDelivered()
    {
        if (currentRecipe == null || stage != Stage.FetchingRaw) return;
        craftTimer = currentRecipe.craftTime;
        stage = Stage.Crafting;
    }

    // Рейс сорван (мастерскую снесли, жителя забрали в казарму, долго шёл и т.п.)
    void OnRawAborted(CraftRecipe recipe, bool loaded)
    {
        if (stage != Stage.FetchingRaw) return;
        // Сырьё уже несли и мастерская цела - считаем, что донесли: крафт начинается (раньше сырьё уходило
        // обратно в казну, очередь тут же заказывала снова, и рабочий ходил туда-сюда без конца)
        if (loaded && buildingInstance != null && !buildingInstance.IsRuined && buildingInstance.isActiveAndEnabled)
        {
            OnRawDelivered();
            return;
        }
        WarManager.Notify($"Мастерская: рабочий не донёс сырьё для «{recipe.recipeName}», сырьё вернулось в Цитадель", false);
        RefundRaw(recipe);
    }

    void RefundRaw(CraftRecipe recipe)
    {
        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.Add(0, recipe.costWood, recipe.costStone, 0, recipe.costIron);
            ResourceManager.Instance.AddResin(recipe.costResin);
            ResourceManager.Instance.AddTar(recipe.costTar);
        }
        UpgradeRules.RefundSpent(spentRefined);
        spentRefined.Clear();
        currentRecipe = null;
        stage = Stage.Idle;
    }

    // ---------- Готовая продукция ----------

    void FinishCraft()
    {
        CraftRecipe recipe = currentRecipe;
        QualityTier quality = RollQuality();
        RefinedKind kind = recipe.kind;
        int amount = recipe.outputAmount;

        // Пропитка - жидкость без качества: несут в Ратушу
        if (recipe.outputsTar)
        {
            currentRecipe = null;
            stage = Stage.Idle;
            outputsInTransit++;
            int[] noteT = new[] { 0, 0, amount, 1 };
            transit.Add(noteT);
            var pmT = PopulationManager.Instance;
            Logistics.Request(new HaulJob
            {
                from = buildingInstance,
                to = pmT != null ? pmT.Hall : null,
                prefer = buildingInstance,
                onDeliver = () => { transit.Remove(noteT); DeliverTar(amount); },
                onAbort = loaded => { transit.Remove(noteT); DeliverTar(amount); }
            });
            return;
        }

        // Мастерская сразу свободна для нового заказа, а готовое несут в Ратушу
        currentRecipe = null;
        stage = Stage.Idle;
        outputsInTransit++;

        var pm = PopulationManager.Instance;
        BuildingInstance hall = pm != null ? pm.Hall : null;

        // Есть Хранилище, которое принимает продукцию мастерских, - несём туда, иначе в Ратушу
        Vector2 here = buildingInstance != null ? buildingInstance.Center : Vector2.zero;
        WarehouseController wh = WarehouseController.PickForOutput(here, amount);
        BuildingInstance dest = wh != null ? wh.Building : hall;
        if (wh != null) wh.Reserve(amount);
        int[] note = new[] { (int)kind, (int)quality, amount, 0 };
        transit.Add(note);

        Logistics.Request(new HaulJob
        {
            from = buildingInstance,
            to = dest,
            prefer = buildingInstance,
            onDeliver = () => { transit.Remove(note); DeliverOutput(kind, quality, amount, wh); },
            onAbort = loaded => { transit.Remove(note); DeliverOutput(kind, quality, amount, wh); } // продукцию не теряем
        });
    }

    // ---------- Сохранение (CitySave) ----------
    // Заказ в работе переживает выход из игры: сырьё уже списано. Пока игрока не было, заказ доделывается
    // (время - по часам сервера); готово - продукция сразу на складе Цитадели.

    // Готовое, которое несут на склад (финал: раньше при выходе из игры в этот момент оно пропадало)
    readonly List<int[]> transit = new List<int[]>();

    public void SaveState(SaveBag bag)
    {
        var outs = new List<string[]>();
        foreach (var t in transit) outs.Add(new[] { SaveBag.I(t[0]), SaveBag.I(t[1]), SaveBag.I(t[2]), SaveBag.I(t[3]) });
        bag.SetList("ws.out", outs);
        if (Queue.Count > 0) bag.Set("ws.queue", Queue.Save(availableRecipes));
        if (Queue.Count > 0 || (currentRecipe != null && stage != Stage.Idle)) bag.Set("ws.at", BuildingInstance.RuinClockUtc.ToString("o"));
        if (currentRecipe == null || stage == Stage.Idle) return;
        int i = availableRecipes.IndexOf(currentRecipe);
        if (i < 0) return;
        float left = stage == Stage.Crafting ? Mathf.Max(0f, craftTimer) : currentRecipe.craftTime;
        bag.Set("ws.recipe", i);
        bag.Set("ws.left", left);
        bag.Set("ws.at", BuildingInstance.RuinClockUtc.ToString("o"));
    }

    public void LoadState(SaveBag bag)
    {
        // Что несли на склад, пока игра закрылась, - сразу на складе Цитадели
        foreach (var a in bag.List("ws.out"))
        {
            int amount = SaveBag.At(a, 2);
            if (amount <= 0) continue;
            if (SaveBag.At(a, 3) == 1) { if (ResourceManager.Instance != null) ResourceManager.Instance.AddTar(amount); }
            else if (TownHallStorage.Instance != null) TownHallStorage.Instance.Add((RefinedKind)SaveBag.At(a, 0), (QualityTier)Mathf.Clamp(SaveBag.At(a, 1), 0, 2), amount);
        }
        Queue.Load(bag.Get("ws.queue"), availableRecipes);
        if (buildingInstance == null) buildingInstance = GetComponent<BuildingInstance>();
        double away = 0.0;
        string at = bag.Get("ws.at");
        if (!string.IsNullOrEmpty(at) && System.DateTime.TryParse(at, null, System.Globalization.DateTimeStyles.RoundtripKind, out System.DateTime t))
            away = System.Math.Max(0.0, (BuildingInstance.RuinClockUtc - t.ToUniversalTime()).TotalSeconds);
        away = System.Math.Min(away, 14 * 86400.0);

        int i = bag.Has("ws.recipe") ? bag.Int("ws.recipe", -1) : -1;
        if (i >= 0 && i < availableRecipes.Count && availableRecipes[i] != null)
        {
            CraftRecipe recipe = availableRecipes[i];
            float left = bag.Float("ws.left", recipe.craftTime);
            if (away < left)
            {
                currentRecipe = recipe;
                craftTimer = left - (float)away;
                stage = Stage.Crafting;
                return;
            }
            // Доделали, пока игрока не было: продукция уже в Цитадели
            GiveOffline(recipe);
            currentRecipe = null;
            stage = Stage.Idle;
            away -= left;
        }
        // Обновление 12: очередь тоже работает, пока игрока нет. Раньше доделывался только текущий заказ,
        // а следующие ждали входа в игру. Сырьё списывается, когда весь город уже загружен (запасы на месте).
        if (Queue.Count > 0 && away > 1.0)
        {
            double rest = away;
            JobSave.AfterLoad(() => OfflineQueue(rest));
        }
    }

    void GiveOffline(CraftRecipe recipe)
    {
        if (recipe.outputsTar) { if (ResourceManager.Instance != null) ResourceManager.Instance.AddTar(recipe.outputAmount); }
        else if (TownHallStorage.Instance != null) TownHallStorage.Instance.Add(recipe.kind, RollQuality(), recipe.outputAmount);
    }

    // Заказы из очереди по порядку: хватает сырья - списываем и делаем за время рецепта (с учётом уровня мастерской);
    // время кончилось посреди заказа - он остаётся в работе с остатком таймера
    void OfflineQueue(double rest)
    {
        if (this == null || buildingInstance == null || buildingInstance.IsRuined) return;
        if (IsBusy) return;   // рабочих после входа ещё не расставили - isProducing тут не смотрим
        int made = 0;
        var names = new List<string>();
        while (Queue.Count > 0 && rest > 0.0)
        {
            CraftRecipe r = Queue.Items[0];
            if (r == null || !Unlocked(r) || !CanAfford(r)) break;
            if (!r.outputsTar && StorageLimits.RefinedRoom(r.kind) < r.outputAmount) break;
            SpendRaw(r);
            spentRefined.Clear();
            Queue.Items.RemoveAt(0);
            double need = r.craftTime / Mathf.Max(0.1f, LevelSpeed);
            if (rest >= need)
            {
                GiveOffline(r);
                rest -= need;
                made++;
                if (!names.Contains(r.recipeName)) names.Add(r.recipeName);
                continue;
            }
            currentRecipe = r;
            craftTimer = (float)((need - rest) * LevelSpeed);
            stage = Stage.Crafting;
            break;
        }
        if (made > 0) WarManager.Notify($"Мастерская, пока вас не было: готово заказов из очереди — {made} ({string.Join(", ", names)})", true);
    }

    // Списать сырьё заказа (как в StartCraft, без рейса рабочего)
    void SpendRaw(CraftRecipe recipe)
    {
        var rm = ResourceManager.Instance;
        if (rm == null) return;
        rm.Spend(0, recipe.costWood, recipe.costStone, 0);
        rm.SpendIron(recipe.costIron);
        rm.SpendResin(recipe.costResin);
        rm.SpendTar(recipe.costTar);
        var st = TownHallStorage.Instance;
        if (st == null) return;
        if (recipe.costPlates > 0) UpgradeRules.SpendAtLeast(st, RefinedKind.StonePlates, QualityTier.Medium, recipe.costPlates, spentRefined);
        if (recipe.costSteel > 0) UpgradeRules.SpendAtLeast(st, RefinedKind.Steel, QualityTier.Medium, recipe.costSteel, spentRefined);
        if (recipe.costPlanks > 0) UpgradeRules.SpendAtLeast(st, RefinedKind.Planks, QualityTier.Medium, recipe.costPlanks, spentRefined);
        if (recipe.costReinforced > 0) UpgradeRules.SpendAtLeast(st, RefinedKind.ReinforcedPlates, QualityTier.Medium, recipe.costReinforced, spentRefined);
        if (recipe.costBeams > 0) UpgradeRules.SpendAtLeast(st, RefinedKind.TarredBeams, QualityTier.Medium, recipe.costBeams, spentRefined);
    }

    void DeliverTar(int amount)
    {
        outputsInTransit = Mathf.Max(0, outputsInTransit - 1);
        if (ResourceManager.Instance != null) ResourceManager.Instance.AddTar(amount);
    }

    void DeliverOutput(RefinedKind kind, QualityTier quality, int amount, WarehouseController wh = null)
    {
        outputsInTransit = Mathf.Max(0, outputsInTransit - 1);

        // Несли в Хранилище. Если его успели разрушить - продукция уходит в Ратушу.
        if (wh != null)
        {
            wh.Unreserve(amount);
            if (wh.Accept(kind, quality, amount))
            {
                Debug.Log($"[Мастерская] Доставлено в Хранилище! Качество: {TownHallStorage.TierName(quality)}");
                return;
            }
        }

        var storage = TownHallStorage.Instance;
        if (storage == null) return;

        storage.Add(kind, quality, amount);

        Debug.Log($"[Мастерская] Доставлено в Ратушу! Качество: {storage.GetQualityName(quality)}");
    }

    // Система шансов в зависимости от уровня здания (1, 2 или 3)
    QualityTier RollQuality()
    {
        int lvl = buildingInstance != null ? buildingInstance.level : 1;
        float roll = Random.Range(0f, 100f);

        if (lvl == 1)
        {
            // Ур. 1: 85% Среднее, 15% Хорошее, 0% Наилучшее
            if (roll < 85f) return QualityTier.Medium;
            else return QualityTier.Good;
        }
        else if (lvl == 2)
        {
            // Ур. 2: 50% Среднее, 40% Хорошее, 10% Наилучшее
            if (roll < 50f) return QualityTier.Medium;
            else if (roll < 90f) return QualityTier.Good;
            else return QualityTier.Best;
        }
        else
        {
            // Ур. 3: 20% Среднее, 50% Хорошее, 30% Наилучшее
            if (roll < 20f) return QualityTier.Medium;
            else if (roll < 70f) return QualityTier.Good;
            else return QualityTier.Best;
        }
    }
}