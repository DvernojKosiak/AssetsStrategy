using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Аванпост: расширяет зелёную зону застройки (см. Territory). Работает, только если:
///  - подключён к зоне (касается её или дороги, ведущей из зоны) и не разрушен;
///  - снабжается: на аванпосте есть припасы (еда) и связь с Цитаделью не прервана.
/// Снабжение:
///  - Аванпост постоянно ест припасы (FoodPerMinute). Когда их меньше RefillBelow, из Цитадели
///    приносят еду: жители (сначала работники Цитадели) или курьеры Курьерской, если их закрепили
///    за этим аванпостом (у курьеров груз больше, с упряжкой - ещё и быстрее).
///  - Связь с Цитаделью: аванпост не может долго жить без неё. Если подвоза из Цитадели не было
///    HallLinkTimeout секунд, аванпост заброшен (даже если еда есть). Поэтому из Цитадели иногда
///    везут и "проверочный" груз, даже когда склад полон.
///  - Ур.3: у аванпоста есть смотрители. Если в радиусе зоны аванпоста стоит ферма и на её
///    складе есть еда, смотрители носят еду оттуда (это не заменяет связь с Цитаделью).
///  - Еда кончилась: аванпост держится StarveGrace секунд, потом заброшен.
///  - Смотрители (рабочие аванпоста, KeepersByLevel): без них аванпост держится UnstaffedGrace секунд,
///    потом заброшен - так же, как без еды. Вернулся хоть один смотритель - снова работает.
///  - Ур.4: сторожевой аванпост - смотрители отстреливаются от врагов, как башня, только намного слабее
///    (числа - ArcherRange / ArcherDamage / ArcherCooldown). Разрушенный или заброшенный не стреляет.
///    В набеге на деревню игрока его аванпост Ур.4 стреляет так же (RaidBuilding.OutpostArcher).
/// Заброшенный аванпост не расширяет зону, пока его снова не снабдят.
/// Добавляется на здание в BuildingPlacer.
/// </summary>
public class OutpostController : MonoBehaviour, ISavedBuilding
{
    public const string TypeName = "Аванпост";

    public static readonly List<OutpostController> All = new List<OutpostController>();

    // ---------- Числа (можно менять) ----------
    public const int MaxLevel = 4;               // Ур.4 - сторожевой: отстреливается
    static readonly float[] RadiusByLevel = { 6f, 8f, 10f, 10f };
    static readonly int[] CapacityByLevel = { 40, 60, 80, 80 };          // сколько еды помещается
    static readonly float[] FoodPerMinuteByLevel = { 6f, 8f, 10f, 10f };  // сколько ест
    public const int HallParcel = 20;            // столько еды несёт житель из Цитадели
    public const int FarmParcel = 15;            // столько смотритель приносит с фермы
    public const float StarveGrace = 120f;       // столько держится без еды
    public const float HallLinkTimeout = 480f;   // столько держится без подвоза из Цитадели
    public const float UnstaffedGrace = 150f;    // столько держится без смотрителей
    static readonly int[] KeepersByLevel = { 1, 1, 2, 2 };           // сколько смотрителей нужно

    // Ур.4: стрельба. Башня Ур.1 - 7.6 урона раз в 1.1 с на 7 клеток; аванпост - втрое слабее и ближе
    public const int ArcherLevel = 4;
    public const float ArcherRange = 6f;
    public const float ArcherDamage = 4f;
    public const float ArcherCooldown = 1.8f;
    const float RefillBelow = 0.6f;              // ниже этой доли припасов - везём из Цитадели
    const float LinkRefreshAt = 0.6f;            // прошло столько от HallLinkTimeout - везём, даже если склад полон
    const float FarmRefillBelow = 0.9f;

    public static float RadiusFor(int level) { return RadiusByLevel[Mathf.Clamp(level, 1, RadiusByLevel.Length) - 1]; }
    public static int KeepersFor(int level) { return KeepersByLevel[Mathf.Clamp(level, 1, KeepersByLevel.Length) - 1]; }

    BuildingInstance bld;
    public BuildingInstance Building
    {
        get
        {
            if (bld == null) bld = GetComponent<BuildingInstance>();
            return bld;
        }
    }

    int Lvl => Building != null ? Mathf.Clamp(Building.level, 1, MaxLevel) : 1;

    // ---------- Состояние ----------
    public int Food { get; private set; }
    public int Capacity => CapacityByLevel[Lvl - 1];
    public float FoodPerMinute => FoodPerMinuteByLevel[Lvl - 1];

    bool inited;
    bool supplied = true;
    float eatAcc, starve, lastHallSupply, tick;
    float unstaffed;              // сколько секунд на аванпосте нет ни одного смотрителя
    int incoming;                 // еда, которую уже несут сюда (место под неё занято)
    bool hallRunPending, farmRunPending;
    float shootTimer;

    public int HallTrips { get; private set; }

    // Сохранение: припасы аванпоста (иначе после загрузки он начинал бы с половины)
    // Еда, которую уже несут на аванпост (sohranenie-1): после входа она уже на аванпосте
    int inHands;

    public void SaveState(SaveBag bag) { bag.Set("outpost.food", Food); bag.Set("outpost.hands", inHands > 0 ? SaveBag.I(inHands) : null); }

    public void LoadState(SaveBag bag)
    {
        if (!bag.Has("outpost.food")) return;
        inited = true;
        Food = Mathf.Clamp(bag.Int("outpost.food") + Mathf.Max(0, bag.Int("outpost.hands")), 0, Capacity);
        lastHallSupply = Time.time;
    }
    public int FarmTrips { get; private set; }

    public bool Supplied => supplied;
    public float SinceHallSupply => inited ? Time.time - lastHallSupply : 0f;
    public float HallLinkLeft => Mathf.Max(0f, HallLinkTimeout - SinceHallSupply);
    public float StarveLeft => Mathf.Max(0f, StarveGrace - starve);
    public float UnstaffedLeft => Mathf.Max(0f, UnstaffedGrace - unstaffed);
    public bool HasKeepers => unstaffed <= 0f;

    // Есть хоть один смотритель (пока менеджер жителей не посчитал рабочих - считаем, что есть)
    bool Staffed(BuildingInstance b) { return b.workersNeeded <= 0 || b.workers > 0; }

    // Почему аванпост заброшен (для сообщения и панели)
    string AbandonReason => Food <= 0 && starve >= StarveGrace ? "нет еды"
                          : unstaffed >= UnstaffedGrace ? "нет смотрителей"
                          : "нет подвоза из Цитадели";
    public bool LinkDue => SinceHallSupply > HallLinkTimeout * LinkRefreshAt;

    /// <summary>Пора везти еду из Цитадели (мало припасов или давно не было связи).</summary>
    public bool NeedsHallFood => Food + incoming < Capacity * RefillBelow || LinkDue;

    public bool Connected => Territory.Instance != null && Building != null && Territory.Instance.IsActive(Building);

    /// <summary>Аванпост сейчас расширяет зону (не руины, снабжается, подключён).</summary>
    public bool GivesZone => Building != null && !Building.IsRuined && supplied && Connected;

    string ZoneLostReason => Building != null && Building.IsRuined ? "аванпост разрушен"
                           : !supplied ? AbandonReason
                           : "нет дороги от зелёной зоны";

    /// <summary>Сколько секунд еды осталось (с учётом того, что уже съедено сверх целых единиц).</summary>
    float FoodLeftSeconds => FoodPerMinute > 0f ? Mathf.Max(0f, (Food - eatAcc) / FoodPerMinute * 60f) : 0f;

    /// <summary>
    /// Через сколько секунд аванпост забросят, если ничего больше не привезут: самый ранний из сроков -
    /// связь с Цитаделью, еда (+ время без еды), смотрители (если их сейчас нет). limit - что кончится первым.
    /// </summary>
    public float HoldsFor(out string limit)
    {
        float link = HallLinkLeft;
        float food = Food > 0 ? FoodLeftSeconds + StarveGrace : StarveLeft;
        float best = link;
        limit = "раньше всего прервётся связь с Цитаделью";
        if (food < best) { best = food; limit = Food > 0 ? "раньше всего кончится еда" : "еды уже нет"; }
        if (!HasKeepers && UnstaffedLeft < best) { best = UnstaffedLeft; limit = "нет смотрителей"; }
        return best;
    }

    /// <summary>Для Territory: аванпост снабжается (не аванпост - всегда true).</summary>
    public static bool IsSupplied(BuildingInstance b)
    {
        if (b == null) return false;
        OutpostController oc = b.GetComponent<OutpostController>();
        return oc == null || oc.supplied;
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    // ---------- Жизнь ----------

    void Update()
    {
        BuildingInstance b = Building;
        if (b == null || b.type == null) return;

        if (!inited)
        {
            inited = true;
            Food = Capacity / 2;          // новый аванпост строят с запасом на первое время
            lastHallSupply = Time.time;
        }

        float dt = Time.deltaTime;
        if (b.IsRuined) return;           // в руинах никто не ест и никто ничего не везёт

        // Расход припасов
        eatAcc += FoodPerMinute / 60f * dt;
        while (eatAcc >= 1f)
        {
            eatAcc -= 1f;
            if (Food > 0) Food--;
        }
        if (Food > 0) starve = 0f;
        else starve += dt;

        if (Staffed(b)) unstaffed = 0f;
        else unstaffed += dt;

        bool now = starve < StarveGrace && SinceHallSupply < HallLinkTimeout && unstaffed < UnstaffedGrace;
        if (now != supplied)
        {
            supplied = now;
            Territory.MarkDirty();
            string where = $"[{b.OriginCell.x},{b.OriginCell.y}]";
            if (supplied) WarManager.Notify($"Аванпост {where} снова работает — зона восстановлена", true);
            else if (AbandonReason == "нет еды") WarManager.Notify($"Аванпост {where} заброшен: кончилась еда", false);
            else if (AbandonReason == "нет смотрителей") WarManager.Notify($"Аванпост {where} заброшен: нет смотрителей", false);
            else WarManager.Notify($"Аванпост {where} заброшен: нет подвоза из Цитадели", false);
        }

        Shoot(dt);

        tick -= dt;
        if (tick > 0f) return;
        tick = 2f;
        RequestSupplies();
    }

    // ---------- Ур.4: отстреливается ----------

    /// <summary>Аванпост сейчас может стрелять: Ур.4, не в руинах, снабжается (смотрители на месте).</summary>
    public bool Shoots => Building != null && Building.level >= ArcherLevel && !Building.IsRuined && supplied;

    void Shoot(float dt)
    {
        if (!Shoots) return;
        shootTimer -= dt;
        if (shootTimer > 0f) return;

        Vector2 c = Building.Center;
        EnemyAgent target = EnemyAgent.TowerTarget(c, ArcherRange);   // Тяжёлый Рыцарь - первым, как у башни
        if (target == null) { shootTimer = 0.3f; return; }

        shootTimer = ArcherCooldown;
        CombatFx.Arrow(c, target.Position);
        target.TakeDamage(ArcherDamage * TowerController.DefenseBalance, null, Building);
    }

    // Круг дальности (виден, пока выбран аванпост Ур.4)
    LineRenderer circle;

    void LateUpdate()
    {
        BuildingInstance b = Building;
        bool show = b != null && b.level >= ArcherLevel && !b.IsRuined && BuildingPlacer.Selected == b;
        if (!show) { if (circle != null) circle.enabled = false; return; }

        if (circle == null)
        {
            GameObject go = new GameObject("OutpostRange");
            circle = go.AddComponent<LineRenderer>();
            circle.useWorldSpace = true;
            circle.loop = true;
            circle.material = new Material(Shader.Find("Sprites/Default"));
            circle.widthMultiplier = 0.06f;
            circle.startColor = circle.endColor = new Color(1f, 0.75f, 0.35f, 0.45f);
            circle.sortingOrder = -40;
            circle.positionCount = 64;
        }
        circle.enabled = true;
        Vector2 c = b.Center;
        for (int i = 0; i < 64; i++)
        {
            float a = i * Mathf.PI * 2f / 64;
            circle.SetPosition(i, new Vector3(c.x + Mathf.Cos(a) * ArcherRange, c.y + Mathf.Sin(a) * ArcherRange, 0f));
        }
    }

    void OnDestroy()
    {
        if (circle != null) Destroy(circle.gameObject);
    }

    void RequestSupplies()
    {
        // Из Цитадели - жители (если курьеров за аванпостом не закрепили или связь вот-вот прервётся)
        bool couriers = CourierHouse.AnyAssigned(Building);
        if (!hallRunPending && NeedsHallFood && (!couriers || HallLinkLeft < 60f || (Food <= 0 && starve > 30f)))
        {
            hallRunPending = true;
            Logistics.Request(MakeHallJob(HallParcel, () => hallRunPending = false));
        }

        // Ур.3: смотрители носят еду с фермы в радиусе аванпоста
        if (Lvl >= 3 && !farmRunPending && Food + incoming < Capacity * FarmRefillBelow) TryFarmRun();
    }

    // ---------- Подвоз из Цитадели ----------

    /// <summary>
    /// Рейс "Цитадель -> аванпост". parcel - сколько еды берут (не больше свободного места).
    /// Используют жители (Logistics) и курьеры Курьерской. done - вызывается в конце рейса (успех или срыв).
    /// </summary>
    public HaulJob MakeHallJob(int parcel, System.Action done)
    {
        var pm = PopulationManager.Instance;
        BuildingInstance hall = pm != null ? pm.Hall : null;
        int want = Mathf.Clamp(Capacity - Food - incoming, 0, parcel);   // 0 - "проверочный" рейс: связь есть, места нет
        if (AccumulationMode.On) want = 0;   // режим накопления: еду из Цитадели не возим
        incoming += want;
        int carried = 0;

        return new HaulJob
        {
            from = hall,
            to = Building,
            prefer = hall,
            onPickup = () =>
            {
                var rm = ResourceManager.Instance;
                carried = rm != null ? Mathf.Min(want, rm.food) : 0;
                if (carried > 0) rm.Spend(0, 0, 0, carried);
                inHands += carried;
            },
            onDeliver = () =>
            {
                inHands = Mathf.Max(0, inHands - carried);
                incoming = Mathf.Max(0, incoming - want);
                Food = Mathf.Min(Capacity, Food + carried);
                lastHallSupply = Time.time;
                HallTrips++;
                if (done != null) done();
            },
            onAbort = loaded =>
            {
                if (loaded) inHands = Mathf.Max(0, inHands - carried);
                incoming = Mathf.Max(0, incoming - want);
                if (loaded && carried > 0 && ResourceManager.Instance != null) ResourceManager.Instance.Add(0, 0, 0, carried);
                if (done != null) done();
            }
        };
    }

    // ---------- Подвоз с фермы (Ур.3) ----------

    /// <summary>Ферма в радиусе аванпоста, на складе которой есть еда (ближайшая).</summary>
    public BuildingInstance FarmInReach(int minFood = 5)
    {
        BuildingInstance b = Building;
        if (b == null) return null;
        float r = RadiusFor(Lvl);
        BuildingInstance best = null;
        float bestD = float.MaxValue;
        foreach (var f in BuildingInstance.All)
        {
            if (f == null || f.type == null || f.type.prodFood <= 0 || f.IsRuined || !f.isActiveAndEnabled) continue;
            if (f.storedFood < minFood) continue;
            float d = f.DistanceTo(b.Center);
            if (d > r || d >= bestD) continue;
            bestD = d;
            best = f;
        }
        return best;
    }

    void TryFarmRun()
    {
        BuildingInstance farm = FarmInReach();
        if (farm == null) return;
        CitizenAgent keeper = FreeKeeper();
        if (keeper == null) return;

        int want = Mathf.Clamp(Capacity - Food - incoming, 0, FarmParcel);
        if (want <= 0) return;
        incoming += want;
        int carried = 0;
        farmRunPending = true;

        var job = new HaulJob
        {
            from = farm,
            to = Building,
            prefer = Building,
            onPickup = () =>
            {
                carried = farm != null ? Mathf.Min(want, farm.storedFood) : 0;
                if (carried > 0) farm.storedFood -= carried;
                inHands += carried;
            },
            onDeliver = () =>
            {
                inHands = Mathf.Max(0, inHands - carried);
                incoming = Mathf.Max(0, incoming - want);
                Food = Mathf.Min(Capacity, Food + carried);
                FarmTrips++;
                farmRunPending = false;
            },
            onAbort = loaded =>
            {
                if (loaded) inHands = Mathf.Max(0, inHands - carried);
                incoming = Mathf.Max(0, incoming - want);
                if (loaded && carried > 0)
                {
                    if (farm != null && !farm.IsRuined) farm.storedFood += carried;
                    else if (ResourceManager.Instance != null) ResourceManager.Instance.Add(0, 0, 0, carried);
                }
                farmRunPending = false;
            }
        };

        if (!keeper.StartHaul(job))
        {
            incoming = Mathf.Max(0, incoming - want);
            farmRunPending = false;
        }
    }

    // Свободный смотритель (рабочий этого аванпоста)
    CitizenAgent FreeKeeper()
    {
        GameClock clock = GameClock.Instance;
        bool workTime = clock == null || clock.IsWorkTime;
        foreach (var a in CitizenAgent.All)
        {
            if (a == null || a.IsDead || a.IsMilitary || !ReferenceEquals(a.Workplace, Building)) continue;
            if (a.IsCourier || a.IsHauling || a.IsHealerBusy || a.IsGathering || a.IsLyingInBed || a.IsCartDriver || a.IsPanicking) continue;
            if (!workTime && !a.IsSkeleton) continue;
            if (a.HpFraction < 0.4f) continue;
            return a;
        }
        return null;
    }

    // ---------- Панель ----------

    static string Clock(float s)
    {
        int t = Mathf.Max(0, Mathf.CeilToInt(s));
        return $"{t / 60}:{t % 60:00}";
    }

    public Rect DrawPanel(float x, float y, float w)
    {
        BuildingInstance b = Building;
        int lvl = b.level;
        float cx = x + UiKit.Pad, cw = w - UiKit.Pad * 2f;

        // --- Состояние: коротко + пояснение в подсказке ---
        string state, stateTip;
        if (b.IsRuined) { state = UiKit.Col(UiKit.BadHex, "Разрушен"); stateTip = "Территория не расширяется, пока аванпост не восстановят."; }
        else if (!supplied)
        {
            string why = AbandonReason;
            state = UiKit.Col(UiKit.BadHex, "Заброшен: " + why);
            stateTip = why == "нет смотрителей"
                ? "Зона не расширяется, пока на аванпост не придёт хоть один смотритель (рабочий). Дайте ему рабочих кнопкой «+» или «Авто»."
                : "Зона не расширяется, пока аванпост не снабжают едой из Цитадели.";
        }
        else if (Connected) { state = UiKit.Col(UiKit.GoodHex, $"Работает · зона +{RadiusFor(lvl):F0} кл."); stateTip = "Аванпост расширяет зелёную зону застройки вокруг себя."; }
        else { state = UiKit.Col(UiKit.BadHex, "Нет дороги от зелёной зоны"); stateTip = "Проведите дорогу от зелёной зоны к аванпосту — иначе территория не расширяется."; }

        // --- Подробности (в свёрнутом блоке) ---
        bool couriers = CourierHouse.AnyAssigned(b);
        string who = couriers ? "Из Цитадели возят курьеры Курьерской" : "Из Цитадели носят жители (сначала работники Цитадели)";
        string farmLine;
        if (lvl < 3) farmLine = UiKit.Col(UiKit.MutedHex, "Ур. 3: смотрители ещё и носят еду с фермы в радиусе аванпоста");
        else
        {
            BuildingInstance farm = FarmInReach(0);
            farmLine = farm != null
                ? $"Ферма рядом [{farm.OriginCell.x},{farm.OriginCell.y}]: {farm.storedFood} еды — смотрители носят оттуда"
                : UiKit.Col(UiKit.WarnHex, "В радиусе нет фермы — вся еда только из Цитадели");
        }
        string details = $"{who}.\nРейсов из Цитадели: {HallTrips}" + (lvl >= 3 ? $", с фермы: {FarmTrips}" : "") +
                         $"\nПодвоз был {Clock(SinceHallSupply)} назад.\n{farmLine}\n" +
                         UiKit.Col(UiKit.MutedHex, "Закрепить курьеров: панель Курьерской → строка аванпоста.");
        bool open = UiKit.IsOpen("outpost-more");
        float detailsH = open ? UiKit.TextH(details, cw, UiKit.Small) + 4f : 0f;

        bool keepers = PopulationManager.Instance != null && b.workersNeeded > 0;
        float stateY = UiKit.TitleH + 6f;
        float barY = stateY + UiKit.RowH + 2f;
        float linkY = barY + 24f;
        float staffY = linkY + UiKit.RowH + 2f;
        float guardY = staffY + UiKit.RowH + 2f;
        float foldY = guardY + UiKit.RowH + 2f;
        float workersY = foldY + UiKit.FoldH + detailsH + 4f;
        float upgradeY = workersY + (keepers ? 32f : 0f);
        float h = upgradeY + UiKit.SectionH + UpgradeRules.SectionHeight(b) + 8f;
        UiKit.Window(new Rect(x, y, w, h), $"{TypeName}  {UiKit.Col(UiKit.MutedHex, "· Ур. " + lvl)}");

        Rect sr = new Rect(cx, y + stateY, cw, UiKit.RowH);
        LocGUI.Label(sr, state);
        UiKit.Tip(sr, stateTip);

        // --- Припасы ---
        float frac = Capacity > 0 ? Food / (float)Capacity : 0f;
        float minutesLeft = FoodPerMinute > 0f ? Food / FoodPerMinute : 0f;
        string barText = Food > 0
            ? $"Еда {Food}/{Capacity} · ~{minutesLeft:F0} мин"
            : $"Нет еды! Продержится {Clock(StarveLeft)}";
        if (incoming > 0) barText += $" · везут {incoming}";
        Rect bar = new Rect(cx, y + barY, cw, 18f);
        UiKit.Bar(bar, frac, Color.Lerp(UiKit.Bad, UiKit.Good, frac), barText);
        UiKit.Tip(bar, $"Припасы аванпоста: {Food}/{Capacity} еды. Ест {FoodPerMinute:F0} в минуту.");

        // --- Сколько ещё продержится зона ---
        // Раньше здесь был только таймер связи с Цитаделью: он мог показывать 8 минут, когда зона уже пропала
        // (кончилась еда, ушли смотрители, нет дороги, руины). Теперь - честно: самый ранний из сроков
        // или прямо «зона пропала» с причиной.
        Rect lr = new Rect(cx, y + linkY, cw, UiKit.RowH);
        if (!GivesZone)
        {
            UiKit.Row(lr, "Зона", UiKit.Col(UiKit.BadHex, "пропала — " + ZoneLostReason));
            UiKit.Tip(lr, $"Аванпост сейчас не расширяет зелёную зону. Подвоз из Цитадели был {Clock(SinceHallSupply)} назад.");
        }
        else
        {
            float left = HoldsFor(out string limit);
            string linkCol = left < 90f ? UiKit.BadHex : (left < HallLinkTimeout * (1f - LinkRefreshAt) ? UiKit.WarnHex : UiKit.GoodHex);
            UiKit.Row(lr, "Без подвоза продержится", UiKit.Col(linkCol, Clock(left)));
            UiKit.Tip(lr, $"Столько зона продержится, если еду больше не принесут ({limit}). " +
                          $"Связь с Цитаделью: подвоз был {Clock(SinceHallSupply)} назад, без него — не больше {Clock(HallLinkTimeout)}. " +
                          $"Еды хватит на {Clock(FoodLeftSeconds)}, потом ещё {Clock(StarveGrace)} без еды.");
        }

        // --- Смотрители ---
        string staffVal;
        if (b.workersNeeded <= 0) staffVal = UiKit.Col(UiKit.MutedHex, "—");
        else if (b.workers > 0) staffVal = UiKit.Col(UiKit.GoodHex, $"{b.workers}/{b.workersNeeded}");
        else staffVal = UiKit.Col(UiKit.BadHex, unstaffed < UnstaffedGrace ? $"нет! продержится {Clock(UnstaffedLeft)}" : "нет");
        Rect kr = new Rect(cx, y + staffY, cw, UiKit.RowH);
        UiKit.Row(kr, "Смотрители", staffVal);
        UiKit.Tip(kr, $"Без смотрителей аванпост держится {Clock(UnstaffedGrace)}, потом заброшен — как без еды. " +
                      "Нужен хотя бы один." + (lvl >= 3 ? " На Ур.3 смотрители ещё носят еду с фермы рядом." : ""));

        // --- Оборона (Ур.4) ---
        string guardVal;
        if (lvl < ArcherLevel) guardVal = UiKit.Col(UiKit.MutedHex, $"с Ур. {ArcherLevel}");
        else if (Shoots) guardVal = UiKit.Col(UiKit.GoodHex, $"стреляет · {ArcherRange:F0} кл.");
        else guardVal = UiKit.Col(UiKit.BadHex, b.IsRuined ? "разрушен" : "заброшен — не стреляет");
        Rect gr = new Rect(cx, y + guardY, cw, UiKit.RowH);
        UiKit.Row(gr, "Оборона", guardVal);
        UiKit.Tip(gr, $"Ур. {ArcherLevel}: смотрители отстреливаются от врагов в радиусе {ArcherRange:F0} кл., как башня, " +
                      $"только намного слабее ({ArcherDamage * TowerController.DefenseBalance:F1} урона раз в {ArcherCooldown:F1} с). " +
                      "Заброшенный или разрушенный аванпост не стреляет. В набеге на вашу деревню — тоже стреляет.");

        if (UiKit.Fold(new Rect(cx, y + foldY, cw, UiKit.FoldH), "outpost-more", "Снабжение"))
            LocGUI.Label(new Rect(cx, y + foldY + UiKit.FoldH, cw, detailsH), details, UiKit.Small);

        if (keepers) PopulationManager.Instance.DrawWorkersRow(cx, y + workersY, b);

        float uy = UiKit.Section(cx, y + upgradeY, cw, "Улучшение");
        UpgradeRules.DrawUpgradeSection(cx, uy, cw, b);
        return new Rect(x, y, w + 5f, h);
    }
}
