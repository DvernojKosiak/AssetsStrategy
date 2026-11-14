using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Живой набег, сторона защитника: другой игрок напал, пока мы в игре, - бой идёт прямо в нашей деревне.
///  - Раз в пару секунд спрашиваем сервер «я дома» (BattleWatch, home): пока мы в своём городе, сервер знает,
///    что армию нападающего можно привести к нам вживую.
///  - Армия пришла: её бойцы выходят с западного края нашей карты - это враги, как войско соседей (RivalAssault в
///    режиме игрока, EnemyAgent.SpawnPlayer), с ранами, уроном, конями и щитами их хозяина. Начинается режим войны:
///    жители прячутся, башни, пушки и ловушки бьют, бойцы и казармы обороняются - мы играем как обычно.
///  - Наша игра считает бой и несколько раз в секунду шлёт снимок (SiegeFrame): где кто стоит, здания, выстрелы,
///    кто из нападающих погиб или ушёл с добычей. В ответ - приказы нападающего (он видит нашу деревню у себя).
///  - Нападающий может ставить взятые с собой заслоны: приходит приказ (SiegePosts), укрепление встаёт у нас как
///    «чужое» (FieldPost.Hostile) и уходит вместе с его армией. Все укрепления на карте идут в снимок.
///  - Добычу враги выносят по-настоящему (как соседи); убили врага - добыча у убийцы. Набег окончен, когда войско
///    ушло целиком; сервер пришлёт отчёт, вычитать по нему ничего не нужно (RaidReport.live).
/// Бой не вживую (его считает игра нападающего на копии деревни; игрок мог быть и в игре, просто не дома) - сверху полоса с именем нападающего.
/// Создаётся вместе с AccountManager.
/// </summary>
public class HomeSiege : MonoBehaviour
{
    static HomeSiege inst;

    const float IdleEvery = 2f, PushEvery = 0.2f;
    const int MaxDefenders = 160, MaxShots = 40;

    string raidId;            // идущий на нас набег (любой)
    string attacker = "";
    bool liveRaid;            // он идёт вживую у нас
    // final-13: общий таймер набега (сервер считает по снимкам нападающего; старый сервер не присылает - таймера нет)
    float leftAt = -1f, leftAtTime;
    int raidPhase = -1;
    float hostedAt = -1f;     // когда армия вошла к нам (живой набег без данных сервера - считаем сами)
    float timer = 1.5f;
    bool inFlight;

    // Бой вживую
    RivalAssault army;
    string hostRaid;          // набег, армию которого мы выставили
    readonly HashSet<string> hosted = new HashSet<string>();   // эти армии уже выставлены (второй раз не выставлять)
    bool finalSent;
    int seq;
    readonly Dictionary<CitizenAgent, int> ids = new Dictionary<CitizenAgent, int>();
    readonly Dictionary<int, CitizenAgent> byId = new Dictionary<int, CitizenAgent>();
    readonly HashSet<CitizenAgent> mourned = new HashSet<CitizenAgent>();
    readonly List<string> killed = new List<string>();
    readonly List<SiegeShot> shots = new List<SiegeShot>();
    readonly HashSet<int> postOrders = new HashSet<int>();   // приказы «поставить» уже выполнены (сервер может прислать повтор)
    int nextId = 1;
    int spoilBudget;          // трофеи (обновление 23): сколько очков ещё могут взять у нас за этот набег
    int spoilsSent = -1;      // столько унесённых трофеев нападающий уже точно получил (снимок дошёл до сервера)

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { inst = null; }

    void Awake() { inst = this; }
    void OnDestroy()
    {
        if (inst == this) inst = null;
        if (CombatFx.Record == RecordShot) CombatFx.Record = null;
    }

    /// <summary>На деревню сейчас идёт набег другого игрока.</summary>
    public static bool UnderRaid => inst != null && inst.raidId != null;
    /// <summary>Набег идёт вживую - армия игрока у нас на карте.</summary>
    public static bool Hosting => inst != null && inst.army != null;

    // Под баннером войны, если он сейчас виден (он мог опуститься под верхнюю полосу на узком экране)
    static Rect BannerRect => new Rect(UiKit.W / 2f - Mathf.Min(220f, UiKit.W / 2f - 8f), Mathf.Max(74f, WarManager.BannerBottom), Mathf.Min(440f, UiKit.W - 16f), 58f);

    /// <summary>Курсор над полосой «на вас напали» (клики не уходят на карту города).</summary>
    public static bool IsPointerOver(Vector2 p)
    {
        return inst != null && inst.ShowBanner && BannerRect.Contains(p);
    }

    bool ShowBanner => raidId != null && army == null && !liveRaid;

    /// <summary>final-13: сколько ещё может идти набег на нас (секунд); -1 - неизвестно.</summary>
    public static float SecondsLeft
    {
        get
        {
            if (inst == null || inst.raidId == null) return -1f;
            if (inst.leftAt > 0f) return Mathf.Max(0f, inst.leftAt - (Time.unscaledTime - inst.leftAtTime));
            if (inst.army != null && inst.hostedAt >= 0f) return Mathf.Max(0f, Api.RaidBattleSeconds - (Time.unscaledTime - inst.hostedAt));
            return -1f;
        }
    }

    /// <summary>Таймер для плашки войны, когда армия нападающего у нас (живой набег). 0 - не показывать.</summary>
    public static float LiveSecondsLeft => inst != null && inst.army != null ? Mathf.Max(0f, SecondsLeft) : 0f;

    /// <summary>Строка под баннером войны: кто нападает и что делает его войско.</summary>
    public static string ThreatLine()
    {
        if (inst == null || inst.army == null) return "";
        return $"Игрок {inst.attacker}: {inst.army.Status} · бойцов {inst.army.Units.Count}";
    }

    // Наша деревня загружена и в ней можно принять бой
    static bool CanHost =>
        AccountManager.LoggedIn && CityBoot.Started && !WarCityMode.Active && GridNavigation.Instance != null &&
        BuildingPlacer.Instance != null && BuildingPlacer.Instance.WorldReady;

    // ---------- Связь с сервером ----------

    void Update()
    {
        if (!AccountManager.LoggedIn || string.IsNullOrEmpty(AccountManager.Token) || AccountManager.Blocking) return;
        timer -= Time.unscaledDeltaTime;
        if (timer > 0f || inFlight) return;
        if (army != null || (hostRaid != null && !finalSent)) { timer = PushEvery; Push(); }
        else { timer = IdleEvery; Watch(); }
    }

    void Watch()
    {
        inFlight = true;
        var req = new ApiRequest { raidId = raidId, home = CanHost };
        AccountManager.Backend.Call(Api.BattleWatch, AccountManager.Token, req, res =>
        {
            inFlight = false;
            if (this == null || !res.ok) return;
            if (string.IsNullOrEmpty(res.raidId))
            {
                if (raidId != null && !liveRaid) WarManager.Notify($"Набег игрока {attacker} окончен. Потери деревни придут с отчётом", true);
                raidId = null;
                liveRaid = false;
                leftAt = -1f;
                raidPhase = -1;
                return;
            }
            if (raidId != res.raidId)
            {
                raidId = res.raidId;
                leftAt = -1f;
                raidPhase = -1;
                attacker = string.IsNullOrEmpty(res.incomingName) ? "?" : res.incomingName;
                if (!res.siegeLive) WarManager.Notify($"Игрок {attacker} грабит вашу деревню!", false);
            }
            liveRaid = res.siegeLive;
            if (res.raidLeft > 0.5f) { leftAt = res.raidLeft; leftAtTime = Time.unscaledTime; raidPhase = res.raidPhase; }
            else if (!liveRaid) { leftAt = -1f; raidPhase = res.raidPhase; }
            if (res.siegeLive && res.siegeArmy != null && res.siegeArmy.Count > 0 && army == null && !hosted.Contains(res.raidId) && CanHost)
                Host(res.raidId, res.siegeArmy, res.camp, res.siegeSide);
        });
    }

    // Армия нападающего входит в нашу деревню
    void Host(string id, List<SiegeUnit> list, bool camp, int side)
    {
        var t = new RaidTarget { Name = attacker, Level = 1, Trait = RivalTrait.Calculating, IsPlayer = true, Activity = "нападает на вас" };
        side = Mathf.Clamp(side, 0, 3);
        army = RivalAssault.LaunchPlayer(t, list, camp, side);
        if (army == null) return;
        hosted.Add(id);
        hostRaid = id;
        hostedAt = Time.unscaledTime;
        finalSent = false;
        seq = 0;
        ids.Clear();
        byId.Clear();
        mourned.Clear();
        killed.Clear();
        shots.Clear();
        postOrders.Clear();
        nextId = 1;
        // Сила нападающего нам не известна - потолок трофеев по нашей Цитадели (SpoilsRules.Budget)
        spoilBudget = SpoilsRules.Budget(StorageLimits.HallLevel);
        spoilsSent = -1;
        lastSpoils = null;
        lastSpoilsUnits = 0;
        escapedItems.Clear();
        army.OnEnd = a => { if (a == army) army = null; };   // ушли все - последний снимок с отметкой «окончен»
        CombatFx.Record = RecordShot;
        WarManager.Notify($"Игрок {attacker} напал на вашу деревню! Его армия ({army.Units.Count}) входит {RaidMap.SideName(side)} — защищайтесь!", false);
        WarManager.RaiseAlarm();
        timer = 0f;
    }

    void Push()
    {
        string token = AccountManager.Token;
        if (string.IsNullOrEmpty(token) || hostRaid == null) return;
        bool over = army == null;
        SiegeFrame f = Frame(over);
        inFlight = true;
        string id = hostRaid;
        int spoilsInFrame = lastSpoilsUnits;   // Frame только что обновил
        AccountManager.Backend.Call(Api.SiegePush, token, new ApiRequest { raidId = id, siegeFrame = f }, res =>
        {
            inFlight = false;
            if (this == null || !res.ok) return;
            spoilsSent = Mathf.Max(spoilsSent, spoilsInFrame);
            if (over)
            {
                Finish(killed.Count == 0 ? $"Набег игрока {attacker} окончен — его войско ушло" : $"Войско игрока {attacker} ушло. Погибло наших бойцов: {killed.Count}");
                return;
            }
            if (res.commands != null && army != null)
                foreach (var c in res.commands)
                {
                    if (c != null && c.kind == SiegePosts.Order) PlacePost(c);
                    else if (c != null && c.kind == SiegePosts.TrapOrder) PlaceTrap(c);
                    else army.PlayerCommand(c, UnitById, CitizenById);
                }
            if (res.siegeOver && army != null)
            {
                // Нападающий закончил набег (или закрыл игру): оставшиеся уходят сами, снимки больше не нужны
                RivalAssault a = army;
                army = null;
                a.OnEnd = null;
                a.Abandon();
                Finish($"Игрок {attacker} закончил набег — его войско уходит");
            }
        });
    }

    void Finish(string text)
    {
        FieldPost.ClearHostile();   // укрепления и ловушки нападающего уходят вместе с его армией
        Trap.ClearHostile();
        finalSent = true;
        hostRaid = null;
        if (CombatFx.Record == RecordShot) CombatFx.Record = null;
        WarManager.Notify(text, killed.Count == 0);
        AccountManager.SaveSoon();   // город с потерями - на сервер
    }

    // Нападающий ставит укрепление. Клетки проверяем у себя: место могли занять, пока шёл приказ
    void PlacePost(BattleCommand c)
    {
        if (c.units == null || c.units.Count < 4 || c.target < 0 || c.target >= FieldPost.Specs.Length) return;
        int id = c.units[3];
        if (!postOrders.Add(id)) return;
        PostKind kind = (PostKind)c.target;
        FieldPost.Spec s = FieldPost.Specs[c.target];
        Vector2 at = new Vector2(c.x, c.y);
        float angle = c.units[0];
        GridNavigation nav = GridNavigation.Instance;
        if (nav == null) return;
        var cells = FieldPost.CellsFor(s, at, angle, false);
        var set = new HashSet<Vector2Int>(cells);
        foreach (var cell in cells)
            if (!nav.InBounds(cell) || nav.IsBlocked(cell)) return;
        foreach (var o in FieldPost.All)
        {
            if (o == null || !o.Alive || o.Raid || o.Cells == null) continue;
            foreach (var oc in o.Cells) if (set.Contains(oc)) return;
        }
        foreach (var a in CitizenAgent.All)
            if (a != null && !a.IsDead && !a.IsInsideBuilding && set.Contains(nav.WorldToCell(a.transform.position))) return;
        foreach (var e in EnemyAgent.All)
            if (EnemyAgent.Alive(e) && set.Contains(nav.WorldToCell(e.Position))) return;

        float strength = c.units[1] > 0 ? c.units[1] / 1000f : 1f;
        FieldPost.CreateHostile(kind, at, angle, strength, c.units[2], id);
        WarManager.Notify($"Игрок {attacker} ставит {s.name.ToLower()} — пока строится, бойцы могут растоптать стройку", false);
    }

    // Нападающий ставит боевую ловушку (её не видно; наступят наши бойцы)
    void PlaceTrap(BattleCommand c)
    {
        TrapKit kit = SiegePosts.KitOf(c);
        if (kit == null) return;
        int id = c.units[4];
        if (!postOrders.Add(-id)) return;   // номера ловушек - со знаком минус, чтобы не путать с укреплениями
        GridNavigation nav = GridNavigation.Instance;
        Vector2 at = new Vector2(c.x, c.y);
        if (nav == null || !nav.InBounds(nav.WorldToCell(at)) || nav.IsBlocked(nav.WorldToCell(at))) return;
        Trap.CreateHostile(kit, at, id);
    }

    EnemyAgent UnitById(int id)
    {
        if (army == null) return null;
        foreach (var e in army.Units) if (EnemyAgent.Alive(e) && e.PlayerId == id) return e;
        return null;
    }

    CitizenAgent CitizenById(int id)
    {
        return byId.TryGetValue(id, out CitizenAgent c) && CitizenAgent.IsTargetable(c) ? c : null;
    }

    int IdOf(CitizenAgent c)
    {
        if (ids.TryGetValue(c, out int id)) return id;
        id = nextId++;
        ids[c] = id;
        byId[id] = c;
        return id;
    }

    // ---------- Снимок деревни ----------

    void RecordShot(int kind, Vector2 a, Vector2 b)
    {
        if (army == null || shots.Count >= MaxShots) return;
        shots.Add(new SiegeShot { kind = kind, x0 = a.x, y0 = a.y, x1 = b.x, y1 = b.y });
    }

    string lastSpoils;
    int lastSpoilsUnits;
    readonly SpoilBundle escapedItems = new SpoilBundle();   // отнятое, которое унесли ушедшие бойцы (на последний снимок)

    // ---------- Трофеи (обновление 23) ----------

    /// <summary>Боец нападающего разрушил наше здание: жребий на копии и кражу сложных вещей (Spoils) - в его добычу.</summary>
    public static void RollSpoils(BuildingInstance b, Loot loot)
    {
        if (inst == null || inst.army == null || b == null || loot == null || inst.spoilBudget <= 0) return;
        var stolen = new SpoilBundle();
        var copies = new SpoilBundle();
        Spoils.Roll(Spoils.StashOf(b), ref inst.spoilBudget, stolen, copies);
        if (stolen.IsEmpty && copies.IsEmpty) return;
        loot.items.Add(stolen);
        loot.copies.Add(copies);
        string bname = b.type != null ? b.type.name : "здание";
        if (stolen.IsEmpty) return;   // такие же вещи, как у вас, у вас ничего не убавляют - не пугаем
        WarManager.Notify($"Игрок {inst.attacker} в «{bname}»: отнимают {stolen.Text}. Отбейте бойцов - отнятое вернётся", false);
    }

    /// <summary>
    /// Сохранение посреди живого боя: отнятое, которое враги ещё несут (или унесли, а нападающий об этом ещё не узнал),
    /// пока числится нашим - если игра закроется, оно вернётся к нам (сундук трофеев), а не пропадёт.
    /// </summary>
    public static SpoilBundle HeldSpoils()
    {
        var b = new SpoilBundle();
        // Отбитые у врагов вещи, которые наши бойцы ещё несут домой
        foreach (var c in CitizenAgent.All) if (c != null && !c.IsDead) b.Add(c.CarriedLoot.items);
        if (inst == null || inst.hostRaid == null) return b;
        if (inst.army != null)
            foreach (var e in inst.army.Units) if (EnemyAgent.Alive(e)) b.Add(e.Carried.items);
        if (inst.lastSpoilsUnits > inst.spoilsSent) b.Add(inst.escapedItems);
        return b;
    }

    SiegeFrame Frame(bool over)
    {
        var f = new SiegeFrame { seq = ++seq, t = Time.realtimeSinceStartup, over = over };
        if (army != null)
        {
            f.status = army.Status;
            f.engaged = army.Engaged;
            foreach (var e in army.Units)
            {
                if (!EnemyAgent.Alive(e)) continue;
                f.attackers.Add(new SiegeMark
                {
                    id = e.PlayerId, unit = e.Unit.unitName, x = e.Position.x, y = e.Position.y, hp = e.HpFraction,
                    ranged = e.Unit.attackRange > 2f, mounted = e.IsMounted, fighting = Time.time - e.LastHitAt < 2f, ram = e.IsRam,
                    carried = RaidLoot.From(e.Carried),
                });
            }
            f.gone.AddRange(army.Gone);
            lastSpoils = SiegePosts.EncodeSpoils(army.EscapedItems, army.EscapedCopies);
            lastSpoilsUnits = army.EscapedItems.Units + army.EscapedCopies.Units;
            escapedItems.Clear();
            escapedItems.Add(army.EscapedItems);
        }
        else f.status = "войско ушло";
        // Трофеи, которые унесли ушедшие бойцы (весь список с начала боя; последний снимок - тоже)
        if (lastSpoils != null) f.defenders.Add(new SiegeMark { id = -1, unit = lastSpoils });

        // Наши бойцы и жители на улице (кто в здании или в походе - не виден)
        int n = 0;
        foreach (var c in CitizenAgent.All)
        {
            if (c == null || c.IsDead) continue;
            if (!CitizenAgent.IsTargetable(c) || (c.IsConcealed && (!c.IsSoldier || c.LookoutPosted))) continue;   // дозорного нападающий не видит
            if (n >= MaxDefenders && !c.IsSoldier) continue;
            Vector2 p = c.transform.position;
            f.defenders.Add(new SiegeMark
            {
                id = IdOf(c), unit = c.IsSoldier ? c.UnitName : "", x = p.x, y = p.y,
                hp = c.HomeRamOn ? 1f - c.RamWear : c.HpFraction,   // таран: его прочность
                ranged = c.IsSoldier && c.ExpRanged, mounted = c.HasHorse, citizen = !c.IsSoldier, fighting = c.InCombat,
                ram = c.HomeRamOn,
            });
            n++;
        }
        // Полевые укрепления на карте: наши и нападающего (у него это только картинка)
        foreach (var p in FieldPost.All)
        {
            if (p == null || !p.Alive || p.Raid) continue;
            Vector2 pp = p.Position;
            f.defenders.Add(new SiegeMark
            {
                id = p.NetId, unit = SiegePosts.Encode(p), x = pp.x, y = pp.y,
                hp = p.MaxHp > 0f ? Mathf.Clamp01(p.Hp / p.MaxHp) : 0f,
            });
        }

        // Ловушки нападающего (свои не показываем: враги ловушек не видят)
        foreach (var t in Trap.All)
        {
            if (t == null || !t.Alive || !t.Hostile || t.Kit == null) continue;
            Vector2 tp = t.Position;
            f.defenders.Add(new SiegeMark { id = t.NetId, unit = SiegePosts.EncodeTrap(t), x = tp.x, y = tp.y, hp = t.MaxHp > 0f ? Mathf.Clamp01(t.Hp / t.MaxHp) : 1f });
        }

        // Погибшие в бою бойцы (их уже может не быть на сцене)
        foreach (var kv in ids)
        {
            CitizenAgent c = kv.Key;
            if ((object)c == null || (c != null && !c.IsDead) || !c.IsSoldier || mourned.Contains(c)) continue;
            mourned.Add(c);
            killed.Add(c.UnitName);
        }
        f.killed.AddRange(killed);

        // Повреждённые и разрушенные здания (целые нападающий считает целыми)
        foreach (var b in BuildingInstance.All)
        {
            if (b == null || !b.IsDamageable || !b.isActiveAndEnabled) continue;
            bool ruined = b.IsRuined;
            if (!ruined && b.Hp >= b.MaxHp - 0.5f) continue;
            f.houses.Add(new SiegeHouse { x = b.OriginCell.x, y = b.OriginCell.y, hp = b.MaxHp > 0f ? Mathf.Clamp01(b.Hp / b.MaxHp) : 0f, ruined = ruined });
        }

        f.shots.AddRange(shots);
        shots.Clear();
        return f;
    }

    // ---------- Полоса «на вас напали» ----------

    void OnGUI()
    {
        if (WarView.IsOpen || AccountManager.Blocking || !ShowBanner || RaidManager.ViewingBattle) return;   // на экране боя - не мешаем (обновление 14)
        UiKit.Apply();
        GUI.depth = -60;
        // final-13: раньше - красный прямоугольник с одной строкой «Игрок … грабит вашу деревню!».
        // Теперь карточка как в игре: мечи, пульсирующая рамка, кто нападает, что делает его армия и общий таймер набега
        Rect b = BannerRect;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f);
        GameUi.Card(b, new Color(1f, 0.3f, 0.24f), 1f, 0.25f + 0.35f * pulse);
        GameUi.Icon(new Rect(b.x + 12f, b.y + (b.height - 36f) * 0.5f, 36f, 36f), "ui_swords");
        float left = SecondsLeft;
        float tx = b.x + 58f, tw = b.width - 66f - (left > 0f ? 86f : 0f);
        var head = new GUIStyle(UiKit.Body) { fontSize = 15, fontStyle = FontStyle.Bold, wordWrap = false, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
        head.normal.textColor = new Color(1f, 0.66f, 0.55f);
        LocGUI.Label(new Rect(tx, b.y + 6f, tw, 22f), "Набег на вашу деревню!", head);
        var sub = new GUIStyle(UiKit.Small) { fontSize = 12, wordWrap = false, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
        sub.normal.textColor = new Color(0.9f, 0.86f, 0.8f);
        string what = raidPhase == 0 ? "армия осматривает деревню" : raidPhase == 1 ? "идёт бой" : raidPhase == 2 ? "армия уходит с добычей" : "армия в пути";
        LocGUI.Label(new Rect(tx, b.y + 30f, tw, 20f), $"{UiKit.B(attacker)} · {Loc.T(what)}", sub);
        if (left > 0f)
        {
            Rect pr = new Rect(b.xMax - 90f, b.y + (b.height - 34f) * 0.5f, 80f, 34f);
            GameUi.Pill(pr, left < 120f ? UiKit.Bad : UiKit.Accent);
            GameUi.Icon(new Rect(pr.x + 4f, pr.y + 5f, 24f, 24f), "ui_hourglass");
            var ts = new GUIStyle(UiKit.Body) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, wordWrap = false };
            ts.normal.textColor = left < 120f ? new Color(1f, 0.6f, 0.5f) : new Color(1f, 0.88f, 0.55f);
            GUI.Label(new Rect(pr.x + 26f, pr.y, pr.width - 34f, pr.height), GameUi.Clock(left), ts);
        }
        UiKit.Tip(b, Loc.T("Игрок напал на копию вашей деревни. Таймер — сколько ещё может идти набег. Потери придут с отчётом, повтор боя — в отчёте о набеге."));
    }
}

/// <summary>
/// Полевые укрепления в живом бою. Сервер не знает об укреплениях, поэтому они едут в уже существующих полях:
///  - приказ нападающего «поставить» - BattleCommand с kind = Order: x, y - точка (клетки деревни защитника),
///    target - вид (PostKind), units - [угол в градусах, сила × 1000, лечебный запас (-1 - полный), номер укрепления];
///  - в снимке защитника (SiegeFrame.defenders) укрепление - SiegeMark с unit = Encode(...), id - номер укрепления,
///    x, y - центр, hp - доля прочности. Нападающий отличает их по началу строки (Is) и бойцами не считает.
/// Боевые ловушки нападающего - так же: приказ kind = TrapOrder (target - основа ловушки, units - [добавки, марка,
/// сила × 1000, срабатываний, номер]), в снимке - SiegeMark с unit = EncodeTrap(...). Ловушки защитника
/// нападающему не показываются (враги ловушек не видят).
/// Так обновлять программу сервера не нужно.
/// </summary>
public static class SiegePosts
{
    public const string Order = "post", TrapOrder = "trap";
    const string Prefix = "#post|", TrapPrefix = "#trap|";
    static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

    public static bool Is(SiegeMark m) { return m != null && m.unit != null && m.unit.StartsWith(Prefix); }
    public static bool IsTrap(SiegeMark m) { return m != null && m.unit != null && m.unit.StartsWith(TrapPrefix); }
    /// <summary>Не боец: укрепление или ловушка.</summary>
    public static bool IsThing(SiegeMark m) { return Is(m) || IsTrap(m) || IsSpoils(m); }

    // Трофеи (обновление 23): что унесли ушедшие бойцы нападающего - одной записью «#spoils|отнятое/копии» (SpoilBundle)
    const string SpoilsPrefix = "#spoils|";
    public static bool IsSpoils(SiegeMark m) { return m != null && m.unit != null && m.unit.StartsWith(SpoilsPrefix); }
    public static string EncodeSpoils(SpoilBundle stolen, SpoilBundle copies)
    {
        return stolen.IsEmpty && copies.IsEmpty ? null : SpoilsPrefix + stolen.Encode() + "/" + copies.Encode();
    }
    public static void DecodeSpoils(SiegeMark m, SpoilBundle stolen, SpoilBundle copies)
    {
        stolen.Clear();
        copies.Clear();
        if (!IsSpoils(m)) return;
        string[] a = m.unit.Substring(SpoilsPrefix.Length).Split('/');
        stolen.Add(SpoilBundle.Decode(a[0]));
        if (a.Length > 1) copies.Add(SpoilBundle.Decode(a[1]));
    }

    public static string EncodeTrap(Trap t)
    {
        TrapKit k = t.Kit;
        return TrapPrefix + (int)k.type + "|" + (int)k.mods + "|" + k.mk + "|" + k.power.ToString("0.###", Inv) + "|" +
               t.Charges + "|" + t.Progress.ToString("0.###", Inv) + "|" + (t.Rearming ? "1" : "0");
    }

    public static bool DecodeTrap(string s, out TrapKit kit, out float progress, out bool rearming)
    {
        kit = null; progress = 0f; rearming = false;
        if (s == null || !s.StartsWith(TrapPrefix)) return false;
        string[] f = s.Substring(TrapPrefix.Length).Split('|');
        if (f.Length < 7 || !int.TryParse(f[0], out int type) || !int.TryParse(f[1], out int mods) || !int.TryParse(f[2], out int mk)) return false;
        var st = System.Globalization.NumberStyles.Float;
        if (!float.TryParse(f[3], st, Inv, out float power) || power <= 0f) power = 1f;
        int.TryParse(f[4], out int charges);
        float.TryParse(f[5], st, Inv, out progress);
        rearming = f[6] == "1";
        kit = new TrapKit { type = (TrapBase)type, mods = (TrapMod)mods, mk = Mathf.Clamp(mk, 1, 3), combat = true, power = power, charges = charges };
        return true;
    }

    public static BattleCommand TrapCommand(TrapKit k, Vector2 net, int id)
    {
        var c = new BattleCommand { kind = TrapOrder, x = net.x, y = net.y, target = (int)k.type };
        c.units.Add((int)k.mods);
        c.units.Add(k.mk);
        c.units.Add(Mathf.RoundToInt(k.power * 1000f));
        c.units.Add(k.ChargesLeft);
        c.units.Add(id);
        return c;
    }

    public static TrapKit KitOf(BattleCommand c)
    {
        if (c == null || c.units == null || c.units.Count < 5) return null;
        return new TrapKit { type = (TrapBase)c.target, mods = (TrapMod)c.units[0], mk = Mathf.Clamp(c.units[1], 1, 3), combat = true,
                             power = c.units[2] > 0 ? c.units[2] / 1000f : 1f, charges = c.units[3] };
    }

    /// <summary>mine - укрепление нападающего (для него - своё).</summary>
    public static string Encode(FieldPost p)
    {
        return Prefix + (int)p.Kind + "|" + p.Angle.ToString("0.#", Inv) + "|" + p.Progress.ToString("0.###", Inv) + "|" +
               p.Strength.ToString("0.###", Inv) + "|" + (p.Hostile ? "1" : "0");
    }

    public static bool Decode(string s, out PostKind kind, out float angle, out float progress, out float strength, out bool mine)
    {
        kind = PostKind.Barricade; angle = 0f; progress = 0f; strength = 1f; mine = false;
        if (s == null || !s.StartsWith(Prefix)) return false;
        string[] f = s.Substring(Prefix.Length).Split('|');
        if (f.Length < 5 || !int.TryParse(f[0], out int k) || k < 0 || k >= FieldPost.Specs.Length) return false;
        kind = (PostKind)k;
        var st = System.Globalization.NumberStyles.Float;
        float.TryParse(f[1], st, Inv, out angle);
        float.TryParse(f[2], st, Inv, out progress);
        if (!float.TryParse(f[3], st, Inv, out strength) || strength <= 0f) strength = 1f;
        mine = f[4] == "1";
        return true;
    }

    public static BattleCommand Command(PostKind kind, Vector2 net, float angle, float strength, float pool, int id)
    {
        var c = new BattleCommand { kind = Order, x = net.x, y = net.y, target = (int)kind };
        c.units.Add(Mathf.RoundToInt(angle));
        c.units.Add(Mathf.RoundToInt(strength * 1000f));
        c.units.Add(pool < 0f ? -1 : Mathf.RoundToInt(pool));
        c.units.Add(id);
        return c;
    }
}
