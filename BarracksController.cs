using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

[System.Serializable]
public class UnitType
{
    public string unitName = "Воин";
    public int costGold = 20;
    public int costFood = 15;
    public float trainTime = 5f; // Время тренировки в секундах
    public int spaceOccupied = 1; // Сколько мест занимает юнит в казарме

    [Header("Внешний вид солдата на карте")]
    public Color color = new Color(0.9f, 0.2f, 0.2f);
    public float bodyScale = 0.5f; // размер квадратика (у жителя 0.5)

    [Header("Бой")]
    public float maxHp = 100f;
    public float damage = 12f;
    public float attackRange = 1.1f;    // больше 2 - стреляет издалека (стрелы)
    public float attackCooldown = 1f;   // секунд между ударами
    public float moveSpeed = 3f;

    // Общая прибавка нашим бойцам (их здоровье и урон чуть выше чисел в списке; враги и деревни соседей - без неё)
    public const float SoldierHpMul = 1.15f, SoldierDamageMul = 1.15f;
    public float SoldierHp => maxHp * SoldierHpMul;
    public float SoldierDamage => damage * SoldierDamageMul;

    [Header("Доступность и лечение")]
    public int minBarracksLevel = 1;    // с какого уровня казармы можно обучать
    public float healPerSecond = 0f;    // > 0 - лекарь: в бою лечит своих (см. CitizenAgent, "Полевой маг")
    public float healRange = 0f;        // с какого расстояния лечит

    [Header("Снаряжение")]
    public bool usesWeapon = false;                 // бойцу можно выдать оружие из Оружейной
    public WeaponKind weaponKind = WeaponKind.Sword; // какое: меч или лук
    public bool usesShield = false;                 // бойцу можно выдать щит из Оружейной (ближний бой)
    public bool requiresHorse = false;              // конница: при найме нужна лошадь из Конюшни

    [Header("Похлёбка с Кухни")]
    public int pottageCost = 0;             // порций на бойца; 0 - по размеру бойца (1, а крупным с 3 мест - 2)
    public bool requiresPottage = false;    // без похлёбки не обучить вовсе (для будущих видов войск)
    public float pottageHpMul = 1.35f;      // усиленный похлёбкой боец (★): здоровье ...
    public float pottageDamageMul = 1.25f;  // ... урон ...
    public float pottageSpeedMul = 1.1f;    // ... и скорость

    [Header("Магия (путь развития)")]
    public PathKind path = PathKind.None;   // вид войск только для этого пути (None - для всех)
    public float aoeRadius = 0f;            // > 0 - снаряд бьёт по площади
    public float poisonDps = 0f;            // > 0 - удар отравляет (урон ядом в секунду, × сила маны)
    public float poisonDuration = 0f;
    public int potionCost = 0;              // зелий отравления на бойца (из Зельеварни)

    [Header("Горящая смола (путь Военного дела)")]
    public float fireRadius = 0f;           // > 0 - первый удар: ведро горящей смолы по площади (Поджигатель)
    public float fireDamage = 0f;           // урон всем, кто в огне в момент разлива
    public float burnDps = 0f;              // потом ещё горят: урон в секунду ...
    public float burnDuration = 0f;         // ... столько секунд
    public float fistDamage = 0f;           // ведро пустое - бьёт кулаками
    public int resinCost = 0;               // смолы на бойца при найме (из Цитадели)
    public bool IsFireBomber => fireRadius > 0f;

    [Header("Маг стихий (путь Магии)")]
    public float fireballChance = 0f;       // > 0 - иногда вместо снаряда пускает огненный шар (по площади, поджигает: burnDps)
    public float fireballRadius = 0f;
    public float fireballDamage = 0f;
    public float floodChance = 0f;          // > 0 - рядом с рекой иногда выводит её из берегов (волна по врагам)
    public float floodWaterRange = 4f;      // насколько близко к воде должен стоять маг
    public float floodRadius = 0f;
    public float floodDamage = 0f;
    public float floodSlow = 1f;            // замедление после волны (0.35 = почти втрое медленнее)
    public float floodSeconds = 0f;
    public float floodCooldown = 15f;
    public bool IsElementalist => fireballChance > 0f || floodChance > 0f;

    [Header("Полевая пушка (путь Военного дела)")]
    public bool isCannon = false;           // бьёт ядрами прямой наводкой (CannonBallistics), осколки
    public int cannonFragments = 0;
    public float cannonFragDamage = 0f;
    public float cannonFragRange = 2f;
    public float cannonBuildingMul = 1f;    // во сколько раз сильнее по зданиям
    public float cannonBarrierMul = 1f;     // ... и ещё по стенам, воротам, заслонам
    public int steelCost = 0;               // стали на бойца при найме (из Цитадели / Хранилищ)
    public int planksCost = 0;              // досок на бойца при найме
    public bool IsFieldCannon => isCannon;

    [Header("Катапульта (Инженерия: Военный инженер)")]
    public EngSchool engSchool = EngSchool.None;   // вид войск только для этого направления инженеров
    public bool isCatapult = false;         // бьёт камнями навесом по площади (через стены), урон падает с расстоянием
    public float catapultMinRange = 0f;     // ближе не стреляет
    public float catapultFarMul = 1f;       // урон на пределе дальности (× от полного)
    public float catapultRadius = 0f;       // радиус удара камня
    public float catapultBuildingMul = 1f;  // по зданиям (в набеге) сильнее
    public int partsCost = 0;               // деталей на бойца при найме (из Литейной)
    public bool IsCatapult => isCatapult;

    [Header("Тяжёлый Рыцарь")]
    public bool taunt = false;              // башни и пушки врага бьют его первым, пока он у них в радиусе
    /// <summary>Притягивает огонь башен (Тяжёлый Рыцарь; по имени - если вид войск сохранился в сцене старым).</summary>
    public bool IsTaunter => taunt || unitName == BarracksController.HeavyKnightName;

    [Header("Таран (казарма Ур.2)")]
    public bool isRam = false;              // двое бойцов (crew) несут таран: ломает стены и здания, по людям не бьёт
    public int crew = 1;                    // сколько новобранцев идёт на одного «бойца» этого вида (таран - 2 носильщика)
    public float ramHp = 0f;                // прочность самого тарана (сломан - носильщики дерутся ножами)
    public float ramDamage = 0f;            // удар тарана
    public float ramCooldown = 1.6f;
    public float ramSpeed = 2.2f;           // носильщики с тараном бегут (один - медленнее, RamSoloSpeedMul)
    public float ramBuildingMul = 1f;       // по зданиям
    public float ramBarrierMul = 1f;        // по стенам, воротам, заслонам
    public float ramBaseArmor = 0.1f;       // своя защита тарана; броня носильщиков прибавляется (CitizenAgent_Ram)
    public int ramRebuildPlanks = 0;        // досок на новый таран вместо сломанного (на пару носильщиков)
    public bool IsRam => isRam;
    public int Crew => Mathf.Max(1, crew);
    /// <summary>Мест в казарме под одно обучение (таран - все носильщики).</summary>
    public int TrainSpace => spaceOccupied * Crew;

    public bool IsHealer => healPerSecond > 0f;
    public bool IsPoisoner => poisonDps > 0f;
    // Похлёбку едят все бойцы (и расчёт полевой пушки); катапульте её не дают
    public bool UsesPottage => pottageCost > 0 || requiresPottage || !isCatapult;
    public int PottagePortions => (pottageCost > 0 ? pottageCost : (spaceOccupied >= 3 ? 2 : 1)) * Crew;   // таран - на каждого носильщика
    /// <summary>Лечение в секунду с учётом похлёбки (усиленный маг лечит сильнее, как и бьёт).</summary>
    public float HealRate(bool hearty) { return healPerSecond * (hearty ? pottageDamageMul : 1f); }
}

/// <summary>
/// Казарма. Обучение физическое:
///  1. Из свободных жителей выбирается новобранец, идёт к казарме и заходит внутрь.
///  2. Пока он внутри, идёт обучение.
///  3. Обучение закончилось - житель выходит уже солдатом (другой цвет/размер) и принадлежит этой казарме.
/// Приказы солдатам: сидеть внутри / охранять казарму / идти в точку на карте. Можно уволить со службы.
/// Снаряжение: оружие и щиты выдаются из Оружейной (кнопка "Снаряж." в списке солдат).
/// Ур.3 (нужна сталь): мест больше, и можно обучать полевого мага-целителя, который лечит своих в бою.
/// Ур.4: конница (нужны лошади из Конюшни).
/// Похлёбка (галочка "С похлёбкой"): любые бойцы выходят усиленными (★) - больше здоровья, урона
/// (у лекаря - лечения) и скорости. Мелким бойцам - 1 порция, крупным (от 3 мест) и коннице - 2. Порции берутся с Кухни:
///  - Кухня стоит вплотную к этой казарме или к группе казарм, стоящих вплотную друг к другу, -
///    похлёбка поступает сразу, без курьеров;
///  - иначе порцию несёт житель, и обучение начнётся, когда её принесут.
/// Для видов войск с requiresPottage похлёбка обязательна.
/// </summary>
public class BarracksController : MonoBehaviour, ISavedBuilding
{
    public const string TypeName = "Казарма";

    private BuildingInstance buildingInstance;
    public BuildingInstance BuildingRef => Bld;   // финал: голем ищет казарму павшего
    BuildingInstance Bld
    {
        get
        {
            if (buildingInstance == null) buildingInstance = GetComponent<BuildingInstance>();
            return buildingInstance;
        }
    }

    [Header("Настройки вместимости")]
    public int maxGarrisonSpace = 10; // Вместимость казармы на 1 уровне (10 мест)

    // Реальная вместимость: 10 мест за каждый уровень казармы
    public int MaxSpace
    {
        get
        {
            if (Bld == null) return maxGarrisonSpace;
            return maxGarrisonSpace * Mathf.Max(1, Bld.level);
        }
    }

    [Header("Охрана")]
    public float guardRadius = 3.5f;  // в каком радиусе от казармы ходят охранники

    [Header("Новобранец")]
    public float recruitTimeout = 25f; // если новобранец не дошёл за это время - заходит принудительно

    [Header("Лагерь и снабжение")]
    public int campWoodCost = 5;       // дерево из казны на баррикаду лагеря
    public int foodPerSoldier = 6;     // сколько еды берёт с собой каждый боец
    public float eatInterval = 60f;    // раз в сколько секунд каждый боец съедает 1 еду
    public int lowFoodMeals = 2;       // когда еды меньше, чем на столько "обедов" отряда - посылаем гонца
    public float starveGrace = 20f;    // сколько секунд отряд терпит голод без помощи, потом уходит в казарму

    [Header("Доступные для найма юниты")]
    public List<UnitType> availableUnits = new List<UnitType>
    {
        new UnitType { unitName = "Мечник", costGold = 20, costFood = 15, trainTime = 3f, spaceOccupied = 1, color = new Color(0.9f, 0.15f, 0.2f),  bodyScale = 0.55f,
                       maxHp = 100f, damage = 12f, attackRange = 1.1f, attackCooldown = 1f,   moveSpeed = 1.6f,
                       usesWeapon = true, weaponKind = WeaponKind.Sword, usesShield = true },
        new UnitType { unitName = "Лучник", costGold = 35, costFood = 20, trainTime = 5f, spaceOccupied = 1, color = new Color(0.25f, 0.85f, 0.3f), bodyScale = 0.5f,
                       maxHp = 70f,  damage = 9f,  attackRange = 6f,   attackCooldown = 1.4f, moveSpeed = 1.5f,
                       usesWeapon = true, weaponKind = WeaponKind.Bow },
        new UnitType { unitName = "Рыцарь", costGold = 80, costFood = 40, trainTime = 8f, spaceOccupied = 3, color = new Color(0.92f, 0.92f, 1f),   bodyScale = 0.75f,
                       maxHp = 240f, damage = 24f, attackRange = 1.2f, attackCooldown = 1.3f, moveSpeed = 1.3f,
                       usesWeapon = true, weaponKind = WeaponKind.Sword, usesShield = true },
        // Полевой маг-целитель: слабо бьёт издалека, зато лечит раненых бойцов рядом прямо в бою
        new UnitType { unitName = "Полевой маг", costGold = 90, costFood = 40, trainTime = 10f, spaceOccupied = 2, color = new Color(0.3f, 0.9f, 0.95f), bodyScale = 0.5f,
                       maxHp = 70f, damage = 4f, attackRange = 4f, attackCooldown = 1.5f, moveSpeed = 1.6f,
                       minBarracksLevel = 3, healPerSecond = 4f, healRange = 4f },
        // Конница (казарма Ур.4): каждому нужна лошадь из Конюшни. Лошадь гибнет вместе со всадником.
        // С похлёбкой (2 порции) - усиленный всадник ★
        new UnitType { unitName = "Всадник", costGold = 120, costFood = 50, trainTime = 10f, spaceOccupied = 4, color = new Color(1f, 0.6f, 0.15f), bodyScale = 0.8f,
                       maxHp = 200f, damage = 20f, attackRange = 1.3f, attackCooldown = 1f, moveSpeed = 2.8f,
                       minBarracksLevel = 4, requiresHorse = true, usesWeapon = true, weaponKind = WeaponKind.Sword, usesShield = true,
                       pottageCost = 2 },
        new UnitType { unitName = "Конный лучник", costGold = 130, costFood = 50, trainTime = 11f, spaceOccupied = 4, color = new Color(0.75f, 0.9f, 0.2f), bodyScale = 0.75f,
                       maxHp = 140f, damage = 11f, attackRange = 6.5f, attackCooldown = 1.2f, moveSpeed = 2.6f,
                       minBarracksLevel = 4, requiresHorse = true, usesWeapon = true, weaponKind = WeaponKind.Bow,
                       pottageCost = 2 }
    };

    // Разведчик (казарма Ур.2): быстрый, слабый в бою. Нужен, чтобы разведать деревню перед набегом
    // (см. RaidManager), а в набеге и дома видит дальше остальных (туман войны).
    static UnitType ScoutType()
    {
        return new UnitType { unitName = RaidManager.ScoutUnitName, costGold = 30, costFood = 15, trainTime = 4f, spaceOccupied = 1,
                              color = new Color(0.95f, 0.85f, 0.4f), bodyScale = 0.45f,
                              maxHp = 60f, damage = 5f, attackRange = 1.1f, attackCooldown = 1f, moveSpeed = 2.4f,
                              minBarracksLevel = 2 };
    }

    // Тяжёлый Рыцарь (казарма Ур.2): очень много здоровья, медленный, урона почти нет. Башни, пушки и
    // магические пушки врага бьют его первым, пока он у них в радиусе (UnitType.taunt) - в это время
    // остальные бойцы спокойно делают своё дело. Носит щит (в школе Хаоса - и с ядром хаоса).
    public const string HeavyKnightName = "Тяжёлый Рыцарь";
    public static UnitType HeavyKnightType()
    {
        return new UnitType { unitName = HeavyKnightName, costGold = 70, costFood = 35, trainTime = 9f, spaceOccupied = 3,
                              color = new Color(0.5f, 0.55f, 0.66f), bodyScale = 0.85f,
                              maxHp = 650f, damage = 4f, attackRange = 1.1f, attackCooldown = 1.6f, moveSpeed = 1.05f,
                              minBarracksLevel = 2, usesShield = true, taunt = true,
                              usesWeapon = true, weaponKind = WeaponKind.Sword };   // меч из Оружейной тоже можно
    }

    // Маг-отравитель (путь Магии, казарма Ур.2): бьёт издалека ядовитыми снарядами по площади.
    // Сила яда зависит от маны в воздухе (ManaManager.Factor).
    public const string PoisonerName = "Маг-отравитель";
    static UnitType PoisonerType()
    {
        return new UnitType { unitName = PoisonerName, costGold = 70, costFood = 25, trainTime = 7f, spaceOccupied = 1,
                              color = new Color(0.45f, 0.8f, 0.35f), bodyScale = 0.5f,
                              maxHp = 70f, damage = 6f, attackRange = 5.5f, attackCooldown = 2.2f, moveSpeed = 1.5f,
                              minBarracksLevel = 2, path = PathKind.Magic, potionCost = 1,
                              aoeRadius = 1.6f, poisonDps = 6f, poisonDuration = 5f };
    }

    // Маг стихий (путь Магии, казарма Ур.3): сильный, но дорогой и занимает много места.
    // Бьёт водяными снарядами; иногда - огненный шар по площади; у реки изредка выводит её из берегов
    // на врагов (волна + долгое замедление). Сила - от маны в воздухе (ManaManager.Factor).
    public const string ElementalistName = "Маг стихий";
    static UnitType ElementalistType()
    {
        return new UnitType { unitName = ElementalistName, costGold = 180, costFood = 60, trainTime = 16f, spaceOccupied = 4,
                              color = new Color(0.35f, 0.55f, 1f), bodyScale = 0.62f,
                              maxHp = 160f, damage = 15f, attackRange = 5.5f, attackCooldown = 1.6f, moveSpeed = 1.4f,
                              minBarracksLevel = 3, path = PathKind.Magic, potionCost = 3,
                              fireballChance = 0.2f, fireballRadius = 1.6f, fireballDamage = 40f, burnDps = 6f, burnDuration = 3f,
                              floodChance = 0.12f, floodWaterRange = 4f, floodRadius = 2.4f, floodDamage = 25f,
                              floodSlow = 0.35f, floodSeconds = 6f, floodCooldown = 15f };
    }

    // Поджигатель (путь Военного дела, казарма Ур.3): подбегает и выплёскивает ведро горящей смолы -
    // огонь бьёт всех на площади и поджигает (они ещё немного горят). Потом дерётся кулаками, слабо.
    // Ведро снова наполняют в казарме (RefillResin смолы из Цитадели).
    public const string FireBomberName = "Поджигатель";
    public const int RefillResin = 2;
    static UnitType FireBomberType()
    {
        return new UnitType { unitName = FireBomberName, costGold = 45, costFood = 20, trainTime = 6f, spaceOccupied = 1,
                              color = new Color(1f, 0.42f, 0.12f), bodyScale = 0.52f,
                              maxHp = 95f, damage = 4f, attackRange = 3.2f, attackCooldown = 1.1f, moveSpeed = 1.9f,
                              minBarracksLevel = 3, path = PathKind.War, resinCost = 3,
                              fireRadius = 1.9f, fireDamage = 32f, burnDps = 5f, burnDuration = 4f, fistDamage = 4f };
    }

    // Полевая пушка (путь Военного дела, казарма Ур.3): медленная, занимает много места и дорогая (сталь, доски).
    // Бьёт ядрами прямой наводкой - через стены не стреляет, зато ядро, попав в стену, ломает её: лучшая против
    // стен, ворот, заслонов и зданий. Осколки ядра ранят тех, кто стоит за целью.
    // «Оружие» - бронебойное ядро Инженерного двора; модули - подъёмник (Инженерия) и магические снаряды (Магия),
    // их пушка берёт сама, когда дома и не в бою.
    public const string FieldCannonName = "Полевая пушка";
    static UnitType FieldCannonType()
    {
        return new UnitType { unitName = FieldCannonName, costGold = 220, costFood = 40, trainTime = 18f, spaceOccupied = 5,
                              color = new Color(0.36f, 0.33f, 0.3f), bodyScale = 0.8f,
                              maxHp = 260f, damage = 34f, attackRange = 7.5f, attackCooldown = 4.2f, moveSpeed = 0.95f,
                              minBarracksLevel = 3, path = PathKind.War, usesWeapon = true, weaponKind = WeaponKind.Shot,
                              isCannon = true, cannonFragments = 4, cannonFragDamage = 11f, cannonFragRange = 2.3f,
                              cannonBuildingMul = 3f, cannonBarrierMul = 1.5f, steelCost = 6, planksCost = 8 };
    }

    // Катапульта (Инженерия, направление «Военный инженер», Инженерная казарма): расчёт - один житель,
    // плюс доски и детали. Камень летит навесом через стены и бьёт по площади; чем дальше цель, тем слабее удар.
    public const string CatapultName = "Катапульта";
    public static UnitType CatapultType()
    {
        return new UnitType { unitName = CatapultName, costGold = 180, costFood = 30, trainTime = 20f, spaceOccupied = 4,
                              color = new Color(0.55f, 0.4f, 0.24f), bodyScale = 0.8f,
                              maxHp = 220f, damage = 45f, attackRange = 13f, attackCooldown = 5f, moveSpeed = 0.8f,
                              minBarracksLevel = 1, path = PathKind.Engineering, engSchool = EngSchool.Military,
                              isCatapult = true, catapultMinRange = 3f, catapultFarMul = 0.35f, catapultRadius = 1.6f,
                              catapultBuildingMul = 1.5f, planksCost = 10, partsCost = 8 };
    }

    // Таран (казарма Ур.2, недорогой): двое обычных бойцов бегом несут бревно с железной головой. Очень сильно
    // бьёт по стенам, чуть слабее - по зданиям, по людям не бьёт вовсе. Хлипкий - выдерживает несколько ударов.
    // Сам идёт к стенам, если игрок не приказал другое. Сломан - носильщики выпадают и дерутся ножами (слабее
    // мечника). Носильщикам можно дать меч (не щит) и броню; их броня прибавляется к броне тарана.
    // Новый таран вместо сломанного собирают в казарме из досок (ramRebuildPlanks), когда оба носильщика внутри.
    public const string RamName = "Таран";
    public const float RamSoloSpeedMul = 0.6f;   // один носильщик (второй погиб / продан) тащит таран медленнее
    public static UnitType RamType()
    {
        return new UnitType { unitName = RamName, costGold = 35, costFood = 24, trainTime = 6f, spaceOccupied = 1,
                              color = new Color(0.62f, 0.47f, 0.3f), bodyScale = 0.48f,
                              maxHp = 70f, damage = 7f, attackRange = 1.1f, attackCooldown = 0.9f, moveSpeed = 1.75f,
                              minBarracksLevel = 2, usesWeapon = true, weaponKind = WeaponKind.Sword, usesShield = false,
                              isRam = true, crew = 2, ramHp = 90f, ramDamage = 60f, ramCooldown = 1.6f, ramSpeed = 2.3f,
                              ramBuildingMul = 1.6f, ramBarrierMul = 2.6f, ramBaseArmor = 0.1f,
                              planksCost = 6, ramRebuildPlanks = 4 };
    }

    public static UnitType FindType(string unitName)
    {
        foreach (var b in BuildingInstance.All)
        {
            if (b == null) continue;
            BarracksController br = b.GetComponent<BarracksController>();
            if (br == null || br.availableUnits == null) continue;
            UnitType u = br.availableUnits.Find(x => x != null && x.unitName == unitName);
            if (u != null) return u;
        }
        if (unitName == CatapultName) return CatapultType();   // Инженерной казармы может и не быть
        if (unitName == HeavyKnightName) return HeavyKnightType();   // и для чужого Тяжёлого Рыцаря (живой набег)
        if (unitName == RamName) return RamType();
        return null;
    }

    // ---------- Инженерная казарма ----------
    // Тот же контроллер, но в режиме инженеров: в списке только инженерные войска (катапульта),
    // свои места. Все приказы, отряды, лагеря и набеги работают как у обычной казармы.
    bool modeChecked;
    bool engineerMode;
    public bool IsEngineer
    {
        get
        {
            if (!modeChecked) ApplyMode();
            return engineerMode;
        }
    }

    void ApplyMode()
    {
        bool eng = GetComponent<EngineerBarracks>() != null
                   || (Bld != null && Bld.type != null && Bld.type.name == EngineerBarracks.TypeName);
        if (Bld != null && Bld.type != null) modeChecked = true;   // тип здания уже известен - решение окончательное
        else if (eng) modeChecked = true;
        if (!eng || engineerMode) return;
        engineerMode = true;
        availableUnits = new List<UnitType> { CatapultType() };
        Balance.ApplyUnits(availableUnits);   // цена и время обучения - из таблицы Balance
        maxGarrisonSpace = EngineerSpacePerLevel;
    }

    public const int EngineerSpacePerLevel = 8;   // 2 катапульты на уровень

    /// <summary>Название казармы для текстов.</summary>
    public string Title => IsEngineer ? EngineerBarracks.TypeName : TypeName;

    void Start() { ApplyMode(); }

    // Список мог сохраниться в сцене старым - разведчика добавляем сами (после лучника)
    void Awake()
    {
        ApplyMode();
        if (engineerMode) return;   // у инженеров - свой список (только катапульта)

        if (availableUnits != null && !availableUnits.Exists(u => u != null && u.unitName == FireBomberName))
            availableUnits.Add(FireBomberType());
        if (availableUnits != null && !availableUnits.Exists(u => u != null && u.unitName == PoisonerName))
            availableUnits.Add(PoisonerType());
        if (availableUnits != null && !availableUnits.Exists(u => u != null && u.unitName == ElementalistName))
            availableUnits.Add(ElementalistType());
        if (availableUnits != null && !availableUnits.Exists(u => u != null && u.unitName == FieldCannonName))
            availableUnits.Add(FieldCannonType());
        if (availableUnits == null) availableUnits = new List<UnitType>();
        var poisoner = availableUnits.Find(u => u != null && u.unitName == PoisonerName);
        if (poisoner != null && poisoner.potionCost <= 0) poisoner.potionCost = 1;   // старый список из сцены
        var fireBomber = availableUnits.Find(u => u != null && u.unitName == FireBomberName);
        if (fireBomber != null && fireBomber.minBarracksLevel < 3) fireBomber.minBarracksLevel = 3;   // старый список из сцены (было Ур.2)
        if (!availableUnits.Exists(u => u != null && u.unitName == RaidManager.ScoutUnitName))
        {
            int at = availableUnits.FindIndex(u => u != null && u.unitName == "Лучник");
            availableUnits.Insert(at >= 0 ? at + 1 : availableUnits.Count, ScoutType());
        }
        if (!availableUnits.Exists(u => u != null && u.unitName == HeavyKnightName))
        {
            int at = availableUnits.FindIndex(u => u != null && u.unitName == "Рыцарь");
            availableUnits.Insert(at >= 0 ? at + 1 : availableUnits.Count, HeavyKnightType());
        }
        var heavy = availableUnits.Find(u => u != null && u.unitName == HeavyKnightName);
        if (heavy != null) { heavy.taunt = true; heavy.usesWeapon = true; heavy.weaponKind = WeaponKind.Sword; }
        if (!availableUnits.Exists(u => u != null && u.unitName == RamName))
        {
            int at = availableUnits.FindIndex(u => u != null && u.unitName == HeavyKnightName);
            availableUnits.Insert(at >= 0 ? at + 1 : availableUnits.Count, RamType());
        }
        Balance.ApplyUnits(availableUnits);   // цена и время обучения - из таблицы Balance (и для списка из сцены)
    }

    // Юнит доступен на текущем уровне казармы
    public bool Unlocked(UnitType u)
    {
        int lvl = Bld != null ? Bld.level : 1;
        return u != null && lvl >= u.minBarracksLevel && DevelopmentPath.Allows(u.path) && DevelopmentPath.EngAllows(u.engSchool);
    }

    // Слот «на тело» (броня / плащ): показываем, если бойцу можно и это вообще есть у города
    static bool ShowBody(CitizenAgent s)
    {
        if (s == null || !s.CanWearBody) return false;
        return s.EquippedBody != null || ArmoryController.TotalBodies > 0 || DevelopmentPath.IsHeavy || DevelopmentPath.IsCamo;
    }

    // Положен ли бойцу щит (по виду войск)
    bool UnitUsesShield(CitizenAgent s)
    {
        if (s == null) return false;
        UnitType u = availableUnits.Find(x => x.unitName == s.UnitName);
        return u != null && u.usesShield;
    }

    // Солдаты этой казармы (физические жители на карте)
    private readonly List<CitizenAgent> soldiers = new List<CitizenAgent>();
    private readonly HashSet<CitizenAgent> selection = new HashSet<CitizenAgent>();
    public IReadOnlyList<CitizenAgent> Soldiers => soldiers;

    // Обучение
    private UnitType currentTrainingUnit = null;
    private CitizenAgent recruit = null;
    private CitizenAgent recruit2 = null;   // второй носильщик тарана (UnitType.crew = 2)
    private float trainingTimer = 0f;
    private float recruitWait = 0f;
    private bool trainingHorse = false;   // под текущее обучение уже забрана лошадь из Конюшни
    private BuildingInstance potionSource;   // откуда взяли зелье (Цитадель / Хранилище)
    private bool potionsPending = false;     // зелье ещё несут (обучение ждёт)
    private int potionToken = 0;
    private int trainingPotions = 0;  // зелья, забранные под текущее обучение
    private readonly List<UpgradeRules.Spent> trainingMaterials = new List<UpgradeRules.Spent>();   // сталь / доски полевой пушки
    private int trainingParts = 0;   // детали, забранные под текущее обучение (катапульта)

    // Похлёбка для текущего обучения
    public bool UsePottage { get; set; }  // галочка "С похлёбкой" (усиленные бойцы ★)
    // Обучение переживает выход из игры (обновление final-1). Новобранца при загрузке ещё нет, поэтому всё, что
    // взяли под обучение, при загрузке возвращается на склады, а через несколько секунд обучение того же бойца
    // начинается заново с оставшимся временем (если есть кого и на что учить; иначе ресурсы просто вернулись).
    public void SaveState(SaveBag bag)
    {
        bag.Set("set.pottage", UsePottage);
        if (trainQueue.Count > 0)
        {
            var names = new List<string>();
            foreach (var q in trainQueue) if (q != null) names.Add(q.unitName);
            bag.Set("br.queue", string.Join("|", names));
        }
        if (currentTrainingUnit == null) return;
        UnitType u = currentTrainingUnit;
        bag.Set("br.train", u.unitName);
        bag.Set("br.left", Mathf.Max(0.05f, trainingTimer));
        bag.Set("br.ref", string.Join(",", new[] { SaveBag.I(u.costGold), SaveBag.I(u.costFood), SaveBag.I(u.resinCost), SaveBag.I(trainingParts),
                                                  SaveBag.I(trainingPotions), SaveBag.I(pottageHeld), SaveBag.B(trainingHorse) }));
        var mats = new List<string[]>();
        foreach (var sp in trainingMaterials) mats.Add(new[] { SaveBag.I((int)sp.kind), SaveBag.I((int)sp.tier), SaveBag.I(sp.amount) });
        bag.SetList("br.mats", mats);
    }

    public void LoadState(SaveBag bag)
    {
        UsePottage = bag.Bool("set.pottage", UsePottage);
        string q = bag.Get("br.queue");
        if (!string.IsNullOrEmpty(q)) queueNames = q.Split('|');
        string unit = bag.Get("br.train");
        // Обновление 14: очередь учится и без игрока. Время отсутствия, которое осталось после текущего бойца,
        // засчитывается бойцам из очереди: каждый начнёт учиться, когда казарма проснётся, и выучится сразу
        // (или с остатком времени). Ресурсы берутся как обычно, в момент начала обучения.
        offlineCredit = 0f;
        offlineUnits = 0;
        if (queueNames != null && queueNames.Length > 0 && JobSave.Away > 0.0)
        {
            float busy = string.IsNullOrEmpty(unit) ? 0f : Mathf.Max(0f, bag.Float("br.left", 1f));
            offlineCredit = Mathf.Max(0f, (float)JobSave.Away - busy);
            offlineUnits = offlineCredit > 0f ? queueNames.Length : 0;
        }
        if (string.IsNullOrEmpty(unit)) return;
        string[] r = (bag.Get("br.ref") ?? "").Split(',');
        var mats = bag.List("br.mats");
        float left = JobSave.Left(bag.Float("br.left", 1f));
        JobSave.AfterLoad(() =>
        {
            var rm = ResourceManager.Instance;
            if (rm != null)
            {
                rm.Add(SaveBag.At(r, 0), 0, 0, SaveBag.At(r, 1));
                rm.AddResin(SaveBag.At(r, 2));
                rm.AddParts(SaveBag.At(r, 3));
                if (SaveBag.At(r, 4) > 0) rm.AddPotions(SaveBag.At(r, 4));
            }
            if (SaveBag.At(r, 5) > 0) KitchenController.ReturnPortions(SaveBag.At(r, 5), BarracksPos());
            if (SaveBag.AtB(r, 6)) StableController.ReturnHorse();
            var st = TownHallStorage.Instance;
            if (st != null)
                foreach (var a in mats)
                    if (SaveBag.At(a, 2) > 0) st.Add((RefinedKind)SaveBag.At(a, 0), (QualityTier)Mathf.Clamp(SaveBag.At(a, 1), 0, 2), SaveBag.At(a, 2));
            resumeUnit = unit;
            resumeLeft = left;
            resumeAt = Time.time + 6f;
        });
    }

    // Обновление 14: время, пока игрока не было, для бойцов из очереди (см. LoadState)
    float offlineCredit;
    int offlineUnits;
    bool startingFromQueue;
    public bool OfflineQueueActive => offlineUnits > 0 && offlineCredit > 0f;

    string resumeUnit;      // какое обучение продолжить после загрузки
    float resumeLeft, resumeAt;

    void TryResumeTraining()
    {
        if (resumeUnit == null || Time.time < resumeAt) return;
        string name = resumeUnit;
        resumeUnit = null;
        if (IsTraining || availableUnits == null) return;
        UnitType u = availableUnits.Find(x => x != null && x.unitName == name);
        if (u == null) return;
        TrainUnit(u);
        if (IsTraining && currentTrainingUnit == u) trainingTimer = Mathf.Min(trainingTimer, resumeLeft);
    }
    private int pottageHeld = 0;          // сколько порций забрано с Кухни под это обучение
    private bool pottagePending = false;  // порцию ещё несут (обучение ждёт)
    private int pottageToken = 0;         // номер заказа: опоздавший рейс от отменённого обучения не засчитывается

    // Режим "кликните на карту, куда послать" (обрабатывает BuildingPlacer)
    public bool PickingTarget { get; set; }

    // UI
    private Vector2 scroll;
    private CitizenAgent focused;
    private float focusUntil;

    public bool IsTraining => currentTrainingUnit != null;
    public string CurrentTrainingName => currentTrainingUnit != null ? currentTrainingUnit.unitName + (pottageHeld > 0 ? " ★" : "") : "";
    public bool RecruitInside => recruit != null && recruit.IsInside(Bld);
    public bool WaitingPottage => IsTraining && pottagePending;
    public float TrainingProgress => currentTrainingUnit != null && currentTrainingUnit.trainTime > 0 ? (1f - (trainingTimer / currentTrainingUnit.trainTime)) : 0f;

    // Общее количество людей (готовые солдаты + новобранец, который обучается прямо сейчас)
    public int HeadCount
    {
        get
        {
            int c = 0;
            foreach (var s in soldiers) if (s != null) c++;
            if (IsTraining) c += currentTrainingUnit.Crew;
            return c;
        }
    }

    // Занятое место в казарме с учётом веса каждого юнита
    public int CurrentOccupiedSpace
    {
        get
        {
            int total = 0;
            foreach (var s in soldiers)
            {
                if (s == null) continue;
                UnitType u = availableUnits.Find(x => x.unitName == s.UnitName);
                total += u != null ? u.spaceOccupied : 1;
            }
            total += WarFieldSoldiers.SpaceOf(this);   // klan-voina-14: бойцы этой казармы на карте войны тоже занимают место
            return total;
        }
    }

    void Update()
    {
        soldiers.RemoveAll(s => s == null);
        TickSquads(Time.deltaTime);
        RefillBuckets();
        RebuildRams();
        LoadCannons();
        LoadChaosCores();
        CheckMarchClick();
        TryResumeTraining();
        TickQueue();

        if (currentTrainingUnit == null) return;

        // Новобранец пропал (например, уменьшилось население) - отменяем и возвращаем ресурсы
        if (recruit == null || recruit.TrainingAt != Bld
            || (currentTrainingUnit.Crew > 1 && (recruit2 == null || recruit2.TrainingAt != Bld)))
        {
            CancelTraining(true);
            return;
        }

        if (Bld != null && !Bld.isWorking) return;

        // Пока новобранец не зашёл в казарму - обучение не идёт
        if (!recruit.IsInside(Bld) || (recruit2 != null && !recruit2.IsInside(Bld)))
        {
            recruitWait += Time.deltaTime;
            if (recruitWait > recruitTimeout)   // застрял по дороге
            {
                recruit.ForceEnter(Bld);
                if (recruit2 != null) recruit2.ForceEnter(Bld);
            }
            return;
        }

        // Похлёбку ещё несут с Кухни - ждём
        if (pottagePending) return;

        // Зелье ещё несут - ждём
        if (potionsPending) return;

        trainingTimer -= Time.deltaTime;
        if (trainingTimer <= 0f)
        {
            bool hearty = pottageHeld > 0;
            UnitType unit = currentTrainingUnit;
            recruit.BecomeSoldier(Bld, unit, hearty);
            trainingPotions = 0;   // зелье израсходовано
            trainingMaterials.Clear();   // сталь и доски ушли на пушку
            trainingParts = 0;           // детали ушли на катапульту
            soldiers.Add(recruit);
            AssignNewSoldier(recruit);
            if (recruit2 != null)
            {
                // Второй носильщик тарана - в тот же отряд, что и первый
                recruit2.BecomeSoldier(Bld, unit, hearty);
                soldiers.Add(recruit2);
                if (recruit.Squad != null) JoinSquad(recruit2, recruit.Squad);
                else AssignNewSoldier(recruit2);
                if (!hearty) WarManager.Notify($"{Title}: готов {unit.unitName} — два носильщика", true);
                recruit2 = null;
            }
            trainingHorse = false;   // лошадь теперь у бойца
            pottageHeld = 0;         // похлёбку съели
            if (hearty) WarManager.Notify($"{Title}: обучен {unit.unitName} ★ (на похлёбке)", true);

            recruit = null;
            currentTrainingUnit = null;
        }
    }

    // Поджигатели с пустым ведром, зашедшие в казарму, заправляются смолой из Цитадели
    float refillTimer;
    void RefillBuckets()
    {
        if (AccumulationMode.On) return;   // режим накопления: смолу не тратим
        refillTimer -= Time.deltaTime;
        if (refillTimer > 0f) return;
        refillTimer = 1f;
        var rm = ResourceManager.Instance;
        if (rm == null || Bld == null) return;
        foreach (var s in soldiers)
        {
            if (s == null || !s.NeedsBucket || !s.IsInside(Bld)) continue;
            if (!rm.SpendResin(RefillResin)) return;
            s.RefillBucket();
        }
    }

    // Сломанный таран собирают заново: носильщики со сломанным тараном внутри казармы (лучше - вдвоём),
    // доски берутся из Цитадели (ramRebuildPlanks на таран). Нет досок - носильщики так и дерутся ножами.
    float ramTimer;
    void RebuildRams()
    {
        ramTimer -= Time.deltaTime;
        if (ramTimer > 0f) return;
        ramTimer = 1.5f;
        if (Bld == null || Bld.IsRuined || !Bld.isWorking) return;
        var st = TownHallStorage.Instance;
        foreach (var s in soldiers)
        {
            if (s == null || !s.IsRamCarrier || !s.IsInside(Bld)) continue;
            bool broken = s.RamBroken;
            float wear = s.RamWear;
            if (!broken && wear < 0.05f) continue;
            UnitType u = s.ExpUnit;
            CitizenAgent mate = s.RamMate();
            if (mate != null && mate.RamBroken == broken && !mate.IsInside(Bld) && !mate.IsOnExpedition) continue;   // напарника ждём
            // Сломан - новый таран целиком; побит - починка: досок по доле выбитой прочности (не меньше одной)
            int full = Mathf.Max(0, u.ramRebuildPlanks);
            int need = broken ? full : Mathf.Min(full, Mathf.Max(1, Mathf.CeilToInt(full * wear)));
            if (need > 0)
            {
                if (st == null || UpgradeRules.CountAtLeast(st, RefinedKind.Planks, QualityTier.Medium) < need) continue;
                UpgradeRules.SpendAtLeast(st, RefinedKind.Planks, QualityTier.Medium, need, new List<UpgradeRules.Spent>());
            }
            s.RepairRam();
            if (mate != null && mate.RamBroken == broken && mate.IsInside(Bld)) mate.RepairRam();
            WarManager.Notify(broken ? $"{Title}: собран новый таран ({need} досок)" : $"{Title}: таран починен ({need} досок)", true);
        }
    }

    // Полевые пушки сами берут модули со склада Цитадели / двора: подъёмник (Инженерия) и пачку магических
    // снарядов (Магия). Покупные с рынка - тоже. Где бы ни стояли дома (на посту, в лагере), лишь бы не в бою
    // и не в походе: носильщика нет - упрощение. Снаряды - выбранного в «Снаряж.» вида, если он есть, иначе лучшие.
    float cannonTimer;
    void LoadCannons()
    {
        cannonTimer -= Time.deltaTime;
        if (cannonTimer > 0f) return;
        cannonTimer = 2f;
        if (Bld == null || Bld.IsRuined) return;
        foreach (var s in soldiers)
        {
            if (s == null || !s.IsFieldCannon || s.IsDead || s.IsOnExpedition || s.InCombat) continue;
            if (cargoFor.Contains(s) || !AtHome(s)) continue;   // уже несут / пушка не у казармы
            if (!s.CannonLift.Has && EngineerYard.CountKits(ModuleKind.CannonLift) > 0)
            {
                int mk = EngineerYard.BestMk(ModuleKind.CannonLift);
                ModuleKit lift = new ModuleKit { kind = ModuleKind.CannonLift, mk = mk };
                if (EngineerYard.TakeKit(lift, out BuildingInstance src))
                {
                    CitizenAgent c = s;
                    BringKit(s, lift, src, () =>
                    {
                        if (c.CannonLift.Has) return false;
                        c.MountCannonLift(mk);
                        WarManager.Notify($"{c.UnitName} {c.CitizenName}: поставлен {TowerModules.KitName(ModuleKind.CannonLift, mk)}", true);
                        return true;
                    });
                    continue;
                }
            }
            if (s.CannonHasShells || !s.CannonShellAuto) continue;
            ModuleKit pack = new ModuleKit();
            bool got = s.CannonShellPref != ModuleKind.None && MagicShells.BestPack(s.CannonShellPref, out pack);
            if (!got) got = MagicShells.BestPack(out pack);
            if (got && EngineerYard.TakeKit(pack, out BuildingInstance from))
            {
                CitizenAgent c = s;
                ModuleKit p = pack;
                BringKit(s, pack, from, () =>
                {
                    if (c.CannonHasShells) return false;
                    c.LoadCannonShells(p);
                    WarManager.Notify($"{c.UnitName} {c.CitizenName}: заряжена — {TowerModules.KitName(p)}", true);
                    return true;
                });
            }
        }
    }

    // ---------- Подвоз к пушкам и магам ----------
    // Снаряды, подъёмник, бронебойное ядро и ядро хаоса не появляются у бойца сами: житель забирает их
    // там, где они лежат (Цитадель, Инженерный двор, Оружейная), и несёт в казарму. Боец должен ждать у казармы.

    readonly HashSet<CitizenAgent> cargoFor = new HashSet<CitizenAgent>();   // кому сейчас что-то несут

    /// <summary>Боец у казармы (внутри или у двери) - ему можно принести груз.</summary>
    public bool AtHome(CitizenAgent s)
    {
        return s != null && Bld != null && !Bld.IsRuined && (s.IsInside(Bld) || Bld.DistanceTo(s.transform.position) <= 1.5f);
    }

    /// <summary>Обновление 14: снаряжение выдают и снимают только бойцу, который сейчас в казарме (внутри или у двери).
    /// Ушёл в бой, в поход или стоит на карте - меч ему не передать.</summary>
    public bool CanGear(CitizenAgent s)
    {
        if (s == null || s.IsDead || !s.IsSoldier || s.IsOnExpedition) return false;
        if (WarCityMode.Active && WarFieldSoldiers.Staying.Contains(s) && Bld != null && !Bld.IsRuined) return true;   // зашёл с карты войны в казарму
        return AtHome(s);
    }

    public static string NotHereText(CitizenAgent s)
    {
        if (s != null && s.IsOnExpedition) return "Боец в походе — снаряжение выдают только в казарме";
        return "Боец не в казарме — снаряжение выдают только в казарме. Пусть вернётся (кнопка «Домой» у отряда)";
    }

    /// <summary>Боец ждёт груз из Цитадели / двора / Оружейной (для подписи в списке).</summary>
    public bool CargoComing(CitizenAgent s) { return s != null && cargoFor.Contains(s); }

    static BuildingInstance HallOrNull()
    {
        var pm = PopulationManager.Instance;
        return pm != null ? pm.Hall : null;
    }

    // Комплект уже взят со склада (TakeKit): житель несёт его из src в казарму. load() - отдать бойцу;
    // false (боец пропал, уже есть такое) или рейс сорван - комплект возвращается в Цитадель.
    void BringKit(CitizenAgent s, ModuleKit kit, BuildingInstance src, System.Func<bool> load)
    {
        cargoFor.Add(s);
        BarracksController self = this;
        int carry = CarryLedger.Add(kit);   // выход из игры в пути - комплект в Цитадели
        Logistics.Request(new HaulJob
        {
            from = src != null ? src : HallOrNull(),
            to = Bld,
            prefer = Bld,
            onDeliver = () =>
            {
                CarryLedger.Done(carry);
                if (self != null) self.cargoFor.Remove(s);
                bool ok = self != null && s != null && !s.IsDead && s.IsSoldier && !s.IsOnExpedition && load();
                if (!ok) EngineerYard.AddToHall(kit);
            },
            onAbort = loaded =>
            {
                CarryLedger.Done(carry);
                if (self != null) self.cargoFor.Remove(s);
                EngineerYard.AddToHall(kit);
            }
        });
    }

    // Маги стихий в казарме (или у её двери) сами берут лучшее ядро хаоса из Цитадели (школа Хаоса, ChaosMagic).
    // Покупное ядро с рынка тоже годится, даже если школу сменили.
    float chaosTimer;
    void LoadChaosCores()
    {
        chaosTimer -= Time.deltaTime;
        if (chaosTimer > 0f) return;
        chaosTimer = 2f;
        if (Bld == null || Bld.IsRuined) return;
        foreach (var s in soldiers)
        {
            if (s == null || !s.IsElementalist || s.HasChaosCore || s.IsDead || s.IsOnExpedition || s.InCombat) continue;
            if (cargoFor.Contains(s) || !AtHome(s)) continue;
            if (!BestChaosCore(out ModuleKit core) || !EngineerYard.TakeKit(core, out BuildingInstance src)) return;
            CitizenAgent mage = s;
            BringKit(s, core, src, () =>
            {
                if (mage.HasChaosCore) return false;
                mage.LoadChaosCore(core);
                WarManager.Notify($"{mage.UnitName} {mage.CitizenName}: принесли {TowerModules.KitName(core)}", true);
                return true;
            });
        }
    }

    // Лучшее ядро в Цитадели / дворах: выше марка, при равной - начатое (с остатком) раньше целого
    static bool BestChaosCore(out ModuleKit best)
    {
        best = default(ModuleKit);
        bool found = false;
        foreach (var k in EngineerYard.AllKits())
        {
            if (k.kind != ModuleKind.ChaosCore) continue;
            if (!found || k.mk > best.mk || (k.mk == best.mk && k.charges > 0 && best.charges == 0)) { best = k; found = true; }
        }
        return found;
    }

    // ---------- Наёмники (рынок) ----------

    /// <summary>Свободное место под этот вид войск.</summary>
    public int FreeSpace => MaxSpace - CurrentOccupiedSpace - (IsTraining && currentTrainingUnit != null ? currentTrainingUnit.TrainSpace : 0);

    /// <summary>Казарма, где поместится боец этого вида (больше всего места).</summary>
    public static BarracksController WithRoomFor(int space)
    {
        BarracksController best = null;
        int bestFree = -1;
        foreach (var b in BuildingInstance.All)
        {
            if (b == null || b.IsRuined) continue;
            BarracksController br = b.GetComponent<BarracksController>();
            if (br == null || br.IsEngineer) continue;   // наёмников в Инженерную казарму не селим
            int free = br.FreeSpace;
            if (free >= space && free > bestFree) { bestFree = free; best = br; }
        }
        return best;
    }

    /// <summary>Наёмник прибыл (купили на рынке или вернулся из лагеря гильдии): становится бойцом этой казармы.</summary>
    public void AdoptSoldier(CitizenAgent a, UnitType u, bool isHearty, Weapon w, Shield sh, Squad into = null)
    {
        if (a == null || u == null) return;
        a.BecomeSoldier(Bld, u, isHearty);
        if (w != null && !a.EquipWeapon(w) && !ArmoryController.Stash(w))
            WarManager.Notify("Оружие наёмника некуда деть — потеряно", false);
        if (sh != null && !ShieldBearer.Equip(a, sh) && !ArmoryController.StashShield(sh))
            WarManager.Notify("Щит наёмника некуда деть — потерян", false);
        soldiers.Add(a);
        if (into != null && squads.Contains(into)) JoinSquad(a, into);   // купленный отряд остаётся отрядом
        else AssignNewSoldier(a);
    }

    /// <summary>Новый пустой отряд для купленного на рынке отряда (AdoptSoldier(..., into)).</summary>
    public Squad NewSquadForArrivals() { return NewSquad(); }

    /// <summary>Отпустить бойца в лагерь наёмников гильдии (продажа на рынке). Его снаряжение и конь уходят с ним.</summary>
    public bool ReleaseForSale(CitizenAgent a, out SoldierLot lot)
    {
        lot = null;
        if (a == null || a.IsDead || !soldiers.Contains(a) || a.IsOnExpedition) return false;
        bool mounted = a.HasHorse;
        lot = a.PackForMercenaryCamp();
        if (lot == null) return false;
        lot.shield = ShieldBearer.Unequip(a);
        soldiers.Remove(a);
        selection.Remove(a);
        // Боец не исчезает на месте: идёт к Торговому дому и уходит за край карты (CitizenAgent_Departure)
        a.DepartForSale(TradeGuild.Open && TradeGuild.House != null ? TradeGuild.House.Building : null, mounted);
        return true;
    }

    /// <summary>Боец уходит из военного города на карту войны (со своим оружием и щитом): идёт за край карты.</summary>
    // ---------- Военный город: сами ли бойцы выходят на карту войны ----------
    // На карте боец уже не числится в казарме, и продать его на рынке клана нельзя.
    // «Держать в казарме» - бойцы остаются дома: их можно продать союзникам или вывести вручную («Послать...» за край).
    // Помним по клетке казармы и логину (военный город - перезапуск игры, поле не дожило бы).

    string HoldKey
    {
        get
        {
            string who = AccountManager.Profile != null ? AccountManager.Profile.login : "";
            Vector2Int c = Bld != null ? Bld.OriginCell : Vector2Int.zero;
            return $"Strategyca.WarHold.{who}.{c.x},{c.y}";
        }
    }

    public bool HoldFromWarMap
    {
        get { return PlayerPrefs.GetInt(HoldKey, 0) == 1; }
        set { PlayerPrefs.SetInt(HoldKey, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    public bool ReleaseToWar(CitizenAgent a)
    {
        if (a == null || a.IsDead || !soldiers.Contains(a) || a.IsOnExpedition) return false;
        bool mounted = a.HasHorse;
        // Как при продаже: отряд, койка, добыча, модули - остаются городу; оружие и щит уходят с ним
        if (a.PackForMercenaryCamp() == null) return false;
        ShieldBearer.Unequip(a);
        soldiers.Remove(a);
        selection.Remove(a);
        a.VanishToWarMap(mounted);   // на карте войны он появится там же, где стоял (его ведёт сервер)
        return true;
    }

    void CancelTraining(bool refund)
    {
        // Зелье: уже здесь - вернуть; ещё несут - вернёт сам рейс (заказ устарел)
        if (trainingPotions > 0 && !potionsPending) PotionHouse.ReturnPotions(trainingPotions, potionSource);
        trainingPotions = 0;
        potionToken++;
        potionsPending = false;
        potionSource = null;
        if (refund && currentTrainingUnit != null && ResourceManager.Instance != null)
        {
            ResourceManager.Instance.Add(currentTrainingUnit.costGold, 0, 0, currentTrainingUnit.costFood);
            ResourceManager.Instance.AddResin(currentTrainingUnit.resinCost);
            UpgradeRules.RefundSpent(trainingMaterials);
            ResourceManager.Instance.AddParts(trainingParts);
        }
        trainingMaterials.Clear();
        trainingParts = 0;
        if (recruit != null) recruit.CancelTraining();
        if (recruit2 != null) recruit2.CancelTraining();
        recruit2 = null;
        if (trainingHorse && !StableController.ReturnHorse())   // обучение сорвано - лошадь обратно в Конюшню
            WarManager.Notify("В Конюшнях нет места: лошадь сорванного обучения потеряна", false);
        trainingHorse = false;

        // Похлёбка: уже здесь - обратно на Кухню; ещё несут - вернёт сам рейс (заказ устарел)
        if (pottageHeld > 0 && !pottagePending) KitchenController.ReturnPortions(pottageHeld, BarracksPos());
        pottageToken++;
        pottageHeld = 0;
        pottagePending = false;

        recruit = null;
        currentTrainingUnit = null;
    }

    Vector2 BarracksPos() { return Bld != null ? Bld.Center : (Vector2)transform.position; }

    // Сколько порций похлёбки нужно этому виду войск сейчас (0 - без похлёбки)
    public int PottageNeed(UnitType unit)
    {
        if (unit == null || !unit.UsesPottage) return 0;
        if (unit.requiresPottage || UsePottage) return unit.PottagePortions;
        return 0;
    }

    static bool quietFail;      // очередь пробует начать обучение сама - без всплывающих сообщений
    static string lastFail = "";
    static void Fail(string msg) { lastFail = msg; if (!quietFail) WarManager.Notify(msg, false); }

    // ---------- Очередь обучения (final-8) ----------
    // До QueueMax бойцов «на потом». Ресурсы НЕ берутся заранее: каждый начнёт учиться, когда казарма освободится
    // и хватит ресурсов, мест и свободных жителей. Не хватает - очередь ждёт и пишет, чего ждёт.
    public const int QueueMax = 5;
    readonly List<UnitType> trainQueue = new List<UnitType>();
    string[] queueNames;        // из сохранения: бойцы в очереди (разберём, когда казарма проснётся)
    float queueTimer;
    string queueProblem = "";

    public int QueueCount => trainQueue.Count;

    /// <summary>Кнопка «Обучить» / «В очередь»: свободна - учим сразу, занята - ставим в очередь.</summary>
    public void TrainOrQueue(UnitType unit)
    {
        if (unit == null) return;
        if (!IsTraining && trainQueue.Count == 0 && resumeUnit == null) { TrainUnit(unit); return; }
        if (trainQueue.Count >= QueueMax) { Fail($"Очередь {Title} полна ({QueueMax})"); return; }
        trainQueue.Add(unit);
        queueTimer = 0f;
        WarManager.Notify($"{Title}: {unit.unitName} в очереди ({trainQueue.Count}/{QueueMax}). Ресурсы возьмём, когда начнётся обучение", true);
    }

    void TickQueue()
    {
        if (queueNames != null && availableUnits != null)
        {
            foreach (var n in queueNames)
            {
                UnitType u = availableUnits.Find(x => x != null && x.unitName == n);
                if (u != null && trainQueue.Count < QueueMax) trainQueue.Add(u);
            }
            queueNames = null;
            if (OfflineQueueActive && trainQueue.Count > 0)
                WarManager.Notify($"{Title}: пока вас не было, очередь училась — бойцы выйдут один за другим", true);
        }
        if (trainQueue.Count == 0 && queueNames == null) { offlineUnits = 0; offlineCredit = 0f; }
        if (IsTraining || trainQueue.Count == 0 || resumeUnit != null) return;
        queueTimer -= Time.deltaTime;
        if (queueTimer > 0f) return;
        queueTimer = OfflineQueueActive ? 0.5f : 2f;
        UnitType next = trainQueue[0];
        lastFail = "";
        quietFail = true;
        startingFromQueue = true;
        try { TrainUnit(next); }
        finally { quietFail = false; startingFromQueue = false; }
        if (IsTraining)
        {
            trainQueue.RemoveAt(0);
            queueProblem = "";
            return;
        }
        if (!Unlocked(next) || !DevelopmentPath.EngAllows(next.engSchool))
        {
            trainQueue.RemoveAt(0);   // боец больше недоступен (сменили направление) - убираем из очереди
            WarManager.Notify($"{Title}: {next.unitName} убран из очереди — {lastFail}", false);
            return;
        }
        if (lastFail != queueProblem && lastFail != "") WarManager.Notify($"{Title}: очередь ждёт — {lastFail}", false);
        queueProblem = lastFail;
    }

    float QueueRowH => trainQueue.Count > 0 ? 30f + (queueProblem != "" ? 18f : 0f) : 0f;

    float DrawQueue(float x, float y, float w)
    {
        if (trainQueue.Count == 0) return y;
        LocGUI.Label(new Rect(x, y, 70f, 26f), UiKit.Col(UiKit.MutedHex, $"Очередь {trainQueue.Count}/{QueueMax}:"), Mid(UiKit.Small, false));
        float bx = x + 74f, bw = Mathf.Min(110f, (w - 74f) / QueueMax - 4f);
        for (int i = 0; i < trainQueue.Count; i++)
        {
            UnitType u = trainQueue[i];
            if (UiKit.Button(new Rect(bx, y + 1f, bw, 24f), "✕ " + u.unitName, UiKit.Kind.Ghost, $"Убрать {u.unitName} из очереди (ресурсы ещё не взяты)"))
            {
                trainQueue.RemoveAt(i);
                if (i == 0) queueProblem = "";
                break;
            }
            bx += bw + 4f;
        }
        y += 30f;
        if (queueProblem != "")
        {
            LocGUI.Label(new Rect(x, y, w, 18f), UiKit.Col(UiKit.WarnHex, "Ждёт: " + queueProblem), UiKit.Small);
            y += 18f;
        }
        return y;
    }

    public void TrainUnit(UnitType unit)
    {
        if (unit == null) return;
        if (Bld != null && !Bld.isWorking) { Fail($"{Title} не работает: {Bld.StatusText}"); return; }
        if (!DevelopmentPath.EngAllows(unit.engSchool)) { Fail($"{unit.unitName}: нужно направление «{DevelopmentPath.EngName(unit.engSchool)}»"); return; }
        if (!Unlocked(unit)) { Fail($"{unit.unitName}: нужна казарма Ур.{unit.minBarracksLevel}"); return; }
        if (IsTraining) { Fail($"{Title} уже занята обучением"); return; }

        var pm = PopulationManager.Instance;
        if (pm != null && pm.FreePeople < unit.Crew) { Fail(unit.Crew > 1 ? $"{unit.unitName}: нужно {unit.Crew} свободных жителя" : "Нет свободных жителей для найма"); return; }

        if (CurrentOccupiedSpace + unit.TrainSpace > MaxSpace)
        {
            Fail($"Мало места в казарме: нужно {unit.TrainSpace}, свободно {MaxSpace - CurrentOccupiedSpace}");
            return;
        }

        var rm = ResourceManager.Instance;
        if (rm == null || !rm.HasEnough(unit.costGold, 0, 0, unit.costFood))
        {
            Fail($"{unit.unitName}: нужно {unit.costGold} золота и {unit.costFood} еды");
            return;
        }

        if (unit.resinCost > 0 && rm.resin < unit.resinCost)
        {
            Fail($"{unit.unitName}: нужно {unit.resinCost} смолы в Цитадели (её даёт Лесопилка Ур.3 или рынок)");
            return;
        }

        if (unit.steelCost > 0 || unit.planksCost > 0)
        {
            var st0 = TownHallStorage.Instance;
            int steel = st0 != null ? UpgradeRules.CountAtLeast(st0, RefinedKind.Steel, QualityTier.Medium) : 0;
            int planks = st0 != null ? UpgradeRules.CountAtLeast(st0, RefinedKind.Planks, QualityTier.Medium) : 0;
            if (steel < unit.steelCost || planks < unit.planksCost)
            {
                Fail($"{unit.unitName}: нужно {unit.steelCost} стали и {unit.planksCost} досок (есть {steel} и {planks})");
                return;
            }
        }

        if (unit.partsCost > 0 && rm.parts < unit.partsCost)
        {
            Fail($"{unit.unitName}: нужно {unit.partsCost} деталей (есть {rm.parts}) — их льёт Литейная");
            return;
        }

        if (unit.potionCost > 0 && PotionHouse.TotalPotions < unit.potionCost)
        {
            Fail($"{unit.unitName}: нужно зелье отравления ({unit.potionCost}) — сварите в Зельеварне, оно должно лежать в Цитадели или Хранилище");
            return;
        }

        // Похлёбка: ищем Кухню, где хватает порций (лучше - стоящую вплотную)
        int portions = PottageNeed(unit);
        KitchenController kitchen = null;
        bool linked = false;
        if (portions > 0)
        {
            kitchen = KitchenController.PickFor(Bld, portions, out linked);
            if (kitchen == null)
            {
                Fail($"{unit.unitName}: нужна похлёбка ({portions} порц.) — сварите её на Кухне" +
                     (unit.requiresPottage ? "" : " или снимите галочку «С похлёбкой»"));
                return;
            }
        }

        // Конница: сначала берём лошадь из Конюшни
        bool horse = unit.requiresHorse;
        if (horse && !StableController.TryTakeHorse())
        {
            Fail($"{unit.unitName}: нет свободных лошадей (выращивайте их в Конюшне)");
            return;
        }

        // Берём реального жителя с карты
        CitizenAgent r = pm != null ? pm.TakeRecruit(Bld) : null;
        if (r == null)
        {
            if (horse) StableController.ReturnHorse();
            Fail("Некого отправить на обучение");
            return;
        }
        // Таран: второй носильщик
        CitizenAgent r2 = null;
        if (unit.Crew > 1)
        {
            r2 = pm.TakeRecruit(Bld);
            if (r2 == null)
            {
                r.CancelTraining();
                if (horse) StableController.ReturnHorse();
                Fail($"{unit.unitName}: нужен второй носильщик — нет свободных жителей");
                return;
            }
        }

        // Зелье берём из Цитадели / Хранилища - его принесут физически
        BuildingInstance potSrc = null;
        if (unit.potionCost > 0 && !PotionHouse.TakePotions(unit.potionCost, out potSrc))
        {
            r.CancelTraining();
            if (r2 != null) r2.CancelTraining();
            if (horse) StableController.ReturnHorse();
            Fail($"{unit.unitName}: зелья закончились — попробуйте ещё раз");
            return;
        }

        // Забираем похлёбку с Кухни
        if (kitchen != null && !kitchen.Take(portions))
        {
            r.CancelTraining();
            if (r2 != null) r2.CancelTraining();
            if (horse) StableController.ReturnHorse();
            if (unit.potionCost > 0) PotionHouse.ReturnPotions(unit.potionCost, potSrc);
            Fail("Похлёбка закончилась — попробуйте ещё раз");
            return;
        }

        trainingPotions = unit.potionCost;
        potionSource = potSrc;

        trainingHorse = horse;
        rm.Spend(unit.costGold, 0, 0, unit.costFood);
        rm.SpendResin(unit.resinCost);
        trainingMaterials.Clear();
        var stx = TownHallStorage.Instance;
        if (stx != null && unit.steelCost > 0) UpgradeRules.SpendAtLeast(stx, RefinedKind.Steel, QualityTier.Medium, unit.steelCost, trainingMaterials);
        if (stx != null && unit.planksCost > 0) UpgradeRules.SpendAtLeast(stx, RefinedKind.Planks, QualityTier.Medium, unit.planksCost, trainingMaterials);
        trainingParts = unit.partsCost > 0 && rm.SpendParts(unit.partsCost) ? unit.partsCost : 0;
        recruit = r;
        recruit2 = r2;
        currentTrainingUnit = unit;
        trainingTimer = unit.trainTime;
        recruitWait = 0f;
        if (startingFromQueue && offlineUnits > 0 && offlineCredit > 0f)
        {
            // Пока игрока не было, этот боец уже учился (обновление 14)
            float use = Mathf.Min(offlineCredit, Mathf.Max(0f, trainingTimer - 0.05f));
            trainingTimer -= use;
            offlineCredit -= use;
            offlineUnits--;
        }

        pottageHeld = kitchen != null ? portions : 0;
        pottagePending = kitchen != null && !linked;
        int token = ++pottageToken;

        // Кухня не вплотную - порцию несёт житель (повар, курьер Цитадели...). Обучение начнётся, когда принесут.
        if (pottagePending)
        {
            int n = portions;
            Vector2 at = BarracksPos();
            Logistics.Request(new HaulJob
            {
                from = kitchen.Building,
                to = Bld,
                prefer = kitchen.Building,
                onDeliver = () => OnPottageArrived(token, n, at),
                onAbort = loaded => OnPottageFailed(token, n, at)
            });
        }

        // Зелье несёт житель из Цитадели / Хранилища; обучение начнётся, когда принесут
        potionsPending = false;
        int potTok = ++potionToken;
        if (trainingPotions > 0)
        {
            int pn = trainingPotions;
            BuildingInstance pfrom = potSrc != null ? potSrc : (pm != null ? pm.Hall : null);
            potionsPending = true;
            Logistics.Request(new HaulJob
            {
                from = pfrom,
                to = Bld,
                prefer = Bld,
                onDeliver = () => OnPotionsArrived(potTok, pn, potSrc),
                onAbort = loaded => OnPotionsFailed(potTok, pn, potSrc)
            });
        }
    }

    void OnPotionsArrived(int token, int n, BuildingInstance src)
    {
        // Обучение отменили / казарму снесли, пока несли - зелья назад
        if (this == null || token != potionToken || !IsTraining)
        {
            PotionHouse.ReturnPotions(n, src);
            return;
        }
        potionsPending = false;
    }

    void OnPotionsFailed(int token, int n, BuildingInstance src)
    {
        PotionHouse.ReturnPotions(n, src);
        if (this == null || token != potionToken || !IsTraining) return;
        trainingPotions = 0;      // уже вернули выше
        potionsPending = false;
        Fail("Зелье не донесли — обучение отменено");
        CancelTraining(true);
    }

    void OnPottageArrived(int token, int n, Vector2 at)
    {
        // Обучение отменили / казарму снесли, пока несли - порции на Кухню
        if (this == null || token != pottageToken || !IsTraining)
        {
            KitchenController.ReturnPortions(n, at);
            return;
        }
        pottagePending = false;
    }

    void OnPottageFailed(int token, int n, Vector2 at)
    {
        KitchenController.ReturnPortions(n, at);
        if (this == null || token != pottageToken || !IsTraining) return;

        pottageHeld = 0;
        pottagePending = false;
        if (currentTrainingUnit != null && currentTrainingUnit.requiresPottage)
        {
            Fail($"{currentTrainingUnit.unitName}: похлёбку не донесли — обучение отменено");
            CancelTraining(true);
        }
        else Fail("Похлёбку не донесли — боец выйдет без усиления");
    }

    // ---------- Отряды ----------

    private readonly List<Squad> squads = new List<Squad>();
    private int squadCounter = 0;
    public IReadOnlyList<Squad> Squads => squads;

    Squad NewSquad()
    {
        Squad sq = new Squad(this, ++squadCounter);
        squads.Add(sq);
        return sq;
    }

    /// <summary>Загрузка города (SquadSave): отряд номер i по порядку, недостающие создаются.</summary>
    public Squad SquadForRestore(int i)
    {
        while (squads.Count <= i) NewSquad();
        return squads[Mathf.Max(0, i)];
    }

    /// <summary>Загрузка города: перевести бойца в отряд, в котором он был.</summary>
    public void PutInSquad(CitizenAgent a, Squad sq)
    {
        if (a == null || sq == null || a.Squad == sq) return;
        if (a.Squad != null) a.Squad.Remove(a);
        JoinSquad(a, sq);
    }

    // Новый солдат попадает в отряд, который сейчас дома; если таких нет - создаётся новый
    void JoinSquad(CitizenAgent s, Squad sq)
    {
        sq.Add(s);
        if (sq.Order == SoldierOrder.Garrison) s.SetOrder(SoldierOrder.Garrison, Vector2.zero);
    }

    void AssignNewSoldier(CitizenAgent s)
    {
        Squad home = null;
        foreach (var sq in squads)
            if (sq.Order != SoldierOrder.Move && !sq.OnExpedition) { home = sq; break; }
        if (home == null) home = NewSquad();

        home.Add(s);
        if (home.Order == SoldierOrder.Garrison) s.SetOrder(SoldierOrder.Garrison, Vector2.zero);
    }

    void TickSquads(float dt)
    {
        for (int i = squads.Count - 1; i >= 0; i--)
        {
            Squad sq = squads[i];
            sq.Tick(dt);
            if (sq.Members.Count == 0)
            {
                sq.Dispose();
                squads.RemoveAt(i);
            }
        }
    }

    void OnDestroy()
    {
        foreach (var sq in squads) sq.Dispose(); // казарму снесли - лагеря на карте не остаются

        // Снесли во время обучения: золото и еду возвращаем, лошадь - в Конюшню, похлёбку - на Кухню
        // (раньше всё это пропадало)
        if (IsTraining && gameObject.scene.isLoaded) CancelTraining(true);
    }

    // Отряды, к которым относятся отмеченные бойцы (если никто не отмечен - все отряды)
    List<Squad> TargetSquads()
    {
        var result = new List<Squad>();
        foreach (var s in Targets())
            if (s.Squad != null && !result.Contains(s.Squad)) result.Add(s.Squad);
        return result;
    }

    public void FormSquadFromSelected()
    {
        var list = new List<CitizenAgent>();
        foreach (var s in soldiers) if (s != null && selection.Contains(s) && !s.IsOnExpedition) list.Add(s);
        if (list.Count == 0) return;

        Squad sq = NewSquad();
        foreach (var s in list)
        {
            if (s.Squad != null) s.Squad.Remove(s);
            sq.Add(s);
            s.SetOrder(SoldierOrder.Guard, Vector2.zero); // новый отряд сначала охраняет казарму
        }
    }

    float SquadSectionHeight()
    {
        return Mathf.Max(1, squads.Count) * 20f + 4f;
    }

    // ---------- Приказы ----------

    // Кому отдаём приказ: отмеченным галочками, а если никого не отметили - всем
    List<CitizenAgent> Targets()
    {
        var list = new List<CitizenAgent>();
        foreach (var s in soldiers)
            if (s != null && selection.Contains(s)) list.Add(s);
        if (list.Count == 0)
            foreach (var s in soldiers) if (s != null) list.Add(s);
        return list;
    }

    // Кому пойдёт приказ: целый отряд - если отмечены все его бойцы, иначе новый отряд из отмеченных
    List<Squad> SquadsForOrder()
    {
        var result = new List<Squad>();
        var bySquad = new Dictionary<Squad, List<CitizenAgent>>();
        foreach (var s in Targets())
        {
            if (s == null || s.Squad == null || s.IsOnExpedition) continue;
            if (!bySquad.TryGetValue(s.Squad, out var l)) { l = new List<CitizenAgent>(); bySquad[s.Squad] = l; }
            l.Add(s);
        }

        foreach (var kv in bySquad)
        {
            Squad sq = kv.Key;
            bool whole = true;
            foreach (var m in sq.Members)
                if (m != null && !kv.Value.Contains(m)) { whole = false; break; }

            if (whole) { result.Add(sq); continue; }

            Squad ns = NewSquad();
            foreach (var s in kv.Value) { sq.Remove(s); ns.Add(s); }
            result.Add(ns);
        }
        return result;
    }

    public void OrderGarrison()
    {
        foreach (var sq in SquadsForOrder()) sq.GoHome(SoldierOrder.Garrison);
    }

    public void OrderGuard()
    {
        foreach (var sq in SquadsForOrder()) sq.GoHome(SoldierOrder.Guard);
    }

    public void SendSelectedTo(Vector2 point)
    {
        var nav = GridNavigation.Instance;
        if (nav != null && !nav.InBounds(nav.WorldToCell(point)))
        {
            // На войне за краем города - общая карта: бойцы выходят на неё и идут туда
            if (WarView.Embedded)
            {
                var list = new List<CitizenAgent>();
                foreach (var s in Targets()) if (s != null && !s.IsDead && !s.IsOnExpedition && !s.InCombat && s.IsSoldier) list.Add(s);
                PickingTarget = false;
                WarCityMode.SendOut(this, list, point);
            }
            return; // кликнули за картой - ждём новый клик
        }

        // Приказ получает весь отряд: он идёт в точку, ставит лагерь и живёт там на своих припасах.
        // Один клик - идут и по дороге бьют всех, кто встретится; второй клик сразу следом - марш (см. CheckMarchClick).
        sentSquads.Clear();
        foreach (var sq in SquadsForOrder()) { sq.Deploy(point); sentSquads.Add(sq); }
        sentAt = Time.unscaledTime;
        sentFrame = Time.frameCount;
        sentScreen = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

        PickingTarget = false;
    }

    // ---------- Марш (двойной клик) ----------
    // Отряд послали кликом по карте, и сразу же был второй клик рядом - отряд идёт маршем:
    // не отвлекается ни на врагов, ни на лечение, пока не дойдёт до точки. Там - лагерь, как обычно.

    public const float DoubleClickSeconds = 0.35f;   // на компьютере; на телефоне - TouchInput.DoubleSeconds (final-9)
    static float DoubleSec => TouchInput.DoubleSeconds;
    static float DoublePx => TouchInput.DoublePixels;
    readonly List<Squad> sentSquads = new List<Squad>();
    float sentAt = -99f;
    int sentFrame;
    Vector2 sentScreen;

    void CheckMarchClick()
    {
        if (sentSquads.Count == 0) return;
        if (Time.unscaledTime - sentAt > DoubleSec) { sentSquads.Clear(); return; }
        Mouse m = Mouse.current;
        if (m == null || Time.frameCount == sentFrame || !m.leftButton.wasPressedThisFrame) return;
        if ((m.position.ReadValue() - sentScreen).magnitude > DoublePx) { sentSquads.Clear(); return; }
        marchFrame = Time.frameCount;   // этот клик - марш, а не новый приказ (SendSquad его пропустит)

        int n = 0;
        foreach (var sq in sentSquads)
        {
            if (sq == null || sq.Order != SoldierOrder.Move) continue;
            foreach (var s in sq.Members) if (s != null && !s.IsDead) { s.StartMarch(); n++; }
        }
        sentSquads.Clear();
        if (n > 0) WarManager.Notify("Марш: отряд идёт к точке, не ввязываясь в бой", true);
    }

    int marchFrame = -1;

    /// <summary>Прямой приказ с карты (final-8): выбрали бойца, нажали на карту - его отряд идёт туда.
    /// Второе нажатие рядом сразу следом - марш (как из окна казармы). false - точка вне карты.</summary>
    public bool SendSquad(Squad sq, Vector2 point, EnemyAgent foe = null)
    {
        if (sq == null || sq.OnExpedition) return false;
        if (Time.frameCount == marchFrame) return true;   // этот же клик уже стал маршем
        var nav = GridNavigation.Instance;
        if (nav != null && !nav.InBounds(nav.WorldToCell(point)))
        {
            // На войне за краем своего города - общая карта (земли клана, соседи): отряд выходит на неё и идёт туда (final-9).
            // Раньше такой приказ молча пропадал - бойцы «не могли» зайти к соседу по клану
            if (!WarView.Embedded) return false;
            var list = new List<CitizenAgent>();
            foreach (var s in sq.Members) if (s != null && !s.IsDead && !s.IsOnExpedition && s.IsSoldier) list.Add(s);
            if (list.Count == 0) return false;
            WarCityMode.SendOut(this, list, point);
            sentSquads.Clear();
            return true;
        }
        Vector2 scr = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        if (sentSquads.Contains(sq) && Time.frameCount != sentFrame && Time.unscaledTime - sentAt <= DoubleSec
            && (scr - sentScreen).magnitude <= DoublePx)
        {
            int n = 0;
            foreach (var s in sq.Members) if (s != null && !s.IsDead) { s.StartMarch(); n++; }
            sentSquads.Clear();
            if (n > 0) WarManager.Notify("Марш: отряд идёт к точке, не ввязываясь в бой", true);
            return true;
        }
        sq.Deploy(point);
        // Нажали по врагу - отряд бьёт именно его, а не первого встречного (final-9)
        foreach (var s in sq.Members) if (s != null && !s.IsDead) s.Hunt(foe);
        sentSquads.Clear();
        sentSquads.Add(sq);
        sentAt = Time.unscaledTime;
        sentFrame = Time.frameCount;
        sentScreen = scr;
        return true;
    }

    public void Dismiss(CitizenAgent a)
    {
        if (a == null) return;
        if (a.IsOnExpedition)
        {
            WarManager.Notify($"{a.CitizenName} в походе — уволить можно после возвращения", false);
            return;
        }
        ShieldBearer.ReturnToArmory(a);   // щит - на склад Оружейной (оружие вернёт сам Discharge)
        a.Discharge();
        soldiers.Remove(a);
        selection.Remove(a);
    }

    public void DismissSelected()
    {
        var list = new List<CitizenAgent>();
        foreach (var s in soldiers) if (s != null && selection.Contains(s)) list.Add(s);
        foreach (var s in list) Dismiss(s);
    }

    // ---------- Снаряжение ----------

    private CitizenAgent equipFor;      // чьё снаряжение открыто в боковой панели
    private Vector2 equipScroll;
    private Vector2 shieldScroll;
    private Vector2 bodyScroll;

    // Выдать бойцу оружие со склада Оружейной (старое оружие бойца уходит на склад)
    bool EquipFrom(CitizenAgent s, ArmoryController from, Weapon w)
    {
        if (s == null || from == null || w == null || !s.CanUseWeapon || w.kind != s.WeaponKindAllowed) return false;
        if (w.kind == WeaponKind.Shot) return BringShot(s, from, w);   // ядро пушке несут из Оружейной
        if (!CanGear(s)) return false;
        if (!from.RemoveFromStock(w)) return false;

        Weapon old = s.UnequipWeapon();
        if (old != null && !ArmoryController.Stash(old))
        {
            // Старое оружие некуда положить - ничего не меняем
            s.EquipWeapon(old);
            from.AddToStock(w);
            WarManager.Notify("Оружейная: нет места для старого оружия", false);
            return false;
        }
        return s.EquipWeapon(w);
    }

    // Бронебойное ядро полевой пушке: житель несёт его из Оружейной в казарму, пушка ждёт у казармы.
    // Старое ядро уходит на склад, когда принесли новое.
    bool BringShot(CitizenAgent s, ArmoryController from, Weapon w)
    {
        if (cargoFor.Contains(s)) return false;
        if (!AtHome(s))
        {
            WarManager.Notify($"{s.UnitName}: ядро несут в казарму — пусть пушка вернётся к казарме", false);
            return false;
        }
        if (!from.RemoveFromStock(w)) return false;
        cargoFor.Add(s);
        BarracksController self = this;
        WarManager.Notify($"{s.UnitName}: из Оружейной несут {w.Name.ToLower()}", true);
        int carry = CarryLedger.Add(w);
        Logistics.Request(new HaulJob
        {
            from = from.Building,
            to = Bld,
            prefer = Bld,
            onDeliver = () =>
            {
                CarryLedger.Done(carry);
                if (self != null) self.cargoFor.Remove(s);
                if (self == null || s == null || s.IsDead || !s.IsSoldier || s.IsOnExpedition) { StashOrLose(w); return; }
                Weapon old = s.UnequipWeapon();
                if (!s.EquipWeapon(w)) { if (old != null) s.EquipWeapon(old); StashOrLose(w); return; }
                if (old != null) StashOrLose(old);
                WarManager.Notify($"{s.UnitName} {s.CitizenName}: заряжено {w.Name.ToLower()}", true);
            },
            onAbort = loaded =>
            {
                CarryLedger.Done(carry);
                if (self != null) self.cargoFor.Remove(s);
                StashOrLose(w);
            }
        });
        return true;
    }

    static void StashOrLose(Weapon w)
    {
        if (w != null && !ArmoryController.Stash(w)) WarManager.Notify($"В Оружейных нет места: {w.Name.ToLower()} потеряно", false);
    }

    // Выдать бойцу щит со склада Оружейной (старый щит уходит на склад)
    bool EquipShieldFrom(CitizenAgent s, ArmoryController from, Shield sh)
    {
        if (s == null || from == null || sh == null || !UnitUsesShield(s) || !CanGear(s)) return false;
        if (!from.RemoveShield(sh)) return false;

        Shield old = ShieldBearer.Unequip(s);
        if (old != null && !ArmoryController.StashShield(old))
        {
            ShieldBearer.Equip(s, old);
            from.AddShield(sh);
            WarManager.Notify("Оружейная: нет места для старого щита", false);
            return false;
        }

        if (!ShieldBearer.Equip(s, sh))
        {
            from.AddShield(sh);
            return false;
        }
        return true;
    }

    // Выдать бойцу броню или плащ со склада Оружейной (прежняя вещь уходит на склад)
    bool EquipBodyFrom(CitizenAgent s, ArmoryController from, BodyGear g)
    {
        if (s == null || from == null || g == null || !s.CanWearBody || !CanGear(s)) return false;
        if (!from.RemoveBody(g)) return false;

        BodyGear old = s.UnequipBody();
        if (old != null && !ArmoryController.StashBody(old))
        {
            s.EquipBody(old);
            from.AddBody(g);
            WarManager.Notify("Оружейная: нет места для прежней брони / плаща", false);
            return false;
        }
        if (!s.EquipBody(g))
        {
            from.AddBody(g);
            return false;
        }
        return true;
    }

    // Что лучше надеть бойцу: разведчику и стрелкам - плащ, бойцам ближнего боя - броню
    static BodyKind PreferredBody(CitizenAgent s)
    {
        return s.IsScout || s.ExpRanged ? BodyKind.Cloak : BodyKind.Armor;
    }

    // Лучшая броня / плащ для бойца (сначала - предпочтительного вида). found = false - на складах пусто.
    static ArmoryController.BodyEntry BestBodyFor(CitizenAgent s, out bool found)
    {
        var opts = ArmoryController.AvailableBodies();
        found = opts.Count > 0;
        if (!found) return new ArmoryController.BodyEntry();
        BodyKind want = PreferredBody(s);
        foreach (var o in opts) if (o.gear.kind == want) return o;
        return opts[0];
    }

    // Каждому бойцу без оружия / щита - лучшее из доступного
    void AutoEquipAll()
    {
        int given = 0, away = 0;
        foreach (var s in soldiers)
        {
            if (s == null) continue;
            if (!CanGear(s))
            {
                if ((s.CanUseWeapon && s.EquippedWeapon == null) || (UnitUsesShield(s) && ShieldBearer.ShieldOf(s) == null) || (s.CanWearBody && s.EquippedBody == null)) away++;
                continue;   // не в казарме - снаряжение не передать (обновление 14)
            }

            if (s.CanUseWeapon && s.EquippedWeapon == null)
            {
                var opts = ArmoryController.Available(s.WeaponKindAllowed);
                if (opts.Count > 0 && EquipFrom(s, opts[0].armory, opts[0].weapon)) given++;
            }

            if (UnitUsesShield(s) && ShieldBearer.ShieldOf(s) == null)
            {
                var sopts = ArmoryController.AvailableShields();
                if (sopts.Count > 0 && EquipShieldFrom(s, sopts[0].armory, sopts[0].shield)) given++;
            }

            if (s.CanWearBody && s.EquippedBody == null)
            {
                var bo = BestBodyFor(s, out bool found);
                if (found && EquipBodyFrom(s, bo.armory, bo.gear)) given++;
            }
        }
        string awayText = away > 0 ? $". Не в казарме: {away} — выдайте, когда вернутся" : "";
        WarManager.Notify((given > 0 ? $"Казарма: выдано снаряжения — {given}" : "Казарма: нет бойцов без снаряжения или нет подходящих предметов") + awayText, given > 0);
    }

    // ---------- Интерфейс ----------

    // Вкладки панели: 0 - Обучение, 1 - Войска, 2 - Снаряжение
    private int tab = 0;
    private Vector2 gearListScroll;

    const float UnitRowH = 32f;      // строка вида войск
    const float SoldierRowH = 26f;   // строка бойца в списке
    const float ListH = 150f;        // высота прокручиваемых списков
    const float EquipRowH = 28f;     // строка предмета в Оружейной

    static GUIStyle Mid(GUIStyle from, bool wrap)
    {
        return new GUIStyle(from) { alignment = TextAnchor.MiddleLeft, wordWrap = wrap, clipping = TextClipping.Clip };
    }

    string TypeSummary()
    {
        var counts = new Dictionary<string, int>();
        foreach (var s in soldiers)
        {
            if (s == null) continue;
            counts.TryGetValue(s.DisplayName, out int c);
            counts[s.DisplayName] = c + 1;
        }
        if (counts.Count == 0) return "Бойцов нет";

        var parts = new List<string>();
        foreach (var kv in counts) parts.Add($"{kv.Key}: {kv.Value}");
        return string.Join("\n", parts);
    }

    public void DrawMarkers()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        GUIStyle st = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        foreach (var s in soldiers)
        {
            if (s == null || s.IsInsideBuilding) continue;
            bool mark = selection.Contains(s) || (s == focused && Time.unscaledTime < focusUntil);
            if (!mark) continue;

            Vector3 sp = cam.WorldToScreenPoint(s.transform.position + Vector3.up * 0.7f);
            if (sp.z < 0f) continue;

            Rect rc = new Rect(UiKit.ToGui(sp).x - 15f, UiKit.ToGui(sp).y - 15f, 30f, 30f);
            st.normal.textColor = Color.black;
            LocGUI.Label(new Rect(rc.x + 1f, rc.y + 1f, rc.width, rc.height), "▼", st);
            st.normal.textColor = UiKit.Accent;
            LocGUI.Label(rc, "▼", st);
        }
    }

    /// <summary>
    /// Пока игрок выбирает точку на карте ("Послать..."), большая панель казармы прячется,
    /// остаётся маленькая плашка с подсказкой и кнопкой отмены. Метки выбранных бойцов остаются.
    /// </summary>
    public Rect DrawPickBanner(float x, float y, float w)
    {
        Rect r = new Rect(x, y, w, 66f);
        LocGUI.Box(r, GUIContent.none);
        LocGUI.Label(new Rect(x + 10f, y + 6f, w - 20f, 22f),
            UiKit.B(UiKit.Col(UiKit.AccentHex, "Куда послать отряд?")) + "  " + UiKit.Col(UiKit.MutedHex, "клик — с боем по пути, двойной клик — марш"), UiKit.Body);
        if (UiKit.Button(new Rect(x + 10f, y + 32f, w - 20f, 26f), "Отмена", UiKit.Kind.Ghost, "Esc")) PickingTarget = false;
        return r;
    }

    // Строка про похлёбку под галочкой
    string PottageInfoLine()
    {
        int avail = KitchenController.PortionsAvailable;
        if (KitchenController.All.Count == 0) return UiKit.Col(UiKit.MutedHex, "Кухни нет");
        string link = KitchenController.AnyLinked(Bld)
            ? UiKit.Col(UiKit.GoodHex, "кухня вплотную")
            : UiKit.Col(UiKit.WarnHex, "кухня не рядом — несут курьеры");
        return $"Похлёбки: <b>{avail}</b> порц. · {link}";
    }

    // ---------- Вкладка «Обучение» ----------

    List<UnitType> UnlockedUnits()
    {
        return availableUnits != null ? availableUnits.FindAll(u => u != null && Unlocked(u)) : new List<UnitType>();
    }

    bool AnyPottageUnit(List<UnitType> units)
    {
        foreach (var u in units) if (u.UsesPottage) return true;
        return false;
    }

    // Ближайший уровень казармы, на котором откроются новые бойцы (0 - больше нечего открывать)
    int NextUnlockLevel(out string names)
    {
        names = "";
        int lvl = Bld != null ? Bld.level : 1;
        int next = 0;
        if (availableUnits == null) return 0;
        foreach (var u in availableUnits)
            if (u != null && !Unlocked(u) && DevelopmentPath.Allows(u.path) && DevelopmentPath.EngAllows(u.engSchool) && u.minBarracksLevel > lvl && (next == 0 || u.minBarracksLevel < next))
                next = u.minBarracksLevel;
        if (next == 0) return 0;
        var list = new List<string>();
        foreach (var u in availableUnits)
            if (u != null && u.minBarracksLevel == next && DevelopmentPath.Allows(u.path) && DevelopmentPath.EngAllows(u.engSchool)) list.Add(u.unitName);
        names = string.Join(", ", list);
        return next;
    }

    float TrainTabHeight()
    {
        var units = UnlockedUnits();
        float h = Mathf.Max(1, units.Count) * UnitRowH + 4f + QueueRowH;
        if (NextUnlockLevel(out _) > 0) h += 20f;
        if (AnyPottageUnit(units)) h += 6f + 22f + 20f;
        h += 10f + UpgradeRules.SectionHeight(Bld);
        return h;
    }

    // Подсказка к виду войск: характеристики и особенности
    string UnitTip(UnitType u)
    {
        string t = $"<b>{u.unitName}</b>\n";
        t += $"Здоровье {u.SoldierHp:F0} · урон {u.SoldierDamage:F0} · удар раз в {u.attackCooldown:0.#} с\n";
        t += $"Скорость {u.moveSpeed:0.#} · {(u.attackRange > 2f ? "бьёт издалека" : "ближний бой")}";
        if (u.IsHealer) t += $"\nЛечит своих рядом в бою ({u.healPerSecond:0.#} в сек.). Несколько магов в одного бойца подряд лечат слабее: 100%, 60%, 35%, 15%";
        if (u.unitName == RaidManager.ScoutUnitName) t += "\nБыстрый, видит дальше. Нужен для разведки перед набегом.";
        if (u.IsTaunter) t += "\nБашни и пушки врага бьют его первым, пока он у них в радиусе. Пусть принимает огонь на себя, а остальные в это время атакуют.";
        if (u.IsPoisoner) t += "\nЯдовитые снаряды по площади. Сила яда — от маны в воздухе.";
        if (u.IsElementalist) t += "\nИногда огненный шар по площади; у реки — волна, долго замедляет врагов. Сила — от маны." +
                                   (DevelopmentPath.IsChaos ? $"\n<color=#C9A6FF>Ядро хаоса</color> (берёт сам в казарме): раз в {ChaosMagic.CooldownSeconds:F0} с чёрная дыра — стягивает врагов." : "");
        if (u.IsFireBomber) t += $"\nВыплёскивает ведро горящей смолы по площади, потом бьётся кулаками. Ведро заправляется в казарме ({RefillResin} смолы).";
        if (u.IsCatapult) t += $"\nКамни навесом через стены, удар по площади (радиус {u.catapultRadius:0.#}). Дальность {u.catapultMinRange:0}–{u.attackRange:0}: " +
                                $"вблизи урон {u.damage:F0}, на пределе — {u.damage * u.catapultFarMul:F0}. По зданиям в набеге ×{u.catapultBuildingMul:0.#}.\n" +
                                "Расчёт — один житель. В лагере («Послать...») стоит на месте и бьёт всё в радиусе.";
        if (u.IsRam) t += $"\nОдна боевая единица: двое жителей и таран (второй сидит «в таране»). Удар {u.ramDamage:F0} раз в {u.ramCooldown:0.#} с — по стенам, воротам и заслонам ×{u.ramBarrierMul:0.#}, по зданиям ×{u.ramBuildingMul:0.#}. " +
                          $"По людям не бьёт. Хлипкий (прочность {u.ramHp:F0}), сам идёт к стенам, если не дать другой приказ.\n" +
                          $"Сломан — носильщики выпадают и дерутся ножами (здоровье {u.SoldierHp:F0}, урон {u.SoldierDamage:F0}). Новый таран собирают в казарме: {u.ramRebuildPlanks} досок.\n" +
                          "И в набеге, и дома: в обороне ломает заслоны нападающего и лагерь войска соседа. Побитый таран чинят в казарме.\n" +
                          "Носильщикам можно выдать меч и броню (не щит); их броня прибавляется к броне тарана.";
        if (u.IsFieldCannon) t += "\nЯдра прямой наводкой: лучшая против стен, ворот и зданий; осколки ранят тех, кто за целью. Модули и снаряды берёт сама; вид снарядов — в «Снаряж.».";
        if (u.requiresHorse) t += $"\nНужна лошадь из Конюшни (свободно: {StableController.HorsesAvailable}). Гибнет вместе со всадником.";
        if (u.UsesPottage) t += $"\nС похлёбкой ({u.PottagePortions} порц.) выходит усиленным ★.";
        if (u.usesWeapon || u.usesShield)
        {
            string kind = u.weaponKind == WeaponKind.Bow ? "лук" : (u.weaponKind == WeaponKind.Shot ? "бронебойное ядро" : "меч");
            t += "\nСнаряжение из Оружейной: " + (u.usesWeapon ? kind : "") + (u.usesWeapon && u.usesShield ? ", " : "") + (u.usesShield ? "щит" : "");
        }
        if (u.IsRam) t += $"\nНужно {u.Crew} свободных жителя.";
        if (!u.isCannon && !u.isCatapult && (DevelopmentPath.IsHeavy || DevelopmentPath.IsCamo))
            t += "\nНа тело: " + (DevelopmentPath.IsHeavy ? "броня (−25…36% урона, −10% скорости)" : "плащ из листвы (замечают вдвое ближе)");
        t += $"\nОбучение {u.trainTime:0.#} с · мест в казарме: {u.TrainSpace}";
        return t;
    }

    // Дополнительные затраты (кроме золота и еды); missing - чего-то не хватает
    string UnitExtras(UnitType u, int portions, out bool missing)
    {
        missing = false;
        var parts = new List<string>();
        if (u.requiresHorse)
        {
            parts.Add($"конь ({StableController.HorsesAvailable})");
            if (StableController.HorsesAvailable <= 0) missing = true;
        }
        if (portions > 0)
        {
            parts.Add($"{portions} пох.");
            if (KitchenController.PortionsAvailable < portions) missing = true;
        }
        if (u.potionCost > 0)
        {
            parts.Add($"{u.potionCost} зел.");
            if (PotionHouse.TotalPotions < u.potionCost) missing = true;
        }
        if (u.resinCost > 0)
        {
            parts.Add($"{u.resinCost} смолы");
            if (ResourceManager.Instance == null || ResourceManager.Instance.resin < u.resinCost) missing = true;
        }
        if (u.Crew > 1)
        {
            parts.Add($"{u.Crew} жит.");
            var pm0 = PopulationManager.Instance;
            if (pm0 != null && pm0.FreePeople < u.Crew) missing = true;
        }
        if (u.steelCost > 0) parts.Add($"{u.steelCost} стали");
        if (u.planksCost > 0) parts.Add($"{u.planksCost} досок");
        if (u.partsCost > 0)
        {
            parts.Add($"{u.partsCost} дет.");
            if (ResourceManager.Instance == null || ResourceManager.Instance.parts < u.partsCost) missing = true;
        }
        return string.Join(", ", parts);
    }

    // Почему нельзя обучить прямо сейчас ("" - можно)
    string TrainProblem(UnitType u, int portions, bool busy = true)
    {
        if (Bld != null && !Bld.isWorking) return Title + " не работает: " + Bld.StatusText;
        if (busy && IsTraining) return Title + " уже обучает: " + CurrentTrainingName;
        if (u.partsCost > 0 && (ResourceManager.Instance == null || ResourceManager.Instance.parts < u.partsCost)) return $"Мало деталей: нужно {u.partsCost} (Литейная)";
        if (u.requiresHorse && StableController.HorsesAvailable <= 0) return "Нет свободных лошадей в Конюшне";
        if (portions > 0 && KitchenController.PortionsAvailable < portions) return $"Мало похлёбки: нужно {portions} порц.";
        if (u.potionCost > 0 && PotionHouse.TotalPotions < u.potionCost) return $"Мало зелий: нужно {u.potionCost} (Зельеварня)";
        if (u.resinCost > 0 && (ResourceManager.Instance == null || ResourceManager.Instance.resin < u.resinCost)) return $"Мало смолы: нужно {u.resinCost}";
        var pmc = PopulationManager.Instance;
        if (u.Crew > 1 && pmc != null && pmc.FreePeople < u.Crew) return $"Нужно {u.Crew} свободных жителя";
        return "";
    }

    float DrawTrainTab(float x, float y, float w)
    {
        float cy = y;
        var units = UnlockedUnits();
        GUIStyle nameSt = Mid(UiKit.Body, false);
        GUIStyle costSt = Mid(UiKit.Small, true);
        GUIStyle infoSt = Mid(UiKit.MutedStyle, true);
        infoSt.fontSize = 11;

        const float btnW = 82f, infoW = 46f, nameW = 128f, iconW = UnitRowH;
        float costW = w - iconW - nameW - infoW - btnW - 12f;

        cy = DrawQueue(x, cy, w);

        if (units.Count == 0)
        {
            string none = IsEngineer && !DevelopmentPath.IsMilitaryEng ? "Нужно направление инженеров «Военный инженер»" : "Нет доступных бойцов";
            LocGUI.Label(new Rect(x, cy, w, UnitRowH), UiKit.Col(UiKit.MutedHex, none), nameSt);
            cy += UnitRowH;
        }

        foreach (var unit in units)
        {
            int portions = PottageNeed(unit);
            string star = unit.UsesPottage ? UiKit.Col(UiKit.AccentHex, " ★") : "";
            Rect iconR = new Rect(x, cy + 1f, iconW - 4f, UnitRowH - 4f);
            UnitIcons.Draw(iconR, unit);
            UiKit.Tip(iconR, UnitTip(unit));
            Rect nameR = new Rect(x + iconW, cy, nameW, UnitRowH - 2f);
            LocGUI.Label(nameR, $"<b>{unit.unitName}</b>{star}", nameSt);
            UiKit.Tip(nameR, UnitTip(unit));

            string extras = UnitExtras(unit, portions, out bool missing);
            string cost = UiKit.Cost(unit.costGold, 0, 0, unit.costFood);
            if (extras != "") cost += "\n" + UiKit.Col(missing ? UiKit.WarnHex : UiKit.MutedHex, "+ " + extras);
            Rect costR = new Rect(x + iconW + nameW + 4f, cy, costW, UnitRowH - 2f);
            LocGUI.Label(costR, cost, costSt);
            if (extras != "") UiKit.Tip(costR, UnitTip(unit));

            Rect infoR = new Rect(x + iconW + nameW + costW + 8f, cy, infoW, UnitRowH - 2f);
            LocGUI.Label(infoR, $"{unit.trainTime:0.#} с\n{unit.TrainSpace} м.", infoSt);
            UiKit.Tip(infoR, $"Обучение {unit.trainTime:0.#} с · мест в казарме: {unit.TrainSpace}");

            string problem = TrainProblem(unit, portions);
            bool queue = IsTraining || trainQueue.Count > 0;
            if (queue)
            {
                // Казарма занята: в очередь можно, даже если сейчас чего-то не хватает - подождёт
                bool full = trainQueue.Count >= QueueMax || (Bld != null && !Bld.isWorking);
                string qp = TrainProblem(unit, portions, false);
                string tip = full ? (trainQueue.Count >= QueueMax ? $"Очередь полна ({QueueMax})" : problem)
                                  : $"Поставить в очередь: {unit.unitName}. Ресурсы возьмём, когда начнётся обучение"
                                    + (qp != "" ? "\nСейчас не хватает: " + qp : "");
                if (UiKit.Button(new Rect(x + w - btnW, cy + 2f, btnW, UnitRowH - 6f), "+ Очередь", !full, UiKit.Kind.Normal, tip))
                    TrainOrQueue(unit);
            }
            else if (UiKit.Button(new Rect(x + w - btnW, cy + 2f, btnW, UnitRowH - 6f), "Обучить", problem == "", UiKit.Kind.Primary,
                    problem == "" ? $"Обучить: {unit.unitName}" : problem))
                TrainOrQueue(unit);

            UiKit.Line(x, cy + UnitRowH - 1f, w);
            cy += UnitRowH;
        }
        cy += 4f;

        // Какие бойцы откроются дальше
        int next = NextUnlockLevel(out string nextNames);
        if (next > 0)
        {
            Rect nr = new Rect(x, cy, w, 20f);
            LocGUI.Label(nr, UiKit.Col(UiKit.MutedHex, $"Ещё бойцы — с Ур. {next} казармы"), UiKit.Small);
            UiKit.Tip(nr, "Откроются: " + nextNames);
            cy += 20f;
        }

        // --- Похлёбка (только если есть бойцы, которым её дают) ---
        if (AnyPottageUnit(units))
        {
            cy += 6f;
            Rect tr = new Rect(x, cy, w, 22f);
            bool usePot = LocGUI.Toggle(tr, UsePottage, " <b>С похлёбкой</b> " + UiKit.Col(UiKit.AccentHex, "★"));
            UiKit.Tip(tr, "Любые бойцы ★ выходят усиленными: +35% здоровья, +25% урона (у лекаря — лечения), +10% скорости.\n" +
                          "Мелким бойцам — 1 порция, крупным (от 3 мест) и коннице — 2.\n" +
                          "Порции берутся с Кухни. Кухня вплотную к казарме (или к группе казарм) — подаётся сразу, иначе порцию несёт житель, и обучение ждёт.");
            if (usePot != UsePottage) UsePottage = usePot;
            cy += 22f;
            LocGUI.Label(new Rect(x + 18f, cy, w - 18f, 20f), PottageInfoLine(), UiKit.Small);
            cy += 20f;
        }

        // --- Улучшение казармы ---
        cy += 10f;
        UiKit.Line(x, cy - 5f, w);
        cy += UpgradeRules.DrawUpgradeSection(x, cy, w, Bld);
        return cy - y;
    }

    // ---------- Вкладка «Войска» ----------

    float ArmyTabHeight()
    {
        return UiKit.SectionH + SquadSectionHeight() + 24f + ListH + 6f + UiKit.SectionH + 32f + 32f + 30f
            + (WarCityMode.Active ? 32f + WarFieldSoldiers.SectionHeight(this) : 0f);   // klan-voina-14: «На карте войны»
    }

    void FindSoldier(CitizenAgent s)
    {
        focused = s;
        focusUntil = Time.unscaledTime + 5f;
        Vector2 p = s.IsInsideBuilding && Bld != null ? Bld.Center : (Vector2)s.transform.position;
        if (CameraController.Instance != null) CameraController.Instance.FocusOn(p);
    }

    float DrawArmyTab(float x, float y, float w)
    {
        float cy = y;
        GUIStyle small = UiKit.Small;
        GUIStyle toggle = new GUIStyle(GUI.skin.toggle) { fontSize = 12, richText = true, wordWrap = false, clipping = TextClipping.Clip };

        // --- Отряды ---
        cy = UiKit.Section(x, cy, w, "Отряды");
        UiKit.Tip(new Rect(x, cy - UiKit.SectionH, w, UiKit.SectionH),
            "Новые бойцы встают в отряд, который дома. Приказ получает весь отряд; если отмечена только часть, из неё делается новый отряд.");
        if (squads.Count == 0)
            LocGUI.Label(new Rect(x, cy, w, 20f), UiKit.Col(UiKit.MutedHex, "Отрядов пока нет"), small);
        for (int i = 0; i < squads.Count; i++)
        {
            Squad sq = squads[i];
            LocGUI.Label(new Rect(x, cy + i * 20f, w, 20f), $"<b>{sq.Name}</b> ({sq.Members.Count}) " + UiKit.Col(UiKit.MutedHex, "— " + sq.StatusText), Mid(small, false));
        }
        cy += SquadSectionHeight();

        // --- Список бойцов ---
        LocGUI.Label(new Rect(x, cy, w - 130f, 22f), $"<b>Бойцы: {soldiers.Count}</b>  " + UiKit.Col(UiKit.MutedHex, $"отмечено: {selection.Count}"), Mid(UiKit.Body, false));
        if (UiKit.Button(new Rect(x + w - 122f, cy, 56f, 22f), "Все", UiKit.Kind.Normal, "Отметить всех"))
            foreach (var s in soldiers) selection.Add(s);
        if (UiKit.Button(new Rect(x + w - 62f, cy, 62f, 22f), "Сброс", UiKit.Kind.Ghost, "Снять отметки"))
            selection.Clear();
        cy += 24f;

        // Таран - одна строка на расчёт: напарник, сидящий в таране, отдельно не показывается
        var rows = new List<CitizenAgent>();
        foreach (var s in soldiers)
            if (s != null && !(s.RidingRam && soldiers.Contains(s.RamHostAgent))) rows.Add(s);

        float innerW = w - 14f;
        scroll = UiKit.BeginScrollView(new Rect(x, cy, w, ListH), scroll,
            new Rect(0f, 0f, innerW, Mathf.Max(ListH, rows.Count * SoldierRowH)));

        if (soldiers.Count == 0)
            LocGUI.Label(new Rect(0f, 0f, innerW, 22f), UiKit.Col(UiKit.MutedHex, "Бойцов нет — обучите их на вкладке «Обучение»"), small);

        CitizenAgent toDismiss = null;
        for (int i = 0; i < rows.Count; i++)
        {
            CitizenAgent s = rows[i];
            float ry = i * SoldierRowH;
            CitizenAgent rider = s.HomeRamOn ? s.RamRiderAgent : null;

            bool sel = selection.Contains(s);
            float hpFrac = s.MaxHp > 0f ? s.Hp / s.MaxHp : 1f;
            string hp = UiKit.Col(hpFrac < 0.35f ? UiKit.BadHex : (hpFrac < 0.7f ? UiKit.WarnHex : UiKit.MutedHex), $"{Mathf.CeilToInt(s.Hp)}/{Mathf.CeilToInt(s.MaxHp)}");
            if (s.HomeRamOn)
            {
                // Целый таран: вместо здоровья носильщика - прочность тарана
                float rf = 1f - s.RamWear;
                hp = UiKit.Col(rf < 0.35f ? UiKit.BadHex : (rf < 0.7f ? UiKit.WarnHex : UiKit.MutedHex), $"таран {Mathf.RoundToInt(rf * 100f)}%");
            }
            UnitIcons.Draw(new Rect(0f, ry + 1f, 22f, 22f), s.ExpUnit);
            Rect tr = new Rect(24f, ry, innerW - 174f, 24f);
            string who = rider != null ? " " + UiKit.Col(UiKit.MutedHex, $"({s.RamCrewNames})") : "";
            bool nsel = LocGUI.Toggle(tr, sel, $" [{(s.Squad != null ? s.Squad.Number : 0)}] {s.DisplayName}{who} {hp} " + UiKit.Col(UiKit.MutedHex, s.StatusText), toggle);
            if (nsel != sel)
            {
                if (nsel) { selection.Add(s); if (rider != null) selection.Add(rider); }
                else { selection.Remove(s); if (rider != null) selection.Remove(rider); }
            }
            string chaosLine = s.HasChaosCore ? "\n" + UiKit.Col("#C9A6FF", s.ChaosCoreText) : "";
            string cannonLine = s.IsFieldCannon ? "\n" + CannonGearLine(s) : "";
            string ramLine = rider != null
                ? $"\nОдин таран на двоих: {s.RamCrewNames}. Здоровье {rider.CitizenName}: {Mathf.CeilToInt(rider.Hp)}/{Mathf.CeilToInt(rider.MaxHp)}" +
                  "\nПока таран цел, бьют по нему. Разобьют — оба выпадут и будут драться ножами."
                : "";
            UiKit.Tip(tr, $"<b>{s.DisplayName}</b> {s.CitizenName}\nЗдоровье {Mathf.CeilToInt(s.Hp)}/{Mathf.CeilToInt(s.MaxHp)}\n{s.StatusText}{chaosLine}{cannonLine}{ramLine}");

            bool canGear = s.CanUseWeapon || UnitUsesShield(s) || ShowBody(s);
            bool geared = s.EquippedWeapon != null || ShieldBearer.ShieldOf(s) != null || s.EquippedBody != null;
            if (UiKit.Button(new Rect(innerW - 146f, ry, 66f, 24f), geared ? "Снаряж.✓" : "Снаряж.", canGear, UiKit.Kind.Normal,
                    canGear ? "Оружие, щит, броня или плащ бойца" : "Этому бойцу снаряжение не положено"))
            {
                equipFor = s;
                tab = 2;
            }

            if (UiKit.Button(new Rect(innerW - 78f, ry, 50f, 24f), "Найти", UiKit.Kind.Normal, "Показать на карте"))
                FindSoldier(s);
            if (UiKit.Button(new Rect(innerW - 26f, ry, 26f, 24f), "×", UiKit.Kind.Danger, "Уволить со службы")) toDismiss = s;
        }
        GUI.EndScrollView();
        cy += ListH + 6f;

        if (toDismiss != null)
        {
            CitizenAgent who = toDismiss;
            CitizenAgent mate = who.HomeRamOn ? who.RamRiderAgent : null;
            if (mate != null)
                ConfirmDialog.Ask($"Уволить расчёт тарана ({who.RamCrewNames})?", "Оба носильщика уйдут со службы и снова станут жителями. Оружие вернётся в Оружейную, таран разберут.", "Уволить",
                    () => { Dismiss(mate); Dismiss(who); });
            else
                ConfirmDialog.Ask($"Уволить {who.CitizenName}?", "Боец уйдёт со службы и снова станет жителем. Оружие и щит вернутся в Оружейную.", "Уволить", () => Dismiss(who));
        }

        // --- Приказы ---
        float secY = cy;
        cy = UiKit.Section(x, cy, w, "Приказ");
        var rightMuted = new GUIStyle(UiKit.MutedStyle) { alignment = TextAnchor.LowerRight, wordWrap = false, fontSize = 11 };
        LocGUI.Label(new Rect(x, secY, w, UiKit.SectionH - 4f), selection.Count > 0 ? $"отмеченным ({selection.Count})" : "всем (никто не отмечен)", rightMuted);

        float bw = (w - 8f) / 3f;
        if (UiKit.Button(new Rect(x, cy, bw, UiKit.BtnH), "Внутрь", UiKit.Kind.Normal, "Зайти в казарму и сидеть внутри")) { PickingTarget = false; OrderGarrison(); }
        if (UiKit.Button(new Rect(x + 4f + bw, cy, bw, UiKit.BtnH), "Охранять", UiKit.Kind.Normal, "Охранять казарму вокруг неё")) { PickingTarget = false; OrderGuard(); }
        if (UiKit.Button(new Rect(x + 8f + 2f * bw, cy, bw, UiKit.BtnH), PickingTarget ? "Отмена" : "Послать...", soldiers.Count > 0 || PickingTarget,
                PickingTarget ? UiKit.Kind.Ghost : UiKit.Kind.Primary,
                soldiers.Count > 0 ? "Кликнуть по карте: отряд пойдёт туда, разобьёт лагерь и будет жить на своих припасах (Esc — отмена)" +
                    (IsEngineer ? "\nКатапульты там встанут и будут бить всё в радиусе, не сходя с места." : "") : "Нет бойцов"))
            PickingTarget = !PickingTarget && soldiers.Count > 0;
        cy += 32f;

        // --- Поведение отряда в лагере ---
        List<Squad> tsq = TargetSquads();
        bool allPatrol = tsq.Count > 0, allHold = tsq.Count > 0;
        foreach (var q in tsq)
        {
            if (q.PatrolAtCamp) allHold = false; else allPatrol = false;
        }

        float hw = (w - 4f) / 2f;
        Rect holdR = new Rect(x, cy, hw, UiKit.BtnH);
        if (LocGUI.Toggle(holdR, allHold, "Лагерь: стоять", GUI.skin.button) != allHold)
            foreach (var q in tsq) q.SetPatrol(false);
        UiKit.Tip(holdR, "В лагере бойцы стоят на месте");
        Rect patrolR = new Rect(x + 4f + hw, cy, hw, UiKit.BtnH);
        if (LocGUI.Toggle(patrolR, allPatrol, "Лагерь: патруль", GUI.skin.button) != allPatrol)
            foreach (var q in tsq) q.SetPatrol(true);
        UiKit.Tip(patrolR, "В лагере бойцы обходят окрестности");
        cy += 32f;

        bool any = selection.Count > 0;
        if (UiKit.Button(new Rect(x, cy, hw, UiKit.BtnH), "Новый отряд", any, UiKit.Kind.Normal,
                any ? "Сформировать новый отряд из отмеченных" : "Сначала отметьте бойцов галочками"))
            FormSquadFromSelected();
        if (UiKit.Button(new Rect(x + 4f + hw, cy, hw, UiKit.BtnH), "Уволить отмеченных", any, UiKit.Kind.Danger,
                any ? "Уволить отмеченных со службы" : "Сначала отметьте бойцов галочками"))
            ConfirmDialog.Ask($"Уволить отмеченных: {selection.Count}?", "Бойцы уйдут со службы и снова станут жителями. Оружие и щиты вернутся в Оружейную.", "Уволить", DismissSelected);
        cy += 30f;

        // --- Военный город: выходить ли на карту самим ---
        if (WarCityMode.Active)
        {
            bool hold = HoldFromWarMap;
            Rect autoR = new Rect(x, cy, hw, UiKit.BtnH);
            if (LocGUI.Toggle(autoR, !hold, "Сами на карту", GUI.skin.button) == hold) HoldFromWarMap = false;
            UiKit.Tip(autoR, "Обученные бойцы сами выходят на общую карту войны и защищают город там");
            Rect holdHomeR = new Rect(x + 4f + hw, cy, hw, UiKit.BtnH);
            if (LocGUI.Toggle(holdHomeR, hold, "Держать в казарме", GUI.skin.button) != hold) HoldFromWarMap = true;
            UiKit.Tip(holdHomeR, "Бойцы остаются в казарме: их можно продать союзникам на рынке клана " +
                "или вывести вручную («Послать...» и щелчок за краем города)");
            cy += 32f;
            cy = WarFieldSoldiers.DrawSection(this, x, cy, w);   // klan-voina-14: бойцы казармы на карте войны
        }

        return cy - y;
    }

    /// <summary>klan-voina-14: открыть снаряжение бойца (зашёл в казарму с карты войны).</summary>
    public void OpenGearFor(CitizenAgent a)
    {
        if (a == null || !soldiers.Contains(a)) return;
        equipFor = a;
        tab = 2;
    }

    // ---------- Вкладка «Снаряжение» ----------

    List<CitizenAgent> GearableSoldiers()
    {
        var list = new List<CitizenAgent>();
        foreach (var s in soldiers) if (s != null && (s.CanUseWeapon || UnitUsesShield(s) || ShowBody(s))) list.Add(s);
        return list;
    }

    float GearTabHeight()
    {
        if (equipFor != null) return EquipBodyHeight(equipFor);
        return 34f + UiKit.SectionH + ListH;
    }

    float DrawGearTab(float x, float y, float w)
    {
        if (equipFor != null) return DrawEquipBody(x, y, w);

        float cy = y;
        if (UiKit.Button(new Rect(x, cy, w, UiKit.BtnH), "Выдать лучшее всем", UiKit.Kind.Primary,
                "Каждому бойцу в казарме без оружия, щита, брони или плаща — лучшее, что есть в Оружейных (разведчикам и стрелкам — плащи, остальным — броня). Кто сейчас на карте или в походе — нажмите ещё раз, когда он вернётся в казарму"))
            AutoEquipAll();
        cy += 34f;

        cy = UiKit.Section(x, cy, w, "Снаряжение бойцов");
        var list = GearableSoldiers();
        float innerW = w - 14f;
        gearListScroll = UiKit.BeginScrollView(new Rect(x, cy, w, ListH), gearListScroll,
            new Rect(0f, 0f, innerW, Mathf.Max(ListH, list.Count * SoldierRowH)));
        if (list.Count == 0)
            LocGUI.Label(new Rect(0f, 0f, innerW, 40f), UiKit.Col(UiKit.MutedHex, "Нет бойцов, которым положено снаряжение"), UiKit.Small);

        GUIStyle row = Mid(UiKit.Small, false);
        for (int i = 0; i < list.Count; i++)
        {
            CitizenAgent s = list[i];
            float ry = i * SoldierRowH;
            string wpn = s.CanUseWeapon ? (s.EquippedWeapon != null ? s.EquippedWeapon.Name : UiKit.Col(UiKit.WarnHex, "без оружия")) : "";
            string shd = "";
            if (UnitUsesShield(s))
            {
                Shield sh = ShieldBearer.ShieldOf(s);
                shd = sh != null ? sh.Name : UiKit.Col(UiKit.WarnHex, "без щита");
            }
            string gear = wpn + (wpn != "" && shd != "" ? UiKit.Col(UiKit.MutedHex, " · ") : "") + shd;
            BodyGear bg = s.EquippedBody;
            if (bg != null) gear += (gear != "" ? UiKit.Col(UiKit.MutedHex, " · ") : "") + UiKit.Col(bg.IsArmor ? "#B8C4D0" : "#90D070", bg.Name);
            if (s.IsFieldCannon && s.CannonHasShells)
                gear += UiKit.Col(UiKit.MutedHex, " · ") + UiKit.Col("#D0A0FF", $"{ShellShort(s.CannonShellKind)} {s.CannonShellCharges}");
            UnitIcons.Draw(new Rect(0f, ry + 1f, SoldierRowH - 4f, SoldierRowH - 4f), s.ExpUnit);
            Rect lr = new Rect(SoldierRowH, ry, innerW - 80f - SoldierRowH, SoldierRowH - 2f);
            if (!CanGear(s)) gear = UiKit.Col(UiKit.WarnHex, s.IsOnExpedition ? "в походе · " : "не в казарме · ") + gear;
            LocGUI.Label(lr, $"<b>{s.DisplayName}</b> " + UiKit.Col(UiKit.MutedHex, s.CitizenName) + "  " + gear, row);
            if (UiKit.Button(new Rect(innerW - 74f, ry, 74f, SoldierRowH - 2f), "Выбрать", UiKit.Kind.Normal, "Сменить оружие, щит, броню или плащ"))
                equipFor = s;
        }
        GUI.EndScrollView();
        cy += ListH;
        return cy - y;
    }

    // Высота карточки снаряжения выбранного бойца (должна совпадать с DrawEquipBody)
    float EquipBodyHeight(CitizenAgent s)
    {
        bool canWeapon = s.CanUseWeapon;
        bool canShield = UnitUsesShield(s);
        bool canBody = ShowBody(s);
        int wn = canWeapon ? ArmoryController.Available(s.WeaponKindAllowed).Count : 0;
        int sn = canShield ? ArmoryController.AvailableShields().Count : 0;
        int bn = canBody ? ArmoryController.AvailableBodies().Count : 0;
        float h = 30f;
        if (!CanGear(s)) h += 24f;
        if (canWeapon) h += 26f;
        if (canShield) h += 26f;
        if (canBody) h += 26f;
        if (SoldierEffects.AnyFor(s)) h += UiKit.SectionH + 30f;
        h += 4f;
        if (canWeapon) h += UiKit.SectionH + Mathf.Clamp(wn * EquipRowH, EquipRowH * 2f, EquipRowH * 4f) + 6f;
        if (canShield) h += UiKit.SectionH + Mathf.Clamp(sn * EquipRowH, EquipRowH * 2f, EquipRowH * 4f) + 6f;
        if (canBody) h += UiKit.SectionH + Mathf.Clamp(bn * EquipRowH, EquipRowH * 2f, EquipRowH * 4f) + 6f;
        if (s.IsFieldCannon) h += CannonShellsH;
        return h;
    }

    // ---------- Полевая пушка: снаряды и подъёмник ----------

    const float CannonShellsH = UiKit.SectionH + 22f + 30f + 6f;   // раздел + строка + кнопки + отступ

    static string ShellShort(ModuleKind k)
    {
        return k == ModuleKind.FireShells ? "Огонь" : (k == ModuleKind.FrostShells ? "Лёд" : (k == ModuleKind.ArcaneShells ? "Чары" : "-"));
    }

    // Строка «снаряды · подъёмник» для подсказок
    static string CannonGearLine(CitizenAgent s)
    {
        string sh = s.CannonHasShells
            ? UiKit.Col("#D0A0FF", $"{TowerModules.Name(s.CannonShellKind)} Мк.{TowerModules.Roman(s.CannonShellMk)}: {s.CannonShellCharges}")
            : UiKit.Col(UiKit.MutedHex, "снарядов нет") + (s.CannonShellAuto ? "" : UiKit.Col(UiKit.MutedHex, " (авто выкл.)"));
        string lift = s.CannonLift.Has ? TowerModules.KitName(ModuleKind.CannonLift, s.CannonLift.mk) : UiKit.Col(UiKit.MutedHex, "без подъёмника");
        return "Снаряды: " + sh + UiKit.Col(UiKit.MutedHex, " · ") + lift;
    }

    float DrawCannonShells(float x, float y, float w, CitizenAgent s)
    {
        float cy = UiKit.Section(x, y, w, "Ядра: магические снаряды");
        Rect lr = new Rect(x, cy, w, 22f);
        LocGUI.Label(lr, CannonGearLine(s), Mid(UiKit.Small, false));
        UiKit.Tip(lr, s.CannonHasShells ? MagicShells.Stats(s.CannonShellKind, s.CannonShellMk)
            : "Пачки делает Магическая оружейная или продаёт рынок. С «Авто» пушка берёт пачку сама, когда она дома и не в бою.");
        cy += 22f;

        float bw = (w - 16f) / 5f;
        float bx = x;
        foreach (var k in MagicShells.Kinds)
        {
            int n = MagicShells.Packs(k);
            bool cur = s.CannonHasShells && s.CannonShellKind == k;
            string label = (s.CannonShellPref == k && s.CannonShellAuto ? "• " : "") + $"{ShellShort(k)} ({n})";
            string tip = n > 0 ? $"Зарядить: {TowerModules.Name(k)} (пачек: {n}). Прежняя пачка — в Цитадель. Этот вид пушка будет брать сама."
                               : $"{TowerModules.Name(k)}: пачек нет — их делает Магическая оружейная или продаёт рынок";
            bool atHome = CanGear(s);
            if (!atHome) tip = NotHereText(s);
            if (UiKit.IconButton(new Rect(bx, cy, bw, 28f), ItemIcons.Module(k), (cur ? UiKit.Col(UiKit.GoodHex, "✓ ") : "") + label, n > 0 && atHome, cur ? UiKit.Kind.Primary : UiKit.Kind.Normal, tip))
            {
                if (s.LoadCannonShellsOf(k))
                    WarManager.Notify($"{s.UnitName} {s.CitizenName}: заряжена — {TowerModules.KitName(k, s.CannonShellMk)}", true);
            }
            bx += bw + 4f;
        }
        Rect ar = new Rect(bx, cy, bw, 28f);
        bool auto = LocGUI.Toggle(ar, s.CannonShellAuto, s.CannonShellAuto ? "Авто ✓" : "Авто", GUI.skin.button);
        if (auto != s.CannonShellAuto) s.CannonShellAuto = auto;
        UiKit.Tip(ar, "Снарядов нет — пушка сама берёт пачку со склада (где бы ни стояла дома, не в бою)");
        bx += bw + 4f;
        if (UiKit.Button(new Rect(bx, cy, bw, 28f), "Снять", s.CannonHasShells && CanGear(s), UiKit.Kind.Normal,
                !CanGear(s) ? NotHereText(s) : s.CannonHasShells ? "Пачку — обратно в Цитадель (Авто выключится)" : "Снарядов нет"))
            s.UnloadCannonShells();
        cy += 30f + 6f;
        return cy;
    }

    // Эффекты на надетый лук / щит бойца (обновление 14): кнопки в одну строку
    float DrawSoldierEffects(float x, float y, float w, CitizenAgent s)
    {
        float cy = UiKit.Section(x, y, w, "Эффекты на снаряжение бойца");
        Weapon bow = SoldierEffects.BowOf(s);
        Shield sh = ShieldBearer.ShieldOf(s);
        var btns = new List<System.Action<Rect>>();
        if (bow != null && ArmoryController.ShowPoison(bow))
            btns.Add(r =>
            {
                string p = SoldierEffects.PoisonProblem(s, this);
                string label = bow.IsPoisoned ? $"Яд ({bow.poisonCharges})" : "Яд";
                if (UiKit.IconButton(r, "potion", label, p == "", UiKit.Kind.Normal,
                        p == "" ? $"1 зелье отравления = {Weapon.PoisonChargesPerPotion} выстрелов ядом. Житель принесёт его в казарму." : p))
                    SoldierEffects.Poison(s, this);
            });
        if (bow != null && ArmoryController.ShowSlow(bow))
            btns.Add(r =>
            {
                string p = SoldierEffects.SlowProblem(s, this);
                string label = bow.IsSlowing ? $"Замедл. ({bow.slowCharges})" : "Замедл.";
                if (UiKit.IconButton(r, "slow_potion", label, p == "", UiKit.Kind.Normal,
                        p == "" ? $"1 зелье замедления = {Weapon.SlowChargesPerPotion} выстрелов: стрелы замедляют врагов." : p))
                    SoldierEffects.Slow(s, this);
            });
        if (bow != null && ArmoryController.ShowSalute(bow))
            btns.Add(r =>
            {
                string p = SoldierEffects.SaluteProblem(s, this);
                string label = bow.IsSalute ? $"Салют ({bow.saluteCharges})" : "Салют";
                if (UiKit.IconButton(r, "firework", label, p == "", UiKit.Kind.Normal,
                        p == "" ? "Тратит 1 Фейерверочный лук из Цитадели: стрела-ракета рвётся у цели." : p))
                    SoldierEffects.Salute(s, this);
            });
        if (sh != null && ArmoryController.ShowCore(sh))
            btns.Add(r =>
            {
                string p = SoldierEffects.CoreProblem(s, this);
                string label = sh.HasChaos ? $"Хаос ({sh.chaosCharges})" : "Ядро хаоса";
                if (UiKit.IconButton(r, "chaos_core", label, p == "", UiKit.Kind.Normal,
                        p == "" ? "Щит с небольшим шансом отражает снаряды обратно. Тратится лучшее ядро хаоса из Цитадели." : p))
                    SoldierEffects.Core(s, this);
            });
        if (btns.Count > 0)
        {
            float bw = (w - 4f * (btns.Count - 1)) / btns.Count;
            for (int i = 0; i < btns.Count; i++) btns[i](new Rect(x + i * (bw + 4f), cy, bw, 28f));
        }
        return cy + 30f;
    }

    float DrawEquipBody(float x, float y, float w)
    {
        CitizenAgent s = equipFor;
        float cy = y;

        bool canWeapon = s.CanUseWeapon;
        bool canShield = UnitUsesShield(s);
        var wOpts = canWeapon ? ArmoryController.Available(s.WeaponKindAllowed) : new List<ArmoryController.StockEntry>();
        var sOpts = canShield ? ArmoryController.AvailableShields() : new List<ArmoryController.ShieldEntry>();
        float wListH = Mathf.Clamp(wOpts.Count * EquipRowH, EquipRowH * 2f, EquipRowH * 4f);
        float sListH = Mathf.Clamp(sOpts.Count * EquipRowH, EquipRowH * 2f, EquipRowH * 4f);
        Shield curShield = ShieldBearer.ShieldOf(s);
        bool canBody = ShowBody(s);
        var bOpts = canBody ? ArmoryController.AvailableBodies() : new List<ArmoryController.BodyEntry>();
        float bListH = Mathf.Clamp(bOpts.Count * EquipRowH, EquipRowH * 2f, EquipRowH * 4f);
        BodyGear curBody = s.EquippedBody;
        bool slots = ArmoryController.FreeSlotsTotal > 0;
        string noSlots = "На складах Оружейных нет места";

        // Шапка: назад к списку + кто
        bool back = UiKit.Button(new Rect(x, cy, 84f, 24f), "← Бойцы", UiKit.Kind.Ghost, "К списку бойцов");
        UnitIcons.Draw(new Rect(x + 90f, cy, 24f, 24f), s.ExpUnit);
        LocGUI.Label(new Rect(x + 118f, cy, w - 118f, 24f),
            $"<b>{s.DisplayName}</b> {s.CitizenName}" + (s.HasHorse ? UiKit.Col(UiKit.MutedHex, " (верхом)") : ""), Mid(UiKit.Body, false));
        cy += 30f;

        GUIStyle line = Mid(UiKit.Body, false);
        bool here = CanGear(s);
        string notHere = NotHereText(s);
        if (!here)
        {
            LocGUI.Label(new Rect(x, cy, w, 22f), UiKit.Col(UiKit.WarnHex, "⚠ " + (s.IsOnExpedition ? "В походе" : "Не в казарме") + ": снаряжение не выдать"), Mid(UiKit.Small, false));
            UiKit.Tip(new Rect(x, cy, w, 22f), notHere);
            cy += 24f;
        }

        // --- Что надето ---
        if (canWeapon)
        {
            Weapon cw = s.EquippedWeapon;
            Rect lr = new Rect(x, cy, w - 90f, 24f);
            LocGUI.Label(lr, UiKit.Col(UiKit.MutedHex, "Оружие: ") + (cw != null ? $"<b>{cw.Name}</b>" : UiKit.Col(UiKit.WarnHex, "нет")), line);
            if (cw != null)
            {
                UiKit.Tip(lr, cw.StatsText);
                if (UiKit.Button(new Rect(x + w - 84f, cy, 84f, 24f), "Снять", slots && here, UiKit.Kind.Normal, !here ? notHere : slots ? "Оружие — на склад Оружейной" : noSlots))
                {
                    Weapon old = s.UnequipWeapon();
                    if (old != null && !ArmoryController.Stash(old)) s.EquipWeapon(old);
                }
            }
            cy += 26f;
        }
        if (canShield)
        {
            Rect lr = new Rect(x, cy, w - 90f, 24f);
            LocGUI.Label(lr, UiKit.Col(UiKit.MutedHex, "Щит: ") + (curShield != null ? $"<b>{curShield.Name}</b>" : UiKit.Col(UiKit.WarnHex, "нет")), line);
            if (curShield != null)
            {
                UiKit.Tip(lr, curShield.StatsText);
                if (UiKit.Button(new Rect(x + w - 84f, cy, 84f, 24f), "Снять", slots && here, UiKit.Kind.Normal, !here ? notHere : slots ? "Щит — на склад Оружейной" : noSlots))
                {
                    Shield old = ShieldBearer.Unequip(s);
                    if (old != null && !ArmoryController.StashShield(old)) ShieldBearer.Equip(s, old);
                }
            }
            cy += 26f;
        }
        if (canBody)
        {
            Rect lr = new Rect(x, cy, w - 90f, 24f);
            string bodyName = curBody != null ? UiKit.Col(curBody.IsArmor ? "#B8C4D0" : "#90D070", $"<b>{curBody.Name}</b>") + UiKit.Col(UiKit.MutedHex, $" {curBody.Percent}%") : UiKit.Col(UiKit.WarnHex, "нет");
            LocGUI.Label(lr, UiKit.Col(UiKit.MutedHex, "На теле: ") + bodyName, line);
            UiKit.Tip(lr, curBody != null ? curBody.StatsText : "Броня (Тяжёлая стратегия) или плащ из листвы (Мастер маскировки) из Оружейной. Только одно из двух.");
            if (curBody != null && UiKit.Button(new Rect(x + w - 84f, cy, 84f, 24f), "Снять", slots && here, UiKit.Kind.Normal, !here ? notHere : slots ? "На склад Оружейной" : noSlots))
            {
                BodyGear old = s.UnequipBody();
                if (old != null && !ArmoryController.StashBody(old)) s.EquipBody(old);
            }
            cy += 26f;
        }
        // --- Эффекты на надетое (обновление 14): только вооружённому бойцу в казарме ---
        if (SoldierEffects.AnyFor(s)) cy = DrawSoldierEffects(x, cy, w, s);
        cy += 4f;

        GUIStyle itemSt = Mid(UiKit.Small, false);
        float innerW = w - 14f;

        // --- Оружие на складах ---
        if (canWeapon)
        {
            string kindName = s.WeaponKindAllowed == WeaponKind.Bow ? "луки" : (s.WeaponKindAllowed == WeaponKind.Shot ? "бронебойные ядра" : "мечи");
            cy = UiKit.Section(x, cy, w, $"В оружейных: {kindName}");
            equipScroll = UiKit.BeginScrollView(new Rect(x, cy, w, wListH), equipScroll,
                new Rect(0f, 0f, innerW, Mathf.Max(wListH, wOpts.Count * EquipRowH)));

            if (wOpts.Count == 0)
            {
                Rect er = new Rect(0f, 0f, innerW, 40f);
                LocGUI.Label(er, UiKit.Col(UiKit.MutedHex, "Нет подходящего оружия"), UiKit.Small);
                UiKit.Tip(er, "Постройте Оружейную и закажите ковку.");
            }

            for (int i = 0; i < wOpts.Count; i++)
            {
                var o = wOpts[i];
                float ry = i * EquipRowH;
                ItemIcons.Draw(new Rect(0f, ry + 1f, EquipRowH - 4f, EquipRowH - 4f), ItemIcons.Of(o.weapon), (int)o.weapon.quality);
                LocGUI.Label(new Rect(EquipRowH, ry, innerW - 70f - EquipRowH, EquipRowH - 2f),
                    $"<b>{o.weapon.Name}</b>  " + UiKit.Col(UiKit.MutedHex, $"×{o.weapon.DamageMul:F2} · {o.weapon.Percent}%"), itemSt);
                if (UiKit.Button(new Rect(innerW - 64f, ry, 64f, EquipRowH - 4f), "Взять", here || o.weapon.kind == WeaponKind.Shot, UiKit.Kind.Primary, here ? o.weapon.StatsText : notHere))
                {
                    EquipFrom(s, o.armory, o.weapon);
                    break;   // список изменился - перерисуем в следующем кадре
                }
            }
            GUI.EndScrollView();
            cy += wListH + 6f;
        }

        // --- Щиты на складах ---
        if (canShield)
        {
            cy = UiKit.Section(x, cy, w, "В оружейных: щиты");
            shieldScroll = UiKit.BeginScrollView(new Rect(x, cy, w, sListH), shieldScroll,
                new Rect(0f, 0f, innerW, Mathf.Max(sListH, sOpts.Count * EquipRowH)));

            if (sOpts.Count == 0)
            {
                Rect er = new Rect(0f, 0f, innerW, 40f);
                LocGUI.Label(er, UiKit.Col(UiKit.MutedHex, "Нет щитов"), UiKit.Small);
                UiKit.Tip(er, "Закажите щиты в Оружейной.");
            }

            for (int i = 0; i < sOpts.Count; i++)
            {
                var o = sOpts[i];
                float ry = i * EquipRowH;
                ItemIcons.Draw(new Rect(0f, ry + 1f, EquipRowH - 4f, EquipRowH - 4f), ItemIcons.Of(o.shield));
                LocGUI.Label(new Rect(EquipRowH, ry, innerW - 70f - EquipRowH, EquipRowH - 2f),
                    $"<b>{o.shield.Name}</b>  " + UiKit.Col(UiKit.MutedHex, $"блок {o.shield.BlockChance * 100f:F0}% · {o.shield.Percent}%"), itemSt);
                string tip = $"Блок {o.shield.BlockChance * 100f:F0}%, урон −{o.shield.ReduceFraction * 100f:F0}%, отражение {o.shield.ReflectChance * 100f:F1}%, состояние {o.shield.Percent}%";
                if (UiKit.Button(new Rect(innerW - 64f, ry, 64f, EquipRowH - 4f), "Взять", here, UiKit.Kind.Primary, here ? tip : notHere))
                {
                    EquipShieldFrom(s, o.armory, o.shield);
                    break;
                }
            }
            GUI.EndScrollView();
            cy += sListH + 6f;
        }

        // --- Броня и плащи на складах ---
        if (canBody)
        {
            cy = UiKit.Section(x, cy, w, "В оружейных: броня и плащи");
            bodyScroll = UiKit.BeginScrollView(new Rect(x, cy, w, bListH), bodyScroll,
                new Rect(0f, 0f, innerW, Mathf.Max(bListH, bOpts.Count * EquipRowH)));

            if (bOpts.Count == 0)
            {
                Rect er = new Rect(0f, 0f, innerW, 40f);
                LocGUI.Label(er, UiKit.Col(UiKit.MutedHex, "Нет брони и плащей"), UiKit.Small);
                UiKit.Tip(er, DevelopmentPath.IsHeavy ? "Закажите броню в Оружейной: сталь и доски."
                    : (DevelopmentPath.IsCamo ? "Закажите плащ в Оружейной: листва (Лесопилка Ур.4) и немного золота." : "Броню и плащи делают ветки Военного дела."));
            }

            for (int i = 0; i < bOpts.Count; i++)
            {
                var o = bOpts[i];
                float ry = i * EquipRowH;
                string col = o.gear.IsArmor ? "#B8C4D0" : "#90D070";
                string brief = o.gear.IsArmor ? $"−{o.gear.Reduce * 100f:F0}% урона" : "скрытность";
                LocGUI.Label(new Rect(0f, ry, innerW - 70f, EquipRowH - 2f),
                    UiKit.Col(col, $"<b>{o.gear.Name}</b>") + "  " + UiKit.Col(UiKit.MutedHex, $"{brief} · {o.gear.Percent}%"), itemSt);
                if (UiKit.Button(new Rect(innerW - 64f, ry, 64f, EquipRowH - 4f), "Надеть", here, UiKit.Kind.Primary, here ? o.gear.StatsText : notHere))
                {
                    EquipBodyFrom(s, o.armory, o.gear);
                    break;
                }
            }
            GUI.EndScrollView();
            cy += bListH + 6f;
        }

        if (s.IsFieldCannon) cy = DrawCannonShells(x, cy, w, s);

        if (back) equipFor = null;
        return cy - y;
    }

    // ---------- Панель ----------

    // Шапка: строка обучения + строка мест (+ строка «не работает»)
    float HeaderHeight()
    {
        float h = UiKit.RowH + UiKit.RowH;
        if (Bld != null && !Bld.isWorking) h += UiKit.RowH;
        return h + 4f;
    }

    float TabHeight(int t)
    {
        if (t == 1) return ArmyTabHeight();
        if (t == 2) return GearTabHeight();
        return TrainTabHeight();
    }

    /// <summary>Рисует панель казармы. Вызывается из BuildingPlacer.OnGUI. Возвращает занятый прямоугольник.</summary>
    public Rect DrawPanel(float x, float y, float w)
    {
        soldiers.RemoveAll(s => s == null);
        selection.RemoveWhere(s => s == null);
        if (equipFor != null && !soldiers.Contains(equipFor)) equipFor = null;

        const float tabsH = 26f;
        float contentH = HeaderHeight() + tabsH + 8f + TabHeight(tab);
        Rect panel = new Rect(x, y, w, UiKit.WindowH(contentH));

        int lvl = Bld != null ? Bld.level : 1;
        Rect c = UiKit.Window(panel, $"{Title} · Ур. {lvl}");
        float cx = c.x, cw = c.width, cy = c.y;

        // --- Что делает ---
        GUIStyle body = Mid(UiKit.Body, false);
        bool showBar = false;
        string training;
        if (!IsTraining) training = UiKit.Col(UiKit.MutedHex, "Никого не обучает");
        else if (!RecruitInside) training = $"<b>{CurrentTrainingName}</b> " + UiKit.Col(UiKit.MutedHex, "— новобранец идёт");
        else if (pottagePending) training = $"<b>{CurrentTrainingName}</b> " + UiKit.Col(UiKit.WarnHex, "— ждёт похлёбку");
        else if (potionsPending) training = $"<b>{CurrentTrainingName}</b> " + UiKit.Col(UiKit.WarnHex, "— ждёт зелье");
        else { training = $"Обучение: <b>{CurrentTrainingName}</b>"; showBar = true; }
        LocGUI.Label(new Rect(cx, cy, showBar ? cw - 150f : cw, UiKit.RowH), training, body);
        if (showBar)
            UiKit.Bar(new Rect(cx + cw - 140f, cy + 4f, 140f, 14f), TrainingProgress, UiKit.Good, $"{TrainingProgress * 100f:F0}%");
        cy += UiKit.RowH;

        // --- Места ---
        Rect placesR = new Rect(cx, cy, cw, UiKit.RowH);
        int fieldSoldiers = WarFieldSoldiers.CountOf(this);   // klan-voina-14
        float freeFrac = MaxSpace > 0 ? (float)CurrentOccupiedSpace / MaxSpace : 0f;
        string places = UiKit.Col(freeFrac >= 1f ? UiKit.WarnHex : UiKit.GoodHex, $"{CurrentOccupiedSpace}/{MaxSpace}");
        LocGUI.Label(placesR, UiKit.Col(UiKit.MutedHex, "Места: ") + places
            + UiKit.Col(UiKit.MutedHex, "   Бойцов: ") + soldiers.Count + (fieldSoldiers > 0 ? UiKit.Col(UiKit.MutedHex, " + на карте ") + fieldSoldiers : "")
            + UiKit.Col(UiKit.MutedHex, "   Отрядов: ") + squads.Count, body);
        UiKit.Tip(placesR, "Состав:\n" + TypeSummary() + $"\n\nМест: {maxGarrisonSpace} за каждый уровень казармы.");
        cy += UiKit.RowH;

        if (Bld != null && !Bld.isWorking)
        {
            LocGUI.Label(new Rect(cx, cy, cw, UiKit.RowH), UiKit.Col(UiKit.BadHex, Bld.StatusText), body);
            cy += UiKit.RowH;
        }
        cy += 4f;

        // --- Вкладки ---
        int prevTab = tab;
        int fieldN = WarFieldSoldiers.CountOf(this);   // klan-voina-14: и те, кто сейчас на карте войны
        int newTab = UiKit.Tabs(new Rect(cx, cy, cw, tabsH), new[] { "Обучение", fieldN > 0 ? $"Войска ({soldiers.Count}+{fieldN})" : $"Войска ({soldiers.Count})", "Снаряжение" }, tab);
        cy += tabsH + 8f;

        if (tab == 1) DrawArmyTab(cx, cy, cw);
        else if (tab == 2) DrawGearTab(cx, cy, cw);
        else DrawTrainTab(cx, cy, cw);

        // Вкладку меняем после отрисовки - чтобы высота панели в этом кадре совпала с содержимым
        if (newTab != prevTab)
        {
            if (newTab == 2) equipFor = null;   // по вкладке - сначала список бойцов
            tab = newTab;
        }

        return panel;
    }
}
