using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Набеги на соседние деревни (пока - боты).
///  1. Кнопка "В бой" справа внизу: выбираем деревню (разведданные: защитники, башни, стены, богатство)
///     и отряды из казарм. Припасы: еда на каждого бойца (запас похода, остаток вернётся домой)
///     и дерево на лагерь (тратится).
///  2. Бойцы идут к краю нашей карты и уходят. Дорога занимает время (зависит от деревни).
///  3. Прибыли: камера переносится на карту набега. Видно чужую деревню - решаем: атаковать или уйти.
///  4. Бой: отряды сами штурмуют (бьют защитников, ломают здания, пробивают стены) или выполняют приказы:
///     ЛКМ по земле - идти и держать точку (бьют всех по дороге; двойной клик - марш, ни на что не отвлекаясь), ЛКМ по зданию - ломать его. Разрушенное здание отдаёт склад
///     в мешки бойцов рядом (пеший уносит CarryFoot, конный - CarryMounted). Лишнее остаётся на руинах.
///  5. Отступление: бойцы уходят через западный край карты. Кто погиб - погиб и дома, его мешок потерян.
///  6. Дорога назад, бойцы появляются у края нашей карты и сами несут добычу в казарму / Цитадель.
/// Пока армия в походе, деревня остаётся без неё - набеги на нас никто не отменял.
/// Клавиша V - переключить камеру: деревня / поле боя.
/// Деревня игрока, а он в игре (живой бой, см. HomeSiege): армия входит прямо в его деревню, бой считает его игра.
/// Здесь - его деревня на тех же клетках и «куклы» бойцов (RaidUnit.Puppet) по снимкам SiegeFrame; приказы
/// (штурм, держать точку, ломать здание, отступить) уходят к нему через сервер. Его нет в игре - бой как раньше, на копии.
/// </summary>
public partial class RaidManager : MonoBehaviour
{
    public static RaidManager Instance { get; private set; }

    public enum Phase { Idle, Marching, TravelOut, Scouting, Battle, Leaving, TravelHome }

    // ---------- Числа (можно менять) ----------
    public const int FoodMinutes = 24;         // final-13-fix-3: было 8 еды на бойца - хватало на 8 минут, а бой идёт 12         // запас на столько минут: дорога туда-обратно + 12 минут боя

    /// <summary>
    /// final-13-fix-3: сколько еды боец съедает в минуту похода: пеший - 1, крупный (занимает 2+ места в казарме) - по числу мест,
    /// всадник - ещё 1 за коня. Запас похода = сумма × FoodMinutes.
    /// </summary>
    public static int Ration(CitizenAgent a)
    {
        if (a == null) return 1;
        UnitType u = a.ExpUnit;
        return Mathf.Max(1, u != null ? u.spaceOccupied : 1) + (a.HasHorse ? 1 : 0);
    }

    int PickedRations()
    {
        int n = 0;
        foreach (var sq in picked) if (sq != null && !sq.OnExpedition) foreach (var m in SoldiersOf(sq)) n += Ration(m);
        return n;
    }

    static int UnitEats(RaidUnit u) { return u.IsGolem ? 0 : (u.Source != null ? Ration(u.Source) : 1) + Mathf.Max(0, u.Heads - 1); }
    public const int CampWood = 20;            // дерево на лагерь у места высадки
    public const int CarryFoot = 300;          // сколько добычи уносит пеший
    public const int CarryMounted = 600;       // ... и конный
    const float EatInterval = 60f;             // раз в столько секунд каждый боец съедает 1 еду из запаса
    const float HungerPerSecond = 0.4f;        // запас кончился - здоровье тает (не ниже четверти)
    const float CampRadius = 4.5f, CampCover = 1.6f;
    const float CampHealPerSecond = 3f, CampHpPerFood = 10f;
    const float RecoverSeconds = 600f;         // столько деревня восстанавливается после набега
    const float MarchTimeout = 300f;   // только страховка: у бойцов свой таймаут по длине пути

    // ---------- Разведка ----------
    public const string ScoutUnitName = "Разведчик";
    const float ScoutSeconds = 8f;                     // сколько разведчик осматривает деревню на месте
    public static int ScoutCost(RaidTarget t) { return 15 + 10 * t.Level; }   // золото за разведку

    public Phase State { get; private set; } = Phase.Idle;
    // Бой идёт (башни стреляют, защитники дерутся) - только если мы напали. Ушли из разведки - никто не тронет.
    public bool BattleOn => fought && !live && (State == Phase.Battle || State == Phase.Leaving);   // живой бой считает игра защитника
    bool fought;
    public static bool ViewingBattle => Instance != null && Instance.viewBattle && RaidMap.Instance != null;

    readonly List<RaidTarget> targets = new List<RaidTarget>();
    RaidTarget current;

    // ---------- Набеги на игроков (в аккаунте) ----------
    bool online;
    bool searching, launching;
    string findError = "";
    float battleDeadline;                                    // к этому времени бой у деревни игрока заканчивается
    BotVillage.Plan arrivedPlan;                             // план деревни на момент прибытия (для подсчёта добычи)
    readonly List<string> killedNames = new List<string>(); // погибшие защитники (по виду)
    bool reported;                                           // итог набега уже отправлен серверу
    public static bool Online => Instance != null && Instance.online;
    /// <summary>Сколько секунд осталось до конца набега на деревню игрока.</summary>
    public float BattleTimeLeft => Mathf.Max(0f, battleDeadline - Time.time);

    // Осмотр перед штурмом (final-6): пока не нажали «Атаковать», наших не бьют - ни часовые, ни башни, ни ловушки.
    // Ударить первыми злые соседи могут только после ScoutGrace секунд стояния у ворот (и только они).
    public const float ScoutGrace = 90f;
    float scoutSince;
    public float WaitSeconds => State == Phase.Scouting ? Time.time - scoutSince : 0f;
    /// <summary>Наши у деревни, штурм ещё не начат - урон по ним не проходит.</summary>
    public bool ScoutSafe => State == Phase.Scouting;
    readonly List<Squad> squads = new List<Squad>();
    readonly List<CitizenAgent> members = new List<CitizenAgent>();
    readonly Dictionary<CitizenAgent, Loot> survivors = new Dictionary<CitizenAgent, Loot>();

    // ---------- Трофеи: сложные вещи из разрушенных зданий игрока (Spoils, обновление 23) ----------
    int spoilBudget;                                                  // сколько очков трофеев ещё можно взять в этом набеге
    bool spoilCapTold;
    readonly SpoilBundle liveStolen = new SpoilBundle();              // живой бой: что унесли наши бойцы (по снимку защитника)
    readonly SpoilBundle liveCopies = new SpoilBundle();
    string spoilsReport = "";                                         // строка для отчёта о набеге
    SpoilBundle spoilsIcons = new SpoilBundle();                      // те же трофеи - картинками в отчёте (final-1)

    // ---------- Живой бой: в деревне защитника, его игра считает бой ----------
    const int GolemNetId = 9999;
    const float PullEvery = 0.2f;

    // Плавность: снимки приходят рывками (сеть), поэтому бойцов показываем чуть «в прошлом» (NetDelay)
    // и ведём между двумя снимками. Часы защитника переводим в наши (netOffset - наименьшая задержка).
    const float NetDelay = 0.4f;
    static float netOffset = float.MaxValue;
    int lastSeq;
    /// <summary>Момент (наши часы, Time.unscaledTime), в котором сейчас показываем живой бой.</summary>
    public static float NetRenderTime => Time.unscaledTime - NetDelay;

    static float NetStamp(SiegeFrame f)
    {
        float now = Time.unscaledTime;
        if (f.t <= 0f) return now;   // старый сервер не передал время - как пришло
        float off = now - f.t;
        if (netOffset == float.MaxValue || off < netOffset) netOffset = off;
        else netOffset = Mathf.MoveTowards(netOffset, off, 0.01f);   // часы чуть разошлись - подтягиваемся
        return f.t + netOffset;
    }
    bool live;                 // бой идёт у него вживую
    bool arriving;             // армия у деревни: ждём, примет ли он бой у себя
    bool liveDecided;          // сервер сказал: вживую - ждём первый снимок
    float arriveWait, pullTimer;
    bool pullInFlight;
    int pullFails;
    readonly Dictionary<int, CitizenAgent> armyIds = new Dictionary<int, CitizenAgent>();
    readonly Dictionary<int, RaidUnit> netAttackers = new Dictionary<int, RaidUnit>();
    readonly Dictionary<int, RaidUnit> netDefenders = new Dictionary<int, RaidUnit>();
    readonly Dictionary<int, RaidLoot> lastCarried = new Dictionary<int, RaidLoot>();
    readonly HashSet<int> goneSeen = new HashSet<int>();
    readonly List<BattleCommand> outbox = new List<BattleCommand>();
    int liveKilled;
    string liveStatus = "";
    /// <summary>Бой идёт вживую в деревне игрока (заслоны ставятся приказом - SendPost, ловушки ставить нельзя).</summary>
    public bool Live => live;

    // Укрепления живого боя: картинки по снимку (номер -> укрепление) и наши приказы, которых ещё нет в снимке
    readonly Dictionary<int, FieldPost> netPosts = new Dictionary<int, FieldPost>();
    readonly Dictionary<int, float> netPostHp = new Dictionary<int, float>();
    class PendingPost { public PostKind kind; public FieldGear.Item item; public float at; }
    readonly Dictionary<int, PendingPost> pendingPosts = new Dictionary<int, PendingPost>();
    int postSeq;
    bool postsPrimed;   // первый снимок уже был (о его укреплениях не уведомляем)
    const float PostOrderTimeout = 15f;   // столько ждём, что защитник поставил укрепление; нет - вещь обратно

    /// <summary>
    /// Живой бой: поставить укрепление у защитника (его игра строит его, мы видим по снимку).
    /// item - вещь из набора похода (уже взята); null - быстрый заслон (дерево уже заплачено).
    /// </summary>
    public void SendPost(PostKind kind, Vector2 world, float angle, FieldGear.Item item)
    {
        FieldPost.Spec s = FieldPost.Specs[(int)kind];
        int id = ++postSeq;
        outbox.Add(SiegePosts.Command(kind, NetPos(world), RaidMap.RealAngle(angle), item != null ? item.strength : 1f, item != null ? item.pool : -1f, id));
        pullTimer = Mathf.Min(pullTimer, 0.05f);
        pendingPosts[id] = new PendingPost { kind = kind, item = item, at = Time.time };
        WarManager.Notify($"{s.name}: приказ отправлен — бойцы закладывают его в деревне {(current != null ? current.Name : "")}, ~{s.buildSeconds:F0} с (пока рядом нет защитников)", true);
    }

    // Приказ так и не исполнили (место заняли, связь) - вещь обратно в набор, дерево - в казну
    void ExpirePendingPosts()
    {
        if (pendingPosts.Count == 0) return;
        foreach (var id in new List<int>(pendingPosts.Keys))
        {
            PendingPost pp = pendingPosts[id];
            if (Time.time - pp.at < PostOrderTimeout) continue;
            pendingPosts.Remove(id);
            FieldPost.Spec s = FieldPost.Specs[(int)pp.kind];
            if (pp.item != null) ReturnKitItem(pp.item);
            else if (ResourceManager.Instance != null) ResourceManager.Instance.Add(0, s.wood, 0, 0);
            WarManager.Notify($"{s.name}: не удалось поставить (место заняли) — вернулось в запас похода", false);
        }
    }

    // Наши ловушки в живом бою: картинки по снимку и приказы, которых ещё нет в снимке
    readonly Dictionary<int, Trap> netTraps = new Dictionary<int, Trap>();
    readonly Dictionary<int, float> netTrapProgress = new Dictionary<int, float>();
    readonly Dictionary<int, TrapKit> pendingTraps = new Dictionary<int, TrapKit>();
    readonly Dictionary<int, float> pendingTrapAt = new Dictionary<int, float>();

    /// <summary>Живой бой: поставить боевую ловушку у защитника (kit уже взят из набора похода).</summary>
    public void SendTrap(TrapKit kit, Vector2 world)
    {
        int id = ++postSeq;
        outbox.Add(SiegePosts.TrapCommand(kit, NetPos(world), id));
        pullTimer = Mathf.Min(pullTimer, 0.05f);
        pendingTraps[id] = kit;
        pendingTrapAt[id] = Time.time;
        WarManager.Notify($"{kit.Name}: приказ отправлен — ставят, ~{kit.Stats.installSeconds:F0} с (пока рядом нет защитников). Пока ставят — хрупкая!", true);
    }

    void ApplyTraps(SiegeFrame f)
    {
        var seen = new HashSet<int>();
        foreach (var d in f.defenders)
        {
            if (!SiegePosts.IsTrap(d) || !SiegePosts.DecodeTrap(d.unit, out TrapKit kit, out float progress, out bool rearming)) continue;
            seen.Add(d.id);
            if (!netTraps.TryGetValue(d.id, out Trap t) || t == null || !t.Alive)
            {
                t = Trap.CreatePuppet(kit, LivePos(d.x, d.y), d.id);
                netTraps[d.id] = t;
                pendingTraps.Remove(d.id);
                pendingTrapAt.Remove(d.id);
            }
            t.SetNet(progress, kit.charges, rearming);
            netTrapProgress[d.id] = progress;
        }
        foreach (var id in new List<int>(netTraps.Keys))
        {
            if (seen.Contains(id)) continue;
            Trap t = netTraps[id];
            bool broken = netTrapProgress.TryGetValue(id, out float pr) && pr < 1f;   // не достроили - растоптали
            if (t != null && t.Alive)
            {
                if (broken) WarManager.Notify($"Защитники растоптали ловушку: {t.Kit.Name}", false);
                t.NetRemove(broken);
            }
            netTraps.Remove(id);
            netTrapProgress.Remove(id);
        }
        // Приказ не исполнили (место заняли) - ловушка обратно в набор
        foreach (var id in new List<int>(pendingTraps.Keys))
        {
            if (Time.time - pendingTrapAt[id] < PostOrderTimeout) continue;
            TrapStock.ReturnToRaidKit(pendingTraps[id]);
            WarManager.Notify($"{pendingTraps[id].Name}: не удалось поставить — вернулась в набор похода", false);
            pendingTraps.Remove(id);
            pendingTrapAt.Remove(id);
        }
    }

    // Укрепления из снимка: создать картинку, обновить, убрать пропавшие
    void ApplyPosts(SiegeFrame f)
    {
        ApplyTraps(f);
        var seen = new HashSet<int>();
        foreach (var d in f.defenders)
        {
            if (!SiegePosts.Is(d)) continue;
            if (!SiegePosts.Decode(d.unit, out PostKind kind, out float angle, out float progress, out float strength, out bool mine)) continue;
            seen.Add(d.id);
            angle = RaidMap.WorldAngleOfReal(angle);   // угол - в клетках защитника
            Vector2 p = LivePos(d.x, d.y);
            if (!netPosts.TryGetValue(d.id, out FieldPost fp) || fp == null || !fp.Alive)
            {
                fp = FieldPost.CreatePuppet(kind, p, angle, strength, !mine, d.id);
                netPosts[d.id] = fp;
                if (mine) pendingPosts.Remove(d.id);
                else if (postsPrimed) WarManager.Notify($"{current?.Name} ставит: {fp.S.name}", false);
            }
            fp.SetNet(p, angle, progress, d.hp);
            netPostHp[d.id] = fp.Built ? d.hp : 0f;
        }
        foreach (var id in new List<int>(netPosts.Keys))
        {
            if (seen.Contains(id)) continue;
            FieldPost fp = netPosts[id];
            bool broken = !netPostHp.TryGetValue(id, out float hp) || hp < 0.6f;
            if (fp != null && fp.Alive)
            {
                if (broken) WarManager.Notify(fp.Enemy ? $"Разрушено укрепление защитника: {fp.S.name}" : $"Защитники разрушили: {fp.S.name}", fp.Enemy);
                fp.NetRemove(broken);
            }
            netPosts.Remove(id);
            netPostHp.Remove(id);
        }
        postsPrimed = true;
        ExpirePendingPosts();
    }

    int food;
    int woodPaid;
    bool campPaid;
    float eatTimer, campFoodAcc, timer, pickupTimer;
    Vector2 homeEdge, campPos, landing;
    int landSide;   // откуда подошли к деревне: 0 - слева, 1 - справа, 2 - снизу, 3 - сверху (RaidMap.Side)
    GameObject campGo;

    public bool HasCamp => campPaid && campGo != null;
    public Vector2 CampPosition => campPos;

    // Разведчики в пути и принесённые карты
    class ScoutMission
    {
        public CitizenAgent agent;
        public RaidTarget target;
        public Vector2 edge;
        public bool away;
        public float timer;
    }
    readonly List<ScoutMission> missions = new List<ScoutMission>();
    readonly Dictionary<RaidTarget, ScoutReport> reports = new Dictionary<RaidTarget, ScoutReport>();
    ScoutReport mapShown;   // открытая карта разведчика

    // ---------- Полевое снаряжение, взятое в поход (заслоны / пункты из запаса Цитадели) ----------
    readonly List<FieldGear.Item>[] kit = FieldGear.NewStock();
    readonly int[] kitPick = new int[FieldGear.Kinds];   // сколько берём (окно «В бой!»)

    /// <summary>Сколько таких укреплений взято в поход.</summary>
    public int KitCount(PostKind k) { return kit[(int)k].Count; }

    /// <summary>Достать лучшее укрепление этого вида из набора похода.</summary>
    public bool TakeKit(PostKind k, out FieldGear.Item item)
    {
        item = null;
        List<FieldGear.Item> l = kit[(int)k];
        int best = -1;
        float bestScore = float.MinValue;
        for (int i = 0; i < l.Count; i++)
            if (l[i].Score > bestScore) { bestScore = l[i].Score; best = i; }
        if (best < 0) return false;
        item = l[best];
        l.RemoveAt(best);
        return true;
    }

    /// <summary>Свернули укрепление на поле боя - вещь снова в наборе похода.</summary>
    public void ReturnKitItem(FieldGear.Item it)
    {
        if (it != null) kit[(int)it.kind].Add(it);
    }

    // Забрать выбранное из запаса Цитадели в поход
    void LoadKit()
    {
        for (int k = 0; k < FieldGear.Kinds; k++)
        {
            int n = Mathf.Min(kitPick[k], FieldGear.Count((PostKind)k));
            for (int i = 0; i < n; i++)
                if (FieldGear.Take((PostKind)k, out FieldGear.Item it)) kit[k].Add(it);
            kitPick[k] = 0;
        }
        TrapStock.LoadRaidKit();   // боевые ловушки
    }

    // Набор похода: toStock - вернуть остаток в запас Цитадели, иначе он потерян
    void UnloadKit(bool toStock)
    {
        for (int k = 0; k < FieldGear.Kinds; k++)
        {
            if (toStock) foreach (var it in kit[k]) FieldGear.Add(it);
            kit[k].Clear();
        }
        TrapStock.UnloadRaidKit(toStock);
    }

    // Какие укрепления показывать в окне «В бой!»: изготовленные в Кузне и доступные ветке (или уже лежащие в запасе).
    // Быстрый заслон не берут с собой - его ставят на месте за дерево.
    static List<int> KitKinds()
    {
        var list = new List<int>();
        for (int k = 0; k < FieldGear.Kinds; k++)
        {
            if (!FieldPost.Specs[k].crafted) continue;
            if (FieldPost.Allowed((PostKind)k) || FieldGear.Count((PostKind)k) > 0) list.Add(k);
        }
        return list;
    }

    // Строки выбора снаряжения в окне «В бой!»
    // final-13-fix-2: значок полевого укрепления в строке снаряжения
    static string KitIcon(PostKind k)
    {
        switch (k)
        {
            case PostKind.FieldTower: return "field_tower";
            case PostKind.Waypoint: return "ui_flag";
            case PostKind.SpikedBarricade: return "trap_spikes";
            default: return "barricade";
        }
    }

    void DrawKitRows(float x, float y, float w)
    {
        GUIStyle small = Style(12);
        var kinds = KitKinds();
        for (int i = 0; i < kinds.Count; i++)
        {
            int k = kinds[i];
            FieldPost.Spec spec = FieldPost.Specs[k];
            int have = FieldGear.Count((PostKind)k);
            kitPick[k] = Mathf.Clamp(kitPick[k], 0, have);
            float ry = y + i * 24f;
            GameUi.IconLine(new Rect(x, ry, w - 100f, 22f), KitIcon((PostKind)k), $"{spec.name}: в запасе <b>{have}</b>, берём <b>{kitPick[k]}</b>", small);

            bool en = GUI.enabled;
            GUI.enabled = en && kitPick[k] > 0;
            if (LocGUI.Button(new Rect(x + w - 84f, ry, 40f, 22f), "−")) kitPick[k]--;
            GUI.enabled = en && kitPick[k] < have;
            if (LocGUI.Button(new Rect(x + w - 40f, ry, 40f, 22f), "+")) kitPick[k]++;
            GUI.enabled = en;
        }
        TrapStock.DrawRaidPickRow(x, y + kinds.Count * 24f, w, small);   // последняя строка - боевые ловушки
    }

    // Итоги
    int startCount, lost, kills, ruinedCount, defendersTotal, towersTotal;
    int ruinedMain;   // из них не стены, не ворота и не заслоны (для щита города защитника)

    // Голем Алтаря жизни (школа Магии жизни) в походе
    bool takeGolem = true;      // галочка в окне «Набег»
    bool golemOut;              // голем ушёл с армией
    bool golemLost;             // погиб в набеге
    float golemHpBack = -1f;    // ушёл с поля живым: с каким здоровьем
    RaidUnit golemUnit;
    bool hallSacked, resistanceBroken;
    readonly List<Weapon> trophyWeapons = new List<Weapon>();
    readonly List<Shield> trophyShields = new List<Shield>();
    string report;

    // Камера
    bool viewBattle;
    Vector3 homeCamPos;

    // UI
    bool planOpen, statusOpen;
    bool planWasOpen;
    int pickTarget;
    bool takeCamp = true;
    readonly HashSet<Squad> picked = new HashSet<Squad>();
    readonly HashSet<Squad> selected = new HashSet<Squad>();
    Vector2 planScroll, villageScroll;
    float hudHeight = 200f;
    RaidBuilding hoverBuilding;
    RaidUnit hoverUnit;

    static readonly List<Rect> frameRects = new List<Rect>();
    static Rect[] publishedRects = new Rect[0];

    // ---------- Жизненный цикл ----------

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        // В аккаунте набеги - только на деревни других игроков (их ищет сервер); боты-соседи - в игре офлайн.
        // Без RivalManager к нам снова ходят слабые набеги (WarManager)
        online = AccountManager.LoggedIn;
        if (online) return;

        var rng = new System.Random(Random.Range(1, int.MaxValue));
        var usedNames = new HashSet<string>();
        for (int lvl = 1; lvl <= 3; lvl++)
        {
            string n;
            do n = BotVillage.RandomName(rng); while (!usedNames.Add(n));
            targets.Add(new RaidTarget { Name = n, Level = lvl, Seed = rng.Next(1, int.MaxValue) });
        }

        // Соседи - живые соперники: копят войско, укрепляются, нападают и мстят
        gameObject.AddComponent<RivalManager>().Init(targets);
    }

    /// <summary>Армия не дома (в пути, у чужой деревни или возвращается).</summary>
    public bool ArmyAway => State != Phase.Idle;
    /// <summary>Для уведомлений на телефоне (LocalNotifications): через сколько секунд армия дойдёт до цели (out = true) или до дома. -1 - не в пути.</summary>
    public float TripSecondsLeft(out bool outbound)
    {
        outbound = State == Phase.TravelOut;
        if (State == Phase.TravelHome || (State == Phase.TravelOut && !arriving)) return Mathf.Max(0f, timer);
        return -1f;
    }
    public string TripTarget => current != null ? current.Name : "";
    /// <summary>Деревня, на которую идёт (или с которой возвращается) наш набег.</summary>
    public RaidTarget Current => current;

    /// <summary>К защитникам подошло подкрепление.</summary>
    public void AddDefendersTotal(int n) { defendersTotal += n; }

    /// <summary>Защитники заметили нашу армию и ударили первыми.</summary>
    public void DefendersStrikeFirst()
    {
        if (State != Phase.Scouting) return;
        State = Phase.Battle;
        fought = true;
        if (!live) foreach (var u in RaidUnit.All) if (u != null && u.Attacker && u.Alive) u.OrderHold(u.Position);
        WarManager.Notify($"{current.Name} ударили первыми! Бойцы держат позиции — отдайте приказы", false);
    }

    // Тревога (WarManager.RaiseAlarm): свернуть окно, чтобы было видно бой
    void OnEnable() { WarManager.OnAlarm += CollapseForAlarm; }
    void OnDisable()
    {
        WarManager.OnAlarm -= CollapseForAlarm;
        // карта снова на весь экран
        Camera cam = Camera.main;
        if (cam != null) cam.rect = new Rect(0f, 0f, 1f, 1f);
        if (sideClear != null) sideClear.enabled = false;
    }
    bool CollapseForAlarm()   // только окно планирования; идущий набег и его статус не трогаем
    {
        if (!planOpen) return false;
        planOpen = false;
        return true;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>Курсор над окнами набега (BuildingPlacer не должен кликать по карте под ними).</summary>
    public static bool IsPointerOverUI(Vector2 guiPoint)
    {
        foreach (var r in publishedRects) if (r.Contains(guiPoint)) return true;
        return false;
    }

    // ---------- Выбор отрядов ----------

    static List<Squad> AvailableSquads()
    {
        var list = new List<Squad>();
        foreach (var b in BuildingInstance.All)
        {
            if (b == null) continue;
            BarracksController br = b.GetComponent<BarracksController>();
            if (br == null) continue;
            foreach (var sq in br.Squads)
                if (sq != null && !sq.OnExpedition && SoldiersOf(sq).Count > 0) list.Add(sq);
        }
        return list;
    }

    static List<CitizenAgent> SoldiersOf(Squad sq)
    {
        var list = new List<CitizenAgent>();
        foreach (var m in sq.Members)
            if (m != null && !m.IsDead && m.IsSoldier && !m.IsOnExpedition) list.Add(m);
        return list;
    }

    static string BarracksLabel(Squad sq)
    {
        foreach (var m in sq.Members)
            if (m != null && m.Barracks != null)
                return $"казарма [{m.Barracks.OriginCell.x},{m.Barracks.OriginCell.y}]";
        return "казарма";
    }

    int PickedSoldiers()
    {
        int n = 0;
        foreach (var sq in picked) if (sq != null && !sq.OnExpedition) n += SoldiersOf(sq).Count;
        return n;
    }

    // Почему нельзя выступить ("" - можно)
    string LaunchProblem()
    {
        if (State != Phase.Idle) return "Армия уже в походе";
        if (launching) return "Сервер подтверждает поход…";
        if (pickTarget < 0 || pickTarget >= targets.Count) return online ? "Найдите противника" : "Выберите деревню";
        if (!targets[pickTarget].Ready) return "Деревня ещё не восстановилась";
        int n = PickedSoldiers();
        if (n == 0) return "Отметьте отряды";
        var rm = ResourceManager.Instance;
        int needFood = PickedRations() * FoodMinutes, needWood = takeCamp ? CampWood : 0;
        if (rm == null || rm.food < needFood) return $"Не хватает еды: нужно {needFood}";
        if (rm.wood < needWood) return $"Не хватает дерева на лагерь: нужно {needWood}";
        return "";
    }

    // ---------- Выступление ----------

    void Launch()
    {
        if (LaunchProblem() != "") return;
        RaidTarget t = targets[pickTarget];
        if (!t.IsPlayer) { LaunchNow(t); return; }

        // Деревня игрока: сервер закрепляет её за нашим набегом (больше никто не нападёт)
        launching = true;
        reported = false;
        AccountManager.StartRaid(t.RaidId, res =>
        {
            launching = false;
            if (!res.ok)
            {
                WarManager.Notify("Поход не начат: " + res.error, false);
                targets.Remove(t);
                pickTarget = -1;
                return;
            }
            if (LaunchProblem() == "" && targets.Contains(t)) { pickTarget = targets.IndexOf(t); LaunchNow(t); }
            else SendResult(t, true);   // пока ждали ответа, что-то изменилось - освобождаем деревню
        });
    }

    void LaunchNow(RaidTarget t)
    {

        squads.Clear();
        members.Clear();
        foreach (var sq in picked)
        {
            if (sq == null || sq.OnExpedition) continue;
            var list = SoldiersOf(sq);
            if (list.Count == 0) continue;
            sq.GoHome(SoldierOrder.Guard);   // снять лагерь, вернуть его еду
            sq.OnExpedition = true;
            squads.Add(sq);
            members.AddRange(list);
        }
        if (members.Count == 0) return;

        golemOut = takeGolem && LifeAltar.TakeForRaid();
        golemLost = false;
        golemHpBack = -1f;
        golemUnit = null;

        var rm = ResourceManager.Instance;
        food = 0;
        foreach (var m in members) food += Ration(m) * FoodMinutes;   // final-13-fix-3: по составу армии
        woodPaid = takeCamp ? CampWood : 0;
        rm.Spend(0, woodPaid, 0, food);
        campPaid = takeCamp;
        LoadKit();

        // Сторона похода известна заранее (окно «В бой!»): к деревне подходят с неё, а из своего города
        // уходят через противоположный край карты (идут с запада - значит, наш город западнее, выходим на востоке)
        landSide = t.ApproachSide;
        Vector2 c = Vector2.zero;
        foreach (var m in members) c += (Vector2)m.transform.position;
        c /= members.Count;
        homeEdge = ExitEdgePoint(c, landSide);
        foreach (var m in members) m.BeginExpedition(JitterHome(homeEdge), landSide);   // к краю - по полю расстояний (без «телепорта»)

        current = t;
        ResetStats();
        ClearLive();
        reported = false;
        raidStartUtc = WorldClock.ServerUtc.ToString("o");   // сервер держит набег Api.RaidLockMinutes с начала похода
        startCount = members.Count;
        State = Phase.Marching;
        timer = 0f;
        planOpen = false;
        picked.Clear();
        WarManager.Notify($"Армия выступила в набег на {t.Name}: бойцов {members.Count}{(golemOut ? " и голем" : "")}, взято еды {food}", true);
    }

    // Голем возвращается домой к алтарю (hp < 0 - раны как были, в бою не был)
    void GolemHome(float hp, bool atHome = false)
    {
        if (!golemOut) return;
        golemOut = false;
        golemUnit = null;
        LifeAltar altar = LifeAltar.Current;
        BuildingInstance ab = altar != null ? altar.GetComponent<BuildingInstance>() : null;
        LifeAltar.BackFromRaid(hp, atHome && ab != null ? JitterHome(ab.Center) : JitterHome(homeEdge));
    }

    void ResetStats()
    {
        lost = kills = ruinedCount = ruinedMain = defendersTotal = towersTotal = 0;
        killedNames.Clear();
        arrivedPlan = null;
        hallSacked = resistanceBroken = fought = false;
        defensePoints = 0f;
        bountyPaid.Clear();
        survivors.Clear();
        trophyWeapons.Clear();
        trophyShields.Clear();
        selected.Clear();
        allSquads = !UiKit.Phone;   // на компьютере по-старому (приказ всем), на телефоне сначала выбрать отряд
        eatTimer = campFoodAcc = 0f;
        int hl = StorageLimits.HallLevel;
        spoilBudget = Mathf.Min(SpoilsRules.Budget(hl), SpoilsChest.DailyLeft(hl));
        spoilCapTold = false;
        liveStolen.Clear();
        liveCopies.Clear();
        spoilsReport = "";
        spoilsIcons = new SpoilBundle();
    }

    static Vector2 HomeEdgePoint(Vector2 p)
    {
        var nav = GridNavigation.Instance;
        if (nav == null) return p;
        float h = nav.mapSize / 2f - 0.5f;
        float dl = p.x + h, dr = h - p.x, db = p.y + h, du = h - p.y;
        float m = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(db, du));
        Vector2 e;
        if (m == dl) e = new Vector2(-h, p.y);
        else if (m == dr) e = new Vector2(h, p.y);
        else if (m == db) e = new Vector2(p.x, -h);
        else e = new Vector2(p.x, h);
        Vector2Int cell = nav.WorldToCell(e);
        if (nav.IsBlocked(cell)) cell = nav.NearestFree(cell);
        return nav.CellCenter(cell);
    }

    /// <summary>Край нашей карты, через который армия уходит к деревне, подходя к ней со стороны side (и возвращается тем же путём).</summary>
    static Vector2 ExitEdgePoint(Vector2 p, int side)
    {
        var nav = GridNavigation.Instance;
        if (nav == null) return p;
        float h = nav.mapSize / 2f - 0.5f;
        float m = h - 2f;   // вдоль края - напротив казарм, но не в самом углу
        Vector2 e;
        switch (side)
        {
            case 1: e = new Vector2(-h, Mathf.Clamp(p.y, -m, m)); break;   // подходим с востока - выходим на западе
            case 2: e = new Vector2(Mathf.Clamp(p.x, -m, m), h); break;    // с юга - выходим на севере
            case 3: e = new Vector2(Mathf.Clamp(p.x, -m, m), -h); break;   // с севера - на юге
            default: e = new Vector2(h, Mathf.Clamp(p.y, -m, m)); break;   // с запада - на востоке
        }
        Vector2Int cell = nav.WorldToCell(e);
        if (!nav.InBounds(cell) || nav.IsBlocked(cell)) cell = nav.NearestFree(cell);
        return nav.CellCenter(cell);
    }

    static Vector2 JitterHome(Vector2 p)
    {
        var nav = GridNavigation.Instance;
        if (nav == null) return p;
        Vector2Int cell = nav.WorldToCell(p + Random.insideUnitCircle * 1.5f);
        if (!nav.InBounds(cell) || nav.IsBlocked(cell)) cell = nav.NearestFree(nav.WorldToCell(p));
        return nav.CellCenter(cell);
    }

    /// <summary>Отменить поход, пока армия ещё не дошла до деревни: всё возвращается.</summary>
    void Recall()
    {
        if (State != Phase.Marching && State != Phase.TravelOut) return;
        ClearLive();
        foreach (var m in members) if (m != null && !m.IsDead) m.CancelExpedition();
        var rm = ResourceManager.Instance;
        if (rm != null) rm.Add(0, woodPaid, 0, food);
        UnloadKit(true);
        GolemHome(-1f);
        food = 0;
        woodPaid = 0;
        ReleaseSquads();
        members.Clear();
        SendResult(current, true);
        current = null;
        State = Phase.Idle;
        statusOpen = false;
        WarManager.Notify("Поход отменён — армия возвращается", false);
    }

    void ReleaseSquads()
    {
        foreach (var sq in squads)
        {
            if (sq == null) continue;
            sq.OnExpedition = false;
            sq.GoHome(SoldierOrder.Guard);
        }
        squads.Clear();
    }

    // ---------- Главный цикл ----------

    void Update()
    {
        SpoilsChest.Tick(Time.deltaTime);   // трофеи, которым не хватило места, - на склады, как только место появится
        members.RemoveAll(m => m == null || m.IsDead);
        UpdateSideView();
        HandleKeys();
        ScoutTick(Time.deltaTime);
        FlushBounty();
        if (ResumeTick(Time.deltaTime)) return;   // ждём сервер: кто погиб в живом бою без нас (final-8)

        switch (State)
        {
            case Phase.Marching:
                timer += Time.deltaTime;
                if (members.Count == 0) { FailEmpty(); break; }
                bool allAway = true;
                foreach (var m in members) if (!m.IsAway) { allAway = false; break; }
                if (!allAway && timer > MarchTimeout)
                {
                    foreach (var m in members) m.ForceAway();
                    allAway = true;
                }
                if (allAway)
                {
                    State = Phase.TravelOut;
                    timer = current.TravelSeconds;
                    WarManager.Notify($"Армия ушла за край карты. До {current.Name} — {timer:F0} с", true);
                }
                break;

            case Phase.TravelOut:
                if (members.Count == 0) { FailEmpty(); break; }
                timer -= Time.deltaTime;
                if (arriving) ArriveTick(Time.deltaTime);
                else if (timer <= 0f) BeginArrive();
                break;

            case Phase.Scouting:
            case Phase.Battle:
            case Phase.Leaving:
                if (live) LiveTick(Time.deltaTime);
                if (State == Phase.Idle || State == Phase.TravelHome) break;   // живой бой мог закончиться
                BattleTick(Time.deltaTime);
                if (!live) GolemReviveTick(Time.deltaTime);
                if (ViewingBattle && !WarView.IsOpen) HandleBattleInput();
                break;

            case Phase.TravelHome:
                timer -= Time.deltaTime;
                if (timer <= 0f) ReturnHome();
                break;
        }
    }

    // Все бойцы погибли / исчезли ещё по дороге
    void FailEmpty()
    {
        UnloadKit(true);
        GolemHome(-1f);
        ReleaseSquads();
        SendResult(current, true);
        current = null;
        State = Phase.Idle;
        WarManager.Notify("Поход сорван: бойцов не осталось", false);
    }

    void HandleKeys()
    {
        var kb = Keyboard.current;
        if (kb == null || WarView.IsOpen) return;
        if (!UiKit.Typing && kb.vKey.wasPressedThisFrame && RaidMap.Instance != null) SetView(!viewBattle);
        if (kb.escapeKey.wasPressedThisFrame)
        {
            if (mapShown != null) mapShown = null;
            else if (planOpen) planOpen = false;
            else if (ViewingBattle) { selected.Clear(); allSquads = false; inspectedUnit = null; }
        }
        // Ctrl+A (на телефоне - кнопка «Все бойцы»): приказы - всем отрядам
        if (ViewingBattle && !UiKit.Typing && kb.aKey.wasPressedThisFrame && kb.ctrlKey.isPressed) { selected.Clear(); allSquads = true; }
    }

    // ---------- Прибытие и бой ----------

    void Arrive()
    {
        BotVillage.Plan plan = current.GetPlan();
        arrivedPlan = plan;
        battleDeadline = Time.time + Api.RaidBattleSeconds - TakeArriveSpent();   // дошли, пока игрока не было - часть времени уже прошла
        // Сторона, с которой армия подходит к деревне, выбрана при выходе (RaidTarget.ApproachSide, видна заранее)
        if (current.IsPlayer) landSide = plan.Rot;   // деревня игрока повёрнута в план этой же стороной (final-8)
        RaidMap.SetSide(landSide);
        RaidMap.SetPlanRot(plan.Rot);
        RaidMap map = RaidMap.Create(plan.Trees, plan.Water, plan.Woods);
        BotVillage.Build(plan, map, current);
        RememberDefense(plan);
        defendersTotal = plan.Defenders.Count;
        towersTotal = plan.Towers + plan.Cannons + plan.Guns;

        float half = RaidMap.Size / 2f;
        landing = RaidMap.Local(-half + 5.5f, 0.5f);
        campPos = RaidMap.Local(-half + 2.5f, 0.5f);

        int i = 0;
        var spawned = new List<RaidUnit>();
        foreach (var m in members)
        {
            if (m == null || m.IsDead || !m.IsAway) continue;
            int row = i / 5, col = i % 5;
            Vector2 p = landing + RaidMap.WorldDir(new Vector2(row * 0.9f, (col - 2) * 0.9f));
            Vector2Int cell = map.WorldToCell(p);
            if (map.IsBlocked(cell)) p = map.CellCenter(map.NearestFree(cell));
            spawned.Add(RaidUnit.SpawnAttacker(m, m.Squad, p));
            i++;
        }
        RaidUnit.FormRams(spawned);   // носильщики таранов - по двое в таран

        // Голем идёт последним рядом (если алтарь дома ещё цел)
        if (golemOut)
        {
            if (LifeAltar.RaidBound)
            {
                int row = i / 5, col = i % 5;
                Vector2 p = landing + RaidMap.WorldDir(new Vector2(row * 0.9f, (col - 2) * 0.9f));
                Vector2Int cell = map.WorldToCell(p);
                if (map.IsBlocked(cell)) p = map.CellCenter(map.NearestFree(cell));
                golemUnit = RaidUnit.SpawnGolem(LifeAltar.RaidUnitType(), LifeAltar.RaidHp, p);
            }
            else { golemOut = false; golemLost = true; }
        }

        if (campPaid) BuildCamp();
        FogOfWar.CreateRaid();   // туман: видно края карты и вокруг наших

        State = Phase.Scouting;
        scoutSince = Time.time;
        SetView(true);
        WarManager.Notify(current.IsPlayer
            ? (current.IsNpc ? $"Армия подошла к деревне {current.Name} {RaidMap.SideName(landSide)}. На бой — {Api.RaidBattleSeconds / 60f:F0} минут. Атаковать или уйти?"
                             : $"Армия подошла к деревне игрока {current.Name} {RaidMap.SideName(landSide)}. На бой — {Api.RaidBattleSeconds / 60f:F0} минут. Атаковать или уйти?")
            : $"Армия подошла к деревне {current.Name} {RaidMap.SideName(landSide)}. Осмотритесь: атаковать или уйти?", true);
        RivalManager.PlayerArrived(current);
        // final-12-fix-2: снимки боя снова уходят на сервер - из них у защитника собирается повтор набега
        // (и он может зайти и командовать сам). Вызов потерялся в одной из старых сборок, и повторы были пустыми.
        if (current.IsPlayer && !current.IsNpc) BattleRelay.Begin(current);
    }

    // ---------- Живой бой ----------

    // Живой бой: точки защитника - в клетках его города; план повёрнут стороной подхода (RaidMap.PlanRot, final-8)
    static Vector2 LivePos(float x, float y) { return RaidMap.WorldOfReal(new Vector2(x, y)); }
    static Vector2 NetPos(Vector2 world) { return RaidMap.RealOf(world); }
    // Здание - его левой нижней клеткой у хозяина (по ней игра защитника находит здание)
    static Vector2Int LiveCell(RaidBuilding b) { return RaidMap.RealOrigin(b.OriginCell, b.SizeCells); }

    // Армия у деревни игрока: спрашиваем сервер, в игре ли он (тогда бой - у него вживую)
    void BeginArrive()
    {
        if (current == null || !current.IsPlayer || !online || string.IsNullOrEmpty(AccountManager.Token)) { Arrive(); return; }
        arriving = true;
        liveDecided = false;
        arriveWait = 0f;
        armyIds.Clear();
        var army = new List<SiegeUnit>();
        var idOf = new Dictionary<CitizenAgent, SiegeUnit>();
        int id = 1;
        foreach (var m in members)
        {
            if (m == null || m.IsDead || !m.IsAway) continue;
            armyIds[id] = m;
            SiegeUnit su = m.ExpSiegeUnit(id);
            army.Add(su);
            idOf[m] = su;
            id++;
        }
        // Тараны: ведущий несёт таран (его прочность и удар), остальные носильщики «в нём» до поломки
        ramCrewIds.Clear();
        foreach (var crew in CitizenAgent.RamCrews(idOf.Keys))
        {
            SiegeUnit lead = idOf[crew[0]];
            CitizenAgent.FillRamSiege(lead, crew);
            var followers = new List<int>();
            for (int k = 1; k < crew.Count; k++) { idOf[crew[k]].inRam = lead.id; followers.Add(idOf[crew[k]].id); }
            ramCrewIds[lead.id] = followers;
        }
        if (golemOut && LifeAltar.RaidBound)
        {
            UnitType g = LifeAltar.RaidUnitType();
            army.Add(new SiegeUnit
            {
                id = GolemNetId, golem = true, unit = g.unitName, name = LifeAltar.GolemName, hp = LifeAltar.RaidHp, maxHp = g.maxHp,
                damage = g.damage, range = g.attackRange, cooldown = g.attackCooldown, speed = g.moveSpeed, scale = g.bodyScale,
                r = g.color.r, g = g.color.g, b = g.color.b, carry = 0,
            });
        }
        RaidTarget t = current;
        AccountManager.Backend.Call(Api.SiegeArrive, AccountManager.Token, new ApiRequest { raidId = t.RaidId, siegeArmy = army, camp = campPaid, siegeSide = Mathf.Clamp(t.ApproachSide, 0, 3) }, res =>
        {
            if (this == null || !arriving || current != t) return;
            if (!res.ok || !res.siegeLive) { arriving = false; Arrive(); return; }
            liveDecided = true;
            WarManager.Notify($"{t.Name} в игре — армия входит прямо в его деревню!", true);
        });
    }

    // Ждём первый снимок из его деревни (или отказ - тогда бой без него)
    void ArriveTick(float dt)
    {
        arriveWait += dt;
        if (arriveWait > 40f) { arriving = false; Arrive(); return; }   // сервер молчит - бой без него
        if (!liveDecided) return;
        pullTimer -= dt;
        if (pullTimer > 0f || pullInFlight) return;
        pullTimer = 0.5f;
        pullInFlight = true;
        RaidTarget t = current;
        AccountManager.Backend.Call(Api.SiegePull, AccountManager.Token, new ApiRequest { raidId = t.RaidId }, res =>
        {
            pullInFlight = false;
            if (this == null || !arriving || current != t || !res.ok) return;
            if (!res.siegeLive)
            {
                arriving = false;
                WarManager.Notify($"{t.Name} не принял бой у себя — деревня обороняется сама", true);
                Arrive();
                return;
            }
            if (res.siegeFrame != null) BuildLive(res.siegeFrame);
        });
    }

    readonly Dictionary<int, List<int>> ramCrewIds = new Dictionary<int, List<int>>();   // живой бой: ведущий тарана -> носильщики в нём

    void BuildLive(SiegeFrame f)
    {
        arriving = false;
        live = true;
        current.LiveLayout = true;
        BotVillage.Plan plan = current.GetPlan();
        arrivedPlan = plan;
        battleDeadline = Time.time + Api.RaidBattleSeconds - TakeArriveSpent();   // дошли, пока игрока не было - часть времени уже прошла
        if (current.IsPlayer) landSide = plan.Rot;
        RaidMap.SetSide(landSide);   // живой бой: снимки - в клетках защитника (LivePos), план повёрнут стороной подхода
        RaidMap.SetPlanRot(plan.Rot);
        RaidMap map = RaidMap.Create(plan.Trees, plan.Water, plan.Woods, plan.MapSize);
        BotVillage.Build(plan, map, current);
        RememberDefense(plan);
        defendersTotal = 0;
        towersTotal = plan.Towers + plan.Cannons + plan.Guns;
        float half = RaidMap.Size / 2f;
        landing = RaidMap.Local(-half + 3.5f, 0.5f);
        campPos = landing;

        var marks = new Dictionary<int, SiegeMark>();
        foreach (var m in f.attackers) if (m != null) marks[m.id] = m;
        netAttackers.Clear();
        netDefenders.Clear();
        lastCarried.Clear();
        goneSeen.Clear();
        outbox.Clear();
        liveKilled = 0;
        netPosts.Clear();
        netPostHp.Clear();
        pendingPosts.Clear();
        postsPrimed = false;
        netTraps.Clear();
        netTrapProgress.Clear();
        pendingTraps.Clear();
        pendingTrapAt.Clear();
        lastSeq = 0;
        netOffset = float.MaxValue;
        int i = 0;
        foreach (var kv in armyIds)
        {
            CitizenAgent m = kv.Value;
            if (m == null || m.IsDead || !m.IsAway) continue;
            Vector2 p = marks.TryGetValue(kv.Key, out SiegeMark mk) ? LivePos(mk.x, mk.y) : landing + RaidMap.WorldDir(new Vector2(0f, (i % 9 - 4) * 0.8f));
            RaidUnit u = RaidUnit.SpawnAttacker(m, m.Squad, p);
            u.MakePuppet(kv.Key);
            netAttackers[kv.Key] = u;
            i++;
        }
        // Тараны - те же расчёты, что ушли защитнику (BeginArrive)
        foreach (var kv in ramCrewIds)
        {
            if (!netAttackers.TryGetValue(kv.Key, out RaidUnit host) || host == null) continue;
            var others = new List<RaidUnit>();
            foreach (int fid in kv.Value) if (netAttackers.TryGetValue(fid, out RaidUnit r) && r != null) others.Add(r);
            RaidUnit.FormRam(host, others);
        }
        if (golemOut)
        {
            if (LifeAltar.RaidBound)
            {
                Vector2 p = marks.TryGetValue(GolemNetId, out SiegeMark gm) ? LivePos(gm.x, gm.y) : landing;
                golemUnit = RaidUnit.SpawnGolem(LifeAltar.RaidUnitType(), LifeAltar.RaidHp, p);
                golemUnit.MakePuppet(GolemNetId);
                netAttackers[GolemNetId] = golemUnit;
            }
            else { golemOut = false; golemLost = true; }
        }

        FogOfWar.CreateRaid();
        State = Phase.Scouting;
        scoutSince = Time.time;
        SetView(true);
        ApplyFrame(f);
        WarManager.Notify($"Армия в деревне игрока {current.Name} — он в игре и защищается сам! На бой — {Api.RaidBattleSeconds / 60f:F0} минут. Атаковать или уйти?", true);
        RivalManager.PlayerArrived(current);
    }

    void Send(string kind, IEnumerable<RaidUnit> units, float x = 0f, float y = 0f, int target = 0)
    {
        var c = new BattleCommand { kind = kind, x = x, y = y, target = target };
        if (units != null) foreach (var u in units) if (u != null && u.Puppet && u.Attacker) c.units.Add(u.NetId);
        outbox.Add(c);
        NoteRoutes(c, units);
    }

    // Живой бой: бойцами управляет игра защитника, а пунктир к цели (RouteMarks) рисуем у себя по своему же приказу.
    // Защитнику маршрут не уходит.
    void NoteRoutes(BattleCommand c, IEnumerable<RaidUnit> units)
    {
        RaidBuilding bld = null;
        RaidUnit enemy = null;
        if (c.kind == BattleCommand.Focus)
            foreach (var b in RaidBuilding.All)
                if (b != null && LiveCell(b) == new Vector2Int(Mathf.RoundToInt(c.x), Mathf.RoundToInt(c.y))) { bld = b; break; }
        if (c.kind == BattleCommand.Attack)
            foreach (var e in RaidUnit.All)
                if (e != null && !e.Attacker && e.Puppet && e.NetId == c.target) { enemy = e; break; }
        foreach (var u in units ?? RaidUnit.All)
        {
            if (u == null || !u.Puppet || !u.Attacker) continue;
            if (c.kind == BattleCommand.Move) u.NoteRoute(LivePos(c.x, c.y));
            else if (c.kind == BattleCommand.Focus && bld != null) u.NoteRoute(bld);
            else if (c.kind == BattleCommand.Attack && enemy != null) u.NoteRoute(enemy);
            else if (c.kind == BattleCommand.Retreat) u.NoteRoute(ExitFor(u), 3);
            else u.ClearRoute();
        }
        pullTimer = Mathf.Min(pullTimer, 0.05f);   // отправить сразу
    }

    void LiveTick(float dt)
    {
        pullTimer -= dt;
        if (pullTimer > 0f || pullInFlight) return;
        pullTimer = PullEvery;
        pullInFlight = true;
        var cmds = outbox.Count > 0 ? new List<BattleCommand>(outbox) : null;
        outbox.Clear();
        RaidTarget t = current;
        AccountManager.Backend.Call(Api.SiegePull, AccountManager.Token, new ApiRequest { raidId = t.RaidId, commands = cmds }, res =>
        {
            pullInFlight = false;
            if (this == null || !live || current != t) return;
            if (!res.ok)
            {
                if (cmds != null) outbox.InsertRange(0, cmds);   // приказы - ещё раз
                if (++pullFails >= 40) DefenderLost("нет связи с сервером");
                return;
            }
            pullFails = 0;
            if (res.siegeFrame != null) ApplyFrame(res.siegeFrame);
            if (res.siegeOver || !res.siegeLive) DefenderLost($"{t.Name} пропал из игры");
        });
    }

    // Связь с его деревней пропала: кто ещё там - уходит домой с тем, что несёт
    void DefenderLost(string why)
    {
        if (!live) return;
        WarManager.Notify($"{why} — армия уходит домой с тем, что несёт", false);
        if (State == Phase.Scouting || State == Phase.Battle) State = Phase.Leaving;
        foreach (var kv in new List<KeyValuePair<int, RaidUnit>>(netAttackers))
        {
            RaidUnit u = kv.Value;
            if (u == null || !u.Alive) continue;
            lastCarried.TryGetValue(kv.Key, out RaidLoot l);
            u.PuppetLeave(l);
        }
        if (Attackers().Count == 0) Finish();
    }

    void ApplyFrame(SiegeFrame f)
    {
        if (f == null) return;
        if (f.seq > 0 && f.seq <= lastSeq) return;   // этот снимок уже был (спросили раньше, чем он обновился)
        lastSeq = f.seq;
        // Трофеи, которые унесли наши ушедшие бойцы (весь список с начала боя)
        foreach (var d in f.defenders) if (SiegePosts.IsSpoils(d)) SiegePosts.DecodeSpoils(d, liveStolen, liveCopies);
        float stamp = NetStamp(f);
        liveStatus = f.status ?? "";

        // Наши бойцы
        foreach (var m in f.attackers)
        {
            if (m == null || !netAttackers.TryGetValue(m.id, out RaidUnit u) || u == null || !u.Alive) continue;
            if (u.RamHost != null) u.PuppetUnhide(LivePos(m.x, m.y));   // выпал из разбитого тарана
            u.SetNetRam(m.ram);
            u.SetNet(LivePos(m.x, m.y), m.hp, m.fighting, stamp);
            u.SetCarried(m.carried);
            lastCarried[m.id] = m.carried;
        }
        foreach (var g in f.gone)
        {
            if (g == null || !goneSeen.Add(g.id)) continue;
            if (!netAttackers.TryGetValue(g.id, out RaidUnit u) || u == null || !u.Alive) continue;
            if (g.dead) u.PuppetKill();
            else u.PuppetLeave(g.loot);
        }

        // Погибшие защитники
        bool died = f.killed.Count > liveKilled;
        for (int i = liveKilled; i < f.killed.Count; i++)
        {
            kills++;
            killedNames.Add(f.killed[i]);
        }
        liveKilled = Mathf.Max(liveKilled, f.killed.Count);

        ApplyPosts(f);

        // Его бойцы и жители на улице
        var seen = new HashSet<int>();
        foreach (var d in f.defenders)
        {
            if (d == null || SiegePosts.IsThing(d)) continue;   // укрепления и ловушки - не бойцы (ApplyPosts)
            seen.Add(d.id);
            Vector2 p = LivePos(d.x, d.y);
            if (!netDefenders.TryGetValue(d.id, out RaidUnit u) || u == null)
            {
                UnitType ut = d.citizen ? null : (BotVillage.FindUnit(d.unit) ?? BarracksController.FindType(d.unit));
                if (!d.citizen && ut == null) ut = BotVillage.FindUnit("Мечник");
                u = RaidUnit.SpawnPuppet(ut, p, d.id);
                netDefenders[d.id] = u;
            }
            u.SetNetDefenderRam(d.ram);   // его таран - одна фигурка; разбили - выпадут двое
            u.SetNet(p, d.hp, d.fighting, stamp);
        }
        foreach (var id in new List<int>(netDefenders.Keys))
        {
            if (seen.Contains(id)) continue;
            RaidUnit u = netDefenders[id];
            if (u != null) u.PuppetVanish(died && !u.Unit.unitName.Equals("Житель"));
            netDefenders.Remove(id);
        }
        defendersTotal = Mathf.Max(defendersTotal, CountDefenders() + kills);

        // Здания: что не целое - то в списке
        var houses = new Dictionary<Vector2Int, SiegeHouse>();
        foreach (var h in f.houses) if (h != null) houses[new Vector2Int(h.x, h.y)] = h;
        foreach (var b in new List<RaidBuilding>(RaidBuilding.All))
        {
            if (b == null) continue;
            if (houses.TryGetValue(LiveCell(b), out SiegeHouse h)) b.SetNet(h.hp, h.ruined);
            else b.SetNet(1f, false);
        }

        // Выстрелы - только картинка (у нас в деревне их никто не записывает)
        var rec = CombatFx.Record;
        CombatFx.Record = null;
        try
        {
            foreach (var sh in f.shots)
            {
                if (sh == null) continue;
                Vector2 a = LivePos(sh.x0, sh.y0), z = LivePos(sh.x1, sh.y1);
                switch (sh.kind)
                {
                    case 1: CombatFx.Shell(a, z, 0.8f, 1.2f, null); break;
                    case 2: CombatFx.Bolt(a, z); break;
                    case 3: CombatFx.Fireball(a, z, 1.2f, null); break;
                    default: CombatFx.Arrow(a, z); break;
                }
            }
        }
        finally { CombatFx.Record = rec; }

        if (f.engaged && State == Phase.Scouting) DefendersStrikeFirst();
    }

    void ClearLive()
    {
        live = false;
        arriving = false;
        liveDecided = false;
        if (current != null) current.LiveLayout = false;
        armyIds.Clear();
        netAttackers.Clear();
        netDefenders.Clear();
        lastCarried.Clear();
        goneSeen.Clear();
        outbox.Clear();
        netPosts.Clear();
        netPostHp.Clear();
        pendingPosts.Clear();
        postsPrimed = false;
        netTraps.Clear();
        netTrapProgress.Clear();
        pendingTraps.Clear();
        pendingTrapAt.Clear();
        liveStatus = "";
    }

    void BuildCamp()
    {
        campGo = new GameObject("RaidCamp");
        campGo.transform.position = new Vector3(campPos.x, campPos.y, 0f);
        CampPart("Tent", new Vector2(0f, -0.1f), new Vector2(1.1f, 0.7f), new Color(0.85f, 0.78f, 0.55f), 3);
        CampPart("Pole", new Vector2(0.65f, 0.35f), new Vector2(0.06f, 1f), new Color(0.35f, 0.25f, 0.15f), 4);
        CampPart("Flag", new Vector2(0.85f, 0.7f), new Vector2(0.38f, 0.26f), new Color(0.25f, 0.55f, 1f), 4);
    }

    void CampPart(string n, Vector2 pos, Vector2 size, Color c, int order)
    {
        GameObject go = new GameObject(n);
        go.transform.SetParent(campGo.transform, false);
        go.transform.localPosition = new Vector3(pos.x, pos.y, 0f);
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = CombatFx.Square();
        sr.color = c;
        sr.sortingOrder = order;
    }

    public bool InCampCover(Vector2 p) { return campPaid && campGo != null && Vector2.Distance(p, campPos) <= CampCover; }

    void StartAttack()
    {
        if (State != Phase.Scouting) return;
        State = Phase.Battle;
        fought = true;
        if (live) Send(BattleCommand.Assault, null);
        else foreach (var u in RaidUnit.All) if (u != null && u.Attacker) u.OrderAuto();
        WarManager.Notify($"Штурм {current.Name}!", true);
    }

    Vector2 ExitFor(RaidUnit u)
    {
        float y = Mathf.Clamp(RaidMap.FrameOf(u.Position).y, -8f, 8f);
        return RaidMap.Local(-RaidMap.Size / 2f + 0.5f, y);
    }

    void RetreatUnits(IEnumerable<RaidUnit> list)
    {
        if (live) { Send(BattleCommand.Retreat, list); return; }
        foreach (var u in list) if (u != null && u.Attacker && u.Alive) u.OrderRetreat(ExitFor(u));
    }

    void RetreatAll()
    {
        if (live) Send(BattleCommand.Retreat, null);
        else RetreatUnits(new List<RaidUnit>(RaidUnit.All));
        State = Phase.Leaving;
        WarManager.Notify("Армия отступает домой", false);
    }

    List<RaidUnit> Attackers()
    {
        var list = new List<RaidUnit>();
        foreach (var u in RaidUnit.All) if (u != null && u.Attacker && u.Alive) list.Add(u);
        return list;
    }

    // Обновление 12: только для чтения - один список на кадр (OnGUI зовётся по нескольку раз за кадр)
    readonly List<RaidUnit> attFrame = new List<RaidUnit>();
    int attFrameNo = -1;
    List<RaidUnit> AttackersNow()
    {
        if (attFrameNo != Time.frameCount)
        {
            attFrameNo = Time.frameCount;
            attFrame.Clear();
            foreach (var u in RaidUnit.All) if (u != null && u.Attacker && u.Alive) attFrame.Add(u);
        }
        else attFrame.RemoveAll(u => u == null || !u.Alive);
        return attFrame;
    }

    List<RaidUnit> SelectedAttackers()
    {
        var list = Attackers();
        if (selected.Count == 0) return list;
        list.RemoveAll(u => u.Squad == null || !selected.Contains(u.Squad));
        return list.Count > 0 ? list : Attackers();
    }

    void BattleTick(float dt)
    {
        List<RaidUnit> att = AttackersNow();

        // Еда из запаса похода
        eatTimer += dt;
        if (eatTimer >= EatInterval)
        {
            eatTimer -= EatInterval;
            int had = food;
            int eaters = 0;
            foreach (var u in att) eaters += UnitEats(u);   // голем не ест, в таране - все носильщики, всадник - ещё и конь
            food = Mathf.Max(0, food - eaters);
            if (had > 0 && food == 0) WarManager.Notify("В походе кончилась еда — бойцы слабеют", false);
        }
        if (food <= 0 && !live)   // живой бой: здоровье считает игра защитника
            foreach (var u in att) if (u.Source != null) u.Source.ExpeditionHunger(HungerPerSecond * dt);

        // Лагерь лечит тех, кто рядом и не в бою (за еду из запаса)
        if (campPaid && campGo != null && food > 0 && !live)
        {
            foreach (var u in att)
            {
                if (u.Source == null || u.IsRam || u.InCombat || u.Hp >= u.MaxHp - 0.01f) continue;   // голема лечит только алтарь, таран чинят дома
                if (Vector2.Distance(u.Position, campPos) > CampRadius) continue;
                float got = u.Source.ReceiveHeal(CampHealPerSecond * dt);
                campFoodAcc += got / CampHpPerFood;
            }
            while (campFoodAcc >= 1f && food > 0) { food--; campFoodAcc -= 1f; }
        }

        // Добычу с руин подбирают бойцы, которые стоят рядом и у которых есть место
        pickupTimer -= dt;
        if (pickupTimer <= 0f && !live)
        {
            pickupTimer = 0.5f;
            foreach (var b in RaidBuilding.All)
            {
                if (b == null || !b.IsRuined || b.Storage.IsEmpty) continue;
                foreach (var u in att)
                {
                    if ((u.CarryFree <= 0 && !b.Storage.HasItems && (u.CarryCap <= 0 || b.Storage.ValuableWeight <= 0)) || b.DistanceTo(u.Position) > 1.6f) continue;
                    int got = TakeLoot(b.Storage, u.Carried, u.CarryFree);
                    if (got > 0) CombatFx.LootFly(b.Center, u.Position);
                    if (b.Storage.IsEmpty) break;
                }
            }
        }

        // Победные вехи
        if (State != Phase.Scouting && !live)
        {
            if (!resistanceBroken && defendersTotal > 0 && CountDefenders() == 0)
            {
                resistanceBroken = true;
                WarManager.Notify($"Защитники {current.Name} разбиты!", true);
            }
        }

        // Деревня игрока: время боя вышло - все отступают; кто не успел уйти за минуту - остаётся на поле
        if (current != null && current.IsPlayer)
        {
            if ((State == Phase.Scouting || State == Phase.Battle) && Time.time >= battleDeadline)
            {
                WarManager.Notify("Время набега вышло — армия отступает", false);
                RetreatAll();
            }
            else if (State == Phase.Leaving && Time.time >= battleDeadline + (live ? 150f : 60f)) { Finish(); return; }
        }

        // Никого из наших на поле не осталось - поход окончен
        if ((State == Phase.Battle || State == Phase.Leaving) && att.Count == 0) Finish();
    }

    static int CountDefenders()
    {
        int n = 0;
        foreach (var u in RaidUnit.All) if (u != null && !u.Attacker && u.Alive) n++;
        return n;
    }

    // Тревога: защитники рядом бросаются в бой
    public void Alarm(Vector2 at, float radius)
    {
        if (!BattleOn) return;
        foreach (var u in RaidUnit.All)
            if (u != null && !u.Attacker && u.Alive && Vector2.Distance(u.Position, at) <= radius) u.Alert();
    }

    // ---------- События боя ----------

    public void OnBuildingRuined(RaidBuilding b, RaidUnit by)
    {
        if (restoring) return;   // руины из сохранения: счёт и склады уже как были (RaidManager_Save)
        if (!fought) return;     // живой бой: здание было разрушено ещё до нас (первый снимок) - не наша заслуга
        ruinedCount++;
        if (!b.IsBarrier) ruinedMain++;
        if (b.IsHall && !hallSacked)
        {
            hallSacked = true;
            WarManager.Notify($"Цитадель {current.Name} разрушена — казна деревни ваша!", true);
            PayHallBounty();     // премия за победу - сразу в казну, не в мешки (RaidManager_Bounty)
        }
        else if (b.IsDefense) PayTowerBounty(b);
        if (current != null && current.IsPlayer && !live) RollSpoils(b);
        if (b.Storage.IsEmpty) return;

        // Склад делят бойцы рядом: у кого больше места - тот берёт первым
        var near = new List<RaidUnit>();
        foreach (var u in Attackers())
            if (u == by || b.DistanceTo(u.Position) <= 3.5f) near.Add(u);
        near.Sort((a, c) => c.CarryFree.CompareTo(a.CarryFree));

        int taken = 0;
        foreach (var u in near)
        {
            if (b.Storage.IsEmpty) break;
            int got = TakeLoot(b.Storage, u.Carried, u.CarryFree);
            if (got > 0) { taken += got; CombatFx.LootFly(b.Center, u.Position); }
        }

        if (!b.Storage.IsEmpty)
            WarManager.Notify($"{b.Title}: мешки полны, на руинах осталось {b.Storage.Total} — подойдите, чтобы забрать", false);
    }

    // Здание деревни игрока разрушено: по сложным вещам в нём - жребий на копию и кражу (Spoils). Выпавшее ложится
    // на руины, как обычная добыча: засчитается только то, с чем бойцы уйдут с поля
    void RollSpoils(RaidBuilding b)
    {
        if (b == null || b.Stash == null) return;
        var stolen = new SpoilBundle();
        var copies = new SpoilBundle();
        int used = Spoils.Roll(b.Stash, ref spoilBudget, stolen, copies);
        b.ClearStash();
        if (used > 0) SpoilsChest.UseDaily(used);
        if (stolen.IsEmpty && copies.IsEmpty)
        {
            if (spoilBudget <= 0 && !spoilCapTold)
            {
                spoilCapTold = true;
                WarManager.Notify(SpoilsChest.DailyLeft(StorageLimits.HallLevel) <= 0
                    ? "Трофеев на сегодня больше не будет: за сутки взято всё, что можно. Простые ресурсы уносить можно"
                    : "Потолок трофеев за этот набег набран: сложных вещей больше не выпадет, простые ресурсы уносить можно", false);
            }
            return;
        }
        b.Storage.items.Add(stolen);
        b.Storage.copies.Add(copies);
        var all = stolen.Copy();
        all.Add(copies);
        WarManager.Notify($"{b.Title}: трофеи — {all.Text}. Засчитается то, с чем бойцы уйдут с поля", true);
    }

    public void OnDefenderKilled(RaidUnit u, RaidUnit by, Weapon weapon, Shield shield)
    {
        kills++;
        if (u != null && u.Unit != null) killedNames.Add(u.Unit.unitName);
        int lvl = current != null ? current.Level : 1;
        if (u != null && u.Unit != null) RivalManager.DefenderLost(current, u.Unit.unitName);

        // Убийце - кошель защитника
        if (by != null && by.Attacker && by.Alive && by.CarryFree > 0)
        {
            int gold = Mathf.Min(by.CarryFree * Loot.GoldPerSlot, Random.Range(5, 16) * lvl);
            by.Carried.gold += gold;
            CombatFx.LootFly(u.Position, by.Position);
        }

        // Трофейное снаряжение (поношенное): оружие и щит павшего - изредка (у NPC реже, чем у игроков). Домой, если кто-то вернётся
        var got = new List<string>();
        float gearChance = current != null && current.IsPlayer ? 0.25f : 0.10f;
        if (weapon != null && Random.value < gearChance)
        {
            weapon.durability = weapon.maxDurability * Random.Range(0.25f, 0.6f);
            trophyWeapons.Add(weapon);
            got.Add(weapon.Name);
        }
        if (shield != null && Random.value < gearChance)
        {
            shield.durability = shield.maxDurability * Random.Range(0.25f, 0.6f);
            trophyShields.Add(shield);
            got.Add(shield.Name);
        }
        if (got.Count > 0) WarManager.Notify("Трофей: " + string.Join(", ", got) + " (унесут домой, если кто-то вернётся)", false);
    }

    // ---------- Финал: голем в набеге поднимает павших (алтарь Ур.3) и взрывается лечением (Ур.2-3) ----------
    float golemReviveTimer = 1f;

    void GolemReviveTick(float dt)
    {
        golemReviveTimer -= dt;
        if (golemReviveTimer > 0f) return;
        golemReviveTimer = 1f;
        if (golemUnit == null || !golemUnit.Alive || golemUnit.Puppet) return;
        LifeAltar.Fallen f = LifeAltar.TakeFallenNear(golemUnit.Position, true);
        if (f == null) return;
        var pm = PopulationManager.Instance;
        BarracksController br = LifeAltar.BarracksFor(f);
        if (pm == null || br == null) { LifeAltar.PutBack(f); WarManager.Notify("Голем хотел поднять павшего, но дома в казармах нет места", false); return; }
        Vector2 home = pm.Hall != null ? pm.Hall.Center : Vector2.zero;
        CitizenAgent c = pm.SpawnAgent(home, f.mercenary);
        br.AdoptSoldier(c, f.unit, f.hearty, null, null, f.squad);
        if (!c.BeginExpedition(JitterHome(homeEdge))) { WarManager.Notify($"Голем поднял павшего: {f.unit.unitName} вернулся домой", true); return; }
        c.ForceAway();
        members.Add(c);
        Vector2 at = f.pos;
        RaidMap map = RaidMap.Instance;
        if (map != null)
        {
            Vector2Int cell = map.WorldToCell(at);
            if (map.IsBlocked(cell)) at = map.CellCenter(map.NearestFree(cell));
        }
        RaidUnit nu = RaidUnit.SpawnAttacker(c, c.Squad, at);
        if (State == Phase.Battle) nu.OrderAuto();
        LifeAltar.ReviveFx(at);
        WarManager.Notify($"Голем поднял павшего: {f.unit.unitName} снова в бою (без оружия)", true);
    }

    void GolemBurst(Vector2 at)
    {
        LifeAltar a = LifeAltar.Current;
        if (a == null || a.BurstHeal <= 0f) return;
        LifeAltar.BurstFx(at, a.BurstRadius);
        int n = 0;
        foreach (var o in new List<RaidUnit>(RaidUnit.All))
        {
            if (o == null || !o.Attacker || !o.Alive || o.IsGolem || Vector2.Distance(o.Position, at) > a.BurstRadius) continue;
            o.Heal(a.BurstHeal);
            CombatFx.HealBurst(o.Position);
            n++;
        }
        if (n > 0) WarManager.Notify($"Голем рассыпался зелёной вспышкой и подлечил бойцов: {n}", true);
    }

    public void OnAttackerKilled(RaidUnit u)
    {
        if (u.IsGolem)
        {
            if (!u.Puppet && !live) GolemBurst(u.Position);
            LifeAltar.ClearFallen(true);
            golemLost = true;
            golemUnit = null;
            LifeAltar.DiedInRaid();
            return;
        }
        lost++;
        if (!live && !u.Puppet && u.Source != null && golemUnit != null && golemUnit.Alive)
            LifeAltar.NoteFallen(u.Source.ExpUnit, u.Source.ExpHearty, u.Source.IsMercenary, u.Source.ExpBarracks, u.Squad, u.Position, true, u.Source.CitizenName);
        if (u.Carried.IsEmpty) return;

        // Мешок погибшего забирают защитники - он возвращается на склад Цитадели
        foreach (var b in RaidBuilding.All)
        {
            if (b != null && b.IsHall && !b.IsRuined)
            {
                u.Carried.MoveTo(b.Storage);
                WarManager.Notify("Добыча погибшего бойца вернулась в чужую Цитадель", false);
                return;
            }
        }
        WarManager.Notify($"Погибший боец обронил добычу: {u.Carried.ShortText}", false);
        u.Carried.Clear();
    }

    public void OnAttackerLeft(RaidUnit u)
    {
        if (u.IsGolem) { golemHpBack = Mathf.Max(1f, u.Hp); golemUnit = null; return; }
        if (u.Source == null || u.Source.IsDead) return;
        Loot l = new Loot();
        u.Carried.MoveTo(l);
        survivors[u.Source] = l;
    }

    // Переложить добычу (сначала ценное), не больше room мест. Мешок полон, а тут золото или сталь -
    // дешёвое (камень, дерево, еда) выкладывают обратно. Возвращает, сколько мест занято.
    static int TakeLoot(Loot from, Loot to, int room)
    {
        int moved = 0;
        // Трофеи места в мешке не занимают - берут всегда
        to.items.Add(from.items); from.items.Clear();
        to.copies.Add(from.copies); from.copies.Clear();
        if (room < from.ValuableWeight) room += Loot.MakeRoom(to, from, from.ValuableWeight - room);
        if (room <= 0) return 0;
        int coins = Mathf.Min(from.gold, room * Loot.GoldPerSlot);
        if (coins > 0) { from.gold -= coins; to.gold += coins; moved += Loot.GoldSlots(coins); }
        int Move(ref int a, ref int b)
        {
            int n = Mathf.Min(a, room - moved);
            if (n <= 0) return 0;
            a -= n;
            b += n;
            moved += n;
            return n;
        }

        for (int t = 2; t >= 0; t--) Move(ref from.steel[t], ref to.steel[t]);
        for (int t = 2; t >= 0; t--) Move(ref from.reinforced[t], ref to.reinforced[t]);
        for (int t = 2; t >= 0; t--) Move(ref from.plates[t], ref to.plates[t]);
        for (int t = 2; t >= 0; t--) Move(ref from.planks[t], ref to.planks[t]);
        Move(ref from.iron, ref to.iron);
        Move(ref from.food, ref to.food);
        Move(ref from.wood, ref to.wood);
        Move(ref from.stone, ref to.stone);
        Move(ref from.oats, ref to.oats);
        Move(ref from.berries, ref to.berries);
        Move(ref from.herbs, ref to.herbs);
        Move(ref from.potions, ref to.potions);
        return moved;
    }

    // ---------- Итог набега на игрока (сервер) ----------

    // Отправить итог один раз. empty - набега не было (отменили, никто не дошёл): деревня просто освобождается
    void SendResult(RaidTarget t, bool empty, Loot taken = null)
    {
        if (t == null || !t.IsPlayer || reported) return;
        reported = true;
        var req = new ApiRequest { raidId = t.RaidId, loot = new RaidLoot(), killed = new List<string>(), live = live };
        SpoilBundle stolen = null;
        if (!empty && fought)
        {
            req.loot = RaidLoot.From(taken);
            req.killed = new List<string>(killedNames);
            req.hallSacked = hallSacked;
            req.ruined = ruinedCount;
            req.fought = true;   // рейтинг лиги за набег (сервер)
            req.damage = DamagePercent();   // от неё зависит, сколько простоит щит его города
            if (!live)
            {
                req.ruinedAt = RuinedCells(t);   // у него в городе встанут руины
                stolen = TakeSpoilsOut(req.ruinedAt);
            }
        }
        if (live) CreditLiveSpoils();
        System.Action<ApiResponse> done = null;
        if (stolen != null && !stolen.IsEmpty)
        {
            // Отнятое - наше, только если сервер записал набег (тогда защитник его и потеряет)
            SpoilBundle mine = stolen;
            done = res =>
            {
                for (int i = mine.Kinds - 1; i >= 0; i--) SpoilsChest.Pending.Remove(mine.KeyAt(i), mine.CountAt(i));
                if (res.ok) SpoilsChest.Put(mine);
                else WarManager.Notify($"Отнятые трофеи не засчитаны (итог набега не дошёл до сервера) и остались у хозяина: {mine.Text}", false);
                AccountManager.SaveSoon();
            };
        }
        AccountManager.FinishRaid(req, done);
    }

    // Набег на копию деревни окончен: трофеи из мешков ушедших бойцов. Копии - сразу в сундук (у хозяина ничего не
    // пропадает), отнятое - в отчёт защитнику (cells: записи «#…», Spoils.Tokens) и ждёт ответа сервера
    SpoilBundle TakeSpoilsOut(List<string> cells)
    {
        var stolen = new SpoilBundle();
        var copies = new SpoilBundle();
        foreach (var kv in survivors)
        {
            if (kv.Value == null) continue;
            stolen.Add(kv.Value.items);
            copies.Add(kv.Value.copies);
            kv.Value.items.Clear();
            kv.Value.copies.Clear();
        }
        // Сервер пропускает не больше 300 записей о зданиях - трофеям место всегда найдётся (их немного)
        var tokens = Spoils.Tokens(stolen);
        if (cells.Count + tokens.Count > 300) cells.RemoveRange(0, Mathf.Min(cells.Count, cells.Count + tokens.Count - 300));
        cells.AddRange(tokens);
        SpoilsChest.Put(copies);
        SpoilsChest.Pending.Add(stolen);
        NoteSpoils(stolen, copies);
        return stolen;
    }

    // Живой бой: что унесли наши бойцы, защитник уже снял у себя - всё наше
    void CreditLiveSpoils()
    {
        if (liveStolen.IsEmpty && liveCopies.IsEmpty) return;
        NoteSpoils(liveStolen, liveCopies);
        SpoilsChest.Put(liveStolen.Copy());
        SpoilsChest.Put(liveCopies.Copy());
        liveStolen.Clear();
        liveCopies.Clear();
    }

    void NoteSpoils(SpoilBundle stolen, SpoilBundle copies)
    {
        var all = stolen.Copy();
        all.Add(copies);
        spoilsReport = all.IsEmpty ? "" : "Трофеи: " + all.Text;
        spoilsIcons = all;
        if (!all.IsEmpty) WarManager.Notify(spoilsReport + ". Разложим по складам, когда армия вернётся", true);
    }

    // Сколько процентов его построек (без стен, ворот и заслонов) мы разрушили. Руины прошлых набегов - не в счёт.
    // Поля боя уже нет (бой восстановлен из сохранения) - -1: сервер оценит сам
    int DamagePercent()
    {
        int main = 0, ruinedNow = 0;
        foreach (var b in RaidBuilding.All)
        {
            if (b == null || b.IsBarrier) continue;
            main++;
            if (b.IsRuined) ruinedNow++;
        }
        if (main == 0) return -1;
        int standing = main - Mathf.Max(0, ruinedNow - ruinedMain);
        return standing > 0 ? Mathf.Clamp(Mathf.RoundToInt(100f * ruinedMain / standing), 0, 100) : 0;
    }

    // Какие его здания мы разрушили: клетки в его городе («x,y»). Руины прошлого набега и наши заслоны - не в счёт
    List<string> RuinedCells(RaidTarget t)
    {
        var res = new List<string>();
        BotVillage.Plan plan = arrivedPlan ?? t.GetPlan();
        if (plan == null) return res;
        var before = new HashSet<Vector2Int>();
        foreach (var s in plan.Buildings) if (s != null && s.ruined) before.Add(s.origin);
        foreach (var b in RaidBuilding.All)
        {
            if (b == null || !b.IsRuined || b.IsBarricade || before.Contains(b.OriginCell)) continue;
            Vector2Int rc = RaidMap.RealOrigin(b.OriginCell, b.SizeCells, plan.Shift);   // в клетках его города (план повёрнут, final-8)
            res.Add(SaveBag.I(rc.x) + "," + SaveBag.I(rc.y));
        }
        return res;
    }

    static Loot Minus(Loot a, Loot b)
    {
        Loot r = new Loot();
        r.gold = Mathf.Max(0, a.gold - b.gold); r.wood = Mathf.Max(0, a.wood - b.wood); r.stone = Mathf.Max(0, a.stone - b.stone);
        r.food = Mathf.Max(0, a.food - b.food); r.iron = Mathf.Max(0, a.iron - b.iron); r.oats = Mathf.Max(0, a.oats - b.oats);
        r.berries = Mathf.Max(0, a.berries - b.berries); r.herbs = Mathf.Max(0, a.herbs - b.herbs);
        r.potions = Mathf.Max(0, a.potions - b.potions); r.resin = Mathf.Max(0, a.resin - b.resin);
        for (int i = 0; i < 3; i++)
        {
            r.planks[i] = Mathf.Max(0, a.planks[i] - b.planks[i]);
            r.plates[i] = Mathf.Max(0, a.plates[i] - b.plates[i]);
            r.steel[i] = Mathf.Max(0, a.steel[i] - b.steel[i]);
            r.reinforced[i] = Mathf.Max(0, a.reinforced[i] - b.reinforced[i]);
        }
        return r;
    }

    // Поиск противника на сервере: найденная деревня заменяет прежнюю
    void FindOpponent()
    {
        if (searching) return;
        searching = true;
        findError = "";
        AccountManager.FindOpponent(res =>
        {
            searching = false;
            if (!res.ok) { findError = res.error; return; }
            CityData c = null;
            try { c = JsonUtility.FromJson<CityData>(res.city); }
            catch (System.Exception) { c = null; }
            if (c == null || c.buildings == null || c.buildings.Count == 0) { findError = "Город противника повреждён — поищите другого"; return; }
            targets.RemoveAll(x => x.IsPlayer && x != current);
            targets.Insert(0, RaidTarget.FromCity(c, res.raidId));
            foundUtc = WorldClock.ServerUtc.ToString("o");   // найденная деревня держится за нами RaidLockMinutes (final-9: и после выхода)
            pickTarget = 0;
            villageScroll = Vector2.zero;
        });
    }

    // ---------- Конец боя ----------

    void Finish()
    {
        LifeAltar.ClearFallen(true);   // бой кончился - павших этого боя голем больше не поднимет
        // Деревня игрока: что из неё унесли (было в плане минус осталось на складах) - на сервер
        if (current != null && current.IsPlayer && live)
        {
            // Живой бой: унесли то, с чем бойцы ушли из его деревни
            Loot taken = new Loot();
            foreach (var kv in survivors) taken.Add(kv.Value);
            fought = fought || kills > 0 || ruinedCount > 0 || !taken.IsEmpty;
            SendResult(current, false, taken);
        }
        else if (current != null && current.IsPlayer && arrivedPlan != null)
        {
            Loot left = new Loot();
            foreach (var b in RaidBuilding.All) if (b != null) left.Add(b.Storage);
            SendResult(current, false, Minus(arrivedPlan.Total, left));
        }
        // Трофеи, которые так и не попали в итог набега (сервер его не ждал), - не наши: дома бойцы их не сдают
        foreach (var kv in survivors) if (kv.Value != null) { kv.Value.items.Clear(); kv.Value.copies.Clear(); }
        ClearLive();

        BattleRelay.End();

        // Поле боя убираем целиком
        foreach (var u in new List<RaidUnit>(RaidUnit.All)) if (u != null) Destroy(u.gameObject);
        foreach (var b in new List<RaidBuilding>(RaidBuilding.All)) if (b != null) Destroy(b.gameObject);
        if (campGo != null) Destroy(campGo);
        campGo = null;
        if (RaidMap.Instance != null) Destroy(RaidMap.Instance.gameObject);
        RaidMap.SetSide(0);
        RaidMap.SetPlanRot(0);
        if (FogOfWar.Raid != null) Destroy(FogOfWar.Raid.gameObject);
        if (RaidDefense.Instance != null) Destroy(RaidDefense.Instance.gameObject);
        FieldPost.ClearRaid();   // заслоны и пункты на поле боя пропадают вместе с ним
        Trap.ClearRaid();        // поставленные боевые ловушки - тоже
        mapShown = null;
        SetView(false);

        RivalManager.RaidFinished(current, fought, hallSacked, ruinedCount, kills);

        // Деревня игрока уходит из списка (в следующий раз - новый поиск).
        // Деревня-бот отстраивается заново (другая расстановка), пока - восстанавливается
        if (current != null && current.IsPlayer) targets.Remove(current);
        else if (current != null && (ruinedCount > 0 || kills > 0))
        {
            current.TimesRaided++;
            current.RecoverAt = Time.time + RecoverSeconds;
            current.Seed = Random.Range(1, int.MaxValue);
            current.PlanCache = null;
        }

        State = Phase.TravelHome;
        if (fought || hallSacked || ruinedCount > 0) Sfx.Sting(hallSacked || ruinedCount > 0 ? "victory" : "defeat");
        timer = current != null ? current.TravelSeconds : 20f;
        if (survivors.Count == 0 && golemHpBack <= 0f)
        {
            timer = 0.1f;
            WarManager.Notify("Из набега не вернулся никто", false);
        }
        else WarManager.Notify($"Армия возвращается домой: {survivors.Count} бойцов{(golemHpBack > 0f ? " и голем" : "")}, в пути {timer:F0} с", true);
    }

    /// <summary>Армия дома. atHome - пришла, пока игрока не было (обновление 14): бойцы уже у своих казарм,
    /// добыча уже сдана (раньше они снова появлялись у края карты и шли домой только после входа).</summary>
    void ReturnHome(bool atHome = false)
    {
        Loot total = new Loot();
        int back = 0;
        foreach (var kv in survivors)
        {
            CitizenAgent m = kv.Key;
            if (m == null || m.IsDead) continue;
            total.Add(kv.Value);
            if (atHome)
            {
                BuildingInstance home = m.ExpBarracks != null ? m.ExpBarracks.BuildingRef : null;
                Loot l = new Loot();
                if (kv.Value != null) l.Add(kv.Value);
                m.ReturnFromExpedition(home != null ? JitterHome(home.Center) : JitterHome(homeEdge), null);
                l.DepositToTown();
            }
            else m.ReturnFromExpedition(JitterHome(homeEdge), kv.Value);
            back++;
        }

        // Голем: ушёл с поля живым - возвращается к алтарю с теми же ранами; не вышел - погиб
        bool golemWent = golemOut || golemLost;
        bool golemBack = golemOut && golemHpBack > 0f && LifeAltar.RaidBound;
        if (golemOut)
        {
            if (golemBack) GolemHome(golemHpBack, atHome);
            else { LifeAltar.DiedInRaid(); golemOut = false; golemLost = true; }
        }

        // Кто числился в походе, но на поле не попал (такого быть не должно) - просто возвращается
        foreach (var m in members) if (m != null && !m.IsDead && m.IsOnExpedition) m.CancelExpedition();

        // Остаток еды - в казну, трофеи - в Оружейную
        var rm = ResourceManager.Instance;
        if (rm != null && food > 0 && back > 0) rm.Add(0, 0, 0, food);
        int foodBack = back > 0 ? food : 0;
        food = 0;
        UnloadKit(back > 0);   // уцелевшие укрепления возвращаются в запас Цитадели

        int trophies = 0, trophiesLost = 0;
        if (back > 0)
        {
            // Не влезло в Оружейные - в сундук трофеев: разложится, когда освободится место (обновление 23)
            var chest = new SpoilBundle();
            foreach (var w in trophyWeapons) { if (ArmoryController.Stash(w)) trophies++; else { chest.Add(Spoils.Of(w), 1); trophiesLost++; } }
            foreach (var s in trophyShields) { if (ArmoryController.StashShield(s)) trophies++; else { chest.Add(Spoils.Of(s), 1); trophiesLost++; } }
            SpoilsChest.Put(chest);
        }
        if (back > 0) SpoilsChest.DeliverNow();
        trophyWeapons.Clear();
        trophyShields.Clear();

        ReleaseSquads();

        // Отчёт
        var sb = new StringBuilder();
        sb.Append($"<b>Набег на {current.Name}</b> ({(current.IsPlayer ? (current.IsNpc ? "NPC-деревня" : "деревня игрока") : current.LevelName)})\n\n");
        if (back == 0) sb.Append("<color=#FF6060>Никто не вернулся.</color>\n");
        else if (hallSacked) sb.Append("<color=#80FF80>Цитадель разграблена!</color>\n");
        else if (resistanceBroken) sb.Append("<color=#80FF80>Защитники разбиты.</color>\n");
        else if (kills == 0 && ruinedCount == 0) sb.Append("Армия ушла без боя.\n");
        else sb.Append("<color=#FFD060>Набег прерван.</color>\n");
        sb.Append($"Бойцов: ушло {startCount}, вернулось <b>{back}</b>, погибло <color=#FF8060>{lost}</color>\n");
        sb.Append($"Убито защитников: {kills}/{defendersTotal}, разрушено зданий: {ruinedCount}\n");
        if (golemWent) sb.Append(golemBack ? UiKit.Col(UiKit.GoodHex, "Голем вернулся к алтарю.") + "\n" : UiKit.Col(UiKit.BadHex, "Голем погиб — растите нового на алтаре.") + "\n");
        // Добыча и трофеи - картинками под текстом (DrawReport), а не списком (обновление final-1)
        reportLoot = total.IconEntries();
        Spoils.AddEntries(spoilsIcons, reportLoot);
        if (reportLoot.Count == 0) sb.Append("Добыча: ничего\n");
        if (!bountyPaid.IsEmpty) sb.Append($"Награда за победу (уже в казне): <color=#FFD040>{bountyPaid.ShortText}</color>\n");
        string overflow = OverflowText(total);
        if (overflow != "") sb.Append($"<color=#FF9060>Не хватит места в Цитадели и Хранилищах: пропадёт ~{overflow}. Постройте или улучшите Хранилище.</color>\n");
        if (trophies > 0) sb.Append($"Трофейное снаряжение на складе Оружейной: {trophies}\n");
        if (trophiesLost > 0) sb.Append($"<color=#FF9060>Трофейному снаряжению не хватило места в Оружейных: {trophiesLost}. Оно ждёт в сундуке трофеев и само ляжет в Оружейную, когда там освободится место</color>\n");

        if (SpoilsChest.Line != "") sb.Append($"<color=#FF9060>{SpoilsChest.Line}</color>\n");
        if (foodBack > 0) sb.Append($"Остаток припасов вернулся в казну: {foodBack} еды\n");
        sb.Append(atHome ? "\n<color=#A0A0A0>Армия пришла, пока вас не было: добыча уже в казне.</color>" : "\n<color=#A0A0A0>Бойцы сами отнесут добычу в казарму.</color>");
        report = sb.ToString();

        WarManager.Notify($"Армия вернулась из набега: {back} бойцов", back > 0);
        survivors.Clear();
        members.Clear();
        current = null;
        State = Phase.Idle;
    }

    // ---------- Камера ----------

    void SetView(bool battle)
    {
        bool can = battle && RaidMap.Instance != null;
        if (can == viewBattle) return;
        Camera cam = Camera.main;
        viewBattle = can;

        if (viewBattle)
        {
            if (cam != null) homeCamPos = cam.transform.position;
            CameraController.SetViewArea(RaidMap.Origin, RaidMap.Size);
            Vector2 focus = landing + RaidMap.WorldDir(new Vector2(8f, 0f));
            var att = Attackers();
            if (att.Count > 0)
            {
                Vector2 c = Vector2.zero;
                foreach (var u in att) c += u.Position;
                focus = c / att.Count;
            }
            if (CameraController.Instance != null) CameraController.Instance.FocusOn(focus);
        }
        else
        {
            CameraController.ClearViewArea();
            if (CameraController.Instance != null) CameraController.Instance.FocusOn(homeCamPos);
        }
    }

    // ---------- Управление боем (мышь) ----------

    void HandleBattleInput()
    {
        Mouse mouse = Mouse.current;
        Camera cam = Camera.main;
        if (mouse == null || cam == null) return;

        Vector2 sp = mouse.position.ReadValue();
        Vector2 gp = UiKit.ToGui(sp);
        Vector3 w3 = cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, -cam.transform.position.z));
        Vector2 world = new Vector2(w3.x, w3.y);

        bool overUI = IsPointerOverUI(gp) || FieldPostManager.Blocks(gp) || TrapManager.Blocks(gp) || DevelopmentPath.IsPointerOver(gp) || WeaponForge.IsPointerOver(gp)
            || (GameMenu.Instance != null && GameMenu.Instance.IsPointerOver(gp))
            || (GameClock.Instance != null && GameClock.Instance.IsPointerOver(gp));
        if (overUI) CameraController.BlockZoom();

        hoverBuilding = null;
        hoverUnit = null;
        if (!overUI)
        {
            hoverUnit = PickUnit(world, null);
            if (hoverUnit != null && !hoverUnit.Attacker && !FogOfWar.IsVisible(hoverUnit.Position)) hoverUnit = null;   // в тумане не видно
            if (hoverUnit == null && FogOfWar.IsExplored(world))
                foreach (var b in RaidBuilding.All)
                    if (b != null && b.Contains(world)) { hoverBuilding = b; break; }
        }

        if (overUI || !mouse.leftButton.wasPressedThisFrame) return;

        // Свой боец - выбрать его отряд (Shift - добавить)
        RaidUnit own = PickUnit(world, true);
        // final-13: отряд выбран, и нажали по врагу, который стоит в куче своих - это приказ «бить его»,
        // а не выбор своего бойца (раньше в гуще боя палец почти всегда попадал в своего, и приказ пропадал)
        if (own != null && OrdersArmed && State == Phase.Battle)
        {
            RaidUnit near = hoverUnit != null && !hoverUnit.Attacker ? hoverUnit : PickUnit(world, false);
            if (near != null && !near.Attacker && FogOfWar.IsVisible(near.Position)
                && Vector2.Distance(world, near.Position) < Vector2.Distance(world, own.Position)) own = null;
        }
        // Голем: приказов не слушает, но его можно выбрать и посмотреть карточку (финал)
        if (own != null && own.IsGolem) { inspectedUnit = inspectedUnit == own ? null : own; return; }
        inspectedUnit = null;
        if (own != null && own.Squad != null && (!selected.Contains(own.Squad) || !OrdersArmed))
        {
            bool add = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
            if (!add) selected.Clear();
            if (!selected.Add(own.Squad) && add) selected.Remove(own.Squad);
            allSquads = false;
            return;
        }

        if (State != Phase.Battle) return;
        RaidMap map = RaidMap.Instance;
        if (map == null || !map.InBounds(map.WorldToCell(world))) return;

        // Обновление 12: ни один отряд не выбран и «Все» не нажата - касание карты ничего не приказывает
        // (раньше любой случайный тап, пока листали карту, отправлял туда всю армию)
        if (!OrdersArmed) { NoSquadHint(); return; }
        var list = OrderTargets();
        if (list.Count == 0) return;
        if (live)
        {
            // Живой бой: приказ уходит в деревню защитника
            if (hoverBuilding != null && !hoverBuilding.IsRuined)
            {
                Vector2Int lc = LiveCell(hoverBuilding);
                Send(BattleCommand.Focus, list, lc.x, lc.y);
                WarManager.Notify($"Приказ: ломать {hoverBuilding.Title}", true);
            }
            else if (hoverUnit != null && !hoverUnit.Attacker && hoverUnit.Puppet)
                Send(BattleCommand.Attack, list, 0f, 0f, hoverUnit.NetId);
            else
            {
                // Клик - идут с боем по пути; двойной клик - марш (target = 1)
                bool march = IsDoubleGroundClick(sp);
                Vector2 at = NetPos(world);
                Send(BattleCommand.Move, list, at.x, at.y, march ? 1 : 0);
                if (march) WarManager.Notify("Марш: идут к точке, не ввязываясь в бой", true);
            }
            return;
        }
        // Нажали по защитнику - бить именно его (final-9). Раньше это был просто «идти в точку»,
        // и бойцы по дороге били первое попавшееся здание или другого врага
        RaidUnit foe = hoverUnit != null && !hoverUnit.Attacker ? hoverUnit : PickEnemyNear(world);
        if (foe != null)
        {
            foreach (var u in list) u.OrderAttack(foe);
            groundClickAt = -99f;
            CombatFx.Ring(foe.Position, 0.6f, new Color(1f, 0.35f, 0.3f, 0.8f));   // final-13: без надписи, только кольцо
        }
        else if (hoverBuilding != null && !hoverBuilding.IsRuined)
        {
            foreach (var u in list) u.OrderFocus(hoverBuilding);
            CombatFx.Ring(hoverBuilding.Center, 1.2f, new Color(1f, 0.6f, 0.2f, 0.8f));   // final-13: без надписи, только кольцо
        }
        else
        {
            // Клик - идут к точке и бьют всех, кто встретится по дороге; двойной клик - марш, ни на что не отвлекаясь
            bool march = IsDoubleGroundClick(sp);
            foreach (var u in list) u.OrderHold(world, march);
            // final-13: вместо надписи - кольцо в точке приказа (марш - голубое и крупнее)
            CombatFx.Ring(world, march ? 0.9f : 0.5f, march ? new Color(0.45f, 0.8f, 1f, 0.9f) : new Color(1f, 1f, 1f, 0.6f));
        }
    }

    // Двойной клик по земле: второй клик быстро и рядом с первым - марш.
    // На телефоне - дольше и дальше (TouchInput.DoubleSeconds / DoublePixels, final-9): 30 пикселей - это 2 мм под пальцем
    float groundClickAt = -99f;
    Vector2 groundClickScreen;

    // На телефоне палец не так точен: защитник в клетке от нажатия - тоже «по нему» (только видимый)
    static RaidUnit PickEnemyNear(Vector2 world)
    {
        if (!UiKit.Phone) return null;
        RaidUnit best = null;
        float bestD = 1.1f;
        foreach (var u in RaidUnit.All)
        {
            if (u == null || !u.Alive || u.Attacker || !FogOfWar.IsVisible(u.Position)) continue;
            float d = Vector2.Distance(world, u.Position);
            if (d < bestD) { bestD = d; best = u; }
        }
        return best;
    }

    bool IsDoubleGroundClick(Vector2 screen)
    {
        bool dbl = Time.unscaledTime - groundClickAt <= TouchInput.DoubleSeconds && (screen - groundClickScreen).magnitude <= TouchInput.DoublePixels;
        groundClickAt = dbl ? -99f : Time.unscaledTime;   // третий клик - снова одиночный
        groundClickScreen = screen;
        return dbl;
    }

    static RaidUnit PickUnit(Vector2 p, bool? attackers)
    {
        RaidUnit best = null;
        float bestD = 0.55f;
        foreach (var u in RaidUnit.All)
        {
            if (u == null || !u.Alive) continue;
            if (attackers.HasValue && u.Attacker != attackers.Value) continue;
            float d = Vector2.Distance(p, u.Position);
            if (d < bestD) { bestD = d; best = u; }
        }
        return best;
    }

    // ---------- Интерфейс ----------

    void Publish(Rect r) { frameRects.Add(r); }

    void OnGUI()
    {
        if (WarView.IsOpen) return;   // открыта карта клановой войны
        UiKit.Apply();
        frameRects.Clear();
        planOpen = UiKit.KeepFront("raid", planOpen, ref planWasOpen);   // одно большое окно за раз

        DrawCornerButton();
        if (statusOpen) DrawStatusPanel();
        if (planOpen && State == Phase.Idle) DrawPlanWindow();
        if (ViewingBattle)
        {
            // Полоски здоровья и стрелки - только при отрисовке (OnGUI зовётся по нескольку раз за кадр,
            // а в большом бою это сотни бойцов на каждый вызов)
            if (Event.current.type == EventType.Repaint)
            {
                DrawBars();
                DrawMarkers();
            }
            DrawBattleHud();
            DrawRaidClock();
            DrawSquadBar();
            DrawTooltip();
        }
        if (mapShown != null) DrawMapWindow();
        if (report != null) DrawReport();

        if (Event.current.type == EventType.Repaint) publishedRects = frameRects.ToArray();
    }

    // Обновление 12: стили не создаются заново на каждый вызов OnGUI (это сотни объектов в секунду - рывки сборщика мусора)
    static readonly Dictionary<GUIStyle, GUIStyle> oneLineCache = new Dictionary<GUIStyle, GUIStyle>();
    static readonly Dictionary<GUIStyle, GUIStyle> bigCache = new Dictionary<GUIStyle, GUIStyle>();
    static readonly Dictionary<int, GUIStyle> sizedCache = new Dictionary<int, GUIStyle>();
    static GUIStyle sizedBase;
    static Color sizedColor;

    GUIStyle Style(int size, bool wrap = true)
    {
        if (sizedBase != GUI.skin.label || sizedColor != UiKit.Text) { sizedCache.Clear(); sizedBase = GUI.skin.label; sizedColor = UiKit.Text; }
        int key = size * 2 + (wrap ? 1 : 0);
        if (sizedCache.TryGetValue(key, out var c)) return c;
        GUIStyle st = new GUIStyle(GUI.skin.label) { fontSize = size, richText = true, wordWrap = wrap };
        st.normal.textColor = UiKit.Text;
        sizedCache[key] = st;
        return st;
    }

    static GUIStyle Cached(Dictionary<GUIStyle, GUIStyle> cache, GUIStyle from, System.Func<GUIStyle, GUIStyle> make)
    {
        if (from == null) return make(GUI.skin.label);
        if (cache.TryGetValue(from, out var c)) return c;
        if (cache.Count > 64) cache.Clear();   // стили UiKit пересоздаются при смене масштаба - старые выкидываем
        c = make(from);
        cache[from] = c;
        return c;
    }

    // Строка в одну линию (лишнее обрезается, полностью - в подсказке)
    static GUIStyle OneLine(GUIStyle from)
    {
        return Cached(oneLineCache, from, f => new GUIStyle(f) { wordWrap = false, clipping = TextClipping.Clip });
    }

    // Крупная кнопка (угловая, «Выступить», «Атаковать»)
    static GUIStyle BigStyle(UiKit.Kind k)
    {
        return Cached(bigCache, UiKit.ButtonStyle(k), f => new GUIStyle(f) { fontSize = 14, fontStyle = FontStyle.Bold });
    }

    static bool BigButton(Rect r, string text, bool enabled, UiKit.Kind k, string tip)
    {
        bool en = GUI.enabled;
        GUI.enabled = en && enabled;
        bool b = LocGUI.Button(r, text, BigStyle(k));
        GUI.enabled = en;
        UiKit.Tip(r, tip);
        return b && enabled;
    }

    static string HpHex(int pct) { return pct >= 75 ? UiKit.GoodHex : (pct >= 50 ? UiKit.WarnHex : UiKit.BadHex); }

    void DrawCornerButton()
    {
        if (WarCityMode.Active && State == Phase.Idle && !planOpen) return;   // в военной деревне набегов нет: угол - отрядам войны
        Rect r = new Rect(UiKit.W - 190f, UiKit.H - 50f, 180f, 40f);
        Publish(r);

        switch (State)
        {
            case Phase.Idle:
                if (BigButton(r, planOpen ? "Закрыть" : "В бой!", true, planOpen ? UiKit.Kind.Normal : UiKit.Kind.Primary,
                    planOpen ? null : "Набег на соседнюю деревню: цель, отряды, припасы"))
                { planOpen = !planOpen; if (planOpen) PreparePlan(); }
                break;

            case Phase.Marching:
            case Phase.TravelOut:
            case Phase.TravelHome:
                string label = State == Phase.Marching ? "Армия выступает..."
                             : (State == Phase.TravelOut ? (arriving ? "Армия у деревни…" : $"В пути: {Mathf.CeilToInt(timer)} с") : $"Домой: {Mathf.CeilToInt(timer)} с");
                if (BigButton(r, label, true, UiKit.Kind.Normal, "Состояние похода")) statusOpen = !statusOpen;
                break;

            default:
                bool war = WarManager.IsWar;
                if (viewBattle)
                {
                    if (BigButton(r, war ? "К деревне!" : "К деревне", true, war ? UiKit.Kind.Danger : UiKit.Kind.Normal,
                        war ? "На вашу деревню напали — идёт война! (V)" : "Показать свою деревню (V)")) SetView(false);
                }
                else if (BigButton(r, "К битве", true, UiKit.Kind.Primary, "Показать поле набега (V)")) SetView(true);
                break;
        }
    }

    void DrawStatusPanel()
    {
        if (State == Phase.Idle) { statusOpen = false; return; }
        // Над малыми кнопками «Ловушки» / «Заслоны» (раньше окно лежало на них)
        float sh = UiKit.WindowH(22f + 6f + 28f);
        Rect r = new Rect(UiKit.W - 330f, FieldPostManager.SideButtonRect(0).y - 6f - sh, 320f, sh);
        Publish(r);

        string where = current != null ? current.Name : "—";
        Rect c = UiKit.Window(r, State == Phase.TravelHome ? $"Возвращение: {where}" : $"Поход: {where}", out bool close);
        if (close) { statusOpen = false; return; }

        string text = State == Phase.TravelHome
            ? $"Бойцов возвращается: <b>{survivors.Count}</b>"
            : $"Бойцов: <b>{members.Count}</b>   еда в запасе: <b>{food}</b>";
        LocGUI.Label(new Rect(c.x, c.y, c.width, 22f), text, OneLine(UiKit.Body));

        bool can = State == Phase.Marching || State == Phase.TravelOut;
        if (UiKit.Button(new Rect(c.x, c.y + 28f, c.width, 28f), "Отменить поход", can, UiKit.Kind.Danger,
            can ? "Армия вернётся, еда и снаряжение — тоже" : "Армия уже у цели — отменить нельзя"))
            ConfirmDialog.Ask("Отменить поход?", "Армия развернётся и пойдёт домой. Еда и снаряжение вернутся вместе с ней.", "Вернуть армию", Recall);
    }

    // ---------- Окно похода ----------

    void PreparePlan()
    {
        picked.Clear();
        if (pickTarget < 0 || pickTarget >= targets.Count || !targets[pickTarget].Ready)
        {
            pickTarget = targets.FindIndex(t => t.Ready);
            if (pickTarget < 0) pickTarget = 0;
        }
    }

    static GUIStyle planName, planSmall;

    void DrawPlanWindow()
    {
        float w = Mathf.Min(880f, UiKit.W - 40f), h = Mathf.Min(620f, UiKit.H - 100f);
        Rect r = new Rect((UiKit.W - w) / 2f, (UiKit.H - h) / 2f - 20f, w, h);
        if (UiKit.Phone) { r = UiKit.CenterWindow(880f, 620f); w = r.width; h = r.height; }
        Publish(r);
        CameraController.BlockZoom();
        Rect c = UiKit.Window(r, online ? "Набег на деревню игрока" : "Набег на соседнюю деревню", out bool close);
        if (close) { planOpen = false; return; }

        GUIStyle line = OneLine(UiKit.Body), smallLine = OneLine(UiKit.Small), mutedLine = OneLine(UiKit.MutedStyle);
        GUIStyle mutedRight = new GUIStyle(mutedLine) { alignment = TextAnchor.LowerRight };

        // --- Деревни ---
        float leftW = Mathf.Min(360f, c.width * 0.45f);
        int scoutsHome = FreeScouts().Count;
        float cy = UiKit.Section(c.x, c.y, leftW, "Куда идти");
        Rect sh = new Rect(c.x + leftW - 160f, c.y, 160f, UiKit.SectionH - 4f);
        LocGUI.Label(sh, $"разведчиков дома: {scoutsHome}", mutedRight);
        UiKit.Tip(sh, "Разведчики обучаются в Казарме ур. 2. Разведка приносит набросок плана деревни.");
        cy += 2f;

        // В аккаунте: противника подбирает сервер среди других игроков
        if (online)
        {
            bool canFind = !searching && State == Phase.Idle && !launching;
            string fl = searching ? "Ищем противника…" : (targets.Count > 0 ? "Найти другого противника" : "Найти противника");
            if (UiKit.Button(new Rect(c.x, cy, leftW, 28f), fl, canFind, UiKit.Kind.Primary,
                "Сервер подберёт деревню другого игрока похожей силы. Города под щитом не попадаются. Пока идёт ваш набег, на эту деревню не нападёт никто другой.")) FindOpponent();
            cy += 30f;
            string hint = findError != "" ? UiKit.Col(UiKit.BadHex, findError)
                        : (targets.Count == 0 ? UiKit.Col(UiKit.MutedHex, "Противник ещё не выбран") : UiKit.Col(UiKit.MutedHex, $"На бой у деревни — {Api.RaidBattleSeconds / 60f:F0} мин"));
            LocGUI.Label(new Rect(c.x, cy, leftW, 20f), hint, smallLine);
            cy += 22f;
            if (CityShield.Active)
            {
                Rect sr = new Rect(c.x, cy, leftW, 20f);
                LocGUI.Label(sr, UiKit.Col(UiKit.InfoHex, $"Ваш город под щитом ещё {CityShield.LeftText}"), smallLine);
                UiKit.Tip(sr, "Пока стоит щит, другие игроки не видят ваш город во вкладке «Бой». Снять его можно кнопкой «Щит города» слева вверху.");
                cy += 22f;
            }
        }

        var rmRes = ResourceManager.Instance;
        const float cardH = 112f, cardStep = 118f;
        Rect villView = new Rect(c.x, cy, leftW, c.yMax - cy);
        float cardW = leftW - 14f;
        villageScroll = UiKit.BeginScrollView(villView, villageScroll, new Rect(0f, 0f, cardW, targets.Count * cardStep));
        float vy = 0f;
        for (int i = 0; i < targets.Count; i++)
        {
            RaidTarget t = targets[i];
            ScoutReport rep = ReportFor(t);
            ScoutMission mis = MissionFor(t);
            Rect card = new Rect(0f, vy, cardW, cardH);
            LocGUI.Box(card, GUIContent.none);
            if (i == pickTarget)
            {
                Color oc = GUI.color;
                GUI.color = UiKit.Good;
                GUI.DrawTexture(new Rect(card.x + 1f, card.y + 3f, 3f, card.height - 6f), Texture2D.whiteTexture);
                GUI.color = oc;
            }

            float tx = card.x + 10f, tw = card.width - 20f;
            // Название, уровень, дорога
            string kindName = t.IsPlayer ? (t.IsNpc ? "NPC · Цитадель ур. " : "игрок · Цитадель ур. ") + CityLoss.HallLevel(t.City) : t.LevelName + " · Ур. " + t.Level;
            const float roadW = 128f;
            LocGUI.Label(new Rect(tx, card.y + 5f, tw - roadW, 22f), $"<b>{t.Name}</b>  {UiKit.Col(UiKit.MutedHex, kindName)}", line);
            Rect road = new Rect(tx + tw - roadW, card.y + 5f, roadW, 20f);
            int aside = t.ApproachSide;
            LocGUI.Label(road, UiKit.Col(UiKit.AccentHex, RaidMap.SideArrow(aside)) + " " + UiKit.Col(UiKit.AccentHex, RaidMap.SideName(aside)) + UiKit.Col(UiKit.MutedHex, $" · {t.TravelSeconds:F0} с"),
                new GUIStyle(mutedLine) { alignment = TextAnchor.UpperRight });
            UiKit.Tip(road, ApproachTip(aside) + $"\nДорога в одну сторону: {t.TravelSeconds:F0} с");

            // Что известно
            string info, infoTip;
            if (!t.Ready)
            {
                info = UiKit.Col(UiKit.BadHex, $"Разорена — отстраивается ещё {FormatTime(t.RecoverAt - Time.time)}");
                infoTip = "После набега деревня отстраивается заново (с другой расстановкой).";
            }
            else if (rep != null)
            {
                info = UiKit.Col(UiKit.GoodHex, $"Карта ~{rep.ShownAccuracy}%") + UiKit.Col(UiKit.MutedHex, " · ") + rep.ShortSummary;
                infoTip = $"Карта разведчика, точность ~{rep.ShownAccuracy}%\n{rep.SummaryText}" +
                          (rep.Valid && rep.StyleGuess != "" ? $"\nДеревня {rep.StyleGuess}" : "") +
                          $"\nНаграда за Цитадель (сразу в казну): ~{HallBountyEstimate(t).ShortText}";
            }
            else if (mis != null)
            {
                info = UiKit.Col(UiKit.WarnHex, $"Разведчик: {MissionText(mis)}");
                infoTip = "Что внутри — пока неизвестно.";
            }
            else
            {
                info = UiKit.Col(UiKit.MutedHex, "Не разведана — что за частоколом, неизвестно");
                infoTip = "Отправьте разведчика (Казарма ур. 2).";
            }
            Rect ir = new Rect(tx, card.y + 28f, tw, 20f);
            LocGUI.Label(ir, info, smallLine);
            UiKit.Tip(ir, infoTip);

            // Соседи-соперники: отношение и войско (нрав и занятие - в подсказке)
            if (t.IsPlayer)
            {
                Rect pr = new Rect(tx, card.y + 48f, tw, 20f);
                LocGUI.Label(pr, $"Войско дома: {t.HomeCount} · постройки ставил сам игрок", smallLine);
                UiKit.Tip(pr, "Если игрок не в игре, деревню защищают его бойцы и постройки сами.");
            }
            else if (RivalManager.Active)
            {
                Rect rr = new Rect(tx, card.y + 48f, tw, 20f);
                LocGUI.Label(rr, $"{RivalManager.TraitName(t.Trait)} · {RivalManager.AttitudeText(t)} · {RivalManager.ArmyText(t, rep)}", smallLine);
                UiKit.Tip(rr, RivalManager.CardLine(t, rep));
            }

            // Кнопки карточки
            float by0 = card.yMax - 34f, bw3 = 70f, bw1 = 84f, bw2 = tw - bw1 - bw3 - 8f;
            string selTip = !t.Ready ? "Деревня ещё не восстановилась" : null;
            if (UiKit.Button(new Rect(tx, by0, bw1, 26f), i == pickTarget ? "✓ Цель" : "Выбрать", t.Ready && i != pickTarget,
                UiKit.Kind.Normal, selTip)) pickTarget = i;

            int cost = ScoutCost(t);
            bool canScout = t.Ready && mis == null && scoutsHome > 0 && rmRes != null && rmRes.gold >= cost;
            string scoutLabel = mis != null ? "Разведчик в пути"
                              : (scoutsHome == 0 ? "Нет разведчика"
                              : (rep != null ? $"Ещё раз · {cost} з" : $"Разведать · {cost} з"));
            string scoutTip = !t.Ready ? "Деревня отстраивается"
                            : mis != null ? "Разведчик уже идёт к этой деревне"
                            : scoutsHome == 0 ? "Нужен свободный разведчик (обучается в Казарме ур. 2)"
                            : (rmRes == null || rmRes.gold < cost) ? $"Разведка стоит {cost} золота — не хватает"
                            : $"Разведка: {cost} золота. Разведчик принесёт набросок плана деревни.";
            if (UiKit.Button(new Rect(tx + bw1 + 4f, by0, bw2, 26f), scoutLabel, canScout, UiKit.Kind.Normal, scoutTip)) StartScout(t);

            if (UiKit.Button(new Rect(tx + tw - bw3, by0, bw3, 26f), "Карта", rep != null, UiKit.Kind.Normal,
                rep != null ? "Открыть карту разведчика" : "Карты нет — сначала разведайте")) mapShown = rep;

            vy += cardStep;
        }
        GUI.EndScrollView();

        // --- Отряды ---
        float rx = c.x + leftW + 16f, rw = c.xMax - rx;
        var avail = AvailableSquads();
        picked.RemoveWhere(sq => sq == null || !avail.Contains(sq));

        float ry = UiKit.Section(rx, c.y, rw, "Кто идёт");
        if (LocGUI.Button(new Rect(rx + rw - 130f, c.y, 60f, 18f), "Все")) foreach (var sq in avail) picked.Add(sq);
        if (LocGUI.Button(new Rect(rx + rw - 66f, c.y, 66f, 18f), "Никого")) picked.Clear();
        ry += 2f;

        // Нижний блок (снаряжение, припасы, кнопка) - фиксированной высоты
        float kitH = KitKinds().Count * 24f + 24f;   // строки укреплений + строка ловушек
        bool golemRow = LifeAltar.CanJoinRaid;
        float bottomH = UiKit.SectionH + kitH + 4f + UiKit.SectionH + 24f + 40f + 22f + 36f;   // final-13-fix-2: лагерь и голем - плитки в один ряд
        float listH = Mathf.Max(40f, c.yMax - bottomH - ry - 6f);
        // Обновление 13: отряды - карточками, как в бою: картинка бойца, которого больше всего, имя, состав, здоровье
        bool twoCols = rw >= 330f;
        float sqH = 58f, gapC = 4f;
        float innerW = rw - 14f;
        float sqW = twoCols ? (innerW - gapC) / 2f : innerW;
        int rowsN = twoCols ? Mathf.CeilToInt(avail.Count / 2f) : avail.Count;
        Rect view = new Rect(rx, ry, rw, listH);
        planScroll = UiKit.BeginScrollView(view, planScroll, new Rect(0f, 0f, innerW, Mathf.Max(listH, rowsN * (sqH + gapC))));
        if (avail.Count == 0)
            LocGUI.Label(new Rect(0f, 0f, innerW, 60f), UiKit.Col(UiKit.MutedHex, "Нет отрядов. Обучите бойцов в Казарме — каждый новый боец попадает в отряд."), UiKit.Body);
        if (planName == null)
        {
            planName = new GUIStyle(UiKit.Small) { fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip, richText = true };
            planSmall = new GUIStyle(UiKit.Small) { wordWrap = false, clipping = TextClipping.Clip, richText = true };
        }
        for (int i = 0; i < avail.Count; i++)
        {
            Squad sq = avail[i];
            var soldiers = SoldiersOf(sq);
            var counts = new Dictionary<string, int>();
            var types = new Dictionary<UnitType, int>();
            UnitType best = null;
            int bestN = 0;
            float hpSum = 0f;
            int horses = 0;
            foreach (var m in soldiers)
            {
                counts.TryGetValue(m.DisplayName, out int cnt);
                counts[m.DisplayName] = cnt + 1;
                hpSum += m.HpFraction;
                if (m.HasHorse) horses++;
                UnitType ut = m.ExpUnit;
                if (ut == null) continue;
                types.TryGetValue(ut, out int tn);
                types[ut] = ++tn;
                if (tn > bestN) { bestN = tn; best = ut; }
            }
            var parts = new List<string>();
            foreach (var kv in counts) parts.Add($"{kv.Key} {kv.Value}");
            int hpPct = Mathf.RoundToInt(hpSum / Mathf.Max(1, soldiers.Count) * 100f);
            int carry = (soldiers.Count - horses) * CarryFoot + horses * CarryMounted;

            bool on = picked.Contains(sq);
            int col = twoCols ? i % 2 : 0, row = twoCols ? i / 2 : i;
            Rect cr0 = new Rect(col * (sqW + gapC), row * (sqH + gapC), sqW, sqH);
            if (LocGUI.Button(cr0, GUIContent.none, UiKit.ButtonStyle(on ? UiKit.Kind.Primary : UiKit.Kind.Normal)))
            {
                if (on) picked.Remove(sq); else picked.Add(sq);
            }
            Rect ir = new Rect(cr0.x + 4f, cr0.y + 4f, sqH - 8f, sqH - 8f);
            if (best != null && UnitIcons.Has(best)) UnitIcons.Draw(ir, best);
            float tx = ir.xMax + 4f, tw = cr0.xMax - tx - 4f;
            LocGUI.Label(new Rect(tx, cr0.y + 3f, tw, 18f), (on ? "✓ " : "") + sq.Name, planName);
            LocGUI.Label(new Rect(tx, cr0.y + 20f, tw, 18f), $"{soldiers.Count} бойц. · <color={HpHex(hpPct)}>{hpPct}%</color>" + (horses > 0 ? $" · коней {horses}" : ""), planSmall);
            LocGUI.Label(new Rect(tx, cr0.y + 37f, tw, 18f), UiKit.Col(UiKit.MutedHex, string.Join(", ", parts)), planSmall);
            UiKit.Tip(cr0, $"{BarracksLabel(sq)}\nСостав: {string.Join(", ", parts)}\nЗдоровье: {hpPct}%\nУнесут добычи: до {carry}\n" + (on ? "Идёт в набег — нажмите, чтобы оставить дома" : "Нажмите, чтобы взять в набег"));
        }
        GUI.EndScrollView();

        // --- Снаряжение ---
        float by = c.yMax - bottomH;
        by = UiKit.Section(rx, by, rw, "Снаряжение в поход");
        DrawKitRows(rx, by, rw);
        by += kitH + 4f;

        // --- Припасы и выступление ---
        by = UiKit.Section(rx, by, rw, "Припасы");
        int n = PickedSoldiers();
        var rm = ResourceManager.Instance;
        int haveFood = rm != null ? rm.food : 0, haveWood = rm != null ? rm.wood : 0;
        int needFood = PickedRations() * FoodMinutes;
        string fcol = haveFood >= needFood ? UiKit.GoodHex : UiKit.BadHex;
        Rect fr = new Rect(rx, by, rw, 22f);
        // final-13-fix-2: значки вместо голого текста
        float half = (rw - 10f) * 0.5f;
        GameUi.IconLine(new Rect(rx, by, half, 22f), "ui_helmet", $"Бойцов: <b>{n}</b>", line);
        GameUi.IconLine(new Rect(rx + half + 10f, by, half, 22f), "food", $"<color={fcol}><b>{needFood}</b></color> {UiKit.Col(UiKit.MutedHex, "(в казне " + haveFood + ")")}", line);
        UiKit.Tip(fr, $"Еда — запас похода на {FoodMinutes} минут: пеший боец ест 1 в минуту, крупный — по числу мест в казарме, у всадника ещё 1 за коня. Остаток вернётся домой. Без еды бойцы слабеют.\n" +
                      $"Добычу несут в мешках: пеший — {CarryFoot}, конный — {CarryMounted} мест. Золото лёгкое: {Loot.GoldPerSlot} монеты на место.");
        by += 24f;

        // Лагерь у высадки и голем Алтаря жизни - плитки со значками в один ряд
        string wcol = haveWood >= CampWood ? UiKit.GoodHex : UiKit.BadHex;
        float tileW = golemRow ? half : rw;
        Rect cr = new Rect(rx, by, tileW, 36f);
        takeCamp = GameUi.OptionTile(cr, "ui_camp", $"Лагерь у высадки\n<color={wcol}>{CampWood} дерева</color>", takeCamp,
            "Лагерь лечит бойцов рядом и даёт укрытие. Дерево тратится.");

        if (golemRow)
        {
            string gp = LifeAltar.RaidProblem();
            LifeAltar alt = LifeAltar.Current;
            Rect gr = new Rect(rx + half + 10f, by, half, 36f);
            if (gp == "")
            {
                int ghp = alt != null && alt.GolemMaxHp > 0f ? Mathf.RoundToInt(alt.GolemHp / alt.GolemMaxHp * 100f) : 0;
                takeGolem = GameUi.OptionTile(gr, "ui_golem", $"Взять голема\n<color={HpHex(ghp)}>здоровье {ghp}%</color>", takeGolem,
                    $"Голем Алтаря жизни идёт с армией: {(alt != null ? alt.StatsLine : "")}. Не ест, добычу не носит.\n" +
                    "Раны остаются с ним. Погибнет в набеге — погибнет и дома. Пока он в походе, алтарь без охраны.");
            }
            else GameUi.OptionTile(gr, "ui_golem", $"Голем\n{UiKit.Col(UiKit.MutedHex, gp.ToLower())}", false, "Голема растят на Алтаре жизни.", false);
        }
        by += 40f;

        string problem = LaunchProblem();
        // Раненые (отдыхают после прошлого похода) идут сразу, но со своим здоровьем - предупреждаем
        int wounded = 0;
        foreach (var sq in picked) if (sq != null) foreach (var m in SoldiersOf(sq)) if (m.HpFraction < 0.95f) wounded++;
        string warn = wounded > 0 ? UiKit.Col(UiKit.WarnHex, $"Ранены: {wounded} — пойдут с тем здоровьем, что есть") : "";
        int goSide = pickTarget >= 0 && pickTarget < targets.Count ? targets[pickTarget].ApproachSide : 0;
        if (problem == "" && warn == "")
            warn = UiKit.Col(UiKit.AccentHex, RaidMap.SideArrow(goSide)) + " " + UiKit.Col(UiKit.MutedHex, ApproachLine(goSide));
        LocGUI.Label(new Rect(rx, by, rw, 20f), problem == "" ? warn : UiKit.Col(UiKit.BadHex, problem), smallLine);
        by += 22f;
        if (BigButton(new Rect(rx, by, rw, 34f), problem == "" ? $"Выступить на {targets[pickTarget].Name}" : "Выступить", problem == "", UiKit.Kind.Primary,
            problem == "" ? ApproachTip(goSide) + " Дома её не будет до возвращения." : problem)) Launch();
    }

    // Откуда подойдёт армия - целыми фразами (так их проще перевести)
    static string ApproachTip(int side)
    {
        switch (side)
        {
            case 1: return "Армия подойдёт к деревне с востока и войдёт на карту у восточного края. Из нашего города уйдёт через западный край.";
            case 2: return "Армия подойдёт к деревне с юга и войдёт на карту у южного края. Из нашего города уйдёт через северный край.";
            case 3: return "Армия подойдёт к деревне с севера и войдёт на карту у северного края. Из нашего города уйдёт через южный край.";
            default: return "Армия подойдёт к деревне с запада и войдёт на карту у западного края. Из нашего города уйдёт через восточный край.";
        }
    }

    static string ApproachLine(int side)
    {
        switch (side)
        {
            case 1: return "Подойдём с востока, из города выйдем на западе";
            case 2: return "Подойдём с юга, из города выйдем на севере";
            case 3: return "Подойдём с севера, из города выйдем на юге";
            default: return "Подойдём с запада, из города выйдем на востоке";
        }
    }

    static string FormatTime(float s)
    {
        int t = Mathf.Max(0, Mathf.CeilToInt(s));
        return $"{t / 60}:{t % 60:00}";
    }

    // final-13: общий таймер набега на игрока - крупно сверху по центру (у защитника такой же)
    void DrawRaidClock()
    {
        if (current == null || !current.IsPlayer) return;
        if (State != Phase.Scouting && State != Phase.Battle && State != Phase.Leaving) return;
        float left = battleDeadline - Time.time;
        if (left <= 0f && State == Phase.Leaving) return;
        float free = SideW, cx = free + (UiKit.W - free) / 2f;
        string cap = State == Phase.Scouting ? "Осмотр · до конца набега" : State == Phase.Leaving ? "Отступление" : "До конца набега";
        string sub = live ? $"{current.Name} обороняется сам" : "";
        Rect r = GameUi.RaidTimer(cx, UiKit.TopBtnY + 2f, Mathf.Max(0f, left), cap, sub);
        Publish(r);
    }

    // ---------- Панель боя ----------

    // Панель боя - отдельная колонка слева во всю высоту экрана; карта боя занимает место справа от неё
    // (камера рисует только туда, см. UpdateSideView), панель не закрывает поле боя.
    public const float SideW = 310f, SideTop = 45f;
    Vector2 hudScroll;
    Camera sideClear;   // закрашивает экран под панелью (основная камера туда больше не рисует)

    /// <summary>Каждый кадр: смотрим на бой - карта справа от панели, иначе - на весь экран.</summary>
    void UpdateSideView()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        bool side = ViewingBattle && Screen.width > 0;
        float frac = side ? Mathf.Clamp(SideW * UiKit.Scale / Screen.width, 0f, 0.6f) : 0f;
        Rect want = new Rect(frac, 0f, 1f - frac, 1f);
        if (cam.rect != want) cam.rect = want;
        if (side && sideClear == null)
        {
            var go = new GameObject("RaidSideClear");
            go.transform.SetParent(transform, false);
            sideClear = go.AddComponent<Camera>();
            sideClear.cullingMask = 0;
            sideClear.clearFlags = CameraClearFlags.SolidColor;
            sideClear.backgroundColor = new Color(0.1f, 0.11f, 0.12f);
            sideClear.depth = cam.depth - 1f;
            sideClear.orthographic = true;
        }
        if (sideClear != null && sideClear.enabled != side) sideClear.enabled = side;
    }

    void DrawBattleHud()
    {
        float x = 0f, y = SideTop, w = SideW;
        Rect box = new Rect(x, y, w, UiKit.H - y);
        Publish(box);

        var att = AttackersNow();
        int towersRuined = 0;
        Loot inBags = new Loot();
        foreach (var b in RaidBuilding.All) if (b != null && b.IsDefense && b.IsRuined) towersRuined++;
        foreach (var u in att) inBags.Add(u.Carried);
        foreach (var kv in survivors) inBags.Add(kv.Value);

        string phase = State == Phase.Scouting ? UiKit.Col(UiKit.WarnHex, "разведка")
                     : (State == Phase.Battle ? UiKit.Col(UiKit.BadHex, "бой") : UiKit.Col(UiKit.WarnHex, "отступление"));
        Rect c = UiKit.Window(box, $"{current.Name} · {phase}");
        // Не влезает по высоте (маленький экран, раскрыты отряды) - листается
        bool scroll = hudHeight > c.height + 1f;
        float barW = scroll ? UiKit.ScrollBarW + 4f : 0f;
        hudScroll = UiKit.BeginScrollView(c, hudScroll, new Rect(0f, 0f, c.width - barW, Mathf.Max(hudHeight, c.height)));
        float cx = 0f, cw = c.width - barW, cy = 0f;
        GUIStyle line = OneLine(UiKit.Body), small = UiKit.Small;

        // Главные числа
        int eatersN = 0;
        foreach (var u in att) eatersN += UnitEats(u);
        float minutes = eatersN > 0 ? food / (float)eatersN * EatInterval / 60f : 0f;
        string foodText = food > 0 ? $"еда ~{minutes:F0} мин" : UiKit.Col(UiKit.BadHex, "еды нет!");
        Rect nr = new Rect(cx, cy, cw, 20f);
        int soldiersOn = 0;
        RaidUnit golemOn = null;
        foreach (var u in att) { if (u.IsGolem) golemOn = u; else soldiersOn++; }
        string golemText = golemOn != null ? $"  {UiKit.Col(UiKit.GoodHex, "+голем")} <color={HpHex(Mathf.RoundToInt(golemOn.HpFraction * 100f))}>{Mathf.RoundToInt(golemOn.HpFraction * 100f)}%</color>" : "";
        LocGUI.Label(nr, $"Бойцы <b>{soldiersOn}</b>/{startCount}{golemText}   убито <b>{kills}</b>   {foodText}", line);
        UiKit.Tip(nr, $"Бойцов на поле: {soldiersOn} из {startCount}" + (golemOn != null ? $"\nГолем: {Mathf.CeilToInt(golemOn.Hp)}/{Mathf.CeilToInt(golemOn.MaxHp)}" : "") + $"\nПогибло: {lost}, ушли с поля: {survivors.Count}\n" +
                      $"Убито защитников: {kills}, башен разрушено: {towersRuined}\nЕда в запасе: {food}" + (food > 0 ? "" : " — бойцы слабеют"));
        cy += 22f;

        if (current.IsPlayer)
        {
            float left = battleDeadline - Time.time;
            string tl = left > 0f ? $"До конца набега: <b>{FormatTime(left)}</b>" : UiKit.Col(UiKit.BadHex, "Время вышло — отступаем");
            LocGUI.Label(new Rect(cx, cy, cw, 20f), left > 0f && left < 120f ? UiKit.Col(UiKit.WarnHex, tl) : tl, line);
            cy += 22f;
            if (live)
            {
                LocGUI.Label(new Rect(cx, cy, cw, 20f), UiKit.Col(UiKit.BadHex, $"Бой вживую: {current.Name} защищается сам!"), line);
                cy += 22f;
            }
        }

        string loot = $"Добыча: {UiKit.Col(UiKit.GoldHex, inBags.ShortText)}";
        float lh = UiKit.TextH(loot, cw, small);
        LocGUI.Label(new Rect(cx, cy, cw, lh), loot, small);
        cy += lh + 2f;

        // Премия за победу: не в мешках, гибель бойцов её не отнимет (RaidManager_Bounty)
        string bounty = hallSacked
            ? $"Награда за победу: {UiKit.Col(UiKit.GoldHex, bountyPaid.ShortText)} — уже в казне"
            : $"За Цитадель: {UiKit.Col(UiKit.GoldHex, HallBountyFor(arrivedPlan != null ? arrivedPlan.Level : current.Level, defensePoints).ShortText)}" +
              (bountyPaid.IsEmpty ? "" : $"\nУже в казне (башни): {UiKit.Col(UiKit.GoldHex, bountyPaid.ShortText)}");
        float bh = UiKit.TextH(bounty, cw, small);
        Rect bRect = new Rect(cx, cy, cw, bh);
        LocGUI.Label(bRect, bounty, small);
        UiKit.Tip(bRect, "Премия за победу идёт сразу в казну, а не в мешки: даже если все бойцы погибнут, она останется.\n" +
                         "Чем сильнее оборона деревни (башни, пушки, стены, войско), тем больше премия.\n" +
                         "За каждую разрушенную башню или пушку — тоже немного. У хозяина деревни премию не отнимают.");
        cy += bh + 2f;

        RaidDefense def = RaidDefense.Instance;
        if (live && liveStatus != "")
        {
            string en = $"{UiKit.Col(UiKit.MutedHex, "Наше войско:")} {liveStatus}";
            float eh = UiKit.TextH(en, cw, small);
            LocGUI.Label(new Rect(cx, cy, cw, eh), en, small);
            cy += eh + 2f;
        }
        else if (def != null && !string.IsNullOrEmpty(def.Status))
        {
            string en = $"{UiKit.Col(UiKit.MutedHex, "Противник:")} {def.Status}";
            float eh = UiKit.TextH(en, cw, small);
            LocGUI.Label(new Rect(cx, cy, cw, eh), en, small);
            cy += eh + 2f;
        }

        ScoutReport myMap = ReportFor(current);
        if (myMap != null)
        {
            if (UiKit.Button(new Rect(cx, cy + 2f, cw, 24f), mapShown == null ? "Карта разведчика" : "Скрыть карту", UiKit.Kind.Normal,
                $"Набросок плана, точность ~{myMap.ShownAccuracy}%")) mapShown = mapShown == null ? myMap : null;
            cy += 28f;
        }
        cy += 4f;

        if (State == Phase.Scouting)
        {
            string st = live ? UiKit.Col(UiKit.MutedHex, $"Армия стоит у края деревни. {current.Name} видит её и готовится — решайте быстрее.")
                : def != null ? def.ScoutingText : UiKit.Col(UiKit.MutedHex, "Защитники пока вас не заметили. Смотрите карту и решайте.");
            float sh = UiKit.TextH(st, cw, small);
            LocGUI.Label(new Rect(cx, cy, cw, sh), st, small);
            cy += sh + 4f;
            // Осмотр безопасен (final-6): армия стоит, её не трогают, пока вы не начали
            string safe = UiKit.Col(UiKit.GoodHex, "Пока вы не нажали «Атаковать», вашу армию не трогают.");
            float sfh = UiKit.TextH(safe, cw, small);
            LocGUI.Label(new Rect(cx, cy, cw, sfh), safe, small);
            cy += sfh + 2f;
            if (!live && current != null && !current.IsPlayer && (current.Trait == RivalTrait.Warlike || current.Trait == RivalTrait.Calculating))
            {
                float left = ScoutGrace - WaitSeconds;
                string warn = left > 0f
                    ? UiKit.Col(UiKit.WarnHex, string.Format("Сосед вспыльчивый: если простоять у ворот ещё {0} и попасться, он может ударить первым.", Balance.Dur(left)))
                    : UiKit.Col(UiKit.BadHex, "Вы стоите у ворот слишком долго — заметивший вас сосед может ударить первым.");
                float wh = UiKit.TextH(warn, cw, small);
                LocGUI.Label(new Rect(cx, cy, cw, wh), warn, small);
                cy += wh + 2f;
            }
            cy += 2f;
            float bw = (cw - 4f) / 2f;
            if (BigButton(new Rect(cx, cy, bw, 32f), "Атаковать!", true, UiKit.Kind.Primary, "Начать штурм деревни")) StartAttack();
            if (BigButton(new Rect(cx + bw + 4f, cy, bw, 32f), "Уйти домой", true, UiKit.Kind.Normal, "Уйти без боя через западный край")) RetreatAll();
            cy += 36f;
        }
        else
        {
            if (State == Phase.Battle)
            {
                bool won = hallSacked || resistanceBroken;
                if (BigButton(new Rect(cx, cy, cw, 32f), won ? "Уходить с добычей!" : "Отступить всем", true, won ? UiKit.Kind.Primary : UiKit.Kind.Danger,
                    "Все бойцы уходят через западный край карты; погибшие теряют свой мешок")) RetreatAll();
                cy += 36f;

                // Приказ выбранным отрядам (ничего не выбрано - всем)
                float bw = (cw - 4f) / 2f;
                if (UiKit.Button(new Rect(cx, cy, bw, 26f), "Штурм", UiKit.Kind.Normal, "Выбранные (или все) штурмуют сами: бьют защитников, ломают здания"))
                {
                    if (live) Send(BattleCommand.Auto, SelectedAttackers());
                    else foreach (var u in SelectedAttackers()) u.OrderAuto();
                }
                if (UiKit.Button(new Rect(cx + bw + 4f, cy, bw, 26f), "Отступить выбр.", selected.Count > 0, UiKit.Kind.Normal,
                        selected.Count > 0 ? "Отступить только выбранным отрядам" : "Сначала выберите отряд на карточке внизу"))
                    RetreatUnits(OrderTargets());
                cy += 30f;
            }
            else
            {
                LocGUI.Label(new Rect(cx, cy, cw, 20f), UiKit.Col(UiKit.WarnHex, $"Отступают — на поле ещё {att.Count}"), line);
                cy += 22f;
            }

            // Отряды - свёрнуто: выбор, кому отдавать приказы
            string selText = allSquads ? "все" : selected.Count == 0 ? "никто" : selected.Count.ToString();
            if (UiKit.Fold(new Rect(cx, cy, cw, UiKit.FoldH), "raid.squads", $"Отряды (выбрано: {selText})"))
            {
                cy += UiKit.FoldH;
                GUIStyle tg = new GUIStyle(GUI.skin.toggle) { fontSize = 12, wordWrap = false };
                foreach (var sq in squads)
                {
                    int alive = 0;
                    float hpSum = 0f;
                    foreach (var u in att) if (u.Squad == sq) { alive++; hpSum += u.HpFraction; }
                    if (alive == 0) continue;
                    bool on = selected.Contains(sq);
                    int hp = Mathf.RoundToInt(hpSum / alive * 100f);
                    string lbl = $" {sq.Name}: {alive} бойц., <color={HpHex(hp)}>{hp}%</color>";
                    bool now = LocGUI.Toggle(new Rect(cx + 8f, cy, cw - 8f, 20f), on, lbl, tg);
                    if (now != on) { if (now) { selected.Add(sq); allSquads = false; } else selected.Remove(sq); }
                    cy += 21f;
                }
                if (UiKit.Button(new Rect(cx + 8f, cy + 2f, 90f, 22f), "Все", allSquads ? UiKit.Kind.Primary : UiKit.Kind.Normal, "Приказы с карты получат все отряды")) { selected.Clear(); allSquads = !allSquads; }
                cy += 28f;
            }
            else cy += UiKit.FoldH + 2f;

            if (State == Phase.Battle)
            {
                Rect hr = new Rect(cx, cy, cw, 18f);
                LocGUI.Label(hr, UiKit.Col(UiKit.MutedHex, "? Управление мышью"), OneLine(UiKit.Small));
                UiKit.Tip(hr,
                    "Сначала выберите отряд на карточке внизу или нажмите «Все»\n" +
                    "ЛКМ по земле — идти, по дороге бить всех\nДвойной клик — марш, ни на что не отвлекаясь\nЛКМ по зданию — ломать его\n" +
                    "ЛКМ по своему бойцу — выбрать отряд (Shift — добавить)\nНичего не выбрано — нажатие по карте ничего не приказывает\nV — деревня / битва, Esc — сброс выбора, Ctrl+A — все");
                cy += 20f;
            }
        }

        hudHeight = cy + UiKit.Pad * 0.6f + 2f;
        GUI.EndScrollView();
    }

    RaidUnit inspectedUnit;   // выбранный нажатием голем: карточка висит, пока его не снимут

    void DrawTooltip()
    {
        string text = null;
        RaidUnit show = hoverUnit != null && hoverUnit.Alive ? hoverUnit : null;
        bool pinned = false;
        if (inspectedUnit != null && (!inspectedUnit.Alive || !ViewingBattle)) inspectedUnit = null;
        if (show == null && inspectedUnit != null) { show = inspectedUnit; pinned = true; }
        if (show != null)
        {
            string side = show.Attacker ? UiKit.Col(UiKit.InfoHex, "наш") : UiKit.Col(UiKit.BadHex, "защитник");
            string carry = show.Attacker && !show.Carried.IsEmpty ? $"\nнесёт: {UiKit.Col(UiKit.GoldHex, show.Carried.ShortText)} ({show.Carried.Weight}/{show.CarryCap})" : "";
            text = $"<b>{show.Name}</b> ({side})\nздоровье {Mathf.CeilToInt(show.Hp)}/{Mathf.CeilToInt(show.MaxHp)}, урон {show.DamagePreview:F0}" +
                   (show.Attacker ? $"\n{show.StatusText}" : "") + carry + (show.IsGolem ? "\n" + LifeAltar.GolemAbilitiesText() : "");
        }
        else if (hoverBuilding != null) text = hoverBuilding.TooltipText;
        if (text == null) return;
        if (!pinned && Mouse.current == null) return;

        GUIStyle st = Style(12);
        float w = 280f;
        float h = st.CalcHeight(Loc.Content(text), w - 12f) + 10f;
        Vector2 anchor;
        if (pinned)
        {
            // Карточка - у самого голема, над ним стрелка выбора
            Camera cam = Camera.main;
            Vector3 sp = cam != null ? cam.WorldToScreenPoint(show.Position + Vector2.up * 0.9f) : Vector3.zero;
            Vector2 g = UiKit.ToGui(sp);
            GUIStyle mk = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            mk.normal.textColor = Color.black;
            GUI.Label(new Rect(g.x - 14f, g.y - 14f, 30f, 30f), "▼", mk);
            mk.normal.textColor = new Color(0.55f, 1f, 0.6f);
            GUI.Label(new Rect(g.x - 15f, g.y - 15f, 30f, 30f), "▼", mk);
            anchor = new Vector2(g.x + 4f, g.y + 10f);
        }
        else anchor = UiKit.ToGui(Mouse.current.position.ReadValue());
        Rect r = new Rect(Mathf.Clamp(anchor.x + 18f, 4f, UiKit.W - w - 4f), Mathf.Clamp(anchor.y + 18f, 4f, UiKit.H - h - 4f), w, h);
        LocGUI.Box(r, GUIContent.none);
        LocGUI.Label(new Rect(r.x + 6f, r.y + 5f, w - 12f, h - 10f), text, st);
    }

    void DrawBars()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        float ppu = UiKit.PointsPerUnit(cam);

        foreach (var b in RaidBuilding.All)
        {
            if (b == null || b.IsRuined || b.Hp >= b.MaxHp - 0.5f) continue;
            if (!FogOfWar.IsVisible(b.Center)) continue;
            float f = b.Hp / b.MaxHp;
            Bar(cam, b.Center + Vector2.up * (b.SizeCells.y * 0.5f + 0.2f), Mathf.Clamp(ppu * b.SizeCells.x * 0.8f, 16f, 90f), f,
                Color.Lerp(new Color(1f, 0.25f, 0.2f), new Color(0.35f, 0.9f, 0.35f), f));
        }
        foreach (var u in RaidUnit.All)
        {
            if (u == null || !u.Alive) continue;
            if (u.Hp >= u.MaxHp - 0.01f && !u.InCombat) continue;
            if (!u.Attacker && !FogOfWar.IsVisible(u.Position)) continue;
            Color col = u.Attacker ? new Color(0.3f, 0.9f, 0.3f) : new Color(1f, 0.2f, 0.2f);
            Bar(cam, u.Position + Vector2.up * 0.55f, Mathf.Clamp(ppu * 0.7f, 14f, 60f), u.HpFraction, col);
        }
    }

    static void Bar(Camera cam, Vector2 world, float w, float frac, Color fill)
    {
        Vector3 sp = cam.WorldToScreenPoint(world);
        if (sp.z < 0f) return;
        float h = Mathf.Clamp(w * 0.12f, 3f, 7f);
        Rect r = new Rect(UiKit.ToGui(sp).x - w / 2f, UiKit.ToGui(sp).y - h / 2f, w, h);
        if (r.xMax < 0f || r.x > UiKit.W || r.yMax < 0f || r.y > UiKit.H) return;
        Color old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.75f);
        GUI.DrawTexture(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f), Texture2D.whiteTexture);
        GUI.color = fill;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(frac), r.height), Texture2D.whiteTexture);
        GUI.color = old;
    }

    static GUIStyle markerStyle;

    // Жёлтые стрелки над выбранными бойцами
    void DrawMarkers()
    {
        if (selected.Count == 0) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        if (markerStyle == null) markerStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        GUIStyle st = markerStyle;
        foreach (var u in RaidUnit.All)
        {
            if (u == null || !u.Attacker || !u.Alive || u.Squad == null || !selected.Contains(u.Squad)) continue;
            Vector3 sp = cam.WorldToScreenPoint(u.Position + Vector2.up * 0.75f);
            if (sp.z < 0f) continue;
            Rect rc = new Rect(UiKit.ToGui(sp).x - 12f, UiKit.ToGui(sp).y - 12f, 24f, 24f);
            st.normal.textColor = Color.black;
            LocGUI.Label(new Rect(rc.x + 1f, rc.y + 1f, rc.width, rc.height), "▼", st);
            st.normal.textColor = Color.yellow;
            LocGUI.Label(rc, "▼", st);
        }
    }

    List<ItemIcons.Entry> reportLoot = new List<ItemIcons.Entry>();   // добыча набега плитками (final-1)
    Vector2 reportScroll;

    void DrawReport()
    {
        float w = 460f;
        float iw = w - UiKit.Pad * 2f;
        float th = UiKit.TextH(report, iw);
        bool hasLoot = reportLoot != null && reportLoot.Count > 0;
        float lootH = hasLoot ? 22f + ItemIcons.GridHeight(reportLoot.Count, iw) + 6f : 0f;
        float bodyH = th + lootH;
        float maxBody = Mathf.Max(160f, UiKit.H - 160f);
        float shownH = Mathf.Min(bodyH, maxBody);
        float h = UiKit.WindowH(shownH + 8f + 30f);
        Rect r = new Rect((UiKit.W - w) / 2f, (UiKit.H - h) / 2f, w, h);
        if (UiKit.Phone) r = UiKit.CenterWindow(w, h);
        Publish(r);
        Rect c = UiKit.Window(r, "Итоги набега", out bool close);
        if (close) { report = null; return; }
        Rect view = new Rect(c.x, c.y, c.width, shownH);
        bool scroll = bodyH > shownH + 0.5f;
        if (scroll) reportScroll = UiKit.BeginScrollView(view, reportScroll, new Rect(0f, 0f, c.width - 16f, bodyH));
        float ox = scroll ? 0f : c.x, oy = scroll ? 0f : c.y, cw = scroll ? c.width - 16f : c.width;
        LocGUI.Label(new Rect(ox, oy, cw, th), report, UiKit.Body);
        if (hasLoot)
        {
            LocGUI.Label(new Rect(ox, oy + th + 2f, cw, 20f), "<b>Добыча</b>", UiKit.Body);
            ItemIcons.DrawGrid(ox, oy + th + 22f, cw, reportLoot);
        }
        if (scroll) GUI.EndScrollView();
        if (UiKit.Button(new Rect(r.x + w / 2f - 60f, c.y + shownH + 8f, 120f, 30f), "Хорошо", UiKit.Kind.Primary)) report = null;
    }

    // =====================================================================
    // Разведка
    // =====================================================================

    static List<CitizenAgent> FreeScouts()
    {
        var list = new List<CitizenAgent>();
        foreach (var a in CitizenAgent.All)
            if (a != null && !a.IsDead && a.IsScout && !a.IsOnExpedition) list.Add(a);
        return list;
    }

    ScoutMission MissionFor(RaidTarget t)
    {
        foreach (var m in missions) if (m.target == t) return m;
        return null;
    }

    /// <summary>Карта разведчика по деревне (если она ещё верна - деревня не отстроилась заново).</summary>
    public ScoutReport ReportFor(RaidTarget t)
    {
        if (t == null || !reports.TryGetValue(t, out ScoutReport r)) return null;
        if (r.Valid) return r;
        reports.Remove(t);
        return null;
    }

    static string MissionText(ScoutMission m)
    {
        return m.away ? $"вернётся через {Mathf.CeilToInt(m.timer)} с" : "идёт к краю карты";
    }

    void StartScout(RaidTarget t)
    {
        if (t == null || !t.Ready || MissionFor(t) != null) return;
        var scouts = FreeScouts();
        if (scouts.Count == 0) { WarManager.Notify("Нет свободного разведчика (обучается в Казарме ур. 2)", false); return; }

        var rm = ResourceManager.Instance;
        int cost = ScoutCost(t);
        if (rm == null || !rm.Spend(cost, 0, 0, 0)) { WarManager.Notify($"Разведка стоит {cost} золота — не хватает", false); return; }

        // Ближайший к краю карты, через который идут к этой деревне (та же сторона, что и у армии)
        int side = t.ApproachSide;
        CitizenAgent best = scouts[0];
        Vector2 edge = ExitEdgePoint(best.transform.position, side);
        float bestD = Vector2.Distance(best.transform.position, edge);
        foreach (var s in scouts)
        {
            Vector2 e = ExitEdgePoint(s.transform.position, side);
            float d = Vector2.Distance(s.transform.position, e);
            if (d < bestD) { bestD = d; best = s; edge = e; }
        }

        if (!best.BeginExpedition(JitterHome(edge), side))
        {
            rm.Add(cost, 0, 0, 0);
            return;
        }
        missions.Add(new ScoutMission { agent = best, target = t, edge = edge });
        WarManager.Notify($"Разведчик {best.CitizenName} отправился к {t.Name}", true);
    }

    void ScoutTick(float dt)
    {
        for (int i = missions.Count - 1; i >= 0; i--)
        {
            ScoutMission m = missions[i];
            if (m.agent == null || m.agent.IsDead)
            {
                WarManager.Notify($"Разведка {m.target.Name} сорвана: разведчика больше нет", false);
                missions.RemoveAt(i);
                continue;
            }

            if (!m.away)
            {
                m.timer += dt;
                if (m.agent.IsAway)
                {
                    m.away = true;
                    m.timer = m.target.TravelSeconds + ScoutSeconds;   // туда (полдороги), осмотр, обратно (полдороги)
                }
                else if (m.timer > MarchTimeout) m.agent.ForceAway();
                continue;
            }

            m.timer -= dt;
            if (m.timer > 0f) continue;

            m.agent.ReturnFromExpedition(JitterHome(m.edge), null);
            ScoutReport rep = ScoutReport.Make(m.target);
            reports[m.target] = rep;
            missions.RemoveAt(i);
            WarManager.Notify($"Разведчик вернулся с картой {m.target.Name} (точность ~{rep.ShownAccuracy}%) — смотрите в окне «В бой!»", true);
        }
    }

    void DrawMapWindow()
    {
        ScoutReport rep = mapShown;
        if (rep == null || !rep.Valid) { mapShown = null; return; }

        float side = Mathf.Min(UiKit.H - (UiKit.Phone ? 46f + UiKit.WindowH(50f) : 150f), 560f, UiKit.W - 60f);
        side = Mathf.Max(220f, side);
        float w = side + UiKit.Pad * 2f, h = UiKit.WindowH(24f + side + 26f);
        Rect r = new Rect((UiKit.W - w) / 2f, (UiKit.H - h) / 2f, w, h);
        if (UiKit.Phone) r = UiKit.CenterWindow(w, h);
        Publish(r);
        CameraController.BlockZoom();
        Rect c = UiKit.Window(r, $"Карта разведчика: {rep.Target.Name}", out bool close);
        if (close) { mapShown = null; return; }

        GUIStyle line = OneLine(UiKit.Small);
        Rect sr = new Rect(c.x, c.y, side, 20f);
        LocGUI.Label(sr, UiKit.Col(UiKit.AccentHex, $"Точность ~{rep.ShownAccuracy}%") + UiKit.Col(UiKit.MutedHex, " · ") + rep.ShortSummary, line);
        UiKit.Tip(sr, $"Набросок от руки: что-то сдвинуто, пропущено или выдумано. Точность называет сам разведчик (~{rep.ShownAccuracy}%).\n\n{rep.SummaryText}");

        Rect view = new Rect(c.x, c.y + 24f, side, side);
        DrawZoomableMap(rep, view);

        Rect lr = new Rect(c.x, view.yMax + 4f, 115f, 20f);
        LocGUI.Label(lr, UiKit.Col(UiKit.InfoHex, "? Обозначения"), line);
        UiKit.Tip(lr, ScoutReport.Legend);
        LocGUI.Label(new Rect(c.x + 120f, view.yMax + 4f, side - 120f, 20f), "колесо — приблизить, тянуть — двигать",
            new GUIStyle(OneLine(UiKit.MutedStyle)) { alignment = TextAnchor.UpperRight });
    }

    // ---------- Приближение карты разведчика ----------

    float mapZoom = 1f;
    Vector2 mapPan = new Vector2(0.5f, 0.5f);   // центр окна в долях карты (0..1, y - сверху вниз)
    ScoutReport mapZoomFor;

    void FocusMapOnVillage(ScoutReport rep, float zoom)
    {
        float half = rep.MapSize / 2f;
        mapZoom = zoom;
        Vector2 v = rep.View(rep.VillageCenterGuess);   // рисунок повёрнут стороной подхода
        mapPan = new Vector2((v.x + half) / rep.MapSize, (half - v.y) / rep.MapSize);
    }

    void DrawZoomableMap(ScoutReport rep, Rect view)
    {
        // Новая карта - сразу приближаем к деревне (на большой карте иначе ничего не разглядеть)
        if (mapZoomFor != rep)
        {
            mapZoomFor = rep;
            FocusMapOnVillage(rep, Mathf.Clamp(rep.MapSize / 34f, 1f, 5f));
        }

        Event e = Event.current;
        if (view.Contains(e.mousePosition))
        {
            if (e.type == EventType.ScrollWheel)
            {
                // Приближаем к точке под курсором
                float vs0 = view.width * mapZoom;
                Vector2 mouseFrac = mapPan + (e.mousePosition - view.center) / vs0;
                float newZoom = Mathf.Clamp(mapZoom * (e.delta.y > 0f ? 0.85f : 1.18f), 1f, 8f);
                float vs1 = view.width * newZoom;
                mapPan = mouseFrac - (e.mousePosition - view.center) / vs1;
                mapZoom = newZoom;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag)
            {
                mapPan -= e.delta / (view.width * mapZoom);
                e.Use();
            }
        }

        // Карта не уезжает за край окна
        float halfView = 0.5f / mapZoom;
        mapPan.x = Mathf.Clamp(mapPan.x, halfView, 1f - halfView);
        mapPan.y = Mathf.Clamp(mapPan.y, halfView, 1f - halfView);

        float vs = view.width * mapZoom;
        float vx = view.width / 2f - mapPan.x * vs;
        float vy = view.height / 2f - mapPan.y * vs;
        GUI.BeginGroup(view);
        rep.Draw(new Rect(vx, vy, vs, vs));
        GUI.EndGroup();

        // Кнопки масштаба поверх карты
        float bx = view.xMax - 30f, by = view.y + 6f;
        if (LocGUI.Button(new Rect(bx, by, 24f, 24f), "+")) mapZoom = Mathf.Min(8f, mapZoom * 1.4f);
        if (LocGUI.Button(new Rect(bx, by + 28f, 24f, 24f), "−")) mapZoom = Mathf.Max(1f, mapZoom / 1.4f);
        if (LocGUI.Button(new Rect(view.x + 6f, view.yMax - 28f, 90f, 22f), "К деревне")) FocusMapOnVillage(rep, Mathf.Max(mapZoom, 2f));
        if (LocGUI.Button(new Rect(view.x + 100f, view.yMax - 28f, 90f, 22f), "Вся карта")) { mapZoom = 1f; mapPan = new Vector2(0.5f, 0.5f); }

        GUIStyle hint = Style(11);
        hint.normal.textColor = new Color(0.25f, 0.18f, 0.1f);
        LocGUI.Label(new Rect(view.x + 196f, view.yMax - 26f, 60f, 20f), $"×{mapZoom:F1}", hint);
    }
}