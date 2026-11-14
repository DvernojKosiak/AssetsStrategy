using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Зельеварня (только путь Магии). Всё носится физически:
///  - травы берутся из казны Цитадели (их туда принесли со станций): житель несёт их в Зельеварню, потом варка;
///  - готовые зелья несут из Зельеварни в Цитадель или в Хранилище (если оно принимает продукцию);
///  - из Цитадели / Хранилища зелья несут дальше: в Оружейную (отравить лук), в казарму (маг-отравитель).
/// Варка зависит от маны: ниже MinManaPercent не варят, скорость умножается на ManaManager.Factor.
/// Два зелья:
///  - отравления: травы -> зелье (луки с ядом, Маг-отравитель);
///  - замедления: травы + смола (Лесопилка Ур.3) -> зелье (луки, замедляющие цель). Носят в Цитадель.
/// </summary>
public class PotionHouse : MonoBehaviour, ISavedBuilding
{
    public const string TypeName = "Зельеварня";

    public static readonly List<PotionHouse> All = new List<PotionHouse>();

    public const int HerbsPerBrew = 6;
    public const int PotionsPerBrew = 2;
    const float BrewSeconds = 16f;
    public const int MinManaPercent = 20;
    static readonly int[] CapacityByLevel = { 10, 16, 24 };   // сколько готовых зелий может ждать выноса
    static readonly float[] SpeedByLevel = { 1f, 1.3f, 1.6f };

    // Зелье замедления
    public const int SlowHerbs = 4;
    public const int SlowResin = 3;
    public const int SlowPerBrew = 2;
    const float SlowBrewSeconds = 18f;

    enum Stage { Idle, Fetching, Brewing }
    bool brewingSlow;                  // текущая варка - замедление
    public bool AutoSlow;              // что варить постоянно: false - отравление, true - замедление
    Stage stage = Stage.Idle;
    float timer;
    float autoTimer;
    int inTransit;                     // готовые зелья, которые несут из зельеварни

    public bool AutoBrew = true;
    public int BrewedTotal { get; private set; }

    int slowTransit;                   // из них - зелья замедления

    // Варка и готовые зелья в пути переживают выход из игры (обновление final-1)
    public void SaveState(SaveBag bag)
    {
        bag.Set("set.autoBrew", AutoBrew); bag.Set("set.autoSlow", AutoSlow);
        if (stage != Stage.Idle)
        {
            bag.Set("ph.slow", brewingSlow);
            bag.Set("ph.left", stage == Stage.Brewing ? Mathf.Max(0.05f, timer) : (brewingSlow ? SlowBrewSeconds : BrewSeconds));
        }
        if (inTransit - slowTransit > 0) bag.Set("ph.carry", inTransit - slowTransit);
        if (slowTransit > 0) bag.Set("ph.carrySlow", slowTransit);
    }

    public void LoadState(SaveBag bag)
    {
        AutoBrew = bag.Bool("set.autoBrew", AutoBrew); AutoSlow = bag.Bool("set.autoSlow", AutoSlow);
        if (bag.Has("ph.left"))
        {
            brewingSlow = bag.Bool("ph.slow");
            timer = JobSave.Left(bag.Float("ph.left", BrewSeconds));
            stage = Stage.Brewing;
        }
        int carry = bag.Int("ph.carry"), carrySlow = bag.Int("ph.carrySlow");
        if (carry > 0 || carrySlow > 0)
            JobSave.AfterLoad(() =>
            {
                var rm = ResourceManager.Instance;
                if (rm == null) return;
                if (carry > 0) rm.AddPotions(carry);
                if (carrySlow > 0) rm.AddSlowPotions(carrySlow);
            });
    }
    public int InTransit => inTransit;

    public BuildingInstance Building { get; private set; }
    int Lvl => Building != null ? Mathf.Clamp(Building.level, 1, 3) - 1 : 0;
    public int Capacity => CapacityByLevel[Lvl];
    public bool Works => Building != null && Building.isActiveAndEnabled && !Building.IsRuined;

    void Awake() { Building = GetComponent<BuildingInstance>(); }
    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    // ---------- Общий запас зелий: Цитадель + Хранилища ----------

    static BuildingInstance HallBuilding()
    {
        var pm = PopulationManager.Instance;
        return pm != null ? pm.Hall : null;
    }

    public static int TotalPotions
    {
        get
        {
            var rm = ResourceManager.Instance;
            return (rm != null ? rm.potions : 0) + WarehouseController.TotalPotions;
        }
    }

    /// <summary>
    /// Списать n зелий из одного места: сначала Цитадель, потом Хранилище. source - откуда взяли
    /// (оттуда житель и понесёт). false - нигде нет столько зелий.
    /// </summary>
    public static bool TakePotions(int n, out BuildingInstance source)
    {
        source = null;
        if (n <= 0) return true;
        var rm = ResourceManager.Instance;
        if (rm != null && rm.SpendPotions(n)) { source = HallBuilding(); return true; }
        return WarehouseController.TakePotions(n, out source);
    }

    /// <summary>Вернуть зелья (сорвано обучение / рейс): в то Хранилище, откуда взяли, иначе в Цитадель.</summary>
    public static void ReturnPotions(int n, BuildingInstance source)
    {
        if (n <= 0) return;
        if (source != null)
        {
            WarehouseController w = source.GetComponent<WarehouseController>();
            if (w != null && w.ReturnPotionsHere(n)) return;
        }
        if (ResourceManager.Instance != null) ResourceManager.Instance.AddPotions(n);
    }

    // ---------- Варка ----------

    static bool ManaOk => ManaManager.Instance == null || ManaManager.Instance.Percent >= MinManaPercent;

    public string BrewProblem() { return BrewProblem(false); }

    public string BrewProblem(bool slow)
    {
        if (!DevelopmentPath.IsMagic) return "Нужен путь Магии";
        if (!Works) return "Зельеварня разрушена";
        if (stage != Stage.Idle) return "Уже варят";
        if (!Building.isWorking) return "Нет рабочих";
        if (!ManaOk) return $"Мало маны (нужно от {MinManaPercent}%)";
        if (inTransit + PotionsPerBrew > Capacity) return "Готовые зелья ещё не вынесли";
        var rm = ResourceManager.Instance;
        int herbsNeed = slow ? SlowHerbs : HerbsPerBrew;
        if (rm == null || rm.herbs < herbsNeed) return $"Нет трав в Цитадели (нужно {herbsNeed})";
        if (slow && rm.resin < SlowResin) return $"Нет смолы в Цитадели (нужно {SlowResin})";
        // Лимит склада (StorageLimits): зелья замедления лежат в Цитадели, отравления - ещё и в Хранилищах
        if (slow ? StorageLimits.Free(StoreRes.SlowPotions) < SlowPerBrew + inTransit
                 : StorageLimits.Free(StoreRes.Potions) < PotionsPerBrew + inTransit
                   && WarehouseController.TotalFree < PotionsPerBrew)
            return "Склад полон: зелья некуда положить";
        return "";
    }

    public void StartBrew() { StartBrew(false); }

    public void StartBrew(bool slow)
    {
        if (BrewProblem(slow) != "") return;
        int herbsNeed = slow ? SlowHerbs : HerbsPerBrew;
        var rmx = ResourceManager.Instance;
        if (!rmx.SpendHerbs(herbsNeed)) return;
        if (slow && !rmx.SpendResin(SlowResin)) { rmx.AddHerbs(herbsNeed); return; }

        brewingSlow = slow;
        stage = Stage.Fetching;

        // Рабочий идёт в Цитадель за травами и несёт их сюда
        Logistics.Request(new HaulJob
        {
            from = HallBuilding(),
            to = Building,
            prefer = Building,
            onDeliver = () => { timer = slow ? SlowBrewSeconds : BrewSeconds; stage = Stage.Brewing; },
            onAbort = loaded =>
            {
                if (ResourceManager.Instance != null)
                {
                    ResourceManager.Instance.AddHerbs(herbsNeed);
                    if (slow) ResourceManager.Instance.AddResin(SlowResin);
                }
                stage = Stage.Idle;
            }
        });
    }

    void FinishBrew()
    {
        stage = Stage.Idle;
        if (brewingSlow)
        {
            // Зелье замедления - в Цитадель
            int n = SlowPerBrew;
            BrewedTotal += n;
            inTransit += n;
            slowTransit += n;
            Logistics.Request(new HaulJob
            {
                from = Building,
                to = HallBuilding(),
                prefer = Building,
                onDeliver = () => DeliverSlow(n),
                onAbort = loaded => DeliverSlow(n)
            });
            return;
        }
        int amount = PotionsPerBrew;
        BrewedTotal += amount;
        inTransit += amount;

        // Хранилище, которое принимает продукцию, иначе Цитадель
        WarehouseController wh = WarehouseController.PickForOutput(Building.Center, amount);
        BuildingInstance dest = wh != null ? wh.Building : HallBuilding();
        if (wh != null) wh.Reserve(amount);

        Logistics.Request(new HaulJob
        {
            from = Building,
            to = dest,
            prefer = Building,
            onDeliver = () => DeliverPotions(amount, wh),
            onAbort = loaded => DeliverPotions(amount, wh)   // зелья не теряем
        });
    }

    void DeliverSlow(int n)
    {
        inTransit = Mathf.Max(0, inTransit - n);
        slowTransit = Mathf.Max(0, slowTransit - n);
        if (ResourceManager.Instance != null) ResourceManager.Instance.AddSlowPotions(n);
    }

    void DeliverPotions(int amount, WarehouseController wh)
    {
        inTransit = Mathf.Max(0, inTransit - amount);
        if (wh != null)
        {
            wh.Unreserve(amount);
            if (wh.AcceptPotions(amount)) return;
        }
        // В Цитадели нет места - в любое Хранилище, где оно есть
        if (StorageLimits.Free(StoreRes.Potions) < amount)
            foreach (var w in WarehouseController.All)
                if (w != null && w.Works && w.Free >= amount && w.AcceptPotions(amount)) return;
        if (ResourceManager.Instance != null) ResourceManager.Instance.AddPotions(amount);
    }

    void Update()
    {
        if (Building == null) Building = GetComponent<BuildingInstance>();

        if (stage == Stage.Brewing)
        {
            if (!Works || !Building.isProducing) return;
            float mana = ManaOk ? ManaManager.Factor : 0f;
            timer -= Time.deltaTime * SpeedByLevel[Lvl] * mana * Building.HealthFactor * Building.CraftCrewFactor * Building.CraftBoost;
            if (timer <= 0f) FinishBrew();
            return;
        }

        if (stage == Stage.Idle && AutoBrew)
        {
            autoTimer -= Time.deltaTime;
            if (autoTimer <= 0f)
            {
                autoTimer = 3f;
                if (BrewProblem(AutoSlow) == "") StartBrew(AutoSlow);
            }
        }
    }

    // ---------- Панель ----------

    string StatusLine
    {
        get
        {
            switch (stage)
            {
                case Stage.Fetching: return "Несут травы из Цитадели...";
                case Stage.Brewing:
                    float pct = Mathf.Clamp01(1f - timer / (brewingSlow ? SlowBrewSeconds : BrewSeconds)) * 100f;
                    string what = brewingSlow ? "замедления" : "отравления";
                    return ManaOk ? $"Варится зелье {what}: {pct:F0}%" : UiKit.Col(UiKit.BadHex, $"Варка стоит — мало маны ({pct:F0}%)");
                default: return "Котёл свободен";
            }
        }
    }

    float BrewFraction
    {
        get
        {
            if (stage != Stage.Brewing) return 0f;
            return Mathf.Clamp01(1f - timer / (brewingSlow ? SlowBrewSeconds : BrewSeconds));
        }
    }

    // Строка рецепта: название · цена · короткая кнопка. true - нажали.
    static bool RecipeRow(float x, float y, float w, string name, string cost, string problem, string tip, bool primary)
    {
        float il = ItemIcons.Lead(new Rect(x, y, 26f, 26f), name == "Замедление" ? "slow_potion" : "potion");
        LocGUI.Label(new Rect(x + il, y + 4f, 110f - il, 20f), "<b>" + name + "</b>", UiKit.Body);
        LocGUI.Label(new Rect(x + 112f, y + 5f, w - 112f - 92f, 20f), cost, UiKit.Small);
        bool ok = problem == "";
        return UiKit.Button(new Rect(x + w - 88f, y, 88f, 26f), "Варить", ok,
            UiKit.Kind.Primary, ok ? tip : problem);   // final-13-fix-3: оба рецепта - одна кнопка одного цвета (было: яд жёлтая, замедление серая)
    }

    public Rect DrawPanel(float x, float y, float w)
    {
        BuildingInstance b = Building;
        GUIStyle st = UiKit.Body;
        GUIStyle small = UiKit.Small;
        GUIStyle muted = UiKit.MutedStyle;

        const string foldId = "potion-info";
        string infoText =
            "Травы со станций уносят в Цитадель («Собрать», курьеры). Повар несёт их из Цитадели в Зельеварню и варит. " +
            "Готовые зелья несут в Цитадель или Хранилище, оттуда — в Оружейную (отравленные луки) и в казарму (маг-отравитель). " +
            $"Смолу даёт Лесопилка Ур.3. Ниже {MinManaPercent}% маны варка стоит. Сварено всего: {BrewedTotal}.";
        bool infoOpen = UiKit.IsOpen(foldId);
        float infoH = infoOpen ? UiKit.TextH(infoText, w - 20f, muted) + 4f : 0f;

        float statusY = UiKit.TitleH + 6f;
        float barY = statusY + 22f;
        float stockY = barY + 14f;
        float herbsY = stockY + 20f;
        float problemY = herbsY + 20f;
        float recipesY = problemY + (b.isWorking ? 2f : 22f);
        float row1Y = recipesY + UiKit.SectionH + 2f;
        float row2Y = row1Y + 30f;
        float autoY = row2Y + 32f;
        float workersY = autoY + 26f;
        float foldY = workersY + 32f;
        float upgradeY = foldY + UiKit.FoldH + infoH + 6f;
        float h = upgradeY + UpgradeRules.SectionHeight(b) + 8f;
        UiKit.Window(new Rect(x, y, w, h), $"{TypeName} · Ур. {b.level}");

        var rm = ResourceManager.Instance;
        int herbs = rm != null ? rm.herbs : 0;
        int resin = rm != null ? rm.resin : 0;
        int mana = ManaManager.Instance != null ? ManaManager.Instance.Percent : 100;

        // --- Что происходит ---
        LocGUI.Label(new Rect(x + 10f, y + statusY, w - 20f, 20f), StatusLine, st);
        UiKit.Bar(new Rect(x + 10f, y + barY, w - 20f, 10f), BrewFraction, ManaOk ? UiKit.Info : UiKit.Bad);

        string carry = inTransit > 0 ? "  " + UiKit.Col(UiKit.WarnHex, $"· несут: {inTransit}") : "";
        LocGUI.Label(new Rect(x + 10f, y + stockY, w - 20f, 18f),
            $"Зелья: отравления <b>{TotalPotions}</b> · замедления <b>{(rm != null ? rm.slowPotions : 0)}</b>{carry}", small);

        string herbCol = herbs >= SlowHerbs ? UiKit.GoodHex : UiKit.WarnHex;
        string manaCol = mana >= MinManaPercent ? UiKit.InfoHex : UiKit.BadHex;
        Rect hr = new Rect(x + 10f, y + herbsY, w - 20f, 18f);
        LocGUI.Label(hr, $"Травы: <color={herbCol}><b>{herbs}</b></color> · смола: {resin} · мана: <color={manaCol}>{mana}%</color>  " +
                      UiKit.Col(UiKit.MutedHex, $"скорость ×{(mana >= MinManaPercent ? ManaManager.Factor : 0f) * SpeedByLevel[Lvl]:F2}"), small);
        UiKit.Tip(hr, $"Травы и смола — в казне Цитадели. На станциях ещё ждут трав: {BerryStation.TotalHerbs}.");

        if (!b.isWorking)
            LocGUI.Label(new Rect(x + 10f, y + problemY, w - 20f, 20f), UiKit.Col(UiKit.BadHex, b.StatusText), small);

        // --- Рецепты ---
        UiKit.Section(x + 10f, y + recipesY, w - 20f, "Варка");
        if (RecipeRow(x + 10f, y + row1Y, w - 20f, "Отравление",
                $"{HerbsPerBrew} трав → {PotionsPerBrew}", BrewProblem(false),
                "Зелье для отравленных луков и мага-отравителя", true))
            StartBrew(false);
        if (RecipeRow(x + 10f, y + row2Y, w - 20f, "Замедление",
                $"{SlowHerbs} трав + {SlowResin} смолы → {SlowPerBrew}", BrewProblem(true),
                "Зелье для луков, замедляющих цель", false))
            StartBrew(true);

        float half = (w - 20f) * 0.55f;
        Rect abr = new Rect(x + 10f, y + autoY, half, 22f);
        AutoBrew = LocGUI.Toggle(abr, AutoBrew, " Варить постоянно");
        UiKit.Tip(abr, "Зельеварня сама начинает новую варку, когда может.");
        Rect asr = new Rect(x + 10f + half, y + autoY, w - 20f - half, 22f);
        // final-13-fix-3: было непонятной галочкой «отравление/замедление» - теперь два переключателя рядом
        AutoSlow = UiKit.Tabs(asr, new[] { "Отравление", "Замедление" }, AutoSlow ? 1 : 0) == 1;
        UiKit.Tip(asr, "Какое зелье варить постоянно.");

        if (PopulationManager.Instance != null) PopulationManager.Instance.DrawWorkersRow(x + 10f, y + workersY, b);

        if (UiKit.Fold(new Rect(x + 10f, y + foldY, w - 20f, UiKit.FoldH), foldId) && infoOpen)
            LocGUI.Label(new Rect(x + 10f, y + foldY + UiKit.FoldH, w - 20f, infoH), infoText, muted);

        UpgradeRules.DrawUpgradeSection(x + 10f, y + upgradeY, w - 20f, b);
        return new Rect(x, y, w + 5f, h);
    }
}
