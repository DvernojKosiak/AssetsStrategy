using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Таран (продолжение класса CitizenAgent). Каждый носильщик - обычный житель-боец вида «Таран» (UnitType.isRam);
/// двое носильщиков одного отряда несут один таран (RamCrews). Состояние тарана хранится у каждого носильщика:
///  - ramBroken: таран сломан - носильщик дерётся ножом (урон вида войск) или мечом из Оружейной;
///  - ramWear: сколько прочности тарана уже выбито (0 - целый), чтобы раны тарана пережили сохранение.
/// В набеге таран - один RaidUnit (ведущий носильщик, остальные «едут» в нём), см. RaidUnit (раздел «Таран»).
/// Броня носильщиков прибавляется к броне тарана (RamArmor) и изнашивается от ударов по тарану.
/// Сломанный таран собирают заново в казарме из досок (BarracksController.RebuildRams).
/// </summary>
public partial class CitizenAgent
{
    bool ramBroken;
    float ramWear;

    /// <summary>Боец вида «Таран» (носильщик).</summary>
    public bool IsRamCarrier => unit != null && unit.IsRam;
    /// <summary>Носильщик, у которого таран цел.</summary>
    public bool HasRam => IsRamCarrier && !ramBroken;
    /// <summary>Таран сломан: носильщик дерётся сам, пока в казарме не соберут новый.</summary>
    public bool RamBroken => IsRamCarrier && ramBroken;
    /// <summary>Доля выбитой прочности тарана (0 - целый, 1 - сломан).</summary>
    public float RamWear => IsRamCarrier ? (ramBroken ? 1f : Mathf.Clamp01(ramWear)) : 0f;

    public void RepairRam()
    {
        ramBroken = false;
        ramWear = 0f;
    }

    /// <summary>Таран разбит (в набеге или в живом бою).</summary>
    public void BreakRam()
    {
        if (!IsRamCarrier) return;
        ramBroken = true;
        ramWear = 0f;
    }

    /// <summary>Таран цел, но побит: frac - сколько прочности осталось.</summary>
    public void SetRamCondition(float frac)
    {
        if (!IsRamCarrier || ramBroken) return;
        ramWear = Mathf.Clamp01(1f - frac);
    }

    /// <summary>Из сохранения / с рынка.</summary>
    public void RestoreRam(bool broken, float wear)
    {
        ramBroken = broken;
        ramWear = Mathf.Clamp01(wear);
    }

    /// <summary>Напарник по тарану: другой носильщик из того же отряда (null - один).</summary>
    public CitizenAgent RamMate()
    {
        if (!IsRamCarrier || Squad == null) return null;
        foreach (var crew in RamCrews(Squad.Members, false))
            if (crew.Contains(this)) return crew.Count > 1 ? (crew[0] == this ? crew[1] : crew[0]) : null;
        return null;
    }

    /// <summary>
    /// Расчёты таранов из этих бойцов: носильщики одного отряда парами (crew из UnitType), по порядку в списке.
    /// intactOnly - только с целым тараном (в набег); иначе - все носильщики (кто чей напарник в казарме).
    /// Лишний носильщик несёт таран один (медленнее).
    /// </summary>
    public static List<List<CitizenAgent>> RamCrews(IEnumerable<CitizenAgent> members, bool intactOnly = true)
    {
        var result = new List<List<CitizenAgent>>();
        var open = new Dictionary<Squad, List<CitizenAgent>>();
        List<CitizenAgent> noSquad = null;
        foreach (var m in members)
        {
            if (m == null || m.IsDead || !m.IsRamCarrier) continue;
            if (intactOnly && m.RamBroken) continue;
            List<CitizenAgent> cur;
            if (m.Squad != null) open.TryGetValue(m.Squad, out cur);
            else cur = noSquad;
            if (cur == null || cur.Count >= m.unit.Crew)
            {
                cur = new List<CitizenAgent>();
                result.Add(cur);
                if (m.Squad != null) open[m.Squad] = cur;
                else noSquad = cur;
            }
            cur.Add(m);
        }
        return result;
    }

    /// <summary>Защита тарана: своя + половина брони каждого носильщика, не больше RamArmorCap.</summary>
    public const float RamArmorCap = 0.6f;
    public static float RamArmor(UnitType u, IList<CitizenAgent> crew)
    {
        float a = u != null ? u.ramBaseArmor : 0.1f;
        if (crew != null)
            foreach (var c in crew)
                if (c != null && !c.IsDead && c.HasArmor) a += c.bodyGear.Reduce * 0.5f;
        return Mathf.Clamp(a, 0f, RamArmorCap);
    }

    /// <summary>Удар по тарану: изнашивается броня одного из носильщиков (по очереди).</summary>
    public static void RamWearArmor(IList<CitizenAgent> crew, ref int turn)
    {
        if (crew == null || crew.Count == 0) return;
        for (int k = 0; k < crew.Count; k++)
        {
            CitizenAgent c = crew[(turn + k) % crew.Count];
            if (c == null || c.IsDead || !c.HasArmor) continue;
            turn = (turn + k + 1) % crew.Count;
            BodyGear g = c.bodyGear;
            if (g.Wear())
            {
                c.UnequipBody();
                WarManager.Notify($"Пробита: {g.Name} ({c.unit.unitName} {c.citizenName})", false);
            }
            return;
        }
    }

    /// <summary>Скорость тарана: вдвоём - ramSpeed, один носильщик - медленнее. Похлёбка ускоряет, броня - нет (несут таран).</summary>
    public static float RamSpeed(UnitType u, IList<CitizenAgent> crew)
    {
        if (u == null) return 1.5f;
        int n = 0;
        bool allHearty = true;
        if (crew != null) foreach (var c in crew) if (c != null && !c.IsDead) { n++; if (!c.hearty) allHearty = false; }
        float s = u.ramSpeed * (n >= u.Crew ? 1f : BarracksController.RamSoloSpeedMul);
        return s * (allHearty && n > 0 ? u.pottageSpeedMul : 1f);
    }

    /// <summary>Удар тарана (без множителей цели). Похлёбка у всех носильщиков - сильнее.</summary>
    public static float RamHit(UnitType u, IList<CitizenAgent> crew)
    {
        if (u == null) return 0f;
        int n = 0;
        bool allHearty = true;
        if (crew != null) foreach (var c in crew) if (c != null && !c.IsDead) { n++; if (!c.hearty) allHearty = false; }
        float d = u.ramDamage * (n >= u.Crew ? 1f : 0.75f);   // один носильщик раскачивает слабее
        return d * (allHearty && n > 0 ? u.pottageDamageMul : 1f);
    }

    /// <summary>Живой бой: ведущий носильщик несёт таран всего расчёта (CitizenAgent.ExpSiegeUnit уже заполнен).</summary>
    public static void FillRamSiege(SiegeUnit su, IList<CitizenAgent> crew)
    {
        if (su == null || crew == null || crew.Count == 0) return;
        CitizenAgent lead = crew[0];
        UnitType u = lead.unit;
        if (u == null || !u.IsRam) return;
        su.ram = true;
        su.ramMaxHp = Mathf.Max(1f, u.ramHp);
        su.ramHp = Mathf.Max(1f, su.ramMaxHp * (1f - lead.RamWear));
        su.ramArmor = RamArmor(u, crew);
        su.ramDamage = RamHit(u, crew);
        su.ramCd = Mathf.Max(0.3f, u.ramCooldown);
        su.ramSpeed = RamSpeed(u, crew);
        su.bldMul = u.ramBuildingMul;
        su.wallMul = u.ramBarrierMul;
        su.carry = RaidManager.CarryFoot * crew.Count;
    }

    public string RamText
    {
        get
        {
            if (!IsRamCarrier) return "";
            if (ramBroken) return UiKit.Col(UiKit.WarnHex, "таран сломан — соберут в казарме");
            return ramWear > 0.01f ? $"таран {Mathf.RoundToInt((1f - ramWear) * 100f)}%" : "таран цел";
        }
    }
}

/// <summary>
/// Таран дома (оборона, поход к краю карты, жизнь в казарме) - одна боевая единица, как в набеге:
///  - ведущий носильщик (первый в расчёте) несёт таран, напарник сидит «в нём» (ramHost): его не видно,
///    враги его не бьют, он входит в здания и выходит вместе с ведущим, вместе с ним уходит в поход;
///  - удары врагов по ведущему бьют по тарану (броня носильщиков помогает и изнашивается), людей таран не трогает;
///  - сам ломает укрепления врага рядом: заслоны и полевые башни нападающего (живой бой) и лагерь войска соседа;
///    по заслонам и лагерю ×ramBarrierMul, по остальному ×ramBuildingMul;
///  - таран разбит - напарник выпадает рядом, оба дерутся ножами (или мечами из Оружейной).
/// </summary>
public partial class CitizenAgent
{
    CitizenAgent ramHost;                     // напарник: в чьём таране сидит
    CitizenAgent ramRider;                    // ведущий: кто сидит в его таране
    float ramCrewCheckAt;
    int homeRamArmorTurn;
    float ramSwingAt = -99f, ramScan;
    FieldPost ramPost, ramIgnorePost;
    EnemyCamp ramCamp;
    float ramIgnoreUntil;
    SpriteRenderer ramLogSr, ramHeadSr, ramMateSr;
    Vector3 ramLogAt, ramHeadAt;

    const float RamHomeReach = 1.3f;
    const float RamHomeSeek = 9f;             // сам ищет укрепления врага не дальше этого
    const float RamFixAt = 0.4f;              // выбито столько прочности - таран сам идёт в казарму чиниться

    /// <summary>Целый, но побитый таран, и в Цитадели есть доски на починку.</summary>
    bool RamWantsFix
    {
        get
        {
            if (!HasRam || RamWear < 0.05f) return false;
            var st = TownHallStorage.Instance;
            int full = Mathf.Max(0, unit.ramRebuildPlanks);
            int need = Mathf.Min(full, Mathf.Max(1, Mathf.CeilToInt(full * RamWear)));   // как в BarracksController.RebuildRams
            return need == 0 || (st != null && UpgradeRules.CountAtLeast(st, RefinedKind.Planks, QualityTier.Medium) >= need);
        }
    }

    /// <summary>Сидит в таране напарника (не виден на карте).</summary>
    public bool RidingRam => ramHost != null;
    /// <summary>Кто несёт таран, в котором сидит этот носильщик.</summary>
    public CitizenAgent RamHostAgent => ramHost;
    /// <summary>Кто сидит в таране этого носильщика (null - несёт один).</summary>
    public CitizenAgent RamRiderAgent => ramRider != null && !ramRider.dead && ramRider.ramHost == this ? ramRider : null;
    /// <summary>Дома этот боец - таран: несёт целый таран сам (напарник внутри или его нет).</summary>
    public bool HomeRamOn => IsSoldier && HasRam && ramHost == null && !dead;

    /// <summary>Расчёт тарана дома: ведущий первым.</summary>
    List<CitizenAgent> HomeRamCrew()
    {
        var l = new List<CitizenAgent> { this };
        CitizenAgent r = RamRiderAgent;
        if (r != null) l.Add(r);
        return l;
    }

    /// <summary>Имена расчёта: «Иван и Пётр».</summary>
    public string RamCrewNames
    {
        get
        {
            CitizenAgent r = RamRiderAgent;
            return r != null ? citizenName + " и " + r.citizenName : citizenName;
        }
    }

    // Ведущий, в чей таран этот носильщик должен сесть (null - идёт сам)
    CitizenAgent WantedRamHost()
    {
        if (!IsSoldier || !HasRam || dead || IsDeparting || IsLookout || Squad == null) return null;
        foreach (var crew in RamCrews(Squad.Members, true))
        {
            if (!crew.Contains(this)) continue;
            CitizenAgent lead = crew[0];
            if (lead == this || lead.dead || !lead.IsSoldier || !lead.HasRam || lead.IsDeparting || lead.IsLookout) return null;
            if (lead.ramHost != null) return null;
            // В поход - вместе (оба идут или оба уже там), иначе каждый сам по себе
            bool meOut = expPhase != ExpPhase.None, leadOut = lead.expPhase != ExpPhase.None;
            if (meOut != leadOut) return null;
            return lead;
        }
        return null;
    }

    /// <summary>true - носильщик сидит в таране: за него ходит и воюет ведущий.</summary>
    bool RamRideTick(GridNavigation nav, float dt)
    {
        if (ramRider != null && (ramRider.dead || ramRider.ramHost != this)) ramRider = null;
        if (!IsRamCarrier)
        {
            if (ramHost != null) RamDismount(nav);
            return false;
        }

        // Состав расчёта пересчитываем не каждый кадр; разбитый таран и гибель ведущего - сразу
        bool stale = ramHost != null && (ramHost.dead || !ramHost.HasRam || !HasRam || ramHost.ramHost != null
                                         || ramHost.IsDeparting || IsDeparting || !ramHost.IsSoldier || ramHost.Squad != Squad);
        if (stale || Time.time >= ramCrewCheckAt)
        {
            ramCrewCheckAt = Time.time + 0.5f;
            CitizenAgent want = WantedRamHost();
            if (want != ramHost)
            {
                if (ramHost != null) RamDismount(nav);
                if (want != null) RamMount(want);
            }
        }
        if (ramHost == null) return false;

        CitizenAgent h = ramHost;
        mover.Clear();
        path = null;
        pathTarget = null;
        target = null;
        if (sr != null && sr.enabled) sr.enabled = false;

        // Поход: ведущий ушёл за край - и напарник с ним
        if (expPhase == ExpPhase.Away) return true;
        if (h.expPhase == ExpPhase.Away)
        {
            if (expPhase == ExpPhase.MarchOut) GoAway();
            return true;
        }

        if (h.inside)
        {
            if (!inside || !ReferenceEquals(insideRef, h.insideRef))
            {
                EnterBuilding(h.insideRef);   // и добычу там сдаст
                if (sr != null) sr.enabled = false;
            }
            if (barracks != null && ReferenceEquals(insideRef, barracks)) RestInBarracks(dt);
            return true;
        }
        if (inside)
        {
            inside = false;
            insideRef = null;
        }
        transform.position = h.transform.position;
        return true;
    }

    void RamMount(CitizenAgent lead)
    {
        ramHost = lead;
        lead.ramRider = this;
        pushingPost = null;
        if (IsSupplying) CancelSupply();
        mover.Clear();
        path = null;
        target = null;
        if (sr != null) sr.enabled = false;
        if (!inside && expPhase != ExpPhase.Away) transform.position = lead.transform.position;
    }

    /// <summary>Напарник выходит из тарана рядом с ведущим (таран разбит, ведущий пал, расчёт распался).</summary>
    void RamDismount(GridNavigation nav)
    {
        CitizenAgent h = ramHost;
        ramHost = null;
        if (h != null && h.ramRider == this) h.ramRider = null;
        mover.Clear();
        mover.ResetFails();
        path = null;
        pathTarget = null;
        if (expPhase == ExpPhase.Away) return;   // за краем карты - там его покажет набег
        if (inside)
        {
            if (sr != null) sr.enabled = false;
            return;
        }
        Vector2 p = h != null ? (Vector2)h.transform.position + new Vector2(-0.45f, 0.35f) : (Vector2)transform.position;
        if (nav != null)
        {
            Vector2Int cell = nav.WorldToCell(p);
            if (nav.IsBlocked(cell)) p = nav.CellCenter(nav.NearestFree(cell));
        }
        transform.position = new Vector3(p.x, p.y, 0f);
        if (sr != null) sr.enabled = true;
        lastCombatTime = Time.time;
    }

    // ---------- Таран дома: ведущий ----------

    /// <summary>Удар врага по тарану (укрытия уже учтены в TakeDamage).</summary>
    void RamHomeDamage(float dmg)
    {
        var crew = HomeRamCrew();
        dmg *= 1f - RamArmor(unit, crew);
        RamWearArmor(crew, ref homeRamArmorTurn);
        lastCombatTime = Time.time;
        if (dmg <= 0f) return;
        float max = Mathf.Max(1f, unit.ramHp);
        float left = max * (1f - RamWear) - dmg;
        if (left <= 0f) { BreakHomeRam(crew); return; }
        foreach (var c in crew) c.SetRamCondition(left / max);
    }

    void BreakHomeRam(List<CitizenAgent> crew)
    {
        string names = RamCrewNames;
        foreach (var c in crew) c.BreakRam();
        CombatFx.Ring(transform.position, 0.8f, new Color(0.55f, 0.4f, 0.22f, 0.7f), 0.5f);
        WarManager.Notify(crew.Count > 1 ? $"Таран разбит — {names} дерутся ножами" : $"Таран разбит — {names} дерётся ножом", false);
        ramPost = null;
        ramCamp = null;
        CitizenAgent r = RamRiderAgent;
        if (r != null) r.RamDismount(GridNavigation.Instance);
    }

    /// <summary>Ход ведущего с целым тараном: людей не трогает, ломает укрепления врага, иначе - приказ отряда.</summary>
    void RamHomeTick(GridNavigation nav, float dt)
    {
        target = null;
        healTarget = null;
        var crew = HomeRamCrew();
        float foot = moveSpeed;
        moveSpeed = RamSpeed(unit, crew);   // бегом с тараном (на время хода)
        try
        {
            if (marching && !IsMarching) marching = false;
            if (IsMarching) { MoveTick(nav, dt); return; }

            ramScan -= dt;
            if (ramScan <= 0f || !RamTargetValid())
            {
                ramScan = 0.4f;
                RamPickHome();
            }
            if (RamTargetValid()) { RamStrikeHome(nav, crew); return; }

            // Таран сильно побит, а врагов рядом нет - в казарму на починку (если есть доски)
            if (order != SoldierOrder.Move && RamWear >= RamFixAt && RamWantsFix && Usable(barracks)
                && !InCombat && !EnemyAgent.AnyWithin(transform.position, 8f))
            {
                recovering = true;
                if (WalkTo(nav, barracks)) EnterBuilding(barracks);
                return;
            }

            switch (order)
            {
                case SoldierOrder.Garrison:
                    if (Usable(barracks)) { if (WalkTo(nav, barracks)) EnterBuilding(barracks); }
                    else GuardTick(nav, dt, false);
                    break;
                case SoldierOrder.Guard:
                    GuardTick(nav, dt, Usable(barracks));
                    break;
                default:
                    MoveTick(nav, dt);
                    break;
            }
        }
        finally { moveSpeed = foot; }
    }

    bool RamTargetValid()
    {
        if (ramPost != null && (!ramPost.Alive || !ramPost.Hostile)) ramPost = null;
        if (ramCamp != null && !ramCamp.Alive) ramCamp = null;
        return ramPost != null || ramCamp != null;
    }

    void RamPickHome()
    {
        Vector2 me = transform.position;
        Anchor(out Vector2 anchor, out float leash);
        float reach = Mathf.Max(RamHomeSeek, AggroRadius);

        // Уже ломаем и цель не убежала за привязь - не дёргаемся
        if (RamTargetValid())
        {
            Vector2 at = ramPost != null ? ramPost.ClosestPoint(me) : ramCamp.Position;
            if (Vector2.Distance(at, anchor) <= leash + 3f) return;
            ramPost = null;
            ramCamp = null;
        }

        float best = float.MaxValue;
        foreach (var p in FieldPost.All)
        {
            if (p == null || !p.Alive || p.Raid || !p.Hostile) continue;
            if (p == ramIgnorePost && Time.time < ramIgnoreUntil) continue;
            float d = p.DistanceTo(me);
            if (d > reach || p.DistanceTo(anchor) > leash) continue;
            if (d < best) { best = d; ramPost = p; ramCamp = null; }
        }
        foreach (var c in EnemyCamp.All)
        {
            if (c == null || !c.Alive) continue;
            float d = Mathf.Max(0f, Vector2.Distance(me, c.Position) - EnemyCamp.SmashReach);
            if (d > reach || Vector2.Distance(c.Position, anchor) > leash + EnemyCamp.SmashReach) continue;
            if (d < best) { best = d; ramCamp = c; ramPost = null; }
        }
    }

    void RamStrikeHome(GridNavigation nav, List<CitizenAgent> crew)
    {
        Vector2 me = transform.position;
        Vector2 aim;
        float d;
        if (ramPost != null) { aim = ramPost.ClosestPoint(me); d = ramPost.DistanceTo(me); }
        else { aim = ramCamp.Position; d = Mathf.Max(0f, Vector2.Distance(me, aim) - EnemyCamp.SmashReach); }

        if (d <= RamHomeReach)
        {
            mover.Clear();
            lastCombatTime = Time.time;
            if (facing != null) facing.LookAt(aim);
            if (Time.time < ramSwingAt) return;
            ramSwingAt = Time.time + Mathf.Max(0.3f, unit.ramCooldown);
            float hit = RamHit(unit, crew);
            bool barrier = ramPost == null || IsBarrierPost(ramPost.Kind);
            hit *= barrier ? unit.ramBarrierMul : unit.ramBuildingMul;
            CombatFx.Flash(aim);
            Sfx.At("ram_hit", aim);
            if (figure != null) figure.Lunge();
            if (ramPost != null) ramPost.TakeDamage(hit);
            else ramCamp.Smash(hit);
            return;
        }

        mover.Step(transform, nav, aim, moveSpeed, RamHomeReach * 0.8f);
        if (mover.Fails >= 3)
        {
            // Не подойти (за стеной) - бросаем на время
            if (ramPost != null) { ramIgnorePost = ramPost; ramIgnoreUntil = Time.time + 8f; }
            ramPost = null;
            ramCamp = null;
            mover.Clear();
            mover.ResetFails();
        }
    }

    static bool IsBarrierPost(PostKind k)
    {
        return k == PostKind.Barricade || k == PostKind.SpikedBarricade || k == PostKind.WheeledBarricade || k == PostKind.QuickBarricade;
    }

    /// <summary>Таран у ведущего: бревно и напарник рядом (как в набеге).</summary>
    void RamLook(bool shown)
    {
        bool on = shown && HomeRamOn && !IsAway;
        if (!on)
        {
            if (ramLogSr != null) ramLogSr.enabled = ramHeadSr.enabled = ramMateSr.enabled = false;
            return;
        }
        float s = bodyScale;
        if (ramLogSr == null)
        {
            ramMateSr = MakePart("RamMate", new Vector2(-s * 0.85f, s * 0.05f), new Vector2(1f, 1f), currentTint, 9);
            ramLogSr = MakePart("RamLog", new Vector2(-s * 0.2f, -s * 0.12f), new Vector2(s * 2.1f, s * 0.3f), new Color(0.47f, 0.31f, 0.16f), 16);
            ramHeadSr = MakePart("RamHead", new Vector2(s * 0.95f, -s * 0.12f), new Vector2(s * 0.34f, s * 0.46f), new Color(0.27f, 0.27f, 0.3f), 17);
            GameArt.DressRam(ramLogSr, ramHeadSr, s);   // нарисованный таран вместо двух квадратиков
            ramLogAt = ramLogSr.transform.localPosition;
            ramHeadAt = ramHeadSr.transform.localPosition;
        }
        ramLogSr.enabled = ramHeadSr.enabled = true;
        // Facing разворачивает только картинки - смещения частей зеркалим сами
        float dir = facing != null && facing.Left ? -1f : 1f;
        ramLogSr.transform.localPosition = new Vector3(ramLogAt.x * dir, ramLogAt.y, 0f);
        ramHeadSr.transform.localPosition = new Vector3(ramHeadAt.x * dir, ramHeadAt.y, 0f);
        CitizenAgent r = RamRiderAgent;
        ramMateSr.enabled = r != null;
        if (r != null && sr != null)
        {
            // Напарник - копия рисунка ведущего, сзади на бревне
            ramMateSr.sprite = sr.sprite;
            ramMateSr.color = sr.color;
            ramMateSr.flipX = sr.flipX;
            ramMateSr.sortingOrder = sr.sortingOrder - 1;
            ramMateSr.transform.localPosition = sr.transform.localPosition + new Vector3(-s * 0.85f * dir, s * 0.05f, 0f);
            ramMateSr.transform.localScale = sr.transform.localScale;
        }
    }
}
