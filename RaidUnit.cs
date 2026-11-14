using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Боец на карте набега.
///  - Наш налётчик (Attacker): "тень" настоящего солдата из деревни (Source). Здоровье, урон, оружие,
///    щит и конь - его собственные: раны и износ остаются с ним после похода, погиб здесь - погиб и дома.
///    Несёт мешок добычи (CarryCap). Приказы: Штурм (сам ищет врагов и здания), Держать точку,
///    Ломать здание, Отступить (уйти через западный край карты домой).
///    Держать точку: клик по земле - идут туда и по дороге бьют всех, кто встретится (и здания вплотную);
///    двойной клик - марш: идут, ни на что не отвлекаясь, а дойдя - держат точку как обычно.
///  - Тяжёлый Рыцарь (UnitType.taunt): башни и пушки защитников бьют его первым (TowerTarget).
///  - Щит с ядром хаоса: снаряд издалека иногда улетает обратно - в стрелка или в башню (ChaosReflect).
///  - Защитник (бот): свои здоровье и снаряжение. Сам по себе стоит на посту и по тревоге бросается на наших
///    (далеко от поста не уходит). Но обычно им командует RaidDefense, как живой игрок: назначает точку
///    (Command), насколько от неё отходить и кого бить - контратака, оборона у Цитадели, раненых - лечиться.
///    За заслоном или стеной защитник получает меньше урона от тех, кто снаружи. Вражеский маг лечит своих.
///  - Наш боец в плаще из листвы: защитники замечают его вдвое ближе, пока он не бьёт (разведчика - лишь вплотную),
///    см. NoticeRange. Броня и плащ видны на бойце; скрытый плащом боец для игрока полупрозрачный.
///  - pushingPost: колёсная баррикада, которую толкает наш боец (FieldPost.TickRaidPusher).
///  - Таран (UnitType.isRam): носильщики одного отряда - один RaidUnit (ведущий, остальные спрятаны в нём - Riders).
///    У тарана своя прочность и броня (+ броня носильщиков), по людям не бьёт, сам идёт к стенам. Разбит - носильщики
///    выпадают рядом и дерутся ножами (или мечами) как обычные бойцы. См. раздел «Таран».
/// Создаёт и удаляет RaidManager.
/// </summary>
public class RaidUnit : MonoBehaviour
{
    public static readonly List<RaidUnit> All = new List<RaidUnit>();

    public enum Order { Idle, Auto, Hold, Focus, Retreat }

    public bool Attacker { get; private set; }
    public CitizenAgent Source { get; private set; }
    public Squad Squad { get; private set; }
    public UnitType Unit { get; private set; }
    /// <summary>Голем Алтаря жизни: наш налётчик без жителя (Source == null), статы - из Unit, здоровье - своё.</summary>
    public bool IsGolem { get; private set; }
    // Наш боец-житель (статы и раны - у Source)
    bool FromSource => Attacker && !IsGolem;
    /// <summary>Какой это боец (для иконки на карточке отряда): у налётчика - его тип из казармы.</summary>
    public UnitType ShownUnit => FromSource ? (Source != null ? Source.ExpUnit : null) : Unit;

    public Order Mode { get; private set; } = Order.Idle;
    public Vector2 Post { get; private set; }          // защитник: пост; налётчик: точка удержания
    public RaidBuilding Focus { get; private set; }
    public readonly Loot Carried = new Loot();
    public bool Left { get; private set; }
    public DefRole Role { get; set; } = DefRole.Guard;   // защитник: роль в обороне (см. RaidDefense)
    /// <summary>Наш налётчик толкает колёсную баррикаду (ставит FieldPost / FieldPostManager).</summary>
    public FieldPost pushingPost;

    // Приказ командира обороны: держаться у точки Post, отходить от неё не дальше cmdLeash, замечать врага за cmdLook
    bool commanded;
    float cmdLeash = 14f, cmdLook;

    bool dead;

    // Защитник
    float hp, maxHp, powerMul = 1f;
    Weapon weapon;
    Shield shield;
    bool alerted;

    // Бой
    RaidUnit target;
    RaidBuilding targetBuilding;
    RaidBuilding ignoreBuilding;
    float ignoreBuildingUntil;
    RaidUnit ignoreUnit;
    float ignoreUnitUntil;
    bool breach;
    Vector2 approach;
    float approachTimer;
    RaidUnit healTarget;
    float attackTimer, thinkTimer, healFx;
    float lastCombat = -99f;

    float patrolWait;
    Vector2 patrolPoint;
    bool hasPatrol;
    Vector2 exitPoint;

    readonly RaidMover mover = new RaidMover();
    bool restingAtPost;   // раненый отдыхает на перевалочном пункте
    bool marching;        // марш (двойной клик): до точки Post ни на что не отвлекается
    // Приказы точнее (final-9): «бить этого» - нажали по врагу; путь к точке - от holdFrom до Post,
    // по дороге бьют только тех, кто рядом с дорогой (раньше гонялись за любым в 6+ клетках и «отбегали»)
    RaidUnit hunt;
    Vector2 huntLast, holdFrom;
    const float WayCorridor = 5f;       // по дороге к точке: враг не дальше этого от линии пути
    const float FocusDefend = 2.6f;     // «ломать здание»: отвечают только тем, кто вплотную
    const float MarchArrive = 1.6f, HoldTravel = 3f, HealPulse = 0.8f;
    const float WayBuildingReach = 1.6f;   // клик «с боем по пути»: здание ближе этого по дороге - ломают
    SpriteRenderer body, sack;

    // ---------- Свойства ----------

    public Vector2 Position => transform.position;
    public bool Alive => !dead && !Left && (!FromSource || (Source != null && !Source.IsDead));
    public float Hp => ramOn ? ramHp : FromSource ? (Source != null ? Source.Hp : 0f) : hp;
    public float MaxHp => ramOn ? ramMax : FromSource ? (Source != null ? Source.MaxHp : 1f) : maxHp;
    public float HpFraction => MaxHp > 0f ? Mathf.Clamp01(Hp / MaxHp) : 0f;
    public bool InCombat => Time.time - lastCombat < 3f;
    public bool IsScout => FromSource ? (Source != null && Source.IsScout) : (Unit != null && Unit.unitName == RaidManager.ScoutUnitName);
    public bool IsMounted => FromSource ? (Source != null && Source.HasHorse) : (Unit != null && Unit.requiresHorse);
    /// <summary>Наша полевая пушка: бьёт ядрами (CannonBallistics) - что стоит на прямой, то и ломает.</summary>
    public bool IsCannon => Attacker && Source != null && Source.IsFieldCannon;
    /// <summary>Наша катапульта: камни навесом через стены, по площади (урон падает с дальностью).</summary>
    public bool IsCatapult => Attacker && Source != null && Source.IsCatapult;

    public string Name => ramOn ? BarracksController.RamName + ": " + RamCrewNames : IsGolem ? LifeAltar.GolemName + " Алтаря жизни" : Attacker ? (Source != null ? Source.DisplayName + " " + Source.CitizenName : "—") : (Unit != null ? Unit.unitName : "—");

    float Range => ramOn ? RamReach : FromSource ? (Source != null ? Source.ExpRange : 1f) : Unit.attackRange + (weapon != null ? weapon.RangeBonus : 0f);
    float Cooldown => ramOn ? Mathf.Max(0.3f, RamUnit.ramCooldown) : FromSource ? (Source != null ? Source.ExpCooldown : 1f) : Mathf.Max(0.1f, Unit.attackCooldown);
    // Замедление (лук с зельем замедления) режет скорость
    float Speed => (ramOn ? CitizenAgent.RamSpeed(RamUnit, ramCrew) : FromSource ? (Source != null ? Source.moveSpeed : 1.5f) : (IsGolem ? Unit.moveSpeed : Unit.moveSpeed * 0.95f)) * SlowEffect.Mul(this) * (RaidMap.Instance != null ? RaidMap.Instance.SpeedMul(Position, IsMounted) : 1f);
    bool Ranged => !ramOn && (FromSource ? (Source != null && Source.ExpRanged) : Unit.attackRange > 2f);
    float HealPerSecond => ramOn ? 0f : FromSource ? (Source != null ? Source.ExpHealPerSecond : 0f) : Unit.healPerSecond;
    float HealRange => FromSource ? (Source != null ? Source.ExpHealRange : 0f) : Unit.healRange;
    float Aggro => Mathf.Max(6f, Range + 1.5f);

    // Сколько добычи унесёт (конь везёт вдвое больше)
    public int CarryCap => IsGolem ? 0 : ramOn ? RaidManager.CarryFoot * Mathf.Max(1, ramCrew.Count) : (IsMounted ? RaidManager.CarryMounted : RaidManager.CarryFoot);   // носильщики тарана несут каждый свой мешок   // голем добычу не носит
    public int CarryFree => Mathf.Max(0, CarryCap - Carried.Weight);   // золото лёгкое - см. Loot.GoldPerSlot

    // Урон удара (у налётчика при этом изнашивается оружие)
    float StrikeDamage()
    {
        if (ramOn) return CitizenAgent.RamHit(RamUnit, ramCrew);
        if (FromSource) return Source != null ? Source.ExpeditionStrike() : 0f;
        return Unit.damage * powerMul * (weapon != null ? weapon.DamageMul : 1f);
    }

    public bool IsRangedUnit => Ranged;
    public bool IsHealerUnit => HealPerSecond > 0f;
    /// <summary>Идёт маршем к точке (двойной клик): врагов по дороге не замечает.</summary>
    public bool IsMarching => marching && Mode == Order.Hold;
    /// <summary>Притягивает огонь башен (Тяжёлый Рыцарь).</summary>
    public bool Taunts
    {
        get
        {
            if (!Alive || ramOn) return false;
            UnitType u = FromSource ? (Source != null ? Source.ExpUnit : null) : Unit;
            return u != null && u.IsTaunter;
        }
    }
    /// <summary>Боевая ценность с учётом ран (для решений командиров).</summary>
    public float Power => (FromSource ? BotVillage.UnitPower(Source != null ? Source.ExpUnit : null) : BotVillage.UnitPower(Unit)) * Mathf.Max(0.15f, HpFraction);

    /// <summary>
    /// С какого расстояния защитники замечают этого бойца: у нашего бойца-жителя - по его плащу (CitizenAgent.NoticeRange),
    /// у остальных - baseRange.
    /// </summary>
    public float NoticeRange(float baseRange)
    {
        return FromSource && Source != null && !ramOn ? Source.NoticeRange(baseRange) : baseRange;   // таран плащом не спрячешь
    }

    /// <summary>Скрыт плащом (или сидит в дозоре): замечают ближе обычного.</summary>
    public bool IsConcealed => FromSource && Source != null && !ramOn && Source.IsConcealed;

    /// <summary>Защитник (или башня) у from замечает этого нашего бойца на расстоянии look.</summary>
    public bool NoticedFrom(Vector2 from, float look)
    {
        return Vector2.Distance(from, Position) <= NoticeRange(look);
    }

    /// <summary>
    /// Ближайший живой НАШ боец, которого видно из from (плащ из листвы прячет: NoticeRange).
    /// Для башен и защитников; NearestOf(true, ...) - без учёта скрытности (проверки "свои рядом").
    /// </summary>
    public static RaidUnit NearestNoticed(Vector2 from, float radius, System.Func<RaidUnit, bool> filter = null)
    {
        return NearestOf(true, from, radius, u => u.NoticedFrom(from, radius) && (filter == null || filter(u)));
    }

    /// <summary>Цель башни / пушки защитников: Тяжёлый Рыцарь в радиусе - первым, иначе ближайший замеченный.</summary>
    public static RaidUnit TowerTarget(Vector2 from, float radius, System.Func<RaidUnit, bool> filter = null)
    {
        RaidUnit t = NearestNoticed(from, radius, u => u.Taunts && (filter == null || filter(u)));
        return t != null ? t : NearestNoticed(from, radius, filter);
    }

    // Наш боец ударил - плащ его на время не прячет
    void MarkStrike()
    {
        if (artFigure != null) artFigure.Lunge();
        if (FromSource && Source != null) Source.MarkAttacked();
    }

    public float DamagePreview => ramOn ? CitizenAgent.RamHit(RamUnit, ramCrew) : FromSource ? (Source != null ? Source.ExpDamagePreview : 0f) : Unit.damage * powerMul * (weapon != null ? weapon.DamageMul : 1f);

    // ---------- Создание ----------

    public static RaidUnit SpawnAttacker(CitizenAgent src, Squad sq, Vector2 pos)
    {
        RaidUnit u = Make("Raider_" + src.CitizenName, pos);
        u.Attacker = true;
        u.Source = src;
        u.Squad = sq;
        u.Post = pos;
        u.exitPoint = pos;
        u.BuildLook(src.ExpBodyScale, src.ExpTint, new Color(0.85f, 0.95f, 1f), src.HasHorse, src.ArtKind);
        u.BuildGearLook(src.ExpBodyScale);
        if (src.IsFieldCannon) u.BuildCannonLook(src.ExpBodyScale);
        if (src.IsCatapult) u.BuildCatapultLook(src.ExpBodyScale);
        return u;
    }

    /// <summary>Голем Алтаря жизни в набеге: наш налётчик без жителя. hp - его раны из дома.</summary>
    public static RaidUnit SpawnGolem(UnitType unit, float hp, Vector2 pos)
    {
        RaidUnit u = Make("Raider_Golem", pos);
        u.Attacker = true;
        u.IsGolem = true;
        u.Unit = unit;
        u.Post = pos;
        u.exitPoint = pos;
        u.powerMul = 1f;
        u.maxHp = Mathf.Max(1f, unit.maxHp);
        u.hp = Mathf.Clamp(hp, 1f, u.maxHp);
        u.attackTimer = Random.Range(0f, 1f);
        u.BuildLook(unit.bodyScale, unit.color, new Color(0.85f, 0.95f, 1f), false, GameArt.Golem);
        if (u.artFigure == null) u.Part("Core", Vector2.zero, new Vector2(0.24f, 0.24f), new Color(0.6f, 1f, 0.6f), 14);   // без рисунка - квадратик с ядром
        return u;
    }

    public static RaidUnit SpawnDefender(UnitType unit, Vector2 pos, float powerMul, float gearChance, int level)
    {
        RaidUnit u = Make("Defender_" + unit.unitName, pos);
        u.Attacker = false;
        u.Unit = unit;
        u.Post = pos;
        u.powerMul = powerMul;
        u.maxHp = u.hp = Mathf.Max(1f, unit.maxHp * powerMul);
        if (unit.usesWeapon && Random.value < gearChance) u.weapon = new Weapon(unit.weaponKind, RollQuality(level));
        if (unit.usesShield && Random.value < gearChance * 0.7f) u.shield = new Shield(RollQuality(level));
        u.attackTimer = Random.Range(0f, 1f);
        u.BuildLook(unit.bodyScale, unit.color, Color.black, unit.requiresHorse, GameArt.FigureKind(unit));
        return u;
    }

    // ---------- Живой бой: «кукла» бойца из деревни защитника (его игра считает бой, тут - только картинка) ----------

    /// <summary>Боец живого боя: стоит там, где сказал последний снимок (SetNet), сам не думает и не дерётся.</summary>
    public bool Puppet { get; private set; }
    public int NetId { get; private set; }
    Vector2 netPos;
    struct NetSample { public float t; public Vector2 p; }
    readonly List<NetSample> netBuf = new List<NetSample>();

    /// <summary>Наш налётчик в живом бою: дальше им управляет снимок из деревни защитника.</summary>
    public void MakePuppet(int id)
    {
        Puppet = true;
        NetId = id;
        netPos = Position;
        mover.Clear();
    }

    /// <summary>Защитник в живом бою: житель (unit == null) или боец деревни.</summary>
    public static RaidUnit SpawnPuppet(UnitType unit, Vector2 pos, int id)
    {
        bool citizen = unit == null;
        if (citizen) unit = CitizenLook;
        RaidUnit u = Make((citizen ? "Citizen_" : "Defender_") + unit.unitName, pos);
        u.Attacker = false;
        u.Unit = unit;
        u.Post = pos;
        u.maxHp = u.hp = Mathf.Max(1f, unit.maxHp);
        u.BuildLook(unit.bodyScale, unit.color, Color.black, unit.requiresHorse, citizen ? GameArt.Civilian : GameArt.FigureKind(unit));
        u.MakePuppet(id);
        return u;
    }

    static readonly UnitType CitizenLook = new UnitType
    {
        unitName = "Житель", maxHp = CitizenAgent.CitizenMaxHp, damage = 0f, attackRange = 1f, moveSpeed = 2f,
        color = new Color(0.85f, 0.72f, 0.5f), bodyScale = 0.42f
    };

    /// <summary>Снимок: где боец, сколько здоровья (доля), дерётся ли.</summary>
    /// stamp - когда снят снимок (наши часы, см. RaidManager.NetRenderTime); меньше 0 - без плавности.
    public void SetNet(Vector2 pos, float hpFrac, bool fighting, float stamp = -1f)
    {
        if (!Puppet || dead || Left) return;
        netPos = pos;
        Vector2 prev = netBuf.Count > 0 ? netBuf[netBuf.Count - 1].p : Position;
        if (Vector2.Distance(prev, pos) > 5f)
        {
            // Скачок (вышел из здания, догнали) - сразу на место
            netBuf.Clear();
            transform.position = new Vector3(pos.x, pos.y, 0f);
        }
        if (stamp >= 0f && (netBuf.Count == 0 || stamp > netBuf[netBuf.Count - 1].t + 0.001f))
        {
            netBuf.Add(new NetSample { t = stamp, p = pos });
            if (netBuf.Count > 12) netBuf.RemoveAt(0);
        }
        hpFrac = Mathf.Clamp01(hpFrac);
        if (ramOn) { ramHp = Mathf.Max(0.5f, hpFrac * ramMax); SyncRam(); }
        else if (FromSource) { if (Source != null && !Source.IsDead) Source.ExpSetHp(hpFrac * Source.MaxHp); }
        else hp = Mathf.Max(0.5f, hpFrac * maxHp);
        if (fighting) lastCombat = Time.time;
    }

    /// <summary>Снимок: что боец несёт.</summary>
    public void SetCarried(RaidLoot l)
    {
        Carried.Clear();
        if (l != null) Carried.Add(l.ToLoot());
    }

    /// <summary>Погиб в деревне защитника - погиб и у нас (житель-боец тоже).</summary>
    public void PuppetKill()
    {
        if (dead || Left) return;
        CitizenAgent src = FromSource ? Source : null;
        Carried.Clear();   // мешок остался у защитника
        Die(null);
        if (src != null && !src.IsDead) src.KillOutright();
    }

    /// <summary>Ушёл за край его карты с добычей - домой.</summary>
    public void PuppetLeave(RaidLoot loot)
    {
        if (dead || Left) return;
        SetCarried(loot);
        Leave();
    }

    /// <summary>Защитник пропал из снимка: погиб (died) или зашёл в здание.</summary>
    public void PuppetVanish(bool died)
    {
        if (dead) return;
        dead = true;
        if (died) CombatFx.Death(Position);
        Destroy(gameObject);
    }

    void PuppetTick(float dt)
    {
        if (netBuf.Count > 0)
        {
            // Между двумя снимками, в моменте NetRenderTime: движение ровное, без рывков
            float rt = RaidManager.NetRenderTime;
            while (netBuf.Count >= 2 && netBuf[1].t <= rt) netBuf.RemoveAt(0);
            Vector2 want;
            NetSample a = netBuf[0];
            if (netBuf.Count >= 2 && rt > a.t)
            {
                NetSample b = netBuf[1];
                want = Vector2.Lerp(a.p, b.p, Mathf.Clamp01((rt - a.t) / Mathf.Max(0.001f, b.t - a.t)));
            }
            else want = a.p;   // новых снимков нет (или ещё рано) - стоит, где был
            Vector2 cur = Position;
            // Подхват после скачка или задержки - быстро, но не мгновенно
            Vector2 n = Vector2.Distance(cur, want) > 1.5f ? Vector2.MoveTowards(cur, want, 12f * dt) : want;
            transform.position = new Vector3(n.x, n.y, 0f);
            return;
        }
        Vector2 p = Position;
        float d = Vector2.Distance(p, netPos);
        if (d > 0.01f)
        {
            Vector2 n = Vector2.MoveTowards(p, netPos, Mathf.Max(2.5f, d * 4f) * dt);
            transform.position = new Vector3(n.x, n.y, 0f);
        }
    }

    static QualityTier RollQuality(int level)
    {
        float r = Random.value;
        if (r < 0.05f * level) return QualityTier.Best;
        if (r < 0.15f + 0.12f * level) return QualityTier.Good;
        return QualityTier.Medium;
    }

    static RaidUnit Make(string n, Vector2 pos)
    {
        GameObject go = new GameObject(n);
        go.transform.position = new Vector3(pos.x, pos.y, 0f);
        RaidUnit nu = go.AddComponent<RaidUnit>();
        // Обновление 12: «думают» не все в один кадр (раньше сотня бойцов искала цели разом раз в 0,3 с - рывки)
        nu.thinkTimer = Random.Range(0f, 0.3f);
        return nu;
    }

    /// <summary>Как боец выглядит (для записи боя, обновление 12): рисунок, конь, цвет.</summary>
    public string LookKind { get; private set; }
    public bool LookHorse { get; private set; }
    public Color LookTint { get; private set; } = Color.white;

    void BuildLook(float scale, Color color, Color outline, bool horse, string kind)
    {
        LookKind = kind; LookHorse = horse; LookTint = color;
        scale = Mathf.Max(0.3f, scale);
        SpriteRenderer ol = Part("Outline", Vector2.zero, new Vector2(scale + 0.12f, scale + 0.12f), outline, 12);
        body = Part("Body", Vector2.zero, new Vector2(scale, scale), color, 13);
        artFigure = GameArt.Dress(body, kind, ol);
        if (horse)
        {
            SpriteRenderer h = Part("Horse", new Vector2(0f, -scale * 0.45f), new Vector2(scale * 1.5f, scale * 0.42f), new Color(0.45f, 0.29f, 0.14f), 11);
            if (artFigure != null) { GameArt.PlaceHorse(h, scale); artFigure.Mounted = true; artFigure.Horse = h; }
        }
        sack = Part("Sack", new Vector2(scale * 0.45f, scale * 0.45f), new Vector2(0.22f, 0.22f), new Color(0.9f, 0.7f, 0.2f), 15);
        sack.enabled = false;
        facing = gameObject.AddComponent<Facing>();
    }

    ArtFigure artFigure;   // рисунок человечка (null - квадратик)
    Facing facing;         // смотрит туда, куда идёт

    // ---------- Броня и плащ нашего бойца (CitizenAgent_Gear) ----------

    SpriteRenderer gearArmor, gearBelt, gearCloak, gearLeafA, gearLeafB;
    SpriteRenderer[] allParts;
    float partsAlpha = 1f;

    void BuildGearLook(float scale)
    {
        scale = Mathf.Max(0.3f, scale);
        gearArmor = Part("Armor", new Vector2(0f, scale * 0.08f), new Vector2(scale * 1.1f, scale * 0.55f), new Color(0.66f, 0.7f, 0.76f), 14);
        gearBelt = Part("ArmorBelt", new Vector2(0f, -scale * 0.22f), new Vector2(scale * 0.8f, scale * 0.12f), new Color(0.3f, 0.32f, 0.36f), 15);
        gearCloak = Part("Cloak", new Vector2(0f, scale * 0.2f), new Vector2(scale * 1.12f, scale * 0.68f), new Color(0.25f, 0.5f, 0.2f), 14);
        gearLeafA = Part("LeafA", new Vector2(-scale * 0.52f, -scale * 0.05f), new Vector2(scale * 0.26f, scale * 0.26f), new Color(0.5f, 0.8f, 0.3f), 15);
        gearLeafB = Part("LeafB", new Vector2(scale * 0.5f, scale * 0.38f), new Vector2(scale * 0.26f, scale * 0.26f), new Color(0.5f, 0.8f, 0.3f), 15);
        gearLeafA.transform.localRotation = gearLeafB.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        if (artFigure != null && GameArt.Armor() != null)
        {
            // Нарисованные броня и плащ - поверх человечка, в его рамке
            DressGear(gearArmor, GameArt.Armor(), scale, 15);
            DressGear(gearCloak, GameArt.Cloak(), scale, 15);
            gearBelt.sprite = gearLeafA.sprite = gearLeafB.sprite = null;
        }
        UpdateGearLook();
    }

    static void DressGear(SpriteRenderer r, Sprite s, float scale, int order)
    {
        r.sprite = s;
        r.color = Color.white;
        r.sortingOrder = order;
        r.transform.localPosition = Vector3.zero;
        r.transform.localScale = new Vector3(scale, scale, 1f);
    }

    // Броня / плащ могут порваться в бою; скрытый плащом боец для игрока полупрозрачный
    void UpdateGearLook()
    {
        if (gearArmor == null || Source == null) return;
        bool armor = Source.HasArmor, cloak = Source.HasCloak;
        gearArmor.enabled = gearBelt.enabled = armor;
        gearCloak.enabled = gearLeafA.enabled = gearLeafB.enabled = cloak;

        float alpha = Source.IsConcealed ? 0.45f : 1f;
        if (Mathf.Abs(alpha - partsAlpha) < 0.001f) return;
        partsAlpha = alpha;
        if (allParts == null) allParts = GetComponentsInChildren<SpriteRenderer>(true);
        foreach (var r in allParts)
        {
            if (r == null) continue;
            Color c = r.color;
            c.a = r == gearCloak || r == gearLeafA || r == gearLeafB ? Mathf.Min(1f, alpha + 0.15f) : alpha;
            r.color = c;
        }
    }

    // Полевая пушка: колёса и ствол (ствол смотрит на цель, с подъёмника - длиннее)
    SpriteRenderer cannonBarrel, cannonWheels;
    Vector2 cannonAim = Vector2.right;

    void BuildCannonLook(float scale)
    {
        cannonWheels = Part("Wheels", new Vector2(0f, -scale * 0.42f), new Vector2(scale * 1.2f, scale * 0.3f), new Color(0.35f, 0.24f, 0.12f), 11);
        cannonBarrel = Part("Barrel", Vector2.zero, Vector2.one, new Color(0.12f, 0.12f, 0.14f), 14);
        UpdateCannonLook();
    }

    void UpdateCannonLook()
    {
        if (cannonBarrel == null || Source == null) return;
        float scale = Mathf.Max(0.3f, Source.ExpBodyScale);
        bool up = Source.CannonLift.Up;
        int state = up ? 2 : Source.CannonLift.Has ? 1 : 0;
        Sprite carriage = GameArt.FxCarriage(state);
        if (carriage != null && GameArt.FxBarrel() != null)
        {
            // Нарисованная пушка, как дома (CitizenAgent.CannonLook): лафет впереди бойца, ствол на цапфе смотрит на цель
            cannonWheels.sprite = carriage;
            cannonWheels.color = Color.white;
            cannonWheels.sortingOrder = 16;
            cannonBarrel.sprite = GameArt.FxBarrel();
            cannonBarrel.color = Color.white;
            cannonBarrel.sortingOrder = 17;
            float side = cannonWheels.flipX ? -1f : 1f;
            float k = scale * 1.3f;
            Vector2 at = new Vector2(side * scale * 0.5f, -scale * 0.22f);
            cannonWheels.transform.localPosition = at;
            cannonWheels.transform.localScale = new Vector3(k, k, 1f);
            Vector2 tr = GameArt.CarriageTrunnion(state) * k;
            Vector2 aim = new Vector2(side * Mathf.Abs(cannonAim.x), cannonAim.y);
            if (aim.sqrMagnitude < 0.01f) aim = new Vector2(side, 0f);
            aim.Normalize();
            float blen = scale * (up ? 1.0f : 0.85f);
            cannonBarrel.transform.localScale = new Vector3(blen, blen, 1f);
            cannonBarrel.transform.localPosition = at + new Vector2(side * tr.x, tr.y) + aim * blen * 0.25f;
            float a = Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg - (cannonBarrel.flipX ? 180f : 0f);
            cannonBarrel.transform.localRotation = Quaternion.Euler(0f, 0f, a);
            cannonBarrel.flipY = cannonBarrel.flipX ? aim.x > 0f : aim.x < 0f;
            return;
        }
        float len = scale * (up ? 1.35f : 1.05f), th = scale * (up ? 0.34f : 0.28f);
        cannonBarrel.transform.localPosition = new Vector3(cannonAim.x * len * 0.5f, cannonAim.y * len * 0.5f + (up ? scale * 0.2f : 0f), 0f);
        cannonBarrel.transform.localScale = new Vector3(len, th, 1f);
        cannonBarrel.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(cannonAim.y, cannonAim.x) * Mathf.Rad2Deg);
        if (cannonWheels != null) cannonWheels.color = up ? new Color(0.75f, 0.55f, 0.25f) : new Color(0.35f, 0.24f, 0.12f);
    }

    /// <summary>
    /// Выстрел полевой пушки по точке aim (по бойцу tu или по зданию tb). Ядро бьёт первое, что стоит на прямой:
    /// стену или заслон на пути - так пушка и пробивает дорогу. Подъёмник поднимает пушку, если прямая занята.
    /// </summary>
    void CannonFire(Vector2 aim, RaidUnit tu, RaidBuilding tb)
    {
        attackTimer = Cooldown;
        lastCombat = Time.time;
        MarkStrike();
        bool lob = Source.CannonLift.Up;
        if (!lob && Source.CannonLift.Has)
        {
            RaidBuilding block = CannonBallistics.RaidFirstBlock(Position, aim, null, tb, out _);
            if (block != null && Source.CannonLift.TryRaise())
            {
                attackTimer = 0.5f;   // поднимается - выстрел через полсекунды
                return;
            }
        }
        cannonAim = (aim - Position).sqrMagnitude > 0.001f ? (aim - Position).normalized : cannonAim;
        Source.ExpCannonAim(cannonAim);
        CannonShot s = Source.ExpCannonShot(lob);
        CannonBallistics.FireRaid(Position + cannonAim * 0.4f, aim, tu, tb, s, this, null, true);
    }

    // ---------- Катапульта ----------

    SpriteRenderer catArm, catStone;
    Vector2 catAim = Vector2.right;
    float catShotAt = -99f;
    static readonly Color CatStoneColor = new Color(0.5f, 0.48f, 0.44f);
    static readonly Color CatDustColor = new Color(0.62f, 0.55f, 0.45f, 0.6f);

    void BuildCatapultLook(float scale)
    {
        scale = Mathf.Max(0.3f, scale);
        Part("CatWheels", new Vector2(0f, -scale * 0.45f), new Vector2(scale * 1.3f, scale * 0.22f), new Color(0.22f, 0.15f, 0.08f), 11);
        Part("CatFrame", new Vector2(0f, -scale * 0.25f), new Vector2(scale * 1.2f, scale * 0.25f), new Color(0.45f, 0.3f, 0.15f), 14);
        catArm = Part("CatArm", Vector2.zero, Vector2.one, new Color(0.66f, 0.5f, 0.28f), 15);
        catStone = Part("CatStone", Vector2.zero, new Vector2(scale * 0.22f, scale * 0.22f), CatStoneColor, 16);
        UpdateCatapultLook();
    }

    void UpdateCatapultLook()
    {
        if (catArm == null || Source == null) return;
        float scale = Mathf.Max(0.3f, Source.ExpBodyScale);
        float angle = CitizenAgent.CatapultArmAngle(Time.time - catShotAt, Cooldown);
        CitizenAgent.CatapultArmPose(scale, angle, catAim.x >= 0f, out Vector3 pos, out Quaternion rot, out Vector3 size, out Vector3 tip);
        catArm.transform.localPosition = pos;
        catArm.transform.localRotation = rot;
        catArm.transform.localScale = size;
        if (catStone != null)
        {
            catStone.enabled = angle > 150f;
            catStone.transform.localPosition = tip;
        }
    }

    /// <summary>
    /// Бросок камня по точке aim (по бойцу tu или зданию tb): летит навесом через стены, бьёт по площади.
    /// Защитники в радиусе - урон (у края 60%), цель-здание - ×catapultBuildingMul, здания рядом с местом падения - половину.
    /// </summary>
    void CatapultFire(Vector2 aim, RaidUnit tu, RaidBuilding tb)
    {
        attackTimer = Cooldown;
        lastCombat = Time.time;
        MarkStrike();
        Vector2 me = Position;
        float d = Vector2.Distance(me, aim);
        if ((aim - me).sqrMagnitude > 0.001f) catAim = (aim - me).normalized;
        catShotAt = Time.time;
        Source.ExpCatapultShot(d, out float dmg, out float radius, out float bMul);
        radius = Mathf.Max(0.3f, radius);

        RaidUnit byRef = this;
        RaidBuilding tbRef = tb;
        CannonFx.Ball(me + Vector2.up * 0.25f, aim, CitizenAgent.ExpCatapultFlight(d), true, CatStoneColor, () =>
        {
            if (RaidMap.Instance == null) return;   // бой уже кончился
            RaidUnit by = byRef != null && byRef.Alive ? byRef : null;
            CannonFx.Impact(aim, ModuleKind.None);
            CombatFx.Ring(aim, radius, CatDustColor, 0.45f);
            foreach (var u in new List<RaidUnit>(All))
            {
                if (u == null || !u.Alive || u.Attacker) continue;
                float r = Vector2.Distance(u.Position, aim);
                if (r > radius + 0.3f) continue;
                // Камень падает сверху: укрытие считается от места падения
                u.TakeDamage(dmg * Mathf.Lerp(1f, 0.6f, Mathf.Clamp01(r / radius)), by, false, aim);
            }
            foreach (var b in new List<RaidBuilding>(RaidBuilding.All))
            {
                if (b == null || b.IsRuined) continue;
                bool main = b == tbRef;
                if (!main && b.DistanceTo(aim) > radius * 0.5f) continue;
                b.TakeDamage(dmg * bMul * (main ? 1f : 0.5f), by);
            }
        });
    }

    void CatapultFightUnit(RaidMap map)
    {
        Vector2 me = Position;
        Vector2 tp = target.Position;
        float d = Vector2.Distance(me, tp);
        float range = Range;
        float minR = Source != null ? Source.CatapultMinRange : 0f;
        bool hold = Mode == Order.Hold;

        // Слишком близко: цель подальше, иначе откатываемся (держит точку - ждёт)
        if (d < minR)
        {
            lastCombat = Time.time;
            RaidUnit alt = NearestOf(false, me, range, u => Vector2.Distance(u.Position, me) >= minR);
            if (alt != null) { target = alt; return; }
            if (!hold)
            {
                Vector2 away = (me - tp).sqrMagnitude > 0.001f ? (me - tp).normalized : Vector2.left;
                mover.Step(transform, map, me + away * (minR + 0.6f - d), Speed, 0.2f);
            }
            return;
        }

        if (d <= range)
        {
            mover.Clear();
            lastCombat = Time.time;
            if (attackTimer <= 0f) CatapultFire(tp, target, null);
            return;
        }

        if (hold && ReferenceEquals(target, hunt))
        {
            // Приказ «бить этого» (final-9): подъезжает на дальность выстрела
            mover.Step(transform, map, tp, Speed, range * 0.85f);
            return;
        }
        if (hold)
        {
            // Держит точку: за целью не едет
            ignoreUnit = target;
            ignoreUnitUntil = Time.time + 2f;
            target = null;
            return;
        }

        mover.Step(transform, map, tp, Speed, range * 0.8f);
        if (mover.Fails >= 3)
        {
            if (ReferenceEquals(target, hunt)) { hunt = null; Post = Position; }   // до него не добраться (final-9)
            ignoreUnit = target;
            ignoreUnitUntil = Time.time + 5f;
            target = null;
            mover.Clear();
            mover.ResetFails();
        }
    }

    SpriteRenderer Part(string n, Vector2 pos, Vector2 size, Color c, int order)
    {
        GameObject go = new GameObject(n);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(pos.x, pos.y, 0f);
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        SpriteRenderer r = go.AddComponent<SpriteRenderer>();
        r.sprite = CombatFx.Square();
        r.color = c;
        r.sortingOrder = order;
        return r;
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void OnDestroy()
    {
        Riders.Remove(this);
        // Поле боя убрали, пока носильщики ещё «в таране» - убираем и их
        foreach (var r in riders) if (r != null && r.RamHost == this) Destroy(r.gameObject);
        riders.Clear();
    }

    // ---------- Таран ----------
    // Ведущий носильщик (этот RaidUnit) несёт таран; остальные носильщики расчёта - его riders: их RaidUnit спрятаны
    // (не в All), пока таран цел. Прочность тарана - своя (ramHp), её доля пишется всем носильщикам (SetRamCondition),
    // чтобы пережить сохранение. Разбит - riders появляются рядом с теми же приказами, все дерутся как бойцы.

    /// <summary>Спрятанные носильщики (сидят «в таране»): их нет в All, но они ещё в набеге.</summary>
    public static readonly List<RaidUnit> Riders = new List<RaidUnit>();
    const float RamReach = 1.3f;

    bool ramOn;
    float ramHp, ramMax;
    int ramArmorTurn;
    readonly List<CitizenAgent> ramCrew = new List<CitizenAgent>();   // все носильщики, ведущий - первый
    readonly List<RaidUnit> riders = new List<RaidUnit>();
    SpriteRenderer ramLog, ramHead, ramMate;

    /// <summary>Это таран (цел).</summary>
    public bool IsRam => ramOn && Alive;
    /// <summary>Носильщик сидит в этом таране (спрятан).</summary>
    public RaidUnit RamHost { get; private set; }
    /// <summary>Сколько бойцов в этом RaidUnit (таран - все носильщики).</summary>
    public int Heads => IsGolem ? 0 : 1 + riders.Count;
    UnitType RamUnit => Source != null && Source.ExpUnit != null ? Source.ExpUnit : BarracksController.RamType();

    string RamCrewNames
    {
        get
        {
            var n = new List<string>();
            foreach (var c in ramCrew) if (c != null) n.Add(c.CitizenName);
            return string.Join(" и ", n);
        }
    }

    /// <summary>Собрать тараны из только что выставленных наших бойцов: носильщики одного отряда - в один таран.</summary>
    public static void FormRams(List<RaidUnit> spawned)
    {
        var bySource = new Dictionary<CitizenAgent, RaidUnit>();
        var sources = new List<CitizenAgent>();
        foreach (var u in spawned)
        {
            if (u == null || u.Source == null || !u.Source.HasRam) continue;
            bySource[u.Source] = u;
            sources.Add(u.Source);
        }
        foreach (var crew in CitizenAgent.RamCrews(sources))
        {
            RaidUnit host = bySource[crew[0]];
            var others = new List<RaidUnit>();
            for (int k = 1; k < crew.Count; k++) others.Add(bySource[crew[k]]);
            host.MountRam(others);
        }
    }

    /// <summary>Таран по известному расчёту (живой бой: как его увидел защитник).</summary>
    public static void FormRam(RaidUnit host, List<RaidUnit> others)
    {
        if (host == null || host.Source == null || !host.Source.HasRam) return;
        host.MountRam(others);
    }

    void MountRam(List<RaidUnit> others)
    {
        ramOn = true;
        ramCrew.Clear();
        ramCrew.Add(Source);
        foreach (var r in others)
        {
            if (r == null) continue;
            ramCrew.Add(r.Source);
            riders.Add(r);
            r.RamHost = this;
            r.mover.Clear();
            r.gameObject.SetActive(false);   // уходит из All: на поле - один таран
            if (!Riders.Contains(r)) Riders.Add(r);
        }
        ramMax = Mathf.Max(1f, RamUnit.ramHp);
        ramHp = Mathf.Max(1f, ramMax * (1f - Source.RamWear));
        BuildRamLook();
    }

    void SyncRam()
    {
        float f = ramMax > 0f ? Mathf.Clamp01(ramHp / ramMax) : 0f;
        foreach (var c in ramCrew) if (c != null && !c.IsDead) c.SetRamCondition(f);
    }

    void RamTakeDamage(float dmg, RaidUnit by, bool magical, Vector2? shotFrom)
    {
        float d = dmg;
        if (!magical)
        {
            if (RaidManager.Instance != null && RaidManager.Instance.InCampCover(Position)) d *= CampMarker.CoverDamage;
            Vector2? src = shotFrom.HasValue ? shotFrom : (by != null ? (Vector2?)by.Position : null);
            if (src.HasValue) d *= FieldPost.CoverFactor(Position, src.Value);
            // Броня тарана: своя + броня носильщиков (она и изнашивается)
            d *= 1f - CitizenAgent.RamArmor(RamUnit, ramCrew);
            CitizenAgent.RamWearArmor(ramCrew, ref ramArmorTurn);
        }
        if (d <= 0f) return;
        ramHp -= d;
        if (ramHp <= 0f) { BreakRam(); return; }
        SyncRam();
    }

    /// <summary>Таран разбит: носильщики выпадают и дерутся сами (в живом бою их выставит снимок защитника).</summary>
    public void BreakRam()
    {
        if (!ramOn) return;
        ramOn = false;
        ramHp = 0f;
        foreach (var c in ramCrew) if (c != null && !c.IsDead) c.BreakRam();
        ShowRamLook(false);
        CombatFx.Ring(Position, 0.8f, new Color(0.55f, 0.4f, 0.22f, 0.7f), 0.5f);
        WarManager.Notify($"Таран разбит — {RamCrewNames} дерутся ножами", false);
        target = null;
        targetBuilding = null;
        breach = false;
        if (Mode == Order.Focus && (Focus == null || Focus.IsRuined)) Mode = Order.Auto;
        if (!Puppet) ReleaseRiders();
    }

    void ReleaseRiders()
    {
        int k = 0;
        foreach (var r in riders)
        {
            if (r == null || !r.Alive) continue;
            k++;
            Vector2 p = Position + new Vector2(-0.5f * k, (k % 2 == 0 ? 0.45f : -0.45f));
            RaidMap map = RaidMap.Instance;
            if (map != null)
            {
                Vector2Int cell = map.WorldToCell(p);
                if (map.IsBlocked(cell)) p = map.CellCenter(map.NearestFree(cell));
            }
            r.Unhide(p);
            switch (Mode)
            {
                case Order.Hold: r.OrderHold(Post, marching); break;
                case Order.Focus: r.OrderFocus(Focus); break;
                case Order.Retreat: r.OrderRetreat(exitPoint); break;
                case Order.Auto: r.OrderAuto(); break;
            }
        }
        riders.Clear();
    }

    void Unhide(Vector2 p)
    {
        RamHost = null;
        Riders.Remove(this);
        transform.position = new Vector3(p.x, p.y, 0f);
        gameObject.SetActive(true);
        lastCombat = Time.time;
    }

    /// <summary>Живой бой: носильщик появился в снимке защитника - выпал из разбитого тарана.</summary>
    public void PuppetUnhide(Vector2 p)
    {
        if (RamHost == null && gameObject.activeSelf) return;
        RaidUnit host = RamHost;
        if (host != null) host.riders.Remove(this);
        Unhide(p);
    }

    /// <summary>Живой бой: снимок говорит, цел ли таран.</summary>
    public void SetNetRam(bool intact)
    {
        if (ramOn && !intact) BreakRam();
    }

    void RidersLeave()
    {
        foreach (var r in riders)
        {
            if (r == null || r.Left || r.dead) continue;
            r.RamHost = null;
            r.Left = true;
            if (RaidManager.Instance != null) RaidManager.Instance.OnAttackerLeft(r);
            Riders.Remove(r);
            Destroy(r.gameObject);
        }
        riders.Clear();
    }

    void BuildRamLook()
    {
        float scale = Source != null ? Mathf.Max(0.3f, Source.ExpBodyScale) : 0.5f;
        if (ramLog == null)
        {
            ramMate = Part("RamMate", new Vector2(-scale * 0.85f, scale * 0.05f), new Vector2(scale * 0.85f, scale * 0.85f), Source != null ? Source.ExpTint : Color.gray, 12);
            if (body != null && body.sprite != null) { ramMate.sprite = body.sprite; ramMate.color = body.color; }
            ramLog = Part("RamLog", new Vector2(-scale * 0.2f, -scale * 0.12f), new Vector2(scale * 2.1f, scale * 0.3f), new Color(0.47f, 0.31f, 0.16f), 16);
            ramHead = Part("RamHead", new Vector2(scale * 0.95f, -scale * 0.12f), new Vector2(scale * 0.34f, scale * 0.46f), new Color(0.27f, 0.27f, 0.3f), 17);
            GameArt.DressRam(ramLog, ramHead, scale);   // нарисованный таран вместо двух квадратиков
            allParts = null;
        }
        ShowRamLook(true);
    }

    void ShowRamLook(bool on)
    {
        if (ramLog != null) ramLog.enabled = on;
        if (ramHead != null) ramHead.enabled = on;
        if (ramMate != null) ramMate.enabled = on && (riders.Count > 0 || netRamMate);
    }

    // Живой бой, у нападающего: таран защитника (только картинка по снимкам, бой считает его игра)
    bool netRamLook, netRamMate;

    public void SetNetDefenderRam(bool ram)
    {
        if (!Puppet || Attacker) return;
        if (ram == netRamLook) return;
        netRamLook = ram;
        netRamMate = ram;   // на картинке - двое носильщиков (сколько их на самом деле, снимок не говорит)
        if (ram) BuildRamLook();
        else ShowRamLook(false);
    }

    // Мозги тарана: людей не трогает. Штурм - ближайшая стена / ворота / заслон, если их нет - здание;
    // «Ломать здание» - его; «Держать точку» - идёт к точке и ломает стену, что встала на пути.
    void RamThink()
    {
        target = null;
        Vector2 me = Position;
        bool valid = targetBuilding != null && !targetBuilding.IsRuined;
        switch (Mode)
        {
            case Order.Hold:
                if (valid && targetBuilding.DistanceTo(me) <= 2.5f) return;
                targetBuilding = null;
                if (Vector2.Distance(me, Post) <= HoldTravel)
                {
                    RaidBuilding near = RamBarrier(me, 4f);
                    targetBuilding = near != null ? near : WayBuilding(me);
                }
                else targetBuilding = RamBarrier(me, WayBuildingReach + 0.5f);
                return;

            case Order.Focus:
                if (Focus == null || Focus.IsRuined) { Mode = Order.Auto; Focus = null; goto case Order.Auto; }
                if (breach && valid && targetBuilding.IsBarrier) return;
                breach = false;
                targetBuilding = Focus;
                return;

            case Order.Auto:
                if (valid) return;
                breach = false;
                targetBuilding = RamBarrier(me, 30f);
                if (targetBuilding == null) targetBuilding = PickBuilding(me);
                return;

            default:
                targetBuilding = null;
                return;
        }
    }

    RaidBuilding RamBarrier(Vector2 me, float within)
    {
        RaidBuilding best = null;
        float bestD = within;
        foreach (var b in RaidBuilding.All)
        {
            if (b == null || b.IsRuined || !b.IsBarrier) continue;
            if (b == ignoreBuilding && Time.time < ignoreBuildingUntil) continue;
            float d = b.DistanceTo(me);
            if (d < bestD) { bestD = d; best = b; }
        }
        return best;
    }

    // ---------- Приказы (налётчики) ----------

    public void OrderAuto()
    {
        if (!Attacker || Mode == Order.Retreat) return;
        marching = false;
        hunt = null;
        Mode = Order.Auto;
        Focus = null;
        targetBuilding = null;
        breach = false;
        mover.Clear();
    }

    /// <summary>Идти к точке и держать её. march - марш (двойной клик): по дороге ни на что не отвлекаться.</summary>
    public void OrderHold(Vector2 point, bool march = false)
    {
        if (!Attacker || Mode == Order.Retreat) return;
        Mode = Order.Hold;
        marching = march;
        hunt = null;
        holdFrom = Position;
        healTarget = null;
        Post = point + Random.insideUnitCircle * 0.8f;
        Focus = null;
        target = null;
        targetBuilding = null;
        breach = false;
        mover.Clear();
        mover.ResetFails();
    }

    public void OrderFocus(RaidBuilding b)
    {
        if (!Attacker || Mode == Order.Retreat || b == null || b.IsRuined) return;
        marching = false;
        hunt = null;
        Mode = Order.Focus;
        Focus = b;
        target = null;
        targetBuilding = b;
        breach = false;
        mover.Clear();
        mover.ResetFails();
    }

    /// <summary>Бить этого защитника (нажали по нему, final-9): идут за ним и бьют, пока он жив; потом держат то место.</summary>
    public void OrderAttack(RaidUnit enemy)
    {
        if (!Attacker || Mode == Order.Retreat || !Alive2(enemy) || enemy.Attacker) return;
        if (ramOn) { OrderHold(enemy.Position); return; }   // таран людей не бьёт
        Mode = Order.Hold;
        marching = false;
        healTarget = null;
        hunt = enemy;
        huntLast = enemy.Position;
        holdFrom = Position;
        Post = enemy.Position;
        target = enemy;
        Focus = null;
        targetBuilding = null;
        breach = false;
        restingAtPost = false;
        mover.Clear();
        mover.ResetFails();
    }

    public void OrderRetreat(Vector2 exit)
    {
        if (!Attacker) return;
        marching = false;
        hunt = null;
        Mode = Order.Retreat;
        pushingPost = null;   // бросает баррикаду
        exitPoint = exit;
        target = null;
        targetBuilding = null;
        mover.Clear();
        mover.ResetFails();
    }

    // ---------- Маршрут на карте (RouteMarks): куда наш налётчик идёт по приказу ----------

    // Живой бой: приказ ушёл защитнику, сами мы бойцом не управляем - запоминаем, куда послали
    bool routeOn;
    Vector2 routePoint;
    RaidBuilding routeBuilding;
    RaidUnit routeUnit;
    int routeKind;

    /// <summary>Живой бой: послали к точке (kind 1 - идти, 3 - отступать).</summary>
    public void NoteRoute(Vector2 point, int kind = 1) { routeOn = true; routePoint = point; routeBuilding = null; routeUnit = null; routeKind = kind; }
    /// <summary>Живой бой: послали ломать здание.</summary>
    public void NoteRoute(RaidBuilding b) { routeOn = b != null; routeBuilding = b; routeUnit = null; routeKind = 2; }
    /// <summary>Живой бой: послали бить бойца защитника.</summary>
    public void NoteRoute(RaidUnit enemy) { routeOn = enemy != null; routeUnit = enemy; routeBuilding = null; routeKind = 2; }
    public void ClearRoute() { routeOn = false; routeBuilding = null; routeUnit = null; }

    /// <summary>
    /// Куда идёт наш налётчик по приказу игрока: 0 - никуда (дошёл, штурмует сам, защитник), 1 - к точке,
    /// 2 - ломать здание / бить врага, 3 - отступает. radius - размер маркера цели.
    /// </summary>
    public int RouteGoal(out Vector2 to, out float radius)
    {
        to = Vector2.zero;
        radius = 0.42f;
        if (!Attacker || !Alive) return 0;
        if (Puppet)
        {
            if (!routeOn) return 0;
            if (routeBuilding != null)
            {
                if (routeBuilding.IsRuined || routeBuilding.DistanceTo(Position) < 1.2f) { ClearRoute(); return 0; }
                to = routeBuilding.Center;
                radius = Mathf.Max(routeBuilding.SizeCells.x, routeBuilding.SizeCells.y) * 0.5f;
            }
            else if (routeUnit != null)
            {
                if (!routeUnit.Alive || Vector2.Distance(routeUnit.Position, Position) < 1.5f) { ClearRoute(); return 0; }
                to = routeUnit.Position;
            }
            else
            {
                if (Vector2.Distance(routePoint, Position) < MarchArrive) { ClearRoute(); return 0; }
                to = routePoint;
            }
            return routeKind;
        }
        switch (Mode)
        {
            case Order.Hold:
                if (Vector2.Distance(Post, Position) < MarchArrive) return 0;
                to = Post;
                return 1;
            case Order.Focus:
                if (Focus == null || Focus.IsRuined || Focus.DistanceTo(Position) <= Range + 0.6f) return 0;
                to = Focus.Center;
                radius = Mathf.Max(Focus.SizeCells.x, Focus.SizeCells.y) * 0.5f;
                return 2;
            case Order.Retreat:
                if (Vector2.Distance(exitPoint, Position) < 1f) return 0;
                to = exitPoint;
                return 3;
        }
        return 0;
    }

    // Защитники поднимаются по тревоге
    public void Alert() { if (!Attacker) alerted = true; }

    /// <summary>Командир обороны: стоять у anchor, гоняться за врагом не дальше leash от неё, замечать за look.</summary>
    public void Command(Vector2 anchor, float leash, float look)
    {
        if (Attacker) return;
        if (!commanded || Vector2.Distance(anchor, Post) > 0.6f) hasPatrol = false;
        commanded = true;
        Post = anchor;
        cmdLeash = Mathf.Max(0.5f, leash);
        cmdLook = look;
    }

    /// <summary>Командир указал, кого бить.</summary>
    public void CommandTarget(RaidUnit u)
    {
        if (Attacker || !Alive2(u)) return;
        target = u;
        mover.Clear();
    }

    // ---------- Урон ----------

    public void TakeDamage(float dmg, RaidUnit by, bool magical, Vector2? shotFrom = null)
    {
        if (!Alive || dmg <= 0f) return;
        if (Attacker && RaidManager.Instance != null && RaidManager.Instance.ScoutSafe) return;   // штурм ещё не начат - наших не бьют (final-6)
        lastCombat = Time.time;

        if (IsGolem)
        {
            float d = dmg;
            if (!magical && RaidManager.Instance != null && RaidManager.Instance.InCampCover(Position)) d *= CampMarker.CoverDamage;
            if (!magical)
            {
                Vector2? src = shotFrom.HasValue ? shotFrom : (by != null ? (Vector2?)by.Position : null);
                if (src.HasValue) d *= FieldPost.CoverFactor(Position, src.Value);
            }
            if (d <= 0f) return;
            hp -= d;
            if (hp <= 0f) { hp = 0f; Die(by); return; }
            if (Mode != Order.Retreat && !IsMarching && !Alive2(target) && Alive2(by)) target = by;
            return;
        }

        if (ramOn) { RamTakeDamage(dmg, by, magical, shotFrom); return; }

        if (Attacker)
        {
            if (ChaosReflect(dmg, by, magical, shotFrom)) return;   // щит с ядром хаоса отбил снаряд обратно
            float d = ShieldBearer.Mitigate(Source, dmg, null, magical);
            if (!magical && RaidManager.Instance != null && RaidManager.Instance.InCampCover(Position)) d *= CampMarker.CoverDamage;
            if (!magical)
            {
                // Заслон / пункт режет урон, если стрелок или бьющий - по ту сторону стенки
                Vector2? src = shotFrom.HasValue ? shotFrom : (by != null ? (Vector2?)by.Position : null);
                if (src.HasValue) d *= FieldPost.CoverFactor(Position, src.Value);
            }
            if (d > 0f) Source.TakeDamage(d, null);
            if (Source == null || Source.IsDead) Die(by);
            else if (Mode != Order.Retreat && !IsMarching && !Alive2(target) && Alive2(by)) target = by;
            return;
        }

        if (!magical)
        {
            // За заслоном или стеной - укрытие от тех, кто снаружи
            Vector2? from = shotFrom.HasValue ? shotFrom : (by != null ? (Vector2?)by.Position : null);
            if (from.HasValue) dmg *= RaidBuilding.CoverFor(Position, from.Value);
            dmg = MitigateByShield(dmg, by);
        }
        if (dmg <= 0f) return;
        hp -= dmg;
        alerted = true;
        if (RaidManager.Instance != null) RaidManager.Instance.Alarm(Position, 12f);
        if (hp <= 0f) { Die(by); return; }
        if (!Alive2(target) && Alive2(by)) target = by;
    }

    /// <summary>
    /// Наш боец со щитом с ядром хаоса: снаряд издалека (стрела, заряд, ядро) с шансом улетает обратно -
    /// в стрелка, а если стреляло здание (башня, пушка) - в здание. true - отражён, урона нет.
    /// </summary>
    bool ChaosReflect(float dmg, RaidUnit by, bool magical, Vector2? shotFrom)
    {
        if (!FromSource || Source == null || dmg <= 0f) return false;
        Shield s = ShieldBearer.ShieldOf(Source);
        if (s == null || !s.HasChaos) return false;
        Vector2? src = Alive2(by) ? (Vector2?)by.Position : shotFrom;
        if (!src.HasValue || Vector2.Distance(Position, src.Value) < Shield.ChaosMinDistance) return false;   // вплотную - не снаряд

        RaidBuilding tower = null;
        if (!Alive2(by))
        {
            foreach (var b in RaidBuilding.All)
                if (b != null && !b.IsRuined && b.DistanceTo(src.Value) < 0.8f) { tower = b; break; }
            if (tower == null) return false;   // непонятно, откуда прилетело (огонь, яд) - не отражаем
        }
        if (!s.TryChaosReflect()) return false;

        CombatFx.ChaosReflect(Position, src.Value);
        lastCombat = Time.time;
        if (tower != null) tower.TakeDamage(dmg, this);
        else by.TakeDamage(dmg, this, magical, Position);
        return true;
    }

    float MitigateByShield(float dmg, RaidUnit by)
    {
        if (shield == null) return dmg;
        if (Random.value < shield.ReflectChanceVs(dmg))
        {
            if (by != null) CombatFx.Arrow(Position, by.Position);
            if (shield.Wear()) shield = null;
            return 0f;
        }
        if (Random.value < shield.BlockChance)
        {
            dmg *= 1f - shield.ReduceFraction;
            if (shield.Wear()) shield = null;
        }
        return dmg;
    }

    public void Heal(float amount)
    {
        if (!Alive || amount <= 0f || ramOn) return;   // таран лечением не чинят
        if (FromSource) Source.ReceiveHeal(amount);
        else hp = Mathf.Min(maxHp, hp + amount);
    }

    void Die(RaidUnit by)
    {
        if (dead) return;
        if (riders.Count > 0 && !Puppet) { ramOn = false; ShowRamLook(false); ReleaseRiders(); }   // ведущий пал - остальные выпадают
        dead = true;
        CombatFx.Death(Position);
        RaidManager rm = RaidManager.Instance;
        if (rm != null)
        {
            if (Attacker) rm.OnAttackerKilled(this);
            else rm.OnDefenderKilled(this, by, weapon, shield);
        }
        Destroy(gameObject);
    }

    // Налётчик ушёл с карты (домой)
    void Leave()
    {
        if (Left || dead) return;
        Left = true;
        if (RaidManager.Instance != null) RaidManager.Instance.OnAttackerLeft(this);
        RidersLeave();   // носильщики уходят вместе с тараном
        Destroy(gameObject);
    }

    static bool Alive2(RaidUnit u) { return u != null && u.Alive; }

    static void Poison(RaidUnit u, float dps, float dur)
    {
        if (dps <= 0f || !Alive2(u)) return;
        PoisonEffect.Apply(u, dps, dur, d => { if (Alive2(u)) u.TakeDamage(d, null, true); });
    }

    static void Slow(RaidUnit u, float factor, float dur)
    {
        if (dur <= 0f || factor >= 1f || !Alive2(u)) return;
        SlowEffect.Apply(u, factor, dur);
    }

    // Поджигатель в набеге: ведро горящей смолы по защитникам
    static void FireSplash(RaidUnit by, Vector2 at, UnitType ut, float dmg)
    {
        if (ut == null) return;
        // Поджигатель - ведро смолы; Маг стихий - огненный шар (у него своё поле радиуса)
        bool fireball = ut.fireRadius <= 0f && ut.fireballRadius > 0f;
        float radius = fireball ? ut.fireballRadius : ut.fireRadius, dps = ut.burnDps * (fireball ? ManaManager.Factor : 1f), dur = ut.burnDuration;
        System.Action land = () =>
        {
            foreach (var u in new List<RaidUnit>(All))
            {
                if (u == null || u.Attacker || !u.Alive || Vector2.Distance(u.Position, at) > radius) continue;
                u.TakeDamage(dmg, by, true);
                if (!Alive2(u)) continue;
                RaidUnit victim = u;
                BurnEffect.Apply(u, dps, dur, d => { if (Alive2(victim)) victim.TakeDamage(d, null, true); });
            }
        };
        if (fireball) CombatFx.Fireball(by != null ? by.Position : at, at, radius, land);
        else CombatFx.Bucket(by != null ? by.Position : at, at, radius, land);
    }

    // ---------- Поиск ----------

    /// <summary>Ближайший живой боец стороны attackers в радиусе (для башен и ИИ).</summary>
    public static RaidUnit NearestOf(bool attackers, Vector2 from, float radius, System.Func<RaidUnit, bool> filter = null)
    {
        RaidUnit best = null;
        float bestD = radius;
        foreach (var u in All)
        {
            if (u == null || u.Attacker != attackers || !u.Alive) continue;
            float d = Vector2.Distance(from, u.Position);
            if (d > bestD) continue;
            if (filter != null && !filter(u)) continue;
            bestD = d;
            best = u;
        }
        return best;
    }

    // ---------- Мозги ----------

    void Update()
    {
        if (dead || Left) return;
        RaidMap map = RaidMap.Instance;
        RaidManager rm = RaidManager.Instance;
        if (map == null || rm == null) return;

        if (FromSource && (Source == null || Source.IsDead)) { Die(null); return; }
        if (IsGolem && !LifeAltar.RaidBound) { Die(null); return; }   // алтарь дома потерян - голем рассыпается

        float dt = Time.deltaTime;
        // Обновление 14: голем сам не лечится (травы усиливают его взрыв-лечение при гибели)
        attackTimer -= dt;

        if (Puppet) PuppetTick(dt);
        else if (Attacker) AttackerTick(map, rm, dt);
        else DefenderTick(map, rm, dt);

        if (sack != null) sack.enabled = !Carried.IsEmpty;
        if (cannonBarrel != null) UpdateCannonLook();
        if (catArm != null) UpdateCatapultLook();
        if (gearArmor != null) UpdateGearLook();
    }

    void AttackerTick(RaidMap map, RaidManager rm, float dt)
    {
        if (Mode == Order.Retreat)
        {
            // Отступают, не ввязываясь в бой (кто не успел - того догонят)
            bool arrived = mover.Step(transform, map, exitPoint, Speed, 0.5f);
            Vector2 local = RaidMap.FrameOf(Position);   // край высадки - «запад плана» (сторона высадки, final-1)
            if (arrived || local.x < -RaidMap.Size / 2f + 1.2f || mover.Fails >= 6) Leave();
            return;
        }

        // Толкает колёсную баррикаду (FieldPost): пока она не доехала, другим не занят
        if (pushingPost != null)
        {
            if (pushingPost.TickRaidPusher(this)) return;
            pushingPost = null;
        }

        if (!rm.BattleOn) return;   // разведка: стоят у лагеря и ждут приказа
        if (Mode == Order.Idle) Mode = Order.Auto;

        // Марш: идут к точке, ни на кого не отвлекаясь; дошли - держат её как обычно
        if (marching && (Mode != Order.Hold || Vector2.Distance(Position, Post) <= MarchArrive)) marching = false;
        if (IsMarching)
        {
            target = null;
            targetBuilding = null;
            restingAtPost = false;
            if (mover.Step(transform, map, Post, Speed, 0.3f) || mover.Fails >= 6) { marching = false; mover.ResetFails(); }
            return;
        }

        thinkTimer -= dt;
        if (thinkTimer <= 0f)
        {
            thinkTimer = 0.3f;
            AttackerThink();
        }

        if (HealPerSecond > 0f && HealAllies(map, dt)) return;
        if (Alive2(target)) { FightUnit(map); return; }
        if (Mode != Order.Auto || ramOn) restingAtPost = false;
        else if (PostRestTick(map)) return;
        if (targetBuilding != null && !targetBuilding.IsRuined) { FightBuilding(map); return; }

        if (Mode == Order.Hold) mover.Step(transform, map, Post, Speed, 0.3f);
    }

    // Раненый боец на штурме (меньше SeekBelow здоровья) сам идёт отдыхать на ближайший пункт с припасами,
    // сидит за стенкой до его предела лечения, потом возвращается в бой. true - занят отдыхом.
    // Поиск пункта и угрозы - не каждый кадр (в большом бою это перебор всех бойцов на каждого раненого)
    float restThink;
    FieldPost restPost;
    Vector2 restSpot;

    bool PostRestTick(RaidMap map)
    {
        if (InCombat) { restingAtPost = false; return false; }
        float f = HpFraction;
        if (!restingAtPost && f >= FieldPost.SeekBelow) return false;

        restThink -= Time.deltaTime;
        if (restThink <= 0f || restPost == null || !restPost.CanHeal)
        {
            restThink = 0.4f;
            restPost = FieldPost.FindHealPost(Position, true, 18f);
            if (restPost == null) { restingAtPost = false; return false; }
            if (!restingAtPost)
            {
                if (NearestOf(false, Position, 5f) != null) return false;
                restingAtPost = true;
            }
            RaidUnit threat = NearestOf(false, restPost.Position, 30f);
            float lateral = ((GetHashCode() & 3) - 1.5f) * 0.5f;
            restSpot = restPost.RestSpot(Position, threat != null ? (Vector2?)threat.Position : null, lateral);
        }
        if (!restingAtPost) return false;
        if (f >= restPost.HealCap - 0.02f) { restingAtPost = false; return false; }

        if (Vector2.Distance(Position, restSpot) > 0.7f) mover.Step(transform, map, restSpot, Speed, 0.5f);
        else mover.Clear();
        return true;
    }

    void AttackerThink()
    {
        if (ramOn) { RamThink(); return; }
        Vector2 me = Position;

        // «Бить этого»: цель одна, пока жива (final-9). Убили - держат место, где он был
        if (hunt != null)
        {
            if (Alive2(hunt) && (FogOfWar.IsVisible(hunt.Position) || Vector2.Distance(hunt.Position, me) <= Aggro))
            {
                huntLast = hunt.Position;
                Post = huntLast;
                target = hunt;
                targetBuilding = null;
                return;
            }
            hunt = null;
            Post = huntLast;
            holdFrom = me;
        }

        float aggro = Aggro;
        bool hold = Mode == Order.Hold;
        bool focus = Mode == Order.Focus && Focus != null && !Focus.IsRuined;
        Vector2 post = Post;
        // Клик по земле: пока идут к точке - бьют тех, кто встретится по дороге (рядом с линией пути)
        bool onTheWay = hold && !IsCatapult && Vector2.Distance(me, post) > HoldTravel;
        Vector2 from = holdFrom;
        bool NearWay(Vector2 p, float extra) => SegDist(p, from, post) <= WayCorridor + extra || Vector2.Distance(p, me) <= 2.2f;

        if (Alive2(target) && Vector2.Distance(target.Position, me) <= aggro * 1.6f
            && (!focus || Vector2.Distance(target.Position, me) <= FocusDefend + 1.5f)
            && (!hold || (onTheWay ? NearWay(target.Position, 3f) : (IsCatapult ? Vector2.Distance(target.Position, me) <= Range : Vector2.Distance(target.Position, post) <= 9f)))) return;

        bool cat = IsCatapult;
        float minR = cat ? Source.CatapultMinRange : 0f;
        float range = Range;
        target = NearestOf(false, me, focus ? Mathf.Min(aggro, FocusDefend) : aggro, u =>
            (u != ignoreUnit || Time.time > ignoreUnitUntil)
            && (!cat || Vector2.Distance(u.Position, me) >= minR)
            && (!hold || (onTheWay ? NearWay(u.Position, 0f) : (cat ? Vector2.Distance(u.Position, me) <= range : Vector2.Distance(u.Position, post) <= 9f))));
        if (target != null) return;

        if (onTheWay)
        {
            // Здание у самой дороги (башня - чуть дальше) - сначала его
            if (targetBuilding != null && !targetBuilding.IsRuined && targetBuilding.DistanceTo(me) <= WayBuildingReach + 1f) return;
            targetBuilding = WayBuilding(me);
            return;
        }

        switch (Mode)
        {
            case Order.Focus:
                if (Focus == null || Focus.IsRuined) { Mode = Order.Auto; Focus = null; goto case Order.Auto; }
                if (breach && (targetBuilding == null || targetBuilding.IsRuined || !targetBuilding.IsBarrier)) breach = false;
                if (!breach) targetBuilding = Focus;
                break;

            case Order.Auto:
                if (targetBuilding != null && !targetBuilding.IsRuined) break;
                if (breach)
                {
                    targetBuilding = NearestBarrier(me);
                    if (targetBuilding == null) breach = false;
                    break;
                }
                targetBuilding = PickBuilding(me);
                break;

            default:
                targetBuilding = null;
                break;
        }
    }

    // Расстояние от точки до отрезка a-b
    static float SegDist(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float l2 = ab.sqrMagnitude;
        if (l2 < 0.0001f) return Vector2.Distance(p, a);
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
        return Vector2.Distance(p, a + ab * t);
    }

    RaidBuilding PickBuilding(Vector2 me)
    {
        RaidBuilding best = null;
        float bestScore = float.MaxValue;
        foreach (var b in RaidBuilding.All)
        {
            if (b == null || b.IsRuined || b.IsBarrier) continue;
            if (b == ignoreBuilding && Time.time < ignoreBuildingUntil) continue;
            float d = b.DistanceTo(me);
            float score = d;
            if (b.IsDefense && d < 10f) score -= 5f;          // башни поблизости - в первую очередь
            if ((IsCannon || IsCatapult) && b.IsDefense && d < 14f) score -= 4f;   // полевая пушка охотится за башнями и пушками
            if (!b.Storage.IsEmpty) score -= 1.5f;            // где есть чем поживиться
            if (score < bestScore) { bestScore = score; best = b; }
        }
        return best;
    }

    // Здание у дороги (клик «с боем по пути»): ближайшее не-стена вплотную, башня или пушка - чуть дальше
    RaidBuilding WayBuilding(Vector2 me)
    {
        RaidBuilding best = null;
        float bestD = float.MaxValue;
        foreach (var b in RaidBuilding.All)
        {
            if (b == null || b.IsRuined || b.IsBarrier) continue;
            if (b == ignoreBuilding && Time.time < ignoreBuildingUntil) continue;
            float d = b.DistanceTo(me);
            float reach = b.IsDefense ? Mathf.Max(WayBuildingReach, Range + 1f) : WayBuildingReach;
            if (d > reach || d >= bestD) continue;
            bestD = d;
            best = b;
        }
        return best;
    }

    static RaidBuilding NearestBarrier(Vector2 me)
    {
        RaidBuilding best = null;
        float bestD = float.MaxValue;
        foreach (var b in RaidBuilding.All)
        {
            if (b == null || b.IsRuined || !b.IsBarrier) continue;
            float d = b.DistanceTo(me);
            if (b.Kind == RaidBuildingKind.Gate) d += 1.5f;   // ворота крепче стены
            if (d < bestD) { bestD = d; best = b; }
        }
        return best;
    }

    void DefenderTick(RaidMap map, RaidManager rm, float dt)
    {
        if (!rm.BattleOn)
        {
            // До боя: по приказу (тревога) - на позицию, иначе - бродят у поста
            if (commanded && Vector2.Distance(Position, Post) > 1.5f) { mover.Step(transform, map, Post, Speed, 0.3f); return; }
            Patrol(map, dt, commanded ? 1f : 1.5f);
            return;
        }

        thinkTimer -= dt;
        if (thinkTimer <= 0f)
        {
            thinkTimer = 0.35f;
            DefenderThink();
        }

        if (HealPerSecond > 0f && HealAllies(map, dt)) return;
        if (Alive2(target)) { FightUnit(map); return; }

        // Врагов рядом нет - на пост (по приказу командира - точно на точку)
        if (Vector2.Distance(Position, Post) > (commanded ? 1f : 2.5f)) mover.Step(transform, map, Post, Speed, 0.3f);
        else Patrol(map, dt, commanded ? 0.8f : 1.5f);
    }

    void DefenderThink()
    {
        Vector2 me = Position;
        float leash = commanded ? cmdLeash : 14f;
        float look = commanded && cmdLook > 0f ? cmdLook : (alerted ? 12f : 6f);

        // Цель в плаще отошла дальше, чем её можно разглядеть, - потеряли
        if (Alive2(target) && target.IsConcealed && Vector2.Distance(target.Position, me) > target.NoticeRange(look) * 1.6f) target = null;
        if (Alive2(target) && Vector2.Distance(target.Position, Post) <= leash + 2f) return;
        target = NearestNoticed(me, look, u =>
            Vector2.Distance(u.Position, Post) <= leash && (u != ignoreUnit || Time.time > ignoreUnitUntil));
        if (target != null && !alerted)
        {
            alerted = true;
            if (RaidManager.Instance != null) RaidManager.Instance.Alarm(me, 10f);
        }
    }

    void Patrol(RaidMap map, float dt, float radius)
    {
        if (patrolWait > 0f) { patrolWait -= dt; return; }
        if (!hasPatrol)
        {
            if (!map.TryRandomWalkable(Post, radius, out patrolPoint)) { patrolWait = 2f; return; }
            hasPatrol = true;
            mover.Clear();
            mover.ResetFails();
        }
        if (mover.Step(transform, map, patrolPoint, Speed * 0.5f, 0.1f) || mover.Fails >= 3)
        {
            hasPatrol = false;
            patrolWait = Random.Range(2f, 5f);
        }
    }

    // ---------- Бой ----------

    void Strike(Vector2 at)
    {
        if (IsHealerUnit) CombatFx.MageBolt(Position, at);   // полевой маг - магическим зарядом
        else if (Ranged) CombatFx.Arrow(Position, at);
        else CombatFx.Flash(at);
    }

    void FightUnit(RaidMap map)
    {
        if (IsCatapult) { CatapultFightUnit(map); return; }   // навесом по площади

        Vector2 me = Position;
        Vector2 tp = target.Position;
        float range = Range;

        if (Vector2.Distance(me, tp) <= range)
        {
            mover.Clear();
            lastCombat = Time.time;
            if (facing != null) facing.LookAt(tp);
            if (attackTimer <= 0f && IsCannon) { CannonFire(tp, target, null); return; }
            // Маг стихий с ядром хаоса: иногда вместо удара - чёрная дыра (ChaosMagic)
            if (attackTimer <= 0f && Attacker && Source != null && ChaosMagic.TryCastRaid(this, Source, tp)) { attackTimer = Cooldown; MarkStrike(); return; }
            if (attackTimer <= 0f)
            {
                attackTimer = Cooldown;
                MarkStrike();
                float dmg = StrikeDamage();
                float aoe = Attacker && Source != null ? Source.ExpAoeRadius : 0f;
                float pDps = Attacker && Source != null ? Source.ExpLastPoisonDps : 0f;
                float pDur = Attacker && Source != null ? Source.ExpLastPoisonDuration : 0f;
                float sF = Attacker && Source != null ? Source.ExpLastSlowFactor : 1f;
                float sDur = Attacker && Source != null ? Source.ExpLastSlowDuration : 0f;

                if (Attacker && Source != null && Source.ExpLastFire)
                {
                    FireSplash(this, tp, Source.ExpUnit, dmg);
                }
                else if (aoe > 0f)
                {
                    // Ядовитый снаряд по площади (Маг-отравитель)
                    RaidUnit by = this;
                    CombatFx.Shell(Position, tp, 0.55f, aoe, () =>
                    {
                        foreach (var u in new List<RaidUnit>(All))
                        {
                            if (u == null || u.Attacker || !u.Alive || Vector2.Distance(u.Position, tp) > aoe) continue;
                            u.TakeDamage(dmg, by, true);
                            Poison(u, pDps, pDur);
                            Slow(u, sF, sDur);
                        }
                    });
                }
                else
                {
                    Strike(tp);
                    RaidUnit t = target;
                    t.TakeDamage(dmg, this, false);
                    Poison(t, pDps, pDur);
                    Slow(t, sF, sDur);
                    if (Attacker && Source != null) Pyrotechnics.BowSaluteRaid(this, Source.EquippedWeapon, t, dmg);   // лук с салютом
                }
            }
            return;
        }

        mover.Step(transform, map, tp, Speed, range * 0.8f);
        if (mover.Fails >= 3)
        {
            if (ReferenceEquals(target, hunt)) { hunt = null; Post = Position; }   // до него не добраться (final-9)
            ignoreUnit = target;
            ignoreUnitUntil = Time.time + 5f;
            target = null;
            mover.Clear();
            mover.ResetFails();
            if (Attacker && Mode == Order.Auto) breach = true;   // за стеной - надо пробивать
        }
    }

    void FightBuilding(RaidMap map)
    {
        RaidBuilding b = targetBuilding;
        Vector2 me = Position;

        if (b.DistanceTo(me) <= Range + 0.15f)
        {
            mover.Clear();
            if (facing != null) facing.LookAt(b.Center);
            if (attackTimer <= 0f && IsCannon) { CannonFire(b.Center, null, b); return; }
            if (attackTimer <= 0f && IsCatapult) { CatapultFire(b.Center, null, b); return; }
            if (attackTimer <= 0f)
            {
                attackTimer = Cooldown;
                MarkStrike();
                Strike(b.ClosestPoint(me));
                float mul = Ranged ? 0.4f : 1f;   // стрелами здания ломать долго
                if (ramOn)
                {
                    mul = b.IsBarrier ? RamUnit.ramBarrierMul : RamUnit.ramBuildingMul;
                    CombatFx.Ring(b.ClosestPoint(me), 0.5f, new Color(0.62f, 0.55f, 0.45f, 0.6f), 0.3f);   // пыль от удара
                }
                b.TakeDamage(StrikeDamage() * mul, this);
            }
            return;
        }

        approachTimer -= Time.deltaTime;
        if (approachTimer <= 0f)
        {
            approachTimer = 1.5f;
            approach = map.ApproachPoint(b, me);
        }
        mover.Step(transform, map, approach, Speed, 0.2f);

        if (mover.Fails >= 3)
        {
            // Не подойти: за стеной - пробиваем ближайшую стену, иначе бросаем это здание на время
            mover.Clear();
            mover.ResetFails();
            if (!b.IsBarrier && Mode != Order.Hold) breach = true;   // идут с боем к точке - стены не пробивают, просто дальше
            else { ignoreBuilding = b; ignoreBuildingUntil = Time.time + 10f; }
            if (b.IsBarrier && Mode == Order.Focus && Focus == b) { Mode = Order.Auto; Focus = null; }
            targetBuilding = breach ? NearestBarrier(me) : null;
        }
    }

    // Лечение своих (наш полевой маг / маг защитников). true - занят лечением.
    float healScanAt;

    bool HealAllies(RaidMap map, float dt)
    {
        if (Hp < MaxHp) Heal(HealPerSecond * 0.5f * dt);

        bool lost = healTarget == null || !healTarget.Alive || healTarget.Hp >= healTarget.MaxHp * 0.98f;
        if (lost) healTarget = null;
        healScanAt -= dt;
        // final-13: раненых ищем раз в 0,3 с, а не каждый кадр (отряд магов перебирал всё поле боя 60 раз в секунду)
        if (lost && healScanAt <= 0f)
        {
            healScanAt = 0.3f;
            float best = float.MaxValue;
            foreach (var u in All)
            {
                if (u == null || u == this || u.Attacker != Attacker || !u.Alive || u.ramOn) continue;
                if (u.Hp >= u.MaxHp * 0.9f) continue;
                float d = Vector2.Distance(Position, u.Position);
                if (d > Aggro + 3f) continue;
                float score = d + u.HpFraction * 12f + MageHeal.Recent(u) * 4f;   // final-13: сначала тех, кого не лечат
                if (score < best) { best = score; healTarget = u; }
            }
        }
        if (healTarget == null) return false;

        Vector2 tp = healTarget.Position;
        if (Vector2.Distance(Position, tp) > HealRange)
        {
            mover.Step(transform, map, tp, Speed, HealRange * 0.75f);
            if (mover.Fails >= 4) { healTarget = null; mover.Clear(); mover.ResetFails(); }
            return true;
        }

        mover.Clear();
        if (facing != null) facing.LookAt(tp);
        // Лечение летит снарядом: раз в HealPulse секунд светящийся шар, лечит, когда долетит
        healFx -= dt;
        if (healFx <= 0f)
        {
            healFx = HealPulse;
            float amount = HealPerSecond * HealPulse;
            RaidUnit patient = healTarget;
            CombatFx.HealOrb(Position, patient.transform, () => { if (Alive2(patient)) patient.Heal(MageHeal.Apply(patient, amount)); });   // final-13: усталость от лечения
            if (artFigure != null) artFigure.Lunge();
        }
        return true;
    }

    // ---------- Тексты ----------

    public string StatusText
    {
        get
        {
            if (!Alive) return "выбыл";
            if (Mode == Order.Retreat) return "отступает";
            if (ramOn && targetBuilding != null && !targetBuilding.IsRuined) return "таран бьёт: " + targetBuilding.Title;
            if (restingAtPost) return "отдыхает на пункте";
            if (IsMarching) return "идёт маршем";
            if (InCombat) return "в бою";
            if (targetBuilding != null && !targetBuilding.IsRuined) return breach ? "пробивает стену" : "ломает: " + targetBuilding.Title;
            switch (Mode)
            {
                case Order.Hold: return Vector2.Distance(Position, Post) > HoldTravel ? "идёт с боем" : "держит точку";
                case Order.Focus: return "идёт ломать: " + (Focus != null ? Focus.Title : "—");
                case Order.Auto: return "штурм";
                default: return "ждёт приказа";
            }
        }
    }
}
