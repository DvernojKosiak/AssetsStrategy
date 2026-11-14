using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Хранилище. Второй склад для обработанных материалов (доски, плиты, сталь всех качеств).
///  - Вместимость зависит от уровня (100 / 200 / 320 штук).
///  - Материалы можно перенаправлять руками: из Цитадели в Хранилище и обратно. Переносят жители
///    (как и всё остальное в игре: рейс через Logistics) - сначала рабочие Цитадели, потом свободные.
///  - Можно включить "Принимать продукцию мастерских": тогда готовое несут не в Цитадель, а сюда
///    (ближайшее Хранилище со свободным местом).
///  - Материалы Хранилища считаются наравне с Цитаделью: на улучшения и постройки копий они
///    списываются автоматически (сначала Цитадель, потом Хранилища) - житель несёт их оттуда.
///  - Если Хранилище разграбят и разрушат, враги уносят 20% содержимого. Снесёте само здание -
///    всё, что в нём лежало, возвращается в Цитадель (сколько влезет до лимита, см. StorageLimits).
///  - Каждое Хранилище поднимает лимиты казны (золото, дерево, камень, еда, руда...), тем больше, чем выше уровень.
///    Когда в Цитадели кончается место под материал, лишнее само ложится в свободное место Хранилищ.
/// Добавляется на здание в BuildingPlacer.
/// </summary>
public class WarehouseController : MonoBehaviour, ISavedBuilding
{
    public const string TypeName = "Хранилище";

    public static readonly List<WarehouseController> All = new List<WarehouseController>();

    static readonly int[] CapacityByLevel = { 100, 200, 320 };
    public const float RaidShare = 0.2f;     // сколько содержимого уносят враги, когда Хранилище разрушено

    // Порядок совпадает с RefinedKind (по номеру хранится склад)
    static readonly RefinedKind[] Kinds = { RefinedKind.Planks, RefinedKind.StonePlates, RefinedKind.Steel, RefinedKind.ReinforcedPlates,
                                            RefinedKind.TarredBeams, RefinedKind.Monolith };
    const int KindCount = 6;
    static readonly QualityTier[] Tiers = { QualityTier.Medium, QualityTier.Good, QualityTier.Best };
    static readonly string[] TierColors = { "#D0D0D0", "#80E0FF", "#FFD060" };

    public BuildingInstance Building { get; private set; }
    int Lvl => Building != null ? Mathf.Clamp(Building.level, 1, 3) - 1 : 0;

    public int Capacity => CapacityByLevel[Lvl];
    public bool Works => Building != null && Building.isActiveAndEnabled && !Building.IsRuined;

    // Принимать готовую продукцию мастерских (вместо Цитадели)
    public bool AcceptProduction { get; set; }

    // Содержимое: [вид, качество]
    readonly int[,] stock = new int[KindCount, 3];
    int incoming;   // сколько уже несут сюда (место под них занято)

    public int RefinedTotal
    {
        get
        {
            int n = 0;
            for (int k = 0; k < KindCount; k++)
                for (int q = 0; q < 3; q++) n += stock[k, q];
            return n;
        }
    }

    // Зелья отравления (их приносят из Зельеварни) занимают место наравне с материалами
    int potions;
    public int Potions => potions;
    public int Total => RefinedTotal + potions;

    public static int TotalPotions
    {
        get
        {
            int n = 0;
            foreach (var w in All) if (w != null && w.Works) n += w.potions;
            return n;
        }
    }

    /// <summary>Списать n зелий из одного Хранилища. source - его здание (оттуда понесут).</summary>
    public static bool TakePotions(int n, out BuildingInstance source)
    {
        source = null;
        foreach (var w in All)
        {
            if (w == null || !w.Works || w.potions < n) continue;
            w.potions -= n;
            source = w.Building;
            return true;
        }
        return false;
    }

    public bool AcceptPotions(int n)
    {
        if (!Works) return false;
        potions += n;
        return true;
    }

    public bool ReturnPotionsHere(int n)
    {
        if (!Works || Free < n) return false;
        potions += n;
        return true;
    }

    public int Incoming => incoming;
    public int Free => Mathf.Max(0, Capacity - Total - incoming);

    /// <summary>Свободное место во всех целых Хранилищах.</summary>
    public static int TotalFree
    {
        get
        {
            int n = 0;
            foreach (var w in All) if (w != null && w.Works) n += w.Free;
            return n;
        }
    }

    /// <summary>
    /// В Цитадели нет места под материал - кладём его в свободное место Хранилищ (сразу, без рейса:
    /// он уже лежит на складе, просто «в другом углу»). Возвращает, сколько разместили.
    /// </summary>
    public static int StoreOverflow(RefinedKind kind, QualityTier q, int amount)
    {
        int left = amount;
        foreach (var w in All)
        {
            if (left <= 0) break;
            if (w == null || !w.Works) continue;
            int put = Mathf.Min(left, w.Free);
            if (put <= 0) continue;
            w.Put(kind, q, put);
            left -= put;
        }
        return amount - left;
    }

    public int Get(RefinedKind kind, QualityTier q) { return stock[(int)kind, (int)q]; }

    /// <summary>Обновление 23 (трофеи): забрать из этого Хранилища до n штук материала. Возвращает, сколько взяли.</summary>
    public int Take(RefinedKind kind, QualityTier q, int n)
    {
        int m = Mathf.Clamp(stock[(int)kind, (int)q], 0, Mathf.Max(0, n));
        stock[(int)kind, (int)q] -= m;
        return m;
    }

    /// <summary>Обновление 23 (трофеи): забрать отсюда до n зелий отравления.</summary>
    public int TakePotionsHere(int n)
    {
        int m = Mathf.Clamp(potions, 0, Mathf.Max(0, n));
        potions -= m;
        return m;
    }

    // ---------- Сохранение: содержимое и галочка «принимать продукцию» ----------

    public void SaveState(SaveBag bag)
    {
        var items = new List<string[]>();
        for (int k = 0; k < KindCount; k++)
            for (int q = 0; q < 3; q++)
                if (stock[k, q] > 0) items.Add(new[] { SaveBag.I(k), SaveBag.I(q), SaveBag.I(stock[k, q]) });
        bag.SetList("wh.stock", items);
        bag.Set("wh.potions", potions);
        bag.Set("set.whAccept", AcceptProduction);
    }

    public void LoadState(SaveBag bag)
    {
        foreach (var a in bag.List("wh.stock"))
        {
            int k = SaveBag.At(a, 0), q = SaveBag.At(a, 1);
            if (k >= 0 && k < KindCount && q >= 0 && q < 3) stock[k, q] = Mathf.Max(0, SaveBag.At(a, 2));
        }
        potions = Mathf.Max(0, bag.Int("wh.potions"));
        AcceptProduction = bag.Bool("set.whAccept", AcceptProduction);
    }

    void Awake() { Building = GetComponent<BuildingInstance>(); }
    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Update()
    {
        if (Building == null) Building = GetComponent<BuildingInstance>();
    }

    // Здание снесли - содержимое возвращается в Цитадель, ничего не пропадает
    void OnDestroy()
    {
        TownHallStorage hall = TownHallStorage.Instance;
        if (hall == null) return;
        for (int k = 0; k < KindCount; k++)
            for (int q = 0; q < 3; q++)
            {
                int n = stock[k, q];
                if (n > 0) hall.Add(Kinds[k], Tiers[q], n);
                stock[k, q] = 0;
            }
        if (potions > 0 && ResourceManager.Instance != null) ResourceManager.Instance.AddPotions(potions);
        potions = 0;
    }

    // ---------- Общий запас (Цитадель + Хранилища) ----------

    // Сколько этого материала лежит во всех целых Хранилищах
    public static int TotalStored(RefinedKind kind, QualityTier q)
    {
        int n = 0;
        foreach (var w in All)
            if (w != null && w.Works) n += w.Get(kind, q);
        return n;
    }

    /// <summary>Взять до amount штук из Хранилищ. source - здание, откуда взяли (для рейса жителя).</summary>
    public static int Take(RefinedKind kind, QualityTier q, int amount, out BuildingInstance source)
    {
        source = null;
        int left = amount;
        foreach (var w in All)
        {
            if (left <= 0) break;
            if (w == null || !w.Works) continue;
            int have = w.Get(kind, q);
            int take = Mathf.Min(left, have);
            if (take <= 0) continue;
            w.stock[(int)kind, (int)q] -= take;
            left -= take;
            if (source == null) source = w.Building;
        }
        return amount - left;
    }

    // Куда нести готовую продукцию мастерской: ближайшее Хранилище, которое принимает продукцию и где есть место
    public static WarehouseController PickForOutput(Vector2 from, int amount)
    {
        WarehouseController best = null;
        float bestD = float.MaxValue;
        foreach (var w in All)
        {
            if (w == null || !w.Works || !w.AcceptProduction || w.Free < amount) continue;
            float d = w.Building.DistanceTo(from);
            if (d < bestD) { bestD = d; best = w; }
        }
        return best;
    }

    public void Reserve(int n) { incoming += n; }
    public void Unreserve(int n) { incoming = Mathf.Max(0, incoming - n); }

    // Продукцию доставили. false - Хранилище уже разрушено, нести надо в Цитадель.
    public bool Accept(RefinedKind kind, QualityTier q, int amount)
    {
        if (!Works) return false;
        Put(kind, q, amount);
        return true;
    }

    void Put(RefinedKind kind, QualityTier q, int amount)
    {
        if (amount > 0) stock[(int)kind, (int)q] += amount;
    }

    static void GiveToHall(RefinedKind kind, QualityTier q, int amount)
    {
        TownHallStorage hall = TownHallStorage.Instance;
        if (hall != null) hall.Add(kind, q, amount);
    }

    // ---------- Переносы руками ----------

    /// <summary>Из Цитадели в Хранилище. Материалы списываются сразу, житель несёт их из Цитадели. Возвращает, сколько отправили.</summary>
    public int RequestToWarehouse(RefinedKind kind, QualityTier q, int amount)
    {
        if (!Works) return 0;
        TownHallStorage hall = TownHallStorage.Instance;
        var pm = PopulationManager.Instance;
        BuildingInstance hallBld = pm != null ? pm.Hall : null;
        if (hall == null || hallBld == null) return 0;

        amount = Mathf.Min(amount, Mathf.Min(hall.Get(kind, q), Free));
        if (amount <= 0) return 0;
        if (!hall.TrySpend(kind, q, amount)) return 0;

        incoming += amount;
        int n = amount;
        int carry = CarryLedger.Add(Spoils.Refined(kind, q), n);   // выход из игры в пути - материалы в Цитадели

        Logistics.Request(new HaulJob
        {
            from = hallBld,
            to = Building,
            prefer = Building,
            onDeliver = () =>
            {
                CarryLedger.Done(carry);
                Unreserve(n);
                if (!Accept(kind, q, n)) GiveToHall(kind, q, n);   // пока несли, Хранилище разрушили
            },
            onAbort = loaded =>
            {
                CarryLedger.Done(carry);
                Unreserve(n);
                GiveToHall(kind, q, n);                            // рейс сорван - материалы назад в Цитадель
            }
        });
        return n;
    }

    /// <summary>Из Хранилища в Цитадель. Со склада списывается сразу, житель несёт материалы в Цитадель.</summary>
    public int RequestToHall(RefinedKind kind, QualityTier q, int amount)
    {
        if (!Works) return 0;
        TownHallStorage hall = TownHallStorage.Instance;
        var pm = PopulationManager.Instance;
        BuildingInstance hallBld = pm != null ? pm.Hall : null;
        if (hall == null || hallBld == null) return 0;

        // Не больше, чем влезет в Цитадель (лимит вида)
        amount = Mathf.Min(amount, Mathf.Min(Get(kind, q), StorageLimits.HallRefinedFree(kind)));
        if (amount <= 0) return 0;
        stock[(int)kind, (int)q] -= amount;
        int n = amount;
        int carry = CarryLedger.Add(Spoils.Refined(kind, q), n);

        Logistics.Request(new HaulJob
        {
            from = Building,
            to = hallBld,
            prefer = Building,
            onDeliver = () => { CarryLedger.Done(carry); GiveToHall(kind, q, n); },
            onAbort = loaded =>
            {
                CarryLedger.Done(carry);
                // Рейс сорван: если Хранилище цело - кладём обратно, иначе в Цитадель
                if (this != null && Works) Put(kind, q, n);
                else GiveToHall(kind, q, n);
            }
        });
        return n;
    }

    // Подсказка: на сколько это Хранилище поднимает лимиты казны (и что будет на следующем уровне)
    static string BonusTip(int lv)
    {
        var sb = new System.Text.StringBuilder("Хранилище поднимает лимиты казны Цитадели:\n");
        for (int i = 0; i < StorageLimits.ResCount; i++)
        {
            StoreRes r = (StoreRes)i;
            int now = StorageLimits.WarehouseBonusAt(r, lv);
            if (now <= 0) continue;
            string next = lv < 3 ? $"  (Ур. {lv + 1}: +{StorageLimits.WarehouseBonusAt(r, lv + 1)})" : "";
            sb.Append($" • {StorageLimits.Name(r)}: +{now}{next}\n");
        }
        sb.Append("Материалы (доски, плиты, сталь...) лежат в самом Хранилище: когда в Цитадели нет места, лишнее несут сюда.");
        return sb.ToString();
    }

    // ---------- Разграбление ----------

    /// <summary>Враги разрушили Хранилище: часть содержимого уносят (списывается сразу).</summary>
    public Loot Raid()
    {
        Loot l = new Loot();
        // Смоляной брус и монолит враги не уносят (слишком тяжёлые) - они и не разыгрываются. Раньше их тоже тянули
        // жребием и просто списывали: вещь пропадала. Обновление 23: остаются в Хранилище
        const int Carryable = 4;
        int total = 0;
        for (int k = 0; k < Carryable; k++)
            for (int q = 0; q < 3; q++) total += stock[k, q];
        if (total <= 0) return l;

        float exact = total * RaidShare;
        int take = Mathf.FloorToInt(exact);
        if (Random.value < exact - take) take++;

        for (int i = 0; i < take && total > 0; i++)
        {
            int r = Random.Range(0, total);
            bool picked = false;
            for (int k = 0; k < Carryable && !picked; k++)
            {
                for (int q = 0; q < 3; q++)
                {
                    int have = stock[k, q];
                    if (r >= have) { r -= have; continue; }

                    stock[k, q]--;
                    if (k == 0) l.planks[q]++;
                    else if (k == 1) l.plates[q]++;
                    else if (k == 2) l.steel[q]++;
                    else l.reinforced[q]++;
                    picked = true;
                    break;
                }
            }
            total--;
        }
        return l;
    }

    // ---------- Панель здания ----------

    int tab;               // 0 - что лежит здесь (отправить в Цитадель), 1 - склад Цитадели (отправить сюда)
    Vector2 scroll;

    struct Row { public RefinedKind kind; public QualityTier tier; public int count; }
    readonly List<Row> rows = new List<Row>();

    public Rect DrawPanel(float x, float y, float w)
    {
        BuildingInstance b = Building;
        int lvl = b.level;

        GUIStyle st = UiKit.Body;
        GUIStyle small = UiKit.Small;
        GUIStyle muted = UiKit.MutedStyle;

        const float rowH = 26f;
        const int viewRows = 5;
        float listH = viewRows * rowH;

        float statusY = UiKit.TitleH + 6f;
        float barY = statusY + 22f;
        float bonusY = barY + 16f;
        float problemY = bonusY + 20f;
        float acceptY = problemY + (b.isWorking ? 0f : 22f);
        float tabsY = acceptY + 28f;
        float hintY = tabsY + 30f;
        float listY = hintY + 20f;
        float upgradeY = listY + listH + 8f;
        float h = upgradeY + UpgradeRules.SectionHeight(b) + 8f;

        UiKit.Window(new Rect(x, y, w, h), $"{TypeName} · Ур. {lvl}");

        // --- Заполнение ---
        int total = Total;
        float frac = Capacity > 0 ? total / (float)Capacity : 0f;
        string transit = incoming > 0 ? UiKit.Col(UiKit.WarnHex, $"  · в пути сюда: {incoming}") : "";
        string potNote = potions > 0 ? UiKit.Col(UiKit.MutedHex, $"  · зелий: {potions}") : "";
        LocGUI.Label(new Rect(x + 10f, y + statusY, w - 20f, 20f), $"Заполнено: <b>{total}/{Capacity}</b>{transit}{potNote}", st);
        UiKit.Bar(new Rect(x + 10f, y + barY, w - 20f, 10f), frac,
            Color.Lerp(UiKit.Good, UiKit.Bad, Mathf.Clamp01((frac - 0.6f) / 0.4f)));
        UiKit.Tip(new Rect(x + 10f, y + statusY, w - 20f, 36f),
            "Хранилище держит доски, плиты, сталь, смоляной брус и монолит. На постройки и улучшения они списываются сами " +
            "(сначала из Цитадели, потом отсюда). Если Хранилище разрушат враги, они унесут 20% содержимого; " +
            "если снести его самому — всё вернётся в Цитадель.");

        // Что Хранилище даёт казне
        int lv = Mathf.Clamp(lvl, 1, 3);
        Rect bonusR = new Rect(x + 10f, y + bonusY, w - 20f, 20f);
        LocGUI.Label(bonusR, UiKit.Col(UiKit.MutedHex,
            $"Лимит казны: +{StorageLimits.WarehouseBonusAt(StoreRes.Gold, lv)} зол., +{StorageLimits.WarehouseBonusAt(StoreRes.Wood, lv)} дер./кам. и др."), small);
        UiKit.Tip(bonusR, BonusTip(lv));

        if (!b.isWorking)
            LocGUI.Label(new Rect(x + 10f, y + problemY, w - 20f, 20f), UiKit.Col(UiKit.BadHex, b.StatusText), small);

        // --- Приём продукции ---
        Rect accR = new Rect(x + 10f, y + acceptY, w - 20f, 24f);
        bool acc = LocGUI.Toggle(accR, AcceptProduction, " Принимать продукцию мастерских");
        UiKit.Tip(accR, "Готовые материалы мастерских понесут не в Цитадель, а в ближайшее Хранилище со свободным местом.");
        if (acc != AcceptProduction) AcceptProduction = acc;

        // --- Вкладки ---
        int newTab = UiKit.Tabs(new Rect(x + 10f, y + tabsY, w - 18f, 26f), new[] { "Здесь лежит", "Склад Цитадели" }, tab);
        if (newTab != tab) { tab = newTab; scroll = Vector2.zero; }

        LocGUI.Label(new Rect(x + 10f, y + hintY, w - 20f, 18f),
            tab == 0 ? "Отправить в Цитадель:" : "Отправить в Хранилище:", muted);

        // --- Строки: только то, что реально есть (нули не показываем) ---
        TownHallStorage hall = TownHallStorage.Instance;
        rows.Clear();
        foreach (RefinedKind k in Kinds)
            foreach (QualityTier q in Tiers)
            {
                int n = tab == 0 ? Get(k, q) : (hall != null ? hall.Get(k, q) : 0);
                if (n > 0) rows.Add(new Row { kind = k, tier = q, count = n });
            }

        bool canMove = Works;
        float innerW = w - 44f;
        scroll = UiKit.BeginScrollView(new Rect(x + 10f, y + listY, w - 20f, listH), scroll,
            new Rect(0f, 0f, innerW, Mathf.Max(listH, rows.Count * rowH)));

        if (rows.Count == 0)
            LocGUI.Label(new Rect(0f, 0f, innerW, 40f),
                tab == 0 ? "В Хранилище пока пусто" : "На складе Цитадели нет досок, плит и стали", muted);

        for (int i = 0; i < rows.Count; i++)
        {
            Row r = rows[i];
            float ry = i * rowH;
            string col = TierColors[(int)r.tier];
            float lead = ItemIcons.Lead(new Rect(0f, ry, 24f, rowH - 2f), ItemIcons.Refined(r.kind), (int)r.tier);
            LocGUI.Label(new Rect(lead, ry + 2f, innerW - 150f - lead, rowH - 2f),
                $"{UpgradeRules.KindLabel(r.kind)} <color={col}>({TownHallStorage.TierName(r.tier).ToLower()})</color>: <b>{r.count}</b>", small);

            // В Цитадель - не больше её лимита по этому виду
            bool hallFull = tab == 0 && StorageLimits.HallRefinedFree(r.kind) <= 0;
            bool rowEnabled = canMove && (tab == 0 ? !hallFull : Free > 0);
            string why = !canMove ? "Хранилище не работает"
                : (tab == 1 && Free <= 0 ? "В Хранилище нет места"
                : (hallFull ? $"В Цитадели нет места: {UpgradeRules.KindLabel(r.kind).ToLower()} — до {StorageLimits.RefinedCapOf(r.kind)} шт." : null));
            int amt = 0;
            if (UiKit.Button(new Rect(innerW - 148f, ry, 44f, rowH - 2f), "×1", rowEnabled, UiKit.Kind.Normal, why)) amt = 1;
            if (UiKit.Button(new Rect(innerW - 100f, ry, 44f, rowH - 2f), "×5", rowEnabled, UiKit.Kind.Normal, why)) amt = 5;
            if (UiKit.Button(new Rect(innerW - 52f, ry, 50f, rowH - 2f), "Все", rowEnabled, UiKit.Kind.Normal, why)) amt = r.count;

            if (amt > 0)
            {
                int sent = tab == 0 ? RequestToHall(r.kind, r.tier, amt) : RequestToWarehouse(r.kind, r.tier, amt);
                if (sent <= 0 && tab == 1) WarManager.Notify("Хранилище: нет места или некому нести", false);
                if (sent <= 0 && tab == 0) WarManager.Notify("Цитадель: нет места под этот материал или некому нести", false);
            }
        }
        GUI.EndScrollView();

        UpgradeRules.DrawUpgradeSection(x + 10f, y + upgradeY, w - 20f, b);
        return new Rect(x, y, w + 5f, h);
    }
}
